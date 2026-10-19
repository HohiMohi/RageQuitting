using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Local-only visual and compact HUD guidance for mounting a carried bridge panel.</summary>
internal sealed class BridgeDeckPanelPlacementGuidance
{
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorProperty = Shader.PropertyToID("_Color");
    private readonly Object owner;
    private BridgeDeckPanelSlotOutline outline;
    private GameObject arrowObject;
    private LineRenderer arrow;
    private MaterialPropertyBlock arrowProperties;
    private string label = string.Empty;
    private BridgeDeckPanelPlacementFailure lastFailure;
    private int lastAlongCentimeters = int.MinValue;
    private int lastAcrossCentimeters = int.MinValue;
    private int lastHeightCentimeters = int.MinValue;
    private int lastAngleDegrees = int.MinValue;
    private bool lastHorizontalOutside;
    private bool lastAlongOutside;
    private bool lastAcrossOutside;
    private bool lastHeightOutside;
    private bool lastOrientationOutside;

    public string Label => label;

    public BridgeDeckPanelPlacementGuidance(Object owner) => this.owner = owner;

    public void Refresh(BridgeDeckPanelPlacementAssessment result, Vector3 candidatePosition,
        Camera aimCamera, int renderLayer)
    {
        if (outline != result.Outline)
        {
            ClearOutlineRequest();
            outline = result.Outline;
        }
        if (outline != null)
            outline.SetPlacementFeedback(owner, result.Failure == BridgeDeckPanelPlacementFailure.None);

        int alongCm = Mathf.RoundToInt(-result.AlongMeters * 100f);
        int acrossCm = Mathf.RoundToInt(-result.AcrossMeters * 100f);
        int heightCm = Mathf.RoundToInt(-result.HeightMeters * 100f);
        int angleDegrees = Mathf.RoundToInt(result.OrientationDegrees);
        UpdateLabel(result, alongCm, acrossCm, heightCm, angleDegrees);

        if (!result.HorizontalOutside && !result.HeightOutside
            || outline == null || aimCamera == null || renderLayer < 0)
        {
            HideArrow();
            return;
        }

        Transform slot = outline.transform.parent;
        if (slot == null) { HideArrow(); return; }
        Vector3 correction = Vector3.zero;
        if (result.AlongOutside) correction += slot.right * -result.AlongMeters;
        if (result.AcrossOutside) correction += slot.forward * -result.AcrossMeters;
        if (result.HeightOutside) correction += slot.up * -result.HeightMeters;
        if (correction.sqrMagnitude < 0.000001f)
        {
            HideArrow();
            return;
        }
        Vector3 start = candidatePosition + Vector3.up * 0.12f;
        Vector3 end = start + correction.normalized * Mathf.Clamp(correction.magnitude, 0.2f, 0.8f);
        outline.TryGetComponent(out LineRenderer outlineLine);
        ShowArrow(start, end, renderLayer, outlineLine != null ? outlineLine.sharedMaterial : null,
            aimCamera.transform.forward);
    }

    public void Clear(string message = null)
    {
        ClearOutlineRequest();
        HideArrow();
        label = message ?? string.Empty;
        lastFailure = BridgeDeckPanelPlacementFailure.SectionNotConfigured;
        lastAlongCentimeters = lastAcrossCentimeters = lastHeightCentimeters = lastAngleDegrees = int.MinValue;
        lastHorizontalOutside = lastHeightOutside = lastOrientationOutside = false;
        lastAlongOutside = lastAcrossOutside = false;
    }

    public void Dispose()
    {
        Clear();
        if (arrowObject != null) Object.Destroy(arrowObject);
        arrowObject = null;
        arrow = null;
        arrowProperties = null;
    }

    private void UpdateLabel(BridgeDeckPanelPlacementAssessment result, int alongCm, int acrossCm,
        int heightCm, int angleDegrees)
    {
        if (result.Failure == lastFailure && alongCm == lastAlongCentimeters
            && acrossCm == lastAcrossCentimeters && heightCm == lastHeightCentimeters
            && angleDegrees == lastAngleDegrees && result.HorizontalOutside == lastHorizontalOutside
            && result.AlongOutside == lastAlongOutside && result.AcrossOutside == lastAcrossOutside
            && result.HeightOutside == lastHeightOutside && result.OrientationOutside == lastOrientationOutside) return;
        lastFailure = result.Failure;
        lastAlongCentimeters = alongCm;
        lastAcrossCentimeters = acrossCm;
        lastHeightCentimeters = heightCm;
        lastAngleDegrees = angleDegrees;
        lastHorizontalOutside = result.HorizontalOutside;
        lastAlongOutside = result.AlongOutside;
        lastAcrossOutside = result.AcrossOutside;
        lastHeightOutside = result.HeightOutside;
        lastOrientationOutside = result.OrientationOutside;

        if (result.Failure == BridgeDeckPanelPlacementFailure.SlotOccupied)
            label = "Mount: Slot occupied";
        else if (result.Failure == BridgeDeckPanelPlacementFailure.NeighborPenetration)
            label = "Mount: Adjacent overlap";
        else if (result.HorizontalOutside || result.HeightOutside || result.OrientationOutside)
        {
            string correction = string.Empty;
            if (result.AlongOutside) correction += $"Along {(alongCm > 0 ? "+" : string.Empty)}{alongCm}cm";
            if (result.AcrossOutside) correction += $"{(correction.Length > 0 ? " " : string.Empty)}Across {(acrossCm > 0 ? "+" : string.Empty)}{acrossCm}cm";
            if (result.HeightOutside) correction += $"{(correction.Length > 0 ? " " : string.Empty)}Height {(heightCm > 0 ? "+" : string.Empty)}{heightCm}cm";
            if (result.OrientationOutside) correction += $"{(correction.Length > 0 ? " " : string.Empty)}Angle {angleDegrees}°";
            label = $"Mount: {correction}";
        }
        else label = "Mount: Aligned";
    }

    private void ShowArrow(Vector3 start, Vector3 end, int renderLayer, Material sharedMaterial,
        Vector3 cameraForward)
    {
        if (sharedMaterial == null) { HideArrow(); return; }
        if (arrowObject == null)
        {
            arrowObject = new GameObject("Local deck panel correction guide");
            arrow = arrowObject.AddComponent<LineRenderer>();
            arrow.useWorldSpace = true;
            arrow.loop = false;
            arrow.positionCount = 5;
            arrow.startWidth = 0.018f;
            arrow.endWidth = 0.018f;
            arrow.shadowCastingMode = ShadowCastingMode.Off;
            arrow.receiveShadows = false;
            arrow.lightProbeUsage = LightProbeUsage.Off;
            arrow.reflectionProbeUsage = ReflectionProbeUsage.Off;
            arrowProperties = new MaterialPropertyBlock();
        }
        arrowObject.layer = renderLayer;
        arrow.sharedMaterial = sharedMaterial;
        Vector3 direction = (end - start).normalized;
        Vector3 side = Vector3.Cross(direction, cameraForward);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(direction, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(direction, Vector3.right);
        side.Normalize();
        Vector3 wing = end - direction * 0.12f;
        arrow.SetPosition(0, start);
        arrow.SetPosition(1, end);
        arrow.SetPosition(2, wing + side * 0.07f);
        arrow.SetPosition(3, end);
        arrow.SetPosition(4, wing - side * 0.07f);
        float materialAlpha = sharedMaterial.HasProperty(BaseColorProperty)
            ? sharedMaterial.GetColor(BaseColorProperty).a
            : sharedMaterial.HasProperty(LegacyColorProperty) ? sharedMaterial.GetColor(LegacyColorProperty).a : 1f;
        Color color = new(1f, 0.62f, 0.12f, materialAlpha);
        arrow.startColor = color;
        arrow.endColor = color;
        arrow.GetPropertyBlock(arrowProperties);
        arrowProperties.SetColor(BaseColorProperty, color);
        arrowProperties.SetColor(LegacyColorProperty, color);
        arrow.SetPropertyBlock(arrowProperties);
        arrow.enabled = true;
        if (!arrowObject.activeSelf) arrowObject.SetActive(true);
    }

    private void HideArrow()
    {
        if (arrow != null) arrow.enabled = false;
        if (arrowObject != null && arrowObject.activeSelf) arrowObject.SetActive(false);
    }

    private void ClearOutlineRequest()
    {
        if (outline != null) outline.ClearPlacementFeedback(owner);
        outline = null;
    }
}
