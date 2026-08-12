using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
public sealed class RopeEndProjectile : NetworkBehaviour
{
    private readonly List<(Collider, Collider)> ignoredOwnerPairs = new List<(Collider, Collider)>();
    private Rigidbody body;
    private RopeToolController ownerRope;
    private float restoreCollisionsAt;
    private bool collisionResolved;
    private float flightLinearDamping;
    private float flightAngularDamping;
    private Collider cachedCollider;
    private Collider supportCollider;
    private Vector3 supportNormal = Vector3.up;

    public bool IsFlying => !collisionResolved;
    public bool IsLanded => collisionResolved;
    public RopeEndMotionState CurrentMotionState => collisionResolved
        ? RopeEndMotionState.Landed
        : RopeEndMotionState.Flying;
    public float CollisionRadius => cachedCollider != null
        ? Mathf.Max(0.01f, Mathf.Min(cachedCollider.bounds.extents.x, cachedCollider.bounds.extents.z))
        : 0.11f;

    public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        cachedCollider = GetComponentInChildren<Collider>();
        flightLinearDamping = body.linearDamping;
        flightAngularDamping = body.angularDamping;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public override void OnNetworkSpawn()
    {
        ConfigurePhysicsAuthority();
    }

    public void Initialize(RopeToolController rope, Vector3 velocity)
    {
        ownerRope = rope;
        Body.linearVelocity = velocity;
        restoreCollisionsAt = Time.time + 0.25f;

        if (rope == null)
        {
            return;
        }

        foreach (Collider endpointCollider in GetComponentsInChildren<Collider>())
        {
            foreach (Collider ownerCollider in rope.GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(endpointCollider, ownerCollider, true);
                ignoredOwnerPairs.Add((endpointCollider, ownerCollider));
            }
        }
    }

    public void RestoreOwner(RopeToolController rope)
    {
        ownerRope = rope;
    }

    public void Land(float linearDamping, float angularDamping)
    {
        collisionResolved = true;
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.linearDamping = Mathf.Max(0f, linearDamping);
        Body.angularDamping = Mathf.Max(0f, angularDamping);
        if (HasSimulationAuthority())
        {
            Body.isKinematic = false;
            Body.useGravity = true;
            Body.WakeUp();
        }
    }

    public bool TryGetSupportNormal(float probeDistance, LayerMask mask, out Vector3 normal)
    {
        normal = Vector3.up;
        supportCollider = null;
        if (!HasSimulationAuthority() || cachedCollider == null)
        {
            return false;
        }

        float radius = CollisionRadius * 0.85f;
        Vector3 origin = Body.worldCenterOfMass + Vector3.up * Mathf.Max(0.02f, radius * 0.35f);
        if (!Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit,
                Mathf.Max(0.01f, probeDistance) + radius * 0.35f, mask, QueryTriggerInteraction.Ignore)
            || hit.collider == cachedCollider || hit.collider.transform.IsChildOf(transform))
        {
            return false;
        }

        supportCollider = hit.collider;
        supportNormal = hit.normal.sqrMagnitude > 0.001f ? hit.normal.normalized : Vector3.up;
        normal = supportNormal;
        return Vector3.Dot(normal, Vector3.up) > 0.2f;
    }

    public bool IsSupportCollider(Collider candidate)
    {
        return candidate != null && candidate == supportCollider;
    }

    private void Update()
    {
        if (ignoredOwnerPairs.Count == 0 || Time.time < restoreCollisionsAt)
        {
            return;
        }

        foreach ((Collider endpointCollider, Collider ownerCollider) in ignoredOwnerPairs)
        {
            if (endpointCollider != null && ownerCollider != null)
            {
                Physics.IgnoreCollision(endpointCollider, ownerCollider, false);
            }
        }
        ignoredOwnerPairs.Clear();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collisionResolved || ownerRope == null || !HasSimulationAuthority())
        {
            return;
        }

        collisionResolved = true;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        ownerRope.ResolveProjectileCollision(this, collision.collider, point);
    }

    private bool HasSimulationAuthority()
    {
        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || IsServer;
    }

    private void ConfigurePhysicsAuthority()
    {
        bool hasAuthority = HasSimulationAuthority();
        Body.isKinematic = !hasAuthority;
        Body.useGravity = hasAuthority;
        if (!collisionResolved)
        {
            Body.linearDamping = flightLinearDamping;
            Body.angularDamping = flightAngularDamping;
        }
    }
}
