using System;
using System.IO;
using UnityEngine;

/// <summary>
/// One saved placement: the cloud anchor that finds the spot again, where the model sits relative to
/// it, and roughly where the spot is (only to guide the user there). Stored as JSON on the phone
/// (see PlacementStore).
/// </summary>
[Serializable]
public class SavedPlacement
{
    public int version = 2;
    public string savedAtUtc;

    [Header("Cloud anchor: the only way the building is restored")]
    public string cloudAnchorId;
    public string cloudAnchorExpiresUtc;

    [Header("Rough location of the spot: guidance only, never used to place the building")]
    public bool hasLocation;
    public double latitude;
    public double longitude;
    public float locationAccuracy;   // metres, when it was saved

    [Header("Model relative to the anchor")]
    public Vector3 modelLocalPosition;
    public Quaternion modelLocalRotation = Quaternion.identity;
    public float modelScale = 1f;
    public bool platformVisible = true;

    public bool HasCloudAnchor => !string.IsNullOrEmpty(cloudAnchorId);

    public bool CloudAnchorExpired =>
        DateTime.TryParse(cloudAnchorExpiresUtc, null, System.Globalization.DateTimeStyles.RoundtripKind,
                          out var expires) &&
        DateTime.UtcNow >= expires;
}

/// <summary>
/// Reads and writes the saved placement (Application.persistentDataPath/placement.json).
/// Writing goes through a temporary file, so a crash mid-write never leaves a broken save.
/// </summary>
public static class PlacementStore
{
    const string FileName = "placement.json";

    static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    public static bool Exists => File.Exists(FilePath);

    public static SavedPlacement Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var saved = JsonUtility.FromJson<SavedPlacement>(File.ReadAllText(FilePath));
            // Without a cloud anchor a save can't be restored (version 1 saves could be location-only).
            if (saved == null || !saved.HasCloudAnchor) return null;
            return saved;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlacementStore] Could not read the saved placement: {e.Message}");
            return null;
        }
    }

    public static bool Save(SavedPlacement placement)
    {
        try
        {
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(placement, true));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temp, FilePath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlacementStore] Could not write the saved placement: {e.Message}");
            return false;
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlacementStore] Could not delete the saved placement: {e.Message}");
        }
    }
}
