using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BaseResourceNew : NetworkBehaviour, IInteractableNew, IPIckableNew, IDamageable, ISharedCarryObject
{
    private const ulong NoHolderClientId = ulong.MaxValue;
    private const float SharedCarryInputStaleTime = 0.2f;
    private static readonly Dictionary<ulong, BaseResourceNew> HeldResourceByClientId = new Dictionary<ulong, BaseResourceNew>();

    [SerializeField] private BaseResourceSO baseResourceSO;
    [SerializeField] private float resourceDurability;
    public EventHandler EquippableItemNeeded;
    [SerializeField] private bool isPickedUp = false;
    public bool IsPickedUp => isPickedUp;
    private Rigidbody _rigidbody;
    private readonly List<ulong> holderClientIds = new List<ulong>();
    private readonly Dictionary<ulong, int> holderAttachPointIndices = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, Vector3> holderAttachLocalPoints = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, Vector3> holderMoveInputs = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, float> holderLastInputTimes = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, Vector3> holderBodyAnchorLocalOffsets = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, float> holderControllerRadii = new Dictionary<ulong, float>();
    private ICarryActor externalCarryActor;

    public EventHandler<ResourceDurabilityChangedEventArgs> ResourceDurabilityChanged;
    public class ResourceDurabilityChangedEventArgs : EventArgs
    {
        public float resourceDurability;
        public float resourceDurabilityNormalized;
    }

    public void Interact(Transform interactor)
    {
        Debug.Log("Interacted with Base Resource");
        PickedUp(interactor);
    }

    public void PickedUp(Transform parent)
    {
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
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        resourceDurability = baseResourceSO.resourceDurability;
    }

    // Update is called once per frame
    void Update()
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
        float damageAmount = 0;
        bool equippedItemSupported = false;
        BaseResourceSO productBaseResourceSO = null;
        if (equippableItemSO != null)
        {

            foreach (BaseResourceDestructionRecipe recipe in baseResourceSO.baseResourceDestructionRecipeArray)
            {
                if (equippableItemSO.itemType == recipe.neededEquippableItemType)
                {
                    equippedItemSupported = true;
                    productBaseResourceSO = recipe.finalProductBaseResourceSO;
                }
            }
            if (equippedItemSupported)
            {
                Debug.Log("Tool supported");
                damageAmount = equippableItemSO.damage;
                damageAmount *= 2;
            }
            else
            {
                EquippableItemNeeded?.Invoke(this, EventArgs.Empty);
                Debug.Log("Unsupported tool type");
            }
        }

        resourceDurability -= damageAmount;
        if (resourceDurability <= 0f)
        {
            if (productBaseResourceSO != null)
            {
                Debug.Log($"{baseResourceSO.name} resource source destroyed. Resource spawned {productBaseResourceSO.name}");
                // Here you would implement the logic to spawn the resource, e.g.:
                Instantiate(productBaseResourceSO.resourcePrefab, transform.position, Quaternion.identity);
            }
            //Destroy the resource source object after spawning the resource
            DespawnOrDestroy();

        }
        else
        {
            ResourceDurabilityChanged?.Invoke(this, new ResourceDurabilityChangedEventArgs
            {
                resourceDurability = resourceDurability,
                resourceDurabilityNormalized = GetCurrentResourceDurabilityNormalized()
            });
        }

    }

    public void DamageReceived(float damage)
    {
        resourceDurability -= damage;
        Debug.Log($"Current resource durability: {resourceDurability}");
        if (resourceDurability <= 0f)
        {
            foreach (BaseResourceDestructionRecipe recipe in baseResourceSO.baseResourceDestructionRecipeArray)
            {
                if (recipe.neededEquippableItemType == EquippableItemType.None)
                {
                    Debug.Log(recipe.finalProductBaseResourceSO.resourcePrefab);
                    Instantiate(recipe.finalProductBaseResourceSO.resourcePrefab, transform.position, Quaternion.identity);
                    break;
                }
            }
                DespawnOrDestroy();
        }
        else
        {
            ResourceDurabilityChanged?.Invoke(this, new ResourceDurabilityChangedEventArgs
            {
                resourceDurability = resourceDurability,
                resourceDurabilityNormalized = GetCurrentResourceDurabilityNormalized()
            });
        }
    }

    public float GetMovementSpeedPenalty()
    {
        return baseResourceSO.movementSpeedPenalty;
    }

    public bool CanBeCarriedBy(ICarryActor carryActor)
    {
        return carryActor != null
            && carryActor.CanCarryObject
            && !isPickedUp
            && externalCarryActor == null
            && !AllowsMultipleCarriers();
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
        return resourceDurability / baseResourceSO.resourceDurability;
    }

    private void UpdatePickedUpProperties()
    {
        if (_rigidbody == null)
        {
            return;
        }

        if (isPickedUp)
        {
            _rigidbody.useGravity = false;           
            _rigidbody.isKinematic = true;
        }
        else
        {
            _rigidbody.useGravity = true;
            _rigidbody.isKinematic = false;
        }
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

    private bool TryCompleteNetworkPickup(ulong ownerClientId, Vector3 bodyAnchorLocalOffset, float playerControllerRadius)
    {
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

        if (!AllowsMultipleCarriers() && holderClientIds.Count > 0)
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (holderClientIds.Count >= GetMaxCarriers())
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (HeldResourceByClientId.TryGetValue(ownerClientId, out BaseResourceNew heldResource) && heldResource != null && heldResource != this)
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        int attachPointIndex = ShouldUseServerDrivenCarry() ? GetFirstFreeAttachPointIndex() : -1;
        Vector3 attachLocalPoint = ShouldUseServerDrivenCarry() ? GetCarryAttachLocalPoint(attachPointIndex, playerControllerRadius) : Vector3.zero;
        holderClientIds.Add(ownerClientId);
        HeldResourceByClientId[ownerClientId] = this;
        holderAttachPointIndices[ownerClientId] = attachPointIndex;
        holderAttachLocalPoints[ownerClientId] = attachLocalPoint;
        holderMoveInputs[ownerClientId] = Vector3.zero;
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
        ClearHeldResource(senderClientId);
        StopHolderVisualOverrideClientRpc(senderClientId);
        ConfirmReleaseClientRpc(CreateTargetClientRpcParams(senderClientId));

        if (holderClientIds.Count > 0)
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
    private void RequestRemoveFromWorldServerRpc()
    {
        DespawnOrDestroy();
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
        if (holderClientIds.Count == 0)
        {
            return;
        }

        ulong[] previousHolderClientIds = holderClientIds.ToArray();
        holderClientIds.Clear();

        foreach (ulong holderClientId in previousHolderClientIds)
        {
            ClearHeldResource(holderClientId);
            StopHolderVisualOverrideClientRpc(holderClientId);
            ConfirmReleaseClientRpc(CreateTargetClientRpcParams(holderClientId));
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
        holderLastInputTimes.Remove(clientId);
        holderBodyAnchorLocalOffsets.Remove(clientId);
        holderControllerRadii.Remove(clientId);
    }

    private bool AllowsMultipleCarriers()
    {
        return baseResourceSO != null && baseResourceSO.allowMultipleCarriers;
    }

    private bool ShouldUseServerDrivenCarry()
    {
        return AllowsMultipleCarriers();
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
        return Mathf.Clamp01((float)holderClientIds.Count / GetRecommendedCarriers());
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

        foreach (ulong holderClientId in holderClientIds)
        {
            UpdateCarryLoadClientRpc(movementSpeedPenalty, CreateTargetClientRpcParams(holderClientId));
        }
    }

    private bool ShouldUpdateKinematicCarryPosition()
    {
        if (!ShouldUseServerDrivenCarry() || holderClientIds.Count == 0)
        {
            return false;
        }

        return !IsNetworkSessionActive() || IsServer;
    }

    private void UpdateKinematicCarryPosition()
    {
        Vector3 combinedInput = Vector3.zero;

        foreach (ulong holderClientId in holderClientIds)
        {
            if (!holderMoveInputs.TryGetValue(holderClientId, out Vector3 holderInput))
            {
                continue;
            }

            if (holderLastInputTimes.TryGetValue(holderClientId, out float lastInputTime) && Time.time - lastInputTime > SharedCarryInputStaleTime)
            {
                holderInput = Vector3.zero;
            }

            combinedInput += holderInput;
        }

        combinedInput.y = 0f;
        combinedInput = Vector3.ClampMagnitude(combinedInput / GetRecommendedCarriers(), 1f);
        if (combinedInput == Vector3.zero)
        {
            return;
        }

        float carryMoveSpeed = baseResourceSO != null ? baseResourceSO.carryMoveSpeed : 4f;
        transform.position += combinedInput * carryMoveSpeed * Time.deltaTime;
    }

    private bool TryGetHolderTransform(ulong holderClientId, out Transform holderTransform)
    {
        holderTransform = null;

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.TryGetValue(holderClientId, out NetworkClient networkClient))
        {
            return false;
        }

        if (networkClient.PlayerObject == null || !networkClient.PlayerObject.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            return false;
        }

        holderTransform = playerInteraction.GetPickUpHoldPositionHolder();
        return holderTransform != null;
    }

    public void SubmitSharedCarryInput(Vector3 worldMoveInput)
    {
        worldMoveInput.y = 0f;
        worldMoveInput = Vector3.ClampMagnitude(worldMoveInput, 1f);

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                SetSharedCarryInput(NetworkManager.Singleton.LocalClientId, worldMoveInput);
            }
            else
            {
                SubmitSharedCarryInputServerRpc(worldMoveInput);
            }

            return;
        }

        SetSharedCarryInput(NoHolderClientId, worldMoveInput);
    }

    private void SetSharedCarryInput(ulong clientId, Vector3 worldMoveInput)
    {
        if (!holderClientIds.Contains(clientId))
        {
            return;
        }

        holderMoveInputs[clientId] = worldMoveInput;
        holderLastInputTimes[clientId] = Time.time;

        if (IsServer && clientId != NoHolderClientId)
        {
            UpdateHolderSharedCarryAnimationInputClientRpc(clientId, worldMoveInput);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitSharedCarryInputServerRpc(Vector3 worldMoveInput, ServerRpcParams serverRpcParams = default)
    {
        SetSharedCarryInput(serverRpcParams.Receive.SenderClientId, worldMoveInput);
    }

    private void SetupLocalSharedCarryPickup(PlayerInteractionNew playerInteraction)
    {
        if (!holderClientIds.Contains(NoHolderClientId))
        {
            int attachPointIndex = GetFirstFreeAttachPointIndex();
            Vector3 attachLocalPoint = GetCarryAttachLocalPoint(attachPointIndex, playerInteraction.GetCharacterControllerRadius());
            holderClientIds.Add(NoHolderClientId);
            holderAttachPointIndices[NoHolderClientId] = attachPointIndex;
            holderAttachLocalPoints[NoHolderClientId] = attachLocalPoint;
            holderMoveInputs[NoHolderClientId] = Vector3.zero;
            holderLastInputTimes[NoHolderClientId] = Time.time;
            holderBodyAnchorLocalOffsets[NoHolderClientId] = playerInteraction.CarryBodyAnchorLocalOffset;
            holderControllerRadii[NoHolderClientId] = playerInteraction.GetCharacterControllerRadius();
        }

        SetPickedUpState(true);
        playerInteraction.ConfirmPickedUpObject(gameObject, this, false, CalculateCarryMovementSpeedPenalty(), true, holderAttachLocalPoints[NoHolderClientId]);
    }

    private void ClearLocalSharedCarryState()
    {
        if (!holderClientIds.Contains(NoHolderClientId))
        {
            return;
        }

        holderClientIds.Remove(NoHolderClientId);
        ClearHeldResource(NoHolderClientId);
    }

    private int GetFirstFreeAttachPointIndex()
    {
        int maxCarriers = GetMaxCarriers();
        for (int i = 0; i < maxCarriers; i++)
        {
            if (!holderAttachPointIndices.ContainsValue(i))
            {
                return i;
            }
        }

        return Mathf.Max(0, holderAttachPointIndices.Count);
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
        Bounds bounds = GetLocalColliderBounds();
        int carrierCount = GetMaxCarriers();
        float attachHeight = bounds.center.y;
        float playerClearance = baseResourceSO != null ? baseResourceSO.carryPlayerClearance : 0.35f;
        float attachDistance = playerControllerRadius + playerClearance;

        if (carrierCount <= 1)
        {
            return new Vector3(bounds.center.x, attachHeight, bounds.max.z + attachDistance);
        }

        if (carrierCount == 2)
        {
            float side = attachPointIndex == 0 ? -1f : 1f;
            return new Vector3(bounds.center.x + side * (bounds.extents.x + attachDistance), attachHeight, bounds.center.z);
        }

        float angle = (Mathf.PI * 2f / carrierCount) * attachPointIndex;
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + attachDistance;
        return bounds.center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    private Bounds GetLocalColliderBounds()
    {
        if (TryGetComponent(out Collider objectCollider))
        {
            Bounds worldBounds = objectCollider.bounds;
            return new Bounds(transform.InverseTransformPoint(worldBounds.center), new Vector3(
                worldBounds.size.x / Mathf.Max(transform.lossyScale.x, 0.001f),
                worldBounds.size.y / Mathf.Max(transform.lossyScale.y, 0.001f),
                worldBounds.size.z / Mathf.Max(transform.lossyScale.z, 0.001f)));
        }

        return new Bounds(Vector3.zero, Vector3.one);
    }

    [ClientRpc]
    private void SetPickedUpStateClientRpc(bool pickedUp)
    {
        SetPickedUpState(pickedUp);
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
    private void UpdateCarryLoadClientRpc(float movementSpeedPenalty, ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient?.PlayerObject == null)
        {
            return;
        }

        if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            playerInteraction.SetHoldedItemMovementSpeedPenalty(movementSpeedPenalty);
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
        if(collision.gameObject.TryGetComponent<IDamageable>(out IDamageable damageableObject))
        {
            if(collision.relativeVelocity.magnitude > 1)
            {
                damageableObject.DamageReceived(collision.relativeVelocity.magnitude);
            }
        }
    }

    public override void OnDestroy()
    {
        if (holderClientIds.Count > 0)
        {
            foreach (ulong holderClientId in holderClientIds)
            {
                ClearHeldResource(holderClientId);
            }
        }

        base.OnDestroy();
    }

}
