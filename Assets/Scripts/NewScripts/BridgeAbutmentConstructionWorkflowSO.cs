using UnityEngine;

[CreateAssetMenu(fileName = "BridgeAbutmentConstructionWorkflowSO", menuName = "Scriptable Objects/Bridge Abutment Construction Workflow")]
public class BridgeAbutmentConstructionWorkflowSO : ScriptableObject
{
    [SerializeField] private EquippableItemType levelingTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType anchoringTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType backfillingTool = EquippableItemType.Shovel;
    [Header("Leveling")]
    [SerializeField, Min(1)] private int maximumLogicalTilt = 8;
    [SerializeField, Min(1)] private int minimumInitialAbsoluteTilt = 1;
    [SerializeField, Min(0)] private int levelingSuccessTolerance;
    [SerializeField, Min(0)] private int visuallyStraightTiltRange = 4;
    [SerializeField, Min(0f)] private float maximumVisualTiltDegrees = 3f;
    [SerializeField, Min(0.01f)] private float anchorProgressNeeded = 40f;
    [SerializeField, Min(0.01f)] private float backfillProgressNeeded = 100f;

    public EquippableItemType LevelingTool => levelingTool;
    public EquippableItemType AnchoringTool => anchoringTool;
    public EquippableItemType BackfillingTool => backfillingTool;
    public int MaximumLogicalTilt => Mathf.Max(1, maximumLogicalTilt);
    public int MinimumInitialAbsoluteTilt => Mathf.Clamp(minimumInitialAbsoluteTilt, 1, MaximumLogicalTilt);
    public int LevelingSuccessTolerance => Mathf.Clamp(levelingSuccessTolerance, 0, MaximumLogicalTilt);
    public int VisuallyStraightTiltRange => Mathf.Clamp(visuallyStraightTiltRange, 0, Mathf.Max(0, MaximumLogicalTilt - 1));
    public float MaximumVisualTiltDegrees => Mathf.Max(0f, maximumVisualTiltDegrees);
    public float AnchorProgressNeeded => Mathf.Max(0.01f, anchorProgressNeeded);
    public float BackfillProgressNeeded => Mathf.Max(0.01f, backfillProgressNeeded);
}
