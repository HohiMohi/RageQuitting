using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(WheelbarrowPourGripInteraction))]
public sealed class WheelbarrowPourStationVisualizer : MonoBehaviour
{
    private readonly List<LineRenderer> gripLines = new List<LineRenderer>(6);
    private readonly List<LineRenderer> footprintLines = new List<LineRenderer>(2);

    private WheelbarrowPourGripInteraction interaction;
    private BoxCollider interactionCollider;
    private Material lineMaterial;

    private void Awake()
    {
        interaction = GetComponent<WheelbarrowPourGripInteraction>();
        interactionCollider = GetComponent<BoxCollider>();
        BuildVisuals();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (lineMaterial != null) Destroy(lineMaterial);
    }

    private void LateUpdate()
    {
        WheelbarrowPouringMinigame minigame = interaction != null ? interaction.Minigame : null;
        ulong localClientId = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
            ? NetworkManager.Singleton.LocalClientId
            : 0;
        bool visible = minigame != null && minigame.ShouldShowJoinStations(localClientId);
        SetVisible(visible);
        if (!visible) return;

        ConcretePouringProfileSO profile = minigame.Profile;
        bool occupied = minigame.IsSideOccupied(interaction.LeftSide);
        bool targeted = interaction.IsLocallyTargeted && !occupied;
        Color color = occupied
            ? profile != null ? profile.OccupiedStationColor : new Color(0.42f, 0.45f, 0.48f, 0.5f)
            : targeted
                ? profile != null ? profile.TargetedStationColor : new Color(0.25f, 1f, 0.55f, 1f)
                : profile != null ? profile.AvailableStationColor : new Color(0.1f, 0.9f, 1f, 0.85f);
        float pulse = targeted
            ? 1f + Mathf.Sin(Time.unscaledTime * (profile != null ? profile.StationMarkerPulseSpeed : 2.5f) * Mathf.PI * 2f) *
                (profile != null ? profile.StationMarkerPulseAmount : 0.08f)
            : 1f;

        UpdateGripOutline(profile, color, pulse);
        UpdateFootprints(minigame, profile, color, pulse);
    }

    private void BuildVisuals()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        lineMaterial = new Material(shader) { name = "WheelbarrowPourStationMarker_Runtime" };
        if (lineMaterial.HasProperty("_Surface")) lineMaterial.SetFloat("_Surface", 1f);
        if (lineMaterial.HasProperty("_ZWrite")) lineMaterial.SetFloat("_ZWrite", 0f);
        lineMaterial.renderQueue = 3000;

        Transform visualRoot = new GameObject("PourStationMarker_Runtime").transform;
        visualRoot.SetParent(transform, false);
        visualRoot.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

        for (int i = 0; i < 6; i++)
            gripLines.Add(CreateLine(visualRoot, $"GripOutline_{i}", true));
        footprintLines.Add(CreateLine(visualRoot, "FootprintLeft", true));
        footprintLines.Add(CreateLine(visualRoot, "FootprintRight", true));
    }

    private LineRenderer CreateLine(Transform parent, string objectName, bool loop)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(parent, false);
        lineObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.sharedMaterial = lineMaterial;
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = 4;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private void UpdateGripOutline(ConcretePouringProfileSO profile, Color color, float pulse)
    {
        if (interactionCollider == null) return;
        float padding = (profile != null ? profile.GripMarkerPadding : 0.05f) * pulse;
        Vector3 half = interactionCollider.size * 0.5f + Vector3.one * padding;
        Vector3 center = interactionCollider.center;
        Vector3[] corners = new Vector3[8];
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 local = center + new Vector3(
                (i & 1) == 0 ? -half.x : half.x,
                (i & 2) == 0 ? -half.y : half.y,
                (i & 4) == 0 ? -half.z : half.z);
            corners[i] = interactionCollider.transform.TransformPoint(local);
        }

        int[,] faces =
        {
            { 0, 1, 3, 2 }, { 4, 5, 7, 6 },
            { 0, 1, 5, 4 }, { 2, 3, 7, 6 },
            { 0, 2, 6, 4 }, { 1, 3, 7, 5 }
        };
        for (int face = 0; face < gripLines.Count; face++)
        {
            LineRenderer line = gripLines[face];
            for (int point = 0; point < 4; point++) line.SetPosition(point, corners[faces[face, point]]);
            ConfigureLine(line, profile, color);
        }
    }

    private void UpdateFootprints(
        WheelbarrowPouringMinigame minigame,
        ConcretePouringProfileSO profile,
        Color color,
        float pulse)
    {
        if (!minigame.TryGetStationMarkerPose(interaction.LeftSide, out Vector3 position, out Quaternion rotation))
            return;

        Vector2 size = profile != null ? profile.StationFootprintSize : new Vector2(0.18f, 0.36f);
        float separation = profile != null ? profile.StationFootprintSeparation : 0.24f;
        Vector3 right = rotation * Vector3.right;
        Vector3 forward = rotation * Vector3.forward;
        for (int foot = 0; foot < footprintLines.Count; foot++)
        {
            float side = foot == 0 ? -1f : 1f;
            Vector3 center = position + right * (separation * 0.5f * side);
            Vector3 halfRight = right * (size.x * 0.5f * pulse);
            Vector3 halfForward = forward * (size.y * 0.5f * pulse);
            LineRenderer line = footprintLines[foot];
            line.SetPosition(0, center - halfRight - halfForward);
            line.SetPosition(1, center + halfRight - halfForward);
            line.SetPosition(2, center + halfRight + halfForward);
            line.SetPosition(3, center - halfRight + halfForward);
            ConfigureLine(line, profile, color);
        }
    }

    private static void ConfigureLine(LineRenderer line, ConcretePouringProfileSO profile, Color color)
    {
        float width = profile != null ? profile.StationMarkerLineWidth : 0.035f;
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
    }

    private void SetVisible(bool visible)
    {
        for (int i = 0; i < gripLines.Count; i++) gripLines[i].enabled = visible;
        for (int i = 0; i < footprintLines.Count; i++) footprintLines[i].enabled = visible;
    }
}
