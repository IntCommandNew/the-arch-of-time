using System.Collections.Generic;
using CesiumForUnity;
using Google.XR.ARCoreExtensions.GeospatialCreator;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Placing the in-city targets with Geospatial Creator.
///
/// "Create city authoring scene" makes Assets/_Project/Scenes/CityAuthoring.unity: a Geospatial Creator
/// origin with Cesium ion's map of the city (see CityAuthoringMap) and one anchor per target, each with a
/// preview (the academy at real size, a block for each piece). The scene is git-ignored (it held the Google
/// Map Tiles API key until 20 Nov 2026).
///
/// Move and turn the anchors in the Scene view, then "Export city targets" writes their positions to
/// Assets/_Project/Settings/CityTargets.asset, which the app reads.
/// The anchors are found by name, so keep the names.
/// </summary>
public static class CityTargetsAuthoring
{
    public const string ScenePath = "Assets/_Project/Scenes/CityAuthoring.unity";
    public const string TargetsPath = "Assets/_Project/Settings/CityTargets.asset";
    const string AcademyPrefabPath = "Assets/_Project/Prefabs/academy.prefab";
    const string StoneMaterialPath = "Assets/_Project/Models/Academy/Materials/m_stone.mat";

    // Iasi city centre, a starting point for the origin, and the origin's height (WGS84 m, near the ground).
    const double StartLatitude = 47.1585;
    const double StartLongitude = 27.6014;
    const double StartHeight = 60;

    // The academy's scale on the map (exported with it; change the preview's scale to change it), its
    // street level in the model (the building's bottom, put on the ground), and a piece's block (metres).
    // 35, not 100: at 1:100 the academy looked far too big next to the real houses (user, 8 Nov 2026).
    const float AcademyScale = 35f;
    const float AcademyStreetLevel = 0.0147f;
    const string AcademyPreviewName = "Academy preview";
    static readonly Vector3 PiecePreviewSize = new Vector3(1.2f, 0.8f, 0.6f);

    // Put city anchors on the 3D ground: points sampled on a ring around a Terrain anchor (metres).
    const float GroundSampleRadius = 12f;
    const int GroundSamples = 8;
    // ... only for anchors this close to the Scene view camera (metres): farther away only coarse tiles are
    // loaded (with the view 52 km away, the Iasi anchors came out 7 to 33 m too high, 20 Nov 2026).
    const float GroundMaxViewDistance = 3000f;

    // Export refuses anchors farther than this from the map origin (metres): their position went wrong.
    const double MaxDistanceFromOrigin = 200000;

    /// The targets, in GameProgress piece order after the academy. The anchor names are their names.
    static readonly (string name, CityTargetKind kind, int piece)[] Targets =
    {
        ("Academy", CityTargetKind.Academy, -1),
        ("Left column", CityTargetKind.ArchPiece, 0),
        ("Right column", CityTargetKind.ArchPiece, 1),
        ("Arc", CityTargetKind.ArchPiece, 2),
        ("Top beam", CityTargetKind.ArchPiece, 3),
    };

    [MenuItem("Tools/The Arch of Time/Create city authoring scene")]
    static void CreateSceneFromMenu() => Debug.Log("[CityTargetsAuthoring] " + CreateScene());

    [MenuItem("Tools/The Arch of Time/Put city anchors on the 3D ground")]
    static void SnapFromMenu() => Debug.Log("[CityTargetsAuthoring] " + PutAnchorsOnGround());

    [MenuItem("Tools/The Arch of Time/Export city targets")]
    static void ExportFromMenu() => Debug.Log("[CityTargetsAuthoring] " + Export());

    /// The academy preview's scale (its first child), or the default if it has none.
    static float AcademyPreviewScale(Transform anchor)
    {
        return anchor.childCount > 0 ? anchor.GetChild(0).localScale.x : AcademyScale;
    }

    /// <summary>
    /// Geospatial Creator draws Terrain and Rooftop anchors in the Scene view at their altitude above the
    /// WGS84 ellipsoid (about 80 m under the ground in Iasi), because the Editor doesn't know the terrain.
    /// This sets each anchor's Editor altitude override to the 3D tiles' surface under it (the lowest
    /// surface for Terrain, the highest for Rooftop). Editor only: the app still uses the real terrain.
    /// Needs collision on the tiles; the first run turns it on, run it again once the tiles have reloaded.
    /// </summary>
    public static string PutAnchorsOnGround()
    {
        // Every map layer shown (Cesium ion's terrain and buildings, see CityAuthoringMap).
        var tilesets = Object.FindObjectsByType<Cesium3DTileset>(FindObjectsSortMode.None);
        var georeference = Object.FindFirstObjectByType<CesiumGeoreference>();
        if (tilesets.Length == 0 || georeference == null)
            return "No map in the open scene: Tools > The Arch of Time > Add Cesium ion map.";
        bool turnedOn = false;
        foreach (var tileset in tilesets)
        {
            if (tileset.createPhysicsMeshes) continue;
            tileset.createPhysicsMeshes = true;
            tileset.RecreateTileset();
            EditorUtility.SetDirty(tileset);
            turnedOn = true;
        }
        if (turnedOn)
        {
            EditorSceneManager.MarkSceneDirty(georeference.gameObject.scene);
            return "Turned on collision for the map. Wait until it has reloaded, then run this again.";
        }

        Physics.SyncTransforms();
        var report = new System.Text.StringBuilder("Editor altitude (WGS84 m):");
        foreach (var anchor in Object.FindObjectsByType<ARGeospatialCreatorAnchor>(FindObjectsSortMode.None))
        {
            if (anchor.AltitudeType == AnchorAltitudeType.WGS84) continue;
            var view = SceneView.lastActiveSceneView;
            if (view != null
                && Vector3.Distance(view.camera.transform.position, anchor.transform.position) > GroundMaxViewDistance)
            {
                report.Append($" {anchor.name}: too far from the view (move the view there and wait);");
                continue;
            }
            // From latitude / longitude: the anchor's Unity position can be out of date.
            var above = georeference.TransformEarthCenteredEarthFixedPositionToUnity(
                CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                    new Unity.Mathematics.double3(anchor.Longitude, anchor.Latitude, 3000.0)));
            // Terrain also samples a ring around the spot, so a monument or a car under the anchor
            // doesn't count as the ground.
            var start = new Vector3((float)above.x, (float)above.y, (float)above.z);
            float ring = anchor.AltitudeType == AnchorAltitudeType.Rooftop ? 0f : GroundSampleRadius;
            bool found = false;
            var point = Vector3.zero;
            for (int i = 0; i <= GroundSamples; i++)
            {
                var offset = i == 0 || ring == 0f
                    ? Vector3.zero
                    : Quaternion.Euler(0f, 360f * i / GroundSamples, 0f) * new Vector3(ring, 0f, 0f);
                foreach (var hit in Physics.RaycastAll(start + offset, Vector3.down, 6000f))
                {
                    bool better = anchor.AltitudeType == AnchorAltitudeType.Rooftop
                        ? hit.point.y > point.y
                        : hit.point.y < point.y;
                    if (!found || better) point = hit.point;
                    found = true;
                }
            }
            if (!found)
            {
                report.Append($" {anchor.name}: no tiles under it (move the view there and wait);");
                continue;
            }
            var ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(
                new Unity.Mathematics.double3(point.x, point.y, point.z));
            double height = CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef).z;

            Undo.RecordObject(anchor, "Put city anchors on the 3D ground");
            anchor.UseEditorAltitudeOverride = true;
            anchor.EditorAltitudeOverride = height;
            EditorUtility.SetDirty(anchor);
            report.Append($" {anchor.name} {height:0.0};");
        }
        EditorSceneManager.MarkSceneDirty(georeference.gameObject.scene);
        return report.ToString();
    }

    /// <summary>Creates and opens the authoring scene (does nothing if it already exists).</summary>
    public static string CreateScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            EditorSceneManager.OpenScene(ScenePath);
            return "The authoring scene already exists; opened it.";
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Cancelled.";

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var originObject = new GameObject("AR Geospatial Creator Origin", typeof(ARGeospatialCreatorOrigin));
        originObject.tag = "EditorOnly";
        var origin = originObject.GetComponent<ARGeospatialCreatorOrigin>();
        origin.SetOriginPoint(StartLatitude, StartLongitude, StartHeight);
        // The georeference places the map; the object must be active when it is added (an inactive one
        // stayed at Cesium's default place in Colorado, 8 Nov 2026).
        var georeference = originObject.AddComponent<CesiumGeoreference>();
        georeference.SetOriginLongitudeLatitudeHeight(StartLongitude, StartLatitude, StartHeight);
        string map = CityAuthoringMap.AddIonMap(origin);
        AddAnchorManagerStandIn();

        var academyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AcademyPrefabPath);
        var stone = AssetDatabase.LoadAssetAtPath<Material>(StoneMaterialPath);
        for (int i = 0; i < Targets.Length; i++)
        {
            var (name, kind, _) = Targets[i];
            var anchorObject = new GameObject(name, typeof(ARGeospatialCreatorAnchor));
            var anchor = anchorObject.GetComponent<ARGeospatialCreatorAnchor>();
            anchor.Origin = origin;
            anchor.AltitudeType = AnchorAltitudeType.Terrain;
            anchor.Altitude = kind == CityTargetKind.ArchPiece ? 1.0 : 0.0; // pieces float 1 m above the ground
            // Spread them out around the origin, so each can be picked and moved.
            anchor.Latitude = StartLatitude + 0.0002 * (i - 2);
            anchor.Longitude = StartLongitude + (kind == CityTargetKind.Academy ? -0.0006 : 0.0);

            if (kind == CityTargetKind.Academy && academyPrefab != null)
            {
                var preview = (GameObject)PrefabUtility.InstantiatePrefab(academyPrefab, anchorObject.transform);
                SetAcademyPreview(preview.transform, AcademyScale);
            }
            else
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Preview";
                Object.DestroyImmediate(block.GetComponent<Collider>());
                block.transform.SetParent(anchorObject.transform, false);
                block.transform.localScale = PiecePreviewSize;
                if (stone != null) block.GetComponent<MeshRenderer>().sharedMaterial = stone;
            }
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        return $"Created {ScenePath}. {map} Move the anchors, then export.";
    }

    /// <summary>
    /// The academy preview as the app shows it: the given scale, the building's bottom (street level) on the
    /// anchor, the platform hidden. To resize it, change its scale and run this again (or just export: the
    /// export reads the scale).
    /// </summary>
    public static void SetAcademyPreview(Transform preview, float scale)
    {
        preview.name = AcademyPreviewName;
        preview.localScale = Vector3.one * scale;
        preview.localPosition = new Vector3(0f, -AcademyStreetLevel * scale, 0f);
        preview.localRotation = Quaternion.identity;
        var platform = preview.Find("Platform");
        if (platform != null)
            foreach (var renderer in platform.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
    }

    /// <summary>
    /// Every Geospatial Creator anchor looks for an ARAnchorManager when the scene loads and logs "No
    /// ARAnchorManagers were found in the scene" (an error, five per load) if there is none. The authoring
    /// scene has no AR, so an INACTIVE, EditorOnly object with one stands in (Google's code accepts an
    /// inactive manager). Returns false if the open scene already has one.
    /// </summary>
    [MenuItem("Tools/The Arch of Time/Add anchor manager stand-in to the open scene")]
    public static bool AddAnchorManagerStandIn()
    {
        if (Resources.FindObjectsOfTypeAll<UnityEngine.XR.ARFoundation.ARAnchorManager>().Length > 0) return false;
        var standIn = new GameObject("Anchor manager stand-in (inactive, Editor only)");
        standIn.SetActive(false);
        standIn.tag = "EditorOnly";
        standIn.AddComponent<UnityEngine.XR.ARFoundation.ARAnchorManager>(); // adds the XROrigin it requires
        EditorSceneManager.MarkSceneDirty(standIn.scene);
        return true;
    }

    /// <summary>Writes the anchors of the open scene to the CityTargets asset.</summary>
    public static string Export()
    {
        var found = new Dictionary<string, ARGeospatialCreatorAnchor>();
        foreach (var anchor in Object.FindObjectsByType<ARGeospatialCreatorAnchor>(FindObjectsSortMode.None))
            found[anchor.name] = anchor;

        var targets = new List<CityTarget>();
        var missing = new List<string>();
        var faraway = new List<string>();
        var origin = Object.FindFirstObjectByType<ARGeospatialCreatorOrigin>();
        var originGeo = origin != null ? origin.GetComponent<CesiumGeoreference>() : null;
        foreach (var (name, kind, piece) in Targets)
        {
            if (!found.TryGetValue(name, out var anchor))
            {
                missing.Add(name);
                continue;
            }
            // An anchor whose scene position jumped (8 Nov 2026: two of them suddenly read 38.48 N, 179.06 W)
            // gets its latitude / longitude recomputed from that position: never export such a place.
            if (originGeo != null && GeoMath.Distance(originGeo.latitude, originGeo.longitude, anchor.Latitude,
                    anchor.Longitude) > MaxDistanceFromOrigin)
                faraway.Add($"{name} ({anchor.Latitude:0.0000}, {anchor.Longitude:0.0000})");
            targets.Add(new CityTarget
            {
                name = name,
                kind = kind,
                piece = piece,
                latitude = anchor.Latitude,
                longitude = anchor.Longitude,
                altitudeType = anchor.AltitudeType switch
                {
                    AnchorAltitudeType.Rooftop => CityTargetAltitude.Rooftop,
                    AnchorAltitudeType.WGS84 => CityTargetAltitude.WGS84,
                    _ => CityTargetAltitude.Terrain,
                },
                altitude = anchor.Altitude,
                eunRotation = anchor.transform.rotation,
                scale = kind == CityTargetKind.Academy ? AcademyPreviewScale(anchor.transform) : 0f,
            });
        }
        if (missing.Count > 0)
            return "Not exported: no anchor named " + string.Join(", ", missing) + " in the open scene.";
        if (faraway.Count > 0)
            return $"Not exported: more than {MaxDistanceFromOrigin / 1000:0} km from the map origin: " +
                   string.Join(", ", faraway) + ". Set their latitude / longitude again in the inspector.";

        var asset = AssetDatabase.LoadAssetAtPath<CityTargets>(TargetsPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<CityTargets>();
            AssetDatabase.CreateAsset(asset, TargetsPath);
        }
        asset.targets = targets.ToArray();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();

        var report = new System.Text.StringBuilder($"Exported {targets.Count} targets to {TargetsPath}:");
        report.Append($" (academy scale {targets.Find(x => x.kind == CityTargetKind.Academy)?.scale:0.##})");
        foreach (var t in targets)
            report.Append($" {t.name} ({t.latitude:0.000000}, {t.longitude:0.000000}, " +
                          $"{t.altitudeType} {t.altitude:0.0} m);");
        return report.ToString();
    }
}
