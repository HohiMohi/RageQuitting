using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ConcreteMixerCrankUI : MonoBehaviour
{
    private static Sprite runtimeDialSprite;

    [SerializeField] private ConcreteMixerController mixer;
    [SerializeField] private Color panelColor = new Color(0.055f, 0.065f, 0.07f, 0.96f);
    [SerializeField] private Color dialColor = new Color(0.18f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color markerColor = new Color(0.3f, 0.85f, 0.45f, 1f);
    [SerializeField] private float markerRadius = 132f;

    private GameObject visualRoot;
    private RectTransform dialCenter;
    private RectTransform marker;
    private TMP_Text statusText;
    private TMP_Text progressText;
    private PlayerInputNew localPlayerInput;
    private PlayerHealth localPlayerHealth;
    private bool isOpen;
    private bool isDragging;
    private float lastPointerAngle;
    private float targetDegrees;
    private float displayedDegrees;
    private float displayVelocity;
    private float unsentDegrees;
    private float nextSendTime;

    private void Awake()
    {
        if (mixer == null) mixer = GetComponentInParent<ConcreteMixerController>();
        BuildUi();
    }

    private void Start()
    {
        if (mixer == null) { enabled = false; return; }
        mixer.CrankGranted += OnCrankGranted;
        mixer.CrankRevoked += OnCrankRevoked;
        mixer.CrankDenied += OnCrankDenied;
        mixer.StateChanged += OnMixerStateChanged;
    }

    private void OnDestroy()
    {
        if (mixer != null)
        {
            mixer.CrankGranted -= OnCrankGranted;
            mixer.CrankRevoked -= OnCrankRevoked;
            mixer.CrankDenied -= OnCrankDenied;
            mixer.StateChanged -= OnMixerStateChanged;
        }
        DetachInput();
    }

    private void Update()
    {
        if (!isOpen || mixer == null) return;
        if (localPlayerHealth != null && localPlayerHealth.IsDowned)
        {
            Close(true);
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
        {
            float pointerAngle = GetPointerAngle(mouse.position.ReadValue());
            if (!isDragging)
            {
                isDragging = true;
                lastPointerAngle = pointerAngle;
            }
            else
            {
                float delta = Mathf.DeltaAngle(lastPointerAngle, pointerAngle);
                lastPointerAngle = pointerAngle;
                if (delta > 0f)
                {
                    targetDegrees = Mathf.Min(targetDegrees + delta, displayedDegrees + 150f);
                }
            }
        }
        else
        {
            isDragging = false;
        }

        float previous = displayedDegrees;
        float response = mixer.Profile != null ? mixer.Profile.CrankResponseTime : 0.12f;
        float maximumSpeed = mixer.Profile != null ? mixer.Profile.MaximumCrankAngularSpeed : 240f;
        displayedDegrees = Mathf.SmoothDamp(displayedDegrees, targetDegrees, ref displayVelocity,
            response, maximumSpeed, Time.unscaledDeltaTime);
        float moved = Mathf.Max(0f, displayedDegrees - previous);
        unsentDegrees += moved;

        float interval = mixer.Profile != null ? mixer.Profile.CrankInputInterval : 0.05f;
        if (unsentDegrees > 0.001f && Time.unscaledTime >= nextSendTime)
        {
            mixer.RequestCrankDelta(unsentDegrees);
            unsentDegrees = 0f;
            nextSendTime = Time.unscaledTime + interval;
        }

        float confirmed = mixer.DrumRotationDegrees;
        if (confirmed > displayedDegrees)
        {
            displayedDegrees = confirmed;
            targetDegrees = Mathf.Max(targetDegrees, confirmed);
        }
        RefreshUi();
    }

    private void OnCrankGranted(object sender, EventArgs e)
    {
        ResolveLocalPlayer();
        if (localPlayerInput == null) return;
        isOpen = true;
        isDragging = false;
        displayedDegrees = mixer.DrumRotationDegrees;
        targetDegrees = displayedDegrees;
        displayVelocity = 0f;
        unsentDegrees = 0f;
        nextSendTime = Time.unscaledTime;
        visualRoot.SetActive(true);
        localPlayerInput.OnUI_Interact += OnCloseRequested;
        localPlayerInput.OnUI_Back += OnCloseRequested;
        if (localPlayerHealth != null) localPlayerHealth.OnDownedStateChanged += OnDownedChanged;
        localPlayerInput.SetGameplayUiOpen(true);
        RefreshUi();
    }

    private void OnCrankRevoked(object sender, EventArgs e) => Close(false);
    private void OnCrankDenied(string reason) { }

    private void OnMixerStateChanged(object sender, EventArgs e)
    {
        if (isOpen && (mixer.Mode != ConcreteMixerMode.Mixing ||
            mixer.BatchState == ConcreteMixerBatchState.ConcreteReady ||
            mixer.BatchState == ConcreteMixerBatchState.RuinedMix))
        {
            Close(false);
        }
        else if (isOpen) RefreshUi();
    }

    private void Close(bool release)
    {
        if (!isOpen) return;
        if (unsentDegrees > 0.001f) mixer.RequestCrankDelta(unsentDegrees);
        unsentDegrees = 0f;
        isOpen = false;
        isDragging = false;
        visualRoot.SetActive(false);
        DetachInput();
        if (release) mixer.RequestReleaseCrank();
    }

    private void ResolveLocalPlayer()
    {
        DetachInput();
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && manager.LocalClient != null && manager.LocalClient.PlayerObject != null)
        {
            localPlayerInput = manager.LocalClient.PlayerObject.GetComponent<PlayerInputNew>();
            localPlayerHealth = manager.LocalClient.PlayerObject.GetComponent<PlayerHealth>();
            return;
        }
        localPlayerInput = FindFirstObjectByType<PlayerInputNew>();
        localPlayerHealth = localPlayerInput != null ? localPlayerInput.GetComponent<PlayerHealth>() : null;
    }

    private void DetachInput()
    {
        if (localPlayerInput != null)
        {
            localPlayerInput.OnUI_Interact -= OnCloseRequested;
            localPlayerInput.OnUI_Back -= OnCloseRequested;
            localPlayerInput.SetGameplayUiOpen(false);
        }
        if (localPlayerHealth != null) localPlayerHealth.OnDownedStateChanged -= OnDownedChanged;
        localPlayerInput = null;
        localPlayerHealth = null;
    }

    private void OnCloseRequested(object sender, EventArgs e) => Close(true);
    private void OnDownedChanged(object sender, EventArgs e) { if (localPlayerHealth != null && localPlayerHealth.IsDowned) Close(true); }

    private float GetPointerAngle(Vector2 screenPosition)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(dialCenter, screenPosition, null, out Vector2 point)) return lastPointerAngle;
        return Mathf.Repeat(Mathf.Atan2(point.x, point.y) * Mathf.Rad2Deg, 360f);
    }

    private void RefreshUi()
    {
        if (marker != null)
        {
            float angle = Mathf.Repeat(displayedDegrees, 360f);
            float radians = angle * Mathf.Deg2Rad;
            marker.anchoredPosition = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * markerRadius;
            marker.localRotation = Quaternion.Euler(0f, 0f, -angle);
        }
        if (progressText != null)
        {
            float rotations = mixer.MixingDegrees / 360f;
            int required = mixer.Profile != null ? mixer.Profile.RequiredRotations : 6;
            progressText.text = $"Rotations {rotations:0.0} / {required} - limit {mixer.MaximumMixingProgress:P0}";
        }
        if (statusText != null) statusText.text = mixer.GetStatusText();
    }

    private void BuildUi()
    {
        GameObject canvasObject = new GameObject("ConcreteMixerCrankCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 85;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        visualRoot = CreateImage("Panel", canvasObject.transform, panelColor, new Vector2(460f, 520f)).gameObject;
        RectTransform panel = visualRoot.GetComponent<RectTransform>();
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;

        TMP_Text title = CreateText("Title", panel, "CONCRETE MIXER", 28f, new Vector2(400f, 42f));
        title.rectTransform.anchoredPosition = new Vector2(0f, 220f);
        statusText = CreateText("Status", panel, string.Empty, 18f, new Vector2(410f, 58f));
        statusText.rectTransform.anchoredPosition = new Vector2(0f, 170f);

        Image dial = CreateImage("Dial", panel, dialColor, new Vector2(330f, 330f));
        dialCenter = dial.rectTransform;
        dialCenter.anchoredPosition = new Vector2(0f, -20f);
        dial.sprite = GetRuntimeDialSprite();
        dial.type = Image.Type.Simple;
        dial.preserveAspect = true;

        Image markerImage = CreateImage("Marker", dialCenter, markerColor, new Vector2(42f, 62f));
        marker = markerImage.rectTransform;
        marker.pivot = new Vector2(0.5f, 0.2f);
        progressText = CreateText("Progress", panel, string.Empty, 22f, new Vector2(400f, 36f));
        progressText.rectTransform.anchoredPosition = new Vector2(0f, -218f);
        TMP_Text hint = CreateText("Hint", panel, "Hold LMB and move clockwise. E / Esc closes.", 16f, new Vector2(420f, 28f));
        hint.color = new Color(0.72f, 0.75f, 0.76f, 1f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, -246f);
        visualRoot.SetActive(false);
    }

    private static Image CreateImage(string name, Transform parent, Color color, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Sprite GetRuntimeDialSprite()
    {
        if (runtimeDialSprite != null)
        {
            return runtimeDialSprite;
        }

        const int textureSize = 128;
        float center = (textureSize - 1) * 0.5f;
        float radius = center - 1f;
        Color32[] pixels = new Color32[textureSize * textureSize];

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius + 0.75f - distance) * 255f);
                pixels[y * textureSize + x] = new Color32(255, 255, 255, alpha);
            }
        }

        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false, true)
        {
            name = "ConcreteMixerRuntimeDial",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        runtimeDialSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f);
        runtimeDialSprite.name = "ConcreteMixerRuntimeDialSprite";
        runtimeDialSprite.hideFlags = HideFlags.HideAndDontSave;
        return runtimeDialSprite;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, float size, Vector2 dimensions)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = dimensions;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }
}
