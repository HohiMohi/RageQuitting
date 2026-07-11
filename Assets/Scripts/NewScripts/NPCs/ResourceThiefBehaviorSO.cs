using UnityEngine;
using UnityEngine.AI;

[CreateAssetMenu(fileName = "ResourceThiefBehavior", menuName = "Scriptable Objects/NPC/Behaviors/Resource Thief")]
public class ResourceThiefBehaviorSO : NPCBehaviorSO
{
    public enum DeliveryMode
    {
        RemoveFromWorld,
        DropAtDestination
    }

    [SerializeField] private DeliveryMode deliveryMode = DeliveryMode.RemoveFromWorld;
    [SerializeField] private float targetRefreshInterval = 1f;
    [SerializeField] private float deliveryDistance = 1.5f;

    public DeliveryMode Delivery => deliveryMode;
    public float TargetRefreshInterval => targetRefreshInterval;
    public float DeliveryDistance => deliveryDistance;

    public override NPCBehaviorController CreateController(NPCBrain brain)
    {
        return new ResourceThiefController(brain, this);
    }

    private class ResourceThiefController : NPCBehaviorController
    {
        private readonly ResourceThiefBehaviorSO config;
        private Vector3 homePosition;
        private Vector3 patrolTarget;
        private GameObject targetObject;
        private float targetRefreshTimer;

        public ResourceThiefController(NPCBrain brain, ResourceThiefBehaviorSO config) : base(brain)
        {
            this.config = config;
        }

        public override void Enter()
        {
            homePosition = Brain.transform.position;
            SelectPatrolTarget();
        }

        public override void Tick()
        {
            if (Brain.Carrier.CarriedObject != null)
            {
                UpdateDelivering();
                return;
            }

            targetRefreshTimer -= Time.deltaTime;
            if (targetObject == null || targetRefreshTimer <= 0f)
            {
                targetRefreshTimer = Mathf.Max(0.1f, config.TargetRefreshInterval);
                targetObject = FindClosestStealableObject();
            }

            if (targetObject != null)
            {
                UpdateStealing();
                return;
            }

            UpdatePatrol();
        }

        private void UpdateStealing()
        {
            if (!IsStealable(targetObject))
            {
                targetObject = null;
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
                Brain.Agent.SetDestination(homePosition);
            }
        }

        private void UpdateDelivering()
        {
            Brain.Agent.SetDestination(homePosition);
            if (Vector3.Distance(Brain.transform.position, homePosition) > config.DeliveryDistance)
            {
                return;
            }

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

        private void UpdatePatrol()
        {
            if (Brain.Agent.pathPending || Brain.Agent.remainingDistance > 0.75f)
            {
                return;
            }

            SelectPatrolTarget();
        }

        private void SelectPatrolTarget()
        {
            Vector3 randomDirection = Random.insideUnitSphere * Brain.PatrolRadius + homePosition;
            if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, Brain.PatrolRadius, NavMesh.AllAreas))
            {
                patrolTarget = hit.position;
                Brain.Agent.SetDestination(patrolTarget);
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
