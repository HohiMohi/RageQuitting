using UnityEngine;

[DisallowMultipleComponent]
public sealed class WheelbarrowNetworkTransform : ClientNetworkTransform
{
    private WheelbarrowController wheelbarrow;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        wheelbarrow = GetComponent<WheelbarrowController>();
        if (IsServer)
            OnClientRequestChange = ValidateClientTransformRequest;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            OnClientRequestChange = null;
        base.OnNetworkDespawn();
    }

    private (Vector3 pos, Quaternion rotOut, Vector3 scale) ValidateClientTransformRequest(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        if (wheelbarrow != null &&
            wheelbarrow.TryValidateOwnerTransformRequest(position, rotation, out Vector3 acceptedPosition, out Quaternion acceptedRotation))
            return (acceptedPosition, acceptedRotation, transform.localScale);

        return (transform.position, transform.rotation, transform.localScale);
    }
}
