using System;
using Unity.Netcode;

public enum PlayerConcreteTrapState : byte
{
    None,
    InWheelbarrow,
    Ejected,
    Collapsing
}

public struct PlayerConcreteTrapNetworkState : INetworkSerializable, IEquatable<PlayerConcreteTrapNetworkState>
{
    public PlayerConcreteTrapState State;
    public ulong SourceWheelbarrowNetworkObjectId;
    public float Progress;

    public PlayerConcreteTrapNetworkState(PlayerConcreteTrapState state, ulong sourceWheelbarrowNetworkObjectId, float progress)
    {
        State = state;
        SourceWheelbarrowNetworkObjectId = sourceWheelbarrowNetworkObjectId;
        Progress = progress;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref State);
        serializer.SerializeValue(ref SourceWheelbarrowNetworkObjectId);
        serializer.SerializeValue(ref Progress);
    }

    public bool Equals(PlayerConcreteTrapNetworkState other)
    {
        return State == other.State &&
               SourceWheelbarrowNetworkObjectId == other.SourceWheelbarrowNetworkObjectId &&
               Progress.Equals(other.Progress);
    }
}
