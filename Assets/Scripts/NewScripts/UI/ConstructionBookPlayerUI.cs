using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerInputNew), typeof(PlayerHealth), typeof(NetworkObject))]
public sealed class PlayerConstructionBookUI : MonoBehaviour
{
    private const float PageTurnDuration = 0.15f;
    private const float PageSlideDistance = 24f;

    private PlayerInputNew input;
    private PlayerHealth health;
    private ConstructionBookController book;
    private CanvasGroup group;   // fades the page contents during a page turn
    private RectTransform panel; // full-screen root, hidden while the book is closed
    private RectTransform pages; // page contents that slide during a page turn
    private ConstructionBookScreenView view;
    private bool open;
    private bool presentationDirty;
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
            if (book != null)
            {
                book.PageChanged -= OnPageChanged;
                book.PresentationChanged -= OnPresentationChanged;
                book.Despawned -= OnBookDespawned;
            }
            book = target;
            book.PageChanged += OnPageChanged;
            book.PresentationChanged += OnPresentationChanged;
            book.Despawned += OnBookDespawned;
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
        if (book != null)
        {
            book.PageChanged -= OnPageChanged;
            book.PresentationChanged -= OnPresentationChanged;
            book.Despawned -= OnBookDespawned;
        }
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
        if (pages != null) pages.anchoredPosition = Vector2.zero;
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
    private void OnPresentationChanged()
    {
        if (!open) return;
        if (transition != null) presentationDirty = true;
        else Refresh();
    }
    private void OnBookDespawned() => Close();
    private IEnumerator SlideFade()
    {
        // Only the page contents turn; the book, its signs and the hints stay put.
        const float half = PageTurnDuration * 0.5f;
        for (float t = 0; t < half; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / half; pages.anchoredPosition = Vector2.left * PageSlideDistance * t / half; yield return null; }
        Refresh(); pages.anchoredPosition = Vector2.right * PageSlideDistance;
        for (float t = 0; t < half; t += Time.unscaledDeltaTime) { group.alpha = t / half; pages.anchoredPosition = Vector2.Lerp(Vector2.right * PageSlideDistance, Vector2.zero, t / half); yield return null; }
        group.alpha = 1f; pages.anchoredPosition = Vector2.zero; transition = null;
        if (presentationDirty) { presentationDirty = false; Refresh(); }
    }
    private void Refresh()
    {
        if (book == null) return;
        book.PopulateView(view, () => transition != null);
        view.SetNavigation(book.CanGoPrevious, book.CanGoNext);
    }

    private void EnsureUi()
    {
        if (panel != null) return;
        GameObject canvasGo = new GameObject("ConstructionBookScreenCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 90;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
        // Expand keeps the whole 1920x1080 book spread on screen for every aspect ratio.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        GameObject panelGo = new GameObject("ConstructionBookPanel", typeof(RectTransform)); panelGo.transform.SetParent(canvasGo.transform, false);
        panel = panelGo.GetComponent<RectTransform>(); panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one; panel.offsetMin = panel.offsetMax = Vector2.zero;
        view = panelGo.AddComponent<ConstructionBookScreenView>(); view.Build();
        group = view.PagesGroup; pages = view.Pages;
        view.PreviousButton.onClick.AddListener(() => { if (book != null) book.RequestTurn(-1, transform); });
        view.NextButton.onClick.AddListener(() => { if (book != null) book.RequestTurn(1, transform); });
        view.CloseButton.onClick.AddListener(Close);
        panel.gameObject.SetActive(false);
    }
    private bool IsLocalPlayer()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening) return true;
        NetworkObject networkObject = GetComponent<NetworkObject>();
        return networkObject != null && networkObject.IsSpawned && networkObject.IsOwner;
    }
}
