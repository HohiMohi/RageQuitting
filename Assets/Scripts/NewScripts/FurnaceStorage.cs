using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

public class FurnaceStorage : BaseStorageNew
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField] private BlastFurnaceFactory blastFurnaceFactory;
    [SerializeField] private Bellows bellows;
    [SerializeField] private VentilationGrille ventilationGrille;
    [SerializeField] private Kindling kindling;
    [SerializeField] private FurnaceSwitch furnaceSwitch;
    [SerializeField] private BlastFurnaceMinigame minigame;
    private bool furnaceIsOn;
    private bool furnaceIsOnFire;
    private float furnaceTemperature;
    private float furnacePressure;
    private float furnaceReferencePressureHolder;
    private MountableBridgeComponentSO selectedMountableBridgeComponentSO;

    #region Furnace Production Parameters
    private float productionProgress;
    private float neededProgress;
    private float meltingPoint;
    private float combustionTemperature;
    private float combustionProgress;
    private float neededCombustionProgress;
    #endregion

    [Header("Furnace Parameters")]
    #region Tooltip
    [Tooltip("Furnace Pressure Temperature Multiplier describes how much bellows and ventilation grille interactions will affect furnace temperature.")]
    #endregion
    [SerializeField] private float furnacePressureTemperatureMultiplier;
    #region Tooltip
    [Tooltip("Furnace Pressure Change Speed determines how fast pressure variable will return to the initial value (0) - in seconds.")]
    #endregion
    [SerializeField] private float furnacePressureChangeSpeed;
    #region Tooltip
    [Tooltip("Furnace Fuel Normalized Temperature describes describes the temperature, at which the furnace will use up one fuel base in 60 seconds.")]
    #endregion
    [SerializeField] private float furnaceFuelNormalizedTemperature;
    #region Tooltip
    [Tooltip("Furnace Heating Rate describes how fast (in 1 sec) temperature raises without player's interference. There has to be fuel in furnace. ")]
    #endregion
    [SerializeField] private float furnaceHeatingRate;
    #region Tooltip
    [Tooltip("Furnace Max Temperature describes maximum temperature, to which temperature will raise without Player's interference.")]
    #endregion
    [SerializeField] private float furnaceMaxTemperature;
    #region Tooltip
    [Tooltip("Furnace Overheat Temperature Decay Rate describes how fast temperature will dropping (per sec) to furnaceMaxTemperature, if pressure is <= 0.")]
    #endregion
    [SerializeField] private float furnaceOverheatTemperatureDecayRate;
    [SerializeField] private float furnaceFuel;
    [SerializeField] private bool isFuelAvailable;

    #region Events
    public EventHandler ProductionStarted;
    public EventHandler ProductionFinished;
    public EventHandler<TryEndProductionEventArgs> TryEndProduction;

    public class TryEndProductionEventArgs : EventArgs
    {
        public Transform interactor;
    }

    #endregion
    private void Awake()
    {
        furnaceIsOn = false;
        furnaceIsOnFire = false;
        isFuelAvailable = false;
        furnacePressure = 0;
        furnaceTemperature = 30;
        storedBaseResourceDictionary = new Dictionary<BaseResourceSO, int>();
        foreach (BaseResourceSO baseResourceSO in storableBaseResourcesSOList)
        {
            storedBaseResourceDictionary.Add(baseResourceSO, 0);
        }
    }
    void Start()
    {
        blastFurnaceFactory.BridgeComponentSelectionConfirm += BlastFurnaceFactory_OnBridgeComponentSelectionConfirm;
        bellows.BellowsPressed += Bellows_OnBellowsPressed;
        ventilationGrille.VentilationGrilleClosed += VentilationGrille_OnVentilationGrilleClosed;
        kindling.SetFurnaceOnFire += Kindling_OnFurnaceSettedOnFire;
        furnaceSwitch.FurnaceSwitchPressed += FurnaceSwitch_OnFurnaceSwitchPressed;
        minigame.MinigameCompletedEvent += BlastFurnaceMinigame_OnMinigameCompleted;
        productionProgress = 0;
        combustionProgress = 0;
    }

    private void BlastFurnaceMinigame_OnMinigameCompleted(object sender, EventArgs e)
    {
        Debug.Log("Minigame completed succesfully.");
        ProductionFinished?.Invoke(this, EventArgs.Empty);
        Debug.Log("Production finished. Furnace has been stopped");
    }

    private void BlastFurnaceFactory_OnBridgeComponentSelectionConfirm(object sender, BlastFurnaceFactory.BridgeComponentSelectionConfirmEventArgs e)
    {
        
        furnaceIsOn = false;
        productionProgress = 0;
        combustionProgress = 0;
        if (e.mountableBridgeComponentSO != null)
        {
            neededProgress = e.mountableBridgeComponentSO.neededProgress;
            meltingPoint = e.mountableBridgeComponentSO.meltingPoint;
            combustionTemperature = e.mountableBridgeComponentSO.combustionTemperature;
            neededCombustionProgress = e.mountableBridgeComponentSO.neededCombustionProgress;
            selectedMountableBridgeComponentSO = e.mountableBridgeComponentSO;
        }
    }

    private void VentilationGrille_OnVentilationGrilleClosed(object sender, EventArgs e)
    {
        if (furnaceIsOn)
        {
            furnacePressure -= 1;
            furnaceReferencePressureHolder = furnacePressure;
        }
    }

    private void Bellows_OnBellowsPressed(object sender, EventArgs e)
    {
        if (furnaceIsOn)
        {
            furnacePressure += 1;
            furnaceReferencePressureHolder = furnacePressure;
        }
    }

    private void FurnaceSwitch_OnFurnaceSwitchPressed(object sender, FurnaceSwitch.FurnaceSwitchPressedEventArgs e)
    {
        if (furnaceIsOn)
        {
            furnaceIsOn = false;
            if (GetProductionProgressNormalized() == 1)
            {
                TryEndProduction?.Invoke(this, new TryEndProductionEventArgs
                {
                    interactor = e.interactor
                });
            }

        }
        else if (!furnaceIsOn && neededProgress != 0)
        {
            if (blastFurnaceFactory.CheckRequiredBaseResources(selectedMountableBridgeComponentSO))
            {
                furnaceIsOn = true;
                ProductionStarted?.Invoke(this, EventArgs.Empty);
                Debug.Log("Furnace turned on. Starting production");
            }
            else
            {
                Debug.Log("Missing Base Resources in Base Storage. Cannot start production");
            }

        }

    }

    private void Kindling_OnFurnaceSettedOnFire(object sender, EventArgs e)
    {
        if (furnaceIsOn)
        {
            furnaceIsOnFire = true;
            Debug.Log("Furnace is now on fire.");

        }
        else
        {
            Debug.Log("Furnace is off");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void FixedUpdate()
    {
        if (furnaceIsOnFire)
        {
            HandleFurnaceTemperature();
            HandleFurnaceFuelUsage();
            HandleProductionProgress();
            HandleCombustionProgress();
        }
    }
    public void HandleFurnaceTemperature()
    {
        if (furnaceFuel > 0)
        {
            if (furnacePressure != 0)
            {
                furnaceTemperature += furnacePressureTemperatureMultiplier * furnacePressure * Time.deltaTime;
            }
            if (furnaceTemperature < furnaceMaxTemperature)
            {
                furnaceTemperature += furnaceHeatingRate * Time.deltaTime;
            }
        }
        else if (furnaceFuel <= 0 && furnaceTemperature > 30)
        {
            furnaceTemperature -= Time.deltaTime;
        }
        // Furnace is overheated and there is no positive(value) pressure -> cool down furnace
        if (furnacePressure <= 0 && furnaceTemperature > furnaceMaxTemperature)
        {
            furnaceTemperature -= Time.deltaTime * furnaceOverheatTemperatureDecayRate;
            //Clamp furnace temperature, to avoid temperature fluctuations aroud furnaceMaxTemperature
            furnaceTemperature = Math.Clamp(furnaceTemperature, furnaceMaxTemperature, 3000);
        }
        furnaceTemperature = math.clamp(furnaceTemperature, 30, 3000);
        //Debug.Log($"Current Temperature is: {furnaceTemperature}");
        if (math.abs(furnacePressure) > 0.1)
        {
            furnacePressure -= Time.deltaTime * furnaceReferencePressureHolder / furnacePressureChangeSpeed;
        }
        else
        {
            furnacePressure = 0;
        }
    }
    // To change for handling fuel from furnace storage
    private void HandleFurnaceFuelUsage()
    {
        if(furnaceFuel < 1 && isFuelAvailable && furnaceIsOn)
        {
            TryRefuelFurnace();
        }
        if (furnaceFuel > 0 && furnaceTemperature > 30)
        {
            float fuelUsage = (furnaceTemperature / furnaceFuelNormalizedTemperature) * Time.deltaTime / 60;
            furnaceFuel -= fuelUsage;
            //Debug.Log($"Current fuel usage: {fuelUsage}");
        }
        if (furnaceFuel <= 0 && furnaceTemperature <= 30 && furnaceIsOnFire)
        {
            furnaceIsOnFire = false;
            furnaceIsOn = false;
        }
    }

    private void HandleProductionProgress()
    {
        if (furnaceIsOn) { 
        float temperatureDelta = furnaceTemperature - meltingPoint;
            if ((temperatureDelta > 0 && productionProgress < neededProgress) || (temperatureDelta < 0 && productionProgress > 0 && combustionProgress == 0))
            {
                productionProgress += temperatureDelta * Time.deltaTime;
                productionProgress = math.clamp(productionProgress, 0, neededProgress);
            }
        }
    }

    private void HandleCombustionProgress()
    {
        if (furnaceIsOn) {
        float temperatureDelta = furnaceTemperature - combustionTemperature;
            //Debug.Log(temperatureDelta);
            if ((temperatureDelta > 0 && combustionProgress < neededCombustionProgress && neededProgress == productionProgress) || (temperatureDelta < 0 && combustionProgress > 0))
            {
                combustionProgress += temperatureDelta * Time.deltaTime;
                combustionProgress = math.clamp(combustionProgress, 0, neededCombustionProgress);
                if (combustionProgress == neededCombustionProgress)
                {
                    Debug.Log("Spalone");
                    // Handle there production failed event
                    //ProductionFinished?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
    private void TryRefuelFurnace()
    {
        bool fuelAdded = false;
        foreach (KeyValuePair<BaseResourceSO, int> keyValuePair in storedBaseResourceDictionary)
        {
            if (keyValuePair.Value > 0)
            {
                furnaceFuel += keyValuePair.Key.furnaceFuelAmount;
                fuelAdded = true;
                storedBaseResourceDictionary[keyValuePair.Key] -= 1;
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
        if(neededProgress != 0)
            return productionProgress / neededProgress;
        else return 0;
    }

    public float GetCombustionProgressNormalized()
    {
        if (neededCombustionProgress != 0)
            return combustionProgress / neededCombustionProgress;
        else return 0;
    }
}
