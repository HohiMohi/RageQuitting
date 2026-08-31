using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerInputNew), typeof(PlayerInventory), typeof(PlayerStaminaController))]
public sealed class RopeToolController : NetworkBehaviour
{
    public const ulong InvalidNetworkObjectId = ulong.MaxValue;
    private static readonly HashSet<RopeToolController> ActiveRopeSet = new HashSet<RopeToolController>();
    private static readonly Dictionary<ulong, RopeToolController> ReservedTargets = new Dictionary<ulong, RopeToolController>();

    private readonly NetworkVariable<RopeState> stateNetwork = new NetworkVariable<RopeState>(RopeState.Inactive);
    private readonly NetworkVariable<ulong> endpointNetworkId = new NetworkVariable<ulong>(InvalidNetworkObjectId);
    private readonly NetworkVariable<ulong> targetNetworkId = new NetworkVariable<ulong>(InvalidNetworkObjectId);
    private readonly NetworkVariable<RopeTargetKind> targetKindNetwork = new NetworkVariable<RopeTargetKind>(RopeTargetKind.None);
    private readonly NetworkVariable<Vector3> targetLocalPointNetwork = new NetworkVariable<Vector3>();
    private readonly NetworkVariable<float> currentLengthNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<float> normalizedTensionNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<bool> blockedNetwork = new NetworkVariable<bool>();
    private readonly NetworkVariable<bool> hardLimitNetwork = new NetworkVariable<bool>();
    private readonly NetworkVariable<float> escapeProgressNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<RopePlayerConstraintSettings> playerConstraintSettingsNetwork =
        new NetworkVariable<RopePlayerConstraintSettings>();

    [SerializeField] private Transform ropeHandAnchor;
    [SerializeField] private Vector3 fallbackHandAnchorLocalPosition = new Vector3(0.28f, 1.25f, 0.45f);
    [SerializeField] private Material ropeLineMaterial;

    private PlayerInputNew input;
    private PlayerInventory inventory;
    private PlayerStaminaController stamina;
    private PlayerHealth health;
    private PlayerConcreteTrapController concreteTrap;
    private LineRenderer line;
    private RopeToolProfileSO profile;
    private RopeEndProjectile endpoint;
    private NetworkObject target;
    private RopeState localState;
    private RopeTargetKind localTargetKind;
    private float localLength;
    private float localTension;
    private float localEscapeProgress;
    private RopePlayerConstraintSettings localPlayerConstraintSettings;
    private bool localBlocked;
    private bool localHardLimit;
    private float chargeStartedAt;
    private float overloadStartedAt = -1f;
    private float targetEscapeStartedAt = -1f;
    private ulong escapingTargetClientId = InvalidNetworkObjectId;
    private bool reelHeld;
    private bool payOutHeld;
    private bool chargeHeld;
    private bool deactivationRequested;
    private float nextActivationRequestAt;
    private float nextLengthSyncAt;

    public static IReadOnlyCollection<RopeToolController> ActiveRopes => ActiveRopeSet;
    public RopeState CurrentState => IsNetworkActive ? stateNetwork.Value : localState;
    public float CurrentLength => !IsNetworkActive || IsServer || IsOwner ? localLength : currentLengthNetwork.Value;
    public float NormalizedTension => IsNetworkActive ? normalizedTensionNetwork.Value : localTension;
    public NetworkObject AttachedTarget => ResolveTarget();
    public bool IsBlocked => IsNetworkActive ? blockedNetwork.Value : localBlocked;
    public bool IsAtHardLimit => IsNetworkActive ? hardLimitNetwork.Value : localHardLimit;
    public float EscapeProgress => IsNetworkActive ? escapeProgressNetwork.Value : localEscapeProgress;
    public RopeToolProfileSO ActiveProfile => profile;
    public float ChargeNormalized => CurrentState == RopeState.Charging && profile != null
        ? Mathf.Clamp01((Time.unscaledTime - chargeStartedAt) / profile.fullChargeDuration)
        : 0f;
    public Vector3 ThrowPreviewStartPosition => RopeStartPosition + GetThrowDirection() * 0.25f;
    public Vector3 ThrowPreviewDirection => GetThrowDirection();
    public float PredictedThrowSpeed => profile != null
        ? Mathf.Lerp(profile.minimumThrowSpeed, profile.maximumThrowSpeed, ChargeNormalized)
        : 0f;
    public float PredictedThrowLength => profile != null
        ? Mathf.Lerp(Mathf.Clamp(profile.minimumThrowLength, profile.minimumLength, profile.maximumLength),
            profile.maximumLength, ChargeNormalized)
        : 0f;

    private RopeTargetKind TargetKind => IsNetworkActive ? targetKindNetwork.Value : localTargetKind;
    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool HasSimulationAuthority => !IsNetworkActive || IsServer;
    private RopePlayerConstraintSettings PlayerConstraintSettings => IsNetworkActive
        ? playerConstraintSettingsNetwork.Value
        : localPlayerConstraintSettings;
    private Vector3 RopeStartPosition => ropeHandAnchor != null ? ropeHandAnchor.position : transform.position + Vector3.up * 1.2f;
    private Vector3 RopeEndPosition
    {
        get
        {
            NetworkObject attached = ResolveTarget();
            if (attached != null)
            {
                Vector3 localPoint = IsNetworkActive ? targetLocalPointNetwork.Value : localTargetPoint;
                return attached.transform.TransformPoint(localPoint);
            }
            return endpoint != null ? endpoint.transform.position : RopeStartPosition;
        }
    }

    internal bool IsAttachedTo(WheelbarrowController wheelbarrow)
    {
        return wheelbarrow != null && TargetKind == RopeTargetKind.Wheelbarrow &&
            ResolveTarget() == wheelbarrow.NetworkObject;
    }

    internal static bool TryRetractAttachedTarget(NetworkObject attachedTarget)
    {
        if (attachedTarget == null) return false;

        if (ReservedTargets.TryGetValue(attachedTarget.NetworkObjectId, out RopeToolController reserved) &&
            reserved != null && reserved.HasSimulationAuthority && reserved.ResolveTarget() == attachedTarget)
        {
            reserved.ResetRope(false);
            return true;
        }

        foreach (RopeToolController rope in ActiveRopeSet)
        {
            if (rope == null || !rope.HasSimulationAuthority || rope.TargetKind == RopeTargetKind.None ||
                rope.ResolveTarget() != attachedTarget) continue;
            rope.ResetRope(false);
            return true;
        }
        return false;
    }

    private void Awake()
    {
        input = GetComponent<PlayerInputNew>();
        inventory = GetComponent<PlayerInventory>();
        stamina = GetComponent<PlayerStaminaController>();
        health = GetComponent<PlayerHealth>();
        concreteTrap = GetComponent<PlayerConcreteTrapController>();
        EnsureHandAnchor();
        EnsureLineRenderer();
        EnsureTrajectoryPreview();
    }

    private void OnEnable()
    {
        ActiveRopeSet.Add(this);
        input.OnAction += HandleActionStarted;
        input.OnActionCanceled += HandleActionCanceled;
        input.OnActionAlt += HandleActionAltStarted;
        input.OnActionAltCanceled += HandleActionAltCanceled;
        input.OnInteractCanceled += HandleInteractCanceled;
        input.OnDropItem += HandleDropItem;
        inventory.OnSelectedItemChanged += HandleSelectedItemChanged;
        health.OnDownedStateChanged += HandleDownedStateChanged;
    }

    private void OnDisable()
    {
        ActiveRopeSet.Remove(this);
        if (input != null)
        {
            input.OnAction -= HandleActionStarted;
            input.OnActionCanceled -= HandleActionCanceled;
            input.OnActionAlt -= HandleActionAltStarted;
            input.OnActionAltCanceled -= HandleActionAltCanceled;
            input.OnInteractCanceled -= HandleInteractCanceled;
            input.OnDropItem -= HandleDropItem;
        }
        if (inventory != null) inventory.OnSelectedItemChanged -= HandleSelectedItemChanged;
        if (health != null) health.OnDownedStateChanged -= HandleDownedStateChanged;
        ClearStaminaDrain();
        if (HasSimulationAuthority) ResetRope(true);
    }

    public override void OnNetworkSpawn()
    {
        stateNetwork.OnValueChanged += HandleReplicatedStateChanged;
        endpointNetworkId.OnValueChanged += HandleEndpointChanged;
        targetNetworkId.OnValueChanged += HandleTargetChanged;
        currentLengthNetwork.OnValueChanged += HandleLengthChanged;
        localLength = currentLengthNetwork.Value;
        RefreshSelectedItem(false);
    }

    public override void OnNetworkDespawn()
    {
        stateNetwork.OnValueChanged -= HandleReplicatedStateChanged;
        endpointNetworkId.OnValueChanged -= HandleEndpointChanged;
        targetNetworkId.OnValueChanged -= HandleTargetChanged;
        currentLengthNetwork.OnValueChanged -= HandleLengthChanged;
        if (IsServer) ResetRope(true);
    }

    private void Update()
    {
        RefreshReferences();
        UpdateLine();

        if (IsOwnerOrSingleplayer())
        {
            if (IsRopeSelected() && profile == null)
            {
                EquippableItemSO selected = inventory.GetCurrentSelectedItem();
                profile = selected != null ? selected.ropeProfile : null;
            }

            bool shouldDeactivate = CurrentState != RopeState.Inactive &&
                (!IsRopeSelected() || health == null || health.IsDowned || concreteTrap != null && concreteTrap.IsTrapped ||
                 input == null || input.IsGameplayUiOpen);
            if (shouldDeactivate && !deactivationRequested)
            {
                deactivationRequested = true;
                RequestAction(RopeInputAction.Deactivate, 0f, Vector3.zero);
            }
            else if (!shouldDeactivate)
            {
                deactivationRequested = false;
            }

            bool shouldActivate = IsRopeSelected() && profile != null && health != null && !health.IsDowned &&
                (concreteTrap == null || !concreteTrap.IsTrapped) && input != null && !input.IsGameplayUiOpen &&
                CurrentState == RopeState.Inactive;
            if (shouldActivate && Time.unscaledTime >= nextActivationRequestAt)
            {
                nextActivationRequestAt = Time.unscaledTime + 0.25f;
                RequestAction(RopeInputAction.Activate, 0f, Vector3.zero);
            }
        }

        if (HasSimulationAuthority)
        {
            Simulate(Time.deltaTime);
        }
        else if (IsOwner)
        {
            PredictLength(Time.deltaTime);
        }
    }

    private void FixedUpdate()
    {
        if (HasSimulationAuthority)
        {
            SimulateConstraint();
        }
    }

    public bool TryHandleInteractPressed()
    {
        if (!IsOwnerOrSingleplayer() || concreteTrap != null && concreteTrap.IsTrapped)
        {
            return false;
        }

        if (IsRopeSelected() && TargetKind != RopeTargetKind.None && IsDeployedState(CurrentState))
        {
            RequestAction(RopeInputAction.Detach, 0f, Vector3.zero);
            return true;
        }

        if (TryGetRopeAttachedToThisPlayer(out RopeToolController sourceRope) && sourceRope.CanTargetEscape(this))
        {
            sourceRope.RequestTargetEscape(true);
            return true;
        }

        return false;
    }

    private void HandleInteractCanceled(object sender, EventArgs e)
    {
        if (TryGetRopeAttachedToThisPlayer(out RopeToolController sourceRope))
        {
            sourceRope.RequestTargetEscape(false);
        }
    }

    private void HandleDropItem(object sender, EventArgs e)
    {
        if (IsRopeSelected())
        {
            RequestAction(RopeInputAction.Deactivate, 0f, Vector3.zero);
        }
    }

    private void HandleActionStarted(object sender, EventArgs e)
    {
        if (!CanUseLocally()) return;
        if (CurrentState == RopeState.Ready)
        {
            chargeHeld = true;
            chargeStartedAt = Time.unscaledTime;
            RequestAction(RopeInputAction.BeginCharge, 0f, Vector3.zero);
        }
        else if (CurrentState == RopeState.Loose || CurrentState == RopeState.Attached || CurrentState == RopeState.PayingOut)
        {
            reelHeld = true;
            RequestAction(RopeInputAction.BeginReel, 0f, Vector3.zero);
        }
    }

    private void HandleActionCanceled(object sender, EventArgs e)
    {
        if (concreteTrap != null && concreteTrap.IsTrapped)
        {
            chargeHeld = false;
            reelHeld = false;
            return;
        }
        if (!IsRopeSelected()) return;
        if (chargeHeld)
        {
            chargeHeld = false;
            float charge = profile != null ? Mathf.Clamp01((Time.unscaledTime - chargeStartedAt) / profile.fullChargeDuration) : 0f;
            RequestAction(RopeInputAction.Throw, charge, GetThrowDirection());
        }
        else if (reelHeld)
        {
            reelHeld = false;
            RequestAction(RopeInputAction.StopReel, 0f, Vector3.zero);
        }
    }

    private void HandleActionAltStarted(object sender, EventArgs e)
    {
        if (!CanUseLocally()) return;
        if (CurrentState == RopeState.Loose || CurrentState == RopeState.Attached || CurrentState == RopeState.Reeling)
        {
            payOutHeld = true;
            RequestAction(RopeInputAction.BeginPayOut, 0f, Vector3.zero);
        }
    }

    private void HandleActionAltCanceled(object sender, EventArgs e)
    {
        if (concreteTrap != null && concreteTrap.IsTrapped)
        {
            payOutHeld = false;
            return;
        }
        if (!IsRopeSelected() || !payOutHeld) return;
        payOutHeld = false;
        RequestAction(RopeInputAction.StopPayOut, 0f, Vector3.zero);
    }

    private void HandleSelectedItemChanged(object sender, PlayerInventory.OnSelectedItemChangedEventArgs e)
    {
        RefreshSelectedItem(true);
    }

    private void HandleDownedStateChanged(object sender, EventArgs e)
    {
        if (health.IsDowned && IsOwnerOrSingleplayer())
        {
            RequestAction(RopeInputAction.Deactivate, 0f, Vector3.zero);
        }
    }

    private void RefreshSelectedItem(bool notifyServer)
    {
        EquippableItemSO item = HasSimulationAuthority && inventory != null
            ? inventory.GetSelectedItemForServerValidation()
            : inventory != null ? inventory.GetCurrentSelectedItem() : null;
        RopeToolProfileSO selectedProfile = item != null && item.itemType == EquippableItemType.Rope ? item.ropeProfile : null;
        profile = selectedProfile;
        if (profile != null)
        {
            SetPlayerConstraintSettings(CreatePlayerConstraintSettings(profile));
        }
        else if (HasSimulationAuthority)
        {
            SetPlayerConstraintSettings(default);
        }

        if (profile == null)
        {
            if (notifyServer && IsOwnerOrSingleplayer()) RequestAction(RopeInputAction.Deactivate, 0f, Vector3.zero);
            if (line != null) line.enabled = false;
        }
        else if (HasSimulationAuthority && CurrentState == RopeState.Inactive)
        {
            ResetRope(false);
        }
    }

    private void Simulate(float deltaTime)
    {
        if (profile == null) return;

        if (TargetKind != RopeTargetKind.None && ResolveTarget() == null)
        {
            ResetRope(false);
            return;
        }

        if (TargetKind == RopeTargetKind.Wheelbarrow &&
            (!TryGetAttachedWheelbarrow(out WheelbarrowController wheelbarrow) ||
             !CanRemainAttachedToWheelbarrow(wheelbarrow)))
        {
            ResetRope(false);
            return;
        }

        if ((CurrentState == RopeState.Flying || CurrentState == RopeState.Loose ||
             CurrentState == RopeState.Reeling || CurrentState == RopeState.PayingOut) &&
            TargetKind == RopeTargetKind.None && endpoint == null)
        {
            ResetRope(false);
            return;
        }

        if (CurrentState == RopeState.Reeling)
        {
            if (TargetKind == RopeTargetKind.None)
            {
                ClearStaminaDrain();
                if (!IsBlocked)
                {
                    TryShortenRope(profile.emptyEndpointReelSpeed, deltaTime, 0f);
                }
            }
            else
            {
                if (stamina.CurrentStamina > 0.001f && !IsBlocked)
                {
                    bool shortened = TryShortenRope(profile.attachedTargetReelSpeed, deltaTime, profile.minimumLength);
                    float drain = shortened ? NormalizedTension * profile.maximumReelingStaminaDrain : 0f;
                    stamina.SetAuthoritativeDrainSource(StaminaDrainSource.RopeReeling, drain);
                }
                else if (stamina.CurrentStamina <= 0.001f)
                {
                    ClearStaminaDrain();
                    SetState(GetBaseDeployedState());
                }
                else ClearStaminaDrain();
            }
        }
        else
        {
            ClearStaminaDrain();
            if (CurrentState == RopeState.PayingOut)
            {
                SetLength(CurrentLength + profile.payOutSpeed * deltaTime);
            }
        }

        if (targetEscapeStartedAt >= 0f)
        {
            float progress = Mathf.Clamp01((Time.time - targetEscapeStartedAt) / profile.targetEscapeHoldDuration);
            SetEscapeProgress(progress);
            if (progress >= 1f)
            {
                DetachToLooseEnd();
            }
        }
    }

    private void SimulateConstraint()
    {
        if (profile == null || !IsConstraintState(CurrentState))
        {
            SetTension(0f);
            SetBlocked(false);
            SetHardLimit(false);
            overloadStartedAt = -1f;
            return;
        }

        Vector3 start = RopeStartPosition;
        Vector3 end = RopeEndPosition;
        Vector3 delta = end - start;
        float distance = delta.magnitude;
        if (TargetKind == RopeTargetKind.None && endpoint != null && endpoint.IsLanded)
        {
            endpoint.TryGetSupportNormal(profile.endpointGroundProbeDistance, profile.obstructionMask, out _);
        }
        bool blocked = IsLineBlocked(start, end);
        SetBlocked(blocked);
        float extension = Mathf.Max(0f, distance - CurrentLength - profile.tautDeadZone);
        SetTension(Mathf.Clamp01(extension / Mathf.Max(0.01f, profile.maximumStretch)));
        bool atHardLimit = extension >= Mathf.Max(0.01f, profile.maximumStretch) - 0.01f;
        SetHardLimit(atHardLimit);
        Vector3 direction = distance > 0.0001f ? delta / distance : Vector3.zero;

        WheelbarrowController ropeWheelbarrow = null;
        bool hasWheelbarrowTarget = TargetKind == RopeTargetKind.Wheelbarrow &&
            TryGetAttachedWheelbarrow(out ropeWheelbarrow);
        if (hasWheelbarrowTarget)
            ApplyWheelbarrowPull(ropeWheelbarrow, direction, extension, blocked);

        if (TargetKind == RopeTargetKind.None && endpoint != null && CurrentState == RopeState.Reeling)
        {
            float planarDistance = Vector3.ProjectOnPlane(end - start, Vector3.up).magnitude;
            bool endpointAtPlayer = endpoint.IsLanded
                ? planarDistance <= profile.endpointReturnDistance
                : distance <= profile.endpointReturnDistance;
            if (endpointAtPlayer)
            {
                ResetRope(false);
                return;
            }
        }

        if (!blocked && extension > 0f)
        {
            if (TargetKind == RopeTargetKind.Resource && ResolveTarget() != null && target.TryGetComponent(out Rigidbody targetBody))
            {
                Vector3 localPoint = IsNetworkActive ? targetLocalPointNetwork.Value : localTargetPoint;
                Vector3 point = target.transform.TransformPoint(localPoint);
                float separatingSpeed = Vector3.Dot(targetBody.GetPointVelocity(point), direction);
                float acceleration = Mathf.Clamp(profile.resourceSpring * extension + profile.resourceDamping * separatingSpeed,
                    0f, profile.maximumResourceAcceleration);
                targetBody.AddForceAtPosition(-direction * acceleration, point, ForceMode.Acceleration);
            }
            else if (TargetKind == RopeTargetKind.None && endpoint != null)
            {
                Rigidbody endpointBody = endpoint.Body;
                if (endpoint.IsFlying)
                {
                    float separatingSpeed = Vector3.Dot(endpointBody.linearVelocity, direction);
                    float acceleration = Mathf.Clamp(profile.resourceSpring * extension + profile.resourceDamping * separatingSpeed,
                        0f, profile.maximumResourceAcceleration * 2f);
                    endpointBody.AddForce(-direction * acceleration, ForceMode.Acceleration);
                    ConstrainFlyingEndpoint(endpointBody, start, direction, distance);
                }
                else
                {
                    ApplyLandedEndpointPull(endpoint, endpointBody, direction, extension);
                    ApplyLooseEndpointEmergencyLimit(endpointBody, start, direction, distance);
                }
            }
        }

        bool wheelbarrowRighting = TargetKind == RopeTargetKind.Wheelbarrow &&
            TryGetAttachedWheelbarrow(out WheelbarrowController attachedWheelbarrow) &&
            attachedWheelbarrow.State == WheelbarrowState.Righting;
        if (!wheelbarrowRighting && profile.breakOnOverload && TargetKind != RopeTargetKind.None && extension > profile.maximumStretch)
        {
            if (overloadStartedAt < 0f) overloadStartedAt = Time.time;
            else if (Time.time - overloadStartedAt >= profile.overloadDuration) DetachToLooseEnd();
        }
        else overloadStartedAt = -1f;

    }

    private void ApplyWheelbarrowPull(WheelbarrowController wheelbarrow, Vector3 direction, float extension,
        bool blocked)
    {
        if (wheelbarrow == null) return;
        Vector3 localPoint = IsNetworkActive ? targetLocalPointNetwork.Value : localTargetPoint;
        wheelbarrow.ApplyRopeTow(localPoint, direction, NormalizedTension, extension, blocked, profile);
    }

    private bool TryGetAttachedWheelbarrow(out WheelbarrowController wheelbarrow)
    {
        NetworkObject attached = ResolveTarget();
        wheelbarrow = attached != null ? attached.GetComponent<WheelbarrowController>() : null;
        return wheelbarrow != null;
    }

    private static bool CanRemainAttachedToWheelbarrow(WheelbarrowController wheelbarrow)
    {
        return wheelbarrow != null && wheelbarrow.CanRemainRopeAttached;
    }

    private static float CalculateWheelbarrowPullAcceleration(RopeToolProfileSO sourceProfile, float extension,
        float separatingSpeed, float loadRatio, float fixedDeltaTime)
    {
        if (sourceProfile == null || extension <= 0f) return 0f;
        float acceleration = sourceProfile.wheelbarrowSpring * extension +
            sourceProfile.wheelbarrowDamping * separatingSpeed;
        float pullSpeed = -separatingSpeed;
        if (sourceProfile.maximumWheelbarrowPullSpeed > 0f)
        {
            float speedLimitedAcceleration = Mathf.Max(0f,
                (sourceProfile.maximumWheelbarrowPullSpeed - pullSpeed) / Mathf.Max(fixedDeltaTime, 0.0001f));
            acceleration = Mathf.Min(acceleration, speedLimitedAcceleration);
        }

        float loadMultiplier = Mathf.Lerp(1f, sourceProfile.fullLoadWheelbarrowPullMultiplier,
            Mathf.Clamp01(loadRatio));
        return Mathf.Clamp(acceleration, 0f, sourceProfile.maximumWheelbarrowAcceleration) * loadMultiplier;
    }

#if UNITY_EDITOR
    public static bool RunEditorWheelbarrowPullProfileProbe(RopeToolProfileSO sourceProfile, out string result)
    {
        float empty = CalculateWheelbarrowPullAcceleration(sourceProfile, 1f, 0f, 0f, 0.02f);
        float loaded = CalculateWheelbarrowPullAcceleration(sourceProfile, 1f, 0f, 1f, 0.02f);
        float speedLimited = CalculateWheelbarrowPullAcceleration(sourceProfile, 1f,
            -(sourceProfile != null ? sourceProfile.maximumWheelbarrowPullSpeed : 0f), 0f, 0.02f);
        bool passed = sourceProfile != null && empty > 0f && loaded > 0f && loaded < empty &&
            speedLimited <= 0.0001f;
        result = $"empty={empty:F3}, loaded={loaded:F3}, speedLimited={speedLimited:F3}, passed={passed}";
        return passed;
    }
#endif

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    private void ApplyLandedEndpointPull(RopeEndProjectile ropeEndpoint, Rigidbody endpointBody,
        Vector3 outwardDirection, float extension)
    {
        Vector3 pullDirection = -outwardDirection;
        bool grounded = ropeEndpoint.TryGetSupportNormal(profile.endpointGroundProbeDistance,
            profile.obstructionMask, out Vector3 supportNormal);
        if (grounded)
        {
            pullDirection = Vector3.ProjectOnPlane(pullDirection, supportNormal);
        }

        if (pullDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        pullDirection.Normalize();
        float pullSpeed = Vector3.Dot(endpointBody.linearVelocity, pullDirection);
        float acceleration = profile.emptyEndpointSpring * extension - profile.emptyEndpointDamping * pullSpeed;
        acceleration = Mathf.Clamp(acceleration, 0f, profile.maximumEmptyEndpointAcceleration);
        if (pullSpeed >= profile.maximumEmptyEndpointSpeed)
        {
            acceleration = 0f;
        }

        endpointBody.AddForce(pullDirection * acceleration, ForceMode.Acceleration);
    }

    private void ApplyLooseEndpointEmergencyLimit(Rigidbody endpointBody, Vector3 ropeStart,
        Vector3 outwardDirection, float distance)
    {
        float softLimit = CurrentLength + profile.maximumStretch * 2f;
        float emergencyThreshold = CurrentLength + profile.maximumStretch * 4f;
        if (distance <= emergencyThreshold)
        {
            return;
        }

        Vector3 destination = ropeStart + outwardDirection * softLimit;
        Vector3 correction = destination - endpointBody.position;
        float correctionDistance = correction.magnitude;
        if (correctionDistance <= 0.001f)
        {
            return;
        }

        Vector3 correctedPosition = destination;
        if (endpointBody.SweepTest(correction / correctionDistance, out RaycastHit hit,
                correctionDistance, QueryTriggerInteraction.Ignore))
        {
            correctedPosition = endpointBody.position + correction.normalized * Mathf.Max(0f, hit.distance - 0.02f);
        }

        endpointBody.position = correctedPosition;
        float outwardSpeed = Vector3.Dot(endpointBody.linearVelocity, outwardDirection);
        if (outwardSpeed > 0f)
        {
            endpointBody.linearVelocity -= outwardDirection * outwardSpeed;
        }
    }

    private void ConstrainFlyingEndpoint(Rigidbody endpointBody, Vector3 ropeStart, Vector3 direction, float distance)
    {
        if (endpointBody == null || profile == null)
        {
            return;
        }

        float maximumEndpointDistance = CurrentLength + profile.tautDeadZone;
        if (distance <= maximumEndpointDistance)
        {
            return;
        }

        float outwardSpeed = Vector3.Dot(endpointBody.linearVelocity, direction);
        if (outwardSpeed > 0f)
        {
            endpointBody.linearVelocity -= direction * outwardSpeed;
        }

        endpointBody.position = ropeStart + direction * maximumEndpointDistance;
    }

    public bool TryGetPlayerConstraintVelocity(NetworkObject player, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        RopePlayerConstraintSettings settings = PlayerConstraintSettings;
        if (player == null || !settings.IsValid || IsBlocked || TargetKind != RopeTargetKind.Player || !IsDeployedState(CurrentState))
        {
            return false;
        }

        NetworkObject attached = ResolveTarget();
        if (attached == null || (player != NetworkObject && player != attached))
        {
            return false;
        }

        Vector3 delta = RopeEndPosition - RopeStartPosition;
        float distance = delta.magnitude;
        float extension = Mathf.Max(0f, distance - CurrentLength - settings.TautDeadZone);
        if (extension <= 0f) return false;

        Vector3 direction = delta / Mathf.Max(distance, 0.0001f);
        bool isAttachedTarget = player == attached;
        float roleShare = isAttachedTarget ? settings.TargetPullShare : settings.HolderReactionShare;
        float tension = Mathf.Clamp01(extension / Mathf.Max(0.01f, settings.MaximumStretch));
        velocity = direction * settings.PullSpeed * roleShare * tension;
        if (isAttachedTarget) velocity = -velocity;
        return true;
    }

    public bool TryGetPlayerSuspensionData(NetworkObject player, out Vector3 anchorPosition,
        out Vector3 targetPosition, out float ropeLength, out RopePlayerConstraintSettings settings)
    {
        anchorPosition = Vector3.zero;
        targetPosition = Vector3.zero;
        ropeLength = 0f;
        settings = PlayerConstraintSettings;

        if (player == null || !settings.IsValid || IsBlocked || TargetKind != RopeTargetKind.Player
            || !IsDeployedState(CurrentState))
        {
            return false;
        }

        NetworkObject attached = ResolveTarget();
        if (attached == null || player != attached)
        {
            return false;
        }

        anchorPosition = RopeStartPosition;
        targetPosition = RopeEndPosition;
        ropeLength = CurrentLength + settings.TautDeadZone;
        return Vector3.Distance(anchorPosition, targetPosition) >= ropeLength - settings.SwingTautThreshold;
    }

    public bool TryGetPlayerMovementLimit(NetworkObject player, out Vector3 towardOther,
        out float overflow, out float correctionShare)
    {
        towardOther = Vector3.zero;
        overflow = 0f;
        correctionShare = 0f;

        RopePlayerConstraintSettings settings = PlayerConstraintSettings;
        if (player == null || !settings.IsValid || !IsConstraintState(CurrentState))
        {
            return false;
        }

        NetworkObject attached = ResolveTarget();
        bool isHolder = player == NetworkObject;
        bool isAttachedPlayer = TargetKind == RopeTargetKind.Player && attached != null && player == attached;
        if (!isHolder && !isAttachedPlayer)
        {
            return false;
        }

        Vector3 delta = isAttachedPlayer
            ? RopeStartPosition - RopeEndPosition
            : RopeEndPosition - RopeStartPosition;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return false;
        }

        float maximumDistance = CurrentLength + settings.TautDeadZone + settings.MaximumStretch;
        overflow = Mathf.Max(0f, distance - maximumDistance);
        bool atLimit = distance >= maximumDistance - 0.01f;
        if (!atLimit)
        {
            return false;
        }

        towardOther = delta / distance;
        correctionShare = TargetKind == RopeTargetKind.Player
            ? (isAttachedPlayer ? settings.TargetPullShare : settings.HolderReactionShare)
            : 1f;
        return true;
    }

    public void ResolveProjectileCollision(RopeEndProjectile projectile, Collider hitCollider, Vector3 hitPoint)
    {
        if (!HasSimulationAuthority || projectile == null || projectile != endpoint) return;
        if (RopeAttachmentResolver.TryResolve(hitCollider, this, hitPoint, out RopeAttachment attachment)
            && ReserveTarget(attachment.Target))
        {
            Attach(attachment);
        }
        else
        {
            projectile.Land(profile.landedLinearDamping, profile.landedAngularDamping);
            SetLength(Vector3.Distance(RopeStartPosition, projectile.transform.position));
            SetState(RopeState.Loose);
        }
    }

    private void Attach(RopeAttachment attachment)
    {
        target = attachment.Target;
        localTargetKind = attachment.Kind;
        if (IsNetworkActive)
        {
            targetNetworkId.Value = target.NetworkObjectId;
            targetKindNetwork.Value = attachment.Kind;
            targetLocalPointNetwork.Value = attachment.LocalPoint;
        }
        else
        {
            localTargetPoint = attachment.LocalPoint;
        }
        SetLength(Vector3.Distance(RopeStartPosition, RopeEndPosition));
        DestroyEndpoint();
        SetState(RopeState.Attached);
    }

    private Vector3 localTargetPoint;

    private bool ReserveTarget(NetworkObject candidate)
    {
        if (candidate == null || ReservedTargets.ContainsKey(candidate.NetworkObjectId)) return false;
        ReservedTargets[candidate.NetworkObjectId] = this;
        return true;
    }

    private void ReleaseTargetReservation()
    {
        ulong id = IsNetworkActive ? targetNetworkId.Value : target != null ? target.NetworkObjectId : InvalidNetworkObjectId;
        if (id != InvalidNetworkObjectId && ReservedTargets.TryGetValue(id, out RopeToolController rope) && rope == this)
        {
            ReservedTargets.Remove(id);
        }
    }

    private void DetachToLooseEnd()
    {
        if (profile == null) return;
        if (TargetKind == RopeTargetKind.None)
        {
            overloadStartedAt = -1f;
            SetState(RopeState.Loose);
            return;
        }

        Vector3 spawnPosition = RopeEndPosition;
        ReleaseTargetReservation();
        ClearTarget();
        DestroyEndpoint();
        SpawnEndpoint(spawnPosition, Vector3.zero, true);
        SetState(RopeState.Loose);
    }

    private void ResetRope(bool inactive)
    {
        ClearStaminaDrain();
        ReleaseTargetReservation();
        DestroyEndpoint();
        ClearTarget();
        localLength = profile != null ? profile.minimumLength : 0f;
        localTension = 0f;
        localBlocked = false;
        localHardLimit = false;
        localEscapeProgress = 0f;
        targetEscapeStartedAt = -1f;
        escapingTargetClientId = InvalidNetworkObjectId;
        chargeHeld = false;
        deactivationRequested = false;

        if (IsNetworkActive && IsServer)
        {
            currentLengthNetwork.Value = localLength;
            normalizedTensionNetwork.Value = 0f;
            blockedNetwork.Value = false;
            hardLimitNetwork.Value = false;
            escapeProgressNetwork.Value = 0f;
        }
        SetPlayerConstraintSettings(inactive || profile == null
            ? default
            : CreatePlayerConstraintSettings(profile));
        SetState(inactive || profile == null ? RopeState.Inactive : RopeState.Ready);
    }

    private void ClearTarget()
    {
        if (TargetKind == RopeTargetKind.Wheelbarrow &&
            TryGetAttachedWheelbarrow(out WheelbarrowController attachedWheelbarrow))
        {
            attachedWheelbarrow.NotifyRopeTowDetached();
        }

        target = null;
        localTargetKind = RopeTargetKind.None;
        localTargetPoint = Vector3.zero;
        if (IsNetworkActive && IsServer)
        {
            targetNetworkId.Value = InvalidNetworkObjectId;
            targetKindNetwork.Value = RopeTargetKind.None;
            targetLocalPointNetwork.Value = Vector3.zero;
        }
    }

    private void SpawnEndpoint(Vector3 position, Vector3 velocity, bool settleImmediately = false)
    {
        if (profile == null || profile.ropeEndProjectilePrefab == null) return;
        GameObject instance = Instantiate(profile.ropeEndProjectilePrefab, position, Quaternion.identity);
        RopeEndProjectile spawnedEndpoint = instance.GetComponent<RopeEndProjectile>();
        NetworkObject endpointNetworkObject = instance.GetComponent<NetworkObject>();
        if (spawnedEndpoint == null || endpointNetworkObject == null)
        {
            Destroy(instance);
            endpoint = null;
            return;
        }

        endpoint = spawnedEndpoint;
        spawnedEndpoint.Initialize(this, velocity);
        if (settleImmediately)
        {
            spawnedEndpoint.Land(profile.landedLinearDamping, profile.landedAngularDamping);
        }
        if (IsNetworkActive)
        {
            endpointNetworkObject.Spawn(true);
            endpointNetworkId.Value = endpointNetworkObject.NetworkObjectId;
        }
    }

    private void DestroyEndpoint()
    {
        if (endpoint != null)
        {
            if (endpoint.IsSpawned) endpoint.NetworkObject.Despawn(true);
            else Destroy(endpoint.gameObject);
        }
        endpoint = null;
        if (IsNetworkActive && IsServer) endpointNetworkId.Value = InvalidNetworkObjectId;
    }

    private void Throw(float normalizedCharge, Vector3 requestedDirection)
    {
        if (profile == null || CurrentState != RopeState.Charging) return;
        Vector3 direction = ValidateThrowDirection(requestedDirection);
        float speed = Mathf.Lerp(profile.minimumThrowSpeed, profile.maximumThrowSpeed, Mathf.Clamp01(normalizedCharge));
        float minimumThrowLength = Mathf.Clamp(profile.minimumThrowLength, profile.minimumLength, profile.maximumLength);
        SetLength(Mathf.Lerp(minimumThrowLength, profile.maximumLength, Mathf.Clamp01(normalizedCharge)));
        SpawnEndpoint(RopeStartPosition + direction * 0.25f, direction * speed);
        SetState(endpoint != null ? RopeState.Flying : RopeState.Ready);
    }

    private Vector3 GetThrowDirection()
    {
        Camera camera = GetComponentInChildren<Camera>();
        Vector3 forward = camera != null ? camera.transform.forward : transform.forward;
        return (forward + Vector3.up * profile.throwUpwardBias).normalized;
    }

    private Vector3 ValidateThrowDirection(Vector3 requestedDirection)
    {
        Vector3 fallback = transform.forward;
        if (requestedDirection.sqrMagnitude < 0.5f)
        {
            return fallback;
        }

        Vector3 normalized = requestedDirection.normalized;
        return Vector3.Angle(fallback, normalized) <= 85f ? normalized : fallback;
    }

    private void RequestAction(RopeInputAction action, float value, Vector3 direction)
    {
        if (IsNetworkActive && !IsServer) RopeActionServerRpc(action, value, direction);
        else ApplyAction(action, value, direction, OwnerClientId);
    }

    [ServerRpc]
    private void RopeActionServerRpc(RopeInputAction action, float value, Vector3 direction, ServerRpcParams rpcParams = default)
    {
        ApplyAction(action, value, direction, rpcParams.Receive.SenderClientId);
    }

    private void ApplyAction(RopeInputAction action, float value, Vector3 direction, ulong senderClientId)
    {
        if (IsNetworkActive && senderClientId != OwnerClientId) return;
        if (concreteTrap != null && concreteTrap.IsTrapped && action != RopeInputAction.Deactivate) return;
        if (action == RopeInputAction.Activate)
        {
            RefreshSelectedItem(false);
            if (profile != null && CurrentState == RopeState.Inactive)
            {
                ResetRope(false);
            }
            return;
        }
        if (action == RopeInputAction.Deactivate)
        {
            ResetRope(true);
            return;
        }
        if (profile == null) RefreshSelectedItem(false);
        if (profile == null) return;

        switch (action)
        {
            case RopeInputAction.BeginCharge when CurrentState == RopeState.Ready:
                chargeStartedAt = Time.unscaledTime;
                SetState(RopeState.Charging);
                break;
            case RopeInputAction.Throw when CurrentState == RopeState.Charging:
                float serverCharge = Mathf.Clamp01((Time.unscaledTime - chargeStartedAt) / profile.fullChargeDuration);
                Throw(Mathf.Min(Mathf.Clamp01(value), serverCharge + 0.1f), direction);
                break;
            case RopeInputAction.BeginReel when IsDeployedState(CurrentState):
                reelHeld = true;
                payOutHeld = false;
                SetState(RopeState.Reeling);
                break;
            case RopeInputAction.StopReel when CurrentState == RopeState.Reeling:
                reelHeld = false;
                SetState(GetBaseDeployedState());
                break;
            case RopeInputAction.BeginPayOut when IsDeployedState(CurrentState):
                payOutHeld = true;
                reelHeld = false;
                SetState(RopeState.PayingOut);
                break;
            case RopeInputAction.StopPayOut when CurrentState == RopeState.PayingOut:
                payOutHeld = false;
                SetState(GetBaseDeployedState());
                break;
            case RopeInputAction.Detach when TargetKind != RopeTargetKind.None:
                DetachToLooseEnd();
                break;
        }
    }

    public bool CanTargetEscape(RopeToolController targetPlayerRope)
    {
        return profile != null && profile.allowTargetEscape && targetPlayerRope != null && TargetKind == RopeTargetKind.Player
            && ResolveTarget() == targetPlayerRope.NetworkObject && targetPlayerRope.health != null && !targetPlayerRope.health.IsDowned
            && (targetPlayerRope.concreteTrap == null || !targetPlayerRope.concreteTrap.IsTrapped);
    }

    public void RequestTargetEscape(bool held)
    {
        if (concreteTrap != null && concreteTrap.IsTrapped) return;
        if (!TryGetLocalClientId(out ulong localClientId)) return;
        if (IsNetworkActive && !IsServer) TargetEscapeServerRpc(held);
        else ApplyTargetEscape(localClientId, held);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TargetEscapeServerRpc(bool held, ServerRpcParams rpcParams = default)
    {
        ApplyTargetEscape(rpcParams.Receive.SenderClientId, held);
    }

    private void ApplyTargetEscape(ulong senderClientId, bool held)
    {
        NetworkObject attached = ResolveTarget();
        RopeToolController targetRope = attached != null ? attached.GetComponent<RopeToolController>() : null;
        if (attached == null || attached.OwnerClientId != senderClientId || !CanTargetEscape(targetRope)) return;

        escapingTargetClientId = held ? senderClientId : InvalidNetworkObjectId;
        targetEscapeStartedAt = held ? Time.time : -1f;
        SetEscapeProgress(0f);
    }

    private void PredictLength(float deltaTime)
    {
        if (profile == null) return;
        if (reelHeld && !IsBlocked)
        {
            float reelSpeed = TargetKind == RopeTargetKind.None
                ? profile.emptyEndpointReelSpeed
                : profile.attachedTargetReelSpeed;
            float minimumLength = TargetKind == RopeTargetKind.None ? 0f : profile.minimumLength;
            float tensionFloor = GetMinimumLengthAtHardLimit(minimumLength);
            if (localLength > tensionFloor + 0.0001f)
            {
                localLength = Mathf.Max(tensionFloor, localLength - reelSpeed * deltaTime);
            }
        }
        else if (payOutHeld) localLength = Mathf.Min(profile.maximumLength, localLength + profile.payOutSpeed * deltaTime);
        float correctionSpeed = Mathf.Max(profile.emptyEndpointReelSpeed, profile.payOutSpeed);
        localLength = Mathf.MoveTowards(localLength, currentLengthNetwork.Value, correctionSpeed * deltaTime);
        if (reelHeld && !IsBlocked)
        {
            float minimumLength = TargetKind == RopeTargetKind.None ? 0f : profile.minimumLength;
            localLength = Mathf.Max(localLength, GetMinimumLengthAtHardLimit(minimumLength));
        }
    }

    private void SetLength(float value, float? minimumOverride = null)
    {
        if (profile == null) return;
        value = Mathf.Clamp(value, minimumOverride ?? profile.minimumLength, profile.maximumLength);
        localLength = value;
        if (IsNetworkActive && IsServer && (Time.time >= nextLengthSyncAt || Mathf.Abs(currentLengthNetwork.Value - value) > 0.25f))
        {
            currentLengthNetwork.Value = value;
            nextLengthSyncAt = Time.time + 0.1f;
        }
    }

    private bool TryShortenRope(float speed, float deltaTime, float baseMinimumLength)
    {
        if (speed <= 0f || deltaTime <= 0f)
        {
            return false;
        }

        float tensionFloor = GetMinimumLengthAtHardLimit(baseMinimumLength);
        if (CurrentLength <= tensionFloor + 0.0001f)
        {
            return false;
        }

        float nextLength = Mathf.Max(tensionFloor, CurrentLength - speed * deltaTime);
        if (nextLength >= CurrentLength - 0.0001f)
        {
            return false;
        }

        SetLength(nextLength, baseMinimumLength);
        return true;
    }

    private float GetMinimumLengthAtHardLimit(float baseMinimumLength)
    {
        if (profile == null || !IsConstraintState(CurrentState))
        {
            return baseMinimumLength;
        }

        float distance = Vector3.Distance(RopeStartPosition, RopeEndPosition);
        float tensionFloor = distance - profile.tautDeadZone - profile.maximumStretch;
        return Mathf.Clamp(Mathf.Max(baseMinimumLength, tensionFloor), baseMinimumLength, profile.maximumLength);
    }

    private void SetState(RopeState value)
    {
        localState = value;
        if (IsNetworkActive && IsServer) stateNetwork.Value = value;
    }

    private static RopePlayerConstraintSettings CreatePlayerConstraintSettings(RopeToolProfileSO sourceProfile)
    {
        if (sourceProfile == null)
        {
            return default;
        }

        float targetShare = Mathf.Max(0f, sourceProfile.playerTargetPullShare);
        float holderShare = Mathf.Max(0f, sourceProfile.playerHolderReactionShare);
        float totalShare = targetShare + holderShare;
        if (totalShare <= 0.0001f)
        {
            targetShare = 0.9f;
            holderShare = 0.1f;
            totalShare = 1f;
        }

        return new RopePlayerConstraintSettings
        {
            PullSpeed = Mathf.Max(0f, sourceProfile.playerPullSpeed),
            TargetPullShare = targetShare / totalShare,
            HolderReactionShare = holderShare / totalShare,
            TautDeadZone = Mathf.Max(0f, sourceProfile.tautDeadZone),
            MaximumStretch = Mathf.Max(0.01f, sourceProfile.maximumStretch),
            SwingGravityMultiplier = Mathf.Max(0f, sourceProfile.suspendedSwingGravityMultiplier),
            SwingInputAcceleration = Mathf.Max(0f, sourceProfile.suspendedSwingInputAcceleration),
            SwingDamping = Mathf.Max(0f, sourceProfile.suspendedSwingDamping),
            MaximumSwingSpeed = Mathf.Max(0f, sourceProfile.maximumSuspendedSwingSpeed),
            SwingTautThreshold = Mathf.Max(0f, sourceProfile.suspendedTautThreshold),
            WallContactGraceDuration = Mathf.Max(0f, sourceProfile.suspendedWallContactGraceDuration),
            PositionDeadZone = Mathf.Max(0f, sourceProfile.suspendedPositionDeadZone),
            PositionCorrectionSpeed = Mathf.Max(0f, sourceProfile.suspendedPositionCorrectionSpeed),
            PositionCorrectionAcceleration = Mathf.Max(0f, sourceProfile.suspendedPositionCorrectionAcceleration),
            MaximumAnchorTransferSpeed = Mathf.Max(0f, sourceProfile.maximumSuspendedAnchorTransferSpeed),
            GroundedReleaseDelay = Mathf.Max(0f, sourceProfile.suspendedGroundedReleaseDelay),
            UpwardPullThreshold = Mathf.Clamp01(sourceProfile.suspendedUpwardPullThreshold),
            WallJumpOutwardSpeed = Mathf.Max(0f, sourceProfile.wallJumpOutwardSpeed),
            WallJumpUpwardSpeed = Mathf.Max(0f, sourceProfile.wallJumpUpwardSpeed),
            WallJumpCooldown = Mathf.Max(0f, sourceProfile.wallJumpCooldown)
        };
    }

    private void SetPlayerConstraintSettings(RopePlayerConstraintSettings value)
    {
        localPlayerConstraintSettings = value;
        if (IsNetworkActive && IsServer && !playerConstraintSettingsNetwork.Value.Equals(value))
        {
            playerConstraintSettingsNetwork.Value = value;
        }
    }

    private void SetTension(float value)
    {
        localTension = value;
        if (IsNetworkActive && IsServer && Mathf.Abs(normalizedTensionNetwork.Value - value) > 0.03f) normalizedTensionNetwork.Value = value;
    }

    private void SetBlocked(bool value)
    {
        localBlocked = value;
        if (IsNetworkActive && IsServer && blockedNetwork.Value != value) blockedNetwork.Value = value;
    }

    private void SetHardLimit(bool value)
    {
        localHardLimit = value;
        if (IsNetworkActive && IsServer && hardLimitNetwork.Value != value)
        {
            hardLimitNetwork.Value = value;
        }
    }

    private void SetEscapeProgress(float value)
    {
        localEscapeProgress = value;
        if (IsNetworkActive && IsServer && Mathf.Abs(escapeProgressNetwork.Value - value) > 0.02f) escapeProgressNetwork.Value = value;
    }

    private bool IsLineBlocked(Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        float distance = direction.magnitude;
        if (distance <= 0.1f) return false;
        foreach (RaycastHit hit in Physics.SphereCastAll(start, profile.obstructionRadius, direction / distance, distance,
                     profile.obstructionMask, QueryTriggerInteraction.Ignore))
        {
            NetworkObject hitObject = hit.collider.GetComponentInParent<NetworkObject>();
            bool endpointContact = distance - hit.distance <= profile.obstructionRadius * 3f;
            bool endpointSupport = endpoint != null && endpoint.IsLanded && endpoint.IsSupportCollider(hit.collider);
            if (!endpointContact && !endpointSupport && hitObject != NetworkObject && hitObject != ResolveTarget()
                && (endpoint == null || hitObject != endpoint.NetworkObject))
            {
                return true;
            }
        }
        return false;
    }

    private RopeState GetBaseDeployedState()
    {
        return TargetKind == RopeTargetKind.None ? RopeState.Loose : RopeState.Attached;
    }

    private static bool IsDeployedState(RopeState state)
    {
        return state == RopeState.Loose || state == RopeState.Attached || state == RopeState.Reeling || state == RopeState.PayingOut;
    }

    private static bool IsConstraintState(RopeState state)
    {
        return state == RopeState.Flying || IsDeployedState(state);
    }

    private bool CanUseLocally()
    {
        return IsOwnerOrSingleplayer() && IsRopeSelected() && health != null && !health.IsDowned &&
            (concreteTrap == null || !concreteTrap.IsTrapped) && !input.IsGameplayUiOpen;
    }

    internal void CancelForConcreteTrap()
    {
        chargeHeld = false;
        reelHeld = false;
        payOutHeld = false;
        if (IsNetworkActive)
        {
            if (IsServer) ResetRope(true);
            else if (IsOwner) RequestAction(RopeInputAction.Deactivate, 0f, Vector3.zero);
        }
        else
        {
            ResetRope(true);
        }
    }

    public bool IsRopeSelected()
    {
        EquippableItemSO selected = inventory != null ? inventory.GetCurrentSelectedItem() : null;
        return selected != null && selected.itemType == EquippableItemType.Rope && selected.ropeProfile != null;
    }

    private bool IsOwnerOrSingleplayer()
    {
        return !IsNetworkActive || IsOwner;
    }

    private void ClearStaminaDrain()
    {
        if (stamina != null && HasSimulationAuthority) stamina.SetAuthoritativeDrainSource(StaminaDrainSource.RopeReeling, 0f);
    }

    private NetworkObject ResolveTarget()
    {
        if (!IsNetworkActive) return target;
        if (target != null && target.NetworkObjectId == targetNetworkId.Value) return target;
        target = ResolveNetworkObject(targetNetworkId.Value);
        return target;
    }

    private void RefreshReferences()
    {
        if (endpoint == null)
        {
            endpoint = ResolveNetworkObject(endpointNetworkId.Value)?.GetComponent<RopeEndProjectile>();
            if (endpoint != null && HasSimulationAuthority) endpoint.RestoreOwner(this);
        }
        ResolveTarget();
    }

    private NetworkObject ResolveNetworkObject(ulong id)
    {
        if (id == InvalidNetworkObjectId || !IsNetworkActive) return null;
        return NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject found) ? found : null;
    }

    private bool TryGetRopeAttachedToThisPlayer(out RopeToolController source)
    {
        return TryGetRopeAttachedToPlayer(NetworkObject, out source);
    }

    public static bool TryGetRopeAttachedToPlayer(NetworkObject player, out RopeToolController source)
    {
        if (player == null)
        {
            source = null;
            return false;
        }

        foreach (RopeToolController rope in ActiveRopeSet)
        {
            if (rope != null && rope.NetworkObject != player && rope.TargetKind == RopeTargetKind.Player && rope.ResolveTarget() == player)
            {
                source = rope;
                return true;
            }
        }
        source = null;
        return false;
    }

    private bool TryGetLocalClientId(out ulong clientId)
    {
        if (!IsNetworkActive)
        {
            clientId = 0;
            return true;
        }
        clientId = NetworkManager.Singleton.LocalClientId;
        return true;
    }

    private void EnsureHandAnchor()
    {
        if (ropeHandAnchor != null) return;
        Transform existing = transform.Find("RopeHandAnchor");
        if (existing != null) ropeHandAnchor = existing;
        else
        {
            GameObject anchor = new GameObject("RopeHandAnchor");
            anchor.transform.SetParent(transform, false);
            anchor.transform.localPosition = fallbackHandAnchorLocalPosition;
            ropeHandAnchor = anchor.transform;
        }
    }

    private void EnsureLineRenderer()
    {
        line = GetComponent<LineRenderer>();
        if (line == null) line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.textureMode = LineTextureMode.Tile;
        line.material = ropeLineMaterial != null ? ropeLineMaterial : new Material(Shader.Find("Sprites/Default"));
        line.enabled = false;
    }

    private void EnsureTrajectoryPreview()
    {
        if (GetComponent<RopeThrowTrajectoryPreview>() == null)
        {
            gameObject.AddComponent<RopeThrowTrajectoryPreview>();
        }
    }

    public bool CanRenderThrowPreviewLocally()
    {
        return IsOwnerOrSingleplayer() && CurrentState == RopeState.Charging && IsRopeSelected()
            && health != null && !health.IsDowned && input != null && !input.IsGameplayUiOpen;
    }

    public float GetEndpointCollisionRadius()
    {
        if (profile == null || profile.ropeEndProjectilePrefab == null)
        {
            return 0.11f;
        }

        SphereCollider sphere = profile.ropeEndProjectilePrefab.GetComponentInChildren<SphereCollider>();
        if (sphere == null)
        {
            return Mathf.Max(0.03f, profile.obstructionRadius);
        }

        Vector3 scale = sphere.transform.lossyScale;
        return sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    private void UpdateLine()
    {
        if (line == null || profile == null || !IsConstraintState(CurrentState))
        {
            if (line != null) line.enabled = false;
            return;
        }

        Vector3 start = RopeStartPosition;
        Vector3 end = RopeEndPosition;
        float distance = Vector3.Distance(start, end);
        float slack = Mathf.Max(0f, CurrentLength - distance);
        float sag = Mathf.Min(profile.maximumVisualSag, slack * 0.35f) * (1f - NormalizedTension);
        int segments = Mathf.Max(2, profile.lineSegments);
        line.enabled = true;
        line.positionCount = segments;
        line.startWidth = line.endWidth = profile.lineWidth;
        Color color = IsBlocked ? profile.blockedColor : Color.Lerp(profile.relaxedColor, profile.tautColor, NormalizedTension);
        line.startColor = line.endColor = color;
        for (int i = 0; i < segments; i++)
        {
            float t = i / (segments - 1f);
            line.SetPosition(i, Vector3.Lerp(start, end, t) + Vector3.down * (4f * t * (1f - t) * sag));
        }
    }

    private void HandleReplicatedStateChanged(RopeState previous, RopeState current) => localState = current;
    private void HandleLengthChanged(float previous, float current)
    {
        if (!IsOwner)
        {
            localLength = current;
        }
    }
    private void HandleEndpointChanged(ulong previous, ulong current)
    {
        if (endpoint == null || current == InvalidNetworkObjectId || endpoint.NetworkObjectId != current)
        {
            endpoint = null;
        }
    }

    private void HandleTargetChanged(ulong previous, ulong current)
    {
        if (target == null || current == InvalidNetworkObjectId || target.NetworkObjectId != current)
        {
            target = null;
        }
    }

    private enum RopeInputAction
    {
        Activate,
        BeginCharge,
        Throw,
        BeginReel,
        StopReel,
        BeginPayOut,
        StopPayOut,
        Detach,
        Deactivate
    }
}
