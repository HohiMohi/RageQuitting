using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerInventoryUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private Image firstItemSprite;
    [SerializeField] private TMP_Text firstItemNameText;
    [SerializeField] private Image secondItemSprite;
    [SerializeField] private TMP_Text secondItemNameText;

    private void Awake()
    {
        EnsureReferences();
        RefreshSlots();
    }

    private void OnEnable()
    {
        EnsureReferences();
        if (playerInventory != null)
        {
            playerInventory.OnInventoryUpdated += HandleInventoryUpdated;
            playerInventory.OnInventorySlotsChanged += HandleInventorySlotsChanged;
        }

        RefreshSlots();
    }

    private void OnDisable()
    {
        if (playerInventory == null)
        {
            return;
        }

        playerInventory.OnInventoryUpdated -= HandleInventoryUpdated;
        playerInventory.OnInventorySlotsChanged -= HandleInventorySlotsChanged;
    }

    private void HandleInventoryUpdated(object sender, PlayerInventory.OnInventoryUpdateArgs e)
    {
        RefreshSlots();
    }

    private void HandleInventorySlotsChanged(object sender, System.EventArgs e)
    {
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        EnsureReferences();
        SetSlot(firstItemSprite, firstItemNameText, playerInventory != null ? playerInventory.GetItemInSlot(0) : null);
        SetSlot(secondItemSprite, secondItemNameText, playerInventory != null ? playerInventory.GetItemInSlot(1) : null);
    }

    private void EnsureReferences()
    {
        if (playerInventory == null)
        {
            playerInventory = GetComponentInParent<PlayerInventory>();
        }

        if (playerInventory == null && transform.root != null)
        {
            playerInventory = transform.root.GetComponentInChildren<PlayerInventory>(true);
        }
    }

    private static void SetSlot(Image slotImage, TMP_Text itemNameText, EquippableItemSO item)
    {
        if (slotImage != null)
        {
            slotImage.sprite = item != null ? item.uiSprite : null;
            slotImage.enabled = item != null && item.uiSprite != null;
        }

        if (itemNameText != null)
        {
            itemNameText.text = item != null ? item.itemName : string.Empty;
        }
    }
}
