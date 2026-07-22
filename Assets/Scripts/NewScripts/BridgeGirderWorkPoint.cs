using System.Collections.Generic;
using UnityEngine;

public enum BridgeGirderWorkPointId
{
    StartWedge = 0,
    EndWedge = 1,
    Fastener0 = 10,
    Fastener1 = 11,
    Fastener2 = 12,
    Fastener3 = 13
}

public class BridgeGirderWorkPoint : MonoBehaviour, IInteractableNew, IDamageable, IInteractionPromptProvider
{
    [SerializeField] private BridgeGirderWorkPointId workPointId;
    private BridgeGirderConstructionSite site;

    public BridgeGirderWorkPointId WorkPointId => workPointId;

    private void Awake()
    {
        site = GetComponentInParent<BridgeGirderConstructionSite>();
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
