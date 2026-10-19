using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class BridgeDeckPanelSlotOutline : MonoBehaviour
{
    private struct PlacementFeedbackRequest
    {
        public Object Owner;
        public bool IsValid;
    }

    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorProperty = Shader.PropertyToID("_Color");
    private static readonly Color ValidPlacementColor = new(0.18f, 0.95f, 0.25f, 1f);
    private static readonly Color InvalidPlacementColor = new(1f, 0.55f, 0.1f, 1f);

    [SerializeField] private LineRenderer outline;
    [SerializeField] private Vector3 panelDimensions = new(2.16f, 0.2f, 2.4f);
    [SerializeField, Min(0.001f)] private float lineWidth = 0.015f;
    [SerializeField] private Color outlineColor = Color.white;
    private BridgeDeckPanelConstructionSite site;
    private bool lastVisible;
    private bool hasAppliedFeedbackColor;
    private Color lastAppliedFeedbackColor;
    private MaterialPropertyBlock feedbackPropertyBlock;
    private readonly List<PlacementFeedbackRequest> placementFeedbackRequests = new(2);

    private void Awake()
    {
        site = GetComponentInParent<BridgeDeckPanelConstructionSite>();
        Configure();
    }
    private void OnEnable() { Configure(); RefreshFeedbackColor(); RefreshVisibility(); }
    private void OnDisable()
    {
        placementFeedbackRequests.Clear();
        hasAppliedFeedbackColor = false;
        SetSegmentsEnabled(false);
        ApplyFeedbackColor(outlineColor);
    }
    private void OnDestroy()
    {
        placementFeedbackRequests.Clear();
        feedbackPropertyBlock = null;
    }
    private void OnValidate() { Configure(); hasAppliedFeedbackColor = false; RefreshFeedbackColor(); }

    private void Update()
    {
        RefreshVisibility();
        // Prune owners that were destroyed without an explicit focus-clear callback.
        RefreshFeedbackColor();
    }

    private void RefreshVisibility()
    {
        if (outline == null) return;
        BridgeComponent component = site != null ? site.GetComponent<BridgeComponent>() : null;
        bool debugCompleted = component != null && component.StartFullyCompleted;
        bool visible = site != null && site.UsesPhysicalPanelPlacement && !site.IsComplete && !debugCompleted
            && (site.DeckSection == null || site.DeckSection.PhysicalPanelState == null
                || !site.DeckSection.PhysicalPanelState.IsSlotComplete(site.SlotIndex));
        if (lastVisible == visible)
        {
            if (outline == null || outline.enabled == visible) return;
        }
        lastVisible = visible;
        SetSegmentsEnabled(visible);
    }

    private void Configure()
    {
        if (outline == null) return;
        LineRenderer line = outline;
        if (line == null) return;
        float halfX = panelDimensions.x * 0.5f;
        float halfZ = panelDimensions.z * 0.5f;
        Vector3 a = new(-halfX, 0.008f, -halfZ), b = new(halfX, 0.008f, -halfZ);
        Vector3 c = new(halfX, 0.008f, halfZ), d = new(-halfX, 0.008f, halfZ);
        float top = panelDimensions.y + 0.008f;
        Vector3 au = new(-halfX, top, -halfZ), bu = new(halfX, top, -halfZ);
        Vector3 cu = new(halfX, top, halfZ), du = new(-halfX, top, halfZ);
        // A continuous edge walk covers the two rings and four uprights; three uprights
        // are retraced so every adjacent point pair lies on a cuboid edge.
        Vector3[] points = { a, b, c, d, a, au, bu, b, bu, cu, c, cu, du, d, du, au, a };
        line.useWorldSpace = false;
        line.loop = false;
        line.positionCount = points.Length;
        for (int i = 0; i < points.Length; i++) line.SetPosition(i, points[i]);
        line.widthMultiplier = lineWidth;
        line.widthCurve = CreateFrameWidthCurve(points);
        line.startColor = outlineColor;
        line.endColor = outlineColor;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        line.textureMode = LineTextureMode.Stretch;
        line.generateLightingData = false;
    }

    private static AnimationCurve CreateFrameWidthCurve(Vector3[] points)
    {
        bool[] lowerCorners = { true, true, true, true, true, false, false, true, false,
            false, true, false, false, true, false, false, true };
        float totalLength = 0f;
        float[] distances = new float[points.Length];
        for (int i = 1; i < points.Length; i++)
        {
            totalLength += Vector3.Distance(points[i - 1], points[i]);
            distances[i] = totalLength;
        }
        Keyframe[] keys = new Keyframe[lowerCorners.Length];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = new Keyframe(totalLength > 0f ? distances[i] / totalLength : 0f,
                lowerCorners[i] ? 1.3f : 1f);
        return new AnimationCurve(keys);
    }

    private void SetSegmentsEnabled(bool visible)
    {
        if (outline != null) outline.enabled = visible;
    }

    public void SetPlacementFeedback(Object owner, bool placementValid)
    {
        if (ReferenceEquals(owner, null) || !isActiveAndEnabled) return;
        for (int i = placementFeedbackRequests.Count - 1; i >= 0; i--)
        {
            if (placementFeedbackRequests[i].Owner == null)
            {
                placementFeedbackRequests.RemoveAt(i);
                continue;
            }
            if (!ReferenceEquals(placementFeedbackRequests[i].Owner, owner)) continue;
            if (placementFeedbackRequests[i].IsValid == placementValid) return;
            PlacementFeedbackRequest updated = placementFeedbackRequests[i];
            updated.IsValid = placementValid;
            placementFeedbackRequests[i] = updated;
            RefreshFeedbackColor();
            return;
        }
        placementFeedbackRequests.Add(new PlacementFeedbackRequest { Owner = owner, IsValid = placementValid });
        RefreshFeedbackColor();
    }

    public void ClearPlacementFeedback(Object owner)
    {
        if (ReferenceEquals(owner, null)) return;
        bool changed = false;
        for (int i = placementFeedbackRequests.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(placementFeedbackRequests[i].Owner, owner) || placementFeedbackRequests[i].Owner == null)
            {
                placementFeedbackRequests.RemoveAt(i);
                changed = true;
            }
        }
        if (changed) RefreshFeedbackColor();
    }

    private void RefreshFeedbackColor()
    {
        bool hasRequest = false;
        bool hasInvalidRequest = false;
        for (int i = placementFeedbackRequests.Count - 1; i >= 0; i--)
        {
            PlacementFeedbackRequest request = placementFeedbackRequests[i];
            if (request.Owner == null)
            {
                placementFeedbackRequests.RemoveAt(i);
                continue;
            }
            hasRequest = true;
            hasInvalidRequest |= !request.IsValid;
        }

        Color color = !hasRequest ? outlineColor : hasInvalidRequest ? InvalidPlacementColor : ValidPlacementColor;
        if (hasAppliedFeedbackColor && lastAppliedFeedbackColor == color) return;
        ApplyFeedbackColor(color);
        lastAppliedFeedbackColor = color;
        hasAppliedFeedbackColor = true;
    }

    private void ApplyFeedbackColor(Color color)
    {
        if (outline == null) return;
        feedbackPropertyBlock ??= new MaterialPropertyBlock();
        LineRenderer line = outline;
        line.startColor = color;
        line.endColor = color;

        // URP/Lit ignores LineRenderer vertex colors. Tint the authored shared
        // renderer through its property block while retaining authored transparency.
        float alpha = color.a;
        Material shared = line.sharedMaterial;
        if (shared != null && shared.HasProperty(BaseColorProperty)) alpha *= shared.GetColor(BaseColorProperty).a;
        else if (shared != null && shared.HasProperty(LegacyColorProperty)) alpha *= shared.GetColor(LegacyColorProperty).a;
        Color renderedColor = new(color.r, color.g, color.b, alpha);
        line.GetPropertyBlock(feedbackPropertyBlock);
        feedbackPropertyBlock.SetColor(BaseColorProperty, renderedColor);
        feedbackPropertyBlock.SetColor(LegacyColorProperty, renderedColor);
        line.SetPropertyBlock(feedbackPropertyBlock);
    }
}
