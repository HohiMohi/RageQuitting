using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(BoxCollider))]
public class GoatPushZone : MonoBehaviour
{
    private static readonly HashSet<GoatPushZone> ActiveZones = new HashSet<GoatPushZone>();

    [SerializeField] private Transform approachPoint;
    [SerializeField] private Vector3 localPushDirection = Vector3.forward;
    [SerializeField] private ExternalImpulseProfileSO pushImpulseProfile;
    [SerializeField] private float setupPositionSampleRadius = 0.75f;
    [SerializeField] private bool requirePlayerOnPushSide = true;
    [SerializeField, Range(-1f, 1f)] private float minimumPushSideDot = 0f;

    private BoxCollider zoneCollider;

    public Vector3 ApproachPosition => approachPoint != null ? approachPoint.position : transform.position;
    public Vector3 PushDirection => transform.TransformDirection(localPushDirection).normalized;
    public ExternalImpulseProfileSO PushImpulseProfile => pushImpulseProfile;

    public static IReadOnlyCollection<GoatPushZone> Zones => ActiveZones;

    private void Awake()
    {
        zoneCollider = GetComponent<BoxCollider>();
        zoneCollider.isTrigger = true;
    }

    private void OnEnable()
    {
        ActiveZones.Add(this);
    }

    private void OnDisable()
    {
        ActiveZones.Remove(this);
    }

    public bool ContainsPlayer(PlayerHealth player)
    {
        if (player == null || player.IsDowned || !isActiveAndEnabled)
        {
            return false;
        }

        if (zoneCollider == null)
        {
            zoneCollider = GetComponent<BoxCollider>();
        }

        Vector3 closestPoint = zoneCollider.ClosestPoint(player.transform.position);
        if ((closestPoint - player.transform.position).sqrMagnitude > 0.0001f)
        {
            return false;
        }

        if (!requirePlayerOnPushSide)
        {
            return true;
        }

        Vector3 toPlayer = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up);
        return toPlayer.sqrMagnitude > 0.0001f
            && Vector3.Dot(toPlayer.normalized, PushDirection) >= minimumPushSideDot;
    }

    public bool TryGetPushSetupPosition(PlayerHealth player, float distance, out Vector3 position)
    {
        position = default;
        if (!ContainsPlayer(player))
        {
            return false;
        }

        Vector3 desiredPosition = player.transform.position
            - PushDirection * Mathf.Max(0.1f, distance);
        if (!NavMesh.SamplePosition(
            desiredPosition,
            out NavMeshHit hit,
            Mathf.Max(0.1f, setupPositionSampleRadius),
            NavMesh.AllAreas))
        {
            return false;
        }

        position = hit.position;
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.25f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = previousMatrix;

        Vector3 origin = ApproachPosition;
        Gizmos.DrawLine(origin, origin + PushDirection * 2f);
        Gizmos.DrawSphere(origin, 0.15f);
    }
}
