using StarterAssets;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractionNew : MonoBehaviour
{
    private PlayerInputNew _playerInputNew;

    [Header("Interaction Parameters")]
    private IInteractableNew _currentInteractable = null;
    [SerializeField] private float interactDistance = 2f;
    [SerializeField] private float interactSphereRadius = 0.25f;
    
    
    [Header("PickUp Parameters")]
    public bool temp;
    [SerializeField] private IPIckableNew pickedUpObject = null;
    [SerializeField] private Transform pickUpHoldPositionHolder;
    [SerializeField] private GameObject _pickedUpGameObject = null;
    private int minAmountOfPlayersNeeded = 0;
    private int currentAmountOfPlayersSupporting = 0;
    private float holdedItemMovementSpeedPenalty = 0;

    public EventHandler<UpdateHoldedItemMovementSpeedPenaltyEventArgs> UpdateHoldedItemMovementSpeedPenalty;
    public class UpdateHoldedItemMovementSpeedPenaltyEventArgs : EventArgs
    {
        public float currentMovementSpeedPenaltyMultiplier;
    }

    private void Awake()
    {
        _playerInputNew = GetComponent<PlayerInputNew>();
        _playerInputNew.OnInteract += HandleInteract;
    }

    

    private void OnDestroy()
    {
        _playerInputNew.OnInteract -= HandleInteract;
    }

    private void HandleInteract(object sender, EventArgs e)
    {
        RaycastHit[] raycasts = Physics.SphereCastAll(Camera.main.transform.position, interactSphereRadius, Camera.main.transform.forward, interactDistance);
        
        // Sort hits by ascending distance so we prioritize the closest target
        Array.Sort(raycasts, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit raycastHit in raycasts)
        {
            if (raycastHit.transform.gameObject == gameObject || raycastHit.transform.IsChildOf(transform))
                continue;

            raycastHit.transform.TryGetComponent<IInteractableNew>(out IInteractableNew interactable);
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
                raycastHit.transform.TryGetComponent<IPIckableNew>(out IPIckableNew pickableObject);
                if (pickableObject != null)
                {
                    if (_pickedUpGameObject == null)
                        pickableObject.PickedUp(transform);
                    return;
                }

                // If looking at interactable object, interact with it
                interactable.Interact(transform);
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
            return;
        }

    }

    public void PickUpObject(GameObject pickUpObject, IPIckableNew pIckableObject)
    {
        _pickedUpGameObject = pickUpObject;
        SetHoldedItemProperties(pIckableObject);
        _pickedUpGameObject.transform.SetParent(pickUpHoldPositionHolder);
        _pickedUpGameObject.transform.localPosition = Vector3.zero;
        _pickedUpGameObject.transform.localRotation = Quaternion.identity;
    }

    public bool DropObject()
    {
        if (_pickedUpGameObject != null)
        {
            GameObject droppedGo = _pickedUpGameObject;
            _pickedUpGameObject = null;

            droppedGo.transform.SetParent(null);

            // Position it slightly in front of the player to avoid physics clipping/stuck
            Vector3 dropPosition = transform.position + transform.forward * 1.0f + Vector3.up * 0.5f;
            droppedGo.transform.position = dropPosition;

            pickedUpObject.DroppedDown();

            // Add a gentle forward nudge
            if (droppedGo.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.AddForce(transform.forward * 1.5f, ForceMode.Impulse);
            }

            SetHoldedItemProperties(null);
            return true;
        }
        return false;
    }

    public void RemovePickedUpObject()
    {
        if (_pickedUpGameObject != null)
        {
            Destroy(_pickedUpGameObject);
            _pickedUpGameObject = null;
            SetHoldedItemProperties(null);
        }
    }

    public GameObject GetPickedUpGameObject()
    {
        return _pickedUpGameObject;
    }

    public IInteractableNew GetCurrentInteractable()
    {
        return _currentInteractable;
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
            return true;
        }
        return false;
    }

    private void CheckLookAtInteractable()
    {
        RaycastHit[] raycasts = Physics.SphereCastAll(Camera.main.transform.position, interactSphereRadius, Camera.main.transform.forward, interactDistance);
        
        // Sort hits by ascending distance so we prioritize the closest target
        Array.Sort(raycasts, (a, b) => a.distance.CompareTo(b.distance));

        IInteractableNew newInteractableObject = null;
        foreach (RaycastHit raycastHit in raycasts)
        {
            if (raycastHit.transform.gameObject == gameObject || raycastHit.transform.IsChildOf(transform))
                continue;

            raycastHit.transform.TryGetComponent<IInteractableNew>(out IInteractableNew interactable);
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
            if(_currentInteractable == null)
                _currentInteractable = null;
            _currentInteractable.LookedAway(transform);
            _currentInteractable = null;
        }
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

    // Update is called once per frame
    void Update()
    {
        
    }


    private void FixedUpdate()
    {
        CheckLookAtInteractable();
    }
}
