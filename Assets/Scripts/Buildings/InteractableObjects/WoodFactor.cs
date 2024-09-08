using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class WoodFactor : MonoBehaviour, IInteractable
{
    // Move to other class to inherit from -> Factory
    [SerializeField] public BuildingMaterialDetailsSO buildingMaterialDetailsSO;
    [SerializeField] private GameObject storagePrefab;
    [SerializeField] private int numOfNeededMaterial;
    private Storage storage;

    [SerializeField] private Transform spawnPoint;



    private void Awake()
    {
        storage = storagePrefab.GetComponent<Storage>();
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
            storage.IncreaseMaterialQuantity(buildingMaterialDetailsSO, -numOfNeededMaterial);
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

    public bool IsEnoughMaterials()
    {
        bool isEnough = false;
        
        if (storage.GetQuantityOfMaterial(buildingMaterialDetailsSO) >= numOfNeededMaterial) // adjustment needed - nod it just take buildingMaterialDetailsSO of created material, need to add option to specify materials needed by factory to create material
        {
            isEnough = true;
        }

        return isEnough;
    }
}
