using UnityEngine;

[CreateAssetMenu(fileName = "BridgeDeckPanelConstructionWorkflowSO", menuName = "Scriptable Objects/Bridge Deck Panel Construction Workflow")]
public class BridgeDeckPanelConstructionWorkflowSO : ScriptableObject
{
    [SerializeField] private EquippableItemType alignmentTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType gapTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType fasteningTool = EquippableItemType.Wrench;
    [SerializeField, Min(1)] private int initialAlignmentOffset = 2;
    [SerializeField, Min(1)] private int maximumAlignmentStep = 2;
    [SerializeField, Min(0.001f)] private float lateralStepDistance = 0.08f;
    [SerializeField, Min(0.1f)] private float rotationStepDegrees = 3f;
    [SerializeField, Min(1)] private int minimumInitialGapStep = 1;
    [SerializeField, Min(1)] private int maximumInitialGapStep = 2;
    [SerializeField, Min(0.001f)] private float gapStepDistance = 0.02f;
    [SerializeField, Min(0.01f)] private float fastenerProgressNeeded = 40f;

    public EquippableItemType AlignmentTool => alignmentTool;
    public EquippableItemType GapTool => gapTool;
    public EquippableItemType FasteningTool => fasteningTool;
    public int MaximumAlignmentStep => Mathf.Max(1, maximumAlignmentStep);
    public int InitialAlignmentOffset => Mathf.Clamp(initialAlignmentOffset, 1, MaximumAlignmentStep);
    public float LateralStepDistance => Mathf.Max(0.001f, lateralStepDistance);
    public float RotationStepDegrees => Mathf.Max(0.1f, rotationStepDegrees);
    public int MinimumInitialGapStep => Mathf.Max(1, minimumInitialGapStep);
    public int MaximumInitialGapStep => Mathf.Max(MinimumInitialGapStep, maximumInitialGapStep);
    public float GapStepDistance => Mathf.Max(0.001f, gapStepDistance);
    public float FastenerProgressNeeded => Mathf.Max(0.01f, fastenerProgressNeeded);
}
