using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>A unique request handle bound to one reservation on one nail incarnation.</summary>
public readonly struct MountingNailExtractionHandle : IEquatable<MountingNailExtractionHandle>
{
    internal readonly ulong High;
    internal readonly ulong Low;

    internal MountingNailExtractionHandle(ulong high, ulong low)
    {
        High = high;
        Low = low;
    }

    public bool IsValid => High != 0 || Low != 0;
    public bool Equals(MountingNailExtractionHandle other) => High == other.High && Low == other.Low;
    public override bool Equals(object obj) => obj is MountingNailExtractionHandle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(High, Low);
    public static bool operator ==(MountingNailExtractionHandle left, MountingNailExtractionHandle right) => left.Equals(right);
    public static bool operator !=(MountingNailExtractionHandle left, MountingNailExtractionHandle right) => !left.Equals(right);
}

public enum MountingNailExtractionAvailability
{
    Available,
    Reserved,
    PermanentlyMounted,
    Unavailable
}

/// <summary>Read-only reservation state for gameplay and presentation consumers.</summary>
public readonly struct MountingNailExtractionReadState
{
    public MountingNailExtractionAvailability Availability { get; }
    public MountingNailExtractionHandle Handle { get; }
    public ulong ExecutorNetworkObjectId { get; }
    public ulong ExecutorOwnerClientId { get; }
    public Behaviour OfflineExecutor { get; }
    public double StartedAtServerTime { get; }
    public float DurationSeconds { get; }
    public double DeadlineServerTime { get; }
    public float ProgressNormalized { get; }

    internal MountingNailExtractionReadState(MountingNailExtractionAvailability availability,
        MountingNailExtractionSnapshot snapshot, Behaviour offlineExecutor, double currentTime)
    {
        Availability = availability;
        Handle = new MountingNailExtractionHandle(snapshot.HandleHigh, snapshot.HandleLow);
        ExecutorNetworkObjectId = snapshot.ExecutorNetworkObjectId;
        ExecutorOwnerClientId = snapshot.ExecutorOwnerClientId;
        OfflineExecutor = offlineExecutor;
        StartedAtServerTime = snapshot.StartedAtServerTime;
        DurationSeconds = snapshot.DurationSeconds;
        DeadlineServerTime = snapshot.StartedAtServerTime + snapshot.DurationSeconds;
        ProgressNormalized = snapshot.Active && DurationSeconds > 0f && IsFinite(DurationSeconds)
            ? Mathf.Clamp01((float)((currentTime - StartedAtServerTime) / DurationSeconds)) : 0f;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>Late-join-safe reservation data; progress is derived from the synchronized server clock.</summary>
internal struct MountingNailExtractionSnapshot : INetworkSerializable, IEquatable<MountingNailExtractionSnapshot>
{
    public ulong HandleHigh;
    public ulong HandleLow;
    public ulong ExecutorNetworkObjectId;
    public ulong ExecutorOwnerClientId;
    public double StartedAtServerTime;
    public float DurationSeconds;
    public bool Active;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref HandleHigh);
        serializer.SerializeValue(ref HandleLow);
        serializer.SerializeValue(ref ExecutorNetworkObjectId);
        serializer.SerializeValue(ref ExecutorOwnerClientId);
        serializer.SerializeValue(ref StartedAtServerTime);
        serializer.SerializeValue(ref DurationSeconds);
        serializer.SerializeValue(ref Active);
    }

    public bool Equals(MountingNailExtractionSnapshot other) => HandleHigh == other.HandleHigh
        && HandleLow == other.HandleLow && ExecutorNetworkObjectId == other.ExecutorNetworkObjectId
        && ExecutorOwnerClientId == other.ExecutorOwnerClientId
        && StartedAtServerTime.Equals(other.StartedAtServerTime)
        && DurationSeconds.Equals(other.DurationSeconds) && Active == other.Active;
}

public sealed partial class MountingNail
{
    private BridgeDeckPanelMounting extractionFirstPanel;
    private BridgeDeckPanelMounting extractionSecondPanel;
    private readonly NetworkVariable<MountingNailExtractionSnapshot> extractionSnapshot = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private MountingNailExtractionSnapshot offlineExtractionSnapshot;
    private Behaviour extractionExecutor;
    private NetworkObject extractionExecutorNetworkObject;
    private NetworkManager extractionNetworkManager;
    private bool extractionCommitting;

    /// <summary>True when an eligible nail has no current reservation.</summary>
    public bool IsCrowbarExtractionAvailable => GetExtractionSnapshot().Active == false && IsExtractionTargetEligible();

    /// <summary>Progress derived from the server's synchronized time; never replicated per frame.</summary>
    public float CrowbarExtractionProgressNormalized
    {
        get
        {
            MountingNailExtractionSnapshot snapshot = GetExtractionSnapshot();
            if (!snapshot.Active || !IsFinite(snapshot.DurationSeconds) || snapshot.DurationSeconds <= 0f) return 0f;
            double now = IsNetworkSessionActive() && NetworkManager != null
                ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
            return Mathf.Clamp01((float)((now - snapshot.StartedAtServerTime) / snapshot.DurationSeconds));
        }
    }

    public MountingNailExtractionReadState CrowbarExtractionReadState
    {
        get
        {
            MountingNailExtractionSnapshot snapshot = GetExtractionSnapshot();
            double now = IsNetworkSessionActive() && NetworkManager != null
                ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
            MountingNailExtractionAvailability availability = snapshot.Active
                ? MountingNailExtractionAvailability.Reserved
                : !HasExtractionAttachment() ? MountingNailExtractionAvailability.Unavailable
                : IsExtractionTargetProtected() ? MountingNailExtractionAvailability.PermanentlyMounted
                : MountingNailExtractionAvailability.Available;
            return new MountingNailExtractionReadState(availability, snapshot, extractionExecutor, now);
        }
    }

    /// <summary>Starts an authoritative reservation. Invalid executors and non-positive durations are rejected.</summary>
    public bool TryBeginCrowbarExtraction(Behaviour executor, float durationSeconds,
        out MountingNailExtractionHandle handle)
    {
        handle = default;
        if (!IsValidExecutor(executor) || !IsFinite(durationSeconds) || durationSeconds <= 0f
            || GetExtractionSnapshot().Active || !CanMutateExtractionAuthoritatively()
            || !IsExtractionTargetEligible()) return false;

        bool networked = IsNetworkSessionActive();
        NetworkObject executorNetworkObject = null;
        NetworkManager manager = null;
        ulong owner = NetworkManager.ServerClientId;
        if (networked)
        {
            manager = NetworkManager;
            executorNetworkObject = executor.GetComponentInParent<NetworkObject>();
            if (!IsValidNetworkExecutor(executorNetworkObject, manager, out owner)) return false;
        }
        else if (IsSpawned || NetworkManager != null && NetworkManager.IsListening) return false;

        Guid id = Guid.NewGuid();
        handle = new MountingNailExtractionHandle(BitConverter.ToUInt64(id.ToByteArray(), 0),
            BitConverter.ToUInt64(id.ToByteArray(), 8));
        double now = networked ? manager.ServerTime.Time : Time.timeAsDouble;
        extractionExecutor = executor;
        extractionExecutorNetworkObject = executorNetworkObject;
        extractionNetworkManager = manager;
        WriteExtractionSnapshot(new MountingNailExtractionSnapshot
        {
            HandleHigh = handle.High,
            HandleLow = handle.Low,
            ExecutorNetworkObjectId = executorNetworkObject != null ? executorNetworkObject.NetworkObjectId : 0,
            ExecutorOwnerClientId = owner,
            StartedAtServerTime = now,
            DurationSeconds = durationSeconds,
            Active = true
        });
        StateOrAttachmentChanged += OnExtractionNailChanged;
        SubscribeExtractionPanels();
        return true;
    }

    /// <summary>Cancels only this executor's current reservation when its unique request handle still matches.</summary>
    public bool CancelCrowbarExtraction(Behaviour executor, MountingNailExtractionHandle handle)
    {
        if (!IsCurrentExtraction(executor, handle) || !CanMutateExtractionAuthoritatively()) return false;
        ClearExtractionReservation();
        return true;
    }

    /// <summary>Commits only after the duration elapsed and after all nail, executor, and panel checks pass.</summary>
    public bool TryCommitCrowbarExtraction(Behaviour executor, MountingNailExtractionHandle handle)
    {
        if (!IsCurrentExtraction(executor, handle) || !CanMutateExtractionAuthoritatively()
            || !IsValidExecutor(executor)) return false;
        MountingNailExtractionSnapshot snapshot = GetExtractionSnapshot();
        if (!IsCurrentNetworkExecutor(snapshot) || !IsExtractionTargetEligible()
            || !IsCurrentExtraction(executor, handle)) return false;
        snapshot = GetExtractionSnapshot();
        double now = IsNetworkSessionActive() && NetworkManager != null
            ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        if (!snapshot.Active || now - snapshot.StartedAtServerTime < snapshot.DurationSeconds) return false;

        extractionCommitting = true;
        bool removed;
        try { removed = TryExtractForCrowbar(); }
        finally { extractionCommitting = false; }
        ClearExtractionReservation();
        return removed;
    }

    private bool CanMutateExtractionAuthoritatively()
    {
        if (!IsNetworkSessionActive())
            return !IsSpawned && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening);
        return IsSpawned && IsServer;
    }

    private static bool IsValidExecutor(Behaviour executor) => executor != null
        && executor.isActiveAndEnabled && executor.gameObject.activeInHierarchy;

    private static bool IsValidNetworkExecutor(NetworkObject networkObject, NetworkManager manager, out ulong owner)
    {
        owner = NetworkManager.ServerClientId;
        if (networkObject == null || manager == null || !manager.IsListening || !manager.IsServer
            || !networkObject.IsSpawned || networkObject.NetworkManager != manager) return false;
        owner = networkObject.OwnerClientId;
        return owner == NetworkManager.ServerClientId || manager.ConnectedClients.ContainsKey(owner);
    }

    private bool IsCurrentNetworkExecutor(MountingNailExtractionSnapshot snapshot)
    {
        if (extractionNetworkManager == null) return !IsSpawned
            && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening);
        if (!extractionNetworkManager.IsListening
            || !extractionNetworkManager.IsServer) return false;
        if (extractionExecutorNetworkObject == null) return false;
        return extractionExecutorNetworkObject.IsSpawned
            && extractionExecutorNetworkObject.NetworkManager == extractionNetworkManager
            && extractionExecutorNetworkObject.NetworkObjectId == snapshot.ExecutorNetworkObjectId
            && extractionExecutorNetworkObject.OwnerClientId == snapshot.ExecutorOwnerClientId
            && (snapshot.ExecutorOwnerClientId == NetworkManager.ServerClientId
                || extractionNetworkManager.ConnectedClients.ContainsKey(snapshot.ExecutorOwnerClientId));
    }

    private bool IsCurrentExtraction(Behaviour executor, MountingNailExtractionHandle handle)
    {
        MountingNailExtractionSnapshot snapshot = GetExtractionSnapshot();
        return handle.IsValid && snapshot.Active && handle.High == snapshot.HandleHigh
            && handle.Low == snapshot.HandleLow && executor != null && executor == extractionExecutor;
    }

    private bool IsExtractionTargetEligible()
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy || removalPending || !IsEmbeddedState(State)
            || !IsConnectionTargetAlive(true) || State == MountingNailState.EmbeddedPair && !IsConnectionTargetAlive(false))
            return false;
        return !BridgeDeckPanelSectionNetworkState.IsPanelProtectedFromExtraction(embeddedTarget)
            && (State != MountingNailState.EmbeddedPair
                || !BridgeDeckPanelSectionNetworkState.IsPanelProtectedFromExtraction(embeddedSecondTarget));
    }

    private void OnExtractionNailChanged(MountingNail _)
    {
        if (!extractionCommitting && CanMutateExtractionAuthoritatively() && GetExtractionSnapshot().Active)
            ClearExtractionReservation();
    }

    private void ClearExtractionReservation()
    {
        if (CanMutateExtractionAuthoritatively() && GetExtractionSnapshot().Active) WriteExtractionSnapshot(default);
        StateOrAttachmentChanged -= OnExtractionNailChanged;
        UnsubscribeExtractionPanels();
        extractionExecutor = null;
        extractionExecutorNetworkObject = null;
        extractionNetworkManager = null;
    }

    private void Update()
    {
        MountingNailExtractionSnapshot snapshot = GetExtractionSnapshot();
        if (!snapshot.Active || IsSpawned && !IsServer) return;
        if (!IsValidExecutor(extractionExecutor) || !IsCurrentNetworkExecutor(snapshot)
            || !IsExtractionTargetEligible()) ClearExtractionReservation();
    }

    private void SubscribeExtractionPanels()
    {
        UnsubscribeExtractionPanels();
        extractionFirstPanel = FindExtractionPanel(embeddedTarget);
        extractionSecondPanel = State == MountingNailState.EmbeddedPair ? FindExtractionPanel(embeddedSecondTarget) : null;
        if (extractionFirstPanel != null) extractionFirstPanel.AssessmentChanged += OnExtractionPanelChanged;
        if (extractionSecondPanel != null && extractionSecondPanel != extractionFirstPanel)
            extractionSecondPanel.AssessmentChanged += OnExtractionPanelChanged;
    }

    private void UnsubscribeExtractionPanels()
    {
        if (extractionFirstPanel != null) extractionFirstPanel.AssessmentChanged -= OnExtractionPanelChanged;
        if (extractionSecondPanel != null && extractionSecondPanel != extractionFirstPanel)
            extractionSecondPanel.AssessmentChanged -= OnExtractionPanelChanged;
        extractionFirstPanel = null;
        extractionSecondPanel = null;
    }

    private static BridgeDeckPanelMounting FindExtractionPanel(Transform target)
        => target != null ? target.GetComponentInParent<BridgeDeckPanelMounting>() : null;

    private void OnExtractionPanelChanged(BridgeDeckPanelMounting _)
    {
        if (!extractionCommitting && CanMutateExtractionAuthoritatively() && GetExtractionSnapshot().Active)
            ClearExtractionReservation();
    }

    private void OnExtractionLifecycleEnded()
    {
        if (IsSpawned && IsServer) extractionSnapshot.Value = default;
        else if (!IsNetworkSessionActive()
            && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            offlineExtractionSnapshot = default;
        StateOrAttachmentChanged -= OnExtractionNailChanged;
        UnsubscribeExtractionPanels();
        extractionExecutor = null;
        extractionExecutorNetworkObject = null;
        extractionNetworkManager = null;
    }

    private MountingNailExtractionSnapshot GetExtractionSnapshot()
        => IsSpawned ? extractionSnapshot.Value : offlineExtractionSnapshot;

    private void WriteExtractionSnapshot(MountingNailExtractionSnapshot snapshot)
    {
        if (IsSpawned)
        {
            if (IsServer) extractionSnapshot.Value = snapshot;
            return;
        }
        offlineExtractionSnapshot = snapshot;
    }

    private bool HasExtractionAttachment() => isActiveAndEnabled && gameObject.activeInHierarchy
        && !removalPending && IsEmbeddedState(State) && IsConnectionTargetAlive(true)
        && (State != MountingNailState.EmbeddedPair || IsConnectionTargetAlive(false));

    private bool IsExtractionTargetProtected() => HasExtractionAttachment()
        && (BridgeDeckPanelSectionNetworkState.IsPanelProtectedFromExtraction(embeddedTarget)
            || State == MountingNailState.EmbeddedPair
                && BridgeDeckPanelSectionNetworkState.IsPanelProtectedFromExtraction(embeddedSecondTarget));
}
