using System.Collections.Generic;
using UnityEngine;

public sealed class EquippableItemRespawnPoint : MonoBehaviour
{
    private static readonly HashSet<EquippableItemRespawnPoint> ActivePoints = new HashSet<EquippableItemRespawnPoint>();

    [SerializeField] private EquippableItemType itemType;
    [SerializeField] private int priority;
    [SerializeField, Min(0.1f)] private float occupancyRadius = 0.65f;

    public EquippableItemType ItemType => itemType;
    public int Priority => priority;
    public float OccupancyRadius => occupancyRadius;
    public static IReadOnlyCollection<EquippableItemRespawnPoint> Points => ActivePoints;

    private void OnEnable() => ActivePoints.Add(this);
    private void OnDisable() => ActivePoints.Remove(this);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 0.95f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, occupancyRadius);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.75f);
    }
}
