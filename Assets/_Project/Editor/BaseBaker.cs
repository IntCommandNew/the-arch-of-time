using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool: bakes a longer academy base (the "Platform" slab) so the arch, which stands out from the front
/// wall (-X), rests on it instead of half on the slab and half in the air (user, 22 Nov 2026).
///
/// The slab keeps its thickness, its two materials (base = top, slabside = chamfer, sides, bottom) and the
/// FBX's top UVs, so the building's outline baked into base.jpg still lines up with the walls. Only the -X end
/// moves out to frontEdge; the added top repeats the plain paving band at the texture's -X edge (mirrored strips,
/// so there is no seam). The mesh is written to academy_base.asset and set on the prefab's Platform.
///
/// Menu: Tools > The Arch of Time > Bake academy base. Re-bake after a new model iteration (if the slab changes).
/// </summary>
public static class BaseBaker
{
    const string PrefabPath = "Assets/_Project/Prefabs/academy.prefab";
    const string PlatformChild = "Platform";
    public const string OutputPath = "Assets/_Project/Models/Academy/academy_base.asset";

    // The FBX slab (model iteration 4), at scale 1 (metres).
    const float HalfX = 0.3570f;     // outer half length (X)
    const float HalfZ = 0.3066f;     // outer half width (Z)
    const float Top = 0.0147f;       // top = street level
    const float Chamfer = 0.0006f;   // the top edge's 45 degrees chamfer
    const float SideUvPerMetre = 4.76f; // slabside's tiling in the FBX
    const float SideUvV0 = 0.95f;       // slabside's v at y = 0

    // The arch's outer plinth ends at x -0.524 (ArchModel position -0.395 - footprint 0.129): 3 cm more.
    public const float DefaultFrontEdge = -0.555f;
    // Width of one added strip: the plain band at the texture's -X edge is ~6 cm deep, stay inside it.
    const float MaxStrip = 0.04f;

    [MenuItem("Tools/The Arch of Time/Bake academy base")]
    static void BakeFromMenu() => Debug.Log("[BaseBaker] " + Bake(DefaultFrontEdge));

    public static string Bake(float frontEdge)
    {
        if (frontEdge > -HalfX) return $"frontEdge {frontEdge} must be <= {-HalfX}";
        var mesh = Build(frontEdge);

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(OutputPath);
        if (existing != null)
        {
            // Keep the asset (and its GUID): copy the new data into it.
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = "academy_base";
            Object.DestroyImmediate(mesh);
            mesh = existing;
            EditorUtility.SetDirty(mesh);
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, OutputPath);
        }
        AssetDatabase.SaveAssets();

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var platform = root.transform.Find(PlatformChild);
            if (platform == null) return $"no {PlatformChild} in {PrefabPath}";
            platform.GetComponent<MeshFilter>().sharedMesh = mesh;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return $"{OutputPath}: x {frontEdge:F3}..{HalfX:F3}, z +/-{HalfZ:F3}, {mesh.vertexCount} verts, " +
               $"bounds {mesh.bounds}";
    }

    static Mesh Build(float frontEdge)
    {
        var b = new Builder();
        float x0 = frontEdge, x1 = HalfX, z0 = -HalfZ, z1 = HalfZ;
        float ix0 = x0 + Chamfer, ix1 = x1 - Chamfer, iz0 = z0 + Chamfer, iz1 = z1 - Chamfer;
        float low = Top - Chamfer;

        // Top (base.jpg): the FBX's own quad and UVs, u = across Z, v = from +X to -X.
        float origX = HalfX - Chamfer; // 0.3564
        Vector2 TopUv(float x, float z) => new Vector2(
            0.001f + 0.998f * (z - iz0) / (iz1 - iz0),
            0.001f + 0.998f * (origX - x) / (2f * origX));
        b.Quad(0, Vector3.up,
            new Vector3(origX, Top, iz0), new Vector3(origX, Top, iz1),
            new Vector3(-origX, Top, iz1), new Vector3(-origX, Top, iz0),
            TopUv(origX, iz0), TopUv(origX, iz1), TopUv(-origX, iz1), TopUv(-origX, iz0));

        // The added top, from -origX out to the new edge: strips that go back and forth over the plain band.
        float length = -origX - ix0;
        int strips = Mathf.Max(1, Mathf.CeilToInt(length / MaxStrip));
        float w = length / strips;
        float dv = 0.998f * w / (2f * origX);
        for (int i = 0; i < strips; i++)
        {
            float xa = -origX - i * w, xb = xa - w;
            float va = i % 2 == 0 ? 0.999f : 0.999f - dv;
            float vb = i % 2 == 0 ? 0.999f - dv : 0.999f;
            float ua = TopUv(0f, iz0).x, ub = TopUv(0f, iz1).x;
            b.Quad(0, Vector3.up,
                new Vector3(xa, Top, iz0), new Vector3(xa, Top, iz1),
                new Vector3(xb, Top, iz1), new Vector3(xb, Top, iz0),
                new Vector2(ua, va), new Vector2(ub, va), new Vector2(ub, vb), new Vector2(ua, vb));
        }

        // Chamfers and sides (slabside.jpg): planar UVs, horizontal = along the side, vertical = height.
        float k = SideUvPerMetre;
        Vector2 SideUv(float along, float y) => new Vector2(along * k, SideUvV0 + y * k);
        var c45 = 0.70710678f;

        // +X side
        b.Quad(1, new Vector3(c45, c45, 0f),
            new Vector3(ix1, Top, iz0), new Vector3(x1, low, z0), new Vector3(x1, low, z1), new Vector3(ix1, Top, iz1),
            SideUv(z0, Top), SideUv(z0, low), SideUv(z1, low), SideUv(z1, Top));
        b.Quad(1, Vector3.right,
            new Vector3(x1, low, z0), new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), new Vector3(x1, low, z1),
            SideUv(z0, low), SideUv(z0, 0f), SideUv(z1, 0f), SideUv(z1, low));
        // -X side
        b.Quad(1, new Vector3(-c45, c45, 0f),
            new Vector3(ix0, Top, iz1), new Vector3(x0, low, z1), new Vector3(x0, low, z0), new Vector3(ix0, Top, iz0),
            SideUv(-z1, Top), SideUv(-z1, low), SideUv(-z0, low), SideUv(-z0, Top));
        b.Quad(1, Vector3.left,
            new Vector3(x0, low, z1), new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), new Vector3(x0, low, z0),
            SideUv(-z1, low), SideUv(-z1, 0f), SideUv(-z0, 0f), SideUv(-z0, low));
        // +Z side
        b.Quad(1, new Vector3(0f, c45, c45),
            new Vector3(ix1, Top, iz1), new Vector3(x1, low, z1), new Vector3(x0, low, z1), new Vector3(ix0, Top, iz1),
            SideUv(-x1, Top), SideUv(-x1, low), SideUv(-x0, low), SideUv(-x0, Top));
        b.Quad(1, Vector3.forward,
            new Vector3(x1, low, z1), new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), new Vector3(x0, low, z1),
            SideUv(-x1, low), SideUv(-x1, 0f), SideUv(-x0, 0f), SideUv(-x0, low));
        // -Z side
        b.Quad(1, new Vector3(0f, c45, -c45),
            new Vector3(ix0, Top, iz0), new Vector3(x0, low, z0), new Vector3(x1, low, z0), new Vector3(ix1, Top, iz0),
            SideUv(x0, Top), SideUv(x0, low), SideUv(x1, low), SideUv(x1, Top));
        b.Quad(1, Vector3.back,
            new Vector3(x0, low, z0), new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), new Vector3(x1, low, z0),
            SideUv(x0, low), SideUv(x0, 0f), SideUv(x1, 0f), SideUv(x1, low));
        // Bottom
        b.Quad(1, Vector3.down,
            new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0),
            new Vector2(x0 * k, z0 * k), new Vector2(x0 * k, z1 * k), new Vector2(x1 * k, z1 * k),
            new Vector2(x1 * k, z0 * k));

        return b.ToMesh("academy_base");
    }

    class Builder
    {
        readonly List<Vector3> positions = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int>[] triangles = { new List<int>(), new List<int>() };

        /// A flat quad a-b-c-d (a ring, either winding): turned so its front faces the given normal.
        public void Quad(int submesh, Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                         Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = positions.Count;
            positions.AddRange(new[] { a, b, c, d });
            uvs.AddRange(new[] { ua, ub, uc, ud });
            for (int n = 0; n < 4; n++) normals.Add(normal.normalized);
            // Unity's front faces are clockwise seen from the front: the cross product points along the normal.
            bool clockwise = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f;
            var t = triangles[submesh];
            if (clockwise) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(triangles[0], 0);
            mesh.SetTriangles(triangles[1], 1);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
