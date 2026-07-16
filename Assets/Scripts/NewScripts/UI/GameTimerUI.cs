using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameTimerUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private Image timerProgressBar; // Radial or linear fill progress bar

    [Header("Runtime HUD")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private Vector2 anchoredPosition = new Vector2(-24f, -24f);
    [SerializeField] private Vector2 hudSize = new Vector2(176f, 52f);

    [Header("Feedback Settings")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color warningColor = new Color(0.9f, 0.2f, 0.2f); // Vibrant red warning color
    [SerializeField] private float warningThreshold = 60f; // Warning effects trigger under 60 seconds
    [SerializeField] private float pulseSpeed = 5f;

    private GameTimerManager subscribedTimerManager;

    private void OnEnable()
    {
        EnsureVisuals();
        TrySubscribeTimerManager();
    }

    private void OnDestroy()
    {
        UnsubscribeTimerManager();
    }

    private void Update()
    {
        TrySubscribeTimerManager();
    }

    private void OnDisable()
    {
        UnsubscribeTimerManager();
    }

    private void TrySubscribeTimerManager()
    {
        GameTimerManager timerManager = GameTimerManager.Instance;
        if (timerManager == subscribedTimerManager)
        {
            return;
        }

        UnsubscribeTimerManager();
        if (timerManager == null)
        {
            return;
        }

        subscribedTimerManager = timerManager;
        subscribedTimerManager.OnTimerChanged += GameTimerManager_OnTimerChanged;
        UpdateUI(subscribedTimerManager.GetTimeRemaining(), subscribedTimerManager.GetNormalizedTimeRemaining());
    }

    private void UnsubscribeTimerManager()
    {
        if (subscribedTimerManager != null)
        {
            subscribedTimerManager.OnTimerChanged -= GameTimerManager_OnTimerChanged;
            subscribedTimerManager = null;
        }
    }

    private void GameTimerManager_OnTimerChanged(object sender, GameTimerManager.OnTimerChangedEventArgs e)
    {
        UpdateUI(e.timeRemaining, e.normalizedTimeRemaining);
    }

    private void UpdateUI(float timeRemaining, float normalizedTimeRemaining)
    {
        EnsureVisuals();
        if (timerText == null)
        {
            return;
        }

        // 1. Format time into MM:SS format
        int minutes = Mathf.FloorToInt(timeRemaining / 60F);
        int seconds = Mathf.FloorToInt(timeRemaining - (minutes * 60));
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);

        // 2. Update horizontal progress bar fill
        if (timerProgressBar != null)
        {
            timerProgressBar.fillAmount = normalizedTimeRemaining;
        }

        // 3. Danger/Warning Feedback Effects (Color and Text Pulsing)
        if (timeRemaining <= warningThreshold)
        {
            timerText.color = warningColor;
            if (timerProgressBar != null)
            {
                timerProgressBar.color = warningColor;
            }

            // Create a subtle breathing/pulsing scale effect to highlight high urgency
            float pulseScale = 1.0f + Mathf.Sin(Time.time * pulseSpeed) * 0.08f;
            timerText.transform.localScale = new Vector3(pulseScale, pulseScale, 1.0f);
        }
        else
        {
            timerText.color = normalColor;
            if (timerProgressBar != null)
            {
                timerProgressBar.color = normalColor;
            }
            timerText.transform.localScale = Vector3.one;
        }
    }

    private void EnsureVisuals()
    {
        if (timerText != null)
        {
            return;
        }

        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInChildren<Canvas>(true);
        }

        if (targetCanvas == null)
        {
            return;
        }

        GameObject root = new GameObject("GameTimerHUD", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rootRectTransform = root.GetComponent<RectTransform>();
        rootRectTransform.SetParent(targetCanvas.transform, false);
        rootRectTransform.anchorMin = Vector2.one;
        rootRectTransform.anchorMax = Vector2.one;
        rootRectTransform.pivot = Vector2.one;
        rootRectTransform.anchoredPosition = anchoredPosition;
        rootRectTransform.sizeDelta = hudSize;

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.03f, 0.05f, 0.07f, 0.78f);
        background.raycastTarget = false;

        GameObject textObject = new GameObject("Time", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform textRectTransform = textObject.GetComponent<RectTransform>();
        textRectTransform.SetParent(rootRectTransform, false);
        textRectTransform.anchorMin = new Vector2(0.08f, 0.22f);
        textRectTransform.anchorMax = new Vector2(0.92f, 0.88f);
        textRectTransform.offsetMin = Vector2.zero;
        textRectTransform.offsetMax = Vector2.zero;

        timerText = textObject.GetComponent<TextMeshProUGUI>();
        timerText.font = TMP_Settings.defaultFontAsset;
        timerText.fontSize = 28f;
        timerText.alignment = TextAlignmentOptions.Center;
        timerText.color = normalColor;
        timerText.raycastTarget = false;

        GameObject progressObject = new GameObject("Progress", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform progressRectTransform = progressObject.GetComponent<RectTransform>();
        progressRectTransform.SetParent(rootRectTransform, false);
        progressRectTransform.anchorMin = new Vector2(0.08f, 0.09f);
        progressRectTransform.anchorMax = new Vector2(0.92f, 0.17f);
        progressRectTransform.offsetMin = Vector2.zero;
        progressRectTransform.offsetMax = Vector2.zero;

        timerProgressBar = progressObject.GetComponent<Image>();
        timerProgressBar.type = Image.Type.Filled;
        timerProgressBar.fillMethod = Image.FillMethod.Horizontal;
        timerProgressBar.fillOrigin = 0;
        timerProgressBar.color = normalColor;
        timerProgressBar.raycastTarget = false;
    }
}
