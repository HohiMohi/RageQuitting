using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public enum BeaverDefenderState
{
    Idle,
    FollowingScout,
    AttackMode,
    ApproachingDownedPlayer,
    CarryingDownedPlayer,
    AquaticEgress
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

    [Header("Downed Player Carry")]
    [SerializeField, Min(0.1f)] private float pushZoneSearchRadius = 30f;
    [SerializeField, Min(0.05f)] private float downedPlayerApproachRefreshInterval = 0.2f;
    [SerializeField, Range(0.1f, 1f)] private float carryingMoveSpeedMultiplier = 0.7f;
    [SerializeField, Min(0.1f)] private float dropArrivalDistance = 0.65f;
    [SerializeField, Min(0.05f)] private float pushZoneArrivalDistance = 0.15f;
    [SerializeField, Min(0.05f)] private float dropRetryInterval = 0.5f;
    [SerializeField, Min(0.1f)] private float dropAttemptTimeout = 1f;

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
    public float PushZoneSearchRadius => Mathf.Max(0.1f, pushZoneSearchRadius);
    public float DownedPlayerApproachRefreshInterval => Mathf.Max(0.05f, downedPlayerApproachRefreshInterval);
    public float CarryingMoveSpeedMultiplier => Mathf.Clamp(carryingMoveSpeedMultiplier, 0.1f, 1f);
    public float DropArrivalDistance => Mathf.Max(0.1f, dropArrivalDistance);
    public float PushZoneArrivalDistance => Mathf.Max(0.05f, pushZoneArrivalDistance);
    public float DropRetryInterval => Mathf.Max(0.05f, dropRetryInterval);
    public float DropAttemptTimeout => Mathf.Max(0.1f, dropAttemptTimeout);

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
        private NPCAquaticLocomotionController aquaticLocomotion;
        private DownedPlayerCarryable downedPlayerTarget;
        private GoatPushZone selectedPushZone;
        private NPCDownedPlayerDropPoint denDropPoint;
        private Vector3 carryDestination;
        private Quaternion carryDestinationRotation = Quaternion.identity;
        private bool carryDestinationIsPushZone;
        private float defaultAgentSpeed;
        private float dropAttemptStartedAt = -1f;
        private float nextDropAttemptTime;
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
            aquaticLocomotion = Brain.GetComponent<NPCAquaticLocomotionController>();
            defaultAgentSpeed = Brain.Definition != null ? Brain.Definition.moveSpeed : Brain.Agent.speed;
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
                case BeaverDefenderState.ApproachingDownedPlayer:
                    TickApproachingDownedPlayer();
                    break;
                case BeaverDefenderState.CarryingDownedPlayer:
                    TickCarryingDownedPlayer();
                    break;
                case BeaverDefenderState.AquaticEgress:
                    TickAquaticEgress();
                    break;
            }
        }

        public override void Exit()
        {
            NPCFactionDamageAlertSystem.OnNpcFactionMemberDamaged -= HandleFactionDamageAlert;
            ClearDownedPlayerCarryState(dropCarriedPlayer: true);
            BeaverDefenderEscortRegistry.Release(Brain);
            Brain.AttackController?.CancelPendingAttacks();
            followedScout = null;
            combatTarget = null;
            StopAgent();
        }

        public override void HandleDeferredDamage(NPCHealth.DamageEventArgs damageEvent)
        {
            if (damageEvent != null && IsValidCombatTarget(damageEvent.Attacker))
            {
                EnterAttackMode(damageEvent.Attacker);
            }
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
            ClearDownedPlayerCarryState(dropCarriedPlayer: true);
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

            ClearDownedPlayerCarryState(dropCarriedPlayer: true);
            EnterAttackMode(alert.Attacker);
        }

        private void EnterAttackMode(NetworkObject target)
        {
            ClearDownedPlayerCarryState(dropCarriedPlayer: true);
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
            if (TryGetDownedPlayerTarget(combatTarget, out DownedPlayerCarryable carryable))
            {
                BeginApproachingDownedPlayer(carryable);
                return;
            }

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

        private bool TryGetDownedPlayerTarget(NetworkObject target, out DownedPlayerCarryable carryable)
        {
            carryable = null;
            if (target == null
                || !target.TryGetComponent(out PlayerHealth playerHealth)
                || !playerHealth.IsDowned
                || !playerHealth.gameObject.activeInHierarchy)
            {
                return false;
            }

            carryable = target.GetComponent<DownedPlayerCarryable>();
            return carryable != null && (carryable.CanBeCarried || carryable.IsCarriedBy(Brain.Carrier.ActorId));
        }

        private void BeginApproachingDownedPlayer(DownedPlayerCarryable carryable)
        {
            if (carryable == null
                || Brain.Carrier == null
                || !Brain.Carrier.CanCarryObject
                || DownedPlayerCarryReservation.IsReservedByOther(carryable, Brain.Carrier)
                || !DownedPlayerCarryReservation.TryReserve(carryable, Brain.Carrier))
            {
                EnterIdle();
                return;
            }

            BeaverDefenderEscortRegistry.Release(Brain);
            Brain.AttackController?.CancelPendingAttacks();
            downedPlayerTarget = carryable;
            combatTarget = carryable.NetworkObject;
            currentState = BeaverDefenderState.ApproachingDownedPlayer;
            nextDestinationRefreshTime = 0f;
            unreachableSince = -1f;
            dropAttemptStartedAt = -1f;
            RestoreAgentSpeed();
        }

        private void TickApproachingDownedPlayer()
        {
            if (!IsDownedCarryTargetValid(requireCarriedByThisNpc: false))
            {
                EnterIdle();
                return;
            }

            float distance = Vector3.Distance(Brain.transform.position, downedPlayerTarget.transform.position);
            if (distance <= Brain.InteractionDistance)
            {
                SelectCarryDestination();
                if (!Brain.Carrier.TryPickup(downedPlayerTarget.gameObject))
                {
                    EnterIdle();
                    return;
                }

                currentState = BeaverDefenderState.CarryingDownedPlayer;
                Brain.Agent.speed = defaultAgentSpeed * config.CarryingMoveSpeedMultiplier;
                nextDestinationRefreshTime = 0f;
                dropAttemptStartedAt = -1f;
                nextDropAttemptTime = 0f;
                if (aquaticLocomotion != null && aquaticLocomotion.IsSwimming && aquaticLocomotion.BeginAquaticEgress())
                {
                    currentState = BeaverDefenderState.AquaticEgress;
                    return;
                }
                if (!TrySetCarryDestination())
                {
                    SwitchCarryDestinationToDen();
                }

                return;
            }

            if (Time.time < nextDestinationRefreshTime)
            {
                return;
            }

            nextDestinationRefreshTime = Time.time + config.DownedPlayerApproachRefreshInterval;
            if (!TrySetDestination(downedPlayerTarget.transform.position, Brain.InteractionDistance * 0.85f))
            {
                TrackUnreachableTarget();
                return;
            }

            unreachableSince = -1f;
        }

        private void TickCarryingDownedPlayer()
        {
            if (!IsDownedCarryTargetValid(requireCarriedByThisNpc: true))
            {
                EnterIdle();
                return;
            }

            if (carryDestinationIsPushZone
                && (selectedPushZone == null
                    || !selectedPushZone.CanAcceptCarriedPlayerDrop
                    || !HasCompletePath(carryDestination)))
            {
                SwitchCarryDestinationToDen();
            }

            if (Time.time >= nextDestinationRefreshTime)
            {
                nextDestinationRefreshTime = Time.time + config.DownedPlayerApproachRefreshInterval;
                if (TrySetCarryDestination())
                {
                    unreachableSince = -1f;
                }
                else if (carryDestinationIsPushZone)
                {
                    SwitchCarryDestinationToDen();
                }
                else
                {
                    TrackUnreachableCarryDestination();
                }
            }

            Vector3 delta = carryDestination - Brain.transform.position;
            delta.y = 0f;
            float arrivalDistance = GetCurrentCarryArrivalDistance();
            if (delta.sqrMagnitude > arrivalDistance * arrivalDistance)
            {
                return;
            }

            StopAgent();
            if (carryDestinationIsPushZone)
            {
                Brain.transform.rotation = carryDestinationRotation;
                ThrowCarriedPlayer();
                return;
            }

            TryDropCarriedPlayerAtDen();
        }

        private void TickAquaticEgress()
        {
            if (!IsDownedCarryTargetValid(requireCarriedByThisNpc: true))
            {
                EnterIdle();
                return;
            }

            if (aquaticLocomotion == null || !aquaticLocomotion.IsAquaticEgressActive)
            {
                currentState = BeaverDefenderState.CarryingDownedPlayer;
                if (!TrySetCarryDestination())
                {
                    SwitchCarryDestinationToDen();
                }
                return;
            }

            if (!aquaticLocomotion.TryGetAquaticEgressDestination(out Vector3 exitPosition)
                || !TrySetDestination(exitPosition, 0.3f))
            {
                TryDropCarriedPlayerNearCarrier();
                return;
            }

            if (aquaticLocomotion.HasReachedAquaticEgressDestination)
            {
                aquaticLocomotion.EndAquaticEgress();
                currentState = BeaverDefenderState.CarryingDownedPlayer;
                if (!TrySetCarryDestination())
                {
                    SwitchCarryDestinationToDen();
                }
            }
        }

        private void SelectCarryDestination()
        {
            selectedPushZone = null;
            carryDestinationIsPushZone = false;
            float bestPathLength = float.PositiveInfinity;

            foreach (GoatPushZone zone in GoatPushZone.Zones)
            {
                if (zone == null
                    || !zone.CanAcceptCarriedPlayerDrop
                    || Vector3.Distance(Brain.transform.position, zone.ApproachPosition) > config.PushZoneSearchRadius
                    || !zone.TryGetCarrierThrowPose(Brain.Agent, out Vector3 position, out Quaternion rotation))
                {
                    continue;
                }

                NavMeshPath path = new NavMeshPath();
                if (!Brain.Agent.CalculatePath(position, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }

                float pathLength = GetPathLength(path, Brain.transform.position);
                if (pathLength >= bestPathLength)
                {
                    continue;
                }

                bestPathLength = pathLength;
                selectedPushZone = zone;
                carryDestination = position;
                carryDestinationRotation = rotation;
                carryDestinationIsPushZone = true;
            }

            if (!carryDestinationIsPushZone)
            {
                ResolveDenDestination();
            }
        }

        private void SwitchCarryDestinationToDen()
        {
            selectedPushZone = null;
            carryDestinationIsPushZone = false;
            ResolveDenDestination();
            if (TrySetCarryDestination())
            {
                unreachableSince = -1f;
            }
            else if (unreachableSince < 0f)
            {
                unreachableSince = Time.time;
            }
        }

        private void ResolveDenDestination()
        {
            denDropPoint = Brain.OriginSpawner != null
                ? Brain.OriginSpawner.GetComponent<NPCDownedPlayerDropPoint>()
                : null;
            carryDestination = denDropPoint != null ? denDropPoint.Position : Brain.SpawnPosition;
            Vector3 direction = Vector3.ProjectOnPlane(carryDestination - Brain.transform.position, Vector3.up);
            carryDestinationRotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : Brain.transform.rotation;
        }

        private bool TrySetCarryDestination()
        {
            return TrySetDestination(carryDestination, GetCurrentCarryArrivalDistance());
        }

        private float GetCurrentCarryArrivalDistance()
        {
            return carryDestinationIsPushZone
                ? config.PushZoneArrivalDistance
                : config.DropArrivalDistance;
        }

        private void ThrowCarriedPlayer()
        {
            if (selectedPushZone == null || !selectedPushZone.CanAcceptCarriedPlayerDrop)
            {
                SwitchCarryDestinationToDen();
                return;
            }

            DownedPlayerCarryable target = downedPlayerTarget;
            Vector3 releasePosition = selectedPushZone.GetCarriedPlayerReleasePosition(Brain.transform.position);
            Quaternion releaseRotation = Quaternion.LookRotation(selectedPushZone.PushDirection, Vector3.up);
            ExternalImpulseProfileSO impulseProfile = selectedPushZone.PushImpulseProfile;
            Vector3 pushDirection = selectedPushZone.PushDirection;

            if (!Brain.Carrier.DropHeldObject(releasePosition, releaseRotation))
            {
                EnterIdle();
                return;
            }

            if (target != null && impulseProfile != null)
            {
                foreach (MonoBehaviour behaviour in target.GetComponents<MonoBehaviour>())
                {
                    if (behaviour is IExternalImpulseReceiver receiver)
                    {
                        receiver.TryApplyExternalImpulse(
                            impulseProfile.CreateImpulse(pushDirection),
                            Brain.NetworkObject);
                        break;
                    }
                }
            }

            FinishDownedPlayerTransport();
        }

        private void TryDropCarriedPlayerAtDen()
        {
            if (Time.time < nextDropAttemptTime)
            {
                return;
            }

            nextDropAttemptTime = Time.time + config.DropRetryInterval;
            if (dropAttemptStartedAt < 0f)
            {
                dropAttemptStartedAt = Time.time;
            }

            bool hasSafePosition = denDropPoint != null
                ? denDropPoint.TryGetSafeDropPosition(downedPlayerTarget, Brain.transform, out Vector3 dropPosition)
                : TryGetFallbackDropPosition(carryDestination, out dropPosition);
            if (!hasSafePosition && Time.time - dropAttemptStartedAt < config.DropAttemptTimeout)
            {
                return;
            }

            if (!hasSafePosition)
            {
                dropPosition = Brain.transform.position + Brain.transform.forward * 1.1f + Vector3.up * 0.1f;
            }

            if (!Brain.Carrier.DropHeldObject(dropPosition, carryDestinationRotation))
            {
                EnterIdle();
                return;
            }

            FinishDownedPlayerTransport();
        }

        private void TrackUnreachableCarryDestination()
        {
            if (unreachableSince < 0f)
            {
                unreachableSince = Time.time;
                return;
            }

            if (Time.time - unreachableSince < config.DropAttemptTimeout)
            {
                return;
            }

            TryDropCarriedPlayerNearCarrier();
        }

        private void TryDropCarriedPlayerNearCarrier()
        {
            Vector3 desiredPosition = Brain.transform.position
                + Brain.transform.forward * 1.1f;
            Vector3 dropPosition = TryGetFallbackDropPosition(desiredPosition, out Vector3 safePosition)
                ? safePosition
                : desiredPosition + Vector3.up * 0.1f;

            if (!Brain.Carrier.DropHeldObject(dropPosition, Brain.transform.rotation))
            {
                EnterIdle();
                return;
            }

            FinishDownedPlayerTransport();
        }

        private bool TryGetFallbackDropPosition(Vector3 desiredPosition, out Vector3 position)
        {
            position = default;
            if (!NavMesh.SamplePosition(desiredPosition, out NavMeshHit hit, 2f, Brain.Agent.areaMask))
            {
                return false;
            }

            position = hit.position;
            return true;
        }

        private bool IsDownedCarryTargetValid(bool requireCarriedByThisNpc)
        {
            if (downedPlayerTarget == null
                || downedPlayerTarget.NetworkObject == null
                || !downedPlayerTarget.gameObject.activeInHierarchy
                || downedPlayerTarget.GetComponent<PlayerHealth>() is not PlayerHealth health
                || !health.IsDowned
                || !DownedPlayerCarryReservation.IsReservedBy(downedPlayerTarget, Brain.Carrier))
            {
                return false;
            }

            return requireCarriedByThisNpc
                ? downedPlayerTarget.IsCarriedBy(Brain.Carrier.ActorId)
                    && Brain.Carrier.CarriedObject == downedPlayerTarget.gameObject
                : !downedPlayerTarget.IsCarried;
        }

        private void FinishDownedPlayerTransport()
        {
            DownedPlayerCarryReservation.Release(downedPlayerTarget, Brain.Carrier);
            downedPlayerTarget = null;
            selectedPushZone = null;
            denDropPoint = null;
            carryDestinationIsPushZone = false;
            dropAttemptStartedAt = -1f;
            unreachableSince = -1f;
            RestoreAgentSpeed();
            EnterIdle();
        }

        private void ClearDownedPlayerCarryState(bool dropCarriedPlayer)
        {
            aquaticLocomotion?.EndAquaticEgress();
            if (dropCarriedPlayer
                && downedPlayerTarget != null
                && Brain.Carrier != null
                && Brain.Carrier.CarriedObject == downedPlayerTarget.gameObject)
            {
                Brain.Carrier.DropHeldObject();
            }

            DownedPlayerCarryReservation.Release(downedPlayerTarget, Brain.Carrier);
            DownedPlayerCarryReservation.ReleaseAll(Brain.Carrier);
            downedPlayerTarget = null;
            selectedPushZone = null;
            denDropPoint = null;
            carryDestinationIsPushZone = false;
            dropAttemptStartedAt = -1f;
            unreachableSince = -1f;
            RestoreAgentSpeed();
        }

        private void RestoreAgentSpeed()
        {
            if (Brain.Agent != null)
            {
                Brain.Agent.speed = defaultAgentSpeed > 0f
                    ? defaultAgentSpeed
                    : Brain.Definition != null ? Brain.Definition.moveSpeed : Brain.Agent.speed;
            }
        }

        private static float GetPathLength(NavMeshPath path, Vector3 start)
        {
            if (path?.corners == null || path.corners.Length == 0)
            {
                return float.PositiveInfinity;
            }

            float length = 0f;
            Vector3 previous = start;
            foreach (Vector3 corner in path.corners)
            {
                length += Vector3.Distance(previous, corner);
                previous = corner;
            }

            return length;
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
