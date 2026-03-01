using UnityEngine;

[CreateAssetMenu(fileName = "BridgeComponentSO", menuName = "Scriptable Objects/BridgeComponentSO")]
public class BridgeComponentSO : ScriptableObject
{
    public string componentName;
    public Sprite componentSprite;
    public GameObject componentPrefab;
    public BridgeComponentType bridgeComponentType;
    public int componentAdvancementLevel; // The advancement level of component (e.g., 0 for basic, 1 for intermediate, 2 for advanced)


}

public enum BridgeComponentType
{
    Support,
    Roadway,
    Suspension,
    Barrier,
    Base,
    NotSetted
}
