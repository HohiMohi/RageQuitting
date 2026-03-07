using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class MainStorageNew : BaseStorageNew
{
    public MainStorageNew Instance { get; private set; }
    [SerializeField] private bool allRequiredResourcesStored;
    //[SerializeField] private RequiredBridgeComponentsArray requiredBridgeComponentsArray; Check if we need this for the main storage
    private Dictionary<BridgeComponentSO, int> storedBridgeComponentDictionary;
    public EventHandler<BridgeComponentStoredEventArgs> BridgeComponentStored;
    public class BridgeComponentStoredEventArgs : EventArgs
    {
        public BridgeComponentSO bridgeComponentSO;
        public int totalAmountStored;
        public BridgeComponentStoredEventArgs(BridgeComponentSO bridgeComponentSO, int totalAmountStored)
        {
            this.bridgeComponentSO = bridgeComponentSO;
            this.totalAmountStored = totalAmountStored;
        }
    }


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
        allRequiredResourcesStored = false;
        storedBaseResourceDictionary = new Dictionary<BaseResourceSO, int>();
        storedBridgeComponentDictionary = new Dictionary<BridgeComponentSO, int>();
    }

    private void CheckAllRequiredStoredResources()
    {
        /*
        foreach (RequiredBridgeComponent requiredBridgeComponent in requiredBridgeComponentsArray.requiredBridgeComponents)
        {
            if(requiredBridgeComponent.requiredAmount >= storedBaseResourceDictionary[requiredBridgeComponent.resourceSO])
            {
                continue;
            }
            else
            {
                allRequiredResourcesStored = false;
                return;
            }
        }
        */
        Debug.Log("Temp function");
        allRequiredResourcesStored = true;
    }

    public override void StoreBaseResource(BaseResourceSO baseResourceSO, int amount)
    {
        base.StoreBaseResource(baseResourceSO, amount);
        CheckAllRequiredStoredResources();
        if (allRequiredResourcesStored)
        {
            Debug.Log("All required resources stored! Bridge can be built!");
            //Invoke event to notify bridge that all required resources are stored and it can be built
        }
    }
    
    public void StoreBridgeComponent(BridgeComponentSO bridgeComponentSO)
    {
        if (storedBridgeComponentDictionary.ContainsKey(bridgeComponentSO))
        {
            storedBridgeComponentDictionary[bridgeComponentSO]++;
        }
        else
        {
            storedBridgeComponentDictionary.Add(bridgeComponentSO, 1);
        }

        //Invoke event to notify BridgeBuildingManager that a bridge component has been stored in the main storage
        Debug.Log($"Bridge component {bridgeComponentSO.componentName} stored in main storage. Total amount stored: {storedBridgeComponentDictionary[bridgeComponentSO]}");
        BridgeComponentStored?.Invoke(this, new BridgeComponentStoredEventArgs(bridgeComponentSO, storedBridgeComponentDictionary[bridgeComponentSO]));
    }
}


/*
[Serializable]
public struct RequiredBridgeComponentsArray
{
    public RequiredBridgeComponent[] requiredBridgeComponents;
}

[Serializable]
public struct RequiredBridgeComponent
{
    // To change for BridgeComponentSO if we want to add more info about the bridge component in the future
    public BridgeComponentSO componentSO;
    public int requiredAmount;
    public RequiredBridgeComponent(BridgeComponentSO bridgeComponentSO, int requiredAmount)
    {
        this.componentSO = bridgeComponentSO;
        this.requiredAmount = requiredAmount;
    }
}
*/