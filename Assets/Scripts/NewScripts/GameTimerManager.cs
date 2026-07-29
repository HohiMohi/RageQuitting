using System;
using Unity.Netcode;
using UnityEngine;

public enum GameTimerState
{
    Waiting,
    Running,
    Victory,
    Defeat
}

public class GameTimerManager : NetworkBehaviour
{
    public static GameTimerManager Instance { get; private set; }

    [Header("Timer Settings")]
    [SerializeField] private float levelDuration = 300f;
    [SerializeField] private bool waitForStartSignal;

    private readonly NetworkVariable<GameTimerState> timerStateNetwork = new NetworkVariable<GameTimerState>(
        GameTimerState.Waiting,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> timerEndTimeNetwork = new NetworkVariable<double>(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> stoppedTimeRemainingNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private GameTimerState localState = GameTimerState.Waiting;
    private double localEndTime;
    private float localStoppedTimeRemaining;
    private GameplayManager subscribedGameplayManager;

    public event EventHandler OnTimerStarted;
    public event EventHandler<OnTimerChangedEventArgs> OnTimerChanged;
    public event EventHandler<GameTimerStateChangedEventArgs> OnTimerStateChanged;
    public event EventHandler OnVictory;
    public event EventHandler OnDefeat;

    public class OnTimerChangedEventArgs : EventArgs
    {
        public float timeRemaining;
        public float normalizedTimeRemaining;
    }

    public class GameTimerStateChangedEventArgs : EventArgs
    {
        public GameTimerState PreviousState;
        public GameTimerState CurrentState;
    }

    public GameTimerState State => IsNetworkStateActive ? timerStateNetwork.Value : localState;
    public bool IsWaiting => State == GameTimerState.Waiting;
    public bool IsRunning => State == GameTimerState.Running;
    public bool CanStartTimer => IsWaiting && (!IsNetworkStateActive || IsServer);
    public float ElapsedRunningTime => IsRunning
        ? Mathf.Max(0f, levelDuration - GetTimeRemaining())
        : 0f;

    private bool IsNetworkStateActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("GameTimerManager: Duplicate instance detected. Disabling this instance.");
            enabled = false;
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        TrySubscribeGameplayManager();
        if (!IsNetworkStateActive)
        {
            InitializeLocalTimer();
        }
    }

    public override void OnNetworkSpawn()
    {
        timerStateNetwork.OnValueChanged += TimerStateNetwork_OnValueChanged;
        timerEndTimeNetwork.OnValueChanged += TimerEndTimeNetwork_OnValueChanged;

        if (IsServer)
        {
            timerEndTimeNetwork.Value = 0d;
            stoppedTimeRemainingNetwork.Value = levelDuration;
            timerStateNetwork.Value = GameTimerState.Waiting;
            if (!waitForStartSignal)
            {
                TryStartTimer();
            }
        }

        NotifyTimerStateChanged(State, State);
        NotifyTimerChanged();
    }

    public override void OnNetworkDespawn()
    {
        timerStateNetwork.OnValueChanged -= TimerStateNetwork_OnValueChanged;
        timerEndTimeNetwork.OnValueChanged -= TimerEndTimeNetwork_OnValueChanged;
    }

    private void Update()
    {
        TrySubscribeGameplayManager();
        if (!IsRunning)
        {
            return;
        }

        if ((!IsNetworkStateActive || IsServer) && GetTimeRemaining() <= 0f)
        {
            TriggerDefeat();
        }

        NotifyTimerChanged();
    }

    public bool TryStartTimer()
    {
        if (!CanStartTimer)
        {
            return false;
        }

        double endTime = GetCurrentTime() + Mathf.Max(0f, levelDuration);
        if (IsNetworkStateActive)
        {
            timerEndTimeNetwork.Value = endTime;
            timerStateNetwork.Value = GameTimerState.Running;
        }
        else
        {
            localEndTime = endTime;
            SetLocalState(GameTimerState.Running);
        }

        NotifyTimerChanged();
        return true;
    }

    private void InitializeLocalTimer()
    {
        localEndTime = 0d;
        localStoppedTimeRemaining = levelDuration;
        localState = GameTimerState.Waiting;
        if (!waitForStartSignal)
        {
            TryStartTimer();
        }
        else
        {
            NotifyTimerStateChanged(localState, localState);
            NotifyTimerChanged();
        }
    }

    private void GameplayManager_OnBridgeFullyAssembled(object sender, EventArgs e)
    {
        if (IsNetworkStateActive && !IsServer)
        {
            return;
        }

        TriggerVictory();
    }

    private void OnDestroy()
    {
        if (subscribedGameplayManager != null)
        {
            subscribedGameplayManager.OnBridgeFullyAssembled -= GameplayManager_OnBridgeFullyAssembled;
            subscribedGameplayManager = null;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void TrySubscribeGameplayManager()
    {
        if (subscribedGameplayManager != null || GameplayManager.Instance == null)
        {
            return;
        }

        subscribedGameplayManager = GameplayManager.Instance;
        subscribedGameplayManager.OnBridgeFullyAssembled += GameplayManager_OnBridgeFullyAssembled;
    }

    private void TriggerVictory()
    {
        if (State == GameTimerState.Victory || State == GameTimerState.Defeat)
        {
            return;
        }

        SetAuthoritativeState(GameTimerState.Victory);
    }

    private void TriggerDefeat()
    {
        if (!IsRunning)
        {
            return;
        }

        SetAuthoritativeState(GameTimerState.Defeat);
    }

    private void SetAuthoritativeState(GameTimerState state)
    {
        if (IsNetworkStateActive)
        {
            if (!IsServer)
            {
                return;
            }

            stoppedTimeRemainingNetwork.Value = GetStoppedTimeRemaining(state);
            timerStateNetwork.Value = state;
        }
        else
        {
            localStoppedTimeRemaining = GetStoppedTimeRemaining(state);
            SetLocalState(state);
        }

        NotifyTimerChanged();
    }

    private float GetStoppedTimeRemaining(GameTimerState state)
    {
        if (state == GameTimerState.Defeat)
        {
            return 0f;
        }

        return IsRunning ? GetRunningTimeRemaining() : Mathf.Max(0f, levelDuration);
    }

    private void SetLocalState(GameTimerState newState)
    {
        GameTimerState previousState = localState;
        localState = newState;
        NotifyTimerStateChanged(previousState, newState);
    }

    private void TimerStateNetwork_OnValueChanged(GameTimerState previousState, GameTimerState currentState)
    {
        NotifyTimerStateChanged(previousState, currentState);
        NotifyTimerChanged();
    }

    private void TimerEndTimeNetwork_OnValueChanged(double previousValue, double currentValue)
    {
        NotifyTimerChanged();
    }

    private void NotifyTimerStateChanged(GameTimerState previousState, GameTimerState currentState)
    {
        OnTimerStateChanged?.Invoke(this, new GameTimerStateChangedEventArgs
        {
            PreviousState = previousState,
            CurrentState = currentState
        });

        if (previousState == currentState)
        {
            return;
        }

        switch (currentState)
        {
            case GameTimerState.Running:
                OnTimerStarted?.Invoke(this, EventArgs.Empty);
                break;
            case GameTimerState.Victory:
                Debug.Log("Goblin Victory! Bridge fully assembled before timer expired.");
                OnVictory?.Invoke(this, EventArgs.Empty);
                break;
            case GameTimerState.Defeat:
                Debug.Log("Goblin Defeat! Time ran out before bridge was assembled.");
                OnDefeat?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void NotifyTimerChanged()
    {
        float timeRemaining = GetTimeRemaining();
        OnTimerChanged?.Invoke(this, new OnTimerChangedEventArgs
        {
            timeRemaining = timeRemaining,
            normalizedTimeRemaining = GetNormalizedTimeRemaining()
        });
    }

    private double GetCurrentTime()
    {
        if (IsNetworkStateActive)
        {
            return NetworkManager.ServerTime.Time;
        }

        return Time.timeAsDouble;
    }

    public float GetTimeRemaining()
    {
        if (IsWaiting)
        {
            return Mathf.Max(0f, levelDuration);
        }

        if (State == GameTimerState.Defeat)
        {
            return 0f;
        }
        if (State == GameTimerState.Victory)
        {
            return IsNetworkStateActive ? stoppedTimeRemainingNetwork.Value : localStoppedTimeRemaining;
        }

        return GetRunningTimeRemaining();
    }

    private float GetRunningTimeRemaining()
    {
        double endTime = IsNetworkStateActive ? timerEndTimeNetwork.Value : localEndTime;
        return Mathf.Max(0f, (float)(endTime - GetCurrentTime()));
    }

    public float GetNormalizedTimeRemaining()
    {
        return levelDuration > 0f ? Mathf.Clamp01(GetTimeRemaining() / levelDuration) : 0f;
    }

    public bool IsGameOver()
    {
        return State == GameTimerState.Victory || State == GameTimerState.Defeat;
    }
}
