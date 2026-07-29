using UnityEngine;

[CreateAssetMenu(fileName = "NPCSpawnTimerCondition", menuName = "Scriptable Objects/NPC/Spawn Conditions/Timer Elapsed")]
public class NPCSpawnTimerConditionSO : NPCSpawnUnlockConditionSO
{
    [SerializeField, Min(0f)] private float requiredRunningTime = 60f;

    public float RequiredRunningTime => Mathf.Max(0f, requiredRunningTime);

    public override bool IsSatisfied(NPCSpawnConditionContext context)
    {
        return context.Timer != null
            && context.Timer.IsRunning
            && context.Timer.ElapsedRunningTime >= RequiredRunningTime;
    }
}
