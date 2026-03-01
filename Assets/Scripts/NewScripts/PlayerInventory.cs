using NUnit.Framework;
using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerInventory : MonoBehaviour
{
    private PlayerInputNew playerInputNew;
    [Header("Inventory Settings")]
    [SerializeField] private EquippableItemSO[] inventoryItems;
    [SerializeField] private int _selectedItemIndex;
    [SerializeField] private int _inventorySlots = 2;
    [SerializeField] private int _currentInventoryOccupiedSlots = 0;

    public EventHandler<OnInventoryUpdateArgs> OnInventoryUpdated;
    public class OnInventoryUpdateArgs : EventArgs
    {
        public int itemSlotIndex;
        public EquippableItemSO itemInSlot;
    };

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
        if (_inventorySlots - _currentInventoryOccupiedSlots >= item.inventorySlotsRequired)
        {
            inventoryItems[_currentInventoryOccupiedSlots] = item;
            _currentInventoryOccupiedSlots += item.inventorySlotsRequired;
            OnInventoryUpdated?.Invoke(this, new OnInventoryUpdateArgs
            {
                itemSlotIndex = _currentInventoryOccupiedSlots - item.inventorySlotsRequired,
                itemInSlot = item
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
            EquippableItem.DropItem(itemToRemove, transform.position + transform.forward); // Drop the item in front of the player
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
        }
        else
        {
            Debug.Log("Not enough items to swap.");
        }
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
}
