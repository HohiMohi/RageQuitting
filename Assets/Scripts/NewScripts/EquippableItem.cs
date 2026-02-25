using UnityEngine;

public class EquippableItem : MonoBehaviour, IInteractableNew
{
    [SerializeField] private EquippableItemSO equippableItemSO;

    public void Interact(Transform interactor)
    {
        // To rework - check if there could be sent event to the inventory system to add the item instead of directly accessing the inventory component here
        interactor.GetComponent<PlayerInventory>().AddItem(equippableItemSO);
        Destroy(gameObject);
    }

    public static void DropItem(EquippableItemSO itemToDrop, Vector3 dropPosition)
    {
        // Instantiate the equippable item prefab at the specified drop position
        GameObject droppedItem = Instantiate(itemToDrop.equippableItemPrefab, dropPosition, Quaternion.identity);
        // Optionally, you can add some physics or other components to the dropped item here
    }
}
