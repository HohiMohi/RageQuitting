using System.Collections.Generic;
using UnityEngine;

public static class NPCRegistry
{
    private static readonly HashSet<NPCBrain> ActiveBrains = new HashSet<NPCBrain>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        ActiveBrains.Clear();
    }

    public static IEnumerable<NPCBrain> ActiveNPCs
    {
        get
        {
            ActiveBrains.RemoveWhere(brain => brain == null);
            return ActiveBrains;
        }
    }

    public static void Register(NPCBrain brain)
    {
        if (brain != null)
        {
            ActiveBrains.Add(brain);
        }
    }

    public static void Unregister(NPCBrain brain)
    {
        if (brain != null)
        {
            ActiveBrains.Remove(brain);
        }
    }
}
