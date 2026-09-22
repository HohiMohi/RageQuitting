using System.Collections.Generic;

public readonly struct ConstructionBookRosterMember
{
    public readonly ulong ClientId;
    public readonly string Label;
    public ConstructionBookRosterMember(ulong clientId, string label) { ClientId = clientId; Label = label; }
}

public static class ConstructionBookRoster
{
    public static List<ConstructionBookRosterMember> Build(IList<ulong> clientIds, bool hasHost, ulong hostClientId)
    {
        List<ulong> sortedClientIds = new List<ulong>(clientIds.Count);
        for (int i = 0; i < clientIds.Count; i++) sortedClientIds.Add(clientIds[i]);
        sortedClientIds.Sort();
        List<ConstructionBookRosterMember> result = new List<ConstructionBookRosterMember>();
        int playerNumber = hasHost ? 2 : 1;
        if (hasHost && sortedClientIds.Contains(hostClientId))
            result.Add(new ConstructionBookRosterMember(hostClientId, "Host"));
        for (int i = 0; i < sortedClientIds.Count; i++)
        {
            ulong id = sortedClientIds[i];
            if (hasHost && id == hostClientId) continue;
            result.Add(new ConstructionBookRosterMember(id, $"Player {playerNumber++}"));
        }
        return result;
    }
}
