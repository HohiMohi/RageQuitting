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
    [Min(0f)] public float resourceDamage;
    [Min(0f)] public float constructionWorkPower;
    public float movementSpeedPenalty;
    public bool actionRepeatability;
    public EquippableItemType itemType;
    public EquippableActionProfileSO actionProfile;
    public ExternalImpulseProfileSO impactImpulseProfile;

    public float ResourceDamage => resourceDamage > 0f ? resourceDamage : damage * 2f;
    public float ConstructionWorkPower => constructionWorkPower > 0f ? constructionWorkPower : damage;
    public bool IsTwoHanded => inventorySlotsRequired >= 2;
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
    None, // for object-object and object-NPC interaction
    Wrench // Tightening bridge construction connections
}
