using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool: bakes the academy model's footprint outline into a mesh asset, used by FootprintOutline
/// while adjusting. The footprint is a horizontal slice through the "Building" mesh near its base
/// (so it has the real shape, octagonal bay included); short loops (drainpipes, posts) are dropped.
/// The result is a flat strip of quads (in the model's space, y = 0) that the runtime places at
/// street level under the model.
///
/// Menu: Tools > The Arch of Time > Bake footprint outline. Re-bake after a new model iteration.
/// </summary>
public static class FootprintBaker
{
    const string PrefabPath = "Assets/_Project/Prefabs/academy.prefab";
    const string BuildingChild = "Building";
    public const string OutputPath = "Assets/_Project/Models/Academy/academy_footprint.asset";

    // Defaults, at model scale 1 (metres). At y = 0.01 (the foundation, just above the mesh's bottom
    // at 0.0063) the slice is one clean closed loop, 1.92 m long, with the octagonal bay. Higher
    // slices add an inner loop and many tiny ones (drainpipes, posts).
    const float DefaultSliceHeight = 0.01f;
    const float DefaultLineWidth = 0.005f;
    const float DefaultMinLoopLength = 0.06f;

    [MenuItem("Tools/The Arch of Time/Bake footprint outline")]
    static void BakeFromMenu() =>
        Debug.Log("[FootprintBaker] " + Bake(DefaultSliceHeight, DefaultLineWidth, DefaultMinLoopLength));

    /// <summary>Slices the building, keeps the loops longer than minLoopLength, writes the mesh asset.</summary>
    public static string Bake(float sliceHeight, float lineWidth, float minLoopLength)
    {
        var lines = Slice(sliceHeight, out string problem);
        if (lines == null) return problem;

        var kept = new List<List<Vector2>>();
        var report = new StringBuilder();
        foreach (var line in lines)
            if (Length(line) >= minLoopLength) kept.Add(line);

        var mesh = BuildStripMesh(kept, lineWidth);
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(OutputPath);
        if (existing != null)
        {
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, OutputPath);
        }
        AssetDatabase.SaveAssets();

        report.Append($"slice y={sliceHeight}: {lines.Count} lines, kept {kept.Count} (>= {minLoopLength} m): ");
        foreach (var line in kept)
            report.Append($"[{line.Count} pts, {Length(line):0.000} m{(IsClosed(line) ? ", closed" : "")}] ");
        report.Append($"-> {OutputPath}");
        return report.ToString();
    }

    /// <summary>Describes the lines at a slice height (nothing is written): for choosing the height.</summary>
    public static string Describe(float sliceHeight)
    {
        var lines = Slice(sliceHeight, out string problem);
        if (lines == null) return problem;
        lines.Sort((a, b) => Length(b).CompareTo(Length(a)));
        var sb = new StringBuilder($"slice y={sliceHeight}: {lines.Count} lines. Longest: ");
        for (int i = 0; i < Mathf.Min(8, lines.Count); i++)
            sb.Append($"[{Length(lines[i]):0.000} m{(IsClosed(lines[i]) ? " closed" : "")}] ");
        return sb.ToString();
    }

    /// <summary>Draws the lines at a slice height, seen from above (+X = right, +Z = up), into a PNG.</summary>
    public static string SaveTopView(float sliceHeight, float minLoopLength, string pngPath, int size = 512)
    {
        var lines = Slice(sliceHeight, out string problem);
        if (lines == null) return problem;

        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(30, 30, 34, 255);
        const float half = 0.4f; // the model is about 0.71 x 0.61 m
        foreach (var line in lines)
        {
            var colour = Length(line) >= minLoopLength ? new Color32(240, 190, 70, 255) : new Color32(200, 70, 70, 255);
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b) / (2f * half) * size * 2f) + 1;
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                    int x = Mathf.RoundToInt((p.x + half) / (2f * half) * (size - 1));
                    int y = Mathf.RoundToInt((p.y + half) / (2f * half) * (size - 1));
                    if (x >= 0 && x < size && y >= 0 && y < size) pixels[y * size + x] = colour;
                }
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        System.IO.File.WriteAllBytes(pngPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        return $"wrote {pngPath} ({lines.Count} lines; gold = kept, red = shorter than {minLoopLength} m)";
    }

    // Slicing
    /// Lines (in the model's XZ plane) where the building mesh crosses the plane y = sliceHeight.
    static List<List<Vector2>> Slice(float sliceHeight, out string problem)
    {
        problem = null;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var building = prefab != null ? prefab.transform.Find(BuildingChild) : null;
        var mesh = building != null ? building.GetComponent<MeshFilter>()?.sharedMesh : null;
        if (mesh == null)
        {
            problem = $"No mesh found at {PrefabPath} / {BuildingChild}.";
            return null;
        }

        // Model space = the prefab root's space.
        Matrix4x4 toModel = prefab.transform.worldToLocalMatrix * building.localToWorldMatrix;
        var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = toModel.MultiplyPoint3x4(vertices[i]);
        var triangles = mesh.triangles;

        var segments = new List<(Vector2, Vector2)>();
        var crossing = new List<Vector2>(3);
        for (int t = 0; t < triangles.Length; t += 3)
        {
            crossing.Clear();
            AddCrossing(vertices[triangles[t]], vertices[triangles[t + 1]], sliceHeight, crossing);
            AddCrossing(vertices[triangles[t + 1]], vertices[triangles[t + 2]], sliceHeight, crossing);
            AddCrossing(vertices[triangles[t + 2]], vertices[triangles[t]], sliceHeight, crossing);
            if (crossing.Count == 2 && (crossing[0] - crossing[1]).sqrMagnitude > 1e-12f)
                segments.Add((crossing[0], crossing[1]));
        }
        return ChainSegments(segments);
    }

    static void AddCrossing(Vector3 a, Vector3 b, float y, List<Vector2> result)
    {
        // Half-open test, so a vertex exactly on the plane is counted once.
        if ((a.y < y) == (b.y < y)) return;
        float t = (y - a.y) / (b.y - a.y);
        Vector3 p = Vector3.Lerp(a, b, t);
        result.Add(new Vector2(p.x, p.z));
    }

    /// Joins segments that share end points into polylines (closed ones end where they start).
    static List<List<Vector2>> ChainSegments(List<(Vector2 a, Vector2 b)> segments)
    {
        Vector2Int Key(Vector2 p) => new Vector2Int(Mathf.RoundToInt(p.x * 100000f), Mathf.RoundToInt(p.y * 100000f));

        var byPoint = new Dictionary<Vector2Int, List<int>>();
        for (int i = 0; i < segments.Count; i++)
        {
            foreach (var p in new[] { segments[i].a, segments[i].b })
            {
                var k = Key(p);
                if (!byPoint.TryGetValue(k, out var list)) byPoint[k] = list = new List<int>(2);
                list.Add(i);
            }
        }

        var used = new bool[segments.Count];
        var lines = new List<List<Vector2>>();
        for (int start = 0; start < segments.Count; start++)
        {
            if (used[start]) continue;
            used[start] = true;
            var line = new List<Vector2> { segments[start].a, segments[start].b };
            Extend(line, atEnd: true);
            Extend(line, atEnd: false);
            lines.Add(line);
        }
        return lines;

        void Extend(List<Vector2> line, bool atEnd)
        {
            while (true)
            {
                Vector2 tip = atEnd ? line[line.Count - 1] : line[0];
                if (!byPoint.TryGetValue(Key(tip), out var candidates)) return;
                int next = -1;
                foreach (int c in candidates) if (!used[c]) { next = c; break; }
                if (next < 0) return;
                used[next] = true;
                Vector2 other = Key(segments[next].a) == Key(tip) ? segments[next].b : segments[next].a;
                if (atEnd) line.Add(other); else line.Insert(0, other);
            }
        }
    }

    static float Length(List<Vector2> line)
    {
        float length = 0f;
        for (int i = 0; i + 1 < line.Count; i++) length += Vector2.Distance(line[i], line[i + 1]);
        return length;
    }

    static bool IsClosed(List<Vector2> line) =>
        line.Count > 2 && (line[0] - line[line.Count - 1]).sqrMagnitude < 1e-10f;

    // Mesh
    /// A flat quad per line segment (y = 0, facing up), lineWidth wide, plus a small square at each joint.
    static Mesh BuildStripMesh(List<List<Vector2>> lines, float lineWidth)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        float h = lineWidth * 0.5f;

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
        }

        foreach (var line in lines)
        {
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                Vector2 dir = (b - a).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x) * h;
                AddQuad(new Vector3(a.x - side.x, 0f, a.y - side.y), new Vector3(a.x + side.x, 0f, a.y + side.y),
                        new Vector3(b.x + side.x, 0f, b.y + side.y), new Vector3(b.x - side.x, 0f, b.y - side.y));
            }
            foreach (var p in line) // fill the gaps at the corners
                AddQuad(new Vector3(p.x - h, 0f, p.y - h), new Vector3(p.x - h, 0f, p.y + h),
                        new Vector3(p.x + h, 0f, p.y + h), new Vector3(p.x + h, 0f, p.y - h));
        }

        // Same as the asset's file name, or Unity warns "Main Object Name ... does not match filename".
        var mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(OutputPath) };
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
