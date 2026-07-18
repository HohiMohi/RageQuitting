using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerStaminaUI : MonoBehaviour
{
    [SerializeField] private Image staminaMeterHolder;
    [SerializeField] private Image warningPulseTarget;
    [SerializeField] private TMP_Text staminaValueText;
    [SerializeField] private FirstPersonController firstPersonController;
    [SerializeField] private Color exhaustionWarningColor = new Color(0.9f, 0.08f, 0.08f, 1f);
    [SerializeField] private float exhaustionWarningBlinkSpeed = 8f;

    private Color normalFillColor = Color.white;
    private Color normalPulseTargetColor = Color.white;

    private void Awake()
    {
        EnsureReferences();
        if (staminaMeterHolder != null)
        {
            normalFillColor = staminaMeterHolder.color;
        }
        if (warningPulseTarget != null)
        {
            normalPulseTargetColor = warningPulseTarget.color;
        }
    }

    private void Update()
    {
        EnsureReferences();
        if (firstPersonController == null)
        {
            return;
        }

        float normalized = firstPersonController.GetStaminaNormalized();
        if (staminaMeterHolder != null)
        {
            staminaMeterHolder.fillAmount = normalized;
            SetHorizontalFillScale(staminaMeterHolder, normalized);
        }
        if (staminaValueText != null)
        {
            staminaValueText.text = $"{firstPersonController.CurrentStamina:0} / {firstPersonController.MaxStamina:0}";
        }

        bool showWarning = firstPersonController.IsSharedCarryExhaustionWarningActive;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * exhaustionWarningBlinkSpeed);
        if (staminaMeterHolder != null)
        {
            staminaMeterHolder.color = showWarning
                ? Color.Lerp(normalFillColor, exhaustionWarningColor, pulse)
                : normalFillColor;
        }
        if (warningPulseTarget != null)
        {
            warningPulseTarget.color = showWarning
                ? Color.Lerp(normalPulseTargetColor, exhaustionWarningColor, pulse)
                : normalPulseTargetColor;
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
        if (firstPersonController == null)
        {
            firstPersonController = GetComponentInParent<FirstPersonController>();
        }

        if (firstPersonController == null && transform.root != null)
        {
            firstPersonController = transform.root.GetComponentInChildren<FirstPersonController>(true);
        }
    }
}
