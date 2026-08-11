using UnityEngine;

[CreateAssetMenu(fileName = "BridgeConstructionWorkflowSO", menuName = "Scriptable Objects/Bridge Construction Workflow")]
public class BridgeConstructionWorkflowSO : ScriptableObject
{
    [SerializeField] private EquippableItemType diggingTool = EquippableItemType.Shovel;
    [SerializeField, Min(0.01f)] private float diggingProgressNeeded = 100f;
    [SerializeField, Min(1)] private int diggingCycleCount = 3;
    [SerializeField, Min(0.01f)] private float looseningProgressPerCycle = 60f;
    [SerializeField, Min(1)] private int soilUnitsPerCycle = 6;
    [SerializeField, Min(0.1f)] private float finalExcavationDepth = 1.2f;

    public EquippableItemType DiggingTool => diggingTool;
    public float DiggingProgressNeeded => Mathf.Max(0.01f, diggingProgressNeeded);
    public int DiggingCycleCount => Mathf.Max(1, diggingCycleCount);
    public float LooseningProgressPerCycle => Mathf.Max(0.01f, looseningProgressPerCycle);
    public int SoilUnitsPerCycle => Mathf.Max(1, soilUnitsPerCycle);
    public float FinalExcavationDepth => Mathf.Max(0.1f, finalExcavationDepth);
}

public enum FoundationDiggingSubstage
{
    Loosening,
    SoilRemoval
}

public enum BridgeConstructionStage
{
    Clearing,
    Digging,
    ReadyForMount,
    Hammering,
    Complete,
    WaitingForFoundation,
    Leveling,
    Anchoring,
    Backfilling,
    WaitingForSupports,
    Fastening,
    WaitingForGirders,
    Aligning,
    Clamping,
    WaitingForCrossBeams,
    TemporaryFixing,
    WaitingForPrevious,
    GapSetting
}
