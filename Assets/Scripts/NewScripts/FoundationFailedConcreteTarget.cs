using System.Collections.Generic;
using UnityEngine;

public sealed class FoundationFailedConcreteTarget : MonoBehaviour, IInteractableNew, IDamageable,
    IInteractionPromptProvider, IActionImpactSurfaceProvider
{
    public const int WorkPointId = 9000;

    [SerializeField] private BridgeConstructionSite constructionSite;
    [SerializeField] private Collider interactionCollider;

    public BridgeConstructionSite ConstructionSite =>
        constructionSite != null ? constructionSite : constructionSite = GetComponentInParent<BridgeConstructionSite>();
    public Collider InteractionCollider =>
        interactionCollider != null ? interactionCollider : interactionCollider = GetComponent<Collider>();
    public ActionImpactSurfaceType ImpactSurfaceType => ActionImpactSurfaceType.Stone;

    private void Awake()
    {
        constructionSite ??= GetComponentInParent<BridgeConstructionSite>();
        interactionCollider ??= GetComponent<Collider>();
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        if (equippableItemSO == null || ConstructionSite == null) return;
        GameplayManager.Instance?.RequestConstructionSiteWork(
            ConstructionSite.BridgeComponent,
            equippableItemSO,
            equippableItemSO.ConstructionWorkPower,
            WorkPointId);
    }

    public void DamageReceived(float damage) { }
    public void Interact(Transform interactor) { }
    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (ConstructionSite == null || !ConstructionSite.CanBreakFailedConcrete) return;
        prompts.Add(new InteractionPrompt(
            PlayerInputActionKind.Action,
            $"Break hardened concrete: {Mathf.CeilToInt(ConstructionSite.FailedConcreteBreakProgress)} / " +
            $"{Mathf.CeilToInt(ConstructionSite.FailedConcreteWorkRequired)}"));
    }

#if UNITY_EDITOR
    public void ConfigureEditor(BridgeConstructionSite site, Collider targetCollider)
    {
        constructionSite = site;
        interactionCollider = targetCollider;
    }
#endif
}
