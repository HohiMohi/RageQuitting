using System.Collections.Generic;
using UnityEngine;

public enum BridgeAbutmentWorkPointId
{
    LeftWedge = 0,
    RightWedge = 1,
    StartWedge = 2,
    EndWedge = 3,
    Anchor0 = 10,
    Anchor1 = 11,
    Anchor2 = 12,
    Anchor3 = 13,
    Backfill = 20
}

public class BridgeAbutmentWorkPoint : MonoBehaviour, IInteractableNew, IDamageable, IInteractionPromptProvider,
    ILevelingConfirmationSource
{
    [SerializeField] private BridgeAbutmentWorkPointId workPointId;
    [SerializeField] private bool usesLevelingRole;
    [SerializeField] private int pointInstanceId = -1;
    [SerializeField] private BridgeLevelingAdjustmentRole levelingRole;
    private BridgeAbutmentConstructionSite site;

    public BridgeAbutmentWorkPointId WorkPointId => workPointId;
    public bool IsLevelingPoint => usesLevelingRole || (int)workPointId is >= 0 and <= 3;
    public int RequestId => usesLevelingRole ? pointInstanceId : (int)workPointId;
    public BridgeLevelingAdjustmentRole LevelingRole => usesLevelingRole ? levelingRole : LegacyRole(workPointId);
    public BridgeConstructionSite ConfirmationSite => site;
    public LevelingConfirmationSourceType ConfirmationSourceType => LevelingConfirmationSourceType.AdjustmentPoint;
    public int ConfirmationPointId => RequestId;
    public Collider ConfirmationCollider => GetComponent<Collider>();
    public bool IsLevelingConfirmationAvailable => site != null && site.IsLevelingActive &&
                                                   IsLevelingPoint && gameObject.activeInHierarchy;

    private void Awake()
    {
        site = GetComponentInParent<BridgeAbutmentConstructionSite>();
        if (IsLevelingPoint &&
            GetComponent<BridgeLevelingAdjustmentPointVisualizer>() == null)
        {
            gameObject.AddComponent<BridgeLevelingAdjustmentPointVisualizer>();
        }
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        if (site == null || equippableItemSO == null)
        {
            return;
        }

        site.RequestToolWork(equippableItemSO, RequestId);
    }

    public void DamageReceived(float damage) { }
    public void Interact(Transform interactor)
    {
        site?.RequestLevelingConfirmation(interactor, ConfirmationSourceType, ConfirmationPointId);
    }
    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (site != null)
        {
            site.GetWorkPointPrompts(RequestId, prompts);
        }
    }

    private static BridgeLevelingAdjustmentRole LegacyRole(BridgeAbutmentWorkPointId id) => id switch
    {
        BridgeAbutmentWorkPointId.StartWedge => BridgeLevelingAdjustmentRole.LengthIncrease,
        BridgeAbutmentWorkPointId.EndWedge => BridgeLevelingAdjustmentRole.LengthDecrease,
        BridgeAbutmentWorkPointId.LeftWedge => BridgeLevelingAdjustmentRole.WidthIncrease,
        _ => BridgeLevelingAdjustmentRole.WidthDecrease
    };

#if UNITY_EDITOR
    public void ConfigureLevelingPointEditor(int instanceId, BridgeLevelingAdjustmentRole role)
    {
        usesLevelingRole = true;
        pointInstanceId = instanceId;
        levelingRole = role;
    }
#endif
}
