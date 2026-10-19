using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public sealed class BridgeDeckPanelStage1NetworkPlayModeTests
{
    private GameObject hostRoot;
    private GameObject clientRoot;
    private NetworkManager host;
    private NetworkManager client;
    private NetworkManager priorSingleton;
    private NetworkObject serverPanel;
    private GameObject supportSurface;
    private GameObject blockingWall;
    private Keyboard testKeyboard;
    private GameObject isolatedPlayerPrefabRoot;
    private InputActionAsset isolatedPlayerActions;
    private Stage1ProjectWideInputActionScope projectWideActions;

    [UnitySetUp]
    public IEnumerator SetupInputFixture()
    {
        projectWideActions = new Stage1ProjectWideInputActionScope();
        projectWideActions.CaptureAndDisable();
        yield return null;
    }

    [UnityTest]
    public IEnumerator RemoteOwnerCanPickUpAndThrowTheActualPhysicalDeckPanel()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("This loopback test requires a local process without an active NGO session.");
#if UNITY_EDITOR
        GameObject playerPrefab = CreateIsolatedPlayerPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab"));
        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
#else
        Assert.Ignore("The Stage1 network fixture loads production prefabs through the Editor AssetDatabase.");
        yield break;
#endif
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(panelPrefab, Is.Not.Null);
        priorSingleton = NetworkManager.Singleton;
        supportSurface = CreateFixtureGround("Deck panel loopback ground");
        ushort port = ReservePort();
        host = CreateManager("Deck panel loopback host", playerPrefab, panelPrefab, port, out hostRoot);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return WaitFor(() => host.LocalClient?.PlayerObject != null, "host player spawn");
        AssertSpawnedPlayerUsesIsolatedInputAsset(host.LocalClient.PlayerObject, playerPrefab);
        MovePlayerOutOfTestArea(host.LocalClient.PlayerObject, new Vector3(-5f, 0f, 0f));

        client = CreateManager("Deck panel loopback client", playerPrefab, panelPrefab, port, out clientRoot);
        Assert.That(client.StartClient(), Is.True);
        client.SetSingleton();
        yield return WaitFor(() => client.IsConnectedClient && client.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 2, "remote client connection");
        AssertSpawnedPlayerUsesIsolatedInputAsset(host.ConnectedClients[client.LocalClientId].PlayerObject, playerPrefab);
        AssertSpawnedPlayerUsesIsolatedInputAsset(client.LocalClient.PlayerObject, playerPrefab);
        IgnorePlayerReplicaCollisions(host, client);

        Transform authoritativeRemotePlayer = host.ConnectedClients[client.LocalClientId].PlayerObject.transform;
        host.SetSingleton();
        GameObject panelInstance = Object.Instantiate(panelPrefab,
            authoritativeRemotePlayer.position + authoritativeRemotePlayer.forward * 1.25f, Quaternion.identity);
        serverPanel = panelInstance.GetComponent<NetworkObject>();
        Assert.That(serverPanel, Is.Not.Null);
        serverPanel.Spawn();
        ulong panelId = serverPanel.NetworkObjectId;
        yield return WaitFor(() => Find(client, panelId) != null, "panel replication to client");

        NetworkObject remotePanel = Find(client, panelId);
        client.SetSingleton();
        IgnoreReplicaCollisions(serverPanel, remotePanel);
        System.Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        System.Type mountableType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        Component remoteInteraction = client.LocalClient.PlayerObject.GetComponent(interactionType);
        Component remoteMountable = remotePanel.GetComponent(mountableType);
        Assert.That(remoteInteraction, Is.Not.Null);
        Assert.That((bool)mountableType.GetProperty("CanEnterSingleCarryPlacement").GetValue(remoteMountable), Is.True);
        mountableType.GetMethod("PickedUp").Invoke(remoteMountable, new object[] { client.LocalClient.PlayerObject.transform });
        yield return WaitFor(() => serverPanel.OwnerClientId == client.LocalClientId
            && (bool)mountableType.GetProperty("IsPickedUp").GetValue(serverPanel.GetComponent(mountableType))
            && (bool)mountableType.GetProperty("IsPickedUp").GetValue(remotePanel.GetComponent(mountableType)), "remote pickup ownership and held state");
        Assert.That((GameObject)interactionType.GetMethod("GetPickedUpGameObject").Invoke(remoteInteraction, null),
            Is.SameAs(remotePanel.gameObject), "The owner client binds the authoritative pickup to its own player interaction state.");

        Collider remoteCollider = remotePanel.GetComponent<Collider>();
        Assert.That(remoteCollider.excludeLayers.value, Is.EqualTo(Physics.AllLayers), "The held panel remains queryable while solid contact is isolated.");
        Assert.That(serverPanel.GetComponent<Rigidbody>().isKinematic, Is.True);
        Rigidbody authoritativeBody = serverPanel.GetComponent<Rigidbody>();
        Vector3 throwDirection = host.ConnectedClients[client.LocalClientId].PlayerObject.transform.forward;
        float expectedThrowSpeed = (float)interactionType.GetProperty("SingleCarryDropThrowMetersPerSecond").GetValue(remoteInteraction);
        Transform authoritativeHolder = host.ConnectedClients[client.LocalClientId].PlayerObject.transform;
        Vector3 expectedDropPosition = serverPanel.transform.position;
        Collider[] authoritativeHolderColliders = authoritativeHolder.GetComponentsInChildren<Collider>();
        float maximumHolderPenetration = MeasureMaximumPenetration(remotePanel.GetComponent<Collider>(), expectedDropPosition,
            remotePanel.transform.rotation, authoritativeHolderColliders);
        Transform peerHolder = client.LocalClient.PlayerObject.transform;
        Vector3 peerDropPosition = remotePanel.transform.position;
        float maximumPeerPenetration = MeasureMaximumPenetration(remotePanel.GetComponent<Collider>(), peerDropPosition,
            remotePanel.transform.rotation, peerHolder.GetComponentsInChildren<Collider>());
        Debug.Log($"Deck panel enabled-holder drop overlap: server {maximumHolderPenetration:F4}m at {expectedDropPosition}; peer {maximumPeerPenetration:F4}m at {peerDropPosition}.");
        blockingWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blockingWall.name = "Loopback physical panel throw blocker";
        blockingWall.transform.position = authoritativeHolder.position + authoritativeHolder.forward * 1.5f + Vector3.up * 0.5f;
        blockingWall.transform.rotation = Quaternion.LookRotation(authoritativeHolder.forward);
        blockingWall.transform.localScale = new Vector3(10f, 5f, 0.1f);
        Physics.SyncTransforms();
        Assert.That((bool)interactionType.GetMethod("DropObject").Invoke(remoteInteraction, null), Is.True,
            "The actual owner E/drop path sends a throw request for server validation.");
        yield return null;
        yield return null;
        Assert.That(serverPanel.OwnerClientId, Is.EqualTo(client.LocalClientId), "A blocked request preserves network ownership.");
        Assert.That((bool)mountableType.GetProperty("IsPickedUp").GetValue(serverPanel.GetComponent(mountableType)), Is.True,
            "The server retains held state when no unobstructed forward pose exists.");
        Assert.That((bool)mountableType.GetProperty("IsPickedUp").GetValue(remotePanel.GetComponent(mountableType)), Is.True);
        Assert.That(authoritativeBody.isKinematic && !authoritativeBody.useGravity, Is.True);
        Assert.That(remoteCollider.excludeLayers.value, Is.EqualTo(Physics.AllLayers), "Rejected throws preserve contact isolation.");
        Object.Destroy(blockingWall);
        blockingWall = null;
        yield return null;

        System.Type resolverType = System.Type.GetType("SingleCarryDropPoseResolver, Assembly-CSharp");
        object[] resolveArgs = { serverPanel.gameObject, authoritativeHolder,
            (float)interactionType.GetProperty("SingleCarryDropSearchDistance").GetValue(
                host.ConnectedClients[client.LocalClientId].PlayerObject.GetComponent(interactionType)), Vector3.zero };
        bool serverCanResolve = (bool)resolverType.GetMethod("TryResolvePhysicalPanel").Invoke(null, resolveArgs);
        Debug.Log($"Loopback server panel drop resolve={serverCanResolve}, candidate={resolveArgs[3]}, panelStart={serverPanel.transform.position}, holder={authoritativeHolder.position}, forward={authoritativeHolder.forward}, range={resolveArgs[2]}.");
        string initialOverlaps = DescribePanelOverlaps(serverPanel.GetComponent<BoxCollider>());
        Assert.That(serverCanResolve, Is.True,
            $"After removing the wall, the server physics world has a bounded clear panel pose. Start={serverPanel.transform.position}; holder={authoritativeHolder.position}; overlaps=[{initialOverlaps}].");

        Assert.That((bool)interactionType.GetMethod("DropObject").Invoke(remoteInteraction, null), Is.True,
            "The actual owner E/drop path asks the authoritative server to validate and release the panel.");
        yield return WaitFor(() => !(bool)mountableType.GetProperty("IsPickedUp").GetValue(serverPanel.GetComponent(mountableType))
            && !(bool)mountableType.GetProperty("IsPickedUp").GetValue(remotePanel.GetComponent(mountableType)), "throw release");
        Assert.That((GameObject)interactionType.GetMethod("GetPickedUpGameObject").Invoke(remoteInteraction, null), Is.Null,
            "The instance NetworkManager routes the release confirmation to the owner client's own player interaction state.");
        Vector3 authoritativeReleasePosition = serverPanel.transform.position;
        string releaseOverlaps = DescribePanelOverlaps(serverPanel.GetComponent<BoxCollider>());
        yield return new WaitForFixedUpdate();
        Assert.That(serverPanel.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(serverPanel.GetComponent<Rigidbody>().useGravity, Is.True);
        Assert.That(remoteCollider.excludeLayers.value, Is.Zero, "Throw release restores carried-contact settings on the remote copy.");
        Vector3 authoritativeDeltaV = authoritativeBody.linearVelocity;
        Assert.That(Vector3.Dot(authoritativeDeltaV, throwDirection), Is.EqualTo(expectedThrowSpeed).Within(0.5f),
            $"The host's unignored physical holder and clear environment receive the configured throw after physics. The server resets velocity to zero before applying ForceMode.VelocityChange. Actual server Δv={authoritativeDeltaV}; release pose={authoritativeReleasePosition}; overlaps=[{releaseOverlaps}]; pre-drop overlap diagnostic={maximumHolderPenetration:F4}m.");
        yield return WaitFor(() => Vector3.Distance(serverPanel.transform.position, remotePanel.transform.position) < 0.25f,
            "released pose replication to the remote client");
        Assert.That(Vector3.Distance(serverPanel.transform.position, remotePanel.transform.position), Is.LessThan(0.25f),
            "The client's released pose follows the authoritative host object.");
    }

    [UnityTest]
    public IEnumerator RemoteOwnerCanUseActualFAndEToPlacePhysicalPanelWithoutConsumingIt()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("This loopback test requires a local process without an active NGO session.");
        projectWideActions.UseGameViewInputRouting();
        testKeyboard = InputSystem.AddDevice<Keyboard>();
#if UNITY_EDITOR
        GameObject playerPrefab = CreateIsolatedPlayerPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab"));
        SetFixturePlayerLocomotionEnabled(playerPrefab, false);
        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
#else
        Assert.Ignore("The Stage1 network fixture loads production prefabs through the Editor AssetDatabase.");
        yield break;
#endif
        priorSingleton = NetworkManager.Singleton;
        supportSurface = CreateFixtureGround("Network Stage1 panel support");
        ushort port = ReservePort();
        host = CreateManager("Deck panel placement loopback host", playerPrefab, panelPrefab, port, out hostRoot);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return WaitFor(() => host.LocalClient?.PlayerObject != null, "host player spawn");
        AssertSpawnedPlayerUsesIsolatedInputAsset(host.LocalClient.PlayerObject, playerPrefab);
        RestrictFixturePlayersToSyntheticKeyboard(host, testKeyboard);
        client = CreateManager("Deck panel placement loopback client", playerPrefab, panelPrefab, port, out clientRoot);
        Assert.That(client.StartClient(), Is.True);
        client.SetSingleton();
        yield return WaitFor(() => client.IsConnectedClient && client.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 2, "remote client connection");
        AssertSpawnedPlayerUsesIsolatedInputAsset(host.ConnectedClients[client.LocalClientId].PlayerObject, playerPrefab);
        AssertSpawnedPlayerUsesIsolatedInputAsset(client.LocalClient.PlayerObject, playerPrefab);
        RestrictFixturePlayersToSyntheticKeyboard(host, testKeyboard);
        RestrictFixturePlayersToSyntheticKeyboard(client, testKeyboard);
        IgnorePlayerReplicaCollisions(host, client);
        yield return WaitFor(() => Find(client, host.LocalClient.PlayerObject.NetworkObjectId) != null,
            "host player replica on placement client");
        NetworkObject hostLocalPlayerForPlacement = host.LocalClient.PlayerObject;
        NetworkObject clientLocalPlayerForPlacement = client.LocalClient.PlayerObject;
        NetworkObject clientReplicaOfHostPlayer = Find(client, hostLocalPlayerForPlacement.NetworkObjectId);
        NetworkObject hostReplicaOfClientPlayer = host.ConnectedClients[client.LocalClientId].PlayerObject;
        Assert.That(clientReplicaOfHostPlayer, Is.Not.Null);
        Assert.That(hostReplicaOfClientPlayer, Is.Not.Null);

        int clockAlignedFrames = 0;
        float clockAlignmentDeadline = Time.realtimeSinceStartup + 15f;
        int clockAlignmentErrorTicks = int.MaxValue;
        while (clockAlignedFrames < 3 && Time.realtimeSinceStartup < clockAlignmentDeadline)
        {
            clockAlignmentErrorTicks = Mathf.Abs(client.NetworkTickSystem.ServerTime.Tick - host.NetworkTickSystem.ServerTime.Tick);
            clockAlignedFrames = clockAlignmentErrorTicks <= 2 ? clockAlignedFrames + 1 : 0;
            if (clockAlignedFrames < 3) yield return null;
        }
        Assert.That(clockAlignedFrames, Is.GreaterThanOrEqualTo(3),
            $"Loopback client and host server clocks must align before controlled owner teleports. Residual={clockAlignmentErrorTicks} ticks.");

        host.SetSingleton();
        MovePlayerOutOfTestArea(hostLocalPlayerForPlacement, new Vector3(-5f, 0f, 0f));
        client.SetSingleton();
        MovePlayerOutOfTestArea(clientLocalPlayerForPlacement, Vector3.zero);

        Transform remotePlayer = client.LocalClient.PlayerObject.transform;
        System.Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        GameObject aimObject = new GameObject("Network deck panel test aim camera");
        aimObject.transform.SetParent(remotePlayer, false);
        Transform placementAimAnchor = (Transform)interactionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(
            client.LocalClient.PlayerObject.GetComponent(interactionType));
        aimObject.transform.SetParent(placementAimAnchor, false);
        aimObject.transform.localPosition = Vector3.zero;
        aimObject.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        Camera aimCamera = aimObject.AddComponent<Camera>();
        aimCamera.nearClipPlane = 0.2f;
        Component remoteInteraction = client.LocalClient.PlayerObject.GetComponent(interactionType);
        interactionType.GetField("aimCamera", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(remoteInteraction, aimCamera);
        System.Type previewType = System.Type.GetType("PlayerSingleCarryPlacementNetworkPreview, Assembly-CSharp");
        Component preview = client.LocalClient.PlayerObject.GetComponent(previewType);
        previewType.GetField("placementDistance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(preview, 5f);
        Component authoritativePreview = host.ConnectedClients[client.LocalClientId].PlayerObject.GetComponent(previewType);
        previewType.GetField("placementDistance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(authoritativePreview, 5f);

        Assert.That(clientReplicaOfHostPlayer, Is.Not.Null, "The client must have a loopback replica of the host player for collision-separated setup.");
        int poseStableFrames = 0;
        float controlledPoseDeadline = Time.realtimeSinceStartup + 15f;
        float ownerPoseError = float.PositiveInfinity;
        float hostPoseError = float.PositiveInfinity;
        int controlledPoseClockErrorTicks = int.MaxValue;
        while (poseStableFrames < 3 && Time.realtimeSinceStartup < controlledPoseDeadline)
        {
            controlledPoseClockErrorTicks = Mathf.Abs(client.NetworkTickSystem.ServerTime.Tick - host.NetworkTickSystem.ServerTime.Tick);
            ownerPoseError = Vector3.Distance(clientLocalPlayerForPlacement.transform.position,
                hostReplicaOfClientPlayer.transform.position);
            hostPoseError = Vector3.Distance(hostLocalPlayerForPlacement.transform.position,
                clientReplicaOfHostPlayer.transform.position);
            bool atControlledPositions = Vector3.Distance(clientLocalPlayerForPlacement.transform.position, Vector3.zero) < 0.03f
                && Vector3.Distance(hostLocalPlayerForPlacement.transform.position, new Vector3(-5f, 0f, 0f)) < 0.03f;
            bool synchronized = ownerPoseError < 0.03f && hostPoseError < 0.03f && controlledPoseClockErrorTicks <= 2;
            poseStableFrames = atControlledPositions && synchronized ? poseStableFrames + 1 : 0;
            if (poseStableFrames < 3) yield return null;
        }
        Assert.That(poseStableFrames, Is.GreaterThanOrEqualTo(3),
            $"All loopback player replicas must settle at separated controlled poses before restoring locomotion. "
            + $"Owner pose residual={ownerPoseError:F4}m; host pose residual={hostPoseError:F4}m; clock residual={controlledPoseClockErrorTicks} ticks.");

        Component ownerFirstPersonController = clientLocalPlayerForPlacement.GetComponent(
            System.Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp"));
        Assert.That(ownerFirstPersonController, Is.Not.Null);
        ownerFirstPersonController.GetType().GetMethod("ResetMovementAfterForcedPlacement")
            .Invoke(ownerFirstPersonController, null);
        SetFixturePlayerLocomotionEnabled(clientLocalPlayerForPlacement.gameObject, true);

        Transform serverAimAnchorForInitialization = (Transform)interactionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(
            hostReplicaOfClientPlayer.GetComponent(interactionType));
        Unity.Netcode.Components.NetworkTransform serverTransformForInitialization =
            hostReplicaOfClientPlayer.GetComponent<Unity.Netcode.Components.NetworkTransform>();
        int anchorStableFrames = 0;
        float anchorInitDeadline = Time.realtimeSinceStartup + 15f;
        float initializationAnchorError = float.PositiveInfinity;
        float ownerReplicaErrorAfterEnable = float.PositiveInfinity;
        int anchorClockErrorTicks = int.MaxValue;
        while (anchorStableFrames < 3 && Time.realtimeSinceStartup < anchorInitDeadline)
        {
            Vector3 serverAnchorLocalOffset = hostReplicaOfClientPlayer.transform.InverseTransformPoint(serverAimAnchorForInitialization.position);
            Vector3 trustedServerAnchor = serverTransformForInitialization.GetSpaceRelativePosition(true)
                + serverTransformForInitialization.GetSpaceRelativeRotation(true)
                    * Vector3.Scale(serverAnchorLocalOffset, hostReplicaOfClientPlayer.transform.localScale);
            initializationAnchorError = Vector3.Distance(placementAimAnchor.position, trustedServerAnchor);
            ownerReplicaErrorAfterEnable = Vector3.Distance(clientLocalPlayerForPlacement.transform.position,
                hostReplicaOfClientPlayer.transform.position);
            anchorClockErrorTicks = Mathf.Abs(client.NetworkTickSystem.ServerTime.Tick - host.NetworkTickSystem.ServerTime.Tick);
            bool settled = initializationAnchorError < 0.03f && ownerReplicaErrorAfterEnable < 0.03f
                && anchorClockErrorTicks <= 2;
            anchorStableFrames = settled ? anchorStableFrames + 1 : 0;
            if (anchorStableFrames < 3) yield return null;
        }
        Assert.That(anchorStableFrames, Is.GreaterThanOrEqualTo(3),
            $"Owner locomotion restore must converge against the host's trusted anchor before panel spawn. "
            + $"Anchor residual={initializationAnchorError:F4}m; root residual={ownerReplicaErrorAfterEnable:F4}m; clock residual={anchorClockErrorTicks} ticks.");

        Vector3 supportCenter = supportSurface.transform.position;
        Physics.SyncTransforms();

        GameObject panelInstance = Object.Instantiate(panelPrefab, remotePlayer.position + remotePlayer.forward * 1.25f, Quaternion.identity);
        serverPanel = panelInstance.GetComponent<NetworkObject>();
        host.SetSingleton();
        serverPanel.Spawn();
        ulong panelId = serverPanel.NetworkObjectId;
        yield return WaitFor(() => Find(client, panelId) != null, "panel replication to placement client");
        NetworkObject remotePanel = Find(client, panelId);
        client.SetSingleton();
        IgnoreReplicaCollisions(serverPanel, remotePanel);
        System.Type mountableType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        Component remoteMountable = remotePanel.GetComponent(mountableType);
        remoteMountable.GetType().GetMethod("PickedUp").Invoke(remoteMountable, new object[] { remotePlayer });
        yield return WaitFor(() => serverPanel.OwnerClientId == client.LocalClientId
            && (bool)mountableType.GetProperty("IsPickedUp").GetValue(serverPanel.GetComponent(mountableType)), "remote panel pickup");

        Component serverInteractionBeforePlacement =
            host.ConnectedClients[client.LocalClientId].PlayerObject.GetComponent(interactionType);
        Transform serverAimAnchorBeforePlacement = (Transform)interactionType.GetProperty("SingleCarryPlacementAimAnchor")
            .GetValue(serverInteractionBeforePlacement);
        Unity.Netcode.Components.NetworkTransform serverPlayerTransformBeforePlacement =
            host.ConnectedClients[client.LocalClientId].PlayerObject.GetComponent<Unity.Netcode.Components.NetworkTransform>();
        Transform ownerRootBeforeAnchorWait = remotePlayer;
        Transform serverRootBeforeAnchorWait = host.ConnectedClients[client.LocalClientId].PlayerObject.transform;
        Vector3 ownerRootStartPosition = ownerRootBeforeAnchorWait.position;
        Vector3 serverRootStartPosition = serverRootBeforeAnchorWait.position;
        int stableAnchorFrames = 0;
        float anchorSyncError = float.PositiveInfinity;
        float anchorSyncDeadline = Time.realtimeSinceStartup + 6f;
        while (stableAnchorFrames < 3 && Time.realtimeSinceStartup < anchorSyncDeadline)
        {
            Vector3 serverAnchorLocalOffset = host.ConnectedClients[client.LocalClientId].PlayerObject.transform
                .InverseTransformPoint(serverAimAnchorBeforePlacement.position);
            Vector3 trustedServerAnchor = serverPlayerTransformBeforePlacement.GetSpaceRelativePosition(true)
                + serverPlayerTransformBeforePlacement.GetSpaceRelativeRotation(true)
                    * Vector3.Scale(serverAnchorLocalOffset,
                        host.ConnectedClients[client.LocalClientId].PlayerObject.transform.localScale);
            anchorSyncError = Vector3.Distance(placementAimAnchor.position, trustedServerAnchor);
            stableAnchorFrames = anchorSyncError < 0.03f ? stableAnchorFrames + 1 : 0;
            if (stableAnchorFrames < 3) yield return new WaitForFixedUpdate();
        }
        Assert.That(stableAnchorFrames, Is.GreaterThanOrEqualTo(3),
            $"The owner's real aim anchor must settle against the host's latest authoritative NetworkTransform before submitting F/E. "
            + $"Residual={anchorSyncError:F4}m; ownerRootStart/end={ownerRootStartPosition}/{ownerRootBeforeAnchorWait.position}; "
            + $"serverRootStart/end={serverRootStartPosition}/{serverRootBeforeAnchorWait.position}; "
            + $"owner/server roots now={ownerRootBeforeAnchorWait.position}/{serverRootBeforeAnchorWait.position}; "
            + $"owner/server anchors now={placementAimAnchor.position}/{serverAimAnchorBeforePlacement.position}; "
            + $"trusted host pose={serverPlayerTransformBeforePlacement.GetSpaceRelativePosition(true)} "
            + $"rotation={serverPlayerTransformBeforePlacement.GetSpaceRelativeRotation(true)}; "
            + $"owner/host server ticks={client.NetworkTickSystem.ServerTime.Tick}/{host.NetworkTickSystem.ServerTime.Tick}.");

        System.Type controllerType = System.Type.GetType("PlayerSingleCarryPlacementController, Assembly-CSharp");
        Component controller = client.LocalClient.PlayerObject.GetComponent(controllerType);
        Assert.That(testKeyboard.fKey.isPressed, Is.False, "The fresh loopback test keyboard must begin with F released.");
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.F));
        InputSystem.Update();
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            $"The remote client's production geometry preview finds the world support. Active={controllerType.GetProperty("IsActive").GetValue(controller)}; geometry={controllerType.GetProperty("LastGeometryStatus").GetValue(controller)}; validation={controllerType.GetProperty("LastValidation").GetValue(controller)}; player={remotePlayer.position}; aim={aimObject.transform.position}/{aimObject.transform.forward}; supportTop={supportCenter + Vector3.up * 0.5f}.");
        Assert.That((bool)controllerType.GetProperty("CanConfirmPlacement").GetValue(controller), Is.True,
            $"The owner controller is ready to submit placement. IsActive={controllerType.GetProperty("IsActive").GetValue(controller)}; validation={controllerType.GetProperty("LastValidation").GetValue(controller)}.");
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.E));
        InputSystem.Update();
        float commitDeadline = Time.realtimeSinceStartup + 15f;
        while (((bool)mountableType.GetProperty("IsPickedUp").GetValue(serverPanel.GetComponent(mountableType))
            || (bool)controllerType.GetProperty("IsActive").GetValue(controller))
            && Time.realtimeSinceStartup < commitDeadline) yield return null;
        Component authoritativeInteraction = host.ConnectedClients[client.LocalClientId].PlayerObject.GetComponent(interactionType);
        Transform authoritativeAimAnchor = (Transform)interactionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(authoritativeInteraction);
        Unity.Netcode.Components.NetworkTransform authoritativePlayerTransform =
            host.ConnectedClients[client.LocalClientId].PlayerObject.GetComponent<Unity.Netcode.Components.NetworkTransform>();
        Vector3 anchorLocalOffset = host.ConnectedClients[client.LocalClientId].PlayerObject.transform
            .InverseTransformPoint(authoritativeAimAnchor.position);
        Vector3 trustedAnchorPosition = authoritativePlayerTransform.GetSpaceRelativePosition(true)
            + authoritativePlayerTransform.GetSpaceRelativeRotation(true)
                * Vector3.Scale(anchorLocalOffset, host.ConnectedClients[client.LocalClientId].PlayerObject.transform.localScale);
        object[] aimRayArgs = { default(Ray) };
        interactionType.GetMethod("TryGetAimRay", new[] { typeof(Ray).MakeByRefType() }).Invoke(remoteInteraction, aimRayArgs);
        Ray submittedAimRay = (Ray)aimRayArgs[0];
        Vector3 aimOriginOffset = submittedAimRay.origin - trustedAnchorPosition;
        float aimForwardOffset = Vector3.Dot(aimOriginOffset, submittedAimRay.direction);
        Vector3 aimLateralOffset = aimOriginOffset - submittedAimRay.direction * aimForwardOffset;
        float aimOriginResidual = Mathf.Sqrt(aimLateralOffset.sqrMagnitude
            + Mathf.Pow(aimForwardOffset - Mathf.Clamp(aimForwardOffset, 0f, 0.2f), 2f));
        Assert.That((bool)mountableType.GetProperty("IsPickedUp").GetValue(serverPanel.GetComponent(mountableType))
            || (bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False,
            $"Remote F/E commit completes through the production controller and network request. Active={controllerType.GetProperty("IsActive").GetValue(controller)}; confirmationReady={controllerType.GetProperty("CanConfirmPlacement").GetValue(controller)}; valid={controllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller)}; validation={controllerType.GetProperty("LastValidation").GetValue(controller)}; result={previewType.GetProperty("LastPlacementResult").GetValue(preview)}; requestPending={controllerType.GetProperty("IsRequestPending").GetValue(controller)}; ray={submittedAimRay}; clientAnchor={placementAimAnchor.position}; serverAnchor={authoritativeAimAnchor.position}; trustedAnchor={trustedAnchorPosition}; playerCopies={remotePlayer.position}/{host.ConnectedClients[client.LocalClientId].PlayerObject.transform.position}; originResidual={aimOriginOffset} (forward={aimForwardOffset:F3}, lateral={aimLateralOffset.magnitude:F3}, bounded residual={aimOriginResidual:F3}); supportTop={supportCenter + Vector3.up * 0.5f}.");

        Assert.That(serverPanel.IsSpawned, Is.True, "The physical panel remains the original network object after placement.");
        Assert.That(serverPanel.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(remoteInteraction.GetType().GetMethod("GetPickedUpGameObject").Invoke(remoteInteraction, null), Is.Null);
        Assert.That(serverPanel.transform.position.z - remotePlayer.position.z, Is.GreaterThan(2.5f));
        Assert.That(serverPanel.GetComponent<Collider>().bounds.min.y, Is.EqualTo(supportCenter.y + 0.5f).Within(0.05f));
        Assert.That(serverPanel.GetComponent<Rigidbody>().isKinematic, Is.False);
    }

    [UnityTearDown]
    public IEnumerator ShutdownNetworkFixture()
    {
        var playerCopies = new HashSet<GameObject>();
        bool managersStopped = false;
        string clientShutdownState = "missing";
        string hostShutdownState = "missing";
        try
        {
            CaptureAndDeactivatePlayers(host, playerCopies);
            CaptureAndDeactivatePlayers(client, playerCopies);
            if (serverPanel != null && serverPanel.IsSpawned && host != null && host.IsListening)
                serverPanel.Despawn(true);
            if (client != null && client.IsListening) client.Shutdown(true);
            if (host != null && host.IsListening) host.Shutdown(true);
            float shutdownDeadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < shutdownDeadline
                && ((client != null && (client.IsListening || client.ShutdownInProgress))
                    || (host != null && (host.IsListening || host.ShutdownInProgress))))
            {
                yield return null;
            }
            managersStopped = (client == null || (!client.IsListening && !client.ShutdownInProgress))
                && (host == null || (!host.IsListening && !host.ShutdownInProgress));
            clientShutdownState = client == null ? "missing" : $"listening={client.IsListening},shuttingDown={client.ShutdownInProgress}";
            hostShutdownState = host == null ? "missing" : $"listening={host.IsListening},shuttingDown={host.ShutdownInProgress}";
            if (clientRoot != null) Object.Destroy(clientRoot);
            if (hostRoot != null) Object.Destroy(hostRoot);
            foreach (GameObject playerCopy in playerCopies)
                if (playerCopy != null) Object.Destroy(playerCopy);
            if (serverPanel != null && serverPanel.gameObject != null) Object.Destroy(serverPanel.gameObject);
            if (supportSurface != null) Object.Destroy(supportSurface);
            if (blockingWall != null) Object.Destroy(blockingWall);
            yield return null;
            Assert.That(playerCopies.Count, Is.GreaterThanOrEqualTo(2), "Both loopback managers must have had fixture-owned player copies captured before shutdown.");
            if (isolatedPlayerPrefabRoot != null) Object.Destroy(isolatedPlayerPrefabRoot);
            if (isolatedPlayerActions != null)
            {
                isolatedPlayerActions.Disable();
                Object.Destroy(isolatedPlayerActions);
            }
            yield return null;
            yield return null;
            foreach (GameObject playerCopy in playerCopies)
                Assert.That(playerCopy == null, Is.True, "Every player object created by the Stage1 NGO fixture must be destroyed before restoring test input state.");
            projectWideActions?.DisableForTest();
            if (testKeyboard != null) InputSystem.RemoveDevice(testKeyboard);
            if (priorSingleton != null) priorSingleton.SetSingleton();
            else typeof(NetworkManager).GetMethod("ResetSingleton", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.Invoke(null, null);
            Assert.That(managersStopped, Is.True,
                $"Both loopback NGO managers must finish shutdown before their roots are destroyed or the prior singleton is restored. "
                + $"Client {clientShutdownState}; host {hostShutdownState}.");
        }
        finally
        {
            try
            {
                projectWideActions?.DisableForTest();
                projectWideActions?.RestoreOriginalMapState();
            }
            finally
            {
                projectWideActions?.RestoreInputSettings();
                projectWideActions = null;
            }
        }
        yield return null;
    }

    private static NetworkManager CreateManager(string objectName, GameObject playerPrefab, GameObject panelPrefab,
        ushort port, out GameObject root)
    {
        root = new GameObject(objectName);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = playerPrefab,
            EnableSceneManagement = false
        };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = panelPrefab });
        return manager;
    }

    private static void MovePlayerOutOfTestArea(NetworkObject playerObject, Vector3 position)
    {
        if (playerObject.TryGetComponent(out Unity.Netcode.Components.NetworkTransform networkTransform))
            networkTransform.Teleport(position, playerObject.transform.rotation, playerObject.transform.localScale);
        else
            playerObject.transform.position = position;
    }

    private static void SetFixturePlayerLocomotionEnabled(GameObject playerObject, bool enabled)
    {
        System.Type firstPersonControllerType = System.Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp");
        if (firstPersonControllerType != null && playerObject.GetComponent(firstPersonControllerType) is Behaviour firstPersonController)
            firstPersonController.enabled = enabled;
        if (playerObject.TryGetComponent(out CharacterController characterController))
            characterController.enabled = enabled;
    }

    private static GameObject CreateFixtureGround(string objectName)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = objectName;
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(20f, 1f, 20f);
        return ground;
    }

    private static NetworkObject Find(NetworkManager manager, ulong id) => manager?.SpawnManager?.SpawnedObjects.TryGetValue(id, out NetworkObject value) == true ? value : null;

    private static void IgnoreReplicaCollisions(NetworkObject authoritative, NetworkObject replica)
    {
        if (authoritative == null || replica == null || authoritative.NetworkObjectId != replica.NetworkObjectId) return;
        foreach (Collider source in authoritative.GetComponentsInChildren<Collider>(true))
        foreach (Collider mirror in replica.GetComponentsInChildren<Collider>(true))
            if (source != null && mirror != null) Physics.IgnoreCollision(source, mirror, true);
    }

    private void RestrictFixturePlayersToSyntheticKeyboard(NetworkManager manager, Keyboard keyboard)
    {
        if (manager?.SpawnManager == null) return;
        System.Type inputType = System.Type.GetType("PlayerInputNew, Assembly-CSharp");
        foreach (NetworkObject playerObject in manager.SpawnManager.SpawnedObjectsList)
        {
            if (playerObject == null) continue;
            Component inputNew = playerObject.GetComponent(inputType);
            PlayerInput unityInput = playerObject.GetComponent<PlayerInput>();
            if (inputNew == null || unityInput == null) continue;
            object generatedActions = inputType.GetField("playerGameInputActions", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(inputNew);
            if (generatedActions == null) continue;
            InputActionAsset generatedAsset = (InputActionAsset)generatedActions.GetType().GetProperty("asset").GetValue(generatedActions);
            projectWideActions.RestrictToSyntheticKeyboard(generatedAsset, unityInput, keyboard);
        }
    }

    private static void IgnorePlayerReplicaCollisions(NetworkManager host, NetworkManager client)
    {
        foreach (ulong clientId in host.ConnectedClientsIds)
        {
            NetworkObject serverPlayer = host.ConnectedClients[clientId].PlayerObject;
            NetworkObject clientPlayer = Find(client, serverPlayer.NetworkObjectId);
            if (clientPlayer != null) IgnoreReplicaCollisions(serverPlayer, clientPlayer);
        }
    }

    private static float MeasureMaximumPenetration(Collider panel, Vector3 panelPosition, Quaternion panelRotation, Collider[] holderColliders)
    {
        float maximum = 0f;
        foreach (Collider holder in holderColliders)
        {
            if (holder == null || !holder.enabled || holder.isTrigger) continue;
            if (Physics.ComputePenetration(panel, panelPosition, panelRotation,
                    holder, holder.transform.position, holder.transform.rotation, out _, out float distance))
                maximum = Mathf.Max(maximum, distance);
        }
        return maximum;
    }

    private static string DescribePanelOverlaps(BoxCollider box)
    {
        Vector3 center = box.transform.TransformPoint(box.center);
        Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, box.transform.lossyScale);
        Collider[] overlaps = Physics.OverlapBox(center, halfExtents, box.transform.rotation,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        return string.Join(", ", System.Array.ConvertAll(overlaps, overlap =>
            overlap == null ? "<null>" : $"{overlap.name}@{overlap.transform.position}"));
    }

    private static IEnumerator WaitFor(System.Func<bool> condition, string operation)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out while waiting for " + operation + ".");
    }

    private static ushort ReservePort()
    {
        UdpClient listener = new UdpClient(0);
        try
        {
            return (ushort)((IPEndPoint)listener.Client.LocalEndPoint).Port;
        }
        finally { listener.Close(); }
    }

    private static void CaptureAndDeactivatePlayers(NetworkManager manager, HashSet<GameObject> playerCopies)
    {
        if (manager == null || manager.SpawnManager == null) return;
        foreach (NetworkObject player in manager.SpawnManager.SpawnedObjectsList)
        {
            if (player == null || !player.IsPlayerObject) continue;
            playerCopies.Add(player.gameObject);
            System.Type playerInputNewType = System.Type.GetType("PlayerInputNew, Assembly-CSharp");
            Component inputNew = playerInputNewType != null
                ? player.GetComponentInChildren(playerInputNewType, true) : null;
            if (inputNew != null)
                playerInputNewType.GetMethod("DisposeInput", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(inputNew, null);
            foreach (PlayerInput input in player.GetComponentsInChildren<PlayerInput>(true))
            {
                if (input == null) continue;
                if (input.inputIsActive) input.DeactivateInput();
                input.actions.Disable();
            }
        }
    }

    private static void AssertSpawnedPlayerUsesIsolatedInputAsset(NetworkObject player, GameObject isolatedPrefab)
    {
        Assert.That(player, Is.Not.Null);
        PlayerInput runtimeInput = player.GetComponent<PlayerInput>();
        PlayerInput prefabInput = isolatedPrefab.GetComponent<PlayerInput>();
        FieldInfo actionsField = typeof(PlayerInput).GetField("m_Actions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(runtimeInput, Is.Not.Null);
        Assert.That(prefabInput, Is.Not.Null);
        Assert.That(actionsField, Is.Not.Null);
        InputActionAsset productionActions = (InputActionAsset)actionsField.GetValue(
            UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab").GetComponent<PlayerInput>());
        Assert.That(runtimeInput.actions, Is.Not.Null);
        Assert.That(UnityEditor.EditorUtility.IsPersistent(runtimeInput.actions), Is.False,
            "An actually spawned NGO player must use a runtime InputActionAsset instance.");
        Assert.That(runtimeInput.actions, Is.Not.SameAs(productionActions),
            $"Spawned player {player.name} (networkId={player.NetworkObjectId}, manager={player.NetworkManager?.name}) must not retain the production PlayerNew action asset.");
    }

    private GameObject CreateIsolatedPlayerPrefab(GameObject productionPrefab)
    {
        Assert.That(productionPrefab, Is.Not.Null);
        PlayerInput sourceInput = productionPrefab.GetComponent<PlayerInput>();
        FieldInfo actionsField = typeof(PlayerInput).GetField("m_Actions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(sourceInput, Is.Not.Null);
        Assert.That(actionsField, Is.Not.Null);
        InputActionAsset productionActions = (InputActionAsset)actionsField.GetValue(sourceInput);
        Assert.That(productionActions, Is.Not.Null);

        isolatedPlayerActions = Object.Instantiate(productionActions);
        isolatedPlayerPrefabRoot = new GameObject("Stage1 isolated production PlayerNew prefab holder");
        isolatedPlayerPrefabRoot.SetActive(false);
        GameObject isolatedPrefab = Object.Instantiate(productionPrefab, isolatedPlayerPrefabRoot.transform);
        PlayerInput isolatedInput = isolatedPrefab.GetComponent<PlayerInput>();
        Assert.That(isolatedInput, Is.Not.Null);
        actionsField.SetValue(isolatedInput, isolatedPlayerActions);
        Assert.That(isolatedInput.actions, Is.SameAs(isolatedPlayerActions));
        NetworkBehaviour[] sourceBehaviours = productionPrefab.GetComponents<NetworkBehaviour>();
        NetworkBehaviour[] isolatedBehaviours = isolatedPrefab.GetComponents<NetworkBehaviour>();
        Assert.That(isolatedBehaviours.Length, Is.EqualTo(sourceBehaviours.Length),
            "The test-only input isolation clone preserves the production NetworkBehaviour count.");
        for (int i = 0; i < sourceBehaviours.Length; i++)
            Assert.That(isolatedBehaviours[i].GetType(), Is.EqualTo(sourceBehaviours[i].GetType()),
                "The test-only input isolation clone preserves the production NetworkBehaviour component order.");
        Assert.That(InputActionsAreStructurallyEquivalent(productionActions, isolatedPlayerActions), Is.True,
            "The fixture uses an isolated copy of the production PlayerInput action asset with the same maps/actions.");
        return isolatedPrefab;
    }

    private static bool InputActionsAreStructurallyEquivalent(InputActionAsset source, InputActionAsset copy)
    {
        if (source.actionMaps.Count != copy.actionMaps.Count) return false;
        for (int mapIndex = 0; mapIndex < source.actionMaps.Count; mapIndex++)
        {
            InputActionMap sourceMap = source.actionMaps[mapIndex];
            InputActionMap copyMap = copy.actionMaps[mapIndex];
            if (sourceMap.name != copyMap.name || sourceMap.actions.Count != copyMap.actions.Count) return false;
            for (int actionIndex = 0; actionIndex < sourceMap.actions.Count; actionIndex++)
                if (sourceMap.actions[actionIndex].name != copyMap.actions[actionIndex].name) return false;
        }
        return true;
    }

}
