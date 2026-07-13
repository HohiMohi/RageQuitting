using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public struct BaseResourceStorageNetworkState : INetworkSerializable, IEquatable<BaseResourceStorageNetworkState>
{
    public int resourceIndex;
    public int amount;

    public BaseResourceStorageNetworkState(int resourceIndex, int amount)
    {
        this.resourceIndex = resourceIndex;
        this.amount = amount;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref resourceIndex);
        serializer.SerializeValue(ref amount);
    }

    public bool Equals(BaseResourceStorageNetworkState other)
    {
        return resourceIndex == other.resourceIndex && amount == other.amount;
    }
}

public class BaseStorageNew : NetworkBehaviour, IInteractableNew
{
    [SerializeField] protected List<BaseResourceSO> storableBaseResourcesSOList = new List<BaseResourceSO>();
    [SerializeField] private Transform withdrawSpawnPoint;
    [SerializeField] private Vector3 withdrawSpawnFallbackOffset = new Vector3(0f, 0.25f, 1f);

    protected Dictionary<BaseResourceSO, int> storedBaseResourceDictionary;
    private readonly NetworkList<BaseResourceStorageNetworkState> storedBaseResourceNetworkList = new NetworkList<BaseResourceStorageNetworkState>();

    public IReadOnlyList<BaseResourceSO> StorableBaseResources => storableBaseResourcesSOList;

    public event EventHandler<BaseResourceAmountChangedEventArgs> BaseResourceAmountChanged;

    public class BaseResourceAmountChangedEventArgs : EventArgs
    {
        public BaseResourceSO baseResourceSO;
        public int currentAmount;
    }

    public void Interact(Transform interactor)
    {
        Debug.Log("Interacted with Base Storage");
    }

    protected virtual void Awake()
    {
        InitializeLocalStorageState();
    }

    public override void OnNetworkSpawn()
    {
        storedBaseResourceNetworkList.OnListChanged += StoredBaseResourceNetworkList_OnListChanged;

        if (IsServer)
        {
            InitializeNetworkStorageState();
        }

        RebuildLocalCacheFromNetworkState();
    }

    public override void OnNetworkDespawn()
    {
        storedBaseResourceNetworkList.OnListChanged -= StoredBaseResourceNetworkList_OnListChanged;
    }

    public bool IsStorable(BaseResourceSO baseResourceSO)
    {
        return baseResourceSO != null && storedBaseResourceDictionary != null && storedBaseResourceDictionary.ContainsKey(baseResourceSO);
    }

    public virtual void StoreBaseResource(BaseResourceSO baseResourceSO, int amount)
    {
        if (baseResourceSO == null || amount <= 0 || !IsStorable(baseResourceSO))
        {
            Debug.Log($"Cannot store {(baseResourceSO != null ? baseResourceSO.resourceName : "null")} in this storage.");
            return;
        }

        if (IsNetworkSessionActive())
        {
            int resourceIndex = GetResourceIndex(baseResourceSO);
            if (resourceIndex < 0)
            {
                return;
            }

            if (IsServer)
            {
                AddBaseResourceAmountByIndex(resourceIndex, amount);
            }
            else
            {
                RequestStoreBaseResourceServerRpc(resourceIndex, amount);
            }

            return;
        }

        AddBaseResourceAmount(baseResourceSO, amount);
    }

    public int CheckBaseResourceAmount(BaseResourceSO baseResourceSO)
    {
        if (!IsStorable(baseResourceSO))
        {
            return -1;
        }

        return storedBaseResourceDictionary[baseResourceSO];
    }

    public void RemoveBaseResourceAmount(BaseResourceSO baseResourceSO, int amount)
    {
        TryRemoveBaseResourceAmount(baseResourceSO, amount);
    }

    public bool TryRemoveBaseResourceAmount(BaseResourceSO baseResourceSO, int amount)
    {
        if (!CanWithdrawBaseResource(baseResourceSO, amount))
        {
            return false;
        }

        if (IsNetworkSessionActive())
        {
            int resourceIndex = GetResourceIndex(baseResourceSO);
            if (resourceIndex < 0)
            {
                return false;
            }

            if (IsServer)
            {
                return TryRemoveBaseResourceAmountByIndex(resourceIndex, amount);
            }

            RequestRemoveBaseResourceServerRpc(resourceIndex, amount);
            return true;
        }

        return TryRemoveBaseResourceAmountLocal(baseResourceSO, amount);
    }

    public bool CanWithdrawBaseResource(BaseResourceSO baseResourceSO, int amount = 1)
    {
        return amount > 0 && IsStorable(baseResourceSO) && CheckBaseResourceAmount(baseResourceSO) >= amount;
    }

    public bool TryWithdrawBaseResource(BaseResourceSO baseResourceSO, ICarryActor carryActor, out GameObject spawnedResource, int amount = 1)
    {
        spawnedResource = null;
        if (!CanWithdrawBaseResource(baseResourceSO, amount) || baseResourceSO.resourcePrefab == null)
        {
            return false;
        }

        if (IsNetworkSessionActive() && !IsServer)
        {
            return false;
        }

        if (!TryRemoveBaseResourceAmount(baseResourceSO, amount))
        {
            return false;
        }

        if (!BaseResourceSpawnUtility.TrySpawnResource(baseResourceSO, GetWithdrawSpawnPosition(), GetWithdrawSpawnRotation(), out spawnedResource))
        {
            StoreBaseResource(baseResourceSO, amount);
            return false;
        }

        if (carryActor != null && spawnedResource.TryGetComponent(out BaseResourceNew baseResource))
        {
            baseResource.TryPickupByCarrier(carryActor);
        }

        return true;
    }

    public void AddStorableBaseResource(BaseResourceSO baseResourceSO)
    {
        if (baseResourceSO == null)
        {
            return;
        }

        EnsureStorageInitialized();
        if (!storableBaseResourcesSOList.Contains(baseResourceSO))
        {
            storableBaseResourcesSOList.Add(baseResourceSO);
        }

        if (!storedBaseResourceDictionary.ContainsKey(baseResourceSO))
        {
            storedBaseResourceDictionary.Add(baseResourceSO, 0);
        }

        if (IsNetworkSessionActive() && IsServer)
        {
            EnsureNetworkStateForResource(baseResourceSO);
        }
    }

    public Vector3 GetWithdrawSpawnPosition()
    {
        if (withdrawSpawnPoint != null)
        {
            return withdrawSpawnPoint.position;
        }

        return transform.position + transform.TransformDirection(withdrawSpawnFallbackOffset);
    }

    public Quaternion GetWithdrawSpawnRotation()
    {
        return withdrawSpawnPoint != null ? withdrawSpawnPoint.rotation : transform.rotation;
    }

    protected bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }

    protected virtual void OnBaseResourceAmountChanged(BaseResourceSO baseResourceSO, int currentAmount)
    {
        BaseResourceAmountChanged?.Invoke(this, new BaseResourceAmountChangedEventArgs
        {
            baseResourceSO = baseResourceSO,
            currentAmount = currentAmount
        });
    }

    private void InitializeLocalStorageState()
    {
        if (storableBaseResourcesSOList == null)
        {
            storableBaseResourcesSOList = new List<BaseResourceSO>();
        }

        storedBaseResourceDictionary = new Dictionary<BaseResourceSO, int>();
        foreach (BaseResourceSO baseResourceSO in storableBaseResourcesSOList)
        {
            if (baseResourceSO != null && !storedBaseResourceDictionary.ContainsKey(baseResourceSO))
            {
                storedBaseResourceDictionary.Add(baseResourceSO, 0);
            }
        }
    }

    private void InitializeNetworkStorageState()
    {
        for (int i = 0; i < storableBaseResourcesSOList.Count; i++)
        {
            BaseResourceSO baseResourceSO = storableBaseResourcesSOList[i];
            if (baseResourceSO == null || FindNetworkStateIndex(i) >= 0)
            {
                continue;
            }

            int currentAmount = storedBaseResourceDictionary != null && storedBaseResourceDictionary.TryGetValue(baseResourceSO, out int amount)
                ? amount
                : 0;
            storedBaseResourceNetworkList.Add(new BaseResourceStorageNetworkState(i, currentAmount));
        }
    }

    private void RebuildLocalCacheFromNetworkState()
    {
        EnsureStorageInitialized();
        foreach (BaseResourceSO baseResourceSO in storableBaseResourcesSOList)
        {
            if (baseResourceSO != null)
            {
                storedBaseResourceDictionary[baseResourceSO] = 0;
            }
        }

        for (int i = 0; i < storedBaseResourceNetworkList.Count; i++)
        {
            BaseResourceStorageNetworkState state = storedBaseResourceNetworkList[i];
            BaseResourceSO baseResourceSO = GetResourceSOByIndex(state.resourceIndex);
            if (baseResourceSO != null)
            {
                storedBaseResourceDictionary[baseResourceSO] = Mathf.Max(0, state.amount);
            }
        }
    }

    private void StoredBaseResourceNetworkList_OnListChanged(NetworkListEvent<BaseResourceStorageNetworkState> changeEvent)
    {
        RebuildLocalCacheFromNetworkState();
        BaseResourceSO baseResourceSO = GetResourceSOByIndex(changeEvent.Value.resourceIndex);
        if (baseResourceSO != null)
        {
            OnBaseResourceAmountChanged(baseResourceSO, CheckBaseResourceAmount(baseResourceSO));
        }
    }

    private void AddBaseResourceAmount(BaseResourceSO baseResourceSO, int amount)
    {
        storedBaseResourceDictionary[baseResourceSO] += amount;
        Debug.Log($"Stored {amount} of {baseResourceSO.resourceName}. Total: {storedBaseResourceDictionary[baseResourceSO]}");
        OnBaseResourceAmountChanged(baseResourceSO, storedBaseResourceDictionary[baseResourceSO]);
    }

    private void AddBaseResourceAmountByIndex(int resourceIndex, int amount)
    {
        BaseResourceSO baseResourceSO = GetResourceSOByIndex(resourceIndex);
        if (baseResourceSO == null)
        {
            return;
        }

        int networkStateIndex = EnsureNetworkStateForResource(baseResourceSO);
        if (networkStateIndex < 0)
        {
            return;
        }

        BaseResourceStorageNetworkState state = storedBaseResourceNetworkList[networkStateIndex];
        state.amount = Mathf.Max(0, state.amount + amount);
        storedBaseResourceNetworkList[networkStateIndex] = state;
        storedBaseResourceDictionary[baseResourceSO] = state.amount;
        Debug.Log($"Stored {amount} of {baseResourceSO.resourceName}. Total: {state.amount}");
        OnBaseResourceAmountChanged(baseResourceSO, state.amount);
    }

    private bool TryRemoveBaseResourceAmountByIndex(int resourceIndex, int amount)
    {
        BaseResourceSO baseResourceSO = GetResourceSOByIndex(resourceIndex);
        if (baseResourceSO == null)
        {
            return false;
        }

        int networkStateIndex = FindNetworkStateIndex(resourceIndex);
        if (networkStateIndex < 0)
        {
            return false;
        }

        BaseResourceStorageNetworkState state = storedBaseResourceNetworkList[networkStateIndex];
        if (state.amount < amount)
        {
            return false;
        }

        state.amount -= amount;
        storedBaseResourceNetworkList[networkStateIndex] = state;
        storedBaseResourceDictionary[baseResourceSO] = state.amount;
        Debug.Log($"{baseResourceSO.resourceName} left in storage: {state.amount}");
        OnBaseResourceAmountChanged(baseResourceSO, state.amount);
        return true;
    }

    private bool TryRemoveBaseResourceAmountLocal(BaseResourceSO baseResourceSO, int amount)
    {
        if (!storedBaseResourceDictionary.TryGetValue(baseResourceSO, out int currentAmount) || currentAmount < amount)
        {
            return false;
        }

        storedBaseResourceDictionary[baseResourceSO] = currentAmount - amount;
        Debug.Log($"{baseResourceSO.resourceName} left in storage: {storedBaseResourceDictionary[baseResourceSO]}");
        OnBaseResourceAmountChanged(baseResourceSO, storedBaseResourceDictionary[baseResourceSO]);
        return true;
    }

    private int EnsureNetworkStateForResource(BaseResourceSO baseResourceSO)
    {
        int resourceIndex = GetResourceIndex(baseResourceSO);
        if (resourceIndex < 0)
        {
            return -1;
        }

        int networkStateIndex = FindNetworkStateIndex(resourceIndex);
        if (networkStateIndex >= 0)
        {
            return networkStateIndex;
        }

        storedBaseResourceNetworkList.Add(new BaseResourceStorageNetworkState(resourceIndex, 0));
        return storedBaseResourceNetworkList.Count - 1;
    }

    private int FindNetworkStateIndex(int resourceIndex)
    {
        for (int i = 0; i < storedBaseResourceNetworkList.Count; i++)
        {
            if (storedBaseResourceNetworkList[i].resourceIndex == resourceIndex)
            {
                return i;
            }
        }

        return -1;
    }

    private int GetResourceIndex(BaseResourceSO baseResourceSO)
    {
        return storableBaseResourcesSOList != null ? storableBaseResourcesSOList.IndexOf(baseResourceSO) : -1;
    }

    private BaseResourceSO GetResourceSOByIndex(int resourceIndex)
    {
        if (storableBaseResourcesSOList == null || resourceIndex < 0 || resourceIndex >= storableBaseResourcesSOList.Count)
        {
            return null;
        }

        return storableBaseResourcesSOList[resourceIndex];
    }

    private void EnsureStorageInitialized()
    {
        if (storedBaseResourceDictionary == null)
        {
            InitializeLocalStorageState();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStoreBaseResourceServerRpc(int resourceIndex, int amount)
    {
        AddBaseResourceAmountByIndex(resourceIndex, amount);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestRemoveBaseResourceServerRpc(int resourceIndex, int amount)
    {
        TryRemoveBaseResourceAmountByIndex(resourceIndex, amount);
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Base Storage");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Base Storage");
    }
}
