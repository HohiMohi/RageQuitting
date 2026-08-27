using Unity.Netcode;
using UnityEngine;

public struct WheelbarrowMotionSnapshot : INetworkSerializable
{
    public uint AuthorityEpoch;
    public uint Sequence;
    public double ServerTimestamp;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;
    public float SteeringAngle;
    public float WheelSpinDegrees;
    public float ThrottleInput;
    public float SteeringInput;

    public WheelbarrowMotionSnapshot(
        uint authorityEpoch,
        uint sequence,
        double serverTimestamp,
        Vector3 position,
        Quaternion rotation,
        Vector3 linearVelocity,
        Vector3 angularVelocity,
        float steeringAngle,
        float wheelSpinDegrees,
        float throttleInput,
        float steeringInput)
    {
        AuthorityEpoch = authorityEpoch;
        Sequence = sequence;
        ServerTimestamp = serverTimestamp;
        Position = position;
        Rotation = rotation;
        LinearVelocity = linearVelocity;
        AngularVelocity = angularVelocity;
        SteeringAngle = steeringAngle;
        WheelSpinDegrees = wheelSpinDegrees;
        ThrottleInput = throttleInput;
        SteeringInput = steeringInput;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref AuthorityEpoch);
        serializer.SerializeValue(ref Sequence);
        serializer.SerializeValue(ref ServerTimestamp);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref Rotation);
        serializer.SerializeValue(ref LinearVelocity);
        serializer.SerializeValue(ref AngularVelocity);
        serializer.SerializeValue(ref SteeringAngle);
        serializer.SerializeValue(ref WheelSpinDegrees);
        serializer.SerializeValue(ref ThrottleInput);
        serializer.SerializeValue(ref SteeringInput);
    }
}
