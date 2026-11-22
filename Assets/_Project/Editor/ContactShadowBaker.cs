using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool: bakes the contact shadow's shapes (ContactShadowShape assets, used by ContactShadow):
/// - academy_contact_slab: the base slab's rectangle (academy_base.asset's bounds), for when the base shows;
/// - academy_contact_footprint: the building's outside wall slice (as FootprintBaker), for when it is hidden;
/// - arch_contact_left / _right: each pier's plinth slice (as FootprintBaker.BakeArch), in the arch's space.
/// Each texture holds the distance from the outline (0 on and inside it), so concave corners stay clean.
///
/// Menu: Tools > The Arch of Time > Bake contact shadows. Re-bake after a new model iteration or base.
/// </summary>
public static class ContactShadowBaker
{
    const string AcademyPrefab = "Assets/_Project/Prefabs/academy.prefab";
    const string ArchPath = "Assets/_Project/Models/Arch/arch.fbx";
    const string AcademyFolder = "Assets/_Project/Models/Academy/";
    const string ArchFolder = "Assets/_Project/Models/Arch/";

    // Model units (scale 1). The shader's band width must stay below MaxDistance.
    const float MaxDistance = 0.12f; // the panel's width slider goes up to 12 cm
    const float PixelsPerMetre = 500f; // 2 mm a pixel at tabletop size
    const float FootprintSlice = 0.016f; // FootprintBaker's: just above the building's bottom
    const float PierSlice = 0.010f;      // FootprintBaker.BakeArch's

    [MenuItem("Tools/The Arch of Time/Bake contact shadows")]
    static void BakeFromMenu() => Debug.Log("[ContactShadowBaker] " + BakeAll());

    public static string BakeAll()
    {
        var log = new System.Text.StringBuilder();

        var baseMesh = AssetDatabase.LoadAssetAtPath<Mesh>(BaseBaker.OutputPath);
        if (baseMesh == null) return "no " + BaseBaker.OutputPath + " (Bake academy base first)";
        var b = baseMesh.bounds;
        var slab = new List<Vector2>
        {
            new Vector2(b.min.x, b.min.z), new Vector2(b.min.x, b.max.z),
            new Vector2(b.max.x, b.max.z), new Vector2(b.max.x, b.min.z), new Vector2(b.min.x, b.min.z)
        };
        log.AppendLine(Write(AcademyFolder + "academy_contact_slab.asset", new List<List<Vector2>> { slab }));

        var walls = FootprintBaker.Slice(AssetDatabase.LoadAssetAtPath<GameObject>(AcademyPrefab),
                                         new[] { "Building" }, FootprintSlice, out string problem);
        if (walls == null) return problem;
        log.AppendLine(Write(AcademyFolder + "academy_contact_footprint.asset", new List<List<Vector2>>
        {
            Longest(walls) // the outside of the walls (the open bottom also gives the inside)
        }));

        var arch = AssetDatabase.LoadAssetAtPath<GameObject>(ArchPath);
        var piers = new[] { ("LeftColumn", "arch_contact_left"), ("RightColumn", "arch_contact_right") };
        foreach (var (child, file) in piers)
        {
            var pier = FootprintBaker.Slice(arch, new[] { child }, PierSlice, out problem);
            if (pier == null) return problem;
            log.AppendLine(Write(ArchFolder + file + ".asset", new List<List<Vector2>> { Longest(pier) }));
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static List<Vector2> Longest(List<List<Vector2>> lines)
    {
        List<Vector2> best = null;
        float bestLength = -1f;
        foreach (var line in lines)
        {
            float length = 0f;
            for (int i = 1; i < line.Count; i++) length += Vector2.Distance(line[i - 1], line[i]);
            if (length > bestLength) { best = line; bestLength = length; }
        }
        return best;
    }

    static string Write(string path, List<List<Vector2>> loops)
    {
        // The outline's segments (each loop closed), and the texture's rectangle around them.
        var segments = new List<(Vector2 a, Vector2 b)>();
        var lo = new Vector2(float.MaxValue, float.MaxValue);
        var hi = new Vector2(float.MinValue, float.MinValue);
        foreach (var loop in loops)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                lo = Vector2.Min(lo, loop[i]);
                hi = Vector2.Max(hi, loop[i]);
                var next = loop[(i + 1) % loop.Count];
                if ((next - loop[i]).sqrMagnitude > 1e-12f) segments.Add((loop[i], next));
            }
        }
        lo -= Vector2.one * MaxDistance;
        hi += Vector2.one * MaxDistance;
        var size = hi - lo;
        int w = Mathf.Max(8, Mathf.CeilToInt(size.x * PixelsPerMetre));
        int h = Mathf.Max(8, Mathf.CeilToInt(size.y * PixelsPerMetre));

        var pixels = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Texel centre in the model's XZ plane (u along x, v along z).
                var p = new Vector2(lo.x + (x + 0.5f) / w * size.x, lo.y + (y + 0.5f) / h * size.y);
                float d = Inside(p, segments) ? 0f : Distance(p, segments);
                pixels[y * w + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(d / MaxDistance) * 255f);
            }
        }
        var texture = new Texture2D(w, h, TextureFormat.R8, false, true)
        {
            name = System.IO.Path.GetFileNameWithoutExtension(path),
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        texture.SetPixelData(pixels, 0);
        texture.Apply(false, false);

        var shape = AssetDatabase.LoadAssetAtPath<ContactShadowShape>(path);
        if (shape == null)
        {
            shape = ScriptableObject.CreateInstance<ContactShadowShape>();
            AssetDatabase.CreateAsset(shape, path);
        }
        if (shape.texture != null) AssetDatabase.RemoveObjectFromAsset(shape.texture);
        AssetDatabase.AddObjectToAsset(texture, shape);
        shape.texture = texture;
        shape.min = lo;
        shape.size = size;
        shape.maxDistance = MaxDistance;
        EditorUtility.SetDirty(shape);
        return $"{path}: {segments.Count} edges, {w} x {h} px, x {lo.x:F3}..{hi.x:F3}, z {lo.y:F3}..{hi.y:F3}";
    }

    // Even-odd rule: a ray towards +x crosses the outline an odd number of times from inside.
    static bool Inside(Vector2 p, List<(Vector2 a, Vector2 b)> segments)
    {
        bool inside = false;
        foreach (var (a, b) in segments)
        {
            if ((a.y > p.y) == (b.y > p.y)) continue;
            float x = a.x + (p.y - a.y) / (b.y - a.y) * (b.x - a.x);
            if (x > p.x) inside = !inside;
        }
        return inside;
    }

    static float Distance(Vector2 p, List<(Vector2 a, Vector2 b)> segments)
    {
        float best = float.MaxValue;
        foreach (var (a, b) in segments)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
        }
        return Mathf.Sqrt(best);
    }
}
