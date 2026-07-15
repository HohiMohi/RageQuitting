using UnityEngine;
using UnityEngine.UI;

public class FrameRateSettingsUI : MonoBehaviour
{
    [SerializeField] private Toggle frameRateToggle;

    private bool isRefreshing;

    private void Awake()
    {
        if (frameRateToggle == null)
        {
            frameRateToggle = GetComponent<Toggle>();
        }
    }

    private void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        if (frameRateToggle != null)
        {
            frameRateToggle.onValueChanged.RemoveListener(FrameRateToggle_OnValueChanged);
        }

        if (FrameRateSettings.Instance != null)
        {
            FrameRateSettings.Instance.OnFrameRateLimitChanged -= FrameRateSettings_OnFrameRateLimitChanged;
        }
    }

    private void Subscribe()
    {
        if (frameRateToggle == null)
        {
            return;
        }

        frameRateToggle.onValueChanged.RemoveListener(FrameRateToggle_OnValueChanged);
        frameRateToggle.onValueChanged.AddListener(FrameRateToggle_OnValueChanged);

        if (FrameRateSettings.Instance == null)
        {
            return;
        }

        FrameRateSettings.Instance.OnFrameRateLimitChanged -= FrameRateSettings_OnFrameRateLimitChanged;
        FrameRateSettings.Instance.OnFrameRateLimitChanged += FrameRateSettings_OnFrameRateLimitChanged;
    }

    private void Refresh()
    {
        if (frameRateToggle == null || FrameRateSettings.Instance == null)
        {
            return;
        }

        isRefreshing = true;
        frameRateToggle.SetIsOnWithoutNotify(FrameRateSettings.Instance.IsFrameRateLimited);
        isRefreshing = false;
    }

    private void FrameRateToggle_OnValueChanged(bool isLimited)
    {
        if (!isRefreshing)
        {
            FrameRateSettings.Instance?.SetFrameRateLimited(isLimited);
        }
    }

    private void FrameRateSettings_OnFrameRateLimitChanged(object sender, bool isLimited)
    {
        if (frameRateToggle == null)
        {
            return;
        }

        isRefreshing = true;
        frameRateToggle.SetIsOnWithoutNotify(isLimited);
        isRefreshing = false;
    }
}
