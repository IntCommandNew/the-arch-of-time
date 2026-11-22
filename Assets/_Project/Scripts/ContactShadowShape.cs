using UnityEngine;

/// <summary>
/// One outline for the contact shadow (ContactShadow): a texture of the distance from the outline, outside it,
/// over a rectangle of the model's XZ plane (scale 1, metres). Baked by ContactShadowBaker
/// (Tools > The Arch of Time > Bake contact shadows); the texture is a sub-asset.
/// </summary>
public class ContactShadowShape : ScriptableObject
{
    [Tooltip("R8: 0 = on or inside the outline, 1 = maxDistance or more away from it.")]
    public Texture2D texture;
    [Tooltip("The texture's rectangle in the model's XZ plane: lowest corner (x, z).")]
    public Vector2 min;
    [Tooltip("The texture's rectangle in the model's XZ plane: size (x, z).")]
    public Vector2 size;
    [Tooltip("Distance (model units) that the texture's value 1 stands for.")]
    public float maxDistance = 0.05f;

    Mesh quad;

    /// <summary>A flat quad (facing up, y = 0) over the texture's rectangle, made once.</summary>
    public Mesh Quad
    {
        get
        {
            if (quad != null) return quad;
            float x0 = min.x, z0 = min.y, x1 = min.x + size.x, z1 = min.y + size.y;
            quad = new Mesh { name = name + " quad" };
            quad.SetVertices(new[]
            {
                new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0)
            });
            quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
            quad.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            quad.RecalculateBounds();
            return quad;
        }
    }
}
