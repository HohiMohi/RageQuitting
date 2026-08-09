using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerFlexibleSaplingUI : MonoBehaviour
{
    private PlayerInteractionNew player;
    private GameObject panel;
    private RectTransform marker;
    private Image markerImage;
    private Image leftTargetZone;
    private Image rightTargetZone;
    private TMP_Text pullArrow;
    private TMP_Text progressText;
    private GameObject timerRoot;
    private Image timerFill;
    private RectTransform timerFillRect;
    private FlexibleSaplingController sapling;
    private float displayedTilt;
    private float displayedTiltVelocity;

    private void Awake()
    {
        player = GetComponent<PlayerInteractionNew>();
        BuildUI();
        Hide();
    }

    private void Update()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned && !networkObject.IsOwner)
        {
            Hide();
            return;
        }

        if (sapling == null || player == null || player.ActiveFlexibleSapling != sapling)
        {
            Hide();
            return;
        }

        if (!panel.activeSelf) panel.SetActive(true);
        float height = 300f;
        float localTilt = GetLocalTilt(sapling.GetDisplayTilt(player));
        displayedTilt = Mathf.SmoothDamp(displayedTilt, localTilt, ref displayedTiltVelocity, 0.08f, Mathf.Infinity, Time.unscaledDeltaTime);
        marker.anchoredPosition = new Vector2(0f, Mathf.Clamp(displayedTilt, -1f, 1f) * height * 0.5f);
        UpdateTargetZones(height);
        leftTargetZone.color = new Color(0.2f, 0.95f, 0.35f, 0.95f);
        rightTargetZone.color = new Color(0.2f, 0.55f, 0.3f, 0.25f);
        markerImage.color = GetMarkerColor(sapling.PullFeedback);

        bool localTurn = sapling.IsPlayersTurn(player);
        float pulse = localTurn ? 1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.12f : 1f;
        pullArrow.rectTransform.localScale = Vector3.one * pulse;
        pullArrow.color = localTurn
            ? new Color(0.25f, 1f, 0.4f, 0.9f + Mathf.Sin(Time.unscaledTime * 8f) * 0.1f)
            : new Color(1f, 1f, 1f, 0.12f);
        progressText.text = $"{sapling.CompletedPulls} / {sapling.RequiredPulls}";
        timerRoot.SetActive(sapling.IsCurrentStageTimed);
        if (sapling.IsCurrentStageTimed)
        {
            float normalizedTime = sapling.NormalizedRemainingStageTime;
            timerFillRect.anchorMax = new Vector2(0.85f, 0.05f + normalizedTime * 0.9f);
            timerFill.color = localTurn
                ? new Color(0.25f, 1f, 0.4f, 0.95f)
                : new Color(0.55f, 0.65f, 0.55f, 0.28f);
        }
    }

    public void Show(FlexibleSaplingController target)
    {
        sapling = target;
        displayedTilt = target != null ? GetLocalTilt(target.GetDisplayTilt(player)) : 0f;
        displayedTiltVelocity = 0f;
        if (panel != null) panel.SetActive(target != null);
    }

    public void Hide()
    {
        sapling = null;
        if (panel != null) panel.SetActive(false);
    }

    private void BuildUI()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned && !networkObject.IsOwner)
        {
            enabled = false;
            return;
        }

        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null) return;

        panel = new GameObject("FlexibleSaplingHUD", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.SetParent(canvas.transform, false);
        panelRect.anchorMin = new Vector2(0.88f, 0.5f);
        panelRect.anchorMax = new Vector2(0.88f, 0.5f);
        panelRect.sizeDelta = new Vector2(180f, 360f);
        panel.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.04f, 0.8f);

        GameObject bar = CreateImage("TiltBar", panelRect, new Color(0.85f, 0.72f, 0.3f, 0.9f));
        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.sizeDelta = new Vector2(12f, 300f);
        barRect.anchoredPosition = new Vector2(35f, 8f);

        CreateDangerZone(barRect, -135f);
        CreateDangerZone(barRect, 135f);
        leftTargetZone = CreateTargetZone(barRect, -112.5f);
        rightTargetZone = CreateTargetZone(barRect, 112.5f);
        marker = CreateImage("TiltMarker", barRect, new Color(0.2f, 1f, 0.4f, 1f)).GetComponent<RectTransform>();
        marker.sizeDelta = new Vector2(28f, 10f);
        markerImage = marker.GetComponent<Image>();

        GameObject arrowObject = new GameObject("PullDownArrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform arrowRect = arrowObject.GetComponent<RectTransform>();
        arrowRect.SetParent(panelRect, false);
        arrowRect.sizeDelta = new Vector2(72f, 80f);
        arrowRect.anchoredPosition = new Vector2(-42f, 8f);
        pullArrow = arrowObject.GetComponent<TextMeshProUGUI>();
        pullArrow.text = "\u25BC";
        pullArrow.alignment = TextAlignmentOptions.Center;
        pullArrow.fontSize = 58f;

        timerRoot = CreateImage("StageTimer", panelRect, new Color(0.08f, 0.1f, 0.08f, 0.9f));
        RectTransform timerRect = timerRoot.GetComponent<RectTransform>();
        timerRect.sizeDelta = new Vector2(14f, 92f);
        timerRect.anchoredPosition = new Vector2(-76f, 8f);
        timerFill = CreateImage("Fill", timerRect, new Color(0.25f, 1f, 0.4f, 0.95f)).GetComponent<Image>();
        timerFillRect = timerFill.rectTransform;
        timerFillRect.anchorMin = new Vector2(0.15f, 0.05f);
        timerFillRect.anchorMax = new Vector2(0.85f, 0.95f);
        timerFillRect.offsetMin = Vector2.zero;
        timerFillRect.offsetMax = Vector2.zero;

        GameObject textObject = new GameObject("Progress", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(panelRect, false);
        textRect.sizeDelta = new Vector2(180f, 26f);
        textRect.anchoredPosition = new Vector2(0f, -166f);
        progressText = textObject.GetComponent<TextMeshProUGUI>();
        progressText.alignment = TextAlignmentOptions.Center;
        progressText.fontSize = 18f;
        progressText.color = Color.white;
    }

    private static GameObject CreateImage(string name, Transform parent, Color color)
    {
        GameObject result = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        result.transform.SetParent(parent, false);
        result.GetComponent<Image>().color = color;
        return result;
    }

    private static void CreateDangerZone(RectTransform bar, float y)
    {
        RectTransform danger = CreateImage("BreakZone", bar, new Color(0.9f, 0.12f, 0.08f, 0.95f)).GetComponent<RectTransform>();
        danger.sizeDelta = new Vector2(18f, 30f);
        danger.anchoredPosition = new Vector2(0f, y);
    }

    private static Image CreateTargetZone(RectTransform bar, float y)
    {
        Image zone = CreateImage("TargetZone", bar, new Color(0.2f, 0.95f, 0.35f, 0.95f)).GetComponent<Image>();
        zone.rectTransform.sizeDelta = new Vector2(22f, 21f);
        zone.rectTransform.anchoredPosition = new Vector2(0f, y);
        return zone;
    }

    private static Color GetMarkerColor(FlexibleSaplingPullFeedback feedback)
    {
        switch (feedback)
        {
            case FlexibleSaplingPullFeedback.WaitingForEvaluation:
                return new Color(1f, 0.82f, 0.15f, 1f);
            case FlexibleSaplingPullFeedback.Success:
                return new Color(0.2f, 1f, 0.4f, 1f);
            case FlexibleSaplingPullFeedback.Failure:
                return new Color(1f, 0.18f, 0.12f, 1f);
            default:
                return Color.white;
        }
    }

    private void UpdateTargetZones(float barHeight)
    {
        float center = (sapling.TargetTiltMinimum + sapling.TargetTiltMaximum) * 0.5f;
        float zoneHeight = Mathf.Max(6f, (sapling.TargetTiltMaximum - sapling.TargetTiltMinimum) * barHeight * 0.5f);
        float y = center * barHeight * 0.5f;
        leftTargetZone.rectTransform.sizeDelta = new Vector2(22f, zoneHeight);
        rightTargetZone.rectTransform.sizeDelta = new Vector2(22f, zoneHeight);
        leftTargetZone.rectTransform.anchoredPosition = new Vector2(0f, -y);
        rightTargetZone.rectTransform.anchoredPosition = new Vector2(0f, y);
    }

    private float GetLocalTilt(float authoritativeTilt)
    {
        if (player == null)
        {
            return authoritativeTilt;
        }

        return player.ActiveFlexibleSaplingSide == FlexibleSaplingGripSide.Left
            ? authoritativeTilt
            : -authoritativeTilt;
    }
}
