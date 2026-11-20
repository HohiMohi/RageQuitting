using System;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BridgeDeckPanelStage2AuthoringEditModeTests
{
    private const string SlotPrefabPath = "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelBridgeComponent.prefab";
    private const string SectionPrefabPath = "Assets/Prefabs/New/BridgeComponents/BridgeDeckSection.prefab";
    private const string WorkflowPath = "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenDeckPanelWorkflow.asset";

    [Test]
    public void WoodenDeckPanelWorkflowOptsIntoDirectionalMountingTolerance()
    {
        UnityEngine.Object workflow = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(WorkflowPath);
        Assert.That(workflow, Is.Not.Null);
        SerializedObject serialized = new(workflow);
        Assert.That(serialized.FindProperty("physicalPanelUsesDirectionalHorizontalTolerance").boolValue, Is.True);
        Assert.That(serialized.FindProperty("physicalPanelMaximumAlongOffset").floatValue, Is.EqualTo(0.05f).Within(0.0001f));
        Assert.That(serialized.FindProperty("physicalPanelMaximumAcrossOffset").floatValue, Is.EqualTo(0.1f).Within(0.0001f));
    }

    [Test]
    public void SectionPrefabStateIsOnNonGeometryChildAndSlotOutlineIsHollowRendererOnly()
    {
        GameObject sectionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SectionPrefabPath);
        GameObject slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SlotPrefabPath);
        Type sectionType = Type.GetType("BridgeDeckSection, Assembly-CSharp");
        Type siteType = Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        Type stateType = Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        Type outlineType = Type.GetType("BridgeDeckPanelSlotOutline, Assembly-CSharp");
        Type networkObjectType = Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime");
        Assert.That(sectionPrefab, Is.Not.Null);
        Assert.That(slotPrefab, Is.Not.Null);
        Assert.That(sectionType, Is.Not.Null);
        Assert.That(stateType, Is.Not.Null);
        Assert.That(outlineType, Is.Not.Null);
        Assert.That(networkObjectType, Is.Not.Null);
        Assert.That(sectionPrefab.GetComponent(networkObjectType), Is.Null,
            "A NetworkObject on the section root would become the nail target root for every girder and panel.");

        Component section = sectionPrefab.GetComponent(sectionType);
        Component state = sectionType.GetProperty("PhysicalPanelState").GetValue(section) as Component;
        Assert.That(state, Is.Not.Null);
        Assert.That(state.transform.parent, Is.EqualTo(section.transform));
        Assert.That(state.GetComponent(networkObjectType), Is.Not.Null);
        Assert.That(state.GetComponent(stateType), Is.Not.Null);
        SerializedProperty slots = new SerializedObject(section).FindProperty("panelSlots");
        Assert.That(slots.arraySize, Is.EqualTo(7));

        GameObject slot = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab);
        try
        {
            Component site = slot.GetComponent(siteType);
            Component outline = slot.GetComponentInChildren(outlineType, true);
            LineRenderer line = outline != null ? outline.GetComponent<LineRenderer>() : null;
            Assert.That(site, Is.Not.Null);
            Assert.That(outline, Is.Not.Null);
            Assert.That(outline.transform.parent, Is.EqualTo(slot.transform),
                "The outline must not inherit PanelLayoutRoot scaling.");
            Assert.That(line, Is.Not.Null);
            InitializeOutline(outlineType, outline);
            InitializeOutline(outlineType, outline);
            outlineType.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(outline, null);
            outlineType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(outline, null);
            LineRenderer[] lines = outline.GetComponents<LineRenderer>();
            Assert.That(lines.Length, Is.EqualTo(1), "The hollow frame uses one authored renderer and no runtime components.");
            Assert.That(line.positionCount, Is.EqualTo(17), "One edge walk covers the two rings and four uprights without diagonals.");
            Vector3 a = line.transform.TransformPoint(line.GetPosition(0));
            Vector3 b = line.transform.TransformPoint(line.GetPosition(1));
            Vector3 c = line.transform.TransformPoint(line.GetPosition(2));
            Assert.That(Vector3.Distance(a, b), Is.EqualTo(2.16f).Within(0.001f));
            Assert.That(Vector3.Distance(b, c), Is.EqualTo(2.4f).Within(0.001f));
            Assert.That(line.GetPosition(0).y, Is.EqualTo(0.008f).Within(0.001f), "The lower ring follows the panel underside.");
            Assert.That(line.GetPosition(5).y, Is.EqualTo(0.208f).Within(0.001f), "The upper ring follows the panel top face.");
            AssertCuboidEdgeWalk(line, new Vector3(1.08f, 0.008f, 1.2f), 0.2f);
            float lowerWorldWidth = line.widthMultiplier * line.widthCurve.Evaluate(0f);
            float pathLength = 0f;
            float upperCornerDistance = 0f;
            for (int i = 1; i < line.positionCount; i++)
            {
                pathLength += Vector3.Distance(line.GetPosition(i - 1), line.GetPosition(i));
                if (i <= 6) upperCornerDistance += Vector3.Distance(line.GetPosition(i - 1), line.GetPosition(i));
            }
            float upperWorldWidth = line.widthMultiplier * line.widthCurve.Evaluate(upperCornerDistance / pathLength);
            Assert.That(lowerWorldWidth, Is.EqualTo(0.0195f).Within(0.0001f));
            Assert.That(upperWorldWidth, Is.EqualTo(0.015f).Within(0.0001f));
            Assert.That(lowerWorldWidth, Is.GreaterThan(upperWorldWidth),
                "The lower footprint has slightly stronger visual weight than the upper wire.");
            Assert.That(line.startColor, Is.EqualTo(Color.white));
            Assert.That(line.endColor, Is.EqualTo(Color.white));
            Assert.That(line.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
            Assert.That(line.receiveShadows, Is.False);
            Assert.That(outline.GetComponentsInChildren<Collider>(true), Is.Empty,
                "The outline hierarchy is renderer-only.");

            Assert.That(line.sharedMaterial, Is.SameAs(lines[0].sharedMaterial),
                "The edge walk retains the authored shared material.");

            bool expectedVisibleBeforeDisable = ExpectedOutlineVisible(site, slot.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp")));
            Assert.That(line.enabled, Is.EqualTo(expectedVisibleBeforeDisable),
                "The outline visibility must reflect the physical-slot and completion state.");
            outlineType.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(outline, null);
            foreach (LineRenderer segment in lines)
                Assert.That(segment.enabled, Is.False, "Disabling the outline hides every generated edge.");
            outlineType.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(outline, null);
            Assert.That(outline.GetComponents<LineRenderer>().Length, Is.EqualTo(1),
                "Repeated lifecycle calls must not add renderer components.");
            Assert.That(line.enabled, Is.EqualTo(ExpectedOutlineVisible(site, slot.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp")))),
                "Re-enabling must keep complete or unavailable slots hidden.");

            Component bridge = slot.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp"));
            GameObject ready = bridge.GetType().GetProperty("ReadyForMountingVisualsGameObject").GetValue(bridge) as GameObject;
            Assert.That(ready, Is.Not.Null);
            foreach (Renderer renderer in ready.GetComponentsInChildren<Renderer>(true))
                Assert.That(renderer.enabled, Is.False, "The former filled slot plane must not cover the physical panel.");
        }
        finally { UnityEngine.Object.DestroyImmediate(slot); }
    }

    [Test]
    public void PlacementFeedbackTintsOnlyTheRequestedRendererAndRestoresNeutralColor()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SlotPrefabPath);
        GameObject first = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        GameObject second = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        GameObject firstOwner = new("First local panel view");
        GameObject secondOwner = new("Second local panel view");
        GameObject survivingOwner = null;
        try
        {
            Type outlineType = Type.GetType("BridgeDeckPanelSlotOutline, Assembly-CSharp");
            Component firstOutline = first.GetComponentInChildren(outlineType, true);
            Component secondOutline = second.GetComponentInChildren(outlineType, true);
            InitializeOutline(outlineType, firstOutline);
            InitializeOutline(outlineType, secondOutline);
            LineRenderer firstLine = firstOutline.GetComponent<LineRenderer>();
            LineRenderer secondLine = secondOutline.GetComponent<LineRenderer>();
            LineRenderer[] firstLines = firstOutline.GetComponents<LineRenderer>();
            LineRenderer[] secondLines = secondOutline.GetComponents<LineRenderer>();
            Assert.That(firstLines.Length, Is.EqualTo(1));
            Assert.That(secondLines.Length, Is.EqualTo(1));
            Material originalMaterial = firstLine.sharedMaterial;
            Assert.That(originalMaterial, Is.Not.Null);
            foreach (LineRenderer segment in firstLines)
                Assert.That(segment.sharedMaterial, Is.SameAs(originalMaterial), "Each frame edge reuses the authored material.");
            MethodInfo set = outlineType.GetMethod("SetPlacementFeedback", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo clear = outlineType.GetMethod("ClearPlacementFeedback", BindingFlags.Instance | BindingFlags.Public);

            set.Invoke(firstOutline, new object[] { firstOwner, true });
            Color green = ReadRenderedBaseColor(firstLine);
            Assert.That(green.r, Is.EqualTo(0.18f).Within(0.001f));
            Assert.That(green.g, Is.EqualTo(0.95f).Within(0.001f));
            Assert.That(green.b, Is.EqualTo(0.25f).Within(0.001f));
            AssertRenderedBaseColor(firstLines, new Color(0.18f, 0.95f, 0.25f, 1f));

            set.Invoke(firstOutline, new object[] { secondOwner, false });
            Color orange = ReadRenderedBaseColor(firstLine);
            Assert.That(orange.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(orange.g, Is.EqualTo(0.55f).Within(0.001f));
            Assert.That(orange.b, Is.EqualTo(0.1f).Within(0.001f));
            AssertRenderedBaseColor(firstLines, new Color(1f, 0.55f, 0.1f, 1f));
            set.Invoke(secondOutline, new object[] { firstOwner, true });
            Assert.That(ReadRenderedBaseColor(secondLine).g, Is.EqualTo(0.95f).Within(0.001f));
            AssertRenderedBaseColor(secondLines, new Color(0.18f, 0.95f, 0.25f, 1f));
            Assert.That(ReadRenderedBaseColor(firstLine).g, Is.EqualTo(0.55f).Within(0.001f),
                "One view changing a different slot must not overwrite the first slot's renderer tint.");

            clear.Invoke(firstOutline, new object[] { firstOwner });
            Assert.That(ReadRenderedBaseColor(firstLine).g, Is.EqualTo(0.55f).Within(0.001f),
                "Clearing one view must retain the other local view's request.");
            clear.Invoke(firstOutline, new object[] { secondOwner });
            Color neutral = ReadRenderedBaseColor(firstLine);
            Assert.That(neutral.r, Is.EqualTo(Color.white.r).Within(0.001f));
            Assert.That(neutral.g, Is.EqualTo(Color.white.g).Within(0.001f));
            Assert.That(neutral.b, Is.EqualTo(Color.white.b).Within(0.001f));
            Assert.That(neutral.a, Is.EqualTo(originalMaterial.GetColor(Shader.PropertyToID("_BaseColor")).a).Within(0.001f),
                "Neutral feedback preserves the authored material transparency.");
            AssertRenderedBaseColor(firstLines, Color.white);
            Assert.That(firstLine.sharedMaterial, Is.SameAs(originalMaterial), "Feedback must not instantiate or replace the shared material.");

            GameObject destroyedOwner = new("Destroyed feedback owner");
            survivingOwner = new GameObject("Surviving feedback owner");
            set.Invoke(firstOutline, new object[] { survivingOwner, true });
            set.Invoke(firstOutline, new object[] { destroyedOwner, false });
            Assert.That(ReadRenderedBaseColor(firstLine).g, Is.EqualTo(0.55f).Within(0.001f));
            UnityEngine.Object.DestroyImmediate(destroyedOwner);
            outlineType.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(firstOutline, null);
            Assert.That(ReadRenderedBaseColor(firstLine).g, Is.EqualTo(0.95f).Within(0.001f),
                "A destroyed invalid owner is pruned and the surviving valid owner's green tint returns without an explicit Clear call.");
            UnityEngine.Object.DestroyImmediate(survivingOwner);
            outlineType.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(firstOutline, null);
            Assert.That(ReadRenderedBaseColor(firstLine).g, Is.EqualTo(1f).Within(0.001f),
                "Destroying the final owner restores neutral white without an explicit Clear call.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(firstOwner);
            UnityEngine.Object.DestroyImmediate(secondOwner);
            UnityEngine.Object.DestroyImmediate(survivingOwner);
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }
    }

    private static Color ReadRenderedBaseColor(LineRenderer line)
    {
        MaterialPropertyBlock block = new();
        line.GetPropertyBlock(block);
        return block.GetColor(Shader.PropertyToID("_BaseColor"));
    }

    private static void AssertRenderedBaseColor(LineRenderer[] lines, Color expected)
    {
        foreach (LineRenderer line in lines)
        {
            Color actual = ReadRenderedBaseColor(line);
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f));
            Assert.That(actual.a, Is.EqualTo(line.sharedMaterial.GetColor(Shader.PropertyToID("_BaseColor")).a).Within(0.001f));
        }
    }

    [Test]
    public void Stage02HasDistinctSectionStateObjectsAndResolversKeepFixtureGirdersSeparate()
    {
        BridgeAssemblyTestFixtureUtility fixtureScenes = new(includeProductionPreview: true);
        Scene preview = fixtureScenes.TestScene;
        try
        {
            Type sectionType = Type.GetType("BridgeDeckSection, Assembly-CSharp");
            Component fixture = null;
            Component production = null;
            foreach (GameObject root in preview.GetRootGameObjects())
            foreach (Component candidate in root.GetComponentsInChildren(sectionType, true))
            {
                Component marker = candidate.GetComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"));
                bool isFixture = marker != null && (bool)marker.GetType().GetProperty("IsStage1TestFixture").GetValue(marker);
                if (isFixture) fixture = candidate; else production = candidate;
            }
            Assert.That(fixture, Is.Not.Null);
            Assert.That(production, Is.Not.Null);
            Type noType = Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime");
            Component fixtureState = sectionType.GetProperty("PhysicalPanelState").GetValue(fixture) as Component;
            Component productionState = sectionType.GetProperty("PhysicalPanelState").GetValue(production) as Component;
            Assert.That(fixtureState, Is.Not.Null);
            Assert.That(productionState, Is.Not.Null);
            Assert.That(fixtureState, Is.Not.SameAs(productionState));
            Assert.That(fixture.GetComponent(noType), Is.Null);
            Assert.That(production.GetComponent(noType), Is.Null);
            Assert.That(fixtureState.transform.parent, Is.EqualTo(fixture.transform));
            Assert.That(productionState.transform.parent, Is.EqualTo(production.transform));
            Assert.That(fixtureState.GetComponent(noType), Is.Not.Null);
            Assert.That(productionState.GetComponent(noType), Is.Not.Null);
            var fixtureGirders = (System.Collections.IEnumerable)sectionType.GetProperty("MainGirders").GetValue(fixture);
            var productionGirders = (System.Collections.IEnumerable)sectionType.GetProperty("MainGirders").GetValue(production);
            Component[] fixtureGirderArray = System.Linq.Enumerable.Cast<Component>(fixtureGirders).ToArray();
            Component[] productionGirderArray = System.Linq.Enumerable.Cast<Component>(productionGirders).ToArray();
            Assert.That(fixtureGirderArray.Length, Is.EqualTo(2));
            Assert.That(productionGirderArray.Length, Is.EqualTo(2));
            AssertPhysicalSlotOutlines(fixture, sectionType);
            AssertPhysicalSlotOutlines(production, sectionType);

            MethodInfo resolve = Type.GetType("MountingNail, Assembly-CSharp")
                .GetMethod("ResolveTargetRoot", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(resolve, Is.Not.Null);
            Transform firstRoot = null;
            foreach (Component girder in fixtureGirderArray)
            {
                Collider collider = girder.GetComponentInChildren<Collider>(true);
                Assert.That(collider, Is.Not.Null);
                Transform resolved = resolve.Invoke(null, new object[] { collider }) as Transform;
                Assert.That(object.ReferenceEquals(resolved, girder.transform), Is.True);
                if (firstRoot == null) firstRoot = resolved;
                else Assert.That(object.ReferenceEquals(resolved, firstRoot), Is.False,
                    "Fixture main girders must resolve to distinct physical roots.");
            }

            string[] targetIds = new string[2];
            for (int i = 0; i < fixtureGirderArray.Length; i++)
            {
                Component identity = fixtureGirderArray[i].GetComponent(Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp"));
                Assert.That(identity, Is.Not.Null);
                targetIds[i] = (string)identity.GetType().GetProperty("StableTargetId").GetValue(identity);
                Assert.That(targetIds[i], Is.Not.Empty);
            }
            Assert.That(targetIds[0], Is.Not.EqualTo(targetIds[1]));
        }
        finally { fixtureScenes.Dispose(); }
    }

    private static void AssertPhysicalSlotOutlines(Component section, Type sectionType)
    {
        Type outlineType = Type.GetType("BridgeDeckPanelSlotOutline, Assembly-CSharp");
        Type siteType = Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        System.Collections.IEnumerable slots = (System.Collections.IEnumerable)sectionType.GetProperty("PanelSlots").GetValue(section);
        int checkedSlots = 0;
        foreach (Component site in System.Linq.Enumerable.Cast<Component>(slots))
        {
            if (site == null || !(bool)siteType.GetProperty("UsesPhysicalPanelPlacement").GetValue(site)) continue;
            Component outline = site.GetComponentInChildren(outlineType, true);
            Assert.That(outline, Is.Not.Null, $"{section.name} has a physical slot without its hollow outline.");
            LineRenderer line = outline.GetComponent<LineRenderer>();
            Assert.That(line, Is.Not.Null);
            InitializeOutline(outlineType, outline);
            outlineType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(outline, null);
            LineRenderer[] lines = outline.GetComponents<LineRenderer>();
            Assert.That(lines.Length, Is.EqualTo(1));
            Vector3 a = line.transform.TransformPoint(line.GetPosition(0));
            Vector3 b = line.transform.TransformPoint(line.GetPosition(1));
            Vector3 c = line.transform.TransformPoint(line.GetPosition(2));
            Assert.That(Vector3.Distance(a, b), Is.EqualTo(2.16f).Within(0.001f));
            Assert.That(Vector3.Distance(b, c), Is.EqualTo(2.4f).Within(0.001f));
            Assert.That(line.GetPosition(0).y, Is.EqualTo(0.008f).Within(0.001f));
            Assert.That(line.GetPosition(5).y, Is.EqualTo(0.208f).Within(0.001f),
                "The production slot outline must include the panel's top face.");
            Assert.That(line.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
            Assert.That(line.receiveShadows, Is.False);
            Assert.That(outline.GetComponentsInChildren<Collider>(true), Is.Empty);
            Component bridge = site.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp"));
            GameObject filled = (GameObject)bridge.GetType().GetProperty("ReadyForMountingVisualsGameObject").GetValue(bridge);
            foreach (Renderer renderer in filled.GetComponentsInChildren<Renderer>(true))
                Assert.That(renderer.enabled, Is.False, "Physical slot must not retain the white filled plane.");
            checkedSlots++;
        }
        Assert.That(checkedSlots, Is.GreaterThan(0), $"{section.name} must author at least one physical panel slot.");
    }

    private static void InitializeOutline(Type outlineType, Component outline)
        => outlineType.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(outline, null);

    private static bool ExpectedOutlineVisible(Component site, Component bridge)
    {
        if (site == null || bridge == null) return false;
        if (!(bool)site.GetType().GetProperty("UsesPhysicalPanelPlacement").GetValue(site)) return false;
        if ((bool)site.GetType().GetProperty("IsComplete").GetValue(site)) return false;
        if ((bool)bridge.GetType().GetProperty("StartFullyCompleted").GetValue(bridge)) return false;
        Component section = site.GetType().GetProperty("DeckSection").GetValue(site) as Component;
        if (section == null) return true;
        Component state = section.GetType().GetProperty("PhysicalPanelState").GetValue(section) as Component;
        if (state == null) return true;
        int slotIndex = (int)site.GetType().GetProperty("SlotIndex").GetValue(site);
        return !(bool)state.GetType().GetMethod("IsSlotComplete").Invoke(state, new object[] { slotIndex });
    }

    private static void AssertCuboidEdgeWalk(LineRenderer line, Vector3 halfExtents, float height)
    {
        var expectedEdges = new System.Collections.Generic.HashSet<string>();
        Vector3[] corners =
        {
            new(-halfExtents.x, 0.008f, -halfExtents.z), new(halfExtents.x, 0.008f, -halfExtents.z),
            new(halfExtents.x, 0.008f, halfExtents.z), new(-halfExtents.x, 0.008f, halfExtents.z),
            new(-halfExtents.x, 0.008f + height, -halfExtents.z), new(halfExtents.x, 0.008f + height, -halfExtents.z),
            new(halfExtents.x, 0.008f + height, halfExtents.z), new(-halfExtents.x, 0.008f + height, halfExtents.z)
        };
        for (int i = 0; i < line.positionCount; i++) Assert.That(corners, Does.Contain(line.GetPosition(i)), $"Position {i} must be a cuboid corner.");
        for (int i = 0; i < line.positionCount - 1; i++)
        {
            Vector3 from = line.GetPosition(i), to = line.GetPosition(i + 1);
            float dx = Mathf.Abs(from.x - to.x), dy = Mathf.Abs(from.y - to.y), dz = Mathf.Abs(from.z - to.z);
            Assert.That((dx > 0f ? 1 : 0) + (dy > 0f ? 1 : 0) + (dz > 0f ? 1 : 0), Is.EqualTo(1),
                $"Segment {i} must follow one cuboid axis without a diagonal.");
            string a = $"{from.x:F3},{from.y:F3},{from.z:F3}";
            string b = $"{to.x:F3},{to.y:F3},{to.z:F3}";
            expectedEdges.Add(string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}");
        }
        Assert.That(expectedEdges.Count, Is.EqualTo(12), "The walk must cover each unique cuboid edge.");
        for (int i = 0; i < corners.Length; i++)
        {
            bool found = false;
            for (int j = 0; j < line.positionCount; j++) found |= Vector3.Distance(corners[i], line.GetPosition(j)) < 0.001f;
            Assert.That(found, Is.True, $"Cuboid corner {corners[i]} must be present in the walk.");
        }
    }

    [Test]
    public void StartFullyCompletedSlotKeepsFinalVisualAndPhysicalCollidersWhileHidingOutline()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SlotPrefabPath);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            System.Type siteType = Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
            Component bridge = instance.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp"));
            SerializedObject data = new(bridge);
            data.FindProperty("startFullyCompleted").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            bridge.GetType().GetMethod("RefreshInitialStatePreview", BindingFlags.Instance | BindingFlags.Public).Invoke(bridge, null);
            GameObject ready = bridge.GetType().GetProperty("ReadyForMountingVisualsGameObject").GetValue(bridge) as GameObject;
            SerializedProperty serialized = new SerializedObject(bridge).FindProperty("mountedComponentVisualsGameObject");
            GameObject finalVisual = serialized.objectReferenceValue as GameObject;
            Assert.That(finalVisual, Is.Not.Null);
            Assert.That(finalVisual.activeSelf, Is.True);
            Assert.That(ready.activeSelf, Is.False);
            System.Type promptType = Type.GetType("InteractionPrompt, Assembly-CSharp");
            System.Type promptListType = typeof(System.Collections.Generic.List<>).MakeGenericType(promptType);
            object prompts = System.Activator.CreateInstance(promptListType);
            siteType.GetMethod("GetInteractionPrompts", BindingFlags.Instance | BindingFlags.Public)
                .Invoke(instance.GetComponent(siteType), new[] { null, prompts });
            object prompt = promptListType.GetProperty("Item").GetValue(prompts, new object[] { 0 });
            Assert.That(promptType.GetField("Description").GetValue(prompt), Is.EqualTo("Deck panel secured"));
            Component outline = instance.GetComponentInChildren(Type.GetType("BridgeDeckPanelSlotOutline, Assembly-CSharp"), true);
            outline.GetType().GetMethod("RefreshVisibility", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(outline, null);
            Assert.That(outline.GetComponent<LineRenderer>().enabled, Is.False);
            SerializedProperty physicalColliders = new SerializedObject(bridge).FindProperty("mountedPhysicalColliders");
            for (int i = 0; i < physicalColliders.arraySize; i++)
            {
                Collider collider = physicalColliders.GetArrayElementAtIndex(i).objectReferenceValue as Collider;
                if (collider != null) Assert.That(collider.enabled, Is.True);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

    [Test]
    public void UnconfiguredSectionAndStateCanBeEnabledWithoutSlotOrGirderArrays()
    {
        GameObject root = new("Unconfigured panel section fixture");
        try
        {
            Component section = root.AddComponent(Type.GetType("BridgeDeckSection, Assembly-CSharp"));
            GameObject stateRoot = new("State carrier");
            stateRoot.transform.SetParent(root.transform, false);
            Component state = stateRoot.AddComponent(Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp"));
            Assert.That(section.GetType().GetProperty("PanelSlots").GetValue(section), Is.Null);
            Assert.That(section.GetType().GetProperty("MainGirders").GetValue(section), Is.Null);
            int slot = (int)state.GetType().GetMethod("FindNearestSlot", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, new object[] { Vector3.zero });
            Assert.That(slot, Is.EqualTo(-1), "An unconfigured section safely reports no physical slot.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void LegacyCompletedSlotRejectsASecondPhysicalPanelClaim()
    {
        BridgeAssemblyTestFixtureUtility fixtureScenes = new(includeProductionPreview: false);
        Scene preview = fixtureScenes.TestScene;
        GameObject panel = new("Panel claiming legacy-completed slot");
        try
        {
            SceneManager.MoveGameObjectToScene(panel, preview);
            Type sectionType = Type.GetType("BridgeDeckSection, Assembly-CSharp");
            Component fixture = null;
            foreach (GameObject root in preview.GetRootGameObjects())
            foreach (Component candidate in root.GetComponentsInChildren(sectionType, true))
            {
                Component marker = candidate.GetComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"));
                bool isFixture = marker != null && (bool)marker.GetType().GetProperty("IsStage1TestFixture").GetValue(marker);
                if (isFixture) { fixture = candidate; break; }
            }
            Assert.That(fixture, Is.Not.Null);
            Component state = (Component)sectionType.GetProperty("PhysicalPanelState").GetValue(fixture);
            Component site = System.Linq.Enumerable.Cast<Component>((System.Collections.IEnumerable)sectionType.GetProperty("PanelSlots").GetValue(fixture))
                .First(candidate => candidate != null && (bool)candidate.GetType().GetProperty("UsesPhysicalPanelPlacement").GetValue(candidate));
            Component bridge = site.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp"));
            SerializedObject serialized = new(bridge);
            serialized.FindProperty("startFullyCompleted").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            bridge.GetType().GetMethod("RefreshInitialStatePreview", BindingFlags.Instance | BindingFlags.Public).Invoke(bridge, null);
            panel.AddComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"));
            int slotIndex = (int)site.GetType().GetProperty("SlotIndex").GetValue(site);
            state.GetType().GetMethod("EnsureInitialized", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(state, null);
            bool claimed = (bool)state.GetType().GetMethod("HasClaim", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, new object[] { panel.GetComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp")), slotIndex, true });
            Assert.That(claimed, Is.True, "A legacy final visual/collider occupies the physical slot for the session.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(panel);
            fixtureScenes.Dispose();
        }
    }

    [Test]
    public void LegacyCompletedSlotInAnotherSectionDoesNotBlockFreeSlot()
    {
        BridgeAssemblyTestFixtureUtility fixtureScenes = new(includeProductionPreview: true);
        Scene preview = fixtureScenes.TestScene;
        GameObject panel = new("Panel for free production slot");
        System.Collections.IList activeStates = null;
        Component productionState = null;
        Component fixtureState = null;
        try
        {
            SceneManager.MoveGameObjectToScene(panel, preview);
            Type sectionType = Type.GetType("BridgeDeckSection, Assembly-CSharp");
            Component fixture = null;
            Component production = null;
            foreach (GameObject root in preview.GetRootGameObjects())
            foreach (Component candidate in root.GetComponentsInChildren(sectionType, true))
            {
                Component marker = candidate.GetComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"));
                bool isFixture = marker != null && (bool)marker.GetType().GetProperty("IsStage1TestFixture").GetValue(marker);
                if (isFixture) fixture = candidate; else production = candidate;
            }
            Assert.That(fixture, Is.Not.Null);
            Assert.That(production, Is.Not.Null);

            Component fixtureSite = System.Linq.Enumerable.Cast<Component>((System.Collections.IEnumerable)sectionType.GetProperty("PanelSlots").GetValue(fixture))
                .First(candidate => candidate != null && (bool)candidate.GetType().GetProperty("UsesPhysicalPanelPlacement").GetValue(candidate));
            Component fixtureBridge = fixtureSite.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp"));
            SerializedObject fixtureSerialized = new(fixtureBridge);
            fixtureSerialized.FindProperty("startFullyCompleted").boolValue = true;
            fixtureSerialized.ApplyModifiedPropertiesWithoutUndo();
            fixtureBridge.GetType().GetMethod("RefreshInitialStatePreview", BindingFlags.Instance | BindingFlags.Public).Invoke(fixtureBridge, null);

            Component productionSite = System.Linq.Enumerable.Cast<Component>((System.Collections.IEnumerable)sectionType.GetProperty("PanelSlots").GetValue(production))
                .First(candidate => candidate != null && (bool)candidate.GetType().GetProperty("UsesPhysicalPanelPlacement").GetValue(candidate));
            panel.transform.position = productionSite.transform.position;
            Component markerOnPanel = panel.AddComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"));
            productionState = (Component)sectionType.GetProperty("PhysicalPanelState").GetValue(production);
            fixtureState = (Component)sectionType.GetProperty("PhysicalPanelState").GetValue(fixture);
            MethodInfo initialize = productionState.GetType().GetMethod("EnsureInitialized", BindingFlags.Instance | BindingFlags.NonPublic);
            initialize.Invoke(productionState, null);
            initialize.Invoke(fixtureState, null);
            activeStates = (System.Collections.IList)productionState.GetType()
                .GetField("ActiveSections", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (!activeStates.Contains(productionState)) activeStates.Add(productionState);
            if (!activeStates.Contains(fixtureState)) activeStates.Add(fixtureState);
            int slotIndex = (int)productionSite.GetType().GetProperty("SlotIndex").GetValue(productionSite);
            bool claimed = (bool)productionState.GetType().GetMethod("IsClaimedByAnotherSlot", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(productionState, new object[] { markerOnPanel, slotIndex });
            Assert.That(claimed, Is.False,
                "A legacy completion in a distant section must not occupy this section's independent slot.");
        }
        finally
        {
            if (activeStates != null)
            {
                if (productionState != null) activeStates.Remove(productionState);
                if (fixtureState != null) activeStates.Remove(fixtureState);
            }
            UnityEngine.Object.DestroyImmediate(panel);
            fixtureScenes.Dispose();
        }
    }
}
