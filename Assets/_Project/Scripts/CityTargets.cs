using System;
using UnityEngine;

/// <summary>What a target in the city is.</summary>
public enum CityTargetKind
{
    Academy,     // viewed only
    ArchPiece,   // collected (piece number in CityTarget.piece)
}

/// <summary>Where a target is anchored in height.</summary>
public enum CityTargetAltitude
{
    Terrain,  // on the ground (Google's terrain model); altitude = metres above it
    Rooftop,  // on a building's roof; altitude = metres above it
    WGS84,    // a fixed height above the WGS84 ellipsoid; altitude = that height
}

/// <summary>One place of the in-city mode, as authored with Geospatial Creator.</summary>
[Serializable]
public class CityTarget
{
    [Tooltip("Shown to the user, e.g. \"The academy\" or \"Left column\".")]
    public string name;
    public CityTargetKind kind;
    [Tooltip("For arch pieces: 0 left column, 1 right column, 2 arc, 3 top beam (as in GameProgress).")]
    public int piece = -1;
    public double latitude;
    public double longitude;
    public CityTargetAltitude altitudeType = CityTargetAltitude.Terrain;
    public double altitude;
    [Tooltip("Rotation in the East-Up-North frame, as Geospatial Creator and ARCore's geospatial anchors use it.")]
    public Quaternion eunRotation = Quaternion.identity;
    [Tooltip("The academy: the model's scale, as previewed in the authoring scene (0 = the app's default). " +
             "Its street level (the building's bottom) goes on the anchor; the platform is hidden.")]
    public float scale;

    /// <summary>The name shown in the app ("The academy", "Left column").</summary>
    public string DisplayName => kind == CityTargetKind.Academy ? "The academy" : name;

    /// <summary>A piece already collected in the in-city mode (the academy never is).</summary>
    public bool IsCollected => kind == CityTargetKind.ArchPiece && GameProgress.IsCollected(GameMode.InCity, piece);
}

/// <summary>
/// The in-city mode's targets (the academy and the 4 arch pieces). Written by the editor tool
/// Tools > The Arch of Time > Export city targets from the Geospatial Creator anchors of the authoring
/// scene, and read by the app at runtime. Holds no keys, so it is safe to commit.
/// </summary>
[CreateAssetMenu(fileName = "CityTargets", menuName = "The Arch of Time/City targets")]
public class CityTargets : ScriptableObject
{
    public CityTarget[] targets = Array.Empty<CityTarget>();
}
