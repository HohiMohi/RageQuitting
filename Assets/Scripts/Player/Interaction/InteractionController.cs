using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Interactor))]
public class InteractionController : MonoBehaviour
{

    private Interactor interactor;
    private Player player;
    private bool isHolding = false; //handle it different - to change
    public List<GameObject> interactableObjectInRangeList; // change to private and implement add/remove methods
    private Dictionary<GameObject, FixedJoint> jointedObjectsDictionary;
    private Rigidbody rigidbody;

    private void Awake()
    {
        // Load components
        interactor = GetComponent<Interactor>();
        player = GetComponent<Player>();
        interactableObjectInRangeList = new List<GameObject>();
        jointedObjectsDictionary = new();
        rigidbody = GetComponent<Rigidbody>();
    }

    public void OnInteract(InputAction.CallbackContext callbackContext)
    {
        if (callbackContext.started)
        {
            IInteractable objectToInteract = interactor.GetInteractableObject();
            //if (objectToInteract != null)
            if(interactableObjectInRangeList.Count > 0)
            {
                //if (interactor.GetPickableObject() != null)
                if (interactableObjectInRangeList[0].GetComponent<IPickable>() != null)
                {

                    // Temporary function - change needed
                    if (!isHolding)
                    {
                        //objectToInteract.GetGameObject().GetComponent<BuildingMaterial>().Interact(callbackContext, gameObject, "PickUp");
                        interactableObjectInRangeList[0].GetComponent<IInteractable>().GetGameObject().GetComponent<BuildingMaterial>().Interact(callbackContext, gameObject, "PickUp");
                    }
                    else
                    {
                        interactableObjectInRangeList[0].GetComponent<IInteractable>().GetGameObject().GetComponent<BuildingMaterial>().Interact(callbackContext, gameObject, "PutDown");
                        //objectToInteract.GetGameObject().GetComponent<BuildingMaterial>().Interact(callbackContext, gameObject, "PutDown");
                    }
                    Debug.Log("Holding status: " + isHolding);
                    //player.HoldObject(objectToInteract.GetGameObject());
                }
                else
                {
                    //objectToInteract.Interact(callbackContext); 
                    interactableObjectInRangeList[0].GetComponent<IInteractable>().Interact(callbackContext);
                }
            }
            else
            {
                Debug.Log("No interactable objects in range.");
            }
        }
    }

    /// <summary>
    /// Change isHolding to the opposite value.
    /// </summary>
    public void ChangeHoldingStatus()
    {
        isHolding = !isHolding;
    }

    /// <summary>
    /// Destroy all joints between holded connected objects.
    /// </summary>
    public void ClearJointDictionary()
    {
        foreach(KeyValuePair<GameObject, FixedJoint> keyValuePair in jointedObjectsDictionary)
        {
            Destroy(jointedObjectsDictionary[keyValuePair.Key]);
            if(keyValuePair.Key.GetComponent<InteractionController>() != null)
            {
                keyValuePair.Key.GetComponent<InteractionController>().DestroyJointToObject(gameObject);
            }
        }
        jointedObjectsDictionary = new();
    }

    public void DestroyJointToObject(GameObject connectedObject)
    {
        if(jointedObjectsDictionary.ContainsKey(connectedObject))
        {
            Destroy(jointedObjectsDictionary[connectedObject]);
            jointedObjectsDictionary.Remove(connectedObject);
        }
    }

    public void ConnectObject(Rigidbody rigidbodyToConnect, bool isRecurrent)
    {
        if (rigidbodyToConnect.gameObject != gameObject)
        {
            FixedJoint joint = rigidbody.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = rigidbodyToConnect;
            jointedObjectsDictionary.Add(rigidbodyToConnect.gameObject, joint);
            if (rigidbodyToConnect.gameObject.GetComponent<InteractionController>() != null && isRecurrent)
            {
                rigidbodyToConnect.gameObject.GetComponent<InteractionController>().ConnectObject(rigidbody, false);
            }
        }
    }
}
