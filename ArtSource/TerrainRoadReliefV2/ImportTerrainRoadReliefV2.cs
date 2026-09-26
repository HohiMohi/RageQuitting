((System.Action)(() => {
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate);

string assetDir = "Assets/Art/Environment/TerrainRoadLookdev/SurfaceV2";
string[] names = {
    "T_Road_ReliefV2", "N_Road_ReliefV2",
    "T_Stone_Grey_ReliefV2", "N_Stone_Grey_ReliefV2",
    "T_Earth_Dark_ReliefV2", "N_Earth_Dark_ReliefV2",
    "M_Road_StonePatches", "M_Road_EarthRecess", "M_Road_EarthPatches", "M_Road_FineChips"
};
System.Collections.Generic.List<string> rows = new System.Collections.Generic.List<string>();
bool allPassed = true;
foreach (string name in names)
{
    string path = assetDir + "/" + name + ".png";
    UnityEditor.TextureImporter importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    if (importer == null)
        throw new System.InvalidOperationException("Unity did not import texture: " + path);

    bool isNormal = name.StartsWith("N_", System.StringComparison.Ordinal);
    bool isMask = name.StartsWith("M_", System.StringComparison.Ordinal);
    importer.textureType = isNormal ? UnityEditor.TextureImporterType.NormalMap : UnityEditor.TextureImporterType.Default;
    importer.sRGBTexture = !isNormal && !isMask;
    importer.flipGreenChannel = false;
    importer.mipmapEnabled = !isMask;
    if (isMask)
        importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
    importer.SaveAndReimport();

    UnityEditor.TextureImporter verified = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    UnityEngine.Texture2D texture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
    bool passed = verified != null && texture != null && texture.width == 1024 && texture.height == 1024
        && verified.sRGBTexture == (!isNormal && !isMask)
        && verified.flipGreenChannel == false
        && verified.textureType == (isNormal ? UnityEditor.TextureImporterType.NormalMap : UnityEditor.TextureImporterType.Default)
        && verified.mipmapEnabled == !isMask;
    allPassed &= passed;
    rows.Add("    {\"name\":\"" + name + "\",\"width\":" + (texture == null ? 0 : texture.width)
        + ",\"height\":" + (texture == null ? 0 : texture.height)
        + ",\"type\":\"" + (verified == null ? "missing" : verified.textureType.ToString())
        + "\",\"sRGB\":" + (verified != null && verified.sRGBTexture ? "true" : "false")
        + ",\"flipGreenChannel\":" + (verified != null && verified.flipGreenChannel ? "true" : "false")
        + ",\"mipmaps\":" + (verified != null && verified.mipmapEnabled ? "true" : "false")
        + ",\"passed\":" + (passed ? "true" : "false") + "}");
}

string report = "{\n  \"unityEditorVersion\":\"" + UnityEngine.Application.unityVersion
    + "\",\n  \"assetCount\":" + names.Length
    + ",\n  \"allPassed\":" + (allPassed ? "true" : "false")
    + ",\n  \"textures\":[\n" + string.Join(",\n", rows) + "\n  ]\n}\n";
string reportDirectory = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Artifacts", "TerrainRoadReliefV2");
System.IO.Directory.CreateDirectory(reportDirectory);
System.IO.File.WriteAllText(System.IO.Path.Combine(reportDirectory, "unity_import_validation.json"), report, System.Text.Encoding.UTF8);
if (!allPassed)
    throw new System.InvalidOperationException("One or more Unity texture import checks failed.");
UnityEngine.Debug.Log("TerrainRoadReliefV2 imported and verified: " + names.Length + " textures.");
}))();
