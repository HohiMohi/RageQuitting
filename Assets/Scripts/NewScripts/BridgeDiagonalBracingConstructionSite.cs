using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum DiagonalBracingOrientation
{
    ForwardSlash,
    BackSlash
}

public class BridgeDiagonalBracingConstructionSite : BridgeConstructionSite
{
    private const int FastenerCount = 4;
    private static readonly BridgeDiagonalBracingWorkPointId[] FasteningOrder =
    {
        BridgeDiagonalBracingWorkPointId.StartTop,
        BridgeDiagonalBracingWorkPointId.EndBottom,
        BridgeDiagonalBracingWorkPointId.StartBottom,
        BridgeDiagonalBracingWorkPointId.EndTop
    };

    [Header("Dependencies")]
    [SerializeField] private BridgeComponent[] prerequisiteCrossBeams;

    [Header("Diagonal bracing")]
    [SerializeField] private DiagonalBracingOrientation orientation;
    [SerializeField] private Transform bracingVisualRoot;
    [SerializeField] private Transform ghostVisualRoot;
    [SerializeField] private Transform startTemporaryFixVisual;
    [SerializeField] private Transform endTemporaryFixVisual;
    [SerializeField] private Renderer alignmentIndicatorRenderer;
    [SerializeField] private Color misalignedColor = new Color(0.8f, 0.15f, 0.1f, 1f);
    [SerializeField] private Color almostAlignedColor = new Color(0.9f, 0.65f, 0.1f, 1f);
    [SerializeField] private Color alignedColor = new Color(0.15f, 0.8f, 0.2f, 1f);

    private BridgeDiagonalBracingConstructionWorkflowSO diagonalWorkflow;
    private BridgeDiagonalBracingWorkPoint[] workPoints;
    private int alignmentStep;
    private int currentFasteningIndex;
    private float startTemporaryFixProgress;
    private float endTemporaryFixProgress;
    private readonly float[] fastenerProgress = new float[FastenerCount];
    private MaterialPropertyBlock indicatorPropertyBlock;

    public DiagonalBracingOrientation Orientation => orientation;
    public int AlignmentStep => alignmentStep;
    public float StartTemporaryFixProgress => startTemporaryFixProgress;
    public float EndTemporaryFixProgress => endTemporaryFixProgress;
    public int CurrentFasteningIndex => currentFasteningIndex;
    public IReadOnlyList<float> FastenerProgress => fastenerProgress;
    public override bool CanAcceptMountedComponent => currentStage == BridgeConstructionStage.ReadyForMount;

    protected override void Awake()
    {
        workPoints = GetComponentsInChildren<BridgeDiagonalBracingWorkPoint>(true);
        base.Awake();
        diagonalWorkflow = bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null
            ? bridgeComponent.GetBridgeComponentSO().diagonalBracingConstructionWorkflow
            : null;
        ApplyVisualState();
    }

    private void Update()
    {
        if (currentStage != BridgeConstructionStage.WaitingForCrossBeams || !HasAuthority() || !ArePrerequisitesComplete())
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
            : BridgeConstructionStage.WaitingForCrossBeams;
    }

    public override void NotifyMounted()
    {
        if (currentStage != BridgeConstructionStage.ReadyForMount)
        {
            return;
        }

        BridgeDiagonalBracingConstructionWorkflowSO settings = GetWorkflow();
        int initialOffset = settings != null ? settings.InitialAlignmentOffset : 2;
        alignmentStep = HasAuthority() && Random.Range(0, 2) == 0 ? -initialOffset : initialOffset;
        currentFasteningIndex = 0;
        startTemporaryFixProgress = 0f;
        endTemporaryFixProgress = 0f;
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
        BridgeDiagonalBracingConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null || workPower <= 0f)
        {
            return false;
        }

        if (currentStage == BridgeConstructionStage.Aligning && toolType == settings.AlignmentTool)
        {
            if (workPointId == (int)BridgeDiagonalBracingWorkPointId.RotateCounterClockwise)
            {
                if (alignmentStep >= settings.MaximumAlignmentStep) return false;
                alignmentStep++;
            }
            else if (workPointId == (int)BridgeDiagonalBracingWorkPointId.RotateClockwise)
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
                currentStage = BridgeConstructionStage.TemporaryFixing;
            }

            ApplyVisualState();
            return true;
        }

        if (currentStage == BridgeConstructionStage.TemporaryFixing && toolType == settings.TemporaryFixingTool)
        {
            bool isStart = workPointId == (int)BridgeDiagonalBracingWorkPointId.StartTemporaryFix;
            bool isEnd = workPointId == (int)BridgeDiagonalBracingWorkPointId.EndTemporaryFix;
            if (!isStart && !isEnd) return false;

            if (isStart)
            {
                startTemporaryFixProgress = Mathf.Min(settings.TemporaryFixProgressNeeded, startTemporaryFixProgress + workPower);
            }
            else
            {
                endTemporaryFixProgress = Mathf.Min(settings.TemporaryFixProgressNeeded, endTemporaryFixProgress + workPower);
            }

            if (startTemporaryFixProgress >= settings.TemporaryFixProgressNeeded &&
                endTemporaryFixProgress >= settings.TemporaryFixProgressNeeded)
            {
                currentStage = BridgeConstructionStage.Fastening;
                currentFasteningIndex = 0;
            }

            ApplyVisualState();
            return true;
        }

        if (currentStage != BridgeConstructionStage.Fastening ||
            toolType != settings.FasteningTool ||
            currentFasteningIndex < 0 ||
            currentFasteningIndex >= FastenerCount)
        {
            return false;
        }

        BridgeDiagonalBracingWorkPointId expectedPoint = FasteningOrder[currentFasteningIndex];
        if (workPointId != (int)expectedPoint)
        {
            return false;
        }

        fastenerProgress[currentFasteningIndex] = Mathf.Min(
            settings.FastenerProgressNeeded,
            fastenerProgress[currentFasteningIndex] + workPower);
        if (fastenerProgress[currentFasteningIndex] >= settings.FastenerProgressNeeded)
        {
            currentFasteningIndex++;
            if (currentFasteningIndex >= FastenerCount)
            {
                currentStage = BridgeConstructionStage.Complete;
                bridgeComponent.CompleteConstructionFromSite();
            }
        }

        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
        return true;
    }

    public override bool CanApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        BridgeDiagonalBracingConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null || workPower <= 0f) return false;

        if (currentStage == BridgeConstructionStage.Aligning && toolType == settings.AlignmentTool)
        {
            return workPointId == (int)BridgeDiagonalBracingWorkPointId.RotateCounterClockwise
                ? alignmentStep < settings.MaximumAlignmentStep
                : workPointId == (int)BridgeDiagonalBracingWorkPointId.RotateClockwise &&
                  alignmentStep > -settings.MaximumAlignmentStep;
        }

        if (currentStage == BridgeConstructionStage.TemporaryFixing &&
            toolType == settings.TemporaryFixingTool)
        {
            return workPointId == (int)BridgeDiagonalBracingWorkPointId.StartTemporaryFix
                ? startTemporaryFixProgress < settings.TemporaryFixProgressNeeded
                : workPointId == (int)BridgeDiagonalBracingWorkPointId.EndTemporaryFix &&
                  endTemporaryFixProgress < settings.TemporaryFixProgressNeeded;
        }

        return currentStage == BridgeConstructionStage.Fastening &&
               toolType == settings.FasteningTool &&
               currentFasteningIndex >= 0 &&
               currentFasteningIndex < FastenerCount &&
               workPointId == (int)FasteningOrder[currentFasteningIndex] &&
               fastenerProgress[currentFasteningIndex] < settings.FastenerProgressNeeded;
    }

    public void GetWorkPointPrompts(BridgeDiagonalBracingWorkPointId pointId, List<InteractionPrompt> prompts)
    {
        BridgeDiagonalBracingConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null) return;

        if (currentStage == BridgeConstructionStage.Aligning)
        {
            string struckSide = pointId == BridgeDiagonalBracingWorkPointId.RotateClockwise
                ? "clockwise"
                : "counter-clockwise";
            string direction = pointId == BridgeDiagonalBracingWorkPointId.RotateClockwise
                ? "counter-clockwise"
                : "clockwise";
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"Strike {struckSide} side - rotate {direction} - offset {alignmentStep}"));
            return;
        }

        if (currentStage == BridgeConstructionStage.TemporaryFixing)
        {
            bool isStart = pointId == BridgeDiagonalBracingWorkPointId.StartTemporaryFix;
            float progress = isStart ? startTemporaryFixProgress : endTemporaryFixProgress;
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"Temporarily secure {(isStart ? "start" : "end")} - {Mathf.CeilToInt(progress)} / {Mathf.CeilToInt(settings.TemporaryFixProgressNeeded)}"));
            return;
        }

        if (currentStage == BridgeConstructionStage.Fastening)
        {
            BridgeDiagonalBracingWorkPointId expected = FasteningOrder[Mathf.Clamp(currentFasteningIndex, 0, FastenerCount - 1)];
            int pointIndex = GetFastenerIndex(pointId);
            float progress = pointIndex >= 0 ? fastenerProgress[pointIndex] : 0f;
            string suffix = pointId == expected ? string.Empty : $" - next: {GetPointDisplayName(expected)}";
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"Tighten {GetPointDisplayName(pointId)} - {Mathf.CeilToInt(progress)} / {Mathf.CeilToInt(settings.FastenerProgressNeeded)}{suffix}"));
        }
    }

    public override void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (currentStage == BridgeConstructionStage.WaitingForCrossBeams)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Complete required cross beams first"));
        }
        else if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            bridgeComponent.AddReadyForMountPrompt(prompts, "Deliver Wooden Diagonal Bracing");
        }
    }

    public override void PopulateNetworkState(ref BridgeComponentNetworkState state)
    {
        base.PopulateNetworkState(ref state);
        state.constructionValueA = alignmentStep;
        state.constructionValueB = currentFasteningIndex;
        state.constructionAux0 = startTemporaryFixProgress;
        state.constructionAux1 = endTemporaryFixProgress;
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
        currentFasteningIndex = state.constructionValueB;
        startTemporaryFixProgress = state.constructionAux0;
        endTemporaryFixProgress = state.constructionAux1;
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
            foreach (BridgeDiagonalBracingWorkPoint point in workPoints)
            {
                if (point != null && (int)point.WorkPointId == workPointId) return point;
            }
        }
        return this;
    }

    protected override void ApplyVisualState()
    {
        BridgeDiagonalBracingConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null) return;

        if (bracingVisualRoot != null)
        {
            float targetYaw = orientation == DiagonalBracingOrientation.ForwardSlash ? 45f : -45f;
            bracingVisualRoot.localRotation = Quaternion.Euler(0f, targetYaw + alignmentStep * settings.AlignmentAngleStep, 0f);
            if (ghostVisualRoot != null)
            {
                ghostVisualRoot.localRotation = Quaternion.Euler(0f, targetYaw, 0f);
            }
        }

        UpdateTemporaryFixVisual(startTemporaryFixVisual, startTemporaryFixProgress, settings.TemporaryFixProgressNeeded);
        UpdateTemporaryFixVisual(endTemporaryFixVisual, endTemporaryFixProgress, settings.TemporaryFixProgressNeeded);

        if (alignmentIndicatorRenderer != null)
        {
            alignmentIndicatorRenderer.gameObject.SetActive(
                currentStage == BridgeConstructionStage.Aligning ||
                currentStage == BridgeConstructionStage.TemporaryFixing);
            indicatorPropertyBlock ??= new MaterialPropertyBlock();
            alignmentIndicatorRenderer.GetPropertyBlock(indicatorPropertyBlock);
            Color color = alignmentStep == 0 ? alignedColor : Mathf.Abs(alignmentStep) == 1 ? almostAlignedColor : misalignedColor;
            indicatorPropertyBlock.SetColor("_BaseColor", color);
            indicatorPropertyBlock.SetColor("_Color", color);
            alignmentIndicatorRenderer.SetPropertyBlock(indicatorPropertyBlock);
        }

        if (workPoints != null)
        {
            BridgeDiagonalBracingWorkPointId expected = currentFasteningIndex >= 0 && currentFasteningIndex < FastenerCount
                ? FasteningOrder[currentFasteningIndex]
                : BridgeDiagonalBracingWorkPointId.StartTop;
            foreach (BridgeDiagonalBracingWorkPoint point in workPoints)
            {
                if (point == null) continue;
                int id = (int)point.WorkPointId;
                bool active = (currentStage == BridgeConstructionStage.Aligning && id <= 1) ||
                              (currentStage == BridgeConstructionStage.TemporaryFixing && id >= 10 && id <= 11) ||
                              (currentStage == BridgeConstructionStage.Fastening && point.WorkPointId == expected);
                point.gameObject.SetActive(active);
                point.SetHighlighted(currentStage == BridgeConstructionStage.Fastening && point.WorkPointId == expected);
            }
        }
    }

    private BridgeDiagonalBracingConstructionWorkflowSO GetWorkflow()
    {
        if (diagonalWorkflow == null && bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null)
        {
            diagonalWorkflow = bridgeComponent.GetBridgeComponentSO().diagonalBracingConstructionWorkflow;
        }
        return diagonalWorkflow;
    }

    private bool ArePrerequisitesComplete()
    {
        if (prerequisiteCrossBeams == null || prerequisiteCrossBeams.Length == 0) return true;
        foreach (BridgeComponent crossBeam in prerequisiteCrossBeams)
        {
            if (crossBeam != null && !crossBeam.IsAssembled) return false;
        }
        return true;
    }

    private static int GetFastenerIndex(BridgeDiagonalBracingWorkPointId pointId)
    {
        for (int i = 0; i < FasteningOrder.Length; i++)
        {
            if (FasteningOrder[i] == pointId) return i;
        }
        return -1;
    }

    private static string GetPointDisplayName(BridgeDiagonalBracingWorkPointId pointId)
    {
        return pointId switch
        {
            BridgeDiagonalBracingWorkPointId.StartTop => "start top",
            BridgeDiagonalBracingWorkPointId.EndBottom => "end bottom",
            BridgeDiagonalBracingWorkPointId.StartBottom => "start bottom",
            BridgeDiagonalBracingWorkPointId.EndTop => "end top",
            _ => pointId.ToString()
        };
    }

    private static void UpdateTemporaryFixVisual(Transform target, float progress, float required)
    {
        if (target == null) return;
        float normalized = required > 0f ? Mathf.Clamp01(progress / required) : 0f;
        target.localScale = new Vector3(target.localScale.x, Mathf.Lerp(0.5f, 1f, normalized), target.localScale.z);
    }

    private static bool HasAuthority()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
    }
}
