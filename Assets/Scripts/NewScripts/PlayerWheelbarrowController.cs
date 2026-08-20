using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerInputNew), typeof(CharacterController))]
public class PlayerWheelbarrowController : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float inputSendInterval = 0.05f;
    [SerializeField, Min(0f)] private float anchorSnapDistance = 2f;

    private PlayerInputNew input;
    private CharacterController characterController;
    private NetworkObject networkObject;
    private PlayerStaminaController stamina;
    private PlayerHealth health;
    private WheelbarrowController current;
    private WheelbarrowController safeExitWheelbarrow;
    private WheelbarrowController pouringCollisionWheelbarrow;
    private bool suppressAnchoredMovement;
    private WheelbarrowPouringMinigame pouringMinigame;
    private WheelbarrowController righting;
    private float inputTimer;
    private float pendingPourDelta;

    public WheelbarrowController CurrentWheelbarrow => pouringMinigame != null ? pouringMinigame.Wheelbarrow : current;
    public WheelbarrowOccupantRole CurrentRole
    {
        get
        {
            if (pouringMinigame != null)
                return pouringMinigame.LeftPlayer == LocalClientId ? WheelbarrowOccupantRole.PourLeft : WheelbarrowOccupantRole.PourRight;
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
        networkObject = GetComponent<NetworkObject>();
        stamina = GetComponent<PlayerStaminaController>();
        health = GetComponent<PlayerHealth>();
        input.OnInteractCanceled += HandleInteractCanceled;
    }

    private void OnDestroy()
    {
        if (input != null) input.OnInteractCanceled -= HandleInteractCanceled;
        pouringCollisionWheelbarrow?.SetPlayerCollisionIgnored(transform, false);
        stamina?.ClearDrainSource(StaminaDrainSource.WheelbarrowDriving);
    }

    private void Update()
    {
        if (!IsLocalPlayer) return;
        pouringMinigame = WheelbarrowPouringMinigame.FindForPlayer(LocalClientId);
        current = WheelbarrowController.FindForPlayer(LocalClientId);
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
            stamina?.ClearDrainSource(StaminaDrainSource.WheelbarrowDriving);
            return;
        }

        if (CurrentRole == WheelbarrowOccupantRole.Driver)
        {
            Vector2 move = Vector2.ClampMagnitude(input.GetMoveVectorValue(), 1f);
            if (health != null && health.IsDowned)
            {
                if (IsNetworkActive && !current.IsServer) current.RequestExitServerRpc();
                else current.RequestExit(LocalClientId);
                return;
            }
            if (stamina != null && stamina.CurrentStamina <= 0f) move.y = 0f;
            inputTimer += Time.deltaTime;
            if (inputTimer >= inputSendInterval)
            {
                inputTimer = 0f;
                if (IsNetworkActive && !current.IsServer) current.SubmitDriveInputServerRpc(move.y, move.x);
                else current.SubmitDriveInput(move.y, move.x, LocalClientId);
            }
            stamina?.SetDrainSource(StaminaDrainSource.WheelbarrowDriving, current.GetEstimatedDrivingStaminaDrain(move.y));
        }
        else stamina?.ClearDrainSource(StaminaDrainSource.WheelbarrowDriving);
    }

    public void ApplyAnchoredMovement(float deltaTime)
    {
        WheelbarrowController activeWheelbarrow = CurrentWheelbarrow;
        if (!IsLocalPlayer || suppressAnchoredMovement || activeWheelbarrow == null || characterController == null || !characterController.enabled) return;
        Transform anchor = pouringMinigame != null ? pouringMinigame.GetAnchor(LocalClientId) : activeWheelbarrow.GetRoleAnchor(LocalClientId);
        if (anchor == null) return;
        Vector3 delta = anchor.position - transform.position;
        float speed = CurrentRole == WheelbarrowOccupantRole.Driver
            ? (activeWheelbarrow.Profile != null ? activeWheelbarrow.Profile.DriverFollowSpeed : 8f)
            : (activeWheelbarrow.Profile != null ? activeWheelbarrow.Profile.PassengerFollowSpeed : 12f);
        Vector3 movement = delta.magnitude > anchorSnapDistance ? delta : Vector3.ClampMagnitude(delta, speed * deltaTime);
        characterController.Move(movement);
        if (CurrentRole == WheelbarrowOccupantRole.Passenger)
            transform.rotation = Quaternion.Euler(0f, activeWheelbarrow.transform.eulerAngles.y, 0f);
    }

    public void BeginSafeExit(WheelbarrowController wheelbarrow, Vector3 position)
    {
        if (!IsLocalPlayer || wheelbarrow == null) return;
        safeExitWheelbarrow = wheelbarrow;
        suppressAnchoredMovement = true;
        bool wasEnabled = characterController != null && characterController.enabled;
        if (wasEnabled) characterController.enabled = false;
        transform.position = position;
        if (wasEnabled) characterController.enabled = true;
        GetComponent<StarterAssets.FirstPersonController>()?.ResetMovementAfterForcedPlacement();
    }

    public void CompleteSafeExit(WheelbarrowController wheelbarrow)
    {
        if (safeExitWheelbarrow != wheelbarrow) return;
        safeExitWheelbarrow = null;
        suppressAnchoredMovement = false;
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
