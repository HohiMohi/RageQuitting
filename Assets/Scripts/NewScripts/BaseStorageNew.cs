using System.Collections.Generic;
using UnityEngine;

public class BaseStorageNew : MonoBehaviour, IInteractableNew
{
    // To change for custom struct if we want to add more info about the storable resources in the future
    [SerializeField] protected BaseResourceListSO storableBaseResourceSOList;
    protected Dictionary<BaseResourceSO, int> storedBaseResourceDictionary;

    public void Interact(Transform interactor)
    {
        Debug.Log("Interacted with Base Storage");
    }

    private void Awake()
    {
        storedBaseResourceDictionary = new Dictionary<BaseResourceSO, int>();
        foreach (BaseResourceSO baseResourceSO in storableBaseResourceSOList.baseResourceSOList)
        {
            storedBaseResourceDictionary.Add(baseResourceSO, 0);
        }
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
}
