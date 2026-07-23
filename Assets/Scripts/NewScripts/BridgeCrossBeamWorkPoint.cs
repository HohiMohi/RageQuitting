using System.Collections.Generic;
using UnityEngine;

public enum BridgeCrossBeamWorkPointId
{
    MoveLeft = 0,
    MoveRight = 1,
    LeftClamp = 10,
    RightClamp = 11,
    Fastener0 = 20,
    Fastener1 = 21,
    Fastener2 = 22,
    Fastener3 = 23
}

public class BridgeCrossBeamWorkPoint : MonoBehaviour, IInteractableNew, IDamageable, IInteractionPromptProvider
{
    [SerializeField] private BridgeCrossBeamWorkPointId workPointId;
    private BridgeCrossBeamConstructionSite site;

    public BridgeCrossBeamWorkPointId WorkPointId => workPointId;

    private void Awake()
    {
        site = GetComponentInParent<BridgeCrossBeamConstructionSite>();
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
