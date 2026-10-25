using System;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class BridgeDeckAreaV1StageAssessmentTests
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly System.Collections.Generic.List<GameObject> objects = new();
    private readonly System.Collections.Generic.List<UnityEngine.Object> assets = new();
    private System.Type panelType, assessmentType, resultType, gapType;
    private object reusableAssessment;
    private Transform frame;
    private BridgeDeckAreaProfileProxy proxy;

    [SetUp]
    public void SetUp()
    {
        panelType = Type.GetType("BridgeDeckAreaPanel, Assembly-CSharp");
        assessmentType = Type.GetType("BridgeDeckAreaStageAssessment, Assembly-CSharp");
        resultType = Type.GetType("BridgeDeckAreaStageResult, Assembly-CSharp");
        gapType = Type.GetType("BridgeDeckAreaNeighborGap, Assembly-CSharp");
        Assert.That(panelType, Is.Not.Null); Assert.That(assessmentType, Is.Not.Null);
        reusableAssessment = Activator.CreateInstance(assessmentType);
        GameObject frameObject = NewObject("area frame");
        frame = frameObject.transform;
        frame.position = new Vector3(6f, 1.5f, -8f);
        frame.rotation = Quaternion.Euler(0f, 23f, 0f);
        proxy = new BridgeDeckAreaProfileProxy(this);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
        for (int i = assets.Count - 1; i >= 0; i--) if (assets[i] != null) UnityEngine.Object.DestroyImmediate(assets[i]);
        objects.Clear(); assets.Clear();
    }

    [Test]
    public void EmptyActiveAreaExposesWholeSpanAsAnOpenGapWithoutCoveringProgress()
    {
        object setup = CreateRequirement();
        object result = Evaluate(setup);
        Assert.That(GetField<bool>(result, "IsValid"), Is.True);
        Assert.That(GetField<bool>(result, "IsComplete"), Is.False);
        Assert.That(GetField<float>(result, "CoveredLengthMeters"), Is.Zero);
        System.Collections.IList gaps = GetField<System.Collections.IList>(result, "NeighborGaps");
        Assert.That(gaps.Count, Is.EqualTo(1));
        object open = gaps[0];
        Assert.That(GetField<object>(open, "First"), Is.Null);
        Assert.That(GetField<object>(open, "Second"), Is.Null);
        Assert.That(GetField<float>(open, "MinimumMeters"), Is.EqualTo(6.56f).Within(.0001f));
        Assert.That(GetField<float>(open, "MaximumMeters"), Is.EqualTo(6.56f).Within(.0001f));

        Type requirementType = Type.GetType("BridgeDeckAreaRequirement, Assembly-CSharp");
        SetField((Component)setup, "lastResult", result);
        Ray ray = new(frame.TransformPoint(new Vector3(0f, 2f, 0f)), -frame.up);
        object[] args = { ray, null, 0f, 0f, 0f };
        Assert.That((bool)requirementType.GetMethod("TryGetAimedGap").Invoke(setup, args), Is.True);
        Assert.That((float)args[3], Is.EqualTo(-3.28f).Within(.0001f));
        Assert.That((float)args[4], Is.EqualTo(3.28f).Within(.0001f));
        GameObject cameraObject = NewObject("aim camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = frame.TransformPoint(new Vector3(0f, 2f, 0f));
        cameraObject.transform.rotation = Quaternion.LookRotation(-frame.up, frame.forward);
        cameraObject.AddComponent<Camera>();
        Assert.That(requirementType.GetMethod("GetAimedGapGuidance").Invoke(setup, new object[] { cameraObject.transform }).ToString(),
            Does.Contain("Open area: 6560–6560 mm"));
    }

    [Test]
    public void AssessmentMeasuresActualWedgeExtremaAndAllowedEndJointInTranslatedYawedFrame()
    {
        object setup = CreateRequirement();
        object left = CreatePanel("left stock", 2500, -2.03f, 0f, 0f);
        object middle = CreatePanel("middle cut", 1520, 0f, 0f, .3f);
        object right = CreatePanel("right stock", 2500, 2.03f, 0f, 0f);
        object result = Evaluate(setup);
        Assert.That(GetField<bool>(result, "IsValid"), Is.True);
        Assert.That(GetField<bool>(result, "IsComplete"), Is.False, "No panel has four physical fasteners in this geometry fixture.");
        object centralGap = FindGap(result, middle, right);
        float yaw = .3f * Mathf.Deg2Rad;
        float expectedMin = .02f - 1.19f * Mathf.Tan(yaw) - .76f * (1f / Mathf.Cos(yaw) - 1f);
        float expectedMax = .02f + 1.19f * Mathf.Tan(yaw) - .76f * (1f / Mathf.Cos(yaw) - 1f);
        Assert.That(GetField<float>(centralGap, "MinimumMeters"), Is.EqualTo(expectedMin).Within(.0001f));
        Assert.That(GetField<float>(centralGap, "MaximumMeters"), Is.EqualTo(expectedMax).Within(.0001f));
        object rightEnd = FindGap(result, right, null);
        Assert.That(GetField<float>(rightEnd, "MinimumMeters"), Is.EqualTo(0f).Within(.0001f));
        Assert.That(GetField<float>(rightEnd, "MaximumMeters"), Is.EqualTo(0f).Within(.0001f));

        // Move only the right plate inward. The actual boundary joint is 15 mm, which is closed.
        SetPanelLocalPose(middle, 0f, 0f, 0f);
        SetPanelLocalPose(right, 2.015f, 0f, 0f);
        result = Evaluate(setup);
        rightEnd = FindGap(result, right, null);
        Assert.That(GetField<float>(rightEnd, "MinimumMeters"), Is.EqualTo(.015f).Within(.0001f));
        Assert.That(GetField<float>(rightEnd, "MaximumMeters"), Is.EqualTo(.015f).Within(.0001f));
    }

    [Test]
    public void ActualOverlappingPanelsAreBothExcludedFromTheContributorSet()
    {
        object setup = CreateRequirement();
        object left = CreatePanel("left stock", 2500, -2.03f, 0f, 0f);
        object middle = CreatePanel("middle cut", 1520, 0f, 0f, 0f);
        CreatePanel("right stock", 2500, 2.03f, 0f, 0f);
        object duplicate = CreatePanel("overlapping duplicate", 1520, 0f, 0f, 0f);
        object result = Evaluate(setup);
        System.Collections.IList contributors = GetField<System.Collections.IList>(result, "ContributingPanels");
        Assert.That(contributors.Count, Is.EqualTo(2));
        Assert.That(GetField<bool>(result, "IsComplete"), Is.False);
        AssertPanelDiagnostic(setup, result, middle, "Overlap");
        AssertPanelDiagnostic(setup, result, duplicate, "Overlap");
    }

    [Test]
    public void LaidPanelShowsMeasurableNarrowOpeningBeforeLaterHeightFailureWithoutContributing()
    {
        object setup = CreateRequirement();
        CreatePanel("left valid neighbor", 1500, -1.95f, 0f, 0f);
        object candidate = CreatePanel("raised middle plate", 1000, 0f, 0f, 0f);
        CreatePanel("right valid neighbor", 1560, 1.36f, 0f, 0f);
        ((Component)candidate).transform.position = frame.TransformPoint(new Vector3(0f, .041f, 0f));

        object result = Evaluate(setup);
        AssertPanelDiagnostic(setup, result, candidate, "NarrowOpening");
        System.Collections.IList contributors = GetField<System.Collections.IList>(result, "ContributingPanels");
        Assert.That(contributors.Count, Is.EqualTo(2), "Higher display priority does not admit invalid-height geometry into coverage.");
        Assert.That(GetField<float>(result, "CoveredLengthMeters"), Is.EqualTo(3.06f).Within(.001f));
        Assert.That(GetField<float>(result, "SecuredLengthMeters"), Is.Zero);
        Assert.That(GetField<bool>(result, "IsComplete"), Is.False);

        ((Component)candidate).transform.position = frame.TransformPoint(Vector3.zero);
        result = Evaluate(setup);
        AssertPanelDiagnostic(setup, result, candidate, "NarrowOpening");
        Assert.That(GetField<System.Collections.IList>(result, "ContributingPanels").Count, Is.EqualTo(3));
        Assert.That(GetField<float>(result, "CoveredLengthMeters"), Is.EqualTo(4.06f).Within(.001f));
    }

    private static void AssertPanelDiagnostic(object setup, object result, object panel, string kind)
    {
        Type requirementType = Type.GetType("BridgeDeckAreaRequirement, Assembly-CSharp");
        MethodInfo method = requirementType.GetMethod("TryGetPanelPresentationDiagnostic", BindingFlags.Instance | BindingFlags.Public);
        object[] args = { panel, result, null, false };
        Assert.That((bool)method.Invoke(setup, args), Is.True);
        Assert.That(GetField<object>(args[2], "Kind").ToString(), Is.EqualTo(kind));
        if (kind == "NarrowOpening")
            Assert.That(GetField<float>(args[2], "MinimumGapMeters"), Is.EqualTo(.08f).Within(.001f));
    }

    [Test]
    public void OptedInAreaPanelWithLegacyMarkerNeverEntersLegacyAssessmentOrReset()
    {
        GameObject legacySection = NewObject("nearby legacy section");
        legacySection.AddComponent(Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp"));
        GameObject panelObject = NewObject("dual-marker area panel");
        Component areaPanel = panelObject.AddComponent(panelType);
        panelType.GetMethod("Awake", InstanceFields).Invoke(areaPanel, null);
        panelType.GetMethod("OnEnable", InstanceFields).Invoke(areaPanel, null);
        Type legacyMarkerType = Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        Component legacyMarker = panelObject.AddComponent(legacyMarkerType);
        legacyMarkerType.GetMethod("OnEnable", InstanceFields).Invoke(legacyMarker, null);

        Type legacyStateType = Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        MethodInfo owner = legacyStateType.GetMethod("FindOwningSection", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(owner.Invoke(null, new object[] { legacyMarker }), Is.Null,
            "A nearby legacy section cannot claim a panel that explicitly opted into area assessment.");

        Type activeListType = typeof(System.Collections.Generic.List<>).MakeGenericType(legacyMarkerType);
        System.Collections.IList active = (System.Collections.IList)Activator.CreateInstance(activeListType);
        legacyMarkerType.GetMethod("CopyActivePanels", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { panelObject.scene, active });
        Assert.That(active.Count, Is.Zero, "Dual-marked area panels are excluded from the legacy active-panel scan.");

        MethodInfo assessment = legacyStateType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "TryGetAssessmentFor" && method.GetParameters().Length == 2);
        object[] assessmentArgs = { legacyMarker, null };
        Assert.That(assessment.Invoke(null, assessmentArgs), Is.EqualTo(false));
        Assert.That(legacyStateType.GetMethod("TryResetPanelWithHammer", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { legacyMarker, Vector3.right }), Is.EqualTo(false));
    }

    private object CreateRequirement()
    {
        Type sectionType = Type.GetType("BridgeDeckSection, Assembly-CSharp");
        Type componentType = Type.GetType("BridgeComponent, Assembly-CSharp");
        Type requirementType = Type.GetType("BridgeDeckAreaRequirement, Assembly-CSharp");
        GameObject sectionObject = NewObject("deck section");
        Component section = sectionObject.AddComponent(sectionType);
        GameObject girderA = NewObject("main girder A"), girderB = NewObject("main girder B");
        Component a = girderA.AddComponent(componentType), b = girderB.AddComponent(componentType);
        SetField(section, "mainGirders", Array.CreateInstance(componentType, 2));
        Array girders = (Array)GetField<object>(section, "mainGirders"); girders.SetValue(a, 0); girders.SetValue(b, 1);
        GameObject requirementObject = NewObject("area requirement");
        requirementObject.transform.SetParent(sectionObject.transform, false);
        Component requirement = requirementObject.AddComponent(requirementType);
        SetField(requirement, "profile", proxy.Profile);
        SetField(requirement, "deckFrame", frame);
        SetField(requirement, "deckSection", section);
        return requirement;
    }

    private object CreatePanel(string name, int lengthMm, float x, float z, float yaw)
    {
        GameObject go = NewObject(name);
        Component panel = go.AddComponent(panelType);
        SetField(panel, "geometryProfile", proxy.Profile);
        // EditMode does not execute ordinary runtime lifecycle callbacks for these
        // non-ExecuteAlways components. Drive the same initialization paths explicitly.
        panelType.GetMethod("Awake", InstanceFields).Invoke(panel, null);
        panelType.GetMethod("OnEnable", InstanceFields).Invoke(panel, null);
        SetDimensions(panel, lengthMm, 2400, 200);
        SetPanelLocalPose(panel, x, z, yaw);
        Assert.That(panelType.GetProperty("PhysicalCollider").GetValue(panel), Is.Not.Null, "Panel collider cache must be initialized.");
        Rigidbody body = go.GetComponent<Rigidbody>(); body.isKinematic = true;
        return panel;
    }

    private void SetPanelLocalPose(object panel, float x, float z, float yaw)
    {
        Transform t = ((Component)panel).transform;
        t.position = frame.TransformPoint(new Vector3(x, 0f, z));
        t.rotation = frame.rotation * Quaternion.Euler(0f, yaw, 0f);
    }

    private void SetDimensions(object panel, int length, int width, int thickness)
    {
        Type snapshotType = Type.GetType("BridgeDeckAreaPanelSnapshot, Assembly-CSharp");
        object snapshot = Activator.CreateInstance(snapshotType);
        SetField(snapshot, "LengthMillimeters", length); SetField(snapshot, "WidthMillimeters", width);
        SetField(snapshot, "ThicknessMillimeters", thickness); SetField(snapshot, "SourceLengthMillimeters", 2500);
        SetField(snapshot, "SourceMassGrams", 1000); SetField(snapshot, "OperatorClientId", ulong.MaxValue);
        panelType.GetMethod("ApplySnapshot", InstanceFields).Invoke(panel, new[] { snapshot });
    }

    private object Evaluate(object requirement)
    {
        return assessmentType.GetMethod("Evaluate").Invoke(reusableAssessment, new[] { requirement });
    }

    private object FindGap(object result, object first, object second)
    {
        System.Collections.IList gaps = GetField<System.Collections.IList>(result, "NeighborGaps");
        foreach (object gap in gaps)
            if (ReferenceEquals(GetField<object>(gap, "First"), first) && ReferenceEquals(GetField<object>(gap, "Second"), second)) return gap;
        var measured = new System.Text.StringBuilder();
        foreach (object gap in gaps)
            measured.Append(GetField<object>(gap, "First") is Component a ? a.name : "left end")
                .Append("->").Append(GetField<object>(gap, "Second") is Component b ? b.name : "right end").Append(';');
        Assert.Fail($"No gap for {((Component)first)?.name ?? "left end"} to {((Component)second)?.name ?? "right end"}; measured {measured}; valid={GetField<bool>(result, "IsValid")}, covered={GetField<float>(result, "CoveredLengthMeters")}, contributors={GetField<System.Collections.IList>(result, "ContributingPanels").Count}.");
        return null;
    }

    private GameObject NewObject(string name) { GameObject go = new(name); objects.Add(go); return go; }
    private static void SetField(object target, string name, object value)
    {
        (target as Type ?? target.GetType()).GetField(name, InstanceFields).SetValue(target is Type ? null : target, value);
    }
    private static T GetField<T>(object target, string name) => (T)target.GetType().GetField(name, InstanceFields).GetValue(target);

    private sealed class BridgeDeckAreaProfileProxy
    {
        public readonly ScriptableObject Profile;
        public BridgeDeckAreaProfileProxy(BridgeDeckAreaV1StageAssessmentTests owner)
        {
            Type type = Type.GetType("BridgeDeckAreaProfile, Assembly-CSharp");
            Profile = ScriptableObject.CreateInstance(type);
            owner.assets.Add(Profile);
        }
    }
}
