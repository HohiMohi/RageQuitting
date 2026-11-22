using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ExcavationTestSceneEditModeTests
{
    private const string SourcePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
    private const string FixturePath = "Assets/Tests/Fixtures/TutorialTerrainStage02.unity";
    private string sourceHash;

    [SetUp]
    public void CaptureSource()
    {
        sourceHash = ComputeHash(SourcePath);
    }

    [Test]
    public void DedicatedFixtureHasIndependentSceneAndNetworkIdsAndStartsInClearing()
    {
        Assert.That(AssetDatabase.AssetPathToGUID(FixturePath), Is.Not.EqualTo(AssetDatabase.AssetPathToGUID(SourcePath)));
        Assert.That(EditorBuildSettings.scenes.Any(scene => scene.path == FixturePath), Is.False,
            "The dedicated test scene must not be part of normal player Build Settings.");

        Scene source = EditorSceneManager.OpenPreviewScene(SourcePath);
        Scene fixture = default;
        try
        {
            fixture = EditorSceneManager.OpenPreviewScene(FixturePath);
            Assert.That(fixture.name, Is.EqualTo("TutorialTerrainStage02"),
                "The dedicated fixture retains the canonical gameplay scene name needed by the public restart controller.");
            Type volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
            Type bridgeType = Type.GetType("BridgeComponent, Assembly-CSharp");
            Type networkObjectType = Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime");
            Type identityType = Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
            Assert.That(volumeType, Is.Not.Null);
            Assert.That(bridgeType, Is.Not.Null);
            Assert.That(networkObjectType, Is.Not.Null);
            Assert.That(identityType, Is.Not.Null);

            Component[] volumes = ComponentsInScene(volumeType, fixture);
            Assert.That(volumes, Has.Length.EqualTo(2));
            foreach (Component volume in volumes)
            {
                SerializedObject volumeData = new(volume);
                Assert.That(volumeData.FindProperty("useLooseSoilHeightField").boolValue, Is.True);
                Component field = volumeData.FindProperty("looseSoilField").objectReferenceValue as Component;
                Component site = volumeData.FindProperty("constructionSite").objectReferenceValue as Component;
                BoxCollider support = volumeData.FindProperty("soilSupportCollider").objectReferenceValue as BoxCollider;
                Assert.That(field, Is.Not.Null);
                Assert.That(site, Is.Not.Null);
                Assert.That(support, Is.Not.Null);
                Assert.That(field.transform.parent, Is.SameAs(volume.transform));
                Assert.That(field.GetComponent<MeshFilter>().sharedMesh, Is.Null,
                    "The scene copy must not serialize a runtime-generated loose-soil mesh.");
                SerializedObject fieldData = new(field);
                Assert.That(fieldData.FindProperty("cellSize").floatValue, Is.EqualTo(.2f));
                Assert.That(fieldData.FindProperty("unitsPerLayer").floatValue, Is.EqualTo(6f));
                Assert.That(fieldData.FindProperty("initialHeight").floatValue, Is.EqualTo(.4f).Within(.0001f));
                Assert.That(fieldData.FindProperty("footprintWidth").floatValue, Is.EqualTo(6.5f));
                Assert.That(fieldData.FindProperty("footprintLength").floatValue, Is.EqualTo(4f));

                Component bridge = site.GetComponent(bridgeType);
                Assert.That(bridge, Is.Not.Null);
                Assert.That(new SerializedObject(bridge).FindProperty("startFullyCompleted").boolValue, Is.False);
                Assert.That(Property(site, "CurrentStage").ToString(), Is.EqualTo("Clearing"));
                Assert.That((int)Property(site, "RemainingObstacleCount"), Is.EqualTo(3));
                Assert.That((int)Property(site, "DiggingCycleIndex"), Is.Zero);
                Assert.That((float)Property(site, "RemovedSoilUnits"), Is.Zero.Within(.001f));
                SerializedProperty obstacleRefs = new SerializedObject(site).FindProperty("clearingObstacles");
                Component[] obstacles = Enumerable.Range(0, obstacleRefs.arraySize)
                    .Select(index => obstacleRefs.GetArrayElementAtIndex(index).objectReferenceValue as Component).ToArray();
                Assert.That(obstacles, Has.Length.EqualTo(3));
                Assert.That(obstacles.All(obstacle => obstacle.gameObject.activeSelf), Is.True);
            }

            Component[] bridgeComponents = ComponentsInScene(bridgeType, fixture)
                .OrderBy(component => (int)Property(component, "ComponentID")).ToArray();
            Assert.That(bridgeComponents, Has.Length.EqualTo(13));
            CollectionAssert.AreEqual(Enumerable.Range(0, 13), bridgeComponents.Select(component => (int)Property(component, "ComponentID")));
            Assert.That(bridgeComponents.All(component =>
                !new SerializedObject(component).FindProperty("startFullyCompleted").boolValue), Is.True,
                "All thirteen copied bridge components start unbuilt, matching the verified pre-construction baseline used by wall and excavation tests.");

            string[] sourceHashes = NetworkHashes(networkObjectType, source);
            string[] fixtureHashes = NetworkHashes(networkObjectType, fixture);
            Assert.That(fixtureHashes, Is.Not.Empty);
            Assert.That(fixtureHashes.All(hash => hash != "0"), Is.True);
            Assert.That(fixtureHashes.Distinct().Count(), Is.EqualTo(fixtureHashes.Length));
            Assert.That(fixtureHashes.Intersect(sourceHashes), Is.Empty,
                "The copy's in-scene NGO IDs must be regenerated from its own scene GlobalObjectIds.");

            string[] sourceTargetIds = ComponentsInScene(identityType, source).Select(identity => (string)Property(identity, "StableTargetId")).ToArray();
            string[] fixtureTargetIds = ComponentsInScene(identityType, fixture).Select(identity => (string)Property(identity, "StableTargetId")).ToArray();
            Assert.That(fixtureTargetIds, Is.Not.Empty);
            Assert.That(fixtureTargetIds.All(id => !string.IsNullOrWhiteSpace(id)), Is.True);
            Assert.That(fixtureTargetIds.Distinct().Count(), Is.EqualTo(fixtureTargetIds.Length));
            Assert.That(fixtureTargetIds.Intersect(sourceTargetIds), Is.Empty,
                "Copied static mounting target IDs must follow their copied scene object IDs.");
        }
        finally
        {
            if (fixture.IsValid()) EditorSceneManager.ClosePreviewScene(fixture);
            if (source.IsValid()) EditorSceneManager.ClosePreviewScene(source);
            Assert.That(ComputeHash(SourcePath), Is.EqualTo(sourceHash),
                "Opening and inspecting the fixture must leave the working scene byte-for-byte unchanged.");
            Assert.That(EditorBuildSettings.scenes.Any(scene => scene.path == FixturePath), Is.False);
        }
    }

    [Test]
    public void GameplayAndFixtureUseTheSharedExcavationPrefabWithScopedStaticTargets()
    {
        const string prefabPath = "Assets/Prefabs/New/BridgeComponents/FoundationExcavation.prefab";
        Type volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        Type identityType = Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
        Type failedType = Type.GetType("FoundationFailedConcreteTarget, Assembly-CSharp");
        Type navSurfaceType = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
        Type modifierType = Type.GetType("Unity.AI.Navigation.NavMeshModifierVolume, Unity.AI.Navigation");
        Assert.That(volumeType, Is.Not.Null);
        Assert.That(identityType, Is.Not.Null);
        Assert.That(failedType, Is.Not.Null);
        Assert.That(navSurfaceType, Is.Not.Null);
        Assert.That(modifierType, Is.Not.Null);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null);
        Component prefabVolume = ComponentsInRoot(volumeType, prefab.transform).Single();
        Assert.That(prefabVolume, Is.Not.Null);
        SerializedObject prefabData = new(prefabVolume);
        Assert.That(prefabData.FindProperty("constructionSite").objectReferenceValue, Is.Null,
            "Prefab defaults must not retain a scene construction site.");
        Assert.That((prefabData.FindProperty("soilRenderer").objectReferenceValue as Renderer).sharedMaterial,
            Is.SameAs(AssetDatabase.LoadAssetAtPath<Material>("Assets/ScriptableObjectAssets/New/Substances/CompactSoil.mat")));
        Assert.That((prefabData.FindProperty("concreteRenderer").objectReferenceValue as Renderer).sharedMaterial,
            Is.SameAs(AssetDatabase.LoadAssetAtPath<Material>("Assets/GeneratedAssets/Wheelbarrow/ConcreteWet.mat")));
        Assert.That(ComponentsInRoot(identityType, prefab.transform)
            .All(identity => string.IsNullOrEmpty(new SerializedObject(identity).FindProperty("stableTargetId").stringValue)), Is.True,
            "Stable target IDs belong to each scene instance, not the shared prefab defaults.");
        Assert.That(ComponentsInRoot(navSurfaceType, prefab.transform)
            .All(surface => new SerializedObject(surface).FindProperty("m_NavMeshData").objectReferenceValue == null), Is.True,
            "Baked scene NavMeshData must not be stored in the prefab defaults.");

        Scene gameplay = EditorSceneManager.OpenPreviewScene(SourcePath);
        Scene fixture = default;
        try
        {
            fixture = EditorSceneManager.OpenPreviewScene(FixturePath);
            var allIds = new System.Collections.Generic.List<string>();
            VerifyPrefabPits(gameplay, prefab, allIds, volumeType, identityType, failedType, navSurfaceType, modifierType);
            VerifyPrefabPits(fixture, prefab, allIds, volumeType, identityType, failedType, navSurfaceType, modifierType);
            Assert.That(allIds, Has.Count.EqualTo(36));
            Assert.That(allIds.Distinct().Count(), Is.EqualTo(36),
                "Each side and scene must have independent baked stable target IDs.");
        }
        finally
        {
            if (fixture.IsValid()) EditorSceneManager.ClosePreviewScene(fixture);
            if (gameplay.IsValid()) EditorSceneManager.ClosePreviewScene(gameplay);
            Assert.That(ComputeHash(SourcePath), Is.EqualTo(sourceHash),
                "Inspecting prefab-backed pits must not change the working scene.");
            Assert.That(EditorBuildSettings.scenes.Any(scene => scene.path == FixturePath), Is.False);
        }
    }

    private static void VerifyPrefabPits(Scene scene, GameObject prefab, System.Collections.Generic.List<string> allIds,
        Type volumeType, Type identityType, Type failedType, Type navSurfaceType, Type modifierType)
    {
        foreach (string side in new[] { "West_FoundationExcavation", "East_FoundationExcavation" })
        {
            Transform root = Resources.FindObjectsOfTypeAll<Transform>()
                .FirstOrDefault(t => t != null && t.gameObject.scene == scene && t.name == side &&
                    PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == prefab);
            Assert.That(root, Is.Not.Null, scene.path + " is missing prefab instance " + side);
            Assert.That(root.GetComponentsInChildren<Transform>(true), Has.Length.EqualTo(45));
            BoxCollider pitBottom = root.GetComponentsInChildren<BoxCollider>(true).FirstOrDefault(c => c.name == "PitBottom");
            Assert.That(pitBottom, Is.Not.Null);
            float expectedPivotX = side.StartsWith("West", StringComparison.Ordinal) ? -1.25f : 11.25f;
            Assert.That(root.position.x, Is.EqualTo(expectedPivotX).Within(.001f));
            Component volume = ComponentsInRoot(volumeType, root).Single();
            Assert.That(volume, Is.Not.Null);
            var volumeData = new SerializedObject(volume);
            Component site = volumeData.FindProperty("constructionSite").objectReferenceValue as Component;
            Assert.That(site, Is.Not.Null);
            Assert.That(new SerializedObject(site).FindProperty("excavationVolume").objectReferenceValue, Is.SameAs(volume));
            foreach (Component failed in ComponentsInRoot(failedType, root))
                Assert.That(new SerializedObject(failed).FindProperty("constructionSite").objectReferenceValue, Is.SameAs(site));

            Renderer rampRenderer = volumeData.FindProperty("exitRampRenderer").objectReferenceValue as Renderer;
            Collider rampCollider = volumeData.FindProperty("exitRampCollider").objectReferenceValue as Collider;
            Assert.That(rampRenderer, Is.Not.Null);
            Assert.That(rampCollider, Is.Not.Null);
            Assert.That(rampRenderer.gameObject, Is.SameAs(rampCollider.gameObject),
                "The authored ExitRamp renderer and collider must remain together on their dedicated object.");
            Assert.That(rampRenderer.enabled, Is.False);
            Assert.That(rampCollider.enabled, Is.False);
            Assert.That(rampCollider.isTrigger, Is.False);

            Transform rampVisual = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PitWallRampVisual");
            Assert.That(rampVisual, Is.Not.Null);
            Assert.That(rampVisual.GetComponent<Renderer>(), Is.Not.Null);
            Assert.That(rampVisual.GetComponent<Collider>(), Is.Null,
                "The inherited ramp visual is render-only; collision stays on the authored ramp collider.");
            Transform riverWall = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PitWallRiver");
            Assert.That(riverWall, Is.Not.Null);
            Assert.That(riverWall.GetComponent<Renderer>(), Is.Not.Null);
            BoxCollider riverCollider = riverWall.GetComponent<BoxCollider>();
            Assert.That(riverCollider, Is.Not.Null);
            Assert.That(riverCollider.enabled && !riverCollider.isTrigger, Is.True);

            Component[] surfaces = ComponentsInRoot(navSurfaceType, root);
            Assert.That(surfaces, Is.Not.Empty);
            Assert.That(surfaces.All(surface => new SerializedObject(surface).FindProperty("m_NavMeshData").objectReferenceValue != null), Is.True,
                "Scene instances must preserve their authored baked NavMeshData references.");
            Component modifier = ComponentsInRoot(modifierType, root).Single();
            Assert.That(modifier, Is.Not.Null);
            Vector3 expectedCenter = side.StartsWith("West", StringComparison.Ordinal)
                ? new Vector3(1.25f, -.5f, 0f) : new Vector3(-17.25f, -.5f, 0f);
            Assert.That(new SerializedObject(modifier).FindProperty("m_Center").vector3Value, Is.EqualTo(expectedCenter));
            Assert.That((modifier as Behaviour).enabled, Is.False, "The authored modifier volume remains disabled.");
            Vector3 expectedWorldCenter = side.StartsWith("West", StringComparison.Ordinal)
                ? new Vector3(0f, -.5f, 0f) : new Vector3(-6f, -.5f, 0f);
            Assert.That(Vector3.Distance(modifier.transform.TransformPoint(expectedCenter), expectedWorldCenter), Is.LessThan(.001f),
                "Rebasing the prefab pivot must preserve the modifier volume's authored world center.");

            Component[] identities = ComponentsInRoot(identityType, root);
            Assert.That(identities, Has.Length.EqualTo(9));
            foreach (Component identity in identities)
            {
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(identity.gameObject).ToString();
                string stableId = new SerializedObject(identity).FindProperty("stableTargetId").stringValue;
                Assert.That(stableId, Is.EqualTo(globalId));
                allIds.Add(stableId);
            }
            Transform looseSoilSurface = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "LooseSoilSurface");
            Assert.That(looseSoilSurface.GetComponent<MeshFilter>().sharedMesh, Is.Null);
        }
    }

    private static Component[] ComponentsInRoot(Type type, Transform root) =>
        root.GetComponentsInChildren<Component>(true).Where(component => component != null && type.IsInstanceOfType(component)).ToArray();

    private static Component[] ComponentsInScene(Type type, Scene scene) =>
        Resources.FindObjectsOfTypeAll(type).Cast<Component>()
            .Where(component => component != null && component.gameObject.scene == scene).ToArray();

    private static string[] NetworkHashes(Type type, Scene scene)
    {
        FieldInfo hashField = type.GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(hashField, Is.Not.Null);
        return ComponentsInScene(type, scene).Select(component => Convert.ToUInt32(hashField.GetValue(component)).ToString()).ToArray();
    }

    private static object Property(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);

    private static string ComputeHash(string assetPath)
    {
        using SHA256 sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.GetFullPath(assetPath)))).Replace("-", string.Empty);
    }
}
