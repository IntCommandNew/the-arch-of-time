using UnityEngine;

/// <summary>
/// Fades the model where it comes close to the camera, so walking up to (or into) the building at
/// real size shows walls dissolving instead of being cut open by the camera's near plane.
///
/// Sets the global shader values used by "TheArchOfTime/Lit Near Fade" (the model's materials):
/// geometry nearer than _NearFadeStart starts to dissolve and is gone at _NearFadeEnd. The distances
/// grow with the model's scale: 35 to 15 cm at tabletop size, 4 to 1.2 m at real size.
/// The fade must end well beyond the camera's near clip plane (0.1 m), or the plane's hard cut shows
/// instead of the fade (first phone test: a 0.15 to 0.06 m band still looked like a cut).
///
/// Also the model's "see-through" look (_ModelSeeThrough, same dither, over the whole model): while
/// adjusting, the real ground and surroundings show through the building, which helps to line it up.
/// AppController switches it with SetSeeThrough; it eases in and out.
/// </summary>
public class NearFade : MonoBehaviour
{
    [SerializeField] PlacementController placement;

    [Tooltip("Fade start distance per unit of model scale (metres). 0.08 gives 4 m at real size (x50).")]
    [SerializeField] float startPerScale = 0.08f;
    [Tooltip("The fade start never goes below this (metres), e.g. at tabletop size.")]
    [SerializeField] float minStart = 0.35f;
    [SerializeField] float maxStart = 5f;
    [Tooltip("Where the geometry is completely gone, as a fraction of the start distance.")]
    [Range(0f, 0.95f)]
    [SerializeField] float endFraction = 0.3f;
    [Tooltip("The geometry is never completely gone closer than this (metres): keep it beyond the " +
             "camera's near clip plane (0.1 m), so the fade, not the plane's cut, is what shows.")]
    [SerializeField] float minEnd = 0.15f;

    [Header("See-through")]
    [Tooltip("Share of the model's pixels dropped when see-through is on (0.5 = half: looks half transparent).")]
    [Range(0f, 0.9f)]
    [SerializeField] float seeThroughAmount = 0.5f;
    [Tooltip("Seconds to ease in / out.")]
    [SerializeField] float seeThroughSeconds = 0.25f;

    static readonly int StartId = Shader.PropertyToID("_NearFadeStart");
    static readonly int EndId = Shader.PropertyToID("_NearFadeEnd");
    static readonly int SeeThroughId = Shader.PropertyToID("_ModelSeeThrough");

    float seeThroughTarget;
    float seeThrough;

    public bool SeeThrough => seeThroughTarget > 0f;

    /// <summary>Makes the model look semi-transparent (eases in), or solid again.</summary>
    public void SetSeeThrough(bool on) => seeThroughTarget = on ? seeThroughAmount : 0f;

    void LateUpdate()
    {
        float scale = placement != null && placement.Model != null ? placement.Model.localScale.x : 1f;
        float start = Mathf.Clamp(startPerScale * scale, minStart, maxStart);
        float end = Mathf.Min(Mathf.Max(start * endFraction, minEnd), start * 0.95f);
        Shader.SetGlobalFloat(StartId, start);
        Shader.SetGlobalFloat(EndId, end);

        float step = seeThroughSeconds > 0f
            ? Time.unscaledDeltaTime / seeThroughSeconds * Mathf.Max(seeThroughAmount, 0.01f)
            : 1f;
        seeThrough = Mathf.MoveTowards(seeThrough, seeThroughTarget, step);
        Shader.SetGlobalFloat(SeeThroughId, seeThrough);
    }

    void OnDisable()
    {
        // Equal values switch the fade off in the shader; 0 = solid.
        Shader.SetGlobalFloat(StartId, 0f);
        Shader.SetGlobalFloat(EndId, 0f);
        Shader.SetGlobalFloat(SeeThroughId, 0f);
    }
}
