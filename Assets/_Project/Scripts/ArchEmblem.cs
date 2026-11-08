using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The arch drawn as its 4 pieces, after the replica of the academy's arch in Parcul Expozitiei: a wide
/// top block (the attic, with three round medallions), the arc between the piers (a semicircular
/// opening under it), and two broad piers with an impost ledge at the spring of the arch and a wider
/// plinth at the foot. Collected pieces are drawn in gilt, the others faintly.
///
/// Piece numbers follow GameProgress: 0 left column, 1 right column, 2 arc, 3 top beam.
/// Drawn in a 100 x 112 box scaled to the element's width; give the element that aspect ratio in USS.
/// </summary>
[UxmlElement]
public partial class ArchEmblem : VisualElement
{
    static readonly Color Gilt = new Color32(201, 154, 62, 255);
    static readonly Color Faint = new Color32(236, 229, 211, 56);
    static readonly Color Ink = new Color32(30, 38, 51, 255);

    readonly bool[] collected = new bool[GameProgress.PieceCount];

    public ArchEmblem()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    /// <summary>Which pieces are collected (by piece number).</summary>
    public void SetCollected(Func<int, bool> isCollected)
    {
        for (int i = 0; i < collected.Length; i++) collected[i] = isCollected != null && isCollected(i);
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        float s = contentRect.width / 100f;
        if (s <= 0f) return;
        var p = context.painter2D;

        // Piers: body, impost ledge where the arch springs, wider plinth at the foot.
        DrawPier(p, s, collected[0], mirror: false);
        DrawPier(p, s, collected[1], mirror: true);

        // The arc: the block between the piers, with the semicircular opening cut out of its underside.
        p.fillColor = collected[2] ? Gilt : Faint;
        p.BeginPath();
        p.MoveTo(P(31f, 62f, s));
        p.LineTo(P(31f, 35f, s));
        p.LineTo(P(69f, 35f, s));
        p.LineTo(P(69f, 62f, s));
        p.Arc(P(50f, 62f, s), 19f * s, Angle.Degrees(0f), Angle.Degrees(180f), ArcDirection.CounterClockwise);
        p.ClosePath();
        p.Fill();
        // The arch band around the opening, and the keystone.
        p.strokeColor = Ink;
        p.lineWidth = 1.6f * s;
        p.BeginPath();
        p.Arc(P(50f, 62f, s), 23.5f * s, Angle.Degrees(180f), Angle.Degrees(0f), ArcDirection.Clockwise);
        p.Stroke();
        p.BeginPath();
        p.MoveTo(P(47.5f, 38f, s));
        p.LineTo(P(52.5f, 38f, s));
        p.LineTo(P(51.5f, 43.5f, s));
        p.LineTo(P(48.5f, 43.5f, s));
        p.ClosePath();
        p.Stroke();

        // The top beam (attic) with its three round medallions.
        p.fillColor = collected[3] ? Gilt : Faint;
        p.BeginPath();
        p.MoveTo(P(0f, 0f, s));
        p.LineTo(P(100f, 0f, s));
        p.LineTo(P(100f, 32f, s));
        p.LineTo(P(0f, 32f, s));
        p.ClosePath();
        p.Fill();
        p.strokeColor = Ink;
        p.lineWidth = 1.6f * s;
        foreach (float x in new[] { 17f, 50f, 83f })
        {
            p.BeginPath();
            p.Arc(P(x, 15f, s), 5.5f * s, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }
        p.BeginPath(); // the cornice line under the medallions
        p.MoveTo(P(0f, 27f, s));
        p.LineTo(P(100f, 27f, s));
        p.Stroke();
    }

    static void DrawPier(Painter2D p, float s, bool isCollected, bool mirror)
    {
        Vector2[] outline =
        {
            new Vector2(2f, 35f), new Vector2(28f, 35f), new Vector2(28f, 58f), new Vector2(30f, 58f),
            new Vector2(30f, 63f), new Vector2(28f, 63f), new Vector2(28f, 99f), new Vector2(30f, 99f),
            new Vector2(30f, 112f), new Vector2(0f, 112f), new Vector2(0f, 99f), new Vector2(2f, 99f),
            new Vector2(2f, 63f), new Vector2(0f, 63f), new Vector2(0f, 58f), new Vector2(2f, 58f),
        };
        p.fillColor = isCollected ? Gilt : Faint;
        p.BeginPath();
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 v = outline[i];
            if (mirror) v.x = 100f - v.x;
            if (i == 0) p.MoveTo(v * s);
            else p.LineTo(v * s);
        }
        p.ClosePath();
        p.Fill();
    }

    static Vector2 P(float x, float y, float s) => new Vector2(x * s, y * s);
}
