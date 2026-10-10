using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class SingleCarryPlacementStage6PlayModeTests
{
    private static readonly Type PreviewType = Type.GetType("PlayerSingleCarryPlacementNetworkPreview, Assembly-CSharp");
    private static readonly Type PlacementControllerType = Type.GetType("PlayerSingleCarryPlacementController, Assembly-CSharp");
    private static readonly Type PlayerInputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
    private static readonly Type InteractionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
    private static readonly Type HealthType = Type.GetType("PlayerHealth, Assembly-CSharp");
    private static readonly Type PickableType = Type.GetType("IPIckableNew, Assembly-CSharp");
    private static readonly Type BaseResourceType = Type.GetType("BaseResourceNew, Assembly-CSharp");
    private static readonly Type BaseResourceProfileType = Type.GetType("BaseResourceSO, Assembly-CSharp");
    private static readonly Type BridgeType = Type.GetType("MountableBridgeComponent, Assembly-CSharp");
    private static readonly Type BridgeProfileType = Type.GetType("MountableBridgeComponentSO, Assembly-CSharp");
    private static readonly Type InventoryType = Type.GetType("PlayerInventory, Assembly-CSharp");
    private static readonly Type EquippableItemProfileType = Type.GetType("EquippableItemSO, Assembly-CSharp");
    private static readonly Type EquippableItemType = Type.GetType("EquippableItem, Assembly-CSharp");

    private NetworkManager priorSingleton;
    private bool capturedSingleton;
    private readonly List<NetworkManager> managers = new List<NetworkManager>();
    private readonly List<GameObject> roots = new List<GameObject>();
    private readonly List<GameObject> runtimeObjects = new List<GameObject>();
    private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
    private readonly List<NetworkObject> spawnedObjects = new List<NetworkObject>();
    private readonly HashSet<uint> additionalFixturePrefabHashes = new HashSet<uint>();
    private Keyboard syntheticKeyboard;
    private ScopedInputTestFixture isolatedInput;
    private Action<ulong> disconnectBoundaryObserver;
    private static Vector3 previousGravity;

    [UnitySetUp]
    public IEnumerator PreservePhysicsSettings()
    {
        previousGravity = Physics.gravity;
        Physics.gravity = Vector3.zero;
        isolatedInput = new ScopedInputTestFixture();
        isolatedInput.Begin();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        bool fixtureActionMapsDisabledBeforeDeviceRemoval = false;
        bool shutdownComplete = false;
        try
        {
            if (disconnectBoundaryObserver != null)
            {
                foreach (NetworkManager manager in managers)
                    if (manager != null) manager.OnClientDisconnectCallback -= disconnectBoundaryObserver;
                disconnectBoundaryObserver = null;
            }
            List<InputActionAsset> fixtureActionAssets = CaptureFixtureInputAssets();
            DisposeFixtureInputMaps(null);
            fixtureActionMapsDisabledBeforeDeviceRemoval = AreActionMapsDisabled(fixtureActionAssets);
            if (syntheticKeyboard != null)
            {
                InputSystem.RemoveDevice(syntheticKeyboard);
                syntheticKeyboard = null;
            }
            List<GameObject> networkCopies = CaptureFixtureNetworkCopies();
            foreach (NetworkObject obj in spawnedObjects)
            {
                if (obj == null || !obj.IsSpawned) continue;
                NetworkManager objectManager = obj.NetworkManager;
                if (objectManager != null && objectManager.IsServer) obj.Despawn(true);
            }
            foreach (GameObject networkCopy in networkCopies)
            {
                NetworkObject networkObject = networkCopy != null ? networkCopy.GetComponent<NetworkObject>() : null;
                if (networkObject == null || !networkObject.IsSpawned) continue;
                NetworkManager objectManager = networkObject.NetworkManager;
                if (objectManager != null && objectManager.IsServer) networkObject.Despawn(true);
            }
            for (int i = managers.Count - 1; i >= 0; i--)
                if (managers[i] != null && managers[i].IsListening) managers[i].Shutdown(true);
            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline && managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress)))
                yield return null;
            shutdownComplete = !managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress));
            foreach (GameObject root in roots) if (root != null) UnityEngine.Object.Destroy(root);
            foreach (GameObject networkCopy in networkCopies) if (networkCopy != null) UnityEngine.Object.Destroy(networkCopy);
            GameObject fixtureCamera = Camera.main != null ? Camera.main.gameObject : null;
            foreach (GameObject obj in runtimeObjects)
                if (obj != null && obj != fixtureCamera) UnityEngine.Object.Destroy(obj);
            yield return null;
            foreach (UnityEngine.Object asset in assets) if (asset != null) UnityEngine.Object.Destroy(asset);
            yield return null;
            if (fixtureCamera != null) UnityEngine.Object.Destroy(fixtureCamera);
            yield return null;
            if (capturedSingleton)
            {
                if (priorSingleton != null) priorSingleton.SetSingleton();
                else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            }
            managers.Clear(); roots.Clear(); runtimeObjects.Clear(); assets.Clear(); spawnedObjects.Clear();
            additionalFixturePrefabHashes.Clear();
            priorSingleton = null; capturedSingleton = false;
        }
        finally
        {
            try
            {
                isolatedInput?.End();
            }
            finally
            {
                isolatedInput = null;
                Physics.gravity = previousGravity;
            }
        }
        Assert.That(fixtureActionMapsDisabledBeforeDeviceRemoval, Is.True,
            "Stage6 fixture-owned maps must be disabled before removing the synthetic keyboard.");
        Assert.That(shutdownComplete, Is.True, "A fixture manager did not finish shutdown before transport teardown.");
    }

    private List<InputActionAsset> CaptureFixtureInputAssets()
    {
        var actionAssets = new List<InputActionAsset>();
        var seen = new HashSet<int>();
        foreach (NetworkManager manager in managers)
        {
            if (manager == null || manager.SpawnManager == null) continue;
            foreach (NetworkObject player in manager.SpawnManager.SpawnedObjects.Values)
            {
                if (player == null || !seen.Add(player.GetInstanceID())) continue;
                Component input = player.GetComponent(PlayerInputType);
                if (input == null) continue;
                var actionsField = PlayerInputType.GetField("playerGameInputActions", BindingFlags.Instance | BindingFlags.NonPublic);
                object actions = actionsField.GetValue(input);
                if (actions == null) continue;
                InputActionAsset asset = (InputActionAsset)actions.GetType().GetProperty("asset").GetValue(actions);
                actionAssets.Add(asset);
            }
        }
        return actionAssets;
    }

    private static bool AreActionMapsDisabled(List<InputActionAsset> assetsToCheck)
    {
        foreach (InputActionAsset asset in assetsToCheck)
        {
            if (asset == null) return false;
            foreach (InputActionMap map in asset.actionMaps)
                if (map.enabled) return false;
        }
        return true;
    }

    [UnityTest]
    public IEnumerator OwnerRequestCommitsExactServerPoseAndRejectsForgedOrOccupiedRequests()
    {
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True,
            "The isolated loopback fixture cannot replace an already-listening project manager.");
        Assert.That(PreviewType, Is.Not.Null);
        priorSingleton = NetworkManager.Singleton; capturedSingleton = true;

        // ScriptableObject.CreateInstance(Type) returns UnityEngine.Object; retain it in the cleanup set.
        UnityEngine.Object resourceProfileObject = ScriptableObject.CreateInstance(BaseResourceProfileType);
        assets.Add(resourceProfileObject);
        resourceProfileObject.GetType().GetField("canBeCarried").SetValue(resourceProfileObject, true);
        resourceProfileObject.GetType().GetField("allowMultipleCarriers").SetValue(resourceProfileObject, false);

        UnityEngine.Object bridgeProfile = ScriptableObject.CreateInstance(BridgeProfileType);
        assets.Add(bridgeProfile);
        bridgeProfile.GetType().GetField("allowMultipleCarriers").SetValue(bridgeProfile, false);

        GameObject playerPrefab = CreatePlayerPrefab();
        GameObject resourcePrefab = CreateSourcePrefab("Stage 6 resource", BaseResourceType, resourceProfileObject, 0x61A60011);
        GameObject bridgePrefab = CreateSourcePrefab("Stage 6 bridge", BridgeType, bridgeProfile, 0x61A60012);
        Assert.That(GetPrefabHash(playerPrefab.GetComponent<NetworkObject>()), Is.EqualTo(0x61A60001u));
        ushort port = ReservePort();
        NetworkManager host = CreateManager("Stage 6 host", playerPrefab, new[] { resourcePrefab, bridgePrefab }, port);
        Assert.That(host.StartHost(), Is.True); host.SetSingleton();
        yield return Until(() => host.LocalClient?.PlayerObject != null, "host player spawn");
        Assert.That(GetPrefabHash(host.LocalClient.PlayerObject), Is.EqualTo(0x61A60001u), "The host must spawn the Stage6 player prefab.");

        NetworkManager ownerA = CreateManager("Stage 6 resource owner", playerPrefab, new[] { resourcePrefab, bridgePrefab }, port);
        Assert.That(ownerA.StartClient(), Is.True);
        yield return Until(() => ownerA.IsConnectedClient && ownerA.LocalClient?.PlayerObject != null && host.ConnectedClientsIds.Count >= 2,
            "resource owner connection");
        ulong ownerAId = ownerA.LocalClientId;

        NetworkManager ownerB = CreateManager("Stage 6 bridge owner and observer", playerPrefab, new[] { resourcePrefab, bridgePrefab }, port);
        Assert.That(ownerB.StartClient(), Is.True);
        yield return Until(() => ownerB.IsConnectedClient && ownerB.LocalClient?.PlayerObject != null && host.ConnectedClientsIds.Count >= 3,
            "observer connection");
        DisposeFixtureInputMaps(null);
        ulong ownerBId = ownerB.LocalClientId;
        foreach (NetworkClient connectedClient in host.ConnectedClientsList)
        {
            if (connectedClient.ClientId == host.LocalClientId || connectedClient.PlayerObject == null) continue;
            ((Behaviour)connectedClient.PlayerObject.GetComponent(InteractionType)).enabled = false;
        }

        GameObject resource = UnityEngine.Object.Instantiate(resourcePrefab);
        runtimeObjects.Add(resource);
        resource.transform.position = new Vector3(20f, 4f, 20f);
        NetworkObject resourceNetObj = resource.GetComponent<NetworkObject>(); spawnedObjects.Add(resourceNetObj);
        resourceNetObj.Spawn();
        GameObject bridge = UnityEngine.Object.Instantiate(bridgePrefab);
        runtimeObjects.Add(bridge);
        bridge.transform.position = new Vector3(24f, 4f, 20f);
        NetworkObject bridgeNetObj = bridge.GetComponent<NetworkObject>(); spawnedObjects.Add(bridgeNetObj);
        bridgeNetObj.Spawn();
        yield return Until(() => Find(ownerA, resourceNetObj.NetworkObjectId) != null
            && Find(ownerB, resourceNetObj.NetworkObjectId) != null && Find(ownerB, bridgeNetObj.NetworkObjectId) != null,
            "sources replicated to owners and observer");
        DisableMirrorColliders(ownerA, resourceNetObj.NetworkObjectId, host);
        DisableMirrorColliders(ownerB, resourceNetObj.NetworkObjectId, host);
        DisableMirrorColliders(ownerA, bridgeNetObj.NetworkObjectId, host);
        DisableMirrorColliders(ownerB, bridgeNetObj.NetworkObjectId, host);

        Assert.That(InvokePickup(resource, ownerAId), Is.True, "Server must register the resource owner before placement.");
        Assert.That(InvokePickup(bridge, ownerBId), Is.True, "Server must register the bridge owner before placement.");
        ConfirmLocalPickup(ownerA, resourceNetObj.NetworkObjectId);
        ConfirmLocalPickup(ownerB, bridgeNetObj.NetworkObjectId);
        yield return Until(() => resourceNetObj.OwnerClientId == ownerAId && bridgeNetObj.OwnerClientId == ownerBId,
            "server transfers each held source to its holder");
        yield return Until(() => resourceNetObj.OwnerClientId == ownerAId && bridgeNetObj.OwnerClientId == ownerBId
            && GetHeld(ownerA.LocalClient.PlayerObject) == Find(ownerA, resourceNetObj.NetworkObjectId).gameObject
            && GetHeld(ownerB.LocalClient.PlayerObject) == Find(ownerB, bridgeNetObj.NetworkObjectId).gameObject,
            "server and owners agree on actual held objects");

        Vector3 originA = GetAimAnchor(ownerA.LocalClient.PlayerObject).position;
        Vector3 originB = GetAimAnchor(ownerB.LocalClient.PlayerObject).position;
        Quaternion rotation = Quaternion.Euler(0f, 37f, 0f);
        Vector3 sharedPose = originA + Vector3.forward * 2f;
        PreviewType.GetMethod("SetOwnerPreview").Invoke(Preview(ownerA.LocalClient.PlayerObject),
            new object[] { Find(ownerA, resourceNetObj.NetworkObjectId).gameObject, sharedPose, rotation, true });
        PreviewType.GetMethod("SetOwnerPreview").Invoke(Preview(ownerB.LocalClient.PlayerObject),
            new object[] { Find(ownerB, bridgeNetObj.NetworkObjectId).gameObject, sharedPose, rotation, true });
        yield return Until(() => PreviewActive(Preview(host.ConnectedClients[ownerAId].PlayerObject))
            && PreviewActive(Preview(host.ConnectedClients[ownerBId].PlayerObject)), "both replicated placement previews");

        // Another connected owner cannot place a source held by someone else.
        Send(ownerB, Preview(ownerB.LocalClient.PlayerObject), resourceNetObj, 1, sharedPose, rotation,
            new Ray(originB, Vector3.forward));
        yield return Until(() => LastRequest(Preview(ownerB.LocalClient.PlayerObject)) == 1, "wrong-holder rejection");
        AssertResult(ownerB.LocalClient.PlayerObject, "IneligibleHolder");
        Assert.That(resourceNetObj.OwnerClientId, Is.EqualTo(ownerAId));

        // A distant client origin cannot redirect the server query.
        Send(ownerA, Preview(ownerA.LocalClient.PlayerObject), resourceNetObj, 1, sharedPose, rotation,
            new Ray(originA + Vector3.right, Vector3.forward));
        yield return Until(() => LastRequest(Preview(ownerA.LocalClient.PlayerObject)) == 1, "fake aim-origin rejection");
        AssertResult(ownerA.LocalClient.PlayerObject, "InvalidAimOrigin");
        AssertHeld(ownerA, resourceNetObj);
        Vector3 heldPoseBeforeForgedOrigins = resourceNetObj.transform.position;

        // A ray may start at the trusted anchor or at the known 0.2m camera near plane,
        // with only the bounded 0.15m snapshot/feedback residual. Far-forward and lateral
        // offsets must still be rejected before geometry or source state can change.
        Send(ownerA, Preview(ownerA.LocalClient.PlayerObject), resourceNetObj, 2, sharedPose, rotation,
            new Ray(originA + Vector3.forward * 0.6f, Vector3.forward));
        yield return Until(() => LastRequest(Preview(ownerA.LocalClient.PlayerObject)) == 2,
            "excessive forward aim-origin rejection");
        AssertResult(ownerA.LocalClient.PlayerObject, "InvalidAimOrigin");
        AssertHeld(ownerA, resourceNetObj);
        Assert.That(resourceNetObj.OwnerClientId, Is.EqualTo(ownerAId));
        Assert.That(Vector3.Distance(resourceNetObj.transform.position, heldPoseBeforeForgedOrigins), Is.LessThan(0.001f));

        Send(ownerA, Preview(ownerA.LocalClient.PlayerObject), resourceNetObj, 3, sharedPose, rotation,
            new Ray(originA + Vector3.forward * 0.2f + Vector3.right * 0.3f, Vector3.forward));
        yield return Until(() => LastRequest(Preview(ownerA.LocalClient.PlayerObject)) == 3,
            "lateral near-plane aim-origin rejection");
        AssertResult(ownerA.LocalClient.PlayerObject, "InvalidAimOrigin");
        AssertHeld(ownerA, resourceNetObj);
        Assert.That(resourceNetObj.OwnerClientId, Is.EqualTo(ownerAId));
        Assert.That(Vector3.Distance(resourceNetObj.transform.position, heldPoseBeforeForgedOrigins), Is.LessThan(0.001f));

        // A finite but forged pose must match the shared ray solver's answer.
        Send(ownerA, Preview(ownerA.LocalClient.PlayerObject), resourceNetObj, 4, sharedPose + Vector3.right, rotation,
            new Ray(originA, Vector3.forward));
        yield return Until(() => LastRequest(Preview(ownerA.LocalClient.PlayerObject)) == 4, "forged pose rejection");
        AssertResult(ownerA.LocalClient.PlayerObject, "InvalidPose");
        AssertHeld(ownerA, resourceNetObj);

        Send(ownerA, Preview(ownerA.LocalClient.PlayerObject), resourceNetObj, 5, sharedPose, rotation,
            new Ray(originA, new Vector3(float.NaN, 0f, 1f)));
        yield return Until(() => LastRequest(Preview(ownerA.LocalClient.PlayerObject)) == 5
            && ResultName(ownerA.LocalClient.PlayerObject) == "InvalidRequest", "non-finite aim rejection");
        AssertHeld(ownerA, resourceNetObj);

        // Holder colliders block placement even though geometry's aim ray ignores the holder hierarchy.
        GameObject holderBlocker = new GameObject("Stage 6 holder placement blocker");
        runtimeObjects.Add(holderBlocker);
        holderBlocker.transform.SetParent(host.ConnectedClients[ownerAId].PlayerObject.transform, true);
        holderBlocker.transform.position = sharedPose + Vector3.right * 0.60f;
        holderBlocker.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
        Physics.SyncTransforms();
        Send(ownerA, Preview(ownerA.LocalClient.PlayerObject), resourceNetObj, 6, sharedPose, rotation,
            new Ray(originA, Vector3.forward));
        yield return Until(() => LastRequest(Preview(ownerA.LocalClient.PlayerObject)) == 6
            && ResultName(ownerA.LocalClient.PlayerObject) == "Occupied", "holder collider occupancy rejection");
        AssertHeld(ownerA, resourceNetObj);
        UnityEngine.Object.Destroy(holderBlocker);
        yield return null;
        Physics.SyncTransforms();

        GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        runtimeObjects.Add(blocker);
        blocker.name = "Stage 6 server-only placement blocker";
        blocker.transform.SetPositionAndRotation(sharedPose + Vector3.right * 0.70f, Quaternion.identity);
        blocker.transform.localScale = Vector3.one * 0.25f;
        Physics.SyncTransforms();
        Send(ownerA, Preview(ownerA.LocalClient.PlayerObject), resourceNetObj, 7, sharedPose, rotation,
            new Ray(originA, Vector3.forward));
        yield return Until(() => LastRequest(Preview(ownerA.LocalClient.PlayerObject)) == 7
            && ResultName(ownerA.LocalClient.PlayerObject) == "Occupied", "server occupancy rejection");
        AssertHeld(ownerA, resourceNetObj);
        UnityEngine.Object.Destroy(blocker);
        yield return null;
        Physics.SyncTransforms();

        // With two peer requests in flight, only one source can claim the same air position.
        Vector3 resourceHeldPoseBeforePlacement = resourceNetObj.transform.position;
        Vector3 bridgeHeldPoseBeforePlacement = bridgeNetObj.transform.position;
        Send(ownerA, Preview(ownerA.LocalClient.PlayerObject), resourceNetObj, 8, sharedPose, rotation,
            new Ray(originA, Vector3.forward));
        Send(ownerB, Preview(ownerB.LocalClient.PlayerObject), bridgeNetObj, 2, sharedPose, rotation,
            new Ray(originB, Vector3.forward));
        yield return Until(() => LastRequest(Preview(ownerA.LocalClient.PlayerObject)) == 8
            && LastRequest(Preview(ownerB.LocalClient.PlayerObject)) == 2, "both contending placement responses");
        string resultA = ResultName(ownerA.LocalClient.PlayerObject);
        string resultB = ResultName(ownerB.LocalClient.PlayerObject);
        Assert.That((resultA == "Accepted" ? 1 : 0) + (resultB == "Accepted" ? 1 : 0), Is.EqualTo(1),
            "Exactly one source may commit the contested position.");
        NetworkObject winner = resultA == "Accepted" ? Find(host, resourceNetObj.NetworkObjectId) : Find(host, bridgeNetObj.NetworkObjectId);
        NetworkObject loser = resultA == "Accepted" ? Find(host, bridgeNetObj.NetworkObjectId) : Find(host, resourceNetObj.NetworkObjectId);
        Assert.That(winner.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(loser.OwnerClientId, Is.EqualTo(resultA == "Accepted" ? ownerBId : ownerAId));
        Assert.That(Vector3.Distance(winner.transform.position, sharedPose), Is.LessThan(0.003f));
        Assert.That(GetBody(winner.gameObject).linearVelocity.sqrMagnitude, Is.LessThan(0.000001f));
        Assert.That(GetBody(winner.gameObject).angularVelocity.sqrMagnitude, Is.LessThan(0.000001f));
        Assert.That(GetBody(winner.gameObject).useGravity, Is.True);
        Assert.That(Quaternion.Angle(winner.transform.rotation, rotation), Is.LessThan(0.1f));

        // The loser remains held and can choose another valid point. This also exercises both backends.
        NetworkManager loserOwner = resultA == "Accepted" ? ownerB : ownerA;
        ulong loserId = loserOwner.LocalClientId;
        Vector3 loserOrigin = GetAimAnchor(loserOwner.LocalClient.PlayerObject).position;
        Vector3 secondPose = loserOrigin + Vector3.right * 2f;
        ulong retryId = LastRequest(Preview(loserOwner.LocalClient.PlayerObject)) + 1;
        Send(loserOwner, Preview(loserOwner.LocalClient.PlayerObject), loser, retryId, secondPose, rotation,
            new Ray(loserOrigin, Vector3.right));
        yield return Until(() => LastRequest(Preview(loserOwner.LocalClient.PlayerObject)) == retryId,
            "loser retry response");
        AssertResult(loserOwner.LocalClient.PlayerObject, "Accepted");
        Assert.That(loser.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(Vector3.Distance(loser.transform.position, secondPose), Is.LessThan(0.003f));
        Assert.That(Quaternion.Angle(loser.transform.rotation, rotation), Is.LessThan(0.1f));

        yield return Until(() => Find(ownerA, resourceNetObj.NetworkObjectId).OwnerClientId == NetworkManager.ServerClientId
            && Find(ownerB, resourceNetObj.NetworkObjectId).OwnerClientId == NetworkManager.ServerClientId
            && Find(ownerA, bridgeNetObj.NetworkObjectId).OwnerClientId == NetworkManager.ServerClientId
            && Find(ownerB, bridgeNetObj.NetworkObjectId).OwnerClientId == NetworkManager.ServerClientId
            && Vector3.Distance(Find(ownerA, winner.NetworkObjectId).transform.position, sharedPose) < 0.02f
            && Vector3.Distance(Find(ownerB, winner.NetworkObjectId).transform.position, sharedPose) < 0.02f
            && Vector3.Distance(Find(ownerA, loser.NetworkObjectId).transform.position, secondPose) < 0.02f
            && Vector3.Distance(Find(ownerB, loser.NetworkObjectId).transform.position, secondPose) < 0.02f
            && Quaternion.Angle(Find(ownerA, resourceNetObj.NetworkObjectId).transform.rotation, rotation) < 0.1f
            && Quaternion.Angle(Find(ownerB, resourceNetObj.NetworkObjectId).transform.rotation, rotation) < 0.1f
            && Quaternion.Angle(Find(ownerA, bridgeNetObj.NetworkObjectId).transform.rotation, rotation) < 0.1f
            && Quaternion.Angle(Find(ownerB, bridgeNetObj.NetworkObjectId).transform.rotation, rotation) < 0.1f,
            "all peers receive exact server pose and rotation");
        yield return Until(() => !IsPickedUp(Find(host, resourceNetObj.NetworkObjectId))
            && !IsPickedUp(Find(host, bridgeNetObj.NetworkObjectId))
            && !IsPickedUp(Find(ownerA, resourceNetObj.NetworkObjectId))
            && !IsPickedUp(Find(ownerA, bridgeNetObj.NetworkObjectId))
            && !IsPickedUp(Find(ownerB, resourceNetObj.NetworkObjectId))
            && !IsPickedUp(Find(ownerB, bridgeNetObj.NetworkObjectId)),
            "all peers clear carried state");
        yield return Until(() => !PreviewActive(Preview(host.ConnectedClients[ownerAId].PlayerObject))
            && !PreviewActive(Preview(host.ConnectedClients[ownerBId].PlayerObject))
            && !PreviewActive(Preview(ownerA.LocalClient.PlayerObject))
            && !PreviewActive(Preview(ownerB.LocalClient.PlayerObject)),
            "accepted placement clears replicated previews");
        NetworkObject[] committedObjects = { winner, loser };
        Vector3[] committedPoses = { sharedPose, secondPose };
        Vector3[] previousHeldPoses = resultA == "Accepted"
            ? new[] { resourceHeldPoseBeforePlacement, bridgeHeldPoseBeforePlacement }
            : new[] { bridgeHeldPoseBeforePlacement, resourceHeldPoseBeforePlacement };
        float observationDeadline = Time.realtimeSinceStartup + 5f;
        int observedTicks = 0;
        while (observedTicks < 10)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(observationDeadline),
                "The repeated replication observations exceeded their bounded safety deadline.");
            yield return new WaitForSecondsRealtime(0.06f);
            observedTicks++;
            foreach (NetworkManager observer in new[] { ownerA, ownerB })
            {
                for (int i = 0; i < committedObjects.Length; i++)
                {
                    NetworkObject observed = Find(observer, committedObjects[i].NetworkObjectId);
                    Assert.That(Vector3.Distance(observed.transform.position, committedPoses[i]), Is.LessThan(0.02f),
                        $"Released pose changed on peer {observer.name} at sample {observedTicks}; a queued previous-owner update may have snapped it back.");
                    Assert.That(Quaternion.Angle(observed.transform.rotation, rotation), Is.LessThan(0.1f),
                        $"Released rotation changed on peer {observer.name} at sample {observedTicks}.");
                    if (Vector3.Distance(previousHeldPoses[i], committedPoses[i]) > 0.1f)
                        Assert.That(Vector3.Distance(observed.transform.position, previousHeldPoses[i]), Is.GreaterThan(0.1f),
                            $"Released object returned near its prior held pose on peer {observer.name} at sample {observedTicks}.");
                }
            }
        }
        Assert.That(observedTicks, Is.EqualTo(10), "The ownership-transfer regression must inspect several network ticks.");
        Assert.That(GetHeld(loserOwner.LocalClient.PlayerObject), Is.Null, "Accepted release clears local holder bookkeeping.");
        Assert.That(Find(ownerA, winner.NetworkObjectId).transform.parent, Is.Null, "Owner-side held-object parenting must be detached.");
        Assert.That(Find(ownerA, loser.NetworkObjectId).transform.parent, Is.Null, "Loser-side held-object parenting must be detached after retry.");

        // Stale duplicate IDs are rejected without a second mutation.
        Send(ownerA, Preview(ownerA.LocalClient.PlayerObject), resourceNetObj, 5, sharedPose, rotation,
            new Ray(originA, Vector3.forward));
        yield return Until(() => LastRequest(Preview(ownerA.LocalClient.PlayerObject)) == 5
            && ResultName(ownerA.LocalClient.PlayerObject) == "StaleRequest", "duplicate request response");
        AssertResult(ownerA.LocalClient.PlayerObject, "StaleRequest");
        yield return null;
    }

    [UnityTest]
    public IEnumerator DisconnectingRemoteOwnerPreservesHeldWorldResourceAndDropsReservedTwoSlotToolOnce()
    {
#if !UNITY_EDITOR
        Assert.Ignore("The disconnect fixture requires editor-only access to the actual two-slot tool assets.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        priorSingleton = NetworkManager.Singleton; capturedSingleton = true;

        // The real equippable prefab includes a HUD LookAtCamera that reads Camera.main during
        // LateUpdate, so provide the same camera context before creating any prefab instances.
        GameObject cameraObject = new GameObject("Disconnect fixture MainCamera");
        runtimeObjects.Add(cameraObject);
        cameraObject.tag = "MainCamera";
        cameraObject.AddComponent<Camera>().nearClipPlane = 0.2f;

        UnityEngine.Object toolAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
            "Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        UnityEngine.Object axeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
            "Assets/ScriptableObjectAssets/New/Axe.asset");
        UnityEngine.Object pickaxeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
            "Assets/ScriptableObjectAssets/New/Pickaxe.asset");
        GameObject toolPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/EquippableItems/IndustrialHammer.prefab");
        GameObject axePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/EquippableItems/Axe.prefab");
        GameObject pickaxePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/EquippableItems/Pickaxe.prefab");
        GameObject sharedLogAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/WoodenLogResource_prefab.prefab");
        Assert.That(toolAsset, Is.Not.Null);
        Assert.That(axeAsset, Is.Not.Null);
        Assert.That(pickaxeAsset, Is.Not.Null);
        Assert.That(toolPrefab, Is.Not.Null);
        Assert.That(axePrefab, Is.Not.Null);
        Assert.That(pickaxePrefab, Is.Not.Null);
        Assert.That(sharedLogAsset, Is.Not.Null);
        Assert.That((int)EquippableItemProfileType.GetField("inventorySlotsRequired").GetValue(toolAsset), Is.EqualTo(2));
        Assert.That((int)EquippableItemProfileType.GetField("inventorySlotsRequired").GetValue(axeAsset), Is.EqualTo(1));
        Assert.That((int)EquippableItemProfileType.GetField("inventorySlotsRequired").GetValue(pickaxeAsset), Is.EqualTo(1));
        NetworkObject toolNetworkPrefab = toolPrefab.GetComponent<NetworkObject>();
        NetworkObject axeNetworkPrefab = axePrefab.GetComponent<NetworkObject>();
        NetworkObject pickaxeNetworkPrefab = pickaxePrefab.GetComponent<NetworkObject>();
        Assert.That(toolNetworkPrefab, Is.Not.Null, "The actual two-slot tool drop prefab must be network-spawnable.");
        additionalFixturePrefabHashes.Add(GetPrefabHash(toolNetworkPrefab));
        additionalFixturePrefabHashes.Add(GetPrefabHash(axeNetworkPrefab));
        additionalFixturePrefabHashes.Add(GetPrefabHash(pickaxeNetworkPrefab));
        GameObject sharedLogPrefab = UnityEngine.Object.Instantiate(sharedLogAsset);
        sharedLogPrefab.name = "Stage6 actual shared Wooden Log prefab";
        sharedLogPrefab.transform.position = Vector3.one * 10000f;
        assets.Add(sharedLogPrefab);
        Collider[] sharedLogTemplateColliders = sharedLogPrefab.GetComponentsInChildren<Collider>(true);
        bool[] sharedLogColliderEnabled = new bool[sharedLogTemplateColliders.Length];
        for (int i = 0; i < sharedLogTemplateColliders.Length; i++)
        {
            sharedLogColliderEnabled[i] = sharedLogTemplateColliders[i].enabled;
            sharedLogTemplateColliders[i].enabled = false;
        }
        Rigidbody sharedLogTemplateBody = sharedLogPrefab.GetComponent<Rigidbody>();
        Assert.That(sharedLogTemplateBody, Is.Not.Null, "The actual Wooden Log prefab must retain its Rigidbody.");
        bool sharedLogUseGravity = sharedLogTemplateBody.useGravity;
        bool sharedLogIsKinematic = sharedLogTemplateBody.isKinematic;
        sharedLogTemplateBody.isKinematic = true;
        sharedLogTemplateBody.useGravity = false;
        NetworkObject sharedLogNetworkPrefab = sharedLogPrefab.GetComponent<NetworkObject>();
        Assert.That(sharedLogNetworkPrefab, Is.Not.Null, "The actual shared Wooden Log must be network-spawnable.");
        // Keep the asset's genuine NGO prefab identity; this fixture must not invent
        // source-hash metadata for an imported prefab.
        additionalFixturePrefabHashes.Add(GetPrefabHash(sharedLogNetworkPrefab));

        UnityEngine.Object resourceProfile = ScriptableObject.CreateInstance(BaseResourceProfileType);
        assets.Add(resourceProfile);
        resourceProfile.GetType().GetField("canBeCarried").SetValue(resourceProfile, true);
        resourceProfile.GetType().GetField("allowMultipleCarriers").SetValue(resourceProfile, false);
        UnityEngine.Object bridgeProfile = ScriptableObject.CreateInstance(BridgeProfileType);
        assets.Add(bridgeProfile);
        bridgeProfile.GetType().GetField("allowMultipleCarriers").SetValue(bridgeProfile, false);
        GameObject playerPrefab = CreatePlayerPrefab();
        CharacterController fixtureCharacterController = playerPrefab.AddComponent<CharacterController>();
        fixtureCharacterController.height = 1.8f;
        fixtureCharacterController.radius = 0.3f;
        fixtureCharacterController.center = new Vector3(0f, 0.9f, 0f);
        Component inventoryPrefab = playerPrefab.AddComponent(InventoryType);
        Array toolCatalog = Array.CreateInstance(EquippableItemProfileType, 3);
        toolCatalog.SetValue(toolAsset, 0);
        toolCatalog.SetValue(axeAsset, 1);
        toolCatalog.SetValue(pickaxeAsset, 2);
        InventoryType.GetField("equippableItemCatalog", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(inventoryPrefab, toolCatalog);
        GameObject resourcePrefab = CreateSourcePrefab("Disconnect source resource", BaseResourceType,
            resourceProfile, 0x61A60011);
        GameObject bridgePrefab = CreateSourcePrefab("Disconnect mountable component", BridgeType,
            bridgeProfile, 0x61A60012);
        ushort port = ReservePort();
        NetworkManager host = CreateManager("Disconnect cleanup host", playerPrefab,
            new[] { resourcePrefab, bridgePrefab, sharedLogPrefab, toolPrefab, axePrefab, pickaxePrefab }, port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.LocalClient?.PlayerObject != null, "disconnect host player");
        NetworkManager owner = CreateManager("Disconnect cleanup owner", playerPrefab,
            new[] { resourcePrefab, bridgePrefab, sharedLogPrefab, toolPrefab, axePrefab, pickaxePrefab }, port);
        Assert.That(owner.StartClient(), Is.True);
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 2, "disconnect owner connection");
        NetworkManager observer = CreateManager("Disconnect cleanup observer", playerPrefab,
            new[] { resourcePrefab, bridgePrefab, sharedLogPrefab, toolPrefab, axePrefab, pickaxePrefab }, port);
        Assert.That(observer.StartClient(), Is.True);
        yield return Until(() => observer.IsConnectedClient && observer.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 3, "disconnect observer connection");
        // Join all fixture peers before spawning runtime-only manually-hashed sources. NGO
        // includes existing runtime objects in late-join approval; this fixture does not need
        // to test how manually assembled test prefabs serialize their source-hash metadata.
        NetworkManager sharedOwner = CreateManager("Disconnect shared-carry owner", playerPrefab,
            new[] { resourcePrefab, bridgePrefab, sharedLogPrefab, toolPrefab, axePrefab, pickaxePrefab }, port);
        Assert.That(sharedOwner.StartClient(), Is.True);
        yield return Until(() => sharedOwner.IsConnectedClient && sharedOwner.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 4, "shared-carry owner connection");
        NetworkManager inventoryOwner = CreateManager("Disconnect inventory owner", playerPrefab,
            new[] { resourcePrefab, bridgePrefab, sharedLogPrefab, toolPrefab, axePrefab, pickaxePrefab }, port);
        Assert.That(inventoryOwner.StartClient(), Is.True);
        yield return Until(() => inventoryOwner.IsConnectedClient && inventoryOwner.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 5, "second inventory owner connection");
        DisableDisconnectFixtureRemotePlayerPhysics(host, new[] { owner, observer, sharedOwner, inventoryOwner });
        DisposeFixtureInputMaps(null);

        GameObject source = UnityEngine.Object.Instantiate(resourcePrefab);
        runtimeObjects.Add(source);
        source.transform.position = new Vector3(15f, 3f, 11f);
        NetworkObject sourceNetworkObject = source.GetComponent<NetworkObject>();
        spawnedObjects.Add(sourceNetworkObject);
        sourceNetworkObject.Spawn();
        yield return Until(() => Find(owner, sourceNetworkObject.NetworkObjectId) != null
            && Find(observer, sourceNetworkObject.NetworkObjectId) != null, "held source replication");
        Assert.That(InvokePickup(source, owner.LocalClientId), Is.True);
        ConfirmLocalPickup(owner, sourceNetworkObject.NetworkObjectId);
        yield return Until(() => sourceNetworkObject.OwnerClientId == owner.LocalClientId
            && GetHeld(owner.LocalClient.PlayerObject) == Find(owner, sourceNetworkObject.NetworkObjectId).gameObject,
            "server and owner agree on held resource");
        DisableDisconnectFixtureMirrorColliders(new[] { owner, observer, sharedOwner, inventoryOwner }, sourceNetworkObject.NetworkObjectId);

        GameObject bridge = UnityEngine.Object.Instantiate(bridgePrefab);
        runtimeObjects.Add(bridge);
        bridge.transform.position = new Vector3(18f, 3f, 11f);
        NetworkObject bridgeNetworkObject = bridge.GetComponent<NetworkObject>();
        spawnedObjects.Add(bridgeNetworkObject);
        bridgeNetworkObject.Spawn();
        yield return Until(() => Find(owner, bridgeNetworkObject.NetworkObjectId) != null
            && Find(observer, bridgeNetworkObject.NetworkObjectId) != null, "held mountable replication");
        Assert.That(InvokePickup(bridge, owner.LocalClientId), Is.True);
        ConfirmLocalPickup(owner, bridgeNetworkObject.NetworkObjectId);
        yield return Until(() => bridgeNetworkObject.OwnerClientId == owner.LocalClientId,
            "server and owner agree on held mountable");
        DisableDisconnectFixtureMirrorColliders(new[] { owner, observer, sharedOwner, inventoryOwner }, bridgeNetworkObject.NetworkObjectId);
        Vector3 bridgeHeldPosition = bridge.transform.position;
        Quaternion bridgeHeldRotation = bridge.transform.rotation;

        Component serverInventory = host.ConnectedClients[owner.LocalClientId].PlayerObject.GetComponent(InventoryType);
        Component ownerInventory = owner.LocalClient.PlayerObject.GetComponent(InventoryType);
        Assert.That((bool)InventoryType.GetMethod("AddItem").Invoke(ownerInventory, new[] { toolAsset }), Is.True,
            "Seed the remote owner's inventory through its normal owner-side AddItem path.");
        yield return Until(() => (int)InventoryType.GetMethod("GetNetworkSlotItemTypeValue").Invoke(serverInventory, new object[] { 0 })
                == (int)EquippableItemProfileType.GetField("itemType").GetValue(toolAsset)
            && (int)InventoryType.GetMethod("GetNetworkSlotItemTypeValue").Invoke(serverInventory, new object[] { 1 }) == -2,
            "server inventory slot replication");
        FieldInfo inventoryItemsField = InventoryType.GetField("inventoryItems", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(inventoryItemsField.GetValue(serverInventory), Is.Not.Null);
        Assert.That(((Array)inventoryItemsField.GetValue(serverInventory)).GetValue(0), Is.Null,
            "The server must exercise replicated slot state rather than a populated private inventory array.");
        Assert.That((int)InventoryType.GetMethod("GetNetworkSlotItemTypeValue").Invoke(serverInventory, new object[] { 1 }),
            Is.EqualTo(-2), "The reserved slot must not become a duplicate drop.");

        ulong sourceId = sourceNetworkObject.NetworkObjectId;
        ulong ownerClientId = owner.LocalClientId;
        Vector3 serverKnownPlayerPosition = host.ConnectedClients[ownerClientId].PlayerObject.transform.position;
        Vector3 initialResourceVelocity = Vector3.one * float.NaN;
        Vector3 initialMountableVelocity = Vector3.one * float.NaN;
        Vector3 initialMountableAngularVelocity = Vector3.one * float.NaN;
        bool observedDisconnectBoundary = false;
        disconnectBoundaryObserver = clientId =>
        {
            if (clientId != ownerClientId || observedDisconnectBoundary) return;
            observedDisconnectBoundary = true;
            NetworkObject releasedSource = Find(host, sourceId);
            NetworkObject releasedMountable = Find(host, bridgeNetworkObject.NetworkObjectId);
            if (releasedSource != null) initialResourceVelocity = GetBody(releasedSource.gameObject).linearVelocity;
            if (releasedMountable != null)
            {
                Rigidbody mountableBody = GetBody(releasedMountable.gameObject);
                initialMountableVelocity = mountableBody.linearVelocity;
                initialMountableAngularVelocity = mountableBody.angularVelocity;
            }
        };
        host.OnClientDisconnectCallback += disconnectBoundaryObserver;
        host.DisconnectClient(ownerClientId);
        yield return Until(() => host.ConnectedClientsIds.Count == 4, "actual NGO owner disconnect cleanup");
        yield return Until(() => Find(host, sourceId) != null
            && !IsPickedUp(Find(host, sourceId))
            && Find(host, bridgeNetworkObject.NetworkObjectId) != null
            && Find(host, bridgeNetworkObject.NetworkObjectId).OwnerClientId == NetworkManager.ServerClientId
            && !IsPickedUp(Find(host, bridgeNetworkObject.NetworkObjectId))
            && observedDisconnectBoundary,
            "disconnect single-source and mountable release");

        NetworkObject remainingSource = Find(host, sourceId);
        var droppedTools = new List<NetworkObject>();
        foreach (NetworkObject spawned in host.SpawnManager.SpawnedObjectsList)
        {
            if (spawned == null || !spawned.gameObject.TryGetComponent(EquippableItemType, out Component itemComponent)) continue;
            object itemProfile = EquippableItemType.GetMethod("GetEquippableItemSO").Invoke(itemComponent, null);
            if (itemProfile == toolAsset) droppedTools.Add(spawned);
        }
        Assert.That(remainingSource, Is.Not.Null, "A client-owned held resource must survive NGO's disconnect cleanup.");
        Assert.That(initialResourceVelocity.sqrMagnitude, Is.LessThan(0.000001f),
            "The resource has zero linear velocity in the same-frame disconnect callback after recovery.");
        Assert.That(droppedTools.Count, Is.EqualTo(1), "Only the occupied two-slot tool entry should spawn; its reserved slot is skipped.");
        Assert.That(Vector3.Distance(droppedTools[0].transform.position, serverKnownPlayerPosition + Vector3.up),
            Is.LessThan(2f), "An inventory recovery drop must remain near the server's last player position.");
        if (remainingSource != null)
        {
            Assert.That(remainingSource.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
            Assert.That((bool)BaseResourceType.GetProperty("IsPickedUp").GetValue(remainingSource.GetComponent(BaseResourceType)), Is.False);
            Assert.That(remainingSource.GetComponent<Rigidbody>().isKinematic, Is.False);
            Assert.That(remainingSource.GetComponent<Rigidbody>().useGravity, Is.True);
            NetworkObject observerSource = Find(observer, sourceId);
            Assert.That(observerSource, Is.Not.Null, "Surviving observer keeps the released resource replica.");
            Assert.That((bool)BaseResourceType.GetProperty("IsPickedUp").GetValue(observerSource.GetComponent(BaseResourceType)), Is.False);
        }

        ulong bridgeId = bridgeNetworkObject.NetworkObjectId;
        NetworkObject releasedBridge = Find(host, bridgeId);
        Assert.That(releasedBridge.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(IsPickedUp(releasedBridge), Is.False);
        Assert.That(releasedBridge.transform.parent, Is.Null);
        Assert.That(releasedBridge.GetComponent<Rigidbody>().isKinematic, Is.False);
        Assert.That(releasedBridge.GetComponent<Rigidbody>().useGravity, Is.True);
        Assert.That(initialMountableVelocity.sqrMagnitude, Is.LessThan(0.000001f),
            "The mountable has zero linear velocity in the same-frame disconnect callback after recovery.");
        Assert.That(initialMountableAngularVelocity.sqrMagnitude, Is.LessThan(0.000001f),
            "The mountable has zero angular velocity in the same-frame disconnect callback after recovery.");
        Assert.That(Vector3.Distance(releasedBridge.transform.position, bridgeHeldPosition), Is.LessThan(0.02f),
            "The mountable must resume from the server's last held pose without an ordinary-drop nudge.");
        Assert.That(Quaternion.Angle(releasedBridge.transform.rotation, bridgeHeldRotation), Is.LessThan(0.1f));
        NetworkObject observerBridge = Find(observer, bridgeId);
        Assert.That(observerBridge, Is.Not.Null);
        Assert.That(IsPickedUp(observerBridge), Is.False);

        // Verify the authoritative disconnect drop stays at the approved server pose over multiple ticks.
        Vector3 releasedBridgePosition = releasedBridge.transform.position;
        Quaternion releasedBridgeRotation = releasedBridge.transform.rotation;
        for (int sample = 0; sample < 8; sample++)
        {
            yield return null;
            Assert.That(Vector3.Distance(releasedBridge.transform.position, releasedBridgePosition), Is.LessThan(0.02f),
                "The mountable's server pose must not snap back to its former owner's queued transform.");
            observerBridge = Find(observer, bridgeId);
            Assert.That(observerBridge, Is.Not.Null);
            Assert.That(Vector3.Distance(observerBridge.transform.position, releasedBridgePosition), Is.LessThan(0.08f));
            Assert.That(Quaternion.Angle(observerBridge.transform.rotation, releasedBridgeRotation), Is.LessThan(0.5f));
        }

        // The actual shared WoodenLog profile only permits another pickup after the prior owner's
        // single-carry source has been released by disconnect cleanup.
        GameObject sharedGround = GameObject.CreatePrimitive(PrimitiveType.Plane);
        sharedGround.name = "Disconnect fixture shared-carry ground";
        sharedGround.transform.position = new Vector3(24f, 0f, 11f);
        sharedGround.transform.localScale = new Vector3(6f, 1f, 6f);
        runtimeObjects.Add(sharedGround);
        Vector3 sharedOwnerRootPosition = new Vector3(22f, 0.08f, 11f);
        Vector3 hostRootPosition = new Vector3(26f, 0.08f, 11f);
        host.ConnectedClients[sharedOwner.LocalClientId].PlayerObject.transform.position = sharedOwnerRootPosition;
        sharedOwner.LocalClient.PlayerObject.transform.position = sharedOwnerRootPosition;
        host.LocalClient.PlayerObject.transform.position = hostRootPosition;
        GameObject sharedLog = UnityEngine.Object.Instantiate(sharedLogPrefab);
        runtimeObjects.Add(sharedLog);
        sharedLog.transform.position = new Vector3(24f, 1f, 11f);
        RestoreDisconnectFixturePhysics(sharedLog, sharedLogColliderEnabled, sharedLogIsKinematic, sharedLogUseGravity);
        NetworkObject sharedLogNetworkObject = sharedLog.GetComponent<NetworkObject>();
        spawnedObjects.Add(sharedLogNetworkObject);
        sharedLogNetworkObject.Spawn();
        yield return Until(() => Find(sharedOwner, sharedLogNetworkObject.NetworkObjectId) != null
            && Find(observer, sharedLogNetworkObject.NetworkObjectId) != null, "shared Wooden Log replication");
        // The peers share one Physics scene in this loopback fixture. Remove only non-server
        // copies from the safe-placement query before it tests attach clearance.
        DisableDisconnectFixtureMirrorColliders(new[] { owner, observer, sharedOwner, inventoryOwner }, sharedLogNetworkObject.NetworkObjectId);
        Physics.SyncTransforms();
        Assert.That(InvokePickup(sharedLog, sharedOwner.LocalClientId), Is.True,
            "The actual shared Wooden Log profile and its SharedCarryPhysicsBody must accept its first holder.");
        // The legacy ConfirmPickupClientRpc resolves NetworkManager.Singleton in this
        // one-process multi-manager fixture. Let that owner-targeted delivery drain before
        // establishing the owner and host's own local bookkeeping explicitly.
        yield return Until(() => GetHeld(host.LocalClient.PlayerObject) == Find(sharedOwner, sharedLogNetworkObject.NetworkObjectId).gameObject,
            "legacy owner pickup RPC drains through the fixture singleton");
        ConfirmLocalSharedPickup(sharedOwner, sharedLogNetworkObject);
        Assert.That(GetHeld(sharedOwner.LocalClient.PlayerObject), Is.EqualTo(Find(sharedOwner, sharedLogNetworkObject.NetworkObjectId).gameObject));
        Assert.That(InvokePickup(sharedLog, host.LocalClientId), Is.True,
            "A surviving host holder joins the actual shared resource before its first holder disconnects.");
        ConfirmLocalSharedPickup(host, sharedLogNetworkObject);
        Assert.That(GetHeld(host.LocalClient.PlayerObject), Is.EqualTo(sharedLog),
            "Fixture must establish the surviving host's local held-object bookkeeping before disconnect.");
        yield return Until(() => GetHolderCount(sharedLog) == 2,
            "shared log has two authoritative holders");
        DisableDisconnectFixtureMirrorColliders(new[] { owner, observer, sharedOwner, inventoryOwner }, sharedLogNetworkObject.NetworkObjectId);
        ulong sharedOwnerId = sharedOwner.LocalClientId;
        host.DisconnectClient(sharedOwnerId);
        yield return Until(() => host.ConnectedClientsIds.Count == 3, "shared holder disconnect");
        yield return Until(() => Find(host, sharedLogNetworkObject.NetworkObjectId) != null
            && GetHolderCount(Find(host, sharedLogNetworkObject.NetworkObjectId).gameObject) == 1,
            "shared log retains surviving host holder");
        NetworkObject survivingSharedLog = Find(host, sharedLogNetworkObject.NetworkObjectId);
        Assert.That(survivingSharedLog.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(IsPickedUp(survivingSharedLog), Is.True,
            "Disconnecting one actual shared holder must leave the surviving host holder attached.");
        Assert.That(GetHeld(host.LocalClient.PlayerObject), Is.EqualTo(survivingSharedLog.gameObject));
        Assert.That(IsPickedUp(Find(observer, sharedLogNetworkObject.NetworkObjectId)), Is.True);

        // A scene/manual player despawn must expire the same-frame snapshot without treating
        // it as a network disconnect. Keep this observer connected and confirm no Axe is emitted.
        Component observerServerInventory = host.ConnectedClients[observer.LocalClientId].PlayerObject.GetComponent(InventoryType);
        Component observerOwnerInventory = observer.LocalClient.PlayerObject.GetComponent(InventoryType);
        Assert.That((bool)InventoryType.GetMethod("AddItem").Invoke(observerOwnerInventory, new[] { axeAsset }), Is.True);
        yield return Until(() => (int)InventoryType.GetMethod("GetNetworkSlotItemTypeValue").Invoke(observerServerInventory, new object[] { 0 })
            == (int)EquippableItemProfileType.GetField("itemType").GetValue(axeAsset), "manual-despawn inventory replication");
        NetworkObject manuallyDespawnedPlayer = host.ConnectedClients[observer.LocalClientId].PlayerObject;
        manuallyDespawnedPlayer.Despawn(true);
        yield return Until(() => host.ConnectedClientsIds.Count == 3 && CountDroppedTool(host, axeAsset) == 0,
            "manual player despawn does not emit an inventory drop");
        yield return null;
        Assert.That(CountDroppedTool(host, axeAsset), Is.EqualTo(0),
            "A manual player despawn snapshot expires without producing a disconnect recovery drop.");

        // A later owner's two occupied one-slot tools both return; empty/reserved slots do not.
        Component secondServerInventory = host.ConnectedClients[inventoryOwner.LocalClientId].PlayerObject.GetComponent(InventoryType);
        Component secondOwnerInventory = inventoryOwner.LocalClient.PlayerObject.GetComponent(InventoryType);
        Assert.That((bool)InventoryType.GetMethod("AddItem").Invoke(secondOwnerInventory, new[] { axeAsset }), Is.True);
        Assert.That((bool)InventoryType.GetMethod("AddItem").Invoke(secondOwnerInventory, new[] { pickaxeAsset }), Is.True);
        yield return Until(() => (int)InventoryType.GetMethod("GetNetworkSlotItemTypeValue").Invoke(secondServerInventory, new object[] { 0 })
                == (int)EquippableItemProfileType.GetField("itemType").GetValue(axeAsset)
            && (int)InventoryType.GetMethod("GetNetworkSlotItemTypeValue").Invoke(secondServerInventory, new object[] { 1 })
                == (int)EquippableItemProfileType.GetField("itemType").GetValue(pickaxeAsset),
            "both actual one-slot tools replicate to server slots");
        ulong secondOwnerId = inventoryOwner.LocalClientId;
        host.DisconnectClient(secondOwnerId);
        yield return Until(() => host.ConnectedClientsIds.Count == 2, "second actual client disconnect");
        yield return Until(() => CountDroppedTool(host, axeAsset) == 1 && CountDroppedTool(host, pickaxeAsset) == 1,
            "both occupied one-slot tools drop exactly once");
        Assert.That(CountDroppedTool(host, toolAsset), Is.EqualTo(1), "The earlier reserved two-slot tool remains exactly once.");
#endif
    }

    [UnityTest]
    public IEnumerator ControllerCorrelatesAsyncResultsAndCleansUpPendingLifecycle()
    {
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        priorSingleton = NetworkManager.Singleton; capturedSingleton = true;
        GameObject cameraObject = new GameObject("Stage 6 actual-near-plane aim camera");
        runtimeObjects.Add(cameraObject);
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.2f;
        GameObject playerPrefab = CreatePlayerPrefab();
        GameObject resourcePrefab = CreateActualWoodenBoardPrefabForFixture();
        ushort port = ReservePort();
        NetworkManager host = CreateManager("Stage 6 controller host", playerPrefab, new[] { resourcePrefab }, port);
        Assert.That(host.StartHost(), Is.True); host.SetSingleton();
        yield return Until(() => host.LocalClient?.PlayerObject != null, "controller host spawn");
        NetworkManager owner = CreateManager("Stage 6 controller owner", playerPrefab, new[] { resourcePrefab }, port);
        Assert.That(owner.StartClient(), Is.True);
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 2, "controller owner connection");
        NetworkObject hostPlayer = host.LocalClient.PlayerObject;
        NetworkObject ownerPlayer = owner.LocalClient.PlayerObject;
        Component ownerPlacementController = ownerPlayer.GetComponent(PlacementControllerType);
        Assert.That(ownerPlacementController, Is.Not.Null);
        PlacementControllerType.GetField("placementDistance", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(ownerPlacementController, 3.25f);
        Assert.That((float)PlacementControllerType.GetProperty("PlacementDistance").GetValue(ownerPlacementController),
            Is.EqualTo(2f).Within(0.001f),
            "A listening-session controller must use the spawned network preview's configured distance, not its local fallback.");
        DisposeFixtureInputMaps(ownerPlayer);
        ((Behaviour)host.ConnectedClients[owner.LocalClientId].PlayerObject.GetComponent(InteractionType)).enabled = false;
        Component remotePlacementController = host.ConnectedClients[owner.LocalClientId].PlayerObject.GetComponent(PlacementControllerType);
        Assert.That(remotePlacementController, Is.Not.Null, "The fixture starts with its test-only controller on all peers.");
        UnityEngine.Object.Destroy(remotePlacementController);
        yield return null;
        Assert.That(host.ConnectedClients[owner.LocalClientId].PlayerObject.GetComponent(PlacementControllerType), Is.Null,
            "A remote real PlayerNew has no owner-only placement controller on the server.");
        ((Behaviour)ownerPlayer.GetComponent(InteractionType)).enabled = true;
        Transform aimAnchor = GetAimAnchor(ownerPlayer);
        cameraObject.transform.SetPositionAndRotation(aimAnchor.position, aimAnchor.rotation);
        InteractionType.GetMethod("SetAimCamera").Invoke(ownerPlayer.GetComponent(InteractionType), new object[] { camera });

        GameObject source = UnityEngine.Object.Instantiate(resourcePrefab);
        runtimeObjects.Add(source);
        source.transform.position = new Vector3(10f, 4f, 10f);
        NetworkObject sourceNetworkObject = source.GetComponent<NetworkObject>();
        spawnedObjects.Add(sourceNetworkObject);
        sourceNetworkObject.Spawn();
        yield return Until(() => Find(owner, sourceNetworkObject.NetworkObjectId) != null, "controller source replication");
        DisableControllerFixtureMirrorColliders(host, owner, owner.LocalClient.PlayerObject, sourceNetworkObject.NetworkObjectId);
        Component ownerInteraction = ownerPlayer.GetComponent(InteractionType);
        EventHandler mirrorColliderSync = (_, _) => DisableControllerFixtureMirrorColliders(
            host, owner, ownerPlayer, sourceNetworkObject.NetworkObjectId);
        InteractionType.GetEvent("OnHeldObjectChanged").AddEventHandler(ownerInteraction, mirrorColliderSync);
        Assert.That(InvokePickup(source, owner.LocalClientId), Is.True);
        ConfirmLocalPickup(owner, sourceNetworkObject.NetworkObjectId);
        DisableControllerFixtureMirrorColliders(host, owner, ownerPlayer, sourceNetworkObject.NetworkObjectId);
        yield return Until(() => sourceNetworkObject.OwnerClientId == owner.LocalClientId, "controller source ownership");
        Component serverSourcePlaceable = source.GetComponent(BaseResourceType);
        object serverHolder = host.ConnectedClients[owner.LocalClientId].PlayerObject.GetComponent(InteractionType);
        Assert.That((bool)BaseResourceType.GetMethod("CanCompleteNetworkSingleCarryPlacement").Invoke(
            serverSourcePlaceable, new[] { (object)owner.LocalClientId, serverHolder }), Is.True,
            "The fixture must establish server-side holder eligibility before testing controller requests.");

        syntheticKeyboard = InputSystem.AddDevice<Keyboard>();
        Component ownerInput = ownerPlayer.GetComponent(PlayerInputType);
        ((Behaviour)ownerInput).enabled = true;
        Component controller = ownerPlayer.GetComponent(PlacementControllerType);
        Component preview = Preview(ownerPlayer);
        SetControllerRequestId(controller, 0);

        // Keep the camera ray at the actual 0.2m near plane; offsetting the camera target remains forged.
        PressKey(Key.F);
        yield return Until(() => (bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), "controller placement entry");
        cameraObject.transform.position += Vector3.right;
        yield return null;
        PressKey(Key.E);
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True,
            "The request must enter pending state before the asynchronous server response.");
        Transform ghost = GetControllerGhost(controller).transform;
        Vector3 frozenPosition = ghost.position;
        Quaternion frozenRotation = ghost.rotation;
        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState(Key.UpArrow));
        InputSystem.Update();
        cameraObject.transform.position += Vector3.right;
        PlacementControllerType.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
        PressKey(Key.F);
        PressKey(Key.Escape);
        PressKey(Key.E);
        Assert.That((bool)BaseResourceType.GetMethod("CanCompleteNetworkSingleCarryPlacement").Invoke(
            serverSourcePlaceable, new[] { (object)owner.LocalClientId, serverHolder }), Is.True,
            "Server eligibility changed before the first placement request: "
            + DescribeServerEligibility(serverSourcePlaceable, serverHolder));
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True);
        Assert.That((bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), Is.True);
        Assert.That(Vector3.Distance(ghost.position, frozenPosition), Is.LessThan(0.0001f));
        Assert.That(Quaternion.Angle(ghost.rotation, frozenRotation), Is.LessThan(0.001f));
        Assert.That(GetPendingRequestId(controller), Is.EqualTo(1));
        yield return Until(() => LastRequest(preview) == 1, "controller refusal response");
        AssertResult(ownerPlayer, "InvalidAimOrigin");
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False);
        Assert.That((string)PlacementControllerType.GetProperty("PlacementStatusLabel").GetValue(controller),
            Is.EqualTo("Aim out of sync; try again"));
        AssertHeld(owner, sourceNetworkObject);

        // Escape exits after refusal. A later forced disable must also exit while a second request is pending;
        // its eventual response must not revive the placement session.
        PressKey(Key.Escape);
        Assert.That((bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        cameraObject.transform.position = aimAnchor.position + Vector3.right;
        PressKey(Key.F);
        yield return null;
        PressKey(Key.E);
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True);
        ulong forcedRequestId = GetPendingRequestId(controller);
        ((Behaviour)controller).enabled = false;
        Assert.That((bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False);
        yield return Until(() => LastRequest(preview) == forcedRequestId, "late response after forced cleanup");
        AssertResult(ownerPlayer, "InvalidAimOrigin");
        AssertHeld(owner, sourceNetworkObject);
        ((Behaviour)controller).enabled = true;

        // Re-entry uses a higher ID. Move the camera after the last rendered sample; confirmation must use
        // that cached ray/pose pair, not the changed camera state. The host handles this request synchronously.
        cameraObject.transform.position = aimAnchor.position;
        PressKey(Key.F);
        yield return Until(() => (bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), "controller re-entry");
        yield return null;
        Transform acceptedGhost = GetControllerGhost(controller).transform;
        Vector3 expectedPosition = acceptedGhost.position;
        Quaternion expectedRotation = acceptedGhost.rotation;
        cameraObject.transform.position += Vector3.right;
        PressKey(Key.E);
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True);
        ulong acceptedRequestId = GetPendingRequestId(controller);
        Assert.That(acceptedRequestId, Is.GreaterThan(forcedRequestId));
        PressKey(Key.F);
        PressKey(Key.Escape);
        Assert.That(Vector3.Distance(acceptedGhost.position, expectedPosition), Is.LessThan(0.0001f));
        yield return Until(() => LastRequest(preview) == acceptedRequestId
            && ResultName(ownerPlayer) == "Accepted", "cached rendered ray acceptance");
        Assert.That((bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        Assert.That(sourceNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(Vector3.Distance(source.transform.position, expectedPosition), Is.LessThan(0.003f),
            $"Accepted network placement changed cached rendered pose {expectedPosition} to {source.transform.position}.");
        Assert.That(Quaternion.Angle(source.transform.rotation, expectedRotation), Is.LessThan(0.1f));

        // A local host result arrives synchronously inside RequestNetworkPlacement and exits before input returns.
        DisposeFixtureInputMaps(hostPlayer);
        Assert.That(InvokePickup(source, host.LocalClientId), Is.True);
        ConfirmLocalPickup(host, sourceNetworkObject.NetworkObjectId);
        yield return Until(() => GetHeld(hostPlayer) == source, "host local pickup confirmation");
        hostPlayer.transform.position = new Vector3(80f, 0f, 0f);
        Physics.SyncTransforms();
        Component hostInput = hostPlayer.GetComponent(PlayerInputType);
        ((Behaviour)hostInput).enabled = false;
        ((Behaviour)hostInput).enabled = true;
        Component hostController = hostPlayer.GetComponent(PlacementControllerType);
        Component hostPreview = Preview(hostPlayer);
        SetControllerRequestId(hostController, LastRequest(hostPreview));
        Transform hostAimAnchor = GetAimAnchor(hostPlayer);
        cameraObject.transform.SetPositionAndRotation(hostAimAnchor.position, hostAimAnchor.rotation);
        PressKey(Key.F);
        yield return Until(() => (bool)PlacementControllerType.GetProperty("IsActive").GetValue(hostController), "host controller entry");
        yield return null;
        Transform hostGhost = GetControllerGhost(hostController).transform;
        Vector3 hostExpectedPosition = hostGhost.position;
        Quaternion hostExpectedRotation = hostGhost.rotation;
        PressKey(Key.E);
        Assert.That(ResultName(hostPlayer), Is.EqualTo("Accepted"));
        Assert.That(GetLastRequestId(hostController), Is.GreaterThan(0));
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(hostController), Is.False);
        Assert.That((bool)PlacementControllerType.GetProperty("IsActive").GetValue(hostController), Is.False);
        Assert.That(sourceNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(Vector3.Distance(source.transform.position, hostExpectedPosition), Is.LessThan(0.003f));
        Assert.That(Quaternion.Angle(source.transform.rotation, hostExpectedRotation), Is.LessThan(0.1f));

        // If the held NetworkObject loses ownership while a remote request is pending, the controller
        // exits immediately even if the targeted result arrives later.
        DisposeFixtureInputMaps(ownerPlayer);
        ((Behaviour)ownerInput).enabled = false;
        ((Behaviour)ownerInput).enabled = true;
        Assert.That(InvokePickup(source, owner.LocalClientId), Is.True);
        ConfirmLocalPickup(owner, sourceNetworkObject.NetworkObjectId);
        yield return Until(() => sourceNetworkObject.OwnerClientId == owner.LocalClientId, "source re-pickup for lifecycle check");
        // Forced lifecycle paths clear pending state even when the server result is still in flight.
        foreach (string route in new[] { "menu", "scene", "channel" })
        {
            yield return AssertForcedExitWhilePending(route, owner, ownerPlayer, controller, preview,
                sourceNetworkObject, cameraObject.transform, aimAnchor);
        }
        cameraObject.transform.position = aimAnchor.position;
        PressKey(Key.F);
        yield return null;
        PressKey(Key.E);
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True);
        ulong ownershipLossRequestId = GetPendingRequestId(controller);
        sourceNetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        yield return Until(() => Find(owner, sourceNetworkObject.NetworkObjectId).OwnerClientId == NetworkManager.ServerClientId,
            "source ownership-loss replication");
        yield return Until(() => !(bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller),
            "forced cleanup after held source ownership loss");
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False);
        yield return Until(() => LastRequest(preview) == ownershipLossRequestId, "late result after source ownership loss");
        Assert.That((bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), Is.False);

        // A spawned held source disappearing while a new request is in flight also unlocks local placement.
        sourceNetworkObject.ChangeOwnership(owner.LocalClientId);
        yield return Until(() => Find(owner, sourceNetworkObject.NetworkObjectId).OwnerClientId == owner.LocalClientId,
            "source ownership restored for despawn check");
        cameraObject.transform.position = aimAnchor.position;
        PressKey(Key.F);
        yield return null;
        PressKey(Key.E);
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True);
        ulong despawnRequestId = GetPendingRequestId(controller);
        sourceNetworkObject.Despawn(true);
        yield return Until(() => !(bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller),
            "forced cleanup after held source despawn");
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False);
        yield return Until(() => LastRequest(preview) == despawnRequestId, "late response after source despawn");
        Assert.That((bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), Is.False);

        // A real server downing transition notifies the owner controller and intentionally releases held items.
        GameObject downedSource = UnityEngine.Object.Instantiate(resourcePrefab);
        runtimeObjects.Add(downedSource);
        downedSource.transform.position = new Vector3(10f, 4f, 10f);
        NetworkObject downedSourceNetworkObject = downedSource.GetComponent<NetworkObject>();
        spawnedObjects.Add(downedSourceNetworkObject);
        downedSourceNetworkObject.Spawn();
        yield return Until(() => Find(owner, downedSourceNetworkObject.NetworkObjectId) != null, "downed lifecycle source replication");
        Assert.That(InvokePickup(downedSource, owner.LocalClientId), Is.True);
        ConfirmLocalPickup(owner, downedSourceNetworkObject.NetworkObjectId);
        yield return Until(() => downedSourceNetworkObject.OwnerClientId == owner.LocalClientId, "downed lifecycle source ownership");
        cameraObject.transform.position = aimAnchor.position + Vector3.right;
        PressKey(Key.F);
        yield return null;
        PressKey(Key.E);
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True);
        ulong downedRequestId = GetPendingRequestId(controller);
        host.SetSingleton();
        NetworkObject serverOwnerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        Component serverOwnerHealth = serverOwnerPlayer.GetComponent(HealthType);
        HealthType.GetMethod("DamageReceived", new[] { typeof(float) })
            .Invoke(serverOwnerHealth, new object[] { 1000f });
        yield return Until(() => (bool)HealthType.GetProperty("IsDowned").GetValue(serverOwnerHealth),
            "server enters downed state");
        yield return Until(() => (bool)HealthType.GetProperty("IsDowned").GetValue(ownerPlayer.GetComponent(HealthType)),
            "owner observes the server downed state");
        yield return Until(() => !(bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller),
            "forced cleanup after actual downed state transition");
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False);
        yield return Until(() => LastRequest(preview) == downedRequestId, "late response after downed cleanup");
        Assert.That((bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        InteractionType.GetEvent("OnHeldObjectChanged").RemoveEventHandler(ownerInteraction, mirrorColliderSync);
    }

    private GameObject CreatePlayerPrefab()
    {
        GameObject prefab = new GameObject("Stage 6 placement player prefab"); assets.Add(prefab);
        SetPrefabHash(prefab.AddComponent<NetworkObject>(), 0x61A60001);
        Component input = prefab.AddComponent(PlayerInputType);
        ((Behaviour)input).enabled = false;
        prefab.AddComponent(InteractionType);
        prefab.AddComponent(HealthType);
        prefab.AddComponent(PlacementControllerType);
        prefab.AddComponent(PreviewType);
        Type playerTransformType = Type.GetType("ClientNetworkTransform, Assembly-CSharp");
        if (playerTransformType != null) prefab.AddComponent(playerTransformType);
        Transform anchor = new GameObject("PlacementAimAnchor").transform;
        anchor.SetParent(prefab.transform, false);
        anchor.localPosition = new Vector3(0f, 1.1f, 0f);
        InteractionType.GetField("interactionOrigin", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(prefab.GetComponent(InteractionType), anchor);
        return prefab;
    }

    private GameObject CreateSourcePrefab(string label, Type behaviourType, UnityEngine.Object profile, uint hash)
    {
        GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Cube); prefab.name = label; assets.Add(prefab);
        prefab.transform.localScale = Vector3.one;
        Rigidbody body = prefab.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        SetPrefabHash(prefab.AddComponent<NetworkObject>(), hash);
        Type transformType = Type.GetType("ClientNetworkTransform, Assembly-CSharp");
        if (transformType != null) prefab.AddComponent(transformType);
        Component behaviour = prefab.AddComponent(behaviourType);
        string profileFieldName = behaviourType == BaseResourceType ? "baseResourceSO" : "mountableBridgeComponentSO";
        behaviourType.GetField(profileFieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(behaviour, profile);
        prefab.transform.position = Vector3.one * 10000f;
        return prefab;
    }

    private GameObject CreateActualWoodenBoardPrefabForFixture()
    {
        GameObject boardAsset = null;
#if UNITY_EDITOR
        boardAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/WoodenBoardResource_prefab.prefab");
#else
        Assert.Ignore("Stage 6 actual Wooden Board fixture requires the Unity Editor AssetDatabase.");
#endif
        Assert.That(boardAsset, Is.Not.Null, "The Stage6 runtime repro requires the project's actual Wooden Board prefab.");
        GameObject prefab = UnityEngine.Object.Instantiate(boardAsset);
        prefab.name = "Stage 6 actual Wooden Board fixture prefab";
        prefab.transform.position = Vector3.one * 10000f;
        assets.Add(prefab);
        SetPrefabHash(prefab.GetComponent<NetworkObject>(), 0x61A60031);
        return prefab;
    }

    private NetworkManager CreateManager(string name, GameObject playerPrefab, GameObject[] sourcePrefabs, ushort port)
    {
        GameObject root = new GameObject(name); roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>(); transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = playerPrefab, EnableSceneManagement = false };
        foreach (GameObject source in sourcePrefabs) manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = source });
        managers.Add(manager);
        return manager;
    }

    private static bool InvokePickup(GameObject source, ulong holderId)
    {
        Type type = source.GetComponent(BaseResourceType) != null ? BaseResourceType : BridgeType;
        object result = type.GetMethod("TryCompleteNetworkPickup", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(source.GetComponent(type), new object[] { holderId, Vector3.zero, 0.3f });
        return (bool)result;
    }

    private static void ConfirmLocalPickup(NetworkManager owner, ulong sourceId)
    {
        NetworkObject source = Find(owner, sourceId);
        Component behaviour = source.GetComponent(BaseResourceType) ?? source.GetComponent(BridgeType);
        MethodInfo confirm = InteractionType.GetMethod("ConfirmPickedUpObject", new[]
        {
            typeof(GameObject), PickableType, typeof(bool), typeof(float), typeof(bool), typeof(Vector3)
        });
        confirm.Invoke(owner.LocalClient.PlayerObject.GetComponent(InteractionType),
            new[] { (object)source.gameObject, behaviour, true, 0f, false, Vector3.zero });
    }

    private static void ConfirmLocalSharedPickup(NetworkManager owner, NetworkObject serverSource)
    {
        NetworkObject ownerSource = Find(owner, serverSource.NetworkObjectId);
        Component resource = ownerSource.GetComponent(BaseResourceType);
        object attachPoints = BaseResourceType.GetField("holderAttachLocalPoints", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(serverSource.GetComponent(BaseResourceType));
        Vector3 attachPoint = (Vector3)attachPoints.GetType().GetProperty("Item").GetValue(attachPoints,
            new object[] { owner.LocalClientId });
        float movementPenalty = (float)BaseResourceType.GetMethod("CalculateCarryMovementSpeedPenalty", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(serverSource.GetComponent(BaseResourceType), null);
        MethodInfo confirm = InteractionType.GetMethod("ConfirmPickedUpObject", new[]
        {
            typeof(GameObject), PickableType, typeof(bool), typeof(float), typeof(bool), typeof(Vector3)
        });
        confirm.Invoke(owner.LocalClient.PlayerObject.GetComponent(InteractionType),
            new object[] { ownerSource.gameObject, resource, false, movementPenalty, true, attachPoint });
    }

    private static void Send(NetworkManager owner, Component preview, NetworkObject source, ulong requestId,
        Vector3 position, Quaternion rotation, Ray ray)
    {
        NetworkObject ownerSource = Find(owner, source.NetworkObjectId);
        PreviewType.GetMethod("RequestNetworkPlacement").Invoke(preview,
            new object[] { ownerSource, requestId, position, rotation, ray });
    }

    private static Component Preview(NetworkObject player) => player.GetComponent(PreviewType);
    private static GameObject GetControllerGhost(Component controller)
    {
        object ghost = PlacementControllerType.GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        return (GameObject)ghost.GetType().GetProperty("Root").GetValue(ghost);
    }
    private static void SetControllerRequestId(Component controller, ulong value) =>
        PlacementControllerType.GetField("lastRequestId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, value);
    private static ulong GetLastRequestId(Component controller) =>
        (ulong)PlacementControllerType.GetField("lastRequestId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
    private static ulong GetPendingRequestId(Component controller) =>
        (ulong)PlacementControllerType.GetField("pendingRequestId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
    private void PressKey(Key key)
    {
        Assert.That(syntheticKeyboard, Is.Not.Null);
        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState());
        InputSystem.Update();
        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState(key));
        InputSystem.Update();
        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState());
        InputSystem.Update();
    }
    private static NetworkObject Find(NetworkManager manager, ulong id) => manager != null && manager.SpawnManager != null
        && manager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject obj) ? obj : null;
    private static int CountDroppedTool(NetworkManager manager, UnityEngine.Object profile)
    {
        int count = 0;
        foreach (NetworkObject spawned in manager.SpawnManager.SpawnedObjectsList)
        {
            if (spawned == null || !spawned.gameObject.TryGetComponent(EquippableItemType, out Component itemComponent)) continue;
            object itemProfile = EquippableItemType.GetMethod("GetEquippableItemSO").Invoke(itemComponent, null);
            if (itemProfile == profile) count++;
        }

        return count;
    }
    private static int GetHolderCount(GameObject source)
    {
        object component = source.GetComponent(BaseResourceType);
        object holders = BaseResourceType.GetField("holderClientIds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
        return ((System.Collections.ICollection)holders).Count;
    }
    private static bool CaptureInitialReleaseVelocity(NetworkObject source, ref Vector3 velocity)
    {
        if (source == null || IsPickedUp(source)) return false;
        Rigidbody body = source.GetComponent<Rigidbody>();
        if (body == null) return false;
        velocity = body.linearVelocity;
        return true;
    }
    private static void DisableDisconnectFixtureMirrorColliders(NetworkManager[] peers, ulong sourceId)
    {
        foreach (NetworkManager peer in peers)
        {
            NetworkObject mirror = Find(peer, sourceId);
            if (mirror == null) continue;
            foreach (Collider collider in mirror.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }
    }

    private static void RestoreDisconnectFixturePhysics(GameObject source, bool[] colliderEnabled,
        bool isKinematic, bool useGravity)
    {
        Collider[] colliders = source.GetComponentsInChildren<Collider>(true);
        Assert.That(colliders.Length, Is.EqualTo(colliderEnabled.Length),
            "The runtime Wooden Log clone must preserve the imported prefab collider hierarchy.");
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = colliderEnabled[i];
        Rigidbody body = source.GetComponent<Rigidbody>();
        Assert.That(body, Is.Not.Null, "The runtime Wooden Log clone must keep its imported Rigidbody.");
        body.isKinematic = isKinematic;
        body.useGravity = useGravity;
    }

    private static void DisableDisconnectFixtureRemotePlayerPhysics(NetworkManager server, NetworkManager[] clientManagers)
    {
        foreach (NetworkManager clientManager in clientManagers)
        {
            if (server.ConnectedClients.TryGetValue(clientManager.LocalClientId, out NetworkClient serverClient)
                && serverClient.PlayerObject != null
                && serverClient.PlayerObject.TryGetComponent(out CharacterController serverRemoteController))
            {
                // Matches PlayerNetworkSetup's remote-player collision role while preserving
                // the component dimensions that the shared-carry placement solver reads.
                serverRemoteController.enabled = false;
            }

            if (clientManager.SpawnManager == null) continue;
            foreach (NetworkObject playerCopy in clientManager.SpawnManager.SpawnedObjects.Values)
            {
                if (playerCopy == null || !playerCopy.IsPlayerObject) continue;
                foreach (Collider collider in playerCopy.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
            }
        }
    }
    private static Transform GetAimAnchor(NetworkObject player) => (Transform)InteractionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(player.GetComponent(InteractionType));
    private static GameObject GetHeld(NetworkObject player) => (GameObject)InteractionType.GetMethod("GetPickedUpGameObject").Invoke(player.GetComponent(InteractionType), null);
    private static Rigidbody GetBody(GameObject source) => source.GetComponent<Rigidbody>();
    private static bool IsPickedUp(NetworkObject source) => (bool)source.GetComponent(
        source.GetComponent(BaseResourceType) != null ? BaseResourceType : BridgeType)
        .GetType().GetProperty("IsPickedUp").GetValue(source.GetComponent(
            source.GetComponent(BaseResourceType) != null ? BaseResourceType : BridgeType));
    private static bool PreviewActive(Component preview)
    {
        object networkVariable = PreviewType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
        object value = networkVariable.GetType().GetProperty("Value").GetValue(networkVariable);
        return (bool)value.GetType().GetField("Active", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
    }
    private static ulong LastRequest(Component preview) => (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
    private static string ResultName(NetworkObject player) => PreviewType.GetProperty("LastPlacementResult").GetValue(Preview(player)).ToString();
    private static void AssertResult(NetworkObject player, string expected) => Assert.That(ResultName(player), Is.EqualTo(expected));

    private static void AssertHeld(NetworkManager owner, NetworkObject source)
    {
        Assert.That(source.OwnerClientId, Is.EqualTo(owner.LocalClientId), "A refused placement keeps source ownership.");
        Assert.That(GetHeld(owner.LocalClient.PlayerObject), Is.EqualTo(Find(owner, source.NetworkObjectId).gameObject),
            "A refused placement keeps local holder state.");
    }

    private static string DescribeServerEligibility(Component placeable, object holder)
    {
        object GetField(string name) => placeable.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(placeable);
        Rigidbody body = (Rigidbody)GetField("_rigidbody");
        object holderIds = GetField("holderClientIds");
        NetworkObject source = placeable.GetComponent<NetworkObject>();
        NetworkObject player = ((Component)holder).GetComponent<NetworkObject>();
        return $"pickedUp={GetField("isPickedUp")}; holders={holderIds}; sourceOwner={source.OwnerClientId}; "
            + $"playerOwner={player.OwnerClientId}; sourceSpawned={source.IsSpawned}; playerSpawned={player.IsSpawned}; "
            + $"bodyKinematic={body.isKinematic}; bodyGravity={body.useGravity}; "
            + $"downed={HealthType.GetProperty("IsDowned").GetValue(player.GetComponent(HealthType))}; "
            + $"canEnter={placeable.GetType().GetProperty("CanEnterSingleCarryPlacement").GetValue(placeable)}; "
            + $"controller={player.GetComponent(PlacementControllerType) != null}";
    }

    private IEnumerator AssertForcedExitWhilePending(string route, NetworkManager owner, NetworkObject player,
        Component controller, Component preview, NetworkObject source, Transform camera, Transform aimAnchor)
    {
        camera.position = aimAnchor.position + Vector3.right;
        PressKey(Key.F);
        yield return null;
        PressKey(Key.E);
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True,
            $"The {route} lifecycle case must begin with a pending server request.");
        ulong requestId = GetPendingRequestId(controller);
        switch (route)
        {
            case "menu":
                PlayerInputType.GetMethod("ToggleRestartMenu_performed", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(player.GetComponent(PlayerInputType), new object[] { default(InputAction.CallbackContext) });
                break;
            case "scene":
                PlacementControllerType.GetMethod("ActiveSceneChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, new object[] { default(UnityEngine.SceneManagement.Scene), default(UnityEngine.SceneManagement.Scene) });
                break;
            case "channel":
                PreviewType.GetMethod("OnLostOwnership").Invoke(preview, null);
                break;
            default:
                Assert.Fail("Unknown forced cleanup test route: " + route);
                break;
        }
        Assert.That((bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), Is.False,
            $"The {route} lifecycle event must exit placement while pending.");
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False,
            $"The {route} lifecycle event must clear the pending request correlation.");
        yield return Until(() => LastRequest(preview) == requestId, $"late response after {route} forced cleanup");
        Assert.That((bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller), Is.False,
            $"The late {route} response must not revive placement.");
        AssertHeld(owner, source);
    }

    private static void DisableMirrorColliders(NetworkManager manager, ulong id, NetworkManager server)
    {
        if (manager == server) return;
        NetworkObject mirror = Find(manager, id);
        if (mirror == null) return;
        foreach (Collider collider in mirror.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    private static void DisableControllerFixtureMirrorColliders(NetworkManager server, NetworkManager owner,
        NetworkObject ownerPlayer, ulong sourceId)
    {
        GameObject held = GetHeld(ownerPlayer);
        foreach (NetworkManager manager in new[] { owner, server })
        {
            if (manager == server) continue;
            NetworkObject mirror = Find(manager, sourceId);
            if (mirror == null) continue;
            bool enableForLocalGeometry = manager == owner && held == mirror.gameObject;
            foreach (Collider collider in mirror.GetComponentsInChildren<Collider>(true))
                collider.enabled = enableForLocalGeometry;
        }
    }

    private void DisposeFixtureInputMaps(NetworkObject controlledPlayer)
    {
        var seen = new HashSet<int>();
        foreach (NetworkManager manager in managers)
        {
            if (manager == null || manager.SpawnManager == null) continue;
            foreach (NetworkObject player in manager.SpawnManager.SpawnedObjects.Values)
            {
                if (player == null || player == controlledPlayer || !seen.Add(player.GetInstanceID())) continue;
                Component input = player.GetComponent(PlayerInputType);
                if (input != null) PlayerInputType.GetMethod("DisposeInput", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(input, null);
            }
        }
    }

    private static void SetPrefabHash(NetworkObject networkObject, uint value) =>
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(networkObject, value);
    private static uint GetPrefabHash(NetworkObject networkObject) =>
        (uint)typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(networkObject);
    private static string DescribeSpawnedObjects(NetworkManager manager)
    {
        List<string> matches = new List<string>();
        foreach (NetworkObject obj in manager.SpawnManager.SpawnedObjects.Values)
            matches.Add($"id={obj.NetworkObjectId},hash={GetPrefabHash(obj):X8},name={obj.name},scene={obj.IsSceneObject},player={obj.IsPlayerObject},owner={obj.OwnerClientId}");
        return string.Join(" | ", matches);
    }
    private List<GameObject> CaptureFixtureNetworkCopies()
    {
        var copies = new List<GameObject>();
        var seen = new HashSet<int>();
        foreach (NetworkObject candidate in Resources.FindObjectsOfTypeAll<NetworkObject>())
        {
            uint hash = candidate != null ? GetPrefabHash(candidate) : 0;
            if (candidate == null || assets.Contains(candidate.gameObject)
                || hash != 0x61A60001u && hash != 0x61A60011u && hash != 0x61A60012u
                    && hash != 0x61A60021u && hash != 0x61A60031u && !additionalFixturePrefabHashes.Contains(hash)
                || !candidate.gameObject.scene.IsValid() || !candidate.gameObject.scene.isLoaded
                || !candidate.name.EndsWith("(Clone)", StringComparison.Ordinal))
                continue;
            NetworkManager candidateManager = candidate.NetworkManager;
            if (!managers.Contains(candidateManager) || !seen.Add(candidate.gameObject.GetInstanceID())) continue;
            copies.Add(candidate.gameObject);
        }
        return copies;
    }

    private sealed class ScopedInputTestFixture : InputTestFixture
    {
        public void Begin() => Setup();
        public void End() => TearDown();
    }

    private static ushort ReservePort()
    {
        // Stage4/5 use Windows' ephemeral UDP range. Keep this fixture in a distinct low-port namespace
        // so a just-closed Stage5 host cannot be accidentally reused during an all-stage test run.
        for (ushort port = 46006; port < 46106; port++)
        {
            try
            {
                using UdpClient listener = new UdpClient(port);
                return port;
            }
            catch (SocketException)
            {
                // Try the next dedicated fixture port.
            }
        }
        throw new InvalidOperationException("No free Stage6 fixture port in the dedicated 46006–46105 range.");
    }
    private static IEnumerator Until(Func<bool> condition, string label)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out waiting for " + label + ".");
    }
}
