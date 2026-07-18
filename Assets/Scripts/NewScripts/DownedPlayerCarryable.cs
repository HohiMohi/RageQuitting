using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class DownedPlayerCarryable : NetworkBehaviour, IPIckableNew, IInteractableNew, IHeldObjectHudInfoProvider
{
    private const ulong NoCarrierNetworkObjectId = ulong.MaxValue;
    private static readonly Dictionary<ulong, DownedPlayerCarryable> CarriedPlayerByCarrierClientId = new Dictionary<ulong, DownedPlayerCarryable>();

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
    public bool CanBeCarried => playerHealth != null && playerHealth.IsDowned && !IsCarried;
    public string HeldObjectDisplayName => "Downed player";
    public Sprite HeldObjectIcon => null;

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
        characterController = GetComponent<CharacterController>();
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
        if (playerHealth == null || reviver == null || reviver.root == transform.root)
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

        isCarriedNetwork.Value = true;
        carrierNetworkObjectIdNetwork.Value = carrierNetworkObject.NetworkObjectId;
        CarriedPlayerByCarrierClientId[carrierNetworkObject.OwnerClientId] = this;

        ConfirmPickupClientRpc(CreateTargetClientRpcParams(carrierNetworkObject.OwnerClientId));
        StartCarryOnOwnerClientRpc(carrierNetworkObject.NetworkObjectId, CreateTargetClientRpcParams(OwnerClientId));
        StartCarrierCollisionIgnoreClientRpc(CreateTargetClientRpcParams(carrierNetworkObject.OwnerClientId));
        StartCarriedVisualOverrideClientRpc(carrierNetworkObject.NetworkObjectId, carriedPlayerLocalOffset);
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

        if (CarriedPlayerByCarrierClientId.TryGetValue(carrierNetworkObject.OwnerClientId, out DownedPlayerCarryable carriedPlayer) &&
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
        ulong previousCarrierClientId = TryGetCarrierOwnerClientId(previousCarrierNetworkObjectId, out ulong carrierClientId) ? carrierClientId : OwnerClientId;
        ClearServerCarrierRegistration(previousCarrierClientId);

        isCarriedNetwork.Value = false;
        carrierNetworkObjectIdNetwork.Value = NoCarrierNetworkObjectId;
        transform.SetPositionAndRotation(dropPosition, dropRotation);

        ConfirmReleaseClientRpc(CreateTargetClientRpcParams(previousCarrierClientId));
        StopCarrierCollisionIgnoreClientRpc(CreateTargetClientRpcParams(previousCarrierClientId));
        StopCarryOnOwnerClientRpc(dropPosition, dropRotation, CreateTargetClientRpcParams(OwnerClientId));
        StopCarriedVisualOverrideClientRpc();
    }

    private void CompleteLocalDrop(Vector3 dropPosition, Quaternion dropRotation)
    {
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
        Transform anchor = carrierTransform;
        if (carrierTransform.TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            anchor = playerInteraction.GetCarriedPlayerAnchor();
        }

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
        if (TryGetCarrierOwnerClientId(carrierNetworkObjectIdNetwork.Value, out ulong carrierClientId))
        {
            ClearServerCarrierRegistration(carrierClientId);
        }
    }

    private void ClearServerCarrierRegistration(ulong carrierClientId)
    {
        if (CarriedPlayerByCarrierClientId.TryGetValue(carrierClientId, out DownedPlayerCarryable carriedPlayer) && carriedPlayer == this)
        {
            CarriedPlayerByCarrierClientId.Remove(carrierClientId);
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
    private void StartCarrierCollisionIgnoreClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient?.PlayerObject == null)
        {
            return;
        }

        IgnoreCollisionsWithCarrier(NetworkManager.Singleton.LocalClient.PlayerObject.transform);
    }

    [ClientRpc]
    private void StopCarrierCollisionIgnoreClientRpc(ClientRpcParams clientRpcParams = default)
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
