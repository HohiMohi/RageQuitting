using System.Collections.Generic;

public static class GoatPushTargetReservations
{
    private static readonly Dictionary<int, GoatBehaviorSO.GoatBehaviorController> Reservations =
        new Dictionary<int, GoatBehaviorSO.GoatBehaviorController>();

    public static bool TryReserve(PlayerHealth player, GoatBehaviorSO.GoatBehaviorController controller)
    {
        if (player == null || controller == null)
        {
            return false;
        }

        int key = player.GetInstanceID();
        if (Reservations.TryGetValue(key, out GoatBehaviorSO.GoatBehaviorController owner)
            && owner != null
            && owner != controller)
        {
            return false;
        }

        Reservations[key] = controller;
        return true;
    }

    public static bool IsReservedByOther(PlayerHealth player, GoatBehaviorSO.GoatBehaviorController controller)
    {
        return player != null
            && Reservations.TryGetValue(player.GetInstanceID(), out GoatBehaviorSO.GoatBehaviorController owner)
            && owner != null
            && owner != controller;
    }

    public static void Release(PlayerHealth player, GoatBehaviorSO.GoatBehaviorController controller)
    {
        if (player == null)
        {
            return;
        }

        int key = player.GetInstanceID();
        if (Reservations.TryGetValue(key, out GoatBehaviorSO.GoatBehaviorController owner)
            && owner == controller)
        {
            Reservations.Remove(key);
        }
    }
}
