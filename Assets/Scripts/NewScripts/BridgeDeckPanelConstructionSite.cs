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
    public bool IsComplete => currentStage == BridgeConstructionStage.Complete;
    public override bool CanAcceptMountedComponent =>
        currentStage == BridgeConstructionStage.ReadyForMount &&
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
        ApplyVisualState();
    }

    private void Update()
    {
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

    public void RefreshSectionAvailability()
    {
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
        if (layoutRoot == null || nominalLength <= 0f) return;
        Vector3 scale = layoutRoot.localScale;
        scale.x = panelLength / nominalLength;
        layoutRoot.localScale = scale;
    }

    public override void NotifyMounted()
    {
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
        if (currentStage == BridgeConstructionStage.WaitingForPrevious)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Complete the previous deck panel first"));
        }
        else if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Deliver Wooden Deck Panel"));
        }
    }

    public override void PopulateNetworkState(ref BridgeComponentNetworkState state)
    {
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
