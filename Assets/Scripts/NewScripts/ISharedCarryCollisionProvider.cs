using UnityEngine;

public interface ISharedCarryCollisionProvider
{
    GameObject CollisionRoot { get; }

    bool TryGetSharedCarryCapsule(out Vector3 point1, out Vector3 point2, out float radius);

    bool CanApplySharedCarryDelta(Vector3 delta);
}
