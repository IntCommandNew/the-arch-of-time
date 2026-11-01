using System;
using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Saves the placement and brings it back on the next launch.
///
/// Only the camera can find the exact spot again, so the building is restored ONLY from a cloud
/// anchor (ARCore recognising the saved spot in the camera image). The location (Geospatial) is
/// meters off, so it is never used to place the building: it only guides the user to the spot
/// ("about 25 m ahead, slightly to the right").
///
/// Save (after "Done"):
///   - a cloud anchor hosted from the local anchor (required: without it nothing is saved). Works
///     best after walking around the spot: see EstimateQuality();
///   - the spot's latitude/longitude when roughly known (for the guidance only);
///   - the model's local transform under the anchor, its scale and base visibility.
///   Saving again for the same anchor (after Edit) only rewrites the model part: no new upload.
/// Restore (at launch, if a save exists): search the cloud anchor until it is found (the user can
///   point the camera at the spot any time), until ARCore reports an error, or until cancelled.
///
/// No UI of its own: AppController starts Save / Restore and listens to the results.
/// </summary>
public class PersistenceController : MonoBehaviour
{
    [SerializeField] PlacementController placement;
    [SerializeField] ARAnchorManager anchorManager;
    [SerializeField] AREarthManager earthManager;
    [SerializeField] Camera arCamera;

    [Header("Saving")]
    [Tooltip("Cloud anchor lifetime. Keyless auth allows up to 365.")]
    [SerializeField] int cloudAnchorTtlDays = 365;
    [SerializeField] float hostTimeoutSeconds = 60f;
    [Tooltip("The spot's location is saved (for guidance only) when known to within this many metres.")]
    [SerializeField] float maxSaveLocationAccuracy = 30f;

    [Header("Restoring")]
    [Tooltip("How long to wait for AR tracking before starting the search anyway.")]
    [SerializeField] float sessionWaitSeconds = 20f;
    [Tooltip("Guidance is given when the user's location is known to within this many metres.")]
    [SerializeField] float maxGuideLocationAccuracy = 50f;
    [Tooltip("Directions relative to the camera ('ahead', 'to your left') need a heading this good (degrees); " +
             "otherwise compass points are used.")]
    [SerializeField] float maxGuideHeadingAccuracy = 30f;
    [SerializeField] float guidanceRefreshSeconds = 0.5f;

    public enum SaveOutcome { Saved, SavedWithoutLocation, UpdatedOnly, Failed }
    public enum RestoreSource { None, CloudAnchor, Editor }

    public readonly struct SaveResult
    {
        public readonly SaveOutcome Outcome;
        public readonly string Problem; // why it failed, or what is missing (null if all went well)
        public SaveResult(SaveOutcome outcome, string problem) { Outcome = outcome; Problem = problem; }
    }

    public readonly struct RestoreResult
    {
        public readonly RestoreSource Source;
        public readonly string Problem; // why it failed (Source == None)
        public RestoreResult(RestoreSource source, string problem) { Source = source; Problem = problem; }
    }

    /// <summary>Status text while saving ("Uploading...") or the guidance while restoring.</summary>
    public event Action<string> Progress;
    public event Action<SaveResult> SaveFinished;
    public event Action<RestoreResult> RestoreFinished;

    public bool IsBusy { get; private set; }
    public static bool HasSave => PlacementStore.Load() != null;

    /// <summary>
    /// The current anchor is the saved one (saved or restored this session): saving only updates the model part.
    /// </summary>
    public bool IsCurrentAnchorSaved =>
        savedAnchor != null && placement != null && savedAnchor == placement.AnchorTransform;

    /// <summary>A cloud anchor will be uploaded on Save, so walking around the spot first helps.</summary>
    public bool NeedsWalkAround => !IsCurrentAnchorSaved && CanHost;

    Transform savedAnchor;
    int operation; // bumped by Cancel(), so a running save / restore stops and is thrown away

    bool CanHost => anchorManager != null && placement != null && placement.LocalAnchor != null &&
                    ARSession.state == ARSessionState.SessionTracking;

    /// <summary>Stops a save or restore in progress (no result is raised).</summary>
    public void Cancel()
    {
        operation++;
        IsBusy = false;
    }

    /// <summary>Forgets which anchor is saved (the model was removed; the save file stays).</summary>
    public void ForgetCurrentAnchor() => savedAnchor = null;

    /// <summary>How well ARCore knows the area around the anchor from where the camera is now.</summary>
    public FeatureMapQuality EstimateQuality()
    {
        if (!CanHost || arCamera == null) return FeatureMapQuality.Insufficient;
        return anchorManager.EstimateFeatureMapQualityForHosting(SessionSpace.FromWorld(arCamera.transform));
    }

    // Save
    public async void Save()
    {
        if (IsBusy) return;
        if (placement == null || !placement.HasModel)
        {
            SaveFinished?.Invoke(new SaveResult(SaveOutcome.Failed, "There is nothing placed to save."));
            return;
        }

        IsBusy = true;
        int op = ++operation;
        var previous = PlacementStore.Load();

        var saved = new SavedPlacement
        {
            savedAtUtc = DateTime.UtcNow.ToString("o"),
            modelLocalPosition = placement.Model.localPosition,
            modelLocalRotation = placement.Model.localRotation,
            modelScale = placement.Model.localScale.x,
            platformVisible = placement.PlatformVisible,
        };

#if UNITY_EDITOR
        if (ARSession.state < ARSessionState.SessionInitializing)
        {
            // No AR in the Editor: a pretend cloud anchor id, so the save / restore flow can be tried.
            saved.cloudAnchorId = "EDITOR";
            saved.cloudAnchorExpiresUtc = DateTime.UtcNow.AddDays(cloudAnchorTtlDays).ToString("o");
            FinishSave(op, saved, SaveOutcome.Saved, null);
            return;
        }
#endif

        if (IsCurrentAnchorSaved && previous != null)
        {
            // Same anchor as the save: keep its anchor data, update only the model part.
            CopyAnchorData(previous, saved);
            if (!saved.hasLocation) TryFillLocation(saved);
            FinishSave(op, saved, SaveOutcome.UpdatedOnly, null);
            return;
        }

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            FinishSave(op, null, SaveOutcome.Failed,
                "Saving needs an internet connection. You can keep exploring; it just won't be there next time.");
            return;
        }
        if (placement.LocalAnchor == null || anchorManager == null)
        {
            FinishSave(op, null, SaveOutcome.Failed,
                "There is no anchor to save. Use \"New location\" and place it again.");
            return;
        }

        bool hasLocation = TryFillLocation(saved);

        Progress?.Invoke("Uploading the spot...");
        var promise = anchorManager.HostCloudAnchorAsync(placement.LocalAnchor, cloudAnchorTtlDays);
        float giveUpAt = Time.unscaledTime + hostTimeoutSeconds;
        while (promise.State == PromiseState.Pending)
        {
            if (Time.unscaledTime > giveUpAt) { promise.Cancel(); break; }
            await Awaitable.NextFrameAsync();
            if (op != operation) { promise.Cancel(); return; }
        }

        var result = promise.State == PromiseState.Done ? promise.Result : null;
        if (result == null || result.CloudAnchorState != CloudAnchorState.Success ||
            string.IsNullOrEmpty(result.CloudAnchorId))
        {
            string state = result != null ? result.CloudAnchorState.ToString() : "timed out";
            Debug.LogWarning($"[PersistenceController] Hosting failed: {state}");
            FinishSave(op, null, SaveOutcome.Failed,
                $"The spot could not be uploaded ({state}). Walk around it a bit more and try again.");
            return;
        }

        saved.cloudAnchorId = result.CloudAnchorId;
        saved.cloudAnchorExpiresUtc = DateTime.UtcNow.AddDays(cloudAnchorTtlDays).ToString("o");
        Debug.Log($"[PersistenceController] Cloud anchor hosted: {saved.cloudAnchorId}");
        FinishSave(op, saved, hasLocation ? SaveOutcome.Saved : SaveOutcome.SavedWithoutLocation,
            hasLocation ? null : "Your location wasn't known, so there will be no directions to it next time.");
    }

    void FinishSave(int op, SavedPlacement saved, SaveOutcome outcome, string problem)
    {
        if (op != operation) return;
        IsBusy = false;

        if (saved != null)
        {
            if (PlacementStore.Save(saved))
            {
                savedAnchor = placement.AnchorTransform;
                Debug.Log($"[PersistenceController] Saved ({outcome}). Cloud anchor: {saved.cloudAnchorId}, " +
                          $"location: {saved.hasLocation}.");
            }
            else
            {
                outcome = SaveOutcome.Failed;
                problem = "The save file could not be written on this phone.";
            }
        }
        SaveFinished?.Invoke(new SaveResult(outcome, problem));
    }

    /// The anchor's latitude/longitude (for guidance only), if the location is roughly known.
    bool TryFillLocation(SavedPlacement saved)
    {
        if (placement.AnchorTransform == null || !TryGetCameraPose(out var camera, maxSaveLocationAccuracy, 360f))
            return false;

        var anchorPose = earthManager.Convert(SessionSpace.FromWorld(placement.AnchorTransform));
        saved.hasLocation = true;
        saved.latitude = anchorPose.Latitude;
        saved.longitude = anchorPose.Longitude;
        saved.locationAccuracy = (float)camera.HorizontalAccuracy;
        return true;
    }

    static void CopyAnchorData(SavedPlacement from, SavedPlacement to)
    {
        to.cloudAnchorId = from.cloudAnchorId;
        to.cloudAnchorExpiresUtc = from.cloudAnchorExpiresUtc;
        to.hasLocation = from.hasLocation;
        to.latitude = from.latitude;
        to.longitude = from.longitude;
        to.locationAccuracy = from.locationAccuracy;
    }

    // Restore
    public async void Restore()
    {
        if (IsBusy) return;
        var saved = PlacementStore.Load();
        if (saved == null)
        {
            RestoreFinished?.Invoke(new RestoreResult(RestoreSource.None, "Nothing is saved."));
            return;
        }

        IsBusy = true;
        int op = ++operation;

#if UNITY_EDITOR
        if (ARSession.state < ARSessionState.SessionInitializing)
        {
            // No AR in the Editor: a stand-in anchor where the fake ground is (see PlacementController).
            var standIn = new GameObject("Anchor (Editor stand-in, restored)").transform;
            var cameraTransform = arCamera.transform;
            Vector3 forward = cameraTransform.forward; forward.y = 0f; forward.Normalize();
            standIn.SetPositionAndRotation(cameraTransform.position + forward * 1.5f + Vector3.down * 1.2f,
                                           Quaternion.LookRotation(-forward, Vector3.up));
            Attach(standIn, saved);
            FinishRestore(op, new RestoreResult(RestoreSource.Editor, null));
            return;
        }
#endif

        if (saved.CloudAnchorExpired)
        {
            FinishRestore(op, new RestoreResult(RestoreSource.None,
                "The saved spot has expired (they last up to 365 days). Place it again."));
            return;
        }
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            FinishRestore(op, new RestoreResult(RestoreSource.None,
                "Finding the saved building needs an internet connection."));
            return;
        }

        // ARCore Extensions has no session handle until AR is running; a resolve started before that
        // returns an empty promise at once. Restore starts as soon as AR is switched on, so wait.
        Progress?.Invoke("Starting the camera...");
        float trackingBy = Time.unscaledTime + sessionWaitSeconds;
        while (ARSession.state != ARSessionState.SessionTracking && Time.unscaledTime < trackingBy)
        {
            await Awaitable.NextFrameAsync();
            if (op != operation) return;
        }

        // No time limit: ARCore keeps trying while the promise is pending, so the user can walk to
        // the spot (guided by the location) and point the camera at it whenever they get there.
        float start = Time.unscaledTime;
        float nextGuidance = 0f;
        var promise = anchorManager.ResolveCloudAnchorAsync(saved.cloudAnchorId);
        while (promise.State == PromiseState.Pending)
        {
            if (Time.unscaledTime >= nextGuidance)
            {
                nextGuidance = Time.unscaledTime + guidanceRefreshSeconds;
                Progress?.Invoke(Guidance(saved));
            }
            await Awaitable.NextFrameAsync();
            if (op != operation) { promise.Cancel(); return; }
        }

        var result = promise.State == PromiseState.Done ? promise.Result : null;
        if (result != null && result.CloudAnchorState == CloudAnchorState.Success && result.Anchor != null)
        {
            Attach(result.Anchor.transform, saved);
            Debug.Log($"[PersistenceController] Restored from the cloud anchor after {Time.unscaledTime - start:0} s.");
            FinishRestore(op, new RestoreResult(RestoreSource.CloudAnchor, null));
            return;
        }

        string state = result != null ? result.CloudAnchorState.ToString() : "cancelled";
        Debug.LogWarning($"[PersistenceController] Cloud anchor not resolved: {state}");
        FinishRestore(op, new RestoreResult(RestoreSource.None, $"The saved spot couldn't be looked up ({state})."));
    }

    void Attach(Transform anchor, SavedPlacement saved)
    {
        placement.AttachModel(anchor, saved.modelLocalPosition, saved.modelLocalRotation, saved.modelScale,
                              saved.platformVisible);
        savedAnchor = anchor;
    }

    void FinishRestore(int op, RestoreResult result)
    {
        if (op != operation) return;
        IsBusy = false;
        RestoreFinished?.Invoke(result);
    }

    // Guidance
    /// <summary>
    /// "About 25 m ahead, slightly to the right (+/-8 m). Point the camera at where it was." Relative to
    /// where the camera looks when the heading is good, compass points otherwise.
    /// </summary>
    string Guidance(SavedPlacement saved)
    {
        const string pointAtIt = "Point the camera at where the building was and move slowly around the spot.";
        if (!saved.hasLocation || !TryGetCameraPose(out var camera, maxGuideLocationAccuracy, 360f))
            return pointAtIt;

        double distance = GeoMath.Distance(camera.Latitude, camera.Longitude, saved.latitude, saved.longitude);
        double uncertainty = camera.HorizontalAccuracy + saved.locationAccuracy;
        if (distance <= Math.Max(uncertainty, 5.0))
            return "You're close. " + pointAtIt;

        float bearing = GeoMath.InitialBearing(camera.Latitude, camera.Longitude, saved.latitude, saved.longitude);
        bool headingKnown = camera.OrientationYawAccuracy <= maxGuideHeadingAccuracy;
        string where = headingKnown && TryGetCameraHeading(out float heading)
            ? GeoMath.RelativeDirection(Mathf.DeltaAngle(heading, bearing))
            : $"to the {GeoMath.CompassPoint(bearing)}";
        return $"The saved spot is about {GeoMath.FormatDistance(distance)} {where} " +
               $"({TextSymbols.PlusMinus}{uncertainty:0} m). " +
               "Walk there, then point the camera at where the building was.";
    }

    /// Compass direction the camera faces (the top of the phone when it looks straight down).
    bool TryGetCameraHeading(out float heading)
    {
        heading = 0f;
        var cameraTransform = arCamera.transform;
        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) { forward = cameraTransform.up; forward.y = 0f; }
        if (forward.sqrMagnitude < 1e-6f) return false;

        var herePose = new Pose(cameraTransform.position, Quaternion.identity);
        var aheadPose = new Pose(cameraTransform.position + forward.normalized * 5f, Quaternion.identity);
        var here = earthManager.Convert(SessionSpace.FromWorld(herePose));
        var ahead = earthManager.Convert(SessionSpace.FromWorld(aheadPose));
        heading = GeoMath.InitialBearing(here.Latitude, here.Longitude, ahead.Latitude, ahead.Longitude);
        return true;
    }

    // Geospatial
    bool TryGetCameraPose(out GeospatialPose pose, float maxHorizontal, float maxYaw)
    {
        pose = default;
        if (earthManager == null || !earthManager.enabled) return false;
        if (ARSession.state != ARSessionState.SessionTracking) return false;
        if (earthManager.EarthTrackingState != TrackingState.Tracking) return false;
        pose = earthManager.CameraGeospatialPose;
        return pose.HorizontalAccuracy <= maxHorizontal && pose.OrientationYawAccuracy <= maxYaw;
    }
}
