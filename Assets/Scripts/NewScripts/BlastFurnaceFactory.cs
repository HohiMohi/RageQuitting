using System;
using UnityEngine;

public class BlastFurnaceFactory : BaseFactory
{
    [SerializeField] protected FurnaceStorage furnaceStorage;
    public EventHandler<BridgeComponentSelectionConfirmEventArgs> BridgeComponentSelectionConfirm;
    public class BridgeComponentSelectionConfirmEventArgs: EventArgs
    {
        public MountableBridgeComponentSO mountableBridgeComponentSO;
    }
    private void Start()
    {
        furnaceStorage.ProductionStarted += FurnaceStorage_OnProductionStarted;
        furnaceStorage.ProductionFinished += FurnaceStorage_OnProductionFinished;
        factoryInteractionUI.OnConfirmButtonClick += FactoryInteractionUI_OnBridgeComponentSelectionConfirm;
        InitializeStorageStorableResourcesList();


    }

    private void FurnaceStorage_OnProductionFinished(object sender, EventArgs e)
    {
        SpawnMountableBridgeComponent(currentlySelectedMountableBridgeComponentSO);
    }

    private void FurnaceStorage_OnProductionStarted(object sender, EventArgs e)
    {
        RemoveBaseResourcesFromStorage(currentlySelectedMountableBridgeComponentSO);

    }

    protected override void FactoryInteractionUI_OnBridgeComponentSelectionConfirm(object sender, FactoryInteractionUI.OnConfirmButtonClickEventArgs e)
    {
        bridgeComponentSpriteRenderer.sprite = e.mountableBridgeComponentSO.componentSprite;
        currentlySelectedMountableBridgeComponentSO = e.mountableBridgeComponentSO;
        BridgeComponentSelectionConfirm?.Invoke(this, new BridgeComponentSelectionConfirmEventArgs
        {
            mountableBridgeComponentSO = currentlySelectedMountableBridgeComponentSO
        });
    }

}
