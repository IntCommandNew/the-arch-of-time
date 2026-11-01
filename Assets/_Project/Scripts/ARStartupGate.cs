using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Starts AR only when it is safe to do so, and stops it for good if that stops being true.
///
/// Rules (both permissions are MANDATORY):
///   - At launch: ask for the camera, then precise location, one dialog at a time.
///     If either is missing, AR does not start; a card offers "Try again" and "Open settings"
///     until both are granted. Coming back from Settings with both granted starts AR by itself.
///   - Then: check ARCore / Google Play Services for AR, and activate the "AR Session" and
///     "ARCore Extensions" objects (in that order: Extensions hooks into the session's
///     subsystem when it is enabled, so the session must exist first).
///   - While running: if a permission disappears, AR is switched off, SessionInvalidated is
///     raised (everything from this session is dropped; saved data stays), and the app shows a
///     final "Close app" card. Usually Android itself kills and restarts the app when a
///     permission is revoked; this check is the safety net for when it doesn't.
///
/// Why the gate at all: ARCore Extensions (v1.57) gives the AR session about 60 frames to start
/// after it is enabled, then calls into ARCore anyway; with a permission dialog still open the
/// app crashed after 1 to 2 seconds. And with Geospatial enabled, ARCore refuses to configure the
/// session without precise location, so the camera never started.
///
/// The gate has no UI of its own: it reports through StatusChanged, and AppController shows it.
/// Scene setup: untick (deactivate) the "AR Session" and "ARCore Extensions" objects and assign them here.
/// </summary>
public class ARStartupGate : MonoBehaviour
{
    public enum Status
    {
        Starting,         // asking permissions / checking ARCore; see Detail
        Blocked,          // AR can't start yet; the user can fix it (see the Can* flags)
        Running,          // AR objects are on, both permissions granted
        PermissionsLost,  // a permission was revoked while running; final, only "Close app"
    }

    [Header("Leave these INACTIVE in the scene; the gate turns them on")]
    [SerializeField] GameObject arSessionObject;
    [SerializeField] GameObject arCoreExtensionsObject;

    [Header("While running")]
    [Tooltip("How often (seconds) to check that both permissions are still granted.")]
    [SerializeField] float permissionCheckInterval = 1f;

#pragma warning disable 0414 // read only in the Editor
    [Header("Editor")]
    [Tooltip("Play mode only: skip permissions and ARCore checks and report Running, so the in-AR UI " +
             "can be previewed. AR objects are not switched on in the Editor. Untick to preview the " +
             "'not supported' card. Right-click the component, then 'Test: simulate permission loss', " +
             "to preview that card.")]
    [SerializeField] bool previewInEditor = true;
#pragma warning restore 0414

    [Header("Events")]
    [Tooltip("Raised once, when AR has been switched on.")]
    public UnityEvent onARStarted;

    /// <summary>Raised whenever Status or its texts change.</summary>
    public event Action<ARStartupGate> StatusChanged;

    /// <summary>
    /// AR was stopped because a permission was revoked. Drop everything belonging to this
    /// session (placed model, anchors, unsaved adjustments). Saved data must be kept.
    /// </summary>
    public event Action SessionInvalidated;

    public Status CurrentStatus { get; private set; } = Status.Starting;
    /// <summary>While Starting: what the gate is waiting for.</summary>
    public string Detail { get; private set; } = "";
    public string BlockedTitle { get; private set; } = "";
    public string BlockedMessage { get; private set; } = "";
    public bool CanRetry { get; private set; }
    public bool CanOpenSettings { get; private set; }
    public bool CanClose { get; private set; }

    public bool ARStarted => CurrentStatus == Status.Running;

    enum Answer { Pending, Granted, Denied }
    Answer lastAnswer;
    Coroutine running;
    bool asking;                 // a permission dialog of ours is (about to be) on screen
    bool focusLostWhileAsking;   // the dialog actually appeared (it takes focus from the app)
    float focusRegainedAt;
#pragma warning disable 0414 // read only on the phone
    float nextPermissionCheck;
#pragma warning restore 0414

    // Startup
    void Start()
    {
        if (arSessionObject != null && arSessionObject.activeSelf)
            Debug.LogWarning("[ARStartupGate] 'AR Session' is active in the scene. " +
                             "Untick it so the gate can start it safely.");
        if (arCoreExtensionsObject != null && arCoreExtensionsObject.activeSelf)
            Debug.LogWarning("[ARStartupGate] 'ARCore Extensions' is active in the scene. " +
                             "Untick it so the gate can start it safely.");

        Retry();
    }

    /// <summary>
    /// Start the startup sequence again (the "Try again" button). Asks again for missing permissions.
    /// </summary>
    public void Retry()
    {
        if (CurrentStatus == Status.Running || CurrentStatus == Status.PermissionsLost || running != null) return;
        running = StartCoroutine(StartupRoutine());
    }

    IEnumerator StartupRoutine()
    {
#if UNITY_EDITOR
        // The Editor has no runtime permissions and no ARCore.
        if (previewInEditor)
        {
            SetRunning();
            running = null;
            yield break;
        }
#elif UNITY_ANDROID
        // 1. Camera, then precise location. Both are asked before deciding, so a single card
        //    can list everything that's missing.
        if (!HasCamera())
        {
            SetStarting("Waiting for camera permission...");
            yield return Ask(new[] { Permission.Camera }, Permission.Camera);
        }

        if (!HasPreciseLocation())
        {
            SetStarting("Waiting for location permission...");
            // Android 12+ ignores a request for precise location on its own: ask for both.
            yield return Ask(new[] { Permission.FineLocation, Permission.CoarseLocation }, Permission.FineLocation);
        }

        if (!HasCamera() || !HasPreciseLocation())
        {
            SetBlocked("Permissions needed", MissingPermissionsMessage(),
                canRetry: true, canOpenSettings: true, canClose: false);
            running = null;
            yield break;
        }
#endif

        // 2. ARCore support and installation (Google Play Services for AR).
        SetStarting("Checking ARCore support...");
        if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
            yield return ARSession.CheckAvailability();

        if (ARSession.state == ARSessionState.Unsupported)
        {
            SetBlocked("AR not supported",
                "This phone does not support ARCore, so the AR view can't run on it.",
                canRetry: false, canOpenSettings: false, canClose: false);
            running = null;
            yield break;
        }

        if (ARSession.state == ARSessionState.NeedsInstall)
        {
            SetStarting("Installing Google Play Services for AR...");
            yield return ARSession.Install();
            if (ARSession.state == ARSessionState.NeedsInstall || ARSession.state == ARSessionState.Unsupported)
            {
                SetBlocked("Google Play Services for AR needed",
                    "Install it from the Play Store, then tap \"Try again\".",
                    canRetry: true, canOpenSettings: false, canClose: false);
                running = null;
                yield break;
            }
        }

        // 3. Start. ORDER MATTERS: the AR Session first, then ARCore Extensions.
        if (arSessionObject != null) arSessionObject.SetActive(true);
        if (arCoreExtensionsObject != null) arCoreExtensionsObject.SetActive(true);

        SetRunning();
        running = null;
    }

#if UNITY_ANDROID
    static bool HasCamera() => Permission.HasUserAuthorizedPermission(Permission.Camera);
    static bool HasPreciseLocation() => Permission.HasUserAuthorizedPermission(Permission.FineLocation);

    static string MissingPermissionsMessage()
    {
        string text = "This app needs both permissions to work:\n";
        if (!HasCamera())
            text += "\n" + TextSymbols.Bullet + " Camera: to show the building in your surroundings.";
        if (!HasPreciseLocation())
            text += "\n" + TextSymbols.Bullet + " Precise location: to place the building at its real spot " +
                    "and find it again. When asked, choose \"Precise\".";
        text += "\n\nTap \"Try again\". If no dialog appears, Android has stopped asking: " +
                "open the settings and allow Camera and Location (Precise).";
        return text;
    }

    /// Requests permissions and waits until the answer for <paramref name="key"/> is known.
    IEnumerator Ask(string[] permissions, string key)
    {
        lastAnswer = Answer.Pending;
        asking = true;
        focusLostWhileAsking = false;

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += name => { if (name == key) lastAnswer = Answer.Granted; };
        callbacks.PermissionDenied += name => { if (name == key) lastAnswer = Answer.Denied; };
        Permission.RequestUserPermissions(permissions, callbacks);

        float waited = 0f;
        while (lastAnswer == Answer.Pending)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;

            if (Permission.HasUserAuthorizedPermission(key)) { lastAnswer = Answer.Granted; break; }

            // Safety nets, in case no callback arrives (devices differ on "don't ask again"):
            // no dialog appeared at all -> Android refused without asking;
            if (!focusLostWhileAsking && waited > 2f) lastAnswer = Answer.Denied;
            // the dialog closed and the permission still isn't granted.
            if (focusLostWhileAsking && Application.isFocused &&
                Time.realtimeSinceStartup - focusRegainedAt > 1f) lastAnswer = Answer.Denied;
        }
        asking = false;
    }
#endif

    // While running
    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (CurrentStatus != Status.Running || Time.unscaledTime < nextPermissionCheck) return;
        nextPermissionCheck = Time.unscaledTime + permissionCheckInterval;
        if (!HasCamera() || !HasPreciseLocation()) EnterPermissionsLost();
#endif
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            if (asking) focusLostWhileAsking = true;
            return;
        }
        focusRegainedAt = Time.realtimeSinceStartup;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (asking) return; // our own dialog closing; Ask() handles the answer

        if (CurrentStatus == Status.Running)
        {
            if (!HasCamera() || !HasPreciseLocation()) EnterPermissionsLost();
        }
        else if (CurrentStatus == Status.Blocked && CanRetry && HasCamera() && HasPreciseLocation())
        {
            // Both allowed in Settings while we were away: start without another tap.
            Retry();
        }
#endif
    }

    void EnterPermissionsLost()
    {
        if (CurrentStatus == Status.PermissionsLost) return;
        Debug.Log("[ARStartupGate] A permission was revoked while running: stopping AR.");

        // Stop AR in the reverse order of starting.
        if (arCoreExtensionsObject != null) arCoreExtensionsObject.SetActive(false);
        if (arSessionObject != null) arSessionObject.SetActive(false);

        CurrentStatus = Status.PermissionsLost;
        Detail = "";
        BlockedTitle = "Permission removed";
        BlockedMessage = "A permission this app needs was turned off, so this session has ended.\n\n" +
                         "Close the app and open it again to continue. Saved placements are kept.";
        CanRetry = false;
        CanOpenSettings = false;
        CanClose = true;

        SessionInvalidated?.Invoke();
        StatusChanged?.Invoke(this);
    }

    [ContextMenu("Test: simulate permission loss")]
    void SimulatePermissionLoss()
    {
        if (Application.isPlaying && CurrentStatus == Status.Running) EnterPermissionsLost();
    }

    // Actions for buttons
    /// <summary>Opens this app's Android settings page.</summary>
    public void OpenAppSettings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var uriClass = new AndroidJavaClass("android.net.Uri"))
        using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier))
        using (var intent = new AndroidJavaObject("android.content.Intent",
                   "android.settings.APPLICATION_DETAILS_SETTINGS", uri))
        {
            intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
            activity.Call("startActivity", intent);
        }
#endif
    }

    /// <summary>Closes the app (the "Close app" button after a permission was revoked).</summary>
    public void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Status reporting
    void SetStarting(string detail)
    {
        CurrentStatus = Status.Starting;
        Detail = detail;
        StatusChanged?.Invoke(this);
    }

    void SetBlocked(string title, string message, bool canRetry, bool canOpenSettings, bool canClose)
    {
        CurrentStatus = Status.Blocked;
        BlockedTitle = title;
        BlockedMessage = message;
        CanRetry = canRetry;
        CanOpenSettings = canOpenSettings;
        CanClose = canClose;
        Debug.Log($"[ARStartupGate] Blocked: {title}");
        StatusChanged?.Invoke(this);
    }

    void SetRunning()
    {
        CurrentStatus = Status.Running;
        Detail = "";
        nextPermissionCheck = Time.unscaledTime + permissionCheckInterval;
        StatusChanged?.Invoke(this);
        onARStarted?.Invoke();
    }
}
