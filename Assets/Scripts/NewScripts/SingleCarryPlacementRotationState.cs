using UnityEngine;

/// <summary>Deterministic world-axis placement rotation, independent of quaternion accumulation.</summary>
public sealed class SingleCarryPlacementRotationState
{
    private const double HoldRepeatDelaySeconds = 0.35d;
    private struct AxisState
    {
        public int Direction;
        public double PressedAt;
        public int RepeatsApplied;
    }

    private AxisState xAxis;
    private AxisState zAxis;
    private AxisState yAxis;
    private int worldXDegrees;
    private int worldZDegrees;
    private int worldYDegrees;

    public int WorldXDegrees => worldXDegrees;
    public int WorldZDegrees => worldZDegrees;
    public int WorldYDegrees => worldYDegrees;

    /// <summary>Consume an input edge. A newly pressed or reversed direction always moves one degree immediately.</summary>
    public void SetInput(Vector2 input, bool controlModifier, double timestamp)
    {
        SetAxis(ref xAxis, Sign(input.y), timestamp, true);
        SetAxis(ref zAxis, controlModifier ? 0 : -Sign(input.x), timestamp, false);
        SetAxis(ref yAxis, controlModifier ? Sign(input.x) : 0, timestamp, false, true);
    }

    /// <summary>Advance repeat timers and recover a missed input edge from the current action value.</summary>
    public void Advance(Vector2 input, bool controlModifier, double now, float repeatDegreesPerSecond = 90f)
    {
        SetInput(input, controlModifier, now);
        AdvanceAxis(ref xAxis, now, repeatDegreesPerSecond, true);
        AdvanceAxis(ref zAxis, now, repeatDegreesPerSecond, false);
        AdvanceAxis(ref yAxis, now, repeatDegreesPerSecond, false, true);
    }

    public void ResetRepeatState()
    {
        xAxis = default;
        zAxis = default;
        yAxis = default;
    }

    public void Reset()
    {
        ResetRepeatState();
        worldXDegrees = 0;
        worldZDegrees = 0;
        worldYDegrees = 0;
    }

    public Quaternion BuildWorldRotation(Quaternion cameraYaw)
    {
        Vector3 forward = Vector3.ProjectOnPlane(cameraYaw * Vector3.forward, Vector3.up);
        float yaw = forward.sqrMagnitude < 0.000001f
            ? 0f
            : Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
        int roundedYaw = Mathf.FloorToInt(yaw + 0.5f) % 360;
        Quaternion baseYaw = Quaternion.Euler(0f, roundedYaw, 0f);
        Quaternion worldX = Quaternion.AngleAxis(worldXDegrees, Vector3.right);
        Quaternion worldZ = Quaternion.AngleAxis(worldZDegrees, Vector3.forward);
        Quaternion worldY = Quaternion.AngleAxis(worldYDegrees, Vector3.up);
        return worldY * worldX * worldZ * baseYaw;
    }

    private void SetAxis(ref AxisState axis, int direction, double timestamp, bool isX, bool isY = false)
    {
        if (axis.Direction == direction) return;
        axis.Direction = direction;
        axis.PressedAt = direction == 0 ? 0d : timestamp;
        axis.RepeatsApplied = 0;
        if (direction == 0) return;
        if (isX) worldXDegrees = WrapDegrees(worldXDegrees + direction);
        else if (isY) worldYDegrees = WrapDegrees(worldYDegrees + direction);
        else worldZDegrees = WrapDegrees(worldZDegrees + direction);
    }

    private void AdvanceAxis(ref AxisState axis, double now, float repeatDegreesPerSecond, bool isX, bool isY = false)
    {
        if (axis.Direction == 0 || repeatDegreesPerSecond <= 0f) return;
        double elapsedAfterDelay = now - axis.PressedAt - HoldRepeatDelaySeconds;
        if (elapsedAfterDelay < -0.000001d) return;
        int desiredRepeats = 1 + Mathf.FloorToInt((float)(System.Math.Max(0d, elapsedAfterDelay) * repeatDegreesPerSecond + 0.00001d));
        int delta = desiredRepeats - axis.RepeatsApplied;
        if (delta <= 0) return;
        axis.RepeatsApplied = desiredRepeats;
        int step = axis.Direction * delta;
        if (isX) worldXDegrees = WrapDegrees(worldXDegrees + step);
        else if (isY) worldYDegrees = WrapDegrees(worldYDegrees + step);
        else worldZDegrees = WrapDegrees(worldZDegrees + step);
    }

    private static int WrapDegrees(int degrees)
    {
        int wrapped = degrees % 360;
        return wrapped < 0 ? wrapped + 360 : wrapped;
    }

    private static int Sign(float value) => value > 0.01f ? 1 : value < -0.01f ? -1 : 0;
}
