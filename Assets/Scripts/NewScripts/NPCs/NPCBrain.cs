using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NPCCarrier))]
[RequireComponent(typeof(NPCHealth))]
[RequireComponent(typeof(NPCFactionMember))]
[RequireComponent(typeof(NPCAttackController))]
[RequireComponent(typeof(NPCStorageInteractor))]
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
    private NPCAttackController attackController;
    private NPCStorageInteractor storageInteractor;
    private NPCBehaviorController behaviorController;
    private NPCSpawner originSpawner;
    private Vector3 spawnPosition;
    private float tickTimer;
    private bool isUnderExternalControl;
    private NPCHealth.DamageEventArgs latestDamageEvent;
    private NPCHealth.DamageEventArgs deferredDamageEvent;
    private float latestDamageEventTime = float.NegativeInfinity;

    public NPCDefinitionSO Definition => definition;
    public NPCFactionRelationshipMatrixSO RelationshipMatrix => relationshipMatrix;
    public NavMeshAgent Agent => agent;
    public NPCCarrier Carrier => carrier;
    public NPCHealth Health => health;
    public NPCFactionMember FactionMember => factionMember;
    public NPCAttackController AttackController => attackController;
    public NPCStorageInteractor StorageInteractor => storageInteractor;
    public NPCBehaviorController BehaviorController => behaviorController;
    public NPCSpawner OriginSpawner => originSpawner;
    public Vector3 SpawnPosition => spawnPosition;
    public Transform VisualRoot => visualRoot;
    public float DetectionRadius => definition != null ? definition.detectionRadius : 12f;
    public float InteractionDistance => definition != null ? definition.interactionDistance : 1.4f;
    public float PatrolRadius => definition != null ? definition.patrolRadius : 8f;
    public bool IsUnderExternalControl => isUnderExternalControl;

    private void Awake()
    {
        spawnPosition = transform.position;
        agent = GetComponent<NavMeshAgent>();
        carrier = GetComponent<NPCCarrier>();
        health = GetComponent<NPCHealth>();
        health.OnDamaged += Health_OnDamaged;
        factionMember = GetComponent<NPCFactionMember>();
        attackController = GetComponent<NPCAttackController>();
        storageInteractor = GetComponent<NPCStorageInteractor>();
        ApplyDefinition();
    }

    private void OnEnable()
    {
        NPCRegistry.Register(this);
    }

    private void OnDisable()
    {
        NPCRegistry.Unregister(this);
        behaviorController?.Exit();
        behaviorController = null;
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.OnDamaged -= Health_OnDamaged;
        }
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

    public void SetOriginSpawner(NPCSpawner spawner)
    {
        originSpawner = spawner;
    }

    public void SetBehavior(NPCBehaviorSO behavior)
    {
        behaviorOverride = behavior;
        RebuildBehavior();
    }

    public void BeginExternalControl(NetworkObject source = null)
    {
        if (isUnderExternalControl)
        {
            return;
        }

        isUnderExternalControl = true;
        if (source != null &&
            latestDamageEvent != null &&
            latestDamageEvent.Attacker == source &&
            Time.time - latestDamageEventTime <= 0.25f)
        {
            deferredDamageEvent = latestDamageEvent;
        }

        behaviorController?.Exit();
        tickTimer = 0f;
    }

    public void EndExternalControl()
    {
        if (!isUnderExternalControl)
        {
            return;
        }

        isUnderExternalControl = false;
        tickTimer = 0f;
        if (health != null && !health.IsDead)
        {
            behaviorController?.Enter();
            if (deferredDamageEvent != null)
            {
                NPCHealth.DamageEventArgs damageEvent = deferredDamageEvent;
                deferredDamageEvent = null;
                behaviorController?.HandleDeferredDamage(damageEvent);
            }
        }
    }

    private void Health_OnDamaged(object sender, NPCHealth.DamageEventArgs damageEvent)
    {
        if (damageEvent == null || damageEvent.CurrentHealth >= damageEvent.PreviousHealth)
        {
            return;
        }

        latestDamageEvent = damageEvent;
        latestDamageEventTime = Time.time;
        if (isUnderExternalControl)
        {
            deferredDamageEvent = damageEvent;
        }
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
        ConfigureNavMeshArea(
            definition.waterNavMeshAreaName,
            definition.waterAreaCost,
            definition.waterTraversalMode == NPCWaterTraversalMode.SurfaceSwimmer);
        ConfigureNavMeshArea(
            definition.waterEntryNavMeshAreaName,
            definition.waterEntryAreaCost,
            definition.waterTraversalMode == NPCWaterTraversalMode.SurfaceSwimmer);

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

    private void ConfigureNavMeshArea(string areaName, float areaCost, bool enabled)
    {
        int areaIndex = NavMesh.GetAreaFromName(areaName);
        if (areaIndex < 0)
        {
            return;
        }

        int areaBit = 1 << areaIndex;
        agent.areaMask = enabled ? agent.areaMask | areaBit : agent.areaMask & ~areaBit;
        agent.SetAreaCost(areaIndex, Mathf.Max(1f, areaCost));
    }

    private void RebuildBehavior()
    {
        deferredDamageEvent = null;
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
        return behaviorController != null
            && !isUnderExternalControl
            && !health.IsDead
            && (!IsNetworkSessionActive() || IsServer);
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }
}
