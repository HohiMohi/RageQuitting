using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NPCAttackController))]
[RequireComponent(typeof(NPCAnimationController))]
[RequireComponent(typeof(NPCBrain))]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CapsuleCollider))]
public sealed class BeaverAttackSequenceController : NetworkBehaviour
{
    private enum SequencePhase
    {
        None,
        ChasingLunge,
        Telegraph,
        WaitingForImpact,
        StrikeGap,
        Recovery
    }

    private NPCAttackController attackController;
    private NPCAnimationController animationController;
    private NPCBrain brain;
    private NavMeshAgent agent;
    private CapsuleCollider bodyCollider;
    private NetworkObject primaryTarget;
    private BeaverAttackPatternConfig activePattern;
    private BeaverLungeConfig lungeConfig;
    private Action<bool> completion;
    private SequencePhase phase;
    private float phaseEndTime;
    private float nextChaseRefreshTime;
    private float chaseStartedAt;
    private int strikeIndex;
    private int lastPatternIndex = -1;
    private bool active;
    private bool isLunge;
    private bool lungeMovementActive;
    private float lungeDistanceTravelled;
    private Vector3 lockedForward;
    private bool agentStateCaptured;
    private bool capturedIsStopped;
    private float capturedStoppingDistance;

    public bool IsRunning => active;
    public int LastPatternIndex => lastPatternIndex;

    private void Awake()
    {
        CacheReferences();
    }

    public bool StartSequence(
        NetworkObject target,
        BeaverAttackPatternConfig pattern,
        BeaverLungeConfig lunge,
        Action<bool> completed)
    {
        CacheReferences();
        if (!CanRunAuthoritatively()
            || agent == null
            || !agent.enabled
            || !agent.isOnNavMesh
            || !IsTargetAvailable(target)
            || pattern.StrikeCount == 0)
        {
            return false;
        }

        CancelSequence(false);
        primaryTarget = target;
        activePattern = pattern;
        lungeConfig = lunge;
        completion = completed;
        strikeIndex = 0;
        active = true;
        CaptureAgentState();

        isLunge = GetTargetDistance() > attackController.AttackRange;
        if (isLunge)
        {
            BeginLungeChase();
        }
        else
        {
            BeginTelegraph(activePattern.PrepareDuration);
        }

        return true;
    }

    public bool StartWeightedSequence(
        NetworkObject target,
        BeaverAttackPatternConfig[] patterns,
        BeaverLungeConfig lunge,
        Action<bool> completed)
    {
        int selectedIndex = SelectPatternIndex(patterns, lastPatternIndex, UnityEngine.Random.value);
        if (selectedIndex < 0)
        {
            return false;
        }

        bool started = StartSequence(target, patterns[selectedIndex], lunge, completed);
        lastPatternIndex = ResolveRecordedPatternIndex(lastPatternIndex, selectedIndex, started, isLunge);
        return started;
    }

    public static int ResolveRecordedPatternIndex(
        int previousIndex,
        int selectedIndex,
        bool sequenceStarted,
        bool startedAsLunge)
    {
        return sequenceStarted && !startedAsLunge ? selectedIndex : previousIndex;
    }

    public static int SelectPatternIndex(BeaverAttackPatternConfig[] patterns, int previousIndex, float roll01)
    {
        if (patterns == null || patterns.Length == 0)
        {
            return -1;
        }

        bool hasAlternative = false;
        for (int i = 0; i < patterns.Length; i++)
        {
            if (i != previousIndex && patterns[i].Weight > 0f)
            {
                hasAlternative = true;
                break;
            }
        }

        float totalWeight = 0f;
        for (int i = 0; i < patterns.Length; i++)
        {
            if (hasAlternative && i == previousIndex)
            {
                continue;
            }

            totalWeight += patterns[i].Weight;
        }

        if (totalWeight <= 0f)
        {
            return 0;
        }

        float selection = Mathf.Clamp(roll01, 0f, 0.999999f) * totalWeight;
        for (int i = 0; i < patterns.Length; i++)
        {
            if (hasAlternative && i == previousIndex)
            {
                continue;
            }

            selection -= patterns[i].Weight;
            if (selection < 0f)
            {
                return i;
            }
        }

        return 0;
    }

    public void CancelSequence()
    {
        CancelSequence(true);
    }

    private void Update()
    {
        if (!active || !CanRunAuthoritatively())
        {
            return;
        }

        if (!IsTargetAvailable(primaryTarget))
        {
            Complete(false);
            return;
        }

        if (agentStateCaptured && (agent == null || !agent.enabled || !agent.isOnNavMesh))
        {
            Complete(false);
            return;
        }

        switch (phase)
        {
            case SequencePhase.ChasingLunge:
                TickLungeChase();
                break;
            case SequencePhase.Telegraph:
                FaceTarget();
                if (Time.time >= phaseEndTime)
                {
                    if (isLunge)
                    {
                        float launchRange = attackController.AttackRange + lungeConfig.LaunchExtraRange;
                        if (GetTargetDistance() > launchRange)
                        {
                            BeginLungeChase();
                        }
                        else
                        {
                            BeginLungeStrike();
                        }
                    }
                    else
                    {
                        BeginPatternStrike();
                    }
                }
                break;
            case SequencePhase.WaitingForImpact:
                if (lungeMovementActive)
                {
                    TickLungeMovement();
                }
                break;
            case SequencePhase.StrikeGap:
                FaceTarget();
                if (Time.time >= phaseEndTime)
                {
                    BeginPatternStrike();
                }
                break;
            case SequencePhase.Recovery:
                if (Time.time >= phaseEndTime)
                {
                    Complete(true);
                }
                break;
        }
    }

    private void BeginLungeChase()
    {
        phase = SequencePhase.ChasingLunge;
        nextChaseRefreshTime = 0f;
        chaseStartedAt = Time.time;
        RestoreAgentForChase();
    }

    private void TickLungeChase()
    {
        float launchRange = attackController.AttackRange + lungeConfig.LaunchExtraRange;
        if (GetTargetDistance() <= launchRange)
        {
            StopAgent();
            BeginTelegraph(lungeConfig.TelegraphDuration);
            return;
        }

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            Complete(false);
            return;
        }

        if (Time.time - chaseStartedAt >= lungeConfig.UnreachableTargetTimeout)
        {
            Complete(false);
            return;
        }

        if (Time.time < nextChaseRefreshTime)
        {
            return;
        }

        nextChaseRefreshTime = Time.time + lungeConfig.ChaseRefreshInterval;
        agent.stoppingDistance = Mathf.Max(0f, launchRange * 0.9f);
        agent.isStopped = false;
        if (!agent.SetDestination(primaryTarget.transform.position))
        {
            Complete(false);
        }
    }

    private void BeginTelegraph(float duration)
    {
        phase = SequencePhase.Telegraph;
        phaseEndTime = Time.time + Mathf.Max(0f, duration);
        StopAgent();
        FaceTarget();
    }

    private void BeginPatternStrike()
    {
        if (strikeIndex > 0 && GetTargetDistance() > attackController.AttackRange)
        {
            isLunge = true;
            strikeIndex = activePattern.StrikeCount;
            BeginLungeChase();
            return;
        }

        BeaverStrikeConfig strike = activePattern.GetStrike(strikeIndex);
        lockedForward = GetFlatDirectionToTarget();
        if (animationController != null)
        {
            animationController.PlayAction(strike.AnimationSpeed);
        }
        phase = SequencePhase.WaitingForImpact;
        NPCAttackController.AttackExecutionOptions options = new NPCAttackController.AttackExecutionOptions(
            strike.AnimationSpeed,
            strike.DamageMultiplier,
            lockedForward,
            primaryTarget);
        if (!attackController.StartAttack(options, result => HandlePatternImpact(result, strike)))
        {
            Complete(false);
        }
    }

    private void HandlePatternImpact(
        NPCAttackController.AttackExecutionResult result,
        BeaverStrikeConfig strike)
    {
        if (!active || result.Cancelled)
        {
            return;
        }

        strikeIndex++;
        if (strikeIndex < activePattern.StrikeCount)
        {
            if (!IsTargetAvailable(primaryTarget))
            {
                Complete(false);
                return;
            }

            if (GetTargetDistance() > attackController.AttackRange)
            {
                isLunge = true;
                strikeIndex = activePattern.StrikeCount;
                BeginLungeChase();
                return;
            }

            phase = SequencePhase.StrikeGap;
            phaseEndTime = Time.time + strike.GapAfterImpact;
            return;
        }

        BeginRecovery(activePattern.RecoveryDuration);
    }

    private void BeginLungeStrike()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            Complete(false);
            return;
        }

        agent.ResetPath();
        agent.isStopped = false;
        lockedForward = GetFlatDirectionToTarget();
        if (lockedForward.sqrMagnitude <= 0.0001f)
        {
            lockedForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        }

        transform.rotation = Quaternion.LookRotation(lockedForward, Vector3.up);
        if (animationController != null)
        {
            animationController.PlayAction(lungeConfig.AnimationSpeed);
        }
        lungeDistanceTravelled = 0f;
        lungeMovementActive = true;
        phase = SequencePhase.WaitingForImpact;
        NPCAttackController.AttackExecutionOptions options = new NPCAttackController.AttackExecutionOptions(
            lungeConfig.AnimationSpeed,
            lungeConfig.DamageMultiplier,
            lockedForward,
            primaryTarget,
            lungeConfig.ImpulseProfile);
        if (!attackController.StartAttack(options, HandleLungeImpact))
        {
            Complete(false);
        }
    }

    private void HandleLungeImpact(NPCAttackController.AttackExecutionResult result)
    {
        if (!active || result.Cancelled)
        {
            return;
        }

        lungeMovementActive = false;
        BeginRecovery(lungeConfig.RecoveryDuration);
    }

    private void TickLungeMovement()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            Complete(false);
            return;
        }

        float remaining = lungeConfig.MaxTravelDistance - lungeDistanceTravelled;
        float distance = Mathf.Min(remaining, lungeConfig.Speed * Time.deltaTime);
        if (distance <= 0f)
        {
            lungeMovementActive = false;
            return;
        }

        Vector3 start = transform.position;
        Vector3 desired = start + lockedForward * distance;
        if (NavMesh.Raycast(start, desired, out NavMeshHit navHit, agent.areaMask))
        {
            distance = Mathf.Max(0f, Vector3.Distance(start, navHit.position) - lungeConfig.CollisionSkin);
            lungeMovementActive = false;
        }

        float collisionDistance = GetCollisionLimitedDistance(lockedForward, distance);
        if (collisionDistance < distance)
        {
            distance = collisionDistance;
            lungeMovementActive = false;
        }

        if (distance > 0f)
        {
            agent.Move(lockedForward * distance);
            lungeDistanceTravelled += distance;
        }
    }

    private float GetCollisionLimitedDistance(Vector3 direction, float distance)
    {
        if (distance <= 0f)
        {
            return 0f;
        }

        GetCapsule(out Vector3 point1, out Vector3 point2, out float radius);
        RaycastHit[] hits = Physics.CapsuleCastAll(
            point1,
            point2,
            radius,
            direction,
            distance + lungeConfig.CollisionSkin,
            lungeConfig.ObstacleLayers,
            QueryTriggerInteraction.Ignore);
        float allowed = distance;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null
                || hit.collider.transform.root == transform.root
                || hit.normal.y > 0.55f)
            {
                continue;
            }

            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - lungeConfig.CollisionSkin));
        }

        return allowed;
    }

    private void GetCapsule(out Vector3 point1, out Vector3 point2, out float radius)
    {
        if (bodyCollider != null)
        {
            Vector3 scale = bodyCollider.transform.lossyScale;
            radius = bodyCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(bodyCollider.height * Mathf.Abs(scale.y), radius * 2f);
            Vector3 center = bodyCollider.transform.TransformPoint(bodyCollider.center);
            float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
            point1 = center + Vector3.up * halfSegment;
            point2 = center - Vector3.up * halfSegment;
            return;
        }

        radius = agent != null ? Mathf.Max(0.05f, agent.radius) : 0.35f;
        float agentHeight = agent != null ? Mathf.Max(radius * 2f, agent.height) : 1.4f;
        Vector3 agentCenter = transform.position + Vector3.up * agentHeight * 0.5f;
        float agentHalfSegment = Mathf.Max(0f, agentHeight * 0.5f - radius);
        point1 = agentCenter + Vector3.up * agentHalfSegment;
        point2 = agentCenter - Vector3.up * agentHalfSegment;
    }

    private void BeginRecovery(float duration)
    {
        lungeMovementActive = false;
        StopAgent();
        phase = SequencePhase.Recovery;
        phaseEndTime = Time.time + Mathf.Max(0f, duration);
    }

    private void Complete(bool succeeded)
    {
        if (!active)
        {
            return;
        }

        active = false;
        phase = SequencePhase.None;
        lungeMovementActive = false;
        Action<bool> completed = completion;
        completion = null;
        primaryTarget = null;
        if (!succeeded)
        {
            if (attackController != null)
            {
                attackController.CancelPendingAttacks();
            }
        }
        RestoreAgentState();
        completed?.Invoke(succeeded);
    }

    private void CancelSequence(bool invokeCompletion)
    {
        if (!active)
        {
            return;
        }

        active = false;
        phase = SequencePhase.None;
        lungeMovementActive = false;
        Action<bool> completed = completion;
        completion = null;
        primaryTarget = null;
        if (attackController != null)
        {
            attackController.CancelPendingAttacks();
        }
        RestoreAgentState();
        if (invokeCompletion)
        {
            completed?.Invoke(false);
        }
    }

    private void CacheReferences()
    {
        if (attackController == null)
        {
            attackController = GetComponent<NPCAttackController>();
        }
        if (animationController == null)
        {
            animationController = GetComponent<NPCAnimationController>();
        }
        if (brain == null)
        {
            brain = GetComponent<NPCBrain>();
        }
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }
        if (bodyCollider == null)
        {
            bodyCollider = GetComponent<CapsuleCollider>();
        }
        if (bodyCollider == null)
        {
            bodyCollider = GetComponentInChildren<CapsuleCollider>();
        }
    }

    private bool CanRunAuthoritatively()
    {
        return attackController != null
            && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !IsSpawned || IsServer)
            && (brain == null || brain.Health == null || !brain.Health.IsDead);
    }

    private static bool IsTargetAvailable(NetworkObject target)
    {
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (target.TryGetComponent(out PlayerHealth playerHealth))
        {
            return !playerHealth.IsDowned;
        }

        if (target.TryGetComponent(out NPCHealth npcHealth))
        {
            return !npcHealth.IsDead;
        }

        return false;
    }

    private float GetTargetDistance()
    {
        return primaryTarget != null
            ? Vector3.Distance(transform.position, primaryTarget.transform.position)
            : float.PositiveInfinity;
    }

    private Vector3 GetFlatDirectionToTarget()
    {
        if (primaryTarget == null)
        {
            return Vector3.zero;
        }

        return Vector3.ProjectOnPlane(primaryTarget.transform.position - transform.position, Vector3.up).normalized;
    }

    private void FaceTarget()
    {
        Vector3 direction = GetFlatDirectionToTarget();
        if (direction.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }

    private void CaptureAgentState()
    {
        if (agentStateCaptured || agent == null || !agent.enabled)
        {
            return;
        }

        agentStateCaptured = true;
        capturedIsStopped = agent.isStopped;
        capturedStoppingDistance = agent.stoppingDistance;
    }

    private void RestoreAgentForChase()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
    }

    private void StopAgent()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private void RestoreAgentState()
    {
        if (!agentStateCaptured)
        {
            return;
        }

        agentStateCaptured = false;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.stoppingDistance = capturedStoppingDistance;
            agent.isStopped = capturedIsStopped;
        }
    }

    public override void OnNetworkDespawn()
    {
        CancelSequence();
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        CancelSequence();
    }
}
