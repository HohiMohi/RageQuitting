using System;
using UnityEngine;

public class BridgeComponent : MonoBehaviour, IInteractableNew
{
    [SerializeField] private int componentID;
    [SerializeField] private bool isMounted;
    [SerializeField] private bool canBeMounted;
    [SerializeField] private BridgeComponentSO bridgeComponentSO;


    public void Interact(Transform interactor)
    {
        Debug.Log("Interacted with bridge component: " + gameObject.name);
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
        Debug.Log(e.componentID + " " + componentID);
        if (e.componentID == componentID)
        {
            canBeMounted = e.canBeMounted;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
