using UnityEngine;
using UnityEngine.AI;

public class NPCDownedPlayerDropPoint : MonoBehaviour
{
    [SerializeField] private Transform dropPoint;
    [SerializeField, Min(0.1f)] private float searchRadius = 2f;
    [SerializeField, Min(1)] private int sampleAttempts = 12;
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [SerializeField, Min(0f)] private float capsuleClearance = 0.05f;

    public Vector3 Position => dropPoint != null ? dropPoint.position : transform.position;

    public bool TryGetSafeDropPosition(DownedPlayerCarryable player, Transform carrier, out Vector3 position)
    {
        position = default;
        if (player == null)
        {
            return false;
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        float radius = controller != null ? controller.radius : 0.4f;
        float height = controller != null ? Mathf.Max(controller.height, radius * 2f) : 1.8f;
        Vector3 center = controller != null ? controller.center : Vector3.up * height * 0.5f;

        int attempts = Mathf.Max(1, sampleAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset2D = i == 0 ? Vector2.zero : Random.insideUnitCircle * searchRadius;
            Vector3 candidate = Position + new Vector3(offset2D.x, 0f, offset2D.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, searchRadius, NavMesh.AllAreas))
            {
                continue;
            }

            Vector3 rootPosition = navHit.position + Vector3.up * capsuleClearance;
            Vector3 worldCenter = rootPosition + center;
            float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 top = worldCenter + Vector3.up * halfSegment;
            Vector3 bottom = worldCenter - Vector3.up * halfSegment;
            Collider[] overlaps = Physics.OverlapCapsule(
                top,
                bottom,
                radius + capsuleClearance,
                obstacleLayers,
                QueryTriggerInteraction.Ignore);
            bool blocked = false;
            foreach (Collider overlap in overlaps)
            {
                if (overlap == null
                    || overlap.transform.IsChildOf(player.transform)
                    || (carrier != null && overlap.transform.IsChildOf(carrier)))
                {
                    continue;
                }

                blocked = true;
                break;
            }

            if (blocked)
            {
                continue;
            }

            position = rootPosition;
            return true;
        }

        return false;
    }
}
