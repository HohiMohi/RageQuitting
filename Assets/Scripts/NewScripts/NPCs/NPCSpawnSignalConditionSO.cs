using UnityEngine;

[CreateAssetMenu(fileName = "NPCSpawnSignalCondition", menuName = "Scriptable Objects/NPC/Spawn Conditions/Manual Signal")]
public class NPCSpawnSignalConditionSO : NPCSpawnUnlockConditionSO
{
    [SerializeField] private NPCSpawnSignalSO requiredSignal;

    public NPCSpawnSignalSO RequiredSignal => requiredSignal;

    public override bool IsSatisfied(NPCSpawnConditionContext context)
    {
        return context.Spawner != null
            && requiredSignal != null
            && context.Spawner.HasReceivedSignal(requiredSignal);
    }

    public override bool IsConfigurationValid(out string reason)
    {
        if (requiredSignal == null)
        {
            reason = "Spawn signal is not assigned.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
