using UnityEngine;

[CreateAssetMenu(fileName = "NPCSpawnAlwaysCondition", menuName = "Scriptable Objects/NPC/Spawn Conditions/Always")]
public class NPCSpawnAlwaysConditionSO : NPCSpawnUnlockConditionSO
{
    public override bool IsSatisfied(NPCSpawnConditionContext context)
    {
        return true;
    }
}
