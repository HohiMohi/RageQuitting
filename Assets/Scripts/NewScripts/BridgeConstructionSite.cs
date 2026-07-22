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

    private BridgeComponent bridgeComponent;
    private BridgeConstructionWorkflowSO workflow;
    private BridgeConstructionStage currentStage;
    private float currentWorkProgress;
    private bool initialized;

    public BridgeConstructionStage CurrentStage => currentStage;
    public bool RequiresSiteClearing => requiresSiteClearing;
    public Vector2 ClearingAreaSize => clearingAreaSize;
    public int RemainingObstacleCount => CountRemainingObstacles();
    public float CurrentWorkProgress => currentWorkProgress;
    public float RequiredWorkProgress => currentStage == BridgeConstructionStage.Digging && workflow != null
        ? workflow.DiggingProgressNeeded
        : bridgeComponent != null ? bridgeComponent.GetAssemblingProgressNeeded() : 0f;
    public bool CanAcceptMountedComponent => currentStage == BridgeConstructionStage.ReadyForMount;

    private void Awake()
    {
        Initialize();
    }

    public void Initialize()
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

    public bool TryApplyToolWork(EquippableItemType toolType, float workPower)
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

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
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

    public void ApplyNetworkState(BridgeConstructionStage stage, float progress)
    {
        currentStage = stage;
        currentWorkProgress = Mathf.Max(0f, progress);
        ApplyVisualState();
        bridgeComponent.RefreshVisualAndColliderState();
    }

    public void NotifyMounted()
    {
        if (currentStage == BridgeConstructionStage.ReadyForMount)
        {
            currentStage = BridgeConstructionStage.Hammering;
            currentWorkProgress = 0f;
            ApplyVisualState();
        }
    }

    public void NotifyAssembled()
    {
        currentStage = BridgeConstructionStage.Complete;
        currentWorkProgress = RequiredWorkProgress;
        ApplyVisualState();
    }

    public void ApplyAssemblyProgress(float progress)
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

    private BridgeConstructionStage GetInitialStage()
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

    private void ApplyVisualState()
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
            completedDigVisual.SetActive(currentStage >= BridgeConstructionStage.ReadyForMount && currentStage < BridgeConstructionStage.Complete);
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
}
