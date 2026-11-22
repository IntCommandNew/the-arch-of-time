using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The dial of the in-city guide screen, drawn with Painter2D behind its child labels, like the compass ring
/// of an engraved city plan (docs/Design.md): a double ink ring with 32 ticks.
///   - Far: the ring alone (the distance and direction are written inside it).
///   - Near: a needle on the ring points where the target is, from where the phone points; it takes the colour
///     of warmer (wax) or colder (Prussian blue), ink when neither.
///   - Arrived: the ring is printed solid in Prussian blue.
/// </summary>
[UxmlElement]
public partial class GuideDial : VisualElement
{
    public enum Look { Far, Near, Arrived }

    const int Ticks = 32;

    Look look = Look.Far;
    bool showPointer;
    float angle;          // where the target is: 0 = straight ahead (top), positive = to the right
    Color tint = Palette.Ink;

    public GuideDial()
    {
        generateVisualContent += Draw;
    }

    /// <summary>Sets what the dial shows. angle: degrees from straight ahead, positive = to the right.</summary>
    public void Set(Look newLook, bool pointer, float newAngle, Color newTint)
    {
        if (newLook == look && pointer == showPointer && Mathf.Abs(newAngle - angle) < 0.5f && newTint == tint)
            return;
        look = newLook;
        showPointer = pointer;
        angle = newAngle;
        tint = newTint;
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        // The whole element (the padding only keeps the labels inside the ring).
        var rect = new Rect(0f, 0f, layout.width, layout.height);
        float size = Mathf.Min(rect.width, rect.height);
        if (size <= 0f) return;
        var center = rect.center;
        float outer = size * 0.42f;
        float inner = outer - size * 0.035f;
        var p = context.painter2D;
        p.lineCap = LineCap.Butt;

        if (look == Look.Arrived)
        {
            // The band between the two rings printed solid.
            p.strokeColor = Palette.Prussian;
            p.lineWidth = outer - inner;
            p.BeginPath();
            p.Arc(center, (outer + inner) * 0.5f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }

        // The double ring: a heavy outer line and a fine inner one.
        p.strokeColor = Palette.Ink;
        p.lineWidth = size * 0.008f;
        p.BeginPath();
        p.Arc(center, outer, Angle.Degrees(0f), Angle.Degrees(360f));
        p.Stroke();
        p.lineWidth = size * 0.003f;
        p.BeginPath();
        p.Arc(center, inner, Angle.Degrees(0f), Angle.Degrees(360f));
        p.Stroke();

        // 32 ticks inside the inner ring, longer every eighth (ahead, right, behind, left).
        p.strokeColor = Palette.WithAlpha(Palette.Ink, 0.7f);
        p.BeginPath();
        for (int i = 0; i < Ticks; i++)
        {
            float a = i * 360f / Ticks * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            float length = i % 8 == 0 ? size * 0.05f : i % 2 == 0 ? size * 0.028f : size * 0.016f;
            p.MoveTo(center + dir * inner);
            p.LineTo(center + dir * (inner - length));
        }
        p.lineWidth = size * 0.004f;
        p.Stroke();

        if (look != Look.Near || !showPointer) return;

        // The needle: a narrow lozenge across the ring (clear of the text inside), its point towards the target.
        float rad = angle * Mathf.Deg2Rad;
        var ahead = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
        var side = new Vector2(-ahead.y, ahead.x);
        var tip = center + ahead * (outer + size * 0.07f);
        var waist = center + ahead * (outer - size * 0.01f);
        var tail = center + ahead * (inner - size * 0.07f);
        float half = size * 0.03f;
        p.fillColor = tint;
        p.BeginPath();
        p.MoveTo(tip);
        p.LineTo(waist + side * half);
        p.LineTo(tail);
        p.LineTo(waist - side * half);
        p.ClosePath();
        p.Fill();
    }
}
