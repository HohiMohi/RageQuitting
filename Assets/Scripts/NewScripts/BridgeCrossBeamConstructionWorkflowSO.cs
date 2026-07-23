using UnityEngine;

[CreateAssetMenu(fileName = "BridgeCrossBeamConstructionWorkflowSO", menuName = "Scriptable Objects/Bridge Cross Beam Construction Workflow")]
public class BridgeCrossBeamConstructionWorkflowSO : ScriptableObject
{
    [SerializeField] private EquippableItemType alignmentTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType clampingTool = EquippableItemType.Wrench;
    [SerializeField] private EquippableItemType fasteningTool = EquippableItemType.Wrench;
    [SerializeField, Min(1)] private int maximumAlignmentStep = 2;
    [SerializeField, Min(0.01f)] private float alignmentStepDistance = 0.12f;
    [SerializeField, Min(0.01f)] private float clampProgressNeeded = 60f;
    [SerializeField, Min(0.01f)] private float maximumClampProgressDifference = 20f;
    [SerializeField, Min(0.01f)] private float fastenerProgressNeeded = 40f;

    public EquippableItemType AlignmentTool => alignmentTool;
    public EquippableItemType ClampingTool => clampingTool;
    public EquippableItemType FasteningTool => fasteningTool;
    public int MaximumAlignmentStep => Mathf.Max(1, maximumAlignmentStep);
    public float AlignmentStepDistance => Mathf.Max(0.01f, alignmentStepDistance);
    public float ClampProgressNeeded => Mathf.Max(0.01f, clampProgressNeeded);
    public float MaximumClampProgressDifference => Mathf.Max(0.01f, maximumClampProgressDifference);
    public float FastenerProgressNeeded => Mathf.Max(0.01f, fastenerProgressNeeded);
}
