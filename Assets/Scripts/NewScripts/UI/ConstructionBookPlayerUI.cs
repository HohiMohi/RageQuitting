using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerInputNew), typeof(PlayerHealth), typeof(NetworkObject))]
public sealed class PlayerConstructionBookUI : MonoBehaviour
{
    private PlayerInputNew input;
    private PlayerHealth health;
    private ConstructionBookController book;
    private CanvasGroup group;
    private RectTransform panel;
    private ConstructionBookView view;
    private Button previousButton;
    private Button nextButton;
    private bool open;
    private Coroutine transition;

    private void Awake()
    {
        input = GetComponent<PlayerInputNew>();
        health = GetComponent<PlayerHealth>();
    }

    private void Start()
    {
        if (IsLocalPlayer()) EnsureUi();
    }

    private void OnDisable() { Close(); }
    private void OnDestroy() { Close(); }
    private void Update() { if (open && health != null && health.IsDowned) Close(); }

    public void Open(ConstructionBookController target)
    {
        if (!IsLocalPlayer() || target == null || health != null && health.IsDowned) return;
        EnsureUi();
        if (book != target)
        {
            if (book != null) book.PageChanged -= OnPageChanged;
            book = target;
            book.PageChanged += OnPageChanged;
        }
        if (!open)
        {
            open = true;
            input.OnUI_Left += HandleUiLeft;
            input.OnUI_Right += HandleUiRight;
            input.OnUI_Back += HandleUiBack;
            input.SetGameplayUiOpen(true);
            panel.gameObject.SetActive(true);
        }
        Refresh();
    }

    public void Close()
    {
        ResetTransition();
        if (!open)
        {
            if (panel != null) panel.gameObject.SetActive(false);
            return;
        }
        open = false;
        if (input != null)
        {
            input.OnUI_Left -= HandleUiLeft;
            input.OnUI_Right -= HandleUiRight;
            input.OnUI_Back -= HandleUiBack;
            input.SetGameplayUiOpen(false);
        }
        if (book != null) book.PageChanged -= OnPageChanged;
        book = null;
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private void ResetTransition()
    {
        if (transition != null)
        {
            StopCoroutine(transition);
            transition = null;
        }
        if (group != null) group.alpha = 1f;
        if (panel != null) panel.anchoredPosition = Vector2.zero;
    }

    private void HandleUiLeft(object sender, EventArgs args) { if (book != null) book.RequestTurn(-1, transform); }
    private void HandleUiRight(object sender, EventArgs args) { if (book != null) book.RequestTurn(1, transform); }
    private void HandleUiBack(object sender, EventArgs args) { Close(); }
    private void OnPageChanged(int index, bool snap)
    {
        if (!open) return;
        if (snap)
        {
            ResetTransition();
            Refresh();
            return;
        }
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(SlideFade());
    }
    private IEnumerator SlideFade()
    {
        const float duration = 0.15f;
        for (float t = 0; t < duration * 0.5f; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / (duration * 0.5f); panel.anchoredPosition = Vector2.left * 50f * t / (duration * 0.5f); yield return null; }
        Refresh(); panel.anchoredPosition = Vector2.right * 50f;
        for (float t = 0; t < duration * 0.5f; t += Time.unscaledDeltaTime) { group.alpha = t / (duration * 0.5f); panel.anchoredPosition = Vector2.Lerp(Vector2.right * 50f, Vector2.zero, t / (duration * 0.5f)); yield return null; }
        group.alpha = 1f; panel.anchoredPosition = Vector2.zero; transition = null;
    }
    private void Refresh()
    {
        if (book == null) return;
        book.PopulateView(view);
        previousButton.interactable = book.CanGoPrevious; nextButton.interactable = book.CanGoNext;
    }

    private void EnsureUi()
    {
        if (panel != null) return;
        GameObject canvasGo = new GameObject("ConstructionBookScreenCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 90;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        GameObject panelGo = new GameObject("ConstructionBookPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image)); panelGo.transform.SetParent(canvasGo.transform, false);
        panel = panelGo.GetComponent<RectTransform>(); panel.anchorMin = new Vector2(0.04f, 0.04f); panel.anchorMax = new Vector2(0.96f, 0.96f); panel.offsetMin = panel.offsetMax = Vector2.zero;
        panelGo.GetComponent<Image>().color = new Color(0.82f, 0.68f, 0.43f, 0.98f); group = panelGo.GetComponent<CanvasGroup>();
        view = panelGo.AddComponent<ConstructionBookView>(); view.Build(new Color(0.82f, 0.68f, 0.43f, 0.98f), new Color(0.12f, 0.08f, 0.04f));
        previousButton = CreateButton("Previous", "‹", panel, new Vector2(0.01f, 0.43f), new Vector2(0.06f, 0.57f), () => { if (book != null) book.RequestTurn(-1, transform); });
        nextButton = CreateButton("Next", "›", panel, new Vector2(0.94f, 0.43f), new Vector2(0.99f, 0.57f), () => { if (book != null) book.RequestTurn(1, transform); });
        CreateButton("Close", "×", panel, new Vector2(0.94f, 0.92f), new Vector2(0.99f, 0.99f), Close);
        panel.gameObject.SetActive(false);
    }
    private bool IsLocalPlayer()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening) return true;
        NetworkObject networkObject = GetComponent<NetworkObject>();
        return networkObject != null && networkObject.IsSpawned && networkObject.IsOwner;
    }
    private static TextMeshProUGUI CreateText(string name, RectTransform parent, Vector2 min, Vector2 max)
    { GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); RectTransform rt=go.GetComponent<RectTransform>();rt.anchorMin=min;rt.anchorMax=max;rt.offsetMin=rt.offsetMax=Vector2.zero;return go.GetComponent<TextMeshProUGUI>(); }
    private static Button CreateButton(string name, string label, RectTransform parent, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>(); rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = new Color(0.22f, 0.13f, 0.06f, 0.9f); Button button = go.GetComponent<Button>(); button.onClick.AddListener(action);
        TextMeshProUGUI text = CreateText("Label", rt, Vector2.zero, Vector2.one); text.text = label; text.fontSize = 64; text.color = Color.white; text.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
