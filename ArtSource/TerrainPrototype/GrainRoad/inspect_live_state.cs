var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
var cameras = UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(UnityEngine.FindObjectsSortMode.None);
var cameraLines = System.Linq.Enumerable.Select(cameras,
    (System.Func<UnityEngine.Camera, string>)(c => $"CAMERA|{c.name}|{c.transform.position.ToString("F4")}|{c.transform.eulerAngles.ToString("F4")}|scale={c.transform.localScale.ToString("F4")}|fov={c.fieldOfView:F2}|enabled={c.enabled}"));
var targetNames = new[] { "Ground_West", "West_Main", "WestPath_0", "WestPath_1" };
var rendererLines = System.Linq.Enumerable.SelectMany(targetNames, n =>
{
    var go = UnityEngine.GameObject.Find(n);
    if (go == null) return new[] { $"TARGET|{n}|MISSING" };
    var renderers = go.GetComponentsInChildren<UnityEngine.Renderer>(true);
    if (renderers.Length == 0) return new[] { $"TARGET|{n}|NO_RENDERER" };
    return System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(renderers, r =>
        $"TARGET|{n}|{r.GetType().Name}|{r.name}|bounds={r.bounds.center.ToString("F3")}/{r.bounds.size.ToString("F3")}|{string.Join(",", System.Linq.Enumerable.Select(r.sharedMaterials, m => m == null ? "null" : m.name + "@" + UnityEditor.AssetDatabase.GetAssetPath(m)))}"));
});
return string.Join("\n", new[] { $"SCENE|{scene.name}|{scene.path}|dirty={scene.isDirty}|playing={UnityEditor.EditorApplication.isPlaying}",
    "CAMERA_TRANSFORM|" + string.Join(";", cameraLines), "TARGET_RENDERERS|" + string.Join(";", rendererLines) });
