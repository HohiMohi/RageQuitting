using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(Collider))]
public class WheelbarrowDockingStation : NetworkBehaviour, IConcreteBatchReceiver
{
    [SerializeField] private WheelbarrowDockType dockType;
    [SerializeField] private Transform targetPose;
    [SerializeField] private BridgeConstructionSite foundationSite;
    [SerializeField] private WheelbarrowPouringMinigame pouringMinigame;
    [Header("Docking")]
    [SerializeField, Min(0f)] private float maximumCaptureSpeed = 1.2f;

    private readonly NetworkVariable<ulong> dockedWheelbarrowNetwork =
        new NetworkVariable<ulong>(WheelbarrowController.NoClient);

    private WheelbarrowController docked;
    private Collider captureVolume;
    private ulong localDockedWheelbarrowId = WheelbarrowController.NoClient;

    public WheelbarrowDockType DockType => dockType;
    public Transform TargetPose => targetPose;
    public Collider CaptureVolume => captureVolume != null ? captureVolume : captureVolume = GetComponent<Collider>();
    public WheelbarrowController DockedWheelbarrow => ResolveDockedWheelbarrow();
    public ulong DockedWheelbarrowNetworkObjectId => IsNetworkSessionActive()
        ? dockedWheelbarrowNetwork.Value
        : localDockedWheelbarrowId;
    public BridgeConstructionSite FoundationSite => foundationSite;
    public bool CanReceiveConcreteBatch => dockType == WheelbarrowDockType.MixerLoading &&
        DockedWheelbarrow != null && DockedWheelbarrow.IsDockSecured &&
        ContainsPoint(DockedWheelbarrow.transform.position) && DockedWheelbarrow.CanReceiveConcreteBatch;
    private bool HasAuthority => !IsNetworkSessionActive() || IsServer;

    private void Awake()
    {
        captureVolume = GetComponent<Collider>();
        if (GetComponent<WheelbarrowDockingVisualizer>() == null)
            gameObject.AddComponent<WheelbarrowDockingVisualizer>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            docked = null;
            dockedWheelbarrowNetwork.Value = WheelbarrowController.NoClient;
        }
    }

    public override void OnNetworkDespawn()
    {
        ForceReleaseCurrentWheelbarrow();
        if (HasAuthority)
            CleanupMissingTrackedWheelbarrow();
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!HasAuthority || DockedWheelbarrowNetworkObjectId == WheelbarrowController.NoClient) return;
        if (DockedWheelbarrow == null)
            CleanupMissingTrackedWheelbarrow();
    }

    private void Reset()
    {
        Collider volume = GetComponent<Collider>();
        volume.isTrigger = true;
    }

    public bool IsCompatibleWith(WheelbarrowController wheelbarrow)
    {
        return wheelbarrow != null && targetPose != null && !IsOccupiedByOther(wheelbarrow) &&
            IsPayloadCompatible(wheelbarrow);
    }

    public bool EvaluateDriverDockingReadiness(WheelbarrowController wheelbarrow)
    {
        float maximumReleaseSpeed = wheelbarrow != null && wheelbarrow.Profile != null
            ? wheelbarrow.Profile.MaximumExitSpeed
            : 0.8f;
        return IsCompatibleWith(wheelbarrow) &&
            wheelbarrow.State == WheelbarrowState.Driven &&
            wheelbarrow.DriverClientId != WheelbarrowController.NoClient &&
            wheelbarrow.Speed <= Mathf.Min(maximumCaptureSpeed, maximumReleaseSpeed) &&
            ContainsPoint(wheelbarrow.transform.position);
    }

    public bool CanDriverReleaseForDocking(WheelbarrowController wheelbarrow) =>
        EvaluateDriverDockingReadiness(wheelbarrow);

    public bool TryDockImmediately(WheelbarrowController wheelbarrow, ulong clientId)
    {
        if (!HasAuthority || DockedWheelbarrow != null || wheelbarrow == null ||
            wheelbarrow.DriverClientId != clientId || !EvaluateDriverDockingReadiness(wheelbarrow)) return false;
        if (!wheelbarrow.DockImmediately(this, targetPose, clientId)) return false;
        SetDockedWheelbarrow(wheelbarrow);
        pouringMinigame?.Bind(this, wheelbarrow, foundationSite);
        return true;
    }

    public bool ContainsPoint(Vector3 worldPoint)
    {
        Collider volume = CaptureVolume;
        if (volume == null || !volume.enabled) return false;
        if (volume is BoxCollider box)
        {
            Vector3 local = box.transform.InverseTransformPoint(worldPoint) - box.center;
            Vector3 half = box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
        return (volume.ClosestPoint(worldPoint) - worldPoint).sqrMagnitude <= 0.0001f;
    }

    public bool TryReceiveConcreteBatch(ConcreteMixerController source)
    {
        WheelbarrowController wheelbarrow = DockedWheelbarrow;
        return HasAuthority && CanReceiveConcreteBatch && wheelbarrow != null &&
            wheelbarrow.TryReceiveConcreteBatch(source);
    }

    internal bool ReleaseWheelbarrowForDriver(WheelbarrowController wheelbarrow, ulong clientId)
    {
        if (!HasAuthority || wheelbarrow == null || docked != wheelbarrow ||
            wheelbarrow.DriverClientId != clientId || wheelbarrow.State == WheelbarrowState.Pouring ||
            pouringMinigame != null && pouringMinigame.State == WheelbarrowPouringState.Active) return false;

        pouringMinigame?.CancelAndRelease(false);
        SetDockedWheelbarrow(null);
        return true;
    }

    public void ForceReleaseWheelbarrow(WheelbarrowController wheelbarrow)
    {
        if (!HasAuthority || wheelbarrow == null || DockedWheelbarrow != wheelbarrow) return;
        foundationSite?.ForceCleanupConcreteFailure(wheelbarrow, this);
        pouringMinigame?.CancelAndRelease(false);
        if (DockedWheelbarrow == wheelbarrow) SetDockedWheelbarrow(null);
        wheelbarrow.ForceReleaseDock(this);
    }

    internal void CompleteFailedConcreteRecovery(WheelbarrowController wheelbarrow)
    {
        if (!HasAuthority || wheelbarrow == null || DockedWheelbarrow != wheelbarrow) return;
        pouringMinigame?.ResetAfterFailedConcreteRecovery();
        SetDockedWheelbarrow(null);
    }

    private bool IsPayloadCompatible(WheelbarrowController wheelbarrow)
    {
        if (dockType == WheelbarrowDockType.MixerLoading)
            return !wheelbarrow.HasConcrete && !wheelbarrow.HasResourceCargo;
        return dockType == WheelbarrowDockType.FoundationPouring && wheelbarrow.HasPourableConcrete &&
            foundationSite != null && foundationSite.CanAcceptFoundationDock;
    }

    private bool IsOccupiedByOther(WheelbarrowController wheelbarrow)
    {
        WheelbarrowController occupied = DockedWheelbarrow;
        if (occupied != null) return occupied != wheelbarrow;
        ulong occupiedId = DockedWheelbarrowNetworkObjectId;
        return occupiedId != WheelbarrowController.NoClient &&
            (!wheelbarrow.IsSpawned || occupiedId != wheelbarrow.NetworkObjectId);
    }

    private WheelbarrowController ResolveDockedWheelbarrow()
    {
        ulong id = DockedWheelbarrowNetworkObjectId;
        if (!IsNetworkSessionActive()) return docked;
        if (id == WheelbarrowController.NoClient)
        {
            docked = null;
            return null;
        }
        if (docked != null && docked.IsSpawned && docked.NetworkObjectId == id) return docked;
        docked = null;
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject networkObject))
            networkObject.TryGetComponent(out docked);
        return docked;
    }

    private void SetDockedWheelbarrow(WheelbarrowController wheelbarrow)
    {
        docked = wheelbarrow;
        ulong id = wheelbarrow != null && wheelbarrow.IsSpawned
            ? wheelbarrow.NetworkObjectId
            : WheelbarrowController.NoClient;
        localDockedWheelbarrowId = id;
        if (IsNetworkSessionActive() && IsServer) dockedWheelbarrowNetwork.Value = id;
    }

    private void ForceReleaseCurrentWheelbarrow()
    {
        if (!HasAuthority) return;
        WheelbarrowController current = DockedWheelbarrow;
        if (current != null)
        {
            ForceReleaseWheelbarrow(current);
            return;
        }
        CleanupMissingTrackedWheelbarrow();
    }

    internal void CleanupMissingTrackedWheelbarrow()
    {
        if (!HasAuthority) return;
        bool hasTrackedId = DockedWheelbarrowNetworkObjectId != WheelbarrowController.NoClient;
        if (!hasTrackedId && (foundationSite == null || !foundationSite.HasActiveConcreteFailure)) return;

        foundationSite?.ForceCleanupConcreteFailure(null, this);
        pouringMinigame?.ResetAfterFailedConcreteRecovery();
        SetDockedWheelbarrow(null);
    }

    private bool IsNetworkSessionActive() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
}
