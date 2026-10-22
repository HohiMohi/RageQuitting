using System;
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

public class CrowbarStage3NetworkPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private const uint TestActorHash = 0x43524231u;
    private const uint TestTargetHash = 0x43524232u;
    private readonly List<GameObject> roots = new();
    private readonly List<NetworkManager> managers = new();
    private NetworkManager previousSingleton;
    private NetworkManager host;
    private NetworkManager client;
    private GameObject actorPrefab;
    private GameObject targetPrefab;
    private GameObject crowbarPrefab;
    private ScriptableObject crowbarAsset;
    private GameObject testCamera;

    [SetUp]
    public void SetUpTestCamera()
    {
        testCamera = new GameObject("Crowbar Stage3 network test camera");
        testCamera.tag = "MainCamera";
        testCamera.AddComponent<Camera>();
    }

    [UnityTest]
    public IEnumerator ActualCrowbarPrefabReplicatesAndClientActionRpcDealsEightWithoutImpulse()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity assets and in-process NGO peers.");
        yield break;
#else
        previousSingleton = NetworkManager.Singleton;
        crowbarAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/Crowbar.asset");
        Assert.That(crowbarAsset, Is.Not.Null);
        crowbarPrefab = (GameObject)crowbarAsset.GetType().GetField("equippableItemPrefab").GetValue(crowbarAsset);
        BuildRuntimePrefabs();
        ushort port = ReservePort();
        host = CreateManager("Crowbar Stage3 host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "host startup");
        client = CreateManager("Crowbar Stage3 client", port);
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "client connection");

        NetworkObject serverTarget = Spawn(targetPrefab, new Vector3(200f, 10f, 200f));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(serverTarget.NetworkObjectId), "target replica");
        GameObject clientTargetObject = client.SpawnManager.SpawnedObjects[serverTarget.NetworkObjectId].gameObject;
        Collider[] serverTargetColliders = serverTarget.GetComponentsInChildren<Collider>(true);
        Collider[] clientTargetColliders = clientTargetObject.GetComponentsInChildren<Collider>(true);
        foreach (Collider serverCollider in serverTargetColliders)
        foreach (Collider clientCollider in clientTargetColliders)
            if (serverCollider != null && clientCollider != null)
                Physics.IgnoreCollision(serverCollider, clientCollider, true);
        Component serverHealth = serverTarget.GetComponent(Type.GetType("PlayerHealth, Assembly-CSharp"));
        Component clientHealth = clientTargetObject.GetComponent(Type.GetType("PlayerHealth, Assembly-CSharp"));
        Assert.That(serverHealth, Is.Not.Null);
        Assert.That(clientHealth, Is.Not.Null);
        Rigidbody targetBody = serverTarget.GetComponent<Rigidbody>();
        Component serverImpulse = serverTarget.GetComponent(Type.GetType("PlayerExternalImpulseController, Assembly-CSharp"));
        Component clientImpulse = clientTargetObject.GetComponent(Type.GetType("PlayerExternalImpulseController, Assembly-CSharp"));
        targetBody.useGravity = false;
        targetBody.isKinematic = false;
        targetBody.linearVelocity = Vector3.zero;

        NetworkObject serverActor = SpawnAsPlayer(actorPrefab, client.LocalClientId, new Vector3(200f, 10f, 199.1f));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(serverActor.NetworkObjectId), "owner player replica");
        GameObject clientActorObject = client.SpawnManager.SpawnedObjects[serverActor.NetworkObjectId].gameObject;
        Component clientInventory = clientActorObject.GetComponent(Type.GetType("PlayerInventory, Assembly-CSharp"));
        Component serverInventory = serverActor.GetComponent(Type.GetType("PlayerInventory, Assembly-CSharp"));
        Assert.That(clientInventory.GetType().GetMethod("AddItem").Invoke(clientInventory, new object[] { crowbarAsset }), Is.EqualTo(true));
        yield return Until(() => (int)serverInventory.GetType().GetMethod("GetNetworkSlotItemTypeValue").Invoke(serverInventory, new object[] { 0 }) == 11,
            "Crowbar inventory slot replicates to the authoritative server");
        Assert.That(serverInventory.GetType().GetMethod("GetSelectedItemForServerValidation").Invoke(serverInventory, null), Is.SameAs(crowbarAsset));

        NetworkObject serverCrowbar = Spawn(crowbarPrefab, new Vector3(203f, 10f, 200f));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(serverCrowbar.NetworkObjectId), "actual Crowbar prefab replication");
        GameObject clientCrowbarObject = client.SpawnManager.SpawnedObjects[serverCrowbar.NetworkObjectId].gameObject;
        Component clientWorldItem = clientCrowbarObject.GetComponent(Type.GetType("EquippableItem, Assembly-CSharp"));
        Assert.That(clientWorldItem.GetType().GetMethod("GetEquippableItemSO").Invoke(clientWorldItem, null), Is.SameAs(crowbarAsset));
        serverCrowbar.Despawn(true);

        Component clientInteraction = clientActorObject.GetComponent(Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        clientInteraction.GetType().GetField("_currentTarget", PrivateInstance).SetValue(clientInteraction, clientHealth);
        Component clientAction = clientActorObject.GetComponent(Type.GetType("PlayerActionController, Assembly-CSharp"));
        Rigidbody clientTargetBody = clientTargetObject.GetComponent<Rigidbody>();
        Assert.That(clientAction.GetType().GetMethod("PerformAction", PublicInstance).Invoke(clientAction, null), Is.EqualTo(true),
            "The owner invokes the normal PlayerActionController path; its combat impact uses the real ServerRpc.");
        yield return Until(() => Math.Abs(ReadFloat(serverHealth, "CurrentHealth") - 92f) < 0.01f, "server-authoritative eight damage");
        yield return Until(() => Math.Abs(ReadFloat(clientHealth, "CurrentHealth") - 92f) < 0.01f, "Crowbar damage replication to client");
        Assert.That(ReadFloat(clientHealth, "CurrentHealth"), Is.EqualTo(92f).Within(0.01f), "Health damage replicates to the client.");
        Assert.That(serverImpulse.GetType().GetProperty("IsImpulseActive").GetValue(serverImpulse), Is.EqualTo(false),
            "The actual target impulse receiver remains inactive after Crowbar's ServerRpc impact.");
        Assert.That((Vector3)serverImpulse.GetType().GetProperty("CurrentExternalVelocity").GetValue(serverImpulse), Is.EqualTo(Vector3.zero));
        Assert.That(clientImpulse.GetType().GetProperty("IsImpulseActive").GetValue(clientImpulse), Is.EqualTo(false));

        ScriptableObject hammerAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        NetworkObject hostActor = SpawnAsPlayer(actorPrefab, NetworkManager.ServerClientId, new Vector3(200f, 10f, 199.1f));
        yield return null;
        Component hostInventory = hostActor.GetComponent(Type.GetType("PlayerInventory, Assembly-CSharp"));
        Assert.That(hostInventory.GetType().GetMethod("AddItem").Invoke(hostInventory, new object[] { crowbarAsset }), Is.EqualTo(true));
        Component hostInteraction = hostActor.GetComponent(Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        hostInteraction.GetType().GetField("_currentTarget", PrivateInstance).SetValue(hostInteraction, serverHealth);
        Component hostAction = hostActor.GetComponent(Type.GetType("PlayerActionController, Assembly-CSharp"));
        Assert.That(hostAction.GetType().GetMethod("PerformAction", PublicInstance).Invoke(hostAction, null), Is.EqualTo(true),
            "The host uses the same real action path to land an actual Crowbar hit.");
        yield return Until(() => Math.Abs(ReadFloat(serverHealth, "CurrentHealth") - 84f) < 0.01f, "host Crowbar attack damage");
        yield return Until(() => Math.Abs(ReadFloat(clientHealth, "CurrentHealth") - 84f) < 0.01f, "host Crowbar damage replication");
        Assert.That(serverImpulse.GetType().GetProperty("IsImpulseActive").GetValue(serverImpulse), Is.EqualTo(false),
            "Host Crowbar hit also applies no impulse.");
        Assert.That(hostInventory.GetType().GetMethod("RemoveItem", PrivateInstance).Invoke(hostInventory, null), Is.EqualTo(true),
            "The fixture frees both occupied slots through the real inventory removal path.");
        yield return null;
        ulong droppedCrowbarId = ulong.MaxValue;
        foreach (NetworkObject candidate in host.SpawnManager.SpawnedObjectsList)
        {
            Component worldItem = candidate.GetComponent(Type.GetType("EquippableItem, Assembly-CSharp"));
            if (worldItem != null && worldItem.GetType().GetMethod("GetEquippableItemSO").Invoke(worldItem, null) == crowbarAsset)
            {
                droppedCrowbarId = candidate.NetworkObjectId;
                candidate.Despawn(true);
                break;
            }
        }
        Assert.That(droppedCrowbarId, Is.Not.EqualTo(ulong.MaxValue), "The normal removal path must spawn a networked Crowbar for server cleanup.");
        yield return Until(() => !client.SpawnManager.SpawnedObjects.ContainsKey(droppedCrowbarId), "dropped Crowbar replica despawn");
        yield return new WaitForSeconds(1.05f);
        Assert.That(hostInventory.GetType().GetMethod("AddItem").Invoke(hostInventory, new object[] { hammerAsset }), Is.EqualTo(true));
        hostInteraction.GetType().GetField("_currentTarget", PrivateInstance).SetValue(hostInteraction, serverHealth);
        Assert.That(hostAction.GetType().GetMethod("PerformAction", PublicInstance).Invoke(hostAction, null), Is.EqualTo(true),
            "The host then lands an IndustrialHammer positive-control hit on the same real receiver.");
        yield return Until(() => Math.Abs(ReadFloat(serverHealth, "CurrentHealth") - 76f) < 0.01f, "host hammer attack damage");
        yield return Until(() => Math.Abs(ReadFloat(clientHealth, "CurrentHealth") - 76f) < 0.01f, "host hammer damage replication");
        yield return Until(() => (bool)serverImpulse.GetType().GetProperty("IsImpulseActive").GetValue(serverImpulse), "positive-control hammer impulse");
        Assert.That((Vector3)serverImpulse.GetType().GetProperty("CurrentExternalVelocity").GetValue(serverImpulse), Is.Not.EqualTo(Vector3.zero));
#endif
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (NetworkManager manager in managers)
            if (manager != null && manager.IsListening) manager.Shutdown(true);
        float deadline = Time.realtimeSinceStartup + 12f;
        while (Time.realtimeSinceStartup < deadline && managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress)))
            yield return null;
        bool stopped = !managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress));
        foreach (GameObject go in roots) if (go != null) Object.Destroy(go);
        yield return null;
        foreach (NetworkManager manager in managers) if (manager != null && manager.IsListening) manager.Shutdown(true);
        foreach (GameObject go in roots) if (go != null) Object.Destroy(go);
        if (testCamera != null) Object.Destroy(testCamera);
        if (previousSingleton != null) previousSingleton.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", StaticPrivate)?.Invoke(null, null);
        roots.Clear(); managers.Clear();
        Assert.That(stopped, Is.True, "NGO must finish shutdown before temporary network objects are destroyed.");
    }

#if UNITY_EDITOR
    private void BuildRuntimePrefabs()
    {
        actorPrefab = new GameObject("Crowbar Stage3 RPC actor prefab"); roots.Add(actorPrefab);
        NetworkObject actorNetworkObject = actorPrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(actorNetworkObject, TestActorHash);
        actorNetworkObject.SetSceneObjectStatus(false);
        actorPrefab.AddComponent(Type.GetType("PlayerInputNew, Assembly-CSharp"));
        Component inventory = actorPrefab.AddComponent(Type.GetType("PlayerInventory, Assembly-CSharp"));
        UnityEditor.SerializedObject inventorySerialized = new UnityEditor.SerializedObject(inventory);
        ScriptableObject hammerAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        var catalog = inventorySerialized.FindProperty("equippableItemCatalog"); catalog.arraySize = 2;
        catalog.GetArrayElementAtIndex(0).objectReferenceValue = crowbarAsset;
        catalog.GetArrayElementAtIndex(1).objectReferenceValue = hammerAsset;
        inventorySerialized.ApplyModifiedPropertiesWithoutUndo();
        Component interaction = actorPrefab.AddComponent(Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        Component action = actorPrefab.AddComponent(Type.GetType("PlayerActionController, Assembly-CSharp"));
        Transform actionHolder = new GameObject("ActionTransformHolder").transform; actionHolder.SetParent(actorPrefab.transform, false); actionHolder.localPosition = new Vector3(0f, 0.5f, 0.8f);
        action.GetType().GetField("actionTransformHolder", PrivateInstance).SetValue(action, actionHolder);

        targetPrefab = new GameObject("Crowbar Stage3 health target prefab"); roots.Add(targetPrefab);
        NetworkObject targetNetworkObject = targetPrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(targetNetworkObject, TestTargetHash);
        targetNetworkObject.SetSceneObjectStatus(false);
        targetPrefab.AddComponent(Type.GetType("PlayerHealth, Assembly-CSharp"));
        targetPrefab.AddComponent(Type.GetType("PlayerInputNew, Assembly-CSharp"));
        targetPrefab.AddComponent(Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        targetPrefab.AddComponent(Type.GetType("PlayerExternalImpulseController, Assembly-CSharp"));
        Rigidbody body = targetPrefab.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = false;
        BoxCollider collider = targetPrefab.AddComponent<BoxCollider>(); collider.size = new Vector3(0.5f, 1.6f, 0.5f);
    }

    private NetworkManager CreateManager(string name, ushort port)
    {
        GameObject root = new GameObject(name); roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>(); transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = actorPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = targetPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = crowbarPrefab });
        managers.Add(manager); return manager;
    }

    private NetworkObject Spawn(GameObject prefab, Vector3 position)
    {
        NetworkObject instance = Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<NetworkObject>();
        roots.Add(instance.gameObject); instance.SetSceneObjectStatus(false); instance.Spawn(); return instance;
    }

    private NetworkObject SpawnAsPlayer(GameObject prefab, ulong owner, Vector3 position)
    {
        NetworkObject instance = Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<NetworkObject>();
        roots.Add(instance.gameObject); instance.SetSceneObjectStatus(false); instance.SpawnAsPlayerObject(owner); return instance;
    }
#endif

    private static float ReadFloat(Component component, string propertyName)
    {
        return Convert.ToSingle(component.GetType().GetProperty(propertyName, PublicInstance).GetValue(component));
    }

    private static ushort ReservePort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); ushort port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    private static IEnumerator Until(Func<bool> condition, string description)
    {
        float deadline = Time.realtimeSinceStartup + 12f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, $"Timed out waiting for {description}.");
    }
}

