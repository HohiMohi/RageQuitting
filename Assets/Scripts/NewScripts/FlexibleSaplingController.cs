using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum FlexibleSaplingState
{
    WaitingForPlayers,
    Ready,
    Pulling,
    Uprooted,
    Broken,
    StumpDigging,
    Cleared
}

public enum FlexibleSaplingGripSide
{
    Left = -1,
    Right = 1
}

public enum FlexibleSaplingPullFeedback
{
    None,
    WaitingForEvaluation,
    Success,
    Failure
}

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class FlexibleSaplingController : NetworkBehaviour, IInteractableNew, IDamageable, IInteractionPromptProvider, IActionImpactSurfaceProvider
{
    private const ulong NoPlayer = ulong.MaxValue;

    [Header("Configuration")]
    [SerializeField] private FlexibleSaplingProfileSO profile;
    [SerializeField] private bool allowSinglePlayerTesting;

    [Header("Interaction")]
    [SerializeField] private Transform leftGripPoint;
    [SerializeField] private Transform rightGripPoint;
    [SerializeField] private Collider interactionCollider;

    [Header("Visuals")]
    [SerializeField] private Transform flexibleVisualRoot;
    [SerializeField] private GameObject intactVisual;
    [SerializeField] private GameObject stumpVisual;

    private readonly NetworkVariable<int> stateNetwork = new NetworkVariable<int>((int)FlexibleSaplingState.WaitingForPlayers);
    private readonly NetworkVariable<float> tiltNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<int> completedPullsNetwork = new NetworkVariable<int>();
    private readonly NetworkVariable<int> activeSideNetwork = new NetworkVariable<int>((int)FlexibleSaplingGripSide.Left);
    private readonly NetworkVariable<ulong> leftPlayerNetwork = new NetworkVariable<ulong>(NoPlayer);
    private readonly NetworkVariable<ulong> rightPlayerNetwork = new NetworkVariable<ulong>(NoPlayer);
    private readonly NetworkVariable<int> stumpHitsNetwork = new NetworkVariable<int>();
    private readonly NetworkVariable<int> pullFeedbackNetwork = new NetworkVariable<int>();
    private readonly NetworkVariable<double> stageDeadlineNetwork = new NetworkVariable<double>(-1d);
    private readonly NetworkVariable<float> stageTimeLimitNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<ulong> acknowledgedInputClientNetwork = new NetworkVariable<ulong>(NoPlayer);
    private readonly NetworkVariable<int> acknowledgedInputSequenceNetwork = new NetworkVariable<int>();

    private Quaternion visualBaseRotation;
    private float visualTilt;
    private float lastAcceptedInputTime;
    private float nextLocalInputSendTime;
    private float nextLeftServerInputTime;
    private float nextRightServerInputTime;
    private float turnStartTilt;
    private float evaluationTime;
    private float feedbackEndTime;
    private bool rewardSpawned;
    private PlayerInteractionNew localPlayer;
    private float accumulatedLocalDownwardDelta;
    private float locallyPredictedTilt;
    private float localPredictionVelocity;
    private int localInputSequence;
    private FlexibleSaplingGripSide localPredictionSide;

    public FlexibleSaplingState CurrentState => (FlexibleSaplingState)stateNetwork.Value;
    public float NormalizedTilt => tiltNetwork.Value;
    public int CompletedPulls => completedPullsNetwork.Value;
    public int RequiredPulls => profile != null ? Mathf.Max(1, profile.requiredPulls) : 10;
    public FlexibleSaplingGripSide ActiveSide => (FlexibleSaplingGripSide)activeSideNetwork.Value;
    public int StumpHits => stumpHitsNetwork.Value;
    public int RequiredStumpHits => profile != null ? Mathf.Max(1, profile.stumpShovelHits) : 12;
    public float TargetTiltMinimum => Mathf.Max(0f, GetTargetCenter() - GetTargetZoneHalfWidth());
    public float TargetTiltMaximum => Mathf.Min(GetBreakingTilt() - 0.01f, GetTargetCenter() + GetTargetZoneHalfWidth());
    public bool IsCurrentStageTimed => CompletedPulls > 0 && stageDeadlineNetwork.Value >= 0d;
    public float CurrentStageTimeLimit => stageTimeLimitNetwork.Value;
    public float RemainingStageTime => IsCurrentStageTimed
        ? Mathf.Max(0f, (float)(stageDeadlineNetwork.Value - GetSynchronizedTime()))
        : 0f;
    public float NormalizedRemainingStageTime => IsCurrentStageTimed && CurrentStageTimeLimit > 0f
        ? Mathf.Clamp01(RemainingStageTime / CurrentStageTimeLimit)
        : 0f;
    public bool IsWaitingForPullEvaluation => PullFeedback == FlexibleSaplingPullFeedback.WaitingForEvaluation;
    public FlexibleSaplingPullFeedback PullFeedback => (FlexibleSaplingPullFeedback)pullFeedbackNetwork.Value;
    public bool IsCleared => CurrentState == FlexibleSaplingState.Cleared || CurrentState == FlexibleSaplingState.Uprooted;
    public bool CanApplyTool(EquippableItemType tool) =>
        CurrentState == FlexibleSaplingState.StumpDigging && tool == EquippableItemType.Shovel;
    public ActionImpactSurfaceType ImpactSurfaceType => CurrentState == FlexibleSaplingState.StumpDigging
        ? ActionImpactSurfaceType.Soil
        : ActionImpactSurfaceType.Wood;

    private void Awake()
    {
        if (flexibleVisualRoot != null)
        {
            visualBaseRotation = flexibleVisualRoot.localRotation;
        }

        ApplyVisualState();
    }

    public override void OnNetworkSpawn()
    {
        stateNetwork.OnValueChanged += OnStateChanged;
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        ApplyVisualState();
    }

    public override void OnNetworkDespawn()
    {
        stateNetwork.OnValueChanged -= OnStateChanged;
        if (NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void Update()
    {
        UpdateVisualTilt();
        UpdateClientPredictionReconciliation();
        if (!IsAuthority() || !IsPullingState())
        {
            return;
        }

        if (IsCurrentStageTimed && GetSynchronizedTime() >= stageDeadlineNetwork.Value)
        {
            HandleStageTimeout();
            return;
        }

        if (PullFeedback == FlexibleSaplingPullFeedback.WaitingForEvaluation && Time.time >= evaluationTime)
        {
            EvaluateCurrentPull();
            return;
        }

        if ((PullFeedback == FlexibleSaplingPullFeedback.Success || PullFeedback == FlexibleSaplingPullFeedback.Failure) &&
            Time.time >= feedbackEndTime)
        {
            pullFeedbackNetwork.Value = (int)FlexibleSaplingPullFeedback.None;
            BeginCurrentStageTimer();
        }

        if (PullFeedback == FlexibleSaplingPullFeedback.None && Time.time - lastAcceptedInputTime > GetInputTimeout())
        {
            tiltNetwork.Value = Mathf.MoveTowards(tiltNetwork.Value, 0f, GetRecenterSpeed() * Time.deltaTime);
            turnStartTilt = tiltNetwork.Value;
        }
    }

    public void Interact(Transform interactor)
    {
        PlayerInteractionNew player = interactor != null ? interactor.GetComponentInParent<PlayerInteractionNew>() : null;
        if (player == null)
        {
            return;
        }

        if (IsLocalParticipant(player))
        {
            Release(player);
        }
        else
        {
            TryJoin(player);
        }
    }

    public bool TryJoin(PlayerInteractionNew player)
    {
        if (player == null || player.IsHoldingObject)
        {
            return false;
        }

        NetworkObject playerObject = player.GetComponent<NetworkObject>();
        if (IsNetworkSessionActive())
        {
            if (playerObject == null)
            {
                return false;
            }

            if (IsServer)
            {
                return TryJoinServer(playerObject.OwnerClientId, playerObject);
            }

            RequestJoinServerRpc(playerObject.NetworkObjectId);
            return true;
        }

        return TryJoinLocal(player);
    }

    public void Release(PlayerInteractionNew player)
    {
        if (player == null)
        {
            return;
        }

        NetworkObject playerObject = player.GetComponent<NetworkObject>();
        if (IsNetworkSessionActive())
        {
            if (playerObject == null)
            {
                return;
            }

            if (IsServer)
            {
                ReleaseServer(playerObject.OwnerClientId, true);
            }
            else
            {
                RequestReleaseServerRpc();
            }
            return;
        }

        FlexibleSaplingGripSide side = player.ActiveFlexibleSaplingSide;
        MovePlayerToSafeReleasePosition(player.gameObject, side);
        ClearLocalSession(player);
        localPlayer = null;
        leftPlayerNetwork.Value = NoPlayer;
        rightPlayerNetwork.Value = NoPlayer;
        SetWaitingState();
    }

    public void SubmitLocalPull(PlayerInteractionNew player, float downwardDelta)
    {
        if (player == null || !IsPlayersTurn(player) ||
            PullFeedback == FlexibleSaplingPullFeedback.Success || PullFeedback == FlexibleSaplingPullFeedback.Failure)
        {
            return;
        }

        if (downwardDelta > 0f)
        {
            float acceptedLocalDelta = Mathf.Clamp(downwardDelta, 0f, 40f);
            accumulatedLocalDownwardDelta += acceptedLocalDelta;
            ApplyLocalPullPrediction(player, acceptedLocalDelta);
        }

        if (Time.unscaledTime < nextLocalInputSendTime || accumulatedLocalDownwardDelta < 0.001f)
        {
            return;
        }

        float sendInterval = profile != null ? Mathf.Max(0.02f, profile.inputSendInterval) : 1f / 30f;
        nextLocalInputSendTime = Time.unscaledTime + sendInterval;
        float accumulatedDelta = accumulatedLocalDownwardDelta;
        accumulatedLocalDownwardDelta = 0f;
        int sequence = ++localInputSequence;
        NetworkObject playerObject = player.GetComponent<NetworkObject>();
        FlexibleSaplingGripSide requestedSide = ActiveSide;
        if (IsNetworkSessionActive())
        {
            if (playerObject == null)
            {
                return;
            }

            if (IsServer)
            {
                ApplyPullInput(playerObject.OwnerClientId, requestedSide, accumulatedDelta, sequence);
            }
            else
            {
                SubmitPullServerRpc((int)requestedSide, accumulatedDelta, sequence);
            }
            return;
        }

        ApplyPullInput(0, requestedSide, accumulatedDelta, sequence);
    }

    public float GetDisplayTilt(PlayerInteractionNew player)
    {
        if (!ShouldUseLocalPrediction(player))
        {
            return NormalizedTilt;
        }

        return locallyPredictedTilt;
    }

    public bool IsLocalParticipant(PlayerInteractionNew player)
    {
        if (player == null)
        {
            return false;
        }

        if (!IsNetworkSessionActive())
        {
            return player.ActiveFlexibleSapling == this;
        }

        NetworkObject playerObject = player.GetComponent<NetworkObject>();
        return playerObject != null && IsPlayerAssigned(playerObject.OwnerClientId);
    }

    public bool IsPlayersTurn(PlayerInteractionNew player)
    {
        if (player == null || !IsPullingState())
        {
            return false;
        }

        if (!IsNetworkSessionActive())
        {
            return allowSinglePlayerTesting || player.ActiveFlexibleSaplingSide == ActiveSide;
        }

        NetworkObject playerObject = player.GetComponent<NetworkObject>();
        return playerObject != null && GetPlayerForSide(ActiveSide) == playerObject.OwnerClientId;
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        EquippableItemType tool = equippableItemSO != null ? equippableItemSO.itemType : EquippableItemType.None;
        if (IsNetworkSessionActive() && !IsServer)
        {
            RequestStumpHitServerRpc((int)tool);
            return;
        }

        ApplyStumpHit(tool);
    }

    public void DamageReceived(float damage) { }

    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        switch (CurrentState)
        {
            case FlexibleSaplingState.WaitingForPlayers:
            case FlexibleSaplingState.Ready:
            case FlexibleSaplingState.Pulling:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact,
                    IsInteractorAssigned(interactor) ? "Release sapling" : "Grip flexible sapling"));
                break;
            case FlexibleSaplingState.StumpDigging:
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
                    $"Dig out stump - {StumpHits} / {RequiredStumpHits}"));
                break;
        }
    }

    private bool TryJoinLocal(PlayerInteractionNew player)
    {
        if (!CanJoin(player.transform.position))
        {
            return false;
        }

        FlexibleSaplingGripSide side = GetClosestAvailableSide(player.transform.position);
        if (allowSinglePlayerTesting)
        {
            leftPlayerNetwork.Value = 0;
            rightPlayerNetwork.Value = 0;
        }
        else if (side == FlexibleSaplingGripSide.Left)
        {
            leftPlayerNetwork.Value = 0;
        }
        else
        {
            rightPlayerNetwork.Value = 0;
        }

        BeginLocalSession(player, side);
        localPlayer = player;
        RefreshPullingState();
        return true;
    }

    private bool TryJoinServer(ulong clientId, NetworkObject playerObject)
    {
        if (playerObject == null || IsPlayerAssigned(clientId) || !CanJoin(playerObject.transform.position))
        {
            return false;
        }

        PlayerInteractionNew interaction = playerObject.GetComponent<PlayerInteractionNew>();
        PlayerHealth health = playerObject.GetComponent<PlayerHealth>();
        if (interaction == null || interaction.IsHoldingObject || (health != null && health.IsDowned))
        {
            return false;
        }

        FlexibleSaplingGripSide side = GetClosestAvailableSide(playerObject.transform.position);
        if (allowSinglePlayerTesting && GetAssignedPlayerCount() == 0)
        {
            leftPlayerNetwork.Value = clientId;
            rightPlayerNetwork.Value = clientId;
        }
        else if (side == FlexibleSaplingGripSide.Left)
        {
            leftPlayerNetwork.Value = clientId;
        }
        else
        {
            rightPlayerNetwork.Value = clientId;
        }

        PlacePlayer(playerObject, side);
        BeginSessionClientRpc(NetworkObjectId, (int)side, CreateTargetRpc(clientId));
        RefreshPullingState();
        return true;
    }

    private void ApplyPullInput(ulong clientId, FlexibleSaplingGripSide side, float downwardDelta, int sequence)
    {
        if (!IsPullingState() || side != ActiveSide || GetPlayerForSide(side) != clientId ||
            PullFeedback == FlexibleSaplingPullFeedback.Success || PullFeedback == FlexibleSaplingPullFeedback.Failure)
        {
            return;
        }

        float now = Time.unscaledTime;
        float nextAllowed = side == FlexibleSaplingGripSide.Left ? nextLeftServerInputTime : nextRightServerInputTime;
        if (IsNetworkSessionActive() && now < nextAllowed)
        {
            return;
        }

        float interval = profile != null ? Mathf.Max(0.02f, profile.inputSendInterval) : 0.05f;
        if (side == FlexibleSaplingGripSide.Left)
        {
            nextLeftServerInputTime = now + interval * 0.8f;
        }
        else
        {
            nextRightServerInputTime = now + interval * 0.8f;
        }

        if (downwardDelta <= 0f)
        {
            return;
        }

        if (IsNetworkSessionActive())
        {
            acknowledgedInputClientNetwork.Value = clientId;
            acknowledgedInputSequenceNetwork.Value = sequence;
        }

        float expectedSign = side == FlexibleSaplingGripSide.Left ? -1f : 1f;
        // The packet contains input accumulated over several rendered frames.
        // Keep a packet-level safety cap without truncating normal 30 Hz input.
        float clampedDelta = Mathf.Clamp(downwardDelta, 0f, 240f) * expectedSign;
        lastAcceptedInputTime = Time.time;
        float sensitivity = profile != null ? Mathf.Max(0.001f, profile.mouseSensitivity) : 0.012f;
        float nextTilt = tiltNetwork.Value + clampedDelta * sensitivity;
        float breakingTilt = profile != null ? Mathf.Max(0.2f, profile.breakingTilt) : 0.92f;
        if (Mathf.Abs(nextTilt) >= breakingTilt)
        {
            BreakSapling(clientId);
            return;
        }

        tiltNetwork.Value = Mathf.Clamp(nextTilt, -1f, 1f);
        if (Mathf.Abs(tiltNetwork.Value - turnStartTilt) < GetMinimumGestureTravel())
        {
            CancelPendingEvaluation();
            return;
        }

        pullFeedbackNetwork.Value = (int)FlexibleSaplingPullFeedback.WaitingForEvaluation;
        evaluationTime = Time.time + GetPullEvaluationDelay();
    }

    private void EvaluateCurrentPull()
    {
        float signedTilt = tiltNetwork.Value * (ActiveSide == FlexibleSaplingGripSide.Left ? -1f : 1f);
        bool successful = signedTilt >= TargetTiltMinimum && signedTilt <= TargetTiltMaximum;
        if (!successful)
        {
            completedPullsNetwork.Value = Mathf.Max(0, completedPullsNetwork.Value - 1);
            tiltNetwork.Value = turnStartTilt;
            SetPullFeedback(FlexibleSaplingPullFeedback.Failure);
            return;
        }

        completedPullsNetwork.Value++;
        SetPullFeedback(FlexibleSaplingPullFeedback.Success);
        if (completedPullsNetwork.Value >= RequiredPulls)
        {
            UprootSapling();
            return;
        }

        activeSideNetwork.Value = ActiveSide == FlexibleSaplingGripSide.Left
            ? (int)FlexibleSaplingGripSide.Right
            : (int)FlexibleSaplingGripSide.Left;
        turnStartTilt = tiltNetwork.Value;
        lastAcceptedInputTime = Time.time;
    }

    private void SetPullFeedback(FlexibleSaplingPullFeedback feedback)
    {
        ClearStageTimer();
        pullFeedbackNetwork.Value = (int)feedback;
        feedbackEndTime = Time.time + 0.25f;
        evaluationTime = 0f;
        lastAcceptedInputTime = Time.time;
    }

    private void HandleStageTimeout()
    {
        CancelPendingEvaluation();
        completedPullsNetwork.Value = Mathf.Max(0, completedPullsNetwork.Value - 1);
        tiltNetwork.Value = turnStartTilt;
        activeSideNetwork.Value = ActiveSide == FlexibleSaplingGripSide.Left
            ? (int)FlexibleSaplingGripSide.Right
            : (int)FlexibleSaplingGripSide.Left;
        turnStartTilt = tiltNetwork.Value;
        SetPullFeedback(FlexibleSaplingPullFeedback.Failure);
    }

    private void BeginCurrentStageTimer()
    {
        ClearStageTimer();
        if (!IsPullingState() || completedPullsNetwork.Value <= 0)
        {
            return;
        }

        float duration = GetCurrentStageTimeLimit();
        stageTimeLimitNetwork.Value = duration;
        stageDeadlineNetwork.Value = GetSynchronizedTime() + duration;
    }

    private void ClearStageTimer()
    {
        stageDeadlineNetwork.Value = -1d;
        stageTimeLimitNetwork.Value = 0f;
    }

    private void CancelPendingEvaluation()
    {
        if (PullFeedback == FlexibleSaplingPullFeedback.WaitingForEvaluation)
        {
            pullFeedbackNetwork.Value = (int)FlexibleSaplingPullFeedback.None;
        }
        evaluationTime = 0f;
    }

    private void BreakSapling(ulong activeClientId)
    {
        CancelPullEvaluation();
        ulong partnerClientId = activeClientId == leftPlayerNetwork.Value ? rightPlayerNetwork.Value : leftPlayerNetwork.Value;
        Vector3 activePosition = TryGetPlayerObject(activeClientId, out NetworkObject activePlayer)
            ? activePlayer.transform.position
            : transform.position - transform.right;
        Vector3 partnerPosition = TryGetPlayerObject(partnerClientId, out NetworkObject partnerPlayer)
            ? partnerPlayer.transform.position
            : transform.position + transform.right;
        Vector3 direction = Vector3.ProjectOnPlane(partnerPosition - activePosition, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = transform.right;
        }

        ReleaseAllPlayers(false);
        stateNetwork.Value = (int)FlexibleSaplingState.Broken;
        tiltNetwork.Value = Mathf.Sign(tiltNetwork.Value == 0f ? 1f : tiltNetwork.Value);
        ApplyVisualState();

        ApplyImpulse(activePlayer, profile != null ? profile.activePlayerImpulse : null, direction);
        ApplyImpulse(partnerPlayer, profile != null ? profile.partnerImpulse : null, direction);
        stateNetwork.Value = (int)FlexibleSaplingState.StumpDigging;
        ApplyVisualState();
    }

    private void UprootSapling()
    {
        CancelPullEvaluation();
        stateNetwork.Value = (int)FlexibleSaplingState.Uprooted;
        tiltNetwork.Value = 0f;
        ApplyVisualState();
        ReleaseAllPlayers(true);
        SpawnReward();
    }

    private void ApplyStumpHit(EquippableItemType tool)
    {
        if (!IsAuthority() || CurrentState != FlexibleSaplingState.StumpDigging || tool != EquippableItemType.Shovel)
        {
            return;
        }

        stumpHitsNetwork.Value++;
        if (stumpHitsNetwork.Value >= RequiredStumpHits)
        {
            stateNetwork.Value = (int)FlexibleSaplingState.Cleared;
            SpawnReward();
            ApplyVisualState();
        }
    }

    private void SpawnReward()
    {
        if (rewardSpawned || profile == null || profile.uprootedProduct == null)
        {
            return;
        }

        rewardSpawned = BaseResourceSpawnUtility.TrySpawnResource(
            profile.uprootedProduct,
            transform.position + Vector3.up * 0.35f,
            Quaternion.identity,
            out _);
    }

    private void RefreshPullingState()
    {
        int assigned = GetAssignedPlayerCount();
        int required = allowSinglePlayerTesting ? 1 : profile != null ? Mathf.Max(2, profile.requiredPlayers) : 2;
        stateNetwork.Value = assigned >= required
            ? (int)FlexibleSaplingState.Pulling
            : assigned > 0 ? (int)FlexibleSaplingState.Ready : (int)FlexibleSaplingState.WaitingForPlayers;
        lastAcceptedInputTime = Time.time;
        CancelPullEvaluation();
        turnStartTilt = tiltNetwork.Value;
        if (stateNetwork.Value == (int)FlexibleSaplingState.Pulling)
        {
            BeginCurrentStageTimer();
        }
    }

    private void ReleaseServer(ulong clientId, bool notifyClient)
    {
        FlexibleSaplingGripSide side = leftPlayerNetwork.Value == clientId
            ? FlexibleSaplingGripSide.Left
            : FlexibleSaplingGripSide.Right;
        bool released = false;
        if (leftPlayerNetwork.Value == clientId)
        {
            leftPlayerNetwork.Value = NoPlayer;
            released = true;
        }
        if (rightPlayerNetwork.Value == clientId)
        {
            rightPlayerNetwork.Value = NoPlayer;
            released = true;
        }
        if (released && notifyClient)
        {
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            bool hasSafePose = TryGetPlayerObject(clientId, out NetworkObject playerObject) &&
                               TryFindSafeReleasePose(playerObject.gameObject, side, out position, out rotation);
            if (hasSafePose)
            {
                ApplyPlayerReleasePose(playerObject.gameObject, position, rotation);
            }
            EndSessionClientRpc(NetworkObjectId, hasSafePose, position, rotation, CreateTargetRpc(clientId));
        }
        if (released && !IsCleared && CurrentState != FlexibleSaplingState.StumpDigging)
        {
            CancelPullEvaluation();
            tiltNetwork.Value = 0f;
            RefreshPullingState();
        }
    }

    private void ReleaseAllPlayers(bool repositionSafely)
    {
        CancelPullEvaluation();
        if (!IsNetworkSessionActive())
        {
            if (localPlayer != null)
            {
                if (repositionSafely)
                {
                    MovePlayerToSafeReleasePosition(localPlayer.gameObject, localPlayer.ActiveFlexibleSaplingSide);
                }
                else
                {
                    ResetPlayerMovement(localPlayer.gameObject);
                }
                ClearLocalSession(localPlayer);
                localPlayer = null;
            }
            leftPlayerNetwork.Value = NoPlayer;
            rightPlayerNetwork.Value = NoPlayer;
            return;
        }

        ulong left = leftPlayerNetwork.Value;
        ulong right = rightPlayerNetwork.Value;
        if (left != NoPlayer)
        {
            ReleasePlayerClient(left, FlexibleSaplingGripSide.Left, repositionSafely);
        }
        if (right != NoPlayer && right != left)
        {
            ReleasePlayerClient(right, FlexibleSaplingGripSide.Right, repositionSafely);
        }
        leftPlayerNetwork.Value = NoPlayer;
        rightPlayerNetwork.Value = NoPlayer;
    }

    private void ClearLocalSession(PlayerInteractionNew player)
    {
        player?.EndFlexibleSaplingSession(this);
    }

    private void BeginLocalSession(PlayerInteractionNew player, FlexibleSaplingGripSide side)
    {
        Transform grip = GetGrip(side);
        if (grip != null)
        {
            player.transform.SetPositionAndRotation(grip.position, grip.rotation);
        }
        player.BeginFlexibleSaplingSession(this, side);
        ResetLocalPrediction();
    }

    private void PlacePlayer(NetworkObject playerObject, FlexibleSaplingGripSide side)
    {
        Transform grip = GetGrip(side);
        if (playerObject != null && grip != null)
        {
            CharacterController controller = playerObject.GetComponent<CharacterController>();
            bool enabled = controller != null && controller.enabled;
            if (enabled) controller.enabled = false;
            playerObject.transform.SetPositionAndRotation(grip.position, grip.rotation);
            if (enabled) controller.enabled = true;
        }
    }

    private void ReleasePlayerClient(ulong clientId, FlexibleSaplingGripSide side, bool repositionSafely)
    {
        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;
        NetworkObject playerObject = null;
        bool hasSafePose = repositionSafely && TryGetPlayerObject(clientId, out playerObject) &&
                           TryFindSafeReleasePose(playerObject.gameObject, side, out position, out rotation);
        if (hasSafePose)
        {
            ApplyPlayerReleasePose(playerObject.gameObject, position, rotation);
        }
        EndSessionClientRpc(NetworkObjectId, hasSafePose, position, rotation, CreateTargetRpc(clientId));
    }

    private void MovePlayerToSafeReleasePosition(GameObject playerObject, FlexibleSaplingGripSide side)
    {
        if (TryFindSafeReleasePose(playerObject, side, out Vector3 position, out Quaternion rotation))
        {
            ApplyPlayerReleasePose(playerObject, position, rotation);
        }
        else
        {
            ResetPlayerMovement(playerObject);
        }
    }

    private bool TryFindSafeReleasePose(GameObject playerObject, FlexibleSaplingGripSide side, out Vector3 position, out Quaternion rotation)
    {
        position = playerObject != null ? playerObject.transform.position : transform.position;
        Transform grip = GetGrip(side);
        rotation = grip != null ? grip.rotation : playerObject != null ? playerObject.transform.rotation : Quaternion.identity;
        CharacterController controller = playerObject != null ? playerObject.GetComponent<CharacterController>() : null;
        if (playerObject == null || controller == null || grip == null)
        {
            return false;
        }

        Vector3 outward = Vector3.ProjectOnPlane(grip.position - transform.position, Vector3.up).normalized;
        if (outward.sqrMagnitude < 0.001f)
        {
            outward = side == FlexibleSaplingGripSide.Left ? -transform.right : transform.right;
        }

        float clearance = profile != null ? Mathf.Max(0f, profile.releaseClearance) : 0.45f;
        float searchRadius = profile != null ? Mathf.Max(0.1f, profile.releaseSearchRadius) : 1.2f;
        float step = Mathf.Max(0.15f, controller.radius * 0.5f);
        Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;
        for (float distance = clearance; distance <= clearance + searchRadius + 0.001f; distance += step)
        {
            for (int lateralIndex = 0; lateralIndex < 5; lateralIndex++)
            {
                float lateral = lateralIndex == 0 ? 0f : ((lateralIndex & 1) == 1 ? 1f : -1f) * ((lateralIndex + 1) / 2) * step;
                Vector3 candidate = grip.position + outward * distance + right * lateral;
                if (TryGroundAndValidateCapsule(playerObject.transform, controller, candidate, rotation, out position))
                {
                    return true;
                }
            }
        }

        return TryGroundAndValidateCapsule(playerObject.transform, controller, playerObject.transform.position, rotation, out position);
    }

    private bool TryGroundAndValidateCapsule(Transform playerRoot, CharacterController controller, Vector3 candidate, Quaternion rotation, out Vector3 groundedPosition)
    {
        groundedPosition = candidate;
        float probeDistance = profile != null ? Mathf.Max(0.2f, profile.releaseGroundProbeDistance) : 1.5f;
        Vector3 probeOrigin = candidate + Vector3.up * (probeDistance * 0.5f + controller.height * 0.5f);
        RaycastHit[] groundHits = Physics.RaycastAll(probeOrigin, Vector3.down, probeDistance + controller.height, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(groundHits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in groundHits)
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(playerRoot) || hit.normal.y < 0.55f)
            {
                continue;
            }

            groundedPosition.y = hit.point.y - (controller.center.y - controller.height * 0.5f) + controller.skinWidth;
            if (IsPlayerCapsuleClear(playerRoot, controller, groundedPosition, rotation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlayerCapsuleClear(Transform playerRoot, CharacterController controller, Vector3 rootPosition, Quaternion rotation)
    {
        Vector3 center = rootPosition + rotation * controller.center;
        float radius = Mathf.Max(0.01f, controller.radius - controller.skinWidth * 0.5f);
        float halfSegment = Mathf.Max(0f, controller.height * 0.5f - radius);
        Vector3 pointA = center + Vector3.up * halfSegment;
        Vector3 pointB = center - Vector3.up * halfSegment;
        Collider[] overlaps = Physics.OverlapCapsule(pointA, pointB, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap != null && !overlap.transform.IsChildOf(playerRoot))
            {
                return false;
            }
        }
        return true;
    }

    private static void ApplyPlayerReleasePose(GameObject playerObject, Vector3 position, Quaternion rotation)
    {
        if (playerObject == null)
        {
            return;
        }
        CharacterController controller = playerObject.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        playerObject.transform.SetPositionAndRotation(position, rotation);
        if (wasEnabled) controller.enabled = true;
        ResetPlayerMovement(playerObject);
    }

    private static void ResetPlayerMovement(GameObject playerObject)
    {
        playerObject?.GetComponent<StarterAssets.FirstPersonController>()?.ResetMovementAfterForcedPlacement();
    }

    private FlexibleSaplingGripSide GetClosestAvailableSide(Vector3 position)
    {
        bool leftFree = leftPlayerNetwork.Value == NoPlayer;
        bool rightFree = rightPlayerNetwork.Value == NoPlayer;
        if (!leftFree) return FlexibleSaplingGripSide.Right;
        if (!rightFree) return FlexibleSaplingGripSide.Left;
        float leftDistance = leftGripPoint != null ? (leftGripPoint.position - position).sqrMagnitude : float.MaxValue;
        float rightDistance = rightGripPoint != null ? (rightGripPoint.position - position).sqrMagnitude : float.MaxValue;
        return leftDistance <= rightDistance ? FlexibleSaplingGripSide.Left : FlexibleSaplingGripSide.Right;
    }

    private bool CanJoin(Vector3 position)
    {
        return !IsCleared
            && CurrentState != FlexibleSaplingState.StumpDigging
            && GetAssignedPlayerCount() < (allowSinglePlayerTesting ? 1 : 2)
            && Vector3.Distance(position, transform.position) <= (profile != null ? profile.interactionDistance : 3f);
    }

    private void SetWaitingState()
    {
        if (!IsCleared && CurrentState != FlexibleSaplingState.StumpDigging)
        {
            CancelPullEvaluation();
            stateNetwork.Value = (int)FlexibleSaplingState.WaitingForPlayers;
            tiltNetwork.Value = 0f;
        }
    }

    private void UpdateVisualTilt()
    {
        float target = IsPullingState() ? GetLocallyPredictedVisualTilt() : 0f;
        float speed = profile != null ? profile.visualFollowSpeed : 12f;
        visualTilt = Mathf.MoveTowards(visualTilt, target, Mathf.Max(0f, speed) * Time.deltaTime);
        if (flexibleVisualRoot != null)
        {
            float degrees = profile != null ? profile.maximumVisualTiltDegrees : 32f;
            flexibleVisualRoot.localRotation = visualBaseRotation * Quaternion.Euler(0f, 0f, -visualTilt * degrees);
        }
    }

    private void ApplyLocalPullPrediction(PlayerInteractionNew player, float downwardDelta)
    {
        if (!ShouldUseLocalPrediction(player))
        {
            return;
        }

        if (localPredictionSide != ActiveSide)
        {
            ResetLocalPrediction();
        }

        localPredictionSide = ActiveSide;
        float sign = ActiveSide == FlexibleSaplingGripSide.Left ? -1f : 1f;
        float sensitivity = profile != null ? Mathf.Max(0.001f, profile.mouseSensitivity) : 0.012f;
        float contribution = Mathf.Clamp(downwardDelta, 0f, 40f) * sensitivity * sign;
        locallyPredictedTilt = Mathf.Clamp(
            locallyPredictedTilt + contribution,
            -GetBreakingTilt(),
            GetBreakingTilt());
    }

    private void UpdateClientPredictionReconciliation()
    {
        if (!IsNetworkSessionActive() || IsServer || NetworkManager?.LocalClient?.PlayerObject == null ||
            !NetworkManager.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew interaction))
        {
            return;
        }

        if (!ShouldUseLocalPrediction(interaction) || localPredictionSide != ActiveSide)
        {
            ResetLocalPrediction();
            return;
        }

        float directionSign = ActiveSide == FlexibleSaplingGripSide.Left ? -1f : 1f;
        float predictedProgress = locallyPredictedTilt * directionSign;
        float authoritativeProgress = tiltNetwork.Value * directionSign;

        // A pull only moves away from its turn origin. Network snapshots may arrive
        // one tick behind the acknowledgement, so never reconcile backwards mid-turn.
        if (authoritativeProgress > predictedProgress)
        {
            locallyPredictedTilt = Mathf.SmoothDamp(
                locallyPredictedTilt,
                tiltNetwork.Value,
                ref localPredictionVelocity,
                0.06f,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
        }
    }

    private bool ShouldUseLocalPrediction(PlayerInteractionNew player)
    {
        return player != null && IsNetworkSessionActive() && !IsServer && IsLocalParticipant(player) &&
               IsPlayersTurn(player) &&
               PullFeedback != FlexibleSaplingPullFeedback.Success &&
               PullFeedback != FlexibleSaplingPullFeedback.Failure;
    }

    private float GetLocallyPredictedVisualTilt()
    {
        if (NetworkManager?.LocalClient?.PlayerObject != null &&
            NetworkManager.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew interaction) &&
            ShouldUseLocalPrediction(interaction))
        {
            return locallyPredictedTilt;
        }

        return tiltNetwork.Value;
    }

    private void ResetLocalPrediction()
    {
        locallyPredictedTilt = tiltNetwork.Value;
        localPredictionVelocity = 0f;
        accumulatedLocalDownwardDelta = 0f;
        localPredictionSide = ActiveSide;
    }

    private void ApplyVisualState()
    {
        bool stump = CurrentState == FlexibleSaplingState.Broken || CurrentState == FlexibleSaplingState.StumpDigging;
        if (intactVisual != null) intactVisual.SetActive(!stump && !IsCleared);
        if (stumpVisual != null) stumpVisual.SetActive(stump);
        if (interactionCollider != null) interactionCollider.enabled = !IsCleared;
    }

    private void OnStateChanged(int previous, int current) => ApplyVisualState();
    private bool IsPullingState() => CurrentState == FlexibleSaplingState.Pulling;
    private float GetInputTimeout() => profile != null ? Mathf.Max(0.1f, profile.inputTimeout) : 3f;
    private float GetPullEvaluationDelay() => profile != null ? Mathf.Max(0.05f, profile.pullEvaluationDelay) : 0.45f;
    private float GetMinimumGestureTravel() => profile != null ? Mathf.Clamp(profile.minimumGestureTravel, 0.01f, 1f) : 0.15f;
    private float GetRecenterSpeed() => profile != null ? Mathf.Max(0f, profile.recenterSpeed) : 1.5f;
    private float GetBreakingTilt() => profile != null ? Mathf.Max(0.2f, profile.breakingTilt) : 0.92f;
    private float GetTargetZoneHalfWidth() => profile != null ? Mathf.Clamp(profile.targetZoneHalfWidth, 0.01f, 0.25f) : 0.07f;
    private float GetTargetCenter()
    {
        float initial = profile != null ? Mathf.Clamp(profile.initialTargetCenter, 0.1f, 0.9f) : 0.35f;
        float final = profile != null ? Mathf.Clamp(profile.finalTargetCenter, initial, GetBreakingTilt() - 0.01f) : 0.84f;
        float denominator = Mathf.Max(1f, RequiredPulls - 1f);
        float progress = Mathf.Clamp01(completedPullsNetwork.Value / denominator);
        float exponent = profile != null ? Mathf.Max(0.1f, profile.targetProgressExponent) : 2f;
        return Mathf.Lerp(initial, final, Mathf.Pow(progress, exponent));
    }
    private float GetCurrentStageTimeLimit()
    {
        float second = profile != null ? Mathf.Max(0.1f, profile.secondStageTimeLimit) : 5f;
        float final = profile != null ? Mathf.Max(0.1f, profile.finalStageTimeLimit) : 3.5f;
        float timedStageProgress = RequiredPulls <= 2
            ? 1f
            : Mathf.Clamp01((completedPullsNetwork.Value - 1f) / (RequiredPulls - 2f));
        return Mathf.Lerp(second, final, timedStageProgress);
    }
    private double GetSynchronizedTime()
    {
        return IsNetworkSessionActive() && NetworkManager != null
            ? NetworkManager.ServerTime.Time
            : Time.timeAsDouble;
    }
    private void CancelPullEvaluation()
    {
        ClearStageTimer();
        pullFeedbackNetwork.Value = (int)FlexibleSaplingPullFeedback.None;
        evaluationTime = 0f;
        feedbackEndTime = 0f;
    }
    private Transform GetGrip(FlexibleSaplingGripSide side) => side == FlexibleSaplingGripSide.Left ? leftGripPoint : rightGripPoint;
    private ulong GetPlayerForSide(FlexibleSaplingGripSide side) => side == FlexibleSaplingGripSide.Left ? leftPlayerNetwork.Value : rightPlayerNetwork.Value;
    private bool IsPlayerAssigned(ulong clientId) => leftPlayerNetwork.Value == clientId || rightPlayerNetwork.Value == clientId;
    private int GetAssignedPlayerCount() => leftPlayerNetwork.Value == NoPlayer ? (rightPlayerNetwork.Value == NoPlayer ? 0 : 1) : rightPlayerNetwork.Value == NoPlayer || rightPlayerNetwork.Value == leftPlayerNetwork.Value ? 1 : 2;
    private bool IsInteractorAssigned(Transform interactor) => interactor != null && IsLocalParticipant(interactor.GetComponentInParent<PlayerInteractionNew>());
    private bool IsAuthority() => !IsNetworkSessionActive() || IsServer;
    private bool IsNetworkSessionActive() => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    private bool TryGetPlayerObject(ulong clientId, out NetworkObject playerObject)
    {
        playerObject = null;
        return clientId != NoPlayer && NetworkManager != null && NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && (playerObject = client.PlayerObject) != null;
    }

    private static void ApplyImpulse(NetworkObject player, ExternalImpulseProfileSO impulseProfile, Vector3 direction)
    {
        if (player != null && impulseProfile != null && player.TryGetComponent(out IExternalImpulseReceiver receiver))
        {
            receiver.TryApplyExternalImpulse(impulseProfile.CreateImpulse(direction), null);
        }
    }

    private void OnClientDisconnected(ulong clientId) => ReleaseServer(clientId, false);

    [ServerRpc(RequireOwnership = false)]
    private void RequestJoinServerRpc(ulong playerObjectId, ServerRpcParams rpcParams = default)
    {
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out NetworkObject playerObject) && playerObject.OwnerClientId == rpcParams.Receive.SenderClientId)
        {
            TryJoinServer(rpcParams.Receive.SenderClientId, playerObject);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestReleaseServerRpc(ServerRpcParams rpcParams = default) => ReleaseServer(rpcParams.Receive.SenderClientId, true);

    [ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
    private void SubmitPullServerRpc(int sideValue, float delta, int sequence, ServerRpcParams rpcParams = default)
    {
        if (Enum.IsDefined(typeof(FlexibleSaplingGripSide), sideValue))
        {
            ApplyPullInput(rpcParams.Receive.SenderClientId, (FlexibleSaplingGripSide)sideValue, delta, sequence);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStumpHitServerRpc(int toolValue, ServerRpcParams rpcParams = default)
    {
        if (toolValue != (int)EquippableItemType.Shovel || !TryGetPlayerObject(rpcParams.Receive.SenderClientId, out NetworkObject player) || Vector3.Distance(player.transform.position, transform.position) > 3f)
        {
            return;
        }

        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        EquippableItemSO selectedItem = inventory != null ? inventory.GetSelectedItemForServerValidation() : null;
        if (selectedItem == null || selectedItem.itemType != EquippableItemType.Shovel)
        {
            return;
        }

        ApplyStumpHit(EquippableItemType.Shovel);
    }

    [ClientRpc]
    private void BeginSessionClientRpc(ulong saplingId, int sideValue, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.LocalClient?.PlayerObject != null && NetworkManager.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew interaction))
        {
            BeginLocalSession(interaction, (FlexibleSaplingGripSide)sideValue);
        }
    }

    [ClientRpc]
    private void EndSessionClientRpc(ulong saplingId, bool applySafePose, Vector3 position, Quaternion rotation, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.LocalClient?.PlayerObject != null && NetworkManager.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew interaction))
        {
            if (applySafePose)
            {
                ApplyPlayerReleasePose(interaction.gameObject, position, rotation);
            }
            else
            {
                ResetPlayerMovement(interaction.gameObject);
            }
            interaction.EndFlexibleSaplingSession(this);
        }
    }

    private static ClientRpcParams CreateTargetRpc(ulong clientId) => new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
}
