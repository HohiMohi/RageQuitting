using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BucketRespawnPoint : MonoBehaviour
{
    private static readonly List<BucketRespawnPoint> Points = new List<BucketRespawnPoint>();
    [SerializeField] private int bucketIndex;
    [SerializeField] private float occupancyRadius = 0.6f;

    private void OnEnable() => Points.Add(this);
    private void OnDisable() => Points.Remove(this);

    public static bool TryGetReturnPose(int preferredIndex, out Vector3 position, out Quaternion rotation)
    {
        BucketRespawnPoint point = Points.Find(candidate => candidate != null && candidate.bucketIndex == preferredIndex);
        point ??= FindAvailablePoint();
        if (point == null)
        {
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        position = point.transform.position;
        rotation = point.transform.rotation;
        return true;
    }

    private static BucketRespawnPoint FindAvailablePoint()
    {
        Points.Sort((a, b) => a.bucketIndex.CompareTo(b.bucketIndex));
        foreach (BucketRespawnPoint point in Points)
        {
            if (point != null && Physics.OverlapSphere(point.transform.position, point.occupancyRadius).Length == 0) return point;
        }
        return Points.Count > 0 ? Points[0] : null;
    }
}
