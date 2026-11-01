using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// The Place step: finds the ground at the centre of the screen, shows the reticle there and, on
/// Place(), creates an AR anchor at that point with the model on it (tabletop size, front facing the user).
///
/// It has no UI of its own: AppController starts and stops the search, calls Place() and Clear(),
/// and listens to GroundChanged / Placed / PlaceFailed.
///
/// Anchor and model: the anchor sits on the ground at the reticle point, its +Z pointing at the user
/// (level, at placement time). The model is its child with offset 0 and yaw +90 degrees, which turns the
/// model's front (-X) towards +Z. Adjust will change only the model's local transform, and that local
/// transform is what gets saved (the "offset from the anchor").
/// </summary>
public class PlacementController : MonoBehaviour
{
    [Header("AR (XR Origin and Main Camera)")]
    [SerializeField] ARRaycastManager raycastManager;
    [SerializeField] ARAnchorManager anchorManager;
    [SerializeField] Camera arCamera;

    [Header("Placement")]
    [Tooltip("The academy prefab (not the FBX).")]
    [SerializeField] GameObject modelPrefab;
    [SerializeField] PlacementReticle reticle;
    [Tooltip("Model scale when first placed. 1 = the model's own size (about 0.7 m): tabletop.")]
    [SerializeField] float initialScale = 1f;
    [Tooltip("Seconds the ground may be lost before the reticle hides (stops flicker).")]
    [SerializeField] float groundLostDelay = 0.5f;
    [Tooltip("Layer of the AR plane prefab. Planes are hidden by taking this layer out of the camera's " +
             "culling mask, so detection and raycasts keep working.")]
    [SerializeField] string planeLayer = "ARPlanes";

#pragma warning disable 0414 // read only in the Editor
    [Header("Editor")]
    [Tooltip("Play mode only (there is no AR in the Editor): pretend there is ground 1.2 m below the " +
             "camera and 1.5 m ahead, so the Place flow can be tried.")]
    [SerializeField] bool fakeGroundInEditor = true;
#pragma warning restore 0414

    /// <summary>Raised while searching, when ground appears under the reticle (true) or is lost (false).</summary>
    public event Action<bool> GroundChanged;
    /// <summary>Place() finished: the anchor and the model exist.</summary>
    public event Action Placed;
    /// <summary>Place() could not create the anchor (or there was no ground).</summary>
    public event Action PlaceFailed;

    public bool IsSearching { get; private set; }
    public bool HasGround { get; private set; }
    public bool IsPlacing { get; private set; }
    public bool HasModel => model != null;
    /// <summary>The anchor's transform (a local, cloud or geospatial anchor; in the Editor a stand-in).</summary>
    public Transform AnchorTransform => anchorRoot;
    /// <summary>
    /// The local AR anchor made by Place() (null for restored anchors): the one a cloud anchor is hosted from.
    /// </summary>
    public ARAnchor LocalAnchor => arAnchor;
    public Transform Model => model;

    [Tooltip("Child of the model that 'Hide base' hides.")]
    [SerializeField] string platformChild = "Platform";

    /// <summary>Whether the model's base (platform) is shown.</summary>
    public bool PlatformVisible
    {
        get
        {
            var platform = model != null ? model.Find(platformChild) : null;
            var r = platform != null ? platform.GetComponentInChildren<Renderer>(true) : null;
            return r == null || r.enabled;
        }
    }

    public void SetPlatformVisible(bool visible)
    {
        var platform = model != null ? model.Find(platformChild) : null;
        if (platform == null) return;
        foreach (var r in platform.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
    }

    /// Turns the model's front (-X) towards the anchor's +Z.
    static readonly Quaternion FrontTowardsAnchorZ = Quaternion.Euler(0f, 90f, 0f);

    static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    Pose groundPose;
    float lastGroundTime;
    ARAnchor arAnchor;
    Transform anchorRoot;
    Transform model;
    int placeVersion; // bumped by Clear(), so a Place() still in progress is thrown away

    // Search
    /// <summary>Starts looking for ground and showing the reticle.</summary>
    public void BeginSearch()
    {
        IsSearching = true;
        HasGround = false;
    }

    /// <summary>Stops looking for ground and hides the reticle.</summary>
    public void EndSearch()
    {
        IsSearching = false;
        HasGround = false;
        if (reticle != null) reticle.Hide();
    }

    void Update()
    {
        if (!IsSearching) return;

        if (TryGetGround(out var pose))
        {
            groundPose = pose;
            lastGroundTime = Time.unscaledTime;
            if (reticle != null) reticle.Show(pose);
            SetHasGround(true);
        }
        else if (HasGround && Time.unscaledTime - lastGroundTime > groundLostDelay)
        {
            if (reticle != null) reticle.Hide();
            SetHasGround(false);
        }
    }

    /// <summary>Shows or hides the detected planes (they stay detected).</summary>
    public void SetPlanesVisible(bool visible)
    {
        int layer = LayerMask.NameToLayer(planeLayer);
        if (arCamera == null || layer < 0) return;
        if (visible) arCamera.cullingMask |= 1 << layer;
        else arCamera.cullingMask &= ~(1 << layer);
    }

    void SetHasGround(bool value)
    {
        if (HasGround == value) return;
        HasGround = value;
        GroundChanged?.Invoke(value);
    }

    /// Ground under the centre of the screen: the nearest upward-facing horizontal plane.
    bool TryGetGround(out Pose pose)
    {
        pose = default;
        if (arCamera == null) return false;

#if UNITY_EDITOR
        if (fakeGroundInEditor && ARSession.state < ARSessionState.SessionInitializing)
        {
            var cameraTransform = arCamera.transform;
            Vector3 point = cameraTransform.position + Flat(cameraTransform.forward) * 1.5f + Vector3.down * 1.2f;
            pose = new Pose(point, FacingCamera(point));
            return true;
        }
#endif

        if (raycastManager == null || raycastManager.subsystem == null) return false;

        var screenCentre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (!raycastManager.Raycast(screenCentre, hits, TrackableType.PlaneWithinPolygon)) return false;

        foreach (var hit in hits) // nearest first
        {
            // Skip ceilings and walls (the plane manager detects horizontal planes facing down too).
            if (hit.trackable is ARPlane plane && plane.alignment == PlaneAlignment.HorizontalUp)
            {
                pose = new Pose(hit.pose.position, FacingCamera(hit.pose.position));
                return true;
            }
        }
        return false;
    }

    /// Level rotation whose +Z points from <paramref name="point"/> towards the camera.
    Quaternion FacingCamera(Vector3 point)
    {
        Vector3 toCamera = Flat(arCamera.transform.position - point);
        if (toCamera == Vector3.zero) toCamera = -Flat(arCamera.transform.forward);
        return toCamera == Vector3.zero ? Quaternion.identity : Quaternion.LookRotation(toCamera, Vector3.up);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized;
    }

    // Place / clear
    /// <summary>
    /// Anchors the model where the reticle is. Raises Placed or PlaceFailed when done
    /// (creating an anchor can take a few frames).
    /// </summary>
    public async void Place()
    {
        if (IsPlacing || HasModel) return;
        if (modelPrefab == null)
        {
            Debug.LogError("[PlacementController] Assign the model prefab.", this);
            PlaceFailed?.Invoke();
            return;
        }
        if (!HasGround)
        {
            PlaceFailed?.Invoke();
            return;
        }

        IsPlacing = true;
        int version = ++placeVersion;
        Pose pose = groundPose;

        ARAnchor newAnchor = null;
        Transform newRoot = null;
        try
        {
            if (anchorManager != null && anchorManager.enabled && anchorManager.subsystem != null)
            {
                var result = await anchorManager.TryAddAnchorAsync(pose);
                if (result.status.IsSuccess() && result.value != null)
                {
                    newAnchor = result.value;
                    newRoot = newAnchor.transform;
                }
                else
                {
                    Debug.LogWarning($"[PlacementController] The anchor could not be created ({result.status}).");
                }
            }
#if UNITY_EDITOR
            else
            {
                // No AR in the Editor: a plain object stands in for the anchor.
                newRoot = new GameObject("Anchor (Editor stand-in)").transform;
                newRoot.SetPositionAndRotation(pose.position, pose.rotation);
            }
#endif
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }

        if (version != placeVersion)
        {
            // Clear() was called while the anchor was being made: throw it away.
            RemoveAnchor(newAnchor, newRoot);
            return;
        }
        IsPlacing = false;

        if (newRoot == null)
        {
            PlaceFailed?.Invoke();
            return;
        }

        arAnchor = newAnchor;
        anchorRoot = newRoot;
        model = Instantiate(modelPrefab, anchorRoot).transform;
        model.localPosition = Vector3.zero;
        model.localRotation = FrontTowardsAnchorZ;
        model.localScale = Vector3.one * initialScale;

        string anchorName = arAnchor != null ? arAnchor.trackableId.ToString() : "Editor stand-in";
        Debug.Log($"[PlacementController] Placed at {pose.position} (anchor: {anchorName}).");
        Placed?.Invoke();
    }

    /// <summary>
    /// Puts the model on an anchor made elsewhere (a resolved cloud anchor or, in the Editor, a
    /// stand-in), with a saved local transform. Replaces any current model.
    /// </summary>
    public void AttachModel(Transform anchor, Vector3 localPosition, Quaternion localRotation, float scale,
                            bool platformVisible)
    {
        Clear();
        if (anchor == null || modelPrefab == null) return;

        anchorRoot = anchor;
        arAnchor = anchor.GetComponent<ARAnchor>(); // null for cloud anchors
        model = Instantiate(modelPrefab, anchorRoot).transform;
        model.localPosition = localPosition;
        model.localRotation = localRotation;
        model.localScale = Vector3.one * scale;
        SetPlatformVisible(platformVisible);
    }

    /// <summary>Removes the model and its anchor (also cancels a Place() in progress).</summary>
    public void Clear()
    {
        placeVersion++;
        IsPlacing = false;

        if (model != null) Destroy(model.gameObject);
        model = null;

        RemoveAnchor(arAnchor, anchorRoot);
        arAnchor = null;
        anchorRoot = null;
    }

    void RemoveAnchor(ARAnchor anchor, Transform root)
    {
        // The anchor manager destroys the anchor's GameObject once ARCore has removed it.
        // Cloud anchors aren't ARAnchors: destroying their GameObject detaches them.
        if (anchor != null && anchorManager != null && anchorManager.enabled && anchorManager.subsystem != null &&
            anchorManager.TryRemoveAnchor(anchor))
            return;
        if (root != null) Destroy(root.gameObject);
    }
}
