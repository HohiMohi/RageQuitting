using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class BridgeDeckAreaV1GeometryTests
{
    private readonly List<ScriptableObject> assets = new();
    private static readonly System.Type Geometry = System.Type.GetType("BridgeDeckAreaGeometry, Assembly-CSharp");
    private static readonly System.Type RulesType = System.Type.GetType("BridgeDeckAreaRules, Assembly-CSharp");
    private static object Rules => RulesType.GetProperty("Defaults").GetValue(null);
    private static System.Reflection.MethodInfo ValidationMethod => Geometry.GetMethod("ValidatePanel",
        new[] { RulesType, typeof(Vector3[]), typeof(Quaternion), typeof(Vector2[]).MakeByRefType() });

    [Test]
    public void SplitUsesIntegerMillimetersAndPreservesKerfAndMassProportion()
    {
        object split = null;
        Assert.That(TryCut(2500, 1560, ref split), Is.True);
        Assert.That(Field(split, "FirstLengthMillimeters"), Is.EqualTo(1560));
        Assert.That(Field(split, "SecondLengthMillimeters"), Is.EqualTo(935));
        Assert.That(TryCut(805, 400, ref split), Is.True);
        Assert.That(Field(split, "SecondLengthMillimeters"), Is.EqualTo(400));
        Assert.That(TryCut(804, 400, ref split), Is.False);
        Assert.That((float)Geometry.GetMethod("MassForLength").Invoke(null, new object[] { 1f, 1560, 2500 }), Is.EqualTo(0.624f).Within(0.0001f));
        Assert.That(Field(Rules, "AreaLength"), Is.EqualTo(6.56f).Within(0.0001f));
        Assert.That(Field(Rules, "MaximumJointWidth"), Is.EqualTo(0.02f).Within(0.0001f));
        Assert.That(Field(Rules, "Kerf"), Is.EqualTo(0.005f).Within(0.0001f));
    }

    [Test]
    public void NonDefaultAreaProfileIsTheSingleSourceForSawMinimumKerfAndStockDensity()
    {
        System.Type profileType = System.Type.GetType("BridgeDeckAreaProfile, Assembly-CSharp");
        ScriptableObject profile = ScriptableObject.CreateInstance(profileType);
        assets.Add(profile);
        profileType.GetField("minimumFragmentLengthMeters").SetValue(profile, .45f);
        profileType.GetField("sawKerfMeters").SetValue(profile, .009f);
        profileType.GetField("fullStockMassKilograms").SetValue(profile, 1.6f);
        object rules = profileType.GetMethod("ToRules").Invoke(profile, null);
        System.Type geometryType = System.Type.GetType("BridgeDeckAreaGeometry, Assembly-CSharp");
        object[] args = { rules, 1000, 450, null };
        Assert.That(geometryType.GetMethod("TryCreateCut").Invoke(null, args), Is.EqualTo(true));
        object cut = args[3];
        Assert.That((int)cut.GetType().GetField("FirstLengthMillimeters").GetValue(cut), Is.EqualTo(450));
        Assert.That((int)cut.GetType().GetField("SecondLengthMillimeters").GetValue(cut), Is.EqualTo(541));
        Assert.That((float)geometryType.GetMethod("MassForLength").Invoke(null, new object[] { 1.6f, 450, 1000 }),
            Is.EqualTo(.72f).Within(.0001f));
        args[2] = 449;
        Assert.That(geometryType.GetMethod("TryCreateCut").Invoke(null, args), Is.EqualTo(false),
            "Saw split validation uses the same configured minimum as area correction gaps.");

        System.Type sawProfile = System.Type.GetType("SawCutProfileSO, Assembly-CSharp");
        Assert.That(sawProfile.GetField("minimumFragmentMeters"), Is.Null);
        Assert.That(sawProfile.GetField("kerfMeters"), Is.Null);
        Assert.That(sawProfile.GetField("fullStockMassKilograms"), Is.Null);
    }

    [Test]
    public void ClosedJointAndCorrectionGapRespectConfiguredBoundariesAndEpsilon()
    {
        object atLimit = MeasureRectangles(0.02f);
        Assert.That(Field(atLimit, "AllGapsClosed"), Is.True);
        Assert.That(Field(atLimit, "HasCorrectionSizedGap"), Is.False);

        object overLimit = MeasureRectangles(0.0206f);
        Assert.That(Field(overLimit, "AllGapsClosed"), Is.False);
        Assert.That(Field(overLimit, "HasCorrectionSizedGap"), Is.True);
        object fragmentSized = MeasureRectangles(0.4f);
        Assert.That(Field(fragmentSized, "AllGapsClosed"), Is.False);
        Assert.That(Field(fragmentSized, "HasCorrectionSizedGap"), Is.False);
        Assert.That((float)Field(fragmentSized, "MinimumGapMeters"), Is.EqualTo(0.4f).Within(0.001f));
    }

    [Test]
    public void CoverageMergesOverlapAndMeasuresWedgeGapExtrema()
    {
        Vector2[][] overlapping = { Rect(-3.28f, -0.2f, -1.2f, 1.2f), Rect(-0.5f, 0.2f, -1.2f, 1.2f), Rect(0.1f, 3.28f, -1.2f, 1.2f) };
        object overlapResult = Measure(overlapping);
        Assert.That(Field(overlapResult, "AllGapsClosed"), Is.True);
        Assert.That((float)Field(overlapResult, "CoveredLengthMeters"), Is.EqualTo(6.56f).Within(0.001f));
        Assert.That((float)Field(overlapResult, "CoveredAreaSquareMeters"), Is.EqualTo(6.56f * 2.38f).Within(0.01f));

        float middleHalfLength = 0.76f;
        float yawRadians = 0.3f * Mathf.Deg2Rad;
        Vector2[][] wedge =
        {
            Rect(-3.28f, -0.78f, -1.2f, 1.2f),
            RotatedRect(-middleHalfLength, middleHalfLength, yawRadians),
            Rect(0.78f, 3.28f, -1.2f, 1.2f)
        };
        object wedgeResult = Measure(wedge);
        Assert.That(Field(wedgeResult, "AllGapsClosed"), Is.False);
        Assert.That(Field(wedgeResult, "HasCorrectionSizedGap"), Is.True);
        Assert.That(Field(wedgeResult, "HasOverlappingPanels"), Is.False);
        float expectedMinimum = 0.78f - middleHalfLength / Mathf.Cos(yawRadians) - 1.19f * Mathf.Tan(yawRadians);
        float expectedMaximum = 0.78f - middleHalfLength / Mathf.Cos(yawRadians) + 1.19f * Mathf.Tan(yawRadians);
        Assert.That((float)Field(wedgeResult, "MinimumGapMeters"), Is.EqualTo(expectedMinimum).Within(0.0001f));
        Assert.That((float)Field(wedgeResult, "MaximumGapMeters"), Is.EqualTo(expectedMaximum).Within(0.0001f));
    }

    [Test]
    public void SparseRightEdgeAndInteriorHoleReportActualCoveredLength()
    {
        object rightEdge = Measure(new[] { Rect(2f, 3.28f, -1.2f, 1.2f) });
        Assert.That((float)Field(rightEdge, "CoveredLengthMeters"), Is.EqualTo(1.28f).Within(0.001f));
        object interiorHole = Measure(new[] { Rect(-3.28f, -1f, -1.2f, 1.2f), Rect(1f, 3.28f, -1.2f, 1.2f) });
        Assert.That((float)Field(interiorHole, "CoveredLengthMeters"), Is.EqualTo(4.56f).Within(0.001f));
        Assert.That(Field(interiorHole, "AllGapsClosed"), Is.False);
        Assert.That(Field(interiorHole, "HasCorrectionSizedGap"), Is.False);
    }

    [Test]
    public void AllowedEndAndInteriorJointsCountAsLogicalCompletionButLargeHolesDoNot()
    {
        object rightEnd = Measure(new[] { Rect(-3.28f, 3.265f, -1.2f, 1.2f) });
        Assert.That(Field(rightEnd, "AllGapsClosed"), Is.True);
        Assert.That((float)Field(rightEnd, "CoveredLengthMeters"), Is.EqualTo(6.56f).Within(0.001f));
        Assert.That(Field(Measure(new[] { Rect(-3.265f, 3.28f, -1.2f, 1.2f) }), "AllGapsClosed"), Is.True);
        Assert.That(Field(MeasureRectangles(0.015f), "AllGapsClosed"), Is.True);
    }

    [Test]
    public void OverlapIsReportedSeparatelyAndDynamicInputCapacityDoesNotTruncate()
    {
        Vector2[][] overlapping = { Rect(-3.28f, 0.2f, -1.2f, 1.2f), Rect(0f, 3.28f, -1.2f, 1.2f) };
        Assert.That(Field(Measure(overlapping), "HasOverlappingPanels"), Is.True);
        Vector2[][] touching = { Rect(-3.28f, 0f, -1.2f, 1.2f), Rect(0f, 3.28f, -1.2f, 1.2f) };
        Assert.That(Field(Measure(touching), "HasOverlappingPanels"), Is.False);
        Vector2[][] many = new Vector2[130][];
        for (int i = 0; i < many.Length; i++) many[i] = Rect(-3.28f, 3.28f, -1.2f, 1.2f);
        object manyResult = Measure(many);
        Assert.That(Field(manyResult, "IsValid"), Is.True);
        Assert.That(Field(manyResult, "HasOverlappingPanels"), Is.True);
    }

    [Test]
    public void PanelValidationAcceptsBothYawsAndAreaFrameCoordinatesAndRejectsPoseOrBoundsErrors()
    {
        Vector3 origin = new(9f, 2f, -7f);
        Quaternion frameRotation = Quaternion.Euler(0f, 90f, 0f);
        System.Type frameType = System.Type.GetType("BridgeDeckAreaFrame, Assembly-CSharp");
        object frame = System.Activator.CreateInstance(frameType, origin, frameRotation);
        Vector3 localPoint = new Vector3(1f, 0f, 2f);
        Vector3 worldPoint = (Vector3)frameType.GetMethod("ToWorld").Invoke(frame, new object[] { localPoint });
        Vector3 returnedLocal = (Vector3)frameType.GetMethod("ToLocal").Invoke(frame, new object[] { worldPoint });
        Assert.That((returnedLocal - localPoint).magnitude, Is.LessThan(0.0001f));
        Vector3[] localCorners =
        {
            new Vector3(-3.28f, 0f, -1.2f), new Vector3(3.28f, 0f, -1.2f),
            new Vector3(3.28f, 0f, 1.2f), new Vector3(-3.28f, 0f, 1.2f)
        };
        Vector3[] worldCorners = new Vector3[4];
        for (int i = 0; i < 4; i++) worldCorners[i] = (Vector3)frameType.GetMethod("ToWorld").Invoke(frame, new object[] { localCorners[i] });
        Vector3[] localizedCorners = new Vector3[4];
        for (int i = 0; i < 4; i++) localizedCorners[i] = (Vector3)frameType.GetMethod("ToLocal").Invoke(frame, new object[] { worldCorners[i] });
        object[] frameArgs = { Rules, localizedCorners, Quaternion.identity, null };
        Assert.That(ValidationMethod.Invoke(null, frameArgs).ToString(), Is.EqualTo("None"));
        Assert.That(ValidateAtYaw(0f, 0f, 0f), Is.EqualTo("None"));
        Assert.That(ValidateAtYaw(180f, 0f, 0f), Is.EqualTo("None"));
        Assert.That(ValidateAtYaw(0.6f, 0f, 0f), Is.EqualTo("Orientation"));
        Assert.That(ValidateAtYaw(0f, 0.031f, 0f), Is.EqualTo("Height"));
        Assert.That(ValidateAtYaw(0f, 0f, 0.02f), Is.EqualTo("LateralCoverage"));
        Assert.That(ValidateAtYaw(0f, 0f, -0.01f), Is.EqualTo("None"));
        Assert.That(ValidateAtYaw(0.1f, 0f, -0.002f), Is.EqualTo("None"));
        Assert.That(ValidateAtYaw(-0.1f, 0f, 0.002f), Is.EqualTo("None"));
        Assert.That(ValidateAtYaw(0.1f, 0f, 0.015f), Is.EqualTo("LateralCoverage"));
        Assert.That(ValidateAtYaw(0f, 0f, 0.011f), Is.EqualTo("LateralCoverage"));
        Assert.That(ValidateNarrowStrip(), Is.EqualTo("LateralCoverage"));
        Assert.That(ValidateZeroQuaternion(), Is.EqualTo("InvalidGeometry"));
        Assert.That(ValidateNonFinite(), Is.EqualTo("InvalidGeometry"));
        Assert.That(ValidateLongitudinalOffset(0.011f), Is.EqualTo("LongitudinalBounds"));
        Assert.That(ValidateStockAtCenter(-2.03f, 2.5f, 0f), Is.EqualTo("None"));
        Assert.That(ValidateStockAtCenter(2.03f, 2.5f, 180f), Is.EqualTo("None"));
        Assert.That(ValidateStockAtCenter(-3.08f, 0.4f, 0f), Is.EqualTo("None"));
        Assert.That(ValidateStockAtCenter(3.08f, 0.4f, 180f), Is.EqualTo("None"));

        object completeRotated = MeasureInTranslatedYawedFrame();
        Assert.That(Field(completeRotated, "AllGapsClosed"), Is.True);
        Assert.That((float)Field(completeRotated, "CoveredLengthMeters"), Is.EqualTo(6.56f).Within(0.001f));
        Vector2[][] invalid = { new[] { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero } };
        Assert.That(Field(Measure(invalid), "IsValid"), Is.False);
    }

    [Test]
    public void StableMovementReevaluationDoesNotLatchGeometry()
    {
        Vector2[][] panels = { Rect(-3.28f, 0f, -1.2f, 1.2f), Rect(0f, 3.28f, -1.2f, 1.2f) };
        Assert.That(Field(Measure(panels), "AllGapsClosed"), Is.True);
        panels[1] = Rect(0.05f, 3.28f, -1.2f, 1.2f);
        Assert.That(Field(Measure(panels), "HasCorrectionSizedGap"), Is.True);
    }

    [Test]
    public void StructuredDiagnosticsMeasureErrorsAndTheirCorrectionsSeparately()
    {
        Quaternion rotation = Quaternion.identity;
        Vector3[] heightCorners = MakePanelCorners(2.5f, 2.4f, 0f, .041f, 0f, 0f);
        object height = Diagnose(heightCorners, rotation);
        AssertDiagnostic(height, "Height");
        Assert.That((float)Field(height, "AmountMeters"), Is.EqualTo(.0105f).Within(.0001f),
            "Height amount is the error beyond tolerance and geometry epsilon.");
        float verticalCorrection = (float)Field(height, "CorrectionMeters");
        Assert.That(verticalCorrection, Is.EqualTo(.041f).Within(.0001f));
        for (int i = 0; i < 4; i++) heightCorners[i].y -= verticalCorrection;
        Assert.That(Validate(heightCorners, rotation), Is.EqualTo("None"), "Applying the reported vertical correction fixes this isolated error.");

        Vector3[] endCorners = MakePanelCorners(.4f, 2.4f, 3.101f, 0f, 0f, 0f);
        object end = Diagnose(endCorners, rotation);
        AssertDiagnostic(end, "LongitudinalBounds");
        Assert.That((float)Field(end, "AmountMeters"), Is.EqualTo(.0105f).Within(.0001f),
            "End overhang excludes the permitted boundary margin and geometry epsilon.");
        float endCorrection = (float)Field(end, "CorrectionMeters");
        Assert.That(endCorrection, Is.LessThan(0f));
        for (int i = 0; i < 4; i++) endCorners[i].x += endCorrection;
        Assert.That(Validate(endCorners, rotation), Is.EqualTo("None"), "Applying the reported inward correction fixes the end overhang.");

        Vector3[] shiftedCorners = MakePanelCorners(2.5f, 2.4f, 0f, 0f, .012f, 0f);
        object side = Diagnose(shiftedCorners, rotation);
        AssertDiagnostic(side, "LateralOverhang");
        Assert.That((float)Field(side, "AmountMeters"), Is.EqualTo(.0015f).Within(.0001f),
            "Side overhang reports only the amount outside the allowed margin.");
        float lateralCorrection = (float)Field(side, "CorrectionMeters");
        Assert.That(lateralCorrection, Is.EqualTo(-.012f).Within(.0001f),
            "The recentering correction is distinct from the measured edge overhang.");
        string sideText = Format(side);
        Assert.That(sideText, Does.Contain("1.5 mm").And.Contain("12 mm"));
        for (int i = 0; i < 4; i++) shiftedCorners[i].z += lateralCorrection;
        Assert.That(Validate(shiftedCorners, rotation), Is.EqualTo("None"), "The exact reported lateral correction fixes the isolated case.");

        Quaternion smallYaw = Quaternion.Euler(0f, .47f, 0f);
        Vector3[] yawedShift = MakePanelCorners(2.5f, 2.4f, 0f, 0f, .003f, .47f);
        object yawedSide = Diagnose(yawedShift, smallYaw);
        AssertDiagnostic(yawedSide, "LateralOverhang");
        Assert.That(Field(yawedSide, "Correction").ToString(), Is.EqualTo("MoveLaterally"),
            "A slightly tilted stock plate can still be corrected by recentering if the actual collider edges fit.");
        for (int i = 0; i < 4; i++) yawedShift[i].z += (float)Field(yawedSide, "CorrectionMeters");
        Assert.That(Validate(yawedShift, smallYaw), Is.EqualTo("None"));

        Vector3[] tooWide = MakePanelCorners(2.5f, 2.4215f, 0f, 0f, 0f, 0f);
        object widthIssue = Diagnose(tooWide, rotation);
        Assert.That(Field(widthIssue, "Correction").ToString(), Is.EqualTo("Reorient"),
            "A plate whose two long edges cannot fit within both lateral margin bands must not receive a translation instruction.");
        Assert.That(Format(widthIssue), Does.Contain("sliding cannot fit both edges"));
    }

    [Test]
    public void OverlapSweepIncludesActualWedgeAndContainedPanelExtents()
    {
        Vector2[] longPanel = Rect(-1f, 1f, -1.2f, 1.2f);
        Vector2[] contained = Rect(-.2f, .2f, -1.2f, 1.2f);
        System.Type listType = typeof(List<float>);
        var scratch = new List<float>(32);
        var overlapMethod = Geometry.GetMethod("MeasureMaximumOverlap", new[] { typeof(Vector2[]), typeof(Vector2[]), RulesType, listType });
        float containedOverlap = (float)overlapMethod.Invoke(null, new object[] { longPanel, contained, Rules, scratch });
        Assert.That(containedOverlap, Is.EqualTo(.4f).Within(.0001f));
        float containedOffset = 1.201f;
        for (int i = 0; i < contained.Length; i++) contained[i].x += containedOffset;
        Assert.That((bool)Geometry.GetMethod("PolygonsOverlap", new[] { typeof(Vector2[]), typeof(Vector2[]), typeof(float) })
            .Invoke(null, new object[] { longPanel, contained, .0005f }), Is.False,
            "The actual one-sided separation needed for a contained panel is larger than its overlap depth; the feedback only tells players to spread panels.");

        Vector2[] upperLeft = Rect(-1f, .1f, 1.25f, 1.35f);
        Vector2[] upperRight = Rect(0f, .3f, 1.25f, 1.35f);
        float sideOverlap = (float)overlapMethod.Invoke(null, new object[] { upperLeft, upperRight, Rules, scratch });
        Assert.That(sideOverlap, Is.EqualTo(.1f).Within(.0001f),
            "Overlap measurement includes the actual collider intersection beyond the nominal lateral area edge.");
    }

    [Test]
    public void NarrowGapTextUsesTheActiveRulesMinimumAndKeepsMillimeterPrecision()
    {
        System.Type diagnosticType = System.Type.GetType("BridgeDeckAreaDiagnostic, Assembly-CSharp");
        System.Type kindType = System.Type.GetType("BridgeDeckAreaDiagnosticKind, Assembly-CSharp");
        System.Type correctionType = System.Type.GetType("BridgeDeckAreaCorrection, Assembly-CSharp");
        object narrowKind = System.Enum.Parse(kindType, "NarrowOpening");
        object spread = System.Enum.Parse(correctionType, "SpreadPanels");
        object diagnostic = System.Activator.CreateInstance(diagnosticType,
            new[] { narrowKind, spread, (object)0f, 0f, .025f, .031f, .45f, 0f });
        string formatted = (string)System.Type.GetType("BridgeDeckAreaRequirement, Assembly-CSharp")
            .GetMethod("FormatDiagnostic").Invoke(null, new[] { diagnostic, false, false });
        Assert.That(formatted, Does.Contain("25–31 mm").And.Contain("450 mm"));
        Assert.That(formatted, Does.Not.Contain("400 mm"), "The guidance follows the configured minimum fragment length.");
        string tinyHeight = Format(Diagnose(MakePanelCorners(2.5f, 2.4f, 0f, .031f, 0f, 0f), Quaternion.identity));
        Assert.That(tinyHeight, Does.Contain("0.5 mm").And.Not.Contain("0 mm"),
            "A half-millimeter rejected height error must never round to zero.");
        Quaternion subMillimeterYaw = Quaternion.Euler(0f, .48f, 0f);
        string tinyLateral = Format(Diagnose(RotatedPanelCorners(2.5f, 2.4f, Vector3.zero, subMillimeterYaw), subMillimeterYaw));
        Assert.That(tinyLateral, Does.Not.Contain("0 mm"), "An actual rejected inner corner must retain a nonzero lateral measure.");
    }

    [Test]
    public void OrientationRaiseLateralDeficitAndSequentialCorrectionsAreReportedInPriorityOrder()
    {
        Quaternion yawAndTilt = Quaternion.Euler(.4f, .6f, 0f);
        Vector3[] rotated = MakePanelCorners(2.5f, 2.4f, 0f, 0f, 0f, .6f);
        object orientation = Diagnose(rotated, yawAndTilt);
        AssertDiagnostic(orientation, "Orientation");
        Assert.That((float)Field(orientation, "AngleDegrees"), Is.GreaterThan(.5f));
        Assert.That(Format(orientation), Does.Contain("°").And.Contain("bridge").And.Contain("0° or 180°")
            .And.Not.Contain("0 mm").And.Not.Contain("rotation or tilt"));

        Vector3[] raisedCorners = MakePanelCorners(2.5f, 2.4f, 0f, -.041f, 0f, 0f);
        object raised = Diagnose(raisedCorners, Quaternion.identity);
        AssertDiagnostic(raised, "Height");
        Assert.That(Field(raised, "Correction").ToString(), Is.EqualTo("Raise"));
        float raiseBy = (float)Field(raised, "CorrectionMeters");
        Assert.That(raiseBy, Is.EqualTo(.041f).Within(.0001f));
        for (int i = 0; i < raisedCorners.Length; i++) raisedCorners[i].y += raiseBy;
        Assert.That(Validate(raisedCorners, Quaternion.identity), Is.EqualTo("None"));

        Vector3[] deficient = MakePanelCorners(2.5f, 2.37f, 0f, 0f, 0f, 0f);
        object lateralDeficit = Diagnose(deficient, Quaternion.identity);
        AssertDiagnostic(lateralDeficit, "LateralDeficit");
        Assert.That(Field(lateralDeficit, "Correction").ToString(), Is.EqualTo("Reorient"));
        Assert.That(Format(lateralDeficit), Does.Contain("mm").And.Contain("Align rotation"));
        float lateralShift = (float)Field(lateralDeficit, "CorrectionMeters");
        for (int i = 0; i < deficient.Length; i++) deficient[i].z += lateralShift;
        Assert.That(Validate(deficient, Quaternion.identity), Is.EqualTo("LateralCoverage"),
            "A width deficit cannot be fixed by pretending the center can slide to cover both edges.");

        // Correcting the first issue reveals the next one instead of combining instructions.
        Vector3[] sequential = MakePanelCorners(2.5f, 2.4f, 0f, -.041f, 0f, 0f);
        Quaternion sequentialRotation = Quaternion.Euler(0f, .6f, 0f);
        object first = Diagnose(sequential, sequentialRotation);
        AssertDiagnostic(first, "Orientation");
        sequential = MakePanelCorners(2.5f, 2.4f, 0f, -.041f, 0f, 0f);
        object second = Diagnose(sequential, Quaternion.identity);
        AssertDiagnostic(second, "Height");
        for (int i = 0; i < sequential.Length; i++) sequential[i].y += (float)Field(second, "CorrectionMeters");
        AssertDiagnostic(Diagnose(sequential, Quaternion.identity), "None");
    }

    [Test]
    public void DiagnosisUsesActualCornerExtremaForTiltedHeightAndYawedLateralDeficit()
    {
        Quaternion tilt = Quaternion.Euler(.4f, 0f, 0f);
        Vector3[] tiltedCorners = RotatedPanelCorners(2.5f, 2.4f, new Vector3(0f, .023f, 0f), tilt);
        object height = Diagnose(tiltedCorners, tilt);
        AssertDiagnostic(height, "Height");
        Assert.That((float)Field(height, "AmountMeters"), Is.GreaterThan(0f),
            "The highest actual underside corner exceeds the limit even though the mean height remains below it.");
        Assert.That(Format(height), Does.Not.Contain("0 mm"));

        Quaternion yaw = Quaternion.Euler(0f, .48f, 0f);
        Vector3[] yawedCorners = RotatedPanelCorners(2.5f, 2.4f, Vector3.zero, yaw);
        object lateral = Diagnose(yawedCorners, yaw);
        AssertDiagnostic(lateral, "LateralDeficit");
        Assert.That((float)Field(lateral, "AmountMeters"), Is.GreaterThan(0f),
            "A failed inner endpoint must produce a nonzero full-width deficit measure.");
        Assert.That(Format(lateral), Does.Not.Contain("0 mm"));
    }

    [TestCase(89f, 0f, 0f)]
    [TestCase(90f, 0f, 0f)]
    [TestCase(0f, 0f, 89f)]
    [TestCase(0f, 0f, 90f)]
    public void PositionSelectorReportsExtremePitchOrRollBeforeDegenerateProjection(float pitch, float yaw, float roll)
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, roll);
        Vector3[] corners = RotatedPanelCorners(2.5f, 2.4f, Vector3.zero, rotation);
        if (Mathf.Abs(pitch) == 90f)
            for (int i = 0; i < corners.Length; i++) corners[i].z = 0f;
        if (Mathf.Abs(roll) == 90f)
            for (int i = 0; i < corners.Length; i++) corners[i].x = 0f;
        Vector2[] footprint = new Vector2[4];
        bool finiteProjection = (bool)Geometry.GetMethod("TryProjectFiniteFootprint").Invoke(null,
            new object[] { Rules, corners, rotation, footprint });
        bool projectedForMeasurement = (bool)Geometry.GetMethod("TryGetProjectedFootprint").Invoke(null,
            new object[] { Rules, corners, rotation, footprint });
        object diagnostic = InvokeSelect(Geometry.GetMethod("SelectPositionDiagnostic"), corners, rotation,
            footprint, false, 0f, false, 0f, 0f);

        Assert.That(finiteProjection, Is.True, "Finite real collider corners remain available to feedback even when their projection collapses.");
        AssertDiagnostic(diagnostic, "Orientation");
        Assert.That((float)Field(diagnostic, "AngleDegrees"), Is.GreaterThan(88f));
        Assert.That(Validate(corners, rotation), Is.EqualTo("Orientation"),
            "The selector is feedback-only; the existing placement validator still rejects the rotated collider as an orientation failure.");
        Assert.That(projectedForMeasurement, Is.EqualTo(Mathf.Abs(pitch) < 90f && Mathf.Abs(roll) < 90f),
            "A collapsed 90-degree projection remains unavailable to strict polygon measurement; the selector still reports orientation first.");
    }

    [Test]
    public void PositionDiagnosticPriorityCanMeasureOpeningsBeforeLaterLateralAndHeightIssues()
    {
        System.Reflection.MethodInfo select = Geometry.GetMethod("SelectPositionDiagnostic");
        Quaternion misaligned = Quaternion.Euler(0f, .6f, 0f);
        Vector3[] corners = MakePanelCorners(2.5f, 2.4215f, 3.1f, .041f, 0f, .6f);
        Vector2[] footprint = new Vector2[4];
        object diagnostic = InvokeSelect(select, corners, misaligned, footprint, true, .12f, true, .08f, .08f);
        AssertDiagnostic(diagnostic, "Orientation");

        corners = MakePanelCorners(2.5f, 2.4215f, 0f, .041f, 0f, 0f);
        Assert.That(BridgeDeckAreaGeometryCanMeasure(corners, Quaternion.identity, footprint), Is.True,
            "The actual collider spans the whole measurement width even though its side margins are invalid.");
        diagnostic = InvokeSelect(select, corners, Quaternion.identity, footprint, true, .12f, true, .08f, .08f);
        AssertDiagnostic(diagnostic, "Overlap");
        diagnostic = InvokeSelect(select, corners, Quaternion.identity, footprint, false, 0f, true, .08f, .08f);
        AssertDiagnostic(diagnostic, "NarrowOpening");
        Assert.That(Validate(corners, Quaternion.identity), Is.EqualTo("Height"),
            "Displaying the higher-priority opening must not change the actual placement verdict.");
        Vector3[] lateralOnlyCorners = (Vector3[])corners.Clone();
        for (int i = 0; i < lateralOnlyCorners.Length; i++) lateralOnlyCorners[i].y = 0f;
        Assert.That(Validate(lateralOnlyCorners, Quaternion.identity), Is.EqualTo("LateralCoverage"),
            "The existing validator still rejects the independent side overhang.");
        diagnostic = InvokeSelect(select, corners, Quaternion.identity, footprint, false, 0f, false, 0f, 0f);
        AssertDiagnostic(diagnostic, "LateralOverhang");

        corners = MakePanelCorners(2.5f, 2.4f, 3.1f, .041f, 0f, 0f);
        diagnostic = InvokeSelect(select, corners, Quaternion.identity, footprint, true, .12f, true, .08f, .08f);
        AssertDiagnostic(diagnostic, "LongitudinalBounds");
        corners = MakePanelCorners(2.5f, 2.4f, 0f, .041f, 0f, 0f);
        diagnostic = InvokeSelect(select, corners, Quaternion.identity, footprint, false, 0f, false, 0f, 0f);
        AssertDiagnostic(diagnostic, "Height");

        Vector3[] acceptedSideShift = MakePanelCorners(2.5f, 2.4f, 0f, 0f, .005f, 0f);
        Assert.That(Validate(acceptedSideShift, Quaternion.identity), Is.EqualTo("None"),
            "A 5 mm lateral center shift remains within the existing full-width acceptance margin.");
        Assert.That(BridgeDeckAreaGeometryCanMeasure(acceptedSideShift, Quaternion.identity, footprint), Is.True,
            "Gap eligibility follows the same effective sweep band, not the nominal ±half-width endpoints.");
        for (int i = 0; i < acceptedSideShift.Length; i++) acceptedSideShift[i].y += .041f;
        Assert.That(Validate(acceptedSideShift, Quaternion.identity), Is.EqualTo("Height"),
            "Introducing the separate height fault leaves the lateral placement valid.");
        diagnostic = InvokeSelect(select, acceptedSideShift, Quaternion.identity, footprint, false, 0f, true, .08f, .08f);
        AssertDiagnostic(diagnostic, "NarrowOpening");
    }

    private static object InvokeSelect(System.Reflection.MethodInfo select, Vector3[] corners, Quaternion rotation,
        Vector2[] footprint, bool overlap, float overlapMeters, bool narrow, float gapMin, float gapMax)
        => select.Invoke(null, new object[] { Rules, corners, rotation, footprint, overlap, overlapMeters, narrow, gapMin, gapMax });

    private static bool BridgeDeckAreaGeometryCanMeasure(Vector3[] corners, Quaternion rotation, Vector2[] footprint)
    {
        bool projected = (bool)Geometry.GetMethod("TryGetProjectedFootprint").Invoke(null,
            new object[] { Rules, corners, rotation, footprint });
        return projected && (bool)Geometry.GetMethod("CanMeasureAcrossAreaWidth").Invoke(null,
            new object[] { footprint, Rules });
    }

    private static object Diagnose(Vector3[] corners, Quaternion rotation)
    {
        System.Type methodOwner = Geometry;
        return methodOwner.GetMethod("DiagnosePanel").Invoke(null, new object[] { Rules, corners, rotation, new Vector2[4] });
    }

    private static string Validate(Vector3[] corners, Quaternion rotation)
    {
        object[] args = { Rules, corners, rotation, new Vector2[4] };
        return ValidationMethod.Invoke(null, args).ToString();
    }

    private static string Format(object diagnostic) => (string)System.Type.GetType("BridgeDeckAreaRequirement, Assembly-CSharp")
        .GetMethod("FormatDiagnostic").Invoke(null, new[] { diagnostic, false, false });

    private static void AssertDiagnostic(object diagnostic, string kind)
        => Assert.That(Field(diagnostic, "Kind").ToString(), Is.EqualTo(kind));

    private static Vector3[] MakePanelCorners(float length, float width, float x, float y, float z, float yawDegrees)
    {
        Quaternion rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        Vector3 center = new(x, y, z);
        return new[]
        {
            center + rotation * new Vector3(-length * .5f, 0f, -width * .5f),
            center + rotation * new Vector3(length * .5f, 0f, -width * .5f),
            center + rotation * new Vector3(length * .5f, 0f, width * .5f),
            center + rotation * new Vector3(-length * .5f, 0f, width * .5f)
        };
    }

    private static Vector3[] RotatedPanelCorners(float length, float width, Vector3 center, Quaternion rotation)
    {
        return new[]
        {
            center + rotation * new Vector3(-length * .5f, 0f, -width * .5f),
            center + rotation * new Vector3(length * .5f, 0f, -width * .5f),
            center + rotation * new Vector3(length * .5f, 0f, width * .5f),
            center + rotation * new Vector3(-length * .5f, 0f, width * .5f)
        };
    }

    private static object MeasureRectangles(float gapWidth)
    {
        Vector2[][] polygons = { Rect(-3.28f, 2.5f, -1.2f, 1.2f), Rect(2.5f + gapWidth, 3.28f, -1.2f, 1.2f) };
        return Measure(polygons);
    }

    private static string ValidateAtYaw(float yaw, float height, float lateralShift)
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3[] corners =
        {
            rotation * new Vector3(-1.25f, height, -1.2f + lateralShift),
            rotation * new Vector3(1.25f, height, -1.2f + lateralShift),
            rotation * new Vector3(1.25f, height, 1.2f + lateralShift),
            rotation * new Vector3(-1.25f, height, 1.2f + lateralShift)
        };
        object[] args = { Rules, corners, rotation, null };
        return ValidationMethod.Invoke(null, args).ToString();
    }

    private static string ValidateStockAtCenter(float centerX, float length, float yaw)
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3[] corners =
        {
            rotation * new Vector3(-length * 0.5f, 0f, -1.2f) + Vector3.right * centerX,
            rotation * new Vector3(length * 0.5f, 0f, -1.2f) + Vector3.right * centerX,
            rotation * new Vector3(length * 0.5f, 0f, 1.2f) + Vector3.right * centerX,
            rotation * new Vector3(-length * 0.5f, 0f, 1.2f) + Vector3.right * centerX
        };
        object[] args = { Rules, corners, rotation, null };
        return ValidationMethod.Invoke(null, args).ToString();
    }

    private static bool TryCut(int parent, int offset, ref object cut)
    {
        object[] args = { Rules, parent, offset, null };
        bool valid = (bool)Geometry.GetMethod("TryCreateCut").Invoke(null, args);
        cut = args[3];
        return valid;
    }

    private static object Measure(Vector2[][] polygons) =>
        Geometry.GetMethod("MeasureCoverage").Invoke(null, new object[] { Rules, polygons, polygons.Length });

    private static string ValidateNonFinite()
    {
        Vector3[] corners = { Vector3.zero, Vector3.right, Vector3.forward, new Vector3(float.NaN, 0f, 1f) };
        object[] args = { Rules, corners, Quaternion.identity, null };
        return ValidationMethod.Invoke(null, args).ToString();
    }

    private static string ValidateZeroQuaternion()
    {
        Vector3[] corners = { new(-3.28f, 0f, -1.2f), new(3.28f, 0f, -1.2f), new(3.28f, 0f, 1.2f), new(-3.28f, 0f, 1.2f) };
        object[] args = { Rules, corners, new Quaternion(0f, 0f, 0f, 0f), null };
        return ValidationMethod.Invoke(null, args).ToString();
    }

    private static string ValidateNarrowStrip()
    {
        Vector3[] corners = { new(-1f, 0f, -1.2025f), new(1f, 0f, -1.2025f), new(1f, 0f, -1.1975f), new(-1f, 0f, -1.1975f) };
        object[] args = { Rules, corners, Quaternion.identity, null };
        return ValidationMethod.Invoke(null, args).ToString();
    }

    private static Vector2[] RotatedRect(float minX, float maxX, float yawRadians)
    {
        float z = 1.2f;
        Vector2[] points = { new(minX, -z), new(maxX, -z), new(maxX, z), new(minX, z) };
        float c = Mathf.Cos(yawRadians), s = Mathf.Sin(yawRadians);
        for (int i = 0; i < points.Length; i++) points[i] = new Vector2(c * points[i].x - s * points[i].y, s * points[i].x + c * points[i].y);
        return points;
    }

    private static object MeasureInTranslatedYawedFrame()
    {
        System.Type frameType = System.Type.GetType("BridgeDeckAreaFrame, Assembly-CSharp");
        object frame = System.Activator.CreateInstance(frameType, new Vector3(12f, 4f, -9f), Quaternion.Euler(0f, 37f, 0f));
        var toWorld = frameType.GetMethod("ToWorld"); var toLocal = frameType.GetMethod("ToLocal");
        Vector2[][] localPolygons = { Rect(-3.28f, 0f, -1.19f, 1.19f), Rect(0f, 3.28f, -1.19f, 1.19f) };
        Vector2[][] roundTripped = new Vector2[localPolygons.Length][];
        for (int p = 0; p < localPolygons.Length; p++)
        {
            roundTripped[p] = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 world = (Vector3)toWorld.Invoke(frame, new object[] { new Vector3(localPolygons[p][i].x, 0f, localPolygons[p][i].y) });
                Vector3 local = (Vector3)toLocal.Invoke(frame, new object[] { world });
                roundTripped[p][i] = new Vector2(local.x, local.z);
            }
        }
        return Measure(roundTripped);
    }

    private static string ValidateLongitudinalOffset(float offset)
    {
        Vector3[] corners =
        {
            new Vector3(-3.28f + offset, 0f, -1.2f), new Vector3(3.28f + offset, 0f, -1.2f),
            new Vector3(3.28f + offset, 0f, 1.2f), new Vector3(-3.28f + offset, 0f, 1.2f)
        };
        object[] args = { Rules, corners, Quaternion.identity, null };
        return ValidationMethod.Invoke(null, args).ToString();
    }

    private static object Field(object instance, string name) => instance.GetType().GetField(name).GetValue(instance);

    private static Vector2[] Rect(float minX, float maxX, float minZ, float maxZ) => new[]
    {
        new Vector2(minX, minZ), new Vector2(maxX, minZ), new Vector2(maxX, maxZ), new Vector2(minX, maxZ)
    };

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < assets.Count; i++) if (assets[i] != null) Object.DestroyImmediate(assets[i]);
        assets.Clear();
    }
}
