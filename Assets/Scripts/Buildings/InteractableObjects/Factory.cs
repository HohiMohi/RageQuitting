using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Factory : MonoBehaviour, IInteractable
{
    [SerializeField] public BuildingMaterialDetailsSO buildingMaterialDetailsSO;
    [SerializeField] private GameObject warehousePrefab;
    [SerializeField] private int numOfNeededMaterial;
    private Warehouse warehouse;
    [SerializeField] private BuildingMaterialDetailsSO objectTypeNeededToInteraction;

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

    public void OnTriggerEnter(Collider other)
    {
        if(other.gameObject.GetComponent<PlayerInput>() != null)
        {
            if (!other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Contains(gameObject))
            {
                other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Add(gameObject);
                Debug.Log(other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Count);
            }
        }
    }

    public void OnTriggerExit(Collider other)
    {
        if (other.gameObject.GetComponent<PlayerInput>() != null)
        {
            if (other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Contains(gameObject))
            {
                other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Remove(gameObject);
                Debug.Log(other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Count);
            }
        }
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

    public bool CheckIntaractionConditions(GameObject interactionCallerObject)
    {
        if (objectTypeNeededToInteraction == null)
            return true;

        BuildingMaterialDetailsSO holdedItemType = null;
        try
        {
            // Do POPRAWY - temp solution
            holdedItemType = interactionCallerObject.GetComponent<InteractionController>().GetHoldedObject().transform.parent.transform.parent.GetComponent<BuildingMaterial>().buildingMaterialSO;
            print(holdedItemType);
        }
        catch
        {

        }
        if (objectTypeNeededToInteraction == holdedItemType)
            return true;
        else
        {
            print(interactionCallerObject.GetComponent<InteractionController>().GetHoldedObject());
            print("You need to hold object that contains " + objectTypeNeededToInteraction.ToString());
            return false;
        }
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
