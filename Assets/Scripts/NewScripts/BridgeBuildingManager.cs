using System;
using Unity.VisualScripting;
using UnityEngine;

public class BridgeBuildingManager : MonoBehaviour
{
    public static BridgeBuildingManager Instance { get; private set; }

    public EventHandler<BridgeComponentMountableStatusUpdateEventArgs> BridgeComponentMountableStatusUpdate;
    public class BridgeComponentMountableStatusUpdateEventArgs : EventArgs
    {
        public bool canBeMounted;
        public int componentID;
    }
    public EventHandler<BridgeComponentStoredEventArgs> BridgeComponentStored;

    public class BridgeComponentStoredEventArgs : EventArgs
    {
        public BridgeComponentSO bridgeComponentSO;
        public int componentID;
    }

    [SerializeField] private Bridge bridge;
    [SerializeField] private MainStorageNew mainStorageNew;
    [SerializeField] private BridgeComponentData[] bridgeComponentDataArray;
    [SerializeField] private BridgeBuildingStage[] bridgeBuildingStages;
    [SerializeField] private int currentBridgeBuildingStageIndex;
    [SerializeField] private bool isFullyAsembled;
    

    private void Awake()
    {
        Instance = this;
        isFullyAsembled = false;
    }

    private void Start()
    {
        mainStorageNew.BridgeComponentStored += mainStorage_OnBridgeComponentStored;
        bridge.ComponentMounted += Bridge_OnComponentMounted;
        bridge.ComponentAssembled += Bridge_OnComponentAssembled;
    }

    private void Bridge_OnComponentAssembled(object sender, Bridge.ComponentAssembledEventArgs e)
    {
        bridgeComponentDataArray[e.componentID].isAssembled = true;
        CheckCurrentStageMountingProgress();
    }

    private void Bridge_OnComponentMounted(object sender, Bridge.ComponentMountedEventArgs e)
    {
        bridgeComponentDataArray[e.componentID].isMounted = true;
        CheckCurrentStageMountingProgress();

    }

    private void mainStorage_OnBridgeComponentStored(object sender, MainStorageNew.BridgeComponentStoredEventArgs e)
    {
        int componentID = -1;
        foreach (BridgeComponentData bridgeComponentData in bridgeComponentDataArray)
        {
            if (bridgeComponentData.bridgeComponentType == e.bridgeComponentSO.bridgeComponentType && bridgeComponentData.componentAdvancementLevel <= e.bridgeComponentSO.componentAdvancementLevel && !bridgeComponentData.CanBeMounted)
            {
                componentID = Array.IndexOf(bridgeComponentDataArray, bridgeComponentData);
                break;
            }
        }
        if (componentID == -1)
        {
            Debug.Log("No suitable bridge component found to override for the stored component: " + e.bridgeComponentSO.name);
            Debug.Log("In the future, invoke event to update the UI about the stored component that cannot be mounted yet.");
        }
        else
        {
            bridgeComponentDataArray[componentID].SetBridgeComponentSO(e.bridgeComponentSO);
            BridgeComponentStored?.Invoke(this, new BridgeComponentStoredEventArgs { bridgeComponentSO = e.bridgeComponentSO, componentID = componentID});
            UpdateComponentsCanBeMountedProperty();
        }

    }

    private void UpdateComponentsCanBeMountedProperty()
    {
        if (isFullyAsembled)
            return;
        foreach (int componentIndex in bridgeBuildingStages[currentBridgeBuildingStageIndex].bridgeComponentDataIndexes)
        {
            BridgeComponentData bridgeComponentData = bridgeComponentDataArray[componentIndex];
            if (bridgeComponentData.CanBeMounted)
            {
                BridgeComponentMountableStatusUpdate?.Invoke(this, new BridgeComponentMountableStatusUpdateEventArgs { canBeMounted = true, componentID = componentIndex });
            }
        }
    }

    private void CheckCurrentStageMountingProgress()
    {
        if (isFullyAsembled)
            return;
        foreach (int componentIndex in bridgeBuildingStages[currentBridgeBuildingStageIndex].bridgeComponentDataIndexes)
        {
            if (!bridgeComponentDataArray[componentIndex].isMounted || !bridgeComponentDataArray[componentIndex].isAssembled)
            {
                return;
            }
        }
        currentBridgeBuildingStageIndex++;
        if (currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length)
        {
            isFullyAsembled = true;
            // Invoke event, that bridge has been fully asembled
        }
        UpdateComponentsCanBeMountedProperty();

    }

}

[Serializable]
public struct BridgeBuildingStage
{
     public int[] bridgeComponentDataIndexes; // Indexes of the bridge components required for this stage

}

[Serializable]
public struct BridgeComponentData
{
    public BridgeComponentType bridgeComponentType;
    public int componentAdvancementLevel;
    public BridgeComponentSO bridgeComponentSO;
    public Vector3 position;
    public bool isMounted;
    public bool isAssembled;
    [SerializeField] private bool canBeMounted;

    public BridgeComponentData( BridgeComponentType componentType)
    {         
        bridgeComponentType = componentType;
        componentAdvancementLevel = 0;
        bridgeComponentSO = null;
        position = Vector3.zero;
        isMounted = false;
        isAssembled = false;
        canBeMounted = false;
    }
    public bool CanBeMounted
    {
        get { return canBeMounted; }
        set { canBeMounted = value; }
    }
    public BridgeComponentSO BridgeComponentSO
        {
            get { return bridgeComponentSO; }
            set
            {
                bridgeComponentSO = value;
                canBeMounted = true; // Set canBeMounted to true when a new BridgeComponentSO is assigned
        }
    }
    public void SetBridgeComponentSO(BridgeComponentSO newSO)
    {
        BridgeComponentSO = newSO;

    }
}