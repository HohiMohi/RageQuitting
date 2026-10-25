using UnityEngine;

/// <summary>Configurable dimensions and tolerances for the opt-in bridge deck area.</summary>
[CreateAssetMenu(fileName = "BridgeDeckAreaProfile", menuName = "RageQuitting/Bridge Deck Area Profile")]
public sealed class BridgeDeckAreaProfile : ScriptableObject
{
    [Min(0.01f)] public float areaLengthMeters = 6.56f;
    [Min(0.01f)] public float areaWidthMeters = 2.40f;
    [Min(0.01f)] public float stockLengthMeters = 2.50f;
    [Min(0.01f)] public float stockWidthMeters = 2.40f;
    [Min(0.01f)] public float stockThicknessMeters = 0.20f;
    [Min(0.01f)] public float minimumFragmentLengthMeters = 0.40f;
    [Min(0f)] public float maximumJointWidthMeters = 0.02f;
    [Min(0f)] public float boundaryMarginMeters = 0.01f;
    [Min(0f)] public float orientationToleranceDegrees = 0.5f;
    [Min(0f)] public float undersideHeightToleranceMeters = 0.03f;
    [Min(0f)] public float geometryEpsilonMeters = 0.0005f;
    [Min(0f)] public float sawKerfMeters = 0.005f;
    [Min(0f)] public float fullStockMassKilograms = 1f;
    [Header("Presentation")]
    public Material presentationLineMaterial;

    public BridgeDeckAreaRules ToRules()
    {
        return new BridgeDeckAreaRules(areaLengthMeters, areaWidthMeters, stockLengthMeters,
            stockWidthMeters, stockThicknessMeters, minimumFragmentLengthMeters,
            maximumJointWidthMeters, boundaryMarginMeters, orientationToleranceDegrees,
            undersideHeightToleranceMeters, geometryEpsilonMeters, sawKerfMeters,
            BridgeDeckAreaRules.Defaults.ActiveCutSeconds, fullStockMassKilograms);
    }
}
