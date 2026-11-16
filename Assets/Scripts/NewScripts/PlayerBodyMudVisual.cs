using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerTemporaryStatusController))]
public sealed class PlayerBodyMudVisual : MonoBehaviour
{
    private static readonly int MudAmountId = Shader.PropertyToID("_MudAmount");

    [SerializeField] private Mesh sourceMesh;
    [SerializeField] private Material sourceMaterial;
    [SerializeField] private Material mudMaterialTemplate;
    [SerializeField, Min(0)] private int sourceMaterialSlot;

    private PlayerTemporaryStatusController statuses;
    private GameObject visualRoot;
    private SkinnedMeshRenderer boundRenderer;
    private Material originalMaterial;
    private Material ownedMaterial;
    private int boundMaterialSlot = -1;
    private bool statusSubscribed;

    private void Awake()
    {
        TryGetComponent(out statuses);
    }

    private void OnEnable()
    {
        SubscribeToStatusChanges();
        if (visualRoot != null) BindVisual(visualRoot);
        ApplyMudAmount();
    }

    private void OnDisable()
    {
        UnsubscribeFromStatusChanges();
        ReleaseBoundMaterial();
    }

    private void OnDestroy()
    {
        UnsubscribeFromStatusChanges();
        ReleaseBoundMaterial();
    }

    /// <summary>Bind only the registered goblin visual whose mesh and source material match this authored setup.</summary>
    public void BindVisual(GameObject registeredVisual)
    {
        if (visualRoot != registeredVisual)
        {
            ReleaseBoundMaterial();
            visualRoot = registeredVisual;
        }

        if (!isActiveAndEnabled || visualRoot == null)
        {
            return;
        }

        if (boundRenderer == null && ownedMaterial != null)
        {
            // The previous renderer may have been destroyed while the registered root survived.
            ReleaseBoundMaterial();
        }

        if (boundRenderer == null && TryFindMatchingRenderer(visualRoot, out SkinnedMeshRenderer renderer, out int slot))
        {
            Material[] materials = renderer.sharedMaterials;
            originalMaterial = materials[slot];
            ownedMaterial = new Material(mudMaterialTemplate)
            {
                name = $"{mudMaterialTemplate.name} ({name})"
            };
            materials[slot] = ownedMaterial;
            renderer.sharedMaterials = materials;
            boundRenderer = renderer;
            boundMaterialSlot = slot;
        }

        SubscribeToStatusChanges();
        ApplyMudAmount();
    }

    private bool TryFindMatchingRenderer(GameObject visual, out SkinnedMeshRenderer renderer, out int slot)
    {
        renderer = null;
        slot = -1;
        if (sourceMesh == null || sourceMaterial == null || mudMaterialTemplate == null ||
            !mudMaterialTemplate.HasProperty(MudAmountId) || sourceMaterialSlot < 0 || visual == null)
        {
            return false;
        }

        foreach (SkinnedMeshRenderer candidate in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate.sharedMesh != sourceMesh)
            {
                continue;
            }

            Material[] materials = candidate.sharedMaterials;
            if (sourceMaterialSlot >= materials.Length || materials[sourceMaterialSlot] != sourceMaterial)
            {
                continue;
            }

            renderer = candidate;
            slot = sourceMaterialSlot;
            return true;
        }

        return false;
    }

    private void SubscribeToStatusChanges()
    {
        if (statuses == null) TryGetComponent(out statuses);
        if (statuses == null || statusSubscribed) return;
        statuses.StatusChanged += HandleStatusChanged;
        statusSubscribed = true;
    }

    private void UnsubscribeFromStatusChanges()
    {
        if (statuses != null && statusSubscribed) statuses.StatusChanged -= HandleStatusChanged;
        statusSubscribed = false;
    }

    private void HandleStatusChanged(PlayerTemporaryStatusSnapshot _) => ApplyMudAmount();

    private void ApplyMudAmount()
    {
        if (ownedMaterial == null) return;
        if (statuses == null) TryGetComponent(out statuses);
        ownedMaterial.SetFloat(MudAmountId, statuses != null ? Mathf.Clamp01(statuses.MudAmount) : 0f);
    }

    private void ReleaseBoundMaterial()
    {
        if (boundRenderer != null && ownedMaterial != null && boundMaterialSlot >= 0)
        {
            Material[] materials = boundRenderer.sharedMaterials;
            if (boundMaterialSlot < materials.Length && materials[boundMaterialSlot] == ownedMaterial)
            {
                materials[boundMaterialSlot] = originalMaterial;
                boundRenderer.sharedMaterials = materials;
            }
        }

        boundRenderer = null;
        boundMaterialSlot = -1;
        originalMaterial = null;
        if (ownedMaterial != null)
        {
            if (Application.isPlaying) Destroy(ownedMaterial);
            else DestroyImmediate(ownedMaterial);
            ownedMaterial = null;
        }
    }
}
