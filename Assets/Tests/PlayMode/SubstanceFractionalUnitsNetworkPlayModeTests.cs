using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Type = System.Type;

public sealed class SubstanceFractionalUnitsNetworkPlayModeTests
{
    private readonly List<GameObject> roots = new();
    private readonly List<NetworkManager> managers = new();
    private NetworkManager previous, host, owner, late;
    private GameObject bucketPrefab, pilePrefab;
    private Object soil;
    private Type bucketType, pileType, substanceType;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        bucketType = Type.GetType("PortableSubstanceContainer, Assembly-CSharp");
        pileType = Type.GetType("LooseSubstancePile, Assembly-CSharp");
        substanceType = Type.GetType("ContainerSubstanceSO, Assembly-CSharp");
        Assert.That(new[] { bucketType, pileType, substanceType }, Has.None.Null);
#if UNITY_EDITOR
        bucketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/PortableBucket.prefab");
        pilePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/EarthPile.prefab");
        soil = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
#endif
        Assert.That(bucketPrefab, Is.Not.Null); Assert.That(pilePrefab, Is.Not.Null); Assert.That(soil, Is.Not.Null);
        previous = NetworkManager.Singleton;
        ushort port = ReservePort();
        host = Manager("Fractional host", port);
        Assert.That(host.StartHost(), Is.True); host.SetSingleton();
        yield return Until(() => host.IsListening, "host start");
        owner = Manager("Fractional owner", port);
        Assert.That(owner.StartClient(), Is.True); owner.SetSingleton();
        yield return Until(() => owner.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "owner connect");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (NetworkManager manager in managers) if (manager != null && manager.IsListening) manager.Shutdown(true);
        float deadline = Time.realtimeSinceStartup + 12f;
        while (Time.realtimeSinceStartup < deadline && managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress)))
            yield return null;
        foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        roots.Clear(); managers.Clear();
        if (previous != null && previous.IsListening) previous.SetSingleton();
        yield return null;
    }

    [UnityTest]
    public IEnumerator HostBucketAndPileFractionsReplicateAndClientsCannotMutateIncludingLateJoin()
    {
        host.SetSingleton();
        NetworkObject bucketObject = Spawn(bucketPrefab, new Vector3(50, 0, 0), owner.LocalClientId);
        ulong bucketId = bucketObject.NetworkObjectId;
        yield return Until(() => owner.SpawnManager.SpawnedObjects.ContainsKey(bucketId), "bucket replica");
        Component serverBucket = bucketObject.GetComponent(bucketType);
        Component ownerBucket = owner.SpawnManager.SpawnedObjects[bucketId].GetComponent(bucketType);
        Assert.That(Add(serverBucket, soil, .25f), Is.EqualTo(.25f).Within(.00001f));
        Assert.That(Add(serverBucket, soil, .5f), Is.EqualTo(.5f).Within(.00001f));
        yield return Until(() => Units(ownerBucket) > .749f, "bucket fractional replication");
        Assert.That(Units(ownerBucket), Is.EqualTo(.75f).Within(.00001f));
        owner.SetSingleton();
        Assert.That(Add(ownerBucket, soil, .25f), Is.Zero);
        Assert.That(Units(ownerBucket), Is.EqualTo(.75f).Within(.00001f));

        host.SetSingleton();
        GameObject pileInstance = Object.Instantiate(pilePrefab, new Vector3(51, 0, 0), Quaternion.identity);
        roots.Add(pileInstance);
        NetworkObject pileObject = pileInstance.GetComponent<NetworkObject>();
        pileObject.SetSceneObjectStatus(false);
        Component serverPile = pileObject.GetComponent(pileType);
        pileType.GetMethod("Initialize").Invoke(serverPile, new object[] { soil, .25f });
        pileObject.Spawn();
        ulong pileId = pileObject.NetworkObjectId;
        yield return Until(() => owner.SpawnManager.SpawnedObjects.ContainsKey(pileId), "pile replica");
        Component ownerPile = owner.SpawnManager.SpawnedObjects[pileId].GetComponent(pileType);
        yield return Until(() => Units(ownerPile) > .249f, "pile fraction replication");
        Assert.That(Units(ownerPile), Is.EqualTo(.25f).Within(.00001f));
        owner.SetSingleton();
        Assert.That((float)pileType.GetMethod("TryExtract").Invoke(ownerPile, new object[] { soil, .25f }), Is.Zero);
        Assert.That(Units(ownerPile), Is.EqualTo(.25f).Within(.00001f));

        host.SetSingleton(); late = Manager("Fractional late join", host.GetComponent<UnityTransport>().ConnectionData.Port);
        Assert.That(late.StartClient(), Is.True); late.SetSingleton();
        yield return Until(() => late.IsConnectedClient && host.ConnectedClientsIds.Count == 3, "late join connect");
        yield return Until(() => late.SpawnManager.SpawnedObjects.ContainsKey(bucketId) && late.SpawnManager.SpawnedObjects.ContainsKey(pileId), "late join objects");
        Assert.That(Units(late.SpawnManager.SpawnedObjects[bucketId].GetComponent(bucketType)), Is.EqualTo(.75f).Within(.00001f));
        Assert.That(Units(late.SpawnManager.SpawnedObjects[pileId].GetComponent(pileType)), Is.EqualTo(.25f).Within(.00001f));
    }

    private NetworkObject Spawn(GameObject prefab, Vector3 position, ulong? ownerId = null)
    {
        GameObject instance = Object.Instantiate(prefab, position, Quaternion.identity); roots.Add(instance);
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        networkObject.SetSceneObjectStatus(false);
        if (ownerId.HasValue) networkObject.SpawnWithOwnership(ownerId.Value); else networkObject.Spawn();
        return networkObject;
    }
    private NetworkManager Manager(string name, ushort port)
    {
        GameObject root = new(name); roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>(); transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = bucketPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = pilePrefab });
        managers.Add(manager); return manager;
    }
    private static float Add(Component c, Object s, float n) => (float)c.GetType().GetMethod("TryAddUnits", new[] { Type.GetType("ContainerSubstanceSO, Assembly-CSharp"), typeof(float) }).Invoke(c, new object[] { s, n });
    private static float Units(Component c) => (float)c.GetType().GetProperty("CurrentUnits").GetValue(c);
    private static ushort ReservePort() { using UdpClient socket = new UdpClient(0); return (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port; }
    private static IEnumerator Until(System.Func<bool> condition, string label)
    { float deadline = Time.realtimeSinceStartup + 20f; while (!condition() && Time.realtimeSinceStartup < deadline) yield return null; Assert.That(condition(), Is.True, "Timed out: " + label); }
}
