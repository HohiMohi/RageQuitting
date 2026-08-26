using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BridgeLevelingPointLayout))]
public sealed class BridgeLevelingPointLayoutEditor : Editor
{
    private const string DefaultAdjustmentMaterialPath = "Assets/Materials/WoodPiece_material.mat";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("pointAttachmentRoot"),
            new GUIContent("Point Attachment Root"));
        EditorGUILayout.Space();
        DrawList("lengthIncrease", "Length Increase");
        DrawList("lengthDecrease", "Length Decrease");
        DrawList("widthIncrease", "Width Increase");
        DrawList("widthDecrease", "Width Decrease");
        DrawList("lengthMeasurements", "Length Measurements");
        DrawList("widthMeasurements", "Width Measurements");
        bool changed = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (GUILayout.Button("Rebuild Points") || changed)
        {
            Rebuild((BridgeLevelingPointLayout)target, true);
        }
        if (GUILayout.Button("Validate Layout"))
        {
            ValidateAndLog((BridgeLevelingPointLayout)target);
        }
    }

    private void DrawList(string propertyName, string label)
    {
        EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName), new GUIContent(label), true);
    }

    public static void Rebuild(BridgeLevelingPointLayout layout, bool registerUndo)
    {
        if (layout == null) return;
        EnsureStableIds(layout);
        BridgeConstructionSite site = layout.GetComponent<BridgeConstructionSite>();
        if (site == null)
        {
            Debug.LogError("BridgeLevelingPointLayout requires a BridgeConstructionSite on the same GameObject.", layout);
            return;
        }

        Transform oldRoot = layout.GeneratedRoot != null ? layout.GeneratedRoot : layout.transform.Find("GeneratedLevelingPoints");
        if (oldRoot != null)
        {
            if (registerUndo) Undo.DestroyObjectImmediate(oldRoot.gameObject);
            else Object.DestroyImmediate(oldRoot.gameObject);
        }

        Transform attachmentRoot = layout.PointAttachmentRoot != null ? layout.PointAttachmentRoot : layout.transform;
        if (layout.PointAttachmentRoot == null)
        {
            Debug.LogWarning("Bridge leveling points have no Point Attachment Root. Falling back to the layout transform.", layout);
        }
        else if (!layout.PointAttachmentRoot.IsChildOf(layout.transform) && layout.PointAttachmentRoot != layout.transform)
        {
            Debug.LogWarning("Point Attachment Root must belong to the same bridge component hierarchy.", layout);
            attachmentRoot = layout.transform;
        }

        GameObject rootObject = new GameObject("GeneratedLevelingPoints");
        if (registerUndo) Undo.RegisterCreatedObjectUndo(rootObject, "Rebuild leveling points");
        rootObject.transform.SetParent(attachmentRoot, false);
        layout.SetGeneratedRoot(rootObject.transform);

        BuildAdjustmentGroup(layout, rootObject.transform, BridgeLevelingAdjustmentRole.LengthIncrease);
        BuildAdjustmentGroup(layout, rootObject.transform, BridgeLevelingAdjustmentRole.LengthDecrease);
        BuildAdjustmentGroup(layout, rootObject.transform, BridgeLevelingAdjustmentRole.WidthIncrease);
        BuildAdjustmentGroup(layout, rootObject.transform, BridgeLevelingAdjustmentRole.WidthDecrease);
        BuildMeasurementGroup(layout, site, rootObject.transform, SpiritLevelMeasurementAxis.Length);
        BuildMeasurementGroup(layout, site, rootObject.transform, SpiritLevelMeasurementAxis.Width);

        EditorUtility.SetDirty(layout);
        PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
        ValidateAndLog(layout, false);
    }

    private static void BuildAdjustmentGroup(BridgeLevelingPointLayout layout, Transform root, BridgeLevelingAdjustmentRole role)
    {
        int index = 0;
        foreach (BridgeLevelingAdjustmentPointDefinition definition in layout.GetAdjustmentDefinitions(role))
        {
            GameObject pointObject = CreatePointObject($"{role}_{index++}", root, definition.localPosition,
                definition.localEuler, definition.colliderSize);
            BuildAdjustmentVisual(pointObject.transform, definition);
            BridgeLevelingAdjustmentPointVisualizer visualizer = pointObject.AddComponent<BridgeLevelingAdjustmentPointVisualizer>();
            visualizer.ConfigureEditor(definition.markerColor, definition.targetedColor,
                definition.markerLineWidth, definition.markerMinimumRadius);
            if (layout.TryGetComponent(out BridgeGirderConstructionSite _))
            {
                pointObject.AddComponent<BridgeGirderWorkPoint>()
                    .ConfigureLevelingPointEditor(definition.pointInstanceId, role);
            }
            else
            {
                pointObject.AddComponent<BridgeAbutmentWorkPoint>()
                    .ConfigureLevelingPointEditor(definition.pointInstanceId, role);
            }
        }
    }

    private static void BuildAdjustmentVisual(Transform pointRoot, BridgeLevelingAdjustmentPointDefinition definition)
    {
        if (!definition.createPhysicalVisual)
        {
            return;
        }

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "AdjustmentPointVisual";
        visual.layer = LayerMask.NameToLayer("Ignore Raycast");
        visual.transform.SetParent(pointRoot, false);
        visual.transform.localPosition = definition.visualLocalPosition;
        visual.transform.localEulerAngles = definition.visualLocalEuler;
        visual.transform.localScale = new Vector3(
            Mathf.Max(0.02f, definition.visualSize.x),
            Mathf.Max(0.02f, definition.visualSize.y),
            Mathf.Max(0.02f, definition.visualSize.z));

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            Object.DestroyImmediate(visualCollider);
        }

        Material material = definition.visualMaterial != null
            ? definition.visualMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultAdjustmentMaterialPath);
        MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static void BuildMeasurementGroup(BridgeLevelingPointLayout layout, BridgeConstructionSite site,
        Transform root, SpiritLevelMeasurementAxis axis)
    {
        int index = 0;
        foreach (SpiritLevelMeasurementPointDefinition definition in layout.GetMeasurementDefinitions(axis))
        {
            GameObject pointObject = CreatePointObject($"SpiritLevel_{axis}_{index++}", root,
                definition.localPosition, definition.localEuler, definition.colliderSize);
            Transform pose = new GameObject("MeasurementPose").transform;
            pose.SetParent(pointObject.transform, false);
            pose.localPosition = definition.measurementPoseLocalPosition;
            pose.localEulerAngles = definition.measurementPoseLocalEuler;
            SpiritLevelMeasurementPoint point = pointObject.AddComponent<SpiritLevelMeasurementPoint>();
            point.ConfigureEditor(site, definition.pointId, axis, pose, definition.positiveTiltLocalDirection,
                definition.fallbackViewSign, definition.markerLocalCenter, definition.markerSize);
            pointObject.AddComponent<SpiritLevelMeasurementPointVisualizer>();
        }
    }

    private static GameObject CreatePointObject(string name, Transform root, Vector3 position, Vector3 euler, Vector3 size)
    {
        GameObject point = new GameObject(name);
        point.transform.SetParent(root, false);
        point.transform.localPosition = position;
        point.transform.localEulerAngles = euler;
        BoxCollider collider = point.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(Mathf.Max(0.02f, size.x), Mathf.Max(0.02f, size.y), Mathf.Max(0.02f, size.z));
        return point;
    }

    private static void EnsureStableIds(BridgeLevelingPointLayout layout)
    {
        HashSet<int> adjustmentIds = new();
        int nextAdjustmentId = 1000;
        foreach (BridgeLevelingAdjustmentRole role in System.Enum.GetValues(typeof(BridgeLevelingAdjustmentRole)))
        foreach (BridgeLevelingAdjustmentPointDefinition definition in layout.GetAdjustmentDefinitions(role))
        {
            if (definition.pointInstanceId < 1000 || !adjustmentIds.Add(definition.pointInstanceId))
            {
                while (!adjustmentIds.Add(nextAdjustmentId)) nextAdjustmentId++;
                definition.pointInstanceId = nextAdjustmentId++;
            }
        }

        HashSet<int> measurementIds = new();
        int nextMeasurementId = 0;
        foreach (SpiritLevelMeasurementAxis axis in System.Enum.GetValues(typeof(SpiritLevelMeasurementAxis)))
        foreach (SpiritLevelMeasurementPointDefinition definition in layout.GetMeasurementDefinitions(axis))
        {
            if (definition.pointId < 0 || !measurementIds.Add(definition.pointId))
            {
                while (!measurementIds.Add(nextMeasurementId)) nextMeasurementId++;
                definition.pointId = nextMeasurementId++;
            }
        }
    }

    private static void ValidateAndLog(BridgeLevelingPointLayout layout, bool logSuccess = true)
    {
        List<string> issues = new();
        Transform attachmentRoot = layout.PointAttachmentRoot;
        if (attachmentRoot == null)
        {
            issues.Add("Point Attachment Root is not assigned.");
        }
        else if (!attachmentRoot.IsChildOf(layout.transform) && attachmentRoot != layout.transform)
        {
            issues.Add("Point Attachment Root does not belong to this bridge component hierarchy.");
        }
        HashSet<int> adjustmentIds = new();
        HashSet<int> measurementIds = new();
        List<(string name, Bounds bounds)> volumes = new();
        foreach (BridgeLevelingAdjustmentRole role in System.Enum.GetValues(typeof(BridgeLevelingAdjustmentRole)))
        {
            List<BridgeLevelingAdjustmentPointDefinition> definitions = layout.GetAdjustmentDefinitions(role);
            if (definitions.Count == 0) issues.Add($"Missing {role} point.");
            for (int i = 0; i < definitions.Count; i++)
            {
                BridgeLevelingAdjustmentPointDefinition definition = definitions[i];
                if (!adjustmentIds.Add(definition.pointInstanceId))
                    issues.Add($"Duplicate adjustment ID {definition.pointInstanceId}.");
                volumes.Add(($"{role}[{i}]", new Bounds(definition.localPosition, definition.colliderSize)));
            }
        }
        foreach (SpiritLevelMeasurementAxis axis in System.Enum.GetValues(typeof(SpiritLevelMeasurementAxis)))
        {
            List<SpiritLevelMeasurementPointDefinition> definitions = layout.GetMeasurementDefinitions(axis);
            if (definitions.Count == 0) issues.Add($"Missing {axis} measurement point.");
            for (int i = 0; i < definitions.Count; i++)
            {
                SpiritLevelMeasurementPointDefinition definition = definitions[i];
                if (!measurementIds.Add(definition.pointId))
                    issues.Add($"Duplicate measurement ID {definition.pointId}.");
                volumes.Add(($"{axis}Measurement[{i}]", new Bounds(definition.localPosition, definition.colliderSize)));
            }
        }
        for (int i = 0; i < volumes.Count; i++)
        for (int j = i + 1; j < volumes.Count; j++)
        {
            if (volumes[i].bounds.Intersects(volumes[j].bounds))
                issues.Add($"Point volumes overlap: {volumes[i].name} and {volumes[j].name}.");
        }
        if (issues.Count > 0) Debug.LogWarning(string.Join("\n", issues), layout);
        else if (logSuccess) Debug.Log("Bridge leveling point layout is valid.", layout);
    }
}
