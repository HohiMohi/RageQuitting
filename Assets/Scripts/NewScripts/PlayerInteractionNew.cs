using StarterAssets;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractionNew : MonoBehaviour
{
    private PlayerInputNew _playerInputNew;
    private PlayerHealth _playerHealth;

    [Header("Interaction Parameters")]
    private IInteractableNew _currentInteractable = null;
    [SerializeField] private Transform interactionOrigin;
    [SerializeField] private float interactDistance = 2f;
    [SerializeField] private float interactSphereRadius = 0.25f;
    
    
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
    private int minAmountOfPlayersNeeded = 0;
    private int currentAmountOfPlayersSupporting = 0;
    private float holdedItemMovementSpeedPenalty = 0;

    public bool IsSharedCarryMovementActive => sharedCarryMovementActive && _pickedUpGameObject != null;
    public bool HasPickedUpObject => _pickedUpGameObject != null;
    public bool IsHoldingObject => _pickedUpGameObject != null;
    public bool IsHoldingDownedPlayer => _pickedUpGameObject != null && _pickedUpGameObject.TryGetComponent(out DownedPlayerCarryable _);
    public bool IsHoldingSelfPositionedObject => _pickedUpGameObject != null && pickedUpObjectSelfPositioned;
    public Vector3 CarryBodyAnchorLocalOffset => defaultCarryBodyAnchorLocalPosition;

    public EventHandler<UpdateHoldedItemMovementSpeedPenaltyEventArgs> UpdateHoldedItemMovementSpeedPenalty;
    public event EventHandler OnInteractionPerformed;
    public event EventHandler OnHeldObjectChanged;
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

        if (_currentInteractable is DownedPlayerCarryable downedPlayerCarryable)
        {
            downedPlayerCarryable.RequestRevive(transform);
            OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_currentInteractable is PlayerHealth playerHealth)
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

        if (!TryGetInteractionHits(out RaycastHit[] raycasts))
        {
            return;
        }

        foreach (RaycastHit raycastHit in raycasts)
        {
            if (raycastHit.transform.root == transform.root)
                continue;

            IInteractableNew interactable = raycastHit.transform.GetComponent<IInteractableNew>();
            interactable ??= raycastHit.transform.GetComponentInParent<IInteractableNew>();
            if (interactable != null)
            {
                // If raycast hit object is same as currently holded object, continue
                if (_pickedUpGameObject == raycastHit.transform.gameObject)
                    continue;
                // Check if looking at storage and have object to store, if yes try to store object
                raycastHit.transform.TryGetComponent<BaseStorageNew>(out BaseStorageNew baseStorage);
                if (baseStorage != null && pickedUpObject != null)
                {
                    //Check if the storage is instance of MainStorageNew, if yes try to store object in main storage, if no try to store object in normal storage
                    raycastHit.transform.TryGetComponent<MainStorageNew>(out MainStorageNew mainStorage);
                    if (mainStorage != null)
                    {
                        if (TryStoreObject(mainStorage))
                        {
                            return;
                        }
                        else
                        {
                            Debug.Log("Cannot store object in main storage");
                            //Add feedback for player that the object cannot be stored in main storage
                            return;
                        }
                    }

                    // If not main storage, try to store in normal storage
                    if (TryStoreObject(baseStorage))
                    {
                        return;
                    }
                    else
                    {
                        Debug.Log("Cannot store object in this storage");
                        //Add feedback for player that the object cannot be stored in this storage
                        return;
                    }
                }
                // If looking at pickable object, pick it up
                IPIckableNew pickableObject = raycastHit.transform.GetComponent<IPIckableNew>();
                pickableObject ??= raycastHit.transform.GetComponentInParent<IPIckableNew>();
                if (pickableObject != null)
                {
                    if (_pickedUpGameObject == null)
                    {
                        pickableObject.PickedUp(transform);
                        OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
                    }
                    return;
                }

                // If looking at interactable object, interact with it
                interactable.Interact(transform);
                OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Block interaction if we hit a solid, non-trigger physical obstacle closer than any interactable
            if (raycastHit.collider != null && !raycastHit.collider.isTrigger)
            {
                break;
            }
        }
        // Try to drop object - objects need to be affected by gravity - now Objects are just dropped and stay in the air, player has to jump to pick them up again - to change
        if (DropObject())
        {
            OnInteractionPerformed?.Invoke(this, EventArgs.Empty);
            return;
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
        SetHoldedItemProperties(null);
        OnHeldObjectChanged?.Invoke(this, EventArgs.Empty);
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
        DropObject();
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

    public void SubmitSharedCarryInput(Vector3 worldMoveInput)
    {
        if (!IsSharedCarryMovementActive || !_pickedUpGameObject.TryGetComponent(out ISharedCarryObject sharedCarryObject))
        {
            return;
        }

        sharedCarryObject.SubmitSharedCarryInput(worldMoveInput);
    }

    public Vector3 GetSharedCarryAnchorCorrection()
    {
        if (!IsSharedCarryMovementActive || _pickedUpGameObject == null)
        {
            return Vector3.zero;
        }

        EnsureCarryBodyAnchor();
        Vector3 targetAnchorPosition = _pickedUpGameObject.transform.TransformPoint(sharedCarryAttachLocalPoint);
        Vector3 correction = targetAnchorPosition - carryBodyAnchor.position;
        correction.y = 0f;
        return correction;
    }

    public IInteractableNew GetCurrentInteractable()
    {
        return _currentInteractable;
    }

    public void SetInteractionOrigin(Transform origin)
    {
        interactionOrigin = origin;
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
        if (!TryGetInteractionHits(out RaycastHit[] raycasts))
        {
            ClearCurrentInteractable();
            return;
        }

        IInteractableNew newInteractableObject = null;
        foreach (RaycastHit raycastHit in raycasts)
        {
            if (raycastHit.transform.root == transform.root)
                continue;

            IInteractableNew interactable = raycastHit.transform.GetComponent<IInteractableNew>();
            interactable ??= raycastHit.transform.GetComponentInParent<IInteractableNew>();
            if (interactable != null)
            {
                newInteractableObject = interactable;
                if (interactable == _currentInteractable)
                    break;
                interactable.LookedAt(transform);
                break;
            }

            // Block sight if we hit a solid, non-trigger physical obstacle closer than any interactable
            if (raycastHit.collider != null && !raycastHit.collider.isTrigger)
            {
                break;
            }
        }
        if (newInteractableObject != null)
        {
            if (_currentInteractable != null && newInteractableObject != _currentInteractable)
            {
                _currentInteractable.LookedAway(transform);
                _currentInteractable = newInteractableObject;
            }
            else if (_currentInteractable == null)
            {
                _currentInteractable = newInteractableObject;
            }
        }
        else if (_currentInteractable != null)
        {
            ClearCurrentInteractable();
        }
    }

    private bool TryGetInteractionHits(out RaycastHit[] raycasts)
    {
        raycasts = Array.Empty<RaycastHit>();

        if (interactionOrigin == null)
        {
            return false;
        }

        raycasts = Physics.SphereCastAll(interactionOrigin.position, interactSphereRadius, interactionOrigin.forward, interactDistance);
        Array.Sort(raycasts, (a, b) => a.distance.CompareTo(b.distance));
        return true;
    }

    private void ClearCurrentInteractable()
    {
        if (_currentInteractable == null)
        {
            return;
        }

        _currentInteractable.LookedAway(transform);
        _currentInteractable = null;
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
        _currentInteractable = null;
    }

    void Update()
    {
        if (_playerHealth != null && _playerHealth.IsDowned)
        {
            ClearCurrentInteractable();
            return;
        }

        MovePickedUpObjectToHoldPosition();
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
