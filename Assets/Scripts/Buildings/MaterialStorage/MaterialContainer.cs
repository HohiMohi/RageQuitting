using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[Serializable]
public struct MaterialContainer
{
    [SerializeField]
    private BuildingMaterialDetailsSO buildingMaterialDetailsSO;
    [SerializeField]
    private int numOfHoldedMaterials;

     public MaterialContainer(BuildingMaterialDetailsSO buildingMaterialDetailsSO, int materialQuantity)
    {
        this.buildingMaterialDetailsSO = buildingMaterialDetailsSO;
        this.numOfHoldedMaterials = materialQuantity;
    }

    public void AddMaterialQuantity(int addedMaterialQuantity)
    {
        Debug.Log(numOfHoldedMaterials);
        this.numOfHoldedMaterials += addedMaterialQuantity;
        Debug.Log(numOfHoldedMaterials);
    }

    public bool RemoveMaterialQuantity(int removedMaterialQuantity)
    {
        if(numOfHoldedMaterials >= removedMaterialQuantity)
        {
            numOfHoldedMaterials -= removedMaterialQuantity;
            return true;
        }
        else
        {
            Debug.Log("There is not enough materials to remove from.");
            return false;
        }
    }

    public int GetHoldedMaterialQuantity()
    {
        return numOfHoldedMaterials;
    }

    public BuildingMaterialDetailsSO GetBuidlingMaterialDetialsSO()
    {
        return buildingMaterialDetailsSO;
    }

    #region Validation
#if UNITY_EDITOR
    
    private void OnValidation()
    {

    }

#endif
    #endregion
}
