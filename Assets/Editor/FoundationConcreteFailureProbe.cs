using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class FoundationConcreteFailureProbe
{
    private const string TutorialScenePath = "Assets/Scenes/Tutorial_scene.unity";

    [MenuItem("Tools/RageQuitting/Validate Foundation Concrete Failure")]
    public static string ValidateFromMenu()
    {
        string message = Validate();
        Debug.Log(message);
        return message;
    }

    // Returns a success message only after cleanup. Invalid configuration throws;
    // unsafe Editor states throw an explicit blocked result before opening the preview.
    // This validates the saved Tutorial configuration, never the user's live objects.
    public static string Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
            throw new InvalidOperationException("BLOCKED: validation requires an idle EditMode Editor.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene open = SceneManager.GetSceneAt(i);
            if (open.path == TutorialScenePath && open.isDirty)
                throw new InvalidOperationException("BLOCKED: Tutorial_scene has unsaved changes; saved configuration would be stale.");
        }

        Scene scene = default;
        FoundationExcavationVolume[] volumes = Array.Empty<FoundationExcavationVolume>();
        try
        {
            scene = EditorSceneManager.OpenPreviewScene(TutorialScenePath);
            ValidateMalformedCrackThresholds();

            volumes = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<FoundationExcavationVolume>(true))
                .ToArray();
            if (volumes.Length != 2)
                throw new InvalidOperationException($"Expected two foundation excavation volumes, found {volumes.Length}.");

            WheelbarrowDockingStation[] stations = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<WheelbarrowDockingStation>(true))
                .Where(station => station.DockType == WheelbarrowDockType.FoundationPouring)
                .ToArray();
            if (stations.Length != 2)
                throw new InvalidOperationException($"Expected two foundation docks, found {stations.Length}.");

            ValidateBakedSlabSurfaces(scene, volumes);
            foreach (FoundationExcavationVolume volume in volumes)
                ValidateVolume(volume, stations);

            return "Foundation concrete failure probe passed for both Tutorial_scene foundations.";
        }
        finally
        {
            try
            {
                // These materials are allocated by ApplyDiggingState/ApplyConcreteState,
                // and FoundationExcavationVolume has no OnDestroy to release them.
                foreach (FoundationExcavationVolume volume in volumes)
                {
                    DestroyRuntimeMaterial(volume, "runtimeMaterial");
                    DestroyRuntimeMaterial(volume, "runtimeConcreteMaterial");
                }
            }
            finally
            {
                // Discard the entire transaction, including nonserialized caches and
                // Initialize() side effects. Never attempt restoration through saving.
                if (scene.IsValid())
                    EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }

    private static void DestroyRuntimeMaterial(FoundationExcavationVolume volume, string fieldName)
    {
        FieldInfo field = typeof(FoundationExcavationVolume).GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(typeof(FoundationExcavationVolume).Name, fieldName);
        if (volume != null && field.GetValue(volume) is Material material && !EditorUtility.IsPersistent(material))
            UnityEngine.Object.DestroyImmediate(material);
    }

    private static void ValidateMalformedCrackThresholds()
    {
        ConcretePouringProfileSO profile = ScriptableObject.CreateInstance<ConcretePouringProfileSO>();
        try
        {
            SerializedObject serialized = new SerializedObject(profile);
            SerializedProperty thresholdsProperty = serialized.FindProperty("failedConcreteCrackThresholds");
            if (thresholdsProperty == null)
                throw new InvalidOperationException("Concrete pouring profile has no serialized crack thresholds.");

            thresholdsProperty.vector3Value = new Vector3(12f, -5f, 3f);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Vector3 thresholds = profile.FailedConcreteCrackThresholds;
            if (!Mathf.Approximately(thresholds.x, 12f) ||
                !Mathf.Approximately(thresholds.y, 12f) ||
                !Mathf.Approximately(thresholds.z, 12f))
                throw new InvalidOperationException(
                    $"Malformed crack thresholds were not sequentially clamped: {thresholds}.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    private static void ValidateVolume(
        FoundationExcavationVolume volume,
        WheelbarrowDockingStation[] stations)
    {
        SerializedObject serialized = new SerializedObject(volume);
        FoundationFailedConcreteTarget target =
            serialized.FindProperty("failedConcreteTarget").objectReferenceValue as FoundationFailedConcreteTarget;
        Transform pose = serialized.FindProperty("failedWheelbarrowPose").objectReferenceValue as Transform;
        BoxCollider recovery = serialized.FindProperty("recoveryVolume").objectReferenceValue as BoxCollider;
        NavMeshObstacle obstacle =
            serialized.FindProperty("pitNavMeshObstacle").objectReferenceValue as NavMeshObstacle;
        GameObject proxy = serialized.FindProperty("navMeshBakeProxy").objectReferenceValue as GameObject;
        Collider concreteCollider = serialized.FindProperty("driedConcreteCollider").objectReferenceValue as Collider;
        Renderer concreteRenderer = serialized.FindProperty("concreteRenderer").objectReferenceValue as Renderer;
        SerializedProperty cracks = serialized.FindProperty("failedConcreteCrackVisuals");
        if (target == null || target.InteractionCollider == null || pose == null || recovery == null ||
            obstacle == null || proxy == null || concreteCollider == null || concreteRenderer == null ||
            cracks == null || cracks.arraySize != 3)
            throw new InvalidOperationException($"{volume.name} has incomplete critical-failure wiring.");
        if (!volume.ContainsRecoveryWheelbarrow(pose.position))
            throw new InvalidOperationException($"{volume.name} trap pose is outside its recovery volume.");

        ValidateCrackVisuals(volume, cracks);
        ValidateBakeProxy(volume, proxy);

        BridgeConstructionSite site = target.ConstructionSite;
        site?.Initialize();
        WheelbarrowDockingStation station = stations.SingleOrDefault(item => item.FoundationSite == site);
        if (site == null || station == null)
            throw new InvalidOperationException($"{volume.name} target is not paired with one foundation site and dock.");

        SerializedObject siteSerialized = new SerializedObject(site);
        ConcretePouringProfileSO profile =
            siteSerialized.FindProperty("concretePouringProfile").objectReferenceValue as ConcretePouringProfileSO;
        if (profile == null)
            throw new InvalidOperationException($"{volume.name} has no concrete pouring profile.");
        Vector3 thresholds = profile.FailedConcreteCrackThresholds;
        if (thresholds.x < 0f || thresholds.x > thresholds.y || thresholds.y > thresholds.z)
            throw new InvalidOperationException($"{volume.name} crack thresholds are not sequentially clamped: {thresholds}.");

        BridgeComponentNetworkState original = default;
        site.PopulateNetworkState(ref original);
        BridgeComponentNetworkState probe = original;
        probe.constructionStage = (int)BridgeConstructionStage.ConcretePouring;
        probe.constructionAnchor1 = (float)FoundationConcreteFailureState.HardenedFailure;
        probe.constructionAnchor2 = thresholds.y;
        probe.constructionAnchor3 = 0f;
        try
        {
            site.ApplyNetworkState(probe);
            if (site.ConcreteFailureState != FoundationConcreteFailureState.HardenedFailure ||
                Mathf.Abs(site.FailedConcreteBreakProgress - thresholds.y) > 0.01f ||
                !site.CanApplyToolWork(EquippableItemType.Pickaxe, 1f, FoundationFailedConcreteTarget.WorkPointId) ||
                site.CanApplyToolWork(EquippableItemType.Axe, 1f, FoundationFailedConcreteTarget.WorkPointId) ||
                !target.enabled || !target.InteractionCollider.enabled)
                throw new InvalidOperationException($"{volume.name} failed state/tool round-trip validation.");
            ValidateObstacle(volume, obstacle, false, "HardenedFailure");
            if (!concreteCollider.enabled)
                throw new InvalidOperationException($"{volume.name} hardened concrete collider is disabled.");

            Vector3 hardenedScale = concreteRenderer.transform.localScale;
            Vector3 hardenedPosition = concreteRenderer.transform.localPosition;
            probe.constructionAnchor1 = (float)FoundationConcreteFailureState.Collapsing;
            probe.constructionAnchor3 = Time.time + profile.FailedConcreteCollapseDuration * 0.5f;
            site.ApplyNetworkState(probe);
            ValidateObstacle(volume, obstacle, true, "Collapsing");
            if (!concreteCollider.enabled)
                throw new InvalidOperationException($"{volume.name} disables its slab collider before collapse completes.");
            if (concreteRenderer.transform.localScale.y >= hardenedScale.y ||
                concreteRenderer.transform.localPosition.y >= hardenedPosition.y)
                throw new InvalidOperationException($"{volume.name} has no visible procedural collapse at mid-sequence.");

            probe.constructionAnchor1 = (float)FoundationConcreteFailureState.AwaitingWheelbarrowExit;
            probe.constructionAnchor3 = 0f;
            site.ApplyNetworkState(probe);
            ValidateObstacle(volume, obstacle, true, "AwaitingWheelbarrowExit");
            if (concreteCollider.enabled)
                throw new InvalidOperationException($"{volume.name} leaves the failed slab collider enabled after collapse.");

            BridgeComponentNetworkState roundTrip = default;
            site.PopulateNetworkState(ref roundTrip);
            if (Mathf.RoundToInt(roundTrip.constructionAnchor1) !=
                (int)FoundationConcreteFailureState.AwaitingWheelbarrowExit ||
                Mathf.Abs(roundTrip.constructionAnchor2 - thresholds.y) > 0.01f)
                throw new InvalidOperationException($"{volume.name} failed network-state serialization validation.");
        }
        finally
        {
            site.ApplyNetworkState(original);
        }
    }

    private static void ValidateBakedSlabSurfaces(Scene scene, FoundationExcavationVolume[] volumes)
    {
        NavMeshSurface[] surfaces = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<NavMeshSurface>(true))
            .Where(surface => surface.navMeshData != null)
            .ToArray();
        if (surfaces.Length == 0)
            throw new InvalidOperationException("Tutorial_scene has no NavMeshSurface with baked data.");

        // Register read-only baked data in a remote region so the live scene's
        // baked meshes/obstacles cannot satisfy or carve the probe's samples.
        Vector3 offset = new Vector3(0f, 10000f, 0f);
        var instances = new List<NavMeshDataInstance>();
        try
        {
            foreach (FoundationExcavationVolume volume in volumes)
            {
                // Check for unrelated navigation data before registering ours.
                Vector3 center = GetSlabCenter(volume) + offset;
                if (NavMesh.SamplePosition(center, out _, 1f, NavMesh.AllAreas))
                    throw new InvalidOperationException("BLOCKED: probe NavMesh sampling region is occupied.");
            }
            foreach (NavMeshSurface surface in surfaces)
            {
                NavMeshDataInstance instance = NavMesh.AddNavMeshData(surface.navMeshData,
                    surface.transform.position + offset, surface.transform.rotation);
                instances.Add(instance);
                if (!instance.valid)
                    throw new InvalidOperationException($"Could not register temporary NavMesh data for {surface.name}.");
            }
            foreach (FoundationExcavationVolume volume in volumes)
                ValidateBakedSlabCenter(volume, offset);
        }
        finally
        {
            foreach (NavMeshDataInstance instance in instances)
                instance.Remove();
        }
    }

    private static Vector3 GetSlabCenter(FoundationExcavationVolume volume)
    {
        GameObject proxy = volume != null ? volume.NavMeshBakeProxy : null;
        MeshFilter meshFilter = proxy != null ? proxy.GetComponent<MeshFilter>() : null;
        if (meshFilter == null || meshFilter.sharedMesh == null)
            throw new InvalidOperationException($"{volume?.name ?? "Foundation"} bake proxy has no mesh.");

        Bounds meshBounds = meshFilter.sharedMesh.bounds;
        return proxy.transform.TransformPoint(new Vector3(
            meshBounds.center.x,
            meshBounds.max.y,
            meshBounds.center.z));
    }

    private static void ValidateBakedSlabCenter(FoundationExcavationVolume volume, Vector3 offset)
    {
        Vector3 expectedTop = GetSlabCenter(volume) + offset;
        if (!NavMesh.SamplePosition(expectedTop + Vector3.up * 0.05f, out NavMeshHit hit, 0.5f, NavMesh.AllAreas))
            throw new InvalidOperationException($"{volume.name} has no baked NavMesh at the slab center.");

        float horizontalError = Vector2.Distance(
            new Vector2(hit.position.x, hit.position.z),
            new Vector2(expectedTop.x, expectedTop.z));
        float heightError = Mathf.Abs(hit.position.y - expectedTop.y);
        if (horizontalError > 0.5f || heightError > 0.15f)
            throw new InvalidOperationException(
                $"{volume.name} slab NavMesh sample is outside tolerance: horizontal={horizontalError:F3}, " +
                $"height={heightError:F3}, expectedY={expectedTop.y:F3}, actualY={hit.position.y:F3}.");
    }

    private static void ValidateCrackVisuals(FoundationExcavationVolume volume, SerializedProperty cracks)
    {
        for (int i = 0; i < cracks.arraySize; i++)
        {
            GameObject crack = cracks.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (crack == null)
                throw new InvalidOperationException($"{volume.name} crack visual {i} is missing.");
            if (crack.GetComponentsInChildren<Collider>(true).Length > 0)
                throw new InvalidOperationException($"{volume.name} crack visual {crack.name} contains a collider.");
        }
    }

    private static void ValidateBakeProxy(FoundationExcavationVolume volume, GameObject proxy)
    {
        Collider proxyCollider = proxy.GetComponent<Collider>();
        Renderer proxyRenderer = proxy.GetComponent<Renderer>();
        NavMeshModifier modifier = proxy.GetComponent<NavMeshModifier>();
        NavMeshSurface surface = proxy.GetComponent<NavMeshSurface>();
        if (proxyCollider == null || proxyRenderer == null || modifier == null || surface == null ||
            proxyCollider.enabled || proxyRenderer.enabled || !modifier.overrideArea || modifier.area != 0)
            throw new InvalidOperationException($"{volume.name} bake-only proxy or NavMeshModifier is invalid.");
        if (surface.collectObjects != CollectObjects.Children ||
            surface.useGeometry != NavMeshCollectGeometry.PhysicsColliders ||
            surface.defaultArea != 0 || surface.navMeshData == null)
            throw new InvalidOperationException($"{volume.name} bake-only NavMeshSurface is invalid or has no baked data.");
    }

    private static void ValidateObstacle(
        FoundationExcavationVolume volume,
        NavMeshObstacle obstacle,
        bool expectedEnabled,
        string state)
    {
        if (obstacle.enabled != expectedEnabled || expectedEnabled && !obstacle.carving)
            throw new InvalidOperationException(
                $"{volume.name} obstacle is invalid for {state}: enabled={obstacle.enabled}, carving={obstacle.carving}.");
    }
}
