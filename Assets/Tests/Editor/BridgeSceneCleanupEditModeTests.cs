using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class BridgeSceneCleanupEditModeTests
{
    [Test]
    public void TutorialKeepsOriginalSupplyItemsAndMainBridgeWhileFixtureStaysIsolated()
    {
        using BridgeAssemblyTestFixtureUtility scenes = new(includeProductionPreview: false);
        scenes.OpenSeparateProductionPreview();
        var production = scenes.ProductionPreview;
        var fixture = scenes.FixturePreview;
        var supplies = production.GetRootGameObjects().SingleOrDefault(root => root.name == "BridgeAssemblySupplies");
        Assert.That(supplies, Is.Not.Null);
        Assert.That(production.GetRootGameObjects().Any(root => root.name == "NailTestArea"), Is.False);

        string[] removedTestProps = { "IndustrialHammer_LaterStages", "Stage3_DynamicPair_First", "Stage3_DynamicPair_Second" };
        Assert.That(production.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Any(transform => removedTestProps.Contains(transform.name)), Is.False);
        Assert.That(supplies.GetComponentsInChildren<Transform>(true).Any(transform => transform.name.StartsWith("Board_")), Is.False);

        System.Type nailType = TypeByName("MountingNail");
        System.Type mountingType = TypeByName("BridgeDeckPanelMounting");
        System.Type sectionType = TypeByName("BridgeDeckSection");
        Component[] productionNails = supplies.GetComponentsInChildren(nailType, true).Cast<Component>().ToArray();
        Component[] productionPanels = supplies.GetComponentsInChildren(mountingType, true).Cast<Component>()
            .Where(marker => (bool)mountingType.GetProperty("IsPhysicalPanel").GetValue(marker)).ToArray();
        Component[] fixtureNails = scenes.FixtureRoot.GetComponentsInChildren(nailType, true).Cast<Component>().ToArray();
        Component[] fixturePanels = scenes.FixtureRoot.GetComponentsInChildren(mountingType, true).Cast<Component>()
            .Where(marker => (bool)mountingType.GetProperty("IsPhysicalPanel").GetValue(marker)).ToArray();
        Assert.That(productionNails.Length, Is.EqualTo(48));
        Assert.That(productionPanels.Length, Is.EqualTo(3));
        Assert.That(productionNails.Select(item => item.name).OrderBy(name => name),
            Is.EqualTo(fixtureNails.Select(item => item.name).OrderBy(name => name)));
        Assert.That(productionPanels.Select(item => item.name).OrderBy(name => name),
            Is.EqualTo(fixturePanels.Select(item => item.name).OrderBy(name => name)));

        foreach (Component item in productionNails.Cast<Component>().Concat(productionPanels))
        {
            Component[] archiveCandidates = item.GetType() == nailType ? fixtureNails : fixturePanels;
            GameObject sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(item.gameObject) as GameObject;
            string sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
            Component archived = archiveCandidates.Where(other =>
                other.name == item.name && AssetDatabase.GetAssetPath(
                    PrefabUtility.GetCorrespondingObjectFromSource(other.gameObject)) == sourcePath)
                .OrderBy(other => SquaredXZ(other.transform.position, item.transform.position)).FirstOrDefault();
            Assert.That(archived, Is.Not.Null, item.name);
            GameObject originalPrefab = PrefabUtility.GetCorrespondingObjectFromSource(item.gameObject) as GameObject;
            GameObject archivedPrefab = PrefabUtility.GetCorrespondingObjectFromSource(archived.gameObject) as GameObject;
            Assert.That(originalPrefab, Is.Not.Null, item.name);
            Assert.That(archivedPrefab, Is.Not.Null, item.name);
            Assert.That(AssetDatabase.GetAssetPath(originalPrefab), Is.EqualTo(AssetDatabase.GetAssetPath(archivedPrefab)), item.name);
            Assert.That(item.transform.position.x, Is.EqualTo(archived.transform.position.x).Within(0.00001f), item.name);
            Assert.That(item.transform.position.z, Is.EqualTo(archived.transform.position.z).Within(0.00001f), item.name);
            Assert.That(Quaternion.Angle(item.transform.rotation, archived.transform.rotation), Is.LessThan(0.001f), item.name);
            Assert.That(item.transform.lossyScale, Is.EqualTo(archived.transform.lossyScale), item.name);
            if (item.name == "FixtureNail_28")
            {
                Assert.That(item.transform.position.y - archived.transform.position.y, Is.InRange(0.02f, 0.05f));
                CapsuleCollider capsule = item.GetComponent<CapsuleCollider>();
                TerrainCollider[] terrain = production.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<TerrainCollider>(true)).ToArray();
                Assert.That(capsule, Is.Not.Null);
                Assert.That(terrain.Any(collider => Physics.ComputePenetration(capsule, capsule.transform.position,
                    capsule.transform.rotation, collider, collider.transform.position, collider.transform.rotation,
                    out _, out _)), Is.False, "The corrected nail capsule clears the production terrain.");
            }
            else
                Assert.That(item.transform.position.y, Is.EqualTo(archived.transform.position.y).Within(0.00001f), item.name);
        }

        Component[] productionSections = production.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren(sectionType, true)).Cast<Component>().ToArray();
        Assert.That(productionSections.Length, Is.EqualTo(1));
        Component main = productionSections[0];
        Assert.That(((IList)sectionType.GetProperty("PanelSlots").GetValue(main)).Count, Is.EqualTo(3));
        Assert.That(((IList)sectionType.GetProperty("MainGirders").GetValue(main)).Count, Is.EqualTo(2));
        Assert.That(sectionType.GetProperty("PhysicalPanelState").GetValue(main), Is.Not.Null);
        Component[] fixtureSections = scenes.FixtureRoot.GetComponentsInChildren(sectionType, true).Cast<Component>()
            .Where(section =>
            {
                Component marker = section.GetComponent(mountingType);
                return marker != null && (bool)mountingType.GetProperty("IsStage1TestFixture").GetValue(marker);
            }).ToArray();
        Assert.That(fixtureSections.Length, Is.EqualTo(1));
        Component fixtureSection = fixtureSections[0];
        Assert.That(((IList)sectionType.GetProperty("PanelSlots").GetValue(fixtureSection)).Count, Is.EqualTo(3));
        Assert.That(((IList)sectionType.GetProperty("MainGirders").GetValue(fixtureSection)).Count, Is.EqualTo(2));
        Assert.That(fixtureNails.Length, Is.EqualTo(48));
        Assert.That(fixturePanels.Length, Is.EqualTo(3));
        Assert.That(EditorBuildSettings.scenes.Any(scene => scene.path == BridgeAssemblyTestFixtureUtility.FixtureScenePath),
            Is.False, "The authoring fixture remains outside Build Settings.");
    }

    private static System.Type TypeByName(string name) => System.Type.GetType(name + ", Assembly-CSharp");
    private static float SquaredXZ(Vector3 a, Vector3 b) =>
        (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);
}
