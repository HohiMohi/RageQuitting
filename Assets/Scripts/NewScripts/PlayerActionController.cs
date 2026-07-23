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
    [SerializeField, Min(0f)] private float serverActionRangeTolerance = 0.15f;
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
        Collider[] colliders = GetActionAreaColliders(actionRange);
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
                continue;
            }

            IDamageable damageable = ResolveDamageable(collider);
            print(damageable);

            if (damageable != null && damagedObjects.Add(damageable))
            {
                if (damageable is PlayerHealth playerHealth && ShouldRequestPlayerDamageOnServer(playerHealth))
                {
                    playerHealth.DamageReceived(actionDamage, _networkObject);
                }
                else if (damageable is NPCHealth npcHealth)
                {
                    npcHealth.DamageReceived(actionDamage, _networkObject);
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

    public bool CanPerformActionOn(MonoBehaviour target)
    {
        EquippableItemSO selectedItem = _inventory != null ? _inventory.GetItemInSlot(0) : null;
        float selectedRange = selectedItem != null ? selectedItem.actionRange : baseActionRange;
        return CanPerformActionOn(target, selectedRange, 0f);
    }

    public bool CanPerformServerValidatedActionOn(MonoBehaviour target, EquippableItemSO selectedItem)
    {
        if (selectedItem == null)
        {
            return false;
        }

        return CanPerformActionOn(target, selectedItem.actionRange, serverActionRangeTolerance);
    }

    private bool CanPerformActionOn(MonoBehaviour target, float range, float tolerance)
    {
        if (target == null || actionTransformHolder == null)
        {
            return false;
        }

        IDamageable expectedDamageable = ResolveTargetDamageable(target);
        if (expectedDamageable == null)
        {
            return false;
        }

        foreach (Collider collider in GetActionAreaColliders(range + Mathf.Max(0f, tolerance)))
        {
            if (collider == null || collider.transform.root == transform.root)
            {
                continue;
            }

            if (ReferenceEquals(ResolveDamageable(collider), expectedDamageable))
            {
                return true;
            }
        }

        return false;
    }

    private Collider[] GetActionAreaColliders(float range)
    {
        if (actionTransformHolder == null)
        {
            return Array.Empty<Collider>();
        }

        float safeRange = Mathf.Max(0.01f, range);
        return Physics.OverlapBox(
            actionTransformHolder.position,
            Vector3.one * safeRange,
            actionTransformHolder.rotation);
    }

    private static IDamageable ResolveTargetDamageable(MonoBehaviour target)
    {
        BridgeDiagonalBracingWorkPoint diagonalBracingWorkPoint = target.GetComponentInParent<BridgeDiagonalBracingWorkPoint>();
        if (diagonalBracingWorkPoint != null)
        {
            return diagonalBracingWorkPoint;
        }

        BridgeCrossBeamWorkPoint crossBeamWorkPoint = target.GetComponentInParent<BridgeCrossBeamWorkPoint>();
        if (crossBeamWorkPoint != null)
        {
            return crossBeamWorkPoint;
        }

        BridgeGirderWorkPoint girderWorkPoint = target.GetComponentInParent<BridgeGirderWorkPoint>();
        if (girderWorkPoint != null)
        {
            return girderWorkPoint;
        }

        BridgeAbutmentWorkPoint workPoint = target.GetComponentInParent<BridgeAbutmentWorkPoint>();
        if (workPoint != null)
        {
            return workPoint;
        }

        BridgeConstructionSite constructionSite = target.GetComponentInParent<BridgeConstructionSite>();
        if (ConstructionSiteHandlesAction(constructionSite))
        {
            return constructionSite;
        }

        return target as IDamageable ?? target.GetComponent<IDamageable>() ?? target.GetComponentInParent<IDamageable>();
    }

    private static IDamageable ResolveDamageable(Collider collider)
    {
        if (collider == null)
        {
            return null;
        }

        BridgeDiagonalBracingWorkPoint diagonalBracingWorkPoint = collider.GetComponentInParent<BridgeDiagonalBracingWorkPoint>();
        if (diagonalBracingWorkPoint != null)
        {
            return diagonalBracingWorkPoint;
        }

        BridgeCrossBeamWorkPoint crossBeamWorkPoint = collider.GetComponentInParent<BridgeCrossBeamWorkPoint>();
        if (crossBeamWorkPoint != null)
        {
            return crossBeamWorkPoint;
        }

        BridgeGirderWorkPoint girderWorkPoint = collider.GetComponentInParent<BridgeGirderWorkPoint>();
        if (girderWorkPoint != null)
        {
            return girderWorkPoint;
        }

        BridgeAbutmentWorkPoint workPoint = collider.GetComponentInParent<BridgeAbutmentWorkPoint>();
        if (workPoint != null)
        {
            return workPoint;
        }

        BridgeConstructionSite constructionSite = collider.GetComponentInParent<BridgeConstructionSite>();
        if (ConstructionSiteHandlesAction(constructionSite))
        {
            return constructionSite;
        }

        return collider.GetComponent<IDamageable>() ?? collider.GetComponentInParent<IDamageable>();
    }

    private static bool ConstructionSiteHandlesAction(BridgeConstructionSite constructionSite)
    {
        return constructionSite != null &&
               (constructionSite.CurrentStage == BridgeConstructionStage.Clearing ||
                constructionSite.CurrentStage == BridgeConstructionStage.Digging);
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
