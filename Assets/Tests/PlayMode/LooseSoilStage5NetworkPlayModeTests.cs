using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Type = System.Type;

public sealed class LooseSoilStage5NetworkPlayModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const uint SyntheticPlayerHash = 0x53544735u;
    private readonly List<GameObject> roots = new();
    private readonly HashSet<GameObject> trackedRoots = new();
    private readonly List<NetworkManager> managers = new();
    private PlayModeTestScope scope;
    private NetworkManager previous, host, owner, late;
    private Component previousGameplay, serverGameplay, ownerGameplay, lateGameplay;
    private GameObject playerPrefab, bucketPrefab;
    private ushort port;
    private Object soil;
    private Peer serverPeer, ownerPeer, latePeer;
    private Type playerInteractionType, containerType, resourceType, fieldType, siteType, gameplayType,
        bridgeType, bridgeComponentType, volumeType;

    private sealed class Peer
    {
        public GameObject BridgeRoot;
        public Component[] Sites = new Component[2];
        public Component[] Fields = new Component[2];
    }

    [UnityTest]
    public IEnumerator HostAndRemoteReturnsReplicateAndLateJoinRestoresTheReturnedShovelOverlay()
    {
#if !UNITY_EDITOR
        Assert.Ignore("The NGO fixture loads the production bucket prefab through AssetDatabase.");
        yield break;
#else
        yield return StartHostAndOwner();
        PositionPlayer(host.LocalClient.PlayerObject, new Vector3(0f, .08f, -1.1f));
        PositionPlayer(owner.LocalClient.PlayerObject, new Vector3(12f, .08f, -1.1f));
        yield return Until(() => Near(host.ConnectedClients[owner.LocalClientId].PlayerObject.transform.position,
            new Vector3(12f, .08f, -1.1f)), "remote player pose before real aim validation");

        NetworkObject[] returnBuckets = new NetworkObject[2];
        float[] shovelProgresses = new float[2];
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        for (int index = 0; index < 2; index++)
        {
            Component site = serverPeer.Sites[index];
            Component field = serverPeer.Fields[index];
            object model = Get(field, "HeightField");
            Assert.That((double)Prop(model, "TotalUnitsPrecise"), Is.GreaterThan(0d), $"site={index} initial soil");
            int loads = 0;
            while ((double)Prop(model, "TotalUnitsPrecise") > .000001d && loads < 4)
            {
                NetworkObject collectionObject = SpawnBucket(new Vector3(50f + index * 5f + loads, 2f, 0f));
                Component collectionBucket = collectionObject.GetComponent(containerType);
                if (loads == 0) returnBuckets[index] = collectionObject;
                float collected = (float)Call(site, "TryCollectLooseSoil", collectionBucket, soil,
                    field.transform.TransformPoint(Vector3.zero), 100f, 3f, .05f);
                Assert.That(collected, Is.GreaterThan(0f), $"site={index}, load={loads} uses the real source transaction");
                Assert.That(Units(collectionBucket), Is.EqualTo(collected).Within(.00001f));
                loads++;
            }
            Assert.That(loads, Is.GreaterThan(0).And.LessThan(4));
            Assert.That((double)Prop(model, "TotalUnitsPrecise"), Is.LessThan(.000001d));
            Assert.That((int)Prop(site, "DiggingCycleIndex"), Is.EqualTo(1));
            Assert.That(Prop(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));

            object workflow = Get(Call(Get(site, "bridgeComponent"), "GetBridgeComponentSO"), "constructionWorkflow");
            float shovelProgress = (float)Prop(workflow, "LooseningProgressPerCycle") * .35f;
            shovelProgresses[index] = shovelProgress;
            Assert.That((bool)Call(site, "TryApplyToolWork", Prop(workflow, "DiggingTool"), shovelProgress, -1), Is.True);
            host.SetSingleton(); SetGameplaySingleton(serverGameplay);
            Call(serverGameplay, "NotifyConstructionSiteStateChanged", site);
            Assert.That((float)Prop(site, "CurrentWorkProgress"), Is.EqualTo(shovelProgress).Within(.00001f));
            int siteIndex = index;
            yield return Until(() => Mathf.Abs((float)Prop(ownerPeer.Sites[siteIndex], "CurrentWorkProgress") - shovelProgress) < .0001f,
                $"site={index} shovel progress reaches the remote owner");
        }
        for (int index = 0; index < 2; index++)
            Assert.That((float)Prop(serverPeer.Sites[index], "CurrentWorkProgress"),
                Is.EqualTo(shovelProgresses[index]).Within(.0001f), $"site={index} shovel progress survives preparing the second site");

        NetworkObject hostBucketObject = returnBuckets[0];
        NetworkObject ownerBucketObject = returnBuckets[1];
        Assert.That(hostBucketObject, Is.Not.Null); Assert.That(ownerBucketObject, Is.Not.Null);
        ulong hostBucketId = hostBucketObject.NetworkObjectId;
        ulong ownerBucketId = ownerBucketObject.NetworkObjectId;
        yield return Until(() => owner.SpawnManager.SpawnedObjects.ContainsKey(hostBucketId) &&
            owner.SpawnManager.SpawnedObjects.ContainsKey(ownerBucketId), "owner bucket mirrors");
        Component hostBucket = hostBucketObject.GetComponent(containerType);
        Component ownerServerBucket = ownerBucketObject.GetComponent(containerType);
        yield return Until(() => Mathf.Abs(Units(Find(owner, ownerBucketId).GetComponent(containerType)) - 3f) < .00001f,
            "the real collection transactions fill both network buckets");
        Assert.That(Units(hostBucket), Is.EqualTo(3f).Within(.00001f));
        Assert.That(Units(ownerServerBucket), Is.EqualTo(3f).Within(.00001f));
        Assert.That((float)Prop(serverPeer.Sites[0], "CurrentWorkProgress"), Is.EqualTo(shovelProgresses[0]).Within(.0001f),
            "Host progress survives waiting for network bucket replication.");

        NetworkObject hostPlayer = host.LocalClient.PlayerObject;
        NetworkObject ownerServerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        Assert.That(CompletePickup(hostBucketObject.GetComponent(resourceType), hostPlayer,
            NetworkManager.ServerClientId), Is.True, "Host bucket uses the server pickup transaction.");
        Assert.That(CompletePickup(ownerBucketObject.GetComponent(resourceType), ownerServerPlayer,
            owner.LocalClientId), Is.True, "Remote bucket uses the server pickup transaction.");
        yield return Until(() => IsHeld(hostBucketObject.GetComponent(resourceType), NetworkManager.ServerClientId) &&
            IsHeld(ownerBucketObject.GetComponent(resourceType), owner.LocalClientId) &&
            (bool)Prop(Find(owner, ownerBucketId).GetComponent(resourceType), "IsPickedUp"),
            "server holder and remote resource pickup state replicate");
        // The multi-Manager fixture cannot dispatch NGO ClientRpc presentation through one global
        // NetworkManager singleton. Apply the same local confirmation after the authoritative pickup transaction.
        host.SetSingleton();
        Call(hostPlayer.GetComponent(playerInteractionType), "ConfirmPickedUpObject",
            hostBucketObject.gameObject, hostBucketObject.GetComponent(resourceType));
        owner.SetSingleton();
        Component ownerRePickedResource = Find(owner, ownerBucketId).GetComponent(resourceType);
        Call(owner.LocalClient.PlayerObject.GetComponent(playerInteractionType), "ConfirmPickedUpObject",
            ownerRePickedResource.gameObject, ownerRePickedResource);
        Assert.That(SamePickedUp(owner.LocalClient.PlayerObject.GetComponent(playerInteractionType), ownerRePickedResource.gameObject),
            Is.True, "Fixture pickup presentation must target the genuine remote bucket.");

        object[] before = new object[2];
        float[] bucketBefore = new float[2];
        float[] fieldBefore = new float[2];
        for (int index = 0; index < 2; index++)
        {
            before[index] = Capture(serverPeer.Fields[index]);
            fieldBefore[index] = PreciseUnits(serverPeer.Fields[index]);
        }
        bucketBefore[0] = Units(hostBucket);
        bucketBefore[1] = Units(ownerServerBucket);
        HashSet<int> pilesBefore = CapturePileIds();

        // A client ray with a forged origin must be rejected by the server authority path.
        Component ownerPlayer = owner.LocalClient.PlayerObject.GetComponent(playerInteractionType);
        Set(ownerPlayer, "_currentTarget", ownerPeer.Sites[1]);
        Ray validOwnerRay = BuildNearClipAimRay(ownerPlayer, ownerPeer.Fields[1], 0f);
        Ray forgedOrigin = new Ray(validOwnerRay.origin + validOwnerRay.direction * .7f, validOwnerRay.direction);
        owner.SetSingleton(); SetGameplaySingleton(ownerGameplay);
        Call(Find(owner, ownerBucketId).GetComponent(containerType), "RequestContextActionWithAimRay",
            ownerPlayer, ownerPeer.Sites[1], forgedOrigin);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return new WaitForSeconds(.25f);
        Assert.That(Units(ownerServerBucket), Is.EqualTo(bucketBefore[1]).Within(.00001f));
        Assert.That(Matches(serverPeer.Fields[1], before[1]), Is.True, "The server rejects a ray outside its near-clip anchor allowance.");

        // Gaze can identify a different pit, but the fixed dump column must stay inside that same pit.
        Ray otherPitRay = BuildNearClipAimRay(ownerPlayer, ownerPeer.Fields[0], 0f);
        owner.SetSingleton(); SetGameplaySingleton(ownerGameplay);
        Call(Find(owner, ownerBucketId).GetComponent(containerType), "RequestContextActionWithAimRay",
            ownerPlayer, ownerPeer.Sites[0], otherPitRay);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return new WaitForSeconds(.2f);
        Assert.That(Units(ownerServerBucket), Is.EqualTo(bucketBefore[1]).Within(.00001f),
            "A gaze hit on site 0 cannot transfer soil when the fixed dump column is inside site 1.");
        Assert.That(Matches(serverPeer.Fields[0], before[0]), Is.True);
        Assert.That(Matches(serverPeer.Fields[1], before[1]), Is.True);

        // Bypass the client's local picked-object guard and verify the server rejects a non-holder RPC.
        Ray nonHolderRay = BuildNearClipAimRay(ownerPlayer, ownerPeer.Fields[0], 0f);
        owner.SetSingleton();
        Call(Find(owner, hostBucketId).GetComponent(containerType), "RequestContextActionWithAimRayServerRpc",
            (byte)1, (ulong)0, nonHolderRay.origin, nonHolderRay.direction, new ServerRpcParams());
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return new WaitForSeconds(.2f);
        Assert.That(Units(hostBucket), Is.EqualTo(bucketBefore[0]).Within(.00001f),
            "The server rejects a real aim RPC from a client that does not hold the target bucket.");
        Assert.That(Matches(serverPeer.Fields[0], before[0]), Is.True);

        // Host direct execution and remote client RPC both aim inside their pit while the fixed dump column stays at its center.
        Component hostPlayerInteraction = hostPlayer.GetComponent(playerInteractionType);
        Set(hostPlayerInteraction, "_currentTarget", serverPeer.Sites[0]);
        Ray hostRay = BuildNearClipAimRay(hostPlayerInteraction, serverPeer.Fields[0], 0f);
        Assert.That((float)Prop(serverPeer.Sites[0], "CurrentWorkProgress"), Is.EqualTo(shovelProgresses[0]).Within(.0001f),
            "Host site retains its shovel progress immediately before return.");
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        Call(hostBucket, "RequestContextActionWithAimRay", hostPlayerInteraction, serverPeer.Sites[0], hostRay);
        Assert.That((float)Prop(serverPeer.Sites[0], "CurrentWorkProgress"), Is.EqualTo(shovelProgresses[0]).Within(.0001f),
            "Host return preserves shovel progress synchronously.");

        owner.SetSingleton(); SetGameplaySingleton(ownerGameplay);
        // The crosshair targets farther into the same pit than the fixed physical dump point.
        Ray ownerRay = BuildNearClipAimRay(ownerPlayer, ownerPeer.Fields[1], .7f);
        Call(Find(owner, ownerBucketId).GetComponent(containerType), "RequestContextActionWithAimRay",
            ownerPlayer, ownerPeer.Sites[1], ownerRay);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);

        yield return Until(() => Units(hostBucket) < bucketBefore[0] - .00001f &&
            Units(ownerServerBucket) < bucketBefore[1] - .00001f, "server accepts host and remote same-pit return RPCs");
        float[] accepted = { bucketBefore[0] - Units(hostBucket), bucketBefore[1] - Units(ownerServerBucket) };
        for (int index = 0; index < 2; index++)
        {
            Assert.That(accepted[index], Is.GreaterThan(0f), $"site={index} accepted return amount");
            float fieldAfter = PreciseUnits(serverPeer.Fields[index]);
            Assert.That(fieldAfter, Is.EqualTo(accepted[index]).Within(.00002f),
                $"site={index} returned-overlay field equals the server bucket debit; prior Loosening layer={fieldBefore[index]}");
            Assert.That((int)Prop(serverPeer.Sites[index], "DiggingCycleIndex"), Is.EqualTo(1));
            Assert.That(Prop(serverPeer.Sites[index], "DiggingSubstage").ToString(), Is.EqualTo("ReturnedSoilRemoval"));
            Assert.That((float)Prop(serverPeer.Sites[index], "CurrentWorkProgress"),
                Is.EqualTo(shovelProgresses[index]).Within(.0001f));
        }
        Assert.That(CaptureNewPiles(pilesBefore), Is.Empty, "Aimed pit returns do not spawn fallback piles.");

        yield return Until(() => Matches(ownerPeer.Fields[0], Capture(serverPeer.Fields[0])) &&
            Matches(ownerPeer.Fields[1], Capture(serverPeer.Fields[1])) &&
            Mathf.Abs(Units(Find(owner, hostBucketId).GetComponent(containerType)) - Units(hostBucket)) < .00001f &&
            Mathf.Abs(Units(Find(owner, ownerBucketId).GetComponent(containerType)) - Units(ownerServerBucket)) < .00001f,
            "return shapes and contents replicate to the remote owner");
        Assert.That(Prop(ownerPeer.Sites[0], "DiggingSubstage").ToString(), Is.EqualTo("ReturnedSoilRemoval"));
        Assert.That(Prop(ownerPeer.Sites[1], "DiggingSubstage").ToString(), Is.EqualTo("ReturnedSoilRemoval"));

        object[] fullBrushShapes = { Capture(serverPeer.Fields[0]), Capture(serverPeer.Fields[1]) };
        float[] fullBrushBuckets = { Units(hostBucket), Units(ownerServerBucket) };
        float[] fullBrushRemoved = { (float)Prop(serverPeer.Sites[0], "RemovedSoilUnits"), (float)Prop(serverPeer.Sites[1], "RemovedSoilUnits") };
        float[] fullBrushProgress = { (float)Prop(serverPeer.Sites[0], "CurrentWorkProgress"), (float)Prop(serverPeer.Sites[1], "CurrentWorkProgress") };
        HashSet<int> pilesBeforeFullBrushRetry = CapturePileIds();

        // Repeating both normal host/client action paths against the now-full local brush must reject without any debit.
        Component hostInteractionForRetry = hostPlayer.GetComponent(playerInteractionType);
        if (!SamePickedUp(hostInteractionForRetry, hostBucketObject.gameObject))
        {
            host.SetSingleton();
            Call(hostInteractionForRetry, "ConfirmPickedUpObject", hostBucketObject.gameObject, hostBucketObject.GetComponent(resourceType));
        }
        Assert.That(SamePickedUp(hostInteractionForRetry, hostBucketObject.gameObject), Is.True,
            "The host still holds the bucket immediately before the retry RPC.");
        Set(hostInteractionForRetry, "_currentTarget", serverPeer.Sites[0]);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        Call(hostBucket, "RequestContextActionWithAimRay", hostInteractionForRetry,
            serverPeer.Sites[0], BuildNearClipAimRay(hostInteractionForRetry, serverPeer.Fields[0], 0f));

        owner.SetSingleton();
        Component ownerInteractionForRetry = owner.LocalClient.PlayerObject.GetComponent(playerInteractionType);
        Component ownerResourceForRetry = Find(owner, ownerBucketId).GetComponent(resourceType);
        if (!SamePickedUp(ownerInteractionForRetry, ownerResourceForRetry.gameObject))
            Call(ownerInteractionForRetry, "ConfirmPickedUpObject", ownerResourceForRetry.gameObject, ownerResourceForRetry);
        Assert.That(SamePickedUp(ownerInteractionForRetry, ownerResourceForRetry.gameObject), Is.True,
            "The remote owner still holds the bucket immediately before the retry RPC.");
        Set(ownerInteractionForRetry, "_currentTarget", ownerPeer.Sites[1]);
        Call(Find(owner, ownerBucketId).GetComponent(containerType), "RequestContextActionWithAimRay",
            ownerInteractionForRetry, ownerPeer.Sites[1], BuildNearClipAimRay(ownerInteractionForRetry, ownerPeer.Fields[1], .7f));
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return new WaitForSeconds(.2f);
        Assert.That(Units(hostBucket), Is.EqualTo(fullBrushBuckets[0]).Within(.00001f));
        Assert.That(Units(ownerServerBucket), Is.EqualTo(fullBrushBuckets[1]).Within(.00001f));
        for (int index = 0; index < 2; index++)
        {
            Assert.That(Matches(serverPeer.Fields[index], fullBrushShapes[index]), Is.True, $"site={index} full brush stays unchanged");
            Assert.That((float)Prop(serverPeer.Sites[index], "RemovedSoilUnits"), Is.EqualTo(fullBrushRemoved[index]).Within(.0001f));
            Assert.That((float)Prop(serverPeer.Sites[index], "CurrentWorkProgress"), Is.EqualTo(shovelProgresses[index]).Within(.0001f));
        }
        Assert.That(CaptureNewPiles(pilesBeforeFullBrushRetry), Is.Empty, "A rejected full-brush return creates no fallback pile.");

        late = CreateManager("Stage5 return late observer", port);
        Assert.That(late.StartClient(), Is.True);
        late.SetSingleton();
        yield return Until(() => late.IsConnectedClient && late.LocalClient?.PlayerObject != null &&
            host.ConnectedClientsIds.Count == 3, "late observer joins host");
        latePeer = CreatePeer("late observer", active: false);
        lateGameplay = CreateGameplay("Stage5 late bridge state", latePeer);
        DisablePeerPhysics(latePeer);
        RegisterGameplay(lateGameplay, late, server: false);
        RequestSnapshot(lateGameplay, late);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return Until(() => late.SpawnManager.SpawnedObjects.ContainsKey(hostBucketId) &&
            late.SpawnManager.SpawnedObjects.ContainsKey(ownerBucketId) &&
            Matches(latePeer.Fields[0], Capture(serverPeer.Fields[0])) &&
            Matches(latePeer.Fields[1], Capture(serverPeer.Fields[1])) &&
            Prop(latePeer.Sites[0], "DiggingSubstage").ToString() == "ReturnedSoilRemoval" &&
            Prop(latePeer.Sites[1], "DiggingSubstage").ToString() == "ReturnedSoilRemoval",
            "late observer restores both returned overlays");
        Assert.That((int)Prop(latePeer.Sites[0], "DiggingCycleIndex"), Is.EqualTo(1));
        Assert.That((int)Prop(latePeer.Sites[1], "DiggingCycleIndex"), Is.EqualTo(1));
        Assert.That((float)Prop(latePeer.Sites[0], "CurrentWorkProgress"), Is.EqualTo(shovelProgresses[0]).Within(.0001f));
        Assert.That((float)Prop(latePeer.Sites[1], "CurrentWorkProgress"), Is.EqualTo(shovelProgresses[1]).Within(.0001f));
        Assert.That((float)Prop(latePeer.Sites[0], "RemovedSoilUnits"),
            Is.EqualTo((float)Prop(serverPeer.Sites[0], "RemovedSoilUnits")).Within(.0001f));
        Assert.That((float)Prop(latePeer.Sites[1], "RemovedSoilUnits"),
            Is.EqualTo((float)Prop(serverPeer.Sites[1], "RemovedSoilUnits")).Within(.0001f));
        Assert.That(Units(Find(late, hostBucketId).GetComponent(containerType)), Is.EqualTo(Units(hostBucket)).Within(.00001f));
        Assert.That(Units(Find(late, ownerBucketId).GetComponent(containerType)), Is.EqualTo(Units(ownerServerBucket)).Within(.00001f));

        // Collect each returned overlay back out through the server transaction; cycle and partial shovel progress survive.
        for (int index = 0; index < 2; index++)
        {
            Component returnBucket = index == 0 ? hostBucket : ownerServerBucket;
            float bucketUnitsBeforeCollect = Units(returnBucket);
            double fieldBeforeCollect = (double)Prop(Get(serverPeer.Fields[index], "HeightField"), "TotalUnitsPrecise");
            float collectedAgain = (float)Call(serverPeer.Sites[index], "TryCollectLooseSoil", returnBucket, soil,
                serverPeer.Fields[index].transform.TransformPoint(Vector3.zero), 100f, 3f, 0f);
            Assert.That(collectedAgain, Is.GreaterThan(0f));
            Assert.That(bucketUnitsBeforeCollect + collectedAgain, Is.EqualTo(Units(returnBucket)).Within(.00002f));
            Assert.That(fieldBeforeCollect - (double)Prop(Get(serverPeer.Fields[index], "HeightField"), "TotalUnitsPrecise"),
                Is.EqualTo(collectedAgain).Within(.00002d));
            Assert.That((int)Prop(serverPeer.Sites[index], "DiggingCycleIndex"), Is.EqualTo(1));
            Assert.That(Prop(serverPeer.Sites[index], "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
            Assert.That((float)Prop(serverPeer.Sites[index], "CurrentWorkProgress"),
                Is.EqualTo(shovelProgresses[index]).Within(.0001f));
            Assert.That((float)Prop(ownerPeer.Sites[index], "CurrentWorkProgress"),
                Is.EqualTo(shovelProgresses[index]).Within(.0001f));
        }
        yield return Until(() => Matches(ownerPeer.Fields[0], Capture(serverPeer.Fields[0])) &&
            Matches(ownerPeer.Fields[1], Capture(serverPeer.Fields[1])) &&
            Matches(latePeer.Fields[0], Capture(serverPeer.Fields[0])) &&
            Matches(latePeer.Fields[1], Capture(serverPeer.Fields[1])) &&
            Prop(ownerPeer.Sites[0], "DiggingSubstage").ToString() == "Loosening" &&
            Prop(ownerPeer.Sites[1], "DiggingSubstage").ToString() == "Loosening" &&
            Prop(latePeer.Sites[0], "DiggingSubstage").ToString() == "Loosening" &&
            Prop(latePeer.Sites[1], "DiggingSubstage").ToString() == "Loosening",
            "recollected overlay state replicates to owner and late observer");
#endif
    }

    private IEnumerator StartHostAndOwner()
    {
        scope = new PlayModeTestScope("LooseSoilStage5Network");
        previous = NetworkManager.Singleton;
        if (previous != null && previous.IsListening)
            Assert.Ignore("The isolated Stage5 NGO test requires no pre-existing network session.");
        gameplayType = Type.GetType("GameplayManager, Assembly-CSharp");
        if (gameplayType != null)
        {
            previousGameplay = GetGameplaySingleton();
            scope.RestoreWith(() => SetGameplaySingleton(previousGameplay));
        }
        bridgeType = Type.GetType("Bridge, Assembly-CSharp");
        bridgeComponentType = Type.GetType("BridgeComponent, Assembly-CSharp");
        siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        containerType = Type.GetType("PortableSubstanceContainer, Assembly-CSharp");
        resourceType = Type.GetType("BaseResourceNew, Assembly-CSharp");
        playerInteractionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        Assert.That(new[] { gameplayType, bridgeType, bridgeComponentType, siteType, volumeType,
            fieldType, containerType, resourceType, playerInteractionType }, Has.None.Null);
        bucketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/PortableBucket.prefab");
        soil = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
        Assert.That(bucketPrefab, Is.Not.Null);
        Assert.That(soil, Is.Not.Null);
        playerPrefab = CreatePlayerPrefab();

        port = ReservePort();
        host = CreateManager("Stage5 soil host", port);
        Assert.That(host.StartHost(), Is.True);
        Assert.That(((NetworkObject)playerPrefab.GetComponent(typeof(NetworkObject))).IsSpawned, Is.False);
        host.SetSingleton();
        yield return Until(() => host.LocalClient?.PlayerObject != null, "Stage5 host player");
        owner = CreateManager("Stage5 soil remote owner", port);
        Assert.That(owner.StartClient(), Is.True);
        owner.SetSingleton();
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null &&
            host.ConnectedClientsIds.Count == 2, "Stage5 remote owner player");
        host.SetSingleton();
        DisableFixturePlayer(host.LocalClient.PlayerObject);
        DisableFixturePlayer(host.ConnectedClients[owner.LocalClientId].PlayerObject);
        DisableFixturePlayer(owner.LocalClient.PlayerObject);
        host.LocalClient.PlayerObject.transform.position = new Vector3(0f, .08f, 0f);
        owner.LocalClient.PlayerObject.transform.position = new Vector3(0f, .08f, 0f);
        yield return Until(() => Mathf.Abs(host.ConnectedClients[owner.LocalClientId].PlayerObject.transform.position.y - .08f) < .1f,
            "Stage5 remote player pose sync");
        host.SetSingleton();

        serverPeer = CreatePeer("server", active: true);
        ownerPeer = CreatePeer("remote owner", active: false);
        serverGameplay = CreateGameplay("Stage5 host bridge state", serverPeer);
        ownerGameplay = CreateGameplay("Stage5 owner bridge state", ownerPeer);
        DisablePeerPhysics(ownerPeer);
        RegisterGameplay(ownerGameplay, owner, server: false);
        RegisterGameplay(serverGameplay, host, server: true);
    }

    private Peer CreatePeer(string label, bool active)
    {
        Peer peer = new();
        peer.BridgeRoot = new GameObject("Stage5 " + label + " foundation bridge");
        peer.BridgeRoot.SetActive(false); TrackRoot(peer.BridgeRoot);
        Component bridge = peer.BridgeRoot.AddComponent(bridgeType);
        GameObject holder = new("Stage5 foundations"); holder.transform.SetParent(peer.BridgeRoot.transform, false);
        Set(bridge, "bridgeComponentHolder", holder);
        Object definition = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenFoundation.asset");
        Material looseMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat");
        Assert.That(definition, Is.Not.Null); Assert.That(looseMaterial, Is.Not.Null);
        for (int index = 0; index < 2; index++)
        {
            GameObject siteRoot = new("Stage5 foundation " + index); siteRoot.transform.SetParent(holder.transform, false);
            siteRoot.transform.localPosition = new Vector3(index * 12f, 0f, 0f);
            Component bridgeComponent = siteRoot.AddComponent(bridgeComponentType);
            ((Behaviour)bridgeComponent).enabled = false;
            Set(bridgeComponent, "componentID", index); Set(bridgeComponent, "bridgeComponentSO", definition);
            Component site = siteRoot.AddComponent(siteType);
            GameObject volumeRoot = new("Stage5 separate excavation-volume branch " + index);
            volumeRoot.transform.SetParent(holder.transform, false);
            volumeRoot.transform.localPosition = new Vector3(index * 12f, 0f, 0f);
            Component volume = volumeRoot.AddComponent(volumeType);
            GameObject supportRoot = new("Stage5 hard support"); supportRoot.transform.SetParent(volumeRoot.transform, false);
            supportRoot.transform.localPosition = new Vector3(0f, .02f, 0f);
            BoxCollider support = supportRoot.AddComponent<BoxCollider>(); support.size = new Vector3(6.5f, .12f, 4f);
            MeshRenderer supportRenderer = supportRoot.AddComponent<MeshRenderer>();
            GameObject fieldRoot = new("Stage5 loose soil field", typeof(MeshFilter), typeof(MeshRenderer));
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
            Component site = peer.Sites[index]; Component field = peer.Fields[index];
            Component volume = Get(site, "excavationVolume") as Component;
            Assert.That(field.GetComponentInParent(siteType), Is.Null, "Stage02 network replica uses separate pit/site branches.");
            Assert.That(Get(volume, "ConstructionSite"), Is.SameAs(site));
            Assert.That(Get(volume, "LooseSoilField"), Is.SameAs(field));
            object[] resolved = { null };
            Assert.That((bool)fieldType.GetMethod("TryGetConstructionSite").Invoke(field, resolved), Is.True);
            Assert.That(resolved[0], Is.SameAs(site));
        }
        for (int index = 0; index < 2; index++) SetSoilRemoval(peer, index);
        if (!active) DisablePeerPhysics(peer);
        return peer;
    }

    private void SetSoilRemoval(Peer peer, int index)
    {
        object digging = Enum.Parse(Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "Digging");
        object removal = Enum.Parse(Type.GetType("FoundationDiggingSubstage, Assembly-CSharp"), "SoilRemoval");
        Set(peer.Sites[index], "currentStage", digging); Set(peer.Sites[index], "diggingSubstage", removal);
        object workflow = Get(Call(Get(peer.Sites[index], "bridgeComponent"), "GetBridgeComponentSO"), "constructionWorkflow");
        Material material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat");
        Call(peer.Fields[index], "SetSurfaceState", true, material);
        Call(Get(peer.Sites[index], "excavationVolume"), "ApplyDiggingState", digging, removal, 0f, 0f, workflow, 0);
    }

    private Component CreateGameplay(string name, Peer peer)
    {
        GameObject go = new(name); go.SetActive(false); TrackRoot(go);
        Component gameplay = go.AddComponent(gameplayType); Set(gameplay, "bridge", peer.BridgeRoot.GetComponent(bridgeType));
        Type rowType = (Type)gameplayType.GetField("bridgeComponentDataArray", Instance).FieldType.GetElementType();
        Array rows = Array.CreateInstance(rowType, 2);
        for (int i = 0; i < 2; i++)
        {
            object row = Activator.CreateInstance(rowType);
            Set(row, "bridgeComponentSO", Call(peer.BridgeRoot.GetComponentsInChildren(bridgeComponentType, true)[i], "GetBridgeComponentSO"));
            rows.SetValue(row, i);
        }
        Set(gameplay, "bridgeComponentDataArray", rows);
        Type stageType = (Type)gameplayType.GetField("bridgeBuildingStages", Instance).FieldType.GetElementType();
        Array stages = Array.CreateInstance(stageType, 1); object stage = Activator.CreateInstance(stageType);
        Set(stage, "bridgeComponentDataIndexes", new[] { 0, 1 }); stages.SetValue(stage, 0);
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
    { manager.SetSingleton(); SetGameplaySingleton(gameplay); Call(gameplay, "RequestFullBridgeStateFromServer"); }

    private static void DisablePeerPhysics(Peer peer)
    {
        foreach (Component field in peer.Fields) ((Behaviour)field).enabled = false;
        foreach (Collider collider in peer.BridgeRoot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    private static void DisableFixturePlayer(NetworkObject player)
    {
        foreach (Collider collider in player.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Behaviour behaviour in player.GetComponentsInChildren<Behaviour>(true))
            if (behaviour is not NetworkBehaviour && behaviour.GetType().Name != "PlayerInteractionNew") behaviour.enabled = false;
    }

    private GameObject CreatePlayerPrefab()
    {
        GameObject staging = new("Stage5 inactive player prefab template"); staging.SetActive(false); TrackRoot(staging);
        GameObject prefab = new("Stage5 soil network player"); prefab.transform.SetParent(staging.transform, false);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", Instance).SetValue(networkObject, SyntheticPlayerHash);
        networkObject.SetSceneObjectStatus(false);
        GameObject anchor = new("Stage5 player anchor"); anchor.transform.SetParent(prefab.transform, false); anchor.transform.localPosition = new Vector3(0f, 1f, 0f);
        CharacterController capsule = prefab.AddComponent<CharacterController>(); capsule.center = new Vector3(0f, 1f, 0f);
        Behaviour input = (Behaviour)prefab.AddComponent(Type.GetType("PlayerInputNew, Assembly-CSharp")); input.enabled = false;
        Component interaction = prefab.AddComponent(playerInteractionType); ((Behaviour)interaction).enabled = false;
        interaction.GetType().GetField("interactionOrigin", Instance).SetValue(interaction, anchor.transform);
        prefab.AddComponent(Type.GetType("PlayerHealth, Assembly-CSharp")); prefab.AddComponent(Type.GetType("ClientNetworkTransform, Assembly-CSharp"));
        TrackRoot(prefab); return prefab;
    }

    private NetworkManager CreateManager(string label, ushort listenPort)
    {
        GameObject go = new(label); TrackRoot(go);
        UnityTransport transport = go.AddComponent<UnityTransport>(); transport.SetConnectionData("127.0.0.1", listenPort, "127.0.0.1");
        NetworkManager manager = go.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = playerPrefab, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = bucketPrefab });
        managers.Add(manager); scope.OwnManager(manager); return manager;
    }

    private NetworkObject SpawnBucket(Vector3 position)
    {
        GameObject instance = Object.Instantiate(bucketPrefab, position, Quaternion.identity); TrackRoot(instance);
        NetworkObject networkObject = instance.GetComponent<NetworkObject>(); networkObject.SetSceneObjectStatus(false); networkObject.Spawn(); return networkObject;
    }

    private bool CompletePickup(Component resource, NetworkObject player, ulong clientId)
    {
        Component interaction = player.GetComponent(playerInteractionType);
        Vector3 anchor = (Vector3)playerInteractionType.GetProperty("CarryBodyAnchorLocalOffset", Instance).GetValue(interaction);
        float radius = (float)playerInteractionType.GetMethod("GetCharacterControllerRadius", Instance).Invoke(interaction, null);
        return (bool)resourceType.GetMethod("TryCompleteNetworkPickup", Instance).Invoke(resource, new object[] { clientId, anchor, radius });
    }

    private bool Drop(Component resource, ulong clientId) => (bool)resourceType.GetMethod("TryCompleteNetworkDrop", Instance)
        .Invoke(resource, new object[] { clientId, resource.transform.position, resource.transform.rotation, false, null });

    private static bool IsHeld(Component resource, ulong clientId) => (bool)resourceTypeStatic().GetMethod("IsHeldBy").Invoke(resource, new object[] { clientId });
    private static bool SamePickedUp(Component interaction, GameObject go) => (GameObject)playerInteractionStatic()
        .GetMethod("GetPickedUpGameObject").Invoke(interaction, null) == go;
    private static Type playerInteractionStatic() => Type.GetType("PlayerInteractionNew, Assembly-CSharp");
    private static Type resourceTypeStatic() => Type.GetType("BaseResourceNew, Assembly-CSharp");
    private static Component Find(NetworkManager manager, ulong id) => manager.SpawnManager.SpawnedObjects[id].GetComponent(containerTypeStatic());
    private static Type containerTypeStatic() => Type.GetType("PortableSubstanceContainer, Assembly-CSharp");
    private static float Units(Component container) => (float)containerTypeStatic().GetProperty("CurrentUnits").GetValue(container);
    private static float PreciseUnits(Component field) => Convert.ToSingle(Prop(Get(field, "HeightField"), "TotalUnitsPrecise"));
    private static float Add(Component container, Object substance, float amount) => (float)containerTypeStatic()
        .GetMethod("TryAddUnits", Instance, null, new[] { Type.GetType("ContainerSubstanceSO, Assembly-CSharp"), typeof(float) }, null)
        .Invoke(container, new object[] { substance, amount });
    private static object Capture(Component field) => Call(field, "CaptureSnapshot");
    private static bool Matches(Component field, object snapshot) => (bool)Call(Get(field, "HeightField"), "MatchesSnapshot", snapshot);
    private static object Get(object target, string name) => target.GetType().GetField(name, Instance)?.GetValue(target) ?? target.GetType().GetProperty(name, Instance)?.GetValue(target);
    private static object Prop(object target, string name) => target.GetType().GetProperty(name, Instance).GetValue(target);
    private static object FieldValue(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    private static object Call(object target, string name, params object[] args)
    {
        MethodInfo method = Array.Find(target.GetType().GetMethods(Instance), m => m.Name == name && m.GetParameters().Length == args.Length);
        Assert.That(method, Is.Not.Null, $"Missing {target.GetType().Name}.{name}/{args.Length}."); return method.Invoke(target, args);
    }
    private static void SetGameplaySingleton(Component gameplay) => gameplayTypeStatic().GetField("<Instance>k__BackingField", Static).SetValue(null, gameplay);
    private static Type gameplayTypeStatic() => Type.GetType("GameplayManager, Assembly-CSharp");
    private static Component GetGameplaySingleton() => gameplayTypeStatic().GetField("<Instance>k__BackingField", Static).GetValue(null) as Component;
    private static ushort ReservePort() { using UdpClient socket = new(0); return (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port; }
    private static IEnumerator Until(Func<bool> predicate, string label)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(predicate(), Is.True, "Timed out: " + label);
    }
    private static bool Near(Vector3 actual, Vector3 expected) => Vector3.Distance(actual, expected) < .15f;
    private static void PositionPlayer(NetworkObject player, Vector3 position) { player.transform.position = position; Physics.SyncTransforms(); }
    private static Ray BuildNearClipAimRay(Component interaction, Component field, float forwardOffset)
    {
        Transform anchor = (Transform)playerInteractionStatic().GetProperty("SingleCarryPlacementAimAnchor", Instance).GetValue(interaction);
        Vector3 aim = field.transform.TransformPoint(new Vector3(0f, 0f, forwardOffset));
        Vector3 origin = anchor.position + (aim - anchor.position).normalized * .2f;
        return new Ray(origin, (aim - origin).normalized);
    }
    private static HashSet<int> CapturePileIds() => new(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
        .Where(component => component != null && component.GetType().Name == "LooseSubstancePile")
        .Select(component => component.GetInstanceID()));
    private static Component[] CaptureNewPiles(HashSet<int> before) => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
        .Where(component => component != null && component.GetType().Name == "LooseSubstancePile" && !before.Contains(component.GetInstanceID()))
        .Cast<Component>().ToArray();
    private IEnumerator MoveBothPlayers(int frames, float distance)
    {
        for (int i = 0; i < frames; i++)
        {
            if (owner != null && owner.IsListening && owner.LocalClient?.PlayerObject != null)
                owner.LocalClient.PlayerObject.transform.position += Vector3.forward * distance;
            if (host?.LocalClient?.PlayerObject != null) host.LocalClient.PlayerObject.transform.position += Vector3.forward * distance;
            Physics.SyncTransforms(); yield return new WaitForSeconds(.12f);
        }
    }
    private IEnumerator MoveRemotePlayer(int frames, float distance)
    {
        for (int i = 0; i < frames; i++) { owner.LocalClient.PlayerObject.transform.position += Vector3.forward * distance; yield return new WaitForSeconds(.12f); }
    }
    private void TrackRoot(GameObject root)
    {
        if (root != null && trackedRoots.Add(root))
        {
            roots.Add(root);
            scope?.Own(root);
        }
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (scope == null) yield break;
        PlayModeTestScope cleanupScope = scope;
        foreach (GameObject root in roots) cleanupScope.Own(root);
        foreach (NetworkManager manager in managers) cleanupScope.OwnManager(manager);
        yield return cleanupScope.Cleanup();
        roots.Clear(); trackedRoots.Clear(); managers.Clear();
        host = owner = late = previous = null;
        previousGameplay = serverGameplay = ownerGameplay = lateGameplay = null;
        playerPrefab = bucketPrefab = null; soil = null; port = 0;
        serverPeer = ownerPeer = latePeer = null;
        scope = null;
        cleanupScope.AssertCleanupSucceeded();
    }

    [UnityTest]
    public IEnumerator HeldIntentSuppressesServerCollectionUntilReleaseAndCannotBeSpoofedOrInherited()
    {
#if !UNITY_EDITOR
        Assert.Ignore("The NGO fixture loads the production bucket prefab through AssetDatabase.");
        yield break;
#else
        yield return StartHostAndOwner();
        PositionPlayer(host.LocalClient.PlayerObject, new Vector3(0f, .08f, -1.1f));
        PositionPlayer(owner.LocalClient.PlayerObject, new Vector3(12f, .08f, -1.1f));
        yield return Until(() => Near(host.ConnectedClients[owner.LocalClientId].PlayerObject.transform.position,
            new Vector3(12f, .08f, -1.1f)), "remote player pose before automatic collection");

        NetworkObject hostBucketObject = SpawnBucket(new Vector3(60f, 2f, 0f));
        NetworkObject ownerBucketObject = SpawnBucket(new Vector3(61f, 2f, 0f));
        ulong hostBucketId = hostBucketObject.NetworkObjectId;
        ulong ownerBucketId = ownerBucketObject.NetworkObjectId;
        yield return Until(() => owner.SpawnManager.SpawnedObjects.ContainsKey(hostBucketId) &&
            owner.SpawnManager.SpawnedObjects.ContainsKey(ownerBucketId), "both bucket mirrors");

        NetworkObject hostPlayer = host.LocalClient.PlayerObject;
        NetworkObject ownerServerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        Assert.That(CompletePickup(hostBucketObject.GetComponent(resourceType), hostPlayer,
            NetworkManager.ServerClientId), Is.True);
        Assert.That(CompletePickup(ownerBucketObject.GetComponent(resourceType), ownerServerPlayer,
            owner.LocalClientId), Is.True);
        yield return Until(() => IsHeld(hostBucketObject.GetComponent(resourceType), NetworkManager.ServerClientId) &&
            IsHeld(ownerBucketObject.GetComponent(resourceType), owner.LocalClientId) &&
            (bool)Prop(Find(owner, ownerBucketId).GetComponent(resourceType), "IsPickedUp"),
            "server holder and remote resource pickup state replicate");
        host.SetSingleton();
        Call(hostPlayer.GetComponent(playerInteractionType), "ConfirmPickedUpObject",
            hostBucketObject.gameObject, hostBucketObject.GetComponent(resourceType));
        owner.SetSingleton();
        Component ownerClientResource = Find(owner, ownerBucketId).GetComponent(resourceType);
        Call(owner.LocalClient.PlayerObject.GetComponent(playerInteractionType), "ConfirmPickedUpObject",
            ownerClientResource.gameObject, ownerClientResource);
        Assert.That(SamePickedUp(owner.LocalClient.PlayerObject.GetComponent(playerInteractionType), ownerClientResource.gameObject),
            Is.True, "Fixture pickup presentation must target the genuine remote bucket.");
        yield return new WaitForSeconds(.3f);

        Component hostBucket = hostBucketObject.GetComponent(containerType);
        Component ownerServerBucket = ownerBucketObject.GetComponent(containerType);
        Component ownerPlayer = owner.LocalClient.PlayerObject.GetComponent(playerInteractionType);
        object hostShapeBefore = Capture(serverPeer.Fields[0]);
        object ownerShapeBefore = Capture(serverPeer.Fields[1]);
        float hostUnitsBefore = Units(hostBucket);
        float ownerUnitsBefore = Units(ownerServerBucket);
        float hostFieldBefore = PreciseUnits(serverPeer.Fields[0]);
        float ownerFieldBefore = PreciseUnits(serverPeer.Fields[1]);

        // An unauthorized remote RPC cannot begin suppression on the host's bucket.
        owner.SetSingleton();
        Call(Find(owner, hostBucketId).GetComponent(containerType), "SetBucketActionCollectionIntentServerRpc",
            true, new ServerRpcParams());
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return new WaitForSeconds(.15f);
        Assert.That(FieldValue(hostBucket, "bucketActionCollectionSuppressed"), Is.EqualTo(false),
            "The server rejects a spoofed begin from a client that does not hold the bucket.");

        // Legitimate host and remote holders begin through the exact API used by PlayerInteractionNew.
        Assert.That((bool)Call(hostBucket, "SetBucketActionCollectionIntent", hostPlayer.GetComponent(playerInteractionType), true), Is.True);
        owner.SetSingleton();
        Component ownerClientBucket = Find(owner, ownerBucketId);
        bool ownerIntentAcceptedLocally = (bool)Call(ownerClientBucket,
            "SetBucketActionCollectionIntent", ownerPlayer, true);
        Assert.That(ownerIntentAcceptedLocally, Is.True,
            $"Remote API guard: picked={SamePickedUp(ownerPlayer, ownerClientResource.gameObject)}, playerOwner={ownerPlayer.GetComponent<NetworkObject>().IsOwner}, bucketOwner={Prop(ownerClientBucket, "IsOwner")}, bucketSpawned={Prop(ownerClientBucket, "IsSpawned")}, localId={owner.LocalClientId}, holder={IsHeld(ownerBucketObject.GetComponent(resourceType), owner.LocalClientId)}");
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return Until(() => FieldValue(hostBucket, "bucketActionCollectionSuppressed").Equals(true) &&
            FieldValue(ownerServerBucket, "bucketActionCollectionSuppressed").Equals(true), "both authoritative intents begin");

        // The non-holder cannot clear the host's active intent either.
        owner.SetSingleton();
        Call(Find(owner, hostBucketId).GetComponent(containerType), "SetBucketActionCollectionIntentServerRpc",
            false, new ServerRpcParams());
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return new WaitForSeconds(.15f);
        Assert.That(FieldValue(hostBucket, "bucketActionCollectionSuppressed"), Is.EqualTo(true),
            "The server ignores a spoofed release from another connected holder.");

        yield return MoveBothPlayers(5, .12f);
        Assert.That(Units(hostBucket), Is.EqualTo(hostUnitsBefore).Within(.00001f));
        Assert.That(Units(ownerServerBucket), Is.EqualTo(ownerUnitsBefore).Within(.00001f));
        Assert.That(Matches(serverPeer.Fields[0], hostShapeBefore), Is.True, "Host field is unchanged while its physical intent is held.");
        Assert.That(Matches(serverPeer.Fields[1], ownerShapeBefore), Is.True, "Remote field is unchanged while its physical intent is held.");

        owner.SetSingleton();
        Assert.That((bool)Call(Find(owner, ownerBucketId).GetComponent(containerType),
            "SetBucketActionCollectionIntent", ownerPlayer, false), Is.True);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return Until(() => FieldValue(ownerServerBucket, "bucketActionCollectionSuppressed").Equals(false),
            "remote physical release reaches server");
        yield return MoveBothPlayers(5, .12f);
        float ownerAfterRelease = Units(ownerServerBucket);
        float ownerFieldAfterRelease = PreciseUnits(serverPeer.Fields[1]);
        Assert.That(ownerAfterRelease, Is.GreaterThan(ownerUnitsBefore), "Remote-owner movement resumes collection after release.");
        Assert.That(ownerFieldBefore - ownerFieldAfterRelease,
            Is.EqualTo(ownerAfterRelease - ownerUnitsBefore).Within(.00002f));
        Assert.That(Units(hostBucket), Is.EqualTo(hostUnitsBefore).Within(.00001f),
            "The host remains suppressed until its own release.");
        Assert.That(PreciseUnits(serverPeer.Fields[0]), Is.EqualTo(hostFieldBefore).Within(.00001f));

        Assert.That((bool)Call(hostBucket, "SetBucketActionCollectionIntent",
            hostPlayer.GetComponent(playerInteractionType), false), Is.True);
        yield return Until(() => FieldValue(hostBucket, "bucketActionCollectionSuppressed").Equals(false),
            "host physical release clears its intent");
        yield return MoveBothPlayers(5, .12f);
        float hostAfterRelease = Units(hostBucket);
        float hostFieldAfterRelease = PreciseUnits(serverPeer.Fields[0]);
        Assert.That(hostAfterRelease, Is.GreaterThan(hostUnitsBefore));
        Assert.That(hostFieldBefore - hostFieldAfterRelease,
            Is.EqualTo(hostAfterRelease - hostUnitsBefore).Within(.00002f));

        // Drop while pressed clears intent and prevents collection from the former holder.
        owner.SetSingleton();
        Assert.That((bool)Call(Find(owner, ownerBucketId).GetComponent(containerType),
            "SetBucketActionCollectionIntent", ownerPlayer, true), Is.True);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return Until(() => FieldValue(ownerServerBucket, "bucketActionCollectionSuppressed").Equals(true),
            "remote intent rearmed before drop");
        float ownerBeforeDrop = Units(ownerServerBucket);
        float ownerFieldBeforeDrop = PreciseUnits(serverPeer.Fields[1]);
        Assert.That(Drop(ownerBucketObject.GetComponent(resourceType), owner.LocalClientId), Is.True);
        yield return Until(() => !IsHeld(ownerBucketObject.GetComponent(resourceType), owner.LocalClientId) &&
            FieldValue(ownerServerBucket, "bucketActionCollectionSuppressed").Equals(false), "drop clears remote intent");
        yield return MoveRemotePlayer(5, .12f);
        Assert.That(Units(ownerServerBucket), Is.EqualTo(ownerBeforeDrop).Within(.00001f));
        Assert.That(PreciseUnits(serverPeer.Fields[1]), Is.EqualTo(ownerFieldBeforeDrop).Within(.00001f));

        // Re-pick and begin again; disconnecting that actual holder also clears server state.
        Assert.That(CompletePickup(ownerBucketObject.GetComponent(resourceType), ownerServerPlayer, owner.LocalClientId), Is.True);
        yield return Until(() => IsHeld(ownerBucketObject.GetComponent(resourceType), owner.LocalClientId), "remote re-pick transaction");
        owner.SetSingleton();
        Component ownerRePickedResource = Find(owner, ownerBucketId).GetComponent(resourceType);
        Call(owner.LocalClient.PlayerObject.GetComponent(playerInteractionType), "ConfirmPickedUpObject",
            ownerRePickedResource.gameObject, ownerRePickedResource);
        Assert.That((bool)Call(Find(owner, ownerBucketId).GetComponent(containerType),
            "SetBucketActionCollectionIntent", ownerPlayer, true), Is.True);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        yield return Until(() => FieldValue(ownerServerBucket, "bucketActionCollectionSuppressed").Equals(true),
            "remote intent rearmed before disconnect");
        float ownerBeforeDisconnect = Units(ownerServerBucket);
        float ownerFieldBeforeDisconnect = PreciseUnits(serverPeer.Fields[1]);
        owner.Shutdown(true);
        yield return Until(() => host.ConnectedClientsIds.Count == 1 &&
            FieldValue(ownerServerBucket, "bucketActionCollectionSuppressed").Equals(false),
            "disconnect clears the server-held intent");
        yield return new WaitForSeconds(.35f);
        host.SetSingleton(); SetGameplaySingleton(serverGameplay);
        Assert.That(Units(ownerServerBucket), Is.EqualTo(ownerBeforeDisconnect).Within(.00001f));
        Assert.That(PreciseUnits(serverPeer.Fields[1]),
            Is.EqualTo(ownerFieldBeforeDisconnect).Within(.00001f));
#endif
    }
}
