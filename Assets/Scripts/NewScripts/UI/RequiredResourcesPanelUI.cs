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
        SetRequiredResourcesInformations(mountableBridgeComponentSO, null);
    }

    public void SetRequiredResourcesInformations(MountableBridgeComponentSO mountableBridgeComponentSO, BaseStorageNew storage)
    {
        if (instantiatedRequiredResourceObjectHolderList.Count != 0)
        {
            foreach (GameObject gameObject in instantiatedRequiredResourceObjectHolderList)
            {
                Destroy(gameObject);
            }
            instantiatedRequiredResourceObjectHolderList.Clear();
        }
        if (mountableBridgeComponentSO == null)
        {
            return;
        }

        foreach(RequiredResource requiredResource in mountableBridgeComponentSO.requiredResources)
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
