using System;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MountingNailStage1EditModeTests
{
    [Test]
    public void NailProfilePrefabAndDefaultNetworkRegistrationHaveStageOneDefaults()
    {
        const string prefabPath = "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab";
        const string profilePath = "Assets/ScriptableObjectAssets/New/MountingNailProfiles/StandardMountingNailProfile.asset";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null);
        Type nailType = Type.GetType("MountingNail, Assembly-CSharp");
        Component nail = prefab.GetComponent(nailType);
        Assert.That(nail, Is.Not.Null);
        object profile = nailType.GetProperty("Profile", BindingFlags.Instance | BindingFlags.Public).GetValue(nail);
        Assert.That(profile, Is.SameAs(AssetDatabase.LoadAssetAtPath<ScriptableObject>(profilePath)));
        Assert.That(ReadFloat(profile, "LengthMeters"), Is.EqualTo(0.35f).Within(0.0001f));
        Assert.That(ReadFloat(profile, "VisualWidthMultiplier"), Is.EqualTo(8f).Within(0.0001f));
        Assert.That(ReadFloat(profile, "MassKg"), Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(ReadFloat(profile, "PreparedTimeoutSeconds"), Is.EqualTo(3f).Within(0.0001f));
        Assert.That(ReadFloat(profile, "TipContactToleranceMeters"), Is.EqualTo(0.02f).Within(0.0001f));
        Assert.That(ReadFloat(profile, "PenetrationRangeMeters"), Is.EqualTo(0.3f).Within(0.0001f));
        Assert.That(ReadFloat(profile, "AimRadiusMeters"), Is.EqualTo(0.06f).Within(0.0001f));
        Assert.That(ReadInt(profile, "RequiredHitsForSingleObject"), Is.EqualTo(1));
        Assert.That(ReadInt(profile, "RequiredHitsForTwoObjects"), Is.EqualTo(3));
        Component networkObject = prefab.GetComponent(Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime"));
        Assert.That(networkObject, Is.Not.Null);
        SerializedProperty prefabHash = new SerializedObject(networkObject).FindProperty("GlobalObjectIdHash");
        Assert.That(prefabHash.uintValue, Is.Not.Zero, "The saved and reloaded NGO prefab hash must be persisted.");
        string prefabYaml = File.ReadAllText(prefabPath);
        Assert.That(prefabYaml, Does.Contain("GlobalObjectIdHash: " + prefabHash.uintValue),
            "The NGO hash must survive a disk save, not only be available on the loaded object.");
        Assert.That(prefab.GetComponent<Rigidbody>().mass, Is.EqualTo(0.1f).Within(0.0001f));
        CapsuleCollider capsule = prefab.GetComponent<CapsuleCollider>();
        Assert.That(capsule.height, Is.EqualTo(0.35f).Within(0.0001f));
        Transform visual = (Transform)nailType.GetField("visual", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(nail);
        Vector3 authoredScale = (Vector3)nailType.GetField("authoredVisualScale", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(nail);
        float authoredRadius = (float)nailType.GetField("authoredCapsuleRadius", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(nail);
        Assert.That(visual.localScale.x, Is.EqualTo(authoredScale.x * 8f).Within(0.0001f));
        Assert.That(visual.localScale.z, Is.EqualTo(authoredScale.z * 8f).Within(0.0001f));
        Assert.That(capsule.radius, Is.EqualTo(authoredRadius * 8f).Within(0.0001f));

        Type listType = Type.GetType("Unity.Netcode.NetworkPrefabsList, Unity.Netcode.Runtime");
        ScriptableObject listAsset = AssetDatabase.LoadAssetAtPath("Assets/DefaultNetworkPrefabs.asset", listType) as ScriptableObject;
        Assert.That(listAsset, Is.Not.Null);
        SerializedProperty list = new SerializedObject(listAsset).FindProperty("List");
        Assert.That(Enumerable.Range(0, list.arraySize).Any(i =>
            list.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == prefab), Is.True);
    }

    [Test]
    public void DedicatedTestSceneRetainsTheStageOneFixtureAndSupplyNails()
    {
        const string path = BridgeAssemblyTestFixtureUtility.FixtureScenePath;
        Scene activeBefore = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.OpenPreviewScene(path);
        try
        {
            GameObject area = scene.GetRootGameObjects().SingleOrDefault(x => x.name == "NailTestArea");
            Assert.That(area, Is.Not.Null);
            Assert.That(Vector3.Distance(area.transform.position, new Vector3(-6f, area.transform.position.y, 2f)), Is.LessThan(0.01f));
            Type nailType = Type.GetType("MountingNail, Assembly-CSharp");
            Type panelMarkerType = Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
            Assert.That(panelMarkerType, Is.Not.Null);
            PropertyInfo isFixtureProperty = panelMarkerType.GetProperty("IsStage1TestFixture");
            Component[] fixtureMarkers = area.GetComponentsInChildren(panelMarkerType, true)
                .Where(marker => (bool)isFixtureProperty.GetValue(marker)).Cast<Component>().ToArray();
            Assert.That(fixtureMarkers.Length, Is.EqualTo(1), "Exactly one explicitly marked Stage1 fixture is added under NailTestArea.");
            Component[] allNails = area.GetComponentsInChildren(nailType, true).Cast<Component>().ToArray();
            int fixtureNailCount = allNails.Count(nail => nail.transform.IsChildOf(fixtureMarkers[0].transform));
            Assert.That(allNails.Length, Is.EqualTo(48), "The 12 existing nails remain alongside the 36 fixture nails.");
            Assert.That(fixtureNailCount, Is.EqualTo(36), "The Stage1 fixture contributes exactly 36 nails.");
            Assert.That(allNails.Length - fixtureNailCount, Is.EqualTo(12), "All 12 pre-existing NailTestArea nails remain outside the fixture.");
            Assert.That(area.transform.Find("Board_Single"), Is.Not.Null);
            Assert.That(area.transform.Find("Board_Stacked_Lower"), Is.Not.Null);
            Assert.That(area.transform.Find("Board_Stacked_Upper"), Is.Not.Null);
            Assert.That(area.transform.Find("Board_Vertical"), Is.Not.Null);
            Assert.That(area.transform.Find("IndustrialHammer_LaterStages"), Is.Not.Null);
            Assert.That(activeBefore.path, Is.EqualTo(SceneManager.GetActiveScene().path));
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static float ReadFloat(object asset, string property) => (float)asset.GetType()
        .GetProperty(property, BindingFlags.Instance | BindingFlags.Public).GetValue(asset);
    private static int ReadInt(object asset, string property) => (int)asset.GetType()
        .GetProperty(property, BindingFlags.Instance | BindingFlags.Public).GetValue(asset);
}




