using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Type = System.Type;

public sealed class LooseSoilStage2SceneEditModeTests
{
    private const string Stage02 = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
    private const string LegacyTutorial = "Assets/Scenes/Tutorial_scene.unity";
    private const string LooseMaterial = "Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat";
    private SceneSetup[] originalSetup;
    private Type volumeType;

    [SetUp]
    public void CaptureEditorState()
    {
        originalSetup = EditorSceneManager.GetSceneManagerSetup();
        volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        Assert.That(volumeType, Is.Not.Null);
    }

    [TearDown]
    public void RestoreEditorState()
    {
        CloseSceneOpenedByTest(Stage02);
        CloseSceneOpenedByTest(LegacyTutorial);
    }

    [Test]
    public void LooseSoilStage02SceneHasTwoOptInFieldsAndKeepsLegacyGeometryUntouched()
    {
        Scene stage = EditorSceneManager.OpenScene(Stage02, OpenSceneMode.Additive);
        Component[] volumes = SceneComponents(volumeType, stage);
        Assert.That(volumes, Has.Length.EqualTo(2));
        bool foundWest = false, foundEast = false;
        foreach (Component volume in volumes)
        {
            Assert.That((bool)Property(volume, "UsesLooseSoilHeightField"), Is.True);
            Assert.That((bool)Property(volume, "HasValidLooseSoilConfiguration"), Is.True);
            Component field = Property(volume, "LooseSoilField") as Component;
            Assert.That(field, Is.Not.Null);
            Assert.That(field.transform.parent, Is.SameAs(volume.transform));
            Assert.That(field.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(field.transform.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(field.GetComponent<MeshFilter>().sharedMesh, Is.Null,
                "Runtime meshes must not be serialized into the scene.");
            Assert.That(field.GetComponent<Collider>(), Is.Null,
                "The rendered loose surface must not have a solid collider.");
            Assert.That(field.GetComponent<MeshRenderer>().enabled, Is.False,
                "The loose grid is hidden until the first layer is loosened.");
            Assert.That(AssetDatabase.GetAssetPath(field.GetComponent<MeshRenderer>().sharedMaterial), Is.EqualTo(LooseMaterial));

            SerializedObject fieldData = new SerializedObject(field);
            Assert.That(fieldData.FindProperty("cellSize").floatValue, Is.EqualTo(.2f));
            Assert.That(fieldData.FindProperty("footprintWidth").floatValue, Is.EqualTo(6.5f));
            Assert.That(fieldData.FindProperty("footprintLength").floatValue, Is.EqualTo(4f));
            Assert.That(fieldData.FindProperty("layerDepth").floatValue, Is.EqualTo(.4f).Within(.0001f));
            Assert.That(fieldData.FindProperty("initialHeight").floatValue, Is.EqualTo(.4f).Within(.0001f));
            Assert.That(fieldData.FindProperty("unitsPerLayer").floatValue, Is.EqualTo(6f));

            SerializedObject volumeData = new SerializedObject(volume);
            Assert.That(volumeData.FindProperty("useLooseSoilHeightField").boolValue, Is.True);
            Assert.That(volumeData.FindProperty("looseSoilField").objectReferenceValue, Is.SameAs(field));
            BoxCollider support = volumeData.FindProperty("soilSupportCollider").objectReferenceValue as BoxCollider;
            Assert.That(support, Is.Not.Null);
            Assert.That(volumeData.FindProperty("soilSurface").objectReferenceValue, Is.SameAs(support.transform));

            Component site = volumeData.FindProperty("constructionSite").objectReferenceValue as Component;
            Assert.That(site, Is.Not.Null);
            Component bridgeComponent = site.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp"));
            Assert.That(bridgeComponent, Is.Not.Null);
            Assert.That((bool)Property(bridgeComponent, "StartFullyCompleted"), Is.False);

            if (Mathf.Abs(volume.transform.position.x + 1.25f) < .01f) foundWest = true;
            if (Mathf.Abs(volume.transform.position.x - 11.25f) < .01f)
            {
                foundEast = true;
                Assert.That(volume.transform.localPosition.x, Is.EqualTo(17.25f).Within(.01f),
                    "East excavation is translated by its parent; its authored local X must remain local.");
            }
        }
        Assert.That(foundWest && foundEast, Is.True);

        foreach (string preservedObject in new[] { "PitWallNorth", "PitWallSouth", "PitBottom", "ExitRamp" })
            Assert.That(SceneTransforms(stage).Count(t => t.name == preservedObject), Is.EqualTo(2),
                $"Stage02 must retain the authored {preservedObject} geometry on both foundations.");

        Scene legacy = EditorSceneManager.OpenScene(LegacyTutorial, OpenSceneMode.Additive);
        foreach (Component volume in SceneComponents(volumeType, legacy))
            Assert.That((bool)Property(volume, "UsesLooseSoilHeightField"), Is.False,
                "The legacy tutorial scene must remain outside the opt-in mode.");
    }

    [Test]
    public void Stage02PreviewFieldsResolveTheirSerializedSitesAcrossSeparateHierarchyBranches()
    {
        Scene stage = EditorSceneManager.OpenPreviewScene(Stage02);
        try
        {
            Type siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
            Type fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
            Component[] volumes = SceneComponents(volumeType, stage);
            Assert.That(volumes, Has.Length.EqualTo(2));
            foreach (Component volume in volumes)
            {
                Component field = Property(volume, "LooseSoilField") as Component;
                Component site = Property(volume, "ConstructionSite") as Component;
                Assert.That(field, Is.Not.Null);
                Assert.That(site, Is.Not.Null);
                Assert.That(field.GetComponentInParent(siteType), Is.Null,
                    "Stage02 fields are under the volume branch, separate from their referenced construction sites.");
                Assert.That(Property(site, "ExcavationVolume"), Is.SameAs(volume));
                Assert.That(Property(volume, "ConstructionSite"), Is.SameAs(site));
                object[] resolvedSite = { null };
                Assert.That((bool)fieldType.GetMethod("TryGetConstructionSite").Invoke(field, resolvedSite), Is.True);
                Assert.That(resolvedSite[0], Is.SameAs(site));
            }
        }
        finally
        {
            if (stage.IsValid() && stage.isLoaded) EditorSceneManager.ClosePreviewScene(stage);
        }
    }

    private static Component[] SceneComponents(Type type, Scene scene) =>
        Resources.FindObjectsOfTypeAll(type).Cast<Component>()
            .Where(component => component != null && component.gameObject.scene == scene).ToArray();

    private static Transform[] SceneTransforms(Scene scene) =>
        Resources.FindObjectsOfTypeAll<Transform>().Where(transform => transform.gameObject.scene == scene).ToArray();

    private void CloseSceneOpenedByTest(string path)
    {
        if (originalSetup != null && originalSetup.Any(setup => setup.path == path && setup.isLoaded)) return;
        Scene scene = SceneManager.GetSceneByPath(path);
        if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
    }

    private static object Property(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
}
