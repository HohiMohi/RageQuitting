var failures=new System.Collections.Generic.List<string>();
var rows=new System.Collections.Generic.List<string>();
var surface="Assets/Art/Environment/TerrainRoadLookdev/SurfaceV4_1/";
var files=new[]{"RoadV4_1_RoadAlbedo.png","RoadV4_1_StoneAlbedo.png","RoadV4_1_DarkEarthAlbedo.png","RoadV4_1_Normal.png","RoadV4_1_Height.exr","RoadV4_1_AO.png","RoadV4_1_URP_MaskMap.png"};
var layers=new[]{"TL_Road_ReliefV4_1","TL_Stone_Grey_ReliefV4_1","TL_Earth_Dark_ReliefV4_1"};
for(int i=0;i<files.Length;i++){
    string path=surface+files[i];
    var importer=UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    var texture=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
    if(importer==null||texture==null){failures.Add("Missing imported texture "+path);continue;}
    bool normal=i==3; bool srgb=i<3;
    bool ok=importer.textureType==(normal?UnityEditor.TextureImporterType.NormalMap:UnityEditor.TextureImporterType.Default)&&importer.sRGBTexture==srgb&&importer.wrapMode==UnityEngine.TextureWrapMode.Repeat&&importer.mipmapEnabled&&importer.filterMode==UnityEngine.FilterMode.Trilinear&&importer.anisoLevel==8&&importer.maxTextureSize==2048&&importer.textureCompression==UnityEditor.TextureImporterCompression.Uncompressed&&texture.width==2048&&texture.height==2048;
    if(!ok) failures.Add("Texture importer mismatch: "+path);
    rows.Add("{\"path\":\""+path+"\",\"width\":"+texture.width+",\"height\":"+texture.height+",\"format\":\""+texture.format+"\",\"type\":\""+importer.textureType+"\",\"sRGB\":"+importer.sRGBTexture.ToString().ToLower()+",\"wrap\":\""+importer.wrapMode+"\",\"mips\":"+importer.mipmapEnabled.ToString().ToLower()+",\"filter\":\""+importer.filterMode+"\",\"aniso\":"+importer.anisoLevel+",\"maxTextureSize\":"+importer.maxTextureSize+",\"compression\":\""+importer.textureCompression+"\",\"ok\":"+(ok?"true":"false")+"}");
}
var roadLayers=new UnityEngine.TerrainLayer[3];
for(int i=0;i<layers.Length;i++){
    string path="Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/V4_1/"+layers[i]+".terrainlayer";
    var l=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(path); roadLayers[i]=l;
    var albedo=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+files[i]);
    bool ok=l!=null&&l.diffuseTexture==albedo&&l.normalMapTexture==UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+files[3])&&l.maskMapTexture==UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+files[6])&&l.tileSize==new UnityEngine.Vector2(4f,4f)&&l.tileOffset==UnityEngine.Vector2.zero&&System.Math.Abs(l.normalScale-1f)<1e-5f&&l.metallic==0f&&l.smoothness==0f&&l.diffuseRemapMin.w==0f&&l.diffuseRemapMax.w==1f&&l.maskMapRemapMin==UnityEngine.Vector4.zero&&l.maskMapRemapMax==UnityEngine.Vector4.one;
    if(!ok) failures.Add("TerrainLayer settings mismatch: "+path);
}
var scene=UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if(scene.path!="Assets/Scenes/TerrainRoad_Lookdev.unity"||scene.isDirty) failures.Add("Target scene is missing, wrong, or dirty.");
var terrain=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
var collider=terrain?terrain.GetComponent<UnityEngine.TerrainCollider>():null;
var switcher=terrain?terrain.GetComponent<RageQuitting.Lookdev.TerrainRoadV3.TerrainRoadV3RuntimeSwitcher>():null;
var so=switcher==null?null:new UnityEditor.SerializedObject(switcher);
var templateProp=so==null?null:so.FindProperty("v4TemplateLayers");
if(terrain==null||collider==null||switcher==null||templateProp==null||templateProp.arraySize!=3) failures.Add("Terrain/switcher/serialized template array missing.");
else for(int i=0;i<3;i++) if(templateProp.GetArrayElementAtIndex(i).objectReferenceValue!=roadLayers[i]) failures.Add("Scene template array mismatch at index "+i);
var player=UnityEngine.GameObject.Find("PlayerNew");
var baselineData=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>("Assets/Art/Environment/TerrainRoadLookdev/RoadsideV1/TerrainData/TD_TerrainRoad_RoadsideV1.asset");
string heightHash="",alphaHash="";
if(baselineData!=null){
    var h=baselineData.GetHeights(0,0,baselineData.heightmapResolution,baselineData.heightmapResolution);var hb=new byte[h.Length*sizeof(float)];System.Buffer.BlockCopy(h,0,hb,0,hb.Length);
    using(var sha=System.Security.Cryptography.SHA256.Create()) heightHash=System.BitConverter.ToString(sha.ComputeHash(hb)).Replace("-","");
    var a=baselineData.GetAlphamaps(0,0,baselineData.alphamapWidth,baselineData.alphamapHeight);var ab=new byte[a.Length*sizeof(float)];System.Buffer.BlockCopy(a,0,ab,0,ab.Length);
    using(var sha=System.Security.Cryptography.SHA256.Create()) alphaHash=System.BitConverter.ToString(sha.ComputeHash(ab)).Replace("-","");
    if(!heightHash.Equals("e7c79929ccba4b7c8b748bc321a338701d657cc8220bc42469524b14147cd536",System.StringComparison.OrdinalIgnoreCase)) failures.Add("Roadside V1 heightmap hash changed.");
    if(!alphaHash.Equals("0b0fe297ae520d0da58366d571f3c359435da09ab1f715ff75bc910e08250607",System.StringComparison.OrdinalIgnoreCase)) failures.Add("Roadside V1 alphamap hash changed.");
}
if(terrain==null||collider==null||baselineData==null||terrain.terrainData!=baselineData||collider.terrainData!=baselineData) failures.Add("Edit-mode source Terrain/TerrainCollider do not reference the original Roadside V1 TerrainData.");
bool playerOk=player!=null&&System.Math.Abs(player.transform.localPosition.x-14.15f)<.001f&&System.Math.Abs(player.transform.localPosition.y-.445746f)<.001f&&System.Math.Abs(player.transform.localPosition.z-4.3f)<.001f;
if(!playerOk) failures.Add("PlayerNew local spawn position changed.");
var failureJson=failures.Count==0?"[]":"[\""+string.Join("\",\"",failures.ToArray())+"\"]";
var output="{\"passed\":"+(failures.Count==0?"true":"false")+",\"scene\":\""+scene.path+"\",\"sceneDirty\":"+scene.isDirty.ToString().ToLower()+",\"terrainAndColliderUseOriginalRoadsideV1Data\":"+(terrain!=null&&collider!=null&&baselineData!=null&&terrain.terrainData==baselineData&&collider.terrainData==baselineData?"true":"false")+",\"roadsideV1HeightSha256\":\""+heightHash+"\",\"roadsideV1AlphamapSha256\":\""+alphaHash+"\",\"playerLocalPosition\":\""+(player==null?"missing":player.transform.localPosition.ToString("F6"))+"\",\"textures\":["+string.Join(",",rows.ToArray())+"],\"templateLayerCount\":"+roadLayers.Length+",\"failures\":"+failureJson+"}";
System.IO.File.WriteAllText("Artifacts/TerrainRoadV4_1/UnityEditModeImportSceneValidation.json",output,new System.Text.UTF8Encoding(false));
if(failures.Count>0) throw new System.Exception(string.Join("; ",failures.ToArray()));
return output;

