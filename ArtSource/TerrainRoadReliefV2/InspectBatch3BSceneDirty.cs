((System.Action)(() => {
var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var terrain=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
var collider=terrain?terrain.GetComponent<UnityEngine.TerrainCollider>():null;
string PathOf(UnityEngine.Transform t){var parts=new System.Collections.Generic.List<string>();while(t){parts.Insert(0,t.name);t=t.parent;}return string.Join("/",parts);}
var lines=new System.Collections.Generic.List<string>();
void Visit(UnityEngine.Transform tr){var go=tr.gameObject;string p=PathOf(tr);lines.Add("GO|"+p+"|active="+go.activeSelf+"|layer="+go.layer+"|tag="+go.tag+"|pos="+tr.localPosition.ToString("F6")+"|rot="+tr.localRotation.eulerAngles.ToString("F6")+"|scale="+tr.localScale.ToString("F6"));foreach(UnityEngine.Component c in go.GetComponents<UnityEngine.Component>()){if(!c){lines.Add("MISSING|"+p);continue;}if(c==terrain||c==collider)continue;lines.Add("COMP|"+p+"|"+c.GetType().FullName+"|"+UnityEditor.EditorJsonUtility.ToJson(c));}for(int i=0;i<tr.childCount;i++)Visit(tr.GetChild(i));}
foreach(var go in scene.GetRootGameObjects())Visit(go.transform);
string snap=string.Join("\n",lines.OrderBy(x=>x,System.StringComparer.Ordinal));
string Hash(string s){using(var sha=System.Security.Cryptography.SHA256.Create())return System.BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s))).Replace("-","");}
string terrainSettings=terrain.name+"|"+terrain.drawInstanced+"|"+terrain.heightmapPixelError+"|"+terrain.basemapDistance+"|"+terrain.shadowCastingMode+"|"+(terrain.materialTemplate?UnityEditor.AssetDatabase.GetAssetPath(terrain.materialTemplate):"null")+"|"+terrain.transform.position+"|"+terrain.transform.rotation+"|"+terrain.transform.localScale;
string colliderSettings=collider.enabled+"|"+collider.isTrigger+"|"+collider.contactOffset+"|"+(collider.sharedMaterial?UnityEditor.AssetDatabase.GetAssetPath(collider.sharedMaterial):"null");
string result="scene="+scene.path+"|dirty="+scene.isDirty+"|sceneCount="+UnityEngine.SceneManagement.SceneManager.sceneCount+"|terrainObjDirty="+UnityEditor.EditorUtility.IsDirty(terrain)+"|colliderObjDirty="+UnityEditor.EditorUtility.IsDirty(collider)+"|terrainDataDirty="+UnityEditor.EditorUtility.IsDirty(terrain.terrainData)+"|snapshotHash="+Hash(snap)+"|terrain="+terrainSettings+"|collider="+colliderSettings;
System.IO.File.WriteAllText("Artifacts/TerrainRoadReliefV2/batch3b_dirty_scene_diagnostic.txt",result+"\n"+snap,new System.Text.UTF8Encoding(false));
Debug.Log(result);
}))();
