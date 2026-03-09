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
    
    
    [Header("PickUp Parameters")]
    public bool temp;
    [SerializeField] private IPIckableNew pickedUpObject = null;
    [SerializeField] private Transform pickUpHoldPositionHolder;
    [SerializeField]private GameObject _pickedUpGameObject = null;



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


        RaycastHit[] raycasts = Physics.RaycastAll(transform.position, transform.forward, interactDistance);
        //Collider[] colliders = Physics.OverlapSphere(transform.position, interactDistance);
        foreach (RaycastHit raycastHit in raycasts)
        {
            raycastHit.transform.parent.TryGetComponent<IInteractableNew>(out IInteractableNew interactable);
            if (interactable != null)
            {
                // Check if looking at storage and have object to store, if yes try to store object
                raycastHit.transform.parent.TryGetComponent<BaseStorageNew>(out BaseStorageNew baseStorage);
                if (baseStorage != null && pickedUpObject != null)
                {
                    //Check if the storage is instance of MainStorageNew, if yes try to store object in main storage, if no try to store object in normal storage
                    raycastHit.transform.parent.TryGetComponent<MainStorageNew>(out MainStorageNew mainStorage);
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
                Debug.Log(raycastHit.transform.name);
                raycastHit.transform.parent.TryGetComponent<IPIckableNew>(out IPIckableNew pickableObject);
                if (pickableObject != null)
                {
                    pickableObject.PickedUp(transform);
                    return;
                }
                // If looking at interactable object, interact with it
                interactable.Interact(transform);
                return;
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
        pickedUpObject = pIckableObject;
        _pickedUpGameObject.transform.SetParent(pickUpHoldPositionHolder);
        _pickedUpGameObject.transform.localPosition = Vector3.zero;
        _pickedUpGameObject.transform.localRotation = Quaternion.identity;
    }

    public bool DropObject()
    {
        if (_pickedUpGameObject != null)
        {
            _pickedUpGameObject.transform.SetParent(null);
            _pickedUpGameObject = null;
            pickedUpObject = null;
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
            pickedUpObject = null;
        }
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
        RaycastHit[] raycasts = Physics.RaycastAll(transform.position, transform.forward, interactDistance);
        //Collider[] colliders = Physics.OverlapSphere(transform.position, interactDistance);
        IInteractableNew newInteractableObject = null;
        foreach (RaycastHit raycastHit in raycasts)
        {
            raycastHit.transform.parent.TryGetComponent<IInteractableNew>(out IInteractableNew interactable);
            if (interactable != null)
            {
                newInteractableObject = interactable;
                if (interactable == _currentInteractable)
                    break;
                interactable.LookedAt(transform);
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
