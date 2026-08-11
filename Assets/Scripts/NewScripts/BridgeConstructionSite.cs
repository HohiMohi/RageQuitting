using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(BridgeComponent))]
public class BridgeConstructionSite : MonoBehaviour, IDamageable, IInteractionPromptProvider, ISubstanceSource
{
    [Header("Clearing")]
    [SerializeField] private bool requiresSiteClearing = true;
    [SerializeField] private Vector2 clearingAreaSize = new Vector2(2.4f, 2.4f);
    [SerializeField] private BaseResourceNew[] clearingObstacles;
    [SerializeField] private FlexibleSaplingController[] clearingSaplings;

    [Header("Stage visuals")]
    [SerializeField] private Collider constructionInteractionCollider;
    [SerializeField] private GameObject markedGroundVisual;
    [SerializeField] private GameObject diggingVisual;
    [SerializeField] private GameObject completedDigVisual;
    [SerializeField] private FoundationExcavationVolume excavationVolume;
    [SerializeField] private bool hideMountedVisualsWhenComplete;
    [SerializeField] private bool disablePhysicalCollidersWhenComplete;

    protected BridgeComponent bridgeComponent;
    protected BridgeConstructionWorkflowSO workflow;
    protected BridgeConstructionStage currentStage;
    protected float currentWorkProgress;
    private int diggingCycleIndex;
    private int removedSoilUnits;
    private FoundationDiggingSubstage diggingSubstage;
    private bool initialized;

    public virtual BridgeConstructionStage CurrentStage => currentStage;
    public bool RequiresSiteClearing => requiresSiteClearing;
    public Vector2 ClearingAreaSize => clearingAreaSize;
    public int RemainingObstacleCount => CountRemainingObstacles();
    public virtual float CurrentWorkProgress => currentWorkProgress;
    public virtual float RequiredWorkProgress => currentStage == BridgeConstructionStage.Digging && workflow != null
        ? workflow.LooseningProgressPerCycle
        : bridgeComponent != null ? bridgeComponent.GetAssemblingProgressNeeded() : 0f;
    public virtual bool CanAcceptMountedComponent => currentStage == BridgeConstructionStage.ReadyForMount;
    public BridgeComponent BridgeComponent => bridgeComponent;
    public FoundationDiggingSubstage DiggingSubstage => diggingSubstage;
    public int DiggingCycleIndex => diggingCycleIndex;
    public int RemovedSoilUnits => removedSoilUnits;
    public int RequiredSoilUnits => workflow != null ? workflow.SoilUnitsPerCycle : 0;

    protected virtual void Awake()
    {
        Initialize();
    }

    public virtual void Initialize()
    {
        if (initialized)
        {
            return;
        }

        bridgeComponent = GetComponent<BridgeComponent>();
        workflow = bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null
            ? bridgeComponent.GetBridgeComponentSO().constructionWorkflow
            : null;
        currentStage = GetInitialStage();
        diggingCycleIndex = 0;
        diggingSubstage = FoundationDiggingSubstage.Loosening;
        ApplyClearingAreaSize();
        if (constructionInteractionCollider != null)
        {
            constructionInteractionCollider.isTrigger = true;
        }
        ApplyVisualState();
        initialized = true;
    }

    private void Start()
    {
        RefreshClearingState();
    }

    private void Update()
    {
        if (currentStage != BridgeConstructionStage.Clearing || !HasAuthority())
        {
            return;
        }

        if (RefreshClearingState())
        {
            GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        }
    }

    public virtual bool TryApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        if (workPower <= 0f)
        {
            return false;
        }

        if (currentStage == BridgeConstructionStage.Digging)
        {
            if (workflow == null || diggingSubstage != FoundationDiggingSubstage.Loosening || toolType != workflow.DiggingTool)
            {
                return false;
            }

            currentWorkProgress = Mathf.Clamp(currentWorkProgress + workPower, 0f, workflow.LooseningProgressPerCycle);
            if (currentWorkProgress >= workflow.LooseningProgressPerCycle)
            {
                diggingSubstage = FoundationDiggingSubstage.SoilRemoval;
            }

            ApplyVisualState();
            bridgeComponent.RefreshVisualAndColliderState();
            return true;
        }

        return false;
    }

    public virtual bool CanApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        return workPower > 0f &&
               currentStage == BridgeConstructionStage.Digging &&
               diggingSubstage == FoundationDiggingSubstage.Loosening &&
               workflow != null &&
               toolType == workflow.DiggingTool;
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        if (equippableItemSO == null || currentStage != BridgeConstructionStage.Digging)
        {
            return;
        }

        GameplayManager.Instance?.RequestConstructionSiteWork(
            bridgeComponent,
            equippableItemSO,
            equippableItemSO.ConstructionWorkPower);
    }

    public void DamageReceived(float damage)
    {
    }

    public virtual void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        switch (currentStage)
        {
            case BridgeConstructionStage.Clearing:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                    $"Clear marked obstacles - remaining: {RemainingObstacleCount}"));
                break;
            case BridgeConstructionStage.Digging:
                int cycleDisplay = Mathf.Clamp(diggingCycleIndex + 1, 1, workflow != null ? workflow.DiggingCycleCount : 1);
                if (diggingSubstage == FoundationDiggingSubstage.Loosening)
                {
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                        $"Loosen soil - {Mathf.CeilToInt(currentWorkProgress)} / {Mathf.CeilToInt(RequiredWorkProgress)} (cycle {cycleDisplay} / {workflow.DiggingCycleCount})"));
                }
                else
                {
                    int cycleStart = diggingCycleIndex * workflow.SoilUnitsPerCycle;
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                        $"Remove soil - {Mathf.Max(0, removedSoilUnits - cycleStart)} / {workflow.SoilUnitsPerCycle} (cycle {cycleDisplay} / {workflow.DiggingCycleCount})"));
                }
                break;
            case BridgeConstructionStage.ReadyForMount:
                bridgeComponent.AddReadyForMountPrompt(prompts, "Deliver Wooden Foundation");
                break;
            case BridgeConstructionStage.Hammering:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                    $"Secure foundation - {Mathf.CeilToInt(currentWorkProgress)} / {Mathf.CeilToInt(RequiredWorkProgress)}"));
                break;
        }
    }

    public virtual void ApplyNetworkState(BridgeComponentNetworkState state)
    {
        currentStage = (BridgeConstructionStage)state.constructionStage;
        currentWorkProgress = Mathf.Max(0f, state.constructionProgress);
        diggingCycleIndex = Mathf.Max(0, state.constructionValueA);
        diggingSubstage = (FoundationDiggingSubstage)Mathf.Clamp(state.constructionValueB, 0, 1);
        removedSoilUnits = Mathf.Max(0, Mathf.RoundToInt(state.constructionAux0));
        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
    }

    public virtual void PopulateNetworkState(ref BridgeComponentNetworkState state)
    {
        state.constructionStage = (int)currentStage;
        state.constructionProgress = currentWorkProgress;
        state.constructionValueA = diggingCycleIndex;
        state.constructionValueB = (int)diggingSubstage;
        state.constructionAux0 = removedSoilUnits;
    }

    public virtual void NotifyMounted()
    {
        if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            currentStage = BridgeConstructionStage.Hammering;
            currentWorkProgress = 0f;
            ApplyVisualState();
        }
    }

    public virtual void NotifyAssembled()
    {
        currentStage = BridgeConstructionStage.Complete;
        currentWorkProgress = RequiredWorkProgress;
        ApplyVisualState();
        bridgeComponent?.RefreshVisualAndColliderState();
    }

    public virtual void ApplyAssemblyProgress(float progress)
    {
        if (currentStage != BridgeConstructionStage.Hammering)
        {
            return;
        }

        currentWorkProgress = Mathf.Clamp(progress, 0f, bridgeComponent.GetAssemblingProgressNeeded());
        ApplyVisualState();
    }

    public bool RefreshClearingState()
    {
        if (currentStage != BridgeConstructionStage.Clearing || CountRemainingObstacles() > 0)
        {
            return false;
        }

        currentStage = BridgeConstructionStage.Digging;
        currentWorkProgress = 0f;
        diggingCycleIndex = 0;
        diggingSubstage = FoundationDiggingSubstage.Loosening;
        removedSoilUnits = 0;
        ApplyVisualState();
        return true;
    }

    protected virtual BridgeConstructionStage GetInitialStage()
    {
        if (workflow == null)
        {
            return BridgeConstructionStage.ReadyForMount;
        }

        return requiresSiteClearing && CountRemainingObstacles() > 0
            ? BridgeConstructionStage.Clearing
            : BridgeConstructionStage.Digging;
    }

    private int CountRemainingObstacles()
    {
        int count = 0;
        if (clearingObstacles != null)
        {
            foreach (BaseResourceNew obstacle in clearingObstacles)
            {
                if (obstacle != null &&
                    obstacle.gameObject.activeInHierarchy &&
                    obstacle.CanBeDestroyedWith(EquippableItemType.Axe))
                {
                    count++;
                }
            }
        }

        if (clearingSaplings != null)
        {
            foreach (FlexibleSaplingController sapling in clearingSaplings)
            {
                if (sapling != null && sapling.gameObject.activeInHierarchy && !sapling.IsCleared)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private bool HasAuthority()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
    }

    protected virtual void ApplyVisualState()
    {
        if (constructionInteractionCollider != null)
        {
            constructionInteractionCollider.enabled = currentStage == BridgeConstructionStage.Clearing ||
                                                     currentStage == BridgeConstructionStage.Digging;
        }
        if (markedGroundVisual != null)
        {
            markedGroundVisual.SetActive(currentStage == BridgeConstructionStage.Clearing);
        }

        if (diggingVisual != null)
        {
            diggingVisual.SetActive(currentStage == BridgeConstructionStage.Digging);
        }

        excavationVolume?.ApplyDiggingState(currentStage, diggingSubstage, currentWorkProgress, removedSoilUnits, workflow);

        if (completedDigVisual != null)
        {
            completedDigVisual.SetActive(currentStage == BridgeConstructionStage.ReadyForMount);
        }
    }

    private void ApplyClearingAreaSize()
    {
        if (markedGroundVisual == null)
        {
            return;
        }

        Vector2 safeSize = new Vector2(Mathf.Max(0.1f, clearingAreaSize.x), Mathf.Max(0.1f, clearingAreaSize.y));
        markedGroundVisual.transform.localScale = new Vector3(safeSize.x / 2.4f, 1f, safeSize.y / 2.4f);
    }

    protected virtual void OnValidate()
    {
        clearingAreaSize.x = Mathf.Max(0.1f, clearingAreaSize.x);
        clearingAreaSize.y = Mathf.Max(0.1f, clearingAreaSize.y);
        ApplyClearingAreaSize();
    }

    public bool IsConstructionInteractionCollider(Collider candidate)
    {
        return candidate != null && candidate == constructionInteractionCollider;
    }

    public Vector3 GetClosestInteractionPoint(Vector3 position)
    {
        return constructionInteractionCollider != null
            ? constructionInteractionCollider.ClosestPoint(position)
            : transform.position;
    }

    public virtual bool ShouldEnablePhysicalColliders(bool isMounted)
    {
        return isMounted && (!disablePhysicalCollidersWhenComplete || currentStage != BridgeConstructionStage.Complete);
    }

    public virtual bool ShouldShowMountedComponentVisuals(bool isMounted)
    {
        return isMounted && (!hideMountedVisualsWhenComplete || currentStage != BridgeConstructionStage.Complete);
    }

    public virtual MonoBehaviour GetWorkValidationTarget(int workPointId)
    {
        return this;
    }

    public bool TryExtractSoil(int units, ContainerSubstanceSO substance)
    {
        if (!HasAuthority() || units <= 0 || substance == null || !substance.IsSoil || workflow == null ||
            currentStage != BridgeConstructionStage.Digging || diggingSubstage != FoundationDiggingSubstage.SoilRemoval)
        {
            return false;
        }

        int target = (diggingCycleIndex + 1) * workflow.SoilUnitsPerCycle;
        if (removedSoilUnits >= target)
        {
            return false;
        }

        removedSoilUnits = Mathf.Min(target, removedSoilUnits + units);
        if (removedSoilUnits >= target)
        {
            diggingCycleIndex++;
            currentWorkProgress = 0f;
            if (diggingCycleIndex >= workflow.DiggingCycleCount)
            {
                currentStage = BridgeConstructionStage.ReadyForMount;
            }
            else
            {
                diggingSubstage = FoundationDiggingSubstage.Loosening;
            }
        }

        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        return true;
    }

    public bool CanExtract(ContainerSubstanceSO substance)
    {
        return substance != null && substance.IsSoil && currentStage == BridgeConstructionStage.Digging &&
               diggingSubstage == FoundationDiggingSubstage.SoilRemoval && workflow != null &&
               removedSoilUnits < (diggingCycleIndex + 1) * workflow.SoilUnitsPerCycle;
    }

    public bool TryExtract(ContainerSubstanceSO substance, int units)
    {
        return TryExtractSoil(units, substance);
    }

    public int ReturnSoil(int units)
    {
        if (!HasAuthority() || units <= 0 || workflow == null || currentStage == BridgeConstructionStage.Hammering ||
            currentStage == BridgeConstructionStage.Complete || removedSoilUnits <= 0)
        {
            return 0;
        }

        int totalSoilUnits = workflow.DiggingCycleCount * workflow.SoilUnitsPerCycle;
        removedSoilUnits = Mathf.Clamp(removedSoilUnits, 0, totalSoilUnits);
        int accepted = Mathf.Min(units, removedSoilUnits);
        removedSoilUnits = Mathf.Clamp(removedSoilUnits - accepted, 0, totalSoilUnits);
        if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            currentStage = BridgeConstructionStage.Digging;
            diggingCycleIndex = workflow.DiggingCycleCount - 1;
            diggingSubstage = FoundationDiggingSubstage.SoilRemoval;
            currentWorkProgress = workflow.LooseningProgressPerCycle;
        }

        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        return accepted;
    }
}
