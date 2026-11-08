using System;
using System.Collections;
using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Puts an in-city target in the AR view, at the place authored with Geospatial Creator (CityTargets).
///
/// Show(target) waits until ARCore's Geospatial pose is good enough (Earth tracking, horizontal and
/// heading accuracy within the limits below), then resolves a geospatial anchor the way Google's
/// Geospatial Creator would (Terrain / Rooftop / WGS84, the authored altitude and East-Up-North rotation).
/// The Creator's own anchors resolve as soon as Earth tracking starts, with no accuracy check, which can
/// put things metres off: that's why the anchors are made here instead.
/// On the anchor, with the same local transform as the authoring scene's previews:
///   - the academy at real size (through PlacementController, so light, shadow and fade follow it);
///   - a piece (through ArchPieces, which handles aiming and collecting).
/// In the Editor (no AR) a stand-in anchor is put in front of the camera.
/// No UI of its own: AppController shows the progress.
/// </summary>
public class CityContent : MonoBehaviour
{
    public enum Status
    {
        Idle,       // nothing to show
        Locating,   // waiting for an accurate enough Geospatial pose
        Resolving,  // the anchor is being made
        Shown,      // the target is in the AR view
        Failed,     // the anchor could not be made (FailedReason says why)
    }

    [Header("AR (XR Origin)")]
    [SerializeField] AREarthManager earthManager;
    [SerializeField] ARAnchorManager anchorManager;
    [SerializeField] Camera arCamera;

    [Header("Content")]
    [SerializeField] PlacementController placement;
    [SerializeField] ArchPieces pieces;
    [Tooltip("The academy's scale when the target doesn't set one (CityTarget.scale, authored on the map).")]
    [SerializeField] float academyScale = 35f;
    [Tooltip("Street level in the model at scale 1 (AdjustController.streetLevel): it goes on the ground.")]
    [SerializeField] float academyStreetLevel = 0.0147f;

    [Header("Accuracy needed before the anchor is made")]
    [SerializeField] float maxHorizontalAccuracy = 8f;
    [SerializeField] float maxHeadingAccuracy = 15f;

    /// <summary>Status or accuracy changed.</summary>
    public event Action Changed;

    public Status CurrentStatus { get; private set; } = Status.Idle;
    public CityTarget Target { get; private set; }
    public string FailedReason { get; private set; } = "";
    /// <summary>The current Geospatial accuracy (metres, degrees); infinite while Earth isn't tracking.</summary>
    public float HorizontalAccuracy { get; private set; } = float.PositiveInfinity;
    public float HeadingAccuracy { get; private set; } = float.PositiveInfinity;
    public float MaxHorizontalAccuracy => maxHorizontalAccuracy;
    public float MaxHeadingAccuracy => maxHeadingAccuracy;

    int version; // bumped by Clear(), so an anchor still being made is thrown away
    float nextRefresh;

    /// <summary>Starts looking for the target's place; the content appears once the pose is good enough.</summary>
    public void Show(CityTarget target)
    {
        Clear();
        Target = target;
        SetStatus(Status.Locating);
    }

    /// <summary>Removes the content and its anchor.</summary>
    public void Clear()
    {
        version++;
        if (Target != null)
        {
            if (Target.kind == CityTargetKind.Academy) placement.Clear();
            else pieces.End();
        }
        Target = null;
        FailedReason = "";
        SetStatus(Status.Idle);
    }

    /// <summary>After a failure: try again.</summary>
    public void Retry()
    {
        if (CurrentStatus == Status.Failed && Target != null) Show(Target);
    }

    void Update()
    {
        if (CurrentStatus != Status.Locating) return;
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;

#if UNITY_EDITOR
        if (anchorManager == null || anchorManager.subsystem == null)
        {
            StartCoroutine(MakeEditorStandIn(version));
            return;
        }
#endif
        UpdateAccuracy();
        Changed?.Invoke();
        if (HorizontalAccuracy <= maxHorizontalAccuracy && HeadingAccuracy <= maxHeadingAccuracy)
            StartCoroutine(Resolve(Target, version));
    }

    void UpdateAccuracy()
    {
        HorizontalAccuracy = HeadingAccuracy = float.PositiveInfinity;
        if (earthManager == null || ARSession.state != ARSessionState.SessionTracking ||
            earthManager.EarthState != EarthState.Enabled ||
            earthManager.EarthTrackingState != TrackingState.Tracking) return;
        var pose = earthManager.CameraGeospatialPose;
        HorizontalAccuracy = (float)pose.HorizontalAccuracy;
        HeadingAccuracy = (float)pose.OrientationYawAccuracy;
    }

    IEnumerator Resolve(CityTarget target, int forVersion)
    {
        SetStatus(Status.Resolving);
        ARGeospatialAnchor anchor = null;
        string error = null;

        switch (target.altitudeType)
        {
            case CityTargetAltitude.Terrain:
                {
                    var promise = anchorManager.ResolveAnchorOnTerrainAsync(
                        target.latitude, target.longitude, target.altitude, target.eunRotation);
                    yield return promise;
                    var result = promise.Result;
                    if (result.TerrainAnchorState == TerrainAnchorState.Success) anchor = result.Anchor;
                    else error = result.TerrainAnchorState.ToString();
                    break;
                }
            case CityTargetAltitude.Rooftop:
                {
                    var promise = anchorManager.ResolveAnchorOnRooftopAsync(
                        target.latitude, target.longitude, target.altitude, target.eunRotation);
                    yield return promise;
                    var result = promise.Result;
                    if (result.RooftopAnchorState == RooftopAnchorState.Success) anchor = result.Anchor;
                    else error = result.RooftopAnchorState.ToString();
                    break;
                }
            default:
                anchor = anchorManager.AddAnchor(target.latitude, target.longitude, target.altitude,
                    target.eunRotation);
                if (anchor == null) error = "AddAnchor failed";
                break;
        }

        if (forVersion != version)
        {
            if (anchor != null) Destroy(anchor.gameObject); // cleared meanwhile
            yield break;
        }
        if (anchor == null)
        {
            FailedReason = error ?? "unknown";
            Debug.LogWarning($"[CityContent] The anchor for {target.name} could not be made ({FailedReason}).");
            SetStatus(Status.Failed);
            yield break;
        }

        Debug.Log($"[CityContent] {target.name} anchored (accuracy {HorizontalAccuracy:0.0} m, " +
                  $"heading {HeadingAccuracy:0.0} deg).");
        Attach(target, anchor.transform);
    }

#if UNITY_EDITOR
    /// No AR in the Editor: a plain object in front of the camera stands in for the anchor.
    IEnumerator MakeEditorStandIn(int forVersion)
    {
        SetStatus(Status.Resolving);
        yield return new WaitForSeconds(1f); // so the locating screen can be seen
        if (forVersion != version) yield break;
        var cameraTransform = arCamera != null ? arCamera.transform : null;
        Vector3 origin = cameraTransform != null ? cameraTransform.position : Vector3.zero;
        Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        float distance = Target.kind == CityTargetKind.Academy ? 60f : 5f;
        var root = new GameObject("City anchor (Editor stand-in)").transform;
        root.SetPositionAndRotation(origin + forward * distance + Vector3.down * 1.4f,
            Quaternion.LookRotation(-forward, Vector3.up));
        Attach(Target, root);
    }
#endif

    void Attach(CityTarget target, Transform root)
    {
        if (target.kind == CityTargetKind.Academy)
        {
            // The size authored on the map; the building's bottom (street level) on the anchor, no platform.
            float scale = target.scale > 0f ? target.scale : academyScale;
            placement.ShowOn(root, new Vector3(0f, -academyStreetLevel * scale, 0f), Quaternion.identity, scale);
            placement.SetPlatformVisible(false);
        }
        else
        {
            pieces.Begin(GameMode.InCity);
            pieces.ShowOn(root, target.piece);
        }
        SetStatus(Status.Shown);
    }

    void SetStatus(Status status)
    {
        CurrentStatus = status;
        Changed?.Invoke();
    }
}
