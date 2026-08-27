using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour, IDamageable, IInteractableNew
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float healthRegenerationPerSecond = 5f;
    [SerializeField] private float regenerationDelayAfterDamage = 5f;
    [SerializeField] private float respawnAvailableDelay = 15f;

    private readonly NetworkVariable<float> currentHealthNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isDownedNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> downedAtTimeNetwork = new NetworkVariable<float>(
        -1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> npcCarryRespawnPauseStartedAtNetwork = new NetworkVariable<float>(
        -1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float currentHealthLocal;
    private bool isDownedLocal;
    private float downedAtTimeLocal = -1f;
    private float npcCarryRespawnPauseStartedAtLocal = -1f;
    private float lastDamageTime = float.NegativeInfinity;
    private PlayerInteractionNew playerInteraction;
    private DownedPlayerCarryable downedPlayerCarryable;
    private PlayerWaterExposureController waterExposureController;

    public event EventHandler OnHealthChanged;
    public event EventHandler OnDownedStateChanged;

    public float CurrentHealth => IsNetworkStateActive ? currentHealthNetwork.Value : currentHealthLocal;
    public float MaxHealth => maxHealth;
    public bool IsDowned => IsNetworkStateActive ? isDownedNetwork.Value : isDownedLocal;
    public bool IsCarriedByNPC => downedPlayerCarryable != null && downedPlayerCarryable.IsCarriedByNPC;
    public bool IsRespawnTimerPausedByNpcCarry => IsNetworkStateActive
        ? npcCarryRespawnPauseStartedAtNetwork.Value >= 0f
        : npcCarryRespawnPauseStartedAtLocal >= 0f;
    public bool CanBeRevived => IsDowned
        && !IsCarriedByNPC
        && (waterExposureController == null || !waterExposureController.IsInUnsafeWater);
    public bool CanRespawn => IsDowned && !IsCarriedByNPC && GetRespawnTimeRemaining() <= 0f;

    private bool IsNetworkStateActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    private void Awake()
    {
        playerInteraction = GetComponent<PlayerInteractionNew>();
        downedPlayerCarryable = GetComponent<DownedPlayerCarryable>();
        waterExposureController = GetComponent<PlayerWaterExposureController>();
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
            downedAtTimeNetwork.Value = -1f;
            npcCarryRespawnPauseStartedAtNetwork.Value = -1f;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentHealthNetwork.OnValueChanged -= CurrentHealthNetwork_OnValueChanged;
        isDownedNetwork.OnValueChanged -= IsDownedNetwork_OnValueChanged;
        npcCarryRespawnPauseStartedAtLocal = -1f;
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

    public float GetRespawnTimeRemaining()
    {
        if (!IsDowned)
        {
            return respawnAvailableDelay;
        }

        float downedAtTime = IsNetworkStateActive ? downedAtTimeNetwork.Value : downedAtTimeLocal;
        if (downedAtTime < 0f)
        {
            return respawnAvailableDelay;
        }

        float pauseStartedAt = IsNetworkStateActive
            ? npcCarryRespawnPauseStartedAtNetwork.Value
            : npcCarryRespawnPauseStartedAtLocal;
        float effectiveCurrentTime = pauseStartedAt >= 0f ? pauseStartedAt : GetCurrentStateTime();
        return Mathf.Max(0f, respawnAvailableDelay - (effectiveCurrentTime - downedAtTime));
    }

    internal void PauseRespawnTimerForNpcCarry()
    {
        if (!IsDowned)
        {
            return;
        }

        if (IsNetworkStateActive)
        {
            if (!IsServer || npcCarryRespawnPauseStartedAtNetwork.Value >= 0f)
            {
                return;
            }

            npcCarryRespawnPauseStartedAtNetwork.Value = GetCurrentStateTime();
            return;
        }

        if (npcCarryRespawnPauseStartedAtLocal < 0f)
        {
            npcCarryRespawnPauseStartedAtLocal = GetCurrentStateTime();
        }
    }

    internal void ResumeRespawnTimerAfterNpcCarry()
    {
        if (IsNetworkStateActive)
        {
            if (!IsServer || npcCarryRespawnPauseStartedAtNetwork.Value < 0f)
            {
                return;
            }

            float pauseDuration = Mathf.Max(
                0f,
                GetCurrentStateTime() - npcCarryRespawnPauseStartedAtNetwork.Value);
            if (isDownedNetwork.Value && downedAtTimeNetwork.Value >= 0f)
            {
                downedAtTimeNetwork.Value += pauseDuration;
            }

            npcCarryRespawnPauseStartedAtNetwork.Value = -1f;
            return;
        }

        if (npcCarryRespawnPauseStartedAtLocal < 0f)
        {
            return;
        }

        float localPauseDuration = Mathf.Max(
            0f,
            GetCurrentStateTime() - npcCarryRespawnPauseStartedAtLocal);
        if (isDownedLocal && downedAtTimeLocal >= 0f)
        {
            downedAtTimeLocal += localPauseDuration;
        }

        npcCarryRespawnPauseStartedAtLocal = -1f;
    }

    public void RequestRevive(NetworkObject reviver)
    {
        if (!CanBeRevived)
        {
            return;
        }

        if (IsNetworkStateActive)
        {
            ulong reviverNetworkObjectId = reviver != null ? reviver.NetworkObjectId : 0;
            if (IsServer)
            {
                TryReviveNetwork(reviverNetworkObjectId);
            }
            else
            {
                RequestReviveRpc(reviverNetworkObjectId);
            }

            return;
        }

        RestoreFullHealth();
    }

    public void RequestRespawn()
    {
        if (!CanRespawn)
        {
            return;
        }

        if (IsNetworkStateActive)
        {
            if (IsServer)
            {
                RespawnNetwork();
            }
            else
            {
                RequestRespawnRpc();
            }

            return;
        }

        RespawnLocal();
    }

    public void RestoreFullHealth()
    {
        ForceDropIfCarried();

        if (IsNetworkStateActive)
        {
            if (!IsServer)
            {
                return;
            }

            currentHealthNetwork.Value = maxHealth;
            isDownedNetwork.Value = false;
            downedAtTimeNetwork.Value = -1f;
            npcCarryRespawnPauseStartedAtNetwork.Value = -1f;
        }
        else
        {
            currentHealthLocal = maxHealth;
            isDownedLocal = false;
            downedAtTimeLocal = -1f;
            npcCarryRespawnPauseStartedAtLocal = -1f;
        }

        OnDownedStateChanged?.Invoke(this, EventArgs.Empty);
        OnHealthChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Interact(Transform interactor)
    {
    }

    public void LookedAt(Transform interactor)
    {
    }

    public void LookedAway(Transform interactor)
    {
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

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestReviveRpc(ulong reviverNetworkObjectId)
    {
        TryReviveNetwork(reviverNetworkObjectId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestRespawnRpc()
    {
        RespawnNetwork();
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
            downedAtTimeNetwork.Value = GetCurrentStateTime();
            npcCarryRespawnPauseStartedAtNetwork.Value = -1f;
        }
        else
        {
            ForceDropIfCarried();
            downedAtTimeNetwork.Value = -1f;
            npcCarryRespawnPauseStartedAtNetwork.Value = -1f;
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
            downedAtTimeLocal = GetCurrentStateTime();
            npcCarryRespawnPauseStartedAtLocal = -1f;
            DropHeldObjectBecauseDowned();
        }
        else
        {
            ForceDropIfCarried();
            downedAtTimeLocal = -1f;
            npcCarryRespawnPauseStartedAtLocal = -1f;
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

    private void TryReviveNetwork(ulong reviverNetworkObjectId)
    {
        if (!IsServer || !CanBeRevived || reviverNetworkObjectId == NetworkObject.NetworkObjectId)
        {
            return;
        }

        RestoreFullHealth();
    }

    private void RespawnNetwork()
    {
        if (!IsServer || !CanRespawn)
        {
            return;
        }

        Transform spawnPoint = PlayerSpawnManager.GetSpawnPointForClient(OwnerClientId);
        if (spawnPoint == null)
        {
            Debug.LogWarning($"PlayerHealth: Could not find respawn point for client {OwnerClientId}.");
            return;
        }

        Transform playerTransform = transform;
        ForceDropIfCarried();
        playerTransform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
        RestoreFullHealth();
        TeleportOwnerClientRpc(spawnPoint.position, spawnPoint.rotation, CreateTargetClientRpcParams(OwnerClientId));
    }

    private void RespawnLocal()
    {
        Transform spawnPoint = PlayerSpawnManager.GetSpawnPointForClient(0);
        if (spawnPoint != null)
        {
            ForceDropIfCarried();
            TeleportLocally(spawnPoint.position, spawnPoint.rotation);
        }

        RestoreFullHealth();
    }

    private void ForceDropIfCarried()
    {
        if (downedPlayerCarryable == null || !downedPlayerCarryable.IsCarried)
        {
            return;
        }

        downedPlayerCarryable.ForceDrop();
    }

    [ClientRpc]
    private void TeleportOwnerClientRpc(Vector3 position, Quaternion rotation, ClientRpcParams clientRpcParams = default)
    {
        TeleportLocally(position, rotation);
    }

    public void ApplyTechnicalTransportExit(Vector3 position, Quaternion rotation)
    {
        TeleportLocally(position, rotation);
        GetComponent<PlayerWheelbarrowController>()?.CompleteTechnicalSafeExit();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer &&
            OwnerClientId != NetworkManager.ServerClientId)
            TechnicalTransportExitOwnerClientRpc(position, rotation, CreateTargetClientRpcParams(OwnerClientId));
    }

    [ClientRpc]
    private void TechnicalTransportExitOwnerClientRpc(
        Vector3 position,
        Quaternion rotation,
        ClientRpcParams clientRpcParams = default)
    {
        TeleportLocally(position, rotation);
        GetComponent<PlayerWheelbarrowController>()?.CompleteTechnicalSafeExit();
    }

    private void TeleportLocally(Vector3 position, Quaternion rotation)
    {
        CharacterController characterController = GetComponent<CharacterController>();
        bool wasCharacterControllerEnabled = characterController != null && characterController.enabled;
        if (wasCharacterControllerEnabled)
        {
            characterController.enabled = false;
        }

        transform.SetPositionAndRotation(position, rotation);

        if (wasCharacterControllerEnabled)
        {
            characterController.enabled = true;
        }
        GetComponent<PlayerTransportCollisionController>()?.EnsureSuppressed();
    }

    private ClientRpcParams CreateTargetClientRpcParams(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };
    }

    private float GetCurrentStateTime()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return (float)NetworkManager.Singleton.ServerTime.Time;
        }

        return Time.time;
    }
}
