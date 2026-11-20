using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>Opt-in area requirement placed beside the bridge component whose stage owns the deck.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BridgeDeckAreaPresentation))]
public sealed class BridgeDeckAreaRequirement : MonoBehaviour
{
    [SerializeField] private BridgeDeckAreaProfile profile;
    [SerializeField] private Transform deckFrame;
    [SerializeField] private BridgeDeckSection deckSection;
    private readonly BridgeDeckAreaStageAssessment assessment = new();
    private BridgeDeckAreaStageResult lastResult;
    private float refreshTimer;
    private bool hasPreviousMetrics;
    private bool previousComplete;
    private float previousCovered;
    private float previousSecured;
    private int previousFingerprint;
    private readonly Vector3[] candidateCorners = new Vector3[4];
    private readonly Vector3[] localCorners = new Vector3[4];
    private readonly Vector2[] candidateFootprint = new Vector2[4];
    private readonly Vector3[] previewOtherCorners = new Vector3[4];
    private readonly Vector2[] previewOtherFootprint = new Vector2[4];
    private readonly Vector3[] previewCompareCorners = new Vector3[4];
    private readonly Vector2[] previewCompareFootprint = new Vector2[4];
    private readonly System.Collections.Generic.List<BridgeDeckAreaPanel> previewGeometryQualified = new();
    private readonly System.Collections.Generic.HashSet<BridgeDeckAreaPanel> previewOverlapPanels = new();
    private readonly Vector2[] aimedEdgeScratch = new Vector2[4];
    private readonly BridgeDeckAreaStageAssessment placementAssessment = new();
    private static readonly System.Collections.Generic.List<BridgeDeckAreaRequirement> Active = new();

    public BridgeDeckAreaProfile Profile => profile;
    public Transform DeckFrame => deckFrame != null ? deckFrame : transform;
    public BridgeDeckSection DeckSection => deckSection;
    public float NailAxisExclusionHalfWidth
    {
        get
        {
            BridgeComponent component = GetComponentInParent<BridgeComponent>();
            BridgeDeckPanelConstructionWorkflowSO workflow = component != null && component.GetBridgeComponentSO() != null
                ? component.GetBridgeComponentSO().deckPanelConstructionWorkflow : null;
            return workflow != null ? workflow.PhysicalPanelNailAxisExclusionHalfWidth : 0f;
        }
    }
    public NetworkManager NetworkManager
    {
        get
        {
            NetworkObject networkObject = GetComponentInParent<NetworkObject>();
            return networkObject != null && networkObject.NetworkManager != null
                ? networkObject.NetworkManager : global::Unity.Netcode.NetworkManager.Singleton;
        }
    }
    public BridgeDeckAreaStageResult LastResult => lastResult;
    public event Action<BridgeDeckAreaRequirement, BridgeDeckAreaStageResult> AssessmentChanged;

    private void Awake()
    {
        if (deckSection == null) deckSection = GetComponentInParent<BridgeDeckSection>();
        BridgeDeckAreaPresentation presentation = GetComponent<BridgeDeckAreaPresentation>();
        if (presentation != null) presentation.Bind(this);
    }

    private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    private void OnDisable() { Active.Remove(this); }

    private void Update()
    {
        if (!isActiveAndEnabled) return;
        refreshTimer += Time.deltaTime;
        if (refreshTimer < .2f) return;
        refreshTimer = 0f;
        BridgeDeckAreaStageResult current = Reassess();
        int fingerprint = BuildPresentationFingerprint(current);
        if (!hasPreviousMetrics || previousComplete != current.IsComplete
            || Mathf.Abs(previousCovered - current.CoveredLengthMeters) > .005f
            || previousSecured != current.SecuredLengthMeters || previousFingerprint != fingerprint)
        {
            GameplayManager manager = GameplayManager.Instance;
            if (HasAuthority && manager != null) manager.NotifyAreaRequirementChanged(this);
            else
            {
                AssessmentChanged?.Invoke(this, current);
                if (manager != null) manager.NotifyLocalAreaPresentationChanged();
            }
        }
        hasPreviousMetrics = true;
        previousComplete = current.IsComplete;
        previousCovered = current.CoveredLengthMeters;
        previousSecured = current.SecuredLengthMeters;
        previousFingerprint = fingerprint;
    }

    private static int BuildPresentationFingerprint(BridgeDeckAreaStageResult result)
    {
        HashCode hash = new();
        hash.Add(result.IsValid); hash.Add(result.IsComplete);
        for (int i = 0; i < result.ContributingPanels.Count; i++)
        {
            BridgeDeckAreaPanel panel = result.ContributingPanels[i];
            if (panel == null) { hash.Add(0); continue; }
            hash.Add(panel.GetInstanceID()); hash.Add(panel.IsSecuredForArea);
            hash.Add(panel.transform.position); hash.Add(panel.transform.rotation);
        }
        for (int i = 0; i < result.NeighborGaps.Count; i++)
        {
            BridgeDeckAreaNeighborGap gap = result.NeighborGaps[i];
            hash.Add(gap.First != null ? gap.First.GetInstanceID() : 0);
            hash.Add(gap.Second != null ? gap.Second.GetInstanceID() : 0);
            hash.Add(Mathf.RoundToInt(gap.MinimumMeters * 100000f));
            hash.Add(Mathf.RoundToInt(gap.MaximumMeters * 100000f));
        }
        return hash.ToHashCode();
    }

    public BridgeDeckAreaStageResult Reassess()
    {
        BridgeDeckAreaStageResult next = assessment.Evaluate(this);
        lastResult = next;
        AssessmentChanged?.Invoke(this, next);
        return next;
    }

    public bool IsRegisteredMainGirder(Transform target)
        => GetRegisteredMainGirderIndex(target) >= 0;

    public int GetRegisteredMainGirderIndex(Transform target)
    {
        if (target == null || deckSection == null || deckSection.MainGirders == null) return -1;
        BridgeComponent girder = target.GetComponentInParent<BridgeComponent>();
        if (girder == null) return -1;
        for (int i = 0; i < deckSection.MainGirders.Count; i++)
            if (deckSection.MainGirders[i] == girder) return i;
        return -1;
    }

    public static bool TryGetPanelStagePresentation(BridgeDeckAreaPanel panel, out bool active,
        out BridgeDeckAreaStageResult result)
        => TryGetPanelStagePresentation(panel, out active, out result, out _);

    public static bool TryGetPanelStagePresentation(BridgeDeckAreaPanel panel, out bool active,
        out BridgeDeckAreaStageResult result, out BridgeDeckAreaRequirement matchedRequirement)
    {
        active = false; result = null; matchedRequirement = null;
        if (panel == null) return false;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            BridgeDeckAreaRequirement candidate = Active[i];
            if (candidate == null) { Active.RemoveAt(i); continue; }
            if (candidate.gameObject.scene != panel.gameObject.scene || !SameNetworkScope(candidate.NetworkManager, panel.NetworkManager)) continue;
            GameplayManager manager = GameplayManager.Instance;
            active = manager != null && manager.IsAreaRequirementPresented(candidate);
            matchedRequirement = candidate;
            if (!active) return true;
            result = candidate.Reassess();
            return true;
        }
        return false;
    }

    public static bool TryGetPresentedInScene(UnityEngine.SceneManagement.Scene scene, NetworkManager scope,
        out BridgeDeckAreaRequirement requirement)
    {
        requirement = null;
        GameplayManager manager = GameplayManager.Instance;
        if (manager == null) return false;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            BridgeDeckAreaRequirement candidate = Active[i];
            if (candidate == null) { Active.RemoveAt(i); continue; }
            if (candidate.gameObject.scene != scene || !SameNetworkScope(candidate.NetworkManager, scope)
                || !manager.IsAreaRequirementActive(candidate)) continue;
            requirement = candidate;
            return true;
        }
        return false;
    }

    private static bool SameNetworkScope(NetworkManager first, NetworkManager second)
    {
        if (first == second) return true;
        return first == null && (second == null || !second.IsListening)
            || second == null && (first == null || !first.IsListening);
    }

    public static bool IsAreaPanelLocked(Transform target)
    {
        BridgeDeckAreaPanel panel = target != null ? target.GetComponentInParent<BridgeDeckAreaPanel>() : null;
        return panel != null && panel.IsLockedForCompletedArea;
    }

    public static bool RefreshPanelBeforeNailRemoval(Transform target)
    {
        BridgeDeckAreaPanel panel = target != null ? target.GetComponentInParent<BridgeDeckAreaPanel>() : null;
        if (panel == null) return false;
        bool locked = panel.IsLockedForCompletedArea;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            BridgeDeckAreaRequirement requirement = Active[i];
            if (requirement == null) { Active.RemoveAt(i); continue; }
            if (requirement.gameObject.scene != panel.gameObject.scene || !requirement.HasAuthority
                || !SameNetworkScope(requirement.NetworkManager, panel.NetworkManager)) continue;
            BridgeDeckAreaStageResult result = requirement.Reassess();
            if (result.ContributingPanels.Contains(panel))
            {
                GameplayManager manager = GameplayManager.Instance;
                if (manager != null) manager.NotifyAreaRequirementChanged(requirement);
            }
            locked |= panel.IsLockedForCompletedArea;
        }
        return locked;
    }

    public static string GetPlacementCandidateMessage(BridgeDeckAreaPanel panel, Vector3 position,
        Quaternion rotation, out bool areaScope, out bool stageActive)
    {
        bool hasDiagnostic = TryGetPlacementCandidateDiagnostic(panel, position, rotation,
            out BridgeDeckAreaDiagnostic diagnostic, out areaScope, out stageActive);
        return !hasDiagnostic ? string.Empty : FormatDiagnostic(diagnostic, false, false);
    }

    public static bool TryGetPlacementCandidateDiagnostic(BridgeDeckAreaPanel panel, Vector3 position,
        Quaternion rotation, out BridgeDeckAreaDiagnostic diagnostic, out bool areaScope, out bool stageActive)
    {
        diagnostic = default; areaScope = false; stageActive = false;
        if (panel == null) return false;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            BridgeDeckAreaRequirement candidate = Active[i];
            if (candidate == null) { Active.RemoveAt(i); continue; }
            if (candidate.gameObject.scene != panel.gameObject.scene || !SameNetworkScope(candidate.NetworkManager, panel.NetworkManager)) continue;
            areaScope = true;
            stageActive = GameplayManager.Instance != null && GameplayManager.Instance.IsAreaRequirementActive(candidate);
            if (!stageActive) return false;
            BoxCollider box = panel.PhysicalCollider;
            if (candidate.Profile == null || box == null)
            {
                diagnostic = new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.InvalidGeometry);
                return true;
            }
            BridgeDeckAreaRules rules = candidate.Profile.ToRules();
            float hx = box.size.x * .5f, hz = box.size.z * .5f;
            float y = box.center.y - box.size.y * .5f;
            Vector3 c = box.center;
            candidate.localCorners[0] = new Vector3(c.x-hx,y,c.z-hz);
            candidate.localCorners[1] = new Vector3(c.x+hx,y,c.z-hz);
            candidate.localCorners[2] = new Vector3(c.x+hx,y,c.z+hz);
            candidate.localCorners[3] = new Vector3(c.x-hx,y,c.z+hz);
            for (int n = 0; n < 4; n++)
                candidate.candidateCorners[n] = candidate.DeckFrame.InverseTransformPoint(position + rotation * candidate.localCorners[n]);
            Quaternion candidateRotation = Quaternion.Inverse(candidate.DeckFrame.rotation) * rotation;
            if (!BridgeDeckAreaGeometry.TryProjectFiniteFootprint(rules, candidate.candidateCorners,
                    candidateRotation, candidate.candidateFootprint))
            {
                diagnostic = new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.InvalidGeometry);
                return true;
            }
            if (!BridgeDeckAreaGeometry.FootprintIntersectsArea(candidate.candidateFootprint, rules))
            {
                diagnostic = new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.OutsideArea);
                return true;
            }
            if (panel.CutStarted)
            {
                diagnostic = new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.UnfinishedCut);
                return true;
            }

            // An extreme pitch/roll can collapse the XZ projection. Preserve the priority of
            // orientation feedback before running overlap/opening measurements on that projection.
            BridgeDeckAreaDiagnostic early = BridgeDeckAreaGeometry.SelectPositionDiagnostic(rules,
                candidate.candidateCorners, candidateRotation, candidate.candidateFootprint,
                false, 0f, false, 0f, 0f);
            if (early.Kind == BridgeDeckAreaDiagnosticKind.Orientation
                || early.Kind == BridgeDeckAreaDiagnosticKind.InvalidGeometry)
            {
                diagnostic = early;
                return true;
            }

            BridgeDeckAreaPanel.CopyActivePanels(candidate.gameObject.scene, candidate.NetworkManager, ActivePanelScratch);
            float centerX = (candidate.candidateFootprint[0].x + candidate.candidateFootprint[1].x
                + candidate.candidateFootprint[2].x + candidate.candidateFootprint[3].x) * .25f;
            BridgeDeckAreaPanel nearestLeft = null, nearestRight = null;
            float leftCenter = float.NegativeInfinity, rightCenter = float.PositiveInfinity;
            bool hasOverlap = false;
            float maximumOverlap = 0f;
            candidate.previewGeometryQualified.Clear();
            candidate.previewOverlapPanels.Clear();
            foreach (BridgeDeckAreaPanel other in ActivePanelScratch)
            {
                if (other == null || other == panel || other.CutStarted || other.PhysicalCollider == null) continue;
                Vector2[] otherFootprint = GetFootprintForPreview(candidate, other);
                if (otherFootprint == null) continue;
                candidate.previewGeometryQualified.Add(other);
                if (BridgeDeckAreaGeometry.PolygonsOverlap(candidate.candidateFootprint, otherFootprint, rules.Epsilon))
                {
                    hasOverlap = true;
                    maximumOverlap = Mathf.Max(maximumOverlap,
                        candidate.placementAssessment.MeasureMaximumOverlap(candidate.candidateFootprint, otherFootprint, rules));
                }
            }

            // Match the laid-panel opening assessment: only geometrically valid panels which
            // survive overlap exclusion define contributor-to-contributor openings.
            for (int overlapIndex = 0; overlapIndex < candidate.previewGeometryQualified.Count; overlapIndex++)
            for (int otherIndex = overlapIndex + 1; otherIndex < candidate.previewGeometryQualified.Count; otherIndex++)
            {
                Vector2[] a = GetFootprintForPreview(candidate, candidate.previewGeometryQualified[overlapIndex],
                    candidate.previewOtherCorners, candidate.previewOtherFootprint);
                Vector2[] b = GetFootprintForPreview(candidate, candidate.previewGeometryQualified[otherIndex],
                    candidate.previewCompareCorners, candidate.previewCompareFootprint);
                if (a == null || b == null || !BridgeDeckAreaGeometry.PolygonsOverlap(a, b, rules.Epsilon)) continue;
                candidate.previewOverlapPanels.Add(candidate.previewGeometryQualified[overlapIndex]);
                candidate.previewOverlapPanels.Add(candidate.previewGeometryQualified[otherIndex]);
            }
            for (int neighborIndex = 0; neighborIndex < candidate.previewGeometryQualified.Count; neighborIndex++)
            {
                BridgeDeckAreaPanel other = candidate.previewGeometryQualified[neighborIndex];
                if (candidate.previewOverlapPanels.Contains(other)) continue;
                Vector2[] otherFootprint = GetFootprintForPreview(candidate, other);
                if (otherFootprint == null || !BridgeDeckAreaGeometry.CanMeasureAcrossAreaWidth(otherFootprint, rules)) continue;
                float otherCenterX = (otherFootprint[0].x + otherFootprint[1].x + otherFootprint[2].x + otherFootprint[3].x) * .25f;
                if (otherCenterX < centerX && otherCenterX > leftCenter)
                { leftCenter = otherCenterX; nearestLeft = other; }
                else if (otherCenterX > centerX && otherCenterX < rightCenter)
                { rightCenter = otherCenterX; nearestRight = other; }
            }
            bool hasNarrow = false;
            float minimumGap = 0f, maximumGap = 0f;
            if (BridgeDeckAreaGeometry.CanMeasureAcrossAreaWidth(candidate.candidateFootprint, rules))
            {
                Vector2[] leftFootprint = nearestLeft != null ? GetFootprintForPreview(candidate, nearestLeft) : null;
                if (nearestLeft != null && candidate.placementAssessment.MeasureNeighborOpening(leftFootprint,
                        candidate.candidateFootprint, rules, out float leftMin, out float leftMax)
                    && IsCorrectionOpening(leftMin, leftMax, rules))
                {
                    hasNarrow = true; minimumGap = leftMin; maximumGap = leftMax;
                }
                if (!hasNarrow && nearestLeft == null && candidate.placementAssessment.MeasureBoundaryOpening(candidate.candidateFootprint,
                        true, rules, out float leftBoundaryMin, out float leftBoundaryMax)
                    && IsCorrectionOpening(leftBoundaryMin, leftBoundaryMax, rules))
                { hasNarrow = true; minimumGap = leftBoundaryMin; maximumGap = leftBoundaryMax; }
                Vector2[] rightFootprint = nearestRight != null ? GetFootprintForPreview(candidate, nearestRight) : null;
                if (!hasNarrow && nearestRight != null && candidate.placementAssessment.MeasureNeighborOpening(candidate.candidateFootprint,
                        rightFootprint, rules, out float rightMin, out float rightMax)
                    && IsCorrectionOpening(rightMin, rightMax, rules))
                { hasNarrow = true; minimumGap = rightMin; maximumGap = rightMax; }
                if (!hasNarrow && nearestRight == null && candidate.placementAssessment.MeasureBoundaryOpening(candidate.candidateFootprint,
                        false, rules, out float rightBoundaryMin, out float rightBoundaryMax)
                    && IsCorrectionOpening(rightBoundaryMin, rightBoundaryMax, rules))
                { hasNarrow = true; minimumGap = rightBoundaryMin; maximumGap = rightBoundaryMax; }
            }

            diagnostic = BridgeDeckAreaGeometry.SelectPositionDiagnostic(rules, candidate.candidateCorners,
                candidateRotation, candidate.candidateFootprint, hasOverlap, maximumOverlap,
                hasNarrow, minimumGap, maximumGap);
            return true;
        }
        return false;
    }

    public bool TryGetPanelDiagnostic(BridgeDeckAreaPanel panel, BridgeDeckAreaStageResult result,
        out BridgeDeckAreaDiagnostic diagnostic)
    {
        bool found = TryGetPanelPresentationDiagnostic(panel, result, out diagnostic, out _);
        return found;
    }

    public bool TryGetPanelPresentationDiagnostic(BridgeDeckAreaPanel panel, BridgeDeckAreaStageResult result,
        out BridgeDeckAreaDiagnostic diagnostic, out bool hasFourPhysicalNails)
    {
        diagnostic = default; hasFourPhysicalNails = false;
        if (panel == null || result == null) return false;
        for (int i = 0; i < result.PanelDiagnostics.Count; i++)
        {
            BridgeDeckAreaPanelDiagnosticEntry entry = result.PanelDiagnostics[i];
            if (entry.Panel != panel) continue;
            diagnostic = entry.Diagnostic;
            hasFourPhysicalNails = entry.HasFourPhysicalNails;
            return true;
        }
        if (!result.ContributingPanels.Contains(panel)) return false;
        if (panel.IsLockedForCompletedArea)
        {
            diagnostic = new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.Locked);
            return true;
        }
        if (TryGetNarrowGapForPanel(panel, result, out float min, out float max))
        {
            diagnostic = new BridgeDeckAreaDiagnostic(BridgeDeckAreaDiagnosticKind.NarrowOpening,
                BridgeDeckAreaCorrection.SpreadPanels, 0f, 0f, min, max, Profile.ToRules().MinimumFragmentLength);
            return true;
        }
        diagnostic = new BridgeDeckAreaDiagnostic(panel.IsSecuredForArea
            ? BridgeDeckAreaDiagnosticKind.FourNails : BridgeDeckAreaDiagnosticKind.NeedsNails);
        hasFourPhysicalNails = panel.IsSecuredForArea;
        return true;
    }

    public bool HasFourPhysicalNails(BridgeDeckAreaPanel panel)
        => assessment.HasFourPhysicalNails(panel, this);

    private bool TryGetNarrowGapForPanel(BridgeDeckAreaPanel panel, BridgeDeckAreaStageResult result,
        out float minimum, out float maximum)
    {
        minimum = maximum = 0f;
        if (Profile == null) return false;
        for (int i = 0; i < result.NeighborGaps.Count; i++)
        {
            BridgeDeckAreaNeighborGap gap = result.NeighborGaps[i];
            if (gap.First != panel && gap.Second != panel) continue;
            if (IsCorrectionOpening(gap.MinimumMeters, gap.MaximumMeters, Profile.ToRules()))
            { minimum = gap.MinimumMeters; maximum = gap.MaximumMeters; return true; }
        }
        return false;
    }

    public static string FormatDiagnostic(BridgeDeckAreaDiagnostic diagnostic, bool hasFourNails, bool locked)
    {
        string Mm(float meters)
        {
            float millimeters = meters * 1000f;
            return meters > 0f && millimeters < .0005f ? "<1" : millimeters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
        switch (diagnostic.Kind)
        {
            case BridgeDeckAreaDiagnosticKind.OutsideArea: return "Move the plate over the bridge.";
            case BridgeDeckAreaDiagnosticKind.UnfinishedCut: return "Finish cutting this plate.";
            case BridgeDeckAreaDiagnosticKind.Orientation:
                return $"Rotation is {diagnostic.AngleDegrees.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}° off. Align with the bridge at 0° or 180°.";
            case BridgeDeckAreaDiagnosticKind.Height:
                return $"{ (diagnostic.Correction == BridgeDeckAreaCorrection.Raise ? "Raise" : "Lower") } {Mm(diagnostic.CorrectionMeters)} mm; underside exceeds tolerance by {Mm(diagnostic.AmountMeters)} mm.";
            case BridgeDeckAreaDiagnosticKind.LongitudinalBounds:
                return $"Move inward by {Mm(Mathf.Abs(diagnostic.CorrectionMeters))} mm to clear the end overhang.";
            case BridgeDeckAreaDiagnosticKind.LateralOverhang:
                return diagnostic.Correction == BridgeDeckAreaCorrection.Reorient
                    ? $"Side overhang: {Mm(diagnostic.AmountMeters)} mm. Realign rotation; sliding cannot fit both edges."
                    : $"Side overhang: {Mm(diagnostic.AmountMeters)} mm. Move toward the deck center by {Mm(Mathf.Abs(diagnostic.CorrectionMeters))} mm.";
            case BridgeDeckAreaDiagnosticKind.LateralDeficit:
                return diagnostic.Correction == BridgeDeckAreaCorrection.Reorient
                    ? $"Full-width deficit: {Mm(diagnostic.AmountMeters)} mm. Align rotation or use a full-width plate."
                    : $"Full-width deficit: {Mm(diagnostic.AmountMeters)} mm. Move toward the deck center by {Mm(Mathf.Abs(diagnostic.CorrectionMeters))} mm.";
            case BridgeDeckAreaDiagnosticKind.Overlap:
                return $"Overlap: {Mm(diagnostic.AmountMeters)} mm. Spread the plates along the bridge.";
            case BridgeDeckAreaDiagnosticKind.NarrowOpening:
                string nails = locked ? " Locked after completion." : hasFourNails ? " 4/4 nails." : " Needs four nails.";
                return $"Opening {Mm(diagnostic.MinimumGapMeters)}–{Mm(diagnostic.MaximumGapMeters)} mm too narrow (min {Mm(diagnostic.RequiredGapMeters)} mm). Change spacing.{nails}";
            case BridgeDeckAreaDiagnosticKind.NeedsNails: return "Needs four nails.";
            case BridgeDeckAreaDiagnosticKind.FourNails: return "4/4 nails.";
            case BridgeDeckAreaDiagnosticKind.Locked: return "Locked after completion.";
            default: return diagnostic.Kind == BridgeDeckAreaDiagnosticKind.None ? "Position accepted." : "Check plate geometry.";
        }
    }

    private static readonly System.Collections.Generic.List<BridgeDeckAreaPanel> ActivePanelScratch = new();
    private static bool IsCorrectionOpening(float minimum, float maximum, BridgeDeckAreaRules rules)
        => maximum > rules.MaximumJointWidth + rules.Epsilon
            && minimum < rules.MinimumFragmentLength - rules.Epsilon;
    private static Vector2[] GetFootprintForPreview(BridgeDeckAreaRequirement candidate, BridgeDeckAreaPanel panel)
        => GetFootprintForPreview(candidate, panel, candidate.previewOtherCorners, candidate.previewOtherFootprint);

    private static Vector2[] GetFootprintForPreview(BridgeDeckAreaRequirement candidate, BridgeDeckAreaPanel panel,
        Vector3[] cornerBuffer, Vector2[] footprintBuffer)
    {
        if (candidate == null || panel == null || panel.CutStarted) return null;
        BoxCollider box = panel.PhysicalCollider;
        if (box == null || !box.enabled || box.isTrigger) return null;
        Vector3 c = box.center;
        float hx = box.size.x * .5f, hz = box.size.z * .5f, y = c.y - box.size.y * .5f;
        Vector2[] footprint = footprintBuffer;
        for (int i = 0; i < 4; i++)
        {
            Vector3 local = i == 0 ? new Vector3(c.x-hx,y,c.z-hz) : i == 1 ? new Vector3(c.x+hx,y,c.z-hz)
                : i == 2 ? new Vector3(c.x+hx,y,c.z+hz) : new Vector3(c.x-hx,y,c.z+hz);
            Vector3 point = candidate.DeckFrame.InverseTransformPoint(panel.transform.TransformPoint(local));
            cornerBuffer[i] = point;
        }
        return BridgeDeckAreaGeometry.ValidatePanel(candidate.Profile.ToRules(), cornerBuffer,
            Quaternion.Inverse(candidate.DeckFrame.rotation) * panel.transform.rotation, footprint) == BridgeDeckAreaPanelFailure.None
            ? footprint : null;
    }

    public string GetGapGuidance()
    {
        return FormatGapGuidance(lastResult);
    }

    public bool TryGetAimedGap(Ray ray, out BridgeDeckAreaNeighborGap selected,
        out float localZ, out float leftX, out float rightX)
    {
        selected = default; localZ = leftX = rightX = 0f;
        BridgeDeckAreaStageResult result = lastResult;
        if (result == null || DeckFrame == null || Profile == null) return false;
        Plane plane = new(DeckFrame.up, DeckFrame.position);
        if (!plane.Raycast(ray, out float enter) || enter < 0f) return false;
        Vector3 local = DeckFrame.InverseTransformPoint(ray.GetPoint(enter));
        if (Mathf.Abs(local.x) > Profile.areaLengthMeters * .5f + Profile.boundaryMarginMeters
            || Mathf.Abs(local.z) > Profile.areaWidthMeters * .5f + Profile.boundaryMarginMeters) return false;
        if (Mathf.Abs(local.z) > Profile.areaWidthMeters * .5f + Profile.boundaryMarginMeters) return false;
        localZ = Mathf.Clamp(local.z, -Profile.areaWidthMeters * .5f, Profile.areaWidthMeters * .5f);
        float best = float.PositiveInfinity;
        for (int i = 0; i < result.NeighborGaps.Count; i++)
        {
            BridgeDeckAreaNeighborGap gap = result.NeighborGaps[i];
            float a = gap.First == null ? -Profile.areaLengthMeters * .5f : GetPanelEdgeX(gap.First, localZ, true);
            float b = gap.Second == null ? Profile.areaLengthMeters * .5f : GetPanelEdgeX(gap.Second, localZ, false);
            if (float.IsNaN(a) || float.IsNaN(b)) continue;
            float distance = local.x < a ? a - local.x : local.x > b ? local.x - b : 0f;
            if (distance > best || distance > .25f) continue;
            best = distance; selected = gap; leftX = a; rightX = b;
        }
        return best < float.PositiveInfinity;
    }

    public string GetAimedGapGuidance(Transform interactor)
    {
        Camera camera = interactor != null ? interactor.GetComponentInChildren<Camera>() : null;
        if (camera == null) camera = Camera.main;
        if (camera != null && TryGetAimedGap(new Ray(camera.transform.position, camera.transform.forward),
                out BridgeDeckAreaNeighborGap gap, out _, out _, out _))
        {
            string name = gap.First == null && gap.Second == null ? "Open area"
                : gap.First == null ? "Left end" : gap.Second == null ? "Right end" : "Opening";
            bool correction = gap.MaximumMeters > Profile.maximumJointWidthMeters + Profile.geometryEpsilonMeters
                && gap.MinimumMeters < Profile.minimumFragmentLengthMeters - Profile.geometryEpsilonMeters;
            string measuredGap = $"{name}: {FormatMillimeters(gap.MinimumMeters)}–{FormatMillimeters(gap.MaximumMeters)} mm"
                + (correction ? $" too narrow (min {FormatMillimeters(Profile.minimumFragmentLengthMeters)} mm). Change spacing." : string.Empty);
            BridgeDeckAreaStageResult current = lastResult;
            if (current == null) return measuredGap;
            return $"Covered: {current.CoveredLengthMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} m\n"
                + $"Secured: {current.SecuredLengthMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} m\n"
                + measuredGap;
        }
        return string.Empty;
    }

    private static string FormatMillimeters(float meters)
        => (meters * 1000f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private float GetPanelEdgeX(BridgeDeckAreaPanel panel, float localZ, bool rightEdge)
    {
        if (panel == null || panel.PhysicalCollider == null || DeckFrame == null) return float.NaN;
        BoxCollider box = panel.PhysicalCollider; Vector3 c = box.center;
        float hx = box.size.x * .5f, hz = box.size.z * .5f, y = c.y - box.size.y * .5f;
        Vector2[] points = aimedEdgeScratch;
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = i == 0 ? new(c.x-hx,y,c.z-hz) : i == 1 ? new(c.x+hx,y,c.z-hz)
                : i == 2 ? new(c.x+hx,y,c.z+hz) : new(c.x-hx,y,c.z+hz);
            Vector3 q = DeckFrame.InverseTransformPoint(panel.transform.TransformPoint(p)); points[i] = new(q.x,q.z);
        }
        float edge = rightEdge ? float.NegativeInfinity : float.PositiveInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = points[i], b = points[(i+1)&3];
            if (localZ < Mathf.Min(a.y,b.y)-.00001f || localZ > Mathf.Max(a.y,b.y)+.00001f) continue;
            float x = Mathf.Abs(a.y-b.y) < .00001f ? (rightEdge ? Mathf.Max(a.x,b.x) : Mathf.Min(a.x,b.x))
                : Mathf.Lerp(a.x,b.x,Mathf.Clamp01((localZ-a.y)/(b.y-a.y)));
            edge = rightEdge ? Mathf.Max(edge,x) : Mathf.Min(edge,x);
        }
        return float.IsInfinity(edge) ? float.NaN : edge;
    }

    public static string FormatGapGuidance(BridgeDeckAreaStageResult result)
    {
        if (result == null || result.NeighborGaps.Count == 0) return "Deck joints: none measured";
        System.Text.StringBuilder text = new("Gaps: ");
        for (int i = 0; i < result.NeighborGaps.Count; i++)
        {
            if (i > 0) text.Append("; ");
            BridgeDeckAreaNeighborGap gap = result.NeighborGaps[i];
            string name = gap.First == null ? "Left end" : gap.Second == null ? "Right end" : $"{gap.First.name}–{gap.Second.name}";
            text.Append(name).Append(' ').Append((gap.MinimumMeters * 1000f).ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append('–')
                .Append((gap.MaximumMeters * 1000f).ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append(" mm");
        }
        return text.ToString();
    }

    public bool HasAuthority
    {
        get
        {
            NetworkManager manager = NetworkManager;
            return manager == null || !manager.IsListening || manager.IsServer;
        }
    }

    public bool TryPrepareFinalization()
    {
        if (!HasAuthority) return false;
        BridgeDeckAreaStageResult result = Reassess();
        if (!result.IsComplete) return false;
        for (int i = 0; i < result.ContributingPanels.Count; i++)
        {
            BridgeDeckAreaPanel panel = result.ContributingPanels[i];
            if (panel == null || !panel.IsSecuredForArea) return false;
        }
        return true;
    }

    public bool CommitFinalization()
    {
        if (!HasAuthority || lastResult == null || !lastResult.IsComplete) return false;
        for (int i = 0; i < lastResult.ContributingPanels.Count; i++)
            if (lastResult.ContributingPanels[i] == null || !lastResult.ContributingPanels[i].IsSecuredForArea) return false;
        for (int i = 0; i < lastResult.ContributingPanels.Count; i++)
        {
            if (!lastResult.ContributingPanels[i].SetAreaLock(true))
            {
                return false;
            }
        }
        return true;
    }

    public void RollbackFinalization()
    {
        // A successfully committed area lock is durable. CommitFinalization prevalidates every
        // contributor before mutating any panel, so a failed prepare never needs to unlock it.
    }

    public void RefreshPanelStates()
    {
        if (!HasAuthority) return;
        Reassess();
    }
}
