using System;
using Unity.Netcode;
using UnityEngine;

public readonly struct NPCFactionDamageAlert
{
    public NPCFactionDamageAlert(
        NPCHealth victim,
        NPCFactionSO victimFaction,
        NetworkObject attacker,
        Vector3 position)
    {
        Victim = victim;
        VictimFaction = victimFaction;
        Attacker = attacker;
        Position = position;
    }

    public NPCHealth Victim { get; }
    public NPCFactionSO VictimFaction { get; }
    public NetworkObject Attacker { get; }
    public Vector3 Position { get; }
}

public static class NPCFactionDamageAlertSystem
{
    public static event Action<NPCFactionDamageAlert> OnNpcFactionMemberDamaged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        OnNpcFactionMemberDamaged = null;
    }

    public static void Publish(NPCHealth victim, NetworkObject attacker)
    {
        if (victim == null || attacker == null)
        {
            return;
        }

        OnNpcFactionMemberDamaged?.Invoke(new NPCFactionDamageAlert(
            victim,
            victim.Faction,
            attacker,
            victim.transform.position));
    }
}
