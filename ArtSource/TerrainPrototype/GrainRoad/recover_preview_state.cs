// Manual fallback only if capture_lookdev_preview.cs cannot finish its own finally block.
var ground = UnityEngine.GameObject.Find("Ground_West").GetComponent<UnityEngine.Renderer>();
var westMain = UnityEngine.GameObject.Find("West_Main").GetComponent<UnityEngine.Renderer>();
var road0 = UnityEngine.GameObject.Find("WestPath_0").GetComponent<UnityEngine.Renderer>();
var road1 = UnityEngine.GameObject.Find("WestPath_1").GetComponent<UnityEngine.Renderer>();
var grass = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Materials/Tutorial/Grass_Blockout.mat");
var path = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Materials/Tutorial/Path_Blockout.mat");
if (ground == null || westMain == null || road0 == null || road1 == null || grass == null || path == null)
    throw new System.InvalidOperationException("Recovery reference missing; no scene save was attempted.");
ground.sharedMaterials = new[] { grass };
westMain.sharedMaterials = new[] { grass };
road0.sharedMaterials = new[] { path };
road1.sharedMaterials = new[] { path };
var camera = UnityEngine.GameObject.Find("MainCamera").transform;
camera.localPosition = new UnityEngine.Vector3(-28.5f, 1.1f, -22.5f);
camera.localRotation = UnityEngine.Quaternion.Euler(0f, 45f, 0f);
camera.localScale = UnityEngine.Vector3.one;
return "Restored recorded Tutorial_scene target references and camera pose. Scene remains unsaved.";
