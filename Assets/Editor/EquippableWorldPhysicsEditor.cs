using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EquippableWorldPhysics))]
public sealed class EquippableWorldPhysicsEditor : Editor
{
    private const string InteractableLayerName = "Interactable";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EquippableWorldPhysics worldPhysics = (EquippableWorldPhysics)target;
        DrawValidationSummary(worldPhysics);

        EditorGUILayout.Space();
        if (GUILayout.Button("Configure All Equippable Profiles"))
        {
            EquippableWorldPhysicsValidation.ConfigureAllProfiles();
        }

        if (GUILayout.Button("Validate All Equippable Prefabs"))
        {
            EquippableWorldPhysicsValidation.ValidateAllPrefabs();
        }
    }

    private void OnSceneGUI()
    {
        EquippableWorldPhysics worldPhysics = (EquippableWorldPhysics)target;
        EquippableWorldPhysicsProfileSO profile = worldPhysics.Profile;
        Transform shapeRoot = worldPhysics.ColliderShapeRoot;
        if (profile == null || shapeRoot == null)
        {
            return;
        }

        EquippableColliderShape[] shapes = profile.ColliderShapes;
        if (shapes == null)
        {
            return;
        }

        using (new Handles.DrawingScope(new Color(0.2f, 1f, 0.35f, 0.9f), shapeRoot.localToWorldMatrix))
        {
            for (int i = 0; i < shapes.Length; i++)
            {
                DrawShapeBounds(shapes[i], Vector3.zero);
            }
        }

        if (!profile.GeneratePickupInteractionColliders)
        {
            return;
        }

        using (new Handles.DrawingScope(new Color(0.1f, 0.85f, 1f, 0.85f), shapeRoot.localToWorldMatrix))
        {
            Vector3 padding = Vector3.one * (profile.PickupInteractionPadding * 2f);
            for (int i = 0; i < shapes.Length; i++)
            {
                DrawShapeBounds(shapes[i], padding);
            }
        }
    }

    private static void DrawShapeBounds(EquippableColliderShape shape, Vector3 sizeAddition)
    {
        Matrix4x4 previous = Handles.matrix;
        Handles.matrix *= Matrix4x4.TRS(shape.center, Quaternion.Euler(shape.rotationEuler), Vector3.one);
        Handles.DrawWireCube(Vector3.zero, MaxComponents(shape.size + sizeAddition, 0.02f));
        Handles.matrix = previous;
    }

    private static Vector3 MaxComponents(Vector3 value, float minimum)
    {
        return new Vector3(
            Mathf.Max(minimum, value.x),
            Mathf.Max(minimum, value.y),
            Mathf.Max(minimum, value.z));
    }

    private static void DrawValidationSummary(EquippableWorldPhysics worldPhysics)
    {
        if (worldPhysics.Profile == null)
        {
            EditorGUILayout.HelpBox("The equippable item has no world physics profile.", MessageType.Error);
        }
        else if (worldPhysics.Profile.ColliderShapes == null || worldPhysics.Profile.ColliderShapes.Length == 0)
        {
            EditorGUILayout.HelpBox("The world physics profile has no collider shapes.", MessageType.Error);
        }

        if (worldPhysics.ColliderShapeRoot == worldPhysics.transform)
        {
            EditorGUILayout.HelpBox(
                "Item_visuals was not found. Collider shapes will use the item root and may not match the model.",
                MessageType.Warning);
        }

        if (LayerMask.NameToLayer(InteractableLayerName) < 0)
        {
            EditorGUILayout.HelpBox($"Required layer '{InteractableLayerName}' does not exist.", MessageType.Error);
        }
    }
}

public static class EquippableWorldPhysicsValidation
{
    private const string ConfigureMenuPath = "Tools/RageQuitting/Equippable Tools/Configure Pickup Colliders";
    private const string ValidateMenuPath = "Tools/RageQuitting/Equippable Tools/Validate Prefabs";
    private const string InteractableLayerName = "Interactable";

    [MenuItem(ConfigureMenuPath)]
    public static void ConfigureAllProfiles()
    {
        string[] profileGuids = AssetDatabase.FindAssets("t:EquippableWorldPhysicsProfileSO");
        int changedCount = 0;
        for (int i = 0; i < profileGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(profileGuids[i]);
            EquippableWorldPhysicsProfileSO profile =
                AssetDatabase.LoadAssetAtPath<EquippableWorldPhysicsProfileSO>(path);
            if (profile == null)
            {
                continue;
            }

            SerializedObject serializedProfile = new SerializedObject(profile);
            SerializedProperty generateProperty =
                serializedProfile.FindProperty("generatePickupInteractionColliders");
            SerializedProperty paddingProperty = serializedProfile.FindProperty("pickupInteractionPadding");
            bool changed = false;

            if (generateProperty != null && !generateProperty.boolValue)
            {
                generateProperty.boolValue = true;
                changed = true;
            }

            if (paddingProperty != null && paddingProperty.floatValue <= 0f)
            {
                paddingProperty.floatValue = 0.04f;
                changed = true;
            }

            serializedProfile.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            if (changed)
            {
                changedCount++;
            }
        }

        ConfigureInteractionLayerInCurrentEditor();
        AssetDatabase.SaveAssets();
        Debug.Log($"Configured pickup interaction colliders for {profileGuids.Length} profiles ({changedCount} changed).");
        ValidateAllPrefabs();
    }

    [MenuItem(ValidateMenuPath)]
    public static void ValidateAllPrefabs()
    {
        string[] itemGuids = AssetDatabase.FindAssets("t:EquippableItemSO");
        int checkedCount = 0;
        int issueCount = 0;
        HashSet<string> checkedPrefabPaths = new HashSet<string>();

        for (int i = 0; i < itemGuids.Length; i++)
        {
            string itemPath = AssetDatabase.GUIDToAssetPath(itemGuids[i]);
            EquippableItemSO item = AssetDatabase.LoadAssetAtPath<EquippableItemSO>(itemPath);
            if (item == null || item.equippableItemPrefab == null)
            {
                continue;
            }

            string prefabPath = AssetDatabase.GetAssetPath(item.equippableItemPrefab);
            if (string.IsNullOrEmpty(prefabPath) || !checkedPrefabPaths.Add(prefabPath))
            {
                continue;
            }

            checkedCount++;
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                EquippableWorldPhysics worldPhysics = prefabRoot.GetComponent<EquippableWorldPhysics>();
                if (worldPhysics == null)
                {
                    Debug.LogError($"Equippable prefab '{prefabPath}' has no EquippableWorldPhysics component.");
                    issueCount++;
                    continue;
                }

                EquippableWorldPhysicsProfileSO profile = item.worldPhysicsProfile;
                if (profile == null || profile.ColliderShapes == null || profile.ColliderShapes.Length == 0)
                {
                    Debug.LogError($"Equippable prefab '{prefabPath}' has no configured collider shapes.");
                    issueCount++;
                    continue;
                }

                EquippableToolVisualBuilder visualBuilder =
                    prefabRoot.GetComponent<EquippableToolVisualBuilder>();
                if (visualBuilder != null)
                {
                    visualBuilder.Rebuild();
                }

                Transform shapeRoot = worldPhysics.ColliderShapeRoot;
                if (shapeRoot == null || shapeRoot == prefabRoot.transform)
                {
                    Debug.LogWarning($"Equippable prefab '{prefabPath}' has no Item_visuals collider space root.");
                    issueCount++;
                    continue;
                }

                if (TryCalculateRendererBounds(shapeRoot, out Bounds rendererBounds) &&
                    TryCalculateShapeBounds(profile.ColliderShapes, out Bounds shapeBounds) &&
                    !rendererBounds.Intersects(shapeBounds))
                {
                    Debug.LogWarning(
                        $"Equippable prefab '{prefabPath}' has collider bounds that do not overlap its rendered model. " +
                        $"Renderer center/size: {rendererBounds.center}/{rendererBounds.size}; " +
                        $"shape center/size: {shapeBounds.center}/{shapeBounds.size}.");
                    issueCount++;
                }

                issueCount += ValidateGeneratedRuntimeColliders(
                    worldPhysics,
                    profile,
                    shapeRoot,
                    prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        if (LayerMask.NameToLayer(InteractableLayerName) < 0)
        {
            Debug.LogError($"Required layer '{InteractableLayerName}' does not exist.");
            issueCount++;
        }
        else
        {
            int interactableLayer = LayerMask.NameToLayer(InteractableLayerName);
            for (int layer = 0; layer < 32; layer++)
            {
                if (!Physics.GetIgnoreLayerCollision(interactableLayer, layer))
                {
                    Debug.LogError(
                        $"Layer '{InteractableLayerName}' still collides with layer {layer}; pickup triggers could activate gameplay callbacks.");
                    issueCount++;
                }
            }
        }

        if (issueCount == 0)
        {
            Debug.Log($"Validated {checkedCount} equippable prefabs without collider configuration issues.");
        }
        else
        {
            Debug.LogWarning($"Validated {checkedCount} equippable prefabs and found {issueCount} collider configuration issue(s).");
        }
    }

    private static void ConfigureInteractionLayerInCurrentEditor()
    {
        int interactableLayer = LayerMask.NameToLayer(InteractableLayerName);
        if (interactableLayer < 0)
        {
            return;
        }

        for (int layer = 0; layer < 32; layer++)
        {
            Physics.IgnoreLayerCollision(interactableLayer, layer, true);
        }
    }

    private static int ValidateGeneratedRuntimeColliders(
        EquippableWorldPhysics worldPhysics,
        EquippableWorldPhysicsProfileSO profile,
        Transform shapeRoot,
        string prefabPath)
    {
        FieldInfo profileField = typeof(EquippableWorldPhysics).GetField(
            "profile",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo buildMethod = typeof(EquippableWorldPhysics).GetMethod(
            "BuildCompoundColliders",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (profileField == null || buildMethod == null)
        {
            Debug.LogError("Equippable runtime collider validation could not resolve its build entry point.");
            return 1;
        }

        profileField.SetValue(worldPhysics, profile);
        buildMethod.Invoke(worldPhysics, null);

        Transform physicalRoot = shapeRoot.Find("WorldPhysicsColliders");
        Transform pickupRoot = shapeRoot.Find("PickupInteractionColliders");
        Collider[] physicalColliders = physicalRoot != null
            ? physicalRoot.GetComponentsInChildren<Collider>(true)
            : System.Array.Empty<Collider>();
        Collider[] pickupColliders = pickupRoot != null
            ? pickupRoot.GetComponentsInChildren<Collider>(true)
            : System.Array.Empty<Collider>();
        int expectedCount = profile.ColliderShapes.Length;
        int issueCount = 0;

        if (physicalColliders.Length != expectedCount)
        {
            Debug.LogError(
                $"Equippable prefab '{prefabPath}' generated {physicalColliders.Length} physical colliders; expected {expectedCount}.");
            issueCount++;
        }

        int expectedPickupCount = profile.GeneratePickupInteractionColliders ? expectedCount : 0;
        if (pickupColliders.Length != expectedPickupCount)
        {
            Debug.LogError(
                $"Equippable prefab '{prefabPath}' generated {pickupColliders.Length} pickup colliders; expected {expectedPickupCount}.");
            issueCount++;
        }

        int interactableLayer = LayerMask.NameToLayer(InteractableLayerName);
        for (int i = 0; i < physicalColliders.Length; i++)
        {
            if (physicalColliders[i].isTrigger)
            {
                Debug.LogError($"Equippable prefab '{prefabPath}' generated a trigger in its physical collider root.");
                issueCount++;
            }
        }

        for (int i = 0; i < pickupColliders.Length; i++)
        {
            Collider pickupCollider = pickupColliders[i];
            if (!pickupCollider.isTrigger || pickupCollider.gameObject.layer != interactableLayer)
            {
                Debug.LogError(
                    $"Equippable prefab '{prefabPath}' generated a pickup collider with an invalid trigger or layer configuration.");
                issueCount++;
            }

            if (i < physicalColliders.Length && !pickupCollider.bounds.Intersects(physicalColliders[i].bounds))
            {
                Debug.LogError(
                    $"Equippable prefab '{prefabPath}' generated a pickup collider that does not cover its physical shape {i}.");
                issueCount++;
            }

            if (BridgeTargetResolver.Resolve(pickupCollider) is not EquippableItem)
            {
                Debug.LogError(
                    $"Equippable prefab '{prefabPath}' pickup collider {i} does not resolve to EquippableItem.");
                issueCount++;
            }
        }

        return issueCount;
    }

    private static bool TryCalculateRendererBounds(Transform shapeRoot, out Bounds bounds)
    {
        bounds = default;
        bool initialized = false;
        Renderer[] renderers = shapeRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.transform == shapeRoot)
            {
                continue;
            }

            Bounds rendererLocalBounds = renderer.localBounds;
            Vector3 min = rendererLocalBounds.min;
            Vector3 max = rendererLocalBounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 rendererLocalCorner = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 worldCorner = renderer.localToWorldMatrix.MultiplyPoint3x4(rendererLocalCorner);
                Vector3 localCorner = shapeRoot.InverseTransformPoint(worldCorner);
                if (!initialized)
                {
                    bounds = new Bounds(localCorner, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(localCorner);
                }
            }
        }

        return initialized;
    }

    private static bool TryCalculateShapeBounds(EquippableColliderShape[] shapes, out Bounds bounds)
    {
        bounds = default;
        bool initialized = false;
        for (int i = 0; i < shapes.Length; i++)
        {
            EquippableColliderShape shape = shapes[i];
            Vector3 halfSize = new Vector3(
                Mathf.Max(0.02f, shape.size.x),
                Mathf.Max(0.02f, shape.size.y),
                Mathf.Max(0.02f, shape.size.z)) * 0.5f;
            Quaternion rotation = Quaternion.Euler(shape.rotationEuler);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = new Vector3(
                    (corner & 1) == 0 ? -halfSize.x : halfSize.x,
                    (corner & 2) == 0 ? -halfSize.y : halfSize.y,
                    (corner & 4) == 0 ? -halfSize.z : halfSize.z);
                Vector3 point = shape.center + rotation * offset;
                if (!initialized)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }
        }

        return initialized;
    }
}
