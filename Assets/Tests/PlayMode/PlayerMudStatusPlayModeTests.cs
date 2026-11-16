using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;

public sealed class PlayerMudStatusPlayModeTests
{
    private PlayModeTestScope scope;
    private static readonly Type StatusControllerType = Type.GetType("PlayerTemporaryStatusController, Assembly-CSharp");
    private static readonly Type MudControllerType = Type.GetType("PlayerMudStatusController, Assembly-CSharp");
    private static readonly Type DomainKindType = Type.GetType("PlayerDomainStatusKind, Assembly-CSharp");
    private static readonly Type WaterStateType = Type.GetType("WaterExposureState, Assembly-CSharp");

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
    public void FootTravelUsesDistanceAndWaterExposureTakesPrecedence()
    {
        GameObject player = new GameObject("Mud movement sample player");
        try
        {
            object mud = CreateControllers(player, out object statuses);
            SetMud(mud, 0f);
            Assert.That((bool)ApplySample(mud, 4f, 0.1f, 0.01f, "None"), Is.True);
            Assert.That(GetMud(statuses), Is.EqualTo(0.2f).Within(0.0001f), "Four metres gives the same gain regardless of immersion depth or sample time.");

            foreach (string state in new[] { "Wading", "Unsafe", "Exhausted" })
            {
                SetMud(mud, 0.2f);
                Assert.That((bool)ApplySample(mud, 2f, 0.5f, 1f, state), Is.True);
                Assert.That(GetMud(statuses), Is.EqualTo(0.15f).Within(0.0001f), $"{state} removes mud and suppresses overlapping soil gain.");
            }
            Assert.That((bool)ApplySample(mud, 0f, 1f, 1f, "None"), Is.False, "Standing does not add or remove mud.");
            ApplySample(mud, 20f, 20f, 1f, "None");
            Assert.That(GetMud(statuses), Is.EqualTo(1f));
            ApplySample(mud, 20f, 10f, 0f, "Unsafe");
            Assert.That(GetMud(statuses), Is.Zero);
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    [Test]
    public void MudStacksClampsAppliesMultiplierAndRetainsEntryInstance()
    {
        GameObject player = new GameObject("Mud status test player");
        try
        {
            object mud = CreateControllers(player, out object statuses);
            SetMud(mud, 0.5f);
            object entry = GetDomainStatus(statuses, "MudSoiling");
            ulong instance = (ulong)entry.GetType().GetField("InstanceId").GetValue(entry);
            Assert.That(GetMud(statuses), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That((float)StatusControllerType.GetProperty("MovementMultiplier").GetValue(statuses), Is.EqualTo(0.85f).Within(0.0001f));

            MudControllerType.GetMethod("AddTransferredLooseSoil").Invoke(mud, new object[] { 0.5f });
            Assert.That(GetMud(statuses), Is.EqualTo(0.575f).Within(0.0001f));
            entry = GetDomainStatus(statuses, "MudSoiling");
            Assert.That((ulong)entry.GetType().GetField("InstanceId").GetValue(entry), Is.EqualTo(instance));
            SetMud(mud, 5f);
            Assert.That(GetMud(statuses), Is.EqualTo(1f));
            SetMud(mud, 0f);
            Assert.That(GetMud(statuses), Is.Zero);
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    [Test]
    public void MudCoexistsWithAllDomainEntriesAndTheReservedPlacementSlot()
    {
        GameObject player = new GameObject("Mud snapshot capacity test player");
        try
        {
            object mud = CreateControllers(player, out object statuses);
            Assert.That((bool)StatusControllerType.GetMethod("BeginPlacementLock").Invoke(statuses, null), Is.True);
            MethodInfo setDomain = StatusControllerType.GetMethod("SetDomainStatus", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (string name in new[] { "WaterExposure", "WaterUnsupportedTimer", "WaterExhaustionTimer", "ConcreteTrap", "TumbleweedImmunity" })
            {
                object[] args = { Enum.Parse(DomainKindType, name), 0UL, 1d, 0f, 0 };
                Assert.That((bool)setDomain.Invoke(statuses, args), Is.True, name);
            }
            Assert.That((bool)MudControllerType.GetMethod("SetAmount", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(mud, new object[] { 0.25f }), Is.True);
            object snapshot = StatusControllerType.GetMethod("CaptureSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(statuses, null);
            object entries = snapshot.GetType().GetField("Entries").GetValue(snapshot);
            Assert.That((int)entries.GetType().GetProperty("Length").GetValue(entries), Is.EqualTo(7));
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    [Test]
    public void AuthoredPlayerPrefabWiresCatalogAndNonInteractiveOverlay()
    {
        Type assetDatabase = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
        Assert.That(assetDatabase, Is.Not.Null, "This authored asset check runs in the Unity Editor.");
        MethodInfo loadAsset = Array.Find(assetDatabase.GetMethods(BindingFlags.Static | BindingFlags.Public), method =>
            method.Name == "LoadAssetAtPath" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1);
        GameObject prefab = (GameObject)loadAsset.MakeGenericMethod(typeof(GameObject))
            .Invoke(null, new object[] { "Assets/Prefabs/PlayerNew.prefab" });
        Assert.That(prefab, Is.Not.Null);
        Component mud = prefab.GetComponent(MudControllerType);
        Assert.That(mud, Is.Not.Null);
        object catalog = MudControllerType.GetField("catalog", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mud);
        CanvasGroup overlay = (CanvasGroup)MudControllerType
            .GetField("mudOverlay", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mud);
        Assert.That(catalog, Is.Not.Null);
        object profile = catalog.GetType().GetProperty("Mud").GetValue(catalog);
        Assert.That((float)profile.GetType().GetField("soilPercentagePointsPerMetre").GetValue(profile), Is.EqualTo(5f));
        Assert.That((float)profile.GetType().GetField("bucketPercentagePointsPerUnit").GetValue(profile), Is.EqualTo(15f));
        Assert.That((float)profile.GetType().GetField("waterRemovalPercentagePointsPerSecond").GetValue(profile), Is.EqualTo(10f));
        Assert.That((float)profile.GetType().GetField("maximumMovementPenalty").GetValue(profile), Is.EqualTo(0.3f));
        Assert.That((float)profile.GetType().GetField("maximumAlpha").GetValue(profile), Is.EqualTo(0.6f));
        Assert.That(overlay, Is.Not.Null);
        Assert.That(overlay.interactable, Is.False);
        Assert.That(overlay.blocksRaycasts, Is.False);
        Canvas canvas = overlay.GetComponent<Canvas>();
        Assert.That(canvas, Is.Not.Null);
        Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        Assert.That(canvas.sortingOrder, Is.LessThan(0));
        Image image = overlay.GetComponentInChildren<Image>(true);
        Assert.That(image, Is.Not.Null);
        Assert.That(image.raycastTarget, Is.False);
        Assert.That(image.sprite, Is.Not.Null);
    }

    [Test]
    public void BlockedAndTransportedPlayersAreNotEligibleForFootSamples()
    {
        GameObject player = new GameObject("Mud movement eligibility test player");
        GameObject transportObject = new GameObject("Mud movement transport fixture");
        try
        {
            object mud = CreateControllers(player, out object statuses);
            MethodInfo eligible = MudControllerType.GetMethod("IsEligible", BindingFlags.Instance | BindingFlags.NonPublic);
            StatusControllerType.GetMethod("BeginPlacementLock").Invoke(statuses, null);
            Assert.That((bool)eligible.Invoke(mud, null), Is.False, "Movement blocking statuses exclude foot accumulation.");
            StatusControllerType.GetMethod("ResetTemporaryStatuses").Invoke(statuses, null);
            Type transportType = Type.GetType("PlayerTransportCollisionController, Assembly-CSharp");
            Component transport = player.AddComponent(transportType);
            Assert.That((bool)transportType.GetMethod("BeginTransport").Invoke(transport, new object[] { transportObject }), Is.True);
            Assert.That((bool)eligible.Invoke(mud, null), Is.False, "Transported players do not accrue mud from shared vehicle motion.");
            transportType.GetMethod("EndTransport").Invoke(transport, new object[] { transportObject });
            Assert.That((bool)eligible.Invoke(mud, null), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(transportObject);
        }
    }

    [UnityTest]
    public IEnumerator NetworkSnapshotReplicatesMudAndOnlyTheOwningPlayerShowsOverlay()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The mud replication fixture requires no pre-existing network session.");
        scope = new PlayModeTestScope("PlayerMudStatusNetwork");
        GameObject playerPrefab = CreateNetworkPlayerPrefab();
        scope.Own(playerPrefab.transform.parent.gameObject);
        NetworkManager host = null;
        NetworkManager owner = null;
        GameObject hostRoot = null;
        GameObject ownerRoot = null;
        try
        {
            ushort port = ReservePort();
            hostRoot = new GameObject("Mud replication host manager");
            scope.Own(hostRoot);
            host = CreateManager(hostRoot, playerPrefab, port);
            scope.OwnManager(host);
            Assert.That(host.StartHost(), Is.True);
            host.SetSingleton();
            yield return Until(() => host.LocalClient != null && host.LocalClient.PlayerObject != null, "host player spawn");

            ownerRoot = new GameObject("Mud replication owner manager");
            scope.Own(ownerRoot);
            owner = CreateManager(ownerRoot, playerPrefab, port);
            scope.OwnManager(owner);
            Assert.That(owner.StartClient(), Is.True);
            owner.SetSingleton();
            yield return Until(() => owner.IsConnectedClient && owner.LocalClient != null &&
                owner.LocalClient.PlayerObject != null && host.ConnectedClientsIds.Count == 2, "remote owner player spawn");

            NetworkObject hostPlayer = host.LocalClient.PlayerObject;
            NetworkObject ownerOnHost = host.ConnectedClients[owner.LocalClientId].PlayerObject;
            Type mudType = MudControllerType;
            host.SetSingleton();
            Assert.That((bool)mudType.GetMethod("SetAmount", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hostPlayer.GetComponent(mudType), new object[] { 0.5f }), Is.True);
            Assert.That((bool)mudType.GetMethod("SetAmount", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(ownerOnHost.GetComponent(mudType), new object[] { 0.75f }), Is.True);

            ulong hostPlayerId = hostPlayer.NetworkObjectId;
            yield return Until(() => owner.SpawnManager.SpawnedObjects.TryGetValue(hostPlayerId, out NetworkObject remoteHostPlayer) &&
                Mathf.Abs(GetMud(remoteHostPlayer.GetComponent(StatusControllerType)) - 0.5f) < 0.0001f &&
                Mathf.Abs(GetMud(owner.LocalClient.PlayerObject.GetComponent(StatusControllerType)) - 0.75f) < 0.0001f,
                "both mud snapshots replicate to the client");

            object hostMud = hostPlayer.GetComponent(mudType);
            object ownerServerMud = ownerOnHost.GetComponent(mudType);
            NetworkObject remoteHost = owner.SpawnManager.SpawnedObjects[hostPlayerId];
            object remoteHostMud = remoteHost.GetComponent(mudType);
            object ownerLocalMud = owner.LocalClient.PlayerObject.GetComponent(mudType);
            Assert.That(GetMud(hostPlayer.GetComponent(StatusControllerType)), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(GetMud(remoteHost.GetComponent(StatusControllerType)), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(GetMud(ownerOnHost.GetComponent(StatusControllerType)), Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(GetMud(owner.LocalClient.PlayerObject.GetComponent(StatusControllerType)), Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That((float)StatusControllerType.GetProperty("MovementMultiplier").GetValue(hostPlayer.GetComponent(StatusControllerType)),
                Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That((float)StatusControllerType.GetProperty("MovementMultiplier").GetValue(owner.LocalClient.PlayerObject.GetComponent(StatusControllerType)),
                Is.EqualTo(0.775f).Within(0.0001f));
            Assert.That(GetOverlayAlpha(hostMud), Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(GetOverlayAlpha(ownerLocalMud), Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(GetOverlayAlpha(remoteHostMud), Is.Zero);
            Assert.That(GetOverlayAlpha(ownerServerMud), Is.Zero);

            GameObject unspawned = new GameObject("Unspawned mud player while network is live");
            try
            {
                unspawned.AddComponent<NetworkObject>();
                object statuses = unspawned.AddComponent(StatusControllerType);
                object unspawnedMud = unspawned.AddComponent(mudType);
                Assert.That((bool)mudType.GetMethod("SetAmount", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(unspawnedMud, new object[] { 0.4f }), Is.False);
                Assert.That(GetMud(statuses), Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(unspawned); }
        }
        finally
        {
            // PlayModeTestScope waits for transport shutdown before destroying manager roots.
        }
    }

    private static object CreateControllers(GameObject player, out object statuses)
    {
        player.AddComponent<Unity.Netcode.NetworkObject>();
        statuses = player.AddComponent(StatusControllerType);
        return player.AddComponent(MudControllerType);
    }

    private static GameObject CreateNetworkPlayerPrefab()
    {
        GameObject staging = new GameObject("Mud network player prefab staging");
        staging.SetActive(false);
        GameObject prefab = new GameObject("Mud network player prefab");
        prefab.transform.SetParent(staging.transform, false);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .SetValue(networkObject, 0x4D554431u);
        networkObject.SetSceneObjectStatus(false);
        prefab.AddComponent(StatusControllerType);
        Component mud = prefab.AddComponent(MudControllerType);
        GameObject overlayObject = new GameObject("Mud overlay group");
        overlayObject.transform.SetParent(prefab.transform, false);
        CanvasGroup overlay = overlayObject.AddComponent<CanvasGroup>();
        overlay.alpha = 0f; overlay.interactable = false; overlay.blocksRaycasts = false;
        MudControllerType.GetField("mudOverlay", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mud, overlay);
        return prefab;
    }

    private static NetworkManager CreateManager(GameObject root, GameObject playerPrefab, ushort port)
    {
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = playerPrefab, EnableSceneManagement = false };
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

    private static float GetOverlayAlpha(object mud) =>
        ((CanvasGroup)MudControllerType.GetField("mudOverlay", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mud)).alpha;

    private static void SetMud(object mud, float amount) =>
        MudControllerType.GetMethod("SetAmount", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(mud, new object[] { amount });

    private static object ApplySample(object mud, float distance, float elapsed, float immersion, string waterState) =>
        MudControllerType.GetMethod("ApplyMovementSample", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(mud, new[] { (object)distance, elapsed, immersion, Enum.Parse(WaterStateType, waterState) });

    private static float GetMud(object statuses) => (float)StatusControllerType.GetProperty("MudAmount").GetValue(statuses);

    private static object GetDomainStatus(object statuses, string name) =>
        StatusControllerType.GetMethod("GetDomainStatus", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(statuses, new[] { Enum.Parse(DomainKindType, name) });
}
