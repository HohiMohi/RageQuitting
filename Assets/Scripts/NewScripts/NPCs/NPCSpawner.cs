using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NPCSpawner : MonoBehaviour
{
    [Header("Spawner Configuration")]
    [SerializeField] private GameObject npcBasePrefab;
    [SerializeField] private List<NPCDefinitionSO> npcDefinitions = new List<NPCDefinitionSO>();
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
    [SerializeField] private float initialSpawnDelay = 10f;
    [SerializeField] private float spawnIntervalMin = 15f;
    [SerializeField] private float spawnIntervalMax = 30f;
    [SerializeField] private int maxNPCCount = 5;

    private List<GameObject> activeNPCs = new List<GameObject>();
    private float spawnTimer;

    private void Start()
    {
        spawnTimer = initialSpawnDelay;
    }

    private void Update()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // Clean up destroyed NPCs from the list
        activeNPCs.RemoveAll(npc => npc == null);

        if (GameTimerManager.Instance != null && GameTimerManager.Instance.IsGameOver())
        {
            return; // Stop spawning if game is over
        }

        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            if (activeNPCs.Count < maxNPCCount)
            {
                SpawnNPC();
            }
            spawnTimer = Random.Range(spawnIntervalMin, spawnIntervalMax);
        }
    }

    private void SpawnNPC()
    {
        if (npcBasePrefab == null || npcDefinitions == null || npcDefinitions.Count == 0)
        {
            Debug.LogWarning("NPCSpawner is missing NPC prefab or definitions.");
            return;
        }

        Transform spawnPoint = spawnPoints != null && spawnPoints.Count > 0
            ? spawnPoints[Random.Range(0, spawnPoints.Count)]
            : transform;
        NPCDefinitionSO definition = npcDefinitions[Random.Range(0, npcDefinitions.Count)];
        GameObject npcInstance = Instantiate(npcBasePrefab, spawnPoint.position, spawnPoint.rotation);

        if (npcInstance.TryGetComponent(out NPCBrain brain))
        {
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
                Debug.LogError($"NPC prefab '{npcBasePrefab.name}' is missing NetworkObject.");
                Destroy(npcInstance);
                return;
            }
        }

        activeNPCs.Add(npcInstance);
        Debug.Log($"Spawned NPC '{definition.name}' at spawn point: {spawnPoint.name}");
    }
}
