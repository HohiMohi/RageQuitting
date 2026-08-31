using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NetworkObject), typeof(Rigidbody), typeof(WheelbarrowPresentationController))]
public class WheelbarrowController : NetworkBehaviour, IConcreteBatchReceiver, IRopeAttachable
{
    public const ulong NoClient = ulong.MaxValue;
    private const float WheelContactDistanceEpsilon = 0.0001f;
    private static readonly HashSet<WheelbarrowController> Instances = new HashSet<WheelbarrowController>();

    private enum DrivenWheelContactSource : byte
    {
        None,
        SphereCast,
        RaycastFallback
    }

    private enum DrivenWheelContactRejection : byte
    {
        None,
        NoHit,
        IgnoredCollider,
        InitialOverlap,
        InvalidPoint,
        InvalidNormal,
        SurfaceAboveWheel,
        PointTooFar
    }

    private struct DrivenWheelContactSample
    {
        public RaycastHit Hit;
        public float SuspensionError;
        public DrivenWheelContactSource Source;
    }

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
    [SerializeField] private Transform presentationVisualRoot;
    [SerializeField] private Transform[] cargoSlots = Array.Empty<Transform>();
    [SerializeField] private GameObject concreteCargoVisual;
    [SerializeField] private GameObject spillVisual;
    [SerializeField, Min(0.1f)] private float spillVisualDuration = 1.25f;
    [SerializeField] private Transform leftPourAnchor;
    [SerializeField] private Transform rightPourAnchor;
    [SerializeField] private Transform[] safeExitPoints = Array.Empty<Transform>();
    [SerializeField] private Collider rightingInteractionCollider;
    [SerializeField] private BoxCollider automaticBoardingTrigger;

    private readonly NetworkVariable<byte> stateNetwork = new NetworkVariable<byte>((byte)WheelbarrowState.Free);
    private readonly NetworkVariable<ulong> driverNetwork = new NetworkVariable<ulong>(NoClient);
    private readonly NetworkVariable<ulong> passengerNetwork = new NetworkVariable<ulong>(NoClient);
    private readonly NetworkVariable<int> concreteLoadsNetwork = new NetworkVariable<int>();
    private readonly NetworkVariable<ulong> dockNetwork = new NetworkVariable<ulong>(NoClient);
    private readonly NetworkVariable<int> spillSequenceNetwork = new NetworkVariable<int>();
    private readonly NetworkVariable<uint> authorityEpochNetwork = new NetworkVariable<uint>();
    private readonly NetworkList<ulong> cargoNetwork = new NetworkList<ulong>();
    private readonly List<BaseResourceNew> localCargo = new List<BaseResourceNew>();
    private readonly Dictionary<ulong, PendingSafeExit> pendingSafeExits = new Dictionary<ulong, PendingSafeExit>();
    private readonly HashSet<ulong> collisionIgnoredPlayers = new HashSet<ulong>();
    private readonly HashSet<ulong> locallyAppliedCollisionIgnores = new HashSet<ulong>();

    private sealed class PendingPassengerBoarding
    {
        public ulong ClientId;
        public uint Token;
        public float StartedAt;
        public bool Automatic;
        public bool StartedDowned;
        public Vector3 VelocityBefore;
        public Vector3 AngularVelocityBefore;
        public ulong PhysicsOwnerClientId;
        public bool PassengerOwnerReady;
        public bool PhysicsOwnerReady;
    }

    private sealed class PendingSafeExit
    {
        public ulong ClientId;
        public uint Token;
        public float StartedAt;
        public float OperationStartedAt;
        public float SearchRadius;
        public bool Passenger;
        public bool Forced;
        public bool ApplyTippedPassengerImpulse;
        public bool PlacementRequested;
        public bool PlacementConfirmed;
        public bool RoleCleared;
        public Vector3 RequestedPosition;
        public Vector3 EjectionDirection;
        public Vector3 ReservedCapsuleBottom;
        public Vector3 ReservedCapsuleTop;
        public float ReservedCapsuleRadius;
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
    private Vector3 failedConcreteOriginalDockPosition;
    private Quaternion failedConcreteOriginalDockRotation = Quaternion.identity;
    private bool hasFailedConcreteOriginalDockPose;
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
    private readonly RaycastHit[] wheelContactRayHits = new RaycastHit[12];
    private readonly RaycastHit[] ropeTowGroundHits = new RaycastHit[12];
    private DrivenWheelContactSource drivenWheelContactSource;
    private DrivenWheelContactSource lastLoggedWheelContactSource;
    private DrivenWheelContactRejection lastLoggedWheelContactRejection;
    private Collider lastLoggedWheelContactCollider;
    private Collider[] physicalColliders = Array.Empty<Collider>();
    private readonly Dictionary<Collider, PhysicsMaterial> ropeTowOriginalMaterials =
        new Dictionary<Collider, PhysicsMaterial>();
    private bool isRopeTowActive;
    private float ropeTowTension;
    private float lastRopeTowSignalTime = float.NegativeInfinity;
    private float ropeTowSlackStartedAt = float.NegativeInfinity;
    private Collider ropeTowGroundCollider;
    private Vector3 ropeTowGroundNormal = Vector3.up;
    private Vector3 ropeTowDirection;
    private float ropeTowAcceleration;
    private float navObstacleSettledTime;
    private WheelbarrowPresentationController presentationController;
    private PendingPassengerBoarding pendingPassengerBoarding;
    private uint nextPassengerBoardingToken;
    private bool massPropertiesDirty = true;
    private float motionSnapshotAccumulator;
    private uint localMotionSequence;
    private uint lastAcceptedMotionSequence;
    private bool hasAcceptedMotionSnapshot;
    private WheelbarrowMotionSnapshot lastAcceptedMotionSnapshot;
    private WheelbarrowAuthorityPhysicsSeed pendingAuthoritySeed;
    private bool hasPendingAuthoritySeed;
    private bool pendingAuthorityPreparationAck;
    private ulong pendingAuthorityGrantClient = NoClient;
    private uint pendingAuthorityGrantEpoch;
    private float pendingAuthorityGrantDeadline;
    private double lastAcceptedMotionReceiveTime;
    private float remoteTippingElapsed;
    private uint nextSafeExitToken;
    private WheelbarrowNetworkTransform wheelbarrowNetworkTransform;

    public WheelbarrowProfileSO Profile => profile;
    public Rigidbody PhysicsBody => physicsBody;
    public Transform PassengerAnchor => passengerAnchor;
    public WheelbarrowState State => IsSessionActive ? (WheelbarrowState)stateNetwork.Value : localState;
    public ulong DriverClientId => IsSessionActive ? driverNetwork.Value : localDriver;
    public ulong PassengerClientId => IsSessionActive ? passengerNetwork.Value : localPassenger;
    public int ConcreteLoads => IsSessionActive ? concreteLoadsNetwork.Value : localConcreteLoads;
    public bool HasConcrete => ConcreteLoads > 0;
    public bool HasHardenedPassengerConcrete => TryGetPassengerConcreteTrap(out PlayerConcreteTrapController trap) &&
        trap.IsAttachedToWheelbarrow && trap.IsSourcedBy(this);
    public bool HasPourableConcrete => HasConcrete && !HasHardenedPassengerConcrete;
    public bool HasResourceCargo => CargoCount > 0;
    public int CargoCount => IsSessionActive ? cargoNetwork.Count : localCargo.Count;
    public float Speed => HasLocalPhysicsAuthority && physicsBody != null
        ? physicsBody.linearVelocity.magnitude
        : hasAcceptedMotionSnapshot ? lastAcceptedMotionSnapshot.LinearVelocity.magnitude : 0f;
    public bool IsDocked => State == WheelbarrowState.Docked || State == WheelbarrowState.Pouring;
    public bool IsDockSecured => IsDocked || State == WheelbarrowState.TrappedInFailedConcrete;
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
    public float PresentationWheelbase
    {
        get
        {
            Transform support = driverSupportPoint != null ? driverSupportPoint : driverAnchor;
            if (support == null || wheelVisual == null) return 2f;
            return Mathf.Abs(transform.InverseTransformPoint(wheelVisual.position).z -
                transform.InverseTransformPoint(support.position).z);
        }
    }
    private bool IsSessionActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool HasAuthority => !IsSessionActive || IsServer;
    public bool HasLocalPhysicsAuthority => !IsSessionActive || IsOwner;
    public uint AuthorityEpoch => IsSessionActive ? authorityEpochNetwork.Value : 0;
    public float CurrentSteeringAngle => currentSteeringAngle;
    public float CurrentWheelSpinDegrees => wheelVisualSpinDegrees;
    public bool IsRopeTowActive => isRopeTowActive;
    public float CurrentRopeTowTension => ropeTowTension;
    public Collider RopeTowGroundCollider => ropeTowGroundCollider;
    public Vector3 RopeTowGroundNormal => ropeTowGroundNormal;
    public Vector3 ResolvedRopeTowDirection => ropeTowDirection;
    public float CurrentRopeTowAcceleration => ropeTowAcceleration;
    public int RopeTowSwappedColliderCount => ropeTowOriginalMaterials.Count;
    internal bool CanRemainRopeAttached => CanKeepExistingRopeAttachment(State);
    internal bool CanReceiveRopePull => (State == WheelbarrowState.Free || State == WheelbarrowState.Tipped) &&
        HasLocalPhysicsAuthority && physicsBody != null && !physicsBody.isKinematic;

    public bool TryCreateRopeAttachment(RopeToolController rope, Vector3 hitPoint, out RopeAttachment attachment)
    {
        attachment = default;
        if (rope == null || !CanAcceptNewRopeAttachment(State) ||
            !IsFinite(hitPoint)) return false;

        attachment = new RopeAttachment(NetworkObject, RopeTargetKind.Wheelbarrow,
            transform.InverseTransformPoint(hitPoint));
        return true;
    }

    internal float GetRopePullLoadRatio()
    {
        if (physicsBody == null) return 0f;
        float baseMass = profile != null ? profile.BaseMass : 22f;
        float largestTypicalLoad = profile != null
            ? Mathf.Max(profile.MaximumResourceCargoMass, profile.ConcreteBatchMass, profile.PassengerMass)
            : 80f;
        return Mathf.Clamp01((physicsBody.mass - baseMass) / Mathf.Max(1f, largestTypicalLoad));
    }

    internal void ApplyRopeTow(Vector3 localAttachmentPoint, Vector3 ropeDirection, float normalizedTension,
        float extension, bool blocked, RopeToolProfileSO ropeProfile)
    {
        lastRopeTowSignalTime = Time.time;
        ropeTowTension = Mathf.Clamp01(normalizedTension);
        ropeTowAcceleration = 0f;

        bool allowed = CanReceiveRopePull && !blocked && ropeProfile != null && extension > 0f &&
            ropeTowTension > (profile != null ? profile.RopeTowActivationTension : 0.04f);
        if (!allowed)
        {
            SetRopeTowInactive(false);
            return;
        }

        Vector3 point = transform.TransformPoint(localAttachmentPoint);
        Vector3 pullDirection = -ropeDirection;
        if (!IsFinite(point) || !IsFinite(pullDirection) || pullDirection.sqrMagnitude <= 0.0001f)
        {
            SetRopeTowInactive(false);
            return;
        }

        ResolveRopeTowGround(out ropeTowGroundCollider, out ropeTowGroundNormal);
        ropeTowDirection = LimitRopeTowNormalComponent(
            pullDirection.normalized,
            ropeTowGroundNormal,
            profile != null ? profile.MaximumRopeTowVerticalRatio : 0.3f);
        if (ropeTowDirection.sqrMagnitude <= 0.0001f)
        {
            SetRopeTowInactive(false);
            return;
        }

        SetRopeTowActive();
        float pullSpeed = Vector3.Dot(physicsBody.GetPointVelocity(point), ropeTowDirection);
        float acceleration = ropeProfile.wheelbarrowSpring * extension -
            ropeProfile.wheelbarrowDamping * pullSpeed;
        if (ropeProfile.maximumWheelbarrowPullSpeed > 0f)
        {
            float speedLimitedAcceleration = Mathf.Max(0f,
                (ropeProfile.maximumWheelbarrowPullSpeed - pullSpeed) /
                Mathf.Max(Time.fixedDeltaTime, 0.0001f));
            acceleration = Mathf.Min(acceleration, speedLimitedAcceleration);
        }

        float loadMultiplier = Mathf.Lerp(1f, ropeProfile.fullLoadWheelbarrowPullMultiplier,
            GetRopePullLoadRatio());
        ropeTowAcceleration = Mathf.Clamp(acceleration, 0f, ropeProfile.maximumWheelbarrowAcceleration) *
            loadMultiplier;
        if (ropeTowAcceleration > 0f)
            physicsBody.AddForceAtPosition(
                ropeTowDirection * ropeTowAcceleration,
                point,
                ForceMode.Acceleration);
    }

    internal void NotifyRopeTowDetached()
    {
        SetRopeTowInactive(true);
    }

    private static Vector3 LimitRopeTowNormalComponent(Vector3 direction, Vector3 groundNormal,
        float maximumNormalRatio)
    {
        Vector3 normal = groundNormal.sqrMagnitude > 0.0001f ? groundNormal.normalized : Vector3.up;
        Vector3 tangent = Vector3.ProjectOnPlane(direction, normal);
        float tangentMagnitude = tangent.magnitude;
        if (tangentMagnitude <= 0.0001f) return Vector3.zero;

        float normalComponent = Vector3.Dot(direction, normal);
        float limitedNormal = Mathf.Clamp(
            normalComponent,
            -tangentMagnitude * Mathf.Clamp01(maximumNormalRatio),
            tangentMagnitude * Mathf.Clamp01(maximumNormalRatio));
        return (tangent + normal * limitedNormal).normalized;
    }

    private void ResolveRopeTowGround(out Collider groundCollider, out Vector3 groundNormal)
    {
        groundCollider = null;
        groundNormal = Vector3.up;
        if (physicsBody == null) return;

        float probeDistance = profile != null ? profile.RopeTowGroundProbeDistance : 1.5f;
        Vector3 origin = physicsBody.worldCenterOfMass + Vector3.up * 0.2f;
        int count = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            ropeTowGroundHits,
            probeDistance + 0.2f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        ResolveWheelContactIgnoredRoots(out Transform driverRoot, out Transform passengerRoot);
        float nearestDistance = float.PositiveInfinity;
        float minimumNormalDot = profile != null ? profile.MinimumWheelGroundNormalDot : 0.25f;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = ropeTowGroundHits[i];
            Collider candidate = hit.collider;
            if (candidate == null || candidate.transform == transform || candidate.transform.IsChildOf(transform)) continue;
            if (driverRoot != null &&
                (candidate.transform == driverRoot || candidate.transform.IsChildOf(driverRoot))) continue;
            if (passengerRoot != null &&
                (candidate.transform == passengerRoot || candidate.transform.IsChildOf(passengerRoot))) continue;
            if (hit.distance <= WheelContactDistanceEpsilon || hit.distance >= nearestDistance ||
                !IsFinite(hit.point) || !IsFinite(hit.normal) || hit.normal.sqrMagnitude <= 0.0001f) continue;
            Vector3 normal = hit.normal.normalized;
            if (Vector3.Dot(normal, Vector3.up) < minimumNormalDot) continue;

            nearestDistance = hit.distance;
            groundCollider = candidate;
            groundNormal = normal;
        }
    }

    private void SetRopeTowActive()
    {
        isRopeTowActive = true;
        ropeTowSlackStartedAt = float.NegativeInfinity;
        navObstacleSettledTime = 0f;
        if (navigationObstacle != null && navigationObstacle.carving)
            navigationObstacle.carving = false;
        ApplyRopeTowMaterial();
    }

    private void SetRopeTowInactive(bool restoreImmediately)
    {
        if (isRopeTowActive)
            ropeTowSlackStartedAt = Time.time;
        else if (float.IsNegativeInfinity(ropeTowSlackStartedAt))
            ropeTowSlackStartedAt = Time.time;
        isRopeTowActive = false;
        ropeTowAcceleration = 0f;
        if (restoreImmediately) RestoreRopeTowMaterials();
    }

    private void UpdateRopeTowLifecycle()
    {
        bool allowedState = State == WheelbarrowState.Free || State == WheelbarrowState.Tipped;
        if (!HasLocalPhysicsAuthority || !allowedState || physicsBody == null || physicsBody.isKinematic)
        {
            SetRopeTowInactive(true);
            return;
        }

        float releaseDelay = profile != null ? profile.RopeTowReleaseDelay : 0.2f;
        if (isRopeTowActive && Time.time - lastRopeTowSignalTime > releaseDelay)
        {
            SetRopeTowInactive(true);
            return;
        }
        if (!isRopeTowActive && ropeTowOriginalMaterials.Count > 0 &&
            Time.time - ropeTowSlackStartedAt >= releaseDelay)
            RestoreRopeTowMaterials();
    }

    private void ApplyRopeTowMaterial()
    {
        PhysicsMaterial towMaterial = profile != null ? profile.RopeTowContactMaterial : null;
        if (towMaterial == null) return;
        if (physicalColliders == null || physicalColliders.Length == 0)
            physicalColliders = GetComponentsInChildren<Collider>(true)
                .Where(item => item != null && !item.isTrigger)
                .ToArray();

        foreach (Collider item in physicalColliders)
        {
            if (item == null || item.isTrigger || item is WheelCollider ||
                ropeTowOriginalMaterials.ContainsKey(item)) continue;
            ropeTowOriginalMaterials.Add(item, item.sharedMaterial);
            item.sharedMaterial = towMaterial;
        }
    }

    private void RestoreRopeTowMaterials()
    {
        foreach (KeyValuePair<Collider, PhysicsMaterial> entry in ropeTowOriginalMaterials)
        {
            if (entry.Key != null) entry.Key.sharedMaterial = entry.Value;
        }
        ropeTowOriginalMaterials.Clear();
        ropeTowSlackStartedAt = float.NegativeInfinity;
        ropeTowGroundCollider = null;
        ropeTowGroundNormal = Vector3.up;
        ropeTowDirection = Vector3.zero;
        ropeTowAcceleration = 0f;
        ropeTowTension = 0f;
    }

    private static bool CanAcceptNewRopeAttachment(WheelbarrowState state)
    {
        return state == WheelbarrowState.Free || state == WheelbarrowState.Tipped;
    }

    private static bool CanKeepExistingRopeAttachment(WheelbarrowState state)
    {
        return state == WheelbarrowState.Free || state == WheelbarrowState.Tipped ||
            state == WheelbarrowState.Righting;
    }

#if UNITY_EDITOR
    public bool RunEditorRopeLifecycleProbe(out string result)
    {
        bool attachmentStates = CanAcceptNewRopeAttachment(WheelbarrowState.Free) &&
            CanAcceptNewRopeAttachment(WheelbarrowState.Tipped) &&
            !CanAcceptNewRopeAttachment(WheelbarrowState.Driven) &&
            !CanAcceptNewRopeAttachment(WheelbarrowState.Docked) &&
            !CanAcceptNewRopeAttachment(WheelbarrowState.Pouring) &&
            !CanAcceptNewRopeAttachment(WheelbarrowState.Righting) &&
            !CanAcceptNewRopeAttachment(WheelbarrowState.TrappedInFailedConcrete);
        bool lifecycleStates = CanKeepExistingRopeAttachment(WheelbarrowState.Free) &&
            CanKeepExistingRopeAttachment(WheelbarrowState.Tipped) &&
            CanKeepExistingRopeAttachment(WheelbarrowState.Righting) &&
            !CanKeepExistingRopeAttachment(WheelbarrowState.Driven) &&
            !CanKeepExistingRopeAttachment(WheelbarrowState.Docked) &&
            !CanKeepExistingRopeAttachment(WheelbarrowState.Pouring) &&
            !CanKeepExistingRopeAttachment(WheelbarrowState.TrappedInFailedConcrete);
        Vector3 samplePoint = transform.position + transform.right * 0.37f + transform.up * 0.22f;
        Vector3 restoredPoint = transform.TransformPoint(transform.InverseTransformPoint(samplePoint));
        bool exactPoint = Vector3.Distance(samplePoint, restoredPoint) <= 0.0001f;
        bool passed = attachmentStates && lifecycleStates && exactPoint;
        result = $"attachmentStates={attachmentStates}, lifecycleStates={lifecycleStates}, exactPoint={exactPoint}, " +
            $"roundTripError={Vector3.Distance(samplePoint, restoredPoint):F6}, passed={passed}";
        return passed;
    }
#endif

    private void Awake()
    {
        Instances.Add(this);
        physicsBody ??= GetComponent<Rigidbody>();
        presentationController = GetComponent<WheelbarrowPresentationController>();
        wheelbarrowNetworkTransform = GetComponent<WheelbarrowNetworkTransform>();
        presentationVisualRoot ??= transform.Find("Visual");
        presentationController?.Initialize(
            this,
            driverAnchor,
            presentationVisualRoot,
            concreteCargoVisual != null ? concreteCargoVisual.transform : null);
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
        ConfigureAutomaticBoardingTrigger();
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
        SetRopeTowInactive(true);
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
        driverNetwork.OnValueChanged += OnDriverChanged;
        passengerNetwork.OnValueChanged += OnPassengerChanged;
        authorityEpochNetwork.OnValueChanged += OnAuthorityEpochChanged;
        if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        if (IsServer)
        {
            activeDock = null;
            authorityEpochNetwork.Value = 1;
            SetState(WheelbarrowState.Free);
            SetDriver(NoClient);
            SetPassenger(NoClient);
        }
        ConfigureBody();
        ConfigureWheelContactMode();
        UpdateNavigationObstacle(true);
        RefreshCargoReferences();
        RestoreReplicatedOccupantCollisions();
        ResetMotionStream();
    }

    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        pendingAuthorityPreparationAck = false;
        ConfigureBody();
        if (hasPendingAuthoritySeed && pendingAuthoritySeed.Motion.AuthorityEpoch == AuthorityEpoch)
        {
            ApplyAuthorityPhysicsSeed(pendingAuthoritySeed, true);
            hasPendingAuthoritySeed = false;
            driveContactWarmupStepsRemaining = Mathf.Max(1, profile != null ? profile.WheelContactWarmupFixedSteps : 1);
        }
        ResetMotionStream();
        presentationController?.ResetPresentation();
    }

    public override void OnLostOwnership()
    {
        base.OnLostOwnership();
        SetRopeTowInactive(true);
        pendingAuthorityPreparationAck = false;
        ConfigureBody();
        ResetDriveInput();
        presentationController?.ResetPresentation();
    }

    public override void OnNetworkDespawn()
    {
        SetRopeTowInactive(true);
        cargoNetwork.OnListChanged -= OnCargoChanged;
        spillSequenceNetwork.OnValueChanged -= OnSpillSequenceChanged;
        driverNetwork.OnValueChanged -= OnDriverChanged;
        passengerNetwork.OnValueChanged -= OnPassengerChanged;
        authorityEpochNetwork.OnValueChanged -= OnAuthorityEpochChanged;
        if (NetworkManager != null && IsServer) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        if (IsServer)
        {
            activeDock?.ForceReleaseWheelbarrow(this);
            ReleaseAllOccupants(true);
            SpillAllCargo();
        }
        RestoreAllPlayerCollisions(false);
    }

    private void ResetMotionStream()
    {
        motionSnapshotAccumulator = 0f;
        localMotionSequence = 0;
        presentationController?.ResetPresentation();
    }

    private WheelbarrowMotionSnapshot CaptureMotionSnapshot(uint sequence)
    {
        double timestamp = NetworkManager != null
            ? (IsServer ? NetworkManager.ServerTime.Time : NetworkManager.LocalTime.Time)
            : Time.unscaledTimeAsDouble;
        return new WheelbarrowMotionSnapshot(
            AuthorityEpoch,
            sequence,
            timestamp,
            physicsBody != null ? physicsBody.position : transform.position,
            physicsBody != null ? physicsBody.rotation : transform.rotation,
            physicsBody != null ? physicsBody.linearVelocity : Vector3.zero,
            physicsBody != null ? physicsBody.angularVelocity : Vector3.zero,
            currentSteeringAngle,
            wheelVisualSpinDegrees,
            throttleInput,
            steeringInput);
    }

    private WheelbarrowAuthorityPhysicsSeed CaptureAuthorityPhysicsSeed(uint authorityEpoch)
    {
        massPropertiesDirty = true;
        RefreshMassAndCenterOfMass();
        WheelbarrowMotionSnapshot motion = CaptureMotionSnapshot(0);
        motion.AuthorityEpoch = authorityEpoch;
        motion.ServerTimestamp = NetworkManager != null ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
        return new WheelbarrowAuthorityPhysicsSeed
        {
            Motion = motion,
            TotalMass = physicsBody != null ? physicsBody.mass : profile != null ? profile.BaseMass : 22f,
            LocalCenterOfMass = physicsBody != null ? physicsBody.centerOfMass : Vector3.zero,
            DriverSupportLoadShare = driverSupportLoadShare,
            WheelLoadShare = wheelLoadShare
        };
    }

    private void ApplyAuthorityPhysicsSeed(WheelbarrowAuthorityPhysicsSeed seed, bool applyMotion)
    {
        if (physicsBody == null) return;
        physicsBody.mass = Mathf.Max(0.01f, seed.TotalMass);
        physicsBody.centerOfMass = seed.LocalCenterOfMass;
        driverSupportLoadShare = Mathf.Clamp01(seed.DriverSupportLoadShare);
        wheelLoadShare = Mathf.Clamp01(seed.WheelLoadShare);
        massPropertiesDirty = false;
        if (applyMotion) ApplyMotionSeed(seed.Motion, true);
    }

    private void PublishMotionIfDue(float deltaTime)
    {
        if (!IsSessionActive || !HasLocalPhysicsAuthority || physicsBody == null) return;
        float rate = profile != null ? profile.MotionSnapshotRate : 50f;
        float interval = 1f / Mathf.Max(1f, rate);
        motionSnapshotAccumulator += Mathf.Max(0f, deltaTime);
        if (motionSnapshotAccumulator < interval) return;
        motionSnapshotAccumulator %= interval;
        WheelbarrowMotionSnapshot snapshot = CaptureMotionSnapshot(++localMotionSequence);
        if (IsServer)
            AcceptMotionSnapshot(snapshot, NetworkManager.ServerClientId);
        else
            SubmitMotionSnapshotServerRpc(snapshot);
    }

    [ServerRpc(RequireOwnership = true, Delivery = RpcDelivery.Unreliable)]
    private void SubmitMotionSnapshotServerRpc(WheelbarrowMotionSnapshot snapshot, ServerRpcParams rpc = default)
    {
        if (!AcceptMotionSnapshot(snapshot, rpc.Receive.SenderClientId))
            SendMotionCorrection(rpc.Receive.SenderClientId);
    }

    private bool AcceptMotionSnapshot(WheelbarrowMotionSnapshot snapshot, ulong senderClientId)
    {
        if (!TryNormalizeQuaternion(snapshot.Rotation, out Quaternion normalizedRotation)) return false;
        snapshot.Rotation = normalizedRotation;
        if (!IsServer || NetworkObject == null || senderClientId != NetworkObject.OwnerClientId ||
            snapshot.AuthorityEpoch != AuthorityEpoch ||
            (State != WheelbarrowState.Driven && senderClientId != NetworkManager.ServerClientId) ||
            (State == WheelbarrowState.Driven && senderClientId != DriverClientId) ||
            (hasAcceptedMotionSnapshot && !IsSequenceNewer(snapshot.Sequence, lastAcceptedMotionSequence)) ||
            !ValidateMotionEnvelope(snapshot))
            return false;

        snapshot.ServerTimestamp = NetworkManager.ServerTime.Time;
        lastAcceptedMotionSequence = snapshot.Sequence;
        lastAcceptedMotionReceiveTime = snapshot.ServerTimestamp;
        lastAcceptedMotionSnapshot = snapshot;
        hasAcceptedMotionSnapshot = true;
        lastReceivedThrottleInput = throttleInput = Mathf.Clamp(snapshot.ThrottleInput, -1f, 1f);
        lastReceivedSteeringInput = steeringInput = Mathf.Clamp(snapshot.SteeringInput, -1f, 1f);
        currentSteeringAngle = snapshot.SteeringAngle;
        wheelVisualSpinDegrees = snapshot.WheelSpinDegrees;
        lastInputTime = Time.time;

        if (!HasLocalPhysicsAuthority)
        {
            ApplyAcceptedMotionToServerRoot();
            UpdateCargoTransforms();
            EvaluateRemoteTipping(snapshot);
        }
        ReceiveMotionSnapshotClientRpc(snapshot);
        return true;
    }

    private bool ValidateMotionEnvelope(WheelbarrowMotionSnapshot snapshot)
    {
        if (!IsFinite(snapshot.Position) || !IsFinite(snapshot.Rotation) ||
            !IsFinite(snapshot.LinearVelocity) || !IsFinite(snapshot.AngularVelocity) ||
            !float.IsFinite(snapshot.SteeringAngle) || !float.IsFinite(snapshot.WheelSpinDegrees) ||
            Mathf.Abs(snapshot.ThrottleInput) > 1.001f || Mathf.Abs(snapshot.SteeringInput) > 1.001f)
            return false;

        float maximumLinear = profile != null ? profile.MotionMaximumLinearSpeed : 14f;
        float maximumAngular = (profile != null ? profile.MotionMaximumAngularSpeedDegrees : 240f) * Mathf.Deg2Rad;
        if (snapshot.LinearVelocity.magnitude > maximumLinear || snapshot.AngularVelocity.magnitude > maximumAngular ||
            Mathf.Abs(snapshot.SteeringAngle) > (profile != null ? profile.MaximumSteeringAngle : 36f) + 1f)
            return false;
        if (!hasAcceptedMotionSnapshot || snapshot.AuthorityEpoch != lastAcceptedMotionSnapshot.AuthorityEpoch) return true;

        double elapsed = Math.Max(0.001d, NetworkManager.ServerTime.Time - lastAcceptedMotionReceiveTime);
        float positionAllowance = maximumLinear * (float)elapsed + (profile != null ? profile.MotionPositionTolerance : 0.2f);
        float rotationAllowance = (profile != null ? profile.MotionMaximumAngularSpeedDegrees : 240f) * (float)elapsed +
            (profile != null ? profile.MotionRotationToleranceDegrees : 8f);
        return Vector3.Distance(lastAcceptedMotionSnapshot.Position, snapshot.Position) <= positionAllowance &&
            Quaternion.Angle(lastAcceptedMotionSnapshot.Rotation, snapshot.Rotation) <= rotationAllowance;
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ReceiveMotionSnapshotClientRpc(WheelbarrowMotionSnapshot snapshot)
    {
        if (HasLocalPhysicsAuthority) return;
        presentationController?.ReceiveSnapshot(snapshot);
    }

    private void SendMotionCorrection(ulong clientId)
    {
        if (!IsServer || !hasAcceptedMotionSnapshot || clientId == NetworkManager.ServerClientId) return;
        ApplyMotionCorrectionClientRpc(lastAcceptedMotionSnapshot, Target(clientId));
        wheelbarrowNetworkTransform?.SetState(
            lastAcceptedMotionSnapshot.Position,
            lastAcceptedMotionSnapshot.Rotation,
            transform.localScale,
            false);
    }

    [ClientRpc]
    private void ApplyMotionCorrectionClientRpc(WheelbarrowMotionSnapshot snapshot, ClientRpcParams rpc = default)
    {
        if (!HasLocalPhysicsAuthority || snapshot.AuthorityEpoch != AuthorityEpoch) return;
        ApplyMotionSeed(snapshot, true);
    }

    private static bool IsSequenceNewer(uint sequence, uint previous) =>
        sequence != previous && unchecked(sequence - previous) < 0x80000000u;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);

    private static bool TryNormalizeQuaternion(Quaternion value, out Quaternion normalized)
    {
        normalized = Quaternion.identity;
        if (!IsFinite(value)) return false;

        double lengthSquared = (double)value.x * value.x + (double)value.y * value.y +
            (double)value.z * value.z + (double)value.w * value.w;
        if (!double.IsFinite(lengthSquared) || lengthSquared < 1e-12d) return false;

        float inverseLength = (float)(1d / Math.Sqrt(lengthSquared));
        normalized = new Quaternion(
            value.x * inverseLength,
            value.y * inverseLength,
            value.z * inverseLength,
            value.w * inverseLength);
        return IsFinite(normalized);
    }

    private void ApplyMotionSeed(WheelbarrowMotionSnapshot snapshot, bool resetSequence)
    {
        if (physicsBody == null || !TryNormalizeQuaternion(snapshot.Rotation, out Quaternion normalizedRotation)) return;
        snapshot.Rotation = normalizedRotation;
        physicsBody.position = snapshot.Position;
        physicsBody.rotation = snapshot.Rotation;
        transform.SetPositionAndRotation(snapshot.Position, snapshot.Rotation);
        physicsBody.linearVelocity = snapshot.LinearVelocity;
        physicsBody.angularVelocity = snapshot.AngularVelocity;
        currentSteeringAngle = snapshot.SteeringAngle;
        wheelVisualSpinDegrees = snapshot.WheelSpinDegrees;
        wheelVisualSteerDegrees = snapshot.SteeringAngle;
        throttleInput = Mathf.Clamp(snapshot.ThrottleInput, -1f, 1f);
        steeringInput = Mathf.Clamp(snapshot.SteeringInput, -1f, 1f);
        previousWheelbarrowPosition = snapshot.Position;
        if (resetSequence) localMotionSequence = snapshot.Sequence;
        physicsBody.WakeUp();
    }

    private void ApplyAcceptedMotionToServerRoot()
    {
        if (!IsServer || HasLocalPhysicsAuthority || !hasAcceptedMotionSnapshot || physicsBody == null) return;
        physicsBody.position = lastAcceptedMotionSnapshot.Position;
        physicsBody.rotation = lastAcceptedMotionSnapshot.Rotation;
        transform.SetPositionAndRotation(lastAcceptedMotionSnapshot.Position, lastAcceptedMotionSnapshot.Rotation);
    }

    private void EvaluateRemoteTipping(WheelbarrowMotionSnapshot snapshot)
    {
        if (!IsServer || HasLocalPhysicsAuthority || State != WheelbarrowState.Driven) return;
        float angle = Vector3.Angle(snapshot.Rotation * Vector3.up, Vector3.up);
        if (angle < (profile != null ? profile.TippingAngle : 60f))
        {
            remoteTippingElapsed = 0f;
            return;
        }
        remoteTippingElapsed += 1f / (profile != null ? profile.MotionSnapshotRate : 50f);
        if (remoteTippingElapsed >= (profile != null ? profile.TippingDuration : 0.25f))
        {
            remoteTippingElapsed = 0f;
            TipOver();
        }
    }

    public bool TryValidateOwnerTransformRequest(
        Vector3 requestedPosition,
        Quaternion requestedRotation,
        out Vector3 acceptedPosition,
        out Quaternion acceptedRotation)
    {
        acceptedPosition = transform.position;
        acceptedRotation = transform.rotation;
        if (!IsServer || NetworkObject == null) return false;
        if (NetworkObject.OwnerClientId == NetworkManager.ServerClientId)
        {
            acceptedPosition = requestedPosition;
            acceptedRotation = requestedRotation;
            return true;
        }
        if (State != WheelbarrowState.Driven || DriverClientId != NetworkObject.OwnerClientId || !hasAcceptedMotionSnapshot)
            return false;

        float positionLimit = profile != null ? profile.MotionCorrectionPositionThreshold : 0.45f;
        float rotationLimit = profile != null ? profile.MotionCorrectionRotationThresholdDegrees : 15f;
        if (Vector3.Distance(lastAcceptedMotionSnapshot.Position, requestedPosition) > positionLimit ||
            Quaternion.Angle(lastAcceptedMotionSnapshot.Rotation, requestedRotation) > rotationLimit)
        {
            acceptedPosition = lastAcceptedMotionSnapshot.Position;
            acceptedRotation = lastAcceptedMotionSnapshot.Rotation;
            return false;
        }
        acceptedPosition = requestedPosition;
        acceptedRotation = requestedRotation;
        return true;
    }

    private void GrantDriverPhysicsAuthority(ulong clientId)
    {
        if (!IsSessionActive || !IsServer || NetworkObject == null) return;
        uint epoch = authorityEpochNetwork.Value + 1u;
        if (epoch == 0u) epoch = 1u;
        authorityEpochNetwork.Value = epoch;
        localMotionSequence = 0;
        lastAcceptedMotionSequence = 0;
        WheelbarrowAuthorityPhysicsSeed seed = CaptureAuthorityPhysicsSeed(epoch);
        if (profile != null && profile.EnableDiagnostics)
            Debug.Log($"[Wheelbarrow] Authority seed client={clientId} epoch={epoch} mass={seed.TotalMass:F2} " +
                $"com={seed.LocalCenterOfMass} support={seed.DriverSupportLoadShare:F2}/{seed.WheelLoadShare:F2}.", this);
        lastAcceptedMotionSnapshot = seed.Motion;
        hasAcceptedMotionSnapshot = true;
        lastAcceptedMotionReceiveTime = seed.Motion.ServerTimestamp;

        if (clientId != NetworkManager.ServerClientId)
        {
            pendingAuthorityGrantClient = clientId;
            pendingAuthorityGrantEpoch = epoch;
            pendingAuthorityGrantDeadline = Time.unscaledTime + 1f;
            ConfigureBody();
            PrepareDriverAuthorityClientRpc(seed, Target(clientId));
        }
        else if (NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }
        ResetMotionStream();
    }

    [ClientRpc]
    private void PrepareDriverAuthorityClientRpc(WheelbarrowAuthorityPhysicsSeed seed, ClientRpcParams rpc = default)
    {
        if (seed.Motion.AuthorityEpoch < AuthorityEpoch) return;
        pendingAuthoritySeed = seed;
        hasPendingAuthoritySeed = true;
        pendingAuthorityPreparationAck = true;
        ConfigureBody();
        ApplyAuthorityPhysicsSeed(seed, false);
        TryConfirmDriverAuthorityPrepared();
    }

    private void TryConfirmDriverAuthorityPrepared()
    {
        if (!pendingAuthorityPreparationAck || IsServer || !hasPendingAuthoritySeed) return;
        if (PassengerClientId != NoClient &&
            !SetPassengerTransportCollisionState(PassengerClientId, true, false)) return;

        pendingAuthorityPreparationAck = false;
        ConfirmDriverAuthorityPreparedServerRpc(pendingAuthoritySeed.Motion.AuthorityEpoch);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConfirmDriverAuthorityPreparedServerRpc(uint epoch, ServerRpcParams rpc = default)
    {
        ulong sender = rpc.Receive.SenderClientId;
        if (!IsServer || NetworkObject == null || pendingAuthorityGrantClient != sender ||
            pendingAuthorityGrantEpoch != epoch || AuthorityEpoch != epoch ||
            DriverClientId != sender || State != WheelbarrowState.Driven ||
            NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            return;

        pendingAuthorityGrantClient = NoClient;
        pendingAuthorityGrantEpoch = 0;
        pendingAuthorityGrantDeadline = 0f;
        NetworkObject.ChangeOwnership(sender);
        ConfigureBody();
    }

    private void ProcessPendingPhysicsAuthorityGrant()
    {
        if (!IsServer || pendingAuthorityGrantClient == NoClient ||
            Time.unscaledTime <= pendingAuthorityGrantDeadline) return;

        ulong clientId = pendingAuthorityGrantClient;
        pendingAuthorityGrantClient = NoClient;
        pendingAuthorityGrantEpoch = 0;
        pendingAuthorityGrantDeadline = 0f;
        if (DriverClientId != clientId || State != WheelbarrowState.Driven) return;

        SetDriver(NoClient);
        SetState(WheelbarrowState.Free);
        ConfigureBody();
        BeginSafeExit(clientId);
    }

    private void ReclaimPhysicsAuthority(bool preserveVelocity)
    {
        if (!IsSessionActive || !IsServer || NetworkObject == null) return;
        pendingAuthorityGrantClient = NoClient;
        pendingAuthorityGrantEpoch = 0;
        pendingAuthorityGrantDeadline = 0f;
        ulong previousOwner = NetworkObject.OwnerClientId;
        WheelbarrowMotionSnapshot seed = hasAcceptedMotionSnapshot
            ? lastAcceptedMotionSnapshot
            : CaptureMotionSnapshot(0);
        if (!preserveVelocity)
        {
            seed.LinearVelocity = Vector3.zero;
            seed.AngularVelocity = Vector3.zero;
            seed.ThrottleInput = 0f;
            seed.SteeringInput = 0f;
        }
        uint epoch = authorityEpochNetwork.Value + 1u;
        if (epoch == 0u) epoch = 1u;
        authorityEpochNetwork.Value = epoch;
        seed.AuthorityEpoch = epoch;
        seed.Sequence = 0;
        seed.ServerTimestamp = NetworkManager.ServerTime.Time;
        lastAcceptedMotionSnapshot = seed;
        hasAcceptedMotionSnapshot = true;
        lastAcceptedMotionSequence = 0;
        lastAcceptedMotionReceiveTime = seed.ServerTimestamp;

        if (previousOwner != NetworkManager.ServerClientId)
        {
            EndDriverAuthorityClientRpc(seed, Target(previousOwner));
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }
        ConfigureBody();
        ApplyMotionSeed(seed, true);
        ResetMotionStream();
    }

    [ClientRpc]
    private void EndDriverAuthorityClientRpc(WheelbarrowMotionSnapshot seed, ClientRpcParams rpc = default)
    {
        if (seed.AuthorityEpoch < AuthorityEpoch) return;
        ApplyMotionSeed(seed, true);
        presentationController?.ResetPresentation();
    }

    private void Update()
    {
        if (concreteCargoVisual != null) concreteCargoVisual.SetActive(HasPourableConcrete);
        if (spillVisual != null && spillVisual.activeSelf && Time.time >= spillVisualUntil) spillVisual.SetActive(false);
    }

    private void ConfigureBody()
    {
        if (physicsBody == null) return;
        bool controlledKinematicState = IsDockSecured || State == WheelbarrowState.Righting;
        bool awaitingRemotePhysicsOwner = IsServer && pendingAuthorityGrantClient != NoClient;
        physicsBody.isKinematic = controlledKinematicState || awaitingRemotePhysicsOwner ||
            IsSessionActive && !HasLocalPhysicsAuthority;
        physicsBody.useGravity = !physicsBody.isKinematic;
        physicsBody.interpolation = physicsBody.isKinematic
            ? RigidbodyInterpolation.None
            : RigidbodyInterpolation.Interpolate;
        physicsBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        massPropertiesDirty = true;
    }

    private void ConfigureAutomaticBoardingTrigger()
    {
        automaticBoardingTrigger ??= transform.Find("FrontBoardingTrigger")?.GetComponent<BoxCollider>();
        if (automaticBoardingTrigger == null) return;
        float lead = profile != null ? profile.AutomaticBoardingLeadDistance : 0.6f;
        Vector3 size = automaticBoardingTrigger.size;
        size.z = Mathf.Max(0.1f, lead);
        automaticBoardingTrigger.size = size;
        Vector3 localPosition = automaticBoardingTrigger.transform.localPosition;
        localPosition.z = 0.9f + size.z * 0.5f;
        automaticBoardingTrigger.transform.localPosition = localPosition;
    }

    private void FixedUpdate()
    {
        EnsureReplicatedOccupantCollisions();
        TryConfirmDriverAuthorityPrepared();
        if (physicsBody == null) return;
        UpdateRopeTowLifecycle();

        if (HasAuthority)
        {
            ProcessPendingPhysicsAuthorityGrant();
            ProcessPendingPassengerBoarding();
            ProcessPendingSafeExits();
        }

        if (IsServer && pendingAuthorityGrantClient != NoClient)
        {
            ConfigureWheelContactMode();
            UpdateNavigationObstacle();
            return;
        }

        if (!HasLocalPhysicsAuthority)
        {
            if (IsServer) ApplyAcceptedMotionToServerRoot();
            ConfigureWheelContactMode();
            UpdateNavigationObstacle();
            return;
        }
        RefreshMassAndCenterOfMass();
        ConfigureWheelContactMode();
        UpdateCargoTransforms();
        if (State == WheelbarrowState.Docked)
        {
            if (!physicsBody.isKinematic) SetDockSecured(true);
            RestoreSecuredDockPose();
        }
        else if (State == WheelbarrowState.Righting) SimulateRighting();
        else if (State == WheelbarrowState.Driven) SimulateDrive();
        else if (State == WheelbarrowState.Tipped) ApplyTippedDamping();
        else if (State != WheelbarrowState.Docked && State != WheelbarrowState.Pouring &&
                 State != WheelbarrowState.TrappedInFailedConcrete && !IsRopeTowActive) ApplyIdleBrake();
        UpdateWheelVisual();
        if (HasAuthority) DetectTipping();
        UpdateNavigationObstacle();
        PublishMotionIfDue(Time.fixedDeltaTime);
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
            ClearDrivenWheelContact(DrivenWheelContactRejection.NoHit, null);
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
        float suspensionDistance = profile != null ? profile.WheelSuspensionDistance : 0.03f;
        Vector3 wheelCenter = transform.TransformPoint(wheelVisualRootLocalPosition);
        Vector3 castUp = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        if (!TryResolveDrivenWheelContact(
                wheelCenter,
                castUp,
                radius,
                probeDistance,
                suspensionDistance,
                out DrivenWheelContactSample sample,
                out DrivenWheelContactRejection rejection,
                out Collider rejectedCollider))
        {
            ClearDrivenWheelContact(rejection, rejectedCollider);
            return;
        }

        drivenWheelHit = sample.Hit;
        drivenWheelGrounded = true;
        drivenWheelContactSource = sample.Source;
        LogDrivenWheelContactTransition(sample.Source, rejection, sample.Hit.collider);

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
            float previousHeight = Vector3.Dot(filteredWheelContactPoint, castUp);
            float rawHeight = Vector3.Dot(drivenWheelHit.point, castUp);
            float filteredHeight = Mathf.Lerp(previousHeight, rawHeight, heightFilter);
            filteredWheelContactPoint = drivenWheelHit.point + castUp * (filteredHeight - rawHeight);
        }
        wheelSuspensionError = sample.SuspensionError;
        if (!IsCurrentDrivenWheelContactUsable())
            ClearDrivenWheelContact(DrivenWheelContactRejection.PointTooFar, drivenWheelHit.collider);
    }

    private bool TryResolveDrivenWheelContact(
        Vector3 wheelCenter,
        Vector3 castUp,
        float radius,
        float probeDistance,
        float suspensionDistance,
        out DrivenWheelContactSample sample,
        out DrivenWheelContactRejection rejection,
        out Collider rejectedCollider)
    {
        sample = default;
        rejection = DrivenWheelContactRejection.NoHit;
        rejectedCollider = null;
        ResolveWheelContactIgnoredRoots(out Transform driverRoot, out Transform passengerRoot);

        Vector3 origin = wheelCenter + castUp * probeDistance;
        int sphereCount = Physics.SphereCastNonAlloc(
            origin,
            radius,
            -castUp,
            wheelContactHits,
            probeDistance + suspensionDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        bool foundSphereContact = false;
        for (int i = 0; i < sphereCount; i++)
        {
            RaycastHit hit = wheelContactHits[i];
            if (!TryValidateDrivenWheelContact(
                    hit,
                    wheelCenter,
                    castUp,
                    radius,
                    suspensionDistance,
                    driverRoot,
                    passengerRoot,
                    out DrivenWheelContactRejection hitRejection))
            {
                rejection = hitRejection;
                rejectedCollider = hit.collider;
                continue;
            }
            if (hit.distance >= nearestDistance) continue;

            nearestDistance = hit.distance;
            sample.Hit = hit;
            sample.SuspensionError = Mathf.Clamp(
                probeDistance - hit.distance,
                -suspensionDistance,
                suspensionDistance);
            sample.Source = DrivenWheelContactSource.SphereCast;
            foundSphereContact = true;
        }
        if (foundSphereContact)
        {
            rejection = DrivenWheelContactRejection.None;
            rejectedCollider = null;
            return true;
        }

        DrivenWheelContactRejection sphereRejection = rejection;
        Collider sphereRejectedCollider = rejectedCollider;
        float validationMargin = profile != null ? profile.WheelContactValidationMargin : 0.05f;
        int rayCount = Physics.RaycastNonAlloc(
            origin,
            -castUp,
            wheelContactRayHits,
            radius + probeDistance + suspensionDistance + validationMargin,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        nearestDistance = float.PositiveInfinity;
        bool foundRayContact = false;
        for (int i = 0; i < rayCount; i++)
        {
            RaycastHit hit = wheelContactRayHits[i];
            if (!TryValidateDrivenWheelContact(
                    hit,
                    wheelCenter,
                    castUp,
                    radius,
                    suspensionDistance,
                    driverRoot,
                    passengerRoot,
                    out DrivenWheelContactRejection hitRejection))
            {
                rejection = hitRejection;
                rejectedCollider = hit.collider;
                continue;
            }

            float verticalDistance = Vector3.Dot(wheelCenter - hit.point, castUp);
            if (verticalDistance < -validationMargin ||
                verticalDistance > radius + suspensionDistance + validationMargin)
            {
                rejection = verticalDistance < -validationMargin
                    ? DrivenWheelContactRejection.SurfaceAboveWheel
                    : DrivenWheelContactRejection.PointTooFar;
                rejectedCollider = hit.collider;
                continue;
            }
            if (hit.distance >= nearestDistance) continue;

            nearestDistance = hit.distance;
            sample.Hit = hit;
            sample.SuspensionError = Mathf.Clamp(
                radius - verticalDistance,
                -suspensionDistance,
                suspensionDistance);
            sample.Source = DrivenWheelContactSource.RaycastFallback;
            foundRayContact = true;
        }
        if (!foundRayContact) return false;

        rejection = sphereRejection;
        rejectedCollider = sphereRejectedCollider;
        return true;
    }

    private bool TryValidateDrivenWheelContact(
        RaycastHit hit,
        Vector3 wheelCenter,
        Vector3 castUp,
        float radius,
        float suspensionDistance,
        Transform driverRoot,
        Transform passengerRoot,
        out DrivenWheelContactRejection rejection)
    {
        Collider hitCollider = hit.collider;
        if (hitCollider == null || IsWheelContactColliderIgnored(hitCollider, driverRoot, passengerRoot))
        {
            rejection = DrivenWheelContactRejection.IgnoredCollider;
            return false;
        }
        if (hit.distance <= WheelContactDistanceEpsilon)
        {
            rejection = DrivenWheelContactRejection.InitialOverlap;
            return false;
        }
        if (!IsFinite(hit.point) || hit.point.sqrMagnitude <= WheelContactDistanceEpsilon * WheelContactDistanceEpsilon)
        {
            rejection = DrivenWheelContactRejection.InvalidPoint;
            return false;
        }
        if (!IsFinite(hit.normal) || hit.normal.sqrMagnitude <= 0.0001f)
        {
            rejection = DrivenWheelContactRejection.InvalidNormal;
            return false;
        }

        float minimumGroundDot = profile != null ? profile.MinimumWheelGroundNormalDot : 0.25f;
        if (Vector3.Dot(hit.normal.normalized, castUp) < minimumGroundDot)
        {
            rejection = DrivenWheelContactRejection.InvalidNormal;
            return false;
        }

        float validationMargin = profile != null ? profile.WheelContactValidationMargin : 0.05f;
        if (Vector3.Dot(hit.point - wheelCenter, castUp) > validationMargin)
        {
            rejection = DrivenWheelContactRejection.SurfaceAboveWheel;
            return false;
        }
        if (Vector3.Distance(hit.point, wheelCenter) > radius + suspensionDistance + validationMargin)
        {
            rejection = DrivenWheelContactRejection.PointTooFar;
            return false;
        }

        rejection = DrivenWheelContactRejection.None;
        return true;
    }

    private bool IsCurrentDrivenWheelContactUsable()
    {
        if (!drivenWheelGrounded || drivenWheelHit.collider == null ||
            !IsFinite(filteredWheelContactPoint) ||
            filteredWheelContactPoint.sqrMagnitude <= WheelContactDistanceEpsilon * WheelContactDistanceEpsilon ||
            !IsFinite(filteredGroundNormal) || filteredGroundNormal.sqrMagnitude <= 0.0001f)
            return false;

        ResolveWheelContactIgnoredRoots(out Transform driverRoot, out Transform passengerRoot);
        if (IsWheelContactColliderIgnored(drivenWheelHit.collider, driverRoot, passengerRoot)) return false;

        float radius = profile != null ? profile.WheelRadius : wheelVisualRadius;
        float suspensionDistance = profile != null ? profile.WheelSuspensionDistance : 0.03f;
        float validationMargin = profile != null ? profile.WheelContactValidationMargin : 0.05f;
        Vector3 wheelCenter = transform.TransformPoint(wheelVisualRootLocalPosition);
        Vector3 castUp = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        if (Vector3.Dot(filteredGroundNormal.normalized, castUp) <
            (profile != null ? profile.MinimumWheelGroundNormalDot : 0.25f)) return false;
        if (Vector3.Dot(filteredWheelContactPoint - wheelCenter, castUp) > validationMargin) return false;
        return Vector3.Distance(filteredWheelContactPoint, wheelCenter) <=
            radius + suspensionDistance + validationMargin;
    }

    private void ClearDrivenWheelContact(DrivenWheelContactRejection rejection, Collider rejectedCollider)
    {
        drivenWheelGrounded = false;
        drivenWheelHit = default;
        drivenWheelContactSource = DrivenWheelContactSource.None;
        drivenContactInitialized = false;
        filteredWheelContactPoint = Vector3.zero;
        filteredGroundNormal = Vector3.up;
        wheelSuspensionError = 0f;
        wheelSupportAcceleration = 0f;
        longitudinalAcceleration = 0f;
        residualLateralSpeed = 0f;
        targetYawRate = 0f;
        currentYawAcceleration = 0f;
        LogDrivenWheelContactTransition(DrivenWheelContactSource.None, rejection, rejectedCollider);
    }

    private void ResolveWheelContactIgnoredRoots(out Transform driverRoot, out Transform passengerRoot)
    {
        driverRoot = DriverClientId != NoClient && TryFindPlayerOnPeer(DriverClientId, out NetworkObject driver)
            ? driver.transform
            : null;
        passengerRoot = PassengerClientId != NoClient && TryFindPlayerOnPeer(PassengerClientId, out NetworkObject passenger)
            ? passenger.transform
            : null;
#if UNITY_EDITOR
        if (passengerRoot == null && editorProbePassengerRoot != null)
            passengerRoot = editorProbePassengerRoot;
#endif
    }

    private bool IsWheelContactColliderIgnored(Collider collider, Transform driverRoot, Transform passengerRoot)
    {
        if (collider == null) return true;
        Transform candidate = collider.transform;
        return candidate == transform || candidate.IsChildOf(transform) ||
            driverRoot != null && (candidate == driverRoot || candidate.IsChildOf(driverRoot)) ||
            passengerRoot != null && (candidate == passengerRoot || candidate.IsChildOf(passengerRoot));
    }

    private void LogDrivenWheelContactTransition(
        DrivenWheelContactSource source,
        DrivenWheelContactRejection rejection,
        Collider collider)
    {
        if (profile == null || !profile.EnableDiagnostics) return;
        if (lastLoggedWheelContactSource == source &&
            lastLoggedWheelContactRejection == rejection &&
            lastLoggedWheelContactCollider == collider) return;

        lastLoggedWheelContactSource = source;
        lastLoggedWheelContactRejection = rejection;
        lastLoggedWheelContactCollider = collider;
        string colliderName = collider != null ? collider.name : "none";
        Debug.Log(
            $"[Wheelbarrow] Wheel contact source={source}, collider={colliderName}, rejection={rejection}.",
            this);
    }

    private void ApplyDrivenWheelSupport()
    {
        if (!IsCurrentDrivenWheelContactUsable())
        {
            if (drivenWheelGrounded)
                ClearDrivenWheelContact(DrivenWheelContactRejection.InvalidPoint, drivenWheelHit.collider);
            return;
        }
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
        if (!IsCurrentDrivenWheelContactUsable())
        {
            if (drivenWheelGrounded)
                ClearDrivenWheelContact(DrivenWheelContactRejection.InvalidPoint, drivenWheelHit.collider);
            return;
        }

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
        ResolveWheelContactIgnoredRoots(out Transform driverRoot, out Transform passengerRoot);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = driverSupportGroundHits[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null || hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform)) continue;
            if (driverRoot != null &&
                (hitCollider.transform == driverRoot || hitCollider.transform.IsChildOf(driverRoot))) continue;
            if (passengerRoot != null &&
                (hitCollider.transform == passengerRoot || hitCollider.transform.IsChildOf(passengerRoot))) continue;
            if (hit.distance <= WheelContactDistanceEpsilon || !IsFinite(hit.point) ||
                hit.point.sqrMagnitude <= WheelContactDistanceEpsilon * WheelContactDistanceEpsilon ||
                !IsFinite(hit.normal) || hit.normal.sqrMagnitude <= 0.0001f ||
                Vector3.Dot(hit.normal.normalized, Vector3.up) <
                (profile != null ? profile.MinimumWheelGroundNormalDot : 0.25f)) continue;
            if (hit.distance >= nearestDistance) continue;
            nearestDistance = hit.distance;
            height = hit.point.y;
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
        if (!IsCurrentDrivenWheelContactUsable())
        {
            targetYawRate = 0f;
            currentYawAcceleration = 0f;
            return;
        }

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
        if (!IsCurrentDrivenWheelContactUsable())
        {
            if (drivenWheelGrounded)
                ClearDrivenWheelContact(DrivenWheelContactRejection.InvalidPoint, drivenWheelHit.collider);
            return;
        }

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
        bool secured = state == WheelbarrowState.Docked || state == WheelbarrowState.Pouring ||
                       state == WheelbarrowState.TrappedInFailedConcrete;
        bool canBecomeStationaryObstacle = state == WheelbarrowState.Free || state == WheelbarrowState.Tipped;
        bool shouldEnable = secured || canBecomeStationaryObstacle;

        if (isRopeTowActive)
        {
            navObstacleSettledTime = 0f;
            if (force || navigationObstacle.carving) navigationObstacle.carving = false;
            if (force || navigationObstacle.enabled != shouldEnable) navigationObstacle.enabled = shouldEnable;
            return;
        }

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
        massPropertiesDirty = true;
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
        drivenWheelHit = default;
        drivenWheelContactSource = DrivenWheelContactSource.None;
        drivenContactInitialized = false;
        filteredWheelContactPoint = Vector3.zero;
        filteredGroundNormal = Vector3.up;
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
        if (IsServer) ReclaimPhysicsAuthority(true);
        ulong previousDriver = DriverClientId;
        tippedRestUp = ResolveTippedRestUp();
        SetState(WheelbarrowState.Tipped);
        if (previousDriver != NoClient) BeginSafeExit(previousDriver, false, true);
        bool trappedPassenger = HasHardenedPassengerConcrete;
        ReleasePassenger(true);
        SpillAllCargo();
        if (!trappedPassenger) SpillConcrete();
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
        if (!HasLocalPhysicsAuthority || PassengerClientId != NoClient || pendingPassengerBoarding != null ||
            Speed < (profile != null ? profile.AutomaticBoardingMinimumSpeed : 1.5f) || other == null) return;
        NetworkObject player = other.GetComponentInParent<NetworkObject>();
        if (player == null || player.GetComponent<PlayerInteractionNew>() == null) return;
        Vector3 toPlayer = (player.transform.position - transform.position).normalized;
        if (Vector3.Dot(transform.forward, toPlayer) < (profile != null ? profile.AutomaticBoardingDirectionDot : 0.65f)) return;
        if (IsSessionActive && !IsServer)
            RequestAutomaticBoardingServerRpc(player.NetworkObjectId);
        else
            BeginPassengerBoarding(player.OwnerClientId, true);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestAutomaticBoardingServerRpc(ulong playerNetworkObjectId, ServerRpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId != DriverClientId || State != WheelbarrowState.Driven ||
            !TryGetNetworkObject(playerNetworkObjectId, out NetworkObject player) ||
            player.GetComponent<PlayerInteractionNew>() == null ||
            Speed < (profile != null ? profile.AutomaticBoardingMinimumSpeed : 1.5f)) return;
        Vector3 toPlayer = (player.transform.position - transform.position).normalized;
        if (Vector3.Dot(transform.forward, toPlayer) < (profile != null ? profile.AutomaticBoardingDirectionDot : 0.65f)) return;
        BeginPassengerBoarding(player.OwnerClientId, true);
    }

    public void SubmitDriveInput(float throttle, float steering, ulong senderClientId)
    {
        if (!HasLocalPhysicsAuthority || DriverClientId != senderClientId || State != WheelbarrowState.Driven) return;
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

    public void SetLocalPresentationInput(float throttle, float steering)
    {
        presentationController?.SetLocalDriveInput(throttle, steering);
    }

    public bool TryGetLocalDriverPresentationPose(ulong clientId, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        return DriverClientId == clientId && presentationController != null &&
            presentationController.TryGetDriverAnchorPose(out position, out rotation);
    }

    public bool TryGetPresentedAnchorPose(Transform anchor, out Vector3 position, out Quaternion rotation)
    {
        if (presentationController != null &&
            presentationController.TryGetPresentedAnchorPose(anchor, out position, out rotation))
            return true;
        position = anchor != null ? anchor.position : transform.position;
        rotation = anchor != null ? anchor.rotation : transform.rotation;
        return anchor != null;
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
                {
                    if (Physics.GetIgnoreCollision(playerCollider, item) != ignored)
                        Physics.IgnoreCollision(playerCollider, item, ignored);
                }
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
            if (ignored) locallyAppliedCollisionIgnores.Add(clientId);
            else locallyAppliedCollisionIgnores.Remove(clientId);
        }
        else if (!ignored) locallyAppliedCollisionIgnores.Remove(clientId);

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
        if (pendingPassengerBoarding != null) CancelPendingPassengerBoarding(false);
        foreach (ulong clientId in collisionIgnoredPlayers.ToArray())
            SetOccupantCollisionIgnored(clientId, false, broadcast);
        if (PassengerClientId != NoClient)
            SetPassengerTransportCollisionState(PassengerClientId, false, broadcast);
        pendingSafeExits.Clear();
    }

    private void RestoreReplicatedOccupantCollisions()
    {
        if (DriverClientId != NoClient) SetOccupantCollisionIgnored(DriverClientId, true, false);
        if (PassengerClientId != NoClient) SetPassengerTransportCollisionState(PassengerClientId, true, false);
    }

    private void EnsureReplicatedOccupantCollisions()
    {
        foreach (ulong clientId in collisionIgnoredPlayers)
        {
            if (!TryFindPlayerOnPeer(clientId, out NetworkObject player)) continue;
            SetPlayerCollisionIgnored(player.transform, true);
            locallyAppliedCollisionIgnores.Add(clientId);
        }
        if (PassengerClientId != NoClient)
            SetPassengerTransportCollisionState(PassengerClientId, true, false);
    }

    private bool SetPassengerTransportCollisionState(ulong clientId, bool active, bool broadcast = true)
    {
        if (clientId == NoClient) return false;
        bool applied = false;
        if (TryFindPlayerOnPeer(clientId, out NetworkObject player))
        {
            if (active)
            {
                collisionIgnoredPlayers.Remove(clientId);
                locallyAppliedCollisionIgnores.Remove(clientId);
                SetPlayerCollisionIgnored(player.transform, false);
            }
            PlayerWheelbarrowController controller = player.GetComponent<PlayerWheelbarrowController>();
            applied = controller != null && controller.SetPassengerTransportCollisionState(this, active);
        }
        if (broadcast && IsSessionActive && IsServer)
            SetPassengerTransportCollisionStateClientRpc(clientId, active);
        return applied;
    }

    [ClientRpc]
    private void SetPassengerTransportCollisionStateClientRpc(ulong clientId, bool active)
    {
        if (IsServer) return;
        SetPassengerTransportCollisionState(clientId, active, false);
    }

    private void OnDriverChanged(ulong previous, ulong current)
    {
        localDriver = current;
        if (current != NoClient) SetOccupantCollisionIgnored(current, true, false);
    }

    private void OnPassengerChanged(ulong previous, ulong current)
    {
        localPassenger = current;
        massPropertiesDirty = true;
        if (current != NoClient) SetPassengerTransportCollisionState(current, true, false);
    }

    private void OnAuthorityEpochChanged(uint previous, uint current)
    {
        if (hasPendingAuthoritySeed && pendingAuthoritySeed.Motion.AuthorityEpoch == current && HasLocalPhysicsAuthority)
        {
            ConfigureBody();
            ApplyAuthorityPhysicsSeed(pendingAuthoritySeed, true);
            hasPendingAuthoritySeed = false;
            driveContactWarmupStepsRemaining = Mathf.Max(1, profile != null ? profile.WheelContactWarmupFixedSteps : 1);
        }
        presentationController?.ResetPresentation();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        if (pendingPassengerBoarding != null && pendingPassengerBoarding.ClientId == clientId)
            CancelPendingPassengerBoarding(false);
        if (PassengerClientId == clientId)
        {
            if (TryGetPassengerConcreteTrap(out PlayerConcreteTrapController trap) ||
                PlayerConcreteTrapController.TryFindForOwner(clientId, out trap))
                trap.ClearForDisconnect(this);
            SetPassenger(NoClient);
            SetPassengerTransportCollisionState(clientId, false);
        }
        if (DriverClientId == clientId)
        {
            ReclaimPhysicsAuthority(true);
            SetDriver(NoClient);
            if (State == WheelbarrowState.Driven) SetState(WheelbarrowState.Free);
            SetOccupantCollisionIgnored(clientId, false);
        }
    }

    public bool RequestEnterDriver(Transform interactor)
    {
        return RequestRole(interactor, WheelbarrowOccupantRole.Driver);
    }

#if UNITY_EDITOR
    private int editorProbeResourceCount = -1;
    private Transform editorProbePassengerRoot;
    private PlayerConcreteTrapController editorProbePassengerTrap;

    public void SetEditorConcreteTrapProbePassenger(PlayerConcreteTrapController trap)
    {
        editorProbePassengerTrap = trap;
    }

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

    public bool RunEditorWheelContactQueryProbe(out string result)
    {
        UpdateDrivenWheelContact();
        string colliderName = drivenWheelHit.collider != null ? drivenWheelHit.collider.name : "none";
        bool finitePoint = IsFinite(filteredWheelContactPoint);
        bool nonZeroPoint = filteredWheelContactPoint.sqrMagnitude >
            WheelContactDistanceEpsilon * WheelContactDistanceEpsilon;
        bool usable = IsCurrentDrivenWheelContactUsable();
        bool passed = drivenWheelGrounded && finitePoint && nonZeroPoint && usable;
        result = $"grounded={drivenWheelGrounded}, source={drivenWheelContactSource}, " +
            $"collider={colliderName}, point={filteredWheelContactPoint}, finite={finitePoint}, " +
            $"nonZero={nonZeroPoint}, usable={usable}, error={wheelSuspensionError:F4}";
        return passed;
    }

    public void SetEditorWheelContactIgnoredPassenger(Transform passengerRoot)
    {
        editorProbePassengerRoot = passengerRoot;
    }

    public bool RunEditorPassengerMassTransitionProbe(out string result)
    {
        SetConcreteLoads(0);
        SetPassenger(123u);
        massPropertiesDirty = true;
        RefreshMassAndCenterOfMass();
        float expectedMass = (profile != null ? profile.BaseMass : 22f) +
            (profile != null ? profile.PassengerMass : 75f);
        float seededMass = physicsBody.mass;
        Vector3 seededCenterOfMass = physicsBody.centerOfMass;
        ConfigureBody();
        float immediateAfterConfigure = physicsBody.mass;
        RefreshMassAndCenterOfMass();
        float afterRefresh = physicsBody.mass;
        bool passed = Mathf.Abs(seededMass - expectedMass) < 0.01f &&
            Mathf.Abs(immediateAfterConfigure - expectedMass) < 0.01f &&
            Mathf.Abs(afterRefresh - expectedMass) < 0.01f &&
            Vector3.Distance(seededCenterOfMass, physicsBody.centerOfMass) < 0.001f;
        result = $"expected={expectedMass:F2}, seeded={seededMass:F2}, " +
            $"immediateAfterConfigure={immediateAfterConfigure:F2}, afterRefresh={afterRefresh:F2}, " +
            $"com={physicsBody.centerOfMass}, passed={passed}";
        return passed;
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
            if (DriverClientId != NoClient || State == WheelbarrowState.Pouring ||
                State == WheelbarrowState.Righting || State == WheelbarrowState.Tipped ||
                State == WheelbarrowState.TrappedInFailedConcrete) return false;
            RopeToolController.TryRetractAttachedTarget(NetworkObject);
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
            GrantDriverPhysicsAuthority(clientId);
            return true;
        }
        if (role == WheelbarrowOccupantRole.Passenger)
        {
            return BeginPassengerBoarding(clientId, false);
        }
        return false;
    }

    private bool BeginPassengerBoarding(ulong clientId, bool automatic)
    {
        if (!HasAuthority || State == WheelbarrowState.TrappedInFailedConcrete ||
            clientId == DriverClientId || PassengerClientId != NoClient || pendingPassengerBoarding != null ||
            !TryGetPlayer(clientId, out NetworkObject player)) return false;

        if (!automatic && passengerAnchor != null &&
            Vector3.Distance(player.transform.position, passengerAnchor.position) > 3f) return false;
        if (!IsPassengerAnchorClear(player)) return false;

        pendingSafeExits.Remove(clientId);
        uint token = ++nextPassengerBoardingToken;
        if (token == 0) token = ++nextPassengerBoardingToken;
        pendingPassengerBoarding = new PendingPassengerBoarding
        {
            ClientId = clientId,
            Token = token,
            StartedAt = Time.time,
            Automatic = automatic,
            StartedDowned = player.GetComponent<PlayerHealth>() is PlayerHealth boardingHealth && boardingHealth.IsDowned,
            VelocityBefore = physicsBody != null ? physicsBody.linearVelocity : Vector3.zero,
            AngularVelocityBefore = physicsBody != null ? physicsBody.angularVelocity : Vector3.zero,
            PhysicsOwnerClientId = IsSessionActive && NetworkObject != null
                ? NetworkObject.OwnerClientId
                : NoClient
        };

        SetPassengerTransportCollisionState(clientId, true, false);
        float duration = profile != null ? profile.PassengerPlacementDuration : 0.2f;
        if (!IsSessionActive)
        {
            PlayerWheelbarrowController controller = player.GetComponent<PlayerWheelbarrowController>();
            if (controller == null || !controller.PreparePassengerBoarding(this, token, duration))
            {
                CancelPendingPassengerBoarding(false);
                return false;
            }
            pendingPassengerBoarding.PassengerOwnerReady = true;
            pendingPassengerBoarding.PhysicsOwnerReady = true;
            TryCompletePassengerBoarding();
            return true;
        }

        if (clientId == NetworkManager.ServerClientId)
        {
            PlayerWheelbarrowController controller = player.GetComponent<PlayerWheelbarrowController>();
            pendingPassengerBoarding.PassengerOwnerReady = controller != null &&
                controller.PreparePassengerBoarding(this, token, duration);
        }
        else
        {
            PreparePassengerBoardingClientRpc(token, duration, Target(clientId));
        }

        ulong physicsOwner = pendingPassengerBoarding.PhysicsOwnerClientId;
        if (physicsOwner == NetworkManager.ServerClientId)
        {
            pendingPassengerBoarding.PhysicsOwnerReady = SetPassengerTransportCollisionState(clientId, true, false);
        }
        else
        {
            PreparePassengerPhysicsOwnerClientRpc(clientId, token, Target(physicsOwner));
        }
        TryCompletePassengerBoarding();
        if (profile != null && profile.EnableDiagnostics)
            Debug.Log($"[Wheelbarrow] Passenger boarding prepared ({(automatic ? "automatic" : "manual")}) client={clientId} token={token} speed={Speed:F2}.", this);
        return true;
    }

    [ClientRpc]
    private void PreparePassengerBoardingClientRpc(uint token, float duration, ClientRpcParams rpc = default)
    {
        NetworkObject player = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        PlayerWheelbarrowController controller = player != null
            ? player.GetComponent<PlayerWheelbarrowController>()
            : null;
        if (controller != null && controller.PreparePassengerBoarding(this, token, duration))
            ConfirmPassengerOwnerBoardingReadyServerRpc(token);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConfirmPassengerOwnerBoardingReadyServerRpc(uint token, ServerRpcParams rpc = default)
    {
        if (pendingPassengerBoarding == null || pendingPassengerBoarding.Token != token ||
            pendingPassengerBoarding.ClientId != rpc.Receive.SenderClientId) return;
        pendingPassengerBoarding.PassengerOwnerReady = true;
        TryCompletePassengerBoarding();
    }

    [ClientRpc]
    private void PreparePassengerPhysicsOwnerClientRpc(ulong passengerClientId, uint token, ClientRpcParams rpc = default)
    {
        if (SetPassengerTransportCollisionState(passengerClientId, true, false))
            ConfirmPassengerPhysicsOwnerReadyServerRpc(passengerClientId, token);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConfirmPassengerPhysicsOwnerReadyServerRpc(ulong passengerClientId, uint token, ServerRpcParams rpc = default)
    {
        if (pendingPassengerBoarding == null || pendingPassengerBoarding.Token != token ||
            pendingPassengerBoarding.ClientId != passengerClientId ||
            pendingPassengerBoarding.PhysicsOwnerClientId != rpc.Receive.SenderClientId) return;
        pendingPassengerBoarding.PhysicsOwnerReady = true;
        TryCompletePassengerBoarding();
    }

    private void TryCompletePassengerBoarding()
    {
        if (!HasAuthority || pendingPassengerBoarding == null || PassengerClientId != NoClient ||
            !pendingPassengerBoarding.PassengerOwnerReady || !pendingPassengerBoarding.PhysicsOwnerReady) return;
        if (!IsPendingPassengerStillValid())
        {
            CancelPendingPassengerBoarding(true);
            return;
        }

        bool automatic = pendingPassengerBoarding.Automatic;
        ulong clientId = pendingPassengerBoarding.ClientId;
        Vector3 velocityBefore = pendingPassengerBoarding.VelocityBefore;
        Vector3 angularVelocityBefore = pendingPassengerBoarding.AngularVelocityBefore;
        pendingPassengerBoarding = null;
        SetPassenger(clientId);
        SetPassengerTransportCollisionState(clientId, true);
        if (profile != null && profile.EnableDiagnostics)
            Debug.Log($"[Wheelbarrow] Passenger boarding committed ({(automatic ? "automatic" : "manual")}) client={clientId} " +
                $"velocity={velocityBefore}->{physicsBody.linearVelocity} angular={angularVelocityBefore}->{physicsBody.angularVelocity}.", this);
    }

    private void ProcessPendingPassengerBoarding()
    {
        if (!HasAuthority || pendingPassengerBoarding == null) return;
        if (!IsPendingPassengerStillValid())
        {
            CancelPendingPassengerBoarding(true);
            return;
        }
        float timeout = profile != null ? profile.PassengerBoardingPreparationTimeout : 0.5f;
        if (Time.time - pendingPassengerBoarding.StartedAt >= timeout)
            CancelPendingPassengerBoarding(true);
    }

    private bool IsPendingPassengerStillValid()
    {
        if (pendingPassengerBoarding == null ||
            !TryGetPlayer(pendingPassengerBoarding.ClientId, out NetworkObject player)) return false;
        if (IsSessionActive && NetworkObject != null &&
            NetworkObject.OwnerClientId != pendingPassengerBoarding.PhysicsOwnerClientId) return false;
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        return pendingPassengerBoarding.StartedDowned || health == null || !health.IsDowned;
    }

    private void CancelPendingPassengerBoarding(bool notifyOwner)
    {
        if (pendingPassengerBoarding == null) return;
        ulong clientId = pendingPassengerBoarding.ClientId;
        uint token = pendingPassengerBoarding.Token;
        pendingPassengerBoarding = null;
        SetPassengerTransportCollisionState(clientId, false);
        if (notifyOwner && IsSessionActive && IsServer)
            CancelPassengerBoardingClientRpc(token, Target(clientId));
        else if (TryFindPlayerOnPeer(clientId, out NetworkObject player))
            player.GetComponent<PlayerWheelbarrowController>()?.CancelPassengerBoarding(this, token);
    }

    private bool IsPassengerAnchorClear(NetworkObject player)
    {
        if (passengerAnchor == null || player == null) return false;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller == null) return true;

        Vector3 center = passengerAnchor.position + passengerAnchor.rotation * controller.center;
        Vector3 capsuleAxis = passengerAnchor.up;
        float half = Mathf.Max(controller.radius, controller.height * 0.5f - controller.radius);
        Collider[] overlaps = Physics.OverlapCapsule(
            center - capsuleAxis * half,
            center + capsuleAxis * half,
            controller.radius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        return overlaps.All(item => item == null ||
            item.transform == player.transform || item.transform.IsChildOf(player.transform) ||
            item.transform == transform || item.transform.IsChildOf(transform));
    }

    [ClientRpc]
    private void CancelPassengerBoardingClientRpc(uint token, ClientRpcParams rpc = default)
    {
        NetworkObject player = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        player?.GetComponent<PlayerWheelbarrowController>()?.CancelPassengerBoarding(this, token);
    }

    public bool RequestExit(ulong clientId)
    {
        if (!HasAuthority || Speed > (profile != null ? profile.MaximumExitSpeed : 0.8f)) return false;
        if (DriverClientId == clientId) return RequestDriverExitAndDock(clientId);
        if (PassengerClientId == clientId)
        {
            if (HasHardenedPassengerConcrete) return false;
            return ReleasePassenger(false);
        }
        return false;
    }

    public bool RequestDriverExitAndDock(ulong clientId)
    {
        if (!HasAuthority || DriverClientId != clientId || State != WheelbarrowState.Driven) return false;

        WheelbarrowDockingStation requestedDock = FindReadyDockForDriverExit();
        if (requestedDock != null && requestedDock.TryDockImmediately(this, clientId)) return true;

        ReclaimPhysicsAuthority(true);
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

        ReclaimPhysicsAuthority(false);
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
        hasFailedConcreteOriginalDockPose = false;
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
        hasFailedConcreteOriginalDockPose = false;
        SetDockSecured(false);
        return true;
    }

    internal void ForceReleaseDock(WheelbarrowDockingStation station)
    {
        if (!HasAuthority || activeDock != station) return;
        activeDock = null;
        if (IsSessionActive) dockNetwork.Value = NoClient;
        hasSecuredDockPose = false;
        hasFailedConcreteOriginalDockPose = false;
        SetDockSecured(false);
        if (State == WheelbarrowState.Docked || State == WheelbarrowState.Pouring ||
            State == WheelbarrowState.TrappedInFailedConcrete)
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

    internal bool BeginFailedConcreteTrap(WheelbarrowDockingStation station)
    {
        if (!HasAuthority || station == null || activeDock != station || physicsBody == null ||
            station.DockedWheelbarrow != this || State != WheelbarrowState.Pouring || !HasPourableConcrete ||
            !hasSecuredDockPose)
            return false;

        failedConcreteOriginalDockPosition = securedDockPosition;
        failedConcreteOriginalDockRotation = securedDockRotation;
        hasFailedConcreteOriginalDockPose = true;
        ReclaimPhysicsAuthority(false);
        ResetDriveInput();
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        SetDockSecured(true);
        SetState(WheelbarrowState.TrappedInFailedConcrete);
        return true;
    }

    internal bool RollbackFailedConcreteTrap(WheelbarrowDockingStation station)
    {
        if (!HasAuthority || station == null || activeDock != station || physicsBody == null ||
            !hasFailedConcreteOriginalDockPose)
            return false;

        ReclaimPhysicsAuthority(false);
        securedDockPosition = failedConcreteOriginalDockPosition;
        securedDockRotation = failedConcreteOriginalDockRotation;
        hasSecuredDockPose = true;
        SetState(WheelbarrowState.Docked);
        SetDockSecured(true);
        physicsBody.position = securedDockPosition;
        physicsBody.rotation = securedDockRotation;
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(securedDockPosition, securedDockRotation);
        hasFailedConcreteOriginalDockPose = false;
        return true;
    }

    internal void MoveFailedConcreteTrap(Vector3 position, Quaternion rotation)
    {
        if (!HasAuthority || State != WheelbarrowState.TrappedInFailedConcrete || physicsBody == null) return;
        physicsBody.MovePosition(position);
        physicsBody.MoveRotation(rotation);
        transform.SetPositionAndRotation(position, rotation);
    }

    internal void CompleteFailedConcreteTrap(Vector3 position, Quaternion rotation)
    {
        if (!HasAuthority || State != WheelbarrowState.TrappedInFailedConcrete || physicsBody == null) return;
        securedDockPosition = position;
        securedDockRotation = rotation;
        hasSecuredDockPose = true;
        physicsBody.position = position;
        physicsBody.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
    }

    internal void ReleaseFromFailedConcrete(WheelbarrowDockingStation station)
    {
        if (!HasAuthority || State != WheelbarrowState.TrappedInFailedConcrete ||
            activeDock != station || physicsBody == null) return;
        activeDock = null;
        if (IsSessionActive) dockNetwork.Value = NoClient;
        hasSecuredDockPose = false;
        hasFailedConcreteOriginalDockPose = false;
        SetState(WheelbarrowState.Free);
        SetDockSecured(false);
        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        ConfigureBody();
    }

    internal bool ReleasePassengerForCriticalFailure()
    {
        if (!HasAuthority) return false;
        if (PassengerClientId == NoClient) return true;
        return ReleasePassenger(true);
    }

    internal void ForceCompletePassengerReleaseForCriticalFailure()
    {
        if (!HasAuthority || PassengerClientId == NoClient) return;
        ulong passenger = PassengerClientId;
        TechnicalReleaseOccupant(passenger, true);
        SetPassenger(NoClient);
    }

    public void SetPouringState(bool pouring)
    {
        if (!HasAuthority) return;
        SetState(pouring ? WheelbarrowState.Pouring : WheelbarrowState.Docked);
    }

    public bool ConsumeConcreteLoad()
    {
        if (!HasAuthority || !HasPourableConcrete) return false;
        SetConcreteLoads(ConcreteLoads - 1);
        return true;
    }

    internal void RestoreConcreteLoadsAfterCriticalFailureRollback(int concreteLoads)
    {
        if (!HasAuthority) return;
        SetConcreteLoads(concreteLoads);
    }

    public void SpillConcrete()
    {
        if (!HasAuthority || ConcreteLoads <= 0 || HasHardenedPassengerConcrete) return;
        SetConcreteLoads(0);
        SetSpillSequence(localSpillSequence + 1);
    }

    public bool TryRemoveDownedPassenger(PlayerInteractionNew rescuer)
    {
        if (!HasAuthority || rescuer == null || PassengerClientId == NoClient || HasHardenedPassengerConcrete ||
            Speed > (profile != null ? profile.MaximumExitSpeed : 0.8f) ||
            !TryGetPlayer(PassengerClientId, out NetworkObject passenger)) return false;
        PlayerHealth health = passenger.GetComponent<PlayerHealth>();
        if (health == null || !health.IsDowned) return false;
        return ReleasePassenger(false);
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
        if (!massPropertiesDirty || physicsBody == null) return;
        massPropertiesDirty = false;
        float previousMass = physicsBody.mass;
        float baseMass = profile != null ? profile.BaseMass : 22f;
        float cargoMass = GetResourceCargoMass() + ConcreteLoads * (profile != null ? profile.ConcreteBatchMass : 80f);
        float passengerMass = PassengerClientId != NoClient
            ? (profile != null ? profile.PassengerMass : 75f)
            : 0f;
        float totalMass = baseMass + cargoMass + passengerMass;
        physicsBody.mass = totalMass;
        Vector3 baseCenterOfMass = profile != null ? profile.BaseCenterOfMassLocal : new Vector3(0f, 0.45f, -0.15f);
        Vector3 cargoPoint = cargoRoot != null ? transform.InverseTransformPoint(cargoRoot.position) : Vector3.up * 0.4f;
        Vector3 passengerPoint = passengerAnchor != null
            ? transform.InverseTransformPoint(passengerAnchor.position)
            : cargoPoint;
        physicsBody.centerOfMass = (baseCenterOfMass * baseMass + cargoPoint * cargoMass +
            passengerPoint * passengerMass) / Mathf.Max(1f, totalMass);
        RefreshSupportLoadDistribution();
        if (State == WheelbarrowState.Driven)
        {
            driverSupportTargetInitialized = false;
            CaptureDriverSupportTarget();
        }
        if (profile != null && profile.EnableDiagnostics && !Mathf.Approximately(previousMass, totalMass))
            Debug.Log($"[Wheelbarrow] Mass/COM updated {previousMass:F2}->{totalMass:F2}, com={physicsBody.centerOfMass}, " +
                $"support={driverSupportLoadShare:F2}/{wheelLoadShare:F2}, passenger={PassengerClientId}.", this);
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

    public void ApplyPresentedCargoTransforms(Vector3 rootPosition, Quaternion rootRotation)
    {
        if (HasLocalPhysicsAuthority) return;
        List<BaseResourceNew> cargo = localCargo;
        Quaternion sourceRootRotation = transform.rotation;
        for (int i = 0; i < cargo.Count; i++)
        {
            BaseResourceNew resource = cargo[i];
            if (resource == null) continue;
            Transform slot = cargoSlots != null && i < cargoSlots.Length && cargoSlots[i] != null
                ? cargoSlots[i]
                : cargoRoot;
            if (slot == null) continue;
            Vector3 localPosition = transform.InverseTransformPoint(slot.position);
            Quaternion localRotation = Quaternion.Inverse(sourceRootRotation) * slot.rotation;
            resource.transform.SetPositionAndRotation(
                rootPosition + rootRotation * localPosition,
                rootRotation * localRotation);
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

    private bool ReleasePassenger(bool tipped)
    {
        if (pendingPassengerBoarding != null) CancelPendingPassengerBoarding(true);
        ulong clientId = PassengerClientId;
        if (clientId == NoClient) return false;
        return BeginSafeExit(clientId, true, tipped);
    }

    private void ReleaseAllOccupants(bool force)
    {
        ulong driver = DriverClientId;
        ulong passenger = PassengerClientId;
        if (force)
        {
            if (driver != NoClient) TechnicalReleaseOccupant(driver, false);
            if (passenger != NoClient) TechnicalReleaseOccupant(passenger, true);
            SetDriver(NoClient);
            SetPassenger(NoClient);
            return;
        }
        if (driver != NoClient) BeginSafeExit(driver);
        if (passenger != NoClient) BeginSafeExit(passenger, true, true);
    }

    private bool BeginSafeExit(ulong clientId, bool passenger = false, bool forced = false)
    {
        if (!TryGetPlayer(clientId, out NetworkObject player)) return false;
        if (pendingSafeExits.ContainsKey(clientId)) return true;
        float radius = profile != null ? profile.ExitSearchRadius : 1.8f;
        if (!TryResolveSafeExitPosition(player, clientId, radius, out Vector3 candidate) && !forced)
        {
            NotifyExitDenied(clientId);
            return false;
        }

        PendingSafeExit pending = new PendingSafeExit
        {
            ClientId = clientId,
            Token = NextSafeExitToken(),
            StartedAt = Time.time,
            OperationStartedAt = Time.time,
            SearchRadius = radius,
            Passenger = passenger,
            Forced = forced,
            ApplyTippedPassengerImpulse = ShouldApplyTippedPassengerImpulse(passenger, forced, State)
        };
        pendingSafeExits[clientId] = pending;
        if (!passenger) SetOccupantCollisionIgnored(clientId, true);
        else SetPassengerTransportCollisionState(clientId, true, false);
        if (TryResolveSafeExitPosition(player, clientId, radius, out candidate))
            RequestSafeExitPlacement(pending, player, candidate);
        return true;
    }

    private uint NextSafeExitToken()
    {
        uint token = ++nextSafeExitToken;
        if (token == 0) token = ++nextSafeExitToken;
        return token;
    }

    private bool TryResolveSafeExitPosition(
        NetworkObject player,
        ulong clientId,
        float radius,
        out Vector3 resolved)
    {
        resolved = default;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (safeExitPoints != null)
        {
            foreach (Transform exit in safeExitPoints)
            {
                if (exit == null) continue;
                if (TryResolveGroundedCandidate(
                    exit.position,
                    controller,
                    player.transform,
                    clientId,
                    out resolved)) return true;
            }
        }

        Vector3 center = driverAnchor != null ? driverAnchor.position : transform.position - transform.forward;
        float[] angles = { 0f, -30f, 30f, -60f, 60f, -90f, 90f, 180f };
        foreach (float angle in angles)
        {
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * -transform.forward;
            Vector3 sample = center + direction * radius;
            if (TryResolveGroundedCandidate(
                sample,
                controller,
                player.transform,
                clientId,
                out resolved)) return true;
        }
        return false;
    }

    private bool TryResolveGroundedCandidate(
        Vector3 sample,
        CharacterController controller,
        Transform playerRoot,
        ulong clientId,
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
            if (!IsCapsuleFree(grounded, controller, playerRoot, hit.collider)) return false;
            BuildPaddedPlayerCapsule(grounded, controller, out Vector3 bottom, out Vector3 top, out float radius);
            return !IsPendingExitCapsuleReserved(clientId, bottom, top, radius);
        }
        return false;
    }

    private bool IsCapsuleFree(
        Vector3 rootPosition,
        CharacterController controller,
        Transform playerRoot,
        Collider supportingCollider = null)
    {
        BuildPaddedPlayerCapsule(rootPosition, controller, out Vector3 bottom, out Vector3 top, out float radius);
        Collider[] overlaps = Physics.OverlapCapsule(bottom, top, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        return overlaps.All(item => item == null || item == supportingCollider ||
            item.transform == playerRoot || item.transform.IsChildOf(playerRoot));
    }

    private void BuildPaddedPlayerCapsule(
        Vector3 rootPosition,
        CharacterController controller,
        out Vector3 bottom,
        out Vector3 top,
        out float radius)
    {
        radius = controller.radius + (profile != null ? profile.ExitSeparationPadding : 0.05f);
        Vector3 axis = controller.transform.up;
        if (axis.sqrMagnitude <= 0.0001f) axis = Vector3.up;
        else axis.Normalize();
        Vector3 center = rootPosition + controller.transform.rotation * controller.center;
        float segmentHalfLength = Mathf.Max(0f, controller.height * 0.5f - radius);
        bottom = center - axis * segmentHalfLength;
        top = center + axis * segmentHalfLength;
    }

    private bool IsPendingExitCapsuleReserved(
        ulong requestingClientId,
        Vector3 bottom,
        Vector3 top,
        float radius)
    {
        foreach (PendingSafeExit pending in pendingSafeExits.Values)
        {
            if (pending.ClientId == requestingClientId || !pending.PlacementRequested ||
                pending.ReservedCapsuleRadius <= 0f) continue;
            if (CapsulesOverlap(
                bottom,
                top,
                radius,
                pending.ReservedCapsuleBottom,
                pending.ReservedCapsuleTop,
                pending.ReservedCapsuleRadius)) return true;
        }
        return false;
    }

    private static bool CapsulesOverlap(
        Vector3 firstBottom,
        Vector3 firstTop,
        float firstRadius,
        Vector3 secondBottom,
        Vector3 secondTop,
        float secondRadius)
    {
        float combinedRadius = Mathf.Max(0f, firstRadius) + Mathf.Max(0f, secondRadius);
        return SegmentDistanceSquared(firstBottom, firstTop, secondBottom, secondTop) <
            combinedRadius * combinedRadius;
    }

    private static float SegmentDistanceSquared(Vector3 firstStart, Vector3 firstEnd, Vector3 secondStart, Vector3 secondEnd)
    {
        const float epsilon = 0.000001f;
        Vector3 firstDirection = firstEnd - firstStart;
        Vector3 secondDirection = secondEnd - secondStart;
        Vector3 offset = firstStart - secondStart;
        float firstLengthSquared = Vector3.Dot(firstDirection, firstDirection);
        float secondLengthSquared = Vector3.Dot(secondDirection, secondDirection);
        float secondProjection = Vector3.Dot(secondDirection, offset);
        float firstT;
        float secondT;

        if (firstLengthSquared <= epsilon && secondLengthSquared <= epsilon)
            return offset.sqrMagnitude;
        if (firstLengthSquared <= epsilon)
        {
            firstT = 0f;
            secondT = Mathf.Clamp01(secondProjection / secondLengthSquared);
        }
        else
        {
            float firstProjection = Vector3.Dot(firstDirection, offset);
            if (secondLengthSquared <= epsilon)
            {
                secondT = 0f;
                firstT = Mathf.Clamp01(-firstProjection / firstLengthSquared);
            }
            else
            {
                float directionsDot = Vector3.Dot(firstDirection, secondDirection);
                float denominator = firstLengthSquared * secondLengthSquared - directionsDot * directionsDot;
                firstT = denominator > epsilon
                    ? Mathf.Clamp01((directionsDot * secondProjection - firstProjection * secondLengthSquared) / denominator)
                    : 0f;
                secondT = (directionsDot * firstT + secondProjection) / secondLengthSquared;
                if (secondT < 0f)
                {
                    secondT = 0f;
                    firstT = Mathf.Clamp01(-firstProjection / firstLengthSquared);
                }
                else if (secondT > 1f)
                {
                    secondT = 1f;
                    firstT = Mathf.Clamp01((directionsDot - firstProjection) / firstLengthSquared);
                }
            }
        }

        Vector3 firstClosest = firstStart + firstDirection * firstT;
        Vector3 secondClosest = secondStart + secondDirection * secondT;
        return (firstClosest - secondClosest).sqrMagnitude;
    }

    private static void ApplySafeExitPlacement(NetworkObject player, Vector3 position)
    {
        if (player == null) return;
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        player.transform.position = position;
        if (wasEnabled) controller.enabled = true;
        player.GetComponent<PlayerTransportCollisionController>()?.EnsureSuppressed();
        player.GetComponent<StarterAssets.FirstPersonController>()?.ResetMovementAfterForcedPlacement();
    }

    private void RequestSafeExitPlacement(PendingSafeExit pending, NetworkObject player, Vector3 position)
    {
        pending.PlacementRequested = true;
        pending.PlacementConfirmed = false;
        pending.RequestedPosition = position;
        CharacterController characterController = player != null ? player.GetComponent<CharacterController>() : null;
        if (characterController != null)
        {
            BuildPaddedPlayerCapsule(
                position,
                characterController,
                out pending.ReservedCapsuleBottom,
                out pending.ReservedCapsuleTop,
                out pending.ReservedCapsuleRadius);
        }
        pending.StartedAt = Time.time;
        if (!IsSessionActive || pending.ClientId == NetworkManager.ServerClientId)
        {
            PlayerWheelbarrowController controller = player.GetComponent<PlayerWheelbarrowController>();
            if (controller != null && controller.BeginSafeExit(this, position))
                ConfirmSafeExitPlacement(pending.ClientId, pending.Token);
            return;
        }
        BeginSafeExitClientRpc(pending.Token, position, Target(pending.ClientId));
    }

    [ClientRpc]
    private void BeginSafeExitClientRpc(uint token, Vector3 position, ClientRpcParams rpc = default)
    {
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject == null) return;
        NetworkObject player = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (player.GetComponent<PlayerWheelbarrowController>()?.BeginSafeExit(this, position) == true)
            ConfirmSafeExitPlacementServerRpc(token);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConfirmSafeExitPlacementServerRpc(uint token, ServerRpcParams rpc = default)
    {
        ConfirmSafeExitPlacement(rpc.Receive.SenderClientId, token);
    }

    private void ConfirmSafeExitPlacement(ulong clientId, uint token)
    {
        if (!pendingSafeExits.TryGetValue(clientId, out PendingSafeExit pending) || pending.Token != token ||
            !TryGetPlayer(clientId, out NetworkObject player)) return;
        ApplySafeExitPlacement(player, pending.RequestedPosition);
        pending.PlacementConfirmed = true;
        pending.StartedAt = Time.time;
        Vector3 ejectionOrigin = pending.ApplyTippedPassengerImpulse && passengerAnchor != null
            ? passengerAnchor.position
            : transform.position;
        pending.EjectionDirection = ResolveEjectionDirection(
            ejectionOrigin,
            transform.forward,
            transform.right,
            pending.RequestedPosition);
        if (!pending.RoleCleared)
        {
            if (pending.Passenger && PassengerClientId == clientId &&
                player.TryGetComponent(out PlayerConcreteTrapController trap) &&
                trap.IsAttachedToWheelbarrow && trap.IsSourcedBy(this))
                trap.CompleteWheelbarrowEjection(this);
            if (pending.Passenger && PassengerClientId == clientId) SetPassenger(NoClient);
            else if (!pending.Passenger && DriverClientId == clientId) SetDriver(NoClient);
            pending.RoleCleared = true;
        }
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

            PendingSafeExit pending = pair.Value;
            if (HasForcedExitFallbackElapsed(
                pending.Forced,
                pending.PlacementConfirmed,
                pending.OperationStartedAt,
                Time.time,
                profile != null ? profile.ForcedExitFallbackDelay : 3f))
            {
                CompleteForcedExitAtSpawn(pair.Key, pending);
                continue;
            }
            if (!pending.PlacementRequested)
            {
                float growth = profile != null ? profile.ForcedExitSearchRadiusGrowthRate : 0.75f;
                float maximum = profile != null ? profile.MaximumForcedExitSearchRadius : 4f;
                pending.SearchRadius = Mathf.Min(maximum, pending.SearchRadius + growth * Time.fixedDeltaTime);
                if (TryResolveSafeExitPosition(player, pair.Key, pending.SearchRadius, out Vector3 waitingCandidate))
                    RequestSafeExitPlacement(pending, player, waitingCandidate);
                continue;
            }

            float elapsed = Time.time - pending.StartedAt;
            if (!pending.PlacementConfirmed)
            {
                float timeout = profile != null ? profile.PassengerExitPreparationTimeout : 0.5f;
                if (elapsed < timeout) continue;
                if (!pending.Forced && pending.Passenger)
                {
                    pendingSafeExits.Remove(pair.Key);
                    CancelSafeExitPlacementClientRpc(Target(pair.Key));
                    continue;
                }
                pending.Forced = true;
                pending.PlacementRequested = false;
                continue;
            }
            if (elapsed >= minimumGrace && IsPlayerSeparatedFromWheelbarrow(player))
            {
                CompleteSafeExit(pair.Key);
                continue;
            }

            if (elapsed < maximumGrace) continue;
            pending.Forced = true;
            pending.PlacementRequested = false;
            pending.PlacementConfirmed = false;
        }
    }

    private bool IsPlayerSeparatedFromWheelbarrow(NetworkObject player)
    {
        CharacterController controller = player != null ? player.GetComponent<CharacterController>() : null;
        if (controller == null) return true;

        BuildPaddedPlayerCapsule(player.transform.position, controller, out Vector3 bottom, out Vector3 top, out float radius);
        Collider[] overlaps = Physics.OverlapCapsule(
            bottom,
            top,
            radius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        return overlaps.All(item => item == null ||
            (item.transform != transform && !item.transform.IsChildOf(transform)));
    }

    private void CompleteSafeExit(ulong clientId)
    {
        if (!pendingSafeExits.TryGetValue(clientId, out PendingSafeExit pending)) return;
        bool passenger = pending.Passenger;
        ExternalImpulseData impulse = default;
        bool applyImpulse = pending.ApplyTippedPassengerImpulse &&
            profile != null && profile.PassengerTippedEjectionImpulseProfile != null;
        if (applyImpulse)
            impulse = profile.PassengerTippedEjectionImpulseProfile.CreateImpulse(pending.EjectionDirection);
        pendingSafeExits.Remove(clientId);
        if (passenger) SetPassengerTransportCollisionState(clientId, false);
        else SetOccupantCollisionIgnored(clientId, false);
        if (IsSessionActive && IsServer) CompleteSafeExitClientRpc(applyImpulse, impulse, Target(clientId));
        else if (TryFindPlayerOnPeer(clientId, out NetworkObject player))
            CompleteSafeExitLocally(player, applyImpulse, impulse);
    }

    [ClientRpc]
    private void CompleteSafeExitClientRpc(
        bool applyImpulse,
        ExternalImpulseData impulse,
        ClientRpcParams rpc = default)
    {
        NetworkObject player = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        CompleteSafeExitLocally(player, applyImpulse, impulse);
    }

    private void CompleteSafeExitLocally(
        NetworkObject player,
        bool applyImpulse,
        ExternalImpulseData impulse)
    {
        if (player == null) return;
        player.GetComponent<PlayerWheelbarrowController>()?.CompleteSafeExit(this);
        if (applyImpulse)
            player.GetComponent<PlayerExternalImpulseController>()?.ApplyServerAuthorizedImpulse(impulse);
    }

    private void CompleteForcedExitAtSpawn(ulong clientId, PendingSafeExit pending)
    {
        pendingSafeExits.Remove(clientId);
        if (pending.Passenger && PassengerClientId == clientId)
        {
            if (TryGetPassengerConcreteTrap(out PlayerConcreteTrapController trap)) trap.CompleteWheelbarrowEjection(this);
            SetPassenger(NoClient);
        }
        else if (!pending.Passenger && DriverClientId == clientId) SetDriver(NoClient);
        TechnicalReleaseOccupant(clientId, pending.Passenger);
    }

    private static bool ShouldApplyTippedPassengerImpulse(
        bool passenger,
        bool forced,
        WheelbarrowState state)
    {
        return passenger && forced && state == WheelbarrowState.Tipped;
    }

    private static Vector3 ResolveEjectionDirection(
        Vector3 ejectionOrigin,
        Vector3 wheelbarrowForward,
        Vector3 wheelbarrowRight,
        Vector3 exitPosition)
    {
        Vector3 direction = Vector3.ProjectOnPlane(exitPosition - ejectionOrigin, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.ProjectOnPlane(-wheelbarrowForward, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.ProjectOnPlane(wheelbarrowRight, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.right;
        return direction.normalized;
    }

    private static bool HasForcedExitFallbackElapsed(
        bool forced,
        bool placementConfirmed,
        float operationStartedAt,
        float currentTime,
        float fallbackDelay)
    {
        return forced && !placementConfirmed &&
            currentTime - operationStartedAt >= Mathf.Max(0.1f, fallbackDelay);
    }

    [ClientRpc]
    private void CancelSafeExitPlacementClientRpc(ClientRpcParams rpc = default)
    {
        NetworkObject player = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        player?.GetComponent<PlayerWheelbarrowController>()?.CancelSafeExitPlacement(this);
    }

    private void NotifyExitDenied(ulong clientId)
    {
        float duration = profile != null ? profile.ExitDeniedMessageDuration : 1.25f;
        if (profile != null && profile.EnableDiagnostics)
            Debug.Log($"[Wheelbarrow] Exit denied for client={clientId}: no valid capsule placement within " +
                $"{(profile != null ? profile.ExitSearchRadius : 1.8f):F2}m.", this);
        if (!IsSessionActive || clientId == NetworkManager.ServerClientId)
        {
            if (TryFindPlayerOnPeer(clientId, out NetworkObject player))
                player.GetComponent<PlayerWheelbarrowController>()?.ShowExitDenied(duration);
            return;
        }
        ExitDeniedClientRpc(duration, Target(clientId));
    }

    [ClientRpc]
    private void ExitDeniedClientRpc(float duration, ClientRpcParams rpc = default)
    {
        NetworkManager.Singleton?.LocalClient?.PlayerObject?
            .GetComponent<PlayerWheelbarrowController>()?.ShowExitDenied(duration);
    }

    private void TechnicalReleaseOccupant(ulong clientId, bool passenger)
    {
        if (!TryGetPlayer(clientId, out NetworkObject player)) return;
        Transform spawn = PlayerSpawnManager.GetSpawnPointForClient(clientId);
        Vector3 position = spawn != null ? spawn.position : player.transform.position;
        Quaternion rotation = spawn != null ? spawn.rotation : player.transform.rotation;
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health != null) health.ApplyTechnicalTransportExit(position, rotation);
        else
        {
            ApplySafeExitPlacement(player, position);
            player.GetComponent<PlayerWheelbarrowController>()?.CompleteTechnicalSafeExit();
        }
        if (passenger) SetPassengerTransportCollisionState(clientId, false);
        else SetOccupantCollisionIgnored(clientId, false);
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

        float targetSteer = State == WheelbarrowState.Driven && HasLocalPhysicsAuthority
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

    public void ApplyPresentedWheelVisual(float steeringAngle, float spinDegrees)
    {
        if (wheelVisual == null || HasLocalPhysicsAuthority) return;
        wheelVisualSteerDegrees = steeringAngle;
        wheelVisualSpinDegrees = spinDegrees;
        Quaternion rotation = transform.rotation *
            Quaternion.Euler(0f, steeringAngle, 0f) *
            Quaternion.AngleAxis(spinDegrees, Vector3.right);
        wheelVisual.SetPositionAndRotation(
            transform.TransformPoint(wheelVisualRootLocalPosition),
            rotation * wheelVisualPoseOffset);
    }

    private void SetState(WheelbarrowState value)
    {
        WheelbarrowState previous = State;
        if (previous != value && value != WheelbarrowState.Free && value != WheelbarrowState.Tipped)
            SetRopeTowInactive(true);
        if (previous != value && value != WheelbarrowState.Free && value != WheelbarrowState.Tipped &&
            value != WheelbarrowState.Righting)
            RopeToolController.TryRetractAttachedTarget(NetworkObject);
        if (previous != value && (previous == WheelbarrowState.Driven || value == WheelbarrowState.Driven))
            ResetDriveInput();
        if (previous != value && value == WheelbarrowState.Driven && HasLocalPhysicsAuthority)
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
    private void SetPassenger(ulong value)
    {
        localPassenger = value;
        InvalidateDrivenWheelPhysics();
        if (IsSessionActive && IsServer) passengerNetwork.Value = value;
        TryActivatePassengerConcreteTrap();
    }

    private void SetConcreteLoads(int value)
    {
        localConcreteLoads = Mathf.Max(0, value);
        InvalidateDrivenWheelPhysics();
        if (IsSessionActive && IsServer) concreteLoadsNetwork.Value = localConcreteLoads;
        TryActivatePassengerConcreteTrap();
    }

    private void TryActivatePassengerConcreteTrap()
    {
        if (!HasAuthority || PassengerClientId == NoClient || ConcreteLoads <= 0 ||
            !TryGetPassengerConcreteTrap(out PlayerConcreteTrapController trap)) return;
        trap.ActivateInWheelbarrow(this);
    }

    private bool TryGetPassengerConcreteTrap(out PlayerConcreteTrapController trap)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && editorProbePassengerTrap != null &&
            PassengerClientId == editorProbePassengerTrap.OwnerClientId)
        {
            trap = editorProbePassengerTrap;
            return true;
        }
#endif
        trap = null;
        return PassengerClientId != NoClient && TryGetPlayer(PassengerClientId, out NetworkObject player) &&
            player.TryGetComponent(out trap);
    }

    internal void CompleteHardenedPassengerConcreteBreak(PlayerConcreteTrapController trap)
    {
        if (!HasAuthority || trap == null || !trap.IsSourcedBy(this)) return;
        SetConcreteLoads(0);
        RefreshMassAndCenterOfMass();
    }

    internal void ClearHardenedPassengerConcreteAfterEjection(PlayerConcreteTrapController trap)
    {
        if (!HasAuthority || trap == null) return;
        SetConcreteLoads(0);
        RefreshMassAndCenterOfMass();
    }

    internal void ClearHardenedPassengerConcreteForDisconnect(PlayerConcreteTrapController trap)
    {
        if (!HasAuthority || trap == null) return;
        SetConcreteLoads(0);
        if (PassengerClientId == trap.OwnerClientId) SetPassenger(NoClient);
        RefreshMassAndCenterOfMass();
    }
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

        if (NetworkManager.Singleton?.SpawnManager == null) return false;
        foreach (NetworkObject candidateNetworkObject in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (candidateNetworkObject != null && candidateNetworkObject.IsPlayerObject &&
                candidateNetworkObject.OwnerClientId == clientId)
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
