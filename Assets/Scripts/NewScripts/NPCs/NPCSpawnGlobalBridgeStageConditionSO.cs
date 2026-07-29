using UnityEngine;

[CreateAssetMenu(fileName = "NPCSpawnGlobalBridgeStageCondition", menuName = "Scriptable Objects/NPC/Spawn Conditions/Global Bridge Stage")]
public class NPCSpawnGlobalBridgeStageConditionSO : NPCSpawnUnlockConditionSO
{
    [SerializeField, Min(0)] private int requiredStageIndex;

    public int RequiredStageIndex => Mathf.Max(0, requiredStageIndex);

    public override bool IsSatisfied(NPCSpawnConditionContext context)
    {
        return context.Gameplay != null
            && context.Gameplay.CurrentBridgeStageIndex >= RequiredStageIndex;
    }
}
