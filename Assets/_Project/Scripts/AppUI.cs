using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>The app's screens. Each one is a "screen-..." element in MainUI.uxml.</summary>
public enum AppScreen
{
    Starting,
    Blocked,
    Scan,    // scanning and placing: hint + "Place here"
    Adjust,  // the model is placed: adjust panel (move / turn / size / height), "Place again", "Done"
    Explore, // placement confirmed: "Edit", "New location"
    Save,    // after "Done": walk around the spot (quality meter), "Save" / "Not now"
    Restore, // at launch with a saved placement: finding it, or why it wasn't found
    Pieces,  // arch pieces test mode: spawn a piece around you, aim, collect
}

/// <summary>Buttons of the adjust panel. The float that comes with them is noted per command.</summary>
public enum AdjustCommand
{
    NudgeLeft, NudgeRight, NudgeAway, NudgeCloser,
    Rotate,          // value: degrees, positive = clockwise seen from above
    FaceSouth,
    Scale,           // value: the new scale (from the slider)
    Tabletop, RealSize, TogglePlatform,
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
    /// <summary>"Try again" on the blocked screen.</summary>
    public event Action RetryClicked;

    /// <summary>"Open settings" on the blocked screen.</summary>
    public event Action OpenSettingsClicked;

    /// <summary>"Close app" on the blocked screen (after a permission was revoked).</summary>
    public event Action CloseClicked;

    /// <summary>"Place here" on the scan screen.</summary>
    public event Action PlaceClicked;

    /// <summary>"Place again" on the adjust screen.</summary>
    public event Action PlaceAgainClicked;

    /// <summary>A button (or the size slider) of the adjust panel. See AdjustCommand for the value.</summary>
    public event Action<AdjustCommand, float> AdjustCommandIssued;

    /// <summary>"Done" on the adjust screen.</summary>
    public event Action DoneClicked;

    /// <summary>"See-through" on the adjust screen.</summary>
    public event Action SeeThroughClicked;

    /// <summary>"Cancel" on the adjust screen (shown instead of "Place again" when editing).</summary>
    public event Action CancelEditClicked;

    /// <summary>"Edit" on the explore screen.</summary>
    public event Action EditClicked;

    /// <summary>"New location" on the explore screen (asks for confirmation first).</summary>
    public event Action NewLocationClicked;

    /// <summary>The answer on the "Pick a new location?" card.</summary>
    public event Action<bool> NewLocationAnswered;

    /// <summary>"Hidden by real objects" on the explore screen.</summary>
    public event Action OcclusionClicked;

    /// <summary>"Arch pieces" on the scan or restore screen.</summary>
    public event Action PiecesModeClicked;

    /// <summary>Buttons of the arch pieces screen.</summary>
    public event Action PiecesBackClicked;
    public event Action SpawnPieceClicked;
    public event Action CollectPieceClicked;
    public event Action ResetPiecesClicked;

    /// <summary>"Save" on the save screen.</summary>
    public event Action SaveClicked;

    /// <summary>"Not now" on the save screen.</summary>
    public event Action SaveSkipClicked;

    /// <summary>"Try again" on the restore screen.</summary>
    public event Action RestoreRetryClicked;

    /// <summary>"Place a new one" on the restore screen.</summary>
    public event Action RestoreNewClicked;

    public AppScreen CurrentScreen { get; private set; } = AppScreen.Starting;

    UIDocument document;
    VisualElement root;
    VisualElement safeArea;
    readonly Dictionary<AppScreen, VisualElement> screens = new Dictionary<AppScreen, VisualElement>();

    Label startingDetail;
    Label blockedTitle;
    Label blockedMessage;
    Label scanHint;
    Label adjustHint;
    Label debugOverlay;
    Button retryButton;
    Button settingsButton;
    Button closeButton;
    Button placeButton;
    Button placeAgainButton;

    // Adjust panel
    Label adjustReadout;
    Slider scaleSlider;
    Button faceSouthButton;
    Button realSizeButton;
    Button platformButton;
    Button seeThroughButton;
    Button cancelEditButton;
    readonly List<(Button tab, VisualElement tool)> adjustTabs = new List<(Button, VisualElement)>();
    int selectedTab;

    // Explore
    Label exploreHint;
    VisualElement exploreActions;
    VisualElement newLocationConfirm;
    Button occlusionButton;

    // Save
    Label saveMessage;
    VisualElement saveMeter;
    Label saveQuality;
    Button saveButton;
    Button saveSkipButton;

    // Arch pieces
    Label piecesHint;
    Label piecesCount;
    Button spawnPieceButton;
    Button collectPieceButton;

    // Restore
    Label restoreTitle;
    Label restoreMessage;
    Button restoreRetryButton;
    Button restoreNewButton;

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
        screens[AppScreen.Starting] = Find<VisualElement>("screen-starting");
        screens[AppScreen.Blocked] = Find<VisualElement>("screen-blocked");
        screens[AppScreen.Scan] = Find<VisualElement>("screen-scan");
        screens[AppScreen.Adjust] = Find<VisualElement>("screen-adjust");
        screens[AppScreen.Explore] = Find<VisualElement>("screen-explore");
        screens[AppScreen.Save] = Find<VisualElement>("screen-save");
        screens[AppScreen.Restore] = Find<VisualElement>("screen-restore");
        screens[AppScreen.Pieces] = Find<VisualElement>("screen-pieces");

        startingDetail = Find<Label>("starting-detail");
        blockedTitle = Find<Label>("blocked-title");
        blockedMessage = Find<Label>("blocked-message");
        scanHint = Find<Label>("scan-hint");
        adjustHint = Find<Label>("adjust-hint");
        debugOverlay = Find<Label>("debug-overlay");
        retryButton = Find<Button>("btn-retry");
        settingsButton = Find<Button>("btn-settings");
        closeButton = Find<Button>("btn-close");
        placeButton = Find<Button>("btn-place");
        placeAgainButton = Find<Button>("btn-place-again");

        if (retryButton != null) retryButton.clicked += OnRetryClicked;
        if (settingsButton != null) settingsButton.clicked += OnSettingsClicked;
        if (closeButton != null) closeButton.clicked += OnCloseClicked;
        if (placeButton != null) placeButton.clicked += OnPlaceClicked;
        if (placeAgainButton != null) placeAgainButton.clicked += OnPlaceAgainClicked;

        SetUpAdjustPanel();
        SetUpExplore();
        SetUpSaveAndRestore();
        SetUpPieces();
        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

        appliedSafeArea = default;
        ShowScreen(CurrentScreen);
        SetDebugText(null);
    }

    void OnDisable()
    {
        if (retryButton != null) retryButton.clicked -= OnRetryClicked;
        if (settingsButton != null) settingsButton.clicked -= OnSettingsClicked;
        if (closeButton != null) closeButton.clicked -= OnCloseClicked;
        if (placeButton != null) placeButton.clicked -= OnPlaceClicked;
        if (placeAgainButton != null) placeAgainButton.clicked -= OnPlaceAgainClicked;
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
        // Rotation, split screen or a changed cut-out all change the safe area.
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

    public void SetScanHint(string text)
    {
        if (scanHint == null) return;
        scanHint.text = text ?? "";
        SetVisible(scanHint, !string.IsNullOrEmpty(text));
    }

    /// <summary>"Place here" is shown on the scan screen, but only enabled with ground under the reticle.</summary>
    public void SetPlaceEnabled(bool enabled)
    {
        placeButton?.SetEnabled(enabled);
    }

    public void SetAdjustHint(string text)
    {
        if (adjustHint == null) return;
        adjustHint.text = text ?? "";
        SetVisible(adjustHint, !string.IsNullOrEmpty(text));
    }

    // Adjust panel
    /// <summary>The live line at the top of the adjust panel (size, front bearing, height).</summary>
    public void SetAdjustReadout(string text)
    {
        if (adjustReadout != null) adjustReadout.text = text ?? "";
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
    public void SetRealSizeEnabled(bool enabled) => realSizeButton?.SetEnabled(enabled);

    public void SetPlatformButtonText(string text)
    {
        if (platformButton != null) platformButton.text = text ?? "";
    }

    /// <summary>Shows the "See-through" button as on (gold) or off.</summary>
    public void SetSeeThroughOn(bool on) => seeThroughButton?.EnableInClassList("btn--toggle-on", on);

    /// <summary>Editing a confirmed placement: "Cancel" instead of "Place again".</summary>
    public void SetAdjustEditing(bool editing)
    {
        SetVisible(cancelEditButton, editing);
        SetVisible(placeAgainButton, !editing);
    }

    // Explore
    public void SetExploreHint(string text)
    {
        if (exploreHint == null) return;
        exploreHint.text = text ?? "";
        SetVisible(exploreHint, !string.IsNullOrEmpty(text));
    }

    /// <summary>Shows the "Pick a new location?" card (and hides the explore buttons behind it).</summary>
    public void SetNewLocationConfirmVisible(bool visible)
    {
        SetVisible(newLocationConfirm, visible);
        SetVisible(exploreActions, !visible);
        SetVisible(occlusionButton, !visible);
    }

    /// <summary>The "Hidden by real objects" option: its text, and whether it can be used.</summary>
    public void SetOcclusionButton(string text, bool enabled)
    {
        if (occlusionButton == null) return;
        occlusionButton.text = text ?? "";
        occlusionButton.SetEnabled(enabled);
    }

    // Save
    /// <summary>The save screen's text; while busy (uploading) its buttons are disabled.</summary>
    public void SetSaveStatus(string message, bool busy)
    {
        if (saveMessage != null) saveMessage.text = message ?? "";
        saveButton?.SetEnabled(!busy);
        saveSkipButton?.SetEnabled(!busy);
    }

    public void SetSaveButtonText(string text)
    {
        if (saveButton != null) saveButton.text = text ?? "";
    }

    /// <summary>The walk-around meter: level 0 (low), 1 (enough), 2 (good); below 0 hides it.</summary>
    public void SetSaveQuality(int level, string label)
    {
        bool show = level >= 0;
        SetVisible(saveMeter, show);
        SetVisible(saveQuality, show);
        if (!show) return;
        if (saveQuality != null) saveQuality.text = label ?? "";
        if (saveMeter == null) return;
        saveMeter.EnableInClassList("meter--low", level == 0);
        saveMeter.EnableInClassList("meter--mid", level == 1);
        saveMeter.EnableInClassList("meter--high", level >= 2);
    }

    // Arch pieces
    /// <summary>The pieces screen: count ("Arch pieces: 2 / 5"), hint, and which buttons can be used.</summary>
    public void SetPiecesStatus(string count, string hint, bool canSpawn, bool canCollect)
    {
        if (piecesCount != null) piecesCount.text = count ?? "";
        if (piecesHint != null)
        {
            piecesHint.text = hint ?? "";
            SetVisible(piecesHint, !string.IsNullOrEmpty(hint));
        }
        spawnPieceButton?.SetEnabled(canSpawn);
        collectPieceButton?.SetEnabled(canCollect);
    }

    // Restore
    public void SetRestoreStatus(string title, string message, bool showRetry)
    {
        if (restoreTitle != null) restoreTitle.text = title ?? "";
        if (restoreMessage != null) restoreMessage.text = message ?? "";
        SetVisible(restoreRetryButton, showRetry);
    }

    // Developer overlay
    /// <summary>Shows the overlay text (rich text allowed). Null or empty hides it.</summary>
    public void SetDebugText(string text)
    {
        if (debugOverlay == null) return;
        bool show = !string.IsNullOrEmpty(text);
        if (show) debugOverlay.text = text;
        SetVisible(debugOverlay, show);
    }

    // Touch helpers (for AR gestures later)
    /// <summary>
    /// True if a screen position (Input System coordinates: origin bottom-left, pixels) is over
    /// an interactive part of the UI, e.g. a card or button. Use it to ignore AR gestures there.
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
    void OnRetryClicked() => RetryClicked?.Invoke();
    void OnSettingsClicked() => OpenSettingsClicked?.Invoke();
    void OnCloseClicked() => CloseClicked?.Invoke();
    void OnPlaceClicked() => PlaceClicked?.Invoke();
    void OnPlaceAgainClicked() => PlaceAgainClicked?.Invoke();

    void SetUpAdjustPanel()
    {
        adjustReadout = Find<Label>("adjust-readout");
        scaleSlider = Find<Slider>("scale-slider");
        faceSouthButton = Find<Button>("btn-face-south");
        realSizeButton = Find<Button>("btn-real-size");
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
        Wire(Find<Button>("btn-tabletop"), () => Issue(AdjustCommand.Tabletop));
        Wire(realSizeButton, () => Issue(AdjustCommand.RealSize));
        Wire(platformButton, () => Issue(AdjustCommand.TogglePlatform));
        Wire(Find<Button>("btn-street-level"), () => Issue(AdjustCommand.StreetLevel));
        Wire(Find<Button>("btn-base-on-ground"), () => Issue(AdjustCommand.BaseOnGround));
        Wire(Find<Button>("btn-done"), () => DoneClicked?.Invoke());
        seeThroughButton = Find<Button>("btn-see-through");
        Wire(seeThroughButton, () => SeeThroughClicked?.Invoke());
        cancelEditButton = Find<Button>("btn-cancel-edit");
        Wire(cancelEditButton, () => CancelEditClicked?.Invoke());

        scaleSlider?.RegisterValueChangedCallback(OnScaleSliderChanged);
    }

    void SetUpExplore()
    {
        exploreHint = Find<Label>("explore-hint");
        exploreActions = Find<VisualElement>("explore-actions");
        newLocationConfirm = Find<VisualElement>("confirm-new-location");
        occlusionButton = Find<Button>("btn-occlusion");
        Wire(occlusionButton, () => OcclusionClicked?.Invoke());
        Wire(Find<Button>("btn-edit"), () => EditClicked?.Invoke());
        Wire(Find<Button>("btn-new-location"), () => NewLocationClicked?.Invoke());
        Wire(Find<Button>("btn-new-location-confirm"), () => NewLocationAnswered?.Invoke(true));
        Wire(Find<Button>("btn-new-location-cancel"), () => NewLocationAnswered?.Invoke(false));
        SetNewLocationConfirmVisible(false);
    }

    void SetUpSaveAndRestore()
    {
        saveMessage = Find<Label>("save-message");
        saveMeter = Find<VisualElement>("save-meter");
        saveQuality = Find<Label>("save-quality");
        saveButton = Find<Button>("btn-save");
        saveSkipButton = Find<Button>("btn-save-skip");
        Wire(saveButton, () => SaveClicked?.Invoke());
        Wire(saveSkipButton, () => SaveSkipClicked?.Invoke());

        restoreTitle = Find<Label>("restore-title");
        restoreMessage = Find<Label>("restore-message");
        restoreRetryButton = Find<Button>("btn-restore-retry");
        restoreNewButton = Find<Button>("btn-restore-new");
        Wire(restoreRetryButton, () => RestoreRetryClicked?.Invoke());
        Wire(restoreNewButton, () => RestoreNewClicked?.Invoke());
    }

    void SetUpPieces()
    {
        piecesHint = Find<Label>("pieces-hint");
        piecesCount = Find<Label>("pieces-count");
        spawnPieceButton = Find<Button>("btn-spawn-piece");
        collectPieceButton = Find<Button>("btn-collect-piece");
        Wire(Find<Button>("btn-pieces-from-scan"), () => PiecesModeClicked?.Invoke());
        Wire(Find<Button>("btn-pieces-from-restore"), () => PiecesModeClicked?.Invoke());
        Wire(Find<Button>("btn-pieces-back"), () => PiecesBackClicked?.Invoke());
        Wire(spawnPieceButton, () => SpawnPieceClicked?.Invoke());
        Wire(collectPieceButton, () => CollectPieceClicked?.Invoke());
        Wire(Find<Button>("btn-pieces-reset"), () => ResetPiecesClicked?.Invoke());
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
