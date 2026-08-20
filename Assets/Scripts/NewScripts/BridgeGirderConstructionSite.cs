using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BridgeGirderConstructionSite : BridgeConstructionSite
{
    private const int FastenerCount = 4;
    private const int FastenerPairCount = 2;
    private const int NoActiveFastener = -1;

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
    private int activeFirstFastenerIndex = NoActiveFastener;
    private double fastenerPairDeadline = -1d;
    private MaterialPropertyBlock levelIndicatorPropertyBlock;

    public int StartLevelStep => startLevelStep;
    public int EndLevelStep => endLevelStep;
    public IReadOnlyList<float> FastenerProgress => fastenerProgress;
    public float FirstPairProgress => fastenerProgress[0];
    public float SecondPairProgress => fastenerProgress[1];
    public int ActiveFastenerPairIndex => GetPairIndex(activeFirstFastenerIndex);
    public BridgeGirderWorkPointId ActiveFirstFastener => activeFirstFastenerIndex >= 0
        ? (BridgeGirderWorkPointId)((int)BridgeGirderWorkPointId.Fastener0 + activeFirstFastenerIndex)
        : (BridgeGirderWorkPointId)(-1);
    public BridgeGirderWorkPointId RequiredPairedFastener => activeFirstFastenerIndex >= 0
        ? (BridgeGirderWorkPointId)((int)BridgeGirderWorkPointId.Fastener0 + GetPairedFastenerIndex(activeFirstFastenerIndex))
        : (BridgeGirderWorkPointId)(-1);
    public bool IsFastenerPairWindowActive => currentStage == BridgeConstructionStage.Fastening &&
                                               activeFirstFastenerIndex >= 0 &&
                                               RemainingFastenerPairTime > 0f;
    public float RemainingFastenerPairTime => activeFirstFastenerIndex >= 0 && fastenerPairDeadline > 0d
        ? Mathf.Max(0f, (float)(fastenerPairDeadline - GetSynchronizedTime()))
        : 0f;
    public double FastenerPairDeadline => fastenerPairDeadline;
    public float FastenerPairWindowDuration => GetWorkflow() != null ? GetWorkflow().FastenerPairWindowDuration : 15f;
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
        if (currentStage == BridgeConstructionStage.Fastening && HasAuthority() &&
            activeFirstFastenerIndex >= 0 && RemainingFastenerPairTime <= 0f)
        {
            ClearFastenerPairWindow();
            ApplyVisualState();
            GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        }

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
                ClearFastenerPairWindow();
            }

            ApplyVisualState();
            return true;
        }

        return TryApplyFastenerPairWork(toolType, workPower, workPointId, ulong.MaxValue);
    }

    public override bool TryApplyToolWork(
        EquippableItemType toolType,
        float workPower,
        int workPointId,
        ulong actorClientId)
    {
        if (currentStage != BridgeConstructionStage.Fastening)
        {
            return TryApplyToolWork(toolType, workPower, workPointId);
        }

        return TryApplyFastenerPairWork(toolType, workPower, workPointId, actorClientId);
    }

    private bool TryApplyFastenerPairWork(
        EquippableItemType toolType,
        float workPower,
        int workPointId,
        ulong actorClientId)
    {
        BridgeGirderConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null || currentStage != BridgeConstructionStage.Fastening ||
            toolType != settings.FasteningTool || workPower <= 0f)
        {
            return false;
        }

        int fastenerIndex = workPointId - (int)BridgeGirderWorkPointId.Fastener0;
        int pairIndex = GetPairIndex(fastenerIndex);
        if (pairIndex < 0 || IsPairComplete(pairIndex, settings.FastenerProgressNeeded))
        {
            return false;
        }

        if (activeFirstFastenerIndex >= 0 && RemainingFastenerPairTime <= 0f)
        {
            ClearFastenerPairWindow();
        }

        if (activeFirstFastenerIndex < 0)
        {
            activeFirstFastenerIndex = fastenerIndex;
            fastenerPairDeadline = GetSynchronizedTime() + settings.FastenerPairWindowDuration;
            GameplayManager.Instance?.NotifyGirderFasteningWindowStarted(
                actorClientId,
                bridgeComponent.ComponentID,
                (int)RequiredPairedFastener,
                fastenerPairDeadline);
            ApplyVisualState();
            return true;
        }

        if (fastenerIndex == activeFirstFastenerIndex || pairIndex != ActiveFastenerPairIndex)
        {
            return false;
        }

        if (fastenerIndex != GetPairedFastenerIndex(activeFirstFastenerIndex))
        {
            return false;
        }

        float pairProgress = Mathf.Min(settings.FastenerProgressNeeded, GetPairProgress(pairIndex) + workPower);
        SetPairProgress(pairIndex, pairProgress);
        ClearFastenerPairWindow();
        if (AreAllFastenerPairsComplete(settings.FastenerProgressNeeded))
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
        BridgeGirderConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null || workPower <= 0f) return false;

        if (currentStage == BridgeConstructionStage.Leveling && toolType == settings.LevelingTool)
        {
            return workPointId == (int)BridgeGirderWorkPointId.StartWedge
                ? startLevelStep > 0
                : workPointId == (int)BridgeGirderWorkPointId.EndWedge && endLevelStep > 0;
        }

        int index = workPointId - (int)BridgeGirderWorkPointId.Fastener0;
        int pairIndex = GetPairIndex(index);
        bool fastenerAvailable = pairIndex >= 0 && !IsPairComplete(pairIndex, settings.FastenerProgressNeeded);
        if (IsFastenerPairWindowActive)
        {
            fastenerAvailable &= index == GetPairedFastenerIndex(activeFirstFastenerIndex);
        }
        return currentStage == BridgeConstructionStage.Fastening &&
               toolType == settings.FasteningTool &&
               fastenerAvailable;
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
                int pairIndex = GetPairIndex(index);
                string pairName = GetPairDisplayName(pairIndex);
                if (IsFastenerPairWindowActive)
                {
                    if (index == GetPairedFastenerIndex(activeFirstFastenerIndex))
                    {
                        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                            $"Strike paired fastener - {RemainingFastenerPairTime:F1} s"));
                    }
                    else if (index == activeFirstFastenerIndex)
                    {
                        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                            "Strike the diagonal fastener"));
                    }
                    else
                    {
                        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                            "Finish the active diagonal pair first"));
                    }
                    return;
                }

                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                    $"Secure diagonal pair {pairName} - {Mathf.CeilToInt(GetPairProgress(pairIndex))} / {Mathf.CeilToInt(settings.FastenerProgressNeeded)}"));
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
            bridgeComponent.AddReadyForMountPrompt(prompts, "Deliver Wooden Main Girder");
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
        state.constructionAux0 = activeFirstFastenerIndex + 1;
        state.constructionAux1 = activeFirstFastenerIndex >= 0 ? (float)fastenerPairDeadline : 0f;
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
        NormalizePairProgress();
        activeFirstFastenerIndex = Mathf.Clamp(Mathf.RoundToInt(state.constructionAux0) - 1, -1, FastenerCount - 1);
        fastenerPairDeadline = activeFirstFastenerIndex >= 0 ? state.constructionAux1 : -1d;
        if (currentStage != BridgeConstructionStage.Fastening)
        {
            ClearFastenerPairWindow();
        }
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

    private bool AreAllFastenerPairsComplete(float required)
    {
        for (int i = 0; i < FastenerPairCount; i++)
        {
            if (!IsPairComplete(i, required)) return false;
        }
        return true;
    }

    private float GetPairProgress(int pairIndex)
    {
        return pairIndex == 0 ? fastenerProgress[0] : pairIndex == 1 ? fastenerProgress[1] : 0f;
    }

    private void SetPairProgress(int pairIndex, float progress)
    {
        if (pairIndex == 0)
        {
            fastenerProgress[0] = progress;
            fastenerProgress[3] = progress;
        }
        else if (pairIndex == 1)
        {
            fastenerProgress[1] = progress;
            fastenerProgress[2] = progress;
        }
    }

    private void NormalizePairProgress()
    {
        SetPairProgress(0, Mathf.Max(fastenerProgress[0], fastenerProgress[3]));
        SetPairProgress(1, Mathf.Max(fastenerProgress[1], fastenerProgress[2]));
    }

    private bool IsPairComplete(int pairIndex, float required)
    {
        return pairIndex >= 0 && GetPairProgress(pairIndex) >= required;
    }

    private void ClearFastenerPairWindow()
    {
        activeFirstFastenerIndex = NoActiveFastener;
        fastenerPairDeadline = -1d;
    }

    private static int GetPairIndex(int fastenerIndex)
    {
        if (fastenerIndex == 0 || fastenerIndex == 3) return 0;
        if (fastenerIndex == 1 || fastenerIndex == 2) return 1;
        return -1;
    }

    private static int GetPairedFastenerIndex(int fastenerIndex)
    {
        switch (fastenerIndex)
        {
            case 0: return 3;
            case 1: return 2;
            case 2: return 1;
            case 3: return 0;
            default: return -1;
        }
    }

    private static string GetPairDisplayName(int pairIndex)
    {
        return pairIndex == 0 ? "A" : pairIndex == 1 ? "B" : "?";
    }

    private static double GetSynchronizedTime()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening
            ? networkManager.ServerTime.Time
            : Time.timeAsDouble;
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
