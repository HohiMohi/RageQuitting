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
    private PlayerInventory playerInventory;
    private PlayerActionController playerActionController;
    private PlayerSingleCarryPlacementController placementController;
    private BridgeDeckPanelSlotOutline focusedPanelOutline;
    private string cachedPlacementHudText;
    private string cachedPlacementHudStatus;
    private string cachedPlacementHudConfirmHint;
    private string cachedPlacementHudMounting;
    private bool cachedPlacementHudPending;
    private MountingNail currentCrowbarPresentationNail;
    private bool currentTargetHasCrowbarNailHighlight;
    private bool currentCrowbarExtractionAwaitingServer;
    private float currentCrowbarExtractionProgress;
    private bool currentSawPresentation;
    private bool currentSawCutActive;
    private float currentSawCutProgress;
    private RectTransform promptLayoutRootRect;
    private RectTransform promptVisualRect;
    private RectTransform promptTextRect;
    private Vector2 originalPromptRootSize;
    private Vector2 originalPromptVisualSize;
    private Vector2 originalPromptTextSize;
    private string lastRenderedPromptText;
    private bool lastRenderedPromptUsesAreaLayout;
    private bool promptLayoutCaptured;
    private static readonly RaycastHit[] DeckOcclusionHits = new RaycastHit[16];
    private readonly List<InteractionPrompt> prompts = new List<InteractionPrompt>();
    public MonoBehaviour EvaluatedTarget { get; private set; }
    public bool CurrentTargetHasActionablePrompt { get; private set; }
    public bool CurrentTargetHasCrowbarNailHighlight => currentTargetHasCrowbarNailHighlight;

    private void Start()
    {
        if (playerInteraction == null)
        {
            playerInteraction = GetComponentInParent<PlayerInteractionNew>();
        }

        playerInput = GetComponentInParent<PlayerInputNew>();
        playerHealth = GetComponentInParent<PlayerHealth>();
        playerInventory = GetComponentInParent<PlayerInventory>();
        playerActionController = GetComponentInParent<PlayerActionController>();
        placementController = GetComponentInParent<PlayerSingleCarryPlacementController>();
        Hide();
    }

    private void Update()
    {
        if (playerInteraction == null)
        {
            Hide();
            return;
        }

        if (playerInput != null && !playerInput.IsLocalGameplayInputActive)
            ClearPanelPlacementFeedback();

        if (playerHealth != null && playerHealth.IsDowned)
        {
            Hide();
            return;
        }

        if (playerInput != null && playerInput.IsSingleCarryPlacementActive)
        {
            ClearPanelPlacementFeedback();
            if (placementController == null)
                placementController = GetComponentInParent<PlayerSingleCarryPlacementController>();
            ShowPlacementStatus();
            return;
        }

        MonoBehaviour currentTarget = playerInteraction.CurrentTarget;
        prompts.Clear();
        ResetCrowbarPresentation();
        ResetSawPresentation();
        string sawPrompt = null;
        bool sawPresented = playerActionController != null
            && playerActionController.TryGetSawCutPresentation(currentTarget, out sawPrompt,
                out currentSawCutProgress, out currentSawCutActive);
        if (sawPresented) currentSawPresentation = true;

        string aimedGapPrompt = null;
        Camera aimCamera = playerInteraction.GetComponentInChildren<Camera>();
        if (aimCamera == null) aimCamera = Camera.main;
        Unity.Netcode.NetworkObject actorNetworkObject = playerInteraction.GetComponentInParent<Unity.Netcode.NetworkObject>();
        Unity.Netcode.NetworkManager actorNetworkManager = actorNetworkObject != null
            ? actorNetworkObject.NetworkManager : Unity.Netcode.NetworkManager.Singleton;
        Ray aimedGapRay = aimCamera != null ? new Ray(aimCamera.transform.position, aimCamera.transform.forward) : default;
        if (aimCamera != null
            && BridgeDeckAreaRequirement.TryGetPresentedInScene(gameObject.scene, actorNetworkManager, out BridgeDeckAreaRequirement areaRequirement)
            && areaRequirement.TryGetAimedGap(aimedGapRay,
                out BridgeDeckAreaNeighborGap aimedGap, out _, out _, out _)
            && IsDeckOpeningVisible(areaRequirement, aimedGapRay, actorNetworkObject)
            && (currentTarget == null || currentTarget.GetComponentInParent<BridgeDeckAreaPanel>() == null))
        {
            aimedGapPrompt = areaRequirement.GetAimedGapGuidance(playerInteraction.transform);
        }

        if (currentTarget == null)
        {
            if (!sawPresented && string.IsNullOrEmpty(aimedGapPrompt)) { Hide(); return; }
        }
        else if (TryGetCrowbarNailTarget(currentTarget, out _))
        {
            if (playerInput == null || !playerInput.IsLocalGameplayInputActive
                || playerActionController == null
                || !playerActionController.TryGetCrowbarNailPresentation(currentTarget,
                    out currentCrowbarPresentationNail, out MountingNailExtractionAvailability availability,
                    out currentCrowbarExtractionProgress, out bool ownAcceptedAttempt,
                    out currentCrowbarExtractionAwaitingServer, out bool withinExtractionRange))
            {
                Hide();
                return;
            }

            currentTargetHasCrowbarNailHighlight = true;
            AddCrowbarNailPresentationPrompt(availability, ownAcceptedAttempt, withinExtractionRange);
        }
        else
        {
            AddPromptsForCurrentState(currentTarget);
            FilterUnavailableActionPrompts(currentTarget);
            if (playerInventory != null && playerInventory.GetCurrentSelectedItem() != null
                && playerInventory.GetCurrentSelectedItem().itemType == EquippableItemType.Crowbar)
                RemoveConsumedCrowbarAltPrompts();
        }
        bool targetIsAreaPanel = currentTarget != null && currentTarget.GetComponentInParent<BridgeDeckAreaPanel>() != null;
        bool showGapGuidance = !targetIsAreaPanel && !string.IsNullOrEmpty(aimedGapPrompt);
        if (sawPresented)
        {
            PlayerInputActionKind kind = sawPrompt.StartsWith("Saw: cut unavailable", System.StringComparison.Ordinal)
                ? PlayerInputActionKind.Information : PlayerInputActionKind.Action;
            prompts.Insert(0, new InteractionPrompt(kind,
                showGapGuidance ? $"{sawPrompt}\n{aimedGapPrompt}" : sawPrompt));
        }
        if (!sawPresented && showGapGuidance)
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, aimedGapPrompt));
        if (currentTarget != null) RefreshPanelPlacementFeedback(currentTarget);
        EvaluatedTarget = currentTarget;
        CurrentTargetHasActionablePrompt = prompts.Exists(
            prompt => prompt.ActionKind != PlayerInputActionKind.Information);

        if (prompts.Count == 0)
        {
            Hide();
            return;
        }

        SetPromptText(FormatPrompts(), targetIsAreaPanel || showGapGuidance);
        UpdateProgressVisual(currentTarget);
        Show();
    }

    private static bool IsDeckOpeningVisible(BridgeDeckAreaRequirement requirement, Ray ray,
        Unity.Netcode.NetworkObject actor)
    {
        if (requirement == null || requirement.DeckFrame == null) return false;
        Plane plane = new(requirement.DeckFrame.up, requirement.DeckFrame.position);
        if (!plane.Raycast(ray, out float distance) || distance < 0f) return false;
        int hitCount = Physics.RaycastNonAlloc(ray, DeckOcclusionHits, Mathf.Max(0f, distance - .01f),
            ~0, QueryTriggerInteraction.Ignore);
        if (hitCount == DeckOcclusionHits.Length) return false;
        for (int i = 0; i < hitCount; i++)
        {
            Transform hit = DeckOcclusionHits[i].collider != null ? DeckOcclusionHits[i].collider.transform : null;
            if (actor != null && hit != null && hit.IsChildOf(actor.transform)) continue;
            return false;
        }
        return true;
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
            MountingNail aimedNail = target as MountingNail;
            if (aimedNail == null && target != null) aimedNail = target.GetComponentInParent<MountingNail>();
            if (aimedNail != null && (aimedNail.State == MountingNailState.Prepared || aimedNail.BlocksPickup))
            {
                aimedNail.GetInteractionPrompts(playerInteraction.transform, prompts);
                return;
            }
            BridgeDeckAreaPanel areaPanel = target != null ? target.GetComponentInParent<BridgeDeckAreaPanel>() : null;
            bool areaSurfaceTarget = target == areaPanel || target is BridgeDeckPanelMounting || target is MountableBridgeComponent;
            if (areaPanel != null && areaSurfaceTarget
                && (!areaPanel.TryGetComponent(out MountableBridgeComponent areaMountable) || !areaMountable.IsPickedUp))
            {
                areaPanel.GetInteractionPrompts(playerInteraction.transform, prompts);
                return;
            }
            BridgeDeckPanelMounting panelMarker = target != null ? target.GetComponentInParent<BridgeDeckPanelMounting>() : null;
            if (panelMarker != null && panelMarker.TryGetComponent(out MountableBridgeComponent lookedAtPanel)
                && !lookedAtPanel.IsPickedUp)
                panelMarker.AddPlacementInformationPrompts(prompts);
            return;
        }

        AddPromptsForTarget(target);
    }

    private void AddPromptsForTarget(MonoBehaviour target)
    {
        BridgeDeckAreaPanel areaPanel = target != null ? target.GetComponentInParent<BridgeDeckAreaPanel>() : null;
        MountingNail mountingNail = target as MountingNail;
        if (mountingNail == null && target != null) mountingNail = target.GetComponentInParent<MountingNail>();
        if (mountingNail != null && (mountingNail.State == MountingNailState.Prepared || mountingNail.BlocksPickup))
        {
            mountingNail.GetInteractionPrompts(playerInteraction.transform, prompts);
            return;
        }

        bool areaSurfaceTarget = target != null && (target == areaPanel
            || target is BridgeDeckPanelMounting || target is MountableBridgeComponent);
        if (areaPanel != null && areaSurfaceTarget)
        {
            areaPanel.GetInteractionPrompts(playerInteraction.transform, prompts);
            return;
        }

        if ((mountingNail == null || mountingNail.State != MountingNailState.Loose)
            && target.TryGetComponent(out IInteractionPromptProvider promptProvider))
        {
            promptProvider.GetInteractionPrompts(playerInteraction.transform, prompts);
            return;
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
                if (baseResource.CanBePickedUp && !baseResource.IsPickedUp)
                {
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Pick up"));
                }
                break;
            case MountableBridgeComponent:
                if (target is MountableBridgeComponent bridge && bridge.CanBePickedUp)
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Pick up"));
                break;
            case EquippableItem equippable when !MountingNailSceneState.IsTargetConnected(equippable.transform):
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

    private bool TryGetCrowbarNailTarget(MonoBehaviour target, out MountingNail nail)
    {
        nail = null;
        EquippableItemSO selected = playerInventory != null ? playerInventory.GetCurrentSelectedItem() : null;
        if (selected == null || selected.itemType != EquippableItemType.Crowbar || target == null) return false;
        nail = target as MountingNail;
        if (nail == null) nail = target.GetComponentInParent<MountingNail>();
        return nail != null && (nail.State == MountingNailState.Partial
            || nail.State == MountingNailState.EmbeddedSingle
            || nail.State == MountingNailState.EmbeddedPair);
    }

    private void AddCrowbarNailPresentationPrompt(MountingNailExtractionAvailability availability,
        bool ownAcceptedAttempt, bool withinExtractionRange)
    {
        if (currentCrowbarExtractionAwaitingServer)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Awaiting server"));
            return;
        }

        switch (availability)
        {
            case MountingNailExtractionAvailability.Available:
                prompts.Add(withinExtractionRange
                    ? new InteractionPrompt(PlayerInputActionKind.ActionAlt, "Hold to extract nail")
                    : new InteractionPrompt(PlayerInputActionKind.Information, "Move closer - Extract nail"));
                break;
            case MountingNailExtractionAvailability.Reserved:
                if (ownAcceptedAttempt)
                {
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                        $"Extracting nail: {Mathf.RoundToInt(currentCrowbarExtractionProgress * 100f)}%"));
                }
                else
                {
                    prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
                        "Nail is being extracted by another player"));
                }
                break;
            case MountingNailExtractionAvailability.PermanentlyMounted:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Nail permanently mounted"));
                break;
            default:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Nail unavailable"));
                break;
        }
    }

    private void RemoveConsumedCrowbarAltPrompts()
    {
        for (int i = prompts.Count - 1; i >= 0; i--)
        {
            if (prompts[i].ActionKind == PlayerInputActionKind.ActionAlt)
                prompts.RemoveAt(i);
        }
    }

    private bool TryAddDownedPlayerPrompts(MonoBehaviour target)
    {
        PlayerHealth targetHealth = target.GetComponentInParent<PlayerHealth>();
        DownedPlayerCarryable carryable = target.GetComponentInParent<DownedPlayerCarryable>();
        PlayerConcreteTrapController concreteTrap = target.GetComponentInParent<PlayerConcreteTrapController>();
        bool ejectedTrap = concreteTrap != null && concreteTrap.IsEjected;
        if (targetHealth == null || (!targetHealth.IsDowned && !ejectedTrap) ||
            target.transform.root == playerInteraction.transform.root)
        {
            return false;
        }

        if (carryable != null && carryable.CanBeCarriedByHuman)
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

        if (storage is MainStorageNew &&
            heldObject.TryGetComponent(out MountableBridgeComponent _) &&
            !heldObject.TryGetComponent(out BridgeDeckAreaPanel _))
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
        MountingNail mountingNail = currentTarget as MountingNail;
        if (mountingNail == null && currentTarget != null) mountingNail = currentTarget.GetComponentInParent<MountingNail>();
        bool showNailProgress = mountingNail != null && mountingNail.BlocksPickup
            && !(currentCrowbarPresentationNail != null && currentCrowbarPresentationNail == mountingNail);
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
        bool showBucketHoldProgress = playerInteraction.IsBucketActionHoldActive;
        bool showSawProgress = currentSawPresentation && currentSawCutActive;
        bool showProgress = showSawProgress || currentCrowbarPresentationNail == null
            && (showNailProgress || showBucketHoldProgress || showConstructionProgress || showAssemblyProgress);

        if (progressCircleHolder != null)
        {
            progressCircleHolder.SetActive(showProgress);
        }

        if (showProgress && assemblingProgressBar != null)
        {
            if (showSawProgress)
            {
                assemblingProgressBar.fillAmount = currentSawCutProgress;
            }
            else if (showNailProgress)
            {
                assemblingProgressBar.fillAmount = mountingNail.PenetrationProgressNormalized;
            }
            else if (showBucketHoldProgress)
            {
                assemblingProgressBar.fillAmount = playerInteraction.BucketActionHoldProgress;
            }
            else if (showConstructionProgress && constructionSite.RequiredWorkProgress > 0f)
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
        else if (assemblingProgressBar != null)
        {
            assemblingProgressBar.fillAmount = 0f;
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

    private void ShowPlacementStatus()
    {
        ClearPanelPlacementFeedback();
        ResetCrowbarPresentation();
        EvaluatedTarget = null;
        CurrentTargetHasActionablePrompt = false;
        if (progressCircleHolder != null) progressCircleHolder.SetActive(false);
        if (assemblingProgressBar != null) assemblingProgressBar.fillAmount = 0f;
        string status = placementController != null ? placementController.PlacementStatusLabel : "Placement status unavailable";
        string confirmHint = placementController != null ? placementController.PlacementConfirmationHint : "E Placement unavailable";
        string mountingGuidance = placementController != null ? placementController.PanelMountingGuidanceLabel : string.Empty;
        bool waitingForServer = placementController != null && placementController.IsRequestPending;
        if (cachedPlacementHudText == null || cachedPlacementHudStatus != status || cachedPlacementHudConfirmHint != confirmHint
            || cachedPlacementHudMounting != mountingGuidance || cachedPlacementHudPending != waitingForServer)
        {
            cachedPlacementHudStatus = status;
            cachedPlacementHudConfirmHint = confirmHint;
            cachedPlacementHudMounting = mountingGuidance;
            cachedPlacementHudPending = waitingForServer;
            cachedPlacementHudText = waitingForServer
                ? $"{status}\nAwaiting server response\n{confirmHint}"
                : string.IsNullOrEmpty(mountingGuidance)
                    ? $"{status}\nF Toggle | Esc Cancel | Up/Down X | Left/Right Z (1°) | Ctrl + Left/Right Y\n{confirmHint}"
                    : $"{status}\n{mountingGuidance}\n{confirmHint} | F Toggle | Esc Cancel\nUp/Down X | Left/Right Z (1°) | Ctrl + Left/Right Y";
        }
        SetPromptText(cachedPlacementHudText, true);
        Show();
    }

    private void SetPromptText(string text, bool areaLayout)
    {
        if (componentInfoText == null) return;
        CapturePromptLayoutBaseline();
        if (!areaLayout) RestorePromptLayout();
        if (lastRenderedPromptUsesAreaLayout == areaLayout && lastRenderedPromptText == text
            && componentInfoText.text == text) return;
        if (areaLayout && lastRenderedPromptUsesAreaLayout) RestorePromptLayout();

        componentInfoText.text = text;
        if (areaLayout && promptTextRect != null)
        {
            componentInfoText.ForceMeshUpdate();
            float width = promptTextRect.rect.width;
            float preferredHeight = componentInfoText.GetPreferredValues(text, width, 0f).y;
            float originalTextHeight = promptTextRect.rect.height;
            float growth = Mathf.Max(0f, preferredHeight + 8f - originalTextHeight);
            if (promptLayoutRootRect != null && growth > 0f)
                promptLayoutRootRect.sizeDelta = new Vector2(originalPromptRootSize.x, originalPromptRootSize.y + growth);
            componentInfoText.ForceMeshUpdate();
        }
        lastRenderedPromptText = text;
        lastRenderedPromptUsesAreaLayout = areaLayout;
    }

    private void CapturePromptLayoutBaseline()
    {
        if (promptLayoutCaptured || componentInfoText == null) return;
        promptTextRect = componentInfoText.rectTransform;
        promptLayoutRootRect = transform as RectTransform;
        promptVisualRect = visualRoot != null ? visualRoot.GetComponent<RectTransform>() : null;
        if (promptLayoutRootRect != null) originalPromptRootSize = promptLayoutRootRect.sizeDelta;
        if (promptVisualRect != null) originalPromptVisualSize = promptVisualRect.sizeDelta;
        if (promptTextRect != null) originalPromptTextSize = promptTextRect.sizeDelta;
        promptLayoutCaptured = true;
    }

    private void RestorePromptLayout()
    {
        if (!promptLayoutCaptured || !lastRenderedPromptUsesAreaLayout) return;
        if (promptTextRect != null) promptTextRect.sizeDelta = originalPromptTextSize;
        if (promptLayoutRootRect != null) promptLayoutRootRect.sizeDelta = originalPromptRootSize;
        if (promptVisualRect != null) promptVisualRect.sizeDelta = originalPromptVisualSize;
        lastRenderedPromptUsesAreaLayout = false;
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
        ClearPanelPlacementFeedback();
        ResetCrowbarPresentation();
        ResetSawPresentation();
        EvaluatedTarget = null;
        CurrentTargetHasActionablePrompt = false;
        SetPromptText(string.Empty, false);
        if (visualRoot != null)
        {
            visualRoot.SetActive(false);
        }

        if (progressCircleHolder != null)
        {
            progressCircleHolder.SetActive(false);
        }
        if (assemblingProgressBar != null) assemblingProgressBar.fillAmount = 0f;
    }

    private void ResetCrowbarPresentation()
    {
        currentCrowbarPresentationNail = null;
        currentTargetHasCrowbarNailHighlight = false;
        currentCrowbarExtractionAwaitingServer = false;
        currentCrowbarExtractionProgress = 0f;
    }

    private void ResetSawPresentation()
    {
        currentSawPresentation = false;
        currentSawCutActive = false;
        currentSawCutProgress = 0f;
    }

    private void OnDisable() => Hide();

    private void OnDestroy() => ClearPanelPlacementFeedback();

    private void RefreshPanelPlacementFeedback(MonoBehaviour target)
    {
        if (playerInput == null || !playerInput.IsLocalGameplayInputActive || playerInteraction == null)
        {
            ClearPanelPlacementFeedback();
            return;
        }

        BridgeDeckAreaPanel areaPanel = target != null ? target.GetComponentInParent<BridgeDeckAreaPanel>() : null;
        if (areaPanel != null)
        {
            ClearPanelPlacementFeedback();
            return;
        }
        BridgeDeckPanelMounting marker = target != null ? target.GetComponentInParent<BridgeDeckPanelMounting>() : null;
        MountableBridgeComponent panel = null;
        if (marker != null) marker.TryGetComponent(out panel);
        if (marker == null || !marker.IsPhysicalPanel || panel == null || panel.IsPickedUp
            || !BridgeDeckPanelSectionNetworkState.TryGetAssessmentFor(marker, out BridgeDeckPanelReplicatedState state,
                out BridgeDeckPanelConstructionSite slot)
            || state.Complete)
        {
            ClearPanelPlacementFeedback();
            return;
        }

        BridgeDeckPanelSlotOutline nextOutline = slot.GetComponentInChildren<BridgeDeckPanelSlotOutline>(true);
        if (nextOutline == null)
        {
            ClearPanelPlacementFeedback();
            return;
        }
        if (focusedPanelOutline != nextOutline)
        {
            ClearPanelPlacementFeedback();
            focusedPanelOutline = nextOutline;
        }
        focusedPanelOutline.SetPlacementFeedback(this,
            BridgeDeckPanelMounting.IsPlacementCorrect((BridgeDeckPanelPlacementFailure)state.Failure));
    }

    private void ClearPanelPlacementFeedback()
    {
        if (focusedPanelOutline == null) return;
        focusedPanelOutline.ClearPlacementFeedback(this);
        focusedPanelOutline = null;
    }
}
