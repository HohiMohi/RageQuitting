using System.Collections;
using System.Reflection;
using System.Net.Sockets;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class BridgeCrossBeamClampColliderPlayModeTests
{
    private const string PrefabPath = "Assets/Prefabs/New/BridgeComponents/WoodenCrossBeamBridgeComponent.prefab";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly System.Collections.Generic.List<GameObject> roots = new();
    private readonly System.Collections.Generic.List<NetworkManager> managers = new();
    private NetworkManager priorSingleton;
    private NetworkManager host;
    private NetworkManager client;
    private NetworkManager lateJoin;
    private Component priorGameplay;
    private System.Type gameplayType;
    private Component serverGameplay;
    private Component clientGameplay;
    private Component lateGameplay;
    private GameObject serverBridgeRoot;
    private GameObject clientBridgeRoot;
    private GameObject lateBridgeRoot;

    [UnitySetUp]
    public IEnumerator CapturePriorSingleton()
    {
        priorSingleton = NetworkManager.Singleton;
        gameplayType = System.Type.GetType("GameplayManager, Assembly-CSharp");
        priorGameplay = gameplayType.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as Component;
        yield return null;
    }

    [UnityTest]
    public IEnumerator CompletedConstructionWorkflowDisablesOnlyClampColliders()
    {
#if !UNITY_EDITOR
        Assert.Ignore("The crossbeam prefab regression fixture is loaded through AssetDatabase in the Editor.");
        yield break;
#else
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            Component bridge = instance.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"));
            Component site = instance.GetComponent(System.Type.GetType("BridgeCrossBeamConstructionSite, Assembly-CSharp"));
            Collider mainBeam = bridge.GetComponent<Collider>();
            Transform clampRoot = instance.transform.Find("CrossBeamClampColliders");
            Collider[] clamps = clampRoot.GetComponentsInChildren<Collider>(true);
            Assert.That(clamps.Length, Is.EqualTo(2));
            foreach (Collider clamp in clamps) clamp.enabled = true;

            System.Type constructionSiteType = System.Type.GetType("BridgeConstructionSite, Assembly-CSharp");
            constructionSiteType.GetField("currentStage", PrivateInstance).SetValue(site,
                System.Enum.Parse(System.Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "ReadyForMount"));
            bridge.GetType().GetMethod("ApplyMountedState").Invoke(bridge, null);
            Assert.That((bool)bridge.GetType().GetProperty("IsMounted").GetValue(bridge), Is.True);
            Assert.That((bool)bridge.GetType().GetProperty("IsAssembled").GetValue(bridge), Is.False);

            ScriptableObject componentDefinition = bridge.GetType().GetMethod("GetBridgeComponentSO").Invoke(bridge, null) as ScriptableObject;
            ScriptableObject workflow = componentDefinition.GetType().GetField("crossBeamConstructionWorkflow").GetValue(componentDefinition) as ScriptableObject;
            int maxAlignmentStep = (int)workflow.GetType().GetProperty("MaximumAlignmentStep").GetValue(workflow);
            object alignmentTool = workflow.GetType().GetProperty("AlignmentTool").GetValue(workflow);
            object clampTool = workflow.GetType().GetProperty("ClampingTool").GetValue(workflow);
            object fasteningTool = workflow.GetType().GetProperty("FasteningTool").GetValue(workflow);
            float clampNeeded = (float)workflow.GetType().GetProperty("ClampProgressNeeded").GetValue(workflow);
            float fastenerNeeded = (float)workflow.GetType().GetProperty("FastenerProgressNeeded").GetValue(workflow);
            System.Type toolType = System.Type.GetType("EquippableItemType, Assembly-CSharp");
            MethodInfo applyWork = site.GetType().GetMethod("TryApplyToolWork", PrivateInstance | BindingFlags.Public,
                binder: null, types: new[] { toolType, typeof(float), typeof(int) }, modifiers: null);
            int step = (int)site.GetType().GetProperty("AlignmentStep").GetValue(site);
            int workCount = 0;
            while (step != 0 && workCount++ < maxAlignmentStep * 2)
            {
                int workPoint = step > 0 ? 1 : 0;
                Assert.That((bool)applyWork.Invoke(site, new object[] { alignmentTool, 1f, workPoint }), Is.True);
                step = (int)site.GetType().GetProperty("AlignmentStep").GetValue(site);
            }
            Assert.That(step, Is.Zero);

            float chunk = Mathf.Min(20f, clampNeeded);
            int chunks = Mathf.CeilToInt(clampNeeded / chunk);
            for (int i = 0; i < chunks; i++)
            {
                Assert.That((bool)applyWork.Invoke(site, new object[] { clampTool, chunk, 10 }), Is.True);
                Assert.That((bool)applyWork.Invoke(site, new object[] { clampTool, chunk, 11 }), Is.True);
            }
            for (int i = 0; i < 4; i++)
                Assert.That((bool)applyWork.Invoke(site, new object[] { fasteningTool, fastenerNeeded, 20 + i }), Is.True);

            Assert.That((bool)bridge.GetType().GetProperty("IsAssembled").GetValue(bridge), Is.True);
            Assert.That(mainBeam.enabled, Is.True);
            foreach (Collider clamp in clamps) Assert.That(clamp.enabled, Is.False);
            AssertClampMeshesVisible(instance);

            bridge.GetType().GetMethod("RefreshVisualAndColliderState").Invoke(bridge, null);
            bridge.GetType().GetMethod("RefreshVisualAndColliderState").Invoke(bridge, null);
            Assert.That(mainBeam.enabled, Is.True);
            foreach (Collider clamp in clamps) Assert.That(clamp.enabled, Is.False);
        }
        finally { Object.Destroy(instance); }
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator GameplayManagerSnapshotKeepsClampsDisabledForRemoteAndLateJoinPeers()
    {
#if !UNITY_EDITOR
        Assert.Ignore("The crossbeam fixture and authored definition are loaded through AssetDatabase in the Editor.");
        yield break;
#else
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The isolated loopback fixture requires no pre-existing NGO session.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        ScriptableObject definition = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenCrossBeam.asset");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(definition, Is.Not.Null);
        ushort port = ReservePort();
        host = CreateManager("Crossbeam clamp snapshot host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "host startup");
        client = CreateManager("Crossbeam clamp snapshot remote", port);
        Assert.That(client.StartClient(), Is.True);
        client.SetSingleton();
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "remote peer connection");

        serverBridgeRoot = CreateBridgeRoot(prefab, startCompleted: true);
        AssertCompleted(serverBridgeRoot);
        host.SetSingleton();
        serverGameplay = CreateGameplayFixture("Crossbeam host GameplayManager", serverBridgeRoot, definition, server: true);
        clientBridgeRoot = CreateBridgeRoot(prefab, startCompleted: false);
        client.SetSingleton();
        clientGameplay = CreateGameplayFixture("Crossbeam remote GameplayManager", clientBridgeRoot, definition, server: false);
        RegisterGameplay(clientGameplay, client, server: false);
        RegisterGameplay(serverGameplay, host, server: true);
        Broadcast(serverGameplay, host);

        client.SetSingleton();
        SetGameplaySingleton(clientGameplay);
        yield return Until(() => IsAssembled(clientBridgeRoot), "remote completed crossbeam snapshot");
        AssertCompleted(clientBridgeRoot);

        lateJoin = CreateManager("Crossbeam clamp snapshot late join", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        lateJoin.SetSingleton();
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3, "late join connection");
        lateBridgeRoot = CreateBridgeRoot(prefab, startCompleted: false);
        lateJoin.SetSingleton();
        lateGameplay = CreateGameplayFixture("Crossbeam late-join GameplayManager", lateBridgeRoot, definition, server: false);
        RegisterGameplay(lateGameplay, lateJoin, server: false);
        RequestSnapshot(lateGameplay, lateJoin);
        host.SetSingleton();
        SetGameplaySingleton(serverGameplay);
        yield return Until(() => IsAssembled(lateBridgeRoot), "late-join completed crossbeam snapshot");
        AssertCompleted(lateBridgeRoot);
#endif
    }

#if UNITY_EDITOR
    private GameObject CreateBridgeRoot(GameObject prefab, bool startCompleted)
    {
        GameObject root = new("Crossbeam bridge fixture");
        root.SetActive(false);
        roots.Add(root);
        root.AddComponent(System.Type.GetType("Bridge, Assembly-CSharp"));
        GameObject holder = new("Bridge component holder");
        holder.transform.SetParent(root.transform, false);
        GameObject component = Object.Instantiate(prefab, holder.transform, false);
        component.name = "WoodenCrossBeamBridgeComponent";
        SerializedObject serialized = new(component.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp")));
        serialized.FindProperty("startFullyCompleted").boolValue = startCompleted;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (startCompleted)
        {
            // The host owns initial completion; invoking normal Awake exercises that path before the server snapshot is built.
            host.SetSingleton();
            Component constructionSite = component.GetComponent(System.Type.GetType("BridgeCrossBeamConstructionSite, Assembly-CSharp"));
            constructionSite.GetType().GetMethod("Awake", PrivateInstance).Invoke(constructionSite, null);
            component.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"))
                .GetType().GetMethod("Awake", PrivateInstance).Invoke(component.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp")), null);
        }
        else
        {
            Component constructionSite = component.GetComponent(System.Type.GetType("BridgeCrossBeamConstructionSite, Assembly-CSharp"));
            constructionSite.GetType().GetMethod("Awake", PrivateInstance).Invoke(constructionSite, null);
            Component bridgeComponent = component.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"));
            bridgeComponent.GetType().GetMethod("Awake", PrivateInstance).Invoke(bridgeComponent, null);
        }
        return root;
    }

    private Component CreateGameplayFixture(string name, GameObject bridgeRoot, ScriptableObject definition, bool server)
    {
        GameObject root = new(name);
        root.SetActive(false);
        roots.Add(root);
        Component manager = root.AddComponent(gameplayType);
        Component bridge = bridgeRoot.GetComponent(System.Type.GetType("Bridge, Assembly-CSharp"));
        gameplayType.GetField("bridge", PrivateInstance).SetValue(manager, bridge);
        System.Type dataType = gameplayType.GetField("bridgeComponentDataArray", PrivateInstance).FieldType.GetElementType();
        System.Array data = System.Array.CreateInstance(dataType, 1);
        object row = System.Activator.CreateInstance(dataType);
        dataType.GetField("bridgeComponentSO").SetValue(row, definition);
        dataType.GetField("isMounted").SetValue(row, server);
        dataType.GetField("isAssembled").SetValue(row, server);
        data.SetValue(row, 0);
        gameplayType.GetField("bridgeComponentDataArray", PrivateInstance).SetValue(manager, data);
        System.Type stageType = gameplayType.GetField("bridgeBuildingStages", PrivateInstance).FieldType.GetElementType();
        System.Array stages = System.Array.CreateInstance(stageType, 1);
        object stage = System.Activator.CreateInstance(stageType);
        stageType.GetField("bridgeComponentDataIndexes", PrivateInstance | BindingFlags.Public).SetValue(stage, new[] { 0 });
        stages.SetValue(stage, 0);
        gameplayType.GetField("bridgeBuildingStages", PrivateInstance).SetValue(manager, stages);
        gameplayType.GetField("bridgeProgressInitialized", PrivateInstance).SetValue(manager, true);
        gameplayType.GetMethod("CacheBridgeComponents", PrivateInstance).Invoke(manager, null);
        SetGameplaySingleton(manager);
        return manager;
    }

    private static void RegisterGameplay(Component gameplay, NetworkManager manager, bool server)
    {
        manager.SetSingleton();
        SetGameplaySingleton(gameplay);
        MethodInfo register = gameplay.GetType().GetMethod("EnsureNetworkMessagingRegistered", PrivateInstance);
        register.Invoke(gameplay, null);
        if (server) gameplay.GetType().GetMethod("InitializeServerBridgeState", PrivateInstance).Invoke(gameplay, null);
    }

    private static void Broadcast(Component gameplay, NetworkManager manager)
    {
        manager.SetSingleton();
        SetGameplaySingleton(gameplay);
        gameplay.GetType().GetMethod("BroadcastBridgeState", PrivateInstance).Invoke(gameplay, null);
    }

    private static void RequestSnapshot(Component gameplay, NetworkManager manager)
    {
        manager.SetSingleton();
        SetGameplaySingleton(gameplay);
        gameplay.GetType().GetMethod("RequestFullBridgeStateFromServer", PrivateInstance).Invoke(gameplay, null);
    }
#endif

    private NetworkManager CreateManager(string name, ushort port)
    {
        GameObject root = new(name);
        roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        managers.Add(manager);
        return manager;
    }

    private static bool IsAssembled(GameObject root)
    {
        Component bridge = root.GetComponentInChildren(System.Type.GetType("BridgeComponent, Assembly-CSharp"), true);
        return bridge != null && (bool)bridge.GetType().GetProperty("IsAssembled").GetValue(bridge);
    }

    private static void AssertCompleted(GameObject root)
    {
        Component bridge = root.GetComponentInChildren(System.Type.GetType("BridgeComponent, Assembly-CSharp"), true);
        Assert.That(bridge.GetComponent<Collider>().enabled, Is.True);
        Collider[] clamps = bridge.transform.Find("CrossBeamClampColliders").GetComponentsInChildren<Collider>(true);
        Assert.That(clamps, Has.Length.EqualTo(2));
        foreach (Collider clamp in clamps) Assert.That(clamp.enabled, Is.False);
    }

    private static void AssertClampMeshesVisible(GameObject root)
    {
        foreach (string clampPath in new[] { "CrossBeamVisualRoot/LeftClampVisual", "CrossBeamVisualRoot/RightClampVisual" })
        {
            Transform clampVisual = root.transform.Find(clampPath);
            Assert.That(clampVisual, Is.Not.Null, "Missing authored clamp mesh " + clampPath + ".");
            Renderer[] renderers = clampVisual.GetComponentsInChildren<Renderer>(true);
            Assert.That(System.Array.Exists(renderers, renderer => renderer.enabled && renderer.gameObject.activeInHierarchy),
                Is.True, "Clamp mesh should remain visible after assembly: " + clampPath + ".");
        }
    }

    private static void SetGameplaySingleton(Component gameplay)
    {
        System.Type type = System.Type.GetType("GameplayManager, Assembly-CSharp");
        type.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, gameplay);
    }

    private static ushort ReservePort()
    {
        TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        ushort port = (ushort)((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static IEnumerator Until(System.Func<bool> condition, string description)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out waiting for " + description + ".");
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        Unregister(serverGameplay, host, server: true);
        Unregister(clientGameplay, client, server: false);
        Unregister(lateGameplay, lateJoin, server: false);
        foreach (NetworkManager manager in new[] { lateJoin, client, host })
            if (manager != null && manager.IsListening) manager.Shutdown(true);
        yield return null;
        foreach (NetworkManager manager in new[] { lateJoin, client, host })
            if (manager != null && (manager.IsListening || manager.ShutdownInProgress))
                yield return Until(() => !manager.IsListening && !manager.ShutdownInProgress, "NGO shutdown");
        if (lateJoin != null) lateJoin.SetSingleton();
        if (client != null) client.SetSingleton();
        if (host != null) host.SetSingleton();
        foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        yield return null;
        if (priorSingleton != null) priorSingleton.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
        gameplayType.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, priorGameplay);
        roots.Clear();
        managers.Clear();
        host = null;
        client = null;
        lateJoin = null;
        serverGameplay = null;
        clientGameplay = null;
        lateGameplay = null;
        serverBridgeRoot = null;
        clientBridgeRoot = null;
        lateBridgeRoot = null;
    }

    private static void Unregister(Component gameplay, NetworkManager manager, bool server)
    {
        if (gameplay == null || manager == null || manager.CustomMessagingManager == null) return;
        string[] names = server
            ? new[] { "GameplayManager_BridgeState", "GameplayManager_GirderFasteningWindow", "GameplayManager_RequestBridgeState", "GameplayManager_RequestMountBridgeComponent", "GameplayManager_RequestAssembleBridgeComponent", "GameplayManager_RequestConstructionWork", "GameplayManager_RequestLevelingConfirmation" }
            : new[] { "GameplayManager_BridgeState", "GameplayManager_GirderFasteningWindow" };
        foreach (string name in names) manager.CustomMessagingManager.UnregisterNamedMessageHandler(name);
        gameplay.GetType().GetField("networkMessagingRegistered", PrivateInstance).SetValue(gameplay, false);
    }
}
