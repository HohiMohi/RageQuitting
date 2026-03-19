using System;
using UnityEngine;

public class VentilationGrille : MonoBehaviour, IInteractableNew
{
    public EventHandler VentilationGrilleClosed;


    public void Interact(Transform interactor)
    {
        VentilationGrilleClosed?.Invoke(this, EventArgs.Empty);
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Ventilation Grille");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Ventilation Grille");
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
