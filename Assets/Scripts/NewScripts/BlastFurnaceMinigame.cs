using System;
using Unity.Mathematics;
using UnityEngine;

public class BlastFurnaceMinigame : Minigame
{
    [SerializeField] private ProductionMinigameUI productionMinigameUI;
    [SerializeField] private FurnaceStorage furnaceStorage;
    private float playerValuePositionUpperBound;
    private float playerValuePositionLowerBound;
    private float currentRequiredValuePositionUpperBound;
    private float currentRequiredValuePositionLowerBound;
    private float perfectValuePositionLowerBound;
    private float perfectValuePositionUpperBound;
    private float criticalFailureValuePositionLowerBound;
    private float criticalFailureValuePositionUpperBound;
    [Header("Minigame UI properties")]
    [SerializeField] private float minigamePanelHeight;
    [SerializeField] private float requiredValueObjectHeight;
    [SerializeField] private float playerValueObjectHeight;
    [SerializeField] private float perfectValueObjectHeight;
    [SerializeField] private float criticalFailureObjectHeight;
    [Header("Minigame options")]
    [SerializeField] private float minigameCompleteTime;
    [SerializeField] private float perfectValueProgressMultiplier;
    [SerializeField] private float minigameFailureTime;
    [SerializeField] private float minigameCriticalFailureTime;

    private float failureTimer;
    private float criticalFailureTimer;
    private float minigameCalculateValueStep;
    private float playerValue;
    private float currentRequiredValue;
    private float currentPerfectValue;
    private float currentCriticalFailureValue;
    private Transform interactor;
    private PlayerInputNew playerInputNew;
    private bool isGameOn;

    public EventHandler MinigameCompletedEvent;
    public EventHandler MinigameFailedEvent;
    public EventHandler MinigameCriticallyFailedEvent;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        furnaceStorage.TryEndProduction += FurnaceStorage_OnTryEndProduction;
        isGameOn = false;
        CalculateObjectsBounds();
        minigameCalculateValueStep = Mathf.Abs(currentRequiredValuePositionLowerBound - currentRequiredValuePositionUpperBound) / minigameCompleteTime;

    }

    private void FurnaceStorage_OnTryEndProduction(object sender, FurnaceStorage.TryEndProductionEventArgs e)
    {
        interactor = e.interactor;
        playerInputNew = interactor.GetComponent<PlayerInputNew>();
        if (playerInputNew != null)
        {
        }
        StartMinigame();
    }



    // Update is called once per frame
    void Update()
    {
        if (isGameOn)
        {
            UpdateMinigame();
        }
    }

    public override bool IsActive() => isGameOn;

    public override void StartMinigame()
    {
        currentRequiredValue = currentRequiredValuePositionUpperBound;
        playerValue = playerValuePositionUpperBound;
        StartMinigameEvent?.Invoke(this, EventArgs.Empty);
        isGameOn = true;
        failureTimer = 0;
        criticalFailureTimer = 0;
    }

    public override void StopMinigame()
    {
        throw new NotImplementedException();
    }

    public override void MinigameFailed()
    {
        Debug.Log("Minigame failed");
        // invoke event - minigame failed
        MinigameFailedEvent?.Invoke(this, EventArgs.Empty);
        EndMinigameEvent?.Invoke(this, EventArgs.Empty);
        isGameOn = false;

    }

    public override void MinigameCompleted()
    {
        // invoke event - minigame completed
        Debug.Log("Minigame completed");
        MinigameCompletedEvent?.Invoke(this, EventArgs.Empty);
        EndMinigameEvent?.Invoke(this, EventArgs.Empty );
        isGameOn = false;


    }
    public override void UpdateMinigame()
    {
        UpdatePlayerValue();
        CalculateCurrentValues();
        CheckMinigameProgress();
    }

    private void UpdatePlayerValue()
    {
        float yDelta = playerInputNew.GetLookDeltaValueForMinigames().y;
        playerValue += yDelta;
        playerValue = Mathf.Clamp(playerValue, playerValuePositionLowerBound, playerValuePositionUpperBound);
    }
    private void CalculateCurrentValues()
    {
        float normalizedProgress = CalculateNormalizedProgress();
        if (CalculateValueDifference() < requiredValueObjectHeight/2)
        {
            currentRequiredValue = Mathf.Clamp(currentRequiredValue - (Time.deltaTime * (minigameCalculateValueStep * (0.5f + normalizedProgress))), currentRequiredValuePositionLowerBound, currentRequiredValuePositionUpperBound);
        } 
        else if(CalculatePerfectValueDifference() < perfectValueObjectHeight / 2)
        {
            currentRequiredValue = Mathf.Clamp(currentRequiredValue - (Time.deltaTime * (minigameCalculateValueStep * (0.5f + normalizedProgress)) * perfectValueProgressMultiplier), currentRequiredValuePositionLowerBound, currentRequiredValuePositionUpperBound);
        }
        currentPerfectValue = Mathf.Clamp(currentRequiredValue - (requiredValueObjectHeight / 2 + perfectValueObjectHeight / 2), perfectValuePositionLowerBound, perfectValuePositionUpperBound);
        currentCriticalFailureValue = Mathf.Clamp(currentPerfectValue - (perfectValueObjectHeight / 2 + criticalFailureObjectHeight / 2), criticalFailureValuePositionLowerBound, criticalFailureValuePositionUpperBound);

    }



    private void CheckMinigameProgress()
    {
        if (CalculateValueDifference() < requiredValueObjectHeight / 2)
        {

        }
        else if (CalculateCriticalFailureDifference() < criticalFailureObjectHeight / 2 && currentCriticalFailureValue != criticalFailureValuePositionLowerBound)
        {
            criticalFailureTimer += Time.deltaTime;
            if (criticalFailureTimer > minigameCriticalFailureTime)
            {
                Debug.Log("Critical Failure");
                // invoke critical failure event
                MinigameFailed(); //temp
            } 
        }
        else
        {
            failureTimer += Time.deltaTime;
        }
        if (failureTimer > minigameFailureTime)
        {
            MinigameFailed();
        }
        else if (CalculateNormalizedProgress() == 1)
        {
            MinigameCompleted();
        }
    }

    private float CalculateNormalizedProgress()
    {
        return Mathf.Abs(currentRequiredValue /currentRequiredValuePositionLowerBound);
    }

    public override float GetPlayerValue()
    {
        return playerValue;
    }

    public override float GetCurrentRequiredValue()
    {
        return currentRequiredValue;
    }

    public override float GetCurrentPerfectValue()
    {
        return currentPerfectValue;
    }

    public override float GetCurrentCriticalFailureValue()
    {
        return currentCriticalFailureValue;
    }

    public override float CalculateValueDifference()
    {
        return Mathf.Abs(currentRequiredValue - playerValue);
    }

    public override float CalculatePerfectValueDifference()
    {
        return Mathf.Abs(currentPerfectValue - playerValue);
    }

    private float CalculateCriticalFailureDifference()
    {
        return Mathf.Abs (currentCriticalFailureValue - playerValue);
    }

    private void CalculateObjectsBounds()
    {
        playerValuePositionUpperBound = -playerValueObjectHeight / 2;
        playerValuePositionLowerBound = -(minigamePanelHeight - playerValueObjectHeight / 2);
        currentRequiredValuePositionUpperBound = -requiredValueObjectHeight / 2;
        currentRequiredValuePositionLowerBound = -(minigamePanelHeight - requiredValueObjectHeight / 2);
        perfectValuePositionUpperBound = -perfectValueObjectHeight / 2;
        perfectValuePositionLowerBound = -(minigamePanelHeight - perfectValueObjectHeight / 2);
        criticalFailureValuePositionUpperBound = -criticalFailureObjectHeight / 2;
        criticalFailureValuePositionLowerBound = -(minigamePanelHeight - criticalFailureObjectHeight / 2);
    }
}


