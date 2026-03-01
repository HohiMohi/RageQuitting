using System;
using UnityEngine;
using static BridgeBuildingManager;

public class Bridge : MonoBehaviour
{
    [SerializeField] private BridgeComponent[] bridgeComponentArray;
    [SerializeField] private GameObject bridgeComponentHolder;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public EventHandler<ComponentMountedEventArgs> ComponentMounted;

    public class ComponentMountedEventArgs : EventArgs
    {
        public int componentID;
    }

    private void Awake()
    {
        bridgeComponentArray = bridgeComponentHolder.GetComponentsInChildren<BridgeComponent>();

    }

    private void BridgeComponent_OnComponeneMounted(object sender, BridgeComponent.ComponentMountedEventArgs e)
    {
        // Invoke event -> will be received by BridgeBuildingManager
        ComponentMounted?.Invoke(this, new ComponentMountedEventArgs { componentID = e.componentID });
    }

    void Start()
    {
        foreach (BridgeComponent bridgeComponent in bridgeComponentArray)
        {
            bridgeComponent.ComponentMounted += BridgeComponent_OnComponeneMounted;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
