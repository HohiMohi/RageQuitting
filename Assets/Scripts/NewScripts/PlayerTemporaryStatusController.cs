using System;
using Unity.Netcode;
using UnityEngine;

public enum PlayerTemporaryStatusKind : byte
{
    None,
    PlacementLock,
    CrushedHands
}

public struct PlayerTemporaryStatusSnapshot : INetworkSerializable, IEquatable<PlayerTemporaryStatusSnapshot>
{
    public ulong Revision;
    public PlayerTemporaryStatusKind Status;
    public double ExpiresAt;

    public PlayerTemporaryStatusSnapshot(ulong revision, PlayerTemporaryStatusKind status, double expiresAt)
    {
        Revision = revision;
        Status = status;
        ExpiresAt = expiresAt;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Revision);
        serializer.SerializeValue(ref Status);
        serializer.SerializeValue(ref ExpiresAt);
    }

    public bool Equals(PlayerTemporaryStatusSnapshot other) => Revision == other.Revision
        && Status == other.Status && ExpiresAt.Equals(other.ExpiresAt);
}

/// <summary>Owns the authoritative, timed control and action statuses for one player.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerTemporaryStatusController : NetworkBehaviour
{
    [SerializeField, Min(0f)] private float placementLockDurationSeconds = 1f;
    [SerializeField, Min(0f)] private float crushedHandsDurationSeconds = 15f;

    private readonly NetworkVariable<PlayerTemporaryStatusSnapshot> replicatedStatus = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> tumbleweedNetworkId = new(ulong.MaxValue);
    private readonly NetworkVariable<double> tumbleweedReleaseAt = new(0d);
    private readonly NetworkVariable<int> tumbleweedSlot = new(-1);
    private readonly NetworkVariable<Vector3> tumbleweedOffset = new(Vector3.zero);
    private readonly NetworkVariable<Quaternion> tumbleweedRelativeRotation = new(Quaternion.identity);
    private double tumbleweedImmuneUntil;
    private PlayerTransportCollisionController transportCollision;
    private bool transportSuppressed;
    private PlayerNetworkSetup networkSetup;
    private bool releasePosePending;
    private Vector3 pendingReleasePosition;
    private Quaternion pendingReleaseRotation;
    private Vector3 pendingReleaseVelocity;

    private PlayerTemporaryStatusSnapshot offlineStatus;
    private PlayerTemporaryStatusSnapshot appliedStatus;
    private ulong highestAppliedRevision;
    private bool subscribed;

    public event Action<PlayerTemporaryStatusSnapshot> StatusChanged;

    public PlayerTemporaryStatusKind CurrentStatus
    {
        get
        {
            PlayerTemporaryStatusSnapshot snapshot = CurrentSnapshot;
            return IsSnapshotActive(snapshot) ? snapshot.Status : PlayerTemporaryStatusKind.None;
        }
    }

    public bool BlocksControls => CurrentStatus == PlayerTemporaryStatusKind.PlacementLock || IsTumbleweedCaptured;
    public bool BlocksActions => CurrentStatus != PlayerTemporaryStatusKind.None || IsTumbleweedCaptured;
    public bool IsCrushedHands => CurrentStatus == PlayerTemporaryStatusKind.CrushedHands;
    public bool IsTumbleweedCaptured => IsNetworkStateActive ? tumbleweedNetworkId.Value != ulong.MaxValue : offlineTumbleweedCaptured;
    private bool offlineTumbleweedCaptured;
    private double offlineTumbleweedReleaseAt;
    private ulong offlineTumbleweedNetworkId = ulong.MaxValue;
    private Vector3 offlineTumbleweedOffset;
    private int offlineTumbleweedSlot = -1;
    private Quaternion offlineTumbleweedRelativeRotation = Quaternion.identity;
    public double TumbleweedReleaseAt => IsNetworkStateActive ? tumbleweedReleaseAt.Value : offlineTumbleweedReleaseAt;
    public int TumbleweedSlot => IsNetworkStateActive ? tumbleweedSlot.Value : offlineTumbleweedSlot;
    public bool IsTumbleweedImmune => tumbleweedImmuneUntil > GetCurrentTime();
    public ulong CurrentRevision => CurrentSnapshot.Revision;
    public float RemainingSeconds => GetRemainingSeconds(CurrentSnapshot);
    public float PlacementLockDurationSeconds => NormalizeDuration(placementLockDurationSeconds);
    public float CrushedHandsDurationSeconds => NormalizeDuration(crushedHandsDurationSeconds);

    private bool IsNetworkStateActive => IsSpawned && NetworkManager != null && NetworkManager.IsListening;
    private PlayerTemporaryStatusSnapshot CurrentSnapshot => IsNetworkStateActive
        ? appliedStatus : offlineStatus;

    public override void OnNetworkSpawn()
    {
        replicatedStatus.OnValueChanged += ReplicatedStatusChanged;
        subscribed = true;
        if (IsServer)
        {
            highestAppliedRevision = replicatedStatus.Value.Revision;
            WriteStatus(PlayerTemporaryStatusKind.None, 0f);
        }
        else
        {
            ApplySnapshot(replicatedStatus.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (subscribed)
        {
            replicatedStatus.OnValueChanged -= ReplicatedStatusChanged;
            subscribed = false;
        }

        if (IsServer && IsTumbleweedCaptured) EndTumbleweedCapture(0f);
        tumbleweedImmuneUntil = 0d;

        ClearLocalLifecycleState();
    }

    public override void OnDestroy()
    {
        if (subscribed)
        {
            replicatedStatus.OnValueChanged -= ReplicatedStatusChanged;
            subscribed = false;
        }
        StatusChanged = null;
        base.OnDestroy();
    }

    private void Update()
    {
        SyncCaptureCollisionState();
        if (!IsNetworkStateActive)
        {
            FollowTumbleweed(offlineTumbleweedNetworkId, offlineTumbleweedOffset, offlineTumbleweedRelativeRotation);
            if (offlineStatus.Status != PlayerTemporaryStatusKind.None
                && offlineStatus.ExpiresAt <= GetCurrentTime())
                WriteStatus(PlayerTemporaryStatusKind.None, 0f);
            return;
        }

        if (IsTumbleweedCaptured) FollowTumbleweed(tumbleweedNetworkId.Value, tumbleweedOffset.Value, tumbleweedRelativeRotation.Value);
        if (releasePosePending && !IsTumbleweedCaptured) ApplyPendingReleasePose();

        if (IsServer && replicatedStatus.Value.Status != PlayerTemporaryStatusKind.None
            && replicatedStatus.Value.ExpiresAt <= GetCurrentTime())
            WriteStatus(PlayerTemporaryStatusKind.None, 0f);
    }

    /// <summary>Starts or refreshes the short lock after an accepted placement.</summary>
    public bool BeginPlacementLock()
    {
        if (!HasAuthority() || IsTumbleweedCaptured) return false;

        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        if (IsSnapshotActive(current) && current.Status == PlayerTemporaryStatusKind.CrushedHands)
            return false;

        WriteStatus(PlayerTemporaryStatusKind.PlacementLock, PlacementLockDurationSeconds);
        return true;
    }

    public bool BeginTumbleweedCapture(ulong weedNetworkId, int slot, float durationSeconds, Vector3 offset, Quaternion relativeRotation)
    {
        if (!HasAuthority() || IsTumbleweedCaptured || IsTumbleweedImmune) return false;
        double releaseAt = GetCurrentTime() + Mathf.Max(0.1f, durationSeconds);
        if (IsNetworkStateActive) { tumbleweedNetworkId.Value = weedNetworkId; tumbleweedSlot.Value = slot; tumbleweedReleaseAt.Value = releaseAt; tumbleweedOffset.Value = offset; tumbleweedRelativeRotation.Value = relativeRotation; }
        else { offlineTumbleweedCaptured = true; offlineTumbleweedNetworkId = weedNetworkId; offlineTumbleweedSlot = slot; offlineTumbleweedReleaseAt = releaseAt; offlineTumbleweedOffset = offset; offlineTumbleweedRelativeRotation = relativeRotation; }
        if (IsNetworkStateActive)
        {
            BaseResourceNew.ReleaseHeldObjectForTemporaryStatus(NetworkManager, OwnerClientId);
            MountableBridgeComponent.ReleaseHeldObjectForTemporaryStatus(NetworkManager, OwnerClientId);
            DownedPlayerCarryable.ReleaseCarriedPlayerForTemporaryStatus(NetworkManager, OwnerClientId);
        }
        else if (TryGetComponent(out PlayerInteractionNew interaction)) interaction.DropHeldObjectForStateChange();
        return true;
    }

    public bool EndTumbleweedCapture(float immunitySeconds = 5f)
    {
        if (!HasAuthority() || !IsTumbleweedCaptured) return false;
        if (IsNetworkStateActive) { tumbleweedNetworkId.Value = ulong.MaxValue; tumbleweedSlot.Value = -1; tumbleweedReleaseAt.Value = 0d; }
        else { offlineTumbleweedCaptured = false; offlineTumbleweedNetworkId = ulong.MaxValue; offlineTumbleweedSlot = -1; offlineTumbleweedReleaseAt = 0d; }
        tumbleweedImmuneUntil = GetCurrentTime() + Mathf.Max(0f, immunitySeconds);
        return true;
    }

    public void ApplyTumbleweedReleasePose(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        if (IsNetworkStateActive)
        {
            if (!IsServer) return;
            if (IsOwner) QueueTumbleweedReleasePose(position, rotation, velocity);
            else ApplyTumbleweedReleasePoseRpc(position, rotation, velocity);
            return;
        }
        QueueTumbleweedReleasePose(position, rotation, velocity);
    }

    [Rpc(SendTo.Owner)]
    private void ApplyTumbleweedReleasePoseRpc(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        QueueTumbleweedReleasePose(position, rotation, velocity);
    }

    private void QueueTumbleweedReleasePose(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        pendingReleasePosition = position;
        pendingReleaseRotation = rotation;
        pendingReleaseVelocity = velocity;
        releasePosePending = true;
        if (!IsTumbleweedCaptured) ApplyPendingReleasePose();
    }

    private void ApplyPendingReleasePose()
    {
        releasePosePending = false;
        ApplyTumbleweedReleasePoseLocal(pendingReleasePosition, pendingReleaseRotation, pendingReleaseVelocity);
    }

    private void ApplyTumbleweedReleasePoseLocal(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        transform.SetPositionAndRotation(position, rotation);
        if (TryGetComponent(out StarterAssets.FirstPersonController firstPerson))
        {
            firstPerson.ResetMovementAfterForcedPlacement();
            firstPerson.ResetLookRotationState();
            firstPerson.ApplyRopeReleaseVelocity(velocity);
        }
    }

    private void SyncCaptureCollisionState()
    {
        if (networkSetup == null) TryGetComponent(out networkSetup);
        if (networkSetup != null)
        {
            networkSetup.SetTumbleweedCameraSuppressed(IsTumbleweedCaptured && (!IsNetworkStateActive || IsOwner));
        }
        if (transportCollision == null) TryGetComponent(out transportCollision);
        if (transportCollision == null) return;
        if (IsTumbleweedCaptured)
        {
            transportCollision.BeginTransport(this);
            transportSuppressed = true;
        }
        else if (transportSuppressed)
        {
            transportCollision.EndTransport(this);
            transportSuppressed = false;
        }
    }

    private void FollowTumbleweed(ulong id, Vector3 offset, Quaternion relativeRotation)
    {
        if (id == ulong.MaxValue || NetworkManager == null || NetworkManager.SpawnManager == null
            || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject weed)) return;
        transform.SetPositionAndRotation(weed.transform.position + weed.transform.rotation * offset,
            weed.transform.rotation * relativeRotation);
    }

    /// <summary>Applies the injury once, and releases the player's inventory at its onset.</summary>
    internal bool TryApplyCrushedHands(ulong expectedPlacementRevision)
    {
        if (!HasAuthority() || IsTumbleweedCaptured) return false;

        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        if (expectedPlacementRevision == 0 || current.Revision != expectedPlacementRevision
            || current.Status != PlayerTemporaryStatusKind.PlacementLock
            || current.ExpiresAt <= GetCurrentTime())
            return false;

        WriteStatus(PlayerTemporaryStatusKind.CrushedHands, CrushedHandsDurationSeconds);
        if (TryGetComponent(out PlayerInventory inventory)) inventory.DropAllInventoryForAuthority();
        if (IsNetworkStateActive)
        {
            BaseResourceNew.ReleaseHeldObjectForTemporaryStatus(NetworkManager, OwnerClientId);
            MountableBridgeComponent.ReleaseHeldObjectForTemporaryStatus(NetworkManager, OwnerClientId);
            DownedPlayerCarryable.ReleaseCarriedPlayerForTemporaryStatus(NetworkManager, OwnerClientId);
        }
        else if (TryGetComponent(out PlayerInteractionNew interaction))
        {
            interaction.DropHeldObjectForStateChange();
        }
        return true;
    }

    /// <summary>Resets temporary statuses only when the player actually respawns.</summary>
    public bool ResetTemporaryStatuses()
    {
        if (!HasAuthority()) return false;
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        if (current.Status == PlayerTemporaryStatusKind.None && !IsSnapshotActive(current)) return false;
        WriteStatus(PlayerTemporaryStatusKind.None, 0f);
        return true;
    }

    internal PlayerTemporaryStatusSnapshot CaptureSnapshot() => CurrentSnapshot;

    internal bool ShouldKeepPendingPlacementMovementBlocked(ulong requestStartRevision)
    {
        if (IsTumbleweedCaptured) return true;
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        return current.Revision <= requestStartRevision
            || IsSnapshotActive(current) && current.Status == PlayerTemporaryStatusKind.PlacementLock;
    }

    public static bool IsWorldActionBlockedForClient(NetworkManager manager, ulong clientId)
    {
        if (manager == null || !manager.IsListening || !manager.IsServer
            || !manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
            || client.PlayerObject == null)
            return false;

        return client.PlayerObject.TryGetComponent(out PlayerTemporaryStatusController controller)
            && controller.BlocksActions;
    }

    /// <summary>Applies the authoritative response payload before placement input is released.</summary>
    internal bool ApplyServerSnapshot(PlayerTemporaryStatusSnapshot snapshot)
    {
        if (IsNetworkStateActive && !IsOwner && !IsServer) return false;
        return ApplySnapshot(snapshot);
    }

    private bool HasAuthority() => !IsNetworkStateActive || IsServer;

    private void ReplicatedStatusChanged(PlayerTemporaryStatusSnapshot previous,
        PlayerTemporaryStatusSnapshot current)
    {
        ApplySnapshot(current);
    }

    private bool ApplySnapshot(PlayerTemporaryStatusSnapshot snapshot)
    {
        if (snapshot.Revision < highestAppliedRevision) return false;
        if (snapshot.Revision == highestAppliedRevision && CurrentSnapshot.Equals(snapshot)) return false;
        if (snapshot.Revision == highestAppliedRevision && highestAppliedRevision != 0) return false;

        highestAppliedRevision = snapshot.Revision;
        appliedStatus = snapshot;
        if (!IsNetworkStateActive) offlineStatus = snapshot;
        StatusChanged?.Invoke(snapshot);
        return true;
    }

    private void WriteStatus(PlayerTemporaryStatusKind status, float durationSeconds)
    {
        ulong currentRevision = CurrentSnapshot.Revision;
        ulong nextRevision = Math.Max(highestAppliedRevision, currentRevision) + 1;
        PlayerTemporaryStatusSnapshot next = new(nextRevision, status,
            status == PlayerTemporaryStatusKind.None ? 0d : GetCurrentTime() + NormalizeDuration(durationSeconds));

        if (IsNetworkStateActive)
        {
            if (!IsServer) return;
            replicatedStatus.Value = next;
            ApplySnapshot(next);
        }
        else
        {
            offlineStatus = next;
            ApplySnapshot(next);
        }
    }

    private double GetCurrentTime() => IsNetworkStateActive
        ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

    private bool IsSnapshotActive(PlayerTemporaryStatusSnapshot snapshot) =>
        snapshot.Status != PlayerTemporaryStatusKind.None && snapshot.ExpiresAt > GetCurrentTime();

    private float GetRemainingSeconds(PlayerTemporaryStatusSnapshot snapshot)
    {
        if (snapshot.Status == PlayerTemporaryStatusKind.None) return 0f;
        return Mathf.Max(0f, (float)(snapshot.ExpiresAt - GetCurrentTime()));
    }

    private static float NormalizeDuration(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);

    private void ClearLocalLifecycleState()
    {
        PlayerTemporaryStatusSnapshot previous = appliedStatus.Status != PlayerTemporaryStatusKind.None
            ? appliedStatus : offlineStatus;
        offlineStatus = default;
        appliedStatus = default;
        highestAppliedRevision = 0;
        if (previous.Status != PlayerTemporaryStatusKind.None)
            StatusChanged?.Invoke(default);
    }
}
