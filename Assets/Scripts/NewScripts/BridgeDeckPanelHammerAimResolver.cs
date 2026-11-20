using UnityEngine;
using Unity.Netcode;

/// <summary>Resolves only a physically visible, directly hit deck panel for hammer resets.</summary>
public static class BridgeDeckPanelHammerAimResolver
{
    public static bool TryFindVisiblePanel(Ray ray, float maximumDistance, Transform excludedRoot,
        out BridgeDeckPanelMounting marker, out Collider hitCollider)
    {
        marker = null;
        hitCollider = null;
        if (!IsFinite(ray.origin) || !IsFinite(ray.direction) || ray.direction.sqrMagnitude < 0.99f
            || ray.direction.sqrMagnitude > 1.01f || !IsFinite(maximumDistance) || maximumDistance <= 0f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(ray, maximumDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        NetworkObject playerObject = excludedRoot != null ? excludedRoot.GetComponentInParent<NetworkObject>() : null;
        NetworkManager manager = playerObject != null && playerObject.IsSpawned ? playerObject.NetworkManager : null;
        if (IsOriginInsideSolid(ray.origin, excludedRoot, manager)) return false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null || excludedRoot != null && collider.transform.root == excludedRoot.root) continue;
            NetworkObject hitNetworkObject = collider.GetComponentInParent<NetworkObject>();
            if (manager != null && hitNetworkObject != null && hitNetworkObject.IsSpawned
                && hitNetworkObject.NetworkManager != manager) continue;
            BridgeDeckPanelMounting candidate = collider.GetComponentInParent<BridgeDeckPanelMounting>();
            if (candidate == null || !candidate.isActiveAndEnabled || !candidate.gameObject.activeInHierarchy
                || !candidate.IsPhysicalPanel
                || !candidate.TryGetComponent(out MountableBridgeComponent panel)
                || !panel.IsPhysicalDeckPanel || panel.IsPickedUp)
                return false;
            marker = candidate;
            hitCollider = collider;
            return true;
        }
        return false;
    }

    private static bool IsOriginInsideSolid(Vector3 origin, Transform excludedRoot, NetworkManager manager)
    {
        Collider[] overlaps = Physics.OverlapSphere(origin, 0.001f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider collider = overlaps[i];
            if (collider == null || excludedRoot != null && collider.transform.root == excludedRoot.root) continue;
            NetworkObject targetObject = collider.GetComponentInParent<NetworkObject>();
            if (manager != null && targetObject != null && targetObject.IsSpawned
                && targetObject.NetworkManager != manager) continue;
            if ((collider.ClosestPoint(origin) - origin).sqrMagnitude <= 0.00000225f) return true;
        }
        return false;
    }

    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
