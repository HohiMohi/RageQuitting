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
    [SerializeField] private BeaverAttackPatternConfig closeCombatAttack = new BeaverAttackPatternConfig(
        "Scout Strike",
        1f,
        0.15f,
        new[] { new BeaverStrikeConfig(1f, 1.5f, 0f) },
        0.5f);
    [SerializeField] private BeaverLungeConfig lungeAttack = new BeaverLungeConfig(
        1f, 0.15f, 5f, 1.2f, 1.5f, 1f, 0.5f);
    [SerializeField] private float rageHealthThresholdNormalized = 0.5f;
    [SerializeField] private int storageSweepPatrolThreshold = 5;
    [SerializeField] private float storageSweepArrivalDistance = 1.4f;
    [SerializeField] private float resourceZoneSweepArrivalDistance = 1.4f;
    [SerializeField] private float resourceDestructionAttackInterval = 1f;
    [SerializeField] private float idlePatrolRangeIncrease = 2f;
    [SerializeField] private float maxPatrolRadius = 30f;
    [Header("Mudball")]
    [SerializeField] private bool mudballThrowEnabled = true;
    [SerializeField, Min(0.1f)] private float mudballThrowRange = 10f;
    [SerializeField] private bool mudballRequireLineOfSight = true;
    [SerializeField] private LayerMask mudballLineOfSightMask = ~0;
    [SerializeField, Min(0f)] private float mudballCooldown = 45f;
    [SerializeField, Min(0f)] private float mudballWindup = 0.4f;
    [SerializeField, Min(0f)] private float mudballMudGainPercentagePoints = 15f;
    [SerializeField] private BeaverScoutMudball mudballPrefab;
    [SerializeField] private ExternalImpulseProfileSO mudballImpulseProfile;

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
    public BeaverAttackPatternConfig CloseCombatAttack => closeCombatAttack;
    public BeaverLungeConfig LungeAttack => lungeAttack;
    public float RageHealthThresholdNormalized => Mathf.Clamp01(rageHealthThresholdNormalized);
    public int StorageSweepPatrolThreshold => Mathf.Max(1, storageSweepPatrolThreshold);
    public float StorageSweepArrivalDistance => Mathf.Max(0.1f, storageSweepArrivalDistance);
    public float ResourceZoneSweepArrivalDistance => Mathf.Max(0.1f, resourceZoneSweepArrivalDistance);
    public float ResourceDestructionAttackInterval => Mathf.Max(0.05f, resourceDestructionAttackInterval);
    public float IdlePatrolRangeIncrease => Mathf.Max(0f, idlePatrolRangeIncrease);
    public bool MudballThrowEnabled => mudballThrowEnabled;
    public float MudballThrowRange => Mathf.Max(0.1f, mudballThrowRange);
    public bool MudballRequireLineOfSight => mudballRequireLineOfSight;
    public LayerMask MudballLineOfSightMask => mudballLineOfSightMask;
    public float MudballCooldown => Mathf.Max(0f, mudballCooldown);
    public float MudballWindup => Mathf.Max(0f, mudballWindup);
    public float MudballMudGainPercentagePoints => Mathf.Max(0f, mudballMudGainPercentagePoints);
    public BeaverScoutMudball MudballPrefab => mudballPrefab;
    public ExternalImpulseProfileSO MudballImpulseProfile => mudballImpulseProfile;

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
        RagePreparingAttack,
        RageAttacking,
        ReturningToBaseForStorageSweep,
        MovingToKnownSweepLocation,
        SearchingKnownResourceZone
    }

    private class BeaverScoutController : NPCBehaviorController
    {
        private const float PretendStartAngle = 45f;
        private const float PretendResumeAngle = 55f;
        private const float PretendResumeGrace = 0.25f;
        private const float FollowResumeDeadband = 0.5f;

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
        private readonly NavMeshPath followPath = new NavMeshPath();
        private Vector3 homePosition;
        private Vector3 patrolTarget;
        private GameObject targetObject;
        private BaseStorageNew targetStorage;
        private BaseResourceSO targetStorageResource;
        private BaseResourceNew destructionTarget;
        private PlayerHealth followTargetPlayerHealth;
        private Transform followTargetTransform;
        private enum PlayerVisitState
        {
            InitialVisit,
            Outside,
            ReturnReady,
            ReturnAttempted
        }

        private sealed class PlayerVisitMemory
        {
            public Transform TargetTransform;
            public PlayerVisitState State;
        }

        private readonly Dictionary<PlayerHealth, PlayerVisitMemory> playerVisitMemory =
            new Dictionary<PlayerHealth, PlayerVisitMemory>();
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
        private float followRemainingWhenPretending;
        private float gazeLossStartTime = float.NegativeInfinity;
        private Quaternion pretendLookRotation;
        private bool isPretendingToLookAway;
        private bool isStoppedAtFollowDistance;
        private float previousStoppingDistance;
        private bool hasPreviousStoppingDistance;
        private bool previousUpdateRotation;
        private bool hasPreviousUpdateRotation;
        private float reactionLockEndTime;
        private float previousHealth;
        private NetworkObject lastAttacker;
        private NetworkObject rageTargetNetworkObject;
        private bool pendingRageAfterCommittedAttack;
        private NPCAnimationController animationController;
        private BeaverAttackSequenceController attackSequenceController;
        private int consecutivePatrolIdleCount;
        private SweepLocationTarget currentSweepTarget;
        private Vector3 currentSweepDestination;
        private bool finishStorageSweepAfterReturningToBase;
        private float nextResourceDestructionAttackTime;
        private float currentPatrolRadius;
        private BeaverScoutMudThrow mudballThrow;

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
            attackSequenceController = Brain.GetComponent<BeaverAttackSequenceController>();
            mudballThrow = Brain.GetComponent<BeaverScoutMudThrow>();
            if (Brain.Health != null)
            {
                Brain.Health.OnDamaged += BrainHealth_OnDamaged;
            }

            EnterIdleSearching();
        }

        public override void Exit()
        {
            ClearFollowTarget();
            if (mudballThrow != null)
                mudballThrow.CancelWindup();
            if (Brain.Health != null)
            {
                Brain.Health.OnDamaged -= BrainHealth_OnDamaged;
            }

            targetObject = null;
            ClearDestructionTarget();
            lastAttacker = null;
            rageTargetNetworkObject = null;
            pendingRageAfterCommittedAttack = false;
            if (attackSequenceController != null)
            {
                attackSequenceController.CancelSequence();
            }
            Brain.AttackController?.CancelPendingAttacks();
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
            UpdatePlayerVisitMemory();
            if (mudballThrow != null && mudballThrow.IsWindingUp)
            {
                StopAgent();
                return;
            }
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
                case ScoutState.RagePreparingAttack:
                    UpdateRagePreparingAttack();
                    break;
                case ScoutState.RageAttacking:
                    UpdateRageAttacking();
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

        public override void TickFrame()
        {
            if (!isPretendingToLookAway || Brain.Agent == null || !Brain.Agent.isOnNavMesh)
            {
                return;
            }

            Brain.transform.rotation = Quaternion.RotateTowards(
                Brain.transform.rotation,
                pretendLookRotation,
                Brain.Agent.angularSpeed * Time.deltaTime);
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
                if (Brain.Health.IsDead)
                {
                    StopAgent();
                    ClearFollowTarget();
                }
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
            if (mudballThrow != null)
                mudballThrow.CancelWindup();
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
        }

        private void EnterIdleSearching()
        {
            state = ScoutState.IdleSearching;
            targetObject = null;
            ClearDestructionTarget();
            ClearStorageTarget();
            ClearFollowTarget();
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
            if (Time.time >= reactionLockEndTime)
            {
                EnterAttacking();
            }
        }

        private void EnterAttacking()
        {
            state = ScoutState.Attacking;
            FaceLastAttacker();
            if (attackSequenceController == null)
            {
                attackSequenceController = Brain.GetComponent<BeaverAttackSequenceController>();
            }
            if (attackSequenceController == null
                || !attackSequenceController.StartSequence(
                    lastAttacker,
                    config.CloseCombatAttack,
                    config.LungeAttack,
                    HandleScoutAttackCompleted))
            {
                FinishScoutAttack();
            }
        }

        private void UpdateAttacking()
        {
            if (attackSequenceController == null || !attackSequenceController.IsRunning)
            {
                FinishScoutAttack();
            }
        }

        private void HandleScoutAttackCompleted(bool succeeded)
        {
            if (state == ScoutState.Attacking)
            {
                FinishScoutAttack();
            }
            else if (state == ScoutState.RageAttacking)
            {
                FinishRageAttack();
            }
        }

        private void FinishScoutAttack()
        {
            if (pendingRageAfterCommittedAttack && IsRageTargetAvailable())
            {
                pendingRageAfterCommittedAttack = false;
                EnterRagePreparingAttack();
                return;
            }

            ClearRageState();
            EnterIdleSearching();
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
            if (Time.time >= reactionLockEndTime)
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
            FaceRageTarget();
            if (attackSequenceController == null)
            {
                attackSequenceController = Brain.GetComponent<BeaverAttackSequenceController>();
            }
            if (attackSequenceController == null
                || !attackSequenceController.StartSequence(
                    rageTargetNetworkObject,
                    config.CloseCombatAttack,
                    config.LungeAttack,
                    HandleScoutAttackCompleted))
            {
                FinishRageAttack();
            }
        }

        private void UpdateRageAttacking()
        {
            if (attackSequenceController == null || !attackSequenceController.IsRunning)
            {
                FinishRageAttack();
            }
        }

        private void FinishRageAttack()
        {
            if (!IsRageTargetAvailable())
            {
                ExitRageToIdle();
                return;
            }

            EnterRagePreparingAttack();
        }

        private void EnterRagePreparingAttack()
        {
            state = ScoutState.RagePreparingAttack;
            RestoreAgentStoppingDistance();
            StopAgent();
            FaceRageTarget();
            reactionLockEndTime = Time.time;
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
            if (TryFindClosestUnencounteredPlayer(out PlayerHealth playerHealth, out Transform playerTransform))
            {
                ResetConsecutivePatrolIdleCount();
                ClearStorageSweep();
                EnterNoticingPlayer(playerHealth, playerTransform);
                return true;
            }

            if (TryBeginReturnVisitMudballThrow()) return true;

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

        private bool TryBeginReturnVisitMudballThrow()
        {
            if (!config.MudballThrowEnabled || mudballThrow == null || mudballThrow.IsWindingUp ||
                Time.time < mudballThrow.NextThrowAt || config.MudballPrefab == null) return false;

            Collider[] colliders = Physics.OverlapSphere(Brain.transform.position, Brain.DetectionRadius);
            List<(PlayerHealth health, Transform target, float distance)> candidates = new List<(PlayerHealth, Transform, float)>();
            HashSet<PlayerHealth> considered = new HashSet<PlayerHealth>();
            foreach (Collider collider in colliders)
            {
                GameObject candidate = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                if (!TryGetPlayerTarget(candidate, out PlayerHealth health, out Transform targetTransform)) continue;
                if (!considered.Add(health) || !playerVisitMemory.TryGetValue(health, out PlayerVisitMemory memory) ||
                    memory.State != PlayerVisitState.ReturnReady ||
                    (targetTransform.position - Brain.transform.position).sqrMagnitude > Brain.DetectionRadius * Brain.DetectionRadius ||
                    !mudballThrow.CanTarget(health, targetTransform,
                        config.MudballThrowRange, config.MudballRequireLineOfSight, config.MudballLineOfSightMask)) continue;
                float distance = (targetTransform.position - Brain.transform.position).sqrMagnitude;
                candidates.Add((health, targetTransform, distance));
            }

            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
            foreach (var candidate in candidates)
            {
                if (mudballThrow.TryBeginWindup(candidate.health, candidate.target,
                        config.MudballThrowRange, config.MudballRequireLineOfSight, config.MudballLineOfSightMask,
                        config.MudballWindup, config.MudballCooldown, config.MudballPrefab,
                        config.MudballMudGainPercentagePoints, config.MudballImpulseProfile,
                        IsMudballScanContextValid))
                {
                    if (playerVisitMemory.TryGetValue(candidate.health, out PlayerVisitMemory selectedMemory))
                        selectedMemory.State = PlayerVisitState.ReturnAttempted;
                    return true;
                }
            }
            return false;
        }

        private bool IsMudballScanContextValid()
        {
            if (state == ScoutState.IdleSearching) return true;
            if (state != ScoutState.SearchingKnownResourceZone) return false;
            ResourcePopulationZone zone = currentSweepTarget.ResourceZoneInfo.Zone;
            return zone != null && zone.isActiveAndEnabled;
        }

        private void UpdatePlayerVisitMemory()
        {
            List<PlayerHealth> stalePlayers = null;
            foreach (KeyValuePair<PlayerHealth, PlayerVisitMemory> pair in playerVisitMemory)
            {
                PlayerHealth health = pair.Key;
                Transform trackedTransform = pair.Value.TargetTransform;
                if (trackedTransform == null) trackedTransform = ResolvePlayerVisitTransform(health);
                if (health == null || trackedTransform == null ||
                    NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening &&
                    health.NetworkObject != null && !health.NetworkObject.IsSpawned)
                {
                    stalePlayers ??= new List<PlayerHealth>();
                    stalePlayers.Add(health);
                    continue;
                }

                pair.Value.TargetTransform = trackedTransform;
                bool isWithinRange = (trackedTransform.position - Brain.transform.position).sqrMagnitude <=
                                     Brain.DetectionRadius * Brain.DetectionRadius;
                if (isWithinRange && pair.Value.State == PlayerVisitState.Outside)
                    pair.Value.State = PlayerVisitState.ReturnReady;
                else if (!isWithinRange && pair.Value.State != PlayerVisitState.Outside)
                    pair.Value.State = PlayerVisitState.Outside;
            }

            if (stalePlayers != null)
            {
                foreach (PlayerHealth stalePlayer in stalePlayers)
                {
                    playerVisitMemory.Remove(stalePlayer);
                }
            }

        }

        private static Transform ResolvePlayerVisitTransform(PlayerHealth health)
        {
            if (health == null) return null;
            NPCFactionMember factionMember = health.GetComponent<NPCFactionMember>();
            if (factionMember == null) factionMember = health.GetComponentInParent<NPCFactionMember>();
            return factionMember != null ? factionMember.TargetTransform : health.transform;
        }

        private void EnterNoticingPlayer(PlayerHealth playerHealth, Transform playerTransform)
        {
            StopAgent();
            RestoreAgentStoppingDistance();
            ResetConsecutivePatrolIdleCount();
            targetObject = null;
            followTargetPlayerHealth = playerHealth;
            followTargetTransform = playerTransform;
            if (playerHealth != null)
            {
                if (!playerVisitMemory.TryGetValue(playerHealth, out PlayerVisitMemory memory))
                {
                    memory = new PlayerVisitMemory { State = PlayerVisitState.InitialVisit };
                    playerVisitMemory.Add(playerHealth, memory);
                }

                memory.TargetTransform = playerTransform;
            }
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
            gazeLossStartTime = float.NegativeInfinity;
            isStoppedAtFollowDistance = false;
            UpdateFollowingPlayer();
        }

        private void UpdateFollowingPlayer()
        {
            if ((!isPretendingToLookAway && Time.time >= followEndTime) || !IsFollowTargetAvailable())
            {
                EnterIdleSearching();
                return;
            }

            if (isPretendingToLookAway)
            {
                UpdatePretendLookAway();
                return;
            }

            if (TryGetGazeObservation(out float horizontalAngle, out float targetDistance) &&
                targetDistance <= Brain.DetectionRadius && horizontalAngle <= PretendStartAngle)
            {
                BeginPretendingToLookAway();
                return;
            }

            if (Time.time < nextFollowRefreshTime)
            {
                return;
            }

            nextFollowRefreshTime = Time.time + config.FollowRefreshInterval;
            UpdateFollowMovement();
        }

        private void UpdateFollowMovement()
        {
            SetFollowStoppingDistance();
            float distance = GetFollowTargetDistance();
            if (isStoppedAtFollowDistance)
            {
                if (distance < config.FollowStoppingDistance + FollowResumeDeadband)
                {
                    StopAgent();
                    return;
                }

                isStoppedAtFollowDistance = false;
            }

            if (distance <= config.FollowStoppingDistance)
            {
                isStoppedAtFollowDistance = true;
                StopAgent();
                return;
            }

            ResumeAgent();
            RefreshFollowDestination();
        }

        private void RefreshFollowDestination()
        {
            if (Brain.Agent == null || !Brain.Agent.isOnNavMesh || followTargetTransform == null)
            {
                StopAgent();
                return;
            }

            if (!Brain.Agent.CalculatePath(followTargetTransform.position, followPath) ||
                followPath.status != NavMeshPathStatus.PathComplete || !Brain.Agent.SetPath(followPath))
            {
                StopAgent();
            }
        }

        private float GetFollowTargetDistance()
        {
            return followTargetTransform != null
                ? Vector3.Distance(Brain.transform.position, followTargetTransform.position)
                : float.PositiveInfinity;
        }

        private void BeginPretendingToLookAway()
        {
            if (isPretendingToLookAway)
            {
                return;
            }

            followRemainingWhenPretending = Mathf.Max(0f, followEndTime - Time.time);
            isPretendingToLookAway = true;
            gazeLossStartTime = float.NegativeInfinity;
            if (Brain.Agent != null && Brain.Agent.isOnNavMesh)
            {
                if (!hasPreviousUpdateRotation)
                {
                    previousUpdateRotation = Brain.Agent.updateRotation;
                    hasPreviousUpdateRotation = true;
                }

                Brain.Agent.updateRotation = false;
            }

            Vector3 toPlayer = followTargetTransform.position - Brain.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.0001f)
            {
                toPlayer = Brain.transform.forward;
                toPlayer.y = 0f;
            }

            float leftYaw = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg - 90f;
            float rightYaw = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg + 90f;
            float currentYaw = Brain.transform.eulerAngles.y;
            float leftDelta = Mathf.Abs(Mathf.DeltaAngle(currentYaw, leftYaw));
            float rightDelta = Mathf.Abs(Mathf.DeltaAngle(currentYaw, rightYaw));
            float chosenYaw = leftDelta < rightDelta ? leftYaw : rightYaw;
            pretendLookRotation = Quaternion.Euler(0f, chosenYaw, 0f);
            StopAgent();
        }

        private void UpdatePretendLookAway()
        {
            if (TryGetGazeObservation(out float horizontalAngle, out float targetDistance) &&
                targetDistance <= Brain.DetectionRadius && horizontalAngle <= PretendResumeAngle)
            {
                gazeLossStartTime = float.NegativeInfinity;
                return;
            }

            if (float.IsNegativeInfinity(gazeLossStartTime))
            {
                gazeLossStartTime = Time.time;
            }

            if (Time.time - gazeLossStartTime >= PretendResumeGrace)
            {
                StopPretendingToLookAway(true);
            }
        }

        private void StopPretendingToLookAway(bool resumeFollowing)
        {
            if (!isPretendingToLookAway)
            {
                return;
            }

            isPretendingToLookAway = false;
            gazeLossStartTime = float.NegativeInfinity;
            if (resumeFollowing)
            {
                RestoreAgentUpdateRotation();
            }

            if (resumeFollowing && state == ScoutState.FollowingPlayer)
            {
                followEndTime = Time.time + followRemainingWhenPretending;
                followRemainingWhenPretending = 0f;
                nextFollowRefreshTime = 0f;
                if (IsFollowTargetAvailable())
                {
                    UpdateFollowMovement();
                }
            }
            else
            {
                followRemainingWhenPretending = 0f;
            }
        }

        private bool TryGetGazeObservation(out float horizontalAngle, out float targetDistance)
        {
            horizontalAngle = 180f;
            targetDistance = float.PositiveInfinity;
            if (followTargetPlayerHealth == null || followTargetTransform == null || !IsFollowTargetAvailable())
            {
                return false;
            }

            Vector3 playerPosition = followTargetTransform.position;
            Quaternion playerRotation = followTargetTransform.rotation;
            ClientNetworkTransform networkTransform = followTargetTransform.GetComponent<ClientNetworkTransform>();
            if (networkTransform == null)
            {
                networkTransform = followTargetTransform.GetComponentInParent<ClientNetworkTransform>();
            }
            if (networkTransform != null && networkTransform.TryGetLatestAuthoritativeWorldPose(
                    out Vector3 authoritativePosition, out Quaternion authoritativeRotation))
            {
                playerPosition = authoritativePosition;
                playerRotation = authoritativeRotation;
            }

            Vector3 toScout = Brain.transform.position - playerPosition;
            toScout.y = 0f;
            Vector3 bodyForward = playerRotation * Vector3.forward;
            bodyForward.y = 0f;
            if (toScout.sqrMagnitude < 0.0001f || bodyForward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            targetDistance = Vector3.Distance(playerPosition, Brain.transform.position);
            horizontalAngle = Vector3.Angle(bodyForward.normalized, toScout.normalized);
            if (targetDistance > Brain.DetectionRadius || horizontalAngle > PretendResumeAngle)
            {
                return false;
            }

            Vector3 eye = playerPosition + Vector3.up * GetPlayerEyeHeight(followTargetTransform);
            Vector3 scoutCenter = GetScoutCapsuleCenter();
            Vector3 sight = scoutCenter - eye;
            float sightDistance = sight.magnitude;
            if (sightDistance <= 0.001f)
            {
                return true;
            }

            RaycastHit[] hits = Physics.RaycastAll(eye, sight / sightDistance, sightDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                Transform hitTransform = hit.collider.transform;
                if (hitTransform == followTargetPlayerHealth.transform || hitTransform.IsChildOf(followTargetPlayerHealth.transform) ||
                    followTargetTransform.IsChildOf(hitTransform) ||
                    hitTransform == Brain.transform || hitTransform.IsChildOf(Brain.transform))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static float GetPlayerEyeHeight(Transform player)
        {
            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller == null) controller = player.GetComponentInChildren<CharacterController>();
            return controller != null ? Mathf.Max(0.1f, controller.height * 0.9f) : 1.5f;
        }

        private Vector3 GetScoutCapsuleCenter()
        {
            CapsuleCollider capsule = Brain.GetComponent<CapsuleCollider>();
            return capsule != null ? capsule.bounds.center : Brain.transform.position + Vector3.up;
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

            Brain.Agent.ResetPath();
            Brain.Agent.velocity = Vector3.zero;
            Brain.Agent.isStopped = true;
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

        private void RestoreAgentFollowingSettings()
        {
            RestoreAgentStoppingDistance();
            RestoreAgentUpdateRotation();
        }

        private void RestoreAgentUpdateRotation()
        {
            if (Brain.Agent != null && hasPreviousUpdateRotation)
            {
                Brain.Agent.updateRotation = previousUpdateRotation;
                hasPreviousUpdateRotation = false;
            }
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

        private bool TryFindClosestUnencounteredPlayer(out PlayerHealth playerHealth, out Transform playerTransform)
        {
            playerHealth = null;
            playerTransform = null;

            Collider[] colliders = Physics.OverlapSphere(Brain.transform.position, Brain.DetectionRadius);
            float closestDistance = float.MaxValue;

            foreach (Collider collider in colliders)
            {
                GameObject candidate = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                if (!TryGetPlayerTarget(candidate, out PlayerHealth candidateHealth, out Transform candidateTransform))
                {
                    continue;
                }

                if (playerVisitMemory.ContainsKey(candidateHealth))
                {
                    continue;
                }

                float distance = Vector3.Distance(Brain.transform.position, candidateTransform.position);
                if (distance <= Brain.DetectionRadius && distance < closestDistance)
                {
                    closestDistance = distance;
                    playerHealth = candidateHealth;
                    playerTransform = candidateTransform;
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
                if (TryGetPlayerTarget(candidate, out _, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetPlayerTarget(GameObject candidate, out PlayerHealth playerHealth, out Transform playerTransform)
        {
            playerHealth = null;
            playerTransform = null;

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

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening &&
                followTargetPlayerHealth.NetworkObject != null && !followTargetPlayerHealth.NetworkObject.IsSpawned)
            {
                return false;
            }

            return followTargetPlayerHealth.gameObject.activeInHierarchy;
        }

        private void ClearFollowTarget()
        {
            StopPretendingToLookAway(false);
            RestoreAgentFollowingSettings();
            isStoppedAtFollowDistance = false;
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
            return state == ScoutState.PreparingAttack || state == ScoutState.Attacking;
        }

        private bool IsRageActive()
        {
            return state == ScoutState.RagePreparingAttack
                || state == ScoutState.RageAttacking;
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
