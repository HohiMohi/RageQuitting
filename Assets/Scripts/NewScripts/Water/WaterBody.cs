using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class WaterBody : MonoBehaviour
{
    private static readonly HashSet<WaterBody> activeBodies = new HashSet<WaterBody>();

    [SerializeField] private WaterBodyProfileSO profile;
    [SerializeField] private BoxCollider gameplayVolume;
    [SerializeField] private Transform surfaceReference;
    [SerializeField] private WaterShorelineSegment[] shorelineSegments;
    [SerializeField] private Transform[] bankExitPoints;

    public static IReadOnlyCollection<WaterBody> ActiveBodies => activeBodies;
    public WaterBodyProfileSO Profile => profile;
    public float SurfaceHeight => surfaceReference != null ? surfaceReference.position.y : transform.position.y;

    private void OnEnable() => activeBodies.Add(this);
    private void OnDisable() => activeBodies.Remove(this);

    public bool Contains(Vector3 worldPosition)
    {
        if (gameplayVolume == null || !gameplayVolume.enabled || !gameplayVolume.gameObject.activeInHierarchy)
        {
            return false;
        }

        Vector3 local = gameplayVolume.transform.InverseTransformPoint(worldPosition) - gameplayVolume.center;
        Vector3 half = gameplayVolume.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x
            && Mathf.Abs(local.y) <= half.y
            && Mathf.Abs(local.z) <= half.z;
    }

    public bool TryGetClosestExitPosition(NavMeshAgent agent, out Vector3 position)
    {
        position = default;
        if (agent == null)
        {
            return false;
        }

        float bestDistance = float.PositiveInfinity;
        if (shorelineSegments != null)
        {
            foreach (WaterShorelineSegment segment in shorelineSegments)
            {
                if (segment == null
                    || !segment.TryGetClosestLandPosition(agent, out Vector3 candidate)
                    || !TryGetPathLength(agent, candidate, out float pathLength)
                    || pathLength >= bestDistance)
                {
                    continue;
                }

                bestDistance = pathLength;
                position = candidate;
            }
        }

        if (bestDistance < float.PositiveInfinity)
        {
            return true;
        }

        return TryGetClosestLegacyExitPosition(agent, out position);
    }

    private bool TryGetClosestLegacyExitPosition(NavMeshAgent agent, out Vector3 position)
    {
        position = default;
        if (bankExitPoints == null)
        {
            return false;
        }

        float bestDistance = float.PositiveInfinity;
        foreach (Transform exitPoint in bankExitPoints)
        {
            if (exitPoint == null
                || !NavMesh.SamplePosition(exitPoint.position, out NavMeshHit hit, 2f, agent.areaMask)
                || !TryGetPathLength(agent, hit.position, out float pathLength)
                || pathLength >= bestDistance)
            {
                continue;
            }

            bestDistance = pathLength;
            position = hit.position;
        }

        return bestDistance < float.PositiveInfinity;
    }

    private static bool TryGetPathLength(NavMeshAgent agent, Vector3 destination, out float length)
    {
        length = 0f;
        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(destination, path)
            || path.status != NavMeshPathStatus.PathComplete
            || path.corners == null
            || path.corners.Length == 0)
        {
            return false;
        }

        Vector3 previous = agent.transform.position;
        foreach (Vector3 corner in path.corners)
        {
            length += Vector3.Distance(previous, corner);
            previous = corner;
        }

        return true;
    }

    public static bool TryGetContaining(Vector3 worldPosition, out WaterBody waterBody)
    {
        waterBody = null;
        foreach (WaterBody candidate in activeBodies)
        {
            if (candidate != null && candidate.Contains(worldPosition))
            {
                waterBody = candidate;
                return true;
            }
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (gameplayVolume != null)
        {
            Gizmos.color = new Color(0f, 0.65f, 1f, 0.22f);
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = gameplayVolume.transform.localToWorldMatrix;
            Gizmos.DrawCube(gameplayVolume.center, gameplayVolume.size);
            Gizmos.color = new Color(0f, 0.85f, 0.35f, 0.7f);
            float safeY = gameplayVolume.transform.InverseTransformPoint(
                new Vector3(
                    transform.position.x,
                    SurfaceHeight - (profile != null ? profile.MaximumSafeWadingDepth : 1.2f),
                    transform.position.z)).y;
            Gizmos.DrawWireCube(
                new Vector3(gameplayVolume.center.x, safeY, gameplayVolume.center.z),
                new Vector3(gameplayVolume.size.x, 0.03f, gameplayVolume.size.z));
            Gizmos.matrix = previous;
        }

        if (bankExitPoints == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        foreach (Transform exitPoint in bankExitPoints)
        {
            if (exitPoint != null)
            {
                Gizmos.DrawWireSphere(exitPoint.position, 0.35f);
            }
        }
    }
}
