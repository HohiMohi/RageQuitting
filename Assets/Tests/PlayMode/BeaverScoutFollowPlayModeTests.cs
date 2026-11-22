using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using System.Net;
using System.Net.Sockets;

public sealed class BeaverScoutFollowPlayModeTests
{
    private sealed class TemporaryNavMesh : IDisposable
    {
        private readonly GameObject surface;
        private readonly NavMeshData data;
        private NavMeshDataInstance instance;

        public TemporaryNavMesh(float size)
        {
            surface = GameObject.CreatePrimitive(PrimitiveType.Plane);
            surface.name = "Beaver Scout follow test NavMesh";
            surface.transform.localScale = Vector3.one * (size / 10f);
            NavMeshBuildSource source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Mesh,
                sourceObject = surface.GetComponent<MeshFilter>().sharedMesh,
                transform = surface.transform.localToWorldMatrix,
                area = 0
            };
            data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),
                new List<NavMeshBuildSource> { source }, new Bounds(Vector3.zero, new Vector3(size + 4f, 4f, size + 4f)),
                Vector3.zero, Quaternion.identity);
            Assert.That(data, Is.Not.Null);
            instance = NavMesh.AddNavMeshData(data);
            Assert.That(instance.valid, Is.True);
        }

        public void Dispose()
        {
            if (instance.valid) instance.Remove();
            if (data != null) UnityEngine.Object.DestroyImmediate(data);
            if (surface != null) UnityEngine.Object.DestroyImmediate(surface);
        }
    }

    private static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Type BrainType = Type.GetType("NPCBrain, Assembly-CSharp");
    private static readonly Type BehaviorType = Type.GetType("BeaverScoutBehaviorSO, Assembly-CSharp");
    private static readonly Type DefinitionType = Type.GetType("NPCDefinitionSO, Assembly-CSharp");
    private static readonly Type PlayerHealthType = Type.GetType("PlayerHealth, Assembly-CSharp");
    private static readonly Type NPCHealthType = Type.GetType("NPCHealth, Assembly-CSharp");
    private static readonly Type ClientNetworkTransformType = Type.GetType("ClientNetworkTransform, Assembly-CSharp");
    private PlayModeTestScope networkScope;
    private TemporaryNavMesh networkNavMesh;

    [UnityTearDown]
    public IEnumerator CleanupNetworkFixture()
    {
        if (networkScope != null)
        {
            PlayModeTestScope cleanup = networkScope;
            yield return cleanup.Cleanup();
            networkScope = null;
            cleanup.AssertCleanupSucceeded();
        }
        networkNavMesh?.Dispose();
        networkNavMesh = null;
    }

    [UnityTest]
    public IEnumerator FollowStopsAtFiveMetersAndPausesItsTimerUntilGazeIsLostBeyondFourteenMeters()
    {
        TemporaryNavMesh navMesh = null;
        GameObject scout = null;
        GameObject player = null;
        ScriptableObject definition = null;
        ScriptableObject behavior = null;
        object controller = null;
        try
        {
            navMesh = new TemporaryNavMesh(60f);
            SetupScout(out scout, out definition, out behavior, out controller);
            player = CreatePlayer("Follow target", new Vector3(0f, 0f, 4f));
            SetFollowTarget(controller, player);
            NavMeshAgent agent = scout.GetComponent<NavMeshAgent>();
            float originalStoppingDistance = agent.stoppingDistance;
            Invoke(controller, "EnterFollowingPlayer");
            Vector3 initialPosition = scout.transform.position;
            Assert.That(agent.isStopped, Is.True, "Starting inside the 5m radius stops immediately without retreating.");
            yield return new WaitForSeconds(0.2f);
            Vector3 horizontalDelta = scout.transform.position - initialPosition;
            horizontalDelta.y = 0f;
            Assert.That(horizontalDelta.magnitude, Is.LessThan(0.02f),
                "No horizontal retreat is allowed while the player is inside the follow distance. Full delta: " +
                (scout.transform.position - initialPosition));

            player.transform.position = new Vector3(0f, 0f, 5.2f);
            Physics.SyncTransforms();
            Invoke(controller, "UpdateFollowMovement");
            Assert.That(agent.isStopped, Is.True, "A stopped Scout keeps the 0.5m re-engage deadband.");
            player.transform.position = new Vector3(0f, 0f, 5.5f);
            Physics.SyncTransforms();
            Invoke(controller, "UpdateFollowMovement");
            Assert.That(agent.isStopped, Is.False, "At 5.5m the Scout resumes the complete NavMesh route.");

            player.transform.position = new Vector3(0f, 0f, 8f);
            player.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Physics.SyncTransforms();
            SetField(controller, "followEndTime", Time.time + 0.1f);
            Invoke(controller, "UpdateFollowingPlayer");
            Assert.That(GetField<bool>(controller, "isPretendingToLookAway"), Is.True,
                "The Scout pauses when the followed player's body faces it.");
            Assert.That(agent.isStopped, Is.True);
            Assert.That(agent.updateRotation, Is.False);
            float yawBeforeTurn = scout.transform.eulerAngles.y;
            yield return null;
            Invoke(controller, "TickFrame");
            float yawTurned = Mathf.Abs(Mathf.DeltaAngle(yawBeforeTurn, scout.transform.eulerAngles.y));
            Assert.That(yawTurned, Is.GreaterThan(0f), "The Scout turns toward its fixed sideways yaw smoothly per frame.");
            Assert.That(yawTurned, Is.LessThanOrEqualTo(agent.angularSpeed * Time.deltaTime + 1f));

            yield return new WaitForSeconds(0.2f);
            Invoke(controller, "UpdateFollowingPlayer");
            Assert.That(GetField<object>(controller, "state").ToString(), Is.EqualTo("FollowingPlayer"),
                "The follow timer cannot expire during the pretend-to-look-away pause.");

            player.transform.position = new Vector3(0f, 0f, 20f);
            Physics.SyncTransforms();
            Invoke(controller, "UpdateFollowingPlayer");
            Assert.That(GetField<bool>(controller, "isPretendingToLookAway"), Is.True,
                "Leaving the 14m gaze radius starts a continuous-loss grace period.");
            yield return new WaitForSeconds(0.15f);
            Invoke(controller, "UpdateFollowingPlayer");
            Assert.That(GetField<bool>(controller, "isPretendingToLookAway"), Is.True,
                "A loss shorter than 0.25s does not resume the chase.");
            yield return new WaitForSeconds(0.15f);
            Invoke(controller, "UpdateFollowingPlayer");
            Assert.That(GetField<bool>(controller, "isPretendingToLookAway"), Is.False);
            Assert.That(GetField<float>(controller, "followEndTime"), Is.GreaterThan(Time.time),
                "The remaining active follow time resumes after the gaze grace period.");
            Assert.That(agent.updateRotation, Is.True);
            Assert.That(agent.isStopped, Is.False, "The Scout resumes chasing even though gaze ended outside 14m.");

            Type damageEventType = Type.GetType("NPCHealth+DamageEventArgs, Assembly-CSharp");
            object damageEvent = Activator.CreateInstance(damageEventType, new object[] { 35f, 34f, null });
            Invoke(controller, "BrainHealth_OnDamaged", new object[] { scout.GetComponent(NPCHealthType), damageEvent });
            Assert.That(GetField<object>(controller, "state").ToString(), Is.EqualTo("PreparingAttack"));
            Assert.That(GetField<bool>(controller, "isPretendingToLookAway"), Is.False);
            Assert.That(agent.stoppingDistance, Is.EqualTo(originalStoppingDistance),
                "Damage reaction restores the temporary 5m stopping distance.");
            Assert.That(agent.updateRotation, Is.True);
            Assert.That(agent.isStopped, Is.True, "Damage reaction holds the Scout until its retaliation decision.");

            Invoke(controller, "Exit");
            controller = null;
            Assert.That(agent.stoppingDistance, Is.EqualTo(originalStoppingDistance));
            Assert.That(agent.updateRotation, Is.True);
        }
        finally
        {
            if (controller != null) Invoke(controller, "Exit");
            if (scout != null) UnityEngine.Object.DestroyImmediate(scout);
            if (player != null) UnityEngine.Object.DestroyImmediate(player);
            if (behavior != null) UnityEngine.Object.DestroyImmediate(behavior);
            if (definition != null) UnityEngine.Object.DestroyImmediate(definition);
            navMesh?.Dispose();
        }
    }

    [Test]
    public void GazeUsesOnlyFollowedBodyYawAndRequiresClearLineOfSight()
    {
        TemporaryNavMesh navMesh = null;
        GameObject scout = null;
        GameObject followedPlayer = null;
        GameObject otherPlayer = null;
        GameObject wall = null;
        ScriptableObject definition = null;
        ScriptableObject behavior = null;
        object controller = null;
        try
        {
            navMesh = new TemporaryNavMesh(40f);
            SetupScout(out scout, out definition, out behavior, out controller);
            followedPlayer = CreatePlayer("Followed player", new Vector3(0f, 0f, 8f));
            otherPlayer = CreatePlayer("Unfollowed player", new Vector3(3f, 0f, 8f));
            otherPlayer.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            SetFollowTarget(controller, followedPlayer);
            Physics.SyncTransforms();

            Assert.That(ObserveGaze(controller), Is.False,
                "A different nearby player's body yaw cannot trigger the followed Scout's gaze response.");

            followedPlayer.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Gaze occluder";
            wall.transform.position = new Vector3(0f, 1.25f, 4f);
            wall.transform.localScale = new Vector3(2f, 2f, 0.6f);
            Physics.SyncTransforms();
            Assert.That(ObserveGaze(controller), Is.False, "A solid wall blocks the body-yaw gaze observation.");

            UnityEngine.Object.DestroyImmediate(wall);
            wall = null;
            Physics.SyncTransforms();
            Assert.That(ObserveGaze(controller), Is.True, "With clear sight, the followed player's body yaw is observed.");
            Invoke(controller, "EnterFollowingPlayer");
            Assert.That(GetField<bool>(controller, "isPretendingToLookAway"), Is.True,
                "A followed player already looking at the Scout is gated before the first follow movement update.");
        }
        finally
        {
            if (controller != null) Invoke(controller, "Exit");
            if (scout != null) UnityEngine.Object.DestroyImmediate(scout);
            if (followedPlayer != null) UnityEngine.Object.DestroyImmediate(followedPlayer);
            if (otherPlayer != null) UnityEngine.Object.DestroyImmediate(otherPlayer);
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            if (behavior != null) UnityEngine.Object.DestroyImmediate(behavior);
            if (definition != null) UnityEngine.Object.DestroyImmediate(definition);
            navMesh?.Dispose();
        }
    }

    [UnityTest]
    public IEnumerator HostUsesRemoteOwnerBodyYawAndObserverReceivesThatYaw()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The Scout body-yaw replication fixture requires no pre-existing network session.");

        networkScope = new PlayModeTestScope("BeaverScoutFollowYawReplication");
        networkNavMesh = new TemporaryNavMesh(60f);
        GameObject playerPrefab = CreateNetworkPlayerPrefab();
        GameObject scoutPrefab = (GameObject)LoadAsset("Assets/Prefabs/New/NPC/NPC_BeaverScout.prefab", typeof(GameObject));
        networkScope.Own(playerPrefab.transform.parent.gameObject);
        GameObject hostRoot = networkScope.Own(new GameObject("Scout yaw host manager"));
        GameObject ownerRoot = networkScope.Own(new GameObject("Scout yaw owner manager"));
        GameObject observerRoot = networkScope.Own(new GameObject("Scout yaw observer manager"));
        ushort port = ReservePort();
        NetworkManager host = CreateManager(hostRoot, playerPrefab, port);
        NetworkManager owner = CreateManager(ownerRoot, playerPrefab, port);
        NetworkManager observer = CreateManager(observerRoot, playerPrefab, port);
        networkScope.OwnManager(host);
        networkScope.OwnManager(owner);
        networkScope.OwnManager(observer);
        host.AddNetworkPrefab(playerPrefab);
        owner.AddNetworkPrefab(playerPrefab);
        observer.AddNetworkPrefab(playerPrefab);
        host.AddNetworkPrefab(scoutPrefab);
        owner.AddNetworkPrefab(scoutPrefab);
        observer.AddNetworkPrefab(scoutPrefab);

        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.LocalClient != null && host.LocalClient.PlayerObject != null, "host player spawn");
        host.LocalClient.PlayerObject.transform.position = new Vector3(3f, 0f, 8f);
        host.LocalClient.PlayerObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Physics.SyncTransforms();
        Assert.That(owner.StartClient(), Is.True);
        owner.SetSingleton();
        yield return Until(() => owner.IsConnectedClient && host.ConnectedClientsIds.Count == 2,
            "remote owner connection");
        Assert.That(observer.StartClient(), Is.True);
        observer.SetSingleton();
        yield return Until(() => observer.IsConnectedClient && observer.LocalClient != null &&
            observer.LocalClient.PlayerObject != null && host.ConnectedClientsIds.Count == 3,
            "observer connection");
        observer.LocalClient.PlayerObject.transform.position = new Vector3(-4f, 0f, 8f);
        observer.LocalClient.PlayerObject.transform.rotation = Quaternion.identity;
        Physics.SyncTransforms();
        foreach (NetworkObject replica in owner.SpawnManager.SpawnedObjects.Values)
            DisableReplicaColliders(replica.gameObject);
        foreach (NetworkObject replica in observer.SpawnManager.SpawnedObjects.Values)
            DisableReplicaColliders(replica.gameObject);

        NetworkObject ownerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        owner.SetSingleton();
        owner.LocalClient.PlayerObject.transform.position = new Vector3(0f, 0f, 8f);
        owner.LocalClient.PlayerObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Physics.SyncTransforms();

        Component hostPlayerTransform = ownerPlayer.GetComponent(ClientNetworkTransformType);
        Assert.That(hostPlayerTransform, Is.Not.Null);
        host.SetSingleton();
        yield return Until(() => HasAuthoritativePose(hostPlayerTransform, out Vector3 position, out Quaternion rotation)
            && Vector3.Distance(position, new Vector3(0f, 0f, 8f)) < 0.5f
            && Mathf.Abs(Mathf.DeltaAngle(rotation.eulerAngles.y, 180f)) < 5f,
            "server receives remote owner's body pose");

        GameObject scout = UnityEngine.Object.Instantiate(scoutPrefab, Vector3.zero, Quaternion.identity);
        networkScope.Own(scout);
        NavMeshAgent scoutAgent = scout.GetComponent<NavMeshAgent>();
        Assert.That(scoutAgent.Warp(Vector3.zero), Is.True);
        NetworkObject scoutNetworkObject = scout.GetComponent<NetworkObject>();
        scoutNetworkObject.Spawn();
        ulong scoutId = scoutNetworkObject.NetworkObjectId;
        NetworkObject ownerScout = null;
        NetworkObject observerScout = null;
        yield return Until(() => owner.SpawnManager.SpawnedObjects.TryGetValue(scoutId, out ownerScout)
            && observer.SpawnManager.SpawnedObjects.TryGetValue(scoutId, out observerScout),
            "remote Scout replicas");
        DisableReplicaColliders(ownerScout.gameObject);
        DisableReplicaColliders(observerScout.gameObject);

        Component scoutBrain = scout.GetComponent(BrainType);
        object controller = BrainType.GetProperty("BehaviorController").GetValue(scoutBrain);
        Invoke(controller, "EnterNoticingPlayer", new object[]
        {
            ownerPlayer.GetComponent(PlayerHealthType), ownerPlayer.transform
        });
        SetField(controller, "noticeEndTime", Time.time);
        Invoke(controller, "Tick");
        Assert.That(GetField<bool>(controller, "isPretendingToLookAway"), Is.True,
            "The server's follow decision uses the owner's replicated body yaw.");
        yield return Until(() => Quaternion.Angle(scout.transform.rotation, Quaternion.Euler(0f, 90f, 0f)) < 6f,
            "server Scout completes its smooth sideways turn");

        yield return Until(() => Quaternion.Angle(observerScout.transform.rotation, scout.transform.rotation) < 5f
            && Mathf.Abs(Mathf.DeltaAngle(observerScout.transform.eulerAngles.y, 90f)) < 6f,
            "observer Scout receives the server's sideways rotation");

        owner.SetSingleton();
        owner.LocalClient.PlayerObject.transform.rotation = Quaternion.identity;
        Physics.SyncTransforms();
        host.SetSingleton();
        yield return Until(() => HasAuthoritativePose(hostPlayerTransform, out _, out Quaternion changedRotation)
            && Mathf.Abs(Mathf.DeltaAngle(changedRotation.eulerAngles.y, 0f)) < 5f,
            "server receives remote owner's changed body yaw");
        Assert.That(ObserveGaze(controller), Is.False,
            "The Scout stops reading the followed owner as watching after the replicated body turns away.");

        yield return Until(() => !GetField<bool>(controller, "isPretendingToLookAway")
            && scout.transform.position.z > 0.1f,
            "server resumes the real NavMesh follow after owner turns away");
        yield return Until(() => Quaternion.Angle(observerScout.transform.rotation, scout.transform.rotation) < 5f,
            "observer Scout follows the replicated server rotation after resuming");

        Assert.That(ObserveGaze(controller), Is.False,
            "Another peer looking toward the Scout cannot trigger a response for the followed owner.");
        Invoke(controller, "Exit");
    }

    private static void SetupScout(out GameObject scout, out ScriptableObject definition,
        out ScriptableObject behavior, out object controller)
    {
        scout = new GameObject("Beaver Scout follow test");
        CapsuleCollider bodyCollider = scout.AddComponent<CapsuleCollider>();
        bodyCollider.center = Vector3.up;
        bodyCollider.height = 2f;
        Component brain = (Component)scout.AddComponent(BrainType);
        NavMeshAgent agent = scout.GetComponent<NavMeshAgent>();
        Assert.That(agent.Warp(Vector3.zero), Is.True);
        agent.speed = 2f;
        agent.angularSpeed = 420f;
        agent.stoppingDistance = 1f;

        definition = ScriptableObject.CreateInstance(DefinitionType);
        DefinitionType.GetField("detectionRadius").SetValue(definition, 14f);
        SetField(brain, "definition", definition);
        BrainType.GetMethod("ApplyDefinition", InstancePrivate).Invoke(brain, null);
        agent.speed = 2f;
        agent.angularSpeed = 420f;
        agent.stoppingDistance = 1f;

        behavior = ScriptableObject.CreateInstance(BehaviorType);
        SetField(behavior, "followStoppingDistance", 5f);
        SetField(behavior, "followDurationMin", 8f);
        SetField(behavior, "followDurationMax", 8f);
        controller = BehaviorType.GetMethod("CreateController").Invoke(behavior, new[] { brain });
        Invoke(controller, "Enter");
    }

    private static GameObject CreatePlayer(string name, Vector3 position)
    {
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = name;
        player.transform.position = position;
        player.AddComponent(PlayerHealthType);
        return player;
    }

    private static GameObject CreateNetworkPlayerPrefab()
    {
        GameObject staging = new GameObject("Scout yaw player prefab staging");
        staging.SetActive(false);
        GameObject prefab = new GameObject("Scout yaw network player prefab");
        prefab.transform.SetParent(staging.transform, false);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", InstancePrivate).SetValue(networkObject, 0x4D554433u);
        networkObject.SetSceneObjectStatus(false);
        prefab.AddComponent<CapsuleCollider>();
        prefab.AddComponent(PlayerHealthType);
        prefab.AddComponent(ClientNetworkTransformType);
        return prefab;
    }

    private static void DisableReplicaColliders(GameObject replica)
    {
        foreach (Collider collider in replica.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
    }

    private static NetworkManager CreateManager(GameObject root, GameObject playerPrefab, ushort port)
    {
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = playerPrefab,
            EnableSceneManagement = false
        };
        return manager;
    }

    private static ushort ReservePort()
    {
        using UdpClient socket = new UdpClient(0);
        return (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
    }

    private static bool HasAuthoritativePose(Component networkTransform, out Vector3 position, out Quaternion rotation)
    {
        object[] args = { Vector3.zero, Quaternion.identity };
        bool available = (bool)ClientNetworkTransformType.GetMethod("TryGetLatestAuthoritativeWorldPose")
            .Invoke(networkTransform, args);
        position = (Vector3)args[0];
        rotation = (Quaternion)args[1];
        return available;
    }

    private static UnityEngine.Object LoadAsset(string path, Type type)
    {
        Type assetDatabase = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
        return (UnityEngine.Object)assetDatabase.GetMethod("LoadAssetAtPath", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(string), typeof(Type) }, null).Invoke(null, new object[] { path, type });
    }

    private static IEnumerator Until(Func<bool> predicate, string label)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(predicate(), Is.True, "Timed out: " + label);
    }

    private static void SetFollowTarget(object controller, GameObject player)
    {
        SetField(controller, "followTargetPlayerHealth", player.GetComponent(PlayerHealthType));
        SetField(controller, "followTargetTransform", player.transform);
    }

    private static bool ObserveGaze(object controller)
    {
        object[] args = { null, null };
        return (bool)Invoke(controller, "TryGetGazeObservation", args);
    }

    private static object Invoke(object target, string method, object[] args = null) =>
        target.GetType().GetMethod(method, InstancePrivate).Invoke(target, args);

    private static void SetField(object target, string fieldName, object value) =>
        target.GetType().GetField(fieldName, InstancePrivate).SetValue(target, value);

    private static T GetField<T>(object target, string fieldName) =>
        (T)target.GetType().GetField(fieldName, InstancePrivate).GetValue(target);
}
