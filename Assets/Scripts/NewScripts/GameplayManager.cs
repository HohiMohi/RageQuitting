using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using static BridgeBuildingManager;

public struct BridgeComponentNetworkState : IEquatable<BridgeComponentNetworkState>
{
    public int componentID;
    public bool isMounted;
    public bool isAssembled;
    public bool canBeMounted;
    public float currentAssemblingProgress;

    public BridgeComponentNetworkState(int componentID)
    {
        this.componentID = componentID;
        isMounted = false;
        isAssembled = false;
        canBeMounted = false;
        currentAssemblingProgress = 0f;
    }

    public bool Equals(BridgeComponentNetworkState other)
    {
        return componentID == other.componentID
            && isMounted == other.isMounted
            && isAssembled == other.isAssembled
            && canBeMounted == other.canBeMounted
            && currentAssemblingProgress.Equals(other.currentAssemblingProgress);
    }

    public override bool Equals(object obj)
    {
        return obj is BridgeComponentNetworkState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(componentID, isMounted, isAssembled, canBeMounted, currentAssemblingProgress);
    }
}

public class GameplayManager : MonoBehaviour
{
    private const string RequestFullStateMessageName = "GameplayManager_RequestBridgeState";
    private const string RequestMountMessageName = "GameplayManager_RequestMountBridgeComponent";
    private const string RequestAssembleMessageName = "GameplayManager_RequestAssembleBridgeComponent";
    private const string StateSyncMessageName = "GameplayManager_BridgeState";
    private const int StateMessageBaseSize = sizeof(int);
    private const int StateMessageItemSize = sizeof(int) + sizeof(bool) * 3 + sizeof(float);
    private const int MountRequestMessageSize = sizeof(int) + sizeof(ulong);
    private const int AssembleRequestMessageSize = sizeof(int) * 2 + sizeof(float);

    public static GameplayManager Instance { get; private set; }

    [SerializeField] private Bridge bridge;
    [SerializeField] private BridgeComponentData[] bridgeComponentDataArray;
    [SerializeField] private BridgeBuildingStage[] bridgeBuildingStages;
    [SerializeField] private int currentBridgeBuildingStageIndex;
    [SerializeField] private bool isFullyAsembled;

    private readonly List<BridgeComponentNetworkState> bridgeComponentStates = new List<BridgeComponentNetworkState>();
    private BridgeComponent[] bridgeComponents;
    private bool bridgeComponentEventsSubscribed;
    private bool bridgeFullyAssembledEventInvoked;
    private bool networkMessagingRegistered;

    public EventHandler<BridgeComponentMountableStatusUpdateEventArgs> BridgeComponentMountableStatusUpdate;
    public class BridgeComponentMountableStatusUpdateEventArgs : EventArgs
    {
        public bool canBeMounted;
        public int componentID;
    }

    public bool IsFullyAssembled => isFullyAsembled;
    public event EventHandler OnBridgeFullyAssembled;

    private void Awake()
    {
        Instance = this;
        isFullyAsembled = false;
    }

    private System.Collections.IEnumerator Start()
    {
        CacheBridgeComponents();
        SubscribeBridgeComponentEvents();
        yield return null;

        if (IsNetworkSessionActive())
        {
            EnsureNetworkMessagingRegistered();
            if (NetworkManager.Singleton.IsServer)
            {
                InitializeServerBridgeState();
                UpdateComponentsCanBeMountedProperty();
                BroadcastBridgeState();
            }
            else
            {
                RequestFullBridgeStateFromServer();
            }
        }
        else
        {
            UpdateComponentsCanBeMountedProperty();
        }
    }

    private void Update()
    {
        if (IsNetworkSessionActive())
        {
            EnsureNetworkMessagingRegistered();
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null || !networkMessagingRegistered)
        {
            return;
        }

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(RequestFullStateMessageName);
        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(RequestMountMessageName);
        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(RequestAssembleMessageName);
        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(StateSyncMessageName);
        networkMessagingRegistered = false;
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private void EnsureNetworkMessagingRegistered()
    {
        if (networkMessagingRegistered || NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(StateSyncMessageName, HandleBridgeStateMessage);
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(RequestFullStateMessageName, HandleRequestFullBridgeStateMessage);
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(RequestMountMessageName, HandleRequestMountMessage);
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(RequestAssembleMessageName, HandleRequestAssembleMessage);
        }

        networkMessagingRegistered = true;
    }

    private void CacheBridgeComponents()
    {
        if (bridge == null)
        {
            bridgeComponents = Array.Empty<BridgeComponent>();
            return;
        }

        bridgeComponents = bridge.GetComponentsInChildren<BridgeComponent>(true);
    }

    private void SubscribeBridgeComponentEvents()
    {
        if (bridge == null || bridgeComponentEventsSubscribed)
        {
            return;
        }

        bridge.ComponentMounted += Bridge_OnComponentMounted;
        bridge.ComponentAssembled += Bridge_OnComponentAssembled;
        bridgeComponentEventsSubscribed = true;
    }

    private void InitializeServerBridgeState()
    {
        if (!NetworkManager.Singleton.IsServer || bridgeComponentStates.Count > 0)
        {
            return;
        }

        for (int i = 0; i < bridgeComponentDataArray.Length; i++)
        {
            bridgeComponentStates.Add(new BridgeComponentNetworkState(i)
            {
                isMounted = bridgeComponentDataArray[i].isMounted,
                isAssembled = bridgeComponentDataArray[i].isAssembled,
                canBeMounted = bridgeComponentDataArray[i].CanBeMounted,
                currentAssemblingProgress = 0f
            });
        }
    }

    private void Bridge_OnComponentAssembled(object sender, Bridge.ComponentAssembledEventArgs e)
    {
        if (IsNetworkSessionActive())
        {
            return;
        }

        bridgeComponentDataArray[e.componentID].isAssembled = true;
        CheckCurrentStageMountingProgress();
    }

    private void Bridge_OnComponentMounted(object sender, Bridge.ComponentMountedEventArgs e)
    {
        if (IsNetworkSessionActive())
        {
            return;
        }

        bridgeComponentDataArray[e.componentID].isMounted = true;
        CheckCurrentStageMountingProgress();
    }

    private void UpdateComponentsCanBeMountedProperty()
    {
        if (isFullyAsembled || currentBridgeBuildingStageIndex < 0 || currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length)
        {
            return;
        }

        foreach (int componentIndex in bridgeBuildingStages[currentBridgeBuildingStageIndex].bridgeComponentDataIndexes)
        {
            bridgeComponentDataArray[componentIndex].CanBeMounted = true;
            if (IsNetworkSessionActive() && NetworkManager.Singleton.IsServer && TryGetStateIndex(componentIndex, out int stateIndex))
            {
                BridgeComponentNetworkState state = bridgeComponentStates[stateIndex];
                state.canBeMounted = true;
                bridgeComponentStates[stateIndex] = state;
                ApplyNetworkState(state);
            }

            BridgeComponentMountableStatusUpdate?.Invoke(this, new BridgeComponentMountableStatusUpdateEventArgs { canBeMounted = true, componentID = componentIndex });
        }
    }

    private void CheckCurrentStageMountingProgress()
    {
        if (isFullyAsembled || currentBridgeBuildingStageIndex < 0 || currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length)
        {
            return;
        }

        foreach (int componentIndex in bridgeBuildingStages[currentBridgeBuildingStageIndex].bridgeComponentDataIndexes)
        {
            if (!bridgeComponentDataArray[componentIndex].isMounted || !bridgeComponentDataArray[componentIndex].isAssembled)
            {
                return;
            }
        }

        currentBridgeBuildingStageIndex++;
        if (currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length)
        {
            InvokeBridgeFullyAssembledOnce();
            return;
        }

        UpdateComponentsCanBeMountedProperty();
        if (IsNetworkSessionActive() && NetworkManager.Singleton.IsServer)
        {
            BroadcastBridgeState();
        }
    }

    public void RequestMountBridgeComponent(BridgeComponent bridgeComponent, MountableBridgeComponent heldComponent)
    {
        if (bridgeComponent == null || heldComponent == null)
        {
            return;
        }

        if (!IsNetworkSessionActive())
        {
            TryMountBridgeComponentLocal(bridgeComponent, heldComponent);
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            TryMountBridgeComponentServer(bridgeComponent.ComponentID, heldComponent.NetworkObjectId);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(MountRequestMessageSize, Allocator.Temp);
        writer.WriteValueSafe(bridgeComponent.ComponentID);
        writer.WriteValueSafe(heldComponent.NetworkObjectId);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(RequestMountMessageName, NetworkManager.ServerClientId, writer);
    }

    public void RequestAssembleBridgeComponent(BridgeComponent bridgeComponent, EquippableItemSO equippableItemSO, float damage)
    {
        if (bridgeComponent == null)
        {
            return;
        }

        if (!IsNetworkSessionActive())
        {
            bridgeComponent.HandleAssemblingLocal(equippableItemSO, damage);
            return;
        }

        if (equippableItemSO == null)
        {
            bridgeComponent.NotifyEquippedItemTypeNeeded();
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            TryAssembleBridgeComponentServer(bridgeComponent.ComponentID, equippableItemSO.itemType, damage);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(AssembleRequestMessageSize, Allocator.Temp);
        writer.WriteValueSafe(bridgeComponent.ComponentID);
        writer.WriteValueSafe((int)equippableItemSO.itemType);
        writer.WriteValueSafe(damage);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(RequestAssembleMessageName, NetworkManager.ServerClientId, writer);
    }

    private void TryMountBridgeComponentLocal(BridgeComponent bridgeComponent, MountableBridgeComponent heldComponent)
    {
        if (!CanMountBridgeComponent(bridgeComponent, heldComponent))
        {
            return;
        }

        bridgeComponent.ApplyMountedState();
        heldComponent.RemoveFromWorld();
    }

    private void HandleRequestMountMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int componentID);
        reader.ReadValueSafe(out ulong mountableComponentNetworkObjectId);
        TryMountBridgeComponentServer(componentID, mountableComponentNetworkObjectId);
    }

    private void TryMountBridgeComponentServer(int componentID, ulong mountableComponentNetworkObjectId)
    {
        if (!NetworkManager.Singleton.IsServer || !TryGetBridgeComponent(componentID, out BridgeComponent bridgeComponent))
        {
            return;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(mountableComponentNetworkObjectId, out NetworkObject mountableNetworkObject))
        {
            return;
        }

        if (!mountableNetworkObject.TryGetComponent(out MountableBridgeComponent heldComponent) || !CanMountBridgeComponent(bridgeComponent, heldComponent))
        {
            return;
        }

        if (!TryGetStateIndex(componentID, out int stateIndex))
        {
            return;
        }

        heldComponent.RemoveFromWorld();
        BridgeComponentNetworkState state = bridgeComponentStates[stateIndex];
        state.isMounted = true;
        state.canBeMounted = false;
        if (!bridgeComponent.NeedAssembling)
        {
            state.isAssembled = true;
            state.currentAssemblingProgress = bridgeComponent.GetAssemblingProgressNeeded();
        }

        bridgeComponentStates[stateIndex] = state;
        ApplyNetworkState(state);
        CheckCurrentStageMountingProgress();
        BroadcastBridgeState();
    }

    private bool CanMountBridgeComponent(BridgeComponent bridgeComponent, MountableBridgeComponent heldComponent)
    {
        return bridgeComponent.CanBeMounted
            && !bridgeComponent.IsMounted
            && heldComponent.GetMountableBridgeComponentSO() != null
            && heldComponent.GetMountableBridgeComponentSO().bridgeComponentSO == bridgeComponent.GetBridgeComponentSO();
    }

    private void HandleRequestAssembleMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int componentID);
        reader.ReadValueSafe(out int equippableItemTypeValue);
        reader.ReadValueSafe(out float damage);
        TryAssembleBridgeComponentServer(componentID, (EquippableItemType)equippableItemTypeValue, damage);
    }

    private void TryAssembleBridgeComponentServer(int componentID, EquippableItemType equippableItemType, float damage)
    {
        if (!NetworkManager.Singleton.IsServer || damage <= 0f || !TryGetBridgeComponent(componentID, out BridgeComponent bridgeComponent))
        {
            return;
        }

        if (!bridgeComponent.IsMounted || bridgeComponent.IsAssembled || !bridgeComponent.NeedAssembling || !bridgeComponent.SupportsEquippableItemType(equippableItemType))
        {
            return;
        }

        if (!TryGetStateIndex(componentID, out int stateIndex))
        {
            return;
        }

        BridgeComponentNetworkState state = bridgeComponentStates[stateIndex];
        state.currentAssemblingProgress = Mathf.Clamp(state.currentAssemblingProgress + damage, 0f, bridgeComponent.GetAssemblingProgressNeeded());
        if (state.currentAssemblingProgress >= bridgeComponent.GetAssemblingProgressNeeded())
        {
            state.isAssembled = true;
        }

        bridgeComponentStates[stateIndex] = state;
        ApplyNetworkState(state);
        CheckCurrentStageMountingProgress();
        BroadcastBridgeState();
    }

    private void RequestFullBridgeStateFromServer()
    {
        if (!IsNetworkSessionActive() || NetworkManager.Singleton.IsServer)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(RequestFullStateMessageName, NetworkManager.ServerClientId, writer);
    }

    private void HandleRequestFullBridgeStateMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            return;
        }

        InitializeServerBridgeState();
        SendBridgeState(senderClientId);
    }

    private void BroadcastBridgeState()
    {
        if (!IsNetworkSessionActive() || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        using FastBufferWriter writer = CreateBridgeStateWriter();
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(StateSyncMessageName, writer);
    }

    private void SendBridgeState(ulong clientId)
    {
        using FastBufferWriter writer = CreateBridgeStateWriter();
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(StateSyncMessageName, clientId, writer);
    }

    private FastBufferWriter CreateBridgeStateWriter()
    {
        FastBufferWriter writer = new FastBufferWriter(StateMessageBaseSize + bridgeComponentStates.Count * StateMessageItemSize, Allocator.Temp);
        writer.WriteValueSafe(bridgeComponentStates.Count);
        for (int i = 0; i < bridgeComponentStates.Count; i++)
        {
            WriteState(writer, bridgeComponentStates[i]);
        }

        return writer;
    }

    private void HandleBridgeStateMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int stateCount);
        bridgeComponentStates.Clear();

        for (int i = 0; i < stateCount; i++)
        {
            BridgeComponentNetworkState state = ReadState(reader);
            bridgeComponentStates.Add(state);
            ApplyNetworkState(state);
        }

        if (IsBridgeFullyAssembledFromStateList())
        {
            InvokeBridgeFullyAssembledOnce();
        }
    }

    private void WriteState(FastBufferWriter writer, BridgeComponentNetworkState state)
    {
        writer.WriteValueSafe(state.componentID);
        writer.WriteValueSafe(state.isMounted);
        writer.WriteValueSafe(state.isAssembled);
        writer.WriteValueSafe(state.canBeMounted);
        writer.WriteValueSafe(state.currentAssemblingProgress);
    }

    private BridgeComponentNetworkState ReadState(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int componentID);
        reader.ReadValueSafe(out bool isMounted);
        reader.ReadValueSafe(out bool isAssembled);
        reader.ReadValueSafe(out bool canBeMounted);
        reader.ReadValueSafe(out float currentAssemblingProgress);

        return new BridgeComponentNetworkState(componentID)
        {
            isMounted = isMounted,
            isAssembled = isAssembled,
            canBeMounted = canBeMounted,
            currentAssemblingProgress = currentAssemblingProgress
        };
    }

    private void ApplyNetworkState(BridgeComponentNetworkState state)
    {
        if (!TryGetBridgeComponent(state.componentID, out BridgeComponent bridgeComponent))
        {
            return;
        }

        bridgeComponent.ApplyNetworkState(state);
        if (state.componentID >= 0 && state.componentID < bridgeComponentDataArray.Length)
        {
            bridgeComponentDataArray[state.componentID].isMounted = state.isMounted;
            bridgeComponentDataArray[state.componentID].isAssembled = state.isAssembled;
            bridgeComponentDataArray[state.componentID].CanBeMounted = state.canBeMounted;
        }

        if (state.canBeMounted)
        {
            BridgeComponentMountableStatusUpdate?.Invoke(this, new BridgeComponentMountableStatusUpdateEventArgs { canBeMounted = true, componentID = state.componentID });
        }
    }

    private bool IsBridgeFullyAssembledFromStateList()
    {
        if (bridgeComponentStates.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < bridgeComponentStates.Count; i++)
        {
            BridgeComponentNetworkState state = bridgeComponentStates[i];
            if (!state.isMounted || !state.isAssembled)
            {
                return false;
            }
        }

        return true;
    }

    private void InvokeBridgeFullyAssembledOnce()
    {
        if (bridgeFullyAssembledEventInvoked)
        {
            return;
        }

        bridgeFullyAssembledEventInvoked = true;
        isFullyAsembled = true;
        OnBridgeFullyAssembled?.Invoke(this, EventArgs.Empty);
    }

    private bool TryGetStateIndex(int componentID, out int stateIndex)
    {
        for (int i = 0; i < bridgeComponentStates.Count; i++)
        {
            if (bridgeComponentStates[i].componentID == componentID)
            {
                stateIndex = i;
                return true;
            }
        }

        stateIndex = -1;
        return false;
    }

    private bool TryGetBridgeComponent(int componentID, out BridgeComponent bridgeComponent)
    {
        CacheBridgeComponents();
        foreach (BridgeComponent currentBridgeComponent in bridgeComponents)
        {
            if (currentBridgeComponent != null && currentBridgeComponent.ComponentID == componentID)
            {
                bridgeComponent = currentBridgeComponent;
                return true;
            }
        }

        bridgeComponent = null;
        return false;
    }
}
