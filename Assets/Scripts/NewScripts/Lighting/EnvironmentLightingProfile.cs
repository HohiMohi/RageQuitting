using UnityEngine;

[CreateAssetMenu(fileName = "EnvironmentLightingProfile", menuName = "Rage Quitting/Lighting/Environment Lighting Profile")]
public sealed class EnvironmentLightingProfile : ScriptableObject
{
    // Batch 1 intentionally keeps the untuned parts of the day on the safe Afternoon look.
    // Future art passes can shape these curves without changing controller or time-source code.
    public const float AfternoonNormalizedTime = 0.6875f;
    public static readonly Vector3 AfternoonSunEulerAngles = new Vector3(28f, 215f, 0f);
    public static readonly Color AfternoonSunColor = new Color(1f, 0.78f, 0.55f, 1f);
    public const float AfternoonSunIntensity = 1.15f;
    public const float AfternoonSunShadowStrength = 0.82f;
    public static readonly Color AfternoonSkyTint = new Color(0.55f, 0.72f, 1f, 1f);
    public static readonly Color AfternoonGroundColor = new Color(0.42f, 0.30f, 0.24f, 1f);
    public const float AfternoonSkyExposure = 1.1f;
    public const float AfternoonAtmosphereThickness = 0.85f;
    public const float AfternoonAmbientIntensity = 1.05f;
    public static readonly Color AfternoonFogColor = new Color(0.72f, 0.78f, 0.72f, 1f);
    public const float AfternoonFogDensity = 0.0035f;
    public const float AfternoonPostExposure = 0.2f;
    public const float AfternoonPostContrast = 8f;
    public const float AfternoonPostSaturation = 12f;
    public static readonly Color AfternoonPostColorFilter = new Color(1f, 0.94f, 0.86f, 1f);
    public const float AfternoonWhiteBalanceTemperature = 12f;
    public const float AfternoonBloomIntensity = 0.22f;
    public const float AfternoonLocalLightMultiplier = 0.9f;

    [Header("Sun")]
    [SerializeField] private AnimationCurve sunPitch = ConstantCurve(AfternoonSunEulerAngles.x);
    [SerializeField] private AnimationCurve sunYaw = ConstantCurve(AfternoonSunEulerAngles.y);
    [SerializeField] private AnimationCurve sunRoll = ConstantCurve(AfternoonSunEulerAngles.z);
    [SerializeField] private Gradient sunColor = ConstantGradient(AfternoonSunColor);
    [SerializeField] private AnimationCurve sunIntensity = ConstantCurve(AfternoonSunIntensity);
    [SerializeField] private AnimationCurve sunShadowStrength = ConstantCurve(AfternoonSunShadowStrength);

    [Header("Procedural Sky and Ambient")]
    [SerializeField] private Gradient skyTint = ConstantGradient(AfternoonSkyTint);
    [SerializeField] private Gradient groundColor = ConstantGradient(AfternoonGroundColor);
    [SerializeField] private AnimationCurve skyExposure = ConstantCurve(AfternoonSkyExposure);
    [SerializeField] private AnimationCurve atmosphereThickness = ConstantCurve(AfternoonAtmosphereThickness);
    [SerializeField] private AnimationCurve ambientIntensity = ConstantCurve(AfternoonAmbientIntensity);

    [Header("Fog")]
    [SerializeField] private bool fogEnabled = true;
    [SerializeField] private Gradient fogColor = ConstantGradient(AfternoonFogColor);
    [SerializeField] private AnimationCurve fogDensity = ConstantCurve(AfternoonFogDensity);

    [Header("Post Processing")]
    [SerializeField] private AnimationCurve postExposure = ConstantCurve(AfternoonPostExposure);
    [SerializeField] private AnimationCurve postContrast = ConstantCurve(AfternoonPostContrast);
    [SerializeField] private AnimationCurve postSaturation = ConstantCurve(AfternoonPostSaturation);
    [SerializeField] private Gradient postColorFilter = ConstantGradient(AfternoonPostColorFilter);
    [SerializeField] private AnimationCurve whiteBalanceTemperature = ConstantCurve(AfternoonWhiteBalanceTemperature);
    [SerializeField] private AnimationCurve bloomIntensity = ConstantCurve(AfternoonBloomIntensity);

    [Header("Local Accents")]
    [SerializeField] private AnimationCurve localLightMultiplier = ConstantCurve(AfternoonLocalLightMultiplier);

    public EnvironmentLightingState Evaluate(float normalizedTime)
    {
        float time = WrapNormalizedTime(normalizedTime);
        return new EnvironmentLightingState
        {
            normalizedTime = time,
            sunEulerAngles = new Vector3(
                EvaluateCurve(sunPitch, time, AfternoonSunEulerAngles.x),
                EvaluateCurve(sunYaw, time, AfternoonSunEulerAngles.y),
                EvaluateCurve(sunRoll, time, AfternoonSunEulerAngles.z)),
            sunColor = EvaluateGradient(sunColor, time, AfternoonSunColor),
            sunIntensity = Mathf.Max(0f, EvaluateCurve(sunIntensity, time, AfternoonSunIntensity)),
            sunShadowStrength = Mathf.Clamp01(EvaluateCurve(sunShadowStrength, time, AfternoonSunShadowStrength)),
            skyTint = EvaluateGradient(skyTint, time, AfternoonSkyTint),
            groundColor = EvaluateGradient(groundColor, time, AfternoonGroundColor),
            skyExposure = Mathf.Max(0f, EvaluateCurve(skyExposure, time, AfternoonSkyExposure)),
            atmosphereThickness = Mathf.Clamp(EvaluateCurve(atmosphereThickness, time, AfternoonAtmosphereThickness), 0f, 5f),
            ambientIntensity = Mathf.Max(0f, EvaluateCurve(ambientIntensity, time, AfternoonAmbientIntensity)),
            fogEnabled = fogEnabled,
            fogColor = EvaluateGradient(fogColor, time, AfternoonFogColor),
            fogDensity = Mathf.Max(0f, EvaluateCurve(fogDensity, time, AfternoonFogDensity)),
            postExposure = EvaluateCurve(postExposure, time, AfternoonPostExposure),
            postContrast = Mathf.Clamp(EvaluateCurve(postContrast, time, AfternoonPostContrast), -100f, 100f),
            postSaturation = Mathf.Clamp(EvaluateCurve(postSaturation, time, AfternoonPostSaturation), -100f, 100f),
            postColorFilter = EvaluateGradient(postColorFilter, time, AfternoonPostColorFilter),
            whiteBalanceTemperature = Mathf.Clamp(EvaluateCurve(whiteBalanceTemperature, time, AfternoonWhiteBalanceTemperature), -100f, 100f),
            bloomIntensity = Mathf.Max(0f, EvaluateCurve(bloomIntensity, time, AfternoonBloomIntensity)),
            localLightMultiplier = Mathf.Max(0f, EvaluateCurve(localLightMultiplier, time, AfternoonLocalLightMultiplier))
        };
    }

    private void OnValidate()
    {
        EnsureDefaults();
    }

    private void EnsureDefaults()
    {
        sunPitch ??= ConstantCurve(AfternoonSunEulerAngles.x);
        sunYaw ??= ConstantCurve(AfternoonSunEulerAngles.y);
        sunRoll ??= ConstantCurve(AfternoonSunEulerAngles.z);
        sunColor ??= ConstantGradient(AfternoonSunColor);
        sunIntensity ??= ConstantCurve(AfternoonSunIntensity);
        sunShadowStrength ??= ConstantCurve(AfternoonSunShadowStrength);
        skyTint ??= ConstantGradient(AfternoonSkyTint);
        groundColor ??= ConstantGradient(AfternoonGroundColor);
        skyExposure ??= ConstantCurve(AfternoonSkyExposure);
        atmosphereThickness ??= ConstantCurve(AfternoonAtmosphereThickness);
        ambientIntensity ??= ConstantCurve(AfternoonAmbientIntensity);
        fogColor ??= ConstantGradient(AfternoonFogColor);
        fogDensity ??= ConstantCurve(AfternoonFogDensity);
        postExposure ??= ConstantCurve(AfternoonPostExposure);
        postContrast ??= ConstantCurve(AfternoonPostContrast);
        postSaturation ??= ConstantCurve(AfternoonPostSaturation);
        postColorFilter ??= ConstantGradient(AfternoonPostColorFilter);
        whiteBalanceTemperature ??= ConstantCurve(AfternoonWhiteBalanceTemperature);
        bloomIntensity ??= ConstantCurve(AfternoonBloomIntensity);
        localLightMultiplier ??= ConstantCurve(AfternoonLocalLightMultiplier);
    }

    private static float EvaluateCurve(AnimationCurve curve, float time, float fallback)
    {
        return curve != null && curve.length > 0 ? curve.Evaluate(time) : fallback;
    }

    private static float WrapNormalizedTime(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? AfternoonNormalizedTime : Mathf.Repeat(value, 1f);
    }

    private static Color EvaluateGradient(Gradient gradient, float time, Color fallback)
    {
        return gradient != null ? gradient.Evaluate(time) : fallback;
    }

    private static AnimationCurve ConstantCurve(float value)
    {
        return new AnimationCurve(new Keyframe(0f, value), new Keyframe(1f, value));
    }

    private static Gradient ConstantGradient(Color color)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a, 1f) });
        return gradient;
    }
}
