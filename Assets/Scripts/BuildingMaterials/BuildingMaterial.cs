using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]

[DisallowMultipleComponent]
public class BuildingMaterial : MonoBehaviour, IPickable, IInteractable
{
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    private int maxHolders;
    private GameObject[] playersHoldingMaterial;

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        playersHoldingMaterial = new GameObject[maxHolders];
    }

    #region IPickable
    public GameObject GetGameObject()
    {
        return gameObject;
    }

    public void InitialiseBuildingMaterial(MeshFilter meshFilter, Material material)
    {
        this.meshFilter = meshFilter;
        this.meshRenderer.material = material;

        gameObject.SetActive(true);
    }

    public void PickedUp()
    {
        throw new System.NotImplementedException();
    }

    public void PuttedDown()
    {
        throw new System.NotImplementedException();
    }
    #endregion

    #region IInteractable
    public void Interact(InputAction.CallbackContext callbackContext)
    {
           
    }

    public void Interact(InputAction.CallbackContext callbackContext, GameObject interactingPlayer)
    {
        Debug.Log("Dzia³a");
    }
    #endregion
}
