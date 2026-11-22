using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// Tools for checking a new model before / after swapping it into the project: Describe (hierarchy,
/// meshes, materials, import settings), Islands (connected parts of each mesh: can it be split?), StreetLevel
/// (the paving height just outside the footprint outline), Render (a model to a PNG). Used via the Unity CLI.
public static class ModelTools
{
    public static string Describe(string path)
    {
        var sb = new StringBuilder();
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        sb.AppendLine($"{path}: scale {importer.globalScale} fileScale {importer.useFileScale} " +
                      $"normals {importer.importNormals} tangents {importer.importTangents} " +
                      $"materials {importer.materialImportMode} location {importer.materialLocation} " +
                      $"readable {importer.isReadable}");
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var bounds = new Bounds();
        bool first = true;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string line = "  " + PathOf(t, root.transform) + $" pos {t.localPosition:F4} rot {t.localEulerAngles:F1} " +
                          $"scale {t.localScale:F3}";
            var filter = t.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                var mesh = filter.sharedMesh;
                line += $" mesh {mesh.name} v {mesh.vertexCount} tris {mesh.triangles.Length / 3}" +
                        $" sub {mesh.subMeshCount}";
            }
            var renderer = t.GetComponent<Renderer>();
            if (renderer != null)
            {
                var names = new List<string>();
                foreach (var m in renderer.sharedMaterials) names.Add(m != null ? m.name : "null");
                line += " mats [" + string.Join(", ", names) + "]";
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            sb.AppendLine(line);
        }
        sb.AppendLine($"  bounds centre {bounds.center:F4} size {bounds.size:F4} min {bounds.min:F4} " +
                      $"max {bounds.max:F4}");
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Material mat)
            {
                var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
                string colour = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString("F3") : "-";
                string smooth = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness").ToString("F2") : "-";
                sb.AppendLine($"  material {mat.name}: colour {colour} tex {(tex != null ? tex.name : "-")} " +
                              $"smoothness {smooth} shader {mat.shader.name}");
            }
            else if (asset is Texture2D t2)
            {
                sb.AppendLine($"  texture {t2.name} {t2.width}x{t2.height}");
            }
        }
        return sb.ToString();
    }

    /// Connected parts of each mesh (vertices joined by triangles; vertices at the same spot count as joined).
    public static string Islands(string path)
    {
        var sb = new StringBuilder();
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            var parent = new int[vertices.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            var bySpot = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * 100000f);
                if (bySpot.TryGetValue(key, out int other)) Union(parent, i, other);
                else bySpot[key] = i;
            }
            var tris = mesh.triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Union(parent, tris[i], tris[i + 1]);
                Union(parent, tris[i], tris[i + 2]);
            }
            var islands = new Dictionary<int, (Bounds b, int tris)>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                int r = Find(parent, tris[i]);
                var p = vertices[tris[i]];
                if (!islands.TryGetValue(r, out var entry)) entry = (new Bounds(p, Vector3.zero), 0);
                entry.b.Encapsulate(vertices[tris[i]]);
                entry.b.Encapsulate(vertices[tris[i + 1]]);
                entry.b.Encapsulate(vertices[tris[i + 2]]);
                entry.tris++;
                islands[r] = entry;
            }
            // Open edges: an edge (between two spots) used by only one triangle = a hole in the surface.
            var edges = new Dictionary<(int, int), int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = bySpot[Vector3Int.RoundToInt(vertices[tris[i + k]] * 100000f)];
                    int b = bySpot[Vector3Int.RoundToInt(vertices[tris[i + (k + 1) % 3]] * 100000f)];
                    if (a == b) continue;
                    var key = a < b ? (a, b) : (b, a);
                    edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            int open = 0;
            foreach (int count in edges.Values)
                if (count == 1) open++;
            sb.AppendLine($"{filter.name}: {islands.Count} islands, {tris.Length / 3} tris, {open} open edges " +
                          (open == 0 ? "(closed)" : "(holes)"));
            var list = new List<(Bounds b, int tris)>(islands.Values);
            list.Sort((a, c) => c.tris.CompareTo(a.tris));
            for (int i = 0; i < list.Count && i < 60; i++)
                sb.AppendLine($"  {list[i].tris} tris centre {list[i].b.center:F3} size {list[i].b.size:F3}");
        }
        return sb.ToString();
    }

    /// Where each mesh's open edges are: on one of its bounding faces (min / max x, y, z: where a piece was cut)
    /// or inside, with the length of the open edges on each.
    public static string OpenEdges(string path)
    {
        var sb = new StringBuilder();
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            var v = mesh.vertices;
            var b = mesh.bounds;
            var spot = new Dictionary<Vector3Int, int>();
            var points = new List<Vector3>();
            int Id(Vector3 p)
            {
                var key = Vector3Int.RoundToInt(p * 100000f);
                if (!spot.TryGetValue(key, out int id))
                {
                    id = points.Count;
                    spot[key] = id;
                    points.Add(p);
                }
                return id;
            }
            var edges = new Dictionary<(int, int), int>();
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = Id(v[t[i + k]]), c = Id(v[t[i + (k + 1) % 3]]);
                    if (a == c) continue;
                    var key = a < c ? (a, c) : (c, a);
                    edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            var where = new Dictionary<string, (int count, float length)>();
            const float tolerance = 0.0005f;
            foreach (var kv in edges)
            {
                if (kv.Value != 1) continue;
                var p = points[kv.Key.Item1];
                var q = points[kv.Key.Item2];
                string side = "inside";
                if (Mathf.Abs(p.x - b.min.x) < tolerance && Mathf.Abs(q.x - b.min.x) < tolerance) side = "min x";
                else if (Mathf.Abs(p.x - b.max.x) < tolerance && Mathf.Abs(q.x - b.max.x) < tolerance) side = "max x";
                else if (Mathf.Abs(p.y - b.min.y) < tolerance && Mathf.Abs(q.y - b.min.y) < tolerance) side = "min y";
                else if (Mathf.Abs(p.y - b.max.y) < tolerance && Mathf.Abs(q.y - b.max.y) < tolerance) side = "max y";
                else if (Mathf.Abs(p.z - b.min.z) < tolerance && Mathf.Abs(q.z - b.min.z) < tolerance) side = "min z";
                else if (Mathf.Abs(p.z - b.max.z) < tolerance && Mathf.Abs(q.z - b.max.z) < tolerance) side = "max z";
                where.TryGetValue(side, out var entry);
                where[side] = (entry.count + 1, entry.length + Vector3.Distance(p, q));
            }
            sb.Append($"{filter.name} (x {b.min.x:F3}..{b.max.x:F3}, y {b.min.y:F3}..{b.max.y:F3}):");
            foreach (var kv in where) sb.Append($" {kv.Key} {kv.Value.count} ({kv.Value.length:F3} m);");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// The ground's height just outside the building: points of the baked footprint outline pushed outward by
    /// `outside`, then the highest GrassGravel / Platform triangle under each (vertical line down).
    public static string StreetLevel(string fbx, string footprintAsset, float outside)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var tris = new List<(Vector3 a, Vector3 b, Vector3 c, string what)>();
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.name != "GrassGravel" && filter.name != "Platform") continue;
            var mesh = filter.sharedMesh;
            var v = mesh.vertices;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var mats = filter.GetComponent<Renderer>().sharedMaterials;
                string what = filter.name + ":" + (s < mats.Length && mats[s] != null ? mats[s].name : "?");
                var t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3) tris.Add((v[t[i]], v[t[i + 1]], v[t[i + 2]], what));
            }
        }
        var outline = AssetDatabase.LoadAssetAtPath<Mesh>(footprintAsset).vertices;
        var centre = Vector3.zero;
        foreach (var p in outline) centre += p;
        centre /= outline.Length;
        var heights = new Dictionary<string, int>();
        var byWhat = new Dictionary<string, int>();
        int samples = 0, step = Mathf.Max(1, outline.Length / 60);
        for (int i = 0; i < outline.Length; i += step)
        {
            var p = outline[i];
            var dir = new Vector3(p.x - centre.x, 0f, p.z - centre.z).normalized;
            var q = p + dir * outside;
            float best = float.NegativeInfinity;
            string bestWhat = "none";
            foreach (var tri in tris)
            {
                if (!Under(q, tri.a, tri.b, tri.c, out float y)) continue;
                if (y > best) { best = y; bestWhat = tri.what; }
            }
            samples++;
            string key = float.IsNegativeInfinity(best) ? "none" : best.ToString("F4");
            heights[key] = heights.TryGetValue(key, out int n) ? n + 1 : 1;
            byWhat[bestWhat] = byWhat.TryGetValue(bestWhat, out int m) ? m + 1 : 1;
        }
        var sb = new StringBuilder($"{samples} samples, {outside} m outside the outline\n");
        foreach (var kv in heights) sb.AppendLine($"  y {kv.Key}: {kv.Value}");
        foreach (var kv in byWhat) sb.AppendLine($"  on {kv.Key}: {kv.Value}");
        return sb.ToString();
    }

    static bool Under(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float y)
    {
        y = 0f;
        float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
        if (Mathf.Abs(d) < 1e-12f) return false;
        float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d;
        float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d;
        float w = 1f - u - v;
        if (u < -1e-6f || v < -1e-6f || w < -1e-6f) return false;
        y = u * a.y + v * b.y + w * c.y;
        return true;
    }

    /// Renders a model to a PNG: camera at (yaw, pitch) degrees around the model's bounds centre (yaw 0 looks
    /// from +Z towards -Z).
    public static string Render(string assetPath, float yaw, float pitch, string png, int size = 1024,
                                string onlyChild = null, bool transparent = false, bool withArch = false)
    {
        var preview = new PreviewRenderUtility();
        try
        {
            var go = preview.InstantiatePrefabInScene(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
            if (withArch)
            {
                // The arch where ArchModel puts it by the academy (its default position, at street level).
                var arch = preview.InstantiatePrefabInScene(
                    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/Arch/arch.fbx"));
                arch.transform.SetParent(go.transform, false);
                arch.transform.localPosition = new Vector3(-0.395f, 0.0147f, -0.19f);
            }
            if (!string.IsNullOrEmpty(onlyChild))
                foreach (Transform child in go.transform) child.gameObject.SetActive(child.name == onlyChild);
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            float radius = bounds.extents.magnitude;
            var cam = preview.camera;
            cam.fieldOfView = 30f;
            cam.nearClipPlane = radius * 0.05f;
            cam.farClipPlane = radius * 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = transparent ? Color.clear : new Color(0.55f, 0.6f, 0.65f);
            var rot = Quaternion.Euler(pitch, yaw + 180f, 0f);
            cam.transform.position = bounds.center - rot * Vector3.forward * radius * 3.6f;
            cam.transform.rotation = rot;
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(40f, yaw + 150f, 0f);
            preview.lights[1].intensity = 0.6f;
            preview.ambientColor = new Color(0.45f, 0.45f, 0.45f);
            preview.BeginStaticPreview(new Rect(0, 0, size, size));
            preview.Render(true);
            Texture2D tex;
            if (transparent)
            {
                // EndStaticPreview returns RGB only: read the camera's target (with alpha) instead.
                var target = cam.targetTexture;
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                tex = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                // The target holds linear colours (the project is in linear space): to sRGB for a normal PNG.
                var pixels = tex.GetPixels();
                for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
                tex.SetPixels(pixels);
                tex.Apply();
                RenderTexture.active = previous;
                preview.EndStaticPreview();
            }
            else
            {
                tex = preview.EndStaticPreview();
            }
            System.IO.File.WriteAllBytes(png, tex.EncodeToPNG());
            return png;
        }
        finally
        {
            preview.Cleanup();
        }
    }

    static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }
        return i;
    }

    static void Union(int[] parent, int a, int b)
    {
        a = Find(parent, a);
        b = Find(parent, b);
        if (a != b) parent[a] = b;
    }

    static string PathOf(Transform t, Transform root)
    {
        string p = t.name;
        while (t != root && t.parent != null)
        {
            t = t.parent;
            p = t.name + "/" + p;
        }
        return p;
    }
}
