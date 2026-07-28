using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FactorySelectedComponentStatusUI : MonoBehaviour
{
    [SerializeField] private Image componentImage;
    [SerializeField] private TextMeshProUGUI statusText;

    public void Refresh(BaseFactory factory)
    {
        EnsureText();

        if (factory == null)
        {
            statusText.text = string.Empty;
            return;
        }

        ProductionRecipeSO selectedRecipe = factory.SelectedRecipe;
        MountableBridgeComponentSO selectedComponent = factory.SelectedComponent;
        if (componentImage != null)
        {
            componentImage.sprite = selectedRecipe != null ? selectedRecipe.RecipeIcon : null;
            componentImage.enabled = selectedRecipe != null && selectedRecipe.RecipeIcon != null;
        }

        string componentName = selectedRecipe != null
            ? selectedRecipe.RecipeName + (selectedRecipe.OutputAmount > 1 ? $" x{selectedRecipe.OutputAmount}" : string.Empty)
            : "None";
        string status = GetFactoryStatusText(factory, selectedRecipe);
        string dimensions = GetDimensionsText(factory, selectedComponent);
        string furnaceProcess = GetFurnaceProcessText(factory, selectedRecipe);
        statusText.text = $"Selected: {componentName}\nStatus: {status}{dimensions}{furnaceProcess}";
    }

    public static FactorySelectedComponentStatusUI CreateRuntimePanel(Transform parent)
    {
        GameObject panelObject = new GameObject("FactorySelectedComponentStatusPanel", typeof(RectTransform));
        panelObject.transform.SetParent(parent, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 0.5f);
        panelRect.anchorMax = new Vector2(1f, 0.5f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.anchoredPosition = new Vector2(-28f, -252f);
        panelRect.sizeDelta = new Vector2(260f, 128f);

        Image background = panelObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.55f);

        VerticalLayoutGroup layoutGroup = panelObject.AddComponent<VerticalLayoutGroup>();
        layoutGroup.padding = new RectOffset(12, 12, 8, 8);
        layoutGroup.spacing = 4f;
        layoutGroup.childControlHeight = true;
        layoutGroup.childControlWidth = true;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = true;

        TextMeshProUGUI text = new GameObject("StatusText", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        text.transform.SetParent(panelObject.transform, false);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(0f, 112f);
        LayoutElement textLayout = text.gameObject.AddComponent<LayoutElement>();
        textLayout.minHeight = 112f;
        textLayout.preferredHeight = 112f;
        text.fontSize = 14f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;

        FactorySelectedComponentStatusUI panel = panelObject.AddComponent<FactorySelectedComponentStatusUI>();
        panel.statusText = text;
        return panel;
    }

    private string GetFactoryStatusText(BaseFactory factory, ProductionRecipeSO selectedRecipe)
    {
        if (factory.IsProducing)
        {
            return $"Producing {factory.ProductionProgressNormalized:P0}";
        }

        if (selectedRecipe == null)
        {
            return "No component selected";
        }

        if (!factory.CheckRequiredBaseResources(selectedRecipe))
        {
            return "Missing resources";
        }

        if (factory is CarpenterTableFactory carpenterTableFactory && !carpenterTableFactory.AreDimensionsMatchingSelectedComponent)
        {
            return "Wrong dimensions";
        }

        return "Ready";
    }

    private string GetDimensionsText(BaseFactory factory, MountableBridgeComponentSO selectedComponent)
    {
        CarpenterTableFactory carpenterTableFactory = factory as CarpenterTableFactory;
        if (carpenterTableFactory == null || selectedComponent == null)
        {
            return string.Empty;
        }

        Vector2 currentDimensions = carpenterTableFactory.GetCurrentDimensions();
        Vector2 requiredDimensions = carpenterTableFactory.GetRequiredDimensionsForSelectedComponent();
        return $"\nCurrent dimensions: W {currentDimensions.x:0.##}, L {currentDimensions.y:0.##}\nRequired dimensions: W {requiredDimensions.x:0.##}, L {requiredDimensions.y:0.##}";
    }

    private string GetFurnaceProcessText(BaseFactory factory, ProductionRecipeSO selectedRecipe)
    {
        if (!(factory is BlastFurnaceFactory) || selectedRecipe == null)
        {
            return string.Empty;
        }

        return $"\nRequired temperature: {selectedRecipe.MeltingPoint:0}\nRequired progress: {selectedRecipe.NeededProgress:0}";
    }

    private void EnsureText()
    {
        if (statusText != null)
        {
            return;
        }

        statusText = gameObject.AddComponent<TextMeshProUGUI>();
        statusText.fontSize = 14f;
        statusText.color = Color.white;
        statusText.alignment = TextAlignmentOptions.TopLeft;
        statusText.enableWordWrapping = true;
        statusText.overflowMode = TextOverflowModes.Ellipsis;
    }
}
