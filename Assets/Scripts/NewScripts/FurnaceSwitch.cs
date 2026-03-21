using System;
using UnityEngine;

public class FurnaceSwitch : MonoBehaviour, IInteractableNew
{
    [SerializeField] private FurnaceStorage furnaceStorage;
    public EventHandler<FurnaceSwitchPressedEventArgs> FurnaceSwitchPressed;
    public class FurnaceSwitchPressedEventArgs : EventArgs
    {
        public Transform interactor;
    }


    public void Interact(Transform interactor)
    {
        // If ProgressNormalized == 1 -> invoke event to open minigame ui
        FurnaceSwitchPressed?.Invoke(this, new FurnaceSwitchPressedEventArgs
        {
            interactor = interactor
        });
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
