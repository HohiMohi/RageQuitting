using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public enum PlayerTemporaryStatusKind : byte
{
    None,
    PlacementLock,
    CrushedHands
}

internal enum PlayerDomainStatusKind : int
{
    WaterExposure = -1001,
    WaterUnsupportedTimer = -1002,
    WaterExhaustionTimer = -1003,
    MudSoiling = -3001,
    ConcreteTrap = -2001,
    TumbleweedCapture = -2002,
    TumbleweedImmunity = -2003
}

public struct PlayerTemporaryStatusEntry : INetworkSerializable, IEquatable<PlayerTemporaryStatusEntry>
{
    public int StatusId;
    public ulong InstanceId;
    public double ExpiresAt;
    public bool HasDuration;
    public bool HasEvent;
    public float MovementMultiplier;
    public bool BlocksControls;
    public bool BlocksActions;
    public double DomainStartedAt;
    public double DomainDeadline;
    public float DomainValue;
    public int DomainData;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref StatusId);
        serializer.SerializeValue(ref InstanceId);
        serializer.SerializeValue(ref ExpiresAt);
        serializer.SerializeValue(ref HasDuration);
        serializer.SerializeValue(ref HasEvent);
        serializer.SerializeValue(ref MovementMultiplier);
        serializer.SerializeValue(ref BlocksControls);
        serializer.SerializeValue(ref BlocksActions);
        serializer.SerializeValue(ref DomainStartedAt);
        serializer.SerializeValue(ref DomainDeadline);
        serializer.SerializeValue(ref DomainValue);
        serializer.SerializeValue(ref DomainData);
    }

    public bool Equals(PlayerTemporaryStatusEntry other) =>
        StatusId == other.StatusId && InstanceId == other.InstanceId
        && ExpiresAt.Equals(other.ExpiresAt) && HasDuration == other.HasDuration
        && HasEvent == other.HasEvent && MovementMultiplier.Equals(other.MovementMultiplier)
        && BlocksControls == other.BlocksControls && BlocksActions == other.BlocksActions
        && DomainStartedAt.Equals(other.DomainStartedAt) && DomainDeadline.Equals(other.DomainDeadline)
        && DomainValue.Equals(other.DomainValue)
        && DomainData == other.DomainData;
}

public struct PlayerTemporaryStatusSnapshot : INetworkSerializable, IEquatable<PlayerTemporaryStatusSnapshot>
{
    public ulong Revision;
    public FixedList512Bytes<PlayerTemporaryStatusEntry> Entries;
    public ulong PlacementCommitId;
    public ulong TumbleweedCaptureEpisodeId;
    public ulong TumbleweedSourceNetworkObjectId;
    public int TumbleweedSlot;
    public Vector3 TumbleweedOffset;
    public Quaternion TumbleweedRelativeRotation;
    public ulong TumbleweedReleaseEpisodeId;
    public Vector3 TumbleweedReleasePosition;
    public Quaternion TumbleweedReleaseRotation;
    public Vector3 TumbleweedReleaseVelocity;

    public PlayerTemporaryStatusSnapshot(ulong revision, PlayerTemporaryStatusKind status, double expiresAt)
    {
        Revision = revision;
        Entries = default;
        PlacementCommitId = 0;
        TumbleweedCaptureEpisodeId = 0;
        TumbleweedSourceNetworkObjectId = ulong.MaxValue;
        TumbleweedSlot = -1;
        TumbleweedOffset = Vector3.zero;
        TumbleweedRelativeRotation = Quaternion.identity;
        TumbleweedReleaseEpisodeId = 0;
        TumbleweedReleasePosition = Vector3.zero;
        TumbleweedReleaseRotation = Quaternion.identity;
        TumbleweedReleaseVelocity = Vector3.zero;
        if (status != PlayerTemporaryStatusKind.None)
        {
            PlayerTemporaryStatusEntry entry = DefaultEntry((int)status);
            entry.ExpiresAt = expiresAt;
            entry.HasDuration = true;
            Entries.Add(entry);
        }
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Revision);
        int count = Entries.Length;
        serializer.SerializeValue(ref count);
        if (serializer.IsReader) Entries.Clear();
        for (int i = 0; i < count; i++)
        {
            PlayerTemporaryStatusEntry entry = serializer.IsReader ? default : Entries[i];
            entry.NetworkSerialize(serializer);
            if (serializer.IsReader) Entries.Add(entry);
        }
        serializer.SerializeValue(ref PlacementCommitId);
        serializer.SerializeValue(ref TumbleweedCaptureEpisodeId);
        if (TumbleweedCaptureEpisodeId != 0)
        {
            serializer.SerializeValue(ref TumbleweedSourceNetworkObjectId);
            serializer.SerializeValue(ref TumbleweedSlot);
            serializer.SerializeValue(ref TumbleweedOffset);
            serializer.SerializeValue(ref TumbleweedRelativeRotation);
        }
        else if (serializer.IsReader)
        {
            TumbleweedSourceNetworkObjectId = ulong.MaxValue;
            TumbleweedSlot = -1;
            TumbleweedOffset = Vector3.zero;
            TumbleweedRelativeRotation = Quaternion.identity;
        }
        serializer.SerializeValue(ref TumbleweedReleaseEpisodeId);
        if (TumbleweedReleaseEpisodeId != 0)
        {
            serializer.SerializeValue(ref TumbleweedReleasePosition);
            serializer.SerializeValue(ref TumbleweedReleaseRotation);
            serializer.SerializeValue(ref TumbleweedReleaseVelocity);
        }
        else if (serializer.IsReader)
        {
            TumbleweedReleasePosition = Vector3.zero;
            TumbleweedReleaseRotation = Quaternion.identity;
            TumbleweedReleaseVelocity = Vector3.zero;
        }
    }

    public bool Equals(PlayerTemporaryStatusSnapshot other)
    {
        if (Revision != other.Revision || PlacementCommitId != other.PlacementCommitId ||
            TumbleweedCaptureEpisodeId != other.TumbleweedCaptureEpisodeId ||
            TumbleweedSourceNetworkObjectId != other.TumbleweedSourceNetworkObjectId ||
            TumbleweedSlot != other.TumbleweedSlot || !TumbleweedOffset.Equals(other.TumbleweedOffset) ||
            !TumbleweedRelativeRotation.Equals(other.TumbleweedRelativeRotation) ||
            TumbleweedReleaseEpisodeId != other.TumbleweedReleaseEpisodeId ||
            !TumbleweedReleasePosition.Equals(other.TumbleweedReleasePosition) ||
            !TumbleweedReleaseRotation.Equals(other.TumbleweedReleaseRotation) ||
            !TumbleweedReleaseVelocity.Equals(other.TumbleweedReleaseVelocity) ||
            Entries.Length != other.Entries.Length) return false;
        for (int i = 0; i < Entries.Length; i++) if (!Entries[i].Equals(other.Entries[i])) return false;
        return true;
    }
    private static PlayerTemporaryStatusEntry DefaultEntry(int id) => new() { StatusId = id, MovementMultiplier = 1f };
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
    private PlayerTransportCollisionController transportCollision;
    private bool transportSuppressed;
    private PlayerNetworkSetup networkSetup;

    private PlayerTemporaryStatusSnapshot offlineStatus;
    private PlayerTemporaryStatusSnapshot appliedStatus;
    private ulong highestAppliedRevision;
    private ulong nextPlacementCommitId;
    private ulong nextStatusInstanceId;
    private ulong lastAppliedTumbleweedReleaseEpisodeId;
    private bool subscribed;

    public event Action<PlayerTemporaryStatusSnapshot> StatusChanged;
    public event Action<int, ulong> StatusActivated;

    public PlayerTemporaryStatusKind CurrentStatus
    {
        get
        {
            PlayerTemporaryStatusSnapshot snapshot = CurrentSnapshot;
            if (IsSnapshotActive(snapshot, (int)PlayerTemporaryStatusKind.CrushedHands)) return PlayerTemporaryStatusKind.CrushedHands;
            return IsSnapshotActive(snapshot, (int)PlayerTemporaryStatusKind.PlacementLock) ? PlayerTemporaryStatusKind.PlacementLock : PlayerTemporaryStatusKind.None;
        }
    }

    public bool BlocksControls => BlocksLook;
    public bool BlocksLook => AggregateBlocksControls() || IsTumbleweedCaptured;
    public bool BlocksMovement => BlocksLook || IsConcreteTrapActive;
    public bool BlocksActions => AggregateBlocksActions() || IsTumbleweedCaptured;
    public bool IsConcreteTrapActive => GetDomainStatus(PlayerDomainStatusKind.ConcreteTrap).InstanceId != 0;
    public bool IsCrushedHands => HasActiveStatus(PlayerTemporaryStatusKind.CrushedHands);
    public bool IsTumbleweedCaptured => HasActiveStatus(PlayerDomainStatusKind.TumbleweedCapture);
    public bool IsTumbleweedImmune => HasActiveStatus(PlayerDomainStatusKind.TumbleweedImmunity);
    public double TumbleweedReleaseAt => GetDomainStatus(PlayerDomainStatusKind.TumbleweedCapture).DomainDeadline;
    public int TumbleweedSlot
    {
        get
        {
            PlayerTemporaryStatusSnapshot snapshot = CurrentSnapshot;
            ulong episodeId = TumbleweedCaptureEpisodeId;
            return episodeId != 0 && snapshot.TumbleweedCaptureEpisodeId == episodeId
                ? snapshot.TumbleweedSlot : -1;
        }
    }
    internal ulong TumbleweedCaptureEpisodeId => GetDomainStatus(PlayerDomainStatusKind.TumbleweedCapture).InstanceId;
    internal bool IsTumbleweedCaptureEpisode(ulong episodeId) => episodeId != 0
        && TumbleweedCaptureEpisodeId == episodeId;
    public ulong CurrentRevision => CurrentSnapshot.Revision;
    public bool IsPlacementLockActive => HasActiveStatus(PlayerTemporaryStatusKind.PlacementLock);
    public float RemainingSeconds => CurrentStatus == PlayerTemporaryStatusKind.None
        ? 0f : GetStatusRemainingSeconds((int)CurrentStatus);
    public float PlacementLockDurationSeconds => NormalizeDuration(placementLockDurationSeconds);
    public float CrushedHandsDurationSeconds => NormalizeDuration(crushedHandsDurationSeconds);
    public float MovementMultiplier => GetMovementMultiplier();
    public float MudAmount => Mathf.Clamp01(GetDomainStatus(PlayerDomainStatusKind.MudSoiling).DomainValue);
    internal bool SetMudAmount(float amount, float movementMultiplier)
    {
        if (!HasAuthority()) return false;
        amount = float.IsNaN(amount) || float.IsInfinity(amount) ? 0f : Mathf.Clamp01(amount);
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        int index = FindEntry(current.Entries, (int)PlayerDomainStatusKind.MudSoiling);
        if (amount <= 0f)
        {
            if (index < 0) return true;
            current.Entries.RemoveAt(index);
            WriteSnapshotMutation(current);
            return true;
        }
        PlayerTemporaryStatusEntry entry = index >= 0 ? current.Entries[index] : default;
        if (index < 0)
        {
            if (current.Entries.Length >= GetDomainCapacityLimit(current.Entries)) return false;
            nextStatusInstanceId = Math.Max(nextStatusInstanceId, Math.Max(current.Revision, highestAppliedRevision)) + 1;
            entry.StatusId = (int)PlayerDomainStatusKind.MudSoiling;
            entry.InstanceId = nextStatusInstanceId;
            entry.HasEvent = true;
        }
        entry.HasDuration = false;
        entry.MovementMultiplier = NormalizeMultiplier(movementMultiplier);
        entry.DomainValue = amount;
        entry.BlocksControls = false;
        entry.BlocksActions = false;
        if (index >= 0 && current.Entries[index].Equals(entry)) return true;
        if (index >= 0) current.Entries[index] = entry;
        else current.Entries.Add(entry);
        WriteSnapshotMutation(current);
        return true;
    }
    public ulong PlacementCommitId => CurrentSnapshot.PlacementCommitId;
    private bool HasActiveStatus(PlayerTemporaryStatusKind kind) => IsSnapshotActive(CurrentSnapshot, (int)kind);
    private bool HasActiveStatus(PlayerDomainStatusKind kind) => IsSnapshotActive(CurrentSnapshot, (int)kind);

    /// <summary>Scales a human carrier's movement once and clears stale input while blocked.</summary>
    public bool ApplySharedCarryInputModifiers(ref Vector3 translation, ref Vector3 lateral, ref float yaw)
    {
        if (BlocksControls || BlocksActions)
        {
            translation = Vector3.zero;
            lateral = Vector3.zero;
            yaw = 0f;
            return true;
        }

        float multiplier = MovementMultiplier;
        translation *= multiplier;
        lateral *= multiplier;
        return false;
    }

    /// <summary>Applies or refreshes a synthetic/status instance on its authority.</summary>
    public bool TryApplyStatus(int statusId, float durationSeconds, bool waitForEvent, float movementMultiplier,
        bool blocksControls, bool blocksActions, out ulong instanceId)
    {
        instanceId = 0;
        if (statusId <= (int)PlayerTemporaryStatusKind.CrushedHands || !HasAuthority()
            || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds)
            || durationSeconds < 0f && (durationSeconds != -1f || !waitForEvent)
            || durationSeconds == 0f)
            return false;
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        FixedList512Bytes<PlayerTemporaryStatusEntry> entries = current.Entries;
        for (int i = entries.Length - 1; i >= 0; i--)
            if (EntryExpired(entries[i])) entries.RemoveAt(i);
        int index = FindEntry(entries, statusId);
        bool wasActive = index >= 0;
        nextStatusInstanceId = Math.Max(nextStatusInstanceId, Math.Max(current.Revision, highestAppliedRevision)) + 1;
        ulong newId = nextStatusInstanceId;
        var entry = new PlayerTemporaryStatusEntry
        {
            StatusId = statusId, InstanceId = newId, HasDuration = durationSeconds >= 0f,
            ExpiresAt = durationSeconds >= 0f ? GetCurrentTime() + NormalizeDuration(durationSeconds) : 0d,
            HasEvent = waitForEvent, MovementMultiplier = NormalizeMultiplier(movementMultiplier),
            BlocksControls = blocksControls, BlocksActions = blocksActions
        };
        if (index >= 0) entries[index] = entry;
        else
        {
            bool hasProductionStatus = FindEntry(entries, (int)PlayerTemporaryStatusKind.PlacementLock) >= 0
                || FindEntry(entries, (int)PlayerTemporaryStatusKind.CrushedHands) >= 0;
            int capacityLimit = hasProductionStatus ? entries.Capacity : Math.Max(0, entries.Capacity - 1);
            if (entries.Length >= capacityLimit) return false;
            entries.Add(entry);
        }
        current.Entries = entries;
        WriteSnapshotMutation(current);
        instanceId = newId;
        if (!wasActive) StatusActivated?.Invoke(statusId, newId);
        return true;
    }

    /// <summary>Signals an event condition. A stale event cannot remove a refreshed/replaced instance.</summary>
    public bool SignalStatusEvent(int statusId, ulong instanceId)
    {
        if (!HasAuthority() || instanceId == 0) return false;
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        int index = FindEntry(current.Entries, statusId);
        if (index < 0) return false;
        PlayerTemporaryStatusEntry entry = current.Entries[index];
        if (entry.InstanceId != instanceId || !entry.HasEvent || EntryExpired(entry)) return false;
        RemoveEntry(ref current.Entries, statusId);
        WriteSnapshotMutation(current);
        return true;
    }

    public float GetStatusRemainingSeconds(int statusId)
    {
        int index = FindEntry(CurrentSnapshot.Entries, statusId);
        if (index < 0) return 0f;
        PlayerTemporaryStatusEntry entry = CurrentSnapshot.Entries[index];
        return entry.HasDuration ? Mathf.Max(0f, (float)(entry.ExpiresAt - GetCurrentTime())) : float.PositiveInfinity;
    }

    internal PlayerTemporaryStatusEntry GetDomainStatus(PlayerDomainStatusKind kind)
    {
        int index = FindEntry(CurrentSnapshot.Entries, (int)kind);
        return index < 0 ? default : CurrentSnapshot.Entries[index];
    }

    internal bool SetDomainStatus(PlayerDomainStatusKind kind, ref ulong instanceId, double startedAt,
        float value, int data)
    {
        if (!HasAuthority()) return false;
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        FixedList512Bytes<PlayerTemporaryStatusEntry> entries = current.Entries;
        int id = (int)kind;
        int index = FindEntry(entries, id);
        if (index >= 0 && entries[index].InstanceId != instanceId) return false;
        PlayerTemporaryStatusEntry entry = index >= 0 ? entries[index] : new PlayerTemporaryStatusEntry
        { StatusId = id, MovementMultiplier = 1f, HasEvent = true };
        if (index < 0)
        {
            nextStatusInstanceId = Math.Max(nextStatusInstanceId, Math.Max(current.Revision, highestAppliedRevision)) + 1;
            instanceId = nextStatusInstanceId;
        }
        entry.InstanceId = instanceId;
        entry.HasDuration = false;
        entry.HasEvent = true;
        entry.BlocksControls = false;
        entry.BlocksActions = kind == PlayerDomainStatusKind.ConcreteTrap;
        entry.DomainStartedAt = startedAt;
        entry.DomainValue = value;
        entry.DomainData = data;
        if (index >= 0)
        {
            if (entries[index].Equals(entry)) return true;
            entries[index] = entry;
        }
        else
        {
            if (entries.Length >= GetDomainCapacityLimit(entries)) return false;
            entries.Add(entry);
        }
        current.Entries = entries;
        WriteSnapshotMutation(current);
        return true;
    }

    internal bool ClearDomainStatus(PlayerDomainStatusKind kind, ref ulong instanceId)
    {
        if (!HasAuthority() || instanceId == 0) return false;
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        int index = FindEntry(current.Entries, (int)kind);
        if (index < 0 || current.Entries[index].InstanceId != instanceId) return false;
        current.Entries.RemoveAt(index);
        WriteSnapshotMutation(current);
        instanceId = 0;
        return true;
    }

    private float GetMovementMultiplier()
    {
        float value = 1f;
        FixedList512Bytes<PlayerTemporaryStatusEntry> entries = CurrentSnapshot.Entries;
        for (int i = 0; i < entries.Length; i++) if (!EntryExpired(entries[i])) value *= NormalizeMultiplier(entries[i].MovementMultiplier);
        return value;
    }
    private bool AggregateBlocksControls()
    {
        FixedList512Bytes<PlayerTemporaryStatusEntry> entries = CurrentSnapshot.Entries;
        for (int i = 0; i < entries.Length; i++)
            if (!EntryExpired(entries[i]) && entries[i].BlocksControls) return true;
        return false;
    }

    private bool AggregateBlocksActions()
    {
        FixedList512Bytes<PlayerTemporaryStatusEntry> entries = CurrentSnapshot.Entries;
        for (int i = 0; i < entries.Length; i++)
            if (!EntryExpired(entries[i]) && entries[i].BlocksActions) return true;
        return false;
    }
    private static float NormalizeMultiplier(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Max(0f, value);

    private bool IsNetworkStateActive => IsSpawned && NetworkManager != null && NetworkManager.IsListening;
    private PlayerTemporaryStatusSnapshot CurrentSnapshot => IsNetworkStateActive
        ? appliedStatus : offlineStatus;

    public override void OnNetworkSpawn()
    {
        replicatedStatus.OnValueChanged += ReplicatedStatusChanged;
        subscribed = true;
        if (IsServer)
        {
            ulong priorRevision = Math.Max(replicatedStatus.Value.Revision, Math.Max(highestAppliedRevision, CurrentSnapshot.Revision));
            ulong placementCommitId = Math.Max(CurrentSnapshot.PlacementCommitId, nextPlacementCommitId);
            PlayerTemporaryStatusSnapshot fresh = new(priorRevision + 1, PlayerTemporaryStatusKind.None, 0d)
            { PlacementCommitId = placementCommitId };
            nextPlacementCommitId = placementCommitId;
            replicatedStatus.Value = fresh;
            ApplySnapshot(fresh);
            lastAppliedTumbleweedReleaseEpisodeId = CurrentSnapshot.TumbleweedReleaseEpisodeId;
        }
        else
        {
            lastAppliedTumbleweedReleaseEpisodeId = replicatedStatus.Value.TumbleweedReleaseEpisodeId;
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
        StatusActivated = null;
        base.OnDestroy();
    }

    private void Update()
    {
        SyncCaptureCollisionState();
        if (!IsNetworkStateActive)
        {
            FollowTumbleweed(CurrentSnapshot);
            if (HasExpiredStatus(offlineStatus)) PruneExpiredStatuses();
            return;
        }

        if (IsTumbleweedCaptured) FollowTumbleweed(CurrentSnapshot);

        if (IsServer && HasExpiredStatus(replicatedStatus.Value)) PruneExpiredStatuses();
    }

    /// <summary>Starts or refreshes the short lock after an accepted placement.</summary>
    public bool BeginPlacementLock()
    {
        if (!HasAuthority() || IsTumbleweedCaptured) return false;
        if (HasActiveStatus(PlayerTemporaryStatusKind.CrushedHands)) return false;

        ulong priorCommitId = nextPlacementCommitId;
        nextPlacementCommitId = Math.Max(nextPlacementCommitId, CurrentSnapshot.PlacementCommitId) + 1;
        if (WriteStatus(PlayerTemporaryStatusKind.PlacementLock, PlacementLockDurationSeconds)) return true;
        nextPlacementCommitId = priorCommitId;
        return false;
    }

    public bool BeginTumbleweedCapture(ulong weedNetworkId, int slot, float durationSeconds, Vector3 offset, Quaternion relativeRotation)
    {
        return BeginTumbleweedCapture(weedNetworkId, slot, durationSeconds, offset, relativeRotation, out _);
    }

    internal bool BeginTumbleweedCapture(ulong weedNetworkId, int slot, float durationSeconds,
        Vector3 offset, Quaternion relativeRotation, out ulong episodeId)
    {
        episodeId = 0;
        if (!HasAuthority() || IsTumbleweedCaptured || IsTumbleweedImmune) return false;
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        FixedList512Bytes<PlayerTemporaryStatusEntry> entries = current.Entries;
        for (int i = entries.Length - 1; i >= 0; i--)
            if (EntryExpired(entries[i])) entries.RemoveAt(i);
        if (FindEntry(entries, (int)PlayerDomainStatusKind.TumbleweedCapture) >= 0) return false;

        double now = GetCurrentTime();
        int exposureIndex = FindEntry(entries, (int)PlayerDomainStatusKind.WaterExposure);
        if (exposureIndex >= 0) entries.RemoveAt(exposureIndex);
        RemoveEntry(ref entries, (int)PlayerDomainStatusKind.WaterUnsupportedTimer);
        RemoveEntry(ref entries, (int)PlayerDomainStatusKind.WaterExhaustionTimer);
        if (entries.Length >= entries.Capacity) return false;
        if (TryGetComponent(out PlayerWaterExposureController waterExposure))
            waterExposure.PrepareForTumbleweedCapture();
        double releaseAt = now + Math.Max(0.1f, NormalizeDuration(durationSeconds));
        nextStatusInstanceId = Math.Max(nextStatusInstanceId, Math.Max(current.Revision, highestAppliedRevision)) + 1;
        episodeId = nextStatusInstanceId;
        entries.Add(new PlayerTemporaryStatusEntry
        {
            StatusId = (int)PlayerDomainStatusKind.TumbleweedCapture,
            InstanceId = episodeId,
            HasEvent = true,
            MovementMultiplier = 1f,
            DomainStartedAt = now,
            DomainDeadline = releaseAt
        });
        current.Entries = entries;
        current.TumbleweedCaptureEpisodeId = episodeId;
        current.TumbleweedSourceNetworkObjectId = weedNetworkId;
        current.TumbleweedSlot = slot;
        current.TumbleweedOffset = offset;
        current.TumbleweedRelativeRotation = relativeRotation;
        current.TumbleweedReleaseEpisodeId = 0;
        current.TumbleweedReleasePosition = Vector3.zero;
        current.TumbleweedReleaseRotation = Quaternion.identity;
        current.TumbleweedReleaseVelocity = Vector3.zero;
        current.Revision = Math.Max(highestAppliedRevision, CurrentSnapshot.Revision) + 1;
        if (!WriteSnapshot(current))
        {
            episodeId = 0;
            return false;
        }
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
        return CompleteTumbleweedCapture(TumbleweedCaptureEpisodeId, immunitySeconds,
            transform.position, transform.rotation, Vector3.up * 1.1f);
    }

    internal bool CompleteTumbleweedCapture(ulong expectedEpisodeId, float immunitySeconds,
        Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        if (!HasAuthority() || expectedEpisodeId == 0) return false;
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        FixedList512Bytes<PlayerTemporaryStatusEntry> entries = current.Entries;
        int captureIndex = FindEntry(entries, (int)PlayerDomainStatusKind.TumbleweedCapture);
        if (captureIndex < 0 || entries[captureIndex].InstanceId != expectedEpisodeId) return false;

        entries.RemoveAt(captureIndex);
        RemoveEntry(ref entries, (int)PlayerDomainStatusKind.TumbleweedImmunity);
        float normalizedImmunity = NormalizeDuration(immunitySeconds);
        if (normalizedImmunity > 0f)
        {
            nextStatusInstanceId = Math.Max(nextStatusInstanceId, Math.Max(current.Revision, highestAppliedRevision)) + 1;
            double immunityDeadline = GetCurrentTime() + normalizedImmunity;
            entries.Add(new PlayerTemporaryStatusEntry
            {
                StatusId = (int)PlayerDomainStatusKind.TumbleweedImmunity,
                InstanceId = nextStatusInstanceId,
                HasDuration = true,
                ExpiresAt = immunityDeadline,
                MovementMultiplier = 1f,
                DomainStartedAt = GetCurrentTime(),
                DomainDeadline = immunityDeadline
            });
        }

        current.Entries = entries;
        current.TumbleweedCaptureEpisodeId = 0;
        current.TumbleweedSourceNetworkObjectId = ulong.MaxValue;
        current.TumbleweedSlot = -1;
        current.TumbleweedOffset = Vector3.zero;
        current.TumbleweedRelativeRotation = Quaternion.identity;
        current.TumbleweedReleaseEpisodeId = expectedEpisodeId;
        current.TumbleweedReleasePosition = position;
        current.TumbleweedReleaseRotation = rotation;
        current.TumbleweedReleaseVelocity = velocity;
        current.Revision = Math.Max(highestAppliedRevision, CurrentSnapshot.Revision) + 1;
        return WriteSnapshot(current);
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

    private void FollowTumbleweed(PlayerTemporaryStatusSnapshot snapshot)
    {
        ulong episodeId = TumbleweedCaptureEpisodeId;
        if (episodeId == 0 || snapshot.TumbleweedCaptureEpisodeId != episodeId
            || snapshot.TumbleweedSourceNetworkObjectId == ulong.MaxValue || NetworkManager == null
            || NetworkManager.SpawnManager == null
            || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(snapshot.TumbleweedSourceNetworkObjectId, out NetworkObject weed)) return;
        transform.SetPositionAndRotation(weed.transform.position + weed.transform.rotation * snapshot.TumbleweedOffset,
            weed.transform.rotation * snapshot.TumbleweedRelativeRotation);
    }

    /// <summary>Applies the injury once, and releases the player's inventory at its onset.</summary>
    internal bool TryApplyCrushedHands(ulong expectedPlacementCommitId)
    {
        if (!HasAuthority() || IsTumbleweedCaptured) return false;

        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        if (expectedPlacementCommitId == 0 || current.PlacementCommitId != expectedPlacementCommitId
            || !IsSnapshotActive(current, (int)PlayerTemporaryStatusKind.PlacementLock))
            return false;

        bool alreadyCrushed = IsSnapshotActive(current, (int)PlayerTemporaryStatusKind.CrushedHands);
        if (!WriteStatus(PlayerTemporaryStatusKind.CrushedHands, CrushedHandsDurationSeconds)) return false;
        if (!alreadyCrushed && TryGetComponent(out PlayerInventory inventory)) inventory.DropAllInventoryForAuthority();
        if (!alreadyCrushed && IsNetworkStateActive)
        {
            BaseResourceNew.ReleaseHeldObjectForTemporaryStatus(NetworkManager, OwnerClientId);
            MountableBridgeComponent.ReleaseHeldObjectForTemporaryStatus(NetworkManager, OwnerClientId);
            DownedPlayerCarryable.ReleaseCarriedPlayerForTemporaryStatus(NetworkManager, OwnerClientId);
        }
        else if (!alreadyCrushed && TryGetComponent(out PlayerInteractionNew interaction))
        {
            interaction.DropHeldObjectForStateChange();
        }
        if (!alreadyCrushed)
        {
            NetworkManager manager = IsNetworkStateActive ? NetworkManager : null;
            WheelbarrowController wheelbarrow = WheelbarrowController.FindForPlayer(OwnerClientId, manager);
            if (wheelbarrow != null) wheelbarrow.ReleaseDriverForCrushedHands(OwnerClientId);
        }
        return true;
    }

    /// <summary>Resets temporary statuses only when the player actually respawns.</summary>
    public bool ResetTemporaryStatuses()
    {
        if (!HasAuthority()) return false;
        if (TryGetComponent(out PlayerMudStatusController mudStatus)) mudStatus.ResetMudPositionSample();
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        FixedList512Bytes<PlayerTemporaryStatusEntry> entries = current.Entries;
        for (int i = entries.Length - 1; i >= 0; i--)
            if (entries[i].StatusId != (int)PlayerDomainStatusKind.TumbleweedImmunity || EntryExpired(entries[i]))
                entries.RemoveAt(i);
        if (entries.Length == current.Entries.Length) return false;
        current.Entries = entries;
        WriteSnapshotMutation(current);
        return true;
    }

    internal PlayerTemporaryStatusSnapshot CaptureSnapshot() => CurrentSnapshot;

    internal bool ShouldKeepPendingPlacementMovementBlocked(ulong requestStartRevision)
    {
        if (IsTumbleweedCaptured) return true;
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        return current.PlacementCommitId <= requestStartRevision
            || IsSnapshotActive(current, (int)PlayerTemporaryStatusKind.PlacementLock);
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

    private bool HasAuthority()
    {
        NetworkManager manager = NetworkManager != null ? NetworkManager : Unity.Netcode.NetworkManager.Singleton;
        if (IsSpawned) return manager != null && manager.IsListening && IsServer;
        return manager == null || !manager.IsListening;
    }

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

        if (snapshot.TumbleweedReleaseEpisodeId > lastAppliedTumbleweedReleaseEpisodeId)
        {
            lastAppliedTumbleweedReleaseEpisodeId = snapshot.TumbleweedReleaseEpisodeId;
            bool newerCaptureActive = IsSnapshotActive(snapshot, (int)PlayerDomainStatusKind.TumbleweedCapture)
                && snapshot.TumbleweedCaptureEpisodeId > snapshot.TumbleweedReleaseEpisodeId;
            if (!newerCaptureActive && (!IsNetworkStateActive || IsOwner))
                ApplyTumbleweedReleasePoseLocal(snapshot.TumbleweedReleasePosition,
                    snapshot.TumbleweedReleaseRotation, snapshot.TumbleweedReleaseVelocity);
        }

        highestAppliedRevision = snapshot.Revision;
        appliedStatus = snapshot;
        if (!IsNetworkStateActive) offlineStatus = snapshot;
        SyncCaptureCollisionState();
        StatusChanged?.Invoke(snapshot);
        return true;
    }

    private bool WriteStatus(PlayerTemporaryStatusKind status, float durationSeconds)
    {
        ulong currentRevision = CurrentSnapshot.Revision;
        ulong nextRevision = Math.Max(highestAppliedRevision, currentRevision) + 1;
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        bool wasActive = status != PlayerTemporaryStatusKind.None && IsSnapshotActive(current, (int)status);
        FixedList512Bytes<PlayerTemporaryStatusEntry> entries = current.Entries;
        for (int i = entries.Length - 1; i >= 0; i--) if (EntryExpired(entries[i])) entries.RemoveAt(i);
        if (status == PlayerTemporaryStatusKind.CrushedHands) RemoveEntry(ref entries, (int)PlayerTemporaryStatusKind.PlacementLock);
        if (status == PlayerTemporaryStatusKind.None) entries.Clear();
        else
        {
            int index = FindEntry(entries, (int)status);
            PlayerTemporaryStatusEntry entry = index >= 0 ? entries[index] : new PlayerTemporaryStatusEntry
            { StatusId = (int)status, InstanceId = nextRevision, MovementMultiplier = 1f,
                BlocksControls = status == PlayerTemporaryStatusKind.PlacementLock,
                BlocksActions = status == PlayerTemporaryStatusKind.PlacementLock || status == PlayerTemporaryStatusKind.CrushedHands };
            entry.HasDuration = true; entry.ExpiresAt = GetCurrentTime() + NormalizeDuration(durationSeconds); entry.HasEvent = false;
            if (index >= 0) entries[index] = entry;
            else if (entries.Length < entries.Capacity) entries.Add(entry);
            else return false;
        }
        current.Revision = nextRevision;
        current.Entries = entries;
        current.PlacementCommitId = Math.Max(current.PlacementCommitId, nextPlacementCommitId);

        if (!WriteSnapshot(current)) return false;
        if (status != PlayerTemporaryStatusKind.None && !wasActive
            && IsSnapshotActive(current, (int)status))
        {
            int activatedIndex = FindEntry(entries, (int)status);
            if (activatedIndex >= 0) StatusActivated?.Invoke((int)status, entries[activatedIndex].InstanceId);
        }
        return true;
    }

    private double GetCurrentTime() => IsNetworkStateActive
        ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

    private bool IsSnapshotActive(PlayerTemporaryStatusSnapshot snapshot)
    {
        for (int i = 0; i < snapshot.Entries.Length; i++)
            if (!EntryExpired(snapshot.Entries[i])) return true;
        return false;
    }

    private bool IsSnapshotActive(PlayerTemporaryStatusSnapshot snapshot, int statusId)
    {
        int index = FindEntry(snapshot.Entries, statusId);
        return index >= 0 && !EntryExpired(snapshot.Entries[index]);
    }

    private bool HasExpiredStatus(PlayerTemporaryStatusSnapshot snapshot)
    {
        for (int i = 0; i < snapshot.Entries.Length; i++)
            if (EntryExpired(snapshot.Entries[i])) return true;
        return false;
    }

    private bool EntryExpired(PlayerTemporaryStatusEntry entry) => entry.HasDuration && entry.ExpiresAt <= GetCurrentTime()
        || !entry.HasDuration && !entry.HasEvent;

    private static int FindEntry(FixedList512Bytes<PlayerTemporaryStatusEntry> entries, int statusId)
    {
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].StatusId == statusId) return i;
        return -1;
    }

    private static int GetDomainCapacityLimit(FixedList512Bytes<PlayerTemporaryStatusEntry> entries)
    {
        bool hasProductionStatus = FindEntry(entries, (int)PlayerTemporaryStatusKind.PlacementLock) >= 0
            || FindEntry(entries, (int)PlayerTemporaryStatusKind.CrushedHands) >= 0;
        return hasProductionStatus ? entries.Capacity : Math.Max(0, entries.Capacity - 1);
    }

    private static void RemoveEntry(ref FixedList512Bytes<PlayerTemporaryStatusEntry> entries, int statusId)
    {
        int index = FindEntry(entries, statusId);
        if (index >= 0) entries.RemoveAt(index);
    }

    private void PruneExpiredStatuses()
    {
        PlayerTemporaryStatusSnapshot current = CurrentSnapshot;
        for (int i = current.Entries.Length - 1; i >= 0; i--)
            if (EntryExpired(current.Entries[i])) current.Entries.RemoveAt(i);
        WriteSnapshotMutation(current);
    }

    private void WriteSnapshotMutation(PlayerTemporaryStatusSnapshot next)
    {
        next.Revision = Math.Max(highestAppliedRevision, CurrentSnapshot.Revision) + 1;
        WriteSnapshot(next);
    }

    private bool WriteSnapshot(PlayerTemporaryStatusSnapshot next)
    {
        if (IsNetworkStateActive)
        {
            if (!IsServer) return false;
            replicatedStatus.Value = next;
        }
        else offlineStatus = next;
        ApplySnapshot(next);
        return true;
    }

    private static float NormalizeDuration(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);

    private void ClearLocalLifecycleState()
    {
        PlayerTemporaryStatusSnapshot previous = appliedStatus.Entries.Length > 0 ? appliedStatus : offlineStatus;
        offlineStatus = default;
        appliedStatus = default;
        highestAppliedRevision = 0;
        lastAppliedTumbleweedReleaseEpisodeId = 0;
        SyncCaptureCollisionState();
        if (previous.Entries.Length > 0)
            StatusChanged?.Invoke(default);
    }
}
