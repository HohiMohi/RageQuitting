using System;
using Unity.Netcode;
using UnityEngine;

public enum FactoryProductionFailureReason
{
    None,
    AlreadyProducing,
    NoSelectedComponent,
    MissingResources,
    InvalidDimensions,
    InvalidComponent,
    SpawnFailed
}

public class BaseFactory : NetworkBehaviour, IInteractableNew
{
    [SerializeField] protected FactoryInteractionUI factoryInteractionUI;

    [Header("Temp Value")]
    public GameObject InteractionOutlineGameobject;

    [Header("Production")]
    [SerializeField] protected MountableBridgeComponentSO[] mountableBridgeComponentSOArray;
    [SerializeField] protected MountableBridgeComponentSO currentlySelectedMountableBridgeComponentSO;
    [SerializeField] protected BaseStorageNew baseStorageNew;
    [SerializeField] protected Transform mountableBridgeComponentSpawnPoint;
    [SerializeField] protected SpriteRenderer bridgeComponentSpriteRenderer;
    [SerializeField] private float productionDuration = 1f;
    [SerializeField] private int defaultSelectedComponentIndex = -1;

    private readonly NetworkVariable<int> selectedComponentIndexNetwork = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<bool> isProducingNetwork = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<float> productionProgressNetwork = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<int> lastFailureReasonNetwork = new NetworkVariable<int>((int)FactoryProductionFailureReason.None);

    private int localSelectedComponentIndex = -1;
    private bool localIsProducing;
    private float localProductionProgress;
    private FactoryProductionFailureReason localLastFailureReason = FactoryProductionFailureReason.None;
    private float productionEndTime;

    public EventHandler OnInteract;
    public EventHandler OnMountableBridgeComponentSOSelected;
    public EventHandler OnFactoryStateChanged;
    public EventHandler<ProductionFailedEventArgs> OnProductionFailed;

    public class OnMountableBridgeComponentSOEventArgs : EventArgs
    {
        public BridgeComponentSO selectedBridgeComponentSO;
    }

    public class ProductionFailedEventArgs : EventArgs
    {
        public FactoryProductionFailureReason reason;
    }

    public static EventHandler OnInteractBaseFactory;

    public BaseStorageNew Storage => baseStorageNew;
    public MountableBridgeComponentSO SelectedComponent => GetMountableBridgeComponentSOByIndex(SelectedComponentIndex);
    public bool IsProducing => IsNetworkSessionActive() ? isProducingNetwork.Value : localIsProducing;
    public float ProductionProgressNormalized => IsNetworkSessionActive() ? productionProgressNetwork.Value : localProductionProgress;
    public FactoryProductionFailureReason LastFailureReason => IsNetworkSessionActive()
        ? (FactoryProductionFailureReason)lastFailureReasonNetwork.Value
        : localLastFailureReason;

    protected int SelectedComponentIndex => IsNetworkSessionActive() ? selectedComponentIndexNetwork.Value : localSelectedComponentIndex;
    protected float ProductionDuration => Mathf.Max(0f, productionDuration);

    public virtual void Interact(Transform interactor)
    {
        Debug.Log("Base Factory");
        OnInteract?.Invoke(this, EventArgs.Empty);
        OnInteractBaseFactory?.Invoke(this, EventArgs.Empty);
    }

    private void Awake()
    {
        if (InteractionOutlineGameobject != null)
        {
            InteractionOutlineGameobject.SetActive(false);
        }
    }

    protected virtual void Start()
    {
        if (factoryInteractionUI != null)
        {
            factoryInteractionUI.OnConfirmButtonClick += FactoryInteractionUI_OnBridgeComponentSelectionConfirm;
        }

        InitializeStorageStorableResourcesList();
        InitializeDefaultSelection();
        UpdateCachedSelectedComponent();
        RefreshSelectedVisual();
    }

    private void InitializeDefaultSelection()
    {
        if (!IsValidComponentIndex(defaultSelectedComponentIndex) || SelectedComponentIndex >= 0)
        {
            return;
        }

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                selectedComponentIndexNetwork.Value = defaultSelectedComponentIndex;
            }

            return;
        }

        localSelectedComponentIndex = defaultSelectedComponentIndex;
    }

    public override void OnNetworkSpawn()
    {
        selectedComponentIndexNetwork.OnValueChanged += SelectedComponentIndexNetwork_OnValueChanged;
        isProducingNetwork.OnValueChanged += FactoryNetworkState_OnValueChanged;
        productionProgressNetwork.OnValueChanged += FactoryNetworkState_OnValueChanged;
        lastFailureReasonNetwork.OnValueChanged += FactoryFailureReason_OnValueChanged;

        UpdateCachedSelectedComponent();
        RefreshSelectedVisual();
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void OnNetworkDespawn()
    {
        selectedComponentIndexNetwork.OnValueChanged -= SelectedComponentIndexNetwork_OnValueChanged;
        isProducingNetwork.OnValueChanged -= FactoryNetworkState_OnValueChanged;
        productionProgressNetwork.OnValueChanged -= FactoryNetworkState_OnValueChanged;
        lastFailureReasonNetwork.OnValueChanged -= FactoryFailureReason_OnValueChanged;
    }

    protected virtual void Update()
    {
        if (!IsProducing)
        {
            return;
        }

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                UpdateProductionServer();
            }

            return;
        }

        UpdateProductionLocal();
    }

    protected virtual void FactoryInteractionUI_OnBridgeComponentSelectionConfirm(object sender, FactoryInteractionUI.OnConfirmButtonClickEventArgs e)
    {
        RequestSelectComponent(e.mountableBridgeComponentSO);
    }

    public void RequestSelectComponent(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        int mountableBridgeComponentSOIndex = GetMountableBridgeComponentSOIndex(mountableBridgeComponentSO);
        RequestSelectComponent(mountableBridgeComponentSOIndex);
    }

    public void RequestSelectComponent(int mountableBridgeComponentSOIndex)
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                TrySetSelectedComponentIndexServer(mountableBridgeComponentSOIndex);
            }
            else
            {
                RequestSelectComponentServerRpc(mountableBridgeComponentSOIndex);
            }

            return;
        }

        TrySetSelectedComponentIndexLocal(mountableBridgeComponentSOIndex);
    }

    public void RequestStartProduction()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                TryStartProductionServer();
            }
            else
            {
                RequestStartProductionServerRpc();
            }

            return;
        }

        TryStartProductionLocal();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSelectComponentServerRpc(int mountableBridgeComponentSOIndex)
    {
        TrySetSelectedComponentIndexServer(mountableBridgeComponentSOIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartProductionServerRpc()
    {
        TryStartProductionServer();
    }

    protected bool TryStartProductionServer()
    {
        if (!CanProduceSelectedComponentServer(out FactoryProductionFailureReason reason))
        {
            SetFailureReasonServer(reason);
            return false;
        }

        MountableBridgeComponentSO selectedComponent = SelectedComponent;
        if (!TryConsumeRequiredResources(selectedComponent))
        {
            SetFailureReasonServer(FactoryProductionFailureReason.MissingResources);
            return false;
        }

        BeginProductionServer();
        return true;
    }

    protected virtual bool CanProduceSelectedComponentServer(out FactoryProductionFailureReason reason)
    {
        return CanProduceSelectedComponentShared(out reason);
    }

    protected virtual bool CanProduceSelectedComponentLocal(out FactoryProductionFailureReason reason)
    {
        return CanProduceSelectedComponentShared(out reason);
    }

    protected virtual bool CanProduceAdditionalConditions(MountableBridgeComponentSO mountableBridgeComponentSO, out FactoryProductionFailureReason reason)
    {
        reason = FactoryProductionFailureReason.None;
        return true;
    }

    private bool CanProduceSelectedComponentShared(out FactoryProductionFailureReason reason)
    {
        reason = FactoryProductionFailureReason.None;
        MountableBridgeComponentSO selectedComponent = SelectedComponent;

        if (IsProducing)
        {
            reason = FactoryProductionFailureReason.AlreadyProducing;
            return false;
        }

        if (selectedComponent == null)
        {
            reason = FactoryProductionFailureReason.NoSelectedComponent;
            return false;
        }

        if (!CheckRequiredBaseResources(selectedComponent))
        {
            reason = FactoryProductionFailureReason.MissingResources;
            return false;
        }

        if (!CanProduceAdditionalConditions(selectedComponent, out reason))
        {
            return false;
        }

        return true;
    }

    protected void FinishProductionServer()
    {
        MountableBridgeComponentSO selectedComponent = SelectedComponent;
        bool spawned = SpawnMountableBridgeComponent(selectedComponent) != null;
        isProducingNetwork.Value = false;
        productionProgressNetwork.Value = 0f;
        productionEndTime = 0f;
        SetFailureReasonServer(spawned ? FactoryProductionFailureReason.None : FactoryProductionFailureReason.SpawnFailed);
    }

    protected void FinishProductionLocal()
    {
        MountableBridgeComponentSO selectedComponent = SelectedComponent;
        bool spawned = SpawnMountableBridgeComponent(selectedComponent) != null;
        localIsProducing = false;
        localProductionProgress = 0f;
        productionEndTime = 0f;
        SetFailureReasonLocal(spawned ? FactoryProductionFailureReason.None : FactoryProductionFailureReason.SpawnFailed);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void BeginManualProductionServer()
    {
        isProducingNetwork.Value = true;
        productionProgressNetwork.Value = 0f;
        productionEndTime = 0f;
        SetFailureReasonServer(FactoryProductionFailureReason.None);
    }

    protected void BeginManualProductionLocal()
    {
        localIsProducing = true;
        localProductionProgress = 0f;
        productionEndTime = 0f;
        SetFailureReasonLocal(FactoryProductionFailureReason.None);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void SetManualProductionProgressServer(float progressNormalized)
    {
        productionProgressNetwork.Value = Mathf.Clamp01(progressNormalized);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void SetManualProductionProgressLocal(float progressNormalized)
    {
        localProductionProgress = Mathf.Clamp01(progressNormalized);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void CancelProductionServer()
    {
        isProducingNetwork.Value = false;
        productionProgressNetwork.Value = 0f;
        productionEndTime = 0f;
        SetFailureReasonServer(FactoryProductionFailureReason.None);
    }

    protected void CancelProductionLocal()
    {
        localIsProducing = false;
        localProductionProgress = 0f;
        productionEndTime = 0f;
        SetFailureReasonLocal(FactoryProductionFailureReason.None);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public GameObject SpawnMountableBridgeComponent(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        if (mountableBridgeComponentSO == null || mountableBridgeComponentSO.inGameGameObjectPrefab == null)
        {
            return null;
        }

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                return SpawnNetworkMountableBridgeComponent(mountableBridgeComponentSO);
            }

            Debug.LogWarning("Clients cannot spawn mountable bridge components directly.");
            return null;
        }

        return Instantiate(mountableBridgeComponentSO.inGameGameObjectPrefab, GetSpawnPosition(), GetSpawnRotation());
    }

    protected virtual GameObject SpawnNetworkMountableBridgeComponent(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        GameObject networkSpawnedGameObject = Instantiate(mountableBridgeComponentSO.inGameGameObjectPrefab, GetSpawnPosition(), GetSpawnRotation());
        if (networkSpawnedGameObject.TryGetComponent(out NetworkObject networkObject))
        {
            networkObject.Spawn(true);
            return networkSpawnedGameObject;
        }

        Debug.LogError($"{networkSpawnedGameObject.name} is missing NetworkObject and cannot be synchronized.");
        Destroy(networkSpawnedGameObject);
        return null;
    }

    public bool CheckRequiredBaseResources(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        if (mountableBridgeComponentSO == null || baseStorageNew == null)
        {
            return false;
        }

        foreach (RequiredResource requiredResource in mountableBridgeComponentSO.requiredResources)
        {
            if (baseStorageNew.CheckBaseResourceAmount(requiredResource.resourceType) < requiredResource.amount)
            {
                return false;
            }
        }

        return true;
    }

    public void RemoveBaseResourcesFromStorage(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        TryConsumeRequiredResources(mountableBridgeComponentSO);
    }

    protected bool TryConsumeRequiredResources(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        if (!CheckRequiredBaseResources(mountableBridgeComponentSO))
        {
            return false;
        }

        int consumedCount = 0;
        foreach (RequiredResource requiredResource in mountableBridgeComponentSO.requiredResources)
        {
            if (!baseStorageNew.TryRemoveBaseResourceAmount(requiredResource.resourceType, requiredResource.amount))
            {
                Debug.LogWarning($"Failed to consume {requiredResource.resourceType.resourceName} for {mountableBridgeComponentSO.name}.");
                RollbackConsumedResources(mountableBridgeComponentSO, consumedCount);
                return false;
            }

            consumedCount++;
        }

        return true;
    }

    private void RollbackConsumedResources(MountableBridgeComponentSO mountableBridgeComponentSO, int consumedCount)
    {
        for (int i = 0; i < consumedCount; i++)
        {
            RequiredResource consumedResource = mountableBridgeComponentSO.requiredResources[i];
            baseStorageNew.StoreBaseResource(consumedResource.resourceType, consumedResource.amount);
        }
    }

    public MountableBridgeComponentSO[] GetMountableBridgeComponentSOArray()
    {
        return mountableBridgeComponentSOArray;
    }

    public int GetMountableBridgeComponentSOIndex(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        if (mountableBridgeComponentSOArray == null)
        {
            return -1;
        }

        for (int i = 0; i < mountableBridgeComponentSOArray.Length; i++)
        {
            if (mountableBridgeComponentSOArray[i] == mountableBridgeComponentSO)
            {
                return i;
            }
        }

        return -1;
    }

    public MountableBridgeComponentSO GetMountableBridgeComponentSOByIndex(int index)
    {
        if (mountableBridgeComponentSOArray == null || index < 0 || index >= mountableBridgeComponentSOArray.Length)
        {
            return null;
        }

        return mountableBridgeComponentSOArray[index];
    }

    public void InitializeStorageStorableResourcesList()
    {
        if (baseStorageNew == null || mountableBridgeComponentSOArray == null)
        {
            return;
        }

        foreach (MountableBridgeComponentSO mountableBridgeComponentSO in mountableBridgeComponentSOArray)
        {
            if (mountableBridgeComponentSO == null)
            {
                continue;
            }

            foreach (RequiredResource requiredResource in mountableBridgeComponentSO.requiredResources)
            {
                baseStorageNew.AddStorableBaseResource(requiredResource.resourceType);
            }
        }
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Base Factory");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Base Factory");
    }

    protected bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }

    protected virtual void HandleSelectedComponentChanged(MountableBridgeComponentSO selectedComponent)
    {
    }

    protected virtual Vector3 GetSpawnPosition()
    {
        return mountableBridgeComponentSpawnPoint != null ? mountableBridgeComponentSpawnPoint.position : transform.position + transform.forward;
    }

    protected virtual Quaternion GetSpawnRotation()
    {
        return mountableBridgeComponentSpawnPoint != null ? mountableBridgeComponentSpawnPoint.rotation : transform.rotation;
    }

    private bool TrySetSelectedComponentIndexServer(int mountableBridgeComponentSOIndex)
    {
        if (isProducingNetwork.Value)
        {
            SetFailureReasonServer(FactoryProductionFailureReason.AlreadyProducing);
            return false;
        }

        if (!IsValidComponentIndex(mountableBridgeComponentSOIndex))
        {
            SetFailureReasonServer(FactoryProductionFailureReason.InvalidComponent);
            return false;
        }

        selectedComponentIndexNetwork.Value = mountableBridgeComponentSOIndex;
        SetFailureReasonServer(FactoryProductionFailureReason.None);
        return true;
    }

    private bool TrySetSelectedComponentIndexLocal(int mountableBridgeComponentSOIndex)
    {
        if (localIsProducing)
        {
            SetFailureReasonLocal(FactoryProductionFailureReason.AlreadyProducing);
            return false;
        }

        if (!IsValidComponentIndex(mountableBridgeComponentSOIndex))
        {
            SetFailureReasonLocal(FactoryProductionFailureReason.InvalidComponent);
            return false;
        }

        localSelectedComponentIndex = mountableBridgeComponentSOIndex;
        UpdateCachedSelectedComponent();
        RefreshSelectedVisual();
        HandleSelectedComponentChanged(currentlySelectedMountableBridgeComponentSO);
        SetFailureReasonLocal(FactoryProductionFailureReason.None);
        OnMountableBridgeComponentSOSelected?.Invoke(this, EventArgs.Empty);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool TryStartProductionLocal()
    {
        if (!CanProduceSelectedComponentLocal(out FactoryProductionFailureReason reason))
        {
            SetFailureReasonLocal(reason);
            return false;
        }

        MountableBridgeComponentSO selectedComponent = SelectedComponent;
        if (!TryConsumeRequiredResources(selectedComponent))
        {
            SetFailureReasonLocal(FactoryProductionFailureReason.MissingResources);
            return false;
        }

        BeginProductionLocal();
        return true;
    }

    private void BeginProductionServer()
    {
        isProducingNetwork.Value = true;
        productionProgressNetwork.Value = 0f;
        productionEndTime = Time.time + ProductionDuration;
        SetFailureReasonServer(FactoryProductionFailureReason.None);

        if (ProductionDuration <= 0f)
        {
            FinishProductionServer();
        }
    }

    private void BeginProductionLocal()
    {
        localIsProducing = true;
        localProductionProgress = 0f;
        productionEndTime = Time.time + ProductionDuration;
        SetFailureReasonLocal(FactoryProductionFailureReason.None);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);

        if (ProductionDuration <= 0f)
        {
            FinishProductionLocal();
        }
    }

    private void UpdateProductionServer()
    {
        if (productionEndTime <= 0f)
        {
            return;
        }

        if (ProductionDuration <= 0f)
        {
            FinishProductionServer();
            return;
        }

        float remaining = Mathf.Max(0f, productionEndTime - Time.time);
        productionProgressNetwork.Value = Mathf.Clamp01(1f - remaining / ProductionDuration);
        if (Time.time >= productionEndTime)
        {
            FinishProductionServer();
        }
    }

    private void UpdateProductionLocal()
    {
        if (productionEndTime <= 0f)
        {
            return;
        }

        if (ProductionDuration <= 0f)
        {
            FinishProductionLocal();
            return;
        }

        float remaining = Mathf.Max(0f, productionEndTime - Time.time);
        localProductionProgress = Mathf.Clamp01(1f - remaining / ProductionDuration);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
        if (Time.time >= productionEndTime)
        {
            FinishProductionLocal();
        }
    }

    private void SelectedComponentIndexNetwork_OnValueChanged(int previousValue, int newValue)
    {
        UpdateCachedSelectedComponent();
        RefreshSelectedVisual();
        HandleSelectedComponentChanged(currentlySelectedMountableBridgeComponentSO);
        OnMountableBridgeComponentSOSelected?.Invoke(this, EventArgs.Empty);
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FactoryNetworkState_OnValueChanged<T>(T previousValue, T newValue)
    {
        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FactoryFailureReason_OnValueChanged(int previousValue, int newValue)
    {
        FactoryProductionFailureReason reason = (FactoryProductionFailureReason)newValue;
        if (reason != FactoryProductionFailureReason.None)
        {
            OnProductionFailed?.Invoke(this, new ProductionFailedEventArgs { reason = reason });
        }

        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetFailureReasonServer(FactoryProductionFailureReason reason)
    {
        lastFailureReasonNetwork.Value = (int)reason;
        if (reason != FactoryProductionFailureReason.None)
        {
            OnProductionFailed?.Invoke(this, new ProductionFailedEventArgs { reason = reason });
        }

        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetFailureReasonLocal(FactoryProductionFailureReason reason)
    {
        localLastFailureReason = reason;
        if (reason != FactoryProductionFailureReason.None)
        {
            OnProductionFailed?.Invoke(this, new ProductionFailedEventArgs { reason = reason });
        }

        OnFactoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsValidComponentIndex(int mountableBridgeComponentSOIndex)
    {
        return mountableBridgeComponentSOIndex >= 0
            && mountableBridgeComponentSOArray != null
            && mountableBridgeComponentSOIndex < mountableBridgeComponentSOArray.Length
            && mountableBridgeComponentSOArray[mountableBridgeComponentSOIndex] != null;
    }

    private void UpdateCachedSelectedComponent()
    {
        currentlySelectedMountableBridgeComponentSO = SelectedComponent;
    }

    private void RefreshSelectedVisual()
    {
        if (bridgeComponentSpriteRenderer != null)
        {
            bridgeComponentSpriteRenderer.sprite = currentlySelectedMountableBridgeComponentSO != null
                ? currentlySelectedMountableBridgeComponentSO.componentSprite
                : null;
        }
    }
}
