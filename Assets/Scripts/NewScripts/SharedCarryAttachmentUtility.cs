using System.Collections.Generic;
using UnityEngine;

public static class SharedCarryAttachmentUtility
{
    private readonly struct AttachPointCandidate
    {
        public readonly int Index;
        public readonly float SqrDistance;

        public AttachPointCandidate(int index, float sqrDistance)
        {
            Index = index;
            SqrDistance = sqrDistance;
        }
    }

    public static List<int> GetFreeAttachPointIndicesByDistance(
        Transform carriedObject,
        Vector3 actorAnchorWorldPosition,
        int attachPointCount,
        System.Func<int, bool> isOccupied,
        System.Func<int, Vector3> getAttachLocalPoint)
    {
        List<AttachPointCandidate> candidates = new List<AttachPointCandidate>();
        if (carriedObject == null || attachPointCount <= 0 || isOccupied == null || getAttachLocalPoint == null)
        {
            return new List<int>();
        }

        for (int index = 0; index < attachPointCount; index++)
        {
            if (isOccupied(index))
            {
                continue;
            }

            Vector3 worldPoint = carriedObject.TransformPoint(getAttachLocalPoint(index));
            candidates.Add(new AttachPointCandidate(index, (worldPoint - actorAnchorWorldPosition).sqrMagnitude));
        }

        candidates.Sort((left, right) =>
        {
            int distanceComparison = left.SqrDistance.CompareTo(right.SqrDistance);
            return distanceComparison != 0 ? distanceComparison : left.Index.CompareTo(right.Index);
        });

        List<int> sortedIndices = new List<int>(candidates.Count);
        foreach (AttachPointCandidate candidate in candidates)
        {
            sortedIndices.Add(candidate.Index);
        }

        return sortedIndices;
    }

    public static Bounds GetLocalColliderBounds(Transform root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(false);
        if (colliders.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.one);
        }

        bool hasBounds = false;
        Bounds localBounds = default;
        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                continue;
            }

            Bounds worldBounds = collider.bounds;
            Vector3 extents = worldBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 localPoint = root.InverseTransformPoint(worldBounds.center + Vector3.Scale(extents, new Vector3(x, y, z)));
                        if (!hasBounds)
                        {
                            localBounds = new Bounds(localPoint, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localPoint);
                        }
                    }
                }
            }
        }

        return hasBounds ? localBounds : new Bounds(Vector3.zero, Vector3.one);
    }

    public static Vector3 GenerateDefaultAttachLocalPoint(Transform root, int attachPointIndex, int carrierCount, float carrierRadius, float clearance)
    {
        Bounds bounds = GetLocalColliderBounds(root);
        float attachDistance = Mathf.Max(0.01f, carrierRadius + clearance);
        float attachHeight = bounds.center.y;

        if (carrierCount <= 1)
        {
            return new Vector3(bounds.center.x, attachHeight, bounds.max.z + attachDistance);
        }

        if (carrierCount == 2)
        {
            float side = attachPointIndex == 0 ? -1f : 1f;
            return new Vector3(bounds.center.x + side * (bounds.extents.x + attachDistance), attachHeight, bounds.center.z);
        }

        float angle = Mathf.PI * 2f * attachPointIndex / carrierCount;
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + attachDistance;
        return bounds.center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    public static void NormalizeSharedCarryOrientation(Rigidbody body)
    {
        NormalizeSharedCarryOrientation(body, Vector3.zero);
    }

    public static void NormalizeSharedCarryOrientation(Rigidbody body, Vector3 rotationOffsetEuler)
    {
        if (body == null)
        {
            return;
        }

        float lowestPointBeforeRotation = GetLowestColliderPointY(body.transform);
        Quaternion rotationOffset = Quaternion.Euler(rotationOffsetEuler);
        Quaternion orientationWithoutOffset = body.rotation * Quaternion.Inverse(rotationOffset);
        Vector3 heading = Vector3.ProjectOnPlane(orientationWithoutOffset * Vector3.forward, Vector3.up);
        if (heading.sqrMagnitude < 0.0001f)
        {
            heading = Vector3.ProjectOnPlane(orientationWithoutOffset * Vector3.right, Vector3.up);
        }

        Quaternion yawRotation = heading.sqrMagnitude >= 0.0001f
            ? Quaternion.LookRotation(heading.normalized, Vector3.up)
            : Quaternion.Euler(0f, body.rotation.eulerAngles.y, 0f);
        Transform bodyTransform = body.transform;
        bodyTransform.rotation = yawRotation * rotationOffset;
        Physics.SyncTransforms();

        float lowestPointAfterRotation = GetLowestColliderPointY(bodyTransform);
        if (float.IsFinite(lowestPointBeforeRotation) && float.IsFinite(lowestPointAfterRotation))
        {
            bodyTransform.position += Vector3.up * (lowestPointBeforeRotation - lowestPointAfterRotation);
            Physics.SyncTransforms();
        }

        body.angularVelocity = Vector3.zero;
        body.linearVelocity = Vector3.zero;
    }

    private static float GetLowestColliderPointY(Transform root)
    {
        float lowestPoint = float.PositiveInfinity;
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(false))
        {
            if (collider != null && collider.enabled && !collider.isTrigger)
            {
                lowestPoint = Mathf.Min(lowestPoint, collider.bounds.min.y);
            }
        }

        return lowestPoint;
    }

    public static bool TryFindSafePlayerRootPosition(
        Transform playerRoot,
        CharacterController controller,
        Transform carriedObject,
        Vector3 desiredAnchorPosition,
        Vector3 bodyAnchorLocalOffset,
        float maxVerticalPlacementDelta,
        out Vector3 safeRootPosition)
    {
        safeRootPosition = playerRoot != null ? playerRoot.position : Vector3.zero;
        if (playerRoot == null || controller == null || carriedObject == null)
        {
            return false;
        }

        float referenceRootY = playerRoot.position.y;
        Vector3 desiredRootPosition = desiredAnchorPosition - playerRoot.TransformVector(bodyAnchorLocalOffset);
        desiredRootPosition.y = referenceRootY;

        float searchStep = Mathf.Max(0.2f, controller.radius * 1.2f);
        for (int ring = 0; ring <= 4; ring++)
        {
            int samples = ring == 0 ? 1 : 12;
            for (int sample = 0; sample < samples; sample++)
            {
                Vector3 offset = Vector3.zero;
                if (ring > 0)
                {
                    float angle = Mathf.PI * 2f * sample / samples;
                    offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * searchStep * ring;
                }

                Vector3 candidate = desiredRootPosition + offset;
                if (!TryFindGroundedRootY(candidate, referenceRootY, maxVerticalPlacementDelta, controller, playerRoot, carriedObject, out float groundedRootY))
                {
                    continue;
                }

                candidate.y = groundedRootY;
                if (IsCapsuleClear(candidate, controller, playerRoot, carriedObject))
                {
                    safeRootPosition = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindGroundedRootY(
        Vector3 position,
        float referenceRootY,
        float maxVerticalPlacementDelta,
        CharacterController controller,
        Transform playerRoot,
        Transform carriedObject,
        out float rootY)
    {
        maxVerticalPlacementDelta = Mathf.Max(0f, maxVerticalPlacementDelta);
        float probeStartHeight = controller.height * 0.5f + maxVerticalPlacementDelta + 0.1f;
        float probeDistance = controller.height + maxVerticalPlacementDelta * 2f + 0.5f;
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * probeStartHeight, Vector3.down, probeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float closestVerticalDelta = float.MaxValue;
        rootY = referenceRootY;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || IsIgnoredCollider(hit.collider, playerRoot, carriedObject) || hit.normal.y < 0.55f)
            {
                continue;
            }

            float candidateRootY = hit.point.y - (controller.center.y - controller.height * 0.5f) + controller.skinWidth;
            float verticalDelta = Mathf.Abs(candidateRootY - referenceRootY);
            if (verticalDelta > maxVerticalPlacementDelta || verticalDelta >= closestVerticalDelta)
            {
                continue;
            }

            closestVerticalDelta = verticalDelta;
            rootY = candidateRootY;
        }

        return closestVerticalDelta < float.MaxValue;
    }

    private static bool IsCapsuleClear(Vector3 rootPosition, CharacterController controller, Transform playerRoot, Transform carriedObject)
    {
        float radius = Mathf.Max(0.01f, controller.radius - controller.skinWidth);
        float halfSegment = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        Vector3 center = rootPosition + controller.center;
        Vector3 point1 = center + Vector3.up * halfSegment;
        Vector3 point2 = center - Vector3.up * halfSegment;

        Collider[] overlaps = Physics.OverlapCapsule(point1, point2, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (!IsIgnoredCollider(overlap, playerRoot, carriedObject))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIgnoredCollider(Collider collider, Transform playerRoot, Transform carriedObject)
    {
        Transform colliderTransform = collider.transform;
        return colliderTransform == playerRoot || colliderTransform.IsChildOf(playerRoot)
            || colliderTransform == carriedObject || colliderTransform.IsChildOf(carriedObject);
    }
}
