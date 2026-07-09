using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthUI : MonoBehaviour
{
    [SerializeField] private Image healthMeterHolder;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private Vector2 anchoredPosition = new Vector2(20f, 48f);
    [SerializeField] private Vector2 size = new Vector2(180f, 14f);

    private RectTransform healthMeterFillRectTransform;

    private void Awake()
    {
        if (playerHealth == null)
        {
            playerHealth = GetComponentInParent<PlayerHealth>();
        }

        if (healthMeterHolder == null)
        {
            CreateDefaultHealthMeter();
        }
    }

    private void OnEnable()
    {
        EnsureReferences();

        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged += PlayerHealth_OnHealthChanged;
            playerHealth.OnDownedStateChanged += PlayerHealth_OnHealthChanged;
        }

        UpdateVisual();
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged -= PlayerHealth_OnHealthChanged;
            playerHealth.OnDownedStateChanged -= PlayerHealth_OnHealthChanged;
        }
    }

    private void Update()
    {
        UpdateVisual();
    }

    private void PlayerHealth_OnHealthChanged(object sender, System.EventArgs e)
    {
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        EnsureReferences();

        if (healthMeterHolder == null || healthMeterFillRectTransform == null || playerHealth == null)
        {
            return;
        }

        float healthNormalized = playerHealth.GetHealthNormalized();
        healthMeterHolder.fillAmount = healthNormalized;
        healthMeterFillRectTransform.anchorMax = new Vector2(healthNormalized, 1f);
        healthMeterFillRectTransform.offsetMax = Vector2.zero;
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

        if (healthMeterHolder != null && healthMeterFillRectTransform == null)
        {
            healthMeterFillRectTransform = healthMeterHolder.rectTransform;
        }

        if (healthMeterHolder == null)
        {
            CreateDefaultHealthMeter();
        }
    }

    private void CreateDefaultHealthMeter()
    {
        RectTransform parentRectTransform = transform as RectTransform;
        if (parentRectTransform == null)
        {
            return;
        }

        GameObject backgroundGameObject = new GameObject("PlayerHealthMeter", typeof(RectTransform), typeof(Image));
        RectTransform backgroundRectTransform = backgroundGameObject.GetComponent<RectTransform>();
        backgroundRectTransform.SetParent(parentRectTransform, false);
        backgroundRectTransform.anchorMin = new Vector2(0f, 0f);
        backgroundRectTransform.anchorMax = new Vector2(0f, 0f);
        backgroundRectTransform.pivot = new Vector2(0f, 0f);
        backgroundRectTransform.anchoredPosition = anchoredPosition;
        backgroundRectTransform.sizeDelta = size;

        Image backgroundImage = backgroundGameObject.GetComponent<Image>();
        backgroundImage.color = new Color(0.18f, 0.03f, 0.03f, 0.8f);

        GameObject fillGameObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        RectTransform fillRectTransform = fillGameObject.GetComponent<RectTransform>();
        fillRectTransform.SetParent(backgroundRectTransform, false);
        fillRectTransform.anchorMin = Vector2.zero;
        fillRectTransform.anchorMax = Vector2.one;
        fillRectTransform.offsetMin = Vector2.zero;
        fillRectTransform.offsetMax = Vector2.zero;

        healthMeterHolder = fillGameObject.GetComponent<Image>();
        healthMeterFillRectTransform = fillRectTransform;
        healthMeterHolder.color = new Color(0.82f, 0.08f, 0.08f, 0.95f);
        healthMeterHolder.type = Image.Type.Simple;
        healthMeterHolder.fillAmount = 1f;
    }
}
