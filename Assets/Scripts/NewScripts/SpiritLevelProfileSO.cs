using UnityEngine;

[CreateAssetMenu(fileName = "SpiritLevelProfile", menuName = "Scriptable Objects/Tools/Spirit Level Profile")]
public sealed class SpiritLevelProfileSO : ScriptableObject
{
    [Header("Measurement")]
    [Min(0.5f)] public float measurementRange = 2.5f;
    [Min(0.01f)] public float measurementTransitionDuration = 0.2f;

    [Header("Vial")]
    [Min(0.01f)] public float bubbleSmoothTime = 0.12f;
    [Min(0.1f)] public float physicalSensitivity = 2.5f;

    [Header("Vial marks")]
    [Min(0f)] public float bubbleToGreenMarkClearance = 0.001f;
    [Min(0f)] public float bubbleEndMargin = 0.002f;
    [Min(0.002f)] public float markThickness = 0.012f;
    [Min(0.01f)] public float markLength = 0.11f;
    public Color greenMarkColor = new Color(0.18f, 0.95f, 0.25f, 1f);
    public Color yellowMarkColor = new Color(1f, 0.72f, 0.05f, 1f);

    [Header("First person pose")]
    public Vector3 idleLocalPosition = new Vector3(0f, -0.12f, 0.34f);
    public Vector3 idleLocalEuler = Vector3.zero;
    public Vector3 measurementLocalPosition = new Vector3(0f, -0.02f, 0.27f);
    public Vector3 measurementLocalEuler = Vector3.zero;

    [Header("Measurement point markers")]
    public Color markerColor = new Color(0.2f, 0.78f, 1f, 0.55f);
    public Color markerTargetedColor = new Color(0.38f, 0.95f, 1f, 0.9f);
    public Color markerMeasuringColor = new Color(0.25f, 1f, 0.35f, 0.95f);
    [Min(0.002f)] public float markerLineWidth = 0.018f;
    [Range(0f, 0.25f)] public float markerPulseAmount = 0.06f;
    [Min(0f)] public float markerPulseSpeed = 2.5f;
}
