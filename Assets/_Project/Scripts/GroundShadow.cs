using UnityEngine;

/// <summary>
/// The building's shadow on the real ground: a flat quad with the ShadowCatcher material (invisible
/// except where the shadow falls), lying on the anchor's ground level under the model. It follows the
/// model's position and size as it is moved or scaled; where the model's own base is, the base hides it.
///
/// Setup: on App, with the PlacementController and a material using "TheArchOfTime/ShadowCatcher"
/// (the scene reference also keeps the shader in the build). The quad is created at runtime.
/// </summary>
public class GroundShadow : MonoBehaviour
{
    [SerializeField] PlacementController placement;
    [SerializeField] Material shadowMaterial;

    [Tooltip("Quad width as a multiple of the model's size (long shadows from a low sun need room).")]
    [SerializeField] float sizeFactor = 3f;
    [Tooltip("Model size at scale 1 (metres): the larger footprint side of the academy model.")]
    [SerializeField] float modelSizeAtScale1 = 0.714f;
    [Tooltip("Lift above the ground (metres), against flicker with the model's base.")]
    [SerializeField] float lift = 0.003f;

    Transform quad;

    void LateUpdate()
    {
        Transform model = placement != null ? placement.Model : null;
        Transform anchor = placement != null ? placement.AnchorTransform : null;
        if (model == null || anchor == null || shadowMaterial == null)
        {
            if (quad != null) quad.gameObject.SetActive(false);
            return;
        }

        if (quad == null) quad = CreateQuad();
        if (quad.parent != anchor) quad.SetParent(anchor, false);
        quad.gameObject.SetActive(true);

        // On the anchor's ground (local y = 0), centred under the model, as wide as needed.
        Vector3 local = anchor.InverseTransformPoint(model.position);
        quad.localPosition = new Vector3(local.x, lift, local.z);
        quad.localRotation = Quaternion.Euler(90f, 0f, 0f); // the quad faces up
        float size = modelSizeAtScale1 * model.localScale.x * sizeFactor;
        quad.localScale = new Vector3(size, size, 1f);
    }

    Transform CreateQuad()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Ground Shadow";
        Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = shadowMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = true;
        return go.transform;
    }

    void OnDisable()
    {
        if (quad != null) quad.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (quad != null) Destroy(quad.gameObject);
    }
}
