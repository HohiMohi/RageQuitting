using StarterAssets;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractionNew : MonoBehaviour
{
    private PlayerInputNew _playerInputNew;
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
            raycastHit.transform.TryGetComponent<IInteractableNew>(out IInteractableNew interactable);
            if (interactable != null)
            {
                interactable.Interact(transform);
            }
        }

    }

    private void CheckLookAtInteractable()
    {
        RaycastHit[] raycasts = Physics.RaycastAll(transform.position, transform.forward, interactDistance);
        //Collider[] colliders = Physics.OverlapSphere(transform.position, interactDistance);
        BaseFactory newInteractableObject = null;
        foreach (RaycastHit raycastHit in raycasts)
        {
            raycastHit.transform.TryGetComponent<IInteractableNew>(out IInteractableNew interactable);
            if (interactable != null)
            {   raycastHit.transform.TryGetComponent<BaseFactory>(out BaseFactory baseFactory);
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
