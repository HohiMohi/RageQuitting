using System;
using Unity.Netcode;
using UnityEngine;

public class CarpenterTableFactory : BaseFactory
{
    private const ulong NoCrankOwner = ulong.MaxValue;

    [SerializeField] private CarpenterTableSwitch tableSwitch;
    [SerializeField] private CarpenterDimensionCrank[] dimensionCranks;
    [SerializeField] private CarpenterTableMinigame carpenterTableMinigame;

    [Header("Dimension crank interaction")]
    [SerializeField] private float dimensionInteractionDistance = 3.5f;

    [Header("Component production properties")]
    [SerializeField] private float componentLengthMax;
    [SerializeField] private float componentLengthMin;
    [SerializeField] private float componentLenghtStep;
    [SerializeField] private float componentWidthMax;
    [SerializeField] private float componentWidthMin;
    [SerializeField] private float componentWidthStep;

    private readonly NetworkVariable<float> currentWidthNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<float> currentLengthNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<ulong> widthCrankOwnerNetwork = new NetworkVariable<ulong>(NoCrankOwner);
    private readonly NetworkVariable<ulong> lengthCrankOwnerNetwork = new NetworkVariable<ulong>(NoCrankOwner);

    private float localCurrentWidth;
    private float localCurrentLength;
    private bool localWidthCrankLocked;
    private bool localLengthCrankLocked;

    public event EventHandler<DimensionAdjustmentEventArgs> DimensionAdjustmentGranted;
    public event EventHandler<DimensionAdjustmentEventArgs> DimensionAdjustmentRevoked;
    public event EventHandler<DimensionAdjustmentEventArgs> DimensionStepRejected;
    public event EventHandler<DimensionAdjustmentDeniedEventArgs> DimensionAdjustmentDenied;

    public class DimensionAdjustmentEventArgs : EventArgs
    {
        public ComponentDimension Dimension;
    }

    public class DimensionAdjustmentDeniedEventArgs : EventArgs
    {
        public ComponentDimension Dimension;
        public string Reason;
    }

    public EventHandler<BridgeComponentSelectionConfirmEventArgs> BridgeComponentSelectionConfirm;
    public class BridgeComponentSelectionConfirmEventArgs : EventArgs
    {
        public MountableBridgeComponentSO mountableBridgeComponentSO;
    }

    public EventHandler<TryEndProductionEventArgs> TryEndProduction;
    public class TryEndProductionEventArgs : EventArgs
    {
        public Transform interactor;
    }

    public float CurrentWidth => IsNetworkSessionActive() ? currentWidthNetwork.Value : localCurrentWidth;
    public float CurrentLength => IsNetworkSessionActive() ? currentLengthNetwork.Value : localCurrentLength;
    public bool AreDimensionsMatchingSelectedComponent => CheckSettedComponentDimensions();

    public Vector2 GetCurrentDimensions()
    {
        return new Vector2(CurrentWidth, CurrentLength);
    }

    public Vector2 GetRequiredDimensionsForSelectedComponent()
    {
        MountableBridgeComponentSO selectedComponent = SelectedComponent;
        if (selectedComponent == null)
        {
            return Vector2.zero;
        }

        return new Vector2(selectedComponent.componentWidth, selectedComponent.componentLength);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        currentWidthNetwork.OnValueChanged += FactoryDimension_OnValueChanged;
        currentLengthNetwork.OnValueChanged += FactoryDimension_OnValueChanged;
        widthCrankOwnerNetwork.OnValueChanged += CrankOwnerNetwork_OnValueChanged;
        lengthCrankOwnerNetwork.OnValueChanged += CrankOwnerNetwork_OnValueChanged;

        if (IsServer)
        {
            currentWidthNetwork.Value = componentWidthMin;
            currentLengthNetwork.Value = componentLengthMin;
            widthCrankOwnerNetwork.Value = NoCrankOwner;
            lengthCrankOwnerNetwork.Value = NoCrankOwner;
            NetworkManager.OnClientDisconnectCallback += NetworkManager_OnClientDisconnectCallback;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentWidthNetwork.OnValueChanged -= FactoryDimension_OnValueChanged;
        currentLengthNetwork.OnValueChanged -= FactoryDimension_OnValueChanged;
        widthCrankOwnerNetwork.OnValueChanged -= CrankOwnerNetwork_OnValueChanged;
        lengthCrankOwnerNetwork.OnValueChanged -= CrankOwnerNetwork_OnValueChanged;
        if (NetworkManager != null && IsServer)
        {
            NetworkManager.OnClientDisconnectCallback -= NetworkManager_OnClientDisconnectCallback;
        }
        base.OnNetworkDespawn();
    }

    protected override void Start()
    {
        base.Start();

        if (tableSwitch != null)
        {
            tableSwitch.CarpenterTableSwitchPressed += CarpenterTableSwitch_OnCarpenterTableSwitchPressed;
        }

        if (dimensionCranks != null)
        {
            foreach (CarpenterDimensionCrank crank in dimensionCranks)
            {
                if (crank != null)
                {
                    crank.Initialize(this);
                }
            }
        }

        if (carpenterTableMinigame != null)
        {
            carpenterTableMinigame.MinigameCompletedEvent += CarpenterTableFactory_OnMinigameCompleted;
            carpenterTableMinigame.MinigameFailedEvent += CarpenterTableFactory_OnMinigameFailed;
            carpenterTableMinigame.MinigameCriticallyFailedEvent += CarpenterTableFactory_OnMinigameCriticallyFailed;
        }

        localCurrentLength = componentLengthMin;
        localCurrentWidth = componentWidthMin;
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Update()
    {
        base.Update();

        if (IsNetworkSessionActive())
        {
            if (!IsServer)
            {
                return;
            }

            if (IsProducing)
            {
                ReleaseAllDimensionLocksServer();
                return;
            }

            ValidateDimensionLockOwnerServer(ComponentDimension.Width);
            ValidateDimensionLockOwnerServer(ComponentDimension.Length);
            return;
        }

        if (IsProducing)
        {
            ReleaseLocalDimensionLock(ComponentDimension.Width);
            ReleaseLocalDimensionLock(ComponentDimension.Length);
        }
    }

    protected override bool CanProduceAdditionalConditions(ProductionRecipeSO productionRecipeSO, out FactoryProductionFailureReason reason)
    {
        if (!CheckSettedComponentDimensions())
        {
            reason = FactoryProductionFailureReason.InvalidDimensions;
            return false;
        }

        reason = FactoryProductionFailureReason.None;
        return true;
    }

    protected override void HandleSelectedRecipeChanged(ProductionRecipeSO selectedRecipe)
    {
        BridgeComponentSelectionConfirm?.Invoke(this, new BridgeComponentSelectionConfirmEventArgs
        {
            mountableBridgeComponentSO = selectedRecipe != null
                ? selectedRecipe.MountableBridgeComponentOutput
                : null
        });
    }

    private void CarpenterTableFactory_OnMinigameCriticallyFailed(object sender, EventArgs e)
    {
        Debug.Log("Carpenter minigame is currently disabled in the production flow.");
    }

    private void CarpenterTableFactory_OnMinigameFailed(object sender, EventArgs e)
    {
        Debug.Log("Carpenter minigame is currently disabled in the production flow.");
    }

    private void CarpenterTableFactory_OnMinigameCompleted(object sender, EventArgs e)
    {
        Debug.Log("Carpenter minigame is currently disabled in the production flow.");
    }

    private void CarpenterTableSwitch_OnCarpenterTableSwitchPressed(object sender, CarpenterTableSwitch.CarpenterTableSwitchPressedEventArgs e)
    {
        RequestStartProduction();
    }

    public float GetDimensionValue(ComponentDimension dimension)
    {
        return dimension == ComponentDimension.Width ? CurrentWidth : CurrentLength;
    }

    public float GetDimensionMin(ComponentDimension dimension)
    {
        return dimension == ComponentDimension.Width ? componentWidthMin : componentLengthMin;
    }

    public float GetDimensionMax(ComponentDimension dimension)
    {
        return dimension == ComponentDimension.Width ? componentWidthMax : componentLengthMax;
    }

    public float GetDimensionStep(ComponentDimension dimension)
    {
        return dimension == ComponentDimension.Width ? componentWidthStep : componentLenghtStep;
    }

    public int GetDimensionStepCount(ComponentDimension dimension)
    {
        float step = Mathf.Max(0.0001f, GetDimensionStep(dimension));
        return Mathf.Max(1, Mathf.RoundToInt((GetDimensionMax(dimension) - GetDimensionMin(dimension)) / step) + 1);
    }

    public int GetDimensionStepIndex(ComponentDimension dimension)
    {
        float step = Mathf.Max(0.0001f, GetDimensionStep(dimension));
        return Mathf.Clamp(
            Mathf.RoundToInt((GetDimensionValue(dimension) - GetDimensionMin(dimension)) / step),
            0,
            GetDimensionStepCount(dimension) - 1);
    }

    public float GetDimensionValueForStep(ComponentDimension dimension, int stepIndex)
    {
        int clampedIndex = Mathf.Clamp(stepIndex, 0, GetDimensionStepCount(dimension) - 1);
        return GetDimensionMin(dimension) + clampedIndex * GetDimensionStep(dimension);
    }

    public bool IsDimensionCrankAvailable(ComponentDimension dimension)
    {
        if (IsProducing)
        {
            return false;
        }

        if (!IsNetworkSessionActive())
        {
            return dimension == ComponentDimension.Width ? !localWidthCrankLocked : !localLengthCrankLocked;
        }

        return GetCrankOwnerNetwork(dimension).Value == NoCrankOwner;
    }

    public void RequestBeginDimensionAdjustment(ComponentDimension dimension, Transform interactor)
    {
        if (!Enum.IsDefined(typeof(ComponentDimension), dimension))
        {
            return;
        }

        if (!IsNetworkSessionActive())
        {
            if (IsProducing || !IsDimensionCrankAvailable(dimension))
            {
                RaiseDimensionDenied(dimension, "Crank is in use");
                return;
            }

            SetLocalCrankLock(dimension, true);
            RaiseDimensionGranted(dimension);
            return;
        }

        if (IsServer)
        {
            ulong requester = interactor != null && interactor.GetComponentInParent<NetworkObject>() != null
                ? interactor.GetComponentInParent<NetworkObject>().OwnerClientId
                : NetworkManager.LocalClientId;
            TryBeginDimensionAdjustmentServer(dimension, requester);
        }
        else
        {
            RequestBeginDimensionAdjustmentServerRpc((int)dimension);
        }
    }

    public void RequestSetDimensionStep(ComponentDimension dimension, int stepIndex)
    {
        if (!IsNetworkSessionActive())
        {
            if (!HasLocalCrankLock(dimension) || !TryApplyDimensionStepLocal(dimension, stepIndex))
            {
                RaiseDimensionStepRejected(dimension);
            }
            return;
        }

        if (IsServer)
        {
            if (!TryApplyDimensionStepServer(dimension, stepIndex, NetworkManager.LocalClientId))
            {
                NotifyDimensionStepRejected(NetworkManager.LocalClientId, dimension);
            }
        }
        else
        {
            RequestSetDimensionStepServerRpc((int)dimension, stepIndex);
        }
    }

    public void RequestEndDimensionAdjustment(ComponentDimension dimension)
    {
        if (!IsNetworkSessionActive())
        {
            SetLocalCrankLock(dimension, false);
            RaiseDimensionRevoked(dimension);
            return;
        }

        if (IsServer)
        {
            ReleaseDimensionLockServer(dimension, NetworkManager.LocalClientId, false);
        }
        else
        {
            RequestEndDimensionAdjustmentServerRpc((int)dimension);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestBeginDimensionAdjustmentServerRpc(int dimensionValue, ServerRpcParams rpcParams = default)
    {
        if (TryParseDimension(dimensionValue, out ComponentDimension dimension))
        {
            TryBeginDimensionAdjustmentServer(dimension, rpcParams.Receive.SenderClientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSetDimensionStepServerRpc(int dimensionValue, int stepIndex, ServerRpcParams rpcParams = default)
    {
        if (!TryParseDimension(dimensionValue, out ComponentDimension dimension))
        {
            return;
        }

        if (!TryApplyDimensionStepServer(dimension, stepIndex, rpcParams.Receive.SenderClientId))
        {
            NotifyDimensionStepRejected(rpcParams.Receive.SenderClientId, dimension);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestEndDimensionAdjustmentServerRpc(int dimensionValue, ServerRpcParams rpcParams = default)
    {
        if (TryParseDimension(dimensionValue, out ComponentDimension dimension))
        {
            ReleaseDimensionLockServer(dimension, rpcParams.Receive.SenderClientId, false);
        }
    }

    private void TryBeginDimensionAdjustmentServer(ComponentDimension dimension, ulong requesterClientId)
    {
        NetworkVariable<ulong> owner = GetCrankOwnerNetwork(dimension);
        if (IsProducing || owner.Value != NoCrankOwner || !IsRequesterNearCrank(requesterClientId, dimension))
        {
            NotifyDimensionDenied(requesterClientId, dimension, owner.Value != NoCrankOwner ? "Crank is in use" : "Crank is unavailable");
            return;
        }

        owner.Value = requesterClientId;
        NotifyDimensionGranted(requesterClientId, dimension);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryApplyDimensionStepServer(ComponentDimension dimension, int stepIndex, ulong requesterClientId)
    {
        if (IsProducing || GetCrankOwnerNetwork(dimension).Value != requesterClientId)
        {
            return false;
        }

        if (stepIndex < 0 || stepIndex >= GetDimensionStepCount(dimension))
        {
            return false;
        }

        float value = GetDimensionValueForStep(dimension, stepIndex);
        if (dimension == ComponentDimension.Width)
        {
            currentWidthNetwork.Value = value;
        }
        else
        {
            currentLengthNetwork.Value = value;
        }

        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool TryApplyDimensionStepLocal(ComponentDimension dimension, int stepIndex)
    {
        if (IsProducing || stepIndex < 0 || stepIndex >= GetDimensionStepCount(dimension))
        {
            return false;
        }

        float value = GetDimensionValueForStep(dimension, stepIndex);
        if (dimension == ComponentDimension.Width)
        {
            localCurrentWidth = value;
        }
        else
        {
            localCurrentLength = value;
        }

        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private NetworkVariable<ulong> GetCrankOwnerNetwork(ComponentDimension dimension)
    {
        return dimension == ComponentDimension.Width ? widthCrankOwnerNetwork : lengthCrankOwnerNetwork;
    }

    private CarpenterDimensionCrank GetDimensionCrank(ComponentDimension dimension)
    {
        if (dimensionCranks == null)
        {
            return null;
        }

        foreach (CarpenterDimensionCrank crank in dimensionCranks)
        {
            if (crank != null && crank.Dimension == dimension)
            {
                return crank;
            }
        }

        return null;
    }

    private bool IsRequesterNearCrank(ulong clientId, ComponentDimension dimension)
    {
        if (NetworkManager == null
            || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
            || client.PlayerObject == null)
        {
            return false;
        }

        PlayerHealth health = client.PlayerObject.GetComponent<PlayerHealth>();
        if (health != null && health.IsDowned)
        {
            return false;
        }

        CarpenterDimensionCrank crank = GetDimensionCrank(dimension);
        return crank != null
            && Vector3.Distance(client.PlayerObject.transform.position, crank.transform.position) <= Mathf.Max(0.5f, dimensionInteractionDistance);
    }

    private void ValidateDimensionLockOwnerServer(ComponentDimension dimension)
    {
        ulong owner = GetCrankOwnerNetwork(dimension).Value;
        if (owner == NoCrankOwner)
        {
            return;
        }

        if (NetworkManager == null
            || !NetworkManager.ConnectedClients.TryGetValue(owner, out NetworkClient client)
            || client.PlayerObject == null)
        {
            ReleaseDimensionLockServer(dimension, owner, true);
            return;
        }

        PlayerHealth health = client.PlayerObject.GetComponent<PlayerHealth>();
        if (health != null && health.IsDowned)
        {
            ReleaseDimensionLockServer(dimension, owner, true);
        }
    }

    private void ReleaseAllDimensionLocksServer()
    {
        ReleaseDimensionLockServer(ComponentDimension.Width, GetCrankOwnerNetwork(ComponentDimension.Width).Value, true);
        ReleaseDimensionLockServer(ComponentDimension.Length, GetCrankOwnerNetwork(ComponentDimension.Length).Value, true);
    }

    private void ReleaseDimensionLockServer(ComponentDimension dimension, ulong requesterClientId, bool force)
    {
        NetworkVariable<ulong> ownerVariable = GetCrankOwnerNetwork(dimension);
        ulong currentOwner = ownerVariable.Value;
        if (currentOwner == NoCrankOwner || (!force && currentOwner != requesterClientId))
        {
            return;
        }

        ownerVariable.Value = NoCrankOwner;
        NotifyDimensionRevoked(currentOwner, dimension);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NetworkManager_OnClientDisconnectCallback(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        ReleaseDimensionLockServer(ComponentDimension.Width, clientId, false);
        ReleaseDimensionLockServer(ComponentDimension.Length, clientId, false);
    }

    private void SetLocalCrankLock(ComponentDimension dimension, bool locked)
    {
        if (dimension == ComponentDimension.Width)
        {
            localWidthCrankLocked = locked;
        }
        else
        {
            localLengthCrankLocked = locked;
        }

        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool HasLocalCrankLock(ComponentDimension dimension)
    {
        return dimension == ComponentDimension.Width ? localWidthCrankLocked : localLengthCrankLocked;
    }

    private void ReleaseLocalDimensionLock(ComponentDimension dimension)
    {
        if (!HasLocalCrankLock(dimension))
        {
            return;
        }

        SetLocalCrankLock(dimension, false);
        RaiseDimensionRevoked(dimension);
    }

    private static bool TryParseDimension(int value, out ComponentDimension dimension)
    {
        if (Enum.IsDefined(typeof(ComponentDimension), value))
        {
            dimension = (ComponentDimension)value;
            return true;
        }

        dimension = default;
        return false;
    }

    private void NotifyDimensionGranted(ulong clientId, ComponentDimension dimension)
    {
        DimensionAdjustmentGrantedClientRpc((int)dimension, CreateTargetClientRpcParams(clientId));
    }

    private void NotifyDimensionDenied(ulong clientId, ComponentDimension dimension, string reason)
    {
        DimensionAdjustmentDeniedClientRpc((int)dimension, reason, CreateTargetClientRpcParams(clientId));
    }

    private void NotifyDimensionRevoked(ulong clientId, ComponentDimension dimension)
    {
        if (clientId == NoCrankOwner || NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(clientId))
        {
            return;
        }

        DimensionAdjustmentRevokedClientRpc((int)dimension, CreateTargetClientRpcParams(clientId));
    }

    private void NotifyDimensionStepRejected(ulong clientId, ComponentDimension dimension)
    {
        DimensionStepRejectedClientRpc((int)dimension, CreateTargetClientRpcParams(clientId));
    }

    private static ClientRpcParams CreateTargetClientRpcParams(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };
    }

    [ClientRpc]
    private void DimensionAdjustmentGrantedClientRpc(int dimensionValue, ClientRpcParams rpcParams = default)
    {
        if (TryParseDimension(dimensionValue, out ComponentDimension dimension))
        {
            RaiseDimensionGranted(dimension);
        }
    }

    [ClientRpc]
    private void DimensionAdjustmentDeniedClientRpc(int dimensionValue, string reason, ClientRpcParams rpcParams = default)
    {
        if (TryParseDimension(dimensionValue, out ComponentDimension dimension))
        {
            RaiseDimensionDenied(dimension, reason);
        }
    }

    [ClientRpc]
    private void DimensionAdjustmentRevokedClientRpc(int dimensionValue, ClientRpcParams rpcParams = default)
    {
        if (TryParseDimension(dimensionValue, out ComponentDimension dimension))
        {
            RaiseDimensionRevoked(dimension);
        }
    }

    [ClientRpc]
    private void DimensionStepRejectedClientRpc(int dimensionValue, ClientRpcParams rpcParams = default)
    {
        if (TryParseDimension(dimensionValue, out ComponentDimension dimension))
        {
            RaiseDimensionStepRejected(dimension);
        }
    }

    private void RaiseDimensionGranted(ComponentDimension dimension)
    {
        DimensionAdjustmentGranted?.Invoke(this, new DimensionAdjustmentEventArgs { Dimension = dimension });
    }

    private void RaiseDimensionDenied(ComponentDimension dimension, string reason)
    {
        DimensionAdjustmentDenied?.Invoke(this, new DimensionAdjustmentDeniedEventArgs
        {
            Dimension = dimension,
            Reason = reason
        });
    }

    private void RaiseDimensionRevoked(ComponentDimension dimension)
    {
        DimensionAdjustmentRevoked?.Invoke(this, new DimensionAdjustmentEventArgs { Dimension = dimension });
    }

    private void RaiseDimensionStepRejected(ComponentDimension dimension)
    {
        DimensionStepRejected?.Invoke(this, new DimensionAdjustmentEventArgs { Dimension = dimension });
    }

    private void CrankOwnerNetwork_OnValueChanged(ulong previousValue, ulong newValue)
    {
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CheckSettedComponentDimensions()
    {
        MountableBridgeComponentSO selectedComponent = SelectedComponent;
        return selectedComponent != null
            && Mathf.Approximately(selectedComponent.componentWidth, CurrentWidth)
            && Mathf.Approximately(selectedComponent.componentLength, CurrentLength);
    }

    private void FactoryDimension_OnValueChanged(float previousValue, float newValue)
    {
        Debug.Log($"Width: {CurrentWidth}, Length: {CurrentLength}");
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
