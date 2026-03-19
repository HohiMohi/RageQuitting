using System;
using UnityEngine;

public class BaseResourceNew : MonoBehaviour, IInteractableNew, IPIckableNew, IDamageable
{
    [SerializeField] private BaseResourceSO baseResourceSO;
    [SerializeField] private float resourceDurability;
    public EventHandler EquippableItemNeeded;
    public void Interact(Transform interactor)
    {
        Debug.Log("Interacted with Base Resource");
        PickedUp(interactor);
    }

    public void PickedUp(Transform parent)
    {
        parent.GetComponent<PlayerInteractionNew>().PickUpObject(this.gameObject, this);

    }
    public void DroppedDown()
    {

    }
    private void Awake()
    {
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
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
            Debug.Log($"Resource source damaged! Current durability: {resourceDurability}");
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


}
