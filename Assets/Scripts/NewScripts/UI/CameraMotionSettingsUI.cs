using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CameraMotionSettingsUI : MonoBehaviour
{
    [SerializeField] private Slider cameraMotionSlider;
    [SerializeField] private TMP_Text valueText;

    private bool isRefreshing;

    private void Awake()
    {
        cameraMotionSlider ??= GetComponent<Slider>();
    }

    private void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        if (cameraMotionSlider != null)
        {
            cameraMotionSlider.onValueChanged.RemoveListener(CameraMotionSlider_OnValueChanged);
        }

        if (CameraMotionSettings.Instance != null)
        {
            CameraMotionSettings.Instance.OnRotationMotionIntensityChanged -= CameraMotionSettings_OnRotationMotionIntensityChanged;
        }
    }

    private void Subscribe()
    {
        if (cameraMotionSlider == null)
        {
            return;
        }

        cameraMotionSlider.minValue = 0f;
        cameraMotionSlider.maxValue = 1f;
        cameraMotionSlider.wholeNumbers = false;
        cameraMotionSlider.onValueChanged.RemoveListener(CameraMotionSlider_OnValueChanged);
        cameraMotionSlider.onValueChanged.AddListener(CameraMotionSlider_OnValueChanged);

        if (CameraMotionSettings.Instance != null)
        {
            CameraMotionSettings.Instance.OnRotationMotionIntensityChanged -= CameraMotionSettings_OnRotationMotionIntensityChanged;
            CameraMotionSettings.Instance.OnRotationMotionIntensityChanged += CameraMotionSettings_OnRotationMotionIntensityChanged;
        }
    }

    private void Refresh()
    {
        float intensity = CameraMotionSettings.Instance != null
            ? CameraMotionSettings.Instance.RotationMotionIntensity
            : 1f;

        isRefreshing = true;
        cameraMotionSlider?.SetValueWithoutNotify(intensity);
        isRefreshing = false;
        RefreshValueText(intensity);
    }

    private void CameraMotionSlider_OnValueChanged(float intensity)
    {
        if (!isRefreshing)
        {
            CameraMotionSettings.Instance?.SetRotationMotionIntensity(intensity);
        }

        RefreshValueText(intensity);
    }

    private void CameraMotionSettings_OnRotationMotionIntensityChanged(object sender, float intensity)
    {
        isRefreshing = true;
        cameraMotionSlider?.SetValueWithoutNotify(intensity);
        isRefreshing = false;
        RefreshValueText(intensity);
    }

    private void RefreshValueText(float intensity)
    {
        if (valueText != null)
        {
            valueText.text = $"{Mathf.RoundToInt(Mathf.Clamp01(intensity) * 100f)}%";
        }
    }
}
