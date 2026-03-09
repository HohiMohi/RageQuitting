using System;
using TMPro;
using UnityEngine;

public class EquippableItemInfoUI : MonoBehaviour
{
    [SerializeField] private EquippableItemUI equippableItemUI;

    [SerializeField] private TextMeshProUGUI equippableItemNameText;
    [SerializeField] private TextMeshProUGUI itemTypeText;
    [SerializeField] private TextMeshProUGUI actionRangeText;
    [SerializeField] private TextMeshProUGUI actionCooldownText;
    [SerializeField] private TextMeshProUGUI damageText;
    [SerializeField] private TextMeshProUGUI movementSpeedPenaltyText;
    [SerializeField] private TextMeshProUGUI actionRepeatabilityText;
    [SerializeField] private TextMeshProUGUI inventorySlotsRequiredText;
    private bool isTextSetted = false;

    private void Start()
    {
        equippableItemUI.EquippableItemUI_ShowUI += ShowUI;
        equippableItemUI.EquippableItemUI_HideUI += HideUI;
        Hide();
    }

    private void ShowUI(object sender, EquippableItemUI.ShowUIEventArgs e)
    {
        if (!isTextSetted)
        {
            SetItemInfoTexts(e.equippableItemSO);
            isTextSetted = true;
        }
        Show();
    }

    private void HideUI(object sender, EventArgs e)
    {
        Hide();
    }



    public void SetItemInfoTexts(EquippableItemSO equippableItemSO)
    {
        equippableItemNameText.text = equippableItemSO.itemName;
        itemTypeText.text = "Item Type: " + equippableItemSO.itemType.ToString();
        actionRangeText.text = "Action Range: " + equippableItemSO.actionRange.ToString();
        actionCooldownText.text = "Action Cooldown: " + equippableItemSO.actionCooldown.ToString();
        damageText.text = "Damage: " + equippableItemSO.damage.ToString();
        movementSpeedPenaltyText.text = "MovementSpeedPenalty: " + equippableItemSO.movementSpeedPenalty.ToString();
        actionRepeatabilityText.text = "Action Repeatability: " + equippableItemSO.actionRepeatability.ToString();
        inventorySlotsRequiredText.text = "Required Inventory Slots: " + equippableItemSO.inventorySlotsRequired.ToString();

    }
    public void Hide()
    {
        gameObject.SetActive(false);
    }
    public void Show()
    {
        gameObject.SetActive(true);
    }
}
