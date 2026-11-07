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

    public string Label => label;

    public BridgeDeckPanelPlacementGuidance(Object owner) => this.owner = owner;

    public void Refresh(BridgeDeckPanelPlacementAssessment result, Vector3 candidatePosition,
        Camera aimCamera, int renderLayer, Vector3 worldCorrection)
    {
        if (outline != result.Outline)
        {
            ClearOutlineRequest();
            outline = result.Outline;
        }
        if (outline != null)
            outline.SetPlacementFeedback(owner, result.Failure == BridgeDeckPanelPlacementFailure.None);

        if (!result.HorizontalOutside && !result.HeightOutside
            || outline == null || aimCamera == null || renderLayer < 0)
        {
            HideArrow();
            return;
        }

        if (outline.transform.parent == null) { HideArrow(); return; }
        if (worldCorrection.sqrMagnitude < 0.000001f)
        {
            HideArrow();
            return;
        }
        Vector3 start = candidatePosition + Vector3.up * 0.12f;
        Vector3 end = start + worldCorrection.normalized * Mathf.Clamp(worldCorrection.magnitude, 0.2f, 0.8f);
        outline.TryGetComponent(out LineRenderer outlineLine);
        ShowArrow(start, end, renderLayer, outlineLine != null ? outlineLine.sharedMaterial : null,
            aimCamera.transform.forward);
    }

    public void SetMessage(string message)
    {
        ClearOutlineRequest();
        HideArrow();
        label = message ?? string.Empty;
    }

    public void Clear(string message = null)
    {
        ClearOutlineRequest();
        HideArrow();
        label = message ?? string.Empty;
    }

    public void Dispose()
    {
        Clear();
        if (arrowObject != null) Object.Destroy(arrowObject);
        arrowObject = null;
        arrow = null;
        arrowProperties = null;
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
