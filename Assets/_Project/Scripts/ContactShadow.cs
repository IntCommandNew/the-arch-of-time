using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Contact shadows (lighting step 2, 22 Nov 2026): a soft dark band on the ground around where the academy and
/// the arch touch it, so they sit on the real ground. Independent of the light (the sun's shadow is GroundShadow).
/// - The academy: around the base slab's bottom when the base shows; around the walls at street level when it is
///   hidden (Hide base, In Iasi).
/// - The arch: around each pier, only when the base is hidden (user, 22 Nov 2026: with the base shown, only the
///   base has the effect). A pier's band follows its piece: none while it is a ghost, it comes in as the piece
///   lands, weaker while the piece is lifted.
/// In Iasi the base is always hidden, so the walls and the piers always have it.
/// - Everything fades with the academy in the finale (1963).
/// The bands are flat quads (shapes baked by ContactShadowBaker) parented to the model / the arch, so they move,
/// turn and scale with them.
///
/// Setup: on App, with PlacementController, ArchModel, a material using "TheArchOfTime/ContactShadow" and the
/// four ContactShadowShape assets.
/// </summary>
[DefaultExecutionOrder(100)] // after ArchModel, which creates the arch in its LateUpdate
public class ContactShadow : MonoBehaviour
{
    [SerializeField] PlacementController placement;
    [SerializeField] ArchModel archModel;
    [Tooltip("A material using \"TheArchOfTime/ContactShadow\" (strength and width: SceneLighting's tuning).")]
    [SerializeField] Material material;

    [Header("Shapes (ContactShadowBaker)")]
    [SerializeField] ContactShadowShape slab;
    [SerializeField] ContactShadowShape footprint;
    [SerializeField] ContactShadowShape leftPier;
    [SerializeField] ContactShadowShape rightPier;

    [Header("Placement, in model units (scale 1)")]
    [Tooltip("Street level, where the walls meet the paving (the footprint's band when the base is hidden).")]
    [SerializeField] float streetLevel = 0.0147f;
    [Tooltip("Lift above the surface it lies on, against flicker (the piers' bands lie on the slab).")]
    [SerializeField] float lift = 0.0008f;
    [Tooltip("A piece lifted this high (model units) or more casts no contact shadow (PlayBuilt's rise).")]
    [SerializeField] float liftFade = 0.03f;

    static readonly int TextureId = Shader.PropertyToID("_ShapeTex");
    static readonly int MaxDistanceId = Shader.PropertyToID("_ShapeMaxDistance");
    static readonly int OpacityId = Shader.PropertyToID("_ShadowOpacity");

    Transform model, arch;
    MeshRenderer slabBand, footprintBand, leftBand, rightBand;
    MaterialPropertyBlock block;

    void LateUpdate()
    {
        var currentModel = placement != null ? placement.Model : null;
        if (currentModel != model)
        {
            model = currentModel;
            slabBand = footprintBand = null;
            if (model != null)
            {
                slabBand = Create("Contact Shadow (base)", slab, model, lift);
                footprintBand = Create("Contact Shadow (walls)", footprint, model, streetLevel + lift);
            }
        }
        var currentArch = archModel != null ? archModel.Root : null;
        if (currentArch != arch)
        {
            arch = currentArch;
            leftBand = rightBand = null;
            if (arch != null)
            {
                leftBand = Create("Contact Shadow (left pier)", leftPier, arch, lift);
                rightBand = Create("Contact Shadow (right pier)", rightPier, arch, lift);
            }
        }
        if (model == null) return;

        float academy = archModel != null ? Solidity(archModel.AcademyFade) : 1f;
        bool baseShown = placement.PlatformVisible;
        Apply(slabBand, slab, baseShown ? academy : 0f);
        Apply(footprintBand, footprint, baseShown ? 0f : academy);
        Apply(leftBand, leftPier, baseShown ? 0f : academy * Pier(0));
        Apply(rightBand, rightPier, baseShown ? 0f : academy * Pier(1));
    }

    /// A piece's (0 left, 1 right pier) share of the shadow: by how solid it is and how high it is lifted.
    float Pier(int piece)
    {
        if (archModel == null || !archModel.TryGetPiece(piece, out float fade, out Vector3 offset)) return 0f;
        float raised = liftFade > 0f ? Mathf.Clamp01(1f - offset.y / liftFade) : 1f;
        return Solidity(fade) * raised;
    }

    // _PieceFade 0 = solid .. 1 = gone; a ghost (ArchModel.ghostFade 0.72) casts none.
    static float Solidity(float fade) => Mathf.Clamp01(1f - fade / 0.7f);

    MeshRenderer Create(string name, ContactShadowShape shape, Transform parent, float height)
    {
        if (shape == null || shape.texture == null || material == null) return null;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, height, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = shape.Quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return r;
    }

    /// Always a property block (also at 0): see ArchModel.SetFade, the SRP Batcher would reuse another's values.
    void Apply(MeshRenderer r, ContactShadowShape shape, float opacity)
    {
        if (r == null) return;
        r.enabled = opacity > 0.001f;
        block ??= new MaterialPropertyBlock();
        block.SetTexture(TextureId, shape.texture);
        block.SetFloat(MaxDistanceId, shape.maxDistance);
        block.SetFloat(OpacityId, opacity);
        r.SetPropertyBlock(block);
    }
}
