using UnityEngine;

public enum NPCSpawnCountScope
{
    AllSpawnerNPCs,
    SpecificGroup
}

[CreateAssetMenu(fileName = "NPCSpawnCountCondition", menuName = "Scriptable Objects/NPC/Spawn Conditions/Spawn Count")]
public class NPCSpawnCountConditionSO : NPCSpawnUnlockConditionSO
{
    [SerializeField] private NPCSpawnCountScope scope = NPCSpawnCountScope.AllSpawnerNPCs;
    [SerializeField, Min(0)] private int spawnCountThreshold;
    [SerializeField] private NPCSpawnGroupSO targetGroup;

    public NPCSpawnCountScope Scope => scope;
    public int SpawnCountThreshold => Mathf.Max(0, spawnCountThreshold);
    public NPCSpawnGroupSO TargetGroup => targetGroup;

    public override bool IsSatisfied(NPCSpawnConditionContext context)
    {
        if (context.Spawner == null)
        {
            return false;
        }

        int count = scope == NPCSpawnCountScope.SpecificGroup
            ? context.Spawner.GetTotalSpawnedCountForGroup(targetGroup)
            : context.Spawner.TotalSpawnedNPCCount;
        return count > SpawnCountThreshold;
    }

    public override bool IsConfigurationValid(out string reason)
    {
        if (scope == NPCSpawnCountScope.SpecificGroup && targetGroup == null)
        {
            reason = "A target spawn group is required for SpecificGroup scope.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
