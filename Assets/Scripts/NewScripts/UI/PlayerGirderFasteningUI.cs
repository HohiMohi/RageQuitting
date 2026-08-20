using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerGirderFasteningUI : MonoBehaviour
{
    private const float StateConfirmationGrace = 1f;

    private GameObject panel;
    private RectTransform timerFill;
    private TMP_Text instructionText;
    private GameplayManager subscribedManager;
    private BridgeGirderConstructionSite activeSite;
    private BridgeGirderWorkPointId requiredPoint;
    private double expectedDeadline;
    private float confirmationExpiresAt;
    private bool stateConfirmed;

    private void Awake()
    {
        BuildUI();
        Hide();
    }

    private void OnEnable()
    {
        TryBindManager();
    }

    private void OnDisable()
    {
        UnbindManager();
        Hide();
    }

    private void Update()
    {
        TryBindManager();
        if (!IsLocalOwner() || activeSite == null || GetSynchronizedTime() >= expectedDeadline)
        {
            Hide();
            return;
        }

        bool matchingState = activeSite.IsFastenerPairWindowActive &&
                             activeSite.RequiredPairedFastener == requiredPoint &&
                             System.Math.Abs(activeSite.FastenerPairDeadline - expectedDeadline) <= 0.1d;
        if (matchingState)
        {
            stateConfirmed = true;
        }
        else if (stateConfirmed || Time.unscaledTime >= confirmationExpiresAt)
        {
            Hide();
            return;
        }

        float remaining = Mathf.Max(0f, (float)(expectedDeadline - GetSynchronizedTime()));
        float duration = Mathf.Max(0.1f, activeSite.FastenerPairWindowDuration);
        float normalized = Mathf.Clamp01(remaining / duration);
        timerFill.anchorMax = new Vector2(normalized, 1f);
        instructionText.text = $"Strike {GetPointDisplayName(requiredPoint)} - {remaining:F1} s";
        if (!panel.activeSelf)
        {
            panel.SetActive(true);
        }
    }

    private void TryBindManager()
    {
        GameplayManager manager = GameplayManager.Instance;
        if (manager == subscribedManager)
        {
            return;
        }

        UnbindManager();
        subscribedManager = manager;
        if (subscribedManager != null)
        {
            subscribedManager.OnLocalGirderFasteningWindowStarted += HandleWindowStarted;
        }
    }

    private void UnbindManager()
    {
        if (subscribedManager != null)
        {
            subscribedManager.OnLocalGirderFasteningWindowStarted -= HandleWindowStarted;
            subscribedManager = null;
        }
    }

    private void HandleWindowStarted(int componentID, int requiredWorkPointId, double deadline)
    {
        if (!IsLocalOwner() || panel == null || subscribedManager == null ||
            !subscribedManager.TryGetConstructionSite(componentID, out BridgeConstructionSite constructionSite))
        {
            return;
        }

        BridgeGirderConstructionSite girderSite = constructionSite as BridgeGirderConstructionSite;
        if (girderSite == null)
        {
            return;
        }

        activeSite = girderSite;
        requiredPoint = (BridgeGirderWorkPointId)requiredWorkPointId;
        expectedDeadline = deadline;
        confirmationExpiresAt = Time.unscaledTime + StateConfirmationGrace;
        stateConfirmed = false;
        panel.SetActive(true);
    }

    private bool IsLocalOwner()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        return networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;
    }

    private void Hide()
    {
        activeSite = null;
        stateConfirmed = false;
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    private void BuildUI()
    {
        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            return;
        }

        panel = new GameObject("GirderFasteningHUD", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.SetParent(canvas.transform, false);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(280f, 58f);
        panelRect.anchoredPosition = new Vector2(0f, -82f);
        panel.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.05f, 0.88f);

        GameObject textObject = new GameObject("Instruction", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(panelRect, false);
        textRect.anchorMin = new Vector2(0f, 0.45f);
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 0f);
        textRect.offsetMax = new Vector2(-10f, -4f);
        instructionText = textObject.GetComponent<TextMeshProUGUI>();
        instructionText.alignment = TextAlignmentOptions.Center;
        instructionText.fontSize = 18f;
        instructionText.color = Color.white;

        GameObject timerBackground = CreateImage("TimerBackground", panelRect, new Color(0.1f, 0.12f, 0.13f, 1f));
        RectTransform backgroundRect = timerBackground.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 0f);
        backgroundRect.anchorMax = new Vector2(1f, 0f);
        backgroundRect.sizeDelta = new Vector2(-20f, 12f);
        backgroundRect.anchoredPosition = new Vector2(0f, 10f);

        GameObject fillObject = CreateImage("Fill", backgroundRect, new Color(0.2f, 0.9f, 0.35f, 1f));
        timerFill = fillObject.GetComponent<RectTransform>();
        timerFill.anchorMin = Vector2.zero;
        timerFill.anchorMax = Vector2.one;
        timerFill.offsetMin = Vector2.zero;
        timerFill.offsetMax = Vector2.zero;
    }

    private static GameObject CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject result = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        result.transform.SetParent(parent, false);
        result.GetComponent<Image>().color = color;
        return result;
    }

    private static string GetPointDisplayName(BridgeGirderWorkPointId point)
    {
        int number = (int)point - (int)BridgeGirderWorkPointId.Fastener0 + 1;
        return number >= 1 && number <= 4 ? $"Fastener {number}" : "paired fastener";
    }

    private static double GetSynchronizedTime()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening
            ? networkManager.ServerTime.Time
            : Time.timeAsDouble;
    }
}
