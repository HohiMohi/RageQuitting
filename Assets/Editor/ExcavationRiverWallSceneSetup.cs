using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Idempotently adds the missing river-facing physical wall to each Stage02 excavation.</summary>
internal static class ExcavationRiverWallSceneSetup
{
    private const string StageScenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";

    [CliCommand("rq_add_stage02_excavation_river_walls", "Add solid river-side end walls to both Stage02 excavation pits.", MainThreadRequired = true)]
    public static string AddWalls()
    {
        Scene originalActive = SceneManager.GetActiveScene();
        SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
        if (originalActive.path != "Assets/Scenes/MultiplayerStartScene.unity")
            throw new InvalidOperationException("Stage02 authoring expects MultiplayerStartScene active.");
        if (originalSetup.Any(item => item.path == StageScenePath && item.isLoaded))
            throw new InvalidOperationException("Stage02 must not already be loaded.");
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Unity Editor must be idle and outside Play Mode.");
        foreach (SceneSetup setup in originalSetup)
        {
            Scene loaded = SceneManager.GetSceneByPath(setup.path);
            if (setup.isLoaded && loaded.IsValid() && loaded.isDirty)
                throw new InvalidOperationException("Refusing Stage02 authoring while another loaded scene has unsaved edits: " + setup.path);
        }

        Scene stage = default;
        bool openedStage = false;
        try
        {
            stage = EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Additive);
            openedStage = true;
            if (!SceneManager.SetActiveScene(stage)) throw new InvalidOperationException("Could not make Stage02 active for authoring.");
            FoundationExcavationVolume[] volumes = Resources.FindObjectsOfTypeAll<FoundationExcavationVolume>()
                .Where(v => v != null && v.gameObject.scene == stage)
                .OrderBy(v => v.transform.position.x)
                .ToArray();
            if (volumes.Length != 2) throw new InvalidOperationException("Expected two Stage02 excavation volumes.");

            Dictionary<string, string> existingIdentities = CaptureIdentities(stage);
            var createdWalls = new List<GameObject>();
            foreach (FoundationExcavationVolume volume in volumes)
            {
                GameObject created = EnsureWall(volume);
                if (created != null) createdWalls.Add(created);
            }

            if (createdWalls.Count > 0)
            {
                ReleaseRuntimeMeshes(volumes);
                EditorSceneManager.MarkSceneDirty(stage);
                if (!EditorSceneManager.SaveScene(stage)) throw new InvalidOperationException("Unity did not save Stage02.");
                foreach (GameObject wall in createdWalls) EnsureStableIdentity(wall);
                EditorSceneManager.MarkSceneDirty(stage);
                if (!EditorSceneManager.SaveScene(stage)) throw new InvalidOperationException("Unity did not save Stage02 wall identities.");
            }
            EditorSceneManager.CloseScene(stage, true);
            openedStage = false;
            stage = default;
            stage = EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Additive);
            openedStage = true;
            Verify(stage);
            VerifyPreservedIdentities(existingIdentities, stage);
            return "Verified both Stage02 river walls; created " + createdWalls.Count + " missing wall(s).";
        }
        finally
        {
            if (openedStage && stage.IsValid() && stage.isLoaded) EditorSceneManager.CloseScene(stage, true);
            if (originalActive.IsValid() && originalActive.isLoaded && SceneManager.GetActiveScene() != originalActive)
                SceneManager.SetActiveScene(originalActive);
        }
    }

    private static GameObject EnsureWall(FoundationExcavationVolume volume)
    {
        Transform existing = volume.transform.Find("PitWallRiver");
        if (existing != null)
        {
            if (existing.GetComponent<BoxCollider>() == null || existing.GetComponent<Renderer>() == null)
                throw new InvalidOperationException(volume.name + "/PitWallRiver exists but is incomplete; refusing to replace it.");
            return null;
        }

        BoxCollider support = FindSceneCollider(volume, "PitBottom");
        BoxCollider north = FindSceneCollider(volume, "PitWallNorth");
        BoxCollider south = FindSceneCollider(volume, "PitWallSouth");
        if (support == null || north == null || south == null)
            throw new InvalidOperationException(volume.name + " is missing its authored bottom or side wall geometry.");
        Renderer template = north.GetComponent<Renderer>();
        if (template == null || template.sharedMaterial == null)
            throw new InvalidOperationException(volume.name + "/PitWallNorth has no wall material to match.");
        if (Mathf.Abs(support.bounds.center.x - volume.transform.position.x) > 0.05f ||
            Mathf.Abs(north.bounds.center.x - support.bounds.center.x) > 0.05f ||
            Mathf.Abs(south.bounds.center.x - support.bounds.center.x) > 0.05f)
            throw new InvalidOperationException(volume.name + " resolved bottom/side geometry that does not belong to its pit.");

        Bounds soil = support.bounds;
        float riverDirection = volume.transform.position.x < 5f ? 1f : -1f;
        float soilHalfWidth = soil.extents.x;
        float endZ = (north.bounds.center.z + south.bounds.center.z) * 0.5f;
        float fullDepth = Mathf.Max(north.bounds.size.y, south.bounds.size.y);
        float outerNorthZ = north.bounds.max.z;
        float outerSouthZ = south.bounds.min.z;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "PitWallRiver";
        wall.transform.SetParent(volume.transform, false);
        Vector3 worldCenter = new Vector3(
            soil.center.x + riverDirection * (soilHalfWidth + 0.1f),
            north.bounds.center.y,
            endZ);
        wall.transform.localPosition = volume.transform.InverseTransformPoint(worldCenter);
        wall.transform.localRotation = Quaternion.identity;
        Vector3 localSize = new Vector3(0.2f, fullDepth, outerNorthZ - outerSouthZ);
        wall.transform.localScale = localSize;
        wall.layer = north.gameObject.layer;
        wall.GetComponent<Renderer>().sharedMaterial = template.sharedMaterial;
        GameObjectUtility.SetStaticEditorFlags(wall, (StaticEditorFlags)int.MaxValue);
        Undo.RegisterCreatedObjectUndo(wall, "Add Stage02 river wall");
        return wall;
    }

    private static void Verify(Scene stage)
    {
        FoundationExcavationVolume[] volumes = Resources.FindObjectsOfTypeAll<FoundationExcavationVolume>()
            .Where(v => v != null && v.gameObject.scene == stage).ToArray();
        if (volumes.Length != 2) throw new InvalidOperationException("Reopened Stage02 did not contain two excavation volumes.");
        foreach (FoundationExcavationVolume volume in volumes)
        {
            Transform wall = volume.transform.Find("PitWallRiver");
            BoxCollider collider = wall != null ? wall.GetComponent<BoxCollider>() : null;
            Renderer renderer = wall != null ? wall.GetComponent<Renderer>() : null;
            if (collider == null || renderer == null || !renderer.enabled || collider.isTrigger)
                throw new InvalidOperationException(volume.name + "/PitWallRiver is not an enabled solid rendered wall.");
            if (renderer.sharedMaterial == null || renderer.sharedMaterial.renderQueue > 2500)
                throw new InvalidOperationException(volume.name + "/PitWallRiver material is transparent or missing.");
            BoxCollider support = FindSceneCollider(volume, "PitBottom");
            BoxCollider north = FindSceneCollider(volume, "PitWallNorth");
            BoxCollider south = FindSceneCollider(volume, "PitWallSouth");
            Bounds soil = support.bounds;
            float direction = volume.transform.position.x < 5f ? 1f : -1f;
            bool wallOverlapsSoil = direction > 0f
                ? collider.bounds.min.x < soil.max.x - 0.001f
                : collider.bounds.max.x > soil.min.x + 0.001f;
            if (wallOverlapsSoil)
                throw new InvalidOperationException("River wall intrudes into the existing soil footprint.");
            if (collider.bounds.size.y + 0.001f < Mathf.Max(north.bounds.size.y, south.bounds.size.y))
                throw new InvalidOperationException("River wall does not cover full pit depth.");
            if (collider.bounds.min.z > south.bounds.min.z + 0.001f || collider.bounds.max.z < north.bounds.max.z - 0.001f)
                throw new InvalidOperationException("River wall does not close both side-wall corners.");
            Component identity = wall.GetComponent("MountingNailSceneTargetIdentity");
            if (identity == null || GetStableTargetId(identity) != GlobalObjectId.GetGlobalObjectIdSlow(wall.gameObject).ToString())
                throw new InvalidOperationException("River wall identity is missing or does not match its saved GlobalObjectId.");
            FoundationLooseSoilField field = volume.LooseSoilField;
            if (field == null || field.GetComponent<MeshFilter>().sharedMesh != null)
                throw new InvalidOperationException(volume.name + " has a serialized loose-soil runtime mesh after save.");
        }
    }

    private static BoxCollider FindSceneCollider(FoundationExcavationVolume volume, string name) =>
        Resources.FindObjectsOfTypeAll<BoxCollider>()
            .Where(c => c != null && c.gameObject.scene == volume.gameObject.scene && c.name == name)
            .OrderBy(c => Mathf.Abs(c.bounds.center.x - volume.transform.position.x))
            .FirstOrDefault();

    private static void ReleaseRuntimeMeshes(FoundationExcavationVolume[] volumes)
    {
        foreach (FoundationExcavationVolume volume in volumes)
            if (volume.LooseSoilField != null) volume.LooseSoilField.ReleaseRuntimeMeshForSceneSave();
    }

    private static Dictionary<string, string> CaptureIdentities(Scene scene)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Component identity in SceneIdentities(scene))
        {
            GameObject target = identity.gameObject;
            result.Add(GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(), GetStableTargetId(identity));
        }
        return result;
    }

    private static IEnumerable<Component> SceneIdentities(Scene scene)
    {
        Type identityType = Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
        if (identityType == null) throw new InvalidOperationException("Cannot resolve MountingNailSceneTargetIdentity.");
        return Resources.FindObjectsOfTypeAll(identityType).Cast<Component>()
            .Where(component => component != null && component.gameObject.scene == scene);
    }

    private static void VerifyPreservedIdentities(Dictionary<string, string> before, Scene scene)
    {
        Dictionary<string, string> after = CaptureIdentities(scene);
        foreach (KeyValuePair<string, string> identity in before)
            if (!after.TryGetValue(identity.Key, out string current) || current != identity.Value)
                throw new InvalidOperationException("Authoring changed an existing Stage02 mounting-target identity.");
    }

    private static string GetStableTargetId(Component identity)
    {
        SerializedProperty property = new SerializedObject(identity).FindProperty("stableTargetId");
        return property != null ? property.stringValue : string.Empty;
    }

    private static void EnsureStableIdentity(GameObject physicalRoot)
    {
        Type identityType = Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
        if (identityType == null) throw new InvalidOperationException("Cannot resolve MountingNailSceneTargetIdentity.");
        Component identity = physicalRoot.GetComponent(identityType);
        if (identity == null) identity = Undo.AddComponent(physicalRoot, identityType);
        GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(physicalRoot);
        if (id.identifierType != 2 || id.assetGUID.ToString() == "00000000000000000000000000000000")
            throw new InvalidOperationException("River wall did not receive a saved-scene GlobalObjectId.");
        SerializedObject serialized = new SerializedObject(identity);
        SerializedProperty stable = serialized.FindProperty("stableTargetId");
        if (stable == null) throw new InvalidOperationException("Identity component has no stableTargetId property.");
        stable.stringValue = id.ToString();
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(identity);
    }
}
