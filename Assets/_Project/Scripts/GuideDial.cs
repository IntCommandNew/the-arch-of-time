using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The dial of the in-city guide screen, drawn with Painter2D behind its child labels.
///   - Far: a quiet ring (the distance and direction are written inside it).
///   - Near: an arc and a pointer on the ring show where the target is, from where the phone points;
///     the ring takes the colour of warmer (gilt) or colder (patina).
///   - Arrived: the ring closes in solid gilt.
/// </summary>
[UxmlElement]
public partial class GuideDial : VisualElement
{
    public enum Look { Far, Near, Arrived }

    static readonly Color Gilt = new Color32(201, 154, 62, 255);
    static readonly Color Faint = new Color32(236, 229, 211, 46);

    Look look = Look.Far;
    bool showPointer;
    float angle;          // where the target is: 0 = straight ahead (top), positive = to the right
    Color tint = Gilt;

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
        float radius = size * 0.41f; // room for the pointer outside the ring
        var painter = context.painter2D;
        painter.lineCap = LineCap.Round;

        // The ring.
        painter.strokeColor = look == Look.Arrived ? Gilt : look == Look.Near ? WithAlpha(tint, 0.35f) : Faint;
        painter.lineWidth = look == Look.Arrived ? size * 0.03f : size * 0.008f;
        painter.BeginPath();
        painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
        painter.Stroke();

        if (look != Look.Near || !showPointer) return;

        // Painter2D angles start at +x and turn clockwise (y points down); "ahead" is the top.
        float a = angle - 90f;
        painter.strokeColor = tint;
        painter.lineWidth = size * 0.03f;
        painter.BeginPath();
        painter.Arc(center, radius, Angle.Degrees(a - 18f), Angle.Degrees(a + 18f));
        painter.Stroke();

        // A pointer just outside the ring, pointing outwards.
        float rad = a * Mathf.Deg2Rad;
        var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        var side = new Vector2(-dir.y, dir.x);
        var tip = center + dir * (radius + size * 0.085f);
        var baseCenter = center + dir * (radius + size * 0.03f);
        painter.fillColor = tint;
        painter.BeginPath();
        painter.MoveTo(tip);
        painter.LineTo(baseCenter + side * size * 0.036f);
        painter.LineTo(baseCenter - side * size * 0.036f);
        painter.ClosePath();
        painter.Fill();
    }

    static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
}
