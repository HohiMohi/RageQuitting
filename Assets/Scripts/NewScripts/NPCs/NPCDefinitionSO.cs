using UnityEngine;

[CreateAssetMenu(fileName = "NPCDefinition", menuName = "Scriptable Objects/NPC/NPC Definition")]
public class NPCDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string npcName = "NPC";
    public NPCFactionSO faction;
    public NPCBehaviorSO behavior;
    public GameObject npcPrefabOverride;
    public GameObject visualPrefab;

    [Header("Stats")]
    public float maxHealth = 50f;
    public float moveSpeed = 3.5f;
    public float acceleration = 8f;
    public float angularSpeed = 360f;

    [Header("AI")]
    public float decisionTickInterval = 0.2f;
    public float detectionRadius = 12f;
    public float interactionDistance = 1.4f;
    public float patrolRadius = 8f;

    [Header("Water Traversal")]
    public NPCWaterTraversalMode waterTraversalMode = NPCWaterTraversalMode.None;
    [Range(0.1f, 1f)] public float surfaceSwimSpeedMultiplier = 0.75f;
    [Min(0f)] public float surfaceSwimVisualBobbingAmplitude;
    [Min(0f)] public float surfaceSwimVisualBobbingFrequency = 1.5f;
    [Min(1f)] public float waterAreaCost = 4f;
    public string waterNavMeshAreaName = "WaterSurface";
    [Min(1f)] public float waterEntryAreaCost = 2f;
    public string waterEntryNavMeshAreaName = "WaterEntry";

    public float SurfaceSwimVisualBobbingAmplitude => Mathf.Max(0f, surfaceSwimVisualBobbingAmplitude);
    public float SurfaceSwimVisualBobbingFrequency => Mathf.Max(0f, surfaceSwimVisualBobbingFrequency);
}
