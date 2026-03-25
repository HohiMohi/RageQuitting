using System;
using System.Collections.Generic;
using UnityEngine;

public class BaseResourceSource : MonoBehaviour, IDamageable
{
    [SerializeField] private float durabilityMax = 100f;
    private float durabilityCurrent;
    [Header("Resource Generation")]
    [SerializeField] private BaseResourceSO spawnedResourceType;
    [SerializeField] private Transform baseResourceSpawnPoint;
    [SerializeField] private List<EquippableItemType> supportedEquippableItemTypeList;
    [SerializeField] private Vector3[] spawnedResourcesPositionVectorArray;

    public EventHandler<ResourceSourceDurabilityChangedEventArgs> ResourceSourceDurabilityChanged;
    public class ResourceSourceDurabilityChangedEventArgs : EventArgs
    {
        public float resourceDurability;
        public float resourceDurabilityNormalized;
    }

    private void Awake()
    {
        durabilityCurrent = durabilityMax;
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        float damageAmount;
        if (equippableItemSO != null)
        {
            damageAmount = equippableItemSO.damage;
            if (supportedEquippableItemTypeList.Contains(equippableItemSO.itemType))
            {
                Debug.Log("Tool supported");
                damageAmount *= 2;
            }
            else
            {
                Debug.Log("Unsupported tool type");
            }
        }
        else
            damageAmount = damage;

        durabilityCurrent -= damageAmount;
        if (durabilityCurrent <= 0f)
        {
            Debug.Log($"{spawnedResourceType.name} resource source destroyed. Resource spawned");
            // Here you would implement the logic to spawn the resource, e.g.:
            HandleSpawningResources();

            //Destroy the resource source object after spawning the resource
            Destroy(gameObject);

        }
        else
        {
            Debug.Log($"Resource source damaged! Current durability: {durabilityCurrent}");
        }

    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void DamageReceived(float damage)
    {
        throw new System.NotImplementedException();
    }

    private void HandleSpawningResources()
    {
        foreach(Vector3 positionVector in spawnedResourcesPositionVectorArray)
        {
            GameObject spawnedResource = Instantiate(spawnedResourceType.resourcePrefab, transform.position + positionVector, Quaternion.identity);
            spawnedResource.transform.Rotate(new Vector3(0, UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 1f)));
        }
    }
}
