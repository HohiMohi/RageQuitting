var folder="Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/RoadBandsV1_1";
var surface="Assets/Art/Environment/TerrainRoadLookdev/SurfaceRoadBandsV1_1/";
if(!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers","RoadBandsV1_1");
var albedo=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadBandsV1_1_Albedo.png");
var normal=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadBandsV1_1_Normal.png");
var mask=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadBandsV1_1_Mask.png");
if(albedo==null||normal==null||mask==null) throw new System.Exception("RoadBands V1.1 texture input missing");
var names=new[]{"TL_Road_RoadBandsV1_1","TL_Stone_RoadBandsV1_1","TL_Earth_RoadBandsV1_1"};
var created=new System.Collections.Generic.List<string>();
for(int i=0;i<names.Length;i++) {
 var path=folder+"/"+names[i]+".terrainlayer";
 if(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(path)!=null) throw new System.Exception("Already exists: "+path);
 var layer=new UnityEngine.TerrainLayer(); layer.name=names[i];
 layer.diffuseTexture=albedo; layer.normalMapTexture=normal; layer.maskMapTexture=mask;
 layer.tileSize=new UnityEngine.Vector2(4f,4f); layer.tileOffset=UnityEngine.Vector2.zero;
 layer.normalScale=1f; layer.metallic=0f; layer.smoothness=0f; layer.specular=UnityEngine.Color.clear;
 layer.diffuseRemapMin=UnityEngine.Color.clear; layer.diffuseRemapMax=UnityEngine.Color.white;
 layer.maskMapRemapMin=UnityEngine.Vector4.zero; layer.maskMapRemapMax=UnityEngine.Vector4.one;
 UnityEditor.AssetDatabase.CreateAsset(layer,path); UnityEditor.AssetDatabase.SaveAssetIfDirty(layer); created.Add(path);
}
UnityEditor.AssetDatabase.Refresh();
return "Created three distinct RoadBandsV1.1 terrain layers: "+string.Join(", ",created.ToArray());
