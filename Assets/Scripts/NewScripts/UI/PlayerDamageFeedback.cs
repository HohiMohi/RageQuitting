using StarterAssets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PlayerDamageFeedback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private FirstPersonController firstPersonController;
    [SerializeField] private Image damageOverlay;
    [SerializeField] private AudioSource audioSource;

    [Header("Flash")]
    [SerializeField] private Color flashColor = new Color(0.9f, 0f, 0f, 1f);
    [SerializeField] private float flashFadeDuration = 0.35f;
    [SerializeField] private float minFlashAlpha = 0.18f;
    [SerializeField] private float maxFlashAlpha = 0.62f;

    [Header("Camera Shake")]
    [SerializeField] private float shakeDuration = 0.18f;
    [SerializeField] private float shakeAmplitude = 0.055f;
    [SerializeField] private float shakeFrequency = 42f;
    [SerializeField] private float downedFeedbackMultiplier = 1.45f;

    [Header("Audio")]
    [SerializeField] private AudioClip damageClip;
    [SerializeField] private float audioVolume = 0.75f;
    [SerializeField] private float audioPitchVariance = 0.08f;

    private NetworkObject networkObject;
    private Transform cameraTarget;
    private Vector3 cameraTargetBaseLocalPosition;
    private float previousHealth;
    private float overlayAlpha;
    private float shakeTimer;
    private float shakeStrength;
    private AudioClip fallbackDamageClip;
    private bool isSubscribed;

    private void Awake()
    {
        networkObject = GetComponent<NetworkObject>();

        if (playerHealth == null)
        {
            playerHealth = GetComponent<PlayerHealth>();
        }

        if (firstPersonController == null)
        {
            firstPersonController = GetComponent<FirstPersonController>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        ResolveCameraTarget();
        EnsureOverlay();
        previousHealth = playerHealth != null ? playerHealth.CurrentHealth : 0f;
    }

    private void OnEnable()
    {
        Subscribe();
        previousHealth = playerHealth != null ? playerHealth.CurrentHealth : previousHealth;
        SetOverlayAlpha(0f);
    }

    private void OnDisable()
    {
        Unsubscribe();
        ResetCameraShake();
        SetOverlayAlpha(0f);
    }

    private void Update()
    {
        UpdateOverlay();
        UpdateCameraShake();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (isSubscribed || playerHealth == null)
        {
            return;
        }

        playerHealth.OnHealthChanged += PlayerHealth_OnHealthChanged;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed || playerHealth == null)
        {
            return;
        }

        playerHealth.OnHealthChanged -= PlayerHealth_OnHealthChanged;
        isSubscribed = false;
    }

    private void PlayerHealth_OnHealthChanged(object sender, System.EventArgs e)
    {
        if (playerHealth == null)
        {
            return;
        }

        float currentHealth = playerHealth.CurrentHealth;
        float lostHealth = previousHealth - currentHealth;
        previousHealth = currentHealth;

        if (lostHealth <= 0f || !IsLocalPlayerFeedbackTarget())
        {
            return;
        }

        PlayFeedback(lostHealth);
    }

    private void PlayFeedback(float lostHealth)
    {
        float damageNormalized = playerHealth.MaxHealth > 0f ? Mathf.Clamp01(lostHealth / playerHealth.MaxHealth) : 0f;
        float multiplier = playerHealth.IsDowned ? downedFeedbackMultiplier : 1f;

        overlayAlpha = Mathf.Max(
            overlayAlpha,
            Mathf.Clamp(Mathf.Lerp(minFlashAlpha, maxFlashAlpha, damageNormalized * 3f) * multiplier, minFlashAlpha, maxFlashAlpha));
        SetOverlayAlpha(overlayAlpha);

        shakeTimer = Mathf.Max(shakeTimer, shakeDuration * multiplier);
        shakeStrength = Mathf.Max(shakeStrength, shakeAmplitude * Mathf.Lerp(0.75f, 1.25f, damageNormalized) * multiplier);

        PlayDamageAudio(multiplier);
    }

    private void UpdateOverlay()
    {
        if (damageOverlay == null || overlayAlpha <= 0f)
        {
            return;
        }

        float fadeSpeed = flashFadeDuration > 0f ? maxFlashAlpha / flashFadeDuration : maxFlashAlpha;
        overlayAlpha = Mathf.MoveTowards(overlayAlpha, 0f, fadeSpeed * Time.deltaTime);
        SetOverlayAlpha(overlayAlpha);
    }

    private void UpdateCameraShake()
    {
        if (cameraTarget == null || shakeTimer <= 0f)
        {
            return;
        }

        shakeTimer -= Time.deltaTime;
        float normalizedTime = shakeDuration > 0f ? Mathf.Clamp01(shakeTimer / shakeDuration) : 0f;
        float strength = shakeStrength * normalizedTime;
        float noiseX = Mathf.PerlinNoise(Time.time * shakeFrequency, 0.13f) * 2f - 1f;
        float noiseY = Mathf.PerlinNoise(0.37f, Time.time * shakeFrequency) * 2f - 1f;
        cameraTarget.localPosition = cameraTargetBaseLocalPosition + new Vector3(noiseX, noiseY, 0f) * strength;

        if (shakeTimer <= 0f)
        {
            ResetCameraShake();
        }
    }

    private void ResetCameraShake()
    {
        shakeTimer = 0f;
        shakeStrength = 0f;

        if (cameraTarget != null)
        {
            cameraTarget.localPosition = cameraTargetBaseLocalPosition;
        }
    }

    private void PlayDamageAudio(float multiplier)
    {
        if (audioSource == null)
        {
            return;
        }

        AudioClip clip = damageClip != null ? damageClip : GetFallbackDamageClip();
        if (clip == null)
        {
            return;
        }

        audioSource.pitch = 1f + Random.Range(-audioPitchVariance, audioPitchVariance);
        audioSource.PlayOneShot(clip, Mathf.Clamp01(audioVolume * multiplier));
    }

    private void EnsureOverlay()
    {
        if (damageOverlay != null)
        {
            damageOverlay.raycastTarget = false;
            SetOverlayAlpha(0f);
            return;
        }

        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            return;
        }

        GameObject overlayGameObject = new GameObject("DamageFeedbackOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rectTransform = overlayGameObject.GetComponent<RectTransform>();
        rectTransform.SetParent(canvas.transform, false);
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.SetAsLastSibling();

        damageOverlay = overlayGameObject.GetComponent<Image>();
        damageOverlay.raycastTarget = false;
        SetOverlayAlpha(0f);
    }

    private void SetOverlayAlpha(float alpha)
    {
        if (damageOverlay == null)
        {
            return;
        }

        Color color = flashColor;
        color.a = Mathf.Clamp01(alpha);
        damageOverlay.color = color;
        damageOverlay.enabled = color.a > 0f;
    }

    private void ResolveCameraTarget()
    {
        if (firstPersonController == null || firstPersonController.CinemachineCameraTarget == null)
        {
            return;
        }

        cameraTarget = firstPersonController.CinemachineCameraTarget.transform;
        cameraTargetBaseLocalPosition = cameraTarget.localPosition;
    }

    private AudioClip GetFallbackDamageClip()
    {
        if (fallbackDamageClip != null)
        {
            return fallbackDamageClip;
        }

        const int sampleRate = 44100;
        const float duration = 0.12f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t * 24f);
            float lowThump = Mathf.Sin(2f * Mathf.PI * 95f * t);
            float noise = Random.Range(-1f, 1f) * 0.25f;
            samples[i] = (lowThump + noise) * envelope * 0.65f;
        }

        fallbackDamageClip = AudioClip.Create("RuntimeDamageFeedback", sampleCount, 1, sampleRate, false);
        fallbackDamageClip.SetData(samples, 0);
        return fallbackDamageClip;
    }

    private bool IsLocalPlayerFeedbackTarget()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return networkObject != null && networkObject.IsSpawned && networkObject.IsOwner;
        }

        return true;
    }
}
