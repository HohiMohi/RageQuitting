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

    private int maxHolders = 2;
    private GameObject[] playersHoldingObject;
    private FixedJoint[] jointsArray;

    public BuildingMaterialDetailsSO buildingMaterialSO;
    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        playersHoldingObject = new GameObject[maxHolders];
        jointsArray = new FixedJoint[maxHolders];
    }

    #region IPickable
    public GameObject GetGameObject()
    {
        return gameObject;
    }

    public void InitialiseBuildingMaterial(MeshFilter meshFilter, Material material, BuildingMaterialDetailsSO buildingMaterial)
    {
        this.meshFilter = meshFilter;
        this.meshRenderer.material = material;
        this.buildingMaterialSO = buildingMaterial;
        
        gameObject.SetActive(true);
    }

    public void PickedUp(GameObject pickerPlayer)
    {
        CreateJointAndConnect(pickerPlayer);
    }

    public void PuttedDown(GameObject puttingDownPlayer)
    {
        if (IsPlayerHolding(puttingDownPlayer))
        {
            var jointToDestroy = gameObject.GetComponent<FixedJoint>();
            Destroy(jointToDestroy);
        }
    }

    // Modify to handle multiple joints
    public void CreateJointAndConnect(GameObject objectToConnect)
    {
        FixedJoint joint = gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = objectToConnect.GetComponent<Rigidbody>();
        playersHoldingObject[0] = objectToConnect;
        jointsArray[0] = joint;
    }

    /// <summary>
    /// Check if interacting Player is connected to gameObject by FixedJoint
    /// </summary>
    public bool IsPlayerHolding(GameObject playerToCheck)
    {
        foreach (FixedJoint jointToCheck in gameObject.GetComponents<FixedJoint>())
        {
            if (playerToCheck.GetInstanceID() == jointToCheck.connectedBody.gameObject.GetInstanceID())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Handle moving BuildingMaterial to storage - destroy all joints and disable this material gameObject.
    /// </summary>
    public void StorageMaterial()
    {
        DestroyAllJoints();
        DisableMaterial();
    }

    /// <summary>
    /// Destroy all FixedJoint object.
    /// </summary>
    public void DestroyAllJoints()
    {
        FixedJoint[] jointsToDestroy = gameObject.GetComponents<FixedJoint>();
        foreach (FixedJoint jointToDestroy in jointsToDestroy)
        {
            Destroy(jointToDestroy);
        }
    }

    /// <summary>
    /// Disable BuildingMaterial - return it to the poolManager
    /// </summary>
    public void DisableMaterial()
    {
        gameObject.SetActive(false);
    }

    #endregion

    #region IInteractable
    public void Interact(InputAction.CallbackContext callbackContext)
    {
           
    }

    public void Interact(InputAction.CallbackContext callbackContext, GameObject interactingPlayer, string performedAction)// change string to enum
    {
        if (performedAction == "PickUp")
            PickedUp(interactingPlayer);
        else if (performedAction == "PutDown")
        {
            PuttedDown(interactingPlayer);
            Debug.Log(gameObject.GetComponent<FixedJoint>());
        }
    }
    #endregion
}
