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
		public event EventHandler<SharedCarryExhaustionWarningChangedEventArgs> OnSharedCarryExhaustionWarningChanged;

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
		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;

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

		// cinemachine
		private float _cinemachineTargetPitch;

		// player
		private float _speed;
		private bool _isSprinting = false;
		private bool _isJumpPerformed = false;
        private float _rotationVelocity;
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
		
		private const float _threshold = 0.01f;

		public float VerticalVelocity => _verticalVelocity;
		public bool IsSprinting => _isSprinting && _currentStamina > 0f && !IsDowned();
		public bool IsSharedCarryExhaustionWarningActive => _isSharedCarryExhaustionWarningActive;

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
			_currentStamina = MaxStamina;
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

			if (!_playerHealth.IsDowned)
			{
				_currentStamina = MaxStamina;
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

#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif
            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;
			_playerInteractionNew.UpdateHoldedItemMovementSpeedPenalty += PlayerInteraction_OnHoldedItemMovementSpeedPenaltyUpdate;
			_playerInventory.MovementSpeedPenaltyUpdated += PlayerInventory_OnInventoryItemMovementSpeedPenaltyUpdate;
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
			HandleStaminaRegeneration();
			HandleCarryingStaminaUsage();
		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		private void GroundedCheck()
		{
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
		}

		private void CameraRotation()
		{
            Vector2 lookDelta = _playerInputNew.GetLookDeltaValue();
            bool hasLookInput = lookDelta.sqrMagnitude >= _threshold;
            if (hasLookInput)
            {
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetPitch += lookDelta.y * RotationSpeed * deltaTimeMultiplier;
                _rotationVelocity = lookDelta.x * RotationSpeed * deltaTimeMultiplier;
            }
            else
            {
                _rotationVelocity = 0f;
            }

            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);
            CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

            if (hasLookInput)
            {
                transform.Rotate(Vector3.up * _rotationVelocity);
            }
		}

		private void Move()
		{
			if (_controller == null || !_controller.enabled)
			{
				_isSprinting = false;
				_isJumpPerformed = false;
				_speed = 0f;
				return;
			}

			if (IsBeingCarried())
			{
				_isSprinting = false;
				_isJumpPerformed = false;
				_speed = 0f;
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

			// set target speed based on move speed, sprint speed and if sprint is pressed
			float targetSpeed = 0f;
			if (_isSprinting && _currentStamina > 0)
			{
				targetSpeed = SprintSpeed;
			}
			else
			{
				targetSpeed = MoveSpeed;
			}
			// apply movement speed penalties from holded item and inventory items
			targetSpeed *= (1- _holdedItemMovementSpeedPenaltyMultiplier);
			targetSpeed *= (1 - _inventoryItemMovementSpeedPenaltyMultiplier);
            // a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

            // note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is no input, set the target speed to 0
            // Prefab setup - StarterAssetsInputs handling
            //if (_input.move == Vector2.zero) targetSpeed = 0.0f;
            if (_playerInputNew.GetMoveVectorValue() == Vector2.zero) targetSpeed = 0.0f;
            // a reference to the players current horizontal velocity
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

			float speedOffset = 0.1f;
            // Prefab setup - StarterAssetsInputs handling
            //float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;
            float inputMagnitude = _input.analogMovement ? _playerInputNew.GetMoveVectorValue().magnitude : 1f;
            // accelerate or decelerate to target speed
            if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				// creates curved result rather than a linear one giving a more organic speed change
				// note T in Lerp is clamped, so we don't need to clamp our speed
				_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);

				// round speed to 3 decimal places
				_speed = Mathf.Round(_speed * 1000f) / 1000f;
			}
			else
			{
				_speed = targetSpeed;
			}

            // normalise input direction
            // Prefab setup - StarterAssetsInputs handling
            //Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;
            Vector3 inputDirection = new Vector3(_playerInputNew.GetMoveVectorValue().x, 0.0f, _playerInputNew.GetMoveVectorValue().y).normalized;
            // note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is a move input rotate player when the player is moving

            // Prefab setup - StarterAssetsInputs handling
            //if (_input.move != Vector2.zero)
            if (_playerInputNew.GetMoveVectorValue() != Vector2.zero)
            {
                // move
                // Prefab setup - StarterAssetsInputs handling
                //inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;
                inputDirection = transform.right * _playerInputNew.GetMoveVectorValue().x + transform.forward * _playerInputNew.GetMoveVectorValue().y;
                if (_isSprinting && _currentStamina > 0)
                {
                    _currentStamina -= Time.deltaTime;
                    if (_currentStamina < 0)
                    {
                        _currentStamina = 0;
                    }
                }
            }


			// move the player
			_controller.Move(inputDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
		}

		private void MoveWhileDowned()
		{
			_isSprinting = false;
			_isJumpPerformed = false;
			_speed = 0f;
			_controller.Move(new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
		}

		private void MoveDuringSharedCarry()
		{
            Vector2 moveInput = _playerInputNew.GetMoveVectorValue();
			Vector3 worldMoveInput = GetSharedCarryWorldMoveInput(moveInput);
			SendSharedCarryInputIfNeeded(worldMoveInput);

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

			_controller.Move(new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
			_speed = 0f;
		}

		private Vector3 GetSharedCarryWorldMoveInput(Vector2 moveInput)
		{
			if (moveInput == Vector2.zero)
			{
				return Vector3.zero;
			}

			Vector3 worldMoveInput = transform.forward * moveInput.y + transform.right * moveInput.x;
			worldMoveInput.y = 0f;
			return Vector3.ClampMagnitude(worldMoveInput, 1f);
		}

		private void SendSharedCarryInputIfNeeded(Vector3 worldMoveInput)
		{
			_sharedCarryInputSendTimer += Time.deltaTime;
			bool inputChanged = Vector3.Distance(_lastSentSharedCarryInput, worldMoveInput) >= sharedCarryInputChangeThreshold;
			if (_sharedCarryInputSendTimer < sharedCarryInputSendInterval && !inputChanged)
			{
				return;
			}

			_sharedCarryInputSendTimer = 0f;
			_lastSentSharedCarryInput = worldMoveInput;
			_playerInteractionNew.SubmitSharedCarryInput(worldMoveInput);
		}


		private void JumpAndGravity()
		{
			if (IsBeingCarried())
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

			_currentStamina = Mathf.Max(0f, _currentStamina - _playerInteractionNew.SharedCarryUnderstaffedStaminaDrainPerSecond * Time.deltaTime);
			if (_currentStamina > 0f)
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
			return _currentStamina / MaxStamina;
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
