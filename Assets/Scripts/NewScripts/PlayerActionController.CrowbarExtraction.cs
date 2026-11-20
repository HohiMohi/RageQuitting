using System;
using Unity.Netcode;
using UnityEngine;

public partial class PlayerActionController
{
    private const float CrowbarAimRefreshInterval = 0.1f;
    private const float CrowbarAimRefreshTimeout = 0.35f;
    private bool crowbarAltHeld;
    private bool crowbarExtractionPending;
    private bool crowbarCommitRequested;
    private uint crowbarAttemptSequence;
    private uint lastServerCrowbarAttempt;
    private uint activeCrowbarAttempt;
    private float nextCrowbarAimRefreshAt;
    private double crowbarServerStartTime;
    private float crowbarServerDuration;
    private ulong expectedCrowbarHandleHigh;
    private ulong expectedCrowbarHandleLow;
    private float crowbarSnapshotSyncDeadline;
    private float nextCrowbarCommitRetryAt;
    private bool crowbarSnapshotConfirmed;
    private MountingNail clientCrowbarNail;
    private MountingNail serverCrowbarNail;
    private MountingNailExtractionHandle serverCrowbarHandle;
    private double serverCrowbarLastRefreshTime;

    private bool IsCrowbarExtractionHeldOrPending => crowbarExtractionPending;

    private bool IsCrowbarSelected()
    {
        EquippableItemSO selected = _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        return selected != null && selected.itemType == EquippableItemType.Crowbar;
    }

    private float GetCrowbarExtractionMovementMultiplier()
    {
        EquippableItemSO selectedItem = _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        EquippableActionProfileSO profile = GetValidCrowbarProfile(selectedItem);
        return profile != null ? Mathf.Clamp01(profile.movementMultiplierDuringAction) : 0.65f;
    }

    private void BeginCrowbarExtractionOnFreshPress()
    {
        if (crowbarAltHeld) return;
        crowbarAltHeld = true;
        if (!HasLocalActionAuthority() || !isActiveAndEnabled || !IsCrowbarSelected()
            || _playerInputNew == null || !_playerInputNew.IsLocalGameplayInputActive
            || IsDowned() || _concreteTrap != null && _concreteTrap.BlocksGameplayInput
            || performAction || IsActionInProgress || crowbarExtractionPending)
            return;

        if (!TryGetLocalCrowbarAim(out MountingNail nail, out Ray ray)) return;
        NetworkObject nailObject = nail.GetComponent<NetworkObject>();
        bool networkSession = IsOwnNetworkSessionActive();
        if (networkSession && (nailObject == null || !nailObject.IsSpawned)) return;
        // A remote owner may still be looking at a previous replicated reservation after a
        // reliable cancel. Let the server serialize cancel/begin and decide availability.
        if ((!networkSession || IsServer) && !nail.IsCrowbarExtractionAvailable) return;

        activeCrowbarAttempt = NextCrowbarAttemptSequence();
        crowbarExtractionPending = true;
        crowbarCommitRequested = false;
        clientCrowbarNail = nail;
        nextCrowbarAimRefreshAt = Time.unscaledTime + CrowbarAimRefreshInterval;

        if (!IsOwnNetworkSessionActive())
        {
            EquippableActionProfileSO profile = GetValidCrowbarProfile(_inventory.GetCurrentSelectedItem());
            if (profile == null || !nail.TryBeginCrowbarExtraction(this, profile.crowbarExtractionDuration,
                    out MountingNailExtractionHandle handle))
            {
                ClearLocalCrowbarAttempt();
                return;
            }
            offlineCrowbarHandle = handle;
            crowbarServerDuration = profile.crowbarExtractionDuration;
            crowbarServerStartTime = Time.timeAsDouble;
            return;
        }

        if (IsServer)
            ProcessCrowbarExtractionBeginServer(OwnerClientId, activeCrowbarAttempt, nailObject.NetworkObjectId,
                ray.origin, ray.direction);
        else
            RequestCrowbarExtractionBeginServerRpc(activeCrowbarAttempt, nailObject.NetworkObjectId,
                ray.origin, ray.direction);
    }

    private MountingNailExtractionHandle offlineCrowbarHandle;

    private uint NextCrowbarAttemptSequence()
    {
        uint next = unchecked(++crowbarAttemptSequence);
        if (next == 0) next = unchecked(++crowbarAttemptSequence);
        return next;
    }

    private static EquippableActionProfileSO GetValidCrowbarProfile(EquippableItemSO item)
    {
        if (item == null || item.itemType != EquippableItemType.Crowbar || item.actionProfile == null)
            return null;
        float duration = item.actionProfile.crowbarExtractionDuration;
        return IsFinite(duration) && duration > 0f
            ? item.actionProfile : null;
    }

    private bool TryGetLocalCrowbarAim(out MountingNail nail, out Ray ray)
    {
        nail = null;
        ray = default;
        EquippableItemSO item = _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        return item != null && item.itemType == EquippableItemType.Crowbar
            && TryGetCrowbarExtractionAimRay(out ray)
            && MountingNailAimResolver.TryFindCandidate(ray, item.actionRange, transform,
                out nail, out _, out _)
            && nail != null;
    }

    private bool TryGetCrowbarExtractionAimRay(out Ray ray)
    {
        ray = default;
        if (_playerInteraction == null
            || !_playerInteraction.TryGetAimRay(out Ray viewportRay, out Camera sourceCamera, out _))
            return false;

        // ViewportPointToRay starts at the near plane. Send the physical camera origin instead,
        // retaining the exact center-screen direction so the trusted server origin tracks the
        // NetworkTransform aim anchor without consuming the forward-offset allowance.
        Vector3 origin = sourceCamera != null ? sourceCamera.transform.position : viewportRay.origin;
        ray = new Ray(origin, viewportRay.direction);
        return IsFinite(ray.origin) && IsValidCrowbarAimDirection(ray.direction);
    }

    /// <summary>Returns presentation data only for a nail currently selected by this local Crowbar aim.</summary>
    public bool TryGetCrowbarNailPresentation(MonoBehaviour target,
        out MountingNail nail, out MountingNailExtractionAvailability availability,
        out float ownProgressNormalized, out bool ownAcceptedAttempt, out bool awaitingServer,
        out bool withinExtractionRange)
    {
        nail = null;
        availability = MountingNailExtractionAvailability.Unavailable;
        ownProgressNormalized = 0f;
        ownAcceptedAttempt = false;
        awaitingServer = false;
        withinExtractionRange = false;
        if (target == null || !HasLocalActionAuthority() || !isActiveAndEnabled || !IsCrowbarSelected()
            || _playerInputNew == null || !_playerInputNew.IsLocalGameplayInputActive
            || IsDowned() || _concreteTrap != null && _concreteTrap.BlocksGameplayInput)
            return false;

        MountingNail targetNail = target as MountingNail;
        if (targetNail == null) targetNail = target.GetComponentInParent<MountingNail>();
        EquippableItemSO selected = _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        if (targetNail == null || selected == null || selected.actionProfile == null
            || !TryGetCrowbarExtractionAimRay(out Ray aimRay)
            || !MountingNailAimResolver.TryFindCandidate(aimRay,
                _playerInteraction.SingleCarryDropSearchDistance, transform,
                out MountingNail aimedNail, out _, out Vector3 aimPoint)
            || aimedNail != targetNail)
            return false;

        nail = targetNail;
        float alongAimRay = Vector3.Dot(aimPoint - aimRay.origin, aimRay.direction.normalized);
        withinExtractionRange = alongAimRay <= selected.actionRange;
        MountingNailExtractionReadState state = nail.CrowbarExtractionReadState;
        availability = state.Availability;
        if (IsOwnNetworkSessionActive()
            && state.Availability == MountingNailExtractionAvailability.Reserved
            && state.ExecutorNetworkObjectId == NetworkObjectId
            && state.ExecutorOwnerClientId == OwnerClientId
            && (!crowbarExtractionPending || clientCrowbarNail != nail
                || !MountingNailExtractionHandleMatches(state)))
        {
            awaitingServer = true;
            return true;
        }
        if (!crowbarExtractionPending || clientCrowbarNail != nail) return true;

        if (!IsOwnNetworkSessionActive())
        {
            if (offlineCrowbarHandle.IsValid && state.Availability == MountingNailExtractionAvailability.Reserved
                && state.OfflineExecutor == this && state.Handle == offlineCrowbarHandle)
            {
                ownAcceptedAttempt = true;
                ownProgressNormalized = state.ProgressNormalized;
            }
            else
                awaitingServer = true;
            return true;
        }

        if (state.Availability == MountingNailExtractionAvailability.Reserved
            && state.ExecutorNetworkObjectId == NetworkObjectId
            && state.ExecutorOwnerClientId == OwnerClientId
            && !MountingNailExtractionHandleMatches(state))
        {
            awaitingServer = true;
            return true;
        }

        if (crowbarServerDuration <= 0f || !MountingNailExtractionHandleMatches(state))
        {
            awaitingServer = crowbarServerDuration <= 0f
                || !crowbarSnapshotConfirmed && Time.unscaledTime <= crowbarSnapshotSyncDeadline;
            if (!awaitingServer && state.Availability == MountingNailExtractionAvailability.Reserved)
                availability = MountingNailExtractionAvailability.Reserved;
            return true;
        }

        ownAcceptedAttempt = true;
        ownProgressNormalized = state.ProgressNormalized;
        return true;
    }

    private bool MountingNailExtractionHandleMatches(MountingNailExtractionReadState state)
    {
        return state.Availability == MountingNailExtractionAvailability.Reserved
            && state.Handle.High == expectedCrowbarHandleHigh && state.Handle.Low == expectedCrowbarHandleLow
            && state.ExecutorNetworkObjectId == NetworkObjectId
            && state.ExecutorOwnerClientId == OwnerClientId;
    }

    private void HandleActionAltCanceled(object sender, EventArgs e)
    {
        crowbarAltHeld = false;
        if (crowbarExtractionPending) CancelCrowbarExtractionImmediately();
    }

    protected void Update()
    {
        if (!crowbarExtractionPending) return;

        if (IsServer && serverCrowbarHandle.IsValid)
        {
            if (!isActiveAndEnabled || !IsSpawned || !IsServer || NetworkManager == null
                || !NetworkManager.IsListening
                || OwnerClientId != NetworkManager.ServerClientId
                    && !NetworkManager.ConnectedClients.ContainsKey(OwnerClientId)
                || IsDowned() || _concreteTrap != null && _concreteTrap.BlocksGameplayInput
                || serverCrowbarNail == null
                || !serverCrowbarNail.isActiveAndEnabled || !serverCrowbarNail.gameObject.activeInHierarchy
                || !IsServerCrowbarReservationCurrent()
                || GetValidCrowbarProfile(_inventory != null ? _inventory.GetSelectedItemForServerValidation() : null) == null
                || NetworkManager.ServerTime.Time - serverCrowbarLastRefreshTime > CrowbarAimRefreshTimeout)
                CancelServerCrowbarAttempt(activeCrowbarAttempt);
        }

        if (!HasLocalActionAuthority())
        {
            if (!IsServer) ClearLocalCrowbarAttempt();
            return;
        }
        if (!crowbarAltHeld || !isActiveAndEnabled || !IsCrowbarSelected()
            || _playerInputNew == null || !_playerInputNew.IsLocalGameplayInputActive
            || IsDowned() || _concreteTrap != null && _concreteTrap.BlocksGameplayInput
            || clientCrowbarNail == null || !clientCrowbarNail.isActiveAndEnabled
            || !TryGetLocalCrowbarAim(out MountingNail currentNail, out Ray aimRay)
            || currentNail != clientCrowbarNail
            || crowbarServerDuration > 0f && !IsLocalCrowbarReservationCurrent())
        {
            CancelCrowbarExtractionImmediately();
            return;
        }

        if (IsOwnNetworkSessionActive())
        {
            if (Time.unscaledTime >= nextCrowbarAimRefreshAt)
            {
                nextCrowbarAimRefreshAt = Time.unscaledTime + CrowbarAimRefreshInterval;
                NetworkObject nailObject = clientCrowbarNail.GetComponent<NetworkObject>();
                if (nailObject != null && nailObject.IsSpawned)
                    RefreshCrowbarExtractionServerRpc(activeCrowbarAttempt, nailObject.NetworkObjectId,
                        aimRay.origin, aimRay.direction);
                else
                {
                    CancelCrowbarExtractionImmediately();
                    return;
                }
            }
        }

        if (crowbarCommitRequested || Time.unscaledTime < nextCrowbarCommitRetryAt || crowbarServerDuration <= 0f) return;
        double now = IsOwnNetworkSessionActive() && NetworkManager != null
            ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        if (now - crowbarServerStartTime < crowbarServerDuration) return;
        crowbarCommitRequested = true;
        NetworkObject targetObject = clientCrowbarNail.GetComponent<NetworkObject>();
        if (!IsOwnNetworkSessionActive())
        {
            bool committed = clientCrowbarNail.TryCommitCrowbarExtraction(this, offlineCrowbarHandle);
            ClearLocalCrowbarAttempt();
        }
        else if (targetObject != null && targetObject.IsSpawned)
        {
            RequestCrowbarExtractionCommitServerRpc(activeCrowbarAttempt, targetObject.NetworkObjectId,
                aimRay.origin, aimRay.direction);
        }
        else CancelCrowbarExtractionImmediately();
    }

    private void CancelCrowbarExtractionImmediately()
    {
        uint attempt = activeCrowbarAttempt;
        if (IsOwnNetworkSessionActive() && attempt != 0)
        {
            if (IsServer) CancelServerCrowbarAttempt(attempt);
            else CancelCrowbarExtractionServerRpc(attempt);
        }
        else if (offlineCrowbarHandle.IsValid && clientCrowbarNail != null)
        {
            clientCrowbarNail.CancelCrowbarExtraction(this, offlineCrowbarHandle);
        }
        ClearLocalCrowbarAttempt();
    }

    private bool IsLocalCrowbarReservationCurrent()
    {
        if (clientCrowbarNail == null) return false;
        MountingNailExtractionReadState state = clientCrowbarNail.CrowbarExtractionReadState;
        if (IsOwnNetworkSessionActive())
        {
            bool matches = state.Availability == MountingNailExtractionAvailability.Reserved
                && state.Handle.High == expectedCrowbarHandleHigh && state.Handle.Low == expectedCrowbarHandleLow
                && state.ExecutorNetworkObjectId == NetworkObjectId
                && state.ExecutorOwnerClientId == OwnerClientId;
            if (matches)
            {
                crowbarSnapshotConfirmed = true;
                return true;
            }
            return !crowbarSnapshotConfirmed && Time.unscaledTime <= crowbarSnapshotSyncDeadline;
        }
        return state.Availability == MountingNailExtractionAvailability.Reserved
            && state.OfflineExecutor == this && state.Handle == offlineCrowbarHandle;
    }

    private void ClearLocalCrowbarAttempt()
    {
        crowbarExtractionPending = false;
        crowbarCommitRequested = false;
        crowbarServerStartTime = 0d;
        crowbarServerDuration = 0f;
        clientCrowbarNail = null;
        offlineCrowbarHandle = default;
        expectedCrowbarHandleHigh = 0;
        expectedCrowbarHandleLow = 0;
        crowbarSnapshotSyncDeadline = 0f;
        nextCrowbarCommitRetryAt = 0f;
        crowbarSnapshotConfirmed = false;
        activeCrowbarAttempt = 0;
    }

    private void ResetCrowbarExtractionNetworkState()
    {
        serverCrowbarNail = null;
        serverCrowbarHandle = default;
        serverCrowbarLastRefreshTime = 0d;
        lastServerCrowbarAttempt = 0;
        crowbarAltHeld = false;
        ClearLocalCrowbarAttempt();
    }

    private void HandleCrowbarExtractionNetworkDespawn()
    {
        if (IsServer && serverCrowbarHandle.IsValid)
            CancelServerCrowbarAttempt(activeCrowbarAttempt);
        ClearLocalCrowbarAttempt();
        crowbarAltHeld = false;
    }

    private static bool IsValidCrowbarAimDirection(Vector3 direction)
    {
        float length = direction.magnitude;
        return IsFinite(direction) && length >= 0.95f && length <= 1.05f;
    }

    private bool ValidateServerCrowbarAim(MountingNail expectedNail, Vector3 origin, Vector3 direction)
    {
        NetworkManager manager = NetworkManager;
        if (manager == null || !manager.IsServer || !manager.IsListening || !IsSpawned || !isActiveAndEnabled
            || !gameObject.activeInHierarchy || expectedNail == null || !expectedNail.isActiveAndEnabled
            || _inventory == null || _playerInteraction == null || _concreteTrap != null && _concreteTrap.IsTrapped
            || IsDowned() || !IsFinite(origin) || !IsValidCrowbarAimDirection(direction)
            || !IsTrustedMountingNailAimOrigin(origin, direction.normalized)) return false;
        EquippableItemSO selected = _inventory.GetSelectedItemForServerValidation();
        EquippableActionProfileSO profile = GetValidCrowbarProfile(selected);
        if (profile == null) return false;
        NetworkObject nailObject = expectedNail.GetComponent<NetworkObject>();
        if (nailObject == null || !nailObject.IsSpawned || nailObject.NetworkManager != manager) return false;
        Ray ray = new(origin, direction.normalized);
        return MountingNailAimResolver.TryFindCandidate(ray, selected.actionRange, transform,
                   out MountingNail aimedNail, out _, out _) && aimedNail == expectedNail;
    }

    private bool IsServerCrowbarReservationCurrent()
    {
        if (serverCrowbarNail == null || !serverCrowbarHandle.IsValid) return false;
        MountingNailExtractionReadState state = serverCrowbarNail.CrowbarExtractionReadState;
        return state.Availability == MountingNailExtractionAvailability.Reserved
            && state.Handle == serverCrowbarHandle
            && state.ExecutorNetworkObjectId == NetworkObjectId
            && state.ExecutorOwnerClientId == OwnerClientId;
    }

    [ServerRpc]
    private void RequestCrowbarExtractionBeginServerRpc(uint attempt, ulong nailId, Vector3 aimOrigin,
        Vector3 aimDirection, ServerRpcParams rpcParams = default)
    {
        ProcessCrowbarExtractionBeginServer(rpcParams.Receive.SenderClientId, attempt, nailId, aimOrigin, aimDirection);
    }

    private void ProcessCrowbarExtractionBeginServer(ulong sender, uint attempt, ulong nailId,
        Vector3 aimOrigin, Vector3 aimDirection)
    {
        NetworkManager manager = NetworkManager;
        if (manager == null || !manager.IsServer || !manager.IsListening || !IsSpawned
            || sender != OwnerClientId || attempt == 0
            || !IsNewerRequestSequence(attempt, lastServerCrowbarAttempt)) return;
        lastServerCrowbarAttempt = attempt;
        if (serverCrowbarNail != null || _inventory == null || _playerInputNew == null || IsDowned()
            || !isActiveAndEnabled || !gameObject.activeInHierarchy || _concreteTrap != null && _concreteTrap.IsTrapped)
        {
            NotifyCrowbarExtractionBeginClientRpc(attempt, false, 0d, 0f, 0, 0, TargetOwnerClientRpcParams());
            return;
        }
        if (!manager.SpawnManager.SpawnedObjects.TryGetValue(nailId, out NetworkObject nailObject)
            || nailObject == null || !nailObject.TryGetComponent(out MountingNail nail)
            || !ValidateServerCrowbarAim(nail, aimOrigin, aimDirection))
        {
            NotifyCrowbarExtractionBeginClientRpc(attempt, false, 0d, 0f, 0, 0, TargetOwnerClientRpcParams());
            return;
        }
        EquippableActionProfileSO profile = GetValidCrowbarProfile(_inventory.GetSelectedItemForServerValidation());
        if (profile == null || !nail.TryBeginCrowbarExtraction(this, profile.crowbarExtractionDuration,
                out MountingNailExtractionHandle handle))
        {
            NotifyCrowbarExtractionBeginClientRpc(attempt, false, 0d, 0f, 0, 0, TargetOwnerClientRpcParams());
            return;
        }

        activeCrowbarAttempt = attempt;
        serverCrowbarNail = nail;
        serverCrowbarHandle = handle;
        serverCrowbarLastRefreshTime = manager.ServerTime.Time;
        crowbarExtractionPending = true;
        crowbarAltHeld = true;
        crowbarServerStartTime = serverCrowbarLastRefreshTime;
        crowbarServerDuration = profile.crowbarExtractionDuration;
        NotifyCrowbarExtractionBeginClientRpc(attempt, true, crowbarServerStartTime,
            crowbarServerDuration, handle.High, handle.Low, TargetOwnerClientRpcParams());
    }

    [ClientRpc]
    private void NotifyCrowbarExtractionBeginClientRpc(uint attempt, bool accepted, double startedAt,
        float duration, ulong handleHigh, ulong handleLow, ClientRpcParams rpcParams = default)
    {
        if (attempt != activeCrowbarAttempt || !crowbarExtractionPending) return;
        if (!accepted || !crowbarAltHeld || !TryGetLocalCrowbarAim(out MountingNail aimed, out _)
            || aimed != clientCrowbarNail)
        {
            CancelCrowbarExtractionImmediately();
            return;
        }
        crowbarServerStartTime = startedAt;
        crowbarServerDuration = duration;
        expectedCrowbarHandleHigh = handleHigh;
        expectedCrowbarHandleLow = handleLow;
        crowbarSnapshotSyncDeadline = Time.unscaledTime + 0.5f;
        crowbarSnapshotConfirmed = false;
    }

    [ServerRpc]
    private void RefreshCrowbarExtractionServerRpc(uint attempt, ulong nailId, Vector3 aimOrigin,
        Vector3 aimDirection, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || attempt != activeCrowbarAttempt
            || serverCrowbarNail == null || serverCrowbarNail.NetworkObjectId != nailId
            || !ValidateServerCrowbarAim(serverCrowbarNail, aimOrigin, aimDirection))
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId) CancelServerCrowbarAttempt(attempt);
            return;
        }
        serverCrowbarLastRefreshTime = NetworkManager.ServerTime.Time;
    }

    [ServerRpc]
    private void CancelCrowbarExtractionServerRpc(uint attempt, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || attempt == 0) return;
        if (IsNewerRequestSequence(attempt, lastServerCrowbarAttempt))
            lastServerCrowbarAttempt = attempt;
        CancelServerCrowbarAttempt(attempt);
    }

    private void CancelServerCrowbarAttempt(uint attempt)
    {
        if (attempt == 0 || attempt != activeCrowbarAttempt) return;
        if (serverCrowbarNail != null && serverCrowbarHandle.IsValid)
            serverCrowbarNail.CancelCrowbarExtraction(this, serverCrowbarHandle);
        serverCrowbarNail = null;
        serverCrowbarHandle = default;
        if (IsSpawned && NetworkManager != null && NetworkManager.IsListening
            && (OwnerClientId == NetworkManager.ServerClientId || NetworkManager.ConnectedClients.ContainsKey(OwnerClientId)))
            NotifyCrowbarExtractionCanceledClientRpc(attempt, TargetOwnerClientRpcParams());
        ClearLocalCrowbarAttempt();
    }

    [ClientRpc]
    private void NotifyCrowbarExtractionCanceledClientRpc(uint attempt, ClientRpcParams rpcParams = default)
    {
        if (attempt == activeCrowbarAttempt) ClearLocalCrowbarAttempt();
    }

    [ServerRpc]
    private void RequestCrowbarExtractionCommitServerRpc(uint attempt, ulong nailId,
        Vector3 aimOrigin, Vector3 aimDirection, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || attempt == 0
            || attempt != activeCrowbarAttempt) return;
        if (serverCrowbarNail == null)
        {
            CancelServerCrowbarAttempt(attempt);
            return;
        }
        if (serverCrowbarNail.NetworkObjectId != nailId
            || NetworkManager.ServerTime.Time - serverCrowbarLastRefreshTime > CrowbarAimRefreshTimeout
            || !ValidateServerCrowbarAim(serverCrowbarNail, aimOrigin, aimDirection)
            || !IsServerCrowbarReservationCurrent())
        {
            CancelServerCrowbarAttempt(attempt);
            return;
        }

        MountingNailExtractionReadState extractionState = serverCrowbarNail.CrowbarExtractionReadState;
        double deadline = extractionState.DeadlineServerTime;
        if (NetworkManager.ServerTime.Time < deadline)
        {
            NotifyCrowbarExtractionCommitDeferredClientRpc(attempt, deadline, TargetOwnerClientRpcParams());
            return;
        }

        MountingNail nail = serverCrowbarNail;
        MountingNailExtractionHandle handle = serverCrowbarHandle;
        bool committed = nail.TryCommitCrowbarExtraction(this, handle);
        if (!committed) nail.CancelCrowbarExtraction(this, handle);
        serverCrowbarNail = null;
        serverCrowbarHandle = default;
        if (IsSpawned && NetworkManager != null && NetworkManager.IsListening
            && (OwnerClientId == NetworkManager.ServerClientId || NetworkManager.ConnectedClients.ContainsKey(OwnerClientId)))
            NotifyCrowbarExtractionCommittedClientRpc(attempt, committed, TargetOwnerClientRpcParams());
        ClearLocalCrowbarAttempt();
    }

    [ClientRpc]
    private void NotifyCrowbarExtractionCommittedClientRpc(uint attempt, bool committed,
        ClientRpcParams rpcParams = default)
    {
        if (attempt != activeCrowbarAttempt) return;
        ClearLocalCrowbarAttempt();
    }

    [ClientRpc]
    private void NotifyCrowbarExtractionCommitDeferredClientRpc(uint attempt, double retryAtServerTime,
        ClientRpcParams rpcParams = default)
    {
        if (attempt != activeCrowbarAttempt || !crowbarExtractionPending) return;
        crowbarServerStartTime = retryAtServerTime - crowbarServerDuration;
        crowbarCommitRequested = false;
        nextCrowbarCommitRetryAt = Time.unscaledTime + CrowbarAimRefreshInterval;
    }

    private ClientRpcParams TargetOwnerClientRpcParams() => new()
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
    };
}
