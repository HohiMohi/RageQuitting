using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Opt-in marker for physical deck panels and their passive placement slots.</summary>
[DisallowMultipleComponent]
public sealed class BridgeDeckPanelMounting : MonoBehaviour, IInteractionPromptProvider
{
    internal bool ResetImpulsePending { get; private set; }
    private static readonly List<BridgeDeckPanelMounting> ActivePanels = new();
    public enum Role
    {
        PhysicalPanel,
        PassiveSlot,
        Stage1TestFixture
    }

    [SerializeField] private Role role = Role.PhysicalPanel;
    private Coroutine resetImpulseCoroutine;

    public bool IsPhysicalPanel => role == Role.PhysicalPanel;
    public bool IsPassiveSlot => role == Role.PassiveSlot;
    public bool IsStage1TestFixture => role == Role.Stage1TestFixture;
    public event System.Action<BridgeDeckPanelMounting> AssessmentChanged;

    private void OnEnable()
    {
        if (!ActivePanels.Contains(this)) ActivePanels.Add(this);
    }

    internal bool TryBeginResetImpulse()
    {
        if (ResetImpulsePending) return false;
        ResetImpulsePending = true;
        return true;
    }

    internal void EndResetImpulse()
    {
        ResetImpulsePending = false;
        resetImpulseCoroutine = null;
    }

    internal void StartResetImpulse(Rigidbody body, Vector3 deltaVelocity, MountableBridgeComponent panel,
        NetworkObject panelNetworkObject, bool networkedPanel, NetworkManager networkManager, ulong panelSpawnId)
    {
        resetImpulseCoroutine = StartCoroutine(ApplyResetImpulseAfterJointRemoval(body, deltaVelocity,
            panel, panelNetworkObject, networkedPanel, networkManager, panelSpawnId));
    }

    private IEnumerator ApplyResetImpulseAfterJointRemoval(Rigidbody body, Vector3 deltaVelocity,
        MountableBridgeComponent panel, NetworkObject panelNetworkObject, bool networkedPanel,
        NetworkManager networkManager, ulong panelSpawnId)
    {
        // Destroy is deferred until after the current Update loop. Do not depend on rendering.
        yield return null;
        bool authorityStillValid = networkedPanel
            ? networkManager != null && networkManager.IsListening && networkManager.IsServer
                && panelNetworkObject != null && panelNetworkObject.IsSpawned
                && panelNetworkObject.NetworkManager == networkManager
                && panelNetworkObject.NetworkObjectId == panelSpawnId
                && panelNetworkObject.OwnerClientId == NetworkManager.ServerClientId
            : NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        if (isActiveAndEnabled && panel != null && panel.isActiveAndEnabled && body != null
            && !body.isKinematic && !panel.IsPickedUp && authorityStillValid)
            body.AddForce(deltaVelocity, ForceMode.VelocityChange);
        EndResetImpulse();
    }

    private void OnDisable()
    {
        if (resetImpulseCoroutine != null) StopCoroutine(resetImpulseCoroutine);
        resetImpulseCoroutine = null;
        ActivePanels.Remove(this);
        ResetImpulsePending = false;
    }

    internal static void CopyActivePanels(UnityEngine.SceneManagement.Scene scene, List<BridgeDeckPanelMounting> results)
    {
        if (results == null) return;
        results.Clear();
        for (int i = ActivePanels.Count - 1; i >= 0; i--)
        {
            BridgeDeckPanelMounting panel = ActivePanels[i];
            if (panel == null) { ActivePanels.RemoveAt(i); continue; }
            if (panel.isActiveAndEnabled && panel.IsPhysicalPanel && panel.gameObject.scene == scene) results.Add(panel);
        }
    }

    internal void RaiseAssessmentChanged() => AssessmentChanged?.Invoke(this);

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (!IsPhysicalPanel || prompts == null) return;
        if (TryGetComponent(out MountableBridgeComponent mountable)
            && mountable.CanBePickedUp && !mountable.IsPickedUp)
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Pick up"));
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
            "Use F / E to position by hand on the marked main-girder slot; place a separate Mounting Nail through the panel into a main girder in each of four quadrants, then strike each nail three times."));
        AddPlacementInformationPrompts(prompts);
    }

    internal void AddPlacementInformationPrompts(List<InteractionPrompt> prompts)
    {
        if (!IsPhysicalPanel || prompts == null) return;
        bool hasAssessment = BridgeDeckPanelSectionNetworkState.TryGetAssessmentFor(this, out BridgeDeckPanelReplicatedState state);
        if (hasAssessment && state.Complete)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Deck panel secured"));
            return;
        }
        if (TryGetComponent(out MountableBridgeComponent carriedPanel) && carriedPanel.IsPickedUp)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Placement: release panel to assess"));
            AddFasteningPrompt(prompts, hasAssessment ? state.QuarterMask : (byte)0);
            return;
        }
        if (!hasAssessment)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Placement: mounting area unavailable"));
            AddFasteningPrompt(prompts, 0);
            return;
        }

        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information,
            IsPlacementCorrect((BridgeDeckPanelPlacementFailure)state.Failure)
                ? "Placement correct"
                : $"Placement: {FailureText((BridgeDeckPanelPlacementFailure)state.Failure)}"));
        AddFasteningPrompt(prompts, state.QuarterMask);
    }

    private static void AddFasteningPrompt(List<InteractionPrompt> prompts, byte mask)
    {
        string text = $"Fastenings: {CountQuarters(mask)}/4";
        string missing = BuildMissingQuarters(mask);
        if (missing.Length > 0) text += $" - missing {missing}";
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, text));
    }

    private static int CountQuarters(byte mask)
    {
        int count = 0;
        for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) count++;
        return count;
    }

    private static string BuildMissingQuarters(byte mask)
    {
        string text = string.Empty;
        AppendMissing(ref text, mask, BridgeDeckPanelQuarterMask.NegativeXNegativeZ, "back-left");
        AppendMissing(ref text, mask, BridgeDeckPanelQuarterMask.PositiveXNegativeZ, "back-right");
        AppendMissing(ref text, mask, BridgeDeckPanelQuarterMask.NegativeXPositiveZ, "front-left");
        AppendMissing(ref text, mask, BridgeDeckPanelQuarterMask.PositiveXPositiveZ, "front-right");
        return text;
    }

    private static void AppendMissing(ref string text, byte mask, BridgeDeckPanelQuarterMask quarter, string name)
    {
        if (((BridgeDeckPanelQuarterMask)mask & quarter) != 0) return;
        if (text.Length > 0) text += ", ";
        text += name;
    }

    private static string FailureText(BridgeDeckPanelPlacementFailure failure) => failure switch
    {
        BridgeDeckPanelPlacementFailure.None => string.Empty,
        BridgeDeckPanelPlacementFailure.MissingQuarters => string.Empty,
        BridgeDeckPanelPlacementFailure.HorizontalOffset => "position out of alignment",
        BridgeDeckPanelPlacementFailure.HeightOffset => "height out of alignment",
        BridgeDeckPanelPlacementFailure.Orientation => "incorrect orientation",
        BridgeDeckPanelPlacementFailure.SlotOccupied => "slot occupied",
        BridgeDeckPanelPlacementFailure.NeighborPenetration => "overlapping adjacent panel",
        _ => "mounting area unavailable"
    };

    internal static bool IsPlacementCorrect(BridgeDeckPanelPlacementFailure failure) =>
        failure == BridgeDeckPanelPlacementFailure.None || failure == BridgeDeckPanelPlacementFailure.MissingQuarters;
}
