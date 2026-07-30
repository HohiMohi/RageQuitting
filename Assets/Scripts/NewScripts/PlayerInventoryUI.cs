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
    [SerializeField] private string reservedSlotLabel = "TWO-HANDED";
    [SerializeField] private Color occupiedSlotTextColor = Color.white;
    [SerializeField] private Color reservedSlotTextColor = new Color(0.55f, 0.58f, 0.6f, 1f);

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
        SetSlot(
            firstItemSprite,
            firstItemNameText,
            playerInventory != null ? playerInventory.GetItemInSlot(0) : null,
            playerInventory != null ? playerInventory.GetSlotState(0) : InventorySlotState.Empty);
        SetSlot(
            secondItemSprite,
            secondItemNameText,
            playerInventory != null ? playerInventory.GetItemInSlot(1) : null,
            playerInventory != null ? playerInventory.GetSlotState(1) : InventorySlotState.Empty);
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

    private void SetSlot(
        Image slotImage,
        TMP_Text itemNameText,
        EquippableItemSO item,
        InventorySlotState slotState)
    {
        if (slotImage != null)
        {
            bool showItem = slotState == InventorySlotState.Occupied && item != null;
            slotImage.sprite = showItem ? item.uiSprite : null;
            slotImage.enabled = showItem && item.uiSprite != null;
        }

        if (itemNameText != null)
        {
            itemNameText.text = slotState == InventorySlotState.Reserved
                ? reservedSlotLabel
                : item != null ? item.itemName : string.Empty;
            itemNameText.color = slotState == InventorySlotState.Reserved
                ? reservedSlotTextColor
                : occupiedSlotTextColor;
        }
    }
}
