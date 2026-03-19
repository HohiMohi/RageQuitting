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
