using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>A peer-independent reference to a physical root used by mounting nails.</summary>
public struct MountingNailTargetReference : INetworkSerializable, IEquatable<MountingNailTargetReference>
{
    public bool IsNetworkObject;
    public ulong NetworkObjectId;
    public FixedString64Bytes PhysicalRootPath;
    public FixedString128Bytes StableTargetId;

    public bool IsValid => IsNetworkObject
        ? NetworkObjectId != 0
        : StableTargetId.Length > 0;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref IsNetworkObject);
        serializer.SerializeValue(ref NetworkObjectId);
        serializer.SerializeValue(ref PhysicalRootPath);
        serializer.SerializeValue(ref StableTargetId);
    }

    public bool Equals(MountingNailTargetReference other) => IsNetworkObject == other.IsNetworkObject
        && NetworkObjectId == other.NetworkObjectId
        && PhysicalRootPath.Equals(other.PhysicalRootPath) && StableTargetId.Equals(other.StableTargetId);

    public override bool Equals(object obj) => obj is MountingNailTargetReference other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(IsNetworkObject, NetworkObjectId, PhysicalRootPath, StableTargetId);
}
