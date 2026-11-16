using Unity.Netcode;
using UnityEngine;

/// <summary>Server-authoritative physics projectile that applies mud and knockback once.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject), typeof(Rigidbody), typeof(SphereCollider))]
public sealed class BeaverScoutMudball : NetworkBehaviour
{
    [SerializeField] private GameObject splashPrefab;
    [SerializeField, Min(0.05f)] private float splashLifetime = 0.65f;

    private float mudGainPercentagePoints = 15f;
    private ExternalImpulseProfileSO impulseProfile;
    private Rigidbody body;
    private SphereCollider sphereCollider;
    private NetworkObject owner;
    private float lifetime = 3f;
    private bool resolved;
    private bool resolvedByCollision;
    private Vector3 pendingVelocity;
    private Vector3 incomingVelocity;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        sphereCollider = GetComponent<SphereCollider>();
    }

    public void Initialize(Vector3 velocity, NetworkObject projectileOwner, float maximumLifetime)
    {
        pendingVelocity = velocity;
        owner = projectileOwner;
        lifetime = Mathf.Max(0.1f, maximumLifetime);
        IgnoreOwnerCollisions();
    }

    public void Launch(Vector3 velocity)
    {
        if (body == null) body = GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = velocity.sqrMagnitude > 0.001f ? velocity : pendingVelocity;
        pendingVelocity = Vector3.zero;
        incomingVelocity = body.linearVelocity;
    }

    public void ConfigureImpact(float mudGain, ExternalImpulseProfileSO profile)
    {
        mudGainPercentagePoints = Mathf.Max(0f, mudGain);
        impulseProfile = profile;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer && sphereCollider != null) sphereCollider.enabled = false;
        if (IsServer && body != null && pendingVelocity.sqrMagnitude > 0.001f)
            body.linearVelocity = pendingVelocity;
    }

    private void Update()
    {
        if (!IsSimulationAuthority()) return;
        lifetime -= Time.deltaTime;
        if (lifetime <= 0f) Resolve(transform.position);
    }

    private void FixedUpdate()
    {
        if (IsSimulationAuthority() && body != null && body.linearVelocity.sqrMagnitude > 0.001f)
            incomingVelocity = body.linearVelocity;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsSimulationAuthority() || resolved || collision == null || collision.collider == null) return;
        Collider hitCollider = collision.collider;
        if (owner != null && hitCollider.transform.root == owner.transform.root) return;

        PlayerHealth health = hitCollider.GetComponentInParent<PlayerHealth>();
        if (health != null)
        {
            if (!health.IsDowned)
            {
                PlayerMudStatusController mud = health.GetComponent<PlayerMudStatusController>();
                if (mud != null) mud.AddMudPercentagePoints(mudGainPercentagePoints);

                IExternalImpulseReceiver receiver = health.GetComponent<IExternalImpulseReceiver>();
                Vector3 horizontal = Vector3.ProjectOnPlane(incomingVelocity, Vector3.up);
                if (receiver != null && impulseProfile != null && horizontal.sqrMagnitude > 0.001f)
                    receiver.TryApplyExternalImpulse(impulseProfile.CreateImpulse(horizontal.normalized), owner);
            }
        }

        Vector3 contact = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Resolve(contact, true);
    }

    private void Resolve(Vector3 position, bool collision = false)
    {
        if (resolved) return;
        resolved = true;
        resolvedByCollision = collision;
        if (IsSimulationAuthority())
        {
            if (resolvedByCollision)
            {
                if (IsSpawned) PresentSplashClientRpc(position);
                else PresentSplash(position);
            }
            if (IsSpawned) NetworkObject.Despawn(true);
            else Destroy(gameObject);
        }
    }

    [ClientRpc]
    private void PresentSplashClientRpc(Vector3 position)
    {
        PresentSplash(position);
    }

    private void PresentSplash(Vector3 position)
    {
        if (splashPrefab == null) return;
        GameObject splash = Instantiate(splashPrefab, position, Quaternion.identity);
        Destroy(splash, Mathf.Max(0.05f, splashLifetime));
    }

    private bool IsSimulationAuthority()
    {
        NetworkManager manager = NetworkObject != null && NetworkObject.NetworkManager != null
            ? NetworkObject.NetworkManager : NetworkManager.Singleton;
        return manager == null || !manager.IsListening || IsServer;
    }

    private void IgnoreOwnerCollisions()
    {
        if (owner == null) return;
        Collider[] projectileColliders = GetComponentsInChildren<Collider>();
        Collider[] ownerColliders = owner.GetComponentsInChildren<Collider>();
        foreach (Collider projectileCollider in projectileColliders)
        {
            if (projectileCollider == null) continue;
            foreach (Collider ownerCollider in ownerColliders)
            {
                if (ownerCollider != null) Physics.IgnoreCollision(projectileCollider, ownerCollider, true);
            }
        }
    }
}
