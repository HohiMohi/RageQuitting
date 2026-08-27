using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class WheelbarrowPouringMinigame : NetworkBehaviour
{
    private static readonly HashSet<WheelbarrowPouringMinigame> Instances = new HashSet<WheelbarrowPouringMinigame>();
    public const ulong NoPlayer = ulong.MaxValue;

    [SerializeField] private ConcretePouringProfileSO profile;
    [SerializeField] private Transform leftPlayerAnchor;
    [SerializeField] private Transform rightPlayerAnchor;
    [SerializeField] private Transform soloPlayerAnchor;
    [SerializeField] private WheelbarrowPourGripInteraction leftGrip;
    [SerializeField] private WheelbarrowPourGripInteraction rightGrip;

    private readonly NetworkVariable<byte> stateNetwork = new NetworkVariable<byte>((byte)WheelbarrowPouringState.Inactive);
    private readonly NetworkVariable<ulong> leftPlayerNetwork = new NetworkVariable<ulong>(NoPlayer);
    private readonly NetworkVariable<ulong> rightPlayerNetwork = new NetworkVariable<ulong>(NoPlayer);
    private readonly NetworkVariable<float> leftCursorNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<float> rightCursorNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<float> criticalDeadlineNetwork = new NetworkVariable<float>();

    private WheelbarrowDockingStation station;
    private WheelbarrowController wheelbarrow;
    private BridgeConstructionSite site;
    private Quaternion transportRotation;
    private float localLeft;
    private float localRight;
    private ulong localLeftPlayer = NoPlayer;
    private ulong localRightPlayer = NoPlayer;
    private WheelbarrowPouringState localState;

    public WheelbarrowPouringState State => IsNetworkActive ? (WheelbarrowPouringState)stateNetwork.Value : localState;
    public ulong LeftPlayer => IsNetworkActive ? leftPlayerNetwork.Value : localLeftPlayer;
    public ulong RightPlayer => IsNetworkActive ? rightPlayerNetwork.Value : localRightPlayer;
    public float LeftCursor => IsNetworkActive ? leftCursorNetwork.Value : localLeft;
    public float RightCursor => IsNetworkActive ? rightCursorNetwork.Value : localRight;
    public ConcretePouringProfileSO Profile => profile;
    public WheelbarrowController Wheelbarrow => wheelbarrow != null ? wheelbarrow : station != null ? station.DockedWheelbarrow : null;
    public Transform LeftPlayerAnchor => leftPlayerAnchor;
    public Transform RightPlayerAnchor => rightPlayerAnchor;
    public Transform SoloPlayerAnchor => soloPlayerAnchor;
    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool HasAuthority => !IsNetworkActive || IsServer;

    private void Awake()
    {
        Instances.Add(this);
        station = GetComponent<WheelbarrowDockingStation>();
        site = station != null ? station.FoundationSite : null;
    }
    private void OnDestroy() => Instances.Remove(this);

    public static WheelbarrowPouringMinigame FindForPlayer(ulong clientId)
    {
        foreach (WheelbarrowPouringMinigame item in Instances)
            if (item != null && item.IsPlayerParticipant(clientId)) return item;
        return null;
    }

    public void Bind(WheelbarrowDockingStation ownerStation, WheelbarrowController target, BridgeConstructionSite constructionSite)
    {
        if (!HasAuthority) return;
        station = ownerStation; wheelbarrow = target; site = constructionSite;
        transportRotation = target.transform.rotation;
        SetState(WheelbarrowPouringState.WaitingForPlayers);
        SetCursors(0f, 0f);
    }

    public bool RequestJoin(Transform playerTransform, bool preferLeft)
    {
        if (playerTransform == null || !playerTransform.TryGetComponent(out NetworkObject player)) return false;
        if (!IsNetworkActive) return Join(player.OwnerClientId, preferLeft, playerTransform);
        if (IsServer) return Join(player.OwnerClientId, preferLeft, playerTransform);
        RequestJoinServerRpc(preferLeft);
        return true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestJoinServerRpc(bool left, ServerRpcParams rpc = default) =>
        Join(rpc.Receive.SenderClientId, left, null);

    public bool CanOfferJoin(Transform interactor, bool left)
    {
        if (interactor == null || !IsReadyForParticipants()) return false;
        NetworkObject playerObject = interactor.GetComponentInParent<NetworkObject>();
        ulong clientId = playerObject != null ? playerObject.OwnerClientId : 0;
        if (IsPlayerParticipant(clientId)) return false;

        PlayerHealth health = interactor.GetComponentInParent<PlayerHealth>();
        if (health != null && health.IsDowned) return false;

        if (profile != null && profile.AllowSinglePlayerTesting)
        {
            if (LeftPlayer != NoPlayer || RightPlayer != NoPlayer) return false;
        }
        else if (IsSideOccupied(left)) return false;

        WheelbarrowPourGripInteraction grip = GetGrip(left);
        Collider gripCollider = grip != null ? grip.InteractionCollider : null;
        float maximumDistance = profile != null ? profile.MaximumJoinDistance : 3f;
        return gripCollider == null || Vector3.Distance(
            interactor.position,
            gripCollider.ClosestPoint(interactor.position)) <= maximumDistance;
    }

    public bool ShouldShowJoinStations(ulong localClientId) =>
        IsReadyForParticipants() && !IsPlayerParticipant(localClientId);

    public bool IsPlayerParticipant(ulong clientId) => LeftPlayer == clientId || RightPlayer == clientId;
    public bool IsSideOccupied(bool left) => (left ? LeftPlayer : RightPlayer) != NoPlayer;
    public WheelbarrowPourGripInteraction GetGrip(bool left) => left ? leftGrip : rightGrip;
    public Transform GetConfiguredAnchor(bool left) => left ? leftPlayerAnchor : rightPlayerAnchor;

    private bool Join(ulong clientId, bool preferLeft, Transform suppliedPlayerTransform)
    {
        WheelbarrowController activeWheelbarrow = Wheelbarrow;
        if (!HasAuthority || activeWheelbarrow == null || !IsReadyForParticipants() ||
            IsPlayerParticipant(clientId)) return false;

        bool solo = profile != null && profile.AllowSinglePlayerTesting;
        if (solo)
        {
            if (LeftPlayer != NoPlayer || RightPlayer != NoPlayer) return false;
        }
        else if (IsSideOccupied(preferLeft)) return false;

        Transform playerTransform = ResolvePlayerTransform(clientId, suppliedPlayerTransform);
        if (!ValidateJoinPosition(clientId, preferLeft, solo, playerTransform)) return false;

        if (solo)
        {
            SetLeftPlayer(clientId);
            SetRightPlayer(clientId);
        }
        else if (preferLeft) SetLeftPlayer(clientId);
        else SetRightPlayer(clientId);

        bool ready = LeftPlayer != NoPlayer && RightPlayer != NoPlayer;
        if (ready)
        {
            SetState(WheelbarrowPouringState.Active);
            activeWheelbarrow.SetPouringState(true);
        }
        return true;
    }

    private bool ValidateJoinPosition(ulong clientId, bool left, bool solo, Transform playerTransform)
    {
        if (playerTransform == null) return false;
        NetworkObject playerObject = playerTransform.GetComponent<NetworkObject>();
        if (IsNetworkActive && (playerObject == null || playerObject.OwnerClientId != clientId)) return false;

        PlayerHealth health = playerTransform.GetComponent<PlayerHealth>();
        if (health != null && health.IsDowned) return false;

        WheelbarrowPourGripInteraction grip = GetGrip(left);
        Collider gripCollider = grip != null ? grip.InteractionCollider : null;
        float maximumDistance = profile != null ? profile.MaximumJoinDistance : 3f;
        if (gripCollider == null || Vector3.Distance(
            playerTransform.position,
            gripCollider.ClosestPoint(playerTransform.position)) > maximumDistance) return false;

        CharacterController controller = playerTransform.GetComponent<CharacterController>();
        Transform requestedAnchor = solo ? soloPlayerAnchor : GetConfiguredAnchor(left);
        return requestedAnchor != null && controller != null &&
            TryResolveAnchorPose(requestedAnchor, playerTransform, controller, out _, out _);
    }

    private Transform ResolvePlayerTransform(ulong clientId, Transform suppliedPlayerTransform)
    {
        if (suppliedPlayerTransform != null)
        {
            NetworkObject suppliedObject = suppliedPlayerTransform.GetComponent<NetworkObject>();
            if (!IsNetworkActive || suppliedObject != null && suppliedObject.OwnerClientId == clientId)
                return suppliedPlayerTransform;
        }

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
            client.PlayerObject != null)
        {
            return client.PlayerObject.transform;
        }
        return null;
    }

    private bool IsReadyForParticipants()
    {
        WheelbarrowController activeWheelbarrow = Wheelbarrow;
        bool validState = State == WheelbarrowPouringState.WaitingForPlayers ||
            State == WheelbarrowPouringState.Active;
        return validState && station != null && activeWheelbarrow != null &&
            station.DockedWheelbarrow == activeWheelbarrow && activeWheelbarrow.IsDockSecured &&
            activeWheelbarrow.HasConcrete;
    }

    public bool TryResolveAnchorPose(
        ulong clientId,
        CharacterController controller,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = controller != null ? controller.transform.position : transform.position;
        rotation = controller != null ? controller.transform.rotation : transform.rotation;
        Transform anchor = GetAnchor(clientId);
        Transform playerRoot = controller != null ? controller.transform : null;
        return anchor != null && playerRoot != null &&
            TryResolveAnchorPose(anchor, playerRoot, controller, out position, out rotation);
    }

    public bool TryGetStationMarkerPose(bool left, out Vector3 position, out Quaternion rotation)
    {
        Transform anchor = GetConfiguredAnchor(left);
        position = anchor != null ? anchor.position : transform.position;
        rotation = ResolveFacingRotation(position, anchor != null ? anchor.rotation : transform.rotation);
        if (anchor == null) return false;

        if (TryFindGround(anchor.position, null, out RaycastHit groundHit))
            position.y = groundHit.point.y + 0.025f;
        return true;
    }

    private bool TryResolveAnchorPose(
        Transform anchor,
        Transform playerRoot,
        CharacterController controller,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = anchor.position;
        rotation = ResolveFacingRotation(position, anchor.rotation);
        if (!TryFindGround(anchor.position, playerRoot, out RaycastHit groundHit)) return false;

        float bottomOffset = controller.center.y - controller.height * 0.5f;
        position.y = groundHit.point.y - bottomOffset;
        rotation = ResolveFacingRotation(position, anchor.rotation);

        float padding = profile != null ? profile.ParticipantCapsulePadding : 0.05f;
        float radius = Mathf.Max(0.05f, controller.radius + padding);
        float height = Mathf.Max(controller.height, radius * 2f);
        Vector3 center = position + controller.center;
        Vector3 bottom = center + Vector3.down * (height * 0.5f - radius);
        Vector3 top = center + Vector3.up * (height * 0.5f - radius);
        Collider[] overlaps = Physics.OverlapCapsule(
            bottom,
            top,
            radius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider overlap = overlaps[i];
            if (overlap == null || overlap == groundHit.collider || overlap.transform.root == playerRoot) continue;
            return false;
        }
        return true;
    }

    private bool TryFindGround(Vector3 anchorPosition, Transform ignoredPlayerRoot, out RaycastHit groundHit)
    {
        float distance = profile != null ? profile.ParticipantGroundProbeDistance : 3f;
        Vector3 origin = anchorPosition + Vector3.up * 1.5f;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            distance + 1.5f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (candidate == null || candidate.transform.root == ignoredPlayerRoot ||
                candidate.GetComponentInParent<PlayerInteractionNew>() != null ||
                candidate.GetComponentInParent<WheelbarrowController>() != null) continue;
            groundHit = hits[i];
            return true;
        }

        groundHit = default;
        return false;
    }

    private Quaternion ResolveFacingRotation(Vector3 position, Quaternion fallback)
    {
        WheelbarrowController activeWheelbarrow = Wheelbarrow;
        if (activeWheelbarrow != null)
        {
            Vector3 direction = Vector3.ProjectOnPlane(
                activeWheelbarrow.transform.position - position,
                Vector3.up);
            if (direction.sqrMagnitude > 0.0001f)
                return Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
        return Quaternion.Euler(0f, fallback.eulerAngles.y, 0f);
    }

    public void SubmitCursorDelta(float verticalDelta, ulong clientId)
    {
        if (!HasAuthority || State != WheelbarrowPouringState.Active || profile == null) return;
        float amount = Mathf.Clamp(verticalDelta * profile.CursorSensitivity, -profile.MaximumCursorSpeed * 0.1f, profile.MaximumCursorSpeed * 0.1f);
        float left = LeftCursor, right = RightCursor;
        bool sameSoloPlayer = LeftPlayer == RightPlayer && LeftPlayer == clientId;
        if (LeftPlayer == clientId) left = Mathf.Clamp01(left + amount);
        if (RightPlayer == clientId && !sameSoloPlayer) right = Mathf.Clamp01(right + amount);
        if (sameSoloPlayer) right = Mathf.MoveTowards(right, left, profile.AutomaticPartnerSpeed * 0.1f);
        SetCursors(left, right);
    }

    [ServerRpc(RequireOwnership = false)] public void SubmitCursorDeltaServerRpc(float delta, ServerRpcParams rpc = default) => SubmitCursorDelta(delta, rpc.Receive.SenderClientId);

    private void Update()
    {
        if (!HasAuthority || State != WheelbarrowPouringState.Active || wheelbarrow == null || profile == null) return;
        float difference = Mathf.Abs(LeftCursor - RightCursor);
        float synchronizedProgress = difference <= profile.SynchronizedTolerance ? Mathf.Min(LeftCursor, RightCursor) : 0f;
        wheelbarrow.PhysicsBody.MoveRotation(transportRotation * Quaternion.Euler(profile.MaximumPourAngle * synchronizedProgress, 0f, 0f));

        if (difference > profile.CriticalDifference)
        {
            if (criticalDeadlineNetwork.Value <= 0f) criticalDeadlineNetwork.Value = Time.time + profile.CriticalDifferenceDuration;
            else if (Time.time >= criticalDeadlineNetwork.Value) Complete(false);
        }
        else criticalDeadlineNetwork.Value = 0f;

        if (LeftCursor >= 0.999f && RightCursor >= 0.999f && difference <= profile.SynchronizedTolerance) Complete(true);
    }

    private void Complete(bool success)
    {
        if (!HasAuthority || wheelbarrow == null) return;
        SetState(success ? WheelbarrowPouringState.Success : WheelbarrowPouringState.CriticalFailure);
        wheelbarrow.RestoreSecuredDockPose();
        if (success)
        {
            if (wheelbarrow.ConsumeConcreteLoad()) site?.TryAcceptConcreteLoad(1);
        }
        else wheelbarrow.SpillConcrete();
        ReleasePlayers();
        wheelbarrow.SetPouringState(false);
    }

    public void RequestLeave(ulong clientId)
    {
        if (!HasAuthority) return;
        if (LeftPlayer == clientId) SetLeftPlayer(NoPlayer);
        if (RightPlayer == clientId) SetRightPlayer(NoPlayer);
        if (State == WheelbarrowPouringState.Active)
        {
            SetState(WheelbarrowPouringState.WaitingForPlayers);
            SetCursors(0f, 0f);
            wheelbarrow?.RestoreSecuredDockPose();
            wheelbarrow?.SetPouringState(false);
        }
    }

    [ServerRpc(RequireOwnership = false)] public void RequestLeaveServerRpc(ServerRpcParams rpc = default) => RequestLeave(rpc.Receive.SenderClientId);

    public void CancelAndRelease(bool loseConcrete)
    {
        if (!HasAuthority || wheelbarrow == null) return;
        if (loseConcrete) wheelbarrow.SpillConcrete();
        ReleasePlayers();
        wheelbarrow.RestoreSecuredDockPose();
        SetState(WheelbarrowPouringState.Inactive);
    }

    private void ReleasePlayers() { SetLeftPlayer(NoPlayer); SetRightPlayer(NoPlayer); SetCursors(0f, 0f); }

    public Transform GetAnchor(ulong clientId)
    {
        if (LeftPlayer == clientId && RightPlayer == clientId)
            return soloPlayerAnchor != null ? soloPlayerAnchor : leftPlayerAnchor;
        if (LeftPlayer == clientId) return leftPlayerAnchor;
        if (RightPlayer == clientId) return rightPlayerAnchor;
        return null;
    }

    public bool IsPlayersTurn(ulong clientId) => IsPlayerParticipant(clientId);
    public float GetCursor(ulong clientId) => LeftPlayer == clientId ? LeftCursor : RightCursor;

    private void SetState(WheelbarrowPouringState value) { localState = value; if (IsNetworkActive && IsServer) stateNetwork.Value = (byte)value; }
    private void SetLeftPlayer(ulong value) { localLeftPlayer = value; if (IsNetworkActive && IsServer) leftPlayerNetwork.Value = value; }
    private void SetRightPlayer(ulong value) { localRightPlayer = value; if (IsNetworkActive && IsServer) rightPlayerNetwork.Value = value; }
    private void SetCursors(float left, float right) { localLeft = left; localRight = right; if (IsNetworkActive && IsServer) { leftCursorNetwork.Value = left; rightCursorNetwork.Value = right; } }
}
