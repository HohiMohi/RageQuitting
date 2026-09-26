((System.Action)(() => {
var checks=new System.Collections.Generic.List<string>();
string project="D:/Programy/UnityProjects/RageQuitting/";
string root="Assets/Art/Environment/TerrainRoadLookdev/";
string surface=root+"SurfaceV3/";
string[] names={"RoadV3_RoadAlbedo.png","RoadV3_StoneAlbedo.png","RoadV3_DarkEarthAlbedo.png","RoadV3_B_Normal.png","RoadV3_A_Normal.png","RoadV3_CavityAO.png","RoadV3_URP_MaskMap.png","RoadV3_B_Height.exr","RoadV3_A_Height.exr"};
foreach(var name in names){
 string path=surface+name; var ti=UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
 if(ti==null) throw new System.Exception("Missing importer: "+path);
 bool normal=name.EndsWith("_Normal.png"); bool color=name.Contains("Albedo");
 if(ti.textureType!=(normal?UnityEditor.TextureImporterType.NormalMap:UnityEditor.TextureImporterType.Default)||ti.sRGBTexture!=color||ti.alphaIsTransparency||ti.wrapMode!=UnityEngine.TextureWrapMode.Repeat||!ti.mipmapEnabled||ti.filterMode!=UnityEngine.FilterMode.Trilinear||ti.anisoLevel!=8||ti.maxTextureSize!=2048) throw new System.Exception("Importer settings mismatch: "+name);
 var tex=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path); if(tex==null||tex.width!=2048||tex.height!=2048) throw new System.Exception("Map missing or wrong dimensions: "+name);
 checks.Add(name+": 2048x2048; "+ti.textureType+"; sRGB="+ti.sRGBTexture+"; Repeat/mips/trilinear/aniso8; alphaTransparency=false");
}
string dataPath=root+"TerrainData/TD_TerrainRoad_ReliefV3.asset";
var v3=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
var v2=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(root+"TerrainData/TD_TerrainRoad_ReliefV2.asset");
var baseline=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(root+"TerrainData/TD_TerrainRoad_Lookdev.asset");
if(v3==null||v2==null||baseline==null||v3.heightmapResolution!=257||v3.terrainLayers.Length!=4||v3.size!=v2.size) throw new System.Exception("V3 TerrainData invariant failed.");
var baselineHeights=baseline.GetHeights(0,0,257,257); var v2Heights=v2.GetHeights(0,0,257,257); var v3Heights=v3.GetHeights(0,0,257,257); double maxHalfError=0, maxOutsideDelta=0;
for(int y=0;y<257;y++) for(int x=0;x<257;x++) { double e=System.Math.Abs(v3Heights[y,x]-(baselineHeights[y,x]+.5*(v2Heights[y,x]-baselineHeights[y,x])))*v3.size.y; if(e>maxHalfError)maxHalfError=e; double outside=System.Math.Abs(v3Heights[y,x]-baselineHeights[y,x]); if(System.Math.Abs(v2Heights[y,x]-baselineHeights[y,x])<1e-8&&outside>maxOutsideDelta)maxOutsideDelta=outside; }
if(maxHalfError>.001||maxOutsideDelta>1e-8) throw new System.Exception("V3 half-height or outside-baseline invariant failed.");
var expectedNames=new[]{"TL_Road_ReliefV3","TL_Stone_Grey_ReliefV3","TL_Earth_Dark_ReliefV3"};
for(int i=0;i<3;i++){var l=v3.terrainLayers[i+1]; if(l.name!=expectedNames[i]||l.tileSize!=new UnityEngine.Vector2(4,4)||l.normalScale!=1f||l.metallic!=0f||l.smoothness!=0f||l.maskMapRemapMin!=UnityEngine.Vector4.zero||l.maskMapRemapMax!=UnityEngine.Vector4.one)throw new System.Exception("Terrain layer configuration mismatch at index "+(i+1));}
if(v3.terrainLayers[0].name!="TL_Grass_Warm") throw new System.Exception("Grass layer index/ref changed.");
var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene(); if(scene.path!="Assets/Scenes/TerrainRoad_Lookdev.unity"||scene.isDirty)throw new System.Exception("Lookdev scene not active/clean.");
var terrain=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>(); var collider=terrain.GetComponent<UnityEngine.TerrainCollider>(); if(terrain.terrainData!=v3||collider.terrainData!=v3)throw new System.Exception("Terrain/collider V3 references mismatch.");
var material=terrain.materialTemplate; if(material==null||material.shader.name!="Universal Render Pipeline/Terrain/Lit"||(material.HasProperty("_EnableHeightBlend")&&material.GetFloat("_EnableHeightBlend")!=0)||material.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT"))throw new System.Exception("Terrain material shader/height blend mismatch.");
var go=UnityEngine.GameObject.Find("PlayerNew"); if(go==null||UnityEngine.Vector2.Distance(new UnityEngine.Vector2(go.transform.position.x-terrain.transform.position.x,go.transform.position.z-terrain.transform.position.z),new UnityEngine.Vector2(14.15f,4.3f))>.01f)throw new System.Exception("PlayerNew roadstart mismatch.");
checks.Add("TerrainData size="+v3.size+", heightmap=257, layers=4; half-height max error="+maxHalfError+"m, outside baseline delta="+maxOutsideDelta);
checks.Add("Terrain/C terrain data same V3 asset; grass preserved; V3 layers at indices 1-3, 4m scale, normalScale1; Terrain/Lit height blend off");
checks.Add("PlayerNew localXZ="+(go.transform.position.x-terrain.transform.position.x)+","+(go.transform.position.z-terrain.transform.position.z)+"; scene active and clean");
var report="{\"status\":\"passed\",\"checks\":["+string.Join(",",checks.ConvertAll(s=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\""))+"]}";
System.IO.Directory.CreateDirectory("Artifacts/TerrainRoadV3");
System.IO.File.WriteAllText("Artifacts/TerrainRoadV3/TerrainRoadV3_IntegrationValidation.json",report,new System.Text.UTF8Encoding(false));
Debug.Log("TerrainRoadV3 integration validation passed: "+checks.Count+" checks.");
}))();
