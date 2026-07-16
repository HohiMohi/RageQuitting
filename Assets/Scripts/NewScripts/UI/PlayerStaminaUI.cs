using StarterAssets;
using UnityEngine;
using UnityEngine.UI;

public class PlayerStaminaUI : MonoBehaviour
{
    [SerializeField] private Image staminaMeterHolder;
    [SerializeField] private FirstPersonController firstPersonController;
    [SerializeField] private Color exhaustionWarningColor = new Color(0.9f, 0.08f, 0.08f, 1f);
    [SerializeField] private float exhaustionWarningBlinkSpeed = 8f;
    private Color normalColor;
    private bool isExhaustionWarningActive;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        normalColor = staminaMeterHolder != null ? staminaMeterHolder.color : Color.white;
        if (firstPersonController != null)
        {
            firstPersonController.OnSharedCarryExhaustionWarningChanged += FirstPersonController_OnSharedCarryExhaustionWarningChanged;
            isExhaustionWarningActive = firstPersonController.IsSharedCarryExhaustionWarningActive;
        }
        Show();
    }

    private void OnDestroy()
    {
        if (firstPersonController != null)
        {
            firstPersonController.OnSharedCarryExhaustionWarningChanged -= FirstPersonController_OnSharedCarryExhaustionWarningChanged;
        }
    }

    private void Update()
    {
        UpdateVisual();
    }
    private void Show()
    {
        gameObject.SetActive(true);
    }
    private void Hide()
    {
        gameObject.SetActive(false);
    }

    private void UpdateVisual()
    {
        if (staminaMeterHolder == null || firstPersonController == null)
        {
            return;
        }

        isExhaustionWarningActive = firstPersonController.IsSharedCarryExhaustionWarningActive;
        if (isExhaustionWarningActive)
        {
            float pulse = Mathf.PingPong(Time.time * exhaustionWarningBlinkSpeed, 1f);
            Color warningColor = exhaustionWarningColor;
            warningColor.a = Mathf.Lerp(0.2f, exhaustionWarningColor.a, pulse);
            staminaMeterHolder.fillAmount = 1f;
            staminaMeterHolder.color = warningColor;
        }
        else
        {
            staminaMeterHolder.fillAmount = firstPersonController.GetStaminaNormalized();
            staminaMeterHolder.color = normalColor;
        }
    }

    private void FirstPersonController_OnSharedCarryExhaustionWarningChanged(object sender, FirstPersonController.SharedCarryExhaustionWarningChangedEventArgs e)
    {
        isExhaustionWarningActive = e.IsWarningActive;
    }
}
