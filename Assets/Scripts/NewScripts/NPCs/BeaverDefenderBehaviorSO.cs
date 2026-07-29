using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public enum BeaverDefenderState
{
    Idle,
    FollowingScout,
    AttackMode
}

[CreateAssetMenu(fileName = "BeaverDefenderBehavior", menuName = "Scriptable Objects/NPC/Behaviors/Beaver Defender")]
public class BeaverDefenderBehaviorSO : NPCBehaviorSO
{
    [Header("Idle")]
    [SerializeField, Min(0.05f)] private float idleDecisionDelay = 0.75f;

    [Header("Follow")]
    [SerializeField] private NPCDefinitionSO scoutDefinition;
    [SerializeField, Min(0.1f)] private float followSearchRadius = 30f;
    [SerializeField, Min(0.1f)] private float followStoppingDistance = 2.25f;
    [SerializeField, Min(0.05f)] private float followDestinationRefreshInterval = 0.25f;
    [SerializeField, Min(1)] private int maxDefendersPerScout = 1;

    [Header("Family Alert")]
    [SerializeField, Min(0.1f)] private float familyAlertRadius = 20f;

    [Header("Combat")]
    [SerializeField, Min(0f)] private float attackPrepareDuration = 0.35f;
    [SerializeField, Min(0f)] private float attackRecoveryDuration = 0.8f;
    [SerializeField, Min(0.05f)] private float attackApproachRefreshInterval = 0.2f;
    [SerializeField, Min(0.1f)] private float unreachableTargetTimeout = 5f;

    public NPCDefinitionSO ScoutDefinition => scoutDefinition;
    public float IdleDecisionDelay => Mathf.Max(0.05f, idleDecisionDelay);
    public float FollowSearchRadius => Mathf.Max(0.1f, followSearchRadius);
    public float FollowStoppingDistance => Mathf.Max(0.1f, followStoppingDistance);
    public float FollowDestinationRefreshInterval => Mathf.Max(0.05f, followDestinationRefreshInterval);
    public int MaxDefendersPerScout => Mathf.Max(1, maxDefendersPerScout);
    public float FamilyAlertRadius => Mathf.Max(0.1f, familyAlertRadius);
    public float AttackPrepareDuration => Mathf.Max(0f, attackPrepareDuration);
    public float AttackRecoveryDuration => Mathf.Max(0f, attackRecoveryDuration);
    public float AttackApproachRefreshInterval => Mathf.Max(0.05f, attackApproachRefreshInterval);
    public float UnreachableTargetTimeout => Mathf.Max(0.1f, unreachableTargetTimeout);

    public override NPCBehaviorController CreateController(NPCBrain brain)
    {
        return new BeaverDefenderBehaviorController(brain, this);
    }

    public sealed class BeaverDefenderBehaviorController : NPCBehaviorController
    {
        private enum CombatPhase
        {
            Approaching,
            Preparing,
            Attacking,
            Recovery
        }

        private readonly BeaverDefenderBehaviorSO config;
        private BeaverDefenderState currentState;
        private CombatPhase combatPhase;
        private NPCBrain followedScout;
        private NetworkObject combatTarget;
        private NPCAnimationController animationController;
        private float stateEndTime;
        private float nextDestinationRefreshTime;
        private float unreachableSince = -1f;

        public BeaverDefenderBehaviorController(NPCBrain brain, BeaverDefenderBehaviorSO config)
            : base(brain)
        {
            this.config = config;
        }

        public BeaverDefenderState CurrentState => currentState;
        public NPCBrain FollowedScout => followedScout;
        public NetworkObject CombatTarget => combatTarget;

        public override void Enter()
        {
            animationController = Brain.GetComponent<NPCAnimationController>();
            NPCFactionDamageAlertSystem.OnNpcFactionMemberDamaged += HandleFactionDamageAlert;
            EnterIdle();
        }

        public override void Tick()
        {
            switch (currentState)
            {
                case BeaverDefenderState.Idle:
                    TickIdle();
                    break;
                case BeaverDefenderState.FollowingScout:
                    TickFollowingScout();
                    break;
                case BeaverDefenderState.AttackMode:
                    TickAttackMode();
                    break;
            }
        }

        public override void Exit()
        {
            NPCFactionDamageAlertSystem.OnNpcFactionMemberDamaged -= HandleFactionDamageAlert;
            BeaverDefenderEscortRegistry.Release(Brain);
            Brain.AttackController?.CancelPendingAttacks();
            followedScout = null;
            combatTarget = null;
            StopAgent();
        }

        private void TickIdle()
        {
            StopAgent();
            if (Time.time < stateEndTime)
            {
                return;
            }

            if (TrySelectScout(out NPCBrain scout))
            {
                EnterFollowingScout(scout);
                return;
            }

            stateEndTime = Time.time + config.IdleDecisionDelay;
        }

        private void EnterIdle()
        {
            BeaverDefenderEscortRegistry.Release(Brain);
            followedScout = null;
            combatTarget = null;
            currentState = BeaverDefenderState.Idle;
            stateEndTime = Time.time + config.IdleDecisionDelay;
            unreachableSince = -1f;
            StopAgent();
        }

        private bool TrySelectScout(out NPCBrain selectedScout)
        {
            selectedScout = null;
            if (config.ScoutDefinition == null || Brain.Agent == null || !Brain.Agent.isOnNavMesh)
            {
                return false;
            }

            float bestDistanceSquared = config.FollowSearchRadius * config.FollowSearchRadius;
            foreach (NPCBrain candidate in NPCRegistry.ActiveNPCs)
            {
                if (!IsValidScout(candidate))
                {
                    continue;
                }

                float distanceSquared = (candidate.transform.position - Brain.transform.position).sqrMagnitude;
                if (distanceSquared > bestDistanceSquared || !HasCompletePath(candidate.transform.position))
                {
                    continue;
                }

                if (!BeaverDefenderEscortRegistry.TryReserve(
                    candidate,
                    Brain,
                    config.MaxDefendersPerScout))
                {
                    continue;
                }

                if (selectedScout != null)
                {
                    BeaverDefenderEscortRegistry.Release(Brain);
                    BeaverDefenderEscortRegistry.TryReserve(
                        candidate,
                        Brain,
                        config.MaxDefendersPerScout);
                }

                selectedScout = candidate;
                bestDistanceSquared = distanceSquared;
            }

            return selectedScout != null;
        }

        private bool IsValidScout(NPCBrain candidate)
        {
            return candidate != null
                && candidate != Brain
                && candidate.Definition == config.ScoutDefinition
                && candidate.Health != null
                && !candidate.Health.IsDead
                && candidate.gameObject.activeInHierarchy;
        }

        private void EnterFollowingScout(NPCBrain scout)
        {
            followedScout = scout;
            currentState = BeaverDefenderState.FollowingScout;
            nextDestinationRefreshTime = 0f;
            unreachableSince = -1f;
            ResumeAgent(config.FollowStoppingDistance);
        }

        private void TickFollowingScout()
        {
            if (!IsValidScout(followedScout))
            {
                EnterIdle();
                return;
            }

            float distance = Vector3.Distance(Brain.transform.position, followedScout.transform.position);
            if (distance <= config.FollowStoppingDistance)
            {
                StopAgent();
                return;
            }

            if (Time.time < nextDestinationRefreshTime)
            {
                return;
            }

            nextDestinationRefreshTime = Time.time + config.FollowDestinationRefreshInterval;
            if (!TrySetDestination(followedScout.transform.position, config.FollowStoppingDistance))
            {
                TrackUnreachableTarget();
                return;
            }

            unreachableSince = -1f;
        }

        private void HandleFactionDamageAlert(NPCFactionDamageAlert alert)
        {
            if (currentState == BeaverDefenderState.AttackMode
                || Brain.FactionMember == null
                || alert.VictimFaction != Brain.FactionMember.Faction
                || alert.Attacker == null
                || Vector3.Distance(Brain.transform.position, alert.Position) > config.FamilyAlertRadius
                || !IsValidCombatTarget(alert.Attacker))
            {
                return;
            }

            EnterAttackMode(alert.Attacker);
        }

        private void EnterAttackMode(NetworkObject target)
        {
            BeaverDefenderEscortRegistry.Release(Brain);
            followedScout = null;
            combatTarget = target;
            currentState = BeaverDefenderState.AttackMode;
            combatPhase = CombatPhase.Approaching;
            nextDestinationRefreshTime = 0f;
            unreachableSince = -1f;
            Brain.AttackController?.CancelPendingAttacks();
        }

        private void TickAttackMode()
        {
            if (!IsValidCombatTarget(combatTarget))
            {
                EnterIdle();
                return;
            }

            switch (combatPhase)
            {
                case CombatPhase.Approaching:
                    TickCombatApproach();
                    break;
                case CombatPhase.Preparing:
                    TickCombatPrepare();
                    break;
                case CombatPhase.Attacking:
                    StopAgent();
                    FaceCombatTarget();
                    break;
                case CombatPhase.Recovery:
                    TickCombatRecovery();
                    break;
            }
        }

        private void TickCombatApproach()
        {
            if (IsCombatTargetInRange())
            {
                combatPhase = CombatPhase.Preparing;
                stateEndTime = Time.time + config.AttackPrepareDuration;
                StopAgent();
                FaceCombatTarget();
                return;
            }

            if (Time.time < nextDestinationRefreshTime)
            {
                return;
            }

            nextDestinationRefreshTime = Time.time + config.AttackApproachRefreshInterval;
            float stoppingDistance = Brain.AttackController != null
                ? Brain.AttackController.AttackRange * 0.85f
                : Brain.InteractionDistance;
            if (!TrySetDestination(combatTarget.transform.position, stoppingDistance))
            {
                TrackUnreachableTarget();
                return;
            }

            unreachableSince = -1f;
        }

        private void TickCombatPrepare()
        {
            StopAgent();
            FaceCombatTarget();
            if (Time.time < stateEndTime)
            {
                return;
            }

            if (!IsCombatTargetInRange())
            {
                combatPhase = CombatPhase.Approaching;
                return;
            }

            animationController?.PlayAction();
            bool started = Brain.AttackController != null
                && Brain.AttackController.StartTargetedAttack(combatTarget, HandleTargetedAttackCompleted);
            if (started)
            {
                combatPhase = CombatPhase.Attacking;
            }
            else
            {
                combatPhase = CombatPhase.Approaching;
            }
        }

        private void HandleTargetedAttackCompleted(NetworkObject target, bool hit)
        {
            if (currentState != BeaverDefenderState.AttackMode)
            {
                return;
            }

            combatPhase = CombatPhase.Recovery;
            stateEndTime = Time.time + config.AttackRecoveryDuration;
        }

        private void TickCombatRecovery()
        {
            StopAgent();
            FaceCombatTarget();
            if (Time.time >= stateEndTime)
            {
                combatPhase = CombatPhase.Approaching;
            }
        }

        private bool IsValidCombatTarget(NetworkObject target)
        {
            if (target == null || target.transform.root == Brain.transform.root)
            {
                return false;
            }

            NPCFactionMember targetFaction = target.GetComponent<NPCFactionMember>();
            if (targetFaction != null && Brain.GetRelationTo(targetFaction.Faction) == NPCFactionRelation.Ally)
            {
                return false;
            }

            if (target.TryGetComponent(out PlayerHealth playerHealth))
            {
                return !playerHealth.IsDowned && playerHealth.gameObject.activeInHierarchy;
            }

            if (target.TryGetComponent(out NPCHealth npcHealth))
            {
                return !npcHealth.IsDead && npcHealth.gameObject.activeInHierarchy;
            }

            return false;
        }

        private bool IsCombatTargetInRange()
        {
            if (combatTarget == null)
            {
                return false;
            }

            float attackRange = Brain.AttackController != null
                ? Brain.AttackController.AttackRange
                : Brain.InteractionDistance;
            return Vector3.Distance(Brain.transform.position, combatTarget.transform.position) <= attackRange;
        }

        private void TrackUnreachableTarget()
        {
            if (unreachableSince < 0f)
            {
                unreachableSince = Time.time;
            }

            if (Time.time - unreachableSince >= config.UnreachableTargetTimeout)
            {
                EnterIdle();
            }
        }

        private bool TrySetDestination(Vector3 destination, float stoppingDistance)
        {
            if (!HasCompletePath(destination))
            {
                return false;
            }

            ResumeAgent(stoppingDistance);
            return Brain.Agent.SetDestination(destination);
        }

        private bool HasCompletePath(Vector3 destination)
        {
            if (Brain.Agent == null || !Brain.Agent.isOnNavMesh)
            {
                return false;
            }

            NavMeshPath path = new NavMeshPath();
            return Brain.Agent.CalculatePath(destination, path)
                && path.status == NavMeshPathStatus.PathComplete;
        }

        private void FaceCombatTarget()
        {
            if (combatTarget == null)
            {
                return;
            }

            Vector3 direction = combatTarget.transform.position - Brain.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            float angularSpeed = Brain.Definition != null ? Brain.Definition.angularSpeed : 360f;
            float stepDuration = Brain.Definition != null
                ? Mathf.Max(Brain.Definition.decisionTickInterval, Time.deltaTime)
                : Mathf.Max(0.2f, Time.deltaTime);
            Brain.transform.rotation = Quaternion.RotateTowards(
                Brain.transform.rotation,
                Quaternion.LookRotation(direction.normalized, Vector3.up),
                angularSpeed * stepDuration);
        }

        private void ResumeAgent(float stoppingDistance)
        {
            if (Brain.Agent == null || !Brain.Agent.isOnNavMesh)
            {
                return;
            }

            Brain.Agent.stoppingDistance = Mathf.Max(0f, stoppingDistance);
            Brain.Agent.isStopped = false;
        }

        private void StopAgent()
        {
            if (Brain.Agent == null || !Brain.Agent.isOnNavMesh)
            {
                return;
            }

            Brain.Agent.isStopped = true;
            Brain.Agent.ResetPath();
        }
    }
}
