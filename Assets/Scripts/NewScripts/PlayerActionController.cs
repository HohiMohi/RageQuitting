using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerActionController : MonoBehaviour
{
    public event EventHandler OnActionPerformed;
    public event EventHandler OnActionAltPerformed;

    private PlayerInventory _inventory;
    private PlayerInputNew _playerInputNew;
    private NetworkObject _networkObject;
    private PlayerHealth _playerHealth;
    private ActionImpactEffectSpawner _impactEffectSpawner;
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
        _networkObject = GetComponent<NetworkObject>();
        _playerHealth = GetComponent<PlayerHealth>();
        _impactEffectSpawner = GetComponent<ActionImpactEffectSpawner>();
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
        if (IsDowned())
        {
            return;
        }

        Debug.Log("Action Alt");
        OnActionAltPerformed?.Invoke(this, EventArgs.Empty);
    }

    private void HandleAction(object sender, EventArgs e)
    {
        if (IsDowned())
        {
            performAction = false;
            return;
        }

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

        HashSet<IDamageable> damagedObjects = new HashSet<IDamageable>();
        bool actionPerformed = false;
        foreach (Collider collider in colliders)
        {
            if (collider.transform.root == transform.root)
            {
                Debug.Log("Trying to interact with self. Continue");
                continue;
            }

            IDamageable damageable = collider.GetComponent<IDamageable>();
            damageable ??= collider.GetComponentInParent<IDamageable>();
            print(damageable);

            if (damageable != null && damagedObjects.Add(damageable))
            {
                if (damageable is PlayerHealth playerHealth && ShouldRequestPlayerDamageOnServer(playerHealth))
                {
                    playerHealth.DamageReceived(actionDamage, _networkObject);
                }
                else
                {
                    damageable.DamageReceived(_inventory.GetCurrentSelectedItem(), actionDamage); // Example damage amount, can be changed or made variable
                }

                Debug.Log($"Action performed on {collider.transform.gameObject.name}");
                SpawnImpactEffect(collider);
                actionPerformed = true;
            }
            else if (damageable == null)
            {
                Debug.Log($"Collider {collider.gameObject.name} is in range but does not implement IDamageableNew");
            }
        }

        if (actionPerformed)
        {
            OnActionPerformed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SpawnImpactEffect(Collider hitCollider)
    {
        if (_impactEffectSpawner == null || hitCollider == null || actionTransformHolder == null)
        {
            return;
        }

        Vector3 impactPoint = hitCollider.ClosestPoint(actionTransformHolder.position);
        Vector3 impactNormal = actionTransformHolder.position - impactPoint;
        if (impactNormal.sqrMagnitude < 0.0001f)
        {
            impactNormal = -transform.forward;
        }

        _impactEffectSpawner.SpawnImpact(impactPoint, impactNormal.normalized);
    }

    private bool ShouldRequestPlayerDamageOnServer(PlayerHealth playerHealth)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer)
        {
            return false;
        }

        if (!playerHealth.TryGetComponent(out NetworkObject targetNetworkObject))
        {
            return false;
        }

        return _networkObject != null && targetNetworkObject != _networkObject;
    }

    public void TryPerformAction()
    {
        if (IsDowned())
        {
            performAction = false;
            return;
        }

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

    private bool IsDowned()
    {
        return _playerHealth != null && _playerHealth.IsDowned;
    }
}
