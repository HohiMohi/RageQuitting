using NUnit.Framework;
using System;
using System.Reflection;
using UnityEngine;

public sealed class BridgeDeckPanelStage2AssessmentEditModeTests
{
    private const float AxisHalfWidth = 0.005f;
    private static readonly Vector3 Dimensions = new(2.64f, 0.2f, 2.4f);

    [Test]
    public void PoseAssessmentAcceptsConfiguredEdgesAndBothYawOrientationsWithoutChangingPose()
    {
        GameObject slot = new("slot");
        GameObject panel = new("panel");
        try
        {
            panel.transform.position = new Vector3(0.05f, 0.03f, 0f);
            panel.transform.rotation = Quaternion.Euler(0f, 3f, 0f);
            Vector3 positionBefore = panel.transform.position;
            Quaternion rotationBefore = panel.transform.rotation;
            AssertPose(panel.transform, slot.transform, "None");
            Assert.That(panel.transform.position, Is.EqualTo(positionBefore));
            Assert.That(panel.transform.rotation, Is.EqualTo(rotationBefore));

            panel.transform.position = new Vector3(0f, 0f, 0f);
            panel.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            AssertPose(panel.transform, slot.transform, "None");
            panel.transform.rotation = Quaternion.Euler(2.5f, 2.5f, 0f);
            AssertPose(panel.transform, slot.transform, "Orientation");

            panel.transform.rotation = Quaternion.identity;
            panel.transform.position = new Vector3(0.04f, 0f, 0.04f);
            AssertPose(panel.transform, slot.transform, "HorizontalOffset");
            slot.transform.localScale = new Vector3(2f, 1f, 3f);
            panel.transform.position = new Vector3(0.0501f, 0f, 0f);
            AssertPose(panel.transform, slot.transform, "HorizontalOffset");
            slot.transform.localScale = Vector3.one;
            panel.transform.position = new Vector3(0f, 0.0301f, 0f);
            AssertPose(panel.transform, slot.transform, "HeightOffset");
            panel.transform.position = Vector3.zero;
            panel.transform.rotation = Quaternion.Euler(0f, 3.01f, 0f);
            AssertPose(panel.transform, slot.transform, "Orientation");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(panel);
            UnityEngine.Object.DestroyImmediate(slot);
        }
    }

    [Test]
    public void DirectionalPoseAssessmentUsesIndependentBridgeAxesAndKeepsLegacyRadialRuleSeparate()
    {
        GameObject slot = new("rotated slot");
        GameObject panel = new("panel");
        try
        {
            slot.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            panel.transform.position = slot.transform.TransformPoint(new Vector3(0.04f, 0f, 0.08f));
            panel.transform.rotation = slot.transform.rotation;
            AssertDirectionalPose(panel.transform, slot.transform, "None");

            slot.transform.rotation = Quaternion.identity;
            panel.transform.position = new Vector3(0.05f, 0f, 0.1f);
            panel.transform.rotation = Quaternion.AngleAxis(180f, Vector3.up);
            AssertDirectionalPose(panel.transform, slot.transform, "None");

            panel.transform.position = new Vector3(0.0501f, 0f, 0f);
            AssertDirectionalPose(panel.transform, slot.transform, "HorizontalOffset");
            panel.transform.position = new Vector3(0f, 0f, -0.1001f);
            AssertDirectionalPose(panel.transform, slot.transform, "HorizontalOffset");

            panel.transform.position = new Vector3(0f, 0.0301f, 0f);
            panel.transform.rotation = Quaternion.identity;
            AssertDirectionalPose(panel.transform, slot.transform, "HeightOffset");
            panel.transform.position = Vector3.zero;
            panel.transform.rotation = Quaternion.AngleAxis(3f, Vector3.right);
            AssertDirectionalPose(panel.transform, slot.transform, "None");
            panel.transform.rotation = Quaternion.AngleAxis(3.01f, Vector3.right);
            AssertDirectionalPose(panel.transform, slot.transform, "Orientation");

            // The original public overload remains radial for profiles that have not opted in.
            panel.transform.rotation = Quaternion.identity;
            panel.transform.position = new Vector3(0.04f, 0f, 0.04f);
            AssertPose(panel.transform, slot.transform, "HorizontalOffset");
            AssertDirectionalPose(panel.transform, slot.transform, "None");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(panel);
            UnityEngine.Object.DestroyImmediate(slot);
        }
    }

    [Test]
    public void MinimumCorrectionClampsRectangleAndRadialPoseAndProjectsOnMovementYaw()
    {
        GameObject slot = new("slot");
        GameObject panel = new("panel");
        GameObject basis = new("movement basis");
        try
        {
            panel.transform.position = new Vector3(2f, 0.4f, 3f);
            basis.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            object result = GetMinimumCorrection(slot.transform, panel.transform, basis.transform,
                true, 0.5f, 0.5f, 1f, 0.1f, 3f);
            AssertCorrection(result, 2.5f, -1.5f, -0.3f, 0, false);

            basis.transform.rotation = Quaternion.Euler(0f, 270f, 0f);
            result = GetMinimumCorrection(slot.transform, panel.transform, basis.transform,
                true, 0.5f, 0.5f, 1f, 0.1f, 3f);
            AssertCorrection(result, -2.5f, 1.5f, -0.3f, 0, false);

            basis.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            result = GetMinimumCorrection(slot.transform, panel.transform, basis.transform,
                true, 0.5f, 0.5f, 1f, 0.1f, 3f);
            Assert.That(ReadFloat(result, "RightMeters"), Is.EqualTo(Mathf.Sqrt(0.5f)).Within(0.0001f));
            Assert.That(ReadFloat(result, "ForwardMeters"), Is.EqualTo(-2f * Mathf.Sqrt(2f)).Within(0.0001f));

            result = GetMinimumCorrection(slot.transform, panel.transform, basis.transform,
                false, 0.5f, 0.5f, 1f, 0.1f, 3f);
            Vector2 radialCorrection = new(ReadFloat(result, "RightMeters"), ReadFloat(result, "ForwardMeters"));
            Assert.That(radialCorrection.magnitude, Is.EqualTo(new Vector2(2f, 3f).magnitude - 1f).Within(0.0001f));
            Assert.That(ReadFloat(result, "HeightMeters"), Is.EqualTo(-0.3f).Within(0.0001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(basis);
            UnityEngine.Object.DestroyImmediate(panel);
            UnityEngine.Object.DestroyImmediate(slot);
        }
    }

    [Test]
    public void MinimumCorrectionFindsValidatedIntegerYawOrRequestsTiltReset()
    {
        GameObject slot = new("slot");
        GameObject panel = new("panel");
        GameObject basis = new("movement basis");
        try
        {
            slot.transform.rotation = Quaternion.Euler(18f, 37f, -11f);
            panel.transform.rotation = slot.transform.rotation * Quaternion.Euler(0f, 178.6f, 0f);
            object result = GetMinimumCorrection(slot.transform, panel.transform, basis.transform,
                true, 0.05f, 0.1f, 0.05f, 0.03f, 1f);
            Assert.That(ReadInt(result, "LocalYRotationDegrees"), Is.EqualTo(1));
            Assert.That(ReadBool(result, "ResetTiltRequired"), Is.False);

            // Preserve a fractional yaw when it is already within Unity's exact quaternion tolerance.
            panel.transform.rotation = slot.transform.rotation * Quaternion.Euler(0f, 179.4f, 0f);
            result = GetMinimumCorrection(slot.transform, panel.transform, basis.transform,
                true, 0.05f, 0.1f, 0.05f, 0.03f, 1f);
            Assert.That(ReadInt(result, "LocalYRotationDegrees"), Is.EqualTo(0));

            // A local tilt just outside tolerance can be corrected by a local-Y step.
            panel.transform.rotation = slot.transform.rotation * Quaternion.Euler(2.5f, 2f, 0.8f);
            result = GetMinimumCorrection(slot.transform, panel.transform, basis.transform,
                true, 0.05f, 0.1f, 0.05f, 0.03f, 3f);
            Assert.That(ReadBool(result, "ResetTiltRequired"), Is.False);
            int localStep = ReadInt(result, "LocalYRotationDegrees");
            Quaternion correctedLocal = Quaternion.Inverse(slot.transform.rotation) * panel.transform.rotation
                * Quaternion.AngleAxis(localStep, Vector3.up);
            Assert.That(Mathf.Min(Quaternion.Angle(correctedLocal, Quaternion.identity),
                Quaternion.Angle(correctedLocal, Quaternion.AngleAxis(180f, Vector3.up))), Is.LessThanOrEqualTo(3f));
            Quaternion oneFewerStep = Quaternion.Inverse(slot.transform.rotation) * panel.transform.rotation
                * Quaternion.AngleAxis(localStep - Math.Sign(localStep), Vector3.up);
            Assert.That(Mathf.Min(Quaternion.Angle(oneFewerStep, Quaternion.identity),
                Quaternion.Angle(oneFewerStep, Quaternion.AngleAxis(180f, Vector3.up))), Is.GreaterThan(3f));

            panel.transform.rotation = slot.transform.rotation * Quaternion.Euler(0f, 0f, 4f);
            result = GetMinimumCorrection(slot.transform, panel.transform, basis.transform,
                true, 0.05f, 0.1f, 0.05f, 0.03f, 3f);
            Assert.That(ReadBool(result, "ResetTiltRequired"), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(basis);
            UnityEngine.Object.DestroyImmediate(panel);
            UnityEngine.Object.DestroyImmediate(slot);
        }
    }

    private static object GetMinimumCorrection(Transform slot, Transform panel, Transform basis,
        bool directional, float along, float across, float radial, float height, float orientation)
    {
        Type assessor = Type.GetType("BridgeDeckPanelStage2Assessment, Assembly-CSharp");
        MethodInfo method = assessor.GetMethod("GetMinimumCorrection", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method.Invoke(null, new object[] { slot, panel.position, panel.rotation, basis,
            directional, along, across, radial, height, orientation });
    }

    private static float ReadFloat(object value, string property) => (float)value.GetType()
        .GetField(property, BindingFlags.Instance | BindingFlags.Public).GetValue(value);
    private static int ReadInt(object value, string property) => (int)value.GetType()
        .GetField(property, BindingFlags.Instance | BindingFlags.Public).GetValue(value);
    private static bool ReadBool(object value, string property) => (bool)value.GetType()
        .GetField(property, BindingFlags.Instance | BindingFlags.Public).GetValue(value);
    private static void AssertCorrection(object value, float right, float forward, float height,
        int yaw, bool resetTilt)
    {
        Assert.That(ReadFloat(value, "RightMeters"), Is.EqualTo(right).Within(0.0001f));
        Assert.That(ReadFloat(value, "ForwardMeters"), Is.EqualTo(forward).Within(0.0001f));
        Assert.That(ReadFloat(value, "HeightMeters"), Is.EqualTo(height).Within(0.0001f));
        Assert.That(ReadInt(value, "LocalYRotationDegrees"), Is.EqualTo(yaw));
        Assert.That(ReadBool(value, "ResetTiltRequired"), Is.EqualTo(resetTilt));
    }

    [Test]
    public void EmbeddedPairNailsMapChildTargetEntriesToDistinctPanelQuarters()
    {
        GameObject panel = new("physical panel");
        GameObject child = new("collider target");
        child.transform.SetParent(panel.transform, false);
        child.transform.localPosition = new Vector3(0.4f, 0f, -0.3f);
        child.transform.localRotation = Quaternion.Euler(0f, 17f, 0f);
        try
        {
            AssertQuarter(panel.transform, child.transform, new Vector3(-0.7f, 0.1f, -0.6f),
                "NegativeXNegativeZ");
            AssertQuarter(panel.transform, child.transform, new Vector3(0.7f, 0.1f, -0.6f),
                "PositiveXNegativeZ");
            AssertQuarter(panel.transform, child.transform, new Vector3(-0.7f, 0.1f, 0.6f),
                "NegativeXPositiveZ");
            AssertQuarter(panel.transform, child.transform, new Vector3(0.7f, 0.1f, 0.6f),
                "PositiveXPositiveZ");

            AssertRejected(panel.transform, child.transform, new Vector3(AxisHalfWidth, 0.1f, 0.4f), "OnQuarterBoundary");
            AssertRejected(panel.transform, child.transform, new Vector3(-AxisHalfWidth, 0.1f, 0.4f),
                "OnQuarterBoundary");
            AssertRejected(panel.transform, child.transform, new Vector3(0.4f, 0.1f, 0f),
                "OnQuarterBoundary");
            AssertRejected(panel.transform, child.transform, new Vector3(1.321f, 0.1f, 0.4f),
                "EntryOutsidePanel");
            AssertRejected(panel.transform, child.transform, new Vector3(0.4f, -0.1f, 0.4f),
                "EntryIsNotOnTop");
            AssertRejected(panel.transform, child.transform, new Vector3(0.4f, 0.1f, 0.4f),
                "EntryIsNotOnTop", Vector3.up);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(panel);
        }
    }

    [Test]
    public void NonPairNailsAndEntriesNearEitherAxisDoNotYieldAQuarter()
    {
        GameObject panel = new("physical panel");
        try
        {
            object single = CreateInfo("EmbeddedSingle",
                panel.transform, new Vector3(0.6f, 0.1f, 0.6f), Vector3.down);
            AssertNail(single, panel.transform, false, "NotEmbeddedPair");

            AssertRejected(panel.transform, panel.transform, new Vector3(0.0051f, 0.1f, -0.5f), "None", expectAccepted: true);
            AssertRejected(panel.transform, panel.transform, new Vector3(-0.0051f, 0.1f, -0.5f), "None", expectAccepted: true);
            AssertRejected(panel.transform, panel.transform, new Vector3(0.5f, 0.1f, -0.0051f), "None", expectAccepted: true);
            AssertRejected(panel.transform, panel.transform, new Vector3(0.5f, 0.1f, 0.0051f), "None", expectAccepted: true);
        }
        finally { UnityEngine.Object.DestroyImmediate(panel); }
    }

    private static void AssertQuarter(Transform panel, Transform target, Vector3 panelLocalEntry, string expected)
    {
        object info = CreateInfo("EmbeddedPair", target,
            target.InverseTransformPoint(panel.TransformPoint(panelLocalEntry)),
            target.InverseTransformDirection(panel.TransformDirection(Vector3.down)));
        AssertNail(info, panel, true, "None", expected);
    }

    private static void AssertRejected(Transform panel, Transform target, Vector3 panelLocalEntry,
        string expected, Vector3? panelLocalAxis = null, bool expectAccepted = false)
    {
        Vector3 axis = panelLocalAxis ?? Vector3.down;
        object info = CreateInfo("EmbeddedPair", target,
            target.InverseTransformPoint(panel.TransformPoint(panelLocalEntry)),
            target.InverseTransformDirection(panel.TransformDirection(axis)));
        AssertNail(info, panel, expectAccepted, expected);
    }

    private static void AssertPose(Transform panel, Transform slot, string expected)
    {
        Type assessor = Type.GetType("BridgeDeckPanelStage2Assessment, Assembly-CSharp");
        Assert.That(assessor, Is.Not.Null);
        MethodInfo method = assessor.GetMethod("EvaluatePose", new[]
        {
            typeof(Transform), typeof(Transform), typeof(float), typeof(float), typeof(float)
        });
        object result = method.Invoke(null, new object[] { panel, slot, 0.05f, 0.03f, 3f });
        Assert.That(result.ToString(), Is.EqualTo(expected));
    }

    private static void AssertDirectionalPose(Transform panel, Transform slot, string expected)
    {
        Type assessor = Type.GetType("BridgeDeckPanelStage2Assessment, Assembly-CSharp");
        Assert.That(assessor, Is.Not.Null);
        MethodInfo method = assessor.GetMethod("EvaluatePose", new[]
        {
            typeof(Transform), typeof(Transform), typeof(float), typeof(float), typeof(float), typeof(float)
        });
        Assert.That(method, Is.Not.Null);
        object result = method.Invoke(null, new object[] { panel, slot, 0.05f, 0.1f, 0.03f, 3f });
        Assert.That(result.ToString(), Is.EqualTo(expected));
    }

    private static object CreateInfo(string stateName, Transform firstTarget, Vector3 entryLocal, Vector3 axisLocal)
    {
        Type stateType = Type.GetType("MountingNailState, Assembly-CSharp");
        Type referenceType = Type.GetType("MountingNailTargetReference, Assembly-CSharp");
        Type infoType = Type.GetType("MountingNailAttachmentInfo, Assembly-CSharp");
        Assert.That(stateType, Is.Not.Null);
        Assert.That(referenceType, Is.Not.Null);
        Assert.That(infoType, Is.Not.Null);
        object state = Enum.Parse(stateType, stateName);
        return infoType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null,
            new[] { stateType, typeof(Transform), typeof(Transform), referenceType,
                referenceType, typeof(Vector3), typeof(Vector3) }, null)
            .Invoke(new object[] { state, firstTarget, null, Activator.CreateInstance(referenceType),
                Activator.CreateInstance(referenceType), entryLocal, axisLocal });
    }

    private static void AssertNail(object info, Transform panel, bool expectedAccepted, string expectedRejection,
        string expectedQuarter = null)
    {
        Type assessor = Type.GetType("BridgeDeckPanelStage2Assessment, Assembly-CSharp");
        Type quarterType = Type.GetType("BridgeDeckPanelQuarterMask, Assembly-CSharp");
        Type rejectionType = Type.GetType("BridgeDeckPanelNailRejection, Assembly-CSharp");
        Assert.That(assessor, Is.Not.Null);
        Assert.That(quarterType, Is.Not.Null);
        Assert.That(rejectionType, Is.Not.Null);
        MethodInfo method = assessor.GetMethod("TryGetNailQuarter");
        object[] args = { info, panel, Dimensions, AxisHalfWidth, Enum.ToObject(quarterType, 0), Enum.ToObject(rejectionType, 0) };
        bool accepted = (bool)method.Invoke(null, args);
        Assert.That(accepted, Is.EqualTo(expectedAccepted));
        Assert.That(args[5].ToString(), Is.EqualTo(expectedRejection));
        if (expectedQuarter != null) Assert.That(args[4].ToString(), Is.EqualTo(expectedQuarter));
    }
}
