using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BridgeGirderConstructionSite : BridgeConstructionSite
{
    private const int FastenerCount = 4;

    [Header("Dependencies")]
    [SerializeField] private BridgeComponent[] prerequisiteSupports;

    [Header("Girder visuals")]
    [SerializeField] private Transform girderVisualRoot;
    [SerializeField] private Transform startWedgeVisual;
    [SerializeField] private Transform endWedgeVisual;
    [SerializeField] private Renderer levelIndicatorRenderer;
    [SerializeField] private Color unlevelColor = new Color(0.8f, 0.15f, 0.1f, 1f);
    [SerializeField] private Color levelColor = new Color(0.15f, 0.8f, 0.2f, 1f);

    private BridgeGirderConstructionWorkflowSO girderWorkflow;
    private BridgeGirderWorkPoint[] workPoints;
    private int startLevelStep;
    private int endLevelStep;
    private readonly float[] fastenerProgress = new float[FastenerCount];
    private MaterialPropertyBlock levelIndicatorPropertyBlock;

    public int StartLevelStep => startLevelStep;
    public int EndLevelStep => endLevelStep;
    public IReadOnlyList<float> FastenerProgress => fastenerProgress;
    public override bool CanAcceptMountedComponent => currentStage == BridgeConstructionStage.ReadyForMount;

    protected override void Awake()
    {
        workPoints = GetComponentsInChildren<BridgeGirderWorkPoint>(true);
        base.Awake();
        girderWorkflow = bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null
            ? bridgeComponent.GetBridgeComponentSO().girderConstructionWorkflow
            : null;
        ApplyVisualState();
    }

    private void Update()
    {
        if (currentStage != BridgeConstructionStage.WaitingForSupports || !HasAuthority() || !ArePrerequisitesComplete())
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
            : BridgeConstructionStage.WaitingForSupports;
    }

    public override void NotifyMounted()
    {
        if (currentStage != BridgeConstructionStage.ReadyForMount)
        {
            return;
        }

        BridgeGirderConstructionWorkflowSO settings = GetWorkflow();
        int difference = settings != null ? settings.InitialLevelDifference : 2;
        if (HasAuthority() && Random.Range(0, 2) == 0)
        {
            startLevelStep = difference;
            endLevelStep = 0;
        }
        else
        {
            startLevelStep = 0;
            endLevelStep = difference;
        }

        currentStage = BridgeConstructionStage.Leveling;
        currentWorkProgress = 0f;
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
        BridgeGirderConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null || workPower <= 0f)
        {
            return false;
        }

        if (currentStage == BridgeConstructionStage.Leveling && toolType == settings.LevelingTool)
        {
            if (workPointId == (int)BridgeGirderWorkPointId.StartWedge)
            {
                if (startLevelStep <= 0) return false;
                startLevelStep--;
            }
            else if (workPointId == (int)BridgeGirderWorkPointId.EndWedge)
            {
                if (endLevelStep <= 0) return false;
                endLevelStep--;
            }
            else
            {
                return false;
            }

            if (startLevelStep == endLevelStep)
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

        int fastenerIndex = workPointId - (int)BridgeGirderWorkPointId.Fastener0;
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

    public void GetWorkPointPrompts(BridgeGirderWorkPointId pointId, List<InteractionPrompt> prompts)
    {
        BridgeGirderConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null)
        {
            return;
        }

        if (currentStage == BridgeConstructionStage.Leveling)
        {
            bool isStart = pointId == BridgeGirderWorkPointId.StartWedge;
            int step = isStart ? startLevelStep : endLevelStep;
            string suffix = step <= 0 ? " - already at minimum" : string.Empty;
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"Lower {(isStart ? "start" : "end")} end - step {step} / {settings.MaximumLevelStep}{suffix}"));
            return;
        }

        if (currentStage == BridgeConstructionStage.Fastening)
        {
            int index = (int)pointId - (int)BridgeGirderWorkPointId.Fastener0;
            if (index >= 0 && index < FastenerCount)
            {
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                    $"Secure girder {index + 1} - {Mathf.CeilToInt(fastenerProgress[index])} / {Mathf.CeilToInt(settings.FastenerProgressNeeded)}"));
            }
        }
    }

    public override void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (currentStage == BridgeConstructionStage.WaitingForSupports)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Complete supporting abutments first"));
        }
        else if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Deliver Wooden Main Girder"));
        }
    }

    public override void PopulateNetworkState(ref BridgeComponentNetworkState state)
    {
        base.PopulateNetworkState(ref state);
        state.constructionValueA = startLevelStep;
        state.constructionValueB = endLevelStep;
        state.constructionAnchor0 = fastenerProgress[0];
        state.constructionAnchor1 = fastenerProgress[1];
        state.constructionAnchor2 = fastenerProgress[2];
        state.constructionAnchor3 = fastenerProgress[3];
    }

    public override void ApplyNetworkState(BridgeComponentNetworkState state)
    {
        currentStage = (BridgeConstructionStage)state.constructionStage;
        currentWorkProgress = Mathf.Max(0f, state.constructionProgress);
        startLevelStep = state.constructionValueA;
        endLevelStep = state.constructionValueB;
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
            foreach (BridgeGirderWorkPoint point in workPoints)
            {
                if (point != null && (int)point.WorkPointId == workPointId)
                {
                    return point;
                }
            }
        }

        return this;
    }

    protected override void ApplyVisualState()
    {
        bool showLevelIndicator = currentStage == BridgeConstructionStage.Leveling ||
                                  currentStage == BridgeConstructionStage.Fastening;
        if (levelIndicatorRenderer != null)
        {
            levelIndicatorRenderer.gameObject.SetActive(showLevelIndicator);
        }

        if (workPoints != null)
        {
            foreach (BridgeGirderWorkPoint point in workPoints)
            {
                if (point == null) continue;
                int id = (int)point.WorkPointId;
                bool active = (currentStage == BridgeConstructionStage.Leveling && id <= 1) ||
                              (currentStage == BridgeConstructionStage.Fastening && id >= 10 && id <= 13);
                point.gameObject.SetActive(active);
            }
        }

        BridgeGirderConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null)
        {
            return;
        }

        float startHeight = startLevelStep * settings.LevelStepHeight;
        float endHeight = endLevelStep * settings.LevelStepHeight;
        SetLocalHeight(startWedgeVisual, startHeight);
        SetLocalHeight(endWedgeVisual, endHeight);
        if (girderVisualRoot != null)
        {
            float pitch = Mathf.Atan2(endHeight - startHeight, 14f) * Mathf.Rad2Deg;
            girderVisualRoot.localRotation = Quaternion.Euler(0f, 0f, pitch);
        }

        bool isLevel = startLevelStep == endLevelStep;
        if (levelIndicatorRenderer != null)
        {
            levelIndicatorPropertyBlock ??= new MaterialPropertyBlock();
            levelIndicatorRenderer.GetPropertyBlock(levelIndicatorPropertyBlock);
            Color indicatorColor = isLevel ? levelColor : unlevelColor;
            levelIndicatorPropertyBlock.SetColor("_BaseColor", indicatorColor);
            levelIndicatorPropertyBlock.SetColor("_Color", indicatorColor);
            levelIndicatorRenderer.SetPropertyBlock(levelIndicatorPropertyBlock);
        }

    }

    private BridgeGirderConstructionWorkflowSO GetWorkflow()
    {
        if (girderWorkflow == null && bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null)
        {
            girderWorkflow = bridgeComponent.GetBridgeComponentSO().girderConstructionWorkflow;
        }
        return girderWorkflow;
    }

    private bool ArePrerequisitesComplete()
    {
        if (prerequisiteSupports == null || prerequisiteSupports.Length == 0)
        {
            return true;
        }

        foreach (BridgeComponent support in prerequisiteSupports)
        {
            if (support != null && !support.IsAssembled)
            {
                return false;
            }
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

    private static void SetLocalHeight(Transform target, float height)
    {
        if (target != null)
        {
            target.localPosition = new Vector3(target.localPosition.x, height, target.localPosition.z);
        }
    }

    private static bool HasAuthority()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
    }
}
