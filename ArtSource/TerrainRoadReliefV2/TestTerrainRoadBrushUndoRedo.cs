((System.Action)(() => {
const string scenePath="Assets/Scenes/TerrainRoad_Lookdev.unity";
const string tdPath="Assets/Art/Environment/TerrainRoadLookdev/TerrainData/TD_TerrainRoad_ReliefV2.asset";
const string stonePath="Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/TL_Stone_Grey_ReliefV2.terrainlayer";
const string earthPath="Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/TL_Earth_Dark_ReliefV2.terrainlayer";
const string maskPath="Assets/Art/Environment/TerrainRoadLookdev/SurfaceV2/RoadLimit_ReliefV2.png";
const string outputPath="Artifacts/TerrainRoadReliefV2/batch4a_undo_redo.json";
var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if(scene.path!=scenePath||scene.isDirty||UnityEngine.SceneManagement.SceneManager.sceneCount!=1||UnityEditor.EditorApplication.isPlaying)throw new System.Exception("Batch4A requires only the clean target scene in Edit Mode.");
var live=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(tdPath);if(!live)throw new System.Exception("Copied TerrainData missing.");
string HashFile(string p){using(var sha=System.Security.Cryptography.SHA256.Create())return System.BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(p))).Replace("-","");}
string sourceHash=HashFile(tdPath);
var clone=UnityEngine.Object.Instantiate(live);clone.name="__Batch4A_DisposableTerrainData";clone.hideFlags=UnityEngine.HideFlags.None;
var previewScene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var terrainGo=UnityEngine.Terrain.CreateTerrainGameObject(clone);terrainGo.name="__Batch4A_DisposableTerrain";UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(terrainGo,previewScene);
var terrain=terrainGo.GetComponent<UnityEngine.Terrain>();var collider=terrainGo.GetComponent<UnityEngine.TerrainCollider>();if(!terrain)throw new System.Exception("Temporary preview Terrain was not created.");if(!collider)collider=terrainGo.AddComponent<UnityEngine.TerrainCollider>();terrain.terrainData=clone;collider.terrainData=clone;
var window=(UnityEditor.EditorWindow)UnityEngine.ScriptableObject.CreateInstance(typeof(RageQuitting.EditorTools.TerrainRoadLookdev.TerrainRoadReliefBrush));
var type=window.GetType();void Set(string n,object v)=>type.GetField(n,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(window,v);
var stone=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(stonePath);var earth=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(earthPath);var mask=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(maskPath);
Set("targetTerrain",terrain);Set("stoneLayer",stone);Set("darkEarthLayer",earth);Set("roadLimitMask",mask);Set("brushSize",1.0f);Set("strength",0.9f);Set("scaleJitter",0f);Set("randomRotation",false);Set("rotationDegrees",17f);Set("random",new System.Random(4400));Set("stampIndex",0);Set("stoneLift",new UnityEngine.Vector2(.03f,.03f));Set("earthRecess",new UnityEngine.Vector2(.015f,.015f));
var setKind=type.GetNestedType("SurfaceKind",System.Reflection.BindingFlags.NonPublic);var begin=type.GetMethod("BeginStroke",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var end=type.GetMethod("EndStroke",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
float[,] CopyH()=>clone.GetHeights(0,0,clone.heightmapResolution,clone.heightmapResolution);float[,,] CopyA()=>clone.GetAlphamaps(0,0,clone.alphamapResolution,clone.alphamapResolution);
float Hdiff(float[,] a,float[,] b){float m=0;for(int y=0;y<a.GetLength(0);y++)for(int x=0;x<a.GetLength(1);x++)m=UnityEngine.Mathf.Max(m,UnityEngine.Mathf.Abs(a[y,x]-b[y,x]));return m;}
float Adiff(float[,,] a,float[,,] b){float m=0;for(int y=0;y<a.GetLength(0);y++)for(int x=0;x<a.GetLength(1);x++)for(int k=0;k<a.GetLength(2);k++)m=UnityEngine.Mathf.Max(m,UnityEngine.Mathf.Abs(a[y,x,k]-b[y,x,k]));return m;}
var errors=new System.Collections.Generic.List<string>();UnityEngine.Application.LogCallback handler=(condition,stack,logType)=>{if(logType==UnityEngine.LogType.Error||logType==UnityEngine.LogType.Exception||logType==UnityEngine.LogType.Assert)errors.Add(logType+":"+condition);};UnityEngine.Application.logMessageReceived+=handler;
var records=new System.Collections.Generic.List<string>();var center=new UnityEngine.Vector3(14.4f,0f,6.5f);
void Stroke(int kind,string label,UnityEngine.TerrainLayer layer){Set("surfaceKind",System.Enum.ToObject(setKind,kind));var beforeH=CopyH();var beforeA=CopyA();begin.Invoke(window,new object[]{center});end.Invoke(window,null);var appliedH=CopyH();var appliedA=CopyA();float applyHd=Hdiff(beforeH,appliedH),applyAd=Adiff(beforeA,appliedA);if(applyHd<=1e-7f||applyAd<=0f)throw new System.Exception(label+" brush did not change both height and layer alphamap on disposable Terrain.");UnityEditor.Undo.PerformUndo();var undoneH=CopyH();var undoneA=CopyA();float undoHd=Hdiff(beforeH,undoneH),undoAd=Adiff(beforeA,undoneA);if(undoHd>1e-7f||undoAd>1e-7f)throw new System.Exception(label+" Undo did not exactly restore disposable baseline: "+undoHd+","+undoAd);UnityEditor.Undo.PerformRedo();var redoneH=CopyH();var redoneA=CopyA();float redoHd=Hdiff(appliedH,redoneH),redoAd=Adiff(appliedA,redoneA);if(redoHd>1e-7f||redoAd>1e-7f)throw new System.Exception(label+" Redo did not exactly restore applied state: "+redoHd+","+redoAd);UnityEditor.Undo.PerformUndo();if(Hdiff(beforeH,CopyH())>1e-7f||Adiff(beforeA,CopyA())>1e-7f)throw new System.Exception(label+" final Undo failed to return clone to baseline.");records.Add("{\"surface\":\""+label+"\",\"heightMaxAppliedNormalized\":"+applyHd.ToString("G9",System.Globalization.CultureInfo.InvariantCulture)+",\"alphamapMaxApplied\":"+applyAd.ToString("G9",System.Globalization.CultureInfo.InvariantCulture)+",\"undoHeightMaxResidual\":"+undoHd.ToString("G9",System.Globalization.CultureInfo.InvariantCulture)+",\"undoAlphaMaxResidual\":"+undoAd.ToString("G9",System.Globalization.CultureInfo.InvariantCulture)+",\"redoHeightMaxResidual\":"+redoHd.ToString("G9",System.Globalization.CultureInfo.InvariantCulture)+",\"redoAlphaMaxResidual\":"+redoAd.ToString("G9",System.Globalization.CultureInfo.InvariantCulture)+"}");}
try {
Stroke(0,"stone",stone);Set("random",new System.Random(4401));Stroke(1,"dark-earth",earth);
string afterHash=HashFile(tdPath);if(afterHash!=sourceHash)throw new System.Exception("Source copied TerrainData asset changed during disposable Undo/Redo test.");if(errors.Count>0)throw new System.Exception("Undo/Redo brush test emitted console errors: "+string.Join(";",errors));
string json="{\n  \"passed\":true,\n  \"previewScene\":\"temporary Unity preview scene\",\n  \"strokes\":["+string.Join(",",records)+"],\n  \"undoRedoMaxResidualTolerance\":1e-7,\n  \"consoleErrorsExceptionsAsserts\":"+errors.Count+",\n  \"sourceTerrainDataHashUnchanged\":"+(sourceHash==afterHash?"true":"false")+",\n  \"activeSceneStill\":\""+UnityEngine.SceneManagement.SceneManager.GetActiveScene().path+"\",\n  \"activeSceneDirty\":"+(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty?"true":"false")+"\n}\n";
System.IO.File.WriteAllText(outputPath,json,new System.Text.UTF8Encoding(false));UnityEngine.Debug.Log("Batch4A disposable brush Undo/Redo test passed; report="+outputPath+".");
} catch(System.Exception exception) {
System.IO.File.WriteAllText(outputPath,"{\"passed\":false,\"error\":\""+exception.Message.Replace("\\","\\\\").Replace("\"","\\\"")+"\"}\n",new System.Text.UTF8Encoding(false));throw;
} finally {
UnityEngine.Application.logMessageReceived-=handler;
if(clone){UnityEditor.Undo.ClearUndo(clone);foreach(var tex in clone.alphamapTextures)if(tex)UnityEditor.Undo.ClearUndo(tex);}
if(terrainGo)UnityEditor.Undo.ClearUndo(terrainGo);
if(window)UnityEngine.Object.DestroyImmediate(window);
if(terrainGo)UnityEngine.Object.DestroyImmediate(terrainGo);
if(clone)UnityEngine.Object.DestroyImmediate(clone);
if(previewScene.IsValid()&&previewScene.isLoaded)UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);
}
}))();

