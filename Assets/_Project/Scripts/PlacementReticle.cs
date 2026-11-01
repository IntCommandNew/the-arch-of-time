using UnityEngine;

/// <summary>
/// The ring that shows where "Place here" will put the model: a flat ring with a centre dot, lying on
/// the detected ground at the centre of the screen. PlacementController moves, shows and hides it.
///
/// Setup: a GameObject with this component and an unlit material; the mesh is built in code.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PlacementReticle : MonoBehaviour
{
    [Header("Shape (metres)")]
    [Tooltip("0.4 m roughly matches the model's footprint at tabletop size, so the ring previews its size.")]
    [SerializeField] float outerRadius = 0.4f;
    [SerializeField] float ringWidth = 0.025f;
    [Tooltip("The dot marks the exact point where the anchor goes.")]
    [SerializeField] float dotRadius = 0.03f;
    [SerializeField] int segments = 64;

    [Header("Motion")]
    [Tooltip("Lifts the ring above the ground so it doesn't flicker against the plane visual.")]
    [SerializeField] float lift = 0.005f;
    [Tooltip("How quickly the ring follows the aim point (higher = snappier, lower = smoother).")]
    [SerializeField] float followSpeed = 15f;

    MeshRenderer meshRenderer;

    public bool IsVisible => meshRenderer != null && meshRenderer.enabled;

    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        GetComponent<MeshFilter>().sharedMesh = BuildMesh();
        Hide();
    }

    /// <summary>Shows the ring at <paramref name="pose"/>: jumps there when appearing, glides after that.</summary>
    public void Show(Pose pose)
    {
        Vector3 position = pose.position + Vector3.up * lift;
        if (!IsVisible)
        {
            transform.SetPositionAndRotation(position, pose.rotation);
        }
        else
        {
            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, position, t),
                                             Quaternion.Slerp(transform.rotation, pose.rotation, t));
        }
        meshRenderer.enabled = true;
    }

    public void Hide()
    {
        if (meshRenderer != null) meshRenderer.enabled = false;
    }

    /// Ring + centre dot in the XZ plane, facing up.
    Mesh BuildMesh()
    {
        int n = Mathf.Max(8, segments);
        var vertices = new Vector3[n * 2 + n + 1];
        var triangles = new int[n * 6 + n * 3];
        float innerRadius = Mathf.Max(0f, outerRadius - ringWidth);

        for (int i = 0; i < n; i++)
        {
            float angle = i * Mathf.PI * 2f / n;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = direction * innerRadius;
            vertices[i * 2 + 1] = direction * outerRadius;
            vertices[n * 2 + i] = direction * dotRadius;
        }
        int centre = n * 3;
        vertices[centre] = Vector3.zero;

        int t = 0;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int inner = i * 2, outer = i * 2 + 1, innerNext = next * 2, outerNext = next * 2 + 1;
            // Clockwise seen from above, so the faces point up.
            triangles[t++] = inner; triangles[t++] = innerNext; triangles[t++] = outer;
            triangles[t++] = outer; triangles[t++] = innerNext; triangles[t++] = outerNext;

            triangles[t++] = centre; triangles[t++] = n * 2 + next; triangles[t++] = n * 2 + i;
        }

        var mesh = new Mesh { name = "Placement Reticle" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
