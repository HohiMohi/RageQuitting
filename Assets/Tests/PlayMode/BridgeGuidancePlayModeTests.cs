using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class BridgeGuidancePlayModeTests
{
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static FieldInfo Field(Type type, string name) => type.GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    [UnityTest]
    public IEnumerator PresentationOptInBindingsMenusAndReenablePreserveScenePreference()
    {
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
            Assert.Ignore("The isolated presentation lifecycle fixture requires offline ownership.");
        GameObject player = new GameObject("Guidance presentation player");
        GameObject managerRoot = new GameObject("Guidance presentation gameplay");
        GameObject modeRoot = new GameObject("Guidance presentation mode");
        player.SetActive(false);
        managerRoot.SetActive(false);
        modeRoot.SetActive(false);
        FieldInfo singleton = Runtime("GameplayManager").GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previousManager = singleton.GetValue(null);
        try
        {
            Component gameplay = managerRoot.AddComponent(Runtime("GameplayManager"));
            singleton.SetValue(null, gameplay);
            Component mode = modeRoot.AddComponent(Runtime("BridgeStageInfoManager"));
            modeRoot.SetActive(true);
            Component input = player.AddComponent(Runtime("PlayerInputNew"));
            ((Behaviour)input).enabled = false;
            object initializedActions = Activator.CreateInstance(Runtime("PlayerGameInputActions"));
            InputActionAsset actions = (InputActionAsset)initializedActions.GetType().GetProperty("asset").GetValue(initializedActions);
            actions.FindAction("Game/ToggleBridgeRequirements", true).ApplyBindingOverride(0, "<Keyboard>/f2");
            Field(input.GetType(), "playerGameInputActions").SetValue(input, initializedActions);
            Field(input.GetType(), "inputInitialized").SetValue(input, true);
            player.AddComponent(Runtime("PlayerInteractionNew"));
            GameObject canvas = new GameObject("Presentation canvas", typeof(RectTransform), typeof(Canvas));
            canvas.transform.SetParent(player.transform, false);
            GameObject nRoot = new GameObject("Requirements", typeof(RectTransform));
            nRoot.transform.SetParent(canvas.transform, false);
            Component n = nRoot.AddComponent(Runtime("BridgeRequirementsUI"));
            GameObject tabRoot = new GameObject("Level menu", typeof(RectTransform));
            tabRoot.transform.SetParent(canvas.transform, false);
            Component tab = tabRoot.AddComponent(Runtime("RestartLevelUI"));
            GameObject uiRoot = new GameObject("Stage information", typeof(RectTransform));
            uiRoot.transform.SetParent(canvas.transform, false);
            Component ui = uiRoot.AddComponent(Runtime("PlayerBridgeStageInfoUI"));
            GameObject popup = new GameObject("Legacy popup", typeof(RectTransform));
            popup.transform.SetParent(uiRoot.transform, false);
            Field(ui.GetType(), "panelRoot").SetValue(ui, popup);
            player.SetActive(true);
            MethodInfo show = ui.GetType().GetMethod("Show");
            show.Invoke(ui, new object[] { "Legacy title", "Legacy help" });
            Assert.That(popup.activeSelf, Is.True, "Legacy scenes retain their existing popup behavior.");

            mode.GetType().GetMethod("ConfigureBridgeGuidance").Invoke(mode, new object[] { true });
            ui.GetType().GetMethod("RefreshConfiguration", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
            Assert.That(popup.activeSelf, Is.False);
            Assert.That(ui.GetType().GetProperty("IsTutorialHudVisible").GetValue(ui), Is.True);
            show.Invoke(ui, new object[] { "Should be suppressed", "Old popup" });
            Assert.That(popup.activeSelf, Is.False);
            string binding = (string)input.GetType().GetMethod("GetInitializedBindingDisplay").Invoke(input, new object[] { "ToggleBridgeRequirements" });
            StringAssert.Contains("F2", binding.ToUpperInvariant());
            object compact = Runtime("BridgeGuidancePresentation").GetMethod("Compact").Invoke(null,
                new object[] { Activator.CreateInstance(Runtime("BridgeGuidanceSnapshot")), new Func<string, string>(name =>
                    (string)input.GetType().GetMethod("GetInitializedBindingDisplay").Invoke(input, new object[] { name })) });
            StringAssert.Contains(binding, (string)compact);

            MethodInfo showN = n.GetType().GetMethod("SetVisible", BindingFlags.Instance | BindingFlags.NonPublic);
            showN.Invoke(n, new object[] { true });
            Assert.That(ui.GetType().GetProperty("IsTutorialHudVisible").GetValue(ui), Is.False);
            Component help = (Component)Field(n.GetType(), "tutorialHelpText").GetValue(n);
            Component tasks = (Component)Field(n.GetType(), "myTasksText").GetValue(n);
            Assert.That(help, Is.Not.Null);
            Assert.That(help.transform.GetSiblingIndex(), Is.LessThan(tasks.transform.GetSiblingIndex()));
            tab.GetType().GetMethod("SetVisible", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tab, new object[] { true });
            Toggle toggle = (Toggle)tab.GetType().GetProperty("TutorialHudToggle").GetValue(tab);
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.interactable, Is.True, "The tutorial preference does not depend on host restart permission.");
            toggle.isOn = false;
            Assert.That(mode.GetType().GetProperty("ShowTutorialHud").GetValue(mode), Is.False);
            Assert.That(help.gameObject.activeSelf, Is.True, "N help remains available when compact HUD is hidden.");
            showN.Invoke(n, new object[] { false });
            Assert.That(ui.GetType().GetProperty("IsTutorialHudVisible").GetValue(ui), Is.False);
            ((Behaviour)ui).enabled = false;
            ((Behaviour)ui).enabled = true;
            Assert.That(mode.GetType().GetProperty("ShowTutorialHud").GetValue(mode), Is.False);
            Assert.That(ui.GetType().GetProperty("IsTutorialHudVisible").GetValue(ui), Is.False);
            ((Behaviour)ui).enabled = false;
            mode.GetType().GetMethod("SetTutorialHudVisible").Invoke(mode, new object[] { true });
            Assert.That(ui.GetType().GetProperty("IsTutorialHudVisible").GetValue(ui), Is.False, "Disabled UI has no retained preference subscription.");
            ((Behaviour)ui).enabled = true;
            Assert.That(ui.GetType().GetProperty("IsTutorialHudVisible").GetValue(ui), Is.True);
            RectTransform hud = (RectTransform)Field(ui.GetType(), "guidanceRect").GetValue(ui);
            Assert.That(hud.anchoredPosition, Is.EqualTo(new Vector2(-24f, -88f)));
            Assert.That(hud.anchorMin, Is.EqualTo(Vector2.one));
            Assert.That(hud.parent, Is.EqualTo(canvas.transform));
            mode.GetType().GetMethod("ConfigureBridgeGuidance").Invoke(mode, new object[] { false });
            Assert.That(ui.GetType().GetProperty("IsTutorialHudVisible").GetValue(ui), Is.False);
            show.Invoke(ui, new object[] { "Legacy again", "Help" });
            Assert.That(popup.activeSelf, Is.True);
        }
        finally
        {
            singleton.SetValue(null, previousManager);
            UnityEngine.Object.Destroy(player);
            UnityEngine.Object.Destroy(modeRoot);
            UnityEngine.Object.Destroy(managerRoot);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator AuthoritativeReadinessPollingRememberedFocusAndRespawnKeepSharedCounts()
    {
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
            Assert.Ignore("The isolated guidance observer fixture requires offline ownership.");
        GameObject player = new GameObject("Guidance observer fixture");
        GameObject managerRoot = new GameObject("Guidance snapshot fixture");
        GameObject first = new GameObject("First guidance site");
        GameObject second = new GameObject("Second guidance site");
        player.SetActive(false);
        managerRoot.SetActive(false);
        first.SetActive(false);
        second.SetActive(false);
        ScriptableObject definition = ScriptableObject.CreateInstance(Runtime("BridgeComponentSO"));
        object source = null;
        try
        {
            definition.GetType().GetField("componentName").SetValue(definition, "Foundation");
            Component manager = managerRoot.AddComponent(Runtime("GameplayManager"));
            Array components = Array.CreateInstance(Runtime("BridgeComponent"), 2);
            Component[] sites = new Component[2];
            for (int i = 0; i < 2; i++)
            {
                GameObject root = i == 0 ? first : second;
                Component site = root.AddComponent(Runtime("BridgeConstructionSite"));
                Component component = root.GetComponent(Runtime("BridgeComponent"));
                Field(component.GetType(), "componentID").SetValue(component, i);
                Field(component.GetType(), "bridgeComponentSO").SetValue(component, definition);
                Field(component.GetType(), "constructionSite").SetValue(component, site);
                Field(site.GetType(), "bridgeComponent").SetValue(site, component);
                Field(site.GetType(), "currentStage").SetValue(site, Enum.Parse(Runtime("BridgeConstructionStage"), "ReadyForMount"));
                components.SetValue(component, i);
                sites[i] = site;
            }
            first.transform.position = new Vector3(100f, 0f, 0f);
            second.transform.position = Vector3.right;
            // Observation hooks receive live targets. Let Awake initialize the real site/component
            // association, then restore the synthetic authoritative state before constructing the observer.
            // This test performs all checks synchronously and destroys the fixtures before its first yield,
            // so unrelated Start/Update gameplay cannot run.
            first.SetActive(true);
            second.SetActive(true);
            foreach (Component site in sites)
                Field(site.GetType(), "currentStage").SetValue(site, Enum.Parse(Runtime("BridgeConstructionStage"), "ReadyForMount"));
            Field(manager.GetType(), "bridgeComponents").SetValue(manager, components);
            Array data = Array.CreateInstance(Runtime("BridgeComponentData"), 2);
            for (int i = 0; i < 2; i++)
            {
                object entry = Activator.CreateInstance(Runtime("BridgeComponentData"));
                Field(entry.GetType(), "bridgeComponentSO").SetValue(entry, definition);
                data.SetValue(entry, i);
            }
            Field(manager.GetType(), "bridgeComponentDataArray").SetValue(manager, data);
            object stage = Activator.CreateInstance(Runtime("BridgeBuildingStage"));
            Field(stage.GetType(), "bridgeComponentDataIndexes").SetValue(stage, new[] { 0, 1 });
            Array stages = Array.CreateInstance(stage.GetType(), 1);
            stages.SetValue(stage, 0);
            Field(manager.GetType(), "bridgeBuildingStages").SetValue(manager, stages);

            ((Behaviour)player.AddComponent(Runtime("PlayerInputNew"))).enabled = false;
            Component health = player.AddComponent(Runtime("PlayerHealth"));
            ((Behaviour)health).enabled = false;
            Component interaction = player.AddComponent(Runtime("PlayerInteractionNew"));
            player.SetActive(true);
            source = Activator.CreateInstance(Runtime("BridgeGuidanceSource"), manager, interaction);
            MethodInfo tick = source.GetType().GetMethod("Tick");
            Func<object> snapshot = () => source.GetType().GetProperty("Snapshot").GetValue(source);
            Func<string, object> read = name => snapshot().GetType().GetField(name).GetValue(snapshot());
            Func<object> instruction = () => read("Instruction");
            Func<int> focusId = () => (int)instruction().GetType().GetField("ComponentId").GetValue(instruction());

            tick.Invoke(source, new object[] { 0f });
            Assert.That(read("IsReady"), Is.False, "A client must not invent progress before the initial snapshot.");
            Field(manager.GetType(), "hasAppliedInitialNetworkBridgeState").SetValue(manager, true);
            tick.Invoke(source, new object[] { .1f });
            Assert.That(read("IsReady"), Is.False);
            tick.Invoke(source, new object[] { .21f });
            Assert.That(read("IsReady"), Is.True);
            Assert.That(read("RequiredCount"), Is.EqualTo(2));
            Assert.That(focusId(), Is.EqualTo(0), "Fallback uses stable order, not proximity.");

            Action<Component> observed = (Action<Component>)Field(interaction.GetType(), "LocalInteractionTargetObserved").GetValue(interaction);
            Assert.That(sites[1].gameObject.activeInHierarchy, Is.True);
            Assert.That(sites[1].GetComponentInParent(Runtime("BridgeConstructionSite")), Is.SameAs(sites[1]),
                "The local observation must use an active, resolvable gameplay target.");
            observed.Invoke(sites[1]);
            Assert.That(Field(source.GetType(), "rememberedSite").GetValue(source), Is.SameAs(sites[1]));
            tick.Invoke(source, new object[] { .22f });
            Assert.That(focusId(), Is.EqualTo(1));
            Field(interaction.GetType(), "_currentTarget").SetValue(interaction, sites[0]);
            tick.Invoke(source, new object[] { .43f });
            Assert.That(focusId(), Is.EqualTo(1), "Aiming alone must not replace used-site memory.");
            player.transform.position = new Vector3(7.001f, 0f, 0f);
            tick.Invoke(source, new object[] { .64f });
            Assert.That(focusId(), Is.EqualTo(0), "Memory expires outside 6 m from the observed interaction point.");

            Field(sites[0].GetType(), "currentStage").SetValue(sites[0], Enum.Parse(Runtime("BridgeConstructionStage"), "Complete"));
            Field(health.GetType(), "isDownedLocal").SetValue(health, true);
            tick.Invoke(source, new object[] { .85f });
            Assert.That(read("CompletedCount"), Is.EqualTo(1));
            Assert.That(read("RequiredCount"), Is.EqualTo(2));
            Assert.That(instruction().GetType().GetField("StepKey").GetValue(instruction()), Is.EqualTo("player.recover"));
            Field(health.GetType(), "isDownedLocal").SetValue(health, false);
            tick.Invoke(source, new object[] { 1.06f });
            Assert.That(focusId(), Is.EqualTo(1));
            Assert.That(read("CompletedCount"), Is.EqualTo(1));
        }
        finally
        {
            if (source != null) ((IDisposable)source).Dispose();
            UnityEngine.Object.Destroy(player);
            UnityEngine.Object.Destroy(managerRoot);
            UnityEngine.Object.Destroy(first);
            UnityEngine.Object.Destroy(second);
            UnityEngine.Object.Destroy(definition);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator MissingOrDestroyedOwnerNeverPublishesInitializedProgress()
    {
        Type sourceType = Type.GetType("BridgeGuidanceSource, Assembly-CSharp", true);
        object source = Activator.CreateInstance(sourceType, new object[] { null, null });
        try
        {
            MethodInfo tick = sourceType.GetMethod("Tick");
            tick.Invoke(source, new object[] { 0f });
            object snapshot = sourceType.GetProperty("Snapshot").GetValue(source);
            Assert.That((bool)snapshot.GetType().GetField("IsReady").GetValue(snapshot), Is.False);
            yield return null;
            tick.Invoke(source, new object[] { 0.25f });
            Assert.That(sourceType.GetProperty("Snapshot").GetValue(source), Is.EqualTo(snapshot));
            sourceType.GetMethod("ResetLocalContext").Invoke(source, null);
            tick.Invoke(source, new object[] { 0.26f });
            Assert.That(sourceType.GetProperty("Snapshot").GetValue(source), Is.EqualTo(snapshot));
        }
        finally
        {
            ((IDisposable)source).Dispose();
            ((IDisposable)source).Dispose();
        }
        sourceType.GetMethod("Tick").Invoke(source, new object[] { 1f });
    }
}
