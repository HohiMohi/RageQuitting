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

/// <summary>Pure, deterministic rules for evaluating a physical deck panel and its four nail quarters.</summary>
public static class BridgeDeckPanelStage2Assessment
{
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
