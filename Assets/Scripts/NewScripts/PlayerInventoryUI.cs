using UnityEngine;
using UnityEngine.UI;

public class PlayerInventoryUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private Image firstItemSprite;
    [SerializeField] private Image secondItemSprite;

    private void Start()
    {
        playerInventory.OnInventoryUpdated += PlayerInventory_OnInventoryUpdated;
    }

    private void PlayerInventory_OnInventoryUpdated(object sender, PlayerInventory.OnInventoryUpdateArgs e)
    {
        if (e.itemSlotIndex == 0)
        {
            firstItemSprite.sprite = e.itemInSlot != null ? e.itemInSlot.uiSprite : null;
            firstItemSprite.enabled = e.itemInSlot != null; // Enable the image only if there's an item
        }
        else if (e.itemSlotIndex == 1)
        {
            secondItemSprite.sprite = e.itemInSlot != null ? e.itemInSlot.uiSprite : null;
            secondItemSprite.enabled = e.itemInSlot != null; // Enable the image only if there's an item
        }
    }
}

