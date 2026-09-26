var strength = 1.0f;
var paths = new[] {
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainSparse_Relief.mat",
    "Assets/Art/Terrain/Prototype/Terrain_DirtPath_Warm_GrainDense_Relief.mat"
};
foreach (var path in paths)
{
    var material = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
    if (material == null) throw new System.InvalidOperationException("Missing new material " + path);
    material.SetFloat("_BumpScale", strength);
    UnityEditor.EditorUtility.SetDirty(material);
}
UnityEditor.AssetDatabase.SaveAssets();
return "Set both new URP Lit materials to supported BumpScale=1.0.";
