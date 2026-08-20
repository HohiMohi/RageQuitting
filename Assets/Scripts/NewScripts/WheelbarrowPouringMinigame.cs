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
    public WheelbarrowController Wheelbarrow => wheelbarrow;
    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool HasAuthority => !IsNetworkActive || IsServer;

    private void Awake() => Instances.Add(this);
    private void OnDestroy() => Instances.Remove(this);

    public static WheelbarrowPouringMinigame FindForPlayer(ulong clientId)
    {
        foreach (WheelbarrowPouringMinigame item in Instances)
            if (item != null && (item.LeftPlayer == clientId || item.RightPlayer == clientId)) return item;
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
        if (!IsNetworkActive) return Join(player.OwnerClientId, preferLeft);
        if (IsServer) return Join(player.OwnerClientId, preferLeft);
        RequestJoinServerRpc(preferLeft);
        return true;
    }

    [ServerRpc(RequireOwnership = false)] private void RequestJoinServerRpc(bool left, ServerRpcParams rpc = default) => Join(rpc.Receive.SenderClientId, left);

    private bool Join(ulong clientId, bool preferLeft)
    {
        if (!HasAuthority || wheelbarrow == null || !wheelbarrow.HasConcrete || State != WheelbarrowPouringState.WaitingForPlayers && State != WheelbarrowPouringState.Active) return false;
        if (LeftPlayer == clientId || RightPlayer == clientId) return true;
        if (preferLeft && LeftPlayer == NoPlayer || RightPlayer != NoPlayer && LeftPlayer == NoPlayer) SetLeftPlayer(clientId);
        else if (RightPlayer == NoPlayer) SetRightPlayer(clientId);
        else if (LeftPlayer == NoPlayer) SetLeftPlayer(clientId);
        else return false;

        bool ready = LeftPlayer != NoPlayer && RightPlayer != NoPlayer;
        if (!ready && profile != null && profile.AllowSinglePlayerTesting)
        {
            if (LeftPlayer == NoPlayer) SetLeftPlayer(clientId);
            if (RightPlayer == NoPlayer) SetRightPlayer(clientId);
            ready = true;
        }
        if (ready)
        {
            SetState(WheelbarrowPouringState.Active);
            wheelbarrow.SetPouringState(true);
        }
        return true;
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
        if (LeftPlayer == clientId && RightPlayer == clientId) return leftPlayerAnchor;
        if (LeftPlayer == clientId) return leftPlayerAnchor;
        if (RightPlayer == clientId) return rightPlayerAnchor;
        return null;
    }

    public bool IsPlayersTurn(ulong clientId) => LeftPlayer == clientId || RightPlayer == clientId;
    public float GetCursor(ulong clientId) => LeftPlayer == clientId ? LeftCursor : RightCursor;

    private void SetState(WheelbarrowPouringState value) { localState = value; if (IsNetworkActive && IsServer) stateNetwork.Value = (byte)value; }
    private void SetLeftPlayer(ulong value) { localLeftPlayer = value; if (IsNetworkActive && IsServer) leftPlayerNetwork.Value = value; }
    private void SetRightPlayer(ulong value) { localRightPlayer = value; if (IsNetworkActive && IsServer) rightPlayerNetwork.Value = value; }
    private void SetCursors(float left, float right) { localLeft = left; localRight = right; if (IsNetworkActive && IsServer) { leftCursorNetwork.Value = left; rightCursorNetwork.Value = right; } }
}
