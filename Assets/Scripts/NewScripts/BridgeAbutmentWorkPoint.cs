using System.Collections.Generic;
using UnityEngine;

public enum BridgeAbutmentWorkPointId
{
    LeftWedge = 0,
    RightWedge = 1,
    Anchor0 = 10,
    Anchor1 = 11,
    Anchor2 = 12,
    Anchor3 = 13,
    Backfill = 20
}

public class BridgeAbutmentWorkPoint : MonoBehaviour, IInteractableNew, IDamageable, IInteractionPromptProvider
{
    [SerializeField] private BridgeAbutmentWorkPointId workPointId;
    private BridgeAbutmentConstructionSite site;

    public BridgeAbutmentWorkPointId WorkPointId => workPointId;

    private void Awake()
    {
        site = GetComponentInParent<BridgeAbutmentConstructionSite>();
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        if (site == null || equippableItemSO == null)
        {
            return;
        }

        site.RequestToolWork(equippableItemSO, (int)workPointId);
    }

    public void DamageReceived(float damage) { }
    public void Interact(Transform interactor) { }
    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (site != null)
        {
            site.GetWorkPointPrompts(workPointId, prompts);
        }
    }
}
