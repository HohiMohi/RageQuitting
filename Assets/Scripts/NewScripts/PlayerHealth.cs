using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float healthRegenerationPerSecond = 5f;
    [SerializeField] private float regenerationDelayAfterDamage = 5f;

    private readonly NetworkVariable<float> currentHealthNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isDownedNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float currentHealthLocal;
    private bool isDownedLocal;
    private float lastDamageTime = float.NegativeInfinity;
    private PlayerInteractionNew playerInteraction;

    public event EventHandler OnHealthChanged;
    public event EventHandler OnDownedStateChanged;

    public float CurrentHealth => IsNetworkStateActive ? currentHealthNetwork.Value : currentHealthLocal;
    public float MaxHealth => maxHealth;
    public bool IsDowned => IsNetworkStateActive ? isDownedNetwork.Value : isDownedLocal;

    private bool IsNetworkStateActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    private void Awake()
    {
        playerInteraction = GetComponent<PlayerInteractionNew>();
        currentHealthLocal = maxHealth;
    }

    public override void OnNetworkSpawn()
    {
        currentHealthNetwork.OnValueChanged += CurrentHealthNetwork_OnValueChanged;
        isDownedNetwork.OnValueChanged += IsDownedNetwork_OnValueChanged;

        if (IsServer)
        {
            currentHealthNetwork.Value = maxHealth;
            isDownedNetwork.Value = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentHealthNetwork.OnValueChanged -= CurrentHealthNetwork_OnValueChanged;
        isDownedNetwork.OnValueChanged -= IsDownedNetwork_OnValueChanged;
    }

    private void Update()
    {
        if (IsDowned)
        {
            return;
        }

        if (IsNetworkStateActive)
        {
            if (IsServer)
            {
                RegenerateNetworkHealth();
            }
        }
        else
        {
            RegenerateLocalHealth();
        }
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        DamageReceived(damage);
    }

    public void DamageReceived(float damage)
    {
        DamageReceived(damage, null);
    }

    public void DamageReceived(float damage, NetworkObject attackerNetworkObject)
    {
        if (damage <= 0f)
        {
            return;
        }

        if (IsNetworkStateActive)
        {
            if (IsServer)
            {
                ApplyDamageNetwork(damage);
            }
            else
            {
                ulong attackerNetworkObjectId = attackerNetworkObject != null ? attackerNetworkObject.NetworkObjectId : 0;
                RequestDamageRpc(damage, attackerNetworkObjectId);
            }
        }
        else
        {
            ApplyDamageLocal(damage);
        }
    }

    public float GetHealthNormalized()
    {
        if (maxHealth <= 0f)
        {
            return 0f;
        }

        return Mathf.Clamp01(CurrentHealth / maxHealth);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestDamageRpc(float damage, ulong attackerNetworkObjectId)
    {
        if (NetworkObject != null && attackerNetworkObjectId == NetworkObject.NetworkObjectId)
        {
            return;
        }

        ApplyDamageNetwork(damage);
    }

    private void ApplyDamageNetwork(float damage)
    {
        if (damage <= 0f || isDownedNetwork.Value)
        {
            return;
        }

        lastDamageTime = Time.time;
        float newHealth = Mathf.Clamp(currentHealthNetwork.Value - damage, 0f, maxHealth);
        currentHealthNetwork.Value = newHealth;
        OnHealthChanged?.Invoke(this, EventArgs.Empty);

        if (newHealth <= 0f)
        {
            SetDownedNetwork(true);
        }
    }

    private void ApplyDamageLocal(float damage)
    {
        if (damage <= 0f || isDownedLocal)
        {
            return;
        }

        lastDamageTime = Time.time;
        currentHealthLocal = Mathf.Clamp(currentHealthLocal - damage, 0f, maxHealth);
        OnHealthChanged?.Invoke(this, EventArgs.Empty);

        if (currentHealthLocal <= 0f)
        {
            SetDownedLocal(true);
        }
    }

    private void RegenerateNetworkHealth()
    {
        if (currentHealthNetwork.Value >= maxHealth || Time.time < lastDamageTime + regenerationDelayAfterDamage)
        {
            return;
        }

        currentHealthNetwork.Value = Mathf.Min(maxHealth, currentHealthNetwork.Value + healthRegenerationPerSecond * Time.deltaTime);
        OnHealthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RegenerateLocalHealth()
    {
        if (currentHealthLocal >= maxHealth || Time.time < lastDamageTime + regenerationDelayAfterDamage)
        {
            return;
        }

        currentHealthLocal = Mathf.Min(maxHealth, currentHealthLocal + healthRegenerationPerSecond * Time.deltaTime);
        OnHealthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetDownedNetwork(bool isDowned)
    {
        if (isDownedNetwork.Value == isDowned)
        {
            return;
        }

        isDownedNetwork.Value = isDowned;
        if (isDowned)
        {
            currentHealthNetwork.Value = 0f;
        }

        OnDownedStateChanged?.Invoke(this, EventArgs.Empty);
        OnHealthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetDownedLocal(bool isDowned)
    {
        if (isDownedLocal == isDowned)
        {
            return;
        }

        isDownedLocal = isDowned;
        if (isDownedLocal)
        {
            currentHealthLocal = 0f;
            DropHeldObjectBecauseDowned();
        }

        OnDownedStateChanged?.Invoke(this, EventArgs.Empty);
        OnHealthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CurrentHealthNetwork_OnValueChanged(float previousValue, float newValue)
    {
        OnHealthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void IsDownedNetwork_OnValueChanged(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            DropHeldObjectBecauseDowned();
        }

        OnDownedStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DropHeldObjectBecauseDowned()
    {
        if (playerInteraction == null)
        {
            return;
        }

        if (!IsNetworkStateActive || IsOwner)
        {
            playerInteraction.DropHeldObjectForStateChange();
        }
    }
}
