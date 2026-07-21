using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BaseResourceNew : NetworkBehaviour, IInteractableNew, IPIckableNew, IDamageable, ISharedCarryObject, IHeldObjectHudInfoProvider
{
    private readonly struct RigidbodyState
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 LinearVelocity;
        public readonly Vector3 AngularVelocity;

        public RigidbodyState(Rigidbody body)
        {
            Position = body.position;
            Rotation = body.rotation;
            LinearVelocity = body.linearVelocity;
            AngularVelocity = body.angularVelocity;
        }
    }

    private const ulong NoHolderClientId = ulong.MaxValue;
    private const float SharedCarryInputStaleTime = 0.2f;
    private static readonly Dictionary<ulong, BaseResourceNew> HeldResourceByClientId = new Dictionary<ulong, BaseResourceNew>();

    [SerializeField] private BaseResourceSO baseResourceSO;
    [SerializeField] private float resourceDurability;
    private readonly NetworkVariable<float> resourceDurabilityNetwork = new NetworkVariable<float>();
    public EventHandler EquippableItemNeeded;
    [SerializeField] private bool isPickedUp = false;
    [SerializeField] private LayerMask sharedCarryGroundLayerMask = Physics.DefaultRaycastLayers;
    [SerializeField] private float sharedCarryGroundRaycastUpOffset = 2f;
    [SerializeField] private float sharedCarryGroundRaycastDownDistance = 20f;
    [SerializeField] private float sharedCarryGroundClearance = 0.02f;
    [SerializeField] private float sharedCarryGroundVerticalFollowSpeed = 12f;
    [SerializeField] private float sharedCarryMaxVerticalPlacementDelta = 0.75f;
    public bool IsPickedUp => isPickedUp;
    public bool CanBeCarried => baseResourceSO != null && baseResourceSO.canBeCarried;
    public bool CanBeDestroyed => baseResourceSO != null
        && baseResourceSO.baseResourceDestructionRecipeArray != null
        && baseResourceSO.baseResourceDestructionRecipeArray.Length > 0;
    private Rigidbody _rigidbody;
    private SharedCarryPhysicsBody _sharedCarryPhysicsBody;
    private SharedCarryCollisionController _sharedCarryCollisionController;
    private readonly List<ulong> holderClientIds = new List<ulong>();
    private readonly Dictionary<ulong, int> holderAttachPointIndices = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, Vector3> holderAttachLocalPoints = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, Vector3> holderMoveInputs = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, float> holderYawInputs = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, float> holderLastInputTimes = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, Vector3> holderBodyAnchorLocalOffsets = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, float> holderControllerRadii = new Dictionary<ulong, float>();
    private readonly List<ulong> npcHolderActorIds = new List<ulong>();
    private readonly Dictionary<ulong, ICarryActor> npcHolderActors = new Dictionary<ulong, ICarryActor>();
    private readonly Dictionary<ulong, int> npcHolderAttachPointIndices = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, Vector3> npcHolderAttachLocalPoints = new Dictionary<ulong, Vector3>();
    private ICarryActor externalCarryActor;

    public EventHandler<ResourceDurabilityChangedEventArgs> ResourceDurabilityChanged;
    public class ResourceDurabilityChangedEventArgs : EventArgs
    {
        public float resourceDurability;
        public float resourceDurabilityNormalized;
    }

    public void Interact(Transform interactor)
    {
        if (!CanBeCarried)
        {
            return;
        }

        Debug.Log("Interacted with Base Resource");
        PickedUp(interactor);
    }

    public void PickedUp(Transform parent)
    {
        if (!CanBeCarried)
        {
            return;
        }

        if (!parent.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            return;
        }

        if (IsNetworkSessionActive())
        {
            if (!parent.TryGetComponent(out NetworkObject playerNetworkObject))
            {
                return;
            }

            if (IsServer)
            {
                TryCompleteNetworkPickup(playerNetworkObject.OwnerClientId, playerInteraction.CarryBodyAnchorLocalOffset, playerInteraction.GetCharacterControllerRadius());
            }
            else
            {
                RequestPickupServerRpc(playerNetworkObject.NetworkObjectId, playerInteraction.CarryBodyAnchorLocalOffset, playerInteraction.GetCharacterControllerRadius());
            }

            return;
        }

        if (ShouldUseServerDrivenCarry())
        {
            SetupLocalSharedCarryPickup(playerInteraction);
            return;
        }

        playerInteraction.PickUpObject(gameObject, this);
        SetPickedUpState(true);
    }
    public void DroppedDown()
    {
        Vector3 dropPosition = transform.position;
        Quaternion dropRotation = transform.rotation;

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                TryCompleteNetworkDrop(NetworkManager.ServerClientId, dropPosition, dropRotation);
            }
            else
            {
                RequestDropServerRpc(dropPosition, dropRotation);
            }

            return;
        }

        SetPickedUpState(false);
        ClearLocalSharedCarryState();
    }
    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _sharedCarryPhysicsBody = GetComponent<SharedCarryPhysicsBody>();
        _sharedCarryCollisionController = GetComponent<SharedCarryCollisionController>();
        if (_sharedCarryCollisionController == null)
        {
            _sharedCarryCollisionController = gameObject.AddComponent<SharedCarryCollisionController>();
        }
        if (_sharedCarryPhysicsBody != null && baseResourceSO != null)
        {
            _sharedCarryPhysicsBody.SetProfile(baseResourceSO.carryPhysicsProfile);
        }
        ApplyCarryabilityPhysicsState();
        resourceDurability = GetMaxResourceDurability();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (!IsNetworkSessionActive())
        {
            SetLocalDurability(GetMaxResourceDurability(), false);
        }
    }

    public override void OnNetworkSpawn()
    {
        resourceDurabilityNetwork.OnValueChanged += ResourceDurabilityNetwork_OnValueChanged;
        if (IsServer)
        {
            resourceDurabilityNetwork.Value = GetMaxResourceDurability();
        }

        resourceDurability = resourceDurabilityNetwork.Value;
        ApplyCarryabilityPhysicsState();
    }

    public override void OnNetworkDespawn()
    {
        resourceDurabilityNetwork.OnValueChanged -= ResourceDurabilityNetwork_OnValueChanged;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (ShouldUpdateKinematicCarryPosition())
        {
            UpdateKinematicCarryPosition();
        }
    }
    public BaseResourceSO GetBaseResourceSO()
    {
        return baseResourceSO;
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Base Resource");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Base Resource");
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        EquippableItemType toolType = equippableItemSO != null ? equippableItemSO.itemType : EquippableItemType.None;
        float damageAmount = equippableItemSO != null ? damage * 2f : damage;
        RequestOrApplyDamage(toolType, damageAmount);
    }

    public void DamageReceived(float damage)
    {
        RequestOrApplyDamage(EquippableItemType.None, damage);
    }

    public float GetMovementSpeedPenalty()
    {
        return baseResourceSO.movementSpeedPenalty;
    }

    public bool CanBeCarriedBy(ICarryActor carryActor)
    {
        if (!CanBeCarried || carryActor == null || !carryActor.CanCarryObject || externalCarryActor != null)
        {
            return false;
        }

        if (!AllowsMultipleCarriers())
        {
            return !isPickedUp;
        }

        if (!carryActor.CanParticipateInSharedCarry)
        {
            return false;
        }

        return GetCurrentHolderCount() < GetMaxCarriers()
            && !npcHolderActors.ContainsKey(carryActor.ActorId);
    }

    public bool TryPickupByCarrier(ICarryActor carryActor)
    {
        if (!CanBeCarriedBy(carryActor))
        {
            return false;
        }

        if (IsNetworkSessionActive() && !IsServer)
        {
            return false;
        }

        if (AllowsMultipleCarriers())
        {
            if (!carryActor.CanParticipateInSharedCarry)
            {
                return false;
            }

            return TryAddNpcSharedCarryHolder(carryActor);
        }

        externalCarryActor = carryActor;
        if (IsSpawned && NetworkObject != null && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }

        SetPickedUpState(true);
        if (IsNetworkSessionActive())
        {
            SetPickedUpStateClientRpc(true);
        }

        carryActor.ConfirmCarry(gameObject);
        return true;
    }

    public bool DropByCarrier(ICarryActor carryActor, Vector3 dropPosition, Quaternion dropRotation)
    {
        if (AllowsMultipleCarriers() && carryActor != null && npcHolderActors.ContainsKey(carryActor.ActorId))
        {
            ClearNpcSharedCarryHolder(carryActor.ActorId, true);

            if (GetCurrentHolderCount() > 0)
            {
                UpdateHolderCarryLoadClientRpcs();
                return true;
            }

            transform.SetPositionAndRotation(dropPosition, dropRotation);
            SetPickedUpState(false);

            if (IsNetworkSessionActive())
            {
                CompleteDropClientRpc(dropPosition, dropRotation);
            }

            return true;
        }

        if (externalCarryActor != carryActor)
        {
            return false;
        }

        externalCarryActor = null;
        carryActor.ForceRelease(gameObject);
        transform.SetPositionAndRotation(dropPosition, dropRotation);
        SetPickedUpState(false);

        if (IsNetworkSessionActive())
        {
            CompleteDropClientRpc(dropPosition, dropRotation);
        }

        return true;
    }

    public int GetMinAmountOfPlayersNeeded()
    {
        return baseResourceSO.minAmountOfPlayersNeeded;
    }

    public float GetCurrentResourceDurabilityNormalized()
    {
        float maxDurability = GetMaxResourceDurability();
        return maxDurability > 0f ? resourceDurability / maxDurability : 0f;
    }

    public string HeldObjectDisplayName => baseResourceSO != null ? baseResourceSO.resourceName : gameObject.name;
    public Sprite HeldObjectIcon => baseResourceSO != null ? baseResourceSO.icon : null;

    public bool CanBeDestroyedWith(EquippableItemType toolType)
    {
        return !isPickedUp && TryGetDestructionRecipe(toolType, out _);
    }

    public bool TryDamageFromNpc(EquippableItemType toolType, float damage)
    {
        if (damage <= 0f || !CanBeDestroyedWith(toolType))
        {
            return false;
        }

        if (IsNetworkSessionActive())
        {
            if (!IsServer)
            {
                return false;
            }

            ApplyDamageServer(toolType, damage);
            return true;
        }

        ApplyDamageLocal(toolType, damage);
        return true;
    }

    private void UpdatePickedUpProperties()
    {
        if (_rigidbody == null)
        {
            return;
        }

        if (baseResourceSO != null && !CanBeCarried)
        {
            ApplyCarryabilityPhysicsState();
            return;
        }

        if (isPickedUp)
        {
            if (AllowsMultipleCarriers() && _sharedCarryPhysicsBody != null)
            {
                bool simulatePhysics = !IsNetworkSessionActive() || IsServer;
                _sharedCarryPhysicsBody.BeginSharedCarry(simulatePhysics);
                return;
            }

            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;
        }
        else
        {
            ApplyCarryabilityPhysicsState();
        }
    }

    private void ApplyCarryabilityPhysicsState()
    {
        if (_rigidbody == null || baseResourceSO == null)
        {
            return;
        }

        if (CanBeCarried)
        {
            if (isPickedUp)
            {
                return;
            }

            _sharedCarryPhysicsBody?.EndSharedCarry();
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            _rigidbody.detectCollisions = true;
            return;
        }

        _sharedCarryPhysicsBody?.EndSharedCarry();

        if (!_rigidbody.isKinematic)
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        _rigidbody.useGravity = false;
        _rigidbody.isKinematic = true;
        _rigidbody.detectCollisions = true;
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }

    private void SetPickedUpState(bool pickedUp)
    {
        isPickedUp = pickedUp;
        UpdatePickedUpProperties();
    }

    private void RequestOrApplyDamage(EquippableItemType toolType, float damage)
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                ApplyDamageServer(toolType, damage);
            }
            else
            {
                RequestDamageServerRpc((int)toolType, damage);
            }

            return;
        }

        ApplyDamageLocal(toolType, damage);
    }

    private void ApplyDamageServer(EquippableItemType toolType, float damage)
    {
        if (!TryGetDestructionRecipe(toolType, out BaseResourceDestructionRecipe recipe))
        {
            NotifyEquippableItemNeeded();
            return;
        }

        float currentDurability = resourceDurabilityNetwork.Value > 0f ? resourceDurabilityNetwork.Value : GetMaxResourceDurability();
        float newDurability = Mathf.Max(0f, currentDurability - Mathf.Max(0f, damage));
        resourceDurabilityNetwork.Value = newDurability;

        if (newDurability <= 0f)
        {
            BaseResourceSpawnUtility.SpawnProducts(recipe, transform.position, transform.rotation);
            DespawnOrDestroy();
        }
    }

    private void ApplyDamageLocal(EquippableItemType toolType, float damage)
    {
        if (!TryGetDestructionRecipe(toolType, out BaseResourceDestructionRecipe recipe))
        {
            NotifyEquippableItemNeeded();
            return;
        }

        SetLocalDurability(Mathf.Max(0f, resourceDurability - Mathf.Max(0f, damage)), true);
        if (resourceDurability <= 0f)
        {
            BaseResourceSpawnUtility.SpawnProducts(recipe, transform.position, transform.rotation);
            DespawnOrDestroy();
        }
    }

    private bool TryGetDestructionRecipe(EquippableItemType toolType, out BaseResourceDestructionRecipe matchingRecipe)
    {
        matchingRecipe = default;
        if (baseResourceSO == null || baseResourceSO.baseResourceDestructionRecipeArray == null)
        {
            return false;
        }

        foreach (BaseResourceDestructionRecipe recipe in baseResourceSO.baseResourceDestructionRecipeArray)
        {
            if (recipe.neededEquippableItemType == toolType)
            {
                matchingRecipe = recipe;
                return true;
            }
        }

        return false;
    }

    private void SetLocalDurability(float durability, bool notify)
    {
        resourceDurability = durability;
        if (notify)
        {
            RaiseResourceDurabilityChanged();
        }
    }

    private void ResourceDurabilityNetwork_OnValueChanged(float previousValue, float newValue)
    {
        SetLocalDurability(newValue, true);
    }

    private void RaiseResourceDurabilityChanged()
    {
        ResourceDurabilityChanged?.Invoke(this, new ResourceDurabilityChangedEventArgs
        {
            resourceDurability = resourceDurability,
            resourceDurabilityNormalized = GetCurrentResourceDurabilityNormalized()
        });
    }

    private void NotifyEquippableItemNeeded()
    {
        if (IsNetworkSessionActive() && IsServer)
        {
            EquippableItemNeededClientRpc();
            return;
        }

        EquippableItemNeeded?.Invoke(this, EventArgs.Empty);
    }

    private float GetMaxResourceDurability()
    {
        return baseResourceSO != null ? Mathf.Max(0f, baseResourceSO.resourceDurability) : 0f;
    }

    private bool TryCompleteNetworkPickup(ulong ownerClientId, Vector3 bodyAnchorLocalOffset, float playerControllerRadius)
    {
        if (!CanBeCarried)
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (externalCarryActor != null)
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (holderClientIds.Contains(ownerClientId))
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (!AllowsMultipleCarriers() && GetCurrentHolderCount() > 0)
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (GetCurrentHolderCount() >= GetMaxCarriers())
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (HeldResourceByClientId.TryGetValue(ownerClientId, out BaseResourceNew heldResource) && heldResource != null && heldResource != this)
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        int attachPointIndex = -1;
        Vector3 attachLocalPoint = Vector3.zero;
        Vector3 playerPlacement = Vector3.zero;
        NetworkObject playerNetworkObject = null;
        if (ShouldUseServerDrivenCarry()
            && !TryPrepareSharedCarryPlayerPickup(ownerClientId, bodyAnchorLocalOffset, playerControllerRadius, out attachPointIndex, out attachLocalPoint, out playerPlacement, out playerNetworkObject))
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }
        holderClientIds.Add(ownerClientId);
        HeldResourceByClientId[ownerClientId] = this;
        holderAttachPointIndices[ownerClientId] = attachPointIndex;
        holderAttachLocalPoints[ownerClientId] = attachLocalPoint;
        holderMoveInputs[ownerClientId] = Vector3.zero;
        holderYawInputs[ownerClientId] = 0f;
        holderLastInputTimes[ownerClientId] = Time.time;
        holderBodyAnchorLocalOffsets[ownerClientId] = bodyAnchorLocalOffset;
        holderControllerRadii[ownerClientId] = playerControllerRadius;

        if (ShouldUseServerDrivenCarry())
        {
            if (NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            {
                NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
            }
        }
        else if (NetworkObject.OwnerClientId != ownerClientId)
        {
            NetworkObject.ChangeOwnership(ownerClientId);
        }

        SetPickedUpState(true);
        if (ShouldUseServerDrivenCarry())
        {
            if (playerNetworkObject.TryGetComponent(out PlayerInteractionNew serverPlayerInteraction))
            {
                serverPlayerInteraction.ApplySharedCarryPickupPlacement(playerPlacement);
            }
            else
            {
                playerNetworkObject.transform.position = playerPlacement;
            }
            _sharedCarryCollisionController.SetHolderCollisionIgnored(playerNetworkObject.transform, true);
            ApplySharedCarryPickupPlacementClientRpc(playerPlacement, CreateTargetClientRpcParams(ownerClientId));
            SetHolderCollisionIgnoredClientRpc(ownerClientId, true);
        }
        SetPickedUpStateClientRpc(true);
        ConfirmPickupClientRpc(!ShouldUseServerDrivenCarry(), CalculateCarryMovementSpeedPenalty(), ShouldUseServerDrivenCarry(), attachLocalPoint, CreateTargetClientRpcParams(ownerClientId));
        if (ShouldUseServerDrivenCarry())
        {
            StartHolderVisualOverrideClientRpc(ownerClientId, NetworkObject.NetworkObjectId, attachLocalPoint, bodyAnchorLocalOffset);
        }
        UpdateHolderCarryLoadClientRpcs();
        return true;
    }

    private bool TryCompleteNetworkDrop(ulong senderClientId, Vector3 dropPosition, Quaternion dropRotation)
    {
        if (!holderClientIds.Contains(senderClientId))
        {
            return false;
        }

        holderClientIds.Remove(senderClientId);
        if (TryGetPlayerObject(senderClientId, out NetworkObject senderPlayerObject))
        {
            _sharedCarryCollisionController?.SetHolderCollisionIgnored(senderPlayerObject.transform, false);
        }
        SetHolderCollisionIgnoredClientRpc(senderClientId, false);
        ClearHeldResource(senderClientId);
        StopHolderVisualOverrideClientRpc(senderClientId);
        ConfirmReleaseClientRpc(CreateTargetClientRpcParams(senderClientId));

        if (GetCurrentHolderCount() > 0)
        {
            UpdateHolderCarryLoadClientRpcs();
            return true;
        }

        transform.SetPositionAndRotation(dropPosition, dropRotation);

        if (NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }

        SetPickedUpState(false);
        CompleteDropClientRpc(dropPosition, dropRotation);
        return true;
    }

    private void TryCrushNetworkSharedCarryHolder(ulong holderClientId)
    {
        if (!AllowsMultipleCarriers() || holderClientIds.Count >= GetRecommendedCarriers() || !TryGetPlayerObject(holderClientId, out NetworkObject playerNetworkObject))
        {
            return;
        }

        if (!TryCompleteNetworkDrop(holderClientId, transform.position, transform.rotation)
            || !playerNetworkObject.TryGetComponent(out PlayerHealth playerHealth))
        {
            return;
        }

        playerHealth.DamageReceived(playerHealth.CurrentHealth);
    }

    private void TryCrushLocalSharedCarryHolder()
    {
        if (!holderClientIds.Contains(NoHolderClientId) || holderClientIds.Count >= GetRecommendedCarriers())
        {
            return;
        }

        PlayerInteractionNew playerInteraction = FindFirstObjectByType<PlayerInteractionNew>();
        if (playerInteraction == null || playerInteraction.GetPickedUpGameObject() != gameObject)
        {
            return;
        }

        PlayerHealth playerHealth = playerInteraction.GetComponent<PlayerHealth>();
        playerInteraction.DropHeldObjectForStateChange();
        if (playerHealth != null)
        {
            playerHealth.DamageReceived(playerHealth.CurrentHealth);
        }
    }

    private void DespawnOrDestroy()
    {
        if (IsSpawned && NetworkObject != null)
        {
            ForceReleaseCurrentHolder();
            ForceReleaseExternalCarryActor();
            NetworkObject.Despawn(true);
            return;
        }

        Destroy(gameObject);
    }

    private bool TryPrepareSharedCarryPlayerPickup(
        ulong ownerClientId,
        Vector3 bodyAnchorLocalOffset,
        float playerControllerRadius,
        out int attachPointIndex,
        out Vector3 attachLocalPoint,
        out Vector3 playerPlacement,
        out NetworkObject playerNetworkObject)
    {
        attachPointIndex = -1;
        attachLocalPoint = Vector3.zero;
        playerPlacement = Vector3.zero;
        playerNetworkObject = null;

        if (!TryGetPlayerObject(ownerClientId, out playerNetworkObject)
            || !playerNetworkObject.TryGetComponent(out CharacterController characterController))
        {
            return false;
        }

        bool preparedInitialOrientation = GetCurrentHolderCount() == 0;
        RigidbodyState orientationState = default;
        if (preparedInitialOrientation)
        {
            orientationState = CaptureRigidbodyState();
            SharedCarryAttachmentUtility.NormalizeSharedCarryOrientation(_rigidbody, GetSharedCarryRotationOffset());
        }

        Vector3 actorAnchorWorldPosition = playerNetworkObject.transform.TransformPoint(bodyAnchorLocalOffset);
        foreach (int candidateIndex in GetFreeAttachPointIndicesByDistance(actorAnchorWorldPosition, playerControllerRadius))
        {
            Vector3 candidateLocalPoint = GetCarryAttachLocalPoint(candidateIndex, playerControllerRadius);
            if (!SharedCarryAttachmentUtility.TryFindSafePlayerRootPosition(
                    playerNetworkObject.transform,
                    characterController,
                    transform,
                    transform.TransformPoint(candidateLocalPoint),
                    bodyAnchorLocalOffset,
                    sharedCarryMaxVerticalPlacementDelta,
                    out playerPlacement))
            {
                continue;
            }

            attachPointIndex = candidateIndex;
            attachLocalPoint = candidateLocalPoint;
            return true;
        }

        if (preparedInitialOrientation)
        {
            RestoreRigidbodyState(orientationState);
        }

        return false;
    }

    private bool TryPrepareLocalSharedCarryPlayerPickup(
        PlayerInteractionNew playerInteraction,
        out int attachPointIndex,
        out Vector3 attachLocalPoint,
        out Vector3 playerPlacement)
    {
        attachPointIndex = -1;
        attachLocalPoint = Vector3.zero;
        playerPlacement = playerInteraction.transform.position;
        if (!playerInteraction.TryGetComponent(out CharacterController characterController))
        {
            return false;
        }

        float controllerRadius = playerInteraction.GetCharacterControllerRadius();
        bool preparedInitialOrientation = GetCurrentHolderCount() == 0;
        RigidbodyState orientationState = default;
        if (preparedInitialOrientation)
        {
            orientationState = CaptureRigidbodyState();
            SharedCarryAttachmentUtility.NormalizeSharedCarryOrientation(_rigidbody, GetSharedCarryRotationOffset());
        }

        Vector3 actorAnchorWorldPosition = playerInteraction.GetCarryBodyAnchor().position;
        foreach (int candidateIndex in GetFreeAttachPointIndicesByDistance(actorAnchorWorldPosition, controllerRadius))
        {
            Vector3 candidateLocalPoint = GetCarryAttachLocalPoint(candidateIndex, controllerRadius);
            if (!SharedCarryAttachmentUtility.TryFindSafePlayerRootPosition(
                    playerInteraction.transform,
                    characterController,
                    transform,
                    transform.TransformPoint(candidateLocalPoint),
                    playerInteraction.CarryBodyAnchorLocalOffset,
                    sharedCarryMaxVerticalPlacementDelta,
                    out playerPlacement))
            {
                continue;
            }

            attachPointIndex = candidateIndex;
            attachLocalPoint = candidateLocalPoint;
            return true;
        }

        if (preparedInitialOrientation)
        {
            RestoreRigidbodyState(orientationState);
        }

        return false;
    }

    public void RemoveFromWorld()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                DespawnOrDestroy();
            }
            else
            {
                RequestRemoveFromWorldServerRpc();
            }

            return;
        }

        Destroy(gameObject);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPickupServerRpc(ulong playerNetworkObjectId, Vector3 bodyAnchorLocalOffset, float playerControllerRadius, ServerRpcParams serverRpcParams = default)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkObjectId, out NetworkObject playerNetworkObject))
        {
            return;
        }

        TryCompleteNetworkPickup(playerNetworkObject.OwnerClientId, bodyAnchorLocalOffset, playerControllerRadius);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestDropServerRpc(Vector3 dropPosition, Quaternion dropRotation, ServerRpcParams serverRpcParams = default)
    {
        TryCompleteNetworkDrop(serverRpcParams.Receive.SenderClientId, dropPosition, dropRotation);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSharedCarryExhaustionServerRpc(ServerRpcParams serverRpcParams = default)
    {
        TryCrushNetworkSharedCarryHolder(serverRpcParams.Receive.SenderClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestRemoveFromWorldServerRpc()
    {
        DespawnOrDestroy();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestDamageServerRpc(int toolType, float damage)
    {
        ApplyDamageServer((EquippableItemType)toolType, damage);
    }

    private ClientRpcParams CreateTargetClientRpcParams(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };
    }

    private void ForceReleaseCurrentHolder()
    {
        if (holderClientIds.Count == 0 && npcHolderActorIds.Count == 0)
        {
            return;
        }

        ulong[] previousHolderClientIds = holderClientIds.ToArray();
        holderClientIds.Clear();

        foreach (ulong holderClientId in previousHolderClientIds)
        {
            if (TryGetPlayerObject(holderClientId, out NetworkObject holderPlayerObject))
            {
                _sharedCarryCollisionController?.SetHolderCollisionIgnored(holderPlayerObject.transform, false);
            }
            SetHolderCollisionIgnoredClientRpc(holderClientId, false);
            ClearHeldResource(holderClientId);
            StopHolderVisualOverrideClientRpc(holderClientId);
            ConfirmReleaseClientRpc(CreateTargetClientRpcParams(holderClientId));
        }

        ulong[] previousNpcHolderActorIds = npcHolderActorIds.ToArray();
        foreach (ulong actorId in previousNpcHolderActorIds)
        {
            ClearNpcSharedCarryHolder(actorId, true);
        }
    }

    private void ForceReleaseExternalCarryActor()
    {
        if (externalCarryActor == null)
        {
            return;
        }

        ICarryActor previousCarryActor = externalCarryActor;
        externalCarryActor = null;
        previousCarryActor.ForceRelease(gameObject);
    }

    private void ClearHeldResource(ulong clientId)
    {
        if (HeldResourceByClientId.TryGetValue(clientId, out BaseResourceNew heldResource) && heldResource == this)
        {
            HeldResourceByClientId.Remove(clientId);
        }

        holderAttachPointIndices.Remove(clientId);
        holderAttachLocalPoints.Remove(clientId);
        holderMoveInputs.Remove(clientId);
        holderYawInputs.Remove(clientId);
        holderLastInputTimes.Remove(clientId);
        holderBodyAnchorLocalOffsets.Remove(clientId);
        holderControllerRadii.Remove(clientId);
    }

    private bool TryAddNpcSharedCarryHolder(ICarryActor carryActor)
    {
        if (carryActor == null || !carryActor.CanParticipateInSharedCarry || npcHolderActors.ContainsKey(carryActor.ActorId) || GetCurrentHolderCount() >= GetMaxCarriers())
        {
            return false;
        }

        int attachPointIndex = -1;
        Vector3 attachLocalPoint = Vector3.zero;
        bool preparedInitialOrientation = ShouldUseServerDrivenCarry() && GetCurrentHolderCount() == 0;
        RigidbodyState orientationState = default;
        if (preparedInitialOrientation)
        {
            orientationState = CaptureRigidbodyState();
            SharedCarryAttachmentUtility.NormalizeSharedCarryOrientation(_rigidbody, GetSharedCarryRotationOffset());
        }

        if (ShouldUseServerDrivenCarry())
        {
            Transform actorRoot = GetCarryActorRoot(carryActor);
            Vector3 actorAnchorWorldPosition = carryActor.BodyAnchor != null
                ? carryActor.BodyAnchor.position
                : actorRoot != null ? actorRoot.position : transform.position;
            if (!TryGetNearestFreeAttachPoint(actorAnchorWorldPosition, carryActor.CollisionRadius, out attachPointIndex, out attachLocalPoint))
            {
                if (preparedInitialOrientation)
                {
                    RestoreRigidbodyState(orientationState);
                }

                return false;
            }
        }

        if (IsSpawned && NetworkObject != null && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }
        ulong actorId = carryActor.ActorId;

        npcHolderActorIds.Add(actorId);
        npcHolderActors[actorId] = carryActor;
        npcHolderAttachPointIndices[actorId] = attachPointIndex;
        npcHolderAttachLocalPoints[actorId] = attachLocalPoint;

        SetPickedUpState(true);
        if (ShouldUseServerDrivenCarry())
        {
            _sharedCarryCollisionController.SetHolderCollisionIgnored(GetCarryActorRoot(carryActor), true);
        }
        if (IsNetworkSessionActive())
        {
            SetPickedUpStateClientRpc(true);
        }

        carryActor.ConfirmSharedCarry(gameObject, attachLocalPoint, CalculateCarryMovementSpeedPenalty());
        UpdateHolderCarryLoadClientRpcs();
        return true;
    }

    private void ClearNpcSharedCarryHolder(ulong actorId, bool notifyActor)
    {
        if (!npcHolderActors.TryGetValue(actorId, out ICarryActor carryActor))
        {
            return;
        }

        npcHolderActorIds.Remove(actorId);
        npcHolderActors.Remove(actorId);
        npcHolderAttachPointIndices.Remove(actorId);
        npcHolderAttachLocalPoints.Remove(actorId);
        _sharedCarryCollisionController?.SetHolderCollisionIgnored(GetCarryActorRoot(carryActor), false);

        if (notifyActor)
        {
            carryActor.ForceRelease(gameObject);
        }
    }

    private int GetCurrentHolderCount()
    {
        return holderClientIds.Count + npcHolderActorIds.Count;
    }

    private static Transform GetCarryActorRoot(ICarryActor carryActor)
    {
        if (carryActor == null)
        {
            return null;
        }

        if (carryActor.NetworkObject != null)
        {
            return carryActor.NetworkObject.transform;
        }

        return carryActor.BodyAnchor != null ? carryActor.BodyAnchor.root : null;
    }

    private bool AllowsMultipleCarriers()
    {
        return baseResourceSO != null && baseResourceSO.allowMultipleCarriers;
    }

    private bool ShouldUseServerDrivenCarry()
    {
        return AllowsMultipleCarriers();
    }

    private Vector3 GetSharedCarryRotationOffset()
    {
        return baseResourceSO != null ? baseResourceSO.sharedCarryRotationOffsetEuler : Vector3.zero;
    }

    private RigidbodyState CaptureRigidbodyState()
    {
        return new RigidbodyState(_rigidbody);
    }

    private void RestoreRigidbodyState(RigidbodyState state)
    {
        if (_rigidbody == null)
        {
            return;
        }

        transform.SetPositionAndRotation(state.Position, state.Rotation);
        Physics.SyncTransforms();
        _rigidbody.linearVelocity = state.LinearVelocity;
        _rigidbody.angularVelocity = state.AngularVelocity;
    }

    private int GetRecommendedCarriers()
    {
        if (baseResourceSO == null)
        {
            return 1;
        }

        return Mathf.Max(1, baseResourceSO.recommendedCarriers, baseResourceSO.minAmountOfPlayersNeeded);
    }

    private int GetMaxCarriers()
    {
        if (!AllowsMultipleCarriers())
        {
            return 1;
        }

        return Mathf.Max(1, baseResourceSO.maxCarriers, GetRecommendedCarriers());
    }

    private float GetCarrierRatio()
    {
        return Mathf.Clamp01((float)GetCurrentHolderCount() / GetRecommendedCarriers());
    }

    private float CalculateCarryMovementSpeedPenalty()
    {
        if (baseResourceSO == null)
        {
            return 0f;
        }

        float missingCarrierRatio = 1f - GetCarrierRatio();
        return baseResourceSO.movementSpeedPenalty + missingCarrierRatio * baseResourceSO.underStaffedPenaltyMultiplier;
    }

    private void UpdateHolderCarryLoadClientRpcs()
    {
        float movementSpeedPenalty = CalculateCarryMovementSpeedPenalty();
        int playerHolderCount = holderClientIds.Count;
        int requiredPlayerCount = GetRecommendedCarriers();
        float staminaDrainPerSecond = baseResourceSO != null ? baseResourceSO.sharedCarryUnderstaffedStaminaDrainPerSecond : 0f;

        foreach (ulong holderClientId in holderClientIds)
        {
            UpdateCarryLoadClientRpc(movementSpeedPenalty, playerHolderCount, requiredPlayerCount, staminaDrainPerSecond, CreateTargetClientRpcParams(holderClientId));
        }
    }

    private bool ShouldUpdateKinematicCarryPosition()
    {
        if (!ShouldUseServerDrivenCarry() || GetCurrentHolderCount() == 0)
        {
            return false;
        }

        return !IsNetworkSessionActive() || IsServer;
    }

    private void UpdateKinematicCarryPosition()
    {
        if (_sharedCarryPhysicsBody == null)
        {
            return;
        }

        Vector3 combinedInput = Vector3.zero;
        List<SharedCarryPhysicsHolder> physicsHolders = new List<SharedCarryPhysicsHolder>();

        foreach (ulong holderClientId in holderClientIds)
        {
            if (!holderMoveInputs.TryGetValue(holderClientId, out Vector3 holderInput))
            {
                continue;
            }

            holderYawInputs.TryGetValue(holderClientId, out float holderYawInput);

            if (holderLastInputTimes.TryGetValue(holderClientId, out float lastInputTime) && Time.time - lastInputTime > SharedCarryInputStaleTime)
            {
                holderInput = Vector3.zero;
                holderYawInput = 0f;
            }

            combinedInput += holderInput;
            if (TryGetHolderBodyAnchor(holderClientId, out Transform bodyAnchor)
                && holderAttachLocalPoints.TryGetValue(holderClientId, out Vector3 attachLocalPoint))
            {
                physicsHolders.Add(new SharedCarryPhysicsHolder
                {
                    BodyAnchor = bodyAnchor,
                    AttachLocalPoint = attachLocalPoint,
                    DesiredYawInput = holderYawInput
                });
            }
        }

        foreach (ulong actorId in npcHolderActorIds)
        {
            if (!npcHolderActors.TryGetValue(actorId, out ICarryActor carryActor))
            {
                continue;
            }

            combinedInput += Vector3.ClampMagnitude(carryActor.GetSharedCarryInput(), 1f);
            if (carryActor.BodyAnchor != null && npcHolderAttachLocalPoints.TryGetValue(actorId, out Vector3 attachLocalPoint))
            {
                physicsHolders.Add(new SharedCarryPhysicsHolder
                {
                    BodyAnchor = carryActor.BodyAnchor,
                    AttachLocalPoint = attachLocalPoint,
                    DesiredYawInput = 0f
                });
            }
        }

        combinedInput.y = 0f;
        combinedInput = Vector3.ClampMagnitude(combinedInput / GetRecommendedCarriers(), 1f);
        _sharedCarryPhysicsBody.Simulate(physicsHolders, combinedInput, GetRecommendedCarriers(), Time.fixedDeltaTime);
        UpdateNpcSharedCarryAttachments();
    }

    private void AlignSharedCarryHeightToHolderAnchors()
    {
        if (!TryGetSharedCarryHolderAnchorHeight(out float targetHeight))
        {
            return;
        }

        float maxDelta = Mathf.Max(0.1f, sharedCarryGroundVerticalFollowSpeed) * Time.deltaTime;
        Vector3 currentPosition = transform.position;
        currentPosition.y = Mathf.MoveTowards(currentPosition.y, targetHeight, maxDelta);
        transform.position = currentPosition;
    }

    private bool TryGetSharedCarryHolderAnchorHeight(out float targetHeight)
    {
        targetHeight = transform.position.y;
        float totalHeight = 0f;
        int validHolderCount = 0;

        foreach (ulong holderClientId in holderClientIds)
        {
            if (!holderAttachLocalPoints.TryGetValue(holderClientId, out Vector3 attachLocalPoint)
                || !TryGetHolderBodyAnchor(holderClientId, out Transform bodyAnchor))
            {
                continue;
            }

            totalHeight += bodyAnchor.position.y - transform.TransformVector(attachLocalPoint).y;
            validHolderCount++;
        }

        foreach (ulong actorId in npcHolderActorIds)
        {
            if (!npcHolderActors.TryGetValue(actorId, out ICarryActor carryActor)
                || !npcHolderAttachLocalPoints.TryGetValue(actorId, out Vector3 attachLocalPoint)
                || carryActor.BodyAnchor == null)
            {
                continue;
            }

            totalHeight += carryActor.BodyAnchor.position.y - transform.TransformVector(attachLocalPoint).y;
            validHolderCount++;
        }

        if (validHolderCount == 0)
        {
            return false;
        }

        targetHeight = totalHeight / validHolderCount;
        return true;
    }

    private bool TryGetHolderBodyAnchor(ulong holderClientId, out Transform holderBodyAnchor)
    {
        holderBodyAnchor = null;

        if (holderClientId == NoHolderClientId || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            PlayerInteractionNew localPlayerInteraction = FindFirstObjectByType<PlayerInteractionNew>();
            if (localPlayerInteraction == null || localPlayerInteraction.GetPickedUpGameObject() != gameObject)
            {
                return false;
            }

            holderBodyAnchor = localPlayerInteraction.GetCarryBodyAnchor();
            return holderBodyAnchor != null;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(holderClientId, out NetworkClient networkClient))
        {
            return false;
        }

        if (networkClient.PlayerObject == null || !networkClient.PlayerObject.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            return false;
        }

        holderBodyAnchor = playerInteraction.GetCarryBodyAnchor();
        return holderBodyAnchor != null;
    }

    public void SubmitSharedCarryInput(Vector3 worldTranslationInput, float yawInput)
    {
        worldTranslationInput.y = 0f;
        worldTranslationInput = Vector3.ClampMagnitude(worldTranslationInput, 1f);
        yawInput = Mathf.Clamp(yawInput, -1f, 1f);

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                SetSharedCarryInput(NetworkManager.Singleton.LocalClientId, worldTranslationInput, yawInput);
            }
            else
            {
                SubmitSharedCarryInputServerRpc(worldTranslationInput, yawInput);
            }

            return;
        }

        SetSharedCarryInput(NoHolderClientId, worldTranslationInput, yawInput);
    }

    public void RequestSharedCarryExhaustion()
    {
        if (!AllowsMultipleCarriers())
        {
            return;
        }

        if (IsNetworkSessionActive())
        {
            RequestSharedCarryExhaustionServerRpc();
            return;
        }

        TryCrushLocalSharedCarryHolder();
    }

    private void SetSharedCarryInput(ulong clientId, Vector3 worldTranslationInput, float yawInput)
    {
        if (!holderClientIds.Contains(clientId))
        {
            return;
        }

        holderMoveInputs[clientId] = worldTranslationInput;
        holderYawInputs[clientId] = yawInput;
        holderLastInputTimes[clientId] = Time.time;

        if (IsServer && clientId != NoHolderClientId)
        {
            UpdateHolderSharedCarryAnimationInputClientRpc(clientId, worldTranslationInput);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitSharedCarryInputServerRpc(Vector3 worldTranslationInput, float yawInput, ServerRpcParams serverRpcParams = default)
    {
        SetSharedCarryInput(serverRpcParams.Receive.SenderClientId, worldTranslationInput, yawInput);
    }
    private void SetupLocalSharedCarryPickup(PlayerInteractionNew playerInteraction)
    {
        if (!holderClientIds.Contains(NoHolderClientId))
        {
            if (!TryPrepareLocalSharedCarryPlayerPickup(
                    playerInteraction,
                    out int attachPointIndex,
                    out Vector3 attachLocalPoint,
                    out Vector3 playerPlacement))
            {
                return;
            }

            holderClientIds.Add(NoHolderClientId);
            holderAttachPointIndices[NoHolderClientId] = attachPointIndex;
            holderAttachLocalPoints[NoHolderClientId] = attachLocalPoint;
            holderMoveInputs[NoHolderClientId] = Vector3.zero;
            holderYawInputs[NoHolderClientId] = 0f;
            holderLastInputTimes[NoHolderClientId] = Time.time;
            holderBodyAnchorLocalOffsets[NoHolderClientId] = playerInteraction.CarryBodyAnchorLocalOffset;
            holderControllerRadii[NoHolderClientId] = playerInteraction.GetCharacterControllerRadius();
            playerInteraction.ApplySharedCarryPickupPlacement(playerPlacement);
            _sharedCarryCollisionController.SetHolderCollisionIgnored(playerInteraction.transform, true);
        }

        SetPickedUpState(true);
        playerInteraction.ConfirmPickedUpObject(gameObject, this, false, CalculateCarryMovementSpeedPenalty(), true, holderAttachLocalPoints[NoHolderClientId]);
        playerInteraction.UpdateSharedCarryLoad(
            CalculateCarryMovementSpeedPenalty(),
            holderClientIds.Count,
            GetRecommendedCarriers(),
            baseResourceSO != null ? baseResourceSO.sharedCarryUnderstaffedStaminaDrainPerSecond : 0f);
    }

    private void ClearLocalSharedCarryState()
    {
        if (!holderClientIds.Contains(NoHolderClientId))
        {
            return;
        }

        holderClientIds.Remove(NoHolderClientId);
        _sharedCarryCollisionController?.SetHolderCollisionIgnored(FindFirstObjectByType<PlayerInteractionNew>()?.transform, false);
        ClearHeldResource(NoHolderClientId);
    }

    private List<int> GetFreeAttachPointIndicesByDistance(Vector3 actorAnchorWorldPosition, float carrierRadius)
    {
        return SharedCarryAttachmentUtility.GetFreeAttachPointIndicesByDistance(
            transform,
            actorAnchorWorldPosition,
            GetMaxCarriers(),
            index => holderAttachPointIndices.ContainsValue(index) || npcHolderAttachPointIndices.ContainsValue(index),
            index => GetCarryAttachLocalPoint(index, carrierRadius));
    }

    private bool TryGetNearestFreeAttachPoint(Vector3 actorAnchorWorldPosition, float carrierRadius, out int attachPointIndex, out Vector3 attachLocalPoint)
    {
        List<int> candidates = GetFreeAttachPointIndicesByDistance(actorAnchorWorldPosition, carrierRadius);
        if (candidates.Count == 0)
        {
            attachPointIndex = -1;
            attachLocalPoint = Vector3.zero;
            return false;
        }

        attachPointIndex = candidates[0];
        attachLocalPoint = GetCarryAttachLocalPoint(attachPointIndex, carrierRadius);
        return true;
    }

    private void UpdateNpcSharedCarryAttachments()
    {
        foreach (ulong actorId in npcHolderActorIds)
        {
            if (!npcHolderActors.TryGetValue(actorId, out ICarryActor carryActor)
                || !npcHolderAttachLocalPoints.TryGetValue(actorId, out Vector3 attachLocalPoint))
            {
                continue;
            }

            carryActor.ApplySharedCarryAttachment(transform.TransformPoint(attachLocalPoint));
        }
    }

    private Vector3 GetCarryAttachLocalPoint(int attachPointIndex, float playerControllerRadius)
    {
        if (baseResourceSO != null && baseResourceSO.carryAttachLocalPoints != null && attachPointIndex >= 0 && attachPointIndex < baseResourceSO.carryAttachLocalPoints.Length)
        {
            return baseResourceSO.carryAttachLocalPoints[attachPointIndex];
        }

        return GenerateDefaultCarryAttachLocalPoint(attachPointIndex, playerControllerRadius);
    }

    private Vector3 GenerateDefaultCarryAttachLocalPoint(int attachPointIndex, float playerControllerRadius)
    {
        float playerClearance = baseResourceSO != null ? baseResourceSO.carryPlayerClearance : 0.35f;
        return SharedCarryAttachmentUtility.GenerateDefaultAttachLocalPoint(transform, attachPointIndex, GetMaxCarriers(), playerControllerRadius, playerClearance);
    }

    private Bounds GetLocalColliderBounds()
    {
        return SharedCarryAttachmentUtility.GetLocalColliderBounds(transform);
    }

    private void AlignSharedCarryHeightToGround()
    {
        if (!TryGetSharedCarryGroundedPosition(out Vector3 groundedPosition))
        {
            return;
        }

        float maxDelta = Mathf.Max(0.1f, sharedCarryGroundVerticalFollowSpeed) * Time.deltaTime;
        Vector3 currentPosition = transform.position;
        currentPosition.y = Mathf.MoveTowards(currentPosition.y, groundedPosition.y, maxDelta);
        transform.position = currentPosition;
    }

    private bool TryGetSharedCarryGroundedPosition(out Vector3 groundedPosition)
    {
        groundedPosition = transform.position;
        Bounds bounds = GetWorldColliderBounds();
        float bottomOffset = transform.position.y - bounds.min.y;
        float rayDistance = sharedCarryGroundRaycastUpOffset + bounds.size.y + sharedCarryGroundRaycastDownDistance;
        Vector3 rayOrigin = new Vector3(transform.position.x, bounds.max.y + sharedCarryGroundRaycastUpOffset, transform.position.z);
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, rayDistance, sharedCarryGroundLayerMask, QueryTriggerInteraction.Ignore);

        bool hasGroundHit = false;
        RaycastHit bestHit = default;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || IsOwnCollider(hit.collider) || hit.distance >= bestDistance)
            {
                continue;
            }

            bestHit = hit;
            bestDistance = hit.distance;
            hasGroundHit = true;
        }

        if (!hasGroundHit)
        {
            return false;
        }

        groundedPosition.y = bestHit.point.y + bottomOffset + sharedCarryGroundClearance;
        return true;
    }

    private Bounds GetWorldColliderBounds()
    {
        if (TryGetComponent(out Collider objectCollider))
        {
            return objectCollider.bounds;
        }

        return new Bounds(transform.position, Vector3.one);
    }

    private bool IsOwnCollider(Collider candidate)
    {
        if (candidate.transform == transform || candidate.transform.IsChildOf(transform))
        {
            return true;
        }

        return candidate.attachedRigidbody != null && candidate.attachedRigidbody.gameObject == gameObject;
    }

    [ClientRpc]
    private void SetPickedUpStateClientRpc(bool pickedUp)
    {
        SetPickedUpState(pickedUp);
    }

    [ClientRpc]
    private void EquippableItemNeededClientRpc()
    {
        EquippableItemNeeded?.Invoke(this, EventArgs.Empty);
    }
    [ClientRpc]
    private void ConfirmPickupClientRpc(bool followHoldPosition, float movementSpeedPenalty, bool useSharedCarryMovement, Vector3 attachLocalPoint, ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient?.PlayerObject == null)
        {
            return;
        }

        if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            playerInteraction.ConfirmPickedUpObject(gameObject, this, followHoldPosition, movementSpeedPenalty, useSharedCarryMovement, attachLocalPoint);
        }
    }

    [ClientRpc]
    private void ApplySharedCarryPickupPlacementClientRpc(Vector3 playerPlacement, ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject != null
            && NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            playerInteraction.ApplySharedCarryPickupPlacement(playerPlacement);
        }
    }

    [ClientRpc]
    private void SetHolderCollisionIgnoredClientRpc(ulong holderClientId, bool ignored)
    {
        if (TryGetPlayerObject(holderClientId, out NetworkObject playerNetworkObject))
        {
            _sharedCarryCollisionController?.SetHolderCollisionIgnored(playerNetworkObject.transform, ignored);
        }
    }

    [ClientRpc]
    private void RejectPickupClientRpc(ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"{name} is already being carried.");
    }

    [ClientRpc]
    private void ConfirmReleaseClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient?.PlayerObject == null)
        {
            return;
        }

        if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            playerInteraction.ForceReleasePickedUpObject(gameObject);
        }
    }

    [ClientRpc]
    private void UpdateCarryLoadClientRpc(float movementSpeedPenalty, int playerHolderCount, int requiredPlayerCount, float staminaDrainPerSecond, ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient?.PlayerObject == null)
        {
            return;
        }

        if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            playerInteraction.UpdateSharedCarryLoad(movementSpeedPenalty, playerHolderCount, requiredPlayerCount, staminaDrainPerSecond);
        }
    }

    [ClientRpc]
    private void StartHolderVisualOverrideClientRpc(ulong holderClientId, ulong carriedObjectNetworkId, Vector3 attachLocalPoint, Vector3 bodyAnchorLocalOffset)
    {
        if (!TryGetPlayerObject(holderClientId, out NetworkObject playerNetworkObject))
        {
            return;
        }

        if (!playerNetworkObject.TryGetComponent(out SharedCarryPlayerVisualOverride visualOverride))
        {
            visualOverride = playerNetworkObject.gameObject.AddComponent<SharedCarryPlayerVisualOverride>();
        }

        visualOverride.StartOverride(carriedObjectNetworkId, attachLocalPoint, bodyAnchorLocalOffset);
    }
    [ClientRpc]
    private void StopHolderVisualOverrideClientRpc(ulong holderClientId)
    {
        if (!TryGetPlayerObject(holderClientId, out NetworkObject playerNetworkObject))
        {
            return;
        }

        if (playerNetworkObject.TryGetComponent(out PlayerAnimationController animationController))
        {
            animationController.ClearExternalSharedCarryAnimationInput();
        }

        if (playerNetworkObject.TryGetComponent(out SharedCarryPlayerVisualOverride visualOverride))
        {
            visualOverride.StopOverride();
        }
    }

    [ClientRpc]
    private void UpdateHolderSharedCarryAnimationInputClientRpc(ulong holderClientId, Vector3 worldMoveInput)
    {
        if (!TryGetPlayerObject(holderClientId, out NetworkObject playerNetworkObject))
        {
            return;
        }

        if (playerNetworkObject.TryGetComponent(out PlayerAnimationController animationController))
        {
            animationController.SetExternalSharedCarryAnimationInput(worldMoveInput);
        }
    }

    private bool TryGetPlayerObject(ulong clientId, out NetworkObject playerNetworkObject)
    {
        playerNetworkObject = null;

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
        {
            return false;
        }

        foreach (NetworkObject spawnedObject in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (spawnedObject != null && spawnedObject.IsPlayerObject && spawnedObject.OwnerClientId == clientId)
            {
                playerNetworkObject = spawnedObject;
                return true;
            }
        }

        return false;
    }

    [ClientRpc]
    private void CompleteDropClientRpc(Vector3 dropPosition, Quaternion dropRotation)
    {
        transform.SetPositionAndRotation(dropPosition, dropRotation);
        SetPickedUpState(false);
    }



    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.collider == null)
        {
            return;
        }

        if (IsNetworkSessionActive() && !IsServer)
        {
            return;
        }

        if (isPickedUp && AllowsMultipleCarriers())
        {
            return;
        }

        if (IsActiveSharedCarryHolder(collision.collider))
        {
            return;
        }

        if (!TryGetCollisionDamageable(collision.collider, out IDamageable damageableObject))
        {
            return;
        }

        if (collision.relativeVelocity.magnitude > 1f)
        {
            damageableObject.DamageReceived(collision.relativeVelocity.magnitude);
        }
    }

    private bool TryGetCollisionDamageable(Collider collider, out IDamageable damageableObject)
    {
        damageableObject = null;
        if (collider == null)
        {
            return false;
        }

        if (collider.TryGetComponent(out damageableObject))
        {
            return true;
        }

        return collider.GetComponentInParent<IDamageable>() != null
            && collider.GetComponentInParent<IDamageable>() is IDamageable parentDamageable
            && (damageableObject = parentDamageable) != null;
    }

    private bool IsActiveSharedCarryHolder(Collider collider)
    {
        if (!isPickedUp || !AllowsMultipleCarriers() || collider == null)
        {
            return false;
        }

        Transform collisionRoot = collider.transform.root;

        foreach (ulong holderClientId in holderClientIds)
        {
            if (holderClientId == NoHolderClientId)
            {
                PlayerInteractionNew localPlayerInteraction = FindFirstObjectByType<PlayerInteractionNew>();
                if (localPlayerInteraction != null
                    && localPlayerInteraction.GetPickedUpGameObject() == gameObject
                    && localPlayerInteraction.transform.root == collisionRoot)
                {
                    return true;
                }

                continue;
            }

            if (TryGetPlayerObject(holderClientId, out NetworkObject playerNetworkObject)
                && playerNetworkObject.transform.root == collisionRoot)
            {
                return true;
            }
        }

        foreach (ulong actorId in npcHolderActorIds)
        {
            if (!npcHolderActors.TryGetValue(actorId, out ICarryActor carryActor))
            {
                continue;
            }

            Transform actorRoot = carryActor.NetworkObject != null
                ? carryActor.NetworkObject.transform.root
                : carryActor.BodyAnchor != null ? carryActor.BodyAnchor.root : null;

            if (actorRoot == collisionRoot)
            {
                return true;
            }
        }

        return false;
    }

    public override void OnDestroy()
    {
        _sharedCarryCollisionController?.RestoreAllCollisions();
        if (holderClientIds.Count > 0)
        {
            foreach (ulong holderClientId in holderClientIds)
            {
                ClearHeldResource(holderClientId);
            }
        }

        if (npcHolderActorIds.Count > 0)
        {
            foreach (ulong actorId in npcHolderActorIds.ToArray())
            {
                ClearNpcSharedCarryHolder(actorId, true);
            }
        }

        base.OnDestroy();
    }

}
