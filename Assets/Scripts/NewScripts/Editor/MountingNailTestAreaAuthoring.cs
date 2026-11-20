using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MountingNailTestAreaAuthoring
{
    private const string ScenePath = "Assets/Tests/Fixtures/MountingNailTestScene.unity";
    private const string NailPrefabPath = "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab";
    private const string BoardPrefabPath = "Assets/Prefabs/New/Resources/WoodenBoardResource_prefab.prefab";
    private const string HammerPrefabPath = "Assets/Prefabs/New/EquippableItems/IndustrialHammer.prefab";

    [MenuItem("Tools/Rage Quitting/Create Nail Test Area")]
    public static void CreateOrUpdate()
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new System.InvalidOperationException("Could not open the mounting nail test fixture scene.");
        try
        {
            SceneManager.SetActiveScene(scene);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "NailTestArea")
                {
                    EnsureStage3TargetsAndIdentities(root, scene);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log("Extended NailTestArea with Stage 3 dynamic probes and baked scene target identities; existing hierarchy was preserved.");
                    return;
                }
            }

            GameObject nailPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NailPrefabPath);
            GameObject boardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoardPrefabPath);
            GameObject hammerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HammerPrefabPath);
            if (nailPrefab == null || boardPrefab == null || hammerPrefab == null)
                throw new System.InvalidOperationException("The nail, WoodenBoard, or IndustrialHammer prefab is missing.");

            Terrain terrain = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                terrain = root.GetComponentInChildren<Terrain>(true);
                if (terrain != null) break;
            }
            if (terrain == null) throw new System.InvalidOperationException("Stage 02 terrain was not found.");

            const float centerX = -6f;
            const float centerZ = 2f;
            float groundY = terrain.SampleHeight(new Vector3(centerX, 0f, centerZ)) + terrain.transform.position.y;
            GameObject area = new GameObject("NailTestArea");
            SceneManager.MoveGameObjectToScene(area, scene);
            Undo.RegisterCreatedObjectUndo(area, "Create nail test area");
            area.transform.position = new Vector3(centerX, groundY, centerZ);

            GameObject markers = new GameObject("Markers_OrientationAndTargets");
            markers.transform.SetParent(area.transform, false);
            CreateStaticBlock(markers.transform, "Static_FloorTarget", new Vector3(0f, 0.05f, 0f), new Vector3(6f, 0.1f, 3f));
            CreateStaticBlock(markers.transform, "Static_WallTarget", new Vector3(0f, 1f, 1.55f), new Vector3(6f, 2f, 0.1f));
            CreateStaticBlock(markers.transform, "Static_CeilingTarget", new Vector3(0f, 2.55f, 0f), new Vector3(6f, 0.1f, 3f));

            CreateMarker(markers.transform, "01_SingleBoard", new Vector3(1.15f, 0.12f, -0.45f));
            CreateMarker(markers.transform, "02_StackedBoards", new Vector3(-1.2f, 0.12f, -0.45f));
            CreateMarker(markers.transform, "03_FloorWallCeiling", new Vector3(0f, 0.12f, 1.2f));

            AddBoard(boardPrefab, area.transform, "Board_Single", new Vector3(centerX + 1.15f, groundY + 0.1f, centerZ - 0.45f), Quaternion.identity, scene);
            AddBoard(boardPrefab, area.transform, "Board_Stacked_Lower", new Vector3(centerX - 1.2f, groundY + 0.1f, centerZ - 0.45f), Quaternion.identity, scene);
            AddBoard(boardPrefab, area.transform, "Board_Stacked_Upper", new Vector3(centerX - 1.2f, groundY + 0.35f, centerZ - 0.45f), Quaternion.identity, scene);
            AddBoard(boardPrefab, area.transform, "Board_Vertical", new Vector3(centerX - 2.35f, groundY + 1.1f, centerZ + 0.1f), Quaternion.Euler(0f, 0f, 90f), scene);

            AddDynamicProbe(area.transform, "Stage3_DynamicPair_First", new Vector3(centerX + 2.2f, groundY + 1.5f, centerZ), scene);
            AddDynamicProbe(area.transform, "Stage3_DynamicPair_Second", new Vector3(centerX + 2.2f, groundY + 1.5f, centerZ + 0.22f), scene);

            Vector3[] nailPositions =
            {
                new(-2.25f, groundY + 0.12f, -0.8f), new(-1.55f, groundY + 0.12f, -0.8f),
                new(-0.85f, groundY + 0.12f, -0.8f), new(-0.15f, groundY + 0.12f, -0.8f),
                new(0.55f, groundY + 0.12f, -0.8f), new(1.25f, groundY + 0.12f, -0.8f),
                new(-2.25f, groundY + 0.12f, -1.3f), new(-1.55f, groundY + 0.12f, -1.3f),
                new(-0.85f, groundY + 0.12f, -1.3f), new(-0.15f, groundY + 0.12f, -1.3f),
                new(0.55f, groundY + 0.12f, -1.3f), new(1.25f, groundY + 0.12f, -1.3f)
            };
            for (int i = 0; i < nailPositions.Length; i++)
                AddPrefab(nailPrefab, area.transform, "SingleMountingNail_" + (i + 1).ToString("00"),
                    nailPositions[i] + new Vector3(centerX, 0f, centerZ), Quaternion.identity, scene);

            AddPrefab(hammerPrefab, area.transform, "IndustrialHammer_LaterStages",
                new Vector3(centerX + 2.2f, groundY + 0.65f, centerZ + 0.65f), Quaternion.identity, scene);

            EnsureStage3TargetsAndIdentities(area, scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Created NailTestArea near the Stage 02 player start at (-6, 0, 2). The active scene was left unchanged.");
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(previousActiveScene);
            if (openedHere && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static GameObject AddBoard(GameObject prefab, Transform parent, string name, Vector3 position,
        Quaternion rotation, Scene scene)
    {
        GameObject instance = AddPrefab(prefab, parent, name, position, rotation, scene);
        Rigidbody body = instance.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
        EnsureStableSceneIdentity(instance);
        return instance;
    }

    private static void AddDynamicProbe(Transform parent, string name, Vector3 position, Scene scene)
    {
        if (parent.gameObject.scene != scene) throw new System.InvalidOperationException("The probe parent is outside Stage 02.");
        GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        probe.name = name;
        probe.transform.SetParent(parent, true);
        probe.transform.position = position;
        probe.transform.localScale = new Vector3(0.35f, 0.35f, 0.16f);
        Undo.RegisterCreatedObjectUndo(probe, "Create " + name);
        Rigidbody body = probe.AddComponent<Rigidbody>();
        body.mass = 1f;
        EnsureStableSceneIdentity(probe);
    }

    private static void EnsureStage3TargetsAndIdentities(GameObject area, Scene scene)
    {
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in sceneRoot.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.GetComponent<Rigidbody>() == null
                    || candidate.GetComponentInParent<Unity.Netcode.NetworkObject>(true) != null) continue;
                EnsureStableSceneIdentity(candidate.gameObject);
            }

            foreach (Collider collider in sceneRoot.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider.isTrigger || collider.attachedRigidbody != null
                    || collider.GetComponentInParent<Rigidbody>(true) != null
                    || collider.GetComponentInParent<Unity.Netcode.NetworkObject>(true) != null
                    || collider.GetComponentInParent<MountingNailSceneTargetIdentity>(true) != null) continue;

                EnsureStableSceneIdentity(ResolveBodylessStaticPhysicalRoot(collider));
            }
        }

        foreach (Transform child in area.GetComponentsInChildren<Transform>(true))
        {
            if (child.TryGetComponent(out Rigidbody _))
            {
                EnsureStableSceneIdentity(child.gameObject);
                continue;
            }
            if (!child.TryGetComponent(out Collider collider) || collider.attachedRigidbody != null
                || child.GetComponentInParent<Rigidbody>(true) != null
                || child.GetComponentInParent<Unity.Netcode.NetworkObject>(true) != null
                || child.GetComponentInParent<MountingNailSceneTargetIdentity>(true) != null) continue;
            BaseResourceNew resource = child.GetComponentInParent<BaseResourceNew>(true);
            Transform resourceRoot = resource != null ? resource.transform : null;
            EnsureStableSceneIdentity(resourceRoot != null ? resourceRoot.gameObject : child.gameObject);
        }

        EnsureDynamicProbe(area.transform, "Stage3_DynamicPair_First", area.transform.position + new Vector3(2.2f, 1.5f, 0f), scene);
        EnsureDynamicProbe(area.transform, "Stage3_DynamicPair_Second", area.transform.position + new Vector3(2.2f, 1.5f, 0.22f), scene);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Terrain terrain = root.GetComponentInChildren<Terrain>(true);
            if (terrain == null) continue;
            EnsureStableSceneIdentity(terrain.gameObject);
            break;
        }
    }

    private static GameObject ResolveBodylessStaticPhysicalRoot(Collider collider)
    {
        BaseResourceNew resource = collider.GetComponentInParent<BaseResourceNew>(true);
        if (resource != null) return resource.gameObject;

        MountableBridgeComponent mountable = collider.GetComponentInParent<MountableBridgeComponent>(true);
        if (mountable != null) return mountable.gameObject;

        GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(collider.gameObject);
        return prefabRoot != null && prefabRoot.scene == collider.gameObject.scene
            ? prefabRoot
            : collider.gameObject;
    }

    private static GameObject EnsureDynamicProbe(Transform parent, string name, Vector3 position, Scene scene)
    {
        if (parent.gameObject.scene != scene) throw new System.InvalidOperationException("The probe parent is outside Stage 02.");
        Transform existing = parent.Find(name);
        GameObject probe = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
        if (existing == null)
        {
            probe.name = name;
            probe.transform.SetParent(parent, true);
            probe.transform.position = position;
            probe.transform.localScale = new Vector3(0.35f, 0.35f, 0.16f);
            Undo.RegisterCreatedObjectUndo(probe, "Create " + name);
        }
        Rigidbody body = probe.GetComponent<Rigidbody>();
        if (body == null) body = Undo.AddComponent<Rigidbody>(probe);
        body.mass = 1f;
        body.isKinematic = false;
        body.useGravity = true;
        EnsureStableSceneIdentity(probe);
        return probe;
    }

    private static void EnsureStableSceneIdentity(GameObject physicalRoot)
    {
        if (physicalRoot == null || physicalRoot.GetComponentInParent<Unity.Netcode.NetworkObject>(true) != null) return;
        Rigidbody parentBody = physicalRoot.GetComponentInParent<Rigidbody>(true);
        if (parentBody != null && parentBody.transform != physicalRoot.transform) return;
        MountingNailSceneTargetIdentity identity = physicalRoot.GetComponent<MountingNailSceneTargetIdentity>();
        if (identity == null) identity = Undo.AddComponent<MountingNailSceneTargetIdentity>(physicalRoot);
        GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(physicalRoot);
        if (globalId.identifierType != 2 || globalId.assetGUID.ToString() == "00000000000000000000000000000000")
            throw new System.InvalidOperationException($"{physicalRoot.name} is not a saved-scene object and cannot receive a stable mounting nail identity.");
        string stableId = globalId.ToString();
        if (identity.StableTargetId == stableId) return;
        SerializedObject serialized = new(identity);
        serialized.FindProperty("stableTargetId").stringValue = stableId;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(identity);
    }

    private static GameObject AddPrefab(GameObject prefab, Transform parent, string name,
        Vector3 position, Quaternion rotation, Scene scene)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        if (instance == null) throw new System.InvalidOperationException("Could not instantiate " + prefab.name);
        instance.name = name;
        instance.transform.SetParent(parent, true);
        instance.transform.SetPositionAndRotation(position, rotation);
        Undo.RegisterCreatedObjectUndo(instance, "Create " + name);
        return instance;
    }

    private static GameObject CreateStaticBlock(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = localPosition;
        block.transform.localScale = localScale;
        block.isStatic = true;
        Undo.RegisterCreatedObjectUndo(block, "Create " + name);
        return block;
    }

    private static void CreateMarker(Transform parent, string name, Vector3 localPosition)
    {
        GameObject marker = new GameObject(name);
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = localPosition;
        Undo.RegisterCreatedObjectUndo(marker, "Create " + name);
    }
}






