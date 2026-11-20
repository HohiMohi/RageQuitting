using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class CrowbarStage4NetworkPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private const uint ActorHash = 0x43524241u;
    private const uint StateHash = 0x43524242u;
    private readonly CrowbarInputSettingsScope inputSettings = new();
    private readonly List<GameObject> roots = new();
    private readonly List<NetworkManager> managers = new();
    private NetworkManager previousSingleton, host, client, lateJoin;
    private GameObject actorPrefab, statePrefab, nailPrefab, clientCameraObject, blocker;
    private ScriptableObject crowbarAsset;
    private ScriptableObject authoredCrowbarAsset, fixtureCrowbar, fixtureProfile;
    private ScriptableObject longDurationCrowbar, longDurationProfile;
    private string originalCrowbarJson, originalProfileJson, originalCrowbarGuid, originalProfileGuid;
    private string originalCrowbarHash;
    private string originalProfileHash;
    private GameObject isolatedPlayerPrefabRoot;
    private InputActionAsset isolatedPlayerActions;
    private bool usingProductionPlayerPrefab;
    private Type nailType, stateType, identityType, actorActionType, interactionType, inventoryType, playerInputType;
    private Component hostState, clientState, clientNail, serverNail, clientAction, clientInteraction, clientInventory;
    private NetworkObject clientActor, serverActor;
    private NetworkObject hostOwnedActor;
    private ulong nailNetworkId;
    private Transform hostTarget, clientTarget, anchor;
    private Camera aimCamera;
    private Mouse mouse;
    private Keyboard keyboard;
    private Mouse hostMouse;
    private Keyboard hostKeyboard;
    private bool restorePlayerLayerCollision;
    private int playerCollisionLayer;
    private bool originalPlayerLayerCollisionIgnored;
    private ushort testPort;

    [UnitySetUp]
    public IEnumerator StartPeers()
    {
        usingProductionPlayerPrefab = TestContext.CurrentContext.Test.Name.Contains(
            nameof(RemoteOwnerPhysicalRmbCompletesExtractionWithProductionPlayerNew));
        inputSettings.UseFocusedGameViewRouting();
        mouse = InputSystem.AddDevice<Mouse>();
        keyboard = InputSystem.AddDevice<Keyboard>();
        previousSingleton = NetworkManager.Singleton;
        nailType = Type.GetType("MountingNail, Assembly-CSharp");
        stateType = Type.GetType("MountingNailSceneState, Assembly-CSharp");
        identityType = Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
        actorActionType = Type.GetType("PlayerActionController, Assembly-CSharp");
        interactionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        inventoryType = Type.GetType("PlayerInventory, Assembly-CSharp");
        playerInputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        Assert.That(new[] { nailType, stateType, identityType, actorActionType, interactionType, inventoryType, playerInputType },
            Has.None.Null);
#if UNITY_EDITOR
        crowbarAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/Crowbar.asset");
        authoredCrowbarAsset = crowbarAsset;
        nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
#endif
        Assert.That(crowbarAsset, Is.Not.Null); Assert.That(nailPrefab, Is.Not.Null);
        ScriptableObject authoredProfile = (ScriptableObject)Read(authoredCrowbarAsset, "actionProfile");
        originalCrowbarJson = UnityEditor.EditorJsonUtility.ToJson(authoredCrowbarAsset);
        originalProfileJson = UnityEditor.EditorJsonUtility.ToJson(authoredProfile);
        originalCrowbarGuid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(authoredCrowbarAsset));
        originalProfileGuid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(authoredProfile));
        originalCrowbarHash = HashCrowbarAsset();
        originalProfileHash = HashAsset(authoredProfile);
        fixtureCrowbar = Object.Instantiate(authoredCrowbarAsset); fixtureCrowbar.name = "Crowbar Stage4 transient fixture copy";
        fixtureProfile = Object.Instantiate(authoredProfile); fixtureProfile.name = "Crowbar Stage4 transient timing profile";
        FieldInfo fixtureDuration = fixtureProfile.GetType().GetField("crowbarExtractionDuration", PublicInstance);
        Assert.That(fixtureDuration, Is.Not.Null); fixtureDuration.SetValue(fixtureProfile, 1.5f);
        fixtureCrowbar.GetType().GetField("actionProfile", PublicInstance).SetValue(fixtureCrowbar, fixtureProfile);
        crowbarAsset = fixtureCrowbar;
        BuildStatePrefab(); BuildActorPrefab();
        ReplaceCrowbarInInventoryCatalog(actorPrefab.GetComponent(inventoryType), fixtureCrowbar);
        if (usingProductionPlayerPrefab)
        {
            playerCollisionLayer = actorPrefab.layer;
            originalPlayerLayerCollisionIgnored = Physics.GetIgnoreLayerCollision(playerCollisionLayer, playerCollisionLayer);
            restorePlayerLayerCollision = true;
            Physics.IgnoreLayerCollision(playerCollisionLayer, playerCollisionLayer, true);
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Crowbar Stage4 production PlayerNew floor";
            floor.transform.position = new Vector3(200f, 9.5f, 200f);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            roots.Add(floor);
            Physics.SyncTransforms();
        }
        testPort = ReservePort();
        host = CreateManager("Crowbar Stage4 input host", testPort);
        Assert.That(host.StartHost(), Is.True); host.SetSingleton();
        yield return Until(() => host.IsListening, "host start");
        client = CreateManager("Crowbar Stage4 input client", testPort);
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "owner client connect");

        NetworkObject stateObject = Spawn(statePrefab, Vector3.zero);
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(stateObject.NetworkObjectId), "scene state replication");
        hostState = stateObject.GetComponent(stateType);
        clientState = client.SpawnManager.SpawnedObjects[stateObject.NetworkObjectId].GetComponent(stateType);
        hostTarget = CreateTarget("crowbar-stage4-target");
        clientTarget = CreateTarget("crowbar-stage4-target");
        Assert.That(Register(hostState, hostTarget.gameObject), Is.True);
        Assert.That(Register(clientState, clientTarget.gameObject), Is.True);

        serverActor = SpawnAsPlayer(actorPrefab, client.LocalClientId, new Vector3(200f, 10f, 198.95f));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(serverActor.NetworkObjectId), "owner actor replica");
        clientActor = client.SpawnManager.SpawnedObjects[serverActor.NetworkObjectId];
        clientAction = clientActor.GetComponent(actorActionType);
        clientInteraction = clientActor.GetComponent(interactionType);
        clientInventory = clientActor.GetComponent(inventoryType);
        ReplaceCrowbarInInventoryCatalog(clientInventory, fixtureCrowbar);
        Component clientPlayerInput = clientActor.GetComponent(playerInputType);
        if (usingProductionPlayerPrefab)
        {
            IsolateDuplicateNailColliders(serverActor, clientActor);
            yield return Until(() => ReadField(clientPlayerInput, "playerGameInputActions") != null,
                "production owner generated input actions initialize before fixture synchronization");
            ConfigureInputDevices(clientPlayerInput);
            ResetOwnedInputDevices();
        }
        Assert.That(clientInventory.GetType().GetMethod("AddItem", PublicInstance).Invoke(clientInventory, new object[] { crowbarAsset }), Is.True);
        Component serverInventory = serverActor.GetComponent(inventoryType);
        ReplaceCrowbarInInventoryCatalog(serverInventory, fixtureCrowbar);
        yield return Until(() => serverInventory.GetType().GetMethod("GetSelectedItemForServerValidation", PublicInstance).Invoke(serverInventory, null) == crowbarAsset,
            "server selected Crowbar replication");

        anchor = (Transform)Read(clientInteraction, "SingleCarryPlacementAimAnchor");
        Assert.That(anchor, Is.Not.Null);
        if (usingProductionPlayerPrefab)
        {
            Transform serverAnchor = (Transform)Read(serverActor.GetComponent(interactionType), "SingleCarryPlacementAimAnchor");
            NetworkTransform serverTransform = serverActor.GetComponent<NetworkTransform>();
            int stableAnchorFrames = 0;
            float anchorSyncDeadline = Time.realtimeSinceStartup + 6f;
            float anchorResidual = float.PositiveInfinity;
            while (stableAnchorFrames < 3 && Time.realtimeSinceStartup < anchorSyncDeadline)
            {
                Vector3 serverAnchorLocalOffset = serverActor.transform.InverseTransformPoint(serverAnchor.position);
                Vector3 trustedServerAnchor = serverTransform.GetSpaceRelativePosition(true)
                    + serverTransform.GetSpaceRelativeRotation(true)
                        * Vector3.Scale(serverAnchorLocalOffset, serverActor.transform.localScale);
                anchorResidual = Vector3.Distance(anchor.position, trustedServerAnchor);
                stableAnchorFrames = anchorResidual < 0.03f ? stableAnchorFrames + 1 : 0;
                if (stableAnchorFrames < 3) yield return new WaitForFixedUpdate();
            }
            Assert.That(stableAnchorFrames, Is.GreaterThanOrEqualTo(3),
                $"The production owner aim anchor must settle against the host's current NetworkTransform before nail placement. "
                + $"Residual={anchorResidual:F4}m; owner/server roots={clientActor.transform.position}/{serverActor.transform.position}; "
                + $"owner/server anchors={anchor.position}/{serverAnchor.position}; "
                + $"host ticks={client.NetworkTickSystem.ServerTime.Tick}/{host.NetworkTickSystem.ServerTime.Tick}.");

            Vector3 targetPosition = anchor.position + anchor.forward * 1f;
            hostTarget.position = targetPosition;
            clientTarget.position = targetPosition;
            Physics.SyncTransforms();
        }

        clientCameraObject = new GameObject("Crowbar Stage4 owner camera"); roots.Add(clientCameraObject);
        clientCameraObject.tag = "MainCamera";
        if (usingProductionPlayerPrefab)
        {
            clientCameraObject.transform.SetParent(anchor, false);
        }
        else
        {
            clientCameraObject.transform.SetParent(clientActor.transform, false);
            clientCameraObject.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            anchor = clientCameraObject.transform;
        }
        aimCamera = clientCameraObject.AddComponent<Camera>();
        clientInteraction.GetType().GetMethod("SetInteractionOrigin", PublicInstance).Invoke(clientInteraction, new object[] { anchor });
        clientInteraction.GetType().GetMethod("SetAimCamera", PublicInstance).Invoke(clientInteraction, new object[] { aimCamera });
        serverNail = SpawnNail(hostTarget);
        NetworkObject serverNailObject = serverNail.GetComponent<NetworkObject>();
        nailNetworkId = serverNailObject.NetworkObjectId;
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(nailNetworkId), "nail replication");
        clientNail = client.SpawnManager.SpawnedObjects[nailNetworkId].GetComponent(nailType);
        IsolateDuplicateNailColliders(serverNailObject, client.SpawnManager.SpawnedObjects[nailNetworkId]);
        yield return Until(() => ReadField(clientNail, "embeddedTarget") == clientTarget
            && Read(clientNail, "State").ToString() == "Partial", "client nail snapshot and local target resolve");
        AimAtNail();
        yield return null; yield return null; Physics.SyncTransforms();
        yield return Until(() => ReadField(clientPlayerInput, "playerGameInputActions") != null,
            "owner generated input actions initialize");
        if (!usingProductionPlayerPrefab) ConfigureInputDevices(clientPlayerInput);
        Component resolvedOwnerAim = ResolveAim();
        Assert.That(resolvedOwnerAim, Is.SameAs(clientNail),
            "The owner camera ray must resolve the real replicated nail. " + (usingProductionPlayerPrefab ? DescribeAimValidation() : string.Empty));
        if (usingProductionPlayerPrefab)
        {
            object[] canonicalRayArgs = { default(Ray) };
            Assert.That((bool)actorActionType.GetMethod("TryGetCrowbarExtractionAimRay", PrivateInstance)
                .Invoke(clientAction, canonicalRayArgs), Is.True);
            Ray canonicalRay = (Ray)canonicalRayArgs[0];
            bool trustedOrigin = (bool)actorActionType.GetMethod("IsTrustedMountingNailAimOrigin", PrivateInstance)
                .Invoke(serverActor.GetComponent(actorActionType), new object[] { canonicalRay.origin, canonicalRay.direction });
            Assert.That(trustedOrigin, Is.True,
                "The regression fixture must exercise the real trusted-origin path before testing the server component lifecycle. "
                + DescribeAimValidation());
        }
    }

    [UnityTearDown]
    public IEnumerator StopPeers()
    {
        bool stopped;
        try
        {
            foreach (NetworkManager manager in managers)
                if (manager != null && manager.IsListening) manager.Shutdown(true);
            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline && managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress))) yield return null;
            stopped = !managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress));
            foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
            yield return null;
            try
            {
                if (fixtureCrowbar != null) AssertSourceCrowbarRemainedUnchanged();
            }
            finally
            {
                if (isolatedPlayerActions != null) Object.Destroy(isolatedPlayerActions);
                isolatedPlayerActions = null;
                if (longDurationCrowbar != null) Object.Destroy(longDurationCrowbar);
                if (longDurationProfile != null) Object.Destroy(longDurationProfile);
                if (fixtureCrowbar != null) Object.Destroy(fixtureCrowbar);
                if (fixtureProfile != null) Object.Destroy(fixtureProfile);
                if (previousSingleton != null) previousSingleton.SetSingleton();
                else typeof(NetworkManager).GetMethod("ResetSingleton", StaticPrivate)?.Invoke(null, null);
                roots.Clear(); managers.Clear();
            }
        }
        finally
        {
            try
            {
                try
                {
                    RemoveOwnedDevice(keyboard);
                    RemoveOwnedDevice(mouse);
                    RemoveOwnedDevice(hostKeyboard);
                    RemoveOwnedDevice(hostMouse);
                }
                finally
                {
                    if (restorePlayerLayerCollision)
                    {
                        Physics.IgnoreLayerCollision(playerCollisionLayer, playerCollisionLayer,
                            originalPlayerLayerCollisionIgnored);
                        Assert.That(Physics.GetIgnoreLayerCollision(playerCollisionLayer, playerCollisionLayer),
                            Is.EqualTo(originalPlayerLayerCollisionIgnored),
                            "The test must restore the original same-layer player collision setting.");
                        restorePlayerLayerCollision = false;
                    }
                }
            }
            finally
            {
                inputSettings.Restore();
            }
        }
        Assert.That(stopped, Is.True, "NGO peers must complete shutdown before fixture objects are destroyed.");
    }

    [UnityTest]
    public IEnumerator HostOwnerPhysicalRmbUsesTheNetworkReservationPath()
    {
        ((Behaviour)clientActor.GetComponent(playerInputType)).enabled = false;
        NetworkObject hostActor = SpawnAsPlayer(actorPrefab, NetworkManager.ServerClientId, new Vector3(200f, 10f, 198.95f));
        Component hostInventory = hostActor.GetComponent(inventoryType);
        ReplaceCrowbarInInventoryCatalog(hostInventory, fixtureCrowbar);
        Assert.That(hostInventory.GetType().GetMethod("AddItem", PublicInstance).Invoke(hostInventory, new object[] { crowbarAsset }), Is.True);
        Component hostInteraction = hostActor.GetComponent(interactionType);
        Transform hostAnchor = hostActor.transform.Find("AimAnchor");
        GameObject hostCameraObject = new("Crowbar Stage4 host camera");
        hostCameraObject.tag = "MainCamera"; hostCameraObject.transform.SetParent(hostAnchor, false);
        Camera hostCamera = hostCameraObject.AddComponent<Camera>();
        Component hostAction = hostActor.GetComponent(actorActionType);
        hostInteraction.GetType().GetMethod("SetInteractionOrigin", PublicInstance).Invoke(hostInteraction, new object[] { hostAnchor });
        hostInteraction.GetType().GetMethod("SetAimCamera", PublicInstance).Invoke(hostInteraction, new object[] { hostCamera });
        hostCamera.transform.rotation = Quaternion.LookRotation(GetAimPoint(serverNail) - hostCamera.transform.position);
        yield return Until(() => ReadField(hostActor.GetComponent(playerInputType), "playerGameInputActions") != null,
            "host generated input actions initialize");
        EnsureHostInputDevices();
        ConfigureInputDevices(hostActor.GetComponent(playerInputType), hostKeyboard, hostMouse);
        yield return Until(() => hostInventory.GetType().GetMethod("GetSelectedItemForServerValidation", PublicInstance)
            .Invoke(hostInventory, null) == crowbarAsset, "host Crowbar selection");
        QueueRmb(hostMouse, true); yield return null;
        yield return Until(() => ReadAvailability(serverNail) == "Reserved"
            && ReadAvailability(clientNail) == "Reserved", "host physical RMB authoritative begin and observer snapshot");
        Assert.That(Read(hostAction, "IsCrowbarExtractionActiveOrPending"), Is.True);
        QueueRmb(hostMouse, false); yield return null;
        yield return Until(() => ReadAvailability(serverNail) == "Available", "host physical release cancels reservation");
    }

    [UnityTest]
    public IEnumerator HostAndRemoteMappedRmbCompeteForOneReservationAndLoserNeedsFreshPress()
    {
        NetworkObject hostActor = SpawnAsPlayer(actorPrefab, NetworkManager.ServerClientId,
            new Vector3(200f, 10f, 198.95f));
        Component hostInventory = hostActor.GetComponent(inventoryType);
        ReplaceCrowbarInInventoryCatalog(hostInventory, fixtureCrowbar);
        Assert.That(hostInventory.GetType().GetMethod("AddItem", PublicInstance).Invoke(hostInventory,
            new object[] { crowbarAsset }), Is.True);
        Component hostAction = hostActor.GetComponent(actorActionType);
        Component hostInteraction = hostActor.GetComponent(interactionType);
        Transform hostAnchor = hostActor.transform.Find("AimAnchor");
        GameObject hostCameraRoot = new("Crowbar Stage5 competing host camera"); roots.Add(hostCameraRoot);
        hostCameraRoot.tag = "MainCamera"; hostCameraRoot.transform.SetParent(hostAnchor, false);
        Camera hostCamera = hostCameraRoot.AddComponent<Camera>();
        hostInteraction.GetType().GetMethod("SetInteractionOrigin", PublicInstance).Invoke(hostInteraction, new object[] { hostAnchor });
        hostInteraction.GetType().GetMethod("SetAimCamera", PublicInstance).Invoke(hostInteraction, new object[] { hostCamera });
        yield return Until(() => ReadField(hostActor.GetComponent(playerInputType), "playerGameInputActions") != null,
            "host generated actions initialize");
        EnsureHostInputDevices();
        ConfigureInputDevices(hostActor.GetComponent(playerInputType), hostKeyboard, hostMouse);
        Assert.That(hostInventory.GetType().GetMethod("GetSelectedItemForServerValidation", PublicInstance)
            .Invoke(hostInventory, null), Is.SameAs(crowbarAsset));

        hostCamera.transform.rotation = Quaternion.LookRotation(GetAimPoint(serverNail) - hostCamera.transform.position);
        AimAtNail();
        QueueRmb(hostMouse, true);
        QueueRmb(mouse, true);
        yield return Until(() => ReadAvailability(serverNail) == "Reserved"
            && ReadAvailability(clientNail) == "Reserved", "concurrent host and remote player requests serialize to one reservation");
        object acceptedHandle = Read(Read(serverNail, "CrowbarExtractionReadState"), "Handle");
        ulong executorId = Convert.ToUInt64(Read(Read(serverNail, "CrowbarExtractionReadState"), "ExecutorNetworkObjectId"));
        bool hostWon = executorId == hostActor.NetworkObjectId;
        Assert.That(hostWon || executorId == serverActor.NetworkObjectId, Is.True,
            "The sole reservation belongs to one of the two actual player executors.");
        Component winnerAction = hostWon ? hostAction : clientAction;
        Component loserAction = hostWon ? clientAction : hostAction;
        Assert.That(Read(winnerAction, "IsCrowbarExtractionActiveOrPending"), Is.True);
        Component loserViewNail = hostWon ? clientNail : serverNail;
        yield return Until(() => !(bool)Read(loserAction, "IsCrowbarExtractionActiveOrPending"),
            "the losing physical request is rejected without local progress");
        Assert.That(ReadAvailability(loserViewNail), Is.EqualTo("Reserved"),
            "The non-winning player's nail view remains busy under the other player's reservation.");
        object[] presentationArgs = { (MonoBehaviour)loserViewNail, null, null, 0f, false, false, false };
        Assert.That((bool)actorActionType.GetMethod("TryGetCrowbarNailPresentation", PublicInstance)
            .Invoke(loserAction, presentationArgs), Is.True);
        Assert.That(presentationArgs[2].ToString(), Is.EqualTo("Reserved"));
        Assert.That(presentationArgs[4], Is.False, "The losing actor has no accepted local extraction session.");
        Assert.That(presentationArgs[5], Is.False, "The losing actor is no longer awaiting its own server acknowledgment.");
        Assert.That((float)presentationArgs[3], Is.Zero, "The losing actor exposes no other executor's progress as its own.");
        Assert.That(Read(Read(serverNail, "CrowbarExtractionReadState"), "Handle"), Is.EqualTo(acceptedHandle));

        QueueRmb(hostWon ? hostMouse : mouse, false);
        yield return Until(() => ReadAvailability(serverNail) == "Available", "winning player's release cancels the one reservation");
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.That(ReadAvailability(serverNail), Is.EqualTo("Available"),
            "A losing held button cannot silently autostart after the winner cancels.");
        QueueRmb(hostWon ? mouse : hostMouse, false);
        yield return null;
        AimAtNail();
        QueueRmb(hostWon ? mouse : hostMouse, true);
        yield return Until(() => ReadAvailability(serverNail) == "Reserved",
            "the losing owner can reserve only after a release and a fresh press");
        Assert.That(Read(Read(serverNail, "CrowbarExtractionReadState"), "ExecutorNetworkObjectId"),
            Is.EqualTo(hostWon ? serverActor.NetworkObjectId : hostActor.NetworkObjectId));
        QueueRmb(hostWon ? mouse : hostMouse, false);
    }

    [UnityTest]
    public IEnumerator PhysicalPlayerRmbAndServerOwnedNpcApiReservationCompeteInBothDirections()
    {
        NetworkObject npcObject = SpawnAsPlayer(actorPrefab, NetworkManager.ServerClientId,
            new Vector3(212f, 10f, 212f));
        AudioSource npcExecutor = npcObject.gameObject.AddComponent<AudioSource>();
        ((Behaviour)clientActor.GetComponent(playerInputType)).enabled = true;

        QueueRmb(true);
        yield return Until(() => ReadAvailability(serverNail) == "Reserved", "physical player wins the first reservation");
        object playerState = Read(serverNail, "CrowbarExtractionReadState");
        object[] rejectedNpc = { npcExecutor, 4.25f, null };
        Assert.That((bool)nailType.GetMethod("TryBeginCrowbarExtraction", PublicInstance)
            .Invoke(serverNail, rejectedNpc), Is.False, "The competing active server-owned NPC cannot replace the player.");
        Assert.That(Read(Read(serverNail, "CrowbarExtractionReadState"), "Handle"), Is.EqualTo(Read(playerState, "Handle")));
        QueueRmb(false);
        yield return Until(() => ReadAvailability(serverNail) == "Available", "player release clears its reservation");

        object[] acceptedNpc = { npcExecutor, 4.25f, null };
        Assert.That((bool)nailType.GetMethod("TryBeginCrowbarExtraction", PublicInstance)
            .Invoke(serverNail, acceptedNpc), Is.True, "The public API accepts an active server-owned Behaviour executor.");
        object npcHandle = acceptedNpc[2];
        object npcState = Read(serverNail, "CrowbarExtractionReadState");
        Assert.That(ReadFloat(npcState, "DurationSeconds"), Is.EqualTo(4.25f));
        Assert.That(Convert.ToUInt64(Read(npcState, "ExecutorNetworkObjectId")), Is.EqualTo(npcObject.NetworkObjectId));
        QueueRmb(true);
        yield return new WaitForSecondsRealtime(0.25f);
        object stillNpcState = Read(serverNail, "CrowbarExtractionReadState");
        Assert.That(Read(stillNpcState, "Handle"), Is.EqualTo(npcHandle));
        Assert.That(ReadFloat(stillNpcState, "DurationSeconds"), Is.EqualTo(4.25f));
        Assert.That(Read(stillNpcState, "ExecutorOwnerClientId"), Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(Read(clientAction, "IsCrowbarExtractionActiveOrPending"), Is.False,
            "A player request rejected by the NPC reservation does not become an accepted player session.");
        Assert.That((bool)nailType.GetMethod("CancelCrowbarExtraction", PublicInstance)
            .Invoke(serverNail, new[] { npcExecutor, npcHandle }), Is.True);
        QueueRmb(false);
        yield return Until(() => ReadAvailability(clientNail) == "Available", "NPC cancellation snapshot reaches client");
    }

    [UnityTest]
    public IEnumerator LateJoinSnapshotsPlayerReservationAndDisconnectCannotAffectReplacementNpcCommit()
    {
        UseLongDurationCrowbarForThisFixture();
        QueueRmb(true);
        yield return Until(() => ReadAvailability(serverNail) == "Reserved"
            && ReadAvailability(clientNail) == "Reserved", "player reservation active before late join");
        object ownerState = Read(serverNail, "CrowbarExtractionReadState");
        object handle = Read(ownerState, "Handle");
        ulong executorId = Convert.ToUInt64(Read(ownerState, "ExecutorNetworkObjectId"));
        ulong ownerId = Convert.ToUInt64(Read(ownerState, "ExecutorOwnerClientId"));
        double started = Convert.ToDouble(Read(ownerState, "StartedAtServerTime"));
        float duration = ReadFloat(ownerState, "DurationSeconds");
        Assert.That(duration, Is.EqualTo(5f).Within(0.001f),
            "This case uses only its transient cloned fixture profile to leave time for the late join handshake.");
        lateJoin = CreateManager("Crowbar Stage5 late join client", testPort);
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3,
            "late-joining observer connects while the owner reservation is active");
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(nailNetworkId),
            "late-joining observer receives nail and reservation snapshot");
        Component lateNail = lateJoin.SpawnManager.SpawnedObjects[nailNetworkId].GetComponent(nailType);
        NetworkObject sceneState = Find(lateJoin, hostState.GetComponent<NetworkObject>().NetworkObjectId);
        Assert.That(sceneState, Is.Not.Null);
        Component lateSceneState = sceneState.GetComponent(stateType);
        Transform lateTarget = CreateTarget("crowbar-stage4-target");
        Assert.That(Register(lateSceneState, lateTarget.gameObject), Is.True);
        yield return Until(() => ReadField(lateNail, "embeddedTarget") == lateTarget
            && ReadAvailability(lateNail) == "Reserved", "late-joining target identity resolves with the active reservation");
        object initialLateSnapshot = Read(lateNail, "CrowbarExtractionReadState");
        float initialLateProgress = ReadFloat(initialLateSnapshot, "ProgressNormalized");
        double lateJoinServerTime = lateJoin.ServerTime.Time;
        if (initialLateProgress <= 0f)
        {
            Debug.Log($"[Crowbar Stage4 late-join clock diagnostic] received reservation at client server-time={lateJoinServerTime:F6}, started-at={Read(ownerState, "StartedAtServerTime"):F6}; progress has not advanced yet.");
        }
        yield return Until(() => ReadAvailability(lateNail) == "Reserved"
            && Read(Read(lateNail, "CrowbarExtractionReadState"), "Handle").Equals(handle)
            && ReadFloat(Read(lateNail, "CrowbarExtractionReadState"), "ProgressNormalized") > 0f,
            "late-joining server clock advances the same reservation beyond its first snapshot tick");
        object lateState = Read(lateNail, "CrowbarExtractionReadState");
        Assert.That(Read(lateState, "Handle"), Is.EqualTo(handle));
        Assert.That(Read(lateState, "ExecutorNetworkObjectId"), Is.EqualTo(executorId));
        Assert.That(Read(lateState, "ExecutorOwnerClientId"), Is.EqualTo(ownerId));
        Assert.That(Convert.ToDouble(Read(lateState, "StartedAtServerTime")), Is.EqualTo(started));
        Assert.That(ReadFloat(lateState, "DurationSeconds"), Is.EqualTo(duration));
        Assert.That(ReadFloat(lateState, "ProgressNormalized"), Is.InRange(0f, 1f).And.GreaterThan(0f).And.LessThan(1f));

        GameObject retainedCamera = new("Crowbar Stage5 retained test camera across owner disconnect"); roots.Add(retainedCamera);
        retainedCamera.tag = "MainCamera";
        retainedCamera.AddComponent<Camera>();
        client.Shutdown(true);
        yield return Until(() => !host.ConnectedClients.ContainsKey(ownerId)
            && ReadAvailability(serverNail) == "Available", "disconnecting owner releases reservation on server");
        yield return Until(() => ReadAvailability(lateNail) == "Available", "owner disconnect cancellation replicates to remaining peer");
        QueueRmb(false);

        NetworkObject npcObject = SpawnAsPlayer(actorPrefab, NetworkManager.ServerClientId,
            new Vector3(214f, 10f, 214f));
        AudioSource npcExecutor = npcObject.gameObject.AddComponent<AudioSource>();
        object[] beginNpc = { npcExecutor, 0.2f, null };
        Assert.That((bool)nailType.GetMethod("TryBeginCrowbarExtraction", PublicInstance)
            .Invoke(serverNail, beginNpc), Is.True);
        object replacementHandle = beginNpc[2];
        Assert.That(replacementHandle, Is.Not.EqualTo(handle));
        Assert.That((bool)nailType.GetMethod("CancelCrowbarExtraction", PublicInstance)
            .Invoke(serverNail, new[] { npcExecutor, handle }), Is.False,
            "The same active NPC executor cannot cancel its replacement reservation with the disconnected owner's stale handle.");
        Assert.That(ReadAvailability(serverNail), Is.EqualTo("Reserved"));
        Assert.That(Read(Read(serverNail, "CrowbarExtractionReadState"), "Handle"), Is.EqualTo(replacementHandle));
        Assert.That((bool)nailType.GetMethod("TryCommitCrowbarExtraction", PublicInstance)
            .Invoke(serverNail, new[] { npcExecutor, handle }), Is.False,
            "The same executor cannot commit its replacement reservation with a stale handle.");
        Assert.That((bool)nailType.GetMethod("TryCommitCrowbarExtraction", PublicInstance)
            .Invoke(serverNail, new[] { npcExecutor, replacementHandle }), Is.False,
            "Even the correct NPC handle cannot commit before its own duration elapses.");
        yield return new WaitForSecondsRealtime(0.25f);
        Assert.That((bool)nailType.GetMethod("TryCommitCrowbarExtraction", PublicInstance)
            .Invoke(serverNail, new[] { npcExecutor, replacementHandle }), Is.True,
            "The replacement server-owned executor commits only after its own duration.");
        yield return Until(() => Find(host, nailNetworkId) == null && Find(lateJoin, nailNetworkId) == null,
            "final NPC commit despawn replicates to host and remaining late-join observer");
    }

    [UnityTest]
    public IEnumerator RemoteOwnerPhysicalRmbCompletesExtractionWithProductionPlayerNew()
    {
        host.SetSingleton();
        hostOwnedActor = SpawnAsPlayer(actorPrefab, NetworkManager.ServerClientId,
            new Vector3(210f, 10f, 210f));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(hostOwnedActor.NetworkObjectId),
            "host owner observer replica");
        NetworkObject clientObserverOfHost = client.SpawnManager.SpawnedObjects[hostOwnedActor.NetworkObjectId];
        Assert.That(((Behaviour)clientActor.GetComponent(actorActionType)).enabled, Is.True,
            "The actual owning client keeps its local PlayerActionController active.");
        Assert.That(((Behaviour)clientActor.GetComponent(playerInputType)).enabled, Is.True,
            "The actual owning client keeps local gameplay input active.");
        Assert.That(((Behaviour)serverActor.GetComponent(playerInputType)).enabled, Is.False,
            "The server-side remote replica must not process local input.");
        Assert.That(((Behaviour)serverActor.GetComponent(interactionType)).enabled, Is.False,
            "The server-side remote replica must not run local interaction input.");
        Type firstPersonControllerType = Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp");
        Assert.That(((Behaviour)serverActor.GetComponent(firstPersonControllerType)).enabled, Is.False,
            "The server-side remote replica must not run local movement or camera input.");
        Assert.That(((Behaviour)hostOwnedActor.GetComponent(actorActionType)).enabled, Is.True,
            "The host-owned PlayerActionController remains enabled on the host.");
        Assert.That(((Behaviour)clientObserverOfHost.GetComponent(actorActionType)).enabled, Is.False,
            "A non-owning remote observer continues to disable PlayerActionController.");

        QueueRmb(true);
        Assert.That(Read(clientAction, "IsCrowbarExtractionActiveOrPending"), Is.True,
            "The actual owner's mapped right mouse button must begin a local request before the server decides it.");
        float beginDeadline = Time.realtimeSinceStartup + 2f;
        while (ReadAvailability(serverNail) != "Reserved" && Time.realtimeSinceStartup < beginDeadline)
            yield return null;
        bool serverActionEnabled = ((Behaviour)serverActor.GetComponent(actorActionType)).enabled;
        Assert.That(ReadAvailability(serverNail), Is.EqualTo("Reserved"),
            $"The mapped remote-owner RMB press must reach server reservation. Server PAC enabled="
            + $"{serverActionEnabled}; owner pending="
            + $"{Read(clientAction, "IsCrowbarExtractionActiveOrPending")}; aim={DescribeAimValidation()}");
        Assert.That(serverActionEnabled, Is.True,
            "The server’s remote-owner PlayerActionController remains active only for authoritative validation; its local input stays disabled.");
        yield return Until(() => ReadAvailability(clientNail) == "Reserved", "owner reservation snapshot");
        Assert.That(ReadFloat(serverNail, "CrowbarExtractionProgressNormalized"), Is.LessThan(1f));
        double initialRefreshTime = Convert.ToDouble(ReadField(serverActor.GetComponent(actorActionType), "serverCrowbarLastRefreshTime"));
        yield return new WaitForSecondsRealtime(0.25f);
        Assert.That(Convert.ToDouble(ReadField(serverActor.GetComponent(actorActionType), "serverCrowbarLastRefreshTime")),
            Is.GreaterThan(initialRefreshTime), "The authoritative server lease must receive the owner's bounded aim refreshes.");
        Assert.That(ReadAvailability(serverNail), Is.EqualTo("Reserved"), "The server lease remains held while the owner maintains aim.");

        QueueRmb(false);
        yield return Until(() => ReadAvailability(serverNail) == "Available", "remote owner release cancels server reservation");
        yield return Until(() => ReadAvailability(clientNail) == "Available", "remote owner release reaches client snapshot");
        Assert.That(ReadFloat(serverNail, "CrowbarExtractionProgressNormalized"), Is.Zero,
            "A canceled remote attempt clears its progress before the next fresh press.");
        AimAtNail();
        QueueRmb(true);
        yield return Until(() => ReadAvailability(serverNail) == "Reserved"
            && ReadAvailability(clientNail) == "Reserved", "fresh remote owner press reserves the nail again");

        float commitDeadline = Time.realtimeSinceStartup + 4f;
        while (Find(host, nailNetworkId) != null && Time.realtimeSinceStartup < commitDeadline)
            yield return null;
        Assert.That(Find(host, nailNetworkId), Is.Null,
            "The server despawns the nail only after the configured extraction duration.");
        yield return Until(() => Find(client, nailNetworkId) == null, "committed nail despawn replication");
        QueueRmb(false);
    }

    [UnityTest]
    public IEnumerator LocallyCanceledAttemptNeverShowsItsStaleOwnSnapshotAsAnotherPlayersProgress()
    {
        QueueRmb(true);
        yield return Until(() => ReadAvailability(serverNail) == "Reserved"
            && ReadAvailability(clientNail) == "Reserved", "owner reservation reaches observer");

        actorActionType.GetMethod("CancelCrowbarExtractionImmediately", PrivateInstance).Invoke(clientAction, null);
        Assert.That(ReadAvailability(clientNail), Is.EqualTo("Reserved"),
            "The test samples the owner's previous replicated state before its cancel delta arrives.");
        MethodInfo presentation = actorActionType.GetMethod("TryGetCrowbarNailPresentation", PublicInstance);
        object[] args = { (MonoBehaviour)clientNail, null, null, 0f, false, false, false };
        Assert.That((bool)presentation.Invoke(clientAction, args), Is.True);
        Assert.That(args[4], Is.False, "A canceled generation is never treated as an accepted local session.");
        Assert.That(args[5], Is.True, "A same-owner stale reservation snapshot stays neutral while cancellation synchronizes.");
        Assert.That((float)args[3], Is.Zero, "No stale snapshot percentage is exposed.");

        yield return Until(() => ReadAvailability(serverNail) == "Available", "server processes owner cancel");
        yield return Until(() => ReadAvailability(clientNail) == "Available", "owner receives canceled snapshot");
    }

    [UnityTest]
    public IEnumerator OwnerRmbReplicatesReservationSurvivesMovementAndCancelsOnLostLosThenFreshPressCanRestart()
    {
        QueueRmb(true); yield return null;
        yield return Until(() => ReadAvailability(serverNail) == "Reserved"
            && ReadAvailability(clientNail) == "Reserved", "owner begin RPC and observer reservation snapshot");
        object handle = Read(Read(serverNail, "CrowbarExtractionReadState"), "Handle");
        ulong handleHigh = Convert.ToUInt64(ReadField(handle, "High"));
        Assert.That(handleHigh, Is.Not.Zero);
        Assert.That(Read(clientAction, "ActionMovementMultiplier"), Is.EqualTo(0.65f).Within(0.001f));
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.That(ReadFloat(serverNail, "CrowbarExtractionProgressNormalized"), Is.GreaterThan(0f));

        // Move the owner-authoritative NetworkTransform in small increments while continuously aiming
        // at the same visible nail; each 100 ms client heartbeat carries a fresh origin and ray.
        float initialZ = clientActor.transform.position.z;
        float moveUntil = Time.realtimeSinceStartup + 0.45f;
        while (Time.realtimeSinceStartup < moveUntil)
        {
            clientActor.transform.position += Vector3.forward * (0.65f * 2f * Time.unscaledDeltaTime);
            AimAtNail();
            yield return null;
            if (ReadAvailability(serverNail) != "Reserved")
                Assert.Fail("The authority canceled while the owner remained aimed at the nail: " + DescribeAimValidation());
        }
        yield return Until(() => Mathf.Abs(serverActor.transform.position.z - clientActor.transform.position.z) < 0.14f,
            "owner transform movement reaches authority");
        Assert.That(clientActor.transform.position.z, Is.GreaterThan(initialZ + 0.1f));
        Assert.That(ReadAvailability(serverNail), Is.EqualTo("Reserved"),
            "Fresh owner aim while moving remains trusted and does not cancel the active reservation.");

        blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); roots.Add(blocker);
        blocker.name = "Crowbar Stage4 temporary LOS blocker";
        blocker.transform.position = (anchor.position + GetAimPoint(clientNail)) * 0.5f;
        blocker.transform.localScale = Vector3.one * 0.2f;
        Physics.SyncTransforms();
        yield return Until(() => ReadAvailability(serverNail) == "Available", "fresh heartbeat detects blocked line of sight");
        yield return Until(() => ReadAvailability(clientNail) == "Available", "observer receives canceled reservation snapshot");
        Assert.That(ReadAvailability(clientNail), Is.EqualTo("Available"));

        Object.Destroy(blocker); blocker = null; yield return null; Physics.SyncTransforms(); AimAtNail();
        // Queue release and a fresh press before yielding, while the old replicated snapshot may remain.
        QueueRmb(false); QueueRmb(true); yield return null;
        yield return Until(() => ReadAvailability(serverNail) == "Reserved", "fresh press is serialized after cancellation");
        Assert.That(Read(clientAction, "IsCrowbarExtractionActiveOrPending"), Is.True);
        uint acceptedAttempt = Convert.ToUInt32(ReadField(clientAction, "activeCrowbarAttempt"));
        MethodInfo earlyCommit = actorActionType.GetMethod("RequestCrowbarExtractionCommitServerRpc", PrivateInstance);
        earlyCommit.Invoke(clientAction, new object[] { acceptedAttempt, nailNetworkId, anchor.position, anchor.forward, default(ServerRpcParams) });
        yield return null;
        Assert.That(ReadAvailability(serverNail), Is.EqualTo("Reserved"), "An early commit request preserves the valid reservation.");
        Assert.That(Read(clientAction, "IsCrowbarExtractionActiveOrPending"), Is.True,
            "The owner remains active and receives a deferred retry instead of becoming stuck or losing the handle.");
        yield return Until(() => !host.SpawnManager.SpawnedObjects.ContainsKey(nailNetworkId)
            && !client.SpawnManager.SpawnedObjects.ContainsKey(nailNetworkId), "owner commit after server duration replicates despawn");
        Assert.That(Read(clientAction, "IsCrowbarExtractionActiveOrPending"), Is.False);
    }

    [UnityTest]
    public IEnumerator RapidReleaseAndFreshPressBeforeBeginAckUsesANewAttempt()
    {
        QueueRmb(true);
        QueueRmb(false);
        QueueRmb(true);
        yield return null;
        yield return Until(() => ReadAvailability(serverNail) == "Reserved"
            && ReadAvailability(clientNail) == "Reserved", "the newest begin wins after queued cancel and prior begin");
        uint latest = Convert.ToUInt32(ReadField(clientAction, "activeCrowbarAttempt"));
        Assert.That(latest, Is.GreaterThan(1), "Each press has a distinct sequence even before the earlier begin is acknowledged.");
        Assert.That(Read(clientAction, "IsCrowbarExtractionActiveOrPending"), Is.True,
            "A stale begin acknowledgment cannot cancel or replace the new owner attempt.");
        QueueRmb(false); yield return null;
        yield return Until(() => ReadAvailability(serverNail) == "Available", "latest attempt cancel is released");
    }

    [UnityTest]
    public IEnumerator OwnerBeginCannotBeForgedWithRemoteAimAndOldSequenceCannotRestart()
    {
        uint attempt = NextOwnerAttempt(clientAction);
        MethodInfo begin = actorActionType.GetMethod("RequestCrowbarExtractionBeginServerRpc", PrivateInstance);
        ulong nailId = serverNail.GetComponent<NetworkObject>().NetworkObjectId;
        begin.Invoke(clientAction, new object[] { attempt, nailId, new Vector3(500f, 500f, 500f), Vector3.forward, default(ServerRpcParams) });
        yield return null; yield return null;
        Assert.That(ReadAvailability(serverNail), Is.EqualTo("Available"), "Server validates origin against the trusted NetworkTransform anchor.");
        Assert.That(Read(clientAction, "IsCrowbarExtractionActiveOrPending"), Is.False);
        actorActionType.GetField("crowbarAttemptSequence", PrivateInstance).SetValue(clientAction, attempt);

        QueueRmb(true); yield return null;
        yield return Until(() => ReadAvailability(serverNail) == "Reserved", "valid physical RMB begins on server");
        uint acceptedAttempt = Convert.ToUInt32(ReadField(clientAction, "activeCrowbarAttempt"));
        QueueRmb(false); yield return null;
        yield return Until(() => ReadAvailability(serverNail) == "Available", "valid owner cancellation");
        begin.Invoke(clientAction, new object[] { acceptedAttempt, nailId, anchor.position, anchor.forward, default(ServerRpcParams) });
        yield return null; yield return null;
        Assert.That(ReadAvailability(serverNail), Is.EqualTo("Available"), "A stale begin sequence cannot recreate a released reservation.");
    }

#if UNITY_EDITOR
    private void BuildStatePrefab()
    {
        statePrefab = new GameObject("Crowbar Stage4 state prefab"); roots.Add(statePrefab);
        NetworkObject networkObject = statePrefab.AddComponent<NetworkObject>();
        SetHash(networkObject, StateHash); statePrefab.AddComponent(stateType); networkObject.SetSceneObjectStatus(false);
    }

    private void BuildActorPrefab()
    {
#if UNITY_EDITOR
        if (usingProductionPlayerPrefab)
        {
            actorPrefab = CreateIsolatedProductionPlayerPrefab(
                UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab"));
            return;
        }
#endif
        actorPrefab = new GameObject("Crowbar Stage4 owner actor prefab"); roots.Add(actorPrefab);
        NetworkObject networkObject = actorPrefab.AddComponent<NetworkObject>(); SetHash(networkObject, ActorHash); networkObject.SetSceneObjectStatus(false);
        actorPrefab.AddComponent(Type.GetType("ClientNetworkTransform, Assembly-CSharp"));
        actorPrefab.AddComponent(playerInputType);
        Component inventory = actorPrefab.AddComponent(inventoryType);
        UnityEditor.SerializedObject serialized = new UnityEditor.SerializedObject(inventory);
        UnityEditor.SerializedProperty catalog = serialized.FindProperty("equippableItemCatalog");
        catalog.arraySize = 1; catalog.GetArrayElementAtIndex(0).objectReferenceValue = crowbarAsset; serialized.ApplyModifiedPropertiesWithoutUndo();
        Component interaction = actorPrefab.AddComponent(interactionType);
        Component action = actorPrefab.AddComponent(actorActionType);
        Transform anchorTransform = new GameObject("AimAnchor").transform; anchorTransform.SetParent(actorPrefab.transform, false);
        anchorTransform.localPosition = new Vector3(0f, 0.5f, 0f);
        interaction.GetType().GetField("interactionOrigin", PrivateInstance).SetValue(interaction, anchorTransform);
    }

    private GameObject CreateIsolatedProductionPlayerPrefab(GameObject sourcePrefab)
    {
        Assert.That(sourcePrefab, Is.Not.Null);
        PlayerInput sourceInput = sourcePrefab.GetComponent<PlayerInput>();
        FieldInfo actionsField = typeof(PlayerInput).GetField("m_Actions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(sourceInput, Is.Not.Null);
        Assert.That(actionsField, Is.Not.Null);
        InputActionAsset sourceActions = (InputActionAsset)actionsField.GetValue(sourceInput);
        Assert.That(sourceActions, Is.Not.Null);

        isolatedPlayerActions = Object.Instantiate(sourceActions);
        isolatedPlayerPrefabRoot = new GameObject("Crowbar Stage4 isolated production PlayerNew prefab holder");
        isolatedPlayerPrefabRoot.SetActive(false);
        roots.Add(isolatedPlayerPrefabRoot);
        GameObject isolatedPrefab = Object.Instantiate(sourcePrefab, isolatedPlayerPrefabRoot.transform);
        PlayerInput isolatedInput = isolatedPrefab.GetComponent<PlayerInput>();
        Assert.That(isolatedInput, Is.Not.Null);
        actionsField.SetValue(isolatedInput, isolatedPlayerActions);
        Assert.That(isolatedInput.actions, Is.SameAs(isolatedPlayerActions));
        return isolatedPrefab;
    }

    private NetworkManager CreateManager(string name, ushort port)
    {
        GameObject root = new(name); roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>(); transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = nailPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = actorPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = statePrefab });
        GameObject dropPrefab = (GameObject)Read(crowbarAsset, "equippableItemPrefab");
        if (dropPrefab != null) manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = dropPrefab });
        managers.Add(manager); return manager;
    }

    private NetworkObject Spawn(GameObject prefab, Vector3 position)
    {
        NetworkObject value = Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<NetworkObject>();
        roots.Add(value.gameObject); value.SetSceneObjectStatus(false); value.Spawn(); return value;
    }

    private NetworkObject SpawnAsPlayer(GameObject prefab, ulong owner, Vector3 position)
    {
        NetworkObject value = Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<NetworkObject>();
        roots.Add(value.gameObject); value.SetSceneObjectStatus(false); value.SpawnAsPlayerObject(owner); return value;
    }

    private Component SpawnNail(Transform target)
    {
        GameObject instance = Object.Instantiate(nailPrefab, target.position, Quaternion.identity); roots.Add(instance);
        Component nail = instance.GetComponent(nailType); NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        networkObject.SetSceneObjectStatus(false); networkObject.Spawn();
        nailType.GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(nail, new object[] { target, null, target.position, Vector3.forward });
        nailType.GetMethod("SetHitCounts", PrivateInstance).Invoke(nail, new object[] { 1, 3, 0.02f });
        nailType.GetMethod("SetState", PrivateInstance).Invoke(nail, new[] { Enum.Parse(Type.GetType("MountingNailState, Assembly-CSharp"), "Partial") });
        return nail;
    }
#endif

    private Transform CreateTarget(string stableId)
    {
        GameObject value = new("Crowbar Stage4 local target"); roots.Add(value);
        value.transform.position = new Vector3(200f, 10.5f, 200f);
        Rigidbody body = value.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
        value.AddComponent<BoxCollider>().enabled = false;
        Component identity = value.AddComponent(identityType); identityType.GetField("stableTargetId", PrivateInstance).SetValue(identity, stableId);
        return value.transform;
    }

    private bool Register(Component state, GameObject target) => (bool)stateType.GetMethod("RegisterLocalTarget", PublicInstance)
        .Invoke(state, new[] { target.GetComponent(identityType) });

    private void IsolateDuplicateNailColliders(NetworkObject server, NetworkObject clientReplica)
    {
        foreach (Collider a in server.GetComponentsInChildren<Collider>(true))
        foreach (Collider b in clientReplica.GetComponentsInChildren<Collider>(true))
            if (a != null && b != null) Physics.IgnoreCollision(a, b, true);
    }

    private void AimAtNail()
    {
        Vector3 point = GetAimPoint(clientNail);
        aimCamera.transform.rotation = Quaternion.LookRotation(point - aimCamera.transform.position);
        Physics.SyncTransforms();
    }

    private static Vector3 GetAimPoint(Component nail)
    {
        object[] args = { Vector3.zero, Vector3.zero };
        Assert.That((bool)nailTypeStatic.GetMethod("TryGetExposedAimSegment", PublicInstance).Invoke(nail, args), Is.True);
        return ((Vector3)args[0] + (Vector3)args[1]) * 0.5f;
    }

    private static Type nailTypeStatic => Type.GetType("MountingNail, Assembly-CSharp");
    private Component ResolveAim()
    {
        object[] rayArgs = { default(Ray) };
        Assert.That((bool)actorActionType.GetMethod("TryGetCrowbarExtractionAimRay", PrivateInstance)
            .Invoke(clientAction, rayArgs), Is.True);
        MethodInfo resolver = Type.GetType("MountingNailAimResolver, Assembly-CSharp").GetMethod("TryFindCandidate", BindingFlags.Static | BindingFlags.Public);
        object[] args = { (Ray)rayArgs[0], ReadFloat(crowbarAsset, "actionRange"), clientActor.transform, null, null, Vector3.zero };
        return (bool)resolver.Invoke(null, args) ? (Component)args[3] : null;
    }

    private void QueueRmb(bool pressed)
    {
        QueueRmb(mouse, pressed);
    }

    private void QueueRmb(Mouse device, bool pressed)
    {
        Assert.That(device, Is.Not.Null, "Each actor must have a dedicated fixture mouse bound to its generated action asset.");
        Assert.That(device.enabled, Is.True, "The fixture mouse must stay enabled even when the Editor GameView is unfocused.");
        Assert.That(IsOwnDeviceRegistered(device), Is.True, "Input events target only the registered device already bound to this actor.");
        MouseState state = default;
        state = state.WithButton(MouseButton.Right, pressed);
        InputSystem.QueueStateEvent(device, state);
        InputSystem.Update();
    }

    private void ConfigureInputDevices(Component playerInput)
    {
        ConfigureInputDevices(playerInput, keyboard, mouse);
    }

    private void ConfigureInputDevices(Component playerInput, Keyboard actorKeyboard, Mouse actorMouse)
    {
        object actions = ReadField(playerInput, "playerGameInputActions");
        Assert.That(actions, Is.Not.Null, "PlayerInputNew must initialize its own generated action asset before binding fixture devices.");
        InputActionAsset asset = (InputActionAsset)actions.GetType().GetProperty("asset", PublicInstance).GetValue(actions);
        Assert.That(asset, Is.Not.Null);
        asset.devices = new InputDevice[] { actorKeyboard, actorMouse };
        InputSystem.Update();
        object game = actions.GetType().GetProperty("Game", PublicInstance).GetValue(actions);
        InputAction alt = (InputAction)game.GetType().GetProperty("ActionAlt", PublicInstance).GetValue(game);
        Assert.That(alt.controls, Has.Some.Matches<InputControl>(control => ReferenceEquals(control.device, actorMouse)),
            "The generated action must resolve its real button binding to this actor's private virtual mouse.");
    }

    private void EnsureHostInputDevices()
    {
        if (hostMouse == null || !hostMouse.added || !IsOwnDeviceRegistered(hostMouse)) hostMouse = InputSystem.AddDevice<Mouse>();
        if (hostKeyboard == null || !hostKeyboard.added || !IsOwnDeviceRegistered(hostKeyboard)) hostKeyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(hostKeyboard, new KeyboardState());
        InputSystem.QueueStateEvent(hostMouse, new MouseState());
        InputSystem.Update();
    }

    private void UseLongDurationCrowbarForThisFixture()
    {
        ScriptableObject authoredProfile = (ScriptableObject)Read(crowbarAsset, "actionProfile");
        longDurationCrowbar = Object.Instantiate(crowbarAsset);
        longDurationCrowbar.name = "Crowbar Stage5 fixture-only duration copy";
        longDurationProfile = Object.Instantiate(authoredProfile);
        longDurationProfile.name = "Crowbar Stage5 fixture-only profile copy";
        FieldInfo duration = longDurationProfile.GetType().GetField("crowbarExtractionDuration", PublicInstance);
        Assert.That(duration, Is.Not.Null);
        duration.SetValue(longDurationProfile, 5f);
        longDurationCrowbar.GetType().GetField("actionProfile", PublicInstance).SetValue(longDurationCrowbar, longDurationProfile);
        ReplaceCrowbarInInventoryCatalog(clientInventory, longDurationCrowbar);
        ReplaceCrowbarInInventoryCatalog(serverActor.GetComponent(inventoryType), longDurationCrowbar);
        ReplaceCrowbarInventorySlot(clientInventory);
    }

    private void ReplaceCrowbarInInventoryCatalog(Component inventory, ScriptableObject replacement)
    {
        FieldInfo catalogField = inventoryType.GetField("equippableItemCatalog", PrivateInstance);
        Assert.That(catalogField, Is.Not.Null);
        Array catalog = (Array)catalogField.GetValue(inventory);
        bool replaced = false;
        for (int i = 0; i < catalog.Length; i++)
            if (catalog.GetValue(i) == crowbarAsset || catalog.GetValue(i) == authoredCrowbarAsset)
            { catalog.SetValue(replacement, i); replaced = true; }
        Assert.That(replaced, Is.True, "The temporary item clone replaces only this fixture inventory catalog entry.");
        catalogField.SetValue(inventory, catalog);
    }

    private void ReplaceCrowbarInventorySlot(Component inventory)
    {
        FieldInfo itemsField = inventoryType.GetField("inventoryItems", PrivateInstance);
        Array items = (Array)itemsField.GetValue(inventory);
        bool replaced = false;
        for (int i = 0; i < items.Length; i++)
            if (items.GetValue(i) == crowbarAsset) { items.SetValue(longDurationCrowbar, i); replaced = true; }
        Assert.That(replaced, Is.True, "The duration copy replaces the existing fixture inventory reference without invoking drop behavior.");
        itemsField.SetValue(inventory, items);
    }

    private void AssertSourceCrowbarRemainedUnchanged()
    {
        ScriptableObject authoredProfile = (ScriptableObject)Read(authoredCrowbarAsset, "actionProfile");
        Assert.That(UnityEditor.EditorJsonUtility.ToJson(authoredCrowbarAsset), Is.EqualTo(originalCrowbarJson));
        Assert.That(HashCrowbarAsset(), Is.EqualTo(originalCrowbarHash));
        Assert.That(HashAsset(authoredProfile), Is.EqualTo(originalProfileHash));
        Assert.That(UnityEditor.EditorJsonUtility.ToJson(authoredProfile), Is.EqualTo(originalProfileJson));
        Assert.That(UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(authoredCrowbarAsset)),
            Is.EqualTo(originalCrowbarGuid));
        Assert.That(UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(authoredProfile)),
            Is.EqualTo(originalProfileGuid));
        Assert.That(Read(fixtureCrowbar, "actionProfile"), Is.SameAs(fixtureProfile));
        Assert.That(ReadFloat(fixtureProfile, "crowbarExtractionDuration"), Is.EqualTo(1.5f).Within(0.001f));
        if (longDurationCrowbar != null)
        {
            Assert.That(authoredProfile, Is.Not.SameAs(longDurationProfile));
            Assert.That(Read(longDurationCrowbar, "actionProfile"), Is.SameAs(longDurationProfile));
            Assert.That(ReadFloat(longDurationProfile, "crowbarExtractionDuration"), Is.EqualTo(5f).Within(0.001f));
        }
    }

    private string HashCrowbarAsset()
    {
        return HashAsset(authoredCrowbarAsset);
    }

    private static string HashAsset(ScriptableObject asset)
    {
        string path = UnityEditor.AssetDatabase.GetAssetPath(asset);
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", string.Empty);
    }

    private void ResetOwnedInputDevices()
    {
        Assert.That(keyboard.enabled, Is.True);
        Assert.That(mouse.enabled, Is.True);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        InputSystem.QueueStateEvent(mouse, new MouseState());
        InputSystem.Update();
    }

    private static bool IsOwnDeviceRegistered(InputDevice device)
    {
        foreach (InputDevice registered in InputSystem.devices)
            if (ReferenceEquals(registered, device)) return true;
        return false;
    }

    private static NetworkObject Find(NetworkManager manager, ulong id)
    {
        return manager != null && manager.SpawnManager != null
            && manager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject value) ? value : null;
    }

    private static void RemoveOwnedDevice(InputDevice device)
    {
        if (device != null && IsOwnDeviceRegistered(device)) InputSystem.RemoveDevice(device);
    }

    private static uint NextOwnerAttempt(Component action)
    {
        uint next = unchecked(Convert.ToUInt32(ReadField(action, "crowbarAttemptSequence")) + 1u);
        return next == 0 ? 1u : next;
    }
    private static string ReadAvailability(Component nail) => Read(Read(nail, "CrowbarExtractionReadState"), "Availability").ToString();
    private static object Read(object value, string property)
    {
        PropertyInfo info = value.GetType().GetProperty(property, PublicInstance);
        return info != null ? info.GetValue(value) : value.GetType().GetField(property, PublicInstance).GetValue(value);
    }
    private static object ReadField(object value, string field) => value.GetType().GetField(field, PrivateInstance).GetValue(value);
    private static float ReadFloat(object value, string property) => Convert.ToSingle(Read(value, property));

    private string DescribeAimValidation()
    {
        Component serverAction = serverActor.GetComponent(actorActionType);
        object[] rayArgs = { default(Ray) };
        bool hasActualAimRay = (bool)actorActionType.GetMethod("TryGetCrowbarExtractionAimRay", PrivateInstance)
            .Invoke(clientAction, rayArgs);
        Ray actualRay = hasActualAimRay ? (Ray)rayArgs[0] : new Ray(anchor.position, aimCamera.transform.forward);
        Vector3 origin = actualRay.origin;
        Vector3 direction = actualRay.direction.normalized;
        bool trusted = (bool)actorActionType.GetMethod("IsTrustedMountingNailAimOrigin", PrivateInstance)
            .Invoke(serverAction, new object[] { origin, direction });
        bool valid = (bool)actorActionType.GetMethod("ValidateServerCrowbarAim", PrivateInstance)
            .Invoke(serverAction, new object[] { serverNail, origin, direction });
        object[] candidateArgs = { new Ray(origin, direction), ReadFloat(crowbarAsset, "actionRange"), serverActor.transform, null, null, Vector3.zero };
        bool resolves = (bool)Type.GetType("MountingNailAimResolver, Assembly-CSharp").GetMethod("TryFindCandidate", BindingFlags.Static | BindingFlags.Public)
            .Invoke(null, candidateArgs);
        Transform serverAnchor = (Transform)Read(serverActor.GetComponent(interactionType), "SingleCarryPlacementAimAnchor");
        Vector3 anchorLocalOffset = serverActor.transform.InverseTransformPoint(serverAnchor.position);
        NetworkTransform serverNetworkTransform = serverActor.GetComponent<NetworkTransform>();
        Vector3 authoritativePosition = serverNetworkTransform.GetSpaceRelativePosition(true);
        Quaternion authoritativeRotation = serverNetworkTransform.GetSpaceRelativeRotation(true);
        Vector3 trustedAnchor = authoritativePosition
            + authoritativeRotation * Vector3.Scale(anchorLocalOffset, serverActor.transform.localScale);
        Vector3 trustedOffset = origin - trustedAnchor;
        float forwardOffset = Vector3.Dot(trustedOffset, direction);
        float allowedForwardOffset = Mathf.Clamp(forwardOffset, 0f, 0.2f);
        float trustedResidual = Mathf.Sqrt((trustedOffset - direction * forwardOffset).sqrMagnitude
            + Mathf.Pow(forwardOffset - allowedForwardOffset, 2f));
        object[] clientCandidateArgs = { new Ray(origin, direction), ReadFloat(crowbarAsset, "actionRange"), clientActor.transform, null, null, Vector3.zero };
        bool clientResolves = (bool)Type.GetType("MountingNailAimResolver, Assembly-CSharp").GetMethod("TryFindCandidate", BindingFlags.Static | BindingFlags.Public)
            .Invoke(null, clientCandidateArgs);
        return $"actualRayAvailable={hasActualAimRay}, origin={origin}, direction={direction}, clientPos={clientActor.transform.position}, " +
               $"serverPos={serverActor.transform.position}, serverAnchor={serverAnchor.position}, trustedAnchor={trustedAnchor}, " +
               $"trustedOffset={trustedOffset}, forwardOffset={forwardOffset}, trustedResidual={trustedResidual}, trusted={trusted}, " +
               $"clientCandidate={clientResolves && clientCandidateArgs[3] == clientNail}, " +
               $"candidate={resolves && candidateArgs[3] == serverNail}, valid={valid}, " +
               $"clientAvailability={ReadAvailability(clientNail)}, serverAvailability={ReadAvailability(serverNail)}, " +
               $"lastRefresh={ReadField(serverAction, "serverCrowbarLastRefreshTime")}, attempt={ReadField(serverAction, "activeCrowbarAttempt")}";
    }
    private static ushort ReservePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0); listener.Start();
        try { return (ushort)((IPEndPoint)listener.LocalEndpoint).Port; } finally { listener.Stop(); }
    }
    private static void SetHash(NetworkObject networkObject, uint hash) => typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, hash);
    private static IEnumerator Until(Func<bool> condition, string what)
    {
        float deadline = Time.realtimeSinceStartup + 12f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, $"Timed out waiting for {what}.");
    }
}
