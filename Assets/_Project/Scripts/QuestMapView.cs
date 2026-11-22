using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The quest map of central Iasi (Anywhere mode), drawn with Painter2D from OpenStreetMap data
/// (Content/IasiMap.json: streets, parks, water, and the names of the main streets and landmarks), in the
/// manner of an engraved city plan (docs/Design.md): streets cut in ink on paper (the main ones as double
/// lines), parks in verdigris, water in Prussian blue, a north arrow and a scale bar. Landmarks are small ink
/// rings with their names; the quest's places are among them, looking exactly like the others (user, 21 Nov
/// 2026: marking only the quest's places gave them away), so the map helps to find a place without saying which
/// places are the answers. One finger drags, two fingers pinch to zoom, a tap raises Tapped with the place in
/// metres (x east, y north from the map's south-west corner).
/// The map is drawn once on a "plate" that is moved and scaled; the names and the marks (the rings, the guess,
/// the hint circle) are on layers above it, at a constant size on screen. Names are placed most important first
/// (landmarks by fame, then the best-known streets) and left out where they would overlap (a landmark's ring
/// only shows with its name); a street's name shows only once its street is long enough on screen to carry it
/// (more names appear as you zoom in).
/// </summary>
[UxmlElement]
public partial class QuestMapView : VisualElement
{
    /// <summary>The map was tapped (not dragged): the spot in metres.</summary>
    public event Action<Vector2> Tapped;

    const float MaxZoom = 8f;
    const float TapSlop = 14f;          // panel pixels a finger may move and still tap

    static readonly float[] ScaleSteps = { 50f, 100f, 200f, 250f, 500f, 1000f };
    const float ScaleBarMaxPixels = 260f;

    readonly VisualElement plate;
    readonly VisualElement names;       // the labels (streets, landmarks, the quest's places)
    readonly VisualElement marks;
    readonly Label scaleLabel;
    float scaleMetres;                  // the scale bar's length in metres and on screen
    float scalePixels;
    MapData data;
    float metresToPixels = 1f;          // at zoom 1, the map's width fills the view
    float zoom = 1f;
    Vector2 offset;                     // the plate's top-left in the view (panel pixels)

    // Marks (metres); NaN = none
    Vector2 guess = new Vector2(float.NaN, float.NaN);
    readonly List<Vector2> misses = new List<Vector2>();
    Vector2 circleCentre = new Vector2(float.NaN, float.NaN);
    float circleRadius;
    Vector2 place = new Vector2(float.NaN, float.NaN);

    // Names, in the order they are placed (most important first)
    enum NameKind { Landmark, Street }

    class MapName
    {
        public Label label;
        public NameKind kind;
        public Vector2 metres;
        public float angle;         // degrees on screen, clockwise
        public float runMetres;     // a street: the straight stretch the name sits on
        public int rank;
        public Vector2 size;        // measured once the font is known
        public bool shown;          // placed in the view now (a landmark's ring is drawn only then)
    }

    readonly List<MapName> mapNames = new List<MapName>();
    readonly List<Rect> taken = new List<Rect>();
    bool namesPending;
    const float RingRadius = 12f;      // a landmark's ring
    const float NameGap = 6f;           // free space kept around each name
    static readonly Vector2 NamePadding = new Vector2(12f, 2f); // .map__name padding, both sides (USS)

    // Touches (pointer id -> position in the view)
    readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
    Vector2 pressStart;
    bool moved;
    float pinchDistance;

    public QuestMapView()
    {
        style.overflow = Overflow.Hidden;
        plate = new VisualElement { name = "map-plate", pickingMode = PickingMode.Ignore };
        plate.style.position = Position.Absolute;
        plate.style.transformOrigin = new TransformOrigin(0, 0);
        plate.generateVisualContent += DrawMap;
        Add(plate);
        names = new VisualElement { name = "map-names", pickingMode = PickingMode.Ignore };
        names.style.position = Position.Absolute;
        names.style.left = 0; names.style.top = 0; names.style.right = 0; names.style.bottom = 0;
        Add(names);
        marks = new VisualElement { name = "map-marks", pickingMode = PickingMode.Ignore };
        marks.style.position = Position.Absolute;
        marks.style.left = 0; marks.style.top = 0; marks.style.right = 0; marks.style.bottom = 0;
        marks.generateVisualContent += DrawMarks;
        Add(marks);
        scaleLabel = new Label { name = "map-scale", pickingMode = PickingMode.Ignore };
        scaleLabel.AddToClassList("map__scale");
        Add(scaleLabel);

        RegisterCallback<GeometryChangedEvent>(_ => Layout(keepCentre: true));
        RegisterCallback<PointerDownEvent>(OnPointerDown);
        RegisterCallback<PointerMoveEvent>(OnPointerMove);
        RegisterCallback<PointerUpEvent>(OnPointerUp);
        RegisterCallback<PointerCancelEvent>(e => EndPointer(e.pointerId));
    }

    /// <summary>Width and height of the map in metres (0 until loaded).</summary>
    public Vector2 SizeMetres => data != null ? new Vector2(data.width, data.height) : Vector2.zero;

    /// <summary>Loads the map data (IasiMap.json).</summary>
    public void Load(TextAsset json)
    {
        data = json != null ? JsonUtility.FromJson<MapData>(json.text) : null;
        BuildNames();
        zoom = 1f;
        Layout(keepCentre: false);
        plate.MarkDirtyRepaint();
    }

    /// <summary>A latitude / longitude as metres on this map.</summary>
    public Vector2 GeoToMetres(double latitude, double longitude)
    {
        if (data == null) return Vector2.zero;
        double mx = 111320.0 * Math.Cos((data.south + data.north) * 0.5 * Math.PI / 180.0);
        return new Vector2((float)((longitude - data.west) * mx), (float)((latitude - data.south) * 110540.0));
    }

    /// <summary>The marks: the guess, earlier misses, the hint circle and the revealed place (NaN = none).</summary>
    public void SetMarks(Vector2 currentGuess, IEnumerable<Vector2> missed, Vector2 circle, float radius,
                         Vector2 revealed)
    {
        guess = currentGuess;
        misses.Clear();
        if (missed != null) misses.AddRange(missed);
        circleCentre = circle;
        circleRadius = radius;
        place = revealed;
        marks.MarkDirtyRepaint();
    }

    /// <summary>Back to the whole map, or centred on a spot (metres) at a zoom.</summary>
    public void ShowAll()
    {
        zoom = 1f;
        Layout(keepCentre: false);
    }

    public void FocusOn(Vector2 metres, float newZoom)
    {
        zoom = Mathf.Clamp(newZoom, 1f, MaxZoom);
        var size = layout.size;
        offset = size * 0.5f - MetresToPlate(metres) * zoom;
        ApplyTransform();
    }

    // Layout and transform
    void Layout(bool keepCentre)
    {
        var size = layout.size;
        if (data == null || size.x <= 0f || size.y <= 0f) return;
        Vector2 centre = keepCentre ? ViewToMetres(size * 0.5f) : new Vector2(data.width, data.height) * 0.5f;
        // Zoom 1 shows the whole map (fit inside the view).
        metresToPixels = Mathf.Min(size.x / data.width, size.y / data.height);
        plate.style.width = data.width * metresToPixels;
        plate.style.height = data.height * metresToPixels;
        offset = size * 0.5f - MetresToPlate(centre) * zoom;
        ApplyTransform();
        plate.MarkDirtyRepaint();
    }

    void ApplyTransform()
    {
        var size = layout.size;
        if (data != null)
        {
            // Keep the map in view: no empty band wider than the map's own margin.
            Vector2 mapSize = new Vector2(data.width, data.height) * metresToPixels * zoom;
            offset.x = mapSize.x <= size.x
                ? (size.x - mapSize.x) * 0.5f
                : Mathf.Clamp(offset.x, size.x - mapSize.x, 0f);
            offset.y = mapSize.y <= size.y
                ? (size.y - mapSize.y) * 0.5f
                : Mathf.Clamp(offset.y, size.y - mapSize.y, 0f);
        }
        plate.style.translate = new Translate(offset.x, offset.y);
        plate.style.scale = new Scale(new Vector2(zoom, zoom));
        UpdateScaleBar();
        PlaceNames();
        marks.MarkDirtyRepaint();
    }

    // Names
    void BuildNames()
    {
        foreach (var old in mapNames) old.label.RemoveFromHierarchy();
        mapNames.Clear();
        if (data?.labels != null)
        {
            foreach (var name in data.labels)
            {
                if (string.IsNullOrEmpty(name.t)) continue;
                AddName(name.t, name.k == 1 ? NameKind.Landmark : NameKind.Street, new Vector2(name.x, name.y),
                        name.a, name.l, name.r);
            }
        }
        SortNames();
    }

    void AddName(string text, NameKind kind, Vector2 metres, float angle, float runMetres, int rank)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("map__name");
        label.AddToClassList(kind == NameKind.Landmark ? "map__name--landmark" : "map__name--street");
        label.style.position = Position.Absolute;
        label.style.visibility = Visibility.Hidden;
        label.style.rotate = new Rotate(angle);
        names.Add(label);
        mapNames.Add(new MapName
        {
            label = label,
            kind = kind,
            metres = metres,
            angle = angle,
            runMetres = runMetres,
            rank = rank,
            size = new Vector2(float.NaN, float.NaN),
        });
    }

    void SortNames() =>
        mapNames.Sort((a, b) => a.kind != b.kind ? a.kind.CompareTo(b.kind) : a.rank.CompareTo(b.rank));

    /// Shows the names that fit, most important first: inside the view, not over another name or the north
    /// arrow and scale bar, and (streets) not longer than their street on screen. A landmark's name goes beside
    /// its ring, on the first free side.
    void PlaceNames()
    {
        var size = layout.size;
        if (data == null || float.IsNaN(size.x) || size.x <= 0f) return;
        taken.Clear();
        taken.Add(new Rect(size.x - 112f, 0f, 112f, 140f));     // the north arrow
        taken.Add(new Rect(0f, size.y - 112f, 320f, 112f));     // the scale bar
        bool unmeasured = false;
        float pixelsPerMetre = metresToPixels * zoom;
        foreach (var name in mapNames)
        {
            name.shown = false;
            if (float.IsNaN(name.size.x) || name.size.x <= 0f)
            {
                name.size = name.label.MeasureTextSize(name.label.text, 0f, MeasureMode.Undefined, 0f,
                                                       MeasureMode.Undefined);
                if (!float.IsNaN(name.size.x) && name.size.x > 0f) name.size += NamePadding;
                if (float.IsNaN(name.size.x) || name.size.x <= 0f)
                {
                    unmeasured = true;
                    name.label.style.visibility = Visibility.Hidden;
                    continue;
                }
            }
            Vector2 at = MetresToView(name.metres);
            Vector2 s = name.size;
            Rect box;
            Vector2 topLeft;
            if (name.kind == NameKind.Landmark)
            {
                // Beside its ring, on the first free side (right, left, above, below); else left out.
                var ring = new Rect(at.x - RingRadius, at.y - RingRadius, RingRadius * 2f, RingRadius * 2f);
                if (!Fits(ring, size) || NameOverlaps(ring))
                {
                    name.label.style.visibility = Visibility.Hidden;
                    continue;
                }
                float gap = RingRadius + NameGap;
                Span<Vector2> sides = stackalloc Vector2[4];
                sides[0] = new Vector2(at.x + gap, at.y - s.y * 0.5f);
                sides[1] = new Vector2(at.x - gap - s.x, at.y - s.y * 0.5f);
                sides[2] = new Vector2(at.x - s.x * 0.5f, at.y - gap - s.y);
                sides[3] = new Vector2(at.x - s.x * 0.5f, at.y + gap);
                int chosen = -1;
                for (int i = 0; i < sides.Length && chosen < 0; i++)
                {
                    var candidate = new Rect(sides[i], s);
                    if (Fits(candidate, size) && !NameOverlaps(candidate)) chosen = i;
                }
                if (chosen < 0)
                {
                    name.label.style.visibility = Visibility.Hidden;
                    continue;
                }
                topLeft = sides[chosen];
                box = new Rect(topLeft, s);
                taken.Add(new Rect(ring.x - NameGap, ring.y - NameGap, ring.width + NameGap * 2f,
                                   ring.height + NameGap * 2f));
            }
            else
            {
                // Centred on its spot, turned along its street; the box is the turned name's bounds.
                topLeft = at - s * 0.5f;
                float radians = name.angle * Mathf.Deg2Rad;
                float c = Mathf.Abs(Mathf.Cos(radians)), sn = Mathf.Abs(Mathf.Sin(radians));
                var bounds = new Vector2(s.x * c + s.y * sn, s.x * sn + s.y * c);
                box = new Rect(at - bounds * 0.5f, bounds);
            }
            bool show = name.kind == NameKind.Landmark || Fits(box, size);
            // A street's name may run a little past its straight stretch, not much further.
            if (show && name.kind == NameKind.Street) show = name.runMetres * pixelsPerMetre >= s.x * 0.8f;
            if (show) show = !NameOverlaps(box);
            if (show)
            {
                taken.Add(new Rect(box.x - NameGap, box.y - NameGap, box.width + NameGap * 2f,
                                   box.height + NameGap * 2f));
                name.label.style.left = topLeft.x;
                name.label.style.top = topLeft.y;
            }
            name.shown = show;
            name.label.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
        }
        // Before the fonts are ready nothing can be measured: try again a moment later.
        if (unmeasured && !namesPending)
        {
            namesPending = true;
            schedule.Execute(() =>
            {
                namesPending = false;
                PlaceNames();
            }).StartingIn(100);
        }
    }

    static bool Fits(Rect box, Vector2 size) =>
        box.xMin >= 0f && box.yMin >= 0f && box.xMax <= size.x && box.yMax <= size.y;

    static bool IsInView(Vector2 point, Vector2 size) =>
        point.x >= 0f && point.y >= 0f && point.x <= size.x && point.y <= size.y;

    bool NameOverlaps(Rect box)
    {
        foreach (var other in taken)
            if (other.Overlaps(box)) return true;
        return false;
    }

    /// The longest round length that fits the scale bar at this zoom.
    void UpdateScaleBar()
    {
        float pixelsPerMetre = metresToPixels * zoom;
        scaleMetres = 0f;
        foreach (float step in ScaleSteps)
            if (step * pixelsPerMetre <= ScaleBarMaxPixels) scaleMetres = step;
        if (scaleMetres <= 0f) scaleMetres = ScaleSteps[0];
        scalePixels = scaleMetres * pixelsPerMetre;
        scaleLabel.text = scaleMetres >= 1000f
            ? (scaleMetres / 1000f).ToString("0.#") + " km"
            : scaleMetres.ToString("0") + " m";
    }

    Vector2 MetresToPlate(Vector2 m) =>
        data == null ? Vector2.zero : new Vector2(m.x, data.height - m.y) * metresToPixels;

    Vector2 MetresToView(Vector2 m) => offset + MetresToPlate(m) * zoom;

    Vector2 ViewToMetres(Vector2 v)
    {
        if (data == null || metresToPixels <= 0f) return Vector2.zero;
        Vector2 p = (v - offset) / (zoom * metresToPixels);
        return new Vector2(p.x, data.height - p.y);
    }

    // Input
    void OnPointerDown(PointerDownEvent e)
    {
        pointers[e.pointerId] = (Vector2)e.localPosition;
        this.CapturePointer(e.pointerId);
        if (pointers.Count == 1)
        {
            pressStart = e.localPosition;
            moved = false;
        }
        else if (pointers.Count == 2)
        {
            moved = true; // a pinch is never a tap
            pinchDistance = PinchDistance();
        }
        e.StopPropagation();
    }

    void OnPointerMove(PointerMoveEvent e)
    {
        if (!pointers.TryGetValue(e.pointerId, out var last)) return;
        Vector2 now = e.localPosition;
        if (pointers.Count == 1)
        {
            if (!moved && Vector2.Distance(now, pressStart) > TapSlop) moved = true;
            if (moved)
            {
                offset += now - last;
                ApplyTransform();
            }
            pointers[e.pointerId] = now;
        }
        else if (pointers.Count >= 2)
        {
            Vector2 before = Midpoint();
            pointers[e.pointerId] = now;
            float distance = PinchDistance();
            if (pinchDistance > 1f && distance > 1f)
            {
                Vector2 anchor = ViewToMetres(before);
                zoom = Mathf.Clamp(zoom * distance / pinchDistance, 1f, MaxZoom);
                offset = Midpoint() - MetresToPlate(anchor) * zoom;
                ApplyTransform();
            }
            pinchDistance = distance;
        }
        e.StopPropagation();
    }

    void OnPointerUp(PointerUpEvent e)
    {
        bool tap = pointers.Count == 1 && pointers.ContainsKey(e.pointerId) && !moved;
        Vector2 at = e.localPosition;
        EndPointer(e.pointerId);
        if (tap && data != null) Tapped?.Invoke(ViewToMetres(at));
        e.StopPropagation();
    }

    void EndPointer(int id)
    {
        pointers.Remove(id);
        if (this.HasPointerCapture(id)) this.ReleasePointer(id);
        if (pointers.Count == 1)
        {
            foreach (var p in pointers.Values) pressStart = p;
        }
    }

    float PinchDistance()
    {
        Vector2 a = default, b = default;
        int i = 0;
        foreach (var p in pointers.Values)
        {
            if (i == 0) a = p; else if (i == 1) b = p;
            i++;
        }
        return Vector2.Distance(a, b);
    }

    Vector2 Midpoint()
    {
        Vector2 sum = Vector2.zero;
        int n = 0;
        foreach (var p in pointers.Values)
        {
            sum += p;
            if (++n == 2) break;
        }
        return n > 0 ? sum / n : Vector2.zero;
    }

    // Drawing
    void DrawMap(MeshGenerationContext context)
    {
        if (data == null) return;
        var painter = context.painter2D;
        painter.lineJoin = LineJoin.Round;
        painter.lineCap = LineCap.Round;

        // No plate fill: the view's paper shows through, and the streets run past the data's edge.
        FillAreas(painter, data.parks, Palette.WithAlpha(Palette.Verdigris, 0.85f));
        FillAreas(painter, data.water, Palette.WithAlpha(Palette.Prussian, 0.35f));
        StrokeLines(painter, data.rivers, Palette.WithAlpha(Palette.Prussian, 0.8f), 9f);
        if (data.roads == null) return;
        // Engraved streets: main and secondary ones as two ink lines (an ink stroke with paper inside it, all
        // inked first so crossings join), minor ones as a single fine line.
        foreach (var road in data.roads)
        {
            if (road.c == 2) continue;
            painter.strokeColor = Palette.Ink;
            painter.lineWidth = Mathf.Max(1.5f, (road.c == 0 ? 18f : 12f) * metresToPixels);
            Polyline(painter, road.p, close: false);
            painter.Stroke();
        }
        foreach (var road in data.roads)
        {
            if (road.c == 2) continue;
            painter.strokeColor = Palette.Paper;
            painter.lineWidth = Mathf.Max(0.6f, (road.c == 0 ? 11f : 6.5f) * metresToPixels);
            Polyline(painter, road.p, close: false);
            painter.Stroke();
        }
        foreach (var road in data.roads)
        {
            if (road.c != 2) continue;
            painter.strokeColor = Palette.WithAlpha(Palette.Ink, 0.55f);
            painter.lineWidth = Mathf.Max(0.8f, 4f * metresToPixels);
            Polyline(painter, road.p, close: false);
            painter.Stroke();
        }
    }

    void FillAreas(Painter2D painter, Poly[] polys, Color color)
    {
        if (polys == null) return;
        painter.fillColor = color;
        foreach (var poly in polys)
        {
            if (poly.p == null || poly.p.Length < 6) continue;
            Polyline(painter, poly.p, close: true);
            painter.Fill();
        }
    }

    void StrokeLines(Painter2D painter, Poly[] polys, Color color, float widthMetres)
    {
        if (polys == null) return;
        painter.strokeColor = color;
        painter.lineWidth = Mathf.Max(1f, widthMetres * metresToPixels);
        foreach (var poly in polys)
        {
            if (poly.p == null || poly.p.Length < 4) continue;
            Polyline(painter, poly.p, close: false);
            painter.Stroke();
        }
    }

    void Polyline(Painter2D painter, int[] p, bool close)
    {
        painter.BeginPath();
        painter.MoveTo(MetresToPlate(new Vector2(p[0], p[1])));
        for (int i = 2; i + 1 < p.Length; i += 2)
            painter.LineTo(MetresToPlate(new Vector2(p[i], p[i + 1])));
        if (close) painter.ClosePath();
    }

    void DrawMarks(MeshGenerationContext context)
    {
        if (data == null) return;
        var painter = context.painter2D;

        if (!float.IsNaN(circleCentre.x) && circleRadius > 0f)
        {
            Vector2 c = MetresToView(circleCentre);
            float r = circleRadius * metresToPixels * zoom;
            painter.fillColor = Palette.WithAlpha(Palette.Rose, 0.35f);
            painter.strokeColor = Palette.Prussian;
            painter.lineWidth = 4f;
            Circle(painter, c, r);
            painter.Fill();
            painter.Stroke();
        }

        // Earlier misses: small wax crosses, so the same spot is not tried twice.
        painter.strokeColor = Palette.Wax;
        painter.lineWidth = 4f;
        foreach (var miss in misses)
        {
            Vector2 c = MetresToView(miss);
            painter.BeginPath();
            painter.MoveTo(c + new Vector2(-10f, -10f));
            painter.LineTo(c + new Vector2(10f, 10f));
            painter.MoveTo(c + new Vector2(10f, -10f));
            painter.LineTo(c + new Vector2(-10f, 10f));
            painter.Stroke();
        }

        // Landmarks: small ink rings, each named beside it (PlaceNames); all alike.
        foreach (var name in mapNames)
        {
            if (name.kind != NameKind.Landmark || !name.shown) continue;
            Vector2 c = MetresToView(name.metres);
            painter.fillColor = Palette.Paper;
            Circle(painter, c, RingRadius);
            painter.Fill();
            painter.strokeColor = Palette.Ink;
            painter.lineWidth = 3f;
            Circle(painter, c, RingRadius);
            painter.Stroke();
            painter.fillColor = Palette.Ink;
            Circle(painter, c, 4f);
            painter.Fill();
        }

        if (!float.IsNaN(place.x))
        {
            // The place: a Prussian blue disc in a paper ring with a fine ink edge, as a plan marks a monument.
            Vector2 c = MetresToView(place);
            painter.fillColor = Palette.Ink;
            Circle(painter, c, 32f);
            painter.Fill();
            painter.fillColor = Palette.Paper;
            Circle(painter, c, 29f);
            painter.Fill();
            painter.fillColor = Palette.Prussian;
            Circle(painter, c, 22f);
            painter.Fill();
        }

        if (!float.IsNaN(guess.x))
        {
            // The pin: an ink ring with a wax centre, on the tapped spot.
            Vector2 c = MetresToView(guess);
            painter.strokeColor = Palette.Ink;
            painter.lineWidth = 6f;
            Circle(painter, c, 22f);
            painter.Stroke();
            painter.fillColor = Palette.Wax;
            Circle(painter, c, 10f);
            painter.Fill();
        }

        DrawNorthArrow(painter);
        DrawScaleBar(painter);
    }

    /// A plan's north arrow in the top-right corner: a lozenge, its north half solid ink.
    void DrawNorthArrow(Painter2D painter)
    {
        var c = new Vector2(layout.width - 56f, 72f);
        var top = c + new Vector2(0f, -44f);
        var bottom = c + new Vector2(0f, 44f);
        var left = c + new Vector2(-14f, 0f);
        var right = c + new Vector2(14f, 0f);
        painter.fillColor = Palette.Paper;
        Lozenge(painter, top, right, bottom, left);
        painter.Fill();
        painter.fillColor = Palette.Ink;
        Lozenge(painter, top, right, c, left);
        painter.Fill();
        painter.strokeColor = Palette.Ink;
        painter.lineWidth = 2.5f;
        Lozenge(painter, top, right, bottom, left);
        painter.Stroke();
    }

    static void Lozenge(Painter2D painter, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        painter.BeginPath();
        painter.MoveTo(a);
        painter.LineTo(b);
        painter.LineTo(c);
        painter.LineTo(d);
        painter.ClosePath();
    }

    /// A plan's scale bar in the bottom-left corner: four segments, alternately ink and paper; its length is
    /// written above it (map__scale).
    void DrawScaleBar(Painter2D painter)
    {
        if (scalePixels <= 0f) return;
        var origin = new Vector2(32f, layout.height - 40f);
        const float height = 12f;
        float segment = scalePixels / 4f;
        for (int i = 0; i < 4; i++)
        {
            painter.fillColor = i % 2 == 0 ? Palette.Ink : Palette.Paper;
            Lozenge(painter, origin + new Vector2(segment * i, 0f), origin + new Vector2(segment * (i + 1), 0f),
                    origin + new Vector2(segment * (i + 1), height), origin + new Vector2(segment * i, height));
            painter.Fill();
        }
        painter.strokeColor = Palette.Ink;
        painter.lineWidth = 2.5f;
        Lozenge(painter, origin, origin + new Vector2(scalePixels, 0f), origin + new Vector2(scalePixels, height),
                origin + new Vector2(0f, height));
        painter.Stroke();
    }

    static void Circle(Painter2D painter, Vector2 centre, float radius)
    {
        painter.BeginPath();
        painter.Arc(centre, radius, 0f, 360f);
        painter.ClosePath();
    }

    // The JSON (see the script that made it: streets in classes 0 major, 1 secondary, 2 minor)
    [Serializable]
    class MapData
    {
        public double south, west, north, east;
        public float width, height;
        public Road[] roads;
        public Poly[] parks;
        public Poly[] water;
        public Poly[] rivers;
        public NameData[] labels;
    }

    /// A name (the make_labels script): text, spot in metres, angle on screen, the straight stretch it sits on,
    /// kind (0 street, 1 landmark) and rank (0 = most important).
    [Serializable]
    class NameData
    {
        public string t;
        public float x, y, a, l;
        public int k, r;
    }

    [Serializable]
    class Road
    {
        public int c;
        public int[] p;
    }

    [Serializable]
    class Poly
    {
        public int[] p;
    }
}
