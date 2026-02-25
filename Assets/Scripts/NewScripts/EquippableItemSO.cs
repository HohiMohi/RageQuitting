using UnityEngine;

[CreateAssetMenu(fileName = "EquippableItemSO", menuName = "Scriptable Objects/EquippableItemSO")]
public class EquippableItemSO : ScriptableObject
{
    public string itemName;
    public Sprite uiSprite;
    public GameObject equippableItemPrefab;
    public int inventorySlotsRequired;
    public float actionRange;
    public float actionCooldown;
    public int damage;
}
