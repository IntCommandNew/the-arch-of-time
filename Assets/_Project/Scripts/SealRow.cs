using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The quest's five seals in a row (the progress register, docs/Design.md): a found seal is the picture of the
/// seal model (the USS value --seal-image: UI/Images/seal.png, the seal the player collects; a drawn wax disc if
/// it is missing); a seal still to find is a broken ink circle. Seal 0 is the tutorial seal, seal 1 + i the seal
/// of stop i (GameProgress).
/// </summary>
[UxmlElement]
public partial class SealRow : VisualElement
{
    const int Lobes = 14;               // bumps in the wax's edge (the fallback disc)
    const float ImageScale = 1.32f;     // the seal fills ~76 % of its picture: the picture's half-size per radius

    static readonly CustomStyleProperty<Texture2D> SealImageProperty =
        new CustomStyleProperty<Texture2D>("--seal-image");

    readonly bool[] found = new bool[GameProgress.SealCount];
    Texture2D sealImage;

    public SealRow()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
        RegisterCallback<CustomStyleResolvedEvent>(e =>
        {
            if (e.customStyle.TryGetValue(SealImageProperty, out var image) && image != sealImage)
            {
                sealImage = image;
                MarkDirtyRepaint();
            }
        });
    }

    /// <summary>Which seals are found (by seal number).</summary>
    public void SetFound(Func<int, bool> isFound)
    {
        for (int i = 0; i < found.Length; i++) found[i] = isFound != null && isFound(i);
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        var rect = contentRect;
        int count = found.Length;
        if (rect.width <= 0f || rect.height <= 0f) return;
        float cell = rect.width / count;
        float radius = Mathf.Min(cell * 0.5f - 8f, rect.height * 0.5f - 4f);
        if (radius <= 4f) return;
        var p = context.painter2D;
        for (int i = 0; i < count; i++)
        {
            var c = new Vector2(rect.xMin + cell * (i + 0.5f), rect.center.y);
            if (found[i] && sealImage != null) DrawImage(context, sealImage, c, radius * ImageScale);
            else if (found[i]) DrawWax(p, c, radius, i);
            else DrawEmpty(p, c, radius * 0.86f);
        }
    }

    /// The seal's picture as a square centred on c (half-size h).
    static void DrawImage(MeshGenerationContext context, Texture2D image, Vector2 c, float h)
    {
        var mesh = context.Allocate(4, 6, image);
        Vertex Corner(float x, float y, float u, float v) => new Vertex
        {
            position = new Vector3(c.x + x * h, c.y + y * h, Vertex.nearZ),
            tint = Color.white,
            uv = new Vector2(u, v), // the renderer maps them into an atlas by itself
        };
        // Top-left, top-right, bottom-right, bottom-left (the panel's y points down; the picture's v up).
        mesh.SetAllVertices(new[] { Corner(-1f, -1f, 0f, 1f), Corner(1f, -1f, 1f, 1f),
                                    Corner(1f, 1f, 1f, 0f), Corner(-1f, 1f, 0f, 0f) });
        mesh.SetAllIndices(new ushort[] { 0, 1, 2, 2, 3, 0 });
    }

    static void DrawWax(Painter2D p, Vector2 c, float r, int seed)
    {
        p.fillColor = Palette.Wax;
        p.BeginPath();
        for (int k = 0; k <= Lobes * 4; k++)
        {
            float a = k * Mathf.PI * 2f / (Lobes * 4);
            // An uneven edge, different for each seal (a phase from its number).
            float wobble = 1f + 0.05f * Mathf.Sin(a * Lobes + seed * 1.7f) + 0.03f * Mathf.Sin(a * 3f + seed);
            var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * wobble;
            if (k == 0) p.MoveTo(v);
            else p.LineTo(v);
        }
        p.ClosePath();
        p.Fill();
        // The ring pressed by the seal's matrix.
        p.strokeColor = Palette.WithAlpha(Palette.Paper, 0.45f);
        p.lineWidth = Mathf.Max(1.5f, r * 0.07f);
        p.BeginPath();
        p.Arc(c, r * 0.66f, Angle.Degrees(0f), Angle.Degrees(360f));
        p.Stroke();
        p.fillColor = Palette.WithAlpha(Palette.Paper, 0.3f);
        p.BeginPath();
        p.Arc(c, r * 0.2f, Angle.Degrees(0f), Angle.Degrees(360f));
        p.Fill();
    }

    static void DrawEmpty(Painter2D p, Vector2 c, float r)
    {
        p.strokeColor = Palette.WithAlpha(Palette.Ink, 0.55f);
        p.lineWidth = Mathf.Max(1.5f, r * 0.05f);
        const int dashes = 16;
        for (int k = 0; k < dashes; k++)
        {
            float a0 = k * 360f / dashes;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(a0), Angle.Degrees(a0 + 360f / dashes * 0.55f));
            p.Stroke();
        }
    }
}
