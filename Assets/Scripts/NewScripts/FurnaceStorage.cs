using System;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

public class FurnaceStorage : BaseStorageNew
{
    [SerializeField] private BlastFurnaceFactory blastFurnaceFactory;
    [SerializeField] private Bellows bellows;
    [SerializeField] private VentilationGrille ventilationGrille;
    [SerializeField] private Kindling kindling;
    [SerializeField] private FurnaceSwitch furnaceSwitch;
    [SerializeField] private BlastFurnaceMinigame minigame;

    [Header("Furnace Parameters")]
    [Tooltip("Furnace Pressure Temperature Multiplier describes how much bellows and ventilation grille interactions will affect furnace temperature.")]
    [SerializeField] private float furnacePressureTemperatureMultiplier;
    [Tooltip("Furnace Pressure Change Speed determines how fast pressure variable will return to the initial value (0) - in seconds.")]
    [SerializeField] private float furnacePressureChangeSpeed;
    [Tooltip("Furnace Fuel Normalized Temperature describes describes the temperature, at which the furnace will use up one fuel base in 60 seconds.")]
    [SerializeField] private float furnaceFuelNormalizedTemperature;
    [Tooltip("Furnace Heating Rate describes how fast (in 1 sec) temperature raises without player's interference. There has to be fuel in furnace.")]
    [SerializeField] private float furnaceHeatingRate;
    [Tooltip("Furnace Max Temperature describes maximum temperature, to which temperature will raise without Player's interference.")]
    [SerializeField] private float furnaceMaxTemperature;
    [Tooltip("Furnace Overheat Temperature Decay Rate describes how fast temperature will dropping (per sec) to furnaceMaxTemperature, if pressure is <= 0.")]
    [SerializeField] private float furnaceOverheatTemperatureDecayRate;
    [SerializeField] private float furnaceFuel;
    [SerializeField] private bool isFuelAvailable;

    private readonly NetworkVariable<bool> furnaceIsOnNetwork = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<bool> furnaceIsOnFireNetwork = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<float> furnaceTemperatureNetwork = new NetworkVariable<float>(30f);
    private readonly NetworkVariable<float> furnacePressureNetwork = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<float> furnaceFuelNetwork = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<float> productionProgressNetwork = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<float> combustionProgressNetwork = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<int> selectedComponentIndexNetwork = new NetworkVariable<int>(-1);

    private bool localFurnaceIsOn;
    private bool localFurnaceIsOnFire;
    private float localFurnaceTemperature;
    private float localFurnacePressure;
    private float localFurnaceFuel;
    private float localProductionProgress;
    private float localCombustionProgress;
    private int localSelectedComponentIndex = -1;
    private float furnaceReferencePressureHolder;
    private ProductionRecipeSO selectedProductionRecipeSO;

    private float neededProgress;
    private float meltingPoint;
    private float combustionTemperature;
    private float neededCombustionProgress;

    public EventHandler ProductionStarted;
    public EventHandler ProductionFinished;
    public EventHandler FurnaceStateChanged;
    public EventHandler<TryEndProductionEventArgs> TryEndProduction;

    public class TryEndProductionEventArgs : EventArgs
    {
        public Transform interactor;
    }

    public bool CanCompleteProduction => IsFurnaceOn && GetProductionProgressNormalized() >= 1f && !IsMaterialBurned;
    public bool IsMaterialBurned => neededCombustionProgress > 0f && GetRawCombustionProgress() >= neededCombustionProgress;
    public float CurrentTemperature => IsNetworkSessionActive() ? furnaceTemperatureNetwork.Value : localFurnaceTemperature;
    public float CurrentPressure => IsNetworkSessionActive() ? furnacePressureNetwork.Value : localFurnacePressure;
    public float CurrentFuel => IsNetworkSessionActive() ? furnaceFuelNetwork.Value : localFurnaceFuel;
    public float CurrentMeltingPoint => meltingPoint;
    public float CurrentCombustionTemperature => combustionTemperature;
    public bool HasSelectedComponent => selectedProductionRecipeSO != null;
    public bool IsFurnaceOn => IsNetworkSessionActive() ? furnaceIsOnNetwork.Value : localFurnaceIsOn;
    public bool IsFurnaceOnFire => IsNetworkSessionActive() ? furnaceIsOnFireNetwork.Value : localFurnaceIsOnFire;

    protected override void Awake()
    {
        base.Awake();
        localFurnaceIsOn = false;
        localFurnaceIsOnFire = false;
        isFuelAvailable = false;
        localFurnacePressure = 0f;
        localFurnaceTemperature = 30f;
        localFurnaceFuel = furnaceFuel;
    }

    private void Start()
    {
        if (blastFurnaceFactory != null)
        {
            blastFurnaceFactory.BridgeComponentSelectionConfirm += BlastFurnaceFactory_OnBridgeComponentSelectionConfirm;
        }

        if (bellows != null)
        {
            bellows.BellowsPressed += Bellows_OnBellowsPressed;
        }

        if (ventilationGrille != null)
        {
            ventilationGrille.VentilationGrilleClosed += VentilationGrille_OnVentilationGrilleClosed;
        }

        if (kindling != null)
        {
            kindling.SetFurnaceOnFire += Kindling_OnFurnaceSettedOnFire;
        }

        if (furnaceSwitch != null)
        {
            furnaceSwitch.FurnaceSwitchPressed += FurnaceSwitch_OnFurnaceSwitchPressed;
        }

        if (minigame != null)
        {
            minigame.MinigameCompletedEvent += BlastFurnaceMinigame_OnMinigameCompleted;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        furnaceIsOnNetwork.OnValueChanged += FurnaceIsOnNetwork_OnValueChanged;
        furnaceIsOnFireNetwork.OnValueChanged += FurnaceStateNetwork_OnValueChanged;
        furnaceTemperatureNetwork.OnValueChanged += FurnaceStateNetwork_OnValueChanged;
        furnacePressureNetwork.OnValueChanged += FurnaceStateNetwork_OnValueChanged;
        furnaceFuelNetwork.OnValueChanged += FurnaceStateNetwork_OnValueChanged;
        productionProgressNetwork.OnValueChanged += FurnaceStateNetwork_OnValueChanged;
        combustionProgressNetwork.OnValueChanged += FurnaceStateNetwork_OnValueChanged;
        selectedComponentIndexNetwork.OnValueChanged += SelectedComponentIndexNetwork_OnValueChanged;

        if (IsServer)
        {
            furnaceTemperatureNetwork.Value = 30f;
            furnacePressureNetwork.Value = 0f;
            furnaceFuelNetwork.Value = Mathf.Max(0f, furnaceFuel);
            productionProgressNetwork.Value = 0f;
            combustionProgressNetwork.Value = 0f;
        }

        UpdateSelectedComponentFromIndex(GetSelectedComponentIndex());
    }

    public override void OnNetworkDespawn()
    {
        furnaceIsOnNetwork.OnValueChanged -= FurnaceIsOnNetwork_OnValueChanged;
        furnaceIsOnFireNetwork.OnValueChanged -= FurnaceStateNetwork_OnValueChanged;
        furnaceTemperatureNetwork.OnValueChanged -= FurnaceStateNetwork_OnValueChanged;
        furnacePressureNetwork.OnValueChanged -= FurnaceStateNetwork_OnValueChanged;
        furnaceFuelNetwork.OnValueChanged -= FurnaceStateNetwork_OnValueChanged;
        productionProgressNetwork.OnValueChanged -= FurnaceStateNetwork_OnValueChanged;
        combustionProgressNetwork.OnValueChanged -= FurnaceStateNetwork_OnValueChanged;
        selectedComponentIndexNetwork.OnValueChanged -= SelectedComponentIndexNetwork_OnValueChanged;
        base.OnNetworkDespawn();
    }

    private void FixedUpdate()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                SimulateFurnace(Time.fixedDeltaTime);
            }

            return;
        }

        SimulateFurnace(Time.fixedDeltaTime);
    }

    public void SetSelectedProductionRecipe(ProductionRecipeSO productionRecipeSO)
    {
        int selectedIndex = blastFurnaceFactory != null
            ? blastFurnaceFactory.GetProductionRecipeSOIndex(productionRecipeSO)
            : -1;

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                selectedComponentIndexNetwork.Value = selectedIndex;
                ResetProductionValues();
            }

            UpdateSelectedComponentFromIndex(selectedIndex);
            return;
        }

        localSelectedComponentIndex = selectedIndex;
        ResetProductionValues();
        UpdateSelectedComponentFromIndex(selectedIndex);
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RequestToggleFurnace(Transform interactor)
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                ToggleFurnaceServer();
            }
            else
            {
                ToggleFurnaceServerRpc();
            }

            return;
        }

        ToggleFurnaceLocal();
    }

    public void RequestBellowsPress()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                ApplyPressureDeltaServer(1f);
            }
            else
            {
                BellowsPressServerRpc();
            }

            return;
        }

        ApplyPressureDeltaLocal(1f);
    }

    public void RequestVentilationGrilleClose()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                ApplyPressureDeltaServer(-1f);
            }
            else
            {
                VentilationGrilleCloseServerRpc();
            }

            return;
        }

        ApplyPressureDeltaLocal(-1f);
    }

    public void RequestKindlingUse()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                SetFurnaceOnFireServer();
            }
            else
            {
                KindlingUseServerRpc();
            }

            return;
        }

        SetFurnaceOnFireLocal();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleFurnaceServerRpc()
    {
        ToggleFurnaceServer();
    }

    [ServerRpc(RequireOwnership = false)]
    private void BellowsPressServerRpc()
    {
        ApplyPressureDeltaServer(1f);
    }

    [ServerRpc(RequireOwnership = false)]
    private void VentilationGrilleCloseServerRpc()
    {
        ApplyPressureDeltaServer(-1f);
    }

    [ServerRpc(RequireOwnership = false)]
    private void KindlingUseServerRpc()
    {
        SetFurnaceOnFireServer();
    }

    private void ToggleFurnaceServer()
    {
        if (furnaceIsOnNetwork.Value)
        {
            if (CanCompleteProduction && blastFurnaceFactory != null && blastFurnaceFactory.TryCompleteFurnaceProductionServer())
            {
                StopFurnaceServer(true);
                return;
            }

            StopFurnaceServer(false);
            return;
        }

        if (selectedProductionRecipeSO == null || blastFurnaceFactory == null)
        {
            Debug.Log("No production recipe currently selected");
            return;
        }

        if (blastFurnaceFactory.TryStartFurnaceProductionServer())
        {
            StartFurnaceServer();
        }
    }

    private void ToggleFurnaceLocal()
    {
        if (localFurnaceIsOn)
        {
            if (CanCompleteProduction && blastFurnaceFactory != null && blastFurnaceFactory.TryCompleteFurnaceProductionServer())
            {
                StopFurnaceLocal(true);
                return;
            }

            StopFurnaceLocal(false);
            return;
        }

        if (selectedProductionRecipeSO == null || blastFurnaceFactory == null)
        {
            Debug.Log("No production recipe currently selected");
            return;
        }

        if (blastFurnaceFactory.TryStartFurnaceProductionServer())
        {
            StartFurnaceLocal();
        }
    }

    private void StartFurnaceServer()
    {
        furnaceIsOnNetwork.Value = true;
        furnaceIsOnFireNetwork.Value = false;
        productionProgressNetwork.Value = 0f;
        combustionProgressNetwork.Value = 0f;
        ProductionStarted?.Invoke(this, EventArgs.Empty);
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
        Debug.Log("Furnace turned on. Starting production");
    }

    private void StartFurnaceLocal()
    {
        localFurnaceIsOn = true;
        localFurnaceIsOnFire = false;
        localProductionProgress = 0f;
        localCombustionProgress = 0f;
        ProductionStarted?.Invoke(this, EventArgs.Empty);
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
        Debug.Log("Furnace turned on. Starting production");
    }

    private void StopFurnaceServer(bool completed)
    {
        furnaceIsOnNetwork.Value = false;
        furnaceIsOnFireNetwork.Value = false;
        productionProgressNetwork.Value = 0f;
        combustionProgressNetwork.Value = 0f;
        if (!completed && blastFurnaceFactory != null)
        {
            blastFurnaceFactory.CancelFurnaceProductionServer();
        }

        ProductionFinished?.Invoke(this, EventArgs.Empty);
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StopFurnaceLocal(bool completed)
    {
        localFurnaceIsOn = false;
        localFurnaceIsOnFire = false;
        localProductionProgress = 0f;
        localCombustionProgress = 0f;
        if (!completed && blastFurnaceFactory != null)
        {
            blastFurnaceFactory.CancelFurnaceProductionServer();
        }

        ProductionFinished?.Invoke(this, EventArgs.Empty);
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetFurnaceOnFireServer()
    {
        if (furnaceIsOnNetwork.Value)
        {
            furnaceIsOnFireNetwork.Value = true;
            Debug.Log("Furnace is now on fire.");
            return;
        }

        Debug.Log("Furnace is off");
    }

    private void SetFurnaceOnFireLocal()
    {
        if (localFurnaceIsOn)
        {
            localFurnaceIsOnFire = true;
            Debug.Log("Furnace is now on fire.");
            FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Debug.Log("Furnace is off");
    }

    private void ApplyPressureDeltaServer(float delta)
    {
        if (!furnaceIsOnNetwork.Value)
        {
            return;
        }

        furnacePressureNetwork.Value += delta;
        furnaceReferencePressureHolder = furnacePressureNetwork.Value;
    }

    private void ApplyPressureDeltaLocal(float delta)
    {
        if (!localFurnaceIsOn)
        {
            return;
        }

        localFurnacePressure += delta;
        furnaceReferencePressureHolder = localFurnacePressure;
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SimulateFurnace(float deltaTime)
    {
        if (IsFurnaceOnFire || CurrentTemperature > 30f || Mathf.Abs(CurrentPressure) > 0.1f)
        {
            HandleFurnaceTemperature(deltaTime);
        }

        if (!IsFurnaceOnFire)
        {
            return;
        }

        HandleFurnaceFuelUsage(deltaTime);
        HandleProductionProgress(deltaTime);
        HandleCombustionProgress(deltaTime);
    }

    private void HandleFurnaceTemperature(float deltaTime)
    {
        float currentFuel = CurrentFuel;
        float currentTemperature = CurrentTemperature;
        float currentPressure = CurrentPressure;

        if (IsFurnaceOnFire && currentFuel > 0)
        {
            if (currentPressure != 0)
            {
                currentTemperature += furnacePressureTemperatureMultiplier * currentPressure * deltaTime;
            }

            if (currentTemperature < furnaceMaxTemperature)
            {
                currentTemperature += furnaceHeatingRate * deltaTime;
            }
        }
        else if (currentTemperature > 30)
        {
            currentTemperature -= deltaTime;
        }

        if (currentPressure <= 0 && currentTemperature > furnaceMaxTemperature)
        {
            currentTemperature -= deltaTime * furnaceOverheatTemperatureDecayRate;
            currentTemperature = Math.Clamp(currentTemperature, furnaceMaxTemperature, 3000);
        }

        currentTemperature = math.clamp(currentTemperature, 30, 3000);
        if (math.abs(currentPressure) > 0.1f)
        {
            currentPressure -= deltaTime * furnaceReferencePressureHolder / furnacePressureChangeSpeed;
        }
        else
        {
            currentPressure = 0;
        }

        SetTemperatureAndPressure(currentTemperature, currentPressure);
    }

    private void HandleFurnaceFuelUsage(float deltaTime)
    {
        float currentFuel = CurrentFuel;
        float currentTemperature = CurrentTemperature;

        if (currentFuel < 1 && isFuelAvailable && IsFurnaceOn)
        {
            TryRefuelFurnace();
            currentFuel = CurrentFuel;
        }

        if (currentFuel > 0 && currentTemperature > 30)
        {
            float fuelUsage = (currentTemperature / furnaceFuelNormalizedTemperature) * deltaTime / 60f;
            currentFuel -= fuelUsage;
            SetFuel(currentFuel);
        }

        if (currentFuel <= 0 && currentTemperature <= 30 && IsFurnaceOnFire)
        {
            if (IsNetworkSessionActive())
            {
                if (IsServer)
                {
                    StopFurnaceServer(false);
                }
            }
            else
            {
                StopFurnaceLocal(false);
            }
        }
    }

    private void HandleProductionProgress(float deltaTime)
    {
        if (!IsFurnaceOn || neededProgress <= 0)
        {
            return;
        }

        float currentProductionProgress = GetRawProductionProgress();
        float temperatureDelta = CurrentTemperature - meltingPoint;
        if ((temperatureDelta > 0 && currentProductionProgress < neededProgress)
            || (temperatureDelta < 0 && currentProductionProgress > 0 && GetRawCombustionProgress() == 0))
        {
            currentProductionProgress += temperatureDelta * deltaTime;
            currentProductionProgress = math.clamp(currentProductionProgress, 0, neededProgress);
            SetProductionProgress(currentProductionProgress);
            if (blastFurnaceFactory != null)
            {
                blastFurnaceFactory.UpdateFurnaceProductionProgress(GetProductionProgressNormalized());
            }
        }
    }

    private void HandleCombustionProgress(float deltaTime)
    {
        if (!IsFurnaceOn || neededCombustionProgress <= 0)
        {
            return;
        }

        float currentCombustionProgress = GetRawCombustionProgress();
        float temperatureDelta = CurrentTemperature - combustionTemperature;
        if ((temperatureDelta > 0 && currentCombustionProgress < neededCombustionProgress && Mathf.Approximately(neededProgress, GetRawProductionProgress()))
            || (temperatureDelta < 0 && currentCombustionProgress > 0))
        {
            currentCombustionProgress += temperatureDelta * deltaTime;
            currentCombustionProgress = math.clamp(currentCombustionProgress, 0, neededCombustionProgress);
            SetCombustionProgress(currentCombustionProgress);
            if (Mathf.Approximately(currentCombustionProgress, neededCombustionProgress))
            {
                Debug.Log("Spalone");
            }
        }
    }

    private void TryRefuelFurnace()
    {
        if (IsNetworkSessionActive() && !IsServer)
        {
            return;
        }

        bool fuelAdded = false;
        foreach (BaseResourceSO baseResourceSO in storableBaseResourcesSOList)
        {
            if (baseResourceSO != null
                && baseResourceSO.furnaceFuelAmount > 0f
                && CheckBaseResourceAmount(baseResourceSO) > 0)
            {
                SetFuel(CurrentFuel + baseResourceSO.furnaceFuelAmount);
                fuelAdded = true;
                TryRemoveBaseResourceAmount(baseResourceSO, 1);
                break;
            }
        }

        if (!fuelAdded)
        {
            isFuelAvailable = false;
        }
    }

    public override void StoreBaseResource(BaseResourceSO baseResourceSO, int amount)
    {
        base.StoreBaseResource(baseResourceSO, amount);
        isFuelAvailable = true;
    }

    public float GetProductionProgressNormalized()
    {
        return neededProgress > 0 ? Mathf.Clamp01(GetRawProductionProgress() / neededProgress) : 0f;
    }

    public float GetCombustionProgressNormalized()
    {
        return neededCombustionProgress > 0 ? Mathf.Clamp01(GetRawCombustionProgress() / neededCombustionProgress) : 0f;
    }

    private void BlastFurnaceFactory_OnBridgeComponentSelectionConfirm(object sender, BlastFurnaceFactory.BridgeComponentSelectionConfirmEventArgs e)
    {
        SetSelectedProductionRecipe(e.productionRecipeSO);
    }

    private void VentilationGrille_OnVentilationGrilleClosed(object sender, EventArgs e)
    {
        RequestVentilationGrilleClose();
    }

    private void Bellows_OnBellowsPressed(object sender, EventArgs e)
    {
        RequestBellowsPress();
    }

    private void FurnaceSwitch_OnFurnaceSwitchPressed(object sender, FurnaceSwitch.FurnaceSwitchPressedEventArgs e)
    {
        RequestToggleFurnace(e.interactor);
    }

    private void Kindling_OnFurnaceSettedOnFire(object sender, EventArgs e)
    {
        RequestKindlingUse();
    }

    private void BlastFurnaceMinigame_OnMinigameCompleted(object sender, EventArgs e)
    {
        Debug.Log("Blast furnace minigame is currently disabled in the production flow.");
    }

    private void FurnaceIsOnNetwork_OnValueChanged(bool previousValue, bool newValue)
    {
        if (newValue && !previousValue)
        {
            ProductionStarted?.Invoke(this, EventArgs.Empty);
        }
        else if (!newValue && previousValue)
        {
            ProductionFinished?.Invoke(this, EventArgs.Empty);
        }

        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FurnaceStateNetwork_OnValueChanged<T>(T previousValue, T newValue)
    {
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectedComponentIndexNetwork_OnValueChanged(int previousValue, int newValue)
    {
        UpdateSelectedComponentFromIndex(newValue);
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectedComponentFromIndex(int selectedIndex)
    {
        selectedProductionRecipeSO = blastFurnaceFactory != null
            ? blastFurnaceFactory.GetProductionRecipeSOByIndex(selectedIndex)
            : null;

        if (selectedProductionRecipeSO == null)
        {
            neededProgress = 0f;
            meltingPoint = 0f;
            combustionTemperature = 0f;
            neededCombustionProgress = 0f;
            return;
        }

        neededProgress = selectedProductionRecipeSO.NeededProgress;
        meltingPoint = selectedProductionRecipeSO.MeltingPoint;
        combustionTemperature = selectedProductionRecipeSO.CombustionTemperature;
        neededCombustionProgress = selectedProductionRecipeSO.NeededCombustionProgress;
    }

    private void ResetProductionValues()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                furnaceIsOnNetwork.Value = false;
                furnaceIsOnFireNetwork.Value = false;
                productionProgressNetwork.Value = 0f;
                combustionProgressNetwork.Value = 0f;
            }

            return;
        }

        localFurnaceIsOn = false;
        localFurnaceIsOnFire = false;
        localProductionProgress = 0f;
        localCombustionProgress = 0f;
    }

    private int GetSelectedComponentIndex()
    {
        return IsNetworkSessionActive() ? selectedComponentIndexNetwork.Value : localSelectedComponentIndex;
    }

    private float GetRawProductionProgress()
    {
        return IsNetworkSessionActive() ? productionProgressNetwork.Value : localProductionProgress;
    }

    private float GetRawCombustionProgress()
    {
        return IsNetworkSessionActive() ? combustionProgressNetwork.Value : localCombustionProgress;
    }

    private void SetTemperatureAndPressure(float temperature, float pressure)
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                furnaceTemperatureNetwork.Value = temperature;
                furnacePressureNetwork.Value = pressure;
            }

            return;
        }

        localFurnaceTemperature = temperature;
        localFurnacePressure = pressure;
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetFuel(float fuel)
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                furnaceFuelNetwork.Value = Mathf.Max(0f, fuel);
            }

            return;
        }

        localFurnaceFuel = Mathf.Max(0f, fuel);
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetProductionProgress(float progress)
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                productionProgressNetwork.Value = progress;
            }

            return;
        }

        localProductionProgress = progress;
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetCombustionProgress(float progress)
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                combustionProgressNetwork.Value = progress;
            }

            return;
        }

        localCombustionProgress = progress;
        FurnaceStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
