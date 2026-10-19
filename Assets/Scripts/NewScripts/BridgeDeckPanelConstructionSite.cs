using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BridgeDeckPanelConstructionSite : BridgeConstructionSite
{
    private static readonly BridgeDeckPanelWorkPointId[] FullFasteningOrder =
    {
        BridgeDeckPanelWorkPointId.FrontLeft,
        BridgeDeckPanelWorkPointId.BackRight,
        BridgeDeckPanelWorkPointId.FrontRight,
        BridgeDeckPanelWorkPointId.BackLeft
    };

    private static readonly BridgeDeckPanelWorkPointId[] ShortFasteningOrder =
    {
        BridgeDeckPanelWorkPointId.FrontLeft,
        BridgeDeckPanelWorkPointId.BackRight
    };

    [Header("Deck section")]
    [SerializeField] private BridgeDeckSection deckSection;
    [SerializeField] private int slotIndex;
    [SerializeField] private bool usesFullFastening;

    [Header("Panel visuals")]
    [SerializeField] private Transform layoutRoot;
    [SerializeField] private Transform panelVisualRoot;
    [SerializeField] private Renderer alignmentIndicatorRenderer;
    [SerializeField] private Color misalignedColor = new Color(0.8f, 0.15f, 0.1f, 1f);
    [SerializeField] private Color alignedColor = new Color(0.15f, 0.8f, 0.2f, 1f);

    private BridgeDeckPanelConstructionWorkflowSO deckWorkflow;
    private bool physicalPanelPlacement;
    private bool physicalPanelPlacementInitialized;
    private BridgeDeckPanelWorkPoint[] workPoints;
    private int lateralAlignmentStep;
    private int rotationAlignmentStep;
    private int leftGapStep;
    private int rightGapStep;
    private readonly float[] fastenerProgress = new float[4];
    private Vector3 panelBasePosition;
    private Quaternion panelBaseRotation;
    private MaterialPropertyBlock indicatorPropertyBlock;

    public int LateralAlignmentStep => lateralAlignmentStep;
    public int RotationAlignmentStep => rotationAlignmentStep;
    public int LeftGapStep => leftGapStep;
    public int RightGapStep => rightGapStep;
    public bool UsesFullFastening => usesFullFastening;
    public BridgeDeckSection DeckSection => deckSection;
    public int SlotIndex => slotIndex;
    public bool IsComplete => currentStage == BridgeConstructionStage.Complete;
    public bool UsesPhysicalPanelPlacement
    {
        get
        {
            if (physicalPanelPlacementInitialized) return physicalPanelPlacement;
            BridgeDeckPanelConstructionWorkflowSO workflow = bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null
                ? bridgeComponent.GetBridgeComponentSO().deckPanelConstructionWorkflow
                : null;
            bool hasPhysicalSlotMarker = TryGetComponent(out BridgeDeckPanelMounting marker) && marker.IsPassiveSlot;
            return hasPhysicalSlotMarker || workflow != null && workflow.UsesPhysicalPanelPlacement;
        }
    }
    public override bool CanAcceptMountedComponent =>
        !UsesPhysicalPanelPlacement && currentStage == BridgeConstructionStage.ReadyForMount &&
        (deckSection == null || deckSection.IsSlotUnlocked(slotIndex));

    protected override void Awake()
    {
        workPoints = GetComponentsInChildren<BridgeDeckPanelWorkPoint>(true);
        if (panelVisualRoot != null)
        {
            panelBasePosition = panelVisualRoot.localPosition;
            panelBaseRotation = panelVisualRoot.localRotation;
        }
        base.Awake();
        deckWorkflow = bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null
            ? bridgeComponent.GetBridgeComponentSO().deckPanelConstructionWorkflow
            : null;
        bool hasPhysicalSlotMarker = TryGetComponent(out BridgeDeckPanelMounting marker) && marker.IsPassiveSlot;
        physicalPanelPlacement = hasPhysicalSlotMarker ||
                                 deckWorkflow != null && deckWorkflow.UsesPhysicalPanelPlacement;
        physicalPanelPlacementInitialized = true;
        ApplyVisualState();
    }

    private void Update()
    {
        if (UsesPhysicalPanelPlacement) return;
        if (currentStage != BridgeConstructionStage.WaitingForPrevious || !HasAuthority() ||
            (deckSection != null && !deckSection.IsSlotUnlocked(slotIndex)))
        {
            return;
        }

        currentStage = BridgeConstructionStage.ReadyForMount;
        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
    }

    protected override BridgeConstructionStage GetInitialStage()
    {
        if (UsesPhysicalPanelPlacement) return BridgeConstructionStage.ReadyForMount;
        return deckSection == null || deckSection.IsSlotUnlocked(slotIndex)
            ? BridgeConstructionStage.ReadyForMount
            : BridgeConstructionStage.WaitingForPrevious;
    }

    public void ConfigureSection(BridgeDeckSection section, int index, bool fullFastening)
    {
        deckSection = section;
        slotIndex = index;
        usesFullFastening = fullFastening;
    }

    internal void ApplyPhysicalPanelCompletion()
    {
        if (!UsesPhysicalPanelPlacement || currentStage == BridgeConstructionStage.Complete) return;
        currentStage = BridgeConstructionStage.Complete;
        currentWorkProgress = 0f;
        ApplyVisualState();
        if (bridgeComponent != null) bridgeComponent.RefreshVisualAndColliderState();
    }

    public void RefreshSectionAvailability()
    {
        if (UsesPhysicalPanelPlacement)
        {
            ApplyVisualState();
            if (bridgeComponent != null) bridgeComponent.RefreshVisualAndColliderState();
            return;
        }
        if (currentStage == BridgeConstructionStage.WaitingForPrevious && HasAuthority() &&
            (deckSection == null || deckSection.IsSlotUnlocked(slotIndex)))
        {
            currentStage = BridgeConstructionStage.ReadyForMount;
            GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        }
        ApplyVisualState();
        bridgeComponent?.RefreshVisualAndColliderState();
    }

    public void SetLayoutLength(float panelLength, float nominalLength)
    {
        if (UsesPhysicalPanelPlacement || layoutRoot == null || nominalLength <= 0f) return;
        Vector3 scale = layoutRoot.localScale;
        scale.x = panelLength / nominalLength;
        layoutRoot.localScale = scale;
    }

    public override void NotifyMounted()
    {
        if (UsesPhysicalPanelPlacement) return;
        if (currentStage != BridgeConstructionStage.ReadyForMount) return;
        BridgeDeckPanelConstructionWorkflowSO settings = GetWorkflow();
        int offset = settings != null ? settings.InitialAlignmentOffset : 2;
        lateralAlignmentStep = HasAuthority() && Random.Range(0, 2) == 0 ? -offset : offset;
        rotationAlignmentStep = HasAuthority() && Random.Range(0, 2) == 0 ? -offset : offset;
        int minGap = settings != null ? settings.MinimumInitialGapStep : 1;
        int maxGap = settings != null ? settings.MaximumInitialGapStep : 2;
        leftGapStep = HasAuthority() ? Random.Range(minGap, maxGap + 1) : minGap;
        rightGapStep = HasAuthority() ? Random.Range(minGap, maxGap + 1) : minGap;
        for (int i = 0; i < fastenerProgress.Length; i++) fastenerProgress[i] = 0f;
        currentStage = BridgeConstructionStage.Aligning;
        ApplyVisualState();
    }

    public void RequestToolWork(EquippableItemSO item, int workPointId)
    {
        if (item == null || bridgeComponent == null) return;
        GameplayManager.Instance?.RequestConstructionSiteWork(
            bridgeComponent, item, item.ConstructionWorkPower, workPointId);
    }

    public override bool TryApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        if (UsesPhysicalPanelPlacement) return false;
        BridgeDeckPanelConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null || workPower <= 0f) return false;

        if (currentStage == BridgeConstructionStage.Aligning && toolType == settings.AlignmentTool)
        {
            bool changed = ApplyAlignmentWork(settings, (BridgeDeckPanelWorkPointId)workPointId);
            if (!changed) return false;
            if (lateralAlignmentStep == 0 && rotationAlignmentStep == 0)
            {
                currentStage = BridgeConstructionStage.GapSetting;
            }
            ApplyVisualState();
            return true;
        }

        if (currentStage == BridgeConstructionStage.GapSetting && toolType == settings.GapTool)
        {
            if (workPointId == (int)BridgeDeckPanelWorkPointId.LeftGap)
            {
                if (leftGapStep <= 0) return false;
                leftGapStep--;
            }
            else if (workPointId == (int)BridgeDeckPanelWorkPointId.RightGap)
            {
                if (rightGapStep <= 0) return false;
                rightGapStep--;
            }
            else
            {
                return false;
            }

            if (leftGapStep == 0 && rightGapStep == 0)
            {
                currentStage = BridgeConstructionStage.Fastening;
            }
            ApplyVisualState();
            return true;
        }

        if (currentStage != BridgeConstructionStage.Fastening || toolType != settings.FasteningTool)
        {
            return false;
        }

        BridgeDeckPanelWorkPointId[] order = GetFasteningOrder();
        int activeIndex = GetActiveFastenerIndex(settings.FastenerProgressNeeded);
        if (activeIndex < 0 || activeIndex >= order.Length || workPointId != (int)order[activeIndex])
        {
            return false;
        }

        int storageIndex = GetFastenerStorageIndex(order[activeIndex]);
        fastenerProgress[storageIndex] = Mathf.Min(
            settings.FastenerProgressNeeded,
            fastenerProgress[storageIndex] + workPower);
        if (GetActiveFastenerIndex(settings.FastenerProgressNeeded) >= order.Length)
        {
            currentStage = BridgeConstructionStage.Complete;
            bridgeComponent.CompleteConstructionFromSite();
        }

        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        return true;
    }

    public override bool CanApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        if (UsesPhysicalPanelPlacement) return false;
        BridgeDeckPanelConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null || workPower <= 0f) return false;

        BridgeDeckPanelWorkPointId point = (BridgeDeckPanelWorkPointId)workPointId;
        if (currentStage == BridgeConstructionStage.Aligning && toolType == settings.AlignmentTool)
        {
            return point switch
            {
                BridgeDeckPanelWorkPointId.StrikeLeft => lateralAlignmentStep < settings.MaximumAlignmentStep,
                BridgeDeckPanelWorkPointId.StrikeRight => lateralAlignmentStep > -settings.MaximumAlignmentStep,
                BridgeDeckPanelWorkPointId.StrikeClockwiseSide => rotationAlignmentStep > -settings.MaximumAlignmentStep,
                BridgeDeckPanelWorkPointId.StrikeCounterClockwiseSide => rotationAlignmentStep < settings.MaximumAlignmentStep,
                _ => false
            };
        }

        if (currentStage == BridgeConstructionStage.GapSetting && toolType == settings.GapTool)
        {
            return point == BridgeDeckPanelWorkPointId.LeftGap
                ? leftGapStep > 0
                : point == BridgeDeckPanelWorkPointId.RightGap && rightGapStep > 0;
        }

        BridgeDeckPanelWorkPointId[] order = GetFasteningOrder();
        int activeIndex = GetActiveFastenerIndex(settings.FastenerProgressNeeded);
        return currentStage == BridgeConstructionStage.Fastening &&
               toolType == settings.FasteningTool &&
               activeIndex >= 0 &&
               activeIndex < order.Length &&
               point == order[activeIndex];
    }

    private bool ApplyAlignmentWork(BridgeDeckPanelConstructionWorkflowSO settings, BridgeDeckPanelWorkPointId point)
    {
        switch (point)
        {
            case BridgeDeckPanelWorkPointId.StrikeLeft:
                if (lateralAlignmentStep >= settings.MaximumAlignmentStep) return false;
                lateralAlignmentStep++;
                return true;
            case BridgeDeckPanelWorkPointId.StrikeRight:
                if (lateralAlignmentStep <= -settings.MaximumAlignmentStep) return false;
                lateralAlignmentStep--;
                return true;
            case BridgeDeckPanelWorkPointId.StrikeClockwiseSide:
                if (rotationAlignmentStep <= -settings.MaximumAlignmentStep) return false;
                rotationAlignmentStep--;
                return true;
            case BridgeDeckPanelWorkPointId.StrikeCounterClockwiseSide:
                if (rotationAlignmentStep >= settings.MaximumAlignmentStep) return false;
                rotationAlignmentStep++;
                return true;
            default:
                return false;
        }
    }

    public void GetWorkPointPrompts(BridgeDeckPanelWorkPointId pointId, List<InteractionPrompt> prompts)
    {
        if (UsesPhysicalPanelPlacement || prompts == null) return;
        BridgeDeckPanelConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null) return;

        if (currentStage == BridgeConstructionStage.Aligning)
        {
            string action = pointId switch
            {
                BridgeDeckPanelWorkPointId.StrikeLeft => "Strike left side - move right",
                BridgeDeckPanelWorkPointId.StrikeRight => "Strike right side - move left",
                BridgeDeckPanelWorkPointId.StrikeClockwiseSide => "Strike clockwise side - rotate counter-clockwise",
                _ => "Strike counter-clockwise side - rotate clockwise"
            };
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"{action} - offset {lateralAlignmentStep}, angle {rotationAlignmentStep}"));
            return;
        }

        if (currentStage == BridgeConstructionStage.GapSetting)
        {
            bool left = pointId == BridgeDeckPanelWorkPointId.LeftGap;
            int value = left ? leftGapStep : rightGapStep;
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"Seat {(left ? "left" : "right")} edge - gap step {value}"));
            return;
        }

        if (currentStage == BridgeConstructionStage.Fastening)
        {
            BridgeDeckPanelWorkPointId[] order = GetFasteningOrder();
            int active = GetActiveFastenerIndex(settings.FastenerProgressNeeded);
            if (active < 0 || active >= order.Length) return;
            BridgeDeckPanelWorkPointId expected = order[active];
            int storageIndex = GetFastenerStorageIndex(pointId);
            float progress = storageIndex >= 0 ? fastenerProgress[storageIndex] : 0f;
            string suffix = pointId == expected ? string.Empty : $" - next: {GetDisplayName(expected)}";
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"Tighten {GetDisplayName(pointId)} - {Mathf.CeilToInt(progress)} / {Mathf.CeilToInt(settings.FastenerProgressNeeded)}{suffix}"));
        }
    }

    public override void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (prompts == null) return;
        if (UsesPhysicalPanelPlacement)
        {
            BridgeDeckPanelSectionNetworkState sectionState = deckSection != null ? deckSection.PhysicalPanelState : null;
            if (sectionState == null || !sectionState.TryGetSlotAssessment(slotIndex, out BridgeDeckPanelReplicatedState state))
            {
                if (IsComplete || bridgeComponent != null && bridgeComponent.StartFullyCompleted)
                {
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Deck panel secured"));
                    return;
                }
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                    "Deck panel: 0/4 - use F / E to place by hand on the marked main-girder slot, then place separate Mounting Nails through the panel into a main girder in each quadrant and strike each three times"));
                return;
            }
            if (state.Complete)
            {
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Deck panel secured"));
                return;
            }

            int count = CountQuarters(state.QuarterMask);
            string missing = BuildMissingQuarterText(state.QuarterMask);
            string cause = GetPlacementFailureText((BridgeDeckPanelPlacementFailure)state.Failure);
            string prompt = $"Deck panel: {count}/4";
            if (missing.Length > 0) prompt += $" - missing {missing}";
            if (cause.Length > 0 && cause != "missing fasteners") prompt += $" - {cause}";
            prompt += " - place by hand on the marked main-girder slot; place a separate Mounting Nail in each quadrant and strike each 3 times";
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, prompt));
            return;
        }
        if (currentStage == BridgeConstructionStage.WaitingForPrevious)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Complete the previous deck panel first"));
        }
        else if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            bridgeComponent.AddReadyForMountPrompt(prompts, "Deliver Wooden Deck Panel");
        }
    }

    public void RefreshPhysicalPlacementPrompt()
    {
        // The existing prompt UI reads this provider every frame; state changes need no extra UI event path.
    }

    private static int CountQuarters(byte mask)
    {
        int count = 0;
        for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) count++;
        return count;
    }

    private static string BuildMissingQuarterText(byte mask)
    {
        string missing = string.Empty;
        AppendMissing(ref missing, mask, BridgeDeckPanelQuarterMask.NegativeXNegativeZ, "back-left");
        AppendMissing(ref missing, mask, BridgeDeckPanelQuarterMask.PositiveXNegativeZ, "back-right");
        AppendMissing(ref missing, mask, BridgeDeckPanelQuarterMask.NegativeXPositiveZ, "front-left");
        AppendMissing(ref missing, mask, BridgeDeckPanelQuarterMask.PositiveXPositiveZ, "front-right");
        return missing;
    }

    private static void AppendMissing(ref string text, byte mask, BridgeDeckPanelQuarterMask quarter, string name)
    {
        if (((BridgeDeckPanelQuarterMask)mask & quarter) != 0) return;
        if (text.Length > 0) text += ", ";
        text += name;
    }

    private static string GetPlacementFailureText(BridgeDeckPanelPlacementFailure failure) => failure switch
    {
        BridgeDeckPanelPlacementFailure.None => string.Empty,
        BridgeDeckPanelPlacementFailure.MissingQuarters => "missing fasteners",
        BridgeDeckPanelPlacementFailure.HorizontalOffset => "position out of alignment",
        BridgeDeckPanelPlacementFailure.HeightOffset => "height out of alignment",
        BridgeDeckPanelPlacementFailure.Orientation => "incorrect orientation",
        BridgeDeckPanelPlacementFailure.SlotOccupied => "slot occupied",
        BridgeDeckPanelPlacementFailure.NeighborPenetration => "overlapping adjacent panel",
        _ => "mounting area unavailable"
    };

    public override void PopulateNetworkState(ref BridgeComponentNetworkState state)
    {
        if (UsesPhysicalPanelPlacement && (bridgeComponent == null || !bridgeComponent.StartFullyCompleted)
            && currentStage != BridgeConstructionStage.Complete)
        {
            base.PopulateNetworkState(ref state);
            state.constructionStage = (int)BridgeConstructionStage.ReadyForMount;
            state.constructionProgress = 0f;
            state.constructionValueA = 0;
            state.constructionValueB = 0;
            state.constructionAux0 = 0f;
            state.constructionAux1 = 0f;
            state.constructionAnchor0 = 0f;
            state.constructionAnchor1 = 0f;
            state.constructionAnchor2 = 0f;
            state.constructionAnchor3 = 0f;
            return;
        }
        base.PopulateNetworkState(ref state);
        state.constructionValueA = lateralAlignmentStep;
        state.constructionValueB = rotationAlignmentStep;
        state.constructionAux0 = leftGapStep;
        state.constructionAux1 = rightGapStep;
        state.constructionAnchor0 = fastenerProgress[0];
        state.constructionAnchor1 = fastenerProgress[1];
        state.constructionAnchor2 = fastenerProgress[2];
        state.constructionAnchor3 = fastenerProgress[3];
    }

    public override void ApplyNetworkState(BridgeComponentNetworkState state)
    {
        if (UsesPhysicalPanelPlacement && (bridgeComponent == null || !bridgeComponent.StartFullyCompleted)
            && state.constructionStage != (int)BridgeConstructionStage.Complete)
        {
            currentStage = BridgeConstructionStage.ReadyForMount;
            currentWorkProgress = 0f;
            lateralAlignmentStep = 0;
            rotationAlignmentStep = 0;
            leftGapStep = 0;
            rightGapStep = 0;
            for (int i = 0; i < fastenerProgress.Length; i++) fastenerProgress[i] = 0f;
            ApplyVisualState();
            bridgeComponent.RefreshVisualAndColliderState();
            return;
        }
        currentStage = (BridgeConstructionStage)state.constructionStage;
        currentWorkProgress = Mathf.Max(0f, state.constructionProgress);
        lateralAlignmentStep = state.constructionValueA;
        rotationAlignmentStep = state.constructionValueB;
        leftGapStep = Mathf.RoundToInt(state.constructionAux0);
        rightGapStep = Mathf.RoundToInt(state.constructionAux1);
        fastenerProgress[0] = state.constructionAnchor0;
        fastenerProgress[1] = state.constructionAnchor1;
        fastenerProgress[2] = state.constructionAnchor2;
        fastenerProgress[3] = state.constructionAnchor3;
        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
    }

    public override bool ShouldEnablePhysicalColliders(bool isMounted)
    {
        return isMounted && currentStage == BridgeConstructionStage.Complete;
    }

    public override MonoBehaviour GetWorkValidationTarget(int workPointId)
    {
        if (workPoints != null)
        {
            foreach (BridgeDeckPanelWorkPoint point in workPoints)
            {
                if (point != null && (int)point.WorkPointId == workPointId) return point;
            }
        }
        return this;
    }

    protected override void ApplyVisualState()
    {
        BridgeDeckPanelConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null) return;

        if (UsesPhysicalPanelPlacement)
        {
            bool debugCompletedPanel = bridgeComponent != null && bridgeComponent.StartFullyCompleted;
            if (panelVisualRoot != null) panelVisualRoot.gameObject.SetActive(debugCompletedPanel);
            if (alignmentIndicatorRenderer != null) alignmentIndicatorRenderer.gameObject.SetActive(false);
            if (workPoints != null)
            {
                foreach (BridgeDeckPanelWorkPoint point in workPoints)
                {
                    if (point == null) continue;
                    foreach (Collider pointCollider in point.GetComponentsInChildren<Collider>(true))
                    {
                        if (pointCollider != null) pointCollider.enabled = false;
                    }
                    point.gameObject.SetActive(false);
                }
            }
            return;
        }

        if (panelVisualRoot != null)
        {
            bool panelIsMounted = currentStage == BridgeConstructionStage.Aligning ||
                                  currentStage == BridgeConstructionStage.GapSetting ||
                                  currentStage == BridgeConstructionStage.Fastening ||
                                  currentStage == BridgeConstructionStage.Complete;
            panelVisualRoot.gameObject.SetActive(panelIsMounted);

            float averageGap = (leftGapStep + rightGapStep) * 0.5f * settings.GapStepDistance;
            float gapYaw = (rightGapStep - leftGapStep) * settings.RotationStepDegrees * 0.5f;
            panelVisualRoot.localPosition = panelBasePosition +
                new Vector3(averageGap, 0f, lateralAlignmentStep * settings.LateralStepDistance);
            panelVisualRoot.localRotation = panelBaseRotation *
                Quaternion.Euler(0f, rotationAlignmentStep * settings.RotationStepDegrees + gapYaw, 0f);
        }

        bool aligned = lateralAlignmentStep == 0 && rotationAlignmentStep == 0 &&
                       (currentStage != BridgeConstructionStage.GapSetting || (leftGapStep == 0 && rightGapStep == 0));
        if (alignmentIndicatorRenderer != null)
        {
            alignmentIndicatorRenderer.gameObject.SetActive(
                currentStage == BridgeConstructionStage.Aligning || currentStage == BridgeConstructionStage.GapSetting);
            indicatorPropertyBlock ??= new MaterialPropertyBlock();
            alignmentIndicatorRenderer.GetPropertyBlock(indicatorPropertyBlock);
            Color color = aligned ? alignedColor : misalignedColor;
            indicatorPropertyBlock.SetColor("_BaseColor", color);
            indicatorPropertyBlock.SetColor("_Color", color);
            alignmentIndicatorRenderer.SetPropertyBlock(indicatorPropertyBlock);
        }

        BridgeDeckPanelWorkPointId[] order = GetFasteningOrder();
        int activeFastener = GetActiveFastenerIndex(settings.FastenerProgressNeeded);
        BridgeDeckPanelWorkPointId expected = activeFastener >= 0 && activeFastener < order.Length
            ? order[activeFastener]
            : BridgeDeckPanelWorkPointId.FrontLeft;
        if (workPoints == null) return;
        foreach (BridgeDeckPanelWorkPoint point in workPoints)
        {
            if (point == null) continue;
            int id = (int)point.WorkPointId;
            bool active = (currentStage == BridgeConstructionStage.Aligning && id >= 0 && id <= 3) ||
                          (currentStage == BridgeConstructionStage.GapSetting && id >= 10 && id <= 11) ||
                          (currentStage == BridgeConstructionStage.Fastening && point.WorkPointId == expected);
            point.gameObject.SetActive(active);
            point.SetHighlighted(currentStage == BridgeConstructionStage.Fastening && point.WorkPointId == expected);
        }
    }

    private BridgeDeckPanelConstructionWorkflowSO GetWorkflow()
    {
        if (deckWorkflow == null && bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null)
        {
            deckWorkflow = bridgeComponent.GetBridgeComponentSO().deckPanelConstructionWorkflow;
        }
        return deckWorkflow;
    }

    private BridgeDeckPanelWorkPointId[] GetFasteningOrder()
    {
        return usesFullFastening ? FullFasteningOrder : ShortFasteningOrder;
    }

    private int GetActiveFastenerIndex(float required)
    {
        BridgeDeckPanelWorkPointId[] order = GetFasteningOrder();
        for (int i = 0; i < order.Length; i++)
        {
            int storageIndex = GetFastenerStorageIndex(order[i]);
            if (storageIndex >= 0 && fastenerProgress[storageIndex] < required) return i;
        }
        return order.Length;
    }

    private static int GetFastenerStorageIndex(BridgeDeckPanelWorkPointId id)
    {
        return id switch
        {
            BridgeDeckPanelWorkPointId.FrontLeft => 0,
            BridgeDeckPanelWorkPointId.BackRight => 1,
            BridgeDeckPanelWorkPointId.FrontRight => 2,
            BridgeDeckPanelWorkPointId.BackLeft => 3,
            _ => -1
        };
    }

    private static string GetDisplayName(BridgeDeckPanelWorkPointId id)
    {
        return id switch
        {
            BridgeDeckPanelWorkPointId.FrontLeft => "front left",
            BridgeDeckPanelWorkPointId.BackRight => "back right",
            BridgeDeckPanelWorkPointId.FrontRight => "front right",
            BridgeDeckPanelWorkPointId.BackLeft => "back left",
            _ => id.ToString()
        };
    }

    private static bool HasAuthority()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
    }
}
