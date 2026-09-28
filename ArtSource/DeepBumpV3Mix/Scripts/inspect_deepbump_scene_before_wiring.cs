var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.path != "Assets/Scenes/TerrainRoad_Lookdev.unity" || scene.isDirty) throw new System.InvalidOperationException("Target lookdev scene is not the clean active scene.");
var go = UnityEngine.GameObject.Find("Terrain_RoadLookdev");
if (go == null) throw new System.InvalidOperationException("Terrain_RoadLookdev was not found.");
var terrain = go.GetComponent<UnityEngine.Terrain>();
var collider = go.GetComponent<UnityEngine.TerrainCollider>();
var switcher = go.GetComponent<RageQuitting.Lookdev.TerrainRoadV3.TerrainRoadV3RuntimeSwitcher>();
if (terrain == null || collider == null || switcher == null) throw new System.InvalidOperationException("Expected Terrain, TerrainCollider, or runtime switcher is missing.");
var serialized = new UnityEditor.SerializedObject(switcher);
var fields = new[] { "sourceLayers", "normalA", "normalB", "v4TemplateLayers", "keyA", "keyB", "keyV4", "deepBumpTemplateLayers", "keyDeepBump" };
var fieldData = new System.Collections.Generic.List<string>();
foreach (var field in fields) {
    var property = serialized.FindProperty(field);
    if (property == null) throw new System.InvalidOperationException("Missing serialized field: " + field);
    if (property.isArray && property.propertyType == UnityEditor.SerializedPropertyType.Generic) {
        var values = new System.Collections.Generic.List<string>();
        for (var i = 0; i < property.arraySize; i++) values.Add(UnityEditor.AssetDatabase.GetAssetPath(property.GetArrayElementAtIndex(i).objectReferenceValue));
        fieldData.Add("\"" + field + "\":[\"" + string.Join("\",\"", values.ToArray()) + "\"]");
    } else if (property.propertyType == UnityEditor.SerializedPropertyType.ObjectReference) {
        fieldData.Add("\"" + field + "\":\"" + UnityEditor.AssetDatabase.GetAssetPath(property.objectReferenceValue) + "\"");
    } else fieldData.Add("\"" + field + "\":\"" + property.intValue + "\"");
}
var layerNames = new System.Collections.Generic.List<string>();
foreach (var layer in terrain.terrainData.terrainLayers) layerNames.Add(layer == null ? "null" : layer.name);
var terrainMaterial = terrain.materialTemplate;
var report = "{\"scenePath\":\"" + scene.path + "\",\"sceneDirty\":false,\"terrainData\":\"" + terrain.terrainData.name + "\",\"terrainLayerStack\":[\"" + string.Join("\",\"", layerNames.ToArray()) + "\"],\"terrainMaterial\":\"" + (terrainMaterial == null ? "" : terrainMaterial.name) + "\",\"terrainColliderMatches\":" + (collider.terrainData == terrain.terrainData).ToString().ToLowerInvariant() + ",\"switcher\":{" + string.Join(",", fieldData.ToArray()) + "}}";
System.IO.Directory.CreateDirectory("Artifacts/DeepBumpF9");
System.IO.File.WriteAllText("Artifacts/DeepBumpF9/scene-before-wiring.json", report);
return report;

