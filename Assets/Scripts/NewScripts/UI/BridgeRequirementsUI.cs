using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BridgeRequirementsUI : MonoBehaviour
{
    [SerializeField] private PlayerInputNew playerInput;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI currentStageText;
    [SerializeField] private TextMeshProUGUI remainingStagesText;
    [SerializeField] private Vector2 anchoredPosition = new Vector2(-24f, -120f);
    [SerializeField] private Vector2 panelSize = new Vector2(380f, 430f);

    private GameplayManager subscribedGameplayManager;
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
        TrySubscribeGameplayManager();
        SetVisible(false);
    }

    private void OnDisable()
    {
        if (playerInput != null)
        {
            playerInput.OnToggleBridgeRequirements -= PlayerInput_OnToggleBridgeRequirements;
        }

        if (subscribedGameplayManager != null)
        {
            subscribedGameplayManager.OnBridgeRequirementsChanged -= GameplayManager_OnBridgeRequirementsChanged;
            subscribedGameplayManager = null;
        }
    }

    private void Update()
    {
        TrySubscribeGameplayManager();
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

        playerInput.OnToggleBridgeRequirements -= PlayerInput_OnToggleBridgeRequirements;
        playerInput.OnToggleBridgeRequirements += PlayerInput_OnToggleBridgeRequirements;
    }

    private void TrySubscribeGameplayManager()
    {
        if (subscribedGameplayManager != null || GameplayManager.Instance == null)
        {
            return;
        }

        subscribedGameplayManager = GameplayManager.Instance;
        subscribedGameplayManager.OnBridgeRequirementsChanged += GameplayManager_OnBridgeRequirementsChanged;
        if (isVisible)
        {
            Refresh();
        }
    }

    private void PlayerInput_OnToggleBridgeRequirements(object sender, EventArgs e)
    {
        SetVisible(!isVisible);
    }

    private void GameplayManager_OnBridgeRequirementsChanged(object sender, EventArgs e)
    {
        if (isVisible)
        {
            Refresh();
        }
    }

    private void SetVisible(bool visible)
    {
        EnsureReferences();
        isVisible = visible;
        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }

        if (visible)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        EnsureReferences();
        if (titleText == null || currentStageText == null || remainingStagesText == null)
        {
            return;
        }

        if (GameplayManager.Instance == null)
        {
            titleText.text = "Bridge Requirements";
            currentStageText.text = "Gameplay manager unavailable";
            remainingStagesText.text = string.Empty;
            return;
        }

        BridgeRequirementsSnapshot snapshot = GameplayManager.Instance.GetBridgeRequirementsSnapshot();
        titleText.text = "Bridge Requirements";
        if (snapshot.IsBridgeComplete)
        {
            currentStageText.text = "Bridge complete";
            remainingStagesText.text = string.Empty;
            return;
        }

        StringBuilder currentBuilder = new StringBuilder();
        currentBuilder.AppendLine($"Current stage {snapshot.CurrentStageIndex + 1}");
        if (snapshot.CurrentStageRequirements.Count == 0)
        {
            currentBuilder.Append("No current requirements");
        }
        else
        {
            foreach (BridgeRequirementLine requirement in snapshot.CurrentStageRequirements)
            {
                currentBuilder.AppendLine($"{requirement.ComponentName} - {requirement.CurrentAmount} / {requirement.RequiredAmount}");
            }
        }

        StringBuilder remainingBuilder = new StringBuilder();
        remainingBuilder.AppendLine("Remaining stages");
        if (snapshot.RemainingStageRequirements.Count == 0)
        {
            remainingBuilder.Append("No remaining requirements");
        }
        else
        {
            foreach (BridgeRequirementLine requirement in snapshot.RemainingStageRequirements)
            {
                remainingBuilder.AppendLine($"{requirement.ComponentName} x {requirement.RequiredAmount}");
            }
        }

        currentStageText.text = currentBuilder.ToString().TrimEnd();
        remainingStagesText.text = remainingBuilder.ToString().TrimEnd();
    }

    private void EnsureReferences()
    {
        if (playerInput == null)
        {
            playerInput = GetComponentInParent<PlayerInputNew>();
        }

        if (panelRoot == null || titleText == null || currentStageText == null || remainingStagesText == null)
        {
            CreateDefaultPanel();
        }
    }

    private void CreateDefaultPanel()
    {
        RectTransform parentRectTransform = transform as RectTransform;
        if (parentRectTransform == null)
        {
            return;
        }

        if (panelRoot == null)
        {
            GameObject panelGameObject = new GameObject("BridgeRequirementsPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            RectTransform panelRectTransform = panelGameObject.GetComponent<RectTransform>();
            panelRectTransform.SetParent(parentRectTransform, false);
            panelRectTransform.anchorMin = new Vector2(1f, 1f);
            panelRectTransform.anchorMax = new Vector2(1f, 1f);
            panelRectTransform.pivot = new Vector2(1f, 1f);
            panelRectTransform.anchoredPosition = anchoredPosition;
            panelRectTransform.sizeDelta = panelSize;

            Image panelImage = panelGameObject.GetComponent<Image>();
            panelImage.color = new Color(0.05f, 0.055f, 0.06f, 0.88f);
            panelImage.raycastTarget = false;

            VerticalLayoutGroup layoutGroup = panelGameObject.GetComponent<VerticalLayoutGroup>();
            layoutGroup.padding = new RectOffset(18, 18, 16, 16);
            layoutGroup.spacing = 12f;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;

            panelRoot = panelGameObject;
        }

        Transform panelTransform = panelRoot.transform;
        if (titleText == null)
        {
            titleText = CreateText("Title", panelTransform, 24f, FontStyles.Bold);
        }

        if (currentStageText == null)
        {
            currentStageText = CreateText("CurrentStage", panelTransform, 18f, FontStyles.Bold);
        }

        if (remainingStagesText == null)
        {
            remainingStagesText = CreateText("RemainingStages", panelTransform, 18f, FontStyles.Normal);
        }
    }

    private TextMeshProUGUI CreateText(string objectName, Transform parent, float fontSize, FontStyles fontStyle)
    {
        GameObject textGameObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rectTransform = textGameObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.sizeDelta = new Vector2(0f, fontSize * 3f);

        TextMeshProUGUI text = textGameObject.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }
}
