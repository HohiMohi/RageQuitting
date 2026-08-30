using System.Collections.Generic;
using UnityEngine;

public enum WheelbarrowInteractionKind : byte { Handles, Cargo, Passenger, Righting }

public class WheelbarrowInteractionPoint : MonoBehaviour, IInteractableNew, IInteractionPromptProvider, ICarriedResourceSink
{
    [SerializeField] private WheelbarrowController wheelbarrow;
    [SerializeField] private WheelbarrowInteractionKind interactionKind;

    public WheelbarrowController Wheelbarrow => wheelbarrow;
    public WheelbarrowInteractionKind InteractionKind => interactionKind;
    public bool IsRightingInteraction => wheelbarrow != null && wheelbarrow.State == WheelbarrowState.Tipped &&
        (interactionKind == WheelbarrowInteractionKind.Righting || interactionKind == WheelbarrowInteractionKind.Handles);

    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }

    public void Interact(Transform interactor)
    {
        if (wheelbarrow == null || interactor == null) return;
        if (wheelbarrow.State == WheelbarrowState.TrappedInFailedConcrete) return;
        PlayerInteractionNew player = interactor.GetComponent<PlayerInteractionNew>();
        if (interactionKind == WheelbarrowInteractionKind.Righting)
        {
            interactor.GetComponent<PlayerWheelbarrowController>()?.BeginRighting(wheelbarrow);
        }
        else if (interactionKind == WheelbarrowInteractionKind.Handles)
        {
            if (wheelbarrow.State == WheelbarrowState.Tipped)
            {
                interactor.GetComponent<PlayerWheelbarrowController>()?.BeginRighting(wheelbarrow);
                return;
            }
            wheelbarrow.RequestEnterDriver(interactor);
        }
        else if (interactionKind == WheelbarrowInteractionKind.Passenger) wheelbarrow.RequestEnterPassenger(interactor);
        else if (player != null && !wheelbarrow.RequestRemoveDownedPassenger(player)) wheelbarrow.TryUnloadLastResource(player);
    }

    public bool TryDepositCarriedResource(PlayerInteractionNew player, BaseResourceNew resource) =>
        interactionKind == WheelbarrowInteractionKind.Cargo && wheelbarrow != null && wheelbarrow.TryLoadResource(player, resource);

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (wheelbarrow == null || wheelbarrow.State == WheelbarrowState.TrappedInFailedConcrete) return;
        string prompt;
        if (interactionKind == WheelbarrowInteractionKind.Righting)
            prompt = "Hold E - Right wheelbarrow";
        else if (interactionKind == WheelbarrowInteractionKind.Handles)
            prompt = wheelbarrow.State == WheelbarrowState.Tipped ? "Hold E - Right wheelbarrow" : "Drive wheelbarrow";
        else if (interactionKind == WheelbarrowInteractionKind.Passenger) prompt = "Ride in wheelbarrow";
        else prompt = wheelbarrow.CargoCount > 0 ? "Take last cargo" : "Load carried resource";
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, prompt));
    }
}
