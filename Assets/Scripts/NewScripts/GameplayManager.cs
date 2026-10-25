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
    public readonly string Details;
    public readonly bool IsPercentage;

    public BridgeRequirementLine(string componentName, int currentAmount, int requiredAmount, string details = null, bool isPercentage = false)
    {
        ComponentName = componentName;
        CurrentAmount = currentAmount;
        RequiredAmount = requiredAmount;
        Details = details ?? string.Empty;
        IsPercentage = isPercentage;
    }
}

public readonly struct OrderedBridgeComponentRequirement
{
    public readonly BridgeComponentSO Component;
    public readonly int RequiredCount;

    public OrderedBridgeComponentRequirement(BridgeComponentSO component, int requiredCount)
    {
        Component = component;
        RequiredCount = requiredCount;
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
    private const string RequestLevelingConfirmationMessageName = "GameplayManager_RequestLevelingConfirmation";
    private const string GirderFasteningWindowMessageName = "GameplayManager_GirderFasteningWindow";
    private const string StateSyncMessageName = "GameplayManager_BridgeState";
    private const int StateMessageBaseSize = sizeof(int) * 2 + sizeof(bool);
    private const int StateMessageItemSize = sizeof(int) * 5 + sizeof(bool) * 3 + sizeof(float) * 8 + sizeof(ulong) + sizeof(double);
    private const int MountRequestMessageSize = sizeof(int) + sizeof(ulong);
    private const int AssembleRequestMessageSize = sizeof(int) * 2;
    private const int ConstructionWorkRequestMessageSize = sizeof(int) * 3;
    private const int LevelingConfirmationRequestMessageSize = sizeof(int) * 3;
    private const int GirderFasteningWindowMessageSize = sizeof(int) * 2 + sizeof(double);

    public static GameplayManager Instance { get; private set; }

    [SerializeField] private Bridge bridge;
    [SerializeField] private BridgeComponentData[] bridgeComponentDataArray;
    [SerializeField] private BridgeBuildingStage[] bridgeBuildingStages;
    [SerializeField] private int currentBridgeBuildingStageIndex;
    [SerializeField] private bool isFullyAsembled;

    [Header("Water Mechanics")]
    [SerializeField] private bool enableRiverBedResourceRemoval = true;
    [SerializeField] private bool enableUnsupportedWaterDowning = true;

    private readonly List<BridgeComponentNetworkState> bridgeComponentStates = new List<BridgeComponentNetworkState>();
    private readonly List<ConstructionBookController> constructionBooks = new List<ConstructionBookController>();
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
    private bool bridgeProgressInitialized;

    public EventHandler<BridgeComponentMountableStatusUpdateEventArgs> BridgeComponentMountableStatusUpdate;
    public class BridgeComponentMountableStatusUpdateEventArgs : EventArgs
    {
        public bool canBeMounted;
        public int componentID;
    }

    public bool IsFullyAssembled => isFullyAsembled;
    public int CurrentBridgeStageIndex => currentBridgeBuildingStageIndex;
    public bool EnableRiverBedResourceRemoval => enableRiverBedResourceRemoval;
    public bool EnableUnsupportedWaterDowning => enableUnsupportedWaterDowning;
    public event EventHandler OnBridgeFullyAssembled;
    public event EventHandler OnBridgeRequirementsChanged;
    public event EventHandler<BridgeConstructionStageChangedEventArgs> OnConstructionStageChanged;
    public event Action<int, int, double> OnLocalGirderFasteningWindowStarted;
    public event Action<ConstructionBookController> ConstructionBookAvailabilityChanged;
    public ConstructionBookController RegisteredConstructionBook => constructionBooks.Count > 0 ? constructionBooks[0] : null;

    public void RegisterConstructionBook(ConstructionBookController book)
    {
        if (book == null || !book.isActiveAndEnabled || constructionBooks.Contains(book)) return;
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening && !book.IsSpawned) return;
        constructionBooks.Add(book);
        book.NotifyGameplayManagerAvailable();
        if (constructionBooks.Count == 1) ConstructionBookAvailabilityChanged?.Invoke(book);
    }

    public void UnregisterConstructionBook(ConstructionBookController book)
    {
        if (book == null) return;
        bool wasAvailable = constructionBooks.Count > 0 && constructionBooks[0] == book;
        if (!constructionBooks.Remove(book) || !wasAvailable) return;
        while (constructionBooks.Count > 0 && constructionBooks[0] == null) constructionBooks.RemoveAt(0);
        ConstructionBookAvailabilityChanged?.Invoke(RegisteredConstructionBook);
    }

    public void GetCurrentStageComponentTypes(List<BridgeComponentSO> results)
    {
        if (results == null) return;
        results.Clear();
        if (isFullyAsembled || bridgeBuildingStages == null || bridgeComponentDataArray == null ||
            currentBridgeBuildingStageIndex < 0 || currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length) return;
        int[] indexes = bridgeBuildingStages[currentBridgeBuildingStageIndex].bridgeComponentDataIndexes;
        if (indexes == null) return;
        HashSet<BridgeComponentSO> emitted = new HashSet<BridgeComponentSO>();
        foreach (int index in indexes)
        {
            if (index < 0 || index >= bridgeComponentDataArray.Length) continue;
            BridgeComponentSO component = bridgeComponentDataArray[index].bridgeComponentSO;
            if (component != null && emitted.Add(component)) results.Add(component);
        }
    }

    public IReadOnlyList<OrderedBridgeComponentRequirement> GetOrderedBridgeComponentRequirements()
    {
        List<OrderedBridgeComponentRequirement> result = new List<OrderedBridgeComponentRequirement>();
        if (bridgeComponentDataArray == null)
        {
            return result;
        }

        Dictionary<BridgeComponentSO, int> counts = new Dictionary<BridgeComponentSO, int>();
        for (int i = 0; i < bridgeComponentDataArray.Length; i++)
        {
            BridgeComponentSO component = bridgeComponentDataArray[i].bridgeComponentSO;
            if (component != null)
            {
                counts[component] = counts.TryGetValue(component, out int count) ? count + 1 : 1;
            }
        }

        HashSet<BridgeComponentSO> emitted = new HashSet<BridgeComponentSO>();
        if (bridgeBuildingStages != null)
        {
            foreach (BridgeBuildingStage stage in bridgeBuildingStages)
            {
                if (stage.bridgeComponentDataIndexes == null) continue;
                foreach (int index in stage.bridgeComponentDataIndexes)
                {
                    if (index < 0 || index >= bridgeComponentDataArray.Length) continue;
                    BridgeComponentSO component = bridgeComponentDataArray[index].bridgeComponentSO;
                    if (component != null && emitted.Add(component))
                    {
                        result.Add(new OrderedBridgeComponentRequirement(component, counts[component]));
                    }
                }
            }
        }

        for (int i = 0; i < bridgeComponentDataArray.Length; i++)
        {
            BridgeComponentSO component = bridgeComponentDataArray[i].bridgeComponentSO;
            if (component != null && emitted.Add(component))
            {
                result.Add(new OrderedBridgeComponentRequirement(component, counts[component]));
            }
        }

        return result;
    }

    public int GetMountedBridgeComponentCount(BridgeComponentSO component)
    {
        if (component == null || bridgeComponentDataArray == null) return 0;
        int count = 0;
        for (int i = 0; i < bridgeComponentDataArray.Length; i++)
        {
            BridgeComponentData data = bridgeComponentDataArray[i];
            if (data.bridgeComponentSO == component && data.isMounted) count++;
        }
        return count;
    }

    public BridgeDeckAreaRequirement GetAreaRequirement(BridgeComponentSO componentType)
    {
        if (componentType == null) return null;
        CacheBridgeComponents();
        for (int i = 0; i < bridgeComponents.Length; i++)
            if (bridgeComponents[i] != null && bridgeComponents[i].GetBridgeComponentSO() == componentType
                && bridgeComponents[i].TryGetComponent(out BridgeDeckAreaRequirement area)) return area;
        return null;
    }

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

        foreach (ConstructionBookController book in FindObjectsOfType<ConstructionBookController>(true)) RegisterConstructionBook(book);

        EnsureBridgeReference();
        if (bridge == null)
        {
            Debug.LogError("GameplayManager: No Bridge reference found in scene. Bridge gameplay state will not be initialized.");
            yield break;
        }

        CacheBridgeComponents();
        if (!IsNetworkSessionActive() || NetworkManager.Singleton.IsServer)
        {
            ApplyConfiguredInitialBridgeState();
            AdvancePastInitiallyCompletedStages();
            if (!IsNetworkSessionActive()) bridgeProgressInitialized = true;
        }
        ReconcileInitialPhysicalDeckPanels();
        SeedObservedConstructionStages();
        SubscribeBridgeComponentEvents();
        yield return null;

        if (IsNetworkSessionActive())
        {
            EnsureNetworkMessagingRegistered();
            if (NetworkManager.Singleton.IsServer)
            {
                InitializeServerBridgeState();
                bridgeProgressInitialized = true;
                ReconcileInitialPhysicalDeckPanels();
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
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(RequestLevelingConfirmationMessageName);
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GirderFasteningWindowMessageName);
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
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
            GirderFasteningWindowMessageName,
            HandleGirderFasteningWindowMessage);
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(RequestFullStateMessageName, HandleRequestFullBridgeStateMessage);
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(RequestMountMessageName, HandleRequestMountMessage);
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(RequestAssembleMessageName, HandleRequestAssembleMessage);
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(RequestConstructionWorkMessageName, HandleRequestConstructionWorkMessage);
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(RequestLevelingConfirmationMessageName, HandleRequestLevelingConfirmationMessage);
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

    private void ApplyConfiguredInitialBridgeState()
    {
        if (bridgeComponents == null)
        {
            return;
        }

        foreach (BridgeComponent component in bridgeComponents)
        {
            if (component == null)
            {
                continue;
            }

            int componentID = component.ComponentID;
            if (IsValidComponentDataIndex(componentID))
            {
                bridgeComponentDataArray[componentID].isMounted = false;
                bridgeComponentDataArray[componentID].isAssembled = false;
                bridgeComponentDataArray[componentID].CanBeMounted = false;
            }

            if (!component.StartFullyCompleted)
            {
                continue;
            }

            component.ApplyInitialCompletedState();
            if (IsValidComponentDataIndex(componentID))
            {
                bridgeComponentDataArray[componentID].isMounted = true;
                bridgeComponentDataArray[componentID].isAssembled = true;
            }
        }
    }

    private void AdvancePastInitiallyCompletedStages()
    {
        if (bridgeBuildingStages == null || bridgeComponentDataArray == null)
        {
            return;
        }

        while (currentBridgeBuildingStageIndex >= 0 &&
               currentBridgeBuildingStageIndex < bridgeBuildingStages.Length &&
               IsStageFullyCompleted(currentBridgeBuildingStageIndex))
        {
            if (!FinalizeAreaRequirementsForStage(currentBridgeBuildingStageIndex)) break;
            currentBridgeBuildingStageIndex++;
        }

        if (currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length)
        {
            isFullyAsembled = true;
            bridgeFullyAssembledEventInvoked = true;
        }
    }

    private bool IsStageFullyCompleted(int stageIndex)
    {
        if (!ValidateStageComponentIndexes(stageIndex))
        {
            return false;
        }

        int[] componentIndexes = bridgeBuildingStages[stageIndex].bridgeComponentDataIndexes;
        if (componentIndexes == null)
        {
            return true;
        }

        foreach (int componentIndex in componentIndexes)
        {
            BridgeComponentData componentData = bridgeComponentDataArray[componentIndex];
            BridgeDeckAreaRequirement area = FindAreaRequirement(componentIndex);
            if (area != null)
            {
                if (!area.Reassess().IsComplete) return false;
            }
            else if (!componentData.isMounted || !componentData.isAssembled) return false;
        }

        return true;
    }

    private BridgeDeckAreaRequirement FindAreaRequirement(int componentId)
    {
        CacheBridgeComponents();
        for (int i = 0; i < bridgeComponents.Length; i++)
        {
            BridgeComponent component = bridgeComponents[i];
            if (component != null && component.ComponentID == componentId
                && component.TryGetComponent(out BridgeDeckAreaRequirement requirement)) return requirement;
        }
        return null;
    }

    private bool FinalizeAreaRequirementsForStage(int stageIndex)
    {
        if (!ValidateStageComponentIndexes(stageIndex)) return false;
        int[] indexes = bridgeBuildingStages[stageIndex].bridgeComponentDataIndexes;
        if (indexes == null) return true;
        List<BridgeDeckAreaRequirement> requirements = new();
        for (int i = 0; i < indexes.Length; i++)
        {
            BridgeDeckAreaRequirement area = FindAreaRequirement(indexes[i]);
            if (area == null) continue;
            if (!area.TryPrepareFinalization()) return false;
            requirements.Add(area);
        }
        int committedRequirements = 0;
        for (int i = 0; i < requirements.Count; i++)
        {
            if (!requirements[i].CommitFinalization())
            {
                for (int rollback = 0; rollback < committedRequirements; rollback++) requirements[rollback].RollbackFinalization();
                return false;
            }
            committedRequirements++;
        }
        for (int i = 0; i < indexes.Length; i++)
        {
            BridgeDeckAreaRequirement area = FindAreaRequirement(indexes[i]);
            if (area == null) continue;
            BridgeComponentData data = bridgeComponentDataArray[indexes[i]];
            data.isMounted = true;
            data.isAssembled = true;
            data.CanBeMounted = false;
            bridgeComponentDataArray[indexes[i]] = data;
            if (!IsNetworkSessionActive())
            {
                // The adapter represents logical stage completion but has no delivered stock
                // component to mount. Apply the event-free completed state so offline observers
                // see the same logical result without recursively advancing the active stage.
                BridgeComponent adapter = area.GetComponent<BridgeComponent>();
                if (adapter != null) adapter.ApplyInitialCompletedState();
            }
            if (IsNetworkSessionActive() && NetworkManager.Singleton.IsServer && TryGetStateIndex(indexes[i], out int stateIndex))
            {
                BridgeComponentNetworkState state = bridgeComponentStates[stateIndex];
                state.isMounted = true; state.isAssembled = true; state.canBeMounted = false;
                bridgeComponentStates[stateIndex] = state;
                ApplyNetworkState(state);
            }
        }
        return true;
    }

    public bool IsAreaRequirementActive(BridgeDeckAreaRequirement requirement)
    {
        if (requirement == null || isFullyAsembled || bridgeBuildingStages == null
            || currentBridgeBuildingStageIndex < 0 || currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length) return false;
        if (!bridgeProgressInitialized && !hasAppliedInitialNetworkBridgeState) return false;
        BridgeComponent component = requirement.GetComponent<BridgeComponent>();
        if (component == null || !TryGetBridgeComponent(component.ComponentID, out BridgeComponent owned) || owned != component) return false;
        int[] indexes = bridgeBuildingStages[currentBridgeBuildingStageIndex].bridgeComponentDataIndexes;
        if (indexes == null) return false;
        for (int i = 0; i < indexes.Length; i++) if (indexes[i] == component.ComponentID) return true;
        return false;
    }

    public bool IsAreaRequirementFuture(BridgeDeckAreaRequirement requirement)
    {
        if (requirement == null || bridgeBuildingStages == null) return false;
        BridgeComponent component = requirement.GetComponent<BridgeComponent>();
        if (component == null) return false;
        int current = currentBridgeBuildingStageIndex;
        for (int stage = Math.Max(0, current); stage < bridgeBuildingStages.Length; stage++)
        {
            int[] indexes = bridgeBuildingStages[stage].bridgeComponentDataIndexes;
            if (indexes == null) continue;
            for (int i = 0; i < indexes.Length; i++) if (indexes[i] == component.ComponentID) return stage > current;
        }
        return false;
    }

    public bool IsAreaRequirementPresented(BridgeDeckAreaRequirement requirement)
        => IsAreaRequirementActive(requirement) || IsAreaRequirementPast(requirement);

    public bool IsAreaRequirementPast(BridgeDeckAreaRequirement requirement)
    {
        if (requirement == null || bridgeBuildingStages == null) return false;
        BridgeComponent component = requirement.GetComponent<BridgeComponent>();
        if (component == null) return false;
        int upper = Math.Min(currentBridgeBuildingStageIndex, bridgeBuildingStages.Length);
        for (int stage = 0; stage < upper; stage++)
        {
            int[] indexes = bridgeBuildingStages[stage].bridgeComponentDataIndexes;
            if (indexes == null) continue;
            for (int i = 0; i < indexes.Length; i++) if (indexes[i] == component.ComponentID) return true;
        }
        return false;
    }

    internal void NotifyAreaRequirementChanged(BridgeDeckAreaRequirement requirement)
    {
        if (requirement == null || !bridgeProgressInitialized) return;
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && !manager.IsServer) return;
        BridgeComponent component = requirement.GetComponent<BridgeComponent>();
        if (component == null || !TryGetBridgeComponent(component.ComponentID, out BridgeComponent owned)
            || owned != component || requirement.DeckSection == null || !IsMainBridgeDeckSection(requirement.DeckSection)) return;
        int before = currentBridgeBuildingStageIndex;
        CheckCurrentStageMountingProgress();
        NotifyBridgeRequirementsChanged();
        if (manager != null && manager.IsListening && manager.IsServer
            && (before == currentBridgeBuildingStageIndex || isFullyAsembled))
            BroadcastBridgeState();
    }

    public void NotifyLocalAreaPresentationChanged()
    {
        OnBridgeRequirementsChanged?.Invoke(this, EventArgs.Empty);
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
            if (bridgeComponentDataArray[componentIndex].isMounted && bridgeComponentDataArray[componentIndex].isAssembled)
            {
                bridgeComponentDataArray[componentIndex].CanBeMounted = false;
                continue;
            }

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
        if (isFullyAsembled || bridgeBuildingStages == null || currentBridgeBuildingStageIndex < 0 || currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length)
        {
            return;
        }

        int previousStageIndex = currentBridgeBuildingStageIndex;
        while (currentBridgeBuildingStageIndex < bridgeBuildingStages.Length && IsStageFullyCompleted(currentBridgeBuildingStageIndex))
        {
            if (!FinalizeAreaRequirementsForStage(currentBridgeBuildingStageIndex)) break;
            currentBridgeBuildingStageIndex++;
        }

        if (currentBridgeBuildingStageIndex == previousStageIndex) return;

        if (currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length)
        {
            // The caller broadcasts the state after this method returns. Set the
            // terminal flag first so that snapshot carries the completed round.
            isFullyAsembled = true;
            InvokeBridgeFullyAssembledOnce();
            return;
        }

        UpdateComponentsCanBeMountedProperty();
        if (IsNetworkSessionActive() && NetworkManager.Singleton.IsServer)
        {
            BroadcastBridgeState();
        }
    }

    internal void RecordPhysicalDeckPanelCompletion(
        BridgeDeckPanelSectionNetworkState sectionState,
        BridgeDeckPanelConstructionSite site)
    {
        if (sectionState == null || site == null || !site.UsesPhysicalPanelPlacement
            || site.DeckSection == null || site.DeckSection.PhysicalPanelState != sectionState
            || !sectionState.IsCompletedSlot(site)) return;
        if (!bridgeProgressInitialized) return;

        NetworkManager activeNetworkManager = NetworkManager.Singleton;
        bool networkSession = IsNetworkSessionActive();
        if (networkSession)
        {
            if (activeNetworkManager == null || !activeNetworkManager.IsServer || !sectionState.IsSpawned
                || !sectionState.IsServer || sectionState.NetworkManager != activeNetworkManager) return;
        }
        else if (sectionState.IsSpawned) return;
        BridgeComponent component = site.GetComponent<BridgeComponent>();
        if (component == null || !TryGetBridgeComponent(component.ComponentID, out BridgeComponent ownedComponent)
            || ownedComponent != component || !IsMainBridgeDeckSection(site.DeckSection)) return;
        int componentID = component.ComponentID;
        if (!IsValidComponentDataIndex(componentID)) return;

        int stateIndex = -1;
        if (networkSession && !TryGetStateIndex(componentID, out stateIndex)) return;

        BridgeComponentData componentData = bridgeComponentDataArray[componentID];
        bool componentDataChanged = !componentData.isMounted || !componentData.isAssembled || componentData.CanBeMounted;
        bool siteChanged = site.CurrentStage != BridgeConstructionStage.Complete;
        bool networkStateChanged = false;
        BridgeComponentNetworkState state = new BridgeComponentNetworkState(componentID)
        {
            isMounted = true,
            isAssembled = true,
            canBeMounted = false,
            currentAssemblingProgress = component.GetAssemblingProgressNeeded()
        };
        if (networkSession)
        {
            state = bridgeComponentStates[stateIndex];
            networkStateChanged = !state.isMounted || !state.isAssembled || state.canBeMounted
                || state.currentAssemblingProgress < component.GetAssemblingProgressNeeded()
                || state.constructionStage != (int)BridgeConstructionStage.Complete;
        }
        bool stageProgressNeedsReconciliation = !isFullyAsembled && bridgeBuildingStages != null
            && currentBridgeBuildingStageIndex >= 0 && currentBridgeBuildingStageIndex < bridgeBuildingStages.Length
            && IsStageFullyCompleted(currentBridgeBuildingStageIndex);
        bool completionAlreadyApplied = !componentDataChanged && !siteChanged && !networkStateChanged
            && !stageProgressNeedsReconciliation;
        if (completionAlreadyApplied) return;

        componentData.isMounted = true;
        componentData.isAssembled = true;
        componentData.CanBeMounted = false;
        bridgeComponentDataArray[componentID] = componentData;
        site.ApplyPhysicalPanelCompletion();

        if (networkSession)
        {
            state.isMounted = true;
            state.isAssembled = true;
            state.canBeMounted = false;
            state.currentAssemblingProgress = component.GetAssemblingProgressNeeded();
        }
        site.PopulateNetworkState(ref state);
        state.isMounted = true;
        state.isAssembled = true;
        state.canBeMounted = false;

        if (networkSession)
        {
            bridgeComponentStates[stateIndex] = state;
        }
        ApplyNetworkState(state, suppressStageEvent: isReconcilingInitialPhysicalDeckPanels);
        int stageIndexBeforeCompletion = currentBridgeBuildingStageIndex;
        CheckCurrentStageMountingProgress();
        NotifyBridgeRequirementsChanged();
        if (networkSession && (currentBridgeBuildingStageIndex == stageIndexBeforeCompletion
            || bridgeBuildingStages == null || currentBridgeBuildingStageIndex >= bridgeBuildingStages.Length))
        {
            // CheckCurrentStageMountingProgress broadcasts a nonterminal stage advance itself.
            // Keep a single snapshot for unchanged progress and for terminal completion.
            BroadcastBridgeState();
        }
    }

    private bool isReconcilingInitialPhysicalDeckPanels;

    private void ReconcileInitialPhysicalDeckPanels()
    {
        isReconcilingInitialPhysicalDeckPanels = true;
        try
        {
            BridgeDeckPanelSectionNetworkState.ReconcileAllCompletedSlotsWithGameplayManager();
        }
        finally
        {
            isReconcilingInitialPhysicalDeckPanels = false;
        }
    }

    private bool IsMainBridgeDeckSection(BridgeDeckSection section)
    {
        if (bridge == null || section == null || section.MainGirders == null || section.MainGirders.Count == 0)
            return false;

        CacheBridgeComponents();
        bool hasMainGirder = false;
        foreach (BridgeComponent girder in section.MainGirders)
        {
            if (girder == null) continue;
            bool belongsToBridge = false;
            foreach (BridgeComponent bridgeComponent in bridgeComponents)
            {
                if (bridgeComponent != girder) continue;
                belongsToBridge = true;
                break;
            }
            if (!belongsToBridge) return false;
            hasMainGirder = true;
        }
        return hasMainGirder;
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
            if (bridgeComponent.ConstructionSite != null &&
                bridgeComponent.ConstructionSite.TryApplyToolWork(equippableItemSO.itemType, workPower, workPointId, 0ul))
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

    public void RequestLevelingConfirmation(
        BridgeComponent bridgeComponent,
        Transform interactor,
        LevelingConfirmationSourceType sourceType,
        int sourcePointId)
    {
        if (bridgeComponent == null || bridgeComponent.ConstructionSite == null || interactor == null)
        {
            return;
        }

        if (!IsNetworkSessionActive())
        {
            bool sourceValid = bridgeComponent.ConstructionSite.TryResolveLevelingConfirmationPoint(
                sourceType,
                sourcePointId,
                out Collider validationCollider);
            float distance = sourceValid
                ? Vector3.Distance(interactor.position, validationCollider.ClosestPoint(interactor.position))
                : float.PositiveInfinity;
            bool accepted = sourceValid && distance <= 3f && bridgeComponent.ConstructionSite.TryConfirmLeveling();
            LogLevelingConfirmation(bridgeComponent, sourceType, sourcePointId, distance, sourceValid, accepted);
            if (accepted)
            {
                ObserveConstructionStage(bridgeComponent.ComponentID);
                NotifyBridgeRequirementsChanged();
            }
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            TryConfirmLevelingServer(
                NetworkManager.Singleton.LocalClientId,
                bridgeComponent.ComponentID,
                sourceType,
                sourcePointId);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(LevelingConfirmationRequestMessageSize, Allocator.Temp);
        writer.WriteValueSafe(bridgeComponent.ComponentID);
        writer.WriteValueSafe((int)sourceType);
        writer.WriteValueSafe(sourcePointId);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            RequestLevelingConfirmationMessageName,
            NetworkManager.ServerClientId,
            writer);
    }

    private void HandleRequestLevelingConfirmationMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int componentID);
        reader.ReadValueSafe(out int sourceTypeValue);
        reader.ReadValueSafe(out int sourcePointId);
        TryConfirmLevelingServer(
            senderClientId,
            componentID,
            (LevelingConfirmationSourceType)sourceTypeValue,
            sourcePointId);
    }

    private void TryConfirmLevelingServer(
        ulong senderClientId,
        int componentID,
        LevelingConfirmationSourceType sourceType,
        int sourcePointId)
    {
        if (!NetworkManager.Singleton.IsServer ||
            !TryGetBridgeComponent(componentID, out BridgeComponent bridgeComponent) ||
            bridgeComponent.ConstructionSite == null ||
            !NetworkManager.Singleton.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client) ||
            client.PlayerObject == null)
        {
            return;
        }

        PlayerHealth health = client.PlayerObject.GetComponent<PlayerHealth>();
        Vector3 playerPosition = client.PlayerObject.transform.position;
        Collider validationCollider = null;
        bool sourceValid = System.Enum.IsDefined(typeof(LevelingConfirmationSourceType), sourceType) &&
                           bridgeComponent.ConstructionSite.TryResolveLevelingConfirmationPoint(
                               sourceType,
                               sourcePointId,
                               out validationCollider);
        float distance = sourceValid
            ? Vector3.Distance(playerPosition, validationCollider.ClosestPoint(playerPosition))
            : float.PositiveInfinity;
        if ((health != null && health.IsDowned) || !sourceValid || distance > 3f)
        {
            LogLevelingConfirmation(bridgeComponent, sourceType, sourcePointId, distance, sourceValid, false);
            return;
        }

        bool accepted = bridgeComponent.ConstructionSite.TryConfirmLeveling();
        LogLevelingConfirmation(bridgeComponent, sourceType, sourcePointId, distance, true, accepted);
        if (!accepted || !TryGetStateIndex(componentID, out int stateIndex)) return;

        BridgeComponentNetworkState state = bridgeComponentStates[stateIndex];
        bridgeComponent.ConstructionSite.PopulateNetworkState(ref state);
        bridgeComponentStates[stateIndex] = state;
        ApplyNetworkState(state);
        ObserveConstructionStage(componentID);
        NotifyBridgeRequirementsChanged();
        BroadcastBridgeState();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void LogLevelingConfirmation(
        BridgeComponent bridgeComponent,
        LevelingConfirmationSourceType sourceType,
        int sourcePointId,
        float distance,
        bool sourceValid,
        bool accepted)
    {
        BridgeConstructionSite site = bridgeComponent != null ? bridgeComponent.ConstructionSite : null;
        int lengthTilt = site is ILevelingMeasurementTarget target
            ? target.GetLogicalTilt(SpiritLevelMeasurementAxis.Length)
            : 0;
        int widthTilt = site is ILevelingMeasurementTarget widthTarget
            ? widthTarget.GetLogicalTilt(SpiritLevelMeasurementAxis.Width)
            : 0;
        string distanceText = float.IsPositiveInfinity(distance) ? "invalid" : distance.ToString("F2");
        Debug.Log($"[Leveling confirmation] component={bridgeComponent?.ComponentID}, source={sourceType}, " +
                  $"point={sourcePointId}, distance={distanceText}, sourceValid={sourceValid}, " +
                  $"tilt={lengthTilt}/{widthTilt}, accepted={accepted}");
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

        if (!bridgeComponent.ConstructionSite.TryApplyToolWork(
                selectedTool.itemType,
                selectedTool.ConstructionWorkPower,
                workPointId,
                senderClientId) ||
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

    public void NotifyGirderFasteningWindowStarted(
        ulong actorClientId,
        int componentID,
        int requiredWorkPointId,
        double deadline)
    {
        if (actorClientId == ulong.MaxValue)
        {
            return;
        }

        if (!IsNetworkSessionActive())
        {
            OnLocalGirderFasteningWindowStarted?.Invoke(componentID, requiredWorkPointId, deadline);
            return;
        }

        if (!NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (actorClientId == NetworkManager.Singleton.LocalClientId)
        {
            OnLocalGirderFasteningWindowStarted?.Invoke(componentID, requiredWorkPointId, deadline);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(GirderFasteningWindowMessageSize, Allocator.Temp);
        writer.WriteValueSafe(componentID);
        writer.WriteValueSafe(requiredWorkPointId);
        writer.WriteValueSafe(deadline);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GirderFasteningWindowMessageName,
            actorClientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    private void HandleGirderFasteningWindowMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (NetworkManager.Singleton != null && senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        reader.ReadValueSafe(out int componentID);
        reader.ReadValueSafe(out int requiredWorkPointId);
        reader.ReadValueSafe(out double deadline);
        OnLocalGirderFasteningWindowStarted?.Invoke(componentID, requiredWorkPointId, deadline);
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
        PlayerConcreteTrapController concreteTrap = client.PlayerObject.GetComponent<PlayerConcreteTrapController>();
        selectedTool = inventory != null ? inventory.GetSelectedItemForServerValidation() : null;
        if (selectedTool == null || selectedTool.itemType != requestedToolType || (health != null && health.IsDowned) ||
            concreteTrap != null && concreteTrap.IsTrapped)
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
        return !bridgeComponent.UsesPhysicalDeckPanelPlacement
            && !heldComponent.IsPhysicalDeckPanel
            && bridgeComponent.CanBeMounted
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
            bridgeComponent.GetComponent<BridgeDeckAreaRequirement>() != null ||
            !TryGetValidatedPlayerTool(senderClientId, bridgeComponent, equippableItemType, out EquippableItemSO selectedTool))
        {
            return;
        }

        if (!bridgeComponent.IsMounted || bridgeComponent.IsAssembled || !bridgeComponent.NeedAssembling ||
            !bridgeComponent.SupportsEquippableItemType(equippableItemType) ||
            (bridgeComponent.ConstructionSite != null && !bridgeComponent.ConstructionSite.AllowsStandardAssemblyWork))
        {
            return;
        }

        if (!TryGetStateIndex(componentID, out int stateIndex))
        {
            return;
        }

        BridgeComponentNetworkState state = bridgeComponentStates[stateIndex];
        if (bridgeComponent.ConstructionSite != null &&
            state.constructionStage != (int)BridgeConstructionStage.Hammering)
        {
            return;
        }

        state.currentAssemblingProgress = Mathf.Clamp(
            state.currentAssemblingProgress + selectedTool.ConstructionWorkPower,
            0f,
            bridgeComponent.GetAssemblingProgressNeeded());
        if (bridgeComponent.ConstructionSite != null)
        {
            state.constructionStage = (int)bridgeComponent.ConstructionSite.CurrentStage;
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
        writer.WriteValueSafe(currentBridgeBuildingStageIndex);
        writer.WriteValueSafe(isFullyAsembled);
        for (int i = 0; i < bridgeComponentStates.Count; i++)
        {
            WriteState(writer, bridgeComponentStates[i]);
        }

        return writer;
    }

    private void HandleBridgeStateMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (IsNetworkSessionActive() && senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        if (!reader.TryBeginRead(sizeof(int)))
        {
            return;
        }

        reader.ReadValueSafe(out int stateCount);
        if (stateCount < 0 ||
            stateCount > (int.MaxValue - StateMessageBaseSize) / StateMessageItemSize ||
            (bridgeComponentDataArray == null && stateCount > 0) ||
            (bridgeComponentDataArray != null && stateCount > bridgeComponentDataArray.Length) ||
            !reader.TryBeginRead(sizeof(int) + sizeof(bool) + stateCount * StateMessageItemSize))
        {
            return;
        }

        reader.ReadValueSafe(out int stageIndex);
        reader.ReadValueSafe(out bool bridgeComplete);
        if (stageIndex < 0 ||
            (bridgeBuildingStages != null && stageIndex > bridgeBuildingStages.Length) ||
            (!bridgeComplete && bridgeBuildingStages != null && bridgeBuildingStages.Length > 0 && stageIndex >= bridgeBuildingStages.Length))
        {
            return;
        }

        List<BridgeComponentNetworkState> receivedStates = new List<BridgeComponentNetworkState>(stateCount);
        for (int i = 0; i < stateCount; i++)
        {
            receivedStates.Add(ReadState(reader));
        }

        bool shouldRaiseCompletedEvent = bridgeComplete && !bridgeFullyAssembledEventInvoked;
        currentBridgeBuildingStageIndex = stageIndex;
        isFullyAsembled = bridgeComplete;
        bridgeFullyAssembledEventInvoked = bridgeComplete;
        bridgeComponentStates.Clear();
        bool suppressStageEvents = !hasAppliedInitialNetworkBridgeState;
        foreach (BridgeComponentNetworkState state in receivedStates)
        {
            bridgeComponentStates.Add(state);
            ApplyNetworkState(state, suppressStageEvents);
        }

        hasAppliedInitialNetworkBridgeState = true;
        NotifyBridgeRequirementsChanged();
        if (shouldRaiseCompletedEvent)
        {
            OnBridgeFullyAssembled?.Invoke(this, EventArgs.Empty);
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
            currentStageRequirements.Add(new BridgeRequirementLine(pair.Key, pair.Value.CurrentAmount, pair.Value.RequiredAmount, pair.Value.Details, pair.Value.IsPercentage));
        }
        currentStageRequirements.Sort((first, second) => string.Compare(first.ComponentName, second.ComponentName, StringComparison.Ordinal));

        Dictionary<string, RequirementCounter> remainingStageCounters = new Dictionary<string, RequirementCounter>();
        for (int stageIndex = currentBridgeBuildingStageIndex + 1; stageIndex < bridgeBuildingStages.Length; stageIndex++)
        {
            AddStageRequirements(stageIndex, remainingStageCounters, includeMountedProgress: true);
        }

        foreach (KeyValuePair<string, RequirementCounter> pair in remainingStageCounters)
        {
            remainingStageRequirements.Add(new BridgeRequirementLine(pair.Key, pair.Value.CurrentAmount, pair.Value.RequiredAmount));
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
            BridgeDeckAreaRequirement areaRequirement = FindAreaRequirement(componentIndex);
            if (areaRequirement != null)
            {
                // Do not expose future area work as global progress before its stage is unlocked.
                if (stageIndex > currentBridgeBuildingStageIndex) continue;
                if (stageIndex == currentBridgeBuildingStageIndex && IsAreaRequirementActive(areaRequirement))
                {
                    BridgeDeckAreaStageResult area = areaRequirement.Reassess();
                    float total = areaRequirement.Profile != null ? areaRequirement.Profile.areaLengthMeters : 6.56f;
                    counter.RequiredAmount = 100;
                    counter.IsPercentage = true;
                    counter.CurrentAmount = area.IsComplete ? 100
                        : Mathf.Clamp(Mathf.RoundToInt(area.SecuredLengthMeters / total * 100f), 0, 100);
                    counter.Details = $"Covered {area.CoveredLengthMeters:0.00} m; secured {area.SecuredLengthMeters:0.00} m; {BridgeDeckAreaRequirement.FormatGapGuidance(area)}";
                }
                else
                {
                    counter.RequiredAmount++;
                    // Physical progress is deliberately hidden until this stage activates.
                    counter.CurrentAmount = 0;
                    counter.Details = string.Empty;
                }
                counters[componentName] = counter;
                continue;
            }
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
        public string Details;
        public bool IsPercentage;
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

    public bool TryGetConstructionSite(int componentID, out BridgeConstructionSite constructionSite)
    {
        if (TryGetBridgeComponent(componentID, out BridgeComponent component) && component.ConstructionSite != null)
        {
            constructionSite = component.ConstructionSite;
            return true;
        }

        constructionSite = null;
        return false;
    }
}
