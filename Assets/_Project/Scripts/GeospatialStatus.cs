using System.Collections;
using System.Text;
using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Developer overlay: shows whether ARCore Geospatial is authorized and tracking, plus accuracy
/// and Street View (VPS) coverage at your position. Text appears at the top of the screen
/// through AppUI. Disable this component to hide the overlay.
/// </summary>
public class GeospatialStatus : MonoBehaviour
{
    [SerializeField] AREarthManager earthManager;   // on the XR Origin
    [SerializeField] AppUI ui;
    [SerializeField] float refreshSeconds = 0.25f;

    string vpsInfo = "not checked yet";
    bool vpsChecked;
    float nextRefresh;
    readonly StringBuilder sb = new StringBuilder();

    // Permissions are requested by ARStartupGate, one at a time, before AR starts.

    void OnDisable()
    {
        if (ui != null) ui.SetDebugText(null);
    }

    void Update()
    {
        if (ui == null || Time.time < nextRefresh) return;
        nextRefresh = Time.time + refreshSeconds;

        sb.Clear();
        sb.AppendLine($"AR session: {ARSession.state}");

#if UNITY_ANDROID
        bool locationOk = Permission.HasUserAuthorizedPermission(Permission.FineLocation);
        sb.AppendLine($"Location permission: {Colour(locationOk, locationOk ? "granted" : "NOT granted")}");
#endif

        // Don't query ARCore before the session exists (the gate starts it after permissions).
        if (earthManager == null || ARSession.state < ARSessionState.SessionInitializing)
        {
            sb.Append("Waiting for AR to start...");
            ui.SetDebugText(sb.ToString());
            return;
        }

        var supported = earthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);
        sb.AppendLine($"Geospatial supported: {Colour(supported == FeatureSupported.Supported, supported.ToString())}");

        var earthState = earthManager.EarthState;
        sb.AppendLine($"Earth state: {Colour(earthState == EarthState.Enabled, earthState.ToString())}");

        var tracking = earthManager.EarthTrackingState;
        sb.AppendLine($"Earth tracking: {Colour(tracking == TrackingState.Tracking, tracking.ToString())}");

        if (tracking == TrackingState.Tracking)
        {
            var pose = earthManager.CameraGeospatialPose;
            sb.AppendLine($"Position: {pose.Latitude:F6}, {pose.Longitude:F6}  alt {pose.Altitude:F1} m");
            sb.AppendLine($"Accuracy: {TextSymbols.PlusMinus}{pose.HorizontalAccuracy:F1} m horizontal, " +
                          $"{TextSymbols.PlusMinus}{pose.VerticalAccuracy:F1} m vertical");
            sb.AppendLine($"Heading accuracy: {TextSymbols.PlusMinus}{pose.OrientationYawAccuracy:F1}" +
                          TextSymbols.Degree);

            if (!vpsChecked)
            {
                vpsChecked = true;
                StartCoroutine(CheckVps(pose.Latitude, pose.Longitude));
            }
        }

        sb.Append($"Street View / VPS here: {vpsInfo}");
        ui.SetDebugText(sb.ToString());
    }

    IEnumerator CheckVps(double latitude, double longitude)
    {
        vpsInfo = "checking...";
        var promise = AREarthManager.CheckVpsAvailabilityAsync(latitude, longitude);
        yield return promise;
        var result = promise.Result;
        vpsInfo = Colour(result == VpsAvailability.Available, result.ToString());
    }

    static string Colour(bool good, string text)
    {
        return good ? $"<color=#4CAF50>{text}</color>" : $"<color=#FF5252>{text}</color>";
    }
}
