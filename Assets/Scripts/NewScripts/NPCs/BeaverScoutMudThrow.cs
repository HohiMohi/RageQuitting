using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using System;

/// <summary>Server-only windup and release logic for a Beaver Scout mudball.</summary>
[DisallowMultipleComponent]
public sealed class BeaverScoutMudThrow : MonoBehaviour
{
    [SerializeField] private Transform releasePoint;
    [SerializeField, Min(0.05f)] private float horizontalSpeed = 12f;
    [SerializeField, Min(0.05f)] private float minimumFlightTime = 0.25f;
    [SerializeField, Min(0.05f)] private float projectileLifetime = 3f;
    private NPCBrain brain;
    private NavMeshAgent agent;
    private PlayerHealth lockedTarget;
    private Transform lockedTargetTransform;
    private float windupEndsAt;
    private float nextThrowAt;
    private bool windingUp;
    private float windupDuration;
    private float throwRange;
    private bool requireLineOfSight;
    private LayerMask lineOfSightMask;
    private float cooldown;
    private BeaverScoutMudball projectilePrefab;
    private float mudGainPercentagePoints;
    private ExternalImpulseProfileSO impulseProfile;
    private Func<bool> scanContextValid;
    private bool needsLegacyScanAfterWindup;

    public bool IsWindingUp => windingUp;
    public float NextThrowAt => nextThrowAt;

    public bool ConsumeLegacyScanAfterWindup()
    {
        if (!needsLegacyScanAfterWindup) return false;
        needsLegacyScanAfterWindup = false;
        return true;
    }

    public bool CanTarget(PlayerHealth target, Transform targetTransform, float range, bool lineOfSight, LayerMask mask)
    {
        return IsServerAuthority() && IsTargetValid(target, targetTransform, range, lineOfSight, mask);
    }

    private void Awake()
    {
        TryGetComponent(out brain);
        TryGetComponent(out agent);
    }

    private void OnDisable() => CancelWindup();

    private void Update()
    {
        if (!windingUp) return;
        if (brain == null || !IsServerAuthority() || brain.IsUnderExternalControl ||
            brain.Health == null || brain.Health.IsDead || !isActiveAndEnabled ||
            scanContextValid != null && !scanContextValid())
        {
            CancelWindup();
            return;
        }

        if (!IsTargetValid(lockedTarget, lockedTargetTransform, throwRange, requireLineOfSight, lineOfSightMask))
        {
            CancelWindup();
            return;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        FaceTarget(lockedTargetTransform.position);
        if (Time.time < windupEndsAt) return;
        if (!TryLaunch(lockedTarget, lockedTargetTransform))
        {
            CancelWindup();
            return;
        }

        nextThrowAt = Time.time + Mathf.Max(0f, cooldown);
        CancelWindup();
    }

    public bool TryBeginWindup(PlayerHealth target, Transform targetTransform, float configuredRange,
        bool configuredLineOfSight, LayerMask configuredMask, float configuredWindup, float configuredCooldown,
        BeaverScoutMudball configuredProjectilePrefab, float configuredMudGain, ExternalImpulseProfileSO configuredImpulseProfile,
        Func<bool> configuredScanContext)
    {
        if (windingUp || Time.time < nextThrowAt || !IsServerAuthority() || configuredProjectilePrefab == null ||
            !IsTargetValid(target, targetTransform, configuredRange, configuredLineOfSight, configuredMask)) return false;

        throwRange = Mathf.Max(0.1f, configuredRange);
        requireLineOfSight = configuredLineOfSight;
        lineOfSightMask = configuredMask;
        windupDuration = Mathf.Max(0f, configuredWindup);
        cooldown = Mathf.Max(0f, configuredCooldown);
        projectilePrefab = configuredProjectilePrefab;
        mudGainPercentagePoints = Mathf.Max(0f, configuredMudGain);
        impulseProfile = configuredImpulseProfile;
        scanContextValid = configuredScanContext;
        lockedTarget = target;
        lockedTargetTransform = targetTransform;
        windingUp = true;
        windupEndsAt = Time.time + windupDuration;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
        FaceTarget(targetTransform.position);
        return true;
    }

    public void CancelWindup()
    {
        if (windingUp) needsLegacyScanAfterWindup = true;
        windingUp = false;
        lockedTarget = null;
        lockedTargetTransform = null;
        scanContextValid = null;
    }

    private bool TryLaunch(PlayerHealth target, Transform targetTransform)
    {
        if (releasePoint == null || !IsTargetValid(target, targetTransform, throwRange, requireLineOfSight, lineOfSightMask)) return false;
        Vector3 origin = releasePoint.position;
        Vector3 aim = GetTargetCenter(target, targetTransform);
        Vector3 displacement = aim - origin;
        Vector3 horizontal = Vector3.ProjectOnPlane(displacement, Vector3.up);
        float speed = Mathf.Max(0.1f, horizontalSpeed);
        float flightTime = Mathf.Max(Mathf.Max(0.05f, minimumFlightTime), horizontal.magnitude / speed);
        Vector3 velocity = displacement / flightTime - 0.5f * Physics.gravity * flightTime;

        BeaverScoutMudball ball = Instantiate(projectilePrefab, origin, Quaternion.identity);
        ball.ConfigureImpact(mudGainPercentagePoints, impulseProfile);
        TryGetComponent(out NetworkObject ownerNetworkObject);
        ball.Initialize(velocity, ownerNetworkObject, Mathf.Max(0.1f, projectileLifetime));
        ball.TryGetComponent(out NetworkObject networkObject);
        if (networkObject == null)
        {
            Destroy(ball.gameObject);
            return false;
        }
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && !networkObject.IsSpawned)
            networkObject.Spawn(true);
        ball.Launch(velocity);
        return true;
    }

    private bool IsTargetValid(PlayerHealth target, Transform targetTransform, float range, bool lineOfSight, LayerMask mask)
    {
        if (target == null || targetTransform == null || !target.gameObject.activeInHierarchy || target.IsDowned ||
            releasePoint == null) return false;
        float maxRange = Mathf.Min(brain != null ? brain.DetectionRadius : 14f, Mathf.Max(0.1f, range));
        Vector3 origin = releasePoint.position;
        Vector3 center = GetTargetCenter(target, targetTransform);
        Vector3 scanOrigin = brain != null ? brain.transform.position : transform.position;
        if ((targetTransform.position - scanOrigin).sqrMagnitude > maxRange * maxRange) return false;
        if (!lineOfSight) return true;
        Vector3 delta = center - origin;
        if (delta.sqrMagnitude < 0.0001f) return true;
        RaycastHit[] hits = Physics.RaycastAll(origin, delta.normalized, delta.magnitude,
            mask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.root == transform.root) continue;
            return hit.collider.transform.root == target.transform.root;
        }
        return true;
    }

    private static Vector3 GetTargetCenter(PlayerHealth target, Transform targetTransform)
    {
        CharacterController controller = target.GetComponent<CharacterController>();
        if (controller != null && controller.enabled) return controller.bounds.center;
        CapsuleCollider[] capsules = target.GetComponentsInChildren<CapsuleCollider>();
        foreach (CapsuleCollider capsule in capsules)
            if (capsule != null && capsule.enabled) return capsule.bounds.center;
        return targetTransform.position + Vector3.up * 0.9f;
    }

    private void FaceTarget(Vector3 position)
    {
        Vector3 direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 720f * Time.deltaTime);
    }

    private bool IsServerAuthority()
    {
        TryGetComponent(out NetworkObject netObject);
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening ||
               brain != null && brain.IsSpawned && brain.IsServer && netObject != null && netObject.IsSpawned;
    }
}
