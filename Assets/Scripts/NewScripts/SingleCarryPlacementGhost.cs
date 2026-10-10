using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Renderer-only transparent preview. It copies no gameplay, physics or networking components.</summary>
public sealed class SingleCarryPlacementGhost
{
    private readonly GameObject root;
    private readonly Transform sourceRoot;
    private readonly Dictionary<Transform, Transform> visualTransforms = new Dictionary<Transform, Transform>();
    private readonly Dictionary<Renderer, Renderer> rendererCopies = new Dictionary<Renderer, Renderer>();
    private readonly Dictionary<Renderer, Material[]> sourceMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Material, Material> materialCopies = new Dictionary<Material, Material>();
    private readonly List<Mesh> ownedMeshes = new List<Mesh>();
    private readonly List<Material> ownedMaterials = new List<Material>();
    private const float ValidationTintStrength = 0.78f;
    private static readonly int ValidationColorId = Shader.PropertyToID("_ValidationColor");
    private static readonly int ValidationStrengthId = Shader.PropertyToID("_ValidationStrength");
    private static Shader previewShader;
    private int appliedValidity = -1;
    private float nextSourceAppearanceSync;
    private bool hasSyncedSourceAppearance;

    public GameObject Root => root;
    public int UnsupportedRendererCount { get; private set; }

    public SingleCarryPlacementGhost(GameObject source)
    {
        if (source == null) return;
        sourceRoot = source.transform;
        root = new GameObject("Single Carry Placement Preview");
        root.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        root.transform.localScale = source.transform.lossyScale;
        root.layer = source.layer;
        visualTransforms.Add(sourceRoot, root.transform);

        Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer sourceRenderer in renderers)
        {
            if (sourceRenderer == null || sourceRenderer is ParticleSystemRenderer) continue;
            Transform visualTransform = GetOrCreateVisualTransform(sourceRenderer.transform);
            if (sourceRenderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                Mesh mesh = new Mesh { name = skinned.sharedMesh.name + " Placement Preview" };
                skinned.BakeMesh(mesh);
                ownedMeshes.Add(mesh);
                MeshFilter filter = visualTransform.gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer copy = visualTransform.gameObject.AddComponent<MeshRenderer>();
                ConfigureRenderer(copy, skinned.sharedMaterials);
                rendererCopies.Add(sourceRenderer, copy);
                sourceMaterials.Add(sourceRenderer, (Material[])skinned.sharedMaterials.Clone());
            }
            else if (sourceRenderer is MeshRenderer meshRenderer
                && sourceRenderer.TryGetComponent(out MeshFilter sourceFilter) && sourceFilter.sharedMesh != null)
            {
                visualTransform.gameObject.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                MeshRenderer copy = visualTransform.gameObject.AddComponent<MeshRenderer>();
                ConfigureRenderer(copy, meshRenderer.sharedMaterials);
                rendererCopies.Add(sourceRenderer, copy);
                sourceMaterials.Add(sourceRenderer, (Material[])meshRenderer.sharedMaterials.Clone());
            }
            else
            {
                UnsupportedRendererCount++;
            }
        }
        SetVisible(true);
        SyncSourceAppearance();
    }

    public void SetPose(Vector3 position, Quaternion rotation)
    {
        if (root != null) root.transform.SetPositionAndRotation(position, rotation);
    }

    public void SetPosition(Vector3 position)
    {
        if (root != null) root.transform.position = position;
    }

    public void SetWorldRenderLayer(int layer)
    {
        if (root == null || layer < 0 || layer > 31) return;
        root.layer = layer;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
    }

    public void SetVisible(bool visible)
    {
        if (root != null) root.SetActive(visible);
    }

    public void SetPlacementValid(bool isValid)
    {
        int nextState = isValid ? 1 : 0;
        if (appliedValidity == nextState) return;
        appliedValidity = nextState;
        foreach (Material material in materialCopies.Values)
            ApplyValidationTint(material, isValid);
    }

    /// <summary>Tracks replicated child activation and renderer state without copying source suppression.</summary>
    public void SyncSourceAppearance()
    {
        if (sourceRoot == null || root == null) return;
        if (hasSyncedSourceAppearance && Time.unscaledTime < nextSourceAppearanceSync) return;
        hasSyncedSourceAppearance = true;
        nextSourceAppearanceSync = Time.unscaledTime + 0.1f;
        foreach (KeyValuePair<Transform, Transform> pair in visualTransforms)
        {
            if (pair.Key == null || pair.Value == null || pair.Key == sourceRoot) continue;
            pair.Value.localPosition = pair.Key.localPosition;
            pair.Value.localRotation = pair.Key.localRotation;
            pair.Value.localScale = pair.Key.localScale;
            if (pair.Key.gameObject.activeSelf != pair.Value.gameObject.activeSelf)
                pair.Value.gameObject.SetActive(pair.Key.gameObject.activeSelf);
        }
        foreach (KeyValuePair<Renderer, Renderer> pair in rendererCopies)
        {
            if (pair.Key == null || pair.Value == null) continue;
            pair.Value.enabled = pair.Key.enabled;
            if (pair.Key is SkinnedMeshRenderer sourceSkinned
                && pair.Value.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                sourceSkinned.BakeMesh(filter.sharedMesh);
            else if (pair.Key.TryGetComponent(out MeshFilter sourceFilter)
                && pair.Value.TryGetComponent(out MeshFilter copyFilter))
                copyFilter.sharedMesh = sourceFilter.sharedMesh;

            Material[] currentSources = pair.Key.sharedMaterials;
            Material[] previousSources = sourceMaterials[pair.Key];
            bool materialsChanged = currentSources.Length != previousSources.Length;
            if (!materialsChanged)
                for (int i = 0; i < currentSources.Length; i++)
                    if (currentSources[i] != previousSources[i]) { materialsChanged = true; break; }
            if (materialsChanged)
            {
                sourceMaterials[pair.Key] = (Material[])currentSources.Clone();
                pair.Value.sharedMaterials = CreatePreviewMaterials(currentSources);
            }
            Material[] previews = pair.Value.sharedMaterials;
            for (int i = 0; i < Mathf.Min(currentSources.Length, previews.Length); i++)
                SyncPreviewMaterial(currentSources[i], previews[i]);
        }
    }

    public void Dispose()
    {
        if (root != null) DestroyOwnedObject(root);
        foreach (Mesh mesh in ownedMeshes)
            if (mesh != null) DestroyOwnedObject(mesh);
        ownedMeshes.Clear();
        foreach (Material material in ownedMaterials)
            if (material != null) DestroyOwnedObject(material);
        ownedMaterials.Clear();
        visualTransforms.Clear();
        rendererCopies.Clear();
        sourceMaterials.Clear();
        materialCopies.Clear();
    }

    private Transform GetOrCreateVisualTransform(Transform sourceTransform)
    {
        if (visualTransforms.TryGetValue(sourceTransform, out Transform existing)) return existing;
        Transform sourceParent = sourceTransform.parent;
        Transform visualParent = sourceParent == null || sourceParent == sourceRoot
            ? root.transform : GetOrCreateVisualTransform(sourceParent);
        GameObject visualObject = new GameObject(sourceTransform.gameObject.name + " Placement Preview");
        Transform visual = visualObject.transform;
        visual.SetParent(visualParent, false);
        visual.localPosition = sourceTransform.localPosition;
        visual.localRotation = sourceTransform.localRotation;
        visual.localScale = sourceTransform.localScale;
        visualObject.layer = sourceTransform.gameObject.layer;
        visualTransforms.Add(sourceTransform, visual);
        return visual;
    }

    private void ConfigureRenderer(MeshRenderer renderer, Material[] sourceMaterials)
    {
        renderer.sharedMaterials = CreatePreviewMaterials(sourceMaterials);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private Material[] CreatePreviewMaterials(Material[] sourceMaterials)
    {
        Material[] result = new Material[sourceMaterials.Length];
        for (int i = 0; i < sourceMaterials.Length; i++) result[i] = GetPreviewMaterial(sourceMaterials[i]);
        return result;
    }

    private Material GetPreviewMaterial(Material source)
    {
        if (source == null) return null;
        if (materialCopies.TryGetValue(source, out Material existing)) return existing;
        if (previewShader == null) previewShader = Resources.Load<Shader>("SingleCarryPlacementGhost");
        if (previewShader == null)
        {
            Debug.LogError("Missing Resources/SingleCarryPlacementGhost shader; placement preview cannot be rendered.");
            return null;
        }

        Material preview = new Material(previewShader) { name = source.name + " Placement Preview" };
        SyncPreviewMaterial(source, preview);
        if (appliedValidity >= 0) ApplyValidationTint(preview, appliedValidity == 1);
        ownedMaterials.Add(preview);
        materialCopies.Add(source, preview);
        return preview;
    }

    private static void SyncPreviewMaterial(Material source, Material preview)
    {
        if (source == null || preview == null) return;
        Color sourceColor = GetMaterialColor(source);
        Color previewColor = Color.Lerp(sourceColor, new Color(0.72f, 0.74f, 0.76f, 1f), 0.18f);
        previewColor.a = Mathf.Min(sourceColor.a, 0.52f);
        preview.SetColor("_BaseColor", previewColor);
        preview.SetFloat("_SourceAlpha", sourceColor.a);
        preview.SetFloat("_PreviewOpacity", 0.52f);

        Texture texture = GetMaterialTexture(source);
        preview.SetTexture("_BaseMap", texture);
        string property = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
        preview.SetTextureScale("_BaseMap", source.HasProperty(property) ? source.GetTextureScale(property) : Vector2.one);
        preview.SetTextureOffset("_BaseMap", source.HasProperty(property) ? source.GetTextureOffset(property) : Vector2.zero);
        if (source.HasProperty("_Cull")) preview.SetFloat("_Cull", source.GetFloat("_Cull"));
        if (source.HasProperty("_AlphaClip")) preview.SetFloat("_AlphaClip", source.GetFloat("_AlphaClip"));
        if (source.HasProperty("_Cutoff")) preview.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
    }

    private static void ApplyValidationTint(Material material, bool isValid)
    {
        if (material == null) return;
        Color color = isValid ? new Color(0.08f, 1f, 0.22f, 1f) : new Color(1f, 0.08f, 0.06f, 1f);
        material.SetColor(ValidationColorId, color);
        material.SetFloat(ValidationStrengthId, ValidationTintStrength);
    }

    private static Color GetMaterialColor(Material material)
    {
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        return Color.white;
    }

    private static Texture GetMaterialTexture(Material material)
    {
        if (material.HasProperty("_BaseMap")) return material.GetTexture("_BaseMap");
        if (material.HasProperty("_MainTex")) return material.GetTexture("_MainTex");
        return null;
    }

    private static void DestroyOwnedObject(Object ownedObject)
    {
        if (Application.isPlaying) Object.Destroy(ownedObject);
        else Object.DestroyImmediate(ownedObject);
    }
}
