using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

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
    private float lastInputTime;
    private float tippingElapsed;
    private float rightingStartedAt = -1f;
    private ulong rightingClient = NoClient;
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
    private Collider[] physicalColliders = Array.Empty<Collider>();

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
    public bool CanReceiveConcreteBatch => IsDockSecured && !HasConcrete && !HasResourceCargo;
    private bool IsSessionActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool HasAuthority => !IsSessionActive || IsServer;

    private void Awake()
    {
        Instances.Add(this);
        physicsBody ??= GetComponent<Rigidbody>();
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
        else if (State != WheelbarrowState.Docked && State != WheelbarrowState.Pouring) ApplyIdleBrake();
        UpdateWheelVisual();
        DetectTipping();
    }

    private void SimulateDrive()
    {
        if (Time.time - lastInputTime > (profile != null ? profile.InputTimeout : 0.25f))
        {
            throttleInput = 0f;
            steeringInput = 0f;
        }

        float forwardSpeed = Vector3.Dot(physicsBody.linearVelocity, transform.forward);
        float speedLimit = throttleInput >= 0f
            ? (profile != null ? profile.MaximumForwardSpeed : 4f)
            : (profile != null ? profile.MaximumReverseSpeed : 2f);
        bool hasThrottle = Mathf.Abs(throttleInput) > 0.01f;
        bool changingDirection = hasThrottle && Mathf.Abs(forwardSpeed) > 0.05f &&
            Mathf.Sign(throttleInput) != Mathf.Sign(forwardSpeed);
        bool belowSpeedLimit = Mathf.Abs(forwardSpeed) < speedLimit;
        float motor = hasThrottle && !changingDirection && belowSpeedLimit
            ? throttleInput * (profile != null ? profile.DriveForce : 220f)
            : 0f;
        float brake = !hasThrottle || changingDirection ? (profile != null ? profile.BrakeForce : 420f) : 0f;

        if (driveContactWarmupStepsRemaining > 0)
        {
            driveContactWarmupStepsRemaining--;
            if (drivenWheelCollider != null && drivenWheelCollider.enabled)
            {
                drivenWheelCollider.motorTorque = 0f;
                drivenWheelCollider.brakeTorque = 0f;
                drivenWheelCollider.steerAngle = 0f;
            }
            ApplyDriverSupport();
            ApplyDriverStabilization();
            return;
        }

        float filteredSteering = 0f;
        if (drivenWheelCollider != null && drivenWheelCollider.enabled)
        {
            float steeringDeadZone = profile != null ? profile.SteeringInputDeadZone : 0.08f;
            filteredSteering = Mathf.Abs(steeringInput) > steeringDeadZone ? steeringInput : 0f;
            float targetSteer = hasThrottle
                ? filteredSteering * (profile != null ? profile.MaximumSteeringAngle : 30f)
                : 0f;
            drivenWheelCollider.steerAngle = CalculateNextSteeringAngle(
                drivenWheelCollider.steerAngle,
                targetSteer,
                Time.fixedDeltaTime);
            drivenWheelCollider.motorTorque = motor;
            drivenWheelCollider.brakeTorque = brake;
        }
        ApplyDrivenYawControl(forwardSpeed, hasThrottle, filteredSteering);
        ApplyDrivenLateralGrip();
        ApplyDriverSupport();
        ApplyDriverStabilization();
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
        float verticalVelocity = Vector3.Dot(physicsBody.GetPointVelocity(point), Vector3.up);
        float gravitySupport = Mathf.Abs(Physics.gravity.y) * driverSupportLoadShare;
        float spring = profile != null ? profile.DriverSupportSpring : 18f;
        float damping = profile != null ? profile.DriverSupportDamping : 7f;
        float maximumAcceleration = profile != null ? profile.MaximumDriverSupportAcceleration : 15f;
        float acceleration = gravitySupport +
            (driverSupportTargetWorldY - point.y) * spring -
            verticalVelocity * damping;
        acceleration = Mathf.Clamp(acceleration, -maximumAcceleration, maximumAcceleration);
        physicsBody.AddForceAtPosition(Vector3.up * (physicsBody.mass * acceleration), point, ForceMode.Force);
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
            ? (profile != null ? profile.SteeringReversalDegreesPerSecond : 45f)
            : (profile != null ? profile.SteeringResponseDegreesPerSecond : 120f);
        return Mathf.MoveTowards(currentAngle, resolvedTarget, response * Mathf.Max(0f, deltaTime));
    }

    private void ApplyDrivenYawControl(float forwardSpeed, bool hasThrottle, float filteredSteering)
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
        float referenceSpeed = Mathf.Max(
            profile != null ? profile.MaximumForwardSpeed : 4f,
            profile != null ? profile.MaximumReverseSpeed : 2f);
        float speedFactor = Mathf.InverseLerp(minimumSpeed, Mathf.Max(minimumSpeed + 0.01f, referenceSpeed), Mathf.Abs(forwardSpeed));
        float direction = Mathf.Abs(forwardSpeed) >= minimumSpeed ? Mathf.Sign(forwardSpeed) : 0f;
        float wheelSteering = drivenWheelCollider != null && Mathf.Abs(profile != null ? profile.MaximumSteeringAngle : 30f) > 0.01f
            ? Mathf.Clamp(drivenWheelCollider.steerAngle / (profile != null ? profile.MaximumSteeringAngle : 30f), -1f, 1f)
            : filteredSteering;
        float targetYaw = hasThrottle ? maximumYaw * wheelSteering * direction * speedFactor : 0f;
        float response = profile != null ? profile.DrivenYawResponse : 8f;
        float maximumAcceleration = (profile != null ? profile.MaximumDrivenYawAccelerationDegrees : 360f) * Mathf.Deg2Rad;
        float acceleration = Mathf.Clamp((targetYaw - currentYaw) * response, -maximumAcceleration, maximumAcceleration);
        physicsBody.AddTorque(worldUp * acceleration, ForceMode.Acceleration);
    }

    private void ApplyDrivenLateralGrip()
    {
        if (drivenWheelCollider == null || !drivenWheelCollider.enabled ||
            !drivenWheelCollider.GetGroundHit(out WheelHit wheelHit)) return;

        Vector3 lateral = Vector3.ProjectOnPlane(transform.right, wheelHit.normal);
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
        float maximumAcceleration = Mathf.Lerp(
            profile != null ? profile.EmptyMaximumLateralGripAcceleration : 14f,
            profile != null ? profile.LoadedMaximumLateralGripAcceleration : 8f,
            loadRatio);
        float lateralSpeed = Vector3.Dot(physicsBody.linearVelocity, lateral);
        float acceleration = Mathf.Clamp(-lateralSpeed * damping, -maximumAcceleration, maximumAcceleration);
        physicsBody.AddForce(lateral * acceleration, ForceMode.Acceleration);
    }

    private void ApplyDriverStabilization()
    {
        Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
        Vector3 localAngular = physicsBody.angularVelocity;
        localAngular.y = 0f;
        Vector3 torque = axis * (profile != null ? profile.DriverStabilizingTorque : 18f) -
            localAngular * (profile != null ? profile.DriverStabilizingDamping : 4f);
        physicsBody.AddTorque(torque, ForceMode.Acceleration);

        if (drivenWheelCollider != null && drivenWheelCollider.enabled &&
            Mathf.Abs(drivenWheelCollider.motorTorque) > 0.01f)
        {
            Vector3 wheelAxle = Quaternion.AngleAxis(drivenWheelCollider.steerAngle, transform.up) * transform.right;
            physicsBody.AddTorque(-wheelAxle * drivenWheelCollider.motorTorque, ForceMode.Force);
        }
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

    private void ConfigureWheelContactMode()
    {
        bool isDriven = State == WheelbarrowState.Driven;
        bool simulateDrivenWheel = isDriven && HasAuthority;

        if (drivenWheelCollider != null && drivenWheelCollider.enabled != simulateDrivenWheel)
        {
            ResetDrivenWheel();
            if (simulateDrivenWheel) ConfigureDrivenWheelPhysics(true);
            drivenWheelCollider.enabled = simulateDrivenWheel;
        }
        if (simulateDrivenWheel) ConfigureDrivenWheelPhysics();

        SetRestingSupportsEnabled(!isDriven);
        if (wheelContactCollider != null && wheelContactCollider.enabled == isDriven)
            wheelContactCollider.enabled = !isDriven;
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
        lastInputTime = Time.time;
        ResetDrivenWheel();
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
        SetState(WheelbarrowState.Tipped);
        if (previousDriver != NoClient) BeginSafeExit(previousDriver);
        SetDriver(NoClient);
        ReleasePassenger(true);
        SpillAllCargo();
        SpillConcrete();
        activeDock?.ForceReleaseWheelbarrow(this);
        activeDock = null;
    }

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
        if (TryGetPlayer(senderClientId, out NetworkObject player) && player.TryGetComponent(out PlayerStaminaController stamina) && stamina.CurrentStamina <= 0f)
            throttle = 0f;
        throttleInput = Mathf.Clamp(throttle, -1f, 1f);
        steeringInput = Mathf.Abs(throttleInput) > 0.01f ? Mathf.Clamp(steering, -1f, 1f) : 0f;
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
        if (State != WheelbarrowState.Tipped || Speed > (profile != null ? profile.MaximumRightingLinearSpeed : 0.2f)) return;
        rightingClient = clientId;
        rightingStartedAt = Time.time;
        SetState(WheelbarrowState.Righting);
    }

    private void SimulateRighting()
    {
        if (rightingClient == NoClient || Time.time - rightingStartedAt < (profile != null ? profile.RightingHoldDuration : 1.5f)) return;
        Quaternion upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        physicsBody.MoveRotation(Quaternion.RotateTowards(physicsBody.rotation, upright,
            (profile != null ? profile.RightingAngularSpeed : 120f) * Time.fixedDeltaTime));
        if (Quaternion.Angle(physicsBody.rotation, upright) <= 1f)
        {
            physicsBody.rotation = upright;
            rightingClient = NoClient;
            SetState(WheelbarrowState.Free);
        }
    }

    public void CancelRighting()
    {
        if (!HasAuthority || State != WheelbarrowState.Righting) return;
        rightingClient = NoClient;
        rightingStartedAt = -1f;
        SetState(WheelbarrowState.Tipped);
    }

    private void RefreshMassAndCenterOfMass()
    {
        float baseMass = profile != null ? profile.BaseMass : 22f;
        float cargoMass = GetResourceCargoMass() + ConcreteLoads * (profile != null ? profile.ConcreteBatchMass : 80f) +
            (PassengerClientId != NoClient ? profile != null ? profile.PassengerMass : 75f : 0f);
        physicsBody.mass = baseMass + cargoMass;
        Vector3 cargoPoint = cargoRoot != null ? transform.InverseTransformPoint(cargoRoot.position) : Vector3.up * 0.4f;
        physicsBody.centerOfMass = Vector3.Lerp(Vector3.zero, cargoPoint, cargoMass / Mathf.Max(1f, baseMass + cargoMass));
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

        float targetSteer = State == WheelbarrowState.Driven && drivenWheelCollider != null && HasAuthority
            ? drivenWheelCollider.steerAngle
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
