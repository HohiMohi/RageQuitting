using System;
using UnityEngine;

[Flags]
public enum BridgeDeckPanelQuarterMask : byte
{
    None = 0,
    NegativeXNegativeZ = 1 << 0,
    PositiveXNegativeZ = 1 << 1,
    NegativeXPositiveZ = 1 << 2,
    PositiveXPositiveZ = 1 << 3,
    All = NegativeXNegativeZ | PositiveXNegativeZ | NegativeXPositiveZ | PositiveXPositiveZ
}

public enum BridgeDeckPanelPoseFailure : byte
{
    None,
    HorizontalOffset,
    HeightOffset,
    Orientation
}

public enum BridgeDeckPanelNailRejection : byte
{
    None,
    NotEmbeddedPair,
    FirstTargetIsNotPanel,
    EntryIsNotOnTop,
    EntryOutsidePanel,
    OnQuarterBoundary
}

internal readonly struct BridgeDeckPanelMinimumCorrection
{
    public readonly float RightMeters;
    public readonly float ForwardMeters;
    public readonly float HeightMeters;
    public readonly int LocalYRotationDegrees;
    public readonly bool ResetTiltRequired;

    public BridgeDeckPanelMinimumCorrection(float rightMeters, float forwardMeters, float heightMeters,
        int localYRotationDegrees, bool resetTiltRequired)
    {
        RightMeters = rightMeters;
        ForwardMeters = forwardMeters;
        HeightMeters = heightMeters;
        LocalYRotationDegrees = localYRotationDegrees;
        ResetTiltRequired = resetTiltRequired;
    }
}

/// <summary>Pure, deterministic rules for evaluating a physical deck panel and its four nail quarters.</summary>
public static class BridgeDeckPanelStage2Assessment
{
    /// <summary>Returns the shortest position correction into the valid pose region and a validated integer yaw correction.</summary>
    internal static BridgeDeckPanelMinimumCorrection GetMinimumCorrection(Transform slot, Vector3 panelPosition,
        Quaternion panelRotation, Transform movementBasis, bool directionalHorizontal, float maximumAlong,
        float maximumAcross, float maximumRadial, float maximumHeight, float maximumOrientationDegrees)
    {
        if (slot == null || movementBasis == null) return default;
        Vector3 delta = panelPosition - slot.position;
        float along = Vector3.Dot(delta, slot.right.normalized);
        float across = Vector3.Dot(delta, slot.forward.normalized);
        float allowedAlong = Mathf.Max(0f, directionalHorizontal ? maximumAlong : maximumRadial);
        float allowedAcross = Mathf.Max(0f, directionalHorizontal ? maximumAcross : maximumRadial);
        float correctionAlong;
        float correctionAcross;
        if (directionalHorizontal)
        {
            correctionAlong = Mathf.Clamp(along, -allowedAlong, allowedAlong) - along;
            correctionAcross = Mathf.Clamp(across, -allowedAcross, allowedAcross) - across;
        }
        else
        {
            Vector2 radial = new(along, across);
            Vector2 clamped = radial.magnitude > allowedAlong && radial.sqrMagnitude > 0f
                ? radial.normalized * allowedAlong : radial;
            correctionAlong = clamped.x - along;
            correctionAcross = clamped.y - across;
        }

        float height = Vector3.Dot(delta, slot.up.normalized);
        float correctionHeight = Mathf.Clamp(height, -Mathf.Max(0f, maximumHeight),
            Mathf.Max(0f, maximumHeight)) - height;
        Vector3 worldCorrection = slot.right.normalized * correctionAlong
            + slot.forward.normalized * correctionAcross;
        Vector3 horizontalCorrection = Vector3.ProjectOnPlane(worldCorrection, Vector3.up);
        Vector3 movementRight = Vector3.ProjectOnPlane(movementBasis.right, Vector3.up).normalized;
        Vector3 movementForward = Vector3.ProjectOnPlane(movementBasis.forward, Vector3.up).normalized;
        float right = Vector3.Dot(horizontalCorrection, movementRight);
        float forward = Vector3.Dot(horizontalCorrection, movementForward);

        Quaternion relative = Quaternion.Inverse(slot.rotation) * panelRotation;
        float swingTwistMagnitude = Mathf.Sqrt(relative.y * relative.y + relative.w * relative.w);
        int bestDelta = 0;
        int bestMagnitude = int.MaxValue;
        bool found = false;
        if (Quaternion.Angle(relative, Quaternion.identity) <= maximumOrientationDegrees
            || Quaternion.Angle(relative, Quaternion.AngleAxis(180f, Vector3.up)) <= maximumOrientationDegrees)
        {
            found = true;
            bestMagnitude = 0;
        }
        else if (swingTwistMagnitude > 0f && maximumOrientationDegrees >= 0f)
        {
            float threshold = Mathf.Cos(maximumOrientationDegrees * Mathf.Deg2Rad * 0.5f);
            float twistHalfAngle = Mathf.Atan2(relative.y, relative.w) * Mathf.Rad2Deg;
            for (int target = 0; target < 2; target++)
            {
                float center = Mathf.DeltaAngle(0f, 2f * ((target == 0 ? 0f : 90f) - twistHalfAngle));
                float intervalHalfWidth = threshold <= swingTwistMagnitude
                    ? 2f * Mathf.Acos(Mathf.Clamp(threshold / swingTwistMagnitude, -1f, 1f)) * Mathf.Rad2Deg
                    : 0f;
                ConsiderLocalYawCandidate(relative, center, intervalHalfWidth, maximumOrientationDegrees,
                    ref bestDelta, ref bestMagnitude, ref found);
            }
        }
        return new BridgeDeckPanelMinimumCorrection(right, forward, correctionHeight,
            found ? bestDelta : 0, !found);
    }

    private static void ConsiderLocalYawCandidate(Quaternion relative, float center, float halfWidth,
        float maximumOrientationDegrees, ref int bestDelta, ref int bestMagnitude, ref bool found)
    {
        // The closest integer in the valid interval is either near its center or one of its boundaries.
        int roundedCenter = Mathf.RoundToInt(center);
        int lowerBoundary = Mathf.CeilToInt(center - halfWidth);
        int upperBoundary = Mathf.FloorToInt(center + halfWidth);
        ConsiderLocalYaw(relative, roundedCenter, maximumOrientationDegrees, ref bestDelta, ref bestMagnitude, ref found);
        ConsiderLocalYaw(relative, roundedCenter - 1, maximumOrientationDegrees, ref bestDelta, ref bestMagnitude, ref found);
        ConsiderLocalYaw(relative, roundedCenter + 1, maximumOrientationDegrees, ref bestDelta, ref bestMagnitude, ref found);
        ConsiderLocalYaw(relative, lowerBoundary, maximumOrientationDegrees, ref bestDelta, ref bestMagnitude, ref found);
        ConsiderLocalYaw(relative, lowerBoundary + 1, maximumOrientationDegrees, ref bestDelta, ref bestMagnitude, ref found);
        ConsiderLocalYaw(relative, lowerBoundary - 1, maximumOrientationDegrees, ref bestDelta, ref bestMagnitude, ref found);
        ConsiderLocalYaw(relative, upperBoundary, maximumOrientationDegrees, ref bestDelta, ref bestMagnitude, ref found);
        ConsiderLocalYaw(relative, upperBoundary - 1, maximumOrientationDegrees, ref bestDelta, ref bestMagnitude, ref found);
        ConsiderLocalYaw(relative, upperBoundary + 1, maximumOrientationDegrees, ref bestDelta, ref bestMagnitude, ref found);
    }

    private static void ConsiderLocalYaw(Quaternion relative, int candidateDegrees, float maximumOrientationDegrees,
        ref int bestDelta, ref int bestMagnitude, ref bool found)
    {
        int signedDelta = Mathf.RoundToInt(Mathf.DeltaAngle(0f, candidateDegrees));
        Quaternion candidate = relative * Quaternion.AngleAxis(signedDelta, Vector3.up);
        float error = Mathf.Min(Quaternion.Angle(candidate, Quaternion.identity),
            Quaternion.Angle(candidate, Quaternion.AngleAxis(180f, Vector3.up)));
        if (error > maximumOrientationDegrees) return;
        int magnitude = Mathf.Abs(signedDelta);
        if (magnitude >= bestMagnitude) return;
        bestMagnitude = magnitude;
        bestDelta = signedDelta;
        found = true;
    }

    public static BridgeDeckPanelPoseFailure EvaluatePose(Transform panel, Transform slot,
        float maximumHorizontalOffset, float maximumHeightError, float maximumOrientationErrorDegrees)
    {
        if (panel == null || slot == null) return BridgeDeckPanelPoseFailure.HorizontalOffset;
        return EvaluatePose(panel.position, panel.rotation, slot, maximumHorizontalOffset,
            maximumHeightError, maximumOrientationErrorDegrees);
    }

    public static BridgeDeckPanelPoseFailure EvaluatePose(Vector3 panelPosition, Quaternion panelRotation, Transform slot,
        float maximumHorizontalOffset, float maximumHeightError, float maximumOrientationErrorDegrees)
    {
        if (slot == null) return BridgeDeckPanelPoseFailure.HorizontalOffset;
        Vector3 positionDelta = panelPosition - slot.position;
        float horizontalX = Vector3.Dot(positionDelta, slot.right.normalized);
        float horizontalZ = Vector3.Dot(positionDelta, slot.forward.normalized);
        if (new Vector2(horizontalX, horizontalZ).magnitude > maximumHorizontalOffset)
            return BridgeDeckPanelPoseFailure.HorizontalOffset;
        if (Mathf.Abs(Vector3.Dot(positionDelta, slot.up.normalized)) > maximumHeightError)
            return BridgeDeckPanelPoseFailure.HeightOffset;

        Quaternion relativeRotation = Quaternion.Inverse(slot.rotation) * panelRotation;
        Quaternion flippedYaw = Quaternion.AngleAxis(180f, Vector3.up);
        float uprightError = Quaternion.Angle(relativeRotation, Quaternion.identity);
        float flippedError = Quaternion.Angle(relativeRotation, flippedYaw);
        float orientationError = Mathf.Min(uprightError, flippedError);
        return orientationError <= maximumOrientationErrorDegrees
            ? BridgeDeckPanelPoseFailure.None : BridgeDeckPanelPoseFailure.Orientation;
    }

    /// <summary>Evaluates independent bridge-longitudinal and bridge-lateral offsets.</summary>
    public static BridgeDeckPanelPoseFailure EvaluatePose(Transform panel, Transform slot,
        float maximumAlongOffset, float maximumAcrossOffset, float maximumHeightError,
        float maximumOrientationErrorDegrees)
    {
        if (panel == null || slot == null) return BridgeDeckPanelPoseFailure.HorizontalOffset;
        return EvaluatePose(panel.position, panel.rotation, slot, maximumAlongOffset,
            maximumAcrossOffset, maximumHeightError, maximumOrientationErrorDegrees);
    }

    public static BridgeDeckPanelPoseFailure EvaluatePose(Vector3 panelPosition, Quaternion panelRotation, Transform slot,
        float maximumAlongOffset, float maximumAcrossOffset, float maximumHeightError,
        float maximumOrientationErrorDegrees)
    {
        if (slot == null) return BridgeDeckPanelPoseFailure.HorizontalOffset;
        Vector3 positionDelta = panelPosition - slot.position;
        float along = Vector3.Dot(positionDelta, slot.right.normalized);
        float across = Vector3.Dot(positionDelta, slot.forward.normalized);
        if (Mathf.Abs(along) > Mathf.Max(0f, maximumAlongOffset)
            || Mathf.Abs(across) > Mathf.Max(0f, maximumAcrossOffset))
            return BridgeDeckPanelPoseFailure.HorizontalOffset;
        if (Mathf.Abs(Vector3.Dot(positionDelta, slot.up.normalized)) > maximumHeightError)
            return BridgeDeckPanelPoseFailure.HeightOffset;

        Quaternion relativeRotation = Quaternion.Inverse(slot.rotation) * panelRotation;
        Quaternion flippedYaw = Quaternion.AngleAxis(180f, Vector3.up);
        float uprightError = Quaternion.Angle(relativeRotation, Quaternion.identity);
        float flippedError = Quaternion.Angle(relativeRotation, flippedYaw);
        float orientationError = Mathf.Min(uprightError, flippedError);
        return orientationError <= maximumOrientationErrorDegrees
            ? BridgeDeckPanelPoseFailure.None : BridgeDeckPanelPoseFailure.Orientation;
    }

    public static void GetPoseError(Transform slot, Vector3 panelPosition, Quaternion panelRotation,
        out float alongMeters, out float acrossMeters, out float heightMeters, out float orientationDegrees)
    {
        if (slot == null)
        {
            alongMeters = acrossMeters = heightMeters = orientationDegrees = 0f;
            return;
        }
        Vector3 delta = panelPosition - slot.position;
        alongMeters = Vector3.Dot(delta, slot.right.normalized);
        acrossMeters = Vector3.Dot(delta, slot.forward.normalized);
        heightMeters = Vector3.Dot(delta, slot.up.normalized);
        Quaternion relative = Quaternion.Inverse(slot.rotation) * panelRotation;
        float uprightError = Quaternion.Angle(relative, Quaternion.identity);
        float flippedError = Quaternion.Angle(relative, Quaternion.AngleAxis(180f, Vector3.up));
        orientationDegrees = Mathf.Min(uprightError, flippedError);
    }

    public static bool TryGetNailQuarter(MountingNailAttachmentInfo nail, Transform physicalPanel,
        Vector3 panelDimensions, float axisExclusionHalfWidth, out BridgeDeckPanelQuarterMask quarter,
        out BridgeDeckPanelNailRejection rejection)
    {
        quarter = BridgeDeckPanelQuarterMask.None;
        rejection = BridgeDeckPanelNailRejection.None;
        if (nail.State != MountingNailState.EmbeddedPair)
        {
            rejection = BridgeDeckPanelNailRejection.NotEmbeddedPair;
            return false;
        }
        if (physicalPanel == null || nail.FirstTarget == null
            || (nail.FirstTarget != physicalPanel && !nail.FirstTarget.IsChildOf(physicalPanel)))
        {
            rejection = BridgeDeckPanelNailRejection.FirstTargetIsNotPanel;
            return false;
        }

        Vector3 worldEntry = nail.FirstTarget.TransformPoint(nail.EntryLocal);
        Vector3 worldAxis = nail.FirstTarget.TransformDirection(nail.AxisLocal).normalized;
        Vector3 localEntry = physicalPanel.InverseTransformPoint(worldEntry);
        Vector3 localAxis = physicalPanel.InverseTransformDirection(worldAxis).normalized;
        if (localEntry.y < 0f || localAxis.y >= 0f)
        {
            rejection = BridgeDeckPanelNailRejection.EntryIsNotOnTop;
            return false;
        }
        float halfX = panelDimensions.x * 0.5f;
        float halfZ = panelDimensions.z * 0.5f;
        if (Mathf.Abs(localEntry.x) > halfX || Mathf.Abs(localEntry.z) > halfZ)
        {
            rejection = BridgeDeckPanelNailRejection.EntryOutsidePanel;
            return false;
        }
        // Absorb transform round-off when a child target is converted into panel-local coordinates.
        const float comparisonEpsilonMeters = 0.000001f;
        if (Mathf.Abs(localEntry.x) <= axisExclusionHalfWidth + comparisonEpsilonMeters
            || Mathf.Abs(localEntry.z) <= axisExclusionHalfWidth + comparisonEpsilonMeters)
        {
            rejection = BridgeDeckPanelNailRejection.OnQuarterBoundary;
            return false;
        }

        bool positiveX = localEntry.x > 0f;
        bool positiveZ = localEntry.z > 0f;
        quarter = positiveX
            ? (positiveZ ? BridgeDeckPanelQuarterMask.PositiveXPositiveZ : BridgeDeckPanelQuarterMask.PositiveXNegativeZ)
            : (positiveZ ? BridgeDeckPanelQuarterMask.NegativeXPositiveZ : BridgeDeckPanelQuarterMask.NegativeXNegativeZ);
        return true;
    }
}
