using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public enum GoatChargePhase
{
    None,
    Telegraph,
    Accelerating,
    Committed,
    Braking
}

public enum GoatChargeEndReason
{
    Hit,
    Obstacle,
    Timeout,
    Cancelled
}

public class GoatChargeController : NetworkBehaviour
{
    private const float GroundNormalThreshold = 0.7f;

    [SerializeField] private Transform visualRoot;

    private readonly List<GameObject> carriedImpactTargets = new List<GameObject>();
    private readonly NetworkVariable<GoatChargePhase> phaseNetwork = new NetworkVariable<GoatChargePhase>();
    private readonly NetworkVariable<double> phaseStartedAtNetwork = new NetworkVariable<double>();
    private readonly NetworkVariable<float> phaseDurationNetwork = new NetworkVariable<float>();

    private NPCBrain brain;
    private NPCHealth health;
    private NavMeshAgent agent;
    private CapsuleCollider bodyCollider;
    private NPCAnimationController animationController;
    private PlayerHealth currentTarget;
    private GoatBehaviorSO config;
    private GoatChargePhase localPhase;
    private double localPhaseStartedAt;
    private float localPhaseDuration;
    private float currentSpeed;
    private Vector3 committedDirection;
    private bool brakingBlocked;
    private float blockedRecoveryEndsAt;
    private Vector3 visualBaseLocalPosition;
    private Quaternion visualBaseLocalRotation;
    private bool visualBaseCached;

    public event Action<GoatChargeEndReason> ChargeFinished;

    public GoatChargePhase CurrentPhase => IsNetworkStateActive ? phaseNetwork.Value : localPhase;
    public float CurrentSpeed => currentSpeed;
    public PlayerHealth CurrentTarget => currentTarget;
    public bool IsCharging => CurrentPhase != GoatChargePhase.None;

    private bool IsNetworkStateActive =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    private bool CanDriveCharge => !IsNetworkStateActive || IsServer;

    private void Awake()
    {
        CacheReferences();
        CacheVisualBasePose();
    }

    public override void OnNetworkSpawn()
    {
        CacheReferences();
        CacheVisualBasePose();
        ApplyVisualFeedback(true);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            CancelCharge(false);
        }

        RestoreVisualImmediately();
    }

    private void Update()
    {
        ApplyVisualFeedback(false);
        if (!CanDriveCharge || CurrentPhase == GoatChargePhase.None)
        {
            return;
        }

        if (health != null && health.IsDead)
        {
            CancelCharge(false);
            return;
        }

        switch (CurrentPhase)
        {
            case GoatChargePhase.Telegraph:
                UpdateTelegraph();
                break;
            case GoatChargePhase.Accelerating:
                UpdateAccelerating();
                break;
            case GoatChargePhase.Committed:
                UpdateCommitted();
                break;
            case GoatChargePhase.Braking:
                UpdateBraking();
                break;
        }
    }

    public bool BeginCharge(PlayerHealth target, GoatBehaviorSO behaviorConfig)
    {
        if (!CanDriveCharge
            || IsCharging
            || target == null
            || target.IsDowned
            || behaviorConfig == null)
        {
            return false;
        }

        CacheReferences();
        currentTarget = target;
        config = behaviorConfig;
        currentSpeed = 0f;
        committedDirection = transform.forward;
        brakingBlocked = false;
        StopAgent();
        animationController?.SetExternalMovementSpeedNormalized(0f);
        SetPhase(GoatChargePhase.Telegraph, config.ChargeTelegraphDuration);
        return true;
    }

    public void CancelCharge()
    {
        CancelCharge(true);
    }

    private void CancelCharge(bool notify)
    {
        if (!CanDriveCharge)
        {
            return;
        }

        bool wasCharging = IsCharging;
        currentSpeed = 0f;
        currentTarget = null;
        config = null;
        animationController?.ClearExternalMovementSpeedOverride();
        RestoreAgent();
        SetPhase(GoatChargePhase.None, 0f);

        if (notify && wasCharging)
        {
            ChargeFinished?.Invoke(GoatChargeEndReason.Cancelled);
        }
    }

    private void UpdateTelegraph()
    {
        if (currentTarget == null
            || currentTarget.IsDowned
            || Vector3.Distance(transform.position, currentTarget.transform.position) > config.ProximityThreatRange)
        {
            FinishCharge(GoatChargeEndReason.Cancelled);
            return;
        }

        FacePosition(currentTarget.transform.position, config.ChargeSteeringDegreesPerSecond);
        if (GetPhaseElapsed() < config.ChargeTelegraphDuration)
        {
            return;
        }

        DisableAgent();
        committedDirection = FlattenDirection(transform.forward);
        SetPhase(GoatChargePhase.Accelerating, 0f);
    }

    private void UpdateAccelerating()
    {
        if (currentTarget != null)
        {
            FacePosition(currentTarget.transform.position, config.ChargeSteeringDegreesPerSecond);
        }

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            config.ChargeMaxSpeed,
            config.ChargeAcceleration * Time.deltaTime);
        UpdateAnimationSpeed();

        if (!TryMoveForward(Time.deltaTime))
        {
            return;
        }

        if (currentSpeed < config.ChargeMaxSpeed - 0.01f)
        {
            return;
        }

        currentSpeed = config.ChargeMaxSpeed;
        committedDirection = FlattenDirection(transform.forward);
        SetPhase(GoatChargePhase.Committed, config.ChargeCommittedDuration);
    }

    private void UpdateCommitted()
    {
        if (committedDirection.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(committedDirection, Vector3.up);
        }

        currentSpeed = config.ChargeMaxSpeed;
        UpdateAnimationSpeed();
        if (!TryMoveForward(Time.deltaTime))
        {
            return;
        }

        if (GetPhaseElapsed() >= config.ChargeCommittedDuration)
        {
            BeginBraking(false);
        }
    }

    private void UpdateBraking()
    {
        if (brakingBlocked)
        {
            currentSpeed = 0f;
            UpdateAnimationSpeed();
            if (Time.time >= blockedRecoveryEndsAt)
            {
                FinishCharge(GoatChargeEndReason.Obstacle);
            }
            return;
        }

        currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, config.ChargeDeceleration * Time.deltaTime);
        UpdateAnimationSpeed();
        if (currentSpeed <= 0.01f)
        {
            FinishCharge(GoatChargeEndReason.Timeout);
            return;
        }

        TryMoveForward(Time.deltaTime);
    }

    private bool TryMoveForward(float deltaTime)
    {
        Vector3 direction = FlattenDirection(transform.forward);
        float requestedDistance = currentSpeed * Mathf.Max(0f, deltaTime);
        if (requestedDistance <= 0.0001f)
        {
            return true;
        }

        float allowedDistance = requestedDistance;
        bool navMeshBlocked = TryGetNavMeshBlockDistance(direction, requestedDistance, out float navMeshDistance);
        if (navMeshBlocked)
        {
            allowedDistance = Mathf.Min(allowedDistance, navMeshDistance);
        }

        bool physicsBlocked = TryGetPhysicsBlock(
            direction,
            requestedDistance,
            out float physicsDistance,
            out Collider blockingCollider);
        if (physicsBlocked)
        {
            allowedDistance = Mathf.Min(allowedDistance, physicsDistance);
        }

        float safeDistance = Mathf.Max(0f, allowedDistance - config.ChargeCollisionSkin);
        if (safeDistance > 0f)
        {
            transform.position += direction * safeDistance;
        }

        if (physicsBlocked && physicsDistance <= navMeshDistance)
        {
            if (TryDamageChargeTarget(blockingCollider))
            {
                FinishCharge(GoatChargeEndReason.Hit);
            }
            else
            {
                BeginBraking(true);
            }
            return false;
        }

        if (navMeshBlocked)
        {
            BeginBraking(true);
            return false;
        }

        return true;
    }

    private bool TryGetNavMeshBlockDistance(Vector3 direction, float distance, out float blockDistance)
    {
        blockDistance = float.PositiveInfinity;
        int areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas;
        Vector3 destination = transform.position + direction * distance;
        if (!NavMesh.Raycast(transform.position, destination, out NavMeshHit hit, areaMask))
        {
            return false;
        }

        blockDistance = Vector3.Distance(transform.position, hit.position);
        return true;
    }

    private bool TryGetPhysicsBlock(
        Vector3 direction,
        float distance,
        out float blockDistance,
        out Collider blockingCollider)
    {
        blockDistance = float.PositiveInfinity;
        blockingCollider = null;
        GetWorldCapsule(out Vector3 point1, out Vector3 point2, out float radius);
        RaycastHit[] hits = Physics.CapsuleCastAll(
            point1,
            point2,
            radius,
            direction,
            distance + config.ChargeCollisionSkin,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null
                || hit.collider.transform.root == transform.root
                || hit.normal.y >= GroundNormalThreshold)
            {
                continue;
            }

            blockDistance = Mathf.Max(0f, hit.distance);
            blockingCollider = hit.collider;
            return true;
        }

        return false;
    }

    private void GetWorldCapsule(out Vector3 point1, out Vector3 point2, out float radius)
    {
        if (bodyCollider == null)
        {
            radius = agent != null ? agent.radius * 0.9f : 0.35f;
            float fallbackHeight = agent != null ? agent.height : 1.2f;
            Vector3 fallbackCenter = transform.position + Vector3.up * (fallbackHeight * 0.5f);
            float segment = Mathf.Max(0f, fallbackHeight * 0.5f - radius);
            point1 = fallbackCenter + Vector3.up * segment;
            point2 = fallbackCenter - Vector3.up * segment;
            return;
        }

        Vector3 scale = bodyCollider.transform.lossyScale;
        radius = bodyCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) * 0.9f;
        float height = Mathf.Max(bodyCollider.height * Mathf.Abs(scale.y), radius * 2f);
        Vector3 center = bodyCollider.transform.TransformPoint(bodyCollider.center);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        point1 = center + Vector3.up * halfSegment;
        point2 = center - Vector3.up * halfSegment;
    }

    private bool TryDamageChargeTarget(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        PlayerHealth player = collider.GetComponentInParent<PlayerHealth>();
        if (player != null && TryDamageChargeTarget(player.gameObject))
        {
            return true;
        }

        NPCHealth npc = collider.GetComponentInParent<NPCHealth>();
        if (npc != null && TryDamageChargeTarget(npc.gameObject))
        {
            return true;
        }

        ICarriedObjectImpactTargetProvider carriedObject =
            FindCarriedObjectImpactTargetProvider(collider);
        if (carriedObject == null || !carriedObject.IsActivelyCarried)
        {
            return false;
        }

        carriedImpactTargets.Clear();
        carriedObject.CollectActiveCarrierRoots(carriedImpactTargets);

        bool damagedAnyCarrier = false;
        foreach (GameObject carrierRoot in carriedImpactTargets)
        {
            damagedAnyCarrier |= TryDamageChargeTarget(carrierRoot);
        }

        carriedImpactTargets.Clear();
        return damagedAnyCarrier;
    }

    private bool TryDamageChargeTarget(GameObject targetRoot)
    {
        if (targetRoot == null)
        {
            return false;
        }

        PlayerHealth player = targetRoot.GetComponent<PlayerHealth>();
        if (player != null)
        {
            if (player.IsDowned)
            {
                return false;
            }

            player.DamageReceived(config.ChargeDamage, NetworkObject);
            ApplyChargeImpulse(player.gameObject);
            return true;
        }

        NPCHealth npc = targetRoot.GetComponent<NPCHealth>();
        if (npc == null || npc.IsDead || npc.transform.root == transform.root)
        {
            return false;
        }

        NPCFactionMember targetFaction = npc.GetComponent<NPCFactionMember>();
        if (brain != null
            && targetFaction != null
            && brain.GetRelationTo(targetFaction.Faction) == NPCFactionRelation.Ally)
        {
            return false;
        }

        npc.DamageReceived(config.ChargeDamage, NetworkObject);
        ApplyChargeImpulse(npc.gameObject);
        return true;
    }

    private static ICarriedObjectImpactTargetProvider FindCarriedObjectImpactTargetProvider(Collider collider)
    {
        MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is ICarriedObjectImpactTargetProvider provider)
            {
                return provider;
            }
        }

        return null;
    }

    private void ApplyChargeImpulse(GameObject target)
    {
        if (target == null || config == null || config.ChargeImpulseProfile == null)
        {
            return;
        }

        MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is IExternalImpulseReceiver receiver)
            {
                receiver.TryApplyExternalImpulse(
                    config.ChargeImpulseProfile.CreateImpulse(transform.forward),
                    NetworkObject);
                return;
            }
        }
    }

    private void BeginBraking(bool blocked)
    {
        if (CurrentPhase == GoatChargePhase.Braking && brakingBlocked)
        {
            return;
        }

        brakingBlocked = blocked;
        if (blocked)
        {
            currentSpeed = 0f;
            blockedRecoveryEndsAt = Time.time + config.ChargeBlockedRecoveryDuration;
        }

        SetPhase(GoatChargePhase.Braking, blocked ? config.ChargeBlockedRecoveryDuration : 0f);
    }

    private void FinishCharge(GoatChargeEndReason reason)
    {
        if (!CanDriveCharge)
        {
            return;
        }

        currentSpeed = 0f;
        currentTarget = null;
        config = null;
        animationController?.ClearExternalMovementSpeedOverride();
        RestoreAgent();
        SetPhase(GoatChargePhase.None, 0f);
        ChargeFinished?.Invoke(reason);
    }

    private void UpdateAnimationSpeed()
    {
        float maximumSpeed = config != null ? config.ChargeMaxSpeed : 1f;
        animationController?.SetExternalMovementSpeedNormalized(
            maximumSpeed > 0f ? currentSpeed / maximumSpeed : 0f);
    }

    private void StopAgent()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = true;
        agent.ResetPath();
    }

    private void DisableAgent()
    {
        StopAgent();
        if (agent != null && agent.enabled)
        {
            agent.enabled = false;
        }
    }

    private void RestoreAgent()
    {
        if (agent == null)
        {
            return;
        }

        int areaMask = agent.areaMask;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, areaMask))
        {
            transform.position = hit.position;
        }

        if (!agent.enabled)
        {
            agent.enabled = true;
        }

        if (agent.isOnNavMesh)
        {
            agent.Warp(transform.position);
            agent.isStopped = false;
            agent.ResetPath();
        }
    }

    private void FacePosition(Vector3 targetPosition, float degreesPerSecond)
    {
        Vector3 direction = FlattenDirection(targetPosition - transform.position);
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(direction, Vector3.up),
            Mathf.Max(0f, degreesPerSecond) * Time.deltaTime);
    }

    private static Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    private void SetPhase(GoatChargePhase phase, float duration)
    {
        double startedAt = GetServerTime();
        if (IsNetworkStateActive)
        {
            if (!IsServer)
            {
                return;
            }

            phaseNetwork.Value = phase;
            phaseStartedAtNetwork.Value = startedAt;
            phaseDurationNetwork.Value = Mathf.Max(0f, duration);
        }
        else
        {
            localPhase = phase;
            localPhaseStartedAt = startedAt;
            localPhaseDuration = Mathf.Max(0f, duration);
        }
    }

    private float GetPhaseElapsed()
    {
        double startedAt = IsNetworkStateActive ? phaseStartedAtNetwork.Value : localPhaseStartedAt;
        return Mathf.Max(0f, (float)(GetServerTime() - startedAt));
    }

    private double GetServerTime()
    {
        return IsNetworkStateActive ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
    }

    private void ApplyVisualFeedback(bool immediate)
    {
        if (visualRoot == null)
        {
            return;
        }

        CacheVisualBasePose();
        Vector3 targetPosition = visualBaseLocalPosition;
        Quaternion targetRotation = visualBaseLocalRotation;
        if (CurrentPhase == GoatChargePhase.Telegraph)
        {
            float duration = IsNetworkStateActive ? phaseDurationNetwork.Value : localPhaseDuration;
            float normalized = duration > 0f ? Mathf.Clamp01(GetPhaseElapsed() / duration) : 1f;
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            float pulse = Mathf.Sin(normalized * Mathf.PI * 6f) * 0.015f * eased;
            targetPosition += new Vector3(0f, -0.08f + pulse, -0.05f * eased);
            targetRotation *= Quaternion.Euler(10f * eased, 0f, 0f);
        }

        if (immediate)
        {
            visualRoot.localPosition = targetPosition;
            visualRoot.localRotation = targetRotation;
            return;
        }

        float blend = 1f - Mathf.Exp(-14f * Time.deltaTime);
        visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, targetPosition, blend);
        visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, targetRotation, blend);
    }

    private void CacheReferences()
    {
        brain ??= GetComponent<NPCBrain>();
        health ??= GetComponent<NPCHealth>();
        agent ??= GetComponent<NavMeshAgent>();
        bodyCollider ??= GetComponent<CapsuleCollider>();
        animationController ??= GetComponent<NPCAnimationController>();
    }

    private void CacheVisualBasePose()
    {
        if (visualBaseCached || visualRoot == null)
        {
            return;
        }

        visualBaseLocalPosition = visualRoot.localPosition;
        visualBaseLocalRotation = visualRoot.localRotation;
        visualBaseCached = true;
    }

    private void RestoreVisualImmediately()
    {
        if (!visualBaseCached || visualRoot == null)
        {
            return;
        }

        visualRoot.localPosition = visualBaseLocalPosition;
        visualRoot.localRotation = visualBaseLocalRotation;
    }
}
