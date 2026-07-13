using System;
using UnityEngine;

public class BlastFurnaceFactory : BaseFactory
{
    [SerializeField] protected FurnaceStorage furnaceStorage;

    public FurnaceStorage FurnaceStorage => furnaceStorage;

    public EventHandler<BridgeComponentSelectionConfirmEventArgs> BridgeComponentSelectionConfirm;
    public class BridgeComponentSelectionConfirmEventArgs : EventArgs
    {
        public MountableBridgeComponentSO mountableBridgeComponentSO;
    }

    protected override void Start()
    {
        base.Start();
        SyncSelectedComponentToFurnace(SelectedComponent);
    }

    protected override void HandleSelectedComponentChanged(MountableBridgeComponentSO selectedComponent)
    {
        SyncSelectedComponentToFurnace(selectedComponent);
        BridgeComponentSelectionConfirm?.Invoke(this, new BridgeComponentSelectionConfirmEventArgs
        {
            mountableBridgeComponentSO = selectedComponent
        });
    }

    public bool TryStartFurnaceProductionServer()
    {
        if (!IsNetworkSessionActive())
        {
            return TryStartFurnaceProductionLocal();
        }

        if (!IsServer)
        {
            return false;
        }

        if (!CanProduceSelectedComponentServer(out FactoryProductionFailureReason reason))
        {
            ReportFurnaceProductionFailureServer(reason);
            return false;
        }

        MountableBridgeComponentSO selectedComponent = SelectedComponent;
        if (!TryConsumeRequiredResources(selectedComponent))
        {
            ReportFurnaceProductionFailureServer(FactoryProductionFailureReason.MissingResources);
            return false;
        }

        BeginManualProductionServer();
        return true;
    }

    public bool TryCompleteFurnaceProductionServer()
    {
        if (!IsNetworkSessionActive())
        {
            FinishProductionLocal();
            return true;
        }

        if (!IsServer || !IsProducing)
        {
            return false;
        }

        FinishProductionServer();
        return true;
    }

    public void CancelFurnaceProductionServer()
    {
        if (!IsNetworkSessionActive())
        {
            CancelProductionLocal();
            return;
        }

        if (IsServer)
        {
            CancelProductionServer();
        }
    }

    public void UpdateFurnaceProductionProgress(float progressNormalized)
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                SetManualProductionProgressServer(progressNormalized);
            }

            return;
        }

        SetManualProductionProgressLocal(progressNormalized);
    }

    public void SyncSelectedComponentToFurnace(MountableBridgeComponentSO selectedComponent)
    {
        if (furnaceStorage != null)
        {
            furnaceStorage.SetSelectedMountableBridgeComponent(selectedComponent);
        }
    }

    private bool TryStartFurnaceProductionLocal()
    {
        if (!CanProduceSelectedComponentLocal(out FactoryProductionFailureReason reason))
        {
            ReportFurnaceProductionFailureLocal(reason);
            return false;
        }

        MountableBridgeComponentSO selectedComponent = SelectedComponent;
        if (!TryConsumeRequiredResources(selectedComponent))
        {
            ReportFurnaceProductionFailureLocal(FactoryProductionFailureReason.MissingResources);
            return false;
        }

        BeginManualProductionLocal();
        return true;
    }

    private void ReportFurnaceProductionFailureServer(FactoryProductionFailureReason reason)
    {
        Debug.Log($"Cannot start furnace production: {reason}");
    }

    private void ReportFurnaceProductionFailureLocal(FactoryProductionFailureReason reason)
    {
        Debug.Log($"Cannot start furnace production: {reason}");
    }
}
