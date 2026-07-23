using UnityEngine;

[CreateAssetMenu(fileName = "BridgeDiagonalBracingConstructionWorkflowSO", menuName = "Scriptable Objects/Bridge Diagonal Bracing Construction Workflow")]
public class BridgeDiagonalBracingConstructionWorkflowSO : ScriptableObject
{
    [SerializeField] private EquippableItemType alignmentTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType temporaryFixingTool = EquippableItemType.IndustrialHammer;
    [SerializeField] private EquippableItemType fasteningTool = EquippableItemType.Wrench;
    [SerializeField, Min(1)] private int initialAlignmentOffset = 2;
    [SerializeField, Min(1)] private int maximumAlignmentStep = 4;
    [SerializeField, Min(0.1f)] private float alignmentAngleStep = 15f;
    [SerializeField, Min(0.01f)] private float temporaryFixProgressNeeded = 40f;
    [SerializeField, Min(0.01f)] private float fastenerProgressNeeded = 40f;

    public EquippableItemType AlignmentTool => alignmentTool;
    public EquippableItemType TemporaryFixingTool => temporaryFixingTool;
    public EquippableItemType FasteningTool => fasteningTool;
    public int InitialAlignmentOffset => Mathf.Clamp(initialAlignmentOffset, 1, MaximumAlignmentStep);
    public int MaximumAlignmentStep => Mathf.Max(1, maximumAlignmentStep);
    public float AlignmentAngleStep => Mathf.Max(0.1f, alignmentAngleStep);
    public float TemporaryFixProgressNeeded => Mathf.Max(0.01f, temporaryFixProgressNeeded);
    public float FastenerProgressNeeded => Mathf.Max(0.01f, fastenerProgressNeeded);
}
