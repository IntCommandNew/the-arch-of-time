using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

/// <summary>The values of the lighting setup that can be tuned on the phone (LightingTuner).</summary>
[Serializable]
public struct LightingTuning
{
    [Tooltip("Key (main) light intensity in the darkest real scene ARCore reports.")]
    public float keyLow;
    [Tooltip("Key light intensity in the brightest real scene.")]
    public float keyHigh;
    [Tooltip("Counter-light: brightness of the fill from the side opposite the key light.")]
    public float fill;
    [Tooltip("Sky light: the even ambient light from every direction.")]
    public float sky;
    [Tooltip("How much of ARCore's colour reaches the light (0 = neutral white, 1 = ARCore's colour).")]
    public float tint;
    [Tooltip("Brightness of the neutral reflections.")]
    public float reflections;
    [Tooltip("Darkness of the models' own shadows (the shadow on the real ground keeps its look).")]
    public float shadowStrength;
    [Tooltip("Contact shadow (ContactShadow): darkness on the line where a model meets the ground (0..2: above 1 " +
             "the band stays fully dark further out).")]
    public float contactStrength;
    [Tooltip("Contact shadow: how far it reaches from that line, in model units at scale 1 (scales with the model).")]
    public float contactWidth;

    /// <summary>Tuned by the user on the phone with the tuning panel (22 Nov 2026, indoors at night).</summary>
    public static LightingTuning Defaults => new LightingTuning
    {
        keyLow = 0.8f,
        keyHigh = 1f,
        fill = 0.1f,
        sky = 0.4f,
        tint = 0.3f,
        reflections = 0.25f,
        shadowStrength = 0.75f,
        contactStrength = 1f, // user, 22 Nov 2026: doubled, then tuned on the phone to 1.0 / 0.10
        contactWidth = 0.1f,
    };
}

/// <summary>
/// Lighting for the models shown over the camera image (the academy and the arch: they share the same shader and
/// the scene's light, ambient and reflections, so they are lit the same way).
///
/// A fixed, slightly dim lighting setup that ARCore's estimate only nudges (user, 21 Nov 2026: the old setup
/// applied ARCore's raw light, 0.3 to 3.7 times, and turned the academy almost white, yellow or black):
///   - Light level: ARCore's main light brightness, smoothed over levelSeconds in log space, mapped from
///     [levelRange] to 0..1. The key light's intensity moves only between keyLow and keyHigh with it, and the
///     ambient, fill and reflections follow in the same proportion.
///   - Colour: a little of ARCore's tint (its colour with the brightness taken out), clamped near white.
///   - Key light direction: ARCore's (so the dynamic shadow falls like real shadows), its height above the
///     horizon clamped to elevationRange: a low sun no longer burns one wall white and leaves the other black.
///   - Ambient = the scene's light probe, built here: an even sky light, a counter-light (fill) from the side
///     opposite the key light, and a little warm light bounced up from the ground; tinted a little by ARCore's
///     ambient colour.
///   - Reflections: a small neutral grey cubemap made here (lighter above, darker below) instead of Unity's
///     default blue skybox.
///   - Shadows: the key light's shadow strength (the models' own shadows); the URP shadow distance follows the
///     models (camera to just past their far side; a fixed 50 m made blocky, shimmering shadows).
/// Until ARCore's first usable estimates (and in the Editor) the last session's level is used (or the middle).
/// Simulate() feeds a made-up estimate, for Play-mode tests.
/// </summary>
public class SceneLighting : MonoBehaviour
{
    [SerializeField] PlacementController placement;
    [SerializeField] Camera arCamera;

    [Header("Light estimation (ARCore)")]
    [Tooltip("On the Main Camera; its Light Estimation must request Ambient Spherical Harmonics, " +
             "Main Light Direction and Main Light Intensity.")]
    [SerializeField] ARCameraManager cameraManager;
    [Tooltip("The Directional Light: the key light.")]
    [SerializeField] Light mainLight;
    [SerializeField] bool useLightEstimation = true;

    [Header("Tuning (the phone's tuning panel can override these)")]
    [SerializeField] LightingTuning tuning = LightingTuning.Defaults;

    [Header("Light level")]
    [Tooltip("ARCore main light brightness (its colour's luminance) mapped to the bottom and the top of the band. " +
             "Phone logs: 0.3 indoors in the evening to about 3.7 in daylight.")]
    [SerializeField] Vector2 levelRange = new Vector2(0.35f, 3f);
    [Tooltip("Seconds over which the light level follows ARCore (ARCore jumps as the phone turns).")]
    [SerializeField] float levelSeconds = 5f;
    [Tooltip("Seconds over which the key light turns to a new direction and colour.")]
    [SerializeField] float smoothingSeconds = 2f;
    [Tooltip("ARCore's first estimates are rough and too dark: ignored for this many seconds.")]
    [SerializeField] float warmUpSeconds = 3f;
    [Tooltip("Level (0..1) at the very first launch, until ARCore's estimate takes over.")]
    [Range(0f, 1f)]
    [SerializeField] float startLevel = 0.5f;

    [Header("Key light")]
    [Tooltip("Lowest and highest key light above the horizon (degrees).")]
    [SerializeField] Vector2 elevationRange = new Vector2(25f, 70f);
    [Tooltip("Each colour channel of the key light and the ambient stays within this range around white.")]
    [SerializeField] Vector2 tintClamp = new Vector2(0.8f, 1.2f);
    [Tooltip("Below this ARCore brightness its colour isn't trusted: the tint fades to white (phone log, 22 Nov " +
             "2026: in a dark room ARCore's near-black colour turned orange and the tint went to the warm limit).")]
    [SerializeField] Vector2 tintTrustRange = new Vector2(0.05f, 0.2f);

    [Header("Ambient (the light probe)")]
    [Tooltip("Height of the counter-light above the horizon (degrees).")]
    [SerializeField] float fillElevation = 30f;
    [Tooltip("Light bounced up from the ground, as a share of the sky light.")]
    [SerializeField] float groundBounce = 0.35f;
    [SerializeField] Color groundColor = new Color(1f, 0.92f, 0.82f);

    [Header("Reflections")]
    [Tooltip("Size of a face of the reflection cubemap made at runtime.")]
    [SerializeField] int reflectionSize = 32;

    [Header("Log")]
    [Tooltip("Seconds between light lines in the log (0 = none).")]
    [SerializeField] float logEverySeconds = 5f;

    [Header("Shadows")]
    [Tooltip("Shadow distance (metres) while no model is placed.")]
    [SerializeField] float idleShadowDistance = 3f;
    [Tooltip("Margin past the model's far side, as a multiple of the model's radius.")]
    [SerializeField] float marginRadii = 0.5f;
    [SerializeField] float minShadowDistance = 1f;
    [SerializeField] float maxShadowDistance = 150f;

    /// <summary>The tuning in use. Setting it applies it at once.</summary>
    public LightingTuning Tuning
    {
        get => tuning;
        set
        {
            tuning = value;
            ApplyLighting(snap: true);
        }
    }

    /// <summary>The values in the scene (what the tuning panel's Reset goes back to).</summary>
    public LightingTuning SceneTuning { get; private set; }

    /// <summary>ARCore's last main light brightness (luminance), or -1 before the first estimate.</summary>
    public float RawBrightness { get; private set; } = -1f;

    /// <summary>Where the light is in its band: 0 = darkest, 1 = brightest.</summary>
    public float Level => LevelFromLog(logLevel);

    /// <summary>The key light's intensity now.</summary>
    public float KeyIntensity => mainLight != null ? mainLight.intensity : 0f;

    UniversalRenderPipelineAsset urp;
    float originalShadowDistance;
    Transform measuredModel;
    Renderer[] modelRenderers;
    float nextRendererRefresh;
    float nextLog;

    // The estimate (smoothed), and what is applied.
    float logLevel;                 // ln of the brightness, smoothed
    bool hasLevel;                  // a real estimate has arrived (after the warm-up)
    float firstEstimateTime = -1f;
    Vector3 targetToLight;          // towards the light, elevation clamped
    Vector3 currentToLight;
    Color targetKeyTint = Color.white, currentKeyTint = Color.white;
    Color targetAmbientTint = Color.white, currentAmbientTint = Color.white;
    float levelEstimateSeconds;     // seconds of real estimates (for remembering)

    // Reflections
    Cubemap reflection;
    float appliedReflection = -1f;
    Color appliedReflectionTint;
#if UNITY_EDITOR
    DefaultReflectionMode originalReflectionMode;
    Texture originalReflectionTexture;
    AmbientMode originalAmbientMode;
#endif

    // The remembered level: the smoothed level as it was at the save tick BEFORE the last one (5 to 10 s old),
    // so lowering / pocketing the phone just before leaving never reaches it (phone test, 1 Nov 2026).
    const string LevelKey = "SceneLighting.Level";
    const float RememberEverySeconds = 5f;
    const float MinSecondsBeforeRemembering = 10f;
    float nextRemember;
    float lastSnapshot = float.NaN, olderSnapshot = float.NaN;

    void Awake()
    {
        SceneTuning = tuning;
    }

    void OnEnable()
    {
        // The old remembered light (colour and ambient) is no longer used.
        PlayerPrefs.DeleteKey("SceneLighting.LastLightColor");
        PlayerPrefs.DeleteKey("SceneLighting.LastAmbient");

        float level = PlayerPrefs.HasKey(LevelKey) ? Mathf.Clamp01(PlayerPrefs.GetFloat(LevelKey)) : startLevel;
        logLevel = LogFromLevel(level);
        currentToLight = targetToLight = ClampElevation(mainLight != null
            ? -mainLight.transform.forward
            : new Vector3(0.5f, 0.7f, 0.5f));
        Debug.Log($"[SceneLighting] Start level {level:0.00} " +
                  (PlayerPrefs.HasKey(LevelKey) ? "(remembered)" : "(default)"));

        urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null) originalShadowDistance = urp.shadowDistance;
        else
            Debug.LogWarning("[SceneLighting] The active render pipeline is not URP; " +
                             "shadows are left as they are.", this);

#if UNITY_EDITOR
        originalReflectionMode = RenderSettings.defaultReflectionMode;
        originalReflectionTexture = RenderSettings.customReflectionTexture;
        originalAmbientMode = RenderSettings.ambientMode;
#endif
        ApplyLighting(snap: true);
        if (useLightEstimation && cameraManager != null) cameraManager.frameReceived += OnCameraFrame;
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) RememberLevel(); // the app may be closed from the background
    }

    void OnDisable()
    {
        RememberLevel();
        if (cameraManager != null) cameraManager.frameReceived -= OnCameraFrame;
        if (urp != null) urp.shadowDistance = originalShadowDistance;
#if UNITY_EDITOR
        RenderSettings.defaultReflectionMode = originalReflectionMode;
        RenderSettings.customReflectionTexture = originalReflectionTexture;
        RenderSettings.ambientMode = originalAmbientMode;
#endif
    }

    void OnDestroy()
    {
        if (reflection != null) Destroy(reflection);
    }

    // Estimates
    /// ARCore's estimate for this frame. All-black estimates (a few at session start) are ignored.
    void OnCameraFrame(ARCameraFrameEventArgs args)
    {
        var estimate = args.lightEstimation;
        Color? colour = estimate.mainLightColor;
        if (!colour.HasValue || MaxComponent(colour.Value) <= 0.01f) return;

        Vector3? direction = estimate.mainLightDirection.HasValue
            ? SessionSpace.DirectionToWorld(estimate.mainLightDirection.Value) // session to world
            : (Vector3?)null;
        Color? ambient = null;
        if (estimate.ambientSphericalHarmonics.HasValue && !IsBlack(estimate.ambientSphericalHarmonics.Value))
        {
            var sh = estimate.ambientSphericalHarmonics.Value;
            ambient = new Color(sh[0, 0], sh[1, 0], sh[2, 0]);
        }
        // ARCore leaves averageMainLightBrightness empty: the brightness is in the colour.
        Take(colour.Value * (estimate.averageMainLightBrightness ?? 1f), direction, ambient);
    }

    /// <summary>
    /// Play-mode test: a made-up ARCore estimate (main light colour with its brightness in it, e.g. white x 3 for
    /// daylight; the direction the light travels; the ambient's colour). Repeat it to let the level settle, or
    /// call SettleNow() after it.
    /// </summary>
    public void Simulate(Color mainLightColor, Vector3 lightTravels, Color ambientColor)
    {
        firstEstimateTime = Time.unscaledTime - warmUpSeconds - 1f; // no warm-up for made-up estimates
        Take(mainLightColor, lightTravels, ambientColor);
    }

    /// <summary>Play-mode test: jump to the latest estimate at once (no smoothing).</summary>
    public void SettleNow()
    {
        if (RawBrightness > 0f) logLevel = Mathf.Log(RawBrightness);
        ApplyLighting(snap: true);
    }

    void Take(Color colour, Vector3? lightTravels, Color? ambient)
    {
        float now = Time.unscaledTime;
        if (firstEstimateTime < 0f) firstEstimateTime = now;
        if (now - firstEstimateTime < warmUpSeconds) return; // ARCore's first estimates are too dark

        float brightness = Luminance(colour);
        RawBrightness = brightness;
        if (!hasLevel)
        {
            hasLevel = true;
            // The first real estimate: start halfway between the remembered level and it.
            logLevel = Mathf.Lerp(logLevel, Mathf.Log(Mathf.Max(1e-3f, brightness)), 0.5f);
        }
        float trust = Mathf.InverseLerp(tintTrustRange.x, tintTrustRange.y, brightness);
        targetKeyTint = Color.Lerp(Color.white, Tint(colour), trust);
        if (ambient.HasValue) targetAmbientTint = Color.Lerp(Color.white, Tint(ambient.Value), trust);
        if (lightTravels.HasValue && lightTravels.Value.sqrMagnitude > 1e-6f)
            targetToLight = ClampElevation(-lightTravels.Value);

        if (logEverySeconds > 0f && now >= nextLog)
        {
            nextLog = now + logEverySeconds;
            Debug.Log($"[SceneLighting] ARCore {brightness:0.00} {colour} -> level {Level:0.00}, key " +
                      $"{KeyIntensity:0.00} {currentKeyTint}, elevation " +
                      $"{Mathf.Asin(Mathf.Clamp(currentToLight.y, -1f, 1f)) * Mathf.Rad2Deg:0}");
        }
    }

    // Applying
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (hasLevel && RawBrightness > 0f)
        {
            float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, levelSeconds));
            logLevel = Mathf.Lerp(logLevel, Mathf.Log(Mathf.Max(1e-3f, RawBrightness)), a);
            levelEstimateSeconds += dt;
        }
        float t = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, smoothingSeconds));
        currentToLight = Vector3.Slerp(currentToLight, targetToLight, t).normalized;
        currentKeyTint = Color.Lerp(currentKeyTint, targetKeyTint, t);
        currentAmbientTint = Color.Lerp(currentAmbientTint, targetAmbientTint, t);
        ApplyLighting(snap: false);

        if (Time.unscaledTime >= nextRemember)
        {
            nextRemember = Time.unscaledTime + RememberEverySeconds;
            RememberLevel(); // saves the older snapshot
            if (levelEstimateSeconds >= MinSecondsBeforeRemembering)
            {
                olderSnapshot = lastSnapshot;
                lastSnapshot = Level;
            }
        }
    }

    /// The key light, the ambient probe and the reflections from the level, the tints and the tuning.
    void ApplyLighting(bool snap)
    {
        if (snap)
        {
            currentToLight = targetToLight;
            currentKeyTint = targetKeyTint;
            currentAmbientTint = targetAmbientTint;
        }
        float level = Level;
        float key = Mathf.Lerp(tuning.keyLow, tuning.keyHigh, level);
        // The ambient, fill and reflections follow the key in proportion.
        float follow = tuning.keyHigh > 1e-3f ? key / tuning.keyHigh : 1f;

        if (mainLight != null)
        {
            mainLight.transform.rotation = Quaternion.LookRotation(-currentToLight);
            mainLight.color = Color.Lerp(Color.white, currentKeyTint, tuning.tint);
            mainLight.intensity = key;
            mainLight.shadowStrength = Mathf.Clamp01(tuning.shadowStrength);
        }

        Color ambientTint = Color.Lerp(Color.white, currentAmbientTint, tuning.tint);
        var probe = new SphericalHarmonicsL2();
        probe.AddAmbientLight(ambientTint * (tuning.sky * follow));
        // SphericalHarmonicsL2.AddDirectionalLight takes the direction TOWARDS the light; a surface facing it
        // gets about 2 x intensity: halved, so "fill" is the brightness added to the side facing it.
        Vector3 away = new Vector3(-currentToLight.x, 0f, -currentToLight.z);
        if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
        away.Normalize();
        float e = fillElevation * Mathf.Deg2Rad;
        Vector3 toFill = away * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e);
        probe.AddDirectionalLight(toFill, ambientTint, tuning.fill * follow * 0.5f);
        probe.AddDirectionalLight(Vector3.down, groundColor * ambientTint,
                                  tuning.sky * groundBounce * follow * 0.5f);
        RenderSettings.ambientMode = AmbientMode.Custom;
        RenderSettings.ambientProbe = probe;

        ApplyReflections(tuning.reflections * follow, ambientTint);

        // The contact shadow doesn't follow the light (it is the dark where the models meet the ground).
        Shader.SetGlobalFloat(ContactStrengthId, Mathf.Clamp(tuning.contactStrength, 0f, 2f));
        Shader.SetGlobalFloat(ContactWidthId, Mathf.Max(0.002f, tuning.contactWidth));
    }

    static readonly int ContactStrengthId = Shader.PropertyToID("_ContactShadowStrength");
    static readonly int ContactWidthId = Shader.PropertyToID("_ContactShadowWidth");

    /// The neutral reflection cubemap: lighter above, darker below. Remade only when it changes visibly.
    void ApplyReflections(float brightness, Color tint)
    {
        if (reflection != null && Mathf.Abs(brightness - appliedReflection) < 0.01f &&
            MaxDifference(tint, appliedReflectionTint) < 0.01f)
            return;
        if (reflection == null)
        {
            int size = Mathf.Max(4, reflectionSize);
            reflection = new Cubemap(size, TextureFormat.RGBAHalf, true)
            {
                name = "Neutral reflections",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
        }
        int n = reflection.width;
        var pixels = new Color[n * n];
        foreach (CubemapFace face in new[]
                 {
                     CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY,
                     CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ,
                 })
        {
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float up = FaceDirection(face, (x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f).y;
                    // Sky 1.0, horizon 0.8, ground 0.45 (times the brightness).
                    float shade = up >= 0f ? Mathf.Lerp(0.8f, 1f, up) : Mathf.Lerp(0.8f, 0.45f, -up);
                    var c = tint * (brightness * shade);
                    c.a = 1f;
                    pixels[y * n + x] = c;
                }
            }
            reflection.SetPixels(pixels, face);
        }
        reflection.Apply(updateMipmaps: true);
        appliedReflection = brightness;
        appliedReflectionTint = tint;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = reflection;
        RenderSettings.reflectionIntensity = 1f;
    }

    /// Direction of a cubemap texel (Unity's face layout; only its height matters here).
    static Vector3 FaceDirection(CubemapFace face, float u, float v)
    {
        switch (face)
        {
            case CubemapFace.PositiveX: return new Vector3(1f, -v, -u).normalized;
            case CubemapFace.NegativeX: return new Vector3(-1f, -v, u).normalized;
            case CubemapFace.PositiveY: return new Vector3(u, 1f, v).normalized;
            case CubemapFace.NegativeY: return new Vector3(u, -1f, -v).normalized;
            case CubemapFace.PositiveZ: return new Vector3(u, -v, 1f).normalized;
            default: return new Vector3(-u, -v, -1f).normalized;
        }
    }

    // Helpers
    float LevelFromLog(float log) =>
        Mathf.InverseLerp(Mathf.Log(levelRange.x), Mathf.Log(levelRange.y), log);

    float LogFromLevel(float level) =>
        Mathf.Lerp(Mathf.Log(levelRange.x), Mathf.Log(levelRange.y), level);

    /// Towards the light, its height above the horizon kept within elevationRange.
    Vector3 ClampElevation(Vector3 toLight)
    {
        toLight.Normalize();
        Vector3 flat = new Vector3(toLight.x, 0f, toLight.z);
        if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward; // straight above: any side
        flat.Normalize();
        float elevation = Mathf.Asin(Mathf.Clamp(toLight.y, -1f, 1f)) * Mathf.Rad2Deg;
        elevation = Mathf.Clamp(elevation, elevationRange.x, elevationRange.y) * Mathf.Deg2Rad;
        return flat * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation);
    }

    /// The colour with its brightness taken out (luminance 1), each channel clamped near white.
    Color Tint(Color c)
    {
        float l = Luminance(c);
        if (l <= 1e-4f) return Color.white;
        return new Color(Mathf.Clamp(c.r / l, tintClamp.x, tintClamp.y),
                         Mathf.Clamp(c.g / l, tintClamp.x, tintClamp.y),
                         Mathf.Clamp(c.b / l, tintClamp.x, tintClamp.y), 1f);
    }

    static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    static float MaxComponent(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

    static float MaxDifference(Color a, Color b) =>
        Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

    /// The constant (DC) term carries the overall brightness of the ambient light.
    static bool IsBlack(SphericalHarmonicsL2 sh) => sh[0, 0] + sh[1, 0] + sh[2, 0] < 1e-4f;

    void RememberLevel()
    {
        if (float.IsNaN(olderSnapshot)) return;
        PlayerPrefs.SetFloat(LevelKey, olderSnapshot);
        PlayerPrefs.Save();
    }

    // Shadow distance
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

    /// The bounds of the model and everything under it (the arch is added a moment after the academy, so the
    /// renderers are looked up again every second).
    bool TryGetModelBounds(Transform model, out Bounds bounds)
    {
        bounds = default;
        if (model == null) return false;

        if (model != measuredModel || Time.unscaledTime >= nextRendererRefresh)
        {
            measuredModel = model;
            modelRenderers = model.GetComponentsInChildren<Renderer>();
            nextRendererRefresh = Time.unscaledTime + 1f;
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
