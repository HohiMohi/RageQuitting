using System.Collections.Generic;
using UnityEngine;

public class ConcreteMixerCrank : MonoBehaviour, IInteractableNew, IInteractionPromptProvider
{
    [SerializeField] private ConcreteMixerController mixer;

    public void Interact(Transform interactor) => mixer?.RequestBeginCrank(interactor);

    public void LookedAt(Transform interactor) { }

    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        string label = mixer != null && mixer.IsCrankAvailable ? "Operate mixer crank" : "Crank unavailable";
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, label));
    }
}
