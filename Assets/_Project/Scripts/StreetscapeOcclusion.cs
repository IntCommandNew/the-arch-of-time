using System.Collections.Generic;
using Google.XR.ARCoreExtensions;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// In Iasi occlusion (22 Nov 2026): the real buildings around the phone, from ARCore's Streetscape Geometry
/// (Google's Street View data, where VPS covers the area), drawn invisibly (TheArchOfTime/DepthOnly), so the
/// academy, the arch and the pieces are hidden behind real buildings. Much further than the phone's own depth
/// (Anywhere mode's DepthOcclusion), with clean edges, but only as exact as the Geospatial position (about a
/// metre); no people, cars or trees; buildings only (no ground mesh, so it never covers the bases).
/// Every building occludes, also one standing on the academy's site (user, 22 Nov 2026: exclude none).
///
/// Switched by DepthOcclusion (the "Hidden by buildings" button in In Iasi), off at every launch. Streetscape
/// Geometry itself stays enabled in ARCoreExtensionsConfig all the time (modes are never changed at runtime);
/// this only shows or hides the meshes.
///
/// Setup: on App, with the ARStreetscapeGeometryManager (on the "ARCore Extensions" object, so it only runs
/// once AR has started), the XR Origin and a material using "TheArchOfTime/DepthOnly".
/// </summary>
public class StreetscapeOcclusion : MonoBehaviour
{
    [SerializeField] ARStreetscapeGeometryManager manager;
    [SerializeField] XROrigin origin;
    [Tooltip("A material using \"TheArchOfTime/DepthOnly\".")]
    [SerializeField] Material occluderMaterial;
    [Tooltip("Seconds between checks of which buildings are tracked.")]
    [SerializeField] float refreshSeconds = 0.5f;
    [Tooltip("Seconds between log lines about the buildings.")]
    [SerializeField] float logSeconds = 10f;

    class Building
    {
        public ARStreetscapeGeometry geometry;
        public Transform transform;
        public MeshRenderer renderer;
        public bool tracked;
    }

    readonly Dictionary<TrackableId, Building> buildings = new Dictionary<TrackableId, Building>();
    bool on;
    float nextRefresh, nextLog;

    public bool IsOn => on;

    public void SetOn(bool value)
    {
        if (value == on) return;
        on = value;
        Debug.Log($"[StreetscapeOcclusion] Hidden by buildings: {(on ? "on" : "off")}, {buildings.Count} buildings.");
        nextRefresh = 0f;
    }

    void OnEnable()
    {
        if (manager != null) manager.StreetscapeGeometriesChanged += OnGeometriesChanged;
    }

    void OnDisable()
    {
        if (manager != null) manager.StreetscapeGeometriesChanged -= OnGeometriesChanged;
    }

    void OnDestroy()
    {
        foreach (var b in buildings.Values)
            if (b.transform != null) Destroy(b.transform.gameObject);
        buildings.Clear();
    }

    // The manager reuses its lists: read them here, at once.
    void OnGeometriesChanged(ARStreetscapeGeometriesChangedEventArgs args)
    {
        foreach (var g in args.Added) Add(g);
        foreach (var g in args.Updated)
            if (buildings.TryGetValue(g.trackableId, out var b)) Follow(b);
        foreach (var g in args.Removed)
        {
            if (!buildings.TryGetValue(g.trackableId, out var b)) continue;
            if (b.transform != null) Destroy(b.transform.gameObject);
            buildings.Remove(g.trackableId);
        }
    }

    void Add(ARStreetscapeGeometry geometry)
    {
        if (geometry.streetscapeGeometryType != StreetscapeGeometryType.Building) return; // no terrain
        if (buildings.ContainsKey(geometry.trackableId) || occluderMaterial == null || origin == null) return;
        var mesh = geometry.mesh;
        if (mesh == null || mesh.vertexCount == 0) return;

        var go = new GameObject("Streetscape Building");
        go.transform.SetParent(origin.TrackablesParent, false); // the geometry's pose is in session space
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = occluderMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.enabled = false;
        var b = new Building { geometry = geometry, transform = go.transform, renderer = r };
        buildings.Add(geometry.trackableId, b);
        Follow(b);
        nextRefresh = 0f;
    }

    static void Follow(Building b)
    {
        if (b.transform == null) return;
        var pose = b.geometry.pose;
        b.transform.SetLocalPositionAndRotation(pose.position, pose.rotation);
    }

    void LateUpdate()
    {
        // No native calls without a running session (AR not started yet, or stopped after a permission was
        // revoked): ARStreetscapeGeometry's getters call ARCore with the current session.
        bool live = manager != null && manager.isActiveAndEnabled &&
                    ARSession.state >= ARSessionState.SessionInitializing;
        if (!live)
        {
            foreach (var b in buildings.Values)
                if (b.renderer != null) b.renderer.enabled = false;
            return;
        }

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshSeconds;
            int tracked = 0;
            foreach (var b in buildings.Values)
            {
                b.tracked = b.geometry.trackingState == TrackingState.Tracking;
                if (b.tracked) tracked++;
            }
            if (Time.unscaledTime >= nextLog && buildings.Count > 0)
            {
                nextLog = Time.unscaledTime + logSeconds;
                Debug.Log($"[StreetscapeOcclusion] {buildings.Count} buildings, {tracked} tracked; " +
                          $"{(on ? "on" : "off")}.");
            }
        }
        foreach (var b in buildings.Values)
            if (b.renderer != null) b.renderer.enabled = on && b.tracked;
    }
}
