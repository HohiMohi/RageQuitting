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
    [Header("Blast Furnace Factory properties")]
    public float meltingPoint;
    public float combustionTemperature;
    public float neededProgress;
    public float neededCombustionProgress;
}

[Serializable]
public struct RequiredResource
{
    public BaseResourceSO resourceType;
    public int amount;
}
