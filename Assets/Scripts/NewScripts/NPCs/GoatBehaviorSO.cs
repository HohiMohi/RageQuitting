using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum GoatPushPhase
{
    None,
    MovingToZone,
    Positioning,
    Attacking,
    Recovery
}

[CreateAssetMenu(fileName = "GoatBehavior", menuName = "Scriptable Objects/NPC/Behaviors/Goat")]
public class GoatBehaviorSO : NPCBehaviorSO
{
    public enum GoatState
    {
        Idle,
        Wandering,
        MovingToStandingTarget,
        JumpingOntoTarget,
        Standing,
        JumpingDown,
        Charging,
        PushAttempt
    }

    [Header("Calm behavior")]
    [SerializeField] private float idleDurationMin = 1.5f;
    [SerializeField] private float idleDurationMax = 3.5f;
    [SerializeField] private float wanderArrivalDistance = 0.6f;
    [SerializeField] private int wanderPointAttempts = 8;

    [Header("Standing targets")]
    [SerializeField] private GoatStandingTargetProfileSO standingTargetProfile;
    [SerializeField] private float standingSearchRadius = 10f;
    [SerializeField] private float standingSearchInterval = 0.5f;
    [SerializeField] private float standingDuration = 15f;
    [SerializeField] private float standingApproachDistance = 0.35f;
    [SerializeField] private float jumpDuration = 0.65f;
    [SerializeField] private float jumpArcHeight = 0.65f;
    [SerializeField] private float maxJumpHeight = 2f;
    [SerializeField] private float maxJumpHorizontalDistance = 2.75f;
    [SerializeField] private float targetMovementTolerance = 0.15f;
    [SerializeField] private float stationaryLinearVelocity = 0.15f;
    [SerializeField] private float stationaryAngularVelocity = 10f;
    [SerializeField] private float landingClearance = 0.03f;

    [Header("Charge")]
    [SerializeField] private NPCFactionSO playerFaction;
    [SerializeField] private ExternalImpulseProfileSO chargeImpulseProfile;
    [SerializeField] private float proximityThreatRange = 6f;
    [SerializeField] private float proximityThreatDuration = 2f;
    [SerializeField] private float chargeTelegraphDuration = 1.2f;
    [SerializeField] private float chargeMaxSpeed = 13.5f;
    [SerializeField] private float chargeAcceleration = 9f;
    [SerializeField] private float chargeSteeringDegreesPerSecond = 360f;
    [SerializeField] private float chargeCommittedDuration = 2.25f;
    [SerializeField] private float chargeDeceleration = 18f;
    [SerializeField] private float chargeDamage = 20f;
    [SerializeField] private float chargeCooldown = 5f;
    [SerializeField] private float chargeCollisionSkin = 0.05f;
    [SerializeField] private float chargeBlockedRecoveryDuration = 0.35f;

    [Header("Push attempt")]
    [SerializeField] private float pushZoneSearchRadius = 30f;
    [SerializeField] private float pushZoneSearchInterval = 0.5f;
    [SerializeField] private float pushApproachDistance = 0.75f;
    [SerializeField] private float pushSetupDistance = 1.1f;
    [SerializeField] private float pushPositionUpdateInterval = 0.2f;
    [SerializeField] private float pushPositionTolerance = 0.2f;
    [SerializeField] private float pushFacingToleranceDegrees = 10f;
    [SerializeField] private float pushRecoveryDuration = 0.75f;
    [SerializeField] private float pushAttemptCooldown = 5f;

    public float IdleDurationMin => Mathf.Max(0.1f, Mathf.Min(idleDurationMin, idleDurationMax));
    public float IdleDurationMax => Mathf.Max(IdleDurationMin, idleDurationMax);
    public float WanderArrivalDistance => Mathf.Max(0.1f, wanderArrivalDistance);
    public int WanderPointAttempts => Mathf.Max(1, wanderPointAttempts);
    public GoatStandingTargetProfileSO StandingTargetProfile => standingTargetProfile;
    public float StandingSearchRadius => Mathf.Max(0.1f, standingSearchRadius);
    public float StandingSearchInterval => Mathf.Max(0.05f, standingSearchInterval);
    public float StandingDuration => Mathf.Max(0.1f, standingDuration);
    public float StandingApproachDistance => Mathf.Max(0.05f, standingApproachDistance);
    public float JumpDuration => Mathf.Max(0.05f, jumpDuration);
    public float JumpArcHeight => Mathf.Max(0f, jumpArcHeight);
    public float MaxJumpHeight => Mathf.Max(0.1f, maxJumpHeight);
    public float MaxJumpHorizontalDistance => Mathf.Max(0.1f, maxJumpHorizontalDistance);
    public float TargetMovementTolerance => Mathf.Max(0.01f, targetMovementTolerance);
    public float StationaryLinearVelocity => Mathf.Max(0f, stationaryLinearVelocity);
    public float StationaryAngularVelocity => Mathf.Max(0f, stationaryAngularVelocity);
    public float LandingClearance => Mathf.Max(0.01f, landingClearance);
    public NPCFactionSO PlayerFaction => playerFaction;
    public ExternalImpulseProfileSO ChargeImpulseProfile => chargeImpulseProfile;
    public float ProximityThreatRange => Mathf.Max(0.1f, proximityThreatRange);
    public float ProximityThreatDuration => Mathf.Max(0f, proximityThreatDuration);
    public float ChargeTelegraphDuration => Mathf.Max(0.05f, chargeTelegraphDuration);
    public float ChargeMaxSpeed => Mathf.Max(0.1f, chargeMaxSpeed);
    public float ChargeAcceleration => Mathf.Max(0.1f, chargeAcceleration);
    public float ChargeSteeringDegreesPerSecond => Mathf.Max(0f, chargeSteeringDegreesPerSecond);
    public float ChargeCommittedDuration => Mathf.Max(0.05f, chargeCommittedDuration);
    public float ChargeDeceleration => Mathf.Max(0.1f, chargeDeceleration);
    public float ChargeDamage => Mathf.Max(0f, chargeDamage);
    public float ChargeCooldown => Mathf.Max(0f, chargeCooldown);
    public float ChargeCollisionSkin => Mathf.Max(0f, chargeCollisionSkin);
    public float ChargeBlockedRecoveryDuration => Mathf.Max(0f, chargeBlockedRecoveryDuration);
    public float PushZoneSearchRadius => Mathf.Max(0.1f, pushZoneSearchRadius);
    public float PushZoneSearchInterval => Mathf.Max(0.05f, pushZoneSearchInterval);
    public float PushApproachDistance => Mathf.Max(0.1f, pushApproachDistance);
    public float PushSetupDistance => Mathf.Max(0.1f, pushSetupDistance);
    public float PushPositionUpdateInterval => Mathf.Max(0.05f, pushPositionUpdateInterval);
    public float PushPositionTolerance => Mathf.Max(0.05f, pushPositionTolerance);
    public float PushFacingToleranceDegrees => Mathf.Clamp(pushFacingToleranceDegrees, 0.5f, 90f);
    public float PushRecoveryDuration => Mathf.Max(0f, pushRecoveryDuration);
    public float PushAttemptCooldown => Mathf.Max(0f, pushAttemptCooldown);

    public override NPCBehaviorController CreateController(NPCBrain brain)
    {
        return new GoatBehaviorController(brain, this);
    }

    public sealed class GoatBehaviorController : NPCBehaviorController
    {
        private readonly struct StandingCandidate
        {
            public readonly Component Target;
            public readonly GoatStandingPose Pose;
            public readonly float Distance;

            public StandingCandidate(Component target, GoatStandingPose pose, float distance)
            {
                Target = target;
                Pose = pose;
                Distance = distance;
            }
        }

        private readonly GoatBehaviorSO config;
        private readonly HashSet<int> visitedStandingTargetIds = new HashSet<int>();
        private GoatState currentState;
        private PlayerHealth currentTarget;
        private PlayerHealth pendingChargeTarget;
        private GoatPushZone activePushZone;
        private Component standingTarget;
        private GoatStandingPose standingPose;
        private Vector3 standingTargetStartPosition;
        private Quaternion standingTargetStartRotation;
        private Vector3 standingLocalPosition;
        private Vector3 homePosition;
        private GoatStandingMotionDriver motionDriver;
        private GoatChargeController chargeController;
        private NPCAnimationController animationController;
        private GoatPushPhase pushPhase;
        private Vector3 currentPushSetupPosition;
        private float stateEndTime;
        private float nextStandingSearchTime;
        private float nextPushSearchTime;
        private float nextPushPositionUpdateTime;
        private float pushAvailableAt;
        private float chargeAvailableAt;
        private float proximityThreatStartedAt = -1f;

        public GoatState CurrentState => currentState;
        public PlayerHealth CurrentTarget => currentTarget;
        public PlayerHealth PendingChargeTarget => pendingChargeTarget;
        public GoatPushZone ActivePushZone => activePushZone;
        public GoatPushPhase CurrentPushPhase => pushPhase;
        public Component CurrentStandingTarget => standingTarget;
        public IReadOnlyCollection<int> VisitedStandingTargetIds => visitedStandingTargetIds;
        public GoatChargePhase CurrentChargePhase =>
            chargeController != null ? chargeController.CurrentPhase : GoatChargePhase.None;

        public GoatBehaviorController(NPCBrain brain, GoatBehaviorSO config) : base(brain)
        {
            this.config = config;
        }

        public override void Enter()
        {
            homePosition = Brain.transform.position;
            animationController = Brain.GetComponent<NPCAnimationController>();
            motionDriver = Brain.GetComponent<GoatStandingMotionDriver>();
            if (motionDriver == null)
            {
                motionDriver = Brain.gameObject.AddComponent<GoatStandingMotionDriver>();
            }

            chargeController = Brain.GetComponent<GoatChargeController>();
            if (chargeController == null)
            {
                Debug.LogError($"Goat '{Brain.name}' is missing {nameof(GoatChargeController)} on its prefab.");
            }
            else
            {
                chargeController.ChargeFinished += ChargeController_ChargeFinished;
            }

            if (Brain.Health != null)
            {
                Brain.Health.OnDamaged += BrainHealth_OnDamaged;
            }

            EnterIdle();
        }

        public override void Exit()
        {
            if (Brain.Health != null)
            {
                Brain.Health.OnDamaged -= BrainHealth_OnDamaged;
            }

            if (chargeController != null)
            {
                chargeController.ChargeFinished -= ChargeController_ChargeFinished;
                chargeController.CancelCharge();
            }
            ReleaseStandingTarget();
            ClearPushAttemptState(false);
            motionDriver?.Cancel();
            RestoreAgentAtNearestNavMesh();
            currentTarget = null;
            pendingChargeTarget = null;
            activePushZone = null;
            ResetAgentPath();
        }

        public override void Tick()
        {
            if (currentState == GoatState.Idle
                && Time.time >= pushAvailableAt
                && Time.time >= nextPushSearchTime)
            {
                nextPushSearchTime = Time.time + config.PushZoneSearchInterval;
                if (TryStartPushOpportunity())
                {
                    return;
                }
            }

            TryUpdateProximityThreat();

            switch (currentState)
            {
                case GoatState.Idle:
                    UpdateIdle();
                    break;
                case GoatState.Wandering:
                    UpdateWandering();
                    break;
                case GoatState.MovingToStandingTarget:
                    UpdateMovingToStandingTarget();
                    break;
                case GoatState.JumpingOntoTarget:
                    UpdateJumpingOntoTarget();
                    break;
                case GoatState.Standing:
                    UpdateStanding();
                    break;
                case GoatState.JumpingDown:
                    UpdateJumpingDown();
                    break;
                case GoatState.Charging:
                    UpdateCharging();
                    break;
                case GoatState.PushAttempt:
                    UpdatePushAttempt();
                    break;
            }
        }

        public bool TryBeginCharge(PlayerHealth target)
        {
            if (target == null
                || target.IsDowned
                || currentState == GoatState.Charging
                || Time.time < chargeAvailableAt)
            {
                return false;
            }

            if (currentState == GoatState.PushAttempt)
            {
                ClearPushAttemptState(true);
            }

            if (IsStandingFlowState())
            {
                pendingChargeTarget = target;
                BeginSafeStandingExit();
                return true;
            }

            EnterCharge(target);
            return true;
        }

        private void EnterCharge(PlayerHealth target)
        {
            if (chargeController == null || !chargeController.BeginCharge(target, config))
            {
                EnterIdle();
                return;
            }

            currentTarget = target;
            pendingChargeTarget = null;
            activePushZone = null;
            currentState = GoatState.Charging;
            StopAgent();
        }

        public bool TryBeginPushAttempt(PlayerHealth target, GoatPushZone zone)
        {
            Vector3 approachPosition = default;
            Vector3 setupPosition = default;
            if (target == null
                || target.IsDowned
                || zone == null
                || zone.PushImpulseProfile == null
                || !zone.ContainsPlayer(target)
                || currentState != GoatState.Idle
                || Time.time < pushAvailableAt
                || GoatPushTargetReservations.IsReservedByOther(target, this)
                || !TryResolveReachablePoint(zone.ApproachPosition, out approachPosition)
                || !zone.TryGetPushSetupPosition(target, config.PushSetupDistance, out setupPosition)
                || !HasCompletePath(Brain.transform.position, approachPosition)
                || !HasCompletePath(approachPosition, setupPosition)
                || !GoatPushTargetReservations.TryReserve(target, this))
            {
                return false;
            }

            currentTarget = target;
            activePushZone = zone;
            pushPhase = GoatPushPhase.MovingToZone;
            currentPushSetupPosition = setupPosition;
            currentState = GoatState.PushAttempt;
            SetDestination(approachPosition, config.PushApproachDistance);
            return true;
        }

        public void CancelStanding()
        {
            BeginSafeStandingExit();
        }

        private void EnterIdle()
        {
            if (currentState == GoatState.PushAttempt)
            {
                ClearPushAttemptState(false);
            }

            currentState = GoatState.Idle;
            currentTarget = null;
            activePushZone = null;
            proximityThreatStartedAt = -1f;
            stateEndTime = Time.time + Random.Range(config.IdleDurationMin, config.IdleDurationMax);
            StopAgent();
        }

        private void UpdateIdle()
        {
            StopAgent();
            if (Time.time < stateEndTime)
            {
                return;
            }

            if (Time.time >= nextStandingSearchTime)
            {
                nextStandingSearchTime = Time.time + config.StandingSearchInterval;
                if (TryFindStandingTarget(out StandingCandidate candidate)
                    && GoatStandingTargetReservations.TryReserve(candidate.Target, this))
                {
                    EnterMovingToStandingTarget(candidate);
                    return;
                }
            }

            EnterWandering();
        }

        private void EnterMovingToStandingTarget(StandingCandidate candidate)
        {
            standingTarget = candidate.Target;
            standingPose = candidate.Pose;
            standingTargetStartPosition = standingTarget.transform.position;
            standingTargetStartRotation = standingTarget.transform.rotation;
            currentState = GoatState.MovingToStandingTarget;
            SetDestination(standingPose.ApproachPosition, config.StandingApproachDistance);
        }

        private void UpdateMovingToStandingTarget()
        {
            if (!ValidateStandingTarget(true))
            {
                AbortStandingOnGround();
                return;
            }

            NavMeshAgent agent = Brain.Agent;
            if (agent == null || !agent.isOnNavMesh || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                AbortStandingOnGround();
                return;
            }

            if (!agent.pathPending && agent.remainingDistance <= config.StandingApproachDistance)
            {
                BeginJumpOntoTarget();
            }
        }

        private void BeginJumpOntoTarget()
        {
            if (!ValidateStandingTarget(true))
            {
                AbortStandingOnGround();
                return;
            }

            StopAndDisableAgent();
            motionDriver.Begin(
                Brain.transform.position,
                standingPose.LandingPosition,
                Brain.transform.rotation,
                standingPose.LandingRotation,
                config.JumpDuration,
                config.JumpArcHeight);
            currentState = GoatState.JumpingOntoTarget;
        }

        private void UpdateJumpingOntoTarget()
        {
            if (!ValidateStandingTarget(true))
            {
                BeginJumpDown();
                return;
            }

            if (motionDriver != null && motionDriver.IsMoving)
            {
                return;
            }

            Brain.transform.SetPositionAndRotation(standingPose.LandingPosition, standingPose.LandingRotation);
            standingLocalPosition = standingTarget.transform.InverseTransformPoint(standingPose.LandingPosition);
            visitedStandingTargetIds.Add(standingTarget.GetInstanceID());
            currentState = GoatState.Standing;
            stateEndTime = Time.time + config.StandingDuration;
        }

        private void UpdateStanding()
        {
            if (!ValidateStandingTarget(false))
            {
                BeginJumpDown();
                return;
            }

            Brain.transform.position = standingTarget.transform.TransformPoint(standingLocalPosition);
            if (Time.time >= stateEndTime)
            {
                BeginJumpDown();
            }
        }

        private void BeginSafeStandingExit()
        {
            if (currentState == GoatState.MovingToStandingTarget)
            {
                AbortStandingOnGround();
                return;
            }

            if (currentState == GoatState.JumpingDown)
            {
                return;
            }

            if (currentState == GoatState.JumpingOntoTarget || currentState == GoatState.Standing)
            {
                BeginJumpDown();
            }
        }

        private void BeginJumpDown()
        {
            Vector3 exitPosition = ResolveSafeExitPosition();
            standingPose = new GoatStandingPose(
                standingPose.LandingPosition,
                standingPose.LandingRotation,
                standingPose.ApproachPosition,
                exitPosition);
            StopAndDisableAgent();
            Vector3 facing = Vector3.ProjectOnPlane(exitPosition - Brain.transform.position, Vector3.up);
            Quaternion exitRotation = facing.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(facing.normalized, Vector3.up)
                : Brain.transform.rotation;
            motionDriver.Begin(
                Brain.transform.position,
                exitPosition,
                Brain.transform.rotation,
                exitRotation,
                config.JumpDuration,
                config.JumpArcHeight);
            currentState = GoatState.JumpingDown;
        }

        private void UpdateJumpingDown()
        {
            Vector3 exitPosition = standingPose.ExitPosition;
            if (motionDriver != null && motionDriver.IsMoving)
            {
                return;
            }

            Brain.transform.position = exitPosition;
            EnableAndWarpAgent(exitPosition);
            ReleaseStandingTarget();

            PlayerHealth chargeTarget = pendingChargeTarget;
            pendingChargeTarget = null;
            if (chargeTarget != null && !chargeTarget.IsDowned)
            {
                chargeAvailableAt = 0f;
                EnterCharge(chargeTarget);
            }
            else
            {
                EnterIdle();
            }
        }

        private Vector3 ResolveSafeExitPosition()
        {
            int areaMask = Brain.Agent != null ? Brain.Agent.areaMask : NavMesh.AllAreas;
            Vector3 preferred = standingPose.ExitPosition;
            if (NavMesh.SamplePosition(preferred, out NavMeshHit preferredHit, 1.5f, areaMask))
            {
                return preferredHit.position;
            }

            if (NavMesh.SamplePosition(Brain.transform.position, out NavMeshHit nearestHit, config.MaxJumpHorizontalDistance, areaMask))
            {
                return nearestHit.position;
            }

            return preferred;
        }

        private void AbortStandingOnGround()
        {
            ReleaseStandingTarget();
            PlayerHealth chargeTarget = pendingChargeTarget;
            pendingChargeTarget = null;
            if (chargeTarget != null && !chargeTarget.IsDowned)
            {
                chargeAvailableAt = 0f;
                EnterCharge(chargeTarget);
            }
            else
            {
                EnterIdle();
            }
        }

        private bool TryFindStandingTarget(out StandingCandidate bestCandidate)
        {
            bestCandidate = default;
            if (config.StandingTargetProfile == null)
            {
                return false;
            }

            float bestDistance = float.PositiveInfinity;
            HashSet<int> checkedTargets = new HashSet<int>();
            Collider[] hits = Physics.OverlapSphere(
                Brain.transform.position,
                config.StandingSearchRadius,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);

            foreach (Collider hit in hits)
            {
                Component target = ResolveAllowedStandingTarget(hit);
                if (target == null
                    || !checkedTargets.Add(target.GetInstanceID())
                    || visitedStandingTargetIds.Contains(target.GetInstanceID())
                    || GoatStandingTargetReservations.IsReservedByOther(target, this)
                    || !IsStandingTargetStable(target))
                {
                    continue;
                }

                GoatStandingSurface surface = target.GetComponentInChildren<GoatStandingSurface>(true);
                bool resolved = surface != null
                    ? surface.TryGetStandingPose(
                        Brain,
                        target.transform,
                        config.MaxJumpHeight,
                        config.MaxJumpHorizontalDistance,
                        config.LandingClearance,
                        out GoatStandingPose pose)
                    : GoatStandingSurfaceResolver.TryResolve(
                        Brain,
                        target.transform,
                        config.MaxJumpHeight,
                        config.MaxJumpHorizontalDistance,
                        config.LandingClearance,
                        out pose);
                if (!resolved)
                {
                    continue;
                }

                float distance = Vector3.Distance(Brain.transform.position, pose.ApproachPosition);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestCandidate = new StandingCandidate(target, pose, distance);
            }

            return !float.IsPositiveInfinity(bestDistance);
        }

        private Component ResolveAllowedStandingTarget(Collider hit)
        {
            BaseResourceNew resource = hit.GetComponentInParent<BaseResourceNew>();
            if (resource != null && config.StandingTargetProfile.IsAllowed(resource))
            {
                return resource;
            }

            MountableBridgeComponent mountable = hit.GetComponentInParent<MountableBridgeComponent>();
            if (mountable != null && config.StandingTargetProfile.IsAllowed(mountable))
            {
                return mountable;
            }

            return null;
        }

        private bool ValidateStandingTarget(bool requireStationary)
        {
            if (standingTarget == null || !standingTarget.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (standingTarget is BaseResourceNew resource && resource.IsPickedUp)
            {
                return false;
            }

            if (standingTarget is MountableBridgeComponent mountable && mountable.IsPickedUp)
            {
                return false;
            }

            float movement = Vector3.Distance(standingTarget.transform.position, standingTargetStartPosition);
            float rotation = Quaternion.Angle(standingTarget.transform.rotation, standingTargetStartRotation);
            if (movement > config.TargetMovementTolerance || rotation > 5f)
            {
                return false;
            }

            return !requireStationary || IsStandingTargetStable(standingTarget);
        }

        private bool IsStandingTargetStable(Component target)
        {
            if (target is BaseResourceNew resource && resource.IsPickedUp)
            {
                return false;
            }

            if (target is MountableBridgeComponent mountable && mountable.IsPickedUp)
            {
                return false;
            }

            Rigidbody body = target.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic)
            {
                return true;
            }

            return body.linearVelocity.magnitude <= config.StationaryLinearVelocity
                && body.angularVelocity.magnitude * Mathf.Rad2Deg <= config.StationaryAngularVelocity;
        }

        private bool IsStandingFlowState()
        {
            return currentState == GoatState.MovingToStandingTarget
                || currentState == GoatState.JumpingOntoTarget
                || currentState == GoatState.Standing
                || currentState == GoatState.JumpingDown;
        }

        private void ReleaseStandingTarget()
        {
            if (standingTarget != null)
            {
                GoatStandingTargetReservations.Release(standingTarget, this);
            }

            standingTarget = null;
        }

        private void EnterWandering()
        {
            if (!TrySelectWanderTarget(out Vector3 target))
            {
                EnterIdle();
                return;
            }

            currentState = GoatState.Wandering;
            SetDestination(target, config.WanderArrivalDistance);
        }

        private void UpdateWandering()
        {
            NavMeshAgent agent = Brain.Agent;
            if (agent == null || !agent.isOnNavMesh || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                EnterIdle();
                return;
            }

            if (!agent.pathPending && agent.remainingDistance <= config.WanderArrivalDistance)
            {
                EnterIdle();
            }
        }

        private void UpdateCharging()
        {
            if (chargeController == null || !chargeController.IsCharging)
            {
                EnterIdle();
            }
        }

        private void ChargeController_ChargeFinished(GoatChargeEndReason reason)
        {
            if (currentState != GoatState.Charging)
            {
                return;
            }

            if (reason != GoatChargeEndReason.Cancelled)
            {
                chargeAvailableAt = Time.time + config.ChargeCooldown;
            }

            EnterIdle();
        }

        private bool TryStartPushOpportunity()
        {
            PlayerHealth bestTarget = null;
            GoatPushZone bestZone = null;
            float searchRadiusSqr = config.PushZoneSearchRadius * config.PushZoneSearchRadius;
            float bestSqrDistance = searchRadiusSqr;
            HashSet<int> checkedPlayers = new HashSet<int>();

            foreach (GoatPushZone zone in GoatPushZone.Zones)
            {
                if (zone == null
                    || !zone.isActiveAndEnabled
                    || zone.PushImpulseProfile == null
                    || (zone.transform.position - Brain.transform.position).sqrMagnitude > searchRadiusSqr)
                {
                    continue;
                }

                Collider[] hits = Physics.OverlapSphere(
                    zone.transform.position,
                    config.PushZoneSearchRadius,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Collide);
                checkedPlayers.Clear();
                foreach (Collider hit in hits)
                {
                    PlayerHealth player = hit.GetComponentInParent<PlayerHealth>();
                    if (player == null
                        || !checkedPlayers.Add(player.GetInstanceID())
                        || player.IsDowned
                        || !IsPlayerFaction(player)
                        || !zone.ContainsPlayer(player)
                        || GoatPushTargetReservations.IsReservedByOther(player, this))
                    {
                        continue;
                    }

                    float sqrDistance = (player.transform.position - Brain.transform.position).sqrMagnitude;
                    if (sqrDistance > bestSqrDistance
                        || !TryResolveReachablePoint(zone.ApproachPosition, out Vector3 approachPosition)
                        || !zone.TryGetPushSetupPosition(player, config.PushSetupDistance, out Vector3 setupPosition)
                        || !HasCompletePath(Brain.transform.position, approachPosition)
                        || !HasCompletePath(approachPosition, setupPosition))
                    {
                        continue;
                    }

                    bestSqrDistance = sqrDistance;
                    bestTarget = player;
                    bestZone = zone;
                }
            }

            return bestTarget != null && TryBeginPushAttempt(bestTarget, bestZone);
        }

        private void UpdatePushAttempt()
        {
            switch (pushPhase)
            {
                case GoatPushPhase.MovingToZone:
                    UpdatePushMovingToZone();
                    break;
                case GoatPushPhase.Positioning:
                    UpdatePushPositioning();
                    break;
                case GoatPushPhase.Attacking:
                    UpdatePushAttacking();
                    break;
                case GoatPushPhase.Recovery:
                    if (Time.time >= stateEndTime)
                    {
                        FinishPushAttempt();
                    }
                    break;
                default:
                    AbortPushAttempt();
                    break;
            }
        }

        private void UpdatePushMovingToZone()
        {
            if (!IsPushTargetValid() || !IsAgentFollowingValidPath())
            {
                AbortPushAttempt();
                return;
            }

            NavMeshAgent agent = Brain.Agent;
            if (agent.pathPending || agent.remainingDistance > config.PushApproachDistance)
            {
                return;
            }

            if (!TryRefreshPushSetupPosition())
            {
                AbortPushAttempt();
                return;
            }

            pushPhase = GoatPushPhase.Positioning;
            nextPushPositionUpdateTime = 0f;
            SetDestination(currentPushSetupPosition, config.PushPositionTolerance);
        }

        private void UpdatePushPositioning()
        {
            if (!IsPushTargetValid() || !IsAgentFollowingValidPath())
            {
                AbortPushAttempt();
                return;
            }

            if (Time.time >= nextPushPositionUpdateTime)
            {
                nextPushPositionUpdateTime = Time.time + config.PushPositionUpdateInterval;
                if (!TryRefreshPushSetupPosition())
                {
                    AbortPushAttempt();
                    return;
                }

                SetDestination(currentPushSetupPosition, config.PushPositionTolerance);
            }

            NavMeshAgent agent = Brain.Agent;
            if (agent.pathPending || agent.remainingDistance > config.PushPositionTolerance)
            {
                return;
            }

            StopAgent();
            FacePushDirection();
            if (GetPushFacingAngle() > config.PushFacingToleranceDegrees)
            {
                return;
            }

            animationController?.PlayAction();
            if (Brain.AttackController == null
                || !Brain.AttackController.StartTargetedAttack(
                    currentTarget,
                    ValidatePushAttackTarget,
                    PushAttackCompleted))
            {
                AbortPushAttempt();
                return;
            }

            pushPhase = GoatPushPhase.Attacking;
        }

        private void UpdatePushAttacking()
        {
            StopAgent();
            FacePushDirection();
            if (!IsPushTargetValid())
            {
                Brain.AttackController?.CancelPendingAttacks();
                BeginPushRecovery();
            }
        }

        private bool ValidatePushAttackTarget(PlayerHealth target)
        {
            return currentState == GoatState.PushAttempt
                && pushPhase == GoatPushPhase.Attacking
                && target != null
                && target == currentTarget
                && IsPushTargetValid();
        }

        private void PushAttackCompleted(PlayerHealth target, bool hit)
        {
            if (currentState != GoatState.PushAttempt || pushPhase != GoatPushPhase.Attacking)
            {
                return;
            }

            if (hit
                && target != null
                && target == currentTarget
                && activePushZone != null
                && activePushZone.ContainsPlayer(target))
            {
                ApplyPushImpulse(target);
            }

            BeginPushRecovery();
        }

        private void ApplyPushImpulse(PlayerHealth target)
        {
            ExternalImpulseProfileSO profile = activePushZone != null
                ? activePushZone.PushImpulseProfile
                : null;
            if (target == null || profile == null)
            {
                return;
            }

            MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IExternalImpulseReceiver receiver)
                {
                    receiver.TryApplyExternalImpulse(
                        profile.CreateImpulse(activePushZone.PushDirection),
                        Brain.NetworkObject);
                    return;
                }
            }
        }

        private void BeginPushRecovery()
        {
            Brain.AttackController?.CancelPendingAttacks();
            GoatPushTargetReservations.Release(currentTarget, this);
            StopAgent();
            pushPhase = GoatPushPhase.Recovery;
            stateEndTime = Time.time + config.PushRecoveryDuration;
        }

        private void FinishPushAttempt()
        {
            ClearPushAttemptState(true);
            EnterIdle();
        }

        private void AbortPushAttempt()
        {
            BeginPushRecovery();
        }

        private void ClearPushAttemptState(bool startCooldown)
        {
            Brain.AttackController?.CancelPendingAttacks();
            GoatPushTargetReservations.Release(currentTarget, this);
            activePushZone = null;
            pushPhase = GoatPushPhase.None;
            currentPushSetupPosition = default;
            if (startCooldown)
            {
                pushAvailableAt = Time.time + config.PushAttemptCooldown;
            }
        }

        private bool IsPushTargetValid()
        {
            return currentTarget != null
                && !currentTarget.IsDowned
                && activePushZone != null
                && activePushZone.isActiveAndEnabled
                && activePushZone.ContainsPlayer(currentTarget)
                && !GoatPushTargetReservations.IsReservedByOther(currentTarget, this);
        }

        private bool TryRefreshPushSetupPosition()
        {
            if (!IsPushTargetValid()
                || !activePushZone.TryGetPushSetupPosition(
                    currentTarget,
                    config.PushSetupDistance,
                    out Vector3 setupPosition)
                || !HasCompletePath(Brain.transform.position, setupPosition))
            {
                return false;
            }

            currentPushSetupPosition = setupPosition;
            return true;
        }

        private bool TryResolveReachablePoint(Vector3 desiredPosition, out Vector3 point)
        {
            int areaMask = Brain.Agent != null ? Brain.Agent.areaMask : NavMesh.AllAreas;
            if (NavMesh.SamplePosition(desiredPosition, out NavMeshHit hit, 1f, areaMask))
            {
                point = hit.position;
                return true;
            }

            point = default;
            return false;
        }

        private bool HasCompletePath(Vector3 start, Vector3 end)
        {
            int areaMask = Brain.Agent != null ? Brain.Agent.areaMask : NavMesh.AllAreas;
            NavMeshPath path = new NavMeshPath();
            return NavMesh.CalculatePath(start, end, areaMask, path)
                && path.status == NavMeshPathStatus.PathComplete;
        }

        private bool IsAgentFollowingValidPath()
        {
            NavMeshAgent agent = Brain.Agent;
            return agent != null
                && agent.enabled
                && agent.isOnNavMesh
                && agent.pathStatus != NavMeshPathStatus.PathInvalid;
        }

        private void FacePushDirection()
        {
            if (activePushZone == null)
            {
                return;
            }

            Vector3 direction = Vector3.ProjectOnPlane(activePushZone.PushDirection, Vector3.up);
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            float angularSpeed = Brain.Definition != null ? Brain.Definition.angularSpeed : 360f;
            float tickInterval = Brain.Definition != null ? Brain.Definition.decisionTickInterval : 0.2f;
            Brain.transform.rotation = Quaternion.RotateTowards(
                Brain.transform.rotation,
                Quaternion.LookRotation(direction),
                angularSpeed * Mathf.Max(0.05f, tickInterval));
        }

        private float GetPushFacingAngle()
        {
            if (activePushZone == null)
            {
                return 180f;
            }

            Vector3 forward = Vector3.ProjectOnPlane(Brain.transform.forward, Vector3.up);
            Vector3 pushDirection = Vector3.ProjectOnPlane(activePushZone.PushDirection, Vector3.up);
            return Vector3.Angle(forward, pushDirection);
        }

        private void TryUpdateProximityThreat()
        {
            if (currentState == GoatState.Charging || currentState == GoatState.PushAttempt || Time.time < chargeAvailableAt)
            {
                return;
            }

            if (currentTarget != null)
            {
                if (currentTarget.IsDowned
                    || Vector3.Distance(Brain.transform.position, currentTarget.transform.position) > config.ProximityThreatRange)
                {
                    currentTarget = null;
                    proximityThreatStartedAt = -1f;
                    return;
                }

                if (proximityThreatStartedAt < 0f)
                {
                    proximityThreatStartedAt = Time.time;
                }

                if (Time.time - proximityThreatStartedAt >= config.ProximityThreatDuration)
                {
                    TryBeginCharge(currentTarget);
                }
                return;
            }

            PlayerHealth nearestPlayer = FindNearestPlayer(config.ProximityThreatRange);
            if (nearestPlayer == null)
            {
                proximityThreatStartedAt = -1f;
                return;
            }

            currentTarget = nearestPlayer;
            proximityThreatStartedAt = Time.time;
        }

        private PlayerHealth FindNearestPlayer(float radius)
        {
            PlayerHealth closest = null;
            float closestSqrDistance = radius * radius;
            Collider[] hits = Physics.OverlapSphere(Brain.transform.position, radius, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                PlayerHealth player = hit.GetComponentInParent<PlayerHealth>();
                if (player == null || player.IsDowned || !IsPlayerFaction(player))
                {
                    continue;
                }

                float sqrDistance = (player.transform.position - Brain.transform.position).sqrMagnitude;
                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closest = player;
                }
            }

            return closest;
        }

        private bool IsPlayerFaction(PlayerHealth player)
        {
            NPCFactionMember member = player.GetComponent<NPCFactionMember>();
            if (config.PlayerFaction != null)
            {
                return member != null && member.Faction == config.PlayerFaction;
            }

            return member == null || member.Faction == null || member.Faction.FactionId == "Player";
        }

        private bool TrySelectWanderTarget(out Vector3 target)
        {
            target = Brain.transform.position;
            float radius = Mathf.Max(0.1f, Brain.PatrolRadius);
            int areaMask = Brain.Agent != null ? Brain.Agent.areaMask : NavMesh.AllAreas;
            for (int i = 0; i < config.WanderPointAttempts; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * radius;
                Vector3 candidate = homePosition + new Vector3(randomCircle.x, 0f, randomCircle.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, areaMask))
                {
                    target = hit.position;
                    return true;
                }
            }

            return false;
        }

        private void BrainHealth_OnDamaged(object sender, NPCHealth.DamageEventArgs e)
        {
            if (e.Attacker == null)
            {
                return;
            }

            PlayerHealth player = e.Attacker.GetComponent<PlayerHealth>();
            player ??= e.Attacker.GetComponentInChildren<PlayerHealth>();
            if (player != null)
            {
                chargeAvailableAt = 0f;
                TryBeginCharge(player);
            }
        }

        private void SetDestination(Vector3 destination, float stoppingDistance)
        {
            NavMeshAgent agent = Brain.Agent;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            agent.isStopped = false;
            agent.stoppingDistance = Mathf.Max(0f, stoppingDistance);
            agent.SetDestination(destination);
        }

        private void StopAgent()
        {
            NavMeshAgent agent = Brain.Agent;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            agent.isStopped = true;
            agent.ResetPath();
        }

        private void StopAndDisableAgent()
        {
            NavMeshAgent agent = Brain.Agent;
            if (agent == null || !agent.enabled)
            {
                return;
            }

            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            agent.enabled = false;
        }

        private void EnableAndWarpAgent(Vector3 position)
        {
            NavMeshAgent agent = Brain.Agent;
            if (agent == null)
            {
                return;
            }

            if (!agent.enabled)
            {
                agent.enabled = true;
            }

            if (agent.isOnNavMesh)
            {
                agent.Warp(position);
                agent.isStopped = false;
            }
        }

        private void RestoreAgentAtNearestNavMesh()
        {
            NavMeshAgent agent = Brain.Agent;
            if (agent == null)
            {
                return;
            }

            if (!agent.enabled)
            {
                agent.enabled = true;
            }

            if (!agent.isOnNavMesh
                && NavMesh.SamplePosition(Brain.transform.position, out NavMeshHit hit, config.MaxJumpHorizontalDistance, agent.areaMask))
            {
                Brain.transform.position = hit.position;
                agent.Warp(hit.position);
            }
        }

        private void ResetAgentPath()
        {
            NavMeshAgent agent = Brain.Agent;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            agent.ResetPath();
            agent.isStopped = false;
        }

        private void FaceTarget()
        {
            if (currentTarget == null)
            {
                return;
            }

            Vector3 direction = Vector3.ProjectOnPlane(currentTarget.transform.position - Brain.transform.position, Vector3.up);
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            float angularSpeed = Brain.Definition != null ? Brain.Definition.angularSpeed : 360f;
            float tickInterval = Brain.Definition != null ? Brain.Definition.decisionTickInterval : 0.2f;
            Brain.transform.rotation = Quaternion.RotateTowards(
                Brain.transform.rotation,
                Quaternion.LookRotation(direction),
                angularSpeed * Mathf.Max(0.05f, tickInterval));
        }
    }
}
