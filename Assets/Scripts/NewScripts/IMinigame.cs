using UnityEngine;

public interface IMinigame
{
    public void StartMinigame();
    public void StopMinigame();

    public void MinigameFailed();

    public void MinigameCompleted();

    public void UpdateMinigame();
}
