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
using Type = System.Type;

public sealed class LooseSoilStage4NetworkPlayModeTests
{
    private const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
    private readonly List<GameObject> roots = new();
    private readonly HashSet<GameObject> trackedRoots = new();
    private readonly List<NetworkManager> managers = new();
    private NetworkManager previous, host, owner, late;
    private Component previousGameplay, serverGameplay, ownerGameplay, lateGameplay;
    private GameObject playerPrefab, bucketPrefab;
    private Object soil;
    private Peer serverPeer, ownerPeer, latePeer;
    private Type playerInteractionType, containerType, resourceType, fieldType, siteType, gameplayType, bridgeType,
        bridgeComponentType, volumeType;

    private sealed class Peer
    {
        public GameObject BridgeRoot;
        public Component[] Sites = new Component[2];
        public Component[] Fields = new Component[2];
    }

    [UnityTest]
    public IEnumerator MovingHeldBucketsCollectAuthoritativelyAndReplicateShapesToRemoteAndLateJoin()
    {
#if !UNITY_EDITOR
        Assert.Ignore("The UTP integration fixture uses production assets through AssetDatabase.");
        yield break;
#else
        previous = NetworkManager.Singleton;
        if (previous != null && previous.IsListening)
            Assert.Ignore("The isolated Stage4 NGO test requires no pre-existing network session.");
        gameplayType = Type.GetType("GameplayManager, Assembly-CSharp");
        bridgeType = Type.GetType("Bridge, Assembly-CSharp");
        bridgeComponentType = Type.GetType("BridgeComponent, Assembly-CSharp");
        siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        containerType = Type.GetType("PortableSubstanceContainer, Assembly-CSharp");
        resourceType = Type.GetType("BaseResourceNew, Assembly-CSharp");
        playerInteractionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        Assert.That(new[] { gameplayType, bridgeType, bridgeComponentType, siteType, volumeType, fieldType,
            containerType, resourceType, playerInteractionType }, Has.None.Null);
        previousGameplay = GetGameplaySingleton();
        bucketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/PortableBucket.prefab");
        soil = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
        Assert.That(bucketPrefab, Is.Not.Null); Assert.That(soil, Is.Not.Null);
        playerPrefab = CreatePlayerPrefab();

        ushort port = ReservePort();
        host = CreateManager("Stage4 soil host", port);
        Assert.That(host.StartHost(), Is.True);
        Assert.That(((NetworkObject)playerPrefab.GetComponent(typeof(NetworkObject))).IsSpawned, Is.False,
            "The inactive synthetic prefab template must never be spawned into the test session.");
        host.SetSingleton();
        yield return Until(() => host.LocalClient?.PlayerObject != null, "host player");
        owner = CreateManager("Stage4 soil remote owner", port);
        Assert.That(owner.StartClient(), Is.True);
        owner.SetSingleton();
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null &&
            host.ConnectedClientsIds.Count == 2, "remote owner player");
        host.SetSingleton();
        DisableFixturePlayer(host.LocalClient.PlayerObject);
        DisableFixturePlayer(host.ConnectedClients[owner.LocalClientId].PlayerObject);
        DisableFixturePlayer(owner.LocalClient.PlayerObject);
        host.LocalClient.PlayerObject.transform.position = new Vector3(0f, .08f, 0f);
        owner.LocalClient.PlayerObject.transform.position = new Vector3(0f, .08f, 0f);
        yield return Until(() => Mathf.Abs(host.ConnectedClients[owner.LocalClientId].PlayerObject.transform.position.y - .08f) < .1f,
            "remote capsule foot point reaches soil height");
        host.SetSingleton();

        serverPeer = CreatePeer("server", active: true);
        ownerPeer = CreatePeer("remote", active: false);
        serverGameplay = CreateGameplay("Stage4 host bridge state", serverPeer);
        ownerGameplay = CreateGameplay("Stage4 remote bridge state", ownerPeer);
        foreach (Component field in ownerPeer.Fields) ((Behaviour)field).enabled = false;
        RegisterGameplay(ownerGameplay, owner, server: false);
        RegisterGameplay(serverGameplay, host, server: true);

        Assert.That(TryFind(serverPeer, new Vector3(0f, .08f, 0f), out int collectionIndex), Is.True);
        Component activeSite = serverPeer.Sites[collectionIndex];
        Component activeField = serverPeer.Fields[collectionIndex];
        object heightField = Get(activeField, "HeightField");
        float initialUnits = (float)Prop(heightField, "TotalUnits");
        object initialShape = Call(activeField, "CaptureSnapshot");

        NetworkObject remoteBucketObject = SpawnBucket(new Vector3(40f, 2f, 0f));
        NetworkObject hostBucketObject = SpawnBucket(new Vector3(41f, 2f, 0f));
        ulong remoteBucketId = remoteBucketObject.NetworkObjectId;
        ulong hostBucketId = hostBucketObject.NetworkObjectId;
        yield return Until(() => owner.SpawnManager.SpawnedObjects.ContainsKey(remoteBucketId) &&
            owner.SpawnManager.SpawnedObjects.ContainsKey(hostBucketId), "both bucket mirrors");

        NetworkObject ownerServerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        NetworkObject hostPlayer = host.LocalClient.PlayerObject;
        Assert.That(CompletePickup(remoteBucketObject.GetComponent(resourceType), ownerServerPlayer, owner.LocalClientId), Is.True,
            "The remote owner must actually pick up the network bucket through the server pickup transaction.");
        Assert.That(CompletePickup(hostBucketObject.GetComponent(resourceType), hostPlayer, NetworkManager.ServerClientId), Is.True,
            "The host must actually pick up the second bucket through the server pickup transaction.");
        yield return Until(() => IsHeld(remoteBucketObject.GetComponent(resourceType), owner.LocalClientId) &&
            IsHeld(hostBucketObject.GetComponent(resourceType), NetworkManager.ServerClientId), "both server holder assignments");
        yield return Until(() => (bool)Find(owner, remoteBucketId).GetComponent(resourceType).GetType()
            .GetProperty("IsPickedUp").GetValue(Find(owner, remoteBucketId).GetComponent(resourceType)),
            "remote pickup presentation");

        Component ownerBucket = Find(owner, remoteBucketId).GetComponent(containerType);
        owner.SetSingleton();
        Assert.That(Add(ownerBucket, soil, .25f), Is.Zero,
            "The live remote bucket must reject a direct client contents mutation while a valid server holder exists.");
        Component eligibleClientField = ownerPeer.Fields[collectionIndex];
        ((Behaviour)eligibleClientField).enabled = true;
        Call(eligibleClientField, "SetSurfaceState", true,
            UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat"));
        object clientShapeBeforeSpoof = Capture(eligibleClientField);
        float spoofed = (float)Call(ownerPeer.Sites[collectionIndex], "TryCollectLooseSoil", ownerBucket, soil,
            new Vector3(0f, .08f, 0f), .6f, .25f, .05f);
        Assert.That(spoofed, Is.Zero,
            "A direct client source transaction is refused even when the local client field and bucket are eligible.");
        Assert.That(Matches(eligibleClientField, clientShapeBeforeSpoof), Is.True);
        ((Behaviour)eligibleClientField).enabled = false;
        owner.SetSingleton(); host.SetSingleton();

        // Move both real player objects through the same active field. Use bounded displacements
        // and retain the host singleton while server Update performs each collection tick.
        Vector3 ownerStart = owner.LocalClient.PlayerObject.transform.position;
        Vector3 serverOwnerStart = ownerServerPlayer.transform.position;
        for (int i = 0; i < 6; i++)
        {
            owner.LocalClient.PlayerObject.transform.position += Vector3.forward * .12f;
            hostPlayer.transform.position += Vector3.forward * .12f;
            yield return new WaitForSeconds(.12f);
        }
        yield return new WaitForSeconds(.25f);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        Component serverRemoteBucket = remoteBucketObject.GetComponent(containerType);
        Component serverHostBucket = hostBucketObject.GetComponent(containerType);
        float remoteUnits = Units(serverRemoteBucket);
        float hostUnits = Units(serverHostBucket);
        Assert.That(remoteUnits, Is.GreaterThan(0f), "Remote-owner motion must collect on the server.");
        Assert.That(hostUnits, Is.GreaterThan(0f), "Host-owner motion must collect on the server.");
        float finalUnits = (float)Prop(heightField, "TotalUnits");
        float collected = remoteUnits + hostUnits;
        Assert.That(initialUnits - finalUnits, Is.EqualTo(collected).Within(.00001f),
            "Spatial field loss must match both authoritative bucket contents, with no duplicate material.");
        Assert.That(Matches(activeField, initialShape), Is.False, "The actual automatic Update path must change the source heightfield.");
        Assert.That((float)Prop(activeSite, "RemovedSoilUnits"), Is.EqualTo(initialUnits - finalUnits).Within(.00001f));
        Assert.That(Vector3.Distance(ownerStart, owner.LocalClient.PlayerObject.transform.position), Is.GreaterThan(.5f));
        Assert.That(Vector3.Distance(serverOwnerStart, ownerServerPlayer.transform.position), Is.GreaterThan(.5f),
            "The remote owner’s movement must reach the server player object before it is credited.");

        NetworkManager ownerManager = owner;
        Component ownerBucketNow = Find(ownerManager, remoteBucketId).GetComponent(containerType);
        // Collection has settled; keep the host NetworkManager selected while the
        // receiver fixture applies its already-queued state trailer.
        SetGameplaySingleton(ownerGameplay);
        yield return Until(() => Mathf.Abs(Units(ownerBucketNow) - remoteUnits) < .00001f &&
            Matches(ownerPeer.Fields[collectionIndex], Capture(activeField)), "remote bucket units and spatial shape");
        Assert.That((float)Prop(ownerPeer.Sites[collectionIndex], "RemovedSoilUnits"),
            Is.EqualTo((float)Prop(activeSite, "RemovedSoilUnits")).Within(.0001f),
            "The remote site's construction counter accompanies the replicated spatial shape.");
        Assert.That(Matches(ownerPeer.Fields[1 - collectionIndex], Capture(serverPeer.Fields[1 - collectionIndex])), Is.True,
            "The unchanged second pit remains independently represented on the remote peer.");

        // Exercise the variable-float schedule through the same real server API and spawned
        // bucket fixture. The second foundation is untouched by the movement phase above.
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        Component cycleSite = serverPeer.Sites[1 - collectionIndex];
        Component cycleField = serverPeer.Fields[1 - collectionIndex];
        Component cycleBucket = hostBucketObject.GetComponent(containerType);
        Call(cycleBucket, "Empty", false, Vector3.zero);
        Call(cycleField, "ResetToInitial");
        SetSoilRemoval(serverPeer, 1 - collectionIndex);
        System.Random schedule = new(613);
        float clock = 1000f;
        float previousClock = clock;
        double networkedTotal = 0d;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            if (cycle > 0)
            {
                Assert.That(Prop(cycleSite, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
                object workflow = Get(Get(cycleSite, "bridgeComponent"), "bridgeComponentSO");
                workflow = Get(workflow, "constructionWorkflow");
                Assert.That((bool)Call(cycleSite, "TryApplyToolWork", Prop(workflow, "DiggingTool"),
                    Prop(workflow, "LooseningProgressPerCycle"), -1), Is.True);
            }

            for (int load = 0; load < 2; load++)
            {
                int calls = 0;
                while ((float)Call(cycleBucket, "GetAvailableCapacity", soil) > 0f && calls < 64)
                {
                    clock += .28f + (float)schedule.NextDouble() * .19f;
                    float elapsed = clock - previousClock;
                    previousClock = clock;
                    float request = Mathf.Min(.25f, .5f * elapsed);
                    host.SetSingleton(); SetGameplaySingleton(serverGameplay);
                    float moved = (float)Call(cycleSite, "TryCollectLooseSoil", cycleBucket, soil,
                        cycleField.transform.TransformPoint(new Vector3(0f, 0f, 0f)), 100f, request, .05f);
                    Assert.That(moved, Is.GreaterThan(0f), $"cycle={cycle}, load={load}, call={calls}");
                    calls++;
                    if (calls % 4 == 0) yield return null;
                }

                Assert.That(calls, Is.GreaterThan(0).And.LessThan(64), $"cycle={cycle}, load={load}");
                Assert.That(Units(cycleBucket), Is.EqualTo(3f).Within(.00001f), $"cycle={cycle}, load={load}");
                networkedTotal += Units(cycleBucket);
                Call(cycleBucket, "Empty", false, Vector3.zero);
                yield return new WaitForSeconds(.05f);
            }

            Assert.That((int)Prop(cycleSite, "DiggingCycleIndex"), Is.EqualTo(cycle + 1),
                $"Exactly two full network bucket loads must complete cycle {cycle}; " +
                $"removed={Prop(cycleSite, "RemovedSoilUnits")}, field={Prop(Get(cycleField, "HeightField"), "TotalUnits")}");
            Assert.That((float)Prop(cycleSite, "RemovedSoilUnits"), Is.EqualTo((cycle + 1) * 6f).Within(.0001f));
        }

        Assert.That(networkedTotal, Is.EqualTo(18d).Within(.00001d));
        Assert.That(Prop(cycleSite, "CurrentStage").ToString(), Is.EqualTo("ConcretePouring"));
        hostUnits = Units(serverHostBucket);
        yield return Until(() => Matches(ownerPeer.Fields[1 - collectionIndex], Capture(cycleField)) &&
            (int)Prop(ownerPeer.Sites[1 - collectionIndex], "DiggingCycleIndex") == 3 &&
            Mathf.Abs((float)Prop(ownerPeer.Sites[1 - collectionIndex], "RemovedSoilUnits") - 18f) < .0001f,
            "remote variable-schedule cycle state");

        float beforeDroppedMovement = Units(serverHostBucket);
        float beforeDropField = (float)Prop(heightField, "TotalUnits");
        Assert.That(Drop(hostBucketObject.GetComponent(resourceType), NetworkManager.ServerClientId), Is.True);
        hostPlayer.transform.position += Vector3.forward * .3f;
        yield return new WaitForSeconds(.35f);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        Assert.That(Units(serverHostBucket), Is.EqualTo(beforeDroppedMovement).Within(.00001f));
        Assert.That((float)Prop(heightField, "TotalUnits"), Is.EqualTo(beforeDropField).Within(.00001f),
            "A dropped bucket cannot keep removing material while its former holder moves.");

        late = CreateManager("Stage4 soil late join", port);
        Assert.That(late.StartClient(), Is.True);
        late.SetSingleton();
        yield return Until(() => late.IsConnectedClient && late.LocalClient?.PlayerObject != null &&
            host.ConnectedClientsIds.Count == 3, "late-joining player");
        latePeer = CreatePeer("late", active: false);
        lateGameplay = CreateGameplay("Stage4 late bridge state", latePeer);
        foreach (Component field in latePeer.Fields) ((Behaviour)field).enabled = false;
        RegisterGameplay(lateGameplay, late, server: false);
        RequestSnapshot(lateGameplay, late);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return Until(() => late.SpawnManager.SpawnedObjects.ContainsKey(remoteBucketId) &&
            late.SpawnManager.SpawnedObjects.ContainsKey(hostBucketId) &&
            Matches(latePeer.Fields[collectionIndex], Capture(activeField)) &&
            Matches(latePeer.Fields[1 - collectionIndex], Capture(serverPeer.Fields[1 - collectionIndex])),
            "both current soil shapes on late join");
        Assert.That(Units(late.SpawnManager.SpawnedObjects[remoteBucketId].GetComponent(containerType)),
            Is.EqualTo(remoteUnits).Within(.00001f), "The late join snapshot includes current remote bucket contents.");
        Assert.That(Units(late.SpawnManager.SpawnedObjects[hostBucketId].GetComponent(containerType)),
            Is.EqualTo(hostUnits).Within(.00001f), "The late join snapshot includes current host bucket contents.");
        Assert.That((float)Prop(latePeer.Sites[collectionIndex], "RemovedSoilUnits"),
            Is.EqualTo((float)Prop(activeSite, "RemovedSoilUnits")).Within(.0001f));
        Assert.That(Matches(latePeer.Fields[1 - collectionIndex], Capture(cycleField)), Is.True,
            "The late-join snapshot includes the completed variable-schedule field shape.");
        Assert.That((int)Prop(latePeer.Sites[1 - collectionIndex], "DiggingCycleIndex"), Is.EqualTo(3));
        Assert.That((float)Prop(latePeer.Sites[1 - collectionIndex], "RemovedSoilUnits"), Is.EqualTo(18f).Within(.0001f));

        float beforeDisconnect = Units(serverRemoteBucket);
        float fieldBeforeDisconnect = (float)Prop(heightField, "TotalUnits");
        owner.Shutdown(true);
        yield return Until(() => host.ConnectedClientsIds.Count == 2, "remote-holder disconnect");
        yield return new WaitForSeconds(.35f);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        Assert.That(Units(serverRemoteBucket), Is.EqualTo(beforeDisconnect).Within(.00001f));
        Assert.That((float)Prop(heightField, "TotalUnits"), Is.EqualTo(fieldBeforeDisconnect).Within(.00001f),
            "A disconnected holder cannot leave an automatic collection tick running.");
#endif
    }

    private Peer CreatePeer(string label, bool active)
    {
        Peer peer = new();
        peer.BridgeRoot = new GameObject("Stage4 " + label + " foundation bridge");
        peer.BridgeRoot.SetActive(false); roots.Add(peer.BridgeRoot);
        Component bridge = peer.BridgeRoot.AddComponent(bridgeType);
        GameObject holder = new("Stage4 foundations"); holder.transform.SetParent(peer.BridgeRoot.transform, false);
        Set(bridge, "bridgeComponentHolder", holder);
        Object definition = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenFoundation.asset");
        Material looseMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat");
        Assert.That(definition, Is.Not.Null); Assert.That(looseMaterial, Is.Not.Null);
        for (int index = 0; index < 2; index++)
        {
            GameObject siteRoot = new("Stage4 foundation " + index); siteRoot.transform.SetParent(holder.transform, false);
            siteRoot.transform.localPosition = new Vector3(index * 12f, 0f, 0f);
            Component bridgeComponent = siteRoot.AddComponent(bridgeComponentType);
            ((Behaviour)bridgeComponent).enabled = false;
            Set(bridgeComponent, "componentID", index); Set(bridgeComponent, "bridgeComponentSO", definition);
            Component site = siteRoot.AddComponent(siteType);
            GameObject volumeRoot = new("Stage4 separate excavation-volume branch " + index);
            volumeRoot.transform.SetParent(holder.transform, false);
            volumeRoot.transform.localPosition = new Vector3(index * 12f, 0f, 0f);
            Component volume = volumeRoot.AddComponent(volumeType);
            GameObject supportRoot = new("Stage4 hard support"); supportRoot.transform.SetParent(volumeRoot.transform, false);
            supportRoot.transform.localPosition = new Vector3(0f, .02f, 0f);
            BoxCollider support = supportRoot.AddComponent<BoxCollider>(); support.size = new Vector3(6.5f, .12f, 4f);
            MeshRenderer supportRenderer = supportRoot.AddComponent<MeshRenderer>();
            GameObject fieldRoot = new("Stage4 loose soil field", typeof(MeshFilter), typeof(MeshRenderer));
            fieldRoot.transform.SetParent(volumeRoot.transform, false);
            Component field = fieldRoot.AddComponent(fieldType);
            Call(field, "Initialize", 6.5f, 4f, .2f, .4f, .4f, 6f);
            Call(field, "SetSurfaceState", false, looseMaterial);
            Set(volume, "soilSurface", supportRoot.transform); Set(volume, "constructionSite", site);
            Set(volume, "soilRenderer", supportRenderer); Set(volume, "loosenedSoilMaterial", looseMaterial);
            Set(volume, "surfaceStartLocalY", .02f); Set(volume, "useLooseSoilHeightField", true);
            Set(volume, "looseSoilField", field); Set(volume, "soilSupportCollider", support);
            Set(volume, "concreteFootprintSize", new Vector2(6.5f, 4f)); Set(site, "excavationVolume", volume);
            peer.Sites[index] = site; peer.Fields[index] = field;
        }
        peer.BridgeRoot.SetActive(true);
        foreach (Component site in peer.Sites) Call(site, "Initialize");
        for (int index = 0; index < peer.Sites.Length; index++)
        {
            Component site = peer.Sites[index];
            Component field = peer.Fields[index];
            Component volume = Get(site, "excavationVolume") as Component;
            Assert.That(field.GetComponentInParent(siteType), Is.Null,
                "The network fixture must keep field and site in separate hierarchy branches, matching Stage02.");
            Assert.That(Get(volume, "ConstructionSite"), Is.SameAs(site));
            Assert.That(Get(volume, "LooseSoilField"), Is.SameAs(field));
            Assert.That(Get(site, "excavationVolume"), Is.SameAs(volume));
            object[] resolvedSite = { null };
            Assert.That((bool)fieldType.GetMethod("TryGetConstructionSite").Invoke(field, resolvedSite), Is.True);
            Assert.That(resolvedSite[0], Is.SameAs(site));
        }
        for (int index = 0; index < 2; index++) SetSoilRemoval(peer, index);
        if (!active) foreach (Component field in peer.Fields) ((Behaviour)field).enabled = false;
        return peer;
    }

    private void SetSoilRemoval(Peer peer, int index)
    {
        object digging = Enum.Parse(Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "Digging");
        object removal = Enum.Parse(Type.GetType("FoundationDiggingSubstage, Assembly-CSharp"), "SoilRemoval");
        Set(peer.Sites[index], "currentStage", digging); Set(peer.Sites[index], "diggingSubstage", removal);
        object definition = Call(Get(peer.Sites[index], "bridgeComponent"), "GetBridgeComponentSO");
        object workflow = Get(definition, "constructionWorkflow");
        Call(peer.Fields[index], "SetSurfaceState", true,
            UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat"));
        Call(Get(peer.Sites[index], "excavationVolume"), "ApplyDiggingState", digging, removal, 0f, 0f, workflow, 0);
    }

    private Component CreateGameplay(string name, Peer peer)
    {
        GameObject go = new(name); go.SetActive(false); roots.Add(go);
        Component gameplay = go.AddComponent(gameplayType); Set(gameplay, "bridge", peer.BridgeRoot.GetComponent(bridgeType));
        Type rowType = (Type)gameplayType.GetField("bridgeComponentDataArray", I).FieldType.GetElementType();
        Array rows = Array.CreateInstance(rowType, 2);
        int[] indexes = { 0, 1 };
        for (int i = 0; i < 2; i++)
        {
            object row = Activator.CreateInstance(rowType);
            Set(row, "bridgeComponentSO", Call(peer.BridgeRoot.GetComponentsInChildren(bridgeComponentType, true)[i], "GetBridgeComponentSO"));
            rows.SetValue(row, i);
        }
        Set(gameplay, "bridgeComponentDataArray", rows);
        Type stageType = (Type)gameplayType.GetField("bridgeBuildingStages", I).FieldType.GetElementType();
        Array stages = Array.CreateInstance(stageType, 1);
        object stage = Activator.CreateInstance(stageType); Set(stage, "bridgeComponentDataIndexes", indexes); stages.SetValue(stage, 0);
        Set(gameplay, "bridgeBuildingStages", stages); Set(gameplay, "bridgeProgressInitialized", true);
        Call(gameplay, "CacheBridgeComponents"); SetGameplaySingleton(gameplay);
        return gameplay;
    }

    private static void RegisterGameplay(Component gameplay, NetworkManager manager, bool server)
    {
        manager.SetSingleton(); SetGameplaySingleton(gameplay); Call(gameplay, "EnsureNetworkMessagingRegistered");
        if (server) Call(gameplay, "InitializeServerBridgeState");
    }

    private static void RequestSnapshot(Component gameplay, NetworkManager manager)
    {
        manager.SetSingleton(); SetGameplaySingleton(gameplay); Call(gameplay, "RequestFullBridgeStateFromServer");
    }

    private static void DisableFixturePlayer(NetworkObject player)
    {
        foreach (Collider collider in player.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Behaviour behaviour in player.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour is NetworkBehaviour || behaviour.GetType().Name == "PlayerInteractionNew") continue;
            behaviour.enabled = false;
        }
    }

    private GameObject CreatePlayerPrefab()
    {
        GameObject stagingParent = new("Stage4 inactive player prefab template");
        stagingParent.SetActive(false);
        TrackRoot(stagingParent);
        GameObject prefab = new("Stage4 soil network player");
        prefab.transform.SetParent(stagingParent.transform, false);
        NetworkObject netObj = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", I).SetValue(netObj, 0x53544734u);
        netObj.SetSceneObjectStatus(false);
        GameObject anchor = new("Stage4 player anchor"); anchor.transform.SetParent(prefab.transform, false);
        anchor.transform.localPosition = new Vector3(0f, 1f, 0f);
        CharacterController capsule = prefab.AddComponent<CharacterController>();
        capsule.center = new Vector3(0f, 1f, 0f);
        Behaviour input = (Behaviour)prefab.AddComponent(Type.GetType("PlayerInputNew, Assembly-CSharp"));
        input.enabled = false;
        Component interaction = prefab.AddComponent(Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        ((Behaviour)interaction).enabled = false;
        interaction.GetType().GetField("interactionOrigin", I).SetValue(interaction, anchor.transform);
        prefab.AddComponent(Type.GetType("PlayerHealth, Assembly-CSharp"));
        prefab.AddComponent(Type.GetType("ClientNetworkTransform, Assembly-CSharp"));
        TrackRoot(prefab);
        return prefab;
    }

    private NetworkManager CreateManager(string label, ushort port)
    {
        GameObject go = new(label); roots.Add(go);
        UnityTransport transport = go.AddComponent<UnityTransport>(); transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = go.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = playerPrefab, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = bucketPrefab });
        managers.Add(manager); return manager;
    }

    private NetworkObject SpawnBucket(Vector3 position)
    {
        GameObject instance = Object.Instantiate(bucketPrefab, position, Quaternion.identity); TrackRoot(instance);
        NetworkObject networkObject = instance.GetComponent<NetworkObject>(); networkObject.SetSceneObjectStatus(false); networkObject.Spawn();
        return networkObject;
    }

    private bool CompletePickup(Component resource, NetworkObject player, ulong clientId)
    {
        Vector3 anchor = (Vector3)player.GetComponent(playerInteractionType).GetType().GetProperty("CarryBodyAnchorLocalOffset").GetValue(
            player.GetComponent(playerInteractionType));
        float radius = (float)player.GetComponent(playerInteractionType).GetType().GetMethod("GetCharacterControllerRadius").Invoke(
            player.GetComponent(playerInteractionType), null);
        MethodInfo method = resourceType.GetMethod("TryCompleteNetworkPickup", I);
        return (bool)method.Invoke(resource, new object[] { clientId, anchor, radius });
    }

    private bool Drop(Component resource, ulong clientId)
    {
        MethodInfo method = resourceType.GetMethod("TryCompleteNetworkDrop", I);
        return (bool)method.Invoke(resource, new object[] { clientId, resource.transform.position,
            resource.transform.rotation, false, null });
    }

    private bool TryFind(Peer peer, Vector3 position, out int index)
    {
        MethodInfo method = fieldType.GetMethod("TryFindCollectionField", BindingFlags.Static | BindingFlags.Public);
        object[] args = { position, null };
        bool found = (bool)method.Invoke(null, args);
        index = Array.IndexOf(peer.Fields, args[1] as Component);
        return found;
    }

    private static bool IsHeld(Component resource, ulong clientId) => (bool)resourceTypeStatic().GetMethod("IsHeldBy").Invoke(resource, new object[] { clientId });
    private static Type resourceTypeStatic() => Type.GetType("BaseResourceNew, Assembly-CSharp");
    private static Component Find(NetworkManager manager, ulong id) => manager.SpawnManager.SpawnedObjects[id].GetComponent(resourceTypeStatic()) ??
        manager.SpawnManager.SpawnedObjects[id].GetComponent(Type.GetType("PortableSubstanceContainer, Assembly-CSharp"));
    private static float Units(Component container) => (float)container.GetType().GetProperty("CurrentUnits").GetValue(container);
    private static float Add(Component container, Object substance, float amount) => (float)container.GetType()
        .GetMethod("TryAddUnits", new[] { Type.GetType("ContainerSubstanceSO, Assembly-CSharp"), typeof(float) })
        .Invoke(container, new[] { substance, (object)amount });
    private static object Capture(Component field) => Call(field, "CaptureSnapshot");
    private static bool Matches(Component field, object snapshot) => (bool)Call(Get(field, "HeightField"), "MatchesSnapshot", snapshot);
    private static object Get(object target, string name) => target.GetType().GetField(name, I)?.GetValue(target) ?? target.GetType().GetProperty(name, I)?.GetValue(target);
    private static object Prop(object target, string name) => target.GetType().GetProperty(name, I).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, I).SetValue(target, value);
    private static object Call(object target, string name, params object[] args)
    {
        MethodInfo method = Array.Find(target.GetType().GetMethods(I), m => m.Name == name && m.GetParameters().Length == args.Length);
        Assert.That(method, Is.Not.Null, $"Missing {target.GetType().Name}.{name}/{args.Length}.");
        return method.Invoke(target, args);
    }
    private static void SetGameplaySingleton(Component gameplay) => gameplayTypeStatic().GetField("<Instance>k__BackingField", S).SetValue(null, gameplay);
    private static Type gameplayTypeStatic() => Type.GetType("GameplayManager, Assembly-CSharp");
    private static Component GetGameplaySingleton() => gameplayTypeStatic().GetField("<Instance>k__BackingField", S).GetValue(null) as Component;
    private static ushort ReservePort() { using UdpClient s = new(0); return (ushort)((IPEndPoint)s.Client.LocalEndPoint).Port; }
    private static IEnumerator Until(Func<bool> predicate, string label)
    { float end = Time.realtimeSinceStartup + 20f; while (!predicate() && Time.realtimeSinceStartup < end) yield return null; Assert.That(predicate(), Is.True, "Timed out: " + label); }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        foreach (NetworkManager manager in managers) TrackSyntheticPlayerReplicas(manager);
        foreach (NetworkManager manager in managers) if (manager != null && manager.IsListening) manager.Shutdown(true);
        float deadline = Time.realtimeSinceStartup + 15f;
        while (Time.realtimeSinceStartup < deadline && managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress))) yield return null;
        Assert.That(managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress)), Is.False,
            "All NGO peers must finish shutdown before fixture objects or singleton state are restored.");
        foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        yield return null;
        NetworkObject[] leftovers = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
        Assert.That(Array.Exists(leftovers, obj => obj != null && GetGlobalObjectHash(obj) == 0x53544734u), Is.False,
            "No synthetic Stage4 player NetworkObject may remain after fixture cleanup.");
        if (previous != null) previous.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", S)?.Invoke(null, null);
        if (gameplayType != null) gameplayType.GetField("<Instance>k__BackingField", S).SetValue(null, previousGameplay);
        roots.Clear(); trackedRoots.Clear(); managers.Clear(); host = owner = late = null;
    }

    private void TrackRoot(GameObject root)
    {
        if (root != null && trackedRoots.Add(root)) roots.Add(root);
    }

    private void TrackSyntheticPlayerReplicas(NetworkManager manager)
    {
        if (manager == null || manager.SpawnManager == null) return;
        foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjectsList)
            if (networkObject != null && GetGlobalObjectHash(networkObject) == 0x53544734u)
                TrackRoot(networkObject.gameObject);
    }

    private static uint GetGlobalObjectHash(NetworkObject networkObject)
    {
        object value = typeof(NetworkObject).GetField("GlobalObjectIdHash", I).GetValue(networkObject);
        return Convert.ToUInt32(value);
    }
}
