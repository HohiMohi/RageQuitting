using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Type = System.Type;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class LooseSoilStage2NetworkPlayModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private readonly List<GameObject> roots = new();
    private readonly List<NetworkManager> managers = new();
    private NetworkManager priorSingleton, host, client, lateJoin;
    private Component priorGameplay, serverGameplay, clientGameplay, lateGameplay;
    private Type gameplayType, bridgeType, bridgeComponentType, siteType, volumeType, fieldType;

    [UnitySetUp]
    public IEnumerator CapturePriorNetworkState()
    {
        priorSingleton = NetworkManager.Singleton;
        if (priorSingleton != null && priorSingleton.IsListening)
            Assert.Ignore("The isolated loose-soil snapshot fixture requires no pre-existing NGO session.");
        gameplayType = Type.GetType("GameplayManager, Assembly-CSharp");
        bridgeType = Type.GetType("Bridge, Assembly-CSharp");
        bridgeComponentType = Type.GetType("BridgeComponent, Assembly-CSharp");
        siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        priorGameplay = gameplayType.GetField("<Instance>k__BackingField", StaticPrivate).GetValue(null) as Component;
        yield return null;
    }

    [UnityTest]
    public IEnumerator TwoGridShapeTrailerReplicatesToClientLateJoinAndRejectsSpoofAndTruncationAtomically()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Stage02 fixtures are cloned from the authored scene through AssetDatabase in the Editor.");
        yield break;
#else
        ushort port = ReservePort();
        host = CreateManager("Loose soil snapshot host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "host startup");
        client = CreateManager("Loose soil snapshot client", port);
        client.SetSingleton();
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "client connection");

        PeerFixture server = CreatePeerFixture("host");
        PeerFixture remote = CreatePeerFixture("client");
        serverGameplay = CreateGameplayFixture("Loose soil host GameplayManager", server, client: false);
        clientGameplay = CreateGameplayFixture("Loose soil client GameplayManager", remote, client: true);
        RegisterGameplay(clientGameplay, client, server: false);
        RegisterGameplay(serverGameplay, host, server: true);

        RemoveShape(server.Fields[0], -2f, 0f, 1f, .5f);
        RemoveShape(server.Fields[1], 2f, 0f, 1f, .5f);
        object firstField0 = Capture(server.Fields[0]);
        object firstField1 = Capture(server.Fields[1]);
        Assert.That(Matches(server.Fields[0], firstField1), Is.False,
            "The two Stage02 foundations must carry distinct spatial shapes at the same zero removed-unit counter.");
        Assert.That((float)Property(server.Sites[0], "RemovedSoilUnits"), Is.Zero);
        Assert.That((float)Property(server.Sites[1], "RemovedSoilUnits"), Is.Zero);

        Broadcast(serverGameplay, host);
        client.SetSingleton();
        SetGameplaySingleton(clientGameplay);
        yield return Until(() => Matches(remote.Fields[0], firstField0) && Matches(remote.Fields[1], firstField1),
            "both complete loose-soil grids on the remote client");

        AddShape(server.Fields[0], -2f, 0f, 1f, .5f);
        RemoveShape(server.Fields[0], 2f, 0f, 1f, .5f);
        object secondField0 = Capture(server.Fields[0]);
        Assert.That(Matches(server.Fields[0], firstField0), Is.False);
        Assert.That((float)Property(server.Sites[0], "RemovedSoilUnits"), Is.Zero,
            "Shape changes are independent from the regular construction counters.");
        Broadcast(serverGameplay, host);
        client.SetSingleton();
        SetGameplaySingleton(clientGameplay);
        yield return Until(() => Matches(remote.Fields[0], secondField0), "same-counter second shape update");

        lateJoin = CreateManager("Loose soil snapshot late join", port);
        lateJoin.SetSingleton();
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3, "late join connection");
        PeerFixture late = CreatePeerFixture("late");
        lateGameplay = CreateGameplayFixture("Loose soil late GameplayManager", late, client: true);
        RegisterGameplay(lateGameplay, lateJoin, server: false);
        RequestSnapshot(lateGameplay, lateJoin);
        host.SetSingleton();
        SetGameplaySingleton(serverGameplay);
        yield return Until(() => Matches(late.Fields[0], secondField0) && Matches(late.Fields[1], firstField1),
            "both full snapshots on the late-joining peer");

        AssertAtomicRejection(serverGameplay, clientGameplay, remote, client, truncated: true);
        AssertAtomicRejection(serverGameplay, clientGameplay, remote, client, truncated: false);
        AssertSpoofRejection(serverGameplay, server, clientGameplay, remote, client);
#endif
    }

#if UNITY_EDITOR
    private sealed class PeerFixture
    {
        public GameObject BridgeRoot;
        public Component[] Sites;
        public Component[] Fields;
    }

    private PeerFixture CreatePeerFixture(string label)
    {
        GameObject bridgeRoot = new GameObject("Loose soil " + label + " Bridge");
        bridgeRoot.SetActive(false);
        roots.Add(bridgeRoot);
        Component bridge = bridgeRoot.AddComponent(bridgeType);
        GameObject holder = new GameObject("Bridge component holder");
        holder.transform.SetParent(bridgeRoot.transform, false);
        bridgeType.GetField("bridgeComponentHolder", Instance).SetValue(bridge, holder);
        UnityEngine.Object definition = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenFoundation.asset");
        Material looseMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat");
        Assert.That(definition, Is.Not.Null);
        Assert.That(looseMaterial, Is.Not.Null);

        var sites = new Component[2];
        var fields = new Component[2];
        for (int index = 0; index < 2; index++)
        {
            GameObject siteObject = new GameObject("Foundation " + index);
            siteObject.transform.SetParent(holder.transform, false);
            Component component = siteObject.AddComponent(bridgeComponentType);
            bridgeComponentType.GetField("componentID", Instance).SetValue(component, index);
            bridgeComponentType.GetField("bridgeComponentSO", Instance).SetValue(component, definition);
            Component site = siteObject.AddComponent(siteType);
            Component volume = siteObject.AddComponent(volumeType);

            GameObject supportObject = new GameObject("DynamicSoilSurface");
            supportObject.transform.SetParent(siteObject.transform, false);
            supportObject.transform.localPosition = new Vector3(0f, .02f, 0f);
            BoxCollider support = supportObject.AddComponent<BoxCollider>();
            support.size = new Vector3(6.5f, .12f, 4f);
            MeshRenderer supportRenderer = supportObject.AddComponent<MeshRenderer>();
            GameObject fieldObject = new GameObject("LooseSoilSurface", typeof(MeshFilter), typeof(MeshRenderer));
            fieldObject.transform.SetParent(siteObject.transform, false);
            fieldObject.transform.localPosition = new Vector3(0f, .08f, 0f);
            Component field = fieldObject.AddComponent(fieldType);
            field.GetType().GetMethod("Initialize").Invoke(field, new object[] { 6.5f, 4f, .2f, .4f, .4f, 6f });
            field.GetType().GetMethod("SetSurfaceState").Invoke(field, new object[] { false, looseMaterial });

            SetField(volume, "soilSurface", supportObject.transform);
            SetField(volume, "constructionSite", site);
            SetField(volume, "soilRenderer", supportRenderer);
            SetField(volume, "loosenedSoilMaterial", looseMaterial);
            SetField(volume, "surfaceStartLocalY", .02f);
            SetField(volume, "useLooseSoilHeightField", true);
            SetField(volume, "looseSoilField", field);
            SetField(volume, "soilSupportCollider", support);
            SetField(volume, "concreteFootprintSize", new Vector2(6.5f, 4f));
            SetField(site, "excavationVolume", volume);
            sites[index] = site;
            fields[index] = field;
        }
        bridgeRoot.SetActive(true);
        foreach (Component site in sites) siteType.GetMethod("Initialize", Instance).Invoke(site, null);
        Assert.That(fields, Has.Length.EqualTo(2));
        foreach (Component field in fields)
        {
            Assert.That(field, Is.Not.Null);
            Assert.That((bool)Property(field, "HasHeightField"), Is.True);
            Assert.That(field.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(Property(field, "RuntimeMesh")));
        }
        return new PeerFixture { BridgeRoot = bridgeRoot, Sites = sites, Fields = fields };
    }

    private Component CreateGameplayFixture(string name, PeerFixture peer, bool client)
    {
        GameObject root = new GameObject(name);
        root.SetActive(false);
        roots.Add(root);
        Component manager = root.AddComponent(gameplayType);
        gameplayType.GetField("bridge", Instance).SetValue(manager, peer.BridgeRoot.GetComponent(bridgeType));
        Type dataType = gameplayType.GetField("bridgeComponentDataArray", Instance).FieldType.GetElementType();
        Component[] components = peer.BridgeRoot.GetComponentsInChildren(bridgeComponentType, true);
        Array.Sort(components, (left, right) => (int)Property(left, "ComponentID") - (int)Property(right, "ComponentID"));
        int dataCount = (int)Property(components[components.Length - 1], "ComponentID") + 1;
        Array componentData = Array.CreateInstance(dataType, dataCount);
        int[] componentIndexes = new int[components.Length];
        for (int i = 0; i < components.Length; i++)
        {
            int componentIndex = (int)Property(components[i], "ComponentID");
            componentIndexes[i] = componentIndex;
            object row = Activator.CreateInstance(dataType);
            dataType.GetField("bridgeComponentSO").SetValue(row,
                components[i].GetType().GetMethod("GetBridgeComponentSO").Invoke(components[i], null));
            componentData.SetValue(row, componentIndex);
        }
        gameplayType.GetField("bridgeComponentDataArray", Instance).SetValue(manager, componentData);
        Type stageType = gameplayType.GetField("bridgeBuildingStages", Instance).FieldType.GetElementType();
        Array stages = Array.CreateInstance(stageType, 1);
        object stage = Activator.CreateInstance(stageType);
        stageType.GetField("bridgeComponentDataIndexes", Instance).SetValue(stage, componentIndexes);
        stages.SetValue(stage, 0);
        gameplayType.GetField("bridgeBuildingStages", Instance).SetValue(manager, stages);
        gameplayType.GetField("bridgeProgressInitialized", Instance).SetValue(manager, true);
        gameplayType.GetMethod("CacheBridgeComponents", Instance).Invoke(manager, null);
        SetGameplaySingleton(manager);
        return manager;
    }

    private static void RegisterGameplay(Component gameplay, NetworkManager manager, bool server)
    {
        manager.SetSingleton();
        SetGameplaySingleton(gameplay);
        gameplay.GetType().GetMethod("EnsureNetworkMessagingRegistered", Instance).Invoke(gameplay, null);
        if (server) gameplay.GetType().GetMethod("InitializeServerBridgeState", Instance).Invoke(gameplay, null);
    }

    private static void Broadcast(Component gameplay, NetworkManager manager)
    {
        manager.SetSingleton();
        SetGameplaySingleton(gameplay);
        gameplay.GetType().GetMethod("BroadcastBridgeState", Instance).Invoke(gameplay, null);
    }

    private static void RequestSnapshot(Component gameplay, NetworkManager manager)
    {
        manager.SetSingleton();
        SetGameplaySingleton(gameplay);
        gameplay.GetType().GetMethod("RequestFullBridgeStateFromServer", Instance).Invoke(gameplay, null);
    }

    private void AssertAtomicRejection(Component serverManager,
        Component clientManager, PeerFixture clientFixture, NetworkManager receiver, bool truncated)
    {
        Type siteBaseType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        object beforeShape = Capture(clientFixture.Fields[0]);
        object previousStage = Property(clientFixture.Sites[0], "CurrentStage");
        object stateEnum = Enum.Parse(Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "ReadyForMount");
        siteBaseType.GetField("currentStage", Instance).SetValue(clientFixture.Sites[0], stateEnum);
        host.SetSingleton();
        SetGameplaySingleton(serverManager);
        using FastBufferWriter writer = (FastBufferWriter)serverManager.GetType().GetMethod("CreateBridgeStateWriter", Instance).Invoke(serverManager, null);
        clientFixture.Sites[0].GetType().GetMethod("ApplyNetworkState", Instance);
        receiver.SetSingleton();
        SetGameplaySingleton(clientManager);
        MethodInfo handler = clientManager.GetType().GetMethod("HandleBridgeStateMessage", Instance);
        if (truncated)
        {
            using FastBufferReader reader = new FastBufferReader(writer, Allocator.Temp, writer.Length - 1);
            handler.Invoke(clientManager, new object[] { NetworkManager.ServerClientId, reader });
        }
        else
        {
            byte[] bytes = writer.ToArray();
            IList states = (IList)serverManager.GetType().GetField("bridgeComponentStates", Instance).GetValue(serverManager);
            int baseLength = GetBridgeStateBaseLength(serverManager.GetType(), states.Count);
            Array.Copy(BitConverter.GetBytes(0), 0, bytes, baseLength, sizeof(int));
            using FastBufferReader reader = new FastBufferReader(bytes, Allocator.Temp);
            handler.Invoke(clientManager, new object[] { NetworkManager.ServerClientId, reader });
        }
        Assert.That(Property(clientFixture.Sites[0], "CurrentStage"), Is.EqualTo(stateEnum),
            "Invalid or incomplete trailers must be rejected before bridge body state mutates.");
        Assert.That(Matches(clientFixture.Fields[0], beforeShape), Is.True);
        siteBaseType.GetField("currentStage", Instance).SetValue(clientFixture.Sites[0], previousStage);
    }

    private void AssertSpoofRejection(Component serverManager, PeerFixture server,
        Component clientManager, PeerFixture clientFixture, NetworkManager receiver)
    {
        object beforeShape = Capture(clientFixture.Fields[0]);
        object previousStage = Property(clientFixture.Sites[0], "CurrentStage");
        Type siteBaseType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        object markerStage = Enum.Parse(Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "ReadyForMount");
        siteBaseType.GetField("currentStage", Instance).SetValue(clientFixture.Sites[0], markerStage);
        host.SetSingleton();
        SetGameplaySingleton(serverManager);
        using FastBufferWriter writer = (FastBufferWriter)serverManager.GetType().GetMethod("CreateBridgeStateWriter", Instance).Invoke(serverManager, null);
        receiver.SetSingleton();
        SetGameplaySingleton(clientManager);
        using FastBufferReader reader = new FastBufferReader(writer, Allocator.Temp);
        clientManager.GetType().GetMethod("HandleBridgeStateMessage", Instance)
            .Invoke(clientManager, new object[] { 987654321UL, reader });
        Assert.That(Property(clientFixture.Sites[0], "CurrentStage"), Is.EqualTo(markerStage));
        Assert.That(Matches(clientFixture.Fields[0], beforeShape), Is.True);
        siteBaseType.GetField("currentStage", Instance).SetValue(clientFixture.Sites[0], previousStage);
    }

    private static int GetBridgeStateBaseLength(Type managerType, int stateCount)
    {
        int header = (int)managerType.GetField("StateMessageBaseSize", StaticPrivate).GetRawConstantValue();
        int item = (int)managerType.GetField("StateMessageItemSize", StaticPrivate).GetRawConstantValue();
        return header + stateCount * item;
    }

    private static void RemoveShape(Component field, float x, float z, float radius, float units)
    {
        object model = Property(field, "HeightField");
        float moved = (float)model.GetType().GetMethod("Remove").Invoke(model, new object[] { x, z, radius, units });
        Assert.That(moved, Is.EqualTo(units).Within(.0001f));
        field.GetType().GetMethod("RebuildMesh").Invoke(field, null);
    }

    private static void AddShape(Component field, float x, float z, float radius, float units)
    {
        object model = Property(field, "HeightField");
        float moved = (float)model.GetType().GetMethod("Add").Invoke(model, new object[] { x, z, radius, units });
        Assert.That(moved, Is.EqualTo(units).Within(.0001f));
        field.GetType().GetMethod("RebuildMesh").Invoke(field, null);
    }

    private static object Capture(Component field) => field.GetType().GetMethod("CaptureSnapshot").Invoke(field, null);
    private static bool Matches(Component field, object snapshot)
    {
        object model = Property(field, "HeightField");
        return (bool)model.GetType().GetMethod("MatchesSnapshot").Invoke(model, new[] { snapshot });
    }

    private NetworkManager CreateManager(string name, ushort port)
    {
        GameObject root = new GameObject(name);
        roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        managers.Add(manager);
        return manager;
    }

    private static ushort ReservePort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        ushort port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static IEnumerator Until(Func<bool> condition, string description)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out waiting for " + description + ".");
    }

    private static object ReadField(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, Instance).SetValue(target, value);
    private static object Property(object target, string name) => target.GetType().GetProperty(name, Instance).GetValue(target);
    private static void SetGameplaySingleton(Component gameplay) =>
        Type.GetType("GameplayManager, Assembly-CSharp").GetField("<Instance>k__BackingField", StaticPrivate).SetValue(null, gameplay);
#endif

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
#if UNITY_EDITOR
        Unregister(serverGameplay, host, true);
        Unregister(clientGameplay, client, false);
        Unregister(lateGameplay, lateJoin, false);
#endif
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
        else typeof(NetworkManager).GetMethod("ResetSingleton", StaticPrivate)?.Invoke(null, null);
        if (gameplayType != null) gameplayType.GetField("<Instance>k__BackingField", StaticPrivate).SetValue(null, priorGameplay);
        roots.Clear(); managers.Clear();
        host = client = lateJoin = null;
        serverGameplay = clientGameplay = lateGameplay = null;
    }

#if UNITY_EDITOR
    private static void Unregister(Component gameplay, NetworkManager manager, bool server)
    {
        if (gameplay == null || manager == null || manager.CustomMessagingManager == null) return;
        string[] names = server
            ? new[] { "GameplayManager_BridgeState", "GameplayManager_GirderFasteningWindow", "GameplayManager_RequestBridgeState", "GameplayManager_RequestMountBridgeComponent", "GameplayManager_RequestAssembleBridgeComponent", "GameplayManager_RequestConstructionWork", "GameplayManager_RequestLevelingConfirmation" }
            : new[] { "GameplayManager_BridgeState", "GameplayManager_GirderFasteningWindow" };
        foreach (string message in names) manager.CustomMessagingManager.UnregisterNamedMessageHandler(message);
        gameplay.GetType().GetField("networkMessagingRegistered", Instance).SetValue(gameplay, false);
    }
#endif
}
