using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public readonly struct GoatStandingPose
{
    public readonly Vector3 LandingPosition;
    public readonly Quaternion LandingRotation;
    public readonly Vector3 ApproachPosition;
    public readonly Vector3 ExitPosition;

    public GoatStandingPose(
        Vector3 landingPosition,
        Quaternion landingRotation,
        Vector3 approachPosition,
        Vector3 exitPosition)
    {
        LandingPosition = landingPosition;
        LandingRotation = landingRotation;
        ApproachPosition = approachPosition;
        ExitPosition = exitPosition;
    }
}

public class GoatStandingSurface : MonoBehaviour
{
    [SerializeField] private Transform landingPoint;
    [SerializeField] private Transform[] approachPoints;

    public bool TryGetStandingPose(
        NPCBrain brain,
        Transform targetRoot,
        float maxJumpHeight,
        float maxJumpDistance,
        float landingClearance,
        out GoatStandingPose pose)
    {
        return GoatStandingSurfaceResolver.TryResolve(
            brain,
            targetRoot,
            landingPoint,
            approachPoints,
            maxJumpHeight,
            maxJumpDistance,
            landingClearance,
            out pose);
    }
}

public static class GoatStandingSurfaceResolver
{
    private const float MinimumTopNormal = 0.65f;
    private const int AutomaticApproachCount = 8;

    public static bool TryResolve(
        NPCBrain brain,
        Transform targetRoot,
        float maxJumpHeight,
        float maxJumpDistance,
        float landingClearance,
        out GoatStandingPose pose)
    {
        return TryResolve(
            brain,
            targetRoot,
            null,
            null,
            maxJumpHeight,
            maxJumpDistance,
            landingClearance,
            out pose);
    }

    public static bool TryResolve(
        NPCBrain brain,
        Transform targetRoot,
        Transform explicitLandingPoint,
        Transform[] explicitApproachPoints,
        float maxJumpHeight,
        float maxJumpDistance,
        float landingClearance,
        out GoatStandingPose pose)
    {
        pose = default;
        if (brain == null || targetRoot == null || brain.Agent == null)
        {
            return false;
        }

        Collider[] allTargetColliders = targetRoot.GetComponentsInChildren<Collider>(false);
        List<Collider> validTargetColliders = new List<Collider>();
        foreach (Collider targetCollider in allTargetColliders)
        {
            if (targetCollider != null && targetCollider.enabled && !targetCollider.isTrigger)
            {
                validTargetColliders.Add(targetCollider);
            }
        }

        if (validTargetColliders.Count == 0)
        {
            return false;
        }

        Collider[] targetColliders = validTargetColliders.ToArray();
        Bounds bounds = targetColliders[0].bounds;
        for (int i = 1; i < targetColliders.Length; i++)
        {
            bounds.Encapsulate(targetColliders[i].bounds);
        }

        Vector3 landingPosition;
        Quaternion landingRotation;
        if (explicitLandingPoint != null)
        {
            landingPosition = explicitLandingPoint.position;
            landingRotation = explicitLandingPoint.rotation;
        }
        else if (!TryFindAutomaticLanding(targetRoot, targetColliders, bounds, landingClearance, out landingPosition, out landingRotation))
        {
            return false;
        }

        if (!HasGoatClearance(brain, targetRoot, landingPosition))
        {
            return false;
        }

        List<Vector3> approachCandidates = new List<Vector3>();
        if (explicitApproachPoints != null)
        {
            foreach (Transform approachPoint in explicitApproachPoints)
            {
                if (approachPoint != null)
                {
                    approachCandidates.Add(approachPoint.position);
                }
            }
        }

        if (approachCandidates.Count == 0)
        {
            float approachRadius = Mathf.Max(bounds.extents.x, bounds.extents.z)
                + brain.Agent.radius
                + 0.3f;
            for (int i = 0; i < AutomaticApproachCount; i++)
            {
                float angle = i * Mathf.PI * 2f / AutomaticApproachCount;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                approachCandidates.Add(new Vector3(bounds.center.x, brain.transform.position.y, bounds.center.z)
                    + direction * approachRadius);
            }
        }

        if (!TryFindBestApproach(
                brain,
                landingPosition,
                approachCandidates,
                maxJumpHeight,
                maxJumpDistance,
                out Vector3 approachPosition))
        {
            return false;
        }

        Vector3 facing = Vector3.ProjectOnPlane(landingPosition - approachPosition, Vector3.up);
        if (facing.sqrMagnitude > 0.0001f)
        {
            landingRotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
        }

        pose = new GoatStandingPose(landingPosition, landingRotation, approachPosition, approachPosition);
        return true;
    }

    private static bool TryFindAutomaticLanding(
        Transform targetRoot,
        Collider[] targetColliders,
        Bounds bounds,
        float landingClearance,
        out Vector3 landingPosition,
        out Quaternion landingRotation)
    {
        landingPosition = default;
        landingRotation = Quaternion.identity;
        Vector3 rayOrigin = new Vector3(bounds.center.x, bounds.max.y + 0.25f, bounds.center.z);
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            bounds.size.y + 0.5f,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.normal.y < MinimumTopNormal || !IsTargetCollider(hit.collider, targetRoot, targetColliders))
            {
                continue;
            }

            landingPosition = hit.point + Vector3.up * Mathf.Max(0.01f, landingClearance);
            Vector3 forward = Vector3.ProjectOnPlane(targetRoot.forward, hit.normal);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(Vector3.forward, hit.normal);
            }

            landingRotation = Quaternion.LookRotation(forward.normalized, hit.normal);
            return true;
        }

        return false;
    }

    private static bool IsTargetCollider(Collider collider, Transform targetRoot, Collider[] targetColliders)
    {
        if (collider == null || !collider.transform.IsChildOf(targetRoot))
        {
            return false;
        }

        foreach (Collider targetCollider in targetColliders)
        {
            if (targetCollider == collider)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGoatClearance(NPCBrain brain, Transform targetRoot, Vector3 rootPosition)
    {
        CapsuleCollider capsule = brain.GetComponent<CapsuleCollider>();
        float radius = capsule != null ? capsule.radius : brain.Agent.radius;
        float height = capsule != null ? Mathf.Max(capsule.height, radius * 2f) : Mathf.Max(brain.Agent.height, radius * 2f);
        Vector3 centerOffset = capsule != null ? capsule.center : Vector3.up * (height * 0.5f);
        Vector3 center = rootPosition + centerOffset;
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 point1 = center + Vector3.up * halfSegment;
        Vector3 point2 = center - Vector3.up * halfSegment;

        Collider[] overlaps = Physics.OverlapCapsule(
            point1,
            point2,
            Mathf.Max(0.05f, radius * 0.92f),
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null
                || overlap.transform.IsChildOf(brain.transform)
                || overlap.transform.IsChildOf(targetRoot))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryFindBestApproach(
        NPCBrain brain,
        Vector3 landingPosition,
        List<Vector3> candidates,
        float maxJumpHeight,
        float maxJumpDistance,
        out Vector3 approachPosition)
    {
        approachPosition = default;
        float bestDistance = float.PositiveInfinity;
        int areaMask = brain.Agent.areaMask;
        NavMeshPath path = new NavMeshPath();

        foreach (Vector3 candidate in candidates)
        {
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 1.5f, areaMask))
            {
                continue;
            }

            float verticalDistance = landingPosition.y - navHit.position.y;
            float horizontalDistance = Vector3.ProjectOnPlane(landingPosition - navHit.position, Vector3.up).magnitude;
            if (verticalDistance < -0.5f
                || verticalDistance > Mathf.Max(0.1f, maxJumpHeight)
                || horizontalDistance > Mathf.Max(0.1f, maxJumpDistance))
            {
                continue;
            }

            if (!NavMesh.CalculatePath(brain.transform.position, navHit.position, areaMask, path)
                || path.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }

            float distance = GetPathLength(path);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            approachPosition = navHit.position;
        }

        return !float.IsPositiveInfinity(bestDistance);
    }

    private static float GetPathLength(NavMeshPath path)
    {
        float length = 0f;
        Vector3[] corners = path.corners;
        for (int i = 1; i < corners.Length; i++)
        {
            length += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return length;
    }
}
