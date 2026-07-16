using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BaseResourceSO", menuName = "Scriptable Objects/BaseResourceSO")]
public class BaseResourceSO : ScriptableObject
{
    public string resourceName;
    public GameObject resourcePrefab;
    public Sprite icon;
    public BaseResourceDestructionRecipe[] baseResourceDestructionRecipeArray;
    public float resourceDurability;
    public float movementSpeedPenalty;
    public int minAmountOfPlayersNeeded;
    public bool allowMultipleCarriers;
    public int recommendedCarriers = 1;
    public int maxCarriers = 1;
    public float underStaffedPenaltyMultiplier = 1f;
    [Min(0f)] public float sharedCarryUnderstaffedStaminaDrainPerSecond = 1f;
    public float carryMoveSpeed = 4f;
    public float carryPlayerClearance = 0.35f;
    public Vector3[] carryAttachLocalPoints;
    public CarryPhysicsProfileSO carryPhysicsProfile;
    public float furnaceFuelAmount;
}

[Serializable]
public struct BaseResourceDestructionRecipe
{
    public BaseResourceSO finalProductBaseResourceSO;
    public EquippableItemType neededEquippableItemType;
    public BaseResourceDestructionProduct[] products;
    public Vector3[] spawnOffsets;
    public float fallbackScatterRadius;
}

[Serializable]
public struct BaseResourceDestructionProduct
{
    public BaseResourceSO resourceSO;
    public int amount;
}
