using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NPCAttackController : NetworkBehaviour
{
    public readonly struct AttackExecutionOptions
    {
        public AttackExecutionOptions(
            float animationSpeed,
            float damageMultiplier,
            Vector3 attackForward,
            NetworkObject primaryTarget = null,
            ExternalImpulseProfileSO impulseProfile = null)
        {
            AnimationSpeed = NPCActionTiming.ClampPlaybackSpeed(animationSpeed);
            DamageMultiplier = Mathf.Max(0f, damageMultiplier);
            AttackForward = Vector3.ProjectOnPlane(attackForward, Vector3.up).normalized;
            PrimaryTarget = primaryTarget;
            ImpulseProfile = impulseProfile;
        }

        public float AnimationSpeed { get; }
        public float DamageMultiplier { get; }
        public Vector3 AttackForward { get; }
        public NetworkObject PrimaryTarget { get; }
        public ExternalImpulseProfileSO ImpulseProfile { get; }
    }

    public readonly struct AttackExecutionResult
    {
        public AttackExecutionResult(bool cancelled, int hitCount, bool primaryTargetHit)
        {
            Cancelled = cancelled;
            HitCount = hitCount;
            PrimaryTargetHit = primaryTargetHit;
        }

        public bool Cancelled { get; }
        public int HitCount { get; }
        public bool PrimaryTargetHit { get; }
    }

    private enum PendingAttackType
    {
        None,
        Combat,
        Resource,
        TargetedCombat,
        ConfiguredCombat
    }

    [SerializeField] private Transform attackOrigin;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackRange = 1.6f;
    [SerializeField] private float attackAngle = 90f;
    [SerializeField] private float attackDamageDelay = 0.35f;
    [SerializeField] private LayerMask attackTargetLayers = ~0;
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private float attackOriginHeight = 0.6f;

    private NPCBrain brain;
    private NPCHealth health;
    private NPCFactionMember factionMember;
    private PendingAttackType pendingAttackType;
    private float pendingAttackTime;
    private BaseResourceNew pendingResourceTarget;
    private EquippableItemType pendingResourceToolType;
    private NetworkObject pendingCombatTarget;
    private Func<NetworkObject, bool> pendingCombatValidation;
    private Action<NetworkObject, bool> pendingTargetedAttackCompleted;
    private AttackExecutionOptions pendingConfiguredOptions;
    private Action<AttackExecutionResult> pendingConfiguredAttackCompleted;

    public float AttackRange => Mathf.Max(0.1f, attackRange);
    public float AttackDamage => Mathf.Max(0f, attackDamage);
    public float AttackDamageDelay => Mathf.Max(0f, attackDamageDelay);

    public static float CalculateEffectiveImpactDelay(float canonicalDelay, float animationSpeed)
    {
        return NPCActionTiming.CalculateEffectiveImpactDelay(canonicalDelay, animationSpeed);
    }

    private void Awake()
    {
        brain = GetComponent<NPCBrain>();
        health = GetComponent<NPCHealth>();
        factionMember = GetComponent<NPCFactionMember>();
    }

    private void Update()
    {
        if (pendingAttackType == PendingAttackType.None || Time.time < pendingAttackTime)
        {
            return;
        }

        PendingAttackType attackType = pendingAttackType;
        pendingAttackType = PendingAttackType.None;
        if (attackType == PendingAttackType.Resource)
        {
            PerformResourceAttackImmediate();
            return;
        }

        if (attackType == PendingAttackType.TargetedCombat)
        {
            PerformTargetedCombatAttackImmediate();
            return;
        }

        if (attackType == PendingAttackType.ConfiguredCombat)
        {
            PerformConfiguredAttackImmediate();
            return;
        }

        PerformAttackImmediate();
    }

    public void StartAttack()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && !IsServer)
        {
            return;
        }

        if (health != null && health.IsDead)
        {
            CancelPendingAttack();
            return;
        }

        pendingAttackType = PendingAttackType.Combat;
        pendingAttackTime = Time.time + Mathf.Max(0f, attackDamageDelay);
    }

    public bool StartAttack(AttackExecutionOptions options, Action<AttackExecutionResult> completed)
    {
        if (!CanRunServerAuthoritativeAttack())
        {
            return false;
        }

        CancelPendingAttack();
        pendingConfiguredOptions = options;
        pendingConfiguredAttackCompleted = completed;
        pendingAttackType = PendingAttackType.ConfiguredCombat;
        pendingAttackTime = Time.time + CalculateEffectiveImpactDelay(AttackDamageDelay, options.AnimationSpeed);
        return true;
    }

    public bool StartResourceAttack(BaseResourceNew target, EquippableItemType toolType)
    {
        if (!CanRunServerAuthoritativeAttack() || target == null || !target.CanBeDestroyedWith(toolType))
        {
            return false;
        }

        pendingResourceTarget = target;
        pendingResourceToolType = toolType;
        pendingAttackType = PendingAttackType.Resource;
        pendingAttackTime = Time.time + Mathf.Max(0f, attackDamageDelay);
        return true;
    }

    public bool StartTargetedAttack(PlayerHealth target, Action<PlayerHealth, bool> completed)
    {
        return StartTargetedAttack(target, null, completed);
    }

    public bool StartTargetedAttack(
        PlayerHealth target,
        Func<PlayerHealth, bool> additionalValidation,
        Action<PlayerHealth, bool> completed)
    {
        if (target == null)
        {
            return false;
        }

        return StartTargetedAttack(
            target.NetworkObject,
            networkTarget => additionalValidation == null || additionalValidation(target),
            (networkTarget, hit) => completed?.Invoke(target, hit));
    }

    public bool StartTargetedAttack(NetworkObject target, Action<NetworkObject, bool> completed)
    {
        return StartTargetedAttack(target, null, completed);
    }

    public bool StartTargetedAttack(
        NetworkObject target,
        Func<NetworkObject, bool> additionalValidation,
        Action<NetworkObject, bool> completed)
    {
        if (!CanRunServerAuthoritativeAttack() || !IsValidTargetedCombatTarget(target))
        {
            return false;
        }

        pendingCombatTarget = target;
        pendingCombatValidation = additionalValidation;
        pendingTargetedAttackCompleted = completed;
        pendingAttackType = PendingAttackType.TargetedCombat;
        pendingAttackTime = Time.time + Mathf.Max(0f, attackDamageDelay);
        return true;
    }

    public void CancelPendingResourceAttack()
    {
        if (pendingAttackType == PendingAttackType.Resource)
        {
            CancelPendingAttack();
        }
    }

    private void PerformAttackImmediate()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && !IsServer)
        {
            return;
        }

        if (health != null && health.IsDead)
        {
            return;
        }

        PerformConeAttack(1f, transform.forward, null, null);
    }

    private void PerformConfiguredAttackImmediate()
    {
        AttackExecutionOptions options = pendingConfiguredOptions;
        Action<AttackExecutionResult> completed = pendingConfiguredAttackCompleted;
        pendingConfiguredAttackCompleted = null;

        if (!CanRunServerAuthoritativeAttack()
            || (options.PrimaryTarget != null && !IsValidTargetedCombatTarget(options.PrimaryTarget)))
        {
            completed?.Invoke(new AttackExecutionResult(true, 0, false));
            return;
        }

        Vector3 forward = options.AttackForward.sqrMagnitude > 0.0001f
            ? options.AttackForward
            : transform.forward;
        AttackExecutionResult result = PerformConeAttack(
            options.DamageMultiplier,
            forward,
            options.PrimaryTarget,
            options.ImpulseProfile);
        completed?.Invoke(result);
    }

    private AttackExecutionResult PerformConeAttack(
        float damageMultiplier,
        Vector3 forward,
        NetworkObject primaryTarget,
        ExternalImpulseProfileSO impulseProfile)
    {
        Vector3 origin = GetAttackOrigin();
        float minimumDot = Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(attackAngle, 1f, 360f) * 0.5f);
        Collider[] colliders = Physics.OverlapSphere(origin, Mathf.Max(0.1f, attackRange), attackTargetLayers, QueryTriggerInteraction.Ignore);
        HashSet<Component> damagedTargets = new HashSet<Component>();
        int hitCount = 0;
        bool primaryTargetHit = false;

        foreach (Collider collider in colliders)
        {
            if (collider == null || collider.transform.root == transform.root)
            {
                continue;
            }

            if (!TryGetDamageTarget(collider, out Component target, out Transform targetTransform))
            {
                continue;
            }

            if (damagedTargets.Contains(target))
            {
                continue;
            }

            Vector3 targetPoint = GetTargetPoint(collider, targetTransform, origin);
            Vector3 directionToTarget = targetPoint - origin;
            if (!IsInAttackCone(directionToTarget, forward, minimumDot))
            {
                continue;
            }

            if (requireLineOfSight && !HasLineOfSight(origin, directionToTarget, targetTransform.root))
            {
                continue;
            }

            damagedTargets.Add(target);
            DamageTarget(target, Mathf.Max(0f, damageMultiplier));
            NetworkObject hitNetworkObject = target.GetComponent<NetworkObject>();
            if (hitNetworkObject == null)
            {
                hitNetworkObject = target.GetComponentInParent<NetworkObject>();
            }
            primaryTargetHit |= primaryTarget != null && hitNetworkObject == primaryTarget;
            hitCount++;

            if (impulseProfile != null && hitNetworkObject != null)
            {
                ApplyImpulse(hitNetworkObject, impulseProfile, forward);
            }
        }

        return new AttackExecutionResult(false, hitCount, primaryTargetHit);
    }

    private void PerformResourceAttackImmediate()
    {
        BaseResourceNew target = pendingResourceTarget;
        EquippableItemType toolType = pendingResourceToolType;
        pendingResourceTarget = null;

        if (!CanRunServerAuthoritativeAttack() || target == null || !target.CanBeDestroyedWith(toolType))
        {
            return;
        }

        Vector3 origin = GetAttackOrigin();
        if (!TryGetResourceTargetPoint(target, origin, out Vector3 targetPoint, out Transform targetRoot))
        {
            return;
        }

        Vector3 directionToTarget = targetPoint - origin;
        if (directionToTarget.magnitude > Mathf.Max(0.1f, attackRange))
        {
            return;
        }

        float minimumDot = Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(attackAngle, 1f, 360f) * 0.5f);
        if (!IsInAttackCone(directionToTarget, transform.forward, minimumDot))
        {
            return;
        }

        if (requireLineOfSight && !HasLineOfSight(origin, directionToTarget, targetRoot))
        {
            return;
        }

        target.TryDamageFromNpc(toolType, attackDamage);
    }

    private void PerformTargetedCombatAttackImmediate()
    {
        NetworkObject target = pendingCombatTarget;
        Func<NetworkObject, bool> additionalValidation = pendingCombatValidation;
        Action<NetworkObject, bool> completed = pendingTargetedAttackCompleted;
        ClearPendingTargetedAttack();

        bool hit = CanRunServerAuthoritativeAttack()
            && IsValidTargetedCombatTarget(target)
            && (additionalValidation == null || additionalValidation(target))
            && TryValidateTargetedCombatHit(target);
        if (hit)
        {
            DamageTargetedCombatTarget(target);
        }

        completed?.Invoke(target, hit);
    }

    private bool TryValidateTargetedCombatHit(NetworkObject target)
    {
        Collider targetCollider = target.GetComponent<Collider>();
        targetCollider ??= target.GetComponentInChildren<Collider>();
        Vector3 origin = GetAttackOrigin();
        Vector3 targetPoint = targetCollider != null
            ? GetTargetPoint(targetCollider, target.transform, origin)
            : target.transform.position;
        Vector3 directionToTarget = targetPoint - origin;
        if (directionToTarget.magnitude > Mathf.Max(0.1f, attackRange))
        {
            return false;
        }

        float minimumDot = Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(attackAngle, 1f, 360f) * 0.5f);
        if (!IsInAttackCone(directionToTarget, transform.forward, minimumDot))
        {
            return false;
        }

        return !requireLineOfSight || HasLineOfSight(origin, directionToTarget, target.transform.root);
    }

    private bool CanRunServerAuthoritativeAttack()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && !IsServer)
        {
            return false;
        }

        return health == null || !health.IsDead;
    }

    private void CancelPendingAttack()
    {
        Action<AttackExecutionResult> configuredCompleted = pendingConfiguredAttackCompleted;
        pendingAttackType = PendingAttackType.None;
        pendingResourceTarget = null;
        pendingConfiguredAttackCompleted = null;
        ClearPendingTargetedAttack();
        configuredCompleted?.Invoke(new AttackExecutionResult(true, 0, false));
    }

    private void ClearPendingTargetedAttack()
    {
        pendingCombatTarget = null;
        pendingCombatValidation = null;
        pendingTargetedAttackCompleted = null;
    }

    private bool IsValidTargetedCombatTarget(NetworkObject target)
    {
        if (target == null || target.transform.root == transform.root)
        {
            return false;
        }

        if (target.TryGetComponent(out PlayerHealth playerHealth))
        {
            return !playerHealth.IsDowned
                && playerHealth.gameObject.activeInHierarchy
                && CanDamageFaction(playerHealth.GetComponent<NPCFactionMember>());
        }

        if (target.TryGetComponent(out NPCHealth npcHealth))
        {
            return !npcHealth.IsDead
                && npcHealth.gameObject.activeInHierarchy
                && CanDamageFaction(npcHealth.GetComponent<NPCFactionMember>());
        }

        return false;
    }

    private void DamageTargetedCombatTarget(NetworkObject target)
    {
        if (target.TryGetComponent(out PlayerHealth playerHealth))
        {
            playerHealth.DamageReceived(attackDamage, NetworkObject);
            return;
        }

        if (target.TryGetComponent(out NPCHealth npcHealth))
        {
            npcHealth.DamageReceived(attackDamage, NetworkObject);
        }
    }

    public void CancelPendingAttacks()
    {
        CancelPendingAttack();
    }

    private Vector3 GetAttackOrigin()
    {
        return attackOrigin != null ? attackOrigin.position : transform.position + Vector3.up * attackOriginHeight;
    }

    private bool TryGetDamageTarget(Collider collider, out Component target, out Transform targetTransform)
    {
        target = null;
        targetTransform = null;

        PlayerHealth playerHealth = collider.GetComponent<PlayerHealth>();
        playerHealth ??= collider.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            if (playerHealth.IsDowned || !CanDamageFaction(playerHealth.GetComponent<NPCFactionMember>()))
            {
                return false;
            }

            target = playerHealth;
            targetTransform = playerHealth.transform;
            return true;
        }

        NPCHealth npcHealth = collider.GetComponent<NPCHealth>();
        npcHealth ??= collider.GetComponentInParent<NPCHealth>();
        if (npcHealth != null)
        {
            if (npcHealth.IsDead || npcHealth.transform.root == transform.root || !CanDamageFaction(npcHealth.GetComponent<NPCFactionMember>()))
            {
                return false;
            }

            target = npcHealth;
            targetTransform = npcHealth.transform;
            return true;
        }

        return false;
    }

    private bool CanDamageFaction(NPCFactionMember targetFactionMember)
    {
        if (brain == null || factionMember == null || targetFactionMember == null)
        {
            return true;
        }

        return brain.GetRelationTo(targetFactionMember.Faction) != NPCFactionRelation.Ally;
    }

    private static Vector3 GetTargetPoint(Collider collider, Transform targetTransform, Vector3 origin)
    {
        Vector3 closestPoint = collider.ClosestPoint(origin);
        if ((closestPoint - origin).sqrMagnitude > 0.0001f)
        {
            return closestPoint;
        }

        return targetTransform.position;
    }

    private bool TryGetResourceTargetPoint(BaseResourceNew target, Vector3 origin, out Vector3 targetPoint, out Transform targetRoot)
    {
        targetPoint = default;
        targetRoot = null;
        if (target == null || target.transform.root == transform.root)
        {
            return false;
        }

        Collider targetCollider = target.GetComponent<Collider>();
        targetCollider ??= target.GetComponentInChildren<Collider>();
        if (targetCollider != null)
        {
            if ((attackTargetLayers.value & (1 << targetCollider.gameObject.layer)) == 0)
            {
                return false;
            }

            targetPoint = GetTargetPoint(targetCollider, target.transform, origin);
            targetRoot = targetCollider.transform.root;
            return true;
        }

        if ((attackTargetLayers.value & (1 << target.gameObject.layer)) == 0)
        {
            return false;
        }

        targetPoint = target.transform.position;
        targetRoot = target.transform.root;
        return true;
    }

    private static bool IsInAttackCone(Vector3 directionToTarget, Vector3 forward, float minimumDot)
    {
        Vector3 flatDirection = Vector3.ProjectOnPlane(directionToTarget, Vector3.up);
        if (flatDirection.sqrMagnitude < 0.0001f)
        {
            return true;
        }

        return Vector3.Dot(forward.normalized, flatDirection.normalized) >= minimumDot;
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 directionToTarget, Transform targetRoot)
    {
        float distance = directionToTarget.magnitude;
        if (distance <= 0.0001f)
        {
            return true;
        }

        RaycastHit[] hits = Physics.RaycastAll(origin, directionToTarget / distance, distance, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.root == transform.root)
            {
                continue;
            }

            return hit.collider.transform.root == targetRoot;
        }

        return true;
    }

    private void DamageTarget(Component target, float damageMultiplier = 1f)
    {
        float damage = attackDamage * Mathf.Max(0f, damageMultiplier);
        if (target is PlayerHealth playerHealth)
        {
            playerHealth.DamageReceived(damage, NetworkObject);
            return;
        }

        if (target is NPCHealth npcHealth)
        {
            npcHealth.DamageReceived(damage, NetworkObject);
        }
    }

    private void ApplyImpulse(NetworkObject target, ExternalImpulseProfileSO profile, Vector3 direction)
    {
        MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is IExternalImpulseReceiver receiver)
            {
                receiver.TryApplyExternalImpulse(profile.CreateImpulse(direction), NetworkObject);
                return;
            }
        }
    }

    protected virtual void OnDisable()
    {
        CancelPendingAttack();
    }

    public override void OnNetworkDespawn()
    {
        CancelPendingAttack();
        base.OnNetworkDespawn();
    }
}
