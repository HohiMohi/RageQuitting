using System;
using Unity.Netcode;
using UnityEngine;

public class CarpenterTableFactory : BaseFactory
{
    [SerializeField] private CarpenterTableSwitch tableSwitch;
    [SerializeField] private DimensionChangeSwitch[] dimensionChangeSwitchArray;
    [SerializeField] private CarpenterTableMinigame carpenterTableMinigame;

    [Header("Component production properties")]
    [SerializeField] private float componentLengthMax;
    [SerializeField] private float componentLengthMin;
    [SerializeField] private float componentLenghtStep;
    [SerializeField] private float componentWidthMax;
    [SerializeField] private float componentWidthMin;
    [SerializeField] private float componentWidthStep;

    private readonly NetworkVariable<float> currentWidthNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<float> currentLengthNetwork = new NetworkVariable<float>();

    private float localCurrentWidth;
    private float localCurrentLength;

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

    public float CurrentWidth => IsNetworkSessionActive() ? currentWidthNetwork.Value : localCurrentWidth;
    public float CurrentLength => IsNetworkSessionActive() ? currentLengthNetwork.Value : localCurrentLength;
    public bool AreDimensionsMatchingSelectedComponent => CheckSettedComponentDimensions();

    public Vector2 GetCurrentDimensions()
    {
        return new Vector2(CurrentWidth, CurrentLength);
    }

    public Vector2 GetRequiredDimensionsForSelectedComponent()
    {
        MountableBridgeComponentSO selectedComponent = SelectedComponent;
        if (selectedComponent == null)
        {
            return Vector2.zero;
        }

        return new Vector2(selectedComponent.componentWidth, selectedComponent.componentLength);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        currentWidthNetwork.OnValueChanged += FactoryDimension_OnValueChanged;
        currentLengthNetwork.OnValueChanged += FactoryDimension_OnValueChanged;

        if (IsServer)
        {
            currentWidthNetwork.Value = componentWidthMin;
            currentLengthNetwork.Value = componentLengthMin;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentWidthNetwork.OnValueChanged -= FactoryDimension_OnValueChanged;
        currentLengthNetwork.OnValueChanged -= FactoryDimension_OnValueChanged;
        base.OnNetworkDespawn();
    }

    protected override void Start()
    {
        base.Start();

        if (tableSwitch != null)
        {
            tableSwitch.CarpenterTableSwitchPressed += CarpenterTableSwitch_OnCarpenterTableSwitchPressed;
        }

        if (dimensionChangeSwitchArray != null)
        {
            foreach (DimensionChangeSwitch dimensionChangeSwitch in dimensionChangeSwitchArray)
            {
                if (dimensionChangeSwitch != null)
                {
                    dimensionChangeSwitch.DimensionChangeSwitchPressed += DimensionChangeSwitch_OnSwitchPressed;
                }
            }
        }

        if (carpenterTableMinigame != null)
        {
            carpenterTableMinigame.MinigameCompletedEvent += CarpenterTableFactory_OnMinigameCompleted;
            carpenterTableMinigame.MinigameFailedEvent += CarpenterTableFactory_OnMinigameFailed;
            carpenterTableMinigame.MinigameCriticallyFailedEvent += CarpenterTableFactory_OnMinigameCriticallyFailed;
        }

        localCurrentLength = componentLengthMin;
        localCurrentWidth = componentWidthMin;
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override bool CanProduceAdditionalConditions(MountableBridgeComponentSO mountableBridgeComponentSO, out FactoryProductionFailureReason reason)
    {
        if (!CheckSettedComponentDimensions())
        {
            reason = FactoryProductionFailureReason.InvalidDimensions;
            return false;
        }

        reason = FactoryProductionFailureReason.None;
        return true;
    }

    protected override void HandleSelectedComponentChanged(MountableBridgeComponentSO selectedComponent)
    {
        BridgeComponentSelectionConfirm?.Invoke(this, new BridgeComponentSelectionConfirmEventArgs
        {
            mountableBridgeComponentSO = selectedComponent
        });
    }

    private void CarpenterTableFactory_OnMinigameCriticallyFailed(object sender, EventArgs e)
    {
        Debug.Log("Carpenter minigame is currently disabled in the production flow.");
    }

    private void CarpenterTableFactory_OnMinigameFailed(object sender, EventArgs e)
    {
        Debug.Log("Carpenter minigame is currently disabled in the production flow.");
    }

    private void CarpenterTableFactory_OnMinigameCompleted(object sender, EventArgs e)
    {
        Debug.Log("Carpenter minigame is currently disabled in the production flow.");
    }

    private void DimensionChangeSwitch_OnSwitchPressed(object sender, DimensionChangeSwitch.DimensionChangeSwitchPressedEventArgs e)
    {
        RequestChangeDimension(e.componentDimension, e.dimensionChangeType);
    }

    private void CarpenterTableSwitch_OnCarpenterTableSwitchPressed(object sender, CarpenterTableSwitch.CarpenterTableSwitchPressedEventArgs e)
    {
        RequestStartProduction();
    }

    private void RequestChangeDimension(ComponentDimension componentDimension, DimensionChangeType dimensionChangeType)
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                ApplyDimensionChangeServer(componentDimension, dimensionChangeType);
            }
            else
            {
                RequestChangeDimensionServerRpc((int)componentDimension, (int)dimensionChangeType);
            }

            return;
        }

        ApplyDimensionChangeLocal(componentDimension, dimensionChangeType);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestChangeDimensionServerRpc(int componentDimensionValue, int dimensionChangeTypeValue)
    {
        if (!Enum.IsDefined(typeof(ComponentDimension), componentDimensionValue)
            || !Enum.IsDefined(typeof(DimensionChangeType), dimensionChangeTypeValue))
        {
            return;
        }

        ApplyDimensionChangeServer((ComponentDimension)componentDimensionValue, (DimensionChangeType)dimensionChangeTypeValue);
    }

    private void ApplyDimensionChangeServer(ComponentDimension componentDimension, DimensionChangeType dimensionChangeType)
    {
        if (IsProducing)
        {
            return;
        }

        switch (componentDimension)
        {
            case ComponentDimension.Length:
                currentLengthNetwork.Value = ClampDimension(currentLengthNetwork.Value, componentLenghtStep, componentLengthMin, componentLengthMax, dimensionChangeType);
                break;
            case ComponentDimension.Width:
                currentWidthNetwork.Value = ClampDimension(currentWidthNetwork.Value, componentWidthStep, componentWidthMin, componentWidthMax, dimensionChangeType);
                break;
        }

        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDimensionChangeLocal(ComponentDimension componentDimension, DimensionChangeType dimensionChangeType)
    {
        if (IsProducing)
        {
            return;
        }

        switch (componentDimension)
        {
            case ComponentDimension.Length:
                localCurrentLength = ClampDimension(localCurrentLength, componentLenghtStep, componentLengthMin, componentLengthMax, dimensionChangeType);
                break;
            case ComponentDimension.Width:
                localCurrentWidth = ClampDimension(localCurrentWidth, componentWidthStep, componentWidthMin, componentWidthMax, dimensionChangeType);
                break;
        }

        Debug.Log($"Width: {CurrentWidth}, Length: {CurrentLength}");
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CheckSettedComponentDimensions()
    {
        MountableBridgeComponentSO selectedComponent = SelectedComponent;
        return selectedComponent != null
            && Mathf.Approximately(selectedComponent.componentWidth, CurrentWidth)
            && Mathf.Approximately(selectedComponent.componentLength, CurrentLength);
    }

    private float ClampDimension(float currentValue, float step, float min, float max, DimensionChangeType dimensionChangeType)
    {
        float signedStep = dimensionChangeType == DimensionChangeType.Decrease ? -step : step;
        return Mathf.Clamp(currentValue + signedStep, min, max);
    }

    private void FactoryDimension_OnValueChanged(float previousValue, float newValue)
    {
        Debug.Log($"Width: {CurrentWidth}, Length: {CurrentLength}");
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
