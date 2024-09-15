using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class WoodFactor : MonoBehaviour, IInteractable
{
    // Move to other class to inherit from -> Factory
    [SerializeField] public BuildingMaterialDetailsSO buildingMaterialDetailsSO;
    [SerializeField] private GameObject warehousePrefab;
    [SerializeField] private int numOfNeededMaterial;
    private Warehouse warehouse;

    [SerializeField] private Transform spawnPoint;

    public MaterialContainer[] requiredBuildingMaterials;

    private void Awake()
    {
        warehouse = warehousePrefab.GetComponent<Warehouse>();
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Interact(InputAction.CallbackContext callbackContext) // Change needed - create new method to encapsulate this funcionality
    {
        if (IsEnoughMaterials())
        { 
            IPickable wood = (IPickable)PoolManager.Instance.ReuseComponent(buildingMaterialDetailsSO.buildingMaterialPrefab, spawnPoint.position, Quaternion.identity);
            wood.InitialiseBuildingMaterial(buildingMaterialDetailsSO.meshFilter, buildingMaterialDetailsSO.material, buildingMaterialDetailsSO);
            warehouse.ReduceMaterialQuantity(requiredBuildingMaterials);
        }
        else
        {
            Debug.Log("Not enough stored materials.");
        }
    }

    public GameObject GetGameObject()
    {
        return gameObject;
    }

    /// <summary>
    /// Check if all needed materials are present in factoryWarehouse and there is enough materials to create new element
    /// </summary>
    public bool IsEnoughMaterials()
    {
        bool isEnough = false;
        int numOfRequirementsMet = 0;
        // if no materials needed (list is empty) - return true
        if (requiredBuildingMaterials.Length < 1) return true; 

        // Check if factoryWarehouse contains enough materials
        foreach(MaterialContainer materialContainerToCheck in requiredBuildingMaterials)
        {
            BuildingMaterialDetailsSO materialToCheck = materialContainerToCheck.GetBuidlingMaterialDetialsSO();
            int numOfRequiredMaterials = materialContainerToCheck.GetHoldedMaterialQuantity();
            if (warehouse.IsMaterialSupported(materialToCheck))
            {
                if (warehouse.GetQuantityOfMaterial(materialToCheck) >= numOfRequiredMaterials)
                {
                    numOfRequirementsMet++;
                }
            }
        }

        // Check if all requirements were met
        if (numOfRequirementsMet == requiredBuildingMaterials.Length)
        {
            isEnough = true;
        }

        return isEnough;
    }

    #region Validation
#if UNITY_EDITOR
    // Fix needed

    private void OnValidate()
    {
        Warehouse warehouseTemp = warehousePrefab.GetComponent<Warehouse>();
        foreach (MaterialContainer materialContainer in requiredBuildingMaterials)
        {
            BuildingMaterialDetailsSO materialToCheck = materialContainer.GetBuidlingMaterialDetialsSO();
            if (!warehouseTemp.IsMaterialSupported(materialToCheck))
            {
                Debug.LogWarning(gameObject.name + " warehouse does not support material that was specified in requiredBuildingMaterials. Missing material: " + materialToCheck);
            }
        }
    }
#endif
    #endregion
}
