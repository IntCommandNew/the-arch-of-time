using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// ARCore Extensions' native calls (AREarthManager.Convert, EstimateFeatureMapQualityForHosting) take
/// poses in AR session space: the XR Origin's "Trackables" space, where anchors live. Unity world space
/// only matches it while the XR Origin sits at the world origin, so convert explicitly.
/// </summary>
public static class SessionSpace
{
    static Transform trackables;

    static Transform Trackables
    {
        get
        {
            if (trackables == null)
            {
                var origin = Object.FindFirstObjectByType<XROrigin>();
                trackables = origin != null ? origin.TrackablesParent : null;
            }
            return trackables;
        }
    }

    public static Pose FromWorld(Pose worldPose) =>
        Trackables != null ? Trackables.InverseTransformPose(worldPose) : worldPose;

    public static Pose FromWorld(Transform t) => FromWorld(new Pose(t.position, t.rotation));

    /// <summary>A direction in session space (e.g. ARCore's main light direction) in world space.</summary>
    public static Vector3 DirectionToWorld(Vector3 sessionDirection) =>
        Trackables != null ? Trackables.TransformDirection(sessionDirection) : sessionDirection;
}
