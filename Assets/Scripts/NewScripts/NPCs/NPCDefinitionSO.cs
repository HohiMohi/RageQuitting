using UnityEngine;

[CreateAssetMenu(fileName = "NPCDefinition", menuName = "Scriptable Objects/NPC/NPC Definition")]
public class NPCDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string npcName = "NPC";
    public NPCFactionSO faction;
    public NPCBehaviorSO behavior;
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
}
