using System.Collections.Generic;
using UnityEngine;

public class BaseStorageNew : MonoBehaviour, IInteractableNew
{
    // To change for custom struct if we want to add more info about the storable resources in the future
    [SerializeField] protected List<BaseResourceSO> storableBaseResourcesSOList;
    protected Dictionary<BaseResourceSO, int> storedBaseResourceDictionary;

    public void Interact(Transform interactor)
    {
        Debug.Log("Interacted with Base Storage");
    }

    private void Awake()
    {
        storedBaseResourceDictionary = new Dictionary<BaseResourceSO, int>();
        storableBaseResourcesSOList = new List<BaseResourceSO>();

    }

    public bool IsStorable(BaseResourceSO baseResourceSO)
    {
        return storedBaseResourceDictionary.ContainsKey(baseResourceSO);
    }

    public virtual void StoreBaseResource(BaseResourceSO baseResourceSO, int amount)
    {
        if (IsStorable(baseResourceSO))
        {
            storedBaseResourceDictionary[baseResourceSO] += amount;
            Debug.Log($"Stored {amount} of {baseResourceSO.resourceName}. Total: {storedBaseResourceDictionary[baseResourceSO]}");
        }
        else
        {
            Debug.Log($"Cannot store {baseResourceSO.resourceName} in this storage.");
        }
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
        storedBaseResourceDictionary[baseResourceSO] -= amount;
        Debug.Log($"{baseResourceSO.resourceName} left in storage: {storedBaseResourceDictionary[baseResourceSO]}");
    }

    public void AddStorableBaseResource(BaseResourceSO baseResourceSO)
    {
        if (!IsStorable(baseResourceSO))
        {
            storableBaseResourcesSOList.Add(baseResourceSO);
            storedBaseResourceDictionary.Add(baseResourceSO, 0);
        }
    }
}
