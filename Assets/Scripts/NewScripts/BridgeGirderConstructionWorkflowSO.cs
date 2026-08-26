using UnityEngine;

[CreateAssetMenu(fileName = "BridgeGirderConstructionWorkflowSO", menuName = "Scriptable Objects/Bridge Girder Construction Workflow")]
public class BridgeGirderConstructionWorkflowSO : ScriptableObject
{
    [SerializeField] private EquippableItemType levelingTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType fasteningTool = EquippableItemType.IndustrialHammer;
    [Header("Leveling")]
    [SerializeField, Min(1)] private int maximumLogicalTilt = 8;
    [SerializeField, Min(1)] private int minimumInitialAbsoluteTilt = 1;
    [SerializeField, Min(0)] private int levelingSuccessTolerance;
    [SerializeField, Min(0)] private int visuallyStraightTiltRange = 4;
    [SerializeField, Min(0f)] private float maximumVisualTiltDegrees = 3f;
    [SerializeField, Min(0.01f)] private float fastenerProgressNeeded = 40f;
    [SerializeField, Min(0.1f)] private float fastenerPairWindowDuration = 15f;

    public EquippableItemType LevelingTool => levelingTool;
    public EquippableItemType FasteningTool => fasteningTool;
    public int MaximumLogicalTilt => Mathf.Max(1, maximumLogicalTilt);
    public int MinimumInitialAbsoluteTilt => Mathf.Clamp(minimumInitialAbsoluteTilt, 1, MaximumLogicalTilt);
    public int LevelingSuccessTolerance => Mathf.Clamp(levelingSuccessTolerance, 0, MaximumLogicalTilt);
    public int VisuallyStraightTiltRange => Mathf.Clamp(visuallyStraightTiltRange, 0, Mathf.Max(0, MaximumLogicalTilt - 1));
    public float MaximumVisualTiltDegrees => Mathf.Max(0f, maximumVisualTiltDegrees);
    public float FastenerProgressNeeded => Mathf.Max(0.01f, fastenerProgressNeeded);
    public float FastenerPairWindowDuration => Mathf.Max(0.1f, fastenerPairWindowDuration);
}
