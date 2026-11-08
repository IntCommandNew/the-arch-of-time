using UnityEngine;

/// <summary>
/// The model's "see-through" look (_ModelSeeThrough, a 4x4 dither over the whole model): while adjusting,
/// the real ground and surroundings show through the building, which helps to line it up.
/// AppController switches it with SetSeeThrough; it eases in and out.
///
/// The name is historical: until 8 Nov 2026 this also dissolved the model near the camera. The user asked
/// for it to go ("a user can inspect the model from very close without the details disappearing"), so
/// close up only the camera's near clip plane (0.1 m) cuts the model now.
/// </summary>
public class NearFade : MonoBehaviour
{
    [Header("See-through")]
    [Tooltip("Share of the model's pixels dropped when see-through is on (0.5 = half: looks half transparent).")]
    [Range(0f, 0.9f)]
    [SerializeField] float seeThroughAmount = 0.5f;
    [Tooltip("Seconds to ease in / out.")]
    [SerializeField] float seeThroughSeconds = 0.25f;

    static readonly int SeeThroughId = Shader.PropertyToID("_ModelSeeThrough");

    float seeThroughTarget;
    float seeThrough;

    public bool SeeThrough => seeThroughTarget > 0f;

    /// <summary>Makes the model look semi-transparent (eases in), or solid again.</summary>
    public void SetSeeThrough(bool on) => seeThroughTarget = on ? seeThroughAmount : 0f;

    void LateUpdate()
    {
        float step = seeThroughSeconds > 0f
            ? Time.unscaledDeltaTime / seeThroughSeconds * Mathf.Max(seeThroughAmount, 0.01f)
            : 1f;
        seeThrough = Mathf.MoveTowards(seeThrough, seeThroughTarget, step);
        Shader.SetGlobalFloat(SeeThroughId, seeThrough);
    }

    void OnDisable() => Shader.SetGlobalFloat(SeeThroughId, 0f); // 0 = solid
}
