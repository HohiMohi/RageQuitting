using System;
using UnityEngine;

[CreateAssetMenu(fileName = "MountableBridgeComponentSO", menuName = "Scriptable Objects/MountableBridgeComponentSO")]
public class MountableBridgeComponentSO : ScriptableObject
{
    public string componentName;
    public Sprite componentSprite;
    public GameObject inGameGameObjectPrefab;
    public RequiredResource[] requiredResources;
    public BridgeComponentSO bridgeComponentSO;
    public float movementSpeedPenalty;
    public int minAmountOfPlayersNeeded;
    [Header("Carry properties")]
    public bool allowMultipleCarriers;
    public int recommendedCarriers = 1;
    public int maxCarriers = 1;
    public float underStaffedPenaltyMultiplier = 1f;
    public float carryMoveSpeed = 4f;
    public float carryPlayerClearance = 0.35f;
    public Vector3[] carryAttachLocalPoints;
    [Header("Blast Furnace Factory properties")]
    public float meltingPoint;
    public float combustionTemperature;
    public float neededProgress;
    public float neededCombustionProgress;
    [Header("Carpenter Table Factory properties")]
    public float componentWidth;
    public float componentLength;
}

[Serializable]
public struct RequiredResource
{
    public BaseResourceSO resourceType;
    public int amount;
}
