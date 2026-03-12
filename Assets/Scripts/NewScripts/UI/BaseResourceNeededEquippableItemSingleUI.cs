using TMPro;
using UnityEngine;

public class BaseResourceNeededEquippableItemSingleUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI equippableItemTypeText;
    public void SetEquippableItemTypeText(EquippableItemType equippableItemType)
    {
        equippableItemTypeText.text = equippableItemType.ToString();
    }
}
