using UnityEngine;

public enum SharedCarryControlMode
{
    DirectYaw,
    SpatialOrbit,
    PhysicalPointGrip
}

[CreateAssetMenu(fileName = "CarryPhysicsProfile", menuName = "Scriptable Objects/Carry Physics Profile")]
public class CarryPhysicsProfileSO : ScriptableObject
{
    [Header("Control Mode")]
    public SharedCarryControlMode controlMode = SharedCarryControlMode.DirectYaw;
    [Range(0f, 90f)] public float orbitArcDegrees = 45f;
    [Min(0f)] public float orbitAngularSpeed = 75f;
    [Min(0f)] public float spatialLateralForce = 180f;
    [Min(0f)] public float orbitPredictionCorrectionSpeed = 180f;

    [Header("Physical Point Grip")]
    [Min(0f)] public float pointGripLateralForce = 120f;
    [Min(0f)] public float pointGripVerticalForce = 900f;
    [Min(0f)] public float pointGripSpring = 180f;
    [Min(0f)] public float pointGripDamping = 45f;
    [Min(0f)] public float pointGripMaxForce = 700f;
    public bool projectGripForcesToColliderSurface = false;
    public bool limitPointGripLiftByCarrierCapacity = false;
    [Range(0f, 2f)] public float pointGripLiftCapacityPerCarrier = 0.55f;
    [Header("Long Axis Roll Stabilization")]
    public bool stabilizeLongAxisRoll = false;
    public Vector3 rollLongAxisLocal = Vector3.up;
    public Vector3 rollReferenceUpLocal = Vector3.right;
    [Min(0f)] public float rollStabilizingTorque = 240f;
    [Min(0f)] public float rollDamping = 50f;
    [Min(0f)] public float maximumRollStabilizingTorque = 180f;
    public bool compensatePointGripRoll = false;
    [Range(0f, 1f)] public float pointGripRollCompensation = 1f;
    [Min(0f)] public float maximumPointGripRollCompensationTorque = 500f;
    public bool limitTilt = false;
    [Range(0f, 89f)] public float maximumTiltAngle = 35f;
    [Min(0f)] public float tiltRestoringTorque = 120f;
    [Min(0f)] public float tiltDamping = 18f;

    [Header("Fully Staffed Stabilization")]
    public bool stabilizeWhenFullyStaffed = false;
    [Min(0f)] public float fullyStaffedLoadDistributionRegularization = 0.08f;
    [Min(0f)] public float fullyStaffedLevelingTorque = 240f;
    [Range(0f, 15f)] public float fullyStaffedLevelingDeadZone = 1.5f;
    [Min(0f)] public float fullyStaffedTiltDamping = 40f;
    [Min(0f)] public float fullyStaffedMaximumTorque = 300f;
    [Min(0f)] public float fullyStaffedStabilizationBlendDuration = 0.35f;

    [Header("Physical Point Grip Tether")]
    [Min(0f)] public float softTetherDeadZone = 0.08f;
    [Min(0f)] public float softTetherPullSpeed = 5f;
    [Range(0f, 2f)] public float softTetherVelocityInfluence = 0.35f;
    public bool preventGroundedUpwardTether = false;
    [Min(0.01f)] public float hardTetherDistance = 2.5f;
    [Min(0f)] public float tetherBreakDelay = 0.75f;

    [Min(0.01f)] public float mass = 20f;
    [Min(0f)] public float linearDrag = 1.5f;
    [Min(0f)] public float angularDrag = 4f;
    [Min(0f)] public float gripSpring = 900f;
    [Min(0f)] public float gripDamper = 90f;
    [Min(0f)] public float maxGripForce = 1800f;
    [Min(0f)] public float maxGripTorque = 250f;
    [Min(0.01f)] public float maxGripDistance = 1.25f;
    [Min(0f)] public float maxVelocity = 6f;
    [Min(0f)] public float maxAngularVelocity = 3f;
    public bool useGravity = true;
    public bool allowYawRotation = true;
    [Min(0f)] public float movementForce = 450f;
    [Min(0f)] public float movementDamper = 65f;
    [Min(0f)] public float sharedCarryYawTorque = 60f;
    [Header("Stabilized Shared Carry")]
    [Min(0f)] public float horizontalConstraintSpring = 140f;
    [Range(0.1f, 2f)] public float horizontalConstraintDampingRatio = 1.05f;
    [Min(0f)] public float maxHorizontalConstraintForce = 650f;
    [Min(0f)] public float horizontalConstraintDeadZone = 0.03f;
    [Min(0.01f)] public float horizontalConstraintForceResponse = 18f;
    [Min(0f)] public float maxHolderAnchorVelocity = 8f;
    [Min(0f)] public float verticalSupportSpring = 220f;
    [Range(0.1f, 2f)] public float verticalSupportDampingRatio = 1.1f;
    [Min(0f)] public float maxVerticalSupportForce = 1600f;
}
