using CesiumForUnity;
using Google.XR.ARCoreExtensions.GeospatialCreator;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The map shown in the city authoring scene (Editor only, never in the app): Cesium ion's Cesium World
/// Terrain with aerial imagery, and Cesium OSM Buildings (OpenStreetMap buildings as plain blocks).
///
/// Google's Photorealistic 3D Tiles were used until 20 Nov 2026, when Google started refusing 3D tiles to
/// accounts with a billing address in the European Economic Area (403 "not available for your account
/// and region"); the Google map and its key were removed. Google is still used on the phone: ARCore's
/// Geospatial API (positioning from Street View, terrain anchors) doesn't depend on the Map Tiles API.
///
/// Needs a Cesium ion account connected in the Cesium panel (Window > Cesium) with a default token; the
/// token is saved under Assets/CesiumSettings, which is git-ignored.
/// The origin's inspector (Google's code) still shows a "Google Map Tiles API Key" field for the first
/// tileset it finds: leave it empty, typing there would write a Google URL onto the terrain tileset.
/// </summary>
public static class CityAuthoringMap
{
    // Cesium ion asset IDs (Asset Depot): every new ion account has these in My Assets.
    const long WorldTerrainAsset = 1;
    const long AerialImageryAsset = 2;  // Bing Maps Aerial
    const long OsmBuildingsAsset = 96188;

    const string TerrainName = "Cesium World Terrain (ion)";
    const string BuildingsName = "Cesium OSM Buildings (ion)";

    [MenuItem("Tools/The Arch of Time/Add Cesium ion map")]
    static void AddFromMenu()
    {
        var origin = Object.FindFirstObjectByType<ARGeospatialCreatorOrigin>(FindObjectsInactive.Include);
        if (origin == null)
        {
            Debug.Log("[CityAuthoringMap] No Geospatial Creator origin in the open scene: open the city "
                + "authoring scene.");
            return;
        }
        string result = AddIonMap(origin);
        EditorSceneManager.MarkSceneDirty(origin.gameObject.scene);
        EditorSceneManager.SaveScene(origin.gameObject.scene);
        Debug.Log("[CityAuthoringMap] " + result);
    }

    /// <summary>
    /// Adds Cesium ion's terrain (with imagery) and buildings under the origin, unless they are already
    /// there, and makes sure they are active. The caller saves the scene.
    /// </summary>
    public static string AddIonMap(ARGeospatialCreatorOrigin origin)
    {
        var terrain = FindTileset(origin.transform, TerrainName)
            ?? CreateIonTileset(origin.transform, TerrainName, WorldTerrainAsset);
        if (terrain.GetComponent<CesiumIonRasterOverlay>() == null)
        {
            var imagery = terrain.gameObject.AddComponent<CesiumIonRasterOverlay>();
            imagery.ionAssetID = AerialImageryAsset;
        }
        var buildings = FindTileset(origin.transform, BuildingsName)
            ?? CreateIonTileset(origin.transform, BuildingsName, OsmBuildingsAsset);
        terrain.gameObject.SetActive(true);
        buildings.gameObject.SetActive(true);

        string token = CesiumIonServer.defaultServer != null
            ? CesiumIonServer.defaultServer.defaultIonAccessToken
            : null;
        return string.IsNullOrEmpty(token)
            ? "Cesium ion map added. No Cesium ion token yet: connect to Cesium ion in the Cesium panel "
                + "(Window > Cesium) and set a default token, then the map loads."
            : "Cesium ion map added.";
    }

    static Cesium3DTileset FindTileset(Transform origin, string name)
    {
        foreach (var tileset in origin.GetComponentsInChildren<Cesium3DTileset>(true))
            if (tileset.name == name) return tileset;
        return null;
    }

    static Cesium3DTileset CreateIonTileset(Transform origin, string name, long assetId)
    {
        var go = new GameObject(name) { tag = "EditorOnly" };
        Undo.RegisterCreatedObjectUndo(go, "Add Cesium ion map");
        go.transform.SetParent(origin, false);
        var tileset = go.AddComponent<Cesium3DTileset>();
        tileset.tilesetSource = CesiumDataSource.FromCesiumIon;
        tileset.ionAssetID = assetId;
        tileset.showCreditsOnScreen = true;
        // Put city anchors on the 3D ground raycasts against the tiles.
        tileset.createPhysicsMeshes = true;
        return tileset;
    }
}
