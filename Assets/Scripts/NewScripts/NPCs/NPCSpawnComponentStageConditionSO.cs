using UnityEngine;

[CreateAssetMenu(fileName = "NPCSpawnComponentStageCondition", menuName = "Scriptable Objects/NPC/Spawn Conditions/Component Construction Stage")]
public class NPCSpawnComponentStageConditionSO : NPCSpawnUnlockConditionSO
{
    [SerializeField] private BridgeComponentSO componentType;
    [SerializeField] private BridgeConstructionStage requiredStage = BridgeConstructionStage.Complete;
    [SerializeField] private bool requireAllInstances;

    public BridgeComponentSO ComponentType => componentType;
    public BridgeConstructionStage RequiredStage => requiredStage;
    public bool RequireAllInstances => requireAllInstances;

    public override bool IsSatisfied(NPCSpawnConditionContext context)
    {
        return context.Gameplay != null
            && componentType != null
            && context.Gameplay.HasReachedConstructionStage(componentType, requiredStage, requireAllInstances);
    }

    public override bool IsConfigurationValid(out string reason)
    {
        if (componentType == null)
        {
            reason = "Bridge component type is not assigned.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
