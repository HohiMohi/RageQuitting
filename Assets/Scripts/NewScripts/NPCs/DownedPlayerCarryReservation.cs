using System.Collections.Generic;
using UnityEngine;

public static class DownedPlayerCarryReservation
{
    private static readonly Dictionary<DownedPlayerCarryable, NPCCarrier> Reservations = new();

    public static bool TryReserve(DownedPlayerCarryable target, NPCCarrier carrier)
    {
        Cleanup();
        if (target == null || carrier == null)
        {
            return false;
        }

        if (Reservations.TryGetValue(target, out NPCCarrier existing))
        {
            return existing == carrier;
        }

        Reservations[target] = carrier;
        return true;
    }

    public static bool IsReservedBy(DownedPlayerCarryable target, NPCCarrier carrier)
    {
        Cleanup();
        return target != null
            && carrier != null
            && Reservations.TryGetValue(target, out NPCCarrier existing)
            && existing == carrier;
    }

    public static bool IsReservedByOther(DownedPlayerCarryable target, NPCCarrier carrier)
    {
        Cleanup();
        return target != null
            && Reservations.TryGetValue(target, out NPCCarrier existing)
            && existing != null
            && existing != carrier;
    }

    public static void Release(DownedPlayerCarryable target, NPCCarrier carrier)
    {
        if (target != null
            && Reservations.TryGetValue(target, out NPCCarrier existing)
            && (carrier == null || existing == carrier))
        {
            Reservations.Remove(target);
        }
    }

    public static void ReleaseAll(NPCCarrier carrier)
    {
        if (carrier == null)
        {
            return;
        }

        List<DownedPlayerCarryable> targets = new();
        foreach (KeyValuePair<DownedPlayerCarryable, NPCCarrier> pair in Reservations)
        {
            if (pair.Value == null || pair.Value == carrier)
            {
                targets.Add(pair.Key);
            }
        }

        foreach (DownedPlayerCarryable target in targets)
        {
            Reservations.Remove(target);
        }
    }

    private static void Cleanup()
    {
        List<DownedPlayerCarryable> staleTargets = null;
        foreach (KeyValuePair<DownedPlayerCarryable, NPCCarrier> pair in Reservations)
        {
            if (pair.Key != null && pair.Value != null)
            {
                continue;
            }

            staleTargets ??= new List<DownedPlayerCarryable>();
            staleTargets.Add(pair.Key);
        }

        if (staleTargets == null)
        {
            return;
        }

        foreach (DownedPlayerCarryable target in staleTargets)
        {
            Reservations.Remove(target);
        }
    }
}
