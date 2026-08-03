using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class EquippableWorldPhysics : NetworkBehaviour
{
    private const ulong NoNetworkObject = ulong.MaxValue;
    private const string ColliderRootName = "WorldPhysicsColliders";

    private readonly NetworkVariable<bool> returningNetwork = new NetworkVariable<bool>();
    private readonly List<Collider> itemColliders = new List<Collider>(4);
    private readonly List<Collider> ignoredDropperColliders = new List<Collider>(4);
    private readonly Dictionary<ulong, float> playerPushTimes = new Dictionary<ulong, float>();
    private readonly Dictionary<int, float> npcPushTimes = new Dictionary<int, float>();
    private readonly Dictionary<int, float> impactDamageTimes = new Dictionary<int, float>();

    private EquippableItem equippableItem;
    private Rigidbody body;
    private EquippableWorldPhysicsProfileSO profile;
    private NetworkObject dropperNetworkObject;
    private ulong ignoredDropperNetworkObjectId = NoNetworkObject;
    private float dropperAttributionExpiresAt;
    private float collisionGraceStartedAt = -1f;
    private Vector3 initialSpawnPosition;
    private Quaternion initialSpawnRotation;
    private Coroutine returnRoutine;
    private Renderer[] cachedRenderers;

    public Rigidbody PhysicsBody => body;
    public bool IsReturning => IsSpawned ? returningNetwork.Value : returnRoutine != null;
    public EquippableWorldPhysicsProfileSO Profile => profile;

    private void Awake()
    {
        equippableItem = GetComponent<EquippableItem>();
        body = GetComponent<Rigidbody>();
        profile = equippableItem != null && equippableItem.GetEquippableItemSO() != null
            ? equippableItem.GetEquippableItemSO().worldPhysicsProfile
            : null;
        initialSpawnPosition = transform.position;
        initialSpawnRotation = transform.rotation;
        BuildCompoundColliders();
        ConfigureRigidbody();
    }

    public override void OnNetworkSpawn()
    {
        returningNetwork.OnValueChanged += ReturningNetwork_OnValueChanged;
        ConfigureAuthority();
        ApplyReturningState(returningNetwork.Value);
    }

    public override void OnNetworkDespawn()
    {
        returningNetwork.OnValueChanged -= ReturningNetwork_OnValueChanged;
        RestoreDropperCollisions();
    }

    private void FixedUpdate()
    {
        if (!HasPhysicsAuthority() || collisionGraceStartedAt < 0f)
        {
            return;
        }

        float elapsed = Time.time - collisionGraceStartedAt;
        if (elapsed < GetCollisionGraceMinimumDuration())
        {
            return;
        }

        if (IsSeparatedFromDropper())
        {
            EndDropperCollisionGrace();
            return;
        }

        if (elapsed >= GetCollisionGraceMaximumDuration())
        {
            TryMoveToSafeDropPosition(dropperNetworkObject, GetDropForward());
            EndDropperCollisionGrace();
        }
    }

    public void InitializeDrop(NetworkObject dropper)
    {
        if (!HasPhysicsAuthority())
        {
            return;
        }

        dropperNetworkObject = dropper;
        dropperAttributionExpiresAt = Time.time + (profile != null ? profile.DropperAttributionDuration : 3f);
        Vector3 forward = GetHorizontalForward(dropper != null ? dropper.transform.forward : transform.forward);
        TryMoveToSafeDropPosition(dropper, forward);
        BeginDropperCollisionGrace(dropper);

        body.isKinematic = false;
        body.linearVelocity = forward * (profile != null ? profile.DropForwardVelocity : 2.2f)
            + Vector3.up * (profile != null ? profile.DropUpwardVelocity : 0.8f);
        float angularSpeed = profile != null ? profile.DropAngularVelocity : 1.5f;
        Vector3 spinAxis = Vector3.Cross(forward, Vector3.up).normalized + Vector3.up * 0.35f;
        body.angularVelocity = spinAxis.normalized * angularSpeed;
        body.WakeUp();
    }

    public void RequestPlayerPush(Transform playerTransform, Vector3 worldDirection, Vector3 contactPoint)
    {
        if (playerTransform == null || IsReturning)
        {
            return;
        }

        Vector3 direction = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
        if (direction.sqrMagnitude < 0.01f)
        {
            return;
        }

        direction.Normalize();
        NetworkObject playerNetworkObject = playerTransform.GetComponent<NetworkObject>();
        if (!IsNetworkSessionActive())
        {
            ApplyPlayerPush(direction, contactPoint);
        }
        else if (IsServer)
        {
            if (playerNetworkObject != null)
            {
                TryApplyValidatedPlayerPush(playerNetworkObject.OwnerClientId, direction, contactPoint);
            }
        }
        else if (playerNetworkObject != null && playerNetworkObject.IsOwner)
        {
            RequestPlayerPushRpc(direction, contactPoint);
        }
    }

    public void ReturnToRespawnPoint()
    {
        if (!HasPhysicsAuthority() || IsReturning)
        {
            return;
        }

        if (returnRoutine != null)
        {
            StopCoroutine(returnRoutine);
        }

        returnRoutine = StartCoroutine(ReturnToRespawnPointRoutine());
    }

    public Bounds GetWorldColliderBounds()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool hasBounds = false;
        for (int i = 0; i < itemColliders.Count; i++)
        {
            Collider collider = itemColliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return bounds;
    }

    private IEnumerator ReturnToRespawnPointRoutine()
    {
        SetReturning(true);
        yield return new WaitForSeconds(2f);

        ResolveRespawnPose(out Vector3 position, out Quaternion rotation);
        transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
        body.position = position;
        body.rotation = rotation;
        SetReturning(false);
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }
        returnRoutine = null;
    }

    private void ResolveRespawnPose(out Vector3 position, out Quaternion rotation)
    {
        EquippableItemType itemType = equippableItem != null && equippableItem.GetEquippableItemSO() != null
            ? equippableItem.GetEquippableItemSO().itemType
            : EquippableItemType.None;
        EquippableItemRespawnPoint bestPoint = null;
        foreach (EquippableItemRespawnPoint point in EquippableItemRespawnPoint.Points)
        {
            if (point == null || !point.isActiveAndEnabled || point.ItemType != itemType)
            {
                continue;
            }

            if (!IsRespawnPointOccupied(point)
                && (bestPoint == null || point.Priority < bestPoint.Priority))
            {
                bestPoint = point;
            }
        }

        if (bestPoint != null)
        {
            position = bestPoint.transform.position;
            rotation = bestPoint.transform.rotation;
            return;
        }

        EquippableItemRespawnPoint fallbackPoint = null;
        foreach (EquippableItemRespawnPoint point in EquippableItemRespawnPoint.Points)
        {
            if (point != null && point.isActiveAndEnabled && point.ItemType == itemType
                && (fallbackPoint == null || point.Priority < fallbackPoint.Priority))
            {
                fallbackPoint = point;
            }
        }

        if (fallbackPoint != null)
        {
            for (int ring = 1; ring <= 3; ring++)
            {
                for (int step = 0; step < 8; step++)
                {
                    Vector3 offset = Quaternion.Euler(0f, step * 45f, 0f) * Vector3.forward * (ring * 0.55f);
                    Vector3 candidate = fallbackPoint.transform.position + offset;
                    if (!Physics.CheckSphere(candidate, fallbackPoint.OccupancyRadius * 0.65f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                    {
                        position = candidate;
                        rotation = fallbackPoint.transform.rotation;
                        return;
                    }
                }
            }
        }

        position = initialSpawnPosition;
        rotation = initialSpawnRotation;
    }

    private bool IsRespawnPointOccupied(EquippableItemRespawnPoint point)
    {
        Collider[] overlaps = Physics.OverlapSphere(
            point.transform.position,
            point.OccupancyRadius,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            EquippableWorldPhysics other = overlaps[i] != null
                ? overlaps[i].GetComponentInParent<EquippableWorldPhysics>()
                : null;
            if (other != null && other != this && !other.IsReturning)
            {
                return true;
            }
        }

        return false;
    }

    private void SetReturning(bool value)
    {
        if (IsNetworkSessionActive() && IsSpawned)
        {
            returningNetwork.Value = value;
        }
        else
        {
            ApplyReturningState(value);
        }
    }

    private void ReturningNetwork_OnValueChanged(bool previousValue, bool newValue)
    {
        ApplyReturningState(newValue);
    }

    private void ApplyReturningState(bool returning)
    {
        RestoreDropperCollisions();
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            cachedRenderers[i].enabled = !returning && !IsPlaceholderRenderer(cachedRenderers[i]);
        }

        for (int i = 0; i < itemColliders.Count; i++)
        {
            if (itemColliders[i] != null)
            {
                itemColliders[i].enabled = !returning;
            }
        }

        if (body != null)
        {
            bool shouldBeKinematic = returning || (IsNetworkSessionActive() && !IsServer);
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.isKinematic = shouldBeKinematic;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }

    private bool IsPlaceholderRenderer(Renderer renderer)
    {
        Transform itemVisuals = transform.Find("Item_visuals");
        return renderer != null && itemVisuals != null && renderer.transform == itemVisuals;
    }

    private void BuildCompoundColliders()
    {
        Collider[] existing = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            if (!existing[i].isTrigger)
            {
                existing[i].enabled = false;
            }
        }

        Transform previous = transform.Find(ColliderRootName);
        if (previous != null)
        {
            Destroy(previous.gameObject);
        }

        GameObject colliderRoot = new GameObject(ColliderRootName);
        colliderRoot.transform.SetParent(transform, false);
        colliderRoot.layer = gameObject.layer;

        EquippableColliderShape[] shapes = profile != null ? profile.ColliderShapes : null;
        if (shapes == null || shapes.Length == 0)
        {
            shapes = new[]
            {
                new EquippableColliderShape
                {
                    shapeType = EquippableColliderShapeType.Capsule,
                    center = Vector3.zero,
                    rotationEuler = Vector3.zero,
                    size = new Vector3(0.22f, 1.2f, 0.22f)
                }
            };
        }

        for (int i = 0; i < shapes.Length; i++)
        {
            EquippableColliderShape shape = shapes[i];
            GameObject shapeObject = new GameObject($"Shape_{i}_{shape.shapeType}");
            shapeObject.layer = gameObject.layer;
            shapeObject.transform.SetParent(colliderRoot.transform, false);
            shapeObject.transform.localPosition = shape.center;
            shapeObject.transform.localRotation = Quaternion.Euler(shape.rotationEuler);
            Vector3 safeSize = new Vector3(
                Mathf.Max(0.02f, shape.size.x),
                Mathf.Max(0.02f, shape.size.y),
                Mathf.Max(0.02f, shape.size.z));

            Collider collider;
            if (shape.shapeType == EquippableColliderShapeType.Box)
            {
                BoxCollider box = shapeObject.AddComponent<BoxCollider>();
                box.size = safeSize;
                collider = box;
            }
            else
            {
                CapsuleCollider capsule = shapeObject.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.radius = Mathf.Max(safeSize.x, safeSize.z) * 0.5f;
                capsule.height = Mathf.Max(safeSize.y, capsule.radius * 2f);
                collider = capsule;
            }

            collider.material = profile != null ? profile.PhysicsMaterial : null;
            itemColliders.Add(collider);
        }
    }

    private void ConfigureRigidbody()
    {
        body.mass = profile != null ? profile.Mass : 3f;
        body.linearDamping = profile != null ? profile.LinearDamping : 0.15f;
        body.angularDamping = profile != null ? profile.AngularDamping : 0.6f;
        body.maxAngularVelocity = profile != null ? profile.MaximumAngularVelocity : 20f;
        body.useGravity = true;
        body.detectCollisions = true;
        body.constraints = RigidbodyConstraints.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.None;
        ConfigureAuthority();
    }

    private void ConfigureAuthority()
    {
        if (body != null)
        {
            body.isKinematic = IsReturning || (IsNetworkSessionActive() && !IsServer);
        }
    }

    private void BeginDropperCollisionGrace(NetworkObject dropper)
    {
        if (dropper == null)
        {
            return;
        }

        ignoredDropperNetworkObjectId = dropper.NetworkObjectId;
        collisionGraceStartedAt = Time.time;
        ApplyDropperCollisionIgnore(dropper.gameObject, true);
        if (IsNetworkSessionActive() && IsSpawned)
        {
            SetDropperCollisionIgnoreClientRpc(ignoredDropperNetworkObjectId, true);
        }
    }

    private void EndDropperCollisionGrace()
    {
        ulong previousDropperId = ignoredDropperNetworkObjectId;
        RestoreDropperCollisions();
        if (previousDropperId != NoNetworkObject && IsNetworkSessionActive() && IsSpawned)
        {
            SetDropperCollisionIgnoreClientRpc(previousDropperId, false);
        }
    }

    private void ApplyDropperCollisionIgnore(GameObject dropper, bool ignore)
    {
        if (dropper == null)
        {
            return;
        }

        Collider[] dropperColliders = dropper.GetComponentsInChildren<Collider>(true);
        for (int itemIndex = 0; itemIndex < itemColliders.Count; itemIndex++)
        {
            Collider itemCollider = itemColliders[itemIndex];
            if (itemCollider == null)
            {
                continue;
            }

            for (int dropperIndex = 0; dropperIndex < dropperColliders.Length; dropperIndex++)
            {
                Collider dropperCollider = dropperColliders[dropperIndex];
                if (dropperCollider == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(itemCollider, dropperCollider, ignore);
                if (ignore && !ignoredDropperColliders.Contains(dropperCollider))
                {
                    ignoredDropperColliders.Add(dropperCollider);
                }
            }
        }
    }

    private void RestoreDropperCollisions()
    {
        for (int itemIndex = 0; itemIndex < itemColliders.Count; itemIndex++)
        {
            Collider itemCollider = itemColliders[itemIndex];
            if (itemCollider == null)
            {
                continue;
            }

            for (int dropperIndex = 0; dropperIndex < ignoredDropperColliders.Count; dropperIndex++)
            {
                Collider dropperCollider = ignoredDropperColliders[dropperIndex];
                if (dropperCollider != null)
                {
                    Physics.IgnoreCollision(itemCollider, dropperCollider, false);
                }
            }
        }

        ignoredDropperColliders.Clear();
        ignoredDropperNetworkObjectId = NoNetworkObject;
        collisionGraceStartedAt = -1f;
    }

    private bool IsSeparatedFromDropper()
    {
        if (dropperNetworkObject == null)
        {
            return true;
        }

        Bounds itemBounds = GetWorldColliderBounds();
        itemBounds.Expand(0.02f);
        Collider[] dropperColliders = dropperNetworkObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < dropperColliders.Length; i++)
        {
            if (dropperColliders[i] != null && itemBounds.Intersects(dropperColliders[i].bounds))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryMoveToSafeDropPosition(NetworkObject dropper, Vector3 forward)
    {
        if (dropper == null)
        {
            return false;
        }

        forward = GetHorizontalForward(forward);
        CharacterController controller = dropper.GetComponent<CharacterController>();
        float playerRadius = controller != null ? controller.radius : 0.5f;
        float clearance = profile != null ? profile.DropClearance : 0.2f;
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, 90f);
        transform.rotation = rotation;
        Physics.SyncTransforms();
        Bounds bounds = GetWorldColliderBounds();
        float horizontalExtent = Mathf.Max(bounds.extents.x, bounds.extents.z);
        float baseDistance = playerRadius + horizontalExtent + clearance;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            float distance = baseDistance + attempt * 0.35f;
            Vector3 horizontalPosition = dropper.transform.position + forward * distance;
            if (!TryFindGround(horizontalPosition + Vector3.up * 2f, out RaycastHit groundHit))
            {
                continue;
            }

            Vector3 candidatePosition = horizontalPosition;
            transform.position = candidatePosition;
            Physics.SyncTransforms();
            bounds = GetWorldColliderBounds();
            candidatePosition.y += groundHit.point.y - bounds.min.y + 0.04f;
            transform.position = candidatePosition;
            Physics.SyncTransforms();
            bounds = GetWorldColliderBounds();
            if (!IsDropPlacementBlocked(bounds, dropper.gameObject))
            {
                body.position = candidatePosition;
                body.rotation = rotation;
                return true;
            }
        }

        Vector3 fallback = dropper.transform.position + forward * (baseDistance + 0.35f) + Vector3.up * 1.1f;
        transform.SetPositionAndRotation(fallback, rotation);
        Physics.SyncTransforms();
        body.position = fallback;
        body.rotation = rotation;
        return false;
    }

    private bool TryFindGround(Vector3 origin, out RaycastHit groundHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 5f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider != null && !hits[i].collider.transform.IsChildOf(transform))
            {
                groundHit = hits[i];
                return true;
            }
        }

        groundHit = default;
        return false;
    }

    private bool IsDropPlacementBlocked(Bounds bounds, GameObject dropper)
    {
        Collider[] overlaps = Physics.OverlapBox(
            bounds.center,
            Vector3.Max(bounds.extents - Vector3.one * 0.025f, Vector3.one * 0.01f),
            transform.rotation,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider overlap = overlaps[i];
            if (overlap == null || overlap.transform.IsChildOf(transform) || overlap.transform.IsChildOf(dropper.transform))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!HasPhysicsAuthority() || collision == null || collision.collider == null || IsReturning)
        {
            return;
        }

        NPCBrain npc = collision.collider.GetComponentInParent<NPCBrain>();
        if (npc == null || npc.Agent == null || !npc.Agent.enabled)
        {
            return;
        }

        int npcId = npc.GetInstanceID();
        float cooldown = profile != null ? profile.PushCooldown : 0.1f;
        if (npcPushTimes.TryGetValue(npcId, out float previousTime) && Time.time - previousTime < cooldown)
        {
            return;
        }

        Vector3 desiredVelocity = Vector3.ProjectOnPlane(npc.Agent.velocity, Vector3.up);
        if (desiredVelocity.sqrMagnitude < 0.04f)
        {
            return;
        }

        Vector3 direction = desiredVelocity.normalized;
        float currentSpeed = Vector3.Dot(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up), direction);
        float missingSpeed = desiredVelocity.magnitude - currentSpeed;
        if (missingSpeed <= 0.1f)
        {
            return;
        }

        npcPushTimes[npcId] = Time.time;
        float maximumVelocityChange = profile != null ? profile.NpcPushVelocityChange : 0.45f;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : body.worldCenterOfMass;
        body.AddForceAtPosition(direction * Mathf.Min(missingSpeed, maximumVelocityChange), point, ForceMode.VelocityChange);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!HasPhysicsAuthority() || collision == null || collision.collider == null || IsReturning)
        {
            return;
        }

        float speed = collision.relativeVelocity.magnitude;
        float threshold = profile != null ? profile.ImpactDamageSpeedThreshold : 6f;
        if (speed <= threshold)
        {
            return;
        }

        PlayerHealth playerHealth = collision.collider.GetComponentInParent<PlayerHealth>();
        NPCHealth npcHealth = collision.collider.GetComponentInParent<NPCHealth>();
        if (playerHealth == null && npcHealth == null)
        {
            return;
        }

        Object target = playerHealth != null ? playerHealth : npcHealth;
        int targetId = target.GetInstanceID();
        float cooldown = profile != null ? profile.ImpactDamageCooldown : 0.75f;
        if (impactDamageTimes.TryGetValue(targetId, out float previousTime) && Time.time - previousTime < cooldown)
        {
            return;
        }

        float damagePerSpeed = profile != null ? profile.ImpactDamagePerSpeed : 2f;
        float minimumDamage = profile != null ? profile.MinimumImpactDamage : 2f;
        float maximumDamage = profile != null ? profile.MaximumImpactDamage : 12f;
        float damage = Mathf.Clamp((speed - threshold) * damagePerSpeed, minimumDamage, maximumDamage);
        NetworkObject attacker = dropperNetworkObject != null && Time.time <= dropperAttributionExpiresAt
            ? dropperNetworkObject
            : null;
        impactDamageTimes[targetId] = Time.time;
        if (playerHealth != null)
        {
            playerHealth.DamageReceived(damage, attacker);
        }
        else if (npcHealth != null && !npcHealth.IsDead)
        {
            npcHealth.DamageReceived(damage, attacker);
        }
    }

    private void TryApplyValidatedPlayerPush(ulong clientId, Vector3 direction, Vector3 contactPoint)
    {
        if (!IsServer || NetworkManager.Singleton == null
            || !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
            || client.PlayerObject == null
            || Vector3.Distance(client.PlayerObject.transform.position, transform.position) > 2.5f
            || !TryResolveValidatedContactPoint(client.PlayerObject.transform, direction, contactPoint, out Vector3 validatedPoint))
        {
            return;
        }

        float cooldown = profile != null ? profile.PushCooldown : 0.1f;
        if (playerPushTimes.TryGetValue(clientId, out float previousTime) && Time.time - previousTime < cooldown)
        {
            return;
        }

        playerPushTimes[clientId] = Time.time;
        ApplyPlayerPush(direction, validatedPoint);
    }

    private bool TryResolveValidatedContactPoint(
        Transform player,
        Vector3 direction,
        Vector3 requestedPoint,
        out Vector3 validatedPoint)
    {
        validatedPoint = body != null ? body.worldCenterOfMass : transform.position;
        Vector3 toItem = Vector3.ProjectOnPlane(validatedPoint - player.position, Vector3.up);
        if (toItem.sqrMagnitude < 0.01f || Vector3.Dot(toItem.normalized, direction.normalized) < 0.15f)
        {
            return false;
        }

        float bestDistanceSquared = float.PositiveInfinity;
        for (int i = 0; i < itemColliders.Count; i++)
        {
            Collider itemCollider = itemColliders[i];
            if (itemCollider == null || !itemCollider.enabled)
            {
                continue;
            }

            Vector3 candidate = itemCollider.ClosestPoint(requestedPoint);
            float distanceSquared = (candidate - requestedPoint).sqrMagnitude;
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                validatedPoint = candidate;
            }
        }

        return bestDistanceSquared <= 0.75f * 0.75f
            && Vector3.Distance(player.position, validatedPoint) <= 2.5f;
    }

    private void ApplyPlayerPush(Vector3 direction, Vector3 contactPoint)
    {
        if (body == null || body.isKinematic)
        {
            return;
        }

        float velocityChange = profile != null ? profile.PlayerPushVelocityChange : 0.7f;
        body.AddForceAtPosition(direction.normalized * velocityChange, contactPoint, ForceMode.VelocityChange);
        body.WakeUp();
    }

    private bool HasPhysicsAuthority()
    {
        return !IsNetworkSessionActive() || IsServer;
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private float GetCollisionGraceMinimumDuration()
    {
        return profile != null ? profile.CollisionGraceMinimumDuration : 0.2f;
    }

    private float GetCollisionGraceMaximumDuration()
    {
        return profile != null ? profile.CollisionGraceMaximumDuration : 1.5f;
    }

    private Vector3 GetDropForward()
    {
        return GetHorizontalForward(dropperNetworkObject != null ? dropperNetworkObject.transform.forward : transform.forward);
    }

    private static Vector3 GetHorizontalForward(Vector3 forward)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(forward, Vector3.up);
        return horizontal.sqrMagnitude > 0.001f ? horizontal.normalized : Vector3.forward;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPlayerPushRpc(Vector3 direction, Vector3 contactPoint, RpcParams rpcParams = default)
    {
        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (direction.sqrMagnitude > 0.01f)
        {
            TryApplyValidatedPlayerPush(rpcParams.Receive.SenderClientId, direction.normalized, contactPoint);
        }
    }

    [ClientRpc]
    private void SetDropperCollisionIgnoreClientRpc(ulong dropperId, bool ignore)
    {
        if (IsServer || NetworkManager.Singleton == null
            || !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(dropperId, out NetworkObject dropper))
        {
            return;
        }

        ApplyDropperCollisionIgnore(dropper.gameObject, ignore);
        if (!ignore)
        {
            ignoredDropperColliders.Clear();
        }
    }
}
