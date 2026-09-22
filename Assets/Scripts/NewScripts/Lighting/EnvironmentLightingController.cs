using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class EnvironmentLightingController : MonoBehaviour
{
    private const float DefaultNormalizedTime = EnvironmentLightingProfile.AfternoonNormalizedTime;
    private const float TimeComparisonTolerance = 0.00001f;
    private const float MinimumGiRefreshInterval = 0.25f;

    private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
    private static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");
    private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
    private static readonly int AtmosphereThicknessId = Shader.PropertyToID("_AtmosphereThickness");

    [Header("Required")]
    [SerializeField] private EnvironmentLightingProfile profile;
    [SerializeField] private Light mainDirectionalLight;

    [Header("Optional Rendering Targets")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private VolumeProfile volumeProfileTemplate;
    [SerializeField] private Material proceduralSkyboxTemplate;
    [SerializeField] private Light[] localAccentLights;

    [Header("Time")]
    [Tooltip("Optional component implementing IEnvironmentTimeSource.")]
    [SerializeField] private MonoBehaviour timeSourceBehaviour;
    [SerializeField, Range(0f, 1f)] private float manualNormalizedTime = DefaultNormalizedTime;

    private IEnvironmentTimeSource timeSource;
    private float normalizedTime = DefaultNormalizedTime;
    private bool initialized;
    private bool originalStateCaptured;
    private bool configurationWarningLogged;
    private bool applicationQuitting;
    private bool giRefreshPending;
    private float lastGiRefreshTime = float.NegativeInfinity;

    private Material runtimeSkybox;
    private VolumeProfile runtimeVolumeProfile;
    private VolumeProfile previousInternalVolumeProfile;
    private VolumeProfile previousSharedVolumeProfile;
    private ColorAdjustments colorAdjustments;
    private WhiteBalance whiteBalance;
    private Bloom bloom;

    private Material previousSkybox;
    private Light previousSun;
    private AmbientMode previousAmbientMode;
    private float previousAmbientIntensity;
    private bool previousFogEnabled;
    private Color previousFogColor;
    private FogMode previousFogMode;
    private float previousFogDensity;
    private float previousFogStartDistance;
    private float previousFogEndDistance;
    private Quaternion previousSunRotation;
    private Color previousSunColor;
    private float previousSunIntensity;
    private float previousSunShadowStrength;
    private float[] previousLocalLightIntensities;

    public float NormalizedTime => normalizedTime;

    protected virtual void OnEnable()
    {
        applicationQuitting = false;
        Initialize();
        ApplyCurrentTime(true);
    }

    protected virtual void Update()
    {
        if (initialized && !originalStateCaptured && profile != null && mainDirectionalLight != null)
        {
            initialized = false;
        }

        if (!initialized)
        {
            Initialize();
        }

        float requestedTime = ResolveRequestedTime();
        if (!Mathf.Approximately(requestedTime, normalizedTime) &&
            Mathf.Abs(requestedTime - normalizedTime) > TimeComparisonTolerance)
        {
            normalizedTime = requestedTime;
            ApplyCurrentTime(false);
        }

        TryRefreshDynamicGi();
    }

    protected virtual void OnDisable()
    {
        Cleanup();
    }

    protected virtual void OnDestroy()
    {
        Cleanup();
    }

    protected virtual void OnApplicationQuit()
    {
        applicationQuitting = true;
    }

    protected virtual void OnValidate()
    {
        manualNormalizedTime = WrapNormalizedTime(manualNormalizedTime);
        if (Application.isPlaying && isActiveAndEnabled)
        {
            SetNormalizedTime(manualNormalizedTime);
        }
    }

    public void SetNormalizedTime(float value)
    {
        manualNormalizedTime = WrapNormalizedTime(value);
        normalizedTime = manualNormalizedTime;
        if (isActiveAndEnabled)
        {
            if (!initialized)
            {
                Initialize();
            }

            ApplyCurrentTime(false);
        }
    }

    public EnvironmentLightingState EvaluateState(float value)
    {
        return profile != null ? profile.Evaluate(value) : default;
    }

    protected virtual EnvironmentLightingState ModifyEvaluatedState(EnvironmentLightingState state)
    {
        return state;
    }

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }

        timeSource = timeSourceBehaviour as IEnvironmentTimeSource;
        normalizedTime = ResolveRequestedTime();
        LogConfigurationWarningOnce();
        if (profile == null || mainDirectionalLight == null)
        {
            initialized = true;
            return;
        }

        CaptureOriginalState();
        originalStateCaptured = true;
        CreateRuntimeRenderingInstances();
        initialized = true;
    }

    private float ResolveRequestedTime()
    {
        float value = timeSource != null ? timeSource.NormalizedTime : manualNormalizedTime;
        return WrapNormalizedTime(value);
    }

    private void LogConfigurationWarningOnce()
    {
        if (configurationWarningLogged)
        {
            return;
        }

        string problem = null;
        if (profile == null || mainDirectionalLight == null)
        {
            problem = "Assign both an EnvironmentLightingProfile and the scene's main Directional Light.";
        }
        else if (timeSourceBehaviour != null && timeSource == null)
        {
            problem = $"The assigned time source '{timeSourceBehaviour.GetType().Name}' must implement IEnvironmentTimeSource; manual time will be used.";
        }

        if (problem != null)
        {
            Debug.LogWarning($"[{nameof(EnvironmentLightingController)}] {problem}", this);
            configurationWarningLogged = true;
        }
    }

    private void CaptureOriginalState()
    {
        previousSkybox = RenderSettings.skybox;
        previousSun = RenderSettings.sun;
        previousAmbientMode = RenderSettings.ambientMode;
        previousAmbientIntensity = RenderSettings.ambientIntensity;
        previousFogEnabled = RenderSettings.fog;
        previousFogColor = RenderSettings.fogColor;
        previousFogMode = RenderSettings.fogMode;
        previousFogDensity = RenderSettings.fogDensity;
        previousFogStartDistance = RenderSettings.fogStartDistance;
        previousFogEndDistance = RenderSettings.fogEndDistance;

        if (mainDirectionalLight != null)
        {
            previousSunRotation = mainDirectionalLight.transform.rotation;
            previousSunColor = mainDirectionalLight.color;
            previousSunIntensity = mainDirectionalLight.intensity;
            previousSunShadowStrength = mainDirectionalLight.shadowStrength;
        }

        if (localAccentLights != null)
        {
            previousLocalLightIntensities = new float[localAccentLights.Length];
            for (int i = 0; i < localAccentLights.Length; i++)
            {
                previousLocalLightIntensities[i] = localAccentLights[i] != null ? localAccentLights[i].intensity : 0f;
            }
        }

        if (globalVolume != null)
        {
            previousSharedVolumeProfile = globalVolume.sharedProfile;
            previousInternalVolumeProfile = globalVolume.HasInstantiatedProfile() ? globalVolume.profile : null;
        }
    }

    private void CreateRuntimeRenderingInstances()
    {
        if (proceduralSkyboxTemplate != null)
        {
            runtimeSkybox = new Material(proceduralSkyboxTemplate)
            {
                name = $"{proceduralSkyboxTemplate.name} (Runtime)"
            };
            RenderSettings.skybox = runtimeSkybox;
        }

        if (globalVolume == null || volumeProfileTemplate == null)
        {
            return;
        }

        runtimeVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeVolumeProfile.name = $"{volumeProfileTemplate.name} (Runtime)";
        foreach (VolumeComponent sourceComponent in volumeProfileTemplate.components)
        {
            if (sourceComponent != null)
            {
                runtimeVolumeProfile.components.Add(Instantiate(sourceComponent));
            }
        }

        globalVolume.profile = runtimeVolumeProfile;
        colorAdjustments = GetOrAdd<ColorAdjustments>(runtimeVolumeProfile);
        whiteBalance = GetOrAdd<WhiteBalance>(runtimeVolumeProfile);
        bloom = GetOrAdd<Bloom>(runtimeVolumeProfile);
    }

    private void ApplyCurrentTime(bool refreshGiImmediately)
    {
        if (!initialized || !originalStateCaptured || profile == null || mainDirectionalLight == null)
        {
            return;
        }

        EnvironmentLightingState state = ModifyEvaluatedState(profile.Evaluate(normalizedTime));
        ApplyState(state);
        RequestDynamicGiRefresh(refreshGiImmediately);
    }

    private void ApplyState(EnvironmentLightingState state)
    {
        mainDirectionalLight.transform.rotation = Quaternion.Euler(state.sunEulerAngles);
        mainDirectionalLight.color = state.sunColor;
        mainDirectionalLight.intensity = state.sunIntensity;
        mainDirectionalLight.shadowStrength = state.sunShadowStrength;

        RenderSettings.sun = mainDirectionalLight;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = state.ambientIntensity;
        RenderSettings.fog = state.fogEnabled;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = state.fogColor;
        RenderSettings.fogDensity = state.fogDensity;

        ApplySky(state);
        ApplyPostProcessing(state);
        ApplyLocalLights(state.localLightMultiplier);
    }

    private void ApplySky(EnvironmentLightingState state)
    {
        if (runtimeSkybox == null)
        {
            return;
        }

        SetColorIfPresent(runtimeSkybox, SkyTintId, state.skyTint);
        SetColorIfPresent(runtimeSkybox, GroundColorId, state.groundColor);
        SetFloatIfPresent(runtimeSkybox, ExposureId, state.skyExposure);
        SetFloatIfPresent(runtimeSkybox, AtmosphereThicknessId, state.atmosphereThickness);
    }

    private void ApplyPostProcessing(EnvironmentLightingState state)
    {
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.Override(state.postExposure);
            colorAdjustments.contrast.Override(state.postContrast);
            colorAdjustments.saturation.Override(state.postSaturation);
            colorAdjustments.colorFilter.Override(state.postColorFilter);
        }

        if (whiteBalance != null)
        {
            whiteBalance.temperature.Override(state.whiteBalanceTemperature);
        }

        if (bloom != null)
        {
            bloom.intensity.Override(state.bloomIntensity);
        }
    }

    private void ApplyLocalLights(float multiplier)
    {
        if (localAccentLights == null || previousLocalLightIntensities == null)
        {
            return;
        }

        for (int i = 0; i < localAccentLights.Length; i++)
        {
            if (localAccentLights[i] != null)
            {
                localAccentLights[i].intensity = previousLocalLightIntensities[i] * multiplier;
            }
        }
    }

    private void RequestDynamicGiRefresh(bool immediately)
    {
        if (immediately || Time.realtimeSinceStartup - lastGiRefreshTime >= MinimumGiRefreshInterval)
        {
            DynamicGI.UpdateEnvironment();
            lastGiRefreshTime = Time.realtimeSinceStartup;
            giRefreshPending = false;
            return;
        }

        giRefreshPending = true;
    }

    private void TryRefreshDynamicGi()
    {
        if (!giRefreshPending || Time.realtimeSinceStartup - lastGiRefreshTime < MinimumGiRefreshInterval)
        {
            return;
        }

        DynamicGI.UpdateEnvironment();
        lastGiRefreshTime = Time.realtimeSinceStartup;
        giRefreshPending = false;
    }

    private void Cleanup()
    {
        if (!initialized)
        {
            return;
        }

        if (!originalStateCaptured)
        {
            initialized = false;
            return;
        }

        RenderSettings.skybox = previousSkybox;
        RenderSettings.sun = previousSun;
        RenderSettings.ambientMode = previousAmbientMode;
        RenderSettings.ambientIntensity = previousAmbientIntensity;
        RenderSettings.fog = previousFogEnabled;
        RenderSettings.fogColor = previousFogColor;
        RenderSettings.fogMode = previousFogMode;
        RenderSettings.fogDensity = previousFogDensity;
        RenderSettings.fogStartDistance = previousFogStartDistance;
        RenderSettings.fogEndDistance = previousFogEndDistance;

        if (mainDirectionalLight != null)
        {
            mainDirectionalLight.transform.rotation = previousSunRotation;
            mainDirectionalLight.color = previousSunColor;
            mainDirectionalLight.intensity = previousSunIntensity;
            mainDirectionalLight.shadowStrength = previousSunShadowStrength;
        }

        if (globalVolume != null && globalVolume.profile == runtimeVolumeProfile)
        {
            globalVolume.profile = previousInternalVolumeProfile;
            globalVolume.sharedProfile = previousSharedVolumeProfile;
        }

        if (localAccentLights != null && previousLocalLightIntensities != null)
        {
            for (int i = 0; i < localAccentLights.Length; i++)
            {
                if (localAccentLights[i] != null)
                {
                    localAccentLights[i].intensity = previousLocalLightIntensities[i];
                }
            }
        }

        DestroyRuntimeObject(runtimeSkybox);
        DestroyRuntimeVolumeProfile(runtimeVolumeProfile);
        runtimeSkybox = null;
        runtimeVolumeProfile = null;
        colorAdjustments = null;
        whiteBalance = null;
        bloom = null;
        previousInternalVolumeProfile = null;
        previousSharedVolumeProfile = null;
        previousLocalLightIntensities = null;
        giRefreshPending = false;
        originalStateCaptured = false;
        initialized = false;

        if (!applicationQuitting)
        {
            DynamicGI.UpdateEnvironment();
        }
    }

    private static void DestroyRuntimeVolumeProfile(VolumeProfile volumeProfile)
    {
        if (volumeProfile == null)
        {
            return;
        }

        for (int i = volumeProfile.components.Count - 1; i >= 0; i--)
        {
            DestroyRuntimeObject(volumeProfile.components[i]);
        }

        volumeProfile.components.Clear();
        DestroyRuntimeObject(volumeProfile);
    }

    private static T GetOrAdd<T>(VolumeProfile targetProfile) where T : VolumeComponent
    {
        if (!targetProfile.TryGet(out T component))
        {
            component = targetProfile.Add<T>(true);
        }

        component.active = true;
        return component;
    }

    private static void SetColorIfPresent(Material material, int propertyId, Color value)
    {
        if (material.HasProperty(propertyId))
        {
            material.SetColor(propertyId, value);
        }
    }

    private static void SetFloatIfPresent(Material material, int propertyId, float value)
    {
        if (material.HasProperty(propertyId))
        {
            material.SetFloat(propertyId, value);
        }
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static float WrapNormalizedTime(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? DefaultNormalizedTime : Mathf.Repeat(value, 1f);
    }
}
