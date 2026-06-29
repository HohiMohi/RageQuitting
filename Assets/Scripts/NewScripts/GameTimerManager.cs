using System;
using UnityEngine;

public class GameTimerManager : MonoBehaviour
{
    public static GameTimerManager Instance { get; private set; }

    [Header("Timer Settings")]
    [SerializeField] private float levelDuration = 300f; // Default 5 minutes
    private float timeRemaining;
    private bool isGameActive = false;
    private bool isGameOver = false;

    public event EventHandler OnTimerStarted;
    public event EventHandler<OnTimerChangedEventArgs> OnTimerChanged;
    public event EventHandler OnVictory;
    public event EventHandler OnDefeat;

    public class OnTimerChangedEventArgs : EventArgs
    {
        public float timeRemaining;
        public float normalizedTimeRemaining;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        timeRemaining = levelDuration;
        isGameActive = true;
        isGameOver = false;

        // Listen to GameplayManager's bridge assembly completion
        if (GameplayManager.Instance != null)
        {
            GameplayManager.Instance.OnBridgeFullyAssembled += GameplayManager_OnBridgeFullyAssembled;
        }

        OnTimerStarted?.Invoke(this, EventArgs.Empty);
    }

    private void Update()
    {
        if (!isGameActive || isGameOver) return;

        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0)
        {
            timeRemaining = 0;
            TriggerDefeat();
        }

        OnTimerChanged?.Invoke(this, new OnTimerChangedEventArgs
        {
            timeRemaining = timeRemaining,
            normalizedTimeRemaining = Mathf.Clamp01(timeRemaining / levelDuration)
        });
    }

    private void GameplayManager_OnBridgeFullyAssembled(object sender, EventArgs e)
    {
        TriggerVictory();
    }

    private void TriggerVictory()
    {
        if (isGameOver) return;
        isGameOver = true;
        isGameActive = false;
        Debug.Log("Goblin Victory! Bridge fully assembled before timer expired.");
        OnVictory?.Invoke(this, EventArgs.Empty);
    }

    private void TriggerDefeat()
    {
        if (isGameOver) return;
        isGameOver = true;
        isGameActive = false;
        Debug.Log("Goblin Defeat! Time ran out before bridge was assembled.");
        OnDefeat?.Invoke(this, EventArgs.Empty);
    }

    public float GetTimeRemaining() => timeRemaining;
    public float GetNormalizedTimeRemaining() => Mathf.Clamp01(timeRemaining / levelDuration);
    public bool IsGameOver() => isGameOver;
}
