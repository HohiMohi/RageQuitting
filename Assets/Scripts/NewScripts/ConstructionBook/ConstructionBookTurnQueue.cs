using System.Collections.Generic;

public readonly struct ConstructionBookTurnRequest
{
    public readonly ulong SenderClientId;
    public readonly int Delta;

    public ConstructionBookTurnRequest(ulong senderClientId, int delta)
    {
        SenderClientId = senderClientId;
        Delta = delta;
    }
}

public sealed class ConstructionBookTurnQueue
{
    private readonly Queue<ConstructionBookTurnRequest> requests = new Queue<ConstructionBookTurnRequest>();
    private readonly Dictionary<ulong, int> pendingBySender = new Dictionary<ulong, int>();
    private readonly int capacity;
    private readonly int perSenderCapacity;
    private double nextAllowedTime;

    public ConstructionBookTurnQueue(int capacity, int perSenderCapacity)
    {
        this.capacity = capacity > 0 ? capacity : 1;
        this.perSenderCapacity = perSenderCapacity > 0 ? perSenderCapacity : 1;
    }

    public int Count => requests.Count;
    public double NextAllowedTime => nextAllowedTime;

    public bool TryEnqueue(ulong senderClientId, int delta)
    {
        if ((delta != -1 && delta != 1) || requests.Count >= capacity)
            return false;

        pendingBySender.TryGetValue(senderClientId, out int senderCount);
        if (senderCount >= perSenderCapacity)
            return false;

        requests.Enqueue(new ConstructionBookTurnRequest(senderClientId, delta));
        pendingBySender[senderClientId] = senderCount + 1;
        return true;
    }

    public bool TryDequeue(double now, out ConstructionBookTurnRequest request)
    {
        if (requests.Count == 0 || now < nextAllowedTime)
        {
            request = default;
            return false;
        }

        request = requests.Dequeue();
        int remaining = pendingBySender[request.SenderClientId] - 1;
        if (remaining == 0) pendingBySender.Remove(request.SenderClientId);
        else pendingBySender[request.SenderClientId] = remaining;
        return true;
    }

    public void MarkApplied(double now, double cadence)
    {
        nextAllowedTime = now + cadence;
    }

    public void RemoveSender(ulong senderClientId)
    {
        if (!pendingBySender.ContainsKey(senderClientId)) return;

        int count = requests.Count;
        for (int i = 0; i < count; i++)
        {
            ConstructionBookTurnRequest request = requests.Dequeue();
            if (request.SenderClientId != senderClientId) requests.Enqueue(request);
        }
        pendingBySender.Remove(senderClientId);
    }

    public void Clear()
    {
        requests.Clear();
        pendingBySender.Clear();
        nextAllowedTime = 0d;
    }
}
