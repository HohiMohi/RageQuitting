using UnityEngine;
using UnityEngine.AI;

public class WaterShorelineSegment : MonoBehaviour
{
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;
    [SerializeField, Min(0.1f)] private float landSampleRadius = 1.25f;

    public Transform StartPoint => startPoint;
    public Transform EndPoint => endPoint;

    public bool TryGetClosestLandPosition(NavMeshAgent agent, out Vector3 position)
    {
        position = default;
        if (agent == null || startPoint == null || endPoint == null || !agent.isOnNavMesh)
        {
            return false;
        }

        Vector3 start = startPoint.position;
        Vector3 segment = endPoint.position - start;
        segment.y = 0f;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= 0.0001f)
        {
            return false;
        }

        Vector3 toAgent = agent.transform.position - start;
        toAgent.y = 0f;
        float t = Mathf.Clamp01(Vector3.Dot(toAgent, segment) / lengthSquared);
        Vector3 projected = Vector3.Lerp(startPoint.position, endPoint.position, t);
        int walkableArea = NavMesh.GetAreaFromName("Walkable");
        if (walkableArea < 0
            || !NavMesh.SamplePosition(projected, out NavMeshHit hit, landSampleRadius, 1 << walkableArea))
        {
            return false;
        }

        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        position = hit.position;
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (startPoint == null || endPoint == null)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 1f, 0.65f, 0.9f);
        Gizmos.DrawLine(startPoint.position, endPoint.position);
        Gizmos.DrawWireSphere(startPoint.position, 0.25f);
        Gizmos.DrawWireSphere(endPoint.position, 0.25f);
    }
}
