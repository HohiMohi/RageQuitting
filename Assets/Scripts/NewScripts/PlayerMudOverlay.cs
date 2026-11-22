using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class PlayerMudOverlay : MonoBehaviour
{
    private static readonly int AmountId = Shader.PropertyToID("_MudAmount");
    private static readonly int ThresholdId = Shader.PropertyToID("_ThresholdMap");
    private static readonly int FeatherId = Shader.PropertyToID("_ThresholdFeather");

    [SerializeField] private PlayerMudOverlayStyleSO style;
    [SerializeField] private Image mudTextureImage;
    [SerializeField] private Image dripImage;
    [SerializeField] private Shader overlayShader;
    [SerializeField, Range(0.001f, 0.1f)] private float thresholdFeather = 0.012f;
    [SerializeField, Min(0f)] private float amountSmoothingSeconds = 0.2f;
    [SerializeField, Range(0f, 1f)] private float dripActivationThreshold = 0.35f;

    private Material runtimeMaterial;
    private CanvasGroup overlayGroup;
    private System.Random random;
    private float targetAmount;
    private float displayedAmount;
    private float nextDripStartTime;
    private float dripStartTime;
    private float dripDuration;
    private float dripFadeRemaining;
    private float lastParentWidth = -1f;
    private float lastParentHeight = -1f;
    private Vector2 dripStart;
    private Vector2 dripEnd;
    private bool locallyActive;
    private bool hasScheduledDrip;
    private bool dripRunning;
    private bool dripFading;

    private void OnEnable()
    {
        if (random == null) random = new System.Random(Environment.TickCount ^ GetInstanceID());
        Configure();
        HideImmediately();
    }

    private void Configure()
    {
        if (overlayGroup == null) overlayGroup = GetComponent<CanvasGroup>();
        if (mudTextureImage != null)
        {
            mudTextureImage.raycastTarget = false;
            mudTextureImage.type = Image.Type.Simple;
            mudTextureImage.preserveAspect = false;
            if (style != null && style.MudTextureSprite != null) mudTextureImage.sprite = style.MudTextureSprite;
            if (runtimeMaterial == null && overlayShader != null) runtimeMaterial = new Material(overlayShader);
            if (runtimeMaterial != null)
            {
                runtimeMaterial.SetTexture(ThresholdId, style != null ? style.ThresholdCoverageMap : null);
                runtimeMaterial.SetFloat(FeatherId, Mathf.Clamp(thresholdFeather, 0.001f, 0.1f));
                mudTextureImage.material = runtimeMaterial;
            }
        }
        if (dripImage != null)
        {
            dripImage.raycastTarget = false;
            dripImage.preserveAspect = true;
            if (style != null && style.DripSprite != null) dripImage.sprite = style.DripSprite;
            FitDripBounds(true);
            dripImage.gameObject.SetActive(false);
        }
    }

    internal void SetMudState(float amount, bool isLocalOwner, float maximumAlpha)
    {
        locallyActive = isLocalOwner;
        targetAmount = isLocalOwner ? Mathf.Clamp01(amount) : 0f;
        if (overlayGroup != null)
        {
            overlayGroup.interactable = false;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.alpha = isLocalOwner && targetAmount > 0f ? Mathf.Clamp01(maximumAlpha) : 0f;
        }
        if (!locallyActive || targetAmount <= 0f)
        {
            HideImmediately();
            return;
        }
        if (mudTextureImage != null) mudTextureImage.enabled = runtimeMaterial != null;
        if (targetAmount < dripActivationThreshold)
        {
            hasScheduledDrip = false;
            if (dripRunning) BeginDripFade();
        }
    }

    private void Update() => Tick(Time.time, Time.deltaTime);

    private void Tick(float gameTime, float deltaTime)
    {
        if (!locallyActive || targetAmount <= 0f) return;
        FitDripBounds(false);
        float smoothTime = Mathf.Clamp(amountSmoothingSeconds, 0f, 0.25f);
        if (smoothTime <= 0f) displayedAmount = targetAmount;
        else displayedAmount = Mathf.MoveTowards(displayedAmount, targetAmount, Mathf.Max(0f, deltaTime) / smoothTime);
        if (runtimeMaterial != null) runtimeMaterial.SetFloat(AmountId, displayedAmount);
        UpdateDrip(gameTime, deltaTime);
    }

    private void UpdateDrip(float gameTime, float deltaTime)
    {
        if (dripImage == null || style == null || style.DripSprite == null)
        {
            hasScheduledDrip = false;
            dripRunning = false;
            return;
        }
        if (targetAmount < dripActivationThreshold)
        {
            hasScheduledDrip = false;
            if (dripRunning) FadeDrip(deltaTime);
            return;
        }
        if (dripFading)
        {
            FadeDrip(deltaTime);
            return;
        }
        if (!dripRunning)
        {
            if (!hasScheduledDrip)
            {
                nextDripStartTime = gameTime + RandomRange(7f, 12f);
                hasScheduledDrip = true;
            }
            if (gameTime < nextDripStartTime) return;
            StartDrip(gameTime);
        }
        float t = Mathf.Clamp01((gameTime - dripStartTime) / dripDuration);
        dripImage.rectTransform.anchoredPosition = Vector2.Lerp(dripStart, dripEnd, t);
        float edgeFade = Mathf.Min(Mathf.Clamp01(t / 0.14f), Mathf.Clamp01((1f - t) / 0.16f));
        SetDripAlpha(edgeFade);
        if (t >= 1f)
        {
            dripRunning = false;
            dripImage.gameObject.SetActive(false);
            // The configured delay is start-to-start, including the time spent moving.
            nextDripStartTime = dripStartTime + RandomRange(7f, 12f);
            hasScheduledDrip = true;
        }
    }

    private void StartDrip(float gameTime)
    {
        RectTransform parent = dripImage.rectTransform.parent as RectTransform;
        float width = parent != null ? parent.rect.width : Screen.width;
        float height = parent != null ? parent.rect.height : Screen.height;
        dripStart = new Vector2(RandomRange(-width * 0.5f, width * 0.5f), RandomRange(-height * 0.5f, height * 0.5f));
        dripEnd = dripStart + new Vector2(RandomRange(-width * 0.025f, width * 0.025f), -height / 3f);
        dripDuration = RandomRange(4f, 6f);
        dripStartTime = gameTime;
        hasScheduledDrip = false;
        dripRunning = true;
        dripFading = false;
        dripFadeRemaining = 0f;
        dripImage.rectTransform.anchoredPosition = dripStart;
        SetDripAlpha(0f);
        dripImage.gameObject.SetActive(true);
    }

    private void BeginDripFade()
    {
        if (dripFading) return;
        dripFading = true;
        dripFadeRemaining = 0.2f;
    }

    private void FadeDrip(float deltaTime)
    {
        if (!dripRunning)
        {
            dripFading = false;
            dripImage.gameObject.SetActive(false);
            return;
        }
        dripFadeRemaining -= Mathf.Max(0f, deltaTime);
        float currentAlpha = dripImage.color.a;
        SetDripAlpha(Mathf.Min(currentAlpha, Mathf.Clamp01(dripFadeRemaining / 0.2f)));
        if (dripFadeRemaining <= 0f)
        {
            dripRunning = false;
            dripFading = false;
            dripImage.gameObject.SetActive(false);
        }
    }

    private void SetDripAlpha(float alpha)
    {
        Color color = dripImage.color;
        color.a = Mathf.Clamp01(alpha);
        dripImage.color = color;
    }

    private void FitDripBounds(bool force)
    {
        if (dripImage == null || dripImage.sprite == null) return;
        RectTransform parent = dripImage.rectTransform.parent as RectTransform;
        float width = parent != null ? parent.rect.width : Screen.width;
        float height = parent != null ? parent.rect.height : Screen.height;
        if (width <= 0f || height <= 0f || (!force && Mathf.Approximately(width, lastParentWidth) &&
            Mathf.Approximately(height, lastParentHeight))) return;
        lastParentWidth = width;
        lastParentHeight = height;
        RectTransform rect = dripImage.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        float aspect = dripImage.sprite.rect.width / Mathf.Max(1f, dripImage.sprite.rect.height);
        float maxWidth = width * 0.1f;
        float maxHeight = height * 0.1f;
        float drawWidth = Mathf.Min(maxWidth, maxHeight * aspect);
        rect.sizeDelta = new Vector2(drawWidth, drawWidth / aspect);
    }

    private float RandomRange(float minimum, float maximum)
    {
        if (random == null) random = new System.Random(Environment.TickCount ^ GetInstanceID());
        return (float)(minimum + random.NextDouble() * (maximum - minimum));
    }

    private void HideImmediately()
    {
        locallyActive = false;
        targetAmount = 0f;
        displayedAmount = 0f;
        hasScheduledDrip = false;
        dripRunning = false;
        dripFading = false;
        dripFadeRemaining = 0f;
        if (runtimeMaterial != null) runtimeMaterial.SetFloat(AmountId, 0f);
        if (overlayGroup != null) overlayGroup.alpha = 0f;
        if (mudTextureImage != null) mudTextureImage.enabled = false;
        if (dripImage != null) dripImage.gameObject.SetActive(false);
    }

    private void OnDisable() => HideImmediately();

    private void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }

}
