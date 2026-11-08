using UnityEditor;

/// <summary>
/// Keeps Cesium for Unity's native Android library out of the app. Cesium is only used in the Editor (to
/// show Google's 3D map in the city authoring scene); its Android library added about 31 MB to the APK.
/// The package's own import settings can't be changed (packages are read-only), so this tells Unity not
/// to include those plugins in builds.
/// </summary>
[InitializeOnLoad]
static class CesiumBuildExclusion
{
    const string CesiumAndroidPlugins = "Packages/com.cesium.unity/Plugins/Android/";

    static CesiumBuildExclusion()
    {
        foreach (var importer in PluginImporter.GetAllImporters())
        {
            if (importer.assetPath.StartsWith(CesiumAndroidPlugins))
                importer.SetIncludeInBuildDelegate(_ => false);
        }
    }
}
