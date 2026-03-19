using System;
using UnityEngine;

public class PlayerActionController : MonoBehaviour
{

    private PlayerInventory _inventory;
    private PlayerInputNew _playerInputNew;
    [Header("Action Parameters")]
    #region Tooltip
    [Tooltip("Base action range - when player has NOT equipped item.")]
    #endregion
    [SerializeField] private float baseActionRange = .9f;
    #region Tooltip
    [Tooltip("Base action range - when player has NOT equipped item.")]
    #endregion
    [SerializeField] private float baseActionCooldown = 1f;
    #region Tooltip
    [Tooltip("Base action repeatability - when player has NOT equipped item. " +
        "If true, the action will be performed repeatedly while the action button is held down." +
        "If false, the action will only be performed once per button press.")]
    #endregion
    [SerializeField] private bool baseRepeatAction = false;
    #region Tooltip
    [Tooltip("Base action damage - when player has NOT equipped item.")]
    #endregion
    [SerializeField] private float baseActionDamage = 5f;
    private float actionRange;
    private float actionCooldown;
    private float actionCooldownTimer = 0f;
    private float actionDamage;
    [SerializeField] private Transform actionTransformHolder;
    private bool repeatAction = true; // If true, the action will be performed repeatedly while the action button is held down.
                                                       // If false, the action will only be performed once per button press.
    private bool performAction = false;
    

    private void Awake()
    {
        _inventory = GetComponent<PlayerInventory>();
        _playerInputNew = GetComponent<PlayerInputNew>();
        _playerInputNew.OnAction += HandleAction;
        _playerInputNew.OnActionAlt += HandleActionAlt;
        _playerInputNew.OnActionCanceled += HandleActionCanceled;
        SetActionParameters(null);
    }

    private void Start()
    {
        _inventory.OnSelectedItemChanged += Inventory_OnSelectedItemChanged;    
    }

    private void Inventory_OnSelectedItemChanged(object sender, PlayerInventory.OnSelectedItemChangedEventArgs e)
    {
        Debug.Log("Action parameters changed");
        SetActionParameters(e.selectedItem);
    }

    private void FixedUpdate()
    {
        TryPerformAction();
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
            if (collider.gameObject == gameObject)
            {
                Debug.Log("Trying to interact with self. Continue");
                continue;
            }
            collider.transform.TryGetComponent<IDamageable>(out IDamageable damageable);
            if (damageable != null)
            {
                damageable.DamageReceived(_inventory.GetCurrentSelectedItem(), actionDamage); // Example damage amount, can be changed or made variable
                Debug.Log($"Action performed on {collider.transform.gameObject.name}");
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
        if (performAction && actionCooldownTimer <= 0)
        {
            PerformAction();
            if (!repeatAction)
            {
                performAction = false;
            }
            actionCooldownTimer = actionCooldown;
        }
    }

    public void SetActionParameters(EquippableItemSO equippableItemSO)
    {
        if (equippableItemSO != null)
        {
            actionRange = equippableItemSO.actionRange;
            actionCooldown = equippableItemSO.actionCooldown;
            repeatAction = equippableItemSO.actionRepeatability;
            actionDamage = equippableItemSO.damage;
        }
        else
        {
            actionRange = baseActionRange;
            actionCooldown = baseActionCooldown;
            repeatAction = baseRepeatAction;
            actionDamage = baseActionDamage;
        }
    }
}
