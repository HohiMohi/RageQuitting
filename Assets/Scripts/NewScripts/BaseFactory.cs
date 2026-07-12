using System;
using Unity.Netcode;
using UnityEngine;

public class BaseFactory : NetworkBehaviour, IInteractableNew
{
    [SerializeField] protected FactoryInteractionUI factoryInteractionUI;

    [Header("Temp Value")]
    public GameObject InteractionOutlineGameobject;

    public EventHandler OnInteract;
    public EventHandler OnMountableBridgeComponentSOSelected;
    public class OnMountableBridgeComponentSOEventArgs : EventArgs
    {
        public BridgeComponentSO selectedBridgeComponentSO;
    }

    public static EventHandler OnInteractBaseFactory;
    [SerializeField] protected MountableBridgeComponentSO[] mountableBridgeComponentSOArray;
    [SerializeField] protected MountableBridgeComponentSO currentlySelectedMountableBridgeComponentSO;
    [SerializeField] protected BaseStorageNew baseStorageNew;
    [SerializeField] protected Transform mountableBridgeComponentSpawnPoint;
    [SerializeField] protected SpriteRenderer bridgeComponentSpriteRenderer;

    public virtual void Interact(Transform interactor)
    {
        Debug.Log("Base Factory");
        // Change for supporting choice of mountableBridgeComponentSO from Array
        OnInteract?.Invoke(this, EventArgs.Empty);
        OnInteractBaseFactory?.Invoke(this, EventArgs.Empty);
        /*
        if (CheckRequiredBaseResources(currentlySelectedMountableBridgeComponentSO))
        {
            RemoveBaseResourcesFromStorage(currentlySelectedMountableBridgeComponentSO);
            SpawnMountableBridgeComponent(currentlySelectedMountableBridgeComponentSO);
        }
        else
        {
            Debug.Log("There is not enough BaseResource in BaseStorageNew to produce this MountableBridgeComponent");
        }
        */
    }

    private void Awake()
    {
        InteractionOutlineGameobject.SetActive(false);
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        factoryInteractionUI.OnConfirmButtonClick += FactoryInteractionUI_OnBridgeComponentSelectionConfirm;
        InitializeStorageStorableResourcesList();
    }

    protected virtual void FactoryInteractionUI_OnBridgeComponentSelectionConfirm(object sender, FactoryInteractionUI.OnConfirmButtonClickEventArgs e)
    {
        bridgeComponentSpriteRenderer.sprite = e.mountableBridgeComponentSO.componentSprite;
        currentlySelectedMountableBridgeComponentSO = e.mountableBridgeComponentSO;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)
        {
            int mountableBridgeComponentSOIndex = GetMountableBridgeComponentSOIndex(currentlySelectedMountableBridgeComponentSO);
            if (mountableBridgeComponentSOIndex >= 0)
            {
                RequestProduceMountableBridgeComponentServerRpc(mountableBridgeComponentSOIndex);
            }
            return;
        }

        TryProduceMountableBridgeComponent(currentlySelectedMountableBridgeComponentSO);
    }

    private bool TryProduceMountableBridgeComponent(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        if (mountableBridgeComponentSO == null)
        {
            return false;
        }

        if (CheckRequiredBaseResources(mountableBridgeComponentSO))
        {
            RemoveBaseResourcesFromStorage(mountableBridgeComponentSO);
            SpawnMountableBridgeComponent(mountableBridgeComponentSO);
            return true;
        }

        Debug.Log("There is not enough BaseResource in BaseStorageNew to produce this MountableBridgeComponent");
        return false;
    }
    
    // Update is called once per frame
    void Update()
    {

    }

    public GameObject SpawnMountableBridgeComponent(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                return SpawnNetworkMountableBridgeComponent(mountableBridgeComponentSO);
            }

            int mountableBridgeComponentSOIndex = GetMountableBridgeComponentSOIndex(mountableBridgeComponentSO);
            if (mountableBridgeComponentSOIndex >= 0)
            {
                RequestSpawnMountableBridgeComponentServerRpc(mountableBridgeComponentSOIndex);
            }
            else
            {
                Debug.LogWarning($"{mountableBridgeComponentSO.name} is not registered in this factory.");
            }

            return null;
        }

        GameObject spawnedGameObject = Instantiate<GameObject>(mountableBridgeComponentSO.inGameGameObjectPrefab, mountableBridgeComponentSpawnPoint.position, Quaternion.identity);
        return spawnedGameObject;
    }

    private GameObject SpawnNetworkMountableBridgeComponent(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        GameObject networkSpawnedGameObject = Instantiate(mountableBridgeComponentSO.inGameGameObjectPrefab, mountableBridgeComponentSpawnPoint.position, Quaternion.identity);
        if (networkSpawnedGameObject.TryGetComponent(out NetworkObject networkObject))
        {
            networkObject.Spawn(true);
        }
        else
        {
            Debug.LogError($"{networkSpawnedGameObject.name} is missing NetworkObject and cannot be synchronized.");
        }

        return networkSpawnedGameObject;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSpawnMountableBridgeComponentServerRpc(int mountableBridgeComponentSOIndex)
    {
        if (mountableBridgeComponentSOIndex < 0 || mountableBridgeComponentSOIndex >= mountableBridgeComponentSOArray.Length)
        {
            return;
        }

        SpawnNetworkMountableBridgeComponent(mountableBridgeComponentSOArray[mountableBridgeComponentSOIndex]);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestProduceMountableBridgeComponentServerRpc(int mountableBridgeComponentSOIndex)
    {
        if (mountableBridgeComponentSOIndex < 0 || mountableBridgeComponentSOIndex >= mountableBridgeComponentSOArray.Length)
        {
            return;
        }

        TryProduceMountableBridgeComponent(mountableBridgeComponentSOArray[mountableBridgeComponentSOIndex]);
    }

    private int GetMountableBridgeComponentSOIndex(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        for (int i = 0; i < mountableBridgeComponentSOArray.Length; i++)
        {
            if (mountableBridgeComponentSOArray[i] == mountableBridgeComponentSO)
            {
                return i;
            }
        }

        return -1;
    }

    public bool CheckRequiredBaseResources(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        // Check if there are enough each BaseResourceSO - if not, return false
        foreach (RequiredResource requiredResource in mountableBridgeComponentSO.requiredResources)
        {
            if (baseStorageNew.CheckBaseResourceAmount(requiredResource.resourceType) < requiredResource.amount || baseStorageNew.CheckBaseResourceAmount(requiredResource.resourceType) == -1)
            {
                return false;
            }
        }
        return true;
    }

    public void RemoveBaseResourcesFromStorage(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        foreach (RequiredResource requiredResource in mountableBridgeComponentSO.requiredResources)
        {
            baseStorageNew.RemoveBaseResourceAmount(requiredResource.resourceType, requiredResource.amount);
        }
    }

    public MountableBridgeComponentSO[] GetMountableBridgeComponentSOArray()
    {
        return mountableBridgeComponentSOArray;
    }

    public void InitializeStorageStorableResourcesList()
    {
        foreach (MountableBridgeComponentSO mountableBridgeComponentSO in mountableBridgeComponentSOArray)
        {
            foreach(RequiredResource requiredResource in mountableBridgeComponentSO.requiredResources)
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
}
