using StarterAssets;
using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerAnimationController : NetworkBehaviour
{
    private static readonly int SpeedNormalizedHash = Animator.StringToHash("SpeedNormalized");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int VerticalVelocityHash = Animator.StringToHash("VerticalVelocity");
    private static readonly int TurnDirectionHash = Animator.StringToHash("TurnDirection");

    private static readonly int LocomotionStateHash = Animator.StringToHash("Locomotion Blend Tree");
    private static readonly int JumpStateHash = Animator.StringToHash("Jump");
    private static readonly int FallStateHash = Animator.StringToHash("Fall");
    private static readonly int ActionStateHash = Animator.StringToHash("Action");
    private static readonly int CarryStateHash = Animator.StringToHash("Carry");
    private static readonly int DownedStateHash = Animator.StringToHash("Downed");
    private static readonly int HitReactionStateHash = Animator.StringToHash("Hit Reaction");
    private static readonly int TurnLeftStateHash = Animator.StringToHash("Turn Left");
    private static readonly int TurnRightStateHash = Animator.StringToHash("Turn Right");
    private static readonly int BackwardMoveStateHash = Animator.StringToHash("Backward Move");
    private static readonly int LandWalkStateHash = Animator.StringToHash("Land Walk");
    private static readonly int LandRunStateHash = Animator.StringToHash("Land Run");

    [SerializeField] private Animator animator;
    [SerializeField] private FirstPersonController firstPersonController;
    [SerializeField] private PlayerInputNew playerInput;
    [SerializeField] private PlayerActionController playerActionController;
    [SerializeField] private PlayerInteractionNew playerInteraction;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private float moveSpeedReference = 4f;
    [SerializeField] private float sprintSpeedReference = 6f;
    [SerializeField] private float speedDampTime = 0.12f;
    [SerializeField] private float idleSpeedThreshold = 0.05f;
    [SerializeField] private float airVelocityThreshold = 0.15f;
    [SerializeField] private float turnInPlaceAngularSpeedThreshold = 35f;
    [SerializeField] private float turnInPlaceMoveSpeedThreshold = 0.2f;
    [SerializeField] private float actionAnimationDuration = 0.75f;
    [SerializeField] private float hitReactionDuration = 0.35f;
    [SerializeField] private float jumpStartAnimationDuration = 0.18f;
    [SerializeField] private float landingAnimationDuration = 0.28f;
    [SerializeField] private float minimumAirTimeForLandingAnimation = 0.12f;
    [SerializeField] private float landingRunSpeedThreshold = 3.5f;
    [SerializeField] private float backwardMoveSpeedThreshold = 0.2f;
    [SerializeField] private float stateCrossFadeDuration = 0.08f;

    private Vector3 lastPosition;
    private float lastYaw;
    private float speedNormalized;
    private float speedVelocity;
    private float actionAnimationTimer;
    private float hitReactionTimer;
    private float jumpStartAnimationTimer;
    private float landingAnimationTimer;
    private float landingSpeed;
    private float airborneTimer;
    private float previousHealth;
    private int currentStateHash;
    private bool hasLastPosition;
    private bool wasGrounded = true;
    private bool IsNetworkAnimationActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    public void SetAnimator(Animator targetAnimator)
    {
        animator = targetAnimator;
    }

    private void Awake()
    {
        if (firstPersonController == null)
        {
            firstPersonController = GetComponent<FirstPersonController>();
        }

        if (playerInput == null)
        {
            playerInput = GetComponent<PlayerInputNew>();
        }

        if (playerActionController == null)
        {
            playerActionController = GetComponent<PlayerActionController>();
        }

        if (playerInteraction == null)
        {
            playerInteraction = GetComponent<PlayerInteractionNew>();
        }

        if (playerHealth == null)
        {
            playerHealth = GetComponent<PlayerHealth>();
        }

        previousHealth = playerHealth != null ? playerHealth.CurrentHealth : 0f;
    }

    private void OnEnable()
    {
        lastPosition = transform.position;
        lastYaw = transform.eulerAngles.y;
        hasLastPosition = true;
        previousHealth = playerHealth != null ? playerHealth.CurrentHealth : previousHealth;
        SubscribeEvents();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
    }

    private void Update()
    {
        if (animator == null)
        {
            CacheAnimatorFromBodyVisual();
            if (animator == null)
            {
                return;
            }
        }

        if (!IsAnimatorReady())
        {
            lastPosition = transform.position;
            lastYaw = transform.eulerAngles.y;
            return;
        }

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        if (!hasLastPosition)
        {
            lastPosition = currentPosition;
            hasLastPosition = true;
        }

        Vector3 delta = currentPosition - lastPosition;
        lastPosition = currentPosition;
        float currentYaw = transform.eulerAngles.y;
        float yawDelta = Mathf.DeltaAngle(lastYaw, currentYaw);
        lastYaw = currentYaw;

        float horizontalSpeed = new Vector2(delta.x, delta.z).magnitude / deltaTime;
        float verticalVelocity = GetVerticalVelocity(delta.y / deltaTime);
        float angularSpeed = yawDelta / deltaTime;
        float signedForwardSpeed = Vector3.Dot(new Vector3(delta.x, 0f, delta.z) / deltaTime, transform.forward);
        float targetSpeedNormalized = GetSpeedNormalized(horizontalSpeed);

        if (playerHealth != null && playerHealth.IsDowned)
        {
            targetSpeedNormalized = 0f;
        }

        speedNormalized = Mathf.SmoothDamp(speedNormalized, targetSpeedNormalized, ref speedVelocity, speedDampTime);

        animator.SetFloat(SpeedNormalizedHash, speedNormalized);
        bool isGrounded = IsGrounded(verticalVelocity);
        animator.SetBool(IsGroundedHash, isGrounded);
        animator.SetFloat(VerticalVelocityHash, verticalVelocity);
        animator.SetFloat(TurnDirectionHash, Mathf.Sign(angularSpeed));
        UpdateLandingState(isGrounded, horizontalSpeed, deltaTime);

        if (actionAnimationTimer > 0f)
        {
            actionAnimationTimer -= deltaTime;
        }

        if (hitReactionTimer > 0f)
        {
            hitReactionTimer -= deltaTime;
        }

        if (jumpStartAnimationTimer > 0f)
        {
            jumpStartAnimationTimer -= deltaTime;
        }

        if (landingAnimationTimer > 0f)
        {
            landingAnimationTimer -= deltaTime;
        }

        UpdateAnimationState(horizontalSpeed, signedForwardSpeed, verticalVelocity, angularSpeed, isGrounded);
    }

    private void CacheAnimatorFromBodyVisual()
    {
        if (!TryGetComponent(out PlayerNetworkSetup playerNetworkSetup) || playerNetworkSetup.PlayerBodyVisual == null)
        {
            return;
        }

        animator = playerNetworkSetup.PlayerBodyVisual.GetComponentInChildren<Animator>(true);
    }

    private float GetSpeedNormalized(float horizontalSpeed)
    {
        if (horizontalSpeed <= idleSpeedThreshold)
        {
            return 0f;
        }

        if (moveSpeedReference <= idleSpeedThreshold)
        {
            return horizontalSpeed > idleSpeedThreshold ? 1f : 0f;
        }

        if (horizontalSpeed <= moveSpeedReference)
        {
            return Mathf.Lerp(0f, 0.5f, horizontalSpeed / moveSpeedReference);
        }

        float sprintRange = Mathf.Max(0.01f, sprintSpeedReference - moveSpeedReference);
        return Mathf.Lerp(0.5f, 1f, Mathf.Clamp01((horizontalSpeed - moveSpeedReference) / sprintRange));
    }

    private void UpdateAnimationState(float horizontalSpeed, float signedForwardSpeed, float verticalVelocity, float angularSpeed, bool isGrounded)
    {
        if (playerHealth != null && playerHealth.IsDowned)
        {
            CrossFadeToState(DownedStateHash);
            return;
        }

        if (hitReactionTimer > 0f)
        {
            CrossFadeToState(HitReactionStateHash);
            return;
        }

        if (actionAnimationTimer > 0f)
        {
            CrossFadeToState(ActionStateHash);
            return;
        }

        if (!isGrounded)
        {
            CrossFadeToState(verticalVelocity >= 0f || jumpStartAnimationTimer > 0f ? JumpStateHash : FallStateHash);
            return;
        }

        if (landingAnimationTimer > 0f)
        {
            CrossFadeToState(landingSpeed >= landingRunSpeedThreshold ? LandRunStateHash : LandWalkStateHash);
            return;
        }

        if (IsCarryingObject())
        {
            CrossFadeToState(CarryStateHash);
            return;
        }

        if (horizontalSpeed > backwardMoveSpeedThreshold && signedForwardSpeed < -backwardMoveSpeedThreshold)
        {
            CrossFadeToState(BackwardMoveStateHash);
            return;
        }

        if (horizontalSpeed <= turnInPlaceMoveSpeedThreshold && Mathf.Abs(angularSpeed) >= turnInPlaceAngularSpeedThreshold)
        {
            CrossFadeToState(angularSpeed < 0f ? TurnLeftStateHash : TurnRightStateHash);
            return;
        }

        CrossFadeToState(LocomotionStateHash);
    }

    private void CrossFadeToState(int stateHash)
    {
        if (!IsAnimatorReady() || currentStateHash == stateHash)
        {
            return;
        }

        currentStateHash = stateHash;
        animator.CrossFadeInFixedTime(stateHash, stateCrossFadeDuration);
    }

    private bool IsAnimatorReady()
    {
        return animator != null && animator.isActiveAndEnabled && animator.gameObject.activeInHierarchy;
    }

    private float GetVerticalVelocity(float transformDeltaVelocity)
    {
        if (firstPersonController == null)
        {
            return transformDeltaVelocity;
        }

        float controllerVelocity = firstPersonController.VerticalVelocity;
        return Mathf.Abs(controllerVelocity) > Mathf.Abs(transformDeltaVelocity) ? controllerVelocity : transformDeltaVelocity;
    }

    private bool IsGrounded(float verticalVelocity)
    {
        if (firstPersonController != null && !firstPersonController.Grounded)
        {
            return false;
        }

        return Mathf.Abs(verticalVelocity) <= airVelocityThreshold;
    }

    private void UpdateLandingState(bool isGrounded, float horizontalSpeed, float deltaTime)
    {
        if (!isGrounded)
        {
            airborneTimer += deltaTime;
            wasGrounded = false;
            return;
        }

        if (!wasGrounded && airborneTimer >= minimumAirTimeForLandingAnimation)
        {
            landingSpeed = horizontalSpeed;
            landingAnimationTimer = landingAnimationDuration;
        }

        airborneTimer = 0f;
        wasGrounded = true;
    }

    private bool IsCarryingObject()
    {
        return playerInteraction != null && playerInteraction.IsHoldingObject;
    }

    private void SubscribeEvents()
    {
        if (playerActionController != null)
        {
            playerActionController.OnActionPerformed += Gameplay_OnActionAnimation;
            playerActionController.OnActionAltPerformed += Gameplay_OnActionAnimation;
        }

        if (playerInteraction != null)
        {
            playerInteraction.OnInteractionPerformed += Gameplay_OnActionAnimation;
            playerInteraction.OnHeldObjectChanged += PlayerInteraction_OnHeldObjectChanged;
        }

        if (firstPersonController != null)
        {
            firstPersonController.OnJumpStarted += FirstPersonController_OnJumpStarted;
        }

        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged += PlayerHealth_OnHealthChanged;
            playerHealth.OnDownedStateChanged += PlayerHealth_OnDownedStateChanged;
        }
    }

    private void UnsubscribeEvents()
    {
        if (playerActionController != null)
        {
            playerActionController.OnActionPerformed -= Gameplay_OnActionAnimation;
            playerActionController.OnActionAltPerformed -= Gameplay_OnActionAnimation;
        }

        if (playerInteraction != null)
        {
            playerInteraction.OnInteractionPerformed -= Gameplay_OnActionAnimation;
            playerInteraction.OnHeldObjectChanged -= PlayerInteraction_OnHeldObjectChanged;
        }

        if (firstPersonController != null)
        {
            firstPersonController.OnJumpStarted -= FirstPersonController_OnJumpStarted;
        }

        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged -= PlayerHealth_OnHealthChanged;
            playerHealth.OnDownedStateChanged -= PlayerHealth_OnDownedStateChanged;
        }
    }

    private void Gameplay_OnActionAnimation(object sender, EventArgs e)
    {
        if (playerHealth != null && playerHealth.IsDowned)
        {
            return;
        }

        TriggerActionAnimation(true);
    }

    private void TriggerActionAnimation(bool broadcastToRemoteClients)
    {
        actionAnimationTimer = actionAnimationDuration;
        currentStateHash = 0;

        if (!broadcastToRemoteClients || !IsNetworkAnimationActive || !IsOwner)
        {
            return;
        }

        if (IsServer)
        {
            PlayActionAnimationClientRpc();
        }
        else
        {
            RequestPlayActionAnimationServerRpc();
        }
    }

    [ServerRpc]
    private void RequestPlayActionAnimationServerRpc()
    {
        PlayActionAnimationClientRpc();
    }

    [ClientRpc]
    private void PlayActionAnimationClientRpc()
    {
        if (IsOwner)
        {
            return;
        }

        TriggerActionAnimation(false);
    }

    private void FirstPersonController_OnJumpStarted(object sender, EventArgs e)
    {
        if (playerHealth != null && playerHealth.IsDowned)
        {
            return;
        }

        jumpStartAnimationTimer = jumpStartAnimationDuration;
        landingAnimationTimer = 0f;
    }

    private void PlayerInteraction_OnHeldObjectChanged(object sender, EventArgs e)
    {
        currentStateHash = 0;
    }

    private void PlayerHealth_OnDownedStateChanged(object sender, EventArgs e)
    {
        actionAnimationTimer = 0f;
        jumpStartAnimationTimer = 0f;
        landingAnimationTimer = 0f;
        currentStateHash = 0;
    }

    private void PlayerHealth_OnHealthChanged(object sender, EventArgs e)
    {
        if (playerHealth == null)
        {
            return;
        }

        float currentHealth = playerHealth.CurrentHealth;
        if (currentHealth < previousHealth)
        {
            hitReactionTimer = hitReactionDuration;
        }

        previousHealth = currentHealth;
    }
}
