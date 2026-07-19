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
    [SerializeField] private Button startTimerButton;
    [SerializeField] private TextMeshProUGUI availabilityText;
    [SerializeField] private Vector2 panelSize = new Vector2(360f, 320f);

    private const float MinimumPanelHeight = 320f;

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

    public void RequestStartTimer()
    {
        GameTimerManager timerManager = GameTimerManager.Instance;
        if (timerManager == null || !timerManager.TryStartTimer())
        {
            Refresh();
            return;
        }

        SetVisible(false);
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
        GameTimerManager timerManager = GameTimerManager.Instance;
        bool canStartTimer = timerManager != null && timerManager.CanStartTimer;
        if (restartButton != null)
        {
            restartButton.interactable = canRestart;
        }

        if (startTimerButton != null)
        {
            startTimerButton.interactable = canStartTimer;
        }

        if (availabilityText == null)
        {
            return;
        }

        if (timerManager != null && !timerManager.IsWaiting)
        {
            availabilityText.text = canRestart
                ? "Timer started. The host can restart the level."
                : "Timer started. Only the host can restart the level.";
        }
        else
        {
            availabilityText.text = canStartTimer
                ? "The host can start the timer or restart the level."
                : "Only the host can start the timer or restart the level.";
        }
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

        if (panelRoot == null || restartButton == null || startTimerButton == null || availabilityText == null)
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
            panelRectTransform.sizeDelta = new Vector2(panelSize.x, Mathf.Max(panelSize.y, MinimumPanelHeight));

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
            CreateText("Title", panelTransform, "Level controls", 28f, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        if (availabilityText == null)
        {
            availabilityText = CreateText("Availability", panelTransform, string.Empty, 17f, FontStyles.Normal, TextAlignmentOptions.Center);
        }

        if (restartButton == null)
        {
            restartButton = CreateButton(panelTransform, "RestartButton", "Restart level", new Color(0.75f, 0.25f, 0.2f, 1f), RequestRestartLevel);
        }

        if (startTimerButton == null)
        {
            startTimerButton = CreateButton(panelTransform, "StartTimerButton", "Start timer", new Color(0.25f, 0.55f, 0.3f, 1f), RequestStartTimer);
            startTimerButton.transform.SetSiblingIndex(restartButton.transform.GetSiblingIndex());
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

    private Button CreateButton(Transform parent, string objectName, string label, Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        LayoutElement layoutElement = buttonObject.GetComponent<LayoutElement>();
        layoutElement.minHeight = 48f;

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        CreateText("Label", buttonObject.transform, label, 20f, FontStyles.Bold, TextAlignmentOptions.Center);
        return button;
    }
}
