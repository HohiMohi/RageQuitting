using System.Collections.Generic;
using UnityEngine;

public class ConcreteMixerModeLever : MonoBehaviour, IInteractableNew, IInteractionPromptProvider
{
    [SerializeField] private ConcreteMixerController mixer;
    [SerializeField] private Transform leverVisual;
    [SerializeField] private Vector3 mixingLocalEuler = new Vector3(-35f, 0f, 0f);
    [SerializeField] private Vector3 pouringLocalEuler = new Vector3(35f, 0f, 0f);
    [SerializeField, Min(1f)] private float rotationSpeed = 180f;

    private void Update()
    {
        if (mixer == null || leverVisual == null) return;
        Quaternion target = Quaternion.Euler(mixer.Mode == ConcreteMixerMode.Mixing ? mixingLocalEuler : pouringLocalEuler);
        leverVisual.localRotation = Quaternion.RotateTowards(leverVisual.localRotation, target, rotationSpeed * Time.deltaTime);
    }

    public void Interact(Transform interactor) => mixer?.RequestToggleMode(interactor);

    public void LookedAt(Transform interactor) { }

    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        string label = mixer != null && mixer.Mode == ConcreteMixerMode.Mixing ? "Switch to pouring" : "Switch to mixing";
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, label));
    }
}
