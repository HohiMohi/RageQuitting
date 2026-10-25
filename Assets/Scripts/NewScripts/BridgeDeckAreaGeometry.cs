using System;
using UnityEngine;

public readonly struct BridgeDeckAreaRules
{
    public readonly float AreaLength, AreaWidth, StockLength, StockWidth, StockThickness;
    public readonly float MinimumFragmentLength, MaximumJointWidth, BoundaryMargin;
    public readonly float OrientationToleranceDegrees, UndersideHeightTolerance, Epsilon;
    public readonly float Kerf, ActiveCutSeconds, FullStockMass;

    public BridgeDeckAreaRules(float areaLength, float areaWidth, float stockLength, float stockWidth,
        float stockThickness, float minimumFragmentLength, float maximumJointWidth, float boundaryMargin,
        float orientationToleranceDegrees, float undersideHeightTolerance, float epsilon, float kerf,
        float activeCutSeconds, float fullStockMass)
    {
        AreaLength = areaLength; AreaWidth = areaWidth; StockLength = stockLength; StockWidth = stockWidth;
        StockThickness = stockThickness; MinimumFragmentLength = minimumFragmentLength;
        MaximumJointWidth = maximumJointWidth; BoundaryMargin = boundaryMargin;
        OrientationToleranceDegrees = orientationToleranceDegrees;
        UndersideHeightTolerance = undersideHeightTolerance; Epsilon = epsilon;
        Kerf = kerf; ActiveCutSeconds = activeCutSeconds; FullStockMass = fullStockMass;
    }

    public static BridgeDeckAreaRules Defaults => new(6.56f, 2.40f, 2.50f, 2.40f, 0.20f,
        0.40f, 0.02f, 0.01f, 0.5f, 0.03f, 0.0005f, 0.005f, 4f, 1f);
}

/// <summary>Rigid area frame. Geometry is always measured in its local X/Z plane.</summary>
public readonly struct BridgeDeckAreaFrame
{
    public readonly Vector3 Origin;
    public readonly Quaternion Rotation;
    public BridgeDeckAreaFrame(Vector3 origin, Quaternion rotation) { Origin = origin; Rotation = rotation; }
    public Vector3 ToLocal(Vector3 world) => Quaternion.Inverse(Rotation) * (world - Origin);
    public Vector3 ToWorld(Vector3 local) => Origin + Rotation * local;
}

/// <summary>
/// Per-instance physical dimensions and state. The length is the current longitudinal
/// dimension after cutting; fragments retain the source material's density and scale.
/// </summary>
[Serializable]
public struct BridgeDeckAreaPanelState
{
    public int lengthMillimeters;
    public int widthMillimeters;
    public int thicknessMillimeters;
    public int sourceLengthMillimeters;
    public int cutOffsetMillimeters;
    public int cutProgressMilliseconds;
    public bool cutStarted;

    public float MassKilograms(float fullStockMassKilograms)
    {
        if (sourceLengthMillimeters <= 0 || lengthMillimeters <= 0) return 0f;
        return fullStockMassKilograms * lengthMillimeters / sourceLengthMillimeters;
    }
}

public enum BridgeDeckAreaPanelFailure : byte
{
    None, InvalidGeometry, Orientation, Height, LongitudinalBounds, LateralCoverage
}

public enum BridgeDeckAreaDiagnosticKind : byte
{
    None, OutsideArea, UnfinishedCut, Orientation, Height, LongitudinalBounds,
    LateralOverhang, LateralDeficit, InvalidGeometry, Overlap, NarrowOpening,
    NeedsNails, FourNails, Locked
}

public enum BridgeDeckAreaCorrection : byte
{
    None, Rotate, Raise, Lower, MoveLongitudinally, MoveLaterally, Reorient, SpreadPanels
}

/// <summary>Structured feedback measured by the same geometry rules used for area acceptance.</summary>
public readonly struct BridgeDeckAreaDiagnostic
{
    public readonly BridgeDeckAreaDiagnosticKind Kind;
    public readonly BridgeDeckAreaCorrection Correction;
    public readonly float AmountMeters;
    public readonly float CorrectionMeters;
    public readonly float AngleDegrees;
    public readonly float MinimumGapMeters;
    public readonly float MaximumGapMeters;
    public readonly float RequiredGapMeters;

    public BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind kind,
        BridgeDeckAreaCorrection correction = BridgeDeckAreaCorrection.None,
        float amountMeters = 0f, float angleDegrees = 0f,
        float minimumGapMeters = 0f, float maximumGapMeters = 0f,
        float requiredGapMeters = 0f, float correctionMeters = 0f)
    {
        Kind = kind; Correction = correction; AmountMeters = amountMeters; CorrectionMeters = correctionMeters;
        AngleDegrees = angleDegrees; MinimumGapMeters = minimumGapMeters; MaximumGapMeters = maximumGapMeters;
        RequiredGapMeters = requiredGapMeters;
    }
}

public readonly struct BridgeDeckAreaPanelDiagnosticEntry
{
    public readonly BridgeDeckAreaPanel Panel;
    public readonly BridgeDeckAreaDiagnostic Diagnostic;
    public readonly bool HasFourPhysicalNails;
    public BridgeDeckAreaPanelDiagnosticEntry(BridgeDeckAreaPanel panel, BridgeDeckAreaDiagnostic diagnostic)
        : this(panel, diagnostic, false) { }
    public BridgeDeckAreaPanelDiagnosticEntry(BridgeDeckAreaPanel panel, BridgeDeckAreaDiagnostic diagnostic,
        bool hasFourPhysicalNails)
    { Panel = panel; Diagnostic = diagnostic; HasFourPhysicalNails = hasFourPhysicalNails; }
}

public readonly struct BridgeDeckAreaCut
{
    public readonly int FirstLengthMillimeters, SecondLengthMillimeters;
    public BridgeDeckAreaCut(int first, int second) { FirstLengthMillimeters = first; SecondLengthMillimeters = second; }
}

public readonly struct BridgeDeckAreaGapSummary
{
    public readonly bool HasGap, AllGapsClosed, HasCorrectionSizedGap, HasOverlappingPanels, IsValid;
    public readonly float MinimumGapMeters, MaximumGapMeters;
    public readonly float CoveredLengthMeters;
    public readonly float CoveredAreaSquareMeters;
    public BridgeDeckAreaGapSummary(bool hasGap, bool allClosed, bool correction, float minimum, float maximum,
        float covered, float coveredArea, bool overlaps, bool valid)
    {
        HasGap = hasGap; AllGapsClosed = allClosed; HasCorrectionSizedGap = correction;
        MinimumGapMeters = minimum; MaximumGapMeters = maximum; CoveredLengthMeters = covered;
        CoveredAreaSquareMeters = coveredArea;
        HasOverlappingPanels = overlaps; IsValid = valid;
    }
}

/// <summary>Deterministic rules for split math and flat deck coverage.</summary>
public static class BridgeDeckAreaGeometry
{
    private const float MillimetersPerMeter = 1000f;
    private static readonly Vector2[] AreaFootprint = new Vector2[4];

    public static bool TryCreateCut(BridgeDeckAreaRules rules, int parentLengthMillimeters,
        int cutOffsetMillimeters, out BridgeDeckAreaCut cut)
    {
        cut = default;
        if (!ValidRules(rules) || !IsFinite(rules.Kerf) || rules.Kerf < 0f) return false;
        int kerf = Mathf.RoundToInt(rules.Kerf * MillimetersPerMeter);
        int minimum = Mathf.CeilToInt(rules.MinimumFragmentLength * MillimetersPerMeter);
        if (parentLengthMillimeters <= 0 || cutOffsetMillimeters < minimum
            || cutOffsetMillimeters > parentLengthMillimeters - kerf - minimum) return false;
        int second = parentLengthMillimeters - kerf - cutOffsetMillimeters;
        if (second < minimum) return false;
        cut = new BridgeDeckAreaCut(cutOffsetMillimeters, second);
        return true;
    }

    public static float MassForLength(float fullStockMassKilograms, int fragmentLengthMillimeters,
        int sourceLengthMillimeters)
    {
        return sourceLengthMillimeters <= 0 || fragmentLengthMillimeters <= 0 ? 0f
            : fullStockMassKilograms * fragmentLengthMillimeters / sourceLengthMillimeters;
    }

    /// <summary>Validates the actual four bottom-face collider corners, already in area-local space.</summary>
    public static BridgeDeckAreaPanelFailure ValidatePanel(BridgeDeckAreaRules rules,
        Vector3[] bottomCornersLocal, Quaternion panelRotationLocal, out Vector2[] footprint)
    {
        footprint = null;
        if (bottomCornersLocal == null || bottomCornersLocal.Length != 4) return BridgeDeckAreaPanelFailure.InvalidGeometry;
        footprint = new Vector2[4];
        BridgeDeckAreaPanelFailure result = ValidatePanel(rules, bottomCornersLocal, panelRotationLocal, footprint);
        if (result != BridgeDeckAreaPanelFailure.None) footprint = null;
        return result;
    }

    public static BridgeDeckAreaPanelFailure ValidatePanel(BridgeDeckAreaRules rules,
        Vector3[] bottomCornersLocal, Quaternion panelRotationLocal, Vector2[] footprint)
    {
        if (!ValidRules(rules) || bottomCornersLocal == null || bottomCornersLocal.Length != 4) return BridgeDeckAreaPanelFailure.InvalidGeometry;
        if (footprint == null || footprint.Length != 4) return BridgeDeckAreaPanelFailure.InvalidGeometry;
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = bottomCornersLocal[i];
            if (!IsFinite(p.x) || !IsFinite(p.y) || !IsFinite(p.z)) return BridgeDeckAreaPanelFailure.InvalidGeometry;
            footprint[i] = new Vector2(p.x, p.z);
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
        }
        if (!IsFinite(panelRotationLocal.x) || !IsFinite(panelRotationLocal.y)
            || !IsFinite(panelRotationLocal.z) || !IsFinite(panelRotationLocal.w))
            return BridgeDeckAreaPanelFailure.InvalidGeometry;
        float qLengthSq = panelRotationLocal.x * panelRotationLocal.x + panelRotationLocal.y * panelRotationLocal.y
            + panelRotationLocal.z * panelRotationLocal.z + panelRotationLocal.w * panelRotationLocal.w;
        if (!IsFinite(qLengthSq) || qLengthSq < rules.Epsilon * rules.Epsilon) return BridgeDeckAreaPanelFailure.InvalidGeometry;
        panelRotationLocal = new Quaternion(panelRotationLocal.x / Mathf.Sqrt(qLengthSq), panelRotationLocal.y / Mathf.Sqrt(qLengthSq),
            panelRotationLocal.z / Mathf.Sqrt(qLengthSq), panelRotationLocal.w / Mathf.Sqrt(qLengthSq));
        float upright = Quaternion.Angle(panelRotationLocal, Quaternion.identity);
        float flipped = Quaternion.Angle(panelRotationLocal, Quaternion.AngleAxis(180f, Vector3.up));
        if (Mathf.Min(upright, flipped) > rules.OrientationToleranceDegrees + rules.Epsilon)
            return BridgeDeckAreaPanelFailure.Orientation;
        for (int i = 0; i < 4; i++)
            if (Mathf.Abs(bottomCornersLocal[i].y) > rules.UndersideHeightTolerance + rules.Epsilon)
                return BridgeDeckAreaPanelFailure.Height;
        float halfLength = rules.AreaLength * 0.5f;
        float halfWidth = rules.AreaWidth * 0.5f;
        if (minX < -halfLength - rules.BoundaryMargin - rules.Epsilon
            || maxX > halfLength + rules.BoundaryMargin + rules.Epsilon)
            return BridgeDeckAreaPanelFailure.LongitudinalBounds;
        if (minZ < -halfWidth - rules.BoundaryMargin - rules.Epsilon
            || maxZ > halfWidth + rules.BoundaryMargin + rules.Epsilon
            || !CoversBothLateralEdges(footprint, halfWidth, rules))
            return BridgeDeckAreaPanelFailure.LateralCoverage;
        if (!IsValidPolygon(footprint, rules.Epsilon)) return BridgeDeckAreaPanelFailure.InvalidGeometry;
        return BridgeDeckAreaPanelFailure.None;
    }

    /// <summary>
    /// Projects the actual bottom collider corners into the area plane without applying the
    /// placement tolerances. Callers use this only for feedback; acceptance still uses ValidatePanel.
    /// </summary>
    public static bool TryGetProjectedFootprint(BridgeDeckAreaRules rules, Vector3[] bottomCornersLocal,
        Quaternion panelRotationLocal, Vector2[] footprint)
    {
        return TryProjectFiniteFootprint(rules, bottomCornersLocal, panelRotationLocal, footprint)
            && IsValidPolygon(footprint, rules.Epsilon);
    }

    /// <summary>
    /// Projects finite collider corners even when the XZ projection is degenerate. This is for
    /// diagnostics only: an extreme tilt can collapse a real box's projection, but the angle
    /// should still be reported before that projection is rejected for measurements.
    /// </summary>
    public static bool TryProjectFiniteFootprint(BridgeDeckAreaRules rules, Vector3[] bottomCornersLocal,
        Quaternion panelRotationLocal, Vector2[] footprint)
    {
        if (!ValidRules(rules) || bottomCornersLocal == null || bottomCornersLocal.Length != 4
            || footprint == null || footprint.Length != 4) return false;
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = bottomCornersLocal[i];
            if (!IsFinite(corner.x) || !IsFinite(corner.y) || !IsFinite(corner.z)) return false;
            footprint[i] = new Vector2(corner.x, corner.z);
        }
        return IsValidRotation(panelRotationLocal, rules.Epsilon);
    }

    /// <summary>Whether a real projected footprint supports measurements across the full deck width.</summary>
    public static bool CanMeasureAcrossAreaWidth(Vector2[] footprint, BridgeDeckAreaRules rules)
    {
        if (!ValidRules(rules) || !IsValidPolygon(footprint, rules.Epsilon)) return false;
        float minimumZ = float.PositiveInfinity, maximumZ = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            minimumZ = Mathf.Min(minimumZ, footprint[i].y);
            maximumZ = Mathf.Max(maximumZ, footprint[i].y);
        }
        float halfWidth = rules.AreaWidth * .5f - rules.BoundaryMargin;
        // Convex footprint projection has a valid cross-section at every intermediate Z.
        // Match the sweep band used by opening measurement. This is only measurement
        // eligibility, not a lateral-acceptance check on all four corners.
        const float sweepTolerance = 0.000001f;
        return minimumZ <= -halfWidth + sweepTolerance && maximumZ >= halfWidth - sweepTolerance;
    }

    /// <summary>
    /// Selects the single visible placement issue in priority order. The supplied overlap and gap
    /// measurements are informational inputs only; this method never changes validation or coverage.
    /// </summary>
    public static BridgeDeckAreaDiagnostic SelectPositionDiagnostic(BridgeDeckAreaRules rules,
        Vector3[] bottomCornersLocal, Quaternion panelRotationLocal, Vector2[] footprint,
        bool hasOverlap, float maximumOverlapMeters, bool hasNarrowOpening,
        float minimumGapMeters, float maximumGapMeters)
    {
        if (!TryProjectFiniteFootprint(rules, bottomCornersLocal, panelRotationLocal, footprint))
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.InvalidGeometry);

        BridgeDeckAreaDiagnostic diagnostic = DiagnoseOrientation(rules, panelRotationLocal);
        if (diagnostic.Kind != BridgeDeckAreaDiagnosticKind.None) return diagnostic;
        if (!IsValidPolygon(footprint, rules.Epsilon))
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.InvalidGeometry);
        diagnostic = DiagnoseLongitudinalBounds(rules, footprint);
        if (diagnostic.Kind != BridgeDeckAreaDiagnosticKind.None) return diagnostic;
        if (hasOverlap)
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.Overlap,
                BridgeDeckAreaCorrection.SpreadPanels, Mathf.Max(0f, maximumOverlapMeters));
        if (hasNarrowOpening && CanMeasureAcrossAreaWidth(footprint, rules))
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.NarrowOpening,
                BridgeDeckAreaCorrection.SpreadPanels, 0f, 0f, minimumGapMeters, maximumGapMeters,
                rules.MinimumFragmentLength);
        diagnostic = DiagnoseLateralCoverage(rules, footprint);
        if (diagnostic.Kind != BridgeDeckAreaDiagnosticKind.None) return diagnostic;
        return DiagnoseHeight(rules, bottomCornersLocal);
    }

    private static BridgeDeckAreaDiagnostic DiagnoseOrientation(BridgeDeckAreaRules rules, Quaternion rotation)
    {
        if (!IsValidRotation(rotation, rules.Epsilon))
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.InvalidGeometry);
        float length = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);
        rotation = new Quaternion(rotation.x / length, rotation.y / length, rotation.z / length, rotation.w / length);
        float angle = Mathf.Min(Quaternion.Angle(rotation, Quaternion.identity),
            Quaternion.Angle(rotation, Quaternion.AngleAxis(180f, Vector3.up)));
        return angle > rules.OrientationToleranceDegrees + rules.Epsilon
            ? new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.Orientation,
                BridgeDeckAreaCorrection.Rotate, angleDegrees: angle)
            : default;
    }

    private static BridgeDeckAreaDiagnostic DiagnoseLongitudinalBounds(BridgeDeckAreaRules rules, Vector2[] footprint)
    {
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        for (int i = 0; i < 4; i++) { minX = Mathf.Min(minX, footprint[i].x); maxX = Mathf.Max(maxX, footprint[i].x); }
        float half = rules.AreaLength * .5f;
        float left = Mathf.Max(0f, -half - rules.BoundaryMargin - rules.Epsilon - minX);
        float right = Mathf.Max(0f, maxX - half - rules.BoundaryMargin - rules.Epsilon);
        if (left <= 0f && right <= 0f) return default;
        return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.LongitudinalBounds,
            BridgeDeckAreaCorrection.MoveLongitudinally, Mathf.Max(left, right),
            correctionMeters: left > 0f ? left : -right);
    }

    private static BridgeDeckAreaDiagnostic DiagnoseLateralCoverage(BridgeDeckAreaRules rules, Vector2[] footprint)
    {
        float halfWidth = rules.AreaWidth * .5f;
        float overhang = 0f, deficit = 0f;
        for (int i = 0; i < 4; i++)
        {
            float z = footprint[i].y;
            if (Mathf.Abs(z + halfWidth) <= Mathf.Abs(z - halfWidth))
            {
                overhang = Mathf.Max(overhang, -halfWidth - rules.BoundaryMargin - rules.Epsilon - z);
                deficit = Mathf.Max(deficit, z + halfWidth - rules.BoundaryMargin - rules.Epsilon);
            }
            else
            {
                overhang = Mathf.Max(overhang, z - halfWidth - rules.BoundaryMargin - rules.Epsilon);
                deficit = Mathf.Max(deficit, halfWidth - rules.BoundaryMargin - rules.Epsilon - z);
            }
        }
        if (overhang > 0f)
        {
            float correction = -(footprint[0].y + footprint[1].y + footprint[2].y + footprint[3].y) * .25f;
            bool canTranslate = CanTranslateLateralEdges(footprint, correction, halfWidth, rules);
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.LateralOverhang,
                canTranslate ? BridgeDeckAreaCorrection.MoveLaterally : BridgeDeckAreaCorrection.Reorient,
                overhang, correctionMeters: canTranslate ? correction : 0f);
        }
        if (deficit <= 0f) return default;
        float lateralCorrection = -(footprint[0].y + footprint[1].y + footprint[2].y + footprint[3].y) * .25f;
        bool canFill = CanTranslateLateralEdges(footprint, lateralCorrection, halfWidth, rules);
        return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.LateralDeficit,
            canFill ? BridgeDeckAreaCorrection.MoveLaterally : BridgeDeckAreaCorrection.Reorient,
            deficit, correctionMeters: canFill ? lateralCorrection : 0f);
    }

    private static BridgeDeckAreaDiagnostic DiagnoseHeight(BridgeDeckAreaRules rules, Vector3[] bottomCornersLocal)
    {
        float sum = 0f, maximumExcess = 0f;
        for (int i = 0; i < 4; i++)
        {
            float y = bottomCornersLocal[i].y;
            sum += y;
            maximumExcess = Mathf.Max(maximumExcess, Mathf.Abs(y) - rules.UndersideHeightTolerance - rules.Epsilon);
        }
        if (maximumExcess <= 0f) return default;
        float offset = sum * .25f;
        return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.Height,
            offset > 0f ? BridgeDeckAreaCorrection.Lower : BridgeDeckAreaCorrection.Raise,
            maximumExcess, correctionMeters: Mathf.Abs(offset));
    }

    private static bool IsValidRotation(Quaternion rotation, float epsilon)
    {
        if (!IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w)) return false;
        float lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        return IsFinite(lengthSquared) && lengthSquared >= epsilon * epsilon;
    }

    public static BridgeDeckAreaDiagnostic DiagnosePanel(BridgeDeckAreaRules rules,
        Vector3[] bottomCornersLocal, Quaternion panelRotationLocal, Vector2[] footprint)
    {
        BridgeDeckAreaPanelFailure failure = ValidatePanel(rules, bottomCornersLocal, panelRotationLocal, footprint);
        if (failure == BridgeDeckAreaPanelFailure.None) return default;
        if (failure == BridgeDeckAreaPanelFailure.InvalidGeometry)
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.InvalidGeometry);
        if (failure == BridgeDeckAreaPanelFailure.Orientation)
        {
            float qLength = Mathf.Sqrt(panelRotationLocal.x * panelRotationLocal.x + panelRotationLocal.y * panelRotationLocal.y
                + panelRotationLocal.z * panelRotationLocal.z + panelRotationLocal.w * panelRotationLocal.w);
            if (qLength > 0f) panelRotationLocal = new Quaternion(panelRotationLocal.x / qLength, panelRotationLocal.y / qLength,
                panelRotationLocal.z / qLength, panelRotationLocal.w / qLength);
            float angle = Quaternion.Angle(panelRotationLocal, Quaternion.identity);
            float flipped = Quaternion.Angle(panelRotationLocal, Quaternion.AngleAxis(180f, Vector3.up));
            angle = Mathf.Min(angle, flipped);
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.Orientation,
                BridgeDeckAreaCorrection.Rotate, 0f, angle);
        }
        if (failure == BridgeDeckAreaPanelFailure.Height)
        {
            float sum = 0f;
            float maximumExcess = 0f;
            for (int i = 0; i < 4; i++)
            {
                float y = bottomCornersLocal[i].y;
                sum += y;
                maximumExcess = Mathf.Max(maximumExcess,
                    Mathf.Abs(y) - rules.UndersideHeightTolerance - rules.Epsilon);
            }
            float offset = sum * .25f;
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.Height,
                offset > 0f ? BridgeDeckAreaCorrection.Lower : BridgeDeckAreaCorrection.Raise,
                Mathf.Max(0f, maximumExcess),
                correctionMeters: Mathf.Abs(offset));
        }
        if (failure == BridgeDeckAreaPanelFailure.LongitudinalBounds)
        {
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            for (int i = 0; i < 4; i++) { minX = Mathf.Min(minX, footprint[i].x); maxX = Mathf.Max(maxX, footprint[i].x); }
            float half = rules.AreaLength * .5f;
            float left = Mathf.Max(0f, -half - rules.BoundaryMargin - rules.Epsilon - minX);
            float right = Mathf.Max(0f, maxX - half - rules.BoundaryMargin - rules.Epsilon);
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.LongitudinalBounds,
                BridgeDeckAreaCorrection.MoveLongitudinally, Mathf.Max(left, right), correctionMeters: left > 0f ? left : -right);
        }
        float halfWidth = rules.AreaWidth * .5f;
        float overhang = 0f, deficit = 0f;
        for (int i = 0; i < 4; i++)
        {
            float z = footprint[i].y;
            if (Mathf.Abs(z + halfWidth) <= Mathf.Abs(z - halfWidth))
            {
                overhang = Mathf.Max(overhang, -halfWidth - rules.BoundaryMargin - rules.Epsilon - z);
                deficit = Mathf.Max(deficit, z + halfWidth - rules.BoundaryMargin - rules.Epsilon);
            }
            else
            {
                overhang = Mathf.Max(overhang, z - halfWidth - rules.BoundaryMargin - rules.Epsilon);
                deficit = Mathf.Max(deficit, halfWidth - rules.BoundaryMargin - rules.Epsilon - z);
            }
        }
        if (overhang > 0f)
        {
            float correction = -(footprint[0].y + footprint[1].y + footprint[2].y + footprint[3].y) * .25f;
            bool canTranslate = CanTranslateLateralEdges(footprint, correction, halfWidth, rules);
            return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.LateralOverhang,
                canTranslate ? BridgeDeckAreaCorrection.MoveLaterally : BridgeDeckAreaCorrection.Reorient,
                overhang, correctionMeters: canTranslate ? correction : 0f);
        }
        float lateralCorrection = -(footprint[0].y + footprint[1].y + footprint[2].y + footprint[3].y) * .25f;
        bool canFill = CanTranslateLateralEdges(footprint, lateralCorrection, halfWidth, rules);
        return new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.LateralDeficit,
            canFill ? BridgeDeckAreaCorrection.MoveLaterally : BridgeDeckAreaCorrection.Reorient,
            deficit, correctionMeters: canFill ? lateralCorrection : 0f);
    }

    private static bool CanTranslateLateralEdges(Vector2[] polygon, float shiftZ, float halfWidth, BridgeDeckAreaRules rules)
    {
        int lower = 0, upper = 0; float lowerX = 0f, upperX = 0f;
        for (int i = 0; i < 4; i++)
        {
            float z = polygon[i].y + shiftZ;
            float lowerDistance = Mathf.Abs(z + halfWidth);
            float upperDistance = Mathf.Abs(z - halfWidth);
            if (lowerDistance <= upperDistance)
            {
                if (lowerDistance > rules.BoundaryMargin + rules.Epsilon) return false;
                lower++; lowerX += polygon[i].x;
            }
            else
            {
                if (upperDistance > rules.BoundaryMargin + rules.Epsilon) return false;
                upper++; upperX += polygon[i].x;
            }
        }
        if (lower != 2 || upper != 2) return false;
        float maxSkew = rules.AreaLength * Mathf.Sin(rules.OrientationToleranceDegrees * Mathf.Deg2Rad)
            + rules.BoundaryMargin + rules.Epsilon;
        return Mathf.Abs(lowerX * .5f - upperX * .5f) <= maxSkew;
    }

    public static bool FootprintIntersectsArea(Vector2[] footprint, BridgeDeckAreaRules rules)
    {
        if (footprint == null || footprint.Length != 4) return false;
        AreaFootprint[0] = new Vector2(-rules.AreaLength * .5f, -rules.AreaWidth * .5f);
        AreaFootprint[1] = new Vector2(rules.AreaLength * .5f, -rules.AreaWidth * .5f);
        AreaFootprint[2] = new Vector2(rules.AreaLength * .5f, rules.AreaWidth * .5f);
        AreaFootprint[3] = new Vector2(-rules.AreaLength * .5f, rules.AreaWidth * .5f);
        return PolygonsOverlap(footprint, AreaFootprint, rules.Epsilon);
    }

    public static float MeasureMaximumOverlap(Vector2[] left, Vector2[] right, BridgeDeckAreaRules rules)
    {
        return MeasureMaximumOverlap(left, right, rules, new System.Collections.Generic.List<float>(18));
    }

    public static float MeasureMaximumOverlap(Vector2[] left, Vector2[] right, BridgeDeckAreaRules rules,
        System.Collections.Generic.List<float> levels)
    {
        if (left == null || right == null || left.Length != 4 || right.Length != 4 || levels == null) return 0f;
        levels.Clear();
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            minZ = Mathf.Min(minZ, Mathf.Min(left[i].y, right[i].y));
            maxZ = Mathf.Max(maxZ, Mathf.Max(left[i].y, right[i].y));
            levels.Add(left[i].y);
            levels.Add(right[i].y);
        }
        levels.Add(minZ);
        levels.Add(maxZ);
        for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++)
            AddEdgeCrossing(levels, left[a], left[(a + 1) & 3], right[b], right[(b + 1) & 3], minZ, maxZ, rules.Epsilon);
        levels.Sort();
        float maximum = 0f;
        for (int i = 0; i < levels.Count; i++)
        {
            MeasureOverlapAtZ(left, right, levels[i], ref maximum);
            if (i + 1 < levels.Count) MeasureOverlapAtZ(left, right, (levels[i] + levels[i + 1]) * .5f, ref maximum);
        }
        return maximum;
    }

    private static void MeasureOverlapAtZ(Vector2[] left, Vector2[] right, float z, ref float maximum)
    {
        if (!TryGetIntervalAtZ(left, z, out float leftMin, out float leftMax)
            || !TryGetIntervalAtZ(right, z, out float rightMin, out float rightMax)) return;
        maximum = Mathf.Max(maximum, Mathf.Min(leftMax, rightMax) - Mathf.Max(leftMin, rightMin));
    }

    private static bool TryGetIntervalAtZ(Vector2[] polygon, float z, out float minimum, out float maximum)
    {
        minimum = float.PositiveInfinity; maximum = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) & 3];
            if (z < Mathf.Min(a.y, b.y) - .00001f || z > Mathf.Max(a.y, b.y) + .00001f) continue;
            float x = Mathf.Abs(a.y - b.y) < .00001f ? (a.x + b.x) * .5f
                : Mathf.Lerp(a.x, b.x, Mathf.Clamp01((z - a.y) / (b.y - a.y)));
            minimum = Mathf.Min(minimum, x); maximum = Mathf.Max(maximum, x);
        }
        return !float.IsPositiveInfinity(minimum);
    }

    private static bool CoversBothLateralEdges(Vector2[] polygon, float halfWidth, BridgeDeckAreaRules rules)
    {
        int lowerCount = 0, upperCount = 0;
        float lowerCenterX = 0f, upperCenterX = 0f;
        for (int i = 0; i < 4; i++)
        {
            float z = polygon[i].y;
            float lowerDistance = Mathf.Abs(z + halfWidth), upperDistance = Mathf.Abs(z - halfWidth);
            if (lowerDistance <= upperDistance)
            {
                if (lowerDistance > rules.BoundaryMargin + rules.Epsilon) return false;
                lowerCount++; lowerCenterX += polygon[i].x;
            }
            else
            {
                if (upperDistance > rules.BoundaryMargin + rules.Epsilon) return false;
                upperCount++; upperCenterX += polygon[i].x;
            }
        }
        if (lowerCount != 2 || upperCount != 2) return false;
        lowerCenterX *= 0.5f; upperCenterX *= 0.5f;
        float allowedLongEdgeSkew = rules.AreaLength * Mathf.Sin(rules.OrientationToleranceDegrees * Mathf.Deg2Rad)
            + rules.BoundaryMargin + rules.Epsilon;
        return Mathf.Abs(lowerCenterX - upperCenterX) <= allowedLongEdgeSkew;
    }

    private static bool ValidRules(BridgeDeckAreaRules rules)
    {
        return IsFinite(rules.AreaLength) && rules.AreaLength > 0f && IsFinite(rules.AreaWidth) && rules.AreaWidth > 0f
            && IsFinite(rules.BoundaryMargin) && rules.BoundaryMargin >= 0f
            && IsFinite(rules.MaximumJointWidth) && rules.MaximumJointWidth >= 0f
            && IsFinite(rules.MinimumFragmentLength) && rules.MinimumFragmentLength > 0f
            && IsFinite(rules.OrientationToleranceDegrees) && rules.OrientationToleranceDegrees >= 0f
            && IsFinite(rules.UndersideHeightTolerance) && rules.UndersideHeightTolerance >= 0f
            && IsFinite(rules.Epsilon) && rules.Epsilon > 0f;
    }

    /// <summary>
    /// Sweeps cross-sections at polygon vertices and edge-order intersections. Gap extrema are
    /// aggregate across openings; callers must not present them as a specific neighbor gap.
    /// This convenience API allocates scratch lists, so frequent runtime evaluation should cache
    /// results or add a reusable workspace overload before integration.
    /// </summary>
    public static BridgeDeckAreaGapSummary MeasureCoverage(BridgeDeckAreaRules rules,
        Vector2[][] convexFootprints, int footprintCount)
    {
        if (!ValidRules(rules) || convexFootprints == null || footprintCount <= 0 || footprintCount > convexFootprints.Length)
            return InvalidSummary(rules);
        float minGap = float.PositiveInfinity, maxGap = 0f;
        bool anyGap = false, closed = true, correction = false, overlaps = false;
        var levels = new System.Collections.Generic.List<float>(2 + footprintCount * 4);
        float effectiveHalfWidth = Mathf.Max(0f, rules.AreaWidth * 0.5f - rules.BoundaryMargin);
        levels.Add(-effectiveHalfWidth); levels.Add(effectiveHalfWidth);
        for (int p = 0; p < footprintCount; p++)
        {
            Vector2[] polygon = convexFootprints[p];
            if (!IsValidPolygon(polygon, rules.Epsilon)) return InvalidSummary(rules);
            for (int i = 0; i < 4; i++)
                if (polygon[i].y > -effectiveHalfWidth && polygon[i].y < effectiveHalfWidth) levels.Add(polygon[i].y);
            for (int other = 0; other < p; other++)
                overlaps |= PolygonsOverlap(polygon, convexFootprints[other], rules.Epsilon);
        }
        for (int a = 0; a < footprintCount; a++)
        for (int ae = 0; ae < 4; ae++)
        for (int b = a; b < footprintCount; b++)
        for (int be = b == a ? ae + 1 : 0; be < 4; be++)
            AddEdgeCrossing(levels, convexFootprints[a][ae], convexFootprints[a][(ae + 1) & 3],
                convexFootprints[b][be], convexFootprints[b][(be + 1) & 3], -effectiveHalfWidth, effectiveHalfWidth, rules.Epsilon);
        levels.Sort();
        for (int i = levels.Count - 1; i > 0; i--)
            if (Mathf.Abs(levels[i] - levels[i - 1]) <= rules.Epsilon) levels.RemoveAt(i);
        float coveredArea = 0f;
        for (int i = 0; i < levels.Count; i++)
        {
            EvaluateSection(rules, convexFootprints, footprintCount, levels[i],
                ref minGap, ref maxGap, ref anyGap, ref closed, ref correction);
            if (i + 1 < levels.Count)
            {
                float middle = EvaluateSection(rules, convexFootprints, footprintCount,
                    (levels[i] + levels[i + 1]) * 0.5f, ref minGap, ref maxGap, ref anyGap, ref closed, ref correction);
                EvaluateSection(rules, convexFootprints, footprintCount, levels[i + 1], ref minGap,
                    ref maxGap, ref anyGap, ref closed, ref correction);
                coveredArea += middle * (levels[i + 1] - levels[i]);
            }
        }
        if (!anyGap) minGap = 0f;
        float effectiveWidth = Mathf.Max(rules.Epsilon, effectiveHalfWidth * 2f);
        float areaEquivalentLength = coveredArea / effectiveWidth;
        return new BridgeDeckAreaGapSummary(anyGap, closed, correction, minGap, maxGap,
            Mathf.Clamp(areaEquivalentLength, 0f, rules.AreaLength), coveredArea, overlaps, true);
    }

    private static float EvaluateSection(BridgeDeckAreaRules rules, Vector2[][] polygons, int count, float z,
        ref float minGap, ref float maxGap, ref bool anyGap, ref bool closed, ref bool correction)
    {
        var intervals = new System.Collections.Generic.List<Vector2>(count);
        for (int p = 0; p < count; p++)
        {
            Vector2[] polygon = polygons[p];
            if (polygon == null || polygon.Length != 4) continue;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int e = 0; e < 4; e++)
            {
                Vector2 a = polygon[e], b = polygon[(e + 1) & 3];
                if (Mathf.Abs(a.y - z) <= rules.Epsilon) { min = Mathf.Min(min, a.x); max = Mathf.Max(max, a.x); }
                if ((a.y < z && b.y > z) || (a.y > z && b.y < z))
                {
                    float x = Mathf.Lerp(a.x, b.x, (z - a.y) / (b.y - a.y));
                    min = Mathf.Min(min, x); max = Mathf.Max(max, x);
                }
            }
            if (!float.IsInfinity(min)) intervals.Add(new Vector2(min, max));
        }
        intervals.Sort((left, right) => left.x.CompareTo(right.x));
        float halfLength = rules.AreaLength * 0.5f, cursor = -halfLength, coveredUnion = 0f;
        for (int i = 0; i < intervals.Count; i++)
        {
            float next = Mathf.Clamp(intervals[i].x, -halfLength, halfLength);
            if (next > cursor + rules.Epsilon)
            {
                float width = next - cursor;
                bool outer = i == 0 && next <= -halfLength + rules.BoundaryMargin + rules.Epsilon;
                RecordGap(width, rules, outer, ref minGap, ref maxGap, ref anyGap, ref closed, ref correction);
                if (outer || width <= rules.MaximumJointWidth + rules.Epsilon) coveredUnion += width;
            }
            float end = Mathf.Clamp(intervals[i].y, -halfLength, halfLength);
            float added = end - Mathf.Max(cursor, next);
            if (added > 0f) coveredUnion += added;
            if (end > cursor) cursor = end;
        }
        if (halfLength > cursor + rules.Epsilon)
        {
            float width = halfLength - cursor;
            RecordGap(width, rules, width <= rules.BoundaryMargin + rules.Epsilon,
                ref minGap, ref maxGap, ref anyGap, ref closed, ref correction);
            if (width <= rules.MaximumJointWidth + rules.Epsilon) coveredUnion += width;
        }
        float covered = Mathf.Clamp(coveredUnion, 0f, rules.AreaLength);
        return covered;
    }

    private static void RecordGap(float width, BridgeDeckAreaRules rules, bool toleratedBoundary, ref float minGap, ref float maxGap,
        ref bool anyGap, ref bool closed, ref bool correction)
    {
        if (toleratedBoundary) return;
        anyGap = true; minGap = Mathf.Min(minGap, width); maxGap = Mathf.Max(maxGap, width);
        if (width > rules.MaximumJointWidth + rules.Epsilon) closed = false;
        if (width > rules.MaximumJointWidth + rules.Epsilon
            && width < rules.MinimumFragmentLength - rules.Epsilon) correction = true;
    }

    private static BridgeDeckAreaGapSummary InvalidSummary(BridgeDeckAreaRules rules) =>
        new(true, false, false, 0f, rules.AreaLength, 0f, 0f, false, false);

    private static bool IsValidPolygon(Vector2[] polygon, float epsilon)
    {
        if (polygon == null || polygon.Length != 4) return false;
        float twiceArea = 0f, turnSign = 0f;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) & 3], c = polygon[(i + 2) & 3];
            if (!IsFinite(a.x) || !IsFinite(a.y)) return false;
            float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
            if (!IsFinite(cross) || Mathf.Abs(cross) <= epsilon * epsilon) return false;
            if (turnSign == 0f) turnSign = Mathf.Sign(cross);
            else if (Mathf.Sign(cross) != turnSign) return false;
            twiceArea += a.x * b.y - b.x * a.y;
            if (!IsFinite(twiceArea)) return false;
        }
        return Mathf.Abs(twiceArea) > epsilon * epsilon * 2f;
    }

    public static bool PolygonsOverlap(Vector2[] a, Vector2[] b, float epsilon)
    {
        return !Separated(a, b, epsilon) && !Separated(b, a, epsilon);
    }

    private static bool Separated(Vector2[] axesFrom, Vector2[] other, float epsilon)
    {
        for (int i = 0; i < 4; i++)
        {
            Vector2 edge = axesFrom[(i + 1) & 3] - axesFrom[i];
            Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
            float amin = float.PositiveInfinity, amax = float.NegativeInfinity;
            float bmin = float.PositiveInfinity, bmax = float.NegativeInfinity;
            for (int j = 0; j < 4; j++)
            {
                float ap = Vector2.Dot(axesFrom[j], axis), bp = Vector2.Dot(other[j], axis);
                amin = Mathf.Min(amin, ap); amax = Mathf.Max(amax, ap);
                bmin = Mathf.Min(bmin, bp); bmax = Mathf.Max(bmax, bp);
            }
            if (Mathf.Min(amax, bmax) - Mathf.Max(amin, bmin) <= epsilon) return true;
        }
        return false;
    }

    private static void AddEdgeCrossing(System.Collections.Generic.List<float> levels,
        Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, float minimumZ, float maximumZ, float epsilon)
    {
        float low = Mathf.Max(Mathf.Min(a0.y, a1.y), Mathf.Min(b0.y, b1.y));
        float high = Mathf.Min(Mathf.Max(a0.y, a1.y), Mathf.Max(b0.y, b1.y));
        if (high - low <= epsilon) return;
        float da = a1.y - a0.y, db = b1.y - b0.y;
        if (Mathf.Abs(da) <= epsilon || Mathf.Abs(db) <= epsilon) return;
        float ma = (a1.x - a0.x) / da, mb = (b1.x - b0.x) / db;
        if (Mathf.Abs(ma - mb) <= epsilon) return;
        float ca = a0.x - ma * a0.y, cb = b0.x - mb * b0.y;
        float z = (cb - ca) / (ma - mb);
        if (z > low + epsilon && z < high - epsilon && z > minimumZ && z < maximumZ) levels.Add(z);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
