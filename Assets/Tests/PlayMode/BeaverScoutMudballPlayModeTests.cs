using System;
using System.Reflection;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class BeaverScoutMudballPlayModeTests
{
    private static readonly Type ThrowType = Type.GetType("BeaverScoutMudThrow, Assembly-CSharp");
    private static readonly Type PlayerHealthType = Type.GetType("PlayerHealth, Assembly-CSharp");
    private static readonly Type MudControllerType = Type.GetType("PlayerMudStatusController, Assembly-CSharp");
    private static readonly Type StatusControllerType = Type.GetType("PlayerTemporaryStatusController, Assembly-CSharp");
    private static readonly Type ImpulseControllerType = Type.GetType("PlayerExternalImpulseController, Assembly-CSharp");
    private static readonly Type PlayerInputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
    private static readonly Type NetworkObjectType = Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime");
    private PlayModeTestScope scope;

    [UnityTearDown]
    public IEnumerator CleanupNetworkFixture()
    {
        if (scope == null) yield break;
        PlayModeTestScope cleanup = scope;
        yield return cleanup.Cleanup();
        scope = null;
        cleanup.AssertCleanupSucceeded();
    }

    [Test]
    public void TargetValidationSkipsShooterCollidersAndRejectsOcclusionAndOutOfRangeTargets()
    {
        GameObject scout = new GameObject("Mudball scout");
        Vector3 offset = new Vector3(100f, 50f, 100f);
        scout.transform.position = offset;
        scout.AddComponent<CapsuleCollider>();
        Component thrower = scout.AddComponent(ThrowType);
        Transform release = new GameObject("Release point").transform;
        release.SetParent(scout.transform, false);
        release.localPosition = new Vector3(0f, 1.1f, 0.45f);
        SetField(thrower, "releasePoint", release);

        GameObject behind = CreatePlayer("Player behind scout", offset + new Vector3(0f, 0f, -4f));
        GameObject covered = CreatePlayer("Covered nearer player", offset + new Vector3(0f, 0f, 4f));
        GameObject visible = CreatePlayer("Visible farther player", offset + new Vector3(4f, 0f, 6f));
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Cover wall";
        wall.transform.position = offset + new Vector3(0f, 1f, 2f);
        wall.transform.localScale = new Vector3(1f, 3f, 0.4f);
        Physics.SyncTransforms();

        try
        {
            Assert.That(CanTarget(thrower, behind, 10f, true), Is.True,
                "The ray must ignore the scout's own body while validating a target behind it.");
            Assert.That(CanTarget(thrower, covered, 10f, true), Is.False,
                "A closer player behind solid cover is not a valid aim target.");
            Assert.That(CanTarget(thrower, visible, 10f, true), Is.True,
                "An occluded nearer candidate must not prevent validating a farther clear candidate.");

            GameObject outOfRange = CreatePlayer("Out of range", offset + new Vector3(0f, 0f, 10.1f));
            Physics.SyncTransforms();
            Assert.That(CanTarget(thrower, outOfRange, 10f, false), Is.False,
                "Range is measured from the scout root and is strict at the configured limit.");
            UnityEngine.Object.DestroyImmediate(outOfRange);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(scout);
            UnityEngine.Object.DestroyImmediate(behind);
            UnityEngine.Object.DestroyImmediate(covered);
            UnityEngine.Object.DestroyImmediate(visible);
            UnityEngine.Object.DestroyImmediate(wall);
        }
    }

    [Test]
    public void CancellingWindupLeavesCooldownAvailable()
    {
        GameObject scout = new GameObject("Mudball cooldown scout");
        Component thrower = scout.AddComponent(ThrowType);
        Transform release = new GameObject("Release point").transform;
        release.SetParent(scout.transform, false);
        release.localPosition = Vector3.up;
        SetField(thrower, "releasePoint", release);
        GameObject player = CreatePlayer("Locked target", new Vector3(0f, 0f, 2f));
        Physics.SyncTransforms();
        UnityEngine.Object ballPrefab = LoadAsset("Assets/Prefabs/New/NPC/BeaverScoutMudball.prefab", Type.GetType("UnityEngine.GameObject, UnityEngine.CoreModule"));
        Component ball = ((GameObject)ballPrefab).GetComponent(Type.GetType("BeaverScoutMudball, Assembly-CSharp"));
        UnityEngine.Object profile = LoadAsset("Assets/ScriptableObjectAssets/New/NPC/BeaverScoutMudImpulse.asset", Type.GetType("ExternalImpulseProfileSO, Assembly-CSharp"));

        try
        {
            MethodInfo begin = ThrowType.GetMethod("TryBeginWindup");
            object[] arguments = { player.GetComponent(PlayerHealthType), player.transform, 10f, false, (LayerMask)~0,
                5f, 45f, ball, 15f, profile, new Func<bool>(() => true) };
            Assert.That((bool)begin.Invoke(thrower, arguments), Is.True);
            ThrowType.GetMethod("CancelWindup").Invoke(thrower, null);
            Assert.That((float)ThrowType.GetProperty("NextThrowAt").GetValue(thrower), Is.Zero,
                "Cancellation must not start the per-scout cooldown.");
            Assert.That((bool)begin.Invoke(thrower, arguments), Is.True,
                "The scout can acquire a new target after a cancelled windup.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(scout);
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void MudGainUsesPercentagePointsAndClampsAtOneHundredPercent()
    {
        GameObject player = CreatePlayer("Mud status player", Vector3.zero);
        player.AddComponent(StatusControllerType);
        Component mud = player.AddComponent(MudControllerType);
        try
        {
            MethodInfo add = MudControllerType.GetMethod("AddMudPercentagePoints");
            Assert.That((bool)add.Invoke(mud, new object[] { 15f }), Is.True);
            Assert.That((float)MudControllerType.GetProperty("Amount").GetValue(mud), Is.EqualTo(0.15f).Within(0.0001f));
            MudControllerType.GetMethod("SetAmount", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(mud, new object[] { 0.95f });
            add.Invoke(mud, new object[] { 15f });
            Assert.That((float)MudControllerType.GetProperty("Amount").GetValue(mud), Is.EqualTo(1f));
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    [UnityTest]
    public IEnumerator LaunchedBallHitsThroughTriggerOnceAndAppliesMudAndHorizontalImpulse()
    {
        Vector3 offset = new Vector3(100f, 50f, 100f);
        GameObject player = CreatePlayer("Projectile target", offset + new Vector3(0f, 0f, 3f));
        player.AddComponent(NetworkObjectType);
        player.AddComponent(PlayerInputType);
        player.AddComponent(StatusControllerType);
        Component mud = player.AddComponent(MudControllerType);
        Component impulseReceiver = player.AddComponent(ImpulseControllerType);
        GameObject waterTrigger = new GameObject("Unrelated water trigger");
        BoxCollider trigger = waterTrigger.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(4f, 4f, 0.5f);
        waterTrigger.transform.position = offset + new Vector3(0f, 1f, 1.5f);

        GameObject prefab = (GameObject)LoadAsset("Assets/Prefabs/New/NPC/BeaverScoutMudball.prefab",
            Type.GetType("UnityEngine.GameObject, UnityEngine.CoreModule"));
        UnityEngine.Object profile = LoadAsset("Assets/ScriptableObjectAssets/New/NPC/BeaverScoutMudImpulse.asset",
            Type.GetType("ExternalImpulseProfileSO, Assembly-CSharp"));
        Component ball = UnityEngine.Object.Instantiate(prefab, offset + new Vector3(0f, 0.9f, 0f), Quaternion.identity)
            .GetComponent(Type.GetType("BeaverScoutMudball, Assembly-CSharp"));
        try
        {
            ball.GetType().GetMethod("ConfigureImpact").Invoke(ball, new object[] { 15f, profile });
            ball.GetType().GetMethod("Initialize").Invoke(ball,
                new object[] { new Vector3(0f, 0f, 9f), null, 3f });
            ball.GetType().GetMethod("Launch").Invoke(ball, new object[] { new Vector3(0f, 0f, 9f) });

            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline &&
                   (float)MudControllerType.GetProperty("Amount").GetValue(mud) < 0.14f)
                yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That((float)MudControllerType.GetProperty("Amount").GetValue(mud), Is.EqualTo(0.15f).Within(0.001f));
            Vector3 applied = (Vector3)ImpulseControllerType.GetProperty("CurrentExternalVelocity").GetValue(impulseReceiver);
            Assert.That(applied.z, Is.GreaterThan(3.4f));
            Assert.That(Mathf.Abs(applied.y), Is.LessThan(0.001f));
            Assert.That(ball == null || !ball.gameObject.activeInHierarchy, Is.True,
                "The first solid collision consumes the projectile exactly once.");
        }
        finally
        {
            if (ball != null) UnityEngine.Object.DestroyImmediate(ball.gameObject);
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(waterTrigger);
        }
    }

    [UnityTest]
    public IEnumerator HostProjectileReplicatesMudAndOwnerImpulseWhileClientColliderStaysDisabled()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The projectile replication fixture requires no pre-existing network session.");

        scope = new PlayModeTestScope("BeaverScoutMudballNetwork");
        Vector3 offset = new Vector3(100f, 50f, 100f);
        GameObject playerPrefab = CreateNetworkPlayerPrefab();
        scope.Own(playerPrefab.transform.parent.gameObject);
        GameObject hostRoot = new GameObject("Mudball host manager");
        GameObject ownerRoot = new GameObject("Mudball remote manager");
        scope.Own(hostRoot);
        scope.Own(ownerRoot);
        ushort port = ReservePort();
        NetworkManager host = CreateManager(hostRoot, playerPrefab, port);
        scope.OwnManager(host);
        NetworkManager owner = CreateManager(ownerRoot, playerPrefab, port);
        scope.OwnManager(owner);

        GameObject ballPrefab = (GameObject)LoadAsset("Assets/Prefabs/New/NPC/BeaverScoutMudball.prefab",
            Type.GetType("UnityEngine.GameObject, UnityEngine.CoreModule"));
        host.AddNetworkPrefab(ballPrefab);
        owner.AddNetworkPrefab(ballPrefab);
        try
        {
            Assert.That(host.StartHost(), Is.True);
            host.SetSingleton();
            yield return Until(() => host.LocalClient != null && host.LocalClient.PlayerObject != null, "host player spawn");

            Assert.That(owner.StartClient(), Is.True);
            owner.SetSingleton();
            yield return Until(() => owner.IsConnectedClient && owner.LocalClient != null &&
                owner.LocalClient.PlayerObject != null && host.ConnectedClientsIds.Count == 2, "remote player spawn");

            NetworkObject victim = host.ConnectedClients[owner.LocalClientId].PlayerObject;
            victim.transform.position = offset + new Vector3(0f, 0f, 3f);
            Physics.SyncTransforms();
            host.SetSingleton();

            UnityEngine.Object profile = LoadAsset("Assets/ScriptableObjectAssets/New/NPC/BeaverScoutMudImpulse.asset",
                Type.GetType("ExternalImpulseProfileSO, Assembly-CSharp"));
            Component ball = UnityEngine.Object.Instantiate(ballPrefab, offset + new Vector3(0f, 0.9f, 0f), Quaternion.identity)
                .GetComponent(Type.GetType("BeaverScoutMudball, Assembly-CSharp"));
            ball.GetType().GetMethod("ConfigureImpact").Invoke(ball, new object[] { 15f, profile });
            ball.GetType().GetMethod("Initialize").Invoke(ball, new object[] { new Vector3(0f, 0f, 9f), null, 3f });
            NetworkObject projectileNetworkObject = ball.GetComponent<NetworkObject>();
            projectileNetworkObject.Spawn(true);
            ball.GetType().GetMethod("Launch").Invoke(ball, new object[] { new Vector3(0f, 0f, 9f) });
            ulong projectileId = projectileNetworkObject.NetworkObjectId;

            yield return Until(() => owner.SpawnManager.SpawnedObjects.ContainsKey(projectileId),
                "remote projectile spawn replication");
            NetworkObject remoteProjectile = owner.SpawnManager.SpawnedObjects[projectileId];
            Assert.That(remoteProjectile.GetComponent<SphereCollider>().enabled, Is.False,
                "The observing client's projectile is a non-colliding visual replica.");
            NetworkObject remoteVictim = owner.SpawnManager.SpawnedObjects[victim.NetworkObjectId];
            yield return Until(() => GetMud(victim.GetComponent(StatusControllerType)) > 0.14f,
                "server applies mud to collided remote player");
            yield return Until(() => ((Vector3)ImpulseControllerType.GetProperty("CurrentExternalVelocity")
                    .GetValue(remoteVictim.GetComponent(ImpulseControllerType))).z > 3.4f,
                "owner receives the horizontal impulse");
            yield return Until(() => GetMud(remoteVictim.GetComponent(StatusControllerType)) > 0.14f,
                "mud status replicates to remote owner");
            Assert.That(GetMud(victim.GetComponent(StatusControllerType)), Is.EqualTo(0.15f).Within(0.001f));
            Assert.That(GetMud(remoteVictim.GetComponent(StatusControllerType)), Is.EqualTo(0.15f).Within(0.001f));
            Assert.That(Mathf.Abs(((Vector3)ImpulseControllerType.GetProperty("CurrentExternalVelocity")
                .GetValue(remoteVictim.GetComponent(ImpulseControllerType))).y), Is.LessThan(0.001f));
        }
        finally
        {
            // PlayModeTestScope tears down host and client after the coroutine completes.
        }
    }

    private static bool CanTarget(Component thrower, GameObject player, float range, bool lineOfSight)
    {
        return (bool)ThrowType.GetMethod("CanTarget").Invoke(thrower, new object[]
        {
            player.GetComponent(PlayerHealthType), player.transform, range, lineOfSight, (LayerMask)~0
        });
    }

    private static float GetMud(Component statuses) =>
        (float)StatusControllerType.GetProperty("MudAmount").GetValue(statuses);

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
        GameObject staging = new GameObject("Mudball player prefab staging");
        staging.SetActive(false);
        GameObject prefab = new GameObject("Mudball network player prefab");
        prefab.transform.SetParent(staging.transform, false);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .SetValue(networkObject, 0x4D554432u);
        networkObject.SetSceneObjectStatus(false);
        prefab.AddComponent<CapsuleCollider>();
        prefab.AddComponent(PlayerHealthType);
        prefab.AddComponent(PlayerInputType);
        prefab.AddComponent(StatusControllerType);
        prefab.AddComponent(MudControllerType);
        prefab.AddComponent(ImpulseControllerType);
        return prefab;
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

    private static IEnumerator Until(Func<bool> predicate, string label)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(predicate(), Is.True, "Timed out: " + label);
    }

    private static UnityEngine.Object LoadAsset(string path, Type type)
    {
        Type assetDatabase = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
        return (UnityEngine.Object)assetDatabase.GetMethod("LoadAssetAtPath", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(string), typeof(Type) }, null).Invoke(null, new object[] { path, type });
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
