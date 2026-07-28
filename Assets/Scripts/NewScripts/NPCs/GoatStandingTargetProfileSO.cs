using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GoatStandingTargetProfile", menuName = "Scriptable Objects/NPC/Goat Standing Target Profile")]
public class GoatStandingTargetProfileSO : ScriptableObject
{
    [SerializeField] private BaseResourceSO[] allowedResources;
    [SerializeField] private bool allowAllMountableBridgeComponents = true;
    [SerializeField] private MountableBridgeComponentSO[] allowedMountableBridgeComponents;

    public bool AllowAllMountableBridgeComponents => allowAllMountableBridgeComponents;

    public bool IsAllowed(BaseResourceNew resource)
    {
        if (resource == null || allowedResources == null)
        {
            return false;
        }

        BaseResourceSO resourceSO = resource.GetBaseResourceSO();
        foreach (BaseResourceSO allowedResource in allowedResources)
        {
            if (allowedResource != null && allowedResource == resourceSO)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsAllowed(MountableBridgeComponent component)
    {
        if (component == null)
        {
            return false;
        }

        if (allowAllMountableBridgeComponents)
        {
            return true;
        }

        if (allowedMountableBridgeComponents == null)
        {
            return false;
        }

        MountableBridgeComponentSO componentSO = component.GetMountableBridgeComponentSO();
        foreach (MountableBridgeComponentSO allowedComponent in allowedMountableBridgeComponents)
        {
            if (allowedComponent != null && allowedComponent == componentSO)
            {
                return true;
            }
        }

        return false;
    }
}

public static class GoatStandingTargetReservations
{
    private sealed class Reservation
    {
        public Object Target;
        public object Owner;
    }

    private static readonly Dictionary<int, Reservation> Reservations = new Dictionary<int, Reservation>();

    public static bool TryReserve(Object target, object owner)
    {
        if (target == null || owner == null)
        {
            return false;
        }

        CleanupDestroyedTargets();
        int targetId = target.GetInstanceID();
        if (Reservations.TryGetValue(targetId, out Reservation existing))
        {
            return ReferenceEquals(existing.Owner, owner);
        }

        Reservations[targetId] = new Reservation
        {
            Target = target,
            Owner = owner
        };
        return true;
    }

    public static bool IsReservedByOther(Object target, object owner)
    {
        if (target == null)
        {
            return false;
        }

        CleanupDestroyedTargets();
        return Reservations.TryGetValue(target.GetInstanceID(), out Reservation reservation)
            && !ReferenceEquals(reservation.Owner, owner);
    }

    public static void Release(Object target, object owner)
    {
        if (target == null)
        {
            return;
        }

        int targetId = target.GetInstanceID();
        if (Reservations.TryGetValue(targetId, out Reservation reservation)
            && ReferenceEquals(reservation.Owner, owner))
        {
            Reservations.Remove(targetId);
        }
    }

    private static void CleanupDestroyedTargets()
    {
        List<int> staleIds = null;
        foreach (KeyValuePair<int, Reservation> pair in Reservations)
        {
            if (pair.Value.Target != null)
            {
                continue;
            }

            staleIds ??= new List<int>();
            staleIds.Add(pair.Key);
        }

        if (staleIds == null)
        {
            return;
        }

        foreach (int staleId in staleIds)
        {
            Reservations.Remove(staleId);
        }
    }
}
