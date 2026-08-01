using System;
using UnityEngine;

public enum SharedCarryPickupFailureReason
{
    None,
    Generic,
    NoAvailableAnchor
}

public readonly struct SharedCarryPickupRejectedEventArgs
{
    public MonoBehaviour Target { get; }
    public SharedCarryPickupFailureReason Reason { get; }

    public SharedCarryPickupRejectedEventArgs(MonoBehaviour target, SharedCarryPickupFailureReason reason)
    {
        Target = target;
        Reason = reason;
    }
}

public readonly struct SharedCarryAnchorPreview
{
    public int AttachPointIndex { get; }
    public Vector3 AttachLocalPoint { get; }
    public Vector3 SurfaceWorldPosition { get; }
    public Vector3 SurfaceOutwardDirection { get; }
    public Vector3 WorldPosition => SurfaceWorldPosition;
    public Vector3 PredictedPlayerPosition { get; }

    public SharedCarryAnchorPreview(
        int attachPointIndex,
        Vector3 attachLocalPoint,
        Vector3 surfaceWorldPosition,
        Vector3 surfaceOutwardDirection,
        Vector3 predictedPlayerPosition)
    {
        AttachPointIndex = attachPointIndex;
        AttachLocalPoint = attachLocalPoint;
        SurfaceWorldPosition = surfaceWorldPosition;
        SurfaceOutwardDirection = surfaceOutwardDirection;
        PredictedPlayerPosition = predictedPlayerPosition;
    }
}

public interface ISharedCarryAnchorPreviewProvider
{
    bool SupportsAnchorPreview { get; }
    bool TryGetAnchorPreview(PlayerInteractionNew player, out SharedCarryAnchorPreview preview);
}
