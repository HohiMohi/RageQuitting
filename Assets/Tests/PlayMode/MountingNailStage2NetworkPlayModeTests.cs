using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public class MountingNailStage2NetworkPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const uint PlayerFixtureHash = 0x4E415432u;
    private const uint TargetFixtureHash = 0x4E415433u;
    private const uint SceneStateFixtureHash = 0x4E415434u;
    private const string PlayerFixturePrefix = "Mounting Nail Stage 2 minimal player fixture";
    private readonly List<NetworkManager> managers = new();
    private readonly List<GameObject> roots = new();
    private readonly List<GameObject> playerCopies = new();
    private readonly List<GameObject> targetCopies = new();
    private readonly List<NetworkObject> spawned = new();
    private NetworkManager previousSingleton;
    private bool capturedSingleton;
    private Vector3 savedPhysicsGravity;
    private bool restorePhysicsGravity;
    private GameObject playerPrefab;
    private GameObject targetPrefab;
    private GameObject mountableTargetPrefab;
    private GameObject nailPrefab;
    private GameObject sceneStatePrefab;
    private ScriptableObject profile;
    private ScriptableObject hammer;
    private ScriptableObject shovel;
    private Type nailType;
    private Type inventoryType;
    private Type actionType;
    private Type interactionType;
    private Type resourceType;
    private Type mountableType;
    private ScriptableObject boardResourceProfile;
    private ScriptableObject mountableProfile;
    private Component serverNail;
    private NetworkObject targetObject;
    private NetworkObject secondTargetObject;
    private Component hostSceneState;
    private NetworkManager host;
    private NetworkManager owner;
    private NetworkManager lateJoin;
    private bool fixturePair;

    [UnityTest]
    public IEnumerator OwnerRpcValidatesAndReplicatesPartialThenEmbeddedToLateJoiners()
    {
        yield return RunOwnerRpcScenario(false);
    }

    [UnityTest]
    public IEnumerator Stage3OwnerRpcCompletesOriginalPairAndReplicatesLocksToLateJoiners()
    {
        yield return RunOwnerRpcScenario(true);
    }

    private IEnumerator RunOwnerRpcScenario(bool withPair)
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Debug.Log("[MountingNailStage2Network] begin");
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        RemoveStaleMountableTargetFixtureClones();
        fixturePair = withPair;
        previousSingleton = NetworkManager.Singleton;
        capturedSingleton = true;
        LoadAssetsAndCreatePrefabs();
        ushort port = ReservePort();
        host = CreateManager("Mounting nail stage 2 host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        GameObject stateInstance = UnityEngine.Object.Instantiate(sceneStatePrefab);
        roots.Add(stateInstance);
        NetworkObject stateNetworkObject = stateInstance.GetComponent<NetworkObject>();
        stateNetworkObject.Spawn();
        spawned.Add(stateNetworkObject);
        hostSceneState = stateInstance.GetComponent(Type.GetType("MountingNailSceneState, Assembly-CSharp"));
        Assert.That(hostSceneState, Is.Not.Null);
        Assert.That(stateNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        yield return Until(() => host.LocalClient?.PlayerObject != null, "host player");
        CapturePlayerCopies();

        owner = CreateManager("Mounting nail stage 2 owner", port);
        Assert.That(owner.StartClient(), Is.True);
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 2, "remote owner");
        CapturePlayerCopies();
        DisablePlayerCopies();
        Vector3 fixturePlayerPosition = new(100f, 25f, withPair ? 98.3f : 98f);
        host.LocalClient.PlayerObject.transform.SetPositionAndRotation(fixturePlayerPosition, Quaternion.identity);
        owner.LocalClient.PlayerObject.transform.SetPositionAndRotation(fixturePlayerPosition, Quaternion.identity);
        yield return Until(() => Vector3.Distance(host.ConnectedClients[owner.LocalClientId].PlayerObject.transform.position,
                fixturePlayerPosition) < 0.05f
            && Vector3.Distance(owner.LocalClient.PlayerObject.transform.position, fixturePlayerPosition) < 0.05f,
            "remote player client and server fixture pose");

        GameObject targetInstance = UnityEngine.Object.Instantiate(targetPrefab, new Vector3(100f, 25f, 100f), Quaternion.identity);
        roots.Add(targetInstance);
        targetObject = targetInstance.GetComponent<NetworkObject>();
        targetObject.Spawn();
        FreezeTargetFixture(targetObject);
        Assert.That(targetObject, Is.Not.Null);
        spawned.Add(targetObject);
        yield return Until(() => Find(owner, targetObject.NetworkObjectId) != null, "target mirror");
        DisableTargetMirrorColliders(owner, targetObject.NetworkObjectId);

        if (fixturePair)
        {
            GameObject secondTargetInstance = UnityEngine.Object.Instantiate(mountableTargetPrefab,
                new Vector3(100f, 25f, 100.22f), Quaternion.identity);
            roots.Add(secondTargetInstance);
            secondTargetObject = secondTargetInstance.GetComponent<NetworkObject>();
            secondTargetObject.Spawn();
            FreezeTargetFixture(secondTargetObject);
            spawned.Add(secondTargetObject);
            yield return Until(() => Find(owner, secondTargetObject.NetworkObjectId) != null, "second pair target mirror");
            DisableTargetMirrorColliders(owner, secondTargetObject.NetworkObjectId);
        }

        GameObject nailObject = UnityEngine.Object.Instantiate(nailPrefab);
        roots.Add(nailObject);
        serverNail = nailObject.GetComponent(nailType);
        profile = UnityEngine.Object.Instantiate((ScriptableObject)nailType.GetProperty("Profile", PublicInstance).GetValue(serverNail));
        profile.GetType().GetField("requiredHitsForSingleObject", PrivateInstance).SetValue(profile, 3);
        profile.GetType().GetField("requiredHitsForTwoObjects", PrivateInstance).SetValue(profile, 3);
        nailType.GetField("profile", PrivateInstance).SetValue(serverNail, profile);
        nailType.GetMethod("ApplyProfileConfiguration", PrivateInstance).Invoke(serverNail, null);
        PlaceTipOnTarget(nailObject, targetObject.gameObject);
        Physics.SyncTransforms();
        NetworkObject nailNetworkObject = nailObject.GetComponent<NetworkObject>();
        nailNetworkObject.Spawn();
        spawned.Add(nailNetworkObject);
        MethodInfo canPrepareAtPose = nailType.GetMethod("CanPrepareAtPose", PublicInstance);
        Transform nailTip = (Transform)nailType.GetProperty("Tip", PublicInstance).GetValue(serverNail);
        Assert.That((bool)canPrepareAtPose.Invoke(serverNail,
            new object[] { nailObject.transform.position, nailObject.transform.rotation }), Is.True,
            $"Fixture must place the real nail tip into the target face. tip={nailTip.position}, forward={nailTip.forward}, " +
            $"targetBounds={targetObject.GetComponent<Collider>().bounds}");
        object[] surfaceArgs = { null };
        MethodInfo findPreparedSurface = nailType.GetMethod("TryFindPreparedSurface", PrivateInstance, null,
            new[] { typeof(Collider).MakeByRefType() }, null);
        Assert.That((bool)findPreparedSurface.Invoke(serverNail, surfaceArgs), Is.True,
            $"Fixture must provide a stable first collider in front of the nail tip. tip={nailTip.position}, forward={nailTip.forward}");
        // Transition the server copy out of dynamic Loose physics before the network
        // spawn yield can place its kinematic mirror at the same location.
        nailType.GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(serverNail, new object[] { null });
        yield return Until(() => State(serverNail) == "Prepared", "prepared server nail");
        yield return Until(() => Find(owner, nailNetworkObject.NetworkObjectId) != null, "nail mirror");
        NetworkObject initialOwnerNail = Find(owner, nailNetworkObject.NetworkObjectId);
        Assert.That(nailNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(initialOwnerNail.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        yield return Until(() => State(initialOwnerNail.GetComponent(nailType)) == "Prepared"
            && Vector3.Distance(initialOwnerNail.transform.position, nailObject.transform.position) < 0.005f
            && Quaternion.Angle(initialOwnerNail.transform.rotation, nailObject.transform.rotation) < 0.1f,
            "prepared remote state and synchronized transform");
        Assert.That(initialOwnerNail.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(nailNetworkObject.GetComponent<Rigidbody>().isKinematic, Is.True);

        NetworkObject ownerPlayer = owner.LocalClient.PlayerObject;
        Component ownerAction = ownerPlayer.GetComponent(actionType);
        Component ownerInventory = ownerPlayer.GetComponent(inventoryType);
        Component serverPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject.GetComponent(actionType);
        Component serverInventory = host.ConnectedClients[owner.LocalClientId].PlayerObject.GetComponent(inventoryType);
        Ray aimRay = GetAimRay(ownerPlayer.GetComponent(interactionType));
        Component clientNail = Find(owner, nailNetworkObject.NetworkObjectId).GetComponent(nailType);
        Assert.That(FindAimedNail(aimRay, ownerPlayer.transform), Is.SameAs(clientNail),
            "The owning peer's production aim resolver must select its own prepared nail mirror. " + DescribeAim(clientNail, aimRay));
        Assert.That(FindAimedNail(aimRay, host.ConnectedClients[owner.LocalClientId].PlayerObject.transform), Is.SameAs(serverNail),
            "The server must resolve the same nail using the remote player's authoritative root.");
        MethodInfo trustedOriginCheck = actionType.GetMethod("IsTrustedMountingNailAimOrigin", PrivateInstance);
        Assert.That((bool)trustedOriginCheck.Invoke(serverPlayer,
            new object[] { aimRay.origin, aimRay.direction }), Is.True,
            "The normal owner's camera ray origin must pass server anchor validation before RPC probes.");
        ScriptableObject hammerForOwner = hammer;
        Assert.That(AddItem(ownerInventory, hammerForOwner), Is.True, "Owner equips the production IndustrialHammer through PlayerInventory.");
        yield return Until(() => ItemType(GetSelectedServerItem(serverInventory)) == "IndustrialHammer", "replicated hammer inventory");
        Assert.That(State(clientNail), Is.EqualTo("Prepared"), "Owner receives no predicted hit before the strike phase.");

        uint sequence = 0;
        InvokeImpactRpc(ownerAction, 77UL, aimRay.origin, aimRay.direction, ++sequence); // absent network object
        yield return Delay(0.15f);
        Assert.That(State(serverNail), Is.EqualTo("Prepared"), "Missing object request is refused.");
        Assert.That(NailInt(serverNail, "HitCount"), Is.Zero);

        SetServerInventorySlot(serverInventory, "Shovel");
        InvokeImpactRpc(ownerAction, nailNetworkObject.NetworkObjectId, aimRay.origin, aimRay.direction, ++sequence);
        yield return Delay(0.15f);
        Assert.That(ItemType(GetSelectedServerItem(serverInventory)), Is.EqualTo("Shovel"));
        Assert.That(State(serverNail), Is.EqualTo("Prepared"), "The server's selected tool controls eligibility.");
        Assert.That(NailInt(serverNail, "HitCount"), Is.Zero);
        SetServerInventorySlot(serverInventory, "IndustrialHammer");

        FieldInfo hammerRangeField = hammer.GetType().GetField("actionRange", PrivateInstance | BindingFlags.Public);
        float configuredHammerRange = (float)hammerRangeField.GetValue(hammer);
        hammerRangeField.SetValue(hammer, 0.1f);
        InvokeImpactRpc(ownerAction, nailNetworkObject.NetworkObjectId, aimRay.origin, aimRay.direction, ++sequence);
        yield return Delay(0.15f);
        Assert.That(State(serverNail), Is.EqualTo("Prepared"), "A valid ray cannot strike a nail beyond the server-selected action range.");
        Assert.That(NailInt(serverNail, "HitCount"), Is.Zero);
        hammerRangeField.SetValue(hammer, configuredHammerRange);

        InvokeImpactRpc(ownerAction, nailNetworkObject.NetworkObjectId, aimRay.origin + Vector3.right,
            aimRay.direction, ++sequence);
        yield return Delay(0.15f);
        Assert.That(State(serverNail), Is.EqualTo("Prepared"), "A forged distant origin is rejected.");
        Assert.That(NailInt(serverNail, "HitCount"), Is.Zero);

        GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(blocker);
        blocker.name = "Stage 2 RPC visibility blocker";
        blocker.transform.position = aimRay.origin + aimRay.direction * 0.2f;
        blocker.transform.localScale = new Vector3(0.4f, 0.4f, 0.08f);
        Physics.SyncTransforms();
        Assert.That(Physics.Raycast(aimRay, out RaycastHit blockerControl, 0.3f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore), Is.True);
        Assert.That(blockerControl.collider, Is.SameAs(blocker.GetComponent<Collider>()),
            "The control ray proves the obstacle lies before the exposed nail segment.");
        Transform exposedHead = (Transform)nailType.GetProperty("Head", PublicInstance).GetValue(serverNail);
        float exposedHeadDistance = Vector3.Dot(exposedHead.position - aimRay.origin, aimRay.direction);
        Assert.That(blockerControl.distance, Is.LessThan(exposedHeadDistance),
            "The blocker control must lie before the exposed head, not behind the candidate segment.");
        InvokeImpactRpc(ownerAction, nailNetworkObject.NetworkObjectId, aimRay.origin, aimRay.direction, ++sequence);
        yield return Delay(0.15f);
        Assert.That(State(serverNail), Is.EqualTo("Prepared"), "A solid blocker between the trusted aim origin and nail rejects impact.");
        Assert.That(NailInt(serverNail, "HitCount"), Is.Zero);
        UnityEngine.Object.Destroy(blocker);
        Physics.SyncTransforms();

        // Direct validation probes above consume the server sequence. Keep the owner's
        // local producer aligned before exercising the ordinary action path.
        actionType.GetField("submittedMountingNailImpactSequence", PrivateInstance).SetValue(ownerAction, sequence);
        // Exercise the real profiled click → WindUp → Strike → ImpactFreeze flow.
        MethodInfo handleAction = actionType.GetMethod("HandleAction", PrivateInstance);
        handleAction.Invoke(ownerAction, new object[] { null, EventArgs.Empty });
        yield return Until(() => actionType.GetProperty("CurrentActionPhase", PublicInstance).GetValue(ownerAction).ToString() != "None",
            "profiled action start");
        Assert.That(actionType.GetProperty("CurrentActionPhase", PublicInstance).GetValue(ownerAction).ToString(), Is.EqualTo("WindUp"));
        Assert.That(State(clientNail), Is.EqualTo("Prepared"), "The client cannot predict the first hit.");
        Assert.That(NailInt(clientNail, "HitCount"), Is.Zero);
        float strikeDeadline = Time.realtimeSinceStartup + 15f;
        while (NailInt(serverNail, "HitCount") != 1 && Time.realtimeSinceStartup < strikeDeadline) yield return null;
        Assert.That(NailInt(serverNail, "HitCount"), Is.EqualTo(1),
            $"Profiled impact did not reach the server. serverState={State(serverNail)}, clientState={State(clientNail)}, " +
            $"serverHits={NailInt(serverNail, "HitCount")}, submittedSeq={actionType.GetField("submittedMountingNailImpactSequence", PrivateInstance).GetValue(ownerAction)}, " +
            $"serverSeq={actionType.GetField("lastServerMountingNailImpactSequence", PrivateInstance).GetValue(serverPlayer)}, " +
            $"phase={actionType.GetProperty("CurrentActionPhase", PublicInstance).GetValue(ownerAction)}");
        Assert.That(State(serverNail), Is.EqualTo("Partial"));
        Assert.That(NailInt(serverNail, "RequiredHitCount"), Is.EqualTo(3));
        if (withPair)
            Assert.That(serverNail.GetType().GetField("embeddedSecondTarget", PrivateInstance).GetValue(serverNail),
                Is.SameAs(secondTargetObject.transform), "The production impact RPC selects the second physical root on its first accepted strike.");
        yield return Until(() => State(clientNail) == "Partial" && NailInt(clientNail, "HitCount") == 1,
            "owner receives partial hit state");
        if (withPair)
            yield return Until(() => object.Equals(clientNail.GetType().GetField("embeddedSecondTarget", PrivateInstance).GetValue(clientNail),
                Find(owner, secondTargetObject.NetworkObjectId).transform), "owner receives the original second target reference");
        sequence = (uint)actionType.GetField("submittedMountingNailImpactSequence", PrivateInstance).GetValue(ownerAction);
        actionType.GetMethod("HandleActionCanceled", PrivateInstance).Invoke(ownerAction, new object[] { null, EventArgs.Empty });
        uint tooFastSequence = ++sequence;
        InvokeImpactRpc(ownerAction, nailNetworkObject.NetworkObjectId, aimRay.origin, aimRay.direction, tooFastSequence);
        yield return Delay(0.12f);
        Assert.That(NailInt(serverNail, "HitCount"), Is.EqualTo(1), "A new but too-fast request is rate limited.");
        Assert.That(Find(owner, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(nailNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(Find(owner, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True);
        yield return Until(() => !(bool)actionType.GetProperty("IsActionInProgress", PublicInstance).GetValue(ownerAction),
            "profiled hammer action completion");

        lateJoin = CreateManager("Mounting nail stage 2 late join", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && lateJoin.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 3, "late joining client");
        CapturePlayerCopies();
        DisablePlayerCopies();
        DisableTargetMirrorColliders(lateJoin, targetObject.NetworkObjectId);
        if (withPair) DisableTargetMirrorColliders(lateJoin, secondTargetObject.NetworkObjectId);
        yield return Until(() => Find(lateJoin, nailNetworkObject.NetworkObjectId) != null
            && State(Find(lateJoin, nailNetworkObject.NetworkObjectId).GetComponent(nailType)) == "Partial",
            "late join partial snapshot");
        NetworkObject lateNail = Find(lateJoin, nailNetworkObject.NetworkObjectId);
        Assert.That(NailInt(lateNail.GetComponent(nailType), "HitCount"), Is.EqualTo(1));
        Assert.That(NailInt(lateNail.GetComponent(nailType), "RequiredHitCount"), Is.EqualTo(3));
        if (withPair)
            Assert.That(lateNail.GetComponent(nailType).GetType().GetField("embeddedSecondTarget", PrivateInstance).GetValue(
                lateNail.GetComponent(nailType)), Is.SameAs(Find(lateJoin, secondTargetObject.NetworkObjectId).transform),
                "A late joiner receives the originally selected second NetworkObject reference while the nail is partial.");
        Assert.That(lateNail.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(lateNail.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(Find(owner, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True);

        // Prepared timeout is gone after the first accepted strike.
        yield return Delay(3.2f);
        Assert.That(State(serverNail), Is.EqualTo("Partial"));
        Assert.That(NailInt(serverNail, "HitCount"), Is.EqualTo(1));
        InvokeImpactRpc(ownerAction, nailNetworkObject.NetworkObjectId, aimRay.origin, aimRay.direction, tooFastSequence);
        yield return Delay(0.15f);
        Assert.That(NailInt(serverNail, "HitCount"), Is.EqualTo(1),
            "Replaying the rate-limited request after cooldown is still rejected.");
        Transform serverTip = (Transform)nailType.GetProperty("Tip", PublicInstance).GetValue(serverNail);
        Vector3 expectedTipLocal = targetObject.transform.InverseTransformPoint(serverTip.position);
        Quaternion expectedNailRotationLocal = Quaternion.Inverse(targetObject.transform.rotation) * nailObject.transform.rotation;
        targetObject.transform.position += Vector3.right * 0.03f;
        if (withPair) secondTargetObject.transform.position += Vector3.right * 0.03f;
        Physics.SyncTransforms();
        yield return Until(() => Vector3.Distance(serverTip.position, targetObject.transform.TransformPoint(expectedTipLocal)) < 0.005f
            && Quaternion.Angle(nailObject.transform.rotation, targetObject.transform.rotation * expectedNailRotationLocal) < 0.1f,
            "server nail retains its observable local tip and rotation after target movement");
        yield return Until(() => Vector3.Distance(Find(owner, nailNetworkObject.NetworkObjectId).transform.position,
                                nailObject.transform.position) < 0.005f
                            && Vector3.Distance(Find(lateJoin, nailNetworkObject.NetworkObjectId).transform.position,
                                nailObject.transform.position) < 0.005f, "target-follow pose on all peers",
            () => "server=" + nailObject.transform.position + ", owner=" + Find(owner, nailNetworkObject.NetworkObjectId).transform.position
                + ", late=" + Find(lateJoin, nailNetworkObject.NetworkObjectId).transform.position + ", target=" + targetObject.transform.position
                + ", state=" + State(serverNail) + ", tip=" + serverTip.position);
        Assert.That(Find(owner, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(Find(lateJoin, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True);

        Component hostAction = host.LocalClient.PlayerObject.GetComponent(actionType);
        Component hostInventory = host.LocalClient.PlayerObject.GetComponent(inventoryType);
        SetServerInventorySlot(hostInventory, "IndustrialHammer");
        Ray hostAimRay = GetAimRay(host.LocalClient.PlayerObject.GetComponent(interactionType));
        actionType.GetField("submittedMountingNailImpactSequence", PrivateInstance).SetValue(ownerAction, sequence);
        if (withPair)
        {
            // Reach two hits through real player action RPCs, then enqueue an owner
            // pickup request while the target is still unlocked. The server completes
            // the third hit before its next transport pump, so that already-sent RPC
            // must be revalidated against the newly-published graph lock.
            InvokeImpactRpc(hostAction, nailNetworkObject.NetworkObjectId, hostAimRay.origin, hostAimRay.direction, 1);
            yield return Until(() => NailInt(serverNail, "HitCount") == 2, "second production hammer RPC hit");
            Component ownerTargetResource = Find(owner, targetObject.NetworkObjectId).GetComponent(resourceType);
            Assert.That((bool)resourceType.GetProperty("CanBePickedUp", PublicInstance).GetValue(ownerTargetResource), Is.True,
                "The owner sees the original target as pickup-eligible before the final strike.");
            resourceType.GetMethod("PickedUp", PublicInstance).Invoke(ownerTargetResource,
                new object[] { owner.LocalClient.PlayerObject.transform });
            Component ownerMountable = Find(owner, secondTargetObject.NetworkObjectId).GetComponent(mountableType);
            Assert.That((bool)mountableType.GetProperty("CanBePickedUp", PublicInstance).GetValue(ownerMountable), Is.True,
                "The owner sees the mountable pair partner as pickup-eligible before the final strike.");
            mountableType.GetMethod("PickedUp", PublicInstance).Invoke(ownerMountable,
                new object[] { owner.LocalClient.PlayerObject.transform });
            Assert.That((bool)resourceType.GetProperty("IsPickedUp", PublicInstance).GetValue(ownerTargetResource), Is.False,
                "The owner has submitted the pickup request but has not predicted a pickup.");
            Assert.That((bool)nailType.GetMethod("TryApplyHammerHit", PublicInstance).Invoke(serverNail,
                new[] { Enum.Parse(Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer") }), Is.True,
                "The server accepts the final hit after the owner's pickup RPC has entered the transport queue.");
            Assert.That(State(serverNail), Is.EqualTo("EmbeddedPair"));
            yield return Delay(0.2f);
            Assert.That((bool)resourceType.GetProperty("IsPickedUp", PublicInstance).GetValue(targetObject.GetComponent(resourceType)), Is.False,
                "The queued owner pickup is rejected after the final hit connects the target.");
            Component serverMountable = secondTargetObject.GetComponent(mountableType);
            Assert.That((bool)mountableType.GetProperty("IsPickedUp", PublicInstance).GetValue(serverMountable), Is.False,
                "The queued owner mountable pickup is rejected by the authoritative connected-target gate.");
            Assert.That(secondTargetObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
                "A rejected mountable request cannot transfer NetworkObject ownership.");
            Assert.That(targetObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
                "The stale pickup request cannot take network ownership.");
            Assert.That((bool)resourceType.GetProperty("CanBePickedUp", PublicInstance).GetValue(targetObject.GetComponent(resourceType)), Is.False);
        }
        else
        {
            InvokeImpactRpc(ownerAction, nailNetworkObject.NetworkObjectId, aimRay.origin, aimRay.direction, ++sequence);
            InvokeImpactRpc(hostAction, nailNetworkObject.NetworkObjectId, hostAimRay.origin, hostAimRay.direction, 1);
        }
        string embeddedState = withPair ? "EmbeddedPair" : "EmbeddedSingle";
        yield return Until(() => State(serverNail) == embeddedState, "accepted strike sequence completes the selected embedding",
            () => "state=" + State(serverNail) + ", hits=" + NailInt(serverNail, "HitCount") + "/" + NailInt(serverNail, "RequiredHitCount")
                + ", ownerSubmitted=" + actionType.GetField("submittedMountingNailImpactSequence", PrivateInstance).GetValue(ownerAction)
                + ", serverSequence=" + actionType.GetField("lastServerMountingNailImpactSequence", PrivateInstance).GetValue(serverPlayer)
                + ", first=" + nailType.GetField("embeddedTarget", PrivateInstance).GetValue(serverNail)
                + ", second=" + nailType.GetField("embeddedSecondTarget", PrivateInstance).GetValue(serverNail));
        Assert.That(NailInt(serverNail, "HitCount"), Is.EqualTo(3),
            withPair
                ? "Two hammer action RPC hits plus the server-completed queued-pickup race reach only the frozen required hit count."
                : "Two distinct player requests on the same server tick cannot exceed the frozen required hit count.");
        yield return Until(() => State(Find(owner, nailNetworkObject.NetworkObjectId).GetComponent(nailType)) == embeddedState
            && State(Find(lateJoin, nailNetworkObject.NetworkObjectId).GetComponent(nailType)) == embeddedState,
            "embedded snapshot on all clients");
        Assert.That(NailInt(Find(lateJoin, nailNetworkObject.NetworkObjectId).GetComponent(nailType), "HitCount"), Is.EqualTo(3));
        Assert.That(nailObject.GetComponent<Rigidbody>().isKinematic, Is.True,
            "The server keeps the world nail kinematic after embedding.");
        Assert.That(Find(owner, nailNetworkObject.NetworkObjectId).OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(Find(owner, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True,
            "The owning client does not simulate the embedded nail.");
        Assert.That(Find(lateJoin, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True,
            "The late-joining client does not simulate the embedded nail.");
        if (withPair)
        {
            Assert.That(GetSceneStateLock(hostSceneState, targetObject), Is.True, "Server locks the first original target.");
            Assert.That(GetSceneStateLock(hostSceneState, secondTargetObject), Is.True, "Server locks the second original target.");
            Component serverTargetResource = targetObject.GetComponent(resourceType);
            Component ownerTargetResource = Find(owner, targetObject.NetworkObjectId).GetComponent(resourceType);
            Assert.That((bool)resourceType.GetProperty("CanBePickedUp", PublicInstance).GetValue(serverTargetResource), Is.False,
                "The production BaseResource admission gate sees the connected target lock on the server.");
            resourceType.GetMethod("PickedUp", PublicInstance).Invoke(ownerTargetResource,
                new object[] { owner.LocalClient.PlayerObject.transform });
            Assert.That((bool)resourceType.GetProperty("IsPickedUp", PublicInstance).GetValue(serverTargetResource), Is.False,
                "A local player's production BaseResource pickup entry point leaves the connected resource grounded.");
            Assert.That((bool)resourceType.GetProperty("IsPickedUp", PublicInstance).GetValue(serverTargetResource), Is.False);
            Component ownerState = FindSceneState(owner);
            Component lateState = FindSceneState(lateJoin);
            yield return Until(() => GetSceneStateLock(ownerState, Find(owner, targetObject.NetworkObjectId))
                && GetSceneStateLock(ownerState, Find(owner, secondTargetObject.NetworkObjectId))
                && GetSceneStateLock(lateState, Find(lateJoin, targetObject.NetworkObjectId))
                && GetSceneStateLock(lateState, Find(lateJoin, secondTargetObject.NetworkObjectId)),
                "pair lock snapshot on owner and late joiner");
        }

        Assert.That(GetResourceBool(serverNail, "CanBeCarried"), Is.False,
            "Embedded nail cannot return to player carry through BaseResource.");
        Component npcCarrier = new GameObject("Stage 2 NPC pickup refusal").AddComponent(Type.GetType("NPCCarrier, Assembly-CSharp"));
        roots.Add(npcCarrier.gameObject);
        MethodInfo npcPickup = npcCarrier.GetType().GetMethod("TryPickup", PublicInstance);
        Assert.That((bool)npcPickup.Invoke(npcCarrier, new object[] { nailObject }), Is.False,
            "The production NPC carrier rejects the embedded BaseResource nail.");
        Component serverResource = nailObject.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"));
        Assert.That((bool)serverResource.GetType().GetProperty("IsPickedUp", PublicInstance).GetValue(serverResource), Is.False);
        if (withPair)
        {
            nailNetworkObject.Despawn(true);
            Component ownerState = FindSceneState(owner);
            Component lateState = FindSceneState(lateJoin);
            yield return Until(() => !GetSceneStateLock(hostSceneState, targetObject)
                && !GetSceneStateLock(hostSceneState, secondTargetObject)
                && !GetSceneStateLock(ownerState, Find(owner, targetObject.NetworkObjectId))
                && !GetSceneStateLock(lateState, Find(lateJoin, targetObject.NetworkObjectId)),
                "pair despawn releases server and client pickup locks");
            Component ownerTargetResource = Find(owner, targetObject.NetworkObjectId).GetComponent(resourceType);
            ownerTargetResource.GetType().GetMethod("PickedUp", PublicInstance).Invoke(ownerTargetResource,
                new object[] { owner.LocalClient.PlayerObject.transform });
            yield return Until(() => (bool)resourceType.GetProperty("IsPickedUp", PublicInstance)
                .GetValue(targetObject.GetComponent(resourceType)), "the same owner pickup RPC succeeds after unlock");
            Assert.That(targetObject.OwnerClientId, Is.EqualTo(owner.LocalClientId),
                "After the graph lock is removed, the valid BaseResource pickup route can transfer ownership.");

            resourceType.GetMethod("DroppedDown", PublicInstance).Invoke(ownerTargetResource, null);
            yield return Until(() => !(bool)resourceType.GetProperty("IsPickedUp", PublicInstance)
                .GetValue(targetObject.GetComponent(resourceType)), "BaseResource control is dropped before Mountable control");

            Component ownerMountable = Find(owner, secondTargetObject.NetworkObjectId).GetComponent(mountableType);
            mountableType.GetMethod("PickedUp", PublicInstance).Invoke(ownerMountable,
                new object[] { owner.LocalClient.PlayerObject.transform });
            yield return Until(() => (bool)mountableType.GetProperty("IsPickedUp", PublicInstance)
                .GetValue(secondTargetObject.GetComponent(mountableType)), "the same owner mountable pickup route succeeds after unlock");
            Assert.That(secondTargetObject.OwnerClientId, Is.EqualTo(owner.LocalClientId),
                "After the graph lock is removed, the valid Mountable pickup route can transfer ownership.");
        }
        if (withPair && secondTargetObject.IsSpawned) secondTargetObject.Despawn(false);
        if (targetObject.IsSpawned) targetObject.Despawn(false);
        yield return Until(() => !nailNetworkObject.IsSpawned, "server removes nail when first target despawns without destroy");
        yield return Until(() => Find(owner, nailNetworkObject.NetworkObjectId) == null
            && Find(lateJoin, nailNetworkObject.NetworkObjectId) == null, "target loss removes all nail mirrors");
        CaptureTargetCopies();
        foreach (GameObject targetCopy in targetCopies)
            if (targetCopy != null)
                foreach (Collider collider in targetCopy.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;

        NetworkObject serverPlayerObject = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        Component serverPlayerAction = serverPlayerObject.GetComponent(actionType);
        FieldInfo nextImpactTimeField = actionType.GetField("nextServerCombatImpactTime", PrivateInstance);
        yield return Until(() => host.ServerTime.Time >= (double)nextImpactTimeField.GetValue(serverPlayerAction),
            "combat cooldown expires before loose-hit scenario");
        savedPhysicsGravity = Physics.gravity;
        restorePhysicsGravity = true;
        Physics.gravity = Vector3.zero;

        // Exercise the loose-hammer RPC on a client-owned loose nail after the embedding
        // scenario has completed, then reconnect a fresh client to verify the kicked pose snapshot.
        Ray looseAimRay = GetAimRay(owner.LocalClient.PlayerObject.GetComponent(interactionType));
        profile.GetType().GetField("looseHammerHorizontalImpulseMetersPerSecond", PrivateInstance).SetValue(profile, 1.6f);
        profile.GetType().GetField("looseHammerVerticalImpulseMetersPerSecond", PrivateInstance).SetValue(profile, 0.35f);
        GameObject looseNailObject = UnityEngine.Object.Instantiate(nailPrefab);
        roots.Add(looseNailObject);
        Component looseNail = looseNailObject.GetComponent(nailType);
        nailType.GetField("profile", PrivateInstance).SetValue(looseNail, profile);
        nailType.GetMethod("ApplyProfileConfiguration", PrivateInstance).Invoke(looseNail, null);
        looseNailObject.transform.SetPositionAndRotation(looseAimRay.origin + looseAimRay.direction * 0.9f,
            Quaternion.LookRotation(looseAimRay.direction));
        Physics.SyncTransforms();
        NetworkObject looseNailNetworkObject = looseNailObject.GetComponent<NetworkObject>();
        looseNailNetworkObject.Spawn();
        spawned.Add(looseNailNetworkObject);
        looseNailNetworkObject.ChangeOwnership(owner.LocalClientId);
        yield return Until(() => Find(owner, looseNailNetworkObject.NetworkObjectId) != null,
            "loose nail mirror owned by remote player");
        NetworkObject looseOwnerMirror = Find(owner, looseNailNetworkObject.NetworkObjectId);
        yield return Until(() => Find(lateJoin, looseNailNetworkObject.NetworkObjectId) != null,
            "original late join mirror before ownership handoff");
        NetworkObject originalLateMirror = Find(lateJoin, looseNailNetworkObject.NetworkObjectId);
        Assert.That(looseNailNetworkObject.OwnerClientId, Is.EqualTo(owner.LocalClientId));
        Assert.That(looseOwnerMirror.OwnerClientId, Is.EqualTo(owner.LocalClientId));
        Assert.That(Vector3.Distance(looseNailObject.transform.position, looseOwnerMirror.transform.position), Is.LessThan(0.05f),
            "The loose nail is synchronized at its client-owned pose before the impact RPC.");
        IgnoreNailMirrorCollisions(looseNailObject, looseOwnerMirror.gameObject);
        IgnoreNailMirrorCollisions(looseNailObject, originalLateMirror.gameObject);
        Physics.SyncTransforms();
        Assert.That(FindAimedNail(looseAimRay, owner.LocalClient.PlayerObject.transform),
            Is.SameAs(looseOwnerMirror.GetComponent(nailType)), "The owner aim resolver selects the loose nail.");
        Assert.That(FindAimedNail(looseAimRay, host.ConnectedClients[owner.LocalClientId].PlayerObject.transform),
            Is.SameAs(looseNail), "The server resolves the same loose nail from the trusted aim ray.");

        FieldInfo serverSequenceField = actionType.GetField("lastServerMountingNailImpactSequence", PrivateInstance);
        uint looseSequence = unchecked((uint)serverSequenceField.GetValue(serverPlayerAction) + 1u);
        if (looseSequence == 0) looseSequence = 1;
        actionType.GetField("submittedMountingNailImpactSequence", PrivateInstance).SetValue(ownerAction, looseSequence);
        Rigidbody looseServerBody = looseNailObject.GetComponent<Rigidbody>();
        looseServerBody.linearDamping = 0f;
        looseServerBody.linearVelocity = Vector3.zero;
        looseServerBody.angularVelocity = Vector3.zero;
        Vector3 poseBeforeHandoff = looseOwnerMirror.transform.position;
        InvokeImpactRpc(ownerAction, looseNailNetworkObject.NetworkObjectId,
            looseAimRay.origin, looseAimRay.direction, looseSequence);
        yield return Until(() => looseNailNetworkObject.OwnerClientId == NetworkManager.ServerClientId
            && looseServerBody.linearVelocity.sqrMagnitude > 0.25f, "server loose-hit impulse and ownership handoff",
            () => "owner=" + looseNailNetworkObject.OwnerClientId + ", velocity=" + looseServerBody.linearVelocity
                + ", state=" + State(looseNail));
        Assert.That(State(looseNail), Is.EqualTo("Loose"));
        Assert.That(NailInt(looseNail, "HitCount"), Is.Zero, "A loose impact never prepares or embeds the nail.");
        Assert.That(Vector3.Distance(looseNailObject.transform.position, poseBeforeHandoff), Is.LessThan(0.1f),
            "The ownership handoff keeps the server pose within a small bound of the client-owned pose.");
        Vector3 horizontalKick = Vector3.ProjectOnPlane(looseServerBody.linearVelocity, Vector3.up);
        Assert.That(horizontalKick.magnitude, Is.EqualTo(1.6f).Within(0.25f));
        Vector3 serverAway = looseNailObject.transform.position - serverPlayerObject.transform.position;
        serverAway.y = 0f;
        serverAway.Normalize();
        Assert.That(Vector3.Dot(horizontalKick.normalized, serverAway), Is.GreaterThan(0.9f));
        Assert.That(looseNailNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
            "The server owns loose physics before applying the hit.");
        Assert.That(looseOwnerMirror.GetComponent<Rigidbody>().isKinematic, Is.True,
            "The former owner remains a kinematic mirror after handoff.");

        Rigidbody ownerVelocitySnapshot = looseNailObject.GetComponent<Rigidbody>();
        Vector3 velocityBeforeReplay = ownerVelocitySnapshot.linearVelocity;
        InvokeImpactRpc(ownerAction, looseNailNetworkObject.NetworkObjectId,
            looseAimRay.origin, looseAimRay.direction, looseSequence);
        yield return Delay(0.15f);
        Vector3 velocityAfterReplay = ownerVelocitySnapshot.linearVelocity;
        Vector3 replayDelta = Vector3.ProjectOnPlane(velocityAfterReplay - velocityBeforeReplay, Vector3.up);
        Assert.That(replayDelta.magnitude, Is.LessThan(0.25f), "Replaying the accepted RPC sequence applies no second kick.");
        looseServerBody.linearVelocity = Vector3.zero;
        looseServerBody.angularVelocity = Vector3.zero;

        NetworkManager kickedLateJoin = CreateManager("Mounting nail stage 4 kicked late join", port);
        Assert.That(kickedLateJoin.StartClient(), Is.True);
        yield return Until(() => kickedLateJoin.IsConnectedClient && kickedLateJoin.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 4, "late join after loose kick");
        CapturePlayerCopies();
        DisablePlayerCopies();
        yield return Until(() => Find(kickedLateJoin, looseNailNetworkObject.NetworkObjectId) != null,
            "late join creates kicked nail mirror");
        NetworkObject kickedLateMirror = Find(kickedLateJoin, looseNailNetworkObject.NetworkObjectId);
        IgnoreNailMirrorCollisions(looseNailObject, kickedLateMirror.gameObject);
        yield return Until(() => Vector3.Distance(kickedLateMirror.transform.position,
            looseNailObject.transform.position) < 0.05f, "late join receives kicked nail pose",
            () => "mirror=" + kickedLateMirror.transform.position + ", server=" + looseNailObject.transform.position);
        Assert.That(kickedLateMirror.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(kickedLateMirror.GetComponent<Rigidbody>().isKinematic, Is.True);

        owner.Shutdown(true);
        yield return Until(() => host.ConnectedClientsIds.Count == 3, "hammer owner disconnect after loose hit");
        Assert.That(looseNailNetworkObject.IsSpawned, Is.True, "Disconnecting the hitting player leaves the kicked world object spawned.");
        Assert.That(looseNailNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Debug.Log("[MountingNailStage2Network] passed");
#endif
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
#if !UNITY_EDITOR
        yield break;
#else
        bool mapsDisabled = true;
        bool managersStopped = false;
        bool noPlayerClones = false;
        bool noTargetClones = false;
        bool noMountableTargetClones = false;
        try
        {
            CapturePlayerCopies();
            CaptureTargetCopies();
            List<InputActionAsset> assets = CaptureInputAssets();
            try { DisposePlayerInputs(); }
            catch (Exception exception) { Debug.LogException(exception); mapsDisabled = false; }
            foreach (InputActionAsset asset in assets)
                if (asset != null)
                    foreach (InputActionMap map in asset.actionMaps) mapsDisabled &= !map.enabled;
            foreach (NetworkObject networkObject in spawned)
                if (networkObject != null && networkObject.IsSpawned
                    && networkObject.NetworkManager != null && networkObject.NetworkManager.IsServer)
                    networkObject.Despawn(true);
            foreach (NetworkManager manager in managers)
                if (manager != null && manager.IsListening) manager.Shutdown(true);
            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline
                && managers.Exists(manager => manager != null && (manager.IsListening || manager.ShutdownInProgress))) yield return null;
            managersStopped = !managers.Exists(manager => manager != null && (manager.IsListening || manager.ShutdownInProgress));
            CapturePlayerCopies();
            CaptureTargetCopies();
            foreach (GameObject playerCopy in playerCopies)
                if (playerCopy != null) UnityEngine.Object.Destroy(playerCopy);
            foreach (GameObject targetCopy in targetCopies)
                if (targetCopy != null) UnityEngine.Object.Destroy(targetCopy);
            foreach (GameObject root in roots)
                if (root != null) UnityEngine.Object.Destroy(root);
            yield return null;
            noPlayerClones = FindPlayerCopies().Count == 0 && playerCopies.TrueForAll(copy => copy == null);
            noTargetClones = FindTargetCopies().Count == 0 && targetCopies.TrueForAll(copy => copy == null);
            noMountableTargetClones = FindMountableTargetCopies().Count == 0;
        }
        finally
        {
            if (restorePhysicsGravity)
            {
                Physics.gravity = savedPhysicsGravity;
                restorePhysicsGravity = false;
            }
            foreach (NetworkManager manager in managers)
                if (manager != null && manager.IsListening) manager.Shutdown(true);
            foreach (GameObject playerCopy in playerCopies)
                if (playerCopy != null) UnityEngine.Object.Destroy(playerCopy);
            foreach (GameObject copy in FindPlayerCopies())
                if (copy != null) UnityEngine.Object.Destroy(copy);
            foreach (GameObject targetCopy in targetCopies)
                if (targetCopy != null) UnityEngine.Object.Destroy(targetCopy);
            foreach (GameObject copy in FindTargetCopies())
                if (copy != null) UnityEngine.Object.Destroy(copy);
            foreach (GameObject root in roots)
                if (root != null) UnityEngine.Object.Destroy(root);
            if (capturedSingleton)
            {
                if (previousSingleton != null) previousSingleton.SetSingleton();
                else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            }
            if (profile != null) UnityEngine.Object.Destroy(profile);
            if (hammer != null) UnityEngine.Object.Destroy(hammer);
            if (shovel != null) UnityEngine.Object.Destroy(shovel);
            managers.Clear(); roots.Clear(); playerCopies.Clear(); targetCopies.Clear(); spawned.Clear();
        }
        Assert.That(mapsDisabled, Is.True, "Fixture InputActionMaps must be disposed and disabled before teardown.");
        Assert.That(managersStopped, Is.True, "Every fixture manager completes shutdown before player clones are destroyed.");
        Assert.That(noPlayerClones, Is.True, "No Stage 2 minimal player clone remains after teardown.");
        Assert.That(noTargetClones, Is.True, "No Stage 2 target fixture clone remains after teardown.");
        Assert.That(noMountableTargetClones, Is.True, "No Stage 2 mountable target fixture clone remains after teardown.");
#endif
    }

#if UNITY_EDITOR
    private void LoadAssetsAndCreatePrefabs()
    {
        nailType = Type.GetType("MountingNail, Assembly-CSharp");
        inventoryType = Type.GetType("PlayerInventory, Assembly-CSharp");
        actionType = Type.GetType("PlayerActionController, Assembly-CSharp");
        interactionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        resourceType = Type.GetType("BaseResourceNew, Assembly-CSharp");
        mountableType = Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        boardResourceProfile = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/BaseResourceSO/WoodenBoard.asset");
        mountableProfile = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenBarrierMountable.asset");
        Assert.That(nailType, Is.Not.Null);
        Assert.That(resourceType, Is.Not.Null);
        Assert.That(mountableType, Is.Not.Null);
        Assert.That(boardResourceProfile, Is.Not.Null);
        Assert.That(mountableProfile, Is.Not.Null);
        nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        ScriptableObject productionHammer = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        ScriptableObject productionShovel = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/Shovel.asset");
        Assert.That(nailPrefab, Is.Not.Null); Assert.That(productionHammer, Is.Not.Null); Assert.That(productionShovel, Is.Not.Null);
        hammer = UnityEngine.Object.Instantiate(productionHammer);
        shovel = UnityEngine.Object.Instantiate(productionShovel);
        hammer.name = "Stage 2 production Industrial Hammer test clone";
        shovel.name = "Stage 2 production Shovel test clone";
        playerPrefab = CreatePlayerPrefab();
        targetPrefab = CreateTargetPrefab();
        mountableTargetPrefab = CreateMountableTargetPrefab();
        sceneStatePrefab = new GameObject("Mounting Nail scene state runtime template");
        roots.Add(sceneStatePrefab);
        NetworkObject stateObject = sceneStatePrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(stateObject, SceneStateFixtureHash);
        sceneStatePrefab.AddComponent(Type.GetType("MountingNailSceneState, Assembly-CSharp"));
        stateObject.SetSceneObjectStatus(false);
    }

    private GameObject CreatePlayerPrefab()
    {
        GameObject prefab = new(PlayerFixturePrefix);
        roots.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, PlayerFixtureHash);
        Behaviour input = (Behaviour)prefab.AddComponent(Type.GetType("PlayerInputNew, Assembly-CSharp"));
        input.enabled = false;
        Component inventory = prefab.AddComponent(inventoryType);
        FieldInfo catalogField = inventoryType.GetField("equippableItemCatalog", PrivateInstance);
        Array catalog = Array.CreateInstance(catalogField.FieldType.GetElementType(), 2);
        catalog.SetValue(hammer, 0);
        catalog.SetValue(shovel, 1);
        catalogField.SetValue(inventory, catalog);
        Component interaction = prefab.AddComponent(interactionType);
        CharacterController characterController = prefab.AddComponent<CharacterController>();
        characterController.height = 1.8f;
        characterController.radius = 0.3f;
        characterController.center = new Vector3(0f, 0.9f, 0f);
        prefab.AddComponent(Type.GetType("PlayerHealth, Assembly-CSharp"));
        prefab.AddComponent(Type.GetType("PlayerSingleCarryPlacementController, Assembly-CSharp"));
        prefab.AddComponent(Type.GetType("PlayerSingleCarryPlacementNetworkPreview, Assembly-CSharp"));
        prefab.AddComponent(Type.GetType("ClientNetworkTransform, Assembly-CSharp"));
        prefab.AddComponent(actionType);
        Transform anchor = new GameObject("SingleCarryPlacementAimAnchor").transform;
        anchor.SetParent(prefab.transform, false);
        anchor.localPosition = Vector3.zero;
        interactionType.GetField("interactionOrigin", PrivateInstance).SetValue(interaction, anchor);
        GameObject camera = new("Stage 2 owner aim camera");
        camera.transform.SetParent(anchor, false);
        camera.tag = "MainCamera";
        Camera cameraComponent = camera.AddComponent<Camera>();
        interactionType.GetField("aimCamera", PrivateInstance).SetValue(interaction, cameraComponent);
        return prefab;
    }

    private GameObject CreateTargetPrefab()
    {
        GameObject prefab = new("Mounting Nail Stage 2 target fixture");
        roots.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, TargetFixtureHash);
        prefab.AddComponent<NetworkTransform>();
        Rigidbody body = prefab.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        BoxCollider collider = prefab.AddComponent<BoxCollider>();
        collider.size = fixturePair ? new Vector3(0.05f, 0.05f, 0.05f) : new Vector3(1.2f, 1.2f, 1.2f);
        Component resource = prefab.AddComponent(resourceType);
        resourceType.GetField("baseResourceSO", PrivateInstance).SetValue(resource, boardResourceProfile);
        networkObject.SetSceneObjectStatus(false);
        return prefab;
    }

    private GameObject CreateMountableTargetPrefab()
    {
        GameObject prefab = new("Mounting Nail Stage 2 mountable target fixture");
        roots.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, 0x4E415336u);
        prefab.AddComponent<NetworkTransform>();
        Rigidbody body = prefab.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        BoxCollider collider = prefab.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.05f, 0.05f, 0.05f);
        Component mountable = prefab.AddComponent(mountableType);
        mountableType.GetField("mountableBridgeComponentSO", PrivateInstance).SetValue(mountable, mountableProfile);
        networkObject.SetSceneObjectStatus(false);
        return prefab;
    }

    private NetworkManager CreateManager(string name, ushort port)
    {
        GameObject root = new(name);
        roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = playerPrefab, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = nailPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = targetPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = mountableTargetPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = sceneStatePrefab });
        managers.Add(manager);
        return manager;
    }

    private static void IgnoreNailMirrorCollisions(GameObject serverNail, GameObject clientMirror)
    {
        Collider[] serverColliders = serverNail.GetComponentsInChildren<Collider>(true);
        Collider[] mirrorColliders = clientMirror.GetComponentsInChildren<Collider>(true);
        foreach (Collider serverCollider in serverColliders)
        foreach (Collider mirrorCollider in mirrorColliders)
            if (serverCollider != null && mirrorCollider != null)
                Physics.IgnoreCollision(serverCollider, mirrorCollider, true);
    }

    private void PlaceTipOnTarget(GameObject nailObject, GameObject target, float offsetFromNearFace = -0.005f)
    {
        Collider collider = target.GetComponent<Collider>();
        Vector3 tipPosition = new(collider.bounds.center.x, collider.bounds.center.y, collider.bounds.min.z + offsetFromNearFace);
        Transform tip = (Transform)nailType.GetProperty("Tip", PublicInstance).GetValue(nailObject.GetComponent(nailType));
        nailObject.transform.SetPositionAndRotation(tipPosition - (tip.position - nailObject.transform.position), Quaternion.identity);
    }

    private static bool AddItem(Component inventory, ScriptableObject item) =>
        (bool)inventory.GetType().GetMethod("AddItem", PublicInstance).Invoke(inventory, new object[] { item });

    private static object GetSelectedServerItem(Component inventory) =>
        inventory.GetType().GetMethod("GetSelectedItemForServerValidation", PublicInstance).Invoke(inventory, null);

    private static string ItemType(object item) => item?.GetType().GetField("itemType")?.GetValue(item)?.ToString();

    private static void SetServerInventorySlot(Component inventory, string typeName)
    {
        Type enumType = Type.GetType("EquippableItemType, Assembly-CSharp");
        int itemValue = (int)Enum.Parse(enumType, typeName);
        inventory.GetType().GetMethod("SetNetworkSlotValues", PrivateInstance).Invoke(inventory, new object[] { itemValue, -1 });
    }

    private static void InvokeImpactRpc(Component controller, ulong nailId, Vector3 origin, Vector3 direction, uint sequence)
    {
        MethodInfo rpc = controller.GetType().GetMethod("RequestMountingNailImpactServerRpc", PrivateInstance);
        Assert.That(rpc, Is.Not.Null);
        rpc.Invoke(controller, new object[] { nailId, origin, direction, sequence, default(ServerRpcParams) });
    }

    private Component FindAimedNail(Ray ray, Transform excludedRoot)
    {
        Type resolver = Type.GetType("MountingNailAimResolver, Assembly-CSharp");
        object[] args = { ray, 1.3f, excludedRoot, null, null, Vector3.zero };
        bool found = (bool)resolver.GetMethod("TryFindCandidate", PublicInstance | BindingFlags.Static)
            .Invoke(null, args);
        return found ? (Component)args[3] : null;
    }

    private static Ray GetAimRay(Component interaction)
    {
        object[] args = { default(Ray) };
        MethodInfo getAimRay = interaction.GetType().GetMethod("TryGetAimRay", PublicInstance, null,
            new[] { typeof(Ray).MakeByRefType() }, null);
        Assert.That((bool)getAimRay.Invoke(interaction, args), Is.True,
            "Fixture PlayerInteraction must provide its production camera aim ray.");
        return (Ray)args[0];
    }

    private string DescribeAim(Component nail, Ray ray)
    {
        Type profileType = nailType.GetProperty("Profile", PublicInstance).GetValue(nail).GetType();
        float radius = (float)profileType.GetProperty("AimRadiusMeters", PublicInstance)
            .GetValue(nailType.GetProperty("Profile", PublicInstance).GetValue(nail));
        object[] segmentArgs = { Vector3.zero, Vector3.zero };
        bool hasSegment = (bool)nailType.GetMethod("TryGetExposedAimSegment", PublicInstance).Invoke(nail, segmentArgs);
        Vector3 start = (Vector3)segmentArgs[0], end = (Vector3)segmentArgs[1];
        float along = 0f, nearestDistance = float.PositiveInfinity;
        if (hasSegment)
        {
            Vector3 segment = end - start;
            float fraction = segment.sqrMagnitude > 0.000001f
                ? Mathf.Clamp01(Vector3.Dot(ray.origin - start, segment) / segment.sqrMagnitude) : 0f;
            Vector3 closest = start + segment * fraction;
            along = Mathf.Max(0f, Vector3.Dot(closest - ray.origin, ray.direction));
            nearestDistance = Vector3.Distance(closest, ray.origin + ray.direction * along);
        }
        Collider[] overlap = Physics.OverlapCapsule(ray.origin, ray.origin + ray.direction * 1.3f,
            radius, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);
        string overlapNames = string.Join(" | ", Array.ConvertAll(overlap, value => value != null
            ? value.name + ":trigger=" + value.isTrigger + ":manager=" + value.GetComponentInParent<NetworkObject>()?.NetworkManager?.name
            : "null"));
        RaycastHit[] lineHits = Physics.RaycastAll(ray, 1.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        string hitNames = string.Join(" | ", Array.ConvertAll(lineHits, hit => hit.collider.name + ":" + hit.distance.ToString("F3")));
        Collider[] nailColliders = nail.GetComponentsInChildren<Collider>(true);
        string nailColliderInfo = string.Join(" | ", Array.ConvertAll(nailColliders,
            value => value.name + ":enabled=" + value.enabled + ":trigger=" + value.isTrigger));
        return $"rayOrigin={ray.origin}, rayDir={ray.direction}, state={State(nail)}, segment={hasSegment} {start}->{end}, " +
               $"radius={radius}, nearestAlong={along:F3}, lateral={nearestDistance:F3}, nailColliders={nailColliderInfo}, " +
               $"overlap=[{overlapNames}], hits=[{hitNames}]";
    }

    private static int NailInt(Component nail, string property) =>
        (int)nail.GetType().GetProperty(property, PublicInstance).GetValue(nail);

    private static Component FindSceneState(NetworkManager manager)
    {
        Type stateType = Type.GetType("MountingNailSceneState, Assembly-CSharp");
        foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjects.Values)
        {
            Component state = networkObject.GetComponent(stateType);
            if (state != null) return state;
        }
        return null;
    }

    private static bool GetSceneStateLock(Component state, NetworkObject target)
    {
        if (state == null || target == null) return false;
        object[] referenceArgs = { target.transform, null };
        MethodInfo createReference = state.GetType().GetMethod("TryCreateReference", PublicInstance);
        if (!(bool)createReference.Invoke(state, referenceArgs)) return false;
        return (bool)state.GetType().GetMethod("IsTargetLocked", PublicInstance)
            .Invoke(state, new[] { referenceArgs[1] });
    }

    private static string State(Component nail) => nail.GetType().GetProperty("State", PublicInstance).GetValue(nail).ToString();
    private static bool GetResourceBool(Component nail, string property) =>
        (bool)nail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp")).GetType().GetProperty(property, PublicInstance).GetValue(
            nail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp")));

    private void DisablePlayerCopies()
    {
        foreach (NetworkManager manager in managers)
        {
            if (manager?.SpawnManager == null) continue;
            foreach (NetworkObject player in manager.SpawnManager.SpawnedObjects.Values)
            {
                if (player == null || player.GetComponent(actionType) == null) continue;
                foreach (Collider collider in player.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (Camera camera in player.GetComponentsInChildren<Camera>(true))
                {
                    player.GetComponent(interactionType).GetType().GetMethod("SetAimCamera", PublicInstance)
                        .Invoke(player.GetComponent(interactionType), new object[] { camera });
                    camera.enabled = player.IsOwner;
                }
            }
        }
    }

    private static void DisableTargetMirrorColliders(NetworkManager manager, ulong targetId)
    {
        NetworkObject mirror = Find(manager, targetId);
        if (mirror == null) return;
        foreach (Collider collider in mirror.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        if (mirror.TryGetComponent(out Rigidbody body))
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    private static void FreezeTargetFixture(NetworkObject target)
    {
        if (target == null || !target.TryGetComponent(out Rigidbody body)) return;
        body.isKinematic = true;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeAll;
    }

    private void CapturePlayerCopies()
    {
        HashSet<int> seen = new();
        foreach (GameObject copy in playerCopies) if (copy != null) seen.Add(copy.GetInstanceID());
        foreach (GameObject copy in FindPlayerCopies()) if (copy != null && seen.Add(copy.GetInstanceID())) playerCopies.Add(copy);
    }

    private static List<GameObject> FindPlayerCopies()
    {
        List<GameObject> found = new();
        foreach (NetworkObject candidate in Resources.FindObjectsOfTypeAll<NetworkObject>())
            if (candidate != null && (uint)typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).GetValue(candidate) == PlayerFixtureHash
                && candidate.name.StartsWith(PlayerFixturePrefix, StringComparison.Ordinal)
                && candidate.name.EndsWith("(Clone)", StringComparison.Ordinal)) found.Add(candidate.gameObject);
        return found;
    }

    private void CaptureTargetCopies()
    {
        HashSet<int> seen = new();
        foreach (GameObject copy in targetCopies) if (copy != null) seen.Add(copy.GetInstanceID());
        foreach (GameObject copy in FindTargetCopies()) if (copy != null && seen.Add(copy.GetInstanceID())) targetCopies.Add(copy);
    }

    private static List<GameObject> FindTargetCopies()
    {
        List<GameObject> found = new();
        foreach (NetworkObject candidate in Resources.FindObjectsOfTypeAll<NetworkObject>())
            if (candidate != null && (uint)typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).GetValue(candidate) == TargetFixtureHash
                && candidate.name.StartsWith("Mounting Nail Stage 2 target fixture", StringComparison.Ordinal)
                && candidate.name.EndsWith("(Clone)", StringComparison.Ordinal)) found.Add(candidate.gameObject);
        foreach (GameObject mountableCopy in FindMountableTargetCopies())
            if (!found.Contains(mountableCopy)) found.Add(mountableCopy);
        return found;
    }

    private static List<GameObject> FindMountableTargetCopies()
    {
        List<GameObject> found = new();
        foreach (NetworkObject candidate in Resources.FindObjectsOfTypeAll<NetworkObject>())
            if (IsMountableTargetFixtureClone(candidate)) found.Add(candidate.gameObject);
        return found;
    }

    private static bool IsMountableTargetFixtureClone(NetworkObject candidate)
    {
        if (candidate == null
            || !candidate.name.StartsWith("Mounting Nail Stage 2 mountable target fixture", StringComparison.Ordinal)
            || !candidate.name.EndsWith("(Clone)", StringComparison.Ordinal)) return false;
        uint hash = (uint)typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).GetValue(candidate);
        return hash == 0x4E415336u || hash == 0x4E415435u;
    }

    private static void RemoveStaleMountableTargetFixtureClones()
    {
        foreach (GameObject copy in FindMountableTargetCopies())
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
    }

    private List<InputActionAsset> CaptureInputAssets()
    {
        List<InputActionAsset> assets = new();
        Type inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        FieldInfo actionsField = inputType?.GetField("playerGameInputActions", PrivateInstance);
        foreach (GameObject copy in playerCopies)
        {
            if (copy == null || actionsField == null) continue;
            Component input = copy.GetComponent(inputType);
            object wrapper = input != null ? actionsField.GetValue(input) : null;
            InputActionAsset asset = wrapper?.GetType().GetProperty("asset")?.GetValue(wrapper) as InputActionAsset;
            if (asset != null && !assets.Contains(asset)) assets.Add(asset);
        }
        return assets;
    }

    private void DisposePlayerInputs()
    {
        Type inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        MethodInfo dispose = inputType?.GetMethod("DisposeInput", PrivateInstance);
        if (dispose == null) return;
        HashSet<int> seen = new();
        foreach (GameObject copy in playerCopies)
        {
            if (copy == null || !seen.Add(copy.GetInstanceID())) continue;
            Component input = copy.GetComponent(inputType);
            if (input != null) dispose.Invoke(input, null);
        }
        foreach (NetworkManager manager in managers)
        {
            if (manager?.SpawnManager == null) continue;
            foreach (NetworkObject player in manager.SpawnManager.SpawnedObjects.Values)
            {
                if (player == null || !seen.Add(player.GetInstanceID())) continue;
                Component input = player.GetComponent(inputType);
                if (input != null) dispose.Invoke(input, null);
            }
        }
    }

    private static NetworkObject Find(NetworkManager manager, ulong id) =>
        manager != null && manager.SpawnManager != null && manager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject value) ? value : null;

    private static ushort ReservePort()
    {
        for (ushort port = 46206; port < 46306; port++)
            try { using UdpClient listener = new(port); return port; } catch (SocketException) { }
        throw new InvalidOperationException("No free loopback port for Stage 2 mounting nail tests.");
    }

    private static IEnumerator Delay(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline) yield return null;
    }

    private static IEnumerator Until(Func<bool> condition, string label, Func<string> failureDetails = null)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        bool succeeded = condition();
        Assert.That(succeeded, Is.True, "Timed out waiting for " + label + "."
            + (succeeded || failureDetails == null ? string.Empty : " (" + failureDetails() + ")"));
    }
#endif
}
