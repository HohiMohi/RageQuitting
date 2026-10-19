using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "MountingNailProfile", menuName = "Rage Quitting/Mounting Nail Profile")]
public sealed class MountingNailProfileSO : ScriptableObject
{
    [Header("Physical properties")]
    [SerializeField, Min(0.01f)] private float lengthMeters = 0.35f;
    [SerializeField, Min(0.01f)] private float visualWidthMultiplier = 8f;
    [SerializeField, Min(0.001f)] private float massKg = 0.1f;
    [SerializeField, Min(0.01f)] private float preparedTimeoutSeconds = 3f;

    [Header("Preparation contact")]
    [SerializeField, Min(0f)] private float tipContactToleranceMeters = 0.02f;
    [SerializeField, Min(0f)] private float penetrationRangeMeters = 0.30f;
    [SerializeField, Min(0f)] private float aimRadiusMeters = 0.06f;
    [SerializeField, Min(1), FormerlySerializedAs("requiredHitsMin")] private int requiredHitsForSingleObject = 1;
    [SerializeField, Min(1), FormerlySerializedAs("requiredHitsMax")] private int requiredHitsForTwoObjects = 3;

    [Header("Prepared stability")]
    [SerializeField, Min(0f)] private float allowedTargetDisplacementMeters = 0.01f;
    [SerializeField, Min(0f)] private float allowedTargetRotationDegrees = 2f;
    [SerializeField, Min(0f)] private float finalDepthMeters = 0.30f;

    [Header("Loose physics")]
    [SerializeField, Min(0f)] private float exitLateralImpulseMetersPerSecond = 0.15f;
    [SerializeField, Min(0f)] private float looseHammerHorizontalImpulseMetersPerSecond = 1f;
    [SerializeField, Min(0f)] private float looseHammerVerticalImpulseMetersPerSecond = 0.25f;

    public float LengthMeters => lengthMeters;
    public float VisualWidthMultiplier => visualWidthMultiplier;
    public float MassKg => massKg;
    public float PreparedTimeoutSeconds => preparedTimeoutSeconds;
    public float TipContactToleranceMeters => tipContactToleranceMeters;
    public float PenetrationRangeMeters => penetrationRangeMeters;
    public float AimRadiusMeters => aimRadiusMeters;
    public int RequiredHitsForSingleObject => requiredHitsForSingleObject;
    public int RequiredHitsForTwoObjects => requiredHitsForTwoObjects;
    public float AllowedTargetDisplacementMeters => allowedTargetDisplacementMeters;
    public float AllowedTargetRotationDegrees => allowedTargetRotationDegrees;
    public float FinalDepthMeters => finalDepthMeters;
    public float ExitLateralImpulseMetersPerSecond => exitLateralImpulseMetersPerSecond;
    public float LooseHammerHorizontalImpulseMetersPerSecond => looseHammerHorizontalImpulseMetersPerSecond;
    public float LooseHammerVerticalImpulseMetersPerSecond => looseHammerVerticalImpulseMetersPerSecond;

    private void OnValidate()
    {
        lengthMeters = Mathf.Max(0.01f, lengthMeters);
        visualWidthMultiplier = Mathf.Max(0.01f, visualWidthMultiplier);
        massKg = Mathf.Max(0.001f, massKg);
        preparedTimeoutSeconds = Mathf.Max(0.01f, preparedTimeoutSeconds);
        requiredHitsForSingleObject = Mathf.Max(1, requiredHitsForSingleObject);
        requiredHitsForTwoObjects = Mathf.Max(1, requiredHitsForTwoObjects);
    }
}


