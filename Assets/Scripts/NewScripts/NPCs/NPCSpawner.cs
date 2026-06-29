using System.Collections.Generic;
using UnityEngine;

public class NPCSpawner : MonoBehaviour
{
    [Header("Spawner Configuration")]
    [SerializeField] private GameObject disturberNPCPrefab;
    [SerializeField] private List<Transform> burrowSpawners; // Burrows where NPCs spawn/return
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
        if (disturberNPCPrefab == null || burrowSpawners == null || burrowSpawners.Count == 0)
        {
            Debug.LogWarning("NPCSpawner is missing prefab or burrow spawners references!");
            return;
        }

        // Pick a random burrow
        Transform chosenBurrow = burrowSpawners[Random.Range(0, burrowSpawners.Count)];
        GameObject npcInstance = Instantiate(disturberNPCPrefab, chosenBurrow.position, chosenBurrow.rotation);
        
        DisturberNPC disturberScript = npcInstance.GetComponent<DisturberNPC>();
        if (disturberScript != null)
        {
            disturberScript.InitializeBurrow(chosenBurrow.position);
        }

        activeNPCs.Add(npcInstance);
        Debug.Log($"Spawned DisturberNPC at burrow: {chosenBurrow.name}");
    }
}
