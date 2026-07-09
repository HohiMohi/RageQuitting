using UnityEngine;
using UnityEngine.UI;

public class PlayerInventoryUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private Image firstItemSprite;
    [SerializeField] private Image secondItemSprite;

    private void Awake()
    {
        if (playerInventory == null)
        {
            playerInventory = GetComponentInParent<PlayerInventory>();
        }

        SetSlot(firstItemSprite, null);
        SetSlot(secondItemSprite, null);
    }

    private void OnEnable()
    {
        if (playerInventory == null)
        {
            return;
        }

        playerInventory.OnInventoryUpdated += PlayerInventory_OnInventoryUpdated;
    }

    private void OnDisable()
    {
        if (playerInventory == null)
        {
            return;
        }

        playerInventory.OnInventoryUpdated -= PlayerInventory_OnInventoryUpdated;
    }

    private void PlayerInventory_OnInventoryUpdated(object sender, PlayerInventory.OnInventoryUpdateArgs e)
    {
        if (e.itemSlotIndex == 0)
        {
            SetSlot(firstItemSprite, e.itemInSlot);
        }
        else if (e.itemSlotIndex == 1)
        {
            SetSlot(secondItemSprite, e.itemInSlot);
        }
    }

    private static void SetSlot(Image slotImage, EquippableItemSO item)
    {
        if (slotImage == null)
        {
            return;
        }

        slotImage.sprite = item != null ? item.uiSprite : null;
        slotImage.enabled = item != null;
    }
}

