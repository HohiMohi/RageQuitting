using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class MountingNailExtractionStage1NetworkPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const uint StatePrefabHash = 0x4E414536u;
    private const uint TargetPrefabHash = 0x4E414537u;
    private readonly List<GameObject> roots = new();
    private NetworkManager host;
    private NetworkManager client;
    private NetworkManager lateJoin;
    private NetworkManager previousSingleton;
    private GameObject nailPrefab;
    private GameObject statePrefab;
    private GameObject targetPrefab;
    private Type stateType;
    private Type identityType;
    private Component hostState;
    private Component clientState;
    private Component lateState;

    [UnityTest]
    public IEnumerator OnlyServerCanExtractAndDespawnReplicatesToClient()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses authored Unity networking components.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        previousSingleton = NetworkManager.Singleton;
        nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null);
        BuildStatePrefab();
        ushort port = ReservePort();
        host = CreateManager("Stage 1 extraction host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "host start");
        client = CreateManager("Stage 1 extraction client", port);
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "client connect");

        GameObject hostTarget = CreateTarget("Stage1-extraction-target");
        GameObject clientTarget = CreateTarget("Stage1-extraction-target");
        Rigidbody clientTargetBody = clientTarget.GetComponent<Rigidbody>();
        bool originalKinematic = clientTargetBody.isKinematic;
        bool originalGravity = clientTargetBody.useGravity;
        RigidbodyConstraints originalConstraints = clientTargetBody.constraints;
        NetworkObject stateObject = Object.Instantiate(statePrefab).GetComponent<NetworkObject>();
        roots.Add(stateObject.gameObject);
        stateObject.SetSceneObjectStatus(false);
        stateObject.Spawn();
        hostState = stateObject.GetComponent(stateType);
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(stateObject.NetworkObjectId), "scene-state replication");
        clientState = client.SpawnManager.SpawnedObjects[stateObject.NetworkObjectId].GetComponent(stateType);
        Assert.That(Register(hostState, hostTarget), Is.True);
        Assert.That(Register(clientState, clientTarget), Is.True);
        object targetReference = CreateReference(hostState, hostTarget);

        Component serverNail = CreateSpawnedNail();
        GameObject nailObject = serverNail.gameObject;
        System.Type type = serverNail.GetType();
        ConfigureNetworkEmbeddedNail(serverNail, hostTarget.transform);
        ulong id = nailObject.GetComponent<NetworkObject>().NetworkObjectId;
        Component survivingNail = CreateSpawnedNail();
        GameObject survivingNailObject = survivingNail.gameObject;
        ConfigureNetworkEmbeddedNail(survivingNail, hostTarget.transform);
        ulong survivingId = survivingNailObject.GetComponent<NetworkObject>().NetworkObjectId;
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(id), "nail replication");
        Component clientNail = client.SpawnManager.SpawnedObjects[id].GetComponent(type);
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(survivingId), "surviving nail replication");
        Component clientSurvivingNail = client.SpawnManager.SpawnedObjects[survivingId].GetComponent(type);
        yield return Until(() => State(clientNail) == "EmbeddedSingle"
            && type.GetField("embeddedTarget", PrivateInstance).GetValue(clientNail) == clientTarget.transform
            && State(clientSurvivingNail) == "EmbeddedSingle"
            && GetFollowCount(clientState, targetReference) == 2, "two resolved attachments and shared follow lock");
        Assert.That(IsTargetConnected(clientState, clientTarget.transform), Is.False,
            "EmbeddedSingle follow locks physics but do not add the pickup lock reserved for pair connections.");
        Assert.That(clientTargetBody.isKinematic, Is.True, "A followed target is frozen while attached.");
        Assert.That(clientNail.GetType().GetProperty("BlocksPickup", PublicInstance).GetValue(clientNail), Is.True);

        Assert.That(Extract(clientNail), Is.False, "A non-server peer cannot perform extraction.");
        Assert.That(Extract(serverNail), Is.True);
        Assert.That(Extract(serverNail), Is.False, "The despawn request is idempotently rejected.");
        yield return Until(() => !client.SpawnManager.SpawnedObjects.ContainsKey(id)
            && GetFollowCount(clientState, targetReference) == 1, "replicated despawn preserves the other nail's follow reference");

        lateJoin = CreateManager("Stage 1 extraction late join", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3, "late join connect");
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(stateObject.NetworkObjectId), "late join state snapshot");
        lateState = lateJoin.SpawnManager.SpawnedObjects[stateObject.NetworkObjectId].GetComponent(stateType);
        GameObject lateTarget = CreateTarget("Stage1-extraction-target");
        Assert.That(Register(lateState, lateTarget), Is.True);
        object lateReference = CreateReference(lateState, lateTarget);
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(survivingId)
            && State(lateJoin.SpawnManager.SpawnedObjects[survivingId].GetComponent(type)) == "EmbeddedSingle"
            && GetFollowCount(lateState, lateReference) == 1, "late join receives surviving attachment snapshot");

        Assert.That(Extract(survivingNail), Is.True, "The last extraction releases the remaining attachment.");
        yield return Until(() => !client.SpawnManager.SpawnedObjects.ContainsKey(survivingId)
            && !lateJoin.SpawnManager.SpawnedObjects.ContainsKey(survivingId)
            && GetFollowCount(clientState, targetReference) == 0
            && GetFollowCount(lateState, lateReference) == 0, "last removal replicates and releases current and late-join locks");
        yield return Until(() => !clientTargetBody.isKinematic
            && clientTargetBody.useGravity == originalGravity
            && clientTargetBody.constraints == originalConstraints, "last follow restores client target physics");
        Rigidbody lateTargetBody = lateTarget.GetComponent<Rigidbody>();
        yield return Until(() => !lateTargetBody.isKinematic && !IsTargetConnected(lateState, lateTarget.transform),
            "late join releases target lock and restores dynamic physics");
        Assert.That(clientTargetBody.isKinematic, Is.EqualTo(originalKinematic));
        Assert.That(lateTargetBody.useGravity, Is.EqualTo(originalGravity));
        Assert.That(lateTargetBody.constraints, Is.EqualTo(originalConstraints));
        Assert.That(IsTargetConnected(clientState, clientTarget.transform), Is.False,
            "After both follows are released, no pair pickup lock remains on the target.");
#endif
    }

    [UnityTest]
    public IEnumerator RespawnedTargetDoesNotReplaceCapturedAttachmentIdentity()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity networking components.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        previousSingleton = NetworkManager.Singleton;
        nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null);
        BuildStatePrefab();
        BuildTargetPrefab();
        ushort port = ReservePort();
        host = CreateManager("Stage 1 target identity host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "host start");
        client = CreateManager("Stage 1 target identity client", port);
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "client connect");

        NetworkObject stateObject = Object.Instantiate(statePrefab).GetComponent<NetworkObject>();
        roots.Add(stateObject.gameObject);
        stateObject.SetSceneObjectStatus(false);
        stateObject.Spawn();
        hostState = stateObject.GetComponent(stateType);
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(stateObject.NetworkObjectId), "state mirror");
        clientState = client.SpawnManager.SpawnedObjects[stateObject.NetworkObjectId].GetComponent(stateType);

        NetworkObject originalTarget = SpawnNetworkTarget();
        ulong targetId = originalTarget.NetworkObjectId;
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(targetId), "original target mirror");
        NetworkObject replacement = SpawnNetworkTarget();
        ulong replacementId = replacement.NetworkObjectId;
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(replacementId), "replacement target mirror");

        Component serverNail = CreateSpawnedNail();
        GameObject nailObject = serverNail.gameObject;
        ConfigureNetworkEmbeddedNail(serverNail, originalTarget.transform);
        ulong nailId = nailObject.GetComponent<NetworkObject>().NetworkObjectId;
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(nailId), "nail mirror");
        Component clientNail = client.SpawnManager.SpawnedObjects[nailId]
            .GetComponent(System.Type.GetType("MountingNail, Assembly-CSharp"));
        yield return Until(() => State(clientNail) == "EmbeddedSingle"
            && (Transform)clientNail.GetType().GetField("embeddedTarget", PrivateInstance).GetValue(clientNail)
                == client.SpawnManager.SpawnedObjects[targetId].transform, "captured original attachment resolves");

        originalTarget.Despawn(true);
        Assert.That(Extract(serverNail), Is.False,
            "A replacement with the same prefab identity cannot satisfy the despawned attachment's captured spawn id.");
        Assert.That(replacement.IsSpawned, Is.True);
        yield return Until(() => !client.SpawnManager.SpawnedObjects.ContainsKey(targetId), "original target despawn");
#endif
    }

    [UnityTest]
    public IEnumerator ExtractionRestoresOriginalNetworkTargetOwnerOnlyAfterFinalConnectedEdge()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity networking components.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        previousSingleton = NetworkManager.Singleton;
        nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null);
        BuildStatePrefab();
        BuildTargetPrefab();
        ushort port = ReservePort();
        host = CreateManager("Stage 1 extraction ownership host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "ownership host start");
        client = CreateManager("Stage 1 extraction ownership client", port);
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "ownership client connect");

        NetworkObject stateObject = Object.Instantiate(statePrefab).GetComponent<NetworkObject>();
        roots.Add(stateObject.gameObject);
        stateObject.SetSceneObjectStatus(false);
        stateObject.Spawn();
        hostState = stateObject.GetComponent(stateType);
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(stateObject.NetworkObjectId), "ownership state mirror");

        NetworkObject sharedTarget = SpawnNetworkTarget();
        NetworkObject secondTarget = SpawnNetworkTarget();
        NetworkObject thirdTarget = SpawnNetworkTarget();
        sharedTarget.ChangeOwnership(client.LocalClientId);
        yield return Until(() => sharedTarget.OwnerClientId == client.LocalClientId, "original target owner established");

        Component firstEdge = CreatePairNail(sharedTarget.transform, secondTarget.transform);
        Component secondEdge = CreatePairNail(sharedTarget.transform, thirdTarget.transform);
        yield return Until(() => sharedTarget.OwnerClientId == NetworkManager.ServerClientId, "connection takes server ownership");
        Assert.That(IsTargetConnected(hostState, sharedTarget.transform), Is.True,
            "A target with connected edges remains protected from pickup.");
        Assert.That(firstEdge.GetType().GetProperty("BlocksPickup", PublicInstance).GetValue(firstEdge), Is.True);
        Assert.That(Extract(firstEdge), Is.True);
        Assert.That(sharedTarget.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
            "Removing one connected edge must preserve server ownership while the other edge still holds a lock.");
        Assert.That(IsTargetConnected(hostState, sharedTarget.transform), Is.True);
        Assert.That(Extract(secondEdge), Is.True);
        yield return Until(() => sharedTarget.OwnerClientId == client.LocalClientId, "final edge restores original target owner");
        Assert.That(IsTargetConnected(hostState, sharedTarget.transform), Is.False,
            "After final extraction the connected target is unlocked and available to pickup.");
#endif
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        foreach (NetworkManager manager in new[] { lateJoin, client, host })
        {
            if (manager == null || !manager.IsListening) continue;
            manager.Shutdown(true);
        }
        float deadline = Time.realtimeSinceStartup + 12f;
        while (Time.realtimeSinceStartup < deadline && AnyManagerStopping()) yield return null;
        bool stopped = !AnyManagerStopping();
        foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        if (previousSingleton != null) previousSingleton.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
        yield return null;
        Assert.That(stopped, Is.True);
    }

    private bool AnyManagerStopping()
    {
        foreach (NetworkManager manager in new[] { lateJoin, client, host })
            if (manager != null && (manager.IsListening || manager.ShutdownInProgress)) return true;
        return false;
    }

#if UNITY_EDITOR
    private NetworkManager CreateManager(string name, ushort port)
    {
        GameObject root = new(name);
        roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = nailPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = statePrefab });
        if (targetPrefab != null) manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = targetPrefab });
        return manager;
    }

    private void BuildStatePrefab()
    {
        stateType = Type.GetType("MountingNailSceneState, Assembly-CSharp");
        identityType = Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
        statePrefab = new GameObject("Stage 1 extraction scene-state prefab");
        roots.Add(statePrefab);
        NetworkObject networkObject = statePrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, StatePrefabHash);
        statePrefab.AddComponent(stateType);
        networkObject.SetSceneObjectStatus(false);
    }

    private void BuildTargetPrefab()
    {
        targetPrefab = new GameObject("Stage 1 captured target prefab");
        roots.Add(targetPrefab);
        NetworkObject networkObject = targetPrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, TargetPrefabHash);
        Rigidbody body = targetPrefab.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        targetPrefab.AddComponent<BoxCollider>();
        networkObject.SetSceneObjectStatus(false);
    }

    private NetworkObject SpawnNetworkTarget()
    {
        GameObject instance = Object.Instantiate(targetPrefab);
        roots.Add(instance);
        NetworkObject target = instance.GetComponent<NetworkObject>();
        target.SetSceneObjectStatus(false);
        target.Spawn();
        return target;
    }

    private Component CreatePairNail(Transform first, Transform second)
    {
        Component nail = CreateSpawnedNail();
        System.Type type = nail.GetType();
        type.GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(nail,
            new object[] { first, second, first.position, Vector3.forward });
        type.GetMethod("SetHitCounts", PrivateInstance).Invoke(nail, new object[] { 1, 1, 0.02f });
        type.GetMethod("SetState", PrivateInstance).Invoke(nail,
            new[] { System.Enum.Parse(System.Type.GetType("MountingNailState, Assembly-CSharp"), "EmbeddedPair") });
        Assert.That((bool)type.GetMethod("TryRegisterPairConnection", PrivateInstance).Invoke(nail, null), Is.True);
        return nail;
    }

    private GameObject CreateTarget(string stableId)
    {
        GameObject target = new("Stage 1 extraction peer-local target");
        roots.Add(target);
        Rigidbody body = target.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = false;
        body.constraints = RigidbodyConstraints.FreezeRotationX;
        target.AddComponent<BoxCollider>().enabled = false;
        Component identity = target.AddComponent(identityType);
        identityType.GetField("stableTargetId", PrivateInstance).SetValue(identity, stableId);
        return target;
    }

    private bool Register(Component state, GameObject target) => (bool)stateType.GetMethod("RegisterLocalTarget", PublicInstance)
        .Invoke(state, new[] { target.GetComponent(identityType) });

    private static object CreateReference(Component state, GameObject target)
    {
        object[] args = { target.transform, null };
        Assert.That((bool)state.GetType().GetMethod("TryCreateReference", PublicInstance).Invoke(state, args), Is.True);
        return args[1];
    }

    private static int GetFollowCount(Component state, object reference)
    {
        object states = state.GetType().GetField("targetStates", PrivateInstance).GetValue(state);
        Type listType = states.GetType();
        int count = (int)listType.GetProperty("Count", PublicInstance).GetValue(states);
        PropertyInfo item = listType.GetProperty("Item", PublicInstance);
        for (int i = 0; i < count; i++)
        {
            object record = item.GetValue(states, new object[] { i });
            if (record.GetType().GetField("Target").GetValue(record).Equals(reference))
                return (int)record.GetType().GetField("FollowCount").GetValue(record);
        }
        return 0;
    }

    private static bool IsTargetConnected(Component state, Transform target) =>
        (bool)state.GetType().GetMethod("IsTargetConnected", BindingFlags.Static | PublicInstance)
            .Invoke(null, new object[] { target });

    private static string State(Component nail) => nail.GetType().GetProperty("State", PublicInstance).GetValue(nail).ToString();

    private Component CreateSpawnedNail()
    {
        GameObject instance = Object.Instantiate(nailPrefab);
        roots.Add(instance);
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        networkObject.SetSceneObjectStatus(false);
        networkObject.Spawn();
        return instance.GetComponent(System.Type.GetType("MountingNail, Assembly-CSharp"));
    }

    private static void ConfigureNetworkEmbeddedNail(Component nail, Transform target)
    {
        System.Type type = nail.GetType();
        type.GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(nail,
            new object[] { target, null, target.position, Vector3.forward });
        type.GetMethod("SetHitCounts", PrivateInstance).Invoke(nail, new object[] { 1, 1, 0.02f });
        type.GetMethod("SetState", PrivateInstance).Invoke(nail,
            new[] { System.Enum.Parse(System.Type.GetType("MountingNailState, Assembly-CSharp"), "EmbeddedSingle") });
    }

    private static bool Extract(Component nail) => (bool)nail.GetType()
        .GetMethod("TryExtractForCrowbar", PrivateInstance).Invoke(nail, null);

    private static ushort ReservePort()
    {
        TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try { return (ushort)((System.Net.IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static IEnumerator Until(System.Func<bool> condition, string description)
    {
        for (int i = 0; i < 300 && !condition(); i++) yield return null;
        Assert.That(condition(), Is.True, description);
    }
#endif
}
