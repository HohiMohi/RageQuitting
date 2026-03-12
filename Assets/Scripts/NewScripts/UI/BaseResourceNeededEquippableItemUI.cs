using System;
using System.Collections;
using UnityEngine;

public class BaseResourceNeededEquippableItemUI : MonoBehaviour
{
    [SerializeField] private BaseResourceNew baseResource;
    [SerializeField] private Transform equippableItemTypeTextTemplate;
    [SerializeField] private Transform equippableItemTypeTextHolder;
    [SerializeField] private float showUITime;
    private bool uiSetted;

    private void Awake()
    {
        uiSetted = false;
    }

    private void Start()
    {
        baseResource.EquippableItemNeeded += BaseResource_OnEquippableItemTypeNeeded;
        Hide();
    }

    private void BaseResource_OnEquippableItemTypeNeeded(object sender, EventArgs e)
    {
        PrepareUI();
        Show();
        StartCoroutine(HideUIAfterTime());
    }

    private void BaseResource_OnBridgeComponentSOAssigned(object sender, BridgeComponent.BridgeComponentSOAssignedEventArgs e)
    {
        PrepareUI();
    }

    public void PrepareUI()
    {
        if (!uiSetted)
        {
            
            foreach (BaseResourceDestructionRecipe recipe in baseResource.GetBaseResourceSO().baseResourceDestructionRecipeArray)
            {
                Transform equippableItemTypeText = Instantiate(equippableItemTypeTextTemplate, equippableItemTypeTextHolder);
                equippableItemTypeText.GetComponent<BaseResourceNeededEquippableItemSingleUI>().SetEquippableItemTypeText(recipe.neededEquippableItemType);
                equippableItemTypeText.gameObject.SetActive(true);
            }
            Destroy(equippableItemTypeTextTemplate.gameObject);
            uiSetted = true;
        }
    }

    private void Show()
    {
        gameObject.SetActive(true);
    }
    private void Hide()
    {
        gameObject.SetActive(false);
    }

    private IEnumerator HideUIAfterTime()
    {
        yield return new WaitForSeconds(showUITime);
        Hide();
    }
}
