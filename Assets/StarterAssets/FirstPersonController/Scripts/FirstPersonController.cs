using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonController : MonoBehaviour
	{
		public event EventHandler OnJumpStarted;
		public event EventHandler<LandedEventArgs> OnLanded;
		public event EventHandler<FootstepEventArgs> OnFootstep;
		public event EventHandler<SharedCarryExhaustionWarningChangedEventArgs> OnSharedCarryExhaustionWarningChanged;

		public class LandedEventArgs : EventArgs
		{
			public float ImpactSpeed;
		}

		public class FootstepEventArgs : EventArgs
		{
			public Vector3 Position;
			public Collider GroundCollider;
			public bool IsSprinting;
		}

		public class SharedCarryExhaustionWarningChangedEventArgs : EventArgs
		{
			public bool IsWarningActive;
		}

		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 4.0f;
		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 6.0f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;
		[Header("Movement Feel")]
		[SerializeField] private float groundAcceleration = 22f;
		[SerializeField] private float groundDeceleration = 28f;
		[SerializeField] private float reverseAcceleration = 34f;
		[SerializeField] private float airAcceleration = 4f;
		[SerializeField, Range(0.1f, 1f)] private float strafeSpeedMultiplier = 0.9f;
		[SerializeField, Range(0.1f, 1f)] private float backwardSpeedMultiplier = 0.8f;
		[SerializeField] private float walkStepDistance = 0.8f;
		[SerializeField] private float sprintStepDistance = 1.05f;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.2f;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.1f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.5f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 90.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -90.0f;
		[Header("Look Feel")]
		[SerializeField] private float maximumCameraBodyYawOffset = 6f;
		[SerializeField] private float bodyYawSmoothTime = 0.09f;
		[SerializeField] private float bodyMaximumYawSpeed = 720f;

		// cinemachine
		private float _cinemachineTargetPitch;
		private float _aimYaw;
		private float _bodyYawVelocity;
		private float _cameraBodyYawOffset;
		private bool _lookRotationInitialized;

		// player
		private Vector3 _horizontalVelocity;
		private float _mostNegativeAirVelocity;
		private float _footstepDistanceAccumulator;
		private bool _isSprinting = false;
		private bool _isJumpPerformed = false;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;
		private float _holdedItemMovementSpeedPenaltyMultiplier = 0f;
		private float _inventoryItemMovementSpeedPenaltyMultiplier = 0f;
		private float _currentStamina;
		private float _staminaRegenerationTimeoutCounter = 0f;
		private bool _countStaminaTimeout = false;
		private bool _canRegenerateStamina = true;
		[Header("Sprint")]
		[Tooltip("Sprint Stamina in seconds")]
		public float MaxStamina;
		[Tooltip("Stamina regeneration timeout")]
		public float StaminaRegenerationTimeout;
		#region
		[Tooltip("Temp value - you can check current base movement speed with penalties")]
        #endregion
        [SerializeField] private float currentMovementSpeed = 0f;
        [Header("Shared Carry")]
        [SerializeField] private float sharedCarryAttachCorrectionSpeed = 12f;
        [SerializeField] private float sharedCarryAttachSnapDistance = 1.5f;
        [SerializeField] private float sharedCarryInputSendInterval = 0.05f;
		[SerializeField] private float sharedCarryInputChangeThreshold = 0.01f;
		[SerializeField] private float sharedCarryExhaustionWarningDuration = 3f;
		private float _sharedCarryInputSendTimer;
		private Vector3 _lastSentSharedCarryInput;
		private Vector3 _lastSentSharedCarryLateralInput;
		private float _lastSentSharedCarryYawInput;
		private float _sharedCarryExhaustionWarningElapsed;
		private bool _isSharedCarryExhaustionWarningActive;
		private bool _sharedCarryExhaustionRequested;

		// timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;

	
#if ENABLE_INPUT_SYSTEM
		private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;
		private PlayerInputNew _playerInputNew;
		private PlayerInteractionNew _playerInteractionNew;
		private PlayerInventory _playerInventory;
		private PlayerHealth _playerHealth;
		private DownedPlayerCarryable _downedPlayerCarryable;
		private PlayerExternalImpulseController _externalImpulseController;
		private PlayerActionController _playerActionController;
		private PlayerStaminaController _staminaController;
		
		private const float _threshold = 0.01f;

		public float VerticalVelocity => _verticalVelocity;
		public Vector3 HorizontalVelocity => _horizontalVelocity;
		public float HorizontalSpeed => _horizontalVelocity.magnitude;
		public bool IsSprinting => _isSprinting
			&& _currentStamina > 0f
			&& !IsDowned()
			&& (_playerActionController == null || !_playerActionController.IsActionInProgress);
		public float CurrentStamina => _staminaController != null ? _staminaController.CurrentStamina : _currentStamina;
		public bool IsSharedCarryExhaustionWarningActive => !IsDowned()
			&& (_isSharedCarryExhaustionWarningActive
				|| (_staminaController != null && _staminaController.CurrentExhaustionReason == StaminaExhaustionReason.Water));
		public float AimYaw => _aimYaw;
		public float BodyYawVelocity => _bodyYawVelocity;
		public float CameraBodyYawOffset => _cameraBodyYawOffset;
		public float CameraBodyYawOffsetNormalized
		{
			get
			{
				float intensity = CameraMotionSettings.Instance != null
					? CameraMotionSettings.Instance.RotationMotionIntensity
					: 1f;
				float maximumOffset = Mathf.Max(0f, maximumCameraBodyYawOffset) * intensity;
				return maximumOffset > 0.0001f
					? Mathf.Clamp(_cameraBodyYawOffset / maximumOffset, -1f, 1f)
					: 0f;
			}
		}

		private bool IsCurrentDeviceMouse
		{
			get
			{
				#if ENABLE_INPUT_SYSTEM
				return _playerInput.currentControlScheme == "KeyboardMouse";
				#else
				return false;
				#endif
			}
		}

		private void Awake()
		{
			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
            _playerInputNew = GetComponent<PlayerInputNew>();
			_playerInputNew.OnSprint += PlayerInputNew_OnSprint;
			_playerInputNew.OnJump += PlayerInputNew_OnJump;
			_playerHealth = GetComponent<PlayerHealth>();
			if (_playerHealth != null)
			{
				_playerHealth.OnDownedStateChanged += PlayerHealth_OnDownedStateChanged;
			}
			_downedPlayerCarryable = GetComponent<DownedPlayerCarryable>();
			_staminaController = GetComponent<PlayerStaminaController>();
			_staminaController?.Configure(MaxStamina, StaminaRegenerationTimeout);
			_currentStamina = MaxStamina;
        }

		private void OnEnable()
		{
			ResetLookRotationState();
		}

		private void OnDisable()
		{
			_bodyYawVelocity = 0f;
		}

		private void OnDestroy()
		{
			if (_playerInputNew != null)
			{
				_playerInputNew.OnSprint -= PlayerInputNew_OnSprint;
				_playerInputNew.OnJump -= PlayerInputNew_OnJump;
			}

			if (_playerHealth != null)
			{
				_playerHealth.OnDownedStateChanged -= PlayerHealth_OnDownedStateChanged;
			}

			if (_playerInteractionNew != null)
			{
				_playerInteractionNew.OnHeldObjectForcedRelease -= PlayerInteraction_OnHeldObjectForcedRelease;
			}
		}

		private void PlayerHealth_OnDownedStateChanged(object sender, EventArgs e)
		{
			if (_playerHealth == null)
			{
				return;
			}

			_isSprinting = false;
			_isJumpPerformed = false;
			_staminaRegenerationTimeoutCounter = 0f;
			_countStaminaTimeout = false;
			_canRegenerateStamina = false;
			CancelSharedCarryExhaustionWarning();
			ResetLookRotationState();

			if (!_playerHealth.IsDowned)
			{
				if (_staminaController != null)
				{
					_staminaController.RestoreFullStamina();
				}
				else
				{
					_currentStamina = MaxStamina;
				}
			}
		}

        private void PlayerInputNew_OnJump(object sender, EventArgs e)
        {
			if (IsDowned())
			{
				_isJumpPerformed = false;
				return;
			}

            _isJumpPerformed = true;
        }

        private void PlayerInputNew_OnSprint(object sender, PlayerInputNew.OnSprintArgs e)
        {
			if (IsDowned())
			{
				_isSprinting = false;
				return;
			}

            _isSprinting = e.IsSprinting;
			if (_isSprinting)
			{
				_canRegenerateStamina = false;
				_countStaminaTimeout = false;
			} else if (_holdedItemMovementSpeedPenaltyMultiplier <= 0) 
			{
				_countStaminaTimeout = true;
			}
			
        }

        private void Start()
		{

            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
			_playerInteractionNew = GetComponent<PlayerInteractionNew>();
			_playerInventory = GetComponent<PlayerInventory>();
			_externalImpulseController = GetComponent<PlayerExternalImpulseController>();
			_playerActionController = GetComponent<PlayerActionController>();

#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif
            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;
			_playerInteractionNew.UpdateHoldedItemMovementSpeedPenalty += PlayerInteraction_OnHoldedItemMovementSpeedPenaltyUpdate;
			_playerInteractionNew.OnHeldObjectForcedRelease += PlayerInteraction_OnHeldObjectForcedRelease;
			_playerInventory.MovementSpeedPenaltyUpdated += PlayerInventory_OnInventoryItemMovementSpeedPenaltyUpdate;
		}

		private void PlayerInteraction_OnHeldObjectForcedRelease(object sender, EventArgs e)
		{
			_isSprinting = false;
			_isJumpPerformed = false;
			CancelSharedCarryExhaustionWarning();
			_staminaRegenerationTimeoutCounter = 0f;
			_canRegenerateStamina = false;
			_countStaminaTimeout = !IsDowned();
		}

        private void PlayerInventory_OnInventoryItemMovementSpeedPenaltyUpdate(object sender, PlayerInventory.MovementSpeedPenaltyUpdatedEventArgs e)
        {
			_inventoryItemMovementSpeedPenaltyMultiplier = e.currentMovementSpeedPenaltyMultiplier;
			currentMovementSpeed = MoveSpeed * (1 - _inventoryItemMovementSpeedPenaltyMultiplier) * (1 - _holdedItemMovementSpeedPenaltyMultiplier);
        }

        private void PlayerInteraction_OnHoldedItemMovementSpeedPenaltyUpdate(object sender, PlayerInteractionNew.UpdateHoldedItemMovementSpeedPenaltyEventArgs e)
        {
			_holdedItemMovementSpeedPenaltyMultiplier = e.currentMovementSpeedPenaltyMultiplier;
            currentMovementSpeed = MoveSpeed * (1 - _inventoryItemMovementSpeedPenaltyMultiplier) * (1 - _holdedItemMovementSpeedPenaltyMultiplier);
			if (_playerInteractionNew != null && _playerInteractionNew.IsSharedCarryMovementActive)
			{
				if (_playerInteractionNew.IsSharedCarryUnderstaffed)
				{
					_canRegenerateStamina = false;
					_countStaminaTimeout = false;
				}
				else if (!_isSprinting)
				{
					_countStaminaTimeout = true;
				}

				return;
			}

			if(_holdedItemMovementSpeedPenaltyMultiplier > 0 )
			{
				_canRegenerateStamina = false;
				_countStaminaTimeout = false;
			}
			else if(!_isSprinting)
			{
				_countStaminaTimeout = true;
			}

        }

        private void Update()
		{
			JumpAndGravity();
			GroundedCheck();
			Move();
			ApplyExternalImpulseMovement();
			HandleStaminaRegeneration();
			HandleCarryingStaminaUsage();
		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		private void GroundedCheck()
		{
			bool wasGrounded = Grounded;
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);

			if (!Grounded)
			{
				_mostNegativeAirVelocity = Mathf.Min(_mostNegativeAirVelocity, _verticalVelocity);
			}
			else if (!wasGrounded)
			{
				float impactSpeed = Mathf.Abs(Mathf.Min(0f, _mostNegativeAirVelocity));
				_mostNegativeAirVelocity = 0f;
				OnLanded?.Invoke(this, new LandedEventArgs { ImpactSpeed = impactSpeed });
			}
		}

		private void CameraRotation()
		{
            EnsureLookRotationInitialized();
            Vector2 lookDelta = _playerInputNew.GetLookDeltaValue();
            bool hasLookInput = lookDelta.sqrMagnitude >= _threshold;
            if (hasLookInput)
            {
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetPitch += lookDelta.y * RotationSpeed * deltaTimeMultiplier;
				_aimYaw += lookDelta.x * RotationSpeed * deltaTimeMultiplier;
            }

            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);
			float motionIntensity = CameraMotionSettings.Instance != null
				? CameraMotionSettings.Instance.RotationMotionIntensity
				: 1f;
			float maximumYawOffset = Mathf.Max(0f, maximumCameraBodyYawOffset) * motionIntensity;

			float bodyYaw;
			if (maximumYawOffset <= 0.0001f || bodyYawSmoothTime <= 0.0001f)
			{
				bodyYaw = _aimYaw;
				_bodyYawVelocity = 0f;
			}
			else
			{
				bodyYaw = Mathf.SmoothDampAngle(
					transform.eulerAngles.y,
					_aimYaw,
					ref _bodyYawVelocity,
					bodyYawSmoothTime,
					Mathf.Max(0f, bodyMaximumYawSpeed),
					Time.deltaTime);

				float yawOffset = Mathf.DeltaAngle(bodyYaw, _aimYaw);
				if (Mathf.Abs(yawOffset) > maximumYawOffset)
				{
					yawOffset = Mathf.Clamp(yawOffset, -maximumYawOffset, maximumYawOffset);
					bodyYaw = _aimYaw - yawOffset;
				}
			}

			transform.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
			_cameraBodyYawOffset = Mathf.DeltaAngle(bodyYaw, _aimYaw);
			CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(
				_cinemachineTargetPitch,
				_cameraBodyYawOffset,
				0f);
		}

		public void ResetLookRotationState()
		{
			_aimYaw = transform.eulerAngles.y;
			_bodyYawVelocity = 0f;
			_cameraBodyYawOffset = 0f;
			_lookRotationInitialized = true;
			if (CinemachineCameraTarget != null)
			{
				CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0f, 0f);
			}
		}

		private void EnsureLookRotationInitialized()
		{
			if (!_lookRotationInitialized)
			{
				ResetLookRotationState();
			}
		}

		private void Move()
		{
			if (_controller == null || !_controller.enabled)
			{
				_isSprinting = false;
				_isJumpPerformed = false;
				_horizontalVelocity = Vector3.zero;
				return;
			}

			if (IsBeingCarried())
			{
				_isSprinting = false;
				_isJumpPerformed = false;
				_horizontalVelocity = Vector3.zero;
				return;
			}

			if (IsDowned())
			{
				MoveWhileDowned();
				return;
			}

			if (_playerInteractionNew != null && _playerInteractionNew.IsSharedCarryMovementActive)
			{
				MoveDuringSharedCarry();
				return;
			}

			Vector2 moveInput = Vector2.ClampMagnitude(_playerInputNew.GetMoveVectorValue(), 1f);
			float targetSpeed = IsSprinting ? SprintSpeed : MoveSpeed;
			targetSpeed *= 1f - _holdedItemMovementSpeedPenaltyMultiplier;
			targetSpeed *= 1f - _inventoryItemMovementSpeedPenaltyMultiplier;
			if (_externalImpulseController != null)
			{
				targetSpeed *= _externalImpulseController.MovementControlMultiplier;
			}
			if (_playerActionController != null)
			{
				targetSpeed *= _playerActionController.ActionMovementMultiplier;
			}

			Vector3 localDesiredVelocity = new Vector3(
				moveInput.x * strafeSpeedMultiplier,
				0f,
				moveInput.y * (moveInput.y < 0f ? backwardSpeedMultiplier : 1f));
			localDesiredVelocity = Vector3.ClampMagnitude(localDesiredVelocity, 1f) * targetSpeed;
			Vector3 desiredVelocity = transform.TransformDirection(localDesiredVelocity);
			desiredVelocity.y = 0f;

			float acceleration = GetHorizontalAcceleration(desiredVelocity);
			_horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, desiredVelocity, acceleration * Time.deltaTime);

			Vector3 positionBeforeMove = transform.position;
			_controller.Move((_horizontalVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime);
			Vector3 actualHorizontalDelta = transform.position - positionBeforeMove;
			actualHorizontalDelta.y = 0f;
			_horizontalVelocity = actualHorizontalDelta / Mathf.Max(Time.deltaTime, 0.0001f);
			UpdateFootsteps(positionBeforeMove, transform.position);

			if (moveInput != Vector2.zero && IsSprinting)
			{
				if (_staminaController == null)
				{
					_currentStamina = Mathf.Max(0f, _currentStamina - Time.deltaTime);
				}
			}
		}

		private float GetHorizontalAcceleration(Vector3 desiredVelocity)
		{
			if (!Grounded)
			{
				return Mathf.Max(0f, airAcceleration);
			}

			if (desiredVelocity.sqrMagnitude <= 0.0001f)
			{
				return Mathf.Max(0f, groundDeceleration);
			}

			if (_horizontalVelocity.sqrMagnitude > 0.0001f
				&& Vector3.Dot(_horizontalVelocity.normalized, desiredVelocity.normalized) < 0f)
			{
				return Mathf.Max(0f, reverseAcceleration);
			}

			return Mathf.Max(0f, groundAcceleration);
		}

		private void UpdateFootsteps(Vector3 previousPosition, Vector3 currentPosition)
		{
			if (!Grounded || HorizontalSpeed < 0.2f)
			{
				_footstepDistanceAccumulator = 0f;
				return;
			}

			Vector3 horizontalDelta = currentPosition - previousPosition;
			horizontalDelta.y = 0f;
			_footstepDistanceAccumulator += horizontalDelta.magnitude;
			float stepDistance = Mathf.Max(0.1f, IsSprinting ? sprintStepDistance : walkStepDistance);
			if (_footstepDistanceAccumulator < stepDistance)
			{
				return;
			}

			_footstepDistanceAccumulator %= stepDistance;
			Collider groundCollider = null;
			Vector3 footstepPosition = transform.position;
			if (Physics.Raycast(transform.position + Vector3.up * 0.25f, Vector3.down, out RaycastHit hit, 2f, GroundLayers, QueryTriggerInteraction.Ignore))
			{
				groundCollider = hit.collider;
				footstepPosition = hit.point;
			}

			OnFootstep?.Invoke(this, new FootstepEventArgs
			{
				Position = footstepPosition,
				GroundCollider = groundCollider,
				IsSprinting = IsSprinting
			});
		}

		private void MoveWhileDowned()
		{
			_isSprinting = false;
			_isJumpPerformed = false;
			_horizontalVelocity = Vector3.zero;
			_controller.Move(new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
		}

		private void MoveDuringSharedCarry()
		{
            Vector2 moveInput = _playerInputNew.GetMoveVectorValue();
			Vector3 worldTranslationInput = GetSharedCarryWorldTranslationInput(moveInput.y);
			Vector3 worldLateralInput = GetSharedCarryWorldLateralInput(moveInput.x);
			_playerInteractionNew.PredictSharedCarryOrbit(worldLateralInput, Time.deltaTime);
			SendSharedCarryInputIfNeeded(worldTranslationInput, worldLateralInput, moveInput.x);

			if (_playerInteractionNew.IsPhysicalPointGripActive)
			{
				_controller.Move(_playerInteractionNew.GetSharedCarryTetherMovement(
					Time.deltaTime,
					Mathf.Max(0f, currentMovementSpeed),
					Grounded));
			}
			else
			{
				Vector3 attachCorrection = _playerInteractionNew.GetSharedCarryAnchorCorrection();
				if (attachCorrection.magnitude > sharedCarryAttachSnapDistance)
				{
					_controller.Move(attachCorrection);
				}
				else
				{
					Vector3 correctionDelta = Vector3.MoveTowards(Vector3.zero, attachCorrection, sharedCarryAttachCorrectionSpeed * Time.deltaTime);
					_controller.Move(correctionDelta);
				}
			}

			_controller.Move(new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
			_horizontalVelocity = Vector3.zero;
		}

		private void ApplyExternalImpulseMovement()
		{
			if (_externalImpulseController == null
				|| !_externalImpulseController.IsImpulseActive
				|| _controller == null
				|| !_controller.enabled
				|| IsBeingCarried())
			{
				return;
			}

			Vector3 impulseVelocity = _externalImpulseController.TickImpulse(Time.deltaTime, Grounded);
			CollisionFlags collisionFlags = _controller.Move(impulseVelocity * Time.deltaTime);
			_externalImpulseController.ReportCollision(collisionFlags);
		}

		private Vector3 GetSharedCarryWorldTranslationInput(float forwardInput)
		{
			if (Mathf.Approximately(forwardInput, 0f))
			{
				return Vector3.zero;
			}

			Vector3 worldTranslationInput = transform.forward * forwardInput;
			worldTranslationInput.y = 0f;
			return Vector3.ClampMagnitude(worldTranslationInput, 1f);
		}

		private Vector3 GetSharedCarryWorldLateralInput(float lateralInput)
		{
			if (Mathf.Approximately(lateralInput, 0f))
			{
				return Vector3.zero;
			}

			Vector3 worldLateralInput = transform.right * lateralInput;
			worldLateralInput.y = 0f;
			return Vector3.ClampMagnitude(worldLateralInput, 1f);
		}

		private void SendSharedCarryInputIfNeeded(Vector3 worldTranslationInput, Vector3 worldLateralInput, float yawInput)
		{
			_sharedCarryInputSendTimer += Time.deltaTime;
			yawInput = Mathf.Clamp(yawInput, -1f, 1f);
			bool inputChanged = Vector3.Distance(_lastSentSharedCarryInput, worldTranslationInput) >= sharedCarryInputChangeThreshold
				|| Vector3.Distance(_lastSentSharedCarryLateralInput, worldLateralInput) >= sharedCarryInputChangeThreshold
				|| Mathf.Abs(_lastSentSharedCarryYawInput - yawInput) >= sharedCarryInputChangeThreshold;
			if (_sharedCarryInputSendTimer < sharedCarryInputSendInterval && !inputChanged)
			{
				return;
			}

			_sharedCarryInputSendTimer = 0f;
			_lastSentSharedCarryInput = worldTranslationInput;
			_lastSentSharedCarryLateralInput = worldLateralInput;
			_lastSentSharedCarryYawInput = yawInput;
			_playerInteractionNew.SubmitSharedCarryInput(worldTranslationInput, worldLateralInput, yawInput);
		}


		private void JumpAndGravity()
		{
			if (IsBeingCarried())
			{
				_isJumpPerformed = false;
				_verticalVelocity = 0f;
				return;
			}

			// External impulses own vertical motion while active. Applying the regular
			// character gravity as well would make player knockback decay much faster
			// than the equivalent server-simulated NPC impulse.
			if (_externalImpulseController != null && _externalImpulseController.IsImpulseActive)
			{
				_isJumpPerformed = false;
				_verticalVelocity = 0f;
				return;
			}

			bool canJump = !IsDowned() && (_playerInteractionNew == null || !_playerInteractionNew.IsSharedCarryMovementActive);
			if (!canJump)
			{
				_isJumpPerformed = false;
			}

			if (Grounded)
			{
				// reset the fall timeout timer
				_fallTimeoutDelta = FallTimeout;

				// stop our velocity dropping infinitely when grounded
				if (_verticalVelocity < 0.0f)
				{
					_verticalVelocity = -2f;
				}

				// Jump
				if (canJump && _isJumpPerformed && _jumpTimeoutDelta <= 0.0f)
				{
					// the square root of H * -2 * G = how much velocity needed to reach desired height
					_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
					OnJumpStarted?.Invoke(this, EventArgs.Empty);
				}

				// jump timeout
				if (_jumpTimeoutDelta >= 0.0f)
				{
					_jumpTimeoutDelta -= Time.deltaTime;
				}
			}
			else
			{
				// reset the jump timeout timer
				_jumpTimeoutDelta = JumpTimeout;

				// fall timeout
				if (_fallTimeoutDelta >= 0.0f)
				{
					_fallTimeoutDelta -= Time.deltaTime;
				}

				// if we are not grounded, do not jump
				_isJumpPerformed = false;
			}

			// apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += Gravity * Time.deltaTime;
			}
		}

        #region Stamina

		private void HandleStaminaRegeneration()
		{
			if (_staminaController != null)
			{
				UpdateStaminaDrainSources();
				return;
			}

			if (IsDowned())
			{
				_countStaminaTimeout = false;
				_canRegenerateStamina = false;
				return;
			}

			if (IsSharedCarryStraining())
			{
				_countStaminaTimeout = false;
				_canRegenerateStamina = false;
				return;
			}

			if (_canRegenerateStamina)
			{
				_currentStamina += Time.deltaTime;
				if (_currentStamina >= MaxStamina)
				{
					_canRegenerateStamina = false;
					_currentStamina = MaxStamina;
				}
			}
			if (_countStaminaTimeout)
			{
				_staminaRegenerationTimeoutCounter += Time.deltaTime;
				if(_staminaRegenerationTimeoutCounter >= StaminaRegenerationTimeout)
				{
					_canRegenerateStamina = true;
					_countStaminaTimeout = false;
					_staminaRegenerationTimeoutCounter = 0;
				}
			}
        }

		private void HandleCarryingStaminaUsage()
		{
			if (IsDowned())
			{
				return;
			}

			if (_staminaController != null)
			{
				if (_playerInteractionNew != null && _playerInteractionNew.IsSharedCarryMovementActive)
				{
					HandleSharedCarryStaminaUsage();
				}
				else
				{
					CancelSharedCarryExhaustionWarning();
				}
				return;
			}

			if (_playerInteractionNew != null && _playerInteractionNew.IsSharedCarryMovementActive)
			{
				HandleSharedCarryStaminaUsage();
				return;
			}

			CancelSharedCarryExhaustionWarning();
			if (_holdedItemMovementSpeedPenaltyMultiplier > 0)
			{
				_currentStamina -= Time.deltaTime;
				if (_currentStamina < 0 )
				{
					_currentStamina = 0;
					Debug.Log("You have been crushed by Holded Item.");
				}
			}
		}

		private void HandleSharedCarryStaminaUsage()
		{
			if (!IsSharedCarryStraining())
			{
				CancelSharedCarryExhaustionWarning();
				return;
			}

			if (_staminaController == null)
			{
				_currentStamina = Mathf.Max(0f, _currentStamina - _playerInteractionNew.SharedCarryUnderstaffedStaminaDrainPerSecond * Time.deltaTime);
			}
			if (CurrentStamina > 0f)
			{
				return;
			}

			if (!_isSharedCarryExhaustionWarningActive)
			{
				_isSharedCarryExhaustionWarningActive = true;
				_sharedCarryExhaustionWarningElapsed = 0f;
				_sharedCarryExhaustionRequested = false;
				OnSharedCarryExhaustionWarningChanged?.Invoke(this, new SharedCarryExhaustionWarningChangedEventArgs { IsWarningActive = true });
			}

			_sharedCarryExhaustionWarningElapsed += Time.deltaTime;
			if (!_sharedCarryExhaustionRequested && _sharedCarryExhaustionWarningElapsed >= Mathf.Max(0.1f, sharedCarryExhaustionWarningDuration))
			{
				_sharedCarryExhaustionRequested = true;
				_playerInteractionNew.RequestSharedCarryExhaustion();
			}
		}

		private bool IsSharedCarryStraining()
		{
			return _playerInteractionNew != null && _playerInteractionNew.IsSharedCarryUnderstaffed;
		}

		private void UpdateStaminaDrainSources()
		{
			bool downed = IsDowned();
			bool sharedCarry = !downed && _playerInteractionNew != null && _playerInteractionNew.IsSharedCarryMovementActive;
			bool understaffed = sharedCarry && _playerInteractionNew.IsSharedCarryUnderstaffed;
			bool moving = _input != null && _input.move != Vector2.zero;
			_staminaController.SetDrainSource(StaminaDrainSource.Sprint, !downed && moving && IsSprinting ? 1f : 0f);
			_staminaController.SetDrainSource(
				StaminaDrainSource.UnderstaffedSharedCarry,
				understaffed ? _playerInteractionNew.SharedCarryUnderstaffedStaminaDrainPerSecond : 0f);
			_staminaController.SetDrainSource(
				StaminaDrainSource.Carry,
				!downed && !sharedCarry && _holdedItemMovementSpeedPenaltyMultiplier > 0f ? 1f : 0f);
		}

		private void CancelSharedCarryExhaustionWarning()
		{
			if (!_isSharedCarryExhaustionWarningActive)
			{
				return;
			}

			_isSharedCarryExhaustionWarningActive = false;
			_sharedCarryExhaustionWarningElapsed = 0f;
			_sharedCarryExhaustionRequested = false;
			OnSharedCarryExhaustionWarningChanged?.Invoke(this, new SharedCarryExhaustionWarningChangedEventArgs { IsWarningActive = false });
		}

		public float GetStaminaNormalized()
		{
			return _staminaController != null ? _staminaController.NormalizedStamina : _currentStamina / MaxStamina;
		}

		public void ApplyWaterFloatHeight(float targetRootY)
		{
			if (_controller == null || IsBeingCarried())
			{
				return;
			}

			_verticalVelocity = 0f;
			float delta = targetRootY - transform.position.y;
			_controller.Move(Vector3.up * Mathf.Clamp(delta, -2f * Time.deltaTime, 2f * Time.deltaTime));
		}

		private bool IsDowned()
		{
			return _playerHealth != null && _playerHealth.IsDowned;
		}

		private bool IsBeingCarried()
		{
			return _downedPlayerCarryable != null && _downedPlayerCarryable.IsCarried;
		}

        #endregion

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}
