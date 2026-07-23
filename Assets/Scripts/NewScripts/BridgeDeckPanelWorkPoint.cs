using System.Collections.Generic;
using UnityEngine;

public enum BridgeDeckPanelWorkPointId
{
    StrikeLeft = 0,
    StrikeRight = 1,
    StrikeClockwiseSide = 2,
    StrikeCounterClockwiseSide = 3,
    LeftGap = 10,
    RightGap = 11,
    FrontLeft = 20,
    BackRight = 21,
    FrontRight = 22,
    BackLeft = 23
}

public class BridgeDeckPanelWorkPoint : MonoBehaviour, IInteractableNew, IDamageable, IInteractionPromptProvider
{
    [SerializeField] private BridgeDeckPanelWorkPointId workPointId;
    [SerializeField] private Renderer markerRenderer;
    [SerializeField] private Transform markerVisual;
    [SerializeField] private Color availableColor = new Color(0.1f, 0.75f, 1f, 1f);
    [SerializeField] private Color activeColor = new Color(1f, 0.65f, 0.08f, 1f);
    [SerializeField, Min(0f)] private float pulseAmount = 0.18f;
    [SerializeField, Min(0f)] private float pulseSpeed = 4f;

    private BridgeDeckPanelConstructionSite site;
    private Material runtimeMaterial;
    private Color normalColor = Color.white;
    private Vector3 baseScale;
    private bool highlighted;

    public BridgeDeckPanelWorkPointId WorkPointId => workPointId;

    private void Awake()
    {
        site = GetComponentInParent<BridgeDeckPanelConstructionSite>();
        markerRenderer ??= GetComponentInChildren<Renderer>(true);
        markerVisual ??= markerRenderer != null ? markerRenderer.transform : transform;
        baseScale = markerVisual.localScale;
        if (markerRenderer != null)
        {
            runtimeMaterial = markerRenderer.material;
            normalColor = availableColor;
            SetColor(normalColor);
        }
    }

    private void Update()
    {
        if (!highlighted) return;
        float wave = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
        markerVisual.localScale = baseScale * (1f + (wave * 2f - 1f) * pulseAmount);
        SetColor(Color.Lerp(activeColor * 0.8f, activeColor * 1.2f, wave));
    }

    private void OnDisable()
    {
        if (markerVisual != null)
        {
            markerVisual.localScale = baseScale == Vector3.zero ? Vector3.one : baseScale;
        }
    }

    public void SetHighlighted(bool value)
    {
        highlighted = value;
        if (markerVisual != null)
        {
            markerVisual.localScale = baseScale;
        }
        SetColor(value ? activeColor : normalColor);
    }

    public void DamageReceived(EquippableItemSO item, float damage)
    {
        if (item != null) site?.RequestToolWork(item, (int)workPointId);
    }

    public void DamageReceived(float damage) { }
    public void Interact(Transform interactor) { }
    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        site?.GetWorkPointPrompts(workPointId, prompts);
    }

    private void SetColor(Color color)
    {
        if (runtimeMaterial == null) return;
        if (runtimeMaterial.HasProperty("_BaseColor")) runtimeMaterial.SetColor("_BaseColor", color);
        if (runtimeMaterial.HasProperty("_Color")) runtimeMaterial.SetColor("_Color", color);
        if (runtimeMaterial.HasProperty("_EmissionColor"))
        {
            runtimeMaterial.EnableKeyword("_EMISSION");
            runtimeMaterial.SetColor("_EmissionColor", highlighted ? color * 0.35f : Color.black);
        }
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }
}
