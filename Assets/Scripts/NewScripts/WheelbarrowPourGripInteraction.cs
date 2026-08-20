using System.Collections.Generic;
using UnityEngine;

public class WheelbarrowPourGripInteraction : MonoBehaviour, IInteractableNew, IInteractionPromptProvider
{
    [SerializeField] private WheelbarrowPouringMinigame minigame;
    [SerializeField] private bool leftSide;
    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }
    public void Interact(Transform interactor) => minigame?.RequestJoin(interactor, leftSide);
    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts) =>
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Help pour concrete"));
}
