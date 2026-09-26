var palette="Cool";
var mode="Relief";
var rs=UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>(UnityEngine.FindObjectsSortMode.None);
UnityEngine.Renderer Find(string n){foreach(var r in rs)if(r.gameObject.name==n)return r;return null;}
var cam=UnityEngine.GameObject.Find("MainCamera").GetComponent<UnityEngine.Camera>();
cam.transform.position=new UnityEngine.Vector3(-12f,1.7f,-16f);
cam.transform.rotation=UnityEngine.Quaternion.LookRotation(new UnityEngine.Vector3(-15.3f,0f,-13.5f)-cam.transform.position,UnityEngine.Vector3.up);
var gm=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Art/Terrain/Prototype/Terrain_Grass_"+palette+"_"+mode+".mat");
var pm=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Art/Terrain/Prototype/Terrain_DirtPath_"+palette+"_"+mode+".mat");
var oldPreview=new System.Collections.Generic.List<UnityEngine.Material>();
foreach(var m in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Material>())if(m&&m.name.StartsWith("PreviewTerrain_"))oldPreview.Add(m);
var renderers=new[]{Find("Ground_West"),Find("West_Main"),Find("WestPath_0"),Find("WestPath_1")};
var output=new System.Collections.Generic.List<string>();
for(int i=0;i<renderers.Length;i++){var r=renderers[i];var src=i<2?gm:pm;var clone=new UnityEngine.Material(src);clone.name="PreviewTerrain_"+palette+"_"+mode+"_"+r.gameObject.name;var tile=new UnityEngine.Vector2(System.Math.Abs(r.transform.lossyScale.x)/4f,System.Math.Abs(r.transform.lossyScale.z)/4f);clone.SetTextureScale("_BaseMap",tile);if(clone.GetTexture("_BumpMap"))clone.SetTextureScale("_BumpMap",tile);r.sharedMaterial=clone;output.Add(r.gameObject.name+"=>"+clone.name+" tiles="+tile.ToString("F3"));}
foreach(var m in oldPreview)UnityEngine.Object.DestroyImmediate(m);
UnityEditor.EditorApplication.QueuePlayerLoopUpdate();UnityEditor.SceneView.RepaintAll();
return "camera="+cam.transform.position+" rotation="+cam.transform.eulerAngles+"\n"+string.Join("\n",output)+"\nsceneDirty="+UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty;


