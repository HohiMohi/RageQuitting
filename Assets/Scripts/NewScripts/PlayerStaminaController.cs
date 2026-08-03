using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerStaminaController : NetworkBehaviour
{
    private readonly NetworkVariable<float> currentStaminaNetwork = new NetworkVariable<float>(
        5f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<StaminaExhaustionReason> exhaustionReasonNetwork = new NetworkVariable<StaminaExhaustionReason>();
    private readonly Dictionary<StaminaDrainSource, float> serverDrainRates = new Dictionary<StaminaDrainSource, float>();
    private readonly Dictionary<StaminaDrainSource, float> requestedDrainRates = new Dictionary<StaminaDrainSource, float>();

    [SerializeField, Min(0.1f)] private float maximumStamina = 5f;
    [SerializeField, Min(0f)] private float regenerationDelay = 2f;
    [SerializeField, Min(0f)] private float regenerationPerSecond = 1f;

    private float currentStaminaLocal;
    private float regenerationBlockedUntil;

    public event EventHandler OnStaminaChanged;
    public event EventHandler OnExhaustionReasonChanged;

    public float CurrentStamina => IsNetworkStateActive ? currentStaminaNetwork.Value : currentStaminaLocal;
    public float MaximumStamina => Mathf.Max(0.1f, maximumStamina);
    public float NormalizedStamina => Mathf.Clamp01(CurrentStamina / MaximumStamina);
    public StaminaExhaustionReason CurrentExhaustionReason => IsNetworkStateActive
        ? exhaustionReasonNetwork.Value
        : ResolveExhaustionReason(currentStaminaLocal, serverDrainRates);

    private bool IsNetworkStateActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    private void Awake()
    {
        currentStaminaLocal = MaximumStamina;
    }

    public override void OnNetworkSpawn()
    {
        currentStaminaNetwork.OnValueChanged += HandleNetworkStaminaChanged;
        exhaustionReasonNetwork.OnValueChanged += HandleNetworkExhaustionChanged;
        if (IsServer)
        {
            currentStaminaNetwork.Value = MaximumStamina;
            exhaustionReasonNetwork.Value = StaminaExhaustionReason.None;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentStaminaNetwork.OnValueChanged -= HandleNetworkStaminaChanged;
        exhaustionReasonNetwork.OnValueChanged -= HandleNetworkExhaustionChanged;
        serverDrainRates.Clear();
        requestedDrainRates.Clear();
    }

    private void Update()
    {
        if (IsNetworkStateActive)
        {
            if (IsServer)
            {
                SimulateServer(Time.deltaTime);
            }
            return;
        }

        SimulateLocal(Time.deltaTime);
    }

    public void Configure(float maxStamina, float regenDelay)
    {
        maximumStamina = Mathf.Max(0.1f, maxStamina);
        regenerationDelay = Mathf.Max(0f, regenDelay);
        if (!IsNetworkStateActive)
        {
            currentStaminaLocal = Mathf.Clamp(currentStaminaLocal <= 0f ? maximumStamina : currentStaminaLocal, 0f, maximumStamina);
        }
    }

    public void SetDrainSource(StaminaDrainSource source, float ratePerSecond)
    {
        ratePerSecond = Mathf.Max(0f, ratePerSecond);
        if (IsNetworkStateActive && !IsOwner && !IsServer)
        {
            return;
        }

        if (requestedDrainRates.TryGetValue(source, out float previous) && Mathf.Approximately(previous, ratePerSecond))
        {
            return;
        }

        requestedDrainRates[source] = ratePerSecond;
        if (IsNetworkStateActive)
        {
            if (IsServer)
            {
                SetServerDrainRate(source, ratePerSecond);
            }
            else
            {
                SetDrainSourceServerRpc(source, ratePerSecond);
            }
        }
        else
        {
            SetServerDrainRate(source, ratePerSecond);
        }
    }

    public void ClearDrainSource(StaminaDrainSource source) => SetDrainSource(source, 0f);

    public void SetAuthoritativeDrainSource(StaminaDrainSource source, float ratePerSecond)
    {
        if (!IsNetworkStateActive || IsServer)
        {
            SetServerDrainRate(source, Mathf.Max(0f, ratePerSecond));
        }
    }

    public void RestoreFullStamina()
    {
        if (IsNetworkStateActive)
        {
            if (IsServer)
            {
                RestoreFullStaminaServer();
            }
            else if (IsOwner)
            {
                RestoreFullStaminaServerRpc();
            }
            return;
        }

        currentStaminaLocal = MaximumStamina;
        OnStaminaChanged?.Invoke(this, EventArgs.Empty);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SetDrainSourceServerRpc(StaminaDrainSource source, float ratePerSecond)
    {
        SetServerDrainRate(source, Mathf.Clamp(ratePerSecond, 0f, 20f));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RestoreFullStaminaServerRpc() => RestoreFullStaminaServer();

    private void RestoreFullStaminaServer()
    {
        currentStaminaNetwork.Value = MaximumStamina;
        exhaustionReasonNetwork.Value = StaminaExhaustionReason.None;
        regenerationBlockedUntil = 0f;
    }

    private void SetServerDrainRate(StaminaDrainSource source, float rate)
    {
        if (rate <= 0f)
        {
            serverDrainRates.Remove(source);
        }
        else
        {
            serverDrainRates[source] = rate;
            regenerationBlockedUntil = Time.time + regenerationDelay;
        }
    }

    private void SimulateServer(float deltaTime)
    {
        float value = SimulateValue(currentStaminaNetwork.Value, deltaTime);
        if (!Mathf.Approximately(value, currentStaminaNetwork.Value))
        {
            currentStaminaNetwork.Value = value;
        }

        exhaustionReasonNetwork.Value = ResolveExhaustionReason(value, serverDrainRates);
    }

    private void SimulateLocal(float deltaTime)
    {
        float previous = currentStaminaLocal;
        currentStaminaLocal = SimulateValue(currentStaminaLocal, deltaTime);
        if (!Mathf.Approximately(previous, currentStaminaLocal))
        {
            OnStaminaChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private float SimulateValue(float value, float deltaTime)
    {
        float totalDrain = 0f;
        foreach (float rate in serverDrainRates.Values)
        {
            totalDrain += rate;
        }

        if (totalDrain > 0f)
        {
            regenerationBlockedUntil = Time.time + regenerationDelay;
            return Mathf.Max(0f, value - totalDrain * deltaTime);
        }

        if (Time.time < regenerationBlockedUntil)
        {
            return value;
        }

        return Mathf.Min(MaximumStamina, value + regenerationPerSecond * deltaTime);
    }

    private static StaminaExhaustionReason ResolveExhaustionReason(
        float stamina,
        IReadOnlyDictionary<StaminaDrainSource, float> rates)
    {
        if (stamina > 0.001f)
        {
            return StaminaExhaustionReason.None;
        }

        if (rates.TryGetValue(StaminaDrainSource.Water, out float waterRate) && waterRate > 0f)
        {
            return StaminaExhaustionReason.Water;
        }

        return rates.TryGetValue(StaminaDrainSource.UnderstaffedSharedCarry, out float sharedRate) && sharedRate > 0f
            ? StaminaExhaustionReason.SharedCarry
            : StaminaExhaustionReason.None;
    }

    private void HandleNetworkStaminaChanged(float previous, float current) => OnStaminaChanged?.Invoke(this, EventArgs.Empty);
    private void HandleNetworkExhaustionChanged(StaminaExhaustionReason previous, StaminaExhaustionReason current) => OnExhaustionReasonChanged?.Invoke(this, EventArgs.Empty);
}
