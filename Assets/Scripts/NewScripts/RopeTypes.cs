using Unity.Netcode;
using UnityEngine;
using System;

public enum RopeState
{
    Inactive,
    Ready,
    Charging,
    Flying,
    Loose,
    Attached,
    Reeling,
    PayingOut
}

public enum RopeTargetKind
{
    None,
    Player,
    Resource,
    Wheelbarrow
}

public enum RopeEndMotionState
{
    Flying,
    Landed
}

public struct RopePlayerConstraintSettings : INetworkSerializable, IEquatable<RopePlayerConstraintSettings>
{
    public float PullSpeed;
    public float TargetPullShare;
    public float HolderReactionShare;
    public float TautDeadZone;
    public float MaximumStretch;
    public float SwingGravityMultiplier;
    public float SwingInputAcceleration;
    public float SwingDamping;
    public float MaximumSwingSpeed;
    public float SwingTautThreshold;
    public float WallContactGraceDuration;
    public float PositionDeadZone;
    public float PositionCorrectionSpeed;
    public float PositionCorrectionAcceleration;
    public float MaximumAnchorTransferSpeed;
    public float GroundedReleaseDelay;
    public float UpwardPullThreshold;
    public float WallJumpOutwardSpeed;
    public float WallJumpUpwardSpeed;
    public float WallJumpCooldown;

    public bool IsValid => PullSpeed > 0f && MaximumStretch > 0f && TargetPullShare + HolderReactionShare > 0f;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref PullSpeed);
        serializer.SerializeValue(ref TargetPullShare);
        serializer.SerializeValue(ref HolderReactionShare);
        serializer.SerializeValue(ref TautDeadZone);
        serializer.SerializeValue(ref MaximumStretch);
        serializer.SerializeValue(ref SwingGravityMultiplier);
        serializer.SerializeValue(ref SwingInputAcceleration);
        serializer.SerializeValue(ref SwingDamping);
        serializer.SerializeValue(ref MaximumSwingSpeed);
        serializer.SerializeValue(ref SwingTautThreshold);
        serializer.SerializeValue(ref WallContactGraceDuration);
        serializer.SerializeValue(ref PositionDeadZone);
        serializer.SerializeValue(ref PositionCorrectionSpeed);
        serializer.SerializeValue(ref PositionCorrectionAcceleration);
        serializer.SerializeValue(ref MaximumAnchorTransferSpeed);
        serializer.SerializeValue(ref GroundedReleaseDelay);
        serializer.SerializeValue(ref UpwardPullThreshold);
        serializer.SerializeValue(ref WallJumpOutwardSpeed);
        serializer.SerializeValue(ref WallJumpUpwardSpeed);
        serializer.SerializeValue(ref WallJumpCooldown);
    }

    public bool Equals(RopePlayerConstraintSettings other)
    {
        return PullSpeed.Equals(other.PullSpeed)
            && TargetPullShare.Equals(other.TargetPullShare)
            && HolderReactionShare.Equals(other.HolderReactionShare)
            && TautDeadZone.Equals(other.TautDeadZone)
            && MaximumStretch.Equals(other.MaximumStretch)
            && SwingGravityMultiplier.Equals(other.SwingGravityMultiplier)
            && SwingInputAcceleration.Equals(other.SwingInputAcceleration)
            && SwingDamping.Equals(other.SwingDamping)
            && MaximumSwingSpeed.Equals(other.MaximumSwingSpeed)
            && SwingTautThreshold.Equals(other.SwingTautThreshold)
            && WallContactGraceDuration.Equals(other.WallContactGraceDuration)
            && PositionDeadZone.Equals(other.PositionDeadZone)
            && PositionCorrectionSpeed.Equals(other.PositionCorrectionSpeed)
            && PositionCorrectionAcceleration.Equals(other.PositionCorrectionAcceleration)
            && MaximumAnchorTransferSpeed.Equals(other.MaximumAnchorTransferSpeed)
            && GroundedReleaseDelay.Equals(other.GroundedReleaseDelay)
            && UpwardPullThreshold.Equals(other.UpwardPullThreshold)
            && WallJumpOutwardSpeed.Equals(other.WallJumpOutwardSpeed)
            && WallJumpUpwardSpeed.Equals(other.WallJumpUpwardSpeed)
            && WallJumpCooldown.Equals(other.WallJumpCooldown);
    }

    public override bool Equals(object obj)
    {
        return obj is RopePlayerConstraintSettings other && Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.Add(PullSpeed);
        hash.Add(TargetPullShare);
        hash.Add(HolderReactionShare);
        hash.Add(TautDeadZone);
        hash.Add(MaximumStretch);
        hash.Add(SwingGravityMultiplier);
        hash.Add(SwingInputAcceleration);
        hash.Add(SwingDamping);
        hash.Add(MaximumSwingSpeed);
        hash.Add(SwingTautThreshold);
        hash.Add(WallContactGraceDuration);
        hash.Add(PositionDeadZone);
        hash.Add(PositionCorrectionSpeed);
        hash.Add(PositionCorrectionAcceleration);
        hash.Add(MaximumAnchorTransferSpeed);
        hash.Add(GroundedReleaseDelay);
        hash.Add(UpwardPullThreshold);
        hash.Add(WallJumpOutwardSpeed);
        hash.Add(WallJumpUpwardSpeed);
        hash.Add(WallJumpCooldown);
        return hash.ToHashCode();
    }
}

public readonly struct RopeAttachment
{
    public RopeAttachment(NetworkObject target, RopeTargetKind kind, Vector3 localPoint)
    {
        Target = target;
        Kind = kind;
        LocalPoint = localPoint;
    }

    public NetworkObject Target { get; }
    public RopeTargetKind Kind { get; }
    public Vector3 LocalPoint { get; }
}

public interface IRopeAttachable
{
    bool TryCreateRopeAttachment(RopeToolController rope, Vector3 hitPoint, out RopeAttachment attachment);
}
