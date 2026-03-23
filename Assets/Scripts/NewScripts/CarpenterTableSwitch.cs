using System;
using UnityEngine;

public class CarpenterTableSwitch : MonoBehaviour, IInteractableNew
{
    public EventHandler<CarpenterTableSwitchPressedEventArgs> CarpenterTableSwitchPressed;
    public class CarpenterTableSwitchPressedEventArgs : EventArgs
    {
        public Transform interactor;
    }
    public void Interact(Transform interactor)
    {
        CarpenterTableSwitchPressed?.Invoke(this, new CarpenterTableSwitchPressedEventArgs
        {
            interactor = interactor
        });
        Debug.Log("Carpenter Switch clicked");
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Carpenter Switch");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Carpenter Switch");

    }

}
