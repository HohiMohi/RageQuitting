using System;
using UnityEngine;

public class BridgeComponent : MonoBehaviour, IInteractableNew, IDamageable
{
    [SerializeField] private int componentID;
    [SerializeField] private bool isMounted;
    [SerializeField] private bool canBeMounted;
    [SerializeField] private bool isAssembled;
    [SerializeField] private BridgeComponentSO bridgeComponentSO;
    [SerializeField] private GameObject readyForMountingVisualsGameObject;
    [SerializeField] private GameObject mountedComponentVisualsGameObject;
    [SerializeField] private Material ghostMaterial;
    private float assemblingProgressNeeded;
    private float currentAssemblingProgress;
    private bool needAssembling;

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
        if (canBeMounted && !isMounted)
        {
            if (interactor.TryGetComponent<PlayerInteractionNew>(out PlayerInteractionNew playerInteraction))
            {
                GameObject heldGo = playerInteraction.GetPickedUpGameObject();
                if (heldGo != null && heldGo.TryGetComponent<MountableBridgeComponent>(out MountableBridgeComponent heldComponent))
                {
                    if (heldComponent.GetMountableBridgeComponentSO().bridgeComponentSO == bridgeComponentSO)
                    {
                        playerInteraction.RemovePickedUpObject();

                        readyForMountingVisualsGameObject.SetActive(false);
                        mountedComponentVisualsGameObject.SetActive(true);
                        ComponentMounted?.Invoke(this, new ComponentMountedEventArgs { componentID = componentID });
                        isMounted = true;
                        if (!needAssembling)
                        {
                            ComponentAsembled?.Invoke(this, new ComponentAsembledEventArgs { componentID = componentID });
                            isAssembled = true;
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

        if (bridgeComponentSO.supportedEquippableItemTypeList.Contains(equippableItemSO.itemType))
        {
            currentAssemblingProgress += damage;
            if (currentAssemblingProgress >= assemblingProgressNeeded)
            {
                ComponentAsembled?.Invoke(this, new ComponentAsembledEventArgs { componentID = componentID });
                isAssembled = true;
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
        readyForMountingVisualsGameObject.SetActive(false);
        mountedComponentVisualsGameObject.SetActive(false);

        // Ensure all colliders in readyForMountingVisualsGameObject are triggers so players can walk through them
        if (readyForMountingVisualsGameObject != null)
        {
            foreach (Collider col in readyForMountingVisualsGameObject.GetComponentsInChildren<Collider>(true))
            {
                col.isTrigger = true;
            }
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        currentAssemblingProgress = 0;

        if (bridgeComponentSO != null)
        {
            assemblingProgressNeeded = bridgeComponentSO.assemblingProgressNeeded;
            needAssembling = bridgeComponentSO.needAssembling;
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
            readyForMountingVisualsGameObject.SetActive(true);
        }
    }

    private void BridgeBuildingManager_OnBridgeComponentMountableStatusUpdate(object sender, BridgeBuildingManager.BridgeComponentMountableStatusUpdateEventArgs e)
    {
        if (e.componentID == componentID && !isMounted)
        {
            canBeMounted = e.canBeMounted;
            readyForMountingVisualsGameObject.SetActive(true);
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
            HandleAssembling(equippableItemSO, damage);
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
    public bool CanBeMounted => canBeMounted;
    public bool IsAssembled => isAssembled;
    public bool NeedAssembling => needAssembling;

    public float GetAssemblingProgressNormalized()
    {
        if (assemblingProgressNeeded <= 0f) return 0f;
        return Mathf.Clamp01(currentAssemblingProgress / assemblingProgressNeeded);
    }

    public void DamageReceived(float damage)
    {
        throw new NotImplementedException();
    }
}
