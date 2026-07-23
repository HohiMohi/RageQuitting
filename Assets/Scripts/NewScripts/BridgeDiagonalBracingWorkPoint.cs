using System.Collections.Generic;
using UnityEngine;

public enum BridgeDiagonalBracingWorkPointId
{
    RotateCounterClockwise = 0,
    RotateClockwise = 1,
    StartTemporaryFix = 10,
    EndTemporaryFix = 11,
    StartTop = 20,
    EndBottom = 21,
    StartBottom = 22,
    EndTop = 23
}

public class BridgeDiagonalBracingWorkPoint : MonoBehaviour, IInteractableNew, IDamageable, IInteractionPromptProvider
{
    [SerializeField] private BridgeDiagonalBracingWorkPointId workPointId;
    [Header("Active point feedback")]
    [SerializeField] private Renderer markerRenderer;
    [SerializeField] private Renderer activeIndicatorRenderer;
    [SerializeField] private Color activeColor = new Color(1f, 0.65f, 0.08f, 1f);
    [SerializeField, Min(0f)] private float pulseAmount = 0.18f;
    [SerializeField, Min(0f)] private float pulseSpeed = 4f;

    private BridgeDiagonalBracingConstructionSite site;
    private Material runtimeMarkerMaterial;
    private Color normalColor = Color.white;
    private Vector3 baseScale;
    private bool isHighlighted;

    public BridgeDiagonalBracingWorkPointId WorkPointId => workPointId;

    private void Awake()
    {
        site = GetComponentInParent<BridgeDiagonalBracingConstructionSite>();
        EnsureVisualInitialized();
    }

    private void Update()
    {
        if (!isHighlighted)
        {
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        transform.localScale = baseScale * pulse;
        ApplyMarkerColor(Color.Lerp(activeColor * 0.8f, activeColor * 1.25f, (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f));
    }

    private void OnDisable()
    {
        transform.localScale = baseScale == Vector3.zero ? Vector3.one : baseScale;
    }

    public void SetHighlighted(bool highlighted)
    {
        EnsureVisualInitialized();
        isHighlighted = highlighted;
        transform.localScale = baseScale;
        if (markerRenderer == null)
        {
            return;
        }

        ApplyMarkerColor(highlighted ? activeColor : normalColor);
        if (activeIndicatorRenderer != null)
        {
            activeIndicatorRenderer.gameObject.SetActive(highlighted);
        }
    }

    private void EnsureVisualInitialized()
    {
        markerRenderer ??= GetComponentInChildren<Renderer>(true);
        if (baseScale == Vector3.zero)
        {
            baseScale = transform.localScale;
        }

        if (runtimeMarkerMaterial == null && markerRenderer != null)
        {
            runtimeMarkerMaterial = markerRenderer.material;
            normalColor = GetMaterialColor(runtimeMarkerMaterial);
        }
    }

    private void ApplyMarkerColor(Color color)
    {
        if (runtimeMarkerMaterial == null)
        {
            return;
        }

        if (runtimeMarkerMaterial.HasProperty("_BaseColor"))
        {
            runtimeMarkerMaterial.SetColor("_BaseColor", color);
        }
        if (runtimeMarkerMaterial.HasProperty("_Color"))
        {
            runtimeMarkerMaterial.SetColor("_Color", color);
        }
        if (runtimeMarkerMaterial.HasProperty("_EmissionColor"))
        {
            runtimeMarkerMaterial.EnableKeyword("_EMISSION");
            runtimeMarkerMaterial.SetColor("_EmissionColor", isHighlighted ? color * 0.35f : Color.black);
        }
    }

    private static Color GetMaterialColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }
        if (material.HasProperty("_Color"))
        {
            return material.GetColor("_Color");
        }
        return Color.white;
    }

    private void OnDestroy()
    {
        if (runtimeMarkerMaterial != null)
        {
            Destroy(runtimeMarkerMaterial);
        }
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        if (site != null && equippableItemSO != null)
        {
            site.RequestToolWork(equippableItemSO, (int)workPointId);
        }
    }

    public void DamageReceived(float damage) { }
    public void Interact(Transform interactor) { }
    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        site?.GetWorkPointPrompts(workPointId, prompts);
    }
}
