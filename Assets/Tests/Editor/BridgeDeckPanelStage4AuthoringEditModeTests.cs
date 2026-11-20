using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BridgeDeckPanelStage4AuthoringEditModeTests
{
    private const string ScenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
    private Scene originalActiveScene;
    private Scene targetScene;
    private bool openedScene;
    private bool targetSceneWasDirty;
    private GameObject nailStateFixture;
    private BridgeAssemblyTestFixtureUtility fixtureScenes;

    [SetUp]
    public void SetUp()
    {
        openedScene = false;
        originalActiveScene = SceneManager.GetActiveScene();
        targetScene = SceneManager.GetSceneByPath(ScenePath);
        targetSceneWasDirty = targetScene.IsValid() && targetScene.isLoaded && targetScene.isDirty;
        if (!targetScene.IsValid() || !targetScene.isLoaded)
        {
            openedScene = true;
            targetScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }
        fixtureScenes = new BridgeAssemblyTestFixtureUtility(includeProductionPreview: false);
    }

    [TearDown]
    public void TearDown()
    {
        if (nailStateFixture != null) UnityEngine.Object.DestroyImmediate(nailStateFixture);
        if (openedScene && targetScene.IsValid() && targetScene.isLoaded)
            EditorSceneManager.CloseScene(targetScene, true);
        fixtureScenes?.Dispose();
        if (targetScene.IsValid() && targetScene.isLoaded)
            Assert.That(targetScene.isDirty, Is.EqualTo(targetSceneWasDirty), "Preserve the production scene dirty state.");
        if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(originalActiveScene.handle), "Preserve the test runner's active scene.");
    }

    [Test]
    public void MainSectionHasThreePhysicalSlotsAndTwoResolvableUniqueGirderTargets()
    {
        Type authoring = Type.GetType("BridgeDeckPanelStage4Authoring, Assembly-CSharp-Editor");
        Assert.That(authoring, Is.Not.Null);
        string validation = (string)authoring.GetMethod("ValidateOpenTutorialAuthoring", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, null);
        Assert.That(validation, Does.Contain("3 main slots"));

        Component main = FindComponent(targetScene, "TutorialGameplay/Bridge/BridgeDeckSection", "BridgeDeckSection");
        Component nailTest = fixtureScenes.FixtureRoot.GetComponentInChildren(TypeByName("BridgeDeckSection"), true) as Component;
        Assert.That(main, Is.Not.Null);
        Assert.That(nailTest, Is.Not.Null);
        PropertyInfo slotsProperty = main.GetType().GetProperty("PanelSlots");
        PropertyInfo girdersProperty = main.GetType().GetProperty("MainGirders");
        IList slots = (IList)slotsProperty.GetValue(main);
        IList girders = (IList)girdersProperty.GetValue(main);
        Assert.That(slots.Count, Is.EqualTo(3));
        Assert.That(girders.Count, Is.EqualTo(2));
        Assert.That(main.GetType().GetProperty("PhysicalPanelState").GetValue(main),
            Is.Not.SameAs(nailTest.GetType().GetProperty("PhysicalPanelState").GetValue(nailTest)));
        for (int i = 0; i < slots.Count; i++)
        {
            Component site = (Component)slots[i];
            Assert.That(site.GetType().GetProperty("DeckSection").GetValue(site), Is.SameAs(main));
            Assert.That(site.GetType().GetProperty("SlotIndex").GetValue(site), Is.EqualTo(i));
            Assert.That(site.GetType().GetProperty("UsesPhysicalPanelPlacement").GetValue(site), Is.True);
        }

        nailStateFixture = new GameObject("Stage 4 nail target resolution fixture");
        SceneManager.MoveGameObjectToScene(nailStateFixture, targetScene);
        Component nailState = nailStateFixture.AddComponent(TypeByName("MountingNailSceneState"));
        Type identityType = TypeByName("MountingNailSceneTargetIdentity");
        Type referenceType = TypeByName("MountingNailTargetReference");
        MethodInfo tryCreateReference = nailState.GetType().GetMethod("TryCreateReference");
        MethodInfo tryResolve = nailState.GetType().GetMethod("TryResolve");
        string[] stableIds = new string[girders.Count];
        for (int i = 0; i < girders.Count; i++)
        {
            Component girder = (Component)girders[i];
            Component identity = girder.GetComponent(identityType);
            Assert.That(identity, Is.Not.Null);
            string bakedId = (string)identityType.GetProperty("StableTargetId").GetValue(identity);
            Assert.That(bakedId, Is.Not.Empty);
            object[] createArguments = { girder.transform, null };
            Assert.That(tryCreateReference.Invoke(nailState, createArguments), Is.True,
                $"The scene nail reference should be creatable for {girder.name}.");
            object reference = createArguments[1];
            Assert.That(referenceType.GetField("IsNetworkObject").GetValue(reference), Is.False);
            stableIds[i] = referenceType.GetField("StableTargetId").GetValue(reference).ToString();
            Assert.That(stableIds[i], Is.EqualTo(bakedId));
            object[] resolveArguments = { reference, null };
            Assert.That(tryResolve.Invoke(nailState, resolveArguments), Is.True);
            Assert.That(resolveArguments[1], Is.SameAs(girder.transform));
        }
        Assert.That(stableIds.Distinct().Count(), Is.EqualTo(2));
        Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(originalActiveScene.handle));
    }

    [Test]
    public void BothConstructionCatalogsDescribeManualPhysicalDeckPanelFastening()
    {
        UnityEngine.Object panel = AssetDatabase.LoadMainAssetAtPath(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenDeckPanel.asset");
        Assert.That(panel, Is.Not.Null);
        string[] paths =
        {
            "Assets/ScriptableObjectAssets/New/ConstructionBook/ConstructionBookCatalog.asset",
            "Assets/Experiments/TutorialTerrainStage02/BridgeWidthRevision/ConstructionBookCatalogShort.asset"
        };
        foreach (string path in paths)
        {
            UnityEngine.Object catalog = AssetDatabase.LoadMainAssetAtPath(path);
            Assert.That(catalog, Is.Not.Null, path);
            SerializedProperty entries = new SerializedObject(catalog).FindProperty("entries");
            SerializedProperty panelEntry = null;
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty candidate = entries.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("component").objectReferenceValue == panel)
                {
                    panelEntry = candidate;
                    break;
                }
            }
            Assert.That(panelEntry, Is.Not.Null, path);
            SerializedProperty steps = panelEntry.FindPropertyRelative("steps");
            string instructions = string.Join(" ", Enumerable.Range(0, steps.arraySize)
                .Select(i => steps.GetArrayElementAtIndex(i).FindPropertyRelative("instruction").stringValue));
            Assert.That(instructions, Does.Contain("F / E"), path);
            Assert.That(instructions, Does.Contain("main girders"), path);
            Assert.That(instructions, Does.Contain("separate Mounting Nail"), path);
            Assert.That(instructions, Does.Contain("four quadrants"), path);
            Assert.That(instructions, Does.Contain("three times"), path);
            Assert.That(instructions, Does.Contain("three times"), path);
            Assert.That(instructions, Does.Not.Contain("Set an even gap"), path);
            Assert.That(instructions, Does.Not.Contain("Wrench"), path);
        }
    }

    [Test]
    public void TutorialStageInfoContainsOnePhysicalDeckPanelCompletionMessage()
    {
        UnityEngine.Object panel = AssetDatabase.LoadMainAssetAtPath(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenDeckPanel.asset");
        Component manager = FindComponent("BridgeStageInfoManager", "BridgeStageInfoManager");
        SerializedProperty entries = new SerializedObject(manager).FindProperty("entries");
        int matches = 0;
        string message = null;
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("componentType").objectReferenceValue != panel
                || entry.FindPropertyRelative("stage").enumValueIndex != (int)Enum.Parse(TypeByName("BridgeConstructionStage"), "Complete")) continue;
            matches++;
            message = entry.FindPropertyRelative("message").stringValue;
        }
        Assert.That(matches, Is.EqualTo(1), "The live authoring helper is idempotent for the panel completion message.");
        Assert.That(message, Does.Contain("separate Mounting Nail"));
        Assert.That(message, Does.Contain("four quadrants"));
        Assert.That(message, Does.Contain("three times"));
        Assert.That(message, Does.Contain("Industrial Hammer"));
    }

    private Component FindComponent(string path, string typeName)
    {
        return FindComponent(targetScene, path, typeName);
    }

    private static Component FindComponent(Scene scene, string path, string typeName)
    {
        string[] parts = path.Split('/');
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform current = root.transform;
            for (int i = 1; i < parts.Length && current != null; i++) current = current.Find(parts[i]);
            return current != null ? current.GetComponent(TypeByName(typeName)) : null;
        }
        return null;
    }

    private static Type TypeByName(string name) => Type.GetType(name + ", Assembly-CSharp");
}
