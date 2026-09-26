var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if(scene.path!="Assets/Scenes/TerrainRoad_Lookdev.unity") throw new System.Exception("Active scene guard failed: "+scene.path);
if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new System.Exception("Asset authoring requires Edit Mode.");
var rootPath="Assets/Art/Environment/TerrainRoadLookdev/RoadsideV1";
var dataPath=rootPath+"/TerrainData/TD_TerrainRoad_RoadsideV1.asset";
if(!UnityEditor.AssetDatabase.IsValidFolder(rootPath+"/TerrainData")) UnityEditor.AssetDatabase.CreateFolder(rootPath,"TerrainData");
if(!UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath)) {
    if(!UnityEditor.AssetDatabase.CopyAsset("Assets/Art/Environment/TerrainRoadLookdev/TerrainData/TD_TerrainRoad_ReliefV3.asset",dataPath)) throw new System.Exception("TerrainData clone failed");
}
var baseLayerPath=rootPath+"/TerrainData/TL_RoadsideV1_Grass.terrainlayer";
var layer=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(baseLayerPath);
if(layer==null) { layer=new UnityEngine.TerrainLayer(); layer.name="TL_RoadsideV1_Grass"; UnityEditor.AssetDatabase.CreateAsset(layer,baseLayerPath); }
layer.diffuseTexture=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(rootPath+"/RoadsideV1_Ground_Albedo.png");
layer.normalMapTexture=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(rootPath+"/RoadsideV1_Ground_Normal.png");
layer.maskMapTexture=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(rootPath+"/RoadsideV1_Ground_URP_MaskMap.png");
layer.tileSize=new UnityEngine.Vector2(4,4); layer.tileOffset=UnityEngine.Vector2.zero; layer.metallic=0; layer.smoothness=0; layer.normalScale=1; layer.maskMapRemapMin=UnityEngine.Vector4.zero; layer.maskMapRemapMax=UnityEngine.Vector4.one;
UnityEditor.EditorUtility.SetDirty(layer); UnityEditor.AssetDatabase.SaveAssetIfDirty(layer);
var shader=UnityEngine.Shader.Find("Universal Render Pipeline/Lit"); if(shader==null) throw new System.Exception("URP/Lit shader missing");
var grassMatPath=rootPath+"/Materials/M_RoadsideV1_Grass.mat"; var rockMatPath=rootPath+"/Materials/M_RoadsideV1_Rock.mat";
if(!UnityEditor.AssetDatabase.IsValidFolder(rootPath+"/Materials")) UnityEditor.AssetDatabase.CreateFolder(rootPath,"Materials");
var grassMat=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(grassMatPath);
if(grassMat==null) { grassMat=new UnityEngine.Material(shader); grassMat.name="M_RoadsideV1_Grass"; UnityEditor.AssetDatabase.CreateAsset(grassMat,grassMatPath); }
grassMat.shader=shader; grassMat.SetTexture("_BaseMap",UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(rootPath+"/RoadsideV1_Grass_Atlas.png"));
grassMat.SetFloat("_Cull",0); grassMat.SetFloat("_AlphaClip",0); grassMat.SetFloat("_Surface",0); grassMat.SetFloat("_Metallic",0); grassMat.SetFloat("_Smoothness",0.15f); grassMat.SetColor("_EmissionColor",UnityEngine.Color.black); grassMat.DisableKeyword("_EMISSION");
grassMat.SetFloat("_ReceiveShadows",1); grassMat.DisableKeyword("_RECEIVE_SHADOWS_OFF"); grassMat.DisableKeyword("_ALPHATEST_ON"); grassMat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); grassMat.renderQueue=-1;
UnityEditor.EditorUtility.SetDirty(grassMat); UnityEditor.AssetDatabase.SaveAssetIfDirty(grassMat);
var rockMat=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(rockMatPath);
if(rockMat==null) { rockMat=new UnityEngine.Material(shader); rockMat.name="M_RoadsideV1_Rock"; UnityEditor.AssetDatabase.CreateAsset(rockMat,rockMatPath); }
rockMat.shader=shader; rockMat.SetTexture("_BaseMap",UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(rootPath+"/RoadsideV1_Rock_Albedo.png"));
rockMat.SetTexture("_BumpMap",UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(rootPath+"/RoadsideV1_Rock_Normal.png")); rockMat.SetFloat("_BumpScale",1); rockMat.SetFloat("_Metallic",0); rockMat.SetFloat("_Smoothness",0.24f); rockMat.EnableKeyword("_NORMALMAP"); rockMat.SetFloat("_Surface",0); rockMat.SetColor("_EmissionColor",UnityEngine.Color.black); rockMat.DisableKeyword("_EMISSION");
rockMat.SetFloat("_ReceiveShadows",1); rockMat.DisableKeyword("_RECEIVE_SHADOWS_OFF"); rockMat.DisableKeyword("_ALPHATEST_ON"); rockMat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); rockMat.renderQueue=-1;
UnityEditor.EditorUtility.SetDirty(rockMat); UnityEditor.AssetDatabase.SaveAssetIfDirty(rockMat);
var prefabs=rootPath+"/Prefabs"; if(!UnityEditor.AssetDatabase.IsValidFolder(prefabs)) UnityEditor.AssetDatabase.CreateFolder(rootPath,"Prefabs");
var previewScene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try {
for(int i=1;i<=3;i++) {
    string kind="Grass"; string num=i.ToString("00",System.Globalization.CultureInfo.InvariantCulture); string name="SM_Roadside"+kind+"_"+num;
    string modelPath=rootPath+"/Models/"+name+".fbx"; var model=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(modelPath); if(model==null) throw new System.Exception("Missing model "+modelPath);
    string prefabPath=prefabs+"/PF_RoadsideV1_"+kind+"_"+num+".prefab";
    var temp=new UnityEngine.GameObject("PF_RoadsideV1_"+kind+"_"+num); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(temp,previewScene); var nested=(UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(model,previewScene); nested.transform.SetParent(temp.transform,false);
    foreach(var renderer in temp.GetComponentsInChildren<UnityEngine.Renderer>(true)) { renderer.sharedMaterial=grassMat; renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On; renderer.receiveShadows=true; }
    var saved=UnityEditor.PrefabUtility.SaveAsPrefabAsset(temp,prefabPath); UnityEngine.Object.DestroyImmediate(temp);
    if(saved==null) throw new System.Exception("Prefab save failed "+prefabPath);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(saved);
}
for(int i=1;i<=3;i++) {
    string name="SM_RoadsideRock_0"+i; var model=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(rootPath+"/Models/"+name+".fbx"); if(model==null) throw new System.Exception("Missing rock model "+name);
    string prefabPath=prefabs+"/PF_RoadsideV1_Rock_0"+i+".prefab"; var temp=new UnityEngine.GameObject("PF_RoadsideV1_Rock_0"+i); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(temp,previewScene); var nested=(UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(model,previewScene); nested.transform.SetParent(temp.transform,false);
    foreach(var renderer in temp.GetComponentsInChildren<UnityEngine.Renderer>(true)) { renderer.sharedMaterial=rockMat; renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On; renderer.receiveShadows=true; }
    var saved=UnityEditor.PrefabUtility.SaveAsPrefabAsset(temp,prefabPath); UnityEngine.Object.DestroyImmediate(temp); if(saved==null) throw new System.Exception("Prefab save failed "+prefabPath); UnityEditor.AssetDatabase.SaveAssetIfDirty(saved);
}
} finally { if(previewScene.IsValid())UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene); }
return "created terrain clone/layer and shared materials; six prefabs ready";
