using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BridgeComponentSO", menuName = "Scriptable Objects/BridgeComponentSO")]
public class BridgeComponentSO : ScriptableObject
{
    public string componentName;
    public Sprite componentSprite;
    public GameObject componentPrefab;
    public BridgeComponentType bridgeComponentType;
    public int componentAdvancementLevel; // The advancement level of component (e.g., 0 for basic, 1 for intermediate, 2 for advanced)
    public List<EquippableItemType> supportedEquippableItemTypeList;
    public float assemblingProgressNeeded;
    public bool needAssembling;
    public BridgeConstructionWorkflowSO constructionWorkflow;
    public BridgeAbutmentConstructionWorkflowSO abutmentConstructionWorkflow;
    public BridgeGirderConstructionWorkflowSO girderConstructionWorkflow;

}

public enum BridgeComponentType
{
    NotSetted,
    Support,
    Roadway,
    Suspension,
    Barrier,
    Base
}
