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
        if (instantiatedRequiredResourceObjectHolderList.Count != 0)
        {
            foreach (GameObject gameObject in instantiatedRequiredResourceObjectHolderList)
            {
                Destroy(gameObject);
            }
        }
        foreach(RequiredResource requiredResource in mountableBridgeComponentSO.requiredResources)
        {
            GameObject requiredResourceInformationHolder = Instantiate(requiredResourceObjectHolderTemplate, contentHolder);
            instantiatedRequiredResourceObjectHolderList.Add(requiredResourceInformationHolder);
            requiredResourceInformationHolder.GetComponent<FactoryRequiredResourceUI>().SetProperties(requiredResource.resourceType.icon, requiredResource.resourceType.resourceName, requiredResource.amount.ToString());
            requiredResourceInformationHolder.SetActive(true);
        }
    }
}
