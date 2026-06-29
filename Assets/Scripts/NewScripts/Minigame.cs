using System;
using UnityEngine;

public abstract class Minigame : MonoBehaviour
{
    public abstract void StartMinigame();
    public abstract void StopMinigame();

    public abstract void MinigameFailed();

    public abstract void MinigameCompleted();

    public abstract void UpdateMinigame();
    public abstract bool IsActive();
    public abstract float GetPlayerValue();
    public abstract float GetCurrentRequiredValue();
    public abstract float GetCurrentPerfectValue();
    public abstract float GetCurrentCriticalFailureValue();
    public abstract float CalculateValueDifference();
    public abstract float CalculatePerfectValueDifference();

    public EventHandler StartMinigameEvent;
    public EventHandler EndMinigameEvent;
}
