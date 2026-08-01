using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class MountableBridgeComponent : NetworkBehaviour, IPIckableNew, IInteractableNew, ISharedCarryObject, IHeldObjectHudInfoProvider, ICarriedObjectImpactTargetProvider, ISharedCarryAnchorPreviewProvider
{
    private const ulong NoHolderClientId = ulong.MaxValue;
    private const float SharedCarryInputStaleTime = 0.2f;
    private const float SharedCarryOrbitSyncInterval = 0.1f;
    private static readonly Dictionary<ulong, MountableBridgeComponent> HeldComponentByClientId = new Dictionary<ulong, MountableBridgeComponent>();
    private readonly NetworkVariable<ulong> occupiedAttachPointMaskNetwork = new NetworkVariable<ulong>();

    [SerializeField] private MountableBridgeComponentSO mountableBridgeComponentSO;
    [SerializeField] private bool isPickedUp = false;
    [SerializeField] private LayerMask sharedCarryGroundLayerMask = Physics.DefaultRaycastLayers;
    [SerializeField] private float sharedCarryGroundRaycastUpOffset = 2f;
    [SerializeField] private float sharedCarryGroundRaycastDownDistance = 20f;
    [SerializeField] private float sharedCarryGroundClearance = 0.02f;
    [SerializeField] private float sharedCarryGroundVerticalFollowSpeed = 12f;
    [SerializeField] private float sharedCarryMaxVerticalPlacementDelta = 0.75f;
    public bool IsPickedUp => isPickedUp;
    public bool IsActivelyCarried => isPickedUp;
    public bool SupportsAnchorPreview => _sharedCarryPhysicsBody != null
        && _sharedCarryPhysicsBody.ControlMode == SharedCarryControlMode.PhysicalPointGrip;

    private Rigidbody _rigidbody;
    private SharedCarryPhysicsBody _sharedCarryPhysicsBody;
    private SharedCarryCollisionController _sharedCarryCollisionController;
    private readonly List<ulong> holderClientIds = new List<ulong>();
    private readonly Dictionary<ulong, int> holderAttachPointIndices = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, Vector3> holderAttachLocalPoints = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, Vector3> holderBaseAttachLocalPoints = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, Vector3> holderMoveInputs = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, Vector3> holderLateralInputs = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, float> holderYawInputs = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, float> holderGripHeightInputs = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, float> holderOrbitAngles = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, float> holderLastOrbitSyncTimes = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, float> holderLastInputTimes = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, Vector3> holderBodyAnchorLocalOffsets = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, float> holderControllerRadii = new Dictionary<ulong, float>();
    private readonly List<ulong> npcHolderActorIds = new List<ulong>();
    private readonly Dictionary<ulong, ICarryActor> npcHolderActors = new Dictionary<ulong, ICarryActor>();
    private readonly Dictionary<ulong, int> npcHolderAttachPointIndices = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, Vector3> npcHolderAttachLocalPoints = new Dictionary<ulong, Vector3>();
    private ICarryActor externalCarryActor;
    private Vector3 sharedCarryOrbitPivotLocalPoint;
    private bool sharedCarryOrbitPivotInitialized;

    public void Interact(Transform interactor)
    {
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
        if (_rigidbody == null)
        {
            _rigidbody = gameObject.AddComponent<Rigidbody>();
        }
        _sharedCarryPhysicsBody = GetComponent<SharedCarryPhysicsBody>();
        _sharedCarryCollisionController = GetComponent<SharedCarryCollisionController>();
        if (_sharedCarryCollisionController == null)
        {
            _sharedCarryCollisionController = gameObject.AddComponent<SharedCarryCollisionController>();
        }
        if (_sharedCarryPhysicsBody != null && mountableBridgeComponentSO != null)
        {
            _sharedCarryPhysicsBody.SetProfile(mountableBridgeComponentSO.carryPhysicsProfile);
        }
    }

    private void FixedUpdate()
    {
        if (ShouldUpdateKinematicCarryPosition())
        {
            UpdateKinematicCarryPosition();
        }
    }

    public MountableBridgeComponentSO GetMountableBridgeComponentSO()
    {
        return mountableBridgeComponentSO;
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Mountable Bridge Component");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Mountable Bridge Component");
    }

    public float GetMovementSpeedPenalty()
    {
        return mountableBridgeComponentSO != null ? mountableBridgeComponentSO.movementSpeedPenalty : 0f;
    }

    public bool CanBeCarriedBy(ICarryActor carryActor)
    {
        if (carryActor == null || !carryActor.CanCarryObject || externalCarryActor != null)
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
        return mountableBridgeComponentSO != null ? mountableBridgeComponentSO.minAmountOfPlayersNeeded : 0;
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

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }

    private void SetPickedUpState(bool pickedUp)
    {
        isPickedUp = pickedUp;
        UpdatePickedUpProperties();
    }

    private void UpdatePickedUpProperties()
    {
        if (_rigidbody == null)
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        if (_rigidbody == null)
        {
            return;
        }

        if (isPickedUp && AllowsMultipleCarriers() && _sharedCarryPhysicsBody != null)
        {
            bool simulatePhysics = !IsNetworkSessionActive() || IsServer;
            _sharedCarryPhysicsBody.BeginSharedCarry(simulatePhysics);
            return;
        }

        _sharedCarryPhysicsBody?.EndSharedCarry();
        _rigidbody.useGravity = !isPickedUp;
        _rigidbody.isKinematic = isPickedUp;
    }

    private bool TryCompleteNetworkPickup(ulong ownerClientId, Vector3 bodyAnchorLocalOffset, float playerControllerRadius)
    {
        if (externalCarryActor != null)
        {
            RejectPickupClientRpc(SharedCarryPickupFailureReason.Generic, CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (holderClientIds.Contains(ownerClientId))
        {
            RejectPickupClientRpc(SharedCarryPickupFailureReason.Generic, CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (!AllowsMultipleCarriers() && GetCurrentHolderCount() > 0)
        {
            RejectPickupClientRpc(SharedCarryPickupFailureReason.NoAvailableAnchor, CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (GetCurrentHolderCount() >= GetMaxCarriers())
        {
            RejectPickupClientRpc(SharedCarryPickupFailureReason.NoAvailableAnchor, CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        if (HeldComponentByClientId.TryGetValue(ownerClientId, out MountableBridgeComponent heldComponent) && heldComponent != null && heldComponent != this)
        {
            RejectPickupClientRpc(SharedCarryPickupFailureReason.Generic, CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        int attachPointIndex = -1;
        Vector3 attachLocalPoint = Vector3.zero;
        Vector3 playerPlacement = Vector3.zero;
        NetworkObject playerNetworkObject = null;
        if (ShouldUseServerDrivenCarry()
            && !TryPrepareSharedCarryPlayerPickup(ownerClientId, bodyAnchorLocalOffset, playerControllerRadius, out attachPointIndex, out attachLocalPoint, out playerPlacement, out playerNetworkObject))
        {
            RejectPickupClientRpc(SharedCarryPickupFailureReason.NoAvailableAnchor, CreateTargetClientRpcParams(ownerClientId));
            return false;
        }
        holderClientIds.Add(ownerClientId);
        HeldComponentByClientId[ownerClientId] = this;
        holderAttachPointIndices[ownerClientId] = attachPointIndex;
        RefreshOccupiedAttachPointMask();
        holderAttachLocalPoints[ownerClientId] = attachLocalPoint;
        holderBaseAttachLocalPoints[ownerClientId] = attachLocalPoint;
        holderMoveInputs[ownerClientId] = Vector3.zero;
        holderLateralInputs[ownerClientId] = Vector3.zero;
        holderYawInputs[ownerClientId] = 0f;
        holderGripHeightInputs[ownerClientId] = 0f;
        holderOrbitAngles[ownerClientId] = 0f;
        holderLastOrbitSyncTimes[ownerClientId] = 0f;
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
        ClearHeldComponent(senderClientId);
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

    public string HeldObjectDisplayName => mountableBridgeComponentSO != null ? mountableBridgeComponentSO.componentName : gameObject.name;
    public Sprite HeldObjectIcon => mountableBridgeComponentSO != null ? mountableBridgeComponentSO.componentSprite : null;

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
            ForceReleaseCurrentHolders();
            ForceReleaseExternalCarryActor();
            NetworkObject.Despawn(true);
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

    private void ForceReleaseCurrentHolders()
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
            ClearHeldComponent(holderClientId);
            StopHolderVisualOverrideClientRpc(holderClientId);
            ConfirmReleaseClientRpc(CreateTargetClientRpcParams(holderClientId));
        }

        ulong[] previousNpcHolderActorIds = npcHolderActorIds.ToArray();
        foreach (ulong actorId in previousNpcHolderActorIds)
        {
            ClearNpcSharedCarryHolder(actorId, true);
        }
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

        if (GetCurrentHolderCount() == 0)
        {
            SharedCarryAttachmentUtility.NormalizeSharedCarryOrientation(_rigidbody);
        }

        Vector3 actorAnchorWorldPosition = playerNetworkObject.transform.TransformPoint(bodyAnchorLocalOffset);
        if (SharedCarryAttachmentUtility.TrySelectSafeAttachPoint(
                playerNetworkObject.transform,
                characterController,
                transform,
                transform.position,
                transform.rotation,
                actorAnchorWorldPosition,
                bodyAnchorLocalOffset,
                sharedCarryMaxVerticalPlacementDelta,
                GetCarryAttachPointCount(),
                index => holderAttachPointIndices.ContainsValue(index) || npcHolderAttachPointIndices.ContainsValue(index),
                index => GetCarryAttachLocalPoint(index, playerControllerRadius),
                out attachPointIndex,
                out attachLocalPoint,
                out _,
                out playerPlacement))
        {
            return true;
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
        Vector3 actorAnchorWorldPosition = playerInteraction.GetCarryBodyAnchor().position;
        if (SharedCarryAttachmentUtility.TrySelectSafeAttachPoint(
                playerInteraction.transform,
                characterController,
                transform,
                transform.position,
                transform.rotation,
                actorAnchorWorldPosition,
                playerInteraction.CarryBodyAnchorLocalOffset,
                sharedCarryMaxVerticalPlacementDelta,
                GetCarryAttachPointCount(),
                index => holderAttachPointIndices.ContainsValue(index) || npcHolderAttachPointIndices.ContainsValue(index),
                index => GetCarryAttachLocalPoint(index, controllerRadius),
                out attachPointIndex,
                out attachLocalPoint,
                out _,
                out playerPlacement))
        {
            return true;
        }

        return false;
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

    private void ClearHeldComponent(ulong clientId)
    {
        if (HeldComponentByClientId.TryGetValue(clientId, out MountableBridgeComponent heldComponent) && heldComponent == this)
        {
            HeldComponentByClientId.Remove(clientId);
        }

        holderAttachPointIndices.Remove(clientId);
        RefreshOccupiedAttachPointMask();
        holderAttachLocalPoints.Remove(clientId);
        holderBaseAttachLocalPoints.Remove(clientId);
        holderMoveInputs.Remove(clientId);
        holderLateralInputs.Remove(clientId);
        holderYawInputs.Remove(clientId);
        holderGripHeightInputs.Remove(clientId);
        holderOrbitAngles.Remove(clientId);
        holderLastOrbitSyncTimes.Remove(clientId);
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
        if (ShouldUseServerDrivenCarry())
        {
            Transform actorRoot = GetCarryActorRoot(carryActor);
            Vector3 actorAnchorWorldPosition = carryActor.BodyAnchor != null
                ? carryActor.BodyAnchor.position
                : actorRoot != null ? actorRoot.position : transform.position;
            if (!TryGetNearestFreeAttachPoint(actorAnchorWorldPosition, carryActor.CollisionRadius, out attachPointIndex, out attachLocalPoint))
            {
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
        RefreshOccupiedAttachPointMask();
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
        RefreshOccupiedAttachPointMask();
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
        return mountableBridgeComponentSO != null && mountableBridgeComponentSO.allowMultipleCarriers;
    }

    private bool ShouldUseServerDrivenCarry()
    {
        return AllowsMultipleCarriers();
    }

    private int GetRecommendedCarriers()
    {
        if (mountableBridgeComponentSO == null)
        {
            return 1;
        }

        return Mathf.Max(1, mountableBridgeComponentSO.recommendedCarriers, mountableBridgeComponentSO.minAmountOfPlayersNeeded);
    }

    private int GetMaxCarriers()
    {
        if (!AllowsMultipleCarriers())
        {
            return 1;
        }

        return Mathf.Max(1, mountableBridgeComponentSO.maxCarriers, GetRecommendedCarriers());
    }

    private float GetCarrierRatio()
    {
        return Mathf.Clamp01((float)GetCurrentHolderCount() / GetRecommendedCarriers());
    }

    private float CalculateCarryMovementSpeedPenalty()
    {
        if (mountableBridgeComponentSO == null)
        {
            return 0f;
        }

        float missingCarrierRatio = 1f - GetCarrierRatio();
        return mountableBridgeComponentSO.movementSpeedPenalty + missingCarrierRatio * mountableBridgeComponentSO.underStaffedPenaltyMultiplier;
    }

    private void UpdateHolderCarryLoadClientRpcs()
    {
        float movementSpeedPenalty = CalculateCarryMovementSpeedPenalty();
        int playerHolderCount = holderClientIds.Count;
        int requiredPlayerCount = GetRecommendedCarriers();
        float staminaDrainPerSecond = mountableBridgeComponentSO != null ? mountableBridgeComponentSO.sharedCarryUnderstaffedStaminaDrainPerSecond : 0f;

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
            holderLateralInputs.TryGetValue(holderClientId, out Vector3 holderLateralInput);
            holderGripHeightInputs.TryGetValue(holderClientId, out float holderGripHeightInput);

            if (holderLastInputTimes.TryGetValue(holderClientId, out float lastInputTime) && Time.time - lastInputTime > SharedCarryInputStaleTime)
            {
                holderInput = Vector3.zero;
                holderLateralInput = Vector3.zero;
                holderYawInput = 0f;
                holderGripHeightInput = 0f;
            }

            combinedInput += holderInput;
            if (TryGetHolderBodyAnchor(holderClientId, out Transform bodyAnchor)
                && holderAttachLocalPoints.TryGetValue(holderClientId, out Vector3 attachLocalPoint))
            {
                holderLateralInput = UpdateSpatialOrbitForHolder(holderClientId, bodyAnchor, holderLateralInput, Time.fixedDeltaTime);
                attachLocalPoint = holderAttachLocalPoints[holderClientId];
                physicsHolders.Add(new SharedCarryPhysicsHolder
                {
                    BodyAnchor = bodyAnchor,
                    BaseAttachLocalPoint = holderBaseAttachLocalPoints.TryGetValue(holderClientId, out Vector3 basePoint) ? basePoint : attachLocalPoint,
                    AttachLocalPoint = attachLocalPoint,
                    DesiredLateralInput = holderLateralInput,
                    DesiredYawInput = holderYawInput,
                    DesiredGripHeightInput = holderGripHeightInput
                });
            }
        }

        foreach (ulong actorId in npcHolderActorIds)
        {
            if (!npcHolderActors.TryGetValue(actorId, out ICarryActor carryActor))
            {
                continue;
            }

            Vector3 npcInput = Vector3.ClampMagnitude(carryActor.GetSharedCarryInput(), 1f);
            combinedInput += npcInput;
            if (carryActor.BodyAnchor != null && npcHolderAttachLocalPoints.TryGetValue(actorId, out Vector3 npcAttachLocalPoint))
            {
                physicsHolders.Add(new SharedCarryPhysicsHolder
                {
                    BodyAnchor = carryActor.BodyAnchor,
                    BaseAttachLocalPoint = npcAttachLocalPoint,
                    AttachLocalPoint = npcAttachLocalPoint,
                    DesiredLateralInput = Vector3.zero,
                    DesiredYawInput = 0f,
                    DesiredGripHeightInput = 0f
                });
            }
        }

        combinedInput.y = 0f;
        combinedInput = Vector3.ClampMagnitude(combinedInput / GetRecommendedCarriers(), 1f);
        _sharedCarryPhysicsBody.Simulate(physicsHolders, combinedInput, GetRecommendedCarriers(), Time.fixedDeltaTime);
        UpdateNpcSharedCarryAttachments();
    }

    private Vector3 UpdateSpatialOrbitForHolder(ulong clientId, Transform bodyAnchor, Vector3 worldLateralInput, float deltaTime)
    {
        if (_sharedCarryPhysicsBody == null || _sharedCarryPhysicsBody.ControlMode != SharedCarryControlMode.SpatialOrbit
            || !holderBaseAttachLocalPoints.TryGetValue(clientId, out Vector3 baseAttachLocalPoint)
            || !holderAttachLocalPoints.TryGetValue(clientId, out Vector3 currentAttachLocalPoint))
        {
            return worldLateralInput;
        }

        if (!sharedCarryOrbitPivotInitialized)
        {
            sharedCarryOrbitPivotLocalPoint = SharedCarryAttachmentUtility.GetLocalColliderBounds(transform).center;
            sharedCarryOrbitPivotInitialized = true;
        }

        Vector3 currentAttachWorldPoint = transform.TransformPoint(currentAttachLocalPoint);
        if (bodyAnchor == null || Vector3.Distance(bodyAnchor.position, currentAttachWorldPoint) > _sharedCarryPhysicsBody.MaxGripDistance)
        {
            return Vector3.zero;
        }

        float tangentialInput = SharedCarryAttachmentUtility.GetTangentialInput(
            transform,
            currentAttachLocalPoint,
            sharedCarryOrbitPivotLocalPoint,
            worldLateralInput);
        holderOrbitAngles.TryGetValue(clientId, out float orbitAngle);
        orbitAngle = Mathf.Clamp(
            orbitAngle + tangentialInput * _sharedCarryPhysicsBody.OrbitAngularSpeed * deltaTime,
            -_sharedCarryPhysicsBody.OrbitArcDegrees,
            _sharedCarryPhysicsBody.OrbitArcDegrees);
        holderOrbitAngles[clientId] = orbitAngle;
        holderAttachLocalPoints[clientId] = SharedCarryAttachmentUtility.CalculateOrbitAttachLocalPoint(
            transform,
            baseAttachLocalPoint,
            sharedCarryOrbitPivotLocalPoint,
            orbitAngle);

        if (IsNetworkSessionActive() && IsServer && clientId != NoHolderClientId
            && (!holderLastOrbitSyncTimes.TryGetValue(clientId, out float lastSyncTime)
                || Time.time - lastSyncTime >= SharedCarryOrbitSyncInterval))
        {
            holderLastOrbitSyncTimes[clientId] = Time.time;
            ReconcileSharedCarryOrbitClientRpc(orbitAngle, CreateTargetClientRpcParams(clientId));
        }

        return worldLateralInput;
    }

    [ClientRpc]
    private void ReconcileSharedCarryOrbitClientRpc(float authoritativeAngle, ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject != null
            && NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            playerInteraction.ReconcileSharedCarryOrbit(authoritativeAngle);
        }
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

    public void SubmitSharedCarryInput(Vector3 worldTranslationInput, Vector3 worldLateralInput, float directYawInput, float gripHeightInput)
    {
        worldTranslationInput.y = 0f;
        worldTranslationInput = Vector3.ClampMagnitude(worldTranslationInput, 1f);
        worldLateralInput.y = 0f;
        worldLateralInput = Vector3.ClampMagnitude(worldLateralInput, 1f);
        directYawInput = Mathf.Clamp(directYawInput, -1f, 1f);
        gripHeightInput = Mathf.Clamp(gripHeightInput, -1f, 1f);

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                SetSharedCarryInput(NetworkManager.Singleton.LocalClientId, worldTranslationInput, worldLateralInput, directYawInput, gripHeightInput);
            }
            else
            {
                SubmitSharedCarryInputServerRpc(worldTranslationInput, worldLateralInput, directYawInput, gripHeightInput);
            }

            return;
        }

        SetSharedCarryInput(NoHolderClientId, worldTranslationInput, worldLateralInput, directYawInput, gripHeightInput);
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

    private void SetSharedCarryInput(ulong clientId, Vector3 worldTranslationInput, Vector3 worldLateralInput, float directYawInput, float gripHeightInput)
    {
        if (!holderClientIds.Contains(clientId))
        {
            return;
        }

        worldTranslationInput.y = 0f;
        worldLateralInput.y = 0f;
        holderMoveInputs[clientId] = Vector3.ClampMagnitude(worldTranslationInput, 1f);
        holderLateralInputs[clientId] = Vector3.ClampMagnitude(worldLateralInput, 1f);
        holderYawInputs[clientId] = Mathf.Clamp(directYawInput, -1f, 1f);
        holderGripHeightInputs[clientId] = Mathf.Clamp(gripHeightInput, -1f, 1f);
        holderLastInputTimes[clientId] = Time.time;

        if (IsServer && clientId != NoHolderClientId)
        {
            Vector3 animationInput = _sharedCarryPhysicsBody != null
                && (_sharedCarryPhysicsBody.ControlMode == SharedCarryControlMode.SpatialOrbit
                    || _sharedCarryPhysicsBody.ControlMode == SharedCarryControlMode.PhysicalPointGrip)
                    ? Vector3.ClampMagnitude(worldTranslationInput + worldLateralInput, 1f)
                    : worldTranslationInput;
            UpdateHolderSharedCarryAnimationInputClientRpc(clientId, animationInput);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitSharedCarryInputServerRpc(Vector3 worldTranslationInput, Vector3 worldLateralInput, float directYawInput, float gripHeightInput, ServerRpcParams serverRpcParams = default)
    {
        SetSharedCarryInput(serverRpcParams.Receive.SenderClientId, worldTranslationInput, worldLateralInput, directYawInput, gripHeightInput);
    }
    private void SetupLocalSharedCarryPickup(PlayerInteractionNew playerInteraction)
    {
        if (!holderClientIds.Contains(NoHolderClientId))
        {
            if (GetCurrentHolderCount() == 0)
            {
                SharedCarryAttachmentUtility.NormalizeSharedCarryOrientation(_rigidbody);
            }

            if (!TryPrepareLocalSharedCarryPlayerPickup(
                    playerInteraction,
                    out int attachPointIndex,
                    out Vector3 attachLocalPoint,
                    out Vector3 playerPlacement))
            {
                playerInteraction.NotifySharedCarryPickupRejected(this, SharedCarryPickupFailureReason.NoAvailableAnchor);
                return;
            }

            holderClientIds.Add(NoHolderClientId);
            holderAttachPointIndices[NoHolderClientId] = attachPointIndex;
            RefreshOccupiedAttachPointMask();
            holderAttachLocalPoints[NoHolderClientId] = attachLocalPoint;
            holderBaseAttachLocalPoints[NoHolderClientId] = attachLocalPoint;
            holderMoveInputs[NoHolderClientId] = Vector3.zero;
            holderLateralInputs[NoHolderClientId] = Vector3.zero;
            holderYawInputs[NoHolderClientId] = 0f;
            holderGripHeightInputs[NoHolderClientId] = 0f;
            holderOrbitAngles[NoHolderClientId] = 0f;
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
            mountableBridgeComponentSO != null ? mountableBridgeComponentSO.sharedCarryUnderstaffedStaminaDrainPerSecond : 0f);
    }

    private void ClearLocalSharedCarryState()
    {
        if (!holderClientIds.Contains(NoHolderClientId))
        {
            return;
        }

        holderClientIds.Remove(NoHolderClientId);
        _sharedCarryCollisionController?.SetHolderCollisionIgnored(FindFirstObjectByType<PlayerInteractionNew>()?.transform, false);
        ClearHeldComponent(NoHolderClientId);
    }

    private List<int> GetFreeAttachPointIndicesByDistance(Vector3 actorAnchorWorldPosition, float carrierRadius)
    {
        return SharedCarryAttachmentUtility.GetFreeAttachPointIndicesByDistance(
            transform,
            actorAnchorWorldPosition,
            GetCarryAttachPointCount(),
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
        if (mountableBridgeComponentSO != null && mountableBridgeComponentSO.carryAttachLocalPoints != null && attachPointIndex >= 0 && attachPointIndex < mountableBridgeComponentSO.carryAttachLocalPoints.Length)
        {
            return mountableBridgeComponentSO.carryAttachLocalPoints[attachPointIndex];
        }

        return GenerateDefaultCarryAttachLocalPoint(attachPointIndex, playerControllerRadius);
    }

    private Vector3 GenerateDefaultCarryAttachLocalPoint(int attachPointIndex, float playerControllerRadius)
    {
        float playerClearance = mountableBridgeComponentSO != null ? mountableBridgeComponentSO.carryPlayerClearance : 0.35f;
        return SharedCarryAttachmentUtility.GenerateDefaultAttachLocalPoint(transform, attachPointIndex, GetMaxCarriers(), playerControllerRadius, playerClearance);
    }

    private int GetCarryAttachPointCount()
    {
        int configuredCount = mountableBridgeComponentSO != null && mountableBridgeComponentSO.carryAttachLocalPoints != null
            ? mountableBridgeComponentSO.carryAttachLocalPoints.Length
            : 0;
        return Mathf.Max(GetMaxCarriers(), configuredCount);
    }

    public bool TryGetAnchorPreview(PlayerInteractionNew player, out SharedCarryAnchorPreview preview)
    {
        preview = default;
        if (!SupportsAnchorPreview || player == null || player.HasPickedUpObject
            || externalCarryActor != null || !player.TryGetComponent(out CharacterController controller))
        {
            return false;
        }

        ulong occupiedMask = GetOccupiedAttachPointMask();
        int occupiedCount = CountOccupiedAttachPoints(occupiedMask);
        if (occupiedCount >= GetMaxCarriers())
        {
            return false;
        }

        Vector3 previewPosition = transform.position;
        Quaternion previewRotation = transform.rotation;
        if (occupiedCount == 0)
        {
            SharedCarryAttachmentUtility.CalculateNormalizedSharedCarryPose(
                transform,
                previewPosition,
                previewRotation,
                Vector3.zero,
                out previewPosition,
                out previewRotation);
        }

        float controllerRadius = player.GetCharacterControllerRadius();
        if (!SharedCarryAttachmentUtility.TrySelectSafeAttachPoint(
                player.transform,
                controller,
                transform,
                previewPosition,
                previewRotation,
                player.GetCarryBodyAnchor().position,
                player.CarryBodyAnchorLocalOffset,
                sharedCarryMaxVerticalPlacementDelta,
                GetCarryAttachPointCount(),
                index => IsAttachPointOccupied(occupiedMask, index),
                index => GetCarryAttachLocalPoint(index, controllerRadius),
                out int attachPointIndex,
                out Vector3 attachLocalPoint,
                out _,
                out Vector3 playerPlacement))
        {
            return false;
        }

        Vector3 markerWorldPoint = _sharedCarryPhysicsBody.ResolvePreviewGripPointForPose(
            attachLocalPoint,
            previewPosition,
            previewRotation,
            out Vector3 markerOutwardDirection);
        preview = new SharedCarryAnchorPreview(
            attachPointIndex,
            attachLocalPoint,
            markerWorldPoint,
            markerOutwardDirection,
            playerPlacement);
        return true;
    }

    private void RefreshOccupiedAttachPointMask()
    {
        if (!IsNetworkSessionActive() || !IsServer)
        {
            return;
        }

        occupiedAttachPointMaskNetwork.Value = BuildOccupiedAttachPointMask();
    }

    private ulong GetOccupiedAttachPointMask()
    {
        return IsNetworkSessionActive() && IsSpawned
            ? occupiedAttachPointMaskNetwork.Value
            : BuildOccupiedAttachPointMask();
    }

    private ulong BuildOccupiedAttachPointMask()
    {
        ulong mask = 0;
        foreach (int index in holderAttachPointIndices.Values)
        {
            if (index >= 0 && index < 64)
            {
                mask |= 1UL << index;
            }
        }

        foreach (int index in npcHolderAttachPointIndices.Values)
        {
            if (index >= 0 && index < 64)
            {
                mask |= 1UL << index;
            }
        }

        return mask;
    }

    private static bool IsAttachPointOccupied(ulong mask, int index)
    {
        return index >= 0 && index < 64 && (mask & (1UL << index)) != 0;
    }

    private static int CountOccupiedAttachPoints(ulong mask)
    {
        int count = 0;
        while (mask != 0)
        {
            mask &= mask - 1;
            count++;
        }

        return count;
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
    private void RejectPickupClientRpc(SharedCarryPickupFailureReason reason, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"{name} is already being carried.");
        if (reason != SharedCarryPickupFailureReason.None
            && NetworkManager.Singleton?.LocalClient?.PlayerObject != null
            && NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            playerInteraction.NotifySharedCarryPickupRejected(this, reason);
        }
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

    public void CollectActiveCarrierRoots(ICollection<GameObject> targets)
    {
        if (!isPickedUp || targets == null)
        {
            return;
        }

        int initialTargetCount = targets.Count;
        foreach (ulong holderClientId in holderClientIds)
        {
            if (holderClientId == NoHolderClientId)
            {
                AddLocalPlayerHoldingThisObject(targets);
                continue;
            }

            if (TryGetPlayerObject(holderClientId, out NetworkObject playerNetworkObject))
            {
                AddUniqueCarrierRoot(targets, playerNetworkObject.gameObject);
            }
        }

        foreach (ICarryActor carryActor in npcHolderActors.Values)
        {
            AddCarryActorRoot(targets, carryActor);
        }

        AddCarryActorRoot(targets, externalCarryActor);

        if (targets.Count == initialTargetCount)
        {
            AddLocalPlayerHoldingThisObject(targets);
        }
    }

    private void AddLocalPlayerHoldingThisObject(ICollection<GameObject> targets)
    {
        PlayerInteractionNew[] playerInteractions =
            FindObjectsByType<PlayerInteractionNew>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (PlayerInteractionNew playerInteraction in playerInteractions)
        {
            if (playerInteraction != null && playerInteraction.GetPickedUpGameObject() == gameObject)
            {
                AddUniqueCarrierRoot(targets, playerInteraction.transform.root.gameObject);
            }
        }
    }

    private static void AddCarryActorRoot(ICollection<GameObject> targets, ICarryActor carryActor)
    {
        if (carryActor == null)
        {
            return;
        }

        GameObject actorRoot = carryActor.NetworkObject != null
            ? carryActor.NetworkObject.gameObject
            : carryActor.BodyAnchor != null
                ? carryActor.BodyAnchor.root.gameObject
                : null;
        AddUniqueCarrierRoot(targets, actorRoot);
    }

    private static void AddUniqueCarrierRoot(ICollection<GameObject> targets, GameObject carrierRoot)
    {
        if (carrierRoot != null && !targets.Contains(carrierRoot))
        {
            targets.Add(carrierRoot);
        }
    }

    [ClientRpc]
    private void CompleteDropClientRpc(Vector3 dropPosition, Quaternion dropRotation)
    {
        transform.SetPositionAndRotation(dropPosition, dropRotation);
        SetPickedUpState(false);
    }

    public override void OnDestroy()
    {
        _sharedCarryCollisionController?.RestoreAllCollisions();
        if (holderClientIds.Count > 0)
        {
            foreach (ulong holderClientId in holderClientIds)
            {
                ClearHeldComponent(holderClientId);
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
