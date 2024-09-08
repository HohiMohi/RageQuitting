using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Storage : MonoBehaviour
{
    #region Tooltip
    [Tooltip("Define BuildingMaterialsSO types that this storage should support.")]
    #endregion
    public BuildingMaterialDetailsSO[] supportedBuildingMaterialsTypes;

    public int numOfHolded;
    private MaterialContainer[] holdedMaterials;

    private void Awake()
    {
        CreateMaterialContainers();
    }

    /// <summary>
    /// Initialise struct, that will hold supported materials and their quantity.
    /// </summary>
    private void CreateMaterialContainers()
    {
        holdedMaterials = new MaterialContainer[supportedBuildingMaterialsTypes.Length];
        int counter = 0;
        foreach (BuildingMaterialDetailsSO buildingMatSO in supportedBuildingMaterialsTypes)
        {
            holdedMaterials[counter] = new MaterialContainer(buildingMatSO, 0);
            counter++;
        }
    }

    /// <summary>
    /// Check wheter the tested material is supported by this Storage.
    /// </summary>
    public bool IsMaterialSupported(BuildingMaterialDetailsSO materialDetailsSOToCheck)
    {
        bool isSupported = false;
        foreach(BuildingMaterialDetailsSO buildingMaterialSO in supportedBuildingMaterialsTypes)
        {
            if (buildingMaterialSO == materialDetailsSOToCheck)
            {
                isSupported = true;
            }
        }

        return isSupported;
    }
    /// <summary>
    /// Increase stored quantity of BuildingMaterial.
    /// </summary>
    public void IncreaseMaterialQuantity(BuildingMaterialDetailsSO materialDetailsSO, int materialQuantity)// materialQuantity is temp, adjustment needed
    {
        int counter = 0;
        foreach (MaterialContainer materialContainer in holdedMaterials)
        {
            if (materialContainer.GetBuidlingMaterialDetialsSO() == materialDetailsSO)
            {
                break;

            }
            counter++;
        }

        // Ogarn¹æ referencje
        holdedMaterials[counter].AddMaterialQuantity(materialQuantity);
        Debug.Log("Added " + materialDetailsSO + " to storage. Currently stored " + holdedMaterials[counter].GetHoldedMaterialQuantity() + " materials of this type.");
        numOfHolded = holdedMaterials[counter].GetHoldedMaterialQuantity();
    }

    /// <summary>
    /// Return quantity of selected BuidlingMaterial. If buildingMaterialDetailsSO is not supported(storage does not contain such as material) - returns -1.
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
