using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public partial class ClientNetworkTransform : NetworkTransform
{
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
    partial void RecordReceiveBeforeBase(ref NetworkTransformState oldState, ref NetworkTransformState newState);
    partial void RecordReceiveAfterBase(ref NetworkTransformState newState);
    partial void RecordPositionChange(ref NetworkTransformState newState);
    partial void RecordRotationChange(ref NetworkTransformState newState);
    partial void RecordReceiveCacheUpdated(ref NetworkTransformState newState);
    partial void RecordReceiveCacheCleared();
    partial void RecordDiagnosticSpawn();
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
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        RecordReceiveBeforeBase(ref oldState, ref newState);
#endif
        base.OnNetworkTransformStateUpdated(ref oldState, ref newState);
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        RecordReceiveAfterBase(ref newState);
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
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
            RecordPositionChange(ref newState);
#endif
        }
        if (newState.HasRotAngleChange)
        {
            latestAuthoritativeSpaceRotation = newState.GetRotation();
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
            RecordRotationChange(ref newState);
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
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        RecordReceiveCacheUpdated(ref newState);
#endif
    }

    public override void OnNetworkSpawn()
    {
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        RecordDiagnosticSpawn();
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
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        RecordReceiveCacheCleared();
#endif
        latestAuthoritativeSpacePosition = default;
        latestAuthoritativeSpaceRotation = default;
        sampleObject = null;
        sampleOwnerClientId = 0;
        sampleParent = null;
        sampleInLocalSpace = false;
        hasCompleteAuthoritativePose = false;
    }

    private static bool IsFinite(Vector3 value) =>
        IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

    private static bool IsUsable(Quaternion value) =>
        IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w)
        && value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > 0.000001f;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
