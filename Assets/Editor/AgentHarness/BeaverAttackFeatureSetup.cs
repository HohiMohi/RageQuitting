using System;
using UnityEditor;
using UnityEngine;

public static class BeaverAttackFeatureSetup
{
    private const string ScoutPrefabPath = "Assets/Prefabs/New/NPC/NPC_BeaverScout.prefab";
    private const string DefenderPrefabPath = "Assets/Prefabs/New/NPC/NPC_BeaverDefender.prefab";
    private const string ScoutBehaviorPath = "Assets/ScriptableObjectAssets/New/NPC/BeaverScoutBehavior.asset";
    private const string DefenderBehaviorPath = "Assets/ScriptableObjectAssets/New/NPC/BeaverDefenderBehavior.asset";
    private const string LungeImpulsePath = "Assets/ScriptableObjectAssets/New/NPC/BeaverLungeImpulse.asset";

    [MenuItem("Tools/RageQuitting/Setup Beaver Attacks")]
    public static string Apply()
    {
        ExternalImpulseProfileSO impulse = AssetDatabase.LoadAssetAtPath<ExternalImpulseProfileSO>(LungeImpulsePath);
        if (impulse == null)
        {
            impulse = ScriptableObject.CreateInstance<ExternalImpulseProfileSO>();
            AssetDatabase.CreateAsset(impulse, LungeImpulsePath);
        }

        ConfigureImpulse(impulse);
        ConfigurePrefab(ScoutPrefabPath, 10f);
        ConfigurePrefab(DefenderPrefabPath, 15f);
        ConfigureScout(impulse);
        ConfigureDefender(impulse);
        AssetDatabase.SaveAssets();
        AssetDatabase.ForceReserializeAssets(
            new[] { ScoutPrefabPath, DefenderPrefabPath, ScoutBehaviorPath, DefenderBehaviorPath },
            ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata);
        AssetDatabase.Refresh();
        return BeaverAttackFeatureProbe.Validate();
    }

    private static void ConfigureImpulse(ExternalImpulseProfileSO impulse)
    {
        SerializedObject serialized = new SerializedObject(impulse);
        serialized.FindProperty("horizontalSpeed").floatValue = 3.5f;
        serialized.FindProperty("verticalSpeed").floatValue = 0.5f;
        serialized.FindProperty("horizontalDeceleration").floatValue = 10f;
        serialized.FindProperty("gravityMultiplier").floatValue = 1f;
        serialized.FindProperty("maximumDuration").floatValue = 0.45f;
        serialized.FindProperty("movementControlMultiplier").floatValue = 0.7f;
        serialized.FindProperty("maximumHorizontalSpeed").floatValue = 6f;
        serialized.FindProperty("maximumVerticalSpeed").floatValue = 2f;
        serialized.FindProperty("forceDropHeldObject").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(impulse);
    }

    private static void ConfigurePrefab(string path, float attackDamage)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponent<BeaverAttackSequenceController>() == null)
            {
                root.AddComponent<BeaverAttackSequenceController>();
            }

            NPCAttackController attack = root.GetComponent<NPCAttackController>();
            if (attack == null)
            {
                throw new InvalidOperationException($"Missing NPCAttackController on {path}.");
            }

            SerializedObject serializedAttack = new SerializedObject(attack);
            serializedAttack.FindProperty("attackDamage").floatValue = attackDamage;
            serializedAttack.FindProperty("attackDamageDelay").floatValue = 0.35f;
            serializedAttack.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureScout(ExternalImpulseProfileSO impulse)
    {
        BeaverScoutBehaviorSO asset = AssetDatabase.LoadAssetAtPath<BeaverScoutBehaviorSO>(ScoutBehaviorPath);
        if (asset == null)
        {
            throw new InvalidOperationException($"Missing {ScoutBehaviorPath}.");
        }

        SerializedObject serialized = new SerializedObject(asset);
        serialized.FindProperty("hitReactionLockDuration").floatValue = 0.4f;
        SetPattern(serialized.FindProperty("closeCombatAttack"), "Scout Strike", 1f, 0.15f,
            new[] { new BeaverStrikeConfig(1f, 1.5f, 0f) }, 0.5f);
        SetLunge(serialized.FindProperty("lungeAttack"), impulse);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    private static void ConfigureDefender(ExternalImpulseProfileSO impulse)
    {
        BeaverDefenderBehaviorSO asset = AssetDatabase.LoadAssetAtPath<BeaverDefenderBehaviorSO>(DefenderBehaviorPath);
        if (asset == null)
        {
            throw new InvalidOperationException($"Missing {DefenderBehaviorPath}.");
        }

        SerializedObject serialized = new SerializedObject(asset);
        SerializedProperty patterns = serialized.FindProperty("attackPatterns");
        patterns.arraySize = 3;
        SetPattern(patterns.GetArrayElementAtIndex(0), "Heavy", 1f, 0.15f,
            new[] { new BeaverStrikeConfig(1.5f, 1f, 0f) }, 0.65f);
        SetPattern(patterns.GetArrayElementAtIndex(1), "Double", 1f, 0.15f,
            new[]
            {
                new BeaverStrikeConfig(0.75f, 1.4f, 0.18f),
                new BeaverStrikeConfig(0.75f, 1.4f, 0f)
            }, 0.55f);
        SetPattern(patterns.GetArrayElementAtIndex(2), "Triple 2+1", 1f, 0.2f,
            new[]
            {
                new BeaverStrikeConfig(0.5f, 1.6f, 0.08f),
                new BeaverStrikeConfig(0.5f, 1.6f, 0.3f),
                new BeaverStrikeConfig(0.5f, 1.6f, 0f)
            }, 0.6f);
        SetLunge(serialized.FindProperty("lungeAttack"), impulse);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    private static void SetPattern(
        SerializedProperty property,
        string patternName,
        float weight,
        float prepareDuration,
        BeaverStrikeConfig[] strikes,
        float recoveryDuration)
    {
        property.FindPropertyRelative("name").stringValue = patternName;
        property.FindPropertyRelative("weight").floatValue = weight;
        property.FindPropertyRelative("prepareDuration").floatValue = prepareDuration;
        property.FindPropertyRelative("recoveryDuration").floatValue = recoveryDuration;
        SerializedProperty strikeArray = property.FindPropertyRelative("strikes");
        strikeArray.arraySize = strikes.Length;
        for (int i = 0; i < strikes.Length; i++)
        {
            SerializedProperty strike = strikeArray.GetArrayElementAtIndex(i);
            strike.FindPropertyRelative("damageMultiplier").floatValue = strikes[i].damageMultiplier;
            strike.FindPropertyRelative("animationSpeed").floatValue = strikes[i].animationSpeed;
            strike.FindPropertyRelative("gapAfterImpact").floatValue = strikes[i].gapAfterImpact;
        }
    }

    private static void SetLunge(SerializedProperty property, ExternalImpulseProfileSO impulse)
    {
        property.FindPropertyRelative("launchExtraRange").floatValue = 1f;
        property.FindPropertyRelative("telegraphDuration").floatValue = 0.15f;
        property.FindPropertyRelative("speed").floatValue = 5f;
        property.FindPropertyRelative("maxTravelDistance").floatValue = 1.2f;
        property.FindPropertyRelative("animationSpeed").floatValue = 1.5f;
        property.FindPropertyRelative("damageMultiplier").floatValue = 1f;
        property.FindPropertyRelative("recoveryDuration").floatValue = 0.5f;
        property.FindPropertyRelative("chaseRefreshInterval").floatValue = 0.1f;
        property.FindPropertyRelative("unreachableTargetTimeout").floatValue = 5f;
        property.FindPropertyRelative("collisionSkin").floatValue = 0.02f;
        property.FindPropertyRelative("obstacleLayers").intValue = ~0;
        property.FindPropertyRelative("impulseProfile").objectReferenceValue = impulse;
    }
}

public static class BeaverAttackFeatureProbe
{
    public static string Validate()
    {
        ValidatePrefab("Assets/Prefabs/New/NPC/NPC_BeaverScout.prefab", 10f);
        ValidatePrefab("Assets/Prefabs/New/NPC/NPC_BeaverDefender.prefab", 15f);

        BeaverScoutBehaviorSO scout = AssetDatabase.LoadAssetAtPath<BeaverScoutBehaviorSO>(
            "Assets/ScriptableObjectAssets/New/NPC/BeaverScoutBehavior.asset");
        BeaverDefenderBehaviorSO defender = AssetDatabase.LoadAssetAtPath<BeaverDefenderBehaviorSO>(
            "Assets/ScriptableObjectAssets/New/NPC/BeaverDefenderBehavior.asset");
        ExternalImpulseProfileSO impulse = AssetDatabase.LoadAssetAtPath<ExternalImpulseProfileSO>(
            "Assets/ScriptableObjectAssets/New/NPC/BeaverLungeImpulse.asset");
        if (scout == null || defender == null || impulse == null)
        {
            throw new InvalidOperationException("Missing beaver attack behavior or impulse asset.");
        }

        if (scout.CloseCombatAttack.StrikeCount != 1
            || defender.AttackPatterns == null
            || defender.AttackPatterns.Length != 3
            || scout.LungeAttack.ImpulseProfile != impulse
            || defender.LungeAttack.ImpulseProfile != impulse)
        {
            throw new InvalidOperationException("Beaver attack configuration or impulse references are incomplete.");
        }

        ValidateLungeConfiguration(scout.LungeAttack, impulse, "scout");
        ValidateLungeConfiguration(defender.LungeAttack, impulse, "defender");

        ExternalImpulseData impulseData = impulse.CreateImpulse(Vector3.forward);
        if (!Mathf.Approximately(impulseData.InitialVelocity.z, 3.5f)
            || !Mathf.Approximately(impulseData.InitialVelocity.y, 0.5f)
            || !Mathf.Approximately(impulseData.HorizontalDeceleration, 10f)
            || !Mathf.Approximately(impulseData.GravityMultiplier, 1f)
            || !Mathf.Approximately(impulseData.MaximumDuration, 0.45f)
            || !Mathf.Approximately(impulseData.MovementControlMultiplier, 0.7f)
            || !Mathf.Approximately(impulseData.MaximumHorizontalSpeed, 6f)
            || !Mathf.Approximately(impulseData.MaximumVerticalSpeed, 2f)
            || impulseData.ForceDropHeldObject)
        {
            throw new InvalidOperationException("Beaver lunge impulse profile values are incorrect.");
        }

        return "Beaver attack feature prefab and ScriptableObject wiring passed.";
    }

    public static string ValidateLungeConfiguration()
    {
        BeaverScoutBehaviorSO scout = AssetDatabase.LoadAssetAtPath<BeaverScoutBehaviorSO>(
            "Assets/ScriptableObjectAssets/New/NPC/BeaverScoutBehavior.asset");
        BeaverDefenderBehaviorSO defender = AssetDatabase.LoadAssetAtPath<BeaverDefenderBehaviorSO>(
            "Assets/ScriptableObjectAssets/New/NPC/BeaverDefenderBehavior.asset");
        ExternalImpulseProfileSO impulse = AssetDatabase.LoadAssetAtPath<ExternalImpulseProfileSO>(
            "Assets/ScriptableObjectAssets/New/NPC/BeaverLungeImpulse.asset");
        if (scout == null || defender == null || impulse == null)
        {
            throw new InvalidOperationException("Missing beaver lunge assets.");
        }

        ValidateLungeConfiguration(scout.LungeAttack, impulse, "scout");
        ValidateLungeConfiguration(defender.LungeAttack, impulse, "defender");
        return "Beaver lunge serialized defaults and wiring passed.";
    }

    private static void ValidateLungeConfiguration(
        BeaverLungeConfig lunge,
        ExternalImpulseProfileSO expectedImpulse,
        string label)
    {
        if (!Mathf.Approximately(lunge.LaunchExtraRange, 1f)
            || !Mathf.Approximately(lunge.TelegraphDuration, 0.15f)
            || !Mathf.Approximately(lunge.Speed, 5f)
            || !Mathf.Approximately(lunge.MaxTravelDistance, 1.2f)
            || !Mathf.Approximately(lunge.AnimationSpeed, 1.5f)
            || !Mathf.Approximately(lunge.DamageMultiplier, 1f)
            || !Mathf.Approximately(lunge.RecoveryDuration, 0.5f)
            || !Mathf.Approximately(lunge.ChaseRefreshInterval, 0.1f)
            || !Mathf.Approximately(lunge.UnreachableTargetTimeout, 5f)
            || !Mathf.Approximately(lunge.CollisionSkin, 0.02f)
            || lunge.ObstacleLayers != ~0
            || lunge.ImpulseProfile != expectedImpulse)
        {
            throw new InvalidOperationException($"Invalid {label} lunge serialized defaults or wiring.");
        }
    }

    private static void ValidatePrefab(string path, float expectedDamage)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null
            || prefab.GetComponent<BeaverAttackSequenceController>() == null
            || prefab.GetComponent<NPCAttackController>() is not NPCAttackController attack
            || !Mathf.Approximately(attack.AttackDamage, expectedDamage)
            || !Mathf.Approximately(attack.AttackDamageDelay, 0.35f))
        {
            throw new InvalidOperationException($"Invalid beaver attack prefab wiring: {path}.");
        }
    }
}
