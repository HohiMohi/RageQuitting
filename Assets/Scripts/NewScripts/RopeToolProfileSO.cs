using UnityEngine;

[CreateAssetMenu(fileName = "RopeToolProfile", menuName = "Scriptable Objects/Rope Tool Profile")]
public sealed class RopeToolProfileSO : ScriptableObject
{
    [Header("Throw")]
    [Min(1f)] public float maximumLength = 15f;
    [Min(0.25f)] public float minimumLength = 0.8f;
    [Min(0.25f)] public float minimumThrowLength = 4f;
    [Min(0.05f)] public float fullChargeDuration = 3f;
    [Min(0f)] public float minimumThrowSpeed = 8f;
    [Min(0f)] public float maximumThrowSpeed = 20f;
    [Min(0f)] public float throwUpwardBias = 0.08f;
    public GameObject ropeEndProjectilePrefab;

    [Header("Length Control")]
    [Min(0f)] public float emptyEndpointReelSpeed = 8f;
    [Min(0f)] public float attachedTargetReelSpeed = 4f;
    [Min(0f)] public float payOutSpeed = 4f;
    [Min(0f)] public float tautDeadZone = 0.1f;
    [Min(0.05f)] public float endpointReturnDistance = 0.35f;

    [Header("Loose Endpoint Physics")]
    [Min(0f)] public float emptyEndpointSpring = 42f;
    [Min(0f)] public float emptyEndpointDamping = 10f;
    [Min(0f)] public float maximumEmptyEndpointAcceleration = 24f;
    [Min(0f)] public float maximumEmptyEndpointSpeed = 8f;
    [Min(0f)] public float landedLinearDamping = 2.5f;
    [Min(0f)] public float landedAngularDamping = 4f;
    [Min(0.01f)] public float endpointGroundProbeDistance = 0.18f;

    [Header("Constraint")]
    [Min(0f)] public float resourceSpring = 35f;
    [Min(0f)] public float resourceDamping = 8f;
    [Min(0f)] public float maximumResourceAcceleration = 10f;
    [Min(0f)] public float playerPullSpeed = 6f;
    [Range(0f, 1f)] public float playerTargetPullShare = 1f;
    [Range(0f, 1f)] public float playerHolderReactionShare = 0f;
    [Min(0f)] public float maximumStretch = 1.25f;
    public bool breakOnOverload;
    [Min(0.05f)] public float overloadDuration = 0.75f;

    [Header("Suspended Player")]
    [Min(0f)] public float suspendedSwingGravityMultiplier = 1f;
    [Min(0f)] public float suspendedSwingInputAcceleration = 2.5f;
    [Min(0f)] public float suspendedSwingDamping = 0.2f;
    [Min(0f)] public float maximumSuspendedSwingSpeed = 10f;
    [Min(0f)] public float suspendedTautThreshold = 0.05f;
    [Min(0f)] public float suspendedWallContactGraceDuration = 0.12f;
    [Min(0f)] public float suspendedPositionDeadZone = 0.03f;
    [Min(0f)] public float suspendedPositionCorrectionSpeed = 3f;
    [Min(0f)] public float suspendedPositionCorrectionAcceleration = 12f;
    [Min(0f)] public float maximumSuspendedAnchorTransferSpeed = 10f;
    [Min(0f)] public float suspendedGroundedReleaseDelay = 0.15f;
    [Range(0f, 1f)] public float suspendedUpwardPullThreshold = 0.1f;
    [Min(0f)] public float wallJumpOutwardSpeed = 4f;
    [Min(0f)] public float wallJumpUpwardSpeed = 2.5f;
    [Min(0f)] public float wallJumpCooldown = 0.3f;

    [Header("Obstruction")]
    public LayerMask obstructionMask = Physics.DefaultRaycastLayers;
    [Min(0.01f)] public float obstructionRadius = 0.06f;

    [Header("Stamina")]
    [Min(0f)] public float maximumReelingStaminaDrain = 5f;

    [Header("Escape")]
    public bool allowTargetEscape = true;
    [Min(0.1f)] public float targetEscapeHoldDuration = 1.5f;

    [Header("Visual")]
    [Min(2)] public int lineSegments = 18;
    [Min(0.005f)] public float lineWidth = 0.035f;
    [Min(0f)] public float maximumVisualSag = 0.8f;
    public Color relaxedColor = new Color(0.55f, 0.36f, 0.18f, 1f);
    public Color tautColor = new Color(0.9f, 0.72f, 0.3f, 1f);
    public Color blockedColor = new Color(0.9f, 0.25f, 0.2f, 1f);

    [Header("Throw Preview")]
    [Min(4)] public int trajectoryPreviewSegments = 40;
    [Min(0.005f)] public float trajectoryPreviewTimeStep = 0.04f;
    [Min(0.005f)] public float trajectoryPreviewLineWidth = 0.025f;
    [Min(0.02f)] public float trajectoryPreviewMarkerRadius = 0.12f;
    public Color trajectoryPreviewColor = new Color(0.95f, 0.78f, 0.24f, 0.9f);
    public Color trajectoryPreviewHitColor = new Color(0.45f, 1f, 0.35f, 0.95f);
}
