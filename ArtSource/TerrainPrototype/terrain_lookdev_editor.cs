UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate);
var root = "Assets/Art/Terrain/Prototype/";
var shader = UnityEngine.Shader.Find("Universal Render Pipeline/Lit");
if (shader == null) return "ERROR: URP/Lit shader is unavailable";
var surfaces = new[] { "Grass", "DirtPath" };
var palettes = new[] { "Warm", "Cool" };
var created = new System.Collections.Generic.List<string>();
foreach (var surface in surfaces) {
    var tile = surface == "Grass" ? new UnityEngine.Vector2(13f, 22.5f) : new UnityEngine.Vector2(1.275f, .75f);
    foreach (var palette in palettes) {
        var basePath = root + "Terrain_" + surface + "_" + palette + ".png";
        var normalPath = root + "Terrain_" + surface + "_Normal.png";
        foreach (var spec in new[] { new { Suffix = "Flat", Relief = false }, new { Suffix = "Relief", Relief = true } }) {
            var importBase = UnityEditor.AssetImporter.GetAtPath(basePath) as UnityEditor.TextureImporter;
            if (importBase == null) return "ERROR: base texture importer missing: " + basePath;
            importBase.textureType = UnityEditor.TextureImporterType.Default;
            importBase.sRGBTexture = true;
            importBase.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importBase.mipmapEnabled = true;
            importBase.filterMode = UnityEngine.FilterMode.Trilinear;
            importBase.maxTextureSize = 1024;
            importBase.SaveAndReimport();
            var baseTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(basePath);
            if (baseTexture == null) return "ERROR: failed to load base texture: " + basePath;
            UnityEngine.Texture2D normalTexture = null;
            if (spec.Relief) {
                var importNormal = UnityEditor.AssetImporter.GetAtPath(normalPath) as UnityEditor.TextureImporter;
                if (importNormal == null) return "ERROR: normal texture importer missing: " + normalPath;
                importNormal.textureType = UnityEditor.TextureImporterType.NormalMap;
                importNormal.sRGBTexture = false;
                importNormal.wrapMode = UnityEngine.TextureWrapMode.Repeat;
                importNormal.mipmapEnabled = true;
                importNormal.filterMode = UnityEngine.FilterMode.Trilinear;
                importNormal.maxTextureSize = 1024;
                importNormal.SaveAndReimport();
                normalTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(normalPath);
                if (normalTexture == null) return "ERROR: failed to load normal texture: " + normalPath;
            }
            var name = "Terrain_" + surface + "_" + palette + "_" + spec.Suffix;
            var materialPath = root + name + ".mat";
            var material = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(materialPath);
            if (material == null) {
                material = new UnityEngine.Material(shader);
                material.name = name;
                UnityEditor.AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", baseTexture);
            material.SetTextureScale("_BaseMap", tile);
            material.SetColor("_BaseColor", UnityEngine.Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0f);
            if (spec.Relief) {
                material.SetTexture("_BumpMap", normalTexture);
                material.SetTextureScale("_BumpMap", tile);
                material.SetFloat("_BumpScale", .30f);
                material.EnableKeyword("_NORMALMAP");
            } else {
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
                material.SetFloat("_BumpScale", .30f);
            }
            UnityEditor.EditorUtility.SetDirty(material);
            created.Add(materialPath);
        }
    }
}
UnityEditor.AssetDatabase.SaveAssets();
return "Created/configured " + created.Count + " URP Lit materials: " + string.Join(", ", created);



