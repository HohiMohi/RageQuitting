using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class LooseSoilStage2SceneSetup
{
    private const string ExpectedAssetsPath = "D:/Programy/UnityProjects/RageQuitting/Assets";
    private const string StageScenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";

    [CliCommand("rq_setup_loose_soil_stage02", "Configure the two Stage02 excavation volumes for loose-soil V1.", MainThreadRequired = true)]
    public static string ConfigureStage02()
    {
        EnsureEditorSafeToEdit();
        SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
        Scene originalActiveScene = SceneManager.GetActiveScene();
        if (originalActiveScene.path != "Assets/Scenes/MultiplayerStartScene.unity")
            throw new InvalidOperationException("Stage02 setup expects MultiplayerStartScene to be active.");
        if (originalSetup.Any(item => item.path == StageScenePath && item.isLoaded))
            throw new InvalidOperationException("Stage02 must not already be loaded before this setup command.");

        Scene stageScene = default;
        bool openedByCommand = false;
        try
        {
            stageScene = EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Additive);
            openedByCommand = true;
            FoundationExcavationVolume[] volumes = Resources.FindObjectsOfTypeAll<FoundationExcavationVolume>()
                .Where(volume => volume != null && volume.gameObject.scene == stageScene)
                .OrderBy(volume => volume.name, StringComparer.Ordinal)
                .ToArray();
            if (volumes.Length != 2)
                throw new InvalidOperationException($"Expected exactly two Stage02 excavation volumes, found {volumes.Length}.");

            foreach (FoundationExcavationVolume volume in volumes)
                ConfigureVolume(volume);

            foreach (FoundationLooseSoilField field in volumes.Select(volume => volume.LooseSoilField))
                field.ReleaseRuntimeMeshForSceneSave();

            EditorSceneManager.MarkSceneDirty(stageScene);
            if (!EditorSceneManager.SaveScene(stageScene))
                throw new InvalidOperationException("Unity did not save the Stage02 scene.");

            EditorSceneManager.CloseScene(stageScene, true);
            openedByCommand = false;
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            stageScene = EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Additive);
            openedByCommand = true;
            VerifySavedScene(stageScene);
            return "Configured and saved exactly two loose-soil excavation volumes in Stage02.";
        }
        finally
        {
            if (openedByCommand)
            {
                Scene target = SceneManager.GetSceneByPath(StageScenePath);
                if (target.IsValid() && target.isLoaded) EditorSceneManager.CloseScene(target, true);
            }
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }
    }

    private static void EnsureEditorSafeToEdit()
    {
        string actualAssetsPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
        if (!string.Equals(actualAssetsPath, ExpectedAssetsPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Wrong Unity project: {actualAssetsPath}");
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Unity Editor must be idle and outside Play Mode.");
        foreach (SceneSetup setup in EditorSceneManager.GetSceneManagerSetup())
        {
            if (!setup.isLoaded) continue;
            Scene scene = SceneManager.GetSceneByPath(setup.path);
            if (scene.IsValid() && scene.isDirty)
                throw new InvalidOperationException($"Refusing to change scenes while '{setup.path}' has unsaved edits.");
        }
    }

    private static void ConfigureVolume(FoundationExcavationVolume volume)
    {
        SerializedObject volumeData = new SerializedObject(volume);
        BridgeConstructionSite site = volumeData.FindProperty("constructionSite").objectReferenceValue as BridgeConstructionSite;
        BridgeComponent bridgeComponent = site != null ? site.GetComponent<BridgeComponent>() : null;
        BridgeComponentSO definition = bridgeComponent != null ? bridgeComponent.GetBridgeComponentSO() : null;
        BridgeConstructionWorkflowSO workflow = definition != null ? definition.constructionWorkflow : null;
        if (site == null || bridgeComponent == null || workflow == null)
            throw new InvalidOperationException($"'{volume.name}' is missing its serialized construction site, bridge component, or workflow.");

        Transform soilSurface = volumeData.FindProperty("soilSurface").objectReferenceValue as Transform;
        Renderer soilRenderer = volumeData.FindProperty("soilRenderer").objectReferenceValue as Renderer;
        Material looseSoilMaterial = volumeData.FindProperty("loosenedSoilMaterial").objectReferenceValue as Material;
        BoxCollider supportCollider = soilSurface != null ? soilSurface.GetComponent<BoxCollider>() : null;
        if (soilSurface == null || soilRenderer == null || supportCollider == null || looseSoilMaterial == null)
            throw new InvalidOperationException($"'{volume.name}' is missing the existing soil surface Renderer/BoxCollider or loosened-soil material.");

        Transform fieldTransform = volume.transform.Find("LooseSoilSurface");
        if (fieldTransform == null)
        {
            GameObject fieldObject = new GameObject("LooseSoilSurface", typeof(MeshFilter), typeof(MeshRenderer));
            fieldTransform = fieldObject.transform;
            fieldTransform.SetParent(volume.transform, false);
            fieldTransform.localPosition = Vector3.zero;
            fieldTransform.localRotation = Quaternion.identity;
            fieldTransform.localScale = Vector3.one;
        }
        FoundationLooseSoilField field = fieldTransform.GetComponent<FoundationLooseSoilField>();
        if (field == null) field = fieldTransform.gameObject.AddComponent<FoundationLooseSoilField>();
        if (fieldTransform.GetComponent<MeshFilter>().sharedMesh != null)
            throw new InvalidOperationException($"'{volume.name}/LooseSoilSurface' has a mesh reference; refusing to overwrite it.");

        Vector2 footprint = volumeData.FindProperty("concreteFootprintSize").vector2Value;
        float layerDepth = workflow.FinalExcavationDepth / workflow.DiggingCycleCount;
        SerializedObject fieldData = new SerializedObject(field);
        fieldData.FindProperty("footprintWidth").floatValue = footprint.x;
        fieldData.FindProperty("footprintLength").floatValue = footprint.y;
        fieldData.FindProperty("cellSize").floatValue = 0.2f;
        fieldData.FindProperty("layerDepth").floatValue = layerDepth;
        fieldData.FindProperty("initialHeight").floatValue = layerDepth;
        fieldData.FindProperty("unitsPerLayer").floatValue = workflow.SoilUnitsPerCycle;
        fieldData.FindProperty("meshFilter").objectReferenceValue = fieldTransform.GetComponent<MeshFilter>();
        fieldData.FindProperty("meshRenderer").objectReferenceValue = fieldTransform.GetComponent<MeshRenderer>();
        fieldData.ApplyModifiedPropertiesWithoutUndo();

        Vector3 supportTopWorld = supportCollider.transform.TransformPoint(
            supportCollider.center + Vector3.up * (supportCollider.size.y * 0.5f));
        Vector3 origin = volume.transform.InverseTransformPoint(supportTopWorld);
        fieldTransform.localPosition = origin;
        fieldTransform.localRotation = Quaternion.identity;
        fieldTransform.localScale = Vector3.one;
        field.Initialize(footprint.x, footprint.y, 0.2f, layerDepth, layerDepth, workflow.SoilUnitsPerCycle);
        field.SetSurfaceState(false, looseSoilMaterial);

        volumeData.FindProperty("useLooseSoilHeightField").boolValue = true;
        volumeData.FindProperty("looseSoilField").objectReferenceValue = field;
        volumeData.FindProperty("soilSupportCollider").objectReferenceValue = supportCollider;
        volumeData.FindProperty("loosenedSoilMaterial").objectReferenceValue = looseSoilMaterial;
        volumeData.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject bridgeData = new SerializedObject(bridgeComponent);
        bridgeData.FindProperty("startFullyCompleted").boolValue = false;
        bridgeData.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(volume);
        EditorUtility.SetDirty(field);
        EditorUtility.SetDirty(fieldTransform.GetComponent<MeshRenderer>());
        EditorUtility.SetDirty(bridgeComponent);
    }

    private static void VerifySavedScene(Scene stageScene)
    {
        FoundationExcavationVolume[] volumes = Resources.FindObjectsOfTypeAll<FoundationExcavationVolume>()
            .Where(volume => volume != null && volume.gameObject.scene == stageScene)
            .ToArray();
        if (volumes.Length != 2)
            throw new InvalidOperationException($"Reopened Stage02 scene has {volumes.Length} excavation volumes, expected two.");
        foreach (FoundationExcavationVolume volume in volumes)
        {
            if (!volume.UsesLooseSoilHeightField || !volume.HasValidLooseSoilConfiguration ||
                volume.LooseSoilField == null || volume.LooseSoilField.GetComponent<MeshFilter>().sharedMesh != null)
                throw new InvalidOperationException($"Saved scene verification failed for '{volume.name}'.");
            if (volume.LooseSoilField.ConfiguredCellSize != 0.2f)
                throw new InvalidOperationException($"Unexpected cell size on '{volume.name}'.");
            SerializedObject volumeData = new SerializedObject(volume);
            BridgeConstructionSite site = volumeData.FindProperty("constructionSite").objectReferenceValue as BridgeConstructionSite;
            BridgeComponent component = site != null ? site.GetComponent<BridgeComponent>() : null;
            if (component == null || component.StartFullyCompleted)
                throw new InvalidOperationException($"'{volume.name}' must begin incomplete after Stage02 setup.");
        }
    }
}
