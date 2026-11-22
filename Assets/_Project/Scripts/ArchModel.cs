using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The arch by the academy (Models/Arch/arch.fbx: LeftColumn, RightColumn, Arc, TopBeam = GameProgress's pieces
/// 0 to 3). It is a child of the placed academy model, so it moves, turns and scales with it; it stands in front
/// of the front wall by the poster boards, not touching the building, its front (the four columns, +Z) facing
/// the octagon (user, 21 Nov 2026).
///
/// What it shows (ShowProgress):
/// - Anywhere: always, in its current state: collected pieces solid, the others a see-through "ghost"; a piece
///   collected while exploring drops into place (PlayBuilt).
/// - In Iasi: the academy is only seen at the start and at the end, so the arch is all ghost until the finale
///   has been played, then whole.
/// The finale (QuestFlow) drives it with the Finale... calls: 1845 the arch rises piece by piece, 1890 it
/// crumbles, 1963 the academy dissolves, then both come back as a memory. Nothing pops: pieces and the academy
/// fade in and out with the shader's per-object dither (_PieceFade, "TheArchOfTime/Lit Near Fade"), on top of
/// the academy's see-through while adjusting (the arch follows that too, being on the same material).
/// While adjusting it shows its own footprint outline (arch_footprint.asset), like the academy's.
/// </summary>
public class ArchModel : MonoBehaviour
{
    [SerializeField] PlacementController placement;
    [Tooltip("Raises ItemCollected when a piece is collected (Anywhere: the piece drops into place).")]
    [SerializeField] ArchPieces pieces;
    [Tooltip("Models/Arch/arch.fbx (children LeftColumn, RightColumn, Arc, TopBeam).")]
    [SerializeField] GameObject archPrefab;
    [Tooltip("arch_footprint.asset (FootprintBaker.BakeArch): the two piers' outlines.")]
    [SerializeField] Mesh outlineMesh;
    [Tooltip("The academy's outline material (TheArchOfTime/Overlay).")]
    [SerializeField] Material outlineMaterial;

    [Header("Where it stands, in the academy model's units (scale 1)")]
    [Tooltip("Its base centre: in front of the front wall (-X, at x = -0.243) by the poster boards, a gap from " +
             "the wall, clear of the octagon (z > -0.096).")]
    [SerializeField] Vector3 position = new Vector3(-0.395f, 0f, -0.19f);
    [Tooltip("Turn around Y in degrees. 0 = its front (+Z, the four columns) faces +Z, along the front wall " +
             "towards the octagon.")]
    [SerializeField] float yaw = 0f;
    [Tooltip("Street level: the arch stands here when the academy's base slab is hidden (In Iasi, Hide base); " +
             "on the floor (0) when the slab shows.")]
    [SerializeField] float streetLevel = 0.0147f;

    [Header("Looks")]
    [Tooltip("A missing piece: share of its pixels dropped (the see-through dither). 0 = solid, 1 = gone.")]
    [Range(0f, 1f)]
    [SerializeField] float ghostFade = 0.72f;

    [Header("Animations")]
    [Tooltip("A piece rising into place: seconds, and how high above its place it starts (model units).")]
    [SerializeField] float buildSeconds = 1.4f;
    [SerializeField] float buildDrop = 0.06f;
    [Tooltip("A piece crumbling away: seconds, and how far it sinks (model units).")]
    [SerializeField] float fallSeconds = 1.2f;
    [SerializeField] float fallSink = 0.035f;
    [Tooltip("Seconds between two pieces in the finale's sequences.")]
    [SerializeField] float stagger = 0.45f;
    [Tooltip("Seconds for the academy to dissolve (1963) or come back.")]
    [SerializeField] float academySeconds = 2.4f;

    static readonly string[] PieceNames = { "LeftColumn", "RightColumn", "Arc", "TopBeam" };
    static readonly int FadeId = Shader.PropertyToID("_PieceFade");
    const string FadeShader = "TheArchOfTime/Lit Near Fade";

    enum Motion { Settle, Rise, Fall }

    class Part
    {
        public Transform transform;
        public Renderer[] renderers;
        public float fade;          // 0 solid .. 1 gone
        public Vector3 offset;      // from its place, in the arch's space
        public bool animating;
        public float start, seconds;
        public float fromFade, toFade;
        public Vector3 fromOffset, toOffset;
        public Motion motion;
    }

    readonly Part[] parts = new Part[GameProgress.PieceCount];
    GameMode mode = GameMode.Free;
    Transform model;                // the academy model the arch is attached to
    Transform root;                 // the arch
    Transform outline;
    bool outlineVisible;

    // The academy's own renderers (for 1963), with their usual shadow casting.
    Renderer[] academyRenderers;
    ShadowCastingMode[] academyShadows;
    float academyFade, academyFrom, academyTo, academyStart, academyDuration;
    bool academyAnimating;

    MaterialPropertyBlock block;

    /// <summary>The arch in the scene (null until an academy is placed).</summary>
    public Transform Root => root;

    /// <summary>The academy's fade (0 solid .. 1 dissolved, the 1963 finale); for ContactShadow.</summary>
    public float AcademyFade => academyFade;

    /// <summary>A piece's fade (0 solid .. 1 gone) and offset from its place (rising, sinking).</summary>
    public bool TryGetPiece(int piece, out float fade, out Vector3 offset)
    {
        var part = piece >= 0 && piece < parts.Length ? parts[piece] : null;
        fade = part != null ? part.fade : 1f;
        offset = part != null ? part.offset : Vector3.zero;
        return part != null;
    }

    void OnEnable()
    {
        if (pieces != null) pieces.ItemCollected += OnItemCollected;
    }

    void OnDisable()
    {
        if (pieces != null) pieces.ItemCollected -= OnItemCollected;
    }

    // Called by AppController / QuestFlow
    /// <summary>The mode being played: the arch shows that mode's progress.</summary>
    public void SetMode(GameMode value)
    {
        mode = value;
        ShowProgress(animate: false);
    }

    /// <summary>The arch's footprint outline (only while adjusting, like the academy's).</summary>
    public void SetOutlineVisible(bool visible)
    {
        outlineVisible = visible;
        if (outline != null) outline.gameObject.SetActive(visible);
    }

    /// <summary>
    /// The looks from the saved progress: Anywhere collected pieces solid, the others ghost; In Iasi all ghost
    /// until the finale has been played, then whole. Also ends the finale's looks (the academy comes back).
    /// </summary>
    public void ShowProgress(bool animate)
    {
        FadeAcademy(0f, animate ? academySeconds * 0.5f : 0f);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;
            float target = UsualFade(i);
            if (animate) Animate(parts[i], target, Vector3.zero, Motion.Settle, 0.6f, 0f);
            else Set(parts[i], target, Vector3.zero);
        }
    }

    /// <summary>A collected piece rises into place (Anywhere, while exploring).</summary>
    public void PlayBuilt(int piece)
    {
        if (piece < 0 || piece >= parts.Length || parts[piece] == null) return;
        Rise(parts[piece], 0f);
    }

    /// <summary>Finale, 1845: the arch rises piece by piece (the ones not standing yet).</summary>
    public void FinaleArchStands()
    {
        FadeAcademy(0f, academySeconds * 0.5f);
        float delay = 0f;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null || (parts[i].fade <= 0.001f && !parts[i].animating)) continue;
            Rise(parts[i], delay);
            delay += stagger;
        }
    }

    /// <summary>Finale, 1890: the arch crumbles away, from the top down.</summary>
    public void FinaleArchFalls()
    {
        float delay = 0f;
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i] == null) continue;
            Animate(parts[i], 1f, new Vector3(0f, -fallSink, 0f), Motion.Fall, fallSeconds, delay);
            delay += stagger * 0.6f;
        }
    }

    /// <summary>Finale, 1963: the academy dissolves (the arch is already gone).</summary>
    public void FinaleAcademyGone()
    {
        foreach (var part in parts)
        {
            if (part != null && part.fade < 1f && !part.animating)
                Animate(part, 1f, part.offset, Motion.Fall, 0.6f, 0f);
        }
        FadeAcademy(1f, academySeconds);
    }

    /// <summary>Finale, the legacy: the academy and the arch come back, as a memory.</summary>
    public void FinaleMemory()
    {
        FadeAcademy(0f, academySeconds);
        float delay = academySeconds * 0.5f;
        foreach (var part in parts)
        {
            if (part == null) continue;
            Rise(part, delay);
            delay += stagger * 0.6f;
        }
    }

    void OnItemCollected(ItemKind kind, int index)
    {
        // Anywhere only: In Iasi the academy isn't in view while a piece is collected.
        if (kind == ItemKind.Piece && mode == GameMode.Free) PlayBuilt(index);
    }

    float UsualFade(int piece)
    {
        bool solid = mode == GameMode.Free
            ? GameProgress.IsCollected(GameMode.Free, piece)
            : GameProgress.Phase(GameMode.InCity) == QuestPhase.Done;
        return solid ? 0f : ghostFade;
    }

    // Attaching to the placed academy
    void LateUpdate()
    {
        var current = placement != null ? placement.Model : null;
        if (current != model) Attach(current);
        if (root == null) return;

        // Every frame, so position and yaw can be tried out live in the Inspector during Play mode.
        root.localPosition = position + Vector3.up * streetLevel; // with the slab shown too (user, 21 Nov 2026)
        root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        float now = Time.unscaledTime;
        foreach (var part in parts)
        {
            if (part == null) continue;
            if (part.animating && now >= part.start) Step(part, now);
            Apply(part);
        }
        if (academyAnimating)
        {
            float t = academyDuration > 0f ? Mathf.Clamp01((now - academyStart) / academyDuration) : 1f;
            academyFade = Mathf.Lerp(academyFrom, academyTo, Smooth(t));
            if (t >= 1f) academyAnimating = false;
            ApplyAcademy();
        }
    }

    void Attach(Transform newModel)
    {
        model = newModel;
        root = null;
        outline = null;
        academyRenderers = null;
        academyFade = 0f;
        academyAnimating = false;
        for (int i = 0; i < parts.Length; i++) parts[i] = null;
        if (model == null || archPrefab == null) return;

        // The academy's renderers, before the arch is added under the model.
        var all = model.GetComponentsInChildren<Renderer>(true);
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in all)
            if (r.sharedMaterial != null && r.sharedMaterial.shader.name == FadeShader) list.Add(r);
        academyRenderers = list.ToArray();
        academyShadows = new ShadowCastingMode[academyRenderers.Length];
        for (int i = 0; i < academyRenderers.Length; i++) academyShadows[i] = academyRenderers[i].shadowCastingMode;

        var go = Instantiate(archPrefab, model, false);
        go.name = "Arch";
        root = go.transform;
        root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        root.localScale = Vector3.one;
        for (int i = 0; i < parts.Length; i++)
        {
            var child = root.Find(PieceNames[i]);
            if (child == null)
            {
                Debug.LogWarning($"[ArchModel] The arch model has no {PieceNames[i]}.");
                continue;
            }
            parts[i] = new Part { transform = child, renderers = child.GetComponentsInChildren<Renderer>(true) };
        }
        if (outlineMesh != null && outlineMaterial != null)
        {
            var line = new GameObject("Arch Outline");
            line.transform.SetParent(root, false);
            line.transform.localPosition = new Vector3(0f, 0.0006f, 0f);
            line.AddComponent<MeshFilter>().sharedMesh = outlineMesh;
            var r = line.AddComponent<MeshRenderer>();
            r.sharedMaterial = outlineMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            outline = line.transform;
            outline.gameObject.SetActive(outlineVisible);
        }
        ShowProgress(animate: false);
    }

    // Animation
    /// From where it is now (a ghost, or gone) to solid: it lifts gently off its place, then lands solid.
    void Rise(Part part, float delay) => Animate(part, 0f, Vector3.zero, Motion.Rise, buildSeconds, delay);

    void Animate(Part part, float toFade, Vector3 toOffset, Motion motion, float seconds, float delay)
    {
        part.animating = true;
        part.start = Time.unscaledTime + delay;
        part.seconds = Mathf.Max(0.01f, seconds);
        part.fromFade = part.fade;
        part.toFade = toFade;
        part.fromOffset = part.offset;
        part.toOffset = toOffset;
        part.motion = motion;
    }

    void Set(Part part, float fade, Vector3 offset)
    {
        part.animating = false;
        part.fade = fade;
        part.offset = offset;
        Apply(part);
    }

    void Step(Part part, float now)
    {
        float t = Mathf.Clamp01((now - part.start) / part.seconds);
        switch (part.motion)
        {
            case Motion.Rise:
                // Lift (first third, still as it was), then land: it turns solid on the way down and settles.
                const float lift = 0.35f;
                var top = part.fromOffset + new Vector3(0f, buildDrop, 0f);
                if (t < lift)
                {
                    part.fade = part.fromFade;
                    part.offset = Vector3.Lerp(part.fromOffset, top, Smooth(t / lift));
                }
                else
                {
                    float u = (t - lift) / (1f - lift);
                    part.fade = Mathf.Lerp(part.fromFade, part.toFade, Smooth(Mathf.Clamp01(u / 0.6f)));
                    part.offset = Vector3.LerpUnclamped(top, part.toOffset, EaseOutBack(u));
                }
                break;
            case Motion.Fall:
                // It sinks, slowly at first, and dissolves as it goes.
                part.fade = Mathf.Lerp(part.fromFade, part.toFade, Smooth(Mathf.Clamp01((t - 0.15f) / 0.85f)));
                part.offset = Vector3.Lerp(part.fromOffset, part.toOffset, t * t);
                break;
            default:
                part.fade = Mathf.Lerp(part.fromFade, part.toFade, Smooth(t));
                part.offset = Vector3.Lerp(part.fromOffset, part.toOffset, Smooth(t));
                break;
        }
        if (t >= 1f)
        {
            part.animating = false;
            part.fade = part.toFade;
            part.offset = part.toOffset;
        }
    }

    void Apply(Part part)
    {
        if (part.transform == null) return;
        part.transform.localPosition = part.offset;
        bool shown = part.fade < 0.995f;
        foreach (var r in part.renderers)
        {
            if (r == null) continue;
            r.enabled = shown;
            r.shadowCastingMode = part.fade < 0.1f ? ShadowCastingMode.On : ShadowCastingMode.Off;
            SetFade(r, part.fade);
        }
    }

    void FadeAcademy(float to, float seconds)
    {
        if (seconds <= 0f)
        {
            academyAnimating = false;
            academyFade = to;
            ApplyAcademy();
            return;
        }
        academyFrom = academyFade;
        academyTo = to;
        academyStart = Time.unscaledTime;
        academyDuration = seconds;
        academyAnimating = true;
    }

    void ApplyAcademy()
    {
        if (academyRenderers == null) return;
        for (int i = 0; i < academyRenderers.Length; i++)
        {
            var r = academyRenderers[i];
            if (r == null) continue;
            // Not r.enabled: the base slab's renderers are switched by "Hide base".
            r.shadowCastingMode = academyFade < 0.5f ? academyShadows[i] : ShadowCastingMode.Off;
            SetFade(r, academyFade);
        }
    }

    /// Always a property block, also when solid (0): a renderer without one is drawn by the SRP Batcher, which
    /// reuses the _PieceFade last set by a faded renderer (found 21 Nov 2026: the solid academy vanished next
    /// to a ghost piece). The collectables (ArchPieces) always carry a block too, for their tint.
    void SetFade(Renderer r, float fade)
    {
        block ??= new MaterialPropertyBlock();
        block.SetFloat(FadeId, fade);
        r.SetPropertyBlock(block);
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    static float EaseOutBack(float t)
    {
        const float c1 = 0.9f; // a small overshoot: the stone settles
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
