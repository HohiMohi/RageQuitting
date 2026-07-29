using System.Collections.Generic;
using UnityEngine;

public static class BeaverDefenderEscortRegistry
{
    private static readonly Dictionary<NPCBrain, HashSet<NPCBrain>> DefendersByScout =
        new Dictionary<NPCBrain, HashSet<NPCBrain>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        DefendersByScout.Clear();
    }

    public static bool TryReserve(NPCBrain scout, NPCBrain defender, int maximumDefenders)
    {
        if (scout == null || defender == null)
        {
            return false;
        }

        Cleanup();
        if (!DefendersByScout.TryGetValue(scout, out HashSet<NPCBrain> defenders))
        {
            defenders = new HashSet<NPCBrain>();
            DefendersByScout.Add(scout, defenders);
        }

        if (defenders.Contains(defender))
        {
            return true;
        }

        if (defenders.Count >= maximumDefenders)
        {
            return false;
        }

        defenders.Add(defender);
        return true;
    }

    public static void Release(NPCBrain defender)
    {
        if (defender == null)
        {
            return;
        }

        foreach (HashSet<NPCBrain> defenders in DefendersByScout.Values)
        {
            defenders.Remove(defender);
        }

        Cleanup();
    }

    private static void Cleanup()
    {
        List<NPCBrain> emptyScouts = null;
        foreach (KeyValuePair<NPCBrain, HashSet<NPCBrain>> pair in DefendersByScout)
        {
            pair.Value.RemoveWhere(defender => defender == null);
            if (pair.Key == null || pair.Value.Count == 0)
            {
                emptyScouts ??= new List<NPCBrain>();
                emptyScouts.Add(pair.Key);
            }
        }

        if (emptyScouts == null)
        {
            return;
        }

        foreach (NPCBrain scout in emptyScouts)
        {
            DefendersByScout.Remove(scout);
        }
    }
}
