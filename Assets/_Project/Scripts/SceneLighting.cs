using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Lighting for a model shown over the camera image.
///
///   - ARCore light estimation (Environmental HDR, requested on the ARCameraManager): the main light
///     follows the real main light's direction, colour and brightness, and the ambient light comes
///     from ARCore's spherical harmonics, so the model is lit like the real scene and its shadow
///     (GroundShadow) falls the same way as real shadows.
///   - Ambient fallback, until ARCore has an estimate (and in the Editor): a neutral grey. Unity's
///     default sky lit the shaded sides of the model blue.
///   - Shadows: URP draws shadows up to "Shadow Distance" from the camera, spread over one shadow map.
///     With the default 50 m, one shadow pixel covered several cm of a 0.7 m model: blocky shadows that
///     shimmered when the phone moved. The model can be anything from tabletop to real size, so the
///     distance follows it: from the camera to just past the model's far side.
///
/// Changes the active URP asset at runtime; in the Editor the original value is put back afterwards.
/// </summary>
public class SceneLighting : MonoBehaviour
{
    [SerializeField] PlacementController placement;
    [SerializeField] Camera arCamera;

    [Header("Ambient")]
    [Tooltip("Light reaching surfaces in shadow until ARCore has an estimate. " +
             "Neutral grey, so shaded sides aren't tinted.")]
    [SerializeField] Color ambientColor = new Color(0.55f, 0.55f, 0.55f);

    [Header("Light estimation (ARCore)")]
    [Tooltip("On the Main Camera; its Light Estimation must request Ambient Spherical Harmonics, " +
             "Main Light Direction and Main Light Intensity.")]
    [SerializeField] ARCameraManager cameraManager;
    [Tooltip("The Directional Light, turned and tinted to match the real main light.")]
    [SerializeField] Light mainLight;
    [SerializeField] bool useLightEstimation = true;
    [Tooltip("Multiplies ARCore's main light brightness (tune on the phone). ARCore sends the brightness " +
             "inside the light colour, so the light's own intensity is this value.")]
    [SerializeField] float mainLightIntensityScale = 1f;
    [Tooltip("Seconds over which the light follows a new estimate. ARCore's estimate jumps as the phone " +
             "turns; smoothing stops the model flickering between too dark and too bright.")]
    [SerializeField] float smoothingSeconds = 1.5f;
    [Tooltip("ARCore's first estimates are rough and too dark (it converges over ~10-15 s). They are " +
             "ignored for this many seconds after the first one arrives; the start light is used meanwhile.")]
    [SerializeField] float warmUpSeconds = 3f;
    [Tooltip("Main light brightness until ARCore's estimate takes over (white x this), when no light level " +
             "is remembered yet. About the average of ARCore's estimates in the first phone tests.")]
    [SerializeField] float startBrightness = 1.15f;
    [Tooltip("Remember the settled light level and start from it next launch (usually the same place, so " +
             "the start fits). A fixed start can't fit both a dim room and daylight.")]
    [SerializeField] bool rememberLightLevel = true;
    [Tooltip("Seconds between saves of the light level while ARCore's estimate is in use.")]
    [SerializeField] float rememberEverySeconds = 5f;
    [Tooltip("The remembered level is a slow average over about this many seconds of use, saved as it was " +
             "5 to 10 s earlier, not the last moment: when the app is left, the phone is usually lowered or " +
             "pocketed and ARCore sees darkness (phone test: remembered levels of 0.18 to 0.23 made the next " +
             "start nearly black).")]
    [SerializeField] float rememberAverageSeconds = 30f;
    [Tooltip("Nothing is remembered before the average has had this many seconds of real estimates.")]
    [SerializeField] float minSecondsBeforeRemembering = 10f;
    [Tooltip("A remembered main light (its brightest colour channel) is clamped to this range when loaded.")]
    [SerializeField] Vector2 rememberedLightRange = new Vector2(0.6f, 2.5f);
    [Tooltip("A remembered ambient is clamped to this range, as a multiple of the neutral start ambient.")]
    [SerializeField] Vector2 rememberedAmbientRange = new Vector2(0.5f, 3f);
    [Tooltip("Multiplies ARCore's ambient light (tune on the phone).")]
    [SerializeField] float ambientIntensityScale = 1f;
    [Tooltip("Seconds between light estimate lines in the log (0 = none).")]
    [SerializeField] float logEverySeconds = 5f;

    [Header("Shadows")]
    [Tooltip("Shadow distance (metres) while no model is placed.")]
    [SerializeField] float idleShadowDistance = 3f;
    [Tooltip("Margin past the model's far side, as a multiple of the model's radius.")]
    [SerializeField] float marginRadii = 0.5f;
    [SerializeField] float minShadowDistance = 1f;
    [SerializeField] float maxShadowDistance = 150f;

    UniversalRenderPipelineAsset urp;
    float originalShadowDistance;
    Transform measuredModel;
    Renderer[] modelRenderers;
    float nextLog;

    // Latest valid estimate (targets). The light glides towards them from the start light.
    Quaternion targetRotation;
    Color targetColor;
    float targetIntensity;
    SphericalHarmonicsL2 targetAmbient;
    bool hasLightTarget, hasAmbientTarget;
    SphericalHarmonicsL2 currentAmbient;
    float firstEstimateTime = -1f; // when ARCore's first usable estimate arrived (warm-up)
    float nextRemember;

    // Slow averages of the light actually applied, for the remembered level.
    Color averageLight;
    SphericalHarmonicsL2 averageAmbient;
    float averagedSeconds;
    bool hasAverageAmbient;
    float neutralAmbientDc; // brightness of the neutral start ambient (reference for clamping)

    // Snapshots of the averages at the last two save ticks (rememberEverySeconds apart). The OLDER
    // one is what gets saved: it's 5 to 10 s old, so the moments of lowering / pocketing the phone
    // before the app is left never reach the remembered level.
    struct LightSnapshot
    {
        public bool Valid;
        public Color Light;
        public SphericalHarmonicsL2 Ambient;
        public bool HasAmbient;
    }
    LightSnapshot lastSnapshot, olderSnapshot;

    // PlayerPrefs: "r,g,b" of the main light colour and the 27 ambient SH coefficients.
    const string LightColorKey = "SceneLighting.LastLightColor";
    const string AmbientKey = "SceneLighting.LastAmbient";

    void OnEnable()
    {
        ApplyAmbient();
        if (mainLight != null)
        {
            // Until ARCore's estimate takes over: the last session's level, or a typical brightness
            // (the scene's own light is brighter).
            Color remembered = default;
            bool hasRemembered = rememberLightLevel && TryLoadLightColor(out remembered);
            mainLight.color = hasRemembered ? remembered : Color.white * startBrightness;
            mainLight.intensity = 1f;
            Debug.Log($"[SceneLighting] Start light: {(hasRemembered ? "remembered" : "default")} {mainLight.color}");
        }
        if (rememberLightLevel && TryLoadAmbient(neutralAmbientDc, out var ambient))
        {
            currentAmbient = ambient;
            RenderSettings.ambientProbe = ambient;
        }

        urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null) originalShadowDistance = urp.shadowDistance;
        else
            Debug.LogWarning("[SceneLighting] The active render pipeline is not URP; " +
                             "shadows are left as they are.", this);

        if (useLightEstimation && cameraManager != null) cameraManager.frameReceived += OnCameraFrame;
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) RememberLightLevel(); // the app may be closed from the background
    }

    void OnDisable()
    {
        RememberLightLevel();
        if (cameraManager != null) cameraManager.frameReceived -= OnCameraFrame;
        if (urp != null) urp.shadowDistance = originalShadowDistance;
    }

    void ApplyAmbient()
    {
        // URP lights from the ambient probe; a flat probe gives the same light from every direction.
        var probe = new SphericalHarmonicsL2();
        probe.AddAmbientLight(ambientColor.linear);
        RenderSettings.ambientProbe = probe;
        currentAmbient = probe; // ARCore's ambient glides in from here
        neutralAmbientDc = AmbientDc(probe);
    }

    /// Overall brightness of an ambient probe (its constant terms).
    static float AmbientDc(SphericalHarmonicsL2 sh) => (sh[0, 0] + sh[1, 0] + sh[2, 0]) / 3f;

    /// <summary>
    /// ARCore's estimate for this camera frame becomes the new target; Update() moves the light towards
    /// it. Missing values keep the last target. All-black estimates (ARCore sends a few at session start,
    /// before it has a real one) are ignored, or the model would turn black for a moment.
    /// </summary>
    void OnCameraFrame(ARCameraFrameEventArgs args)
    {
        var estimate = args.lightEstimation;
        bool usable = estimate.mainLightColor.HasValue && MaxComponent(estimate.mainLightColor.Value) > 0.01f;

        // Warm-up: ARCore's first seconds of estimates are rough and too dark; keep the start light.
        if (usable && firstEstimateTime < 0f) firstEstimateTime = Time.unscaledTime;
        bool warmingUp = firstEstimateTime < 0f || Time.unscaledTime - firstEstimateTime < warmUpSeconds;

        if (usable && !warmingUp)
        {
            Vector3 direction = estimate.mainLightDirection.HasValue
                ? SessionSpace.DirectionToWorld(estimate.mainLightDirection.Value) // session to world
                : Vector3.zero;
            if (direction.sqrMagnitude > 1e-6f) targetRotation = Quaternion.LookRotation(direction);
            else if (!hasLightTarget)
                targetRotation = mainLight != null ? mainLight.transform.rotation : Quaternion.identity;

            targetColor = estimate.mainLightColor.Value;
            // ARCore leaves averageMainLightBrightness empty: the brightness is in the colour, so the
            // intensity is just the scale (the first version kept the scene's 2 and doubled the light).
            targetIntensity = (estimate.averageMainLightBrightness ?? 1f) * mainLightIntensityScale;
            hasLightTarget = true;
        }

        if (!warmingUp && estimate.ambientSphericalHarmonics.HasValue &&
            !IsBlack(estimate.ambientSphericalHarmonics.Value))
        {
            targetAmbient = estimate.ambientSphericalHarmonics.Value * ambientIntensityScale;
            hasAmbientTarget = true;
        }

        if (logEverySeconds > 0f && Time.unscaledTime >= nextLog)
        {
            nextLog = Time.unscaledTime + logEverySeconds;
            string lightNow = mainLight != null
                ? $"colour {mainLight.color}, intensity {mainLight.intensity:0.00}"
                : "colour -, intensity 0.00";
            string hasAmbient = estimate.ambientSphericalHarmonics.HasValue ? "yes" : "no";
            Debug.Log($"[SceneLighting] Estimate: direction {Describe(estimate.mainLightDirection)}, " +
                      $"colour {Describe(estimate.mainLightColor)}, " +
                      $"brightness {Describe(estimate.averageMainLightBrightness)}, " +
                      $"ambient SH {hasAmbient} -> light now: {lightNow}");
        }
    }

    static string Describe<T>(T? value) where T : struct => value.HasValue ? value.Value.ToString() : "none";

    static float MaxComponent(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

    /// The constant (DC) term carries the overall brightness of the ambient light.
    static bool IsBlack(SphericalHarmonicsL2 sh) => sh[0, 0] + sh[1, 0] + sh[2, 0] < 1e-4f;

    /// Moves the light and the ambient towards the latest estimate (exponential smoothing).
    void Update()
    {
        float t = smoothingSeconds > 0f ? 1f - Mathf.Exp(-Time.unscaledDeltaTime / smoothingSeconds) : 1f;

        if (hasLightTarget && mainLight != null)
        {
            mainLight.transform.rotation = Quaternion.Slerp(mainLight.transform.rotation, targetRotation, t);
            mainLight.color = Color.Lerp(mainLight.color, targetColor, t);
            mainLight.intensity = Mathf.Lerp(mainLight.intensity, targetIntensity, t);
        }

        if (hasAmbientTarget)
        {
            currentAmbient = currentAmbient * (1f - t) + targetAmbient * t;
            RenderSettings.ambientProbe = currentAmbient;
        }

        UpdateAverages();

        if (Time.unscaledTime >= nextRemember)
        {
            nextRemember = Time.unscaledTime + rememberEverySeconds;
            RememberLightLevel(); // saves the older snapshot
            TakeSnapshot();
        }
    }

    // Remembered light level
    /// Slow averages of the applied light, only while ARCore's estimate is in use (after the warm-up).
    void UpdateAverages()
    {
        if (!hasLightTarget || mainLight == null) return;
        Color now = mainLight.color * mainLight.intensity;
        float a = averagedSeconds <= 0f
            ? 1f
            : 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.1f, rememberAverageSeconds));
        averageLight = Color.Lerp(averageLight, now, a);
        if (hasAmbientTarget)
        {
            float b = hasAverageAmbient ? a : 1f;
            averageAmbient = averageAmbient * (1f - b) + currentAmbient * b;
            hasAverageAmbient = true;
        }
        averagedSeconds += Time.unscaledDeltaTime;
    }

    /// Shifts the snapshots: the newest average becomes the last one, the last becomes the older one.
    void TakeSnapshot()
    {
        if (averagedSeconds < minSecondsBeforeRemembering) return;
        olderSnapshot = lastSnapshot;
        lastSnapshot = new LightSnapshot
        {
            Valid = true,
            Light = averageLight,
            Ambient = averageAmbient,
            HasAmbient = hasAverageAmbient,
        };
    }

    /// Saves the OLDER averaged snapshot (5 to 10 s old), never the last moments before the app is left.
    void RememberLightLevel()
    {
        if (!rememberLightLevel || !olderSnapshot.Valid) return;
        var s = olderSnapshot;
        PlayerPrefs.SetString(LightColorKey, Join(s.Light.r, s.Light.g, s.Light.b));
        if (s.HasAmbient)
        {
            var values = new float[27];
            for (int channel = 0; channel < 3; channel++)
                for (int k = 0; k < 9; k++)
                    values[channel * 9 + k] = s.Ambient[channel, k];
            PlayerPrefs.SetString(AmbientKey, Join(values));
        }
        PlayerPrefs.Save();
    }

    /// The remembered main light, its brightness clamped to rememberedLightRange.
    bool TryLoadLightColor(out Color color)
    {
        color = default;
        var v = Split(PlayerPrefs.GetString(LightColorKey, ""), 3);
        if (v == null) return false;
        color = new Color(v[0], v[1], v[2], 1f);
        float max = MaxComponent(color);
        if (max <= 1e-4f || float.IsNaN(max)) return false;
        float clamped = Mathf.Clamp(max, rememberedLightRange.x, rememberedLightRange.y);
        color = new Color(color.r * clamped / max, color.g * clamped / max, color.b * clamped / max, 1f);
        return true;
    }

    /// The remembered ambient, its brightness clamped to rememberedAmbientRange x the neutral ambient.
    bool TryLoadAmbient(float neutralDc, out SphericalHarmonicsL2 sh)
    {
        sh = default;
        var v = Split(PlayerPrefs.GetString(AmbientKey, ""), 27);
        if (v == null) return false;
        for (int channel = 0; channel < 3; channel++)
            for (int k = 0; k < 9; k++)
                sh[channel, k] = v[channel * 9 + k];
        float dc = AmbientDc(sh);
        if (dc <= 1e-5f || float.IsNaN(dc) || neutralDc <= 0f) return false;
        float clamped = Mathf.Clamp(dc, rememberedAmbientRange.x * neutralDc, rememberedAmbientRange.y * neutralDc);
        sh *= clamped / dc;
        return true;
    }

    static string Join(params float[] values)
    {
        var parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
            parts[i] = values[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return string.Join(",", parts);
    }

    static float[] Split(string text, int count)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var parts = text.Split(',');
        if (parts.Length != count) return null;
        var values = new float[count];
        for (int i = 0; i < count; i++)
            if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                return null;
        return values;
    }

    void LateUpdate()
    {
        if (urp == null || arCamera == null) return;

        float target = idleShadowDistance;
        if (placement != null && TryGetModelBounds(placement.Model, out var bounds))
        {
            float radius = bounds.extents.magnitude;
            float toCentre = Vector3.Distance(arCamera.transform.position, bounds.center);
            target = toCentre + radius * (1f + marginRadii);
        }
        target = Mathf.Clamp(target, minShadowDistance, maxShadowDistance);

        if (Mathf.Abs(urp.shadowDistance - target) > target * 0.02f)
            urp.shadowDistance = target;
    }

    bool TryGetModelBounds(Transform model, out Bounds bounds)
    {
        bounds = default;
        if (model == null) return false;

        if (model != measuredModel)
        {
            measuredModel = model;
            modelRenderers = model.GetComponentsInChildren<Renderer>();
        }

        bool any = false;
        foreach (var r in modelRenderers)
        {
            if (r == null || !r.enabled) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }
}
