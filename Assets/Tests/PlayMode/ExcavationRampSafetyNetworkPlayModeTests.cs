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

public sealed class ExcavationRampSafetyNetworkPlayModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private readonly ListWrapper ownedRoots = new();
    private PlayModeTestScope scope;
    private NetworkManager host;
    private NetworkManager owner;
    private NetworkManager lateJoin;
    private GameObject playerPrefab;
    private GameObject bucketPrefab;
    private SiteFixture hostSite;
    private SiteFixture ownerSite;
    private SiteFixture lateSite;
    private FieldInfo gameplayInstance;
    private object previousGameplay;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        scope = new PlayModeTestScope("ExcavationRampSafetyNetwork");
        Type gameplayType = TypeByName("GameplayManager");
        gameplayInstance = gameplayType.GetField("<Instance>k__BackingField", Static);
        previousGameplay = gameplayInstance.GetValue(null);
        scope.RestoreWith(() => gameplayInstance.SetValue(null, previousGameplay));
        GameObject dummyRoot = scope.Own(new GameObject("Inactive GameplayManager network fixture"));
        dummyRoot.SetActive(false);
        dummyRoot.AddComponent(gameplayType);
        gameplayInstance.SetValue(null, dummyRoot.GetComponent(gameplayType));
        yield break;
    }

    private sealed class ListWrapper
    {
        public readonly System.Collections.Generic.List<GameObject> Roots = new();
        public void Own(GameObject root)
        {
            if (root != null && !Roots.Contains(root)) Roots.Add(root);
        }
    }

    private sealed class SiteFixture
    {
        public GameObject BridgeRoot;
        public GameObject SiteRoot;
        public GameObject VolumeRoot;
        public Component Bridge;
        public Component BridgeComponent;
        public Component Site;
        public Component Volume;
        public BoxCollider RampCollider;
        public Component Gameplay;
    }

    [UnityTest]
    public IEnumerator ServerGrantStillChecksDisabledRemoteCapsulesAndLateJoinBeforeEnable()
    {
#if !UNITY_EDITOR
        Assert.Ignore("The network fixture creates an in-process UTP host and clients.");
        yield break;
#else
        playerPrefab = CreatePlayerPrefab();
        bucketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Substances/PortableBucket.prefab");
        Assert.That(bucketPrefab, Is.Not.Null, "The production network bucket prefab is required for this occupancy test.");
        ushort port = ReservePort();
        host = CreateManager("Excavation ramp host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.LocalClient?.PlayerObject != null, "host player spawn");

        owner = CreateManager("Excavation ramp owner", port);
        Assert.That(owner.StartClient(), Is.True);
        owner.SetSingleton();
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null &&
                                 host.ConnectedClientsIds.Count == 2, "owner connection");

        host.SetSingleton();
        MoveAllPlayersAway(host);
        owner.SetSingleton();
        MoveAllPlayersAway(owner);
        host.SetSingleton();
        hostSite = CreateSite("Server foundation");
        RegisterGameplay(hostSite.Gameplay, host, server: true);
        Assert.That(HasFoundationState(hostSite.Gameplay), Is.True,
            "The production server bridge state initializes one component before any snapshot is sent.");
        ownerSite = CreateSite("Owner foundation replica");
        RegisterGameplay(ownerSite.Gameplay, owner, server: false);

        host.SetSingleton();
        GameObject bucket = scope.Own(Object.Instantiate(bucketPrefab,
            hostSite.RampCollider.transform.position + Vector3.right * 0.1f, Quaternion.identity));
        NetworkObject bucketNetworkObject = bucket.GetComponent<NetworkObject>();
        Assert.That(bucketNetworkObject, Is.Not.Null);
        bucketNetworkObject.SetSceneObjectStatus(false);
        bucketNetworkObject.SpawnWithOwnership(owner.LocalClientId);
        yield return Until(() => owner.SpawnManager.SpawnedObjects.ContainsKey(bucketNetworkObject.NetworkObjectId),
            "network bucket replica");

        NetworkObject remoteOwnerOnServer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        CharacterController disabledRemoteCapsule = remoteOwnerOnServer.GetComponent<CharacterController>();
        Assert.That(disabledRemoteCapsule, Is.Not.Null);
        disabledRemoteCapsule.enabled = false;
        NetworkObject hostPlayerOnServer = host.LocalClient.PlayerObject;
        CharacterController disabledHostCapsule = hostPlayerOnServer.GetComponent<CharacterController>();
        disabledHostCapsule.enabled = false;
        host.SetSingleton();
        MoveAllPlayersAway(host);
        bucket.transform.position = hostSite.RampCollider.transform.position + Vector3.right * 0.1f;
        Physics.SyncTransforms();
        Set(hostSite.Site, "nextRampClearanceCheckTime", 0f);
        Call(hostSite.Site, "UpdateFoundationRampClearance");
        Assert.That(Get<bool>(hostSite.Site, "foundationRampClearanceGranted"), Is.False,
            "The production network bucket alone blocks server clearance.");
        Assert.That(hostSite.RampCollider.enabled, Is.False);
        PlaceBucketReplicasAway(bucketNetworkObject.NetworkObjectId);
        yield return Until(() => AreBucketReplicasAway(bucketNetworkObject.NetworkObjectId,
                hostSite.RampCollider.transform.position), "all production bucket replicas leave the ramp");
        Assert.That(OrientedCapsuleOverlapsRamp(remoteOwnerOnServer.transform, disabledRemoteCapsule,
            hostSite.RampCollider), Is.False);
        remoteOwnerOnServer.transform.position = hostSite.RampCollider.transform.position;
        hostPlayerOnServer.transform.position = hostSite.RampCollider.transform.position + Vector3.right * 0.2f;
        Physics.SyncTransforms();
        Assert.That(OrientedCapsuleOverlapsRamp(remoteOwnerOnServer.transform, disabledRemoteCapsule,
            hostSite.RampCollider), Is.True, "The disabled remote owner's actual capsule occupies the ramp.");
        Assert.That(OrientedCapsuleOverlapsRamp(hostPlayerOnServer.transform, disabledHostCapsule,
            hostSite.RampCollider), Is.True, "The disabled host player's actual capsule occupies the ramp.");
        host.SetSingleton();
        SetGameplaySingleton(hostSite.Gameplay);
        Set(hostSite.Site, "nextRampClearanceCheckTime", 0f);
        Call(hostSite.Site, "UpdateFoundationRampClearance");
        Assert.That(Get<bool>(hostSite.Site, "foundationRampClearanceGranted"), Is.False,
            "Both disabled player capsules independently block server clearance after the bucket leaves.");

        lateJoin = CreateManager("Excavation ramp late join", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        lateJoin.SetSingleton();
        yield return Until(() => lateJoin.IsConnectedClient && lateJoin.LocalClient?.PlayerObject != null &&
                                 host.ConnectedClientsIds.Count == 3, "late-join connection");
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(bucketNetworkObject.NetworkObjectId),
            "late-join production bucket replica");
        lateJoin.SetSingleton();
        MoveAllPlayersAway(lateJoin);
        PlaceBucketReplicasAway(bucketNetworkObject.NetworkObjectId);
        host.SetSingleton();
        SetGameplaySingleton(hostSite.Gameplay);
        yield return Until(() => AreBucketReplicasAway(bucketNetworkObject.NetworkObjectId,
                hostSite.RampCollider.transform.position), "all late-join bucket replicas remain outside the ramp");
        lateSite = CreateSite("Late-join foundation");
        RegisterGameplay(lateSite.Gameplay, lateJoin, server: false);
        RequestSnapshot(lateSite.Gameplay, lateJoin);
        host.SetSingleton();
        SetGameplaySingleton(hostSite.Gameplay);
        float snapshotDeadline = Time.realtimeSinceStartup + 10f;
        while (!HasFoundationState(lateSite.Gameplay) && Time.realtimeSinceStartup < snapshotDeadline) yield return null;
        Assert.That(HasFoundationState(lateSite.Gameplay), Is.True,
            $"The real request/response delivers the pending full-state snapshot. hostCount={((IList)Get(hostSite.Gameplay, "bridgeComponentStates")).Count}, lateCount={((IList)Get(lateSite.Gameplay, "bridgeComponentStates")).Count}, lateApplied={Get<bool>(lateSite.Gameplay, "hasAppliedInitialNetworkBridgeState")}, hostRegistered={Get<bool>(hostSite.Gameplay, "networkMessagingRegistered")}, lateRegistered={Get<bool>(lateSite.Gameplay, "networkMessagingRegistered")}, connected={host.ConnectedClientsIds.Count}.");
        Assert.That(HasFoundationGrant(lateSite.Gameplay, false), Is.True,
            "The received late-join snapshot is still pending and unapproved.");
        Assert.That(lateSite.RampCollider.enabled, Is.False,
            "A real late-join full-state snapshot carries the pending, unapproved foundation state.");

        host.SetSingleton();
        SetGameplaySingleton(hostSite.Gameplay);
        remoteOwnerOnServer.transform.position = new Vector3(80f, 30f, 80f);
        Physics.SyncTransforms();
        Set(hostSite.Site, "nextRampClearanceCheckTime", 0f);
        Call(hostSite.Site, "UpdateFoundationRampClearance");
        Assert.That(Get<bool>(hostSite.Site, "foundationRampClearanceGranted"), Is.False,
            "The host player's capsule alone continues blocking after the remote player leaves.");
        hostPlayerOnServer.transform.position = new Vector3(90f, 30f, 90f);
        Physics.SyncTransforms();
        Set(hostSite.Site, "nextRampClearanceCheckTime", 0f);
        Call(hostSite.Site, "UpdateFoundationRampClearance");
        bool clearAfterAllLeave = (bool)hostSite.Volume.GetType().GetMethod("IsExitRampVolumeClear").Invoke(hostSite.Volume, null);
        Assert.That(Get<bool>(hostSite.Site, "foundationRampClearanceGranted"), Is.True,
            $"The authoritative server grants once both players and the already-cleared bucket have left. DirectClear={clearAfterAllLeave}; hostPlayer={hostPlayerOnServer.transform.position}; remotePlayer={remoteOwnerOnServer.transform.position}; bucket={bucket.transform.position}; candidates={DescribeRampOccupants(hostSite.RampCollider)}");
        Assert.That(hostSite.RampCollider.enabled, Is.True);
        yield return Until(() => HasFoundationGrant(lateSite.Gameplay, true), "approved full-state broadcast");
        Assert.That(Get<bool>(lateSite.Site, "foundationRampClearanceGranted"), Is.True,
            "The production bridge full-state broadcast replicates the server's clearance grant.");

        NetworkObject localLatePlayer = lateJoin.LocalClient.PlayerObject;
        CharacterController disabledLateCapsule = localLatePlayer.GetComponent<CharacterController>();
        disabledLateCapsule.enabled = false;
        Behaviour transformSync = localLatePlayer.GetComponent("ClientNetworkTransform") as Behaviour;
        if (transformSync != null) transformSync.enabled = false;
        localLatePlayer.transform.position = new Vector3(100f, 30f, 100f);

        // Exercise an actual replicated grant revocation followed by a fresh grant.
        // The client was clear and ready, then becomes occupied while the server remains clear.
        host.SetSingleton();
        SetGameplaySingleton(hostSite.Gameplay);
        MoveAllPlayersAway(host);
        SetStage(hostSite.Site, "Digging");
        Call(hostSite.Site, "ApplyConcreteVisualState");
        Call(hostSite.Gameplay, "NotifyConstructionSiteStateChanged", hostSite.Site);
        yield return Until(() => HasFoundationState(lateSite.Gameplay) && HasFoundationBit(lateSite.Gameplay, false),
            "server grant revocation reaches late joiner");

        localLatePlayer.transform.position = lateSite.RampCollider.transform.position;
        SetStage(hostSite.Site, "ConcreteDrying");
        Call(hostSite.Site, "ApplyConcreteVisualState");
        Set(hostSite.Site, "nextRampClearanceCheckTime", 0f);
        Call(hostSite.Site, "UpdateFoundationRampClearance");
        Assert.That(Get<bool>(hostSite.Site, "foundationRampClearanceGranted"), Is.True);
        yield return Until(() => HasFoundationGrant(lateSite.Gameplay, true), "server regrant reaches late joiner");
        Assert.That(lateSite.RampCollider.enabled, Is.False,
            "A false-to-true replicated grant cannot reuse stale local permission while a disabled local player blocks.");

        localLatePlayer.transform.position = new Vector3(100f, 30f, 100f);
        lateJoin.SetSingleton();
        Set(lateSite.Site, "nextRampClearanceCheckTime", 0f);
        Call(lateSite.Site, "UpdateFoundationRampClearance");
        Assert.That(lateSite.RampCollider.enabled, Is.True,
            "The late joiner activates the ramp only after its local blocker leaves.");
        localLatePlayer.transform.position = lateSite.RampCollider.transform.position;
        Call(lateSite.Site, "ApplyConcreteVisualState");
        Assert.That(lateSite.RampCollider.enabled, Is.True,
            "Repeated visual-state application cannot revoke a grant from a player already standing on the ready ramp.");

        // Force a new OFF-to-ON request, then prove disconnecting a blocking peer
        // removes its spawned player object and allows the host to continue safely.
        host.SetSingleton();
        SetStage(hostSite.Site, "Digging");
        Call(hostSite.Site, "ApplyConcreteVisualState");
        SetStage(hostSite.Site, "ConcreteDrying");
        Call(hostSite.Site, "ApplyConcreteVisualState");
        remoteOwnerOnServer.transform.position = hostSite.RampCollider.transform.position;
        Physics.SyncTransforms();
        Set(hostSite.Site, "nextRampClearanceCheckTime", 0f);
        Call(hostSite.Site, "UpdateFoundationRampClearance");
        Assert.That(hostSite.RampCollider.enabled, Is.False);
        owner.Shutdown();
        yield return Until(() => host.ConnectedClientsIds.Count == 2, "blocking owner disconnect");
        host.SetSingleton();
        Set(hostSite.Site, "nextRampClearanceCheckTime", 0f);
        Call(hostSite.Site, "UpdateFoundationRampClearance");
        Assert.That(hostSite.RampCollider.enabled, Is.True,
            "The host rechecks after a blocking peer disconnects and activates only after that player object is gone.");
#endif
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (scope == null) yield break;
        foreach (GameObject root in ownedRoots.Roots) scope.Own(root);
        if (host != null) scope.OwnManager(host);
        if (owner != null) scope.OwnManager(owner);
        if (lateJoin != null) scope.OwnManager(lateJoin);
        PlayModeTestScope cleanupScope = scope;
        yield return cleanupScope.Cleanup();
        scope = null;
        ownedRoots.Roots.Clear();
        host = owner = lateJoin = null;
        playerPrefab = null;
        hostSite = ownerSite = lateSite = null;
        bucketPrefab = null;
        gameplayInstance = null;
        previousGameplay = null;
        cleanupScope.AssertCleanupSucceeded();
    }

    private SiteFixture CreateSite(string label)
    {
        SiteFixture fixture = new SiteFixture
        {
            BridgeRoot = scope.Own(new GameObject(label + " bridge")),
        };
        fixture.BridgeRoot.SetActive(false);
        fixture.Bridge = fixture.BridgeRoot.AddComponent(TypeByName("Bridge"));
        GameObject holder = new GameObject(label + " component holder");
        holder.transform.SetParent(fixture.BridgeRoot.transform, false);
        fixture.Bridge.GetType().GetField("bridgeComponentHolder", Instance).SetValue(fixture.Bridge, holder);
        fixture.SiteRoot = scope.Own(new GameObject(label));
        fixture.SiteRoot.transform.SetParent(holder.transform, false);
        fixture.BridgeComponent = fixture.SiteRoot.AddComponent(TypeByName("BridgeComponent"));
        ((Behaviour)fixture.BridgeComponent).enabled = false;
        Set(fixture.BridgeComponent, "componentID", 0);
        Object definition = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenFoundation.asset");
        Assert.That(definition, Is.Not.Null);
        Set(fixture.BridgeComponent, "bridgeComponentSO", definition);
        fixture.Site = fixture.SiteRoot.AddComponent(TypeByName("BridgeConstructionSite"));
        fixture.VolumeRoot = scope.Own(new GameObject(label + " volume"));
        fixture.VolumeRoot.transform.SetParent(holder.transform, false);
        fixture.VolumeRoot.transform.position = new Vector3(0f, 25f, 0f);
        GameObject supportRoot = new GameObject(label + " hard soil support");
        supportRoot.transform.SetParent(fixture.VolumeRoot.transform, false);
        supportRoot.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        BoxCollider supportCollider = supportRoot.AddComponent<BoxCollider>();
        supportCollider.size = new Vector3(6.5f, 0.12f, 4f);
        MeshRenderer supportRenderer = supportRoot.AddComponent<MeshRenderer>();
        GameObject fieldRoot = new GameObject(label + " loose soil field", typeof(MeshFilter), typeof(MeshRenderer));
        fieldRoot.transform.SetParent(fixture.VolumeRoot.transform, false);
        Component field = fieldRoot.AddComponent(TypeByName("FoundationLooseSoilField"));
        Call(field, "Initialize", 6.5f, 4f, 0.2f, 0.4f, 0.4f, 6f);
        Material looseMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat");
        Assert.That(looseMaterial, Is.Not.Null);
        Call(field, "SetSurfaceState", false, looseMaterial);
        GameObject ramp = new GameObject(label + " exit ramp");
        ramp.transform.SetParent(fixture.VolumeRoot.transform, false);
        fixture.RampCollider = ramp.AddComponent<BoxCollider>();
        fixture.RampCollider.size = new Vector3(1f, 0.2f, 4f);
        ramp.AddComponent<MeshRenderer>();
        fixture.Volume = fixture.VolumeRoot.AddComponent(TypeByName("FoundationExcavationVolume"));
        Set(fixture.Volume, "useLooseSoilHeightField", true);
        Set(fixture.Volume, "exitRampCollider", fixture.RampCollider);
        Set(fixture.Volume, "exitRampRenderer", ramp.GetComponent<MeshRenderer>());
        Set(fixture.Volume, "soilSurface", supportRoot.transform);
        Set(fixture.Volume, "soilSupportCollider", supportCollider);
        Set(fixture.Volume, "looseSoilField", field);
        Set(fixture.Volume, "constructionSite", fixture.Site);
        Set(fixture.Volume, "soilRenderer", supportRenderer);
        Set(fixture.Volume, "loosenedSoilMaterial", looseMaterial);
        Set(fixture.Volume, "surfaceStartLocalY", 0.02f);
        Set(fixture.Volume, "concreteFootprintSize", new Vector2(6.5f, 4f));
        Assert.That((bool)fixture.Volume.GetType().GetProperty("HasValidLooseSoilConfiguration", Instance)
            .GetValue(fixture.Volume), Is.True, "The network ramp safety fixture must use a valid loose-soil volume configuration.");
        Set(fixture.Site, "excavationVolume", fixture.Volume);
        fixture.BridgeRoot.SetActive(true);
        Set(fixture.Site, "currentStage", Enum.Parse(TypeByName("BridgeConstructionStage"), "ConcreteDrying"));
        Set(fixture.Site, "concreteLoadsPoured", 1);
        Set(fixture.Site, "concreteFailureState", Enum.Parse(TypeByName("FoundationConcreteFailureState"), "None"));
        fixture.Gameplay = CreateGameplay(label + " bridge state", fixture.Bridge);
        Call(fixture.Site, "ApplyConcreteVisualState");
        return fixture;
    }

    private Component CreateGameplay(string label, Component bridge)
    {
        Type gameplayType = TypeByName("GameplayManager");
        GameObject root = scope.Own(new GameObject(label));
        root.SetActive(false);
        Component gameplay = root.AddComponent(gameplayType);
        Set(gameplay, "bridge", bridge);
        Type rowType = gameplayType.GetField("bridgeComponentDataArray", Instance).FieldType.GetElementType();
        Array rows = Array.CreateInstance(rowType, 1);
        object row = Activator.CreateInstance(rowType);
        Set(row, "bridgeComponentSO", Get(bridge.GetComponentInChildren(TypeByName("BridgeComponent"), true), "bridgeComponentSO"));
        rows.SetValue(row, 0);
        Set(gameplay, "bridgeComponentDataArray", rows);
        Type stageType = gameplayType.GetField("bridgeBuildingStages", Instance).FieldType.GetElementType();
        Array stages = Array.CreateInstance(stageType, 1);
        object stage = Activator.CreateInstance(stageType);
        Set(stage, "bridgeComponentDataIndexes", new[] { 0 });
        stages.SetValue(stage, 0);
        Set(gameplay, "bridgeBuildingStages", stages);
        Set(gameplay, "bridgeProgressInitialized", true);
        Call(gameplay, "CacheBridgeComponents");
        return gameplay;
    }

    private static void RegisterGameplay(Component gameplay, NetworkManager manager, bool server)
    {
        manager.SetSingleton();
        SetGameplaySingleton(gameplay);
        Call(gameplay, "EnsureNetworkMessagingRegistered");
        if (server) Call(gameplay, "InitializeServerBridgeState");
    }

    private static void RequestSnapshot(Component gameplay, NetworkManager manager)
    {
        manager.SetSingleton();
        SetGameplaySingleton(gameplay);
        Call(gameplay, "RequestFullBridgeStateFromServer");
    }

    private NetworkManager CreateManager(string label, ushort port)
    {
        GameObject root = scope.Own(new GameObject(label));
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = playerPrefab,
            EnableSceneManagement = false
        };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = bucketPrefab });
        scope.OwnManager(manager);
        return manager;
    }

    private GameObject CreatePlayerPrefab()
    {
        GameObject staging = scope.Own(new GameObject("Ramp safety inactive player template"));
        staging.SetActive(false);
        GameObject prefab = new GameObject("Ramp safety network player");
        prefab.transform.SetParent(staging.transform, false);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", Instance).SetValue(networkObject, 0x52534E54u);
        networkObject.SetSceneObjectStatus(false);
        CharacterController capsule = prefab.AddComponent<CharacterController>();
        capsule.center = new Vector3(0f, 1f, 0f);
        capsule.height = 2f;
        capsule.radius = 0.35f;
        ownedRoots.Own(prefab);
        return prefab;
    }

    private static void MoveAllPlayersAway(NetworkManager manager)
    {
        foreach (NetworkObject player in manager.SpawnManager.SpawnedObjectsList)
        {
            if (player == null || !player.IsPlayerObject) continue;
            CharacterController capsule = player.GetComponent<CharacterController>();
            if (capsule != null) capsule.enabled = false;
            player.transform.position = new Vector3(60f + player.OwnerClientId * 10f, 30f, 60f);
        }
        Physics.SyncTransforms();
    }

    private static bool OrientedCapsuleOverlapsRamp(Transform player, CharacterController capsule, Collider ramp)
    {
        BoxCollider box = ramp as BoxCollider;
        Assert.That(box, Is.Not.Null, "The synthetic ramp occupancy fixture uses an oriented box collider.");
        Vector3 worldCenter = player.TransformPoint(capsule.center);
        float scale = Mathf.Max(Mathf.Abs(player.lossyScale.x), Mathf.Abs(player.lossyScale.z));
        float radius = capsule.radius * scale;
        float height = Mathf.Max(2f * radius, capsule.height * Mathf.Abs(player.lossyScale.y));
        Vector3 axis = player.up;
        Vector3 endpointOffset = axis * Mathf.Max(0f, height * 0.5f - radius);
        Vector3 localStart = box.transform.InverseTransformPoint(worldCenter - endpointOffset);
        Vector3 localEnd = box.transform.InverseTransformPoint(worldCenter + endpointOffset);
        Vector3 halfSize = Vector3.Scale(box.size * 0.5f, Abs(box.transform.lossyScale));
        float low = 0f;
        float high = 1f;
        for (int iteration = 0; iteration < 24; iteration++)
        {
            float first = (2f * low + high) / 3f;
            float second = (low + 2f * high) / 3f;
            if (SegmentPointAabbDistanceSquared(Vector3.Lerp(localStart, localEnd, first), halfSize) <=
                SegmentPointAabbDistanceSquared(Vector3.Lerp(localStart, localEnd, second), halfSize)) high = second;
            else low = first;
        }
        float minimumDistanceSquared = Mathf.Min(
            SegmentPointAabbDistanceSquared(localStart, halfSize),
            SegmentPointAabbDistanceSquared(localEnd, halfSize),
            SegmentPointAabbDistanceSquared(Vector3.Lerp(localStart, localEnd, (low + high) * 0.5f), halfSize));
        return minimumDistanceSquared <= radius * radius + 0.0001f;
    }

    private static Vector3 Abs(Vector3 value) =>
        new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

    private static float SegmentPointAabbDistanceSquared(Vector3 point, Vector3 halfSize)
    {
        Vector3 closest = new Vector3(Mathf.Clamp(point.x, -halfSize.x, halfSize.x),
            Mathf.Clamp(point.y, -halfSize.y, halfSize.y), Mathf.Clamp(point.z, -halfSize.z, halfSize.z));
        return (point - closest).sqrMagnitude;
    }

    private static string DescribeRampOccupants(Collider ramp)
    {
        BoxCollider box = (BoxCollider)ramp;
        Vector3 center = box.transform.TransformPoint(box.center);
        Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, Abs(box.transform.lossyScale)) + Vector3.one * 0.02f;
        Collider[] overlaps = Physics.OverlapBox(center, halfExtents, box.transform.rotation,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        List<string> items = new List<string>();
        foreach (Collider candidate in overlaps)
        {
            if (candidate == ramp) continue;
            items.Add($"{candidate.name}(enabled={candidate.enabled},layer={candidate.gameObject.layer},rb={candidate.attachedRigidbody != null},pos={candidate.transform.position})");
        }
        return string.Join(";", items);
    }

    private void PlaceBucketReplicasAway(ulong networkObjectId)
    {
        Vector3 outside = new Vector3(95f, 30f, 95f);
        if (owner != null && owner.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject ownerReplica))
        {
            owner.SetSingleton();
            ownerReplica.transform.position = outside;
        }
        if (host != null && host.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject serverReplica))
            serverReplica.transform.position = outside;
        if (lateJoin != null && lateJoin.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject lateReplica))
            lateReplica.transform.position = outside;
        Physics.SyncTransforms();
    }

    private bool AreBucketReplicasAway(ulong networkObjectId, Vector3 rampCenter)
    {
        NetworkManager[] managers = { host, owner, lateJoin };
        foreach (NetworkManager manager in managers)
        {
            if (manager == null || manager.SpawnManager == null ||
                !manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject replica)) continue;
            if (Vector3.Distance(replica.transform.position, rampCenter) < 10f) return false;
        }
        return true;
    }

    private static bool HasFoundationGrant(Component gameplay, bool expected)
    {
        IList states = gameplay.GetType().GetField("bridgeComponentStates", Instance).GetValue(gameplay) as IList;
        if (states == null || states.Count == 0) return false;
        object state = states[0];
        Type stateType = TypeByName("BridgeComponentNetworkState");
        int stage = (int)stateType.GetField("constructionStage").GetValue(state);
        int drying = (int)Enum.Parse(TypeByName("BridgeConstructionStage"), "ConcreteDrying");
        return stage == drying &&
               (bool)stateType.GetField("foundationRampClearanceGranted").GetValue(state) == expected;
    }

    private static bool HasFoundationState(Component gameplay)
    {
        IList states = gameplay.GetType().GetField("bridgeComponentStates", Instance).GetValue(gameplay) as IList;
        return states != null && states.Count > 0;
    }

    private static bool HasFoundationBit(Component gameplay, bool expected)
    {
        IList states = gameplay.GetType().GetField("bridgeComponentStates", Instance).GetValue(gameplay) as IList;
        if (states == null || states.Count == 0) return false;
        return (bool)TypeByName("BridgeComponentNetworkState").GetField("foundationRampClearanceGranted")
                   .GetValue(states[0]) == expected;
    }

    private static IEnumerator Until(Func<bool> condition, string label)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out waiting for " + label + ".");
    }

    private static ushort ReservePort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        ushort port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static Type TypeByName(string name) => Type.GetType(name + ", Assembly-CSharp");
    private static object Get(object target, string name) => target.GetType().GetField(name, Instance)?.GetValue(target);
    private static T Get<T>(object target, string name) => (T)Get(target, name);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    private static void SetStage(object site, string stage) => Set(site, "currentStage", Enum.Parse(TypeByName("BridgeConstructionStage"), stage));
    private static void SetGameplaySingleton(Component gameplay) =>
        TypeByName("GameplayManager").GetField("<Instance>k__BackingField", Static).SetValue(null, gameplay);

    private static object Call(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, Instance);
        Assert.That(method, Is.Not.Null, "Missing method " + methodName + " on " + target.GetType().Name);
        object[] arguments = args;
        object result = method.Invoke(target, arguments);
        return result;
    }
}
