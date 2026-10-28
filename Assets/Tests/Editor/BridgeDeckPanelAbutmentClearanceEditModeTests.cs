using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BridgeDeckPanelAbutmentClearanceEditModeTests
{
    private const string ScenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
    private const string PanelPath = "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab";
    private Scene targetScene;
    private string sourceSceneHash;

    [SetUp]
    public void SetUp()
    {
        sourceSceneHash = ComputeSourceSceneHash();
        targetScene = EditorSceneManager.OpenPreviewScene(ScenePath);
    }

    [TearDown]
    public void TearDown()
    {
        if (targetScene.IsValid() && targetScene.isLoaded)
            EditorSceneManager.ClosePreviewScene(targetScene);
        Assert.That(ComputeSourceSceneHash(), Is.EqualTo(sourceSceneHash),
            "The disposable geometry preview must not mutate the accepted source scene.");
    }

    [Test]
    public void MainPanelSlotPosesClearActualAbutmentCollidersAndMatchGirderTop()
    {
        Transform section = FindTransform("TutorialGameplay/Bridge/BridgeDeckSection");
        Assert.That(section, Is.Not.Null, "The test measures the authored tutorial bridge section.");
        Component sectionComponent = section.GetComponent(TypeByName("BridgeDeckSection"));
        Assert.That(sectionComponent, Is.Not.Null);
        IList slots = (IList)sectionComponent.GetType().GetProperty("PanelSlots").GetValue(sectionComponent);
        IList girders = (IList)sectionComponent.GetType().GetProperty("MainGirders").GetValue(sectionComponent);
        Assert.That(slots.Count, Is.EqualTo(3));
        Assert.That(girders.Count, Is.EqualTo(2));
        SerializedObject sectionData = new(sectionComponent);
        Assert.That(sectionData.FindProperty("sectionLength").floatValue, Is.EqualTo(6.56f).Within(0.001f));
        Assert.That(sectionData.FindProperty("nominalPanelLength").floatValue, Is.EqualTo(2.16f).Within(0.001f));

        Transform[] abutmentRoots = section.parent.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("WoodenAbutmentBridgeComponent", StringComparison.Ordinal))
            .ToArray();
        Assert.That(abutmentRoots.Length, Is.EqualTo(2), "Use only the two abutments belonging to the main bridge.");
        Type bridgeComponentType = TypeByName("BridgeComponent");
        foreach (Transform abutmentRoot in abutmentRoots)
            ConfigureCompletedPreview(abutmentRoot.GetComponentInChildren(bridgeComponentType, true));
        foreach (Component girder in girders)
            ConfigureCompletedPreview(girder.GetComponentInChildren(bridgeComponentType, true));

        Collider[] abutments = abutmentRoots.SelectMany(root => root.GetComponentsInChildren<Collider>(true))
            .Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.bounds.size.sqrMagnitude > 0.01f)
            .Distinct().ToArray();
        Assert.That(abutments, Is.Not.Empty, "Use every active solid collider on the real main abutments.");

        GameObject panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        Assert.That(panelPrefab, Is.Not.Null);
        GameObject panel = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, targetScene);
        try
        {
            Collider panelCollider = panel.GetComponent<Collider>();
            Assert.That(panelCollider, Is.TypeOf<BoxCollider>());
            Assert.That(panel.activeInHierarchy && panelCollider.enabled, Is.True);
            Assert.That(((BoxCollider)panelCollider).size, Is.EqualTo(new Vector3(2.16f, 0.2f, 2.4f)));

            int penetrations = 0;
            float deepestPenetration = 0f;
            float[] localCenters = new float[slots.Count];
            List<float> panelBottoms = new();
            List<float> physicalSupportTops = new();
            List<float> visibleSupportTops = new();
            for (int i = 0; i < slots.Count; i++)
            {
                Transform slot = ((Component)slots[i]).transform;
                localCenters[i] = section.InverseTransformPoint(slot.position).x;
                panel.transform.SetPositionAndRotation(slot.position, slot.rotation);
                Physics.SyncTransforms();
                foreach (float yaw in new[] { 0f, 180f })
                {
                    Quaternion panelRotation = slot.rotation * Quaternion.Euler(0f, yaw, 0f);
                    foreach (Collider abutment in abutments)
                    {
                        if (!Physics.ComputePenetration(panelCollider, slot.position, panelRotation,
                                abutment, abutment.transform.position, abutment.transform.rotation,
                                out _, out float depth)) continue;
                        penetrations++;
                        deepestPenetration = Mathf.Max(deepestPenetration, depth);
                    }
                }

                Bounds panelBounds = panelCollider.bounds;
                Collider[] supports = girders.Cast<Component>()
                    .SelectMany(g => g.GetComponentsInChildren<Collider>(true))
                    .Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger
                                && c.bounds.size.sqrMagnitude > 0.01f
                                && c.bounds.min.x <= panelBounds.max.x && c.bounds.max.x >= panelBounds.min.x
                                && c.bounds.min.z <= panelBounds.max.z && c.bounds.max.z >= panelBounds.min.z)
                    .ToArray();
                Assert.That(supports, Is.Not.Empty, $"Slot {i} must overlap the physical girder collider supports.");
                physicalSupportTops.Add(supports.Max(c => c.bounds.max.y));
                Renderer[] visibleSupports = girders.Cast<Component>()
                    .SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy
                                && (r.name == "MainBeam" || r.name == "StartPlate" || r.name == "EndPlate")
                                && r.bounds.size.sqrMagnitude > 0.01f
                                && r.bounds.min.x <= panelBounds.max.x && r.bounds.max.x >= panelBounds.min.x
                                && r.bounds.min.z <= panelBounds.max.z && r.bounds.max.z >= panelBounds.min.z)
                    .ToArray();
                Assert.That(visibleSupports, Is.Not.Empty, $"Slot {i} must overlap visible physical girder mesh.");
                visibleSupportTops.Add(visibleSupports.Max(r => r.bounds.max.y));
                panelBottoms.Add(panelBounds.min.y);
            }

            List<string> failures = new();
            if (penetrations != 0)
                failures.Add($"actual main-abutment penetration across 0/180-degree poses: {penetrations} overlaps, deepest {deepestPenetration:F3} m");
            if (Mathf.Abs(localCenters[0] + 2.2f) > 0.001f || Mathf.Abs(localCenters[1]) > 0.001f || Mathf.Abs(localCenters[2] - 2.2f) > 0.001f)
                failures.Add($"slot centers are not mirrored at -2.2/0/+2.2 m: [{string.Join(", ", localCenters.Select(x => x.ToString("F3")))}]");
            for (int i = 0; i < slots.Count; i++)
            {
                if (i < slots.Count - 1)
                {
                    float actualGap = localCenters[i + 1] - localCenters[i] - 2.16f;
                    if (Mathf.Abs(actualGap - 0.04f) > 0.001f)
                        failures.Add($"slot gap {i}-{i + 1} is {actualGap:F3} m, expected 0.040 m");
                }
                if (Mathf.Abs(panelBottoms[i] - physicalSupportTops[i]) > 0.03f)
                    failures.Add($"slot {i} underside differs from physical girder collider top by {panelBottoms[i] - physicalSupportTops[i]:F3} m (bottom {panelBottoms[i]:F3}, collider top {physicalSupportTops[i]:F3}); tolerance is 0.030 m");
                if (Mathf.Abs(panelBottoms[i] - visibleSupportTops[i]) > 0.03f)
                    failures.Add($"slot {i} underside differs from overlapping visible beam mesh top by {panelBottoms[i] - visibleSupportTops[i]:F3} m (bottom {panelBottoms[i]:F3}, mesh top {visibleSupportTops[i]:F3}); tolerance is 0.030 m");
            }
            Assert.That(failures, Is.Empty, string.Join("; ", failures));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(panel);
        }
    }

    [Test]
    public void FixtureSlotsRemainAtTheirMeasuredGenericGirderTop()
    {
        BridgeAssemblyTestFixtureUtility fixtureScenes = new(includeProductionPreview: false);
        try
        {
        Type sectionType = TypeByName("BridgeDeckSection");
        Type markerType = TypeByName("BridgeDeckPanelMounting");
        Component fixture = fixtureScenes.FixtureRoot
            .GetComponentsInChildren(sectionType, true).Cast<Component>()
            .FirstOrDefault(section =>
            {
                Component marker = section.GetComponent(markerType);
                return marker != null && (bool)markerType.GetProperty("IsStage1TestFixture").GetValue(marker);
            });
        Assert.That(fixture, Is.Not.Null);
        SerializedObject data = new(fixture);
        Assert.That(data.FindProperty("sectionLength").floatValue, Is.EqualTo(6.56f).Within(0.001f));
        Assert.That(data.FindProperty("nominalPanelLength").floatValue, Is.EqualTo(2.16f).Within(0.001f));
        IList slots = (IList)sectionType.GetProperty("PanelSlots").GetValue(fixture);
        IList girders = (IList)sectionType.GetProperty("MainGirders").GetValue(fixture);
        Assert.That(slots.Count, Is.EqualTo(3));
        Assert.That(girders.Count, Is.EqualTo(2));

        List<string> failures = new();
        for (int i = 0; i < slots.Count; i++)
        {
            Transform slot = ((Component)slots[i]).transform;
            float localX = fixture.transform.InverseTransformPoint(slot.position).x;
            float localY = fixture.transform.InverseTransformPoint(slot.position).y;
            if (Mathf.Abs(localX - (i - 1) * 2.2f) > 0.001f)
                failures.Add($"fixture slot {i} center X is {localX:F3} m, expected {(i - 1) * 2.2f:F3} m");
            if (Mathf.Abs(localY - 0.275f) > 0.001f)
                failures.Add($"fixture slot {i} bottom is {localY:F3} m, expected 0.275 m");
        }

        foreach (Component girder in girders)
        {
            BoxCollider support = girder.GetComponent<BoxCollider>();
            Assert.That(support, Is.Not.Null, $"{girder.name} must expose its actual solid support collider.");
            float localTop = fixture.transform.InverseTransformPoint(support.bounds.max).y;
            if (Mathf.Abs(localTop - 0.275f) > 0.001f)
                failures.Add($"{girder.name} collider top is {localTop:F3} m, expected 0.275 m");
        }
        Assert.That(failures, Is.Empty, string.Join("; ", failures));
        }
        finally
        {
            fixtureScenes.Dispose();
        }
    }

    private Transform FindTransform(string path)
    {
        string[] parts = path.Split('/');
        foreach (GameObject root in targetScene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform current = root.transform;
            for (int i = 1; i < parts.Length && current != null; i++) current = current.Find(parts[i]);
            if (current != null) return current;
        }
        return null;
    }

    private static Type TypeByName(string name) => Type.GetType(name + ", Assembly-CSharp");

    private static void ConfigureCompletedPreview(Component component)
    {
        Assert.That(component, Is.Not.Null, "Each measured bridge member must use its BridgeComponent preview state.");
        SerializedObject serialized = new(component);
        SerializedProperty completed = serialized.FindProperty("startFullyCompleted");
        Assert.That(completed, Is.Not.Null, "BridgeComponent must expose its authored initial completion state.");
        completed.boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        MethodInfo refresh = component.GetType().GetMethod("RefreshInitialStatePreview",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.That(refresh, Is.Not.Null, "Completed preview geometry must use the production preview refresh path.");
        refresh.Invoke(component, null);
    }

    private static string ComputeSourceSceneHash()
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(ScenePath))).Replace("-", string.Empty);
    }
}
