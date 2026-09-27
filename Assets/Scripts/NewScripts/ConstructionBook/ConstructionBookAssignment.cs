using System;
using Unity.Netcode;

public struct ConstructionBookAssignment : INetworkSerializable, IEquatable<ConstructionBookAssignment>
{
    public int SpreadIndex;
    public int StepIndex;
    public ulong ClientId;

    public ConstructionBookAssignment(int spreadIndex, int stepIndex, ulong clientId)
    {
        SpreadIndex = spreadIndex;
        StepIndex = stepIndex;
        ClientId = clientId;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SpreadIndex);
        serializer.SerializeValue(ref StepIndex);
        serializer.SerializeValue(ref ClientId);
    }

    public bool Equals(ConstructionBookAssignment other) =>
        SpreadIndex == other.SpreadIndex && StepIndex == other.StepIndex && ClientId == other.ClientId;

    public override bool Equals(object obj) => obj is ConstructionBookAssignment other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(SpreadIndex, StepIndex, ClientId);
}
