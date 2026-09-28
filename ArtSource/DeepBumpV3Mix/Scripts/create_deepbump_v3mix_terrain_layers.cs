var layerFolder = "Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/DeepBumpV3Mix";
var surface = "Assets/Art/Environment/TerrainRoadLookdev/SurfaceDeepBumpV3Mix/";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(layerFolder + "/TL_Road_DeepBumpV3Mix.terrainlayer") != null)
    throw new System.InvalidOperationException("DeepBump V3Mix TerrainLayers already exist; refusing to overwrite.");
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/DeepBumpV3Mix"))
    UnityEditor.AssetDatabase.CreateFolder("Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers", "DeepBumpV3Mix");
var albedo = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface + "Road_Albedo_V1.png");
var normal = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface + "Road_Normal_DeepBump_V2.png");
var mask = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface + "Road_Constant_Mask.png");
if (albedo == null || normal == null || mask == null) throw new System.InvalidOperationException("A DeepBump source map is missing.");
var names = new[] { "TL_Road_DeepBumpV3Mix", "TL_Stone_DeepBumpV3Mix", "TL_Earth_DeepBumpV3Mix" };
var paths = new System.Collections.Generic.List<string>();
for (var i = 0; i < names.Length; i++) {
    var path = layerFolder + "/" + names[i] + ".terrainlayer";
    var layer = new UnityEngine.TerrainLayer { name = names[i] };
    layer.diffuseTexture = albedo;
    layer.normalMapTexture = normal;
    layer.maskMapTexture = mask;
    layer.tileSize = new UnityEngine.Vector2(4f, 4f);
    layer.tileOffset = UnityEngine.Vector2.zero;
    layer.normalScale = 1f;
    layer.metallic = 0f;
    layer.smoothness = 0f;
    layer.specular = UnityEngine.Color.clear;
    layer.diffuseRemapMin = UnityEngine.Vector4.zero;
    layer.diffuseRemapMax = UnityEngine.Vector4.one;
    layer.maskMapRemapMin = UnityEngine.Vector4.zero;
    layer.maskMapRemapMax = UnityEngine.Vector4.one;
    UnityEditor.AssetDatabase.CreateAsset(layer, path);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(layer);
    paths.Add(path);
}
UnityEditor.AssetDatabase.Refresh();
return "Created distinct TerrainLayers: " + string.Join(", ", paths.ToArray());
