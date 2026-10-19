using UnityEngine;

[CreateAssetMenu(fileName = "BridgeDeckPanelConstructionWorkflowSO", menuName = "Scriptable Objects/Bridge Deck Panel Construction Workflow")]
public class BridgeDeckPanelConstructionWorkflowSO : ScriptableObject
{
    [Header("Placement mode")]
    [SerializeField] private bool physicalPanelPlacement;
    [SerializeField] private Vector3 physicalPanelDimensions = new Vector3(2.16f, 0.2f, 2.4f);
    [SerializeField, Min(0f)] private float physicalPanelMaximumHorizontalOffset = 0.05f;
    [SerializeField] private bool physicalPanelUsesDirectionalHorizontalTolerance;
    [SerializeField, Min(0f)] private float physicalPanelMaximumAlongOffset = 0.05f;
    [SerializeField, Min(0f)] private float physicalPanelMaximumAcrossOffset = 0.1f;
    [SerializeField, Min(0f)] private float physicalPanelMaximumHeightError = 0.03f;
    [SerializeField, Min(0f)] private float physicalPanelMaximumOrientationErrorDegrees = 3f;
    [SerializeField, Min(0f)] private float physicalPanelNailAxisExclusionHalfWidth = 0.005f;
    [SerializeField, Min(0f)] private float physicalPanelMaximumNeighborPenetration = 0.001f;

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
    [SerializeField, Min(0f)] private float panelResetUpwardDeltaVelocity = 2f;
    [SerializeField, Min(0f)] private float panelResetAwayDeltaVelocity = 0.5f;

    public bool UsesPhysicalPanelPlacement => physicalPanelPlacement;
    public Vector3 PhysicalPanelDimensions => physicalPanelDimensions;
    public float PhysicalPanelMaximumHorizontalOffset => Mathf.Max(0f, physicalPanelMaximumHorizontalOffset);
    public bool PhysicalPanelUsesDirectionalHorizontalTolerance => physicalPanelUsesDirectionalHorizontalTolerance;
    public float PhysicalPanelMaximumAlongOffset => Mathf.Max(0f, physicalPanelMaximumAlongOffset);
    public float PhysicalPanelMaximumAcrossOffset => Mathf.Max(0f, physicalPanelMaximumAcrossOffset);
    public float PhysicalPanelMaximumHeightError => Mathf.Max(0f, physicalPanelMaximumHeightError);
    public float PhysicalPanelMaximumOrientationErrorDegrees => Mathf.Clamp(physicalPanelMaximumOrientationErrorDegrees, 0f, 180f);
    public float PhysicalPanelNailAxisExclusionHalfWidth => Mathf.Max(0f, physicalPanelNailAxisExclusionHalfWidth);
    public float PhysicalPanelMaximumNeighborPenetration => Mathf.Max(0f, physicalPanelMaximumNeighborPenetration);
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
    public float PanelResetUpwardDeltaVelocity => Mathf.Max(0f, panelResetUpwardDeltaVelocity);
    public float PanelResetAwayDeltaVelocity => Mathf.Max(0f, panelResetAwayDeltaVelocity);
}
