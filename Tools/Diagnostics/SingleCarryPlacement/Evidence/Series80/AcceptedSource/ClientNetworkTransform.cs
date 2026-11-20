using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class ClientNetworkTransform : NetworkTransform
{
#if UNITY_EDITOR
    private int editorLocalSpawnGeneration;
    private bool editorInsideInitialSynchronization;
    public int EditorLocalSpawnGeneration => editorLocalSpawnGeneration;

    [System.Serializable]
    public struct EditorReceiveDiagnostic
    {
        public int receiveCount;
        public int positionChangeCount;
        public int rotationChangeCount;
        public int lastReceiveFrame;
        public double lastReceiveRealtime;
        public float receiveDeltaTime;
        public float receiveUnscaledDeltaTime;
        public int lastPositionChangeFrame;
        public double lastPositionChangeRealtime;
        public int lastRotationChangeFrame;
        public double lastRotationChangeRealtime;
        public int lastReceivedNetworkTick;
        public ulong networkObjectId;
        public bool hasNetworkObjectId;
        public ulong ownerClientId;
        public int parentInstanceId;
        public ulong sampledObjectId;
        public bool hasSampledObjectId;
        public ulong sampledOwnerClientId;
        public int sampledParentInstanceId;
        public bool inLocalSpace;
        public bool currentInLocalSpace;
        public bool sampledInLocalSpace;
        public Vector3 cachedAuthoritativeSpacePosition;
        public Quaternion cachedAuthoritativeSpaceRotation;
        public int lastPositionReceivedNetworkTick;
        public int lastRotationReceivedNetworkTick;
        public bool hasCompletePose;
        public bool sampledContextMatches;
        public bool isAuthority;
        public bool isSpawned;
    }

    private EditorReceiveDiagnostic editorReceiveDiagnostic;
    public EditorReceiveDiagnostic GetEditorReceiveDiagnostic()
    {
        EditorReceiveDiagnostic diagnostic = editorReceiveDiagnostic;
        diagnostic.networkObjectId = NetworkObject != null ? NetworkObject.NetworkObjectId : 0;
        diagnostic.hasNetworkObjectId = NetworkObject != null;
        diagnostic.ownerClientId = NetworkObject != null ? NetworkObject.OwnerClientId : 0;
        diagnostic.parentInstanceId = transform.parent != null ? transform.parent.GetInstanceID() : 0;
        diagnostic.sampledObjectId = sampleObject != null ? sampleObject.NetworkObjectId : 0;
        diagnostic.hasSampledObjectId = sampleObject != null;
        diagnostic.sampledOwnerClientId = sampleOwnerClientId;
        diagnostic.sampledParentInstanceId = sampleParent != null ? sampleParent.GetInstanceID() : 0;
        diagnostic.sampledInLocalSpace = sampleInLocalSpace;
        diagnostic.currentInLocalSpace = InLocalSpace;
        diagnostic.cachedAuthoritativeSpacePosition = latestAuthoritativeSpacePosition;
        diagnostic.cachedAuthoritativeSpaceRotation = latestAuthoritativeSpaceRotation;
        diagnostic.hasCompletePose = hasCompleteAuthoritativePose;
        diagnostic.sampledContextMatches = hasCompleteAuthoritativePose && NetworkObject != null
            && sampleObject == NetworkObject && sampleOwnerClientId == NetworkObject.OwnerClientId
            && sampleParent == transform.parent && sampleInLocalSpace == InLocalSpace;
        diagnostic.isAuthority = CanCommitToTransform;
        diagnostic.isSpawned = IsSpawned;
        return diagnostic;
    }
#endif
    private Vector3 latestAuthoritativeSpacePosition;
    private Quaternion latestAuthoritativeSpaceRotation;
    private NetworkObject sampleObject;
    private ulong sampleOwnerClientId;
    private Transform sampleParent;
    private bool sampleInLocalSpace;
    private bool hasCompleteAuthoritativePose;

    protected override bool OnIsServerAuthoritative() => false;

    /// <summary>Returns the latest complete pose received from this transform's authority.</summary>
    public bool TryGetLatestAuthoritativeWorldPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = default;
        if (!IsSpawned)
            return false;

        if (CanCommitToTransform)
        {
            position = transform.position;
            rotation = transform.rotation;
            if (IsFinite(position) && IsUsable(rotation))
                return true;

            position = default;
            rotation = default;
            return false;
        }

        if (!hasCompleteAuthoritativePose || NetworkObject == null
            || sampleObject != NetworkObject
            || sampleOwnerClientId != NetworkObject.OwnerClientId
            || sampleParent != transform.parent
            || sampleInLocalSpace != InLocalSpace)
        {
            ClearAuthoritativePose();
            return false;
        }

        Transform parentTransform = sampleInLocalSpace ? transform.parent : null;
        position = parentTransform != null
            ? parentTransform.TransformPoint(latestAuthoritativeSpacePosition)
            : latestAuthoritativeSpacePosition;
        rotation = parentTransform != null
            ? parentTransform.rotation * latestAuthoritativeSpaceRotation
            : latestAuthoritativeSpaceRotation;
        if (!IsFinite(position) || !IsUsable(rotation))
        {
            position = default;
            rotation = default;
            ClearAuthoritativePose();
            return false;
        }

        return true;
    }

    protected override void OnNetworkTransformStateUpdated(ref NetworkTransformState oldState, ref NetworkTransformState newState)
    {
#if UNITY_EDITOR
        NetworkObject traceObject = NetworkObject;
        bool trace = SingleCarryStateDivergenceTrace.Tracks(traceObject);
        Vector3 traceCacheBefore = latestAuthoritativeSpacePosition;
        Quaternion traceCacheRotationBefore = latestAuthoritativeSpaceRotation;
        int positionMask = (newState.HasPositionX ? 1 : 0) | (newState.HasPositionY ? 2 : 0) | (newState.HasPositionZ ? 4 : 0);
        int rotationMask = (newState.HasRotAngleX ? 1 : 0) | (newState.HasRotAngleY ? 2 : 0)
            | (newState.HasRotAngleZ ? 4 : 0) | (newState.HasRotAngleChange ? 8 : 0);
        if (trace)
            SingleCarryStateDivergenceTrace.RecordTransform(traceObject.NetworkManager, traceObject,
                editorLocalSpawnGeneration, SingleCarryStateDivergenceTrace.EventKind.ReceiveCallbackBeforeBase,
                newState.GetNetworkTick(), true, newState.GetPosition(), newState.GetRotation(), positionMask,
                rotationMask, newState.QuaternionSync, CanCommitToTransform, IsOwner, editorInsideInitialSynchronization,
                newState.InLocalSpace, transform.position, transform.rotation, traceCacheBefore,
                traceCacheRotationBefore, traceCacheBefore, traceCacheRotationBefore, hasCompleteAuthoritativePose,
                UseUnreliableDeltas, 0);
#endif
        base.OnNetworkTransformStateUpdated(ref oldState, ref newState);
#if UNITY_EDITOR
        editorReceiveDiagnostic.receiveCount++;
        editorReceiveDiagnostic.lastReceiveFrame = Time.frameCount;
        editorReceiveDiagnostic.lastReceiveRealtime = Time.realtimeSinceStartupAsDouble;
        editorReceiveDiagnostic.receiveDeltaTime = Time.deltaTime;
        editorReceiveDiagnostic.receiveUnscaledDeltaTime = Time.unscaledDeltaTime;
        editorReceiveDiagnostic.lastReceivedNetworkTick = newState.GetNetworkTick();
        editorReceiveDiagnostic.networkObjectId = NetworkObject != null ? NetworkObject.NetworkObjectId : 0;
        editorReceiveDiagnostic.hasNetworkObjectId = NetworkObject != null;
        editorReceiveDiagnostic.ownerClientId = NetworkObject != null ? NetworkObject.OwnerClientId : 0;
        editorReceiveDiagnostic.parentInstanceId = transform.parent != null ? transform.parent.GetInstanceID() : 0;
        editorReceiveDiagnostic.inLocalSpace = newState.InLocalSpace;
        editorReceiveDiagnostic.isAuthority = CanCommitToTransform;
        editorReceiveDiagnostic.isSpawned = IsSpawned;
        if (trace)
        {
            SingleCarryStateDivergenceTrace.RecordTransform(traceObject.NetworkManager, traceObject,
                editorLocalSpawnGeneration, SingleCarryStateDivergenceTrace.EventKind.ReceiveCallbackAfterBase,
                newState.GetNetworkTick(), true, newState.GetPosition(), newState.GetRotation(), positionMask,
                rotationMask, newState.QuaternionSync, CanCommitToTransform, IsOwner, editorInsideInitialSynchronization,
                newState.InLocalSpace, transform.position, transform.rotation, traceCacheBefore,
                traceCacheRotationBefore, latestAuthoritativeSpacePosition, latestAuthoritativeSpaceRotation,
                hasCompleteAuthoritativePose, UseUnreliableDeltas, 0);
        }
#endif
        if (NetworkObject == null)
            return;

        bool identityMatches = hasCompleteAuthoritativePose
            && sampleObject == NetworkObject
            && sampleOwnerClientId == NetworkObject.OwnerClientId
            && sampleParent == transform.parent
            && sampleInLocalSpace == newState.InLocalSpace;
        if (hasCompleteAuthoritativePose && !identityMatches)
            ClearAuthoritativePose();

        if (!hasCompleteAuthoritativePose)
        {
            bool positionIsComplete = newState.HasPositionX && newState.HasPositionY && newState.HasPositionZ;
            bool rotationIsComplete = newState.QuaternionSync
                ? newState.HasRotAngleChange
                : newState.HasRotAngleX && newState.HasRotAngleY && newState.HasRotAngleZ;
            // A complete packet from the current owner is a valid basis even when NGO
            // does not mark it as an initial synchronization or teleport (for example,
            // full-precision ownership and coordinate-space transitions).
            if (!positionIsComplete || !rotationIsComplete)
                return;
        }

        if (newState.HasPositionChange)
        {
            latestAuthoritativeSpacePosition = newState.GetPosition();
#if UNITY_EDITOR
            editorReceiveDiagnostic.positionChangeCount++;
            editorReceiveDiagnostic.lastPositionChangeFrame = Time.frameCount;
            editorReceiveDiagnostic.lastPositionChangeRealtime = Time.realtimeSinceStartupAsDouble;
            editorReceiveDiagnostic.lastPositionReceivedNetworkTick = newState.GetNetworkTick();
#endif
        }
        if (newState.HasRotAngleChange)
        {
            latestAuthoritativeSpaceRotation = newState.GetRotation();
#if UNITY_EDITOR
            editorReceiveDiagnostic.rotationChangeCount++;
            editorReceiveDiagnostic.lastRotationChangeFrame = Time.frameCount;
            editorReceiveDiagnostic.lastRotationChangeRealtime = Time.realtimeSinceStartupAsDouble;
            editorReceiveDiagnostic.lastRotationReceivedNetworkTick = newState.GetNetworkTick();
#endif
        }

        if (!IsFinite(latestAuthoritativeSpacePosition) || !IsUsable(latestAuthoritativeSpaceRotation))
        {
            ClearAuthoritativePose();
            return;
        }

        sampleObject = NetworkObject;
        sampleOwnerClientId = NetworkObject.OwnerClientId;
        sampleParent = transform.parent;
        sampleInLocalSpace = newState.InLocalSpace;
        hasCompleteAuthoritativePose = true;
#if UNITY_EDITOR
        editorReceiveDiagnostic.hasCompletePose = true;
        if (trace)
            SingleCarryStateDivergenceTrace.RecordTransform(traceObject.NetworkManager, traceObject,
                editorLocalSpawnGeneration, SingleCarryStateDivergenceTrace.EventKind.ReceiveCacheUpdated,
                newState.GetNetworkTick(), true, newState.GetPosition(), newState.GetRotation(), positionMask,
                rotationMask, newState.QuaternionSync, CanCommitToTransform, IsOwner, editorInsideInitialSynchronization,
                newState.InLocalSpace, transform.position, transform.rotation, traceCacheBefore,
                traceCacheRotationBefore, latestAuthoritativeSpacePosition, latestAuthoritativeSpaceRotation,
                hasCompleteAuthoritativePose, UseUnreliableDeltas, 0);
#endif
    }

#if UNITY_EDITOR
    protected override void OnAuthorityPushTransformState(ref NetworkTransformState state)
    {
        if (SingleCarryStateDivergenceTrace.Tracks(NetworkObject))
        {
            NetworkObject traceObject = NetworkObject;
            int positionMask = (state.HasPositionX ? 1 : 0) | (state.HasPositionY ? 2 : 0) | (state.HasPositionZ ? 4 : 0);
            int rotationMask = (state.HasRotAngleX ? 1 : 0) | (state.HasRotAngleY ? 2 : 0)
                | (state.HasRotAngleZ ? 4 : 0) | (state.HasRotAngleChange ? 8 : 0);
            SingleCarryStateDivergenceTrace.RecordTransform(traceObject.NetworkManager, traceObject,
                editorLocalSpawnGeneration, SingleCarryStateDivergenceTrace.EventKind.AuthorityPush,
                state.GetNetworkTick(), true, state.GetPosition(), state.GetRotation(), positionMask, rotationMask,
                state.QuaternionSync, CanCommitToTransform, IsOwner, editorInsideInitialSynchronization, state.InLocalSpace,
                transform.position, transform.rotation, latestAuthoritativeSpacePosition,
                latestAuthoritativeSpaceRotation, latestAuthoritativeSpacePosition,
                latestAuthoritativeSpaceRotation, hasCompleteAuthoritativePose, UseUnreliableDeltas,
                state.IsTeleportingNextFrame ? 1 : 0);
        }
        base.OnAuthorityPushTransformState(ref state);
    }

    protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
    {
        editorInsideInitialSynchronization = true;
        try { base.OnSynchronize(ref serializer); }
        finally { editorInsideInitialSynchronization = false; }
    }
#endif

    public override void OnNetworkSpawn()
    {
#if UNITY_EDITOR
        editorLocalSpawnGeneration++;
#endif
        // A valid initial synchronization callback can precede OnNetworkSpawn. Preserve that sample.
        if (hasCompleteAuthoritativePose && (sampleObject != NetworkObject || sampleOwnerClientId != NetworkObject.OwnerClientId))
            ClearAuthoritativePose();
        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        ClearAuthoritativePose();
        base.OnNetworkDespawn();
    }

    protected override void OnOwnershipChanged(ulong previous, ulong current)
    {
        ClearAuthoritativePose();
        base.OnOwnershipChanged(previous, current);
    }

    public override void OnNetworkObjectParentChanged(NetworkObject parentNetworkObject)
    {
        ClearAuthoritativePose();
        base.OnNetworkObjectParentChanged(parentNetworkObject);
    }

    private void ClearAuthoritativePose()
    {
#if UNITY_EDITOR
        NetworkObject traceObject = NetworkObject;
        if (SingleCarryStateDivergenceTrace.Tracks(traceObject))
            SingleCarryStateDivergenceTrace.RecordTransform(traceObject.NetworkManager, traceObject,
                editorLocalSpawnGeneration, SingleCarryStateDivergenceTrace.EventKind.ReceiveCacheCleared,
                0, false, latestAuthoritativeSpacePosition, latestAuthoritativeSpaceRotation, 0, 0,
                UseQuaternionSynchronization, CanCommitToTransform, IsOwner, editorInsideInitialSynchronization, InLocalSpace,
                transform.position, transform.rotation, latestAuthoritativeSpacePosition,
                latestAuthoritativeSpaceRotation, default, Quaternion.identity, false, UseUnreliableDeltas, 0);
#endif
        latestAuthoritativeSpacePosition = default;
        latestAuthoritativeSpaceRotation = default;
        sampleObject = null;
        sampleOwnerClientId = 0;
        sampleParent = null;
        sampleInLocalSpace = false;
        hasCompleteAuthoritativePose = false;
#if UNITY_EDITOR
        editorReceiveDiagnostic.hasCompletePose = false;
#endif
    }

    private static bool IsFinite(Vector3 value) =>
        IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

    private static bool IsUsable(Quaternion value) =>
        IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w)
        && value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > 0.000001f;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
