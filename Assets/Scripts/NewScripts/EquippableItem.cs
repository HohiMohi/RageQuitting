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
        if (!interactor.TryGetComponent(out PlayerInventory playerInventory) || !playerInventory.CanAddItem(equippableItemSO))
        {
            return;
        }

        OnLookAway?.Invoke(interactor, EventArgs.Empty);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            if (IsServer)
            {
                CompletePickup(interactor);
            }
            else if (interactor.TryGetComponent(out NetworkObject interactorNetworkObject))
            {
                RequestPickupServerRpc(interactorNetworkObject.NetworkObjectId);
            }

            return;
        }

        CompletePickup(interactor);
    }

    public static void DropItem(EquippableItemSO itemToDrop, Vector3 dropPosition)
    {
        Instantiate(itemToDrop.equippableItemPrefab, dropPosition, Quaternion.identity);
    }

    public static void SpawnNetworkedDrop(EquippableItemSO itemToDrop, Vector3 dropPosition, Quaternion dropRotation)
    {
        GameObject droppedItem = Instantiate(itemToDrop.equippableItemPrefab, dropPosition, dropRotation);

        if (droppedItem.TryGetComponent(out NetworkObject networkObject))
        {
            networkObject.Spawn(true);
        }
        else
        {
            Debug.LogError($"Equippable item prefab '{itemToDrop.equippableItemPrefab.name}' is missing a NetworkObject component.");
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
        if (interactor.TryGetComponent(out PlayerInventory playerInventory) && playerInventory.AddItem(equippableItemSO))
        {
            OnAnyItemEquipped?.Invoke(this, EventArgs.Empty);
            DespawnOrDestroy();
        }
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
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(interactorNetworkObjectId, out NetworkObject interactorNetworkObject))
        {
            return;
        }

        var targetClientIds = new[] { serverRpcParams.Receive.SenderClientId };
        ConfirmPickupClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = targetClientIds
            }
        });

        DespawnOrDestroy();
    }

    [ClientRpc]
    private void ConfirmPickupClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient?.PlayerObject == null)
        {
            return;
        }

        if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInventory playerInventory) && playerInventory.AddItem(equippableItemSO))
        {
            OnAnyItemEquipped?.Invoke(this, EventArgs.Empty);
        }
    }

    public override void OnDestroy()
    {
        OnLookAt = null;
        OnLookAway = null;
        base.OnDestroy();
    }
}
