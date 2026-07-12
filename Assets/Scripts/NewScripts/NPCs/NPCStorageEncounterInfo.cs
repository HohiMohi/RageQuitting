using System;
using UnityEngine;

[Serializable]
public struct NPCStorageEncounterInfo
{
    [SerializeField] private ulong storageId;
    [SerializeField] private Vector3 firstEncounterPosition;
    [SerializeField] private BaseStorageNew storage;

    public ulong StorageId => storageId;
    public Vector3 FirstEncounterPosition => firstEncounterPosition;
    public BaseStorageNew Storage => storage;

    public NPCStorageEncounterInfo(ulong storageId, Vector3 firstEncounterPosition, BaseStorageNew storage)
    {
        this.storageId = storageId;
        this.firstEncounterPosition = firstEncounterPosition;
        this.storage = storage;
    }
}
