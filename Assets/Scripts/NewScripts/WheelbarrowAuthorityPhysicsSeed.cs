using Unity.Netcode;
using UnityEngine;

public struct WheelbarrowAuthorityPhysicsSeed : INetworkSerializable
{
    public WheelbarrowMotionSnapshot Motion;
    public float TotalMass;
    public Vector3 LocalCenterOfMass;
    public float DriverSupportLoadShare;
    public float WheelLoadShare;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Motion);
        serializer.SerializeValue(ref TotalMass);
        serializer.SerializeValue(ref LocalCenterOfMass);
        serializer.SerializeValue(ref DriverSupportLoadShare);
        serializer.SerializeValue(ref WheelLoadShare);
    }
}
