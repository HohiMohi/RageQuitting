using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider), typeof(NetworkObject))]
public sealed class TumbleweedMovement : NetworkBehaviour
{
    [Header("World movement")]
    [SerializeField, Min(0.1f)] private float movementRadius = 0.65f;
    [SerializeField, Min(0f)] private float acceleration = 2.2f;
    [SerializeField, Min(0.1f)] private float maximumSpeed = 2.4f;
    private Bounds worldBounds;
    private bool hasWorldBounds;

    [Header("Rolling and hopping")]
    [SerializeField, Min(0f)] private float rollingTorque = 1.8f;
    [SerializeField, Min(0f)] private float hopImpulse = 1.1f;
    [SerializeField, Min(0.1f)] private float hopIntervalMinimum = 2.8f;
    [SerializeField, Min(0.1f)] private float hopIntervalMaximum = 5.2f;

    [Header("Water surface")]
    [SerializeField, Min(0f)] private float waterFloatHeight = 0.26f;
    [SerializeField, Min(0f)] private float waterFloatStrength = 20f;
    [SerializeField, Min(0f)] private float waterVerticalDamping = 9f;

    [Header("Collision safety")]
    [SerializeField, Min(0f)] private float collisionFilterExtraRadius = 1.4f;

    [SerializeField] private Rigidbody body;
    [SerializeField] private SphereCollider movementCollider;
    [SerializeField] private TumbleweedCaptureSurface captureSurface;

    private readonly Collider[] nearbyColliders = new Collider[64];
    private readonly List<Collider> ignoredColliders = new List<Collider>(16);
    private Vector3 recoveryPosition;
    private Quaternion recoveryRotation;
    private Vector3 heading;
    private Vector3 strongestBlockingNormal;
    private float strongestBlockingDot = -0.05f;
    private bool hasBlockingContact;
    private float nextHopTime;
    private bool offlinePhysicsInitialized;

    public SphereCollider CaptureCollider => captureSurface != null ? captureSurface.Collider : null;
    public Vector3 CapturePosition => transform.position;
    public float CaptureRadius => captureSurface != null ? captureSurface.WorldRadius : movementRadius;
    public bool HasServerAuthority => !IsSpawned || IsServer;
    public bool CanBeCaptured => isActiveAndEnabled && HasServerAuthority;

    private void Awake()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (movementCollider == null) movementCollider = GetComponent<SphereCollider>();
        recoveryPosition = transform.position;
        recoveryRotation = transform.rotation;
        ResolveWorldBoundsFromScene();
        Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        heading = planarForward.sqrMagnitude < 0.001f ? Vector3.forward : planarForward.normalized;
        ScheduleHop();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkObject.DestroyWithScene = true;
            recoveryPosition = body.position;
            recoveryRotation = body.rotation;
            RefreshIgnoredCollisions();
        }
    }

    private void FixedUpdate()
    {
        if (body == null)
        {
            return;
        }

        if (!offlinePhysicsInitialized)
        {
            offlinePhysicsInitialized = true;
            // NGO keeps NetworkRigidbodies kinematic before NetworkObject.Spawn. In a local/offline
            // round the existing NPCSpawner intentionally does not spawn NetworkObjects, so restore
            // dynamic physics for that unspawned offline case after all component Awake calls.
            if (!IsSpawned && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            {
                body.isKinematic = false;
            }
        }

        float now = Time.fixedTime;
        RefreshIgnoredCollisions();
        if (!HasServerAuthority || body.isKinematic)
        {
            return;
        }

        ApplyPendingBlockingBounce();
        if (OutsideWorldBounds(body.position))
        {
            ReflectHeadingAtExceededBounds(body.position);
            RecoverToLastSafePosition();
            return;
        }

        ReflectHeadingAtPredictiveBounds(body.position);

        WaterBody water = null;
        Vector3 waterProbe = body.position - Vector3.up * movementRadius * 0.75f;
        bool onWater = WaterBody.TryGetContaining(waterProbe, out water);
        RaycastHit groundSupportHit = default;
        bool hasGroundSupport = !onWater && HasGroundSupport(body.position, out groundSupportHit);
        if (onWater || hasGroundSupport)
        {
            recoveryPosition = body.position;
            recoveryRotation = body.rotation;
        }

        Vector3 movementAcceleration = heading * acceleration;
        if (hasGroundSupport)
        {
            movementAcceleration = GetLandAcceleration(heading, groundSupportHit);
        }
        body.AddForce(movementAcceleration, ForceMode.Acceleration);

        ConstrainPlanarVelocityToHeading();
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);

        if (onWater && water != null)
        {
            ApplyWaterFloat(water, heading);
        }
        else if (now >= nextHopTime && hasGroundSupport)
        {
            body.AddForce(Vector3.up * hopImpulse, ForceMode.Impulse);
            ScheduleHop();
        }

        if (planarVelocity.sqrMagnitude > 0.04f)
        {
            Vector3 rollingAxis = Vector3.Cross(Vector3.up, planarVelocity.normalized);
            body.AddTorque(rollingAxis * rollingTorque, ForceMode.Acceleration);
        }
    }


    private void ApplyWaterFloat(WaterBody water, Vector3 steeringDirection)
    {
        float targetHeight = GetWaterFloatTarget(water, steeringDirection);
        float verticalAcceleration = -Physics.gravity.y
            + (targetHeight - body.position.y) * waterFloatStrength
            - body.linearVelocity.y * waterVerticalDamping;
        body.AddForce(Vector3.up * verticalAcceleration, ForceMode.Acceleration);
    }

    private float GetWaterFloatTarget(WaterBody water, Vector3 steeringDirection)
    {
        float normalFloatHeight = water.SurfaceHeight + waterFloatHeight;
        if (!TryFindWaterBankSupport(water, steeringDirection, out RaycastHit ground)) return normalFloatHeight;

        float bankFloatHeight = Mathf.Max(ground.point.y, water.SurfaceHeight) + movementRadius + 0.03f;
        return Mathf.Max(normalFloatHeight, bankFloatHeight);
    }

    private bool TryFindWaterBankSupport(WaterBody water, Vector3 direction, out RaycastHit ground)
    {
        ground = default;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.001f) return false;

        Vector3 bankSample = body.position + direction * (movementRadius * 2f);
        bankSample.y = water.SurfaceHeight;
        if (water.Contains(bankSample) || OutsideWorldBounds(bankSample)) return false;

        float maximumBankRise = movementRadius;
        const float maximumBankDrop = 0.25f;
        float rayStartHeight = maximumBankRise + 0.25f;
        Vector3 rayOrigin = bankSample + Vector3.up * rayStartHeight;
        float rayDistance = rayStartHeight + maximumBankDrop;
        if (!Physics.Raycast(rayOrigin, Vector3.down, out ground, rayDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            || !(ground.collider is TerrainCollider)
            || ground.collider.gameObject.scene != gameObject.scene
            || ground.normal.y < 0.7f)
        {
            return false;
        }

        return ground.point.y >= water.SurfaceHeight - maximumBankDrop
            && ground.point.y <= water.SurfaceHeight + maximumBankRise;
    }

    private bool IsEligibleShorelineTerrain(Collider candidate)
    {
        if (!(candidate is TerrainCollider)
            || candidate.gameObject.scene != gameObject.scene)
        {
            return false;
        }

        Vector3 waterProbe = body.position - Vector3.up * movementRadius * 0.75f;
        return WaterBody.TryGetContaining(waterProbe, out WaterBody water)
            && TryFindWaterBankSupport(water, heading, out RaycastHit support)
            && support.collider == candidate;
    }

    private void ConstrainPlanarVelocityToHeading()
    {
        Vector3 velocity = body.linearVelocity;
        float forwardSpeed = Mathf.Clamp(
            Vector3.Dot(Vector3.ProjectOnPlane(velocity, Vector3.up), heading), 0f, maximumSpeed);
        body.linearVelocity = heading * forwardSpeed + Vector3.up * velocity.y;
    }

    private void ApplyPendingBlockingBounce()
    {
        Vector3 normal = strongestBlockingNormal;
        bool shouldBounce = hasBlockingContact;
        strongestBlockingNormal = Vector3.zero;
        strongestBlockingDot = -0.05f;
        hasBlockingContact = false;

        if (shouldBounce) ReflectFromPlanarNormal(normal);
    }

    private void ReflectFromPlanarNormal(Vector3 normal)
    {
        normal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
        if (normal.sqrMagnitude < 0.001f) return;

        heading = Vector3.ProjectOnPlane(Vector3.Reflect(heading, normal), Vector3.up).normalized;
        Vector3 velocity = body.linearVelocity;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (Vector3.Dot(planarVelocity, normal) < 0f)
        {
            planarVelocity = Vector3.Reflect(planarVelocity, normal);
        }
        body.linearVelocity = planarVelocity + Vector3.up * velocity.y;
        ConstrainPlanarVelocityToHeading();
    }

    private void ReflectHeadingAtPredictiveBounds(Vector3 position)
    {
        if (!hasWorldBounds) return;
        float predictionDistance = maximumSpeed * Time.fixedDeltaTime + 0.01f;
        Vector3 predicted = position + heading * predictionDistance;
        float minimumX = worldBounds.min.x + movementRadius;
        float maximumX = worldBounds.max.x - movementRadius;
        float minimumZ = worldBounds.min.z + movementRadius;
        float maximumZ = worldBounds.max.z - movementRadius;

        if (heading.x < 0f && predicted.x < minimumX) ReflectFromPlanarNormal(Vector3.right);
        else if (heading.x > 0f && predicted.x > maximumX) ReflectFromPlanarNormal(Vector3.left);
        if (heading.z < 0f && predicted.z < minimumZ) ReflectFromPlanarNormal(Vector3.forward);
        else if (heading.z > 0f && predicted.z > maximumZ) ReflectFromPlanarNormal(Vector3.back);
    }

    private void ReflectHeadingAtExceededBounds(Vector3 position)
    {
        if (!hasWorldBounds) return;
        float minimumX = worldBounds.min.x + movementRadius;
        float maximumX = worldBounds.max.x - movementRadius;
        float minimumZ = worldBounds.min.z + movementRadius;
        float maximumZ = worldBounds.max.z - movementRadius;

        if (position.x < minimumX)
        {
            if (heading.x < 0f) ReflectFromPlanarNormal(Vector3.right);
            else if (heading.x == 0f) SetHeadingInwardAtBoundary(Vector3.right);
        }
        else if (position.x > maximumX)
        {
            if (heading.x > 0f) ReflectFromPlanarNormal(Vector3.left);
            else if (heading.x == 0f) SetHeadingInwardAtBoundary(Vector3.left);
        }

        if (position.z < minimumZ)
        {
            if (heading.z < 0f) ReflectFromPlanarNormal(Vector3.forward);
            else if (heading.z == 0f) SetHeadingInwardAtBoundary(Vector3.forward);
        }
        else if (position.z > maximumZ)
        {
            if (heading.z > 0f) ReflectFromPlanarNormal(Vector3.back);
            else if (heading.z == 0f) SetHeadingInwardAtBoundary(Vector3.back);
        }
    }

    private void SetHeadingInwardAtBoundary(Vector3 inward)
    {
        heading = (heading + inward * 0.1f).normalized;
    }

    private Vector3 GetLandAcceleration(Vector3 desiredDirection, RaycastHit groundSupport)
    {
        Collider support = groundSupport.collider;
        Vector3 normal = groundSupport.normal;
        if (acceleration <= 0f
            || !body.useGravity
            || !(support is TerrainCollider)
            || support.gameObject.scene != gameObject.scene
            || normal.y < 0.7f)
        {
            return desiredDirection * acceleration;
        }

        Vector3 slopeDirection = Vector3.ProjectOnPlane(desiredDirection, normal);
        if (slopeDirection.sqrMagnitude < 0.001f)
        {
            return desiredDirection * acceleration;
        }

        slopeDirection.Normalize();
        float opposingGravity = -Vector3.Dot(Physics.gravity, slopeDirection);
        if (opposingGravity <= 0f)
        {
            return desiredDirection * acceleration;
        }

        return slopeDirection * (acceleration + opposingGravity);
    }

    private bool HasGroundSupport(Vector3 position, out RaycastHit groundSupport)
    {
        float castRadius = Mathf.Max(0.05f, movementRadius * 0.85f);
        float castDistance = Mathf.Max(0.12f, movementRadius - castRadius + 0.12f);
        return Physics.SphereCast(
            position + Vector3.up * 0.05f,
            castRadius,
            Vector3.down,
            out groundSupport,
            castDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
    }

    private bool OutsideWorldBounds(Vector3 position)
    {
        return hasWorldBounds
            && (position.x - movementRadius < worldBounds.min.x
                || position.x + movementRadius > worldBounds.max.x
                || position.z - movementRadius < worldBounds.min.z
                || position.z + movementRadius > worldBounds.max.z
                || position.y < worldBounds.min.y - 2f);
    }

    private void RecoverToLastSafePosition()
    {
        body.position = recoveryPosition;
        body.rotation = recoveryRotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(recoveryPosition, recoveryRotation);
        if (IsSpawned && IsServer && TryGetComponent(out Unity.Netcode.Components.NetworkTransform networkTransform))
        {
            networkTransform.Teleport(recoveryPosition, recoveryRotation, transform.localScale);
        }

        Physics.SyncTransforms();
        ScheduleHop();
        // Keep this NetworkObject alive so future capture state and references survive recovery.
    }

    private void RefreshIgnoredCollisions()
    {
        if (movementCollider == null)
        {
            return;
        }

        float queryRadius = movementRadius + maximumSpeed * Time.fixedDeltaTime
            + collisionFilterExtraRadius;
        int count = Physics.OverlapSphereNonAlloc(
            body.position,
            queryRadius,
            nearbyColliders,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            IgnoreIfPlayerOrTool(nearbyColliders[i]);
        }

        if (count == nearbyColliders.Length)
        {
            Collider[] overflow = Physics.OverlapSphere(
                body.position,
                queryRadius,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overflow.Length; i++) IgnoreIfPlayerOrTool(overflow[i]);
        }
    }

    private void IgnoreIfPlayerOrTool(Collider candidate)
    {
        if (candidate == null || candidate == movementCollider || candidate.attachedRigidbody == body)
        {
            return;
        }

        if (candidate.GetComponentInParent<PlayerNew>() != null
            || candidate.GetComponentInParent<EquippableItem>() != null)
        {
            IgnoreMovementCollision(candidate);
        }
    }

    private void IgnoreMovementCollision(Collider other)
    {
        if (other == null)
        {
            return;
        }

        if (!Physics.GetIgnoreCollision(movementCollider, other))
        {
            Physics.IgnoreCollision(movementCollider, other, true);
        }
        if (!ignoredColliders.Contains(other)) ignoredColliders.Add(other);
    }

    private void ResolveWorldBoundsFromScene()
    {
        Scene ownerScene = gameObject.scene;
        if (!ownerScene.IsValid() || !ownerScene.isLoaded)
        {
            return;
        }

        foreach (GameObject root in ownerScene.GetRootGameObjects())
        {
            Terrain terrain = root.GetComponentInChildren<Terrain>(true);
            if (terrain == null || terrain.terrainData == null)
            {
                continue;
            }

            Vector3 size = terrain.terrainData.size;
            Vector3 origin = terrain.transform.position;
            Vector3 min = terrain.transform.TransformPoint(Vector3.zero);
            Vector3 max = terrain.transform.TransformPoint(size);
            worldBounds = new Bounds((min + max) * 0.5f, new Vector3(
                Mathf.Abs(max.x - min.x),
                Mathf.Abs(max.y - min.y),
                Mathf.Abs(max.z - min.z)));
            worldBounds.Encapsulate(origin);
            hasWorldBounds = true;
            return;
        }
    }

    private void ScheduleHop()
    {
        float minimum = Mathf.Max(0.1f, Mathf.Min(hopIntervalMinimum, hopIntervalMaximum));
        float maximum = Mathf.Max(minimum, Mathf.Max(hopIntervalMinimum, hopIntervalMaximum));
        nextHopTime = Time.fixedTime + Random.Range(minimum, maximum);
    }

    private void OnCollisionEnter(Collision collision)
    {
        CollectBlockingContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        CollectBlockingContact(collision);
    }

    private void CollectBlockingContact(Collision collision)
    {
        if (!HasServerAuthority
            || collision.contactCount == 0
            || collision.collider == null
            || collision.collider.GetComponentInParent<PlayerNew>() != null
            || collision.collider.GetComponentInParent<EquippableItem>() != null)
        {
            return;
        }

        Vector3 blockingNormal = Vector3.zero;
        float blockingDot = -0.05f;
        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 contactNormal = collision.GetContact(i).normal;
            if (contactNormal.y >= 0.7f) continue;

            Vector3 planarNormal = Vector3.ProjectOnPlane(contactNormal, Vector3.up);
            if (planarNormal.sqrMagnitude < 0.001f) continue;
            planarNormal.Normalize();

            float headingIntoFace = Vector3.Dot(heading, planarNormal);
            if (headingIntoFace >= blockingDot) continue;

            blockingNormal = planarNormal;
            blockingDot = headingIntoFace;
        }

        if (blockingDot >= -0.05f || IsEligibleShorelineTerrain(collision.collider)
            || blockingDot >= strongestBlockingDot) return;

        strongestBlockingNormal = blockingNormal;
        strongestBlockingDot = blockingDot;
        hasBlockingContact = true;
    }

    private void OnDisable()
    {
        hasBlockingContact = false;
        strongestBlockingNormal = Vector3.zero;
        strongestBlockingDot = -0.05f;
        if (movementCollider == null)
        {
            ignoredColliders.Clear();
            return;
        }

        for (int i = 0; i < ignoredColliders.Count; i++)
        {
            if (ignoredColliders[i] != null)
            {
                Physics.IgnoreCollision(movementCollider, ignoredColliders[i], false);
            }
        }

        ignoredColliders.Clear();
    }
}
