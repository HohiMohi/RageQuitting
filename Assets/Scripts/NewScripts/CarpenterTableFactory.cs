using System;
using UnityEngine;
using static BlastFurnaceFactory;

public class CarpenterTableFactory : BaseFactory
{
    [SerializeField] private CarpenterTableSwitch tableSwitch;
    [SerializeField] private DimensionChangeSwitch[] dimensionChangeSwitchArray;
    [SerializeField] private CarpenterTableMinigame carpenterTableMinigame;

    #region Component production properties
    [Header("Component production properties")]
    [SerializeField] private float componentLengthMax;
    [SerializeField] private float componentLengthMin;
    [SerializeField] private float componentLenghtStep;
    [SerializeField] private float componentWidthMax;
    [SerializeField] private float componentWidthMin;
    [SerializeField] private float componentWidthStep;
    private float componentWidth;
    private float componentLength;
    private float currentWidth;
    private float currentLength;
    #endregion

    #region Events
    public EventHandler<BridgeComponentSelectionConfirmEventArgs> BridgeComponentSelectionConfirm;
    public class BridgeComponentSelectionConfirmEventArgs : EventArgs
    {
        public MountableBridgeComponentSO mountableBridgeComponentSO;
    }

    public EventHandler<TryEndProductionEventArgs> TryEndProduction;

    public class TryEndProductionEventArgs : EventArgs
    {
        public Transform interactor;
    }

    #endregion
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        tableSwitch.CarpenterTableSwitchPressed += CarpenterTableSwitch_OnCarpenterTableSwitchPressed;
        factoryInteractionUI.OnConfirmButtonClick += FactoryInteractionUI_OnBridgeComponentSelectionConfirm;
        carpenterTableMinigame.MinigameCompletedEvent += CarpenterTableFactory_OnMinigameCompleted;
        carpenterTableMinigame.MinigameFailedEvent += CarpenterTableFactory_OnMinigameFailed;
        carpenterTableMinigame.MinigameCriticallyFailedEvent += CarpenterTableFactory_OnMinigameCriticallyFailed;
        InitializeStorageStorableResourcesList();
        foreach (DimensionChangeSwitch dimensionChangeSwitch in dimensionChangeSwitchArray)
        {
            dimensionChangeSwitch.DimensionChangeSwitchPressed += DimensionChangeSwitch_OnSwitchPressed;
        }
        currentLength = componentLengthMin;
        currentWidth = componentWidthMin;
    }

    private void CarpenterTableFactory_OnMinigameCriticallyFailed(object sender, EventArgs e)
    {
        Debug.Log("Minigame critically failed event received");
    }

    private void CarpenterTableFactory_OnMinigameFailed(object sender, EventArgs e)
    {
        Debug.Log("Minigame failed event received");
    }

    private void CarpenterTableFactory_OnMinigameCompleted(object sender, EventArgs e)
    {
        SpawnMountableBridgeComponent(currentlySelectedMountableBridgeComponentSO);
    }

    private void DimensionChangeSwitch_OnSwitchPressed(object sender, DimensionChangeSwitch.DimensionChangeSwitchPressedEventArgs e)
    {
        HandleDimensionSwitch(e.componentDimension, e.dimensionChangeType);
        Debug.Log($"Width: {currentWidth}, Length: {currentLength}");
    }

    private void CarpenterTableSwitch_OnCarpenterTableSwitchPressed(object sender, CarpenterTableSwitch.CarpenterTableSwitchPressedEventArgs e)
    {
        if (currentlySelectedMountableBridgeComponentSO == null)
        {
            Debug.Log("No Mountable Bridge Component currently selected");
            //invoke event to show ui//message
        }
        else
        {
            if (CheckRequiredBaseResources(currentlySelectedMountableBridgeComponentSO))
            {
                if (CheckSettedComponentDimensions())
                {
                    RemoveBaseResourcesFromStorage(currentlySelectedMountableBridgeComponentSO);
                    TryEndProduction(this, new TryEndProductionEventArgs
                    {
                        interactor = e.interactor,
                    });
                }
                else
                {
                    Debug.Log("You need to adjust component dimensions to start production");
                }
            }
            else
            {
                Debug.Log("There are not enought resources to start production");
            }
        }
    }

    private bool CheckSettedComponentDimensions()
    {
        if (componentWidth == currentWidth && componentLength == currentLength)
            return true;
        else
            return false;
    }

    protected override void FactoryInteractionUI_OnBridgeComponentSelectionConfirm(object sender, FactoryInteractionUI.OnConfirmButtonClickEventArgs e)
    {
        bridgeComponentSpriteRenderer.sprite = e.mountableBridgeComponentSO.componentSprite;
        currentlySelectedMountableBridgeComponentSO = e.mountableBridgeComponentSO;
        componentLength = e.mountableBridgeComponentSO.componentLength;
        componentWidth = e.mountableBridgeComponentSO.componentWidth;
        BridgeComponentSelectionConfirm?.Invoke(this, new BridgeComponentSelectionConfirmEventArgs
        {
            mountableBridgeComponentSO = currentlySelectedMountableBridgeComponentSO
        });
    }

    private void HandleDimensionSwitch(ComponentDimension componentDimension, DimensionChangeType dimensionChangeType)
    {
        float step = 0;
        switch (componentDimension)
        {
            case ComponentDimension.Length:
                step = componentLenghtStep;
                if (dimensionChangeType == DimensionChangeType.Decrease)
                    step *= -1;
                currentLength = Mathf.Clamp(currentLength + step, componentLengthMin, componentLengthMax);
                break;
            case ComponentDimension.Width:
                step = componentWidthStep;
                if (dimensionChangeType == DimensionChangeType.Decrease)
                    step *= -1;
                currentWidth = Mathf.Clamp(currentWidth + step, componentWidthMin, componentWidthMax);
                break;
            default:
                break;

        }
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
