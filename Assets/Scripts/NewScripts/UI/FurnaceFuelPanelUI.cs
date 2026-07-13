using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FurnaceFuelPanelUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI fuelText;

    public void Refresh(FurnaceStorage furnaceStorage)
    {
        EnsureText();
        if (furnaceStorage == null)
        {
            fuelText.text = "Fuel: -";
            return;
        }

        fuelText.text = $"Fuel: {furnaceStorage.CurrentFuel:0.##}";
    }

    public static FurnaceFuelPanelUI CreateRuntimePanel(Transform parent)
    {
        GameObject panelObject = new GameObject("FurnaceFuelPanel", typeof(RectTransform));
        panelObject.transform.SetParent(parent, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 0.5f);
        panelRect.anchorMax = new Vector2(1f, 0.5f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.anchoredPosition = new Vector2(-28f, -8f);
        panelRect.sizeDelta = new Vector2(260f, 56f);

        Image background = panelObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.55f);

        HorizontalLayoutGroup layoutGroup = panelObject.AddComponent<HorizontalLayoutGroup>();
        layoutGroup.padding = new RectOffset(12, 12, 8, 8);
        layoutGroup.childControlHeight = true;
        layoutGroup.childControlWidth = true;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = true;

        GameObject textObject = new GameObject("FuelText", typeof(RectTransform));
        textObject.transform.SetParent(panelObject.transform, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = 16f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;

        FurnaceFuelPanelUI panel = panelObject.AddComponent<FurnaceFuelPanelUI>();
        panel.fuelText = text;
        return panel;
    }

    private void EnsureText()
    {
        if (fuelText != null)
        {
            return;
        }

        fuelText = gameObject.AddComponent<TextMeshProUGUI>();
        fuelText.fontSize = 16f;
        fuelText.fontStyle = FontStyles.Bold;
        fuelText.color = Color.white;
        fuelText.alignment = TextAlignmentOptions.MidlineLeft;
        fuelText.enableWordWrapping = false;
        fuelText.overflowMode = TextOverflowModes.Ellipsis;
    }
}
