using UnityEngine;

[CreateAssetMenu(fileName = "FlexibleSaplingProfile", menuName = "Scriptable Objects/Flexible Sapling Profile")]
public sealed class FlexibleSaplingProfileSO : ScriptableObject
{
    [Header("Cooperation")]
    [Min(1)] public int requiredPulls = 10;
    [Min(1)] public int requiredPlayers = 2;
    [Min(0.001f)] public float mouseSensitivity = 0.012f;
    [Range(0.1f, 0.9f)] public float initialTargetCenter = 0.35f;
    [Range(0.1f, 0.9f)] public float finalTargetCenter = 0.84f;
    [Range(0.01f, 0.25f)] public float targetZoneHalfWidth = 0.07f;
    [Min(0.1f)] public float targetProgressExponent = 2f;
    [Range(0.2f, 1f)] public float breakingTilt = 0.92f;
    [Min(0.05f)] public float pullEvaluationDelay = 0.45f;
    [Range(0.01f, 1f)] public float minimumGestureTravel = 0.15f;
    [Min(0.1f)] public float secondStageTimeLimit = 5f;
    [Min(0.1f)] public float finalStageTimeLimit = 3.5f;
    [Min(0.05f)] public float inputSendInterval = 0.05f;
    [Min(0.1f)] public float inputTimeout = 3f;
    [Min(1f)] public float interactionDistance = 3f;

    [Header("Release Safety")]
    [Min(0f)] public float releaseClearance = 0.45f;
    [Min(0.1f)] public float releaseSearchRadius = 1.2f;
    [Min(0.2f)] public float releaseGroundProbeDistance = 1.5f;

    [Header("Visuals")]
    [Min(1f)] public float maximumVisualTiltDegrees = 32f;
    [Min(0f)] public float visualFollowSpeed = 12f;
    [Min(0f)] public float recenterSpeed = 1.5f;

    [Header("Failure")]
    [Min(1)] public int stumpShovelHits = 12;
    public ExternalImpulseProfileSO activePlayerImpulse;
    public ExternalImpulseProfileSO partnerImpulse;

    [Header("Reward")]
    public BaseResourceSO uprootedProduct;
}
