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
    public float damage;
    public bool actionRepeatability;
    public EquippableItemType itemType;
}

public enum EquippableItemType
{
    Axe, // Gathering Wood
    Saw, // Wood pre-treatment
    Pickaxe, //Gathering Iron
    Hammer, // Gathering Stone
    Weapon, // Fighting
    IndustrialHammer, // Mounting Bridge Components
    Shovel, // Mounting Bridge Roadway - roadway compaction
}
