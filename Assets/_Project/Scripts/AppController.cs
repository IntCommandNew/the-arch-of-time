using System;
using UnityEngine;

/// <summary>
/// The app's state machine: decides which state the app is in and keeps the UI in sync.
/// Scripts report to it (startup gate, placement, adjusting, arch pieces), and it tells AppUI
/// what to show. AppUI and the AR scripts never talk to each other directly. The quest's story (greeting,
/// riddles, knowledge, quiz, seals, finale) is QuestFlow's: it runs while the user is in a "play area"
/// (Exploring; In Iasi the camera at the quest's place, its content shown).
///
/// States: ChoosingLanguage (first launch only) -> ChoosingMode;
///         ChoosingMode -> "In Iasi" -> Starting (location only) -> (Blocked) -> Guiding (camera off, to the
///         quest's current place: the academy, a stop's piece, the academy again for the finale) ->
///         ("Open the camera" at the place) -> Starting (camera) -> (Blocked) -> CityCamera -> (QuestFlow:
///         "Start walking") Guiding ...; "Modes" (Guiding) -> ChoosingMode. The AR session is paused while
///         the guide (camera off) is shown.
///         ChoosingMode -> "Anywhere" -> Starting -> (Blocked) -> Scanning;
///         Scanning <-> Placing -> Adjusting -> ("Done") Exploring <-> ("Edit") Adjusting;
///         "Place again" (Adjusting) -> Scanning; "Modes" (Scanning, Placing, Exploring) -> ChoosingMode;
///         SessionEnded (final) if a permission is revoked.
/// Nothing about the placement is saved: the academy is placed again each time. Only the quest's progress is
/// saved, separately for each mode (GameProgress). In Iasi the academy and the pieces are at fixed places
/// (CityTargets, authored with Geospatial Creator).
/// </summary>
public class AppController : MonoBehaviour
{
    public enum State
    {
        ChoosingLanguage, // the first launch: choose the language
        ChoosingMode,     // choose how to play
        Guiding,          // In Iasi: walking to the quest's current place, camera off
        CityCamera,       // In Iasi: at the place, camera on: locating, then the academy or a piece
        Starting,         // permissions / ARCore checks
        Blocked,          // AR can't run yet; the user must act
        Scanning,         // AR running, no ground under the reticle yet
        Placing,          // ground under the reticle: "Place here" is enabled
        Adjusting,        // the model is placed: move / turn / size / height
        Exploring,        // placement confirmed: the quest goes on around it; Edit
        SessionEnded,     // a permission was revoked: AR stopped, only "Close app" is left
    }

    [SerializeField] ARStartupGate startupGate;
    [SerializeField] PlacementController placement;
    [SerializeField] AdjustController adjust;
    [SerializeField] DepthOcclusion occlusion;
    [SerializeField] FootprintOutline footprint;
    [Tooltip("The arch by the academy (its state follows the mode's progress; its outline shows while adjusting).")]
    [SerializeField] ArchModel archModel;
    [SerializeField] NearFade modelFade;
    [SerializeField] ArchPieces pieces;
    [SerializeField] AppUI ui;
    [SerializeField] QuestFlow quest;

    [Header("In Iasi")]
    [SerializeField] CityTargets cityTargets;
    [SerializeField] CityGuide guide;
    [SerializeField] CityContent cityContent;

    [Tooltip("See-through while adjusting, until the user switches it off (remembered for the session).")]
    [SerializeField] bool seeThroughWhileAdjusting = true;

    string pendingExploreHint;   // shown once on the explore screen (e.g. "Changes discarded")
    float pendingHintUntil;
    bool editing;                // Adjusting came from "Edit" (not from a new placement): "Cancel" can undo it
    bool questInCamera;          // In Iasi: QuestFlow was started for this camera visit

    const float PendingHintSeconds = 2.5f;

    int cityTarget = -1;         // In Iasi: index in cityTargets.targets of the quest's current place
    bool cityCameraWanted;       // In Iasi: the startup gate was asked for the camera (not only location)
#pragma warning disable 0414 // read only on the phone (no walking away in the Editor)
    float farSince = -1f;        // In Iasi, camera on: when the user went beyond the leave radius
#pragma warning restore 0414
    const float WalkedAwaySeconds = 3f;
    float walkedAwayNoteUntil;   // the guide says why the camera closed, for a moment
    const float WalkedAwayNoteSeconds = 6f;

    public State Current { get; private set; } = State.ChoosingMode;

    /// <summary>The mode being played.</summary>
    public GameMode Mode { get; private set; } = GameMode.Free;

    /// <summary>Raised when the state changes (not when the same state is refreshed).</summary>
    public event Action<State> StateChanged;

    void OnEnable()
    {
        // Subscribe in OnEnable: the gate may report its first status in its own Start.
        if (startupGate != null)
        {
            startupGate.StatusChanged += OnGateStatusChanged;
            startupGate.SessionInvalidated += ClearSession;
        }
        if (placement != null)
        {
            placement.GroundChanged += OnGroundChanged;
            placement.Placed += OnPlaced;
            placement.PlaceFailed += OnPlaceFailed;
        }
        if (adjust != null) adjust.Changed += RefreshAdjustPanel;
        if (occlusion != null) occlusion.Changed += RefreshOcclusionButton;
        if (pieces != null) pieces.Changed += RefreshPlayPanels;
        if (guide != null) guide.Changed += RefreshGuide;
        if (cityContent != null) cityContent.Changed += OnCityContentChanged;
        if (quest != null)
        {
            quest.Changed += RefreshPlayPanels;
            quest.WalkRequested += OnWalkRequested;
            quest.StartedOver += OnQuestStartedOver;
        }
        if (ui != null)
        {
            ui.LanguageChosen += OnLanguageChosen;
            ui.LanguageChanged += OnLanguageChanged;
            ui.ModeChosen += OnModeChosen;
            ui.OpenCameraClicked += OnOpenCameraClicked;
            ui.GuideProgressClicked += OnProgressClicked;
            ui.GuideModesClicked += OnGuideModesClicked;
            ui.CityARProgressClicked += OnProgressClicked;
            ui.CityARRetryClicked += OnCityARRetryClicked;
            ui.CityARActionClicked += OnQuestActionClicked;
            ui.CityCollectClicked += OnCollectClicked;
            ui.RetryClicked += OnRetryClicked;
            ui.OpenSettingsClicked += OnOpenSettingsClicked;
            ui.CloseClicked += OnCloseClicked;
            ui.PlaceClicked += OnPlaceClicked;
            ui.PlaceAgainClicked += OnPlaceAgainClicked;
            ui.AdjustCommandIssued += OnAdjustCommand;
            ui.DoneClicked += OnDoneClicked;
            ui.SeeThroughClicked += OnSeeThroughClicked;
            ui.CancelEditClicked += OnCancelEditClicked;
            ui.EditClicked += OnEditClicked;
            ui.ExploreProgressClicked += OnProgressClicked;
            ui.ExploreActionClicked += OnQuestActionClicked;
            ui.CollectPieceClicked += OnCollectClicked;
            ui.OcclusionClicked += OnOcclusionClicked;
            ui.LeaveModeClicked += OnLeaveModeClicked;
            ui.LeaveModeAnswered += OnLeaveModeAnswered;
        }
    }

    void OnDisable()
    {
        if (startupGate != null)
        {
            startupGate.StatusChanged -= OnGateStatusChanged;
            startupGate.SessionInvalidated -= ClearSession;
        }
        if (placement != null)
        {
            placement.GroundChanged -= OnGroundChanged;
            placement.Placed -= OnPlaced;
            placement.PlaceFailed -= OnPlaceFailed;
        }
        if (adjust != null) adjust.Changed -= RefreshAdjustPanel;
        if (occlusion != null) occlusion.Changed -= RefreshOcclusionButton;
        if (pieces != null) pieces.Changed -= RefreshPlayPanels;
        if (guide != null) guide.Changed -= RefreshGuide;
        if (cityContent != null) cityContent.Changed -= OnCityContentChanged;
        if (quest != null)
        {
            quest.Changed -= RefreshPlayPanels;
            quest.WalkRequested -= OnWalkRequested;
            quest.StartedOver -= OnQuestStartedOver;
        }
        if (ui != null)
        {
            ui.LanguageChosen -= OnLanguageChosen;
            ui.LanguageChanged -= OnLanguageChanged;
            ui.ModeChosen -= OnModeChosen;
            ui.OpenCameraClicked -= OnOpenCameraClicked;
            ui.GuideProgressClicked -= OnProgressClicked;
            ui.GuideModesClicked -= OnGuideModesClicked;
            ui.CityARProgressClicked -= OnProgressClicked;
            ui.CityARRetryClicked -= OnCityARRetryClicked;
            ui.CityARActionClicked -= OnQuestActionClicked;
            ui.CityCollectClicked -= OnCollectClicked;
            ui.RetryClicked -= OnRetryClicked;
            ui.OpenSettingsClicked -= OnOpenSettingsClicked;
            ui.CloseClicked -= OnCloseClicked;
            ui.PlaceClicked -= OnPlaceClicked;
            ui.PlaceAgainClicked -= OnPlaceAgainClicked;
            ui.AdjustCommandIssued -= OnAdjustCommand;
            ui.DoneClicked -= OnDoneClicked;
            ui.SeeThroughClicked -= OnSeeThroughClicked;
            ui.CancelEditClicked -= OnCancelEditClicked;
            ui.EditClicked -= OnEditClicked;
            ui.ExploreProgressClicked -= OnProgressClicked;
            ui.ExploreActionClicked -= OnQuestActionClicked;
            ui.CollectPieceClicked -= OnCollectClicked;
            ui.OcclusionClicked -= OnOcclusionClicked;
            ui.LeaveModeClicked -= OnLeaveModeClicked;
            ui.LeaveModeAnswered -= OnLeaveModeAnswered;
        }
    }

    void Start()
    {
        if (startupGate == null || placement == null || adjust == null || occlusion == null || pieces == null ||
            ui == null || quest == null || cityTargets == null || guide == null || cityContent == null)
        {
            Debug.LogError("[AppController] Assign the ARStartupGate, PlacementController, AdjustController, " +
                           "DepthOcclusion, ArchPieces, AppUI, QuestFlow, CityTargets, CityGuide and CityContent.",
                           this);
            enabled = false;
            return;
        }
        ui.SetScaleRange(adjust.MinScale, adjust.MaxScale);
        EnterState(Localization.HasChosen ? State.ChoosingMode : State.ChoosingLanguage);
    }

    void Update()
    {
        // A one-off explore hint ("Changes discarded") gives way to the quest's hint after a moment.
        if (pendingExploreHint != null && Time.unscaledTime >= pendingHintUntil)
        {
            pendingExploreHint = null;
            RefreshPlayPanels();
        }
    }

    // Language
    /// The language screen (first launch) or the language line on the mode screen.
    void OnLanguageChosen(Language language)
    {
        if (Current != State.ChoosingLanguage && Current != State.ChoosingMode) return;
        Localization.Set(language);
        if (Current == State.ChoosingLanguage) EnterState(State.ChoosingMode);
    }

    /// The static texts changed by themselves; the ones made here are made again.
    void OnLanguageChanged()
    {
        switch (Current)
        {
            case State.ChoosingMode: RefreshModeScreen(); break;
            case State.Guiding: RefreshGuide(); break;
            case State.Scanning: ui.SetScanHint(Localization.Ui("scan.hint")); break;
            case State.Placing: ui.SetScanHint(Localization.Ui("scan.aim")); break;
            case State.Adjusting:
                ui.SetAdjustHint(Localization.Ui("adjust.hint"));
                RefreshAdjustPanel();
                break;
            case State.Exploring:
            case State.CityCamera:
                RefreshOcclusionButton();
                RefreshPlayPanels();
                break;
        }
    }

    // Modes
    void OnModeChosen(GameMode mode)
    {
        if (Current != State.ChoosingMode) return;
        Mode = mode;
        quest.NewVisit();
        if (archModel != null) archModel.SetMode(mode);
        occlusion.SetCityMode(mode == GameMode.InCity); // In Iasi: hidden by buildings (Streetscape)
        if (mode == GameMode.InCity)
        {
            cityCameraWanted = false;
            if (startupGate.ARStarted || startupGate.CurrentStatus == ARStartupGate.Status.LocationReady)
            {
                EnterState(State.Guiding);
                return;
            }
            EnterState(State.Starting);
            startupGate.BeginLocationOnly(); // reports through OnGateStatusChanged
            return;
        }

        if (startupGate.ARStarted)
        {
            EnterState(State.Scanning);
            return;
        }
        EnterState(State.Starting);
        // Begin(), not Retry(): after "In Iasi" the gate was started for location only, and a retry would
        // stop there again (no camera, stuck on "Starting the camera"). Begin() asks for AR.
        startupGate.Begin(); // reports through OnGateStatusChanged
    }

    void RefreshModeScreen()
    {
        ui.SetFreeModeProgress(i => GameProgress.IsCollected(GameMode.Free, i), ModeStatus(GameMode.Free));
        ui.SetCityModeProgress(i => GameProgress.IsCollected(GameMode.InCity, i), ModeStatus(GameMode.InCity));
    }

    static string ModeStatus(GameMode mode) =>
        GameProgress.Phase(mode) == QuestPhase.Done
            ? Localization.Ui("mode.complete")
            : Localization.Ui("mode.pieces", GameProgress.CollectedCount(mode), GameProgress.PieceCount);

    // Quest
    void OnProgressClicked()
    {
        if (Current == State.Guiding || Current == State.CityCamera || Current == State.Exploring)
            quest.OpenProgress(Mode);
    }

    void OnQuestActionClicked()
    {
        if (Current == State.Exploring || Current == State.CityCamera) quest.DoAction();
    }

    void OnCollectClicked()
    {
        if (Current == State.Exploring || Current == State.CityCamera) pieces.Collect();
    }

    /// In Iasi: the quest asks to walk to its next place.
    void OnWalkRequested()
    {
        if (Current == State.CityCamera) EnterState(State.Guiding);
    }

    void OnQuestStartedOver()
    {
        // In Iasi the quest starts again at the academy: back to the guide, which now leads there.
        if (Mode == GameMode.InCity && (Current == State.CityCamera || Current == State.Guiding))
            EnterState(State.Guiding);
        else
            RefreshPlayPanels();
    }

    // In Iasi
    void OnGuideModesClicked()
    {
        if (Current != State.Guiding) return;
        guide.StopLocation();
        EnterState(State.ChoosingMode);
    }

    void OnOpenCameraClicked()
    {
        if (Current != State.Guiding || !CanOpenCamera()) return;
        cityCameraWanted = true;
        if (startupGate.ARStarted)
        {
            EnterState(State.CityCamera);
            return;
        }
        EnterState(State.Starting);
        startupGate.Begin(); // asks for the camera, then starts AR: OnGateStatusChanged
    }

    /// At the place. In the Editor always, so the flow can be tried without walking.
    bool CanOpenCamera()
    {
#if UNITY_EDITOR
        return true;
#else
        return guide.CurrentStage == CityGuide.Stage.Arrived;
#endif
    }

    void OnCityARRetryClicked()
    {
        if (Current == State.CityCamera) cityContent.Retry();
    }

    CityTarget CurrentCityTarget =>
        cityTarget >= 0 && cityTarget < cityTargets.targets.Length ? cityTargets.targets[cityTarget] : null;

    /// The guide screen, from far to near: distance and compass direction, then the dial with warmer /
    /// colder, then "Open the camera". The title and the riddle come from the quest.
    void RefreshGuide()
    {
        if (Current == State.CityCamera)
        {
            CloseCameraIfWalkedAway();
            return;
        }
        if (Current != State.Guiding || CurrentCityTarget == null) return;
        var target = CurrentCityTarget;
        quest.GetGuideTexts(out string title, out string riddle);
        string stage, big, small, accuracy = "";
        int trend = 0;
        var look = GuideDial.Look.Far;
        bool pointer = false;

        switch (guide.CurrentStage)
        {
            case CityGuide.Stage.Far:
                stage = Localization.Ui("guide.far");
                big = GeoMath.FormatDistance(guide.DistanceMetres);
                small = Localization.Ui("guide.toThe", GeoMath.CompassName(guide.Bearing));
                break;
            case CityGuide.Stage.Near:
                stage = Localization.Ui("guide.near");
                look = GuideDial.Look.Near;
                // No needle for now (user, 22 Nov 2026): on the phone the heading never came (CityGuide reads the
                // legacy Input.compass, while the project uses only the new Input System), so the needle was never
                // seen. To bring it back: a heading that works, then pointer = guide.HasHeading and, with it,
                // small = GeoMath.RelativeDirection(guide.RelativeAngle).
                pointer = false;
                trend = guide.CurrentTrend == CityGuide.Trend.Warmer ? 1
                    : guide.CurrentTrend == CityGuide.Trend.Colder ? -1 : 0;
                big = Localization.Ui(trend > 0 ? "guide.warmer" : trend < 0 ? "guide.colder" : "guide.close");
                small = Localization.Ui("guide.toThe", GeoMath.CompassName(guide.Bearing));
                break;
            case CityGuide.Stage.Arrived:
                stage = Localization.Ui("guide.arrived");
                look = GuideDial.Look.Arrived;
                big = Localization.Ui("guide.here");
                small = Localization.Ui(target.kind == CityTargetKind.Academy
                    ? "guide.openForAcademy" : "guide.openForPiece");
                break;
            default:
                stage = Localization.Ui("guide.searching");
                big = "...";
                small = Localization.Ui(guide.LocationUnavailable ? "guide.locationOff" : "guide.finding");
                break;
        }
        if (guide.CurrentStage != CityGuide.Stage.NoFix)
            accuracy = Localization.Ui("guide.accuracy", guide.Source, guide.Accuracy.ToString("0"));
        if (Time.unscaledTime < walkedAwayNoteUntil && guide.CurrentStage != CityGuide.Stage.Arrived)
            stage = Localization.Ui("guide.walkedAway");
        ui.SetGuide(title, riddle, stage, big, small, trend, accuracy, look, pointer, guide.RelativeAngle,
            CanOpenCamera());
    }

    /// With the camera on, walking away (beyond the leave radius for a few seconds) closes it and goes
    /// back to the guide. Not in the Editor, where the position is fixed.
    void CloseCameraIfWalkedAway()
    {
#if !UNITY_EDITOR
        bool far = guide.CurrentStage != CityGuide.Stage.NoFix && guide.DistanceMetres > guide.LeaveRadius;
        if (!far)
        {
            farSince = -1f;
            return;
        }
        if (farSince < 0f) farSince = Time.unscaledTime;
        if (Time.unscaledTime - farSince < WalkedAwaySeconds) return;
        Debug.Log($"[AppController] Walked away from {CurrentCityTarget?.name} ({guide.DistanceMetres:0} m): " +
                  "camera closed.");
        walkedAwayNoteUntil = Time.unscaledTime + WalkedAwayNoteSeconds;
        EnterState(State.Guiding);
#endif
    }

    /// In Iasi, the camera: once the place's content is shown, the quest goes on there.
    void OnCityContentChanged()
    {
        if (Current != State.CityCamera) return;
        if (!questInCamera && cityContent.CurrentStatus == CityContent.Status.Shown)
        {
            questInCamera = true;
            quest.Begin(GameMode.InCity);
        }
        RefreshPlayPanels();
    }

    void OnLeaveModeClicked()
    {
        if (Current == State.Scanning || Current == State.Placing || Current == State.Exploring)
            ui.SetLeaveConfirmVisible(true);
    }

    void OnLeaveModeAnswered(bool confirmed)
    {
        ui.SetLeaveConfirmVisible(false);
        if (!confirmed) return;
        if (Current != State.Scanning && Current != State.Placing && Current != State.Exploring) return;
        placement.Clear(); // AR keeps running behind the mode screen; the progress is already saved
        EnterState(State.ChoosingMode);
    }

    // Startup gate
    void OnGateStatusChanged(ARStartupGate gate)
    {
        switch (gate.CurrentStatus)
        {
            case ARStartupGate.Status.Starting:
                ui.SetStartingDetail(gate.Detail);
                if (Current == State.Blocked) EnterState(State.Starting);
                break;

            case ARStartupGate.Status.Blocked:
                ui.SetBlocked(gate.BlockedTitle, gate.BlockedMessage,
                    gate.CanRetry, gate.CanOpenSettings, gate.CanClose);
                if (Current == State.Starting || Current == State.Blocked) EnterState(State.Blocked);
                break;

            case ARStartupGate.Status.Running:
                if (Current != State.Starting && Current != State.Blocked) break;
                if (Mode == GameMode.Free) EnterState(State.Scanning);
                else EnterState(cityCameraWanted ? State.CityCamera : State.Guiding);
                break;

            case ARStartupGate.Status.LocationReady:
                if ((Current == State.Starting || Current == State.Blocked) && Mode == GameMode.InCity)
                    EnterState(State.Guiding);
                break;

            case ARStartupGate.Status.PermissionsLost:
                ui.SetBlocked(gate.BlockedTitle, gate.BlockedMessage,
                    showRetry: false, showSettings: false, showClose: true);
                EnterState(State.SessionEnded);
                break;
        }
    }

    /// <summary>
    /// Drops everything that belongs to the current AR session (the progress stays saved).
    /// Called when a permission is revoked mid-session.
    /// </summary>
    void ClearSession()
    {
        quest.End();
        adjust.End();
        cityContent.Clear();
        pieces.End();
        placement.EndSearch();
        placement.Clear();
        Debug.Log("[AppController] Session cleared (progress kept).");
    }

    void OnRetryClicked() => startupGate.Retry();
    void OnOpenSettingsClicked() => startupGate.OpenAppSettings();
    void OnCloseClicked() => startupGate.QuitApp();

    // Placement
    void OnGroundChanged(bool found)
    {
        if (found && Current == State.Scanning) EnterState(State.Placing);
        else if (!found && Current == State.Placing) EnterState(State.Scanning);
    }

    void OnPlaceClicked()
    {
        if (Current != State.Placing || placement.IsPlacing) return;
        ui.SetPlaceEnabled(false); // until Placed / PlaceFailed
        placement.Place();
    }

    void OnPlaced()
    {
        if (Current == State.Placing || Current == State.Scanning) EnterState(State.Adjusting);
    }

    void OnPlaceFailed()
    {
        if (Current != State.Placing && Current != State.Scanning) return;
        ui.SetScanHint(Localization.Ui("scan.failed"));
        ui.SetPlaceEnabled(placement.HasGround);
    }

    void OnPlaceAgainClicked()
    {
        if (Current != State.Adjusting) return;
        placement.Clear();
        EnterState(State.Scanning);
    }

    // Adjusting
    void OnAdjustCommand(AdjustCommand command, float value)
    {
        if (Current != State.Adjusting) return;
        switch (command)
        {
            case AdjustCommand.NudgeLeft: adjust.Nudge(Vector2.left); break;
            case AdjustCommand.NudgeRight: adjust.Nudge(Vector2.right); break;
            case AdjustCommand.NudgeAway: adjust.Nudge(Vector2.up); break;
            case AdjustCommand.NudgeCloser: adjust.Nudge(Vector2.down); break;
            case AdjustCommand.Rotate: adjust.Rotate(value); break;
            case AdjustCommand.FaceSouth: adjust.FaceSouth(); break;
            case AdjustCommand.Scale: adjust.SetScale(value); break;
            case AdjustCommand.DefaultSize: adjust.ApplyDefaultSize(); break;
            case AdjustCommand.MaxSize: adjust.ApplyMaxSize(); break;
            case AdjustCommand.TogglePlatform: adjust.SetPlatformVisible(!adjust.PlatformVisible); break;
            case AdjustCommand.Lower: adjust.ChangeHeight(-1f); break;
            case AdjustCommand.Raise: adjust.ChangeHeight(1f); break;
            case AdjustCommand.StreetLevel: adjust.PutStreetLevelOnGround(); break;
            case AdjustCommand.BaseOnGround: adjust.PutBaseOnGround(); break;
        }
    }

    void OnSeeThroughClicked()
    {
        if (Current != State.Adjusting) return;
        seeThroughWhileAdjusting = !seeThroughWhileAdjusting;
        ApplySeeThrough();
    }

    /// See-through only while adjusting, and only if the user wants it.
    void ApplySeeThrough()
    {
        bool on = Current == State.Adjusting && seeThroughWhileAdjusting;
        if (modelFade != null) modelFade.SetSeeThrough(on);
        ui.SetSeeThroughOn(seeThroughWhileAdjusting);
    }

    void OnDoneClicked()
    {
        if (Current == State.Adjusting) EnterState(State.Exploring);
    }

    /// "Cancel" while editing: the model goes back to how it was before Edit.
    void OnCancelEditClicked()
    {
        if (Current != State.Adjusting || !editing) return;
        adjust.RevertToStart();
        pendingExploreHint = Localization.Ui("adjust.discarded");
        pendingHintUntil = Time.unscaledTime + PendingHintSeconds;
        EnterState(State.Exploring);
    }

    /// Updates the readout, slider and buttons from the model's current state.
    void RefreshAdjustPanel()
    {
        if (Current != State.Adjusting) return;
        string size = TextSymbols.Times + adjust.Scale.ToString(adjust.Scale >= 10f ? "0" : "0.0#");
        string front = adjust.TryGetFrontBearing(out float bearing, out float accuracy)
            ? $"{bearing:0}{TextSymbols.Degree} {GeoMath.CompassPoint(bearing)} " +
              $"{TextSymbols.PlusMinus}{accuracy:0}{TextSymbols.Degree}"
            : Localization.Ui("adjust.noHeading");
        string height = $"{adjust.Height:+0.00;-0.00;0.00} m";
        ui.SetAdjustReadout(size, front, height);
        ui.SetScaleValue(adjust.Scale);
        ui.SetFaceSouthEnabled(adjust.CanFaceSouth);
        ui.SetPlatformButtonText(Localization.Ui(adjust.PlatformVisible ? "adjust.hideBase" : "adjust.showBase"));
    }

    // Exploring and the in-city camera: the quest's panel
    void OnEditClicked()
    {
        if (Current != State.Exploring) return;
        editing = true;
        EnterState(State.Adjusting); // AdjustController.Begin remembers the model as it is now
    }

    void RefreshPlayPanels()
    {
        if (Current == State.Exploring) RefreshExplorePanel();
        else if (Current == State.CityCamera) RefreshCityCamera();
    }

    /// The hint for the piece or seal that is out ("The seal is behind you (2.4 m)"), or null when none is.
    string ItemHint(out bool inSight)
    {
        inSight = false;
        bool seal = pieces.OutKind == ItemKind.Seal;
        if (pieces.IsSpawning) return Localization.Ui(seal ? "item.placingSeal" : "item.placingPiece");
        if (!pieces.TryGetPieceDirection(out float angle, out float distance, out bool onScreen)) return null;
        if (pieces.IsAimed)
        {
            inSight = true;
            return Localization.Ui("item.inSight");
        }
        string metres = distance.ToString(Mode == GameMode.InCity ? "0" : "0.0");
        return onScreen
            ? Localization.Ui(seal ? "item.aimSeal" : "item.aimPiece", metres)
            : Localization.Ui(seal ? "item.whereSeal" : "item.wherePiece", GeoMath.RelativeDirection(angle), metres);
    }

    /// The explore panel: the arch, the count, the hint (the item's, or the quest's) and the buttons.
    void RefreshExplorePanel()
    {
        string hint = ItemHint(out bool inSight);
        quest.GetObjective(out string questHint, out string action);
        if (hint == null) hint = questHint;
        if (pendingExploreHint != null) hint = pendingExploreHint;
        ui.SetPieces(i => GameProgress.IsCollected(Mode, i),
            Localization.Ui("explore.count", pieces.Collected, pieces.Total), hint, inSight,
            showCollect: pieces.HasPiece || pieces.IsSpawning, canCollect: pieces.IsAimed, action: action);
    }

    /// The in-city camera screen: locating, placing, then the quest (the academy, a piece, a seal).
    void RefreshCityCamera()
    {
        if (Current != State.CityCamera || CurrentCityTarget == null) return;
        bool piece = CurrentCityTarget.kind == CityTargetKind.ArchPiece;
        string hint = null, action = null;
        bool inSight = false, showCollect = false, showRetry = false;

        switch (cityContent.CurrentStatus)
        {
            case CityContent.Status.Locating:
                hint = float.IsInfinity(cityContent.HorizontalAccuracy)
                    ? Localization.Ui("cityar.pointCamera")
                    : Localization.Ui("cityar.finding", cityContent.HorizontalAccuracy.ToString("0"),
                                      cityContent.HeadingAccuracy.ToString("0"),
                                      cityContent.MaxHorizontalAccuracy.ToString("0"),
                                      cityContent.MaxHeadingAccuracy.ToString("0"));
                break;
            case CityContent.Status.Resolving:
                hint = Localization.Ui(piece ? "cityar.placingPiece" : "cityar.placingAcademy");
                break;
            case CityContent.Status.Failed:
                hint = Localization.Ui("cityar.failed", cityContent.FailedReason);
                showRetry = true;
                break;
            default:
                hint = ItemHint(out inSight);
                showCollect = pieces.HasPiece || pieces.IsSpawning;
                quest.GetObjective(out string questHint, out action);
                if (hint == null) hint = questHint ?? (piece ? null : Localization.Ui("cityar.academyHere"));
                break;
        }
        ui.SetCityAR(hint, inSight, showCollect, pieces.IsAimed, showRetry, action);
    }

    void RefreshOcclusionButton()
    {
        bool unsupported = occlusion.Support == UnityEngine.XR.ARSubsystems.Supported.Unsupported;
        string on = occlusion.CityMode ? "cityar.occlusionOn" : "explore.occlusionOn";
        string off = occlusion.CityMode ? "cityar.occlusionOff" : "explore.occlusionOff";
        string key = unsupported ? "explore.occlusionNone" : occlusion.IsOn ? on : off;
        ui.SetOcclusionButton(Localization.Ui(key), enabled: !unsupported);
    }

    void OnOcclusionClicked()
    {
        if ((Current != State.Exploring && Current != State.CityCamera) ||
            occlusion.Support == UnityEngine.XR.ARSubsystems.Supported.Unsupported)
            return;
        occlusion.SetOn(!occlusion.IsOn); // raises Changed -> RefreshOcclusionButton
    }

    // States
    void EnterState(State next)
    {
        if (Current == State.SessionEnded) return; // final: nothing leaves this state

        bool changed = next != Current;
        var previous = Current;
        Current = next;

        // Leaving a play area: the quest's cards close (the progress is already saved).
        bool wasPlaying = previous == State.Exploring || previous == State.CityCamera;
        bool playing = next == State.Exploring || next == State.CityCamera;
        if (wasPlaying && (!playing || changed)) quest.End();
        if (next != State.CityCamera) questInCamera = false;

        if (next != State.Adjusting)
        {
            adjust.End();
            editing = false;
        }
        if (next != State.CityCamera && cityContent.CurrentStatus != CityContent.Status.Idle)
            cityContent.Clear(); // the in-city academy or piece goes away
        if (!playing)
        {
            pieces.End(); // the item that is out goes away; the progress stays saved
            pendingExploreHint = null;
        }
        // The guide also runs with the camera on, to notice the user walking away.
        if (next != State.Guiding && next != State.CityCamera) guide.End();
        farSince = -1f;
        // The camera is off on the guide (the session is paused, if it runs).
        if (next == State.Guiding) startupGate.SetARPaused(true);
        else if (next == State.Scanning || next == State.CityCamera) startupGate.SetARPaused(false);
        if (footprint != null) footprint.SetVisible(next == State.Adjusting); // only while adjusting
        if (archModel != null) archModel.SetOutlineVisible(next == State.Adjusting);
        ApplySeeThrough();
        ui.SetLeaveConfirmVisible(false);
        ui.HideProgress();

        switch (next)
        {
            case State.ChoosingLanguage:
                ui.ShowScreen(AppScreen.Language);
                break;

            case State.ChoosingMode:
                placement.EndSearch();
                placement.SetPlanesVisible(false);
                RefreshModeScreen();
                ui.ShowScreen(AppScreen.Mode);
                break;

            case State.Guiding:
                placement.EndSearch();
                placement.SetPlanesVisible(false);
                cityTarget = quest.CityTargetIndex(cityTargets);
                if (CurrentCityTarget == null)
                    Debug.LogError("[AppController] No city target for the quest's current place.", this);
                ui.ShowScreen(AppScreen.Guide);
                guide.Begin(CurrentCityTarget); // raises Changed -> RefreshGuide
                RefreshGuide();
                break;

            case State.CityCamera:
                placement.EndSearch();
                placement.SetPlanesVisible(false);
                ui.ShowScreen(AppScreen.CityAR);
                RefreshOcclusionButton();
                guide.Begin(CurrentCityTarget);
                cityContent.Show(CurrentCityTarget); // raises Changed -> OnCityContentChanged
                // After Show (it clears the old content, which can end ArchPieces): seals can appear here too.
                pieces.Begin(GameMode.InCity);
                break;

            case State.Starting:
                ui.SetStartingDetail("");
                ui.ShowScreen(AppScreen.Starting);
                break;

            case State.Blocked:
            case State.SessionEnded:
                placement.EndSearch();
                ui.ShowScreen(AppScreen.Blocked);
                break;

            case State.Scanning:
                if (!placement.IsSearching) placement.BeginSearch();
                placement.SetPlanesVisible(true);
                ui.SetScanHint(Localization.Ui("scan.hint"));
                ui.SetPlaceEnabled(false);
                ui.ShowScreen(AppScreen.Scan);
                break;

            case State.Placing:
                ui.SetScanHint(Localization.Ui("scan.aim"));
                ui.SetPlaceEnabled(!placement.IsPlacing);
                ui.ShowScreen(AppScreen.Scan);
                break;

            case State.Adjusting:
                placement.EndSearch();
                placement.SetPlanesVisible(false); // the model is easier to see without them
                ui.SetAdjustHint(Localization.Ui("adjust.hint"));
                ui.SetAdjustEditing(editing);
                ui.ShowScreen(AppScreen.Adjust);
                adjust.Begin(); // raises Changed -> RefreshAdjustPanel
                break;

            case State.Exploring:
                RefreshOcclusionButton();
                ui.ShowScreen(AppScreen.Explore);
                pieces.Begin(Mode); // raises Changed -> RefreshPlayPanels
                quest.Begin(Mode);
                RefreshExplorePanel();
                break;
        }

        if (changed) StateChanged?.Invoke(next);
    }
}
