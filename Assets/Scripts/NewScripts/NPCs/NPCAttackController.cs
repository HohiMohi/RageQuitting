using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NPCAttackController : NetworkBehaviour
{
    [SerializeField] private Transform attackOrigin;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackRange = 1.6f;
    [SerializeField] private float attackAngle = 90f;
    [SerializeField] private LayerMask attackTargetLayers = ~0;
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private float attackOriginHeight = 0.6f;

    private NPCBrain brain;
    private NPCFactionMember factionMember;

    public float AttackRange => Mathf.Max(0.1f, attackRange);

    private void Awake()
    {
        brain = GetComponent<NPCBrain>();
        factionMember = GetComponent<NPCFactionMember>();
    }

    public void PerformAttack()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && !IsServer)
        {
            return;
        }

        Vector3 origin = GetAttackOrigin();
        Vector3 forward = transform.forward;
        float minimumDot = Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(attackAngle, 1f, 360f) * 0.5f);
        Collider[] colliders = Physics.OverlapSphere(origin, Mathf.Max(0.1f, attackRange), attackTargetLayers, QueryTriggerInteraction.Ignore);
        HashSet<Component> damagedTargets = new HashSet<Component>();

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

            if (!damagedTargets.Add(target))
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

            DamageTarget(target);
        }
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

    private void DamageTarget(Component target)
    {
        if (target is PlayerHealth playerHealth)
        {
            playerHealth.DamageReceived(attackDamage, NetworkObject);
            return;
        }

        if (target is NPCHealth npcHealth)
        {
            npcHealth.DamageReceived(attackDamage, NetworkObject);
        }
    }
}
