using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RestartLevelUI : MonoBehaviour
{
    [SerializeField] private PlayerInputNew playerInput;
    [SerializeField] private PlayerLevelRestartController restartController;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button restartButton;
    [SerializeField] private TextMeshProUGUI availabilityText;
    [SerializeField] private Vector2 panelSize = new Vector2(360f, 210f);

    private bool isVisible;

    private void Awake()
    {
        EnsureReferences();
        SetVisible(false);
    }

    private void OnEnable()
    {
        EnsureReferences();
        SubscribeInput();
        SetVisible(false);
    }

    private void OnDisable()
    {
        if (playerInput != null)
        {
            playerInput.OnToggleRestartMenu -= PlayerInput_OnToggleRestartMenu;
        }

        if (isVisible)
        {
            playerInput?.SetGameplayUiOpen(false);
            isVisible = false;
        }
    }

    private void SubscribeInput()
    {
        if (playerInput == null)
        {
            playerInput = GetComponentInParent<PlayerInputNew>();
        }

        if (playerInput == null)
        {
            return;
        }

        playerInput.OnToggleRestartMenu -= PlayerInput_OnToggleRestartMenu;
        playerInput.OnToggleRestartMenu += PlayerInput_OnToggleRestartMenu;
    }

    private void PlayerInput_OnToggleRestartMenu(object sender, EventArgs e)
    {
        SetVisible(!isVisible);
    }

    public void RequestRestartLevel()
    {
        if (restartController == null || !restartController.CanRequestRestart)
        {
            Refresh();
            return;
        }

        restartButton.interactable = false;
        availabilityText.text = "Restarting level...";
        restartController.RequestRestartLevel();
    }

    private void SetVisible(bool visible)
    {
        EnsureReferences();
        isVisible = visible;

        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }

        playerInput?.SetGameplayUiOpen(visible);
        if (visible)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        EnsureReferences();
        bool canRestart = restartController != null && restartController.CanRequestRestart;
        if (restartButton != null)
        {
            restartButton.interactable = canRestart;
        }

        if (availabilityText == null)
        {
            return;
        }

        availabilityText.text = canRestart
            ? "The host can restart the level for everyone."
            : "Only the host can restart the level.";
    }

    private void EnsureReferences()
    {
        if (playerInput == null)
        {
            playerInput = GetComponentInParent<PlayerInputNew>();
        }

        if (restartController == null)
        {
            restartController = GetComponentInParent<PlayerLevelRestartController>();
        }

        if (panelRoot == null || restartButton == null || availabilityText == null)
        {
            CreateDefaultPanel();
        }
    }

    private void CreateDefaultPanel()
    {
        if (transform is not RectTransform parentRectTransform)
        {
            return;
        }

        if (panelRoot == null)
        {
            GameObject panelObject = new GameObject("RestartLevelPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            RectTransform panelRectTransform = panelObject.GetComponent<RectTransform>();
            panelRectTransform.SetParent(parentRectTransform, false);
            panelRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            panelRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panelRectTransform.pivot = new Vector2(0.5f, 0.5f);
            panelRectTransform.anchoredPosition = Vector2.zero;
            panelRectTransform.sizeDelta = panelSize;

            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.color = new Color(0.045f, 0.05f, 0.06f, 0.94f);

            VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            panelRoot = panelObject;
        }

        Transform panelTransform = panelRoot.transform;
        if (panelTransform.Find("Title") == null)
        {
            CreateText("Title", panelTransform, "Restart level", 28f, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        if (availabilityText == null)
        {
            availabilityText = CreateText("Availability", panelTransform, string.Empty, 17f, FontStyles.Normal, TextAlignmentOptions.Center);
        }

        if (restartButton == null)
        {
            restartButton = CreateButton(panelTransform);
        }
    }

    private TextMeshProUGUI CreateText(string objectName, Transform parent, string value, float fontSize, FontStyles fontStyle, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        LayoutElement layoutElement = textObject.GetComponent<LayoutElement>();
        layoutElement.minHeight = fontSize + 12f;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateButton(Transform parent)
    {
        GameObject buttonObject = new GameObject("RestartButton", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        LayoutElement layoutElement = buttonObject.GetComponent<LayoutElement>();
        layoutElement.minHeight = 48f;

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.75f, 0.25f, 0.2f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(RequestRestartLevel);
        CreateText("Label", buttonObject.transform, "Restart level", 20f, FontStyles.Bold, TextAlignmentOptions.Center);
        return button;
    }
}
