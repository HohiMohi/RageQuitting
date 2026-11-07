using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class ClientNetworkTransformAuthoritativePosePlayModeTests
{
    private static readonly System.Type ClientTransformType = System.Type.GetType("ClientNetworkTransform, Assembly-CSharp");
    private NetworkManager host;
    private NetworkManager client;
    private NetworkManager clientB;
    private GameObject prefab;
    private GameObject prefabHolder;
    private GameObject hostRoot;
    private GameObject clientRoot;
    private GameObject clientBRoot;
    private string checkpointPath;

    [UnityTest]
    public IEnumerator ReceivedPoseTracksLatestCompleteOwnerPoseWhileRenderInterpolates()
    {
        checkpointPath = Path.GetFullPath(Path.Combine(Application.dataPath,
            $"../Artifacts/SingleCarrySyncFix_20261105/pose-test-{System.DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.log"));
        Directory.CreateDirectory(Path.GetDirectoryName(checkpointPath));
        Checkpoint("start");
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True,
            "The isolated fixture requires no other active NGO session.");
        prefabHolder = new GameObject("Authoritative pose prefab holder");
        prefabHolder.SetActive(false);
        prefab = new GameObject("Authoritative pose test player prefab");
        prefab.transform.SetParent(prefabHolder.transform);
        prefab.AddComponent<NetworkObject>();
        SetPrefabHash(prefab.GetComponent<NetworkObject>(), 0x77501001);
        prefab.GetComponent<NetworkObject>().AllowOwnerToParent = true;
        Assert.That(ClientTransformType, Is.Not.Null);
        Component unspawnedTransform = prefab.AddComponent(ClientTransformType);
        prefab.GetComponent<NetworkTransform>().Interpolate = true;
        prefab.GetComponent<NetworkTransform>().SwitchTransformSpaceWhenParented = true;
        Assert.That(TryPose(unspawnedTransform, out _, out _), Is.False,
            "An unspawned non-authority instance has no received sample and must not fall back to its local transform.");

        ushort port = ReservePort();
        host = CreateManager("Authoritative pose test host", port);
        Assert.That(host.StartHost(), Is.True);
        Checkpoint("host-started");
        yield return Until(() => host.LocalClient?.PlayerObject != null, "host player spawn");
        client = CreateManager("Authoritative pose test client", port);
        Assert.That(client.StartClient(), Is.True);
        Checkpoint("client-a-started");
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2
            && client.LocalClient?.PlayerObject != null, "remote client player spawn");
        clientB = CreateManager("Authoritative pose test client B", port);
        Assert.That(clientB.StartClient(), Is.True);
        Checkpoint("client-b-started");
        yield return Until(() => clientB.IsConnectedClient && host.ConnectedClientsIds.Count == 3
            && clientB.LocalClient?.PlayerObject != null, "second remote client player spawn");

        NetworkObject ownerPlayer = client.LocalClient.PlayerObject;
        NetworkObject hostReplica = host.SpawnManager.SpawnedObjectsList.FirstOrDefault(item =>
            item.OwnerClientId == client.LocalClientId && item.IsPlayerObject);
        Assert.That(hostReplica, Is.Not.Null, "The host must have the remote owner's replica.");
        Component ownerTransform = ownerPlayer.GetComponent(ClientTransformType);
        Component remoteTransform = hostReplica.GetComponent(ClientTransformType);
        Assert.That(ownerTransform, Is.Not.Null);
        Assert.That(remoteTransform, Is.Not.Null);
        ((NetworkTransform)remoteTransform).SetMaxInterpolationBound(2f);
        yield return Until(() => TryPose(remoteTransform, out _, out _), "complete initial synchronization sample");
        Assert.That(TryPose(remoteTransform, out Vector3 firstReceivedPosition, out Quaternion firstReceivedRotation), Is.True,
            "The initial synchronization establishes the first complete remote baseline.");
        Assert.That(IsFinite(firstReceivedPosition), Is.True);
        Assert.That(IsValidRotation(firstReceivedRotation), Is.True);
        Checkpoint("initial-sync-received");

        Assert.That(TryPose(ownerTransform, out Vector3 hostPose, out Quaternion hostRotation), Is.True,
            "Authority can use its live world transform immediately.");
        Assert.That(hostPose, Is.EqualTo(ownerPlayer.transform.position));
        Assert.That(hostRotation, Is.EqualTo(ownerPlayer.transform.rotation));

        NetworkTransform ownerNetworkTransform = (NetworkTransform)ownerTransform;
        NetworkTransform remoteNetworkTransform = (NetworkTransform)remoteTransform;
        ownerNetworkTransform.SwitchTransformSpaceWhenParented = false;
        ownerNetworkTransform.InLocalSpace = true;
        Vector3 rootLocalPosition = new Vector3(3.1f, -1.4f, 5.8f);
        Quaternion rootLocalRotation = Quaternion.Euler(41f, 72f, -28f);
        ownerPlayer.transform.SetPositionAndRotation(rootLocalPosition, rootLocalRotation);
        yield return Until(() => remoteNetworkTransform.InLocalSpace && remoteTransform.transform.parent == null
            && TryPose(remoteTransform, out Vector3 p, out Quaternion r)
            && Vector3.Distance(p, rootLocalPosition) < 0.02f && Quaternion.Angle(r, rootLocalRotation) < 0.5f,
            "root local-space pose arrives without a parent");
        Assert.That(TryPose(remoteTransform, out Vector3 rootReceivedPosition, out Quaternion rootReceivedRotation), Is.True,
            "InLocalSpace with no parent is still a complete pose sample.");
        Assert.That(remoteTransform.transform.parent, Is.Null);
        Assert.That(Vector3.Distance(rootReceivedPosition, ownerPlayer.transform.position), Is.LessThan(0.02f));
        Assert.That(Quaternion.Angle(rootReceivedRotation, ownerPlayer.transform.rotation), Is.LessThan(0.5f));
        Checkpoint("unparented-local-pose-received");

        ownerNetworkTransform.SwitchTransformSpaceWhenParented = true;
        ownerNetworkTransform.InLocalSpace = false;
        Vector3 restoredWorldPosition = new Vector3(9.2f, 1.8f, -6.4f);
        Quaternion restoredWorldRotation = Quaternion.Euler(3f, 22f, 57f);
        ownerPlayer.transform.SetPositionAndRotation(restoredWorldPosition, restoredWorldRotation);
        yield return Until(() => !remoteNetworkTransform.InLocalSpace && remoteNetworkTransform.SwitchTransformSpaceWhenParented
            && TryPose(remoteTransform, out Vector3 p, out Quaternion r)
            && Vector3.Distance(p, restoredWorldPosition) < 0.02f && Quaternion.Angle(r, restoredWorldRotation) < 0.5f,
            "owner restores world-space settings before the parent transition test");
        Checkpoint("world-space-settings-restored");

        Vector3 receivedPosition = new Vector3(2.5f, 1.25f, -3.75f);
        Quaternion receivedRotation = Quaternion.Euler(17f, 83f, -11f);
        ownerPlayer.transform.SetPositionAndRotation(receivedPosition, receivedRotation);
        yield return Until(() => TryPose(remoteTransform, out Vector3 p, out Quaternion r)
            && Vector3.Distance(p, receivedPosition) < 0.02f && Quaternion.Angle(r, receivedRotation) < 0.5f,
            "remote complete pose receive");
        Assert.That(TryPose(remoteTransform, out Vector3 received, out Quaternion rotation), Is.True);
        Assert.That(Vector3.Distance(received, receivedPosition), Is.LessThan(0.02f));
        Assert.That(Quaternion.Angle(rotation, receivedRotation), Is.LessThan(0.5f));
        Assert.That(Vector3.Distance(ownerPlayer.transform.position, received), Is.LessThan(0.02f),
            "The cached received position must agree with authority.");
        Assert.That(Vector3.Distance(hostReplica.transform.position, received), Is.GreaterThan(0.05f),
            "The replica's render Transform must still lag while the received-pose API is already current.");
        Checkpoint("complete-pose-received-render-lags");

        Vector3 secondPosition = receivedPosition + new Vector3(0.8f, 0f, 0f);
        ownerPlayer.transform.position = secondPosition;
        yield return Until(() => TryPose(remoteTransform, out Vector3 p, out _)
            && Vector3.Distance(p, secondPosition) < 0.02f, "position-only delta receive");
        Assert.That(TryPose(remoteTransform, out received, out rotation), Is.True);
        Assert.That(Vector3.Distance(received, secondPosition), Is.LessThan(0.02f));
        Assert.That(Quaternion.Angle(rotation, receivedRotation), Is.LessThan(0.5f),
            "A position-only delta retains the last received rotation.");

        Quaternion secondRotation = Quaternion.Euler(17f, 131f, -11f);
        ownerPlayer.transform.rotation = secondRotation;
        yield return Until(() => TryPose(remoteTransform, out _, out Quaternion r)
            && Quaternion.Angle(r, secondRotation) < 0.5f, "rotation-only delta receive");
        Assert.That(TryPose(remoteTransform, out received, out rotation), Is.True);
        Assert.That(Vector3.Distance(received, secondPosition), Is.LessThan(0.02f),
            "A rotation-only delta retains the last received position.");
        Assert.That(Quaternion.Angle(rotation, secondRotation), Is.LessThan(0.5f));
        Checkpoint("rotation-y-only-delta-received");

        Vector3 teleportedPosition = new Vector3(-4.5f, 2.25f, 6.75f);
        Quaternion teleportedRotation = Quaternion.Euler(32f, -47f, 13f);
        ((NetworkTransform)ownerTransform).Teleport(teleportedPosition, teleportedRotation, ownerPlayer.transform.localScale);
        yield return Until(() => TryPose(remoteTransform, out Vector3 p, out Quaternion r)
            && Vector3.Distance(p, teleportedPosition) < 0.02f && Quaternion.Angle(r, teleportedRotation) < 0.5f,
            "teleport state replaces the cached baseline");
        Checkpoint("teleport-received");

        NetworkObject hostParent = host.LocalClient.PlayerObject;
        NetworkObject ownerBReplica = clientB.SpawnManager.SpawnedObjectsList.FirstOrDefault(item =>
            item.NetworkObjectId == hostReplica.NetworkObjectId);
        Assert.That(ownerBReplica, Is.Not.Null, "Client B has the object to receive authority for.");
        hostReplica.ChangeOwnership(clientB.LocalClientId);
        Checkpoint("ownership-a-to-b-requested");
        Assert.That(TryPose(remoteTransform, out _, out _), Is.False,
            "The server invalidates client A's cached sample as ownership transfers A to B.");
        yield return Until(() => ownerBReplica.OwnerClientId == clientB.LocalClientId, "client B receives ownership");
        Vector3 ownerBPosition = new Vector3(3.5f, 1.5f, 4.25f);
        Quaternion ownerBRotation = Quaternion.Euler(-9f, 204f, 7f);
        ownerBReplica.transform.SetPositionAndRotation(ownerBPosition, ownerBRotation);
        yield return Until(() => TryPose(remoteTransform, out Vector3 p, out Quaternion r)
            && Vector3.Distance(p, ownerBPosition) < 0.03f && Quaternion.Angle(r, ownerBRotation) < 0.8f,
            "client B's new authoritative pose reaches the server");
        Checkpoint("ownership-b-pose-received");

        NetworkObject parentReplicaOnB = clientB.SpawnManager.SpawnedObjectsList.FirstOrDefault(item =>
            item.NetworkObjectId == hostParent.NetworkObjectId);
        Assert.That(parentReplicaOnB, Is.Not.Null);
        Assert.That(ownerBReplica.TrySetParent(parentReplicaOnB, false), Is.True,
            "The new owner may parent its network object to the host player.");
        yield return Until(() => remoteTransform.transform.parent == hostParent.transform
            && ((NetworkTransform)remoteTransform).InLocalSpace, "parented local-space state arrives");
        yield return Until(() => TryPose(remoteTransform, out Vector3 p, out _)
            && Vector3.Distance(p, ownerBReplica.transform.position) < 0.03f, "complete parented state is received");
        Assert.That(TryPose(remoteTransform, out Vector3 parentedPose, out _), Is.True);
        Assert.That(Vector3.Distance(parentedPose, ownerBReplica.transform.position), Is.LessThan(0.03f));
        Checkpoint("parented-local-pose-received");
        hostParent.transform.position += new Vector3(0f, 0f, 2.5f);
        Assert.That(TryPose(remoteTransform, out Vector3 movedParentPose, out _), Is.True);
        Assert.That(Vector3.Distance(movedParentPose, parentedPose + new Vector3(0f, 0f, 2.5f)), Is.LessThan(0.03f),
            "A cached local pose is converted through the current parent exactly once when read.");
        Assert.That(ownerBReplica.TryRemoveParent(false), Is.True);
        yield return Until(() => remoteTransform.transform.parent == null && !((NetworkTransform)remoteTransform).InLocalSpace,
            "unparented world-space state arrives");
        yield return Until(() => TryPose(remoteTransform, out Vector3 p, out _)
            && Vector3.Distance(p, ownerBReplica.transform.position) < 0.03f, "complete unparented state is received");
        Checkpoint("unparented-world-pose-received");

        hostReplica.Despawn(false);
        Assert.That(TryPose(remoteTransform, out _, out _), Is.False,
            "A despawned instance cannot expose its previous received sample.");
        hostReplica.SpawnWithOwnership(clientB.LocalClientId);
        yield return Until(() => hostReplica.IsSpawned && TryPose(remoteTransform, out _, out _),
            "respawn produces a new synchronized sample");
        NetworkObject respawnedOwnerReplica = clientB.SpawnManager.SpawnedObjectsList.FirstOrDefault(item =>
            item.NetworkObjectId == hostReplica.NetworkObjectId);
        Assert.That(respawnedOwnerReplica, Is.Not.Null, "The new owner replica exists after respawn.");
        Vector3 stationaryPosition = respawnedOwnerReplica.transform.position;
        Quaternion stationaryRotation = respawnedOwnerReplica.transform.rotation;
        yield return Until(() => TryPose(remoteTransform, out Vector3 p, out Quaternion r)
            && Vector3.Distance(p, stationaryPosition) < 0.03f && Quaternion.Angle(r, stationaryRotation) < 0.8f,
            "the new authority's respawn pose is received");
        Checkpoint("respawn-pose-received");

        // No time-based expiry: a stationary transform remains queryable without further packets.
        yield return new WaitForSeconds(0.5f);
        Assert.That(TryPose(remoteTransform, out received, out rotation), Is.True);
        Assert.That(Vector3.Distance(received, stationaryPosition), Is.LessThan(0.03f));
        Assert.That(Quaternion.Angle(rotation, stationaryRotation), Is.LessThan(0.8f));
        Checkpoint("stationary-pose-retained");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (clientB != null && clientB.IsListening) clientB.Shutdown();
        yield return UntilShutdownCompleted(clientB, "client B shutdown");
        if (client != null && client.IsListening) client.Shutdown();
        yield return UntilShutdownCompleted(client, "client A shutdown");
        if (host != null && host.IsListening) host.Shutdown();
        yield return UntilShutdownCompleted(host, "host shutdown");
        if (clientRoot != null) Object.Destroy(clientRoot);
        if (clientBRoot != null) Object.Destroy(clientBRoot);
        if (hostRoot != null) Object.Destroy(hostRoot);
        if (prefabHolder != null) Object.Destroy(prefabHolder);
        yield return null;
    }

    private NetworkManager CreateManager(string name, ushort port)
    {
        GameObject root = new GameObject(name);
        if (name.Contains(" host")) hostRoot = root;
        else if (name.Contains("client B")) clientBRoot = root;
        else clientRoot = root;
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = prefab,
            EnableSceneManagement = false
        };
        return manager;
    }

    private static void SetPrefabHash(NetworkObject networkObject, uint hash) =>
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(networkObject, hash);

    private void Checkpoint(string name) =>
        File.AppendAllText(checkpointPath, $"{System.DateTime.UtcNow:o} {name}{System.Environment.NewLine}");

    private static bool TryPose(Component component, out Vector3 position, out Quaternion rotation)
    {
        object[] arguments = { Vector3.zero, Quaternion.identity };
        bool result = (bool)ClientTransformType.GetMethod("TryGetLatestAuthoritativeWorldPose").Invoke(component, arguments);
        position = (Vector3)arguments[0];
        rotation = (Quaternion)arguments[1];
        return result;
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
        && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static bool IsValidRotation(Quaternion value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
        && !float.IsNaN(value.z) && !float.IsInfinity(value.z)
        && !float.IsNaN(value.w) && !float.IsInfinity(value.w)
        && value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > 0.000001f;

    private static ushort ReservePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        ushort port = (ushort)((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static IEnumerator Until(System.Func<bool> condition, string description)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!condition())
        {
            if (Time.realtimeSinceStartup >= deadline)
                Assert.Fail($"Timed out waiting for {description}.");
            yield return null;
        }
    }

    private static IEnumerator UntilShutdownCompleted(NetworkManager manager, string description)
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (manager != null && manager.ShutdownInProgress)
        {
            if (Time.realtimeSinceStartup >= deadline)
                Assert.Fail($"Timed out waiting for {description} to complete.");
            yield return null;
        }
    }
}




