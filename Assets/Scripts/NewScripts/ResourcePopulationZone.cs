using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ResourcePopulationZone : MonoBehaviour
{
    [Header("Population")]
    [SerializeField] private BaseResourceSO resourceType;
    [SerializeField, Min(0)] private int minimumAvailableCount = 1;
    [SerializeField, Min(0.1f)] private float populationCheckInterval = 2f;
    [SerializeField, Min(0f)] private float replenishmentCooldown = 15f;

    [Header("Zone")]
    [SerializeField] private Vector3 zoneSize = new Vector3(10f, 4f, 10f);
    [SerializeField] private LayerMask resourceDetectionLayers = ~0;
    [SerializeField] private LayerMask spawnSurfaceLayers = 1;

    [Header("Spawn validation")]
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [SerializeField, Range(0f, 1f)] private float minimumSurfaceUpDot = 0.7f;
    [SerializeField, Min(0f)] private float spawnVerticalOffset = 0.02f;
    [SerializeField, Min(0f)] private float spawnClearanceRadius = 0.75f;
    [SerializeField, Min(0f)] private float spawnClearanceHeight = 0.75f;
    [SerializeField, Min(1)] private int maximumSpawnAttempts = 12;
    [SerializeField] private bool alignToSurfaceNormal = true;
    [SerializeField] private bool randomizeYaw = true;

    [Header("Runtime debug")]
    [SerializeField] private int currentAvailableCount;
    [SerializeField] private bool replenishmentPending;
    [SerializeField] private float remainingCooldown;

    private readonly HashSet<BaseResourceNew> countedResources = new HashSet<BaseResourceNew>();
    private float nextPopulationCheckTime;
    private float replenishmentReadyTime;

    public BaseResourceSO ResourceType => resourceType;
    public int MinimumAvailableCount => Mathf.Max(0, minimumAvailableCount);
    public int CurrentAvailableCount => currentAvailableCount;
    public bool IsReplenishmentPending => replenishmentPending;
    public float RemainingCooldown => replenishmentPending ? Mathf.Max(0f, replenishmentReadyTime - Time.time) : 0f;

    private void OnEnable()
    {
        ResetRuntimeState();
    }

    private void Update()
    {
        if (!HasPopulationAuthority())
        {
            return;
        }

        remainingCooldown = RemainingCooldown;
        if (Time.time < nextPopulationCheckTime)
        {
            return;
        }

        nextPopulationCheckTime = Time.time + Mathf.Max(0.1f, populationCheckInterval);
        CheckPopulationNow();
    }

    public void CheckPopulationNow()
    {
        if (!HasPopulationAuthority() || resourceType == null || resourceType.resourcePrefab == null)
        {
            return;
        }

        currentAvailableCount = CountAvailableResources();
        if (currentAvailableCount >= MinimumAvailableCount)
        {
            CancelPendingReplenishment();
            return;
        }

        if (!replenishmentPending)
        {
            replenishmentPending = true;
            replenishmentReadyTime = Time.time + Mathf.Max(0f, replenishmentCooldown);
            remainingCooldown = RemainingCooldown;
            return;
        }

        if (Time.time < replenishmentReadyTime)
        {
            remainingCooldown = RemainingCooldown;
            return;
        }

        if (!TryFindSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation)
            || !BaseResourceSpawnUtility.TrySpawnResource(resourceType, spawnPosition, spawnRotation, out GameObject _))
        {
            remainingCooldown = 0f;
            return;
        }

        currentAvailableCount++;
        if (currentAvailableCount < MinimumAvailableCount)
        {
            replenishmentPending = true;
            replenishmentReadyTime = Time.time + Mathf.Max(0f, replenishmentCooldown);
            remainingCooldown = RemainingCooldown;
        }
        else
        {
            CancelPendingReplenishment();
        }
    }

    private int CountAvailableResources()
    {
        countedResources.Clear();
        Vector3 halfExtents = GetHalfExtents();
        Collider[] colliders = Physics.OverlapBox(
            transform.position,
            halfExtents,
            transform.rotation,
            resourceDetectionLayers,
            QueryTriggerInteraction.Collide);

        foreach (Collider collider in colliders)
        {
            BaseResourceNew resource = collider.GetComponentInParent<BaseResourceNew>();
            if (resource == null
                || !resource.gameObject.activeInHierarchy
                || resource.IsPickedUp
                || resource.GetBaseResourceSO() != resourceType)
            {
                continue;
            }

            countedResources.Add(resource);
        }

        return countedResources.Count;
    }

    private bool TryFindSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        Vector3 halfExtents = GetHalfExtents();
        Vector3 up = transform.up.normalized;
        int attemptCount = Mathf.Max(1, maximumSpawnAttempts);

        for (int attempt = 0; attempt < attemptCount; attempt++)
        {
            Vector3 localRayOrigin = new Vector3(
                Random.Range(-halfExtents.x, halfExtents.x),
                halfExtents.y,
                Random.Range(-halfExtents.z, halfExtents.z));
            Vector3 rayOrigin = transform.TransformPoint(localRayOrigin);
            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigin,
                -up,
                halfExtents.y * 2f,
                spawnSurfaceLayers,
                QueryTriggerInteraction.Ignore);

            if (hits.Length == 0)
            {
                continue;
            }

            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (!IsValidSpawnSurface(hit.collider)
                    || Vector3.Dot(hit.normal.normalized, up) < minimumSurfaceUpDot
                    || !HasSpawnClearance(hit, up))
                {
                    continue;
                }

                spawnPosition = hit.point + up * Mathf.Max(0f, spawnVerticalOffset);
                float yaw = randomizeYaw ? Random.Range(0f, 360f) : 0f;
                Quaternion surfaceAlignment = alignToSurfaceNormal
                    ? Quaternion.FromToRotation(Vector3.up, hit.normal.normalized)
                    : Quaternion.identity;
                spawnRotation = surfaceAlignment * Quaternion.AngleAxis(yaw, Vector3.up);
                return true;
            }
        }

        spawnPosition = default;
        spawnRotation = default;
        return false;
    }

    private bool IsValidSpawnSurface(Collider surfaceCollider)
    {
        if (surfaceCollider == null
            || surfaceCollider.transform.IsChildOf(transform)
            || surfaceCollider.GetComponentInParent<BaseResourceNew>() != null
            || surfaceCollider.GetComponentInParent<MountableBridgeComponent>() != null)
        {
            return false;
        }

        // Dynamic and kinematic gameplay objects must not be treated as terrain.
        return surfaceCollider.attachedRigidbody == null;
    }

    private bool HasSpawnClearance(RaycastHit surfaceHit, Vector3 up)
    {
        if (spawnClearanceRadius <= 0f)
        {
            return true;
        }

        Vector3 center = surfaceHit.point + up * (Mathf.Max(0f, spawnVerticalOffset) + Mathf.Max(0f, spawnClearanceHeight));
        Collider[] blockers = Physics.OverlapSphere(
            center,
            spawnClearanceRadius,
            obstacleLayers,
            QueryTriggerInteraction.Collide);

        foreach (Collider blocker in blockers)
        {
            if (blocker == null || blocker == surfaceHit.collider || blocker.transform.IsChildOf(transform))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private bool HasPopulationAuthority()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null || !networkManager.IsListening || networkManager.IsServer;
    }

    private Vector3 GetHalfExtents()
    {
        Vector3 size = new Vector3(
            Mathf.Max(0.1f, zoneSize.x),
            Mathf.Max(0.1f, zoneSize.y),
            Mathf.Max(0.1f, zoneSize.z));
        Vector3 scale = transform.lossyScale;
        return Vector3.Scale(size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
    }

    private void CancelPendingReplenishment()
    {
        replenishmentPending = false;
        replenishmentReadyTime = 0f;
        remainingCooldown = 0f;
    }

    private void ResetRuntimeState()
    {
        currentAvailableCount = 0;
        replenishmentPending = false;
        replenishmentReadyTime = 0f;
        remainingCooldown = 0f;
        nextPopulationCheckTime = Time.time;
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Gizmos.color = Application.isPlaying && replenishmentPending
            ? new Color(1f, 0.65f, 0.1f, 0.9f)
            : new Color(0.2f, 0.85f, 0.45f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(
            Mathf.Max(0.1f, zoneSize.x),
            Mathf.Max(0.1f, zoneSize.y),
            Mathf.Max(0.1f, zoneSize.z)));
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
