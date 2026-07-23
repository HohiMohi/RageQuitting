using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BridgeAbutmentConstructionSite : BridgeConstructionSite
{
    private const int AnchorCount = 4;

    [Header("Dependency")]
    [SerializeField] private BridgeComponent prerequisiteFoundation;

    [Header("Ramp visuals")]
    [SerializeField] private Transform rampVisualRoot;
    [SerializeField] private Transform leftWedgeVisual;
    [SerializeField] private Transform rightWedgeVisual;
    [SerializeField] private Renderer levelIndicatorRenderer;
    [SerializeField] private Color unlevelColor = new Color(0.8f, 0.15f, 0.1f, 1f);
    [SerializeField] private Color levelColor = new Color(0.15f, 0.8f, 0.2f, 1f);

    private BridgeAbutmentConstructionWorkflowSO abutmentWorkflow;
    private BridgeAbutmentWorkPoint[] workPoints;
    private int leftLevelStep;
    private int rightLevelStep;
    private readonly float[] anchorProgress = new float[AnchorCount];
    private MaterialPropertyBlock levelIndicatorPropertyBlock;

    public int LeftLevelStep => leftLevelStep;
    public int RightLevelStep => rightLevelStep;
    public IReadOnlyList<float> AnchorProgress => anchorProgress;
    public float BackfillProgress => currentWorkProgress;
    public override bool CanAcceptMountedComponent => currentStage == BridgeConstructionStage.ReadyForMount;
    public override float RequiredWorkProgress => currentStage == BridgeConstructionStage.Backfilling && abutmentWorkflow != null
        ? abutmentWorkflow.BackfillProgressNeeded
        : 0f;

    protected override void Awake()
    {
        workPoints = GetComponentsInChildren<BridgeAbutmentWorkPoint>(true);
        base.Awake();
        abutmentWorkflow = bridgeComponent != null && bridgeComponent.GetBridgeComponentSO() != null
            ? bridgeComponent.GetBridgeComponentSO().abutmentConstructionWorkflow
            : null;
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

        BridgeAbutmentConstructionWorkflowSO settings = GetWorkflow();
        int difference = settings != null ? settings.InitialLevelDifference : 2;
        if (HasAuthority() && Random.Range(0, 2) == 0)
        {
            leftLevelStep = difference;
            rightLevelStep = 0;
        }
        else
        {
            leftLevelStep = 0;
            rightLevelStep = difference;
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
        BridgeAbutmentConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null || workPower <= 0f)
        {
            return false;
        }

        if (currentStage == BridgeConstructionStage.Leveling && toolType == settings.LevelingTool)
        {
            if (workPointId == (int)BridgeAbutmentWorkPointId.LeftWedge)
            {
                if (leftLevelStep <= 0) return false;
                leftLevelStep--;
            }
            else if (workPointId == (int)BridgeAbutmentWorkPointId.RightWedge)
            {
                if (rightLevelStep <= 0) return false;
                rightLevelStep--;
            }
            else
            {
                return false;
            }

            if (leftLevelStep == rightLevelStep)
            {
                currentStage = BridgeConstructionStage.Anchoring;
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

    public void GetWorkPointPrompts(BridgeAbutmentWorkPointId pointId, List<InteractionPrompt> prompts)
    {
        BridgeAbutmentConstructionWorkflowSO settings = GetWorkflow();
        if (settings == null)
        {
            return;
        }

        if (currentStage == BridgeConstructionStage.Leveling)
        {
            string side = pointId == BridgeAbutmentWorkPointId.LeftWedge ? "left" : "right";
            int step = pointId == BridgeAbutmentWorkPointId.LeftWedge ? leftLevelStep : rightLevelStep;
            string suffix = step <= 0 ? " - already at minimum" : string.Empty;
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                $"Lower {side} side - step {step} / {settings.MaximumLevelStep}{suffix}"));
        }
        else if (currentStage == BridgeConstructionStage.Anchoring)
        {
            int anchorIndex = (int)pointId - (int)BridgeAbutmentWorkPointId.Anchor0;
            if (anchorIndex >= 0 && anchorIndex < AnchorCount)
            {
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                    $"Secure anchor {anchorIndex + 1} - {Mathf.CeilToInt(anchorProgress[anchorIndex])} / {Mathf.CeilToInt(settings.AnchorProgressNeeded)}"));
            }
        }
        else if (currentStage == BridgeConstructionStage.Backfilling && pointId == BridgeAbutmentWorkPointId.Backfill)
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
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Deliver Wooden Abutment"));
        }
    }

    public override void PopulateNetworkState(ref BridgeComponentNetworkState state)
    {
        base.PopulateNetworkState(ref state);
        state.constructionValueA = leftLevelStep;
        state.constructionValueB = rightLevelStep;
        state.constructionAnchor0 = anchorProgress[0];
        state.constructionAnchor1 = anchorProgress[1];
        state.constructionAnchor2 = anchorProgress[2];
        state.constructionAnchor3 = anchorProgress[3];
    }

    public override void ApplyNetworkState(BridgeComponentNetworkState state)
    {
        currentStage = (BridgeConstructionStage)state.constructionStage;
        currentWorkProgress = Mathf.Max(0f, state.constructionProgress);
        leftLevelStep = state.constructionValueA;
        rightLevelStep = state.constructionValueB;
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
                                  currentStage == BridgeConstructionStage.Anchoring;
        if (levelIndicatorRenderer != null)
        {
            levelIndicatorRenderer.gameObject.SetActive(showLevelIndicator);
        }

        if (workPoints != null)
        {
            foreach (BridgeAbutmentWorkPoint point in workPoints)
            {
                if (point == null) continue;
                int id = (int)point.WorkPointId;
                bool active = (currentStage == BridgeConstructionStage.Leveling && id <= 1) ||
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

        float leftHeight = leftLevelStep * settings.LevelStepHeight;
        float rightHeight = rightLevelStep * settings.LevelStepHeight;
        if (leftWedgeVisual != null) leftWedgeVisual.localPosition = new Vector3(leftWedgeVisual.localPosition.x, leftHeight, leftWedgeVisual.localPosition.z);
        if (rightWedgeVisual != null) rightWedgeVisual.localPosition = new Vector3(rightWedgeVisual.localPosition.x, rightHeight, rightWedgeVisual.localPosition.z);
        if (rampVisualRoot != null)
        {
            float roll = Mathf.Atan2(rightHeight - leftHeight, 2.4f) * Mathf.Rad2Deg;
            rampVisualRoot.localRotation = Quaternion.Euler(0f, 0f, roll);
        }

        bool isLevel = leftLevelStep == rightLevelStep;
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

    private static bool HasAuthority()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
    }
}
