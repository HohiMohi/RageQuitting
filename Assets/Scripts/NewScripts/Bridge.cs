using System;
using UnityEngine;
using static BridgeBuildingManager;

public class Bridge : MonoBehaviour
{
    [SerializeField] private BridgeComponent[] bridgeComponentArray;
    [SerializeField] private GameObject bridgeComponentHolder;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public EventHandler<ComponentMountedEventArgs> ComponentMounted;
    public EventHandler<ComponentAssembledEventArgs> ComponentAssembled;

    public class ComponentAssembledEventArgs : EventArgs
    {
        public int componentID;
    }
    public class ComponentMountedEventArgs : EventArgs
    {
        public int componentID;
    }

    private void Awake()
    {
        bridgeComponentArray = bridgeComponentHolder.GetComponentsInChildren<BridgeComponent>();

    }

    private void BridgeComponent_OnComponentMounted(object sender, BridgeComponent.ComponentMountedEventArgs e)
    {
        // Invoke event -> will be received by BridgeBuildingManager
        ComponentMounted?.Invoke(this, new ComponentMountedEventArgs { componentID = e.componentID });
    }

    void Start()
    {
        foreach (BridgeComponent bridgeComponent in bridgeComponentArray)
        {
            bridgeComponent.ComponentMounted += BridgeComponent_OnComponentMounted;
            bridgeComponent.ComponentAsembled += BridgeComponent_OnComponentAssembled;
        }
    }

    private void BridgeComponent_OnComponentAssembled(object sender, BridgeComponent.ComponentAsembledEventArgs e)
    {
        ComponentAssembled?.Invoke(this, new ComponentAssembledEventArgs { componentID = e.componentID });
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
