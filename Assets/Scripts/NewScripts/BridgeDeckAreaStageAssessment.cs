using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct BridgeDeckAreaNeighborGap
{
    public readonly BridgeDeckAreaPanel First;
    public readonly BridgeDeckAreaPanel Second;
    public readonly float MinimumMeters;
    public readonly float MaximumMeters;
    public BridgeDeckAreaNeighborGap(BridgeDeckAreaPanel first, BridgeDeckAreaPanel second, float minimum, float maximum)
    { First = first; Second = second; MinimumMeters = minimum; MaximumMeters = maximum; }
}

public sealed class BridgeDeckAreaStageResult
{
    public bool IsValid;
    public bool IsComplete;
    public float CoveredLengthMeters;
    public float SecuredLengthMeters;
    public readonly List<BridgeDeckAreaPanel> ContributingPanels = new();
    public readonly List<BridgeDeckAreaNeighborGap> NeighborGaps = new();
    public readonly List<BridgeDeckAreaPanel> CorrectableGaps = new();
    public readonly List<BridgeDeckAreaPanelDiagnosticEntry> PanelDiagnostics = new();

    internal void Reset()
    {
        IsValid = false; IsComplete = false; CoveredLengthMeters = 0f; SecuredLengthMeters = 0f;
        ContributingPanels.Clear(); NeighborGaps.Clear(); CorrectableGaps.Clear(); PanelDiagnostics.Clear();
    }
}

/// <summary>Reusable server-side area and fastening assessment for the flat, local-X deck variant.</summary>
public sealed class BridgeDeckAreaStageAssessment
{
    private readonly List<BridgeDeckAreaPanel> panels = new();
    private readonly List<MountingNail> nails = new();
    private readonly List<BridgeDeckAreaPanel> ordered = new();
    private readonly List<BridgeDeckAreaPanel> geometryQualified = new();
    private readonly List<float> levels = new(32);
    private readonly Vector3[] candidateWorldCorners = new Vector3[4];
    private readonly Dictionary<BridgeDeckAreaPanel, Vector2[]> footprintCache = new();
    private readonly Dictionary<BridgeDeckAreaPanel, bool> footprintValidCache = new();
    private readonly Dictionary<BridgeDeckAreaPanel, bool> securedCache = new();
    private readonly Dictionary<BridgeDeckAreaPanel, bool> overlapCache = new();
    private readonly Dictionary<BridgeDeckAreaPanel, BridgeDeckAreaDiagnostic> diagnosticCache = new();
    private readonly List<BridgeDeckAreaPanel> destroyedPanelScratch = new();
    private readonly BridgeDeckAreaStageResult result = new();
    public BridgeDeckAreaStageResult Evaluate(BridgeDeckAreaRequirement requirement)
    {
        result.Reset();
        if (requirement == null) return result;
        PruneDestroyedPanelCache();
        BridgeDeckAreaPanel.CopyActivePanels(requirement.gameObject.scene, requirement.NetworkManager, panels);
        BridgeDeckSection section = requirement.DeckSection;
        if (requirement.Profile == null || requirement.DeckFrame == null || section == null
            || section.MainGirders == null || section.MainGirders.Count != 2) return result;
        BridgeDeckAreaProfile profile = requirement.Profile;
        BridgeDeckAreaRules rules = profile.ToRules();
        MountingNail.CopyActiveNails(requirement.gameObject.scene, requirement.NetworkManager, nails);
        ordered.Clear();
        geometryQualified.Clear();
        footprintValidCache.Clear();
        securedCache.Clear();
        overlapCache.Clear();
        diagnosticCache.Clear();
        result.IsValid = true;
        for (int i = 0; i < panels.Count; i++)
        {
            BridgeDeckAreaPanel panel = panels[i];
            if (!TryGetFootprint(requirement, panel, rules, out Vector2[] footprint, out bool secured,
                    out BridgeDeckAreaDiagnostic diagnostic))
            {
                if (panel != null && !panel.IsLockedForCompletedArea) panel.SetAreaAssessmentState(false, false, false);
                continue;
            }
            ordered.Add(panel);
            geometryQualified.Add(panel);
            overlapCache[panel] = false;
        }
        if (ordered.Count == 0)
        {
            // Keep the full empty span selectable before any panel has been placed.
            // It is guidance only and can never satisfy area completion.
            result.NeighborGaps.Add(new BridgeDeckAreaNeighborGap(null, null,
                rules.AreaLength, rules.AreaLength));
            BuildPanelDiagnostics(requirement, rules);
            return result;
        }
        for (int i = 0; i < ordered.Count; i++)
        for (int j = i + 1; j < ordered.Count; j++)
        {
            Vector2[] a = footprintCache[ordered[i]], b = footprintCache[ordered[j]];
            if (!BridgeDeckAreaGeometry.PolygonsOverlap(a, b, rules.Epsilon)) continue;
            overlapCache[ordered[i]] = true;
            overlapCache[ordered[j]] = true;
            float overlap = MeasureMaximumOverlap(a, b, rules);
            diagnosticCache[ordered[i]] = new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.Overlap,
                BridgeDeckAreaCorrection.SpreadPanels, overlap);
            diagnosticCache[ordered[j]] = new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.Overlap,
                BridgeDeckAreaCorrection.SpreadPanels, overlap);
        }
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            BridgeDeckAreaPanel panel = ordered[i];
            if (overlapCache[panel])
            {
                if (!panel.IsLockedForCompletedArea) panel.SetAreaAssessmentState(false, false, false);
                ordered.RemoveAt(i); continue;
            }
            bool secured = securedCache[panel];
            result.ContributingPanels.Add(panel);
            if (secured && !panel.CutStarted) result.SecuredLengthMeters += panel.LengthMillimeters * .001f;
            panel.SetAreaAssessmentState(secured && !panel.CutStarted, true, false);
            result.CoveredLengthMeters += panel.LengthMillimeters * .001f;
        }
        ordered.Sort((a, b) => requirement.DeckFrame.InverseTransformPoint(a.transform.position).x
            .CompareTo(requirement.DeckFrame.InverseTransformPoint(b.transform.position).x));
        for (int i = 0; i + 1 < ordered.Count; i++)
        {
            BridgeDeckAreaPanel left = ordered[i], right = ordered[i + 1];
            if (!TryGetFootprint(requirement, left, rules, out Vector2[] a, out _, out _)
                || !TryGetFootprint(requirement, right, rules, out Vector2[] b, out _, out _)) continue;
            if (!TryMeasureNeighborGap(a, b, rules, out float minGap, out float maxGap)) continue;
            result.NeighborGaps.Add(new BridgeDeckAreaNeighborGap(left, right, minGap, maxGap));
            bool closed = maxGap <= rules.MaximumJointWidth + rules.Epsilon;
            if (!closed && minGap < rules.MinimumFragmentLength - rules.Epsilon)
            {
                result.CorrectableGaps.Add(left);
                result.CorrectableGaps.Add(right);
            }
            if (!closed) continue;
        }
        bool endGapsClosed = false;
        if (ordered.Count > 0
            && TryGetFootprint(requirement, ordered[0], rules, out Vector2[] first, out _, out _)
            && TryGetFootprint(requirement, ordered[ordered.Count - 1], rules, out Vector2[] last, out _, out _)
            && TryMeasureBoundaryGap(first, true, rules, out float leftMin, out float leftMax)
            && TryMeasureBoundaryGap(last, false, rules, out float rightMin, out float rightMax))
        {
            result.NeighborGaps.Insert(0, new BridgeDeckAreaNeighborGap(null, ordered[0], leftMin, leftMax));
            result.NeighborGaps.Add(new BridgeDeckAreaNeighborGap(ordered[ordered.Count - 1], null, rightMin, rightMax));
            endGapsClosed = leftMax <= rules.MaximumJointWidth + rules.Epsilon
                && rightMax <= rules.MaximumJointWidth + rules.Epsilon;
            if (leftMax > rules.MaximumJointWidth + rules.Epsilon
                && leftMin < rules.MinimumFragmentLength - rules.Epsilon) result.CorrectableGaps.Add(ordered[0]);
            if (rightMax > rules.MaximumJointWidth + rules.Epsilon
                && rightMin < rules.MinimumFragmentLength - rules.Epsilon) result.CorrectableGaps.Add(ordered[ordered.Count - 1]);
        }
        // Completion is based on real area endpoints, every neighbor opening and securing every contributor.
        bool noOpenGaps = result.NeighborGaps.Count == ordered.Count + 1 && endGapsClosed;
        for (int i = 0; i < result.NeighborGaps.Count; i++)
            noOpenGaps &= result.NeighborGaps[i].MaximumMeters <= rules.MaximumJointWidth + rules.Epsilon;
        bool allSecured = ordered.Count > 0 && result.SecuredLengthMeters + rules.Epsilon >= result.CoveredLengthMeters;
        result.IsComplete = ordered.Count > 0 && noOpenGaps && allSecured;
        BuildPanelDiagnostics(requirement, rules);
        return result;
    }

    /// <summary>Reads attachment state for feedback only; it never changes secured area state.</summary>
    public bool HasFourPhysicalNails(BridgeDeckAreaPanel panel, BridgeDeckAreaRequirement requirement)
    {
        if (panel == null || requirement == null) return false;
        byte mask = CalculateQuarterMask(panel, requirement, panel.ThicknessMillimeters * .001f, out bool bothGirders);
        return mask == (byte)BridgeDeckPanelQuarterMask.All && bothGirders;
    }

    private void BuildPanelDiagnostics(BridgeDeckAreaRequirement requirement, BridgeDeckAreaRules rules)
    {
        result.PanelDiagnostics.Clear();
        for (int i = 0; i < panels.Count; i++)
        {
            BridgeDeckAreaPanel panel = panels[i];
            if (panel == null || !TryGetDiagnosticFootprint(requirement, panel, rules, out Vector2[] footprint)
                || !BridgeDeckAreaGeometry.FootprintIntersectsArea(footprint, rules)) continue;

            BridgeDeckAreaDiagnostic diagnostic;
            if (panel.CutStarted)
            {
                diagnostic = new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.UnfinishedCut);
            }
            else
            {
                bool hasOverlap = false;
                float maximumOverlap = 0f;
                for (int n = 0; n < geometryQualified.Count; n++)
                {
                    BridgeDeckAreaPanel other = geometryQualified[n];
                    if (other == null || other == panel || !footprintCache.TryGetValue(other, out Vector2[] otherFootprint)) continue;
                    if (!BridgeDeckAreaGeometry.PolygonsOverlap(footprint, otherFootprint, rules.Epsilon)) continue;
                    hasOverlap = true;
                    maximumOverlap = Mathf.Max(maximumOverlap,
                        BridgeDeckAreaGeometry.MeasureMaximumOverlap(footprint, otherFootprint, rules, levels));
                }

                bool hasNarrow = TryGetNarrowOpeningForDiagnostic(panel, footprint, rules,
                    out float minimumGap, out float maximumGap);
                PopulatePanelCorners(requirement, panel);
                Quaternion localRotation = Quaternion.Inverse(requirement.DeckFrame.rotation) * panel.transform.rotation;
                diagnostic = BridgeDeckAreaGeometry.SelectPositionDiagnostic(rules, candidateWorldCorners,
                    localRotation, footprint, hasOverlap, maximumOverlap, hasNarrow, minimumGap, maximumGap);
            }

            if (diagnostic.Kind != BridgeDeckAreaDiagnosticKind.None)
            {
                // Keep the factual nail suffix beside the chosen feedback. It is read from the
                // attachment snapshot already collected for this evaluation and never changes
                // the panel's secured/contributor state.
                bool hasFourNails = diagnostic.Kind == BridgeDeckAreaDiagnosticKind.NarrowOpening
                    && HasFourPhysicalNails(panel, requirement);
                result.PanelDiagnostics.Add(new BridgeDeckAreaPanelDiagnosticEntry(panel, diagnostic, hasFourNails));
            }
        }
    }

    private bool TryGetNarrowOpeningForDiagnostic(BridgeDeckAreaPanel panel, Vector2[] footprint,
        BridgeDeckAreaRules rules, out float minimum, out float maximum)
    {
        minimum = maximum = 0f;
        if (!BridgeDeckAreaGeometry.CanMeasureAcrossAreaWidth(footprint, rules)) return false;
        float centerX = (footprint[0].x + footprint[1].x + footprint[2].x + footprint[3].x) * .25f;
        BridgeDeckAreaPanel nearestLeft = null, nearestRight = null;
        float leftCenter = float.NegativeInfinity, rightCenter = float.PositiveInfinity;
        for (int i = 0; i < ordered.Count; i++)
        {
            BridgeDeckAreaPanel other = ordered[i];
            if (other == null || other == panel || !footprintCache.TryGetValue(other, out Vector2[] otherFootprint)
                || !BridgeDeckAreaGeometry.CanMeasureAcrossAreaWidth(otherFootprint, rules)) continue;
            float otherCenterX = (otherFootprint[0].x + otherFootprint[1].x + otherFootprint[2].x + otherFootprint[3].x) * .25f;
            if (otherCenterX < centerX && otherCenterX > leftCenter)
            { leftCenter = otherCenterX; nearestLeft = other; }
            else if (otherCenterX > centerX && otherCenterX < rightCenter)
            { rightCenter = otherCenterX; nearestRight = other; }
        }

        if (nearestLeft != null && TryMeasureNeighborGap(footprintCache[nearestLeft], footprint, rules,
                out minimum, out maximum) && IsCorrectionOpening(minimum, maximum, rules)) return true;
        if (nearestLeft == null && TryMeasureBoundaryGap(footprint, true, rules, out minimum, out maximum)
            && IsCorrectionOpening(minimum, maximum, rules)) return true;
        if (nearestRight != null && TryMeasureNeighborGap(footprint, footprintCache[nearestRight], rules,
                out minimum, out maximum) && IsCorrectionOpening(minimum, maximum, rules)) return true;
        if (nearestRight == null && TryMeasureBoundaryGap(footprint, false, rules, out minimum, out maximum)
            && IsCorrectionOpening(minimum, maximum, rules)) return true;
        minimum = maximum = 0f;
        return false;
    }

    private bool TryGetDiagnosticFootprint(BridgeDeckAreaRequirement requirement, BridgeDeckAreaPanel panel,
        BridgeDeckAreaRules rules, out Vector2[] footprint)
    {
        footprint = null;
        if (panel.PhysicalCollider == null || !panel.PhysicalCollider.enabled || panel.PhysicalCollider.isTrigger) return false;
        PopulatePanelCorners(requirement, panel);
        if (!footprintCache.TryGetValue(panel, out footprint) || footprint == null)
            footprintCache[panel] = footprint = new Vector2[4];
        return BridgeDeckAreaGeometry.TryProjectFiniteFootprint(rules, candidateWorldCorners,
            Quaternion.Inverse(requirement.DeckFrame.rotation) * panel.transform.rotation, footprint);
    }

    private void PopulatePanelCorners(BridgeDeckAreaRequirement requirement, BridgeDeckAreaPanel panel)
    {
        BoxCollider collider = panel.PhysicalCollider;
        float halfX = collider.size.x * .5f, halfZ = collider.size.z * .5f;
        float bottomLocalY = collider.center.y - collider.size.y * .5f;
        Vector3 center = collider.center;
        for (int i = 0; i < 4; i++)
        {
            Vector3 local = i == 0 ? new Vector3(center.x-halfX,bottomLocalY,center.z-halfZ)
                : i == 1 ? new Vector3(center.x+halfX,bottomLocalY,center.z-halfZ)
                : i == 2 ? new Vector3(center.x+halfX,bottomLocalY,center.z+halfZ)
                : new Vector3(center.x-halfX,bottomLocalY,center.z+halfZ);
            candidateWorldCorners[i] = requirement.DeckFrame.InverseTransformPoint(panel.transform.TransformPoint(local));
        }
    }

    public bool MeasureNeighborOpening(Vector2[] left, Vector2[] right, BridgeDeckAreaRules rules,
        out float minimum, out float maximum) => TryMeasureNeighborGap(left, right, rules, out minimum, out maximum);

    public float MeasureMaximumOverlap(Vector2[] left, Vector2[] right, BridgeDeckAreaRules rules)
        => BridgeDeckAreaGeometry.MeasureMaximumOverlap(left, right, rules, levels);

    public bool MeasureBoundaryOpening(Vector2[] footprint, bool leftBoundary, BridgeDeckAreaRules rules,
        out float minimum, out float maximum) => TryMeasureBoundaryGap(footprint, leftBoundary, rules, out minimum, out maximum);

    private void PruneDestroyedPanelCache()
    {
        destroyedPanelScratch.Clear();
        foreach (BridgeDeckAreaPanel panel in footprintCache.Keys)
            if (panel == null) destroyedPanelScratch.Add(panel);
        for (int i = 0; i < destroyedPanelScratch.Count; i++)
        {
            BridgeDeckAreaPanel panel = destroyedPanelScratch[i];
            footprintCache.Remove(panel); footprintValidCache.Remove(panel);
            securedCache.Remove(panel); overlapCache.Remove(panel);
        }
    }

    private bool TryGetFootprint(BridgeDeckAreaRequirement requirement, BridgeDeckAreaPanel panel,
        BridgeDeckAreaRules rules, out Vector2[] footprint, out bool secured, out BridgeDeckAreaDiagnostic diagnostic)
    {
        footprint = null; secured = false; diagnostic = default;
        if (panel == null || panel.PhysicalCollider == null || !panel.PhysicalCollider.enabled
            || panel.PhysicalCollider.isTrigger) return false;
        if (footprintValidCache.TryGetValue(panel, out bool cachedValid))
        {
            footprint = cachedValid ? footprintCache[panel] : null;
            secured = cachedValid && securedCache[panel];
            if (!cachedValid) diagnosticCache.TryGetValue(panel, out diagnostic);
            return cachedValid;
        }
        float halfX = panel.PhysicalCollider.size.x * .5f, halfZ = panel.PhysicalCollider.size.z * .5f;
        float bottomLocalY = panel.PhysicalCollider.center.y - panel.PhysicalCollider.size.y * .5f;
        float bottomY = panel.PhysicalCollider.size.y;
        Vector3 c = panel.PhysicalCollider.center;
        Transform frame = requirement.DeckFrame;
        for (int i = 0; i < 4; i++)
        {
            Vector3 local = i == 0 ? new Vector3(c.x-halfX,bottomLocalY,c.z-halfZ)
                : i == 1 ? new Vector3(c.x+halfX,bottomLocalY,c.z-halfZ)
                : i == 2 ? new Vector3(c.x+halfX,bottomLocalY,c.z+halfZ)
                : new Vector3(c.x-halfX,bottomLocalY,c.z+halfZ);
            Vector3 world = panel.transform.TransformPoint(local);
            candidateWorldCorners[i] = frame.InverseTransformPoint(world);
        }
        if (!footprintCache.TryGetValue(panel, out footprint) || footprint == null)
            footprintCache[panel] = footprint = new Vector2[4];
        BridgeDeckAreaDiagnostic geometryDiagnostic = BridgeDeckAreaGeometry.DiagnosePanel(rules, candidateWorldCorners,
            Quaternion.Inverse(frame.rotation) * panel.transform.rotation, footprint);
        if (!BridgeDeckAreaGeometry.FootprintIntersectsArea(footprint, rules))
        { footprintValidCache[panel] = false; return false; }
        if (panel.CutStarted)
        {
            diagnostic = new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.UnfinishedCut);
            diagnosticCache[panel] = diagnostic; footprintValidCache[panel] = false; return false;
        }
        if (geometryDiagnostic.Kind != BridgeDeckAreaDiagnosticKind.None)
        {
            diagnostic = geometryDiagnostic; diagnosticCache[panel] = diagnostic;
            footprintValidCache[panel] = false; return false;
        }
        if (requirement.HasAuthority)
        {
            byte mask = CalculateQuarterMask(panel, requirement, bottomY, out bool bothGirders);
            secured = panel.IsLockedForCompletedArea || (mask == (byte)BridgeDeckPanelQuarterMask.All && bothGirders);
        }
        else
        {
            // Clients consume the per-panel replicated assessment, never infer nail authority locally.
            secured = panel.IsLockedForCompletedArea || panel.IsSecuredForArea;
        }
        footprintValidCache[panel] = true;
        securedCache[panel] = secured;
        footprint = footprintCache[panel];
        return true;
    }

    private bool TryMeasureBoundaryGap(Vector2[] polygon, bool leftBoundary, BridgeDeckAreaRules rules,
        out float minimum, out float maximum)
    {
        minimum = float.PositiveInfinity; maximum = 0f; levels.Clear();
        float halfWidth = rules.AreaWidth * .5f - rules.BoundaryMargin;
        levels.Add(-halfWidth); levels.Add(halfWidth);
        for (int i = 0; i < 4; i++) levels.Add(Mathf.Clamp(polygon[i].y, -halfWidth, halfWidth));
        levels.Sort();
        for (int i = levels.Count - 1; i > 0; i--) if (Mathf.Abs(levels[i] - levels[i - 1]) <= rules.Epsilon) levels.RemoveAt(i);
        for (int i = 0; i < levels.Count; i++)
        {
            IncludeBoundaryGap(polygon, levels[i], leftBoundary, rules.AreaLength * .5f, ref minimum, ref maximum);
            if (i + 1 < levels.Count) IncludeBoundaryGap(polygon, (levels[i] + levels[i + 1]) * .5f,
                leftBoundary, rules.AreaLength * .5f, ref minimum, ref maximum);
        }
        return !float.IsPositiveInfinity(minimum);
    }

    private static void IncludeBoundaryGap(Vector2[] polygon, float z, bool left, float halfLength,
        ref float min, ref float max)
    {
        if (!TryGetInterval(polygon, z, out float low, out float high)) return;
        float gap = left ? Mathf.Max(0f, low + halfLength) : Mathf.Max(0f, halfLength - high);
        min = Mathf.Min(min, gap); max = Mathf.Max(max, gap);
    }

    private byte CalculateQuarterMask(BridgeDeckAreaPanel panel, BridgeDeckAreaRequirement requirement, float thickness,
        out bool usesBothGirders)
    {
        byte mask = 0;
        int firstGirder = -1, secondGirder = -1;
        foreach (MountingNail nail in nails)
        {
            if (nail == null || !nail.TryGetAttachmentInfo(out MountingNailAttachmentInfo info)
                || info.State != MountingNailState.EmbeddedPair || info.FirstTarget == null
                || info.FirstTarget.GetComponentInParent<BridgeDeckAreaPanel>() != panel) continue;
            int girderIndex = requirement.GetRegisteredMainGirderIndex(info.SecondTarget);
            if (girderIndex < 0) continue;
            Vector3 dims = new(panel.LengthMillimeters * .001f, thickness, panel.WidthMillimeters * .001f);
            if (BridgeDeckPanelStage2Assessment.TryGetNailQuarter(info, panel.transform, dims,
                    requirement.NailAxisExclusionHalfWidth,
                    out BridgeDeckPanelQuarterMask quarter, out _))
            {
                byte bit = (byte)quarter;
                if ((mask & bit) != 0) continue;
                mask |= bit;
                if (firstGirder < 0) firstGirder = girderIndex;
                else if (girderIndex != firstGirder) secondGirder = girderIndex;
            }
        }
        usesBothGirders = firstGirder >= 0 && secondGirder >= 0;
        return mask;
    }

    private bool TryMeasureNeighborGap(Vector2[] left, Vector2[] right, BridgeDeckAreaRules rules,
        out float minimum, out float maximum)
    {
        minimum = float.PositiveInfinity; maximum = 0f; levels.Clear();
        float halfWidth = rules.AreaWidth * .5f - rules.BoundaryMargin;
        levels.Add(-halfWidth); levels.Add(halfWidth);
        for (int i = 0; i < 4; i++) { levels.Add(Mathf.Clamp(left[i].y, -halfWidth, halfWidth)); levels.Add(Mathf.Clamp(right[i].y, -halfWidth, halfWidth)); }
        for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++)
        {
            Vector2 p = left[a], r = left[(a + 1) & 3] - p, q = right[b], s = right[(b + 1) & 3] - q;
            float cross = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(cross) <= rules.Epsilon) continue;
            float t = ((q.x - p.x) * s.y - (q.y - p.y) * s.x) / cross;
            float u = ((q.x - p.x) * r.y - (q.y - p.y) * r.x) / cross;
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1) levels.Add(Mathf.Clamp(p.y + t * r.y, -halfWidth, halfWidth));
        }
        levels.Sort();
        for (int i = levels.Count - 1; i > 0; i--) if (Mathf.Abs(levels[i] - levels[i - 1]) <= rules.Epsilon) levels.RemoveAt(i);
        for (int i = 0; i < levels.Count; i++)
        {
            IncludeGap(left, right, levels[i], ref minimum, ref maximum);
            if (i + 1 < levels.Count) IncludeGap(left, right, (levels[i] + levels[i + 1]) * .5f, ref minimum, ref maximum);
        }
        if (float.IsPositiveInfinity(minimum)) return false;
        return true;
    }

    private static void IncludeGap(Vector2[] left, Vector2[] right, float z, ref float min, ref float max)
    {
        if (!TryGetInterval(left, z, out float leftMin, out float leftMax)
            || !TryGetInterval(right, z, out float rightMin, out float rightMax)) return;
        float gap = Mathf.Max(0f, rightMin - leftMax);
        min = Mathf.Min(min, gap); max = Mathf.Max(max, gap);
    }

    private static bool TryGetInterval(Vector2[] polygon, float z, out float min, out float max)
    {
        min = float.PositiveInfinity; max = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) & 3];
            if (z < Mathf.Min(a.y, b.y) - 1e-6f || z > Mathf.Max(a.y, b.y) + 1e-6f) continue;
            float x = Mathf.Abs(a.y - b.y) <= 1e-6f ? Mathf.Min(a.x, b.x)
                : Mathf.Lerp(a.x, b.x, Mathf.Clamp01((z - a.y) / (b.y - a.y)));
            min = Mathf.Min(min, x); max = Mathf.Max(max, x);
        }
        return !float.IsPositiveInfinity(min);
    }

    private static bool IsCorrectionOpening(float minimum, float maximum, BridgeDeckAreaRules rules)
        => maximum > rules.MaximumJointWidth + rules.Epsilon
            && minimum < rules.MinimumFragmentLength - rules.Epsilon;
}
