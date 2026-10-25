using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BridgeDeckAreaV1AuthoringTests
{
    private const string ScenePath = "Assets/Experiments/BridgeDeckAreaV1/Scenes/TutorialDeckAreaV1.unity";
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private Scene originalActiveScene;
    private Scene areaScene;
    private Scene lobbyScene;
    private bool openedAreaScene;
    private bool openedLobbyScene;

    [SetUp]
    public void SetUp()
    {
        originalActiveScene = SceneManager.GetActiveScene();
        areaScene = SceneManager.GetSceneByPath(ScenePath);
        if (!areaScene.IsValid() || !areaScene.isLoaded)
        {
            areaScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            openedAreaScene = true;
        }
        Assert.That(areaScene.IsValid() && areaScene.isLoaded, Is.True);
        lobbyScene = SceneManager.GetSceneByPath("Assets/Scenes/MultiplayerStartScene.unity");
        if (!lobbyScene.IsValid() || !lobbyScene.isLoaded)
        {
            lobbyScene = EditorSceneManager.OpenScene("Assets/Scenes/MultiplayerStartScene.unity", OpenSceneMode.Additive);
            openedLobbyScene = true;
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (openedAreaScene && areaScene.IsValid() && areaScene.isLoaded)
            EditorSceneManager.CloseScene(areaScene, true);
        if (openedLobbyScene && lobbyScene.IsValid() && lobbyScene.isLoaded)
            EditorSceneManager.CloseScene(lobbyScene, true);
        if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
            SceneManager.SetActiveScene(originalActiveScene);
        openedAreaScene = false;
        openedLobbyScene = false;
    }

    [Test]
    public void DedicatedProfilesAndNetworkPrefabsUseCurrentAreaConfiguration()
    {
        UnityEngine.Object profile = Load("Assets/Experiments/BridgeDeckAreaV1/Profiles/BridgeDeckAreaV1.asset", "BridgeDeckAreaProfile, Assembly-CSharp");
        Assert.That(profile, Is.Not.Null);
        SerializedObject profileSO = new(profile);
        AssertFloat(profileSO, "areaLengthMeters", 6.56f);
        AssertFloat(profileSO, "areaWidthMeters", 2.4f);
        AssertFloat(profileSO, "stockLengthMeters", 2.5f);
        AssertFloat(profileSO, "stockWidthMeters", 2.4f);
        AssertFloat(profileSO, "stockThicknessMeters", .2f);
        AssertFloat(profileSO, "minimumFragmentLengthMeters", .4f);
        float maximumJointWidth = profileSO.FindProperty("maximumJointWidthMeters").floatValue;
        float boundaryMargin = profileSO.FindProperty("boundaryMarginMeters").floatValue;
        Assert.That(float.IsNaN(maximumJointWidth) || float.IsInfinity(maximumJointWidth), Is.False);
        Assert.That(float.IsNaN(boundaryMargin) || float.IsInfinity(boundaryMargin), Is.False);
        Assert.That(maximumJointWidth, Is.GreaterThanOrEqualTo(0f));
        Assert.That(boundaryMargin, Is.GreaterThanOrEqualTo(0f));
        object areaRules = profile.GetType().GetMethod("ToRules").Invoke(profile, null);
        Assert.That((float)areaRules.GetType().GetField("MaximumJointWidth").GetValue(areaRules),
            Is.EqualTo(maximumJointWidth).Within(.00001f));
        Assert.That((float)areaRules.GetType().GetField("BoundaryMargin").GetValue(areaRules),
            Is.EqualTo(boundaryMargin).Within(.00001f));
        AssertFloat(profileSO, "sawKerfMeters", .005f);
        AssertFloat(profileSO, "fullStockMassKilograms", 1f);
        Assert.That(profileSO.FindProperty("presentationLineMaterial").objectReferenceValue, Is.Not.Null);
        Material lineMaterial = (Material)profileSO.FindProperty("presentationLineMaterial").objectReferenceValue;
        Assert.That(lineMaterial.shader.name, Is.EqualTo("Universal Render Pipeline/Particles/Unlit"));
        Assert.That(lineMaterial.GetFloat("_ColorMode"), Is.EqualTo(0f), "URP particle shader default mode multiplies vertex colors.");

        GameObject stock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Experiments/BridgeDeckAreaV1/Prefabs/WoodenDeckAreaStockPlate.prefab");
        GameObject fragment = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Experiments/BridgeDeckAreaV1/Prefabs/WoodenDeckAreaCutFragment.prefab");
        GameObject saw = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Experiments/BridgeDeckAreaV1/Prefabs/DeckSaw.prefab");
        AssertPrefab(stock, profile, true, 2.5f);
        AssertPrefab(fragment, profile, false, 2.5f);
        Assert.That(saw, Is.Not.Null);
        Assert.That(saw.GetComponent(TypeByName("EquippableToolVisualBuilder")), Is.Not.Null);

        UnityEngine.Object sawItem = Load("Assets/Experiments/BridgeDeckAreaV1/Profiles/BridgeDeckSaw.asset", "EquippableItemSO, Assembly-CSharp");
        Assert.That(sawItem, Is.Not.Null);
        SerializedObject sawSO = new(sawItem);
        Assert.That(sawSO.FindProperty("itemType").enumValueIndex, Is.EqualTo(Enum.Parse(TypeByName("EquippableItemType"), "Saw").GetHashCode()));
        SerializedProperty cutProfile = sawSO.FindProperty("sawCutProfile");
        Assert.That(cutProfile.objectReferenceValue, Is.Not.Null);
        SerializedObject cutSO = new(cutProfile.objectReferenceValue);
        AssertFloat(cutSO, "maximumRangeMeters", 2.5f);
        AssertFloat(cutSO, "activeCutSeconds", 4f);
        Assert.That(cutSO.FindProperty("fragmentNetworkPrefab").objectReferenceValue, Is.SameAs(fragment));
        Assert.That(cutSO.FindProperty("previewLineMaterial").objectReferenceValue,
            Is.SameAs(profileSO.FindProperty("presentationLineMaterial").objectReferenceValue));

        Type listType = Type.GetType("Unity.Netcode.NetworkPrefabsList, Unity.Netcode.Runtime");
        Assert.That(listType, Is.Not.Null);
        UnityEngine.Object list = AssetDatabase.LoadAssetAtPath("Assets/DefaultNetworkPrefabs.asset", listType);
        SerializedProperty entries = new SerializedObject(list).FindProperty("List");
        AssertRegistered(entries, stock);
        AssertRegistered(entries, fragment);
        AssertRegistered(entries, saw);
    }

    [Test]
    public void CopiedSceneUsesSingleAreaRequirementAndPreservesOriginalSceneHash()
    {
        string sourcePath = Path.GetFullPath("Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity");
        using (SHA256 sha = SHA256.Create())
        {
            string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath))).Replace("-", string.Empty);
            Assert.That(hash, Is.EqualTo("94EFADC380FD3FE025680A1E3C35B7774212EAC6D3C0ACDE2C07C3FBF113A93F"));
        }
        Assert.That(EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == ScenePath), Is.True);
        Component manager = FindSingleComponent(areaScene, "GameplayManager");
        SerializedObject managerSO = new(manager);
        SerializedProperty data = managerSO.FindProperty("bridgeComponentDataArray");
        SerializedProperty stages = managerSO.FindProperty("bridgeBuildingStages");
        Assert.That(data.arraySize, Is.EqualTo(11));
        Assert.That(stages.arraySize, Is.EqualTo(6));
        int[][] expected = { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 }, new[] { 6, 7 }, new[] { 8, 9 } };
        for (int i = 0; i < expected.Length; i++)
        {
            SerializedProperty ids = stages.GetArrayElementAtIndex(i).FindPropertyRelative("bridgeComponentDataIndexes");
            Assert.That(ids.arraySize, Is.EqualTo(2));
            Assert.That(ids.GetArrayElementAtIndex(0).intValue, Is.EqualTo(expected[i][0]));
            Assert.That(ids.GetArrayElementAtIndex(1).intValue, Is.EqualTo(expected[i][1]));
        }
        SerializedProperty finalIds = stages.GetArrayElementAtIndex(5).FindPropertyRelative("bridgeComponentDataIndexes");
        Assert.That(finalIds.arraySize, Is.EqualTo(1));
        Assert.That(finalIds.GetArrayElementAtIndex(0).intValue, Is.EqualTo(10));

        Component section = FindSingleComponent(areaScene, "BridgeDeckSection");
        SerializedObject sectionSO = new(section);
        Assert.That(sectionSO.FindProperty("panelSlots").arraySize, Is.Zero);
        Assert.That(sectionSO.FindProperty("physicalPanelState").objectReferenceValue, Is.Null);
        Assert.That(GetProperty(section, "MainGirders"), Is.Not.Null);
        Component requirement = section.GetComponentInChildren(TypeByName("BridgeDeckAreaRequirement"), true);
        Assert.That(requirement, Is.Not.Null);
        Component bridgeComponent = requirement.GetComponent(TypeByName("BridgeComponent"));
        Assert.That(new SerializedObject(bridgeComponent).FindProperty("componentID").intValue, Is.EqualTo(10));
        Transform frame = GetProperty(requirement, "DeckFrame") as Transform;
        Assert.That(frame, Is.Not.Null);
        IEnumerable mainGirders = GetProperty(section, "MainGirders") as IEnumerable;
        Assert.That(mainGirders, Is.Not.Null);
        float girderTop = float.NegativeInfinity;
        foreach (object girder in mainGirders)
        {
            if (girder is not Component component || component == null) continue;
            foreach (Collider collider in component.GetComponents<Collider>())
                if (collider.enabled && !collider.isTrigger) girderTop = Mathf.Max(girderTop, collider.bounds.max.y);
        }
        Assert.That(girderTop, Is.Not.EqualTo(float.NegativeInfinity), "Both configured main girders need physical support colliders.");
        Assert.That(frame.position.y, Is.EqualTo(girderTop).Within(.002f), "The flat deck frame must sit on the actual girder tops.");
        Component presentation = requirement.GetComponentInChildren(TypeByName("BridgeDeckAreaPresentation"), true);
        Assert.That(presentation, Is.Not.Null, "The copied area adapter must include the stage-gated presentation component.");
        Material configuredLineMaterial = (Material)new SerializedObject(GetProperty(requirement, "Profile") as UnityEngine.Object)
            .FindProperty("presentationLineMaterial").objectReferenceValue;
        Assert.That(configuredLineMaterial, Is.Not.Null);
        Assert.That(section.transform.Find("DeckPanelSlot_0"), Is.Null);
        Assert.That(section.transform.Find("DeckPanelSlot_1"), Is.Null);
        Assert.That(section.transform.Find("DeckPanelSlot_2"), Is.Null);

        Component[] panels = FindComponents(areaScene, "BridgeDeckAreaPanel");
        Assert.That(panels.Length, Is.EqualTo(3));
    }

    [Test]
    public void CarpenterAndLobbyReferencesUseCopiedProductAndScene()
    {
        UnityEngine.Object newRecipe = Load("Assets/Experiments/BridgeDeckAreaV1/Recipes/Carpenter_WoodenDeckAreaStockPlate.asset", "ProductionRecipeSO, Assembly-CSharp");
        UnityEngine.Object oldRecipe = Load("Assets/ScriptableObjectAssets/New/ProductionRecipes/Carpenter/Carpenter_WoodenDeckPanel.asset", "ProductionRecipeSO, Assembly-CSharp");
        Assert.That(newRecipe, Is.Not.Null);
        Assert.That(oldRecipe, Is.Not.Null);
        Assert.That(GetProperty(newRecipe, "MountableBridgeComponentOutput").ToString(), Does.Contain("WoodenDeckAreaStockPlate"));
        Assert.That(GetProperty(newRecipe, "OutputAmount"), Is.EqualTo(GetProperty(oldRecipe, "OutputAmount")));

        Component book = FindSingleComponent(areaScene, "ConstructionBookController");
        SerializedObject bookSO = new(book);
        UnityEngine.Object catalog = bookSO.FindProperty("catalog").objectReferenceValue;
        Assert.That(catalog, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(catalog), Does.Contain("BridgeDeckAreaV1/Book/"));
        SerializedProperty recipes = bookSO.FindProperty("carpenterRecipes");
        Assert.That(Enumerable.Range(0, recipes.arraySize)
            .Any(index => recipes.GetArrayElementAtIndex(index).objectReferenceValue == newRecipe), Is.True,
            "The copied book must list the copied Carpenter recipe, independently of the surrounding legacy recipe order.");

        Scene lobby = lobbyScene;
        Assert.That(lobby.IsValid() && lobby.isLoaded, Is.True);
        Transform buttonTransform = lobby.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Single(transform => transform.name == "StartDeckAreaSceneButton");
        Component button = buttonTransform.GetComponent(TypeByName("UnityEngine.UI.Button, UnityEngine.UI"));
        Assert.That(button, Is.Not.Null);
        Type textType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
        Component text = buttonTransform.GetComponentInChildren(textType, true);
        Assert.That(GetProperty(text, "text"), Is.EqualTo("Tutorial — cięcie płyt"));
        UnityEngine.Object labelFont = GetProperty(text, "font") as UnityEngine.Object;
        Assert.That(AssetDatabase.GetAssetPath(labelFont), Is.EqualTo("Assets/Experiments/BridgeDeckAreaV1/Profiles/DeckAreaUiFontBaked.asset"));
        Assert.That(GetProperty(labelFont, "atlasPopulationMode").ToString(), Is.EqualTo("Static"));
        var bakedCharacters = new HashSet<uint>();
        foreach (object character in (IEnumerable)GetProperty(labelFont, "characterTable"))
            bakedCharacters.Add(Convert.ToUInt32(GetProperty(character, "unicode")));
        foreach (uint unicode in "Tutorial — cięcie płyt".Distinct().Select(character => (uint)character))
            Assert.That(bakedCharacters.Contains(unicode), Is.True, $"Static label font is missing U+{unicode:X4}.");
        Component room = FindSingleComponent(lobby, "MultiplayerRoomManager");
        Assert.That(new SerializedObject(room).FindProperty("startDeckAreaSceneButton").objectReferenceValue, Is.SameAs(button));
    }

    [Test]
    public void CopiedSceneNetworkHashesAndStaticNailIdsArePersistedUniqueAndDistinctFromSource()
    {
        const string sourcePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
        Scene source = SceneManager.GetSceneByPath(sourcePath);
        bool openedSource = !source.IsValid() || !source.isLoaded;
        if (openedSource) source = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Additive);
        try
        {
            string[] areaHashes = ReadNetworkHashes(areaScene);
            string[] sourceHashes = ReadNetworkHashes(source);
            Assert.That(areaHashes.Length, Is.GreaterThan(0));
            Assert.That(areaHashes.All(hash => hash != "0"), Is.True, "Every copied scene NetworkObject must have an authored NGO hash.");
            Assert.That(areaHashes.Distinct().Count(), Is.EqualTo(areaHashes.Length), "Copied scene NetworkObject hashes must be unique.");
            Assert.That(areaHashes.Intersect(sourceHashes).Any(), Is.False,
                "The copied scene must not reuse a scene NetworkObject hash from its source scene.");

            string[] areaTargetIds = ReadSceneTargetIds(areaScene);
            string[] sourceTargetIds = ReadSceneTargetIds(source);
            Assert.That(areaTargetIds.Length, Is.GreaterThan(0), "Copied physical mounting targets need baked static nail identities.");
            Assert.That(areaTargetIds.All(id => !string.IsNullOrWhiteSpace(id)), Is.True);
            Assert.That(areaTargetIds.Distinct().Count(), Is.EqualTo(areaTargetIds.Length), "Copied static nail target IDs must be unique.");
            Assert.That(areaTargetIds.Intersect(sourceTargetIds).Any(), Is.False,
                "Copied static nail target IDs must be tied to the new scene objects, not the original scene.");
        }
        finally
        {
            if (openedSource && source.IsValid() && source.isLoaded) EditorSceneManager.CloseScene(source, true);
        }
    }

    [Test]
    public void SawFallbackHasSecondaryGripAndObserverModelUsesAuthoredVisual()
    {
        Type builderType = TypeByName("EquippableToolVisualBuilder");
        Type itemType = TypeByName("EquippableItemType");
        Type materialsType = builderType.GetNestedType("ToolVisualMaterials", BindingFlags.Public);
        object materials = Activator.CreateInstance(materialsType);
        GameObject parent = new("Saw FPP hand visual test");
        try
        {
            object saw = Enum.Parse(itemType, "Saw");
            MethodInfo supportedTool = TypeByName("PlayerFirstPersonArms")
                .GetMethod("IsSupportedToolType", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(supportedTool, Is.Not.Null,
                "The first-person arms must expose their production tool-visibility gate to this regression test.");
            Assert.That((bool)supportedTool.Invoke(null, new[] { saw }), Is.True,
                "An authored Saw must pass the same allowlist that controls whether the equipped hand visual is built.");
            GameObject firstPersonVisual = (GameObject)builderType.GetMethod("BuildVisual", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { saw, parent.transform, materials });
            Assert.That(firstPersonVisual, Is.Not.Null);
            Assert.That(firstPersonVisual.transform.Find("SecondaryGrip"), Is.Not.Null,
                "The generic first-person Saw fallback must expose the marker expected by two-handed arm posing.");

            GameObject observerSawModel = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Experiments/BridgeDeckAreaV1/Prefabs/DeckSawModel.prefab");
            Assert.That(observerSawModel, Is.Not.Null);
            materialsType.GetField("sawModelPrefab").SetValue(materials, observerSawModel);
            GameObject observerVisual = (GameObject)builderType.GetMethod("BuildVisual", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { saw, parent.transform, materials });
            Assert.That(observerVisual, Is.Not.Null);
            Transform authoredModel = observerVisual.transform.Find("DeckSawModel");
            Assert.That(authoredModel, Is.Not.Null, "Observer/tool visual generation must instantiate the authored V1 Saw model.");
            Assert.That(authoredModel.GetComponentsInChildren<MeshRenderer>(true).Length, Is.GreaterThan(0));
            Assert.That(authoredModel.GetComponentsInChildren<Collider>(true), Is.Empty,
                "A presentation-only observer model must not add duplicate world physics colliders.");
        }
        finally { UnityEngine.Object.DestroyImmediate(parent); }
    }

    [Test]
    public void NewBridgeDeckAreaProfileKeepsDefaultJointAndBoundaryTolerances()
    {
        ScriptableObject defaults = ScriptableObject.CreateInstance(TypeByName("BridgeDeckAreaProfile"));
        try
        {
            SerializedObject serialized = new(defaults);
            AssertFloat(serialized, "maximumJointWidthMeters", .02f);
            AssertFloat(serialized, "boundaryMarginMeters", .01f);
        }
        finally { UnityEngine.Object.DestroyImmediate(defaults); }
    }

    private static void AssertPrefab(GameObject prefab, UnityEngine.Object profile, bool visibleToObservers, float length)
    {
        Assert.That(prefab, Is.Not.Null);
        GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
        try
        {
            Component panel = root.GetComponent(TypeByName("BridgeDeckAreaPanel"));
            Assert.That(panel, Is.Not.Null);
            Assert.That(ReadField(panel, "geometryProfile"), Is.SameAs(profile));
            Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(root.GetComponents<Collider>().Length, Is.EqualTo(1));
            Assert.That(root.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(length, .2f, 2.4f)));
            Assert.That(root.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
            Assert.That(root.GetComponent<MeshFilter>().sharedMesh.triangles.Length, Is.EqualTo(36));
            Component networkObject = root.GetComponent(TypeByName("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime"));
            Assert.That(networkObject, Is.Not.Null);
            Assert.That(new SerializedObject(networkObject).FindProperty("SpawnWithObservers").boolValue, Is.EqualTo(visibleToObservers));
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void AssertRegistered(SerializedProperty entries, GameObject prefab)
    {
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty value = entries.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab");
            if (value != null && value.objectReferenceValue == prefab) return;
        }
        Assert.Fail($"{prefab.name} is absent from DefaultNetworkPrefabs.");
    }

    private static void AssertFloat(SerializedObject serialized, string name, float expected)
        => Assert.That(serialized.FindProperty(name).floatValue, Is.EqualTo(expected).Within(.00001f), name);

    private static Type TypeByName(string name) => name.Contains(",") ? Type.GetType(name) : Type.GetType(name + ", Assembly-CSharp");
    private static UnityEngine.Object Load(string path, string typeName) => AssetDatabase.LoadAssetAtPath(path, TypeByName(typeName));
    private static Component[] FindComponents(Scene scene, string typeName)
    {
        Type type = TypeByName(typeName);
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(type, true)).Cast<Component>().ToArray();
    }

    private static string[] ReadNetworkHashes(Scene scene)
    {
        return FindComponents(scene, "Unity.Netcode.NetworkObject, Unity.Netcode.Runtime")
            .Select(value => new SerializedObject(value).FindProperty("GlobalObjectIdHash").uintValue.ToString())
            .ToArray();
    }

    private static string[] ReadSceneTargetIds(Scene scene)
    {
        return FindComponents(scene, "MountingNailSceneTargetIdentity")
            .Select(identity => (string)GetProperty(identity, "StableTargetId"))
            .ToArray();
    }
    private static Component FindSingleComponent(Scene scene, string typeName) => FindComponents(scene, typeName).Single();
    private static object ReadField(object target, string name) => target.GetType().GetField(name, InstanceFields)?.GetValue(target);
    private static object GetProperty(object target, string name) => target.GetType().GetProperty(name, InstanceFields)?.GetValue(target);
}
