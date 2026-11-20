using Unity.Netcode;
using UnityEngine;

/// <summary>Bounded aim-assist and visibility resolver for the exposed portion of mounting nails.</summary>
public static class MountingNailAimResolver
{
    private const int MaxCandidateColliders = 128;
    private const int MaxVisibilityHits = 64;
    private const float VisibilityEpsilon = 0.002f;
    private const float OriginContainmentRadius = 0.001f;
    private const float OriginContainmentTolerance = 0.0015f;
    private static readonly Collider[] CandidateBuffer = new Collider[MaxCandidateColliders];
    private static readonly Collider[] OriginOverlapBuffer = new Collider[MaxVisibilityHits];
    private static readonly RaycastHit[] VisibilityBuffer = new RaycastHit[MaxVisibilityHits];

    public static bool TryFindCandidate(Ray aimRay, float maximumDistance, Transform excludedRoot,
        out MountingNail candidate, out Collider candidateCollider, out Vector3 aimPoint)
    {
        candidate = null;
        candidateCollider = null;
        aimPoint = default;
        if (!IsFinite(aimRay.origin) || !IsFinite(aimRay.direction)
            || aimRay.direction.sqrMagnitude < 0.99f || aimRay.direction.sqrMagnitude > 1.01f
            || !IsFinite(maximumDistance) || maximumDistance <= 0f) return false;

        Vector3 direction = aimRay.direction.normalized;
        NetworkManager authorityManager = ResolveAuthorityManager(excludedRoot);
        Vector3 end = aimRay.origin + direction * maximumDistance;
        float broadPhaseRadius = MountingNail.MaximumActiveAimRadius;
        if (broadPhaseRadius <= 0f) return false;
        int count = Physics.OverlapCapsuleNonAlloc(aimRay.origin, end, broadPhaseRadius,
            CandidateBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        if (count >= CandidateBuffer.Length)
        {
            ArrayClearCandidates(CandidateBuffer.Length);
            return false;
        }
        float bestAlongRay = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider collider = CandidateBuffer[i];
            CandidateBuffer[i] = null;
            if (ShouldIgnoreCollider(collider, excludedRoot, authorityManager)) continue;
            MountingNail nail = collider.GetComponentInParent<MountingNail>();
            if (nail == null || !nail.TryGetExposedAimSegment(out Vector3 segmentStart, out Vector3 segmentEnd)) continue;
            if (IsForeignNetworkCopy(nail, authorityManager)) continue;
            float radius = nail.Profile != null ? nail.Profile.AimRadiusMeters : 0f;
            if (!TryClosestRaySegment(aimRay.origin, direction, segmentStart, segmentEnd,
                    out float alongRay, out Vector3 pointOnSegment, out float distanceSqr)
                || alongRay > maximumDistance || distanceSqr > radius * radius
                || alongRay >= bestAlongRay
                || !HasLineOfSight(aimRay.origin, pointOnSegment, nail, excludedRoot, authorityManager)) continue;

            bestAlongRay = alongRay;
            candidate = nail;
            candidateCollider = collider;
            aimPoint = pointOnSegment;
        }

        ArrayClearCandidates(count);
        return candidate != null;
    }

    private static bool HasLineOfSight(Vector3 origin, Vector3 point, MountingNail target, Transform excludedRoot,
        NetworkManager authorityManager)
    {
        Vector3 delta = point - origin;
        float distance = delta.magnitude;
        if (distance <= VisibilityEpsilon) return true;
        if (IsOriginInsideSolid(origin, target, excludedRoot, authorityManager)) return false;
        int count = Physics.RaycastNonAlloc(origin, delta / distance, VisibilityBuffer,
            distance + VisibilityEpsilon, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        if (count >= VisibilityBuffer.Length)
        {
            ArrayClearVisibility(VisibilityBuffer.Length);
            return false;
        }
        float closestBlocker = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = VisibilityBuffer[i];
            Collider collider = hit.collider;
            if (ShouldIgnoreCollider(collider, excludedRoot, authorityManager)) continue;
            if (collider.isTrigger && collider.GetComponentInParent<MountingNail>() != target) continue;
            if (collider.GetComponentInParent<MountingNail>() == target)
            {
                continue;
            }
            if (hit.distance < closestBlocker) closestBlocker = hit.distance;
        }

        ArrayClearVisibility(count);
        return closestBlocker >= distance - VisibilityEpsilon;
    }

    private static bool IsOriginInsideSolid(Vector3 origin, MountingNail target, Transform excludedRoot,
        NetworkManager authorityManager)
    {
        int count = Physics.OverlapSphereNonAlloc(origin, OriginContainmentRadius, OriginOverlapBuffer,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        if (count >= OriginOverlapBuffer.Length)
        {
            ArrayClearOriginOverlaps(OriginOverlapBuffer.Length);
            return true;
        }

        bool insideSolid = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = OriginOverlapBuffer[i];
            OriginOverlapBuffer[i] = null;
            if (collider == null || collider.isTrigger || ShouldIgnoreCollider(collider, excludedRoot, authorityManager)
                || collider.GetComponentInParent<MountingNail>() == target) continue;

            Vector3 closest = collider.ClosestPoint(origin);
            if ((closest - origin).sqrMagnitude <= OriginContainmentTolerance * OriginContainmentTolerance)
            {
                insideSolid = true;
                break;
            }
        }

        ArrayClearOriginOverlaps(count);
        return insideSolid;
    }

    private static NetworkManager ResolveAuthorityManager(Transform excludedRoot)
    {
        if (excludedRoot == null) return null;
        NetworkObject playerNetworkObject = excludedRoot.GetComponentInParent<NetworkObject>();
        return playerNetworkObject != null && playerNetworkObject.IsSpawned
            ? playerNetworkObject.NetworkManager
            : null;
    }

    private static bool ShouldIgnoreCollider(Collider collider, Transform excludedRoot, NetworkManager authorityManager)
    {
        if (collider == null) return true;
        if (excludedRoot != null && collider.transform.root == excludedRoot.root) return true;
        if (authorityManager == null) return false;
        NetworkObject networkObject = collider.GetComponentInParent<NetworkObject>();
        return networkObject != null && networkObject.IsSpawned
            && networkObject.NetworkManager != authorityManager;
    }

    private static bool IsForeignNetworkCopy(MountingNail nail, NetworkManager authorityManager)
    {
        if (authorityManager == null) return false;
        NetworkObject networkObject = nail.GetComponent<NetworkObject>();
        return networkObject != null && networkObject.IsSpawned && networkObject.NetworkManager != authorityManager;
    }

    private static bool TryClosestRaySegment(Vector3 rayOrigin, Vector3 rayDirection,
        Vector3 segmentStart, Vector3 segmentEnd, out float rayDistance, out Vector3 segmentPoint, out float distanceSqr)
    {
        float projectedStart = Vector3.Dot(segmentStart - rayOrigin, rayDirection);
        float projectedEnd = Vector3.Dot(segmentEnd - rayOrigin, rayDirection);
        if (Mathf.Max(projectedStart, projectedEnd) < 0f)
        {
            rayDistance = 0f;
            segmentPoint = default;
            distanceSqr = float.PositiveInfinity;
            return false;
        }

        Vector3 segment = segmentEnd - segmentStart;
        Vector3 offset = rayOrigin - segmentStart;
        float segmentLengthSqr = segment.sqrMagnitude;
        if (segmentLengthSqr < 0.000001f)
        {
            rayDistance = Mathf.Max(0f, Vector3.Dot(segmentStart - rayOrigin, rayDirection));
            segmentPoint = segmentStart;
            distanceSqr = (rayOrigin + rayDirection * rayDistance - segmentPoint).sqrMagnitude;
            return true;
        }

        float b = Vector3.Dot(rayDirection, segment);
        float d = Vector3.Dot(rayDirection, offset);
        float e = Vector3.Dot(segment, offset);
        float denominator = segmentLengthSqr - b * b;
        float segmentFraction = denominator > 0.000001f
            ? Mathf.Clamp01((e - b * d) / denominator)
            : Mathf.Clamp01(e / segmentLengthSqr);
        rayDistance = b * segmentFraction - d;
        if (rayDistance < 0f)
        {
            rayDistance = 0f;
            segmentFraction = Mathf.Clamp01(e / segmentLengthSqr);
        }
        segmentPoint = segmentStart + segment * segmentFraction;
        distanceSqr = (rayOrigin + rayDirection * rayDistance - segmentPoint).sqrMagnitude;
        return true;
    }

    private static void ArrayClearCandidates(int count)
    {
        for (int i = 0; i < count; i++) CandidateBuffer[i] = null;
    }

    private static void ArrayClearVisibility(int count)
    {
        for (int i = 0; i < count; i++) VisibilityBuffer[i] = default;
    }

    private static void ArrayClearOriginOverlaps(int count)
    {
        for (int i = 0; i < count; i++) OriginOverlapBuffer[i] = null;
    }

    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
