using System;
using UnityEngine;

public class Kindling : MonoBehaviour, IInteractableNew
{
    public EventHandler SetFurnaceOnFire;
    public void Interact(Transform interactor)
    {
        SetFurnaceOnFire?.Invoke(this, EventArgs.Empty);
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Kindling");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Kindling");
    }
}
