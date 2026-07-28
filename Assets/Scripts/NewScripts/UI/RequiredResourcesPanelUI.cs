using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class RequiredResourcesPanelUI : MonoBehaviour
{
    [SerializeField] private Transform contentHolder;
    [SerializeField] private GameObject requiredResourceObjectHolderTemplate;
    private List<GameObject> instantiatedRequiredResourceObjectHolderList;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Awake()
    {
        instantiatedRequiredResourceObjectHolderList = new List<GameObject>();
        requiredResourceObjectHolderTemplate.SetActive(false);
    }

    public void SetRequiredResourcesInformations(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        SetRequiredResourcesInformations(mountableBridgeComponentSO != null ? mountableBridgeComponentSO.requiredResources : null, null);
    }

    public void SetRequiredResourcesInformations(MountableBridgeComponentSO mountableBridgeComponentSO, BaseStorageNew storage)
    {
        SetRequiredResourcesInformations(mountableBridgeComponentSO != null ? mountableBridgeComponentSO.requiredResources : null, storage);
    }

    public void SetRequiredResourcesInformations(ProductionRecipeSO productionRecipeSO, BaseStorageNew storage)
    {
        SetRequiredResourcesInformations(productionRecipeSO != null ? productionRecipeSO.RequiredResources : null, storage);
    }

    private void SetRequiredResourcesInformations(RequiredResource[] requiredResources, BaseStorageNew storage)
    {
        if (instantiatedRequiredResourceObjectHolderList.Count != 0)
        {
            foreach (GameObject gameObject in instantiatedRequiredResourceObjectHolderList)
            {
                Destroy(gameObject);
            }
            instantiatedRequiredResourceObjectHolderList.Clear();
        }
        if (requiredResources == null)
        {
            return;
        }

        foreach(RequiredResource requiredResource in requiredResources)
        {
            GameObject requiredResourceInformationHolder = Instantiate(requiredResourceObjectHolderTemplate, contentHolder);
            instantiatedRequiredResourceObjectHolderList.Add(requiredResourceInformationHolder);
            int ownedAmount = storage != null ? Mathf.Max(0, storage.CheckBaseResourceAmount(requiredResource.resourceType)) : -1;
            string amountText = ownedAmount >= 0 ? $"{ownedAmount} / {requiredResource.amount}" : requiredResource.amount.ToString();
            requiredResourceInformationHolder.GetComponent<FactoryRequiredResourceUI>().SetProperties(requiredResource.resourceType.icon, requiredResource.resourceType.resourceName, amountText);
            requiredResourceInformationHolder.SetActive(true);
        }
    }
}
