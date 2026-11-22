using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Anywhere mode: the map of Iasi where the user points to the riddle's place. A guess within acceptedMetres of
/// the real place (QuestContent: the stop's real coordinates) is a hit. After 3 misses a big gold circle that
/// holds the place appears (at a random offset, so each play differs), after 6 a smaller one, moved again, after
/// 9 the place itself is shown. Misses are saved (GameProgress.Misses), so leaving the map doesn't reset them.
/// Raises PlaceFound(stop, misses) when the user goes on; QuestFlow turns the misses into the score.
/// </summary>
public class QuestMap : MonoBehaviour
{
    [SerializeField] AppUI ui;
    [Tooltip("Content/IasiMap.json: the map's streets, parks and water (from OpenStreetMap).")]
    [SerializeField] TextAsset mapData;
    [Tooltip("A guess this close to the real place (metres) counts. The academy's site is ~55 m from Colegiul " +
             "National, but it is never a map target; the stops are ~1 km apart.")]
    [SerializeField] float acceptedMetres = 150f;
    [SerializeField] float bigCircleMetres = 600f;
    [SerializeField] float smallCircleMetres = 250f;
    [Tooltip("How far from the circle's centre the place may be, as a share of the radius.")]
    [SerializeField] float circleOffsetShare = 0.7f;

    public const int MissesPerHint = 3;

    /// <summary>The riddle's place was found (or shown): the stop and how many guesses missed before.</summary>
    public event Action<int, int> PlaceFound;

    /// <summary>The map was closed with "Back" (the riddle is still open).</summary>
    public event Action Closed;

    public bool IsOpen { get; private set; }

    static readonly Vector2 None = new Vector2(float.NaN, float.NaN);

    int stop;
    QuestContent.Stop data;
    Vector2 target;
    Vector2 guess = None;
    readonly List<Vector2> missed = new List<Vector2>();
    Vector2 circle = None;
    float circleRadius;
    bool found;
    int misses;
    bool circlePending;         // a new hint circle: the next refresh shows it

    void OnEnable()
    {
        if (ui == null) return;
        ui.MapTapped += OnTapped;
        ui.MapCheckClicked += OnCheck;
        ui.MapBackClicked += OnBack;
        ui.LanguageChanged += Refresh;
    }

    void OnDisable()
    {
        if (ui == null) return;
        ui.MapTapped -= OnTapped;
        ui.MapCheckClicked -= OnCheck;
        ui.MapBackClicked -= OnBack;
        ui.LanguageChanged -= Refresh;
    }

    void Start() => ui?.LoadMap(mapData);

    /// <summary>Opens the map for this stop.</summary>
    public void Open(int stopIndex, QuestContent.Stop stopData)
    {
        stop = stopIndex;
        data = stopData;
        target = ui.MapGeoToMetres(data.latitude, data.longitude);
        misses = GameProgress.Misses(GameMode.Free, stop);
        missed.Clear();
        guess = None;
        found = false;
        PickCircle();
        IsOpen = true;
        ui.ShowMap(data.pieceName.Text, data.riddle.Text);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ui.HideMap();
    }

    bool Revealed => misses >= MissesPerHint * 3;

    void OnTapped(Vector2 metres)
    {
        if (!IsOpen || found || Revealed) return;
        guess = metres;
        Refresh();
    }

    void OnCheck()
    {
        if (!IsOpen) return;
        if (found || Revealed)
        {
            Close();
            PlaceFound?.Invoke(stop, misses);
            return;
        }
        if (float.IsNaN(guess.x)) return;
        float distance = Vector2.Distance(guess, target);
        Debug.Log($"[QuestMap] Stop {stop}: guess {distance:0} m from the place.");
        if (distance <= acceptedMetres)
        {
            found = true;
            guess = None;
            Refresh(focusOnPlace: true);
            return;
        }
        GameProgress.AddMiss(GameMode.Free, stop);
        misses++;
        missed.Add(guess);
        guess = None;
        if (misses % MissesPerHint == 0) PickCircle(); // a new hint at 3, 6 and 9 misses
        Refresh(focusOnPlace: Revealed);
    }

    void OnBack()
    {
        Close();
        Closed?.Invoke();
    }

    /// The hint circle for the current number of misses, at a random offset that still holds the place.
    void PickCircle()
    {
        circleRadius = misses >= MissesPerHint * 3 ? 0f
            : misses >= MissesPerHint * 2 ? smallCircleMetres
            : misses >= MissesPerHint ? bigCircleMetres
            : 0f;
        circle = circleRadius > 0f
            ? target + UnityEngine.Random.insideUnitCircle * (circleRadius * circleOffsetShare)
            : None;
        circlePending = circleRadius > 0f;
    }

    void Refresh() => Refresh(false);

    void Refresh(bool focusOnPlace)
    {
        if (!IsOpen || data == null) return;
        int worth = GameProgress.ScoreAfterMisses(misses);
        string status, points, check;
        bool canCheck;
        if (found || Revealed)
        {
            status = Localization.Ui(found ? "map.found" : "map.revealed", data.place.Text);
            points = Localization.Ui("map.points", worth);
            check = Localization.Ui("map.collect");
            canCheck = true;
        }
        else
        {
            if (!float.IsNaN(guess.x)) status = Localization.Ui("map.checkHint");
            else if (misses >= MissesPerHint * 2) status = Localization.Ui("map.hintSmall");
            else if (misses >= MissesPerHint) status = Localization.Ui("map.hintBig");
            else if (misses > 0) status = Localization.Ui("map.miss", MissesPerHint - misses);
            else status = Localization.Ui("map.tapHint");
            points = Localization.Ui("map.worth", worth);
            check = Localization.Ui("map.check");
            canCheck = !float.IsNaN(guess.x);
        }
        ui.SetMapStatus(status, points, check, canCheck);

        // The view moves only for news: the place found / shown, or a new hint circle.
        Vector2 focus = None;
        float focusZoom = 1f;
        if (focusOnPlace)
        {
            focus = target;
            focusZoom = 3f;
        }
        else if (circlePending && !float.IsNaN(circle.x))
        {
            focus = circle;
            focusZoom = circleRadius >= bigCircleMetres ? 1.6f : 3f;
        }
        circlePending = false;
        Vector2 shown = found || Revealed ? target : None;
        ui.SetMapMarks(guess, missed, circle, circleRadius, shown, focus, focusZoom);
    }
}
