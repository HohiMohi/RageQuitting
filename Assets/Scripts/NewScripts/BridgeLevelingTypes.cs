using UnityEngine;

public enum SpiritLevelMeasurementAxis
{
    Length,
    Width
}

public enum LevelingConfirmationSourceType
{
    Component = 0,
    AdjustmentPoint = 1,
    MeasurementPoint = 2
}

public interface ILevelingConfirmationSource
{
    BridgeConstructionSite ConfirmationSite { get; }
    LevelingConfirmationSourceType ConfirmationSourceType { get; }
    int ConfirmationPointId { get; }
    Collider ConfirmationCollider { get; }
    bool IsLevelingConfirmationAvailable { get; }
}

public enum BridgeLevelingAdjustmentRole
{
    LengthIncrease,
    LengthDecrease,
    WidthIncrease,
    WidthDecrease
}

public static class BridgeLevelingAdjustmentRoleUtility
{
    public static void Resolve(BridgeLevelingAdjustmentRole role, out SpiritLevelMeasurementAxis axis, out int delta)
    {
        axis = role == BridgeLevelingAdjustmentRole.LengthIncrease || role == BridgeLevelingAdjustmentRole.LengthDecrease
            ? SpiritLevelMeasurementAxis.Length
            : SpiritLevelMeasurementAxis.Width;
        delta = role == BridgeLevelingAdjustmentRole.LengthIncrease || role == BridgeLevelingAdjustmentRole.WidthIncrease
            ? 1
            : -1;
    }
}

public enum SpiritLevelMeasurementPointId
{
    LengthLeft = 0,
    LengthRight = 1,
    WidthStart = 2,
    WidthEnd = 3
}

public interface ILevelingMeasurementTarget
{
    bool IsLevelingActive { get; }
    int MaximumLogicalTilt { get; }
    int GetLogicalTilt(SpiritLevelMeasurementAxis axis);
}

public static class BridgeLevelingUtility
{
    public static int RandomNonZeroTilt(int minimumAbsoluteTilt, int maximumAbsoluteTilt)
    {
        int maximum = Mathf.Max(1, maximumAbsoluteTilt);
        int minimum = Mathf.Clamp(minimumAbsoluteTilt, 1, maximum);
        int magnitude = Random.Range(minimum, maximum + 1);
        return Random.value < 0.5f ? -magnitude : magnitude;
    }

    public static float GetVisualAngle(
        int logicalTilt,
        int maximumLogicalTilt,
        int visuallyStraightRange,
        float maximumVisualTiltDegrees)
    {
        int maximum = Mathf.Max(1, maximumLogicalTilt);
        int straightRange = Mathf.Clamp(visuallyStraightRange, 0, maximum - 1);
        float magnitude = Mathf.Abs(logicalTilt);
        if (magnitude <= straightRange)
        {
            return 0f;
        }

        float normalized = Mathf.InverseLerp(straightRange, maximum, magnitude);
        return Mathf.Sign(logicalTilt) * normalized * Mathf.Max(0f, maximumVisualTiltDegrees);
    }
}
