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
    public float furnaceFuelAmount;
}

[Serializable]
public struct BaseResourceDestructionRecipe
{
    public BaseResourceSO finalProductBaseResourceSO;
    public EquippableItemType neededEquippableItemType;
}