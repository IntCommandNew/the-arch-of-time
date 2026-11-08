using System;
using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Guides the user to an in-city target while the camera is off, from the phone's location and compass.
///
/// Three stages, from far to near (the design: the closer, the more help, but never a map):
///   - Far: only the straight-line distance and a compass direction ("1.2 km, to the north-east").
///   - Near (within hotRadius): a needle pointing at the target and warmer / colder, no distance.
///   - Arrived (within arriveRadius): the camera can be opened to find the target in AR.
/// The radii are larger for the academy, which is about 70 m wide at real size.
///
/// Position: Android's location service (Input.location; it also works with the new Input System), or
/// ARCore's Geospatial pose when the AR view runs and is more accurate. Heading: the compass.
/// In the Editor there is no location: a fixed position (editorLatitude / editorLongitude) stands in.
/// No UI of its own: AppController reads it and shows the guide screen.
/// </summary>
public class CityGuide : MonoBehaviour
{
    public enum Stage
    {
        NoFix,    // no position yet (location starting, or switched off)
        Far,      // distance + compass direction
        Near,     // needle + warmer / colder
        Arrived,  // at the spot: the camera can be opened
    }

    public enum Trend { None, Warmer, Colder }

    [Tooltip("On the XR Origin. Used for the position while the AR view runs, when it is more accurate.")]
    [SerializeField] AREarthManager earthManager;

    [Header("Radii (metres)")]
    [SerializeField] float pieceHotRadius = 60f;
    [SerializeField] float pieceArriveRadius = 15f;
    [SerializeField] float academyHotRadius = 150f;
    [SerializeField] float academyArriveRadius = 60f;
    [Tooltip("With the camera on, it closes again beyond the arrive radius plus this share of it (at least " +
             "minLeaveMargin): pieces 30 m, the academy 90 m. The gap stops GPS noise from flipping it.")]
    [SerializeField] float leaveMarginFraction = 0.5f;
    [SerializeField] float minLeaveMargin = 15f;

    [Header("Warmer / colder")]
    [Tooltip("How much the distance must change (metres) before warmer or colder is shown.")]
    [SerializeField] float trendStep = 3f;
    [Tooltip("Seconds between Changed events.")]
    [SerializeField] float refreshSeconds = 0.25f;

#pragma warning disable 0414 // read only in the Editor
    [Header("Editor (no location service)")]
    [SerializeField] double editorLatitude = 47.1650;
    [SerializeField] double editorLongitude = 27.5790;
    [SerializeField] float editorHeading;
#pragma warning restore 0414

    /// <summary>Raised a few times a second while guiding.</summary>
    public event Action Changed;

    public CityTarget Target { get; private set; }
    public Stage CurrentStage { get; private set; } = Stage.NoFix;
    public Trend CurrentTrend { get; private set; } = Trend.None;
    public double DistanceMetres { get; private set; }
    /// <summary>Compass bearing from the user to the target (0 = north).</summary>
    public float Bearing { get; private set; }
    /// <summary>The phone's compass heading is known.</summary>
    public bool HasHeading { get; private set; }
    /// <summary>Where the target is from where the phone points (positive = to the right).</summary>
    public float RelativeAngle { get; private set; }
    /// <summary>How precise the position is (metres).</summary>
    public float Accuracy { get; private set; }
    /// <summary>"GPS" or "AR".</summary>
    public string Source { get; private set; } = "";
    /// <summary>The location service is off or was refused (the user must switch it on).</summary>
    public bool LocationUnavailable { get; private set; }
    /// <summary>Within this distance the camera can be opened (depends on the target).</summary>
    public float ArriveRadius =>
        Target != null && Target.kind == CityTargetKind.Academy ? academyArriveRadius : pieceArriveRadius;
    /// <summary>Beyond this distance an open camera closes again.</summary>
    public float LeaveRadius => ArriveRadius + Mathf.Max(minLeaveMargin, ArriveRadius * leaveMarginFraction);

    bool guiding;
    double trendReference = double.NaN;
    float nextRefresh;

    /// <summary>Starts guiding to a target (and the location service, if it isn't running).</summary>
    public void Begin(CityTarget target)
    {
        Target = target;
        guiding = true;
        CurrentStage = Stage.NoFix;
        CurrentTrend = Trend.None;
        trendReference = double.NaN;
        StartLocation();
        Refresh();
    }

    /// <summary>Stops guiding (the location service keeps running for the next target).</summary>
    public void End() => guiding = false;

    /// <summary>The distance to a target from the last known position, or NaN without a fix.</summary>
    public double DistanceTo(CityTarget target)
    {
        StartLocation();
        return TryGetPosition(out double latitude, out double longitude, out _, out _)
            ? GeoMath.Distance(latitude, longitude, target.latitude, target.longitude)
            : double.NaN;
    }

    /// <summary>Switches the location service and the compass off (leaving the in-city mode).</summary>
    public void StopLocation()
    {
        guiding = false;
        if (Input.location.status != LocationServiceStatus.Stopped) Input.location.Stop();
        Input.compass.enabled = false;
    }

    void StartLocation()
    {
#if !UNITY_EDITOR
        if (Input.location.status == LocationServiceStatus.Stopped ||
            Input.location.status == LocationServiceStatus.Failed)
            Input.location.Start(5f, 1f);
        Input.compass.enabled = true;
#endif
    }

    void OnDisable() => StopLocation();

    void Update()
    {
        if (!guiding || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + refreshSeconds;
        Refresh();
    }

    void Refresh()
    {
        if (Target == null) return;

        if (!TryGetPosition(out double latitude, out double longitude, out float accuracy, out string source))
        {
            CurrentStage = Stage.NoFix;
            Changed?.Invoke();
            return;
        }
        Accuracy = accuracy;
        Source = source;
        DistanceMetres = GeoMath.Distance(latitude, longitude, Target.latitude, Target.longitude);
        Bearing = GeoMath.InitialBearing(latitude, longitude, Target.latitude, Target.longitude);

        HasHeading = TryGetHeading(out float heading);
        RelativeAngle = HasHeading ? Mathf.DeltaAngle(heading, Bearing) : 0f;

        bool academy = Target.kind == CityTargetKind.Academy;
        float arrive = ArriveRadius;
        float hot = academy ? academyHotRadius : pieceHotRadius;
        var stage = DistanceMetres <= arrive ? Stage.Arrived : DistanceMetres <= hot ? Stage.Near : Stage.Far;

        // Warmer / colder: compared with the distance when it last changed by a whole step, so GPS noise
        // of a metre or two doesn't flip it.
        if (stage != Stage.Near)
        {
            CurrentTrend = Trend.None;
            trendReference = DistanceMetres;
        }
        else if (double.IsNaN(trendReference))
        {
            trendReference = DistanceMetres;
        }
        else if (Math.Abs(DistanceMetres - trendReference) >= trendStep)
        {
            CurrentTrend = DistanceMetres < trendReference ? Trend.Warmer : Trend.Colder;
            trendReference = DistanceMetres;
        }
        CurrentStage = stage;
        Changed?.Invoke();
    }

    /// The best position: ARCore's Geospatial pose while it tracks and beats the GPS, else the GPS.
    bool TryGetPosition(out double latitude, out double longitude, out float accuracy, out string source)
    {
        latitude = longitude = 0;
        accuracy = float.PositiveInfinity;
        source = "";
        LocationUnavailable = false;

#if UNITY_EDITOR
        latitude = editorLatitude;
        longitude = editorLongitude;
        accuracy = 5f;
        source = "Editor";
        return true;
#else
        bool found = false;
        var status = Input.location.status;
        if (status == LocationServiceStatus.Running)
        {
            var data = Input.location.lastData;
            latitude = data.latitude;
            longitude = data.longitude;
            accuracy = data.horizontalAccuracy;
            source = "GPS";
            found = true;
        }
        else if (status == LocationServiceStatus.Failed || !Input.location.isEnabledByUser)
        {
            LocationUnavailable = true;
        }

        if (earthManager != null && earthManager.enabled && ARSession.state == ARSessionState.SessionTracking &&
            earthManager.EarthTrackingState == TrackingState.Tracking)
        {
            var pose = earthManager.CameraGeospatialPose;
            if (pose.HorizontalAccuracy < accuracy)
            {
                latitude = pose.Latitude;
                longitude = pose.Longitude;
                accuracy = (float)pose.HorizontalAccuracy;
                source = "AR";
                found = true;
            }
        }
        if (found) LocationUnavailable = false;
        return found;
#endif
    }

    bool TryGetHeading(out float heading)
    {
#if UNITY_EDITOR
        heading = editorHeading;
        return true;
#else
        heading = 0f;
        if (!Input.compass.enabled || Input.compass.timestamp <= 0) return false;
        // trueHeading needs the location service; fall back to the magnetic one.
        heading = Input.compass.trueHeading != 0f ? Input.compass.trueHeading : Input.compass.magneticHeading;
        return true;
#endif
    }
}
