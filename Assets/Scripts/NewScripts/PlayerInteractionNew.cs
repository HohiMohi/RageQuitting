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
    [SerializeField] private Transform pickUpHoldPositionHolder;
    private GameObject _pickedUpObject = null;

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
        if(DropObject())
        {
            return;
        }

        RaycastHit[] raycasts = Physics.RaycastAll(transform.position, transform.forward, interactDistance);
        //Collider[] colliders = Physics.OverlapSphere(transform.position, interactDistance);
        foreach (RaycastHit raycastHit in raycasts)
        {
            raycastHit.transform.parent.TryGetComponent<IInteractableNew>(out IInteractableNew interactable);
            if (interactable != null)
            {
                Debug.Log(raycastHit.transform.name);
                raycastHit.transform.parent.TryGetComponent<IPIckableNew>(out IPIckableNew pickableObject);
                if (pickableObject != null)
                {
                    pickableObject.PickedUp(transform);
                    return;
                }
                interactable.Interact(transform);
            }
        }

    }

    public void PickUpObject(GameObject pickUpObject)
    {
        _pickedUpObject = pickUpObject;
        _pickedUpObject.transform.SetParent(pickUpHoldPositionHolder);
        _pickedUpObject.transform.localPosition = Vector3.zero;
        _pickedUpObject.transform.localRotation = Quaternion.identity;
    }

    public bool DropObject()
    {
        if (_pickedUpObject != null)
        {
            _pickedUpObject.transform.SetParent(null);
            _pickedUpObject = null;
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
