using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NPCBrain))]
[RequireComponent(typeof(NPCHealth))]
[RequireComponent(typeof(NavMeshAgent))]
public class NPCExternalImpulseController : NetworkBehaviour, IExternalImpulseReceiver
{
    [Header("Recovery")]
    [SerializeField] private float navMeshRecoveryRadius = 2f;
    [SerializeField] private float offNavMeshDeathDelay = 1f;
    [SerializeField] private float maximumAirborneDuration = 5f;
    [SerializeField] private float collisionSkin = 0.03f;
    [SerializeField] private float groundedProbeDistance = 0.12f;

    private NPCBrain brain;
    private NPCHealth health;
    private NavMeshAgent agent;
    private NPCCarrier carrier;
    private NPCAttackController attackController;
    private NPCAnimationController animationController;
    private CapsuleCollider bodyCollider;

    private Vector3 externalVelocity;
    private float horizontalDeceleration;
    private float gravityMultiplier = 1f;
    private float impulseTimeRemaining;
    private float airborneElapsed;
    private float offNavMeshElapsed;
    private float maximumHorizontalSpeed;
    private float maximumVerticalSpeed;
    private NetworkObject impulseSource;
    private bool isActive;

    public Vector3 CurrentExternalVelocity => externalVelocity;
    public float MovementControlMultiplier => 0f;
    public bool IsImpulseActive => isActive;

    private bool CanSimulate =>
        NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !IsSpawned || IsServer;

    private void Awake()
    {
        brain = GetComponent<NPCBrain>();
        health = GetComponent<NPCHealth>();
        agent = GetComponent<NavMeshAgent>();
        carrier = GetComponent<NPCCarrier>();
        attackController = GetComponent<NPCAttackController>();
        animationController = GetComponent<NPCAnimationController>();
        bodyCollider = GetComponent<CapsuleCollider>();
    }

    private void Update()
    {
        if (!CanSimulate || !isActive)
        {
            return;
        }

        if (health == null || health.IsDead)
        {
            StopExternalControl(false);
            return;
        }

        SimulateImpulse(Time.deltaTime);
    }

    public bool TryApplyExternalImpulse(ExternalImpulseData impulse, NetworkObject source)
    {
        if (!CanSimulate || !impulse.IsValid || health == null || health.IsDead)
        {
            return false;
        }

        if (!isActive)
        {
            BeginExternalControl(impulse.ForceDropHeldObject);
        }
        else if (impulse.ForceDropHeldObject)
        {
            carrier?.DropHeldObject();
        }

        impulseSource = source;
        externalVelocity += impulse.InitialVelocity;
        maximumHorizontalSpeed = Mathf.Max(maximumHorizontalSpeed, impulse.MaximumHorizontalSpeed);
        maximumVerticalSpeed = Mathf.Max(maximumVerticalSpeed, impulse.MaximumVerticalSpeed);
        ClampVelocity();

        horizontalDeceleration = Mathf.Max(horizontalDeceleration, impulse.HorizontalDeceleration);
        gravityMultiplier = Mathf.Max(gravityMultiplier, impulse.GravityMultiplier);
        impulseTimeRemaining = Mathf.Max(impulseTimeRemaining, impulse.MaximumDuration);
        airborneElapsed = 0f;
        offNavMeshElapsed = 0f;
        return true;
    }

    private void BeginExternalControl(bool forceDrop)
    {
        isActive = true;
        brain?.BeginExternalControl();
        attackController?.CancelPendingAttacks();
        if (forceDrop)
        {
            carrier?.DropHeldObject();
        }

        if (agent != null && agent.enabled)
        {
            agent.ResetPath();
            agent.enabled = false;
        }
    }

    private void SimulateImpulse(float deltaTime)
    {
        impulseTimeRemaining = Mathf.Max(0f, impulseTimeRemaining - deltaTime);
        Vector3 horizontal = Vector3.ProjectOnPlane(externalVelocity, Vector3.up);
        horizontal = Vector3.MoveTowards(
            horizontal,
            Vector3.zero,
            Mathf.Max(0f, horizontalDeceleration) * deltaTime);
        if (impulseTimeRemaining <= 0f)
        {
            horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, horizontalDeceleration * 2f * deltaTime);
        }

        externalVelocity = horizontal
            + Vector3.up * (externalVelocity.y + Physics.gravity.y * Mathf.Max(0f, gravityMultiplier) * deltaTime);
        ClampVelocity();

        MoveWithCollision(externalVelocity * deltaTime);
        bool grounded = ProbeGround();
        if (grounded && externalVelocity.y < 0f)
        {
            externalVelocity.y = 0f;
            airborneElapsed = 0f;
        }
        else
        {
            airborneElapsed += deltaTime;
        }

        float normalizedSpeed = maximumHorizontalSpeed > 0.01f
            ? Mathf.Clamp01(horizontal.magnitude / maximumHorizontalSpeed)
            : 0f;
        animationController?.SetExternalMovementSpeedNormalized(normalizedSpeed);

        if (airborneElapsed >= maximumAirborneDuration)
        {
            KillFromImpulse();
            return;
        }

        if (!grounded || horizontal.sqrMagnitude > 0.04f || Mathf.Abs(externalVelocity.y) > 0.1f)
        {
            offNavMeshElapsed = 0f;
            return;
        }

        if (TryRecoverAgent())
        {
            StopExternalControl(true);
            return;
        }

        offNavMeshElapsed += deltaTime;
        if (offNavMeshElapsed >= offNavMeshDeathDelay)
        {
            KillFromImpulse();
        }
    }

    private void MoveWithCollision(Vector3 delta)
    {
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return;
        }

        GetCapsule(out Vector3 point1, out Vector3 point2, out float radius);
        Vector3 direction = delta / distance;
        RaycastHit[] hits = Physics.CapsuleCastAll(
            point1,
            point2,
            radius,
            direction,
            distance + collisionSkin,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        RaycastHit closest = default;
        bool hasBlockingHit = false;
        float closestDistance = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (ShouldIgnoreCollider(hit.collider)
                || (hit.normal.y > 0.55f && direction.y >= -0.01f))
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closest = hit;
                closestDistance = hit.distance;
                hasBlockingHit = true;
            }
        }

        if (!hasBlockingHit)
        {
            transform.position += delta;
            return;
        }

        float safeDistance = Mathf.Max(0f, closest.distance - collisionSkin);
        transform.position += direction * safeDistance;
        float intoSurface = Vector3.Dot(externalVelocity, closest.normal);
        if (intoSurface < 0f)
        {
            externalVelocity -= closest.normal * intoSurface;
        }
    }

    private bool ProbeGround()
    {
        GetCapsule(out Vector3 point1, out Vector3 point2, out float radius);
        RaycastHit[] hits = Physics.CapsuleCastAll(
            point1,
            point2,
            radius,
            Vector3.down,
            groundedProbeDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (!ShouldIgnoreCollider(hit.collider) && hit.normal.y >= 0.55f)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryRecoverAgent()
    {
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshRecoveryRadius, NavMesh.AllAreas))
        {
            return false;
        }

        transform.position = hit.position;
        if (agent != null)
        {
            agent.enabled = true;
            agent.Warp(hit.position);
            agent.ResetPath();
        }

        return true;
    }

    private void KillFromImpulse()
    {
        if (health != null && !health.IsDead)
        {
            health.DamageReceived(Mathf.Max(health.CurrentHealth, health.MaxHealth), impulseSource);
        }

        StopExternalControl(false);
    }

    private void StopExternalControl(bool restartBehavior)
    {
        isActive = false;
        externalVelocity = Vector3.zero;
        impulseTimeRemaining = 0f;
        airborneElapsed = 0f;
        offNavMeshElapsed = 0f;
        horizontalDeceleration = 0f;
        gravityMultiplier = 1f;
        maximumHorizontalSpeed = 0f;
        maximumVerticalSpeed = 0f;
        impulseSource = null;
        animationController?.ClearExternalMovementSpeedOverride();

        if (restartBehavior)
        {
            brain?.EndExternalControl();
        }
    }

    private void ClampVelocity()
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(externalVelocity, Vector3.up);
        if (maximumHorizontalSpeed > 0f)
        {
            horizontal = Vector3.ClampMagnitude(horizontal, maximumHorizontalSpeed);
        }

        float vertical = maximumVerticalSpeed > 0f
            ? Mathf.Clamp(externalVelocity.y, -maximumVerticalSpeed, maximumVerticalSpeed)
            : externalVelocity.y;
        externalVelocity = horizontal + Vector3.up * vertical;
    }

    private void GetCapsule(out Vector3 point1, out Vector3 point2, out float radius)
    {
        if (bodyCollider == null)
        {
            radius = 0.35f;
            Vector3 center = transform.position + Vector3.up * 0.7f;
            point1 = center + Vector3.up * 0.35f;
            point2 = center - Vector3.up * 0.35f;
            return;
        }

        Vector3 scale = bodyCollider.transform.lossyScale;
        radius = bodyCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(bodyCollider.height * Mathf.Abs(scale.y), radius * 2f);
        Vector3 centerWorld = bodyCollider.transform.TransformPoint(bodyCollider.center);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        point1 = centerWorld + Vector3.up * halfSegment;
        point2 = centerWorld - Vector3.up * halfSegment;
    }

    private bool ShouldIgnoreCollider(Collider collider)
    {
        if (collider == null || collider.transform.root == transform.root)
        {
            return true;
        }

        return impulseSource != null && collider.transform.root == impulseSource.transform.root;
    }
}
