using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(BridgeComponent))]
public class BridgeConstructionSite : MonoBehaviour, IDamageable, IInteractionPromptProvider
{
    [Header("Clearing")]
    [SerializeField] private bool requiresSiteClearing = true;
    [SerializeField] private Vector2 clearingAreaSize = new Vector2(2.4f, 2.4f);
    [SerializeField] private BaseResourceNew[] clearingObstacles;

    [Header("Stage visuals")]
    [SerializeField] private Collider constructionInteractionCollider;
    [SerializeField] private GameObject markedGroundVisual;
    [SerializeField] private GameObject diggingVisual;
    [SerializeField] private GameObject completedDigVisual;
    [SerializeField] private bool hideMountedVisualsWhenComplete;
    [SerializeField] private bool disablePhysicalCollidersWhenComplete;

    protected BridgeComponent bridgeComponent;
    protected BridgeConstructionWorkflowSO workflow;
    protected BridgeConstructionStage currentStage;
    protected float currentWorkProgress;
    private bool initialized;

    public virtual BridgeConstructionStage CurrentStage => currentStage;
    public bool RequiresSiteClearing => requiresSiteClearing;
    public Vector2 ClearingAreaSize => clearingAreaSize;
    public int RemainingObstacleCount => CountRemainingObstacles();
    public virtual float CurrentWorkProgress => currentWorkProgress;
    public virtual float RequiredWorkProgress => currentStage == BridgeConstructionStage.Digging && workflow != null
        ? workflow.DiggingProgressNeeded
        : bridgeComponent != null ? bridgeComponent.GetAssemblingProgressNeeded() : 0f;
    public virtual bool CanAcceptMountedComponent => currentStage == BridgeConstructionStage.ReadyForMount;
    public BridgeComponent BridgeComponent => bridgeComponent;

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
        if (currentStage != BridgeConstructionStage.Clearing || !HasAuthority())
        {
            return;
        }

        if (RefreshClearingState())
        {
            GameplayManager.Instance?.NotifyConstructionSiteStateChanged(this);
        }
    }

    public virtual bool TryApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        if (workPower <= 0f)
        {
            return false;
        }

        if (currentStage == BridgeConstructionStage.Digging)
        {
            if (workflow == null || toolType != workflow.DiggingTool)
            {
                return false;
            }

            currentWorkProgress = Mathf.Clamp(currentWorkProgress + workPower, 0f, workflow.DiggingProgressNeeded);
            if (currentWorkProgress >= workflow.DiggingProgressNeeded)
            {
                currentStage = BridgeConstructionStage.ReadyForMount;
                currentWorkProgress = 0f;
            }

            ApplyVisualState();
            bridgeComponent.RefreshVisualAndColliderState();
            return true;
        }

        return false;
    }

    public virtual bool CanApplyToolWork(EquippableItemType toolType, float workPower, int workPointId = -1)
    {
        return workPower > 0f &&
               currentStage == BridgeConstructionStage.Digging &&
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
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                    $"Dig foundation - {Mathf.CeilToInt(currentWorkProgress)} / {Mathf.CeilToInt(RequiredWorkProgress)}"));
                break;
            case BridgeConstructionStage.ReadyForMount:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Deliver Wooden Foundation"));
                break;
            case BridgeConstructionStage.Hammering:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                    $"Secure foundation - {Mathf.CeilToInt(currentWorkProgress)} / {Mathf.CeilToInt(RequiredWorkProgress)}"));
                break;
        }
    }

    public virtual void ApplyNetworkState(BridgeComponentNetworkState state)
    {
        currentStage = (BridgeConstructionStage)state.constructionStage;
        currentWorkProgress = Mathf.Max(0f, state.constructionProgress);
        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
    }

    public virtual void PopulateNetworkState(ref BridgeComponentNetworkState state)
    {
        state.constructionStage = (int)currentStage;
        state.constructionProgress = currentWorkProgress;
    }

    public virtual void NotifyMounted()
    {
        if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            currentStage = BridgeConstructionStage.Hammering;
            currentWorkProgress = 0f;
            ApplyVisualState();
        }
    }

    public virtual void NotifyAssembled()
    {
        currentStage = BridgeConstructionStage.Complete;
        currentWorkProgress = RequiredWorkProgress;
        ApplyVisualState();
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
        if (clearingObstacles == null)
        {
            return 0;
        }

        int count = 0;
        foreach (BaseResourceNew obstacle in clearingObstacles)
        {
            if (obstacle != null &&
                obstacle.gameObject.activeInHierarchy &&
                obstacle.CanBeDestroyedWith(EquippableItemType.Axe))
            {
                count++;
            }
        }

        return count;
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
                                                     currentStage == BridgeConstructionStage.Digging;
        }
        if (markedGroundVisual != null)
        {
            markedGroundVisual.SetActive(currentStage == BridgeConstructionStage.Clearing);
        }

        if (diggingVisual != null)
        {
            diggingVisual.SetActive(currentStage == BridgeConstructionStage.Digging);
        }

        if (completedDigVisual != null)
        {
            completedDigVisual.SetActive(currentStage == BridgeConstructionStage.ReadyForMount);
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

    private void OnValidate()
    {
        clearingAreaSize.x = Mathf.Max(0.1f, clearingAreaSize.x);
        clearingAreaSize.y = Mathf.Max(0.1f, clearingAreaSize.y);
        ApplyClearingAreaSize();
    }

    public bool IsConstructionInteractionCollider(Collider candidate)
    {
        return candidate != null && candidate == constructionInteractionCollider;
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
        return this;
    }
}
