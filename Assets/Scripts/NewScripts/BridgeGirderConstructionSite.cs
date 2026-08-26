using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BridgeGirderConstructionSite : BridgeConstructionSite, ILevelingMeasurementTarget
{
    private const int FastenerCount = 4;
    private const int FastenerPairCount = 2;
    private const int NoActiveFastener = -1;

    [Header("Dependencies")]
    [SerializeField] private BridgeComponent[] prerequisiteSupports;

    [Header("Girder visuals")]
    [SerializeField] private Transform girderVisualRoot;
    [SerializeField, HideInInspector] private Renderer levelIndicatorRenderer;

    private BridgeGirderConstructionWorkflowSO girderWorkflow;
    private BridgeGirderWorkPoint[] workPoints;
    private int lengthTilt;
    private int widthTilt;
    private readonly float[] fastenerProgress = new float[FastenerCount];
    private int activeFirstFastenerIndex = NoActiveFastener;
    private double fastenerPairDeadline = -1d;
    private Quaternion girderBaseLocalRotation;

    public int LengthTilt => lengthTilt;
    public int WidthTilt => widthTilt;
    public bool IsLevelingCorrect => IsAxisCorrect(lengthTilt) && IsAxisCorrect(widthTilt);
    public bool IsLevelingActive => currentStage == BridgeConstructionStage.Leveling;
    public int MaximumLogicalTilt => GetWorkflow() != null ? GetWorkflow().MaximumLogicalTilt : 8;
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
        girderBaseLocalRotation = girderVisualRoot != null ? girderVisualRoot.localRotation : Quaternion.identity;
        base.Awake();
        girderWorkflow = bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null
            ? bridgeComponent.GetBridgeComponentSO().girderConstructionWorkflow
            : null;
        if (levelIndicatorRenderer != null) levelIndicatorRenderer.gameObject.SetActive(false);
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

        RandomizeAxis(ref lengthTilt);
        RandomizeAxis(ref widthTilt);

        currentStage = BridgeConstructionStage.Leveling;
        currentWorkProgress = 0f;
        ApplyVisualState();
    }

    public int GetLogicalTilt(SpiritLevelMeasurementAxis axis)
    {
        return axis == SpiritLevelMeasurementAxis.Length ? lengthTilt : widthTilt;
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
            if (!TryGetLevelingAdjustment(workPointId, out SpiritLevelMeasurementAxis axis, out int delta))
            {
                return false;
            }

            if (axis == SpiritLevelMeasurementAxis.Length)
            {
                lengthTilt = Mathf.Clamp(lengthTilt + delta, -settings.MaximumLogicalTilt, settings.MaximumLogicalTilt);
            }
            else
            {
                widthTilt = Mathf.Clamp(widthTilt + delta, -settings.MaximumLogicalTilt, settings.MaximumLogicalTilt);
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
            return TryGetLevelingAdjustment(workPointId, out SpiritLevelMeasurementAxis axis, out int delta) &&
                   (axis == SpiritLevelMeasurementAxis.Length
                       ? lengthTilt + delta >= -settings.MaximumLogicalTilt && lengthTilt + delta <= settings.MaximumLogicalTilt
                       : widthTilt + delta >= -settings.MaximumLogicalTilt && widthTilt + delta <= settings.MaximumLogicalTilt);
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

    public void GetWorkPointPrompts(int requestId, List<InteractionPrompt> prompts)
    {
        BridgeGirderConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null)
        {
            return;
        }

        if (currentStage == BridgeConstructionStage.Leveling)
        {
            if (TryGetLevelingAdjustment(requestId, out SpiritLevelMeasurementAxis axis, out _))
            {
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                    $"Adjust {axis.ToString().ToLowerInvariant()} support"));
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Confirm leveling"));
            }
            return;
        }

        if (currentStage == BridgeConstructionStage.Fastening)
        {
            int index = requestId - (int)BridgeGirderWorkPointId.Fastener0;
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
        else if (currentStage == BridgeConstructionStage.Leveling)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Confirm leveling"));
        }
    }

    public override bool TryConfirmLeveling()
    {
        if (currentStage != BridgeConstructionStage.Leveling || !HasAuthority())
        {
            return false;
        }

        if (IsLevelingCorrect)
        {
            currentStage = BridgeConstructionStage.Fastening;
            ClearFastenerPairWindow();
        }
        else
        {
            if (!IsAxisCorrect(lengthTilt)) RandomizeAxis(ref lengthTilt);
            if (!IsAxisCorrect(widthTilt)) RandomizeAxis(ref widthTilt);
        }

        ApplyVisualState();
        return true;
    }

    public override void PopulateNetworkState(ref BridgeComponentNetworkState state)
    {
        base.PopulateNetworkState(ref state);
        state.constructionValueA = lengthTilt;
        state.constructionValueB = widthTilt;
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
        lengthTilt = state.constructionValueA;
        widthTilt = state.constructionValueB;
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
                if (point != null && point.RequestId == workPointId)
                {
                    return point;
                }
            }
        }

        return this;
    }

    protected override void ApplyVisualState()
    {
        if (levelIndicatorRenderer != null)
        {
            levelIndicatorRenderer.gameObject.SetActive(false);
        }

        if (workPoints != null)
        {
            foreach (BridgeGirderWorkPoint point in workPoints)
            {
                if (point == null) continue;
                int id = (int)point.WorkPointId;
                bool active = (currentStage == BridgeConstructionStage.Leveling && point.IsLevelingPoint) ||
                              (currentStage == BridgeConstructionStage.Fastening && id >= 10 && id <= 13);
                point.gameObject.SetActive(active);
            }
        }

        BridgeGirderConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null)
        {
            return;
        }

        if (girderVisualRoot != null)
        {
            float pitch = BridgeLevelingUtility.GetVisualAngle(lengthTilt, settings.MaximumLogicalTilt,
                settings.VisuallyStraightTiltRange, settings.MaximumVisualTiltDegrees);
            float roll = BridgeLevelingUtility.GetVisualAngle(widthTilt, settings.MaximumLogicalTilt,
                settings.VisuallyStraightTiltRange, settings.MaximumVisualTiltDegrees);
            girderVisualRoot.localRotation = girderBaseLocalRotation * Quaternion.Euler(pitch, 0f, roll);
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

    private bool IsAxisCorrect(int value)
    {
        BridgeGirderConstructionWorkflowSO settings = GetWorkflow();
        return Mathf.Abs(value) <= (settings != null ? settings.LevelingSuccessTolerance : 0);
    }

    private void RandomizeAxis(ref int axis)
    {
        BridgeGirderConstructionWorkflowSO settings = GetWorkflow();
        axis = BridgeLevelingUtility.RandomNonZeroTilt(
            settings != null ? settings.MinimumInitialAbsoluteTilt : 1,
            settings != null ? settings.MaximumLogicalTilt : 8);
    }

    private bool TryGetLevelingAdjustment(int workPointId, out SpiritLevelMeasurementAxis axis, out int delta)
    {
        if (workPoints != null)
        {
            foreach (BridgeGirderWorkPoint point in workPoints)
            {
                if (point != null && point.IsLevelingPoint && point.RequestId == workPointId)
                {
                    BridgeLevelingAdjustmentRoleUtility.Resolve(point.LevelingRole, out axis, out delta);
                    return true;
                }
            }
        }

        switch ((BridgeGirderWorkPointId)workPointId)
        {
            case BridgeGirderWorkPointId.StartWedge: axis = SpiritLevelMeasurementAxis.Length; delta = 1; return true;
            case BridgeGirderWorkPointId.EndWedge: axis = SpiritLevelMeasurementAxis.Length; delta = -1; return true;
            case BridgeGirderWorkPointId.LeftWedge: axis = SpiritLevelMeasurementAxis.Width; delta = 1; return true;
            case BridgeGirderWorkPointId.RightWedge: axis = SpiritLevelMeasurementAxis.Width; delta = -1; return true;
            default: axis = default; delta = 0; return false;
        }
    }

    private static bool HasAuthority()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
    }
}
