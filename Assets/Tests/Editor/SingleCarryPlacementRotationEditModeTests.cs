using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class SingleCarryPlacementRotationEditModeTests
{
    private static readonly Type StateType = Type.GetType("SingleCarryPlacementRotationState, Assembly-CSharp", true);
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

    [Test]
    public void QuickTapAndLocalAxisOrderArePreserved()
    {
        object state = Create();
        Set(state, new Vector2(0, 1), false, 0);
        Set(state, new Vector2(1, 1), false, .01);
        Set(state, Vector2.zero, false, .02);
        Quaternion expected = Quaternion.AngleAxis(1, Vector3.right) * Quaternion.AngleAxis(-1, Vector3.forward);
        AssertRotation(state, expected);
    }

    [Test]
    public void DirectionReversalStartsANewDelayAndNeutralControlDoesNotRotate()
    {
        object state = Create();
        Vector2 up = new(0f, 1f);
        Set(state, up, false, 0d);
        Advance(state, up, false, .35d);
        Quaternion afterFirstRepeat = GetLocal(state);
        Set(state, new Vector2(0f, -1f), false, .36d);
        Quaternion afterReverse = afterFirstRepeat * Quaternion.AngleAxis(-1f, Vector3.right);
        AssertRotation(state, afterReverse);
        Advance(state, new Vector2(0f, -1f), false, .709d);
        AssertRotation(state, afterReverse);
        Advance(state, new Vector2(0f, -1f), false, .71d);
        AssertRotation(state, afterReverse * Quaternion.AngleAxis(-1f, Vector3.right));
        Set(state, Vector2.zero, true, .72d);
        Quaternion neutral = GetLocal(state);
        Advance(state, Vector2.zero, true, 5d, 0f);
        AssertRotation(state, neutral);
    }

    [Test]
    public void XStepPrecedesHorizontalRepeatDueAtTheSameInputTimestamp()
    {
        object state = Create();
        Vector2 right = new(1f, 0f);
        Set(state, right, false, 0d);
        Set(state, new Vector2(1f, 1f), false, .35d);
        Quaternion expected = Quaternion.AngleAxis(-1f, Vector3.forward)
            * Quaternion.AngleAxis(1f, Vector3.right)
            * Quaternion.AngleAxis(-1f, Vector3.forward);
        Quaternion actual = GetLocal(state);
        if (Quaternion.Dot(actual, expected) < 0f)
            expected = new Quaternion(-expected.x, -expected.y, -expected.z, -expected.w);
        Assert.That(Mathf.Abs(actual.x - expected.x), Is.LessThan(0.000001f));
        Assert.That(Mathf.Abs(actual.y - expected.y), Is.LessThan(0.000001f));
        Assert.That(Mathf.Abs(actual.z - expected.z), Is.LessThan(0.000001f));
        Assert.That(Mathf.Abs(actual.w - expected.w), Is.LessThan(0.000001f),
            "Compare quaternion components because Quaternion.Angle hides this very small order difference.");
    }

    [Test]
    public void FullTurnAndModifierSwitchKeepLocalAxesAndIndependentClock()
    {
        object state = Create();
        Set(state, new Vector2(1, 0), false, 0);
        Advance(state, new Vector2(1, 0), false, 4.33);
        Assert.That(Quaternion.Angle(GetLocal(state), Quaternion.identity), Is.LessThan(.1f));
        Reset(state);
        Set(state, new Vector2(1, 1), false, 0);
        Set(state, new Vector2(1, 1), true, .1);
        Advance(state, new Vector2(1, 1), true, .35);
        Quaternion expected = Quaternion.AngleAxis(1, Vector3.right) * Quaternion.AngleAxis(-1, Vector3.forward)
            * Quaternion.AngleAxis(1, Vector3.up) * Quaternion.AngleAxis(1, Vector3.right);
        AssertRotation(state, expected);
    }

    [TestCase(30)] [TestCase(60)] [TestCase(144)]
    public void TwoAxisSequenceIsFrameRateIndependent(int fps)
    {
        Assert.That(Quaternion.Angle(RunSequence(fps), RunSequence(30)), Is.LessThan(.1f));
    }

    private static Quaternion RunSequence(int fps)
    {
        object state = Create();
        Vector2 both = new(1, 1);
        Set(state, both, false, 0);
        double dt = 1d / fps;
        for (int frame = 1; frame / (double)fps < .7d; frame++)
            Advance(state, both, false, frame * dt);
        Set(state, both, true, .7);
        for (int frame = (int)System.Math.Floor(.7d * fps) + 1; frame / (double)fps < 1.2d; frame++)
            Advance(state, both, true, frame * dt);
        Advance(state, both, true, 1.2);
        return GetLocal(state);
    }
    private static object Create() => Activator.CreateInstance(StateType);
    private static void Set(object s, Vector2 v, bool c, double t) => StateType.GetMethod("SetInput", PublicInstance, null, new[] { typeof(Vector2), typeof(bool), typeof(double) }, null).Invoke(s, new object[] { v,c,t });
    private static void Advance(object s, Vector2 v, bool c, double t) => Advance(s, v, c, t, 90f);
    private static void Advance(object s, Vector2 v, bool c, double t, float rate) => StateType.GetMethod("Advance", PublicInstance).Invoke(s, new object[] { v,c,t,rate });
    private static void Reset(object s) => StateType.GetMethod("Reset", PublicInstance).Invoke(s,null);
    private static Quaternion GetLocal(object s) => (Quaternion)StateType.GetProperty("LocalRotation").GetValue(s);
    private static void AssertRotation(object s, Quaternion q) => Assert.That(Quaternion.Angle(GetLocal(s),q), Is.LessThan(.1f));
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
