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
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        currentAssemblingProgress = 0;
        BridgeBuildingManager.Instance.BridgeComponentMountableStatusUpdate += BridgeBuildingManager_OnBridgeComponentMountableStatusUpdate;
        BridgeBuildingManager.Instance.BridgeComponentStored += BridgeBuildingManager_OnBridgeComponentStored;
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
        Debug.Log("Looked at Bridge Component");
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

    public void DamageReceived(float damage)
    {
        throw new NotImplementedException();
    }
}
