using System;
using UnityEngine;

/// <summary>Distances and compass directions between latitude/longitude points (WGS84 sphere approximation).</summary>
public static class GeoMath
{
    const double EarthRadius = 6371000.0; // metres
    const double ToRad = Math.PI / 180.0;

    static readonly string[] CompassPoints = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    /// <summary>Compass bearing from point 1 to point 2: 0 = north, 90 = east.</summary>
    public static float InitialBearing(double lat1, double lon1, double lat2, double lon2)
    {
        double phi1 = lat1 * ToRad, phi2 = lat2 * ToRad, dLambda = (lon2 - lon1) * ToRad;
        double y = Math.Sin(dLambda) * Math.Cos(phi2);
        double x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLambda);
        return Mathf.Repeat((float)(Math.Atan2(y, x) / ToRad), 360f);
    }

    /// <summary>Great-circle distance in metres (haversine).</summary>
    public static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        double dPhi = (lat2 - lat1) * ToRad, dLambda = (lon2 - lon1) * ToRad;
        double a = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2) +
                   Math.Cos(lat1 * ToRad) * Math.Cos(lat2 * ToRad) * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        return EarthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>"N", "NE", ... for a bearing in degrees.</summary>
    public static string CompassPoint(float bearing) =>
        CompassPoints[Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % 8];

    /// <summary>"35 m" or "1.2 km".</summary>
    public static string FormatDistance(double metres) =>
        metres >= 1000 ? $"{metres / 1000:0.0} km" : $"{metres:0} m";

    /// <summary>
    /// An angle from where the user looks (positive = to the right) in words: "ahead",
    /// "ahead, slightly to the left", "to your right", "behind you, to the left", "behind you".
    /// </summary>
    public static string RelativeDirection(float angle)
    {
        float a = Mathf.Abs(angle);
        string side = angle > 0f ? "right" : "left";
        if (a <= 20f) return "ahead";
        if (a <= 60f) return $"ahead, slightly to the {side}";
        if (a <= 120f) return $"to your {side}";
        if (a <= 160f) return $"behind you, to the {side}";
        return "behind you";
    }
}
