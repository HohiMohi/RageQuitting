using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public static class SharedCarryMovementSolver
{
    public struct Settings
    {
        public LayerMask obstacleLayers;
        public float collisionSkin;
        public float minimumStep;
        public int solverIterations;
        public float supportSurfaceNormalYThreshold;
    }

    public static Vector3 GetSafeSharedCarryDelta(
        Vector3 desiredDelta,
        IList<ISharedCarryCollisionProvider> holders,
        GameObject carriedObject,
        Settings settings)
    {
        if (desiredDelta.sqrMagnitude <= 0.000001f || holders == null || holders.Count == 0)
        {
            return Vector3.zero;
        }

        if (CanMove(desiredDelta, holders, carriedObject, settings))
        {
            return desiredDelta;
        }

        float low = 0f;
        float high = 1f;
        int iterations = Mathf.Clamp(settings.solverIterations, 1, 12);
        for (int i = 0; i < iterations; i++)
        {
            float fraction = (low + high) * 0.5f;
            if (CanMove(desiredDelta * fraction, holders, carriedObject, settings))
            {
                low = fraction;
            }
            else
            {
                high = fraction;
            }
        }

        Vector3 safeDelta = desiredDelta * low;
        return safeDelta.magnitude >= Mathf.Max(0f, settings.minimumStep) ? safeDelta : Vector3.zero;
    }

    private static bool CanMove(
        Vector3 delta,
        IList<ISharedCarryCollisionProvider> holders,
        GameObject carriedObject,
        Settings settings)
    {
        if (delta.sqrMagnitude <= 0.000001f)
        {
            return true;
        }

        float skin = Mathf.Max(0f, settings.collisionSkin);
        foreach (ISharedCarryCollisionProvider holder in holders)
        {
            if (holder == null || !holder.CanApplySharedCarryDelta(delta)
                || !holder.TryGetSharedCarryCapsule(out Vector3 point1, out Vector3 point2, out float radius))
            {
                return false;
            }

            float distance = delta.magnitude;
            Vector3 direction = delta / distance;
            // Keep the capsule at its real size. Expanding the radius here makes a
            // grounded holder overlap the floor before it has moved at all.
            float queryRadius = radius;
            float capsuleBottom = Mathf.Min(point1.y, point2.y) - radius;

            RaycastHit[] hits = Physics.CapsuleCastAll(
                point1,
                point2,
                queryRadius,
                direction,
                distance + skin,
                settings.obstacleLayers,
                QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                if (IsSupportSurface(hit, capsuleBottom, skin, settings.supportSurfaceNormalYThreshold))
                {
                    continue;
                }

                if (!IsIgnoredCollider(hit.collider, carriedObject, holders))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsSupportSurface(
        RaycastHit hit,
        float capsuleBottom,
        float collisionSkin,
        float normalYThreshold)
    {
        float threshold = Mathf.Clamp01(normalYThreshold);
        if (hit.normal.y >= threshold)
        {
            return true;
        }

        // Capsule casts can report a zero normal when the capsule starts tangent
        // to the floor. Treat only colliders whose top is at the holder's feet as
        // support in that case; walls and raised obstacles remain blocking.
        return hit.normal.sqrMagnitude <= 0.0001f
            && hit.collider != null
            && hit.collider.bounds.max.y <= capsuleBottom + Mathf.Max(0.05f, collisionSkin + 0.01f);
    }

    private static bool IsIgnoredCollider(
        Collider collider,
        GameObject carriedObject,
        IList<ISharedCarryCollisionProvider> holders)
    {
        if (collider == null)
        {
            return true;
        }

        Transform colliderTransform = collider.transform;
        if (IsPartOfObject(collider, carriedObject))
        {
            return true;
        }

        // Some network prefabs keep their gameplay component and colliders on
        // different branches. In that layout the shared NetworkObject is the
        // reliable ownership boundary for filtering the carried object.
        NetworkObject carriedNetworkObject = carriedObject != null
            ? carriedObject.GetComponentInParent<NetworkObject>()
            : null;
        NetworkObject hitNetworkObject = collider.GetComponentInParent<NetworkObject>();
        if (carriedNetworkObject != null && hitNetworkObject == carriedNetworkObject)
        {
            return true;
        }

        BaseResourceNew carriedResource = carriedObject != null
            ? carriedObject.GetComponentInParent<BaseResourceNew>()
            : null;
        if (carriedResource != null && collider.GetComponentInParent<BaseResourceNew>() == carriedResource)
        {
            return true;
        }

        MountableBridgeComponent carriedBridge = carriedObject != null
            ? carriedObject.GetComponentInParent<MountableBridgeComponent>()
            : null;
        if (carriedBridge != null && collider.GetComponentInParent<MountableBridgeComponent>() == carriedBridge)
        {
            return true;
        }

        foreach (ISharedCarryCollisionProvider holder in holders)
        {
            if (holder?.CollisionRoot == null)
            {
                continue;
            }

            Transform holderRoot = holder.CollisionRoot.transform;
            if (colliderTransform == holderRoot || colliderTransform.IsChildOf(holderRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPartOfObject(Collider collider, GameObject rootObject)
    {
        if (collider == null || rootObject == null)
        {
            return false;
        }

        Transform colliderTransform = collider.transform;
        Transform rootTransform = rootObject.transform;
        // Support both layouts used by resource prefabs: the gameplay component
        // can live on the collider root, or on a child below a shared collider.
        if (colliderTransform == rootTransform
            || colliderTransform.IsChildOf(rootTransform)
            || rootTransform.IsChildOf(colliderTransform))
        {
            return true;
        }

        Rigidbody rootRigidbody = rootObject.GetComponentInParent<Rigidbody>();
        return rootRigidbody != null && collider.attachedRigidbody == rootRigidbody;
    }
}
