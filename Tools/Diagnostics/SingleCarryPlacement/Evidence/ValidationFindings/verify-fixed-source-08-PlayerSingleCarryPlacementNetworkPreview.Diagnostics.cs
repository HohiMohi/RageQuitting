#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
using Unity.Netcode;
using UnityEngine;
public sealed partial class PlayerSingleCarryPlacementNetworkPreview
{
    [System.Serializable]
    public struct EditorAimValidationDiagnostic
    {
        public string branch;
        public ulong requestId;
        public ulong senderClientId;
        public ulong playerNetworkObjectId;
        public ulong playerOwnerClientId;
        public ulong sourceNetworkObjectId;
        public bool sourceResolved;
        public int serverFrame;
        public double serverRealtime;
        public float serverDeltaTime;
        public float serverUnscaledDeltaTime;
        public int serverNetworkTick;
        public bool aimAnchorExists;
        public bool clientNetworkTransformExists;
        public bool receivedPoseAvailable;
        public bool trustedAimAnchorCalculated;
        public bool residualCalculated;
        public Vector3 authoritativePlayerPosition;
        public Quaternion authoritativePlayerRotation;
        public Vector3 renderedPlayerPosition;
        public Quaternion renderedPlayerRotation;
        public Vector3 anchorLocalOffset;
        public Vector3 playerLocalScale;
        public Vector3 trustedAimAnchor;
        public Vector3 rayOrigin;
        public Vector3 rayDirection;
        public float forwardOffset;
        public Vector3 lateralOffset;
        public float nearestAllowedForwardOffset;
        public float residualSquared;
        public float cameraNearClipDistance;
        public float maximumAimOriginResidual;
        public bool hasFinalResult;
        public int finalResult;
        public ClientNetworkTransform.EditorReceiveDiagnostic receive;
    }

    private EditorAimValidationDiagnostic editorAimValidationDiagnostic;
    public EditorAimValidationDiagnostic GetEditorAimValidationDiagnostic() => editorAimValidationDiagnostic;

    partial void RecordDiagnosticPlacementResponse(ulong requestId, SingleCarryPlacementNetworkResult result)
    {
        if (IsOwner && IsSpawned && NetworkManager != null && SingleCarryStateDivergenceTrace.Tracks(NetworkObject))
            SingleCarryStateDivergenceTrace.RecordResponse(NetworkManager, NetworkObject,
                SingleCarryStateDivergenceTrace.GetLocalSpawnGeneration(NetworkObject), requestId, (int)result, 0);
    }

    partial void RecordDiagnosticFinalResult(SingleCarryPlacementNetworkResult result)
    {
        editorAimValidationDiagnostic.finalResult = (int)result;
        editorAimValidationDiagnostic.hasFinalResult = true;
        if (!SingleCarryStateDivergenceTrace.Tracks(NetworkObject)) return;
        int generation = SingleCarryStateDivergenceTrace.GetLocalSpawnGeneration(NetworkObject);
        int availability = (editorAimValidationDiagnostic.receivedPoseAvailable ? 1 : 0)
            | (editorAimValidationDiagnostic.trustedAimAnchorCalculated ? 2 : 0)
            | (editorAimValidationDiagnostic.residualCalculated ? 4 : 0);
        Vector3 validatedPose = editorAimValidationDiagnostic.receivedPoseAvailable
            ? editorAimValidationDiagnostic.authoritativePlayerPosition
            : editorAimValidationDiagnostic.renderedPlayerPosition;
        Quaternion validatedRotation = editorAimValidationDiagnostic.receivedPoseAvailable
            ? editorAimValidationDiagnostic.authoritativePlayerRotation
            : editorAimValidationDiagnostic.renderedPlayerRotation;
        SingleCarryStateDivergenceTrace.RecordValidation(NetworkManager, NetworkObject, generation,
            editorAimValidationDiagnostic.requestId, editorAimValidationDiagnostic.receive.lastReceivedNetworkTick,
            editorAimValidationDiagnostic.receive.receiveCount > 0, validatedPose, validatedRotation,
            editorAimValidationDiagnostic.trustedAimAnchor, editorAimValidationDiagnostic.rayOrigin,
            editorAimValidationDiagnostic.rayDirection,
            editorAimValidationDiagnostic.lateralOffset + editorAimValidationDiagnostic.rayDirection
                * (editorAimValidationDiagnostic.forwardOffset - editorAimValidationDiagnostic.nearestAllowedForwardOffset),
            editorAimValidationDiagnostic.residualSquared, Animator.StringToHash(editorAimValidationDiagnostic.branch ?? ""),
            (int)result, editorAimValidationDiagnostic.receive.hasCompletePose, availability);
    }

    partial void InitializeDiagnosticValidation(ulong requestId, ulong senderClientId, Ray aimRay)
    {
        editorAimValidationDiagnostic = new EditorAimValidationDiagnostic
        {
            branch = "NotReached",
            requestId = requestId,
            senderClientId = senderClientId,
            playerNetworkObjectId = NetworkObject != null ? NetworkObject.NetworkObjectId : 0,
            playerOwnerClientId = OwnerClientId,
            serverFrame = Time.frameCount,
            serverRealtime = Time.realtimeSinceStartupAsDouble,
            serverDeltaTime = Time.deltaTime,
            serverUnscaledDeltaTime = Time.unscaledDeltaTime,
            serverNetworkTick = NetworkManager != null ? NetworkManager.ServerTime.Tick : 0,
            rayOrigin = aimRay.origin,
            rayDirection = aimRay.direction,
            cameraNearClipDistance = 0.2f,
            maximumAimOriginResidual = 0.15f
        };
    }

    partial void RecordDiagnosticSource(NetworkObject source)
    {
        editorAimValidationDiagnostic.sourceResolved = true;
        editorAimValidationDiagnostic.sourceNetworkObjectId = source.NetworkObjectId;
    }

    partial void RecordDiagnosticPlayerSnapshot(NetworkObject player, Transform aimAnchor)
    {
        editorAimValidationDiagnostic.aimAnchorExists = aimAnchor != null;
        editorAimValidationDiagnostic.renderedPlayerPosition = player.transform.position;
        editorAimValidationDiagnostic.renderedPlayerRotation = player.transform.rotation;
        editorAimValidationDiagnostic.playerNetworkObjectId = player.NetworkObjectId;
        editorAimValidationDiagnostic.playerOwnerClientId = player.OwnerClientId;
    }

    partial void RecordDiagnosticBranch(string branch) => editorAimValidationDiagnostic.branch = branch;

    partial void RecordDiagnosticReceiveSnapshot(ClientNetworkTransform playerTransform)
    {
        editorAimValidationDiagnostic.clientNetworkTransformExists = true;
        editorAimValidationDiagnostic.receive = playerTransform.GetEditorReceiveDiagnostic();
    }

    partial void RecordDiagnosticReceivedPose(bool available, Vector3 position, Quaternion rotation)
    {
        editorAimValidationDiagnostic.receivedPoseAvailable = available;
        editorAimValidationDiagnostic.authoritativePlayerPosition = position;
        editorAimValidationDiagnostic.authoritativePlayerRotation = rotation;
    }

    partial void RecordDiagnosticTrustedAnchor(Vector3 localOffset, Vector3 localScale, Vector3 trustedAnchor)
    {
        editorAimValidationDiagnostic.trustedAimAnchorCalculated = true;
        editorAimValidationDiagnostic.anchorLocalOffset = localOffset;
        editorAimValidationDiagnostic.playerLocalScale = localScale;
        editorAimValidationDiagnostic.trustedAimAnchor = trustedAnchor;
    }

    partial void RecordDiagnosticResidual(Ray aimRay, float forwardOffset, Vector3 lateralOffset,
        float nearestAllowedForwardOffset, float residualSquared, float cameraNearClipDistance, float maximumAimOriginResidual)
    {
        editorAimValidationDiagnostic.residualCalculated = true;
        editorAimValidationDiagnostic.rayOrigin = aimRay.origin;
        editorAimValidationDiagnostic.rayDirection = aimRay.direction;
        editorAimValidationDiagnostic.forwardOffset = forwardOffset;
        editorAimValidationDiagnostic.lateralOffset = lateralOffset;
        editorAimValidationDiagnostic.nearestAllowedForwardOffset = nearestAllowedForwardOffset;
        editorAimValidationDiagnostic.residualSquared = residualSquared;
        editorAimValidationDiagnostic.cameraNearClipDistance = cameraNearClipDistance;
        editorAimValidationDiagnostic.maximumAimOriginResidual = maximumAimOriginResidual;
    }
}
#endif
