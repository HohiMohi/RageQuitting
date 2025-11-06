using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(SphereCollider))] 
[RequireComponent(typeof(Rigidbody))]

[DisallowMultipleComponent]
public class BuildingMaterial : MonoBehaviour, IPickable, IInteractable
{
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    private int maxHolders = 2;
    [SerializeField]
    private GameObject[] playersHoldingObject;
    private FixedJoint[] jointsArray;
    private Dictionary<GameObject, FixedJoint> playersJointsDictionary;
    private List<GameObject> connectedPlayersList;
    [SerializeField]
    private GameObject rigidBodyHolder1, rigidBodyHolder2;
    private bool isRigidbody1Jointed, isRigidbody2Jointer;
    public BuildingMaterialDetailsSO buildingMaterialSO;
    private List<InteractionController> playersInInteractionRangeList;
    private void Awake()
    {
        playersJointsDictionary = new Dictionary<GameObject, FixedJoint>();
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        playersHoldingObject = new GameObject[maxHolders];
        jointsArray = new FixedJoint[maxHolders];
        connectedPlayersList = new List<GameObject>();
        playersInInteractionRangeList = new();
    }

    #region IPickable
    public GameObject GetGameObject()
    {
        return gameObject;
    }

    public void InitialiseBuildingMaterial(MeshFilter meshFilter, Material material, BuildingMaterialDetailsSO buildingMaterial)
    {
        //Temporarly commented
        //this.meshFilter = meshFilter;
        //this.meshRenderer.material = material;
        //this.buildingMaterialSO = buildingMaterial;
        
        gameObject.SetActive(true);
    }

    public bool PickedUp(GameObject pickerPlayer)
    {
        if (connectedPlayersList.Count < maxHolders)
        {
            CreateJointAndConnect(pickerPlayer);
            return true;
        }
        return false;
    }

    public void PuttedDown(GameObject puttingDownPlayer)
    {
        if (IsPlayerHolding(puttingDownPlayer))
        {
            puttingDownPlayer.GetComponent<InteractionController>().ClearJointDictionary();
            connectedPlayersList.Remove(puttingDownPlayer);
        }
    }

    // Modify to handle multiple joints
    // Handle max connected players
    public void CreateJointAndConnect(GameObject objectToConnect)
    {

        InteractionController controller = objectToConnect.GetComponent<InteractionController>();

        controller.ConnectObject(rigidBodyHolder1.GetComponent<Rigidbody>(), false);

        connectedPlayersList.Add(objectToConnect);

        foreach (GameObject playerObject in connectedPlayersList)
        {
            playerObject.GetComponent<InteractionController>().ConnectObject(objectToConnect.GetComponent<Rigidbody>(), true);
        }

        //FixedJoint joint = objectToConnect.AddComponent<FixedJoint>();
        //if (!isRigidbody1Jointed)
        //{
        //    joint.connectedBody = rigidBodyHolder1.GetComponent<Rigidbody>();
        //    playersJointsDictionary.Add(objectToConnect, joint);
        //}
        //else if(!isRigidbody2Jointer)
        //{
        //    joint.connectedBody = rigidBodyHolder2.GetComponent<Rigidbody>();
        //    foreach (KeyValuePair<GameObject, FixedJoint> keyValuePair in playersJointsDictionary)
        //    {
        //        FixedJoint joint2 = keyValuePair.Key.AddComponent<FixedJoint>();
        //        joint2.connectedBody = objectToConnect.GetComponent<Rigidbody>();
        //    }
        //    playersJointsDictionary.Add(objectToConnect, joint);
        //}
        //playersHoldingObject[0] = objectToConnect;
        //jointsArray[0] = joint;
    }

    /// <summary>
    /// Check if interacting Player is connected to gameObject by FixedJoint
    /// </summary>
    public bool IsPlayerHolding(GameObject playerToCheck)
    {
        foreach (GameObject player in connectedPlayersList)
        {
            if (playerToCheck.GetInstanceID() == player.GetInstanceID())
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
        RemoveHoldingPlayers();
        ClearPlayerInInteractionRangeList();
        DisableMaterial();

    }

    /// <summary>
    /// Destroy all FixedJoint object.
    /// </summary>
    public void DestroyAllJoints()
    {
        foreach (GameObject player in connectedPlayersList)
        {
            if (player != null)
            {
                player.gameObject.GetComponent<InteractionController>().ClearJointDictionary();
            }
        }
    }

    /// <summary>
    /// Disable BuildingMaterial - return it to the poolManager
    /// </summary>
    public void DisableMaterial()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Clean list of players holding this object.
    /// </summary>
    public void RemoveHoldingPlayers()
    {
        foreach (GameObject player in connectedPlayersList)
        {
            if (player != null)
            {
                player.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Remove(gameObject);
                player.gameObject.GetComponent<InteractionController>().ChangeHoldingStatus();
            }
        }
        playersHoldingObject = new GameObject[maxHolders];
    }

    public void ClearPlayerInInteractionRangeList()
    {
        foreach (InteractionController controller in playersInInteractionRangeList)
        {
            if(controller.interactableObjectInRangeList.Contains(gameObject))
            {
                controller.interactableObjectInRangeList.Remove(gameObject);
            }
        }
        playersInInteractionRangeList = new();
    }

    #endregion

    #region IInteractable
    public void Interact(InputAction.CallbackContext callbackContext)
    {
           
    }

    public void Interact(InputAction.CallbackContext callbackContext, GameObject interactingPlayer, string performedAction)// change string to enum
    {
        if (performedAction == "PickUp")
        {
            bool isPickingUpPossible = PickedUp(interactingPlayer);
            if (isPickingUpPossible)
                interactingPlayer.GetComponent<InteractionController>().ChangeHoldingStatus();
        }
        else if (performedAction == "PutDown")
        {
            PuttedDown(interactingPlayer);
            interactingPlayer.GetComponent<InteractionController>().ChangeHoldingStatus();
            Debug.Log("Hmm");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.GetComponent<PlayerInput>() != null)
        {
            if (!other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Contains(gameObject))
            {
                other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Add(gameObject);
                Debug.Log(other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Count);
            }

            // Check, if player is in playerInInteractionRangeList - if not, add it to the list
            if (!playersInInteractionRangeList.Contains(other.gameObject.GetComponent<InteractionController>()))
            {
                playersInInteractionRangeList.Add(other.gameObject.GetComponent<InteractionController>());
                Debug.Log("Test1");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.GetComponent<PlayerInput>() != null)
        {
            if (other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Contains(gameObject))
            {
                other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Remove(gameObject);
                Debug.Log(other.gameObject.GetComponent<InteractionController>().interactableObjectInRangeList.Count);
            }
            // Check, if player is in playerInInteractionRangeList - if is present, remove it from the list
            if (playersInInteractionRangeList.Contains(other.gameObject.GetComponent<InteractionController>()))
            {
                playersInInteractionRangeList.Remove(other.gameObject.GetComponent<InteractionController>());
                Debug.Log("Test2");
            }
        }
    }
    #endregion
}
