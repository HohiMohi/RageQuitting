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
    public int constructionStage;
    public float constructionProgress;
    public int constructionValueA;
    public int constructionValueB;
    public float constructionAnchor0;
    public float constructionAnchor1;
    public float constructionAnchor2;
    public float constructionAnchor3;
    public float constructionAux0;
    public float constructionAux1;
    public int mountAlignmentState;
    public ulong mountAlignmentCandidateNetworkObjectId;
    public double mountAlignmentStartedAt;

    public BridgeComponentNetworkState(int componentID)
    {
        this.componentID = componentID;
        isMounted = false;
        isAssembled = false;
        canBeMounted = false;
        currentAssemblingProgress = 0f;
        constructionStage = (int)BridgeConstructionStage.ReadyForMount;
        constructionProgress = 0f;
        constructionValueA = 0;
        constructionValueB = 0;
        constructionAnchor0 = 0f;
        constructionAnchor1 = 0f;
        constructionAnchor2 = 0f;
        constructionAnchor3 = 0f;
        constructionAux0 = 0f;
        constructionAux1 = 0f;
        mountAlignmentState = (int)BridgeMountAlignmentState.Inactive;
        mountAlignmentCandidateNetworkObjectId = BridgeMountSocket.NoCandidateNetworkObjectId;
        mountAlignmentStartedAt = -1d;
    }

    public bool Equals(BridgeComponentNetworkState other)
    {
        return componentID == other.componentID
            && isMounted == other.isMounted
            && isAssembled == other.isAssembled
            && canBeMounted == other.canBeMounted
            && currentAssemblingProgress.Equals(other.currentAssemblingProgress)
            && constructionStage == other.constructionStage
            && constructionProgress.Equals(other.constructionProgress)
            && constructionValueA == other.constructionValueA
            && constructionValueB == other.constructionValueB
            && constructionAnchor0.Equals(other.constructionAnchor0)
            && constructionAnchor1.Equals(other.constructionAnchor1)
            && constructionAnchor2.Equals(other.constructionAnchor2)
            && constructionAnchor3.Equals(other.constructionAnchor3)
            && constructionAux0.Equals(other.constructionAux0)
            && constructionAux1.Equals(other.constructionAux1)
            && mountAlignmentState == other.mountAlignmentState
            && mountAlignmentCandidateNetworkObjectId == other.mountAlignmentCandidateNetworkObjectId
            && mountAlignmentStartedAt.Equals(other.mountAlignmentStartedAt);
    }

    public override bool Equals(object obj)
    {
        return obj is BridgeComponentNetworkState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            HashCode.Combine(componentID, isMounted, isAssembled, canBeMounted, currentAssemblingProgress, constructionStage, constructionProgress),
            constructionValueA,
            constructionValueB,
            constructionAnchor0,
            constructionAnchor1,
            constructionAnchor2,
            constructionAnchor3,
            HashCode.Combine(
                constructionAux0,
                constructionAux1,
                mountAlignmentState,
                mountAlignmentCandidateNetworkObjectId,
                mountAlignmentStartedAt));
    }
}

public readonly struct BridgeRequirementLine
{
    public readonly string ComponentName;
    public readonly int CurrentAmount;
    public readonly int RequiredAmount;

    public BridgeRequirementLine(string componentName, int currentAmount, int requiredAmount)
    {
        ComponentName = componentName;
        CurrentAmount = currentAmount;
        RequiredAmount = requiredAmount;
    }
}

public readonly struct BridgeRequirementsSnapshot
{
    public readonly int CurrentStageIndex;
    public readonly bool IsBridgeComplete;
    public readonly IReadOnlyList<BridgeRequirementLine> CurrentStageRequirements;
    public readonly IReadOnlyList<BridgeRequirementLine> RemainingStageRequirements;

    public BridgeRequirementsSnapshot(
        int currentStageIndex,
        bool isBridgeComplete,
        IReadOnlyList<BridgeRequirementLine> currentStageRequirements,
        IReadOnlyList<BridgeRequirementLine> remainingStageRequirements)
    {
        CurrentStageIndex = currentStageIndex;
        IsBridgeComplete = isBridgeComplete;
        CurrentStageRequirements = currentStageRequirements;
        RemainingStageRequirements = remainingStageRequirements;
    }
}

public sealed class BridgeConstructionStageChangedEventArgs : EventArgs
{
    public BridgeComponent Component { get; }
    public BridgeConstructionStage PreviousStage { get; }
    public BridgeConstructionStage CurrentStage { get; }

    public BridgeConstructionStageChangedEventArgs(
        BridgeComponent component,
        BridgeConstructionStage previousStage,
        BridgeConstructionStage currentStage)
    {
        Component = component;
        PreviousStage = previousStage;
        CurrentStage = currentStage;
    }
}

public class GameplayManager : MonoBehaviour
{
    private const string RequestFullStateMessageName = "GameplayManager_RequestBridgeState";
    private const string RequestMountMessageName = "GameplayManager_RequestMountBridgeComponent";
    private const string RequestAssembleMessageName = "GameplayManager_RequestAssembleBridgeComponent";
    private const string RequestConstructionWorkMessageName = "GameplayManager_RequestConstructionWork";
    private const string StateSyncMessageName = "GameplayManager_BridgeState";
    private const int StateMessageBaseSize = sizeof(int);
    private const int StateMessageItemSize = sizeof(int) * 5 + sizeof(bool) * 3 + sizeof(float) * 8 + sizeof(ulong) + sizeof(double);
    private const int MountRequestMessageSize = sizeof(int) + sizeof(ulong);
    private const int AssembleRequestMessageSize = sizeof(int) * 2;
    private const int ConstructionWorkRequestMessageSize = sizeof(int) * 3;

    public static GameplayManager Instance { get; private set; }

    [SerializeField] private Bridge bridge;
    [SerializeField] private BridgeComponentData[] bridgeComponentDataArray;
    [SerializeField] private BridgeBuildingStage[] bridgeBuildingStages;
    [SerializeField] private int currentBridgeBuildingStageIndex;
    [SerializeField] private bool isFullyAsembled;

    private readonly List<BridgeComponentNetworkState> bridgeComponentStates = new List<BridgeComponentNetworkState>();
    private readonly HashSet<int> reportedInvalidStageComponentIndexes = new HashSet<int>();
    private readonly Dictionary<int, BridgeConstructionStage> observedConstructionStages =
        new Dictionary<int, BridgeConstructionStage>();
    private readonly Dictionary<int, HashSet<BridgeConstructionStage>> reachedConstructionStages =
        new Dictionary<int, HashSet<BridgeConstructionStage>>();
    private BridgeComponent[] bridgeComponents;
    private bool bridgeComponentEventsSubscribed;
    private bool bridgeFullyAssembledEventInvoked;
    private bool networkMessagingRegistered;
    private bool hasAppliedInitialNetworkBridgeState;

    public EventHandler<BridgeComponentMountableStatusUpdateEventArgs> BridgeComponentMountableStatusUpdate;
    public class BridgeComponentMountableStatusUpdateEventArgs : EventArgs
    {
        public bool canBeMounted;
        public int componentID;
    }

    public bool IsFullyAssembled => isFullyAsembled;
    public int CurrentBridgeStageIndex => currentBridgeBuildingStageIndex;
    public event EventHandler OnBridgeFullyAssembled;
    public event EventHandler OnBridgeRequirementsChanged;
    public event EventHandler<BridgeConstructionStageChangedEventArgs> OnConstructionStageChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("GameplayManager: Duplicate instance detected. Disabling this instance.");
            enabled = false;
            return;
        }

        Instance = this;
        isFullyAsembled = false;
    }

    private System.Collections.IEnumerator Start()
    {
        if (Instance != this)
        {
            yield break;
        }

        EnsureBridgeReference();
        if (bridge == null)
        {
            Debug.LogError("GameplayManager: No Bridge reference found in scene. Bridge gameplay state will not be initialized.");
            yield break;
        }

        CacheBridgeComponents();
        SeedObservedConstructionStages();
        SubscribeBridgeComponentEvents();
        yield return null;

        if (IsNetworkSessionActive())
        {
            EnsureNetworkMessagingRegistered();
            if (NetworkManager.Singleton.IsServer)
            {
                InitializeServerBridgeState();
                UpdateComponentsCanBeMountedProperty();
                NotifyBridgeRequirementsChanged();
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
            NotifyBridgeRequirementsChanged();
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
        UnsubscribeBridgeComponentEvents();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.CustomMessagingManager != null && networkMessagingRegistered)
        {
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(RequestFullStateMessageName);
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(RequestMountMessageName);
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(RequestAssembleMessageName);
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(RequestConstructionWorkMessageName);
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(StateSyncMessageName);
            networkMessagingRegistered = false;
        }

        if (Instance == this)
        {
            Instance = null;
        }
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
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(RequestConstructionWorkMessageName, HandleRequestConstructionWorkMessage);
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

    private void UnsubscribeBridgeComponentEvents()
    {
        if (bridge == null || !bridgeComponentEventsSubscribed)
        {
            return;
        }

        bridge.ComponentMounted -= Bridge_OnComponentMounted;
        bridge.ComponentAssembled -= Bridge_OnComponentAssembled;
        bridgeComponentEventsSubscribed = false;
    }

    private void EnsureBridgeReference()
    {
        if (bridge != null)
        {
            return;
        }

        bridge = FindFirstObjectByType<Bridge>();
    }

    private void InitializeServerBridgeState()
    {
        if (!NetworkManager.Singleton.IsServer || bridgeComponentStates.Count > 0)
        {
            return;
        }

        for (int i = 0; i < bridgeComponentDataArray.Length; i++)
        {
            BridgeComponent component = null;
            TryGetBridgeComponent(i, out component);
            BridgeConstructionSite constructionSite = component != null ? component.ConstructionSite : null;
            BridgeComponentNetworkState state = new BridgeComponentNetworkState(i)
            {
                isMounted = bridgeComponentDataArray[i].isMounted,
                isAssembled = bridgeComponentDataArray[i].isAssembled,
                canBeMounted = bridgeComponentDataArray[i].CanBeMounted,
                currentAssemblingProgress = 0f,
                constructionStage = constructionSite != null ? (int)constructionSite.CurrentStage : (int)BridgeConstructionStage.ReadyForMount,
                constructionProgress = constructionSite != null ? constructionSite.CurrentWorkProgress : 0f
            };
            constructionSite?.PopulateNetworkState(ref state);
            component?.MountSocket?.PopulateNetworkState(ref state);
            bridgeComponentStates.Add(state);
        }
    }

    private void Bridge_OnComponentAssembled(object sender, Bridge.ComponentAssembledEventArgs e)
    {
        if (IsNetworkSessionActive())
        {
            return;
        }

        if (!IsValidComponentDataIndex(e.componentID))
        {
            return;
        }

        bridgeComponentDataArray[e.componentID].isAssembled = true;
        ObserveConstructionStage(e.componentID);
        CheckCurrentStageMountingProgress();
        NotifyBridgeRequirementsChanged();
    }

    private void Bridge_OnComponentMounted(object sender, Bridge.ComponentMountedEventArgs e)
    {
        if (IsNetworkSessionActive())
        {
            return;
        }

        if (!IsValidComponentDataIndex(e.componentID))
        {
            return;
        }

        bridgeComponentDataArray[e.componentID].isMounted = true;
        ObserveConstructionStage(e.componentID);
        CheckCurrentStageMountingProgress();
        NotifyBridgeRequirementsChanged();
    }

    private void UpdateComponentsCanBeMountedProperty()
    {
        if (isFullyAsembled || currentBridgeBuildingStageIndex < 0 || currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length)
        {
            return;
        }

        if (!ValidateStageComponentIndexes(currentBridgeBuildingStageIndex))
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

        if (!ValidateStageComponentIndexes(currentBridgeBuildingStageIndex))
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
            TryMountBridgeComponentLocal(bridgeComponent, heldComponent, false);
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            TryMountBridgeComponentServer(NetworkManager.Singleton.LocalClientId, bridgeComponent.ComponentID, heldComponent.NetworkObjectId);
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
            ObserveConstructionStage(bridgeComponent.ComponentID);
            return;
        }

        if (equippableItemSO == null)
        {
            bridgeComponent.NotifyEquippedItemTypeNeeded();
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            TryAssembleBridgeComponentServer(NetworkManager.Singleton.LocalClientId, bridgeComponent.ComponentID, equippableItemSO.itemType);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(AssembleRequestMessageSize, Allocator.Temp);
        writer.WriteValueSafe(bridgeComponent.ComponentID);
        writer.WriteValueSafe((int)equippableItemSO.itemType);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(RequestAssembleMessageName, NetworkManager.ServerClientId, writer);
    }

    public void RequestConstructionSiteWork(BridgeComponent bridgeComponent, EquippableItemSO equippableItemSO, float workPower, int workPointId = -1)
    {
        if (bridgeComponent == null || equippableItemSO == null || workPower <= 0f)
        {
            return;
        }

        if (!IsNetworkSessionActive())
        {
            if (bridgeComponent.ConstructionSite != null && bridgeComponent.ConstructionSite.TryApplyToolWork(equippableItemSO.itemType, workPower, workPointId))
            {
                ObserveConstructionStage(bridgeComponent.ComponentID);
                NotifyBridgeRequirementsChanged();
            }
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            TryApplyConstructionWorkServer(NetworkManager.Singleton.LocalClientId, bridgeComponent.ComponentID, equippableItemSO.itemType, workPointId);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(ConstructionWorkRequestMessageSize, Allocator.Temp);
        writer.WriteValueSafe(bridgeComponent.ComponentID);
        writer.WriteValueSafe((int)equippableItemSO.itemType);
        writer.WriteValueSafe(workPointId);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(RequestConstructionWorkMessageName, NetworkManager.ServerClientId, writer);
    }

    public void NotifyConstructionSiteStateChanged(BridgeConstructionSite constructionSite)
    {
        if (constructionSite == null || !constructionSite.TryGetComponent(out BridgeComponent bridgeComponent))
        {
            return;
        }

        bridgeComponent.RefreshVisualAndColliderState();
        if (!IsNetworkSessionActive())
        {
            ObserveConstructionStage(bridgeComponent.ComponentID);
            NotifyBridgeRequirementsChanged();
            return;
        }

        if (!NetworkManager.Singleton.IsServer || !TryGetStateIndex(bridgeComponent.ComponentID, out int stateIndex))
        {
            return;
        }

        BridgeComponentNetworkState state = bridgeComponentStates[stateIndex];
        constructionSite.PopulateNetworkState(ref state);
        bridgeComponentStates[stateIndex] = state;
        ApplyNetworkState(state);
        NotifyBridgeRequirementsChanged();
        BroadcastBridgeState();
    }

    private void HandleRequestConstructionWorkMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int componentID);
        reader.ReadValueSafe(out int toolTypeValue);
        reader.ReadValueSafe(out int workPointId);
        TryApplyConstructionWorkServer(senderClientId, componentID, (EquippableItemType)toolTypeValue, workPointId);
    }

    private void TryApplyConstructionWorkServer(ulong senderClientId, int componentID, EquippableItemType requestedToolType, int workPointId)
    {
        if (!NetworkManager.Singleton.IsServer ||
            !TryGetBridgeComponent(componentID, out BridgeComponent bridgeComponent) ||
            bridgeComponent.ConstructionSite == null ||
            !TryGetValidatedPlayerTool(senderClientId, bridgeComponent.ConstructionSite.GetWorkValidationTarget(workPointId), requestedToolType, out EquippableItemSO selectedTool))
        {
            return;
        }

        if (!bridgeComponent.ConstructionSite.TryApplyToolWork(selectedTool.itemType, selectedTool.ConstructionWorkPower, workPointId) ||
            !TryGetStateIndex(componentID, out int stateIndex))
        {
            return;
        }

        BridgeComponentNetworkState state = bridgeComponentStates[stateIndex];
        bridgeComponent.ConstructionSite.PopulateNetworkState(ref state);
        state.isAssembled = bridgeComponent.IsAssembled || bridgeComponent.ConstructionSite.CurrentStage == BridgeConstructionStage.Complete;
        if (state.isAssembled)
        {
            state.currentAssemblingProgress = bridgeComponent.GetAssemblingProgressNeeded();
        }
        bridgeComponentStates[stateIndex] = state;
        ApplyNetworkState(state);
        CheckCurrentStageMountingProgress();
        NotifyBridgeRequirementsChanged();
        BroadcastBridgeState();
    }

    private bool TryGetValidatedPlayerTool(
        ulong senderClientId,
        MonoBehaviour target,
        EquippableItemType requestedToolType,
        out EquippableItemSO selectedTool)
    {
        selectedTool = null;
        if (target == null ||
            !NetworkManager.Singleton.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client) ||
            client.PlayerObject == null)
        {
            return false;
        }

        PlayerInventory inventory = client.PlayerObject.GetComponent<PlayerInventory>();
        PlayerHealth health = client.PlayerObject.GetComponent<PlayerHealth>();
        selectedTool = inventory != null ? inventory.GetSelectedItemForServerValidation() : null;
        if (selectedTool == null || selectedTool.itemType != requestedToolType || (health != null && health.IsDowned))
        {
            return false;
        }

        PlayerActionController actionController = client.PlayerObject.GetComponent<PlayerActionController>();
        return actionController != null && actionController.CanPerformServerValidatedActionOn(target, selectedTool);
    }

    private void TryMountBridgeComponentLocal(
        BridgeComponent bridgeComponent,
        MountableBridgeComponent heldComponent,
        bool allowMountSocket)
    {
        if ((!allowMountSocket && bridgeComponent.MountSocket != null)
            || !CanMountBridgeComponent(bridgeComponent, heldComponent)
            || !IsValidComponentDataIndex(bridgeComponent.ComponentID))
        {
            return;
        }

        bridgeComponent.ApplyMountedState();
        heldComponent.RemoveFromWorld();
        bridgeComponentDataArray[bridgeComponent.ComponentID].isMounted = true;
        if (!bridgeComponent.NeedAssembling)
        {
            bridgeComponentDataArray[bridgeComponent.ComponentID].isAssembled = true;
        }
        NotifyBridgeRequirementsChanged();
    }

    private void HandleRequestMountMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int componentID);
        reader.ReadValueSafe(out ulong mountableComponentNetworkObjectId);
        TryMountBridgeComponentServer(senderClientId, componentID, mountableComponentNetworkObjectId);
    }

    private void TryMountBridgeComponentServer(ulong senderClientId, int componentID, ulong mountableComponentNetworkObjectId)
    {
        if (!NetworkManager.Singleton.IsServer || !TryGetBridgeComponent(componentID, out BridgeComponent bridgeComponent))
        {
            return;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(mountableComponentNetworkObjectId, out NetworkObject mountableNetworkObject))
        {
            return;
        }

        if (!mountableNetworkObject.TryGetComponent(out MountableBridgeComponent heldComponent)
            || bridgeComponent.MountSocket != null
            || !heldComponent.IsHeldBy(senderClientId)
            || !CanMountBridgeComponent(bridgeComponent, heldComponent))
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
        if (bridgeComponent.ConstructionSite != null)
        {
            bridgeComponent.ConstructionSite.NotifyMounted();
            bridgeComponent.ConstructionSite.PopulateNetworkState(ref state);
        }
        if (!bridgeComponent.NeedAssembling)
        {
            state.isAssembled = true;
            state.currentAssemblingProgress = bridgeComponent.GetAssemblingProgressNeeded();
        }

        bridgeComponentStates[stateIndex] = state;
        ApplyNetworkState(state);
        CheckCurrentStageMountingProgress();
        NotifyBridgeRequirementsChanged();
        BroadcastBridgeState();
    }

    private bool CanMountBridgeComponent(BridgeComponent bridgeComponent, MountableBridgeComponent heldComponent)
    {
        return bridgeComponent.CanBeMounted
            && !bridgeComponent.IsMounted
            && heldComponent.GetMountableBridgeComponentSO() != null
            && heldComponent.GetMountableBridgeComponentSO().bridgeComponentSO == bridgeComponent.GetBridgeComponentSO();
    }

    public bool TryAutoMountBridgeComponent(BridgeComponent bridgeComponent, MountableBridgeComponent heldComponent)
    {
        if (bridgeComponent == null
            || heldComponent == null
            || bridgeComponent.MountSocket == null
            || !bridgeComponent.MountSocket.IsAuthoritativeCandidateReady(heldComponent)
            || !CanMountBridgeComponent(bridgeComponent, heldComponent))
        {
            return false;
        }

        if (!IsNetworkSessionActive())
        {
            heldComponent.PrepareForMountingRemoval();
            TryMountBridgeComponentLocal(bridgeComponent, heldComponent, true);
            return bridgeComponent.IsMounted;
        }

        if (!NetworkManager.Singleton.IsServer || !TryGetStateIndex(bridgeComponent.ComponentID, out int stateIndex))
        {
            return false;
        }

        heldComponent.PrepareForMountingRemoval();
        heldComponent.RemoveFromWorld();
        BridgeComponentNetworkState state = bridgeComponentStates[stateIndex];
        state.isMounted = true;
        state.canBeMounted = false;
        state.mountAlignmentState = (int)BridgeMountAlignmentState.Complete;
        state.mountAlignmentCandidateNetworkObjectId = BridgeMountSocket.NoCandidateNetworkObjectId;
        if (bridgeComponent.ConstructionSite != null)
        {
            bridgeComponent.ConstructionSite.NotifyMounted();
            bridgeComponent.ConstructionSite.PopulateNetworkState(ref state);
        }
        if (!bridgeComponent.NeedAssembling)
        {
            state.isAssembled = true;
            state.currentAssemblingProgress = bridgeComponent.GetAssemblingProgressNeeded();
        }

        bridgeComponentStates[stateIndex] = state;
        ApplyNetworkState(state);
        CheckCurrentStageMountingProgress();
        NotifyBridgeRequirementsChanged();
        BroadcastBridgeState();
        return true;
    }

    public void ReportMountAlignmentState(
        BridgeComponent bridgeComponent,
        BridgeMountAlignmentState alignmentState,
        ulong candidateNetworkObjectId,
        double startedAt)
    {
        if (bridgeComponent == null || !IsNetworkSessionActive() || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        InitializeServerBridgeState();
        if (!TryGetStateIndex(bridgeComponent.ComponentID, out int stateIndex))
        {
            return;
        }

        BridgeComponentNetworkState state = bridgeComponentStates[stateIndex];
        if (state.mountAlignmentState == (int)alignmentState
            && state.mountAlignmentCandidateNetworkObjectId == candidateNetworkObjectId
            && Math.Abs(state.mountAlignmentStartedAt - startedAt) < 0.0001d)
        {
            return;
        }

        state.mountAlignmentState = (int)alignmentState;
        state.mountAlignmentCandidateNetworkObjectId = candidateNetworkObjectId;
        state.mountAlignmentStartedAt = startedAt;
        bridgeComponentStates[stateIndex] = state;
        BroadcastBridgeState();
    }

    private void HandleRequestAssembleMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int componentID);
        reader.ReadValueSafe(out int equippableItemTypeValue);
        TryAssembleBridgeComponentServer(senderClientId, componentID, (EquippableItemType)equippableItemTypeValue);
    }

    private void TryAssembleBridgeComponentServer(ulong senderClientId, int componentID, EquippableItemType equippableItemType)
    {
        if (!NetworkManager.Singleton.IsServer ||
            !TryGetBridgeComponent(componentID, out BridgeComponent bridgeComponent) ||
            !TryGetValidatedPlayerTool(senderClientId, bridgeComponent, equippableItemType, out EquippableItemSO selectedTool))
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
        state.currentAssemblingProgress = Mathf.Clamp(
            state.currentAssemblingProgress + selectedTool.ConstructionWorkPower,
            0f,
            bridgeComponent.GetAssemblingProgressNeeded());
        if (bridgeComponent.ConstructionSite != null)
        {
            state.constructionStage = (int)BridgeConstructionStage.Hammering;
            state.constructionProgress = state.currentAssemblingProgress;
        }
        if (state.currentAssemblingProgress >= bridgeComponent.GetAssemblingProgressNeeded())
        {
            state.isAssembled = true;
            state.constructionStage = (int)BridgeConstructionStage.Complete;
            state.constructionProgress = bridgeComponent.GetAssemblingProgressNeeded();
        }

        bridgeComponentStates[stateIndex] = state;
        ApplyNetworkState(state);
        CheckCurrentStageMountingProgress();
        NotifyBridgeRequirementsChanged();
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
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(
            StateSyncMessageName,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void SendBridgeState(ulong clientId)
    {
        using FastBufferWriter writer = CreateBridgeStateWriter();
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            StateSyncMessageName,
            clientId,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
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
        bool suppressStageEvents = !hasAppliedInitialNetworkBridgeState;

        for (int i = 0; i < stateCount; i++)
        {
            BridgeComponentNetworkState state = ReadState(reader);
            bridgeComponentStates.Add(state);
            ApplyNetworkState(state, suppressStageEvents);
        }

        hasAppliedInitialNetworkBridgeState = true;
        NotifyBridgeRequirementsChanged();
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
        writer.WriteValueSafe(state.constructionStage);
        writer.WriteValueSafe(state.constructionProgress);
        writer.WriteValueSafe(state.constructionValueA);
        writer.WriteValueSafe(state.constructionValueB);
        writer.WriteValueSafe(state.constructionAnchor0);
        writer.WriteValueSafe(state.constructionAnchor1);
        writer.WriteValueSafe(state.constructionAnchor2);
        writer.WriteValueSafe(state.constructionAnchor3);
        writer.WriteValueSafe(state.constructionAux0);
        writer.WriteValueSafe(state.constructionAux1);
        writer.WriteValueSafe(state.mountAlignmentState);
        writer.WriteValueSafe(state.mountAlignmentCandidateNetworkObjectId);
        writer.WriteValueSafe(state.mountAlignmentStartedAt);
    }

    private BridgeComponentNetworkState ReadState(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int componentID);
        reader.ReadValueSafe(out bool isMounted);
        reader.ReadValueSafe(out bool isAssembled);
        reader.ReadValueSafe(out bool canBeMounted);
        reader.ReadValueSafe(out float currentAssemblingProgress);
        reader.ReadValueSafe(out int constructionStage);
        reader.ReadValueSafe(out float constructionProgress);
        reader.ReadValueSafe(out int constructionValueA);
        reader.ReadValueSafe(out int constructionValueB);
        reader.ReadValueSafe(out float constructionAnchor0);
        reader.ReadValueSafe(out float constructionAnchor1);
        reader.ReadValueSafe(out float constructionAnchor2);
        reader.ReadValueSafe(out float constructionAnchor3);
        reader.ReadValueSafe(out float constructionAux0);
        reader.ReadValueSafe(out float constructionAux1);
        reader.ReadValueSafe(out int mountAlignmentState);
        reader.ReadValueSafe(out ulong mountAlignmentCandidateNetworkObjectId);
        reader.ReadValueSafe(out double mountAlignmentStartedAt);

        return new BridgeComponentNetworkState(componentID)
        {
            isMounted = isMounted,
            isAssembled = isAssembled,
            canBeMounted = canBeMounted,
            currentAssemblingProgress = currentAssemblingProgress,
            constructionStage = constructionStage,
            constructionProgress = constructionProgress,
            constructionValueA = constructionValueA,
            constructionValueB = constructionValueB,
            constructionAnchor0 = constructionAnchor0,
            constructionAnchor1 = constructionAnchor1,
            constructionAnchor2 = constructionAnchor2,
            constructionAnchor3 = constructionAnchor3,
            constructionAux0 = constructionAux0,
            constructionAux1 = constructionAux1,
            mountAlignmentState = mountAlignmentState,
            mountAlignmentCandidateNetworkObjectId = mountAlignmentCandidateNetworkObjectId,
            mountAlignmentStartedAt = mountAlignmentStartedAt
        };
    }

    private void ApplyNetworkState(BridgeComponentNetworkState state, bool suppressStageEvent = false)
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

        ObserveConstructionStage(state.componentID, suppressStageEvent);
    }

    private void SeedObservedConstructionStages()
    {
        observedConstructionStages.Clear();
        reachedConstructionStages.Clear();
        if (bridgeComponents == null)
        {
            return;
        }

        foreach (BridgeComponent component in bridgeComponents)
        {
            if (component != null && component.ConstructionSite != null)
            {
                BridgeConstructionStage currentStage = component.ConstructionSite.CurrentStage;
                observedConstructionStages[component.ComponentID] = currentStage;
                RecordReachedConstructionStage(component.ComponentID, currentStage);
            }
        }
    }

    private void ObserveConstructionStage(int componentID, bool suppressEvent = false)
    {
        if (!TryGetBridgeComponent(componentID, out BridgeComponent component) || component.ConstructionSite == null)
        {
            return;
        }

        BridgeConstructionStage currentStage = component.ConstructionSite.CurrentStage;
        RecordReachedConstructionStage(componentID, currentStage);
        if (!observedConstructionStages.TryGetValue(componentID, out BridgeConstructionStage previousStage))
        {
            observedConstructionStages[componentID] = currentStage;
            return;
        }

        observedConstructionStages[componentID] = currentStage;
        if (!suppressEvent && previousStage != currentStage)
        {
            OnConstructionStageChanged?.Invoke(
                this,
                new BridgeConstructionStageChangedEventArgs(component, previousStage, currentStage));
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

    public bool HasReachedConstructionStage(
        BridgeComponentSO componentType,
        BridgeConstructionStage requiredStage,
        bool requireAll)
    {
        if (componentType == null)
        {
            return false;
        }

        CacheBridgeComponents();
        int matchingCount = 0;
        int reachedCount = 0;
        foreach (BridgeComponent component in bridgeComponents)
        {
            if (component == null || component.GetBridgeComponentSO() != componentType)
            {
                continue;
            }

            matchingCount++;
            BridgeConstructionSite site = component.ConstructionSite;
            bool hasReached = site != null
                && (site.CurrentStage == BridgeConstructionStage.Complete
                    || site.CurrentStage == requiredStage
                    || HasRecordedConstructionStage(component.ComponentID, requiredStage));
            if (hasReached)
            {
                reachedCount++;
            }
        }

        return matchingCount > 0 && (requireAll ? reachedCount == matchingCount : reachedCount > 0);
    }

    private void RecordReachedConstructionStage(int componentID, BridgeConstructionStage stage)
    {
        if (!reachedConstructionStages.TryGetValue(componentID, out HashSet<BridgeConstructionStage> stages))
        {
            stages = new HashSet<BridgeConstructionStage>();
            reachedConstructionStages[componentID] = stages;
        }

        stages.Add(stage);
    }

    private bool HasRecordedConstructionStage(int componentID, BridgeConstructionStage stage)
    {
        return reachedConstructionStages.TryGetValue(componentID, out HashSet<BridgeConstructionStage> stages)
            && stages.Contains(stage);
    }

    private void InvokeBridgeFullyAssembledOnce()
    {
        if (bridgeFullyAssembledEventInvoked)
        {
            return;
        }

        bridgeFullyAssembledEventInvoked = true;
        isFullyAsembled = true;
        NotifyBridgeRequirementsChanged();
        OnBridgeFullyAssembled?.Invoke(this, EventArgs.Empty);
    }

    public BridgeRequirementsSnapshot GetBridgeRequirementsSnapshot()
    {
        List<BridgeRequirementLine> currentStageRequirements = new List<BridgeRequirementLine>();
        List<BridgeRequirementLine> remainingStageRequirements = new List<BridgeRequirementLine>();

        if (isFullyAsembled || bridgeBuildingStages == null || currentBridgeBuildingStageIndex < 0 || currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length)
        {
            return new BridgeRequirementsSnapshot(currentBridgeBuildingStageIndex, isFullyAsembled, currentStageRequirements, remainingStageRequirements);
        }

        Dictionary<string, RequirementCounter> currentStageCounters = new Dictionary<string, RequirementCounter>();
        AddStageRequirements(currentBridgeBuildingStageIndex, currentStageCounters, includeMountedProgress: true);
        foreach (KeyValuePair<string, RequirementCounter> pair in currentStageCounters)
        {
            currentStageRequirements.Add(new BridgeRequirementLine(pair.Key, pair.Value.CurrentAmount, pair.Value.RequiredAmount));
        }
        currentStageRequirements.Sort((first, second) => string.Compare(first.ComponentName, second.ComponentName, StringComparison.Ordinal));

        Dictionary<string, RequirementCounter> remainingStageCounters = new Dictionary<string, RequirementCounter>();
        for (int stageIndex = currentBridgeBuildingStageIndex + 1; stageIndex < bridgeBuildingStages.Length; stageIndex++)
        {
            AddStageRequirements(stageIndex, remainingStageCounters, includeMountedProgress: false);
        }

        foreach (KeyValuePair<string, RequirementCounter> pair in remainingStageCounters)
        {
            remainingStageRequirements.Add(new BridgeRequirementLine(pair.Key, 0, pair.Value.RequiredAmount));
        }
        remainingStageRequirements.Sort((first, second) => string.Compare(first.ComponentName, second.ComponentName, StringComparison.Ordinal));

        return new BridgeRequirementsSnapshot(currentBridgeBuildingStageIndex, false, currentStageRequirements, remainingStageRequirements);
    }

    private void AddStageRequirements(int stageIndex, Dictionary<string, RequirementCounter> counters, bool includeMountedProgress)
    {
        if (stageIndex < 0 || stageIndex >= bridgeBuildingStages.Length || bridgeBuildingStages[stageIndex].bridgeComponentDataIndexes == null)
        {
            return;
        }

        foreach (int componentIndex in bridgeBuildingStages[stageIndex].bridgeComponentDataIndexes)
        {
            if (componentIndex < 0 || componentIndex >= bridgeComponentDataArray.Length)
            {
                continue;
            }

            BridgeComponentData componentData = bridgeComponentDataArray[componentIndex];
            string componentName = componentData.bridgeComponentSO != null && !string.IsNullOrWhiteSpace(componentData.bridgeComponentSO.componentName)
                ? componentData.bridgeComponentSO.componentName
                : $"Bridge Component {componentIndex}";

            counters.TryGetValue(componentName, out RequirementCounter counter);
            counter.RequiredAmount++;
            if (includeMountedProgress && componentData.isMounted)
            {
                counter.CurrentAmount++;
            }

            counters[componentName] = counter;
        }
    }

    private bool ValidateStageComponentIndexes(int stageIndex)
    {
        if (bridgeBuildingStages == null || stageIndex < 0 || stageIndex >= bridgeBuildingStages.Length)
        {
            return false;
        }

        int[] componentIndexes = bridgeBuildingStages[stageIndex].bridgeComponentDataIndexes;
        if (componentIndexes == null)
        {
            return true;
        }

        bool isValid = true;
        foreach (int componentIndex in componentIndexes)
        {
            if (IsValidComponentDataIndex(componentIndex))
            {
                continue;
            }

            isValid = false;
            int reportKey = (stageIndex * 397) ^ componentIndex;
            if (reportedInvalidStageComponentIndexes.Add(reportKey))
            {
                Debug.LogError(
                    $"Bridge stage {stageIndex} references component index {componentIndex}, but bridgeComponentDataArray contains {bridgeComponentDataArray?.Length ?? 0} entries. " +
                    "Add the missing component data and holder, or remove the index from this stage.",
                    this);
            }
        }

        return isValid;
    }

    private bool IsValidComponentDataIndex(int componentIndex)
    {
        return bridgeComponentDataArray != null && componentIndex >= 0 && componentIndex < bridgeComponentDataArray.Length;
    }

    private void NotifyBridgeRequirementsChanged()
    {
        OnBridgeRequirementsChanged?.Invoke(this, EventArgs.Empty);
    }

    private struct RequirementCounter
    {
        public int CurrentAmount;
        public int RequiredAmount;
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
