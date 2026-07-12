using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BeaverSpawnerStorageMemory : MonoBehaviour
{
    [SerializeField] private List<NPCStorageEncounterInfo> debugKnownStorages = new List<NPCStorageEncounterInfo>();

    private readonly Dictionary<ulong, NPCStorageEncounterInfo> knownStorages = new Dictionary<ulong, NPCStorageEncounterInfo>();
    private readonly List<ulong> knownStorageOrder = new List<ulong>();
    private readonly List<NPCStorageEncounterInfo> snapshotBuffer = new List<NPCStorageEncounterInfo>();

    public bool RegisterStorage(NPCStorageEncounterInfo info)
    {
        if (info.StorageId == 0 || knownStorages.ContainsKey(info.StorageId))
        {
            return false;
        }

        knownStorages.Add(info.StorageId, info);
        knownStorageOrder.Add(info.StorageId);
        RefreshDebugList();
        return true;
    }

    public int RegisterStorages(IEnumerable<NPCStorageEncounterInfo> infos)
    {
        if (infos == null)
        {
            return 0;
        }

        int addedCount = 0;
        foreach (NPCStorageEncounterInfo info in infos)
        {
            if (RegisterStorage(info))
            {
                addedCount++;
            }
        }

        return addedCount;
    }

    public IReadOnlyList<NPCStorageEncounterInfo> GetKnownStoragesSnapshot()
    {
        snapshotBuffer.Clear();
        foreach (ulong storageId in knownStorageOrder)
        {
            if (knownStorages.TryGetValue(storageId, out NPCStorageEncounterInfo info))
            {
                snapshotBuffer.Add(info);
            }
        }

        return snapshotBuffer;
    }

    private void Update()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        RefreshDebugList();
    }

    private void RefreshDebugList()
    {
        debugKnownStorages.Clear();
        foreach (ulong storageId in knownStorageOrder)
        {
            if (knownStorages.TryGetValue(storageId, out NPCStorageEncounterInfo info))
            {
                debugKnownStorages.Add(info);
            }
        }
    }
}
