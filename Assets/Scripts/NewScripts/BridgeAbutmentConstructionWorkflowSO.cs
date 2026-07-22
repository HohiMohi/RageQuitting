using UnityEngine;

[CreateAssetMenu(fileName = "BridgeAbutmentConstructionWorkflowSO", menuName = "Scriptable Objects/Bridge Abutment Construction Workflow")]
public class BridgeAbutmentConstructionWorkflowSO : ScriptableObject
{
    [SerializeField] private EquippableItemType levelingTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType anchoringTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType backfillingTool = EquippableItemType.Shovel;
    [SerializeField, Min(1)] private int maximumLevelStep = 4;
    [SerializeField, Min(1)] private int initialLevelDifference = 2;
    [SerializeField, Min(0.01f)] private float levelStepHeight = 0.08f;
    [SerializeField, Min(0.01f)] private float anchorProgressNeeded = 40f;
    [SerializeField, Min(0.01f)] private float backfillProgressNeeded = 100f;

    public EquippableItemType LevelingTool => levelingTool;
    public EquippableItemType AnchoringTool => anchoringTool;
    public EquippableItemType BackfillingTool => backfillingTool;
    public int MaximumLevelStep => Mathf.Max(1, maximumLevelStep);
    public int InitialLevelDifference => Mathf.Clamp(initialLevelDifference, 1, MaximumLevelStep);
    public float LevelStepHeight => Mathf.Max(0.01f, levelStepHeight);
    public float AnchorProgressNeeded => Mathf.Max(0.01f, anchorProgressNeeded);
    public float BackfillProgressNeeded => Mathf.Max(0.01f, backfillProgressNeeded);
}
