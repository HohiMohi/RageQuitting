using Unity.Netcode;
using UnityEngine;

public interface ICarryActor
{
    NetworkObject NetworkObject { get; }
    Transform CarryAnchor { get; }
    float CollisionRadius { get; }
    bool CanCarryObject { get; }
    void ConfirmCarry(GameObject carriedObject);
    void ForceRelease(GameObject carriedObject);
}
