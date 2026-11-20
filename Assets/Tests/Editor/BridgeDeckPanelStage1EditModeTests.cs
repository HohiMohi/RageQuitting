using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class BridgeDeckPanelStage1EditModeTests
{
    private const string PanelPath = "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab";
    private const string ProfilePath = "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenDeckPanelMountable.asset";
    private const string WorkflowPath = "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenDeckPanelWorkflow.asset";
    private const string SectionPath = "Assets/Prefabs/New/BridgeComponents/BridgeDeckSection.prefab";

    [Test]
    public void PhysicalPanelProduct_HasFixedDimensionsAndSingleCarrierProfile()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        ScriptableObject profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ProfilePath);
        ScriptableObject workflow = AssetDatabase.LoadAssetAtPath<ScriptableObject>(WorkflowPath);
        Type markerType = Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(profile, Is.Not.Null);
        Assert.That(workflow, Is.Not.Null);
        Assert.That(markerType, Is.Not.Null);
        Assert.That((bool)markerType.GetProperty("IsPhysicalPanel").GetValue(prefab.GetComponent(markerType)), Is.True);
        Assert.That((bool)profile.GetType().GetField("allowMultipleCarriers").GetValue(profile), Is.False);
        Assert.That((int)profile.GetType().GetField("minAmountOfPlayersNeeded").GetValue(profile), Is.EqualTo(1));
        Assert.That((int)profile.GetType().GetField("recommendedCarriers").GetValue(profile), Is.EqualTo(1));
        Assert.That((int)profile.GetType().GetField("maxCarriers").GetValue(profile), Is.EqualTo(1));
        Assert.That((bool)workflow.GetType().GetProperty("UsesPhysicalPanelPlacement").GetValue(workflow), Is.True);
        Assert.That((Vector3)workflow.GetType().GetProperty("PhysicalPanelDimensions").GetValue(workflow), Is.EqualTo(new Vector3(2.16f, 0.2f, 2.4f)));
        Assert.That(prefab.GetComponent<Rigidbody>().mass, Is.EqualTo(1f));
        Assert.That(prefab.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(2.16f, 0.2f, 2.4f)));
        Assert.That(prefab.GetComponent<BoxCollider>().center, Is.EqualTo(new Vector3(0f, 0.1f, 0f)));

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            Physics.SyncTransforms();
            Bounds modelBounds = default;
            bool hasModelBounds = false;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.bounds.size.sqrMagnitude < 0.01f) continue;
                if (!hasModelBounds) { modelBounds = renderer.bounds; hasModelBounds = true; }
                else modelBounds.Encapsulate(renderer.bounds);
            }
            Assert.That(hasModelBounds, Is.True);
            Assert.That(modelBounds.size.x, Is.EqualTo(2.16f).Within(0.01f));
            Assert.That(modelBounds.size.y, Is.EqualTo(0.2f).Within(0.01f));
            Assert.That(modelBounds.size.z, Is.EqualTo(2.4f).Within(0.01f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void LegacyPanelWorkflowRemainsOptInAndSectionPrefabSlotsStayPassive()
    {
        Type workflowType = Type.GetType("BridgeDeckPanelConstructionWorkflowSO, Assembly-CSharp");
        Type markerType = Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        Type sectionType = Type.GetType("BridgeDeckSection, Assembly-CSharp");
        Type siteType = Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        ScriptableObject fresh = ScriptableObject.CreateInstance(workflowType);
        Assert.That((bool)workflowType.GetProperty("UsesPhysicalPanelPlacement").GetValue(fresh), Is.False,
            "Existing assets remain on the legacy workflow unless explicitly opted in.");
        UnityEngine.Object.DestroyImmediate(fresh);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SectionPath);
        Component section = prefab.GetComponent(sectionType);
        Assert.That(section, Is.Not.Null);
        SerializedObject data = new(section);
        SerializedProperty slots = data.FindProperty("panelSlots");
        Assert.That(data.FindProperty("sectionLength").floatValue, Is.EqualTo(14f), "The generic seven-slot prefab keeps its existing length.");
        Assert.That(slots.arraySize, Is.EqualTo(7));
        for (int i = 0; i < slots.arraySize; i++)
        {
            GameObject slot = ((Component)slots.GetArrayElementAtIndex(i).objectReferenceValue).gameObject;
            Assert.That((bool)markerType.GetProperty("IsPassiveSlot").GetValue(slot.GetComponent(markerType)), Is.True);
            Assert.That((bool)siteType.GetProperty("CanAcceptMountedComponent").GetValue(slot.GetComponent(siteType)), Is.False);
        }
    }

    [Test]
    public void PassivePhysicalSlotRejectsLegacyMountingToolsAndPrompts()
    {
        Type markerType = Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        Type siteType = Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        Type toolType = Type.GetType("EquippableItemType, Assembly-CSharp");
        Type promptType = Type.GetType("InteractionPrompt, Assembly-CSharp");
        GameObject sectionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SectionPath);
        Component prefabSection = sectionPrefab.GetComponent(Type.GetType("BridgeDeckSection, Assembly-CSharp"));
        GameObject slotPrefab = ((Component)new SerializedObject(prefabSection).FindProperty("panelSlots")
            .GetArrayElementAtIndex(0).objectReferenceValue).gameObject;
        GameObject slot = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab);
        try
        {
            Component marker = slot.GetComponent(markerType);
            Component site = slot.GetComponent(siteType);
            Assert.That((bool)markerType.GetProperty("IsPassiveSlot").GetValue(marker), Is.True);
            int stageBefore = (int)siteType.GetProperty("CurrentStage").GetValue(site);
            Assert.That((bool)siteType.GetProperty("CanAcceptMountedComponent").GetValue(site), Is.False);
            siteType.GetMethod("NotifyMounted").Invoke(site, null);
            object alignmentTool = Enum.ToObject(toolType, 0);
            Type[] workArguments = { toolType, typeof(float), typeof(int) };
            object[] workValues = { alignmentTool, 1f, -1 };
            BindingFlags publicInstance = BindingFlags.Instance | BindingFlags.Public;
            Assert.That((bool)siteType.GetMethod("TryApplyToolWork", publicInstance, null, workArguments, null).Invoke(site, workValues), Is.False);
            Assert.That((bool)siteType.GetMethod("CanApplyToolWork", publicInstance, null, workArguments, null).Invoke(site, workValues), Is.False);
            System.Collections.IList prompts = (System.Collections.IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(promptType));
            Type workPointId = Type.GetType("BridgeDeckPanelWorkPointId, Assembly-CSharp");
            Type promptListType = typeof(System.Collections.Generic.List<>).MakeGenericType(promptType);
            siteType.GetMethod("GetWorkPointPrompts", publicInstance, null, new[] { workPointId, promptListType }, null)
                .Invoke(site, new[] { Enum.ToObject(workPointId, 0), prompts });
            Assert.That(prompts.Count, Is.Zero, "Passive slots never advertise old alignment/gap/fastening actions.");
            Assert.That((int)siteType.GetProperty("CurrentStage").GetValue(site), Is.EqualTo(stageBefore),
                "A legacy mount notification cannot advance or consume a physical slot.");
        }
        finally { UnityEngine.Object.DestroyImmediate(slot); }
    }
}
