using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class BridgeGuidanceEditModeTests
{
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Instruction(int id, string key = "work")
    {
        ConstructorInfo constructor = Runtime("BridgeGuidanceInstruction").GetConstructors()[0];
        ParameterInfo[] parameters = constructor.GetParameters();
        object[] arguments = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
        arguments[0] = id;
        arguments[1] = "Foundation " + id;
        arguments[2] = key;
        arguments[3] = 0f;
        arguments[4] = key;
        arguments[5] = "Help";
        return constructor.Invoke(arguments);
    }

    private static object Candidate(int id, int book = 0, bool current = true, bool assigned = false,
        bool actionable = true, bool complete = false, float distance = 0f)
    {
        return Activator.CreateInstance(Runtime("BridgeGuidanceCandidate"), Instruction(id), book, 0,
            current, assigned, actionable, complete, distance);
    }

    private static object Select(int active, int remembered, object failure, object device, params object[] candidates)
    {
        Array array = Array.CreateInstance(Runtime("BridgeGuidanceCandidate"), candidates.Length);
        for (int i = 0; i < candidates.Length; i++) array.SetValue(candidates[i], i);
        return Runtime("BridgeGuidanceSelection").GetMethod("Select").Invoke(null,
            new[] { array, (object)active, remembered, failure ?? Activator.CreateInstance(Runtime("BridgeGuidanceInstruction")),
                device ?? Activator.CreateInstance(Runtime("BridgeGuidanceInstruction")) });
    }

    private static int Id(object instruction) => (int)instruction.GetType().GetField("ComponentId").GetValue(instruction);
    private static string Key(object instruction) => (string)instruction.GetType().GetField("StepKey").GetValue(instruction);

    [Test]
    public void LocalFailureWinsOverActiveDeviceAndAssignments()
    {
        object result = Select(1, 2, Instruction(3, "failure"), Instruction(4, "device"),
            Candidate(1), Candidate(2), Candidate(5, assigned: true));
        Assert.That(Key(result), Is.EqualTo("failure"));
    }

    [Test]
    public void ActiveTransportWinsOverRememberedSite()
    {
        Assert.That(Key(Select(1, 2, null, Instruction(-1, "transport"), Candidate(1), Candidate(2))), Is.EqualTo("transport"));
    }

    [Test]
    public void ActiveEarlyPanelWinsBeforeItsGlobalStage()
    {
        Assert.That(Id(Select(9, 2, null, null, Candidate(2), Candidate(9, current: false))), Is.EqualTo(9));
    }

    [TestCase(6f, 2)]
    [TestCase(6.001f, 1)]
    public void RememberedFocusUsesInclusiveSixMetreBoundary(float distance, int expected)
    {
        Assert.That(Id(Select(-1, 2, null, null, Candidate(1, assigned: true), Candidate(2, distance: distance))), Is.EqualTo(expected));
    }

    [Test]
    public void CompletedWorkIsSkippedEvenIfActiveOrRemembered()
    {
        Assert.That(Id(Select(2, 2, null, null, Candidate(2, complete: true), Candidate(1))), Is.EqualTo(1));
    }

    [Test]
    public void AssignmentRequiresActionabilityThenFallsBackToBookOrder()
    {
        Assert.That(Id(Select(-1, -1, null, null, Candidate(1, book: 0), Candidate(2, book: 1, assigned: true, actionable: false))), Is.EqualTo(1));
        Assert.That(Id(Select(-1, -1, null, null, Candidate(1, book: 0), Candidate(2, book: 1, assigned: true))), Is.EqualTo(2));
    }

    [Test]
    public void TiesUseBookOrderThenComponentIdRatherThanEnumerationOrder()
    {
        Assert.That(Id(Select(-1, -1, null, null, Candidate(8, book: 1), Candidate(4, book: 0), Candidate(2, book: 0))), Is.EqualTo(2));
    }

    [Test]
    public void OrdinaryFutureWorkCannotStealCurrentStageFocus()
    {
        Assert.That(Id(Select(-1, -1, null, null, Candidate(9, book: 0, current: false), Candidate(2, book: 1))), Is.EqualTo(2));
    }

    [Test]
    public void EmptyCandidateListHasNoInstruction()
    {
        object result = Select(-1, -1, null, null);
        Assert.That((bool)result.GetType().GetProperty("HasValue").GetValue(result), Is.False);
    }

    [Test]
    public void FoundationAuthoringKeysAlternateConfiguredCyclesAndHaveNoDuplicates()
    {
        GameObject root = new GameObject("Guidance foundation fixture");
        root.SetActive(false);
        ScriptableObject workflow = ScriptableObject.CreateInstance(Runtime("BridgeConstructionWorkflowSO"));
        try
        {
            Component site = root.AddComponent(Runtime("BridgeConstructionSite"));
            FieldInfo workflowField = Runtime("BridgeConstructionSite").GetField("workflow", BindingFlags.Instance | BindingFlags.NonPublic);
            workflowField.SetValue(site, workflow);
            workflow.GetType().GetField("diggingCycleCount", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(workflow, 4);
            List<string> keys = new List<string>();
            Runtime("BridgeGuidanceSteps").GetMethod("CopyAuthoringKeys", new[] { Runtime("BridgeConstructionSite"), typeof(List<string>) })
                .Invoke(null, new object[] { site, keys });
            Assert.That(keys, Is.EqualTo(new[] { "foundation.clear", "foundation.soften.1", "foundation.collect.1",
                "foundation.soften.2", "foundation.collect.2", "foundation.soften.3", "foundation.collect.3",
                "foundation.soften.4", "foundation.collect.4", "foundation.concrete", "foundation.dry", "foundation.mount", "foundation.hammer" }));
            Assert.That(new HashSet<string>(keys).Count, Is.EqualTo(keys.Count));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(workflow);
        }
    }

    [TestCase("girder.fasten", "Industrial Hammer")]
    [TestCase("crossbeam.clamp", "Wrench")]
    [TestCase("foundation.recover", "Pickaxe")]
    public void DefaultToolCopyMatchesTheConcreteWorkflow(string key, string expectedTool)
    {
        object[] arguments = { key, null, null, null, null };
        Runtime("BridgeGuidanceSteps").GetMethod("DefaultText").Invoke(null, arguments);
        Assert.That(arguments[3], Is.EqualTo(expectedTool));
        Assert.That((string)arguments[2], Is.Not.Empty);
    }

    [Test]
    public void PhysicalPanelGuidanceUsesPlacementActionsAndQuarterProgress()
    {
        object instruction = Runtime("BridgeGuidanceSteps").GetMethod("Describe").Invoke(null, new object[] { null, "panel.nail", 0.5f });
        Assert.That(instruction.GetType().GetField("CompletedWork").GetValue(instruction), Is.EqualTo(2f));
        Assert.That(instruction.GetType().GetField("RequiredWork").GetValue(instruction), Is.EqualTo(4f));
        string actions = instruction.GetType().GetField("Actions").GetValue(instruction).ToString();
        StringAssert.Contains("PlacementToggle", actions);
        StringAssert.Contains("PlacementConfirm", actions);
    }

    [TestCase("soften", "clear", false)]
    [TestCase("soften", "soften", true)]
    [TestCase("soften", "mount", true)]
    [TestCase("mount", "soften", false)]
    [TestCase("soften", "display text is not a key", false)]
    public void AssignedStepsSkipCompletedWorkAndResolveFuturePrerequisites(string current, string assigned, bool expected)
    {
        object result = Runtime("BridgeGuidanceSteps").GetMethod("IsAssignedStepIncomplete").Invoke(null,
            new object[] { new[] { "clear", "soften", "collect", "mount" }, current, assigned, false });
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(0, 0f, "SoilRemoval", 0f, 6f, 0f)]
    [TestCase(0, 2f, "SoilRemoval", 2f, 6f, 1f / 3f)]
    [TestCase(0, 2.25f, "SoilRemoval", 2.25f, 6f, .375f)]
    [TestCase(0, 6f, "SoilRemoval", 6f, 6f, 1f)]
    [TestCase(1, 6f, "SoilRemoval", 0f, 6f, 0f)]
    [TestCase(1, 8f, "SoilRemoval", 2f, 6f, 1f / 3f)]
    [TestCase(1, 8.25f, "SoilRemoval", 2.25f, 6f, .375f)]
    [TestCase(1, 12f, "SoilRemoval", 6f, 6f, 1f)]
    [TestCase(2, 12f, "SoilRemoval", 0f, 6f, 0f)]
    [TestCase(2, 14f, "SoilRemoval", 2f, 6f, 1f / 3f)]
    [TestCase(2, 18f, "SoilRemoval", 6f, 6f, 1f)]
    [TestCase(2, 22f, "SoilRemoval", 6f, 6f, 1f)]
    [TestCase(1, 4f, "ReturnedSoilRemoval", 0f, 6f, 0f)]
    [TestCase(0, -2f, "ReturnedSoilRemoval", 0f, 6f, 0f)]
    [TestCase(1, 8f, "ReturnedSoilRemoval", 2f, 6f, 1f / 3f)]
    [TestCase(2, 14f, "Loosening", 30f, 60f, .5f)]
    public void ExcavationGuidanceReportsCurrentLayerRatherThanCumulativeSoil(int cycle, float removedTotal,
        string substage, float expectedCurrent, float expectedRequired, float expectedFraction)
    {
        GameObject root = new GameObject("Guidance soil layer fixture");
        root.SetActive(false);
        ScriptableObject workflow = ScriptableObject.CreateInstance(Runtime("BridgeConstructionWorkflowSO"));
        try
        {
            Component site = root.AddComponent(Runtime("BridgeConstructionSite"));
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Type siteType = site.GetType();
            workflow.GetType().GetField("soilUnitsPerCycle", fields).SetValue(workflow, 6);
            workflow.GetType().GetField("looseningProgressPerCycle", fields).SetValue(workflow, 60f);
            siteType.GetField("workflow", fields).SetValue(site, workflow);
            siteType.GetField("currentStage", fields).SetValue(site, Enum.Parse(Runtime("BridgeConstructionStage"), "Digging"));
            siteType.GetField("diggingSubstage", fields).SetValue(site, Enum.Parse(Runtime("FoundationDiggingSubstage"), substage));
            siteType.GetField("diggingCycleIndex", fields).SetValue(site, cycle);
            siteType.GetField("removedSoilUnits", fields).SetValue(site, removedTotal);
            siteType.GetField("currentWorkProgress", fields).SetValue(site, 30f);

            object instruction = Runtime("BridgeGuidanceSteps").GetMethod("Read").Invoke(null, new object[] { site });
            Type resultType = instruction.GetType();
            Assert.That(resultType.GetField("CompletedWork").GetValue(instruction), Is.EqualTo(expectedCurrent).Within(.0001f));
            Assert.That(resultType.GetField("RequiredWork").GetValue(instruction), Is.EqualTo(expectedRequired).Within(.0001f));
            Assert.That(resultType.GetField("Progress").GetValue(instruction), Is.EqualTo(expectedFraction).Within(.0001f));
            Assert.That(Key(instruction), Is.EqualTo($"foundation.{(substage == "Loosening" ? "soften" : "collect")}.{cycle + 1}"));
            Assert.That(siteType.GetField("removedSoilUnits", fields).GetValue(site), Is.EqualTo(removedTotal),
                "Guidance must preserve the authoritative cumulative soil total.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(workflow);
        }
    }
}
