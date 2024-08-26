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

    public void InitialiseBuildingMaterial(MeshFilter meshFilter, Material material)
    {
        this.meshFilter = meshFilter;
        this.meshRenderer.material = material;

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
