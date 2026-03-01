using System;
using UnityEngine;

public class BridgeComponent : MonoBehaviour, IInteractableNew
{
    [SerializeField] private int componentID;
    [SerializeField] private bool isMounted;
    [SerializeField] private bool canBeMounted;
    [SerializeField] private BridgeComponentSO bridgeComponentSO;
    [SerializeField] private GameObject readyForMountingVisualsGameObject;
    [SerializeField] private GameObject mountedComponentVisualsGameObject;

    public EventHandler<ComponentMountedEventArgs> ComponentMounted;

    public class ComponentMountedEventArgs: EventArgs
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
        BridgeBuildingManager.Instance.BridgeComponentMountableStatusUpdate += BridgeBuildingManager_OnBridgeComponentMountableStatusUpdate;
        BridgeBuildingManager.Instance.BridgeComponentStored += BridgeBuildingManager_OnBridgeComponentStored;
    }

    private void BridgeBuildingManager_OnBridgeComponentStored(object sender, BridgeBuildingManager.BridgeComponentStoredEventArgs e)
    {
        if (e.componentID == componentID)
        {
            bridgeComponentSO = e.bridgeComponentSO;
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
}
