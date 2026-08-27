using System.Collections.Generic;
using UnityEngine;

public class WheelbarrowPourGripInteraction : MonoBehaviour, IInteractableNew, IInteractionPromptProvider
{
    [SerializeField] private WheelbarrowPouringMinigame minigame;
    [SerializeField] private bool leftSide;

    private bool locallyTargeted;

    public WheelbarrowPouringMinigame Minigame => minigame;
    public bool LeftSide => leftSide;
    public bool IsLocallyTargeted => locallyTargeted;
    public Collider InteractionCollider => GetComponent<Collider>();

    private void Awake()
    {
        if (GetComponent<WheelbarrowPourStationVisualizer>() == null)
        {
            gameObject.AddComponent<WheelbarrowPourStationVisualizer>();
        }
    }

    public void LookedAt(Transform interactor) => locallyTargeted = true;
    public void LookedAway(Transform interactor) => locallyTargeted = false;

    public void Interact(Transform interactor)
    {
        if (minigame != null && minigame.CanOfferJoin(interactor, leftSide))
        {
            minigame.RequestJoin(interactor, leftSide);
        }
    }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (minigame != null && minigame.CanOfferJoin(interactor, leftSide))
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Help pour concrete"));
        }
    }
}
