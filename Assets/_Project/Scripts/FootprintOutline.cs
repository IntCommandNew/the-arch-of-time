using UnityEngine;

/// <summary>
/// The building's footprint as a gold outline while adjusting, to line it up with real things
/// (a street edge, a curb, a wall), especially at real size when the building is too big to see whole.
///
/// The outline is a mesh baked from the model (Tools > The Arch of Time > Bake footprint outline),
/// shown as a child of the model at street level, so it moves, turns and scales with it; at real size
/// with street level on the ground it lies on the real ground. The Overlay material draws it on top
/// of everything, so it shows through the walls.
/// </summary>
public class FootprintOutline : MonoBehaviour
{
    [SerializeField] PlacementController placement;
    [Tooltip("academy_footprint.asset (baked by FootprintBaker).")]
    [SerializeField] Mesh footprintMesh;
    [Tooltip("A material using \"TheArchOfTime/Overlay\".")]
    [SerializeField] Material outlineMaterial;
    [Tooltip("Height of the outline in the model at scale 1: street level, where the paved ground meets " +
             "the walls (0.0147; not the figure's feet, which stand on raised grass at 0.05).")]
    [SerializeField] float heightInModel = 0.0147f;

    bool visible;
    Transform outline;
    Transform outlineModel; // the model the outline is attached to

    public void SetVisible(bool value)
    {
        visible = value;
        Refresh();
    }

    void LateUpdate() => Refresh();

    void Refresh()
    {
        Transform model = placement != null ? placement.Model : null;
        if (!visible || model == null || footprintMesh == null || outlineMaterial == null)
        {
            if (outline != null) outline.gameObject.SetActive(false);
            return;
        }

        // A new model (placed again or restored): make a new outline under it.
        if (outline == null || outlineModel != model)
        {
            if (outline != null) Destroy(outline.gameObject);
            outline = CreateOutline(model);
            outlineModel = model;
        }
        outline.gameObject.SetActive(true);
    }

    Transform CreateOutline(Transform model)
    {
        var go = new GameObject("Footprint Outline");
        go.transform.SetParent(model, false);
        go.transform.localPosition = new Vector3(0f, heightInModel, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = footprintMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = outlineMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }
}
