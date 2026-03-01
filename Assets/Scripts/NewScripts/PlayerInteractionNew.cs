using StarterAssets;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractionNew : MonoBehaviour
{
    private PlayerInputNew _playerInputNew;
    [Header("Interaction Parameters")]
    private GameObject _currentInteractable = null;
    [SerializeField] private float interactDistance = 2f;
    
    [Header("Action Parameters")]
    [SerializeField] private float actionRange = .9f; // to change for holded item action range
    [SerializeField] private float actionCooldown = 1f; // Time in seconds between allowed actions, can be used to prevent spamming actions
                                                        // To change for holded item action cooldown
    private float actionCooldownTimer = 0f;
    [SerializeField] private Transform actionTransformHolder;
    [SerializeField] private bool repeatAction = true; // If true, the action will be performed repeatedly while the action button is held down.
                                                       // If false, the action will only be performed once per button press.
                                                       // To change for holded item action repeatability
    private bool performAction = false;

    [Header("PickUp Parameters")]
    public bool temp;
    [SerializeField] private IPIckableNew pickedUpObject = null;
    [SerializeField] private Transform pickUpHoldPositionHolder;
    [SerializeField]private GameObject _pickedUpGameObject = null;


    private void Awake()
    {
        _playerInputNew = GetComponent<PlayerInputNew>();
        _playerInputNew.OnInteract += HandleInteract;
        _playerInputNew.OnAction += HandleAction;
        _playerInputNew.OnActionAlt += HandleActionAlt;
        _playerInputNew.OnActionCanceled += HandleActionCanceled;
    }

    private void HandleActionCanceled(object sender, EventArgs e)
    {
        performAction = false;
    }

    private void HandleActionAlt(object sender, EventArgs e)
    {
        Debug.Log("Action Alt");
    }

    private void HandleAction(object sender, EventArgs e)
    {
        performAction = true;
    }

    public void PerformAction()
    {
        // Add handling for action range depending on holded item
        Collider[] colliders = Physics.OverlapBox(actionTransformHolder.position, new Vector3(actionRange, actionRange, actionRange), actionTransformHolder.rotation);
        if (colliders.Length == 0)
        {
            Debug.Log("No 'Action' objects in range");
            return;
        }
        foreach (Collider collider in colliders)
        {
            collider.transform.parent.TryGetComponent<IDamageableNew>(out IDamageableNew damageable);
            if (damageable != null)
            {
                damageable.DamageReceived(10f); // Example damage amount, can be changed or made variable
                Debug.Log($"Action performed on {collider.transform.parent.gameObject.name}");
            }
            else
            {
                Debug.Log($"Collider {collider.gameObject.name} is in range but does not implement IDamageableNew");
            }
        }
    }

    public void TryPerformAction()
    {
        actionCooldownTimer -= Time.deltaTime;
        if(performAction && actionCooldownTimer <= 0)
        {
            PerformAction();
            if (!repeatAction)
            {
                performAction = false;
            }
            actionCooldownTimer = actionCooldown;
        }
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
        BaseFactory newInteractableObject = null;
        foreach (RaycastHit raycastHit in raycasts)
        {
            raycastHit.transform.parent.TryGetComponent<IInteractableNew>(out IInteractableNew interactable);
            if (interactable != null)
            {   raycastHit.transform.parent.TryGetComponent<BaseFactory>(out BaseFactory baseFactory);
                if (baseFactory == null)
                {
                    continue;
                }
                baseFactory.InteractionOutlineGameobject.SetActive(true);
                newInteractableObject = baseFactory;
            }
        }
        if (newInteractableObject != null)
        {
            if (_currentInteractable != null && newInteractableObject.gameObject != _currentInteractable)
            {
                _currentInteractable.GetComponent<BaseFactory>().InteractionOutlineGameobject.SetActive(false);
                _currentInteractable = newInteractableObject.gameObject;
                _currentInteractable.GetComponent<BaseFactory>().InteractionOutlineGameobject.SetActive(true);
            }
            else if (_currentInteractable == null)
            {
                _currentInteractable = newInteractableObject.gameObject;
                _currentInteractable.GetComponent<BaseFactory>().InteractionOutlineGameobject.SetActive(true);
            }
        }
        else if (_currentInteractable != null)
        { 
            _currentInteractable.GetComponent<BaseFactory>().InteractionOutlineGameobject.SetActive(false);
            _currentInteractable = null;
        }
    }



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void FixedUpdate()
    {
        CheckLookAtInteractable();
        TryPerformAction();
    }
}
