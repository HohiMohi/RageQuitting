using Unity.Netcode;
using UnityEngine;

/// <summary>Finds a clear, bounded forward drop pose for physical single-carry deck panels.</summary>
public static class SingleCarryDropPoseResolver
{
    private const float VerticalDropOffset = 0.5f;
    private const float Clearance = 0.05f;
    private const float SearchStep = 0.1f;

    public static bool TryResolvePhysicalPanel(GameObject panel, Transform holder, float maximumRange, out Vector3 position)
    {
        position = default;
        if (panel == null || holder == null || !IsFinite(maximumRange) || maximumRange <= 0f
            || !panel.TryGetComponent(out BoxCollider box) || box.transform != panel.transform)
            return false;

        Vector3 forward = holder.forward.normalized;
        if (forward.sqrMagnitude < 0.999f || !IsFinite(forward)) return false;

        Vector3 panelHalfExtents = Vector3.Scale(box.size * 0.5f, Abs(panel.transform.lossyScale));
        Quaternion rotation = panel.transform.rotation;
        float panelForwardExtent = ProjectedExtent(panelHalfExtents, rotation, forward);
        float holderForwardExtent = GetHolderForwardExtent(holder, panel.transform, forward);
        float minimumDistance = panelForwardExtent + holderForwardExtent + Clearance;
        if (!IsFinite(minimumDistance) || minimumDistance > maximumRange) return false;

        Physics.SyncTransforms();
        Vector3 localCenter = Vector3.Scale(box.center, panel.transform.lossyScale);
        Vector3 panelCenter = panel.transform.position + rotation * localCenter;
        Vector3 targetBase = holder.position + Vector3.up * VerticalDropOffset;
        float distance = minimumDistance;
        while (true)
        {
            Vector3 candidate = targetBase + forward * distance;
            Vector3 candidateCenter = candidate + rotation * localCenter;
            if (HasClearStartPose(panel, holder, panelCenter, panelHalfExtents, rotation)
                && HasClearSweep(panel, holder, panelCenter, candidateCenter, panelHalfExtents, rotation)
                && HasClearFinalPose(panel, candidateCenter, panelHalfExtents, rotation))
            {
                position = candidate;
                return true;
            }

            if (distance >= maximumRange) break;
            distance = Mathf.Min(distance + SearchStep, maximumRange);
        }

        return false;
    }

    private static bool HasClearFinalPose(GameObject panel, Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        Collider[] overlaps = Physics.OverlapBox(center, halfExtents, rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null || overlap.transform == panel.transform || overlap.transform.IsChildOf(panel.transform)) continue;
            if (IsSameNetworkObject(overlap.transform, panel)) continue;
            return false;
        }
        return true;
    }

    private static bool HasClearStartPose(GameObject panel, Transform holder, Vector3 center,
        Vector3 halfExtents, Quaternion rotation)
    {
        Collider[] overlaps = Physics.OverlapBox(center, halfExtents, rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null || overlap.transform == panel.transform || overlap.transform.IsChildOf(panel.transform)
                || IsInHierarchy(overlap.transform, holder) || IsSameNetworkObject(overlap.transform, panel)
                || IsSameNetworkObject(overlap.transform, holder)) continue;
            return false;
        }
        return true;
    }

    private static bool HasClearSweep(GameObject panel, Transform holder, Vector3 startCenter, Vector3 endCenter,
        Vector3 halfExtents, Quaternion rotation)
    {
        Vector3 offset = endCenter - startCenter;
        float distance = offset.magnitude;
        if (distance <= 0.0001f) return true;
        Vector3 direction = offset / distance;
        RaycastHit[] hits = Physics.BoxCastAll(startCenter, halfExtents, direction, rotation, distance,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            Collider obstacle = hit.collider;
            if (obstacle == null || obstacle.transform == panel.transform || obstacle.transform.IsChildOf(panel.transform)
                || IsInHierarchy(obstacle.transform, holder) || IsSameNetworkObject(obstacle.transform, panel)
                || IsSameNetworkObject(obstacle.transform, holder)) continue;
            return false;
        }
        return true;
    }

    private static float GetHolderForwardExtent(Transform holder, Transform excludedPanel, Vector3 forward)
    {
        float maximum = 0f;
        bool found = false;
        foreach (Collider collider in holder.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || !collider.enabled || collider.isTrigger
                || collider.transform == excludedPanel || collider.transform.IsChildOf(excludedPanel)) continue;
            Bounds bounds = collider.bounds;
            float projectedRadius = Vector3.Dot(Abs(forward), bounds.extents);
            float forwardCenter = Vector3.Dot(bounds.center - holder.position, forward);
            maximum = Mathf.Max(maximum, forwardCenter + projectedRadius);
            found = true;
        }
        return found ? Mathf.Max(0f, maximum) : 0.5f;
    }

    private static float ProjectedExtent(Vector3 halfExtents, Quaternion rotation, Vector3 direction)
    {
        Vector3 axes = Abs(Quaternion.Inverse(rotation) * direction);
        return Vector3.Dot(axes, halfExtents);
    }

    private static bool IsInHierarchy(Transform target, Transform root)
        => target == root || target.IsChildOf(root);

    private static bool IsSameNetworkObject(Transform target, Transform source)
    {
        NetworkObject sourceNetworkObject = source.GetComponentInParent<NetworkObject>();
        if (sourceNetworkObject == null || !sourceNetworkObject.IsSpawned) return false;
        NetworkObject candidateNetworkObject = target.GetComponentInParent<NetworkObject>();
        return candidateNetworkObject != null && candidateNetworkObject.IsSpawned
            && candidateNetworkObject.NetworkObjectId == sourceNetworkObject.NetworkObjectId;
    }

    private static bool IsSameNetworkObject(Transform target, GameObject source)
    {
        NetworkObject sourceNetworkObject = source.GetComponent<NetworkObject>();
        if (sourceNetworkObject == null || !sourceNetworkObject.IsSpawned) return false;
        NetworkObject candidateNetworkObject = target.GetComponentInParent<NetworkObject>();
        return candidateNetworkObject != null && candidateNetworkObject.IsSpawned
            && candidateNetworkObject.NetworkObjectId == sourceNetworkObject.NetworkObjectId;
    }

    private static Vector3 Abs(Vector3 value) => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
