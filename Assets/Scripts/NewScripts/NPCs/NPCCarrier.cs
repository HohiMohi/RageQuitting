using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class NPCCarrier : NetworkBehaviour, ICarryActor
{
    [SerializeField] private Transform carryAnchor;
    [SerializeField] private Transform bodyAnchor;
    [SerializeField] private Vector3 defaultCarryAnchorLocalPosition = new Vector3(0f, 1f, 0.85f);
    [SerializeField] private Vector3 defaultBodyAnchorLocalPosition = new Vector3(0f, 1f, 0f);
    [SerializeField] private float sharedCarryInputStopDistance = 0.35f;
    [SerializeField] private float sharedCarryObjectStopDistance = 0.5f;
    [SerializeField] private float sharedCarryPathRefreshInterval = 0.25f;
    [SerializeField] private float sharedCarryStuckCheckInterval = 0.35f;
    [SerializeField] private float sharedCarryStuckMinMovement = 0.03f;
    [SerializeField] private float sharedCarryStuckInputPauseDuration = 0.5f;
    [SerializeField] private float sharedCarryTargetSampleRadius = 2f;
    [SerializeField] private float collisionRadius = 0.5f;

    private GameObject carriedObject;
    private bool isSharedCarry;
    private Vector3 sharedCarryMoveTarget;
    private bool hasSharedCarryMoveTarget;
    private NavMeshAgent agent;
    private Vector3 lastSharedCarryStuckCheckPosition;
    private Vector3 lastSharedCarryStuckCheckCarriedObjectPosition;
    private float nextSharedCarryPathRefreshTime;
    private float nextSharedCarryStuckCheckTime;
    private float suppressSharedCarryInputUntil;

    public ulong ActorId => NetworkObject != null && NetworkObject.IsSpawned ? NetworkObject.NetworkObjectId : (ulong)GetInstanceID();
    public CarryActorType ActorType => CarryActorType.NPC;
    NetworkObject ICarryActor.NetworkObject => base.NetworkObject;
    public Transform CarryAnchor
    {
        get
        {
            EnsureCarryAnchor();
            return carryAnchor;
        }
    }

    public Transform BodyAnchor
    {
        get
        {
            EnsureBodyAnchor();
            return bodyAnchor;
        }
    }

    public Vector3 BodyAnchorLocalOffset => BodyAnchor != null ? BodyAnchor.localPosition : defaultBodyAnchorLocalPosition;
    public float CollisionRadius => collisionRadius;
    public bool CanCarryObject => carriedObject == null;
    public GameObject CarriedObject => carriedObject;
    public bool IsSharedCarryActive => isSharedCarry;

    private void Awake()
    {
        EnsureCarryAnchor();
        EnsureBodyAnchor();
        agent = GetComponent<NavMeshAgent>();
        if (TryGetComponent(out CharacterController characterController))
        {
            collisionRadius = characterController.radius;
        }
        else if (TryGetComponent(out CapsuleCollider capsuleCollider))
        {
            collisionRadius = capsuleCollider.radius;
        }
    }

    private void LateUpdate()
    {
        if (carriedObject == null || isSharedCarry || CarryAnchor == null)
        {
            return;
        }

        if (ShouldDriveCarryVisual())
        {
            carriedObject.transform.SetPositionAndRotation(CarryAnchor.position, CarryAnchor.rotation);
        }
    }

    public bool TryPickup(GameObject target)
    {
        if (target == null || !CanCarryObject)
        {
            return false;
        }

        if (target.TryGetComponent(out BaseResourceNew baseResource))
        {
            return baseResource.TryPickupByCarrier(this);
        }

        if (target.TryGetComponent(out MountableBridgeComponent bridgeComponent))
        {
            return bridgeComponent.TryPickupByCarrier(this);
        }

        return false;
    }

    public bool DropHeldObject()
    {
        if (carriedObject == null)
        {
            return false;
        }

        GameObject droppedObject = carriedObject;
        Vector3 dropPosition = transform.position + transform.forward * 1.1f + Vector3.up * 0.35f;
        Quaternion dropRotation = transform.rotation;

        if (droppedObject.TryGetComponent(out BaseResourceNew baseResource))
        {
            return baseResource.DropByCarrier(this, dropPosition, dropRotation);
        }

        if (droppedObject.TryGetComponent(out MountableBridgeComponent bridgeComponent))
        {
            return bridgeComponent.DropByCarrier(this, dropPosition, dropRotation);
        }

        ForceRelease(droppedObject);
        return true;
    }

    public void ConfirmCarry(GameObject carried)
    {
        carriedObject = carried;
        isSharedCarry = false;
    }

    public void ConfirmSharedCarry(GameObject carried, Vector3 attachLocalPoint, float movementPenalty)
    {
        carriedObject = carried;
        isSharedCarry = true;
        ResetSharedCarryPathState();
    }

    public void ForceRelease(GameObject carried)
    {
        if (carriedObject == carried)
        {
            carriedObject = null;
            isSharedCarry = false;
            hasSharedCarryMoveTarget = false;
            ResetSharedCarryPathState();
        }
    }

    public void SetSharedCarryMoveTarget(Vector3 worldTarget)
    {
        sharedCarryMoveTarget = worldTarget;
        hasSharedCarryMoveTarget = true;
        TrySetSharedCarryAgentDestination(force: Time.time >= nextSharedCarryPathRefreshTime);
    }

    public void ClearSharedCarryMoveTarget()
    {
        hasSharedCarryMoveTarget = false;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }

        ResetSharedCarryPathState();
    }

    public Vector3 GetSharedCarryInput()
    {
        if (!isSharedCarry || !hasSharedCarryMoveTarget)
        {
            return Vector3.zero;
        }

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return Vector3.zero;
        }

        if (HasSharedCarryReachedMoveTarget())
        {
            return Vector3.zero;
        }

        TrySetSharedCarryAgentDestination(force: Time.time >= nextSharedCarryPathRefreshTime);

        if (Time.time < suppressSharedCarryInputUntil || agent.pathPending)
        {
            return Vector3.zero;
        }

        bool hasUsablePath = agent.hasPath && agent.pathStatus != NavMeshPathStatus.PathInvalid;
        Vector3 direction = hasUsablePath ? GetSharedCarryPathDirection() : GetCarriedObjectToMoveTargetDirection();
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = GetCarriedObjectToMoveTargetDirection();
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.0001f || ShouldSuppressInputBecauseStuck())
        {
            return Vector3.zero;
        }

        return direction.normalized;
    }

    public bool HasCarriedObjectReached(Vector3 worldTarget, float distance)
    {
        if (carriedObject == null)
        {
            return false;
        }

        Vector3 delta = carriedObject.transform.position - worldTarget;
        delta.y = 0f;
        float clampedDistance = Mathf.Max(0f, distance);
        return delta.sqrMagnitude <= clampedDistance * clampedDistance;
    }

    private bool HasSharedCarryReachedMoveTarget()
    {
        if (carriedObject != null)
        {
            return HasCarriedObjectReached(sharedCarryMoveTarget, sharedCarryObjectStopDistance);
        }

        Vector3 targetDelta = sharedCarryMoveTarget - transform.position;
        targetDelta.y = 0f;
        float stopDistance = Mathf.Max(0f, sharedCarryInputStopDistance);
        return targetDelta.sqrMagnitude <= stopDistance * stopDistance;
    }

    private Vector3 GetCarriedObjectToMoveTargetDirection()
    {
        if (carriedObject == null)
        {
            Vector3 targetDelta = sharedCarryMoveTarget - transform.position;
            targetDelta.y = 0f;
            return targetDelta;
        }

        Vector3 objectDelta = sharedCarryMoveTarget - carriedObject.transform.position;
        objectDelta.y = 0f;
        return objectDelta;
    }

    public void ApplySharedCarryAttachment(Vector3 attachWorldPoint)
    {
        if (!isSharedCarry || BodyAnchor == null)
        {
            return;
        }

        Vector3 delta = attachWorldPoint - BodyAnchor.position;
        if (delta.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Move(delta);
            return;
        }

        transform.position += delta;
    }

    private bool ShouldDriveCarryVisual()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || IsServer;
    }

    private bool TrySetSharedCarryAgentDestination(bool force)
    {
        if (!force || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return false;
        }

        Vector3 destination = sharedCarryMoveTarget;
        if (NavMesh.SamplePosition(sharedCarryMoveTarget, out NavMeshHit hit, sharedCarryTargetSampleRadius, agent.areaMask))
        {
            destination = hit.position;
        }

        bool destinationSet = agent.SetDestination(destination);
        nextSharedCarryPathRefreshTime = Time.time + Mathf.Max(0.05f, sharedCarryPathRefreshInterval);
        return destinationSet;
    }

    private Vector3 GetSharedCarryPathDirection()
    {
        NavMeshPath path = agent.path;
        if (path != null && path.corners != null && path.corners.Length > 1)
        {
            for (int i = 1; i < path.corners.Length; i++)
            {
                Vector3 cornerDirection = path.corners[i] - transform.position;
                cornerDirection.y = 0f;
                if (cornerDirection.sqrMagnitude > 0.01f)
                {
                    return cornerDirection;
                }
            }
        }

        Vector3 steeringDirection = agent.steeringTarget - transform.position;
        steeringDirection.y = 0f;
        return steeringDirection;
    }

    private bool ShouldSuppressInputBecauseStuck()
    {
        if (Time.time < nextSharedCarryStuckCheckTime)
        {
            return false;
        }

        float movedDistance = Vector3.Distance(transform.position, lastSharedCarryStuckCheckPosition);
        if (carriedObject != null)
        {
            movedDistance = Mathf.Max(movedDistance, Vector3.Distance(carriedObject.transform.position, lastSharedCarryStuckCheckCarriedObjectPosition));
            lastSharedCarryStuckCheckCarriedObjectPosition = carriedObject.transform.position;
        }

        lastSharedCarryStuckCheckPosition = transform.position;
        nextSharedCarryStuckCheckTime = Time.time + Mathf.Max(0.05f, sharedCarryStuckCheckInterval);

        if (movedDistance >= sharedCarryStuckMinMovement)
        {
            return false;
        }

        suppressSharedCarryInputUntil = Time.time + Mathf.Max(0f, sharedCarryStuckInputPauseDuration);
        TrySetSharedCarryAgentDestination(force: true);
        return true;
    }

    private void ResetSharedCarryPathState()
    {
        lastSharedCarryStuckCheckPosition = transform.position;
        lastSharedCarryStuckCheckCarriedObjectPosition = carriedObject != null ? carriedObject.transform.position : transform.position;
        nextSharedCarryPathRefreshTime = 0f;
        nextSharedCarryStuckCheckTime = Time.time + Mathf.Max(0.05f, sharedCarryStuckCheckInterval);
        suppressSharedCarryInputUntil = 0f;
    }

    private void EnsureCarryAnchor()
    {
        if (carryAnchor != null)
        {
            return;
        }

        GameObject anchorGameObject = new GameObject("NPCCarryAnchor");
        carryAnchor = anchorGameObject.transform;
        carryAnchor.SetParent(transform);
        carryAnchor.localPosition = defaultCarryAnchorLocalPosition;
        carryAnchor.localRotation = Quaternion.identity;
    }

    private void EnsureBodyAnchor()
    {
        if (bodyAnchor != null)
        {
            return;
        }

        GameObject anchorGameObject = new GameObject("NPCBodyAnchor");
        bodyAnchor = anchorGameObject.transform;
        bodyAnchor.SetParent(transform);
        bodyAnchor.localPosition = defaultBodyAnchorLocalPosition;
        bodyAnchor.localRotation = Quaternion.identity;
    }
}
