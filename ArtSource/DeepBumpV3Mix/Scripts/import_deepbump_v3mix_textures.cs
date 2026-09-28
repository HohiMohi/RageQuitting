var assetRoot = "Assets/Art/Environment/TerrainRoadLookdev/SurfaceDeepBumpV3Mix";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(assetRoot + "/Road_Albedo_V1.png") != null)
    throw new System.InvalidOperationException("DeepBump V3Mix surface assets already exist; refusing to overwrite.");
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Art/Environment/TerrainRoadLookdev/SurfaceDeepBumpV3Mix"))
    UnityEditor.AssetDatabase.CreateFolder("Assets/Art/Environment/TerrainRoadLookdev", "SurfaceDeepBumpV3Mix");
var albedoSource = System.IO.Path.GetFullPath("ArtSource/DeepBumpV3Mix/road_albedo.png");
var normalSource = System.IO.Path.GetFullPath("ArtSource/DeepBumpV3Mix/road_normal_deepbump16.png");
var expectedHashes = new[] { "C3168D8742CB5EABC2886BD1C134A0E48B6A438EC4D286F9F82BC5F905C86B55", "BF6C42699CFF0E232AF59CA251FDB213F5363A9B813EC149C1CDE76E1FD5B44D" };
var sourcePaths = new[] { albedoSource, normalSource };
for (var i = 0; i < sourcePaths.Length; i++) {
    using (var sha = System.Security.Cryptography.SHA256.Create())
    using (var stream = System.IO.File.OpenRead(sourcePaths[i])) {
        var actual = System.BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        if (!string.Equals(actual, expectedHashes[i], System.StringComparison.OrdinalIgnoreCase))
            throw new System.IO.InvalidDataException("Approved source hash mismatch: " + sourcePaths[i] + " actual=" + actual);
    }
}
var albedoPath = assetRoot + "/Road_Albedo_V1.png";
var normalPath = assetRoot + "/Road_Normal_DeepBump_V2.png";
System.IO.File.Copy(albedoSource, System.IO.Path.GetFullPath(albedoPath), false);
System.IO.File.Copy(normalSource, System.IO.Path.GetFullPath(normalPath), false);
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var texturePaths = new[] { albedoPath, normalPath };
for (var i = 0; i < texturePaths.Length; i++) {
    UnityEditor.AssetDatabase.ImportAsset(texturePaths[i], UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    var importer = UnityEditor.AssetImporter.GetAtPath(texturePaths[i]) as UnityEditor.TextureImporter;
    if (importer == null) throw new System.InvalidOperationException("TextureImporter missing: " + texturePaths[i]);
    var isNormal = i == 1;
    importer.textureType = isNormal ? UnityEditor.TextureImporterType.NormalMap : UnityEditor.TextureImporterType.Default;
    importer.sRGBTexture = !isNormal;
    importer.convertToNormalmap = false;
    importer.flipGreenChannel = false;
    importer.npotScale = UnityEditor.TextureImporterNPOTScale.None;
    importer.maxTextureSize = 2048;
    importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
    importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
    importer.mipmapEnabled = true;
    importer.filterMode = UnityEngine.FilterMode.Trilinear;
    importer.anisoLevel = 8;
    importer.SaveAndReimport();
    var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(texturePaths[i]);
    if (texture == null || texture.width != 1254 || texture.height != 1254)
        throw new System.InvalidOperationException("Unexpected imported dimensions for " + texturePaths[i]);
}
var maskPath = assetRoot + "/Road_Constant_Mask.png";
var mask = new UnityEngine.Texture2D(4, 4, UnityEngine.TextureFormat.RGBA32, false, true) { name = "Road_Constant_Mask" };
var pixels = new UnityEngine.Color32[16];
for (var i = 0; i < pixels.Length; i++) pixels[i] = new UnityEngine.Color32(0, 255, 128, 0);
mask.SetPixels32(pixels);
mask.Apply(false, false);
System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(maskPath), mask.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(mask);
UnityEditor.AssetDatabase.ImportAsset(maskPath, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var maskImporter = UnityEditor.AssetImporter.GetAtPath(maskPath) as UnityEditor.TextureImporter;
maskImporter.textureType = UnityEditor.TextureImporterType.Default;
maskImporter.sRGBTexture = false;
maskImporter.convertToNormalmap = false;
maskImporter.npotScale = UnityEditor.TextureImporterNPOTScale.None;
maskImporter.maxTextureSize = 2048;
maskImporter.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
maskImporter.wrapMode = UnityEngine.TextureWrapMode.Repeat;
maskImporter.mipmapEnabled = true;
maskImporter.filterMode = UnityEngine.FilterMode.Trilinear;
maskImporter.anisoLevel = 8;
maskImporter.SaveAndReimport();
UnityEditor.AssetDatabase.SaveAssets();
System.Func<string, string> describeTexture = path => {
    var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
    if (importer == null || texture == null) throw new System.InvalidOperationException("Imported texture missing: " + path);
    return "{\"path\":\"" + path + "\",\"width\":" + texture.width + ",\"height\":" + texture.height +
        ",\"format\":\"" + texture.format + "\",\"graphicsFormat\":\"" + texture.graphicsFormat +
        "\",\"type\":\"" + importer.textureType + "\",\"sRGB\":" + importer.sRGBTexture.ToString().ToLowerInvariant() +
        ",\"convertToNormalmap\":" + importer.convertToNormalmap.ToString().ToLowerInvariant() +
        ",\"flipGreen\":" + importer.flipGreenChannel.ToString().ToLowerInvariant() +
        ",\"npot\":\"" + importer.npotScale + "\",\"maxSize\":" + importer.maxTextureSize +
        ",\"compression\":\"" + importer.textureCompression + "\",\"wrap\":\"" + importer.wrapMode +
        "\",\"mipmaps\":" + importer.mipmapEnabled.ToString().ToLowerInvariant() +
        ",\"filter\":\"" + importer.filterMode + "\",\"aniso\":" + importer.anisoLevel + "}";
};
var report = "{\"assets\":[" + describeTexture(albedoPath) + "," + describeTexture(normalPath) + "," + describeTexture(maskPath) + "]}";
var diagnosticsPath = System.IO.Path.GetFullPath("Artifacts/DeepBumpF9/imported-textures.json");
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(diagnosticsPath));
System.IO.File.WriteAllText(diagnosticsPath, report);
return report;
