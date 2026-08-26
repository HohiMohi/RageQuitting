#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

internal static class SpiritLevelScaleMigration
{
    private const string ProfilePath = "Assets/ScriptableObjectAssets/New/SpiritLevelProfile.asset";
    private const string PrefabPath = "Assets/Prefabs/New/EquippableItems/SpiritLevel.prefab";

    [MenuItem("Tools/RageQuitting/Spirit Level/Rebuild Readout Scale")]
    private static void RunFromMenu() => RunMigration();

    private static void RunMigration()
    {
        try
        {
            SpiritLevelProfileSO profile = AssetDatabase.LoadAssetAtPath<SpiritLevelProfileSO>(ProfilePath);
            if (profile == null) throw new InvalidOperationException($"Missing profile at {ProfilePath}.");

            profile.bubbleToGreenMarkClearance = 0.001f;
            profile.bubbleEndMargin = 0.002f;
            profile.markThickness = 0.012f;
            profile.markLength = 0.11f;
            profile.greenMarkColor = new Color(0.18f, 0.95f, 0.25f, 1f);
            profile.yellowMarkColor = new Color(1f, 0.72f, 0.05f, 1f);
            EditorUtility.SetDirty(profile);

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                SpiritLevelVial vial = root.GetComponentInChildren<SpiritLevelVial>(true);
                Transform visualRoot = vial != null ? vial.transform.parent : null;
                if (visualRoot == null || vial == null)
                    throw new InvalidOperationException("SpiritLevelVial is missing from the prefab.");

                Transform cutout = FindDeep(visualRoot, "VialCutout");
                Transform liquid = FindDeep(visualRoot, "Liquid");
                if (cutout != null) cutout.localScale = new Vector3(0.38f, cutout.localScale.y, cutout.localScale.z);
                if (liquid != null) liquid.localScale = new Vector3(liquid.localScale.x, 0.18f, liquid.localScale.z);

                Transform greenLeft = FindDeep(visualRoot, "GreenMarkLeft") ?? FindDeep(visualRoot, "CenterMarkLeft");
                Transform greenRight = FindDeep(visualRoot, "GreenMarkRight") ?? FindDeep(visualRoot, "CenterMarkRight");
                if (greenLeft == null || greenRight == null)
                    throw new InvalidOperationException("The existing center marks are missing from the prefab.");

                greenLeft.name = "GreenMarkLeft";
                greenRight.name = "GreenMarkRight";
                Transform yellowLeft = FindDeep(visualRoot, "YellowMarkLeft") ?? DuplicateMark(greenLeft, "YellowMarkLeft");
                Transform yellowRight = FindDeep(visualRoot, "YellowMarkRight") ?? DuplicateMark(greenRight, "YellowMarkRight");

                SerializedObject serializedVial = new SerializedObject(vial);
                serializedVial.FindProperty("greenMarkLeft").objectReferenceValue = greenLeft;
                serializedVial.FindProperty("greenMarkRight").objectReferenceValue = greenRight;
                serializedVial.FindProperty("yellowMarkLeft").objectReferenceValue = yellowLeft;
                serializedVial.FindProperty("yellowMarkRight").objectReferenceValue = yellowRight;
                serializedVial.ApplyModifiedPropertiesWithoutUndo();

                vial.Configure(profile);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Validate(profile);
            WriteReport("PASS: Spirit level scale assets migrated and validated.");
            Debug.Log("Spirit level scale assets migrated and validated.");
        }
        catch (Exception exception)
        {
            WriteReport("FAIL: " + exception);
            Debug.LogException(exception);
        }
    }

    private static void Validate(SpiritLevelProfileSO profile)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            SpiritLevelVial vial = root.GetComponentInChildren<SpiritLevelVial>(true);
            if (vial == null) throw new InvalidOperationException("Saved prefab has no SpiritLevelVial.");
            vial.Configure(profile);

            Type type = typeof(SpiritLevelVial);
            MethodInfo evaluate = type.GetMethod("EvaluateMeasurementOffset", BindingFlags.Instance | BindingFlags.NonPublic);
            float diameter = ReadFloat(type, vial, "bubbleDiameter");
            float green = ReadFloat(type, vial, "greenMarkOffset");
            float yellow = ReadFloat(type, vial, "yellowMarkOffset");
            float maximum = ReadFloat(type, vial, "maximumBubbleOffset");
            if (evaluate == null || diameter <= 0f) throw new InvalidOperationException("Scale geometry was not initialized.");

            AssertNear(Evaluate(evaluate, vial, 0f), 0f, "tilt 0");
            AssertNear(Evaluate(evaluate, vial, 1f), green, "tilt 1");
            AssertNear(Evaluate(evaluate, vial, 2f), green + diameter * 0.25f, "tilt 2");
            AssertNear(Evaluate(evaluate, vial, 3f), (green + yellow) * 0.5f, "tilt 3");
            AssertNear(Evaluate(evaluate, vial, 4f), yellow - diameter * 0.25f, "tilt 4");
            AssertNear(Evaluate(evaluate, vial, 5f), yellow, "tilt 5");
            AssertNear(Evaluate(evaluate, vial, 6f), yellow + diameter * 0.25f, "tilt 6");
            AssertNear(Evaluate(evaluate, vial, 7f), maximum, "tilt 7");
            AssertNear(Evaluate(evaluate, vial, 8f), maximum, "tilt 8");
            AssertNear(Evaluate(evaluate, vial, -5f), -yellow, "negative mirror");
            AssertNear(yellow - green, green * 2f, "equal marker spacing");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform DuplicateMark(Transform source, string name)
    {
        Transform copy = UnityEngine.Object.Instantiate(source.gameObject, source.parent).transform;
        copy.name = name;
        return copy;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindDeep(root.GetChild(i), name);
            if (result != null) return result;
        }
        return null;
    }

    private static float Evaluate(MethodInfo method, SpiritLevelVial vial, float value) =>
        (float)method.Invoke(vial, new object[] { value });

    private static float ReadFloat(Type type, object instance, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null ? (float)field.GetValue(instance) : 0f;
    }

    private static void AssertNear(float actual, float expected, string label)
    {
        if (Mathf.Abs(actual - expected) > 0.0001f)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }

    private static void WriteReport(string contents)
    {
        string path = Path.GetFullPath("Temp/SpiritLevelScaleMigration.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, contents);
    }
}
#endif
