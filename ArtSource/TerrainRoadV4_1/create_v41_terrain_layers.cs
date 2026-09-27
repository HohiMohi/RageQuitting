string folder="Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/V4_1";
string parent="Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers";
if(!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder(parent,"V4_1");
var surface="Assets/Art/Environment/TerrainRoadLookdev/SurfaceV4_1/";
var normal=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadV4_1_Normal.png");
var mask=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadV4_1_URP_MaskMap.png");
var diffuse=new[]{"RoadV4_1_RoadAlbedo.png","RoadV4_1_StoneAlbedo.png","RoadV4_1_DarkEarthAlbedo.png"};
var names=new[]{"TL_Road_ReliefV4_1","TL_Stone_Grey_ReliefV4_1","TL_Earth_Dark_ReliefV4_1"};
if(normal==null||mask==null) throw new System.Exception("V4.1 normal or mask was not imported");
var created=new System.Collections.Generic.List<string>();
for(int i=0;i<3;i++) {
    string path=folder+"/"+names[i]+".terrainlayer";
    if(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(path)!=null) throw new System.Exception("Already exists: "+path);
    var albedo=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+diffuse[i]);
    if(albedo==null) throw new System.Exception("Missing albedo: "+diffuse[i]);
    var layer=new UnityEngine.TerrainLayer();
    layer.name=names[i]; layer.diffuseTexture=albedo; layer.normalMapTexture=normal; layer.maskMapTexture=mask;
    layer.tileSize=new UnityEngine.Vector2(4f,4f); layer.tileOffset=UnityEngine.Vector2.zero;
    layer.normalScale=1f; layer.metallic=0f; layer.smoothness=0f; layer.specular=UnityEngine.Color.clear;
    layer.diffuseRemapMin=UnityEngine.Color.clear; layer.diffuseRemapMax=UnityEngine.Color.white;
    layer.maskMapRemapMin=UnityEngine.Vector4.zero; layer.maskMapRemapMax=UnityEngine.Vector4.one;
    UnityEditor.AssetDatabase.CreateAsset(layer,path);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(layer);
    created.Add(path);
}
UnityEditor.AssetDatabase.Refresh();
return "Created V4.1 layers: "+string.Join(", ",created.ToArray());
