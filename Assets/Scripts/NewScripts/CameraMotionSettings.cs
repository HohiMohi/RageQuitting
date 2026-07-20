using System;
using UnityEngine;

public class CameraMotionSettings : MonoBehaviour
{
    public static CameraMotionSettings Instance { get; private set; }

    public event EventHandler<float> OnRotationMotionIntensityChanged;

    public float RotationMotionIntensity { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeInstance()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject settingsObject = new GameObject(nameof(CameraMotionSettings));
        settingsObject.AddComponent<CameraMotionSettings>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetRotationMotionIntensity(float intensity)
    {
        float clampedIntensity = Mathf.Clamp01(intensity);
        if (Mathf.Approximately(RotationMotionIntensity, clampedIntensity))
        {
            return;
        }

        RotationMotionIntensity = clampedIntensity;
        OnRotationMotionIntensityChanged?.Invoke(this, RotationMotionIntensity);
    }
}
