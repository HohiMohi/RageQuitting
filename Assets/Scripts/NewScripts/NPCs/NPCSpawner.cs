using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NPCSpawner : MonoBehaviour
{
    [Header("Spawner Configuration")]
    [SerializeField] private GameObject npcBasePrefab;
    [SerializeField] private List<NPCDefinitionSO> npcDefinitions = new List<NPCDefinitionSO>();
    [SerializeField] private List<NPCSpawnGroupSO> spawnGroups = new List<NPCSpawnGroupSO>();
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
    [SerializeField] private float initialSpawnDelay = 10f;
    [SerializeField] private float spawnIntervalMin = 15f;
    [SerializeField] private float spawnIntervalMax = 30f;
    [SerializeField] private int maxNPCCount = 5;

    private readonly List<SpawnedNpcRecord> activeNPCs = new List<SpawnedNpcRecord>();
    private readonly HashSet<NPCSpawnGroupSO> unlockedGroups = new HashSet<NPCSpawnGroupSO>();
    private readonly HashSet<NPCSpawnSignalSO> receivedSignals = new HashSet<NPCSpawnSignalSO>();
    private readonly Dictionary<NPCSpawnGroupSO, int> totalSpawnedNPCsByGroup =
        new Dictionary<NPCSpawnGroupSO, int>();
    [SerializeField, Tooltip("Runtime-only number of NPCs successfully spawned by this spawner.")]
    private int totalSpawnedNPCCount;
    private float spawnTimer;

    public int TotalSpawnedNPCCount => totalSpawnedNPCCount;

    public int ActiveNPCCount
    {
        get
        {
            CleanupDestroyedNPCs();
            return activeNPCs.Count;
        }
    }

    private void Start()
    {
        totalSpawnedNPCCount = 0;
        totalSpawnedNPCsByGroup.Clear();
        spawnTimer = Mathf.Max(0f, initialSpawnDelay);
        EvaluateGroupUnlocks();
    }

    private void Update()
    {
        if (!HasSpawnAuthority())
        {
            return;
        }

        CleanupDestroyedNPCs();
        EvaluateGroupUnlocks();

        if (GameTimerManager.Instance != null && GameTimerManager.Instance.IsGameOver())
        {
            return;
        }

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f)
        {
            return;
        }

        if (activeNPCs.Count < Mathf.Max(0, maxNPCCount))
        {
            TrySpawnNPC();
        }

        float minimumInterval = Mathf.Max(0.01f, Mathf.Min(spawnIntervalMin, spawnIntervalMax));
        float maximumInterval = Mathf.Max(minimumInterval, Mathf.Max(spawnIntervalMin, spawnIntervalMax));
        spawnTimer = UnityEngine.Random.Range(minimumInterval, maximumInterval);
    }

    public void NotifySpawnSignal(NPCSpawnSignalSO signal)
    {
        if (signal == null || !HasSpawnAuthority())
        {
            return;
        }

        receivedSignals.Add(signal);
        EvaluateGroupUnlocks();
    }

    public bool HasReceivedSignal(NPCSpawnSignalSO signal)
    {
        return signal != null && receivedSignals.Contains(signal);
    }

    public bool IsGroupUnlocked(NPCSpawnGroupSO group)
    {
        return group != null && unlockedGroups.Contains(group);
    }

    public int GetActiveCountForGroup(NPCSpawnGroupSO group)
    {
        CleanupDestroyedNPCs();
        int count = 0;
        for (int i = 0; i < activeNPCs.Count; i++)
        {
            if (activeNPCs[i].Group == group)
            {
                count++;
            }
        }

        return count;
    }

    public int GetTotalSpawnedCountForGroup(NPCSpawnGroupSO group)
    {
        return group != null && totalSpawnedNPCsByGroup.TryGetValue(group, out int count)
            ? count
            : 0;
    }

    [ContextMenu("Validate Spawn Configuration")]
    public void ValidateSpawnConfiguration()
    {
        if (npcBasePrefab == null)
        {
            Debug.LogWarning($"{name}: NPCSpawner has no base NPC prefab.", this);
        }

        if ((spawnGroups == null || spawnGroups.Count == 0)
            && (npcDefinitions == null || npcDefinitions.Count == 0))
        {
            Debug.LogWarning($"{name}: NPCSpawner has neither spawn groups nor legacy NPC definitions.", this);
        }

        if (maxNPCCount <= 0)
        {
            Debug.LogWarning($"{name}: NPCSpawner global max NPC count must be greater than zero.", this);
        }

        if (spawnGroups == null)
        {
            return;
        }

        foreach (NPCSpawnGroupSO group in spawnGroups)
        {
            if (group == null)
            {
                Debug.LogWarning($"{name}: NPCSpawner contains an empty spawn group reference.", this);
                continue;
            }

            group.ValidateConfiguration(this);
        }
    }

    private bool TrySpawnNPC()
    {
        if (!TrySelectSpawn(out NPCDefinitionSO definition, out NPCSpawnGroupSO sourceGroup))
        {
            return false;
        }

        GameObject prefabToSpawn = definition.npcPrefabOverride != null ? definition.npcPrefabOverride : npcBasePrefab;
        if (prefabToSpawn == null)
        {
            Debug.LogWarning($"{name}: NPC definition '{definition.name}' has no prefab override and the spawner has no base prefab.", this);
            return false;
        }

        Transform spawnPoint = spawnPoints != null && spawnPoints.Count > 0
            ? spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Count)]
            : transform;
        GameObject npcInstance = Instantiate(prefabToSpawn, spawnPoint.position, spawnPoint.rotation);

        if (npcInstance.TryGetComponent(out NPCBrain brain))
        {
            brain.SetOriginSpawner(this);
            brain.SetDefinition(definition);
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (npcInstance.TryGetComponent(out NetworkObject networkObject))
            {
                networkObject.Spawn();
            }
            else
            {
                Debug.LogError($"NPC prefab '{prefabToSpawn.name}' is missing NetworkObject.", npcInstance);
                Destroy(npcInstance);
                return false;
            }
        }

        activeNPCs.Add(new SpawnedNpcRecord(npcInstance, definition, sourceGroup));
        RecordSuccessfulSpawn(sourceGroup);
        Debug.Log($"Spawned NPC '{definition.name}' from group '{GetGroupDisplayName(sourceGroup)}' at spawn point: {spawnPoint.name}");
        return true;
    }

    private bool TrySelectSpawn(out NPCDefinitionSO definition, out NPCSpawnGroupSO sourceGroup)
    {
        definition = null;
        sourceGroup = null;

        if (spawnGroups == null || spawnGroups.Count == 0)
        {
            return TrySelectLegacyDefinition(out definition);
        }

        List<NPCSpawnGroupSO> eligibleGroups = new List<NPCSpawnGroupSO>();
        float totalGroupWeight = 0f;
        foreach (NPCSpawnGroupSO group in spawnGroups)
        {
            if (!IsGroupEligible(group))
            {
                continue;
            }

            eligibleGroups.Add(group);
            totalGroupWeight += group.SpawnWeight;
        }

        sourceGroup = SelectWeightedGroup(eligibleGroups, totalGroupWeight);
        return sourceGroup != null && sourceGroup.TrySelectDefinition(npcBasePrefab, out definition);
    }

    private bool TrySelectLegacyDefinition(out NPCDefinitionSO definition)
    {
        definition = null;
        if (npcDefinitions == null || npcDefinitions.Count == 0)
        {
            return false;
        }

        List<NPCDefinitionSO> validDefinitions = new List<NPCDefinitionSO>();
        foreach (NPCDefinitionSO candidate in npcDefinitions)
        {
            if (candidate != null && (candidate.npcPrefabOverride != null || npcBasePrefab != null))
            {
                validDefinitions.Add(candidate);
            }
        }

        if (validDefinitions.Count == 0)
        {
            return false;
        }

        definition = validDefinitions[UnityEngine.Random.Range(0, validDefinitions.Count)];
        return true;
    }

    private void EvaluateGroupUnlocks()
    {
        if (!HasSpawnAuthority() || spawnGroups == null || spawnGroups.Count == 0)
        {
            return;
        }

        NPCSpawnConditionContext context = new NPCSpawnConditionContext(
            this,
            GameTimerManager.Instance,
            GameplayManager.Instance);

        foreach (NPCSpawnGroupSO group in spawnGroups)
        {
            if (group != null && !unlockedGroups.Contains(group) && group.AreUnlockConditionsSatisfied(context))
            {
                unlockedGroups.Add(group);
            }
        }
    }

    private bool IsGroupEligible(NPCSpawnGroupSO group)
    {
        return group != null
            && unlockedGroups.Contains(group)
            && group.SpawnWeight > 0f
            && GetActiveCountForGroup(group) < group.MaxActiveNPCs
            && group.HasValidSpawnEntry(npcBasePrefab);
    }

    private static NPCSpawnGroupSO SelectWeightedGroup(List<NPCSpawnGroupSO> groups, float totalWeight)
    {
        if (groups == null || groups.Count == 0 || totalWeight <= 0f)
        {
            return null;
        }

        float roll = UnityEngine.Random.value * totalWeight;
        foreach (NPCSpawnGroupSO group in groups)
        {
            roll -= group.SpawnWeight;
            if (roll <= 0f)
            {
                return group;
            }
        }

        return groups[groups.Count - 1];
    }

    private void CleanupDestroyedNPCs()
    {
        activeNPCs.RemoveAll(record => record.Instance == null);
    }

    private void RecordSuccessfulSpawn(NPCSpawnGroupSO sourceGroup)
    {
        totalSpawnedNPCCount++;
        if (sourceGroup != null)
        {
            totalSpawnedNPCsByGroup.TryGetValue(sourceGroup, out int groupCount);
            totalSpawnedNPCsByGroup[sourceGroup] = groupCount + 1;
        }

        EvaluateGroupUnlocks();
    }

    private bool HasSpawnAuthority()
    {
        return NetworkManager.Singleton == null
            || !NetworkManager.Singleton.IsListening
            || NetworkManager.Singleton.IsServer;
    }

    private static string GetGroupDisplayName(NPCSpawnGroupSO group)
    {
        return group != null ? group.DisplayName : "Legacy";
    }

    private sealed class SpawnedNpcRecord
    {
        public readonly GameObject Instance;
        public readonly NPCDefinitionSO Definition;
        public readonly NPCSpawnGroupSO Group;

        public SpawnedNpcRecord(GameObject instance, NPCDefinitionSO definition, NPCSpawnGroupSO group)
        {
            Instance = instance;
            Definition = definition;
            Group = group;
        }
    }
}
