using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public interface IInteractable
{
    public void Interact(InputAction.CallbackContext callbackContext);
    GameObject GetGameObject();
}
