using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
public class WheelbarrowController : NetworkBehaviour, IConcreteBatchReceiver
{
    public const ulong NoClient = ulong.MaxValue;
    private static readonly HashSet<WheelbarrowController> Instances = new HashSet<WheelbarrowController>();

    [Header("Configuration")]
    [SerializeField] private WheelbarrowProfileSO profile;
    [SerializeField] private Rigidbody physicsBody;
    [SerializeField] private Collider wheelContactCollider;
    [SerializeField] private WheelCollider drivenWheelCollider;
    [SerializeField] private NavMeshObstacle navigationObstacle;
    [SerializeField] private Transform wheelVisual;
    [SerializeField, Min(0.01f)] private float wheelVisualRadius = 0.44f;
    [SerializeField] private Collider[] restingSupportColliders = Array.Empty<Collider>();
    [SerializeField] private Transform driverAnchor;
    [SerializeField] private Transform driverSupportPoint;
    [SerializeField] private Transform passengerAnchor;
    [SerializeField] private Transform cargoRoot;
    [SerializeField] private Transform[] cargoSlots = Array.Empty<Transform>();
    [SerializeField] private GameObject concreteCargoVisual;
    [SerializeField] private GameObject spillVisual;
    [SerializeField, Min(0.1f)] private float spillVisualDuration = 1.25f;
    [SerializeField] private Transform leftPourAnchor;
    [SerializeField] private Transform rightPourAnchor;
    [SerializeField] private Transform[] safeExitPoints = Array.Empty<Transform>();
    [SerializeField] private Collider rightingInteractionCollider;

    private readonly NetworkVariable<byte> stateNetwork = new NetworkVariable<byte>((byte)WheelbarrowState.Free);
    private readonly NetworkVariable<ulong> driverNetwork = new NetworkVariable<ulong>(NoClient);
    private readonly NetworkVariable<ulong> passengerNetwork = new NetworkVariable<ulong>(NoClient);
    private readonly NetworkVariable<int> concreteLoadsNetwork = new NetworkVariable<int>();
    private readonly NetworkVariable<ulong> dockNetwork = new NetworkVariable<ulong>(NoClient);
    private readonly NetworkVariable<int> spillSequenceNetwork = new NetworkVariable<int>();
    private readonly NetworkList<ulong> cargoNetwork = new NetworkList<ulong>();
    private readonly List<BaseResourceNew> localCargo = new List<BaseResourceNew>();
    private readonly Dictionary<ulong, PendingSafeExit> pendingSafeExits = new Dictionary<ulong, PendingSafeExit>();
    private readonly HashSet<ulong> collisionIgnoredPlayers = new HashSet<ulong>();

    private sealed class PendingSafeExit
    {
        public float StartedAt;
    }

    private WheelbarrowState localState;
    private ulong localDriver = NoClient;
    private ulong localPassenger = NoClient;
    private int localConcreteLoads;
    private float throttleInput;
    private float steeringInput;
    private float lastReceivedThrottleInput;
    private float lastReceivedSteeringInput;
    private float smoothedSteeringInput;
    private float currentYawAcceleration;
    private float targetYawRate;
    private float residualLateralSpeed;
    private float currentSteeringAngle;
    private float corneringRolloverRisk;
    private float corneringLateralAcceleration;
    private float corneringLoadRatio;
    private float corneringSpeedRatio;
    private float corneringDemand;
    private float lastPositiveCorneringDemand;
    private float lastPositiveCorneringDemandTime = float.NegativeInfinity;
    private bool corneringRolloverCommitted;
    private float lastDrivenWheelContactTime = float.NegativeInfinity;
    private string corneringLoadSource = "Empty";
    private float effectiveRolloverReferenceSpeed;
    private float targetCorneringRollAngle;
    private float previousCorneringDirectionSign;
    private bool drivenWheelGrounded;
    private RaycastHit drivenWheelHit;
    private Vector3 filteredGroundNormal = Vector3.up;
    private Vector3 filteredWheelContactPoint;
    private float wheelSuspensionError;
    private float wheelSupportAcceleration;
    private float driverSupportAcceleration;
    private float longitudinalAcceleration;
    private bool drivenContactInitialized;
    private float lastInputTime;
    private float tippingElapsed;
    private float rightingStartedAt = -1f;
    private ulong rightingClient = NoClient;
    private bool rightingPlacementStarted;
    private float rightingPlacementStartedAt;
    private Vector3 rightingStartPosition;
    private Quaternion rightingStartRotation = Quaternion.identity;
    private Vector3 rightingTargetPosition;
    private Quaternion rightingTargetRotation = Quaternion.identity;
    private Vector3 tippedRestUp = Vector3.up;
    private WheelbarrowDockingStation activeDock;
    private Vector3 securedDockPosition;
    private Quaternion securedDockRotation = Quaternion.identity;
    private bool hasSecuredDockPose;
    private int localSpillSequence;
    private float spillVisualUntil;
    private Quaternion wheelVisualPoseOffset = Quaternion.identity;
    private Vector3 wheelVisualRootLocalPosition;
    private Vector3 previousWheelbarrowPosition;
    private float wheelVisualSpinDegrees;
    private float wheelVisualSteerDegrees;
    private float lastConfiguredWheelMass = float.NaN;
    private WheelbarrowProfileSO lastConfiguredWheelProfile;
    private int driveContactWarmupStepsRemaining;
    private float driverSupportLoadShare;
    private float wheelLoadShare = 1f;
    private float driverSupportGroundClearance;
    private float driverSupportTargetWorldY;
    private bool driverSupportTargetInitialized;
    private readonly RaycastHit[] driverSupportGroundHits = new RaycastHit[12];
    private readonly RaycastHit[] wheelContactHits = new RaycastHit[12];
    private Collider[] physicalColliders = Array.Empty<Collider>();
    private float navObstacleSettledTime;

    public WheelbarrowProfileSO Profile => profile;
    public Rigidbody PhysicsBody => physicsBody;
    public WheelbarrowState State => IsSessionActive ? (WheelbarrowState)stateNetwork.Value : localState;
    public ulong DriverClientId => IsSessionActive ? driverNetwork.Value : localDriver;
    public ulong PassengerClientId => IsSessionActive ? passengerNetwork.Value : localPassenger;
    public int ConcreteLoads => IsSessionActive ? concreteLoadsNetwork.Value : localConcreteLoads;
    public bool HasConcrete => ConcreteLoads > 0;
    public bool HasResourceCargo => CargoCount > 0;
    public int CargoCount => IsSessionActive ? cargoNetwork.Count : localCargo.Count;
    public float Speed => physicsBody != null ? physicsBody.linearVelocity.magnitude : 0f;
    public bool IsDocked => State == WheelbarrowState.Docked || State == WheelbarrowState.Pouring;
    public bool IsDockSecured => IsDocked;
    public float CorneringRolloverRisk => corneringRolloverRisk;
    public float CorneringLoadRatio => corneringLoadRatio;
    public float EffectiveRolloverReferenceSpeed => effectiveRolloverReferenceSpeed;
    public float CorneringRolloverDemand => corneringDemand;
    public float TimeSinceDrivenWheelContact => Time.time - lastDrivenWheelContactTime;
    public string CorneringLoadSource => corneringLoadSource;
    public bool IsBeingRighted => State == WheelbarrowState.Righting;
    public float NormalizedRightingProgress
    {
        get
        {
            if (!IsBeingRighted) return 0f;
            float hold = profile != null ? profile.RightingHoldDuration : 1.5f;
            if (!rightingPlacementStarted) return Mathf.Clamp01((Time.time - rightingStartedAt) / Mathf.Max(0.01f, hold));
            float placement = profile != null ? profile.RightingPlacementDuration : 0.4f;
            return Mathf.Clamp01((Time.time - rightingPlacementStartedAt) / Mathf.Max(0.01f, placement));
        }
    }
    public bool CanReceiveConcreteBatch => IsDockSecured && !HasConcrete && !HasResourceCargo;
    private bool IsSessionActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool HasAuthority => !IsSessionActive || IsServer;

    private void Awake()
    {
        Instances.Add(this);
        physicsBody ??= GetComponent<Rigidbody>();
        navigationObstacle ??= GetComponent<NavMeshObstacle>();
        if (wheelVisual != null)
        {
            wheelVisualPoseOffset = Quaternion.Inverse(transform.rotation) * wheelVisual.rotation;
            wheelVisualRootLocalPosition = transform.InverseTransformPoint(wheelVisual.position);
        }
        previousWheelbarrowPosition = transform.position;
        physicalColliders = GetComponentsInChildren<Collider>(true)
            .Where(item => item != null && !item.isTrigger)
            .ToArray();
        ConfigureBody();
        ConfigureWheelContactMode();
        UpdateNavigationObstacle(true);
        if (spillVisual != null) spillVisual.SetActive(false);
    }

    private void OnValidate()
    {
        InvalidateDrivenWheelPhysics();
    }

    private void OnDrawGizmosSelected()
    {
        float radius = profile != null ? profile.WheelRadius : wheelVisualRadius;
        float suspensionDistance = profile != null ? profile.WheelSuspensionDistance : 0.03f;
        float targetPosition = profile != null ? profile.WheelSuspensionTargetPosition : 0.5f;
        Vector3 parkedCenter = wheelVisual != null
            ? transform.InverseTransformPoint(wheelVisual.position)
            : new Vector3(0f, radius, 0.66f);
        Vector3 colliderCenter = parkedCenter + Vector3.up * suspensionDistance * (1f - targetPosition);
        Vector3 nominalWheelCenter = colliderCenter - Vector3.up * suspensionDistance * (1f - targetPosition);

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(parkedCenter, radius);
        Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireSphere(nominalWheelCenter, radius);
        Gizmos.DrawLine(colliderCenter, colliderCenter - Vector3.up * suspensionDistance);
        Gizmos.DrawSphere(colliderCenter, 0.025f);

        Vector3 contact = nominalWheelCenter - Vector3.up * radius;
        float markerSize = Mathf.Max(0.15f, radius * 0.6f);
        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.9f);
        Gizmos.DrawLine(contact - Vector3.right * markerSize, contact + Vector3.right * markerSize);
        Gizmos.DrawLine(contact - Vector3.forward * markerSize, contact + Vector3.forward * markerSize);

        Transform support = driverSupportPoint != null ? driverSupportPoint : driverAnchor;
        if (support != null)
        {
            Vector3 supportLocal = transform.InverseTransformPoint(support.position);
            Gizmos.color = new Color(1f, 0.25f, 0.75f, 0.9f);
            Gizmos.DrawSphere(supportLocal, 0.05f);
            Gizmos.DrawLine(supportLocal, parkedCenter);
        }

        if (physicsBody != null)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.9f);
            Gizmos.DrawSphere(physicsBody.centerOfMass, 0.06f);
        }
        if (Application.isPlaying && profile != null && profile.EnableDiagnostics)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Vector3 origin = physicsBody != null ? physicsBody.worldCenterOfMass : transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + transform.forward * targetYawRate);
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(origin, origin + transform.right * residualLateralSpeed);
            Gizmos.color = new Color(1f, 0.45f, 0.05f, 0.95f);
            Gizmos.DrawLine(origin, origin + transform.right * corneringLateralAcceleration * 0.1f);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin, origin + transform.up * corneringRolloverRisk);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(origin, origin + transform.forward * corneringSpeedRatio);
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(origin, 0.05f + corneringLoadRatio * 0.1f);
            Vector3 rollDirection = Quaternion.AngleAxis(targetCorneringRollAngle, transform.forward) * transform.up;
            Gizmos.DrawLine(origin, origin + rollDirection * 0.8f);
            if (drivenWheelGrounded)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(filteredWheelContactPoint, 0.045f);
                Gizmos.DrawLine(filteredWheelContactPoint,
                    filteredWheelContactPoint + filteredGroundNormal * 0.75f);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(filteredWheelContactPoint,
                    filteredWheelContactPoint + filteredGroundNormal * wheelSupportAcceleration * 0.05f);
            }
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    private void OnDestroy()
    {
        RestoreAllPlayerCollisions(false);
        Instances.Remove(this);
    }

    public static WheelbarrowController FindForPlayer(ulong clientId)
    {
        foreach (WheelbarrowController wheelbarrow in Instances)
        {
            if (wheelbarrow != null && (wheelbarrow.DriverClientId == clientId || wheelbarrow.PassengerClientId == clientId))
                return wheelbarrow;
        }
        return null;
    }

    public override void OnNetworkSpawn()
    {
        cargoNetwork.OnListChanged += OnCargoChanged;
        spillSequenceNetwork.OnValueChanged += OnSpillSequenceChanged;
        if (IsServer)
        {
            activeDock = null;
            SetState(WheelbarrowState.Free);
            SetDriver(NoClient);
            SetPassenger(NoClient);
        }
        ConfigureBody();
        ConfigureWheelContactMode();
        UpdateNavigationObstacle(true);
        RefreshCargoReferences();
    }

    public override void OnNetworkDespawn()
    {
        cargoNetwork.OnListChanged -= OnCargoChanged;
        spillSequenceNetwork.OnValueChanged -= OnSpillSequenceChanged;
        if (IsServer)
        {
            activeDock?.ForceReleaseWheelbarrow(this);
            ReleaseAllOccupants(true);
            SpillAllCargo();
        }
        RestoreAllPlayerCollisions(false);
    }

    private void Update()
    {
        if (concreteCargoVisual != null) concreteCargoVisual.SetActive(HasConcrete);
        if (spillVisual != null && spillVisual.activeSelf && Time.time >= spillVisualUntil) spillVisual.SetActive(false);
    }

    private void ConfigureBody()
    {
        if (physicsBody == null) return;
        physicsBody.mass = profile != null ? profile.BaseMass : 22f;
        physicsBody.isKinematic = IsSessionActive && !IsServer;
        physicsBody.useGravity = !physicsBody.isKinematic;
        physicsBody.interpolation = RigidbodyInterpolation.Interpolate;
        physicsBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    private void FixedUpdate()
    {
        if (!HasAuthority || physicsBody == null)
        {
            ConfigureWheelContactMode();
            UpdateNavigationObstacle();
            UpdateWheelVisual();
            return;
        }
        RefreshMassAndCenterOfMass();
        ConfigureWheelContactMode();
        ProcessPendingSafeExits();
        UpdateCargoTransforms();
        if (State == WheelbarrowState.Docked)
        {
            if (!physicsBody.isKinematic) SetDockSecured(true);
            RestoreSecuredDockPose();
        }
        else if (State == WheelbarrowState.Righting) SimulateRighting();
        else if (State == WheelbarrowState.Driven) SimulateDrive();
        else if (State == WheelbarrowState.Tipped) ApplyTippedDamping();
        else if (State != WheelbarrowState.Docked && State != WheelbarrowState.Pouring) ApplyIdleBrake();
        UpdateWheelVisual();
        DetectTipping();
        UpdateNavigationObstacle();
    }

    private void SimulateDrive()
    {
        if (Time.time - lastInputTime > (profile != null ? profile.InputTimeout : 0.25f))
        {
            throttleInput = 0f;
            steeringInput = 0f;
        }

        float unsupportedTilt = profile != null ? profile.StabilizationFadeEndAngle : 55f;
        if (Vector3.Angle(transform.up, Vector3.up) >= unsupportedTilt)
        {
            bool keepsCommittingToTurn = corneringRolloverRisk > 0f &&
                Mathf.Abs(throttleInput) > 0.01f &&
                Mathf.Abs(steeringInput) > (profile != null ? profile.SteeringInputDeadZone : 0.08f);
            if (keepsCommittingToTurn)
            {
                float duration = profile != null ? profile.CorneringRolloverDuration : 2f;
                float committedDemand = Mathf.Max(corneringDemand, lastPositiveCorneringDemand);
                corneringRolloverRisk = Mathf.MoveTowards(
                    corneringRolloverRisk,
                    1f,
                    committedDemand / Mathf.Max(0.1f, duration) * Time.fixedDeltaTime);
                if (corneringRolloverRisk >= 0.99f) corneringRolloverCommitted = true;
                Vector3 rolloverGroundForward = Vector3.ProjectOnPlane(transform.forward, filteredGroundNormal).normalized;
                float rolloverForwardSpeed = rolloverGroundForward.sqrMagnitude > 0.0001f
                    ? Vector3.Dot(physicsBody.linearVelocity, rolloverGroundForward)
                    : 0f;
                ApplyCorneringRollTorque(rolloverForwardSpeed, targetYawRate);
            }
            drivenWheelGrounded = false;
            drivenContactInitialized = false;
            wheelSupportAcceleration = 0f;
            driverSupportAcceleration = 0f;
            return;
        }

        UpdateDrivenWheelContact();
        Vector3 groundNormal = drivenWheelGrounded ? filteredGroundNormal : Vector3.up;
        Vector3 groundForward = Vector3.ProjectOnPlane(transform.forward, groundNormal).normalized;
        if (groundForward.sqrMagnitude <= 0.0001f) groundForward = transform.forward;
        float forwardSpeed = Vector3.Dot(physicsBody.linearVelocity, groundForward);
        float speedLimit = throttleInput >= 0f
            ? (profile != null ? profile.MaximumForwardSpeed : 4f)
            : (profile != null ? profile.MaximumReverseSpeed : 2f);
        bool hasThrottle = Mathf.Abs(throttleInput) > 0.01f;
        bool changingDirection = hasThrottle && Mathf.Abs(forwardSpeed) > 0.05f &&
            Mathf.Sign(throttleInput) != Mathf.Sign(forwardSpeed);
        bool belowSpeedLimit = Mathf.Abs(forwardSpeed) < speedLimit;
        if (driveContactWarmupStepsRemaining > 0)
        {
            driveContactWarmupStepsRemaining--;
            UpdateSteeringAngle(false);
            ApplyDrivenWheelSupport();
            ApplyDriverSupport();
            ApplyDriverStabilization();
            return;
        }

        UpdateSteeringAngle(hasThrottle);
        ApplyDrivenWheelSupport();
        ApplyDrivenLongitudinalGrip(groundForward, forwardSpeed, hasThrottle, changingDirection, belowSpeedLimit);
        ApplyDrivenYawControl(forwardSpeed, hasThrottle);
        ApplyDrivenLateralGrip();
        ApplyCorneringRollover(forwardSpeed, speedLimit);
        ApplyDriverSupport();
        ApplyDriverStabilization();
    }

    private void UpdateSteeringAngle(bool hasThrottle)
    {
        float filteredSteering = UpdateSmoothedSteeringInput(hasThrottle);
        float targetSteer = hasThrottle
            ? filteredSteering * (profile != null ? profile.MaximumSteeringAngle : 30f)
            : 0f;
        currentSteeringAngle = CalculateNextSteeringAngle(
            currentSteeringAngle,
            targetSteer,
            Time.fixedDeltaTime);
    }

    private void UpdateDrivenWheelContact()
    {
        float radius = profile != null ? profile.WheelRadius : wheelVisualRadius;
        float probeDistance = profile != null ? profile.WheelContactProbeDistance : 0.12f;
        float castOriginOffset = radius + probeDistance;
        Vector3 wheelCenter = transform.TransformPoint(wheelVisualRootLocalPosition);
        Vector3 castUp = transform.up;
        Vector3 origin = wheelCenter + castUp * castOriginOffset;
        int count = Physics.SphereCastNonAlloc(
            origin,
            radius,
            -castUp,
            wheelContactHits,
            castOriginOffset + (profile != null ? profile.WheelSuspensionDistance : 0.03f),
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        drivenWheelGrounded = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = wheelContactHits[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null || hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform)) continue;
            if (hit.distance >= nearestDistance) continue;
            nearestDistance = hit.distance;
            drivenWheelHit = hit;
            drivenWheelGrounded = true;
        }

        if (!drivenWheelGrounded)
        {
            drivenContactInitialized = false;
            wheelSuspensionError = 0f;
            wheelSupportAcceleration = 0f;
            return;
        }

        lastDrivenWheelContactTime = Time.time;

        Vector3 rawNormal = drivenWheelHit.normal.sqrMagnitude > 0.0001f
            ? drivenWheelHit.normal.normalized
            : Vector3.up;
        float normalFilter = 1f - Mathf.Exp(-(profile != null ? profile.GroundNormalFilterSpeed : 12f) * Time.fixedDeltaTime);
        float heightFilter = 1f - Mathf.Exp(-(profile != null ? profile.GroundHeightFilterSpeed : 4f) * Time.fixedDeltaTime);
        if (!drivenContactInitialized)
        {
            filteredGroundNormal = rawNormal;
            filteredWheelContactPoint = drivenWheelHit.point;
            drivenContactInitialized = true;
        }
        else
        {
            filteredGroundNormal = Vector3.Slerp(filteredGroundNormal, rawNormal, normalFilter).normalized;
            filteredWheelContactPoint = Vector3.Lerp(filteredWheelContactPoint, drivenWheelHit.point, heightFilter);
        }
        wheelSuspensionError = Mathf.Clamp(castOriginOffset - nearestDistance,
            -(profile != null ? profile.WheelSuspensionDistance : 0.03f),
            profile != null ? profile.WheelSuspensionDistance : 0.03f);
    }

    private void ApplyDrivenWheelSupport()
    {
        if (!drivenWheelGrounded) return;
        float normalVelocity = Vector3.Dot(physicsBody.linearVelocity, filteredGroundNormal);
        float gravitySupport = Mathf.Max(0f, -Vector3.Dot(Physics.gravity, filteredGroundNormal)) * wheelLoadShare;
        float spring = profile != null ? profile.WheelSuspensionSpring : 45f;
        float damping = profile != null ? profile.WheelSuspensionDamping : 10f;
        float maximumAcceleration = profile != null ? profile.WheelMaximumSupportAcceleration : 18f;
        wheelSupportAcceleration = Mathf.Clamp(
            gravitySupport +
            wheelSuspensionError * spring * wheelLoadShare -
            normalVelocity * damping * wheelLoadShare,
            0f,
            maximumAcceleration * wheelLoadShare);
    }

    private void ApplyDrivenLongitudinalGrip(Vector3 groundForward, float forwardSpeed,
        bool hasThrottle, bool changingDirection, bool belowSpeedLimit)
    {
        longitudinalAcceleration = 0f;
        if (!drivenWheelGrounded) return;

        float requestedForce = 0f;
        if (hasThrottle && !changingDirection && belowSpeedLimit)
            requestedForce = throttleInput * (profile != null ? profile.DriveForce : 220f);
        else if (changingDirection || (!hasThrottle && Mathf.Abs(forwardSpeed) > 0.02f))
            requestedForce = -Mathf.Sign(forwardSpeed) * (profile != null ? profile.BrakeForce : 420f);

        float maximumAcceleration = profile != null ? profile.MaximumLongitudinalGripAcceleration : 10f;
        longitudinalAcceleration = Mathf.Clamp(
            requestedForce / Mathf.Max(1f, physicsBody.mass),
            -maximumAcceleration,
            maximumAcceleration);
        Vector3 appliedForce = groundForward * longitudinalAcceleration * physicsBody.mass;
        physicsBody.AddForceAtPosition(
            appliedForce,
            filteredWheelContactPoint,
            ForceMode.Force);

        // The driver carries the handles and counters the pitch generated where the
        // wheel transmits traction. Keep the physical contact force without turning
        // acceleration into a suspension impulse.
        Vector3 tractionTorque = Vector3.Cross(
            filteredWheelContactPoint - physicsBody.worldCenterOfMass,
            appliedForce);
        Vector3 nonYawTractionTorque = Vector3.ProjectOnPlane(tractionTorque, filteredGroundNormal);
        physicsBody.AddTorque(-nonYawTractionTorque, ForceMode.Force);
    }

    private float UpdateSmoothedSteeringInput(bool hasThrottle)
    {
        float deadZone = profile != null ? profile.SteeringInputDeadZone : 0.08f;
        float rawTarget = hasThrottle && Mathf.Abs(steeringInput) > deadZone ? steeringInput : 0f;
        float rate = Mathf.Approximately(rawTarget, 0f)
            ? (profile != null ? profile.SteeringInputRelease : 6f)
            : (profile != null ? profile.SteeringInputRampUp : 2.5f);
        smoothedSteeringInput = Mathf.MoveTowards(
            smoothedSteeringInput,
            rawTarget,
            rate * Time.fixedDeltaTime);
        return smoothedSteeringInput;
    }

    private void ApplyDriverSupport()
    {
        Transform support = driverSupportPoint != null ? driverSupportPoint : driverAnchor;
        if (support == null || physicsBody == null) return;

        if (!driverSupportTargetInitialized) CaptureDriverSupportTarget();
        if (TryGetDriverSupportGroundHeight(support.position, out float groundHeight))
        {
            float desiredTarget = groundHeight + driverSupportGroundClearance;
            float followSpeed = profile != null ? profile.DriverSupportGroundFollowSpeed : 4f;
            driverSupportTargetWorldY = Mathf.MoveTowards(
                driverSupportTargetWorldY,
                desiredTarget,
                followSpeed * Time.fixedDeltaTime);
        }

        Vector3 point = support.position;
        Vector3 supportNormal = drivenWheelGrounded ? filteredGroundNormal : Vector3.up;
        float normalVelocity = Vector3.Dot(physicsBody.linearVelocity, supportNormal);
        float gravitySupport = Mathf.Max(0f, -Vector3.Dot(Physics.gravity, supportNormal)) * driverSupportLoadShare;
        float spring = profile != null ? profile.DriverSupportSpring : 18f;
        float damping = profile != null ? profile.DriverSupportDamping : 7f;
        float maximumAcceleration = profile != null ? profile.MaximumDriverSupportAcceleration : 15f;
        float maximumHeightCorrection = profile != null ? profile.DriverSupportMaximumHeightCorrection : 0.12f;
        float heightError = Mathf.Clamp(
            driverSupportTargetWorldY - point.y,
            -maximumHeightCorrection,
            maximumHeightCorrection);
        driverSupportAcceleration = gravitySupport +
            heightError * spring * driverSupportLoadShare -
            normalVelocity * damping * driverSupportLoadShare;
        driverSupportAcceleration = Mathf.Clamp(
            driverSupportAcceleration,
            0f,
            maximumAcceleration * driverSupportLoadShare);
        physicsBody.AddForce(
            supportNormal * (wheelSupportAcceleration + driverSupportAcceleration),
            ForceMode.Acceleration);
    }

    private void CaptureDriverSupportTarget()
    {
        Transform support = driverSupportPoint != null ? driverSupportPoint : driverAnchor;
        if (support == null) return;

        driverSupportTargetWorldY = support.position.y;
        driverSupportGroundClearance = 0f;
        if (TryGetDriverSupportGroundHeight(support.position, out float groundHeight))
            driverSupportGroundClearance = Mathf.Max(0f, support.position.y - groundHeight);
        driverSupportTargetInitialized = true;
    }

    private bool TryGetDriverSupportGroundHeight(Vector3 supportPosition, out float height)
    {
        float distance = profile != null ? profile.DriverSupportGroundProbeDistance : 1.5f;
        Vector3 origin = supportPosition + Vector3.up * 0.2f;
        int count = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            driverSupportGroundHits,
            distance + 0.2f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        height = 0f;
        bool found = false;
        Transform driverRoot = null;
        if (DriverClientId != NoClient && TryGetPlayer(DriverClientId, out NetworkObject driver))
            driverRoot = driver.transform;
        for (int i = 0; i < count; i++)
        {
            Collider hitCollider = driverSupportGroundHits[i].collider;
            if (hitCollider == null || hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform)) continue;
            if (driverRoot != null &&
                (hitCollider.transform == driverRoot || hitCollider.transform.IsChildOf(driverRoot))) continue;
            if (driverSupportGroundHits[i].distance >= nearestDistance) continue;
            nearestDistance = driverSupportGroundHits[i].distance;
            height = driverSupportGroundHits[i].point.y;
            found = true;
        }
        return found;
    }

    private float CalculateNextSteeringAngle(float currentAngle, float targetAngle, float deltaTime)
    {
        bool reversesDirection = Mathf.Abs(currentAngle) > 0.01f &&
            Mathf.Abs(targetAngle) > 0.01f &&
            Mathf.Sign(currentAngle) != Mathf.Sign(targetAngle);
        float resolvedTarget = reversesDirection ? 0f : targetAngle;
        float response = reversesDirection
            ? (profile != null ? profile.SteeringReversalDegreesPerSecond : 60f)
            : (profile != null ? profile.SteeringResponseDegreesPerSecond : 240f);
        return Mathf.MoveTowards(currentAngle, resolvedTarget, response * Mathf.Max(0f, deltaTime));
    }

    private void ApplyDrivenYawControl(float forwardSpeed, bool hasThrottle)
    {
        Vector3 worldUp = Vector3.up;
        Vector3 angularVelocity = physicsBody.angularVelocity;
        float currentYaw = Vector3.Dot(angularVelocity, worldUp);
        float maximumYaw = (profile != null ? profile.MaximumDrivenYawSpeedDegrees : 75f) * Mathf.Deg2Rad;

        float clampedYaw = Mathf.Clamp(currentYaw, -maximumYaw, maximumYaw);
        if (!Mathf.Approximately(clampedYaw, currentYaw))
        {
            physicsBody.angularVelocity = angularVelocity + worldUp * (clampedYaw - currentYaw);
            currentYaw = clampedYaw;
        }

        float minimumSpeed = profile != null ? profile.MinimumSteeringSpeed : 0.15f;
        float wheelbase = GetWheelbase();
        float steeringRadians = currentSteeringAngle * Mathf.Deg2Rad;
        float requestedYaw = hasThrottle && Mathf.Abs(forwardSpeed) >= minimumSpeed
            ? forwardSpeed / wheelbase * Mathf.Tan(steeringRadians)
            : 0f;
        targetYawRate = Mathf.Clamp(requestedYaw, -maximumYaw, maximumYaw);
        float response = profile != null ? profile.DrivenYawResponse : 8f;
        float maximumAcceleration = (profile != null ? profile.MaximumDrivenYawAccelerationDegrees : 240f) * Mathf.Deg2Rad;
        float desiredAcceleration = Mathf.Clamp(
            (targetYawRate - currentYaw) * response,
            -maximumAcceleration,
            maximumAcceleration);
        float maximumJerk = (profile != null ? profile.MaximumDrivenYawJerkDegrees : 1080f) * Mathf.Deg2Rad;
        currentYawAcceleration = Mathf.MoveTowards(
            currentYawAcceleration,
            desiredAcceleration,
            maximumJerk * Time.fixedDeltaTime);
        physicsBody.AddTorque(worldUp * currentYawAcceleration, ForceMode.Acceleration);
    }

    private float GetWheelbase()
    {
        Transform support = driverSupportPoint != null ? driverSupportPoint : driverAnchor;
        float wheelZ = wheelVisualRootLocalPosition.z;
        float supportZ = support != null
            ? transform.InverseTransformPoint(support.position).z
            : wheelZ - 2f;
        return Mathf.Max(0.25f, Mathf.Abs(wheelZ - supportZ));
    }

    private void ApplyDrivenLateralGrip()
    {
        if (!drivenWheelGrounded) return;

        Vector3 lateral = Vector3.ProjectOnPlane(transform.right, filteredGroundNormal);
        if (lateral.sqrMagnitude <= 0.0001f) return;
        lateral.Normalize();

        float maximumAddedMass = Mathf.Max(
                profile != null ? profile.MaximumResourceCargoMass : 60f,
                profile != null ? profile.ConcreteBatchMass : 80f) +
            (profile != null ? profile.PassengerMass : 75f);
        float baseMass = profile != null ? profile.BaseMass : 22f;
        float loadRatio = Mathf.Clamp01((physicsBody.mass - baseMass) / Mathf.Max(1f, maximumAddedMass));
        float damping = Mathf.Lerp(
            profile != null ? profile.EmptyLateralVelocityDamping : 8f,
            profile != null ? profile.LoadedLateralVelocityDamping : 4f,
            loadRatio);
        float legacyMaximumAcceleration = Mathf.Lerp(
            profile != null ? profile.EmptyMaximumLateralGripAcceleration : 14f,
            profile != null ? profile.LoadedMaximumLateralGripAcceleration : 8f,
            loadRatio);
        float maximumAcceleration = Mathf.Min(
            legacyMaximumAcceleration,
            profile != null ? profile.MaximumLateralGripAcceleration : 12f);
        float lateralSpeed = Vector3.Dot(physicsBody.linearVelocity, lateral);
        float wheelZ = transform.InverseTransformPoint(filteredWheelContactPoint).z;
        float centerOfMassZ = physicsBody.centerOfMass.z;
        float currentYaw = Vector3.Dot(physicsBody.angularVelocity, filteredGroundNormal);
        float expectedLateralSpeed = -currentYaw * (wheelZ - centerOfMassZ);
        residualLateralSpeed = lateralSpeed - expectedLateralSpeed;
        float acceleration = Mathf.Clamp(-residualLateralSpeed * damping, -maximumAcceleration, maximumAcceleration);
        physicsBody.AddForce(lateral * acceleration, ForceMode.Acceleration);
    }

    private void ApplyDriverStabilization()
    {
        Vector3 targetUp = drivenWheelGrounded ? filteredGroundNormal : Vector3.up;
        float tiltAngle = Vector3.Angle(transform.up, targetUp);
        float fadeStart = profile != null ? profile.StabilizationFadeStartAngle : 35f;
        float fadeEnd = profile != null ? profile.StabilizationFadeEndAngle : 55f;
        float stabilizationWeight = 1f - Mathf.InverseLerp(fadeStart, Mathf.Max(fadeStart + 0.01f, fadeEnd), tiltAngle);
        stabilizationWeight *= 1f - corneringRolloverRisk;
        if (stabilizationWeight <= 0f) return;
        Vector3 axis = Vector3.Cross(transform.up, targetUp);
        Vector3 nonYawAngular = Vector3.ProjectOnPlane(physicsBody.angularVelocity, targetUp);
        float configuredHeight = profile != null ? profile.MaximumStabilizationCenterOfMassHeight : 0.65f;
        float actualHeight = Mathf.Max(0.05f, Mathf.Abs(physicsBody.centerOfMass.y));
        float centerOfMassScale = Mathf.Clamp01(configuredHeight / actualHeight);
        Vector3 torque = (axis * (profile != null ? profile.DriverStabilizingTorque : 18f) * centerOfMassScale -
            nonYawAngular * (profile != null ? profile.DriverStabilizingDamping : 4f)) * stabilizationWeight;
        physicsBody.AddTorque(torque, ForceMode.Acceleration);
    }

    private void ApplyCorneringRollover(float forwardSpeed, float speedLimit)
    {
        bool enabled = profile == null || profile.EnableCorneringRollover;
        corneringLoadRatio = GetCorneringLoadRatio();
        float maximumSteering = profile != null ? profile.MaximumSteeringAngle : 30f;
        float fullLoadReferenceRatio = profile != null ? profile.FullLoadRolloverReferenceSpeedRatio : 0.6f;
        effectiveRolloverReferenceSpeed = speedLimit * Mathf.Lerp(1f, fullLoadReferenceRatio, corneringLoadRatio);
        corneringSpeedRatio = Mathf.Clamp01(
            Mathf.Abs(forwardSpeed) / Mathf.Max(0.01f, effectiveRolloverReferenceSpeed));
        float steeringRatio = Mathf.Clamp01(Mathf.Abs(currentSteeringAngle) / Mathf.Max(0.01f, maximumSteering));
        float minimumSpeed = profile != null ? profile.MinimumRolloverSpeedRatio : 0.65f;
        float minimumSteering = profile != null ? profile.MinimumRolloverSteeringRatio : 0.7f;
        float activeSpeed = Mathf.InverseLerp(minimumSpeed, 1f, corneringSpeedRatio);
        float activeSteering = Mathf.InverseLerp(minimumSteering, 1f, steeringRatio);
        float yawRate = Vector3.Dot(physicsBody.angularVelocity, filteredGroundNormal);
        corneringLateralAcceleration = Mathf.Abs(forwardSpeed * yawRate);
        float corneringDirectionSign = Mathf.Abs(targetYawRate) > 0.001f ? Mathf.Sign(targetYawRate) : 0f;
        if (corneringDirectionSign != 0f && previousCorneringDirectionSign != 0f &&
            corneringDirectionSign != previousCorneringDirectionSign)
        {
            corneringRolloverRisk = 0f;
            corneringRolloverCommitted = false;
        }
        if (corneringDirectionSign != 0f) previousCorneringDirectionSign = corneringDirectionSign;
        float contactGrace = profile != null ? profile.RolloverGroundContactGraceDuration : 0.15f;
        bool hasRolloverContact = drivenWheelGrounded || Time.time - lastDrivenWheelContactTime <= contactGrace;
        float rawDemand = enabled && hasRolloverContact
            ? activeSpeed * activeSteering * corneringLoadRatio
            : 0f;
        bool maintainsTurnIntent = Mathf.Abs(throttleInput) > 0.01f &&
            Mathf.Abs(steeringInput) > (profile != null ? profile.SteeringInputDeadZone : 0.08f);
        if (!maintainsTurnIntent) corneringRolloverCommitted = false;

        if (rawDemand > 0f)
        {
            corneringDemand = rawDemand;
            lastPositiveCorneringDemand = rawDemand;
            lastPositiveCorneringDemandTime = Time.time;
        }
        else
        {
            float maneuverGrace = profile != null ? profile.RolloverManeuverGraceDuration : 0.2f;
            corneringDemand = maintainsTurnIntent && Time.time - lastPositiveCorneringDemandTime <= maneuverGrace
                ? lastPositiveCorneringDemand
                : 0f;
        }

        if (corneringRolloverCommitted && maintainsTurnIntent)
        {
            corneringRolloverRisk = 1f;
            corneringDemand = Mathf.Max(corneringDemand, lastPositiveCorneringDemand);
        }

        if (corneringDemand > 0f)
        {
            float duration = profile != null ? profile.CorneringRolloverDuration : 2f;
            corneringRolloverRisk = Mathf.MoveTowards(
                corneringRolloverRisk,
                1f,
                corneringDemand / Mathf.Max(0.1f, duration) * Time.fixedDeltaTime);
            if (corneringRolloverRisk >= 0.99f) corneringRolloverCommitted = true;
        }
        else
        {
            float recovery = profile != null ? profile.CorneringRolloverRecoveryRate : 1f;
            corneringRolloverRisk = Mathf.MoveTowards(corneringRolloverRisk, 0f, recovery * Time.fixedDeltaTime);
        }

        ApplyCorneringRollTorque(forwardSpeed, yawRate);
    }

    private void ApplyCorneringRollTorque(float forwardSpeed, float yawRate)
    {
        if (corneringRolloverRisk <= 0f || Mathf.Abs(yawRate) <= 0.001f)
        {
            targetCorneringRollAngle = 0f;
            return;
        }

        Vector3 groundForward = Vector3.ProjectOnPlane(transform.forward, filteredGroundNormal).normalized;
        if (groundForward.sqrMagnitude <= 0.0001f) return;
        Vector3 velocityDirection = Mathf.Sign(forwardSpeed) * groundForward;
        Vector3 inward = Vector3.Cross(filteredGroundNormal, velocityDirection) * Mathf.Sign(yawRate);
        Vector3 outward = -inward.normalized;
        float maximumRoll = profile != null ? profile.MaximumCorneringRollAngle : 70f;
        targetCorneringRollAngle = maximumRoll * corneringRolloverRisk;
        Vector3 desiredUp = Quaternion.AngleAxis(targetCorneringRollAngle, velocityDirection) * filteredGroundNormal;
        if (Vector3.Dot(desiredUp, outward) < 0f)
            desiredUp = Quaternion.AngleAxis(-targetCorneringRollAngle, velocityDirection) * filteredGroundNormal;

        Vector3 rollAxis = Vector3.Cross(transform.up, desiredUp);
        float rollSpeed = Vector3.Dot(physicsBody.angularVelocity, velocityDirection);
        float rollError = Vector3.Dot(rollAxis, velocityDirection);
        float acceleration = rollError *
            (profile != null ? profile.CorneringRollSpring : 22f) -
            rollSpeed * (profile != null ? profile.CorneringRollDamping : 5f);
        float committedWeight = Mathf.InverseLerp(0.85f, 1f, corneringRolloverRisk);
        float committedAcceleration = committedWeight *
            (profile != null ? profile.MinimumCommittedRolloverAcceleration : 28f);
        if (Mathf.Abs(rollError) > 0.001f && Mathf.Abs(acceleration) < committedAcceleration)
            acceleration = Mathf.Sign(rollError) * committedAcceleration;
        float maximumAcceleration = profile != null ? profile.MaximumCorneringRollAcceleration : 28f;
        physicsBody.AddTorque(velocityDirection * Mathf.Clamp(acceleration, -maximumAcceleration, maximumAcceleration),
            ForceMode.Acceleration);
    }

    private float GetCorneringLoadRatio()
    {
        int resourceCount = CargoCount;
#if UNITY_EDITOR
        if (editorProbeResourceCount >= 0) resourceCount = editorProbeResourceCount;
#endif
        float resourceFactor = resourceCount switch
        {
            <= 0 => 0f,
            1 => profile != null ? profile.OneResourceRolloverLoadFactor : 0.25f,
            2 => profile != null ? profile.TwoResourcesRolloverLoadFactor : 0.5f,
            _ => profile != null ? profile.ThreeResourcesRolloverLoadFactor : 0.8f
        };
        float concreteFactor = ConcreteLoads > 0
            ? (profile != null ? profile.ConcreteRolloverLoadFactor : 1f)
            : 0f;
        float passengerFactor = PassengerClientId != NoClient
            ? (profile != null ? profile.PassengerRolloverLoadFactor : 1f)
            : 0f;
        corneringLoadSource = ConcreteLoads > 0
            ? "Concrete"
            : resourceCount > 0
                ? $"Resources:{resourceCount}"
                : PassengerClientId != NoClient ? "Passenger" : "Empty";
        return Mathf.Clamp01(resourceFactor + concreteFactor + passengerFactor);
    }

    private void ApplyTippedDamping()
    {
        float damping = profile != null ? profile.TippedAngularDamping : 3f;
        Vector3 torque = -physicsBody.angularVelocity * damping;
        Vector3 currentUp = transform.up;
        Vector3 correctionAxis = Vector3.Cross(currentUp, tippedRestUp);
        float correctionAngle = Vector3.Angle(currentUp, tippedRestUp) * Mathf.Deg2Rad;
        if (correctionAxis.sqrMagnitude > 0.0001f && correctionAngle > 0.001f)
        {
            torque += correctionAxis.normalized * correctionAngle *
                (profile != null ? profile.TippedRecoveryTorque : 12f);
        }
        physicsBody.AddTorque(torque, ForceMode.Acceleration);
    }

    private void ApplyIdleBrake()
    {
        bool shouldSettle = State == WheelbarrowState.Free &&
            Speed <= (profile != null ? profile.IdleDampingMaximumSpeed : 0.25f);
        if (shouldSettle)
        {
            physicsBody.AddTorque(
                -physicsBody.angularVelocity * (profile != null ? profile.IdleAngularDamping : 4f),
                ForceMode.Acceleration);
        }
    }

    private void SetRestingSupportsEnabled(bool enabled)
    {
        if (restingSupportColliders == null) return;
        foreach (Collider support in restingSupportColliders)
        {
            if (support != null && support.enabled != enabled) support.enabled = enabled;
        }
    }

    private void UpdateNavigationObstacle(bool force = false)
    {
        if (navigationObstacle == null) return;

        if (!HasAuthority)
        {
            if (navigationObstacle.enabled) navigationObstacle.enabled = false;
            navObstacleSettledTime = 0f;
            return;
        }

        WheelbarrowState state = State;
        bool secured = state == WheelbarrowState.Docked || state == WheelbarrowState.Pouring;
        bool canBecomeStationaryObstacle = state == WheelbarrowState.Free || state == WheelbarrowState.Tipped;
        bool shouldEnable = secured || canBecomeStationaryObstacle;

        bool isSettled = physicsBody != null &&
            physicsBody.linearVelocity.magnitude <= (profile != null ? profile.NavObstacleLinearSpeedThreshold : 0.15f) &&
            physicsBody.angularVelocity.magnitude * Mathf.Rad2Deg <=
                (profile != null ? profile.NavObstacleAngularSpeedThresholdDegrees : 10f);
        if (secured)
        {
            navObstacleSettledTime = profile != null ? profile.NavObstacleSettleDuration : 0.5f;
        }
        else if (canBecomeStationaryObstacle && isSettled)
        {
            navObstacleSettledTime += Time.fixedDeltaTime;
        }
        else
        {
            navObstacleSettledTime = 0f;
        }

        float settleDuration = profile != null ? profile.NavObstacleSettleDuration : 0.5f;
        bool shouldCarve = secured ||
            (canBecomeStationaryObstacle && navObstacleSettledTime >= settleDuration);
        if (force || navigationObstacle.carving != shouldCarve)
            navigationObstacle.carving = shouldCarve;
        if (force || navigationObstacle.enabled != shouldEnable)
            navigationObstacle.enabled = shouldEnable;
    }

    private void ConfigureWheelContactMode()
    {
        bool isDriven = State == WheelbarrowState.Driven;
        bool canUseParkingContacts = State != WheelbarrowState.Driven &&
            State != WheelbarrowState.Tipped && State != WheelbarrowState.Righting && tippingElapsed <= 0f;
        if (drivenWheelCollider != null && drivenWheelCollider.enabled)
        {
            ResetDrivenWheel();
            drivenWheelCollider.enabled = false;
        }

        SetRestingSupportsEnabled(canUseParkingContacts);
        if (wheelContactCollider != null && wheelContactCollider.enabled != canUseParkingContacts)
            wheelContactCollider.enabled = canUseParkingContacts;
        if (rightingInteractionCollider != null)
            rightingInteractionCollider.enabled = State == WheelbarrowState.Tipped;
    }

    private void ConfigureDrivenWheelPhysics(bool force = false)
    {
        if (drivenWheelCollider == null || physicsBody == null) return;

        if (!force && lastConfiguredWheelProfile == profile &&
            Mathf.Abs(lastConfiguredWheelMass - physicsBody.mass) <= 0.01f) return;

        float radius = profile != null ? profile.WheelRadius : wheelVisualRadius;
        float suspensionDistance = profile != null ? profile.WheelSuspensionDistance : 0.03f;
        float frequency = profile != null ? profile.WheelSuspensionFrequency : 7f;
        float dampingRatio = profile != null ? profile.WheelSuspensionDampingRatio : 1f;
        float targetPosition = profile != null ? profile.WheelSuspensionTargetPosition : 0.5f;
        float angularFrequency = 2f * Mathf.PI * frequency;

        Vector3 parkedWheelCenter = wheelVisual != null
            ? transform.InverseTransformPoint(wheelVisual.position)
            : wheelVisualRootLocalPosition;
        drivenWheelCollider.center = parkedWheelCenter + Vector3.up * suspensionDistance * (1f - targetPosition);
        drivenWheelCollider.radius = radius;
        drivenWheelCollider.suspensionDistance = suspensionDistance;
        JointSpring spring = drivenWheelCollider.suspensionSpring;
        float supportedWheelMass = Mathf.Max(1f, physicsBody.mass * wheelLoadShare);
        spring.spring = supportedWheelMass * angularFrequency * angularFrequency;
        spring.damper = 2f * dampingRatio * supportedWheelMass * angularFrequency;
        spring.targetPosition = targetPosition;
        drivenWheelCollider.suspensionSpring = spring;

        WheelFrictionCurve forward = drivenWheelCollider.forwardFriction;
        forward.stiffness = profile != null ? profile.WheelForwardFrictionStiffness : 1.4f;
        drivenWheelCollider.forwardFriction = forward;
        WheelFrictionCurve sideways = drivenWheelCollider.sidewaysFriction;
        sideways.stiffness = profile != null ? profile.WheelSidewaysFrictionStiffness : 1.8f;
        drivenWheelCollider.sidewaysFriction = sideways;
        lastConfiguredWheelMass = physicsBody.mass;
        lastConfiguredWheelProfile = profile;
    }

    private void InvalidateDrivenWheelPhysics()
    {
        lastConfiguredWheelMass = float.NaN;
        lastConfiguredWheelProfile = null;
    }

    private void ResetDrivenWheel()
    {
        if (drivenWheelCollider == null) return;
        drivenWheelCollider.motorTorque = 0f;
        drivenWheelCollider.brakeTorque = 0f;
        drivenWheelCollider.steerAngle = 0f;
    }

    private void ResetDriveInput()
    {
        throttleInput = 0f;
        steeringInput = 0f;
        smoothedSteeringInput = 0f;
        currentSteeringAngle = 0f;
        corneringRolloverRisk = 0f;
        corneringRolloverCommitted = false;
        corneringLateralAcceleration = 0f;
        corneringLoadRatio = 0f;
        corneringSpeedRatio = 0f;
        effectiveRolloverReferenceSpeed = 0f;
        targetCorneringRollAngle = 0f;
        previousCorneringDirectionSign = 0f;
        currentYawAcceleration = 0f;
        targetYawRate = 0f;
        residualLateralSpeed = 0f;
        drivenWheelGrounded = false;
        drivenContactInitialized = false;
        wheelSuspensionError = 0f;
        wheelSupportAcceleration = 0f;
        driverSupportAcceleration = 0f;
        longitudinalAcceleration = 0f;
        lastInputTime = Time.time;
        ResetDrivenWheel();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (profile == null || !profile.EnableDiagnostics || !IsDocked || collision == null) return;
        NavMeshAgent agent = collision.collider != null
            ? collision.collider.GetComponentInParent<NavMeshAgent>()
            : null;
        if (agent == null) return;
        Debug.Log(
            $"Wheelbarrow NPC contact: state={State}, secured={IsDockSecured}, " +
            $"kinematic={physicsBody != null && physicsBody.isKinematic}, " +
            $"obstacleEnabled={navigationObstacle != null && navigationObstacle.enabled}, " +
            $"carving={navigationObstacle != null && navigationObstacle.carving}",
            this);
    }

    private void DetectTipping()
    {
        if (State == WheelbarrowState.Tipped || State == WheelbarrowState.Righting || IsDocked) return;
        float angle = Vector3.Angle(transform.up, Vector3.up);
        if (angle < (profile != null ? profile.TippingAngle : 60f))
        {
            tippingElapsed = 0f;
            return;
        }
        tippingElapsed += Time.fixedDeltaTime;
        if (tippingElapsed >= (profile != null ? profile.TippingDuration : 0.25f)) TipOver();
    }

    private void TipOver()
    {
        ulong previousDriver = DriverClientId;
        tippedRestUp = ResolveTippedRestUp();
        SetState(WheelbarrowState.Tipped);
        if (previousDriver != NoClient) BeginSafeExit(previousDriver);
        SetDriver(NoClient);
        ReleasePassenger(true);
        SpillAllCargo();
        SpillConcrete();
        activeDock?.ForceReleaseWheelbarrow(this);
        activeDock = null;
    }

    private Vector3 ResolveTippedRestUp()
    {
        float targetTilt = profile != null ? profile.TippedTargetRestAngle : 72f;
        Vector3 leanDirection = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        if (leanDirection.sqrMagnitude <= 0.0001f)
        {
            Vector3 rollAxis = Vector3.ProjectOnPlane(physicsBody.angularVelocity, Vector3.up);
            leanDirection = rollAxis.sqrMagnitude > 0.0001f
                ? Vector3.Cross(rollAxis.normalized, Vector3.up)
                : transform.right;
        }
        leanDirection.Normalize();
        float radians = targetTilt * Mathf.Deg2Rad;
        return (Vector3.up * Mathf.Cos(radians) + leanDirection * Mathf.Sin(radians)).normalized;
    }

    public float TippedRestTilt => Vector3.Angle(tippedRestUp, Vector3.up);

    public void TryAutomaticBoarding(Collider other)
    {
        if (!HasAuthority || PassengerClientId != NoClient || Speed < (profile != null ? profile.AutomaticBoardingMinimumSpeed : 1.5f) || other == null) return;
        NetworkObject player = other.GetComponentInParent<NetworkObject>();
        if (player == null || player.GetComponent<PlayerInteractionNew>() == null) return;
        Vector3 toPlayer = (player.transform.position - transform.position).normalized;
        if (Vector3.Dot(transform.forward, toPlayer) < (profile != null ? profile.AutomaticBoardingDirectionDot : 0.65f)) return;
        AssignRole(player.OwnerClientId, WheelbarrowOccupantRole.Passenger);
    }

    public void SubmitDriveInput(float throttle, float steering, ulong senderClientId)
    {
        if (!HasAuthority || DriverClientId != senderClientId || State != WheelbarrowState.Driven) return;
        if ((profile == null || profile.EnableDrivingStaminaDrain) &&
            TryGetPlayer(senderClientId, out NetworkObject player) &&
            player.TryGetComponent(out PlayerStaminaController stamina) && stamina.CurrentStamina <= 0f)
            throttle = 0f;
        throttleInput = Mathf.Clamp(throttle, -1f, 1f);
        steeringInput = Mathf.Abs(throttleInput) > 0.01f ? Mathf.Clamp(steering, -1f, 1f) : 0f;
        lastReceivedThrottleInput = throttleInput;
        lastReceivedSteeringInput = steeringInput;
        lastInputTime = Time.time;
        if (Mathf.Abs(throttleInput) > 0.01f) physicsBody?.WakeUp();
    }

    public void SetPlayerCollisionIgnored(Transform playerRoot, bool ignored)
    {
        if (playerRoot == null) return;
        Collider[] playerColliders = playerRoot.GetComponentsInChildren<Collider>(true)
            .Where(item => item != null && !item.isTrigger)
            .ToArray();
        if (playerColliders.Length == 0) return;

        if (physicalColliders == null || physicalColliders.Length == 0)
        {
            physicalColliders = GetComponentsInChildren<Collider>(true)
                .Where(item => item != null && !item.isTrigger)
                .ToArray();
        }

        foreach (Collider item in physicalColliders)
        {
            if (item == null) continue;
            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider != item)
                    Physics.IgnoreCollision(playerCollider, item, ignored);
            }
        }
    }

    private void SetOccupantCollisionIgnored(ulong clientId, bool ignored, bool broadcast = true)
    {
        if (clientId == NoClient) return;
        if (ignored) collisionIgnoredPlayers.Add(clientId);
        else collisionIgnoredPlayers.Remove(clientId);

        if (TryFindPlayerOnPeer(clientId, out NetworkObject player))
        {
            SetPlayerCollisionIgnored(player.transform, ignored);
            if (!ignored) player.GetComponent<PlayerWheelbarrowController>()?.CompleteSafeExit(this);
        }

        if (broadcast && IsSessionActive && IsServer)
            SetOccupantCollisionIgnoredClientRpc(clientId, ignored);
    }

    [ClientRpc]
    private void SetOccupantCollisionIgnoredClientRpc(ulong clientId, bool ignored)
    {
        if (IsServer) return;
        SetOccupantCollisionIgnored(clientId, ignored, false);
    }

    private void RestoreAllPlayerCollisions(bool broadcast)
    {
        foreach (ulong clientId in collisionIgnoredPlayers.ToArray())
            SetOccupantCollisionIgnored(clientId, false, broadcast);
        pendingSafeExits.Clear();
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitDriveInputServerRpc(float throttle, float steering, ServerRpcParams rpc = default) =>
        SubmitDriveInput(throttle, steering, rpc.Receive.SenderClientId);

    public bool RequestEnterDriver(Transform interactor)
    {
        return RequestRole(interactor, WheelbarrowOccupantRole.Driver);
    }

#if UNITY_EDITOR
    private int editorProbeResourceCount = -1;

    internal void BeginEditorPhysicsProbe(bool loaded)
    {
        editorProbeResourceCount = -1;
        SetConcreteLoads(loaded ? 1 : 0);
        SetDriver(0);
        SetState(WheelbarrowState.Driven);
        physicsBody.isKinematic = false;
        physicsBody.useGravity = true;
        physicsBody.WakeUp();
    }

    internal void BeginEditorPhysicsProbe(int resourceCount)
    {
        editorProbeResourceCount = Mathf.Clamp(resourceCount, 0, 3);
        SetConcreteLoads(0);
        SetDriver(0);
        SetState(WheelbarrowState.Driven);
        physicsBody.isKinematic = false;
        physicsBody.useGravity = true;
        physicsBody.WakeUp();
    }
#endif

    public bool RequestEnterPassenger(Transform interactor)
    {
        return RequestRole(interactor, WheelbarrowOccupantRole.Passenger);
    }

    private bool RequestRole(Transform interactor, WheelbarrowOccupantRole role)
    {
        if (interactor == null || !interactor.TryGetComponent(out NetworkObject playerObject)) return false;
        if (!IsSessionActive) return AssignRole(playerObject.OwnerClientId, role);
        if (IsServer) return AssignRole(playerObject.OwnerClientId, role);
        RequestRoleServerRpc((byte)role);
        return true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestRoleServerRpc(byte role, ServerRpcParams rpc = default) => AssignRole(rpc.Receive.SenderClientId, (WheelbarrowOccupantRole)role);

    private bool AssignRole(ulong clientId, WheelbarrowOccupantRole role)
    {
        if (!HasAuthority || !TryGetPlayer(clientId, out NetworkObject player) ||
            player.GetComponent<PlayerHealth>() is PlayerHealth health && health.IsDowned && role == WheelbarrowOccupantRole.Driver) return false;

        Transform requestedAnchor = role == WheelbarrowOccupantRole.Driver ? driverAnchor : passengerAnchor;
        if (requestedAnchor != null && Vector3.Distance(player.transform.position, requestedAnchor.position) > 3f) return false;

        if (role == WheelbarrowOccupantRole.Driver)
        {
            if (DriverClientId != NoClient || State == WheelbarrowState.Pouring || State == WheelbarrowState.Righting || State == WheelbarrowState.Tipped) return false;
            pendingSafeExits.Remove(clientId);
            SetOccupantCollisionIgnored(clientId, true);
            SetDriver(clientId);

            if (IsDockSecured && !ReleaseDockForDriver(clientId))
            {
                SetDriver(NoClient);
                SetOccupantCollisionIgnored(clientId, false);
                return false;
            }

            SetState(WheelbarrowState.Driven);
            return true;
        }
        if (role == WheelbarrowOccupantRole.Passenger)
        {
            if (PassengerClientId != NoClient) return false;
            pendingSafeExits.Remove(clientId);
            SetOccupantCollisionIgnored(clientId, true);
            SetPassenger(clientId);
            return true;
        }
        return false;
    }

    public bool RequestExit(ulong clientId)
    {
        if (!HasAuthority || Speed > (profile != null ? profile.MaximumExitSpeed : 0.8f)) return false;
        if (DriverClientId == clientId) return RequestDriverExitAndDock(clientId);
        if (PassengerClientId == clientId)
        {
            ReleasePassenger(false);
            return true;
        }
        return false;
    }

    public bool RequestDriverExitAndDock(ulong clientId)
    {
        if (!HasAuthority || DriverClientId != clientId || State != WheelbarrowState.Driven) return false;

        WheelbarrowDockingStation requestedDock = FindReadyDockForDriverExit();
        if (requestedDock != null && requestedDock.TryDockImmediately(this, clientId)) return true;

        SetDriver(NoClient);
        SetState(WheelbarrowState.Free);
        BeginSafeExit(clientId);
        return true;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestExitServerRpc(ServerRpcParams rpc = default) => RequestExit(rpc.Receive.SenderClientId);

    public Transform GetRoleAnchor(ulong clientId)
    {
        if (DriverClientId == clientId) return driverAnchor;
        if (PassengerClientId == clientId) return passengerAnchor;
        return null;
    }

    public WheelbarrowOccupantRole GetRole(ulong clientId)
    {
        if (DriverClientId == clientId) return WheelbarrowOccupantRole.Driver;
        if (PassengerClientId == clientId) return WheelbarrowOccupantRole.Passenger;
        return WheelbarrowOccupantRole.None;
    }

    public float GetDrivingStaminaDrain()
    {
        if (profile != null && !profile.EnableDrivingStaminaDrain) return 0f;
        if (DriverClientId == NoClient || State != WheelbarrowState.Driven || Mathf.Abs(throttleInput) < 0.01f) return 0f;
        float loadRatio = Mathf.Clamp01((physicsBody.mass - (profile != null ? profile.BaseMass : 22f)) / 140f);
        float uphill = Mathf.Clamp01(Vector3.Dot(transform.forward * Mathf.Sign(throttleInput), Vector3.up));
        float rate = (profile != null ? profile.BaseStaminaDrain : 0.25f) +
            loadRatio * (profile != null ? profile.LoadedStaminaDrain : 1.25f) +
            uphill * (profile != null ? profile.UphillStaminaDrain : 1.5f);
        return Mathf.Min(profile != null ? profile.MaximumStaminaDrain : 3f, rate);
    }

    public float GetEstimatedDrivingStaminaDrain(float throttle)
    {
        if (profile != null && !profile.EnableDrivingStaminaDrain) return 0f;
        if (Mathf.Abs(throttle) < 0.01f) return 0f;
        float cargoMass = GetResourceCargoMass() + ConcreteLoads * (profile != null ? profile.ConcreteBatchMass : 80f) +
            (PassengerClientId != NoClient ? profile != null ? profile.PassengerMass : 75f : 0f);
        float loadRatio = Mathf.Clamp01(cargoMass / 140f);
        float uphill = Mathf.Clamp01(Vector3.Dot(transform.forward * Mathf.Sign(throttle), Vector3.up));
        return Mathf.Min(profile != null ? profile.MaximumStaminaDrain : 3f,
            (profile != null ? profile.BaseStaminaDrain : 0.25f) +
            loadRatio * (profile != null ? profile.LoadedStaminaDrain : 1.25f) +
            uphill * (profile != null ? profile.UphillStaminaDrain : 1.5f));
    }

    public bool TryReceiveConcreteBatch(ConcreteMixerController source)
    {
        if (!HasAuthority || !CanReceiveConcreteBatch || source == null) return false;
        SetConcreteLoads(ConcreteLoads + 1);
        RefreshMassAndCenterOfMass();
        if (concreteCargoVisual != null) concreteCargoVisual.SetActive(true);
        return true;
    }

    public bool TryLoadResource(PlayerInteractionNew player, BaseResourceNew resource)
    {
        if (player == null || resource == null || !resource.CanBeCarried || resource.GetMinAmountOfPlayersNeeded() > 1 || HasConcrete) return false;
        if (!player.TryGetComponent(out NetworkObject playerObject) || !resource.TryGetComponent(out NetworkObject resourceObject)) return false;
        if (!IsSessionActive)
        {
            if (!CanLoadResource(resource)) return false;
            if (!resource.TrySecureForTransport()) return false;
            localCargo.Add(resource);
            InvalidateDrivenWheelPhysics();
            UpdateCargoTransforms();
            return true;
        }
        if (IsServer) return LoadResourceServer(playerObject.OwnerClientId, resourceObject.NetworkObjectId);
        RequestLoadResourceServerRpc(resourceObject.NetworkObjectId);
        return true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestLoadResourceServerRpc(ulong resourceId, ServerRpcParams rpc = default) => LoadResourceServer(rpc.Receive.SenderClientId, resourceId);

    private bool LoadResourceServer(ulong clientId, ulong resourceId)
    {
        if (!HasAuthority || CargoCount >= (profile != null ? profile.ResourceSlots : 3) || HasConcrete ||
            !TryGetNetworkObject(resourceId, out NetworkObject no) || !no.TryGetComponent(out BaseResourceNew resource) ||
            !resource.IsHeldBy(clientId) || resource.GetMinAmountOfPlayersNeeded() > 1) return false;
        BaseResourceSO type = resource.GetBaseResourceSO();
        foreach (BaseResourceNew loaded in GetCargoResources()) if (loaded != null && loaded.GetBaseResourceSO() != type) return false;
        float mass = no.TryGetComponent(out Rigidbody body) ? body.mass : 1f;
        if (GetResourceCargoMass() + mass > (profile != null ? profile.MaximumResourceCargoMass : 60f)) return false;
        if (!resource.TrySecureForTransport()) return false;
        if (IsSessionActive) cargoNetwork.Add(resourceId); else localCargo.Add(resource);
        InvalidateDrivenWheelPhysics();
        RefreshCargoReferences();
        return true;
    }

    private bool CanLoadResource(BaseResourceNew resource)
    {
        if (resource == null || CargoCount >= (profile != null ? profile.ResourceSlots : 3) || HasConcrete ||
            resource.GetMinAmountOfPlayersNeeded() > 1) return false;
        foreach (BaseResourceNew loaded in GetCargoResources())
            if (loaded != null && loaded.GetBaseResourceSO() != resource.GetBaseResourceSO()) return false;
        float mass = resource.TryGetComponent(out Rigidbody body) ? body.mass : 1f;
        return GetResourceCargoMass() + mass <= (profile != null ? profile.MaximumResourceCargoMass : 60f);
    }

    public bool TryUnloadLastResource(PlayerInteractionNew player)
    {
        if (player == null || CargoCount == 0 || player.HasPickedUpObject || !player.TryGetComponent(out NetworkObject playerObject)) return false;
        if (!IsSessionActive) return UnloadLastResourceServer(playerObject.OwnerClientId);
        if (IsServer) return UnloadLastResourceServer(playerObject.OwnerClientId);
        RequestUnloadResourceServerRpc();
        return true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestUnloadResourceServerRpc(ServerRpcParams rpc = default) => UnloadLastResourceServer(rpc.Receive.SenderClientId);

    private bool UnloadLastResourceServer(ulong clientId)
    {
        List<BaseResourceNew> cargo = GetCargoResources();
        if (cargo.Count == 0 || !TryGetPlayer(clientId, out NetworkObject player)) return false;
        BaseResourceNew resource = cargo[cargo.Count - 1];
        RemoveCargoReference(resource);
        resource.ReleaseFromTransport(player.transform.position + player.transform.forward, Quaternion.identity);
        resource.TryGiveFromTransportTo(player.GetComponent<PlayerInteractionNew>());
        return true;
    }

    internal bool DockImmediately(WheelbarrowDockingStation station, Transform targetPose, ulong clientId)
    {
        if (!HasAuthority || station == null || targetPose == null || DriverClientId != clientId ||
            State != WheelbarrowState.Driven || physicsBody == null) return false;

        SetDriver(NoClient);
        BeginSafeExit(clientId);
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        physicsBody.position = targetPose.position;
        physicsBody.rotation = targetPose.rotation;
        transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
        activeDock = station;
        if (IsSessionActive) dockNetwork.Value = station.NetworkObjectId;
        securedDockPosition = targetPose.position;
        securedDockRotation = targetPose.rotation;
        hasSecuredDockPose = true;
        SetDockSecured(true);
        SetState(WheelbarrowState.Docked);
        return true;
    }

    private WheelbarrowDockingStation FindReadyDockForDriverExit()
    {
        WheelbarrowDockingStation best = null;
        float bestDistanceSquared = float.PositiveInfinity;
        foreach (WheelbarrowDockingStation station in FindObjectsByType<WheelbarrowDockingStation>(FindObjectsSortMode.None))
        {
            if (station == null || !station.EvaluateDriverDockingReadiness(this)) continue;
            Vector3 target = station.TargetPose != null ? station.TargetPose.position : station.transform.position;
            float distanceSquared = (target - transform.position).sqrMagnitude;
            if (distanceSquared >= bestDistanceSquared) continue;
            best = station;
            bestDistanceSquared = distanceSquared;
        }
        return best;
    }

    internal bool ReleaseDockForDriver(ulong clientId)
    {
        if (!HasAuthority || !IsDockSecured || activeDock == null || DriverClientId != clientId ||
            State == WheelbarrowState.Pouring) return false;

        WheelbarrowDockingStation station = activeDock;
        if (!station.ReleaseWheelbarrowForDriver(this, clientId)) return false;

        activeDock = null;
        if (IsSessionActive) dockNetwork.Value = NoClient;
        hasSecuredDockPose = false;
        SetDockSecured(false);
        return true;
    }

    internal void ForceReleaseDock(WheelbarrowDockingStation station)
    {
        if (!HasAuthority || activeDock != station) return;
        activeDock = null;
        if (IsSessionActive) dockNetwork.Value = NoClient;
        hasSecuredDockPose = false;
        SetDockSecured(false);
        if (State == WheelbarrowState.Docked || State == WheelbarrowState.Pouring)
            SetState(WheelbarrowState.Free);
    }

    internal void SetDockSecured(bool secured)
    {
        if (!HasAuthority || physicsBody == null) return;
        if (secured && !physicsBody.isKinematic)
        {
            physicsBody.linearVelocity = Vector3.zero;
            physicsBody.angularVelocity = Vector3.zero;
        }
        physicsBody.isKinematic = secured;
        physicsBody.useGravity = !secured;
    }

    internal void RestoreSecuredDockPose()
    {
        if (!HasAuthority || !IsDockSecured || !hasSecuredDockPose || physicsBody == null) return;
        physicsBody.position = securedDockPosition;
        physicsBody.rotation = securedDockRotation;
        if (!physicsBody.isKinematic)
        {
            physicsBody.linearVelocity = Vector3.zero;
            physicsBody.angularVelocity = Vector3.zero;
        }
    }

    public void SetPouringState(bool pouring)
    {
        if (!HasAuthority) return;
        SetState(pouring ? WheelbarrowState.Pouring : WheelbarrowState.Docked);
    }

    public bool ConsumeConcreteLoad()
    {
        if (!HasAuthority || ConcreteLoads <= 0) return false;
        SetConcreteLoads(ConcreteLoads - 1);
        return true;
    }

    public void SpillConcrete()
    {
        if (!HasAuthority || ConcreteLoads <= 0) return;
        SetConcreteLoads(0);
        SetSpillSequence(localSpillSequence + 1);
    }

    public bool TryRemoveDownedPassenger(PlayerInteractionNew rescuer)
    {
        if (!HasAuthority || rescuer == null || PassengerClientId == NoClient || Speed > (profile != null ? profile.MaximumExitSpeed : 0.8f) ||
            !TryGetPlayer(PassengerClientId, out NetworkObject passenger)) return false;
        PlayerHealth health = passenger.GetComponent<PlayerHealth>();
        if (health == null || !health.IsDowned) return false;
        ReleasePassenger(false);
        return true;
    }

    public bool RequestRemoveDownedPassenger(PlayerInteractionNew rescuer)
    {
        if (rescuer == null || !IsPassengerDowned()) return false;
        if (!IsSessionActive) return TryRemoveDownedPassenger(rescuer);
        if (IsServer) return TryRemoveDownedPassenger(rescuer);
        RequestRemoveDownedPassengerServerRpc();
        return true;
    }

    private bool IsPassengerDowned()
    {
        return PassengerClientId != NoClient && TryGetPlayer(PassengerClientId, out NetworkObject passenger) &&
            passenger.TryGetComponent(out PlayerHealth health) && health.IsDowned;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestRemoveDownedPassengerServerRpc(ServerRpcParams rpc = default)
    {
        if (TryGetPlayer(rpc.Receive.SenderClientId, out NetworkObject player))
            TryRemoveDownedPassenger(player.GetComponent<PlayerInteractionNew>());
    }

    public void RequestBeginRighting(Transform interactor)
    {
        if (interactor == null || !interactor.TryGetComponent(out NetworkObject player)) return;
        if (!IsSessionActive) BeginRighting(player.OwnerClientId);
        else if (IsServer) BeginRighting(player.OwnerClientId);
        else BeginRightingServerRpc();
    }

    [ServerRpc(RequireOwnership = false)] private void BeginRightingServerRpc(ServerRpcParams rpc = default) => BeginRighting(rpc.Receive.SenderClientId);
    [ServerRpc(RequireOwnership = false)] public void CancelRightingServerRpc(ServerRpcParams rpc = default) { if (rightingClient == rpc.Receive.SenderClientId) CancelRighting(); }

    private void BeginRighting(ulong clientId)
    {
        float maximumSpeed = profile != null ? profile.MaximumRightingLinearSpeed : 0.5f;
        if (State != WheelbarrowState.Tipped || rightingClient != NoClient || Speed > maximumSpeed ||
            !TryGetPlayer(clientId, out NetworkObject player) ||
            Vector3.Distance(player.transform.position, transform.position) > 3f) return;
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health != null && health.IsDowned) return;

        rightingClient = clientId;
        rightingStartedAt = Time.time;
        rightingPlacementStarted = false;
        rightingStartPosition = physicsBody.position;
        rightingStartRotation = physicsBody.rotation;
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        physicsBody.isKinematic = true;
        physicsBody.useGravity = false;
        SetState(WheelbarrowState.Righting);
    }

    private void SimulateRighting()
    {
        if (!IsRightingOperatorValid())
        {
            CancelRighting();
            return;
        }

        float holdDuration = profile != null ? profile.RightingHoldDuration : 1.5f;
        if (!rightingPlacementStarted)
        {
            if (Time.time - rightingStartedAt < holdDuration) return;
            if (!TryResolveRightingTarget(out rightingTargetPosition, out rightingTargetRotation))
            {
                CancelRighting();
                return;
            }
            rightingPlacementStarted = true;
            rightingPlacementStartedAt = Time.time;
        }

        float duration = profile != null ? profile.RightingPlacementDuration : 0.4f;
        float normalized = Mathf.Clamp01((Time.time - rightingPlacementStartedAt) / Mathf.Max(0.01f, duration));
        float eased = Mathf.SmoothStep(0f, 1f, normalized);
        float lift = Mathf.Sin(eased * Mathf.PI) * (profile != null ? profile.RightingLiftClearance : 0.25f);
        Vector3 position = Vector3.Lerp(rightingStartPosition, rightingTargetPosition, eased) + Vector3.up * lift;
        Quaternion rotation = Quaternion.Slerp(rightingStartRotation, rightingTargetRotation, eased);
        physicsBody.position = position;
        physicsBody.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        if (normalized < 1f) return;

        physicsBody.position = rightingTargetPosition;
        physicsBody.rotation = rightingTargetRotation;
        transform.SetPositionAndRotation(rightingTargetPosition, rightingTargetRotation);
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        rightingClient = NoClient;
        rightingStartedAt = -1f;
        rightingPlacementStarted = false;
        SetState(WheelbarrowState.Free);
        physicsBody.isKinematic = false;
        physicsBody.useGravity = true;
    }

    public void CancelRighting()
    {
        if (!HasAuthority || State != WheelbarrowState.Righting) return;
        physicsBody.position = rightingStartPosition;
        physicsBody.rotation = rightingStartRotation;
        transform.SetPositionAndRotation(rightingStartPosition, rightingStartRotation);
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        physicsBody.isKinematic = false;
        physicsBody.useGravity = true;
        rightingClient = NoClient;
        rightingStartedAt = -1f;
        rightingPlacementStarted = false;
        SetState(WheelbarrowState.Tipped);
    }

    private bool IsRightingOperatorValid()
    {
        if (rightingClient == NoClient || !TryGetPlayer(rightingClient, out NetworkObject player) ||
            Vector3.Distance(player.transform.position, transform.position) > 3f) return false;
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        return health == null || !health.IsDowned;
    }

    private bool TryResolveRightingTarget(out Vector3 targetPosition, out Quaternion targetRotation)
    {
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude <= 0.001f) forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        if (forward.sqrMagnitude <= 0.001f) forward = Vector3.forward;
        targetRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        float radius = profile != null ? profile.RightingPlacementSearchRadius : 0.75f;
        Vector3[] directions =
        {
            Vector3.zero, Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
            (Vector3.right + Vector3.forward).normalized, (Vector3.left + Vector3.forward).normalized,
            (Vector3.right + Vector3.back).normalized, (Vector3.left + Vector3.back).normalized
        };
        float[] distances = radius > 0.01f ? new[] { 0f, radius * 0.5f, radius } : new[] { 0f };
        foreach (float distance in distances)
        {
            foreach (Vector3 direction in directions)
            {
                if (distance <= 0f && direction != Vector3.zero) continue;
                Vector3 sample = rightingStartPosition + direction * distance;
                if (!TryGroundRightingPosition(sample, out Vector3 grounded) ||
                    !IsRightingPoseFree(grounded, targetRotation)) continue;
                targetPosition = grounded;
                return true;
            }
        }

        targetPosition = rightingStartPosition;
        return false;
    }

    private bool TryGroundRightingPosition(Vector3 sample, out Vector3 grounded)
    {
        grounded = sample;
        RaycastHit[] hits = Physics.RaycastAll(sample + Vector3.up * 2f, Vector3.down, 4f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.normal.y < 0.5f ||
                hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)) continue;
            grounded.y = hit.point.y;
            return true;
        }
        return false;
    }

    private bool IsRightingPoseFree(Vector3 position, Quaternion rotation)
    {
        Vector3 center = position + rotation * new Vector3(0f, 0.8f, -0.1f);
        Vector3 halfExtents = new Vector3(0.72f, 0.68f, 1.3f);
        Collider[] overlaps = Physics.OverlapBox(center, halfExtents, rotation,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Transform operatorRoot = TryGetPlayer(rightingClient, out NetworkObject player) ? player.transform : null;
        return overlaps.All(item => item == null ||
            item.transform == transform || item.transform.IsChildOf(transform) ||
            operatorRoot != null && (item.transform == operatorRoot || item.transform.IsChildOf(operatorRoot)));
    }

    private void RefreshMassAndCenterOfMass()
    {
        float baseMass = profile != null ? profile.BaseMass : 22f;
        float cargoMass = GetResourceCargoMass() + ConcreteLoads * (profile != null ? profile.ConcreteBatchMass : 80f) +
            (PassengerClientId != NoClient ? profile != null ? profile.PassengerMass : 75f : 0f);
        physicsBody.mass = baseMass + cargoMass;
        Vector3 baseCenterOfMass = profile != null ? profile.BaseCenterOfMassLocal : new Vector3(0f, 0.45f, -0.15f);
        Vector3 cargoPoint = cargoRoot != null ? transform.InverseTransformPoint(cargoRoot.position) : Vector3.up * 0.4f;
        physicsBody.centerOfMass = (baseCenterOfMass * baseMass + cargoPoint * cargoMass) /
            Mathf.Max(1f, baseMass + cargoMass);
        RefreshSupportLoadDistribution();
    }

    private void RefreshSupportLoadDistribution()
    {
        Transform support = driverSupportPoint != null ? driverSupportPoint : driverAnchor;
        Vector3 wheelPoint = wheelVisual != null
            ? transform.InverseTransformPoint(wheelVisual.position)
            : wheelVisualRootLocalPosition;
        if (support == null)
        {
            driverSupportLoadShare = 0f;
            wheelLoadShare = 1f;
            return;
        }

        float supportZ = transform.InverseTransformPoint(support.position).z;
        float wheelZ = wheelPoint.z;
        float span = wheelZ - supportZ;
        driverSupportLoadShare = span > 0.01f
            ? Mathf.Clamp01((wheelZ - physicsBody.centerOfMass.z) / span)
            : 0f;
        wheelLoadShare = Mathf.Clamp01(1f - driverSupportLoadShare);
    }

    private float GetResourceCargoMass()
    {
        float mass = 0f;
        foreach (BaseResourceNew resource in GetCargoResources()) if (resource != null && resource.TryGetComponent(out Rigidbody body)) mass += body.mass;
        return mass;
    }

    private List<BaseResourceNew> GetCargoResources()
    {
        RefreshCargoReferences();
        return localCargo;
    }

    private void RefreshCargoReferences()
    {
        if (!IsSessionActive) return;
        localCargo.Clear();
        foreach (ulong id in cargoNetwork) if (TryGetNetworkObject(id, out NetworkObject no) && no.TryGetComponent(out BaseResourceNew resource)) localCargo.Add(resource);
    }

    private void OnCargoChanged(NetworkListEvent<ulong> changeEvent)
    {
        InvalidateDrivenWheelPhysics();
        RefreshCargoReferences();
        UpdateCargoTransforms();
    }

    private void UpdateCargoTransforms()
    {
        List<BaseResourceNew> cargo = GetCargoResources();
        for (int i = 0; i < cargo.Count; i++)
        {
            if (cargo[i] == null) continue;
            Transform slot = cargoSlots != null && i < cargoSlots.Length && cargoSlots[i] != null ? cargoSlots[i] : cargoRoot;
            if (slot != null) cargo[i].transform.SetPositionAndRotation(slot.position, slot.rotation);
        }
    }

    private void RemoveCargoReference(BaseResourceNew resource)
    {
        if (resource == null) return;
        if (IsSessionActive && resource.TryGetComponent(out NetworkObject no)) cargoNetwork.Remove(no.NetworkObjectId);
        else localCargo.Remove(resource);
        InvalidateDrivenWheelPhysics();
        RefreshCargoReferences();
    }

    private void SpillAllCargo()
    {
        if (!HasAuthority) return;
        foreach (BaseResourceNew resource in new List<BaseResourceNew>(GetCargoResources()))
        {
            if (resource == null) continue;
            RemoveCargoReference(resource);
            Vector3 offset = UnityEngine.Random.insideUnitSphere; offset.y = Mathf.Abs(offset.y) + 0.2f;
            resource.ReleaseFromTransport((cargoRoot != null ? cargoRoot.position : transform.position) + offset * 0.5f, UnityEngine.Random.rotation);
        }
    }

    private void ReleasePassenger(bool tipped)
    {
        ulong clientId = PassengerClientId;
        if (clientId == NoClient) return;
        BeginSafeExit(clientId);
        SetPassenger(NoClient);
    }

    private void ReleaseAllOccupants(bool force)
    {
        ulong driver = DriverClientId;
        ulong passenger = PassengerClientId;
        if (driver != NoClient) BeginSafeExit(driver);
        if (passenger != NoClient) BeginSafeExit(passenger);
        SetDriver(NoClient); SetPassenger(NoClient);
    }

    private void BeginSafeExit(ulong clientId)
    {
        if (!TryGetPlayer(clientId, out NetworkObject player)) return;
        SetOccupantCollisionIgnored(clientId, true);
        Vector3 candidate = ResolveSafeExitPosition(player);
        ApplySafeExitPlacement(player, candidate);
        pendingSafeExits[clientId] = new PendingSafeExit { StartedAt = Time.time };

        if (IsSessionActive)
            BeginSafeExitClientRpc(candidate, Target(clientId));
        else
            player.GetComponent<PlayerWheelbarrowController>()?.BeginSafeExit(this, candidate);
    }

    private Vector3 ResolveSafeExitPosition(NetworkObject player)
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        if (safeExitPoints != null)
        {
            foreach (Transform exit in safeExitPoints)
            {
                if (exit == null) continue;
                if (TryResolveGroundedCandidate(exit.position, controller, player.transform, out Vector3 candidate)) return candidate;
            }
        }

        float radius = profile != null ? profile.ExitSearchRadius : 1.8f;
        Vector3 center = driverAnchor != null ? driverAnchor.position : transform.position - transform.forward;
        float[] angles = { 0f, -30f, 30f, -60f, 60f, -90f, 90f, 180f };
        foreach (float angle in angles)
        {
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * -transform.forward;
            Vector3 sample = center + direction * radius;
            if (TryResolveGroundedCandidate(sample, controller, player.transform, out Vector3 candidate)) return candidate;
        }

        return center - transform.forward * radius;
    }

    private bool TryResolveGroundedCandidate(
        Vector3 sample,
        CharacterController controller,
        Transform playerRoot,
        out Vector3 grounded)
    {
        grounded = sample;
        if (controller == null) return true;

        float probe = profile != null ? profile.ExitGroundProbeDistance : 2f;
        RaycastHit[] hits = Physics.RaycastAll(
            sample + Vector3.up * probe,
            Vector3.down,
            probe * 2f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.normal.y < 0.5f ||
                hit.collider.transform == playerRoot || hit.collider.transform.IsChildOf(playerRoot) ||
                hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)) continue;

            float centerOffsetY = (controller.transform.rotation * controller.center).y;
            grounded.y = hit.point.y - centerOffsetY + controller.height * 0.5f + controller.skinWidth;
            return IsCapsuleFree(grounded, controller, playerRoot);
        }
        return false;
    }

    private bool IsCapsuleFree(Vector3 rootPosition, CharacterController controller, Transform playerRoot)
    {
        Vector3 center = rootPosition + controller.transform.rotation * controller.center;
        float half = Mathf.Max(controller.radius, controller.height * 0.5f - controller.radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half;
        float radius = controller.radius + (profile != null ? profile.ExitSeparationPadding : 0.05f);
        Collider[] overlaps = Physics.OverlapCapsule(bottom, top, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        return overlaps.All(item => item == null || item.transform == playerRoot || item.transform.IsChildOf(playerRoot));
    }

    private static void ApplySafeExitPlacement(NetworkObject player, Vector3 position)
    {
        if (player == null) return;
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        player.transform.position = position;
        if (wasEnabled) controller.enabled = true;
        player.GetComponent<StarterAssets.FirstPersonController>()?.ResetMovementAfterForcedPlacement();
    }

    [ClientRpc]
    private void BeginSafeExitClientRpc(Vector3 position, ClientRpcParams rpc = default)
    {
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject == null) return;
        NetworkObject player = NetworkManager.Singleton.LocalClient.PlayerObject;
        player.GetComponent<PlayerWheelbarrowController>()?.BeginSafeExit(this, position);
    }

    private void ProcessPendingSafeExits()
    {
        if (!HasAuthority || pendingSafeExits.Count == 0) return;
        float minimumGrace = profile != null ? profile.ExitCollisionGraceMinimum : 0.2f;
        float maximumGrace = profile != null ? profile.ExitCollisionGraceMaximum : 1.5f;

        foreach (KeyValuePair<ulong, PendingSafeExit> pair in pendingSafeExits.ToArray())
        {
            if (!TryGetPlayer(pair.Key, out NetworkObject player))
            {
                CompleteSafeExit(pair.Key);
                continue;
            }

            float elapsed = Time.time - pair.Value.StartedAt;
            if (elapsed >= minimumGrace && IsPlayerSeparatedFromWheelbarrow(player))
            {
                CompleteSafeExit(pair.Key);
                continue;
            }

            if (elapsed < maximumGrace) continue;
            Vector3 candidate = ResolveSafeExitPosition(player);
            ApplySafeExitPlacement(player, candidate);
            pair.Value.StartedAt = Time.time;
            if (IsSessionActive) BeginSafeExitClientRpc(candidate, Target(pair.Key));
        }
    }

    private bool IsPlayerSeparatedFromWheelbarrow(NetworkObject player)
    {
        CharacterController controller = player != null ? player.GetComponent<CharacterController>() : null;
        if (controller == null) return true;

        Vector3 center = player.transform.position + controller.transform.rotation * controller.center;
        float half = Mathf.Max(controller.radius, controller.height * 0.5f - controller.radius);
        float padding = profile != null ? profile.ExitSeparationPadding : 0.05f;
        Collider[] overlaps = Physics.OverlapCapsule(
            center - Vector3.up * half,
            center + Vector3.up * half,
            controller.radius + padding,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        return overlaps.All(item => item == null ||
            (item.transform != transform && !item.transform.IsChildOf(transform)));
    }

    private void CompleteSafeExit(ulong clientId)
    {
        pendingSafeExits.Remove(clientId);
        SetOccupantCollisionIgnored(clientId, false);
        if (IsSessionActive && IsServer) CompleteSafeExitClientRpc(Target(clientId));
        else if (TryFindPlayerOnPeer(clientId, out NetworkObject player))
            player.GetComponent<PlayerWheelbarrowController>()?.CompleteSafeExit(this);
    }

    [ClientRpc]
    private void CompleteSafeExitClientRpc(ClientRpcParams rpc = default)
    {
        NetworkObject player = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        player?.GetComponent<PlayerWheelbarrowController>()?.CompleteSafeExit(this);
    }

    private void UpdateWheelVisual()
    {
        if (wheelVisual == null) return;

        Vector3 displacement = transform.position - previousWheelbarrowPosition;
        if (displacement.sqrMagnitude <= 4f)
        {
            float forwardDistance = Vector3.Dot(displacement, transform.forward);
            float radius = profile != null ? profile.WheelRadius : wheelVisualRadius;
            wheelVisualSpinDegrees += forwardDistance / Mathf.Max(0.01f, radius) * Mathf.Rad2Deg;
        }

        float targetSteer = State == WheelbarrowState.Driven && HasAuthority
            ? currentSteeringAngle
            : 0f;
        wheelVisualSteerDegrees = Mathf.MoveTowards(
            wheelVisualSteerDegrees,
            targetSteer,
            (profile != null ? profile.SteeringResponseDegreesPerSecond : 120f) * Time.fixedDeltaTime);

        Quaternion rotation = transform.rotation *
            Quaternion.Euler(0f, wheelVisualSteerDegrees, 0f) *
            Quaternion.AngleAxis(wheelVisualSpinDegrees, Vector3.right);
        wheelVisual.SetPositionAndRotation(
            transform.TransformPoint(wheelVisualRootLocalPosition),
            rotation * wheelVisualPoseOffset);
        previousWheelbarrowPosition = transform.position;
    }

    private void SetState(WheelbarrowState value)
    {
        WheelbarrowState previous = State;
        if (previous != value && (previous == WheelbarrowState.Driven || value == WheelbarrowState.Driven))
            ResetDriveInput();
        if (previous != value && value == WheelbarrowState.Driven && HasAuthority)
        {
            RefreshMassAndCenterOfMass();
            InvalidateDrivenWheelPhysics();
            driverSupportTargetInitialized = false;
            CaptureDriverSupportTarget();
            driveContactWarmupStepsRemaining = profile != null ? profile.WheelContactWarmupFixedSteps : 1;
        }
        else if (value != WheelbarrowState.Driven)
        {
            driveContactWarmupStepsRemaining = 0;
            driverSupportTargetInitialized = false;
        }
        localState = value;
        if (IsSessionActive && IsServer) stateNetwork.Value = (byte)value;
        ConfigureWheelContactMode();
    }
    private void SetDriver(ulong value) { localDriver = value; if (IsSessionActive && IsServer) driverNetwork.Value = value; }
    private void SetPassenger(ulong value) { localPassenger = value; InvalidateDrivenWheelPhysics(); if (IsSessionActive && IsServer) passengerNetwork.Value = value; }
    private void SetConcreteLoads(int value) { localConcreteLoads = Mathf.Max(0, value); InvalidateDrivenWheelPhysics(); if (IsSessionActive && IsServer) concreteLoadsNetwork.Value = localConcreteLoads; }
    private void SetSpillSequence(int value)
    {
        localSpillSequence = value;
        if (IsSessionActive && IsServer) spillSequenceNetwork.Value = value;
        PlaySpillVisual();
    }

    private void OnSpillSequenceChanged(int previous, int current)
    {
        localSpillSequence = current;
        if (current != previous) PlaySpillVisual();
    }

    private void PlaySpillVisual()
    {
        if (spillVisual == null) return;
        spillVisual.SetActive(true);
        spillVisualUntil = Time.time + spillVisualDuration;
    }

    private bool TryGetPlayer(ulong clientId, out NetworkObject player)
    {
        player = null;
        if (!IsSessionActive)
        {
            PlayerInteractionNew interaction = FindFirstObjectByType<PlayerInteractionNew>();
            player = interaction != null ? interaction.GetComponent<NetworkObject>() : null;
            return player != null;
        }
        return NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && (player = client.PlayerObject) != null;
    }

    private bool TryFindPlayerOnPeer(ulong clientId, out NetworkObject player)
    {
        player = null;
        if (!IsSessionActive) return TryGetPlayer(clientId, out player);

        NetworkObject localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (localPlayer != null && localPlayer.OwnerClientId == clientId)
        {
            player = localPlayer;
            return true;
        }

        foreach (PlayerWheelbarrowController candidate in FindObjectsByType<PlayerWheelbarrowController>(FindObjectsSortMode.None))
        {
            NetworkObject candidateNetworkObject = candidate != null ? candidate.GetComponent<NetworkObject>() : null;
            if (candidateNetworkObject != null && candidateNetworkObject.OwnerClientId == clientId)
            {
                player = candidateNetworkObject;
                return true;
            }
        }
        return false;
    }

    private bool TryGetNetworkObject(ulong id, out NetworkObject networkObject)
    {
        networkObject = null;
        return IsSessionActive && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out networkObject);
    }

    private static ClientRpcParams Target(ulong clientId) => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
    };
}
