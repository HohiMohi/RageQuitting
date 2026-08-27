using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerInputNew), typeof(CharacterController))]
public class PlayerWheelbarrowController : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float inputSendInterval = 1f / 30f;
    [SerializeField, Min(0f)] private float anchorSnapDistance = 2f;

    private PlayerInputNew input;
    private CharacterController characterController;
    private StarterAssets.FirstPersonController firstPersonController;
    private NetworkObject networkObject;
    private PlayerStaminaController stamina;
    private PlayerHealth health;
    private PlayerTransportCollisionController transportCollisions;
    private WheelbarrowController current;
    private WheelbarrowController presentationWheelbarrow;
    private WheelbarrowController passengerBoardingWheelbarrow;
    private uint passengerBoardingToken;
    private bool passengerPlacementActive;
    private float passengerPlacementElapsed;
    private float passengerPlacementDuration;
    private Vector3 passengerPlacementStartPosition;
    private Quaternion passengerPlacementStartRotation;
    private WheelbarrowController safeExitWheelbarrow;
    private WheelbarrowController pouringCollisionWheelbarrow;
    private bool suppressAnchoredMovement;
    private WheelbarrowPouringMinigame pouringMinigame;
    private bool pourPlacementActive;
    private float pourPlacementElapsed;
    private float pourPlacementDuration;
    private Vector3 pourPlacementStartPosition;
    private Quaternion pourPlacementStartRotation;
    private Vector3 pourPlacementTargetPosition;
    private Quaternion pourPlacementTargetRotation;
    private WheelbarrowController righting;
    private float inputTimer;
    private float pendingPourDelta;
    private float exitDeniedMessageUntil;

    public WheelbarrowController CurrentWheelbarrow => pouringMinigame != null
        ? pouringMinigame.Wheelbarrow
        : passengerBoardingWheelbarrow != null ? passengerBoardingWheelbarrow : current;
    public WheelbarrowOccupantRole CurrentRole
    {
        get
        {
            if (pouringMinigame != null)
                return pouringMinigame.LeftPlayer == LocalClientId ? WheelbarrowOccupantRole.PourLeft : WheelbarrowOccupantRole.PourRight;
            if (passengerBoardingWheelbarrow != null) return WheelbarrowOccupantRole.Passenger;
            return current != null ? current.GetRole(LocalClientId) : WheelbarrowOccupantRole.None;
        }
    }
    public bool BlocksStandardMovement => CurrentRole != WheelbarrowOccupantRole.None;
    public bool BlocksCameraRotation => CurrentRole == WheelbarrowOccupantRole.PourLeft || CurrentRole == WheelbarrowOccupantRole.PourRight;
    private ulong LocalClientId => networkObject != null ? networkObject.OwnerClientId : 0;
    private bool IsLocalPlayer => networkObject == null || !IsNetworkActive || networkObject.IsOwner;
    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    private void Awake()
    {
        input = GetComponent<PlayerInputNew>();
        characterController = GetComponent<CharacterController>();
        firstPersonController = GetComponent<StarterAssets.FirstPersonController>();
        networkObject = GetComponent<NetworkObject>();
        stamina = GetComponent<PlayerStaminaController>();
        health = GetComponent<PlayerHealth>();
        transportCollisions = GetComponent<PlayerTransportCollisionController>();
        if (transportCollisions == null)
            transportCollisions = gameObject.AddComponent<PlayerTransportCollisionController>();
        if (!TryGetComponent(out WheelbarrowPassengerVisualOverride _))
            gameObject.AddComponent<WheelbarrowPassengerVisualOverride>();
        input.OnInteractCanceled += HandleInteractCanceled;
    }

    private void OnDestroy()
    {
        if (input != null) input.OnInteractCanceled -= HandleInteractCanceled;
        pouringCollisionWheelbarrow?.SetPlayerCollisionIgnored(transform, false);
        passengerBoardingWheelbarrow?.SetPlayerCollisionIgnored(transform, false);
        transportCollisions?.EndTransport(null);
        presentationWheelbarrow?.SetLocalPresentationInput(0f, 0f);
        stamina?.ClearDrainSource(StaminaDrainSource.WheelbarrowDriving);
    }

    private void Update()
    {
        if (!IsLocalPlayer) return;
        SetPouringMinigame(WheelbarrowPouringMinigame.FindForPlayer(LocalClientId));
        current = WheelbarrowController.FindForPlayer(LocalClientId);
        if (passengerBoardingWheelbarrow != null &&
            passengerBoardingWheelbarrow.PassengerClientId == LocalClientId && !passengerPlacementActive)
        {
            passengerBoardingWheelbarrow = null;
            passengerBoardingToken = 0;
        }
        WheelbarrowController pouringWheelbarrow = pouringMinigame != null ? pouringMinigame.Wheelbarrow : null;
        if (pouringCollisionWheelbarrow != pouringWheelbarrow)
        {
            pouringCollisionWheelbarrow?.SetPlayerCollisionIgnored(transform, false);
            pouringCollisionWheelbarrow = pouringWheelbarrow;
            pouringCollisionWheelbarrow?.SetPlayerCollisionIgnored(transform, true);
        }
        if (pouringMinigame != null)
        {
            Vector2 look = input.GetLookDeltaValueForMinigames();
            pendingPourDelta += look.y;
            inputTimer += Time.deltaTime;
            if (inputTimer >= (pouringMinigame.Profile != null ? pouringMinigame.Profile.InputSendInterval : inputSendInterval))
            {
                inputTimer = 0f;
                float delta = pendingPourDelta;
                pendingPourDelta = 0f;
                if (IsNetworkActive && !pouringMinigame.IsServer) pouringMinigame.SubmitCursorDeltaServerRpc(delta);
                else pouringMinigame.SubmitCursorDelta(delta, LocalClientId);
            }
            stamina?.ClearDrainSource(StaminaDrainSource.WheelbarrowDriving);
            return;
        }
        if (current == null)
        {
            presentationWheelbarrow?.SetLocalPresentationInput(0f, 0f);
            presentationWheelbarrow = null;
            stamina?.ClearDrainSource(StaminaDrainSource.WheelbarrowDriving);
            return;
        }

        if (CurrentRole == WheelbarrowOccupantRole.Driver)
        {
            Vector2 rawMove = input.GetMoveVectorValue();
            Vector2 move = new Vector2(
                Mathf.Clamp(rawMove.x, -1f, 1f),
                Mathf.Clamp(rawMove.y, -1f, 1f));
            if (presentationWheelbarrow != current)
            {
                presentationWheelbarrow?.SetLocalPresentationInput(0f, 0f);
                presentationWheelbarrow = current;
            }
            current.SetLocalPresentationInput(move.y, move.x);
            if (health != null && health.IsDowned)
            {
                if (IsNetworkActive && !current.IsServer) current.RequestExitServerRpc();
                else current.RequestExit(LocalClientId);
                return;
            }
            bool usesDrivingStamina = current.Profile == null || current.Profile.EnableDrivingStaminaDrain;
            if (usesDrivingStamina && stamina != null && stamina.CurrentStamina <= 0f) move.y = 0f;
            if (current.HasLocalPhysicsAuthority)
                current.SubmitDriveInput(move.y, move.x, LocalClientId);
            if (usesDrivingStamina)
                stamina?.SetDrainSource(StaminaDrainSource.WheelbarrowDriving, current.GetEstimatedDrivingStaminaDrain(move.y));
            else
                stamina?.ClearDrainSource(StaminaDrainSource.WheelbarrowDriving);
        }
        else
        {
            presentationWheelbarrow?.SetLocalPresentationInput(0f, 0f);
            presentationWheelbarrow = null;
            stamina?.ClearDrainSource(StaminaDrainSource.WheelbarrowDriving);
        }
    }

    public void ApplyAnchoredMovement(float deltaTime)
    {
        WheelbarrowController activeWheelbarrow = CurrentWheelbarrow;
        if (!IsLocalPlayer || suppressAnchoredMovement || activeWheelbarrow == null || characterController == null || !characterController.enabled) return;
        if (pouringMinigame != null)
        {
            ApplyPouringPlacement(deltaTime);
            return;
        }

        if (passengerBoardingWheelbarrow != null && passengerPlacementActive)
        {
            ApplyPassengerPlacement(deltaTime);
            return;
        }

        Transform anchor = pouringMinigame != null ? pouringMinigame.GetAnchor(LocalClientId) : activeWheelbarrow.GetRoleAnchor(LocalClientId);
        if (anchor == null) return;
        Vector3 targetPosition = anchor.position;
        Quaternion targetRotation = anchor.rotation;
        if (CurrentRole == WheelbarrowOccupantRole.Driver &&
            activeWheelbarrow.TryGetLocalDriverPresentationPose(LocalClientId, out Vector3 predictedPosition, out Quaternion predictedRotation))
        {
            targetPosition = predictedPosition;
            targetRotation = predictedRotation;
        }
        else if (CurrentRole == WheelbarrowOccupantRole.Passenger)
        {
            activeWheelbarrow.TryGetPresentedAnchorPose(anchor, out targetPosition, out targetRotation);
        }
        Vector3 delta = targetPosition - transform.position;
        float speed = CurrentRole == WheelbarrowOccupantRole.Driver
            ? (activeWheelbarrow.Profile != null ? activeWheelbarrow.Profile.DriverFollowSpeed : 8f)
            : (activeWheelbarrow.Profile != null ? activeWheelbarrow.Profile.PassengerFollowSpeed : 12f);
        Vector3 movement = delta.magnitude > anchorSnapDistance ? delta : Vector3.ClampMagnitude(delta, speed * deltaTime);
        characterController.Move(movement);
        if (CurrentRole == WheelbarrowOccupantRole.Passenger)
            transform.rotation = Quaternion.Euler(0f, targetRotation.eulerAngles.y, 0f);
    }

    public bool PreparePassengerBoarding(WheelbarrowController wheelbarrow, uint token, float duration)
    {
        if (!IsLocalPlayer || wheelbarrow == null || characterController == null) return false;
        if (passengerBoardingWheelbarrow != null &&
            (passengerBoardingWheelbarrow != wheelbarrow || passengerBoardingToken != token)) return false;
        if (transportCollisions == null || !transportCollisions.BeginTransport(wheelbarrow)) return false;

        passengerBoardingWheelbarrow = wheelbarrow;
        passengerBoardingToken = token;
        passengerPlacementActive = true;
        passengerPlacementElapsed = 0f;
        passengerPlacementDuration = Mathf.Max(0.05f, duration);
        passengerPlacementStartPosition = transform.position;
        passengerPlacementStartRotation = transform.rotation;
        firstPersonController?.ResetMovementAfterForcedPlacement();
        return true;
    }

    public void CancelPassengerBoarding(WheelbarrowController wheelbarrow, uint token)
    {
        if (passengerBoardingWheelbarrow != wheelbarrow || passengerBoardingToken != token) return;
        transportCollisions?.EndTransport(wheelbarrow);
        passengerBoardingWheelbarrow = null;
        passengerBoardingToken = 0;
        passengerPlacementActive = false;
        firstPersonController?.ResetMovementAfterForcedPlacement();
    }

    private void ApplyPassengerPlacement(float deltaTime)
    {
        if (passengerBoardingWheelbarrow == null || passengerBoardingWheelbarrow.PassengerAnchor == null)
        {
            passengerPlacementActive = false;
            return;
        }

        passengerPlacementElapsed += Mathf.Max(0f, deltaTime);
        float normalized = Mathf.Clamp01(passengerPlacementElapsed / passengerPlacementDuration);
        float eased = Mathf.SmoothStep(0f, 1f, normalized);
        Transform anchor = passengerBoardingWheelbarrow.PassengerAnchor;
        passengerBoardingWheelbarrow.TryGetPresentedAnchorPose(anchor, out Vector3 anchorPosition, out Quaternion anchorRotation);
        Vector3 desiredPosition = Vector3.Lerp(passengerPlacementStartPosition, anchorPosition, eased);
        characterController.Move(desiredPosition - transform.position);
        float yaw = Mathf.LerpAngle(passengerPlacementStartRotation.eulerAngles.y, anchorRotation.eulerAngles.y, eased);
        firstPersonController?.SetForcedViewYaw(yaw);

        if (normalized < 1f) return;
        passengerPlacementActive = false;
        if (passengerBoardingWheelbarrow.PassengerClientId == LocalClientId)
        {
            passengerBoardingWheelbarrow = null;
            passengerBoardingToken = 0;
        }
    }

    private void SetPouringMinigame(WheelbarrowPouringMinigame next)
    {
        if (pouringMinigame == next) return;
        pouringMinigame = next;
        pourPlacementActive = false;
        pourPlacementElapsed = 0f;

        if (pouringMinigame == null)
        {
            firstPersonController?.ResetLookRotationState();
            return;
        }

        if (!pouringMinigame.TryResolveAnchorPose(
            LocalClientId,
            characterController,
            out pourPlacementTargetPosition,
            out pourPlacementTargetRotation))
        {
            Transform fallback = pouringMinigame.GetAnchor(LocalClientId);
            if (fallback == null) return;
            pourPlacementTargetPosition = fallback.position;
            pourPlacementTargetRotation = fallback.rotation;
        }

        pourPlacementStartPosition = transform.position;
        pourPlacementStartRotation = transform.rotation;
        pourPlacementDuration = pouringMinigame.Profile != null
            ? pouringMinigame.Profile.ParticipantPlacementDuration
            : 0.25f;
        pourPlacementActive = true;
        firstPersonController?.ResetMovementAfterForcedPlacement();
    }

    private void ApplyPouringPlacement(float deltaTime)
    {
        if (pourPlacementActive)
        {
            pourPlacementElapsed += Mathf.Max(0f, deltaTime);
            float normalized = Mathf.Clamp01(pourPlacementElapsed / Mathf.Max(0.05f, pourPlacementDuration));
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            Vector3 desiredPosition = Vector3.Lerp(pourPlacementStartPosition, pourPlacementTargetPosition, eased);
            characterController.Move(desiredPosition - transform.position);
            float yaw = Mathf.LerpAngle(
                pourPlacementStartRotation.eulerAngles.y,
                pourPlacementTargetRotation.eulerAngles.y,
                eased);
            firstPersonController?.SetForcedViewYaw(yaw);
            if (normalized >= 1f) pourPlacementActive = false;
            return;
        }

        float followSpeed = CurrentWheelbarrow != null && CurrentWheelbarrow.Profile != null
            ? CurrentWheelbarrow.Profile.PassengerFollowSpeed
            : 12f;
        Vector3 correction = Vector3.ClampMagnitude(
            pourPlacementTargetPosition - transform.position,
            followSpeed * Mathf.Max(0f, deltaTime));
        characterController.Move(correction);
        firstPersonController?.SetForcedViewYaw(pourPlacementTargetRotation.eulerAngles.y);
    }

    public bool BeginSafeExit(WheelbarrowController wheelbarrow, Vector3 position)
    {
        if (!IsLocalPlayer || wheelbarrow == null) return false;
        safeExitWheelbarrow = wheelbarrow;
        suppressAnchoredMovement = true;
        bool wasEnabled = characterController != null && characterController.enabled;
        if (wasEnabled) characterController.enabled = false;
        transform.position = position;
        if (wasEnabled) characterController.enabled = true;
        transportCollisions?.EnsureSuppressed();
        GetComponent<StarterAssets.FirstPersonController>()?.ResetMovementAfterForcedPlacement();
        return true;
    }

    public void CompleteSafeExit(WheelbarrowController wheelbarrow)
    {
        if (safeExitWheelbarrow != wheelbarrow) return;
        safeExitWheelbarrow = null;
        suppressAnchoredMovement = false;
        transportCollisions?.EndTransport(wheelbarrow);
    }

    public void CompleteTechnicalSafeExit()
    {
        safeExitWheelbarrow = null;
        passengerBoardingWheelbarrow = null;
        passengerBoardingToken = 0;
        passengerPlacementActive = false;
        suppressAnchoredMovement = false;
        transportCollisions?.EndTransport(null);
        firstPersonController?.ResetMovementAfterForcedPlacement();
    }

    public void CancelSafeExitPlacement(WheelbarrowController wheelbarrow)
    {
        if (safeExitWheelbarrow != wheelbarrow) return;
        safeExitWheelbarrow = null;
        suppressAnchoredMovement = false;
    }

    public bool SetPassengerTransportCollisionState(WheelbarrowController wheelbarrow, bool active)
    {
        if (transportCollisions == null || wheelbarrow == null) return false;
        if (active)
        {
            bool applied = transportCollisions.BeginTransport(wheelbarrow);
            if (applied) transportCollisions.EnsureSuppressed();
            return applied;
        }
        transportCollisions.EndTransport(wheelbarrow);
        return true;
    }

    public void ShowExitDenied(float duration)
    {
        if (IsLocalPlayer) exitDeniedMessageUntil = Time.unscaledTime + Mathf.Max(0f, duration);
    }

    private void OnGUI()
    {
        if (!IsLocalPlayer || Time.unscaledTime >= exitDeniedMessageUntil) return;
        const float width = 260f;
        GUI.Label(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.68f, width, 28f),
            "No room to exit", GUI.skin.box);
    }

    public bool TryHandleInteractPressed()
    {
        if (!IsLocalPlayer) return false;
        if (pouringMinigame != null)
        {
            if (IsNetworkActive && !pouringMinigame.IsServer) pouringMinigame.RequestLeaveServerRpc();
            else pouringMinigame.RequestLeave(LocalClientId);
            return true;
        }
        if (current == null) return false;
        if (IsNetworkActive && !current.IsServer) current.RequestExitServerRpc();
        else current.RequestExit(LocalClientId);
        return true;
    }

    public void BeginRighting(WheelbarrowController wheelbarrow)
    {
        if (!IsLocalPlayer || wheelbarrow == null) return;
        righting = wheelbarrow;
        wheelbarrow.RequestBeginRighting(transform);
    }

    private void HandleInteractCanceled(object sender, EventArgs e)
    {
        if (righting == null) return;
        if (IsNetworkActive && !righting.IsServer) righting.CancelRightingServerRpc();
        else righting.CancelRighting();
        righting = null;
    }
}
