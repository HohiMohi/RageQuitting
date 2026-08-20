using UnityEngine;

[CreateAssetMenu(fileName = "WheelbarrowProfile", menuName = "Scriptable Objects/Wheelbarrow Profile")]
public class WheelbarrowProfileSO : ScriptableObject
{
    [Header("Mass and cargo")]
    [SerializeField, Min(1f)] private float baseMass = 22f;
    [SerializeField, Min(0f)] private float maximumResourceCargoMass = 60f;
    [SerializeField, Min(0f)] private float concreteBatchMass = 80f;
    [SerializeField, Min(0f)] private float passengerMass = 75f;
    [SerializeField, Range(1, 8)] private int resourceSlots = 3;

    [Header("Drive")]
    [SerializeField, Min(0f)] private float driveForce = 220f;
    [SerializeField, Min(0f)] private float brakeForce = 420f;
    [SerializeField, Min(0f)] private float idleBrakeForce = 35f;
    [SerializeField, Min(0f)] private float idleAngularDamping = 4f;
    [SerializeField, Min(0f)] private float idleDampingMaximumSpeed = 0.25f;
    [SerializeField, Min(0f)] private float maximumForwardSpeed = 4f;
    [SerializeField, Min(0f)] private float maximumReverseSpeed = 2f;
    [SerializeField, Range(0f, 60f)] private float maximumSteeringAngle = 30f;
    [SerializeField, Min(0f)] private float minimumSteeringSpeed = 0.15f;
    [SerializeField, Min(0f)] private float steeringTorque = 8f;
    [SerializeField, Min(0f)] private float driverStabilizingTorque = 18f;
    [SerializeField, Min(0f)] private float driverStabilizingDamping = 4f;
    [SerializeField, Min(0f)] private float maximumDrivenYawSpeedDegrees = 75f;
    [SerializeField, Min(0f)] private float drivenYawResponse = 8f;
    [SerializeField, Min(0f)] private float maximumDrivenYawAccelerationDegrees = 360f;
    [SerializeField, Min(0f)] private float emptyLateralVelocityDamping = 8f;
    [SerializeField, Min(0f)] private float loadedLateralVelocityDamping = 4f;
    [SerializeField, Min(0f)] private float emptyMaximumLateralGripAcceleration = 14f;
    [SerializeField, Min(0f)] private float loadedMaximumLateralGripAcceleration = 8f;
    [SerializeField, Min(0.05f)] private float inputTimeout = 0.25f;

    [Header("Wheel")]
    [SerializeField, Min(0.05f)] private float wheelRadius = 0.44f;
    [SerializeField, Min(0.001f)] private float wheelSuspensionDistance = 0.03f;
    [SerializeField, Min(0.1f)] private float wheelSuspensionFrequency = 7f;
    [SerializeField, Min(0f)] private float wheelSuspensionDampingRatio = 1f;
    [SerializeField, Range(0f, 1f)] private float wheelSuspensionTargetPosition = 0.5f;
    [SerializeField, Range(0, 5)] private int wheelContactWarmupFixedSteps = 1;
    [SerializeField, Min(0f)] private float wheelForwardFrictionStiffness = 1.4f;
    [SerializeField, Min(0f)] private float wheelSidewaysFrictionStiffness = 1.8f;
    [SerializeField, Range(0f, 0.5f)] private float steeringInputDeadZone = 0.08f;
    [SerializeField, Min(1f)] private float steeringResponseDegreesPerSecond = 120f;
    [SerializeField, Min(1f)] private float steeringReversalDegreesPerSecond = 45f;

    [Header("Driver support")]
    [SerializeField, Min(0f)] private float driverSupportSpring = 18f;
    [SerializeField, Min(0f)] private float driverSupportDamping = 7f;
    [SerializeField, Min(0f)] private float maximumDriverSupportAcceleration = 15f;
    [SerializeField, Min(0.1f)] private float driverSupportGroundProbeDistance = 1.5f;
    [SerializeField, Min(0f)] private float driverSupportGroundFollowSpeed = 4f;

    [Header("Player")]
    [SerializeField, Min(0f)] private float driverFollowSpeed = 8f;
    [SerializeField, Min(0f)] private float passengerFollowSpeed = 12f;
    [SerializeField, Min(0f)] private float maximumExitSpeed = 0.8f;
    [SerializeField, Min(0.1f)] private float driverHardTetherDistance = 2f;
    [SerializeField, Min(0.05f)] private float driverHardTetherDelay = 0.5f;
    [SerializeField, Min(0f)] private float exitCollisionGraceMinimum = 0.2f;
    [SerializeField, Min(0f)] private float exitCollisionGraceMaximum = 1.5f;
    [SerializeField, Min(0f)] private float exitSeparationPadding = 0.05f;
    [SerializeField, Min(0.1f)] private float exitSearchRadius = 1.8f;
    [SerializeField, Min(0.1f)] private float exitGroundProbeDistance = 2f;

    [Header("Stamina")]
    [SerializeField, Min(0f)] private float baseStaminaDrain = 0.25f;
    [SerializeField, Min(0f)] private float loadedStaminaDrain = 1.25f;
    [SerializeField, Min(0f)] private float uphillStaminaDrain = 1.5f;
    [SerializeField, Min(0f)] private float maximumStaminaDrain = 3f;

    [Header("Boarding and tipping")]
    [SerializeField, Min(0f)] private float automaticBoardingMinimumSpeed = 1.5f;
    [SerializeField, Range(-1f, 1f)] private float automaticBoardingDirectionDot = 0.65f;
    [SerializeField, Range(1f, 89f)] private float tippingAngle = 60f;
    [SerializeField, Min(0.05f)] private float tippingDuration = 0.25f;
    [SerializeField, Min(0.1f)] private float rightingHoldDuration = 1.5f;
    [SerializeField, Min(1f)] private float rightingAngularSpeed = 120f;
    [SerializeField, Min(0f)] private float maximumRightingLinearSpeed = 0.2f;

    public float BaseMass => Mathf.Max(1f, baseMass);
    public float MaximumResourceCargoMass => Mathf.Max(0f, maximumResourceCargoMass);
    public float ConcreteBatchMass => Mathf.Max(0f, concreteBatchMass);
    public float PassengerMass => Mathf.Max(0f, passengerMass);
    public int ResourceSlots => Mathf.Clamp(resourceSlots, 1, 8);
    public float DriveForce => Mathf.Max(0f, driveForce);
    public float BrakeForce => Mathf.Max(0f, brakeForce);
    public float IdleBrakeForce => Mathf.Max(0f, idleBrakeForce);
    public float IdleAngularDamping => Mathf.Max(0f, idleAngularDamping);
    public float IdleDampingMaximumSpeed => Mathf.Max(0f, idleDampingMaximumSpeed);
    public float MaximumForwardSpeed => Mathf.Max(0f, maximumForwardSpeed);
    public float MaximumReverseSpeed => Mathf.Max(0f, maximumReverseSpeed);
    public float MaximumSteeringAngle => maximumSteeringAngle;
    public float MinimumSteeringSpeed => minimumSteeringSpeed;
    public float SteeringTorque => steeringTorque;
    public float DriverStabilizingTorque => driverStabilizingTorque;
    public float DriverStabilizingDamping => driverStabilizingDamping;
    public float MaximumDrivenYawSpeedDegrees => Mathf.Max(0f, maximumDrivenYawSpeedDegrees);
    public float DrivenYawResponse => Mathf.Max(0f, drivenYawResponse);
    public float MaximumDrivenYawAccelerationDegrees => Mathf.Max(0f, maximumDrivenYawAccelerationDegrees);
    public float EmptyLateralVelocityDamping => Mathf.Max(0f, emptyLateralVelocityDamping);
    public float LoadedLateralVelocityDamping => Mathf.Max(0f, loadedLateralVelocityDamping);
    public float EmptyMaximumLateralGripAcceleration => Mathf.Max(0f, emptyMaximumLateralGripAcceleration);
    public float LoadedMaximumLateralGripAcceleration => Mathf.Max(0f, loadedMaximumLateralGripAcceleration);
    public float InputTimeout => Mathf.Max(0.05f, inputTimeout);
    public float WheelRadius => Mathf.Max(0.05f, wheelRadius);
    public float WheelSuspensionDistance => Mathf.Max(0.001f, wheelSuspensionDistance);
    public float WheelSuspensionFrequency => Mathf.Max(0.1f, wheelSuspensionFrequency);
    public float WheelSuspensionDampingRatio => Mathf.Max(0f, wheelSuspensionDampingRatio);
    public float WheelSuspensionTargetPosition => Mathf.Clamp01(wheelSuspensionTargetPosition);
    public int WheelContactWarmupFixedSteps => Mathf.Clamp(wheelContactWarmupFixedSteps, 0, 5);
    public float WheelForwardFrictionStiffness => Mathf.Max(0f, wheelForwardFrictionStiffness);
    public float WheelSidewaysFrictionStiffness => Mathf.Max(0f, wheelSidewaysFrictionStiffness);
    public float SteeringInputDeadZone => Mathf.Clamp(steeringInputDeadZone, 0f, 0.5f);
    public float SteeringResponseDegreesPerSecond => Mathf.Max(1f, steeringResponseDegreesPerSecond);
    public float SteeringReversalDegreesPerSecond => Mathf.Max(1f, steeringReversalDegreesPerSecond);
    public float DriverSupportSpring => Mathf.Max(0f, driverSupportSpring);
    public float DriverSupportDamping => Mathf.Max(0f, driverSupportDamping);
    public float MaximumDriverSupportAcceleration => Mathf.Max(0f, maximumDriverSupportAcceleration);
    public float DriverSupportGroundProbeDistance => Mathf.Max(0.1f, driverSupportGroundProbeDistance);
    public float DriverSupportGroundFollowSpeed => Mathf.Max(0f, driverSupportGroundFollowSpeed);
    public float DriverFollowSpeed => driverFollowSpeed;
    public float PassengerFollowSpeed => passengerFollowSpeed;
    public float MaximumExitSpeed => maximumExitSpeed;
    public float DriverHardTetherDistance => driverHardTetherDistance;
    public float DriverHardTetherDelay => driverHardTetherDelay;
    public float ExitCollisionGraceMinimum => Mathf.Max(0f, exitCollisionGraceMinimum);
    public float ExitCollisionGraceMaximum => Mathf.Max(ExitCollisionGraceMinimum, exitCollisionGraceMaximum);
    public float ExitSeparationPadding => Mathf.Max(0f, exitSeparationPadding);
    public float ExitSearchRadius => Mathf.Max(0.1f, exitSearchRadius);
    public float ExitGroundProbeDistance => Mathf.Max(0.1f, exitGroundProbeDistance);
    public float BaseStaminaDrain => baseStaminaDrain;
    public float LoadedStaminaDrain => loadedStaminaDrain;
    public float UphillStaminaDrain => uphillStaminaDrain;
    public float MaximumStaminaDrain => maximumStaminaDrain;
    public float AutomaticBoardingMinimumSpeed => automaticBoardingMinimumSpeed;
    public float AutomaticBoardingDirectionDot => automaticBoardingDirectionDot;
    public float TippingAngle => tippingAngle;
    public float TippingDuration => tippingDuration;
    public float RightingHoldDuration => rightingHoldDuration;
    public float RightingAngularSpeed => rightingAngularSpeed;
    public float MaximumRightingLinearSpeed => maximumRightingLinearSpeed;
}
