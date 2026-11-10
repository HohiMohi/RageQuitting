using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TumbleweedMovement))]
public sealed class TumbleweedCaptureController : NetworkBehaviour
{
    [SerializeField, Min(1)] private int capacity = 10;
    [SerializeField, Min(0.1f)] private float captureDuration = 15f;
    [SerializeField, Min(0f)] private float releaseImmunity = 5f;
    private readonly Collider[] overlapResults = new Collider[128];
    private readonly Dictionary<int, Captive> captives = new();
    private readonly HashSet<int> reservations = new();
    private bool[] occupiedSlots;
    private TumbleweedMovement movement;
    private bool IsAuthority => IsSpawned ? IsServer
        : NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
    private double Now => IsSpawned && NetworkManager != null && NetworkManager.IsListening
        ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
    public int CurrentCaptiveCount => captives.Count;
    public int OccupiedSlotCount
    {
        get
        {
            int count = 0;
            if (occupiedSlots != null)
                foreach (bool occupied in occupiedSlots)
                    if (occupied) count++;
            return count;
        }
    }

    private sealed class Captive
    {
        public Transform target;
        public Vector3 lastSafePosition;
        public Quaternion lastSafeRotation;
        public Rigidbody body;
        public bool wasKinematic;
        public Collider[] colliders;
        public bool[] enabled;
        public PlayerTemporaryStatusController status;
        public PlayerHealth health;
        public EquippableWorldPhysics tool;
        public Bounds cachedToolBounds;
        public Vector3 cachedToolCenterOffset;
        public Bounds cachedTargetBounds;
        public Vector3 capsuleCenter;
        public float capsuleHeight;
        public float capsuleRadius;
        public double releaseAt;
        public Vector3 offset;
        public Quaternion relativeRotation;
        public int slot;
    }

    private void Awake()
    {
        movement = GetComponent<TumbleweedMovement>();
        EnsureSlotCapacity();
    }

    private void FixedUpdate()
    {
        if (!IsAuthority) return;
        ScanForTargets();
        List<int> release = null;
        foreach (var pair in captives)
        {
            Captive c = pair.Value;
            if (c.target == null || Now >= c.releaseAt) { release ??= new(); release.Add(pair.Key); continue; }
            Vector3 position = transform.position + transform.rotation * c.offset;
            Quaternion rotation = transform.rotation * c.relativeRotation;
            c.target.SetPositionAndRotation(position, rotation);
            Vector3 horizontalRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            if (horizontalRight.sqrMagnitude < 0.01f) horizontalRight = Vector3.right;
            if (TryFindReleasePosition(c, position + horizontalRight * 1.1f + Vector3.up * 0.8f, out Vector3 safeExit))
            {
                c.lastSafePosition = safeExit;
            }
        }
        if (release != null) foreach (int id in release) Release(id);
    }

    public override void OnNetworkDespawn()
    {
        foreach (int key in new List<int>(captives.Keys)) Release(key);
        base.OnNetworkDespawn();
    }

    private void ScanForTargets()
    {
        TumbleweedCaptureSurface surface = GetComponentInChildren<TumbleweedCaptureSurface>();
        if (surface == null) return;
        int count = Physics.OverlapSphereNonAlloc(surface.WorldPosition, surface.WorldRadius, overlapResults,
            Physics.AllLayers, QueryTriggerInteraction.Collide);
        Collider[] overflow = count == overlapResults.Length
            ? Physics.OverlapSphere(surface.WorldPosition, surface.WorldRadius, Physics.AllLayers, QueryTriggerInteraction.Collide)
            : null;
        if (overflow != null) count = overflow.Length;
        for (int i = 0; i < count && captives.Count < Mathf.Max(1, capacity); i++)
        {
            Collider hit = overflow != null ? overflow[i] : overlapResults[i];
            if (overflow == null) overlapResults[i] = null;
            if (hit == null) continue;
            PlayerTemporaryStatusController status = hit.GetComponentInParent<PlayerTemporaryStatusController>();
            EquippableWorldPhysics tool = hit.GetComponentInParent<EquippableWorldPhysics>();
            Transform target = status != null ? status.transform : tool != null ? tool.transform : null;
            if (target == null) continue;
            if (IsSpawned)
            {
                NetworkObject candidateObject = target.GetComponent<NetworkObject>();
                if (candidateObject == null || !candidateObject.IsSpawned || candidateObject.NetworkManager != NetworkManager)
                    continue;
            }
            int key = target.GetInstanceID();
            if (captives.ContainsKey(key) || !reservations.Add(key)) continue;
            try { TryCapture(target, key, status, tool); }
            finally { reservations.Remove(key); }
        }
    }

    private void TryCapture(Transform target, int key, PlayerTemporaryStatusController status, EquippableWorldPhysics tool)
    {
        PlayerHealth health = target.GetComponent<PlayerHealth>();
        if (status == null && tool == null
            || status != null && health == null
            || tool != null && target.GetComponent<EquippableItem>() == null) return;
        if (status != null && (status.IsTumbleweedCaptured || status.IsTumbleweedImmune)) return;
        if (health != null)
        {
            target.TryGetComponent(out PlayerConcreteTrapController concreteTrap);
            target.TryGetComponent(out DownedPlayerCarryable downedCarryable);
            target.TryGetComponent(out PlayerWheelbarrowController wheelbarrowPlayer);
            if (health.IsDowned && (health.IsCarriedByNPC || health.IsRespawnTimerPaused)
                || concreteTrap != null && concreteTrap.IsTrapped
                || downedCarryable != null && downedCarryable.IsCarried
                || wheelbarrowPlayer != null && wheelbarrowPlayer.IsPassenger) return;
        }
        NetworkObject targetNetworkObject = target.GetComponent<NetworkObject>();
        WheelbarrowController driverWheelbarrow = null;
        ulong driverClientId = WheelbarrowController.NoClient;
        if (targetNetworkObject != null)
        {
            WheelbarrowController wheelbarrow = WheelbarrowController.FindForPlayer(
                targetNetworkObject.OwnerClientId, IsSpawned ? NetworkManager : null);
            if (wheelbarrow != null && wheelbarrow.GetRole(targetNetworkObject.OwnerClientId) == WheelbarrowOccupantRole.Passenger) return;
            if (wheelbarrow != null && wheelbarrow.GetRole(targetNetworkObject.OwnerClientId) == WheelbarrowOccupantRole.Driver)
            {
                driverWheelbarrow = wheelbarrow;
                driverClientId = targetNetworkObject.OwnerClientId;
            }
        }
        if (tool != null && (tool.IsReturning || tool.IsTumbleweedCaptured || tool.IsTumbleweedImmune || MountingNailSceneState.IsTargetConnected(target))) return;

        int slot = FindFreeSlot();
        if (slot < 0) return;
        float angle = slot * 137.5f * Mathf.Deg2Rad;
        Vector3 offset = new(Mathf.Cos(angle) * 0.42f, 0.15f + (slot % 3) * 0.35f, Mathf.Sin(angle) * 0.42f);
        Captive c = new() { target = target, lastSafePosition = target.position, lastSafeRotation = target.rotation,
            body = target.GetComponent<Rigidbody>(), status = status, health = health, tool = tool,
            offset = offset,
            relativeRotation = Quaternion.Inverse(transform.rotation) * target.rotation,
            releaseAt = Now + captureDuration, slot = slot, colliders = target.GetComponentsInChildren<Collider>(true) };
        if (status != null) c.lastSafeRotation = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        if (tool != null) c.cachedToolBounds = tool.GetWorldColliderBounds();
        c.cachedToolCenterOffset = tool != null
            ? target.InverseTransformPoint(c.cachedToolBounds.center)
            : Vector3.zero;
        bool hasTargetBounds = false;
        c.enabled = new bool[c.colliders.Length];
        for (int i = 0; i < c.colliders.Length; i++)
        {
            c.enabled[i] = c.colliders[i] != null && c.colliders[i].enabled;
            if (c.enabled[i] && !c.colliders[i].isTrigger)
            {
                if (hasTargetBounds) c.cachedTargetBounds.Encapsulate(c.colliders[i].bounds);
                else { c.cachedTargetBounds = c.colliders[i].bounds; hasTargetBounds = true; }
            }
        }
        if (status != null && target.TryGetComponent(out CharacterController targetCapsule))
        {
            c.capsuleCenter = targetCapsule.center;
            c.capsuleHeight = targetCapsule.height;
            c.capsuleRadius = targetCapsule.radius;
        }
        ulong weedId = IsSpawned ? NetworkObject.NetworkObjectId : ulong.MaxValue;
        if (c.body != null) c.wasKinematic = c.body.isKinematic;
        for (int i = 0; i < c.colliders.Length; i++)
            if (c.colliders[i] != null && !c.colliders[i].isTrigger) c.colliders[i].enabled = false;
        Vector3 safeDirection = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        if (safeDirection.sqrMagnitude < 0.01f) safeDirection = Vector3.right;
        if (!TryFindReleasePosition(c, target.position + safeDirection * 1.2f + Vector3.up * 0.9f, out c.lastSafePosition))
        {
            Restore(c);
            return;
        }
        // Exit-position checks temporarily disabled the target colliders. Restore them before
        // committing capture so EquippableWorldPhysics snapshots the real pre-capture state.
        Restore(c);
        if (driverWheelbarrow != null && !driverWheelbarrow.ReleaseDriverForTumbleweedCapture(driverClientId))
        {
            Restore(c);
            return;
        }
        if (status != null && !status.BeginTumbleweedCapture(weedId, slot, captureDuration, offset, c.relativeRotation))
        {
            Restore(c);
            return;
        }
        if (tool != null) tool.SetTumbleweedCaptured(true, weedId, slot, c.releaseAt);
        if (c.body != null && !c.body.isKinematic) c.body.isKinematic = true;
        if (health != null && health.IsDowned) health.PauseRespawnTimerForTumbleweedCapture();
        captives.Add(key, c);
        occupiedSlots[slot] = true;
    }

    private void Release(int key)
    {
        if (!captives.TryGetValue(key, out Captive c)) return;
        captives.Remove(key);
        occupiedSlots[c.slot] = false;
        if (c.target == null) return;
        Vector3 horizontalRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        if (horizontalRight.sqrMagnitude < 0.01f) horizontalRight = Vector3.right;
        Vector3 desired = transform.position + horizontalRight * 1.2f + Vector3.up * 1.05f;
        Vector3 position = TryFindReleasePosition(c, desired, out Vector3 found) ? found : c.lastSafePosition;
        Quaternion releaseRotation = c.lastSafeRotation;
        c.target.SetPositionAndRotation(position, releaseRotation);
        Restore(c);
        if (c.status != null)
        {
            c.status.EndTumbleweedCapture(releaseImmunity);
            c.status.ApplyTumbleweedReleasePose(position, releaseRotation, Vector3.up * 1.1f);
        }
        if (c.health != null) c.health.ResumeRespawnTimerAfterTumbleweedCapture();
        if (c.tool != null) c.tool.SetTumbleweedCaptured(false, ulong.MaxValue, -1, 0d);
        if (c.tool != null) c.tool.SetTumbleweedReleaseImmunity(releaseImmunity);
        if (c.body != null && !c.wasKinematic) { if (c.body.isKinematic) c.body.isKinematic = false; c.body.AddForce(Vector3.up * 1.1f, ForceMode.VelocityChange); }
    }

    private bool TryFindReleasePosition(Captive captive, Vector3 desired, out Vector3 result)
    {
        bool hasCapsule = captive.status != null && captive.capsuleHeight > 0f;
        Vector3 capsuleCenterOffset = Vector3.zero;
        float capsuleRadius = 0f;
        float capsuleHalfHeight = 0f;
        float capsuleBottomOffset = 0f;
        if (hasCapsule)
        {
            Vector3 scale = captive.target.lossyScale;
            capsuleRadius = captive.capsuleRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float capsuleHeight = Mathf.Max(capsuleRadius * 2f, captive.capsuleHeight * Mathf.Abs(scale.y));
            capsuleHalfHeight = capsuleHeight * 0.5f;
            capsuleCenterOffset = captive.lastSafeRotation * Vector3.Scale(captive.capsuleCenter, scale);
            capsuleBottomOffset = capsuleCenterOffset.y - capsuleHalfHeight;
        }

        Vector3 ballWaterProbe = transform.position - Vector3.up * movement.CaptureRadius * 0.75f;
        bool ballIsFloating = WaterBody.TryGetContaining(ballWaterProbe, out WaterBody captureWater);
        Vector3 preferredDirection = Vector3.ProjectOnPlane(desired - transform.position, Vector3.up).normalized;
        if (preferredDirection.sqrMagnitude < 0.01f)
            preferredDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        for (int ring = 0; ring < 3; ring++)
        {
            float exitRadius = 1.25f + ring * 0.45f;
            for (int i = 0; i < 12; i++)
            {
                Vector3 radial = Quaternion.Euler(0f, i * 30f, 0f) * preferredDirection;
                Vector3 candidate = transform.position + radial * exitRadius;
                Vector3 waterProbe = new(candidate.x, transform.position.y - movement.CaptureRadius * 0.75f, candidate.z);
                bool supported = false;
                if (WaterBody.TryGetContaining(waterProbe, out WaterBody water))
                {
                    if (ballIsFloating && water != captureWater) continue;
                    supported = true;
                    if (captive.tool != null)
                    {
                        Vector3 scaledCenter = Vector3.Scale(captive.cachedToolCenterOffset, captive.target.lossyScale);
                        candidate.y = water.SurfaceHeight - (captive.lastSafeRotation * scaledCenter).y
                            + captive.cachedToolBounds.extents.y + 0.03f;
                    }
                    else
                    {
                        candidate.y = water.SurfaceHeight + 0.03f - capsuleBottomOffset;
                    }
                }
                else if (Physics.Raycast(candidate + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 4f,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore))
                {
                    if (ballIsFloating) continue;
                    supported = true;
                    if (captive.tool != null)
                    {
                        Vector3 scaledCenter = Vector3.Scale(captive.cachedToolCenterOffset, captive.target.lossyScale);
                        candidate.y = ground.point.y - (captive.lastSafeRotation * scaledCenter).y
                            + captive.cachedToolBounds.extents.y + 0.03f;
                    }
                    else
                    {
                        candidate.y = ground.point.y + 0.03f - capsuleBottomOffset;
                    }
                }
                if (!supported) continue;

                bool clear;
                if (hasCapsule)
                {
                    Vector3 center = candidate + capsuleCenterOffset;
                    float endpoint = Mathf.Max(0f, capsuleHalfHeight - capsuleRadius);
                    clear = !Physics.CheckCapsule(center + Vector3.up * endpoint, center - Vector3.up * endpoint,
                        capsuleRadius, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                }
                else
                {
                    Vector3 center = candidate + captive.lastSafeRotation
                        * Vector3.Scale(captive.cachedToolCenterOffset, captive.target.lossyScale);
                    if (captive.tool != null)
                    {
                        Vector3 halfExtents = Vector3.Scale(captive.cachedToolBounds.extents,
                            new Vector3(Mathf.Abs(captive.target.lossyScale.x), Mathf.Abs(captive.target.lossyScale.y),
                                Mathf.Abs(captive.target.lossyScale.z)));
                        clear = !Physics.CheckBox(center, halfExtents, Quaternion.identity, Physics.AllLayers,
                            QueryTriggerInteraction.Ignore);
                    }
                    else
                    {
                        clear = !Physics.CheckSphere(center, 0.35f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    }
                }

                if (clear)
                {
                    result = candidate;
                    return true;
                }
            }
        }
        result = captive.lastSafePosition;
        return false;
    }

    private int FindFreeSlot()
    {
        EnsureSlotCapacity();
        int limit = Mathf.Min(Mathf.Max(1, capacity), occupiedSlots.Length);
        for (int i = 0; i < limit; i++) if (!occupiedSlots[i]) return i;
        return -1;
    }

    private void EnsureSlotCapacity()
    {
        int requested = Mathf.Max(1, capacity);
        if (occupiedSlots == null)
        {
            occupiedSlots = new bool[requested];
            return;
        }
        if (occupiedSlots.Length >= requested) return;
        System.Array.Resize(ref occupiedSlots, requested);
    }

    private static void Restore(Captive c)
    {
        if (c.target == null) return;
        for (int i = 0; i < c.colliders.Length; i++) if (c.colliders[i] != null) c.colliders[i].enabled = c.enabled[i];
        if (c.body != null && c.wasKinematic && !c.body.isKinematic) c.body.isKinematic = true;
    }

    public override void OnDestroy()
    {
        foreach (int key in new List<int>(captives.Keys)) Release(key);
        base.OnDestroy();
    }
}
