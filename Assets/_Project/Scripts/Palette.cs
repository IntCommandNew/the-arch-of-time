using UnityEngine;

/// <summary>
/// The palette of docs/Design.md for what is drawn in code (Painter2D elements); MainUI.uss has the same values.
/// Taken from Iasi's printed matter of 1835 to 1858: rag paper, iron-gall ink, the blue ink and the rose / green
/// papers of the 1858 Cap de bour stamps, and the red wax of the prince's seals.
/// </summary>
public static class Palette
{
    public static readonly Color Paper = new Color32(230, 223, 204, 255);
    public static readonly Color PaperDeep = new Color32(214, 205, 182, 255);
    public static readonly Color Ink = new Color32(34, 27, 22, 255);
    public static readonly Color Prussian = new Color32(30, 57, 102, 255);
    public static readonly Color Rose = new Color32(227, 180, 170, 255);
    public static readonly Color Verdigris = new Color32(169, 195, 160, 255);
    public static readonly Color Wax = new Color32(140, 32, 28, 255);

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
}
