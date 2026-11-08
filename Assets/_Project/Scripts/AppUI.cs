using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>The app's screens. Each one is a "screen-..." element in MainUI.uxml.</summary>
public enum AppScreen
{
    Mode,     // the first screen: choose how to play
    City,     // In Iasi: the targets to pick from
    Guide,    // In Iasi: walking to the picked target, camera off
    CityAR,   // In Iasi: at the target, camera on (locating, then the academy or a piece)
    Starting, // permissions and ARCore checks
    Blocked,  // AR can't run (or a permission was revoked)
    Scan,     // scanning and placing: hint + "Place here"
    Adjust,   // the model is placed: adjust panel (move / turn / size / height), "Place again", "Done"
    Explore,  // the model is placed: the arch pieces (spawn, aim, collect), "Edit"
}

/// <summary>Buttons of the adjust panel. The float that comes with them is noted per command.</summary>
public enum AdjustCommand
{
    NudgeLeft, NudgeRight, NudgeAway, NudgeCloser,
    Rotate,          // value: degrees, positive = clockwise seen from above
    FaceSouth,
    Scale,           // value: the new scale (from the slider)
    DefaultSize, MaxSize, TogglePlatform,
    Lower, Raise, StreetLevel, BaseOnGround,
}

/// <summary>
/// The only script that talks to the UI Toolkit layout (MainUI.uxml).
/// Other scripts never touch UI elements; they call these methods, and listen to the button events.
///
/// Setup: put this on the same GameObject as the UIDocument (Source Asset = MainUI.uxml).
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class AppUI : MonoBehaviour
{
    /// <summary>A way to play was chosen on the mode screen.</summary>
    public event Action<GameMode> ModeChosen;

    /// <summary>In Iasi, the target list: "Modes", a target picked (index in CityTargets), "Start over".</summary>
    public event Action CityBackClicked;
    public event Action<int> TargetPicked;
    public event Action CityResetClicked;

    /// <summary>In Iasi, the guide: "Targets" and "Open the camera".</summary>
    public event Action GuideBackClicked;
    public event Action OpenCameraClicked;

    /// <summary>In Iasi, at the target: "Targets", "Try again" (the anchor failed), "Collect".</summary>
    public event Action CityARBackClicked;
    public event Action CityARRetryClicked;
    public event Action CityCollectClicked;

    /// <summary>"Try again", "Open settings", "Close app" on the blocked screen.</summary>
    public event Action RetryClicked;
    public event Action OpenSettingsClicked;
    public event Action CloseClicked;

    /// <summary>"Place here" on the scan screen.</summary>
    public event Action PlaceClicked;

    /// <summary>"Place again" on the adjust screen.</summary>
    public event Action PlaceAgainClicked;

    /// <summary>A button (or the size slider) of the adjust panel. See AdjustCommand for the value.</summary>
    public event Action<AdjustCommand, float> AdjustCommandIssued;

    /// <summary>"Done", "See-through" and "Cancel" (instead of "Place again" when editing).</summary>
    public event Action DoneClicked;
    public event Action SeeThroughClicked;
    public event Action CancelEditClicked;

    /// <summary>Buttons of the explore screen.</summary>
    public event Action EditClicked;
    public event Action SpawnPieceClicked;
    public event Action CollectPieceClicked;
    public event Action ResetPiecesClicked;
    public event Action OcclusionClicked;

    /// <summary>"Modes" (scan or explore screen): asks for confirmation first.</summary>
    public event Action LeaveModeClicked;

    /// <summary>The answer on the "Back to the modes?" card.</summary>
    public event Action<bool> LeaveModeAnswered;

    public AppScreen CurrentScreen { get; private set; } = AppScreen.Mode;

    UIDocument document;
    VisualElement root;
    VisualElement safeArea;
    VisualElement backdrop;
    readonly Dictionary<AppScreen, VisualElement> screens = new Dictionary<AppScreen, VisualElement>();

    Label startingDetail;
    Label blockedTitle;
    Label blockedMessage;
    Button retryButton;
    Button settingsButton;
    Button closeButton;
    Label scanHint;
    Button placeButton;
    Label debugOverlay;
    string debugText;

    // Mode screen
    ArchEmblem freeModeArch;
    Label freeModeCount;
    ArchEmblem cityModeArch;
    Label cityModeCount;

    // In Iasi
    public const int CityTargetSlots = 5;
    ArchEmblem cityArch;
    Label cityCount;
    readonly Button[] targetButtons = new Button[CityTargetSlots];
    readonly Label[] targetNames = new Label[CityTargetSlots];
    readonly Label[] targetStatuses = new Label[CityTargetSlots];
    Button cityResetButton;
    Label guideName;
    Label guideStage;
    GuideDial guideDial;
    Label guideBig;
    Label guideSmall;
    Label guideAccuracy;
    Button openCameraButton;
    Label cityARHint;
    Button cityARBackButton;
    Button cityARRetryButton;
    Button cityCollectButton;

    // Adjust panel
    Label adjustHint;
    Label readoutSize;
    Label readoutFront;
    Label readoutHeight;
    Slider scaleSlider;
    Button faceSouthButton;
    Button platformButton;
    Button seeThroughButton;
    Button placeAgainButton;
    Button cancelEditButton;
    readonly List<(Button tab, VisualElement tool)> adjustTabs = new List<(Button, VisualElement)>();
    int selectedTab;

    // Explore
    Label exploreHint;
    ArchEmblem exploreArch;
    Label piecesCount;
    Button spawnPieceButton;
    Button resetPiecesButton;
    Button collectPieceButton;
    Button occlusionButton;
    VisualElement leaveConfirm;

    // Every button wired in OnEnable, so OnDisable can unwire them.
    readonly List<(Button button, Action handler)> wiredButtons = new List<(Button, Action)>();
    readonly List<RepeatButton> repeatButtons = new List<RepeatButton>();

    const long RepeatDelayMs = 350;
    const long RepeatIntervalMs = 60;

    Rect appliedSafeArea;
    Vector2Int appliedScreenSize;

    // Lifecycle
    void OnEnable()
    {
        // UIDocument (re)builds its tree in its own OnEnable, which runs before this one.
        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore; // empty areas let touches through to the AR view

        safeArea = Find<VisualElement>("safe-area");
        backdrop = Find<VisualElement>("backdrop");
        screens[AppScreen.Mode] = Find<VisualElement>("screen-mode");
        screens[AppScreen.City] = Find<VisualElement>("screen-city");
        screens[AppScreen.Guide] = Find<VisualElement>("screen-guide");
        screens[AppScreen.CityAR] = Find<VisualElement>("screen-city-ar");
        screens[AppScreen.Starting] = Find<VisualElement>("screen-starting");
        screens[AppScreen.Blocked] = Find<VisualElement>("screen-blocked");
        screens[AppScreen.Scan] = Find<VisualElement>("screen-scan");
        screens[AppScreen.Adjust] = Find<VisualElement>("screen-adjust");
        screens[AppScreen.Explore] = Find<VisualElement>("screen-explore");
        debugOverlay = Find<Label>("debug-overlay");

        SetUpStartScreens();
        SetUpCity();
        SetUpScan();
        SetUpAdjustPanel();
        SetUpExplore();
        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

        appliedSafeArea = default;
        ShowScreen(CurrentScreen);
    }

    void OnDisable()
    {
        foreach (var (button, handler) in wiredButtons) button.clicked -= handler;
        wiredButtons.Clear();
        foreach (var repeat in repeatButtons) repeat.SetAction(null, RepeatDelayMs, RepeatIntervalMs);
        repeatButtons.Clear();
        scaleSlider?.UnregisterValueChangedCallback(OnScaleSliderChanged);
        adjustTabs.Clear();
        root?.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
    }

    void Update()
    {
        // A split screen or a changed cut-out changes the safe area.
        if (Screen.safeArea != appliedSafeArea ||
            Screen.width != appliedScreenSize.x || Screen.height != appliedScreenSize.y)
            ApplySafeArea();
    }

    // Screens
    public void ShowScreen(AppScreen screen)
    {
        CurrentScreen = screen;
        foreach (var pair in screens)
            SetVisible(pair.Value, pair.Key == screen);
        // The menu screens hide the camera view completely (it may still be running behind them).
        SetVisible(backdrop, !IsCameraScreen(screen));
        UpdateDebugOverlay();
    }

    // Start screens
    /// <summary>The free placement mode's progress on the mode screen: which pieces are collected.</summary>
    public void SetFreeModeProgress(Func<int, bool> isCollected, int collected, int total)
    {
        SetArch(freeModeArch, isCollected);
        if (freeModeCount != null) freeModeCount.text = $"{collected} of {total} arch pieces";
    }

    /// <summary>The in-city mode's progress on the mode screen.</summary>
    public void SetCityModeProgress(Func<int, bool> isCollected, int collected, int total)
    {
        SetArch(cityModeArch, isCollected);
        if (cityModeCount != null) cityModeCount.text = $"{collected} of {total} arch pieces";
    }

    // In Iasi
    /// <summary>
    /// The target list: the arch's progress, and per row its name, status line and whether it can be
    /// picked (a found piece can't). "Start over" shows once every piece is found.
    /// </summary>
    public void SetCityTargets(Func<int, bool> isCollected, int collected, int total, string[] names,
                               string[] statuses, bool[] canPick, bool showReset)
    {
        SetArch(cityArch, isCollected);
        if (cityCount != null) cityCount.text = $"{collected} of {total} pieces found";
        for (int i = 0; i < CityTargetSlots; i++)
        {
            bool used = names != null && i < names.Length;
            SetVisible(targetButtons[i], used);
            if (!used) continue;
            if (targetNames[i] != null) targetNames[i].text = names[i] ?? "";
            if (targetStatuses[i] != null) targetStatuses[i].text = statuses != null ? statuses[i] ?? "" : "";
            targetButtons[i]?.SetEnabled(canPick == null || canPick[i]);
        }
        SetVisible(cityResetButton, showReset);
    }

    /// <summary>
    /// The guide screen. big: the distance or the warmer / colder word (trend: -1 colder, 1 warmer, 0
    /// neither, which colours it); small: the line under it.
    /// </summary>
    public void SetGuide(string name, string stage, string big, string small, int trend, string accuracy,
                         GuideDial.Look look, bool pointer, float angle, bool canOpenCamera)
    {
        if (guideName != null) guideName.text = name ?? "";
        if (guideStage != null) guideStage.text = stage ?? "";
        if (guideBig != null) guideBig.text = big ?? "";
        guideBig?.EnableInClassList("dial__big--warmer", trend > 0);
        guideBig?.EnableInClassList("dial__big--colder", trend < 0);
        SetHint(guideSmall, small);
        if (guideAccuracy != null) guideAccuracy.text = accuracy ?? "";
        var tint = trend < 0 ? new Color32(127, 163, 146, 255) : new Color32(201, 154, 62, 255);
        guideDial?.Set(look, pointer, angle, tint);
        openCameraButton?.SetEnabled(canOpenCamera);
    }

    /// <summary>The in-city camera screen: the hint, and which of Collect / Try again show.</summary>
    public void SetCityAR(string hint, bool inSight, bool showCollect, bool canCollect, bool showRetry)
    {
        SetHint(cityARHint, hint);
        cityARHint?.EnableInClassList("hint--in-sight", inSight);
        SetVisible(cityCollectButton, showCollect);
        cityCollectButton?.SetEnabled(canCollect);
        SetVisible(cityARRetryButton, showRetry);
        // Alone (the academy, or a piece just collected), "Targets" takes the whole row.
        cityARBackButton?.EnableInClassList("btn--narrow", showCollect || showRetry);
    }

    public void SetStartingDetail(string text)
    {
        if (startingDetail == null) return;
        startingDetail.text = text ?? "";
        SetVisible(startingDetail, !string.IsNullOrEmpty(text));
    }

    public void SetBlocked(string title, string message, bool showRetry, bool showSettings, bool showClose)
    {
        if (blockedTitle != null) blockedTitle.text = title ?? "";
        if (blockedMessage != null) blockedMessage.text = message ?? "";
        SetVisible(retryButton, showRetry);
        SetVisible(settingsButton, showSettings);
        SetVisible(closeButton, showClose);
    }

    // Scan
    public void SetScanHint(string text) => SetHint(scanHint, text);

    /// <summary>"Place here" is shown on the scan screen, but only enabled with ground under the reticle.</summary>
    public void SetPlaceEnabled(bool enabled) => placeButton?.SetEnabled(enabled);

    // Adjust panel
    public void SetAdjustHint(string text) => SetHint(adjustHint, text);

    /// <summary>The live values at the top of the adjust panel.</summary>
    public void SetAdjustReadout(string size, string front, string height)
    {
        if (readoutSize != null) readoutSize.text = size ?? "";
        if (readoutFront != null) readoutFront.text = front ?? "";
        if (readoutHeight != null) readoutHeight.text = height ?? "";
    }

    /// <summary>Range of the size slider. The slider is logarithmic: equal steps multiply the size.</summary>
    public void SetScaleRange(float min, float max)
    {
        if (scaleSlider == null) return;
        scaleSlider.lowValue = Mathf.Log10(Mathf.Max(0.001f, min));
        scaleSlider.highValue = Mathf.Log10(Mathf.Max(min, max));
    }

    /// <summary>Moves the size slider without raising a command.</summary>
    public void SetScaleValue(float scale)
    {
        scaleSlider?.SetValueWithoutNotify(Mathf.Log10(Mathf.Max(0.001f, scale)));
    }

    public void SetFaceSouthEnabled(bool enabled) => faceSouthButton?.SetEnabled(enabled);

    public void SetPlatformButtonText(string text)
    {
        if (platformButton != null) platformButton.text = text ?? "";
    }

    /// <summary>Shows the "See-through" button as on (gilt) or off.</summary>
    public void SetSeeThroughOn(bool on) => seeThroughButton?.EnableInClassList("btn--toggle-on", on);

    /// <summary>Editing a confirmed placement: "Cancel" instead of "Place again".</summary>
    public void SetAdjustEditing(bool editing)
    {
        SetVisible(cancelEditButton, editing);
        SetVisible(placeAgainButton, !editing);
    }

    // Explore
    /// <summary>
    /// The explore screen's arch pieces: which are collected, the count, the hint, and which buttons can be
    /// used. When all are collected, "Start over" replaces "Spawn piece".
    /// </summary>
    public void SetPieces(Func<int, bool> isCollected, int collected, int total, string hint, bool inSight,
                          bool canSpawn, bool canCollect, bool complete)
    {
        SetArch(exploreArch, isCollected);
        if (piecesCount != null) piecesCount.text = $"{collected} of {total} pieces found";
        SetHint(exploreHint, hint);
        exploreHint?.EnableInClassList("hint--in-sight", inSight);
        SetVisible(spawnPieceButton, !complete);
        SetVisible(resetPiecesButton, complete);
        spawnPieceButton?.SetEnabled(canSpawn);
        collectPieceButton?.SetEnabled(canCollect);
    }

    /// <summary>The "Hidden by real objects" option: its text, and whether it can be used.</summary>
    public void SetOcclusionButton(string text, bool enabled)
    {
        if (occlusionButton == null) return;
        occlusionButton.text = text ?? "";
        occlusionButton.SetEnabled(enabled);
    }

    /// <summary>Shows the "Back to the modes?" card over the current screen.</summary>
    public void SetLeaveConfirmVisible(bool visible) => SetVisible(leaveConfirm, visible);

    // Developer overlay
    /// <summary>
    /// The overlay text (rich text allowed). Null or empty hides it. It is only shown over the camera
    /// screens, not over the mode screen or the start cards.
    /// </summary>
    public void SetDebugText(string text)
    {
        debugText = text;
        UpdateDebugOverlay();
    }

    void UpdateDebugOverlay()
    {
        if (debugOverlay == null) return;
        bool show = IsCameraScreen(CurrentScreen) && !string.IsNullOrEmpty(debugText);
        if (show) debugOverlay.text = debugText;
        SetVisible(debugOverlay, show);
    }

    // Touch helpers
    /// <summary>
    /// True if a screen position (Input System coordinates: origin bottom-left, pixels) is over
    /// an interactive part of the UI, e.g. a panel or button. Used to ignore AR gestures there.
    /// </summary>
    public bool IsOverUI(Vector2 screenPosition)
    {
        var panel = root?.panel;
        if (panel == null) return false;
        var panelPosition = RuntimePanelUtils.ScreenToPanel(panel,
            new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        var picked = panel.Pick(panelPosition);
        return picked != null && picked != root;
    }

    // Internals
    void SetUpStartScreens()
    {
        freeModeArch = Find<ArchEmblem>("mode-free-arch");
        freeModeCount = Find<Label>("mode-free-count");
        cityModeArch = Find<ArchEmblem>("mode-city-arch");
        cityModeCount = Find<Label>("mode-city-count");
        Wire(Find<Button>("btn-mode-city"), () => ModeChosen?.Invoke(GameMode.InCity));
        Wire(Find<Button>("btn-mode-free"), () => ModeChosen?.Invoke(GameMode.Free));

        startingDetail = Find<Label>("starting-detail");
        blockedTitle = Find<Label>("blocked-title");
        blockedMessage = Find<Label>("blocked-message");
        retryButton = Find<Button>("btn-retry");
        settingsButton = Find<Button>("btn-settings");
        closeButton = Find<Button>("btn-close");
        Wire(retryButton, () => RetryClicked?.Invoke());
        Wire(settingsButton, () => OpenSettingsClicked?.Invoke());
        Wire(closeButton, () => CloseClicked?.Invoke());
    }

    void SetUpCity()
    {
        cityArch = Find<ArchEmblem>("city-arch");
        cityCount = Find<Label>("city-count");
        for (int i = 0; i < CityTargetSlots; i++)
        {
            int index = i;
            targetButtons[i] = Find<Button>("btn-target-" + i);
            targetNames[i] = Find<Label>("target-name-" + i);
            targetStatuses[i] = Find<Label>("target-status-" + i);
            Wire(targetButtons[i], () => TargetPicked?.Invoke(index));
        }
        cityResetButton = Find<Button>("btn-city-reset");
        Wire(cityResetButton, () => CityResetClicked?.Invoke());
        Wire(Find<Button>("btn-city-back"), () => CityBackClicked?.Invoke());

        guideName = Find<Label>("guide-name");
        guideStage = Find<Label>("guide-stage");
        guideDial = Find<GuideDial>("guide-dial");
        guideBig = Find<Label>("guide-big");
        guideSmall = Find<Label>("guide-small");
        guideAccuracy = Find<Label>("guide-accuracy");
        openCameraButton = Find<Button>("btn-open-camera");
        Wire(openCameraButton, () => OpenCameraClicked?.Invoke());
        Wire(Find<Button>("btn-guide-back"), () => GuideBackClicked?.Invoke());

        cityARHint = Find<Label>("city-ar-hint");
        cityARRetryButton = Find<Button>("btn-city-ar-retry");
        cityCollectButton = Find<Button>("btn-city-collect");
        cityARBackButton = Find<Button>("btn-city-ar-back");
        Wire(cityARBackButton, () => CityARBackClicked?.Invoke());
        Wire(cityARRetryButton, () => CityARRetryClicked?.Invoke());
        Wire(cityCollectButton, () => CityCollectClicked?.Invoke());
    }

    void SetUpScan()
    {
        scanHint = Find<Label>("scan-hint");
        placeButton = Find<Button>("btn-place");
        Wire(placeButton, () => PlaceClicked?.Invoke());
        Wire(Find<Button>("btn-leave-from-scan"), () => LeaveModeClicked?.Invoke());
    }

    void SetUpAdjustPanel()
    {
        adjustHint = Find<Label>("adjust-hint");
        readoutSize = Find<Label>("readout-size");
        readoutFront = Find<Label>("readout-front");
        readoutHeight = Find<Label>("readout-height");
        scaleSlider = Find<Slider>("scale-slider");
        faceSouthButton = Find<Button>("btn-face-south");
        platformButton = Find<Button>("btn-platform");

        // Tabs: each shows its own row of tools.
        string[] tabNames = { "move", "rotate", "size", "height" };
        for (int i = 0; i < tabNames.Length; i++)
        {
            var tab = Find<Button>("tab-" + tabNames[i]);
            var tool = Find<VisualElement>("tool-" + tabNames[i]);
            if (tab == null || tool == null) continue;
            int index = adjustTabs.Count;
            adjustTabs.Add((tab, tool));
            Wire(tab, () => SelectAdjustTab(index));
        }
        SelectAdjustTab(selectedTab);

        // Held buttons repeat, so a long press moves / turns / lifts smoothly.
        WireRepeat("btn-nudge-left", AdjustCommand.NudgeLeft);
        WireRepeat("btn-nudge-away", AdjustCommand.NudgeAway);
        WireRepeat("btn-nudge-closer", AdjustCommand.NudgeCloser);
        WireRepeat("btn-nudge-right", AdjustCommand.NudgeRight);
        WireRepeat("btn-rotate-left-1", AdjustCommand.Rotate, -1f);
        WireRepeat("btn-rotate-right-1", AdjustCommand.Rotate, 1f);
        WireRepeat("btn-lower", AdjustCommand.Lower);
        WireRepeat("btn-raise", AdjustCommand.Raise);

        Wire(Find<Button>("btn-rotate-left-10"), () => Issue(AdjustCommand.Rotate, -10f));
        Wire(Find<Button>("btn-rotate-right-10"), () => Issue(AdjustCommand.Rotate, 10f));
        Wire(faceSouthButton, () => Issue(AdjustCommand.FaceSouth));
        Wire(Find<Button>("btn-default-size"), () => Issue(AdjustCommand.DefaultSize));
        Wire(Find<Button>("btn-max-size"), () => Issue(AdjustCommand.MaxSize));
        Wire(platformButton, () => Issue(AdjustCommand.TogglePlatform));
        Wire(Find<Button>("btn-street-level"), () => Issue(AdjustCommand.StreetLevel));
        Wire(Find<Button>("btn-base-on-ground"), () => Issue(AdjustCommand.BaseOnGround));
        Wire(Find<Button>("btn-done"), () => DoneClicked?.Invoke());
        seeThroughButton = Find<Button>("btn-see-through");
        Wire(seeThroughButton, () => SeeThroughClicked?.Invoke());
        placeAgainButton = Find<Button>("btn-place-again");
        Wire(placeAgainButton, () => PlaceAgainClicked?.Invoke());
        cancelEditButton = Find<Button>("btn-cancel-edit");
        Wire(cancelEditButton, () => CancelEditClicked?.Invoke());

        scaleSlider?.RegisterValueChangedCallback(OnScaleSliderChanged);
    }

    void SetUpExplore()
    {
        exploreHint = Find<Label>("explore-hint");
        exploreArch = Find<ArchEmblem>("explore-arch");
        piecesCount = Find<Label>("pieces-count");
        spawnPieceButton = Find<Button>("btn-spawn-piece");
        resetPiecesButton = Find<Button>("btn-reset-pieces");
        collectPieceButton = Find<Button>("btn-collect-piece");
        occlusionButton = Find<Button>("btn-occlusion");
        leaveConfirm = Find<VisualElement>("confirm-leave");
        Wire(Find<Button>("btn-edit"), () => EditClicked?.Invoke());
        Wire(spawnPieceButton, () => SpawnPieceClicked?.Invoke());
        Wire(resetPiecesButton, () => ResetPiecesClicked?.Invoke());
        Wire(collectPieceButton, () => CollectPieceClicked?.Invoke());
        Wire(occlusionButton, () => OcclusionClicked?.Invoke());
        Wire(Find<Button>("btn-leave-from-explore"), () => LeaveModeClicked?.Invoke());
        Wire(Find<Button>("btn-leave-confirm"), () => LeaveModeAnswered?.Invoke(true));
        Wire(Find<Button>("btn-leave-cancel"), () => LeaveModeAnswered?.Invoke(false));
        SetLeaveConfirmVisible(false);
    }

    void SelectAdjustTab(int index)
    {
        selectedTab = Mathf.Clamp(index, 0, Mathf.Max(0, adjustTabs.Count - 1));
        for (int i = 0; i < adjustTabs.Count; i++)
        {
            adjustTabs[i].tab.EnableInClassList("tab--selected", i == selectedTab);
            SetVisible(adjustTabs[i].tool, i == selectedTab);
        }
    }

    static void SetArch(ArchEmblem arch, Func<int, bool> isCollected) => arch?.SetCollected(isCollected);

    static bool IsCameraScreen(AppScreen screen) =>
        screen == AppScreen.Scan || screen == AppScreen.Adjust || screen == AppScreen.Explore ||
        screen == AppScreen.CityAR;

    static void SetHint(Label hint, string text)
    {
        if (hint == null) return;
        hint.text = text ?? "";
        SetVisible(hint, !string.IsNullOrEmpty(text));
    }

    void OnScaleSliderChanged(ChangeEvent<float> e) => Issue(AdjustCommand.Scale, Mathf.Pow(10f, e.newValue));

    void Issue(AdjustCommand command, float value = 0f) => AdjustCommandIssued?.Invoke(command, value);

    void Wire(Button button, Action handler)
    {
        if (button == null) return;
        button.clicked += handler;
        wiredButtons.Add((button, handler));
    }

    void WireRepeat(string elementName, AdjustCommand command, float value = 0f)
    {
        var button = Find<RepeatButton>(elementName);
        if (button == null) return;
        button.SetAction(() => Issue(command, value), RepeatDelayMs, RepeatIntervalMs);
        repeatButtons.Add(button);
    }

    void OnRootGeometryChanged(GeometryChangedEvent _) => ApplySafeArea();

    /// Insets the safe-area container so nothing sits under the notch, camera hole or gesture bar.
    void ApplySafeArea()
    {
        var panel = root?.panel;
        if (panel == null || safeArea == null) return;

        Rect area = Screen.safeArea;
        // Screen space has its origin bottom-left; panel space top-left.
        Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(area.xMin, Screen.height - area.yMax));
        Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(area.xMax, Screen.height - area.yMin));
        Vector2 panelSize = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, Screen.height));

        safeArea.style.left = topLeft.x;
        safeArea.style.top = topLeft.y;
        safeArea.style.right = Mathf.Max(0f, panelSize.x - bottomRight.x);
        safeArea.style.bottom = Mathf.Max(0f, panelSize.y - bottomRight.y);

        appliedSafeArea = area;
        appliedScreenSize = new Vector2Int(Screen.width, Screen.height);
    }

    static void SetVisible(VisualElement element, bool visible)
    {
        element?.EnableInClassList("is-hidden", !visible);
    }

    T Find<T>(string elementName) where T : VisualElement
    {
        var element = root.Q<T>(elementName);
        if (element == null)
            Debug.LogError($"[AppUI] Element '{elementName}' ({typeof(T).Name}) not found in the UXML.", this);
        return element;
    }
}
