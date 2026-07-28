#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class GoatScaffoldingEditor
{
    private const string NpcAssetFolder = "Assets/ScriptableObjectAssets/New/NPC";
    private const string NpcPrefabFolder = "Assets/Prefabs/New/NPC";
    private const string FactionPath = NpcAssetFolder + "/NeutralAnimalsFaction.asset";
    private const string BehaviorPath = NpcAssetFolder + "/GoatBehavior.asset";
    private const string StandingProfilePath = NpcAssetFolder + "/GoatStandingTargetProfile.asset";
    private const string PushImpulseProfilePath = NpcAssetFolder + "/GoatPushImpulse.asset";
    private const string DefinitionPath = NpcAssetFolder + "/GoatDefinition.asset";
    private const string MaterialPath = NpcPrefabFolder + "/GoatPlaceholderMaterial.mat";
    private const string PrefabPath = NpcPrefabFolder + "/NPC_Goat.prefab";
    private const string BasePrefabPath = NpcPrefabFolder + "/NPC_Base.prefab";
    private const string RelationshipMatrixPath = NpcAssetFolder + "/NPCFactionRelationshipMatrix.asset";
    private const string PlayerFactionPath = NpcAssetFolder + "/PlayerFaction.asset";
    private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
    private const string TutorialScenePath = "Assets/Scenes/Tutorial_scene.unity";
    private const string IronVeinPath = "Assets/ScriptableObjectAssets/New/BaseResourceSO/IronVein.asset";
    private const string CoalVeinPath = "Assets/ScriptableObjectAssets/New/BaseResourceSO/Coal.asset";

    static GoatScaffoldingEditor()
    {
        EditorApplication.delayCall += TryBuildAutomatically;
    }

    [MenuItem("Tools/RageQuitting/Rebuild Goat Scaffold")]
    public static void RebuildFromMenu()
    {
        BuildScaffold(true);
    }

    private static void TryBuildAutomatically()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += TryBuildAutomatically;
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Transform visualRoot = prefab != null ? prefab.transform.Find("VisualRoot") : null;
        GoatStandingTargetProfileSO standingProfile =
            AssetDatabase.LoadAssetAtPath<GoatStandingTargetProfileSO>(StandingProfilePath);
        GoatBehaviorSO behavior = AssetDatabase.LoadAssetAtPath<GoatBehaviorSO>(BehaviorPath);
        SerializedObject serializedBehavior = behavior != null ? new SerializedObject(behavior) : null;
        bool missingStandingProfileReference = serializedBehavior == null
            || serializedBehavior.FindProperty("standingTargetProfile")?.objectReferenceValue == null;
        if (prefab == null
            || visualRoot == null
            || Mathf.Abs(visualRoot.localScale.x - 0.78f) > 0.001f
            || prefab.GetComponent<GoatChargeController>() == null
            || standingProfile == null
            || missingStandingProfileReference)
        {
            BuildScaffold(prefab != null);
        }
    }

    private static void BuildScaffold(bool rebuildPrefab)
    {
        NPCFactionSO faction = GetOrCreateFaction();
        GoatStandingTargetProfileSO standingProfile = GetOrCreateStandingTargetProfile();
        GoatBehaviorSO behavior = GetOrCreateBehavior(standingProfile);
        NPCDefinitionSO definition = GetOrCreateDefinition(faction, behavior);
        Material material = GetOrCreateMaterial();
        GameObject goatPrefab = CreateOrUpdateGoatPrefab(definition, faction, material, rebuildPrefab);

        if (goatPrefab == null)
        {
            Debug.LogError("Goat scaffold could not create NPC_Goat.prefab.");
            return;
        }

        SetObjectReference(definition, "npcPrefabOverride", goatPrefab);
        EditorUtility.SetDirty(definition);
        RegisterNetworkPrefab(goatPrefab);
        AddTutorialObjects(goatPrefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Goat scaffold is ready.");
    }

    private static NPCFactionSO GetOrCreateFaction()
    {
        NPCFactionSO faction = AssetDatabase.LoadAssetAtPath<NPCFactionSO>(FactionPath);
        if (faction == null)
        {
            faction = ScriptableObject.CreateInstance<NPCFactionSO>();
            faction.name = "NeutralAnimalsFaction";
            AssetDatabase.CreateAsset(faction, FactionPath);
        }

        SerializedObject serialized = new SerializedObject(faction);
        serialized.FindProperty("factionId").stringValue = "NeutralAnimals";
        serialized.FindProperty("displayName").stringValue = "Neutral Animals";
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return faction;
    }

    private static GoatStandingTargetProfileSO GetOrCreateStandingTargetProfile()
    {
        GoatStandingTargetProfileSO profile =
            AssetDatabase.LoadAssetAtPath<GoatStandingTargetProfileSO>(StandingProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<GoatStandingTargetProfileSO>();
            profile.name = "GoatStandingTargetProfile";
            AssetDatabase.CreateAsset(profile, StandingProfilePath);
        }

        SerializedObject serialized = new SerializedObject(profile);
        SerializedProperty resources = serialized.FindProperty("allowedResources");
        resources.arraySize = 2;
        resources.GetArrayElementAtIndex(0).objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<BaseResourceSO>(IronVeinPath);
        resources.GetArrayElementAtIndex(1).objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<BaseResourceSO>(CoalVeinPath);
        serialized.FindProperty("allowAllMountableBridgeComponents").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static GoatBehaviorSO GetOrCreateBehavior(GoatStandingTargetProfileSO standingProfile)
    {
        GoatBehaviorSO behavior = AssetDatabase.LoadAssetAtPath<GoatBehaviorSO>(BehaviorPath);
        if (behavior == null)
        {
            behavior = ScriptableObject.CreateInstance<GoatBehaviorSO>();
            behavior.name = "GoatBehavior";
            AssetDatabase.CreateAsset(behavior, BehaviorPath);
        }

        SetObjectReference(behavior, "playerFaction", AssetDatabase.LoadAssetAtPath<NPCFactionSO>(PlayerFactionPath));
        SetObjectReference(behavior, "standingTargetProfile", standingProfile);
        SerializedObject serialized = new SerializedObject(behavior);
        serialized.FindProperty("standingSearchRadius").floatValue = 10f;
        serialized.FindProperty("standingSearchInterval").floatValue = 0.5f;
        serialized.FindProperty("standingDuration").floatValue = 15f;
        serialized.FindProperty("jumpDuration").floatValue = 0.65f;
        serialized.FindProperty("jumpArcHeight").floatValue = 0.65f;
        serialized.FindProperty("proximityThreatRange").floatValue = 6f;
        serialized.FindProperty("proximityThreatDuration").floatValue = 2f;
        serialized.FindProperty("chargeTelegraphDuration").floatValue = 1.2f;
        serialized.FindProperty("chargeMaxSpeed").floatValue = 13.5f;
        serialized.FindProperty("chargeAcceleration").floatValue = 9f;
        serialized.FindProperty("chargeSteeringDegreesPerSecond").floatValue = 360f;
        serialized.FindProperty("chargeCommittedDuration").floatValue = 2.25f;
        serialized.FindProperty("chargeDeceleration").floatValue = 18f;
        serialized.FindProperty("chargeDamage").floatValue = 20f;
        serialized.FindProperty("chargeCooldown").floatValue = 5f;
        serialized.FindProperty("chargeCollisionSkin").floatValue = 0.05f;
        serialized.FindProperty("chargeBlockedRecoveryDuration").floatValue = 0.35f;
        serialized.FindProperty("pushZoneSearchRadius").floatValue = 30f;
        serialized.FindProperty("pushZoneSearchInterval").floatValue = 0.5f;
        serialized.FindProperty("pushApproachDistance").floatValue = 0.75f;
        serialized.FindProperty("pushSetupDistance").floatValue = 1.1f;
        serialized.FindProperty("pushPositionUpdateInterval").floatValue = 0.2f;
        serialized.FindProperty("pushPositionTolerance").floatValue = 0.2f;
        serialized.FindProperty("pushFacingToleranceDegrees").floatValue = 10f;
        serialized.FindProperty("pushRecoveryDuration").floatValue = 0.75f;
        serialized.FindProperty("pushAttemptCooldown").floatValue = 5f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(behavior);
        return behavior;
    }

    private static NPCDefinitionSO GetOrCreateDefinition(NPCFactionSO faction, GoatBehaviorSO behavior)
    {
        NPCDefinitionSO definition = AssetDatabase.LoadAssetAtPath<NPCDefinitionSO>(DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<NPCDefinitionSO>();
            definition.name = "GoatDefinition";
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }

        SerializedObject serialized = new SerializedObject(definition);
        serialized.FindProperty("npcName").stringValue = "Goat";
        serialized.FindProperty("faction").objectReferenceValue = faction;
        serialized.FindProperty("behavior").objectReferenceValue = behavior;
        serialized.FindProperty("maxHealth").floatValue = 45f;
        serialized.FindProperty("moveSpeed").floatValue = 3.2f;
        serialized.FindProperty("acceleration").floatValue = 9f;
        serialized.FindProperty("angularSpeed").floatValue = 360f;
        serialized.FindProperty("decisionTickInterval").floatValue = 0.2f;
        serialized.FindProperty("detectionRadius").floatValue = 10f;
        serialized.FindProperty("interactionDistance").floatValue = 1.4f;
        serialized.FindProperty("patrolRadius").floatValue = 8f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return definition;
    }

    private static Material GetOrCreateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        shader ??= Shader.Find("Standard");
        material = new Material(shader)
        {
            name = "GoatPlaceholderMaterial",
            color = new Color(0.78f, 0.75f, 0.67f)
        };
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    private static GameObject CreateOrUpdateGoatPrefab(
        NPCDefinitionSO definition,
        NPCFactionSO faction,
        Material material,
        bool rebuildPrefab)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null && !rebuildPrefab)
        {
            return existing;
        }

        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
        if (basePrefab == null)
        {
            Debug.LogError($"Missing base NPC prefab at {BasePrefabPath}.");
            return null;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(BasePrefabPath);
        try
        {
            root.name = "NPC_Goat";
            NPCBrain brain = root.GetComponent<NPCBrain>();
            SetObjectReference(brain, "definition", definition);
            SetObjectReference(
                brain,
                "relationshipMatrix",
                AssetDatabase.LoadAssetAtPath<NPCFactionRelationshipMatrixSO>(RelationshipMatrixPath));

            NPCFactionMember factionMember = root.GetComponent<NPCFactionMember>();
            SetObjectReference(factionMember, "faction", faction);

            NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 1.25f;
            agent.baseOffset = 0f;

            CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.radius = 0.4f;
                capsule.height = 1.25f;
                capsule.center = new Vector3(0f, 0.625f, 0f);
            }

            Transform visualRoot = root.transform.Find("VisualRoot");
            if (visualRoot == null)
            {
                GameObject visualRootObject = new GameObject("VisualRoot");
                visualRoot = visualRootObject.transform;
                visualRoot.SetParent(root.transform, false);
                SetObjectReference(brain, "visualRoot", visualRoot);
            }

            visualRoot.localScale = Vector3.one * 0.78f;

            GoatChargeController chargeController = root.GetComponent<GoatChargeController>();
            if (chargeController == null)
            {
                chargeController = root.AddComponent<GoatChargeController>();
            }
            SetObjectReference(chargeController, "visualRoot", visualRoot);

            for (int i = visualRoot.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(visualRoot.GetChild(i).gameObject);
            }

            BuildGoatVisual(visualRoot, material);
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildGoatVisual(Transform parent, Material material)
    {
        CreatePrimitive("Body", PrimitiveType.Cube, parent, new Vector3(0f, 0.95f, 0f), new Vector3(0.8f, 0.65f, 1.45f), Vector3.zero, material);
        CreatePrimitive("Neck", PrimitiveType.Cube, parent, new Vector3(0f, 1.25f, 0.55f), new Vector3(0.48f, 0.65f, 0.42f), new Vector3(-18f, 0f, 0f), material);
        CreatePrimitive("Head", PrimitiveType.Cube, parent, new Vector3(0f, 1.55f, 0.82f), new Vector3(0.55f, 0.48f, 0.62f), Vector3.zero, material);
        CreatePrimitive("Snout", PrimitiveType.Cube, parent, new Vector3(0f, 1.47f, 1.18f), new Vector3(0.42f, 0.3f, 0.35f), Vector3.zero, material);

        CreatePrimitive("FrontLeftLeg", PrimitiveType.Cube, parent, new Vector3(-0.25f, 0.42f, 0.48f), new Vector3(0.18f, 0.85f, 0.18f), Vector3.zero, material);
        CreatePrimitive("FrontRightLeg", PrimitiveType.Cube, parent, new Vector3(0.25f, 0.42f, 0.48f), new Vector3(0.18f, 0.85f, 0.18f), Vector3.zero, material);
        CreatePrimitive("BackLeftLeg", PrimitiveType.Cube, parent, new Vector3(-0.25f, 0.42f, -0.48f), new Vector3(0.18f, 0.85f, 0.18f), Vector3.zero, material);
        CreatePrimitive("BackRightLeg", PrimitiveType.Cube, parent, new Vector3(0.25f, 0.42f, -0.48f), new Vector3(0.18f, 0.85f, 0.18f), Vector3.zero, material);

        CreatePrimitive("LeftHorn", PrimitiveType.Cylinder, parent, new Vector3(-0.2f, 1.9f, 0.83f), new Vector3(0.12f, 0.32f, 0.12f), new Vector3(-20f, 0f, -12f), material);
        CreatePrimitive("RightHorn", PrimitiveType.Cylinder, parent, new Vector3(0.2f, 1.9f, 0.83f), new Vector3(0.12f, 0.32f, 0.12f), new Vector3(-20f, 0f, 12f), material);
    }

    private static void CreatePrimitive(
        string name,
        PrimitiveType type,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Vector3 localEuler,
        Material material)
    {
        GameObject primitive = GameObject.CreatePrimitive(type);
        primitive.name = name;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localRotation = Quaternion.Euler(localEuler);
        primitive.transform.localScale = localScale;

        Collider collider = primitive.GetComponent<Collider>();
        if (collider != null)
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }

        Renderer renderer = primitive.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static void RegisterNetworkPrefab(GameObject goatPrefab)
    {
        UnityEngine.Object networkPrefabs = AssetDatabase.LoadMainAssetAtPath(NetworkPrefabsPath);
        if (networkPrefabs == null)
        {
            Debug.LogError($"Missing network prefab list at {NetworkPrefabsPath}.");
            return;
        }

        SerializedObject serialized = new SerializedObject(networkPrefabs);
        SerializedProperty list = serialized.FindProperty("List");
        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == goatPrefab)
            {
                return;
            }
        }

        int index = list.arraySize;
        list.InsertArrayElementAtIndex(index);
        SerializedProperty entry = list.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("Override").boolValue = false;
        entry.FindPropertyRelative("Prefab").objectReferenceValue = goatPrefab;
        entry.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
        entry.FindPropertyRelative("SourceHashToOverride").longValue = 0;
        entry.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(networkPrefabs);
    }

    private static void AddTutorialObjects(GameObject goatPrefab)
    {
        Scene scene = SceneManager.GetSceneByPath(TutorialScenePath);
        bool openedByTool = !scene.IsValid() || !scene.isLoaded;
        if (openedByTool)
        {
            scene = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Additive);
        }

        try
        {
            GameObject tutorialGoat = FindSceneObject(scene, "Tutorial_Goat");
            if (tutorialGoat == null)
            {
                tutorialGoat = (GameObject)PrefabUtility.InstantiatePrefab(goatPrefab, scene);
                tutorialGoat.name = "Tutorial_Goat";
            }
            tutorialGoat.transform.SetPositionAndRotation(new Vector3(-18f, 0f, 16f), Quaternion.identity);

            GameObject pushZoneObject = FindSceneObject(scene, "GoatPushZone_Test");
            if (pushZoneObject == null)
            {
                pushZoneObject = new GameObject("GoatPushZone_Test");
                SceneManager.MoveGameObjectToScene(pushZoneObject, scene);
                pushZoneObject.transform.SetPositionAndRotation(new Vector3(1f, 0.75f, 8f), Quaternion.LookRotation(Vector3.right));

                BoxCollider box = pushZoneObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(4f, 1.5f, 3f);

                GameObject approach = new GameObject("GoatApproachPoint");
                approach.transform.SetParent(pushZoneObject.transform, false);
                approach.transform.localPosition = new Vector3(0f, -0.75f, -2f);
            }

            GoatPushZone zone = pushZoneObject.GetComponent<GoatPushZone>();
            zone ??= pushZoneObject.AddComponent<GoatPushZone>();
            Transform approachPoint = pushZoneObject.transform.Find("GoatApproachPoint");
            SetObjectReference(zone, "approachPoint", approachPoint);
            SetObjectReference(
                zone,
                "pushImpulseProfile",
                AssetDatabase.LoadAssetAtPath<ExternalImpulseProfileSO>(PushImpulseProfilePath));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedByTool)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform candidate in transforms)
            {
                if (candidate.name == objectName)
                {
                    return candidate.gameObject;
                }
            }
        }

        return null;
    }

    private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        if (target == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"Property '{propertyName}' was not found on {target.name}.");
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
#endif
