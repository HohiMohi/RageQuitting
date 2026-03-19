using System;
using UnityEngine;

public class FurnaceSwitch : MonoBehaviour, IInteractableNew
{
    public EventHandler FurnaceSwitchPressed;

    public void Interact(Transform interactor)
    {
        FurnaceSwitchPressed?.Invoke(this, EventArgs.Empty);
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at FurnaceSwitch");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from FurnaceSwitch");
    }
}
