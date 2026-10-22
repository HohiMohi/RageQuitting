using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum MountingNailState : byte
{
    Loose,
    Held,
    Prepared,
    Partial,
    EmbeddedSingle,
    EmbeddedPair
}

public struct MountingNailReplicatedAttachment : INetworkSerializable
{
    public MountingNailTargetReference First;
    public MountingNailTargetReference Second;
    public bool HasSecond;
    public Vector3 EntryLocal;
    public Quaternion RotationLocal;
    public Vector3 AxisLocal;
    public Vector3 TipLocal;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref First);
        serializer.SerializeValue(ref Second);
        serializer.SerializeValue(ref HasSecond);
        serializer.SerializeValue(ref EntryLocal);
        serializer.SerializeValue(ref RotationLocal);
        serializer.SerializeValue(ref AxisLocal);
        serializer.SerializeValue(ref TipLocal);
    }
}

/// <summary>Read-only snapshot of which targets a nail embedded into and where it entered the first target.</summary>
public readonly struct MountingNailAttachmentInfo
{
    public readonly MountingNailState State;
    public readonly Transform FirstTarget;
    public readonly Transform SecondTarget;
    public readonly MountingNailTargetReference FirstTargetReference;
    public readonly MountingNailTargetReference SecondTargetReference;
    public readonly Vector3 EntryLocal;
    public readonly Vector3 AxisLocal;

    public MountingNailAttachmentInfo(MountingNailState state, Transform firstTarget, Transform secondTarget,
        MountingNailTargetReference firstTargetReference, MountingNailTargetReference secondTargetReference,
        Vector3 entryLocal, Vector3 axisLocal)
    {
        State = state;
        FirstTarget = firstTarget;
        SecondTarget = secondTarget;
        FirstTargetReference = firstTargetReference;
        SecondTargetReference = secondTargetReference;
        EntryLocal = entryLocal;
        AxisLocal = axisLocal;
    }
}

/// <summary>Owns a single nail's mounting contact, timeout, and world-physics mode.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody), typeof(BaseResourceNew), typeof(NetworkObject))]
[RequireComponent(typeof(CapsuleCollider))]
public sealed partial class MountingNail : NetworkBehaviour, ISingleCarryPlacementCommitListener, IInteractionPromptProvider
{
    private static readonly List<MountingNail> ActiveNails = new();
    private const float RemainingSyncInterval = 0.1f;
    [SerializeField] private MountingNailProfileSO profile;
    [SerializeField] private Transform visual;
    [SerializeField] private Transform tip;
    [SerializeField] private Transform head;
    [SerializeField, Min(0.01f)] private float authoredVisualLengthMeters = 0.35f;
    [SerializeField] private Vector3 authoredVisualScale = new(0.035f, 0.9722222f, 0.035f);
    [SerializeField] private Vector3 authoredVisualCenter = new(0f, -0.13f, 0f);
    [SerializeField, Min(0.0001f)] private float authoredCapsuleRadius = 0.001115125f;

    private readonly NetworkVariable<MountingNailState> replicatedState = new(
        MountingNailState.Loose, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> replicatedPreparedSecondsRemaining = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> replicatedHitCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> replicatedRequiredHitCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> replicatedEmbeddedFinalDepthMeters = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<MountingNailReplicatedAttachment> replicatedAttachment = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private Rigidbody body;
    private BaseResourceNew resource;
    private Collider preparedSurface;
    private Transform preparedTarget;
    private Vector3 surfaceStartPosition;
    private Quaternion surfaceStartRotation;
    private double preparedDeadline;
    private float nextRemainingSync;
    private bool impulseApplied;
    private bool subscribed;
    private MountingNailState offlineState;
    private int offlineHitCount;
    private int offlineRequiredHitCount;
    private float offlineEmbeddedFinalDepthMeters;
    private Transform embeddedTarget;
    private NetworkObject embeddedTargetNetworkObject;
    private Transform embeddedSecondTarget;
    private NetworkObject embeddedSecondTargetNetworkObject;
    private bool embeddedSecondTargetWasSpawnedNetworkObject;
    private bool embeddedPairWasSelected;
    private ulong embeddedTargetSpawnId;
    private ulong embeddedSecondTargetSpawnId;
    private MountingNailTargetReference embeddedTargetReference;
    private MountingNailTargetReference embeddedSecondTargetReference;
    private MountingNailConnectionRegistry connectionRegistry;
    private MountingNailSceneState sceneState;
    private bool firstTargetFollowRegistered;
    private bool embeddedTargetWasSpawnedNetworkObject;
    private bool removalPending;
    private Vector3 embeddedEntryPointLocal;
    private Quaternion embeddedRotationLocal;
    private Vector3 embeddedAxisLocal;
    private Vector3 tipPositionLocal;

    // Extraction owns no target connection until commit; the reservation is managed separately.

    public MountingNailState State => IsNetworkSessionActive() ? replicatedState.Value : offlineState;
    public event Action<MountingNail> StateOrAttachmentChanged;

    internal static void CopyActiveNails(Scene scene, NetworkManager networkManager, List<MountingNail> results)
    {
        if (results == null) return;
        results.Clear();
        for (int i = ActiveNails.Count - 1; i >= 0; i--)
        {
            MountingNail nail = ActiveNails[i];
            if (nail == null)
            {
                ActiveNails.RemoveAt(i);
                continue;
            }
            if (!nail.isActiveAndEnabled || nail.removalPending || nail.gameObject.scene != scene) continue;
            if (networkManager != null && networkManager.IsListening
                && (!nail.IsSpawned || nail.NetworkManager != networkManager)) continue;
            results.Add(nail);
        }
    }
    public MountingNailProfileSO Profile => profile;
    public Transform Tip => tip;
    public Transform Head => head;
    public Collider PreparedSurface => preparedSurface;
    public int HitCount => IsNetworkSessionActive() ? replicatedHitCount.Value : offlineHitCount;
    public int RequiredHitCount => IsNetworkSessionActive() ? replicatedRequiredHitCount.Value : offlineRequiredHitCount;
    public float PenetrationDepthMeters => RequiredHitCount > 0
        ? EmbeddedFinalDepthMeters * HitCount / RequiredHitCount : 0f;
    public float PenetrationProgressNormalized => RequiredHitCount > 0
        ? Mathf.Clamp01((float)HitCount / RequiredHitCount) : 0f;
    private float EmbeddedFinalDepthMeters => IsNetworkSessionActive()
        ? replicatedEmbeddedFinalDepthMeters.Value : offlineEmbeddedFinalDepthMeters;
    public bool BlocksPickup => removalPending || State == MountingNailState.Partial || State == MountingNailState.EmbeddedSingle
        || State == MountingNailState.EmbeddedPair;

    public bool TryGetAttachmentInfo(out MountingNailAttachmentInfo info)
    {
        info = default;
        if (removalPending || !IsEmbeddedState(State) || embeddedTarget == null
            || State == MountingNailState.EmbeddedPair && embeddedSecondTarget == null) return false;
        info = new MountingNailAttachmentInfo(State, embeddedTarget, embeddedSecondTarget,
            embeddedTargetReference, embeddedSecondTargetReference, embeddedEntryPointLocal, embeddedAxisLocal);
        return true;
    }

    internal static float MaximumActiveAimRadius
    {
        get
        {
            float maximum = 0f;
            for (int i = ActiveNails.Count - 1; i >= 0; i--)
            {
                MountingNail nail = ActiveNails[i];
                if (nail == null)
                {
                    ActiveNails.RemoveAt(i);
                    continue;
                }
                if (nail.isActiveAndEnabled && !nail.removalPending && nail.profile != null)
                    maximum = Mathf.Max(maximum, nail.profile.AimRadiusMeters);
            }
            return maximum;
        }
    }

    private void OnEnable()
    {
        if (!ActiveNails.Contains(this)) ActiveNails.Add(this);
    }

    private void OnDisable()
    {
        ActiveNails.Remove(this);
        OnExtractionLifecycleEnded();
        StateOrAttachmentChanged?.Invoke(this);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        resource = GetComponent<BaseResourceNew>();
        ApplyProfileConfiguration();
    }

    private void OnValidate() => ApplyProfileConfiguration();

    private void ApplyProfileConfiguration()
    {
        if (profile == null) return;
        if (body == null) body = GetComponent<Rigidbody>();
        if (body != null) body.mass = profile.MassKg;
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            capsule.direction = 2;
            capsule.center = Vector3.zero;
            capsule.height = profile.LengthMeters;
            capsule.radius = authoredCapsuleRadius * profile.VisualWidthMultiplier;
        }
        if (visual != null && authoredVisualLengthMeters > 0.0001f)
        {
            Vector3 scale = authoredVisualScale;
            scale.y *= profile.LengthMeters / authoredVisualLengthMeters;
            scale.x *= profile.VisualWidthMultiplier;
            scale.z *= profile.VisualWidthMultiplier;
            visual.localScale = scale;
            visual.localPosition = -(visual.localRotation * Vector3.Scale(authoredVisualCenter, scale));
        }
        if (tip != null) tip.localPosition = new Vector3(0f, 0f, profile.LengthMeters * 0.5f);
        if (head != null) head.localPosition = new Vector3(0f, 0f, -profile.LengthMeters * 0.5f);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            extractionSnapshot.Value = default;
            offlineExtractionSnapshot = default;
            ClearEmbeddedTarget();
            replicatedHitCount.Value = 0;
            replicatedRequiredHitCount.Value = 0;
            replicatedEmbeddedFinalDepthMeters.Value = 0f;
            replicatedAttachment.Value = default;
            replicatedState.Value = MountingNailState.Loose;
            replicatedPreparedSecondsRemaining.Value = 0f;
        }
        replicatedState.OnValueChanged += ReplicatedStateChanged;
        replicatedAttachment.OnValueChanged += ReplicatedAttachmentChanged;
        replicatedPreparedSecondsRemaining.OnValueChanged += RemainingChanged;
        subscribed = true;
        ApplyPhysicsForState(State);
        if (!IsServer && IsEmbeddedState(State)) ResolveReplicatedAttachment();
    }

    public override void OnNetworkDespawn()
    {
        OnExtractionLifecycleEnded();
        ReleaseFirstTargetFollow();
        ReleasePairConnection();
        Unsubscribe();
        StateOrAttachmentChanged?.Invoke(this);
    }

    public override void OnDestroy()
    {
        OnExtractionLifecycleEnded();
        ReleaseFirstTargetFollow();
        ReleasePairConnection();
        Unsubscribe();
        StateOrAttachmentChanged?.Invoke(this);
        base.OnDestroy();
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        replicatedState.OnValueChanged -= ReplicatedStateChanged;
        replicatedAttachment.OnValueChanged -= ReplicatedAttachmentChanged;
        replicatedPreparedSecondsRemaining.OnValueChanged -= RemainingChanged;
        subscribed = false;
    }

    private void FixedUpdate()
    {
        if (profile == null || body == null || resource == null) return;
        if (connectionRegistry != null && sceneState == null) connectionRegistry.Tick();
        if (IsNetworkSessionActive() && !IsServer)
        {
            ApplyPhysicsForState(State);
            if (IsEmbeddedState(State))
            {
                ResolveReplicatedAttachment();
                if (embeddedTarget != null) FollowEmbeddedTarget();
            }
            return;
        }

        if (IsEmbeddedState(State))
        {
            if (!IsEmbeddedTargetValid())
            {
                RemoveForDestroyedEmbeddedTarget();
                return;
            }
            ApplyPhysicsForState(State);
            FollowEmbeddedTarget();
            return;
        }

        if (State == MountingNailState.Prepared)
        {
            if (resource.IsPickedUp)
            {
                SetHeldWithoutImpulse();
                return;
            }
            if (!PreparedSurfaceIsStable() || RemainingSeconds() <= 0f)
            {
                ExitPreparedWithImpulse();
                return;
            }
            SyncRemainingSeconds();
        }
        else if (resource.IsPickedUp && State != MountingNailState.Held)
        {
            SetHeldWithoutImpulse();
        }
        else if (State == MountingNailState.Held && !resource.IsPickedUp)
        {
            SetLooseWithoutImpulse();
        }
        else ApplyPhysicsForState(State);
    }

    private void LateUpdate()
    {
        if (profile == null || body == null
            || !IsEmbeddedState(State)
            || IsNetworkSessionActive() && !IsServer) return;
        if (!IsEmbeddedTargetValid())
        {
            RemoveForDestroyedEmbeddedTarget();
            return;
        }
        ApplyPhysicsForState(State);
        FollowEmbeddedTarget();
    }

    /// <summary>Returns whether the proposed nail pose has a tip-first real collider contact.</summary>
    public bool CanPrepareAtPose(Vector3 position, Quaternion rotation)
    {
        if (profile == null || tip == null || resource == null
            || !IsFinite(position) || !IsFinite(rotation)) return false;
        Vector3 localTip = transform.InverseTransformPoint(tip.position);
        Vector3 worldTip = position + rotation * Vector3.Scale(localTip, transform.lossyScale);
        Vector3 worldForward = rotation * transform.InverseTransformDirection(tip.forward);
        if (worldForward.sqrMagnitude < 0.000001f) return false;
        worldForward.Normalize();

        Ray ray = new Ray(worldTip - worldForward * profile.TipContactToleranceMeters, worldForward);
        const float contactQueryEpsilon = 0.0001f;
        RaycastHit[] hits = Physics.RaycastAll(ray,
            profile.TipContactToleranceMeters * 2f + contactQueryEpsilon, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            Collider collider = hit.collider;
            if (collider == null || collider.isTrigger || IsOwnCollider(collider)) continue;
            if (IsCurrentlyCarried(collider)) return false;
            if (Vector3.Dot(hit.point - worldTip, worldForward) < -contactQueryEpsilon) return false;
            float tipDistance = Vector3.Distance(worldTip, hit.point);
            return tipDistance <= profile.TipContactToleranceMeters + contactQueryEpsilon;
        }
        return false;
    }

    public void OnSingleCarryPlacementCommitted(PlayerInteractionNew holder)
    {
        if (BlocksPickup) return;
        if (profile == null || tip == null || body == null || resource == null
            || IsNetworkSessionActive() && !IsServer || !CanPrepareAtPose(transform.position, transform.rotation))
            return;

        if (!TryFindPreparedSurface(out Collider surface)) return;
        preparedSurface = surface;
        preparedTarget = ResolveTargetRoot(surface);
        surfaceStartPosition = preparedTarget.position;
        surfaceStartRotation = preparedTarget.rotation;
        preparedDeadline = Time.timeAsDouble + profile.PreparedTimeoutSeconds;
        impulseApplied = false;
        if (IsNetworkSessionActive())
        {
            replicatedPreparedSecondsRemaining.Value = profile.PreparedTimeoutSeconds;
            replicatedState.Value = MountingNailState.Prepared;
        }
        else offlineState = MountingNailState.Prepared;
        ApplyPhysicsForState(MountingNailState.Prepared);
    }

    public void OnResourcePickedUpChanged(bool isPickedUp)
    {
        if (profile == null || resource == null || BlocksPickup) return;
        if (isPickedUp)
        {
            if (!IsNetworkSessionActive() || IsServer) SetHeldWithoutImpulse();
        }
        else if (State == MountingNailState.Held)
        {
            if (!IsNetworkSessionActive() || IsServer) SetLooseWithoutImpulse();
        }
    }

    private bool TryFindPreparedSurface(out Collider surface)
    {
        return TryFindPreparedSurface(out surface, out _);
    }

    private bool TryFindPreparedSurface(out Collider surface, out Vector3 contactPoint)
    {
        surface = null;
        contactPoint = default;
        Vector3 direction = tip.forward;
        if (direction.sqrMagnitude < 0.000001f) return false;
        direction.Normalize();
        const float contactQueryEpsilon = 0.0001f;
        RaycastHit[] hits = Physics.RaycastAll(tip.position - direction * profile.TipContactToleranceMeters,
            direction, profile.TipContactToleranceMeters * 2f + contactQueryEpsilon, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger || IsOwnCollider(hit.collider)) continue;
            if (IsCurrentlyCarried(hit.collider)) return false;
            if (Vector3.Dot(hit.point - tip.position, direction) < -contactQueryEpsilon) return false;
            if (Vector3.Distance(tip.position, hit.point) > profile.TipContactToleranceMeters + contactQueryEpsilon)
                return false;
            surface = hit.collider;
            contactPoint = hit.point;
            return true;
        }
        return false;
    }

    private bool PreparedSurfaceIsStable()
    {
        if (preparedTarget == null || !preparedTarget.gameObject.activeInHierarchy)
            return false;
        if (Vector3.Distance(preparedTarget.position, surfaceStartPosition)
                > profile.AllowedTargetDisplacementMeters
            || Quaternion.Angle(preparedTarget.rotation, surfaceStartRotation)
                > profile.AllowedTargetRotationDegrees) return false;
        return TryFindPreparedSurface(out Collider currentSurface)
            && ResolveTargetRoot(currentSurface) == preparedTarget;
    }

    private float RemainingSeconds()
    {
        return Mathf.Max(0f, (float)(preparedDeadline - Time.timeAsDouble));
    }

    private void SyncRemainingSeconds()
    {
        float remaining = RemainingSeconds();
        if (Time.time < nextRemainingSync) return;
        nextRemainingSync = Time.time + RemainingSyncInterval;
        if (IsNetworkSessionActive()) replicatedPreparedSecondsRemaining.Value = remaining;
    }

    private void ExitPreparedWithImpulse()
    {
        Collider lastSurface = preparedSurface;
        Vector3 nailAxis = tip != null ? tip.forward.normalized : transform.forward;
        Vector3 lateral = lastSurface != null
            ? Vector3.ProjectOnPlane(transform.position - lastSurface.ClosestPoint(transform.position), nailAxis)
            : Vector3.zero;
        if (lateral.sqrMagnitude < 0.0001f) lateral = Vector3.ProjectOnPlane(Vector3.up, nailAxis);
        if (lateral.sqrMagnitude < 0.0001f) lateral = Vector3.ProjectOnPlane(Vector3.right, nailAxis);
        if (lateral.sqrMagnitude < 0.0001f) lateral = Vector3.forward;
        SetState(MountingNailState.Loose);
        ApplyPhysicsForState(MountingNailState.Loose);
        if (impulseApplied || body == null || body.isKinematic) return;
        impulseApplied = true;
        Vector3 impulsePoint = head != null ? head.position : transform.position + nailAxis * profile.LengthMeters * 0.5f;
        body.AddForceAtPosition(lateral.normalized * profile.ExitLateralImpulseMetersPerSecond,
            impulsePoint, ForceMode.VelocityChange);
    }

    private void SetLooseWithoutImpulse()
    {
        SetState(MountingNailState.Loose);
        ApplyPhysicsForState(MountingNailState.Loose);
    }

    private void SetHeldWithoutImpulse()
    {
        SetState(MountingNailState.Held);
        ApplyPhysicsForState(MountingNailState.Held);
    }

    private void SetState(MountingNailState state)
    {
        if (IsNetworkSessionActive())
        {
            if (!IsServer) return;
            replicatedPreparedSecondsRemaining.Value = 0f;
            replicatedState.Value = state;
        }
        else offlineState = state;
        StateOrAttachmentChanged?.Invoke(this);
        preparedSurface = null;
        preparedTarget = null;
    }

    /// <summary>Applies one validated IndustrialHammer hit to a prepared or partially embedded nail.</summary>
    public bool TryApplyHammerHit(EquippableItemType hammerType)
    {
        if (removalPending || hammerType != EquippableItemType.IndustrialHammer || profile == null || body == null || resource == null)
            return false;
        if (IsNetworkSessionActive() && !IsServer) return false;
        MountingNailState currentState = State;
        if (currentState == MountingNailState.EmbeddedSingle || currentState == MountingNailState.EmbeddedPair) return false;

        if (currentState == MountingNailState.Prepared)
        {
            if (RemainingSeconds() <= 0f || !PreparedSurfaceIsStable() || resource.IsSecuredForTransport)
            {
                ExitPreparedWithImpulse();
                return false;
            }
            Transform targetRoot = preparedTarget;
            if (targetRoot == null || !TryFindPreparedSurface(out Collider surface, out Vector3 entryPoint)
                || ResolveTargetRoot(surface) != targetRoot || IsCurrentlyCarried(surface)
                || WouldCreateEmbedCycle(targetRoot))
            {
                ExitPreparedWithImpulse();
                return false;
            }

            Vector3 selectedAxis = tip.forward.normalized;
            Transform selectedSecond = !IsActorRoot(targetRoot)
                && TryFindSecondPairTarget(targetRoot, entryPoint, selectedAxis, out Transform candidate)
                ? candidate : null;
            if (selectedSecond != null && !CanReferencePair(targetRoot, selectedSecond)) selectedSecond = null;
            SetEmbeddedTargets(targetRoot, selectedSecond, entryPoint, selectedAxis);
            selectedSecond = embeddedSecondTarget;
            int required = Mathf.Max(1, selectedSecond != null
                ? profile.RequiredHitsForTwoObjects : profile.RequiredHitsForSingleObject);
            SetHitCounts(1, required, profile.FinalDepthMeters);
            preparedDeadline = 0d;
            if (IsNetworkSessionActive()) replicatedPreparedSecondsRemaining.Value = 0f;
            if (required == 1 && selectedSecond != null)
            {
                if (IsPairStillValid() && TryRegisterPairConnection()) SetState(MountingNailState.EmbeddedPair);
                else CompleteEmbeddedSingle();
            }
            else SetState(required == 1 ? MountingNailState.EmbeddedSingle : MountingNailState.Partial);
            ApplyPhysicsForState(State);
            FollowEmbeddedTarget();
            return true;
        }

        if (currentState != MountingNailState.Partial) return false;
        if (!IsEmbeddedTargetValid())
        {
            RemoveForDestroyedEmbeddedTarget();
            return false;
        }
        int requiredHits = Mathf.Max(1, RequiredHitCount);
        int nextHit = Mathf.Min(requiredHits, HitCount + 1);
        if (nextHit >= requiredHits && embeddedPairWasSelected)
        {
            if (!IsEmbeddedTargetValid())
            {
                RemoveForDestroyedEmbeddedTarget();
                return false;
            }
            if (!IsPairStillValid())
            {
                CompleteEmbeddedSingle();
                return true;
            }
            if (!TryRegisterPairConnection())
            {
                CompleteEmbeddedSingle();
                return true;
            }
        }
        SetHitCounts(nextHit, requiredHits, EmbeddedFinalDepthMeters);
        MountingNailState nextState = nextHit >= requiredHits
            ? embeddedPairWasSelected && embeddedSecondTarget != null ? MountingNailState.EmbeddedPair : MountingNailState.EmbeddedSingle
            : MountingNailState.Partial;
        SetState(nextState);
        ApplyPhysicsForState(nextState);
        FollowEmbeddedTarget();
        return true;
    }

    /// <summary>Applies a hammer impact to a prepared/embedded nail or kicks a loose nail away from the authoritative attacker.</summary>
    public bool TryApplyHammerImpact(EquippableItemType hammerType, Vector3 attackerPosition, Vector3 attackerForward)
    {
        if (removalPending || hammerType != EquippableItemType.IndustrialHammer || profile == null || body == null || resource == null
            || !IsFinite(attackerPosition) || !IsFinite(attackerForward)
            || IsNetworkSessionActive() && !IsServer) return false;

        MountingNailState state = State;
        if (state == MountingNailState.Prepared)
        {
            if (TryApplyHammerHit(hammerType)) return true;
            // The embedding API deliberately keeps its historic behavior: an expired or invalid preparation
            // becomes loose and returns false. At an actual impact that newly loose nail can still be kicked.
            if (State != MountingNailState.Loose) return false;
        }
        else if (state == MountingNailState.Partial || state == MountingNailState.EmbeddedSingle
                 || state == MountingNailState.EmbeddedPair)
        {
            return TryApplyHammerHit(hammerType);
        }

        if (State != MountingNailState.Loose || resource.IsPickedUp || resource.IsSecuredForTransport
            || MountingNailSceneState.IsTargetConnected(transform)) return false;

        Vector3 away = transform.position - attackerPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.000001f)
        {
            away = attackerForward;
            away.y = 0f;
        }
        if (away.sqrMagnitude < 0.000001f) away = Vector3.forward;
        away.Normalize();
        Vector3 deltaVelocity = away * profile.LooseHammerHorizontalImpulseMetersPerSecond
            + Vector3.up * profile.LooseHammerVerticalImpulseMetersPerSecond;
        if (!IsFinite(deltaVelocity)) return false;

        if (IsNetworkSessionActive() && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
            TransferLooseNailAuthorityToServer();
        ApplyPhysicsForState(MountingNailState.Loose);
        if (body.isKinematic) return false;
        body.AddForce(deltaVelocity, ForceMode.VelocityChange);
        return true;
    }

    private void TransferLooseNailAuthorityToServer()
    {
        NetworkTransform[] transforms = NetworkObject.GetComponentsInChildren<NetworkTransform>(true);
        Vector3[] positions = new Vector3[transforms.Length];
        Quaternion[] rotations = new Quaternion[transforms.Length];
        Vector3[] scales = new Vector3[transforms.Length];
        for (int i = 0; i < transforms.Length; i++)
        {
            NetworkTransform networkTransform = transforms[i];
            if (networkTransform == null || !networkTransform.IsSpawned
                || networkTransform.NetworkObject != NetworkObject) continue;
            positions[i] = networkTransform.GetSpaceRelativePosition();
            rotations[i] = networkTransform.GetSpaceRelativeRotation();
            scales[i] = networkTransform.transform.localScale;
        }

        Vector3 velocity = body.linearVelocity;
        Vector3 angularVelocity = body.angularVelocity;
        NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        for (int i = 0; i < transforms.Length; i++)
        {
            NetworkTransform networkTransform = transforms[i];
            if (networkTransform != null && networkTransform.IsSpawned
                && networkTransform.NetworkObject == NetworkObject)
                networkTransform.Teleport(positions[i], rotations[i], scales[i]);
        }
        ApplyPhysicsForState(MountingNailState.Loose);
        if (!body.isKinematic)
        {
            body.linearVelocity = velocity;
            body.angularVelocity = angularVelocity;
        }
    }

    public bool TryGetExposedAimSegment(out Vector3 segmentStart, out Vector3 segmentEnd)
    {
        segmentStart = default;
        segmentEnd = default;
        if (tip == null || head == null || profile == null || !isActiveAndEnabled) return false;
        segmentStart = head.position;
        if (IsEmbeddedState(State))
        {
            segmentEnd = tip.position - tip.forward.normalized * PenetrationDepthMeters;
        }
        else if (State == MountingNailState.Prepared && preparedSurface != null)
        {
            segmentEnd = preparedSurface.ClosestPoint(tip.position);
        }
        else segmentEnd = tip.position;
        return (segmentEnd - segmentStart).sqrMagnitude > 0.000001f;
    }

    public void GetInteractionPrompts(Transform interactor, System.Collections.Generic.List<InteractionPrompt> prompts)
    {
        if (prompts == null) return;
        if (State == MountingNailState.Prepared)
        {
            if (resource != null && resource.CanBePickedUp && !resource.IsPickedUp)
            {
                prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Pick up"));
            }
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action, "Hammer nail"));
            return;
        }
        if (!BlocksPickup) return;
        if (State == MountingNailState.EmbeddedSingle || State == MountingNailState.EmbeddedPair)
        {
            prompts.Add(new InteractionPrompt(PlayerInputActionKind.Information, "Nail mounted"));
            return;
        }
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action,
            $"Hammer nail ({HitCount}/{RequiredHitCount})"));
    }

    private void SetHitCounts(int hitCount, int requiredHitCount, float finalDepthMeters)
    {
        if (IsNetworkSessionActive())
        {
            if (!IsServer) return;
            replicatedHitCount.Value = hitCount;
            replicatedRequiredHitCount.Value = requiredHitCount;
            replicatedEmbeddedFinalDepthMeters.Value = finalDepthMeters;
        }
        else
        {
            offlineHitCount = hitCount;
            offlineRequiredHitCount = requiredHitCount;
            offlineEmbeddedFinalDepthMeters = finalDepthMeters;
        }
    }

    private void FollowEmbeddedTarget()
    {
        if (embeddedTarget == null || tip == null) return;
        float depth = PenetrationDepthMeters;
        Vector3 axis = embeddedTarget.TransformDirection(embeddedAxisLocal).normalized;
        Vector3 entry = embeddedTarget.TransformPoint(embeddedEntryPointLocal);
        Quaternion rotation = embeddedTarget.rotation * embeddedRotationLocal;
        Vector3 desiredTip = entry + axis * depth;
        transform.SetPositionAndRotation(desiredTip - rotation * tipPositionLocal, rotation);
        if (body != null && body.isKinematic)
        {
            body.position = transform.position;
            body.rotation = transform.rotation;
        }
    }

    private bool IsEmbeddedTargetValid()
    {
        return embeddedTarget != null && embeddedTarget.gameObject.activeInHierarchy
            && (!embeddedTargetWasSpawnedNetworkObject || embeddedTargetNetworkObject != null
                && embeddedTargetNetworkObject.IsSpawned && embeddedTargetNetworkObject.NetworkManager == NetworkManager
                && embeddedTargetNetworkObject.NetworkObjectId == embeddedTargetSpawnId);
    }

    private bool IsPairStillValid()
    {
        if (!IsEmbeddedTargetValid() || embeddedSecondTarget == null || !embeddedSecondTarget.gameObject.activeInHierarchy)
            return false;
        if (embeddedSecondTargetWasSpawnedNetworkObject
            && (embeddedSecondTargetNetworkObject == null || !embeddedSecondTargetNetworkObject.IsSpawned
                || embeddedSecondTargetNetworkObject.NetworkManager != NetworkManager
                || embeddedSecondTargetNetworkObject.NetworkObjectId != embeddedSecondTargetSpawnId)) return false;
        if (IsTargetCarried(embeddedTarget) || IsTargetCarried(embeddedSecondTarget)) return false;
        Vector3 entry = embeddedTarget.TransformPoint(embeddedEntryPointLocal);
        Vector3 axis = embeddedTarget.TransformDirection(embeddedAxisLocal).normalized;
        if (axis.sqrMagnitude < 0.000001f) return false;
        const float backOffset = 0.001f;
        Vector3 rayStart = entry - axis * backOffset;
        RaycastHit[] hits = Physics.RaycastAll(rayStart, axis, profile.PenetrationRangeMeters + backOffset,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        bool originalFirstContactSeen = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger || IsOwnCollider(hit.collider)) continue;
            Transform root = ResolveTargetRoot(hit.collider);
            if (root == null) continue;
            float distanceFromEntry = Vector3.Dot(hit.point - entry, axis);
            if (distanceFromEntry < -backOffset || distanceFromEntry > profile.PenetrationRangeMeters) continue;
            if (root == embeddedTarget)
            {
                originalFirstContactSeen = true;
                continue;
            }
            if (!originalFirstContactSeen) return false;
            return root == embeddedSecondTarget && !IsActorRoot(root) && IsPairEligible(root)
                && !IsCurrentlyCarried(hit.collider);
        }
        return false;
    }

    internal bool IsConnectionTargetAlive(bool first)
    {
        Transform target = first ? embeddedTarget : embeddedSecondTarget;
        if (target == null || !target.gameObject.activeInHierarchy) return false;
        NetworkObject networkObject = first ? embeddedTargetNetworkObject : embeddedSecondTargetNetworkObject;
        bool wasSpawned = first ? embeddedTargetWasSpawnedNetworkObject : embeddedSecondTargetWasSpawnedNetworkObject;
        ulong spawnId = first ? embeddedTargetSpawnId : embeddedSecondTargetSpawnId;
        return !wasSpawned || networkObject != null && networkObject.IsSpawned
            && networkObject.NetworkManager == NetworkManager && networkObject.NetworkObjectId == spawnId;
    }

    internal MountingNailTargetReference ConnectionTargetReference(bool first) =>
        first ? embeddedTargetReference : embeddedSecondTargetReference;

    private void SetEmbeddedTargets(Transform first, Transform second, Vector3 entryPoint, Vector3 worldAxis)
    {
        embeddedTarget = first;
        embeddedSecondTarget = second;
        embeddedPairWasSelected = second != null;
        embeddedEntryPointLocal = first.InverseTransformPoint(entryPoint);
        embeddedRotationLocal = Quaternion.Inverse(first.rotation) * transform.rotation;
        embeddedAxisLocal = first.InverseTransformDirection(worldAxis).normalized;
        tipPositionLocal = transform.InverseTransformPoint(tip.position);
        CacheTargetNetworkObject(first, out embeddedTargetNetworkObject, out embeddedTargetWasSpawnedNetworkObject,
            out embeddedTargetSpawnId);
        CacheTargetNetworkObject(second, out embeddedSecondTargetNetworkObject,
            out embeddedSecondTargetWasSpawnedNetworkObject, out embeddedSecondTargetSpawnId);
        embeddedTargetReference = default;
        embeddedSecondTargetReference = default;
        if (IsNetworkSessionActive())
        {
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned
                && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
                NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
            sceneState = FindSceneState();
            if (sceneState != null)
            {
                sceneState.TryCreateReference(first, out embeddedTargetReference);
                if (second != null) sceneState.TryCreateReference(second, out embeddedSecondTargetReference);
                if (IsServer && embeddedTargetReference.IsValid && !embeddedTargetReference.IsNetworkObject)
                    firstTargetFollowRegistered = sceneState.SetTargetFollowed(embeddedTargetReference, first, true);
            }
            if (second != null && (!embeddedTargetReference.IsValid || !embeddedSecondTargetReference.IsValid))
            {
                embeddedSecondTarget = null;
                embeddedPairWasSelected = false;
                embeddedSecondTargetNetworkObject = null;
                embeddedSecondTargetWasSpawnedNetworkObject = false;
                embeddedSecondTargetSpawnId = 0;
            }
            replicatedAttachment.Value = CreateReplicatedAttachment();
        }
        StateOrAttachmentChanged?.Invoke(this);
    }

    private void CacheTargetNetworkObject(Transform target, out NetworkObject networkObject, out bool wasSpawned,
        out ulong spawnId)
    {
        networkObject = target != null ? target.GetComponentInParent<NetworkObject>() : null;
        wasSpawned = networkObject != null && networkObject.IsSpawned;
        spawnId = wasSpawned ? networkObject.NetworkObjectId : 0;
    }

    private bool CanReferencePair(Transform first, Transform second)
    {
        if (!IsNetworkSessionActive()) return true;
        sceneState = FindSceneState();
        return sceneState != null && sceneState.TryCreateReference(first, out MountingNailTargetReference firstReference)
            && sceneState.TryCreateReference(second, out MountingNailTargetReference secondReference)
            && firstReference.IsValid && secondReference.IsValid;
    }

    private bool TryRegisterPairConnection()
    {
        if (embeddedSecondTarget == null || WouldCreateEmbedCycle(embeddedSecondTarget)) return false;
        if (IsNetworkSessionActive())
        {
            sceneState = FindSceneState();
            if (sceneState == null || !IsServer || !embeddedTargetReference.IsValid || !embeddedSecondTargetReference.IsValid)
                return false;
            connectionRegistry = sceneState.Connections;
        }
        else connectionRegistry = MountingNailConnectionRegistry.ForOfflineScene(gameObject.scene);
        return connectionRegistry != null && connectionRegistry.Add(this, embeddedTarget, embeddedSecondTarget,
            embeddedTargetReference, embeddedSecondTargetReference,
            embeddedTarget.TransformPoint(embeddedEntryPointLocal));
    }

    private MountingNailReplicatedAttachment CreateReplicatedAttachment() => new()
    {
        First = embeddedTargetReference,
        Second = embeddedSecondTargetReference,
        HasSecond = embeddedSecondTarget != null,
        EntryLocal = embeddedEntryPointLocal,
        RotationLocal = embeddedRotationLocal,
        AxisLocal = embeddedAxisLocal,
        TipLocal = tipPositionLocal
    };

    private void ResolveReplicatedAttachment()
    {
        if (!IsNetworkSessionActive() || IsServer) return;
        MountingNailReplicatedAttachment attachment = replicatedAttachment.Value;
        if (!attachment.First.IsValid) return;
        sceneState = FindSceneState();
        if (sceneState == null) return;
        Transform previousFirst = embeddedTarget;
        Transform previousSecond = embeddedSecondTarget;
        MountingNailTargetReference previousFirstReference = embeddedTargetReference;
        MountingNailTargetReference previousSecondReference = embeddedSecondTargetReference;
        Vector3 previousEntry = embeddedEntryPointLocal;
        Vector3 previousAxis = embeddedAxisLocal;
        if (embeddedTarget == null || !embeddedTargetReference.Equals(attachment.First))
        {
            if (!sceneState.TryResolve(attachment.First, out embeddedTarget)) return;
        }
        embeddedTargetReference = attachment.First;
        embeddedSecondTargetReference = attachment.Second;
        embeddedPairWasSelected = attachment.HasSecond;
        embeddedSecondTarget = null;
        bool secondResolved = !attachment.HasSecond || sceneState.TryResolve(attachment.Second, out embeddedSecondTarget);
        embeddedEntryPointLocal = attachment.EntryLocal;
        embeddedRotationLocal = attachment.RotationLocal;
        embeddedAxisLocal = attachment.AxisLocal;
        tipPositionLocal = attachment.TipLocal;
        CacheTargetNetworkObject(embeddedTarget, out embeddedTargetNetworkObject,
            out embeddedTargetWasSpawnedNetworkObject, out embeddedTargetSpawnId);
        if (secondResolved)
            CacheTargetNetworkObject(embeddedSecondTarget, out embeddedSecondTargetNetworkObject,
                out embeddedSecondTargetWasSpawnedNetworkObject, out embeddedSecondTargetSpawnId);
        else
        {
            embeddedSecondTargetNetworkObject = null;
            embeddedSecondTargetWasSpawnedNetworkObject = attachment.HasSecond && attachment.Second.IsNetworkObject;
            embeddedSecondTargetSpawnId = attachment.Second.NetworkObjectId;
        }
        if (State == MountingNailState.EmbeddedPair && (!secondResolved || embeddedSecondTarget == null)) return;
        bool changed = previousFirst != embeddedTarget || previousSecond != embeddedSecondTarget
            || !previousFirstReference.Equals(embeddedTargetReference)
            || !previousSecondReference.Equals(embeddedSecondTargetReference)
            || previousEntry != embeddedEntryPointLocal || previousAxis != embeddedAxisLocal;
        if (changed) StateOrAttachmentChanged?.Invoke(this);
    }

    private MountingNailSceneState FindSceneState()
    {
        if (sceneState != null && sceneState.IsSpawned
            && sceneState.ScopedNetworkManager == NetworkManager
            && sceneState.gameObject.scene == gameObject.scene) return sceneState;
        foreach (MountingNailSceneState candidate in FindObjectsByType<MountingNailSceneState>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (candidate != null && candidate.IsSpawned && candidate.ScopedNetworkManager == NetworkManager
                && candidate.gameObject.scene == gameObject.scene)
                return candidate;
        return null;
    }

    private void CompleteEmbeddedSingle()
    {
        ReleasePairConnection();
        embeddedSecondTarget = null;
        embeddedPairWasSelected = false;
        embeddedSecondTargetNetworkObject = null;
        embeddedSecondTargetWasSpawnedNetworkObject = false;
        embeddedSecondTargetSpawnId = 0;
        embeddedSecondTargetReference = default;
        if (IsNetworkSessionActive() && IsServer) replicatedAttachment.Value = CreateReplicatedAttachment();
        SetHitCounts(RequiredHitCount, RequiredHitCount, EmbeddedFinalDepthMeters);
        SetState(MountingNailState.EmbeddedSingle);
        ApplyPhysicsForState(State);
        FollowEmbeddedTarget();
    }

    private void ReleasePairConnection(NetworkObject deferOwnershipRestoreFor = null)
    {
        if (connectionRegistry != null)
        {
            if (deferOwnershipRestoreFor != null)
                connectionRegistry.RemoveForPanelReset(this, deferOwnershipRestoreFor);
            else
                connectionRegistry.Remove(this);
        }
        connectionRegistry = null;
    }

    internal MountingNailConnectionRegistry RemoveForPanelReset(NetworkObject deferOwnershipRestoreFor)
    {
        if (removalPending || !IsEmbeddedState(State)
            || NetworkManager != null && NetworkManager.IsListening && !IsServer) return null;
        return RemoveEmbeddedNail(deferOwnershipRestoreFor);
    }

    /// <summary>Removes an embedded nail without recovery when an authorized extraction operation succeeds.</summary>
    internal bool TryExtractForCrowbar()
    {
        if (removalPending || !isActiveAndEnabled || !gameObject.activeInHierarchy || !IsEmbeddedState(State)
            || !IsConnectionTargetAlive(true)
            || State == MountingNailState.EmbeddedPair
                && !IsConnectionTargetAlive(false)) return false;

        bool networkSessionActive = IsNetworkSessionActive();
        if (NetworkManager != null && NetworkManager.IsListening && (!networkSessionActive || !IsSpawned || !IsServer))
            return false;
        if (!networkSessionActive && IsSpawned) return false;

        if (BridgeDeckPanelSectionNetworkState.IsPanelProtectedFromExtraction(embeddedTarget)
            || State == MountingNailState.EmbeddedPair
                && BridgeDeckPanelSectionNetworkState.IsPanelProtectedFromExtraction(embeddedSecondTarget)) return false;

        RemoveEmbeddedNail(null);
        return removalPending;
    }

    private MountingNailConnectionRegistry RemoveEmbeddedNail(NetworkObject deferOwnershipRestoreFor)
    {
        removalPending = true;
        MountingNailConnectionRegistry registry = connectionRegistry;
        ClearEmbeddedTarget(deferOwnershipRestoreFor);
        SetHitCounts(0, 0, 0f);
        SetState(MountingNailState.Loose);
        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
        }
        Collider[] colliders = GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = false;

        if (IsNetworkSessionActive())
        {
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        }
        else Destroy(gameObject);
        return registry;
    }

    private void ReleaseFirstTargetFollow()
    {
        if (!firstTargetFollowRegistered) return;
        if (sceneState != null && embeddedTarget != null && embeddedTargetReference.IsValid)
            sceneState.SetTargetFollowed(embeddedTargetReference, embeddedTarget, false);
        firstTargetFollowRegistered = false;
    }

    internal void ConnectionFirstTargetRemoved() => RemoveForDestroyedEmbeddedTarget();
    internal void ConnectionSecondTargetRemoved() => CompleteEmbeddedSingle();
    internal void ConnectionRegistryCleared(MountingNailConnectionRegistry registry)
    {
        if (connectionRegistry != registry) return;
        connectionRegistry = null;
        if (IsEmbeddedState(State) && embeddedSecondTarget != null) CompleteEmbeddedSingle();
    }

    private void ClearEmbeddedTarget(NetworkObject deferOwnershipRestoreFor = null)
    {
        ReleaseFirstTargetFollow();
        ReleasePairConnection(deferOwnershipRestoreFor);
        embeddedTarget = null;
        embeddedTargetNetworkObject = null;
        embeddedTargetWasSpawnedNetworkObject = false;
        embeddedTargetSpawnId = 0;
        embeddedSecondTarget = null;
        embeddedPairWasSelected = false;
        embeddedSecondTargetNetworkObject = null;
        embeddedSecondTargetWasSpawnedNetworkObject = false;
        embeddedSecondTargetSpawnId = 0;
        embeddedTargetReference = default;
        embeddedSecondTargetReference = default;
        sceneState = null;
        if (IsNetworkSessionActive() && IsServer) replicatedAttachment.Value = default;
        embeddedEntryPointLocal = Vector3.zero;
        embeddedRotationLocal = Quaternion.identity;
        embeddedAxisLocal = Vector3.zero;
        tipPositionLocal = Vector3.zero;
        offlineHitCount = 0;
        offlineRequiredHitCount = 0;
        offlineEmbeddedFinalDepthMeters = 0f;
        StateOrAttachmentChanged?.Invoke(this);
    }

    private bool WouldCreateEmbedCycle(Transform targetRoot)
    {
        if (targetRoot == null) return true;
        HashSet<MountingNail> visited = new();
        Transform cursor = targetRoot;
        while (cursor != null)
        {
            MountingNail targetNail = cursor.GetComponent<MountingNail>();
            if (targetNail == null) targetNail = cursor.GetComponentInParent<MountingNail>();
            if (targetNail == null) return false;
            if (targetNail == this || !visited.Add(targetNail)) return true;
            cursor = targetNail.embeddedTarget;
        }
        return false;
    }

    private void RemoveForDestroyedEmbeddedTarget()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        }
        else Destroy(gameObject);
    }

    private void ApplyPhysicsForState(MountingNailState state)
    {
        if (body == null) return;
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        bool embedded = IsEmbeddedState(state);
        if (capsule != null) capsule.isTrigger = embedded;
        if (resource != null && resource.IsSecuredForTransport)
        {
            SetPhysicsMode(true, false);
            return;
        }
        if (state == MountingNailState.Held || resource != null && resource.IsPickedUp)
        {
            if (IsNetworkSessionActive() && !IsServer) SetPhysicsMode(true, false);
            return;
        }
        bool shouldBeKinematic = state == MountingNailState.Prepared || embedded
            || IsNetworkSessionActive() && !IsServer;
        SetPhysicsMode(shouldBeKinematic, !shouldBeKinematic);
    }

    public void ApplySecuredTransportState(bool secured)
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (secured && State == MountingNailState.Prepared) SetState(MountingNailState.Loose);
        ApplyPhysicsForState(secured ? MountingNailState.Loose : State);
    }

    public bool TryApplyCarryabilityPhysicsState()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (resource == null) resource = GetComponent<BaseResourceNew>();
        if (body == null || resource == null || resource.IsPickedUp) return false;
        if (IsNetworkSessionActive() && !IsServer)
        {
            SetPhysicsMode(true, false);
            return true;
        }
        if (State == MountingNailState.Prepared || BlocksPickup)
        {
            ApplyPhysicsForState(State);
            return true;
        }
        return false;
    }

    private void SetPhysicsMode(bool kinematic, bool gravity)
    {
        if (body.isKinematic == kinematic && body.useGravity == gravity) return;
        if (kinematic && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.useGravity = gravity;
        body.isKinematic = kinematic;
    }

    private bool IsOwnCollider(Collider collider) => collider.transform == transform || collider.transform.IsChildOf(transform);

    private static Transform ResolveTargetRoot(Collider collider)
    {
        if (collider == null) return null;
        Rigidbody attachedBody = collider.attachedRigidbody;
        if (attachedBody != null) return attachedBody.transform;
        NetworkObject networkObject = collider.GetComponentInParent<NetworkObject>();
        if (networkObject != null) return networkObject.transform;
        MountingNailSceneTargetIdentity sceneIdentity = collider.GetComponentInParent<MountingNailSceneTargetIdentity>();
        if (sceneIdentity != null) return sceneIdentity.transform;
        BaseResourceNew targetResource = collider.GetComponentInParent<BaseResourceNew>();
        return targetResource != null ? targetResource.transform : collider.transform;
    }

    private static bool IsCurrentlyCarried(Collider collider)
    {
        MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>();
        foreach (MonoBehaviour behaviour in behaviours)
            if (behaviour is ICarriedObjectImpactTargetProvider provider && provider.IsActivelyCarried)
                return true;

        DownedPlayerCarryable carryable = collider.GetComponentInParent<DownedPlayerCarryable>();
        return carryable != null && carryable.IsCarried;
    }

    private bool TryFindSecondPairTarget(Transform first, Vector3 entryPoint, Vector3 worldAxis,
        out Transform second)
    {
        second = null;
        float range = Mathf.Max(0f, profile.PenetrationRangeMeters);
        if (range <= 0f) return false;
        const float backOffset = 0.001f;
        Vector3 axis = worldAxis.normalized;
        RaycastHit[] hits = Physics.RaycastAll(entryPoint - axis * backOffset, axis,
            range + backOffset, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        bool originalFirstContactSeen = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger || IsOwnCollider(hit.collider)) continue;
            float fromEntry = Vector3.Dot(hit.point - entryPoint, axis);
            if (fromEntry < -backOffset || fromEntry > range) continue;
            Transform root = ResolveTargetRoot(hit.collider);
            if (root == null) return false;
            if (root == first)
            {
                originalFirstContactSeen = true;
                continue;
            }
            if (!originalFirstContactSeen) return false;
            if (IsActorRoot(root) || !IsPairEligible(root) || IsCurrentlyCarried(hit.collider)) return false;
            second = root;
            return true;
        }
        return false;
    }

    private static bool IsActorRoot(Transform root)
    {
        if (root == null) return true;
        foreach (MonoBehaviour behaviour in root.GetComponentsInParent<MonoBehaviour>())
        {
            if (behaviour == null) continue;
            if (behaviour is INPCTarget) return true;
            if (behaviour is PlayerInteractionNew || behaviour is PlayerHealth || behaviour is NPCFactionMember
                || behaviour is NPCHealth || behaviour is NPCCarrier || behaviour is DownedPlayerCarryable) return true;
        }
        NetworkObject networkObject = root.GetComponentInParent<NetworkObject>();
        return networkObject != null && networkObject.IsPlayerObject;
    }

    private bool IsPairEligible(Transform root)
    {
        return root != null && (root.GetComponent<Rigidbody>() != null
            || root.GetComponentInParent<NetworkObject>() != null
            || root.GetComponent<MountingNailSceneTargetIdentity>() != null
            || root.GetComponent<BaseResourceNew>() != null
            || !IsNetworkSessionActive() && root.GetComponent<Collider>() != null);
    }

    private static bool IsTargetCarried(Transform root)
    {
        if (root == null) return false;
        foreach (MonoBehaviour behaviour in root.GetComponentsInParent<MonoBehaviour>())
            if (behaviour is ICarriedObjectImpactTargetProvider provider && provider.IsActivelyCarried) return true;
        DownedPlayerCarryable carryable = root.GetComponentInParent<DownedPlayerCarryable>();
        return carryable != null && carryable.IsCarried;
    }

    private static bool IsEmbeddedState(MountingNailState state) => state == MountingNailState.Partial
        || state == MountingNailState.EmbeddedSingle || state == MountingNailState.EmbeddedPair;

    private bool IsNetworkSessionActive() => IsSpawned && NetworkManager != null && NetworkManager.IsListening;
    private void ReplicatedStateChanged(MountingNailState previous, MountingNailState current)
    {
        ApplyPhysicsForState(current);
        if (current != MountingNailState.Partial && current != MountingNailState.EmbeddedSingle
            && current != MountingNailState.EmbeddedPair)
            embeddedTarget = null;
        else if (!IsServer) ResolveReplicatedAttachment();
        StateOrAttachmentChanged?.Invoke(this);
    }
    private void ReplicatedAttachmentChanged(MountingNailReplicatedAttachment previous,
        MountingNailReplicatedAttachment current)
    {
        if (!IsServer && IsEmbeddedState(State)) ResolveReplicatedAttachment();
    }
    private void RemainingChanged(float previous, float current) { }

    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(Quaternion value) => IsFinite(value.x) && IsFinite(value.y)
        && IsFinite(value.z) && IsFinite(value.w)
        && value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > 0.000001f;
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
