using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FactoryStorageResourcesPanelUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Transform contentHolder;
    [SerializeField] private GameObject resourceRowTemplate;

    private readonly List<GameObject> spawnedRows = new List<GameObject>();

    private void Awake()
    {
        if (titleText != null)
        {
            titleText.text = "Storage";
        }

        if (contentHolder == null)
        {
            contentHolder = transform;
        }

        if (resourceRowTemplate != null)
        {
            resourceRowTemplate.SetActive(false);
        }
    }

    public void Refresh(BaseStorageNew storage)
    {
        ClearRows();
        if (storage == null || storage.StorableBaseResources == null)
        {
            return;
        }

        foreach (BaseResourceSO resourceSO in storage.StorableBaseResources)
        {
            if (resourceSO == null)
            {
                continue;
            }

            int amount = Mathf.Max(0, storage.CheckBaseResourceAmount(resourceSO));
            CreateRow(resourceSO, amount.ToString());
        }
    }

    public static FactoryStorageResourcesPanelUI CreateRuntimePanel(Transform parent)
    {
        GameObject panelObject = CreateRuntimePanelRoot(parent, "FactoryStorageResourcesPanel", "Storage");
        return panelObject.AddComponent<FactoryStorageResourcesPanelUI>();
    }

    private void CreateRow(BaseResourceSO resourceSO, string amountText)
    {
        if (resourceRowTemplate != null)
        {
            GameObject rowObject = Instantiate(resourceRowTemplate, contentHolder);
            spawnedRows.Add(rowObject);
            if (rowObject.TryGetComponent(out FactoryRequiredResourceUI resourceUI))
            {
                resourceUI.SetProperties(resourceSO.icon, resourceSO.resourceName, amountText);
            }
            rowObject.SetActive(true);
            return;
        }

        GameObject textObject = new GameObject($"{resourceSO.resourceName}_StorageRow", typeof(RectTransform));
        textObject.transform.SetParent(contentHolder, false);
        spawnedRows.Add(textObject);
        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(0f, 24f);
        LayoutElement layoutElement = textObject.AddComponent<LayoutElement>();
        layoutElement.minHeight = 24f;
        layoutElement.preferredHeight = 24f;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = $"{resourceSO.resourceName}: {amountText}";
        text.fontSize = 15f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void ClearRows()
    {
        foreach (GameObject row in spawnedRows)
        {
            if (row != null)
            {
                Destroy(row);
            }
        }

        spawnedRows.Clear();
    }

    private static GameObject CreateRuntimePanelRoot(Transform parent, string objectName, string title)
    {
        GameObject panelObject = new GameObject(objectName, typeof(RectTransform));
        panelObject.transform.SetParent(parent, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 0.5f);
        panelRect.anchorMax = new Vector2(1f, 0.5f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.anchoredPosition = new Vector2(-28f, -96f);
        panelRect.sizeDelta = new Vector2(260f, 150f);

        Image background = panelObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.55f);

        VerticalLayoutGroup layoutGroup = panelObject.AddComponent<VerticalLayoutGroup>();
        layoutGroup.padding = new RectOffset(12, 12, 8, 8);
        layoutGroup.spacing = 4f;
        layoutGroup.childControlHeight = true;
        layoutGroup.childControlWidth = true;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = true;

        GameObject titleObject = new GameObject("Title", typeof(RectTransform));
        titleObject.transform.SetParent(panelObject.transform, false);
        LayoutElement titleLayout = titleObject.AddComponent<LayoutElement>();
        titleLayout.minHeight = 26f;
        titleLayout.preferredHeight = 26f;
        TextMeshProUGUI titleText = titleObject.AddComponent<TextMeshProUGUI>();
        titleText.text = title;
        titleText.fontSize = 17f;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = Color.white;
        titleText.alignment = TextAlignmentOptions.MidlineLeft;

        return panelObject;
    }
}
