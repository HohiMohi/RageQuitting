using Unity.Netcode;
using UnityEngine;

public static class RopeAttachmentResolver
{
    public static bool TryResolve(Collider collider, RopeToolController rope, Vector3 hitPoint, out RopeAttachment attachment)
    {
        attachment = default;
        if (collider == null || rope == null)
        {
            return false;
        }

        IRopeAttachable explicitTarget = collider.GetComponentInParent<IRopeAttachable>();
        if (explicitTarget != null)
        {
            return explicitTarget.TryCreateRopeAttachment(rope, hitPoint, out attachment);
        }

        NetworkObject target = collider.GetComponentInParent<NetworkObject>();
        if (target == null || target == rope.NetworkObject)
        {
            return false;
        }

        PlayerHealth player = target.GetComponent<PlayerHealth>();
        if (player != null)
        {
            DownedPlayerCarryable carryable = target.GetComponent<DownedPlayerCarryable>();
            if (carryable != null && carryable.IsCarried)
            {
                return false;
            }

            CharacterController controller = target.GetComponent<CharacterController>();
            Vector3 worldPoint = controller != null
                ? target.transform.TransformPoint(controller.center + Vector3.up * controller.height * 0.15f)
                : target.transform.position + Vector3.up * 1.2f;
            attachment = new RopeAttachment(target, RopeTargetKind.Player, target.transform.InverseTransformPoint(worldPoint));
            return true;
        }

        BaseResourceNew resource = target.GetComponent<BaseResourceNew>();
        BaseResourceSO definition = resource != null ? resource.GetBaseResourceSO() : null;
        if (resource == null || definition == null || !resource.CanBeCarried || resource.IsPickedUp
            || definition.allowMultipleCarriers || definition.recommendedCarriers > 1 || definition.maxCarriers > 1)
        {
            return false;
        }

        attachment = new RopeAttachment(target, RopeTargetKind.Resource, target.transform.InverseTransformPoint(hitPoint));
        return true;
    }
}
