if(UnityEditor.EditorApplication.isPlaying) throw new System.Exception("Stop Play Mode before assigning scene templates.");
var scene=UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
const string expectedScene="Assets/Scenes/TerrainRoad_Lookdev.unity";
if(scene.path!=expectedScene) throw new System.Exception("Unexpected active scene: "+scene.path);
if(scene.isDirty) throw new System.Exception("Scene is already dirty; refusing to save unrelated changes.");
var switcher=UnityEngine.Object.FindFirstObjectByType<RageQuitting.Lookdev.TerrainRoadV3.TerrainRoadV3RuntimeSwitcher>();
if(switcher==null) throw new System.Exception("Road switcher not found");
var so=new UnityEditor.SerializedObject(switcher);
var p=so.FindProperty("v4TemplateLayers");
if(p==null) throw new System.Exception("v4TemplateLayers serialized field not found");
p.arraySize=3;
var names=new[]{"TL_Road_ReliefV4_1","TL_Stone_Grey_ReliefV4_1","TL_Earth_Dark_ReliefV4_1"};
var paths=new string[3];
for(int i=0;i<3;i++){
    paths[i]="Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/V4_1/"+names[i]+".terrainlayer";
    var layer=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(paths[i]);
    if(layer==null) throw new System.Exception("Missing V4.1 template: "+paths[i]);
    p.GetArrayElementAtIndex(i).objectReferenceValue=layer;
}
so.ApplyModifiedProperties();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene)) throw new System.Exception("Scene save failed");
return "Assigned and saved only the three v4TemplateLayers references: "+string.Join(", ",paths);
