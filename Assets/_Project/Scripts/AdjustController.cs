using System;
using System.Collections.Generic;
using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// The Adjust step: moves, turns, sizes and raises the placed model.
///
/// Gestures (only while active; a touch that starts on the UI is ignored):
///   - one finger: drag the model along the ground (the anchor's level plane; it can go beyond the
///     detected planes). It follows the finger at the model's own distance: sideways on screen moves it
///     sideways, up on screen pushes it away, by as much as the finger moved over the model. (The first
///     version put the model where the finger's ray met the floor: with the finger on a big model's walls,
///     near the horizon, that point was far away and the model ran off.)
///   - two fingers: twist to turn it around its own centre, pinch / spread to make it smaller / bigger
///     (both at once are fine).
/// Commands (AppController calls these for the buttons): camera-relative nudges, turn by degrees,
/// size (slider and the presets Default size x1 / Max size x5), height, show/hide the base, face south.
///
/// The free placement mode is for a floor or a table, indoors (user, 8 Nov 2026): sizes go up to x5.
/// Life size (x35, about 25 m) was tried and dropped here: indoors such a building is drawn over the walls,
/// its base sits near the horizon above the visible floor and it can't look grounded. The academy at life
/// size is the in-city mode's job (CityContent).
///
/// Everything changes only the model's LOCAL transform under the anchor (see the anchor convention
/// in PlacementController).
/// </summary>
public class AdjustController : MonoBehaviour
{
    [SerializeField] PlacementController placement;
    [SerializeField] Camera arCamera;
    [Tooltip("Only used to ignore touches that start on the UI.")]
    [SerializeField] AppUI ui;
    [Tooltip("For the front bearing and 'Face south' (on the XR Origin).")]
    [SerializeField] AREarthManager earthManager;

    [Header("Size")]
    [SerializeField] float minScale = 0.25f;
    [Tooltip("The largest size, also the \"Max size\" preset (x5: about 3.6 x 3 m, 2 m tall).")]
    [SerializeField] float maxScale = 5f;
    [Tooltip("Street level in the model at scale 1, in metres above the platform's underside: where the " +
             "paved ground meets the building's walls (measured all round the footprint: 0.0147; the " +
             "figure stands on raised grass at 0.05). The 'Street' height button lowers the model by this " +
             "x scale, so the building's walls stand on the real ground.")]
    [SerializeField] float streetLevel = 0.0147f;

    [Header("Dragging")]
    [Tooltip("Height of the model at scale 1 (metres): the drag is measured at the model's middle.")]
    [SerializeField] float modelHeight = 0.428f;
    [Tooltip("The drag is measured at least this far from the camera (metres).")]
    [SerializeField] float minDragDepth = 0.5f;
    [Tooltip("Half the model's width at scale 1 (metres). The drag is measured at least this x scale away, so a " +
             "big model right in front of the camera still moves by about its width per screen swipe.")]
    [SerializeField] float modelHalfWidth = 0.36f;

    [Header("Steps (metres at scale 1; they grow with the scale)")]
    [SerializeField] float nudgeStep = 0.02f;
    [SerializeField] float heightStep = 0.005f;

    [Header("Heading")]
    [Tooltip("Worst Geospatial heading accuracy (degrees) at which the front bearing is shown and 'Face south' works.")]
    [SerializeField] float maxHeadingAccuracy = 25f;
    [Tooltip("While active, Changed is also raised this often so the bearing stays current.")]
    [SerializeField] float refreshSeconds = 0.5f;

    /// <summary>The model's size, position, rotation or the heading changed.</summary>
    public event Action Changed;

    public bool IsActive { get; private set; }
    public float Scale => model != null ? model.localScale.x : 1f;
    public float MinScale => minScale;
    public float MaxScale => maxScale;
    /// <summary>Height of the model's base above the anchor's ground, in metres.</summary>
    public float Height => model != null ? model.localPosition.y : 0f;
    public bool PlatformVisible => placement == null || placement.PlatformVisible;

    Transform model;
    Transform anchor;
    float nextRefresh;

    // The model as it was when Begin() was called, for RevertToStart() ("Cancel" after Edit).
    Vector3 startPosition;
    Quaternion startRotation;
    Vector3 startScale;
    bool startPlatformVisible;

    // Gesture state
    readonly HashSet<int> gestureTouchIds = new HashSet<int>(); // touches that began outside the UI
    readonly List<Touch> gestureTouches = new List<Touch>();
    int dragTouchId = -1;
    Vector2 lastDragScreen;   // the dragging finger's previous screen position
    float dragDepth;          // the model's distance in front of the camera when the drag began
    bool twisting;
    float lastTwistAngle;
    float lastPinchDistance;
    bool waitForRelease;      // after a twist, no drag until every finger is lifted

    void OnEnable()
    {
        EnhancedTouchSupport.Enable();
#if UNITY_EDITOR
        // The mouse acts as one finger in Play mode.
        UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Enable();
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Disable();
#endif
        EnhancedTouchSupport.Disable();
        ResetGestures();
    }

    // Start / stop
    /// <summary>Starts adjusting the model that PlacementController placed.</summary>
    public void Begin()
    {
        model = placement != null ? placement.Model : null;
        anchor = placement != null ? placement.AnchorTransform : null;
        IsActive = model != null && anchor != null;
        if (IsActive)
        {
            startPosition = model.localPosition;
            startRotation = model.localRotation;
            startScale = model.localScale;
            startPlatformVisible = PlatformVisible;
        }
        ResetGestures();
        NotifyChanged();
    }

    /// <summary>Puts the model back exactly as it was when Begin() was called (Cancel after Edit).</summary>
    public void RevertToStart()
    {
        if (model == null) return;
        model.localPosition = startPosition;
        model.localRotation = startRotation;
        model.localScale = startScale;
        placement.SetPlatformVisible(startPlatformVisible);
        NotifyChanged();
    }

    /// <summary>Stops gestures (the model stays where it is).</summary>
    public void End()
    {
        IsActive = false;
        ResetGestures();
    }

    // Commands
    /// <summary>Moves one step relative to the camera: x = right, y = away from the camera.</summary>
    public void Nudge(Vector2 direction)
    {
        if (!CanEdit()) return;
        Vector3 right = Flat(arCamera.transform.right);
        Vector3 away = Flat(arCamera.transform.forward);
        if (away == Vector3.zero) away = Flat(arCamera.transform.up); // looking straight down
        Vector3 world = (right * direction.x + away * direction.y) * (nudgeStep * Scale);
        Vector3 local = anchor.InverseTransformVector(world);
        local.y = 0f;
        model.localPosition += local;
        NotifyChanged();
    }

    /// <summary>Turns around the model's own centre; positive = clockwise seen from above.</summary>
    public void Rotate(float degreesClockwise)
    {
        if (!CanEdit()) return;
        model.localRotation = Quaternion.Euler(0f, degreesClockwise, 0f) * model.localRotation;
        NotifyChanged();
    }

    /// <summary>
    /// Sets the size around the model's pivot (the base's underside), with its height scaled along: a base on
    /// the floor stays on the floor, a model lowered to street level stays at street level.
    /// </summary>
    public void SetScale(float scale)
    {
        if (!CanEdit()) return;
        float old = Scale;
        scale = Mathf.Clamp(scale, minScale, maxScale);
        if (Mathf.Approximately(scale, old)) return;
        model.localScale = Vector3.one * scale;
        var position = model.localPosition;
        position.y *= scale / old;
        model.localPosition = position;
        NotifyChanged();
    }

    /// <summary>Default size preset: the model's own size (x1), standing on its base where it is.</summary>
    public void ApplyDefaultSize() => ApplyPreset(1f);

    /// <summary>Max size preset: the largest size (x5), standing on its base where it is.</summary>
    public void ApplyMaxSize() => ApplyPreset(maxScale);

    void ApplyPreset(float scale)
    {
        if (!CanEdit()) return;
        model.localScale = Vector3.one * Mathf.Clamp(scale, minScale, maxScale);
        SetHeightValue(0f);
    }

    /// <summary>Raises (+1) or lowers (-1) the model by one step.</summary>
    public void ChangeHeight(float direction)
    {
        if (!CanEdit()) return;
        SetHeightValue(Height + Mathf.Sign(direction) * heightStep * Scale);
    }

    /// <summary>Lowers the model so its street level is on the ground.</summary>
    public void PutStreetLevelOnGround()
    {
        if (!CanEdit()) return;
        SetHeightValue(-streetLevel * Scale);
    }

    /// <summary>Puts the underside of the base on the ground.</summary>
    public void PutBaseOnGround()
    {
        if (!CanEdit()) return;
        SetHeightValue(0f);
    }

    public void SetPlatformVisible(bool visible)
    {
        if (!CanEdit()) return;
        placement.SetPlatformVisible(visible);
        NotifyChanged();
    }

    /// <summary>Turns the model so its front faces south (needs a good Geospatial heading).</summary>
    public void FaceSouth()
    {
        if (TryGetFrontBearing(out float bearing, out _))
            Rotate(Mathf.DeltaAngle(bearing, 180f));
    }

    public bool CanFaceSouth => IsActive && TryGetFrontBearing(out _, out _);

    void SetHeightValue(float height)
    {
        var position = model.localPosition;
        position.y = height;
        model.localPosition = position;
        NotifyChanged();
    }

    bool CanEdit() => IsActive && model != null && anchor != null && arCamera != null;

    void NotifyChanged()
    {
        nextRefresh = Time.unscaledTime + refreshSeconds;
        Changed?.Invoke();
    }

    // Front bearing
    /// <summary>
    /// Compass bearing (0 = north, 90 = east) the model's front (-X) faces. Worked out from the
    /// latitude/longitude of two points (the model and a point in front of it), so it doesn't depend
    /// on rotation conventions. False while Geospatial isn't tracking or the heading is too uncertain.
    /// </summary>
    public bool TryGetFrontBearing(out float bearing, out float accuracy)
    {
        bearing = 0f;
        accuracy = 0f;
        if (model == null || earthManager == null || !earthManager.enabled) return false;
        if (ARSession.state != ARSessionState.SessionTracking) return false;
        if (earthManager.EarthTrackingState != TrackingState.Tracking) return false;

        accuracy = (float)earthManager.CameraGeospatialPose.OrientationYawAccuracy;
        if (accuracy > maxHeadingAccuracy) return false;

        Vector3 front = Flat(-model.right);
        if (front == Vector3.zero) return false;
        var here = earthManager.Convert(SessionSpace.FromWorld(new Pose(model.position, Quaternion.identity)));
        var aheadPose = new Pose(model.position + front * 5f, Quaternion.identity);
        var ahead = earthManager.Convert(SessionSpace.FromWorld(aheadPose));
        bearing = GeoMath.InitialBearing(here.Latitude, here.Longitude, ahead.Latitude, ahead.Longitude);
        return true;
    }

    // Gestures
    void Update()
    {
        if (!IsActive) return;
        if (model == null || anchor == null) { End(); return; }

        if (Time.unscaledTime >= nextRefresh) NotifyChanged(); // keeps the bearing current

        CollectGestureTouches();

        if (gestureTouches.Count == 0)
        {
            ResetGestures();
            return;
        }

        if (gestureTouches.Count >= 2)
        {
            dragTouchId = -1;
            waitForRelease = true;
            TwistAndPinch(gestureTouches[0].screenPosition, gestureTouches[1].screenPosition);
            return;
        }

        twisting = false;
        if (!waitForRelease) Drag(gestureTouches[0]);
    }

    /// Keeps the touches that started outside the UI and are still down.
    void CollectGestureTouches()
    {
        gestureTouches.Clear();
        foreach (var touch in Touch.activeTouches)
        {
            int id = touch.touchId;
            if (touch.phase == TouchPhase.Began && (ui == null || !ui.IsOverUI(touch.screenPosition)))
                gestureTouchIds.Add(id);

            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                gestureTouchIds.Remove(id);
                continue;
            }
            if (gestureTouchIds.Contains(id)) gestureTouches.Add(touch);
        }
    }

    void Drag(Touch touch)
    {
        Vector2 screen = touch.screenPosition;
        if (touch.touchId != dragTouchId)
        {
            dragTouchId = touch.touchId;
            lastDragScreen = screen;
            dragDepth = ModelDepth();
            return;
        }
        MoveByScreenDrag(lastDragScreen, screen, dragDepth);
        lastDragScreen = screen;
    }

    /// The depth a drag is measured at: how far in front of the camera the model's centre is, but at
    /// least half the model's width (at real size the user may stand inside the building, where its centre
    /// is only a metre or two away and the 25 m building would crawl) and at least minDragDepth.
    public float ModelDepth()
    {
        if (model == null || arCamera == null) return minDragDepth;
        Vector3 centre = model.position + anchor.up * (0.5f * modelHeight * Scale);
        var cameraTransform = arCamera.transform;
        float depth = Vector3.Dot(centre - cameraTransform.position, cameraTransform.forward);
        return Mathf.Max(minDragDepth, modelHalfWidth * Scale, depth);
    }

    /// <summary>
    /// Moves the model along the ground by a finger movement on screen, measured at the given depth:
    /// the screen's right is the camera's right on the ground, the screen's up is away from the camera.
    /// </summary>
    public void MoveByScreenDrag(Vector2 from, Vector2 to, float depth)
    {
        if (!CanEdit() || (to - from).sqrMagnitude < 1e-6f) return;
        var cameraTransform = arCamera.transform;
        Vector3 a = arCamera.ScreenToWorldPoint(new Vector3(from.x, from.y, depth));
        Vector3 b = arCamera.ScreenToWorldPoint(new Vector3(to.x, to.y, depth));
        Vector3 moved = b - a;
        Vector3 right = Flat(cameraTransform.right);
        Vector3 away = Flat(cameraTransform.forward);
        if (away == Vector3.zero) away = Flat(cameraTransform.up); // looking straight down
        Vector3 world = right * Vector3.Dot(moved, cameraTransform.right) +
                        away * Vector3.Dot(moved, cameraTransform.up);
        Vector3 local = anchor.InverseTransformVector(world);
        local.y = 0f;
        model.localPosition += local;
        NotifyChanged();
    }

    /// Two fingers: the change of their angle turns the model, the change of their distance sizes it
    /// (spreading by 10 % makes it 10 % bigger; SetScale keeps the street level where it is).
    void TwistAndPinch(Vector2 a, Vector2 b)
    {
        float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        float distance = Vector2.Distance(a, b);
        if (!twisting)
        {
            twisting = true;
            lastTwistAngle = angle;
            lastPinchDistance = distance;
            return;
        }
        float delta = Mathf.DeltaAngle(lastTwistAngle, angle);
        lastTwistAngle = angle;
        // Fingers turning counter-clockwise on screen turn the model counter-clockwise seen from above.
        if (Mathf.Abs(delta) > 0.01f) Rotate(-delta);

        if (lastPinchDistance > 1f && distance > 1f)
        {
            float ratio = distance / lastPinchDistance;
            if (Mathf.Abs(ratio - 1f) > 0.001f) SetScale(Scale * ratio);
        }
        lastPinchDistance = distance;
    }

    void ResetGestures()
    {
        gestureTouchIds.Clear();
        gestureTouches.Clear();
        dragTouchId = -1;
        twisting = false;
        waitForRelease = false;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized;
    }
}
