using StarterAssets;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractionNew : MonoBehaviour, ICarriedPlayerAnchorProvider
{
    private PlayerInputNew _playerInputNew;
    private PlayerHealth _playerHealth;

    [Header("Interaction Parameters")]
    private IInteractableNew _currentInteractable = null;
    private MonoBehaviour _currentTarget;
    [SerializeField] private Transform interactionOrigin;
    [SerializeField] private float interactDistance = 2f;
    [SerializeField, Min(0f)] private float aimAssistRadius = 0.04f;
    private Camera aimCamera;
    
    
    [Header("PickUp Parameters")]
    public bool temp;
    [SerializeField] private IPIckableNew pickedUpObject = null;
    [SerializeField] private Transform pickUpHoldPositionHolder;
    [SerializeField] private Transform carryBodyAnchor;
    [SerializeField] private Vector3 defaultCarryBodyAnchorLocalPosition = new Vector3(0f, 1.4f, 0f);
    [SerializeField] private Transform carriedPlayerAnchor;
    [SerializeField] private Vector3 defaultCarriedPlayerAnchorLocalPosition = new Vector3(0f, 1f, 1f);
    [SerializeField] private GameObject _pickedUpGameObject = null;
    private bool pickedUpObjectParented = false;
    private bool pickedUpObjectFollowsHoldPosition = true;
    private bool pickedUpObjectSelfPositioned = false;
    private bool sharedCarryMovementActive = false;
    private Vector3 sharedCarryAttachLocalPoint = Vector3.zero;
    private Vector3 sharedCarryBaseAttachLocalPoint = Vector3.zero;
    private Vector3 sharedCarryOrbitPivotLocalPoint = Vector3.zero;
    private float sharedCarryPredictedOrbitAngle;
    private float sharedCarryAuthoritativeOrbitAngle;
    private float sharedCarryGripHeightInput;
    private SharedCarryPhysicsBody sharedCarryPhysicsBody;
    private int minAmountOfPlayersNeeded = 0;
    private int currentAmountOfPlayersSupporting = 0;
    private float holdedItemMovementSpeedPenalty = 0;
    private int sharedCarryPlayerHolderCount;
    private int sharedCarryRequiredPlayerCount;
    private float sharedCarryUnderstaffedStaminaDrainPerSecond;

    public bool IsSharedCarryMovementActive => sharedCarryMovementActive && _pickedUpGameObject != null;
    public bool IsPhysicalPointGripActive => IsSharedCarryMovementActive && sharedCarryPhysicsBody != null
        && sharedCarryPhysicsBody.ControlMode == SharedCarryControlMode.PhysicalPointGrip;
    public bool HasPickedUpObject => _pickedUpGameObject != null;
    public bool IsHoldingObject => _pickedUpGameObject != null;
    public bool IsHoldingDownedPlayer => _pickedUpGameObject != null && _pickedUpGameObject.TryGetComponent(out DownedPlayerCarryable _);
    public Transform CarriedPlayerAnchor => GetCarriedPlayerAnchor();
    public bool IsHoldingSelfPositionedObject => _pickedUpGameObject != null && pickedUpObjectSelfPositioned;
    public bool IsSharedCarryUnderstaffed => IsSharedCarryMovementActive && sharedCarryPlayerHolderCount < sharedCarryRequiredPlayerCount;
    public float SharedCarryUnderstaffedStaminaDrainPerSecond => sharedCarryUnderstaffedStaminaDrainPerSecond;
    public int CurrentSharedCarryPlayerCount => sharedCarryPlayerHolderCount;
    public int RequiredSharedCarryPlayerCount => sharedCarryRequiredPlayerCount;
    public Vector3 CarryBodyAnchorLocalOffset => defaultCarryBodyAnchorLocalPosition;

    public EventHandler<UpdateHoldedItemMovementSpeedPenaltyEventArgs> UpdateHoldedItemMovementSpeedPenalty;
    public event EventHandler OnInteractionPerformed;
    public event EventHandler OnHeldObjectChanged;
    public event EventHandler OnHeldObjectForcedRelease;
    public event EventHandler OnCurrentTargetChanged;
    public event Action<SharedCarryPickupRejectedEventArgs> OnSharedCarryPickupRejected;
    public MonoBehaviour CurrentTarget => _currentTarget;
    public class UpdateHoldedItemMovementSpeedPenaltyEventArgs : EventArgs
    {
        public float currentMovementSpeedPenaltyMultiplier;
    }

    private void Awake()
    {
        _playerInputNew = GetComponent<PlayerInputNew>();
        _playerInputNew.OnInteract += HandleInteract;
        _playerInputNew.OnActionAlt += HandleActionAlt;
        _playerHealth = GetComponent<PlayerHealth>();
        EnsureCarryBodyAnchor();
        EnsureCarriedPlayerAnchor();
    }

    

    private void OnDestroy()
    {
        _playerInputNew.OnInteract -= HandleInteract;
        _playerInputNew.OnActionAlt -= HandleActionAlt;
    }

    private void HandleActionAlt(object sender, EventArgs e)
    {
        if (_playerHealth != null && _playerHealth.IsDowned)
        {
            return;
        }

        if (_currentTarget is DownedPlayerCarryable downedPlayerCarryable)
        {
            downedPlayerCarryable.RequestRevive(transform);
            OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_currentTarget is PlayerHealth playerHealth)
        {
            NetworkObject reviverNetworkObject = GetComponent<NetworkObject>();
            playerHealth.RequestRevive(reviverNetworkObject);
            OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandleInteract(object sender, EventArgs e)
    {
        if (_playerHealth != null && _playerHealth.IsDowned)
        {
            _playerHealth.RequestRespawn();
            OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_pickedUpGameObject != null && _pickedUpGameObject.TryGetComponent(out DownedPlayerCarryable _))
        {
            DropObject();
            OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
            return;
        }

        MonoBehaviour target = _currentTarget;
        if (target == null || target.transform.root == transform.root)
        {
            TryDropHeldObject();
            return;
        }

        if (_pickedUpGameObject == target.gameObject)
        {
            TryDropHeldObject();
            return;
        }

        BaseStorageNew baseStorage = target.GetComponent<BaseStorageNew>();
        baseStorage ??= target.GetComponentInParent<BaseStorageNew>();
        if (baseStorage != null && pickedUpObject != null)
        {
            if (baseStorage is MainStorageNew mainStorage)
            {
                if (!TryStoreObject(mainStorage))
                {
                    Debug.Log("Cannot store object in main storage");
                }
                return;
            }

            if (!TryStoreObject(baseStorage))
            {
                Debug.Log("Cannot store object in this storage");
            }
            return;
        }

        IPIckableNew pickableObject = target.GetComponent<IPIckableNew>();
        pickableObject ??= target.GetComponentInParent<IPIckableNew>();
        if (pickableObject != null)
        {
            if (_pickedUpGameObject == null)
            {
                if (pickableObject is BaseResourceNew baseResource && !baseResource.CanBeCarried)
                {
                    return;
                }

                pickableObject.PickedUp(transform);
                OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        if (target is IInteractableNew interactable)
        {
            interactable.Interact(transform);
            OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
            return;
        }

        TryDropHeldObject();
    }

    private void TryDropHeldObject()
    {
        if (DropObject())
        {
            OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PickUpObject(GameObject pickUpObject, IPIckableNew pIckableObject)
    {
        PickUpObject(pickUpObject, pIckableObject, true);
    }

    public void PickUpObject(GameObject pickUpObject, IPIckableNew pIckableObject, bool followHoldPosition)
    {
        PickUpObject(pickUpObject, pIckableObject, followHoldPosition, false, Vector3.zero);
    }

    private void PickUpObject(GameObject pickUpObject, IPIckableNew pIckableObject, bool followHoldPosition, bool useSharedCarryMovement, Vector3 attachLocalPoint)
    {
        PickUpObject(pickUpObject, pIckableObject, followHoldPosition, useSharedCarryMovement, attachLocalPoint, false);
    }

    private void PickUpObject(GameObject pickUpObject, IPIckableNew pIckableObject, bool followHoldPosition, bool useSharedCarryMovement, Vector3 attachLocalPoint, bool selfPositioned)
    {
        _pickedUpGameObject = pickUpObject;
        pickedUpObjectFollowsHoldPosition = followHoldPosition;
        pickedUpObjectSelfPositioned = selfPositioned;
        sharedCarryMovementActive = useSharedCarryMovement;
        sharedCarryAttachLocalPoint = attachLocalPoint;
        sharedCarryBaseAttachLocalPoint = attachLocalPoint;
        sharedCarryPredictedOrbitAngle = 0f;
        sharedCarryAuthoritativeOrbitAngle = 0f;
        sharedCarryPhysicsBody = useSharedCarryMovement ? pickUpObject.GetComponent<SharedCarryPhysicsBody>() : null;
        sharedCarryOrbitPivotLocalPoint = useSharedCarryMovement
            ? SharedCarryAttachmentUtility.GetLocalColliderBounds(pickUpObject.transform).center
            : Vector3.zero;
        ClearSharedCarryStaminaLoad();
        SetHoldedItemProperties(pIckableObject);

        if (!pickedUpObjectSelfPositioned && ShouldParentPickedUpObject(_pickedUpGameObject))
        {
            _pickedUpGameObject.transform.SetParent(pickUpHoldPositionHolder);
            _pickedUpGameObject.transform.localPosition = Vector3.zero;
            _pickedUpGameObject.transform.localRotation = Quaternion.identity;
            pickedUpObjectParented = true;
        }
        else
        {
            pickedUpObjectParented = false;
            MovePickedUpObjectToHoldPosition();
        }

        OnHeldObjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ConfirmPickedUpObject(GameObject pickUpObject, IPIckableNew pIckableObject)
    {
        PickUpObject(pickUpObject, pIckableObject);
    }

    public void ConfirmPickedUpObject(GameObject pickUpObject, IPIckableNew pIckableObject, bool followHoldPosition, float movementSpeedPenalty)
    {
        PickUpObject(pickUpObject, pIckableObject, followHoldPosition);
        SetHoldedItemMovementSpeedPenalty(movementSpeedPenalty);
    }

    public void ConfirmPickedUpObject(GameObject pickUpObject, IPIckableNew pIckableObject, bool followHoldPosition, float movementSpeedPenalty, bool useSharedCarryMovement, Vector3 attachLocalPoint)
    {
        PickUpObject(pickUpObject, pIckableObject, followHoldPosition, useSharedCarryMovement, attachLocalPoint);
        SetHoldedItemMovementSpeedPenalty(movementSpeedPenalty);
    }

    public void ConfirmPickedUpObject(GameObject pickUpObject, IPIckableNew pIckableObject, bool followHoldPosition, float movementSpeedPenalty, bool useSharedCarryMovement, Vector3 attachLocalPoint, bool selfPositioned)
    {
        PickUpObject(pickUpObject, pIckableObject, followHoldPosition, useSharedCarryMovement, attachLocalPoint, selfPositioned);
        SetHoldedItemMovementSpeedPenalty(movementSpeedPenalty);
    }

    public void ForceReleasePickedUpObject(GameObject pickUpObject)
    {
        if (_pickedUpGameObject != pickUpObject)
        {
            return;
        }

        _pickedUpGameObject = null;
        pickedUpObjectParented = false;
        pickedUpObjectFollowsHoldPosition = true;
        pickedUpObjectSelfPositioned = false;
        sharedCarryMovementActive = false;
        sharedCarryAttachLocalPoint = Vector3.zero;
        ClearSharedCarryOrbitState();
        ClearSharedCarryStaminaLoad();
        SetHoldedItemProperties(null);
        OnHeldObjectChanged?.Invoke(this, EventArgs.Empty);
        OnHeldObjectForcedRelease?.Invoke(this, EventArgs.Empty);
    }

    public bool DropObject()
    {
        if (_pickedUpGameObject != null)
        {
            GameObject droppedGo = _pickedUpGameObject;
            bool wasSharedCarryMovementActive = sharedCarryMovementActive;
            bool wasSelfPositioned = pickedUpObjectSelfPositioned;
            _pickedUpGameObject = null;

            if (pickedUpObjectParented)
            {
                droppedGo.transform.SetParent(null);
            }

            pickedUpObjectParented = false;
            pickedUpObjectFollowsHoldPosition = true;
            pickedUpObjectSelfPositioned = false;
            sharedCarryMovementActive = false;
            sharedCarryAttachLocalPoint = Vector3.zero;
            ClearSharedCarryOrbitState();
            ClearSharedCarryStaminaLoad();

            // Position it slightly in front of the player to avoid physics clipping/stuck
            Vector3 dropPosition = transform.position + transform.forward * 1.0f + Vector3.up * 0.5f;
            if (!wasSharedCarryMovementActive && !wasSelfPositioned)
            {
                droppedGo.transform.position = dropPosition;
            }

            pickedUpObject.DroppedDown();

            // Add a gentle forward nudge
            if (!wasSharedCarryMovementActive && !wasSelfPositioned && droppedGo.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.AddForce(transform.forward * 1.5f, ForceMode.Impulse);
            }

            SetHoldedItemProperties(null);
            OnHeldObjectChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    public void DropHeldObjectForStateChange()
    {
        if (DropObject())
        {
            OnHeldObjectForcedRelease?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemovePickedUpObject()
    {
        if (_pickedUpGameObject != null)
        {
            if (_pickedUpGameObject.TryGetComponent(out BaseResourceNew baseResourceNew))
            {
                baseResourceNew.RemoveFromWorld();
            }
            else if (_pickedUpGameObject.TryGetComponent(out MountableBridgeComponent mountableBridgeComponent))
            {
                mountableBridgeComponent.RemoveFromWorld();
            }
            else
            {
                Destroy(_pickedUpGameObject);
            }

            _pickedUpGameObject = null;
            pickedUpObjectParented = false;
            pickedUpObjectFollowsHoldPosition = true;
            pickedUpObjectSelfPositioned = false;
            sharedCarryMovementActive = false;
            sharedCarryAttachLocalPoint = Vector3.zero;
            SetHoldedItemProperties(null);
            OnHeldObjectChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public GameObject GetPickedUpGameObject()
    {
        return _pickedUpGameObject;
    }

    public Transform GetPickUpHoldPositionHolder()
    {
        return pickUpHoldPositionHolder;
    }

    public Transform GetCarryBodyAnchor()
    {
        EnsureCarryBodyAnchor();
        return carryBodyAnchor;
    }

    public Transform GetCarriedPlayerAnchor()
    {
        EnsureCarriedPlayerAnchor();
        return carriedPlayerAnchor;
    }

    public float GetCharacterControllerRadius()
    {
        return TryGetComponent(out CharacterController characterController) ? characterController.radius : 0.5f;
    }

    public void ApplySharedCarryPickupPlacement(Vector3 worldPosition)
    {
        if (TryGetComponent(out CharacterController characterController))
        {
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.position = worldPosition;
            characterController.enabled = wasEnabled;
            return;
        }

        transform.position = worldPosition;
    }

    public void NotifySharedCarryPickupRejected(MonoBehaviour target, SharedCarryPickupFailureReason reason)
    {
        OnSharedCarryPickupRejected?.Invoke(new SharedCarryPickupRejectedEventArgs(target, reason));
    }

    public void SubmitSharedCarryInput(Vector3 worldTranslationInput, Vector3 worldLateralInput, float directYawInput, float gripHeightInput)
    {
        if (!IsSharedCarryMovementActive || !_pickedUpGameObject.TryGetComponent(out ISharedCarryObject sharedCarryObject))
        {
            return;
        }

        sharedCarryGripHeightInput = Mathf.Clamp(gripHeightInput, -1f, 1f);
        sharedCarryObject.SubmitSharedCarryInput(worldTranslationInput, worldLateralInput, directYawInput, sharedCarryGripHeightInput);
    }

    public void PredictSharedCarryOrbit(Vector3 worldLateralInput, float deltaTime)
    {
        if (!IsSharedCarryMovementActive || sharedCarryPhysicsBody == null
            || sharedCarryPhysicsBody.ControlMode != SharedCarryControlMode.SpatialOrbit)
        {
            return;
        }

        float tangentialInput = SharedCarryAttachmentUtility.GetTangentialInput(
            _pickedUpGameObject.transform,
            sharedCarryAttachLocalPoint,
            sharedCarryOrbitPivotLocalPoint,
            worldLateralInput);
        float maxAngle = sharedCarryPhysicsBody.OrbitArcDegrees;
        sharedCarryPredictedOrbitAngle = Mathf.Clamp(
            sharedCarryPredictedOrbitAngle + tangentialInput * sharedCarryPhysicsBody.OrbitAngularSpeed * deltaTime,
            -maxAngle,
            maxAngle);
        sharedCarryAttachLocalPoint = SharedCarryAttachmentUtility.CalculateOrbitAttachLocalPoint(
            _pickedUpGameObject.transform,
            sharedCarryBaseAttachLocalPoint,
            sharedCarryOrbitPivotLocalPoint,
            sharedCarryPredictedOrbitAngle);
    }

    public void ReconcileSharedCarryOrbit(float authoritativeAngle)
    {
        if (!IsSharedCarryMovementActive || sharedCarryPhysicsBody == null
            || sharedCarryPhysicsBody.ControlMode != SharedCarryControlMode.SpatialOrbit)
        {
            return;
        }

        sharedCarryAuthoritativeOrbitAngle = Mathf.Clamp(
            authoritativeAngle,
            -sharedCarryPhysicsBody.OrbitArcDegrees,
            sharedCarryPhysicsBody.OrbitArcDegrees);
        float difference = Mathf.Abs(sharedCarryPredictedOrbitAngle - sharedCarryAuthoritativeOrbitAngle);
        if (difference > 12f)
        {
            sharedCarryPredictedOrbitAngle = sharedCarryAuthoritativeOrbitAngle;
        }
        else if (difference > 1f)
        {
            sharedCarryPredictedOrbitAngle = Mathf.MoveTowards(
                sharedCarryPredictedOrbitAngle,
                sharedCarryAuthoritativeOrbitAngle,
                sharedCarryPhysicsBody.OrbitPredictionCorrectionSpeed * 0.1f);
        }
    }

    public void UpdateSharedCarryLoad(float movementSpeedPenalty, int playerHolderCount, int requiredPlayerCount, float understaffedStaminaDrainPerSecond)
    {
        holdedItemMovementSpeedPenalty = movementSpeedPenalty;
        minAmountOfPlayersNeeded = Mathf.Max(1, requiredPlayerCount);
        currentAmountOfPlayersSupporting = Mathf.Max(0, playerHolderCount);
        sharedCarryPlayerHolderCount = Mathf.Max(0, playerHolderCount);
        sharedCarryRequiredPlayerCount = Mathf.Max(1, requiredPlayerCount);
        sharedCarryUnderstaffedStaminaDrainPerSecond = Mathf.Max(0f, understaffedStaminaDrainPerSecond);
        OnHeldObjectChanged?.Invoke(this, EventArgs.Empty);

        UpdateHoldedItemMovementSpeedPenalty?.Invoke(this, new UpdateHoldedItemMovementSpeedPenaltyEventArgs
        {
            currentMovementSpeedPenaltyMultiplier = movementSpeedPenalty,
        });
    }

    public void RequestSharedCarryExhaustion()
    {
        if (!IsSharedCarryUnderstaffed || _pickedUpGameObject == null || !_pickedUpGameObject.TryGetComponent(out ISharedCarryObject sharedCarryObject))
        {
            return;
        }

        sharedCarryObject.RequestSharedCarryExhaustion();
    }

    public Vector3 GetSharedCarryAnchorCorrection()
    {
        if (!IsSharedCarryMovementActive || _pickedUpGameObject == null)
        {
            return Vector3.zero;
        }

        EnsureCarryBodyAnchor();
        Vector3 targetAnchorPosition = _pickedUpGameObject.transform.TransformPoint(sharedCarryAttachLocalPoint);
        if (sharedCarryPhysicsBody != null && sharedCarryPhysicsBody.ControlMode == SharedCarryControlMode.PhysicalPointGrip)
        {
            targetAnchorPosition -= Vector3.up * sharedCarryGripHeightInput * sharedCarryPhysicsBody.MaximumGripHeightOffset;
            return targetAnchorPosition - carryBodyAnchor.position;
        }
        Vector3 correction = targetAnchorPosition - carryBodyAnchor.position;
        correction.y = 0f;
        return correction;
    }

    public Vector3 GetSharedCarryTetherMovement(float deltaTime, float maximumHorizontalSpeed, bool isGrounded)
    {
        if (!IsSharedCarryMovementActive || sharedCarryPhysicsBody == null
            || sharedCarryPhysicsBody.ControlMode != SharedCarryControlMode.PhysicalPointGrip)
        {
            return Vector3.zero;
        }

        Vector3 correction = GetSharedCarryAnchorCorrection();
        float distance = correction.magnitude;
        if (distance <= sharedCarryPhysicsBody.SoftTetherDeadZone)
        {
            return Vector3.zero;
        }

        float correctedDistance = distance - sharedCarryPhysicsBody.SoftTetherDeadZone;
        Vector3 correctionVelocity = correction.normalized * Mathf.Min(
            sharedCarryPhysicsBody.SoftTetherPullSpeed,
            correctedDistance / Mathf.Max(deltaTime, 0.0001f));
        Rigidbody carriedBody = sharedCarryPhysicsBody.Body;
        if (carriedBody != null)
        {
            correctionVelocity += carriedBody.GetPointVelocity(
                _pickedUpGameObject.transform.TransformPoint(sharedCarryAttachLocalPoint))
                * sharedCarryPhysicsBody.SoftTetherVelocityInfluence;
        }

        if (isGrounded && sharedCarryPhysicsBody.PreventGroundedUpwardTether)
        {
            correctionVelocity.y = Mathf.Min(0f, correctionVelocity.y);
        }

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(correctionVelocity, Vector3.up);
        horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity, Mathf.Max(0f, maximumHorizontalSpeed));
        correctionVelocity = horizontalVelocity + Vector3.up * correctionVelocity.y;
        return correctionVelocity * deltaTime;
    }

    public IInteractableNew GetCurrentInteractable()
    {
        return _currentInteractable;
    }

    public void SetInteractionOrigin(Transform origin)
    {
        interactionOrigin = origin;
    }

    public void SetAimCamera(Camera camera)
    {
        aimCamera = camera;
    }

    public bool TryStoreObject(BaseStorageNew storage)
    {
        BaseResourceNew baseResourceNewObject;
        _pickedUpGameObject.TryGetComponent<BaseResourceNew>(out baseResourceNewObject);
        if (baseResourceNewObject != null)
        {
            BaseResourceSO baseResourceSO = baseResourceNewObject.GetBaseResourceSO();
            if (storage.IsStorable(baseResourceSO))
            {
                storage.StoreBaseResource(baseResourceSO, 1); // Example amount, can be changed or made variable
                RemovePickedUpObject();
                OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
                return true;
            }
        }

        return false;
    }

    public bool TryStoreObject(MainStorageNew storage)
    {
        MountableBridgeComponent mountableBridgeComponent;
        _pickedUpGameObject.TryGetComponent<MountableBridgeComponent>(out mountableBridgeComponent);
        if (mountableBridgeComponent != null)
        {
            // Rework this to not directly invoke storage method from player interaction, maybe add event - to handle later
            storage.StoreBridgeComponent(mountableBridgeComponent.GetMountableBridgeComponentSO().bridgeComponentSO);
            RemovePickedUpObject();
            OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    private void CheckLookAtInteractable()
    {
        if (!TryGetAimRay(out Ray aimRay))
        {
            ClearCurrentInteractable();
            return;
        }

        MonoBehaviour newTarget = FindTarget(
            Physics.RaycastAll(aimRay, interactDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide),
            out bool exactRayBlocked);

        if (newTarget == null && !exactRayBlocked && aimAssistRadius > 0f)
        {
            newTarget = FindTarget(
                Physics.SphereCastAll(
                    aimRay,
                    aimAssistRadius,
                    interactDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide),
                out _);
        }

        if (newTarget != _currentTarget)
        {
            SetCurrentTarget(newTarget);
        }
    }

    private MonoBehaviour FindTarget(RaycastHit[] hits, out bool blocked)
    {
        blocked = false;
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.root == transform.root)
            {
                continue;
            }

            MonoBehaviour target = BridgeTargetResolver.Resolve(hit.collider);
            if (target != null)
            {
                return target;
            }

            if (hit.collider != null && !hit.collider.isTrigger)
            {
                blocked = true;
                return null;
            }
        }

        return null;
    }

    private bool TryGetAimRay(out Ray aimRay)
    {
        if (aimCamera == null || !aimCamera.isActiveAndEnabled)
        {
            aimCamera = Camera.main;
        }

        if (aimCamera != null)
        {
            aimRay = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            return true;
        }

        if (interactionOrigin != null)
        {
            aimRay = new Ray(interactionOrigin.position, interactionOrigin.forward);
            return true;
        }

        aimRay = default;
        return false;
    }

    private void SetCurrentTarget(MonoBehaviour target)
    {
        _currentInteractable?.LookedAway(transform);
        _currentTarget = target;
        _currentInteractable = target as IInteractableNew;
        _currentInteractable?.LookedAt(transform);
        OnCurrentTargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearCurrentInteractable()
    {
        if (_currentTarget == null)
        {
            return;
        }

        SetCurrentTarget(null);
    }


    private void SetHoldedItemProperties(IPIckableNew iPIckableNew)
    {
        if (iPIckableNew != null)
        {
            minAmountOfPlayersNeeded = iPIckableNew.GetMinAmountOfPlayersNeeded();
            holdedItemMovementSpeedPenalty = iPIckableNew.GetMovementSpeedPenalty();
            pickedUpObject = iPIckableNew;
            Debug.Log("Properties setted");
        }
        else
        {
            minAmountOfPlayersNeeded = 0;
            holdedItemMovementSpeedPenalty = 0;
            pickedUpObject = null;
            Debug.Log("Properties resetted");
            ClearSharedCarryStaminaLoad();
        }

        float movementSpeedPenalty = CalculateMovementSpeedPenalty();
        UpdateHoldedItemMovementSpeedPenalty?.Invoke(this, new UpdateHoldedItemMovementSpeedPenaltyEventArgs
        {
            currentMovementSpeedPenaltyMultiplier = movementSpeedPenalty,
        });
    }

    public void SetHoldedItemMovementSpeedPenalty(float movementSpeedPenalty)
    {
        holdedItemMovementSpeedPenalty = movementSpeedPenalty;
        minAmountOfPlayersNeeded = 1;
        currentAmountOfPlayersSupporting = 0;

        UpdateHoldedItemMovementSpeedPenalty?.Invoke(this, new UpdateHoldedItemMovementSpeedPenaltyEventArgs
        {
            currentMovementSpeedPenaltyMultiplier = movementSpeedPenalty,
        });
    }

    private void ClearSharedCarryStaminaLoad()
    {
        sharedCarryPlayerHolderCount = 0;
        sharedCarryRequiredPlayerCount = 0;
        sharedCarryUnderstaffedStaminaDrainPerSecond = 0f;
    }

    private void ClearSharedCarryOrbitState()
    {
        sharedCarryBaseAttachLocalPoint = Vector3.zero;
        sharedCarryOrbitPivotLocalPoint = Vector3.zero;
        sharedCarryPredictedOrbitAngle = 0f;
        sharedCarryAuthoritativeOrbitAngle = 0f;
        sharedCarryGripHeightInput = 0f;
        sharedCarryPhysicsBody = null;
    }

    private float CalculateMovementSpeedPenalty()
    {
        if (minAmountOfPlayersNeeded > currentAmountOfPlayersSupporting && minAmountOfPlayersNeeded > 0)
        {
            return holdedItemMovementSpeedPenalty * (minAmountOfPlayersNeeded - currentAmountOfPlayersSupporting);
        }
        else
            // If there is enough supporting players, movement speed penalty == 0
            return 0;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        EquippableItem.OnAnyItemEquipped += EquippableItem_OnAnyItemEquipped;
    }

    private void EquippableItem_OnAnyItemEquipped(object sender, EventArgs e)
    {
        ClearCurrentInteractable();
    }

    void Update()
    {
        if (_playerHealth != null && _playerHealth.IsDowned)
        {
            ClearCurrentInteractable();
            return;
        }

        MovePickedUpObjectToHoldPosition();
    }

    private void LateUpdate()
    {
        if (_playerHealth != null && _playerHealth.IsDowned)
        {
            return;
        }

        CheckLookAtInteractable();
    }

    private bool ShouldParentPickedUpObject(GameObject pickUpObject)
    {
        return !pickUpObject.TryGetComponent(out NetworkObject _);
    }

    private void MovePickedUpObjectToHoldPosition()
    {
        if (_pickedUpGameObject == null || pickedUpObjectParented || pickedUpObjectSelfPositioned || !pickedUpObjectFollowsHoldPosition || pickUpHoldPositionHolder == null)
        {
            return;
        }

        _pickedUpGameObject.transform.SetPositionAndRotation(pickUpHoldPositionHolder.position, pickUpHoldPositionHolder.rotation);
    }

    private void EnsureCarryBodyAnchor()
    {
        if (carryBodyAnchor != null)
        {
            return;
        }

        GameObject anchorGameObject = new GameObject("CarryBodyAnchor");
        carryBodyAnchor = anchorGameObject.transform;
        carryBodyAnchor.SetParent(transform);
        carryBodyAnchor.localPosition = defaultCarryBodyAnchorLocalPosition;
        carryBodyAnchor.localRotation = Quaternion.identity;
    }

    private void EnsureCarriedPlayerAnchor()
    {
        if (carriedPlayerAnchor != null)
        {
            return;
        }

        GameObject anchorGameObject = new GameObject("CarriedPlayerAnchor");
        carriedPlayerAnchor = anchorGameObject.transform;
        carriedPlayerAnchor.SetParent(transform);
        carriedPlayerAnchor.localPosition = defaultCarriedPlayerAnchorLocalPosition;
        carriedPlayerAnchor.localRotation = Quaternion.identity;
    }
}
