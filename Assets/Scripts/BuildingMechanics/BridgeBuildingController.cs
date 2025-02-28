using System.Collections.Generic;
using UnityEngine;

public class BridgeBuildingController : MonoBehaviour
{
    public BridgeGraph bridgeGraphHolder;
    public Dictionary<ConstructionMaterialSO, int> bridgeCostDictionary = new Dictionary<ConstructionMaterialSO, int>();
    public BridgeGraph2 testBridgeHolder;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        CalculateBridgeCost();
        foreach (KeyValuePair<ConstructionMaterialSO, int> bridgeConstructionMaterial in bridgeCostDictionary)
        {
            Debug.Log(bridgeConstructionMaterial.Key.ID + ": " + bridgeConstructionMaterial.Value);
        }
        Debug.Log(testBridgeHolder.ValidateBridge());
    }

    public void CalculateBridgeCost()
    {
        foreach (BridgeNode node in bridgeGraphHolder.bridgeNodeList)
        {
            foreach (ConstructionMaterialSO construcionMaterial in node.buildingElementSO.constructionMaterialList)
            {
                if (bridgeCostDictionary.ContainsKey(construcionMaterial))
                    bridgeCostDictionary[construcionMaterial] += 1;
                else
                    bridgeCostDictionary.Add(construcionMaterial, 1);
            }
        }
    }
    

    // Update is called once per frame
    void Update()
    {
        
    }
}
