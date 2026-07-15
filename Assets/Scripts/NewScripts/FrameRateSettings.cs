using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FrameRateSettings : MonoBehaviour
{
    private const int LimitedFrameRate = 60;

    public static FrameRateSettings Instance { get; private set; }

    public event EventHandler<bool> OnFrameRateLimitChanged;

    public bool IsFrameRateLimited { get; private set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeInstance()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject settingsObject = new GameObject(nameof(FrameRateSettings));
        settingsObject.AddComponent<FrameRateSettings>();
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
        ApplyFrameRateLimit();
        SceneManager.sceneLoaded += SceneManager_OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneManager_OnSceneLoaded;
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetFrameRateLimited(bool isLimited)
    {
        if (IsFrameRateLimited == isLimited)
        {
            ApplyFrameRateLimit();
            return;
        }

        IsFrameRateLimited = isLimited;
        ApplyFrameRateLimit();
        OnFrameRateLimitChanged?.Invoke(this, IsFrameRateLimited);
    }

    private void SceneManager_OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        ApplyFrameRateLimit();
    }

    private void ApplyFrameRateLimit()
    {
        Application.targetFrameRate = IsFrameRateLimited ? LimitedFrameRate : -1;
    }
}
