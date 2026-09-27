var paths=new[]{
"Assets/Art/Environment/TerrainRoadLookdev/SurfaceRoadBandsV1_1/RoadBandsV1_1_Albedo.png",
"Assets/Art/Environment/TerrainRoadLookdev/SurfaceRoadBandsV1_1/RoadBandsV1_1_Normal.png",
"Assets/Art/Environment/TerrainRoadLookdev/SurfaceRoadBandsV1_1/RoadBandsV1_1_Mask.png"};
var results=new System.Collections.Generic.List<string>();
foreach(var path in paths){var imp=UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter; var tex=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path); if(imp==null||tex==null) throw new System.Exception("Missing importer/texture: "+path); var standalone=imp.GetPlatformTextureSettings("Standalone"); var windows=imp.GetPlatformTextureSettings("Windows"); results.Add(path+"|size="+tex.width+"x"+tex.height+"|type="+imp.textureType+"|sRGB="+imp.sRGBTexture+"|flip="+imp.flipGreenChannel+"|convert="+imp.convertToNormalmap+"|wrap="+imp.wrapMode+"|mips="+imp.mipmapEnabled+"|filter="+imp.filterMode+"|aniso="+imp.anisoLevel+"|compression="+imp.textureCompression+"|standaloneOverride="+standalone.overridden+"|windowsOverride="+windows.overridden);}
var folder="Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/RoadBandsV1_1/";
var layers=new[]{"TL_Road_RoadBandsV1_1","TL_Stone_RoadBandsV1_1","TL_Earth_RoadBandsV1_1"};
var seen=new System.Collections.Generic.HashSet<int>();
foreach(var n in layers){var l=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(folder+n+".terrainlayer"); if(l==null||!seen.Add(l.GetInstanceID())) throw new System.Exception("Missing or repeated TerrainLayer: "+n); if(l.diffuseTexture!=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(paths[0])||l.normalMapTexture!=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(paths[1])||l.maskMapTexture!=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(paths[2])) throw new System.Exception("Layer map reference mismatch: "+n); if(l.tileSize!=new UnityEngine.Vector2(4,4)||l.tileOffset!=UnityEngine.Vector2.zero||l.normalScale!=1||l.metallic!=0||l.smoothness!=0||l.diffuseRemapMin!=UnityEngine.Vector4.zero||l.diffuseRemapMax!=UnityEngine.Vector4.one||l.maskMapRemapMin!=UnityEngine.Vector4.zero||l.maskMapRemapMax!=UnityEngine.Vector4.one) throw new System.Exception("Layer properties mismatch: "+n); results.Add(n+"|unique=true|tile=4,4|offset=0,0|normalScale=1|metallic=0|smoothness=0|diffuseRemap=neutral|minMaxMask="+l.maskMapRemapMin+"/"+l.maskMapRemapMax);}
var report="{\"textureImport\":["+string.Join(",",System.Array.ConvertAll(results.ToArray(),x=>"\""+x.Replace("\\","\\\\").Replace("\"","\\\"")+"\""))+"],\"distinctLayerCount\":"+seen.Count+"}";
System.IO.File.WriteAllText("Artifacts/RoadBandsV1_1/import_validation.json",report);
return report;


