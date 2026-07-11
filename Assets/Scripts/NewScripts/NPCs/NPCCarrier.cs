using Unity.Netcode;
using UnityEngine;

public class NPCCarrier : NetworkBehaviour, ICarryActor
{
    [SerializeField] private Transform carryAnchor;
    [SerializeField] private Vector3 defaultCarryAnchorLocalPosition = new Vector3(0f, 1f, 0.85f);
    [SerializeField] private float collisionRadius = 0.5f;

    private GameObject carriedObject;

    NetworkObject ICarryActor.NetworkObject => base.NetworkObject;
    public Transform CarryAnchor
    {
        get
        {
            EnsureCarryAnchor();
            return carryAnchor;
        }
    }

    public float CollisionRadius => collisionRadius;
    public bool CanCarryObject => carriedObject == null;
    public GameObject CarriedObject => carriedObject;

    private void Awake()
    {
        EnsureCarryAnchor();
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
        if (carriedObject == null || CarryAnchor == null)
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
    }

    public void ForceRelease(GameObject carried)
    {
        if (carriedObject == carried)
        {
            carriedObject = null;
        }
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
}
