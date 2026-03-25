using System;
using UnityEngine;

public class BaseResourceNew : MonoBehaviour, IInteractableNew, IPIckableNew, IDamageable
{
    [SerializeField] private BaseResourceSO baseResourceSO;
    [SerializeField] private float resourceDurability;
    public EventHandler EquippableItemNeeded;
    [SerializeField] private bool isPickedUp = false;
    private Rigidbody _rigidbody;

    public EventHandler<ResourceDurabilityChangedEventArgs> ResourceDurabilityChanged;
    public class ResourceDurabilityChangedEventArgs : EventArgs
    {
        public float resourceDurability;
        public float resourceDurabilityNormalized;
    }

    public void Interact(Transform interactor)
    {
        Debug.Log("Interacted with Base Resource");
        PickedUp(interactor);
    }

    public void PickedUp(Transform parent)
    {
        parent.GetComponent<PlayerInteractionNew>().PickUpObject(this.gameObject, this);
        isPickedUp=true;
        UpdatePickedUpProperties();

    }
    public void DroppedDown()
    {
        isPickedUp = false;
        UpdatePickedUpProperties();
    }
    private void Awake()
    {
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _rigidbody = GetComponent<Rigidbody>();
        resourceDurability = baseResourceSO.resourceDurability;
    }

    // Update is called once per frame
    void Update()
    {
    }
    public BaseResourceSO GetBaseResourceSO()
    {
        return baseResourceSO;
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Base Resource");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Base Resource");
    }

    public void DamageReceived(EquippableItemSO equippableItemSO, float damage)
    {
        float damageAmount = 0;
        bool equippedItemSupported = false;
        BaseResourceSO productBaseResourceSO = null;
        if (equippableItemSO != null)
        {

            foreach (BaseResourceDestructionRecipe recipe in baseResourceSO.baseResourceDestructionRecipeArray)
            {
                if (equippableItemSO.itemType == recipe.neededEquippableItemType)
                {
                    equippedItemSupported = true;
                    productBaseResourceSO = recipe.finalProductBaseResourceSO;
                }
            }
            if (equippedItemSupported)
            {
                Debug.Log("Tool supported");
                damageAmount = equippableItemSO.damage;
                damageAmount *= 2;
            }
            else
            {
                EquippableItemNeeded?.Invoke(this, EventArgs.Empty);
                Debug.Log("Unsupported tool type");
            }
        }

        resourceDurability -= damageAmount;
        if (resourceDurability <= 0f)
        {
            if (productBaseResourceSO != null)
            {
                Debug.Log($"{baseResourceSO.name} resource source destroyed. Resource spawned {productBaseResourceSO.name}");
                // Here you would implement the logic to spawn the resource, e.g.:
                Instantiate(productBaseResourceSO.resourcePrefab, transform.position, Quaternion.identity);
            }
            //Destroy the resource source object after spawning the resource
            Destroy(gameObject);

        }
        else
        {
            ResourceDurabilityChanged?.Invoke(this, new ResourceDurabilityChangedEventArgs
            {
                resourceDurability = resourceDurability,
                resourceDurabilityNormalized = GetCurrentResourceDurabilityNormalized()
            });
        }

    }

    public void DamageReceived(float damage)
    {
        resourceDurability -= damage;
        Debug.Log($"Current resource durability: {resourceDurability}");
        if (resourceDurability <= 0f)
        {
            foreach (BaseResourceDestructionRecipe recipe in baseResourceSO.baseResourceDestructionRecipeArray)
            {
                if (recipe.neededEquippableItemType == EquippableItemType.None)
                {
                    Debug.Log(recipe.finalProductBaseResourceSO.resourcePrefab);
                    Instantiate(recipe.finalProductBaseResourceSO.resourcePrefab, transform.position, Quaternion.identity);
                    break;
                }
            }
                Destroy(gameObject);
        }
        else
        {
            ResourceDurabilityChanged?.Invoke(this, new ResourceDurabilityChangedEventArgs
            {
                resourceDurability = resourceDurability,
                resourceDurabilityNormalized = GetCurrentResourceDurabilityNormalized()
            });
        }
    }

    public float GetMovementSpeedPenalty()
    {
        return baseResourceSO.movementSpeedPenalty;
    }

    public int GetMinAmountOfPlayersNeeded()
    {
        return baseResourceSO.minAmountOfPlayersNeeded;
    }

    public float GetCurrentResourceDurabilityNormalized()
    {
        return resourceDurability / baseResourceSO.resourceDurability;
    }

    private void UpdatePickedUpProperties()
    {
        if (isPickedUp)
        {
            _rigidbody.useGravity = false;           
            _rigidbody.isKinematic = true;
        }
        else
        {
            _rigidbody.useGravity = true;
            _rigidbody.isKinematic = false;
        }
    }



    private void OnCollisionEnter(Collision collision)
    {
        if(collision.gameObject.TryGetComponent<IDamageable>(out IDamageable damageableObject))
        {
            if(collision.relativeVelocity.magnitude > 1)
            {
                damageableObject.DamageReceived(collision.relativeVelocity.magnitude);
            }
        }
    }

}
