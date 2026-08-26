using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BridgeLevelingAdjustmentPointDefinition
{
    public int pointInstanceId = -1;
    public Vector3 localPosition;
    public Vector3 localEuler;
    public Vector3 colliderSize = new Vector3(0.7f, 0.35f, 0.7f);
    [Header("Physical visual")]
    public bool createPhysicalVisual = true;
    public Vector3 visualLocalPosition = new Vector3(0f, -0.085f, 0f);
    public Vector3 visualLocalEuler;
    public Vector3 visualSize = new Vector3(0.65f, 0.18f, 0.65f);
    public Material visualMaterial;
    [Header("Interaction marker")]
    public Color markerColor = new Color(1f, 0.48f, 0.08f, 0.58f);
    public Color targetedColor = new Color(1f, 0.72f, 0.16f, 0.95f);
    [Min(0.002f)] public float markerLineWidth = 0.018f;
    [Min(0.02f)] public float markerMinimumRadius = 0.1f;
}

[Serializable]
public sealed class SpiritLevelMeasurementPointDefinition
{
    public int pointId = -1;
    public Vector3 localPosition;
    public Vector3 localEuler;
    public Vector3 colliderSize = new Vector3(0.9f, 0.18f, 0.35f);
    public Vector3 measurementPoseLocalPosition = new Vector3(0f, 0.08f, 0f);
    public Vector3 measurementPoseLocalEuler;
    public Vector3 positiveTiltLocalDirection = Vector3.right;
    [Range(-1f, 1f)] public float fallbackViewSign = 1f;
    public Vector3 markerLocalCenter = new Vector3(0f, 0.015f, 0f);
    public Vector2 markerSize = new Vector2(0.9f, 0.28f);
}

[DisallowMultipleComponent]
public sealed class BridgeLevelingPointLayout : MonoBehaviour
{
    [Header("Attachment")]
    [SerializeField] private Transform pointAttachmentRoot;
    [Header("Point definitions")]
    [SerializeField] private List<BridgeLevelingAdjustmentPointDefinition> lengthIncrease = new();
    [SerializeField] private List<BridgeLevelingAdjustmentPointDefinition> lengthDecrease = new();
    [SerializeField] private List<BridgeLevelingAdjustmentPointDefinition> widthIncrease = new();
    [SerializeField] private List<BridgeLevelingAdjustmentPointDefinition> widthDecrease = new();
    [SerializeField] private List<SpiritLevelMeasurementPointDefinition> lengthMeasurements = new();
    [SerializeField] private List<SpiritLevelMeasurementPointDefinition> widthMeasurements = new();
    [SerializeField, HideInInspector] private Transform generatedRoot;

    public IReadOnlyList<BridgeLevelingAdjustmentPointDefinition> LengthIncrease => lengthIncrease;
    public IReadOnlyList<BridgeLevelingAdjustmentPointDefinition> LengthDecrease => lengthDecrease;
    public IReadOnlyList<BridgeLevelingAdjustmentPointDefinition> WidthIncrease => widthIncrease;
    public IReadOnlyList<BridgeLevelingAdjustmentPointDefinition> WidthDecrease => widthDecrease;
    public IReadOnlyList<SpiritLevelMeasurementPointDefinition> LengthMeasurements => lengthMeasurements;
    public IReadOnlyList<SpiritLevelMeasurementPointDefinition> WidthMeasurements => widthMeasurements;
    public Transform PointAttachmentRoot => pointAttachmentRoot;
    public Transform GeneratedRoot => generatedRoot;

#if UNITY_EDITOR
    public List<BridgeLevelingAdjustmentPointDefinition> GetAdjustmentDefinitions(BridgeLevelingAdjustmentRole role) => role switch
    {
        BridgeLevelingAdjustmentRole.LengthIncrease => lengthIncrease,
        BridgeLevelingAdjustmentRole.LengthDecrease => lengthDecrease,
        BridgeLevelingAdjustmentRole.WidthIncrease => widthIncrease,
        _ => widthDecrease
    };

    public List<SpiritLevelMeasurementPointDefinition> GetMeasurementDefinitions(SpiritLevelMeasurementAxis axis) =>
        axis == SpiritLevelMeasurementAxis.Length ? lengthMeasurements : widthMeasurements;

    public void SetGeneratedRoot(Transform value) => generatedRoot = value;

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previous = Gizmos.matrix;
        DrawAdjustmentGizmos(lengthIncrease, new Color(1f, 0.75f, 0.1f), "L+");
        DrawAdjustmentGizmos(lengthDecrease, new Color(1f, 0.45f, 0.05f), "L-");
        DrawAdjustmentGizmos(widthIncrease, new Color(0.3f, 1f, 0.35f), "W+");
        DrawAdjustmentGizmos(widthDecrease, new Color(0.1f, 0.7f, 0.25f), "W-");
        DrawMeasurementGizmos(lengthMeasurements, new Color(0.95f, 0.2f, 0.2f), "Length");
        DrawMeasurementGizmos(widthMeasurements, new Color(0.15f, 0.65f, 1f), "Width");
        Gizmos.matrix = previous;
    }

    private void DrawAdjustmentGizmos(IEnumerable<BridgeLevelingAdjustmentPointDefinition> definitions, Color color, string label)
    {
        foreach (BridgeLevelingAdjustmentPointDefinition definition in definitions)
            DrawGizmo(definition.localPosition, definition.localEuler, definition.colliderSize, color,
                $"{label} #{definition.pointInstanceId}");
    }

    private void DrawMeasurementGizmos(IEnumerable<SpiritLevelMeasurementPointDefinition> definitions, Color color, string label)
    {
        foreach (SpiritLevelMeasurementPointDefinition definition in definitions)
            DrawGizmo(definition.localPosition, definition.localEuler, definition.colliderSize, color,
                $"{label} #{definition.pointId}");
    }

    private void DrawGizmo(Vector3 position, Vector3 euler, Vector3 size, Color color, string label)
    {
        Transform pointSpace = pointAttachmentRoot != null ? pointAttachmentRoot : transform;
        Gizmos.matrix = pointSpace.localToWorldMatrix * Matrix4x4.TRS(position, Quaternion.Euler(euler), Vector3.one);
        Gizmos.color = color;
        Gizmos.DrawWireCube(Vector3.zero, size);
        UnityEditor.Handles.Label(pointSpace.TransformPoint(position), label);
    }
#endif
}
