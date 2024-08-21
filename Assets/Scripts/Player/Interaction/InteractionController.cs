using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Interactor))]
public class InteractionController : MonoBehaviour
{

    private Interactor interactor;

    private void Awake()
    {
        // Load components
        interactor = GetComponent<Interactor>();
    }

    public void OnInteract(InputAction.CallbackContext callbackContext)
    {
        if (callbackContext.started)
        {
            IInteractable objectToInteract = interactor.GetInteractableObject();
            if (objectToInteract != null)
            {
                objectToInteract.Interact();
            }
            else
            {
                Debug.Log("No interactable objects in range.");
            }
        }
    }
}
