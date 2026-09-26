var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
const string expectedScene="Assets/Scenes/TerrainRoad_Lookdev.unity";
if(active.path!=expectedScene) throw new System.Exception("Active scene guard failed: "+active.path);
var terrain=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
if(terrain==null||terrain.terrainData==null) throw new System.Exception("Expected loaded Terrain/TerrainData missing.");
var data=terrain.terrainData;
var scenePath=UnityEditor.AssetDatabase.GetAssetPath(active.GetRootGameObjects().Length>0?active.GetRootGameObjects()[0]:null);
var heightData=data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution);
var alphaData=data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight);
var binPath="Artifacts/RoadsideV1/TerrainRoad_Lookdev_PreBatch2.bin";
using(var stream=new System.IO.FileStream(binPath,System.IO.FileMode.Create,System.IO.FileAccess.Write))
using(var writer=new System.IO.BinaryWriter(stream)) {
    writer.Write(data.heightmapResolution); writer.Write(data.alphamapWidth); writer.Write(data.alphamapHeight); writer.Write(data.alphamapLayers);
    for(int z=0;z<data.heightmapResolution;z++) for(int x=0;x<data.heightmapResolution;x++) writer.Write(heightData[z,x]);
    for(int z=0;z<data.alphamapHeight;z++) for(int x=0;x<data.alphamapWidth;x++) for(int layer=0;layer<data.alphamapLayers;layer++) writer.Write(alphaData[z,x,layer]);
}
var lines=new System.Text.StringBuilder();
lines.AppendLine("scene|"+active.path+"|active="+active.isLoaded+"|dirty="+UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty);
lines.AppendLine("terrain|"+terrain.name+"|position="+terrain.transform.position.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|size="+data.size.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|data="+UnityEditor.AssetDatabase.GetAssetPath(data)+"|heightResolution="+data.heightmapResolution+"|alphaResolution="+data.alphamapWidth+"x"+data.alphamapHeight+"|alphaLayers="+data.alphamapLayers+"|colliderEnabled="+terrain.GetComponent<UnityEngine.TerrainCollider>().enabled+"|colliderUsesSameData="+(terrain.GetComponent<UnityEngine.TerrainCollider>().terrainData==data));
lines.AppendLine("terrainMaterial|"+(terrain.materialTemplate?terrain.materialTemplate.shader.name:"null")+"|drawHeightmap="+terrain.drawHeightmap+"|drawTrees="+terrain.drawTreesAndFoliage);
for(int i=0;i<data.terrainLayers.Length;i++) {
    var l=data.terrainLayers[i];
    double sum=0;
    for(int z=0;z<data.alphamapHeight;z+=16) for(int x=0;x<data.alphamapWidth;x+=16) sum+=alphaData[z,x,i];
    lines.AppendLine("layer|"+i+"|"+l.name+"|asset="+UnityEditor.AssetDatabase.GetAssetPath(l)+"|diffuse="+(l.diffuseTexture?UnityEditor.AssetDatabase.GetAssetPath(l.diffuseTexture):"null")+"|normal="+(l.normalMapTexture?UnityEditor.AssetDatabase.GetAssetPath(l.normalMapTexture):"null")+"|mask="+(l.maskMapTexture?UnityEditor.AssetDatabase.GetAssetPath(l.maskMapTexture):"null")+"|tile="+l.tileSize.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|offset="+l.tileOffset.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|sampleMean="+(sum/(System.Math.Ceiling(data.alphamapHeight/16.0)*System.Math.Ceiling(data.alphamapWidth/16.0))).ToString("G9",System.Globalization.CultureInfo.InvariantCulture));
}
string Components(UnityEngine.GameObject go){var c=go.GetComponents<UnityEngine.Component>();var b=new System.Text.StringBuilder();for(int i=0;i<c.Length;i++){if(i>0)b.Append(',');b.Append(c[i]==null?"null":c[i].GetType().Name);}return b.ToString();}
void Walk(UnityEngine.Transform t,string parent) {
    string path=parent.Length==0?t.name:parent+"/"+t.name;
    var p=t.position;
    lines.AppendLine("object|"+path+"|active="+t.gameObject.activeSelf+"|world="+p.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|components="+Components(t.gameObject));
    for(int i=0;i<t.childCount;i++) Walk(t.GetChild(i),path);
}
foreach(var root in active.GetRootGameObjects()) Walk(root.transform,"");
string sceneHash;
using(var sha=System.Security.Cryptography.SHA256.Create()) sceneHash=System.BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(expectedScene))).Replace("-","");
System.IO.File.WriteAllText("Artifacts/RoadsideV1/SceneBaseline_Batch2.txt",lines.ToString()+"sceneSHA256|"+sceneHash+"\n",new System.Text.UTF8Encoding(false));
var summary="{\"scene\":\""+active.path+"\",\"terrainData\":\""+UnityEditor.AssetDatabase.GetAssetPath(data)+"\",\"heightResolution\":"+data.heightmapResolution+",\"alphaResolution\":["+data.alphamapWidth+","+data.alphamapHeight+"],\"alphaLayers\":"+data.alphamapLayers+",\"roots\":"+active.GetRootGameObjects().Length+",\"baselineBinary\":\""+binPath+"\",\"baselineText\":\"Artifacts/RoadsideV1/SceneBaseline_Batch2.txt\",\"sceneSHA256\":\""+sceneHash+"\"}";
System.IO.File.WriteAllText("Artifacts/RoadsideV1/SceneBaseline_Batch2.json",summary+"\n",new System.Text.UTF8Encoding(false));
return summary;
