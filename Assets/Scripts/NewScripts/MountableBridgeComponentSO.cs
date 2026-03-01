using UnityEngine;

[CreateAssetMenu(fileName = "MountableBridgeComponentSO", menuName = "Scriptable Objects/MountableBridgeComponentSO")]
public class MountableBridgeComponentSO : ScriptableObject
{
    public string componentName;
    public Sprite componentSprite;
    public GameObject inGameGameObjectPrefab;
    public RequiredResource[] requiredResources;
    public BridgeComponentSO bridgeComponentSO;
}

public struct RequiredResource
{
    public BaseResourceSO resourceType;
    public int amount;
}
