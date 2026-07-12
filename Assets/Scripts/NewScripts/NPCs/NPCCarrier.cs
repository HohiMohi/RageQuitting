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
    [SerializeField] private float collisionRadius = 0.5f;

    private GameObject carriedObject;
    private bool isSharedCarry;
    private Vector3 sharedCarryMoveTarget;
    private bool hasSharedCarryMoveTarget;
    private NavMeshAgent agent;

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
    }

    public void ForceRelease(GameObject carried)
    {
        if (carriedObject == carried)
        {
            carriedObject = null;
            isSharedCarry = false;
            hasSharedCarryMoveTarget = false;
        }
    }

    public void SetSharedCarryMoveTarget(Vector3 worldTarget)
    {
        sharedCarryMoveTarget = worldTarget;
        hasSharedCarryMoveTarget = true;
    }

    public void ClearSharedCarryMoveTarget()
    {
        hasSharedCarryMoveTarget = false;
    }

    public Vector3 GetSharedCarryInput()
    {
        if (!isSharedCarry || !hasSharedCarryMoveTarget)
        {
            return Vector3.zero;
        }

        Vector3 direction = sharedCarryMoveTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= sharedCarryInputStopDistance * sharedCarryInputStopDistance)
        {
            return Vector3.zero;
        }

        return direction.normalized;
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
