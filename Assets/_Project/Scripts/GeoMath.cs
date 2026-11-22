using System;
using UnityEngine;

/// <summary>
/// Distances and compass directions between latitude/longitude points, and directions in words (in the current
/// language: UiText.json keys compass.N, compassShort.N, dir.*).
/// </summary>
public static class GeoMath
{
    const double ToRad = Math.PI / 180.0;
    const double EarthRadius = 6371008.8; // mean radius (metres)

    /// <summary>Distance in metres between two points (haversine, accurate enough for a city).</summary>
    public static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        double dPhi = (lat2 - lat1) * ToRad, dLambda = (lon2 - lon1) * ToRad;
        double a = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2) +
                   Math.Cos(lat1 * ToRad) * Math.Cos(lat2 * ToRad) * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        return 2 * EarthRadius * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>Compass bearing from point 1 to point 2: 0 = north, 90 = east.</summary>
    public static float InitialBearing(double lat1, double lon1, double lat2, double lon2)
    {
        double phi1 = lat1 * ToRad, phi2 = lat2 * ToRad, dLambda = (lon2 - lon1) * ToRad;
        double y = Math.Sin(dLambda) * Math.Cos(phi2);
        double x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLambda);
        return Mathf.Repeat((float)(Math.Atan2(y, x) / ToRad), 360f);
    }

    /// <summary>"N", "NE", ... for a bearing in degrees.</summary>
    public static string CompassPoint(float bearing) => Localization.Ui("compassShort." + CompassIndex(bearing));

    /// <summary>"north", "north-east", ... for a bearing in degrees.</summary>
    public static string CompassName(float bearing) => Localization.Ui("compass." + CompassIndex(bearing));

    /// <summary>"350 m", "1.2 km", "12 km".</summary>
    public static string FormatDistance(double metres)
    {
        if (metres < 1000) return $"{Math.Round(metres / 10) * 10:0} m";
        if (metres < 10000) return $"{metres / 1000:0.0} km";
        return $"{metres / 1000:0} km";
    }

    /// <summary>
    /// An angle from where the user looks (positive = to the right) in words: "ahead",
    /// "ahead, slightly to the left", "to your right", "behind you, to the left", "behind you".
    /// </summary>
    public static string RelativeDirection(float angle)
    {
        float a = Mathf.Abs(angle);
        string side = angle > 0f ? "Right" : "Left";
        if (a <= 20f) return Localization.Ui("dir.ahead");
        if (a <= 60f) return Localization.Ui("dir.ahead" + side);
        if (a <= 120f) return Localization.Ui("dir." + side.ToLowerInvariant());
        if (a <= 160f) return Localization.Ui("dir.behind" + side);
        return Localization.Ui("dir.behind");
    }

    static int CompassIndex(float bearing) => Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % 8;
}
