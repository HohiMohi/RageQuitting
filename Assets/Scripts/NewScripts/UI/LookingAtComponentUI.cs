using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class LookingAtComponentUI : MonoBehaviour
{
    [SerializeField] private TMP_Text componentInfoText;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private PlayerInteractionNew playerInteraction;
    [SerializeField] private UnityEngine.UI.Image assemblingProgressBar;
    [SerializeField] private GameObject progressCircleHolder;
    [SerializeField] private int maxPromptLines = 3;

    private PlayerInputNew playerInput;
    private PlayerHealth playerHealth;
    private readonly List<InteractionPrompt> prompts = new List<InteractionPrompt>();

    private void Start()
    {
        if (playerInteraction == null)
        {
            playerInteraction = GetComponentInParent<PlayerInteractionNew>();
        }

        playerInput = GetComponentInParent<PlayerInputNew>();
        playerHealth = GetComponentInParent<PlayerHealth>();
        Hide();
    }

    private void Update()
    {
        if (playerInteraction == null) return;

        if (playerHealth != null && playerHealth.IsDowned)
        {
            Hide();
            return;
        }

        IInteractableNew currentInteractable = playerInteraction.GetCurrentInteractable();
        if (currentInteractable == null)
        {
            Hide();
            return;
        }

        prompts.Clear();
        if (currentInteractable is MonoBehaviour currentBehaviour)
        {
            AddPromptsForCurrentState(currentBehaviour);
        }

        if (prompts.Count == 0)
        {
            Hide();
            return;
        }

        componentInfoText.text = FormatPrompts();
        UpdateProgressVisual(currentInteractable);
        Show();
    }

    private void AddPromptsForCurrentState(MonoBehaviour target)
    {
        if (playerInteraction.IsHoldingDownedPlayer)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Put down"));
            return;
        }

        if (playerInteraction.IsHoldingObject)
        {
            if (target is BaseStorageNew storage)
            {
                AddStoragePrompts(storage);
                if (prompts.Count > 0)
                {
                    return;
                }
            }

            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Drop"));
            return;
        }

        AddPromptsForTarget(target);
    }

    private void AddPromptsForTarget(MonoBehaviour target)
    {
        if (target.TryGetComponent(out IInteractionPromptProvider promptProvider))
        {
            promptProvider.GetInteractionPrompts(playerInteraction.transform, prompts);
            if (prompts.Count > 0)
            {
                return;
            }
        }

        if (TryAddDownedPlayerPrompts(target))
        {
            return;
        }

        switch (target)
        {
            case BridgeComponent bridgeComponent:
                AddBridgeComponentPrompts(bridgeComponent);
                break;
            case BaseStorageNew baseStorage:
                AddStoragePrompts(baseStorage);
                break;
            case BaseResourceNew baseResource:
                if (baseResource.CanBeCarried)
                {
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Pick up"));
                }
                break;
            case MountableBridgeComponent:
            case EquippableItem:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Pick up"));
                break;
            default:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Interact"));
                break;
        }

        AddAttackPromptIfValid(target);
    }

    private bool TryAddDownedPlayerPrompts(MonoBehaviour target)
    {
        PlayerHealth targetHealth = target.GetComponent<PlayerHealth>();
        DownedPlayerCarryable carryable = target.GetComponent<DownedPlayerCarryable>();
        if (targetHealth == null || !targetHealth.IsDowned || target.transform.root == playerInteraction.transform.root)
        {
            return false;
        }

        if (carryable != null && carryable.CanBeCarried)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Carry"));
        }

        if (targetHealth.CanBeRevived)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.ActionAlt, "Revive"));
        }

        return true;
    }

    private void AddBridgeComponentPrompts(BridgeComponent bridgeComponent)
    {
        if (bridgeComponent.CanBeMounted && !bridgeComponent.IsMounted)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Mount"));
            return;
        }

        if (bridgeComponent.IsMounted && !bridgeComponent.IsAssembled && bridgeComponent.NeedAssembling)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action, "Assemble"));
        }
    }

    private void AddStoragePrompts(BaseStorageNew storage)
    {
        GameObject heldObject = playerInteraction.GetPickedUpGameObject();
        if (heldObject == null)
        {
            return;
        }

        if (storage is MainStorageNew && heldObject.TryGetComponent(out MountableBridgeComponent _))
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Store"));
            return;
        }

        if (heldObject.TryGetComponent(out BaseResourceNew baseResource) && storage.IsStorable(baseResource.GetBaseResourceSO()))
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Store"));
        }
    }

    private void AddAttackPromptIfValid(MonoBehaviour target)
    {
        if (prompts.Count >= maxPromptLines || target.transform.root == playerInteraction.transform.root)
        {
            return;
        }

        if (target is BaseResourceNew baseResource && !baseResource.CanBeDestroyed)
        {
            return;
        }

        if (target is IDamageable && !(target is BridgeComponent))
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action, "Attack"));
        }
    }

    private void UpdateProgressVisual(IInteractableNew currentInteractable)
    {
        bool showProgress = currentInteractable is BridgeComponent bridgeComponent &&
                            bridgeComponent.IsMounted &&
                            !bridgeComponent.IsAssembled &&
                            bridgeComponent.NeedAssembling;

        if (progressCircleHolder != null)
        {
            progressCircleHolder.SetActive(showProgress);
        }

        if (showProgress && assemblingProgressBar != null && currentInteractable is BridgeComponent progressBridgeComponent)
        {
            assemblingProgressBar.fillAmount = progressBridgeComponent.GetAssemblingProgressNormalized();
        }
    }

    private string FormatPrompts()
    {
        int promptCount = Mathf.Min(prompts.Count, maxPromptLines);
        string[] lines = new string[promptCount];
        for (int i = 0; i < promptCount; i++)
        {
            string inputName = playerInput != null ? playerInput.GetInputDisplayName(prompts[i].ActionKind) : prompts[i].ActionKind.ToString();
            lines[i] = $"{inputName} - {prompts[i].Description}";
        }

        return string.Join("\n", lines);
    }

    private void Show()
    {
        if (visualRoot != null)
        {
            visualRoot.SetActive(true);
        }
    }

    private void Hide()
    {
        if (visualRoot != null)
        {
            visualRoot.SetActive(false);
        }

        if (progressCircleHolder != null)
        {
            progressCircleHolder.SetActive(false);
        }
    }
}
