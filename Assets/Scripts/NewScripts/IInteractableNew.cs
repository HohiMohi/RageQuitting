using UnityEngine;

public interface IInteractableNew
{
    public void Interact(Transform interactor);
    public void LookedAt(Transform interactor);
    public void LookedAway(Transform interactor);
}
