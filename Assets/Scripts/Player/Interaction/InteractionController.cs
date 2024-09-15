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

    private void Awake()
    {
        // Load components
        interactor = GetComponent<Interactor>();
        player = GetComponent<Player>();
    }

    public void OnInteract(InputAction.CallbackContext callbackContext)
    {
        if (callbackContext.started)
        {
            IInteractable objectToInteract = interactor.GetInteractableObject();
            if (objectToInteract != null)
            {
                if (interactor.GetPickableObject() != null)
                {

                    // Temporary function - change needed
                    if (!isHolding)
                    {
                        objectToInteract.GetGameObject().GetComponent<BuildingMaterial>().Interact(callbackContext, gameObject, "PickUp");
                        isHolding = !isHolding;
                    }
                    else
                    {
                        objectToInteract.GetGameObject().GetComponent<BuildingMaterial>().Interact(callbackContext, gameObject, "PutDown");
                        isHolding = !isHolding;
                    }
                    Debug.Log("Holding status: " + isHolding);
                    //player.HoldObject(objectToInteract.GetGameObject());
                }
                else
                {
                    objectToInteract.Interact(callbackContext); 
                }
            }
            else
            {
                Debug.Log("No interactable objects in range.");
            }
        }
    }
}
