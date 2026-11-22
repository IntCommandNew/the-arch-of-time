using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The quest map screen (part of AppUI): the riddle at the top, the map of Iasi (QuestMapView), the status line,
/// the points this place is worth, and "Is it here?" / "Back". Shown over the current screen like the sheets.
/// </summary>
public partial class AppUI
{
    /// <summary>The map was tapped: the spot in metres on the map.</summary>
    public event Action<Vector2> MapTapped;

    /// <summary>The map's main button ("Is it here?", then "Collect the piece").</summary>
    public event Action MapCheckClicked;

    /// <summary>"Back" on the map: close it without answering.</summary>
    public event Action MapBackClicked;

    VisualElement questMap;
    QuestMapView mapView;
    Label mapTitle;
    Label mapRiddle;
    Label mapStatus;
    Label mapPoints;
    Button mapCheck;

    public bool IsMapOpen => IsOpen(questMap);

    void SetUpMap()
    {
        questMap = Find<VisualElement>("quest-map");
        mapView = Find<QuestMapView>("map-view");
        mapTitle = Find<Label>("map-title");
        mapRiddle = Find<Label>("map-riddle");
        mapStatus = Find<Label>("map-status");
        mapPoints = Find<Label>("map-points");
        mapCheck = Find<Button>("btn-map-check");
        Wire(mapCheck, () => MapCheckClicked?.Invoke());
        Wire(Find<Button>("btn-map-back"), () => MapBackClicked?.Invoke());
        if (mapView != null) mapView.Tapped += OnMapViewTapped;
    }

    void TearDownMap()
    {
        if (mapView != null) mapView.Tapped -= OnMapViewTapped;
    }

    void OnMapViewTapped(Vector2 metres) => MapTapped?.Invoke(metres);

    /// <summary>Loads the map's streets, parks and water (IasiMap.json).</summary>
    public void LoadMap(TextAsset json) => mapView?.Load(json);

    /// <summary>A latitude / longitude as metres on the map (x east, y north from its south-west corner).</summary>
    public Vector2 MapGeoToMetres(double latitude, double longitude) =>
        mapView != null ? mapView.GeoToMetres(latitude, longitude) : Vector2.zero;

    /// <summary>Opens the map with the riddle above it, showing the whole map.</summary>
    public void ShowMap(string title, string riddle)
    {
        if (mapTitle != null) mapTitle.text = title ?? "";
        SetHint(mapRiddle, riddle);
        Open(questMap);
        mapView?.ShowAll();
    }

    public void HideMap() => Close(questMap);

    /// <summary>The status line, the points line, and the main button's text and state.</summary>
    public void SetMapStatus(string status, string points, string check, bool canCheck)
    {
        if (mapStatus != null) mapStatus.text = status ?? "";
        SetHint(mapPoints, points);
        if (mapCheck != null)
        {
            mapCheck.text = check ?? "";
            mapCheck.SetEnabled(canCheck);
        }
    }

    /// <summary>The marks on the map (metres; NaN = none) and where to look (focus NaN = keep the view).</summary>
    public void SetMapMarks(Vector2 guess, IEnumerable<Vector2> misses, Vector2 circle, float radius,
                            Vector2 place, Vector2 focus, float focusZoom)
    {
        if (mapView == null) return;
        mapView.SetMarks(guess, misses, circle, radius, place);
        if (!float.IsNaN(focus.x)) mapView.FocusOn(focus, focusZoom);
    }
}
