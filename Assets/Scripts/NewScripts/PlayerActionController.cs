using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public partial class PlayerActionController : NetworkBehaviour
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
    private PlayerConcreteTrapController _concreteTrap;
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
    [SerializeField, Min(0f)] private float serverCombatImpactTimingTolerance = 0.1f;
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
    private double nextServerCombatImpactTime;
    private uint submittedMountingNailImpactSequence;
    private uint lastServerMountingNailImpactSequence;

    public bool IsActionInProgress => currentActionPhase != EquippableActionPhase.None;
    public EquippableActionPhase CurrentActionPhase => currentActionPhase;
    public float CurrentActionPhaseNormalized => currentActionPhaseDuration > 0f
        ? Mathf.Clamp01(1f - currentActionPhaseTimer / currentActionPhaseDuration)
        : 1f;
    public float ActionMovementMultiplier => IsCrowbarExtractionActiveOrPending
        ? GetCrowbarExtractionMovementMultiplier()
        : IsActionInProgress && actionItem != null && actionItem.actionProfile != null
            ? Mathf.Clamp01(actionItem.actionProfile.movementMultiplierDuringAction)
            : 1f;
    public bool IsCrowbarExtractionActiveOrPending => IsCrowbarExtractionHeldOrPending;
    public EquippableItemSO CurrentActionItem => actionItem;
    

    private void Awake()
    {
        TryGetComponent(out _inventory);
        _playerInputNew = GetComponent<PlayerInputNew>();
        _networkObject = GetComponent<NetworkObject>();
        _playerHealth = GetComponent<PlayerHealth>();
        _playerInteraction = GetComponent<PlayerInteractionNew>();
        _concreteTrap = GetComponent<PlayerConcreteTrapController>();
        _impactEffectSpawner = GetComponent<ActionImpactEffectSpawner>();
        _playerInputNew.OnAction += HandleAction;
        _playerInputNew.OnActionAlt += HandleActionAlt;
        _playerInputNew.OnActionAltCanceled += HandleActionAltCanceled;
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
        CancelCrowbarExtractionImmediately();
        CancelCurrentAction();
    }

    public override void OnDestroy()
    {
        if (_playerInputNew != null)
        {
            _playerInputNew.OnAction -= HandleAction;
            _playerInputNew.OnActionAlt -= HandleActionAlt;
            _playerInputNew.OnActionAltCanceled -= HandleActionAltCanceled;
            _playerInputNew.OnActionCanceled -= HandleActionCanceled;
        }
        if (_inventory != null) _inventory.OnSelectedItemChanged -= Inventory_OnSelectedItemChanged;
        CancelCrowbarExtractionImmediately();
        base.OnDestroy();
    }

    private void Inventory_OnSelectedItemChanged(object sender, PlayerInventory.OnSelectedItemChangedEventArgs e)
    {
        Debug.Log("Action parameters changed");
        CancelCrowbarExtractionImmediately();
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
        if (IsCrowbarSelected())
        {
            BeginCrowbarExtractionOnFreshPress();
            return;
        }
        if (_playerInteraction != null && _playerInteraction.HasContextActionConsumer)
        {
            performAction = false;
            CancelCurrentAction();
            return;
        }
        if (_concreteTrap != null && _concreteTrap.BlocksGameplayInput) return;
        if (IsSpecialInputToolSelected())
        {
            return;
        }

        if (IsDowned())
        {
            return;
        }

        Debug.Log("Action Alt");
        OnActionAltPerformed?.Invoke(this, EventArgs.Empty);
    }

    private void HandleAction(object sender, EventArgs e)
    {
        if (IsCrowbarExtractionActiveOrPending) return;
        if (_playerInteraction != null && _playerInteraction.TryConsumeContextAction(ContextActionKind.Primary))
        {
            performAction = false;
            CancelCurrentAction();
            return;
        }
        if (_concreteTrap != null && _concreteTrap.BlocksGameplayInput)
        {
            performAction = false;
            CancelCurrentAction();
            return;
        }
        if (IsSpecialInputToolSelected())
        {
            performAction = false;
            return;
        }

        if (IsDowned())
        {
            performAction = false;
            return;
        }

        performAction = true;
    }

    public bool PerformAction()
    {
        if (IsCrowbarExtractionActiveOrPending) return false;
        EquippableItemSO selectedItem = _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        if (TryConsumeAimedMountingNail(selectedItem, out Collider nailCollider, out ActionImpactSurfaceType nailSurface))
        {
            SpawnImpactEffect(nailCollider, nailSurface);
            OnActionPerformed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (!TryGetSingleActionTarget(actionRange, out IDamageable damageable, out Collider hitCollider))
        {
            Debug.Log("No 'Action' objects in range");
            return false;
        }

        if (IsCombatTarget(damageable))
        {
            if (!TrySubmitCombatImpact(damageable, selectedItem))
            {
                return false;
            }
        }
        else
        {
            damageable.DamageReceived(selectedItem, actionDamage);
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
        MountingNail mountingNail = ResolveMountingNail(target);
        if (mountingNail != null && (mountingNail.State == MountingNailState.Prepared || mountingNail.BlocksPickup))
        {
            bool required = mountingNail.State == MountingNailState.Prepared || mountingNail.State == MountingNailState.Partial;
            EquippableItemType requiredItem = required ? EquippableItemType.IndustrialHammer : EquippableItemType.None;
            bool correct = required && selectedItem != null && selectedItem.itemType == EquippableItemType.IndustrialHammer;
            float nailRange = selectedItem != null ? selectedItem.actionRange : baseActionRange;
            return new ActionAvailability(
                CanPerformActionOn(target, nailRange, 0f), correct, required, requiredItem);
        }

        bool hasRequiredTool = TryGetRequiredTool(target, out EquippableItemType requiredTool);
        bool hasCorrectTool;

        if (target is PlayerConcreteTrapTarget concreteTrapTarget)
        {
            hasCorrectTool = concreteTrapTarget.CanAcceptBreakInteraction && selectedItem != null &&
                selectedItem.itemType == concreteTrapTarget.RequiredTool;
        }
        else if (target is BaseResourceNew baseResource)
        {
            hasCorrectTool = selectedItem != null && baseResource.CanBeDestroyedWith(selectedItem.itemType);
        }
        else if (target is FlexibleSaplingController sapling)
        {
            hasCorrectTool = selectedItem != null && sapling.CanApplyTool(selectedItem.itemType);
        }
        else if (target is BridgeComponent bridgeComponent)
        {
            hasCorrectTool = selectedItem != null &&
                             bridgeComponent.IsMounted &&
                             !bridgeComponent.IsAssembled &&
                             bridgeComponent.NeedAssembling &&
                             (bridgeComponent.ConstructionSite == null ||
                              bridgeComponent.ConstructionSite.AllowsStandardAssemblyWork) &&
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
        if (target is PlayerConcreteTrapTarget concreteTrapTarget)
        {
            requiredTool = concreteTrapTarget.RequiredTool;
            return true;
        }

        if (target is FlexibleSaplingController sapling && sapling.CanApplyTool(EquippableItemType.Shovel))
        {
            requiredTool = EquippableItemType.Shovel;
            return true;
        }

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

        MountingNail mountingNail = ResolveMountingNail(target);
        if (mountingNail != null)
        {
            return TryFindAimedMountingNail(range + Mathf.Max(0f, tolerance), out MountingNail aimedNail,
                out _, out _) && aimedNail == mountingNail;
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

    private bool TryFindAimedMountingNail(float range, out MountingNail nail, out Collider collider, out Ray aimRay)
    {
        nail = null;
        collider = null;
        aimRay = default;
        return _playerInteraction != null && _playerInteraction.TryGetAimRay(out aimRay)
            && MountingNailAimResolver.TryFindCandidate(aimRay, range, transform, out nail, out collider, out _);
    }

    private static MountingNail ResolveMountingNail(MonoBehaviour target)
    {
        MountingNail directNail = target as MountingNail;
        if (directNail != null) return directNail;
        return target != null ? target.GetComponentInParent<MountingNail>() : null;
    }

    private bool TryConsumeAimedMountingNail(EquippableItemSO selectedItem, out Collider hitCollider,
        out ActionImpactSurfaceType surfaceType)
    {
        hitCollider = null;
        surfaceType = ActionImpactSurfaceType.Default;
        float range = selectedItem != null ? selectedItem.actionRange : baseActionRange;
        Ray aimRay = default;
        bool hasAimRay = _playerInteraction != null && _playerInteraction.TryGetAimRay(out aimRay);
        if (hasAimRay && MountingNailAimResolver.TryFindCandidate(aimRay, range, transform,
                out MountingNail nail, out hitCollider, out _))
        {
            // Nails retain priority over every panel or surface candidate, including on rejection.
            surfaceType = ActionImpactSurfaceType.Metal;
            if (selectedItem != null && selectedItem.itemType == EquippableItemType.IndustrialHammer)
            {
                if (!IsOwnNetworkSessionActive())
                    nail.TryApplyHammerImpact(selectedItem.itemType, transform.position, transform.forward);
                else if (nail.NetworkObject != null && nail.NetworkObject.IsSpawned)
                {
                    uint sequence = NextSubmittedMountingNailImpactSequence();
                    if (IsServer)
                        TryApplyMountingNailImpactServer(OwnerClientId, nail.NetworkObject, aimRay.origin,
                            aimRay.direction, sequence);
                    else
                        RequestMountingNailImpactServerRpc(nail.NetworkObject.NetworkObjectId,
                            aimRay.origin, aimRay.direction, sequence);
                }
            }
            return true;
        }

        if (!hasAimRay || !BridgeDeckPanelHammerAimResolver.TryFindVisiblePanel(aimRay, range, transform,
                out BridgeDeckPanelMounting panelMarker, out hitCollider)) return false;

        surfaceType = ActionImpactSurfaceType.Wood;
        if (selectedItem != null && selectedItem.itemType == EquippableItemType.IndustrialHammer)
        {
            if (!IsOwnNetworkSessionActive())
                BridgeDeckPanelSectionNetworkState.TryResetPanelWithHammer(panelMarker,
                    GetPanelResetAwayDirection(panelMarker));
            else if (panelMarker.TryGetComponent(out NetworkObject panelNetworkObject) && panelNetworkObject.IsSpawned)
            {
                uint sequence = NextSubmittedMountingNailImpactSequence();
                if (IsServer)
                {
                    TryApplyPanelHammerImpactServer(OwnerClientId, panelNetworkObject, aimRay.origin,
                        aimRay.direction, sequence);
                }
                else
                {
                    RequestPanelHammerImpactServerRpc(panelNetworkObject.NetworkObjectId,
                        aimRay.origin, aimRay.direction, sequence);
                }
            }
        }
        return true;
    }

    private uint NextSubmittedMountingNailImpactSequence()
    {
        uint sequence = unchecked(++submittedMountingNailImpactSequence);
        if (sequence == 0) sequence = unchecked(++submittedMountingNailImpactSequence);
        return sequence;
    }

    private bool IsOwnNetworkSessionActive() => IsSpawned && NetworkManager != null && NetworkManager.IsListening;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            lastServerMountingNailImpactSequence = 0;
            nextServerCombatImpactTime = 0d;
        }
        submittedMountingNailImpactSequence = 0;
        ResetCrowbarExtractionNetworkState();
    }

    public override void OnNetworkDespawn()
    {
        HandleCrowbarExtractionNetworkDespawn();
    }

    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

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

    private static bool IsCombatTarget(IDamageable damageable)
    {
        return damageable is PlayerHealth || damageable is NPCHealth;
    }

    private bool TrySubmitCombatImpact(IDamageable damageable, EquippableItemSO item)
    {
        if (damageable is not MonoBehaviour targetBehaviour)
        {
            return false;
        }

        NetworkObject targetNetworkObject = targetBehaviour.GetComponentInParent<NetworkObject>();
        if (IsNetworkSessionActive())
        {
            if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
            {
                return false;
            }

            if (IsServer)
            {
                return TryApplyCombatImpactServer(
                    targetNetworkObject,
                    item != null ? item.itemType : EquippableItemType.None);
            }

            RequestCombatImpactServerRpc(
                targetNetworkObject.NetworkObjectId,
                item != null ? (int)item.itemType : (int)EquippableItemType.None);
            return true;
        }

        return ApplyCombatDamageAndImpulse(
            targetBehaviour,
            item != null ? item.damage : baseActionDamage,
            item != null ? item.impactImpulseProfile : null);
    }

    [ServerRpc]
    private void RequestCombatImpactServerRpc(
        ulong targetNetworkObjectId,
        int requestedItemTypeValue,
        ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId ||
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                targetNetworkObjectId,
                out NetworkObject targetNetworkObject))
        {
            return;
        }

        TryApplyCombatImpactServer(targetNetworkObject, (EquippableItemType)requestedItemTypeValue);
    }

    [ServerRpc]
    private void RequestMountingNailImpactServerRpc(ulong nailNetworkObjectId, Vector3 aimOrigin,
        Vector3 aimDirection, uint sequence, ServerRpcParams serverRpcParams = default)
    {
        NetworkManager manager = NetworkManager;
        if (manager == null || !manager.IsServer) return;
        manager.SpawnManager.SpawnedObjects.TryGetValue(nailNetworkObjectId, out NetworkObject nailNetworkObject);
        TryApplyMountingNailImpactServer(serverRpcParams.Receive.SenderClientId, nailNetworkObject,
            aimOrigin, aimDirection, sequence);
    }

    [ServerRpc]
    private void RequestPanelHammerImpactServerRpc(ulong panelNetworkObjectId, Vector3 aimOrigin,
        Vector3 aimDirection, uint sequence, ServerRpcParams serverRpcParams = default)
    {
        NetworkManager manager = NetworkManager;
        if (manager == null || !manager.IsServer) return;
        manager.SpawnManager.SpawnedObjects.TryGetValue(panelNetworkObjectId, out NetworkObject panelNetworkObject);
        TryApplyPanelHammerImpactServer(serverRpcParams.Receive.SenderClientId, panelNetworkObject,
            aimOrigin, aimDirection, sequence);
    }

    private bool TryApplyPanelHammerImpactServer(ulong senderClientId, NetworkObject panelNetworkObject,
        Vector3 aimOrigin, Vector3 aimDirection, uint sequence)
    {
        NetworkManager manager = NetworkManager;
        if (manager == null || !manager.IsServer || senderClientId != OwnerClientId
            || sequence == 0 || !IsNewerRequestSequence(sequence, lastServerMountingNailImpactSequence)) return false;
        lastServerMountingNailImpactSequence = sequence;
        if (panelNetworkObject == null || !panelNetworkObject.IsSpawned || panelNetworkObject == NetworkObject
            || panelNetworkObject.NetworkManager != manager || _inventory == null || _playerInteraction == null
            || _concreteTrap != null && _concreteTrap.IsTrapped || IsDowned()
            || serverCrowbarNail != null
            || !IsFinite(aimOrigin) || !IsFinite(aimDirection)) return false;
        float directionLength = aimDirection.magnitude;
        if (directionLength < 0.95f || directionLength > 1.05f
            || !IsTrustedMountingNailAimOrigin(aimOrigin, aimDirection / directionLength)) return false;
        aimDirection /= directionLength;

        EquippableItemSO serverItem = _inventory.GetSelectedItemForServerValidation();
        if (serverItem == null || serverItem.itemType != EquippableItemType.IndustrialHammer
            || !panelNetworkObject.TryGetComponent(out BridgeDeckPanelMounting marker)) return false;
        Ray aimRay = new(aimOrigin, aimDirection);
        if (MountingNailAimResolver.TryFindCandidate(aimRay, serverItem.actionRange, transform,
                out _, out _, out _)) return false;
        if (!BridgeDeckPanelHammerAimResolver.TryFindVisiblePanel(aimRay, serverItem.actionRange, transform,
                out BridgeDeckPanelMounting aimedMarker, out _) || aimedMarker != marker) return false;

        double serverTime = manager.ServerTime.Time;
        if (serverTime < nextServerCombatImpactTime) return false;
        if (!BridgeDeckPanelSectionNetworkState.TryResetPanelWithHammer(marker,
                GetPanelResetAwayDirection(marker))) return false;
        nextServerCombatImpactTime = serverTime + GetMinimumCombatImpactInterval(serverItem);
        return true;
    }

    private Vector3 GetPanelResetAwayDirection(BridgeDeckPanelMounting marker)
    {
        Vector3 awayDirection = marker.transform.position - transform.position;
        awayDirection.y = 0f;
        if (awayDirection.sqrMagnitude < 0.000001f)
        {
            awayDirection = transform.forward;
            awayDirection.y = 0f;
        }
        if (awayDirection.sqrMagnitude < 0.000001f) awayDirection = Vector3.forward;
        return awayDirection.normalized;
    }

    private bool TryApplyMountingNailImpactServer(ulong senderClientId, NetworkObject nailNetworkObject,
        Vector3 aimOrigin, Vector3 aimDirection, uint sequence)
    {
        NetworkManager manager = NetworkManager;
        if (manager == null || !manager.IsServer || senderClientId != OwnerClientId
            || sequence == 0 || !IsNewerRequestSequence(sequence, lastServerMountingNailImpactSequence)) return false;
        lastServerMountingNailImpactSequence = sequence;
        if (nailNetworkObject == null || !nailNetworkObject.IsSpawned || nailNetworkObject == NetworkObject
            || _inventory == null || _playerInteraction == null
            || _concreteTrap != null && _concreteTrap.IsTrapped || IsDowned()
            || serverCrowbarNail != null
            || !IsFinite(aimOrigin) || !IsFinite(aimDirection)) return false;
        float directionLength = aimDirection.magnitude;
        if (directionLength < 0.95f || directionLength > 1.05f
            || !IsTrustedMountingNailAimOrigin(aimOrigin, aimDirection / directionLength)) return false;
        aimDirection /= directionLength;

        EquippableItemSO serverItem = _inventory.GetSelectedItemForServerValidation();
        if (serverItem == null || serverItem.itemType != EquippableItemType.IndustrialHammer
            || nailNetworkObject.GetComponent<MountingNail>() is not MountingNail nail)
            return false;

        Ray aimRay = new(aimOrigin, aimDirection);
        if (!MountingNailAimResolver.TryFindCandidate(aimRay, serverItem.actionRange, transform,
                out MountingNail aimedNail, out _, out _) || aimedNail != nail)
            return false;

        double serverTime = manager.ServerTime.Time;
        if (serverTime < nextServerCombatImpactTime
            || !nail.TryApplyHammerImpact(EquippableItemType.IndustrialHammer, transform.position, transform.forward))
            return false;

        nextServerCombatImpactTime = serverTime + GetMinimumCombatImpactInterval(serverItem);
        return true;
    }

    private static bool IsNewerRequestSequence(uint candidate, uint previous)
    {
        return candidate != previous && unchecked((int)(candidate - previous)) > 0;
    }

    private bool IsTrustedMountingNailAimOrigin(Vector3 aimOrigin, Vector3 aimDirection)
    {
        if (_playerInteraction == null || NetworkObject == null || !NetworkObject.IsSpawned
            || !NetworkObject.TryGetComponent(out NetworkTransform playerTransform)) return false;
        Transform anchor = _playerInteraction.SingleCarryPlacementAimAnchor;
        if (anchor == null) return false;

        Vector3 anchorLocalOffset = NetworkObject.transform.InverseTransformPoint(anchor.position);
        Vector3 authoritativePosition = playerTransform.GetSpaceRelativePosition(true);
        Quaternion authoritativeRotation = playerTransform.GetSpaceRelativeRotation(true);
        if (playerTransform.InLocalSpace && NetworkObject.transform.parent != null)
        {
            authoritativePosition = NetworkObject.transform.parent.TransformPoint(authoritativePosition);
            authoritativeRotation = NetworkObject.transform.parent.rotation * authoritativeRotation;
        }
        Vector3 trustedAnchor = authoritativePosition
            + authoritativeRotation * Vector3.Scale(anchorLocalOffset, NetworkObject.transform.localScale);

        Vector3 offset = aimOrigin - trustedAnchor;
        float forwardOffset = Vector3.Dot(offset, aimDirection);
        Vector3 lateralOffset = offset - aimDirection * forwardOffset;
        float allowedForwardOffset = Mathf.Clamp(forwardOffset, 0f, 0.2f);
        float residualSqr = lateralOffset.sqrMagnitude + Mathf.Pow(forwardOffset - allowedForwardOffset, 2f);
        return residualSqr <= 0.15f * 0.15f;
    }

    private bool TryApplyCombatImpactServer(
        NetworkObject targetNetworkObject,
        EquippableItemType requestedItemType)
    {
        if (targetNetworkObject == null ||
            targetNetworkObject == NetworkObject ||
            _inventory == null || _concreteTrap != null && _concreteTrap.IsTrapped
            || serverCrowbarNail != null)
        {
            return false;
        }

        EquippableItemSO serverItem = _inventory.GetSelectedItemForServerValidation();
        bool usesBaseAction = requestedItemType == EquippableItemType.None && serverItem == null;
        if ((!usesBaseAction && (serverItem == null || serverItem.itemType != requestedItemType)) ||
            !TryResolveCombatTarget(targetNetworkObject, out MonoBehaviour targetBehaviour))
        {
            return false;
        }

        bool isInRange = usesBaseAction
            ? CanPerformActionOn(targetBehaviour, baseActionRange, serverActionRangeTolerance)
            : CanPerformServerValidatedActionOn(targetBehaviour, serverItem);
        float validatedDamage = usesBaseAction ? baseActionDamage : serverItem.damage;
        double serverTime = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.ServerTime.Time
            : Time.timeAsDouble;
        if (!isInRange || validatedDamage <= 0f || serverTime < nextServerCombatImpactTime)
        {
            return false;
        }

        bool applied = ApplyCombatDamageAndImpulse(
            targetBehaviour,
            validatedDamage,
            serverItem != null ? serverItem.impactImpulseProfile : null);
        if (applied)
        {
            nextServerCombatImpactTime = serverTime + GetMinimumCombatImpactInterval(serverItem);
        }

        return applied;
    }

    private float GetMinimumCombatImpactInterval(EquippableItemSO item)
    {
        if (item == null || item.actionProfile == null)
        {
            return Mathf.Max(0.05f, item != null ? item.actionCooldown : baseActionCooldown);
        }

        float cycleDuration =
            item.actionProfile.GetPhaseDuration(EquippableActionPhase.WindUp) +
            item.actionProfile.GetPhaseDuration(EquippableActionPhase.Strike) +
            item.actionProfile.GetPhaseDuration(EquippableActionPhase.ImpactFreeze) +
            item.actionProfile.GetPhaseDuration(EquippableActionPhase.Recovery);
        return Mathf.Max(0.05f, cycleDuration - serverCombatImpactTimingTolerance);
    }

    private static bool TryResolveCombatTarget(
        NetworkObject targetNetworkObject,
        out MonoBehaviour targetBehaviour)
    {
        targetBehaviour = null;
        if (targetNetworkObject == null)
        {
            return false;
        }

        PlayerHealth playerHealth = targetNetworkObject.GetComponentInChildren<PlayerHealth>();
        if (playerHealth != null)
        {
            if (playerHealth.IsDowned)
            {
                return false;
            }

            targetBehaviour = playerHealth;
            return true;
        }

        NPCHealth npcHealth = targetNetworkObject.GetComponentInChildren<NPCHealth>();
        if (npcHealth == null || npcHealth.IsDead)
        {
            return false;
        }

        targetBehaviour = npcHealth;
        return true;
    }

    private bool ApplyCombatDamageAndImpulse(
        MonoBehaviour targetBehaviour,
        float damage,
        ExternalImpulseProfileSO impulseProfile)
    {
        if (targetBehaviour == null || damage <= 0f)
        {
            return false;
        }

        if (targetBehaviour is PlayerHealth playerHealth)
        {
            if (playerHealth.IsDowned)
            {
                return false;
            }

            playerHealth.DamageReceived(damage, _networkObject);
        }
        else if (targetBehaviour is NPCHealth npcHealth)
        {
            if (npcHealth.IsDead)
            {
                return false;
            }

            npcHealth.DamageReceived(damage, _networkObject);
            if (npcHealth == null || npcHealth.IsDead)
            {
                return true;
            }
        }
        else
        {
            return false;
        }

        TryApplyImpactImpulse(targetBehaviour, impulseProfile);
        return true;
    }

    private void TryApplyImpactImpulse(
        MonoBehaviour targetBehaviour,
        ExternalImpulseProfileSO impulseProfile)
    {
        if (targetBehaviour == null || impulseProfile == null)
        {
            return;
        }

        Vector3 horizontalDirection = targetBehaviour.transform.position - transform.position;
        horizontalDirection.y = 0f;
        if (horizontalDirection.sqrMagnitude <= 0.0001f)
        {
            horizontalDirection = transform.forward;
        }

        foreach (MonoBehaviour behaviour in targetBehaviour.GetComponentsInParent<MonoBehaviour>(true))
        {
            if (behaviour is IExternalImpulseReceiver receiver)
            {
                receiver.TryApplyExternalImpulse(
                    impulseProfile.CreateImpulse(horizontalDirection),
                    _networkObject);
                return;
            }
        }
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null &&
               NetworkManager.Singleton.IsListening &&
               IsSpawned;
    }

    public void TryPerformAction()
    {
        if (_concreteTrap != null && _concreteTrap.BlocksGameplayInput)
        {
            performAction = false;
            CancelCurrentAction();
            return;
        }
        if (IsSpecialInputToolSelected())
        {
            performAction = false;
            CancelCurrentAction();
            return;
        }

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

    private bool IsRopeSelected()
    {
        EquippableItemSO selectedItem = _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        return selectedItem != null && selectedItem.itemType == EquippableItemType.Rope;
    }

    private bool IsSpecialInputToolSelected()
    {
        EquippableItemSO selectedItem = _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        return selectedItem != null &&
               (selectedItem.itemType == EquippableItemType.Rope || selectedItem.itemType == EquippableItemType.SpiritLevel);
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

    public void CancelActionForStateChange()
    {
        performAction = false;
        CancelCurrentAction();
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
        if (TryConsumeAimedMountingNail(impactItem, out Collider nailCollider, out ActionImpactSurfaceType nailSurface))
        {
            hit = true;
            surfaceType = nailSurface;
            SpawnImpactEffect(nailCollider, surfaceType);
            OnActionPerformed?.Invoke(this, EventArgs.Empty);
        }
        else if (TryGetSingleActionTarget(actionRange, out IDamageable damageable, out Collider hitCollider))
        {
            if (IsCombatTarget(damageable))
            {
                hit = TrySubmitCombatImpact(damageable, impactItem);
            }
            else
            {
                damageable.DamageReceived(impactItem, actionDamage);
                hit = true;
            }

            if (hit)
            {
                surfaceType = ResolveImpactSurface(damageable, hitCollider);
                SpawnImpactEffect(hitCollider, surfaceType);
                OnActionPerformed?.Invoke(this, EventArgs.Empty);
            }
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
