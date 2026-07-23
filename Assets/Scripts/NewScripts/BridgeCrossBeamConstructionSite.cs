using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BridgeCrossBeamConstructionSite : BridgeConstructionSite
{
    private const int FastenerCount = 4;

    [Header("Dependencies")]
    [SerializeField] private BridgeComponent[] prerequisiteGirders;

    [Header("Cross beam visuals")]
    [SerializeField] private Transform crossBeamVisualRoot;
    [SerializeField] private Transform leftClampVisual;
    [SerializeField] private Transform rightClampVisual;
    [SerializeField] private Renderer alignmentIndicatorRenderer;
    [SerializeField] private Color misalignedColor = new Color(0.8f, 0.15f, 0.1f, 1f);
    [SerializeField] private Color almostAlignedColor = new Color(0.9f, 0.65f, 0.1f, 1f);
    [SerializeField] private Color alignedColor = new Color(0.15f, 0.8f, 0.2f, 1f);

    private BridgeCrossBeamConstructionWorkflowSO crossBeamWorkflow;
    private BridgeCrossBeamWorkPoint[] workPoints;
    private int alignmentStep;
    private float leftClampProgress;
    private float rightClampProgress;
    private readonly float[] fastenerProgress = new float[FastenerCount];
    private MaterialPropertyBlock indicatorPropertyBlock;

    public int AlignmentStep => alignmentStep;
    public float LeftClampProgress => leftClampProgress;
    public float RightClampProgress => rightClampProgress;
    public IReadOnlyList<float> FastenerProgress => fastenerProgress;
    public override bool CanAcceptMountedComponent => currentStage == BridgeConstructionStage.ReadyForMount;

    protected override void Awake()
    {
        workPoints = GetComponentsInChildren<BridgeCrossBeamWorkPoint>(true);
        base.Awake();
        crossBeamWorkflow = bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null
            ? bridgeComponent.GetBridgeComponentSO().crossBeamConstructionWorkflow
            : null;
        ApplyVisualState();
    }

    private void Update()
    {
        if (currentStage != BridgeConstructionStage.WaitingForGirders || !HasAuthority() || !ArePrerequisitesComplete())
        {
            return;
        }

        currentStage = BridgeConstructionStage.ReadyForMount;
        ApplyVisualState();
        GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
    }

    protected override BridgeConstructionStage GetInitialStage()
    {
        return ArePrerequisitesComplete()
            ? BridgeConstructionStage.ReadyForMount
            : BridgeConstructionStage.WaitingForGirders;
    }

    public override void NotifyMounted()
    {
        if (currentStage != BridgeConstructionStage.ReadyForMount)
        {
            return;
        }

        BridgeCrossBeamConstructionWorkflowSO settings = GetWorkflow();
        int maximumStep = settings != null ? settings.MaximumAlignmentStep : 2;
        alignmentStep = HasAuthority() && Random.Range(0, 2) == 0 ? -maximumStep : maximumStep;
        leftClampProgress = 0f;
        rightClampProgress = 0f;
        for (int i = 0; i < fastenerProgress.Length; i++) fastenerProgress[i] = 0f;
        currentStage = BridgeConstructionStage.Aligning;
        ApplyVisualState();
    }

    public void RequestToolWork(EquippableItemSO item, int workPointId)
    {
        if (item == null || bridgeComponent == null)
        {
            return;
        }

        GameplayManager.Instance?.RequestConstructionSiteWork(
            bridgeComponent,
            item,
            item.ConstructionWorkPower,
            workPointId);
    }

    public override bool TryApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        BridgeCrossBeamConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null || workPower <= 0f)
        {
            return false;
        }

        if (currentStage == BridgeConstructionStage.Aligning && toolType == settings.AlignmentTool)
        {
            if (workPointId == (int)BridgeCrossBeamWorkPointId.MoveLeft)
            {
                if (alignmentStep >= settings.MaximumAlignmentStep) return false;
                alignmentStep++;
            }
            else if (workPointId == (int)BridgeCrossBeamWorkPointId.MoveRight)
            {
                if (alignmentStep <= -settings.MaximumAlignmentStep) return false;
                alignmentStep--;
            }
            else
            {
                return false;
            }

            if (alignmentStep == 0)
            {
                currentStage = BridgeConstructionStage.Clamping;
            }

            ApplyVisualState();
            return true;
        }

        if (currentStage == BridgeConstructionStage.Clamping && toolType == settings.ClampingTool)
        {
            bool isLeft = workPointId == (int)BridgeCrossBeamWorkPointId.LeftClamp;
            bool isRight = workPointId == (int)BridgeCrossBeamWorkPointId.RightClamp;
            if (!isLeft && !isRight) return false;

            float current = isLeft ? leftClampProgress : rightClampProgress;
            float other = isLeft ? rightClampProgress : leftClampProgress;
            float next = Mathf.Min(settings.ClampProgressNeeded, current + workPower);
            if (next - other > settings.MaximumClampProgressDifference + Mathf.Epsilon)
            {
                return false;
            }

            if (isLeft) leftClampProgress = next;
            else rightClampProgress = next;

            if (leftClampProgress >= settings.ClampProgressNeeded && rightClampProgress >= settings.ClampProgressNeeded)
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

        int fastenerIndex = workPointId - (int)BridgeCrossBeamWorkPointId.Fastener0;
        if (fastenerIndex < 0 || fastenerIndex >= FastenerCount || fastenerProgress[fastenerIndex] >= settings.FastenerProgressNeeded)
        {
            return false;
        }

        fastenerProgress[fastenerIndex] = Mathf.Min(
            settings.FastenerProgressNeeded,
            fastenerProgress[fastenerIndex] + workPower);
        if (AreAllFastenersComplete(settings.FastenerProgressNeeded))
        {
            currentStage = BridgeConstructionStage.Complete;
            bridgeComponent.CompleteConstructionFromSite();
        }

        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        return true;
    }

    public void GetWorkPointPrompts(BridgeCrossBeamWorkPointId pointId, List<InteractionPrompt> prompts)
    {
        BridgeCrossBeamConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null) return;

        if (currentStage == BridgeConstructionStage.Aligning)
        {
            string struckSide = pointId == BridgeCrossBeamWorkPointId.MoveLeft ? "left" : "right";
            string direction = pointId == BridgeCrossBeamWorkPointId.MoveLeft ? "right" : "left";
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"Strike {struckSide} side - move beam {direction} - offset {alignmentStep}"));
            return;
        }

        if (currentStage == BridgeConstructionStage.Clamping)
        {
            bool isLeft = pointId == BridgeCrossBeamWorkPointId.LeftClamp;
            float value = isLeft ? leftClampProgress : rightClampProgress;
            float other = isLeft ? rightClampProgress : leftClampProgress;
            bool blocked = value >= other + settings.MaximumClampProgressDifference - Mathf.Epsilon && value < settings.ClampProgressNeeded;
            string suffix = blocked ? " - tighten other side" : string.Empty;
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"Tighten {(isLeft ? "left" : "right")} clamp - {Mathf.CeilToInt(value)} / {Mathf.CeilToInt(settings.ClampProgressNeeded)}{suffix}"));
            return;
        }

        if (currentStage == BridgeConstructionStage.Fastening)
        {
            int index = (int)pointId - (int)BridgeCrossBeamWorkPointId.Fastener0;
            if (index >= 0 && index < FastenerCount)
            {
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                    $"Secure cross beam {index + 1} - {Mathf.CeilToInt(fastenerProgress[index])} / {Mathf.CeilToInt(settings.FastenerProgressNeeded)}"));
            }
        }
    }

    public override void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (currentStage == BridgeConstructionStage.WaitingForGirders)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Complete both main girders first"));
        }
        else if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Deliver Wooden Cross Beam"));
        }
    }

    public override void PopulateNetworkState(ref BridgeComponentNetworkState state)
    {
        base.PopulateNetworkState(ref state);
        state.constructionValueA = alignmentStep;
        state.constructionAux0 = leftClampProgress;
        state.constructionAux1 = rightClampProgress;
        state.constructionAnchor0 = fastenerProgress[0];
        state.constructionAnchor1 = fastenerProgress[1];
        state.constructionAnchor2 = fastenerProgress[2];
        state.constructionAnchor3 = fastenerProgress[3];
    }

    public override void ApplyNetworkState(BridgeComponentNetworkState state)
    {
        currentStage = (BridgeConstructionStage)state.constructionStage;
        currentWorkProgress = Mathf.Max(0f, state.constructionProgress);
        alignmentStep = state.constructionValueA;
        leftClampProgress = state.constructionAux0;
        rightClampProgress = state.constructionAux1;
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
            foreach (BridgeCrossBeamWorkPoint point in workPoints)
            {
                if (point != null && (int)point.WorkPointId == workPointId) return point;
            }
        }
        return this;
    }

    protected override void ApplyVisualState()
    {
        BridgeCrossBeamConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null) return;

        if (crossBeamVisualRoot != null)
        {
            crossBeamVisualRoot.localPosition = new Vector3(
                alignmentStep * settings.AlignmentStepDistance,
                crossBeamVisualRoot.localPosition.y,
                crossBeamVisualRoot.localPosition.z);
        }

        UpdateClampVisual(leftClampVisual, leftClampProgress, settings.ClampProgressNeeded);
        UpdateClampVisual(rightClampVisual, rightClampProgress, settings.ClampProgressNeeded);

        if (alignmentIndicatorRenderer != null)
        {
            alignmentIndicatorRenderer.gameObject.SetActive(currentStage == BridgeConstructionStage.Aligning || currentStage == BridgeConstructionStage.Clamping);
            indicatorPropertyBlock ??= new MaterialPropertyBlock();
            alignmentIndicatorRenderer.GetPropertyBlock(indicatorPropertyBlock);
            Color color = alignmentStep == 0 ? alignedColor : Mathf.Abs(alignmentStep) == 1 ? almostAlignedColor : misalignedColor;
            indicatorPropertyBlock.SetColor("_BaseColor", color);
            indicatorPropertyBlock.SetColor("_Color", color);
            alignmentIndicatorRenderer.SetPropertyBlock(indicatorPropertyBlock);
        }

        if (workPoints != null)
        {
            foreach (BridgeCrossBeamWorkPoint point in workPoints)
            {
                if (point == null) continue;
                int id = (int)point.WorkPointId;
                bool active = (currentStage == BridgeConstructionStage.Aligning && id <= 1) ||
                              (currentStage == BridgeConstructionStage.Clamping && id >= 10 && id <= 11) ||
                              (currentStage == BridgeConstructionStage.Fastening && id >= 20 && id <= 23);
                point.gameObject.SetActive(active);
            }
        }
    }

    private BridgeCrossBeamConstructionWorkflowSO GetWorkflow()
    {
        if (crossBeamWorkflow == null && bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null)
        {
            crossBeamWorkflow = bridgeComponent.GetBridgeComponentSO().crossBeamConstructionWorkflow;
        }
        return crossBeamWorkflow;
    }

    private bool ArePrerequisitesComplete()
    {
        if (prerequisiteGirders == null || prerequisiteGirders.Length == 0) return true;
        foreach (BridgeComponent girder in prerequisiteGirders)
        {
            if (girder != null && !girder.IsAssembled) return false;
        }
        return true;
    }

    private bool AreAllFastenersComplete(float required)
    {
        for (int i = 0; i < FastenerCount; i++)
        {
            if (fastenerProgress[i] < required) return false;
        }
        return true;
    }

    private static void UpdateClampVisual(Transform target, float progress, float required)
    {
        if (target == null) return;
        float normalized = required > 0f ? Mathf.Clamp01(progress / required) : 0f;
        target.localRotation = Quaternion.Euler(0f, normalized * 180f, 0f);
    }

    private static bool HasAuthority()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
    }
}
