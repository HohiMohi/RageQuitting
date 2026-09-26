((System.Action)(() => {
// Recovery only: restore the copied TD to the binary full-array checkpoint before
// reapplying after the paint-context orientation defect has been diagnosed.
const string dataPath="Assets/Art/Environment/TerrainRoadLookdev/TerrainData/TD_TerrainRoad_ReliefV2.asset";
const string baselinePath="Artifacts/TerrainRoadReliefV2/batch3b_baseline.bin";
const string sourcePath="Assets/Art/Environment/TerrainRoadLookdev/TerrainData/TD_TerrainRoad_Lookdev.asset";
const string roadPath="Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/TL_Road_OchreStone.terrainlayer";
const string stonePath="Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/TL_Stone_MutedGrey.terrainlayer";
const string tutorialPath="Assets/Scenes/Tutorial_scene.unity";
const string expectedSource="A4C15D4F711DC3D46C70A8B2A6EE086863729C83F3A4BC3FAA9094B3E0015777";
const string expectedRoad="0D45094A5136E74219A938BD7770D4E2A5BC7D0DBECF2EC66C814EFBA097B6E2";
const string expectedStone="5D75035FFFB47301D59EF89955F75C945C2D5E455D5898D22A98D285E37F4E02";
const string expectedTutorial="36F2C7FA09AB891850B410C5453CFC957A5C290D77672BA57DD3DBCEA017E6FB";
string Hash(string p){using(var s=System.Security.Cryptography.SHA256.Create())return System.BitConverter.ToString(s.ComputeHash(System.IO.File.ReadAllBytes(p))).Replace("-","");}
var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if(!scene.IsValid()||scene.path!="Assets/Scenes/TerrainRoad_Lookdev.unity"||UnityEngine.SceneManagement.SceneManager.sceneCount!=1||scene.isDirty)throw new System.Exception("Recovery requires the single clean target scene.");
if(Hash(sourcePath)!=expectedSource||Hash(roadPath)!=expectedRoad||Hash(stonePath)!=expectedStone||Hash(tutorialPath)!=expectedTutorial)throw new System.Exception("Protected hashes mismatch; recovery stopped.");
var terrain=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();var d=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
if(!terrain||terrain.terrainData!=d||!terrain.GetComponent<UnityEngine.TerrainCollider>()||terrain.GetComponent<UnityEngine.TerrainCollider>().terrainData!=d)throw new System.Exception("Scene references do not target only the approved copied TD.");
int hm,am,layers;float[,] h;float[,,] a;
using(var fs=System.IO.File.OpenRead(baselinePath))using(var r=new System.IO.BinaryReader(fs)){hm=r.ReadInt32();am=r.ReadInt32();layers=r.ReadInt32();h=new float[hm,hm];a=new float[am,am,layers];for(int y=0;y<hm;y++)for(int x=0;x<hm;x++)h[y,x]=r.ReadSingle();for(int y=0;y<am;y++)for(int x=0;x<am;x++)for(int l=0;l<layers;l++)a[y,x,l]=r.ReadSingle();}
if(hm!=d.heightmapResolution||am!=d.alphamapResolution||layers!=d.terrainLayers.Length)throw new System.Exception("Baseline array dimensions do not match copied TD.");
d.SetHeights(0,0,h);d.SetAlphamaps(0,0,a);UnityEditor.EditorUtility.SetDirty(d);UnityEditor.AssetDatabase.SaveAssets();
float[,] actualH=d.GetHeights(0,0,hm,hm);float[,,] actualA=d.GetAlphamaps(0,0,am,am);float maxH=0,maxA=0;
for(int y=0;y<hm;y++)for(int x=0;x<hm;x++)maxH=UnityEngine.Mathf.Max(maxH,UnityEngine.Mathf.Abs(actualH[y,x]-h[y,x]));
for(int y=0;y<am;y++)for(int x=0;x<am;x++)for(int l=0;l<layers;l++)maxA=UnityEngine.Mathf.Max(maxA,UnityEngine.Mathf.Abs(actualA[y,x,l]-a[y,x,l]));
string result="{\n\"recoveryOnly\":true,\n\"restoreMethod\":\"UnityEditor live Editor TerrainData.SetHeights/SetAlphamaps from batch3b_baseline.bin; recovery only\",\n\"maxHeightArrayDifferenceNormalized\":"+maxH.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\n\"maxAlphamapDifference\":"+maxA.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\n\"protectedHashesUnchanged\":true,\n\"bothSceneReferencesRemainCopiedTD\":true\n}\n";
if(maxH>0||maxA>0)throw new System.Exception("Recovery did not exactly match the saved baseline arrays: maxH="+maxH+" maxA="+maxA);
System.IO.File.WriteAllText("Artifacts/TerrainRoadReliefV2/batch3b_recovery_validation.json",result,new System.Text.UTF8Encoding(false));
UnityEditor.EditorPrefs.SetInt("RageQuitting.TerrainRoadReliefV2.Batch3B.Next",0);
Debug.Log("Batch3B recovery restored exact copied-TD baseline; no original assets/scenes were touched.");
}))();
