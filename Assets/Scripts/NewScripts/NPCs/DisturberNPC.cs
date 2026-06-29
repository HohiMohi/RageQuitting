using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class DisturberNPC : MonoBehaviour, IDamageable
{
    public enum NPCState
    {
        Patrolling,
        Stealing,
        Sabotaging,
        Fleeing,
        Stunned
    }

    [Header("NPC Configuration")]
    [SerializeField] private float speed = 4f;
    [SerializeField] private float detectionRadius = 15f;
    [SerializeField] private float pickupDistance = 1.5f;
    [SerializeField] private float stunDuration = 1.5f;
    [SerializeField] private Transform carrySocket; // Socket to attach stolen items visually

    private NavMeshAgent agent;
    private NPCState currentState = NPCState.Patrolling;
    private Vector3 burrowPosition;
    private GameObject stolenItem;
    private Minigame targetMinigame;
    private float stunTimer = 0f;
    private Vector3 patrolTarget;
    private float patrolTimer = 0f;
    private float selectNewPatrolTargetCooldown = 3f;

    private Renderer[] renderers;
    private Color[] originalColors;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = speed;
        burrowPosition = transform.position; // By default, burrow is where they spawn
        renderers = GetComponentsInChildren<Renderer>();
        if (renderers != null)
        {
            originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].material.HasProperty("_Color"))
                {
                    originalColors[i] = renderers[i].material.color;
                }
            }
        }
    }

    private void Start()
    {
        SetState(NPCState.Patrolling);
    }

    public void InitializeBurrow(Vector3 position)
    {
        burrowPosition = position;
    }

    private void Update()
    {
        switch (currentState)
        {
            case NPCState.Patrolling:
                UpdatePatrol();
                break;
            case NPCState.Stealing:
                UpdateSteal();
                break;
            case NPCState.Sabotaging:
                UpdateSabotage();
                break;
            case NPCState.Fleeing:
                UpdateFlee();
                break;
            case NPCState.Stunned:
                UpdateStun();
                break;
        }
    }

    private void SetState(NPCState newState)
    {
        currentState = newState;
        switch (newState)
        {
            case NPCState.Patrolling:
                agent.isStopped = false;
                SelectPatrolTarget();
                break;
            case NPCState.Stealing:
                agent.isStopped = false;
                break;
            case NPCState.Sabotaging:
                agent.isStopped = false;
                break;
            case NPCState.Fleeing:
                agent.isStopped = false;
                agent.SetDestination(burrowPosition);
                break;
            case NPCState.Stunned:
                agent.isStopped = true;
                stunTimer = stunDuration;
                StartCoroutine(FlashRedEffect());
                break;
        }
    }

    private void UpdatePatrol()
    {
        // 1. Search for active minigames to sabotage first (higher priority for disruption)
        Minigame activeMinigame = FindClosestActiveMinigame();
        if (activeMinigame != null)
        {
            targetMinigame = activeMinigame;
            SetState(NPCState.Sabotaging);
            return;
        }

        // 2. Search for items to steal
        GameObject targetItem = FindClosestStealableItem();
        if (targetItem != null)
        {
            stolenItem = targetItem;
            SetState(NPCState.Stealing);
            return;
        }

        patrolTimer -= Time.deltaTime;
        if (patrolTimer <= 0f || (agent.remainingDistance < 1f && !agent.pathPending))
        {
            SelectPatrolTarget();
        }
    }

    private void UpdateSteal()
    {
        if (stolenItem == null || stolenItem.transform.parent != null)
        {
            // Item was picked up by player or destroyed, go back to patrolling
            stolenItem = null;
            SetState(NPCState.Patrolling);
            return;
        }

        agent.SetDestination(stolenItem.transform.position);

        float distance = Vector3.Distance(transform.position, stolenItem.transform.position);
        if (distance <= pickupDistance)
        {
            PerformPickup();
        }
    }

    private void UpdateSabotage()
    {
        if (targetMinigame == null || !targetMinigame.IsActive())
        {
            targetMinigame = null;
            SetState(NPCState.Patrolling);
            return;
        }

        agent.SetDestination(targetMinigame.transform.position);

        float distance = Vector3.Distance(transform.position, targetMinigame.transform.position);
        if (distance <= pickupDistance)
        {
            PerformSabotage();
        }
    }

    private void PerformSabotage()
    {
        if (targetMinigame == null) return;

        Debug.Log($"NPC successfully sabotaged minigame: {targetMinigame.gameObject.name}!");
        targetMinigame.MinigameFailed();
        targetMinigame = null;
        SetState(NPCState.Fleeing); // Flee after successful sabotage
    }

    private Minigame FindClosestActiveMinigame()
    {
        Minigame[] minigames = Object.FindObjectsByType<Minigame>(FindObjectsSortMode.None);
        Minigame closestMinigame = null;
        float minDistance = float.MaxValue;

        foreach (var mg in minigames)
        {
            if (mg != null && mg.IsActive())
            {
                float dist = Vector3.Distance(transform.position, mg.transform.position);
                if (dist < detectionRadius && dist < minDistance)
                {
                    minDistance = dist;
                    closestMinigame = mg;
                }
            }
        }

        return closestMinigame;
    }

    private void UpdateFlee()
    {
        if (agent.remainingDistance < 1.5f && !agent.pathPending)
        {
            // Reached burrow
            if (stolenItem != null)
            {
                Debug.Log($"NPC burrowed and consumed stolen item: {stolenItem.name}");
                Destroy(stolenItem);
            }
            Destroy(gameObject);
        }
    }

    private void UpdateStun()
    {
        stunTimer -= Time.deltaTime;
        if (stunTimer <= 0f)
        {
            SetState(NPCState.Fleeing); // Flee empty-handed after stun
        }
    }

    private void SelectPatrolTarget()
    {
        Vector3 randomDirection = Random.insideUnitSphere * 10f;
        randomDirection += transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            patrolTarget = hit.position;
            agent.SetDestination(patrolTarget);
        }
        patrolTimer = selectNewPatrolTargetCooldown;
    }

    private GameObject FindClosestStealableItem()
    {
        Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRadius);
        GameObject closestItem = null;
        float minDistance = float.MaxValue;

        foreach (var col in colliders)
        {
            bool isStealable = col.GetComponent<BaseResourceNew>() != null || col.GetComponent<MountableBridgeComponent>() != null;
            if (isStealable && col.transform.parent == null)
            {
                float dist = Vector3.Distance(transform.position, col.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestItem = col.gameObject;
                }
            }
        }

        return closestItem;
    }

    private void PerformPickup()
    {
        if (stolenItem == null) return;

        // Disable physics/collider on item
        if (stolenItem.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        if (stolenItem.TryGetComponent<Collider>(out Collider col))
        {
            col.enabled = false;
        }

        // Parent to carrySocket
        Transform parentTarget = carrySocket != null ? carrySocket : transform;
        stolenItem.transform.SetParent(parentTarget);
        stolenItem.transform.localPosition = Vector3.zero;
        stolenItem.transform.localRotation = Quaternion.identity;

        Debug.Log($"NPC stole item: {stolenItem.name}");
        SetState(NPCState.Fleeing);
    }

    private void DropStolenItem()
    {
        if (stolenItem == null) return;

        stolenItem.transform.SetParent(null);
        
        if (stolenItem.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            // Throw it slightly away
            rb.AddForce((transform.forward - transform.up) * 2f, ForceMode.Impulse);
        }
        if (stolenItem.TryGetComponent<Collider>(out Collider col))
        {
            col.enabled = true;
        }

        // Notify item it was dropped
        if (stolenItem.TryGetComponent<IPIckableNew>(out IPIckableNew pickable))
        {
            pickable.DroppedDown();
        }

        stolenItem = null;
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        TakeSlap();
    }

    public void DamageReceived(float damage)
    {
        TakeSlap();
    }

    private void TakeSlap()
    {
        if (currentState == NPCState.Stunned) return;

        Debug.Log($"NPC was slapped! Dropping item and fleeing.");
        DropStolenItem();
        SetState(NPCState.Stunned);
    }

    private IEnumerator FlashRedEffect()
    {
        if (renderers == null) yield break;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].material.HasProperty("_Color"))
            {
                renderers[i].material.color = Color.red;
            }
        }

        yield return new WaitForSeconds(0.5f);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].material.HasProperty("_Color"))
            {
                renderers[i].material.color = originalColors[i];
            }
        }
    }
}
