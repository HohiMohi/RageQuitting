using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class MountableBridgeComponent : NetworkBehaviour, IPIckableNew, IInteractableNew, ISharedCarryObject
{
    private const ulong NoHolderClientId = ulong.MaxValue;
    private const float SharedCarryInputStaleTime = 0.2f;
    private static readonly Dictionary<ulong, MountableBridgeComponent> HeldComponentByClientId = new Dictionary<ulong, MountableBridgeComponent>();

    [SerializeField] private MountableBridgeComponentSO mountableBridgeComponentSO;
    [SerializeField] private bool isPickedUp = false;
    [SerializeField] private LayerMask sharedCarryGroundLayerMask = Physics.DefaultRaycastLayers;
    [SerializeField] private float sharedCarryGroundRaycastUpOffset = 2f;
    [SerializeField] private float sharedCarryGroundRaycastDownDistance = 20f;
    [SerializeField] private float sharedCarryGroundClearance = 0.02f;
    [SerializeField] private float sharedCarryGroundVerticalFollowSpeed = 12f;
    public bool IsPickedUp => isPickedUp;

    private Rigidbody _rigidbody;
    private readonly List<ulong> holderClientIds = new List<ulong>();
    private readonly Dictionary<ulong, int> holderAttachPointIndices = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, Vector3> holderAttachLocalPoints = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, Vector3> holderMoveInputs = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, float> holderLastInputTimes = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, Vector3> holderBodyAnchorLocalOffsets = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, float> holderControllerRadii = new Dictionary<ulong, float>();
    private readonly List<ulong> npcHolderActorIds = new List<ulong>();
    private readonly Dictionary<ulong, ICarryActor> npcHolderActors = new Dictionary<ulong, ICarryActor>();
    private readonly Dictionary<ulong, int> npcHolderAttachPointIndices = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, Vector3> npcHolderAttachLocalPoints = new Dictionary<ulong, Vector3>();
    private ICarryActor externalCarryActor;

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
    }

    private void Update()
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

        _rigidbody.useGravity = !isPickedUp;
        _rigidbody.isKinematic = isPickedUp;
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

        if (HeldComponentByClientId.TryGetValue(ownerClientId, out MountableBridgeComponent heldComponent) && heldComponent != null && heldComponent != this)
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(ownerClientId));
            return false;
        }

        int attachPointIndex = ShouldUseServerDrivenCarry() ? GetFirstFreeAttachPointIndex() : -1;
        Vector3 attachLocalPoint = ShouldUseServerDrivenCarry() ? GetCarryAttachLocalPoint(attachPointIndex, playerControllerRadius) : Vector3.zero;
        holderClientIds.Add(ownerClientId);
        HeldComponentByClientId[ownerClientId] = this;
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
        holderAttachLocalPoints.Remove(clientId);
        holderMoveInputs.Remove(clientId);
        holderLastInputTimes.Remove(clientId);
        holderBodyAnchorLocalOffsets.Remove(clientId);
        holderControllerRadii.Remove(clientId);
    }

    private bool TryAddNpcSharedCarryHolder(ICarryActor carryActor)
    {
        if (carryActor == null || npcHolderActors.ContainsKey(carryActor.ActorId) || GetCurrentHolderCount() >= GetMaxCarriers())
        {
            return false;
        }

        if (IsSpawned && NetworkObject != null && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }

        int attachPointIndex = ShouldUseServerDrivenCarry() ? GetFirstFreeAttachPointIndex() : -1;
        Vector3 attachLocalPoint = ShouldUseServerDrivenCarry() ? GetCarryAttachLocalPoint(attachPointIndex, carryActor.CollisionRadius) : Vector3.zero;
        ulong actorId = carryActor.ActorId;

        npcHolderActorIds.Add(actorId);
        npcHolderActors[actorId] = carryActor;
        npcHolderAttachPointIndices[actorId] = attachPointIndex;
        npcHolderAttachLocalPoints[actorId] = attachLocalPoint;

        SetPickedUpState(true);
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

        if (notifyActor)
        {
            carryActor.ForceRelease(gameObject);
        }
    }

    private int GetCurrentHolderCount()
    {
        return holderClientIds.Count + npcHolderActorIds.Count;
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

        foreach (ulong holderClientId in holderClientIds)
        {
            UpdateCarryLoadClientRpc(movementSpeedPenalty, CreateTargetClientRpcParams(holderClientId));
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

        foreach (ulong actorId in npcHolderActorIds)
        {
            if (!npcHolderActors.TryGetValue(actorId, out ICarryActor carryActor))
            {
                continue;
            }

            combinedInput += Vector3.ClampMagnitude(carryActor.GetSharedCarryInput(), 1f);
        }

        combinedInput.y = 0f;
        combinedInput = Vector3.ClampMagnitude(combinedInput / GetRecommendedCarriers(), 1f);
        if (combinedInput != Vector3.zero)
        {
            float carryMoveSpeed = mountableBridgeComponentSO != null ? mountableBridgeComponentSO.carryMoveSpeed : 4f;
            transform.position += combinedInput * carryMoveSpeed * Time.deltaTime;
        }

        AlignSharedCarryHeightToHolderAnchors();
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
        ClearHeldComponent(NoHolderClientId);
    }

    private int GetFirstFreeAttachPointIndex()
    {
        int maxCarriers = GetMaxCarriers();
        for (int i = 0; i < maxCarriers; i++)
        {
            if (!holderAttachPointIndices.ContainsValue(i) && !npcHolderAttachPointIndices.ContainsValue(i))
            {
                return i;
            }
        }

        return Mathf.Max(0, GetCurrentHolderCount());
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
        Bounds bounds = GetLocalColliderBounds();
        int carrierCount = GetMaxCarriers();
        float attachHeight = bounds.center.y;
        float playerClearance = mountableBridgeComponentSO != null ? mountableBridgeComponentSO.carryPlayerClearance : 0.35f;
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

    public override void OnDestroy()
    {
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
