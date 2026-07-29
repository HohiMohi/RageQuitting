using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class DownedPlayerCarryable : NetworkBehaviour, IPIckableNew, IInteractableNew, IHeldObjectHudInfoProvider
{
    private const ulong NoCarrierNetworkObjectId = ulong.MaxValue;
    private static readonly Dictionary<ulong, DownedPlayerCarryable> CarriedPlayerByCarrierNetworkObjectId = new Dictionary<ulong, DownedPlayerCarryable>();

    [SerializeField] private float movementSpeedPenalty = 0.35f;
    [SerializeField] private Vector3 carriedPlayerLocalOffset = new Vector3(0f, 0f, 0.25f);
    [SerializeField] private Vector3 dropOffset = new Vector3(0f, 0f, 1f);

    private readonly NetworkVariable<bool> isCarriedNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> carrierNetworkObjectIdNetwork = new NetworkVariable<ulong>(
        NoCarrierNetworkObjectId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private PlayerHealth playerHealth;
    private CharacterController characterController;
    private bool isCarriedLocal;
    private ulong localCarrierNetworkObjectId = NoCarrierNetworkObjectId;
    private Transform localCarrierTransform;
    private bool localCarryFollowActive;
    private bool characterControllerWasEnabled;
    private readonly List<ColliderPair> ignoredCollisionPairs = new List<ColliderPair>();

    public bool IsCarried => IsNetworkSessionActive() ? isCarriedNetwork.Value || localCarryFollowActive : isCarriedLocal || localCarryFollowActive;
    public bool IsLocalCarryFollowActive => localCarryFollowActive;
    public bool IsCarriedByNPC => IsCarried && TryResolveCarrierTransform(GetCarrierNetworkObjectId(), out Transform carrier)
        && carrier.TryGetComponent(out NPCCarrier _);
    public bool CanBeCarried => playerHealth != null && playerHealth.IsDowned && !IsCarried;
    public string HeldObjectDisplayName => "Downed player";
    public Sprite HeldObjectIcon => null;

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
        characterController = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        isCarriedNetwork.OnValueChanged += IsCarriedNetwork_OnValueChanged;
        carrierNetworkObjectIdNetwork.OnValueChanged += CarrierNetworkObjectIdNetwork_OnValueChanged;
        ApplyReplicatedCarryState();
    }

    private void LateUpdate()
    {
        if (!localCarryFollowActive)
        {
            return;
        }

        if (!TryResolveLocalCarrierTransform(out Transform carrierTransform))
        {
            return;
        }

        ApplyCarryFollow(carrierTransform);
    }

    public override void OnNetworkDespawn()
    {
        isCarriedNetwork.OnValueChanged -= IsCarriedNetwork_OnValueChanged;
        carrierNetworkObjectIdNetwork.OnValueChanged -= CarrierNetworkObjectIdNetwork_OnValueChanged;
        if (IsServer)
        {
            playerHealth?.ResumeRespawnTimerAfterNpcCarry();
        }

        if (IsServer
            && TryGetCarrierNetworkObject(carrierNetworkObjectIdNetwork.Value) is NetworkObject carrier
            && carrier.TryGetComponent(out NPCCarrier npcCarrier))
        {
            npcCarrier.ForceRelease(gameObject);
            DownedPlayerCarryReservation.Release(this, npcCarrier);
        }

        ClearServerCarrierRegistration();
        RestoreIgnoredCollisions();
        StopLocalCarryFollow(transform.position, transform.rotation);
    }

    public void Interact(Transform interactor)
    {
        PickedUp(interactor);
    }

    public void LookedAt(Transform interactor)
    {
    }

    public void LookedAway(Transform interactor)
    {
    }

    public void PickedUp(Transform parent)
    {
        TryRequestPickup(parent);
    }

    public void DroppedDown()
    {
        RequestDrop();
    }

    public float GetMovementSpeedPenalty()
    {
        return movementSpeedPenalty;
    }

    public int GetMinAmountOfPlayersNeeded()
    {
        return 1;
    }

    public bool TryRequestPickup(Transform carrier)
    {
        if (carrier == null || !CanBeCarried)
        {
            return false;
        }

        if (IsNetworkSessionActive())
        {
            NetworkObject carrierNetworkObject = carrier.GetComponentInParent<NetworkObject>();
            if (carrierNetworkObject == null)
            {
                return false;
            }

            if (IsServer)
            {
                return TryCompleteNetworkPickup(carrierNetworkObject);
            }

            RequestPickupServerRpc(carrierNetworkObject.NetworkObjectId);
            return true;
        }

        return TryCompleteLocalPickup(carrier);
    }

    public bool TryPickupByCarrier(ICarryActor carrier)
    {
        if (carrier == null
            || carrier.ActorType != CarryActorType.NPC
            || carrier.NetworkObject == null
            || !carrier.CanCarryObject
            || !CanBeCarried)
        {
            return false;
        }

        if (carrier is NPCCarrier npcCarrier
            && !DownedPlayerCarryReservation.IsReservedBy(this, npcCarrier))
        {
            return false;
        }

        float pickupDistance = 1.5f;
        if (carrier.NetworkObject.TryGetComponent(out NPCBrain brain))
        {
            pickupDistance = brain.InteractionDistance;
        }

        if (Vector3.Distance(transform.position, carrier.NetworkObject.transform.position) > pickupDistance)
        {
            return false;
        }

        if (IsNetworkSessionActive())
        {
            return IsServer && TryCompleteNpcNetworkPickup(carrier);
        }

        return TryCompleteNpcLocalPickup(carrier);
    }

    public bool DropByCarrier(ICarryActor carrier, Vector3 position, Quaternion rotation)
    {
        if (carrier == null || !IsCarriedBy(carrier.ActorId))
        {
            return false;
        }

        if (IsNetworkSessionActive())
        {
            if (!IsServer)
            {
                return false;
            }

            CompleteNetworkDrop(position, rotation);
            return true;
        }

        carrier.ForceRelease(gameObject);
        CompleteLocalDrop(position, rotation);
        return true;
    }

    public void RequestDrop()
    {
        if (!IsCarried)
        {
            return;
        }

        Vector3 dropPosition = CalculateDropPosition();
        Quaternion dropRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                TryCompleteNetworkDrop(NetworkManager.Singleton.LocalClientId, dropPosition, dropRotation);
            }
            else
            {
                RequestDropServerRpc(dropPosition, dropRotation);
            }

            return;
        }

        CompleteLocalDrop(dropPosition, dropRotation);
    }

    public void ForceDrop()
    {
        if (!IsCarried)
        {
            return;
        }

        Vector3 dropPosition = CalculateDropPosition();
        Quaternion dropRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                CompleteNetworkDrop(dropPosition, dropRotation);
            }

            return;
        }

        CompleteLocalDrop(dropPosition, dropRotation);
    }

    public void RequestRevive(Transform reviver)
    {
        if (playerHealth == null
            || !playerHealth.CanBeRevived
            || reviver == null
            || reviver.root == transform.root)
        {
            return;
        }

        ForceDrop();
        NetworkObject reviverNetworkObject = reviver.GetComponentInParent<NetworkObject>();
        playerHealth.RequestRevive(reviverNetworkObject);
    }

    public bool IsCarriedBy(ulong carrierNetworkObjectId)
    {
        return IsCarried && GetCarrierNetworkObjectId() == carrierNetworkObjectId;
    }

    public bool IsLocallyCarriedBy(Transform carrier)
    {
        return IsCarried && carrier != null && localCarrierTransform == carrier;
    }

    private bool TryCompleteNetworkPickup(NetworkObject carrierNetworkObject)
    {
        if (!IsServer || carrierNetworkObject == null || !ValidateCarrier(carrierNetworkObject))
        {
            RejectPickupClientRpc(CreateTargetClientRpcParams(carrierNetworkObject != null ? carrierNetworkObject.OwnerClientId : OwnerClientId));
            return false;
        }

        carrierNetworkObjectIdNetwork.Value = carrierNetworkObject.NetworkObjectId;
        isCarriedNetwork.Value = true;
        CarriedPlayerByCarrierNetworkObjectId[carrierNetworkObject.NetworkObjectId] = this;

        ConfirmPickupClientRpc(CreateTargetClientRpcParams(carrierNetworkObject.OwnerClientId));
        StartCarryOnOwnerClientRpc(carrierNetworkObject.NetworkObjectId, CreateTargetClientRpcParams(OwnerClientId));
        StartCarrierCollisionIgnoreClientRpc(carrierNetworkObject.NetworkObjectId);
        StartCarriedVisualOverrideClientRpc(carrierNetworkObject.NetworkObjectId, carriedPlayerLocalOffset);
        return true;
    }

    private bool TryCompleteNpcNetworkPickup(ICarryActor carrier)
    {
        if (!IsServer || carrier?.NetworkObject == null || !CanBeCarried || !carrier.CanCarryObject)
        {
            return false;
        }

        ulong carrierId = carrier.NetworkObject.NetworkObjectId;
        if (CarriedPlayerByCarrierNetworkObjectId.TryGetValue(carrierId, out DownedPlayerCarryable existing)
            && existing != null
            && existing != this)
        {
            return false;
        }

        carrierNetworkObjectIdNetwork.Value = carrierId;
        isCarriedNetwork.Value = true;
        CarriedPlayerByCarrierNetworkObjectId[carrierId] = this;
        carrier.ConfirmCarry(gameObject);
        playerHealth?.PauseRespawnTimerForNpcCarry();

        IgnoreCollisionsWithCarrier(carrier.NetworkObject.transform);
        StartCarryOnOwnerClientRpc(carrierId, CreateTargetClientRpcParams(OwnerClientId));
        StartCarrierCollisionIgnoreClientRpc(carrierId);
        StartCarriedVisualOverrideClientRpc(carrierId, carriedPlayerLocalOffset);
        return true;
    }

    private bool TryCompleteNpcLocalPickup(ICarryActor carrier)
    {
        if (carrier?.NetworkObject == null && carrier is not Component)
        {
            return false;
        }

        Transform carrierTransform = carrier.NetworkObject != null
            ? carrier.NetworkObject.transform
            : ((Component)carrier).transform;
        isCarriedLocal = true;
        localCarrierNetworkObjectId = carrier.ActorId;
        localCarrierTransform = carrierTransform;
        carrier.ConfirmCarry(gameObject);
        playerHealth?.PauseRespawnTimerForNpcCarry();
        IgnoreCollisionsWithCarrier(carrierTransform);
        StartLocalCarryFollow(carrierTransform);
        StartCarriedVisualOverride(carrierTransform, carriedPlayerLocalOffset);
        return true;
    }

    private bool TryCompleteLocalPickup(Transform carrier)
    {
        if (carrier == null || carrier.root == transform.root)
        {
            return false;
        }

        if (!carrier.TryGetComponent(out PlayerInteractionNew playerInteraction) || playerInteraction.HasPickedUpObject)
        {
            return false;
        }

        if (carrier.TryGetComponent(out PlayerHealth carrierHealth) && carrierHealth.IsDowned)
        {
            return false;
        }

        isCarriedLocal = true;
        localCarrierTransform = carrier;
        IgnoreCollisionsWithCarrier(carrier);
        playerInteraction.ConfirmPickedUpObject(gameObject, this, false, movementSpeedPenalty, false, Vector3.zero, true);
        StartLocalCarryFollow(carrier);
        StartCarriedVisualOverride(carrier, carriedPlayerLocalOffset);
        return true;
    }

    private bool ValidateCarrier(NetworkObject carrierNetworkObject)
    {
        if (!CanBeCarried || carrierNetworkObject == NetworkObject)
        {
            return false;
        }

        if (!carrierNetworkObject.TryGetComponent(out PlayerInteractionNew playerInteraction) || playerInteraction.HasPickedUpObject)
        {
            return false;
        }

        if (CarriedPlayerByCarrierNetworkObjectId.TryGetValue(carrierNetworkObject.NetworkObjectId, out DownedPlayerCarryable carriedPlayer) &&
            carriedPlayer != null &&
            carriedPlayer != this)
        {
            return false;
        }

        if (carrierNetworkObject.TryGetComponent(out PlayerHealth carrierHealth) && carrierHealth.IsDowned)
        {
            return false;
        }

        return true;
    }

    private bool TryCompleteNetworkDrop(ulong senderClientId, Vector3 dropPosition, Quaternion dropRotation)
    {
        if (!IsServer || !IsCarried)
        {
            return false;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(carrierNetworkObjectIdNetwork.Value, out NetworkObject carrierNetworkObject))
        {
            CompleteNetworkDrop(dropPosition, dropRotation);
            return true;
        }

        if (carrierNetworkObject.OwnerClientId != senderClientId)
        {
            return false;
        }

        CompleteNetworkDrop(dropPosition, dropRotation);
        return true;
    }

    private void CompleteNetworkDrop(Vector3 dropPosition, Quaternion dropRotation)
    {
        if (!IsServer)
        {
            return;
        }

        ulong previousCarrierNetworkObjectId = carrierNetworkObjectIdNetwork.Value;
        NetworkObject previousCarrier = TryGetCarrierNetworkObject(previousCarrierNetworkObjectId);
        ClearServerCarrierRegistration(previousCarrierNetworkObjectId);
        playerHealth?.ResumeRespawnTimerAfterNpcCarry();

        isCarriedNetwork.Value = false;
        carrierNetworkObjectIdNetwork.Value = NoCarrierNetworkObjectId;
        transform.SetPositionAndRotation(dropPosition, dropRotation);

        if (previousCarrier != null && previousCarrier.TryGetComponent(out NPCCarrier npcCarrier))
        {
            npcCarrier.ForceRelease(gameObject);
            DownedPlayerCarryReservation.Release(this, npcCarrier);
        }
        else if (previousCarrier != null)
        {
            ConfirmReleaseClientRpc(CreateTargetClientRpcParams(previousCarrier.OwnerClientId));
        }

        StopCarrierCollisionIgnoreClientRpc();
        StopCarryOnOwnerClientRpc(dropPosition, dropRotation, CreateTargetClientRpcParams(OwnerClientId));
        StopCarriedVisualOverrideClientRpc();
    }

    private void CompleteLocalDrop(Vector3 dropPosition, Quaternion dropRotation)
    {
        playerHealth?.ResumeRespawnTimerAfterNpcCarry();

        if (localCarrierTransform != null && localCarrierTransform.TryGetComponent(out NPCCarrier npcCarrier))
        {
            npcCarrier.ForceRelease(gameObject);
            DownedPlayerCarryReservation.Release(this, npcCarrier);
        }

        isCarriedLocal = false;
        localCarrierNetworkObjectId = NoCarrierNetworkObjectId;
        localCarrierTransform = null;
        StopCarriedVisualOverride();
        RestoreIgnoredCollisions();
        StopLocalCarryFollow(dropPosition, dropRotation);
    }

    private void StartLocalCarryFollow(Transform carrier)
    {
        localCarrierTransform = carrier;
        if (localCarryFollowActive)
        {
            return;
        }

        localCarryFollowActive = true;

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        characterControllerWasEnabled = characterController != null && characterController.enabled;
        if (characterControllerWasEnabled)
        {
            characterController.enabled = false;
        }

        if (TryResolveLocalCarrierTransform(out Transform carrierTransform))
        {
            IgnoreCollisionsWithCarrier(carrierTransform);
        }
    }

    private void StopLocalCarryFollow(Vector3 position, Quaternion rotation)
    {
        localCarryFollowActive = false;
        localCarrierTransform = null;
        localCarrierNetworkObjectId = NoCarrierNetworkObjectId;
        StopCarriedVisualOverride();
        RestoreIgnoredCollisions();
        transform.SetPositionAndRotation(position, rotation);

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (characterController != null && characterControllerWasEnabled)
        {
            characterController.enabled = true;
        }
    }

    private void ApplyCarryFollow(Transform carrierTransform)
    {
        Transform anchor = ResolveCarriedPlayerAnchor(carrierTransform);

        Quaternion yawRotation = Quaternion.Euler(0f, carrierTransform.eulerAngles.y, 0f);
        Vector3 targetPosition = anchor.position + yawRotation * carriedPlayerLocalOffset;
        transform.SetPositionAndRotation(targetPosition, yawRotation);
    }

    private bool TryResolveLocalCarrierTransform(out Transform carrierTransform)
    {
        carrierTransform = localCarrierTransform;
        if (carrierTransform != null)
        {
            return true;
        }

        if (localCarrierNetworkObjectId == NoCarrierNetworkObjectId || NetworkManager.Singleton == null)
        {
            return false;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(localCarrierNetworkObjectId, out NetworkObject carrierNetworkObject))
        {
            return false;
        }

        localCarrierTransform = carrierNetworkObject.transform;
        carrierTransform = localCarrierTransform;
        return true;
    }

    private Vector3 CalculateDropPosition()
    {
        if (TryResolveCarrierTransform(GetCarrierNetworkObjectId(), out Transform carrierTransform))
        {
            Quaternion yawRotation = Quaternion.Euler(0f, carrierTransform.eulerAngles.y, 0f);
            return carrierTransform.position + yawRotation * dropOffset;
        }

        if (localCarrierTransform != null)
        {
            Quaternion yawRotation = Quaternion.Euler(0f, localCarrierTransform.eulerAngles.y, 0f);
            return localCarrierTransform.position + yawRotation * dropOffset;
        }

        return transform.position;
    }

    private bool TryResolveCarrierTransform(ulong carrierNetworkObjectId, out Transform carrierTransform)
    {
        carrierTransform = null;
        if (carrierNetworkObjectId == NoCarrierNetworkObjectId || NetworkManager.Singleton == null)
        {
            return false;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(carrierNetworkObjectId, out NetworkObject carrierNetworkObject))
        {
            return false;
        }

        carrierTransform = carrierNetworkObject.transform;
        return true;
    }

    private NetworkObject TryGetCarrierNetworkObject(ulong carrierNetworkObjectId)
    {
        if (carrierNetworkObjectId == NoCarrierNetworkObjectId || NetworkManager.Singleton == null)
        {
            return null;
        }

        return NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(carrierNetworkObjectId, out NetworkObject carrier)
            ? carrier
            : null;
    }

    private bool TryGetCarrierOwnerClientId(ulong carrierNetworkObjectId, out ulong ownerClientId)
    {
        ownerClientId = OwnerClientId;
        if (carrierNetworkObjectId == NoCarrierNetworkObjectId || NetworkManager.Singleton == null)
        {
            return false;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(carrierNetworkObjectId, out NetworkObject carrierNetworkObject))
        {
            return false;
        }

        ownerClientId = carrierNetworkObject.OwnerClientId;
        return true;
    }

    private void IgnoreCollisionsWithCarrier(Transform carrier)
    {
        if (carrier == null)
        {
            return;
        }

        RestoreIgnoredCollisions();

        Collider[] carriedColliders = GetComponentsInChildren<Collider>(true);
        Collider[] carrierColliders = carrier.GetComponentsInChildren<Collider>(true);
        foreach (Collider carriedCollider in carriedColliders)
        {
            if (carriedCollider == null)
            {
                continue;
            }

            foreach (Collider carrierCollider in carrierColliders)
            {
                if (carrierCollider == null || carrierCollider == carriedCollider)
                {
                    continue;
                }

                Physics.IgnoreCollision(carrierCollider, carriedCollider, true);
                ignoredCollisionPairs.Add(new ColliderPair(carrierCollider, carriedCollider));
            }
        }
    }

    private void RestoreIgnoredCollisions()
    {
        if (ignoredCollisionPairs.Count == 0)
        {
            return;
        }

        foreach (ColliderPair pair in ignoredCollisionPairs)
        {
            if (pair.First != null && pair.Second != null)
            {
                Physics.IgnoreCollision(pair.First, pair.Second, false);
            }
        }

        ignoredCollisionPairs.Clear();
    }

    private void StartCarriedVisualOverride(Transform carrier, Vector3 visualLocalOffset)
    {
        if (carrier == null)
        {
            return;
        }

        if (!TryGetComponent(out CarriedPlayerVisualOverride visualOverride))
        {
            visualOverride = gameObject.AddComponent<CarriedPlayerVisualOverride>();
        }

        if (carrier.TryGetComponent(out NetworkObject carrierNetworkObject))
        {
            visualOverride.StartOverride(carrierNetworkObject.NetworkObjectId, visualLocalOffset);
        }
    }

    private void StopCarriedVisualOverride()
    {
        if (TryGetComponent(out CarriedPlayerVisualOverride visualOverride))
        {
            visualOverride.StopOverride();
        }
    }

    private void ClearServerCarrierRegistration()
    {
        ClearServerCarrierRegistration(carrierNetworkObjectIdNetwork.Value);
    }

    private void ClearServerCarrierRegistration(ulong carrierNetworkObjectId)
    {
        if (CarriedPlayerByCarrierNetworkObjectId.TryGetValue(carrierNetworkObjectId, out DownedPlayerCarryable carriedPlayer) && carriedPlayer == this)
        {
            CarriedPlayerByCarrierNetworkObjectId.Remove(carrierNetworkObjectId);
        }
    }

    private ulong GetCarrierNetworkObjectId()
    {
        return IsNetworkSessionActive() ? carrierNetworkObjectIdNetwork.Value : localCarrierNetworkObjectId;
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPickupServerRpc(ulong carrierNetworkObjectId)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(carrierNetworkObjectId, out NetworkObject carrierNetworkObject))
        {
            return;
        }

        TryCompleteNetworkPickup(carrierNetworkObject);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestDropServerRpc(Vector3 dropPosition, Quaternion dropRotation, ServerRpcParams serverRpcParams = default)
    {
        TryCompleteNetworkDrop(serverRpcParams.Receive.SenderClientId, dropPosition, dropRotation);
    }

    [ClientRpc]
    private void ConfirmPickupClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient?.PlayerObject == null)
        {
            return;
        }

        if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            playerInteraction.ConfirmPickedUpObject(gameObject, this, false, movementSpeedPenalty, false, Vector3.zero, true);
        }
    }

    [ClientRpc]
    private void RejectPickupClientRpc(ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"{name} cannot be carried right now.");
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
    private void StartCarryOnOwnerClientRpc(ulong carrierNetworkObjectId, ClientRpcParams clientRpcParams = default)
    {
        localCarrierNetworkObjectId = carrierNetworkObjectId;
        StartLocalCarryFollow(null);
    }

    [ClientRpc]
    private void StopCarryOnOwnerClientRpc(Vector3 dropPosition, Quaternion dropRotation, ClientRpcParams clientRpcParams = default)
    {
        StopLocalCarryFollow(dropPosition, dropRotation);
    }

    [ClientRpc]
    private void StartCarrierCollisionIgnoreClientRpc(ulong carrierNetworkObjectId)
    {
        if (!TryResolveCarrierTransform(carrierNetworkObjectId, out Transform carrierTransform))
        {
            return;
        }

        IgnoreCollisionsWithCarrier(carrierTransform);
    }

    [ClientRpc]
    private void StopCarrierCollisionIgnoreClientRpc()
    {
        RestoreIgnoredCollisions();
    }

    [ClientRpc]
    private void StartCarriedVisualOverrideClientRpc(ulong carrierNetworkObjectId, Vector3 visualLocalOffset)
    {
        if (!TryGetComponent(out CarriedPlayerVisualOverride visualOverride))
        {
            visualOverride = gameObject.AddComponent<CarriedPlayerVisualOverride>();
        }

        visualOverride.StartOverride(carrierNetworkObjectId, visualLocalOffset);
    }

    [ClientRpc]
    private void StopCarriedVisualOverrideClientRpc()
    {
        StopCarriedVisualOverride();
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

    private void IsCarriedNetwork_OnValueChanged(bool previousValue, bool newValue)
    {
        ApplyReplicatedCarryState();
    }

    private void CarrierNetworkObjectIdNetwork_OnValueChanged(ulong previousValue, ulong newValue)
    {
        ApplyReplicatedCarryState();
    }

    private void ApplyReplicatedCarryState()
    {
        if (!IsSpawned || !isCarriedNetwork.Value)
        {
            StopCarriedVisualOverride();
            RestoreIgnoredCollisions();
            return;
        }

        if (!TryResolveCarrierTransform(carrierNetworkObjectIdNetwork.Value, out Transform carrierTransform))
        {
            return;
        }

        IgnoreCollisionsWithCarrier(carrierTransform);
        StartCarriedVisualOverride(carrierTransform, carriedPlayerLocalOffset);
        if (IsOwner)
        {
            localCarrierNetworkObjectId = carrierNetworkObjectIdNetwork.Value;
            StartLocalCarryFollow(carrierTransform);
        }
    }

    private static Transform ResolveCarriedPlayerAnchor(Transform carrierTransform)
    {
        if (carrierTransform == null)
        {
            return null;
        }

        foreach (MonoBehaviour behaviour in carrierTransform.GetComponents<MonoBehaviour>())
        {
            if (behaviour is ICarriedPlayerAnchorProvider provider && provider.CarriedPlayerAnchor != null)
            {
                return provider.CarriedPlayerAnchor;
            }
        }

        return carrierTransform;
    }

    private readonly struct ColliderPair
    {
        public readonly Collider First;
        public readonly Collider Second;

        public ColliderPair(Collider first, Collider second)
        {
            First = first;
            Second = second;
        }
    }
}
