using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public static class MountingNailAssetAuthoring
{
    private const string PrefabPath = "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab";
    private const string ResourcePath = "Assets/ScriptableObjectAssets/New/BaseResourceSO/MetalProducts/SingleMountingNail.asset";
    private const string ProfilePath = "Assets/ScriptableObjectAssets/New/MountingNailProfiles/StandardMountingNailProfile.asset";
    private const string ModelPath = "Assets/AI_assets/Models/Bridge/Bridge_Nail_LowPoly/Bridge_Nail_LowPoly.prefab";
    private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

    [MenuItem("Tools/Rage Quitting/Create Mounting Nail Assets")]
    public static void CreateOrUpdate()
    {
        EnsureFolder("Assets/ScriptableObjectAssets/New/MountingNailProfiles");
        EnsureFolder("Assets/Prefabs/New/Resources/MetalProducts");

        MountingNailProfileSO profile = AssetDatabase.LoadAssetAtPath<MountingNailProfileSO>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<MountingNailProfileSO>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        BaseResourceSO resource = AssetDatabase.LoadAssetAtPath<BaseResourceSO>(ResourcePath);
        if (resource == null)
        {
            resource = ScriptableObject.CreateInstance<BaseResourceSO>();
            AssetDatabase.CreateAsset(resource, ResourcePath);
        }
        resource.resourceName = "Single Mounting Nail";
        resource.resourceDurability = 10f;
        resource.movementSpeedPenalty = 0f;
        resource.canBeCarried = true;
        resource.minAmountOfPlayersNeeded = 1;
        resource.allowMultipleCarriers = false;
        resource.recommendedCarriers = 1;
        resource.maxCarriers = 1;

        GameObject prefab = BuildPrefab(profile, resource);
        resource.resourcePrefab = prefab;
        EditorUtility.SetDirty(resource);
        RegisterNetworkPrefab(prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.ForceReserializeAssets(new[] { PrefabPath });
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created mounting nail profile, resource, prefab, and network registration.");
    }

    private static GameObject BuildPrefab(MountingNailProfileSO profile, BaseResourceSO resource)
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene stagingScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(stagingScene);
        GameObject root = new GameObject("SingleMountingNail");
        try
        {
            root.AddComponent<NetworkObject>();
            root.AddComponent<ClientNetworkTransform>();
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = profile.MassKg;
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2;
            collider.height = profile.LengthMeters;
            collider.center = Vector3.zero;

            BaseResourceNew baseResource = root.AddComponent<BaseResourceNew>();
            SerializedObject resourceComponent = new SerializedObject(baseResource);
            resourceComponent.FindProperty("baseResourceSO").objectReferenceValue = resource;
            resourceComponent.ApplyModifiedPropertiesWithoutUndo();

            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelPrefab == null) throw new System.InvalidOperationException("The source nail model prefab is missing.");
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            model.name = "Visual";
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            Bounds sourceBounds = CalculateLocalMeshBounds(model.transform);
            if (sourceBounds.size.y <= 0.0001f)
                throw new System.InvalidOperationException("The source nail model has no measurable length axis.");
            float longitudinalScale = profile.LengthMeters / sourceBounds.size.y;
            Vector3 authoredScale = new Vector3(0.035f, longitudinalScale, 0.035f);
            model.transform.localScale = new Vector3(authoredScale.x * profile.VisualWidthMultiplier,
                authoredScale.y, authoredScale.z * profile.VisualWidthMultiplier);
            model.transform.localPosition = -(model.transform.localRotation
                * Vector3.Scale(sourceBounds.center, model.transform.localScale));

            Renderer visualRenderer = model.GetComponentInChildren<Renderer>();
            if (visualRenderer == null) throw new System.InvalidOperationException("The source nail model has no Renderer.");
            Bounds visualBounds = visualRenderer.bounds;
            float authoredRadius = Mathf.Max(0.001f,
                Mathf.Min(visualBounds.extents.x, visualBounds.extents.y) * 0.5f / profile.VisualWidthMultiplier);
            collider.radius = authoredRadius * profile.VisualWidthMultiplier;
            Transform tip = CreateMarker(root.transform, "Tip", new Vector3(0f, 0f, profile.LengthMeters * 0.5f), Quaternion.identity);
            Transform head = CreateMarker(root.transform, "Head", new Vector3(0f, 0f, -profile.LengthMeters * 0.5f), Quaternion.identity);
            MountingNail nail = root.AddComponent<MountingNail>();
            SerializedObject nailComponent = new SerializedObject(nail);
            nailComponent.FindProperty("profile").objectReferenceValue = profile;
            nailComponent.FindProperty("visual").objectReferenceValue = model.transform;
            nailComponent.FindProperty("tip").objectReferenceValue = tip;
            nailComponent.FindProperty("head").objectReferenceValue = head;
            nailComponent.FindProperty("authoredVisualLengthMeters").floatValue = profile.LengthMeters;
            nailComponent.FindProperty("authoredVisualScale").vector3Value = authoredScale;
            nailComponent.FindProperty("authoredVisualCenter").vector3Value = sourceBounds.center;
            nailComponent.FindProperty("authoredCapsuleRadius").floatValue = authoredRadius;
            nailComponent.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (saved == null) throw new System.InvalidOperationException("Unity could not save the mounting nail prefab.");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(previousActiveScene);
            if (stagingScene.IsValid() && stagingScene.isLoaded)
                EditorSceneManager.CloseScene(stagingScene, true);
        }
    }

    private static Bounds CalculateLocalMeshBounds(Transform modelRoot)
    {
        MeshFilter[] filters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        bool hasBounds = false;
        Bounds result = default;
        foreach (MeshFilter filter in filters)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;
            Bounds bounds = mesh.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z);
                Vector3 local = modelRoot.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!hasBounds)
                {
                    result = new Bounds(local, Vector3.zero);
                    hasBounds = true;
                }
                else result.Encapsulate(local);
            }
        }
        if (!hasBounds) throw new System.InvalidOperationException("The source nail model has no MeshFilter bounds.");
        return result;
    }

    private static Transform CreateMarker(Transform parent, string name, Vector3 localPosition, Quaternion localRotation)
    {
        GameObject marker = new GameObject(name);
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = localPosition;
        marker.transform.localRotation = localRotation;
        return marker.transform;
    }

    private static void RegisterNetworkPrefab(GameObject prefab)
    {
        NetworkPrefabsList listAsset = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
        if (listAsset == null) throw new System.InvalidOperationException("DefaultNetworkPrefabs.asset could not be loaded.");
        SerializedObject serialized = new SerializedObject(listAsset);
        SerializedProperty list = serialized.FindProperty("List");
        if (list == null || !list.isArray)
            throw new System.InvalidOperationException("DefaultNetworkPrefabs.asset has no serialized List array.");
        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == prefab)
                return;
        }

        int index = list.arraySize;
        list.InsertArrayElementAtIndex(index);
        SerializedProperty entry = list.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("Override").intValue = 0;
        entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
        entry.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
        entry.FindPropertyRelative("SourceHashToOverride").longValue = 0;
        entry.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(listAsset);
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}

