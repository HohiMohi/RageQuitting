using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "BeaverScoutBehavior", menuName = "Scriptable Objects/NPC/Behaviors/Beaver Scout")]
public class BeaverScoutBehaviorSO : NPCBehaviorSO
{
    public enum DeliveryMode
    {
        RemoveFromWorld,
        DropAtDestination
    }

    [SerializeField] private DeliveryMode deliveryMode = DeliveryMode.RemoveFromWorld;
    [SerializeField] private NPCInterestProfileSO interestProfile;
    [SerializeField] private NPCDestructionProfileSO destructionProfile;
    [SerializeField] private int storageWithdrawAmountThreshold = 0;
    [SerializeField] private float idleSearchDuration = 5f;
    [SerializeField] private float targetRefreshInterval = 1f;
    [SerializeField] private float patrolArrivalDistance = 0.75f;
    [SerializeField] private float deliveryDistance = 1.5f;
    [SerializeField] private float hitReactionLockDuration = 0.75f;
    [SerializeField] private NPCFactionSO playerFaction;
    [SerializeField] private float noticePlayerLockDuration = 1.25f;
    [SerializeField] private float followDurationMin = 15f;
    [SerializeField] private float followDurationMax = 30f;
    [SerializeField] private float followRefreshInterval = 0.25f;
    [SerializeField] private float followStoppingDistance = 1.5f;
    [SerializeField] private float attackPrepareDuration = 1.5f;
    [SerializeField] private float attackRecoveryDuration = 0.5f;
    [SerializeField] private float rageHealthThresholdNormalized = 0.5f;
    [SerializeField] private float rageApproachRefreshInterval = 0.25f;
    [SerializeField] private float rageApproachStoppingDistance = 1.3f;
    [SerializeField] private int storageSweepPatrolThreshold = 5;
    [SerializeField] private float storageSweepArrivalDistance = 1.4f;
    [SerializeField] private float resourceZoneSweepArrivalDistance = 1.4f;
    [SerializeField] private float resourceDestructionAttackInterval = 1f;
    [SerializeField] private float idlePatrolRangeIncrease = 2f;
    [SerializeField] private float maxPatrolRadius = 30f;

    public DeliveryMode Delivery => deliveryMode;
    public NPCInterestProfileSO InterestProfile => interestProfile;
    public NPCDestructionProfileSO DestructionProfile => destructionProfile;
    public int StorageWithdrawAmountThreshold => Mathf.Max(0, storageWithdrawAmountThreshold);
    public float IdleSearchDuration => Mathf.Max(0.1f, idleSearchDuration);
    public float TargetRefreshInterval => Mathf.Max(0.1f, targetRefreshInterval);
    public float PatrolArrivalDistance => Mathf.Max(0.1f, patrolArrivalDistance);
    public float DeliveryDistance => Mathf.Max(0.1f, deliveryDistance);
    public float HitReactionLockDuration => Mathf.Max(0f, hitReactionLockDuration);
    public NPCFactionSO PlayerFaction => playerFaction;
    public float NoticePlayerLockDuration => Mathf.Max(0f, noticePlayerLockDuration);
    public float FollowDurationMin => Mathf.Max(0.1f, Mathf.Min(followDurationMin, followDurationMax));
    public float FollowDurationMax => Mathf.Max(FollowDurationMin, followDurationMax);
    public float FollowRefreshInterval => Mathf.Max(0.05f, followRefreshInterval);
    public float FollowStoppingDistance => Mathf.Max(0.1f, followStoppingDistance);
    public float AttackPrepareDuration => Mathf.Max(0f, attackPrepareDuration);
    public float AttackRecoveryDuration => Mathf.Max(0f, attackRecoveryDuration);
    public float RageHealthThresholdNormalized => Mathf.Clamp01(rageHealthThresholdNormalized);
    public float RageApproachRefreshInterval => Mathf.Max(0.05f, rageApproachRefreshInterval);
    public float RageApproachStoppingDistance => Mathf.Max(0.1f, rageApproachStoppingDistance);
    public int StorageSweepPatrolThreshold => Mathf.Max(1, storageSweepPatrolThreshold);
    public float StorageSweepArrivalDistance => Mathf.Max(0.1f, storageSweepArrivalDistance);
    public float ResourceZoneSweepArrivalDistance => Mathf.Max(0.1f, resourceZoneSweepArrivalDistance);
    public float ResourceDestructionAttackInterval => Mathf.Max(0.05f, resourceDestructionAttackInterval);
    public float IdlePatrolRangeIncrease => Mathf.Max(0f, idlePatrolRangeIncrease);

    public float GetMaxPatrolRadius(float basePatrolRadius)
    {
        return Mathf.Max(Mathf.Max(0.1f, basePatrolRadius), maxPatrolRadius);
    }

    public override NPCBehaviorController CreateController(NPCBrain brain)
    {
        return new BeaverScoutController(brain, this);
    }

    private enum ScoutState
    {
        IdleSearching,
        NoticingPlayer,
        FollowingPlayer,
        MovingToPatrolPoint,
        MovingToTarget,
        MovingToStorage,
        MovingToDestructionTarget,
        DestroyingResource,
        Delivering,
        PreparingAttack,
        Attacking,
        AttackRecovery,
        RagePreparingAttack,
        RageAttacking,
        RageAttackRecovery,
        RageApproachingTarget,
        ReturningToBaseForStorageSweep,
        MovingToKnownSweepLocation,
        SearchingKnownResourceZone
    }

    private class BeaverScoutController : NPCBehaviorController
    {
        private enum SweepLocationKind
        {
            Storage,
            ResourceZone
        }

        private struct SweepLocationTarget
        {
            public SweepLocationKind Kind;
            public NPCStorageEncounterInfo StorageInfo;
            public NPCResourcePopulationZoneEncounterInfo ResourceZoneInfo;
        }

        private readonly BeaverScoutBehaviorSO config;
        private Vector3 homePosition;
        private Vector3 patrolTarget;
        private GameObject targetObject;
        private BaseStorageNew targetStorage;
        private BaseResourceSO targetStorageResource;
        private BaseResourceNew destructionTarget;
        private PlayerHealth followTargetPlayerHealth;
        private Transform followTargetTransform;
        private readonly HashSet<ulong> encounteredPlayerIds = new HashSet<ulong>();
        private readonly Dictionary<ulong, NPCStorageEncounterInfo> encounteredStorages = new Dictionary<ulong, NPCStorageEncounterInfo>();
        private readonly List<ulong> encounteredStorageOrder = new List<ulong>();
        private readonly Dictionary<ulong, NPCResourcePopulationZoneEncounterInfo> encounteredResourceZones =
            new Dictionary<ulong, NPCResourcePopulationZoneEncounterInfo>();
        private readonly List<ulong> encounteredResourceZoneOrder = new List<ulong>();
        private readonly List<SweepLocationTarget> sweepLocations = new List<SweepLocationTarget>();
        private ScoutState state;
        private float idleSearchEndTime;
        private float nextTargetRefreshTime;
        private float noticeEndTime;
        private float followEndTime;
        private float nextFollowRefreshTime;
        private float previousStoppingDistance;
        private bool hasPreviousStoppingDistance;
        private float reactionLockEndTime;
        private float attackPrepareEndTime;
        private float attackRecoveryEndTime;
        private float nextRageApproachRefreshTime;
        private float previousHealth;
        private NetworkObject lastAttacker;
        private NetworkObject rageTargetNetworkObject;
        private bool pendingRageAfterCommittedAttack;
        private NPCAnimationController animationController;
        private int consecutivePatrolIdleCount;
        private SweepLocationTarget currentSweepTarget;
        private Vector3 currentSweepDestination;
        private bool finishStorageSweepAfterReturningToBase;
        private float nextResourceDestructionAttackTime;
        private float currentPatrolRadius;

        public BeaverScoutController(NPCBrain brain, BeaverScoutBehaviorSO config) : base(brain)
        {
            this.config = config;
        }

        public override void Enter()
        {
            homePosition = Brain.transform.position;
            currentPatrolRadius = Brain.PatrolRadius;
            previousHealth = Brain.Health != null ? Brain.Health.CurrentHealth : 0f;
            animationController = Brain.GetComponent<NPCAnimationController>();
            if (Brain.Health != null)
            {
                Brain.Health.OnDamaged += BrainHealth_OnDamaged;
            }

            EnterIdleSearching();
        }

        public override void Exit()
        {
            if (Brain.Health != null)
            {
                Brain.Health.OnDamaged -= BrainHealth_OnDamaged;
            }

            targetObject = null;
            ClearDestructionTarget();
            lastAttacker = null;
            rageTargetNetworkObject = null;
            pendingRageAfterCommittedAttack = false;
            if (Brain.Agent != null && Brain.Agent.isOnNavMesh)
            {
                Brain.Agent.ResetPath();
                Brain.Agent.isStopped = false;
            }
        }

        public override void HandleDeferredDamage(NPCHealth.DamageEventArgs damageEvent)
        {
            BrainHealth_OnDamaged(Brain.Health, damageEvent);
        }

        public override void Tick()
        {
            if (Time.time < reactionLockEndTime)
            {
                StopAgent();
                return;
            }

            switch (state)
            {
                case ScoutState.IdleSearching:
                    UpdateIdleSearching();
                    break;
                case ScoutState.NoticingPlayer:
                    UpdateNoticingPlayer();
                    break;
                case ScoutState.FollowingPlayer:
                    UpdateFollowingPlayer();
                    break;
                case ScoutState.MovingToPatrolPoint:
                    UpdateMovingToPatrolPoint();
                    break;
                case ScoutState.MovingToTarget:
                    UpdateMovingToTarget();
                    break;
                case ScoutState.MovingToStorage:
                    UpdateMovingToStorage();
                    break;
                case ScoutState.MovingToDestructionTarget:
                    UpdateMovingToDestructionTarget();
                    break;
                case ScoutState.DestroyingResource:
                    UpdateDestroyingResource();
                    break;
                case ScoutState.Delivering:
                    UpdateDelivering();
                    break;
                case ScoutState.PreparingAttack:
                    UpdatePreparingAttack();
                    break;
                case ScoutState.Attacking:
                    UpdateAttacking();
                    break;
                case ScoutState.AttackRecovery:
                    UpdateAttackRecovery();
                    break;
                case ScoutState.RagePreparingAttack:
                    UpdateRagePreparingAttack();
                    break;
                case ScoutState.RageAttacking:
                    UpdateRageAttacking();
                    break;
                case ScoutState.RageAttackRecovery:
                    UpdateRageAttackRecovery();
                    break;
                case ScoutState.RageApproachingTarget:
                    UpdateRageApproachingTarget();
                    break;
                case ScoutState.ReturningToBaseForStorageSweep:
                    UpdateReturningToBaseForStorageSweep();
                    break;
                case ScoutState.MovingToKnownSweepLocation:
                    UpdateMovingToKnownSweepLocation();
                    break;
                case ScoutState.SearchingKnownResourceZone:
                    UpdateSearchingKnownResourceZone();
                    break;
            }
        }

        private void BrainHealth_OnDamaged(object sender, NPCHealth.DamageEventArgs e)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && Brain.IsSpawned && !Brain.IsServer)
            {
                return;
            }

            if (Brain.Health == null)
            {
                return;
            }

            if (e.CurrentHealth >= e.PreviousHealth || Brain.Health.IsDead)
            {
                previousHealth = e.CurrentHealth;
                return;
            }

            bool shouldEnterRage = ShouldEnterRage(e) && TrySetRageTarget(e.Attacker);
            previousHealth = e.CurrentHealth;
            ResetConsecutivePatrolIdleCount();

            if (IsRageActive())
            {
                return;
            }

            if (shouldEnterRage)
            {
                if (IsAttackCommitted())
                {
                    pendingRageAfterCommittedAttack = true;
                    return;
                }

                HandleDamageReaction(true);
                return;
            }

            lastAttacker = e.Attacker;
            if (IsAttackCommitted())
            {
                return;
            }

            HandleDamageReaction(false);
        }

        private void HandleDamageReaction(bool enterRage)
        {
            StopAgent();

            if (Brain.Carrier != null && Brain.Carrier.CarriedObject != null)
            {
                Brain.Carrier.DropHeldObject();
                Brain.Carrier.ClearSharedCarryMoveTarget();
            }

            if (animationController == null)
            {
                animationController = Brain.GetComponent<NPCAnimationController>();
            }

            if (animationController != null)
            {
                animationController.PlayHitReaction();
            }

            targetObject = null;
            ClearDestructionTarget();
            ClearStorageTarget();
            ClearFollowTarget();
            ClearStorageSweep();
            ResetConsecutivePatrolIdleCount();
            reactionLockEndTime = Time.time + config.HitReactionLockDuration;
            state = enterRage ? ScoutState.RagePreparingAttack : ScoutState.PreparingAttack;
            attackPrepareEndTime = reactionLockEndTime + config.AttackPrepareDuration;
        }

        private void EnterIdleSearching()
        {
            state = ScoutState.IdleSearching;
            targetObject = null;
            ClearDestructionTarget();
            ClearStorageTarget();
            ClearFollowTarget();
            RestoreAgentStoppingDistance();
            RegisterNearbyStorages();
            RegisterNearbyResourceZones();
            idleSearchEndTime = Time.time + config.IdleSearchDuration;
            nextTargetRefreshTime = 0f;
            StopAgent();
        }

        private float CurrentPatrolRadius => Mathf.Max(0.1f, currentPatrolRadius);

        private void IncreasePatrolRadius()
        {
            float basePatrolRadius = Mathf.Max(0.1f, Brain.PatrolRadius);
            float maxPatrolRadius = config.GetMaxPatrolRadius(basePatrolRadius);
            currentPatrolRadius = Mathf.Clamp(
                Mathf.Max(basePatrolRadius, currentPatrolRadius) + config.IdlePatrolRangeIncrease,
                basePatrolRadius,
                maxPatrolRadius);
        }

        private void UpdatePreparingAttack()
        {
            StopAgent();
            FaceLastAttacker();
            if (Time.time >= attackPrepareEndTime)
            {
                EnterAttacking();
            }
        }

        private void EnterAttacking()
        {
            state = ScoutState.Attacking;
            StopAgent();
            FaceLastAttacker();

            if (animationController == null)
            {
                animationController = Brain.GetComponent<NPCAnimationController>();
            }

            if (animationController != null)
            {
                animationController.PlayAction();
            }

            Brain.AttackController?.StartAttack();
            attackRecoveryEndTime = Time.time + config.AttackRecoveryDuration;
        }

        private void UpdateAttacking()
        {
            StopAgent();
            if (Time.time >= attackRecoveryEndTime)
            {
                state = ScoutState.AttackRecovery;
            }
        }

        private void UpdateAttackRecovery()
        {
            StopAgent();
            if (Time.time >= attackRecoveryEndTime)
            {
                if (pendingRageAfterCommittedAttack && IsRageTargetAvailable())
                {
                    pendingRageAfterCommittedAttack = false;
                    EnterRageApproachingOrPreparing();
                    return;
                }

                ClearRageState();
                EnterIdleSearching();
            }
        }

        private void UpdateRagePreparingAttack()
        {
            StopAgent();
            if (!IsRageTargetAvailable())
            {
                ExitRageToIdle();
                return;
            }

            FaceRageTarget();
            if (Time.time >= attackPrepareEndTime)
            {
                EnterRageAttacking();
            }
        }

        private void EnterRageAttacking()
        {
            if (!IsRageTargetAvailable())
            {
                ExitRageToIdle();
                return;
            }

            state = ScoutState.RageAttacking;
            StopAgent();
            FaceRageTarget();

            if (animationController == null)
            {
                animationController = Brain.GetComponent<NPCAnimationController>();
            }

            if (animationController != null)
            {
                animationController.PlayAction();
            }

            Brain.AttackController?.StartAttack();
            attackRecoveryEndTime = Time.time + config.AttackRecoveryDuration;
        }

        private void UpdateRageAttacking()
        {
            StopAgent();
            if (Time.time >= attackRecoveryEndTime)
            {
                state = ScoutState.RageAttackRecovery;
            }
        }

        private void UpdateRageAttackRecovery()
        {
            StopAgent();
            if (!IsRageTargetAvailable())
            {
                ExitRageToIdle();
                return;
            }

            FaceRageTarget();
            if (Time.time < attackRecoveryEndTime)
            {
                return;
            }

            EnterRageApproachingOrPreparing();
        }

        private void EnterRageApproachingOrPreparing()
        {
            if (!IsRageTargetAvailable())
            {
                ExitRageToIdle();
                return;
            }

            if (IsRageTargetInAttackRange())
            {
                EnterRagePreparingAttack();
                return;
            }

            EnterRageApproachingTarget();
        }

        private void EnterRagePreparingAttack()
        {
            state = ScoutState.RagePreparingAttack;
            RestoreAgentStoppingDistance();
            StopAgent();
            FaceRageTarget();
            attackPrepareEndTime = Time.time + config.AttackPrepareDuration;
        }

        private void EnterRageApproachingTarget()
        {
            state = ScoutState.RageApproachingTarget;
            nextRageApproachRefreshTime = 0f;
            ResumeAgent();
            SetRageApproachStoppingDistance();
            UpdateRageApproachDestination();
        }

        private void UpdateRageApproachingTarget()
        {
            if (!IsRageTargetAvailable())
            {
                ExitRageToIdle();
                return;
            }

            if (IsRageTargetInAttackRange())
            {
                EnterRagePreparingAttack();
                return;
            }

            if (Time.time < nextRageApproachRefreshTime)
            {
                return;
            }

            UpdateRageApproachDestination();
        }

        private void UpdateIdleSearching()
        {
            if (Time.time >= nextTargetRefreshTime && TryFindAndEnterIdleTarget())
            {
                return;
            }

            if (Time.time >= idleSearchEndTime)
            {
                consecutivePatrolIdleCount++;
                IncreasePatrolRadius();
                if (consecutivePatrolIdleCount >= config.StorageSweepPatrolThreshold)
                {
                    EnterReturningToBaseForStorageSweep();
                    return;
                }

                EnterMovingToPatrolPoint();
            }
        }

        private bool TryFindAndEnterIdleTarget()
        {
            nextTargetRefreshTime = Time.time + config.TargetRefreshInterval;
            if (TryFindClosestUnencounteredPlayer(
                out PlayerHealth playerHealth,
                out Transform playerTransform,
                out ulong playerId))
            {
                ResetConsecutivePatrolIdleCount();
                ClearStorageSweep();
                EnterNoticingPlayer(playerHealth, playerTransform, playerId);
                return true;
            }

            ClearStorageTarget();
            targetObject = null;
            ClearDestructionTarget();
            if (!HasAnyAvailablePlayerInDetectionRange()
                && TryFindClosestStorageWithInterestedResource(out BaseStorageNew storage, out BaseResourceSO resourceSO))
            {
                targetStorage = storage;
                targetStorageResource = resourceSO;
            }
            else
            {
                targetObject = FindClosestStealableObject();
                if (targetObject == null)
                {
                    destructionTarget = FindClosestDestructibleResource();
                }
            }

            if (targetStorage != null && targetStorageResource != null)
            {
                ResetConsecutivePatrolIdleCount();
                ClearStorageSweep();
                EnterMovingToStorage();
                return true;
            }

            if (targetObject != null)
            {
                ResetConsecutivePatrolIdleCount();
                ClearStorageSweep();
                EnterMovingToTarget();
                return true;
            }

            if (destructionTarget != null)
            {
                ResetConsecutivePatrolIdleCount();
                ClearStorageSweep();
                EnterMovingToDestructionTarget();
                return true;
            }

            return false;
        }

        private void EnterNoticingPlayer(PlayerHealth playerHealth, Transform playerTransform, ulong playerId)
        {
            StopAgent();
            RestoreAgentStoppingDistance();
            ResetConsecutivePatrolIdleCount();
            targetObject = null;
            followTargetPlayerHealth = playerHealth;
            followTargetTransform = playerTransform;
            encounteredPlayerIds.Add(playerId);
            noticeEndTime = Time.time + config.NoticePlayerLockDuration;
            state = ScoutState.NoticingPlayer;

            if (animationController == null)
            {
                animationController = Brain.GetComponent<NPCAnimationController>();
            }

            if (animationController != null)
            {
                animationController.PlayNotice();
            }
        }

        private void UpdateNoticingPlayer()
        {
            StopAgent();
            if (!IsFollowTargetAvailable())
            {
                EnterIdleSearching();
                return;
            }

            if (Time.time >= noticeEndTime)
            {
                EnterFollowingPlayer();
            }
        }

        private void EnterFollowingPlayer()
        {
            if (!IsFollowTargetAvailable())
            {
                EnterIdleSearching();
                return;
            }

            state = ScoutState.FollowingPlayer;
            ResetConsecutivePatrolIdleCount();
            followEndTime = Time.time + Random.Range(config.FollowDurationMin, config.FollowDurationMax);
            nextFollowRefreshTime = 0f;
            ResumeAgent();
            SetFollowStoppingDistance();
            Brain.Agent.SetDestination(followTargetTransform.position);
        }

        private void UpdateFollowingPlayer()
        {
            if (Time.time >= followEndTime || !IsFollowTargetAvailable())
            {
                EnterIdleSearching();
                return;
            }

            if (Time.time < nextFollowRefreshTime)
            {
                return;
            }

            nextFollowRefreshTime = Time.time + config.FollowRefreshInterval;
            ResumeAgent();
            SetFollowStoppingDistance();
            Brain.Agent.SetDestination(followTargetTransform.position);
        }

        private void EnterMovingToPatrolPoint()
        {
            RestoreAgentStoppingDistance();
            ResumeAgent();
            if (!TrySelectPatrolTarget(out patrolTarget))
            {
                EnterIdleSearching();
                return;
            }

            state = ScoutState.MovingToPatrolPoint;
            Brain.Agent.SetDestination(patrolTarget);
        }

        private void UpdateMovingToPatrolPoint()
        {
            if (HasArrived(config.PatrolArrivalDistance))
            {
                EnterIdleSearching();
            }
        }

        private void EnterMovingToTarget()
        {
            RestoreAgentStoppingDistance();
            if (!IsStealable(targetObject))
            {
                EnterIdleSearching();
                return;
            }

            state = ScoutState.MovingToTarget;
            ResetConsecutivePatrolIdleCount();
            ResumeAgent();
            Brain.Agent.SetDestination(targetObject.transform.position);
        }

        private void UpdateMovingToTarget()
        {
            if (!IsStealable(targetObject))
            {
                EnterIdleSearching();
                return;
            }

            Brain.Agent.SetDestination(targetObject.transform.position);
            if (Vector3.Distance(Brain.transform.position, targetObject.transform.position) > Brain.InteractionDistance)
            {
                return;
            }

            if (Brain.Carrier.TryPickup(targetObject))
            {
                targetObject = null;
                Brain.Carrier.SetSharedCarryMoveTarget(homePosition);
                EnterDelivering();
                return;
            }

            EnterIdleSearching();
        }

        private void EnterMovingToStorage()
        {
            RestoreAgentStoppingDistance();
            if (!CanWithdrawFromTargetStorage())
            {
                EnterIdleSearching();
                return;
            }

            state = ScoutState.MovingToStorage;
            ResetConsecutivePatrolIdleCount();
            ResumeAgent();
            Brain.Agent.SetDestination(targetStorage.transform.position);
        }

        private void EnterMovingToDestructionTarget()
        {
            RestoreAgentStoppingDistance();
            if (!IsDestructionTargetValid(destructionTarget))
            {
                EnterIdleSearching();
                return;
            }

            state = ScoutState.MovingToDestructionTarget;
            ResumeAgent();
            Brain.Agent.SetDestination(destructionTarget.transform.position);
        }

        private void UpdateMovingToDestructionTarget()
        {
            if (!IsDestructionTargetValid(destructionTarget))
            {
                EnterIdleSearching();
                return;
            }

            Brain.Agent.SetDestination(destructionTarget.transform.position);
            if (!IsDestructionTargetInAttackRange())
            {
                return;
            }

            state = ScoutState.DestroyingResource;
            StopAgent();
            nextResourceDestructionAttackTime = 0f;
        }

        private void UpdateDestroyingResource()
        {
            if (!IsDestructionTargetValid(destructionTarget))
            {
                EnterIdleSearching();
                return;
            }

            if (!IsDestructionTargetInAttackRange())
            {
                state = ScoutState.MovingToDestructionTarget;
                ResumeAgent();
                return;
            }

            StopAgent();
            FaceDestructionTarget();
            if (Time.time < nextResourceDestructionAttackTime)
            {
                return;
            }

            if (animationController != null)
            {
                animationController.PlayAction();
            }

            if (!TryStartResourceDestructionAttack())
            {
                EnterIdleSearching();
                return;
            }

            nextResourceDestructionAttackTime = Time.time + config.ResourceDestructionAttackInterval;
        }

        private void UpdateMovingToStorage()
        {
            if (!CanWithdrawFromTargetStorage())
            {
                EnterIdleSearching();
                return;
            }

            Brain.Agent.SetDestination(targetStorage.transform.position);
            if (!Brain.StorageInteractor.HasStorageTargetInRange(targetStorage))
            {
                return;
            }

            if (Brain.StorageInteractor.TryWithdrawAndCarry(targetStorage, targetStorageResource, out _))
            {
                ClearStorageTarget();
                Brain.Carrier.SetSharedCarryMoveTarget(homePosition);
                EnterDelivering();
                return;
            }

            EnterIdleSearching();
        }

        private void EnterDelivering()
        {
            RestoreAgentStoppingDistance();
            if (Brain.Carrier.CarriedObject == null)
            {
                EnterIdleSearching();
                return;
            }

            state = ScoutState.Delivering;
            ResetConsecutivePatrolIdleCount();
            ResumeAgent();
            Brain.Carrier.SetSharedCarryMoveTarget(homePosition);
            Brain.Agent.SetDestination(homePosition);
        }

        private void EnterReturningToBaseForStorageSweep(bool finishAfterArrival = false)
        {
            RestoreAgentStoppingDistance();
            ClearStorageTarget();
            ClearFollowTarget();
            targetObject = null;
            ResetConsecutivePatrolIdleCount();
            finishStorageSweepAfterReturningToBase = finishAfterArrival;

            state = ScoutState.ReturningToBaseForStorageSweep;
            ResumeAgent();
            Brain.Agent.SetDestination(homePosition);
        }

        private void UpdateReturningToBaseForStorageSweep()
        {
            Brain.Agent.SetDestination(homePosition);
            if (Vector3.Distance(Brain.transform.position, homePosition) > config.StorageSweepArrivalDistance)
            {
                return;
            }

            if (finishStorageSweepAfterReturningToBase)
            {
                finishStorageSweepAfterReturningToBase = false;
                ClearStorageSweep();
                EnterIdleSearching();
                return;
            }

            SynchronizeExplorationMemoryWithSpawner();
            BuildSweepLocations();
            EnterNextKnownLocationOrFinishSweep();
        }

        private void EnterNextKnownLocationOrFinishSweep()
        {
            RestoreAgentStoppingDistance();
            int closestIndex = -1;
            float closestPathDistance = float.MaxValue;
            Vector3 closestDestination = default;
            for (int i = 0; i < sweepLocations.Count; i++)
            {
                if (!TryResolveSweepDestination(sweepLocations[i], out Vector3 destination)
                    || !TryCalculatePathDistance(destination, out float pathDistance)
                    || pathDistance >= closestPathDistance)
                {
                    continue;
                }

                closestIndex = i;
                closestPathDistance = pathDistance;
                closestDestination = destination;
            }

            if (closestIndex >= 0)
            {
                currentSweepTarget = sweepLocations[closestIndex];
                sweepLocations.RemoveAt(closestIndex);
                currentSweepDestination = closestDestination;
                state = ScoutState.MovingToKnownSweepLocation;
                ResumeAgent();
                Brain.Agent.SetDestination(currentSweepDestination);
                return;
            }

            ClearStorageSweep();
            if (Vector3.Distance(Brain.transform.position, homePosition) <= config.StorageSweepArrivalDistance)
            {
                EnterIdleSearching();
                return;
            }

            EnterReturningToBaseForStorageSweep(true);
        }

        private void UpdateMovingToKnownSweepLocation()
        {
            if (!TryRefreshCurrentSweepDestination())
            {
                EnterNextKnownLocationOrFinishSweep();
                return;
            }

            Brain.Agent.SetDestination(currentSweepDestination);
            float arrivalDistance = currentSweepTarget.Kind == SweepLocationKind.Storage
                ? config.StorageSweepArrivalDistance
                : config.ResourceZoneSweepArrivalDistance;
            if (Vector3.Distance(Brain.transform.position, currentSweepDestination) > arrivalDistance)
            {
                return;
            }

            if (currentSweepTarget.Kind == SweepLocationKind.Storage)
            {
                if (TryHandleKnownStorageSweepTarget())
                {
                    return;
                }

                EnterNextKnownLocationOrFinishSweep();
                return;
            }

            EnterSearchingKnownResourceZone();
        }

        private void EnterSearchingKnownResourceZone()
        {
            state = ScoutState.SearchingKnownResourceZone;
            RegisterNearbyStorages();
            RegisterNearbyResourceZones();
            idleSearchEndTime = Time.time + config.IdleSearchDuration;
            nextTargetRefreshTime = 0f;
            StopAgent();
        }

        private void UpdateSearchingKnownResourceZone()
        {
            ResourcePopulationZone zone = currentSweepTarget.ResourceZoneInfo.Zone;
            if (zone == null || !zone.isActiveAndEnabled)
            {
                EnterNextKnownLocationOrFinishSweep();
                return;
            }

            if (Time.time >= nextTargetRefreshTime && TryFindAndEnterIdleTarget())
            {
                return;
            }

            if (Time.time >= idleSearchEndTime)
            {
                EnterNextKnownLocationOrFinishSweep();
            }
        }

        private void UpdateDelivering()
        {
            if (Brain.Carrier.CarriedObject == null)
            {
                Brain.Carrier.ClearSharedCarryMoveTarget();
                EnterIdleSearching();
                return;
            }

            Brain.Carrier.SetSharedCarryMoveTarget(homePosition);
            Brain.Agent.SetDestination(homePosition);
            if (!Brain.Carrier.HasCarriedObjectReached(homePosition, config.DeliveryDistance))
            {
                return;
            }

            DeliverCarriedObject();
            SynchronizeExplorationMemoryWithSpawner();
            Brain.Carrier.ClearSharedCarryMoveTarget();
            EnterIdleSearching();
        }

        private void DeliverCarriedObject()
        {
            GameObject carriedObject = Brain.Carrier.CarriedObject;
            if (carriedObject == null)
            {
                return;
            }

            if (config.Delivery == DeliveryMode.RemoveFromWorld)
            {
                if (carriedObject.TryGetComponent(out BaseResourceNew baseResource))
                {
                    baseResource.RemoveFromWorld();
                }
                else if (carriedObject.TryGetComponent(out MountableBridgeComponent bridgeComponent))
                {
                    bridgeComponent.RemoveFromWorld();
                }
                else
                {
                    Object.Destroy(carriedObject);
                }

                Brain.Carrier.ForceRelease(carriedObject);
                return;
            }

            Brain.Carrier.DropHeldObject();
        }

        private void BuildSweepLocations()
        {
            sweepLocations.Clear();

            foreach (ulong storageId in encounteredStorageOrder)
            {
                if (!encounteredStorages.TryGetValue(storageId, out NPCStorageEncounterInfo storageInfo))
                {
                    continue;
                }

                sweepLocations.Add(new SweepLocationTarget
                {
                    Kind = SweepLocationKind.Storage,
                    StorageInfo = storageInfo
                });
            }

            foreach (ulong zoneId in encounteredResourceZoneOrder)
            {
                if (!encounteredResourceZones.TryGetValue(
                    zoneId,
                    out NPCResourcePopulationZoneEncounterInfo zoneInfo)
                    || zoneInfo.Zone == null
                    || !zoneInfo.Zone.isActiveAndEnabled)
                {
                    continue;
                }

                sweepLocations.Add(new SweepLocationTarget
                {
                    Kind = SweepLocationKind.ResourceZone,
                    ResourceZoneInfo = zoneInfo
                });
            }
        }

        private bool TryHandleKnownStorageSweepTarget()
        {
            if (HasAnyAvailablePlayerInDetectionRange())
            {
                return false;
            }

            BaseStorageNew storage = ResolveKnownStorage(currentSweepTarget.StorageInfo);
            if (storage == null)
            {
                return false;
            }

            if (!TrySelectInterestedResourceFromStorage(storage, out BaseResourceSO resourceSO))
            {
                return false;
            }

            if (Brain.StorageInteractor.TryWithdrawAndCarry(storage, resourceSO, out _))
            {
                ClearStorageSweep();
                Brain.Carrier.SetSharedCarryMoveTarget(homePosition);
                EnterDelivering();
                return true;
            }

            return false;
        }

        private BaseStorageNew ResolveKnownStorage(NPCStorageEncounterInfo storageInfo)
        {
            if (storageInfo.Storage != null)
            {
                return storageInfo.Storage;
            }

            Collider[] colliders = Physics.OverlapSphere(storageInfo.FirstEncounterPosition, config.StorageSweepArrivalDistance + 0.5f);
            foreach (Collider collider in colliders)
            {
                GameObject candidate = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                BaseStorageNew storage = candidate.GetComponent<BaseStorageNew>();
                storage ??= candidate.GetComponentInParent<BaseStorageNew>();
                if (storage != null && GetStorageId(storage) == storageInfo.StorageId)
                {
                    return storage;
                }
            }

            return null;
        }

        private bool TryResolveSweepDestination(SweepLocationTarget target, out Vector3 destination)
        {
            destination = default;
            if (target.Kind == SweepLocationKind.Storage)
            {
                BaseStorageNew storage = ResolveKnownStorage(target.StorageInfo);
                destination = storage != null ? storage.transform.position : default;
                return storage != null && storage.gameObject.activeInHierarchy;
            }

            ResourcePopulationZone zone = target.ResourceZoneInfo.Zone;
            return zone != null
                && zone.isActiveAndEnabled
                && zone.TryGetNpcVisitPosition(Brain.Agent, out destination);
        }

        private bool TryRefreshCurrentSweepDestination()
        {
            if (currentSweepTarget.Kind == SweepLocationKind.ResourceZone)
            {
                ResourcePopulationZone zone = currentSweepTarget.ResourceZoneInfo.Zone;
                return zone != null && zone.isActiveAndEnabled;
            }

            BaseStorageNew storage = ResolveKnownStorage(currentSweepTarget.StorageInfo);
            if (storage == null || !storage.gameObject.activeInHierarchy)
            {
                return false;
            }

            currentSweepDestination = storage.transform.position;
            return true;
        }

        private bool TryCalculatePathDistance(Vector3 destination, out float pathDistance)
        {
            pathDistance = 0f;
            if (Brain.Agent == null || !Brain.Agent.isOnNavMesh)
            {
                return false;
            }

            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(Brain.transform.position, destination, Brain.Agent.areaMask, path)
                || path.status != NavMeshPathStatus.PathComplete
                || path.corners.Length == 0)
            {
                return false;
            }

            Vector3 previous = Brain.transform.position;
            for (int i = 0; i < path.corners.Length; i++)
            {
                pathDistance += Vector3.Distance(previous, path.corners[i]);
                previous = path.corners[i];
            }

            return true;
        }

        private bool TrySelectPatrolTarget(out Vector3 selectedPatrolTarget)
        {
            selectedPatrolTarget = Brain.transform.position;
            for (int i = 0; i < 8; i++)
            {
                Vector3 randomDirection = Random.insideUnitSphere * CurrentPatrolRadius + homePosition;
                if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, CurrentPatrolRadius, NavMesh.AllAreas))
                {
                    selectedPatrolTarget = hit.position;
                    return true;
                }
            }

            return false;
        }

        private bool HasArrived(float arrivalDistance)
        {
            if (Brain.Agent.pathPending)
            {
                return false;
            }

            if (Brain.Agent.remainingDistance <= arrivalDistance)
            {
                return true;
            }

            return !Brain.Agent.hasPath && Brain.Agent.velocity.sqrMagnitude <= 0.01f;
        }

        private void StopAgent()
        {
            if (Brain.Agent == null || !Brain.Agent.isOnNavMesh)
            {
                return;
            }

            Brain.Agent.isStopped = true;
            Brain.Agent.ResetPath();
            Brain.Agent.velocity = Vector3.zero;
        }

        private void ResumeAgent()
        {
            if (Brain.Agent == null || !Brain.Agent.isOnNavMesh)
            {
                return;
            }

            Brain.Agent.isStopped = false;
        }

        private void SetFollowStoppingDistance()
        {
            if (Brain.Agent == null)
            {
                return;
            }

            if (!hasPreviousStoppingDistance)
            {
                previousStoppingDistance = Brain.Agent.stoppingDistance;
                hasPreviousStoppingDistance = true;
            }

            Brain.Agent.stoppingDistance = config.FollowStoppingDistance;
        }

        private void RestoreAgentStoppingDistance()
        {
            if (Brain.Agent == null || !hasPreviousStoppingDistance)
            {
                return;
            }

            Brain.Agent.stoppingDistance = previousStoppingDistance;
            previousStoppingDistance = 0f;
            hasPreviousStoppingDistance = false;
        }

        private void SetRageApproachStoppingDistance()
        {
            if (Brain.Agent == null)
            {
                return;
            }

            if (!hasPreviousStoppingDistance)
            {
                previousStoppingDistance = Brain.Agent.stoppingDistance;
                hasPreviousStoppingDistance = true;
            }

            Brain.Agent.stoppingDistance = config.RageApproachStoppingDistance;
        }

        private GameObject FindClosestStealableObject()
        {
            Collider[] colliders = Physics.OverlapSphere(Brain.transform.position, Brain.DetectionRadius);
            GameObject closestObject = null;
            float closestDistance = float.MaxValue;

            foreach (Collider collider in colliders)
            {
                GameObject candidate = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                if (!TryGetStealableRoot(candidate, out GameObject stealableRoot))
                {
                    continue;
                }

                float distance = Vector3.Distance(Brain.transform.position, stealableRoot.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestObject = stealableRoot;
                }
            }

            return closestObject;
        }

        private BaseResourceNew FindClosestDestructibleResource()
        {
            Collider[] colliders = Physics.OverlapSphere(Brain.transform.position, Brain.DetectionRadius);
            BaseResourceNew closestResource = null;
            float closestDistance = float.MaxValue;
            HashSet<BaseResourceNew> checkedResources = new HashSet<BaseResourceNew>();

            foreach (Collider collider in colliders)
            {
                BaseResourceNew resource = collider.GetComponent<BaseResourceNew>();
                resource ??= collider.GetComponentInParent<BaseResourceNew>();
                if (resource == null || !checkedResources.Add(resource) || !IsDestructionTargetValid(resource))
                {
                    continue;
                }

                float distance = Vector3.Distance(Brain.transform.position, resource.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestResource = resource;
                }
            }

            return closestResource;
        }

        private void RegisterNearbyStorages()
        {
            Collider[] colliders = Physics.OverlapSphere(Brain.transform.position, Brain.DetectionRadius);
            foreach (Collider collider in colliders)
            {
                GameObject candidate = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                BaseStorageNew storage = candidate.GetComponent<BaseStorageNew>();
                storage ??= candidate.GetComponentInParent<BaseStorageNew>();
                if (storage == null)
                {
                    continue;
                }

                ulong storageId = GetStorageId(storage);
                if (encounteredStorages.ContainsKey(storageId))
                {
                    continue;
                }

                RegisterEncounteredStorage(new NPCStorageEncounterInfo(storageId, storage.transform.position, storage));
            }
        }

        private void RegisterNearbyResourceZones()
        {
            foreach (ResourcePopulationZone zone in ResourcePopulationZone.ActiveZones)
            {
                if (zone == null || !zone.isActiveAndEnabled)
                {
                    continue;
                }

                Vector3 closestPoint = zone.GetClosestPoint(Brain.transform.position);
                if ((closestPoint - Brain.transform.position).sqrMagnitude
                    > Brain.DetectionRadius * Brain.DetectionRadius)
                {
                    continue;
                }

                ulong zoneId = GetResourceZoneId(zone);
                if (encounteredResourceZones.ContainsKey(zoneId))
                {
                    continue;
                }

                RegisterEncounteredResourceZone(new NPCResourcePopulationZoneEncounterInfo(
                    zoneId,
                    closestPoint,
                    zone));
            }
        }

        private void SynchronizeExplorationMemoryWithSpawner()
        {
            BeaverSpawnerStorageMemory storageMemory = Brain.OriginSpawner != null
                ? Brain.OriginSpawner.GetComponent<BeaverSpawnerStorageMemory>()
                : null;
            if (storageMemory == null)
            {
                return;
            }

            storageMemory.RegisterStorages(GetEncounteredStorageInfos());
            IReadOnlyList<NPCStorageEncounterInfo> sharedStorages = storageMemory.GetKnownStoragesSnapshot();
            for (int i = 0; i < sharedStorages.Count; i++)
            {
                RegisterEncounteredStorage(sharedStorages[i]);
            }

            storageMemory.RegisterResourceZones(GetEncounteredResourceZoneInfos());
            IReadOnlyList<NPCResourcePopulationZoneEncounterInfo> sharedZones =
                storageMemory.GetKnownResourceZonesSnapshot();
            for (int i = 0; i < sharedZones.Count; i++)
            {
                RegisterEncounteredResourceZone(sharedZones[i]);
            }
        }

        private IEnumerable<NPCStorageEncounterInfo> GetEncounteredStorageInfos()
        {
            foreach (ulong storageId in encounteredStorageOrder)
            {
                if (encounteredStorages.TryGetValue(storageId, out NPCStorageEncounterInfo storageInfo))
                {
                    yield return storageInfo;
                }
            }
        }

        private void RegisterEncounteredStorage(NPCStorageEncounterInfo storageInfo)
        {
            if (storageInfo.StorageId == 0 || encounteredStorages.ContainsKey(storageInfo.StorageId))
            {
                return;
            }

            encounteredStorages.Add(storageInfo.StorageId, storageInfo);
            encounteredStorageOrder.Add(storageInfo.StorageId);
        }

        private IEnumerable<NPCResourcePopulationZoneEncounterInfo> GetEncounteredResourceZoneInfos()
        {
            foreach (ulong zoneId in encounteredResourceZoneOrder)
            {
                if (encounteredResourceZones.TryGetValue(
                    zoneId,
                    out NPCResourcePopulationZoneEncounterInfo zoneInfo))
                {
                    yield return zoneInfo;
                }
            }
        }

        private void RegisterEncounteredResourceZone(NPCResourcePopulationZoneEncounterInfo zoneInfo)
        {
            if (zoneInfo.ZoneId == 0 || encounteredResourceZones.ContainsKey(zoneInfo.ZoneId))
            {
                return;
            }

            encounteredResourceZones.Add(zoneInfo.ZoneId, zoneInfo);
            encounteredResourceZoneOrder.Add(zoneInfo.ZoneId);
        }

        private static ulong GetStorageId(BaseStorageNew storage)
        {
            if (storage != null && storage.NetworkObject != null && storage.NetworkObject.IsSpawned)
            {
                return storage.NetworkObject.NetworkObjectId;
            }

            return storage != null ? unchecked((ulong)storage.GetInstanceID()) : 0;
        }

        private static ulong GetResourceZoneId(ResourcePopulationZone zone)
        {
            return zone != null ? unchecked((ulong)zone.GetInstanceID()) : 0;
        }

        private bool TryFindClosestStorageWithInterestedResource(out BaseStorageNew selectedStorage, out BaseResourceSO selectedResource)
        {
            selectedStorage = null;
            selectedResource = null;

            if (Brain.StorageInteractor == null)
            {
                return false;
            }

            Collider[] colliders = Physics.OverlapSphere(Brain.transform.position, Brain.DetectionRadius);
            HashSet<BaseStorageNew> checkedStorages = new HashSet<BaseStorageNew>();
            float closestDistance = float.MaxValue;

            foreach (Collider collider in colliders)
            {
                GameObject candidate = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                BaseStorageNew storage = candidate.GetComponent<BaseStorageNew>();
                storage ??= candidate.GetComponentInParent<BaseStorageNew>();
                if (storage == null || !checkedStorages.Add(storage))
                {
                    continue;
                }

                if (!TrySelectInterestedResourceFromStorage(storage, out BaseResourceSO resourceSO))
                {
                    continue;
                }

                float distance = Vector3.Distance(Brain.transform.position, storage.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    selectedStorage = storage;
                    selectedResource = resourceSO;
                }
            }

            return selectedStorage != null && selectedResource != null;
        }

        private bool TrySelectInterestedResourceFromStorage(BaseStorageNew storage, out BaseResourceSO selectedResource)
        {
            selectedResource = null;
            if (storage == null || Brain.StorageInteractor == null)
            {
                return false;
            }

            IReadOnlyList<BaseResourceSO> orderedResources = GetStorageResourceSearchOrder(storage);
            if (orderedResources == null)
            {
                return false;
            }

            foreach (BaseResourceSO resourceSO in orderedResources)
            {
                if (resourceSO == null)
                {
                    continue;
                }

                if (config.InterestProfile != null && !config.InterestProfile.IsInterestedIn(resourceSO))
                {
                    continue;
                }

                if (CanWithdrawResourceAboveThreshold(storage, resourceSO))
                {
                    selectedResource = resourceSO;
                    return true;
                }
            }

            return false;
        }

        private IReadOnlyList<BaseResourceSO> GetStorageResourceSearchOrder(BaseStorageNew storage)
        {
            if (config.InterestProfile != null && !config.InterestProfile.AllowsAnyBaseResource)
            {
                return config.InterestProfile.AllowedBaseResources;
            }

            return storage != null ? storage.StorableBaseResources : null;
        }

        private bool TryFindClosestUnencounteredPlayer(out PlayerHealth playerHealth, out Transform playerTransform, out ulong playerId)
        {
            playerHealth = null;
            playerTransform = null;
            playerId = 0;

            Collider[] colliders = Physics.OverlapSphere(Brain.transform.position, Brain.DetectionRadius);
            float closestDistance = float.MaxValue;

            foreach (Collider collider in colliders)
            {
                GameObject candidate = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                if (!TryGetPlayerTarget(candidate, out PlayerHealth candidateHealth, out Transform candidateTransform, out ulong candidateId))
                {
                    continue;
                }

                if (encounteredPlayerIds.Contains(candidateId))
                {
                    continue;
                }

                float distance = Vector3.Distance(Brain.transform.position, candidateTransform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    playerHealth = candidateHealth;
                    playerTransform = candidateTransform;
                    playerId = candidateId;
                }
            }

            return playerHealth != null && playerTransform != null;
        }

        private bool HasAnyAvailablePlayerInDetectionRange()
        {
            Collider[] colliders = Physics.OverlapSphere(Brain.transform.position, Brain.DetectionRadius);
            foreach (Collider collider in colliders)
            {
                GameObject candidate = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                if (TryGetPlayerTarget(candidate, out _, out _, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetPlayerTarget(GameObject candidate, out PlayerHealth playerHealth, out Transform playerTransform, out ulong playerId)
        {
            playerHealth = null;
            playerTransform = null;
            playerId = 0;

            if (candidate == null || candidate.transform.root == Brain.transform.root)
            {
                return false;
            }

            NPCFactionMember factionMember = candidate.GetComponent<NPCFactionMember>();
            factionMember ??= candidate.GetComponentInParent<NPCFactionMember>();
            if (!IsPlayerFaction(factionMember))
            {
                return false;
            }

            playerHealth = candidate.GetComponent<PlayerHealth>();
            playerHealth ??= candidate.GetComponentInParent<PlayerHealth>();
            if (playerHealth == null || playerHealth.IsDowned)
            {
                return false;
            }

            if (factionMember != null && !factionMember.IsTargetAvailable)
            {
                return false;
            }

            playerTransform = factionMember != null ? factionMember.TargetTransform : playerHealth.transform;
            if (playerTransform == null)
            {
                return false;
            }

            if (playerHealth.NetworkObject != null && playerHealth.NetworkObject.IsSpawned)
            {
                playerId = playerHealth.NetworkObject.NetworkObjectId;
            }
            else
            {
                playerId = (ulong)playerHealth.GetInstanceID();
            }

            return true;
        }

        private bool IsPlayerFaction(NPCFactionMember factionMember)
        {
            if (factionMember == null)
            {
                return config.PlayerFaction == null;
            }

            if (config.PlayerFaction != null)
            {
                return factionMember.Faction == config.PlayerFaction;
            }

            return factionMember.Faction != null && factionMember.Faction.FactionId == "Player";
        }

        private bool IsFollowTargetAvailable()
        {
            if (followTargetPlayerHealth == null || followTargetTransform == null)
            {
                return false;
            }

            if (followTargetPlayerHealth.IsDowned)
            {
                return false;
            }

            return followTargetPlayerHealth.gameObject.activeInHierarchy;
        }

        private void ClearFollowTarget()
        {
            followTargetPlayerHealth = null;
            followTargetTransform = null;
        }

        private void ClearStorageTarget()
        {
            targetStorage = null;
            targetStorageResource = null;
        }

        private void ClearStorageSweep()
        {
            sweepLocations.Clear();
            currentSweepTarget = default;
            currentSweepDestination = default;
            finishStorageSweepAfterReturningToBase = false;
        }

        private void ResetConsecutivePatrolIdleCount()
        {
            consecutivePatrolIdleCount = 0;
        }

        private bool CanWithdrawFromTargetStorage()
        {
            return targetStorage != null
                && targetStorageResource != null
                && Brain.StorageInteractor != null
                && CanWithdrawResourceAboveThreshold(targetStorage, targetStorageResource);
        }

        private bool CanWithdrawResourceAboveThreshold(BaseStorageNew storage, BaseResourceSO resourceSO)
        {
            if (storage == null || resourceSO == null || Brain.StorageInteractor == null)
            {
                return false;
            }

            return storage.CheckBaseResourceAmount(resourceSO) > config.StorageWithdrawAmountThreshold
                && Brain.StorageInteractor.CanWithdraw(storage, resourceSO);
        }

        private bool IsAttackCommitted()
        {
            return state == ScoutState.PreparingAttack || state == ScoutState.Attacking || state == ScoutState.AttackRecovery;
        }

        private bool IsRageActive()
        {
            return state == ScoutState.RagePreparingAttack
                || state == ScoutState.RageAttacking
                || state == ScoutState.RageAttackRecovery
                || state == ScoutState.RageApproachingTarget;
        }

        private bool ShouldEnterRage(NPCHealth.DamageEventArgs damageEventArgs)
        {
            if (Brain.Health == null || Brain.Health.MaxHealth <= 0f || damageEventArgs.Attacker == null)
            {
                return false;
            }

            float threshold = config.RageHealthThresholdNormalized;
            float previousNormalized = damageEventArgs.PreviousHealth / Brain.Health.MaxHealth;
            float currentNormalized = damageEventArgs.CurrentHealth / Brain.Health.MaxHealth;
            bool crossedThreshold = previousNormalized >= threshold && currentNormalized < threshold;
            bool canRestartRageBelowThreshold = currentNormalized < threshold && !IsRageActive() && !IsRageTargetAvailable();
            return crossedThreshold || canRestartRageBelowThreshold;
        }

        private bool TrySetRageTarget(NetworkObject targetNetworkObject)
        {
            if (!IsValidRageTarget(targetNetworkObject))
            {
                return false;
            }

            rageTargetNetworkObject = targetNetworkObject;
            lastAttacker = targetNetworkObject;
            return true;
        }

        private bool IsRageTargetAvailable()
        {
            return IsValidRageTarget(rageTargetNetworkObject);
        }

        private bool IsValidRageTarget(NetworkObject targetNetworkObject)
        {
            if (targetNetworkObject == null || targetNetworkObject.transform.root == Brain.transform.root)
            {
                return false;
            }

            if (targetNetworkObject.TryGetComponent(out PlayerHealth playerHealth))
            {
                return !playerHealth.IsDowned && playerHealth.gameObject.activeInHierarchy;
            }

            if (targetNetworkObject.TryGetComponent(out NPCHealth npcHealth))
            {
                return !npcHealth.IsDead && npcHealth.gameObject.activeInHierarchy;
            }

            return false;
        }

        private bool IsRageTargetInAttackRange()
        {
            if (rageTargetNetworkObject == null)
            {
                return false;
            }

            float attackRange = Brain.AttackController != null ? Brain.AttackController.AttackRange : Brain.InteractionDistance;
            return Vector3.Distance(Brain.transform.position, rageTargetNetworkObject.transform.position) <= attackRange;
        }

        private void UpdateRageApproachDestination()
        {
            if (Brain.Agent == null || !Brain.Agent.isOnNavMesh || rageTargetNetworkObject == null)
            {
                return;
            }

            nextRageApproachRefreshTime = Time.time + config.RageApproachRefreshInterval;
            ResumeAgent();
            SetRageApproachStoppingDistance();
            Brain.Agent.SetDestination(rageTargetNetworkObject.transform.position);
        }

        private void ExitRageToIdle()
        {
            ClearRageState();
            EnterIdleSearching();
        }

        private void ClearRageState()
        {
            lastAttacker = null;
            rageTargetNetworkObject = null;
            pendingRageAfterCommittedAttack = false;
            RestoreAgentStoppingDistance();
        }

        private void FaceLastAttacker()
        {
            if (lastAttacker == null)
            {
                return;
            }

            Vector3 direction = lastAttacker.transform.position - Brain.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Brain.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private void FaceRageTarget()
        {
            if (rageTargetNetworkObject == null)
            {
                return;
            }

            Vector3 direction = rageTargetNetworkObject.transform.position - Brain.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Brain.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private bool IsStealable(GameObject candidate)
        {
            return TryGetStealableRoot(candidate, out _);
        }

        private bool IsDestructionTargetValid(BaseResourceNew resource)
        {
            if (resource == null || resource.IsPickedUp || config.DestructionProfile == null)
            {
                return false;
            }

            return config.DestructionProfile.TryGetRule(resource, out NPCBaseResourceDestructionRule rule)
                && resource.CanBeDestroyedWith(rule.toolType);
        }

        private bool IsDestructionTargetInAttackRange()
        {
            float attackRange = Brain.AttackController != null ? Brain.AttackController.AttackRange : Brain.InteractionDistance;
            return destructionTarget != null
                && Vector3.Distance(Brain.transform.position, destructionTarget.transform.position) <= attackRange;
        }

        private bool TryStartResourceDestructionAttack()
        {
            return destructionTarget != null
                && Brain.AttackController != null
                && config.DestructionProfile != null
                && config.DestructionProfile.TryGetRule(destructionTarget, out NPCBaseResourceDestructionRule rule)
                && Brain.AttackController.StartResourceAttack(destructionTarget, rule.toolType);
        }

        private void ClearDestructionTarget()
        {
            Brain.AttackController?.CancelPendingResourceAttack();
            destructionTarget = null;
            nextResourceDestructionAttackTime = 0f;
        }

        private void FaceDestructionTarget()
        {
            if (destructionTarget == null)
            {
                return;
            }

            Vector3 direction = destructionTarget.transform.position - Brain.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                Brain.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }

        private bool TryGetStealableRoot(GameObject candidate, out GameObject stealableRoot)
        {
            stealableRoot = null;
            if (candidate == null || candidate.transform.root == Brain.transform.root)
            {
                return false;
            }

            BaseResourceNew baseResource = candidate.GetComponent<BaseResourceNew>();
            baseResource ??= candidate.GetComponentInParent<BaseResourceNew>();
            if (baseResource != null)
            {
                bool canCarry = IsInterestedIn(baseResource) && baseResource.CanBeCarriedBy(Brain.Carrier);
                stealableRoot = canCarry ? baseResource.gameObject : null;
                return canCarry;
            }

            MountableBridgeComponent bridgeComponent = candidate.GetComponent<MountableBridgeComponent>();
            bridgeComponent ??= candidate.GetComponentInParent<MountableBridgeComponent>();
            if (bridgeComponent != null)
            {
                bool canCarry = IsInterestedIn(bridgeComponent) && bridgeComponent.CanBeCarriedBy(Brain.Carrier);
                stealableRoot = canCarry ? bridgeComponent.gameObject : null;
                return canCarry;
            }

            return false;
        }

        private bool IsInterestedIn(BaseResourceNew baseResource)
        {
            return config.InterestProfile == null || config.InterestProfile.IsInterestedIn(baseResource);
        }

        private bool IsInterestedIn(MountableBridgeComponent bridgeComponent)
        {
            return config.InterestProfile == null || config.InterestProfile.IsInterestedIn(bridgeComponent);
        }
    }
}
