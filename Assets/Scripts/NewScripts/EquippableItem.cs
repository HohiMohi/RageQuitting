using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class EquippableItem : MonoBehaviour, IInteractableNew
{
    [SerializeField] private EquippableItemSO equippableItemSO;
    public EventHandler<OnLookAtEventArgs> OnLookAt;
    public static EventHandler OnAnyItemEquipped;

    public class OnLookAtEventArgs : EventArgs
    {
        public EquippableItemSO equippableItemSO;
    }
    public EventHandler OnLookAway;

    public void Interact(Transform interactor)
    {
        // To rework - check if there could be sent event to the inventory system to add the item instead of directly accessing the inventory component here
        interactor.GetComponent<PlayerInventory>().AddItem(equippableItemSO);
        OnAnyItemEquipped?.Invoke(this, EventArgs.Empty);
        Destroy(gameObject);
    }

    public static void DropItem(EquippableItemSO itemToDrop, Vector3 dropPosition)
    {
        // Instantiate the equippable item prefab at the specified drop position
        GameObject droppedItem = Instantiate(itemToDrop.equippableItemPrefab, dropPosition, Quaternion.identity);
        // Optionally, you can add some physics or other components to the dropped item here
    }

    public EquippableItemSO GetEquippableItemSO()
    {
        return equippableItemSO;
    }

    public void LookedAt(Transform interactor)
    {
        OnLookAt?.Invoke(interactor, new OnLookAtEventArgs
        {
            equippableItemSO = equippableItemSO
        });
    }

    public void LookedAway(Transform interactor)
    {
        OnLookAway?.Invoke(interactor, EventArgs.Empty);
    }

    private void OnDestroy()
    {
        OnLookAway?.Invoke(this, EventArgs.Empty);

    }
}
