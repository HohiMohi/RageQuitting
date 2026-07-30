using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerActionController : MonoBehaviour
{
    public sealed class ToolActionEventArgs : EventArgs
    {
        public EquippableItemSO Item;
        public EquippableActionProfileSO Profile;
        public EquippableActionPhase Phase;
        public bool Hit;
        public ActionImpactSurfaceType SurfaceType;
    }

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
    public event EventHandler<ToolActionEventArgs> OnToolActionStarted;
    public event EventHandler<ToolActionEventArgs> OnToolActionImpact;
    public event EventHandler<ToolActionEventArgs> OnToolActionEnded;

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
    private EquippableActionPhase currentActionPhase;
    private float currentActionPhaseTimer;
    private float currentActionPhaseDuration;
    private EquippableItemSO actionItem;

    public bool IsActionInProgress => currentActionPhase != EquippableActionPhase.None;
    public EquippableActionPhase CurrentActionPhase => currentActionPhase;
    public float CurrentActionPhaseNormalized => currentActionPhaseDuration > 0f
        ? Mathf.Clamp01(1f - currentActionPhaseTimer / currentActionPhaseDuration)
        : 1f;
    public float ActionMovementMultiplier => IsActionInProgress && actionItem != null && actionItem.actionProfile != null
        ? Mathf.Clamp01(actionItem.actionProfile.movementMultiplierDuringAction)
        : 1f;
    public EquippableItemSO CurrentActionItem => actionItem;
    

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

    private void OnDisable()
    {
        performAction = false;
        CancelCurrentAction();
    }

    private void Inventory_OnSelectedItemChanged(object sender, PlayerInventory.OnSelectedItemChangedEventArgs e)
    {
        Debug.Log("Action parameters changed");
        CancelCurrentAction();
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

    public bool PerformAction()
    {
        if (!TryGetSingleActionTarget(actionRange, out IDamageable damageable, out Collider hitCollider))
        {
            Debug.Log("No 'Action' objects in range");
            return false;
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
        ActionImpactSurfaceType surfaceType = ResolveImpactSurface(damageable, hitCollider);
        SpawnImpactEffect(hitCollider, surfaceType);
        OnActionPerformed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool TryGetSingleActionTarget(float range, out IDamageable damageable, out Collider hitCollider)
    {
        damageable = null;
        hitCollider = null;

        if (_playerInteraction == null || _playerInteraction.CurrentTarget == null)
        {
            return false;
        }

        Collider[] colliders = GetActionAreaColliders(range);
        IDamageable aimedDamageable = ResolveTargetDamageable(_playerInteraction.CurrentTarget);
        if (aimedDamageable == null ||
            !TryFindColliderForDamageable(colliders, aimedDamageable, out hitCollider))
        {
            return false;
        }

        damageable = aimedDamageable;
        return true;
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

    private void SpawnImpactEffect(Collider hitCollider, ActionImpactSurfaceType surfaceType)
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

        float feedbackStrength = actionItem != null && actionItem.actionProfile != null
            ? actionItem.actionProfile.impactFeedbackStrength
            : 1f;
        _impactEffectSpawner.SpawnImpact(impactPoint, impactNormal.normalized, surfaceType, feedbackStrength);
    }

    private ActionImpactSurfaceType ResolveImpactSurface(IDamageable damageable, Collider hitCollider)
    {
        if (damageable is IActionImpactSurfaceProvider provider)
        {
            return provider.ImpactSurfaceType;
        }

        if (damageable is PlayerHealth || damageable is NPCHealth)
        {
            return ActionImpactSurfaceType.Flesh;
        }

        if (damageable is BaseResourceNew resource && resource.GetBaseResourceSO() != null)
        {
            return resource.GetBaseResourceSO().impactSurfaceType;
        }

        EquippableItemSO selectedItem = actionItem != null
            ? actionItem
            : _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        if (selectedItem != null)
        {
            if (selectedItem.itemType == EquippableItemType.Shovel)
            {
                return ActionImpactSurfaceType.Soil;
            }

            if (selectedItem.itemType == EquippableItemType.Wrench)
            {
                return ActionImpactSurfaceType.Metal;
            }

            if (selectedItem.itemType == EquippableItemType.IndustrialHammer)
            {
                return ActionImpactSurfaceType.Wood;
            }
        }

        IActionImpactSurfaceProvider colliderProvider = hitCollider != null
            ? hitCollider.GetComponentInParent<IActionImpactSurfaceProvider>()
            : null;
        return colliderProvider != null ? colliderProvider.ImpactSurfaceType : ActionImpactSurfaceType.Default;
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
        if (IsDowned() || !HasLocalActionAuthority() || _playerInputNew != null && _playerInputNew.IsGameplayUiOpen)
        {
            performAction = false;
            CancelCurrentAction();
            return;
        }

        if (IsActionInProgress)
        {
            UpdateProfiledAction();
            return;
        }

        EquippableItemSO selectedItem = _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        if (performAction && selectedItem != null && selectedItem.actionProfile != null)
        {
            StartProfiledAction(selectedItem);
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

    public void CancelCurrentAction()
    {
        if (!IsActionInProgress)
        {
            return;
        }

        EquippableItemSO canceledItem = actionItem;
        EquippableActionProfileSO canceledProfile = canceledItem != null ? canceledItem.actionProfile : null;
        currentActionPhase = EquippableActionPhase.None;
        currentActionPhaseTimer = 0f;
        currentActionPhaseDuration = 0f;
        actionItem = null;
        OnToolActionEnded?.Invoke(this, CreateToolEventArgs(
            canceledItem,
            canceledProfile,
            EquippableActionPhase.None,
            false,
            ActionImpactSurfaceType.Default));
    }

    private void StartProfiledAction(EquippableItemSO selectedItem)
    {
        actionItem = selectedItem;
        if (!selectedItem.actionRepeatability)
        {
            performAction = false;
        }

        SetProfiledActionPhase(EquippableActionPhase.WindUp);
        OnToolActionStarted?.Invoke(this, CreateToolEventArgs(
            selectedItem,
            selectedItem.actionProfile,
            currentActionPhase,
            false,
            ActionImpactSurfaceType.Default));
    }

    private void UpdateProfiledAction()
    {
        if (actionItem == null
            || actionItem.actionProfile == null
            || _inventory == null
            || _inventory.GetCurrentSelectedItem() != actionItem)
        {
            CancelCurrentAction();
            return;
        }

        currentActionPhaseTimer -= Time.deltaTime;
        int transitionGuard = 0;
        while (currentActionPhaseTimer <= 0f && IsActionInProgress && transitionGuard++ < 5)
        {
            switch (currentActionPhase)
            {
                case EquippableActionPhase.WindUp:
                    SetProfiledActionPhase(EquippableActionPhase.Strike);
                    break;
                case EquippableActionPhase.Strike:
                    ResolveProfiledImpact();
                    SetProfiledActionPhase(EquippableActionPhase.ImpactFreeze);
                    break;
                case EquippableActionPhase.ImpactFreeze:
                    SetProfiledActionPhase(EquippableActionPhase.Recovery);
                    break;
                case EquippableActionPhase.Recovery:
                    CompleteProfiledAction();
                    break;
            }
        }
    }

    private void ResolveProfiledImpact()
    {
        EquippableItemSO impactItem = actionItem;
        bool hit = false;
        ActionImpactSurfaceType surfaceType = ActionImpactSurfaceType.Default;
        if (TryGetSingleActionTarget(actionRange, out IDamageable damageable, out Collider hitCollider))
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
                damageable.DamageReceived(impactItem, actionDamage);
            }

            surfaceType = ResolveImpactSurface(damageable, hitCollider);
            SpawnImpactEffect(hitCollider, surfaceType);
            OnActionPerformed?.Invoke(this, EventArgs.Empty);
            hit = true;
        }

        OnToolActionImpact?.Invoke(this, CreateToolEventArgs(
            impactItem,
            impactItem != null ? impactItem.actionProfile : null,
            EquippableActionPhase.ImpactFreeze,
            hit,
            surfaceType));
    }

    private void CompleteProfiledAction()
    {
        EquippableItemSO completedItem = actionItem;
        EquippableActionProfileSO completedProfile = completedItem != null ? completedItem.actionProfile : null;
        currentActionPhase = EquippableActionPhase.None;
        currentActionPhaseTimer = 0f;
        currentActionPhaseDuration = 0f;
        actionItem = null;
        OnToolActionEnded?.Invoke(this, CreateToolEventArgs(
            completedItem,
            completedProfile,
            EquippableActionPhase.None,
            false,
            ActionImpactSurfaceType.Default));
    }

    private void SetProfiledActionPhase(EquippableActionPhase phase)
    {
        currentActionPhase = phase;
        currentActionPhaseDuration = actionItem != null && actionItem.actionProfile != null
            ? actionItem.actionProfile.GetPhaseDuration(phase)
            : 0f;
        currentActionPhaseTimer = currentActionPhaseDuration;
    }

    private bool HasLocalActionAuthority()
    {
        return _networkObject == null
            || NetworkManager.Singleton == null
            || !NetworkManager.Singleton.IsListening
            || _networkObject.IsOwner;
    }

    private static ToolActionEventArgs CreateToolEventArgs(
        EquippableItemSO item,
        EquippableActionProfileSO profile,
        EquippableActionPhase phase,
        bool hit,
        ActionImpactSurfaceType surfaceType)
    {
        return new ToolActionEventArgs
        {
            Item = item,
            Profile = profile,
            Phase = phase,
            Hit = hit,
            SurfaceType = surfaceType
        };
    }

    private bool IsDowned()
    {
        return _playerHealth != null && _playerHealth.IsDowned;
    }
}
