using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuildingElementsInstantiator : MonoBehaviour
{
    public BuildingElementSO elementToInstantiate;
    public GameObject bridgeElementPrefab;
    

    //public void InstantiateElement(BuildingElementSO elementSO)
    //{
    //    if (elementSO is BridgeSupportSO)
    //    {
    //        GameObject newObject = Instantiate<GameObject>(bridgeElementPrefab);
    //    }
        
    //}
    private void Start()
    {
       // InstantiateElement(elementToInstantiate);
    }
}
