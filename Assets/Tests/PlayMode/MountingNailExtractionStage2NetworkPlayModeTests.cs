using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class MountingNailExtractionStage2NetworkPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const uint NailExecutorPrefabHash = 0x4E414638u;
    private const uint StatePrefabHash = 0x4E414640u;
    private const uint NailTargetPrefabHash = 0x4E414639u;
    private readonly List<GameObject> roots = new();
    private NetworkManager previousSingleton;
    private NetworkManager host;
    private NetworkManager client;
    private NetworkManager lateJoin;
    private GameObject nailPrefab;
    private GameObject executorPrefab;
    private GameObject targetPrefab;
    private GameObject statePrefab;
    private Type stateType;
    private Type identityType;
    private Component hostState;
    private Component clientState;
    private Component lateState;

    [UnityTest]
    public IEnumerator ServerReservationReplicatesReadStateToClientAndLateJoinAndRejectsClientMutation()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity networking components.");
        yield break;
#else
        previousSingleton = NetworkManager.Singleton;
        nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null);
        BuildStatePrefab();
        BuildNetworkPrefabs();
        ushort port = ReservePort();
        host = CreateManager("Stage 2 reservation host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "host start");
        client = CreateManager("Stage 2 reservation client", port);
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "remote client connect");

        NetworkObject stateObject = SpawnState();
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(stateObject.NetworkObjectId), "scene-state replication");
        hostState = stateObject.GetComponent(stateType);
        clientState = client.SpawnManager.SpawnedObjects[stateObject.NetworkObjectId].GetComponent(stateType);
        GameObject hostTarget = CreateTarget("stage2-extraction-target");
        GameObject clientTarget = CreateTarget("stage2-extraction-target");
        Assert.That(Register(hostState, hostTarget), Is.True);
        Assert.That(Register(clientState, clientTarget), Is.True);

        NetworkObject remoteExecutor = SpawnExecutor(client.LocalClientId);
        Component serverNail = SpawnNail(hostTarget.transform);
        ulong nailId = serverNail.GetComponent<NetworkObject>().NetworkObjectId;
        ulong executorId = remoteExecutor.NetworkObjectId;
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(nailId)
            && client.SpawnManager.SpawnedObjects.ContainsKey(executorId), "nail and remote executor replicas");
        Component clientNail = client.SpawnManager.SpawnedObjects[nailId]
            .GetComponent(System.Type.GetType("MountingNail, Assembly-CSharp"));
        yield return Until(() => clientNail.GetType().GetField("embeddedTarget", PrivateInstance).GetValue(clientNail)
            == clientTarget.transform && Availability(clientNail) == "Available", "client attachment reference resolves");
        Behaviour clientExecutor = client.SpawnManager.SpawnedObjects[executorId].GetComponent<AudioSource>();
        Behaviour serverExecutor = remoteExecutor.GetComponent<AudioSource>();
        object handle = Begin(serverNail, serverExecutor, 5f);
        Assert.That(IsHandleValid(handle), Is.True);
        yield return Until(() => Availability(clientNail) == "Reserved"
            && ReadState(clientNail, "Handle").Equals(handle), "reservation handle replicates before client read assertions");
        Assert.That(Availability(serverNail), Is.EqualTo("Reserved"));
        Assert.That(ReadState(serverNail, "ExecutorNetworkObjectId"), Is.EqualTo(executorId));
        Assert.That(ReadState(serverNail, "ExecutorOwnerClientId"), Is.EqualTo(client.LocalClientId));
        double acceptedStart = (double)ReadState(serverNail, "StartedAtServerTime");
        double deadline = (double)ReadState(serverNail, "DeadlineServerTime");
        Assert.That(deadline - acceptedStart, Is.EqualTo(5d).Within(0.001d));
        Assert.That(Commit(serverNail, serverExecutor, handle), Is.False, "The server refuses commit before deadline.");

        List<string> clientErrors = new();
        Application.LogCallback logHandler = (condition, _, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) clientErrors.Add(condition);
        };
        Application.logMessageReceived += logHandler;

        object clientHandle = ReadState(clientNail, "Handle");
        Assert.That(clientHandle, Is.EqualTo(handle), "The server-owned snapshot exposes the matching request token.");
        Assert.That(Availability(clientNail), Is.EqualTo("Reserved"));
        Assert.That((double)ReadState(clientNail, "StartedAtServerTime"), Is.EqualTo(acceptedStart).Within(0.001d));
        Assert.That((float)ReadState(clientNail, "DurationSeconds"), Is.EqualTo(5f).Within(0.001f));
        Assert.That((double)ReadState(clientNail, "DeadlineServerTime"), Is.EqualTo(deadline).Within(0.001d));
        Assert.That(Cancel(clientNail, clientExecutor, clientHandle), Is.False);
        Assert.That(Commit(clientNail, clientExecutor, clientHandle), Is.False);
        Assert.That(ReadState(clientNail, "Handle"), Is.EqualTo(handle),
            "Rejected client calls leave the received server snapshot unchanged.");

        lateJoin = CreateManager("Stage 2 reservation late join", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3, "late join connect");
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(stateObject.NetworkObjectId), "late-join scene-state replica");
        lateState = lateJoin.SpawnManager.SpawnedObjects[stateObject.NetworkObjectId].GetComponent(stateType);
        GameObject lateTarget = CreateTarget("stage2-extraction-target");
        Assert.That(Register(lateState, lateTarget), Is.True);
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(nailId), "active extraction snapshot for late join");
        Component lateNail = lateJoin.SpawnManager.SpawnedObjects[nailId]
            .GetComponent(System.Type.GetType("MountingNail, Assembly-CSharp"));
        yield return Until(() => lateNail.GetType().GetField("embeddedTarget", PrivateInstance).GetValue(lateNail)
            == lateTarget.transform && Availability(lateNail) == "Reserved", "late-join attachment reference resolves");
        Assert.That(Availability(lateNail), Is.EqualTo("Reserved"));
        Assert.That(ReadState(lateNail, "Handle"), Is.EqualTo(handle));
        Assert.That(ReadState(lateNail, "ExecutorNetworkObjectId"), Is.EqualTo(executorId));
        Assert.That(ReadState(lateNail, "ExecutorOwnerClientId"), Is.EqualTo(client.LocalClientId));
        Assert.That((double)ReadState(lateNail, "StartedAtServerTime"), Is.EqualTo(acceptedStart).Within(0.001d));
        yield return Until(() => Progress(clientNail) > 0f && Progress(lateNail) > 0f,
            "each peer derives progress from synchronized server time");
        Assert.That((float)ReadState(lateNail, "DurationSeconds"), Is.EqualTo(5f).Within(0.001f));
        yield return null;
        yield return null;
        Assert.That(Progress(clientNail), Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That(Progress(lateNail), Is.GreaterThan(0f).And.LessThan(1f));
        Application.logMessageReceived -= logHandler;
        Assert.That(clientErrors, Is.Empty, "Replicated active snapshots do not cause client-side write errors.");
        Assert.That(Cancel(serverNail, serverExecutor, handle), Is.True);
        yield return Until(() => Availability(clientNail) != "Reserved"
            && Availability(lateNail) != "Reserved", "reservation cancellation replicates to connected and late-join peers");
        Assert.That(Progress(serverNail), Is.EqualTo(0f));
        Assert.That(Availability(clientNail), Is.EqualTo("Available"));
        Assert.That(Availability(lateNail), Is.EqualTo("Available"));

        Assert.That(Begin(clientNail, clientExecutor, 0.01f), Is.Null,
            "A remote client cannot create an authoritative reservation when the nail is available.");
        Assert.That(Availability(serverNail), Is.EqualTo("Available"));
        Assert.That(Availability(clientNail), Is.Not.EqualTo("Reserved"));

        // Exercise owner-zero through a networked server-owned executor.
        NetworkObject npcExecutor = SpawnExecutor(NetworkManager.ServerClientId);
        object npcHandle = Begin(serverNail, npcExecutor.GetComponent<AudioSource>(), 0.2f);
        Assert.That(IsHandleValid(npcHandle), Is.True, "A spawned server-owned executor is a valid reservation owner.");
        Assert.That(ReadState(serverNail, "ExecutorOwnerClientId"), Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(Cancel(serverNail, npcExecutor.GetComponent<AudioSource>(), npcHandle), Is.True);
        Assert.That(Begin(clientNail, clientExecutor, 0.01f), Is.Null,
            "A connected remote client cannot mutate an available replicated nail.");
        object finalHandle = Begin(serverNail, npcExecutor.GetComponent<AudioSource>(), 0.05f);
        Assert.That(IsHandleValid(finalHandle), Is.True);
        yield return new WaitForSeconds(0.07f);
        Assert.That(Commit(serverNail, npcExecutor.GetComponent<AudioSource>(), finalHandle), Is.True,
            "The completed reservation is removed only after the caller invokes TryCommit.");
        yield return Until(() => !client.SpawnManager.SpawnedObjects.ContainsKey(nailId)
            && !lateJoin.SpawnManager.SpawnedObjects.ContainsKey(nailId), "authoritative commit despawn replicates to all peers");
#endif
    }

    [UnityTest]
    public IEnumerator RemoteExecutorDisconnectAndExecutorDespawnCancelReservations()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity networking components.");
        yield break;
#else
        previousSingleton = NetworkManager.Singleton;
        nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null);
        BuildStatePrefab();
        BuildNetworkPrefabs();
        ushort port = ReservePort();
        host = CreateManager("Stage 2 disconnect host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "host start");
        client = CreateManager("Stage 2 disconnect client", port);
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "remote client connect");
        ulong remoteClientId = client.LocalClientId;
        NetworkObject stateObject = SpawnState();
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(stateObject.NetworkObjectId), "scene-state replication");
        hostState = stateObject.GetComponent(stateType);
        clientState = client.SpawnManager.SpawnedObjects[stateObject.NetworkObjectId].GetComponent(stateType);
        GameObject hostTarget = CreateTarget("stage2-disconnect-target");
        GameObject clientTarget = CreateTarget("stage2-disconnect-target");
        Assert.That(Register(hostState, hostTarget), Is.True);
        Assert.That(Register(clientState, clientTarget), Is.True);
        NetworkObject executor = SpawnExecutor(client.LocalClientId);
        Component serverNail = SpawnNail(hostTarget.transform);
        ulong nailId = serverNail.GetComponent<NetworkObject>().NetworkObjectId;
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(nailId), "nail replica");
        Component clientNail = client.SpawnManager.SpawnedObjects[nailId]
            .GetComponent(System.Type.GetType("MountingNail, Assembly-CSharp"));
        yield return Until(() => clientNail.GetType().GetField("embeddedTarget", PrivateInstance).GetValue(clientNail)
            == clientTarget.transform && Availability(clientNail) == "Available", "disconnect test attachment resolves");
        object handle = Begin(serverNail, executor.GetComponent<AudioSource>(), 1f);
        Assert.That(IsHandleValid(handle), Is.True);
        client.Shutdown(true);
        yield return Until(() => !host.ConnectedClientsIds.Contains(remoteClientId), "remote executor disconnect");
        yield return Until(() => Availability(serverNail) == "Available", "disconnected executor cancels its reservation");

        // A server-owned executor gives this reservation a valid owner after the remote has disconnected.
        NetworkObject npcExecutor = SpawnExecutor(NetworkManager.ServerClientId);
        object despawnHandle = Begin(serverNail, npcExecutor.GetComponent<AudioSource>(), 0.5f);
        Assert.That(IsHandleValid(despawnHandle), Is.True);
        Assert.That(Cancel(serverNail, npcExecutor.GetComponent<AudioSource>(), handle), Is.False,
            "A stale handle cannot cancel the replacement reservation.");
        npcExecutor.Despawn(true);
        yield return Until(() => Availability(serverNail) == "Available", "despawned executor cancels its reservation");
        NetworkObject replacement = SpawnExecutor(NetworkManager.ServerClientId);
        object replacementHandle = Begin(serverNail, replacement.GetComponent<AudioSource>(), 0.5f);
        Assert.That(IsHandleValid(replacementHandle), Is.True);
        Assert.That(Commit(serverNail, replacement.GetComponent<AudioSource>(), handle), Is.False,
            "An old handle cannot commit a later executor attempt.");
        serverNail.GetComponent<NetworkObject>().Despawn(true);
#endif
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        foreach (NetworkManager manager in new[] { lateJoin, client, host })
            if (manager != null && manager.IsListening) manager.Shutdown(true);
        float deadline = Time.realtimeSinceStartup + 12f;
        while (Time.realtimeSinceStartup < deadline && AnyManagerStopping()) yield return null;
        bool stopped = !AnyManagerStopping();
        foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        if (previousSingleton != null) previousSingleton.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
        yield return null;
        Assert.That(stopped, Is.True, "NGO shutdown must finish before fixtures are destroyed.");
    }

    private bool AnyManagerStopping()
    {
        foreach (NetworkManager manager in new[] { lateJoin, client, host })
            if (manager != null && (manager.IsListening || manager.ShutdownInProgress)) return true;
        return false;
    }

#if UNITY_EDITOR
    private void BuildNetworkPrefabs()
    {
        executorPrefab = new GameObject("Stage 2 executor network prefab");
        roots.Add(executorPrefab);
        NetworkObject executorObject = executorPrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(executorObject, NailExecutorPrefabHash);
        executorPrefab.AddComponent<AudioSource>();
        executorObject.SetSceneObjectStatus(false);

        targetPrefab = new GameObject("Stage 2 target network prefab");
        roots.Add(targetPrefab);
        NetworkObject targetObject = targetPrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(targetObject, NailTargetPrefabHash);
        Rigidbody body = targetPrefab.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        targetPrefab.AddComponent<BoxCollider>();
        targetObject.SetSceneObjectStatus(false);
    }

    private void BuildStatePrefab()
    {
        stateType = Type.GetType("MountingNailSceneState, Assembly-CSharp");
        identityType = Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
        Assert.That(stateType, Is.Not.Null);
        Assert.That(identityType, Is.Not.Null);
        statePrefab = new GameObject("Stage 2 extraction scene-state prefab");
        roots.Add(statePrefab);
        NetworkObject networkObject = statePrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, StatePrefabHash);
        statePrefab.AddComponent(stateType);
        networkObject.SetSceneObjectStatus(false);
    }

    private NetworkObject SpawnState()
    {
        NetworkObject state = Object.Instantiate(statePrefab).GetComponent<NetworkObject>();
        roots.Add(state.gameObject);
        state.SetSceneObjectStatus(false);
        state.Spawn();
        return state;
    }

    private GameObject CreateTarget(string stableId)
    {
        GameObject target = new("Stage 2 peer-local target");
        roots.Add(target);
        Rigidbody body = target.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = false;
        target.AddComponent<BoxCollider>().enabled = false;
        Component identity = target.AddComponent(identityType);
        identityType.GetField("stableTargetId", PrivateInstance).SetValue(identity, stableId);
        return target;
    }

    private bool Register(Component state, GameObject target) => (bool)stateType.GetMethod("RegisterLocalTarget", PublicInstance)
        .Invoke(state, new[] { target.GetComponent(identityType) });

    private NetworkManager CreateManager(string name, ushort port)
    {
        GameObject root = new(name);
        roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = nailPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = executorPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = targetPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = statePrefab });
        return manager;
    }

    private NetworkObject SpawnExecutor(ulong ownerClientId)
    {
        GameObject instance = Object.Instantiate(executorPrefab);
        roots.Add(instance);
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        networkObject.SetSceneObjectStatus(false);
        networkObject.SpawnWithOwnership(ownerClientId);
        return networkObject;
    }

    private Component SpawnNail(Transform target)
    {
        GameObject instance = Object.Instantiate(nailPrefab);
        roots.Add(instance);
        Component nail = instance.GetComponent(System.Type.GetType("MountingNail, Assembly-CSharp"));
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        networkObject.SetSceneObjectStatus(false);
        networkObject.Spawn();
        nail.GetType().GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(nail,
            new object[] { target, null, target.position, Vector3.forward });
        nail.GetType().GetMethod("SetHitCounts", PrivateInstance).Invoke(nail, new object[] { 1, 3, 0.02f });
        nail.GetType().GetMethod("SetState", PrivateInstance).Invoke(nail,
            new[] { Enum.Parse(Type.GetType("MountingNailState, Assembly-CSharp"), "EmbeddedSingle") });
        return nail;
    }
#endif

    private static object Begin(Component nail, Behaviour executor, float duration)
    {
        object[] args = { executor, duration, null };
        bool accepted = (bool)nail.GetType().GetMethod("TryBeginCrowbarExtraction", PublicInstance).Invoke(nail, args);
        return accepted ? args[2] : null;
    }

    private static bool Cancel(Component nail, Behaviour executor, object handle) => (bool)nail.GetType()
        .GetMethod("CancelCrowbarExtraction", PublicInstance).Invoke(nail, new[] { executor, handle });
    private static bool Commit(Component nail, Behaviour executor, object handle) => (bool)nail.GetType()
        .GetMethod("TryCommitCrowbarExtraction", PublicInstance).Invoke(nail, new[] { executor, handle });
    private static bool IsHandleValid(object handle) => handle != null
        && (bool)handle.GetType().GetProperty("IsValid", PublicInstance).GetValue(handle);
    private static object ReadState(Component nail, string property) => nail.GetType()
        .GetProperty("CrowbarExtractionReadState", PublicInstance).PropertyType
        .GetProperty(property, PublicInstance).GetValue(nail.GetType()
            .GetProperty("CrowbarExtractionReadState", PublicInstance).GetValue(nail));
    private static string Availability(Component nail) => ReadState(nail, "Availability").ToString();
    private static float Progress(Component nail) => (float)nail.GetType()
        .GetProperty("CrowbarExtractionProgressNormalized", PublicInstance).GetValue(nail);
    private static ushort ReservePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        try { return (ushort)((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
    private static IEnumerator Until(Func<bool> condition, string label)
    {
        for (int i = 0; i < 300 && !condition(); i++) yield return null;
        Assert.That(condition(), Is.True, label);
    }
}
