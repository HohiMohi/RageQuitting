var assetRoot = "Assets/Art/Environment/TerrainRoadLookdev/SurfaceDeepBumpV3Mix/";
var layerRoot = "Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/DeepBumpV3Mix/";
var textureNames = new[] { "Road_Albedo_V1.png", "Road_Normal_DeepBump_V2.png", "Road_Constant_Mask.png" };
var textureReports = new System.Collections.Generic.List<string>();
for (var i = 0; i < textureNames.Length; i++) {
    var path = assetRoot + textureNames[i];
    var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
    if (importer == null || texture == null) throw new System.InvalidOperationException("Missing texture/importer: " + path);
    if (i < 2 && (texture.width != 1254 || texture.height != 1254)) throw new System.InvalidOperationException("Source dimensions changed: " + path);
    if (importer.npotScale != UnityEditor.TextureImporterNPOTScale.None || importer.maxTextureSize != 2048 || importer.textureCompression != UnityEditor.TextureImporterCompression.Uncompressed || importer.wrapMode != UnityEngine.TextureWrapMode.Repeat || !importer.mipmapEnabled || importer.filterMode != UnityEngine.FilterMode.Trilinear || importer.anisoLevel != 8 || importer.convertToNormalmap || importer.flipGreenChannel)
        throw new System.InvalidOperationException("Texture importer settings mismatch: " + path);
    var expectsNormal = i == 1;
    if (importer.textureType != (expectsNormal ? UnityEditor.TextureImporterType.NormalMap : UnityEditor.TextureImporterType.Default) || importer.sRGBTexture != (i == 0))
        throw new System.InvalidOperationException("Texture color space/type mismatch: " + path);
    textureReports.Add("{\"path\":\"" + path + "\",\"width\":" + texture.width + ",\"height\":" + texture.height + ",\"format\":\"" + texture.format + "\",\"graphicsFormat\":\"" + texture.graphicsFormat + "\",\"type\":\"" + importer.textureType + "\",\"sRGB\":" + importer.sRGBTexture.ToString().ToLowerInvariant() + "}");
}
var maskBytes = System.IO.File.ReadAllBytes(System.IO.Path.GetFullPath(assetRoot + textureNames[2]));
var readableMask = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false, true);
if (!readableMask.LoadImage(maskBytes)) throw new System.InvalidOperationException("Cannot decode constant mask PNG.");
var maskPixels = readableMask.GetPixels32();
foreach (var pixel in maskPixels) if (pixel.r != 0 || pixel.g != 255 || pixel.b != 128 || pixel.a != 0) throw new System.InvalidOperationException("Constant mask is not exact RGBA (0,255,128,0).");
UnityEngine.Object.DestroyImmediate(readableMask);
var layerNames = new[] { "TL_Road_DeepBumpV3Mix", "TL_Stone_DeepBumpV3Mix", "TL_Earth_DeepBumpV3Mix" };
var ids = new System.Collections.Generic.HashSet<string>();
var layerReports = new System.Collections.Generic.List<string>();
for (var i = 0; i < layerNames.Length; i++) {
    var path = layerRoot + layerNames[i] + ".terrainlayer";
    var layer = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(path);
    if (layer == null) throw new System.InvalidOperationException("Missing TerrainLayer: " + path);
    var guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
    if (!ids.Add(guid)) throw new System.InvalidOperationException("TerrainLayer assets are not distinct: " + path);
    if (layer.diffuseTexture != UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(assetRoot + textureNames[0]) || layer.normalMapTexture != UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(assetRoot + textureNames[1]) || layer.maskMapTexture != UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(assetRoot + textureNames[2])) throw new System.InvalidOperationException("TerrainLayer map reference mismatch: " + path);
    if (layer.tileSize != new UnityEngine.Vector2(4f,4f) || layer.tileOffset != UnityEngine.Vector2.zero || layer.normalScale != 1f || layer.metallic != 0f || layer.smoothness != 0f || layer.specular != UnityEngine.Color.clear || layer.diffuseRemapMin != UnityEngine.Vector4.zero || layer.diffuseRemapMax != UnityEngine.Vector4.one || layer.maskMapRemapMin != UnityEngine.Vector4.zero || layer.maskMapRemapMax != UnityEngine.Vector4.one) throw new System.InvalidOperationException("TerrainLayer property mismatch: " + path);
    layerReports.Add("{\"path\":\"" + path + "\",\"guid\":\"" + guid + "\",\"distinct\":true,\"sharedMaps\":true,\"tileSize\":\"4,4\",\"tileOffset\":\"0,0\",\"normalScale\":1,\"metallic\":0,\"smoothness\":0,\"diffuseRemap\":\"neutral\",\"maskRemap\":\"neutral\"}");
}
var report = "{\"textureImport\":[" + string.Join(",", textureReports.ToArray()) + "],\"maskPixel\":\"RGBA(0,255,128,0)\",\"layerValidation\":[" + string.Join(",", layerReports.ToArray()) + "]}";
var reportPath = System.IO.Path.GetFullPath("Artifacts/DeepBumpF9/asset-validation.json");
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(reportPath));
System.IO.File.WriteAllText(reportPath, report);
return report;
