using UnityEngine;
using TMPro;
using System.Collections.Generic;

[DefaultExecutionOrder(100)]
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
    private PlayerActionController playerActionController;
    private readonly List<InteractionPrompt> prompts = new List<InteractionPrompt>();
    public MonoBehaviour EvaluatedTarget { get; private set; }
    public bool CurrentTargetHasActionablePrompt { get; private set; }

    private void Start()
    {
        if (playerInteraction == null)
        {
            playerInteraction = GetComponentInParent<PlayerInteractionNew>();
        }

        playerInput = GetComponentInParent<PlayerInputNew>();
        playerHealth = GetComponentInParent<PlayerHealth>();
        playerActionController = GetComponentInParent<PlayerActionController>();
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

        MonoBehaviour currentTarget = playerInteraction.CurrentTarget;
        if (currentTarget == null)
        {
            Hide();
            return;
        }

        prompts.Clear();
        AddPromptsForCurrentState(currentTarget);
        FilterUnavailableActionPrompts(currentTarget);
        EvaluatedTarget = currentTarget;
        CurrentTargetHasActionablePrompt = prompts.Exists(
            prompt => prompt.ActionKind != PlayerInputActionKind.Information);

        if (prompts.Count == 0)
        {
            Hide();
            return;
        }

        componentInfoText.text = FormatPrompts();
        UpdateProgressVisual(currentTarget);
        Show();
    }

    private void FilterUnavailableActionPrompts(MonoBehaviour target)
    {
        if (playerActionController == null)
        {
            return;
        }

        PlayerActionController.ActionAvailability availability =
            playerActionController.GetActionAvailability(target);
        if (availability.CanExecute)
        {
            return;
        }

        for (int i = 0; i < prompts.Count; i++)
        {
            if (prompts[i].ActionKind != PlayerInputActionKind.Action)
            {
                continue;
            }

            string unavailableReason;
            if (!availability.HasCorrectTool && availability.HasRequiredTool)
            {
                unavailableReason = $"Equip {FormatToolName(availability.RequiredTool)}";
            }
            else if (availability.HasCorrectTool && !availability.IsInRange)
            {
                unavailableReason = "Move closer";
            }
            else
            {
                unavailableReason = "Action unavailable";
            }

            prompts[i] = new InteractionPrompt(
                PlayerInputActionKind.Information,
                $"{unavailableReason} - {prompts[i].Description}");
        }
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
            GameObject heldObjectForPrompt = playerInteraction.GetPickedUpGameObject();
            if (heldObjectForPrompt != null && heldObjectForPrompt.TryGetComponent(out PortableSubstanceContainer container))
            {
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.ActionAlt, container.GetContextActionDescription(target)));
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Drop"));
                return;
            }

            if (target is BridgeComponent bridgeComponent && bridgeComponent.CanBeMounted)
            {
                GameObject heldObject = playerInteraction.GetPickedUpGameObject();
                if (heldObject != null &&
                    heldObject.TryGetComponent(out MountableBridgeComponent heldBridgeComponent) &&
                    heldBridgeComponent.GetMountableBridgeComponentSO() != null &&
                    heldBridgeComponent.GetMountableBridgeComponentSO().bridgeComponentSO == bridgeComponent.GetBridgeComponentSO())
                {
                    string componentName = heldBridgeComponent.GetMountableBridgeComponentSO().componentName;
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, $"Deliver {componentName}"));
                    return;
                }
            }

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
                if (baseResource.CanBeCarried && !baseResource.IsPickedUp)
                {
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Pick up"));
                }
                break;
            case MountableBridgeComponent:
            case EquippableItem:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Pick up"));
                break;
            default:
                if (target is IInteractableNew)
                {
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Interact"));
                }
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

    private void UpdateProgressVisual(MonoBehaviour currentTarget)
    {
        BridgeConstructionSite constructionSite = currentTarget as BridgeConstructionSite;
        if (constructionSite == null && currentTarget is BridgeComponent constructionBridge)
        {
            constructionSite = constructionBridge.ConstructionSite;
        }

        bool showConstructionProgress = constructionSite != null &&
                                        (constructionSite.CurrentStage == BridgeConstructionStage.Digging ||
                                         constructionSite.CurrentStage == BridgeConstructionStage.Hammering);
        bool showAssemblyProgress = currentTarget is BridgeComponent bridgeComponent &&
                                    bridgeComponent.IsMounted &&
                                    !bridgeComponent.IsAssembled &&
                                    bridgeComponent.NeedAssembling;
        bool showProgress = showConstructionProgress || showAssemblyProgress;

        if (progressCircleHolder != null)
        {
            progressCircleHolder.SetActive(showProgress);
        }

        if (showProgress && assemblingProgressBar != null)
        {
            if (showConstructionProgress && constructionSite.RequiredWorkProgress > 0f)
            {
                assemblingProgressBar.fillAmount = Mathf.Clamp01(
                    constructionSite.CurrentWorkProgress /
                    constructionSite.RequiredWorkProgress);
            }
            else if (currentTarget is BridgeComponent progressBridgeComponent)
            {
                assemblingProgressBar.fillAmount = progressBridgeComponent.GetAssemblingProgressNormalized();
            }
        }
    }

    private static string FormatToolName(EquippableItemType toolType)
    {
        return toolType == EquippableItemType.IndustrialHammer
            ? "Industrial Hammer"
            : toolType.ToString();
    }

    private string FormatPrompts()
    {
        int promptCount = Mathf.Min(prompts.Count, maxPromptLines);
        string[] lines = new string[promptCount];
        for (int i = 0; i < promptCount; i++)
        {
            if (prompts[i].ActionKind == PlayerInputActionKind.Information)
            {
                lines[i] = prompts[i].Description;
                continue;
            }

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
        EvaluatedTarget = null;
        CurrentTargetHasActionablePrompt = false;
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
