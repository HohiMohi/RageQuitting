using UnityEngine;

[CreateAssetMenu(fileName = "BridgeGirderConstructionWorkflowSO", menuName = "Scriptable Objects/Bridge Girder Construction Workflow")]
public class BridgeGirderConstructionWorkflowSO : ScriptableObject
{
    [SerializeField] private EquippableItemType levelingTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType fasteningTool = EquippableItemType.IndustrialHammer;
    [SerializeField, Min(1)] private int maximumLevelStep = 4;
    [SerializeField, Min(1)] private int initialLevelDifference = 2;
    [SerializeField, Min(0.01f)] private float levelStepHeight = 0.1f;
    [SerializeField, Min(0.01f)] private float fastenerProgressNeeded = 40f;

    public EquippableItemType LevelingTool => levelingTool;
    public EquippableItemType FasteningTool => fasteningTool;
    public int MaximumLevelStep => Mathf.Max(1, maximumLevelStep);
    public int InitialLevelDifference => Mathf.Clamp(initialLevelDifference, 1, MaximumLevelStep);
    public float LevelStepHeight => Mathf.Max(0.01f, levelStepHeight);
    public float FastenerProgressNeeded => Mathf.Max(0.01f, fastenerProgressNeeded);
}
