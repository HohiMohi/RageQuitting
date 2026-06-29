using System;
using UnityEngine;
using static BridgeBuildingManager;

public class GameplayManager : MonoBehaviour
{
    public static GameplayManager Instance { get; private set; }


    [SerializeField] private Bridge bridge;
    [SerializeField] private BridgeComponentData[] bridgeComponentDataArray;
    [SerializeField] private BridgeBuildingStage[] bridgeBuildingStages;
    [SerializeField] private int currentBridgeBuildingStageIndex;
    [SerializeField] private bool isFullyAsembled;


    public EventHandler<BridgeComponentMountableStatusUpdateEventArgs> BridgeComponentMountableStatusUpdate;
    public class BridgeComponentMountableStatusUpdateEventArgs : EventArgs
    {
        public bool canBeMounted;
        public int componentID;
    }

    public bool IsFullyAssembled => isFullyAsembled;
    public event EventHandler OnBridgeFullyAssembled;
    private void Awake()
    {
        Instance = this;
        isFullyAsembled = false;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private System.Collections.IEnumerator Start()
    {
        bridge.ComponentMounted += Bridge_OnComponentMounted;
        bridge.ComponentAssembled += Bridge_OnComponentAssembled;
        yield return null; // Wait one frame for all BridgeComponents to subscribe in Start
        UpdateComponentsCanBeMountedProperty();
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

    // Update is called once per frame
    void Update()
    {

    }

    private void UpdateComponentsCanBeMountedProperty()
    {
        if (isFullyAsembled)
            return;
        foreach (int componentIndex in bridgeBuildingStages[currentBridgeBuildingStageIndex].bridgeComponentDataIndexes)
        {
            bridgeComponentDataArray[componentIndex].CanBeMounted = true;
            BridgeComponentMountableStatusUpdate?.Invoke(this, new BridgeComponentMountableStatusUpdateEventArgs { canBeMounted = true, componentID = componentIndex });
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
            OnBridgeFullyAssembled?.Invoke(this, EventArgs.Empty);
        }
        UpdateComponentsCanBeMountedProperty();

    }
}
