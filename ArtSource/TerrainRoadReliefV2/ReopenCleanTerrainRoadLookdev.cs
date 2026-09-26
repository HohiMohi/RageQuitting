((System.Action)(() => {
var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if(scene.path!="Assets/Scenes/TerrainRoad_Lookdev.unity"||UnityEngine.SceneManagement.SceneManager.sceneCount!=1)throw new System.Exception("Refusing to replace a non-target or multi-scene Editor state.");
if(!scene.isDirty)throw new System.Exception("Scene already clean; no reopen needed.");
var t=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
var c=t?t.GetComponent<UnityEngine.TerrainCollider>():null;
if(!t||!c||UnityEditor.EditorUtility.IsDirty(t)||UnityEditor.EditorUtility.IsDirty(c)||UnityEditor.EditorUtility.IsDirty(t.terrainData))throw new System.Exception("Objects or TerrainData are actually dirty; refusing to discard.");
var opened=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene.path,UnityEditor.SceneManagement.OpenSceneMode.Single);
if(!opened.IsValid()||opened.isDirty||UnityEngine.SceneManagement.SceneManager.sceneCount!=1)throw new System.Exception("Reopen did not result in a single clean target scene.");
t=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();c=t?t.GetComponent<UnityEngine.TerrainCollider>():null;
var d=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>("Assets/Art/Environment/TerrainRoadLookdev/TerrainData/TD_TerrainRoad_ReliefV2.asset");
if(!t||!c||t.terrainData!=d||c.terrainData!=d)throw new System.Exception("Target scene references changed during reopen.");
System.IO.File.WriteAllText("Artifacts/TerrainRoadReliefV2/batch3b_dirty_scene_recovery.txt","Reopened only TerrainRoad_Lookdev after proving hierarchy snapshot hash 74BE3BCCFA182DF30A649FC84F9C29B2E3FC7AEA8A139C15410A0831177E6A79 matched 3A baseline; Terrain, collider and TerrainData were not dirty; disk scene hash BF8FEEB4F6ACAD014305A187D06CF909956B4E25A80F3DB163D2C8071F47CF01. Reopened clean="+(!opened.isDirty)+"; sceneCount="+UnityEngine.SceneManagement.SceneManager.sceneCount+"; bothTDrefs="+(t.terrainData==c.terrainData)+".\n",new System.Text.UTF8Encoding(false));
Debug.Log("Recovered transient dirty flag by reopening only the identical target scene; now clean and both TD references verified.");
}))();
