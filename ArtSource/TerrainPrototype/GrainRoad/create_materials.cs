var sparsePath = "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainSparse_Relief.mat";
var densePath = "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainDense_Relief.mat";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(sparsePath) != null ||
    UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(densePath) != null)
    throw new System.InvalidOperationException("Refusing to overwrite an existing grain road material.");
var colorPaths = new[] {
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainSparse.png",
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainDense.png"
};
var normalPaths = new[] {
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainSparse_Normal.png",
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainDense_Normal.png"
};
UnityEditor.AssetDatabase.Refresh();
for (var i = 0; i < colorPaths.Length; i++)
{
    var colorImporter = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(colorPaths[i]);
    var normalImporter = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(normalPaths[i]);
    if (colorImporter == null || normalImporter == null)
        throw new System.InvalidOperationException("Texture importer missing for " + colorPaths[i]);
    colorImporter.textureType = UnityEditor.TextureImporterType.Default;
    colorImporter.sRGBTexture = true;
    colorImporter.wrapMode = UnityEngine.TextureWrapMode.Repeat;
    colorImporter.mipmapEnabled = true;
    colorImporter.filterMode = UnityEngine.FilterMode.Trilinear;
    colorImporter.maxTextureSize = 1024;
    colorImporter.SaveAndReimport();
    normalImporter.textureType = UnityEditor.TextureImporterType.NormalMap;
    normalImporter.sRGBTexture = false;
    normalImporter.wrapMode = UnityEngine.TextureWrapMode.Repeat;
    normalImporter.mipmapEnabled = true;
    normalImporter.filterMode = UnityEngine.FilterMode.Trilinear;
    normalImporter.maxTextureSize = 1024;
    normalImporter.SaveAndReimport();
}
UnityEditor.AssetDatabase.Refresh();
var shader = UnityEngine.Shader.Find("Universal Render Pipeline/Lit");
if (shader == null) throw new System.InvalidOperationException("URP Lit shader not found.");
var tiling = new UnityEngine.Vector2(1.275f, .75f);
var strength = 1.0f;
for (var i = 0; i < 2; i++)
{
    var color = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(colorPaths[i]);
    var normal = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(normalPaths[i]);
    if (color == null || normal == null) throw new System.InvalidOperationException("Imported grain texture missing.");
    var material = new UnityEngine.Material(shader);
    material.name = i == 0 ? "Terrain_DirtPath_Warm_GrainSparse_Relief" : "Terrain_DirtPath_Warm_GrainDense_Relief";
    material.SetTexture("_BaseMap", color);
    material.SetColor("_BaseColor", UnityEngine.Color.white);
    material.SetTexture("_BumpMap", normal);
    material.SetFloat("_BumpScale", strength);
    material.SetFloat("_Metallic", 0f);
    material.SetFloat("_Smoothness", 0f);
    material.SetTextureScale("_BaseMap", tiling);
    material.SetTextureScale("_BumpMap", tiling);
    material.EnableKeyword("_NORMALMAP");
    UnityEditor.AssetDatabase.CreateAsset(material, i == 0 ? sparsePath : densePath);
}
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();
return "Created Sparse and Dense URP Lit materials. Metallic=0, Smoothness=0, BumpScale=1.0, texture scale=(1.275,0.75).";
