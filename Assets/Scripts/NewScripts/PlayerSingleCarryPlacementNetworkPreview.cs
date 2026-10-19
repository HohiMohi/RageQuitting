using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public enum SingleCarryPlacementNetworkResult
{
    Accepted,
    InvalidRequest,
    StaleRequest,
    MissingPlayer,
    IneligibleHolder,
    InvalidAimOrigin,
    InvalidPose,
    UnsupportedGeometry,
    Occupied
}

/// <summary>Replicates cosmetic single-carry preview state for observers of this player.</summary>
[DisallowMultipleComponent]
public sealed class PlayerSingleCarryPlacementNetworkPreview : NetworkBehaviour
{
    [SerializeField, Min(0.1f)] private float placementDistance = 2f;

    private struct PreviewState : INetworkSerializable, System.IEquatable<PreviewState>
    {
        public bool Active;
        public NetworkObjectReference Source;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Valid;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Active);
            serializer.SerializeValue(ref Source);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref Valid);
        }

        public bool Equals(PreviewState other) => Active == other.Active && Source.Equals(other.Source)
            && Position == other.Position && Rotation == other.Rotation && Valid == other.Valid;
    }

    private readonly NetworkVariable<PreviewState> state = new NetworkVariable<PreviewState>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private SingleCarryPlacementGhost remoteGhost;
    private NetworkObject remoteSource;
    private Vector3 remotePoseStartPosition;
    private Quaternion remotePoseStartRotation;
    private Vector3 remotePoseTargetPosition;
    private Quaternion remotePoseTargetRotation;
    private float remotePoseStartTime;
    private float remoteInterpolationDuration = 0.05f;
    private bool remotePoseInterpolating;
    private float nextAppearanceCheck;
    private float nextSendTime;
    private PreviewState lastRequested;
    private bool hasLastRequested;
    private ulong lastProcessedPlacementRequestId;

    public event System.Action<ulong, SingleCarryPlacementNetworkResult> PlacementRequestCompleted;
    public event System.Action PlacementChannelLost;
    public ulong LastCompletedPlacementRequestId { get; private set; }
    public SingleCarryPlacementNetworkResult LastPlacementResult { get; private set; }
    public float PlacementDistance => Mathf.Max(0.1f, placementDistance);

    public override void OnNetworkSpawn()
    {
        hasLastRequested = false;
        lastProcessedPlacementRequestId = 0;
        LastCompletedPlacementRequestId = 0;
        LastPlacementResult = SingleCarryPlacementNetworkResult.InvalidRequest;
        nextSendTime = 0f;
        nextAppearanceCheck = 0f;
        DisposeRemoteGhost();
        state.OnValueChanged += StateChanged;
        ApplyState(default, state.Value);
    }

    public override void OnNetworkDespawn()
    {
        PlacementChannelLost?.Invoke();
        state.OnValueChanged -= StateChanged;
        if (IsServer && state.Value.Active) state.Value = default;
        DisposeRemoteGhost();
        hasLastRequested = false;
        lastProcessedPlacementRequestId = 0;
        LastCompletedPlacementRequestId = 0;
        LastPlacementResult = SingleCarryPlacementNetworkResult.InvalidRequest;
        nextSendTime = 0f;
        nextAppearanceCheck = 0f;
    }

    public override void OnLostOwnership()
    {
        PlacementChannelLost?.Invoke();
        if (IsServer && state.Value.Active) state.Value = default;
        hasLastRequested = false;
        DisposeRemoteGhost();
    }

    public override void OnGainedOwnership()
    {
        hasLastRequested = false;
        nextSendTime = 0f;
        DisposeRemoteGhost();
    }

    public override void OnDestroy()
    {
        state.OnValueChanged -= StateChanged;
        DisposeRemoteGhost();
        base.OnDestroy();
    }

    /// <summary>Called by the owner-only controller; the server relays validated cosmetic state.</summary>
    public void SetOwnerPreview(GameObject source, Vector3 position, Quaternion rotation, bool valid)
    {
        if (!IsSpawned || !IsOwner || NetworkManager == null || !NetworkManager.IsListening) return;
        if (source == null)
        {
            ClearOwnerPreview();
            return;
        }

        NetworkObject sourceNetworkObject = source.GetComponent<NetworkObject>();
        if (sourceNetworkObject == null) sourceNetworkObject = source.GetComponentInParent<NetworkObject>();
        if (sourceNetworkObject == null || !sourceNetworkObject.IsSpawned) return;

        PreviewState requested = new PreviewState
        {
            Active = true,
            Source = new NetworkObjectReference(sourceNetworkObject),
            Position = position,
            Rotation = rotation.normalized,
            Valid = valid
        };
        if (!IsFinite(requested)) return;
        bool sourceOrValidityChanged = hasLastRequested &&
            (!requested.Source.Equals(lastRequested.Source) || requested.Valid != lastRequested.Valid);
        bool poseChanged = !hasLastRequested || requested.Position != lastRequested.Position
            || requested.Rotation != lastRequested.Rotation;
        bool previewChanged = sourceOrValidityChanged || poseChanged;
        bool heartbeatDue = hasLastRequested && Time.unscaledTime >= nextSendTime + 0.20f;
        bool immediateChange = !hasLastRequested || !lastRequested.Active;
        if (!immediateChange && !previewChanged && !heartbeatDue) return;
        if (!immediateChange && Time.unscaledTime < nextSendTime) return;
        lastRequested = requested;
        hasLastRequested = true;
        nextSendTime = Time.unscaledTime + 0.05f;
        if (IsServer) AcceptOwnerState(requested, OwnerClientId);
        else SubmitOwnerStateServerRpc(requested);
    }

    public void ClearOwnerPreview()
    {
        if (!IsSpawned || !IsOwner || NetworkManager == null || !NetworkManager.IsListening) return;
        hasLastRequested = false;
        if (IsServer) state.Value = default;
        else ClearOwnerStateServerRpc();
    }

    /// <summary>Submits one authoritative placement request. Host requests complete synchronously.</summary>
    public void RequestNetworkPlacement(NetworkObject source, ulong requestId, Vector3 position,
        Quaternion rotation, Ray aimRay)
    {
        if (!IsSpawned || !IsOwner || NetworkManager == null || !NetworkManager.IsListening
            || source == null || !source.IsSpawned || requestId == 0) return;

        NetworkObjectReference sourceReference = new NetworkObjectReference(source);
        if (IsServer)
        {
            SingleCarryPlacementNetworkResult result = ValidateAndCommitPlacement(sourceReference,
                requestId, position, rotation, aimRay, OwnerClientId);
            ReportPlacementResult(requestId, result);
            return;
        }

        SubmitPlacementServerRpc(sourceReference, requestId, position, rotation,
            aimRay.origin, aimRay.direction);
    }

    /// <summary>Submits a placement captured relative to one spawned target NetworkObject.</summary>
    public void RequestNetworkSurfacePlacement(NetworkObject source, ulong requestId, NetworkObjectReference targetReference,
        Vector3 localPosition, Quaternion localRotation, Vector3 localSurfacePoint, Vector3 localSurfaceNormal,
        string physicalRootPath, bool physicalRootHasRigidbody,
        Vector3 position, Quaternion rotation, Ray aimRay)
    {
        if (!IsSpawned || !IsOwner || NetworkManager == null || !NetworkManager.IsListening
            || source == null || !source.IsSpawned || requestId == 0
            || !targetReference.TryGet(out NetworkObject target, NetworkManager)
            || target == null || !target.IsSpawned || target.NetworkManager != NetworkManager) return;

        var sourceReference = new NetworkObjectReference(source);
        if (IsServer)
        {
            SingleCarryPlacementNetworkResult result = ValidateAndCommitSurfacePlacement(sourceReference,
                targetReference, requestId, localPosition, localRotation, localSurfacePoint, localSurfaceNormal,
                physicalRootPath, physicalRootHasRigidbody, position, rotation, aimRay, OwnerClientId);
            ReportPlacementResult(requestId, result);
            return;
        }

        SubmitSurfacePlacementServerRpc(sourceReference, targetReference, requestId, localPosition,
            localRotation, localSurfacePoint, localSurfaceNormal, physicalRootPath,
            physicalRootHasRigidbody, position, rotation, aimRay.origin, aimRay.direction);
    }

    private void Update()
    {
        if (!IsSpawned || NetworkManager == null || !NetworkManager.IsListening) return;
        if (IsServer && state.Value.Active && !IsAcceptedState(state.Value, OwnerClientId))
        {
            state.Value = default;
            return;
        }

        if (IsOwner || !state.Value.Active) return;
        if (remoteGhost == null || Time.unscaledTime >= nextAppearanceCheck)
        {
            nextAppearanceCheck = Time.unscaledTime + 0.25f;
            TryApplyRemoteSource(state.Value);
        }
        UpdateRemoteGhostPose();
        if (remoteGhost != null) remoteGhost.SyncSourceAppearance();
    }

    [ServerRpc(RequireOwnership = true)]
    private void SubmitOwnerStateServerRpc(PreviewState requested, ServerRpcParams rpc = default)
    {
        AcceptOwnerState(requested, rpc.Receive.SenderClientId);
    }

    [ServerRpc(RequireOwnership = true)]
    private void ClearOwnerStateServerRpc(ServerRpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId == OwnerClientId) state.Value = default;
    }

    [ServerRpc(RequireOwnership = true)]
    private void SubmitPlacementServerRpc(NetworkObjectReference sourceReference, ulong requestId,
        Vector3 position, Quaternion rotation, Vector3 rayOrigin, Vector3 rayDirection,
        ServerRpcParams rpc = default)
    {
        SingleCarryPlacementNetworkResult result = ValidateAndCommitPlacement(sourceReference,
            requestId, position, rotation, new Ray(rayOrigin, rayDirection), rpc.Receive.SenderClientId);
        PlacementResultClientRpc(requestId, result, CreateTargetClientRpcParams(rpc.Receive.SenderClientId));
    }

    [ServerRpc(RequireOwnership = true)]
    private void SubmitSurfacePlacementServerRpc(NetworkObjectReference sourceReference,
        NetworkObjectReference targetReference, ulong requestId, Vector3 localPosition, Quaternion localRotation,
        Vector3 localSurfacePoint, Vector3 localSurfaceNormal, string physicalRootPath,
        bool physicalRootHasRigidbody, Vector3 position, Quaternion rotation,
        Vector3 rayOrigin, Vector3 rayDirection, ServerRpcParams rpc = default)
    {
        SingleCarryPlacementNetworkResult result = ValidateAndCommitSurfacePlacement(sourceReference,
            targetReference, requestId, localPosition, localRotation, localSurfacePoint, localSurfaceNormal,
            physicalRootPath, physicalRootHasRigidbody, position, rotation,
            new Ray(rayOrigin, rayDirection), rpc.Receive.SenderClientId);
        PlacementResultClientRpc(requestId, result, CreateTargetClientRpcParams(rpc.Receive.SenderClientId));
    }

    [ClientRpc]
    private void PlacementResultClientRpc(ulong requestId, SingleCarryPlacementNetworkResult result,
        ClientRpcParams rpc = default)
    {
        if (IsOwner) ReportPlacementResult(requestId, result);
    }

    private void ReportPlacementResult(ulong requestId, SingleCarryPlacementNetworkResult result)
    {
        LastCompletedPlacementRequestId = requestId;
        LastPlacementResult = result;
        PlacementRequestCompleted?.Invoke(requestId, result);
    }

    private SingleCarryPlacementNetworkResult ValidateAndCommitPlacement(NetworkObjectReference sourceReference,
        ulong requestId, Vector3 position, Quaternion rotation, Ray aimRay, ulong senderClientId)
        => ValidateAndCommitPlacementCore(sourceReference, default, false, requestId, default, Quaternion.identity,
            default, default, null, false, position, rotation, aimRay, senderClientId);

    private SingleCarryPlacementNetworkResult ValidateAndCommitSurfacePlacement(NetworkObjectReference sourceReference,
        NetworkObjectReference targetReference, ulong requestId, Vector3 localPosition, Quaternion localRotation,
        Vector3 localSurfacePoint, Vector3 localSurfaceNormal, string physicalRootPath,
        bool physicalRootHasRigidbody, Vector3 position, Quaternion rotation, Ray aimRay, ulong senderClientId)
        => ValidateAndCommitPlacementCore(sourceReference, targetReference, true, requestId, localPosition,
            localRotation, localSurfacePoint, localSurfaceNormal, physicalRootPath,
            physicalRootHasRigidbody, position, rotation, aimRay, senderClientId);

    private SingleCarryPlacementNetworkResult ValidateAndCommitPlacementCore(NetworkObjectReference sourceReference,
        NetworkObjectReference targetReference, bool hasSurfaceAnchor, ulong requestId, Vector3 localPosition,
        Quaternion localRotation, Vector3 localSurfacePoint, Vector3 localSurfaceNormal,
        string physicalRootPath, bool physicalRootHasRigidbody,
        Vector3 position, Quaternion rotation, Ray aimRay, ulong senderClientId)
    {
        NetworkManager manager = NetworkManager;
        if (!IsServer || manager == null || !manager.IsListening || requestId == 0)
            return SingleCarryPlacementNetworkResult.InvalidRequest;
        if (requestId <= lastProcessedPlacementRequestId)
            return SingleCarryPlacementNetworkResult.StaleRequest;
        lastProcessedPlacementRequestId = requestId;

        if (senderClientId != OwnerClientId
            || !manager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client)
            || client == null || client.PlayerObject == null || !client.PlayerObject.IsSpawned
            || client.PlayerObject != NetworkObject
            || !client.PlayerObject.IsPlayerObject || client.PlayerObject.OwnerClientId != senderClientId
            || !client.PlayerObject.TryGetComponent(out PlayerInteractionNew holder))
            return SingleCarryPlacementNetworkResult.MissingPlayer;
        if (!sourceReference.TryGet(out NetworkObject source, manager) || source == null || !source.IsSpawned
            || !source.gameObject.activeInHierarchy || !source.TryGetComponent(out Rigidbody body)
            || !body.gameObject.activeInHierarchy || !IsFinite(position) || !IsUnitQuaternion(rotation)
            || !IsFinite(aimRay.origin) || !IsUnitDirection(aimRay.direction))
            return SingleCarryPlacementNetworkResult.InvalidRequest;

        PlayerHealth health = holder.GetComponent<PlayerHealth>();
        ISingleCarryPlaceable placeable = source.GetComponent<ISingleCarryPlaceable>();
        if (health == null || health.IsDowned || placeable == null
            || !placeable.CanEnterSingleCarryPlacement
            || !placeable.CanCompleteNetworkSingleCarryPlacement(senderClientId, holder))
            return SingleCarryPlacementNetworkResult.IneligibleHolder;

        Transform aimAnchor = holder.SingleCarryPlacementAimAnchor;
        NetworkTransform playerTransform = client.PlayerObject.GetComponent<NetworkTransform>();
        if (aimAnchor == null || playerTransform == null || !playerTransform.IsSpawned)
            return SingleCarryPlacementNetworkResult.InvalidAimOrigin;
        Vector3 anchorLocalOffset = client.PlayerObject.transform.InverseTransformPoint(aimAnchor.position);
        Vector3 authoritativePlayerPosition = playerTransform.GetSpaceRelativePosition(true);
        Quaternion authoritativePlayerRotation = playerTransform.GetSpaceRelativeRotation(true);
        if (playerTransform.InLocalSpace && client.PlayerObject.transform.parent != null)
        {
            authoritativePlayerPosition = client.PlayerObject.transform.parent.TransformPoint(authoritativePlayerPosition);
            authoritativePlayerRotation = client.PlayerObject.transform.parent.rotation * authoritativePlayerRotation;
        }
        Vector3 trustedAimAnchor = authoritativePlayerPosition
            + authoritativePlayerRotation * Vector3.Scale(anchorLocalOffset, client.PlayerObject.transform.localScale);
        // A direct interactionOrigin ray starts at the target. ViewportPointToRay starts at
        // the camera near plane; both first-person camera prefabs use a 0.2m near plane.
        // Keep the existing 0.15m bounded feedback/snapshot allowance around those two known
        // origins instead of treating the legitimate near-plane advance as arbitrary client offset.
        const float cameraNearClipDistance = 0.2f;
        const float maximumAimOriginResidual = 0.15f;
        Vector3 offsetFromAnchor = aimRay.origin - trustedAimAnchor;
        float forwardOffset = Vector3.Dot(offsetFromAnchor, aimRay.direction);
        Vector3 lateralOffset = offsetFromAnchor - aimRay.direction * forwardOffset;
        float nearestAllowedForwardOffset = Mathf.Clamp(forwardOffset, 0f, cameraNearClipDistance);
        float residualSquared = lateralOffset.sqrMagnitude
            + Mathf.Pow(forwardOffset - nearestAllowedForwardOffset, 2f);
        if (residualSquared > maximumAimOriginResidual * maximumAimOriginResidual)
            return SingleCarryPlacementNetworkResult.InvalidAimOrigin;

        Physics.SyncTransforms();
        var geometry = new SingleCarryPlacementGeometrySolver(source.gameObject);
        SingleCarryPlacementPose expectedPose;
        NetworkObject anchoredTarget = null;
        if (hasSurfaceAnchor)
        {
            if (!targetReference.TryGet(out anchoredTarget, manager) || anchoredTarget == null
                || !anchoredTarget.IsSpawned || !anchoredTarget.gameObject.activeInHierarchy
                || anchoredTarget.NetworkManager != manager || anchoredTarget == source
                || !IsFinite(localPosition) || !IsUnitQuaternion(localRotation)
                || !IsFinite(localSurfacePoint) || !IsUnitDirection(localSurfaceNormal)
                || string.IsNullOrEmpty(physicalRootPath)
                || !IsFinite(position) || !IsUnitQuaternion(rotation))
                return SingleCarryPlacementNetworkResult.InvalidRequest;

            if (!SingleCarryPlacementSurfaceAnchor.TryResolveAnchorTransform(anchoredTarget, physicalRootPath,
                    physicalRootHasRigidbody, out Transform anchorTransform))
                return SingleCarryPlacementNetworkResult.InvalidPose;
            Quaternion rebasedRotation = anchorTransform.rotation * localRotation;
            Vector3 worldPoint = anchorTransform.TransformPoint(localSurfacePoint);
            Vector3 worldNormal = anchorTransform.TransformDirection(localSurfaceNormal).normalized;
            Vector3 rebasedPosition = anchorTransform.TransformPoint(localPosition);
            if (!IsFinite(rebasedPosition) || !IsUnitQuaternion(rebasedRotation)
                || !IsFinite(worldPoint) || !IsUnitDirection(worldNormal))
                return SingleCarryPlacementNetworkResult.InvalidPose;
            if (!SingleCarryPlacementSurfaceAnchor.TryResolveContactCollider(anchoredTarget, source.transform,
                    holder.transform, worldPoint, worldNormal, physicalRootPath,
                    physicalRootHasRigidbody, out Collider contactCollider)
                || !SingleCarryPlacementSurfaceAnchor.HasLineOfSightToContact(contactCollider, anchoredTarget,
                    physicalRootPath, physicalRootHasRigidbody, source.transform, holder.transform,
                    aimRay.origin, worldPoint, PlacementDistance))
                return SingleCarryPlacementNetworkResult.InvalidPose;

            expectedPose = geometry.CalculateAtSurfaceContact(worldPoint, worldNormal, rebasedRotation, contactCollider);
            if (expectedPose.Status != SingleCarryPlacementGeometryStatus.SurfaceSupported
                || Vector3.Distance(expectedPose.Position, rebasedPosition) > 0.002f
                || Quaternion.Angle(expectedPose.Rotation, rebasedRotation) > 0.1f)
                return SingleCarryPlacementNetworkResult.InvalidPose;
        }
        else
        {
            expectedPose = geometry.Calculate(aimRay, PlacementDistance,
                rotation.normalized, Quaternion.identity, holder.transform);
        }
        if (expectedPose.Status == SingleCarryPlacementGeometryStatus.UnsupportedCollider
            || expectedPose.Status == SingleCarryPlacementGeometryStatus.NoPhysicalColliders
            || expectedPose.Status == SingleCarryPlacementGeometryStatus.RaycastBufferSaturated)
            return SingleCarryPlacementNetworkResult.UnsupportedGeometry;
        if (!IsFinite(expectedPose.Position) || !IsUnitQuaternion(expectedPose.Rotation))
            return SingleCarryPlacementNetworkResult.InvalidPose;
        const float positionTolerance = 0.002f;
        const float rotationToleranceDegrees = 0.1f;
        if (!hasSurfaceAnchor && (Vector3.Distance(expectedPose.Position, position) > positionTolerance
            || Quaternion.Angle(expectedPose.Rotation, rotation.normalized) > rotationToleranceDegrees))
            return SingleCarryPlacementNetworkResult.InvalidPose;

        var validator = new SingleCarryPlacementValidator(source.gameObject);
        if (!validator.Validate(expectedPose).IsValid)
            return SingleCarryPlacementNetworkResult.Occupied;

        // Requery immediately before mutation so another accepted request cannot claim the same space.
        Physics.SyncTransforms();
        if (!new SingleCarryPlacementValidator(source.gameObject).Validate(expectedPose).IsValid)
            return SingleCarryPlacementNetworkResult.Occupied;

        if (hasSurfaceAnchor)
        {
            if (anchoredTarget == null || !anchoredTarget.IsSpawned
                || !SingleCarryPlacementSurfaceAnchor.TryResolveAnchorTransform(anchoredTarget, physicalRootPath,
                    physicalRootHasRigidbody, out Transform finalAnchorTransform)
                || !SingleCarryPlacementSurfaceAnchor.TryResolveContactCollider(anchoredTarget, source.transform,
                    holder.transform, finalAnchorTransform.TransformPoint(localSurfacePoint),
                    finalAnchorTransform.TransformDirection(localSurfaceNormal).normalized,
                    physicalRootPath, physicalRootHasRigidbody, out Collider finalContact)
                || finalContact.GetComponentInParent<NetworkObject>() != anchoredTarget)
                return SingleCarryPlacementNetworkResult.InvalidPose;
        }

        if (!placeable.TryCompleteNetworkSingleCarryPlacement(senderClientId, holder,
                expectedPose.Position, expectedPose.Rotation))
            return SingleCarryPlacementNetworkResult.IneligibleHolder;

        state.Value = default;
        if (source.TryGetComponent(out ISingleCarryPlacementCommitListener commitListener))
            commitListener.OnSingleCarryPlacementCommitted(holder);
        return SingleCarryPlacementNetworkResult.Accepted;
    }

    private ClientRpcParams CreateTargetClientRpcParams(ulong clientId) => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
    };

    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsUnitDirection(Vector3 value) => IsFinite(value) && Mathf.Abs(value.sqrMagnitude - 1f) <= 0.02f;
    private static bool IsUnitQuaternion(Quaternion value)
    {
        float magnitudeSquared = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w)
            && IsFinite(magnitudeSquared) && Mathf.Abs(magnitudeSquared - 1f) <= 0.02f;
    }
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private void AcceptOwnerState(PreviewState requested, ulong senderClientId)
    {
        if (senderClientId != OwnerClientId) return;
        if (!requested.Active)
        {
            state.Value = default;
            return;
        }
        if (IsAcceptedState(requested, senderClientId)) state.Value = requested;
    }

    private bool IsAcceptedState(PreviewState candidate, ulong holderClientId)
    {
        if (!IsFinite(candidate) || !candidate.Active
            || !candidate.Source.TryGet(out NetworkObject sourceObject, NetworkManager)
            || sourceObject == null || !sourceObject.IsSpawned)
            return false;

        ISingleCarryPlaceable placeable = sourceObject.GetComponent<ISingleCarryPlaceable>();
        if (placeable == null || !placeable.CanEnterSingleCarryPlacement) return false;
        if (sourceObject.TryGetComponent(out BaseResourceNew resource)) return resource.IsHeldBy(holderClientId);
        if (sourceObject.TryGetComponent(out MountableBridgeComponent bridge)) return bridge.IsHeldBy(holderClientId);
        return false;
    }

    private static bool IsFinite(PreviewState candidate)
    {
        Quaternion q = candidate.Rotation;
        float magnitudeSquared = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
        return IsFinite(candidate.Position.x) && IsFinite(candidate.Position.y) && IsFinite(candidate.Position.z)
            && IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w)
            && IsFinite(magnitudeSquared) && magnitudeSquared >= 0.81f && magnitudeSquared <= 1.21f;
    }

    private void StateChanged(PreviewState previous, PreviewState current) => ApplyState(previous, current);

    private void ApplyState(PreviewState previous, PreviewState current)
    {
        if (IsOwner || !IsClient || !current.Active)
        {
            DisposeRemoteGhost();
            return;
        }
        TryApplyRemoteSource(current);
    }

    private void TryApplyRemoteSource(PreviewState current)
    {
        if (IsOwner || !IsClient || !current.Active || NetworkManager == null
            || !current.Source.TryGet(out NetworkObject sourceObject, NetworkManager) || sourceObject == null
            || !sourceObject.IsSpawned)
        {
            DisposeRemoteGhost();
            return;
        }

        if (remoteSource != sourceObject || remoteGhost == null)
        {
            DisposeRemoteGhost();
            remoteSource = sourceObject;
            remoteGhost = new SingleCarryPlacementGhost(sourceObject.gameObject);
            SetRemotePoseTarget(current.Position, current.Rotation, true);
        }
        else
        {
            SetRemotePoseTarget(current.Position, current.Rotation, false);
        }
        remoteGhost.SetPlacementValid(current.Valid);
        remoteGhost.SetVisible(true);
    }

    private void SetRemotePoseTarget(Vector3 position, Quaternion rotation, bool snap)
    {
        rotation = rotation.normalized;
        if (!snap && remotePoseTargetPosition == position
            && Quaternion.Angle(remotePoseTargetRotation, rotation) <= 0.001f)
            return;

        if (snap || remoteGhost == null)
        {
            remotePoseTargetPosition = position;
            remotePoseTargetRotation = rotation;
            remotePoseStartPosition = position;
            remotePoseStartRotation = rotation;
            remotePoseInterpolating = false;
            if (remoteGhost != null) remoteGhost.SetPose(position, rotation);
            return;
        }

        remotePoseStartPosition = remoteGhost.Root.transform.position;
        remotePoseStartRotation = remoteGhost.Root.transform.rotation;
        remotePoseTargetPosition = position;
        remotePoseTargetRotation = rotation;
        remotePoseStartTime = Time.unscaledTime;
        remotePoseInterpolating = remoteInterpolationDuration > 0f;
        if (!remotePoseInterpolating) remoteGhost.SetPose(position, rotation);
    }

    private void UpdateRemoteGhostPose()
    {
        if (!remotePoseInterpolating || remoteGhost == null) return;

        float duration = Mathf.Max(0.001f, remoteInterpolationDuration);
        float progress = Mathf.Clamp01((Time.unscaledTime - remotePoseStartTime) / duration);
        if (progress >= 1f)
        {
            remoteGhost.SetPose(remotePoseTargetPosition, remotePoseTargetRotation);
            remotePoseInterpolating = false;
            return;
        }

        remoteGhost.SetPose(Vector3.Lerp(remotePoseStartPosition, remotePoseTargetPosition, progress),
            Quaternion.Slerp(remotePoseStartRotation, remotePoseTargetRotation, progress));
    }

    private void DisposeRemoteGhost()
    {
        if (remoteGhost != null) remoteGhost.Dispose();
        remoteGhost = null;
        remoteSource = null;
        remotePoseStartPosition = Vector3.zero;
        remotePoseStartRotation = Quaternion.identity;
        remotePoseTargetPosition = Vector3.zero;
        remotePoseTargetRotation = Quaternion.identity;
        remotePoseStartTime = 0f;
        remotePoseInterpolating = false;
    }
}
