using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerActionController : MonoBehaviour
{
    public readonly struct ActionAvailability
    {
        public readonly bool IsInRange;
        public readonly bool HasCorrectTool;
        public readonly bool HasRequiredTool;
        public readonly EquippableItemType RequiredTool;
        public bool CanExecute => IsInRange && HasCorrectTool;

        public ActionAvailability(
            bool isInRange,
            bool hasCorrectTool,
            bool hasRequiredTool,
            EquippableItemType requiredTool)
        {
            IsInRange = isInRange;
            HasCorrectTool = hasCorrectTool;
            HasRequiredTool = hasRequiredTool;
            RequiredTool = requiredTool;
        }
    }

    public event EventHandler OnActionPerformed;
    public event EventHandler OnActionAltPerformed;

    private PlayerInventory _inventory;
    private PlayerInputNew _playerInputNew;
    private NetworkObject _networkObject;
    private PlayerHealth _playerHealth;
    private PlayerInteractionNew _playerInteraction;
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
        _playerInteraction = GetComponent<PlayerInteractionNew>();
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
        if (!TryGetSingleActionTarget(actionRange, out IDamageable damageable, out Collider hitCollider))
        {
            Debug.Log("No 'Action' objects in range");
            return;
        }

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
            damageable.DamageReceived(_inventory.GetCurrentSelectedItem(), actionDamage);
        }

        Debug.Log($"Action performed on {hitCollider.gameObject.name}");
        SpawnImpactEffect(hitCollider);
        OnActionPerformed?.Invoke(this, EventArgs.Empty);
    }

    private bool TryGetSingleActionTarget(float range, out IDamageable damageable, out Collider hitCollider)
    {
        damageable = null;
        hitCollider = null;

        Collider[] colliders = GetActionAreaColliders(range);
        if (colliders.Length == 0)
        {
            return false;
        }

        IDamageable aimedDamageable = null;
        if (_playerInteraction != null && _playerInteraction.CurrentTarget != null)
        {
            aimedDamageable = ResolveTargetDamageable(_playerInteraction.CurrentTarget);
        }

        if (aimedDamageable != null &&
            TryFindColliderForDamageable(colliders, aimedDamageable, out hitCollider))
        {
            damageable = aimedDamageable;
            return true;
        }

        Vector3 origin = actionTransformHolder != null ? actionTransformHolder.position : transform.position;
        Vector3 forward = actionTransformHolder != null ? actionTransformHolder.forward : transform.forward;
        float bestScore = float.PositiveInfinity;
        HashSet<IDamageable> evaluatedTargets = new HashSet<IDamageable>();

        foreach (Collider collider in colliders)
        {
            if (collider == null || collider.transform.root == transform.root)
            {
                continue;
            }

            IDamageable candidate = ResolveDamageable(collider);
            if (candidate == null || !evaluatedTargets.Add(candidate))
            {
                continue;
            }

            Collider candidateCollider = FindClosestColliderForDamageable(colliders, candidate, origin);
            if (candidateCollider == null)
            {
                continue;
            }

            Vector3 closestPoint = candidateCollider.ClosestPoint(origin);
            Vector3 toTarget = closestPoint - origin;
            float distance = toTarget.magnitude;
            float alignment = distance > 0.0001f
                ? Vector3.Dot(forward, toTarget / distance)
                : 1f;
            if (alignment <= 0f)
            {
                continue;
            }

            float score = (1f - alignment) * 10f + distance;
            if (score < bestScore)
            {
                bestScore = score;
                damageable = candidate;
                hitCollider = candidateCollider;
            }
        }

        return damageable != null && hitCollider != null;
    }

    private bool TryFindColliderForDamageable(
        Collider[] colliders,
        IDamageable expectedDamageable,
        out Collider matchingCollider)
    {
        Vector3 origin = actionTransformHolder != null ? actionTransformHolder.position : transform.position;
        matchingCollider = FindClosestColliderForDamageable(colliders, expectedDamageable, origin);
        return matchingCollider != null;
    }

    private Collider FindClosestColliderForDamageable(
        Collider[] colliders,
        IDamageable expectedDamageable,
        Vector3 origin)
    {
        Collider closestCollider = null;
        float closestDistanceSqr = float.PositiveInfinity;

        foreach (Collider collider in colliders)
        {
            if (collider == null || collider.transform.root == transform.root ||
                !ReferenceEquals(ResolveDamageable(collider), expectedDamageable))
            {
                continue;
            }

            float distanceSqr = (collider.ClosestPoint(origin) - origin).sqrMagnitude;
            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                closestCollider = collider;
            }
        }

        return closestCollider;
    }

    public bool CanPerformActionOn(MonoBehaviour target)
    {
        EquippableItemSO selectedItem = _inventory != null ? _inventory.GetItemInSlot(0) : null;
        float selectedRange = selectedItem != null ? selectedItem.actionRange : baseActionRange;
        return CanPerformActionOn(target, selectedRange, 0f);
    }

    public bool CanUseSelectedToolOn(MonoBehaviour target)
    {
        return GetActionAvailability(target).HasCorrectTool;
    }

    public ActionAvailability GetActionAvailability(MonoBehaviour target)
    {
        if (target == null)
        {
            return new ActionAvailability(false, false, false, EquippableItemType.None);
        }

        EquippableItemSO selectedItem = _inventory != null ? _inventory.GetItemInSlot(0) : null;
        bool hasRequiredTool = TryGetRequiredTool(target, out EquippableItemType requiredTool);
        bool hasCorrectTool;

        if (target is BaseResourceNew baseResource)
        {
            hasCorrectTool = selectedItem != null && baseResource.CanBeDestroyedWith(selectedItem.itemType);
        }
        else if (target is BridgeComponent bridgeComponent)
        {
            hasCorrectTool = selectedItem != null &&
                             bridgeComponent.IsMounted &&
                             !bridgeComponent.IsAssembled &&
                             bridgeComponent.NeedAssembling &&
                             bridgeComponent.SupportsEquippableItemType(selectedItem.itemType);
        }
        else if (BridgeTargetResolver.TryGetConstructionWorkTarget(
                     target,
                     out BridgeConstructionSite site,
                     out int workPointId))
        {
            hasCorrectTool = selectedItem != null &&
                             site.CanApplyToolWork(
                                 selectedItem.itemType,
                                 selectedItem.ConstructionWorkPower,
                                 workPointId);
        }
        else
        {
            float selectedDamage = selectedItem != null ? selectedItem.damage : baseActionDamage;
            hasCorrectTool = target is IDamageable && selectedDamage > 0f;
        }

        float range = selectedItem != null ? selectedItem.actionRange : baseActionRange;
        return new ActionAvailability(
            CanPerformActionOn(target, range, 0f),
            hasCorrectTool,
            hasRequiredTool,
            requiredTool);
    }

    private static bool TryGetRequiredTool(MonoBehaviour target, out EquippableItemType requiredTool)
    {
        foreach (EquippableItemType toolType in Enum.GetValues(typeof(EquippableItemType)))
        {
            if (toolType == EquippableItemType.None)
            {
                continue;
            }

            if (target is BaseResourceNew baseResource && baseResource.CanBeDestroyedWith(toolType))
            {
                requiredTool = toolType;
                return true;
            }

            if (target is BridgeComponent bridgeComponent &&
                bridgeComponent.IsMounted &&
                !bridgeComponent.IsAssembled &&
                bridgeComponent.NeedAssembling &&
                bridgeComponent.SupportsEquippableItemType(toolType))
            {
                requiredTool = toolType;
                return true;
            }

            if (BridgeTargetResolver.TryGetConstructionWorkTarget(
                    target,
                    out BridgeConstructionSite site,
                    out int workPointId) &&
                site.CanApplyToolWork(toolType, 1f, workPointId))
            {
                requiredTool = toolType;
                return true;
            }
        }

        requiredTool = EquippableItemType.None;
        return false;
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
        return BridgeTargetResolver.ResolveDamageable(target);
    }

    private static IDamageable ResolveDamageable(Collider collider)
    {
        return BridgeTargetResolver.ResolveDamageable(BridgeTargetResolver.Resolve(collider));
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
