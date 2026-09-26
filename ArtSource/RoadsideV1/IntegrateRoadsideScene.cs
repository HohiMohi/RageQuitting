var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if(scene.path!="Assets/Scenes/TerrainRoad_Lookdev.unity"||UnityEditor.EditorApplication.isPlaying) throw new System.Exception("Expected target scene in Edit Mode.");
var terrain=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
var td=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>("Assets/Art/Environment/TerrainRoadLookdev/RoadsideV1/TerrainData/TD_TerrainRoad_RoadsideV1.asset");
if(terrain==null||td==null) throw new System.Exception("Terrain or RoadsideData missing.");
terrain.terrainData=td; terrain.GetComponent<UnityEngine.TerrainCollider>().terrainData=td;
var existing=UnityEngine.GameObject.Find("Roadside_V1");
if(existing!=null) UnityEngine.Object.DestroyImmediate(existing);
void DisableAt(string expectedName,float x,float z){
 var all=UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(UnityEngine.FindObjectsInactive.Include,UnityEngine.FindObjectsSortMode.None); int count=0;
 foreach(var go in all){if(go.scene!=scene||go.name!=expectedName)continue;var p=go.transform.position;if(System.Math.Abs(p.x-x)<0.06f&&System.Math.Abs(p.z-z)<0.06f){go.SetActive(false);count++;}}
 if(count==0) throw new System.Exception("Expected overlapping legacy prop not found: "+expectedName+" at "+x+","+z);
}
DisableAt("SM_RoadsideRock_02_Instance",19.6f,13.7f);
DisableAt("SM_RoadsideGrass_02_Instance",20.3f,13.9f);
DisableAt("SM_RoadsideGrass_01_Instance",14.7f,16.2f);
DisableAt("SM_RoadsideGrassClusterExtra_0",20.3f,13.9f);
DisableAt("SM_RoadsideGrassClusterExtra_1",20.3f,13.9f);
var root=new UnityEngine.GameObject("Roadside_V1"); root.transform.SetPositionAndRotation(UnityEngine.Vector3.zero,UnityEngine.Quaternion.identity);
var grassRoot=new UnityEngine.GameObject("GrassClumps"); grassRoot.transform.SetParent(root.transform,false);
var rockRoot=new UnityEngine.GameObject("Rocks"); rockRoot.transform.SetParent(root.transform,false);
string prefabFolder="Assets/Art/Environment/TerrainRoadLookdev/RoadsideV1/Prefabs/";
var grassPrefabs=new UnityEngine.GameObject[3]; var rockPrefabs=new UnityEngine.GameObject[3];
for(int i=0;i<3;i++){grassPrefabs[i]=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabFolder+"PF_RoadsideV1_Grass_0"+(i+1)+".prefab");rockPrefabs[i]=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabFolder+"PF_RoadsideV1_Rock_0"+(i+1)+".prefab");if(grassPrefabs[i]==null||rockPrefabs[i]==null)throw new System.Exception("Missing Roadside prefab "+i);}
double[] routeX={14,14.4,15.5,17,17.6,17.1,16.3,16}; double[] routeZ={3,6.5,10,13,16.8,20,24,28};
double RouteX(double z){for(int i=0;i<routeZ.Length-1;i++){if(z<=routeZ[i+1])return routeX[i]+(routeX[i+1]-routeX[i])*(z-routeZ[i])/(routeZ[i+1]-routeZ[i]);}return routeX[routeX.Length-1];}
double RouteSlope(double z){for(int i=0;i<routeZ.Length-1;i++)if(z<=routeZ[i+1])return (routeX[i+1]-routeX[i])/(routeZ[i+1]-routeZ[i]);return 0;}
double[] stationsA={10.15,10.40,10.70,11.15,11.40,12.10,12.35,12.75,13.25,13.60,14.25,14.50,14.80,15.20,15.55,16.05,16.35,16.65,17.10,17.45,17.70,17.95,18.15,18.30,18.45};
double[] stationsB={9.60,9.90,10.25,10.80,11.10,11.85,12.20,12.55,13.00,13.35,13.80,14.10,14.50,14.85,15.20,15.60,15.90,16.20,16.65,16.95,17.30,17.55,17.85,18.05,18.20};
double[] sideA={1.70,1.95,2.15,2.65,2.25,2.45,2.70,1.75,2.05,2.35,2.00,1.75,2.40,2.60,2.15,1.90,2.25,1.80,2.55,2.10,2.35,1.70,2.40,2.00,2.65};
double[] sideB={2.45,1.75,2.55,2.05,2.70,2.15,2.60,1.80,2.40,2.10,2.65,1.75,2.20,2.70,1.95,2.35,1.70,2.55,2.15,2.60,1.90,2.45,1.80,2.30,2.65};
int[] yawA={17,244,62,181,315,104,273,31,151,220,84,342,196,53,287,129,11,236,72,303,167,39,258,94,329};
int[] yawB={193,28,279,113,347,65,218,159,5,296,137,330,87,202,48,266,122,357,74,183,315,99,231,13,284};
int[] typeA={0,1,1,2,1,2,0,1,2,1,0,1,2,1,2,0,1,2,1,1,2,0,1,2,1};
int[] typeB={1,0,2,1,2,1,0,2,1,1,2,0,1,2,1,2,0,1,2,1,1,2,0,1,2};
float worstGrassBaseGap=0,lowestGrassVisible=100;
void PlaceOnTerrain(UnityEngine.GameObject go,float x,float z,float burial,float minimumVisible,out float baseGap,out float visible){
 go.transform.position=new UnityEngine.Vector3(x,0,z); float offset=float.PositiveInfinity;
 foreach(var mf in go.GetComponentsInChildren<UnityEngine.MeshFilter>(true)){
  var verts=mf.sharedMesh.vertices; float meshLow=float.PositiveInfinity; foreach(var v in verts){float wy=mf.transform.TransformPoint(v).y;meshLow=System.Math.Min(meshLow,wy);}
  foreach(var v in verts){var q=mf.transform.TransformPoint(v);if(q.y<=meshLow+0.0001f){float surface=terrain.SampleHeight(new UnityEngine.Vector3(q.x,terrain.transform.position.y+5,q.z));offset=System.Math.Min(offset,surface-q.y);}}
 }
 if(float.IsInfinity(offset))throw new System.Exception("No transformed mesh base vertices: "+go.name);
 go.transform.position=new UnityEngine.Vector3(x,offset-burial,z);baseGap=0;visible=float.NegativeInfinity;
 foreach(var mf in go.GetComponentsInChildren<UnityEngine.MeshFilter>(true))foreach(var v in mf.sharedMesh.vertices){var q=mf.transform.TransformPoint(v);float surface=terrain.SampleHeight(new UnityEngine.Vector3(q.x,terrain.transform.position.y+5,q.z));visible=System.Math.Max(visible,q.y-surface);}
 foreach(var mf in go.GetComponentsInChildren<UnityEngine.MeshFilter>(true)){var verts=mf.sharedMesh.vertices;float meshLow=float.PositiveInfinity;foreach(var v in verts)meshLow=System.Math.Min(meshLow,mf.transform.TransformPoint(v).y);foreach(var v in verts){var q=mf.transform.TransformPoint(v);if(q.y<=meshLow+0.0001f){float surface=terrain.SampleHeight(new UnityEngine.Vector3(q.x,terrain.transform.position.y+5,q.z));baseGap=System.Math.Max(baseGap,q.y-surface);}}}
 if(visible<minimumVisible)throw new System.Exception("Roadside mesh buried or below visible-height threshold: "+go.name+" visible="+visible.ToString("G5",System.Globalization.CultureInfo.InvariantCulture));
}
int grassCount=0;
for(int side=0;side<2;side++)for(int i=0;i<25;i++){
 double st=side==0?stationsA[i]:stationsB[i],slope=RouteSlope(st),normal=System.Math.Sqrt(1+slope*slope),sign=side==0?1:-1,off=side==0?sideA[i]:sideB[i];
 float x=(float)(RouteX(st)+sign*off/normal),z=(float)(st-sign*slope*off/normal);
 int type=side==0?typeA[i]:typeB[i],yaw=side==0?yawA[i]:yawB[i];
 float scale=(float)(0.88+0.08*((i*7+side*3)%5));
 var go=(UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(grassPrefabs[type],scene); go.name="Grass_"+(side==0?"W":"E")+"_"+(i+1).ToString("00",System.Globalization.CultureInfo.InvariantCulture); go.transform.SetParent(grassRoot.transform,false); go.transform.localRotation=UnityEngine.Quaternion.Euler(0,yaw,0);go.transform.localScale=UnityEngine.Vector3.one*scale;
  float baseGap,visible;PlaceOnTerrain(go,x,z,0.005f,0.045f,out baseGap,out visible);worstGrassBaseGap=System.Math.Max(worstGrassBaseGap,baseGap);lowestGrassVisible=System.Math.Min(lowestGrassVisible,visible);
 if(go.GetComponentInChildren<UnityEngine.Collider>(true)!=null||go.GetComponentInChildren<UnityEngine.Rigidbody>(true)!=null)throw new System.Exception("Unexpected physics component on grass prefab");
 grassCount++;
}
double[] rockStations={10.58,11.98,13.17,14.30,15.72,17.07,18.05}; double[] rockOffsets={2.35,2.65,2.25,2.80,2.30,2.60,2.20}; int[] rockTypes={0,1,2,1,2,1,0}; int[] rockYaw={32,149,278,91,217,344,121}; float[] rockScale={1.05f,0.92f,0.86f,1.08f,0.84f,1.0f,1.08f};
int rockCount=0;
for(int i=0;i<rockStations.Length;i++){
 double st=rockStations[i],slope=RouteSlope(st),normal=System.Math.Sqrt(1+slope*slope),sign=(i%2==0)?1:-1,off=rockOffsets[i];
 float x=(float)(RouteX(st)+sign*off/normal),z=(float)(st-sign*slope*off/normal);var go=(UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(rockPrefabs[rockTypes[i]],scene);go.name="Rock_"+(i+1).ToString("00",System.Globalization.CultureInfo.InvariantCulture);go.transform.SetParent(rockRoot.transform,false);go.transform.localRotation=UnityEngine.Quaternion.Euler(0,rockYaw[i],0);go.transform.localScale=UnityEngine.Vector3.one*rockScale[i];float burial=(rockTypes[i]==0?0.02f:rockTypes[i]==1?0.07f:0.12f),baseGap,visible;PlaceOnTerrain(go,x,z,burial,0.025f,out baseGap,out visible);if(go.GetComponentInChildren<UnityEngine.Collider>(true)!=null||go.GetComponentInChildren<UnityEngine.Rigidbody>(true)!=null)throw new System.Exception("Unexpected physics component on rock prefab");rockCount++;
}
if(grassCount!=50||rockCount!=7)throw new System.Exception("Unexpected instance counts "+grassCount+" grass / "+rockCount+" rocks");
UnityEditor.EditorUtility.SetDirty(root); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,"Assets/Scenes/TerrainRoad_Lookdev.unity"))throw new System.Exception("Target scene save failed.");
return "saved target scene; Roadside_V1 grass="+grassCount+" rocks="+rockCount+" disabledLegacy=5; maxGrassBaseGap="+worstGrassBaseGap.ToString("G6",System.Globalization.CultureInfo.InvariantCulture)+"; minGrassVisibleHeight="+lowestGrassVisible.ToString("G6",System.Globalization.CultureInfo.InvariantCulture);
