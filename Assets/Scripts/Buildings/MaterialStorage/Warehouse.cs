using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class Warehouse : MonoBehaviour 
{
    #region Tooltip
    [Tooltip("Define BuildingMaterialsSO types that this Warehouse should support.")]
    #endregion
    [SerializeField]
    private MaterialContainer[] holdedMaterials;

    private void Awake()
    {


    }

    /// <summary>
    /// Check wheter the tested material is supported by this Warehouse.
    /// </summary>
    public bool IsMaterialSupported(BuildingMaterialDetailsSO materialDetailsSOToCheck)
    {
        bool isSupported = false;

        foreach (MaterialContainer materialContainer in holdedMaterials)
        {
            if (materialContainer.GetBuidlingMaterialDetialsSO() == materialDetailsSOToCheck)
            {
                isSupported = true;
                break;
            }
        }

        return isSupported;
    }

    /// <summary>
    /// Remove amount of each required material
    /// </summary>
    public void ReduceMaterialQuantity(MaterialContainer[] requiredMaterialContainers)
    {
        foreach(MaterialContainer matContainer in requiredMaterialContainers)
        {
            BuildingMaterialDetailsSO materialDetailsSO = matContainer.GetBuidlingMaterialDetialsSO();
            int materialQuantityToRemove = matContainer.GetHoldedMaterialQuantity();
            IncreaseMaterialQuantity(materialDetailsSO, -materialQuantityToRemove);
        }
    }

    /// <summary>
    /// Increase stored quantity of BuildingMaterial.
    /// </summary>
    public void IncreaseMaterialQuantity(BuildingMaterialDetailsSO materialDetailsSO, int materialQuantity)// materialQuantity is temp, adjustment needed
    {
        int counter = -1;
        foreach (MaterialContainer materialContainer in holdedMaterials)
        {
            counter++;
            if (materialContainer.GetBuidlingMaterialDetialsSO() == materialDetailsSO)
            {
                break;

            }
        }
        if (counter == -1) return; // temp solution, needed for handling factories, that do not need any materials to create new thing
        // Ogarn¹æ referencje
        holdedMaterials[counter].AddMaterialQuantity(materialQuantity);
        Debug.Log("Added " + materialDetailsSO + " to Warehouse. Currently stored " + holdedMaterials[counter].GetHoldedMaterialQuantity() + " materials of this type.");
    }

    /// <summary>
    /// Return quantity of selected BuidlingMaterial. If buildingMaterialDetailsSO is not supported(Warehouse does not contain such as material) - returns -1.
    /// </summary>
    public int GetQuantityOfMaterial(BuildingMaterialDetailsSO buildingMaterialDetailsSO)
    {
        if (IsMaterialSupported(buildingMaterialDetailsSO))
        {
            foreach (MaterialContainer materialContainer in holdedMaterials)
            {
                if (materialContainer.GetBuidlingMaterialDetialsSO() == buildingMaterialDetailsSO)
                {
                    return materialContainer.GetHoldedMaterialQuantity();
                }
            }
        }

        return -1;
    }

    private void OnCollisionEnter(Collision collision)
    {
        BuildingMaterial materialToCheck = collision.gameObject.GetComponent<BuildingMaterial>();
        if (materialToCheck != null)
        {
            if (IsMaterialSupported(materialToCheck.buildingMaterialSO))
            {
                materialToCheck.StorageMaterial();
                IncreaseMaterialQuantity(materialToCheck.buildingMaterialSO, 1);

            }
        }
    }
}
