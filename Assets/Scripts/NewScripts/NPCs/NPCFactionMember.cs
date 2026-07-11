using Unity.Netcode;
using UnityEngine;

public class NPCFactionMember : NetworkBehaviour, INPCTarget
{
    [SerializeField] private NPCFactionSO faction;

    public Transform TargetTransform => transform;
    public NPCFactionSO Faction => faction;
    public bool IsTargetAvailable => isActiveAndEnabled;

    public void SetFaction(NPCFactionSO newFaction)
    {
        faction = newFaction;
    }
}
