using NUnit.Framework;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerInventory : NetworkBehaviour
{
    private PlayerInputNew playerInputNew;
    [Header("Inventory Settings")]
    [SerializeField] private EquippableItemSO[] inventoryItems;
    [SerializeField] private EquippableItemSO[] equippableItemCatalog;
    [SerializeField] private int _selectedItemIndex;
    [SerializeField] private int _inventorySlots = 2;
    [SerializeField] private int _currentInventoryOccupiedSlots = 0;
    private float inventoryMovementSpeedPenalty = 0;


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
        inventoryItems = new EquippableItemSO[_inventorySlots];
        _selectedItemIndex = 0;
    }
    private void Start()
    {
        playerInputNew.OnSwapItems += PlayerInputNew_OnSwapItems;
        playerInputNew.OnDropItem += PlayerInputNew_OnDropItem;
    }

    private void PlayerInputNew_OnDropItem(object sender, EventArgs e)
    {
        RemoveItem();
    }

    private void PlayerInputNew_OnSwapItems(object sender, EventArgs e)
    {
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
            DropItemInWorld(itemToRemove, transform.position + transform.forward, transform.rotation);
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
            Debug.Log("No items in inventory.");
            return null; // No items in inventory
        }
    }

    private void DropItemInWorld(EquippableItemSO itemToDrop, Vector3 dropPosition, Quaternion dropRotation)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                EquippableItem.SpawnNetworkedDrop(itemToDrop, dropPosition, dropRotation);
            }
            else
            {
                DropItemServerRpc((int)itemToDrop.itemType, dropPosition, dropRotation);
            }

            return;
        }

        EquippableItem.DropItem(itemToDrop, dropPosition);
    }

    [ServerRpc]
    private void DropItemServerRpc(int itemTypeValue, Vector3 dropPosition, Quaternion dropRotation)
    {
        EquippableItemSO itemToDrop = GetEquippableItemSO((EquippableItemType)itemTypeValue);
        if (itemToDrop == null)
        {
            Debug.LogWarning($"PlayerInventory: Could not find equippable item for type {(EquippableItemType)itemTypeValue}.");
            return;
        }

        EquippableItem.SpawnNetworkedDrop(itemToDrop, dropPosition, dropRotation);
    }

    private EquippableItemSO GetEquippableItemSO(EquippableItemType itemType)
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
}
