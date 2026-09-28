if (!UnityEditor.EditorApplication.isPlaying) throw new System.InvalidOperationException("DeepBump smoke requires Play Mode.");
var player = UnityEngine.GameObject.Find("PlayerNew");
var terrain = UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
var switcher = terrain == null ? null : terrain.GetComponent<RageQuitting.Lookdev.TerrainRoadV3.TerrainRoadV3RuntimeSwitcher>();
var collider = terrain == null ? null : terrain.GetComponent<UnityEngine.TerrainCollider>();
if (player == null || switcher == null || collider == null) throw new System.InvalidOperationException("Lookdev player, terrain, switcher, or collider missing.");
var currentVariantProperty = switcher.GetType().GetProperty("CurrentVariant");
var templateField = switcher.GetType().GetField("deepBumpTemplateLayers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var sourceField = switcher.GetType().GetField("sourceLayers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var originalDataField = switcher.GetType().GetField("originalData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var originalColliderField = switcher.GetType().GetField("originalColliderData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var normalAField = switcher.GetType().GetField("normalA", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var normalBField = switcher.GetType().GetField("normalB", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var v4TemplateField = switcher.GetType().GetField("v4TemplateLayers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
if (currentVariantProperty == null || templateField == null || sourceField == null || originalDataField == null || originalColliderField == null || normalAField == null || normalBField == null || v4TemplateField == null) throw new System.InvalidOperationException("Switcher runtime fields missing.");
var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("DeepBumpV3MixSmokeKeyboard");
var errors = new System.Collections.Generic.List<string>();
var completed = false;
var checks = new System.Collections.Generic.List<string>();
var failed = "";
UnityEngine.Application.LogCallback onLog = (message, stack, kind) => { if (kind == UnityEngine.LogType.Error || kind == UnityEngine.LogType.Exception || kind == UnityEngine.LogType.Assert) errors.Add(kind + ":" + message); };
UnityEngine.Application.logMessageReceived += onLog;
System.Action<bool, string> check = (condition, label) => { if (!condition) { if (failed.Length == 0) failed = "FAILED: " + label; } else checks.Add(label); };
System.Collections.IEnumerator Press(UnityEngine.InputSystem.Key key) {
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(key));
    yield return null; yield return null;
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
    yield return null;
}
System.Collections.IEnumerator Smoke() {
    var originalData = (UnityEngine.TerrainData)originalDataField.GetValue(switcher);
    var originalColliderData = (UnityEngine.TerrainData)originalColliderField.GetValue(switcher);
    var runtimeData = terrain.terrainData;
    var runtimeLayers = (UnityEngine.TerrainLayer[])switcher.GetType().GetProperty("RuntimeLayers").GetValue(switcher, null);
    var templates = (UnityEngine.TerrainLayer[])templateField.GetValue(switcher);
    var v4Templates = (UnityEngine.TerrainLayer[])v4TemplateField.GetValue(switcher);
    var normalA = (UnityEngine.Texture2D)normalAField.GetValue(switcher);
    var normalB = (UnityEngine.Texture2D)normalBField.GetValue(switcher);
    var sourceLayers = (UnityEngine.TerrainLayer[])sourceField.GetValue(switcher);
    var albedo = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>("Assets/Art/Environment/TerrainRoadLookdev/SurfaceDeepBumpV3Mix/Road_Albedo_V1.png");
    var normal = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>("Assets/Art/Environment/TerrainRoadLookdev/SurfaceDeepBumpV3Mix/Road_Normal_DeepBump_V2.png");
    var mask = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>("Assets/Art/Environment/TerrainRoadLookdev/SurfaceDeepBumpV3Mix/Road_Constant_Mask.png");
    try {
        yield return null; yield return null;
        check(currentVariantProperty.GetValue(switcher, null).ToString() == "V3B", "Play starts on V3B");
        check(runtimeData != originalData && collider.terrainData == runtimeData, "runtime TerrainData clone paired with collider");
        check(runtimeLayers != null && runtimeLayers.Length == 3, "three runtime TerrainLayer copies");
        yield return Press(UnityEngine.InputSystem.Key.F9);
        check(currentVariantProperty.GetValue(switcher, null).ToString() == "DeepBump", "F9 selects DeepBump through Input System");
        check(terrain.terrainData == runtimeData && collider.terrainData == runtimeData, "F9 keeps runtime TerrainData and collider paired");
        for (var i = 0; i < 3; i++) {
            var layer = runtimeLayers[i];
            check(layer.diffuseTexture == albedo && layer.normalMapTexture == normal && layer.maskMapTexture == mask, "F9 maps assigned on road copy " + i);
            check(layer.tileSize == new UnityEngine.Vector2(4f,4f) && layer.tileOffset == UnityEngine.Vector2.zero && layer.normalScale == 1f, "F9 4m repeat and normal scale on road copy " + i);
            check(layer.metallic == 0f && layer.smoothness == 0f && layer.specular == UnityEngine.Color.clear, "F9 neutral specular response on road copy " + i);
        }
        check(templates != null && templates.Length == 3 && templates[0] != templates[1] && templates[1] != templates[2] && templates[0] != templates[2], "three distinct serialized F9 templates");
        var screenshotPath = System.IO.Path.GetFullPath("Artifacts/DeepBumpF9/deepbump_f9_player_view.png");
        UnityEngine.ScreenCapture.CaptureScreenshot(screenshotPath);
        for (var i = 0; i < 6; i++) yield return null;
        yield return Press(UnityEngine.InputSystem.Key.F9);
        check(currentVariantProperty.GetValue(switcher, null).ToString() == "DeepBump", "repeated F9 retains DeepBump");
        check(terrain.terrainData == runtimeData && switcher.GetType().GetProperty("RuntimeLayers").GetValue(switcher, null) == runtimeLayers, "repeated F9 reuses the same runtime clones");
        yield return Press(UnityEngine.InputSystem.Key.F8);
        check(currentVariantProperty.GetValue(switcher, null).ToString() == "V4", "F8 still selects RoadBands V1.1");
        check(terrain.terrainData == runtimeData && collider.terrainData == runtimeData, "F8 keeps runtime TerrainData and collider paired");
        for (var i = 0; i < 3; i++) check(runtimeLayers[i].diffuseTexture == v4Templates[i].diffuseTexture && runtimeLayers[i].normalMapTexture == v4Templates[i].normalMapTexture && runtimeLayers[i].maskMapTexture == v4Templates[i].maskMapTexture && runtimeLayers[i].tileSize == v4Templates[i].tileSize && runtimeLayers[i].tileOffset == v4Templates[i].tileOffset, "F8 applies RoadBands maps and properties on road copy " + i);
        yield return Press(UnityEngine.InputSystem.Key.F6);
        check(currentVariantProperty.GetValue(switcher, null).ToString() == "V3A", "F6 still selects V3A");
        for (var i = 0; i < 3; i++) check(runtimeLayers[i].diffuseTexture == sourceLayers[i].diffuseTexture && runtimeLayers[i].normalMapTexture == normalA && runtimeLayers[i].maskMapTexture == sourceLayers[i].maskMapTexture, "F6 restores V3 maps on road copy " + i);
        yield return Press(UnityEngine.InputSystem.Key.F7);
        check(currentVariantProperty.GetValue(switcher, null).ToString() == "V3B", "F7 still returns to default V3B");
        for (var i = 0; i < 3; i++) check(runtimeLayers[i].diffuseTexture == sourceLayers[i].diffuseTexture && runtimeLayers[i].normalMapTexture == normalB && runtimeLayers[i].maskMapTexture == sourceLayers[i].maskMapTexture, "F7 restores full V3 source maps on road copy " + i);
        switcher.enabled = false;
        yield return null; yield return null;
        check(terrain.terrainData == originalData && collider.terrainData == originalColliderData, "disable restores original Terrain and collider references");
        check(runtimeData == null && runtimeLayers[0] == null && runtimeLayers[1] == null && runtimeLayers[2] == null, "disable destroys owned runtime copies");
        switcher.enabled = true;
        yield return null; yield return null;
        check(currentVariantProperty.GetValue(switcher, null).ToString() == "V3B", "re-enable defaults to V3B");
        check(terrain.terrainData != originalData && collider.terrainData == terrain.terrainData, "re-enable creates and pairs fresh runtime TerrainData");
        completed = true;
    }
    finally {
        UnityEngine.Application.logMessageReceived -= onLog;
        try { UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState()); UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard); } catch { }
        if (switcher != null && !switcher.enabled) switcher.enabled = true;
        var passed = completed && failed.Length == 0 && errors.Count == 0;
        System.Func<string, string> jsonEscape = value => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        var errorJson = errors.Count == 0 ? "[]" : "[\"" + string.Join("\",\"", errors.ConvertAll(new System.Converter<string, string>(jsonEscape)).ToArray()) + "\"]";
        var checkJson = checks.Count == 0 ? "[]" : "[\"" + string.Join("\",\"", checks.ConvertAll(new System.Converter<string, string>(jsonEscape)).ToArray()) + "\"]";
        var report = "{\"passed\":" + passed.ToString().ToLowerInvariant() + ",\"completed\":" + completed.ToString().ToLowerInvariant() + ",\"mode\":\"InputSystem KeyboardState queued across PlayerLoop coroutine frames\",\"checks\":" + checkJson + ",\"checkCount\":" + checks.Count + ",\"consoleErrors\":" + errorJson + ",\"failure\":\"" + jsonEscape(failed) + "\",\"screenshot\":\"Artifacts/DeepBumpF9/deepbump_f9_player_view.png\"}";
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath("Artifacts/DeepBumpF9/runtime-smoke.json"), report, new System.Text.UTF8Encoding(false));
        if (!passed) UnityEngine.Debug.LogError("DeepBump runtime smoke failed: " + report); else UnityEngine.Debug.Log("DeepBump runtime smoke passed: " + report);
    }
}
switcher.StartCoroutine(Smoke());
System.IO.File.WriteAllText(System.IO.Path.GetFullPath("Artifacts/DeepBumpF9/runtime-smoke.json"), "{\"status\":\"running\"}", new System.Text.UTF8Encoding(false));




