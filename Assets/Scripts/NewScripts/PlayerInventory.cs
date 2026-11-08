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

public struct InventorySlotsState : INetworkSerializable, IEquatable<InventorySlotsState>
{
    public int Slot0;
    public int Slot1;

    public InventorySlotsState(int slot0, int slot1) { Slot0 = slot0; Slot1 = slot1; }
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Slot0);
        serializer.SerializeValue(ref Slot1);
    }
    public bool Equals(InventorySlotsState other) => Slot0 == other.Slot0 && Slot1 == other.Slot1;
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

    private readonly NetworkVariable<InventorySlotsState> networkSlots = new NetworkVariable<InventorySlotsState>(
        new InventorySlotsState(EmptySlotItemTypeValue, EmptySlotItemTypeValue),
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
        if (IsNetworkStateActive() && !IsOwner) return;
        playerInputNew.OnSwapItems += PlayerInputNew_OnSwapItems;
        playerInputNew.OnDropItem += PlayerInputNew_OnDropItem;
    }

    public override void OnNetworkSpawn()
    {
        networkSlots.OnValueChanged += InventorySlotsNetworkValue_OnValueChanged;

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

            bool needsPublish = GetSlotItemTypeValue(0) != networkSlots.Value.Slot0 || GetSlotItemTypeValue(1) != networkSlots.Value.Slot1;
            if (needsPublish)
                SetNetworkSlotValues(GetSlotItemTypeValue(0), GetSlotItemTypeValue(1));
            else
                NotifyInventorySlotsChanged();
        }
        else
        {
            ApplyReplicatedInventory(networkSlots.Value);
            NotifyInventorySlotsChanged();
        }
    }

    public override void OnNetworkDespawn()
    {
        ClearHeldObjectCollisionOverrides(transform);
        CaptureDisconnectRecoverySnapshot();
        networkSlots.OnValueChanged -= InventorySlotsNetworkValue_OnValueChanged;
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
        disconnectSnapshotSlot0 = networkSlots.Value.Slot0;
        disconnectSnapshotSlot1 = networkSlots.Value.Slot1;
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
        if (IsNetworkStateActive() && !IsOwner
            || playerInputNew != null && playerInputNew.IsWorldActionBlocked) return;
        if (concreteTrapController != null && concreteTrapController.BlocksGameplayInput) return;
        RemoveItem();
    }

    private void PlayerInputNew_OnSwapItems(object sender, EventArgs e)
    {
        if (IsNetworkStateActive() && !IsOwner
            || playerInputNew != null && playerInputNew.IsWorldActionBlocked) return;
        if (concreteTrapController != null && concreteTrapController.BlocksGameplayInput) return;
        SwapItems();
    }

    public bool AddItem(EquippableItemSO item)
    {
        if (IsNetworkStateActive() && !IsServer)
        {
            Debug.LogWarning("PlayerInventory: Network inventory additions are server-authoritative.");
            return false;
        }

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
        if (IsNetworkStateActive() && !IsServer)
        {
            InventorySlotsState expected = networkSlots.Value;
            RequestInventoryActionServerRpc(0, expected.Slot0, expected.Slot1);
            return true;
        }
        return RemoveSelectedItemAndDrop();
    }

    private void SwapItems()
    {
        if (IsNetworkStateActive() && !IsServer)
        {
            InventorySlotsState expected = networkSlots.Value;
            RequestInventoryActionServerRpc(1, expected.Slot0, expected.Slot1);
            return;
        }
        SwapItemsAuthority();
    }

    private bool RemoveSelectedItemAndDrop()
    {
        if (_currentInventoryOccupiedSlots <= 0 || inventoryItems[0] == null)
            return false;
        EquippableItemSO item = inventoryItems[0];
        NetworkObject dropper = NetworkObject;
        bool spawned = IsNetworkStateActive()
            ? EquippableItem.SpawnNetworkedDrop(item, dropper)
            : TryDropOffline(item, dropper);
        if (!spawned) return false;

        inventoryItems[0] = inventoryItems.Length > 1 ? inventoryItems[1] : null;
        if (inventoryItems.Length > 1) inventoryItems[1] = null;
        RecountInventory();
        NotifyInventoryContentChanged(0);
        if (inventoryItems.Length > 1) NotifyInventoryContentChanged(1);
        PublishInventorySlotsChanged();
        return true;
    }

    private bool TryDropOffline(EquippableItemSO item, NetworkObject dropper)
    {
        if (item == null || item.equippableItemPrefab == null || dropper == null) return false;
        return EquippableItem.DropItem(item, dropper);
    }

    private void SwapItemsAuthority()
    {
        if (inventoryItems.Length > 1 && _currentInventoryOccupiedSlots > 1 && inventoryItems[0] != null && inventoryItems[1] != null)
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

    private void RecountInventory()
    {
        _currentInventoryOccupiedSlots = 0;
        for (int i = 0; i < inventoryItems.Length; i++)
            if (inventoryItems[i] != null) _currentInventoryOccupiedSlots += inventoryItems[i].inventorySlotsRequired;
        CalculateInventoryMovementSpeedPenalty();
        OnSelectedItemChanged?.Invoke(this, new OnSelectedItemChangedEventArgs { selectedItem = GetCurrentSelectedItem() });
        MovementSpeedPenaltyUpdated?.Invoke(this, new MovementSpeedPenaltyUpdatedEventArgs
            { currentMovementSpeedPenaltyMultiplier = inventoryMovementSpeedPenalty });
    }

    private void NotifyInventoryContentChanged(int slot)
    {
        OnInventoryUpdated?.Invoke(this, new OnInventoryUpdateArgs { itemSlotIndex = slot, itemInSlot = inventoryItems[slot] });
    }

    internal bool DropAllInventoryForAuthority()
    {
        if (IsNetworkStateActive() && !IsServer) return false;
        bool changed = false;
        NetworkObject dropper = NetworkObject;
        EquippableItemSO[] survivors = new EquippableItemSO[inventoryItems.Length];
        int survivorCount = 0;
        for (int i = 0; i < inventoryItems.Length; i++)
        {
            EquippableItemSO item = inventoryItems[i];
            if (item == null) continue;
            bool dropped = IsNetworkStateActive()
                ? EquippableItem.SpawnNetworkedDrop(item, dropper)
                : TryDropOffline(item, dropper);
            if (dropped) changed = true;
            else survivors[survivorCount++] = item;
        }
        if (changed)
        {
            Array.Clear(inventoryItems, 0, inventoryItems.Length);
            Array.Copy(survivors, inventoryItems, survivorCount);
            RecountInventory();
            NotifyInventoryContentChanged(0);
            if (inventoryItems.Length > 1) NotifyInventoryContentChanged(1);
            PublishInventorySlotsChanged();
        }
        return changed;
    }

    [ServerRpc]
    private void RequestInventoryActionServerRpc(int action, int expectedSlot0, int expectedSlot1, ServerRpcParams rpcParams = default)
    {
        if (PlayerTemporaryStatusController.IsWorldActionBlockedForClient(NetworkManager,
                rpcParams.Receive.SenderClientId)
            || !IsRequestingClientOwner(rpcParams.Receive.SenderClientId)
            || networkSlots.Value.Slot0 != expectedSlot0 || networkSlots.Value.Slot1 != expectedSlot1) return;
        if (action == 0) RemoveSelectedItemAndDrop();
        else if (action == 1) SwapItemsAuthority();
    }

    private bool IsRequestingClientOwner(ulong clientId)
    {
        return IsServer && NetworkManager != null
            && NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
            && client.PlayerObject == NetworkObject;
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
        return item != null && item.inventorySlotsRequired > 0
            && _inventorySlots - _currentInventoryOccupiedSlots >= item.inventorySlotsRequired;
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

        int networkValue = slotIndex == 0 ? networkSlots.Value.Slot0 : networkSlots.Value.Slot1;
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
            return slotIndex == 0 ? networkSlots.Value.Slot0 : networkSlots.Value.Slot1;
        }

        return GetSlotItemTypeValue(slotIndex);
    }

    public bool IsNetworkStateActive()
    {
        return NetworkManager != null && NetworkManager.IsListening && IsSpawned;
    }

    public EquippableItemSO GetEquippableItemSO(EquippableItemType itemType)
    {
        if (equippableItemCatalog == null) return null;
        foreach (EquippableItemSO item in equippableItemCatalog)
        {
            if (item != null && item.itemType == itemType)
            {
                return item;
            }
        }

        return null;
    }

    private void InventorySlotsNetworkValue_OnValueChanged(InventorySlotsState previousValue, InventorySlotsState newValue)
    {
        if (!IsServer)
            ApplyReplicatedInventory(newValue);
        NotifyInventorySlotsChanged();
    }

    private void ApplyReplicatedInventory(InventorySlotsState state)
    {
        int old0 = GetSlotItemTypeValue(0);
        int old1 = GetSlotItemTypeValue(1);
        inventoryItems[0] = ResolveNetworkItem(state.Slot0);
        if (inventoryItems.Length > 1) inventoryItems[1] = ResolveNetworkItem(state.Slot1);
        RecountInventory();
        if (old0 != state.Slot0) NotifyInventoryContentChanged(0);
        if (inventoryItems.Length > 1 && old1 != state.Slot1) NotifyInventoryContentChanged(1);
    }

    private EquippableItemSO ResolveNetworkItem(int value)
    {
        return value >= 0 ? GetEquippableItemSO((EquippableItemType)value) : null;
    }

    private void PublishInventorySlotsChanged()
    {
        if (!IsNetworkStateActive())
        {
            NotifyInventorySlotsChanged();
            return;
        }

        if (IsServer)
        {
            int slot0 = GetSlotItemTypeValue(0);
            int slot1 = GetSlotItemTypeValue(1);
            if (slot0 == networkSlots.Value.Slot0 && slot1 == networkSlots.Value.Slot1)
                NotifyInventorySlotsChanged();
            else
                SetNetworkSlotValues(slot0, slot1);
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
        if (IsServer)
            networkSlots.Value = new InventorySlotsState(slot0Value, slot1Value);
    }
}
