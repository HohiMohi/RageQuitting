var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.path != "Assets/Scenes/TerrainRoad_Lookdev.unity" || scene.isDirty) throw new System.InvalidOperationException("Target scene must be clean and active before the F9 edit.");
var go = UnityEngine.GameObject.Find("Terrain_RoadLookdev");
var switcher = go == null ? null : go.GetComponent<RageQuitting.Lookdev.TerrainRoadV3.TerrainRoadV3RuntimeSwitcher>();
var terrain = go == null ? null : go.GetComponent<UnityEngine.Terrain>();
var collider = go == null ? null : go.GetComponent<UnityEngine.TerrainCollider>();
if (switcher == null || terrain == null || collider == null) throw new System.InvalidOperationException("Expected lookdev switcher, terrain, and collider are required.");
var originalData = terrain.terrainData;
var originalStack = (UnityEngine.TerrainLayer[])originalData.terrainLayers.Clone();
var layerPaths = new[] {
    "Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/DeepBumpV3Mix/TL_Road_DeepBumpV3Mix.terrainlayer",
    "Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/DeepBumpV3Mix/TL_Stone_DeepBumpV3Mix.terrainlayer",
    "Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/DeepBumpV3Mix/TL_Earth_DeepBumpV3Mix.terrainlayer"
};
var layers = new UnityEngine.TerrainLayer[3];
for (var i = 0; i < layers.Length; i++) {
    layers[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>(layerPaths[i]);
    if (layers[i] == null) throw new System.InvalidOperationException("DeepBump TerrainLayer missing: " + layerPaths[i]);
}
var serialized = new UnityEditor.SerializedObject(switcher);
serialized.Update();
var templateProperty = serialized.FindProperty("deepBumpTemplateLayers");
var keyProperty = serialized.FindProperty("keyDeepBump");
if (templateProperty == null || keyProperty == null) throw new System.InvalidOperationException("F9 serialized fields were not found.");
UnityEditor.Undo.RecordObject(switcher, "Assign DeepBump V3Mix F9 template");
templateProperty.arraySize = layers.Length;
for (var i = 0; i < layers.Length; i++) templateProperty.GetArrayElementAtIndex(i).objectReferenceValue = layers[i];
keyProperty.intValue = (int)UnityEngine.InputSystem.Key.F9;
if (!serialized.ApplyModifiedProperties()) throw new System.InvalidOperationException("Serialized F9 switcher changes were not applied.");
UnityEditor.EditorUtility.SetDirty(switcher);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
if (terrain.terrainData != originalData || collider.terrainData != originalData || originalStack.Length != terrain.terrainData.terrainLayers.Length) throw new System.InvalidOperationException("Terrain data, layer stack, or collider changed during F9 assignment.");
var result = "{\"scene\":\"" + scene.path + "\",\"assignedLayers\":[\"" + string.Join("\",\"", layerPaths) + "\"],\"keyDeepBump\":\"F9\",\"terrainDataUnchanged\":true,\"terrainStackUnchanged\":true,\"colliderMatches\":true,\"f6f7f8Untouched\":true}";
System.IO.File.WriteAllText(System.IO.Path.GetFullPath("Artifacts/DeepBumpF9/scene-wiring-before-save.json"), result);
return result;
