using UnityEngine;

public readonly struct NPCSpawnConditionContext
{
    public NPCSpawner Spawner { get; }
    public GameTimerManager Timer { get; }
    public GameplayManager Gameplay { get; }

    public NPCSpawnConditionContext(NPCSpawner spawner, GameTimerManager timer, GameplayManager gameplay)
    {
        Spawner = spawner;
        Timer = timer;
        Gameplay = gameplay;
    }
}

public abstract class NPCSpawnUnlockConditionSO : ScriptableObject
{
    public abstract bool IsSatisfied(NPCSpawnConditionContext context);

    public virtual bool IsConfigurationValid(out string reason)
    {
        reason = string.Empty;
        return true;
    }
}
