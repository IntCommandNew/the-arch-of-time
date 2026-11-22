using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The arch drawn as its 4 pieces, after the replica of the academy's arch in Parcul Expozitiei: a wide
/// top block (the attic, with three round medallions), the arc between the piers (a semicircular
/// opening under it), and two broad piers with an impost ledge at the spring of the arch and a wider
/// plinth at the foot.
///
/// Drawn like an architect's engraving (docs/Design.md, "the one memorable thing"): a piece still missing is
/// outlined and hatched in ink, a part not yet built; a collected piece is printed solid in Prussian blue, its
/// details cut in paper colour.
///
/// Piece numbers follow GameProgress: 0 left column, 1 right column, 2 arc, 3 top beam.
/// Drawn in a 100 x 112 box scaled to the element's width; give the element that aspect ratio in USS.
/// </summary>
[UxmlElement]
public partial class ArchEmblem : VisualElement
{
    const float HatchStep = 4.2f;       // drawing units between hatch lines

    static readonly Vector2[] PierOutline =
    {
        new Vector2(2f, 35f), new Vector2(28f, 35f), new Vector2(28f, 58f), new Vector2(30f, 58f),
        new Vector2(30f, 63f), new Vector2(28f, 63f), new Vector2(28f, 99f), new Vector2(30f, 99f),
        new Vector2(30f, 112f), new Vector2(0f, 112f), new Vector2(0f, 99f), new Vector2(2f, 99f),
        new Vector2(2f, 63f), new Vector2(0f, 63f), new Vector2(0f, 58f), new Vector2(2f, 58f),
    };

    static readonly Vector2 OpeningCentre = new Vector2(50f, 62f);
    const float OpeningRadius = 19f;

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
        p.lineJoin = LineJoin.Miter;
        p.lineCap = LineCap.Butt;

        DrawPier(p, s, collected[0], mirror: false);
        DrawPier(p, s, collected[1], mirror: true);
        DrawArc(p, s, collected[2]);
        DrawAttic(p, s, collected[3]);
    }

    static void DrawPier(Painter2D p, float s, bool done, bool mirror)
    {
        void Outline()
        {
            p.BeginPath();
            for (int i = 0; i < PierOutline.Length; i++)
            {
                Vector2 v = PierOutline[i];
                if (mirror) v.x = 100f - v.x;
                if (i == 0) p.MoveTo(v * s);
                else p.LineTo(v * s);
            }
            p.ClosePath();
        }

        if (done)
        {
            p.fillColor = Palette.Prussian;
            Outline();
            p.Fill();
        }
        else
        {
            float x0 = mirror ? 72f : 2f;
            Hatch(p, s, new Rect(x0, 35f, 26f, 77f), cutOpening: false);
        }
        p.strokeColor = Palette.Ink;
        p.lineWidth = 1.3f * s;
        Outline();
        p.Stroke();
    }

    static void DrawArc(Painter2D p, float s, bool done)
    {
        void Outline()
        {
            p.BeginPath();
            p.MoveTo(P(31f, 62f, s));
            p.LineTo(P(31f, 35f, s));
            p.LineTo(P(69f, 35f, s));
            p.LineTo(P(69f, 62f, s));
            p.Arc(OpeningCentre * s, OpeningRadius * s, Angle.Degrees(0f), Angle.Degrees(180f),
                  ArcDirection.CounterClockwise);
            p.ClosePath();
        }

        if (done)
        {
            p.fillColor = Palette.Prussian;
            Outline();
            p.Fill();
        }
        else
        {
            Hatch(p, s, new Rect(31f, 35f, 38f, 27f), cutOpening: true);
        }
        p.strokeColor = Palette.Ink;
        p.lineWidth = 1.3f * s;
        Outline();
        p.Stroke();

        // The arch band around the opening, and the keystone.
        p.strokeColor = done ? Palette.Paper : Palette.Ink;
        p.lineWidth = 1.1f * s;
        p.BeginPath();
        p.Arc(OpeningCentre * s, 23.5f * s, Angle.Degrees(180f), Angle.Degrees(0f), ArcDirection.Clockwise);
        p.Stroke();
        p.BeginPath();
        p.MoveTo(P(47.5f, 38f, s));
        p.LineTo(P(52.5f, 38f, s));
        p.LineTo(P(51.5f, 43.5f, s));
        p.LineTo(P(48.5f, 43.5f, s));
        p.ClosePath();
        p.Stroke();
    }

    static void DrawAttic(Painter2D p, float s, bool done)
    {
        void Outline()
        {
            p.BeginPath();
            p.MoveTo(P(0f, 0f, s));
            p.LineTo(P(100f, 0f, s));
            p.LineTo(P(100f, 32f, s));
            p.LineTo(P(0f, 32f, s));
            p.ClosePath();
        }

        if (done)
        {
            p.fillColor = Palette.Prussian;
            Outline();
            p.Fill();
        }
        else
        {
            Hatch(p, s, new Rect(0f, 0f, 100f, 32f), cutOpening: false);
        }

        // The three round medallions (kept clear of hatching: paper inside) and the cornice line.
        foreach (float x in new[] { 17f, 50f, 83f })
        {
            p.fillColor = done ? Palette.Prussian : Palette.Paper;
            p.BeginPath();
            p.Arc(P(x, 15f, s), 6.5f * s, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
            p.strokeColor = done ? Palette.Paper : Palette.Ink;
            p.lineWidth = 1.1f * s;
            p.BeginPath();
            p.Arc(P(x, 15f, s), 5.5f * s, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }
        p.strokeColor = done ? Palette.Paper : Palette.Ink;
        p.lineWidth = 1.1f * s;
        p.BeginPath();
        p.MoveTo(P(0f, 27f, s));
        p.LineTo(P(100f, 27f, s));
        p.Stroke();

        p.strokeColor = Palette.Ink;
        p.lineWidth = 1.3f * s;
        Outline();
        p.Stroke();
    }

    /// Diagonal hatching (lines running down to the left, as an engraver shades) clipped to a rectangle in
    /// drawing units; cutOpening leaves the arch's semicircular opening clear.
    static void Hatch(Painter2D p, float s, Rect r, bool cutOpening)
    {
        p.strokeColor = Palette.WithAlpha(Palette.Ink, 0.55f);
        p.lineWidth = 0.7f * s;
        p.BeginPath();
        // Lines x = y + c.
        for (float c = r.xMin - r.yMax + HatchStep * 0.5f; c < r.xMax - r.yMin; c += HatchStep)
        {
            float yA = Mathf.Max(r.yMin, r.xMin - c);
            float yB = Mathf.Min(r.yMax, r.xMax - c);
            if (yB <= yA) continue;
            if (cutOpening && CircleSpan(c, out float yIn, out float yOut))
            {
                Segment(p, s, c, yA, Mathf.Min(yB, yIn));
                Segment(p, s, c, Mathf.Max(yA, yOut), yB);
            }
            else
            {
                Segment(p, s, c, yA, yB);
            }
        }
        p.Stroke();
    }

    /// Where the line x = y + c crosses the opening's circle (y from yIn to yOut), if it does.
    static bool CircleSpan(float c, out float yIn, out float yOut)
    {
        // (y + c - cx)^2 + (y - cy)^2 = r^2  ->  2y^2 + 2y(d - cy) + d^2 + cy^2 - r^2 = 0, d = c - cx
        float d = c - OpeningCentre.x;
        float cy = OpeningCentre.y;
        float a = 2f, b = 2f * (d - cy), k = d * d + cy * cy - OpeningRadius * OpeningRadius;
        float disc = b * b - 4f * a * k;
        yIn = yOut = 0f;
        if (disc <= 0f) return false;
        float root = Mathf.Sqrt(disc);
        yIn = (-b - root) / (2f * a);
        yOut = (-b + root) / (2f * a);
        return true;
    }

    static void Segment(Painter2D p, float s, float c, float y0, float y1)
    {
        if (y1 - y0 < 0.3f) return;
        p.MoveTo(P(y0 + c, y0, s));
        p.LineTo(P(y1 + c, y1, s));
    }

    static Vector2 P(float x, float y, float s) => new Vector2(x * s, y * s);
}
