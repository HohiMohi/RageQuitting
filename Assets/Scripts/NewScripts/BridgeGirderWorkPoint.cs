using System.Collections.Generic;
using UnityEngine;

public enum BridgeGirderWorkPointId
{
    StartWedge = 0,
    EndWedge = 1,
    LeftWedge = 2,
    RightWedge = 3,
    Fastener0 = 10,
    Fastener1 = 11,
    Fastener2 = 12,
    Fastener3 = 13
}

public class BridgeGirderWorkPoint : MonoBehaviour, IInteractableNew, IDamageable, IInteractionPromptProvider,
    ILevelingConfirmationSource
{
    [SerializeField] private BridgeGirderWorkPointId workPointId;
    [SerializeField] private bool usesLevelingRole;
    [SerializeField] private int pointInstanceId = -1;
    [SerializeField] private BridgeLevelingAdjustmentRole levelingRole;
    private BridgeGirderConstructionSite site;

    public BridgeGirderWorkPointId WorkPointId => workPointId;
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
        site = GetComponentInParent<BridgeGirderConstructionSite>();
        if (IsLevelingPoint &&
            GetComponent<BridgeLevelingAdjustmentPointVisualizer>() == null)
        {
            gameObject.AddComponent<BridgeLevelingAdjustmentPointVisualizer>();
        }
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        if (site != null && equippableItemSO != null)
        {
            site.RequestToolWork(equippableItemSO, RequestId);
        }
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
        site?.GetWorkPointPrompts(RequestId, prompts);
    }

    private static BridgeLevelingAdjustmentRole LegacyRole(BridgeGirderWorkPointId id) => id switch
    {
        BridgeGirderWorkPointId.StartWedge => BridgeLevelingAdjustmentRole.LengthIncrease,
        BridgeGirderWorkPointId.EndWedge => BridgeLevelingAdjustmentRole.LengthDecrease,
        BridgeGirderWorkPointId.LeftWedge => BridgeLevelingAdjustmentRole.WidthIncrease,
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
