using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RopeToolController))]
public sealed class PlayerRopeUI : NetworkBehaviour
{
    private RopeToolController rope;
    private GameObject visualRoot;
    private Image progressFill;
    private TMP_Text statusText;
    private TMP_Text lengthText;

    private static readonly Color BackgroundColor = new Color(0.035f, 0.045f, 0.05f, 0.9f);
    private static readonly Color ChargeColor = new Color(0.95f, 0.72f, 0.22f, 1f);
    private static readonly Color RopeColor = new Color(0.55f, 0.78f, 0.42f, 1f);
    private static readonly Color BlockedColor = new Color(0.95f, 0.25f, 0.2f, 1f);

    private void Awake()
    {
        rope = GetComponent<RopeToolController>();
        BuildRuntimeHud();
    }

    private void Update()
    {
        if (visualRoot == null || !ShouldRenderLocally())
        {
            if (visualRoot != null) visualRoot.SetActive(false);
            return;
        }

        bool isEscapeTarget = RopeToolController.TryGetRopeAttachedToPlayer(NetworkObject, out RopeToolController sourceRope);
        bool show = (rope.IsRopeSelected() && rope.CurrentState != RopeState.Inactive) || isEscapeTarget;
        visualRoot.SetActive(show);
        if (!show) return;

        if (isEscapeTarget && !rope.IsRopeSelected())
        {
            statusText.text = "ROPE ATTACHED";
            progressFill.color = RopeColor;
            progressFill.fillAmount = sourceRope.EscapeProgress;
            lengthText.text = sourceRope.ActiveProfile != null && sourceRope.ActiveProfile.allowTargetEscape
                ? $"ESCAPING {sourceRope.EscapeProgress * 100f:0}%"
                : "LOCKED";
            return;
        }

        RopeState state = rope.CurrentState;
        float progress = state == RopeState.Charging ? rope.ChargeNormalized : rope.NormalizedTension;
        progressFill.fillAmount = progress;
        progressFill.color = rope.IsBlocked || rope.IsAtHardLimit
            ? BlockedColor
            : state == RopeState.Charging ? ChargeColor : RopeColor;
        statusText.text = rope.IsBlocked
            ? "ROPE BLOCKED"
            : rope.IsAtHardLimit ? "ROPE AT LIMIT" : GetStateLabel(state);
        float maxLength = rope.ActiveProfile != null ? rope.ActiveProfile.maximumLength : 0f;
        lengthText.text = state == RopeState.Charging
            ? $"THROW {rope.ChargeNormalized * 100f:0}%"
            : $"{rope.CurrentLength:0.0} / {maxLength:0.0} m";
    }

    private bool ShouldRenderLocally()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || IsOwner;
    }

    private static string GetStateLabel(RopeState state)
    {
        return state switch
        {
            RopeState.Ready => "ROPE READY",
            RopeState.Charging => "CHARGING THROW",
            RopeState.Flying => "ROPE FLYING",
            RopeState.Loose => "LOOSE END",
            RopeState.Attached => "ROPE ATTACHED",
            RopeState.Reeling => "REELING",
            RopeState.PayingOut => "PAYING OUT",
            _ => string.Empty
        };
    }

    private void BuildRuntimeHud()
    {
        GameObject canvasObject = new GameObject("RopeHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        canvasObject.GetComponent<GraphicRaycaster>().enabled = false;

        visualRoot = new GameObject("RopeStatus", typeof(RectTransform), typeof(Image));
        visualRoot.transform.SetParent(canvasObject.transform, false);
        RectTransform panel = visualRoot.GetComponent<RectTransform>();
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 92f);
        panel.sizeDelta = new Vector2(300f, 64f);
        visualRoot.GetComponent<Image>().color = BackgroundColor;

        statusText = CreateText("Status", panel, new Vector2(10f, 35f), new Vector2(180f, 22f), TextAlignmentOptions.Left);
        lengthText = CreateText("Length", panel, new Vector2(190f, 35f), new Vector2(100f, 22f), TextAlignmentOptions.Right);

        GameObject barBackground = new GameObject("BarBackground", typeof(RectTransform), typeof(Image));
        barBackground.transform.SetParent(panel, false);
        RectTransform backgroundRect = barBackground.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.zero;
        backgroundRect.pivot = Vector2.zero;
        backgroundRect.anchoredPosition = new Vector2(10f, 12f);
        backgroundRect.sizeDelta = new Vector2(280f, 12f);
        barBackground.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.15f, 1f);

        GameObject fillObject = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
        fillObject.transform.SetParent(backgroundRect, false);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        progressFill = fillObject.GetComponent<Image>();
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillOrigin = 0;
        progressFill.fillAmount = 0f;
        progressFill.color = RopeColor;
        visualRoot.SetActive(false);
    }

    private static TMP_Text CreateText(string name, Transform parent, Vector2 position, Vector2 size, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = 15f;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
}
