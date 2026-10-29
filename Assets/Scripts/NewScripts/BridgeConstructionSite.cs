using System;
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
    private float removedSoilUnits;
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
    [NonSerialized] private LooseSoilHeightField collectionBalanceField;
    [NonSerialized] private int collectionBalanceCycle = -1;
    [NonSerialized] private ulong collectionBalanceRevision;
    [NonSerialized] private int collectionBalanceTransferCount;
    [NonSerialized] private double collectionRoundingBalance;
    private bool foundationRampClearanceGranted;
    private bool rampLocalClearanceGranted;
    private bool rampClearancePending;
    private float nextRampClearanceCheckTime;

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
    public FoundationExcavationVolume ExcavationVolume => excavationVolume;
    public FoundationDiggingSubstage DiggingSubstage => diggingSubstage;
    public int DiggingCycleIndex => diggingCycleIndex;
    public float RemovedSoilUnits => removedSoilUnits;
    public int RequiredSoilUnits => workflow != null ? workflow.SoilUnitsPerCycle : 0;
    private bool UsesLooseSoilHeightField => excavationVolume != null && excavationVolume.UsesLooseSoilHeightField;
    public bool IsSoilHardeningActive => !UsesLooseSoilHeightField && currentStage == BridgeConstructionStage.Digging &&
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
        (concreteFailureState is FoundationConcreteFailureState.None or FoundationConcreteFailureState.Ready) &&
        (!UsesLooseSoilHeightField || (foundationRampClearanceGranted && rampLocalClearanceGranted &&
          !rampClearancePending && excavationVolume != null && excavationVolume.IsExitRampColliderEnabled));
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
        foundationRampClearanceGranted = false;
        rampLocalClearanceGranted = false;
        rampClearancePending = false;
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
        UpdateFoundationRampClearance();
        if (IsConcreteDrying)
        {
            ApplyConcreteVisualState();
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
            bool enteredSoilRemoval = currentWorkProgress >= workflow.LooseningProgressPerCycle;
            if (enteredSoilRemoval)
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
                    if (UsesLooseSoilHeightField)
                    {
                        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                            $"Loose soil - layer {cycleDisplay} / {workflow.DiggingCycleCount}"));
                    }
                    else
                    {
                        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                            $"Remove soil - {SubstanceAmount.Format(Mathf.Max(0f, removedSoilUnits - cycleStart))} / {workflow.SoilUnitsPerCycle} " +
                            $"(cycle {cycleDisplay} / {workflow.DiggingCycleCount}) - hardens in {RemainingSoilHardeningTime:F1} s"));
                    }
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
        bool serverGrant = state.foundationRampClearanceGranted;
        bool rampWasAlreadyReady = excavationVolume != null && excavationVolume.IsExitRampColliderEnabled;
        currentStage = (BridgeConstructionStage)state.constructionStage;
        currentWorkProgress = Mathf.Max(0f, state.constructionProgress);
        diggingCycleIndex = Mathf.Max(0, state.constructionValueA);
        diggingSubstage = (FoundationDiggingSubstage)Mathf.Clamp(state.constructionValueB, 0, 2);
        removedSoilUnits = float.IsNaN(state.constructionAux0) || float.IsInfinity(state.constructionAux0)
            ? 0f
            : Mathf.Max(0f, state.constructionAux0);
        concreteLoadsPoured = Mathf.Max(0, Mathf.RoundToInt(state.constructionAnchor0));
        foundationRampClearanceGranted = serverGrant;
        rampLocalClearanceGranted = serverGrant && rampWasAlreadyReady;
        rampClearancePending = serverGrant && !rampWasAlreadyReady;
        if (currentStage == BridgeConstructionStage.Digging)
        {
            foundationRampClearanceGranted = false;
            rampLocalClearanceGranted = false;
            rampClearancePending = false;
        }
        concreteFailureState = (FoundationConcreteFailureState)Mathf.Clamp(
            Mathf.RoundToInt(state.constructionAnchor1),
            (int)FoundationConcreteFailureState.None,
            (int)FoundationConcreteFailureState.AwaitingWheelbarrowExit);
        if (currentStage == BridgeConstructionStage.ConcretePouring &&
            concreteFailureState == FoundationConcreteFailureState.None)
            concreteFailureState = FoundationConcreteFailureState.Ready;
        failedConcreteBreakProgress = Mathf.Max(0f, state.constructionAnchor2);
        failedConcreteCollapseDeadline = Mathf.Max(0f, state.constructionAnchor3);
        soilHardeningDeadline = !UsesLooseSoilHeightField && currentStage == BridgeConstructionStage.Digging
            ? Mathf.Max(0f, state.constructionAux1)
            : 0f;
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
        state.foundationRampClearanceGranted = foundationRampClearanceGranted;
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
        if (excavationVolume != null) excavationVolume.ResetLooseSoilState();
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
        if (excavationVolume != null) excavationVolume.ResetLooseSoilState();
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

        excavationVolume?.ApplyDiggingState(
            currentStage, diggingSubstage, currentWorkProgress, removedSoilUnits, workflow, diggingCycleIndex);
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
        if (UsesLooseSoilHeightField && excavationVolume != null &&
            !excavationVolume.IsExitRampColliderRequested(currentStage, concreteLoadsPoured,
                RequiredConcreteLoads, concreteFailureState))
        {
            // Clearance is an OFF-to-ON grant. Once the ramp is actually requested
            // off (hard concrete, re-dig, restart), a later request needs a fresh check.
            foundationRampClearanceGranted = false;
            rampLocalClearanceGranted = false;
            rampClearancePending = false;
        }
        excavationVolume?.ApplyConcreteState(
            currentStage,
            concreteLoadsPoured,
            RequiredConcreteLoads,
            RemainingConcreteDryingTime,
            workflow != null ? workflow.ConcreteDryingDuration : 30f,
            concreteFailureState,
            failedConcreteBreakProgress,
            concretePouringProfile != null ? concretePouringProfile.FailedConcreteCrackThresholds : new Vector3(1f, 34f, 67f),
            FailedConcreteCollapseProgress,
            foundationRampClearanceGranted && rampLocalClearanceGranted,
            rampClearancePending);
    }

    private void UpdateFoundationRampClearance()
    {
        if (!UsesLooseSoilHeightField || excavationVolume == null) return;
        bool eligible = excavationVolume.IsExitRampColliderRequested(currentStage, concreteLoadsPoured,
                            RequiredConcreteLoads, concreteFailureState);
        if (!eligible)
        {
            if (currentStage == BridgeConstructionStage.Digging)
            {
                foundationRampClearanceGranted = false;
                rampLocalClearanceGranted = false;
            }
            rampClearancePending = false;
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        bool authoritative = manager == null || !manager.IsListening || manager.IsServer;
        if (!authoritative && !foundationRampClearanceGranted)
        {
            rampClearancePending = true;
            return;
        }
        if (Time.time < nextRampClearanceCheckTime) return;
        nextRampClearanceCheckTime = Time.time + 0.1f;
        if (authoritative && foundationRampClearanceGranted && rampLocalClearanceGranted &&
            !rampClearancePending && excavationVolume.IsExitRampColliderEnabled)
        {
            return;
        }
        if (!authoritative && foundationRampClearanceGranted &&
            rampLocalClearanceGranted && excavationVolume.IsExitRampColliderEnabled)
        {
            // A snapshot or visual refresh must not revoke a clearance grant from
            // beneath a player who is already standing on the active ramp.
            return;
        }

        rampClearancePending = true;
        if (!excavationVolume.IsExitRampVolumeClear())
        {
            rampLocalClearanceGranted = false;
            ApplyConcreteVisualState();
            return;
        }

        rampLocalClearanceGranted = true;
        rampClearancePending = false;
        if (authoritative && !foundationRampClearanceGranted)
        {
            foundationRampClearanceGranted = true;
            ApplyConcreteVisualState();
            GameplayManager managerInstance = GameplayManager.Instance;
            if (managerInstance != null) managerInstance.NotifyConstructionSiteStateChanged(this);
        }
        else
        {
            ApplyConcreteVisualState();
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
        soilHardeningDeadline = UsesLooseSoilHeightField
            ? 0f
            : GetSynchronizedTime() + workflow.LoosenedSoilHardeningDuration;
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

    public float TryExtractSoil(float units, ContainerSubstanceSO substance)
    {
        if (UsesLooseSoilHeightField || !HasAuthority() || !SubstanceAmount.IsFinitePositive(units) || substance == null || !substance.IsSoil || workflow == null ||
            currentStage != BridgeConstructionStage.Digging || diggingSubstage != FoundationDiggingSubstage.SoilRemoval)
        {
            return 0f;
        }

        float target = (diggingCycleIndex + 1) * workflow.SoilUnitsPerCycle;
        if (removedSoilUnits >= target)
        {
            return 0f;
        }

        float extracted = Mathf.Min(units, target - removedSoilUnits);
        removedSoilUnits += extracted;
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
        return extracted;
    }

    /// <summary>
    /// Atomically transfers locally reachable loose soil into a bucket. The server owns
    /// both mutations; a field snapshot and bucket rollback protect against a rejected
    /// or partial destination write.
    /// </summary>
    public float TryCollectLooseSoil(PortableSubstanceContainer bucket, ContainerSubstanceSO substance,
        Vector3 worldPosition, float radius, float requestedUnits, float wholeLayerTailThreshold)
    {
        if (!HasAuthority() || bucket == null || substance == null || !substance.IsSoil ||
            !SubstanceAmount.IsFinitePositive(requestedUnits) || !SubstanceAmount.IsFinitePositive(radius) ||
            float.IsNaN(wholeLayerTailThreshold) || float.IsInfinity(wholeLayerTailThreshold) ||
            wholeLayerTailThreshold < 0f || !UsesLooseSoilHeightField || excavationVolume == null || workflow == null ||
            currentStage != BridgeConstructionStage.Digging ||
            diggingSubstage is not (FoundationDiggingSubstage.SoilRemoval or FoundationDiggingSubstage.ReturnedSoilRemoval) ||
            diggingCycleIndex < 0 || diggingCycleIndex >= workflow.DiggingCycleCount)
            return 0f;

        FoundationLooseSoilField soilField = excavationVolume.LooseSoilField;
        LooseSoilHeightField field = soilField != null ? soilField.HeightField : null;
        if (field == null || !soilField.IsSurfaceActive) return 0f;

        EnsureCollectionBalance(field, diggingCycleIndex);
        float cycleStart = diggingCycleIndex * workflow.SoilUnitsPerCycle;
        // The field's measured remaining volume is the source of truth; cumulative
        // network floats can drift slightly after many small transfers.
        double preciseRemaining = Math.Max(0d, field.TotalUnitsPrecise);
        double balanceTolerance = CollectionBalanceTolerance(field, workflow.SoilUnitsPerCycle,
            collectionBalanceTransferCount);
        double logicalRemaining = Math.Max(0d, preciseRemaining - collectionRoundingBalance);
        float remaining = (float)preciseRemaining;
        float capacity = bucket.GetAvailableCapacity(substance);
        if (preciseRemaining <= 0d || bucket.IsReturning) return 0f;

        Vector3 localPosition = soilField.transform.InverseTransformPoint(worldPosition);
        if (float.IsNaN(localPosition.x) || float.IsInfinity(localPosition.x) ||
            float.IsNaN(localPosition.y) || float.IsInfinity(localPosition.y) ||
            float.IsNaN(localPosition.z) || float.IsInfinity(localPosition.z) ||
            localPosition.y < -0.05f ||
            localPosition.y > field.GetMaximumHeightWithinRadius(localPosition.x, localPosition.z, radius) + 0.05f ||
            !field.TryGetHeight(localPosition.x, localPosition.z, out _)) return 0f;

        LooseSoilHeightField.Snapshot before = field.CaptureSnapshot();
        double preciseBefore = field.TotalUnitsPrecise;
        float bucketUnitsBefore = bucket.CurrentUnits;
        int bucketSubstanceBefore = bucket.CurrentSubstanceIndexForRollback;
        double nextRoundingBalance = collectionRoundingBalance;
        double storedDelta = 0d;
        double logicalTransferBudget = 0d;
        double preciseAfter;

        double noBucketCoverageTolerance = CollectionCoverageTolerance(field, workflow.SoilUnitsPerCycle,
            collectionBalanceTransferCount, collectionRoundingBalance, preciseRemaining);
        bool reconcileWithoutBucket = collectionRoundingBalance > 0d &&
                                     collectionRoundingBalance + noBucketCoverageTolerance >= preciseRemaining;
        if (reconcileWithoutBucket)
        {
            field.Clear();
            nextRoundingBalance -= preciseRemaining;
            if (nextRoundingBalance < 0d && nextRoundingBalance >= -balanceTolerance)
                nextRoundingBalance = 0d;
            preciseAfter = 0d;
        }
        else
        {
            if (capacity <= 0f) return 0f;
            bool collectWholeTail = wholeLayerTailThreshold > 0f && logicalRemaining <= wholeLayerTailThreshold &&
                                    (double)capacity >= logicalRemaining;
            double logicalBudget = Math.Min(logicalRemaining, capacity);
            if (!collectWholeTail) logicalBudget = Math.Min(logicalBudget, requestedUnits);
            logicalTransferBudget = logicalBudget;
            float logicalBudgetFloat = (float)logicalBudget;
            if (!SubstanceAmount.IsFinitePositive(logicalBudgetFloat)) return 0f;

            // Choose the destination's representable increment first, then ask the
            // source for that amount. This prevents a downward-rounded bucket write
            // from repeatedly creating avoidable collection debt at normal sample rates.
            float safeRequest = MaximumAddRequestWithinBudget(logicalBudgetFloat, bucketUnitsBefore,
                capacity, bucket.Capacity, logicalBudget);
            if (safeRequest <= 0f) return 0f;
            float expectedBucketUnits = Mathf.Min(bucket.Capacity,
                bucketUnitsBefore + Mathf.Min(safeRequest, capacity));
            double expectedStoredDelta = (double)expectedBucketUnits - bucketUnitsBefore;
            if (expectedStoredDelta <= 0d ||
                expectedStoredDelta > logicalBudget + DoubleRoundoffTolerance(logicalBudget)) return 0f;
            float physicalRequest = collectWholeTail ? remaining : (float)expectedStoredDelta;
            float extracted = collectWholeTail
                ? field.RemoveEverywhere(physicalRequest)
                : field.Remove(localPosition.x, localPosition.z, radius, physicalRequest);
            preciseAfter = field.TotalUnitsPrecise;
            double preciseRemoved = preciseBefore - preciseAfter;
            if (!SubstanceAmount.IsFinitePositive(extracted) || preciseRemoved <= 0d)
            {
                RollbackLooseSoilTransfer(field, before, bucket, bucketSubstanceBefore, bucketUnitsBefore);
                return 0f;
            }

            float destinationBudget = Mathf.Min(extracted, safeRequest);
            safeRequest = MaximumAddRequestWithinBudget(destinationBudget, bucketUnitsBefore,
                capacity, bucket.Capacity, logicalBudget);
            if (safeRequest <= 0f)
            {
                RollbackLooseSoilTransfer(field, before, bucket, bucketSubstanceBefore, bucketUnitsBefore);
                return 0f;
            }
            float added = bucket.TryAddUnits(substance, safeRequest);
            storedDelta = (double)bucket.CurrentUnits - bucketUnitsBefore;
            if (added != safeRequest || storedDelta <= 0d ||
                storedDelta > logicalBudget + DoubleRoundoffTolerance(logicalBudget))
            {
                RollbackLooseSoilTransfer(field, before, bucket, bucketSubstanceBefore, bucketUnitsBefore);
                return 0f;
            }

            nextRoundingBalance += storedDelta - preciseRemoved;
            double remainingCoverageTolerance = CollectionCoverageTolerance(field, workflow.SoilUnitsPerCycle,
                collectionBalanceTransferCount + 1, nextRoundingBalance, preciseAfter);
            if (preciseAfter > 0d && nextRoundingBalance > 0d &&
                nextRoundingBalance + remainingCoverageTolerance >= preciseAfter)
            {
                // Earlier bucket writes already represented this much more material
                // than their physical deltas. Consume only that recorded surplus.
                field.Clear();
                nextRoundingBalance -= preciseAfter;
                if (nextRoundingBalance < 0d && nextRoundingBalance >= -balanceTolerance)
                    nextRoundingBalance = 0d;
                preciseAfter = 0d;
            }

            if (preciseAfter == 0d && nextRoundingBalance < 0d)
            {
                double debt = -nextRoundingBalance;
                double destinationSpacing = FloatSpacing(bucket.CurrentUnits);
                double nearestStoredError = Math.Min(destinationSpacing * 0.5d,
                    logicalTransferBudget * 0.00001d) + DoubleRoundoffTolerance(logicalTransferBudget);
                if (debt <= nearestStoredError)
                {
                    // The already-written float bucket value is the nearest valid
                    // destination amount; do not force it to the next float step.
                    nextRoundingBalance = 0d;
                }
                else
                {
                    int settlementAttempts = 0;
                    while (nextRoundingBalance < 0d && settlementAttempts++ < 4)
                    {
                        debt = -nextRoundingBalance;
                        float availableForDebt = bucket.GetAvailableCapacity(substance);
                        if ((double)availableForDebt + DoubleRoundoffTolerance(debt) < debt)
                        {
                            RollbackLooseSoilTransfer(field, before, bucket, bucketSubstanceBefore, bucketUnitsBefore);
                            return 0f;
                        }

                        float debtRequest = (float)debt;
                        if (debtRequest <= 0f || (double)debtRequest > debt + DoubleRoundoffTolerance(debt))
                            debtRequest = PreviousPositiveFloat(debtRequest);
                        if (debtRequest <= 0f)
                        {
                            RollbackLooseSoilTransfer(field, before, bucket, bucketSubstanceBefore, bucketUnitsBefore);
                            return 0f;
                        }

                        float beforeDebtUnits = bucket.CurrentUnits;
                        float debtAdded = bucket.TryAddUnits(substance, debtRequest);
                        double actualDebtDelta = (double)bucket.CurrentUnits - beforeDebtUnits;
                        double debtOverage = actualDebtDelta - debt;
                        double representableClosureOverage = Math.Min(FloatSpacing(bucket.CurrentUnits),
                            logicalTransferBudget * 0.00001d);
                        if (debtAdded != debtRequest || actualDebtDelta <= 0d ||
                            debtOverage > DoubleRoundoffTolerance(debt) + representableClosureOverage)
                        {
                            RollbackLooseSoilTransfer(field, before, bucket, bucketSubstanceBefore, bucketUnitsBefore);
                            return 0f;
                        }
                        storedDelta += actualDebtDelta;
                        nextRoundingBalance += actualDebtDelta;
                        if (nextRoundingBalance < 0d)
                        {
                            double remainingDestinationSpacing = FloatSpacing(bucket.CurrentUnits);
                            double remainingNearestError = Math.Min(remainingDestinationSpacing * 0.5d,
                                logicalTransferBudget * 0.00001d) +
                                DoubleRoundoffTolerance(logicalTransferBudget);
                            if (-nextRoundingBalance <= remainingNearestError)
                                nextRoundingBalance = 0d;
                        }
                    }
                    if (nextRoundingBalance < -balanceTolerance)
                    {
                        RollbackLooseSoilTransfer(field, before, bucket, bucketSubstanceBefore, bucketUnitsBefore);
                        return 0f;
                    }
                    if (nextRoundingBalance < 0d) nextRoundingBalance = 0d;
                }
            }
        }

        soilField.RebuildMesh();
        double fieldRemainingUnits = Math.Max(0d, field.TotalUnitsPrecise);
        double logicalCycleStart = diggingSubstage == FoundationDiggingSubstage.ReturnedSoilRemoval
            ? cycleStart
            : cycleStart + workflow.SoilUnitsPerCycle;
        removedSoilUnits = Mathf.Clamp((float)(logicalCycleStart - fieldRemainingUnits + nextRoundingBalance),
            0f, workflow.DiggingCycleCount * workflow.SoilUnitsPerCycle);
        if (fieldRemainingUnits <= 0d)
        {
            ClearSoilHardeningDeadline();
            if (diggingSubstage == FoundationDiggingSubstage.ReturnedSoilRemoval)
            {
                diggingSubstage = FoundationDiggingSubstage.Loosening;
            }
            else
            {
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
        }

        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        if (GameplayManager.Instance != null) GameplayManager.Instance.NotifyConstructionSiteStateChanged(this);
        LooseSoilHeightField currentField = soilField.HeightField;
        if (diggingCycleIndex != collectionBalanceCycle || !ReferenceEquals(currentField, field))
        {
            ResetCollectionBalance();
        }
        else
        {
            collectionBalanceField = field;
            collectionBalanceCycle = diggingCycleIndex;
            collectionBalanceRevision = field.MutationRevision;
            collectionRoundingBalance = nextRoundingBalance;
            collectionBalanceTransferCount++;
        }
        return bucket.CurrentUnits - bucketUnitsBefore;
    }

    /// <summary>Returns bucket soil into the active local excavation field.</summary>
    public float TryReturnLooseSoilFromBucket(PortableSubstanceContainer bucket, ContainerSubstanceSO substance,
        Vector3 worldPosition, float requestedUnits)
    {
        if (bucket == null || substance == null || !substance.IsSoil || bucket.IsReturning ||
            !SubstanceAmount.IsFinitePositive(requestedUnits)) return 0f;
        float availableBefore = bucket.CurrentUnits;
        return TryReturnLooseSoil(availableBefore, worldPosition, requestedUnits,
            budget => bucket.GetMaximumRemovableUnits(substance, budget),
            budget => bucket.TryRemoveUnitsWithinBudget(substance, budget));
    }

    internal float TryReturnLooseSoilFromPile(LooseSubstancePile pile, Vector3 worldPosition)
    {
        if (pile == null || pile.Substance == null || !pile.Substance.IsSoil ||
            !SubstanceAmount.IsFinitePositive(pile.CurrentUnits)) return 0f;
        float availableBefore = pile.CurrentUnits;
        return TryReturnLooseSoil(availableBefore, worldPosition, availableBefore,
            budget => pile.GetMaximumRemovableUnits(budget),
            budget => pile.RemoveUnitsWithinBudget(budget));
    }

    private float TryReturnLooseSoil(float sourceUnits, Vector3 worldPosition, float requestedUnits,
        Func<double, double> previewSourceDebit, Func<double, double> debitSource)
    {
        if (!HasAuthority() || !UsesLooseSoilHeightField || excavationVolume == null || workflow == null ||
            !SubstanceAmount.IsFinitePositive(sourceUnits) || !SubstanceAmount.IsFinitePositive(requestedUnits) ||
            concreteLoadsPoured > 0 || HasActiveConcreteFailure ||
            !CanReturnLooseSoilInCurrentState()) return 0f;

        bool returningBeforeConcrete = currentStage == BridgeConstructionStage.ReadyForMount ||
                                       currentStage == BridgeConstructionStage.ConcretePouring;
        BridgeConstructionStage stageBefore = currentStage;
        FoundationDiggingSubstage substageBefore = diggingSubstage;
        int cycleBefore = diggingCycleIndex;
        float progressBefore = currentWorkProgress;
        float removedBefore = removedSoilUnits;
        FoundationConcreteFailureState failureBefore = concreteFailureState;
        float hardeningDeadlineBefore = soilHardeningDeadline;
        LooseSoilHeightField balanceFieldBefore = collectionBalanceField;
        int balanceCycleBefore = collectionBalanceCycle;
        ulong balanceRevisionBefore = collectionBalanceRevision;
        int balanceTransferCountBefore = collectionBalanceTransferCount;
        double roundingBalanceBefore = collectionRoundingBalance;
        FoundationLooseSoilField soilField = excavationVolume.LooseSoilField;
        LooseSoilHeightField field = soilField != null ? soilField.HeightField : null;
        if (field == null) return 0f;
        LooseSoilHeightField modelBeforeStateChange = field;
        LooseSoilHeightField.Snapshot fieldBeforeStateChange = field.CaptureSnapshot();

        if (returningBeforeConcrete)
        {
            currentStage = BridgeConstructionStage.Digging;
            diggingCycleIndex = workflow.DiggingCycleCount - 1;
            diggingSubstage = FoundationDiggingSubstage.SoilRemoval;
            currentWorkProgress = workflow.LooseningProgressPerCycle;
            EnterSoilRemoval();
        }
        else if (diggingSubstage == FoundationDiggingSubstage.Loosening)
        {
            diggingSubstage = FoundationDiggingSubstage.ReturnedSoilRemoval;
        }

        ApplyVisualState();
        field = soilField.HeightField;
        if (field == null || !soilField.IsSurfaceActive)
        {
            RollbackLooseSoilReturnState(stageBefore, substageBefore, cycleBefore, progressBefore,
                removedBefore, failureBefore, hardeningDeadlineBefore, soilField, modelBeforeStateChange,
                fieldBeforeStateChange,
                balanceFieldBefore, balanceCycleBefore, balanceRevisionBefore, balanceTransferCountBefore,
                roundingBalanceBefore);
            return 0f;
        }

        Vector3 localPosition = soilField.transform.InverseTransformPoint(worldPosition);
        if (!IsFinite(localPosition.x) || !IsFinite(localPosition.y) || !IsFinite(localPosition.z) ||
            !field.TryGetHeight(localPosition.x, localPosition.z, out _))
        {
            RollbackLooseSoilReturnState(stageBefore, substageBefore, cycleBefore, progressBefore,
                removedBefore, failureBefore, hardeningDeadlineBefore, soilField, modelBeforeStateChange,
                fieldBeforeStateChange,
                balanceFieldBefore, balanceCycleBefore, balanceRevisionBefore, balanceTransferCountBefore,
                roundingBalanceBefore);
            return 0f;
        }

        EnsureCollectionBalance(field, diggingCycleIndex);
        double balanceForTransfer = collectionRoundingBalance;
        int transferCountBefore = collectionBalanceTransferCount;
        double unitsBeforeAdd = field.TotalUnitsPrecise;
        int supportLayers = diggingSubstage == FoundationDiggingSubstage.ReturnedSoilRemoval
            ? diggingCycleIndex
            : diggingCycleIndex + 1;
        float heightCeiling = workflow.FinalExcavationDepth / workflow.DiggingCycleCount * supportLayers;
        float returnRadius = soilField.ReturnRadius;
        if (!SubstanceAmount.IsFinitePositive(heightCeiling) || !SubstanceAmount.IsFinitePositive(returnRadius))
        {
            RollbackLooseSoilReturnState(stageBefore, substageBefore, cycleBefore, progressBefore,
                removedBefore, failureBefore, hardeningDeadlineBefore, soilField, modelBeforeStateChange,
                fieldBeforeStateChange,
                balanceFieldBefore, balanceCycleBefore, balanceRevisionBefore, balanceTransferCountBefore,
                roundingBalanceBefore);
            return 0f;
        }

        double localCapacity = field.GetAdditionCapacity(localPosition.x, localPosition.z,
            returnRadius, heightCeiling);
        float cycleStart = diggingCycleIndex * workflow.SoilUnitsPerCycle;
        double cycleBaseline = diggingSubstage == FoundationDiggingSubstage.ReturnedSoilRemoval
            ? cycleStart
            : cycleStart + workflow.SoilUnitsPerCycle;
        double logicalAvailable = Math.Max(0d,
            cycleBaseline - field.TotalUnitsPrecise + balanceForTransfer);
        double logicalLocalCapacity = Math.Max(0d, localCapacity + balanceForTransfer);
        double requestedBudget = Math.Min(Math.Min((double)requestedUnits, sourceUnits),
            Math.Min(logicalAvailable, logicalLocalCapacity));
        double previewDebit = previewSourceDebit != null ? previewSourceDebit(requestedBudget) : 0d;
        double physicalRequest = Math.Max(0d, previewDebit - balanceForTransfer);
        double capacityRoundoff = DoubleRoundoffTolerance(Math.Max(cycleBaseline, unitsBeforeAdd));
        if (physicalRequest > localCapacity)
        {
            if (physicalRequest - localCapacity <= capacityRoundoff)
                physicalRequest = localCapacity;
            else
            {
                RollbackLooseSoilReturnState(stageBefore, substageBefore, cycleBefore, progressBefore,
                    removedBefore, failureBefore, hardeningDeadlineBefore, soilField, modelBeforeStateChange,
                    fieldBeforeStateChange, balanceFieldBefore, balanceCycleBefore, balanceRevisionBefore,
                    balanceTransferCountBefore, roundingBalanceBefore);
                return 0f;
            }
        }
        // A double request can sit just below the nearest representable grid-volume
        // quantum. Round the field request upward by at most one float step; the
        // per-cell physical representation guard below then bounds what the grid
        // actually adds, while the source debit stays capped by the logical budget.
        // Cancellation in the double-valued baseline/field subtraction can leave
        // the exact physical request just below a representable grid quantum.
        // Permit only that arithmetic roundoff in the model request; source debit
        // and the signed balance still use the exact logical preview above.
        float fieldRequest = physicalRequest > 0d
            ? FloatNotBelow(physicalRequest + capacityRoundoff)
            : 0f;
        if (double.IsNaN(previewDebit) || double.IsInfinity(previewDebit) || previewDebit <= 0d ||
            physicalRequest > 0d && !SubstanceAmount.IsFinitePositive(fieldRequest))
        {
            RollbackLooseSoilReturnState(stageBefore, substageBefore, cycleBefore, progressBefore,
                removedBefore, failureBefore, hardeningDeadlineBefore, soilField, modelBeforeStateChange,
                fieldBeforeStateChange,
                balanceFieldBefore, balanceCycleBefore, balanceRevisionBefore, balanceTransferCountBefore,
                roundingBalanceBefore);
            return 0f;
        }

        float acceptedFieldUnits = 0f;
        double physicalAdded = 0d;
        if (fieldRequest > 0f)
        {
            acceptedFieldUnits = field.AddWithHeightCeiling(localPosition.x, localPosition.z,
                returnRadius, fieldRequest, heightCeiling);
            physicalAdded = field.TotalUnitsPrecise - unitsBeforeAdd;
            if (!SubstanceAmount.IsFinitePositive(acceptedFieldUnits) || physicalAdded <= 0d ||
                acceptedFieldUnits > fieldRequest)
            {
                RollbackLooseSoilReturnState(stageBefore, substageBefore, cycleBefore, progressBefore,
                    removedBefore, failureBefore, hardeningDeadlineBefore, soilField, modelBeforeStateChange,
                    fieldBeforeStateChange, balanceFieldBefore, balanceCycleBefore, balanceRevisionBefore,
                    balanceTransferCountBefore, roundingBalanceBefore);
                return 0f;
            }
        }
        else if (physicalRequest > 0d)
        {
            RollbackLooseSoilReturnState(stageBefore, substageBefore, cycleBefore, progressBefore,
                removedBefore, failureBefore, hardeningDeadlineBefore, soilField, modelBeforeStateChange,
                fieldBeforeStateChange,
                balanceFieldBefore, balanceCycleBefore, balanceRevisionBefore, balanceTransferCountBefore,
                roundingBalanceBefore);
            return 0f;
        }

        double representationTolerance = physicalRequest > 0d
            ? field.GetAdditionRepresentationTolerance(localPosition.x, localPosition.z,
                returnRadius, heightCeiling, physicalRequest)
            : 0d;
        if (double.IsNaN(representationTolerance) || double.IsInfinity(representationTolerance) ||
            Math.Abs(physicalAdded - physicalRequest) > representationTolerance || debitSource == null)
        {
            RollbackLooseSoilReturnState(stageBefore, substageBefore, cycleBefore, progressBefore,
                removedBefore, failureBefore, hardeningDeadlineBefore, soilField, modelBeforeStateChange,
                fieldBeforeStateChange, balanceFieldBefore, balanceCycleBefore, balanceRevisionBefore,
                balanceTransferCountBefore, roundingBalanceBefore);
            return 0f;
        }

        double sourceDebitBudget = previewDebit;
        double sourceDebit = debitSource(sourceDebitBudget);
        if (double.IsNaN(sourceDebit) || double.IsInfinity(sourceDebit) || sourceDebit <= 0d ||
            sourceDebit > sourceDebitBudget)
        {
            RollbackLooseSoilReturnState(stageBefore, substageBefore, cycleBefore, progressBefore,
                removedBefore, failureBefore, hardeningDeadlineBefore, soilField, modelBeforeStateChange,
                fieldBeforeStateChange,
                balanceFieldBefore, balanceCycleBefore, balanceRevisionBefore, balanceTransferCountBefore,
                roundingBalanceBefore);
            return 0f;
        }

        collectionRoundingBalance = balanceForTransfer + physicalAdded - sourceDebit;
        removedSoilUnits = Mathf.Clamp((float)(cycleBaseline - field.TotalUnitsPrecise + collectionRoundingBalance),
            0f, workflow.DiggingCycleCount * workflow.SoilUnitsPerCycle);
        collectionBalanceField = field;
        collectionBalanceCycle = diggingCycleIndex;
        collectionBalanceRevision = field.MutationRevision;
        collectionBalanceTransferCount = transferCountBefore + 1;
        soilField.RebuildMesh();
        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        if (GameplayManager.Instance != null) GameplayManager.Instance.NotifyConstructionSiteStateChanged(this);
        return FloatNotAbove(sourceDebit);
    }

    private bool CanReturnLooseSoilInCurrentState()
    {
        if (currentStage == BridgeConstructionStage.Digging)
        {
            if (diggingCycleIndex < 0 || diggingCycleIndex >= workflow.DiggingCycleCount) return false;
            return diggingSubstage == FoundationDiggingSubstage.SoilRemoval ||
                   diggingSubstage == FoundationDiggingSubstage.ReturnedSoilRemoval ||
                   diggingSubstage == FoundationDiggingSubstage.Loosening && diggingCycleIndex > 0;
        }

        return currentStage == BridgeConstructionStage.ReadyForMount ||
               currentStage == BridgeConstructionStage.ConcretePouring && concreteFailureState is
                   FoundationConcreteFailureState.None or FoundationConcreteFailureState.Ready;
    }

    private void RollbackLooseSoilReturnState(BridgeConstructionStage stage, FoundationDiggingSubstage substage,
        int cycle, float progress, float removed, FoundationConcreteFailureState failure, float hardeningDeadline,
        FoundationLooseSoilField soilField, LooseSoilHeightField modelBeforeStateChange,
        LooseSoilHeightField.Snapshot fieldSnapshot,
        LooseSoilHeightField balanceField, int balanceCycle, ulong balanceRevision, int balanceTransferCount,
        double roundingBalance)
    {
        currentStage = stage;
        diggingSubstage = substage;
        diggingCycleIndex = cycle;
        currentWorkProgress = progress;
        removedSoilUnits = removed;
        concreteFailureState = failure;
        soilHardeningDeadline = hardeningDeadline;
        ApplyVisualState();
        if (fieldSnapshot != null && soilField != null)
        {
            if (!soilField.RestoreSnapshot(fieldSnapshot))
            {
                Debug.LogError("Loose-soil return rollback could not restore its exact field shape.", this);
                ResetCollectionBalance();
                return;
            }
        }
        LooseSoilHeightField restoredField = soilField != null ? soilField.HeightField : null;
        if (ReferenceEquals(balanceField, modelBeforeStateChange) && balanceCycle == cycle && restoredField != null)
        {
            collectionBalanceField = restoredField;
            collectionBalanceCycle = balanceCycle;
            collectionBalanceRevision = restoredField.MutationRevision;
            collectionBalanceTransferCount = balanceTransferCount;
            collectionRoundingBalance = roundingBalance;
        }
        else
        {
            collectionBalanceField = balanceField;
            collectionBalanceCycle = balanceCycle;
            collectionBalanceRevision = balanceRevision;
            collectionBalanceTransferCount = balanceTransferCount;
            collectionRoundingBalance = roundingBalance;
        }
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static float FloatNotAbove(double maximumValue)
    {
        if (double.IsNaN(maximumValue) || double.IsInfinity(maximumValue) || maximumValue <= 0d) return 0f;
        float rounded = (float)maximumValue;
        if ((double)rounded > maximumValue) rounded = PreviousPositiveFloat(rounded);
        return rounded;
    }

    private static float FloatNotBelow(double minimumValue)
    {
        if (double.IsNaN(minimumValue) || double.IsInfinity(minimumValue) || minimumValue <= 0d) return 0f;
        float rounded = (float)minimumValue;
        if ((double)rounded < minimumValue)
            rounded = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(rounded) + 1);
        return rounded;
    }

    private void EnsureCollectionBalance(LooseSoilHeightField field, int cycleIndex)
    {
        if (!ReferenceEquals(collectionBalanceField, field) || collectionBalanceCycle != cycleIndex ||
            collectionBalanceRevision != field.MutationRevision)
        {
            collectionBalanceField = field;
            collectionBalanceCycle = cycleIndex;
            collectionBalanceRevision = field.MutationRevision;
            collectionBalanceTransferCount = 0;
            collectionRoundingBalance = 0d;
        }
    }

    private void ResetCollectionBalance()
    {
        collectionBalanceField = null;
        collectionBalanceCycle = -1;
        collectionBalanceRevision = 0;
        collectionBalanceTransferCount = 0;
        collectionRoundingBalance = 0d;
    }

    private void RollbackLooseSoilTransfer(LooseSoilHeightField field, LooseSoilHeightField.Snapshot fieldBefore,
        PortableSubstanceContainer bucket, int bucketSubstanceBefore, float bucketUnitsBefore)
    {
        bool fieldRestored = field.RestoreSnapshot(fieldBefore);
        bool bucketRestored = bucket.TryRestoreContentsForRollback(bucketSubstanceBefore, bucketUnitsBefore);
        if (!fieldRestored || !bucketRestored)
        {
            ResetCollectionBalance();
            Debug.LogError("Loose-soil collection rollback could not restore its exact field and bucket state.", this);
            return;
        }

        if (ReferenceEquals(collectionBalanceField, field) && collectionBalanceCycle == diggingCycleIndex)
            collectionBalanceRevision = field.MutationRevision;
        else
            ResetCollectionBalance();
    }

    private static double DoubleRoundoffTolerance(double value) =>
        8d * 2.2204460492503131e-16d * Math.Max(1d, Math.Abs(value));

    private static double CollectionBalanceTolerance(LooseSoilHeightField field, double layerUnits,
        int transferCount)
    {
        double operations = (double)field.Columns * field.Rows + 2d * Math.Max(1, transferCount) + 1d;
        return 8d * 2.2204460492503131e-16d * Math.Max(1d, Math.Abs(layerUnits)) * operations;
    }

    private static double CollectionCoverageTolerance(LooseSoilHeightField field, double layerUnits,
        int transferCount, double recordedSurplus, double remaining)
    {
        if (recordedSurplus <= 0d || remaining <= 0d) return 0d;
        double arithmeticBound = CollectionBalanceTolerance(field, layerUnits, transferCount);
        double relativeBound = Math.Min(recordedSurplus, remaining) * 0.00001d;
        return Math.Min(arithmeticBound, relativeBound);
    }

    private static float PreviousPositiveFloat(float value)
    {
        if (float.IsNaN(value) || value <= 0f || float.IsInfinity(value)) return 0f;
        int bits = BitConverter.SingleToInt32Bits(value);
        return bits <= 1 ? 0f : BitConverter.Int32BitsToSingle(bits - 1);
    }

    private static double FloatSpacing(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0d;
        value = Mathf.Abs(value);
        int bits = BitConverter.SingleToInt32Bits(value);
        if (bits <= 0) return float.Epsilon;
        if (bits >= BitConverter.SingleToInt32Bits(float.MaxValue)) return 0d;
        return (double)BitConverter.Int32BitsToSingle(bits + 1) - value;
    }

    private static float MaximumAddRequestWithinBudget(float maximumRequest, float currentUnits,
        float availableCapacity, int containerCapacity, double transferBudget)
    {
        if (!SubstanceAmount.IsFinitePositive(maximumRequest) || availableCapacity <= 0f || transferBudget <= 0d)
            return 0f;

        maximumRequest = Mathf.Min(maximumRequest, availableCapacity);
        int low = 0;
        int high = BitConverter.SingleToInt32Bits(maximumRequest);
        while (low < high)
        {
            int middle = (int)(low + ((long)high - low + 1L) / 2L);
            float request = BitConverter.Int32BitsToSingle(middle);
            float added = Mathf.Min(request, availableCapacity);
            float resultingUnits = Mathf.Min(containerCapacity, currentUnits + added);
            double resultingDelta = (double)resultingUnits - currentUnits;
            if (resultingDelta <= transferBudget + DoubleRoundoffTolerance(transferBudget))
                low = middle;
            else
                high = middle - 1;
        }

        float safeRequest = BitConverter.Int32BitsToSingle(low);
        float safeAdded = Mathf.Min(safeRequest, availableCapacity);
        float safeResultingUnits = Mathf.Min(containerCapacity, currentUnits + safeAdded);
        return safeResultingUnits > currentUnits ? safeRequest : 0f;
    }

    public bool CanExtract(ContainerSubstanceSO substance)
    {
        return !UsesLooseSoilHeightField && substance != null && substance.IsSoil && currentStage == BridgeConstructionStage.Digging &&
               diggingSubstage == FoundationDiggingSubstage.SoilRemoval && workflow != null &&
               removedSoilUnits < (diggingCycleIndex + 1) * workflow.SoilUnitsPerCycle;
    }

    public float TryExtract(ContainerSubstanceSO substance, float units)
    {
        return TryExtractSoil(units, substance);
    }

    public float ReturnSoil(float units)
    {
        if (UsesLooseSoilHeightField || !HasAuthority() || !SubstanceAmount.IsFinitePositive(units) || workflow == null || currentStage == BridgeConstructionStage.Hammering ||
            currentStage == BridgeConstructionStage.Complete || currentStage == BridgeConstructionStage.ConcreteDrying ||
            concreteLoadsPoured > 0 || removedSoilUnits <= 0)
        {
            return 0;
        }

        float totalSoilUnits = workflow.DiggingCycleCount * workflow.SoilUnitsPerCycle;
        removedSoilUnits = Mathf.Clamp(removedSoilUnits, 0f, totalSoilUnits);
        float accepted = Mathf.Min(units, removedSoilUnits);
        removedSoilUnits = Mathf.Clamp(removedSoilUnits - accepted, 0f, totalSoilUnits);
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
