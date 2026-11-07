#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
using Unity.Netcode;
using UnityEngine;
public partial class ClientNetworkTransform
{
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
    private NetworkObject traceObject;
    private Vector3 traceCacheBefore;
    private Quaternion traceCacheRotationBefore;
    private int tracePositionMask;
    private int traceRotationMask;
    private bool traceReceive;
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

    partial void RecordReceiveBeforeBase(ref NetworkTransformState oldState, ref NetworkTransformState newState)
    {
        traceObject = NetworkObject;
        traceReceive = SingleCarryStateDivergenceTrace.Tracks(traceObject);
        traceCacheBefore = latestAuthoritativeSpacePosition;
        traceCacheRotationBefore = latestAuthoritativeSpaceRotation;
        tracePositionMask = (newState.HasPositionX ? 1 : 0) | (newState.HasPositionY ? 2 : 0) | (newState.HasPositionZ ? 4 : 0);
        traceRotationMask = (newState.HasRotAngleX ? 1 : 0) | (newState.HasRotAngleY ? 2 : 0)
            | (newState.HasRotAngleZ ? 4 : 0) | (newState.HasRotAngleChange ? 8 : 0);
        if (traceReceive)
            SingleCarryStateDivergenceTrace.RecordTransform(traceObject.NetworkManager, traceObject,
                editorLocalSpawnGeneration, SingleCarryStateDivergenceTrace.EventKind.ReceiveCallbackBeforeBase,
                newState.GetNetworkTick(), true, newState.GetPosition(), newState.GetRotation(), tracePositionMask,
                traceRotationMask, newState.QuaternionSync, CanCommitToTransform, IsOwner, editorInsideInitialSynchronization,
                newState.InLocalSpace, transform.position, transform.rotation, traceCacheBefore,
                traceCacheRotationBefore, traceCacheBefore, traceCacheRotationBefore, hasCompleteAuthoritativePose,
                UseUnreliableDeltas, 0);
    }

    partial void RecordReceiveAfterBase(ref NetworkTransformState newState)
    {
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
        if (traceReceive)
            SingleCarryStateDivergenceTrace.RecordTransform(traceObject.NetworkManager, traceObject,
                editorLocalSpawnGeneration, SingleCarryStateDivergenceTrace.EventKind.ReceiveCallbackAfterBase,
                newState.GetNetworkTick(), true, newState.GetPosition(), newState.GetRotation(), tracePositionMask,
                traceRotationMask, newState.QuaternionSync, CanCommitToTransform, IsOwner, editorInsideInitialSynchronization,
                newState.InLocalSpace, transform.position, transform.rotation, traceCacheBefore,
                traceCacheRotationBefore, latestAuthoritativeSpacePosition, latestAuthoritativeSpaceRotation,
                hasCompleteAuthoritativePose, UseUnreliableDeltas, 0);
    }

    partial void RecordPositionChange(ref NetworkTransformState newState)
    {
        editorReceiveDiagnostic.positionChangeCount++;
        editorReceiveDiagnostic.lastPositionChangeFrame = Time.frameCount;
        editorReceiveDiagnostic.lastPositionChangeRealtime = Time.realtimeSinceStartupAsDouble;
        editorReceiveDiagnostic.lastPositionReceivedNetworkTick = newState.GetNetworkTick();
    }

    partial void RecordRotationChange(ref NetworkTransformState newState)
    {
        editorReceiveDiagnostic.rotationChangeCount++;
        editorReceiveDiagnostic.lastRotationChangeFrame = Time.frameCount;
        editorReceiveDiagnostic.lastRotationChangeRealtime = Time.realtimeSinceStartupAsDouble;
        editorReceiveDiagnostic.lastRotationReceivedNetworkTick = newState.GetNetworkTick();
    }

    partial void RecordReceiveCacheUpdated(ref NetworkTransformState newState)
    {
        editorReceiveDiagnostic.hasCompletePose = true;
        if (traceReceive)
            SingleCarryStateDivergenceTrace.RecordTransform(traceObject.NetworkManager, traceObject,
                editorLocalSpawnGeneration, SingleCarryStateDivergenceTrace.EventKind.ReceiveCacheUpdated,
                newState.GetNetworkTick(), true, newState.GetPosition(), newState.GetRotation(), tracePositionMask,
                traceRotationMask, newState.QuaternionSync, CanCommitToTransform, IsOwner, editorInsideInitialSynchronization,
                newState.InLocalSpace, transform.position, transform.rotation, traceCacheBefore,
                traceCacheRotationBefore, latestAuthoritativeSpacePosition, latestAuthoritativeSpaceRotation,
                hasCompleteAuthoritativePose, UseUnreliableDeltas, 0);
    }

    partial void RecordReceiveCacheCleared()
    {
        NetworkObject current = NetworkObject;
        if (SingleCarryStateDivergenceTrace.Tracks(current))
            SingleCarryStateDivergenceTrace.RecordTransform(current.NetworkManager, current,
                editorLocalSpawnGeneration, SingleCarryStateDivergenceTrace.EventKind.ReceiveCacheCleared,
                0, false, latestAuthoritativeSpacePosition, latestAuthoritativeSpaceRotation, 0, 0,
                UseQuaternionSynchronization, CanCommitToTransform, IsOwner, editorInsideInitialSynchronization, InLocalSpace,
                transform.position, transform.rotation, latestAuthoritativeSpacePosition,
                latestAuthoritativeSpaceRotation, default, Quaternion.identity, false, UseUnreliableDeltas, 0);
        editorReceiveDiagnostic.hasCompletePose = false;
    }

    partial void RecordDiagnosticSpawn() => editorLocalSpawnGeneration++;

    protected override void OnAuthorityPushTransformState(ref NetworkTransformState state)
    {
        if (SingleCarryStateDivergenceTrace.Tracks(NetworkObject))
        {
            NetworkObject current = NetworkObject;
            int positionMask = (state.HasPositionX ? 1 : 0) | (state.HasPositionY ? 2 : 0) | (state.HasPositionZ ? 4 : 0);
            int rotationMask = (state.HasRotAngleX ? 1 : 0) | (state.HasRotAngleY ? 2 : 0)
                | (state.HasRotAngleZ ? 4 : 0) | (state.HasRotAngleChange ? 8 : 0);
            SingleCarryStateDivergenceTrace.RecordTransform(current.NetworkManager, current,
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
}
#endif
