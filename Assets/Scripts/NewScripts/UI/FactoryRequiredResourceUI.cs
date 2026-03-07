using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FactoryRequiredResourceUI : MonoBehaviour
{
    [SerializeField] private Image resourceImage;
    [SerializeField] private TextMeshProUGUI resourceNameText;
    [SerializeField] private TextMeshProUGUI resourceAmountText;

    public void SetProperties(Sprite resourceSprite, string resourceName, string resourceAmount )
    {
        resourceNameText.text = resourceName;
        resourceAmountText.text = resourceAmount;
        resourceImage.sprite = resourceSprite;
    }
}
