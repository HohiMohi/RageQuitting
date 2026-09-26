var lines = new System.Collections.Generic.List<string>();
foreach (var path in new[] {
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainSparse.png",
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainDense.png",
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainSparse_Normal.png",
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainDense_Normal.png"
})
{
    var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
    if (importer == null) throw new System.InvalidOperationException("Missing importer: " + path);
    lines.Add($"TEXTURE|{path}|type={importer.textureType}|sRGB={importer.sRGBTexture}|wrap={importer.wrapMode}|mips={importer.mipmapEnabled}|filter={importer.filterMode}|max={importer.maxTextureSize}");
}
foreach (var path in new[] {
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainSparse_Relief.mat",
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainDense_Relief.mat"
})
{
    var material = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
    if (material == null) throw new System.InvalidOperationException("Missing material: " + path);
    lines.Add($"MATERIAL|{path}|shader={material.shader.name}|metallic={material.GetFloat("_Metallic")}|smoothness={material.GetFloat("_Smoothness")}|bump={material.GetFloat("_BumpScale")}|baseTiling={material.GetTextureScale("_BaseMap")}|normalTiling={material.GetTextureScale("_BumpMap")}|normalKeyword={material.IsKeywordEnabled("_NORMALMAP")}");
}
return string.Join("\n", lines);
