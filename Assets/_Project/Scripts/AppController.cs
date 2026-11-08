using System;
using UnityEngine;

/// <summary>
/// The app's state machine: decides which state the app is in and keeps the UI in sync.
/// Scripts report to it (startup gate, placement, adjusting, arch pieces), and it tells AppUI
/// what to show. AppUI and the AR scripts never talk to each other directly.
///
/// States: ChoosingMode -> "In Iasi" -> Starting (location only) -> (Blocked) -> CityList;
///         CityList -> (a target) Guiding (camera off) -> ("Open the camera" at the spot) -> Starting
///         (camera) -> (Blocked) -> CityCamera; "Targets" (Guiding, CityCamera) -> CityList;
///         "Modes" (CityList) -> ChoosingMode. The AR session is paused while the in-city screens
///         with the camera off are shown.
///         ChoosingMode -> "Anywhere" -> Starting -> (Blocked) -> Scanning;
///         Scanning <-> Placing -> Adjusting -> ("Done") Exploring <-> ("Edit") Adjusting;
///         "Place again" (Adjusting) -> Scanning; "Modes" (Scanning, Placing, Exploring) -> ChoosingMode;
///         SessionEnded (final) if a permission is revoked.
/// Nothing about the placement is saved: the academy is placed again each time. Only the progress
/// (the arch pieces collected) is saved, separately for each mode (GameProgress). In Iasi the academy
/// and the pieces are at fixed places (CityTargets, authored with Geospatial Creator).
/// </summary>
public class AppController : MonoBehaviour
{
    public enum State
    {
        ChoosingMode,  // the first screen
        CityList,      // In Iasi: pick a target (the academy or a piece not found yet)
        Guiding,       // In Iasi: walking to the target, camera off
        CityCamera,    // In Iasi: at the target, camera on: locating, then the academy or a piece
        Starting,      // permissions / ARCore checks
        Blocked,       // AR can't run yet; the user must act
        Scanning,      // AR running, no ground under the reticle yet
        Placing,       // ground under the reticle: "Place here" is enabled
        Adjusting,     // the model is placed: move / turn / size / height
        Exploring,     // placement confirmed: arch pieces; Edit
        SessionEnded,  // a permission was revoked: AR stopped, only "Close app" is left
    }

    [SerializeField] ARStartupGate startupGate;
    [SerializeField] PlacementController placement;
    [SerializeField] AdjustController adjust;
    [SerializeField] DepthOcclusion occlusion;
    [SerializeField] FootprintOutline footprint;
    [SerializeField] NearFade modelFade;
    [SerializeField] ArchPieces pieces;
    [SerializeField] AppUI ui;

    [Header("In Iasi")]
    [SerializeField] CityTargets cityTargets;
    [SerializeField] CityGuide guide;
    [SerializeField] CityContent cityContent;

    [Tooltip("See-through while adjusting, until the user switches it off (remembered for the session).")]
    [SerializeField] bool seeThroughWhileAdjusting = true;

    [Header("Texts")]
    [SerializeField] string scanHint = "Move your phone slowly over the ground";
    [SerializeField] string aimHint = "Aim the ring at the spot, then tap \"Place here\"";
    [SerializeField] string placeFailedHint = "Couldn't place it there. Aim at the ground and try again";
    [SerializeField] string adjustHint = "Drag to move. Two fingers: twist to turn, pinch to resize";
    [SerializeField] string editCancelledHint = "Changes discarded";
    [SerializeField] string occlusionOffText = "Hidden by real objects: Off";
    [SerializeField] string occlusionOnText = "Hidden by real objects: On";
    [SerializeField] string occlusionUnsupportedText = "Hidden by real objects: not on this phone";
    [SerializeField] string hideBaseText = "Hide base";
    [SerializeField] string showBaseText = "Show base";

    string pendingExploreHint;   // shown once on the explore screen (e.g. "Changes discarded")
    float pendingHintUntil;
    bool editing;                // Adjusting came from "Edit" (not from a new placement): "Cancel" can undo it

    const float PendingHintSeconds = 2.5f;

    int pickedTarget = -1;       // In Iasi: index in cityTargets.targets
    bool cityCameraWanted;       // In Iasi: the startup gate was asked for the camera (not only location)
    float nextListRefresh;
    const float ListRefreshSeconds = 2f;
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
        if (pieces != null)
        {
            pieces.Changed += RefreshPiecesPanel;
            pieces.Changed += RefreshCityCamera;
        }
        if (guide != null) guide.Changed += RefreshGuide;
        if (cityContent != null) cityContent.Changed += RefreshCityCamera;
        if (ui != null)
        {
            ui.ModeChosen += OnModeChosen;
            ui.CityBackClicked += OnCityBackClicked;
            ui.TargetPicked += OnTargetPicked;
            ui.CityResetClicked += OnCityResetClicked;
            ui.GuideBackClicked += OnBackToTargets;
            ui.OpenCameraClicked += OnOpenCameraClicked;
            ui.CityARBackClicked += OnBackToTargets;
            ui.CityARRetryClicked += OnCityARRetryClicked;
            ui.CityCollectClicked += OnCityCollectClicked;
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
            ui.SpawnPieceClicked += OnSpawnPieceClicked;
            ui.CollectPieceClicked += OnCollectPieceClicked;
            ui.ResetPiecesClicked += OnResetPiecesClicked;
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
        if (pieces != null)
        {
            pieces.Changed -= RefreshPiecesPanel;
            pieces.Changed -= RefreshCityCamera;
        }
        if (guide != null) guide.Changed -= RefreshGuide;
        if (cityContent != null) cityContent.Changed -= RefreshCityCamera;
        if (ui != null)
        {
            ui.ModeChosen -= OnModeChosen;
            ui.CityBackClicked -= OnCityBackClicked;
            ui.TargetPicked -= OnTargetPicked;
            ui.CityResetClicked -= OnCityResetClicked;
            ui.GuideBackClicked -= OnBackToTargets;
            ui.OpenCameraClicked -= OnOpenCameraClicked;
            ui.CityARBackClicked -= OnBackToTargets;
            ui.CityARRetryClicked -= OnCityARRetryClicked;
            ui.CityCollectClicked -= OnCityCollectClicked;
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
            ui.SpawnPieceClicked -= OnSpawnPieceClicked;
            ui.CollectPieceClicked -= OnCollectPieceClicked;
            ui.ResetPiecesClicked -= OnResetPiecesClicked;
            ui.OcclusionClicked -= OnOcclusionClicked;
            ui.LeaveModeClicked -= OnLeaveModeClicked;
            ui.LeaveModeAnswered -= OnLeaveModeAnswered;
        }
    }

    void Start()
    {
        if (startupGate == null || placement == null || adjust == null || occlusion == null || pieces == null ||
            ui == null || cityTargets == null || guide == null || cityContent == null)
        {
            Debug.LogError("[AppController] Assign the ARStartupGate, PlacementController, AdjustController, " +
                           "DepthOcclusion, ArchPieces, AppUI, CityTargets, CityGuide and CityContent.", this);
            enabled = false;
            return;
        }
        ui.SetScaleRange(adjust.MinScale, adjust.MaxScale);
        EnterState(State.ChoosingMode);
    }

    void Update()
    {
        // A one-off explore hint ("Changes discarded") gives way to the pieces hint after a moment.
        if (pendingExploreHint != null && Time.unscaledTime >= pendingHintUntil)
        {
            pendingExploreHint = null;
            RefreshPiecesPanel();
        }
        // The distances in the target list follow the user.
        if (Current == State.CityList && Time.unscaledTime >= nextListRefresh) RefreshCityList();
    }

    // Modes
    void OnModeChosen(GameMode mode)
    {
        if (Current != State.ChoosingMode) return;
        Mode = mode;
        if (mode == GameMode.InCity)
        {
            cityCameraWanted = false;
            if (startupGate.ARStarted || startupGate.CurrentStatus == ARStartupGate.Status.LocationReady)
            {
                EnterState(State.CityList);
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

    // In Iasi
    void OnCityBackClicked()
    {
        if (Current != State.CityList) return;
        guide.StopLocation();
        EnterState(State.ChoosingMode);
    }

    void OnTargetPicked(int index)
    {
        if (Current != State.CityList || index < 0 || index >= cityTargets.targets.Length) return;
        if (cityTargets.targets[index].IsCollected) return;
        pickedTarget = index;
        EnterState(State.Guiding);
    }

    void OnCityResetClicked()
    {
        if (Current != State.CityList) return;
        GameProgress.ResetPieces(GameMode.InCity);
        RefreshCityList();
    }

    void OnBackToTargets()
    {
        if (Current == State.Guiding || Current == State.CityCamera) EnterState(State.CityList);
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

    /// At the spot. In the Editor always, so the flow can be tried without walking.
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

    void OnCityCollectClicked()
    {
        if (Current == State.CityCamera) pieces.Collect();
    }

    CityTarget PickedTarget =>
        pickedTarget >= 0 && pickedTarget < cityTargets.targets.Length ? cityTargets.targets[pickedTarget] : null;

    /// The target list: the arch's progress, each target's status (found, or how far it is).
    void RefreshCityList()
    {
        nextListRefresh = Time.unscaledTime + ListRefreshSeconds;
        var targets = cityTargets.targets;
        int count = Mathf.Min(targets.Length, AppUI.CityTargetSlots);
        var names = new string[count];
        var statuses = new string[count];
        var canPick = new bool[count];
        for (int i = 0; i < count; i++)
        {
            var target = targets[i];
            names[i] = target.DisplayName;
            canPick[i] = !target.IsCollected;
            if (target.IsCollected)
            {
                statuses[i] = "Found";
                continue;
            }
            double distance = guide.DistanceTo(target);
            string what = target.kind == CityTargetKind.Academy ? "Where it stood" : "A piece of the arch";
            statuses[i] = double.IsNaN(distance) ? what : $"{what}, {GeoMath.FormatDistance(distance)} away";
        }
        int collected = GameProgress.CollectedCount(GameMode.InCity);
        ui.SetCityTargets(i => GameProgress.IsCollected(GameMode.InCity, i), collected, GameProgress.PieceCount,
            names, statuses, canPick, showReset: collected == GameProgress.PieceCount);
    }

    /// The guide screen, from far to near: distance and compass direction, then the dial with warmer /
    /// colder, then "Open the camera".
    void RefreshGuide()
    {
        if (Current == State.CityCamera)
        {
            CloseCameraIfWalkedAway();
            return;
        }
        if (Current != State.Guiding || PickedTarget == null) return;
        var target = PickedTarget;
        string stage, big, small, accuracy = "";
        int trend = 0;
        var look = GuideDial.Look.Far;
        bool pointer = false;

        switch (guide.CurrentStage)
        {
            case CityGuide.Stage.Far:
                stage = "Walk towards it";
                big = GeoMath.FormatDistance(guide.DistanceMetres);
                small = "to the " + GeoMath.CompassName(guide.Bearing);
                break;
            case CityGuide.Stage.Near:
                stage = "It's close. Follow the dial";
                look = GuideDial.Look.Near;
                pointer = guide.HasHeading;
                trend = guide.CurrentTrend == CityGuide.Trend.Warmer ? 1
                    : guide.CurrentTrend == CityGuide.Trend.Colder ? -1 : 0;
                big = trend > 0 ? "Warmer" : trend < 0 ? "Colder" : "Close";
                small = guide.HasHeading
                    ? GeoMath.RelativeDirection(guide.RelativeAngle)
                    : "to the " + GeoMath.CompassName(guide.Bearing);
                break;
            case CityGuide.Stage.Arrived:
                stage = "You're there";
                look = GuideDial.Look.Arrived;
                big = "Here";
                small = target.kind == CityTargetKind.Academy
                    ? "Open the camera to see the academy"
                    : "Open the camera and look around for the piece";
                break;
            default:
                stage = "Find it in the city";
                big = "...";
                small = guide.LocationUnavailable
                    ? "Location is off: switch it on in the phone's quick settings"
                    : "Finding your position";
                break;
        }
        if (guide.CurrentStage != CityGuide.Stage.NoFix)
            accuracy = $"Your position ({guide.Source}) is precise to {guide.Accuracy:0} m";
        if (Time.unscaledTime < walkedAwayNoteUntil && guide.CurrentStage != CityGuide.Stage.Arrived)
            stage = "The camera closed: you walked away from it";
        ui.SetGuide(target.DisplayName, stage, big, small, trend, accuracy, look, pointer, guide.RelativeAngle,
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
        Debug.Log($"[AppController] Walked away from {PickedTarget?.name} ({guide.DistanceMetres:0} m): " +
                  "camera closed.");
        walkedAwayNoteUntil = Time.unscaledTime + WalkedAwayNoteSeconds;
        EnterState(State.Guiding);
#endif
    }

    /// The in-city camera screen: locating, placing, then the academy or the piece (aim, collect).
    void RefreshCityCamera()
    {
        if (Current != State.CityCamera || PickedTarget == null) return;
        var target = PickedTarget;
        bool piece = target.kind == CityTargetKind.ArchPiece;
        string hint;
        bool inSight = false, showCollect = false, canCollect = false, showRetry = false;

        switch (cityContent.CurrentStatus)
        {
            case CityContent.Status.Locating:
                hint = float.IsInfinity(cityContent.HorizontalAccuracy)
                    ? "Point the camera at the buildings and the street around you"
                    : $"Finding your exact place: {cityContent.HorizontalAccuracy:0} m and " +
                      $"{cityContent.HeadingAccuracy:0}{TextSymbols.Degree} (needs " +
                      $"{cityContent.MaxHorizontalAccuracy:0} m and {cityContent.MaxHeadingAccuracy:0}" +
                      $"{TextSymbols.Degree}). Keep looking around";
                break;
            case CityContent.Status.Resolving:
                hint = piece ? "Placing the piece..." : "Placing the academy...";
                break;
            case CityContent.Status.Failed:
                hint = $"Couldn't place it here ({cityContent.FailedReason}). Look around, then try again";
                showRetry = true;
                break;
            default:
                if (!piece)
                    hint = "The academy stood here. Walk around it";
                else if (GameProgress.IsCollected(GameMode.InCity, target.piece) && !pieces.HasPiece)
                    hint = $"{target.DisplayName} found: {GameProgress.CollectedCount(GameMode.InCity)} of " +
                           $"{GameProgress.PieceCount} pieces. Tap \"Targets\" for the next one";
                else if (pieces.IsAimed)
                {
                    hint = "In sight: tap \"Collect\"";
                    inSight = true;
                }
                else if (pieces.TryGetPieceDirection(out float angle, out float distance, out bool onScreen))
                    hint = onScreen
                        ? $"Aim the centre of the screen at the piece ({distance:0} m away)"
                        : $"The piece is {GeoMath.RelativeDirection(angle)} ({distance:0} m)";
                else
                    hint = "Collecting...";
                showCollect = piece && pieces.HasPiece;
                canCollect = pieces.IsAimed;
                break;
        }
        ui.SetCityAR(hint, inSight, showCollect, canCollect, showRetry);
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
                else EnterState(cityCameraWanted ? State.CityCamera : State.CityList);
                break;

            case ARStartupGate.Status.LocationReady:
                if ((Current == State.Starting || Current == State.Blocked) && Mode == GameMode.InCity)
                    EnterState(State.CityList);
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
        ui.SetScanHint(placeFailedHint);
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
        pendingExploreHint = editCancelledHint;
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
            : "no heading yet";
        string height = $"{adjust.Height:+0.00;-0.00;0.00} m";
        ui.SetAdjustReadout(size, front, height);
        ui.SetScaleValue(adjust.Scale);
        ui.SetFaceSouthEnabled(adjust.CanFaceSouth);
        ui.SetPlatformButtonText(adjust.PlatformVisible ? hideBaseText : showBaseText);
    }

    // Exploring: the arch pieces
    void OnEditClicked()
    {
        if (Current != State.Exploring) return;
        editing = true;
        EnterState(State.Adjusting); // AdjustController.Begin remembers the model as it is now
    }

    void OnSpawnPieceClicked()
    {
        if (Current == State.Exploring) pieces.Spawn();
    }

    void OnCollectPieceClicked()
    {
        if (Current == State.Exploring) pieces.Collect();
    }

    void OnResetPiecesClicked()
    {
        if (Current == State.Exploring) pieces.ResetProgress();
    }

    /// The arch, the count, the hint ("The piece is behind you (2.4 m)") and which buttons can be used.
    void RefreshPiecesPanel()
    {
        if (Current != State.Exploring) return;

        bool inSight = false;
        string hint;
        if (pendingExploreHint != null)
            hint = pendingExploreHint;
        else if (pieces.AllCollected)
            hint = $"All {pieces.Total} pieces found: the arch is complete";
        else if (pieces.IsSpawning)
            hint = "Placing a piece...";
        else if (!pieces.TryGetPieceDirection(out float angle, out float distance, out bool onScreen))
            hint = "Tap \"Spawn piece\", then find it around you";
        else if (pieces.IsAimed)
        {
            hint = "In sight: tap \"Collect\"";
            inSight = true;
        }
        else if (onScreen)
            hint = $"Aim the centre of the screen at the piece ({distance:0.0} m away)";
        else
            hint = $"The piece is {GeoMath.RelativeDirection(angle)} ({distance:0.0} m)";

        ui.SetPieces(i => GameProgress.IsCollected(Mode, i), pieces.Collected, pieces.Total, hint, inSight,
                     canSpawn: !pieces.AllCollected && !pieces.IsSpawning,
                     canCollect: pieces.IsAimed, complete: pieces.AllCollected);
    }

    void RefreshOcclusionButton()
    {
        bool unsupported = occlusion.Support == UnityEngine.XR.ARSubsystems.Supported.Unsupported;
        string text = unsupported ? occlusionUnsupportedText : occlusion.IsOn ? occlusionOnText : occlusionOffText;
        ui.SetOcclusionButton(text, enabled: !unsupported);
    }

    void OnOcclusionClicked()
    {
        if (Current != State.Exploring || occlusion.Support == UnityEngine.XR.ARSubsystems.Supported.Unsupported)
            return;
        occlusion.SetOn(!occlusion.IsOn); // raises Changed -> RefreshOcclusionButton
    }

    // States
    void EnterState(State next)
    {
        if (Current == State.SessionEnded) return; // final: nothing leaves this state

        bool changed = next != Current;
        Current = next;

        if (next != State.Adjusting)
        {
            adjust.End();
            editing = false;
        }
        if (next != State.CityCamera && cityContent.CurrentStatus != CityContent.Status.Idle)
            cityContent.Clear(); // the in-city academy or piece goes away
        if (next != State.Exploring && next != State.CityCamera)
        {
            pieces.End(); // the piece that is out goes away; the progress stays saved
            pendingExploreHint = null;
        }
        // The guide also runs with the camera on, to notice the user walking away.
        if (next != State.Guiding && next != State.CityCamera) guide.End();
        farSince = -1f;
        // The camera is off on the in-city screens without AR (the session is paused, if it runs).
        if (next == State.CityList || next == State.Guiding) startupGate.SetARPaused(true);
        else if (next == State.Scanning || next == State.CityCamera) startupGate.SetARPaused(false);
        if (footprint != null) footprint.SetVisible(next == State.Adjusting); // only while adjusting
        ApplySeeThrough();
        ui.SetLeaveConfirmVisible(false);

        switch (next)
        {
            case State.ChoosingMode:
                placement.EndSearch();
                placement.SetPlanesVisible(false);
                ui.SetFreeModeProgress(i => GameProgress.IsCollected(GameMode.Free, i),
                    GameProgress.CollectedCount(GameMode.Free), GameProgress.PieceCount);
                ui.SetCityModeProgress(i => GameProgress.IsCollected(GameMode.InCity, i),
                    GameProgress.CollectedCount(GameMode.InCity), GameProgress.PieceCount);
                ui.ShowScreen(AppScreen.Mode);
                break;

            case State.CityList:
                placement.EndSearch();
                placement.SetPlanesVisible(false);
                RefreshCityList();
                ui.ShowScreen(AppScreen.City);
                break;

            case State.Guiding:
                ui.ShowScreen(AppScreen.Guide);
                guide.Begin(PickedTarget); // raises Changed -> RefreshGuide
                break;

            case State.CityCamera:
                placement.EndSearch();
                placement.SetPlanesVisible(false);
                ui.ShowScreen(AppScreen.CityAR);
                guide.Begin(PickedTarget);
                cityContent.Show(PickedTarget); // raises Changed -> RefreshCityCamera
                break;

            case State.Starting:
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
                ui.SetScanHint(scanHint);
                ui.SetPlaceEnabled(false);
                ui.ShowScreen(AppScreen.Scan);
                break;

            case State.Placing:
                ui.SetScanHint(aimHint);
                ui.SetPlaceEnabled(!placement.IsPlacing);
                ui.ShowScreen(AppScreen.Scan);
                break;

            case State.Adjusting:
                placement.EndSearch();
                placement.SetPlanesVisible(false); // the model is easier to see without them
                ui.SetAdjustHint(adjustHint);
                ui.SetAdjustEditing(editing);
                ui.ShowScreen(AppScreen.Adjust);
                adjust.Begin(); // raises Changed -> RefreshAdjustPanel
                break;

            case State.Exploring:
                RefreshOcclusionButton();
                ui.ShowScreen(AppScreen.Explore);
                pieces.Begin(Mode); // raises Changed -> RefreshPiecesPanel
                break;
        }

        if (changed) StateChanged?.Invoke(next);
    }
}
