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

    public DeliveryMode Delivery => deliveryMode;
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
        Delivering
    }

    private class BeaverScoutController : NPCBehaviorController
    {
        private readonly BeaverScoutBehaviorSO config;
        private Vector3 homePosition;
        private Vector3 patrolTarget;
        private GameObject targetObject;
        private PlayerHealth followTargetPlayerHealth;
        private Transform followTargetTransform;
        private readonly HashSet<ulong> encounteredPlayerIds = new HashSet<ulong>();
        private ScoutState state;
        private float idleSearchEndTime;
        private float nextTargetRefreshTime;
        private float noticeEndTime;
        private float followEndTime;
        private float nextFollowRefreshTime;
        private float previousStoppingDistance;
        private bool hasPreviousStoppingDistance;
        private float reactionLockEndTime;
        private float previousHealth;
        private NPCAnimationController animationController;

        public BeaverScoutController(NPCBrain brain, BeaverScoutBehaviorSO config) : base(brain)
        {
            this.config = config;
        }

        public override void Enter()
        {
            homePosition = Brain.transform.position;
            previousHealth = Brain.Health != null ? Brain.Health.CurrentHealth : 0f;
            animationController = Brain.GetComponent<NPCAnimationController>();
            if (Brain.Health != null)
            {
                Brain.Health.OnHealthChanged += BrainHealth_OnHealthChanged;
            }

            EnterIdleSearching();
        }

        public override void Exit()
        {
            if (Brain.Health != null)
            {
                Brain.Health.OnHealthChanged -= BrainHealth_OnHealthChanged;
            }

            targetObject = null;
            if (Brain.Agent != null && Brain.Agent.isOnNavMesh)
            {
                Brain.Agent.ResetPath();
                Brain.Agent.isStopped = false;
            }
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
                case ScoutState.Delivering:
                    UpdateDelivering();
                    break;
            }
        }

        private void BrainHealth_OnHealthChanged(object sender, System.EventArgs e)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && Brain.IsSpawned && !Brain.IsServer)
            {
                return;
            }

            if (Brain.Health == null)
            {
                return;
            }

            float currentHealth = Brain.Health.CurrentHealth;
            bool receivedDamage = currentHealth < previousHealth;
            previousHealth = currentHealth;

            if (!receivedDamage || Brain.Health.IsDead)
            {
                return;
            }

            HandleDamageReaction();
        }

        private void HandleDamageReaction()
        {
            StopAgent();

            if (Brain.Carrier != null && Brain.Carrier.CarriedObject != null)
            {
                Brain.Carrier.DropHeldObject();
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
            ClearFollowTarget();
            reactionLockEndTime = Time.time + config.HitReactionLockDuration;
            EnterIdleSearching();
            idleSearchEndTime = reactionLockEndTime + config.IdleSearchDuration;
        }

        private void EnterIdleSearching()
        {
            state = ScoutState.IdleSearching;
            targetObject = null;
            ClearFollowTarget();
            RestoreAgentStoppingDistance();
            idleSearchEndTime = Time.time + config.IdleSearchDuration;
            nextTargetRefreshTime = 0f;
            StopAgent();
        }

        private void UpdateIdleSearching()
        {
            if (Time.time >= nextTargetRefreshTime)
            {
                nextTargetRefreshTime = Time.time + config.TargetRefreshInterval;
                if (TryFindClosestUnencounteredPlayer(out PlayerHealth playerHealth, out Transform playerTransform, out ulong playerId))
                {
                    EnterNoticingPlayer(playerHealth, playerTransform, playerId);
                    return;
                }

                targetObject = FindClosestStealableObject();
            }

            if (targetObject != null)
            {
                EnterMovingToTarget();
                return;
            }

            if (Time.time >= idleSearchEndTime)
            {
                EnterMovingToPatrolPoint();
            }
        }

        private void EnterNoticingPlayer(PlayerHealth playerHealth, Transform playerTransform, ulong playerId)
        {
            StopAgent();
            RestoreAgentStoppingDistance();
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
            ResumeAgent();
            Brain.Agent.SetDestination(homePosition);
        }

        private void UpdateDelivering()
        {
            if (Brain.Carrier.CarriedObject == null)
            {
                EnterIdleSearching();
                return;
            }

            Brain.Agent.SetDestination(homePosition);
            if (Vector3.Distance(Brain.transform.position, homePosition) > config.DeliveryDistance)
            {
                return;
            }

            DeliverCarriedObject();
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

        private bool TrySelectPatrolTarget(out Vector3 selectedPatrolTarget)
        {
            selectedPatrolTarget = Brain.transform.position;
            for (int i = 0; i < 8; i++)
            {
                Vector3 randomDirection = Random.insideUnitSphere * Brain.PatrolRadius + homePosition;
                if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, Brain.PatrolRadius, NavMesh.AllAreas))
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

        private bool IsStealable(GameObject candidate)
        {
            return TryGetStealableRoot(candidate, out _);
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
                bool canCarry = baseResource.CanBeCarriedBy(Brain.Carrier);
                stealableRoot = canCarry ? baseResource.gameObject : null;
                return canCarry;
            }

            MountableBridgeComponent bridgeComponent = candidate.GetComponent<MountableBridgeComponent>();
            bridgeComponent ??= candidate.GetComponentInParent<MountableBridgeComponent>();
            if (bridgeComponent != null)
            {
                bool canCarry = bridgeComponent.CanBeCarriedBy(Brain.Carrier);
                stealableRoot = canCarry ? bridgeComponent.gameObject : null;
                return canCarry;
            }

            return false;
        }
    }
}
