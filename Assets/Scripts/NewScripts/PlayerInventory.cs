using NUnit.Framework;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

public enum InventorySlotState
{
    Empty,
    Occupied,
    Reserved
}

public class PlayerInventory : NetworkBehaviour
{
    private const int ReservedSlotItemTypeValue = -2;
    private const int EmptySlotItemTypeValue = -1;

    private PlayerInputNew playerInputNew;
    private PlayerConcreteTrapController concreteTrapController;
    private NetworkManager disconnectRecoveryManager;
    private bool isServerPlayerObject;
    private bool hasDisconnectRecoverySnapshot;
    private bool disconnectRecoveryProcessed;
    private int disconnectSnapshotFrame;
    private ulong disconnectSnapshotClientId;
    private Vector3 disconnectSnapshotPosition;
    private Quaternion disconnectSnapshotRotation;
    private int disconnectSnapshotSlot0 = EmptySlotItemTypeValue;
    private int disconnectSnapshotSlot1 = EmptySlotItemTypeValue;
    [Header("Inventory Settings")]
    [SerializeField] private EquippableItemSO[] inventoryItems;
    [SerializeField] private EquippableItemSO[] equippableItemCatalog;
    [SerializeField] private int _selectedItemIndex;
    [SerializeField] private int _inventorySlots = 2;
    [SerializeField] private int _currentInventoryOccupiedSlots = 0;
    private float inventoryMovementSpeedPenalty = 0;

    private readonly NetworkVariable<int> slot0ItemType = new NetworkVariable<int>(
        EmptySlotItemTypeValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> slot1ItemType = new NetworkVariable<int>(
        EmptySlotItemTypeValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public event EventHandler OnInventorySlotsChanged;
    public EventHandler<OnInventoryUpdateArgs> OnInventoryUpdated;
    public class OnInventoryUpdateArgs : EventArgs
    {
        public int itemSlotIndex;
        public EquippableItemSO itemInSlot;
    };

    public EventHandler<OnSelectedItemChangedEventArgs> OnSelectedItemChanged;
    public class OnSelectedItemChangedEventArgs : EventArgs
    {
        public EquippableItemSO selectedItem;
    }

    public EventHandler<MovementSpeedPenaltyUpdatedEventArgs> MovementSpeedPenaltyUpdated;
    public class MovementSpeedPenaltyUpdatedEventArgs : EventArgs
    {
        public float currentMovementSpeedPenaltyMultiplier;
    }

    private void Awake()
    {
        playerInputNew = GetComponent<PlayerInputNew>();
        concreteTrapController = GetComponent<PlayerConcreteTrapController>();
        EnsureInventoryStorage();
        _selectedItemIndex = 0;
    }
    private void Start()
    {
        playerInputNew.OnSwapItems += PlayerInputNew_OnSwapItems;
        playerInputNew.OnDropItem += PlayerInputNew_OnDropItem;
    }

    public override void OnNetworkSpawn()
    {
        slot0ItemType.OnValueChanged += InventorySlotNetworkValue_OnValueChanged;
        slot1ItemType.OnValueChanged += InventorySlotNetworkValue_OnValueChanged;

        UnsubscribeDisconnectRecovery();
        disconnectRecoveryManager = null;
        isServerPlayerObject = false;
        hasDisconnectRecoverySnapshot = false;
        disconnectRecoveryProcessed = false;

        if (IsServer)
        {
            disconnectRecoveryManager = NetworkManager;
            isServerPlayerObject = NetworkObject != null && NetworkObject.IsPlayerObject;
            if (isServerPlayerObject && disconnectRecoveryManager != null)
            {
                disconnectRecoveryManager.OnClientDisconnectCallback -= NetworkManager_OnClientDisconnectCallback;
                disconnectRecoveryManager.OnClientDisconnectCallback += NetworkManager_OnClientDisconnectCallback;
            }

            SetNetworkSlotValues(GetSlotItemTypeValue(0), GetSlotItemTypeValue(1));
        }

        NotifyInventorySlotsChanged();
    }

    public override void OnNetworkDespawn()
    {
        ClearHeldObjectCollisionOverrides(transform);
        CaptureDisconnectRecoverySnapshot();
        slot0ItemType.OnValueChanged -= InventorySlotNetworkValue_OnValueChanged;
        slot1ItemType.OnValueChanged -= InventorySlotNetworkValue_OnValueChanged;
    }

    private void OnDestroy()
    {
        if (disconnectRecoveryManager != null)
        {
            disconnectRecoveryManager.OnClientDisconnectCallback -= NetworkManager_OnClientDisconnectCallback;
            disconnectRecoveryManager = null;
        }

        if (playerInputNew == null)
        {
            return;
        }

        playerInputNew.OnSwapItems -= PlayerInputNew_OnSwapItems;
        playerInputNew.OnDropItem -= PlayerInputNew_OnDropItem;
    }

    private void CaptureDisconnectRecoverySnapshot()
    {
        NetworkManager manager = disconnectRecoveryManager;
        if (!isServerPlayerObject || !IsServer || manager == null || !manager.IsListening
            || manager.ShutdownInProgress || NetworkObject == null)
        {
            return;
        }

        hasDisconnectRecoverySnapshot = true;
        disconnectRecoveryProcessed = false;
        disconnectSnapshotFrame = Time.frameCount;
        disconnectSnapshotClientId = NetworkObject.OwnerClientId;
        disconnectSnapshotPosition = transform.position;
        disconnectSnapshotRotation = transform.rotation;
        disconnectSnapshotSlot0 = slot0ItemType.Value;
        disconnectSnapshotSlot1 = slot1ItemType.Value;
    }

    private void NetworkManager_OnClientDisconnectCallback(ulong clientId)
    {
        NetworkManager manager = disconnectRecoveryManager;
        if (!hasDisconnectRecoverySnapshot || disconnectRecoveryProcessed
            || clientId != disconnectSnapshotClientId || disconnectSnapshotFrame != Time.frameCount
            || !isServerPlayerObject || manager == null || !manager.IsServer || !manager.IsListening
            || manager.ShutdownInProgress || manager.ConnectedClients.ContainsKey(clientId))
        {
            return;
        }

        disconnectRecoveryProcessed = true;
        hasDisconnectRecoverySnapshot = false;
        UnsubscribeDisconnectRecovery();
        ReleaseDisconnectedPlayerCarry(manager, clientId, transform);
        DropDisconnectedPlayerInventory(manager, clientId);
    }

    protected void LateUpdate()
    {
        if (hasDisconnectRecoverySnapshot && disconnectSnapshotFrame != Time.frameCount)
        {
            hasDisconnectRecoverySnapshot = false;
            UnsubscribeDisconnectRecovery();
        }
    }

    private void UnsubscribeDisconnectRecovery()
    {
        if (disconnectRecoveryManager != null)
        {
            disconnectRecoveryManager.OnClientDisconnectCallback -= NetworkManager_OnClientDisconnectCallback;
        }
    }

    private void ClearHeldObjectCollisionOverrides(Transform holderRoot)
    {
        NetworkManager manager = NetworkManager;
        if (manager == null || manager.SpawnManager == null || holderRoot == null)
        {
            return;
        }

        foreach (NetworkObject spawnedObject in manager.SpawnManager.SpawnedObjectsList)
        {
            if (spawnedObject == null)
            {
                continue;
            }

            if (spawnedObject.TryGetComponent(out BaseResourceNew resource))
            {
                resource.ClearHolderCollisionOverride(holderRoot);
            }
            else if (spawnedObject.TryGetComponent(out MountableBridgeComponent mountable))
            {
                mountable.ClearHolderCollisionOverride(holderRoot);
            }
        }
    }

    private static void ReleaseDisconnectedPlayerCarry(NetworkManager manager, ulong clientId, Transform releasedHolderRoot)
    {
        if (manager.SpawnManager == null)
        {
            return;
        }

        foreach (NetworkObject spawnedObject in manager.SpawnManager.SpawnedObjectsList)
        {
            if (spawnedObject == null)
            {
                continue;
            }

            if (spawnedObject.TryGetComponent(out BaseResourceNew resource))
            {
                resource.TryReleaseDisconnectedHolder(clientId, releasedHolderRoot);
            }
            else if (spawnedObject.TryGetComponent(out MountableBridgeComponent mountable))
            {
                mountable.TryReleaseDisconnectedHolder(clientId, releasedHolderRoot);
            }
        }
    }

    private void DropDisconnectedPlayerInventory(NetworkManager manager, ulong clientId)
    {
        if (!manager.IsServer || manager.ShutdownInProgress || NetworkObject == null)
        {
            return;
        }

        SpawnDisconnectedInventorySlot(disconnectSnapshotSlot0, clientId);
        SpawnDisconnectedInventorySlot(disconnectSnapshotSlot1, clientId);
        Physics.SyncTransforms();
    }

    private void SpawnDisconnectedInventorySlot(int itemTypeValue, ulong clientId)
    {
        if (itemTypeValue < 0)
        {
            return;
        }

        EquippableItemSO item = GetEquippableItemSO((EquippableItemType)itemTypeValue);
        if (item == null)
        {
            Debug.LogWarning($"PlayerInventory: Could not recover inventory item type {itemTypeValue} for disconnected client {clientId}.");
            return;
        }

        EquippableItem.SpawnNetworkedDrop(item, NetworkObject,
            disconnectSnapshotPosition + Vector3.up, disconnectSnapshotRotation);
    }

    private void PlayerInputNew_OnDropItem(object sender, EventArgs e)
    {
        if (concreteTrapController != null && concreteTrapController.BlocksGameplayInput) return;
        RemoveItem();
    }

    private void PlayerInputNew_OnSwapItems(object sender, EventArgs e)
    {
        if (concreteTrapController != null && concreteTrapController.BlocksGameplayInput) return;
        SwapItems();
    }

    public bool AddItem(EquippableItemSO item)
    {
        if (CanAddItem(item))
        {
            inventoryItems[_currentInventoryOccupiedSlots] = item;
            _currentInventoryOccupiedSlots += item.inventorySlotsRequired;
            OnInventoryUpdated?.Invoke(this, new OnInventoryUpdateArgs
            {
                itemSlotIndex = _currentInventoryOccupiedSlots - item.inventorySlotsRequired,
                itemInSlot = item
            });
            OnSelectedItemChanged?.Invoke(this, new OnSelectedItemChangedEventArgs
            {
                selectedItem = GetCurrentSelectedItem()
            });
            CalculateInventoryMovementSpeedPenalty();
            MovementSpeedPenaltyUpdated?.Invoke(this, new MovementSpeedPenaltyUpdatedEventArgs
            {
                currentMovementSpeedPenaltyMultiplier = inventoryMovementSpeedPenalty
            });
            PublishInventorySlotsChanged();
            return true; // Item added successfully
        }
        else
        {
            Debug.Log("Inventory is full. Cannot add item.");
            return false; // Inventory is full
        }
    }

    private bool RemoveItem()
    {
        if (_currentInventoryOccupiedSlots > 0)
        {
            _currentInventoryOccupiedSlots -= inventoryItems[_selectedItemIndex].inventorySlotsRequired;
            EquippableItemSO itemToRemove = inventoryItems[_selectedItemIndex];
            inventoryItems[_selectedItemIndex] = inventoryItems[1]; // Assign the second item to the first slot
            DropItemInWorld(itemToRemove);
            OnInventoryUpdated?.Invoke(this, new OnInventoryUpdateArgs
            {
                itemSlotIndex = 0,
                itemInSlot = inventoryItems[0]
            });
            if (_currentInventoryOccupiedSlots > 0)
            {
                inventoryItems[1] = null; // Clear the second slot
                OnInventoryUpdated?.Invoke(this, new OnInventoryUpdateArgs
                {
                    itemSlotIndex = 1,
                    itemInSlot = null
                });
            }
            OnSelectedItemChanged?.Invoke(this, new OnSelectedItemChangedEventArgs
            {
                selectedItem = GetCurrentSelectedItem()
            });
            CalculateInventoryMovementSpeedPenalty();
            MovementSpeedPenaltyUpdated?.Invoke(this, new MovementSpeedPenaltyUpdatedEventArgs
            {
                currentMovementSpeedPenaltyMultiplier = inventoryMovementSpeedPenalty
            });
            PublishInventorySlotsChanged();
            return true; // Item removed successfully
        }
        else
        {
            Debug.Log("Invalid item index. Cannot remove item.");
            return false; // Invalid index
        }
    }

    private void SwapItems()
    {
        if (_currentInventoryOccupiedSlots > 1 && inventoryItems[0] != null && inventoryItems[1] != null)
        {
            // Swap the items in the inventory
            EquippableItemSO temp = inventoryItems[0];
            inventoryItems[0] = inventoryItems[1];
            inventoryItems[1] = temp;
            // Notify UI about the swap
            OnInventoryUpdated?.Invoke(this, new OnInventoryUpdateArgs
            {
                itemSlotIndex = 0,
                itemInSlot = inventoryItems[0]
            });
            OnInventoryUpdated?.Invoke(this, new OnInventoryUpdateArgs
            {
                itemSlotIndex = 1,
                itemInSlot = inventoryItems[1]
            });
            OnSelectedItemChanged?.Invoke(this, new OnSelectedItemChangedEventArgs
            {
                selectedItem = GetCurrentSelectedItem()
            });
            PublishInventorySlotsChanged();
        }
        else
        {
            Debug.Log("Not enough items to swap.");
        }
    }
    public void CalculateInventoryMovementSpeedPenalty()
    {
        float movementSpeedPenalty = 0;
        for (int i = 0; i < inventoryItems.Length; i++)
        {
            if (inventoryItems[i] != null)
            {
                movementSpeedPenalty += inventoryItems[i].movementSpeedPenalty;
            }
        }
        inventoryMovementSpeedPenalty = movementSpeedPenalty;
    }

    public bool CanAddItem(EquippableItemSO item)
    {
        return item != null && _inventorySlots - _currentInventoryOccupiedSlots >= item.inventorySlotsRequired;
    }

    public EquippableItemSO GetCurrentSelectedItem()
    {
        if (_currentInventoryOccupiedSlots > 0)
        {
            return inventoryItems[_selectedItemIndex];
        }
        else
        {
            return null; // No items in inventory
        }
    }

    public EquippableItemSO GetSelectedItemForServerValidation()
    {
        if (IsNetworkStateActive() && IsServer)
        {
            int itemTypeValue = GetNetworkSlotItemTypeValue(0);
            return itemTypeValue == EmptySlotItemTypeValue
                ? null
                : GetEquippableItemSO((EquippableItemType)itemTypeValue);
        }

        return GetCurrentSelectedItem();
    }

    public EquippableItemSO GetItemInSlot(int slotIndex)
    {
        EnsureInventoryStorage();
        if (slotIndex < 0 || slotIndex >= inventoryItems.Length)
        {
            return null;
        }

        return inventoryItems[slotIndex];
    }

    public InventorySlotState GetSlotState(int slotIndex)
    {
        EnsureInventoryStorage();
        if (slotIndex < 0 || slotIndex >= _inventorySlots)
        {
            return InventorySlotState.Empty;
        }

        if (!IsNetworkStateActive() || IsOwner)
        {
            if (inventoryItems[slotIndex] != null)
            {
                return InventorySlotState.Occupied;
            }

            EquippableItemSO primaryItem = inventoryItems.Length > 0 ? inventoryItems[0] : null;
            return slotIndex > 0 &&
                   primaryItem != null &&
                   primaryItem.inventorySlotsRequired > slotIndex
                ? InventorySlotState.Reserved
                : InventorySlotState.Empty;
        }

        int networkValue = slotIndex == 0 ? slot0ItemType.Value : slot1ItemType.Value;
        if (networkValue == ReservedSlotItemTypeValue)
        {
            return InventorySlotState.Reserved;
        }

        return networkValue == EmptySlotItemTypeValue
            ? InventorySlotState.Empty
            : InventorySlotState.Occupied;
    }

    public bool IsSlotReserved(int slotIndex)
    {
        return GetSlotState(slotIndex) == InventorySlotState.Reserved;
    }

    public int GetNetworkSlotItemTypeValue(int slotIndex)
    {
        if (IsNetworkStateActive())
        {
            return slotIndex == 0 ? slot0ItemType.Value : slot1ItemType.Value;
        }

        return GetSlotItemTypeValue(slotIndex);
    }

    public bool IsNetworkStateActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }

    private void DropItemInWorld(EquippableItemSO itemToDrop)
    {
        NetworkObject dropper = GetComponent<NetworkObject>();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                EquippableItem.SpawnNetworkedDrop(itemToDrop, dropper);
            }
            else
            {
                DropItemServerRpc((int)itemToDrop.itemType);
            }

            return;
        }

        EquippableItem.DropItem(itemToDrop, dropper);
    }

    [ServerRpc]
    private void DropItemServerRpc(int itemTypeValue, ServerRpcParams rpcParams = default)
    {
        EquippableItemSO itemToDrop = GetEquippableItemSO((EquippableItemType)itemTypeValue);
        if (itemToDrop == null)
        {
            Debug.LogWarning($"PlayerInventory: Could not find equippable item for type {(EquippableItemType)itemTypeValue}.");
            return;
        }

        if (NetworkManager.Singleton == null
            || !NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client)
            || client.PlayerObject == null
            || client.PlayerObject != NetworkObject)
        {
            return;
        }

        EquippableItem.SpawnNetworkedDrop(itemToDrop, client.PlayerObject);
    }

    public EquippableItemSO GetEquippableItemSO(EquippableItemType itemType)
    {
        foreach (EquippableItemSO item in equippableItemCatalog)
        {
            if (item != null && item.itemType == itemType)
            {
                return item;
            }
        }

        return null;
    }

    private void InventorySlotNetworkValue_OnValueChanged(int previousValue, int newValue)
    {
        NotifyInventorySlotsChanged();
    }

    private void PublishInventorySlotsChanged()
    {
        NotifyInventorySlotsChanged();

        if (!IsNetworkStateActive())
        {
            return;
        }

        int slot0Value = GetSlotItemTypeValue(0);
        int slot1Value = GetSlotItemTypeValue(1);

        if (IsServer)
        {
            SetNetworkSlotValues(slot0Value, slot1Value);
        }
        else if (IsOwner)
        {
            PublishInventorySlotsServerRpc(slot0Value, slot1Value);
        }
    }

    private void NotifyInventorySlotsChanged()
    {
        OnInventorySlotsChanged?.Invoke(this, EventArgs.Empty);
    }

    private int GetSlotItemTypeValue(int slotIndex)
    {
        EquippableItemSO item = GetItemInSlot(slotIndex);
        if (item != null)
        {
            return (int)item.itemType;
        }

        return IsLocalSlotReserved(slotIndex)
            ? ReservedSlotItemTypeValue
            : EmptySlotItemTypeValue;
    }

    private bool IsLocalSlotReserved(int slotIndex)
    {
        EnsureInventoryStorage();
        if (slotIndex <= 0 || slotIndex >= inventoryItems.Length)
        {
            return false;
        }

        EquippableItemSO primaryItem = inventoryItems[0];
        return primaryItem != null && primaryItem.inventorySlotsRequired > slotIndex;
    }

    private void EnsureInventoryStorage()
    {
        int slotCount = Mathf.Max(1, _inventorySlots);
        if (inventoryItems == null)
        {
            inventoryItems = new EquippableItemSO[slotCount];
            return;
        }

        if (inventoryItems.Length != slotCount)
        {
            Array.Resize(ref inventoryItems, slotCount);
        }
    }

    private void SetNetworkSlotValues(int slot0Value, int slot1Value)
    {
        slot0ItemType.Value = slot0Value;
        slot1ItemType.Value = slot1Value;
    }

    [ServerRpc]
    private void PublishInventorySlotsServerRpc(int slot0Value, int slot1Value)
    {
        SetNetworkSlotValues(slot0Value, slot1Value);
    }
}
