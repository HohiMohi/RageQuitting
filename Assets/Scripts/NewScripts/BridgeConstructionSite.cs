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
    [SerializeField] private ConcretePouringProfileSO concretePouringProfile;
    [SerializeField] private bool hideMountedVisualsWhenComplete;
    [SerializeField] private bool disablePhysicalCollidersWhenComplete;

    protected BridgeComponent bridgeComponent;
    protected BridgeConstructionWorkflowSO workflow;
    protected BridgeConstructionStage currentStage;
    protected float currentWorkProgress;
    private int diggingCycleIndex;
    private int removedSoilUnits;
    private FoundationDiggingSubstage diggingSubstage;
    private float soilHardeningDeadline;
    private int concreteLoadsPoured;
    private float concreteDryingDeadline;
    private FoundationConcreteFailureState concreteFailureState;
    private float failedConcreteBreakProgress;
    private float failedConcreteCollapseDeadline;
    private WheelbarrowDockingStation recoveryStation;
    private WheelbarrowController recoveryWheelbarrow;
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
    public virtual bool AllowsStandardAssemblyWork => currentStage == BridgeConstructionStage.Hammering;
    public BridgeComponent BridgeComponent => bridgeComponent;
    public FoundationDiggingSubstage DiggingSubstage => diggingSubstage;
    public int DiggingCycleIndex => diggingCycleIndex;
    public int RemovedSoilUnits => removedSoilUnits;
    public int RequiredSoilUnits => workflow != null ? workflow.SoilUnitsPerCycle : 0;
    public bool IsSoilHardeningActive => currentStage == BridgeConstructionStage.Digging &&
                                         diggingSubstage == FoundationDiggingSubstage.SoilRemoval &&
                                         soilHardeningDeadline > 0f;
    public float RemainingSoilHardeningTime => IsSoilHardeningActive
        ? Mathf.Max(0f, soilHardeningDeadline - GetSynchronizedTime())
        : 0f;
    public int ConcreteLoadsPoured => concreteLoadsPoured;
    public int RequiredConcreteLoads => workflow != null ? workflow.RequiredConcreteLoads : 1;
    public bool IsConcreteDrying => currentStage == BridgeConstructionStage.ConcreteDrying && concreteDryingDeadline > 0f;
    public float RemainingConcreteDryingTime => IsConcreteDrying
        ? Mathf.Max(0f, concreteDryingDeadline - GetSynchronizedTime())
        : 0f;
    public FoundationConcreteFailureState ConcreteFailureState => concreteFailureState;
    public float FailedConcreteBreakProgress => failedConcreteBreakProgress;
    public float FailedConcreteWorkRequired => concretePouringProfile != null
        ? concretePouringProfile.FailedConcreteWorkRequired
        : 100f;
    public EquippableItemType FailedConcreteRequiredTool => concretePouringProfile != null
        ? concretePouringProfile.FailedConcreteRequiredTool
        : EquippableItemType.Pickaxe;
    public bool HasActiveConcreteFailure =>
        concreteFailureState != FoundationConcreteFailureState.None &&
        concreteFailureState != FoundationConcreteFailureState.Ready;
    public float FailedConcreteCollapseProgress
    {
        get
        {
            if (concreteFailureState != FoundationConcreteFailureState.Collapsing) return 0f;
            float duration = concretePouringProfile != null
                ? concretePouringProfile.FailedConcreteCollapseDuration
                : 0.4f;
            return 1f - Mathf.Clamp01((failedConcreteCollapseDeadline - GetSynchronizedTime()) /
                                      Mathf.Max(0.05f, duration));
        }
    }
    public bool CanBreakFailedConcrete =>
        currentStage == BridgeConstructionStage.ConcretePouring &&
        concreteFailureState == FoundationConcreteFailureState.HardenedFailure;
    public bool CanAcceptFoundationDock =>
        currentStage == BridgeConstructionStage.ConcretePouring &&
        concreteFailureState is FoundationConcreteFailureState.None or FoundationConcreteFailureState.Ready;
    public Transform FailedWheelbarrowPose => excavationVolume != null
        ? excavationVolume.FailedWheelbarrowPose
        : null;

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
        concreteFailureState = currentStage == BridgeConstructionStage.ConcretePouring
            ? FoundationConcreteFailureState.Ready
            : FoundationConcreteFailureState.None;
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
        if (IsConcreteDrying)
        {
            excavationVolume?.ApplyConcreteState(currentStage, concreteLoadsPoured, RequiredConcreteLoads,
                RemainingConcreteDryingTime, workflow != null ? workflow.ConcreteDryingDuration : 30f);
        }

        if (concreteFailureState != FoundationConcreteFailureState.None &&
            concreteFailureState != FoundationConcreteFailureState.Ready)
        {
            ResolveRecoveryReferences();
            if (concreteFailureState == FoundationConcreteFailureState.Collapsing)
                ApplyConcreteVisualState();
        }

        if (!HasAuthority())
        {
            return;
        }

        if (currentStage == BridgeConstructionStage.Clearing && RefreshClearingState())
        {
            GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
            return;
        }

        if (IsSoilHardeningActive && RemainingSoilHardeningTime <= 0f)
        {
            HardenLoosenedSoil();
        }

        if (IsConcreteDrying && RemainingConcreteDryingTime <= 0f)
        {
            CompleteConcreteDrying();
        }

        if (HasActiveConcreteFailure && !HasValidRecoveryTracking())
        {
            CleanupMissingRecoveryWheelbarrow();
            return;
        }

        if (concreteFailureState == FoundationConcreteFailureState.Collapsing &&
            GetSynchronizedTime() >= failedConcreteCollapseDeadline)
        {
            CompleteFailedConcreteCollapse();
        }
        else if (concreteFailureState == FoundationConcreteFailureState.AwaitingWheelbarrowExit)
        {
            TryCompleteFailedConcreteRecovery();
        }
    }

    public virtual bool TryApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        if (workPower <= 0f)
        {
            return false;
        }

        if (CanBreakFailedConcrete && workPointId == FoundationFailedConcreteTarget.WorkPointId)
        {
            if (toolType != FailedConcreteRequiredTool) return false;
            failedConcreteBreakProgress = Mathf.Clamp(
                failedConcreteBreakProgress + workPower,
                0f,
                FailedConcreteWorkRequired);
            if (failedConcreteBreakProgress >= FailedConcreteWorkRequired)
                BeginFailedConcreteCollapse();
            ApplyVisualState();
            bridgeComponent.RefreshVisualAndColliderState();
            return true;
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
                EnterSoilRemoval();
            }

            ApplyVisualState();
            bridgeComponent.RefreshVisualAndColliderState();
            return true;
        }

        return false;
    }

    public virtual bool TryApplyToolWork(
        EquippableItemType toolType,
        float workPower,
        int workPointId,
        ulong actorClientId)
    {
        return TryApplyToolWork(toolType, workPower, workPointId);
    }

    public virtual bool TryConfirmLeveling()
    {
        return false;
    }

    public void RequestLevelingConfirmation(
        Transform interactor,
        LevelingConfirmationSourceType sourceType = LevelingConfirmationSourceType.Component,
        int sourcePointId = -1)
    {
        if (bridgeComponent == null || interactor == null)
        {
            return;
        }

        GameplayManager.Instance?.RequestLevelingConfirmation(
            bridgeComponent,
            interactor,
            sourceType,
            sourcePointId);
    }

    public bool TryResolveLevelingConfirmationPoint(
        LevelingConfirmationSourceType sourceType,
        int sourcePointId,
        out Collider validationCollider)
    {
        validationCollider = null;
        if (this is not ILevelingMeasurementTarget levelingTarget || !levelingTarget.IsLevelingActive)
        {
            return false;
        }

        if (sourceType == LevelingConfirmationSourceType.Component)
        {
            if (sourcePointId != -1 || constructionInteractionCollider == null ||
                !constructionInteractionCollider.enabled || !constructionInteractionCollider.gameObject.activeInHierarchy)
            {
                return false;
            }

            validationCollider = constructionInteractionCollider;
            return true;
        }

        ILevelingConfirmationSource resolved = null;
        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is not ILevelingConfirmationSource candidate ||
                candidate.ConfirmationSite != this ||
                candidate.ConfirmationSourceType != sourceType ||
                candidate.ConfirmationPointId != sourcePointId)
            {
                continue;
            }

            if (resolved != null)
            {
                return false;
            }

            resolved = candidate;
        }

        if (resolved == null || !resolved.IsLevelingConfirmationAvailable ||
            resolved.ConfirmationCollider == null || !resolved.ConfirmationCollider.enabled)
        {
            return false;
        }

        validationCollider = resolved.ConfirmationCollider;
        return true;
    }

    public virtual bool CanApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        if (CanBreakFailedConcrete && workPointId == FoundationFailedConcreteTarget.WorkPointId)
            return workPower > 0f && toolType == FailedConcreteRequiredTool;
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
                        $"Remove soil - {Mathf.Max(0, removedSoilUnits - cycleStart)} / {workflow.SoilUnitsPerCycle} " +
                        $"(cycle {cycleDisplay} / {workflow.DiggingCycleCount}) - hardens in {RemainingSoilHardeningTime:F1} s"));
                }
                break;
            case BridgeConstructionStage.ReadyForMount:
                bridgeComponent.AddReadyForMountPrompt(prompts, "Deliver Wooden Foundation");
                break;
            case BridgeConstructionStage.ConcretePouring:
                if (concreteFailureState == FoundationConcreteFailureState.HardenedFailure)
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                        $"Break hardened concrete: {Mathf.CeilToInt(failedConcreteBreakProgress)} / " +
                        $"{Mathf.CeilToInt(FailedConcreteWorkRequired)}"));
                else if (concreteFailureState == FoundationConcreteFailureState.AwaitingWheelbarrowExit)
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                        "Remove the wheelbarrow from the excavation"));
                else if (concreteFailureState == FoundationConcreteFailureState.CriticalSequence ||
                         concreteFailureState == FoundationConcreteFailureState.Collapsing)
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                        "Concrete failure recovery in progress"));
                else
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                        $"Pour concrete - {concreteLoadsPoured} / {RequiredConcreteLoads}"));
                break;
            case BridgeConstructionStage.ConcreteDrying:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                    $"Concrete drying - {RemainingConcreteDryingTime:F1} s"));
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
        concreteLoadsPoured = Mathf.Max(0, Mathf.RoundToInt(state.constructionAnchor0));
        concreteFailureState = (FoundationConcreteFailureState)Mathf.Clamp(
            Mathf.RoundToInt(state.constructionAnchor1),
            (int)FoundationConcreteFailureState.None,
            (int)FoundationConcreteFailureState.AwaitingWheelbarrowExit);
        if (currentStage == BridgeConstructionStage.ConcretePouring &&
            concreteFailureState == FoundationConcreteFailureState.None)
            concreteFailureState = FoundationConcreteFailureState.Ready;
        failedConcreteBreakProgress = Mathf.Max(0f, state.constructionAnchor2);
        failedConcreteCollapseDeadline = Mathf.Max(0f, state.constructionAnchor3);
        soilHardeningDeadline = currentStage == BridgeConstructionStage.Digging ? Mathf.Max(0f, state.constructionAux1) : 0f;
        concreteDryingDeadline = currentStage == BridgeConstructionStage.ConcreteDrying ? Mathf.Max(0f, state.constructionAux1) : 0f;
        if (currentStage != BridgeConstructionStage.Digging || diggingSubstage != FoundationDiggingSubstage.SoilRemoval)
        {
            ClearSoilHardeningDeadline();
        }
        if (currentStage == BridgeConstructionStage.Complete)
        {
            SetClearingObstaclesActive(false);
        }
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
        state.constructionAnchor0 = concreteLoadsPoured;
        state.constructionAnchor1 = (float)concreteFailureState;
        state.constructionAnchor2 = failedConcreteBreakProgress;
        state.constructionAnchor3 = failedConcreteCollapseDeadline;
        state.constructionAux1 = IsSoilHardeningActive
            ? soilHardeningDeadline
            : IsConcreteDrying ? concreteDryingDeadline : 0f;
    }

    public virtual void NotifyMounted()
    {
        if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            currentStage = BridgeConstructionStage.Hammering;
            currentWorkProgress = 0f;
            ClearSoilHardeningDeadline();
            ApplyVisualState();
        }
    }

    public virtual void NotifyAssembled()
    {
        currentStage = BridgeConstructionStage.Complete;
        currentWorkProgress = RequiredWorkProgress;
        ClearSoilHardeningDeadline();
        ApplyVisualState();
        bridgeComponent?.RefreshVisualAndColliderState();
    }

    public virtual void ApplyInitialCompletedState()
    {
        Initialize();
        currentStage = BridgeConstructionStage.Complete;
        currentWorkProgress = RequiredWorkProgress;
        diggingCycleIndex = workflow != null ? workflow.DiggingCycleCount : 0;
        removedSoilUnits = workflow != null
            ? workflow.DiggingCycleCount * workflow.SoilUnitsPerCycle
            : 0;
        concreteLoadsPoured = RequiredConcreteLoads;
        concreteFailureState = FoundationConcreteFailureState.None;
        failedConcreteBreakProgress = 0f;
        failedConcreteCollapseDeadline = 0f;
        diggingSubstage = FoundationDiggingSubstage.SoilRemoval;
        ClearSoilHardeningDeadline();
        concreteDryingDeadline = 0f;
        SetClearingObstaclesActive(false);
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
        ClearSoilHardeningDeadline();
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

    private void SetClearingObstaclesActive(bool active)
    {
        if (clearingObstacles != null)
        {
            foreach (BaseResourceNew obstacle in clearingObstacles)
            {
                if (obstacle != null)
                {
                    obstacle.gameObject.SetActive(active);
                }
            }
        }

        if (clearingSaplings != null)
        {
            foreach (FlexibleSaplingController sapling in clearingSaplings)
            {
                if (sapling != null)
                {
                    sapling.gameObject.SetActive(active);
                }
            }
        }
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
                                                      currentStage == BridgeConstructionStage.Digging ||
                                                      currentStage == BridgeConstructionStage.ConcretePouring ||
                                                      currentStage == BridgeConstructionStage.ConcreteDrying;
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
        ApplyConcreteVisualState();

        if (completedDigVisual != null)
        {
            completedDigVisual.SetActive(currentStage == BridgeConstructionStage.ConcretePouring ||
                                         currentStage == BridgeConstructionStage.ConcreteDrying ||
                                         currentStage == BridgeConstructionStage.ReadyForMount);
        }
    }

    private void ApplyConcreteVisualState()
    {
        excavationVolume?.ApplyConcreteState(
            currentStage,
            concreteLoadsPoured,
            RequiredConcreteLoads,
            RemainingConcreteDryingTime,
            workflow != null ? workflow.ConcreteDryingDuration : 30f,
            concreteFailureState,
            failedConcreteBreakProgress,
            concretePouringProfile != null ? concretePouringProfile.FailedConcreteCrackThresholds : new Vector3(1f, 34f, 67f),
            FailedConcreteCollapseProgress);
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

#if UNITY_EDITOR
    public void RefreshInitialStatePreview(bool completed)
    {
        if (Application.isPlaying)
        {
            return;
        }

        Initialize();
        if (completed)
        {
            currentStage = BridgeConstructionStage.Complete;
            currentWorkProgress = RequiredWorkProgress;
            SetClearingObstaclesActive(false);
        }
        else
        {
            SetClearingObstaclesActive(true);
            currentStage = GetInitialStage();
            currentWorkProgress = 0f;
        }

        ApplyVisualState();
    }
#endif

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
        if (workPointId == FoundationFailedConcreteTarget.WorkPointId &&
            excavationVolume != null && excavationVolume.FailedConcreteTarget != null)
            return excavationVolume.FailedConcreteTarget;
        return this;
    }

    private void EnterSoilRemoval()
    {
        diggingSubstage = FoundationDiggingSubstage.SoilRemoval;
        soilHardeningDeadline = GetSynchronizedTime() + workflow.LoosenedSoilHardeningDuration;
    }

    private void HardenLoosenedSoil()
    {
        if (!IsSoilHardeningActive)
        {
            return;
        }

        diggingSubstage = FoundationDiggingSubstage.Loosening;
        currentWorkProgress = 0f;
        ClearSoilHardeningDeadline();
        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
    }

    private void ClearSoilHardeningDeadline()
    {
        soilHardeningDeadline = 0f;
    }

    public bool TryAcceptConcreteLoad(int loads = 1)
    {
        if (!HasAuthority() || workflow == null || currentStage != BridgeConstructionStage.ConcretePouring ||
            !CanAcceptFoundationDock || loads <= 0)
        {
            return false;
        }

        concreteLoadsPoured = Mathf.Clamp(concreteLoadsPoured + loads, 0, workflow.RequiredConcreteLoads);
        if (concreteLoadsPoured >= workflow.RequiredConcreteLoads)
        {
            currentStage = BridgeConstructionStage.ConcreteDrying;
            concreteDryingDeadline = GetSynchronizedTime() + workflow.ConcreteDryingDuration;
        }

        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        return true;
    }

    public bool CanBeginCriticalConcreteFailure(
        WheelbarrowDockingStation station,
        WheelbarrowController wheelbarrow)
    {
        return HasAuthority() && currentStage == BridgeConstructionStage.ConcretePouring &&
               CanAcceptFoundationDock && station != null && wheelbarrow != null &&
               station.DockType == WheelbarrowDockType.FoundationPouring &&
               station.FoundationSite == this && station.DockedWheelbarrow == wheelbarrow &&
               FailedWheelbarrowPose != null && wheelbarrow.IsDockSecured &&
               wheelbarrow.State == WheelbarrowState.Pouring && wheelbarrow.HasPourableConcrete;
    }

    public bool BeginCriticalConcreteFailure(
        WheelbarrowDockingStation station,
        WheelbarrowController wheelbarrow)
    {
        if (!CanBeginCriticalConcreteFailure(station, wheelbarrow)) return false;

        recoveryStation = station;
        recoveryWheelbarrow = wheelbarrow;
        concreteFailureState = FoundationConcreteFailureState.CriticalSequence;
        failedConcreteBreakProgress = 0f;
        failedConcreteCollapseDeadline = 0f;
        concreteLoadsPoured = 0;
        return true;
    }

    public bool CommitCriticalConcreteFailure(
        WheelbarrowDockingStation station,
        WheelbarrowController wheelbarrow)
    {
        if (!HasAuthority() || concreteFailureState != FoundationConcreteFailureState.CriticalSequence ||
            recoveryStation != station || recoveryWheelbarrow != wheelbarrow ||
            station == null || station.DockedWheelbarrow != wheelbarrow ||
            wheelbarrow == null || wheelbarrow.State != WheelbarrowState.TrappedInFailedConcrete)
            return false;

        ApplyVisualState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        return true;
    }

    public bool CompleteCriticalConcreteFailureSequence(
        WheelbarrowDockingStation station,
        WheelbarrowController wheelbarrow)
    {
        if (!HasAuthority() || concreteFailureState != FoundationConcreteFailureState.CriticalSequence ||
            recoveryStation != station || recoveryWheelbarrow != wheelbarrow ||
            wheelbarrow == null || wheelbarrow.State != WheelbarrowState.TrappedInFailedConcrete)
            return false;
        concreteFailureState = FoundationConcreteFailureState.HardenedFailure;
        ApplyVisualState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        return true;
    }

    public void RollbackCriticalConcreteFailure(
        WheelbarrowController wheelbarrow,
        WheelbarrowDockingStation station,
        bool notify)
    {
        if (!HasAuthority() || !HasActiveConcreteFailure) return;
        if (wheelbarrow != null && recoveryWheelbarrow != null && wheelbarrow != recoveryWheelbarrow) return;
        if (station != null && recoveryStation != null && station != recoveryStation) return;
        ResetConcreteFailureState(notify);
    }

    public void ForceCleanupConcreteFailure(
        WheelbarrowController wheelbarrow,
        WheelbarrowDockingStation station)
    {
        if (!HasAuthority() || concreteFailureState is FoundationConcreteFailureState.None or FoundationConcreteFailureState.Ready)
            return;
        if (wheelbarrow != null && recoveryWheelbarrow != null && wheelbarrow != recoveryWheelbarrow) return;
        if (station != null && recoveryStation != null && station != recoveryStation) return;
        ResetConcreteFailureState(true);
    }

    private void BeginFailedConcreteCollapse()
    {
        if (concreteFailureState != FoundationConcreteFailureState.HardenedFailure) return;
        concreteFailureState = FoundationConcreteFailureState.Collapsing;
        float duration = concretePouringProfile != null
            ? concretePouringProfile.FailedConcreteCollapseDuration
            : 0.4f;
        failedConcreteCollapseDeadline = GetSynchronizedTime() + duration;
        excavationVolume?.PrepareFailedConcreteCollapse();
        ApplyVisualState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
    }

    private void CompleteFailedConcreteCollapse()
    {
        ResolveRecoveryReferences();
        failedConcreteCollapseDeadline = 0f;
        concreteFailureState = FoundationConcreteFailureState.AwaitingWheelbarrowExit;
        recoveryWheelbarrow?.ReleaseFromFailedConcrete(recoveryStation);
        ApplyVisualState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
    }

    private void TryCompleteFailedConcreteRecovery()
    {
        ResolveRecoveryReferences();
        if (!HasValidRecoveryTracking())
        {
            CleanupMissingRecoveryWheelbarrow();
            return;
        }
        if (excavationVolume == null ||
            excavationVolume.ContainsRecoveryWheelbarrow(recoveryWheelbarrow.transform.position))
            return;

        recoveryStation?.CompleteFailedConcreteRecovery(recoveryWheelbarrow);
        ResetConcreteFailureState(true);
    }

    private void ResetConcreteFailureState(bool notify)
    {
        concreteFailureState = currentStage == BridgeConstructionStage.ConcretePouring
            ? FoundationConcreteFailureState.Ready
            : FoundationConcreteFailureState.None;
        failedConcreteBreakProgress = 0f;
        failedConcreteCollapseDeadline = 0f;
        recoveryStation = null;
        recoveryWheelbarrow = null;
        ApplyVisualState();
        bridgeComponent?.RefreshVisualAndColliderState();
        if (notify) GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
    }

    private void ResolveRecoveryReferences()
    {
        if (recoveryStation == null)
        {
            foreach (WheelbarrowDockingStation station in
                     FindObjectsByType<WheelbarrowDockingStation>(FindObjectsSortMode.None))
            {
                if (station != null && station.FoundationSite == this)
                {
                    recoveryStation = station;
                    break;
                }
            }
        }
        if (recoveryWheelbarrow == null && recoveryStation != null)
            recoveryWheelbarrow = recoveryStation.DockedWheelbarrow;
    }

    private bool HasValidRecoveryTracking()
    {
        if (recoveryStation == null) return false;
        WheelbarrowController tracked = recoveryStation.DockedWheelbarrow;
        if (tracked == null) return false;
        if (recoveryWheelbarrow != null && recoveryWheelbarrow != tracked) return false;
        recoveryWheelbarrow = tracked;
        return true;
    }

    private void CleanupMissingRecoveryWheelbarrow()
    {
        WheelbarrowDockingStation station = recoveryStation;
        if (station != null)
        {
            station.CleanupMissingTrackedWheelbarrow();
            if (!HasActiveConcreteFailure) return;
        }
        ForceCleanupConcreteFailure(null, station);
    }

    private void CompleteConcreteDrying()
    {
        if (!IsConcreteDrying)
        {
            return;
        }

        concreteDryingDeadline = 0f;
        currentStage = BridgeConstructionStage.ReadyForMount;
        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
    }

    private static float GetSynchronizedTime()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening
            ? (float)networkManager.ServerTime.Time
            : Time.time;
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
            ClearSoilHardeningDeadline();
            diggingCycleIndex++;
            currentWorkProgress = 0f;
            if (diggingCycleIndex >= workflow.DiggingCycleCount)
            {
                currentStage = BridgeConstructionStage.ConcretePouring;
                concreteFailureState = FoundationConcreteFailureState.Ready;
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
            currentStage == BridgeConstructionStage.Complete || currentStage == BridgeConstructionStage.ConcreteDrying ||
            concreteLoadsPoured > 0 || removedSoilUnits <= 0)
        {
            return 0;
        }

        int totalSoilUnits = workflow.DiggingCycleCount * workflow.SoilUnitsPerCycle;
        removedSoilUnits = Mathf.Clamp(removedSoilUnits, 0, totalSoilUnits);
        int accepted = Mathf.Min(units, removedSoilUnits);
        removedSoilUnits = Mathf.Clamp(removedSoilUnits - accepted, 0, totalSoilUnits);
        if (currentStage == BridgeConstructionStage.ReadyForMount || currentStage == BridgeConstructionStage.ConcretePouring)
        {
            currentStage = BridgeConstructionStage.Digging;
            diggingCycleIndex = workflow.DiggingCycleCount - 1;
            currentWorkProgress = workflow.LooseningProgressPerCycle;
            EnterSoilRemoval();
        }

        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        return accepted;
    }
}
