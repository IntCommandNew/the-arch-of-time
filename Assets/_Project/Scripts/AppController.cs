using System;
using UnityEngine;

/// <summary>
/// The app's state machine: decides which state the app is in and keeps the UI in sync.
/// Scripts report to it (startup gate, placement, adjusting, saving / restoring), and it tells
/// AppUI what to show. AppUI and the AR scripts never talk to each other directly.
///
/// States: Starting -> (Blocked) -> Restoring (if a placement is saved) or Scanning;
///         Scanning <-> Placing -> Adjusting -> ("Done") Saving -> Exploring <-> ("Edit") Adjusting;
///         "Place again" (Adjusting), "New location" (Exploring) and "Place a new one" (Restoring)
///         -> Scanning; SessionEnded (final) if a permission is revoked.
/// </summary>
public class AppController : MonoBehaviour
{
    public enum State
    {
        Starting,      // permissions / ARCore checks
        Blocked,       // AR can't run yet; the user must act
        Restoring,     // a placement is saved: guiding the user to it until the camera recognises it
        Scanning,      // AR running, no ground under the reticle yet
        Placing,       // ground under the reticle: "Place here" is enabled
        Adjusting,     // the model is placed: move / turn / size / height
        Saving,        // after "Done": walk around the spot, then save (or "Not now")
        Exploring,     // placement confirmed: walk around; Edit or New location
        Collecting,    // arch pieces test mode (from Scanning / Placing / Restoring; Back returns there)
        SessionEnded,  // a permission was revoked: AR stopped, only "Close app" is left
    }

    [SerializeField] ARStartupGate startupGate;
    [SerializeField] PlacementController placement;
    [SerializeField] AdjustController adjust;
    [SerializeField] PersistenceController persistence;
    [SerializeField] DepthOcclusion occlusion;
    [SerializeField] FootprintOutline footprint;
    [SerializeField] NearFade modelFade;
    [SerializeField] ArchPieces pieces;
    [SerializeField] AppUI ui;

    [Tooltip("See-through while adjusting, until the user switches it off (remembered for the session).")]
    [SerializeField] bool seeThroughWhileAdjusting = true;

    [Header("Texts")]
    [SerializeField] string scanHint = "Move your phone slowly over the ground";
    [SerializeField] string aimHint = "Aim the ring at the spot, then tap \"Place here\"";
    [SerializeField] string placeFailedHint = "Couldn't place it there. Aim at the ground and try again";
    [SerializeField] string adjustHint = "Drag to move " + TextSymbols.MiddleDot + " twist with two fingers to turn";
    [SerializeField] string exploreHint = "Walk around the building";
    [SerializeField] string editCancelledHint = "Changes discarded";
    [SerializeField] string occlusionOffText = "Hidden by real objects: Off";
    [SerializeField] string occlusionOnText = "Hidden by real objects: On";
    [SerializeField] string occlusionUnsupportedText = "Hidden by real objects: not on this phone";
    [SerializeField] string hideBaseText = "Hide base";
    [SerializeField] string showBaseText = "Show base";

    [Header("Texts: saving")]
    [SerializeField]
    string walkAroundText =
        "Walk slowly around the spot where you placed it, keeping it in view. This helps the app find it again.";
    [SerializeField] string savingText = "Saving...";
    [SerializeField] string saveFailedTitle = "Couldn't save";
    [SerializeField] string savedHint = "Saved. It will be here next time";
    [SerializeField]
    string savedWithoutLocationHint =
        "Saved. (Your location wasn't known, so no directions to it next time)";
    [SerializeField] string savedUpdateHint = "Changes saved";
    [SerializeField] string notSavedHint = "Not saved. Use Edit, then Done, to save it";

    [Header("Texts: restoring")]
    [SerializeField] string restoringTitle = "Finding your building...";
    [SerializeField] string restoreFailedTitle = "Couldn't find the saved building";
    [SerializeField] string restoredCloudHint = "Restored at the saved spot";

    [Header("Saving")]
    [Tooltip("Seconds between feature-map quality checks on the save screen.")]
    [SerializeField] float qualityCheckSeconds = 0.25f;

    string pendingExploreHint;
    float nextQualityCheck;
    bool editing; // Adjusting came from "Edit" (not from a new placement): "Cancel" can undo it
    State piecesReturnState = State.Scanning; // where "Back" goes from the arch pieces mode

    public State Current { get; private set; } = State.Starting;

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
        if (pieces != null) pieces.Changed += RefreshPiecesPanel;
        if (persistence != null)
        {
            persistence.Progress += OnPersistenceProgress;
            persistence.SaveFinished += OnSaveFinished;
            persistence.RestoreFinished += OnRestoreFinished;
        }
        if (ui != null)
        {
            ui.PiecesModeClicked += OnPiecesModeClicked;
            ui.PiecesBackClicked += OnPiecesBackClicked;
            ui.SpawnPieceClicked += OnSpawnPieceClicked;
            ui.CollectPieceClicked += OnCollectPieceClicked;
            ui.ResetPiecesClicked += OnResetPiecesClicked;
            ui.CancelEditClicked += OnCancelEditClicked;
            ui.SeeThroughClicked += OnSeeThroughClicked;
            ui.OcclusionClicked += OnOcclusionClicked;
            ui.SaveClicked += OnSaveClicked;
            ui.SaveSkipClicked += OnSaveSkipClicked;
            ui.RestoreRetryClicked += OnRestoreRetryClicked;
            ui.RestoreNewClicked += OnRestoreNewClicked;
            ui.RetryClicked += OnRetryClicked;
            ui.OpenSettingsClicked += OnOpenSettingsClicked;
            ui.CloseClicked += OnCloseClicked;
            ui.PlaceClicked += OnPlaceClicked;
            ui.PlaceAgainClicked += OnPlaceAgainClicked;
            ui.AdjustCommandIssued += OnAdjustCommand;
            ui.DoneClicked += OnDoneClicked;
            ui.EditClicked += OnEditClicked;
            ui.NewLocationClicked += OnNewLocationClicked;
            ui.NewLocationAnswered += OnNewLocationAnswered;
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
        if (pieces != null) pieces.Changed -= RefreshPiecesPanel;
        if (persistence != null)
        {
            persistence.Progress -= OnPersistenceProgress;
            persistence.SaveFinished -= OnSaveFinished;
            persistence.RestoreFinished -= OnRestoreFinished;
        }
        if (ui != null)
        {
            ui.PiecesModeClicked -= OnPiecesModeClicked;
            ui.PiecesBackClicked -= OnPiecesBackClicked;
            ui.SpawnPieceClicked -= OnSpawnPieceClicked;
            ui.CollectPieceClicked -= OnCollectPieceClicked;
            ui.ResetPiecesClicked -= OnResetPiecesClicked;
            ui.CancelEditClicked -= OnCancelEditClicked;
            ui.SeeThroughClicked -= OnSeeThroughClicked;
            ui.OcclusionClicked -= OnOcclusionClicked;
            ui.SaveClicked -= OnSaveClicked;
            ui.SaveSkipClicked -= OnSaveSkipClicked;
            ui.RestoreRetryClicked -= OnRestoreRetryClicked;
            ui.RestoreNewClicked -= OnRestoreNewClicked;
            ui.RetryClicked -= OnRetryClicked;
            ui.OpenSettingsClicked -= OnOpenSettingsClicked;
            ui.CloseClicked -= OnCloseClicked;
            ui.PlaceClicked -= OnPlaceClicked;
            ui.PlaceAgainClicked -= OnPlaceAgainClicked;
            ui.AdjustCommandIssued -= OnAdjustCommand;
            ui.DoneClicked -= OnDoneClicked;
            ui.EditClicked -= OnEditClicked;
            ui.NewLocationClicked -= OnNewLocationClicked;
            ui.NewLocationAnswered -= OnNewLocationAnswered;
        }
    }

    void Start()
    {
        if (startupGate == null || placement == null || adjust == null || persistence == null ||
            occlusion == null || pieces == null || ui == null)
        {
            Debug.LogError("[AppController] Assign the ARStartupGate, PlacementController, AdjustController, " +
                           "PersistenceController, DepthOcclusion, ArchPieces and AppUI.", this);
            enabled = false;
            return;
        }
        ui.SetScaleRange(adjust.MinScale, adjust.MaxScale);
        ui.SetRealSizeEnabled(adjust.HasRealSize);
        OnGateStatusChanged(startupGate); // catch up with whatever the gate already reported
    }

    // Startup gate
    void OnGateStatusChanged(ARStartupGate gate)
    {
        switch (gate.CurrentStatus)
        {
            case ARStartupGate.Status.Starting:
                ui.SetStartingDetail(gate.Detail);
                EnterState(State.Starting);
                break;

            case ARStartupGate.Status.Blocked:
                ui.SetBlocked(gate.BlockedTitle, gate.BlockedMessage,
                    gate.CanRetry, gate.CanOpenSettings, gate.CanClose);
                EnterState(State.Blocked);
                break;

            case ARStartupGate.Status.Running:
                if (Current == State.Starting || Current == State.Blocked)
                    EnterState(PersistenceController.HasSave ? State.Restoring : State.Scanning);
                break;

            case ARStartupGate.Status.PermissionsLost:
                ui.SetBlocked(gate.BlockedTitle, gate.BlockedMessage,
                    showRetry: false, showSettings: false, showClose: true);
                EnterState(State.SessionEnded);
                break;
        }
    }

    /// <summary>
    /// Drops everything that belongs to the current AR session. Saved data is NOT touched.
    /// Called when a permission is revoked mid-session.
    /// </summary>
    void ClearSession()
    {
        adjust.End();
        pieces.End();
        persistence.Cancel();
        persistence.ForgetCurrentAnchor();
        placement.EndSearch();
        placement.Clear();
        Debug.Log("[AppController] Session cleared (saved data kept).");
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
        RemoveModelAndScan();
    }

    /// The model goes away (the save file stays until a new placement is saved).
    void RemoveModelAndScan()
    {
        persistence.Cancel();
        persistence.ForgetCurrentAnchor();
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
            case AdjustCommand.Tabletop: adjust.ApplyTabletop(); break;
            case AdjustCommand.RealSize: adjust.ApplyRealSize(); break;
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
        if (Current == State.Adjusting) EnterState(State.Saving);
    }

    /// Updates the readout, slider and buttons from the model's current state.
    void RefreshAdjustPanel()
    {
        if (Current != State.Adjusting) return;
        ui.SetAdjustReadout(DescribeAdjustment());
        ui.SetScaleValue(adjust.Scale);
        ui.SetFaceSouthEnabled(adjust.CanFaceSouth);
        ui.SetPlatformButtonText(adjust.PlatformVisible ? hideBaseText : showBaseText);
    }

    /// Scale, front bearing and height, for example "x1.0 . front 182 deg S (+/-15 deg) . height +0.00 m"
    /// (shown with the real times, degree, plus-minus and middle dot signs).
    string DescribeAdjustment()
    {
        string scale = TextSymbols.Times + adjust.Scale.ToString(adjust.Scale >= 10f ? "0" : "0.0#");
        string front = adjust.TryGetFrontBearing(out float bearing, out float accuracy)
            ? $"front {bearing:0}{TextSymbols.Degree} {GeoMath.CompassPoint(bearing)} " +
              $"({TextSymbols.PlusMinus}{accuracy:0}{TextSymbols.Degree})"
            : "front: no heading yet";
        string dot = "  " + TextSymbols.MiddleDot + "  ";
        return $"{scale}{dot}{front}{dot}height {adjust.Height:+0.00;-0.00;0.00} m";
    }

    // Exploring
    void OnEditClicked()
    {
        if (Current != State.Exploring) return;
        editing = true;
        EnterState(State.Adjusting); // AdjustController.Begin remembers the model as it is now
    }

    /// "Cancel" while editing: the model goes back to how it was before Edit; nothing is saved.
    void OnCancelEditClicked()
    {
        if (Current != State.Adjusting || !editing) return;
        adjust.RevertToStart();
        pendingExploreHint = editCancelledHint;
        EnterState(State.Exploring);
    }

    void OnOcclusionClicked()
    {
        if (Current != State.Exploring || occlusion.Support == UnityEngine.XR.ARSubsystems.Supported.Unsupported)
            return;
        occlusion.SetOn(!occlusion.IsOn); // raises Changed -> RefreshOcclusionButton
    }

    void RefreshOcclusionButton()
    {
        bool unsupported = occlusion.Support == UnityEngine.XR.ARSubsystems.Supported.Unsupported;
        string text = unsupported ? occlusionUnsupportedText : occlusion.IsOn ? occlusionOnText : occlusionOffText;
        ui.SetOcclusionButton(text, enabled: !unsupported);
    }

    void OnNewLocationClicked()
    {
        if (Current == State.Exploring) ui.SetNewLocationConfirmVisible(true);
    }

    void OnNewLocationAnswered(bool confirmed)
    {
        ui.SetNewLocationConfirmVisible(false);
        if (!confirmed || Current != State.Exploring) return;
        RemoveModelAndScan();
    }

    // Saving
    void Update()
    {
        // The walk-around meter on the save screen.
        if (Current != State.Saving || persistence.IsBusy || !persistence.NeedsWalkAround) return;
        if (Time.unscaledTime < nextQualityCheck) return;
        nextQualityCheck = Time.unscaledTime + qualityCheckSeconds;

        switch (persistence.EstimateQuality())
        {
            case Google.XR.ARCoreExtensions.FeatureMapQuality.Good:
                ui.SetSaveQuality(2, "Good: ready to save");
                break;
            case Google.XR.ARCoreExtensions.FeatureMapQuality.Sufficient:
                ui.SetSaveQuality(1, "Enough to save. A bit more walking makes it better");
                break;
            default:
                ui.SetSaveQuality(0, "Keep walking around the spot");
                break;
        }
    }

    void ShowWalkAround()
    {
        ui.SetSaveStatus(walkAroundText, busy: false);
        ui.SetSaveQuality(0, "Keep walking around the spot");
        nextQualityCheck = 0f;
    }

    void OnSaveClicked()
    {
        if (Current != State.Saving || persistence.IsBusy) return;
        StartSave();
    }

    void StartSave()
    {
        ui.SetSaveStatus(savingText, busy: true);
        persistence.Save(); // may finish right away (re-save of the same anchor)
    }

    void OnSaveSkipClicked()
    {
        if (Current != State.Saving || persistence.IsBusy) return;
        pendingExploreHint = notSavedHint;
        EnterState(State.Exploring);
    }

    void OnSaveFinished(PersistenceController.SaveResult result)
    {
        if (Current != State.Saving) return;
        if (result.Problem != null) Debug.Log($"[AppController] Save: {result.Outcome}. {result.Problem}");

        if (result.Outcome == PersistenceController.SaveOutcome.Failed)
        {
            // Stay here: the user can try again or continue without saving.
            ui.SetSaveStatus($"{saveFailedTitle}. {result.Problem}", busy: false);
            ui.SetSaveButtonText("Try again");
            return;
        }

        pendingExploreHint = result.Outcome switch
        {
            PersistenceController.SaveOutcome.Saved => savedHint,
            PersistenceController.SaveOutcome.SavedWithoutLocation => savedWithoutLocationHint,
            _ => savedUpdateHint,
        };
        EnterState(State.Exploring);
    }

    // Restoring
    void StartRestore()
    {
        ui.SetRestoreStatus(restoringTitle, "", showRetry: false);
        persistence.Restore();
    }

    void OnRestoreRetryClicked()
    {
        if (Current == State.Restoring && !persistence.IsBusy) StartRestore();
    }

    void OnRestoreNewClicked()
    {
        if (Current == State.Restoring) RemoveModelAndScan();
    }

    void OnPersistenceProgress(string text)
    {
        if (Current == State.Restoring) ui.SetRestoreStatus(restoringTitle, text, showRetry: false);
        else if (Current == State.Saving) ui.SetSaveStatus(text, busy: true);
    }

    void OnRestoreFinished(PersistenceController.RestoreResult result)
    {
        if (Current != State.Restoring) return;

        if (result.Source == PersistenceController.RestoreSource.None)
        {
            ui.SetRestoreStatus(restoreFailedTitle, result.Problem, showRetry: true);
            return;
        }
        pendingExploreHint = restoredCloudHint;
        EnterState(State.Exploring);
    }

    // Arch pieces (test mode)
    void OnPiecesModeClicked()
    {
        if (Current != State.Scanning && Current != State.Placing && Current != State.Restoring) return;
        piecesReturnState = Current == State.Restoring ? State.Restoring : State.Scanning;
        if (Current == State.Restoring) persistence.Cancel(); // restarts when coming back
        EnterState(State.Collecting);
    }

    void OnPiecesBackClicked()
    {
        if (Current == State.Collecting) EnterState(piecesReturnState);
    }

    void OnSpawnPieceClicked()
    {
        if (Current == State.Collecting) pieces.Spawn();
    }

    void OnCollectPieceClicked()
    {
        if (Current == State.Collecting) pieces.Collect();
    }

    void OnResetPiecesClicked()
    {
        if (Current == State.Collecting) pieces.ResetProgress();
    }

    /// Count, hint ("The piece is behind you (2.4 m)") and which buttons can be used.
    void RefreshPiecesPanel()
    {
        if (Current != State.Collecting) return;

        string hint;
        if (pieces.AllCollected)
            hint = $"All {pieces.Total} pieces collected! Tap \"Reset pieces\" to start again";
        else if (pieces.IsSpawning)
            hint = "Placing a piece...";
        else if (!pieces.TryGetPieceDirection(out float angle, out float distance, out bool onScreen))
            hint = "Tap \"Spawn piece\", then find it around you";
        else if (pieces.IsAimed)
            hint = "Got it in sight: tap \"Collect\"";
        else if (onScreen)
            hint = $"Aim the centre of the screen at the piece ({distance:0.0} m away)";
        else
            hint = $"The piece is {GeoMath.RelativeDirection(angle)} ({distance:0.0} m)";

        ui.SetPiecesStatus($"Arch pieces: {pieces.Collected} / {pieces.Total}", hint,
                           canSpawn: !pieces.AllCollected && !pieces.IsSpawning,
                           canCollect: pieces.IsAimed);
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
        if (footprint != null) footprint.SetVisible(next == State.Adjusting); // only while adjusting
        ApplySeeThrough();
        if (next != State.Exploring) ui.SetNewLocationConfirmVisible(false);
        if (next != State.Collecting) pieces.End();
        bool startSave = false, startRestore = false;

        switch (next)
        {
            case State.Collecting:
                placement.EndSearch();
                placement.SetPlanesVisible(false);
                ui.ShowScreen(AppScreen.Pieces);
                pieces.Begin(); // raises Changed -> RefreshPiecesPanel
                break;

            case State.Restoring:
                placement.EndSearch();
                placement.SetPlanesVisible(false);
                ui.ShowScreen(AppScreen.Restore);
                startRestore = true;
                break;

            case State.Saving:
                ui.SetSaveButtonText("Save");
                ui.ShowScreen(AppScreen.Save);
                if (persistence.NeedsWalkAround)
                {
                    ShowWalkAround();
                }
                else
                {
                    // Same anchor as the save (or the Editor): nothing to upload, save straight away.
                    ui.SetSaveQuality(-1, null);
                    startSave = true;
                }
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
                ui.SetExploreHint(pendingExploreHint ?? exploreHint);
                pendingExploreHint = null;
                RefreshOcclusionButton();
                ui.ShowScreen(AppScreen.Explore);
                break;
        }

        if (changed) StateChanged?.Invoke(next);

        // Started last: they can finish at once and move on to the next state.
        if (startSave) StartSave();
        if (startRestore) StartRestore();
    }
}
