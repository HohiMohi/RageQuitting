using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthUI : MonoBehaviour
{
    [SerializeField] private Image healthMeterHolder;
    [SerializeField] private TMP_Text healthValueText;
    [SerializeField] private PlayerHealth playerHealth;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged += HandleHealthChanged;
            playerHealth.OnDownedStateChanged += HandleHealthChanged;
        }

        UpdateVisual();
    }

    private void OnDisable()
    {
        if (playerHealth == null)
        {
            return;
        }

        playerHealth.OnHealthChanged -= HandleHealthChanged;
        playerHealth.OnDownedStateChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(object sender, System.EventArgs e)
    {
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        EnsureReferences();
        if (playerHealth == null)
        {
            return;
        }

        if (healthMeterHolder != null)
        {
            float healthNormalized = playerHealth.GetHealthNormalized();
            healthMeterHolder.fillAmount = healthNormalized;
            SetHorizontalFillScale(healthMeterHolder, healthNormalized);
        }

        if (healthValueText != null)
        {
            healthValueText.text = $"{playerHealth.CurrentHealth:0} / {playerHealth.MaxHealth:0}";
        }
    }

    private static void SetHorizontalFillScale(Image fillImage, float normalizedValue)
    {
        RectTransform fillRect = fillImage.rectTransform;
        fillRect.pivot = Vector2.zero;
        fillRect.localScale = new Vector3(Mathf.Clamp01(normalizedValue), 1f, 1f);
    }

    private void EnsureReferences()
    {
        if (playerHealth == null)
        {
            playerHealth = GetComponentInParent<PlayerHealth>();
        }

        if (playerHealth == null && transform.root != null)
        {
            playerHealth = transform.root.GetComponentInChildren<PlayerHealth>(true);
        }
    }
}
