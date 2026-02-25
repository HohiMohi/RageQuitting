using UnityEngine;

public class BaseResourceSource : MonoBehaviour, IDamageableNew
{
    [SerializeField] private float durabilityMax = 100f;
    private float durabilityCurrent;
    [Header("Resource Generation")]
    [SerializeField] private BaseResourceSO spawnedResourceType;
    [SerializeField] private Transform baseResourceSpawnPoint;

    private void Awake()
    {
        durabilityCurrent = durabilityMax;
    }

    public void DamageReceived(float damageAmount)
    {
        durabilityCurrent -= damageAmount;
        if (durabilityCurrent <= 0f)
        {
            Debug.Log($"{spawnedResourceType.name} resource source destroyed. Resource spawned");
            // Here you would implement the logic to spawn the resource, e.g.:
            Instantiate(spawnedResourceType.resourcePrefab, transform.position, Quaternion.identity);

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
}
