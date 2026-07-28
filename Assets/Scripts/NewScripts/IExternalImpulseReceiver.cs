using Unity.Netcode;
using UnityEngine;

public struct ExternalImpulseData : INetworkSerializable
{
    public Vector3 InitialVelocity;
    public float HorizontalDeceleration;
    public float GravityMultiplier;
    public float MaximumDuration;
    public float MovementControlMultiplier;
    public float MaximumHorizontalSpeed;
    public float MaximumVerticalSpeed;
    public bool ForceDropHeldObject;

    public bool IsValid => InitialVelocity.sqrMagnitude > 0.0001f && MaximumDuration > 0f;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref InitialVelocity);
        serializer.SerializeValue(ref HorizontalDeceleration);
        serializer.SerializeValue(ref GravityMultiplier);
        serializer.SerializeValue(ref MaximumDuration);
        serializer.SerializeValue(ref MovementControlMultiplier);
        serializer.SerializeValue(ref MaximumHorizontalSpeed);
        serializer.SerializeValue(ref MaximumVerticalSpeed);
        serializer.SerializeValue(ref ForceDropHeldObject);
    }
}

public interface IExternalImpulseReceiver
{
    bool TryApplyExternalImpulse(ExternalImpulseData impulse, NetworkObject source);
}
