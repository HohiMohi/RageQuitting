using System;
using System.Collections;
using UnityEngine;

public class BridgeComponentNeededEquippableItemUI : MonoBehaviour
{
    [SerializeField] private BridgeComponent bridgeComponent;
    [SerializeField] private Transform equippableItemTypeTextTemplate;
    [SerializeField] private Transform equippableItemTypeTextHolder;
    private bool uiSetted;

    private void Awake()
    {
        uiSetted = false;
    }

    private void Start()
    {
        bridgeComponent.BridgeComponentSOAssigned += BridgeComponent_OnBridgeComponentSOAssigned;
        bridgeComponent.EquippedItemTypeNeeded += BridgeComponent_OnEquippableItemTypeNeeded;
        Hide();
    }

    private void BridgeComponent_OnEquippableItemTypeNeeded(object sender, EventArgs e)
    {
        Show();
        StartCoroutine(HideUIAfterTime(1.5f));
    }

    private void BridgeComponent_OnBridgeComponentSOAssigned(object sender, BridgeComponent.BridgeComponentSOAssignedEventArgs e)
    {
        PrepareUI(e.bridgeComponentSO);
    }

    public void PrepareUI(BridgeComponentSO bridgeComponentSO)
    {
        if (bridgeComponentSO != null && !uiSetted)
        {
            Debug.Log(bridgeComponentSO);
            foreach (EquippableItemType equippableItemType in bridgeComponentSO.supportedEquippableItemTypeList)
            {
                Transform equippableItemTypeText = Instantiate(equippableItemTypeTextTemplate, equippableItemTypeTextHolder);
                equippableItemTypeText.GetComponent<BridgeComponentNeededEquippableItemSingleUI>().SetEquippableItemTypeText(equippableItemType);
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

    private IEnumerator HideUIAfterTime(float time)
    {
        yield return new WaitForSeconds(time);
        Hide();
    }
}
