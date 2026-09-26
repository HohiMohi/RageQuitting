// Temporary live-Editor preview. Captures synchronously with the Pipeline Game-view renderer,
// then restores the original references and camera transform in finally. Never saves a scene.
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.name != "Tutorial_scene" || !scene.isDirty || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Expected the already-dirty Tutorial_scene in Edit mode.");
var cameraGo = UnityEngine.GameObject.Find("MainCamera");
var camera = cameraGo == null ? null : cameraGo.GetComponent<UnityEngine.Camera>();
var ground = UnityEngine.GameObject.Find("Ground_West").GetComponent<UnityEngine.Renderer>();
var westMain = UnityEngine.GameObject.Find("West_Main").GetComponent<UnityEngine.Renderer>();
var road0 = UnityEngine.GameObject.Find("WestPath_0").GetComponent<UnityEngine.Renderer>();
var road1 = UnityEngine.GameObject.Find("WestPath_1").GetComponent<UnityEngine.Renderer>();
if (camera == null || ground == null || westMain == null || road0 == null || road1 == null)
    throw new System.InvalidOperationException("A preview camera or target renderer was not found.");
var originalGround = (UnityEngine.Material[])ground.sharedMaterials.Clone();
var originalWestMain = (UnityEngine.Material[])westMain.sharedMaterials.Clone();
var originalRoad0 = (UnityEngine.Material[])road0.sharedMaterials.Clone();
var originalRoad1 = (UnityEngine.Material[])road1.sharedMaterials.Clone();
var cameraTransform = camera.transform;
var originalLocalPosition = cameraTransform.localPosition;
var originalLocalRotation = cameraTransform.localRotation;
var originalLocalScale = cameraTransform.localScale;
var clones = new System.Collections.Generic.List<UnityEngine.Material>();
var shots = new System.Collections.Generic.List<string>();
var failure = (string)null;
var restoreFailure = (string)null;
var restored = false;
var projectRoot = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
var artifactDir = System.IO.Path.Combine(projectRoot, "Artifacts", "GrainRoadLookdev");
System.IO.Directory.CreateDirectory(artifactDir);
var screenshotAssembly = System.Linq.Enumerable.First(System.AppDomain.CurrentDomain.GetAssemblies(),
    a => a.GetName().Name == "Unity.Pipeline.Editor");
var screenshotType = screenshotAssembly.GetType("Unity.Pipeline.Editor.Commands.ScreenshotCommand", true);
var captureMethod = screenshotType.GetMethod("CaptureScreenshot", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
if (captureMethod == null) throw new System.InvalidOperationException("Pipeline Game-view screenshot method was not found.");
var capture = (System.Action<string, string>)((label, viewTag) =>
{
    var path = System.IO.Path.Combine(artifactDir, label + ".png");
    var response = captureMethod.Invoke(null, new object[] { "game", path, 1600, 900 });
    var responseType = response.GetType();
    var ok = (bool)responseType.GetProperty("Success").GetValue(response);
    if (!ok) throw new System.InvalidOperationException("Game-view capture failed for " + label + ": " + responseType.GetProperty("Message").GetValue(response));
    shots.Add($"{{\"name\":\"{label}\",\"pose\":\"{viewTag}\",\"path\":\"{path.Replace("\\", "\\\\")}\",\"width\":1600,\"height\":900}}");
});
var setFirstPerson = (System.Action)(() =>
{
    cameraTransform.SetPositionAndRotation(new UnityEngine.Vector3(-13.5f, 1.7f, -20.0f), UnityEngine.Quaternion.Euler(20f, -40f, 0f));
});
var grassMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Art/Terrain/Prototype/Terrain_Grass_Warm_Relief.mat");
var warmRoad = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_Relief.mat");
var sparseRoad = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainSparse_Relief.mat");
var denseRoad = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainDense_Relief.mat");
if (grassMaterial == null || warmRoad == null || sparseRoad == null || denseRoad == null)
    throw new System.InvalidOperationException("A reference or new road material was not found.");
var assignRoad = (System.Action<UnityEngine.Material>)(source =>
{
    var p0 = new UnityEngine.Material(source);
    p0.name = "TEMP_GrainRoad_WestPath0_" + source.name;
    p0.SetTextureScale("_BaseMap", new UnityEngine.Vector2(1.275f, .75f));
    if (p0.HasProperty("_BumpMap")) p0.SetTextureScale("_BumpMap", new UnityEngine.Vector2(1.275f, .75f));
    var p1 = new UnityEngine.Material(source);
    p1.name = "TEMP_GrainRoad_WestPath1_" + source.name;
    p1.SetTextureScale("_BaseMap", new UnityEngine.Vector2(2.915f, .75f));
    if (p1.HasProperty("_BumpMap")) p1.SetTextureScale("_BumpMap", new UnityEngine.Vector2(2.915f, .75f));
    clones.Add(p0); clones.Add(p1);
    road0.sharedMaterials = new[] { p0 };
    road1.sharedMaterials = new[] { p1 };
});
try
{
    ground.sharedMaterials = new[] { grassMaterial };
    westMain.sharedMaterials = new[] { grassMaterial };
    setFirstPerson();
    assignRoad(warmRoad);
    capture("final_01_first_person_warm_brushed", "first_person");
    assignRoad(sparseRoad);
    setFirstPerson();
    capture("final_02_first_person_grain_sparse", "first_person");
    assignRoad(denseRoad);
    setFirstPerson();
    capture("final_03_first_person_grain_dense", "first_person");

    // One close pose over the West path join keeps lighting and framing fixed for all variants.
    var detailPosition = new UnityEngine.Vector3(-17.0f, 7.0f, -20.0f);
    var detailRotation = UnityEngine.Quaternion.Euler(75f, 56f, 0f);
    cameraTransform.SetPositionAndRotation(detailPosition, detailRotation);
    assignRoad(warmRoad);
    capture("final_04_detail_warm_brushed", "detail_and_repeat_join");
    assignRoad(sparseRoad);
    capture("final_05_detail_grain_sparse", "detail_and_repeat_join");
    assignRoad(denseRoad);
    capture("final_06_detail_grain_dense", "detail_and_repeat_join");
}
catch (System.Exception ex)
{
    failure = ex.ToString();
}
finally
{
    try { ground.sharedMaterials = originalGround; } catch (System.Exception ex) { restoreFailure += "ground: " + ex + "\n"; }
    try { westMain.sharedMaterials = originalWestMain; } catch (System.Exception ex) { restoreFailure += "westMain: " + ex + "\n"; }
    try { road0.sharedMaterials = originalRoad0; } catch (System.Exception ex) { restoreFailure += "road0: " + ex + "\n"; }
    try { road1.sharedMaterials = originalRoad1; } catch (System.Exception ex) { restoreFailure += "road1: " + ex + "\n"; }
    try
    {
        cameraTransform.localPosition = originalLocalPosition;
        cameraTransform.localRotation = originalLocalRotation;
        cameraTransform.localScale = originalLocalScale;
    }
    catch (System.Exception ex) { restoreFailure += "camera: " + ex + "\n"; }
    foreach (var clone in clones)
        if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
    restored = restoreFailure == null;
}
var reportPath = System.IO.Path.Combine(artifactDir, "preview_capture_report.json");
System.IO.File.WriteAllText(reportPath,
    "{\"sceneWasDirty\":true,\"sceneSaved\":false,\"restoredInFinally\":" + restored.ToString().ToLowerInvariant() +
    ",\"cameraFirstPersonPosition\":[-13.5,1.7,-20.0],\"cameraFirstPersonEuler\":[20,-40,0],\"bumpScale\":1.0,"+
    "\"metallic\":0,\"smoothness\":0,\"screenshots\":[" + string.Join(",", shots) +
    "],\"captureError\":\"" + (failure ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ") +
    "\",\"restoreError\":\"" + (restoreFailure ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ") + "\"}");
if (!restored) throw new System.InvalidOperationException("Scene restore reported failure: " + restoreFailure);
if (failure != null) throw new System.InvalidOperationException("Preview capture failed after restore: " + failure);
return "Captured " + shots.Count + " Game-view frames; temporary scene references and camera restored in finally.";
