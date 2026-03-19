using System;
using UnityEngine;

public class Bellows : MonoBehaviour, IInteractableNew
{
    public EventHandler BellowsPressed;

    public void Interact(Transform interactor)
    {
        BellowsPressed?.Invoke(this, EventArgs.Empty);
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Bellows");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Bellows");
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
