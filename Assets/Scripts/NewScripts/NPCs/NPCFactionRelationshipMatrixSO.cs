using System;
using UnityEngine;

[CreateAssetMenu(fileName = "NPCFactionRelationshipMatrix", menuName = "Scriptable Objects/NPC/Faction Relationship Matrix")]
public class NPCFactionRelationshipMatrixSO : ScriptableObject
{
    [Serializable]
    public struct FactionRelationship
    {
        public NPCFactionSO sourceFaction;
        public NPCFactionSO targetFaction;
        public NPCFactionRelation relation;
        public NPCBehaviorSO customBehaviorOverride;
    }

    [SerializeField] private NPCFactionRelation defaultRelation = NPCFactionRelation.Neutral;
    [SerializeField] private FactionRelationship[] relationships = Array.Empty<FactionRelationship>();

    public NPCFactionRelation GetRelation(NPCFactionSO sourceFaction, NPCFactionSO targetFaction)
    {
        if (sourceFaction == null || targetFaction == null)
        {
            return defaultRelation;
        }

        if (sourceFaction == targetFaction)
        {
            return NPCFactionRelation.Ally;
        }

        foreach (FactionRelationship relationship in relationships)
        {
            if (relationship.sourceFaction == sourceFaction && relationship.targetFaction == targetFaction)
            {
                return relationship.relation;
            }
        }

        return defaultRelation;
    }

    public NPCBehaviorSO GetCustomBehaviorOverride(NPCFactionSO sourceFaction, NPCFactionSO targetFaction)
    {
        if (sourceFaction == null || targetFaction == null)
        {
            return null;
        }

        foreach (FactionRelationship relationship in relationships)
        {
            if (relationship.sourceFaction == sourceFaction && relationship.targetFaction == targetFaction)
            {
                return relationship.customBehaviorOverride;
            }
        }

        return null;
    }
}
