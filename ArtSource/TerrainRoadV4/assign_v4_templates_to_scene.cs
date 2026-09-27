var scene=UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if(scene.path!="Assets/Scenes/TerrainRoad_Lookdev.unity") throw new System.Exception("Unexpected active scene: "+scene.path);
var switcher=UnityEngine.Object.FindFirstObjectByType<RageQuitting.Lookdev.TerrainRoadV3.TerrainRoadV3RuntimeSwitcher>();
if(switcher==null) throw new System.Exception("Road switcher not found");
var so=new UnityEditor.SerializedObject(switcher);
var p=so.FindProperty("v4TemplateLayers");
p.arraySize=3;
var names=new[]{"TL_Road_ReliefV4","TL_Stone_Grey_ReliefV4","TL_Earth_Dark_ReliefV4"};
for(int i=0;i<3;i++){var l=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>("Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/V4/"+names[i]+".terrainlayer");if(l==null)throw new System.Exception("Missing V4 template: "+names[i]);p.GetArrayElementAtIndex(i).objectReferenceValue=l;}
so.ApplyModifiedProperties();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene)) throw new System.Exception("Scene save failed");
return "Assigned V4 templates to TerrainRoad_Lookdev and saved";