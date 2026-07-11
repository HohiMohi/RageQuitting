using UnityEngine;

[CreateAssetMenu(fileName = "NPCFaction", menuName = "Scriptable Objects/NPC/NPC Faction")]
public class NPCFactionSO : ScriptableObject
{
    [SerializeField] private string factionId = "Faction";
    [SerializeField] private string displayName = "Faction";

    public string FactionId => factionId;
    public string DisplayName => displayName;
}
