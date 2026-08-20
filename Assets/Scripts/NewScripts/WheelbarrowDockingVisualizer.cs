using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(WheelbarrowDockingStation))]
public class WheelbarrowDockingVisualizer : MonoBehaviour
{
    [Header("Visibility")]
    [SerializeField, Min(1f)] private float visibilityDistance = 15f;
    [SerializeField] private Color waitingColor = new Color(1f, 0.72f, 0.08f, 0.75f);
    [SerializeField] private Color readyColor = new Color(0.2f, 1f, 0.35f, 0.9f);

    [Header("Appearance")]
    [SerializeField, Range(0.02f, 1f)] private float ghostOpacity = 0.18f;
    [SerializeField, Min(0.005f)] private float outlineWidth = 0.055f;
    [SerializeField, Min(0.25f)] private float ghostScale = 1.05f;
    [SerializeField, Min(0f)] private float groundPadding = 0.08f;

    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private readonly List<Renderer> ghostRenderers = new List<Renderer>();
    private WheelbarrowDockingStation station;
    private GameObject visualRoot;
    private Material lineMaterial;
    private Material ghostMaterial;

    public float VisibilityDistance => Mathf.Max(1f, visibilityDistance);
    public float GhostOpacity => Mathf.Clamp01(ghostOpacity);
    public float OutlineWidth => Mathf.Max(0.005f, outlineWidth);
    public float GhostScale => Mathf.Max(0.25f, ghostScale);
    public float GroundPadding => Mathf.Max(0f, groundPadding);

    private void Awake()
    {
        station = GetComponent<WheelbarrowDockingStation>();
        BuildVisual();
        SetVisible(false);
    }

    private void LateUpdate()
    {
        station ??= GetComponent<WheelbarrowDockingStation>();
        WheelbarrowController wheelbarrow = ResolveLocalDrivenWheelbarrow();
        bool visible = wheelbarrow != null && station != null && station.IsCompatibleWith(wheelbarrow) &&
            Vector3.Distance(wheelbarrow.transform.position, station.TargetPose.position) <= VisibilityDistance;
        SetVisible(visible);
        if (!visible) return;

        bool ready = station.EvaluateDriverDockingReadiness(wheelbarrow);
        float positionScore = 1f - Mathf.Clamp01(Vector3.Distance(wheelbarrow.transform.position, station.TargetPose.position) / VisibilityDistance);
        float rotationScore = 1f - Mathf.Clamp01(Quaternion.Angle(wheelbarrow.transform.rotation, station.TargetPose.rotation) / 180f);
        float intensity = Mathf.Lerp(0.55f, 1f, (positionScore + rotationScore) * 0.5f);
        Color color = ready ? readyColor : waitingColor;
        color.r *= intensity;
        color.g *= intensity;
        color.b *= intensity;
        ApplyColor(color);
        DrawCaptureOutline();
        DrawDirectionMarker();
    }

    private WheelbarrowController ResolveLocalDrivenWheelbarrow()
    {
        ulong localClientId = 0;
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening) localClientId = manager.LocalClientId;
        WheelbarrowController wheelbarrow = WheelbarrowController.FindForPlayer(localClientId);
        return wheelbarrow != null && wheelbarrow.State == WheelbarrowState.Driven &&
            wheelbarrow.DriverClientId == localClientId
            ? wheelbarrow
            : null;
    }

    private void BuildVisual()
    {
        if (visualRoot != null) return;
        visualRoot = new GameObject("WheelbarrowDockFeedback_Runtime") { hideFlags = HideFlags.DontSave };
        visualRoot.layer = 2;
        visualRoot.transform.SetParent(transform, false);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            lineMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            ghostMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            ConfigureTransparentMaterial(ghostMaterial);
        }

        for (int index = 0; index < 4; index++) lines.Add(CreateLine($"CaptureEdge_{index}"));
        for (int index = 0; index < 3; index++) lines.Add(CreateLine($"Direction_{index}"));
        BuildGhost();
    }

    private void BuildGhost()
    {
        Transform target = station != null && station.TargetPose != null ? station.TargetPose : transform;
        GameObject root = new GameObject("WheelbarrowGhost");
        root.layer = 2;
        root.transform.SetParent(visualRoot.transform, false);
        root.transform.SetPositionAndRotation(target.position, target.rotation);
        root.transform.localScale = Vector3.one * GhostScale;

        CreateGhostPrimitive(root.transform, PrimitiveType.Cube, "Tray", new Vector3(0f, 0.78f, 0.15f), new Vector3(1.35f, 0.42f, 1.5f), Vector3.zero);
        CreateGhostPrimitive(root.transform, PrimitiveType.Cube, "HandleLeft", new Vector3(-0.48f, 0.63f, -1.02f), new Vector3(0.1f, 0.1f, 1.7f), new Vector3(-6f, 0f, 0f));
        CreateGhostPrimitive(root.transform, PrimitiveType.Cube, "HandleRight", new Vector3(0.48f, 0.63f, -1.02f), new Vector3(0.1f, 0.1f, 1.7f), new Vector3(-6f, 0f, 0f));
        CreateGhostPrimitive(root.transform, PrimitiveType.Cylinder, "Wheel", new Vector3(0f, 0.44f, 0.66f), new Vector3(0.88f, 0.17f, 0.88f), new Vector3(0f, 0f, 90f));
    }

    private void CreateGhostPrimitive(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Vector3 rotation)
    {
        GameObject item = GameObject.CreatePrimitive(type);
        item.name = name;
        item.layer = 2;
        item.hideFlags = HideFlags.DontSave;
        item.transform.SetParent(parent, false);
        item.transform.localPosition = position;
        item.transform.localEulerAngles = rotation;
        item.transform.localScale = scale;
        Collider collider = item.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }
        Renderer renderer = item.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = ghostMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ghostRenderers.Add(renderer);
        }
    }

    private LineRenderer CreateLine(string name)
    {
        GameObject item = new GameObject(name) { layer = 2, hideFlags = HideFlags.DontSave };
        item.transform.SetParent(visualRoot.transform, false);
        LineRenderer line = item.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = line.endWidth = OutlineWidth;
        line.sharedMaterial = lineMaterial;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private void DrawCaptureOutline()
    {
        Collider volume = station.CaptureVolume;
        if (!(volume is BoxCollider box)) return;
        Vector3 half = box.size * 0.5f;
        half.x += GroundPadding;
        half.z += GroundPadding;
        Vector3[] corners =
        {
            box.transform.TransformPoint(box.center + new Vector3(-half.x, 0f, -half.z)),
            box.transform.TransformPoint(box.center + new Vector3(half.x, 0f, -half.z)),
            box.transform.TransformPoint(box.center + new Vector3(half.x, 0f, half.z)),
            box.transform.TransformPoint(box.center + new Vector3(-half.x, 0f, half.z))
        };
        float groundY = station.TargetPose.position.y + 0.04f;
        for (int index = 0; index < corners.Length; index++) corners[index].y = groundY;
        for (int index = 0; index < 4; index++)
        {
            lines[index].SetPosition(0, corners[index]);
            lines[index].SetPosition(1, corners[(index + 1) % 4]);
        }
    }

    private void DrawDirectionMarker()
    {
        Transform target = station.TargetPose;
        Vector3 center = target.position + Vector3.up * 0.05f - target.forward * 1.9f;
        Vector3 tip = center - target.forward * 0.75f;
        Vector3 left = center - target.forward * 0.35f - target.right * 0.3f;
        Vector3 right = center - target.forward * 0.35f + target.right * 0.3f;
        SetLine(lines[4], center, tip);
        SetLine(lines[5], tip, left);
        SetLine(lines[6], tip, right);
    }

    private static void SetLine(LineRenderer line, Vector3 start, Vector3 end)
    {
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }

    private void ApplyColor(Color color)
    {
        foreach (LineRenderer line in lines) line.startColor = line.endColor = color;
        Color ghostColor = color;
        ghostColor.a = GhostOpacity * Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(Mathf.Max(color.r, Mathf.Max(color.g, color.b))));
        if (ghostMaterial != null)
        {
            if (ghostMaterial.HasProperty("_BaseColor")) ghostMaterial.SetColor("_BaseColor", ghostColor);
            if (ghostMaterial.HasProperty("_Color")) ghostMaterial.SetColor("_Color", ghostColor);
        }
    }

    private void SetVisible(bool visible)
    {
        if (visualRoot != null && visualRoot.activeSelf != visible) visualRoot.SetActive(visible);
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null) return;
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void OnDestroy()
    {
        if (lineMaterial != null) Destroy(lineMaterial);
        if (ghostMaterial != null) Destroy(ghostMaterial);
        if (visualRoot != null) Destroy(visualRoot);
    }
}
