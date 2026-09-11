using System;
using UnityEngine;

[Serializable]
public struct EnvironmentLightingState
{
    public float normalizedTime;
    public Vector3 sunEulerAngles;
    public Color sunColor;
    public float sunIntensity;
    public float sunShadowStrength;
    public Color skyTint;
    public Color groundColor;
    public float skyExposure;
    public float atmosphereThickness;
    public float ambientIntensity;
    public bool fogEnabled;
    public Color fogColor;
    public float fogDensity;
    public float postExposure;
    public float postContrast;
    public float postSaturation;
    public Color postColorFilter;
    public float whiteBalanceTemperature;
    public float bloomIntensity;
    public float localLightMultiplier;
}
