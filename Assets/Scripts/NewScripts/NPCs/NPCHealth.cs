using System;
using Unity.Netcode;
using UnityEngine;

public class NPCHealth : NetworkBehaviour, IDamageable, INPCTarget
{
    public class DamageEventArgs : EventArgs
    {
        public DamageEventArgs(float previousHealth, float currentHealth, NetworkObject attacker)
        {
            PreviousHealth = previousHealth;
            CurrentHealth = currentHealth;
            Attacker = attacker;
        }

        public float PreviousHealth { get; }
        public float CurrentHealth { get; }
        public NetworkObject Attacker { get; }
    }

    [SerializeField] private float maxHealth = 50f;

    private readonly NetworkVariable<float> currentHealthNetwork = new NetworkVariable<float>(
        50f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float currentHealthLocal;
    private NPCFactionMember factionMember;

    public event EventHandler OnHealthChanged;
    public event EventHandler<DamageEventArgs> OnDamaged;
    public event EventHandler OnDeath;

    public float CurrentHealth => IsNetworkSessionActive() ? currentHealthNetwork.Value : currentHealthLocal;
    public float MaxHealth => maxHealth;
    public bool IsDead => CurrentHealth <= 0f;
    public Transform TargetTransform => transform;
    public NPCFactionSO Faction => factionMember != null ? factionMember.Faction : null;
    public bool IsTargetAvailable => !IsDead;

    private void Awake()
    {
        factionMember = GetComponent<NPCFactionMember>();
        currentHealthLocal = maxHealth;
    }

    public override void OnNetworkSpawn()
    {
        currentHealthNetwork.OnValueChanged += CurrentHealthNetwork_OnValueChanged;
        if (IsServer)
        {
            currentHealthNetwork.Value = maxHealth;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentHealthNetwork.OnValueChanged -= CurrentHealthNetwork_OnValueChanged;
    }

    public void Configure(float configuredMaxHealth)
    {
        maxHealth = Mathf.Max(1f, configuredMaxHealth);
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                currentHealthNetwork.Value = maxHealth;
            }
        }
        else
        {
            currentHealthLocal = maxHealth;
        }

        OnHealthChanged?.Invoke(this, EventArgs.Empty);
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
        if (damage <= 0f || IsDead)
        {
            return;
        }

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                ApplyNetworkDamage(damage, attackerNetworkObject);
            }
            else
            {
                ulong attackerNetworkObjectId = attackerNetworkObject != null ? attackerNetworkObject.NetworkObjectId : 0;
                RequestDamageServerRpc(damage, attackerNetworkObjectId);
            }

            return;
        }

        ApplyLocalDamage(damage, attackerNetworkObject);
    }

    public float GetHealthNormalized()
    {
        return Mathf.Clamp01(CurrentHealth / maxHealth);
    }

    private void ApplyNetworkDamage(float damage, NetworkObject attackerNetworkObject)
    {
        float previousHealth = currentHealthNetwork.Value;
        currentHealthNetwork.Value = Mathf.Clamp(currentHealthNetwork.Value - damage, 0f, maxHealth);
        OnHealthChanged?.Invoke(this, EventArgs.Empty);
        OnDamaged?.Invoke(this, new DamageEventArgs(previousHealth, currentHealthNetwork.Value, attackerNetworkObject));
        NPCFactionDamageAlertSystem.Publish(this, attackerNetworkObject);

        if (currentHealthNetwork.Value <= 0f)
        {
            HandleDeath();
        }
    }

    private void ApplyLocalDamage(float damage, NetworkObject attackerNetworkObject)
    {
        float previousHealth = currentHealthLocal;
        currentHealthLocal = Mathf.Clamp(currentHealthLocal - damage, 0f, maxHealth);
        OnHealthChanged?.Invoke(this, EventArgs.Empty);
        OnDamaged?.Invoke(this, new DamageEventArgs(previousHealth, currentHealthLocal, attackerNetworkObject));
        NPCFactionDamageAlertSystem.Publish(this, attackerNetworkObject);

        if (currentHealthLocal <= 0f)
        {
            HandleDeath();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestDamageServerRpc(float damage, ulong attackerNetworkObjectId)
    {
        ApplyNetworkDamage(damage, TryGetNetworkObject(attackerNetworkObjectId));
    }

    private NetworkObject TryGetNetworkObject(ulong networkObjectId)
    {
        if (networkObjectId == 0 || NetworkManager.Singleton == null)
        {
            return null;
        }

        return NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject)
            ? networkObject
            : null;
    }

    private void HandleDeath()
    {
        OnDeath?.Invoke(this, EventArgs.Empty);

        if (TryGetComponent(out NPCCarrier carrier))
        {
            carrier.DropHeldObject();
        }

        if (IsNetworkSessionActive() && IsServer && NetworkObject != null)
        {
            NetworkObject.Despawn(true);
        }
        else if (!IsNetworkSessionActive())
        {
            Destroy(gameObject);
        }
    }

    private void CurrentHealthNetwork_OnValueChanged(float previousValue, float newValue)
    {
        OnHealthChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }
}
