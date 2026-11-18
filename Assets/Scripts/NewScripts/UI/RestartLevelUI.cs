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
    private Toggle tutorialHudToggle;
    private BridgeStageInfoManager tutorialPreference;

    public Toggle TutorialHudToggle => tutorialHudToggle;

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
        BindTutorialPreference(null);
        if (tutorialHudToggle != null) tutorialHudToggle.gameObject.SetActive(false);
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
        BindTutorialPreference(BridgeStageInfoManager.GetCurrentGuidance());
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

    private void BindTutorialPreference(BridgeStageInfoManager value)
    {
        if (tutorialPreference != value)
        {
            if (tutorialPreference != null) tutorialPreference.PresentationPreferenceChanged -= OnTutorialPreferenceChanged;
            tutorialPreference = value;
            if (tutorialPreference != null) tutorialPreference.PresentationPreferenceChanged += OnTutorialPreferenceChanged;
        }
        if (tutorialPreference != null && tutorialHudToggle == null && panelRoot != null) CreateTutorialToggle();
        if (tutorialHudToggle != null)
        {
            bool available = tutorialPreference != null && tutorialPreference.UsesBridgeGuidance;
            tutorialHudToggle.gameObject.SetActive(available);
            if (available) tutorialHudToggle.SetIsOnWithoutNotify(tutorialPreference.ShowTutorialHud);
            RectTransform panel = panelRoot != null ? panelRoot.GetComponent<RectTransform>() : null;
            if (panel != null) panel.sizeDelta = new Vector2(panel.sizeDelta.x,
                Mathf.Max(panelSize.y, MinimumPanelHeight) + (available ? 58f : 0f));
        }
    }

    private void OnTutorialPreferenceChanged() => BindTutorialPreference(tutorialPreference);

    private void CreateTutorialToggle()
    {
        GameObject row = new GameObject("ShowTutorialHUD", typeof(RectTransform), typeof(LayoutElement), typeof(Toggle));
        row.transform.SetParent(panelRoot.transform, false);
        row.GetComponent<LayoutElement>().minHeight = 44f;
        tutorialHudToggle = row.GetComponent<Toggle>();
        tutorialHudToggle.onValueChanged.AddListener(OnTutorialToggleChanged);
        GameObject box = new GameObject("Checkbox", typeof(RectTransform), typeof(Image));
        RectTransform boxRect = box.GetComponent<RectTransform>();
        boxRect.SetParent(row.transform, false);
        boxRect.anchorMin = new Vector2(0f, .5f);
        boxRect.anchorMax = new Vector2(0f, .5f);
        boxRect.pivot = new Vector2(0f, .5f);
        boxRect.anchoredPosition = Vector2.zero;
        boxRect.sizeDelta = new Vector2(28f, 28f);
        Image background = box.GetComponent<Image>();
        background.color = new Color(.16f, .2f, .24f);
        GameObject mark = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        RectTransform markRect = mark.GetComponent<RectTransform>();
        markRect.SetParent(boxRect, false);
        markRect.anchorMin = Vector2.zero;
        markRect.anchorMax = Vector2.one;
        markRect.offsetMin = new Vector2(6f, 6f);
        markRect.offsetMax = new Vector2(-6f, -6f);
        Image checkmark = mark.GetComponent<Image>();
        checkmark.color = new Color(.25f, .8f, .5f);
        tutorialHudToggle.targetGraphic = background;
        tutorialHudToggle.graphic = checkmark;
        TextMeshProUGUI label = CreateText("Label", row.transform, "Show tutorial HUD", 18f, FontStyles.Normal, TextAlignmentOptions.Left);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(40f, 0f);
        labelRect.offsetMax = Vector2.zero;
    }

    private void OnTutorialToggleChanged(bool visible)
    {
        if (tutorialPreference != null) tutorialPreference.SetTutorialHudVisible(visible);
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
