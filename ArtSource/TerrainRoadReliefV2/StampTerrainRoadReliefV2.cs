((System.Action)(() => {
// Re-run this eval_file once per checkpoint. It executes the existing brush through
// BeginStroke/EndStroke; each invocation applies at most four real brush strokes.
const string scenePath = "Assets/Scenes/TerrainRoad_Lookdev.unity";
const string dataPath = "Assets/Art/Environment/TerrainRoadLookdev/TerrainData/TD_TerrainRoad_ReliefV2.asset";
const string sourceDataPath = "Assets/Art/Environment/TerrainRoadLookdev/TerrainData/TD_TerrainRoad_Lookdev.asset";
const string roadLayerPath = "Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/TL_Road_OchreStone.terrainlayer";
const string oldStonePath = "Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/TL_Stone_MutedGrey.terrainlayer";
const string grassPath = "Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/TL_Grass_Warm.terrainlayer";
const string stoneLayerPath = "Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/TL_Stone_Grey_ReliefV2.terrainlayer";
const string earthLayerPath = "Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/TL_Earth_Dark_ReliefV2.terrainlayer";
const string maskPath = "Assets/Art/Environment/TerrainRoadLookdev/SurfaceV2/RoadLimit_ReliefV2.png";
const string baselinePath = "Artifacts/TerrainRoadReliefV2/batch3b_fixeduv_baseline.bin";
const string snapshotPath = "Artifacts/TerrainRoadReliefV2/batch3b_fixeduv_scene_snapshot.txt";
const string ledgerPath = "Artifacts/TerrainRoadReliefV2/batch3b_fixeduv_stamp_ledger.jsonl";
const string dataReportPath = "Artifacts/TerrainRoadReliefV2/batch3b_data_validation.json";
const string reportPath = "Artifacts/TerrainRoadReliefV2/batch3b_stamps_validation.md";
const string prefKey = "RageQuitting.TerrainRoadReliefV2.Batch3B.FixedUv.Next";
const string expectedSourceDataHash = "A4C15D4F711DC3D46C70A8B2A6EE086863729C83F3A4BC3FAA9094B3E0015777";
const string expectedOldRoadHash = "0D45094A5136E74219A938BD7770D4E2A5BC7D0DBECF2EC66C814EFBA097B6E2";
const string expectedOldStoneHash = "5D75035FFFB47301D59EF89955F75C945C2D5E455D5898D22A98D285E37F4E02";
const string expectedTutorialHash = "36F2C7FA09AB891850B410C5453CFC957A5C290D77672BA57DD3DBCEA017E6FB";
System.IO.Directory.CreateDirectory("Artifacts/TerrainRoadReliefV2");
string HashFile(string path) { using (var sha = System.Security.Cryptography.SHA256.Create()) return System.BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", ""); }
string HashText(string text) { using (var sha = System.Security.Cryptography.SHA256.Create()) return System.BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", ""); }
void VerifyOriginalHashes() {
    if (HashFile(sourceDataPath) != expectedSourceDataHash || HashFile(roadLayerPath) != expectedOldRoadHash || HashFile(oldStonePath) != expectedOldStoneHash || HashFile("Assets/Scenes/Tutorial_scene.unity") != expectedTutorialHash)
        throw new System.Exception("Protected original asset or Tutorial scene hash changed; aborting.");
}
void VerifyCleanScene() {
    var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
    if (!scene.IsValid() || scene.path != scenePath || UnityEngine.SceneManagement.SceneManager.sceneCount != 1 || scene.isDirty)
        throw new System.Exception("Expected the single, clean TerrainRoad_Lookdev scene; refusing to save/close another or dirty scene.");
}
UnityEngine.Terrain FindTerrain() {
    var terrain = UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
    var expected = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
    var collider = terrain ? terrain.GetComponent<UnityEngine.TerrainCollider>() : null;
    if (!terrain || terrain.terrainData != expected || !collider || collider.terrainData != expected)
        throw new System.Exception("Terrain and TerrainCollider must both reference the copied ReliefV2 TerrainData.");
    return terrain;
}
string SceneSnapshot(UnityEngine.SceneManagement.Scene scene, UnityEngine.Terrain terrain) {
    var lines = new System.Collections.Generic.List<string>();
    string PathOf(UnityEngine.Transform t) { var parts = new System.Collections.Generic.List<string>(); while (t) { parts.Insert(0, t.name); t = t.parent; } return string.Join("/", parts); }
    void Visit(UnityEngine.Transform tr) {
        var go = tr.gameObject; string p = PathOf(tr);
        lines.Add("GO|" + p + "|active=" + go.activeSelf + "|layer=" + go.layer + "|tag=" + go.tag + "|pos=" + tr.localPosition.ToString("F6") + "|rot=" + tr.localRotation.eulerAngles.ToString("F6") + "|scale=" + tr.localScale.ToString("F6"));
        foreach (UnityEngine.Component c in go.GetComponents<UnityEngine.Component>()) {
            if (!c) { lines.Add("MISSING|" + p); continue; }
            if (c == terrain || c == terrain.GetComponent<UnityEngine.TerrainCollider>()) continue;
            lines.Add("COMP|" + p + "|" + c.GetType().FullName + "|" + UnityEditor.EditorJsonUtility.ToJson(c));
        }
        for (int i = 0; i < tr.childCount; i++) Visit(tr.GetChild(i));
    }
    foreach (var go in scene.GetRootGameObjects()) Visit(go.transform);
    return string.Join("\n", lines.OrderBy(x => x, System.StringComparer.Ordinal));
}
string TerrainSettings(UnityEngine.Terrain t) { return t.name + "|" + t.drawInstanced + "|" + t.heightmapPixelError + "|" + t.basemapDistance + "|" + t.shadowCastingMode + "|" + (t.materialTemplate ? UnityEditor.AssetDatabase.GetAssetPath(t.materialTemplate) : "null") + "|" + t.transform.position + "|" + t.transform.rotation + "|" + t.transform.localScale; }
string ColliderSettings(UnityEngine.TerrainCollider c) { return c.enabled + "|" + c.isTrigger + "|" + c.contactOffset + "|" + (c.sharedMaterial ? UnityEditor.AssetDatabase.GetAssetPath(c.sharedMaterial) : "null"); }
var path = new UnityEngine.Vector2[] { new UnityEngine.Vector2(14f,3f), new UnityEngine.Vector2(14.4f,6.5f), new UnityEngine.Vector2(15.5f,10f), new UnityEngine.Vector2(17f,13f), new UnityEngine.Vector2(17.6f,16.8f), new UnityEngine.Vector2(17.1f,20f), new UnityEngine.Vector2(16.3f,24f), new UnityEngine.Vector2(16f,28f) };
var candidateRows = new System.Collections.Generic.List<string>();
var candidates = new System.Collections.Generic.List<(UnityEngine.Vector2 pos, bool earth, float size, float liftOrRecess, float angle, int mask, float strength)>();
var terrainForPlan = UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
var roadMask = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(maskPath);
if (!terrainForPlan || !roadMask || !roadMask.isReadable) throw new System.Exception("Terrain/mask is missing or not readable.");
bool Allowed(float x, float z) => x >= 0 && z >= 0 && x <= terrainForPlan.terrainData.size.x && z <= terrainForPlan.terrainData.size.z && roadMask.GetPixelBilinear(x / terrainForPlan.terrainData.size.x, z / terrainForPlan.terrainData.size.z).r >= .5f;
float totalLength = 0f; for (int i = 1; i < path.Length; i++) totalLength += UnityEngine.Vector2.Distance(path[i-1], path[i]);
var stations = new System.Collections.Generic.List<(UnityEngine.Vector2 p, UnityEngine.Vector2 tangent)>();
for (float d = 0f, step = 0f; d <= totalLength + 0.001f; d += 1.05f, step += 1f) {
    float remain = d; int seg = 1;
    while (seg < path.Length - 1 && remain > UnityEngine.Vector2.Distance(path[seg-1], path[seg])) { remain -= UnityEngine.Vector2.Distance(path[seg-1], path[seg]); seg++; }
    UnityEngine.Vector2 a = path[seg-1], b = path[seg], tangent = (b-a).normalized;
    float len = UnityEngine.Vector2.Distance(a,b); UnityEngine.Vector2 p = UnityEngine.Vector2.Lerp(a,b,len <= 0 ? 0 : remain / len);
    stations.Add((p,tangent));
}
for (int i = 0; i < stations.Count; i++) {
    var (p,t) = stations[i]; UnityEngine.Vector2 normal = new UnityEngine.Vector2(-t.y,t.x);
    for (int sideIndex = 0; sideIndex < 2; sideIndex++) {
        int side = sideIndex == 0 ? -1 : 1; UnityEngine.Vector2 pos = p + normal * (side * (i % 4 == 0 ? .78f : .94f));
        if (!Allowed(pos.x,pos.y)) continue;
        bool earth = ((i + sideIndex) % 3 == 0);
        float size = new float[] { .88f,1.16f,1.38f,1.02f,.81f,1.27f }[(i * 2 + sideIndex) % 6];
        float amount = earth ? new float[] { .012f,.018f,.015f,.02f }[(i + sideIndex) % 4] : new float[] { .022f,.031f,.038f,.027f,.034f }[(i * 2 + sideIndex) % 5];
        float angle = (i * 41 + sideIndex * 67) % 360;
        int stamp = (i + sideIndex * 2) % 3;
        float strength = (i % 6 == 0) ? .7f : .9f;
        candidates.Add((pos,earth,size,amount,angle,stamp,strength));
        candidateRows.Add("{\"index\":" + (candidates.Count-1) + ",\"kind\":\"" + (earth ? "dark-earth" : "stone") + "\",\"x\":" + pos.x.ToString("F3",System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + pos.y.ToString("F3",System.Globalization.CultureInfo.InvariantCulture) + ",\"sizeM\":" + size.ToString("F2",System.Globalization.CultureInfo.InvariantCulture) + ",\"requestedHeightM\":" + (earth ? -amount : amount).ToString("F3",System.Globalization.CultureInfo.InvariantCulture) + ",\"rotationDeg\":" + angle + ",\"stamp\":" + stamp + ",\"mask\":" + roadMask.GetPixelBilinear(pos.x/terrainForPlan.terrainData.size.x,pos.y/terrainForPlan.terrainData.size.z).r.ToString("F3",System.Globalization.CultureInfo.InvariantCulture) + "}");
    }
    if (i % 4 == 1 && Allowed(p.x,p.y)) {
        float size = .76f, amount = .022f;
        candidates.Add((p,false,size,amount,(i*53)%360,(i+1)%3,.44f));
        candidateRows.Add("{\"index\":" + (candidates.Count-1) + ",\"kind\":\"stone-chip-centre\",\"x\":" + p.x.ToString("F3",System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + p.y.ToString("F3",System.Globalization.CultureInfo.InvariantCulture) + ",\"sizeM\":0.76,\"requestedHeightM\":0.022,\"rotationDeg\":" + ((i*53)%360) + ",\"stamp\":" + ((i+1)%3) + ",\"strength\":0.44,\"mask\":" + roadMask.GetPixelBilinear(p.x/terrainForPlan.terrainData.size.x,p.y/terrainForPlan.terrainData.size.z).r.ToString("F3",System.Globalization.CultureInfo.InvariantCulture) + "}");
    }
}
int next = UnityEditor.EditorPrefs.GetInt(prefKey, -1);
if (next == -1) {
    VerifyCleanScene(); VerifyOriginalHashes(); var t = FindTerrain(); var d = t.terrainData; var c = t.GetComponent<UnityEngine.TerrainCollider>();
    if (d.terrainLayers.Length != 4 || d.terrainLayers[0] != UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(grassPath) || d.terrainLayers[1] != UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>("Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/TL_Road_ReliefV2.terrainlayer") || d.terrainLayers[2] != UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(stoneLayerPath) || d.terrainLayers[3] != UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(earthLayerPath)) throw new System.Exception("ReliefV2 TD layer setup mismatch.");
    if (System.IO.File.Exists(baselinePath) || System.IO.File.Exists(snapshotPath) || System.IO.File.Exists(ledgerPath)) throw new System.Exception("Batch3B checkpoint files already exist; inspect them rather than overwriting.");
    float[,] heights = d.GetHeights(0,0,d.heightmapResolution,d.heightmapResolution); float[,,] alpha = d.GetAlphamaps(0,0,d.alphamapResolution,d.alphamapResolution);
    using (var fs = System.IO.File.Create(baselinePath)) using (var w = new System.IO.BinaryWriter(fs)) {
        w.Write(d.heightmapResolution); w.Write(d.alphamapResolution); w.Write(d.terrainLayers.Length);
        for (int y=0;y<d.heightmapResolution;y++) for(int x=0;x<d.heightmapResolution;x++) w.Write(heights[y,x]);
        for (int y=0;y<d.alphamapResolution;y++) for(int x=0;x<d.alphamapResolution;x++) for(int l=0;l<d.terrainLayers.Length;l++) w.Write(alpha[y,x,l]);
    }
    string sceneSnapshot=SceneSnapshot(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),t);
    System.IO.File.WriteAllText(snapshotPath,"terrain="+TerrainSettings(t)+"\ncollider="+ColliderSettings(c)+"\nsnapshotHash="+HashText(sceneSnapshot)+"\n",new System.Text.UTF8Encoding(false));
    System.IO.File.WriteAllText("Artifacts/TerrainRoadReliefV2/batch3b_plan_fixeduv.json","{\n\"routeLengthM\":"+totalLength.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+",\n\"stations\":"+stations.Count+",\n\"acceptedStampCentres\":[\n"+string.Join(",\n",candidateRows)+"\n]}\n",new System.Text.UTF8Encoding(false));
    UnityEditor.EditorPrefs.SetInt(prefKey,0);
    Debug.Log("Batch3B baseline ready: " + candidates.Count + " permitted brush centres along " + totalLength.ToString("F2") + " m route; stone/earth counts="+candidates.Count(x=>!x.earth)+"/"+candidates.Count(x=>x.earth)+"; baseline="+HashFile(baselinePath)+"; scene clean, both TD refs verified, protected hashes unchanged.");
    return;
}
if (next >= 0 && next < candidates.Count) {
    VerifyCleanScene(); VerifyOriginalHashes(); var t = FindTerrain(); var d=t.terrainData;
    var window=UnityEditor.EditorWindow.GetWindow<RageQuitting.EditorTools.TerrainRoadLookdev.TerrainRoadReliefBrush>("Road Relief");
    var type=typeof(RageQuitting.EditorTools.TerrainRoadLookdev.TerrainRoadReliefBrush);
    void Set(string name,object value) => type.GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(window,value);
    var stone=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(stoneLayerPath); var earthLayer=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(earthLayerPath);
    int end=System.Math.Min(next+4,candidates.Count);
    UnityEditor.EditorPrefs.SetInt(prefKey,-2-next); // in-progress checkpoint; inspect live terrain before retry if interrupted
    var logs=new System.Collections.Generic.List<string>();
    var begin=type.GetMethod("BeginStroke",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    var finish=type.GetMethod("EndStroke",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    for(int i=next;i<end;i++) {
        var op=candidates[i];
        Set("targetTerrain",t); Set("stoneLayer",stone); Set("darkEarthLayer",earthLayer); Set("roadLimitMask",roadMask);
        Set("surfaceKind",System.Enum.ToObject(type.GetNestedType("SurfaceKind",System.Reflection.BindingFlags.NonPublic),op.earth?1:0));
        Set("stampIndex",op.mask); Set("paintingEnabled",true); Set("brushSize",op.size); Set("strength",op.strength); Set("rotationDegrees",op.angle); Set("scaleJitter",0f); Set("randomRotation",false);
        Set("stoneLift",new UnityEngine.Vector2(op.liftOrRecess,op.liftOrRecess)); Set("earthRecess",new UnityEngine.Vector2(op.liftOrRecess,op.liftOrRecess)); Set("random",new System.Random(42000+i));
        UnityEngine.Vector2 world=op.pos; UnityEngine.Vector3 local=new UnityEngine.Vector3(world.x,0,world.y);
        int hm=d.heightmapResolution, am=d.alphamapResolution; float unitY=d.size.y;
        int hx=UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(world.x/d.size.x*(hm-1)),0,hm-1), hy=UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(world.y/d.size.z*(hm-1)),0,hm-1);
        int ax=UnityEngine.Mathf.Clamp((int)(world.x/d.size.x*am),0,am-1), ay=UnityEngine.Mathf.Clamp((int)(world.y/d.size.z*am),0,am-1);
        float beforeH=d.GetHeights(hx,hy,1,1)[0,0]*unitY; float beforeA=d.GetAlphamaps(ax,ay,1,1)[0,0,op.earth?3:2];
        begin.Invoke(window,new object[]{local}); finish.Invoke(window,null);
        float afterH=d.GetHeights(hx,hy,1,1)[0,0]*unitY; float afterA=d.GetAlphamaps(ax,ay,1,1)[0,0,op.earth?3:2];
        float expected=op.earth?-op.liftOrRecess:op.liftOrRecess; float delta=afterH-beforeH;
        logs.Add("{\"index\":"+i+",\"surface\":\""+(op.earth?"dark-earth":"stone")+"\",\"x\":"+world.x.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+",\"z\":"+world.y.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+",\"requestedHeightDeltaM\":"+expected.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+",\"measuredHeightDeltaM\":"+delta.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+",\"heightBeforeM\":"+beforeH.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+",\"heightAfterM\":"+afterH.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+",\"targetLayerWeightBefore\":"+beforeA.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+",\"targetLayerWeightAfter\":"+afterA.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+",\"brushSizeM\":"+op.size.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+",\"rotationDeg\":"+op.angle+",\"stampIndex\":"+op.mask+",\"strength\":"+op.strength.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"}");
        if (UnityEngine.Mathf.Abs(delta)<0.00001f || afterA<=beforeA) throw new System.Exception("Actual brush stroke failed to change coupled height and surface weights at stamp index "+i+" (delta="+delta+", alpha="+beforeA+" -> "+afterA+"). Inspect busy checkpoint before continuing.");
    }
    UnityEditor.EditorUtility.SetDirty(d); UnityEditor.AssetDatabase.SaveAssets();
    System.IO.File.AppendAllText(ledgerPath,string.Join("\n",logs)+"\n",new System.Text.UTF8Encoding(false));
    UnityEditor.EditorPrefs.SetInt(prefKey,end);
    Debug.Log("Batch3B brush checkpoint "+next+".."+(end-1)+" applied through BeginStroke/EndStroke; saved TerrainData. Next="+end+"/"+candidates.Count+"; records appended.");
    return;
}
if (next == candidates.Count) {
    VerifyCleanScene(); VerifyOriginalHashes();
    var beforeSaveScene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
    if(beforeSaveScene.isDirty && !UnityEditor.SceneManagement.EditorSceneManager.SaveScene(beforeSaveScene)) throw new System.Exception("Could not save the only approved target scene before verification.");
    UnityEditor.AssetDatabase.SaveAssets();
    var reopened=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,UnityEditor.SceneManagement.OpenSceneMode.Single);
    if(!reopened.IsValid()||reopened.isDirty||UnityEngine.SceneManagement.SceneManager.sceneCount!=1) throw new System.Exception("Saved target scene could not be reopened cleanly.");
    var t=FindTerrain(); var d=t.terrainData; var c=t.GetComponent<UnityEngine.TerrainCollider>();
    float[,] oldH; float[,,] oldA; int hm,am,layerCount;
    using(var fs=System.IO.File.OpenRead(baselinePath)) using(var r=new System.IO.BinaryReader(fs)) {
        hm=r.ReadInt32(); am=r.ReadInt32(); layerCount=r.ReadInt32(); oldH=new float[hm,hm]; oldA=new float[am,am,layerCount];
        for(int y=0;y<hm;y++) for(int x=0;x<hm;x++) oldH[y,x]=r.ReadSingle();
        for(int y=0;y<am;y++) for(int x=0;x<am;x++) for(int l=0;l<layerCount;l++) oldA[y,x,l]=r.ReadSingle();
    }
    float[,] newH=d.GetHeights(0,0,hm,hm); float[,,] newA=d.GetAlphamaps(0,0,am,am);
    float maxOutsideHeight=0f,maxOutsideAlpha=0f,maxInsideDelta=0f,maxCenterlineDelta=0f,maxRoadErr=0f,maxStoneInitial=0f,maxEarthInitial=0f; int changedHeights=0,changedAlphas=0,stoneRaised=0,earthLowered=0; float minStone=10f,maxStone=0f,minEarth=0f,maxEarth=-10f; int outsideHx=-1,outsideHy=-1,outsideAx=-1,outsideAy=-1; float outsideHMask=0f,outsideAMask=0f,outsideHDelta=0f,outsideADelta=0f;
    for(int y=0;y<hm;y++) for(int x=0;x<hm;x++) {
        float tx=x/(float)(hm-1)*d.size.x,tz=y/(float)(hm-1)*d.size.z, delta=(newH[y,x]-oldH[y,x])*d.size.y;
        float mask=roadMask.GetPixelBilinear(tx/d.size.x,tz/d.size.z).r;
        if(mask<.5f) { if(UnityEngine.Mathf.Abs(delta)>maxOutsideHeight){maxOutsideHeight=UnityEngine.Mathf.Abs(delta);outsideHx=x;outsideHy=y;outsideHMask=mask;outsideHDelta=delta;} } else maxInsideDelta=UnityEngine.Mathf.Max(maxInsideDelta,UnityEngine.Mathf.Abs(delta));
        if(UnityEngine.Mathf.Abs(delta)>0.00001f) changedHeights++;
    }
    for(int y=0;y<am;y++) for(int x=0;x<am;x++) {
        float tx=(x+.5f)/(float)am*d.size.x,tz=(y+.5f)/(float)am*d.size.z,mask=roadMask.GetPixelBilinear(tx/d.size.x,tz/d.size.z).r;
        for(int l=0;l<layerCount;l++) {
            float delta=newA[y,x,l]-oldA[y,x,l];
            if(mask<.5f && UnityEngine.Mathf.Abs(delta)>maxOutsideAlpha) { maxOutsideAlpha=UnityEngine.Mathf.Abs(delta);outsideAx=x;outsideAy=y;outsideAMask=mask;outsideADelta=delta; }
            if(UnityEngine.Mathf.Abs(delta)>1f/255f+.00001f) changedAlphas++;
        }
        if(newA[y,x,2]>oldA[y,x,2]+.01f) stoneRaised++;
        if(newA[y,x,3]>oldA[y,x,3]+.01f) earthLowered++;
        maxRoadErr=UnityEngine.Mathf.Max(maxRoadErr,UnityEngine.Mathf.Abs(newA[y,x,1]-oldA[y,x,1]));
        maxStoneInitial=UnityEngine.Mathf.Max(maxStoneInitial,oldA[y,x,2]); maxEarthInitial=UnityEngine.Mathf.Max(maxEarthInitial,oldA[y,x,3]);
    }
    foreach(var wp in path) { int x=UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(wp.x/d.size.x*(hm-1)),0,hm-1),y=UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(wp.y/d.size.z*(hm-1)),0,hm-1); float delta=(newH[y,x]-oldH[y,x])*d.size.y; maxCenterlineDelta=UnityEngine.Mathf.Max(maxCenterlineDelta,UnityEngine.Mathf.Abs(delta)); }
    int stoneCenterPositive=0,earthCenterNegative=0;
    foreach(string row in System.IO.File.ReadAllLines(ledgerPath)) { var j=Newtonsoft.Json.Linq.JObject.Parse(row); float delta=(float)j["measuredHeightDeltaM"]; if((string)j["surface"]=="stone"&&delta>0){stoneCenterPositive++;minStone=UnityEngine.Mathf.Min(minStone,delta);maxStone=UnityEngine.Mathf.Max(maxStone,delta);} if((string)j["surface"]=="dark-earth"&&delta<0){earthCenterNegative++;minEarth=UnityEngine.Mathf.Min(minEarth,delta);maxEarth=UnityEngine.Mathf.Max(maxEarth,delta);} }
    if(maxOutsideHeight>0.00002f||maxOutsideAlpha>0.005f||maxCenterlineDelta>0.02f||stoneCenterPositive==0||earthCenterNegative==0||maxStone>0.045f||minEarth<-.025f) throw new System.Exception("Final relief validation failed: outsideHeight="+maxOutsideHeight+" at hm("+outsideHx+","+outsideHy+") mask="+outsideHMask+" signed="+outsideHDelta+"; outsideAlpha="+maxOutsideAlpha+" at am("+outsideAx+","+outsideAy+") mask="+outsideAMask+" signed="+outsideADelta+"; centerline="+maxCenterlineDelta+" stoneCenters="+stoneCenterPositive+" earthCenters="+earthCenterNegative+" stoneRange="+minStone+".."+maxStone+" earthRange="+minEarth+".."+maxEarth+". See batch3b data and ledger.");
    string sceneSnap=SceneSnapshot(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),t); string[] before=System.IO.File.ReadAllLines(snapshotPath);
    if(TerrainSettings(t)!=before[0].Substring("terrain=".Length)||ColliderSettings(c)!=before[1].Substring("collider=".Length)||HashText(sceneSnap)!=before[2].Substring("snapshotHash=".Length)) throw new System.Exception("Scene objects or non-data Terrain/collider settings changed during stamping.");
    string json="{\n  \"passed\":true,\n  \"roadMask\":\"fixed RoadLimit_ReliefV2; 256x256; Clamp; sampled bottom-left UV\",\n  \"stamps\":"+System.IO.File.ReadAllLines(ledgerPath).Length+",\n  \"maxOutsideHeightDeltaM\":"+maxOutsideHeight.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\n  \"maxOutsideAlphamapWeightDelta\":"+maxOutsideAlpha.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\n  \"maxInsideHeightDeltaM\":"+maxInsideDelta.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\n  \"maxCenterlineDeltaM\":"+maxCenterlineDelta.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\n  \"changedHeightSamples\":"+changedHeights+",\n  \"changedAlphamapSamples\":"+changedAlphas+",\n  \"stoneRaisedWeightSamples\":"+stoneRaised+",\n  \"earthRaisedWeightSamples\":"+earthLowered+",\n  \"stoneStrokesWithPositiveMeasuredCentreDelta\":"+stoneCenterPositive+",\n  \"earthStrokesWithNegativeMeasuredCentreDelta\":"+earthCenterNegative+",\n  \"maxRoadLayerWeightDifferenceInPaintedArea\":"+maxRoadErr.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\n  \"initialStoneMax\":"+maxStoneInitial.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\n  \"initialDarkEarthMax\":"+maxEarthInitial.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\n  \"stoneMeasuredCenterDeltaRangeM\":["+minStone.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+","+maxStone.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+"],\n  \"earthMeasuredCenterDeltaRangeM\":["+minEarth.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+","+maxEarth.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+"],\n  \"terrainData\":\""+dataPath+"\",\n  \"terrainAndColliderUseSameData\":"+(t.terrainData==c.terrainData?"true":"false")+",\n  \"originalDataHash\":\""+HashFile(sourceDataPath)+"\",\n  \"oldRoadLayerHash\":\""+HashFile(roadLayerPath)+"\",\n  \"oldStoneLayerHash\":\""+HashFile(oldStonePath)+"\",\n  \"tutorialSceneHash\":\""+HashFile("Assets/Scenes/Tutorial_scene.unity")+"\"\n}\n";
    System.IO.File.WriteAllText(dataReportPath,json,new System.Text.UTF8Encoding(false));
    string report="# Terrain Road Relief V2 — Batch 3B Stamps\n\n- Existing TerrainRoadReliefBrush applied the stamps via its private `BeginStroke` and `EndStroke` workflow (which invokes `StampAt` and `TerrainPaintUtility` for height and selected single-layer painting). No direct height/alphamap writes were used for final strokes; direct SetHeights/SetAlphamaps were used only to recover the copied TD from the failed attempt's exact binary baseline (max differences 0).\n- "+System.IO.File.ReadAllLines(ledgerPath).Length+" centered stone/dark-earth strokes along "+totalLength.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" m road: varied 0.76–1.38 m stamps, three jagged masks, varied rotations, stone 2.2–3.8 cm requested, dark earth 1.2–2.0 cm requested; most stamps favour road edges and a few low-strength chips cross the centre.\n- Each ledger row records requested and measured height deltas in metres, layer weights before/after, size, rotation, mask index and strength. See `batch3b_fixeduv_stamp_ledger.jsonl` and `batch3b_plan_fixeduv.json`.\n- Quantitative checks passed: outside-mask height max "+maxOutsideHeight.ToString("G4",System.Globalization.CultureInfo.InvariantCulture)+" m, outside-mask alphamap max "+maxOutsideAlpha.ToString("G4",System.Globalization.CultureInfo.InvariantCulture)+", centerline max "+maxCenterlineDelta.ToString("G4",System.Globalization.CultureInfo.InvariantCulture)+" m; max road-layer weight change within painted area "+maxRoadErr.ToString("G4",System.Globalization.CultureInfo.InvariantCulture)+". See `batch3b_data_validation.json`.\n- Mask spill in the first applied set came from a custom CPU bilinear sampler mapping UV by `width-1`, which disagreed with Texture2D.GetPixelBilinear at boundary texels. Corrected ApplyStamp to use the same Unity sampler as RoadAllows. A disposable exact-candidate test now gives 0 outside-mask height and splat changes. Final strokes were reapplied from the exact full-array baseline.\n- Assets saved and scene reopened in live Unity Editor; Terrain and TerrainCollider both still reference `TD_TerrainRoad_ReliefV2.asset`; non-Terrain scene snapshot and light/post/collider/Terrain settings preserved. Protected original TD/layers and Tutorial_scene hashes match approved baselines.\n- No new 3D models, Play Mode, or Gameplay preflight in this batch.\n";
    System.IO.File.WriteAllText(reportPath,report,new System.Text.UTF8Encoding(false));
    System.IO.File.Delete(baselinePath); System.IO.File.Delete(snapshotPath);
    UnityEditor.EditorPrefs.SetInt(prefKey,candidates.Count+1);
    Debug.Log("Batch3B final validation passed; report="+reportPath+"; outsideHeight="+maxOutsideHeight+" m, outsideAlpha="+maxOutsideAlpha+", centerline="+maxCenterlineDelta+" m, center deltas stone="+minStone+".."+maxStone+"m earth="+minEarth+".."+maxEarth+"m.");
    return;
}
if(next==candidates.Count+1) { if(!System.IO.File.Exists(reportPath)) throw new System.Exception("Final report missing after validation."); Debug.Log("Batch3B already complete; no stamps repeated."); return; }
if(next < -1) throw new System.Exception("Previous batch3B execution was interrupted while checkpoint "+(-2-next)+" was active. Inspect ledger, baseline, and live TD before deciding recovery; do not retry blindly.");
throw new System.Exception("Unexpected batch3B progress value "+next);
}))();
