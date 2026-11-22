using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// The optional "hidden by real objects" switch (depth occlusion). Off by default and at every launch:
/// at the real site, the apartment block that stands there now would hide the whole building.
///
/// When on, ARCore estimates the real scene's depth and the camera background writes it into the depth
/// buffer before the model is drawn (background mode "Any" = BeforeOpaques on ARCore), so real things
/// in front of the building hide it. Needs a phone with ARCore Depth support (Depth is "Optional").
/// In Iasi (SetCityMode) the same switch controls StreetscapeOcclusion instead ("Hidden by buildings") and the
/// depth stays off. Since Streetscape Geometry is enabled, ARCore's depth also includes Google's buildings and
/// terrain where VPS covers the area (Geospatial Depth, up to 65 m), so Anywhere outdoors reaches further too.
/// </summary>
public class DepthOcclusion : MonoBehaviour
{
    [Tooltip("On the Main Camera (next to the ARCameraManager).")]
    [SerializeField] AROcclusionManager occlusionManager;
    [Tooltip("Depth mode used when switched on. On ARCore every mode means its automatic depth.")]
    [SerializeField] EnvironmentDepthMode onMode = EnvironmentDepthMode.Best;
    [Tooltip("In Iasi: the same switch shows the real buildings' meshes instead (Streetscape Geometry).")]
    [SerializeField] StreetscapeOcclusion buildings;

    /// <summary>On/off or the phone's support changed.</summary>
    public event Action Changed;

    /// <summary>In Iasi: the switch means "hidden by buildings" (StreetscapeOcclusion), the depth stays off.</summary>
    public bool CityMode { get; private set; }

    public bool IsOn => CityMode ? buildings != null && buildings.IsOn : DepthOn;

    bool DepthOn => occlusionManager != null &&
                    occlusionManager.requestedEnvironmentDepthMode != EnvironmentDepthMode.Disabled;

    /// <summary>Whether it can be used: In Iasi always (needs only Geospatial); else the phone's depth support
    /// (Unknown until AR is running).</summary>
    public Supported Support => CityMode ? (buildings != null ? Supported.Supported : Supported.Unsupported)
        : occlusionManager != null && occlusionManager.descriptor != null
            ? occlusionManager.descriptor.environmentDepthImageSupported
            : Supported.Unknown;

    /// <summary>The mode being played: switches off what was on and changes what the switch controls.</summary>
    public void SetCityMode(bool city)
    {
        if (city == CityMode) return;
        SetOn(false);
        CityMode = city;
        Changed?.Invoke();
    }

    Supported lastSupport = Supported.Unknown;
    float nextSupportCheck;

    void Awake()
    {
        if (occlusionManager != null) occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
    }

    public void SetOn(bool on)
    {
        if (on == IsOn) return;
        if (CityMode)
        {
            if (buildings == null) return;
            buildings.SetOn(on);
        }
        else
        {
            if (occlusionManager == null) return;
            occlusionManager.requestedEnvironmentDepthMode = on ? onMode : EnvironmentDepthMode.Disabled;
            Debug.Log($"[DepthOcclusion] Hidden by real objects: {(on ? "on" : "off")} (support: {Support}).");
        }
        Changed?.Invoke();
    }

    void Update()
    {
        // The support is only known once the occlusion subsystem runs: report when it changes.
        if (Time.unscaledTime < nextSupportCheck) return;
        nextSupportCheck = Time.unscaledTime + 0.5f;
        var support = Support;
        if (support == lastSupport) return;
        lastSupport = support;
        Debug.Log($"[DepthOcclusion] Depth support: {support}.");
        Changed?.Invoke();
    }
}
