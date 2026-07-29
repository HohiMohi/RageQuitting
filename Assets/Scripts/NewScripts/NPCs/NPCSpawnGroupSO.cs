using System;
using System.Collections.Generic;
using UnityEngine;

public enum NPCSpawnConditionMode
{
    All,
    Any
}

[Serializable]
public class NPCSpawnEntry
{
    [SerializeField] private NPCDefinitionSO definition;
    [SerializeField, Min(0f)] private float weight = 1f;

    public NPCDefinitionSO Definition => definition;
    public float Weight => Mathf.Max(0f, weight);
}

[CreateAssetMenu(fileName = "NPCSpawnGroup", menuName = "Scriptable Objects/NPC/Spawn Group")]
public class NPCSpawnGroupSO : ScriptableObject
{
    [SerializeField] private string displayName = "NPC Spawn Group";
    [SerializeField, Min(0f)] private float spawnWeight = 1f;
    [SerializeField, Min(1)] private int maxActiveNPCs = 5;
    [SerializeField] private List<NPCSpawnEntry> entries = new List<NPCSpawnEntry>();
    [SerializeField] private NPCSpawnConditionMode conditionMode = NPCSpawnConditionMode.All;
    [SerializeField] private List<NPCSpawnUnlockConditionSO> unlockConditions = new List<NPCSpawnUnlockConditionSO>();

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float SpawnWeight => Mathf.Max(0f, spawnWeight);
    public int MaxActiveNPCs => Mathf.Max(1, maxActiveNPCs);
    public IReadOnlyList<NPCSpawnEntry> Entries => entries;
    public NPCSpawnConditionMode ConditionMode => conditionMode;
    public IReadOnlyList<NPCSpawnUnlockConditionSO> UnlockConditions => unlockConditions;

    public bool AreUnlockConditionsSatisfied(NPCSpawnConditionContext context)
    {
        if (unlockConditions == null || unlockConditions.Count == 0)
        {
            return true;
        }

        if (conditionMode == NPCSpawnConditionMode.All)
        {
            foreach (NPCSpawnUnlockConditionSO condition in unlockConditions)
            {
                if (condition == null || !condition.IsSatisfied(context))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (NPCSpawnUnlockConditionSO condition in unlockConditions)
        {
            if (condition != null && condition.IsSatisfied(context))
            {
                return true;
            }
        }

        return false;
    }

    public bool HasValidSpawnEntry(GameObject basePrefab)
    {
        if (entries == null)
        {
            return false;
        }

        foreach (NPCSpawnEntry entry in entries)
        {
            if (entry != null
                && entry.Definition != null
                && entry.Weight > 0f
                && (entry.Definition.npcPrefabOverride != null || basePrefab != null))
            {
                return true;
            }
        }

        return false;
    }

    public bool TrySelectDefinition(GameObject basePrefab, out NPCDefinitionSO definition)
    {
        definition = null;
        if (entries == null || entries.Count == 0)
        {
            return false;
        }

        float totalWeight = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            NPCSpawnEntry entry = entries[i];
            if (entry != null
                && entry.Definition != null
                && entry.Weight > 0f
                && (entry.Definition.npcPrefabOverride != null || basePrefab != null))
            {
                totalWeight += entry.Weight;
            }
        }

        if (totalWeight <= 0f)
        {
            return false;
        }

        float roll = UnityEngine.Random.value * totalWeight;
        NPCDefinitionSO lastValidDefinition = null;
        for (int i = 0; i < entries.Count; i++)
        {
            NPCSpawnEntry entry = entries[i];
            if (entry == null
                || entry.Definition == null
                || entry.Weight <= 0f
                || (entry.Definition.npcPrefabOverride == null && basePrefab == null))
            {
                continue;
            }

            lastValidDefinition = entry.Definition;
            roll -= entry.Weight;
            if (roll <= 0f)
            {
                definition = entry.Definition;
                return true;
            }
        }

        definition = lastValidDefinition;
        return definition != null;
    }

    public void ValidateConfiguration(UnityEngine.Object context)
    {
        if (spawnWeight <= 0f)
        {
            Debug.LogWarning($"{name}: spawn group weight must be greater than zero.", context);
        }

        if (maxActiveNPCs <= 0)
        {
            Debug.LogWarning($"{name}: max active NPC count must be greater than zero.", context);
        }

        if (entries == null || entries.Count == 0)
        {
            Debug.LogWarning($"{name}: spawn group has no NPC entries.", context);
        }
        else
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null || entries[i].Definition == null)
                {
                    Debug.LogWarning($"{name}: NPC entry {i} has no definition.", context);
                }
                else if (entries[i].Weight <= 0f)
                {
                    Debug.LogWarning($"{name}: NPC entry {i} has a non-positive weight.", context);
                }
            }
        }

        if (unlockConditions == null)
        {
            return;
        }

        for (int i = 0; i < unlockConditions.Count; i++)
        {
            if (unlockConditions[i] == null)
            {
                Debug.LogWarning($"{name}: unlock condition {i} is missing.", context);
            }
            else if (!unlockConditions[i].IsConfigurationValid(out string reason))
            {
                Debug.LogWarning($"{name}: unlock condition {i} is invalid: {reason}", context);
            }
        }
    }
}
