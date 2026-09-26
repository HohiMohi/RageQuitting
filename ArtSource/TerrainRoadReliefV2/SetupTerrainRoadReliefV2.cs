((System.Action)(() => {
// Run from the project root with `unity command eval_file ArtSource/TerrainRoadReliefV2/SetupTerrainRoadReliefV2.cs`.
// Change phase to 1 (mask), 2 (layers + TerrainData), 3 (scene + TerrainCollider references), or 4/5 (reopen verification).

const int phase = 5;
const string scenePath = "Assets/Scenes/TerrainRoad_Lookdev.unity";
const string root = "Assets/Art/Environment/TerrainRoadLookdev/";
const string surface = root + "SurfaceV2/";
const string sourceDataPath = root + "TerrainData/TD_TerrainRoad_Lookdev.asset";
const string dataPath = root + "TerrainData/TD_TerrainRoad_ReliefV2.asset";
const string maskPath = surface + "RoadLimit_ReliefV2.png";
const string grassPath = root + "TerrainLayers/TL_Grass_Warm.terrainlayer";
const string oldRoadPath = root + "TerrainLayers/TL_Road_OchreStone.terrainlayer";
const string oldStonePath = root + "TerrainLayers/TL_Stone_MutedGrey.terrainlayer";
const string roadLayerPath = root + "TerrainLayers/TL_Road_ReliefV2.terrainlayer";
const string stoneLayerPath = root + "TerrainLayers/TL_Stone_Grey_ReliefV2.terrainlayer";
const string earthLayerPath = root + "TerrainLayers/TL_Earth_Dark_ReliefV2.terrainlayer";
const string checkpointPath = "Artifacts/TerrainRoadReliefV2/batch3a_checkpoint.txt";
const string snapshotPath = "Artifacts/TerrainRoadReliefV2/batch3a_scene_before_snapshot.txt";
const string colliderSnapshotPath = "Artifacts/TerrainRoadReliefV2/batch3a_collider_before_snapshot.txt";
const string finalReportPath = "Artifacts/TerrainRoadReliefV2/batch3a_setup_validation.md";

string HashFile(string path) { using (var sha = System.Security.Cryptography.SHA256.Create()) return System.BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", ""); }
string HashText(string text) { using (var sha = System.Security.Cryptography.SHA256.Create()) return System.BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", ""); }
const string expectedSourceDataHash = "A4C15D4F711DC3D46C70A8B2A6EE086863729C83F3A4BC3FAA9094B3E0015777";
const string expectedOldRoadHash = "0D45094A5136E74219A938BD7770D4E2A5BC7D0DBECF2EC66C814EFBA097B6E2";
const string expectedOldStoneHash = "5D75035FFFB47301D59EF89955F75C945C2D5E455D5898D22A98D285E37F4E02";
const string expectedTutorialHash = "36F2C7FA09AB891850B410C5453CFC957A5C290D77672BA57DD3DBCEA017E6FB";
void VerifyOriginalHashes() {
    if (HashFile(sourceDataPath) != expectedSourceDataHash || HashFile(oldRoadPath) != expectedOldRoadHash ||
        HashFile(oldStonePath) != expectedOldStoneHash || HashFile("Assets/Scenes/Tutorial_scene.unity") != expectedTutorialHash)
        throw new System.Exception("An original asset hash differs from the approved baseline; refusing to continue.");
}
void VerifyCleanTargetScene() {
    UnityEngine.SceneManagement.Scene s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
    if (!s.IsValid() || s.path != scenePath) throw new System.Exception("Expected active lookdev scene, got " + (s.IsValid() ? s.path : "invalid scene"));
    if (UnityEngine.SceneManagement.SceneManager.sceneCount != 1 || Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt).Any(x => x.isDirty))
        throw new System.Exception("Unexpected or dirty loaded scene; refusing to edit/save or close it.");
}
UnityEngine.Terrain GetTargetTerrain() {
    UnityEngine.Terrain t = UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
    if (!t || !t.terrainData) throw new System.Exception("Target scene Terrain is missing.");
    return t;
}
UnityEngine.TerrainData GetOriginalData() {
    UnityEngine.Terrain t = GetTargetTerrain();
    if (UnityEditor.AssetDatabase.GetAssetPath(t.terrainData) != sourceDataPath) throw new System.Exception("Scene no longer references the expected original TerrainData.");
    return t.terrainData;
}
UnityEngine.TerrainLayer LoadLayer(string path) {
    UnityEngine.TerrainLayer layer = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(path);
    if (!layer) throw new System.Exception("Missing TerrainLayer " + path);
    return layer;
}
UnityEngine.Texture2D LoadV2Texture(string name, bool normal) {
    string path = surface + name + ".png";
    UnityEngine.Texture2D texture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
    UnityEditor.TextureImporter importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    if (!texture || !importer || importer.wrapMode != UnityEngine.TextureWrapMode.Repeat || importer.textureType != (normal ? UnityEditor.TextureImporterType.NormalMap : UnityEditor.TextureImporterType.Default) || importer.sRGBTexture == normal)
        throw new System.Exception("Missing or incorrectly imported required V2 texture " + path);
    return texture;
}
UnityEngine.Vector2[] samplePoints = { new UnityEngine.Vector2(14f, 3f), new UnityEngine.Vector2(14.4f, 6.5f), new UnityEngine.Vector2(17f, 13f), new UnityEngine.Vector2(16f, 28f), new UnityEngine.Vector2(2f, 16f), new UnityEngine.Vector2(28f, 16f) };
float[] ReadSourceBandSamples(UnityEngine.TerrainData d, int roadIndex, int stoneIndex) {
    int n = d.alphamapResolution;
    float[,,] a = d.GetAlphamaps(0, 0, n, n);
    return samplePoints.Select(p => { int x = UnityEngine.Mathf.Clamp((int)(p.x / d.size.x * n), 0, n - 1); int y = UnityEngine.Mathf.Clamp((int)(p.y / d.size.z * n), 0, n - 1); return a[y, x, roadIndex] + a[y, x, stoneIndex]; }).ToArray();
}
string SceneSnapshot(UnityEngine.SceneManagement.Scene s, UnityEngine.Terrain target) {
    var lines = new System.Collections.Generic.List<string>();
    string PathOf(UnityEngine.Transform t) { var parts = new System.Collections.Generic.List<string>(); while (t) { parts.Insert(0, t.name); t = t.parent; } return string.Join("/", parts); }
    void Visit(UnityEngine.Transform tr) {
        UnityEngine.GameObject go = tr.gameObject;
        string p = PathOf(tr);
        lines.Add("GO|" + p + "|active=" + go.activeSelf + "|layer=" + go.layer + "|tag=" + go.tag + "|pos=" + tr.localPosition.ToString("F6") + "|rot=" + tr.localRotation.eulerAngles.ToString("F6") + "|scale=" + tr.localScale.ToString("F6"));
        foreach (UnityEngine.Component c in go.GetComponents<UnityEngine.Component>()) {
            if (!c) { lines.Add("MISSING|" + p); continue; }
            if (c == target || c == target.GetComponent<UnityEngine.TerrainCollider>()) continue;
            lines.Add("COMP|" + p + "|" + c.GetType().FullName + "|" + UnityEditor.EditorJsonUtility.ToJson(c));
        }
        for (int i = 0; i < tr.childCount; i++) Visit(tr.GetChild(i));
    }
    foreach (UnityEngine.GameObject go in s.GetRootGameObjects()) Visit(go.transform);
    return string.Join("\n", lines.OrderBy(x => x, System.StringComparer.Ordinal));
}
string TerrainSettings(UnityEngine.Terrain t) { return t.name + "|" + t.drawInstanced + "|" + t.heightmapPixelError + "|" + t.basemapDistance + "|" + t.shadowCastingMode + "|" + (t.materialTemplate ? UnityEditor.AssetDatabase.GetAssetPath(t.materialTemplate) : "null") + "|" + t.transform.position + "|" + t.transform.rotation + "|" + t.transform.localScale; }
string TerrainColliderSettings(UnityEngine.TerrainCollider c) { return c.enabled + "|" + c.isTrigger + "|" + c.contactOffset + "|" + c.sharedMaterial; }

System.IO.Directory.CreateDirectory("Artifacts/TerrainRoadReliefV2");
if (phase == 1) {
    VerifyCleanTargetScene(); VerifyOriginalHashes();
    UnityEngine.TerrainData source = GetOriginalData();
    if (source.alphamapResolution != 256) throw new System.Exception("Expected 256x256 source alphamap.");
    UnityEngine.TerrainLayer[] oldLayers = source.terrainLayers;
    int grassIndex = System.Array.IndexOf(oldLayers, LoadLayer(grassPath));
    int roadIndex = System.Array.IndexOf(oldLayers, LoadLayer(oldRoadPath));
    int stoneIndex = System.Array.IndexOf(oldLayers, LoadLayer(oldStonePath));
    if (oldLayers.Length != 3 || grassIndex < 0 || roadIndex < 0 || stoneIndex < 0 || grassIndex == roadIndex || grassIndex == stoneIndex || roadIndex == stoneIndex)
        throw new System.Exception("Original TerrainData does not have the expected three layer assignments.");
    string[] maskSamplesText;
    if (!System.IO.File.Exists(maskPath)) {
        if (System.IO.File.Exists(maskPath + ".meta")) throw new System.Exception("Orphaned mask metadata exists; refusing to overwrite it.");
        int n = source.alphamapResolution;
        float[,,] a = source.GetAlphamaps(0, 0, n, n);
        var pixels = new UnityEngine.Color[n * n];
        float maxSumError = 0f;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) {
            float road = a[y, x, roadIndex], stone = a[y, x, stoneIndex], grass = a[y, x, grassIndex];
            maxSumError = UnityEngine.Mathf.Max(maxSumError, UnityEngine.Mathf.Abs(grass + road + stone - 1f));
            float band = UnityEngine.Mathf.Clamp01(road + stone);
            pixels[y * n + x] = new UnityEngine.Color(band, band, band, 1f);
        }
        if (maxSumError > 0.005f) throw new System.Exception("Original weights do not normalize within 8-bit splat tolerance; error=" + maxSumError);
        var tex = new UnityEngine.Texture2D(n, n, UnityEngine.TextureFormat.RGBA32, false, true) { name = "RoadLimit_ReliefV2" };
        tex.SetPixels(pixels); tex.Apply(false, false); System.IO.File.WriteAllBytes(maskPath, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
        UnityEditor.AssetDatabase.ImportAsset(maskPath, UnityEditor.ImportAssetOptions.ForceUpdate);
    }
    UnityEditor.TextureImporter mi = UnityEditor.AssetImporter.GetAtPath(maskPath) as UnityEditor.TextureImporter;
    if (!mi) throw new System.Exception("Mask import did not create a TextureImporter.");
    mi.textureType = UnityEditor.TextureImporterType.Default; mi.sRGBTexture = false; mi.alphaSource = UnityEditor.TextureImporterAlphaSource.None; mi.alphaIsTransparency = false;
    mi.wrapMode = UnityEngine.TextureWrapMode.Clamp; mi.isReadable = true; mi.mipmapEnabled = false; mi.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; mi.SaveAndReimport();
    UnityEngine.Texture2D mask = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(maskPath);
    if (!mask || mask.width != 256 || mask.height != 256 || !mask.isReadable || mi.wrapMode != UnityEngine.TextureWrapMode.Clamp || mi.sRGBTexture || mi.textureCompression != UnityEditor.TextureImporterCompression.Uncompressed || mi.mipmapEnabled)
        throw new System.Exception("Road mask importer configuration failed.");
    float[] src = ReadSourceBandSamples(source, roadIndex, stoneIndex);
    float[] dst = samplePoints.Select(p => mask.GetPixelBilinear(p.x / source.size.x, p.y / source.size.z).r).ToArray();
    if (src[0] < .95f || src[1] < .95f || src[2] < .95f || src[3] < .95f || src[4] > .05f || src[5] > .05f || dst[0] < .9f || dst[1] < .9f || dst[2] < .9f || dst[3] < .9f || dst[4] > .05f || dst[5] > .05f)
        throw new System.Exception("Bottom-left UV / inside-outside mask samples failed.");
    maskSamplesText = dst.Select(v => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    System.IO.File.WriteAllText(checkpointPath, "phase=mask-complete\nsourceBand=" + string.Join(",", src.Select(v => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))) + "\nmaskSamples=" + string.Join(",", maskSamplesText) + "\nmask=256x256 linear/uncompressed/Clamp/readable/no-mips\n", new System.Text.UTF8Encoding(false));
    Debug.Log("Batch3A phase 1 complete: mask validated, source UV samples=" + string.Join(",", src) + "; imported mask samples=" + string.Join(",", dst));
    return;
}
if (phase == 2) {
    VerifyCleanTargetScene(); VerifyOriginalHashes();
    UnityEngine.TerrainData source = GetOriginalData();
    UnityEngine.Texture2D[] diffuse = { LoadV2Texture("T_Road_ReliefV2", false), LoadV2Texture("T_Stone_Grey_ReliefV2", false), LoadV2Texture("T_Earth_Dark_ReliefV2", false) };
    UnityEngine.Texture2D[] normal = { LoadV2Texture("N_Road_ReliefV2", true), LoadV2Texture("N_Stone_Grey_ReliefV2", true), LoadV2Texture("N_Earth_Dark_ReliefV2", true) };
    string[] paths = { roadLayerPath, stoneLayerPath, earthLayerPath };
    string[] names = { "TL_Road_ReliefV2", "TL_Stone_Grey_ReliefV2", "TL_Earth_Dark_ReliefV2" };
    float[] scales = { .65f, .7f, .55f };
    UnityEngine.TerrainLayer[] fresh = new UnityEngine.TerrainLayer[3];
    for (int i = 0; i < fresh.Length; i++) {
        fresh[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(paths[i]);
        if (!fresh[i]) {
            if (System.IO.File.Exists(paths[i] + ".meta")) throw new System.Exception("Orphaned TerrainLayer metadata exists: " + paths[i]);
            fresh[i] = new UnityEngine.TerrainLayer { name = names[i], diffuseTexture = diffuse[i], normalMapTexture = normal[i], tileSize = new UnityEngine.Vector2(4f, 4f), tileOffset = UnityEngine.Vector2.zero, normalScale = scales[i] };
            UnityEditor.AssetDatabase.CreateAsset(fresh[i], paths[i]);
        }
        if (fresh[i].diffuseTexture != diffuse[i] || fresh[i].normalMapTexture != normal[i] || fresh[i].tileSize != new UnityEngine.Vector2(4f, 4f) || fresh[i].tileOffset != UnityEngine.Vector2.zero || UnityEngine.Mathf.Abs(fresh[i].normalScale - scales[i]) > .001f)
            throw new System.Exception("TerrainLayer validation failed: " + paths[i]);
    }
    UnityEngine.TerrainData existing = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
    if (!existing) {
        if (System.IO.File.Exists(dataPath + ".meta")) throw new System.Exception("Orphaned TerrainData metadata exists.");
        UnityEngine.TerrainLayer grass = LoadLayer(grassPath), oldRoad = LoadLayer(oldRoadPath), oldStone = LoadLayer(oldStonePath);
        UnityEngine.TerrainLayer[] oldLayers = source.terrainLayers;
        int gi = System.Array.IndexOf(oldLayers, grass), ri = System.Array.IndexOf(oldLayers, oldRoad), si = System.Array.IndexOf(oldLayers, oldStone);
        if (gi < 0 || ri < 0 || si < 0) throw new System.Exception("Original layer assignment changed.");
        int n = source.alphamapResolution;
        float[,,] a = source.GetAlphamaps(0, 0, n, n), converted = new float[n, n, 4];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { converted[y, x, 0] = a[y, x, gi]; converted[y, x, 1] = UnityEngine.Mathf.Clamp01(a[y, x, ri] + a[y, x, si]); converted[y, x, 2] = 0f; converted[y, x, 3] = 0f; }
        existing = UnityEngine.Object.Instantiate(source); existing.name = "TD_TerrainRoad_ReliefV2";
        existing.terrainLayers = new[] { grass, fresh[0], fresh[1], fresh[2] };
        UnityEditor.AssetDatabase.CreateAsset(existing, dataPath);
        UnityEditor.AssetDatabase.SaveAssets();
        existing = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
        existing.SetAlphamaps(0, 0, converted);
        UnityEditor.EditorUtility.SetDirty(existing);
    }
    UnityEditor.AssetDatabase.SaveAssets();
    UnityEngine.TerrainData copy = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
    if (!copy || copy.terrainLayers.Length != 4 || copy.terrainLayers[0] != LoadLayer(grassPath) || copy.terrainLayers[1] != fresh[0] || copy.terrainLayers[2] != fresh[1] || copy.terrainLayers[3] != fresh[2]) throw new System.Exception("Copied TerrainData did not reload with expected four layers.");
    int resolution = copy.alphamapResolution;
    float[,,] oldA = source.GetAlphamaps(0, 0, resolution, resolution), newA = copy.GetAlphamaps(0, 0, resolution, resolution);
    UnityEngine.TerrainLayer[] oldLs = source.terrainLayers; int g = System.Array.IndexOf(oldLs, LoadLayer(grassPath)), r = System.Array.IndexOf(oldLs, LoadLayer(oldRoadPath)), s = System.Array.IndexOf(oldLs, LoadLayer(oldStonePath));
    float maxRoadError = 0f, maxGrassError = 0f, maxStone = 0f, maxEarth = 0f, maxSumError = 0f;
    for (int y = 0; y < resolution; y++) for (int x = 0; x < resolution; x++) {
        maxRoadError = UnityEngine.Mathf.Max(maxRoadError, UnityEngine.Mathf.Abs(newA[y, x, 1] - (oldA[y, x, r] + oldA[y, x, s])));
        maxGrassError = UnityEngine.Mathf.Max(maxGrassError, UnityEngine.Mathf.Abs(newA[y, x, 0] - oldA[y, x, g]));
        maxStone = UnityEngine.Mathf.Max(maxStone, newA[y, x, 2]); maxEarth = UnityEngine.Mathf.Max(maxEarth, newA[y, x, 3]);
        maxSumError = UnityEngine.Mathf.Max(maxSumError, UnityEngine.Mathf.Abs(newA[y, x, 0] + newA[y, x, 1] + newA[y, x, 2] + newA[y, x, 3] - 1f));
    }
    if (maxRoadError > .005f || maxGrassError > .005f || maxStone > .0001f || maxEarth > .0001f || maxSumError > .005f) throw new System.Exception("Copied TerrainData splat checks failed.");
    System.IO.File.WriteAllText(checkpointPath, System.IO.File.ReadAllText(checkpointPath) + "phase=assets-complete\nmaxRoadError=" + maxRoadError.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + "\nmaxGrassError=" + maxGrassError.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + "\ninitialStoneMax=" + maxStone.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + "\ninitialEarthMax=" + maxEarth.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + "\nmaxSumError=" + maxSumError.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + "\n", new System.Text.UTF8Encoding(false));
    Debug.Log("Batch3A phase 2 complete: layers and duplicate TerrainData verified.");
    return;
}
if (phase == 3) {
    VerifyCleanTargetScene(); VerifyOriginalHashes();
    UnityEngine.Terrain terrain = GetTargetTerrain(); UnityEngine.TerrainData copy = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
    if (!copy || copy.terrainLayers.Length != 4) throw new System.Exception("Batch 3A TerrainData is not ready.");
    UnityEngine.TerrainCollider collider = terrain.GetComponent<UnityEngine.TerrainCollider>();
    if (!collider) throw new System.Exception("Target TerrainCollider is missing.");
    if (terrain.terrainData != copy && UnityEditor.AssetDatabase.GetAssetPath(terrain.terrainData) != sourceDataPath) throw new System.Exception("Scene Terrain no longer uses expected data.");
    string beforeSnapshot = SceneSnapshot(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), terrain);
    string beforeTerrainSettings = TerrainSettings(terrain);
    string beforeColliderSettings = TerrainColliderSettings(collider);
    System.IO.File.WriteAllText(snapshotPath, "terrain=" + beforeTerrainSettings + "\n" + beforeSnapshot, new System.Text.UTF8Encoding(false));
    terrain.terrainData = copy; collider.terrainData = copy;
    UnityEditor.EditorUtility.SetDirty(terrain); UnityEditor.EditorUtility.SetDirty(collider); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene())) throw new System.Exception("Scene save failed.");
    System.IO.File.WriteAllText(checkpointPath, System.IO.File.ReadAllText(checkpointPath) + "phase=scene-saved\nsceneBeforeHash=" + HashText(beforeSnapshot) + "\n", new System.Text.UTF8Encoding(false));
    Debug.Log("Batch3A phase 3 complete: saved only the new TerrainData reference; next reopen to verify.");
    return;
}
if (phase == 4) {
    VerifyCleanTargetScene();
    if (!System.IO.File.Exists(snapshotPath)) {
        if (!System.IO.File.Exists(finalReportPath)) throw new System.Exception("No scene-before snapshot or final report found; cannot verify a prior setup run.");
        UnityEngine.Terrain checkedTerrain = GetTargetTerrain();
        UnityEngine.TerrainData checkedData = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
        UnityEngine.TerrainLayer[] checkedLayers = checkedTerrain.terrainData.terrainLayers;
        if (!checkedData || checkedTerrain.terrainData != checkedData || checkedLayers.Length != 4 || checkedLayers[0] != LoadLayer(grassPath) || checkedLayers[1] != LoadLayer(roadLayerPath) || checkedLayers[2] != LoadLayer(stoneLayerPath) || checkedLayers[3] != LoadLayer(earthLayerPath))
            throw new System.Exception("Previously verified scene reference or layer order has changed.");
        VerifyOriginalHashes();
        Debug.Log("Batch3A phase 4 repeat check passed: saved scene reference, four layers, and protected hashes remain valid.");
        return;
    }
    UnityEngine.SceneManagement.Scene reopened = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
    if (!reopened.IsValid() || reopened.isDirty || UnityEngine.SceneManagement.SceneManager.sceneCount != 1) throw new System.Exception("Scene reopen failed or returned dirty.");
    UnityEngine.Terrain terrain = GetTargetTerrain(); UnityEngine.TerrainData copy = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
    UnityEngine.TerrainLayer[] layers = terrain.terrainData.terrainLayers;
    if (!copy || terrain.terrainData != copy || layers.Length != 4 || layers[0] != LoadLayer(grassPath) || layers[1] != LoadLayer(roadLayerPath) || layers[2] != LoadLayer(stoneLayerPath) || layers[3] != LoadLayer(earthLayerPath)) throw new System.Exception("Reopened scene Terrain reference or exact layer order failed.");
    string[] beforeLines = System.IO.File.ReadAllLines(snapshotPath);
    string oldTerrainSettings = beforeLines[0].Substring("terrain=".Length);
    string oldSnapshot = string.Join("\n", beforeLines.Skip(1));
    string newSnapshot = SceneSnapshot(reopened, terrain);
    if (oldTerrainSettings != TerrainSettings(terrain) || HashText(oldSnapshot) != HashText(newSnapshot)) throw new System.Exception("A non-Terrain component, light/post object, hierarchy, transform, or Terrain setting changed.");
    VerifyOriginalHashes();
    string checkpoint = System.IO.File.ReadAllText(checkpointPath);
    string report = "# Terrain Road Relief V2 — Batch 3A Setup\n\n" +
        "- Unity " + UnityEngine.Application.unityVersion + "; target scene saved, closed/reopened through EditorSceneManager, and clean.\n" +
        "- New TerrainData: " + dataPath + "; duplicated from the original, size " + copy.size + ", heightmap " + copy.heightmapResolution + ", alphamap " + copy.alphamapResolution + ".\n" +
        "- Four layers in order: original grass, new road, new grey stone, new dark earth. New layers use 4m tiling and zero offset; normal scales 0.65, 0.7, 0.55.\n" +
        "- Splat conversion checkpoint: " + checkpoint.Replace("\n", "; ") + "\n" +
        "- Road mask: 256x256, linear, uncompressed, readable, Clamp, no mipmaps. Bottom-left UV source and imported mask samples (inside then outside) are recorded in checkpoint: " + checkpoint.Replace("\n", "; ") + "\n" +
        "- Reopen validation: exact new TerrainData reference and layer order confirmed. All other hierarchy/transform/component JSON snapshots and Terrain settings match the pre-edit scene, including light and volume objects.\n" +
        "- Unchanged SHA256: original TerrainData " + HashFile(sourceDataPath) + "; original road layer " + HashFile(oldRoadPath) + "; original stone layer " + HashFile(oldStonePath) + "; Tutorial_scene " + HashFile("Assets/Scenes/Tutorial_scene.unity") + ".\n" +
        "- Recovery notes: the first all-in-one eval_file exceeded Pipeline's 5-second main-thread limit before creating outputs; Editor and source hashes stayed healthy. First phase-2 copy serialized grass-only because SetAlphamaps ran before CreateAsset; corrected by creating the duplicate asset first, then setting weights on the loaded asset. Full 256x256 comparisons now pass (road max error <0.000001, grass error 0); source weight sum differs by at most one 8-bit step (1/255). Work was resumed as four idempotent phases with checkpoints.\n" +
        "- No relief stamps, Play Mode, or Gameplay preflight were run in this batch.\n";
    System.IO.File.WriteAllText(finalReportPath, report, new System.Text.UTF8Encoding(false));
    System.IO.File.Delete(snapshotPath);
    Debug.Log("Batch3A phase 4 complete: reopened scene and original hashes verified.\n" + report);
    return;
}
if (phase == 5) {
    VerifyCleanTargetScene();
    UnityEngine.Terrain terrain = GetTargetTerrain();
    UnityEngine.TerrainData copy = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>(dataPath);
    UnityEngine.TerrainCollider collider = terrain.GetComponent<UnityEngine.TerrainCollider>();
    if (!copy || !collider || terrain.terrainData != copy) throw new System.Exception("Expected saved Terrain to use ReliefV2 data before collider repair.");
    if (!System.IO.File.Exists(colliderSnapshotPath)) {
        string snap = SceneSnapshot(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), terrain);
        string terrainSettings = TerrainSettings(terrain);
        string colliderSettings = TerrainColliderSettings(collider);
        System.IO.File.WriteAllText(colliderSnapshotPath, "terrain=" + terrainSettings + "\ncollider=" + colliderSettings + "\nsnapshot=" + HashText(snap) + "\n", new System.Text.UTF8Encoding(false));
        if (collider.terrainData != copy) collider.terrainData = copy;
        UnityEditor.EditorUtility.SetDirty(collider);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene())) throw new System.Exception("Scene save failed while repairing TerrainCollider reference.");
        Debug.Log("Batch3A phase 5 repair saved; reopen scene to verify TerrainCollider data reference.");
        return;
    }
    UnityEngine.SceneManagement.Scene reopened = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
    terrain = GetTargetTerrain(); collider = terrain.GetComponent<UnityEngine.TerrainCollider>();
    string[] baseline = System.IO.File.ReadAllLines(colliderSnapshotPath);
    if (terrain.terrainData != copy || collider.terrainData != copy || TerrainSettings(terrain) != baseline[0].Substring("terrain=".Length) || TerrainColliderSettings(collider) != baseline[1].Substring("collider=".Length) || HashText(SceneSnapshot(reopened, terrain)) != baseline[2].Substring("snapshot=".Length))
        throw new System.Exception("Reopened collider reference or protected scene state verification failed.");
    VerifyOriginalHashes();
    string update = "\n## Collider reference correction\n\n- Live Editor found TerrainCollider still pointed at original TD after Terrain.terrainData switched. Assigned both components to TD_TerrainRoad_ReliefV2, saved and reopened the scene.\n- Reopen verification: Terrain.terrainData and TerrainCollider.terrainData both reference " + dataPath + "; Terrain settings, collider settings other than data reference, all non-Terrain component snapshots, and protected hashes match the pre-correction snapshot.\n- No relief stamps, Play Mode, or Gameplay preflight were run.\n";
    System.IO.File.AppendAllText(finalReportPath, update, new System.Text.UTF8Encoding(false));
    System.IO.File.Delete(colliderSnapshotPath);
    Debug.Log("Batch3A phase 5 complete: collider reference corrected, scene reopened, snapshots and protected hashes verified.");
    return;
}
throw new System.Exception("Unknown phase " + phase + ". Set phase to 1, 2, 3, or 4.");

}))();
