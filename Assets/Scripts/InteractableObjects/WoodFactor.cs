using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class WoodFactor : MonoBehaviour, IInteractable
{
    // Move to other class to inherit from -> Factory
    [SerializeField] public BuildingMaterialDetailsSO buildingMaterialDetailsSO;

    [SerializeField] private Transform spawnPoint;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Interact(InputAction.CallbackContext callbackContext)
    {
        IPickable wood = (IPickable)PoolManager.Instance.ReuseComponent(buildingMaterialDetailsSO.buildingMaterialPrefab, spawnPoint.position, Quaternion.identity);
        wood.InitialiseBuildingMaterial(buildingMaterialDetailsSO.meshFilter, buildingMaterialDetailsSO.material);
    }

    public GameObject GetGameObject()
    {
        return gameObject;
    }
}
