using System;
using UnityEngine;

/// <summary>
/// Developer tool: the lighting tuning panel (AppUI.Tuning) over the camera screens, so the lighting can be tuned
/// on the phone outdoors without a new build. Each slider changes one SceneLighting.Tuning value at once; the
/// tuned values are kept in PlayerPrefs ("LightingTuner.Values") until Reset, and logged as one line
/// ("[LightingTuner] Values: ...") so they can be copied into the scene afterwards.
/// Turn showPanel off (or disable this component) for a build without the panel.
/// </summary>
public class LightingTuner : MonoBehaviour
{
    [SerializeField] SceneLighting lighting;
    [SerializeField] AppUI ui;
    [Tooltip("Show the \"Light\" button and its panel over the camera screens.")]
    [SerializeField] bool showPanel = true;
    [Tooltip("Seconds between updates of the panel's status line.")]
    [SerializeField] float statusSeconds = 0.5f;

    const string ValuesKey = "LightingTuner.Values";

    // Label, min, max, format. Order = the TuningRow indices below.
    static readonly (string label, float min, float max, string format)[] Rows =
    {
        ("Key light, darkest scene", 0f, 1.5f, "0.00"),
        ("Key light, brightest scene", 0f, 1.5f, "0.00"),
        ("Counter-light", 0f, 0.8f, "0.00"),
        ("Sky light", 0f, 0.8f, "0.00"),
        ("Colour from ARCore", 0f, 1f, "0%"),
        ("Reflections", 0f, 1.5f, "0.00"),
        ("Model's own shadows", 0f, 1f, "0%"),
        ("Contact shadow, darkness", 0f, 2f, "0%"),
        ("Contact shadow, width (cm, model at scale 1)", 0.2f, 12f, "0.0"),
    };

    float nextStatus;

    // Start, not OnEnable: AppUI builds its layout in its own OnEnable.
    void Start()
    {
        if (lighting == null || ui == null)
        {
            Debug.LogError("[LightingTuner] Assign SceneLighting and AppUI.", this);
            enabled = false;
            return;
        }
        if (TryLoad(out var saved))
        {
            lighting.Tuning = saved;
            Debug.Log("[LightingTuner] Tuned values loaded: " + Describe(saved));
        }
        var rows = new TuningRow[Rows.Length];
        var values = ToArray(lighting.Tuning);
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new TuningRow(Rows[i].label, Rows[i].min, Rows[i].max, values[i], Rows[i].format);
        ui.SetUpTuning(rows, showPanel);
        ui.TuningChanged += OnChanged;
        ui.TuningResetClicked += OnReset;
    }

    void OnDestroy()
    {
        if (ui == null) return;
        ui.TuningChanged -= OnChanged;
        ui.TuningResetClicked -= OnReset;
    }

    void Update()
    {
        if (ui == null || Time.unscaledTime < nextStatus || !ui.IsTuningOpen) return;
        nextStatus = Time.unscaledTime + statusSeconds;
        string arcore = lighting.RawBrightness >= 0f ? lighting.RawBrightness.ToString("0.00") : "no reading yet";
        ui.SetTuningStatus($"ARCore brightness {arcore}. Level {lighting.Level * 100f:0} % of the band, " +
                           $"key light {lighting.KeyIntensity:0.00}.");
    }

    void OnChanged(int row, float value)
    {
        var values = ToArray(lighting.Tuning);
        if (row < 0 || row >= values.Length) return;
        values[row] = value;
        var tuning = FromArray(values);
        lighting.Tuning = tuning;
        PlayerPrefs.SetString(ValuesKey, JsonUtility.ToJson(tuning));
        PlayerPrefs.Save();
        Debug.Log("[LightingTuner] Values: " + Describe(tuning));
    }

    void OnReset()
    {
        PlayerPrefs.DeleteKey(ValuesKey);
        PlayerPrefs.Save();
        lighting.Tuning = lighting.SceneTuning;
        ui.SetTuningValues(ToArray(lighting.Tuning));
        Debug.Log("[LightingTuner] Reset to the scene's values: " + Describe(lighting.Tuning));
    }

    bool TryLoad(out LightingTuning tuning)
    {
        tuning = default;
        string json = PlayerPrefs.GetString(ValuesKey, "");
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            tuning = JsonUtility.FromJson<LightingTuning>(json);
            // Saved before the contact shadow existed (its values read as 0): take the scene's.
            if (tuning.contactWidth <= 0f)
            {
                tuning.contactStrength = lighting.SceneTuning.contactStrength;
                tuning.contactWidth = lighting.SceneTuning.contactWidth;
            }
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static float[] ToArray(LightingTuning t) =>
        new[]
        {
            t.keyLow, t.keyHigh, t.fill, t.sky, t.tint, t.reflections, t.shadowStrength,
            t.contactStrength, t.contactWidth * 100f // the panel shows the width in cm
        };

    static LightingTuning FromArray(float[] v) => new LightingTuning
    {
        keyLow = v[0],
        keyHigh = v[1],
        fill = v[2],
        sky = v[3],
        tint = v[4],
        reflections = v[5],
        shadowStrength = v[6],
        contactStrength = v[7],
        contactWidth = v[8] / 100f,
    };

    static string Describe(LightingTuning t) =>
        $"keyLow {t.keyLow:0.00}, keyHigh {t.keyHigh:0.00}, fill {t.fill:0.00}, sky {t.sky:0.00}, " +
        $"tint {t.tint:0.00}, reflections {t.reflections:0.00}, shadowStrength {t.shadowStrength:0.00}, " +
        $"contactStrength {t.contactStrength:0.00}, contactWidth {t.contactWidth:0.000}";
}
