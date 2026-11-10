using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

public class EquippableItem : NetworkBehaviour, IInteractableNew
{
    [SerializeField] private EquippableItemSO equippableItemSO;
    public EventHandler<OnLookAtEventArgs> OnLookAt;
    public static EventHandler OnAnyItemEquipped;

    public class OnLookAtEventArgs : EventArgs
    {
        public EquippableItemSO equippableItemSO;
    }
    public EventHandler OnLookAway;

    public void Interact(Transform interactor)
    {
        Unity.Netcode.NetworkManager manager = NetworkManager != null ? NetworkManager : Unity.Netcode.NetworkManager.Singleton;
        if (manager != null && manager.IsListening && !IsSpawned)
            return;

        if (MountingNailSceneState.IsTargetConnected(transform)
            || (TryGetComponent(out EquippableWorldPhysics worldPhysics) && (worldPhysics.IsReturning || worldPhysics.IsTumbleweedCaptured))
            || !interactor.TryGetComponent(out PlayerInventory playerInventory)
            || !playerInventory.CanAddItem(equippableItemSO))
        {
            return;
        }

        OnLookAway?.Invoke(interactor, EventArgs.Empty);

        if (manager != null && manager.IsListening)
        {
            if (IsServer)
            {
                CompleteNetworkPickup(interactor, manager.LocalClientId);
            }
            else if (interactor.TryGetComponent(out NetworkObject interactorNetworkObject))
            {
                RequestPickupServerRpc(interactorNetworkObject.NetworkObjectId);
            }

            return;
        }

        CompletePickup(interactor);
    }

    public static bool DropItem(EquippableItemSO itemToDrop, NetworkObject dropper)
    {
        if (itemToDrop == null || itemToDrop.equippableItemPrefab == null || dropper == null)
        {
            return false;
        }

        GameObject droppedItem = Instantiate(
            itemToDrop.equippableItemPrefab,
            dropper.transform.position + Vector3.up,
            dropper.transform.rotation);
        try
        {
            droppedItem.GetComponent<EquippableWorldPhysics>()?.InitializeDrop(dropper);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, droppedItem);
        }
        return true;
    }

    public static bool SpawnNetworkedDrop(EquippableItemSO itemToDrop, NetworkObject dropper)
    {
        if (itemToDrop == null || itemToDrop.equippableItemPrefab == null || dropper == null)
        {
            return false;
        }

        return SpawnNetworkedDrop(itemToDrop, dropper, dropper.transform.position + Vector3.up, dropper.transform.rotation);
    }

    public static bool SpawnNetworkedDrop(EquippableItemSO itemToDrop, NetworkObject dropper,
        Vector3 worldPosition, Quaternion worldRotation)
    {
        if (itemToDrop == null || itemToDrop.equippableItemPrefab == null || dropper == null)
        {
            return false;
        }

        if (!itemToDrop.equippableItemPrefab.TryGetComponent<NetworkObject>(out _))
        {
            Debug.LogError($"Equippable item prefab '{itemToDrop.equippableItemPrefab.name}' is missing a root NetworkObject component.");
            return false;
        }

        GameObject droppedItem = Instantiate(
            itemToDrop.equippableItemPrefab,
            worldPosition,
            worldRotation);

        if (droppedItem.TryGetComponent(out NetworkObject networkObject))
        {
            try
            {
                networkObject.Spawn(true);
                if (!networkObject.IsSpawned)
                {
                    Destroy(droppedItem);
                    return false;
                }

                try
                {
                    droppedItem.GetComponent<EquippableWorldPhysics>()?.InitializeDrop(dropper);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, droppedItem);
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, droppedItem);
                if (!networkObject.IsSpawned) Destroy(droppedItem);
                return networkObject.IsSpawned;
            }
        }
        else
        {
            Debug.LogError($"Equippable item prefab '{itemToDrop.equippableItemPrefab.name}' is missing a NetworkObject component.");
            Destroy(droppedItem);
            return false;
        }
    }

    public EquippableItemSO GetEquippableItemSO()
    {
        return equippableItemSO;
    }

    public void LookedAt(Transform interactor)
    {
        OnLookAt?.Invoke(interactor, new OnLookAtEventArgs
        {
            equippableItemSO = equippableItemSO
        });
    }

    public void LookedAway(Transform interactor)
    {
        OnLookAway?.Invoke(interactor, EventArgs.Empty);
    }

    private void CompletePickup(Transform interactor)
    {
        if (interactor != null
            && (!interactor.TryGetComponent(out PlayerTemporaryStatusController statusController)
                || !statusController.BlocksActions)
            && !MountingNailSceneState.IsTargetConnected(transform)
            && (!TryGetComponent(out EquippableWorldPhysics worldPhysics) || !worldPhysics.IsReturning && !worldPhysics.IsTumbleweedCaptured)
            && interactor.TryGetComponent(out PlayerInventory playerInventory)
            && playerInventory.AddItem(equippableItemSO))
        {
            OnAnyItemEquipped?.Invoke(this, EventArgs.Empty);
            DespawnOrDestroy();
        }
    }

    private bool CompleteNetworkPickup(Transform interactor, ulong ownerClientId)
    {
        if (!IsServer || PlayerTemporaryStatusController.IsWorldActionBlockedForClient(NetworkManager, ownerClientId)
            || MountingNailSceneState.IsTargetConnected(transform)
            || (TryGetComponent(out EquippableWorldPhysics worldPhysics) && (worldPhysics.IsReturning || worldPhysics.IsTumbleweedCaptured))
            || !interactor.TryGetComponent(out PlayerInventory playerInventory)
            || !playerInventory.AddItem(equippableItemSO))
        {
            return false;
        }

        NotifyOwnerPickupClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { ownerClientId } }
        });
        DespawnOrDestroy();
        return true;
    }

    [ClientRpc]
    private void NotifyOwnerPickupClientRpc(ClientRpcParams clientRpcParams = default)
    {
        OnAnyItemEquipped?.Invoke(this, EventArgs.Empty);
    }

    private void DespawnOrDestroy()
    {
        if (IsSpawned && NetworkObject != null)
        {
            NetworkObject.Despawn(true);
            return;
        }

        Destroy(gameObject);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPickupServerRpc(ulong interactorNetworkObjectId, ServerRpcParams serverRpcParams = default)
    {
        if (NetworkManager == null || NetworkManager.SpawnManager == null
            || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(interactorNetworkObjectId, out NetworkObject interactorNetworkObject))
        {
            return;
        }

        if (interactorNetworkObject.OwnerClientId != serverRpcParams.Receive.SenderClientId
            || PlayerTemporaryStatusController.IsWorldActionBlockedForClient(NetworkManager,
                serverRpcParams.Receive.SenderClientId)
            || Vector3.Distance(interactorNetworkObject.transform.position, transform.position) > 3f
            || MountingNailSceneState.IsTargetConnected(transform)
            || (TryGetComponent(out EquippableWorldPhysics worldPhysics) && (worldPhysics.IsReturning || worldPhysics.IsTumbleweedCaptured))
            || !interactorNetworkObject.TryGetComponent(out PlayerInventory playerInventory)
            || !playerInventory.CanAddItem(equippableItemSO))
        {
            return;
        }

        CompleteNetworkPickup(interactorNetworkObject.transform, serverRpcParams.Receive.SenderClientId);
    }

    public override void OnDestroy()
    {
        OnLookAt = null;
        OnLookAway = null;
        base.OnDestroy();
    }
}
