using System.Collections;
using System.Collections.Generic;
using System;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Loopback wire/snapshot coverage for the server-owned Stage 4 section state.</summary>
public sealed class BridgeDeckPanelStage4NetworkPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> roots = new();
    private readonly List<NetworkManager> managers = new();
    private NetworkManager priorSingleton;
    private NetworkManager host;
    private NetworkManager client;
    private NetworkManager lateJoin;
    private GameObject statePrefab;
    private readonly List<GameObject> gameplayRoots = new();
    private FieldInfo gameplayInstanceField;
    private object priorGameplayInstance;
    private Component serverGameplay;
    private Component clientGameplay;
    private Component lateGameplay;

    [UnityTest]
    public IEnumerator GameplayManagerNamedSnapshotCarriesFuturePanelProgressAndTerminalStageExactlyOnce()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses injected GameplayManager state through NGO named messages in the Editor.");
        yield break;
#else
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The isolated loopback fixture requires no pre-existing NGO session.");
        priorSingleton = NetworkManager.Singleton;
        statePrefab = CreateSectionStatePrefab();
        ScriptableObject panel = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenDeckPanel.asset");
        ScriptableObject earlier = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenAbutment.asset");
        Assert.That(panel, Is.Not.Null);
        Assert.That(earlier, Is.Not.Null);
        System.Type gameplayManagerType = System.Type.GetType("GameplayManager, Assembly-CSharp");
        gameplayInstanceField = gameplayManagerType.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        priorGameplayInstance = gameplayInstanceField.GetValue(null);
        ushort port = ReservePort();
        host = CreateManager("Stage 4 manager snapshot host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "server startup");
        client = CreateManager("Stage 4 manager snapshot client", port);
        Assert.That(client.StartClient(), Is.True);
        client.SetSingleton();
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2,
            "remote peer connection");

        host.SetSingleton();
        serverGameplay = CreateGameplayFixture(gameplayManagerType, panel, earlier, initialServer: true);
        client.SetSingleton();
        clientGameplay = CreateGameplayFixture(gameplayManagerType, panel, earlier, initialServer: false);
        MethodInfo register = gameplayManagerType.GetMethod("EnsureNetworkMessagingRegistered", PrivateInstance);
        client.SetSingleton();
        SetGameplaySingleton(gameplayManagerType, clientGameplay);
        register.Invoke(clientGameplay, null);
        host.SetSingleton();
        SetGameplaySingleton(gameplayManagerType, serverGameplay);
        register.Invoke(serverGameplay, null);

        int clientCompletionEvents = 0;
        EventInfo completed = gameplayManagerType.GetEvent("OnBridgeFullyAssembled");
        AddEventCounter(completed, clientGameplay, () => clientCompletionEvents++);

        MethodInfo broadcast = gameplayManagerType.GetMethod("BroadcastBridgeState", PrivateInstance);
        host.SetSingleton();
        SetGameplaySingleton(gameplayManagerType, serverGameplay);
        broadcast.Invoke(serverGameplay, null);
        client.SetSingleton();
        SetGameplaySingleton(gameplayManagerType, clientGameplay);
        yield return Until(() => (int)gameplayManagerType.GetMethod("GetMountedBridgeComponentCount")
            .Invoke(clientGameplay, new[] { panel }) == 1, "future panel manager snapshot");
        object clientPanelData = ((Array)gameplayManagerType.GetField("bridgeComponentDataArray", PrivateInstance)
            .GetValue(clientGameplay)).GetValue(1);
        Assert.That((bool)clientPanelData.GetType().GetField("isMounted").GetValue(clientPanelData), Is.True);
        Assert.That((bool)clientPanelData.GetType().GetField("isAssembled").GetValue(clientPanelData), Is.True);
        Assert.That(gameplayManagerType.GetProperty("CurrentBridgeStageIndex").GetValue(clientGameplay), Is.EqualTo(0));
        Assert.That(gameplayManagerType.GetProperty("IsFullyAssembled").GetValue(clientGameplay), Is.False);
        Assert.That(gameplayManagerType.GetProperty("CurrentBridgeStageIndex").GetValue(serverGameplay), Is.EqualTo(0),
            "Future panel progress remains blocked by the earlier requirement.");
        Assert.That(clientCompletionEvents, Is.Zero, "Initial/future progress does not fire terminal completion.");

        // Satisfy the earlier stage on the server, then send the final durable snapshot twice.
        object serverData = gameplayManagerType.GetField("bridgeComponentDataArray", PrivateInstance).GetValue(serverGameplay);
        Array dataArray = (Array)serverData;
        SetDataFlag(dataArray, 0, "isMounted", true);
        SetDataFlag(dataArray, 0, "isAssembled", true);
        gameplayManagerType.GetField("bridgeComponentDataArray", PrivateInstance).SetValue(serverGameplay, dataArray);
        System.Collections.IList networkStates = (System.Collections.IList)gameplayManagerType
            .GetField("bridgeComponentStates", PrivateInstance).GetValue(serverGameplay);
        object firstNetworkState = networkStates[0];
        firstNetworkState.GetType().GetField("isMounted").SetValue(firstNetworkState, true);
        firstNetworkState.GetType().GetField("isAssembled").SetValue(firstNetworkState, true);
        networkStates[0] = firstNetworkState;
        host.SetSingleton();
        SetGameplaySingleton(gameplayManagerType, serverGameplay);
        gameplayManagerType.GetMethod("CheckCurrentStageMountingProgress", PrivateInstance).Invoke(serverGameplay, null);
        Assert.That(gameplayManagerType.GetProperty("CurrentBridgeStageIndex").GetValue(serverGameplay), Is.EqualTo(2));
        Assert.That(gameplayManagerType.GetProperty("IsFullyAssembled").GetValue(serverGameplay), Is.True,
            "Completing the earlier requirement advances through the retained future panel and marks the bridge complete.");
        broadcast.Invoke(serverGameplay, null);
        broadcast.Invoke(serverGameplay, null);
        client.SetSingleton();
        SetGameplaySingleton(gameplayManagerType, clientGameplay);
        yield return Until(() => (bool)gameplayManagerType.GetProperty("IsFullyAssembled").GetValue(clientGameplay),
            "terminal manager snapshot");
        Assert.That(gameplayManagerType.GetProperty("CurrentBridgeStageIndex").GetValue(clientGameplay), Is.EqualTo(2));
        Assert.That(clientCompletionEvents, Is.EqualTo(1), "Duplicate snapshots cannot emit terminal completion twice.");

        lateJoin = CreateManager("Stage 4 manager snapshot late join", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        lateJoin.SetSingleton();
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3,
            "late join peer connection");
        lateGameplay = CreateGameplayFixture(gameplayManagerType, panel, earlier, initialServer: false);
        lateJoin.SetSingleton();
        SetGameplaySingleton(gameplayManagerType, lateGameplay);
        register.Invoke(lateGameplay, null);
        lateJoin.SetSingleton();
        SetGameplaySingleton(gameplayManagerType, lateGameplay);
        MethodInfo requestSnapshot = gameplayManagerType.GetMethod("RequestFullBridgeStateFromServer", PrivateInstance);
        requestSnapshot.Invoke(lateGameplay, null);
        host.SetSingleton();
        SetGameplaySingleton(gameplayManagerType, serverGameplay);
        yield return Until(() => (bool)gameplayManagerType.GetProperty("IsFullyAssembled").GetValue(lateGameplay),
            "late join full bridge snapshot");
        Assert.That(gameplayManagerType.GetProperty("CurrentBridgeStageIndex").GetValue(lateGameplay), Is.EqualTo(2));
        object latePanelData = ((Array)gameplayManagerType.GetField("bridgeComponentDataArray", PrivateInstance)
            .GetValue(lateGameplay)).GetValue(1);
        Assert.That((bool)latePanelData.GetType().GetField("isMounted").GetValue(latePanelData), Is.True);

        client.Shutdown(true);
        yield return null;
        host.SetSingleton();
        SetGameplaySingleton(gameplayManagerType, serverGameplay);
        Assert.That(gameplayManagerType.GetProperty("IsFullyAssembled").GetValue(serverGameplay), Is.True,
            "Disconnecting the peer does not erase server completion state.");
#endif
    }

    [UnityTest]
    public IEnumerator ServerCompletionSnapshotReplicatesToRemoteAndLateJoinWithServerWritePermission()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored NetworkBehaviour assets available only in the Editor.");
        yield break;
#else
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The isolated loopback fixture requires no pre-existing NGO session.");
        priorSingleton = NetworkManager.Singleton;
        statePrefab = CreateSectionStatePrefab();
        ushort port = ReservePort();
        host = CreateManager("Stage 4 completion wire host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "server startup");
        client = CreateManager("Stage 4 completion wire client", port);
        Assert.That(client.StartClient(), Is.True);
        client.SetSingleton();
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2,
            "remote peer connection");

        host.SetSingleton();
        GameObject sectionRoot = UnityEngine.Object.Instantiate(statePrefab);
        roots.Add(sectionRoot);
        Component state = sectionRoot.GetComponent(System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp"));
        Assert.That(state, Is.Not.Null);
        NetworkObject sectionObject = sectionRoot.GetComponent<NetworkObject>();
        Assert.That(sectionObject, Is.Not.Null);
        sectionObject.Spawn();
        ulong networkId = sectionObject.NetworkObjectId;
        yield return Until(() => Find(client, networkId) != null, "remote section replica");
        Component clientState = Find(client, networkId).GetComponent(state.GetType());

        object payload = System.Activator.CreateInstance(System.Type.GetType(
            "BridgeDeckPanelReplicatedState, Assembly-CSharp"));
        System.Type payloadType = payload.GetType();
        payloadType.GetField("PanelNetworkObjectId").SetValue(payload, 7001UL);
        payloadType.GetField("SlotIndex").SetValue(payload, 1);
        payloadType.GetField("QuarterMask").SetValue(payload, (byte)15);
        payloadType.GetField("Complete").SetValue(payload, true);
        MethodInfo writePanelState = state.GetType().GetMethod("WritePanelState", PrivateInstance);
        writePanelState.Invoke(state, new object[] { null, payload });
        writePanelState.Invoke(state, new object[] { null, payload });
        object clientList = clientState.GetType().GetField("replicatedPanels", PrivateInstance).GetValue(clientState);
        object serverList = state.GetType().GetField("replicatedPanels", PrivateInstance).GetValue(state);
        MethodInfo canClientWrite = clientList.GetType().GetMethod("CanClientWrite", new[] { typeof(ulong) });
        Assert.That(canClientWrite, Is.Not.Null, "The NGO NetworkList exposes its write-authority check.");
        Assert.That((bool)canClientWrite.Invoke(clientList, new object[] { client.LocalClientId }), Is.False,
            "A remote client replica cannot authoritatively write the completed panel list.");
        Assert.That((bool)canClientWrite.Invoke(serverList, new object[] { NetworkManager.ServerClientId }), Is.True,
            "The server owns writes to the completed panel list.");
        yield return Until(() => ReadCount(clientState) == 1, "remote completion payload");
        Assert.That(ReadComplete(clientState, 0), Is.True);

        lateJoin = CreateManager("Stage 4 completion wire late join", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        lateJoin.SetSingleton();
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3
            && Find(lateJoin, networkId) != null && ReadCount(Find(lateJoin, networkId)
                .GetComponent(state.GetType())) == 1, "late join section state snapshot");
        Component lateState = Find(lateJoin, networkId).GetComponent(state.GetType());
        Assert.That(ReadComplete(lateState, 0), Is.True,
            "The durable NetworkList snapshot carries completed slots to late joiners.");
        Assert.That(ReadCount(state), Is.EqualTo(1), "A repeated completion for one panel is keyed, not appended twice.");
#endif
    }

#if UNITY_EDITOR
    private GameObject CreateSectionStatePrefab()
    {
        GameObject prefab = new("Stage 4 controlled section-state prefab");
        roots.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, 0x42505341u);
        networkObject.SetSceneObjectStatus(false);
        prefab.AddComponent(System.Type.GetType("BridgeDeckSection, Assembly-CSharp"));
        prefab.AddComponent(System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp"));
        prefab.AddComponent(System.Type.GetType("MountingNailSceneState, Assembly-CSharp"));
        return prefab;
    }

    private Component CreateGameplayFixture(System.Type managerType, ScriptableObject panel,
        ScriptableObject earlier, bool initialServer)
    {
        GameObject root = new(initialServer ? "Injected server GameplayManager" : "Injected client GameplayManager");
        gameplayRoots.Add(root);
        root.SetActive(false);
        Component manager = root.AddComponent(managerType);
        GameObject bridgeRoot = new("Injected bridge table");
        gameplayRoots.Add(bridgeRoot);
        bridgeRoot.SetActive(false);
        Component bridge = bridgeRoot.AddComponent(System.Type.GetType("Bridge, Assembly-CSharp"));
        System.Type dataType = managerType.GetField("bridgeComponentDataArray", PrivateInstance).FieldType.GetElementType();
        System.Type bridgeComponentType = System.Type.GetType("BridgeComponent, Assembly-CSharp");
        Array data = Array.CreateInstance(dataType, 2);
        for (int index = 0; index < 2; index++)
        {
            object value = System.Activator.CreateInstance(dataType);
            dataType.GetField("bridgeComponentSO").SetValue(value, index == 0 ? earlier : panel);
            dataType.GetField("isMounted").SetValue(value, initialServer && index == 1);
            dataType.GetField("isAssembled").SetValue(value, initialServer && index == 1);
            data.SetValue(value, index);
            GameObject componentRoot = new("Injected component " + index);
            componentRoot.transform.SetParent(bridgeRoot.transform, false);
            Component component = componentRoot.AddComponent(bridgeComponentType);
            bridgeComponentType.GetField("componentID", PrivateInstance).SetValue(component, index);
            bridgeComponentType.GetField("bridgeComponentSO", PrivateInstance).SetValue(component, index == 0 ? earlier : panel);
        }
        managerType.GetField("bridge", PrivateInstance).SetValue(manager, bridge);
        managerType.GetField("bridgeComponentDataArray", PrivateInstance).SetValue(manager, data);
        managerType.GetField("bridgeBuildingStages", PrivateInstance).SetValue(manager,
            CreateTwoStageFixture(managerType));
        managerType.GetField("currentBridgeBuildingStageIndex", PrivateInstance).SetValue(manager, 0);
        managerType.GetField("bridgeProgressInitialized", PrivateInstance).SetValue(manager, true);
        if (initialServer)
        {
            MethodInfo initialize = managerType.GetMethod("InitializeServerBridgeState", PrivateInstance);
            initialize.Invoke(manager, null);
        }
        return manager;
    }

    private static System.Array CreateTwoStageFixture(System.Type managerType)
    {
        System.Type stageType = managerType.GetField("bridgeBuildingStages", PrivateInstance).FieldType.GetElementType();
        Array stages = Array.CreateInstance(stageType, 2);
        for (int index = 0; index < 2; index++)
        {
            object stage = System.Activator.CreateInstance(stageType);
            stageType.GetField("bridgeComponentDataIndexes", PrivateInstance | BindingFlags.Public)
                .SetValue(stage, index == 0 ? new[] { 0 } : new[] { 1 });
            stages.SetValue(stage, index);
        }
        return stages;
    }

    private static void SetDataFlag(Array data, int index, string fieldName, bool value)
    {
        object item = data.GetValue(index);
        item.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(item, value);
        data.SetValue(item, index);
    }

    private static void SetGameplaySingleton(System.Type managerType, Component value)
        => managerType.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);

    private static void AddEventCounter(EventInfo eventInfo, object target, System.Action counter)
    {
        MethodInfo invoke = eventInfo.EventHandlerType.GetMethod("Invoke");
        ParameterInfo[] parameters = invoke.GetParameters();
        System.Linq.Expressions.ParameterExpression[] lambdaParameters = new System.Linq.Expressions.ParameterExpression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) lambdaParameters[i] = System.Linq.Expressions.Expression.Parameter(parameters[i].ParameterType);
        var body = System.Linq.Expressions.Expression.Call(System.Linq.Expressions.Expression.Constant(counter), counter.GetType().GetMethod("Invoke"));
        Delegate handler = System.Linq.Expressions.Expression.Lambda(eventInfo.EventHandlerType, body, lambdaParameters).Compile();
        eventInfo.AddEventHandler(target, handler);
    }

    private NetworkManager CreateManager(string name, ushort port)
    {
        GameObject root = new(name);
        roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = statePrefab });
        managers.Add(manager);
        return manager;
    }

    private static NetworkObject Find(NetworkManager manager, ulong id) => manager != null
        && manager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject value) ? value : null;
    private static int ReadCount(Component state) => (int)state.GetType().GetField("replicatedPanels", PrivateInstance)
        .GetValue(state).GetType().GetProperty("Count").GetValue(state.GetType().GetField("replicatedPanels", PrivateInstance).GetValue(state));
    private static bool ReadComplete(Component state, int index)
    {
        object list = state.GetType().GetField("replicatedPanels", PrivateInstance).GetValue(state);
        object entry = list.GetType().GetProperty("Item").GetValue(list, new object[] { index });
        return (bool)entry.GetType().GetField("Complete").GetValue(entry);
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
#endif

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        UnregisterGameplayMessages(serverGameplay, host, server: true);
        UnregisterGameplayMessages(clientGameplay, client, server: false);
        UnregisterGameplayMessages(lateGameplay, lateJoin, server: false);
        NetworkManager[] shutdownOrder = { lateJoin, client, host };
        foreach (NetworkManager manager in shutdownOrder)
            if (manager != null && manager.IsListening) manager.Shutdown(true);
        yield return null;
        foreach (NetworkManager manager in shutdownOrder)
            if (manager != null && (manager.IsListening || manager.ShutdownInProgress))
                yield return Until(() => !manager.IsListening && !manager.ShutdownInProgress, "NGO manager shutdown");
        if (host != null) host.SetSingleton();
        if (serverGameplay != null) UnityEngine.Object.Destroy(serverGameplay.gameObject);
        yield return null;
        if (client != null) client.SetSingleton();
        if (clientGameplay != null) UnityEngine.Object.Destroy(clientGameplay.gameObject);
        yield return null;
        if (lateJoin != null) lateJoin.SetSingleton();
        if (lateGameplay != null) UnityEngine.Object.Destroy(lateGameplay.gameObject);
        yield return null;
        foreach (GameObject root in gameplayRoots)
            if (root != null) UnityEngine.Object.Destroy(root);
        yield return null;
        foreach (GameObject root in roots)
            if (root != null) UnityEngine.Object.Destroy(root);
        yield return null;
        if (priorSingleton != null) priorSingleton.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
        if (gameplayInstanceField != null) gameplayInstanceField.SetValue(null, priorGameplayInstance);
        managers.Clear();
        roots.Clear();
        gameplayRoots.Clear();
    }

    private static void UnregisterGameplayMessages(Component gameplay, NetworkManager manager, bool server)
    {
        if (gameplay == null || manager == null || manager.CustomMessagingManager == null) return;
        string[] names = server
            ? new[] { "GameplayManager_BridgeState", "GameplayManager_GirderFasteningWindow",
                "GameplayManager_RequestBridgeState", "GameplayManager_RequestMountBridgeComponent",
                "GameplayManager_RequestAssembleBridgeComponent", "GameplayManager_RequestConstructionWork",
                "GameplayManager_RequestLevelingConfirmation" }
            : new[] { "GameplayManager_BridgeState", "GameplayManager_GirderFasteningWindow" };
        foreach (string name in names) manager.CustomMessagingManager.UnregisterNamedMessageHandler(name);
        gameplay.GetType().GetField("networkMessagingRegistered", PrivateInstance).SetValue(gameplay, false);
    }
}
