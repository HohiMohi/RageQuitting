((System.Action)(() => {
// Run phases 1 and 2 separately with unity command eval_file; all asset/scene writes use Editor APIs.
if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new System.Exception("Refusing V3 authoring while the runtime comparison is active.");
int phase = 2;
const string root = "Assets/Art/Environment/TerrainRoadLookdev/";
const string surface = root + "SurfaceV3/";
const string layerDir = root + "TerrainLayers/";
const string dataPath = root + "TerrainData/TD_TerrainRoad_ReliefV3.asset";
const string v2Path = root + "TerrainData/TD_TerrainRoad_ReliefV2.asset";
const string basePath = root + "TerrainData/TD_TerrainRoad_Lookdev.asset";
const string materialPath = root + "Materials/M_TerrainRoad_TerrainLit_V3.mat";
string[] maps = {"RoadV3_RoadAlbedo.png","RoadV3_StoneAlbedo.png","RoadV3_DarkEarthAlbedo.png","RoadV3_B_Normal.png","RoadV3_A_Normal.png","RoadV3_CavityAO.png","RoadV3_URP_MaskMap.png","RoadV3_B_Height.exr","RoadV3_A_Height.exr"};
string[] layerNames = {"TL_Road_ReliefV3","TL_Stone_Grey_ReliefV3","TL_Earth_Dark_ReliefV3"};
string[] v2Names = {"TL_Road_ReliefV2","TL_Stone_Grey_ReliefV2","TL_Earth_Dark_ReliefV2"};
if (phase == 1) {
    foreach (string name in maps) {
        string path = surface + name;
        var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
        if (importer == null) throw new System.Exception("Missing TextureImporter: " + path);
        bool normal = name.EndsWith("_Normal.png");
        bool color = name.Contains("Albedo");
        importer.textureType = normal ? UnityEditor.TextureImporterType.NormalMap : UnityEditor.TextureImporterType.Default;
        importer.sRGBTexture = color;
        importer.alphaIsTransparency = false;
        importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.filterMode = UnityEngine.FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.maxTextureSize = 2048;
        importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }
    UnityEditor.AssetDatabase.Refresh();
} else if (phase == 2) {
    var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
    if(!scene.IsValid()||scene.path!="Assets/Scenes/TerrainRoad_Lookdev.unity"||scene.isDirty) throw new System.Exception("Expected clean active TerrainRoad_Lookdev scene.");
    var terrain=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
    if(terrain==null) throw new System.Exception("Lookdev Terrain missing.");
    var collider=terrain.GetComponent<UnityEngine.TerrainCollider>();
    var sceneData=UnityEditor.AssetDatabase.GetAssetPath(terrain.terrainData);
    if(collider==null||collider.terrainData!=terrain.terrainData||(sceneData!=v2Path&&sceneData!=dataPath)) throw new System.Exception("Lookdev Terrain/Collider is not on the expected V2/V3 data pair.");
    var go=UnityEngine.GameObject.Find("PlayerNew");
    var cc=go==null?null:go.GetComponentInChildren<UnityEngine.CharacterController>();
    if(go==null||cc==null) throw new System.Exception("PlayerNew or its CharacterController is missing.");
    var switcherType=System.Type.GetType("RageQuitting.Lookdev.TerrainRoadV3.TerrainRoadV3RuntimeSwitcher, TerrainRoadV3.Runtime");
    if(switcherType==null) throw new System.Exception("Runtime switcher assembly has not compiled yet.");
    var baseData = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(basePath);
    var v2Data = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(v2Path);
    if (baseData == null || v2Data == null) throw new System.Exception("Terrain source data missing.");
    if (!System.IO.File.Exists(dataPath)) UnityEditor.AssetDatabase.CopyAsset(v2Path, dataPath);
    var v3Data = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
    if (v3Data == null) throw new System.Exception("Could not load duplicated V3 TerrainData.");
    int resolution = v2Data.heightmapResolution;
    if (resolution != baseData.heightmapResolution) throw new System.Exception("Terrain resolution mismatch.");
    float[,] b = baseData.GetHeights(0,0,resolution,resolution);
    float[,] v = v2Data.GetHeights(0,0,resolution,resolution);
    float[,] blend = new float[resolution,resolution];
    for(int sampleY=0;sampleY<resolution;sampleY++) for(int x=0;x<resolution;x++) blend[sampleY,x]=b[sampleY,x]+0.5f*(v[sampleY,x]-b[sampleY,x]);
    v3Data.SetHeights(0,0,blend);

    var texRoad=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadV3_RoadAlbedo.png");
    var texStone=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadV3_StoneAlbedo.png");
    var texEarth=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadV3_DarkEarthAlbedo.png");
    var normal=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadV3_B_Normal.png");
    var mask=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadV3_URP_MaskMap.png");
    var colors=new UnityEngine.Texture2D[]{texRoad,texStone,texEarth};
    var v2Layers=new UnityEngine.TerrainLayer[3];
    for(int i=0;i<3;i++) {
        string path=layerDir+layerNames[i]+".terrainlayer";
        v2Layers[i]=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(layerDir+v2Names[i]+".terrainlayer");
        if(v2Layers[i]==null) throw new System.Exception("Missing V2 layer "+v2Names[i]);
        if(!System.IO.File.Exists(path)) {
            var layer=(UnityEngine.TerrainLayer)UnityEngine.Object.Instantiate(v2Layers[i]);
            layer.name=layerNames[i];
            UnityEditor.AssetDatabase.CreateAsset(layer,path);
        }
        var v3=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(path);
        v3.diffuseTexture=colors[i]; v3.normalMapTexture=normal; v3.maskMapTexture=mask; v3.normalScale=1f;
        v3.tileSize=new UnityEngine.Vector2(4f,4f); v3.tileOffset=v2Layers[i].tileOffset;
        v3.metallic=0f; v3.smoothness=0f;
        v3.maskMapRemapMin=UnityEngine.Vector4.zero; v3.maskMapRemapMax=UnityEngine.Vector4.one;
        UnityEditor.EditorUtility.SetDirty(v3);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(v3);
    }
    var stack=(UnityEngine.TerrainLayer[])v2Data.terrainLayers.Clone();
    for(int i=0;i<3;i++) {
        int index=System.Array.IndexOf(stack,v2Layers[i]);
        if(index<0) throw new System.Exception("V2 layer absent from stack: "+v2Layers[i].name);
        stack[index]=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(layerDir+layerNames[i]+".terrainlayer");
    }
    v3Data.terrainLayers=stack;
    UnityEditor.EditorUtility.SetDirty(v3Data);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(v3Data);

    if(!System.IO.File.Exists(materialPath)) UnityEditor.AssetDatabase.CopyAsset(root+"Materials/M_TerrainRoad_TerrainLit.mat",materialPath);
    var material=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(materialPath);
    var terrainShader=UnityEngine.Shader.Find("Universal Render Pipeline/Terrain/Lit");
    if(material==null||terrainShader==null) throw new System.Exception("V3 terrain material or URP Terrain/Lit shader missing.");
    material.shader=terrainShader;
    if(material.HasProperty("_EnableHeightBlend")) material.SetFloat("_EnableHeightBlend",0f);
    material.DisableKeyword("_TERRAIN_BLEND_HEIGHT");
    UnityEditor.EditorUtility.SetDirty(material);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(material);

    terrain.terrainData=v3Data; collider.terrainData=v3Data; terrain.materialTemplate=material;
    var worldXZ=terrain.transform.position+new UnityEngine.Vector3(14.15f,0f,4.3f);
    float y=terrain.SampleHeight(worldXZ)+terrain.transform.position.y-cc.center.y+cc.height*.5f+.08f;
    go.transform.position=new UnityEngine.Vector3(worldXZ.x,y,worldXZ.z);
    var switcher=terrain.GetComponent(switcherType);
    if(switcher==null) switcher=terrain.gameObject.AddComponent(switcherType);
    var so=new UnityEditor.SerializedObject(switcher);
    so.FindProperty("terrain").objectReferenceValue=terrain;
    so.FindProperty("sourceLayers").arraySize=3;
    for(int i=0;i<3;i++) so.FindProperty("sourceLayers").GetArrayElementAtIndex(i).objectReferenceValue=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(layerDir+layerNames[i]+".terrainlayer");
    so.FindProperty("normalA").objectReferenceValue=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(surface+"RoadV3_A_Normal.png");
    so.FindProperty("normalB").objectReferenceValue=normal;
    so.ApplyModifiedPropertiesWithoutUndo();
    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene)) throw new System.Exception("Failed to save lookdev scene.");
}
}))();
