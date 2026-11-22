using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Editor helper for the weekly report's pictures (22 Nov 2026): renders the app's screen at the phone's size in
/// Play mode, the UI Toolkit interface over the camera's view (the camera view has no camera image in the Editor:
/// a plain background behind the models). Used through the Unity CLI, in two steps, because the UI draws into
/// the target on the next frame: Begin(w, h), wait a moment, then Save(png, withCamera), which also ends it.
/// </summary>
public static class ReportShots
{
    static RenderTexture uiTarget;
    static UIDocument document;
    static bool clearColour;
    static Color clearValue;

    public static string Begin(int width = 1080, int height = 2340)
    {
        document = Object.FindFirstObjectByType<UIDocument>();
        if (document == null) return "no UIDocument";
        uiTarget = new RenderTexture(width, height, 24);
        // The panel doesn't clear its target (fine on screen): into a texture, a see-through layer would build up
        // frame after frame. Cleared to transparent while capturing; restored in Save.
        var settings = document.panelSettings;
        clearColour = settings.clearColor;
        clearValue = settings.colorClearValue;
        settings.clearColor = true;
        settings.colorClearValue = Color.clear;
        settings.targetTexture = uiTarget;
        return "ok";
    }

    /// <param name="background">The camera's background colour behind the models (no camera image here).</param>
    public static string Save(string png, bool withCamera, Color background)
    {
        if (document == null || uiTarget == null) return "call Begin first";
        int w = uiTarget.width, h = uiTarget.height;
        var ui = Read(uiTarget);
        var panel = document.panelSettings;
        panel.targetTexture = null;
        panel.clearColor = clearColour;
        panel.colorClearValue = clearValue;

        Color[] under = null;
        if (withCamera && Camera.main != null)
        {
            var cam = Camera.main;
            var flags = cam.clearFlags;
            var colour = cam.backgroundColor;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            var rt = new RenderTexture(w, h, 24);
            var previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previousTarget;
            cam.clearFlags = flags;
            cam.backgroundColor = colour;
            under = Read(rt).GetPixels();
            rt.Release();
        }

        var pixels = ui.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            var b = under != null ? under[i] : background;
            pixels[i] = new Color(c.r * c.a + b.r * (1f - c.a), c.g * c.a + b.g * (1f - c.a),
                                  c.b * c.a + b.b * (1f - c.a), 1f);
        }
        var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.SetPixels(pixels);
        result.Apply();
        System.IO.File.WriteAllBytes(png, result.EncodeToPNG());
        uiTarget.Release();
        uiTarget = null;
        return png;
    }

    static Texture2D Read(RenderTexture rt)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        return tex;
    }
}
