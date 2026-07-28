using System;
using UnityEngine;

public class BlastFurnaceFactory : BaseFactory
{
    [SerializeField] protected FurnaceStorage furnaceStorage;

    public FurnaceStorage FurnaceStorage => furnaceStorage;

    public EventHandler<BridgeComponentSelectionConfirmEventArgs> BridgeComponentSelectionConfirm;
    public class BridgeComponentSelectionConfirmEventArgs : EventArgs
    {
        public ProductionRecipeSO productionRecipeSO;
    }

    protected override void Start()
    {
        base.Start();
        SyncSelectedRecipeToFurnace(SelectedRecipe);
    }

    protected override void HandleSelectedRecipeChanged(ProductionRecipeSO selectedRecipe)
    {
        SyncSelectedRecipeToFurnace(selectedRecipe);
        BridgeComponentSelectionConfirm?.Invoke(this, new BridgeComponentSelectionConfirmEventArgs
        {
            productionRecipeSO = selectedRecipe
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

        ProductionRecipeSO selectedRecipe = SelectedRecipe;
        if (!TryConsumeRequiredResources(selectedRecipe))
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

    public void SyncSelectedRecipeToFurnace(ProductionRecipeSO selectedRecipe)
    {
        if (furnaceStorage != null)
        {
            furnaceStorage.SetSelectedProductionRecipe(selectedRecipe);
        }
    }

    private bool TryStartFurnaceProductionLocal()
    {
        if (!CanProduceSelectedComponentLocal(out FactoryProductionFailureReason reason))
        {
            ReportFurnaceProductionFailureLocal(reason);
            return false;
        }

        ProductionRecipeSO selectedRecipe = SelectedRecipe;
        if (!TryConsumeRequiredResources(selectedRecipe))
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
