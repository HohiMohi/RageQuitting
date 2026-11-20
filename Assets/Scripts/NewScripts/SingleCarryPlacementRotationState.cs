using UnityEngine;

/// <summary>Timed placement input accumulated as rotations about the object's current local axes.</summary>
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
    private Quaternion localRotation = Quaternion.identity;

    public Quaternion LocalRotation => localRotation;

    public void SetInput(Vector2 input, bool controlModifier, double timestamp)
        => Update(input, controlModifier, timestamp, 90f);

    public void SetInput(Vector2 input, bool controlModifier, double timestamp, float repeatDegreesPerSecond)
        => Update(input, controlModifier, timestamp, repeatDegreesPerSecond);

    public void Advance(Vector2 input, bool controlModifier, double now, float repeatDegreesPerSecond = 90f)
        => Update(input, controlModifier, now, repeatDegreesPerSecond);

    private void Update(Vector2 input, bool controlModifier, double timestamp, float repeatDegreesPerSecond)
    {
        // Drain events strictly before the new sample. Events at the sample are handled below
        // in X, then horizontal order, so a newly pressed X step wins a simultaneous tie.
        while (true)
        {
            double nextX = NextRepeat(xAxis, repeatDegreesPerSecond);
            double nextZ = NextRepeat(zAxis, repeatDegreesPerSecond);
            double nextY = NextRepeat(yAxis, repeatDegreesPerSecond);
            double next = System.Math.Min(nextX, System.Math.Min(nextZ, nextY));
            if (next >= timestamp - 0.000001d || double.IsPositiveInfinity(next)) break;
            if (nextX <= next + 0.000001d) ApplyRepeat(ref xAxis, Vector3.right);
            if (nextZ <= next + 0.000001d) ApplyRepeat(ref zAxis, Vector3.forward);
            if (nextY <= next + 0.000001d) ApplyRepeat(ref yAxis, Vector3.up);
        }

        UpdateAxis(ref xAxis, Sign(input.y), timestamp, repeatDegreesPerSecond, Vector3.right);
        UpdateAxis(ref zAxis, controlModifier ? 0 : -Sign(input.x), timestamp, repeatDegreesPerSecond, Vector3.forward);
        UpdateAxis(ref yAxis, controlModifier ? Sign(input.x) : 0, timestamp, repeatDegreesPerSecond, Vector3.up);
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
        localRotation = Quaternion.identity;
    }

    private void UpdateAxis(ref AxisState axis, int direction, double timestamp, float rate, Vector3 localAxis)
    {
        if (axis.Direction != direction)
        {
            axis.Direction = direction;
            axis.PressedAt = direction == 0 ? 0d : timestamp;
            axis.RepeatsApplied = 0;
            if (direction != 0) ApplyStep(localAxis, direction);
            return;
        }

        while (NextRepeat(axis, rate) <= timestamp + 0.000001d)
            ApplyRepeat(ref axis, localAxis);
    }

    private static double NextRepeat(AxisState axis, float rate)
    {
        if (axis.Direction == 0 || float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0f)
            return double.PositiveInfinity;
        return axis.PressedAt + HoldRepeatDelaySeconds + axis.RepeatsApplied / (double)rate;
    }

    private void ApplyRepeat(ref AxisState axis, Vector3 localAxis)
    {
        axis.RepeatsApplied++;
        ApplyStep(localAxis, axis.Direction);
    }

    private void ApplyStep(Vector3 axis, int direction)
        => localRotation = (localRotation * Quaternion.AngleAxis(direction, axis)).normalized;

    private static int Sign(float value) => value > 0.01f ? 1 : value < -0.01f ? -1 : 0;
}
