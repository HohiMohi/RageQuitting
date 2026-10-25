using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class SingleCarryPlacementRotationEditModeTests
{
    private static readonly Type StateType = Type.GetType("SingleCarryPlacementRotationState, Assembly-CSharp", true);
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

    [Test]
    public void PressAndReleaseBetweenFramesStillProducesOneWorldAxisStep()
    {
        object state = CreateState();
        SetInput(state, new Vector2(0f, 1f), 4d);
        SetInput(state, Vector2.zero, 4.005d);
        Assert.That(XDegrees(state), Is.EqualTo(1));
        Assert.That(ZDegrees(state), Is.Zero);
        Assert.That(Quaternion.Angle(Build(state, Quaternion.identity), Quaternion.AngleAxis(1f, Vector3.right)), Is.LessThan(.001f));
    }

    [Test]
    public void HoldDelayRepeatAndDirectionReversalAreTimeBasedAndIndependent()
    {
        object state = CreateState();
        SetInput(state, new Vector2(0f, 1f), 0d);
        Advance(state, new Vector2(0f, 1f), .349d);
        Assert.That(XDegrees(state), Is.EqualTo(1), "A short press is exactly one degree before the hold delay.");
        Advance(state, new Vector2(0f, 1f), .35d);
        Assert.That(XDegrees(state), Is.EqualTo(2), "The first repeat occurs at the .35 second hold threshold.");
        SetInput(state, new Vector2(0f, -1f), .36d);
        Assert.That(XDegrees(state), Is.EqualTo(1), "Changing direction adds one immediate reverse step and starts a fresh delay.");
        Advance(state, new Vector2(0f, -1f), .709d);
        Assert.That(XDegrees(state), Is.EqualTo(1));
        Advance(state, new Vector2(0f, -1f), .71d);
        Assert.That(XDegrees(state), Is.EqualTo(0));

        SetInput(state, new Vector2(1f, 1f), 1d);
        Assert.That(XDegrees(state), Is.EqualTo(1));
        Assert.That(ZDegrees(state), Is.EqualTo(359), "Left/right retain their existing negative Z sign.");
        SetInput(state, new Vector2(0f, 1f), 1.1d);
        Assert.That(XDegrees(state), Is.EqualTo(1), "Releasing one axis leaves the other axis unchanged.");
        Assert.That(ZDegrees(state), Is.EqualTo(359), "The released Z axis does not accumulate delayed repeats.");
    }

    [Test]
    public void AQuickTapOnTheOtherAxisDoesNotResetTheHeldAxisRepeatClock()
    {
        object state = CreateState();
        Vector2 up = new(0f, 1f);
        SetInput(state, up, 0d);
        Advance(state, up, .20d);
        SetInput(state, new Vector2(1f, 1f), .20d);
        SetInput(state, up, .205d);
        Assert.That(XDegrees(state), Is.EqualTo(1), "The still-held X axis does not get a duplicate press step.");
        Assert.That(ZDegrees(state), Is.EqualTo(359), "The brief Z-axis tap is preserved as one step.");
        Advance(state, up, .35d);
        Assert.That(XDegrees(state), Is.EqualTo(2), "The first axis continues on its original hold schedule.");

        SetInput(state, new Vector2(0f, -1f), .36d);
        Assert.That(XDegrees(state), Is.EqualTo(1), "Reversing the held axis produces an immediate new step.");
        Advance(state, new Vector2(0f, -1f), .709d);
        Assert.That(XDegrees(state), Is.EqualTo(1), "Reversal restarts the .35 second delay.");
    }

    [Test]
    public void RepeatResultDoesNotDependOnThirtySixtyOrOneHundredFortyFourFrameSampling()
    {
        int at30 = RunHoldAtFrameRate(30);
        int at60 = RunHoldAtFrameRate(60);
        int at144 = RunHoldAtFrameRate(144);
        Assert.That(at30, Is.EqualTo(15));
        Assert.That(at60, Is.EqualTo(at30));
        Assert.That(at144, Is.EqualTo(at30));
    }

    [Test]
    public void OppositeDirectionsCancelAndBothCountersWrapWithoutFractionalDrift()
    {
        object state = CreateState();
        SetInput(state, new Vector2(1f, 1f), 0d);
        SetInput(state, new Vector2(0f, 0f), .01d);
        SetInput(state, new Vector2(-1f, -1f), .02d);
        Assert.That(XDegrees(state), Is.Zero);
        Assert.That(ZDegrees(state), Is.Zero);
        for (int i = 0; i < 361; i++)
        {
            SetInput(state, new Vector2(0f, 1f), 1d + i * .01d);
            SetInput(state, Vector2.zero, 1.001d + i * .01d);
        }
        Assert.That(XDegrees(state), Is.EqualTo(1));
        Assert.That(ZDegrees(state), Is.Zero);
    }

    [Test]
    public void CameraYawIsRoundedToWorldDegreesAcrossTheZeroWrapAndOffsetsUseWorldAxes()
    {
        object state = CreateState();
        Assert.That(Quaternion.Angle(Build(state, Quaternion.Euler(0f, 359.6f, 0f)), Quaternion.identity), Is.LessThan(.01f));
        Assert.That(Quaternion.Angle(Build(state, Quaternion.Euler(0f, .49f, 0f)), Quaternion.identity), Is.LessThan(.01f));
        Assert.That(Quaternion.Angle(Build(state, Quaternion.Euler(0f, .51f, 0f)), Quaternion.Euler(0f, 1f, 0f)), Is.LessThan(.01f));

        SetInput(state, new Vector2(0f, 1f), 0d);
        Quaternion expected = Quaternion.AngleAxis(1f, Vector3.right) * Quaternion.Euler(0f, 90f, 0f);
        Assert.That(Quaternion.Angle(Build(state, Quaternion.Euler(0f, 90f, 0f)), expected), Is.LessThan(.01f),
            "The X adjustment is composed about the world X axis after a 90-degree camera yaw.");
    }

    [Test]
    public void ResetStartsAFreshHoldDelayInsteadOfReplayingBlockedTime()
    {
        object state = CreateState();
        SetInput(state, new Vector2(0f, 1f), 0d);
        Advance(state, new Vector2(0f, 1f), .35d);
        StateType.GetMethod("ResetRepeatState", PublicInstance).Invoke(state, null);
        Advance(state, new Vector2(0f, 1f), 5d);
        Assert.That(XDegrees(state), Is.EqualTo(3), "A still-held key begins a fresh immediate step when an unblocked attempt resumes.");
        Advance(state, new Vector2(0f, 1f), 5.349d);
        Assert.That(XDegrees(state), Is.EqualTo(3), "Time spent while blocked does not replay the hold repeat schedule.");
        Advance(state, new Vector2(0f, 1f), 5.35d);
        Assert.That(XDegrees(state), Is.EqualTo(4), "The fresh repeat delay begins when input is accepted again.");
        StateType.GetMethod("Reset", PublicInstance).Invoke(state, null);
        Assert.That(XDegrees(state), Is.Zero);
        Assert.That(ZDegrees(state), Is.Zero);
        Assert.That(Quaternion.Angle(Build(state, Quaternion.Euler(0f, 359.6f, 0f)), Quaternion.identity), Is.LessThan(.01f));
    }

    [Test]
    public void ControlSwitchesHorizontalHeldArrowToWorldYAndKeepsXRepeatIndependent()
    {
        object state = CreateState();
        Vector2 upAndRight = new(1f, 1f);
        SetInput(state, upAndRight, 10d);
        Assert.That(XDegrees(state), Is.EqualTo(1));
        Assert.That(ZDegrees(state), Is.EqualTo(359));
        Assert.That(YDegrees(state), Is.Zero);

        SetInput(state, upAndRight, true, 10.1d);
        Assert.That(XDegrees(state), Is.EqualTo(1), "Modifier changes do not retrigger held X.");
        Assert.That(ZDegrees(state), Is.EqualTo(359));
        Assert.That(YDegrees(state), Is.EqualTo(1), "Modifier transition immediately steps the newly selected Y axis.");

        Advance(state, upAndRight, true, 10.35d);
        Assert.That(XDegrees(state), Is.EqualTo(2), "The X repeat clock remains independent.");
        Assert.That(YDegrees(state), Is.EqualTo(1), "Y starts a fresh hold delay on modifier transition.");

        Advance(state, upAndRight, true, 10.45d);
        Assert.That(XDegrees(state), Is.EqualTo(11), "X continues its own original repeat schedule.");
        Assert.That(YDegrees(state), Is.EqualTo(2), "Y begins repeating after its independent .35 second delay.");

        SetInput(state, upAndRight, false, 10.46d);
        Assert.That(XDegrees(state), Is.EqualTo(11));
        Assert.That(YDegrees(state), Is.EqualTo(2));
        Assert.That(ZDegrees(state), Is.EqualTo(358), "Releasing Ctrl while Right remains held produces one additional Z step.");
    }

    [Test]
    public void ControlLeftAndRightUseWorldYAndQuaternionOrderIsYawThenZThenXThenY()
    {
        object state = CreateState();
        SetInput(state, new Vector2(-1f, 1f), true, 0d);
        Assert.That(XDegrees(state), Is.EqualTo(1));
        Assert.That(YDegrees(state), Is.EqualTo(359));
        Assert.That(ZDegrees(state), Is.Zero, "Ctrl+Left never produces an accidental Z step.");
        Quaternion expected = Quaternion.AngleAxis(359f, Vector3.up)
            * Quaternion.AngleAxis(1f, Vector3.right)
            * Quaternion.Euler(0f, 90f, 0f);
        Assert.That(Quaternion.Angle(Build(state, Quaternion.Euler(0f, 90f, 0f)), expected), Is.LessThan(.01f));

        SetInput(state, Vector2.zero, true, .01d);
        Assert.That(YDegrees(state), Is.EqualTo(359), "Ctrl alone does not rotate.");
        SetInput(state, new Vector2(1f, 0f), true, .02d);
        Assert.That(YDegrees(state), Is.Zero, "Y wraps at 360 degrees.");
    }

    [Test]
    public void YRepeatHasTheSameFrameIndependentCadenceAndFullTurnWrap()
    {
        int at30 = RunYHoldAtFrameRate(30);
        int at60 = RunYHoldAtFrameRate(60);
        int at144 = RunYHoldAtFrameRate(144);
        Assert.That(at30, Is.EqualTo(15));
        Assert.That(at60, Is.EqualTo(at30));
        Assert.That(at144, Is.EqualTo(at30));

        object state = CreateState();
        for (int i = 0; i < 361; i++)
        {
            SetInput(state, new Vector2(1f, 0f), true, 1d + i * .01d);
            SetInput(state, Vector2.zero, true, 1.001d + i * .01d);
        }
        Assert.That(YDegrees(state), Is.EqualTo(1));
        Assert.That(ZDegrees(state), Is.Zero);
    }

    [Test]
    public void MixedXYZOffsetsUseTheRequiredWorldAxisQuaternionOrderWithCameraYaw()
    {
        object state = CreateState();
        SetInput(state, new Vector2(1f, 1f), true, 0d);
        Quaternion yaw = Quaternion.Euler(0f, 37f, 0f);
        Quaternion expected = Quaternion.AngleAxis(1f, Vector3.up)
            * Quaternion.AngleAxis(1f, Vector3.right)
            * Quaternion.Euler(0f, 37f, 0f);
        Assert.That(Quaternion.Angle(Build(state, yaw), expected), Is.LessThan(.01f));
        SetInput(state, new Vector2(1f, 1f), false, .01d);
        expected = Quaternion.AngleAxis(1f, Vector3.up)
            * Quaternion.AngleAxis(1f, Vector3.right)
            * Quaternion.AngleAxis(359f, Vector3.forward)
            * Quaternion.Euler(0f, 37f, 0f);
        Assert.That(Quaternion.Angle(Build(state, yaw), expected), Is.LessThan(.01f));
    }

    private static int RunHoldAtFrameRate(int framesPerSecond)
    {
        object state = CreateState();
        Vector2 up = new(0f, 1f);
        SetInput(state, up, 0d);
        double step = 1d / framesPerSecond;
        for (double now = step; now < 4.5d; now += step) Advance(state, up, now);
        Advance(state, up, 4.5d);
        return XDegrees(state);
    }

    private static int RunYHoldAtFrameRate(int framesPerSecond)
    {
        object state = CreateState();
        Vector2 right = new(1f, 0f);
        SetInput(state, right, true, 0d);
        double step = 1d / framesPerSecond;
        for (double now = step; now < 4.5d; now += step) Advance(state, right, true, now);
        Advance(state, right, true, 4.5d);
        return YDegrees(state);
    }

    private static object CreateState() => Activator.CreateInstance(StateType);
    private static void SetInput(object state, Vector2 input, double time) => SetInput(state, input, false, time);
    private static void SetInput(object state, Vector2 input, bool control, double time)
        => StateType.GetMethod("SetInput", PublicInstance).Invoke(state, new object[] { input, control, time });
    private static void Advance(object state, Vector2 input, double time) => Advance(state, input, false, time);
    private static void Advance(object state, Vector2 input, bool control, double time)
        => StateType.GetMethod("Advance", PublicInstance).Invoke(state, new object[] { input, control, time, 90f });
    private static int XDegrees(object state) => (int)StateType.GetProperty("WorldXDegrees").GetValue(state);
    private static int ZDegrees(object state) => (int)StateType.GetProperty("WorldZDegrees").GetValue(state);
    private static int YDegrees(object state) => (int)StateType.GetProperty("WorldYDegrees").GetValue(state);
    private static Quaternion Build(object state, Quaternion yaw)
        => (Quaternion)StateType.GetMethod("BuildWorldRotation", PublicInstance).Invoke(state, new object[] { yaw });
}

public sealed class SingleCarryPlacementCameraFeedbackEditModeTests
{
    [Test]
    public void SuppressionRemovesMovementTurnAndActionOffsetsButPreservesDamage()
    {
        Type composerType = Type.GetType("PlayerCameraFeedbackComposer, Assembly-CSharp", true);
        GameObject root = new("Placement feedback composer test");
        try
        {
            GameObject baseTarget = new("Base target");
            baseTarget.transform.SetParent(root.transform, false);
            Component composer = root.AddComponent(composerType);
            composerType.GetField("baseCameraTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(composer, baseTarget.transform);
            composerType.GetMethod("EnsureOutputTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(composer, null);
            Transform output = (Transform)composerType.GetProperty("OutputTarget").GetValue(composer);

            composerType.GetMethod("SetMovementFeedback").Invoke(composer, new object[] { Vector3.right, Vector3.up });
            composerType.GetMethod("SetTurnFeedback").Invoke(composer, new object[] { Vector3.forward, Vector3.right });
            composerType.GetMethod("SetActionFeedback").Invoke(composer, new object[] { Vector3.up, Vector3.forward });
            composerType.GetMethod("SetDamageFeedback").Invoke(composer, new object[] { Vector3.left, new Vector3(0f, 2f, 0f) });

            composerType.GetMethod("SetPlacementModeSuppressed").Invoke(composer, new object[] { true });
            Assert.That(output.localPosition, Is.EqualTo(Vector3.left), "Placement mode retains the damage position channel.");
            Assert.That(Quaternion.Angle(output.localRotation, Quaternion.Euler(0f, 2f, 0f)), Is.LessThan(.001f),
                "Placement mode retains damage rotation while hiding ordinary camera feedback.");

            composerType.GetMethod("SetPlacementModeSuppressed").Invoke(composer, new object[] { false });
            composerType.GetMethod("ApplyOffsets", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(composer, null);
            Assert.That(output.localPosition, Is.EqualTo(Vector3.left), "Ordinary transient channels do not replay after exit.");
            Assert.That(Quaternion.Angle(output.localRotation, Quaternion.Euler(0f, 2f, 0f)), Is.LessThan(.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
