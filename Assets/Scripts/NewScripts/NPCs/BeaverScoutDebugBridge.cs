using System;
using System.Collections.Generic;
using UnityEngine;

public class BeaverScoutDebugBridge : MonoBehaviour
{
    [Serializable]
    public struct EncounteredStorageDebugEntry
    {
        public ulong storageId;
        public Vector3 firstEncounterPosition;
    }

    [SerializeField] private List<EncounteredStorageDebugEntry> encounteredStorages = new List<EncounteredStorageDebugEntry>();

    public IReadOnlyList<EncounteredStorageDebugEntry> EncounteredStorages => encounteredStorages;

    public void AddEncounteredStorage(ulong storageId, Vector3 firstEncounterPosition)
    {
        for (int i = 0; i < encounteredStorages.Count; i++)
        {
            if (encounteredStorages[i].storageId == storageId)
            {
                return;
            }
        }

        encounteredStorages.Add(new EncounteredStorageDebugEntry
        {
            storageId = storageId,
            firstEncounterPosition = firstEncounterPosition
        });
    }
}
