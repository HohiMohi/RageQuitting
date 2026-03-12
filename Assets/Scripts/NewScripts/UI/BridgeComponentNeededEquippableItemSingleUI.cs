using TMPro;
using UnityEngine;

public class BridgeComponentNeededEquippableItemSingleUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI equippableItemTypeText;
    public void SetEquippableItemTypeText(EquippableItemType equippableItemType)
    {
        equippableItemTypeText.text = equippableItemType.ToString();
    }
}
