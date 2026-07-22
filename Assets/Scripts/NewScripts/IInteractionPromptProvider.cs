using System.Collections.Generic;
using UnityEngine;

public enum PlayerInputActionKind
{
    Information,
    Interact,
    Action,
    ActionAlt
}

public readonly struct InteractionPrompt
{
    public readonly PlayerInputActionKind ActionKind;
    public readonly string Description;

    public InteractionPrompt(PlayerInputActionKind actionKind, string description)
    {
        ActionKind = actionKind;
        Description = description;
    }
}

public interface IInteractionPromptProvider
{
    void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts);
}
