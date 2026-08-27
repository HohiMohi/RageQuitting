using UnityEngine;

[CreateAssetMenu(fileName = "WheelbarrowProfile", menuName = "Scriptable Objects/Wheelbarrow Profile")]
public class WheelbarrowProfileSO : ScriptableObject
{
    [Header("Mass and cargo")]
    [SerializeField, Min(1f)] private float baseMass = 22f;
    [SerializeField] private Vector3 baseCenterOfMassLocal = new Vector3(0f, 0.45f, -0.15f);
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
    [SerializeField, Range(0f, 89f)] private float stabilizationFadeStartAngle = 35f;
    [SerializeField, Range(0f, 89f)] private float stabilizationFadeEndAngle = 55f;
    [SerializeField, Min(0f)] private float maximumDrivenYawSpeedDegrees = 75f;
    [SerializeField, Min(0f)] private float drivenYawResponse = 8f;
    [SerializeField, Min(0f)] private float maximumDrivenYawAccelerationDegrees = 240f;
    [SerializeField, Min(0f)] private float maximumDrivenYawJerkDegrees = 1080f;
    [SerializeField, Min(0f)] private float emptyLateralVelocityDamping = 8f;
    [SerializeField, Min(0f)] private float loadedLateralVelocityDamping = 4f;
    [SerializeField, Min(0f)] private float emptyMaximumLateralGripAcceleration = 14f;
    [SerializeField, Min(0f)] private float loadedMaximumLateralGripAcceleration = 8f;
    [SerializeField, Min(0.05f)] private float inputTimeout = 0.25f;

    [Header("Client presentation")]
    [SerializeField, Min(0f)] private float clientPresentationMaximumPositionLead = 0.35f;
    [SerializeField, Min(0f)] private float clientPresentationMaximumYawLead = 10f;
    [SerializeField, Min(0.01f)] private float clientPresentationReconciliationSpeed = 6f;
    [SerializeField, Min(0.01f)] private float clientPresentationVelocityResponse = 5f;
    [SerializeField, Range(1, 6)] private int clientPresentationBufferTicks = 2;
    [SerializeField, Min(0f)] private float clientPresentationMaximumExtrapolation = 0.1f;
    [SerializeField, Min(0.1f)] private float clientPresentationTeleportDistance = 2f;
    [SerializeField, Range(1f, 180f)] private float clientPresentationTeleportAngle = 45f;
    [SerializeField, Range(4, 64)] private int clientPresentationSnapshotCapacity = 24;

    [Header("Network motion authority")]
    [SerializeField, Range(20f, 120f)] private float motionSnapshotRate = 50f;
    [SerializeField, Range(0f, 0.2f)] private float observerPresentationDelay = 0.04f;
    [SerializeField, Min(0.1f)] private float motionMaximumLinearSpeed = 14f;
    [SerializeField, Min(1f)] private float motionMaximumAngularSpeedDegrees = 240f;
    [SerializeField, Min(0f)] private float motionPositionTolerance = 0.2f;
    [SerializeField, Min(0f)] private float motionRotationToleranceDegrees = 8f;
    [SerializeField, Min(0f)] private float motionCorrectionPositionThreshold = 0.45f;
    [SerializeField, Min(0f)] private float motionCorrectionRotationThresholdDegrees = 15f;

    [Header("Cornering rollover")]
    [SerializeField] private bool enableCorneringRollover = true;
    [SerializeField, Min(0.1f)] private float corneringRolloverDuration = 2f;
    [SerializeField, Min(0f)] private float corneringRolloverRecoveryRate = 1f;
    [SerializeField, Range(0f, 1f)] private float minimumRolloverSpeedRatio = 0.65f;
    [SerializeField, Range(0f, 1f)] private float minimumRolloverSteeringRatio = 0.7f;
    [SerializeField, Range(0.1f, 1f)] private float fullLoadRolloverReferenceSpeedRatio = 0.6f;
    [SerializeField, Range(0f, 1f)] private float oneResourceRolloverLoadFactor = 0.25f;
    [SerializeField, Range(0f, 1f)] private float twoResourcesRolloverLoadFactor = 0.5f;
    [SerializeField, Range(0f, 1f)] private float threeResourcesRolloverLoadFactor = 0.8f;
    [SerializeField, Range(0f, 1f)] private float concreteRolloverLoadFactor = 1f;
    [SerializeField, Range(0f, 1f)] private float passengerRolloverLoadFactor = 1f;
    [SerializeField, Min(0f)] private float rolloverGroundContactGraceDuration = 0.15f;
    [SerializeField, Min(0f)] private float rolloverManeuverGraceDuration = 0.2f;
    [SerializeField, Range(0f, 89f)] private float maximumCorneringRollAngle = 70f;
    [SerializeField, Min(0f)] private float corneringRollSpring = 22f;
    [SerializeField, Min(0f)] private float corneringRollDamping = 5f;
    [SerializeField, Min(0f)] private float maximumCorneringRollAcceleration = 28f;
    [SerializeField, Min(0f)] private float minimumCommittedRolloverAcceleration = 28f;

    [Header("Wheel contact solver")]
    [SerializeField, Min(0.05f)] private float wheelRadius = 0.44f;
    [SerializeField, Min(0.01f)] private float wheelContactProbeDistance = 0.12f;
    [SerializeField, Min(0f)] private float wheelSuspensionSpring = 45f;
    [SerializeField, Min(0f)] private float wheelSuspensionDamping = 10f;
    [SerializeField, Min(0f)] private float wheelMaximumSupportAcceleration = 18f;
    [SerializeField, Range(0f, 1f)] private float minimumWheelGroundNormalDot = 0.25f;
    [SerializeField, Min(0f)] private float wheelContactValidationMargin = 0.05f;
    [SerializeField, Min(0f)] private float groundNormalFilterSpeed = 12f;
    [SerializeField, Min(0f)] private float groundHeightFilterSpeed = 4f;
    [SerializeField, Min(0f)] private float maximumLongitudinalGripAcceleration = 10f;
    [SerializeField, Min(0f)] private float maximumLateralGripAcceleration = 12f;
    [SerializeField, Min(0.05f)] private float maximumStabilizationCenterOfMassHeight = 0.65f;

    [Header("Legacy WheelCollider (migration only)")]
    [SerializeField, Min(0.001f)] private float wheelSuspensionDistance = 0.03f;
    [SerializeField, Min(0.1f)] private float wheelSuspensionFrequency = 7f;
    [SerializeField, Min(0f)] private float wheelSuspensionDampingRatio = 1f;
    [SerializeField, Range(0f, 1f)] private float wheelSuspensionTargetPosition = 0.5f;
    [SerializeField, Range(0, 5)] private int wheelContactWarmupFixedSteps = 1;
    [SerializeField, Min(0f)] private float wheelForwardFrictionStiffness = 1.4f;
    [SerializeField, Min(0f)] private float wheelSidewaysFrictionStiffness = 1.8f;
    [SerializeField, Range(0f, 0.5f)] private float steeringInputDeadZone = 0.08f;
    [SerializeField, Min(0.01f)] private float steeringInputRampUp = 2.5f;
    [SerializeField, Min(0.01f)] private float steeringInputRelease = 6f;
    [SerializeField, Min(1f)] private float steeringResponseDegreesPerSecond = 240f;
    [SerializeField, Min(1f)] private float steeringReversalDegreesPerSecond = 60f;

    [Header("Driver support")]
    [SerializeField, Min(0f)] private float driverSupportSpring = 18f;
    [SerializeField, Min(0f)] private float driverSupportDamping = 7f;
    [SerializeField, Min(0f)] private float maximumDriverSupportAcceleration = 15f;
    [SerializeField, Min(0f)] private float driverSupportMaximumHeightCorrection = 0.12f;
    [SerializeField, Min(0.1f)] private float driverSupportGroundProbeDistance = 1.5f;
    [SerializeField, Min(0f)] private float driverSupportGroundFollowSpeed = 4f;

    [Header("Navigation obstacle")]
    [SerializeField, Min(0f)] private float navObstacleSettleDuration = 0.5f;
    [SerializeField, Min(0f)] private float navObstacleLinearSpeedThreshold = 0.15f;
    [SerializeField, Min(0f)] private float navObstacleAngularSpeedThresholdDegrees = 10f;

    [Header("Diagnostics")]
    [SerializeField] private bool enableDiagnostics;

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
    [SerializeField, Min(0.1f)] private float passengerExitPreparationTimeout = 0.5f;
    [SerializeField, Min(0f)] private float forcedExitSearchRadiusGrowthRate = 0.75f;
    [SerializeField, Min(0.1f)] private float maximumForcedExitSearchRadius = 4f;
    [SerializeField, Min(0.1f)] private float forcedExitFallbackDelay = 3f;
    [SerializeField, Min(0f)] private float exitDeniedMessageDuration = 1.25f;
    [SerializeField] private ExternalImpulseProfileSO passengerTippedEjectionImpulseProfile;

    [Header("Stamina")]
    [SerializeField] private bool enableDrivingStaminaDrain = true;
    [SerializeField, Min(0f)] private float baseStaminaDrain = 0.25f;
    [SerializeField, Min(0f)] private float loadedStaminaDrain = 1.25f;
    [SerializeField, Min(0f)] private float uphillStaminaDrain = 1.5f;
    [SerializeField, Min(0f)] private float maximumStaminaDrain = 3f;

    [Header("Boarding and tipping")]
    [SerializeField, Min(0f)] private float automaticBoardingMinimumSpeed = 1.5f;
    [SerializeField, Range(-1f, 1f)] private float automaticBoardingDirectionDot = 0.65f;
    [SerializeField, Min(0.1f)] private float passengerBoardingPreparationTimeout = 0.5f;
    [SerializeField, Min(0.05f)] private float passengerPlacementDuration = 0.2f;
    [SerializeField, Min(0f)] private float automaticBoardingLeadDistance = 0.6f;
    [SerializeField, Range(1f, 89f)] private float tippingAngle = 60f;
    [SerializeField, Min(0.05f)] private float tippingDuration = 0.25f;
    [SerializeField, Min(0.1f)] private float rightingHoldDuration = 1.5f;
    [SerializeField, Min(1f)] private float rightingAngularSpeed = 120f;
    [SerializeField, Min(0f)] private float maximumRightingLinearSpeed = 0.2f;
    [SerializeField, Min(0f)] private float tippedAngularDamping = 3f;
    [SerializeField, Range(0f, 89f)] private float tippedMinimumRestAngle = 55f;
    [SerializeField, Range(0f, 89f)] private float tippedTargetRestAngle = 72f;
    [SerializeField, Min(0f)] private float tippedRecoveryTorque = 60f;
    [SerializeField, Min(0.05f)] private float rightingPlacementDuration = 0.4f;
    [SerializeField, Min(0f)] private float rightingLiftClearance = 0.25f;
    [SerializeField, Min(0f)] private float rightingPlacementSearchRadius = 0.75f;

    public float BaseMass => Mathf.Max(1f, baseMass);
    public Vector3 BaseCenterOfMassLocal => baseCenterOfMassLocal;
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
    public float StabilizationFadeStartAngle => Mathf.Min(stabilizationFadeStartAngle, StabilizationFadeEndAngle);
    public float StabilizationFadeEndAngle => Mathf.Max(stabilizationFadeStartAngle, stabilizationFadeEndAngle);
    public float MaximumDrivenYawSpeedDegrees => Mathf.Max(0f, maximumDrivenYawSpeedDegrees);
    public float DrivenYawResponse => Mathf.Max(0f, drivenYawResponse);
    public float MaximumDrivenYawAccelerationDegrees => Mathf.Max(0f, maximumDrivenYawAccelerationDegrees);
    public float MaximumDrivenYawJerkDegrees => Mathf.Max(0f, maximumDrivenYawJerkDegrees);
    public float EmptyLateralVelocityDamping => Mathf.Max(0f, emptyLateralVelocityDamping);
    public float LoadedLateralVelocityDamping => Mathf.Max(0f, loadedLateralVelocityDamping);
    public float EmptyMaximumLateralGripAcceleration => Mathf.Max(0f, emptyMaximumLateralGripAcceleration);
    public float LoadedMaximumLateralGripAcceleration => Mathf.Max(0f, loadedMaximumLateralGripAcceleration);
    public float InputTimeout => Mathf.Max(0.05f, inputTimeout);
    public float ClientPresentationMaximumPositionLead => Mathf.Max(0f, clientPresentationMaximumPositionLead);
    public float ClientPresentationMaximumYawLead => Mathf.Max(0f, clientPresentationMaximumYawLead);
    public float ClientPresentationReconciliationSpeed => Mathf.Max(0.01f, clientPresentationReconciliationSpeed);
    public float ClientPresentationVelocityResponse => Mathf.Max(0.01f, clientPresentationVelocityResponse);
    public int ClientPresentationBufferTicks => Mathf.Clamp(clientPresentationBufferTicks, 1, 6);
    public float ClientPresentationMaximumExtrapolation => Mathf.Max(0f, clientPresentationMaximumExtrapolation);
    public float ClientPresentationTeleportDistance => Mathf.Max(0.1f, clientPresentationTeleportDistance);
    public float ClientPresentationTeleportAngle => Mathf.Clamp(clientPresentationTeleportAngle, 1f, 180f);
    public int ClientPresentationSnapshotCapacity => Mathf.Clamp(clientPresentationSnapshotCapacity, 4, 64);
    public float MotionSnapshotRate => Mathf.Clamp(motionSnapshotRate, 20f, 120f);
    public float ObserverPresentationDelay => Mathf.Clamp(observerPresentationDelay, 0f, 0.2f);
    public float MotionMaximumLinearSpeed => Mathf.Max(0.1f, motionMaximumLinearSpeed);
    public float MotionMaximumAngularSpeedDegrees => Mathf.Max(1f, motionMaximumAngularSpeedDegrees);
    public float MotionPositionTolerance => Mathf.Max(0f, motionPositionTolerance);
    public float MotionRotationToleranceDegrees => Mathf.Max(0f, motionRotationToleranceDegrees);
    public float MotionCorrectionPositionThreshold => Mathf.Max(0f, motionCorrectionPositionThreshold);
    public float MotionCorrectionRotationThresholdDegrees => Mathf.Max(0f, motionCorrectionRotationThresholdDegrees);
    public bool EnableCorneringRollover => enableCorneringRollover;
    public float CorneringRolloverDuration => Mathf.Max(0.1f, corneringRolloverDuration);
    public float CorneringRolloverRecoveryRate => Mathf.Max(0f, corneringRolloverRecoveryRate);
    public float MinimumRolloverSpeedRatio => Mathf.Clamp01(minimumRolloverSpeedRatio);
    public float MinimumRolloverSteeringRatio => Mathf.Clamp01(minimumRolloverSteeringRatio);
    public float FullLoadRolloverReferenceSpeedRatio => Mathf.Clamp(fullLoadRolloverReferenceSpeedRatio, 0.1f, 1f);
    public float OneResourceRolloverLoadFactor => Mathf.Clamp01(oneResourceRolloverLoadFactor);
    public float TwoResourcesRolloverLoadFactor => Mathf.Clamp01(twoResourcesRolloverLoadFactor);
    public float ThreeResourcesRolloverLoadFactor => Mathf.Clamp01(threeResourcesRolloverLoadFactor);
    public float ConcreteRolloverLoadFactor => Mathf.Clamp01(concreteRolloverLoadFactor);
    public float PassengerRolloverLoadFactor => Mathf.Clamp01(passengerRolloverLoadFactor);
    public float RolloverGroundContactGraceDuration => Mathf.Max(0f, rolloverGroundContactGraceDuration);
    public float RolloverManeuverGraceDuration => Mathf.Max(0f, rolloverManeuverGraceDuration);
    public float MaximumCorneringRollAngle => Mathf.Clamp(maximumCorneringRollAngle, 0f, 89f);
    public float CorneringRollSpring => Mathf.Max(0f, corneringRollSpring);
    public float CorneringRollDamping => Mathf.Max(0f, corneringRollDamping);
    public float MaximumCorneringRollAcceleration => Mathf.Max(0f, maximumCorneringRollAcceleration);
    public float MinimumCommittedRolloverAcceleration => Mathf.Max(0f, minimumCommittedRolloverAcceleration);
    public float WheelRadius => Mathf.Max(0.05f, wheelRadius);
    public float WheelContactProbeDistance => Mathf.Max(0.01f, wheelContactProbeDistance);
    public float WheelSuspensionSpring => Mathf.Max(0f, wheelSuspensionSpring);
    public float WheelSuspensionDamping => Mathf.Max(0f, wheelSuspensionDamping);
    public float WheelMaximumSupportAcceleration => Mathf.Max(0f, wheelMaximumSupportAcceleration);
    public float MinimumWheelGroundNormalDot => Mathf.Clamp01(minimumWheelGroundNormalDot);
    public float WheelContactValidationMargin => Mathf.Max(0f, wheelContactValidationMargin);
    public float GroundNormalFilterSpeed => Mathf.Max(0f, groundNormalFilterSpeed);
    public float GroundHeightFilterSpeed => Mathf.Max(0f, groundHeightFilterSpeed);
    public float MaximumLongitudinalGripAcceleration => Mathf.Max(0f, maximumLongitudinalGripAcceleration);
    public float MaximumLateralGripAcceleration => Mathf.Max(0f, maximumLateralGripAcceleration);
    public float MaximumStabilizationCenterOfMassHeight => Mathf.Max(0.05f, maximumStabilizationCenterOfMassHeight);
    public float WheelSuspensionDistance => Mathf.Max(0.001f, wheelSuspensionDistance);
    public float WheelSuspensionFrequency => Mathf.Max(0.1f, wheelSuspensionFrequency);
    public float WheelSuspensionDampingRatio => Mathf.Max(0f, wheelSuspensionDampingRatio);
    public float WheelSuspensionTargetPosition => Mathf.Clamp01(wheelSuspensionTargetPosition);
    public int WheelContactWarmupFixedSteps => Mathf.Clamp(wheelContactWarmupFixedSteps, 0, 5);
    public float WheelForwardFrictionStiffness => Mathf.Max(0f, wheelForwardFrictionStiffness);
    public float WheelSidewaysFrictionStiffness => Mathf.Max(0f, wheelSidewaysFrictionStiffness);
    public float SteeringInputDeadZone => Mathf.Clamp(steeringInputDeadZone, 0f, 0.5f);
    public float SteeringInputRampUp => Mathf.Max(0.01f, steeringInputRampUp);
    public float SteeringInputRelease => Mathf.Max(0.01f, steeringInputRelease);
    public float SteeringResponseDegreesPerSecond => Mathf.Max(1f, steeringResponseDegreesPerSecond);
    public float SteeringReversalDegreesPerSecond => Mathf.Max(1f, steeringReversalDegreesPerSecond);
    public float DriverSupportSpring => Mathf.Max(0f, driverSupportSpring);
    public float DriverSupportDamping => Mathf.Max(0f, driverSupportDamping);
    public float MaximumDriverSupportAcceleration => Mathf.Max(0f, maximumDriverSupportAcceleration);
    public float DriverSupportMaximumHeightCorrection => Mathf.Max(0f, driverSupportMaximumHeightCorrection);
    public float DriverSupportGroundProbeDistance => Mathf.Max(0.1f, driverSupportGroundProbeDistance);
    public float DriverSupportGroundFollowSpeed => Mathf.Max(0f, driverSupportGroundFollowSpeed);
    public float NavObstacleSettleDuration => Mathf.Max(0f, navObstacleSettleDuration);
    public float NavObstacleLinearSpeedThreshold => Mathf.Max(0f, navObstacleLinearSpeedThreshold);
    public float NavObstacleAngularSpeedThresholdDegrees => Mathf.Max(0f, navObstacleAngularSpeedThresholdDegrees);
    public bool EnableDiagnostics => enableDiagnostics;
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
    public float PassengerExitPreparationTimeout => Mathf.Max(0.1f, passengerExitPreparationTimeout);
    public float ForcedExitSearchRadiusGrowthRate => Mathf.Max(0f, forcedExitSearchRadiusGrowthRate);
    public float MaximumForcedExitSearchRadius => Mathf.Max(ExitSearchRadius, maximumForcedExitSearchRadius);
    public float ForcedExitFallbackDelay => Mathf.Max(0.1f, forcedExitFallbackDelay);
    public float ExitDeniedMessageDuration => Mathf.Max(0f, exitDeniedMessageDuration);
    public ExternalImpulseProfileSO PassengerTippedEjectionImpulseProfile => passengerTippedEjectionImpulseProfile;
    public bool EnableDrivingStaminaDrain => enableDrivingStaminaDrain;
    public float BaseStaminaDrain => baseStaminaDrain;
    public float LoadedStaminaDrain => loadedStaminaDrain;
    public float UphillStaminaDrain => uphillStaminaDrain;
    public float MaximumStaminaDrain => maximumStaminaDrain;
    public float AutomaticBoardingMinimumSpeed => automaticBoardingMinimumSpeed;
    public float AutomaticBoardingDirectionDot => automaticBoardingDirectionDot;
    public float PassengerBoardingPreparationTimeout => Mathf.Max(0.1f, passengerBoardingPreparationTimeout);
    public float PassengerPlacementDuration => Mathf.Max(0.05f, passengerPlacementDuration);
    public float AutomaticBoardingLeadDistance => Mathf.Max(0f, automaticBoardingLeadDistance);
    public float TippingAngle => tippingAngle;
    public float TippingDuration => tippingDuration;
    public float RightingHoldDuration => rightingHoldDuration;
    public float RightingAngularSpeed => rightingAngularSpeed;
    public float MaximumRightingLinearSpeed => maximumRightingLinearSpeed;
    public float TippedAngularDamping => Mathf.Max(0f, tippedAngularDamping);
    public float TippedMinimumRestAngle => Mathf.Clamp(tippedMinimumRestAngle, 0f, 89f);
    public float TippedTargetRestAngle => Mathf.Clamp(tippedTargetRestAngle, TippedMinimumRestAngle, 89f);
    public float TippedRecoveryTorque => Mathf.Max(0f, tippedRecoveryTorque);
    public float RightingPlacementDuration => Mathf.Max(0.05f, rightingPlacementDuration);
    public float RightingLiftClearance => Mathf.Max(0f, rightingLiftClearance);
    public float RightingPlacementSearchRadius => Mathf.Max(0f, rightingPlacementSearchRadius);
}
