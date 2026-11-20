using System.Collections.Generic;
using UnityEngine;

/// <summary>Local, stage-gated area outline and selected-opening feedback for the flat deck.</summary>
[DisallowMultipleComponent]
public sealed class BridgeDeckAreaPresentation : MonoBehaviour
{
    [SerializeField] private BridgeDeckAreaRequirement requirement;
    private readonly List<LineRenderer> panelLines = new();
    private LineRenderer areaLine;
    private LineRenderer gapLine;
    private Material lineMaterial;
    private float refresh;
    private static readonly Color CoveredColor = new(1f, .68f, .15f, .95f);
    private static readonly Color SecuredColor = new(.2f, .92f, .36f, .95f);
    private static readonly Color ClosedGapColor = new(.2f, .82f, .95f, .95f);
    private static readonly Color CorrectionColor = new(1f, .2f, .12f, .98f);
    private readonly Vector3[] areaCorners = new Vector3[4];
    private readonly Vector3[] panelCorners = new Vector3[4];
    private readonly Vector3[] gapCorners = new Vector3[2];

    private void Awake()
    {
        lineMaterial = requirement != null && requirement.Profile != null
            ? requirement.Profile.presentationLineMaterial : null;
        areaLine = CreateLine("Deck Area Outline", 5);
        gapLine = CreateLine("Selected Deck Opening", 7);
    }

    internal void Bind(BridgeDeckAreaRequirement owner)
    {
        requirement = owner;
        lineMaterial = owner != null && owner.Profile != null
            ? owner.Profile.presentationLineMaterial : null;
        AssignLineMaterial(areaLine);
        AssignLineMaterial(gapLine);
        for (int i = 0; i < panelLines.Count; i++) AssignLineMaterial(panelLines[i]);
    }

    private void AssignLineMaterial(LineRenderer line)
    {
        if (line != null && lineMaterial != null) line.sharedMaterial = lineMaterial;
    }

    private void LateUpdate()
    {
        if (requirement == null || requirement.Profile == null || GameplayManager.Instance == null
            || !GameplayManager.Instance.IsAreaRequirementActive(requirement))
        { SetVisible(false); return; }
        refresh += Time.unscaledDeltaTime;
        if (refresh < .15f) return;
        refresh = 0f;
        BridgeDeckAreaStageResult result = requirement.Reassess();
        if (!result.IsValid) { SetVisible(false); return; }
        EnsurePanelLineCount(result.ContributingPanels.Count);
        float y = requirement.Profile.undersideHeightToleranceMeters + .025f;
        float halfLength = requirement.Profile.areaLengthMeters * .5f;
        float halfWidth = requirement.Profile.areaWidthMeters * .5f;
        areaCorners[0] = new Vector3(-halfLength,y,-halfWidth); areaCorners[1] = new Vector3(halfLength,y,-halfWidth);
        areaCorners[2] = new Vector3(halfLength,y,halfWidth); areaCorners[3] = new Vector3(-halfLength,y,halfWidth);
        SetLoop(areaLine, requirement.DeckFrame, areaCorners, ClosedGapColor);
        for (int i = 0; i < result.ContributingPanels.Count; i++)
        {
            BridgeDeckAreaPanel panel = result.ContributingPanels[i];
            if (panel == null) { panelLines[i].enabled = false; continue; }
            BoxCollider box = panel.PhysicalCollider;
            Vector3 c = box.center;
            float hx = box.size.x * .5f, hz = box.size.z * .5f;
            panelCorners[0] = new Vector3(c.x-hx,c.y+box.size.y*.5f+.015f,c.z-hz);
            panelCorners[1] = new Vector3(c.x+hx,c.y+box.size.y*.5f+.015f,c.z-hz);
            panelCorners[2] = new Vector3(c.x+hx,c.y+box.size.y*.5f+.015f,c.z+hz);
            panelCorners[3] = new Vector3(c.x-hx,c.y+box.size.y*.5f+.015f,c.z+hz);
            SetLoop(panelLines[i], panel.transform, panelCorners, panel.IsSecuredForArea || panel.IsLockedForCompletedArea ? SecuredColor : CoveredColor);
        }
        Ray ray = Camera.main != null ? new Ray(Camera.main.transform.position, Camera.main.transform.forward) : default;
        if (Camera.main != null && requirement.TryGetAimedGap(ray, out BridgeDeckAreaNeighborGap selected,
                out float localZ, out float leftX, out float rightX))
        {
            Color color = selected.MaximumMeters > requirement.Profile.maximumJointWidthMeters + requirement.Profile.geometryEpsilonMeters
                && selected.MinimumMeters < requirement.Profile.minimumFragmentLengthMeters - requirement.Profile.geometryEpsilonMeters
                ? CorrectionColor : ClosedGapColor;
            gapCorners[0] = new Vector3(leftX,y,localZ); gapCorners[1] = new Vector3(rightX,y,localZ);
            SetLine(gapLine, requirement.DeckFrame, gapCorners, color);
        }
        else gapLine.enabled = false;
    }

    private LineRenderer CreateLine(string lineName, int order)
    {
        GameObject lineObject = new(lineName);
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true; line.loop = false; line.positionCount = 0;
        line.startWidth = line.endWidth = .025f; line.sortingOrder = order;
        if (lineMaterial != null) line.sharedMaterial = lineMaterial;
        line.enabled = false;
        return line;
    }

    private void EnsurePanelLineCount(int count)
    {
        while (panelLines.Count < count) panelLines.Add(CreateLine($"Deck Panel Outline {panelLines.Count}", 6));
        for (int i = count; i < panelLines.Count; i++) panelLines[i].enabled = false;
    }

    private static void SetLoop(LineRenderer line, Transform frame, Vector3[] points, Color color)
    {
        line.loop = true; SetLine(line, frame, points, color);
    }

    private static void SetLine(LineRenderer line, Transform frame, Vector3[] points, Color color)
    {
        line.positionCount = points.Length;
        for (int i = 0; i < points.Length; i++) line.SetPosition(i, frame.TransformPoint(points[i]));
        line.startColor = line.endColor = color; line.enabled = true;
    }

    private void SetVisible(bool visible)
    {
        if (areaLine != null) areaLine.enabled = visible;
        if (gapLine != null) gapLine.enabled = visible && gapLine.positionCount > 0;
        foreach (LineRenderer line in panelLines) if (line != null) line.enabled &= visible;
    }

    private void OnDestroy()
    {
    }
}
