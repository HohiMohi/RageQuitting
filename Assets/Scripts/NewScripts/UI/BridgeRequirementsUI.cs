using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
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

    private TextMeshProUGUI myTasksText;
    private ScrollRect scrollRect;
    private RectTransform contentRoot;
    private GameplayManager subscribedGameplayManager;
    private ConstructionBookController subscribedBook;
    private readonly List<BridgeComponentSO> currentStageTypes = new List<BridgeComponentSO>();
    private readonly HashSet<BridgeComponentSO> currentStageTypeSet = new HashSet<BridgeComponentSO>();
    private bool isVisible;
    private bool hasSnapshot;
    private int previousStageIndex;
    private bool previousBridgeComplete;

    public bool IsVisible => isVisible;
    public ScrollRect ScrollRect => scrollRect;

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
        isVisible = false;
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
        UnsubscribeInput();
        UnsubscribeGameplayManager();
        UnsubscribeBook();
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
        playerInput.OnBridgeRequirementsScroll -= PlayerInput_OnScroll;
        playerInput.OnBridgeRequirementsScroll += PlayerInput_OnScroll;
    }

    private void UnsubscribeInput()
    {
        if (playerInput == null)
        {
            return;
        }

        playerInput.OnToggleBridgeRequirements -= PlayerInput_OnToggleBridgeRequirements;
        playerInput.OnBridgeRequirementsScroll -= PlayerInput_OnScroll;
    }

    private void TrySubscribeGameplayManager()
    {
        GameplayManager currentManager = GameplayManager.Instance;
        bool managerChanged = subscribedGameplayManager != currentManager;
        if (managerChanged)
        {
            UnsubscribeGameplayManager();
            subscribedGameplayManager = currentManager;
            if (subscribedGameplayManager != null)
            {
                subscribedGameplayManager.OnBridgeRequirementsChanged += GameplayManager_OnBridgeRequirementsChanged;
                subscribedGameplayManager.ConstructionBookAvailabilityChanged += GameplayManager_OnBookAvailabilityChanged;
            }
        }

        RebindBook(currentManager != null ? currentManager.RegisteredConstructionBook : null);
        if (managerChanged && isVisible)
        {
            Refresh(false);
        }
    }

    private void UnsubscribeGameplayManager()
    {
        if (subscribedGameplayManager != null)
        {
            subscribedGameplayManager.OnBridgeRequirementsChanged -= GameplayManager_OnBridgeRequirementsChanged;
            subscribedGameplayManager.ConstructionBookAvailabilityChanged -= GameplayManager_OnBookAvailabilityChanged;
        }

        subscribedGameplayManager = null;
    }

    private void RebindBook(ConstructionBookController book)
    {
        if (subscribedBook == book)
        {
            return;
        }

        UnsubscribeBook();
        subscribedBook = book;
        if (subscribedBook != null)
        {
            subscribedBook.AssignmentsChanged += Book_OnAssignmentsChanged;
            subscribedBook.Despawned += Book_OnDespawned;
        }

        if (isVisible)
        {
            Refresh(false);
        }
    }

    private void UnsubscribeBook()
    {
        if (subscribedBook != null)
        {
            subscribedBook.AssignmentsChanged -= Book_OnAssignmentsChanged;
            subscribedBook.Despawned -= Book_OnDespawned;
        }

        subscribedBook = null;
    }

    private void PlayerInput_OnToggleBridgeRequirements(object sender, EventArgs e)
    {
        SetVisible(!isVisible);
    }

    private void PlayerInput_OnScroll(Vector2 delta)
    {
        if (isVisible && playerInput != null && !playerInput.IsGameplayUiOpen)
        {
            ApplyWheelScroll(delta);
        }
    }

    private void GameplayManager_OnBridgeRequirementsChanged(object sender, EventArgs e)
    {
        if (isVisible)
        {
            Refresh(false);
        }
    }

    private void GameplayManager_OnBookAvailabilityChanged(ConstructionBookController book)
    {
        RebindBook(book);
    }

    private void Book_OnAssignmentsChanged()
    {
        if (isVisible)
        {
            Refresh(false);
        }
    }

    private void Book_OnDespawned()
    {
        UnsubscribeBook();
        if (isVisible)
        {
            Refresh(false);
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
            Refresh(true);
        }
    }

    public void ApplyWheelScroll(Vector2 delta)
    {
        if (!isVisible || scrollRect == null || playerInput != null && playerInput.IsGameplayUiOpen)
        {
            return;
        }

        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollRect.verticalNormalizedPosition + delta.y * 0.08f);
    }

    public void Refresh(bool resetScrollToTop)
    {
        EnsureReferences();
        if (titleText == null || currentStageText == null || remainingStagesText == null || myTasksText == null)
        {
            return;
        }

        float previousScrollOffset = GetScrollOffset();
        GameplayManager manager = GameplayManager.Instance;
        if (manager == null)
        {
            SetContentText("Gameplay manager unavailable", "My tasks\nGameplay data unavailable", string.Empty);
            hasSnapshot = false;
            RestoreScroll(resetScrollToTop, previousScrollOffset);
            return;
        }

        BridgeRequirementsSnapshot snapshot = manager.GetBridgeRequirementsSnapshot();
        bool stageTransition = hasSnapshot &&
            (previousStageIndex != snapshot.CurrentStageIndex || previousBridgeComplete != snapshot.IsBridgeComplete);
        previousStageIndex = snapshot.CurrentStageIndex;
        previousBridgeComplete = snapshot.IsBridgeComplete;
        hasSnapshot = true;

        if (snapshot.IsBridgeComplete)
        {
            SetContentText("Bridge complete", "My tasks\nNo current stage tasks", string.Empty);
            RestoreScroll(resetScrollToTop || stageTransition, previousScrollOffset);
            return;
        }

        StringBuilder currentBuilder = new StringBuilder();
        currentBuilder.Append("Current stage ").Append(snapshot.CurrentStageIndex + 1);
        if (snapshot.CurrentStageRequirements.Count == 0)
        {
            currentBuilder.AppendLine().Append("No current requirements");
        }
        else
        {
            foreach (BridgeRequirementLine requirement in snapshot.CurrentStageRequirements)
            {
                currentBuilder.AppendLine().Append(requirement.ComponentName).Append(" - ")
                    .Append(requirement.CurrentAmount).Append(" / ").Append(requirement.RequiredAmount);
            }
        }

        StringBuilder taskBuilder = new StringBuilder("My tasks");
        if (subscribedBook == null)
        {
            taskBuilder.AppendLine().Append("Construction book unavailable");
        }
        else
        {
            currentStageTypes.Clear();
            manager.GetCurrentStageComponentTypes(currentStageTypes);
            currentStageTypeSet.Clear();
            foreach (BridgeComponentSO component in currentStageTypes)
            {
                currentStageTypeSet.Add(component);
            }

            if (!subscribedBook.HasBookDataFor(currentStageTypeSet))
            {
                taskBuilder.AppendLine().Append("Construction book data unavailable");
            }
            else
            {
                StringBuilder assignedTasks = new StringBuilder();
                NetworkManager networkManager = NetworkManager.Singleton;
                ulong localClientId = networkManager != null && networkManager.IsListening ? networkManager.LocalClientId : 0;
                subscribedBook.AppendAssignedTasks(localClientId, currentStageTypeSet, assignedTasks);
                if (assignedTasks.Length == 0)
                {
                    taskBuilder.AppendLine().Append("No assigned tasks in this stage");
                }
                else
                {
                    taskBuilder.AppendLine().Append(assignedTasks.ToString().TrimEnd());
                }
            }
        }

        StringBuilder remainingBuilder = new StringBuilder("Remaining stages");
        if (snapshot.RemainingStageRequirements.Count == 0)
        {
            remainingBuilder.AppendLine().Append("No remaining requirements");
        }
        else
        {
            foreach (BridgeRequirementLine requirement in snapshot.RemainingStageRequirements)
            {
                remainingBuilder.AppendLine().Append(requirement.ComponentName).Append(" x ").Append(requirement.RequiredAmount);
            }
        }

        SetContentText(currentBuilder.ToString(), taskBuilder.ToString(), remainingBuilder.ToString());
        RestoreScroll(resetScrollToTop || stageTransition, previousScrollOffset);
    }

    private void SetContentText(string currentText, string tasksText, string remainingText)
    {
        titleText.text = "Bridge Requirements";
        currentStageText.text = currentText;
        myTasksText.text = tasksText;
        remainingStagesText.text = remainingText;
    }

    private float GetScrollOffset()
    {
        return scrollRect != null && contentRoot != null ? contentRoot.anchoredPosition.y : 0f;
    }

    private void RestoreScroll(bool reset, float previousOffset)
    {
        if (scrollRect == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        if (contentRoot != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
        }

        float maxOffset = Mathf.Max(0f, contentRoot.rect.height - scrollRect.viewport.rect.height);
        Vector2 position = contentRoot.anchoredPosition;
        position.y = reset ? 0f : Mathf.Clamp(previousOffset, 0f, maxOffset);
        contentRoot.anchoredPosition = position;
        scrollRect.StopMovement();
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

        EnsureScrollLayout();
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
            layoutGroup.spacing = 10f;
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

    private void EnsureScrollLayout()
    {
        if (panelRoot == null || titleText == null || currentStageText == null || remainingStagesText == null)
        {
            return;
        }

        Transform panelTransform = panelRoot.transform;
        Transform viewportTransform = panelTransform.Find("RequirementsViewport");
        if (viewportTransform == null)
        {
            GameObject viewportObject = new GameObject("RequirementsViewport", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            viewportRect.SetParent(panelTransform, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            LayoutElement viewportLayout = viewportObject.GetComponent<LayoutElement>();
            viewportLayout.flexibleHeight = 1f;
            viewportLayout.minHeight = 0f;

            GameObject contentObject = new GameObject("RequirementsContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentRoot = contentObject.GetComponent<RectTransform>();
            contentRoot.SetParent(viewportRect, false);
            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = Vector2.one;
            contentRoot.pivot = new Vector2(0.5f, 1f);
            contentRoot.sizeDelta = Vector2.zero;
            VerticalLayoutGroup contentLayout = contentObject.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 12f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            currentStageText.transform.SetParent(contentRoot, false);
            myTasksText = CreateText("MyTasks", contentRoot, 16f, FontStyles.Normal);
            remainingStagesText.transform.SetParent(contentRoot, false);

            scrollRect = viewportObject.GetComponent<ScrollRect>();
            scrollRect.content = contentRoot;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = false;
            scrollRect.scrollSensitivity = 0f;
        }
        else
        {
            scrollRect = viewportTransform.GetComponent<ScrollRect>();
            contentRoot = scrollRect != null ? scrollRect.content : null;
            if (contentRoot != null)
            {
                Transform tasks = contentRoot.Find("MyTasks");
                if (tasks != null)
                {
                    myTasksText = tasks.GetComponent<TextMeshProUGUI>();
                }
            }
        }

        if (scrollRect == null || contentRoot == null)
        {
            return;
        }

        if (myTasksText == null)
        {
            myTasksText = CreateText("MyTasks", contentRoot, 16f, FontStyles.Normal);
        }

        ConfigureBodyText(currentStageText, FontStyles.Bold);
        ConfigureBodyText(myTasksText, FontStyles.Normal);
        ConfigureBodyText(remainingStagesText, FontStyles.Normal);
        AddFlexibleHeight(titleText.gameObject, 30f);
    }

    private static void ConfigureBodyText(TextMeshProUGUI text, FontStyles style)
    {
        text.fontSize = style == FontStyles.Bold ? 18f : 16f;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        AddFlexibleHeight(text.gameObject, 0f);
    }

    private static void AddFlexibleHeight(GameObject target, float preferredHeight)
    {
        LayoutElement layout = target.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = target.AddComponent<LayoutElement>();
        }

        layout.minHeight = preferredHeight;
        layout.preferredHeight = -1f;
        layout.flexibleHeight = 0f;
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
