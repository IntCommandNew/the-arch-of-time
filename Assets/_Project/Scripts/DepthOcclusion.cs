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
/// </summary>
public class DepthOcclusion : MonoBehaviour
{
    [Tooltip("On the Main Camera (next to the ARCameraManager).")]
    [SerializeField] AROcclusionManager occlusionManager;
    [Tooltip("Depth mode used when switched on. On ARCore every mode means its automatic depth.")]
    [SerializeField] EnvironmentDepthMode onMode = EnvironmentDepthMode.Best;

    /// <summary>On/off or the phone's support changed.</summary>
    public event Action Changed;

    public bool IsOn => occlusionManager != null &&
                        occlusionManager.requestedEnvironmentDepthMode != EnvironmentDepthMode.Disabled;

    /// <summary>Whether this phone can estimate depth (Unknown until AR is running).</summary>
    public Supported Support => occlusionManager != null && occlusionManager.descriptor != null
        ? occlusionManager.descriptor.environmentDepthImageSupported
        : Supported.Unknown;

    Supported lastSupport = Supported.Unknown;
    float nextSupportCheck;

    void Awake()
    {
        if (occlusionManager != null) occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
    }

    public void SetOn(bool on)
    {
        if (occlusionManager == null || on == IsOn) return;
        occlusionManager.requestedEnvironmentDepthMode = on ? onMode : EnvironmentDepthMode.Disabled;
        Debug.Log($"[DepthOcclusion] Hidden by real objects: {(on ? "on" : "off")} (support: {Support}).");
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
