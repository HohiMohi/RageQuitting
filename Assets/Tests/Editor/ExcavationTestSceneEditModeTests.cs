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
