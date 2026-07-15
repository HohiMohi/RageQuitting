using System;
using UnityEngine;

[Serializable]
public struct NPCBaseResourceDestructionRule
{
    public BaseResourceSO resourceSO;
    public EquippableItemType toolType;
}

[CreateAssetMenu(fileName = "NPCDestructionProfile", menuName = "Scriptable Objects/NPC/Destruction Profile")]
public class NPCDestructionProfileSO : ScriptableObject
{
    [SerializeField] private NPCBaseResourceDestructionRule[] baseResourceRules;

    public bool TryGetRule(BaseResourceNew resource, out NPCBaseResourceDestructionRule rule)
    {
        return TryGetRule(resource != null ? resource.GetBaseResourceSO() : null, out rule);
    }

    public bool TryGetRule(BaseResourceSO resourceSO, out NPCBaseResourceDestructionRule rule)
    {
        rule = default;
        if (resourceSO == null || baseResourceRules == null)
        {
            return false;
        }

        foreach (NPCBaseResourceDestructionRule candidate in baseResourceRules)
        {
            if (candidate.resourceSO == resourceSO)
            {
                rule = candidate;
                return true;
            }
        }

        return false;
    }
}
