using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BridgeAbutmentConstructionSite : BridgeConstructionSite, ILevelingMeasurementTarget
{
    private const int AnchorCount = 4;

    [Header("Dependency")]
    [SerializeField] private BridgeComponent prerequisiteFoundation;

    [Header("Ramp visuals")]
    [SerializeField] private Transform rampVisualRoot;
    [SerializeField, HideInInspector] private Renderer levelIndicatorRenderer;

    private BridgeAbutmentConstructionWorkflowSO abutmentWorkflow;
    private BridgeAbutmentWorkPoint[] workPoints;
    private int lengthTilt;
    private int widthTilt;
    private readonly float[] anchorProgress = new float[AnchorCount];
    private Quaternion rampBaseLocalRotation;

    public int LengthTilt => lengthTilt;
    public int WidthTilt => widthTilt;
    public bool IsLevelingCorrect => IsAxisCorrect(lengthTilt) && IsAxisCorrect(widthTilt);
    public bool IsLevelingActive => currentStage == BridgeConstructionStage.Leveling;
    public int MaximumLogicalTilt => GetWorkflow() != null ? GetWorkflow().MaximumLogicalTilt : 8;
    public IReadOnlyList<float> AnchorProgress => anchorProgress;
    public float BackfillProgress => currentWorkProgress;
    public override bool CanAcceptMountedComponent => currentStage == BridgeConstructionStage.ReadyForMount;
    public override float RequiredWorkProgress => currentStage == BridgeConstructionStage.Backfilling && abutmentWorkflow != null
        ? abutmentWorkflow.BackfillProgressNeeded
        : 0f;

    protected override void Awake()
    {
        workPoints = GetComponentsInChildren<BridgeAbutmentWorkPoint>(true);
        rampBaseLocalRotation = rampVisualRoot != null ? rampVisualRoot.localRotation : Quaternion.identity;
        base.Awake();
        abutmentWorkflow = bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null
            ? bridgeComponent.GetBridgeComponentSO().abutmentConstructionWorkflow
            : null;
        if (levelIndicatorRenderer != null) levelIndicatorRenderer.gameObject.SetActive(false);
        ApplyVisualState();
    }

    private void Update()
    {
        if (currentStage != BridgeConstructionStage.WaitingForFoundation || !HasAuthority())
        {
            return;
        }

        if (prerequisiteFoundation == null || prerequisiteFoundation.IsAssembled)
        {
            currentStage = BridgeConstructionStage.ReadyForMount;
            ApplyVisualState();
            GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        }
    }

    protected override BridgeConstructionStage GetInitialStage()
    {
        return prerequisiteFoundation != null && !prerequisiteFoundation.IsAssembled
            ? BridgeConstructionStage.WaitingForFoundation
            : BridgeConstructionStage.ReadyForMount;
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
        BridgeAbutmentConstructionWorkflowSO settings = GetWorkflow();
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

        if (currentStage == BridgeConstructionStage.Anchoring && toolType == settings.AnchoringTool)
        {
            int anchorIndex = workPointId - (int)BridgeAbutmentWorkPointId.Anchor0;
            if (anchorIndex < 0 || anchorIndex >= AnchorCount || anchorProgress[anchorIndex] >= settings.AnchorProgressNeeded)
            {
                return false;
            }

            anchorProgress[anchorIndex] = Mathf.Min(settings.AnchorProgressNeeded, anchorProgress[anchorIndex] + workPower);
            if (AreAllAnchorsComplete(settings.AnchorProgressNeeded))
            {
                currentStage = BridgeConstructionStage.Backfilling;
                currentWorkProgress = 0f;
            }

            ApplyVisualState();
            return true;
        }

        if (currentStage == BridgeConstructionStage.Backfilling &&
            toolType == settings.BackfillingTool &&
            workPointId == (int)BridgeAbutmentWorkPointId.Backfill)
        {
            currentWorkProgress = Mathf.Min(settings.BackfillProgressNeeded, currentWorkProgress + workPower);
            if (currentWorkProgress >= settings.BackfillProgressNeeded)
            {
                currentStage = BridgeConstructionStage.Complete;
                bridgeComponent.CompleteConstructionFromSite();
            }

            ApplyVisualState();
            bridgeComponent.RefreshVisualAndColliderState();
            return true;
        }

        return false;
    }

    public override bool CanApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        BridgeAbutmentConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null || workPower <= 0f) return false;

        if (currentStage == BridgeConstructionStage.Leveling && toolType == settings.LevelingTool)
        {
            return TryGetLevelingAdjustment(workPointId, out SpiritLevelMeasurementAxis axis, out int delta) &&
                   (axis == SpiritLevelMeasurementAxis.Length
                       ? lengthTilt + delta >= -settings.MaximumLogicalTilt && lengthTilt + delta <= settings.MaximumLogicalTilt
                       : widthTilt + delta >= -settings.MaximumLogicalTilt && widthTilt + delta <= settings.MaximumLogicalTilt);
        }

        if (currentStage == BridgeConstructionStage.Anchoring && toolType == settings.AnchoringTool)
        {
            int index = workPointId - (int)BridgeAbutmentWorkPointId.Anchor0;
            return index >= 0 && index < AnchorCount && anchorProgress[index] < settings.AnchorProgressNeeded;
        }

        return currentStage == BridgeConstructionStage.Backfilling &&
               toolType == settings.BackfillingTool &&
               workPointId == (int)BridgeAbutmentWorkPointId.Backfill &&
               currentWorkProgress < settings.BackfillProgressNeeded;
    }

    public void GetWorkPointPrompts(int requestId, List<InteractionPrompt> prompts)
    {
        BridgeAbutmentConstructionWorkflowSO settings = GetWorkflow();
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
        }
        else if (currentStage == BridgeConstructionStage.Anchoring)
        {
            int anchorIndex = requestId - (int)BridgeAbutmentWorkPointId.Anchor0;
            if (anchorIndex >= 0 && anchorIndex < AnchorCount)
            {
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                    $"Secure anchor {anchorIndex + 1} - {Mathf.CeilToInt(anchorProgress[anchorIndex])} / {Mathf.CeilToInt(settings.AnchorProgressNeeded)}"));
            }
        }
        else if (currentStage == BridgeConstructionStage.Backfilling && requestId == (int)BridgeAbutmentWorkPointId.Backfill)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"Backfill abutment - {Mathf.CeilToInt(currentWorkProgress)} / {Mathf.CeilToInt(settings.BackfillProgressNeeded)}"));
        }
    }

    public override void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (currentStage == BridgeConstructionStage.WaitingForFoundation)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Complete foundation first"));
        }
        else if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            bridgeComponent.AddReadyForMountPrompt(prompts, "Deliver Wooden Abutment");
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
            currentStage = BridgeConstructionStage.Anchoring;
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
        state.constructionAnchor0 = anchorProgress[0];
        state.constructionAnchor1 = anchorProgress[1];
        state.constructionAnchor2 = anchorProgress[2];
        state.constructionAnchor3 = anchorProgress[3];
    }

    public override void ApplyNetworkState(BridgeComponentNetworkState state)
    {
        currentStage = (BridgeConstructionStage)state.constructionStage;
        currentWorkProgress = Mathf.Max(0f, state.constructionProgress);
        lengthTilt = state.constructionValueA;
        widthTilt = state.constructionValueB;
        anchorProgress[0] = state.constructionAnchor0;
        anchorProgress[1] = state.constructionAnchor1;
        anchorProgress[2] = state.constructionAnchor2;
        anchorProgress[3] = state.constructionAnchor3;
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
            foreach (BridgeAbutmentWorkPoint point in workPoints)
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
            foreach (BridgeAbutmentWorkPoint point in workPoints)
            {
                if (point == null) continue;
                int id = (int)point.WorkPointId;
                bool active = (currentStage == BridgeConstructionStage.Leveling && point.IsLevelingPoint) ||
                              (currentStage == BridgeConstructionStage.Anchoring && id >= 10 && id <= 13) ||
                              (currentStage == BridgeConstructionStage.Backfilling && id == 20);
                point.gameObject.SetActive(active);
            }
        }

        BridgeAbutmentConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null)
        {
            return;
        }

        if (rampVisualRoot != null)
        {
            float pitch = BridgeLevelingUtility.GetVisualAngle(lengthTilt, settings.MaximumLogicalTilt,
                settings.VisuallyStraightTiltRange, settings.MaximumVisualTiltDegrees);
            float roll = BridgeLevelingUtility.GetVisualAngle(widthTilt, settings.MaximumLogicalTilt,
                settings.VisuallyStraightTiltRange, settings.MaximumVisualTiltDegrees);
            rampVisualRoot.localRotation = rampBaseLocalRotation * Quaternion.Euler(pitch, 0f, roll);
        }

    }

    private BridgeAbutmentConstructionWorkflowSO GetWorkflow()
    {
        if (abutmentWorkflow == null && bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null)
        {
            abutmentWorkflow = bridgeComponent.GetBridgeComponentSO().abutmentConstructionWorkflow;
        }
        return abutmentWorkflow;
    }

    private bool AreAllAnchorsComplete(float required)
    {
        for (int i = 0; i < AnchorCount; i++)
        {
            if (anchorProgress[i] < required) return false;
        }
        return true;
    }

    private bool IsAxisCorrect(int value)
    {
        BridgeAbutmentConstructionWorkflowSO settings = GetWorkflow();
        return Mathf.Abs(value) <= (settings != null ? settings.LevelingSuccessTolerance : 0);
    }

    private void RandomizeAxis(ref int axis)
    {
        BridgeAbutmentConstructionWorkflowSO settings = GetWorkflow();
        axis = BridgeLevelingUtility.RandomNonZeroTilt(
            settings != null ? settings.MinimumInitialAbsoluteTilt : 1,
            settings != null ? settings.MaximumLogicalTilt : 8);
    }

    private bool TryGetLevelingAdjustment(int workPointId, out SpiritLevelMeasurementAxis axis, out int delta)
    {
        if (workPoints != null)
        {
            foreach (BridgeAbutmentWorkPoint point in workPoints)
            {
                if (point != null && point.IsLevelingPoint && point.RequestId == workPointId)
                {
                    BridgeLevelingAdjustmentRoleUtility.Resolve(point.LevelingRole, out axis, out delta);
                    return true;
                }
            }
        }

        switch ((BridgeAbutmentWorkPointId)workPointId)
        {
            case BridgeAbutmentWorkPointId.StartWedge: axis = SpiritLevelMeasurementAxis.Length; delta = 1; return true;
            case BridgeAbutmentWorkPointId.EndWedge: axis = SpiritLevelMeasurementAxis.Length; delta = -1; return true;
            case BridgeAbutmentWorkPointId.LeftWedge: axis = SpiritLevelMeasurementAxis.Width; delta = 1; return true;
            case BridgeAbutmentWorkPointId.RightWedge: axis = SpiritLevelMeasurementAxis.Width; delta = -1; return true;
            default: axis = default; delta = 0; return false;
        }
    }

    private static bool HasAuthority()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
    }
}
