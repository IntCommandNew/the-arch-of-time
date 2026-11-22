using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>One slider of the developer tuning panel.</summary>
public readonly struct TuningRow
{
    public readonly string Label;
    public readonly float Min, Max, Value;
    public readonly string Format; // "0.00", or "0%" for a share shown as a percentage

    public TuningRow(string label, float min, float max, float value, string format)
    {
        Label = label;
        Min = min;
        Max = max;
        Value = value;
        Format = format;
    }
}

/// <summary>
/// The developer tuning panel (LightingTuner): a "Light" button in the top corner of the camera screens opens a
/// paper slip at the top of the screen (the model stays in view in the middle, the screen's own slip at the
/// bottom) with a status line and one ruled row per value: its name and number, then a full-width slider.
/// Built in code: it is a developer tool, not part of the app's screens (texts in English only).
/// </summary>
public partial class AppUI
{
    /// <summary>A slider moved: its row and the new value.</summary>
    public event Action<int, float> TuningChanged;

    /// <summary>"Reset" on the tuning panel.</summary>
    public event Action TuningResetClicked;

    public bool IsTuningOpen => tuningPanel != null && !tuningPanel.ClassListContains("is-hidden");

    TuningRow[] tuningRows;
    bool tuningAvailable;
    Button tuningToggle;
    VisualElement tuningPanel;
    Label tuningStatus;
    readonly List<(Slider slider, Label value, string format)> tuningSliders =
        new List<(Slider, Label, string)>();

    /// <summary>Builds the panel (rows null = none). available: the "Light" button shows on camera screens.</summary>
    public void SetUpTuning(TuningRow[] rows, bool available)
    {
        tuningRows = rows;
        tuningAvailable = available && rows != null;
        BuildTuning();
    }

    /// <summary>Puts the sliders to these values (e.g. after Reset) without raising TuningChanged.</summary>
    public void SetTuningValues(float[] values)
    {
        for (int i = 0; i < tuningSliders.Count && i < values.Length; i++)
        {
            var (slider, label, format) = tuningSliders[i];
            slider.SetValueWithoutNotify(values[i]);
            label.text = FormatTuning(values[i], format);
        }
    }

    public void SetTuningStatus(string text)
    {
        if (tuningStatus != null) tuningStatus.text = text ?? "";
    }

    void BuildTuning()
    {
        tuningToggle?.RemoveFromHierarchy();
        tuningPanel?.RemoveFromHierarchy();
        tuningToggle = null;
        tuningPanel = null;
        tuningStatus = null;
        tuningSliders.Clear();
        debugOverlay?.RemoveFromClassList("debug-overlay--beside-tuner");
        if (safeArea == null || tuningRows == null || !tuningAvailable) return;

        tuningToggle = new Button(() => SetTuningOpen(true)) { name = "btn-tuning", text = "Light" };
        tuningToggle.AddToClassList("btn");
        tuningToggle.AddToClassList("btn--quiet");
        tuningToggle.AddToClassList("tuner-toggle");
        safeArea.Add(tuningToggle);

        tuningPanel = new VisualElement { name = "tuning-panel" };
        tuningPanel.AddToClassList("slip");
        tuningPanel.AddToClassList("tuner");
        tuningStatus = new Label("ARCore: no reading yet.");
        tuningStatus.AddToClassList("tuner__status");
        tuningPanel.Add(tuningStatus);

        var list = new ScrollView(ScrollViewMode.Vertical)
        {
            verticalScrollerVisibility = ScrollerVisibility.Hidden, // dragged by touch
            horizontalScrollerVisibility = ScrollerVisibility.Hidden,
        };
        list.AddToClassList("tuner__list");
        tuningPanel.Add(list);
        for (int i = 0; i < tuningRows.Length; i++)
        {
            var spec = tuningRows[i];
            var row = new VisualElement();
            row.AddToClassList("tuner__row");
            var head = new VisualElement();
            head.AddToClassList("tuner__head");
            var name = new Label(spec.Label);
            name.AddToClassList("tuner__label");
            var value = new Label(FormatTuning(spec.Value, spec.Format));
            value.AddToClassList("tuner__value");
            head.Add(name);
            head.Add(value);
            row.Add(head);

            var slider = new Slider(spec.Min, spec.Max) { value = spec.Value };
            slider.AddToClassList("scale-slider");
            slider.AddToClassList("tuner__slider");
            int index = i;
            string format = spec.Format;
            slider.RegisterValueChangedCallback(e =>
            {
                value.text = FormatTuning(e.newValue, format);
                TuningChanged?.Invoke(index, e.newValue);
            });
            row.Add(slider);
            list.Add(row);
            tuningSliders.Add((slider, value, format));
        }

        var actions = new VisualElement();
        actions.AddToClassList("quiet-row");
        actions.AddToClassList("tuner__actions");
        var reset = new Button(() => TuningResetClicked?.Invoke()) { text = "Reset values" };
        reset.AddToClassList("btn");
        reset.AddToClassList("btn--quiet");
        var close = new Button(() => SetTuningOpen(false)) { text = "Close" };
        close.AddToClassList("btn");
        close.AddToClassList("btn--quiet");
        actions.Add(reset);
        actions.Add(close);
        tuningPanel.Add(actions);
        safeArea.Add(tuningPanel);
        // The developer overlay makes room for the "Light" button.
        debugOverlay?.AddToClassList("debug-overlay--beside-tuner");
        SetTuningOpen(false);
    }

    void SetTuningOpen(bool open)
    {
        SetVisible(tuningPanel, open);
        UpdateTuningVisibility();
        UpdateDebugOverlay();
    }

    /// The "Light" button: on camera screens, when nothing covers them and the panel is closed.
    void UpdateTuningVisibility()
    {
        bool camera = tuningAvailable && IsCameraScreen(CurrentScreen) && !BaseCovered;
        if (!camera && IsTuningOpen) SetVisible(tuningPanel, false);
        SetVisible(tuningToggle, camera && !IsTuningOpen);
    }

    static string FormatTuning(float value, string format) =>
        format == "0%" ? (value * 100f).ToString("0") + " %" : value.ToString(format);
}
