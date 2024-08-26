using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Interactor))]
public class InteractionController : MonoBehaviour
{

    private Interactor interactor;
    private Player player;


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
                Debug.Log("OnInteract - 1");
                if (interactor.GetPickableObject() != null)
                {
                    Debug.Log("OnInteract - 2");
                    // Temporary function - change needed
                    objectToInteract.GetGameObject().GetComponent<BuildingMaterial>().Interact(callbackContext, gameObject);
                    player.HoldObject(objectToInteract.GetGameObject());
                }
                else
                {
                    Debug.Log("OnInteract - 3");
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
