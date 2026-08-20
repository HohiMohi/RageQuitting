using System;
using System.Collections.Generic;
using UnityEngine;

public class BridgeComponent : MonoBehaviour, IInteractableNew, IDamageable
{
    [Header("Initial State")]
    [Tooltip("Start this scene instance fully mounted, assembled and construction-complete.")]
    [SerializeField] private bool startFullyCompleted;

    [Header("Configuration")]
    [SerializeField] private int componentID;
    [HideInInspector]
    [SerializeField] private bool isMounted;
    [HideInInspector]
    [SerializeField] private bool canBeMounted;
    [HideInInspector]
    [SerializeField] private bool isAssembled;
    [SerializeField] private BridgeComponentSO bridgeComponentSO;
    [SerializeField] private GameObject readyForMountingVisualsGameObject;
    [SerializeField] private GameObject mountedComponentVisualsGameObject;
    [SerializeField] private Material ghostMaterial;
    [Tooltip("Optional explicit list of colliders enabled after mounting. When empty, all colliders outside the ghost visuals are used.")]
    [SerializeField] private Collider[] mountedPhysicalColliders;
    private float assemblingProgressNeeded;
    private float currentAssemblingProgress;
    private bool needAssembling;
    private Collider[] readyForMountingInteractionColliders = Array.Empty<Collider>();
    private BridgeConstructionSite constructionSite;
    private BridgeMountSocket mountSocket;

    public EventHandler<ComponentMountedEventArgs> ComponentMounted;
    public EventHandler<ComponentAsembledEventArgs> ComponentAsembled;
    public EventHandler<BridgeComponentSOAssignedEventArgs> BridgeComponentSOAssigned;
    public EventHandler EquippedItemTypeNeeded;
    public class BridgeComponentSOAssignedEventArgs : EventArgs
    {
        public BridgeComponentSO bridgeComponentSO;
    }
    public class ComponentMountedEventArgs: EventArgs
    {
        public int componentID;
    }

    public class ComponentAsembledEventArgs: EventArgs
    {
        public int componentID;
    }

    public void Interact(Transform interactor)
    {
        if (mountSocket != null)
        {
            return;
        }

        if (canBeMounted && !isMounted)
        {
            if (interactor.TryGetComponent<PlayerInteractionNew>(out PlayerInteractionNew playerInteraction))
            {
                GameObject heldGo = playerInteraction.GetPickedUpGameObject();
                if (heldGo != null && heldGo.TryGetComponent<MountableBridgeComponent>(out MountableBridgeComponent heldComponent))
                {
                    if (heldComponent.GetMountableBridgeComponentSO().bridgeComponentSO == bridgeComponentSO)
                    {
                        GameplayManager.Instance.RequestMountBridgeComponent(this, heldComponent);
                        if (Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening)
                        {
                            playerInteraction.ForceReleasePickedUpObject(heldGo);
                        }
                    }
                    else
                    {
                        Debug.Log("Holding wrong bridge component type!");
                    }
                }
                else
                {
                    Debug.Log("Not holding any mountable bridge component!");
                }
            }
        }
    }

    public void HandleAssembling(EquippableItemSO equippableItemSO, float damage)
    {
        GameplayManager.Instance.RequestAssembleBridgeComponent(this, equippableItemSO, damage);
    }

    public void HandleAssemblingLocal(EquippableItemSO equippableItemSO, float damage)
    {

        if (equippableItemSO != null && bridgeComponentSO.supportedEquippableItemTypeList.Contains(equippableItemSO.itemType))
        {
            currentAssemblingProgress += damage;
            constructionSite?.ApplyAssemblyProgress(currentAssemblingProgress);
            if (currentAssemblingProgress >= assemblingProgressNeeded)
            {
                ComponentAsembled?.Invoke(this, new ComponentAsembledEventArgs { componentID = componentID });
                isAssembled = true;
                constructionSite?.NotifyAssembled();
            }
        }
        else
        {
            Debug.Log("You need supported EquippableItemType item to assemble this component");
            //Handle UI there
            EquippedItemTypeNeeded?.Invoke(this, EventArgs.Empty);
        }

    }

    private void Awake()
    {
        constructionSite = GetComponent<BridgeConstructionSite>();
        mountSocket = GetComponent<BridgeMountSocket>();
        InitializeDefinitionState();
        constructionSite?.Initialize();
        CacheBridgeComponentColliders();
        ConfigureReadyForMountingInteractionColliders();
        ResetRuntimeState();
        if (startFullyCompleted && HasInitialStateAuthority())
        {
            ApplyInitialCompletedState();
        }
        ApplyVisualAndColliderState();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        InitializeDefinitionState();
        currentAssemblingProgress = isAssembled ? assemblingProgressNeeded : 0f;

        if (bridgeComponentSO != null)
        {
            BridgeComponentSOAssigned?.Invoke(this, new BridgeComponentSOAssignedEventArgs
            {
                bridgeComponentSO = bridgeComponentSO
            });
        }

        // Apply translucent ghost material to the ready visuals if assigned
        if (readyForMountingVisualsGameObject != null && ghostMaterial != null)
        {
            foreach (Renderer r in readyForMountingVisualsGameObject.GetComponentsInChildren<Renderer>(true))
            {
                r.material = ghostMaterial;
            }
        }

        GameplayManager.Instance.BridgeComponentMountableStatusUpdate += GameplayManager_OnBridgeComponentMountableStatusUpdate;
        //BridgeBuildingManager.Instance.BridgeComponentMountableStatusUpdate += BridgeBuildingManager_OnBridgeComponentMountableStatusUpdate;
        //BridgeBuildingManager.Instance.BridgeComponentStored += BridgeBuildingManager_OnBridgeComponentStored;
    }

    private void BridgeBuildingManager_OnBridgeComponentStored(object sender, BridgeBuildingManager.BridgeComponentStoredEventArgs e)
    {
        if (e.componentID == componentID)
        {
            bridgeComponentSO = e.bridgeComponentSO;
            assemblingProgressNeeded = bridgeComponentSO.assemblingProgressNeeded;
            needAssembling = bridgeComponentSO.needAssembling;
            BridgeComponentSOAssigned?.Invoke(this, new BridgeComponentSOAssignedEventArgs
            {
                bridgeComponentSO = bridgeComponentSO
            });
        }
    }

    private void GameplayManager_OnBridgeComponentMountableStatusUpdate(object sender, GameplayManager.BridgeComponentMountableStatusUpdateEventArgs e)
    {
        Debug.Log("Received BridgeComponentMountableStatusUpdate event in BridgeComponent with componentID: " + componentID);
        if (e.componentID == componentID && !isMounted)
        {
            canBeMounted = e.canBeMounted;
            ApplyVisualAndColliderState();
        }
    }

    private void BridgeBuildingManager_OnBridgeComponentMountableStatusUpdate(object sender, BridgeBuildingManager.BridgeComponentMountableStatusUpdateEventArgs e)
    {
        if (e.componentID == componentID && !isMounted)
        {
            canBeMounted = e.canBeMounted;
            ApplyVisualAndColliderState();
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void LookedAt(Transform interactor)
    {
        if (canBeMounted && !isMounted)
        {
            if (interactor.TryGetComponent<PlayerInteractionNew>(out PlayerInteractionNew playerInteraction))
            {
                GameObject heldGo = playerInteraction.GetPickedUpGameObject();
                if (heldGo != null && heldGo.TryGetComponent<MountableBridgeComponent>(out MountableBridgeComponent heldComponent))
                {
                    if (heldComponent.GetMountableBridgeComponentSO().bridgeComponentSO == bridgeComponentSO)
                    {
                        Debug.Log("Looked at Bridge Component (Holding matching component)");
                        return;
                    }
                }
            }
            // Do not log "Looked at Bridge Component" if we don't have the matching component
        }
        else if (isMounted && !isAssembled)
        {
            Debug.Log("Looked at Bridge Component (Needs assembly)");
        }
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Bridge Component");
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        if (isMounted && !isAssembled && needAssembling && equippableItemSO != null)
        {
            HandleAssembling(equippableItemSO, equippableItemSO.ConstructionWorkPower);
        }
        else if (equippableItemSO == null)
        {
            EquippedItemTypeNeeded?.Invoke(this, EventArgs.Empty);
        }
    }

    public BridgeComponentSO GetBridgeComponentSO()
    {
        return bridgeComponentSO;
    }

    public bool IsMounted => isMounted;
    public bool CanBeMounted => canBeMounted && (constructionSite == null || constructionSite.CanAcceptMountedComponent);
    public bool IsAssembled => isAssembled;
    public bool NeedAssembling => needAssembling;
    public int ComponentID => componentID;
    public bool StartFullyCompleted => startFullyCompleted;

    public float GetAssemblingProgressNormalized()
    {
        if (assemblingProgressNeeded <= 0f) return 0f;
        return Mathf.Clamp01(currentAssemblingProgress / assemblingProgressNeeded);
    }

    public float GetAssemblingProgressNeeded()
    {
        return assemblingProgressNeeded;
    }

    public bool SupportsEquippableItemType(EquippableItemType equippableItemType)
    {
        return bridgeComponentSO != null && bridgeComponentSO.supportedEquippableItemTypeList.Contains(equippableItemType);
    }

    public void NotifyEquippedItemTypeNeeded()
    {
        EquippedItemTypeNeeded?.Invoke(this, EventArgs.Empty);
    }

    public void CompleteConstructionFromSite()
    {
        if (isAssembled)
        {
            return;
        }

        isAssembled = true;
        currentAssemblingProgress = assemblingProgressNeeded;
        ComponentAsembled?.Invoke(this, new ComponentAsembledEventArgs { componentID = componentID });
        ApplyVisualAndColliderState();
    }

    public void ApplyMountedState()
    {
        isMounted = true;
        canBeMounted = false;
        constructionSite?.NotifyMounted();
        ApplyVisualAndColliderState();
        ComponentMounted?.Invoke(this, new ComponentMountedEventArgs { componentID = componentID });
        if (!needAssembling)
        {
            ComponentAsembled?.Invoke(this, new ComponentAsembledEventArgs { componentID = componentID });
            isAssembled = true;
        }
    }

    public void ApplyNetworkState(BridgeComponentNetworkState state)
    {
        if (state.componentID != componentID)
        {
            return;
        }

        bool wasMounted = isMounted;
        bool wasAssembled = isAssembled;
        isMounted = state.isMounted;
        isAssembled = state.isAssembled;
        canBeMounted = state.canBeMounted;
        currentAssemblingProgress = state.currentAssemblingProgress;
        constructionSite?.ApplyNetworkState(state);
        mountSocket?.ApplyNetworkAlignmentState(state);

        ApplyVisualAndColliderState();

        if (!wasMounted && isMounted)
        {
            ComponentMounted?.Invoke(this, new ComponentMountedEventArgs { componentID = componentID });
        }

        if (!wasAssembled && isAssembled)
        {
            ComponentAsembled?.Invoke(this, new ComponentAsembledEventArgs { componentID = componentID });
        }
    }

    internal void ApplyInitialCompletedState()
    {
        InitializeDefinitionState();
        isMounted = true;
        isAssembled = true;
        canBeMounted = false;
        currentAssemblingProgress = assemblingProgressNeeded;
        constructionSite?.ApplyInitialCompletedState();

        if (mountSocket != null)
        {
            BridgeComponentNetworkState completedSocketState = new BridgeComponentNetworkState(componentID)
            {
                mountAlignmentState = (int)BridgeMountAlignmentState.Complete,
                mountAlignmentCandidateNetworkObjectId = BridgeMountSocket.NoCandidateNetworkObjectId,
                mountAlignmentStartedAt = -1d
            };
            mountSocket.ApplyNetworkAlignmentState(completedSocketState);
        }

        ApplyVisualAndColliderState();
    }

    private void ResetRuntimeState()
    {
        isMounted = false;
        isAssembled = false;
        canBeMounted = false;
        currentAssemblingProgress = 0f;
    }

    private void InitializeDefinitionState()
    {
        if (bridgeComponentSO == null)
        {
            assemblingProgressNeeded = 0f;
            needAssembling = false;
            return;
        }

        assemblingProgressNeeded = bridgeComponentSO.assemblingProgressNeeded;
        needAssembling = bridgeComponentSO.needAssembling;
    }

    private static bool HasInitialStateAuthority()
    {
        return Unity.Netcode.NetworkManager.Singleton == null
            || !Unity.Netcode.NetworkManager.Singleton.IsListening
            || Unity.Netcode.NetworkManager.Singleton.IsServer;
    }

    public void DamageReceived(float damage)
    {
        throw new NotImplementedException();
    }

    private void CacheBridgeComponentColliders()
    {
        readyForMountingInteractionColliders = readyForMountingVisualsGameObject != null
            ? readyForMountingVisualsGameObject.GetComponentsInChildren<Collider>(true)
            : Array.Empty<Collider>();

        if (mountedPhysicalColliders != null && mountedPhysicalColliders.Length > 0)
        {
            List<Collider> configuredPhysicalColliders = new List<Collider>();
            foreach (Collider collider in mountedPhysicalColliders)
            {
                if (collider != null && !IsReadyForMountingInteractionCollider(collider))
                {
                    configuredPhysicalColliders.Add(collider);
                }
            }

            mountedPhysicalColliders = configuredPhysicalColliders.ToArray();
            return;
        }

        List<Collider> physicalColliders = new List<Collider>();
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || IsReadyForMountingInteractionCollider(collider) ||
                (mountSocket != null && mountSocket.IsSocketCollider(collider)) ||
                (constructionSite != null && constructionSite.IsConstructionInteractionCollider(collider)) ||
                collider.GetComponentInParent<BridgeAbutmentWorkPoint>() != null ||
                collider.GetComponentInParent<BridgeGirderWorkPoint>() != null ||
                collider.GetComponentInParent<BridgeCrossBeamWorkPoint>() != null ||
                collider.GetComponentInParent<BridgeDiagonalBracingWorkPoint>() != null ||
                collider.GetComponentInParent<BridgeDeckPanelWorkPoint>() != null)
            {
                continue;
            }

            physicalColliders.Add(collider);
        }

        mountedPhysicalColliders = physicalColliders.ToArray();
    }

    private bool IsReadyForMountingInteractionCollider(Collider collider)
    {
        if (readyForMountingVisualsGameObject == null || collider == null)
        {
            return false;
        }

        Transform readyVisualsTransform = readyForMountingVisualsGameObject.transform;
        return collider.transform == readyVisualsTransform || collider.transform.IsChildOf(readyVisualsTransform);
    }

    private void ConfigureReadyForMountingInteractionColliders()
    {
        foreach (Collider interactionCollider in readyForMountingInteractionColliders)
        {
            if (interactionCollider == null)
            {
                continue;
            }

            interactionCollider.enabled = true;
            interactionCollider.isTrigger = true;
        }
    }

    private void ApplyVisualAndColliderState()
    {
        if (readyForMountingVisualsGameObject != null)
        {
            readyForMountingVisualsGameObject.SetActive(CanBeMounted && !isMounted);
        }

        if (mountedComponentVisualsGameObject != null)
        {
            mountedComponentVisualsGameObject.SetActive(constructionSite != null
                ? constructionSite.ShouldShowMountedComponentVisuals(isMounted)
                : isMounted);
        }

        if (mountedPhysicalColliders == null)
        {
            return;
        }

        foreach (Collider physicalCollider in mountedPhysicalColliders)
        {
            if (physicalCollider != null)
            {
                physicalCollider.enabled = constructionSite != null
                    ? constructionSite.ShouldEnablePhysicalColliders(isMounted)
                    : isMounted;
            }
        }
    }

    public BridgeConstructionSite ConstructionSite => constructionSite;
    public BridgeMountSocket MountSocket => mountSocket;
    public GameObject ReadyForMountingVisualsGameObject => readyForMountingVisualsGameObject;

    public void AddReadyForMountPrompt(List<InteractionPrompt> prompts, string legacyPrompt)
    {
        if (mountSocket != null)
        {
            mountSocket.AddMountPrompt(prompts);
            return;
        }

        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, legacyPrompt));
    }

    public void RefreshVisualAndColliderState()
    {
        ApplyVisualAndColliderState();
    }

#if UNITY_EDITOR
    public void RefreshInitialStatePreview()
    {
        if (Application.isPlaying)
        {
            return;
        }

        constructionSite = GetComponent<BridgeConstructionSite>();
        mountSocket = GetComponent<BridgeMountSocket>();
        InitializeDefinitionState();
        constructionSite?.Initialize();
        CacheBridgeComponentColliders();
        ConfigureReadyForMountingInteractionColliders();
        constructionSite?.RefreshInitialStatePreview(startFullyCompleted);
        mountSocket?.RefreshInitialStatePreview(startFullyCompleted);

        bool previousMounted = isMounted;
        bool previousAssembled = isAssembled;
        bool previousMountable = canBeMounted;
        isMounted = startFullyCompleted;
        isAssembled = startFullyCompleted;
        canBeMounted = false;
        ApplyVisualAndColliderState();
        isMounted = previousMounted;
        isAssembled = previousAssembled;
        canBeMounted = previousMountable;
    }
#endif
}
