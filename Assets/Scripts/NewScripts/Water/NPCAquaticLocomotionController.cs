using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NPCBrain))]
public class NPCAquaticLocomotionController : NetworkBehaviour
{
    private readonly NetworkVariable<bool> swimmingNetwork = new NetworkVariable<bool>();

    [SerializeField, Min(0.05f)] private float waterStateCheckInterval = 0.15f;
    [SerializeField, Min(0.05f)] private float shoreSpeedTransitionDuration = 0.75f;
    [SerializeField, Min(0.001f)] private float externalSpeedChangeTolerance = 0.02f;
    [SerializeField, Min(0.05f)] private float aquaticEgressArrivalDistance = 0.35f;

    private NPCBrain brain;
    private NavMeshAgent agent;
    private NPCCarrier carrier;
    private Transform visualRoot;
    private Vector3 visualRootBaseLocalPosition;
    private float nextWaterStateCheckTime;
    private bool swimmingLocal;
    private bool aquaticEgress;
    private Vector3 aquaticEgressDestination;
    private int waterAreaIndex = -1;
    private int waterEntryAreaIndex = -1;
    private int baseAreaMask;
    private bool waterAccessEnabled;
    private float locomotionBaseSpeed;
    private float currentWaterSpeedMultiplier = 1f;
    private float targetWaterSpeedMultiplier = 1f;
    private float lastAppliedAgentSpeed = -1f;

    public bool IsSwimming => IsNetworkStateActive ? swimmingNetwork.Value : swimmingLocal;
    public bool IsAquaticEgressActive => aquaticEgress;
    public bool HasReachedAquaticEgressDestination => aquaticEgress
        && agent != null
        && agent.enabled
        && agent.isOnNavMesh
        && !agent.pathPending
        && Vector3.ProjectOnPlane(transform.position - aquaticEgressDestination, Vector3.up).sqrMagnitude
            <= aquaticEgressArrivalDistance * aquaticEgressArrivalDistance
        && IsStandingOnWalkableArea();

    private bool IsNetworkStateActive => NetworkManager.Singleton != null
        && NetworkManager.Singleton.IsListening
        && IsSpawned;

    private void Awake()
    {
        brain = GetComponent<NPCBrain>();
        agent = GetComponent<NavMeshAgent>();
        carrier = GetComponent<NPCCarrier>();
        visualRoot = brain != null ? brain.VisualRoot : null;
        if (visualRoot != null)
        {
            visualRootBaseLocalPosition = visualRoot.localPosition;
        }

        baseAreaMask = agent != null ? agent.areaMask : NavMesh.AllAreas;
        locomotionBaseSpeed = agent != null ? agent.speed : 0f;
        if (agent != null)
        {
            agent.autoTraverseOffMeshLink = true;
        }
    }

    private void Update()
    {
        if (IsNetworkStateActive)
        {
            if (IsServer && Time.time >= nextWaterStateCheckTime)
            {
                nextWaterStateCheckTime = Time.time + waterStateCheckInterval;
                EvaluateWaterState(true);
            }
        }
        else if (Time.time >= nextWaterStateCheckTime)
        {
            nextWaterStateCheckTime = Time.time + waterStateCheckInterval;
            EvaluateWaterState(false);
        }

        UpdateMovementSpeed();
        UpdateVisualBobbing();
    }

    public bool BeginAquaticEgress()
    {
        if (!IsSwimming
            || !WaterBody.TryGetContaining(transform.position, out WaterBody body)
            || !body.TryGetClosestExitPosition(agent, out aquaticEgressDestination))
        {
            return false;
        }

        aquaticEgress = true;
        RefreshWaterAreaAccess();
        return true;
    }

    public bool TryGetAquaticEgressDestination(out Vector3 destination)
    {
        destination = aquaticEgressDestination;
        return aquaticEgress;
    }

    public void EndAquaticEgress()
    {
        aquaticEgress = false;
        RefreshWaterAreaAccess();
    }

    private void EvaluateWaterState(bool networkAuthority)
    {
        NPCDefinitionSO definition = brain != null ? brain.Definition : null;
        bool supportsSurface = definition != null
            && definition.waterTraversalMode == NPCWaterTraversalMode.SurfaceSwimmer;
        bool swimming = supportsSurface
            && WaterBody.TryGetContaining(transform.position + Vector3.up * 0.1f, out _);

        if (networkAuthority)
        {
            swimmingNetwork.Value = swimming;
        }
        else
        {
            swimmingLocal = swimming;
        }

        RefreshWaterAreaAccess();
        targetWaterSpeedMultiplier = swimming
            ? Mathf.Clamp(definition.surfaceSwimSpeedMultiplier, 0.1f, 1f)
            : 1f;
    }

    private void UpdateMovementSpeed()
    {
        if (agent == null || !agent.enabled || (IsNetworkStateActive && !IsServer))
        {
            return;
        }

        NPCDefinitionSO definition = brain != null ? brain.Definition : null;
        if (definition == null || definition.waterTraversalMode != NPCWaterTraversalMode.SurfaceSwimmer)
        {
            return;
        }

        if (locomotionBaseSpeed <= 0f)
        {
            locomotionBaseSpeed = definition.moveSpeed;
        }

        if (lastAppliedAgentSpeed >= 0f
            && Mathf.Abs(agent.speed - lastAppliedAgentSpeed) > externalSpeedChangeTolerance)
        {
            locomotionBaseSpeed = agent.speed;
        }

        float waterMultiplier = Mathf.Clamp(definition.surfaceSwimSpeedMultiplier, 0.1f, 1f);
        float multiplierChangePerSecond = Mathf.Max(0.01f, 1f - waterMultiplier)
            / Mathf.Max(0.05f, shoreSpeedTransitionDuration);
        currentWaterSpeedMultiplier = Mathf.MoveTowards(
            currentWaterSpeedMultiplier,
            targetWaterSpeedMultiplier,
            multiplierChangePerSecond * Time.deltaTime);

        lastAppliedAgentSpeed = Mathf.Max(0f, locomotionBaseSpeed * currentWaterSpeedMultiplier);
        agent.speed = lastAppliedAgentSpeed;
    }

    private void RefreshWaterAreaAccess()
    {
        NPCDefinitionSO definition = brain != null ? brain.Definition : null;
        if (agent == null || definition == null)
        {
            return;
        }

        if (waterAreaIndex < 0 || waterEntryAreaIndex < 0)
        {
            waterAreaIndex = NavMesh.GetAreaFromName(definition.waterNavMeshAreaName);
            waterEntryAreaIndex = NavMesh.GetAreaFromName(definition.waterEntryNavMeshAreaName);
        }

        if (waterAreaIndex < 0 || waterEntryAreaIndex < 0)
        {
            return;
        }

        int waterBits = (1 << waterAreaIndex) | (1 << waterEntryAreaIndex);
        baseAreaMask = agent.areaMask & ~waterBits;

        bool allow = definition.waterTraversalMode == NPCWaterTraversalMode.SurfaceSwimmer
            && (aquaticEgress || CanCarryThroughWater());
        if (allow == waterAccessEnabled)
        {
            return;
        }

        waterAccessEnabled = allow;
        agent.areaMask = allow ? baseAreaMask | waterBits : baseAreaMask & ~waterBits;
        agent.SetAreaCost(waterAreaIndex, Mathf.Max(1f, definition.waterAreaCost));
        agent.SetAreaCost(waterEntryAreaIndex, Mathf.Max(1f, definition.waterEntryAreaCost));

        if (agent.isOnNavMesh && agent.hasPath && agent.isPathStale)
        {
            agent.ResetPath();
        }
    }

    private bool IsStandingOnWalkableArea()
    {
        int walkableAreaIndex = NavMesh.GetAreaFromName("Walkable");
        if (walkableAreaIndex < 0
            || !NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit hit,
                aquaticEgressArrivalDistance + 0.5f,
                1 << walkableAreaIndex))
        {
            return false;
        }

        return Vector3.ProjectOnPlane(transform.position - hit.position, Vector3.up).sqrMagnitude
            <= (aquaticEgressArrivalDistance + 0.5f) * (aquaticEgressArrivalDistance + 0.5f);
    }

    private bool CanCarryThroughWater()
    {
        if (carrier == null || carrier.CarriedObject == null)
        {
            return true;
        }

        return !carrier.IsSharedCarryActive
            && carrier.CarriedObject.TryGetComponent(out BaseResourceNew _)
            && !carrier.CarriedObject.TryGetComponent(out MountableBridgeComponent _)
            && !carrier.CarriedObject.TryGetComponent(out DownedPlayerCarryable _);
    }

    private void UpdateVisualBobbing()
    {
        if (visualRoot == null)
        {
            return;
        }

        NPCDefinitionSO definition = brain != null ? brain.Definition : null;
        if (!IsSwimming
            || definition == null
            || definition.SurfaceSwimVisualBobbingAmplitude <= 0f)
        {
            visualRoot.localPosition = Vector3.Lerp(
                visualRoot.localPosition,
                visualRootBaseLocalPosition,
                Time.deltaTime * 8f);
            return;
        }

        float animationTime = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
            ? (float)NetworkManager.Singleton.ServerTime.Time
            : Time.time;
        float bob = Mathf.Sin(animationTime * definition.SurfaceSwimVisualBobbingFrequency * Mathf.PI * 2f)
            * definition.SurfaceSwimVisualBobbingAmplitude;
        Vector3 target = visualRootBaseLocalPosition + Vector3.up * bob;
        visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, target, Time.deltaTime * 10f);
    }
}
