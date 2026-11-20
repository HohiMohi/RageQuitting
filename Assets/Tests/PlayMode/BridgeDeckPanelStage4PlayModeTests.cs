using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class BridgeDeckPanelStage4PlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private readonly List<GameObject> roots = new();
    private readonly List<ScriptableObject> scriptableRoots = new();
    private object previousManagerInstance;
    private FieldInfo managerInstanceField;

    [UnityTest]
    public IEnumerator CompletedPhysicalSlotUpdatesOnlyItsManagedMainBridgeComponent()
    {
        Type managerType = T("GameplayManager");
        Type bridgeType = T("Bridge");
        Type bridgeComponentType = T("BridgeComponent");
        Type sectionType = T("BridgeDeckSection");
        Type stateType = T("BridgeDeckPanelSectionNetworkState");
        Type siteType = T("BridgeDeckPanelConstructionSite");
        Type markerType = T("BridgeDeckPanelMounting");
        managerInstanceField = managerType.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        previousManagerInstance = managerInstanceField.GetValue(null);

        GameObject bridgeRoot = Track(new GameObject("Stage 4 main bridge"));
        bridgeRoot.SetActive(false);
        GameObject holder = Track(new GameObject("Components"));
        holder.transform.SetParent(bridgeRoot.transform, false);
        GameObject mainSiteRoot = Track(new GameObject("Main deck site"));
        mainSiteRoot.transform.SetParent(holder.transform, false);
        Component mainMarker = mainSiteRoot.AddComponent(markerType);
        Set(markerType, mainMarker, "role", Enum.Parse(T("BridgeDeckPanelMounting+Role"), "PassiveSlot"));
        Component mainComponent = mainSiteRoot.AddComponent(bridgeComponentType);
        Set(bridgeComponentType, mainComponent, "componentID", 1);
        Component mainSite = mainSiteRoot.AddComponent(siteType);

        GameObject earlierRoot = Track(new GameObject("Earlier stage component"));
        earlierRoot.transform.SetParent(holder.transform, false);
        Component earlierComponent = earlierRoot.AddComponent(bridgeComponentType);
        Set(bridgeComponentType, earlierComponent, "componentID", 0);

        GameObject girderRoot = Track(new GameObject("Main girder"));
        girderRoot.transform.SetParent(holder.transform, false);
        Component mainGirder = girderRoot.AddComponent(bridgeComponentType);
        Set(bridgeComponentType, mainGirder, "componentID", 1);

        Component bridge = bridgeRoot.AddComponent(bridgeType);
        Set(bridgeType, bridge, "bridgeComponentHolder", holder);

        Component mainSection;
        Component mainState;
        BuildPanelSection(sectionType, stateType, mainSite, mainGirder, out mainSection, out mainState);

        GameObject managerRoot = Track(new GameObject("Stage 4 gameplay manager"));
        managerRoot.SetActive(false);
        Component manager = managerRoot.AddComponent(managerType);
        Set(managerType, manager, "bridge", bridge);
        Array componentData = CreateComponentData(managerType, new[] { false, false });
        SetDataFlag(componentData, 1, "CanBeMounted", true);
        Set(managerType, manager, "bridgeComponentDataArray", componentData);
        Set(managerType, manager, "bridgeBuildingStages", CreateStages(managerType, new[] { 0, 1 }));
        Set(managerType, manager, "currentBridgeBuildingStageIndex", 0);
        managerInstanceField.SetValue(null, null);

        bridgeRoot.SetActive(true);
        mainSection.gameObject.SetActive(true);

        GameObject panelRoot = Track(new GameObject("Live deck panel"));
        Component panelMarker = panelRoot.AddComponent(markerType);
        Transform originalParent = panelRoot.transform.parent;
        Vector3 originalPosition = panelRoot.transform.position;
        Quaternion originalRotation = panelRoot.transform.rotation;

        GameObject testSiteRoot = Track(new GameObject("NailTestArea isolated site"));
        testSiteRoot.SetActive(false);
        Component testMarker = testSiteRoot.AddComponent(markerType);
        Set(markerType, testMarker, "role", Enum.Parse(T("BridgeDeckPanelMounting+Role"), "PassiveSlot"));
        Component testComponent = testSiteRoot.AddComponent(bridgeComponentType);
        Set(bridgeComponentType, testComponent, "componentID", 0);
        Component testSite = testSiteRoot.AddComponent(siteType);
        Component testGirder = Track(new GameObject("NailTestArea girder")).AddComponent(bridgeComponentType);
        Set(bridgeComponentType, testGirder, "componentID", 0);
        Component testSection;
        Component testState;
        BuildPanelSection(sectionType, stateType, testSite, testGirder, out testSection, out testState);
        testSiteRoot.SetActive(true);
        testSection.gameObject.SetActive(true);

        int assembledEvents = 0;
        int requirementsEvents = 0;
        int constructionStageEvents = 0;
        int mountableStatusEvents = 0;
        managerType.GetEvent("OnBridgeFullyAssembled", PublicInstance)
            .AddEventHandler(manager, new EventHandler((_, __) => assembledEvents++));
        managerType.GetEvent("OnBridgeRequirementsChanged", PublicInstance)
            .AddEventHandler(manager, new EventHandler((_, __) => requirementsEvents++));
        AddEventCounter(managerType.GetEvent("OnConstructionStageChanged"), manager, () => constructionStageEvents++);
        AddFieldDelegateCounter(managerType.GetField("BridgeComponentMountableStatusUpdate", PublicInstance), manager,
            () => mountableStatusEvents++);

        WriteSlotState(stateType, mainState, panelMarker, complete: true);
        siteType.GetMethod("ApplyPhysicalPanelCompletion", PrivateInstance | PublicInstance).Invoke(mainSite, null);
        managerType.GetMethod("RecordPhysicalDeckPanelCompletion", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(manager, new[] { mainState, mainSite });
        Assert.IsTrue((bool)siteType.GetProperty("IsComplete", PublicInstance).GetValue(mainSite),
            "The section completion is retained locally before the manager is ready.");
        Assert.IsFalse((bool)bridgeComponentType.GetProperty("IsMounted", PublicInstance).GetValue(mainComponent),
            "A section callback before manager Start cannot mutate global bridge progress.");
        Assert.AreEqual(0, assembledEvents, "No terminal event fires before configured bridge state initialization.");

        managerRoot.SetActive(true);
        yield return null;
        Assert.AreEqual(0, managerType.GetProperty("CurrentBridgeStageIndex", PublicInstance).GetValue(manager),
            "Startup reconciliation preserves future completion without passing an incomplete earlier stage.");
        Assert.IsTrue((bool)bridgeComponentType.GetProperty("IsMounted", PublicInstance).GetValue(mainComponent),
            "Startup reconciliation replays the durable future slot after configured state initialization.");
        Assert.IsFalse((bool)managerType.GetProperty("IsFullyAssembled", PublicInstance).GetValue(manager),
            "A completed future slot cannot raise terminal completion before the configured start state is applied.");
        Assert.AreEqual(0, assembledEvents);
        Assert.AreEqual(0, constructionStageEvents, "Startup replay emits no construction stage transition.");
        int requirementsAfterStartup = requirementsEvents;

        managerType.GetMethod("CheckCurrentStageMountingProgress", PrivateInstance).Invoke(manager, null);
        Assert.AreEqual(0, mountableStatusEvents, "An incomplete current stage causes no mountability update.");

        // Repair a reset manager from the already-persisted section Complete record.
        SetDataFlag(componentData, 1, "isMounted", false);
        SetDataFlag(componentData, 1, "isAssembled", false);
        Set(managerType, manager, "bridgeComponentDataArray", componentData);
        managerType.GetMethod("ReconcileInitialPhysicalDeckPanels", PrivateInstance).Invoke(manager, null);
        Assert.AreEqual(0, managerType.GetProperty("CurrentBridgeStageIndex", PublicInstance).GetValue(manager),
            "The future panel is recorded while the earlier stage remains incomplete.");
        Assert.IsFalse((bool)managerType.GetProperty("IsFullyAssembled", PublicInstance).GetValue(manager));
        int requirementsAfterCompletion = requirementsEvents;
        Assert.Greater(requirementsAfterCompletion, requirementsAfterStartup);
        managerType.GetMethod("RecordPhysicalDeckPanelCompletion", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(manager, new[] { mainState, mainSite });
        Assert.AreEqual(requirementsAfterCompletion, requirementsEvents, "Duplicate reconciliation is a no-op.");
        Assert.AreEqual(0, constructionStageEvents, "Initial replay does not emit a live stage transition.");

        // Recreate a live completion after a manager-side reset, while the earlier stage is still blocked.
        SetDataFlag(componentData, 1, "isMounted", false);
        SetDataFlag(componentData, 1, "isAssembled", false);
        SetDataFlag(componentData, 1, "CanBeMounted", true);
        Set(managerType, manager, "bridgeComponentDataArray", componentData);
        T("BridgeConstructionSite").GetField("currentStage", PrivateInstance)
            .SetValue(mainSite, Enum.Parse(T("BridgeConstructionStage"), "ReadyForMount"));
        managerType.GetMethod("SeedObservedConstructionStages", PrivateInstance).Invoke(manager, null);
        managerType.GetMethod("RecordPhysicalDeckPanelCompletion", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(manager, new[] { mainState, mainSite });
        Assert.AreEqual(0, managerType.GetProperty("CurrentBridgeStageIndex", PublicInstance).GetValue(manager));
        Assert.AreEqual(1, constructionStageEvents, "A live completion emits one stage transition.");
        Assert.IsFalse((bool)GetDataMember(componentData.GetValue(1), "CanBeMounted"));
        int requirementsAfterLiveCompletion = requirementsEvents;
        managerType.GetMethod("RecordPhysicalDeckPanelCompletion", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(manager, new[] { mainState, mainSite });
        Assert.AreEqual(requirementsAfterLiveCompletion, requirementsEvents, "A second completion notification does not rebroadcast progress.");
        Assert.AreEqual(1, constructionStageEvents);

        SetDataFlag(componentData, 0, "isMounted", true);
        SetDataFlag(componentData, 0, "isAssembled", true);
        Set(managerType, manager, "bridgeComponentDataArray", componentData);
        managerType.GetMethod("CheckCurrentStageMountingProgress", PrivateInstance).Invoke(manager, null);

        Assert.IsTrue((bool)bridgeComponentType.GetProperty("IsMounted", PublicInstance).GetValue(mainComponent));
        Assert.IsTrue((bool)bridgeComponentType.GetProperty("IsAssembled", PublicInstance).GetValue(mainComponent));
        Assert.AreEqual(2, managerType.GetProperty("CurrentBridgeStageIndex", PublicInstance).GetValue(manager),
            "The precompleted later stage is retained and passed automatically.");
        Assert.IsTrue((bool)managerType.GetProperty("IsFullyAssembled", PublicInstance).GetValue(manager));
        Assert.AreEqual(1, assembledEvents);
        Assert.AreEqual(1, constructionStageEvents);
        Assert.AreSame(originalParent, panelRoot.transform.parent);
        Assert.AreEqual(originalPosition, panelRoot.transform.position);
        Assert.AreEqual(originalRotation, panelRoot.transform.rotation);

        object siteRoundTrip = Activator.CreateInstance(T("BridgeComponentNetworkState"), new object[] { 1 });
        object[] roundTripArguments = { siteRoundTrip };
        siteType.GetMethod("PopulateNetworkState", PublicInstance).Invoke(mainSite, roundTripArguments);
        siteRoundTrip = roundTripArguments[0];
        T("BridgeConstructionSite").GetField("currentStage", PrivateInstance)
            .SetValue(mainSite, Enum.Parse(T("BridgeConstructionStage"), "ReadyForMount"));
        siteType.GetMethod("ApplyNetworkState", PublicInstance).Invoke(mainSite, new[] { siteRoundTrip });
        Assert.IsTrue((bool)siteType.GetProperty("IsComplete", PublicInstance).GetValue(mainSite));

        WriteSlotState(stateType, testState, testMarker, complete: true);
        managerType.GetMethod("RecordPhysicalDeckPanelCompletion", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(manager, new[] { testState, testSite });
        Assert.IsFalse((bool)bridgeComponentType.GetProperty("IsMounted", PublicInstance).GetValue(testComponent),
            "An isolated test section cannot mutate the main bridge progression.");
        Assert.AreEqual(1, assembledEvents);
    }

    [UnityTest]
    public IEnumerator FutureDeckPanelProgressRefreshesWorldAndLocalBookWithoutChangingPage()
    {
        Type managerType = T("GameplayManager");
        Type dataType = managerType.GetField("bridgeComponentDataArray", PrivateInstance).FieldType.GetElementType();
        Type panelType = T("BridgeComponentSO");
        Type workflowType = T("BridgeDeckPanelConstructionWorkflowSO");
        managerInstanceField = managerType.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        previousManagerInstance = managerInstanceField.GetValue(null);

        GameObject managerRoot = Track(new GameObject("Stage 4 future progress manager"));
        managerRoot.SetActive(false);
        Component manager = managerRoot.AddComponent(managerType);
        ScriptableObject panel = ScriptableObject.CreateInstance(panelType);
        scriptableRoots.Add(panel);
        Set(panelType, panel, "componentName", "Wooden Deck Panel");
        ScriptableObject workflow = ScriptableObject.CreateInstance(workflowType);
        scriptableRoots.Add(workflow);
        Set(workflowType, workflow, "physicalPanelPlacement", true);
        Set(panelType, panel, "deckPanelConstructionWorkflow", workflow);
        ScriptableObject earlier = ScriptableObject.CreateInstance(panelType);
        scriptableRoots.Add(earlier);
        Set(panelType, earlier, "componentName", "Earlier Bridge Component");

        Array data = Array.CreateInstance(dataType, 4);
        for (int i = 0; i < data.Length; i++)
        {
            object value = Activator.CreateInstance(dataType);
            Set(dataType, value, "bridgeComponentSO", i == 0 ? earlier : panel);
            Set(dataType, value, "isMounted", i == 1);
            Set(dataType, value, "isAssembled", i > 0);
            data.SetValue(value, i);
        }
        Set(managerType, manager, "bridgeComponentDataArray", data);
        Set(managerType, manager, "bridgeBuildingStages", CreateMultiIndexStages(managerType, new[] { 0 }, new[] { 1, 2, 3 }));
        Set(managerType, manager, "currentBridgeBuildingStageIndex", 0);
        managerInstanceField.SetValue(null, manager);

        GameObject bookRoot = Track(new GameObject("Stage 4 progress book", typeof(BoxCollider)));
        Component book = bookRoot.AddComponent(T("ConstructionBookController"));
        yield return null;
        MethodInfo render = book.GetType().GetMethod("Render", PrivateInstance, null,
            new[] { typeof(int), typeof(bool), typeof(bool) }, null);
        render.Invoke(book, new object[] { 1, true, false });
        Component worldView = (Component)Get(book.GetType(), book, "worldView");
        Assert.That(CollectActiveText(worldView), Does.Contain("Secured panels: 1 / 3"));

        int pageChangedEvents = 0;
        AddEventCounter(book.GetType().GetEvent("PageChanged"), book, () => pageChangedEvents++);
        int currentIndex = (int)book.GetType().GetProperty("CurrentIndex", PublicInstance).GetValue(book);
        bool wasTransitioning = (bool)book.GetType().GetProperty("IsPageTransitioning", PublicInstance).GetValue(book);

        SetDataFlag(data, 2, "isMounted", true);
        Set(managerType, manager, "bridgeComponentDataArray", data);
        Assert.AreSame(panel, GetDataMember(data.GetValue(2), "bridgeComponentSO"));
        Assert.IsTrue((bool)GetDataMember(data.GetValue(2), "isMounted"));
        Array managerData = (Array)Get(managerType, manager, "bridgeComponentDataArray");
        Assert.AreSame(panel, GetDataMember(managerData.GetValue(2), "bridgeComponentSO"));
        Assert.IsTrue((bool)GetDataMember(managerData.GetValue(2), "isMounted"));
        Assert.AreEqual(2, (int)managerType.GetMethod("GetMountedBridgeComponentCount", PublicInstance)
            .Invoke(manager, new[] { panel }), "The authoritative manager retains both future mounted panels.");
        managerType.GetMethod("NotifyBridgeRequirementsChanged", PrivateInstance).Invoke(manager, null);

        Assert.That(CollectActiveText(worldView), Does.Contain("Secured panels: 2 / 3"),
            "The world book refreshes its already-open deck page when the authoritative progress notification arrives.");
        Assert.That(CollectActiveText(worldView), Does.Not.Contain("Secured panels: 1 / 3"));
        Assert.That(book.GetType().GetProperty("CurrentIndex", PublicInstance).GetValue(book), Is.EqualTo(currentIndex));
        Assert.That(book.GetType().GetProperty("IsPageTransitioning", PublicInstance).GetValue(book), Is.EqualTo(wasTransitioning));
        Assert.That(pageChangedEvents, Is.Zero, "A progress refresh does not turn or reselect the current page.");

        GameObject localViewRoot = Track(new GameObject("Stage 4 local progress view", typeof(RectTransform)));
        Component localView = localViewRoot.AddComponent(T("ConstructionBookView"));
        localView.GetType().GetMethod("Build").Invoke(localView, new object[] { Color.white, Color.black });
        book.GetType().GetMethod("PopulateView", PublicInstance, null, new[] { localView.GetType(), typeof(Func<bool>) }, null)
            .Invoke(book, new object[] { localView, null });
        Assert.That(CollectActiveText(localView), Does.Contain("Secured panels: 2 / 3"),
            "The local book view reads the same future-stage completion count.");
        Assert.That(managerType.GetProperty("CurrentBridgeStageIndex", PublicInstance).GetValue(manager), Is.EqualTo(0),
            "Future progress does not move the incomplete earlier global stage.");
        SetDataFlag(data, 0, "isMounted", true);
        SetDataFlag(data, 0, "isAssembled", true);
        SetDataFlag(data, 3, "isMounted", true);
        SetDataFlag(data, 3, "isAssembled", true);
        Set(managerType, manager, "bridgeComponentDataArray", data);
        MethodInfo stageCompleted = managerType.GetMethod("IsStageFullyCompleted", PrivateInstance);
        Assert.IsTrue((bool)stageCompleted.Invoke(manager, new object[] { 0 }));
        Assert.IsTrue((bool)stageCompleted.Invoke(manager, new object[] { 1 }));
        managerType.GetMethod("CheckCurrentStageMountingProgress", PrivateInstance).Invoke(manager, null);
        Assert.IsTrue((bool)managerType.GetProperty("IsFullyAssembled", PublicInstance).GetValue(manager));
        book.GetType().GetMethod("PopulateView", PublicInstance, null, new[] { localView.GetType(), typeof(Func<bool>) }, null)
            .Invoke(book, new object[] { localView, null });
        Assert.That(CollectActiveText(localView), Does.Contain("Secured panels: 3 / 3"));
        Assert.That(book.GetType().GetProperty("CurrentIndex", PublicInstance).GetValue(book), Is.EqualTo(currentIndex));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (GameObject root in roots)
            if (root != null) UnityEngine.Object.Destroy(root);
        roots.Clear();
        yield return null;
        foreach (ScriptableObject asset in scriptableRoots)
            if (asset != null) UnityEngine.Object.Destroy(asset);
        scriptableRoots.Clear();
        yield return null;
        if (managerInstanceField != null) managerInstanceField.SetValue(null, previousManagerInstance);
    }

    private void BuildPanelSection(Type sectionType, Type stateType, Component site, Component mainGirder,
        out Component section, out Component state)
    {
        GameObject root = Track(new GameObject("Deck panel section"));
        root.SetActive(false);
        section = root.AddComponent(sectionType);
        GameObject stateRoot = Track(new GameObject("Panel section state"));
        stateRoot.transform.SetParent(root.transform, false);
        state = stateRoot.AddComponent(stateType);
        Array slots = Array.CreateInstance(site.GetType(), 1);
        slots.SetValue(site, 0);
        Set(sectionType, section, "panelSlots", slots);
        Array girders = Array.CreateInstance(mainGirder.GetType(), 1);
        girders.SetValue(mainGirder, 0);
        Set(sectionType, section, "mainGirders", girders);
        Set(sectionType, section, "physicalPanelState", state);
        site.GetType().GetMethod("ConfigureSection", PublicInstance).Invoke(site, new object[] { section, 0, false });
    }

    private static void WriteSlotState(Type stateType, Component sectionState, Component marker, bool complete)
    {
        Type recordType = T("BridgeDeckPanelReplicatedState");
        object record = Activator.CreateInstance(recordType);
        Set(recordType, record, "PanelNetworkObjectId", 0UL);
        Set(recordType, record, "SlotIndex", 0);
        Set(recordType, record, "QuarterMask", (byte)15);
        Set(recordType, record, "Failure", (byte)0);
        Set(recordType, record, "Complete", complete);
        stateType.GetMethod("WritePanelState", PrivateInstance).Invoke(sectionState, new[] { marker, record });
    }

    private static Array CreateComponentData(Type managerType, bool[] completed)
    {
        Type dataType = managerType.GetField("bridgeComponentDataArray", PrivateInstance).FieldType.GetElementType();
        Array data = Array.CreateInstance(dataType, completed.Length);
        for (int i = 0; i < completed.Length; i++)
        {
            object value = Activator.CreateInstance(dataType);
            Set(dataType, value, "isMounted", completed[i]);
            Set(dataType, value, "isAssembled", completed[i]);
            data.SetValue(value, i);
        }
        return data;
    }

    private static void SetDataFlag(Array data, int index, string fieldName, bool value)
    {
        object item = data.GetValue(index);
        FieldInfo field = item.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null) field.SetValue(item, value);
        else item.GetType().GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(item, value);
        data.SetValue(item, index);
    }

    private static object GetDataMember(object target, string memberName)
    {
        FieldInfo field = target.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null ? field.GetValue(target)
            : target.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
    }

    private static object Get(Type type, object target, string fieldName)
        => type.GetField(fieldName, PrivateInstance).GetValue(target);

    private static void AddEventCounter(EventInfo eventInfo, object target, Action counter)
    {
        Delegate handler = CreateCounterDelegate(eventInfo.EventHandlerType, counter);
        eventInfo.AddEventHandler(target, handler);
    }

    private static void AddFieldDelegateCounter(FieldInfo field, object target, Action counter)
    {
        Delegate handler = CreateCounterDelegate(field.FieldType, counter);
        field.SetValue(target, Delegate.Combine((Delegate)field.GetValue(target), handler));
    }

    private static Delegate CreateCounterDelegate(Type delegateType, Action counter)
    {
        ParameterExpression[] parameters = Array.ConvertAll(delegateType.GetMethod("Invoke").GetParameters(),
            parameter => Expression.Parameter(parameter.ParameterType, parameter.Name));
        return Expression.Lambda(delegateType,
            Expression.Call(Expression.Constant(counter.Target), counter.Method), parameters).Compile();
    }

    private static Array CreateStages(Type managerType, int[] componentIndexes)
    {
        Type stageType = managerType.GetField("bridgeBuildingStages", PrivateInstance).FieldType.GetElementType();
        Array stages = Array.CreateInstance(stageType, componentIndexes.Length);
        for (int i = 0; i < componentIndexes.Length; i++)
        {
            object stage = Activator.CreateInstance(stageType);
            Set(stageType, stage, "bridgeComponentDataIndexes", new[] { componentIndexes[i] });
            stages.SetValue(stage, i);
        }
        return stages;
    }

    private static Array CreateMultiIndexStages(Type managerType, int[] first, int[] second)
    {
        Type stageType = managerType.GetField("bridgeBuildingStages", PrivateInstance).FieldType.GetElementType();
        Array stages = Array.CreateInstance(stageType, 2);
        object firstStage = Activator.CreateInstance(stageType);
        Set(stageType, firstStage, "bridgeComponentDataIndexes", first);
        stages.SetValue(firstStage, 0);
        object secondStage = Activator.CreateInstance(stageType);
        Set(stageType, secondStage, "bridgeComponentDataIndexes", second);
        stages.SetValue(secondStage, 1);
        return stages;
    }

    private static string CollectText(Component view)
    {
        Type textType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        Component[] texts = (Component[])view.GetComponentsInChildren(textType, true);
        List<string> values = new();
        foreach (Component text in texts) values.Add((string)textType.GetProperty("text").GetValue(text));
        return string.Join("\n", values);
    }

    private static string CollectActiveText(Component view)
    {
        Type textType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        Component[] texts = (Component[])view.GetComponentsInChildren(textType, true);
        List<string> values = new();
        foreach (Component text in texts)
            if (text != null && text.gameObject.activeInHierarchy)
                values.Add((string)textType.GetProperty("text").GetValue(text));
        return string.Join("\n", values);
    }

    private GameObject Track(GameObject value)
    {
        roots.Add(value);
        return value;
    }

    private static Type T(string name) => Type.GetType(name + ", Assembly-CSharp", true);

    private static void Set(Type type, object target, string fieldName, object value)
        => type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
}
