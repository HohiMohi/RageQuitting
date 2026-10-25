using UnityEngine;

/// <summary>Optional root-level local orientation offset applied while carrying or placing an object.</summary>
[DisallowMultipleComponent]
public sealed class CarryPlacementOrientation : MonoBehaviour
{
    [SerializeField] private Vector3 offsetEulerDegrees = Vector3.zero;

    /// <summary>Returns the configured offset, or identity when the object has no enabled setting.</summary>
    public static Quaternion ResolveOffset(GameObject source)
    {
        if (source == null || !source.TryGetComponent(out CarryPlacementOrientation orientation)
            || !orientation.enabled)
            return Quaternion.identity;

        return Quaternion.Euler(orientation.offsetEulerDegrees);
    }

    /// <summary>Composes a reference world rotation with the configured local orientation offset.</summary>
    public static Quaternion Compose(Quaternion referenceRotation, Quaternion offset)
    {
        return referenceRotation * offset;
    }
}
