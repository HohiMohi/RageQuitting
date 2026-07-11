using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NPCCarrier))]
[RequireComponent(typeof(NPCHealth))]
[RequireComponent(typeof(NPCFactionMember))]
public class NPCBrain : NetworkBehaviour
{
    [SerializeField] private NPCDefinitionSO definition;
    [SerializeField] private NPCFactionRelationshipMatrixSO relationshipMatrix;
    [SerializeField] private Transform visualRoot;

    private NPCBehaviorSO behaviorOverride;
    private NavMeshAgent agent;
    private NPCCarrier carrier;
    private NPCHealth health;
    private NPCFactionMember factionMember;
    private NPCBehaviorController behaviorController;
    private float tickTimer;

    public NPCDefinitionSO Definition => definition;
    public NPCFactionRelationshipMatrixSO RelationshipMatrix => relationshipMatrix;
    public NavMeshAgent Agent => agent;
    public NPCCarrier Carrier => carrier;
    public NPCHealth Health => health;
    public NPCFactionMember FactionMember => factionMember;
    public float DetectionRadius => definition != null ? definition.detectionRadius : 12f;
    public float InteractionDistance => definition != null ? definition.interactionDistance : 1.4f;
    public float PatrolRadius => definition != null ? definition.patrolRadius : 8f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        carrier = GetComponent<NPCCarrier>();
        health = GetComponent<NPCHealth>();
        factionMember = GetComponent<NPCFactionMember>();
        ApplyDefinition();
    }

    public override void OnNetworkSpawn()
    {
        ApplyDefinition();
        RebuildBehavior();
    }

    private void Start()
    {
        if (!IsNetworkSessionActive())
        {
            RebuildBehavior();
        }
    }

    private void Update()
    {
        if (!ShouldRunBrain())
        {
            return;
        }

        float tickInterval = definition != null ? Mathf.Max(0.05f, definition.decisionTickInterval) : 0.2f;
        tickTimer -= Time.deltaTime;
        if (tickTimer > 0f)
        {
            return;
        }

        tickTimer = tickInterval;
        behaviorController?.Tick();
    }

    public void SetDefinition(NPCDefinitionSO newDefinition)
    {
        definition = newDefinition;
        ApplyDefinition();
        RebuildBehavior();
    }

    public void SetBehavior(NPCBehaviorSO behavior)
    {
        behaviorOverride = behavior;
        RebuildBehavior();
    }

    public NPCFactionRelation GetRelationTo(NPCFactionSO targetFaction)
    {
        if (relationshipMatrix == null || factionMember == null)
        {
            return NPCFactionRelation.Neutral;
        }

        return relationshipMatrix.GetRelation(factionMember.Faction, targetFaction);
    }

    private void ApplyDefinition()
    {
        if (definition == null)
        {
            return;
        }

        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        agent.speed = definition.moveSpeed;
        agent.acceleration = definition.acceleration;
        agent.angularSpeed = definition.angularSpeed;

        if (health == null)
        {
            health = GetComponent<NPCHealth>();
        }
        health.Configure(definition.maxHealth);

        if (factionMember == null)
        {
            factionMember = GetComponent<NPCFactionMember>();
        }
        factionMember.SetFaction(definition.faction);

        EnsureVisual();
    }

    private void RebuildBehavior()
    {
        behaviorController?.Exit();
        NPCBehaviorSO behavior = behaviorOverride != null ? behaviorOverride : definition != null ? definition.behavior : null;
        behaviorController = behavior != null ? behavior.CreateController(this) : null;
        behaviorController?.Enter();
    }

    private void EnsureVisual()
    {
        if (definition.visualPrefab == null || visualRoot == null || visualRoot.childCount > 0)
        {
            return;
        }

        Instantiate(definition.visualPrefab, visualRoot);
    }

    private bool ShouldRunBrain()
    {
        return behaviorController != null && !health.IsDead && (!IsNetworkSessionActive() || IsServer);
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }
}
