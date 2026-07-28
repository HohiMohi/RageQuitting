#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ExternalImpulseScaffoldingEditor
{
    private const string ProfilePath = "Assets/ScriptableObjectAssets/New/NPC/GoatChargeImpulse.asset";
    private const string PushProfilePath = "Assets/ScriptableObjectAssets/New/NPC/GoatPushImpulse.asset";
    private const string GoatBehaviorPath = "Assets/ScriptableObjectAssets/New/NPC/GoatBehavior.asset";
    private const string TutorialScenePath = "Assets/Scenes/Tutorial_scene.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/PlayerNew.prefab";
    private const string NpcBasePrefabPath = "Assets/Prefabs/New/NPC/NPC_Base.prefab";
    private const string BeaverPrefabPath = "Assets/Prefabs/New/NPC/NPC_BeaverScout.prefab";
    private const string GoatPrefabPath = "Assets/Prefabs/New/NPC/NPC_Goat.prefab";

    static ExternalImpulseScaffoldingEditor()
    {
        EditorApplication.delayCall += ConfigureAutomatically;
    }

    [MenuItem("Tools/RageQuitting/Configure External Impulse System")]
    public static void ConfigureFromMenu()
    {
        Configure(true);
    }

    private static void ConfigureAutomatically()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += ConfigureAutomatically;
            return;
        }

        ExternalImpulseProfileSO profile = AssetDatabase.LoadAssetAtPath<ExternalImpulseProfileSO>(ProfilePath);
        ExternalImpulseProfileSO pushProfile = AssetDatabase.LoadAssetAtPath<ExternalImpulseProfileSO>(PushProfilePath);
        GoatBehaviorSO behavior = AssetDatabase.LoadAssetAtPath<GoatBehaviorSO>(GoatBehaviorPath);
        bool missingReference = true;
        if (behavior != null)
        {
            SerializedObject serialized = new SerializedObject(behavior);
            missingReference = serialized.FindProperty("chargeImpulseProfile")?.objectReferenceValue == null;
        }

        if (profile == null
            || pushProfile == null
            || missingReference
            || !PrefabHasComponent<PlayerExternalImpulseController>(PlayerPrefabPath)
            || !PrefabHasComponent<NPCExternalImpulseController>(NpcBasePrefabPath)
            || !PrefabHasComponent<NPCExternalImpulseController>(BeaverPrefabPath)
            || !PrefabHasComponent<NPCExternalImpulseController>(GoatPrefabPath))
        {
            Configure(false);
        }
    }

    private static void Configure(bool logCompletion)
    {
        ExternalImpulseProfileSO profile = GetOrCreateProfile();
        ExternalImpulseProfileSO pushProfile = GetOrCreatePushProfile();
        AssignProfile(profile);
        ConfigurePushBehavior();
        AssignPushProfileToTutorialZones(pushProfile);
        EnsureComponent<PlayerExternalImpulseController>(PlayerPrefabPath);
        EnsureComponent<NPCExternalImpulseController>(NpcBasePrefabPath);
        EnsureComponent<NPCExternalImpulseController>(BeaverPrefabPath);
        EnsureComponent<NPCExternalImpulseController>(GoatPrefabPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (logCompletion)
        {
            Debug.Log("External impulse system configured.");
        }
    }

    private static ExternalImpulseProfileSO GetOrCreateProfile()
    {
        ExternalImpulseProfileSO profile = AssetDatabase.LoadAssetAtPath<ExternalImpulseProfileSO>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<ExternalImpulseProfileSO>();
            profile.name = "GoatChargeImpulse";
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        SerializedObject serialized = new SerializedObject(profile);
        serialized.FindProperty("horizontalSpeed").floatValue = 10f;
        serialized.FindProperty("verticalSpeed").floatValue = 7f;
        serialized.FindProperty("horizontalDeceleration").floatValue = 12.5f;
        serialized.FindProperty("gravityMultiplier").floatValue = 1f;
        serialized.FindProperty("maximumDuration").floatValue = 2.5f;
        serialized.FindProperty("movementControlMultiplier").floatValue = 0.35f;
        serialized.FindProperty("maximumHorizontalSpeed").floatValue = 16f;
        serialized.FindProperty("maximumVerticalSpeed").floatValue = 10f;
        serialized.FindProperty("forceDropHeldObject").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static ExternalImpulseProfileSO GetOrCreatePushProfile()
    {
        ExternalImpulseProfileSO profile =
            AssetDatabase.LoadAssetAtPath<ExternalImpulseProfileSO>(PushProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<ExternalImpulseProfileSO>();
            profile.name = "GoatPushImpulse";
            AssetDatabase.CreateAsset(profile, PushProfilePath);
        }

        SerializedObject serialized = new SerializedObject(profile);
        serialized.FindProperty("horizontalSpeed").floatValue = 10f;
        serialized.FindProperty("verticalSpeed").floatValue = 3f;
        serialized.FindProperty("horizontalDeceleration").floatValue = 12.5f;
        serialized.FindProperty("gravityMultiplier").floatValue = 1f;
        serialized.FindProperty("maximumDuration").floatValue = 2f;
        serialized.FindProperty("movementControlMultiplier").floatValue = 0.35f;
        serialized.FindProperty("maximumHorizontalSpeed").floatValue = 16f;
        serialized.FindProperty("maximumVerticalSpeed").floatValue = 8f;
        serialized.FindProperty("forceDropHeldObject").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void AssignProfile(ExternalImpulseProfileSO profile)
    {
        GoatBehaviorSO behavior = AssetDatabase.LoadAssetAtPath<GoatBehaviorSO>(GoatBehaviorPath);
        if (behavior == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(behavior);
        serialized.FindProperty("chargeImpulseProfile").objectReferenceValue = profile;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(behavior);
    }

    private static void ConfigurePushBehavior()
    {
        GoatBehaviorSO behavior = AssetDatabase.LoadAssetAtPath<GoatBehaviorSO>(GoatBehaviorPath);
        if (behavior == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(behavior);
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
    }

    private static void AssignPushProfileToTutorialZones(ExternalImpulseProfileSO profile)
    {
        Scene scene = SceneManager.GetSceneByPath(TutorialScenePath);
        bool openedByTool = !scene.IsValid() || !scene.isLoaded;
        if (openedByTool)
        {
            scene = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Additive);
        }

        try
        {
            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GoatPushZone[] zones = root.GetComponentsInChildren<GoatPushZone>(true);
                foreach (GoatPushZone zone in zones)
                {
                    SerializedObject serialized = new SerializedObject(zone);
                    SerializedProperty property = serialized.FindProperty("pushImpulseProfile");
                    if (property != null && property.objectReferenceValue != profile)
                    {
                        property.objectReferenceValue = profile;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(zone);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            if (openedByTool)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static bool PrefabHasComponent<T>(string path) where T : Component
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        return prefab != null && prefab.GetComponent<T>() != null;
    }

    private static void EnsureComponent<T>(string path) where T : Component
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null || prefab.GetComponent<T>() != null)
        {
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponent<T>() == null)
            {
                root.AddComponent<T>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
