using UnityEngine;

[CreateAssetMenu(fileName = "BridgeConstructionWorkflowSO", menuName = "Scriptable Objects/Bridge Construction Workflow")]
public class BridgeConstructionWorkflowSO : ScriptableObject
{
    [SerializeField] private EquippableItemType diggingTool = EquippableItemType.Shovel;
    [SerializeField, Min(0.01f)] private float diggingProgressNeeded = 100f;

    public EquippableItemType DiggingTool => diggingTool;
    public float DiggingProgressNeeded => Mathf.Max(0.01f, diggingProgressNeeded);
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
    Clamping
}
