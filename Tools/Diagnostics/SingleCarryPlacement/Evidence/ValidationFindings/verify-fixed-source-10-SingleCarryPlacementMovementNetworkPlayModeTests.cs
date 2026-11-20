using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public sealed partial class SingleCarryPlacementMovementNetworkPlayModeTests
{
    private static readonly Type InputNewType = Type.GetType("PlayerInputNew, Assembly-CSharp");
    private static readonly Type InteractionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
    private static readonly Type ControllerType = Type.GetType("PlayerSingleCarryPlacementController, Assembly-CSharp");
    private static readonly Type PreviewType = Type.GetType("PlayerSingleCarryPlacementNetworkPreview, Assembly-CSharp");
    private static readonly Type ResourceType = Type.GetType("BaseResourceNew, Assembly-CSharp");
    private static readonly Type FirstPersonType = Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp");
    private static readonly Type PickableType = Type.GetType("IPIckableNew, Assembly-CSharp");
    private readonly List<NetworkManager> managers = new List<NetworkManager>();
    private readonly List<GameObject> roots = new List<GameObject>();
    private readonly List<GameObject> objects = new List<GameObject>();
    private readonly List<NetworkObject> spawned = new List<NetworkObject>();
    private Stage1ProjectWideInputActionScope inputScope;
    private InputActionAsset isolatedActions;
    private GameObject isolatedPrefabRoot;
    private Keyboard keyboard;
    private Mouse mouse;
    private NetworkManager previousSingleton;
    private bool capturedSingleton;
    private Component simulator;
    private GameObject fixtureAimCameraObject;
    private Camera fixtureAimCamera;
    private GameObject boardPrefab;
    private bool usesPhysicalDeckPanel;
    partial void AppendCheckpoint(string checkpoint);
    partial void WritePanelColliderSnapshot(string stage, int delayMs, Component controller);
    partial void AddDiagnostic(string kind, int delay, int jitter, int loss, string result, Vector3 preview, Vector3 authoritative,
        float elapsedMs, string requestId, Vector3 movedOwner = default, Vector3 pendingOwner = default,
        float pendingDrift = 0f, float resumeDisplacement = 0f, object networkAim = null,
        string serverRpcAimDiagnostic = null);
    partial void BeginTrace(NetworkManager ownerManager, NetworkObject ownerPlayer, NetworkManager serverManager,
        NetworkObject serverPlayer, int delayMs, string caseName);
    partial void ExportAndValidateTrace(ulong requestId, string result, double responseMilliseconds,
        string expectedBranch = null, bool requireMovement = true, bool isHost = false);
    partial void ResetTrace();
    partial void ExportFallbackTrace();
    partial void SetTaskDiagnosticPath(string caseName);
    partial void AssertServerAimBranch(string json, string expectedBranch, ulong requestId, string expectedResult);
    partial void CaptureNetworkAimCheckpoint(NetworkObject ownerPlayer, NetworkObject serverOwnerPlayer, Component controller, int delayMs);
    partial void InitializeDiagnosticOutputs();

    private static void SetInputDevices(NetworkObject player, params InputDevice[] devices)
    {
        PlayerInput playerInput = player.GetComponent<PlayerInput>();
        Assert.That(playerInput, Is.Not.Null);
        InputActionAsset playerInputAsset = playerInput.actions;
        playerInputAsset.devices = devices;
        playerInput.actions.devices = devices;
        object generatedActions = InputNewType.GetField("playerGameInputActions", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(player.GetComponent(InputNewType));
        Assert.That(generatedActions, Is.Not.Null, "The real PlayerInputNew gameplay action wrapper must be initialized.");
        InputActionAsset gameplayAsset = (InputActionAsset)generatedActions.GetType().GetProperty("asset").GetValue(generatedActions);
        gameplayAsset.devices = devices;
        Assert.That(playerInputAsset.devices.HasValue && playerInputAsset.devices.Value.Count == devices.Length, Is.True,
            "The turning test must route both synthetic keyboard and mouse to the real PlayerInput actions.");
        Assert.That(gameplayAsset.devices.HasValue && gameplayAsset.devices.Value.Count == devices.Length, Is.True,
            "The actual PlayerInputNew Look/Move action map must receive both synthetic devices.");
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        InitializeDiagnosticOutputs();
        AppendCheckpoint("SetUp:start");
        managers.Clear(); roots.Clear(); objects.Clear(); spawned.Clear();
        simulator = null; boardPrefab = null; isolatedActions = null; isolatedPrefabRoot = null;
        ResetTrace();
        fixtureAimCameraObject = null; fixtureAimCamera = null;
        previousSingleton = null; capturedSingleton = false;
        inputScope = new Stage1ProjectWideInputActionScope();
        inputScope.CaptureAndDisable();
        inputScope.UseGameViewInputRouting();
        keyboard = InputSystem.AddDevice<Keyboard>();
        yield return null;
        AppendCheckpoint("SetUp:complete");
    }

    [UnityTest]
    public IEnumerator HostOwnerPlacementAcceptsSynchronouslyWithRealPlayer()
    {
        AppendCheckpoint("HostTest:start");
        yield return BuildLoopback(includeRemoteOwner: false);
        AppendCheckpoint("HostTest:loopback-ready");
        NetworkManager host = managers[0];
        NetworkObject hostPlayer = host.LocalClient.PlayerObject;
        DisableAllExcept(host, hostPlayer);
        QueueKeys();
        AppendCheckpoint($"HostTest:spawn-before-stability position={hostPlayer.transform.position.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} nearby={DescribeNearbyColliders(hostPlayer.transform)}");
        yield return WaitForStableSpawn(hostPlayer, "host player grounded spawn setup");
        yield return ConfigureAimCamera(hostPlayer, 45f);
        AppendCheckpoint("HostTest:camera-ready");
        NetworkObject source = SpawnBoardFor(host, new Vector3(20f, 4f, 20f));
        yield return WaitUntil(() => source.IsSpawned, "host source spawn");
        AppendCheckpoint("HostTest:source-spawned");
        host.SetSingleton();
        Assert.That(InvokePickup(source, host.LocalClientId), Is.True);
        ConfirmLocalPickup(host, source.NetworkObjectId, CarrySpeedPenalty(source));
        yield return WaitUntil(() => source.OwnerClientId == host.LocalClientId, "host source ownership");
        AssertSingleCarryState(host, source);
        DisableNonOwnerSourceMirrors(host, managers.ToArray(), source.NetworkObjectId);
        AppendCheckpoint("HostTest:pickup-ready");

        Component controller = hostPlayer.GetComponent(ControllerType);
        Component preview = hostPlayer.GetComponent(PreviewType);
        BeginTrace(host, hostPlayer, host, hostPlayer, 0, "host-synchronous-acceptance");
        QueueKeys(Key.F);
        yield return WaitUntil(() => (bool)ControllerType.GetProperty("IsActive").GetValue(controller), "host F mode entry");
        AppendCheckpoint("HostTest:F-active");
        Transform ghost = Ghost(controller);
        yield return null;
        Assert.That((bool)FirstPersonType.GetField("Grounded").GetValue(hostPlayer.GetComponent(FirstPersonType)), Is.True,
            "The host PlayerNew CharacterController must be grounded before movement measurement.");
        Vector3 beforeWalk = hostPlayer.transform.position;
        Vector3 expectedForward = HorizontalForward(hostPlayer.transform.forward);
        QueueKeys(Key.F, Key.W);
        yield return WaitForForwardMotion(hostPlayer.transform, beforeWalk, expectedForward, 0.15f, "host CharacterController forward movement in F");
        AppendCheckpoint("HostTest:moving-in-F");
        Vector3 beforeRequest = hostPlayer.transform.position;
        AppendCheckpoint($"HostTest:movement before={beforeWalk.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} after={beforeRequest.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
        Vector3 pose = ghost.position;
        Quaternion rotation = ghost.rotation;
        object validation = ControllerType.GetProperty("LastValidation").GetValue(controller);
        string validationStatus = validation.GetType().GetProperty("Status").GetValue(validation).ToString();
        Collider blockingCollider = (Collider)validation.GetType().GetProperty("BlockingCollider").GetValue(validation);
        float penetrationDepth = (float)validation.GetType().GetProperty("PenetrationDepth").GetValue(validation);
        string blockingColliderSummary = "none";
        if (blockingCollider != null)
        {
            NetworkObject blockingNetworkObject = blockingCollider.GetComponentInParent<NetworkObject>();
            blockingColliderSummary = $"{blockingCollider.transform.root.name}/{blockingCollider.name}:{blockingCollider.GetType().Name}:manager={blockingNetworkObject?.NetworkManager?.name ?? "none"}";
        }
        AppendCheckpoint($"HostTest:preview valid={ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller)} geometry={ControllerType.GetProperty("LastGeometryStatus").GetValue(controller)} validation={validationStatus} blocker={blockingColliderSummary} penetration={penetrationDepth.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} ghost={pose.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} player={hostPlayer.transform.position.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} cameraPosition={fixtureAimCamera.transform.position.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} cameraForward={fixtureAimCamera.transform.forward.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} distance={ControllerType.GetProperty("PlacementDistance").GetValue(controller)}");
        Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "Host fixture placement candidate must be locally valid before E.");
        Assert.That(ControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(), Is.EqualTo("SurfaceSupported"),
            "Host acceptance candidate must be supported by the fixture floor.");
        ulong priorRequestId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
        bool confirmationCallbackReached = false;
        ulong completedRequestIdInCallback = 0;
        ulong pendingRequestIdInCallback = 0;
        bool pendingInCallback = false;
        bool movementBlockedInCallback = false;
        string resultInCallback = string.Empty;
        Vector3 horizontalVelocityInCallback = Vector3.zero;
        EventHandler confirmationSnapshot = (_, _) =>
        {
            confirmationCallbackReached = true;
            completedRequestIdInCallback = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
            pendingRequestIdInCallback = (ulong)ControllerType.GetField("pendingRequestId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            pendingInCallback = (bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller);
            movementBlockedInCallback = (bool)InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked")
                .GetValue(hostPlayer.GetComponent(InputNewType));
            resultInCallback = PreviewType.GetProperty("LastPlacementResult").GetValue(preview).ToString();
            horizontalVelocityInCallback = (Vector3)FirstPersonType.GetProperty("HorizontalVelocity")
                .GetValue(hostPlayer.GetComponent(FirstPersonType));
        };
        EventInfo confirmEvent = InputNewType.GetEvent("OnConfirmSingleCarryPlacement");
        Assert.That(confirmEvent, Is.Not.Null);
        confirmEvent.AddEventHandler(hostPlayer.GetComponent(InputNewType), confirmationSnapshot);
        var hostResponseWatch = System.Diagnostics.Stopwatch.StartNew();
        QueueKeys(Key.F, Key.W, Key.E);
        hostResponseWatch.Stop();
        confirmEvent.RemoveEventHandler(hostPlayer.GetComponent(InputNewType), confirmationSnapshot);
        ulong completedRequestId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
        string result = PreviewType.GetProperty("LastPlacementResult").GetValue(preview).ToString();
        ExportAndValidateTrace(completedRequestId, result, hostResponseWatch.Elapsed.TotalMilliseconds,
            result == "Accepted" ? "AimPassed" : null, isHost: true);
        ulong pendingRequestId = (ulong)ControllerType.GetField("pendingRequestId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        bool isPendingImmediately = (bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller);
        object validationAfterE = ControllerType.GetProperty("LastValidation").GetValue(controller);
        string validationAfterEStatus = validationAfterE.GetType().GetProperty("Status").GetValue(validationAfterE).ToString();
        Collider blockingAfterE = (Collider)validationAfterE.GetType().GetProperty("BlockingCollider").GetValue(validationAfterE);
        string blockerAfterE = blockingAfterE == null ? "none"
            : $"{blockingAfterE.transform.root.name}/{blockingAfterE.name}:{blockingAfterE.GetType().Name}";
        Ray cachedAimRay = (Ray)ControllerType.GetField("lastRenderedAimRay", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        bool previewIsServer = (bool)PreviewType.GetProperty("IsServer").GetValue(preview);
        bool previewIsOwner = (bool)PreviewType.GetProperty("IsOwner").GetValue(preview);
        NetworkManager previewManager = (NetworkManager)PreviewType.GetProperty("NetworkManager").GetValue(preview);
        Component interaction = hostPlayer.GetComponent(InteractionType);
        Transform aimAnchor = (Transform)InteractionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(interaction);
        Component playerNetworkTransform = hostPlayer.GetComponent<NetworkTransform>();
        Assert.That(aimAnchor, Is.Not.Null);
        Assert.That(playerNetworkTransform, Is.Not.Null);
        bool networkTransformSpawned = (bool)playerNetworkTransform.GetType().GetProperty("IsSpawned").GetValue(playerNetworkTransform);
        bool canCommitToTransform = (bool)playerNetworkTransform.GetType().GetProperty("CanCommitToTransform").GetValue(playerNetworkTransform);
        Vector3 authoritativePlayerPosition = (Vector3)playerNetworkTransform.GetType()
            .GetMethod("GetSpaceRelativePosition").Invoke(playerNetworkTransform, new object[] { true });
        Quaternion authoritativePlayerRotation = (Quaternion)playerNetworkTransform.GetType()
            .GetMethod("GetSpaceRelativeRotation").Invoke(playerNetworkTransform, new object[] { true });
        Vector3 anchorLocalOffset = hostPlayer.transform.InverseTransformPoint(aimAnchor.position);
        Vector3 trustedAimAnchor = authoritativePlayerPosition
            + authoritativePlayerRotation * Vector3.Scale(anchorLocalOffset, hostPlayer.transform.localScale);
        PropertyInfo localSpaceProperty = playerNetworkTransform.GetType().GetProperty("InLocalSpace");
        FieldInfo localSpaceField = playerNetworkTransform.GetType().GetField("InLocalSpace");
        bool isInLocalSpace = localSpaceProperty != null
            ? (bool)localSpaceProperty.GetValue(playerNetworkTransform)
            : localSpaceField != null && (bool)localSpaceField.GetValue(playerNetworkTransform);
        if (isInLocalSpace && hostPlayer.transform.parent != null)
        {
            trustedAimAnchor = hostPlayer.transform.parent.TransformPoint(authoritativePlayerPosition)
                + hostPlayer.transform.parent.rotation * authoritativePlayerRotation
                * Vector3.Scale(anchorLocalOffset, hostPlayer.transform.localScale);
        }
        Vector3 offsetFromTrustedAnchor = cachedAimRay.origin - trustedAimAnchor;
        float aimForwardOffset = Vector3.Dot(offsetFromTrustedAnchor, cachedAimRay.direction);
        Vector3 aimLateralOffset = offsetFromTrustedAnchor - cachedAimRay.direction * aimForwardOffset;
        float aimResidual = Vector3.Magnitude(aimLateralOffset
            + cachedAimRay.direction * (aimForwardOffset - Mathf.Clamp(aimForwardOffset, 0f, 0.2f)));
        Camera configuredAimCamera = (Camera)InteractionType.GetField("aimCamera", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(interaction);
        Transform cameraParent = fixtureAimCamera.transform.parent;
        AppendCheckpoint($"HostTest:after-E callback={confirmationCallbackReached} completedBefore={priorRequestId} completedInCallback={completedRequestIdInCallback} completedAfter={completedRequestId} pendingInCallback={pendingInCallback} pendingAfter={isPendingImmediately} pendingRequestInCallback={pendingRequestIdInCallback} pendingRequestAfter={pendingRequestId} resultInCallback={resultInCallback} resultAfter={PreviewType.GetProperty("LastPlacementResult").GetValue(preview)} callbackVelocity={horizontalVelocityInCallback} validation={validationAfterEStatus} blocker={blockerAfterE} rayOrigin={cachedAimRay.origin.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} rayDirection={cachedAimRay.direction.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} rayLength={cachedAimRay.direction.magnitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} anchor={aimAnchor.position.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} anchorLocalOffset={anchorLocalOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} camera={fixtureAimCamera.transform.position.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} cameraParent={cameraParent?.name ?? "none"} cameraLocalPosition={fixtureAimCamera.transform.localPosition.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} cameraLocalRotation={fixtureAimCamera.transform.localRotation.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} configuredCameraMatches={configuredAimCamera == fixtureAimCamera} networkTransformType={playerNetworkTransform.GetType().FullName} networkTransformSpawned={networkTransformSpawned} canCommit={canCommitToTransform} netPosition={authoritativePlayerPosition.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} netRotation={authoritativePlayerRotation.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} rootScale={hostPlayer.transform.localScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} trustedAnchor={trustedAimAnchor.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} aimForwardOffset={aimForwardOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} aimResidual={aimResidual.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} IsServer={previewIsServer} IsOwner={previewIsOwner} manager={previewManager?.name ?? "none"}");
        Assert.That(confirmationCallbackReached, Is.True, "Synthetic E must dispatch the registered confirmation action.");
        Assert.That(completedRequestIdInCallback, Is.GreaterThan(priorRequestId), "Host request must complete synchronously inside the confirmation callback.");
        Assert.That(pendingInCallback, Is.False, "Host response completes synchronously in the confirmation callback.");
        Assert.That(resultInCallback, Is.EqualTo("Accepted"), "A locally valid host placement should be accepted synchronously.");
        Assert.That(horizontalVelocityInCallback.magnitude, Is.LessThan(0.001f), "Host E immediately zeros controlled horizontal velocity.");
        Assert.That(completedRequestId, Is.GreaterThan(priorRequestId), "Host placement result must be reported synchronously in the E input callback.");
        Assert.That(isPendingImmediately, Is.False);
        Assert.That(PreviewType.GetProperty("LastPlacementResult").GetValue(preview).ToString(), Is.EqualTo("Accepted"),
            "A locally valid host placement should be accepted synchronously.");
        Vector3 stoppedVelocity = (Vector3)FirstPersonType.GetProperty("HorizontalVelocity").GetValue(hostPlayer.GetComponent(FirstPersonType));
        Assert.That(stoppedVelocity.magnitude, Is.LessThan(0.001f), "Host E immediately zeros player-controlled horizontal velocity before request delivery.");
        Assert.That((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False);
        Assert.That((bool)ControllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        Assert.That(Vector3.Distance(source.transform.position, pose), Is.LessThan(0.003f));
        Assert.That(Quaternion.Angle(source.transform.rotation, rotation), Is.LessThan(0.1f));
        yield return WaitForForwardMotion(hostPlayer.transform, beforeRequest, expectedForward, 0.05f, "ordinary held-W movement after synchronous F exit");
        AddDiagnostic("host-sync", 0, 0, 0, result, pose, source.transform.position, 0f,
            ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview)).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [UnityTest]
    public IEnumerator RemoteOwnerMovesAndConfirmsWithNoAddedDelay()
    {
        yield return RunRemoteOwnerMovingTrial(0);
    }

    [UnityTest]
    public IEnumerator RemoteOwnerMovesAndConfirmsWithHundredMillisecondDelay()
    {
        yield return RunRemoteOwnerMovingTrial(100);
    }

    private IEnumerator RunRemoteOwnerMovingTrial(int delayMs)
    {
        yield return BuildLoopback();
        NetworkManager host = managers[0];
        NetworkManager owner = managers[1];
        NetworkObject ownerPlayer = owner.LocalClient.PlayerObject;
        NetworkObject serverOwnerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        DisableAllExcept(host, serverOwnerPlayer);
        DisableAllExcept(owner, ownerPlayer);
        DisableServerMirrorLocomotion(serverOwnerPlayer);
        IgnoreReplicaCollisions(host, owner);
        EnableControlledPlayerMotion(ownerPlayer);
        AppendCheckpoint($"RemoteTest:spawn-before-stability position={ownerPlayer.transform.position.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} nearby={DescribeNearbyColliders(ownerPlayer.transform)} delay={delayMs}ms");
        yield return WaitForStableSpawn(ownerPlayer, $"remote owner grounded spawn setup at {delayMs}ms");
        yield return ConfigureAimCamera(ownerPlayer, 45f);

        Component controller = ownerPlayer.GetComponent(ControllerType);
        Component preview = ownerPlayer.GetComponent(PreviewType);

        simulator = AttachNetworkSimulator();
        Assert.That(simulator, Is.Not.Null, "The installed Multiplayer Tools NetworkSimulator runtime must be available.");
        Assert.That((bool)simulator.GetType().GetProperty("IsAvailable").GetValue(simulator), Is.True,
            "NetworkSimulator must report a live UTP transport adapter.");

        SetSimulatorPreset(simulator, delayMs);
        yield return null;
        Assert.That((bool)simulator.GetType().GetProperty("IsConnected").GetValue(simulator), Is.True,
            "The configured simulator is attached to a connected loopback transport.");
        NetworkObject source = SpawnBoardFor(host, new Vector3(20f, 4f, 20f));
            yield return WaitUntil(() => Find(owner, source.NetworkObjectId) != null, $"resource replication at {delayMs}ms");
            host.SetSingleton();
            Assert.That(InvokePickup(source, owner.LocalClientId), Is.True, "Server fixture must establish a single-carry owner before testing.");
            float carryPenalty = CarrySpeedPenalty(source);
            owner.SetSingleton();
            ConfirmLocalPickup(owner, source.NetworkObjectId, carryPenalty);
            yield return WaitUntil(() => source.OwnerClientId == owner.LocalClientId, $"remote source ownership at {delayMs}ms");
            AssertSingleCarryState(owner, Find(owner, source.NetworkObjectId));
            DisableSourceMirrorCollisions(host, owner, ownerPlayer, source.NetworkObjectId);
            AssertPlacementShapePresent(Find(owner, source.NetworkObjectId), Find(host, source.NetworkObjectId));
            yield return new WaitForSecondsRealtime(0.35f);
        BeginTrace(owner, ownerPlayer, host, serverOwnerPlayer, delayMs, "remote-moving-acceptance");
            QueueKeys(Key.F);
            yield return WaitUntil(() => (bool)ControllerType.GetProperty("IsActive").GetValue(controller), $"remote F entry at {delayMs}ms");
            Transform ghost = Ghost(controller);
            yield return null;
            Assert.That((bool)FirstPersonType.GetField("Grounded").GetValue(ownerPlayer.GetComponent(FirstPersonType)), Is.True,
                "The remote owner's CharacterController must be grounded before movement measurement.");
            Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
                $"Candidate must be valid before owner movement begins at {delayMs}ms delay.");

            Vector3 beforeMoving = ownerPlayer.transform.position;
            Vector3 expectedForward = HorizontalForward(ownerPlayer.transform.forward);
            QueueKeys(Key.F, Key.W);
            yield return WaitForForwardMotion(ownerPlayer.transform, beforeMoving, expectedForward, 0.35f, $"real CharacterController forward motion at {delayMs}ms");
            Vector3 movedPose = ownerPlayer.transform.position;
            Vector3 capturedGhostPose = ghost.position;
            Quaternion capturedGhostRotation = ghost.rotation;
            Assert.That(HorizontalDistance(movedPose, beforeMoving), Is.GreaterThan(0.15f),
                "WASD must produce actual owner CharacterController displacement before E.");
            Assert.That(Vector3.Dot(movedPose - beforeMoving, expectedForward), Is.GreaterThan(0.15f),
                "W must move the actual remote owner forward before E.");
            Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
                $"Moving owner's preview must still be locally valid immediately before E at {delayMs}ms.");
            Assert.That(ControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(), Is.EqualTo("SurfaceSupported"),
                "Moving-owner acceptance trial must use a supported floor pose within the configured network distance.");
            object networkAim = null;
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
            CaptureNetworkAimCheckpoint(ownerPlayer, serverOwnerPlayer, controller, delayMs);
            networkAim = GetLastNetworkAimDiagnostic();
#endif

            var watch = System.Diagnostics.Stopwatch.StartNew();
            ulong previousId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
            QueueKeys(Key.F, Key.W, Key.E);
            Assert.That((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True,
                "Remote owner request must enter pending synchronously before its server response.");
            Assert.That((bool)InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType)), Is.True,
                "Pending remote request blocks movement input immediately.");
            Vector3 atPending = ownerPlayer.transform.position;
            Vector3 pendingVelocity = (Vector3)FirstPersonType.GetProperty("HorizontalVelocity").GetValue(ownerPlayer.GetComponent(FirstPersonType));
            Assert.That(pendingVelocity.magnitude, Is.LessThan(0.001f), "Pending entry immediately zeros player-controlled horizontal velocity.");
            float maximumPendingDrift = 0f;
            int pendingSampleFrames = 0;
            float responseDeadline = Time.realtimeSinceStartup + 15f;
            while ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview) <= previousId
                && Time.realtimeSinceStartup < responseDeadline)
            {
                yield return null;
                if ((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller))
                {
                    pendingSampleFrames++;
                    maximumPendingDrift = Mathf.Max(maximumPendingDrift, HorizontalDistance(atPending, ownerPlayer.transform.position));
                }
            }
            Assert.That((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview), Is.GreaterThan(previousId),
                $"Timed out waiting for remote placement response at {delayMs}ms delay.");
            watch.Stop();
            double measuredMs = watch.Elapsed.TotalMilliseconds;
            string result = PreviewType.GetProperty("LastPlacementResult").GetValue(preview).ToString();
            ulong completedRequestId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
            ExportAndValidateTrace(completedRequestId, result, measuredMs, result == "Accepted" ? "AimPassed" : null);
            string serverRpcAimDiagnostic = null;
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
            serverRpcAimDiagnostic = CaptureServerRpcAimDiagnostic(serverOwnerPlayer);
#endif
            AddDiagnostic("remote-moving-e", delayMs, 0, 0, result, capturedGhostPose, source.transform.position,
                (float)measuredMs, completedRequestId.ToString(System.Globalization.CultureInfo.InvariantCulture), movedPose, atPending,
                maximumPendingDrift, 0f, networkAim, serverRpcAimDiagnostic);
            if (result == "Accepted")
                AssertServerAimBranch(serverRpcAimDiagnostic, "AimPassed", completedRequestId, result);
            Assert.That(measuredMs, Is.GreaterThanOrEqualTo(delayMs == 0 ? 0d : 100d),
                $"Measured response delay {measuredMs:F1}ms does not show the configured {delayMs}ms preset materially affecting transmission timing.");
            if (delayMs == 100)
                Assert.That(pendingSampleFrames, Is.GreaterThan(0), "The 100ms latency trial must sample a frame while the request is pending.");
            Assert.That(maximumPendingDrift, Is.LessThan(0.03f),
                $"CharacterController moved {maximumPendingDrift:F4}m horizontally during sampled pending frames.");
            Assert.That(result, Is.EqualTo("Accepted"),
                $"A valid placement rejected under real owner motion is an acceptance failure. "
                + $"delay={delayMs}ms; result={result}; requestId={PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview)}; "
                + $"movedOwner={movedPose}; capturedPreview={capturedGhostPose}; serverObject={source.transform.position}; "
                + $"responseMs={measuredMs:F1}; diagnostics are captured when enabled");
            Assert.That((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False);
            Assert.That((bool)ControllerType.GetProperty("IsActive").GetValue(controller), Is.False,
                "Accepted remote placement exits F mode.");
            Assert.That(Vector3.Distance(source.transform.position, capturedGhostPose), Is.LessThan(0.003f),
                "The authoritative placed object must match the preview pose captured immediately before E.");
            Assert.That(Quaternion.Angle(source.transform.rotation, capturedGhostRotation), Is.LessThan(0.1f));
            ulong acceptedSourceId = source.NetworkObjectId;
            if (source.IsSpawned) source.Despawn(true);
            yield return WaitUntil(() => Find(owner, acceptedSourceId) == null,
                $"accepted source and peer replica cleanup before the next trial at {delayMs}ms");
            yield return null;
    }

    [UnityTest]
    public IEnumerator RemoteOwnerAllWASDDirectionsAndTurningConfirmsImmediatelyWhileMoving()
    {
        const int delayMs = 0;
        SetTaskDiagnosticPath("all-directions-turn");
        yield return BuildLoopback();
        NetworkManager host = managers[0];
        NetworkManager owner = managers[1];
        NetworkObject ownerPlayer = owner.LocalClient.PlayerObject;
        NetworkObject serverOwnerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        DisableAllExcept(host, serverOwnerPlayer);
        DisableAllExcept(owner, ownerPlayer);
        DisableServerMirrorLocomotion(serverOwnerPlayer);
        IgnoreReplicaCollisions(host, owner);
        EnableControlledPlayerMotion(ownerPlayer);
        yield return WaitForStableSpawn(ownerPlayer, "all-direction remote owner grounded spawn");
        yield return ConfigureAimCamera(ownerPlayer, 45f);

        simulator = AttachNetworkSimulator();
        SetSimulatorPreset(simulator, delayMs);
        yield return null;
        Assert.That((bool)simulator.GetType().GetProperty("IsConnected").GetValue(simulator), Is.True);

        NetworkObject source = SpawnBoardFor(host, new Vector3(20f, 4f, 20f));
        yield return WaitUntil(() => Find(owner, source.NetworkObjectId) != null, "all-direction resource replication");
        host.SetSingleton();
        Assert.That(InvokePickup(source, owner.LocalClientId), Is.True);
        owner.SetSingleton();
        ConfirmLocalPickup(owner, source.NetworkObjectId, CarrySpeedPenalty(source));
        yield return WaitUntil(() => source.OwnerClientId == owner.LocalClientId, "all-direction resource ownership");
        AssertSingleCarryState(owner, Find(owner, source.NetworkObjectId));
        DisableSourceMirrorCollisions(host, owner, ownerPlayer, source.NetworkObjectId);
        yield return new WaitForSecondsRealtime(0.35f);

        Component controller = ownerPlayer.GetComponent(ControllerType);
        Component preview = ownerPlayer.GetComponent(PreviewType);
        QueueKeys(Key.F);
        yield return WaitUntil(() => (bool)ControllerType.GetProperty("IsActive").GetValue(controller), "all-direction F entry");
        Transform ghost = Ghost(controller);
        yield return null;
        Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "The genuine owner preview must be valid before the WASD direction sweep.");

        Vector3 forward = HorizontalForward(ownerPlayer.transform.forward);
        Vector3 right = HorizontalForward(ownerPlayer.transform.right);
        var directionCases = new (string label, Key[] keys, Vector3 expected)[]
        {
            ("W", new[] { Key.W }, forward),
            ("A", new[] { Key.A }, -right),
            ("S", new[] { Key.S }, -forward),
            ("D", new[] { Key.D }, right),
            ("W+A", new[] { Key.W, Key.A }, (forward - right).normalized),
            ("W+D", new[] { Key.W, Key.D }, (forward + right).normalized),
            ("S+A", new[] { Key.S, Key.A }, (-forward - right).normalized),
            ("S+D", new[] { Key.S, Key.D }, (-forward + right).normalized)
        };
        foreach (var directionCase in directionCases)
        {
            QueueKeys(Key.F);
            float settleDeadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < settleDeadline
                && ((Vector3)FirstPersonType.GetProperty("HorizontalVelocity").GetValue(ownerPlayer.GetComponent(FirstPersonType))).magnitude > 0.05f)
                yield return null;
            Assert.That(((Vector3)FirstPersonType.GetProperty("HorizontalVelocity").GetValue(ownerPlayer.GetComponent(FirstPersonType))).magnitude,
                Is.LessThanOrEqualTo(0.05f), $"Movement must settle before measuring direction {directionCase.label}.");
            Vector3 start = ownerPlayer.transform.position;
            var heldKeys = new Key[directionCase.keys.Length + 1];
            heldKeys[0] = Key.F;
            Array.Copy(directionCase.keys, 0, heldKeys, 1, directionCase.keys.Length);
            QueueKeys(heldKeys);
            float deadline = Time.realtimeSinceStartup + 3f;
            Vector3 displacement = Vector3.zero;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                displacement = ownerPlayer.transform.position - start;
                displacement.y = 0f;
                if (Vector3.Dot(displacement, directionCase.expected) >= 0.12f) break;
            }
            Assert.That(Vector3.Dot(displacement, directionCase.expected), Is.GreaterThanOrEqualTo(0.12f),
                $"Actual CharacterController input {directionCase.label} must move along its expected world direction. Delay={delayMs}ms; displacement={displacement}.");
            float lateral = (displacement - directionCase.expected * Vector3.Dot(displacement, directionCase.expected)).magnitude;
            Assert.That(lateral, Is.LessThan(0.12f),
                $"Actual CharacterController input {directionCase.label} must not be mistaken for another WASD direction. Delay={delayMs}ms; displacement={displacement}; lateral={lateral:F3}m.");
            Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
                $"The actual preview remains valid after direction {directionCase.label}; delay={delayMs}ms.");

            Vector3 directionPreviewPosition = ghost.position;
            Quaternion directionPreviewRotation = ghost.rotation;
            ulong directionPreviousId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
            Vector3 directionPendingPose = ownerPlayer.transform.position;
            var directionWatch = System.Diagnostics.Stopwatch.StartNew();
            var confirmationKeys = new Key[heldKeys.Length + 1];
            Array.Copy(heldKeys, confirmationKeys, heldKeys.Length);
            confirmationKeys[confirmationKeys.Length - 1] = Key.E;
            QueueKeys(confirmationKeys);
            Assert.That((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True,
                $"E must be confirmed immediately while {directionCase.label} remains held; no catch-up wait is inserted.");
            Assert.That((bool)InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType)), Is.True,
                $"The {directionCase.label} placement request synchronously blocks movement.");
            float directionPendingDrift = 0f;
            float directionResponseDeadline = Time.realtimeSinceStartup + 15f;
            while ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview) <= directionPreviousId
                && Time.realtimeSinceStartup < directionResponseDeadline)
            {
                yield return null;
                if ((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller))
                {
                    directionPendingDrift = Mathf.Max(directionPendingDrift,
                        HorizontalDistance(directionPendingPose, ownerPlayer.transform.position));
                }
            }
            directionWatch.Stop();
            string directionResult = PreviewType.GetProperty("LastPlacementResult").GetValue(preview).ToString();
            AddDiagnostic($"remote-direction-{directionCase.label}", delayMs, 0, 0, directionResult,
                directionPreviewPosition, source.transform.position, (float)directionWatch.Elapsed.TotalMilliseconds,
                ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview)).ToString(),
                ownerPlayer.transform.position, directionPendingPose, directionPendingDrift);
            Assert.That((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview), Is.GreaterThan(directionPreviousId),
                $"The {directionCase.label} confirmation must receive a response; delay={delayMs}ms; diagnostics are captured when enabled.");
            Assert.That(directionResult, Is.EqualTo("Accepted"),
                $"Every WASD direction must accept its genuine valid preview. direction={directionCase.label}; delay={delayMs}ms; responseMs={directionWatch.Elapsed.TotalMilliseconds:F1}; preview={directionPreviewPosition}; final={source.transform.position}; diagnostics are captured when enabled.");
            Assert.That(directionPendingDrift, Is.LessThan(0.03f),
                $"The {directionCase.label} pending request must stop the owner; drift={directionPendingDrift:F4}m.");
            Assert.That(Vector3.Distance(source.transform.position, directionPreviewPosition), Is.LessThan(0.003f),
                $"The committed {directionCase.label} pose must match its captured ghost.");
            Assert.That(Quaternion.Angle(source.transform.rotation, directionPreviewRotation), Is.LessThan(0.1f),
                $"The committed {directionCase.label} rotation must match its captured ghost.");
            Assert.That((GameObject)InteractionType.GetMethod("GetPickedUpGameObject").Invoke(ownerPlayer.GetComponent(InteractionType), null), Is.Null,
                $"The {directionCase.label} acceptance releases the owner's held source.");
            ulong acceptedDirectionSourceId = source.NetworkObjectId;
            QueueKeys();
            host.SetSingleton();
            source.Despawn(true);
            yield return WaitUntil(() => Find(owner, acceptedDirectionSourceId) == null,
                $"accepted {directionCase.label} source cleanup");
            source = SpawnBoardFor(host, new Vector3(20f, 4f, 20f));
            yield return WaitUntil(() => Find(owner, source.NetworkObjectId) != null,
                $"next direction source replication after {directionCase.label}");
            host.SetSingleton();
            Assert.That(InvokePickup(source, owner.LocalClientId), Is.True,
                $"The server must establish the next actual resource hold after {directionCase.label}.");
            owner.SetSingleton();
            ConfirmLocalPickup(owner, source.NetworkObjectId, CarrySpeedPenalty(source));
            yield return WaitUntil(() => source.OwnerClientId == owner.LocalClientId,
                $"next direction source ownership after {directionCase.label}");
            AssertSingleCarryState(owner, Find(owner, source.NetworkObjectId));
            DisableSourceMirrorCollisions(host, owner, ownerPlayer, source.NetworkObjectId);
            yield return new WaitForSecondsRealtime(0.1f);
            QueueKeys(Key.F);
            yield return WaitUntil(() => (bool)ControllerType.GetProperty("IsActive").GetValue(controller),
                $"next direction F entry after {directionCase.label}");
            ghost = Ghost(controller);
            yield return null;
        }

        mouse = InputSystem.AddDevice<Mouse>();
        SetInputDevices(ownerPlayer, keyboard, mouse);
        Vector3 cameraForwardBeforeTurn = HorizontalForward(fixtureAimCamera.transform.forward);
        Quaternion bodyRotationBeforeTurn = ownerPlayer.transform.rotation;
        Vector3 ownerPositionBeforeTurn = ownerPlayer.transform.position;
        float turnDeadline = Time.realtimeSinceStartup + 3f;
        int turnFrames = 0;
        while (Time.realtimeSinceStartup < turnDeadline
            && (Quaternion.Angle(bodyRotationBeforeTurn, ownerPlayer.transform.rotation) < 5f
                || Vector3.Angle(cameraForwardBeforeTurn, HorizontalForward(fixtureAimCamera.transform.forward)) < 5f))
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.W));
            InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(18f, 0f));
            InputSystem.Update();
            turnFrames++;
            yield return null;
        }
        Assert.That(turnFrames, Is.GreaterThan(0));
        Assert.That(Quaternion.Angle(bodyRotationBeforeTurn, ownerPlayer.transform.rotation), Is.GreaterThan(2f),
            "The synthetic mouse Look action must rotate the real PlayerNew body while W remains held.");
        Assert.That(Vector3.Angle(cameraForwardBeforeTurn, HorizontalForward(fixtureAimCamera.transform.forward)), Is.GreaterThan(2f),
            "The turning input must change the actual aim-camera ray while the CharacterController is moving.");
        Assert.That(HorizontalDistance(ownerPositionBeforeTurn, ownerPlayer.transform.position), Is.GreaterThan(0.08f),
            "The CharacterController must continue moving during the real mouse-look turn.");
        Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "The actual preview must be valid immediately before the moving-and-turning E input.");
        Assert.That(ControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(), Is.EqualTo("SurfaceSupported"),
            "The actual preview must remain supported immediately before the moving-and-turning E input.");

        Vector3 capturedGhostPosition = ghost.position;
        Quaternion capturedGhostRotation = ghost.rotation;
        ulong previousId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.W, Key.E));
        InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(18f, 0f));
        InputSystem.Update();
        Assert.That((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True,
            "E is issued immediately with W and look input still held; no server catch-up wait is inserted.");
        Assert.That((bool)InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType)), Is.True,
            "The real request enters the pending movement stop synchronously.");
        Assert.That(((Vector3)FirstPersonType.GetProperty("HorizontalVelocity").GetValue(ownerPlayer.GetComponent(FirstPersonType))).magnitude,
            Is.LessThan(0.001f), "The request synchronously stops player-controlled movement.");
        Vector3 atPending = ownerPlayer.transform.position;
        float maximumPendingDrift = 0f;
        int pendingFrames = 0;
        float responseDeadline = Time.realtimeSinceStartup + 15f;
        while ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview) <= previousId
            && Time.realtimeSinceStartup < responseDeadline)
        {
            yield return null;
            if ((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller))
            {
                pendingFrames++;
                maximumPendingDrift = Mathf.Max(maximumPendingDrift, HorizontalDistance(atPending, ownerPlayer.transform.position));
            }
        }
        watch.Stop();
        Assert.That((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview), Is.GreaterThan(previousId),
            $"The moving-and-turning request must complete. Delay={delayMs}ms; diagnostics are captured when enabled.");
        string result = PreviewType.GetProperty("LastPlacementResult").GetValue(preview).ToString();
        AddDiagnostic("remote-all-directions-turn-e", delayMs, 0, 0, result, capturedGhostPosition, source.transform.position,
            (float)watch.Elapsed.TotalMilliseconds, ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview)).ToString(),
            ownerPlayer.transform.position, atPending, maximumPendingDrift, 0f);
        Assert.That(result, Is.EqualTo("Accepted"),
            $"Locally valid all-direction/turning preview must be accepted; directionSweep=8; delay={delayMs}ms; responseMs={watch.Elapsed.TotalMilliseconds:F1}; preview={capturedGhostPosition}; final={source.transform.position}; diagnostics are captured when enabled.");
        Assert.That(maximumPendingDrift, Is.LessThan(0.03f), $"Pending CharacterController drift={maximumPendingDrift:F4}m.");
        Assert.That((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False);
        Assert.That((bool)ControllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        Assert.That(Vector3.Distance(source.transform.position, capturedGhostPosition), Is.LessThan(0.003f));
        Assert.That(Quaternion.Angle(source.transform.rotation, capturedGhostRotation), Is.LessThan(0.1f));
        Assert.That((GameObject)InteractionType.GetMethod("GetPickedUpGameObject").Invoke(ownerPlayer.GetComponent(InteractionType), null), Is.Null,
            "Accepted placement releases the held source from the owner's inventory state.");
        Vector3 afterResponse = ownerPlayer.transform.position;
        yield return WaitForForwardMotion(ownerPlayer.transform, afterResponse, HorizontalForward(ownerPlayer.transform.forward), 0.08f,
            "normal held-W movement resumes after accepted moving-and-turning placement");
    }

    [UnityTest]
    public IEnumerator RemoteStationaryOwnerPlacesActualPhysicalDeckPanelWithHundredMillisecondDelay()
    {
        const int delayMs = 100;
        SetTaskDiagnosticPath("stationary-physical-panel-100ms");
        yield return BuildLoopback(usePhysicalDeckPanel: true);
        NetworkManager host = managers[0];
        NetworkManager owner = managers[1];
        NetworkObject ownerPlayer = owner.LocalClient.PlayerObject;
        NetworkObject serverOwnerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        DisableAllExcept(host, serverOwnerPlayer);
        DisableAllExcept(owner, ownerPlayer);
        DisableServerMirrorLocomotion(serverOwnerPlayer);
        IgnoreReplicaCollisions(host, owner);
        EnableControlledPlayerMotion(ownerPlayer);
        yield return WaitForStableSpawn(ownerPlayer, "physical panel owner grounded spawn");
        yield return ConfigureAimCamera(ownerPlayer, 37f);

        simulator = AttachNetworkSimulator();
        SetSimulatorPreset(simulator, delayMs);
        yield return null;
        Assert.That((bool)simulator.GetType().GetProperty("IsConnected").GetValue(simulator), Is.True,
            "The physical panel trial uses an attached loopback NetworkSimulator.");

        Vector3 spawnPosition = ownerPlayer.transform.position + HorizontalForward(ownerPlayer.transform.forward) * 1.25f;
        NetworkObject source = SpawnBoardFor(host, spawnPosition);
        yield return WaitUntil(() => Find(owner, source.NetworkObjectId) != null, "actual WoodenDeckPanelMountable replication");
        NetworkObject ownerSource = Find(owner, source.NetworkObjectId);
        Type mountableType = Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        Assert.That(mountableType, Is.Not.Null);
        Component ownerMountable = ownerSource.GetComponent(mountableType);
        Assert.That(ownerMountable, Is.Not.Null, "The source must retain its actual MountableBridgeComponent.");
        Assert.That((bool)mountableType.GetProperty("IsPhysicalDeckPanel").GetValue(ownerMountable), Is.True,
            "The carried source must be the physical WoodenDeckPanel prefab, not a dummy resource.");
        Assert.That((bool)mountableType.GetProperty("CanEnterSingleCarryPlacement").GetValue(ownerMountable), Is.True);
        owner.SetSingleton();
        mountableType.GetMethod("PickedUp").Invoke(ownerMountable, new object[] { ownerPlayer.transform });
        yield return WaitUntil(() => source.OwnerClientId == owner.LocalClientId
            && (bool)mountableType.GetProperty("IsPickedUp").GetValue(source.GetComponent(mountableType))
            && (bool)mountableType.GetProperty("IsPickedUp").GetValue(ownerSource.GetComponent(mountableType)),
            "physical deck panel network pickup and owner state");
        Assert.That((GameObject)InteractionType.GetMethod("GetPickedUpGameObject").Invoke(ownerPlayer.GetComponent(InteractionType), null),
            Is.SameAs(ownerSource.gameObject), "The real PlayerInteractionNew must hold the actual panel.");
        DisableSourceMirrorCollisions(host, owner, ownerPlayer, source.NetworkObjectId);

        Component controller = ownerPlayer.GetComponent(ControllerType);
        Component preview = ownerPlayer.GetComponent(PreviewType);
        BeginTrace(owner, ownerPlayer, host, serverOwnerPlayer, delayMs, "stationary-physical-panel-acceptance");
        Vector3 stationaryPose = ownerPlayer.transform.position;
        Quaternion stationaryRotation = ownerPlayer.transform.rotation;
        QueueKeys(Key.F);
        yield return WaitUntil(() => (bool)ControllerType.GetProperty("IsActive").GetValue(controller), "physical panel F mode entry");
        Transform ghost = Ghost(controller);
        float previewDeadline = Time.realtimeSinceStartup + 15f;
        while (!(bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller)
            && Time.realtimeSinceStartup < previewDeadline) yield return null;
        Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            $"The actual physical panel must have a genuinely valid local preview before E. geometry={ControllerType.GetProperty("LastGeometryStatus").GetValue(controller)}; validation={ControllerType.GetProperty("LastValidation").GetValue(controller)}; player={ownerPlayer.transform.position}; camera={fixtureAimCamera.transform.position}/{fixtureAimCamera.transform.forward}; nearby={DescribeNearbyColliders(ownerPlayer.transform)}; {DescribeSurfaceConfirmState(controller)}.");
        Assert.That(ControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(), Is.EqualTo("SurfaceSupported"),
            "The physical prefab candidate must be supported by the fixture floor.");
        Assert.That((bool)ControllerType.GetField("networkSurfaceAnchorRejected", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller), Is.False,
            $"The physical-panel preview must contact a supported placement surface, not its held source. {DescribeSurfaceConfirmState(controller)}.");
        Assert.That(HorizontalDistance(stationaryPose, ownerPlayer.transform.position), Is.LessThan(0.01f),
            "This is a stationary-owner trial; no pre-E owner teleport or movement is used.");
        Assert.That(Quaternion.Angle(stationaryRotation, ownerPlayer.transform.rotation), Is.LessThan(0.1f));
        Vector3 capturedGhostPosition = ghost.position;
        Quaternion capturedGhostRotation = ghost.rotation;
        Assert.That(Vector3.Distance(fixtureAimCamera.transform.position, capturedGhostPosition), Is.LessThanOrEqualTo(2f),
            $"The stationary physical-panel preview must remain inside the normal 2m placement range. camera={fixtureAimCamera.transform.position}; preview={capturedGhostPosition}; {DescribeSurfaceConfirmState(controller)}.");
        WritePanelColliderSnapshot("stationary-pre-e", delayMs, controller);
        ulong previousId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
        Vector3 atPending = ownerPlayer.transform.position;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        QueueKeys(Key.F, Key.E);
        bool stationaryPanelPendingImmediately = (bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller);
        object stationaryPanelValidation = ControllerType.GetProperty("LastValidation").GetValue(controller);
        string stationaryValidationDetail = DescribePlacementValidation(stationaryPanelValidation);
        Assert.That(stationaryPanelPendingImmediately, Is.True,
            $"The stationary physical-panel E request enters pending synchronously. delay={delayMs}ms; active={ControllerType.GetProperty("IsActive").GetValue(controller)}; previewValid={ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller)}; completedId={PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview)}; previousId={previousId}; result={PreviewType.GetProperty("LastPlacementResult").GetValue(preview)}; inputBlocked={InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType))}; geometry={ControllerType.GetProperty("LastGeometryStatus").GetValue(controller)}; {stationaryValidationDetail}; {DescribeSurfaceConfirmState(controller)}.");
        Assert.That((bool)InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType)), Is.True);
        float maximumPendingDrift = 0f;
        int pendingFrames = 0;
        float responseDeadline = Time.realtimeSinceStartup + 15f;
        while ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview) <= previousId
            && Time.realtimeSinceStartup < responseDeadline)
        {
            yield return null;
            if ((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller))
            {
                pendingFrames++;
                maximumPendingDrift = Mathf.Max(maximumPendingDrift, HorizontalDistance(atPending, ownerPlayer.transform.position));
            }
        }
        watch.Stop();
        Assert.That((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview), Is.GreaterThan(previousId),
            "The physical-panel server response must arrive within the bounded response window.");
        string result = PreviewType.GetProperty("LastPlacementResult").GetValue(preview).ToString();
        ulong completedRequestId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
        ExportAndValidateTrace(completedRequestId, result, watch.Elapsed.TotalMilliseconds,
            result == "Accepted" ? "AimPassed" : null, requireMovement: false);
        AddDiagnostic("remote-stationary-physical-panel", delayMs, 0, 0, result, capturedGhostPosition, source.transform.position,
            (float)watch.Elapsed.TotalMilliseconds, completedRequestId.ToString(),
            ownerPlayer.transform.position, atPending, maximumPendingDrift, 0f);
        Assert.That(watch.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(100d),
            $"The configured 100ms NetworkSimulator setting must materially affect measured request/response time (not interpreted as one-way latency). Actual={watch.Elapsed.TotalMilliseconds:F1}ms.");
        Assert.That(pendingFrames, Is.GreaterThan(0));
        Assert.That(maximumPendingDrift, Is.LessThan(0.03f));
        Assert.That(result, Is.EqualTo("Accepted"),
            $"The actual physical panel with valid stationary preview must be accepted; delay={delayMs}ms; measuredResponseMs={watch.Elapsed.TotalMilliseconds:F1}; preview={capturedGhostPosition}; final={source.transform.position}; diagnostics are captured when enabled.");
        Assert.That(Vector3.Distance(source.transform.position, capturedGhostPosition), Is.LessThan(0.003f));
        Assert.That(Quaternion.Angle(source.transform.rotation, capturedGhostRotation), Is.LessThan(0.1f));
        Assert.That(source.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That((bool)mountableType.GetProperty("IsPickedUp").GetValue(source.GetComponent(mountableType)), Is.False);
        Assert.That((GameObject)InteractionType.GetMethod("GetPickedUpGameObject").Invoke(ownerPlayer.GetComponent(InteractionType), null), Is.Null);
        Vector3 afterResponse = ownerPlayer.transform.position;
        QueueKeys(Key.W);
        yield return WaitForForwardMotion(ownerPlayer.transform, afterResponse, HorizontalForward(ownerPlayer.transform.forward), 0.08f,
            "normal CharacterController movement after stationary physical panel placement");
    }

    [UnityTest]
    public IEnumerator RemoteMovingOwnerTurnsAndPlacesActualPhysicalDeckPanelWithNoAddedDelay()
    {
        yield return RunPhysicalDeckPanelMovingTurnTrial(0);
    }

    [UnityTest]
    public IEnumerator RemoteMovingOwnerTurnsAndPlacesActualPhysicalDeckPanelWithHundredMillisecondDelay()
    {
        yield return RunPhysicalDeckPanelMovingTurnTrial(100);
    }

    private IEnumerator RunPhysicalDeckPanelMovingTurnTrial(int delayMs)
    {
        SetTaskDiagnosticPath($"moving-turn-physical-panel-{delayMs}ms");
        yield return BuildLoopback(usePhysicalDeckPanel: true);
        NetworkManager host = managers[0];
        NetworkManager owner = managers[1];
        NetworkObject ownerPlayer = owner.LocalClient.PlayerObject;
        NetworkObject serverOwnerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        DisableAllExcept(host, serverOwnerPlayer);
        DisableAllExcept(owner, ownerPlayer);
        DisableServerMirrorLocomotion(serverOwnerPlayer);
        IgnoreReplicaCollisions(host, owner);
        EnableControlledPlayerMotion(ownerPlayer);
        yield return WaitForStableSpawn(ownerPlayer, $"physical panel moving owner spawn at {delayMs}ms");
        yield return ConfigureAimCamera(ownerPlayer, 37f);

        simulator = AttachNetworkSimulator();
        SetSimulatorPreset(simulator, delayMs);
        yield return null;
        Assert.That((bool)simulator.GetType().GetProperty("IsConnected").GetValue(simulator), Is.True,
            $"The physical panel trial must use a connected NetworkSimulator at {delayMs}ms.");

        Vector3 spawnPosition = ownerPlayer.transform.position + HorizontalForward(ownerPlayer.transform.forward) * 1.25f;
        NetworkObject source = SpawnBoardFor(host, spawnPosition);
        yield return WaitUntil(() => Find(owner, source.NetworkObjectId) != null, $"physical panel replication at {delayMs}ms");
        NetworkObject ownerSource = Find(owner, source.NetworkObjectId);
        Type mountableType = Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        Component ownerMountable = ownerSource.GetComponent(mountableType);
        Assert.That(ownerMountable, Is.Not.Null);
        Assert.That((bool)mountableType.GetProperty("IsPhysicalDeckPanel").GetValue(ownerMountable), Is.True,
            "Moving-E must use the actual physical WoodenDeckPanelMountable prefab.");
        Assert.That((bool)mountableType.GetProperty("CanEnterSingleCarryPlacement").GetValue(ownerMountable), Is.True);
        owner.SetSingleton();
        mountableType.GetMethod("PickedUp").Invoke(ownerMountable, new object[] { ownerPlayer.transform });
        yield return WaitUntil(() => source.OwnerClientId == owner.LocalClientId
            && (bool)mountableType.GetProperty("IsPickedUp").GetValue(source.GetComponent(mountableType))
            && (bool)mountableType.GetProperty("IsPickedUp").GetValue(ownerSource.GetComponent(mountableType)),
            $"physical panel pickup at {delayMs}ms");
        Assert.That((GameObject)InteractionType.GetMethod("GetPickedUpGameObject").Invoke(ownerPlayer.GetComponent(InteractionType), null),
            Is.SameAs(ownerSource.gameObject));
        DisableSourceMirrorCollisions(host, owner, ownerPlayer, source.NetworkObjectId);
        Collider[] ownerPanelColliders = PlacementShapeColliders(ownerSource);
        Collider[] serverPanelColliders = PlacementShapeColliders(source);
        SetPlacementCollidersEnabled(serverPanelColliders, false);
        AppendCheckpoint($"physical-panel post-pickup before settle delay={delayMs}ms ownerPosition={ownerPlayer.transform.position.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
        yield return WaitForStableSpawn(ownerPlayer, $"physical panel owner settles after pickup at {delayMs}ms");
        AppendCheckpoint($"physical-panel post-pickup after settle delay={delayMs}ms ownerPosition={ownerPlayer.transform.position.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
        Component controller = ownerPlayer.GetComponent(ControllerType);
        Component preview = ownerPlayer.GetComponent(PreviewType);

        QueueKeys(Key.F);
        yield return WaitUntil(() => (bool)ControllerType.GetProperty("IsActive").GetValue(controller), $"physical panel F entry at {delayMs}ms");
        Transform ghost = Ghost(controller);
        float previewDeadline = Time.realtimeSinceStartup + 15f;
        while (!(bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller)
            && Time.realtimeSinceStartup < previewDeadline) yield return null;
        WritePanelColliderSnapshot($"moving-entry-preview-{delayMs}ms", delayMs, controller);
        Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            $"Physical-panel preview must be valid before movement at {delayMs}ms. geometry={ControllerType.GetProperty("LastGeometryStatus").GetValue(controller)}; {DescribePlacementValidation(ControllerType.GetProperty("LastValidation").GetValue(controller))}; player={ownerPlayer.transform.position}; camera={fixtureAimCamera.transform.position}/{fixtureAimCamera.transform.forward}; nearby={DescribeNearbyColliders(ownerPlayer.transform)}; {DescribeSurfaceConfirmState(controller)}.");
        Assert.That(ControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(), Is.EqualTo("SurfaceSupported"),
            $"Physical-panel preview must be supported by an actual surface at {delayMs}ms before turning. valid={ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller)}; validation={DescribePlacementValidation(ControllerType.GetProperty("LastValidation").GetValue(controller))}; player={ownerPlayer.transform.position}; camera={fixtureAimCamera.transform.position}/{fixtureAimCamera.transform.forward}; nearby={DescribeNearbyColliders(ownerPlayer.transform)}; {DescribeSurfaceConfirmState(controller)}.");
        Assert.That((bool)ControllerType.GetField("networkSurfaceAnchorRejected", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller), Is.False,
            $"The moving physical-panel preview must not anchor against its held source. delay={delayMs}ms; {DescribeSurfaceConfirmState(controller)}.");

        mouse = InputSystem.AddDevice<Mouse>();
        SetInputDevices(ownerPlayer, keyboard, mouse);
        Vector3 beforeMove = ownerPlayer.transform.position;
        Vector3 expectedForward = HorizontalForward(ownerPlayer.transform.forward);
        Vector3 cameraForwardBeforeTurn = HorizontalForward(fixtureAimCamera.transform.forward);
        Quaternion bodyRotationBeforeTurn = ownerPlayer.transform.rotation;
        float turnDeadline = Time.realtimeSinceStartup + 5f;
        int turnFrames = 0;
        while (Time.realtimeSinceStartup < turnDeadline
            && (Vector3.Dot(ownerPlayer.transform.position - beforeMove, expectedForward) < 0.15f
                || Quaternion.Angle(bodyRotationBeforeTurn, ownerPlayer.transform.rotation) < 5f
                || Vector3.Angle(cameraForwardBeforeTurn, HorizontalForward(fixtureAimCamera.transform.forward)) < 5f))
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.W));
            InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(18f, 0f));
            InputSystem.Update();
            turnFrames++;
            yield return null;
        }
        Assert.That(turnFrames, Is.GreaterThan(0));
        Assert.That(Vector3.Dot(ownerPlayer.transform.position - beforeMove, expectedForward), Is.GreaterThanOrEqualTo(0.15f),
            $"Actual CharacterController W motion must continue through turn at {delayMs}ms.");
        Assert.That(Quaternion.Angle(bodyRotationBeforeTurn, ownerPlayer.transform.rotation), Is.GreaterThan(2f),
            $"Real mouse Look must rotate the player body during panel motion at {delayMs}ms.");
        Assert.That(Vector3.Angle(cameraForwardBeforeTurn, HorizontalForward(fixtureAimCamera.transform.forward)), Is.GreaterThan(2f),
            $"Real mouse Look must rotate the aim camera/ray at {delayMs}ms.");
        Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            $"The actual physical-panel preview must remain valid immediately before E at {delayMs}ms. geometry={ControllerType.GetProperty("LastGeometryStatus").GetValue(controller)}; {DescribePlacementValidation(ControllerType.GetProperty("LastValidation").GetValue(controller))}; player={ownerPlayer.transform.position}; camera={fixtureAimCamera.transform.position}/{fixtureAimCamera.transform.forward}; {DescribeSurfaceConfirmState(controller)}.");
        Assert.That(ControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(), Is.EqualTo("SurfaceSupported"));

        Vector3 capturedGhostPosition = ghost.position;
        Quaternion capturedGhostRotation = ghost.rotation;
        Assert.That(Vector3.Distance(fixtureAimCamera.transform.position, capturedGhostPosition), Is.LessThanOrEqualTo(2f),
            $"The moving physical-panel preview must remain inside the normal 2m placement range. delay={delayMs}ms; camera={fixtureAimCamera.transform.position}; preview={capturedGhostPosition}; {DescribeSurfaceConfirmState(controller)}.");
        WritePanelColliderSnapshot($"moving-pre-e-{delayMs}ms", delayMs, controller);
        ulong previousId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
        Vector3 atPending = ownerPlayer.transform.position;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.W, Key.E));
        InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(18f, 0f));
        InputSystem.Update();
        bool movingPanelPendingImmediately = (bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller);
        object movingPanelValidation = ControllerType.GetProperty("LastValidation").GetValue(controller);
        string movingValidationDetail = DescribePlacementValidation(movingPanelValidation);
        Assert.That(movingPanelPendingImmediately, Is.True,
            $"Moving, turning physical-panel E enters pending synchronously at {delayMs}ms. active={ControllerType.GetProperty("IsActive").GetValue(controller)}; previewValid={ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller)}; completedId={PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview)}; previousId={previousId}; result={PreviewType.GetProperty("LastPlacementResult").GetValue(preview)}; inputBlocked={InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType))}; geometry={ControllerType.GetProperty("LastGeometryStatus").GetValue(controller)}; {movingValidationDetail}; {DescribeSurfaceConfirmState(controller)}.");
        Assert.That((bool)InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType)), Is.True);
        Assert.That(((Vector3)FirstPersonType.GetProperty("HorizontalVelocity").GetValue(ownerPlayer.GetComponent(FirstPersonType))).magnitude,
            Is.LessThan(0.001f));
        // Loopback clients share one PhysicsScene; while the RPC is in flight, model the server's
        // separate physics view by enabling its physical panel and hiding the owner's mirror.
        SetPlacementCollidersEnabled(ownerPanelColliders, false);
        SetPlacementCollidersEnabled(serverPanelColliders, true);
        float maximumPendingDrift = 0f;
        int pendingFrames = 0;
        float responseDeadline = Time.realtimeSinceStartup + 15f;
        while ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview) <= previousId
            && Time.realtimeSinceStartup < responseDeadline)
        {
            yield return null;
            if ((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller))
            {
                pendingFrames++;
                maximumPendingDrift = Mathf.Max(maximumPendingDrift, HorizontalDistance(atPending, ownerPlayer.transform.position));
            }
        }
        watch.Stop();
        Assert.That((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview), Is.GreaterThan(previousId),
            $"Physical-panel request response must arrive at {delayMs}ms.");
        string result = PreviewType.GetProperty("LastPlacementResult").GetValue(preview).ToString();
        AddDiagnostic($"remote-moving-turn-physical-panel-{delayMs}ms", delayMs, 0, 0, result, capturedGhostPosition,
            source.transform.position, (float)watch.Elapsed.TotalMilliseconds,
            ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview)).ToString(),
            ownerPlayer.transform.position, atPending, maximumPendingDrift, 0f);
        if (delayMs == 100)
            Assert.That(watch.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(100d),
                $"Measured request/response must reflect configured 100ms network delay; not an asserted one-way latency. measured={watch.Elapsed.TotalMilliseconds:F1}ms.");
        if (delayMs == 100)
            Assert.That(pendingFrames, Is.GreaterThan(0), $"Pending request must be observed during the 100ms trial.");
        Assert.That(maximumPendingDrift, Is.LessThan(0.03f), $"Physical-panel pending drift={maximumPendingDrift:F4}m at {delayMs}ms.");
        Assert.That(result, Is.EqualTo("Accepted"),
            $"The valid physical panel preview during actual W+look movement must be accepted; delay={delayMs}ms; responseMs={watch.Elapsed.TotalMilliseconds:F1}; preview={capturedGhostPosition}; final={source.transform.position}; diagnostics are captured when enabled.");
        Assert.That(Vector3.Distance(source.transform.position, capturedGhostPosition), Is.LessThan(0.003f));
        Assert.That(Quaternion.Angle(source.transform.rotation, capturedGhostRotation), Is.LessThan(0.1f));
        Assert.That(source.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That((bool)mountableType.GetProperty("IsPickedUp").GetValue(source.GetComponent(mountableType)), Is.False);
        Assert.That((GameObject)InteractionType.GetMethod("GetPickedUpGameObject").Invoke(ownerPlayer.GetComponent(InteractionType), null), Is.Null);
        SetPlacementCollidersEnabled(serverPanelColliders, false);
        SetPlacementCollidersEnabled(ownerPanelColliders, true);
        Vector3 afterResponse = ownerPlayer.transform.position;
        yield return WaitForForwardMotion(ownerPlayer.transform, afterResponse, HorizontalForward(ownerPlayer.transform.forward), 0.08f,
            $"normal W movement resumes after physical-panel placement at {delayMs}ms");
    }

    [UnityTest]
    public IEnumerator RemoteMissingReceivedPoseRefusesValidPreviewAndResumesMovement()
    {
        const int delayMs = 100;
        SetTaskDiagnosticPath("missing-received-pose-refusal-100ms");
        yield return BuildLoopback();
        NetworkManager host = managers[0];
        NetworkManager owner = managers[1];
        NetworkObject ownerPlayer = owner.LocalClient.PlayerObject;
        NetworkObject serverOwnerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        DisableAllExcept(host, serverOwnerPlayer);
        DisableAllExcept(owner, ownerPlayer);
        DisableServerMirrorLocomotion(serverOwnerPlayer);
        IgnoreReplicaCollisions(host, owner);
        EnableControlledPlayerMotion(ownerPlayer);
        yield return WaitForStableSpawn(ownerPlayer, "missing-pose owner grounded spawn");
        yield return ConfigureAimCamera(ownerPlayer, 45f);
        simulator = AttachNetworkSimulator();
        SetSimulatorPreset(simulator, delayMs);
        yield return null;
        Assert.That((bool)simulator.GetType().GetProperty("IsConnected").GetValue(simulator), Is.True);

        NetworkObject source = SpawnBoardFor(host, new Vector3(20f, 4f, 20f));
        yield return WaitUntil(() => Find(owner, source.NetworkObjectId) != null, "missing-pose resource replication");
        host.SetSingleton();
        Assert.That(InvokePickup(source, owner.LocalClientId), Is.True);
        owner.SetSingleton();
        ConfirmLocalPickup(owner, source.NetworkObjectId, CarrySpeedPenalty(source));
        yield return WaitUntil(() => source.OwnerClientId == owner.LocalClientId, "missing-pose resource ownership");
        AssertSingleCarryState(owner, Find(owner, source.NetworkObjectId));
        DisableSourceMirrorCollisions(host, owner, ownerPlayer, source.NetworkObjectId);
        yield return new WaitForSecondsRealtime(0.35f);

        Component controller = ownerPlayer.GetComponent(ControllerType);
        Component preview = ownerPlayer.GetComponent(PreviewType);
        BeginTrace(owner, ownerPlayer, host, serverOwnerPlayer, delayMs, "missing-received-pose-control");
        QueueKeys(Key.F);
        yield return WaitUntil(() => (bool)ControllerType.GetProperty("IsActive").GetValue(controller), "missing-pose F entry");
        QueueKeys(Key.F, Key.W);
        Vector3 beforeMotion = ownerPlayer.transform.position;
        Vector3 expectedForward = HorizontalForward(ownerPlayer.transform.forward);
        yield return WaitForForwardMotion(ownerPlayer.transform, beforeMotion, expectedForward, 0.2f,
            "real CharacterController motion before missing-sample refusal");
        QueueKeys(Key.F);
        float settleDeadline = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < settleDeadline
            && ((Vector3)FirstPersonType.GetProperty("HorizontalVelocity").GetValue(ownerPlayer.GetComponent(FirstPersonType))).magnitude > 0.05f)
            yield return null;
        Assert.That(((Vector3)FirstPersonType.GetProperty("HorizontalVelocity").GetValue(ownerPlayer.GetComponent(FirstPersonType))).magnitude,
            Is.LessThanOrEqualTo(0.05f), "Owner must be stationary before the test invalidates its server-received pose sample.");
        Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "The owner's unmodified production preview remains genuinely valid before the server-side sample fault injection.");
        Assert.That(ControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(), Is.EqualTo("SurfaceSupported"));

        Type clientTransformType = Type.GetType("ClientNetworkTransform, Assembly-CSharp");
        Component serverTransform = serverOwnerPlayer.GetComponent(clientTransformType);
        Assert.That(serverTransform, Is.Not.Null);
        FieldInfo validSampleField = clientTransformType.GetField("hasCompleteAuthoritativePose", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(validSampleField, Is.Not.Null);
        Assert.That((bool)validSampleField.GetValue(serverTransform), Is.True,
            "Before injection, this non-authority server copy must have a real complete sample received through NGO.");
        validSampleField.SetValue(serverTransform, false);
        host.SetSingleton();
        object[] poseArgs = { Vector3.zero, Quaternion.identity };
        bool sampleAvailable = (bool)clientTransformType.GetMethod("TryGetLatestAuthoritativeWorldPose").Invoke(serverTransform, poseArgs);
        Assert.That(sampleAvailable, Is.False,
            "Fault injection removes only the server's complete-sample validity; production query must then refuse without interpolation fallback.");
        Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "The locally displayed preview stays valid while the server has no authoritative sample.");

        Vector3 capturedPreview = Ghost(controller).position;
        ulong previousId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
        Vector3 atPending = ownerPlayer.transform.position;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        QueueKeys(Key.F, Key.W, Key.E);
        Assert.That((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True,
            "The genuine valid-preview E is sent immediately; the test does not wait for synchronization or retry.");
        Assert.That((bool)InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType)), Is.True);
        float maximumPendingDrift = 0f;
        int pendingFrames = 0;
        float responseDeadline = Time.realtimeSinceStartup + 15f;
        while ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview) <= previousId
            && Time.realtimeSinceStartup < responseDeadline)
        {
            yield return null;
            if ((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller))
            {
                pendingFrames++;
                maximumPendingDrift = Mathf.Max(maximumPendingDrift, HorizontalDistance(atPending, ownerPlayer.transform.position));
            }
        }
        watch.Stop();
        Assert.That((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview), Is.GreaterThan(previousId),
            "The no-sample refusal must be reported by the existing server request path.");
        string result = PreviewType.GetProperty("LastPlacementResult").GetValue(preview).ToString();
        ulong completedRequestId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
        ExportAndValidateTrace(completedRequestId, result, watch.Elapsed.TotalMilliseconds, "MissingReceivedPose");
        string serverRpcAimDiagnostic = null;
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        serverRpcAimDiagnostic = CaptureServerRpcAimDiagnostic(serverOwnerPlayer);
#endif
        AddDiagnostic("remote-missing-received-pose-refusal", delayMs, 0, 0, result, capturedPreview, source.transform.position,
            (float)watch.Elapsed.TotalMilliseconds, completedRequestId.ToString(),
            ownerPlayer.transform.position, atPending, maximumPendingDrift, 0f, null, serverRpcAimDiagnostic);
        Assert.That(watch.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(100d),
            $"Measured request/response reflects the configured delay; not one-way latency. {watch.Elapsed.TotalMilliseconds:F1}ms.");
        Assert.That(pendingFrames, Is.GreaterThan(0));
        Assert.That(maximumPendingDrift, Is.LessThan(0.03f));
        Assert.That(result, Is.EqualTo("InvalidAimOrigin"),
            $"Absence of the server-received authoritative sample must use the existing InvalidAimOrigin refusal. Preview remains locally valid; latency={watch.Elapsed.TotalMilliseconds:F1}ms; diagnostics are captured when enabled.");
        AssertServerAimBranch(serverRpcAimDiagnostic, "MissingReceivedPose", completedRequestId, result);
        Assert.That((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False);
        Assert.That((bool)ControllerType.GetProperty("IsActive").GetValue(controller), Is.True,
            "A refusal preserves F mode so the owner may retry after a fresh sample exists.");
        Assert.That(source.OwnerClientId, Is.EqualTo(owner.LocalClientId));
        Assert.That((GameObject)InteractionType.GetMethod("GetPickedUpGameObject").Invoke(ownerPlayer.GetComponent(InteractionType), null),
            Is.SameAs(Find(owner, source.NetworkObjectId).gameObject), "The refused source remains held by its owner.");
        Assert.That(Vector3.Distance(source.transform.position, capturedPreview), Is.GreaterThan(0.05f),
            "A rejected request leaves the held source away from the preview commit pose.");
        Assert.That((bool)InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType)), Is.False,
            "Refusal clears pending movement stop.");
        Vector3 afterResponse = ownerPlayer.transform.position;
        yield return WaitForForwardMotion(ownerPlayer.transform, afterResponse, expectedForward, 0.12f,
            "held W movement resumes after InvalidAimOrigin due to missing sample");
    }

    [UnityTest]
    public IEnumerator RemoteOwnerResumesHeldWAfterARealPlacementRefusal()
    {
        yield return BuildLoopback();
        NetworkManager host = managers[0];
        NetworkManager owner = managers[1];
        NetworkObject ownerPlayer = owner.LocalClient.PlayerObject;
        NetworkObject serverOwnerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        DisableAllExcept(host, serverOwnerPlayer);
        DisableAllExcept(owner, ownerPlayer);
        DisableServerMirrorLocomotion(serverOwnerPlayer);
        IgnoreReplicaCollisions(host, owner);
        EnableControlledPlayerMotion(ownerPlayer);
        AppendCheckpoint($"RefusalTest:spawn-before-stability position={ownerPlayer.transform.position.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} nearby={DescribeNearbyColliders(ownerPlayer.transform)}");
        yield return WaitForStableSpawn(ownerPlayer, "refusal fixture remote owner grounded spawn");
        yield return ConfigureAimCamera(ownerPlayer, 45f);
        simulator = AttachNetworkSimulator();
        SetSimulatorPreset(simulator, 100);
        yield return null;
        Assert.That((bool)simulator.GetType().GetProperty("IsAvailable").GetValue(simulator), Is.True);
        Assert.That((bool)simulator.GetType().GetProperty("IsConnected").GetValue(simulator), Is.True);

        NetworkObject source = SpawnBoardFor(host, new Vector3(20f, 4f, 20f));
        yield return WaitUntil(() => Find(owner, source.NetworkObjectId) != null, "refusal source replication");
        host.SetSingleton();
        Assert.That(InvokePickup(source, owner.LocalClientId), Is.True);
        float carryPenalty = CarrySpeedPenalty(source);
        owner.SetSingleton();
        ConfirmLocalPickup(owner, source.NetworkObjectId, carryPenalty);
        yield return WaitUntil(() => source.OwnerClientId == owner.LocalClientId, "refusal source ownership");
        AssertSingleCarryState(owner, Find(owner, source.NetworkObjectId));
        DisableSourceMirrorCollisions(host, owner, ownerPlayer, source.NetworkObjectId);
        yield return new WaitForSecondsRealtime(0.35f);

        Component controller = ownerPlayer.GetComponent(ControllerType);
        Component preview = ownerPlayer.GetComponent(PreviewType);
        BeginTrace(owner, ownerPlayer, host, serverOwnerPlayer, 100, "residual-exceeded-refusal-control");
        QueueKeys(Key.F);
        yield return WaitUntil(() => (bool)ControllerType.GetProperty("IsActive").GetValue(controller), "refusal F entry");
        Camera aimCamera = (Camera)InteractionType.GetField("aimCamera", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(ownerPlayer.GetComponent(InteractionType));
        Assert.That(aimCamera, Is.Not.Null);
        QueueKeys(Key.F, Key.W);
        Vector3 beforeMovement = ownerPlayer.transform.position;
        Vector3 expectedForward = HorizontalForward(ownerPlayer.transform.forward);
        yield return WaitForForwardMotion(ownerPlayer.transform, beforeMovement, expectedForward, 0.25f, "remote CharacterController forward movement before refusal");
        Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "The deliberately stale camera origin must begin from an otherwise valid preview.");
        Assert.That(ControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(), Is.EqualTo("SurfaceSupported"));

        // This fixture changes only the local camera after the preview has had a frame to sample it.
        // The request is intentionally invalid by aim-origin policy so this trial proves real stop/resume.
        aimCamera.transform.localPosition += Vector3.right * 0.5f;
        yield return null;
        Assert.That((bool)ControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "A stale-origin refusal must not be caused by local occupancy/geometry invalidity.");
        Assert.That(ControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(), Is.EqualTo("SurfaceSupported"));
        Vector3 capturedGhostPose = Ghost(controller).position;
        ulong previousId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
        Vector3 atPending = ownerPlayer.transform.position;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        QueueKeys(Key.F, Key.W, Key.E);
        Assert.That((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True);
        Assert.That((bool)InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType)), Is.True);
        Vector3 pendingVelocity = (Vector3)FirstPersonType.GetProperty("HorizontalVelocity").GetValue(ownerPlayer.GetComponent(FirstPersonType));
        Assert.That(pendingVelocity.magnitude, Is.LessThan(0.001f));

        float maximumPendingDrift = 0f;
        int pendingSampleFrames = 0;
        float responseDeadline = Time.realtimeSinceStartup + 15f;
        while ((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview) <= previousId
            && Time.realtimeSinceStartup < responseDeadline)
        {
            yield return null;
            if ((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller))
            {
                pendingSampleFrames++;
                maximumPendingDrift = Mathf.Max(maximumPendingDrift, HorizontalDistance(atPending, ownerPlayer.transform.position));
            }
        }
        Assert.That((ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview), Is.GreaterThan(previousId),
            "The deliberate InvalidAimOrigin request must receive a server response.");
        watch.Stop();
        string result = PreviewType.GetProperty("LastPlacementResult").GetValue(preview).ToString();
        ulong completedRequestId = (ulong)PreviewType.GetProperty("LastCompletedPlacementRequestId").GetValue(preview);
        ExportAndValidateTrace(completedRequestId, result, watch.Elapsed.TotalMilliseconds, "ResidualExceeded");
        string serverRpcAimDiagnostic = null;
#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
        serverRpcAimDiagnostic = CaptureServerRpcAimDiagnostic(serverOwnerPlayer);
#endif
        float measuredMs = (float)watch.Elapsed.TotalMilliseconds;
        Vector3 afterResponse = ownerPlayer.transform.position;
        yield return WaitForForwardMotion(ownerPlayer.transform, afterResponse, expectedForward, 0.15f, "held-W owner movement after refusal");
        float resumedDisplacement = ForwardDisplacement(ownerPlayer.transform.position, afterResponse, expectedForward);
        AddDiagnostic("remote-refusal-resume", 100, 0, 0, result, capturedGhostPose, source.transform.position,
            measuredMs, completedRequestId.ToString(System.Globalization.CultureInfo.InvariantCulture), ownerPlayer.transform.position, atPending,
            maximumPendingDrift, resumedDisplacement, null, serverRpcAimDiagnostic);
        Assert.That(pendingSampleFrames, Is.GreaterThan(0), "100ms delay must expose at least one observable pending frame.");
        Assert.That(result, Is.EqualTo("InvalidAimOrigin"), "The fixture must exercise a server-refused request, not local invalid E.");
        AssertServerAimBranch(serverRpcAimDiagnostic, "ResidualExceeded", completedRequestId, result);
        Assert.That((bool)ControllerType.GetProperty("IsActive").GetValue(controller), Is.True,
            "A refused request retains the placement mode and held item.");
        Assert.That((bool)ControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False);
        Assert.That((bool)InputNewType.GetProperty("IsSingleCarryPlacementMovementBlocked").GetValue(ownerPlayer.GetComponent(InputNewType)), Is.False);
        Assert.That((Vector2)InputNewType.GetMethod("GetMoveVectorValue").Invoke(ownerPlayer.GetComponent(InputNewType), null), Is.EqualTo(Vector2.up),
            "The still-held W is available again immediately after refusal.");
        Assert.That(measuredMs, Is.GreaterThanOrEqualTo(100f), "Refusal response must show the configured network delay.");
        Assert.That(maximumPendingDrift, Is.LessThan(0.03f), "CharacterController remains stopped throughout sampled pending frames.");
        Assert.That(resumedDisplacement, Is.GreaterThanOrEqualTo(0.05f), "The real CharacterController resumes held-W motion without key release.");
    }

    private IEnumerator BuildLoopback(bool includeRemoteOwner = true, bool usePhysicalDeckPanel = false)
    {
        AppendCheckpoint("BuildLoopback:start");
        usesPhysicalDeckPanel = usePhysicalDeckPanel;
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True,
            "The isolated loopback fixture cannot replace an active NGO session.");
        previousSingleton = NetworkManager.Singleton;
        capturedSingleton = true;
        GameObject productionPlayer = null;
        GameObject productionBoard = null;
#if UNITY_EDITOR
        productionPlayer = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        productionBoard = usePhysicalDeckPanel
            ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab")
            : UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Resources/WoodenBoardResource_prefab.prefab");
#else
        Assert.Ignore("This loopback fixture loads production prefabs through the Unity Editor AssetDatabase.");
        yield break;
#endif
        Assert.That(productionPlayer, Is.Not.Null);
        Assert.That(productionBoard, Is.Not.Null);
        AppendCheckpoint("BuildLoopback:prefabs-loaded");
        GameObject playerPrefab = CreateIsolatedPlayerPrefab(productionPlayer, includeRemoteOwner);
        if (usePhysicalDeckPanel)
        {
            // Register the real asset prefab itself. NGO resolves this prefab by its baked
            // GlobalObjectIdHash; registering an instantiated clone produces an unresolvable hash.
            boardPrefab = productionBoard;
        }
        else
        {
            boardPrefab = UnityEngine.Object.Instantiate(productionBoard);
            objects.Add(boardPrefab);
            boardPrefab.name = "SingleCarryMovement network WoodenBoard fixture";
            boardPrefab.transform.position = Vector3.one * 10000f;
            DisableLookAtCamera(boardPrefab);
            SetPrefabHash(boardPrefab.GetComponent<NetworkObject>(), 0x77A50111);
        }
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "SingleCarryMovement network placement floor";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(40f, 1f, 40f);
        objects.Add(ground);
        fixtureAimCameraObject = new GameObject("SingleCarryMovement loopback main camera");
        fixtureAimCameraObject.tag = "MainCamera";
        Camera mainCamera = fixtureAimCameraObject.AddComponent<Camera>();
        mainCamera.nearClipPlane = 0.2f;
        objects.Add(fixtureAimCameraObject);
        ushort port = ReservePort();
        NetworkManager host = CreateManager("SingleCarryMovement network host", playerPrefab, boardPrefab, port);
        managers.Add(host);
        Assert.That(host.StartHost(), Is.True);
        AppendCheckpoint("BuildLoopback:host-started");
        host.SetSingleton();
        yield return WaitUntil(() => host.LocalClient?.PlayerObject != null, "network host PlayerNew spawn");
        RestrictInputToKeyboard(host);
        if (!includeRemoteOwner)
        {
            AppendCheckpoint("BuildLoopback:host-only-complete");
            yield break;
        }
        NetworkManager client = CreateManager("SingleCarryMovement network owner", playerPrefab, boardPrefab, port);
        managers.Add(client);
        Assert.That(client.StartClient(), Is.True);
        AppendCheckpoint("BuildLoopback:client-started");
        client.SetSingleton();
        yield return WaitUntil(() => client.IsConnectedClient && client.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 2, "remote owner PlayerNew spawn");
        AppendCheckpoint("BuildLoopback:client-connected");
        RestrictInputToKeyboard(host);
        RestrictInputToKeyboard(client);
        IgnoreReplicaCollisions(host, client);
        AppendCheckpoint("BuildLoopback:complete");
        // The prefab root and cloned action asset are fixture-owned and destroyed by TearDown.
    }

    private GameObject CreateIsolatedPlayerPrefab(GameObject productionPrefab, bool disableMotionForPeerSpawn)
    {
        PlayerInput sourceInput = productionPrefab.GetComponent<PlayerInput>();
        FieldInfo actionsField = typeof(PlayerInput).GetField("m_Actions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(sourceInput, Is.Not.Null);
        InputActionAsset productionActions = (InputActionAsset)actionsField.GetValue(sourceInput);
        isolatedActions = UnityEngine.Object.Instantiate(productionActions);
        isolatedPrefabRoot = new GameObject("SingleCarryMovement isolated PlayerNew prefab holder");
        isolatedPrefabRoot.SetActive(false);
        GameObject clone = UnityEngine.Object.Instantiate(productionPrefab, isolatedPrefabRoot.transform);
        actionsField.SetValue(clone.GetComponent<PlayerInput>(), isolatedActions);
        DisableLookAtCamera(clone);
        if (disableMotionForPeerSpawn)
        {
            if (FirstPersonType != null && clone.GetComponent(FirstPersonType) is Behaviour firstPerson)
                firstPerson.enabled = false;
            CharacterController characterController = clone.GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = false;
            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
                if (collider != null) collider.enabled = false;
        }
        return clone;
    }

    private static void DisableLookAtCamera(GameObject root)
    {
        Type lookAtCameraType = Type.GetType("LookAtCamera, Assembly-CSharp");
        if (lookAtCameraType == null) return;
        foreach (Component component in root.GetComponentsInChildren(lookAtCameraType, true))
            if (component is Behaviour behaviour) behaviour.enabled = false;
    }

    private void RestrictInputToKeyboard(NetworkManager manager)
    {
        foreach (NetworkObject player in manager.SpawnManager.SpawnedObjectsList)
        {
            Component input = player.GetComponent(InputNewType);
            PlayerInput playerInput = player.GetComponent<PlayerInput>();
            if (input == null || playerInput == null) continue;
            object actions = InputNewType.GetField("playerGameInputActions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input);
            if (actions == null) continue;
            InputActionAsset asset = (InputActionAsset)actions.GetType().GetProperty("asset").GetValue(actions);
            inputScope.RestrictToSyntheticKeyboard(asset, playerInput, keyboard);
        }
    }

    private NetworkManager CreateManager(string label, GameObject playerPrefab, GameObject boardPrefab, ushort port)
    {
        var root = new GameObject(label);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        roots.Add(root);
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = playerPrefab, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = boardPrefab });
        if (usesPhysicalDeckPanel)
        {
            NetworkPrefab registeredPanel = manager.NetworkConfig.Prefabs.Prefabs.Single(prefab => prefab.Prefab == boardPrefab);
            uint registeredAssetHash = GetNetworkObjectHash(registeredPanel.Prefab.GetComponent<NetworkObject>());
            uint registeredSourceHash = registeredPanel.SourcePrefabGlobalObjectIdHash;
            Assert.That(registeredPanel.Override, Is.EqualTo(NetworkPrefabOverride.None));
            Assert.That(registeredSourceHash, Is.EqualTo(registeredAssetHash),
                $"The registered real panel prefab source hash must equal the saved asset hash. asset={registeredAssetHash}; source={registeredSourceHash}; override={registeredPanel.Override}.");
            AppendCheckpoint($"physical-panel-registered assetHash={registeredAssetHash} sourceHash={registeredSourceHash} override={registeredPanel.Override}");
        }
        return manager;
    }

    private NetworkObject SpawnBoardFor(NetworkManager host, Vector3 position)
    {
        host.SetSingleton();
        GameObject instance = UnityEngine.Object.Instantiate(boardPrefab, position, Quaternion.identity);
        objects.Add(instance);
        NetworkObject no = instance.GetComponent<NetworkObject>();
        if (usesPhysicalDeckPanel)
        {
            uint prefabHash = GetNetworkObjectHash(boardPrefab.GetComponent<NetworkObject>());
            uint instanceHash = GetNetworkObjectHash(no);
            Assert.That(instanceHash, Is.EqualTo(prefabHash),
                $"The spawned physical panel instance must retain the same GlobalObjectIdHash as its registered asset prefab. asset={prefabHash}; instance={instanceHash}.");
            AppendCheckpoint($"physical-panel-spawn assetHash={prefabHash} instanceHash={instanceHash}");
        }
        no.Spawn();
        spawned.Add(no);
        return no;
    }

    private static bool InvokePickup(NetworkObject source, ulong ownerId)
    {
        Component resource = source.GetComponent(ResourceType);
        return (bool)ResourceType.GetMethod("TryCompleteNetworkPickup", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(resource, new object[] { ownerId, Vector3.zero, 0.3f });
    }

    private static float CarrySpeedPenalty(NetworkObject source) =>
        (float)ResourceType.GetMethod("CalculateCarryMovementSpeedPenalty", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(source.GetComponent(ResourceType), null);

    private static void ConfirmLocalPickup(NetworkManager owner, ulong sourceId, float movementSpeedPenalty)
    {
        NetworkObject source = Find(owner, sourceId);
        Component resource = source.GetComponent(ResourceType);
        MethodInfo confirm = InteractionType.GetMethod("ConfirmPickedUpObject", new[]
        {
            typeof(GameObject), PickableType, typeof(bool), typeof(float)
        });
        confirm.Invoke(owner.LocalClient.PlayerObject.GetComponent(InteractionType),
            new object[] { source.gameObject, resource, true, movementSpeedPenalty });
    }

    private static void AssertSingleCarryState(NetworkManager owner, NetworkObject source)
    {
        Component interaction = owner.LocalClient.PlayerObject.GetComponent(InteractionType);
        Assert.That(InteractionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(source.gameObject),
            "The actual PlayerNew interaction dependency must hold the spawned source.");
        Assert.That((bool)ResourceType.GetProperty("CanEnterSingleCarryPlacement").GetValue(source.GetComponent(ResourceType)), Is.True,
            "The network source must be eligible for the single-carry placement flow.");
    }

    private IEnumerator ConfigureAimCamera(NetworkObject player, float pitch)
    {
        Component interaction = player.GetComponent(InteractionType);
        Transform anchor = (Transform)InteractionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(interaction);
        Assert.That(anchor, Is.Not.Null);
        Assert.That(fixtureAimCameraObject, Is.Not.Null, "The loopback fixture must create a MainCamera before network player spawn.");
        GameObject aimCameraObject = new GameObject("SingleCarryMovement owner aim camera");
        objects.Add(aimCameraObject);
        fixtureAimCamera = aimCameraObject.AddComponent<Camera>();
        fixtureAimCamera.nearClipPlane = 0.2f;
        aimCameraObject.transform.SetParent(anchor, false);
        aimCameraObject.transform.localPosition = Vector3.zero;
        aimCameraObject.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        interaction.GetType().GetMethod("SetAimCamera").Invoke(interaction, new object[] { fixtureAimCamera });
        yield return null;
    }

    private Component AttachNetworkSimulator()
    {
        Type simulatorType = Type.GetType("Unity.Multiplayer.Tools.NetworkSimulator.Runtime.NetworkSimulator, Unity.Multiplayer.Tools.NetworkSimulator.Runtime");
        Assert.That(simulatorType, Is.Not.Null, "The installed NetworkSimulator assembly must resolve.");
        var go = new GameObject("SingleCarryMovement NetworkSimulator");
        objects.Add(go);
        Component instance = go.AddComponent(simulatorType);
        return instance;
    }

    private static void SetSimulatorPreset(Component target, int delayMs)
    {
        Type presetType = Type.GetType("Unity.Multiplayer.Tools.NetworkSimulator.Runtime.NetworkSimulatorPreset, Unity.Multiplayer.Tools.NetworkSimulator.Runtime");
        Type presetsType = Type.GetType("Unity.Multiplayer.Tools.NetworkSimulator.Runtime.NetworkSimulatorPresets, Unity.Multiplayer.Tools.NetworkSimulator.Runtime");
        object preset = delayMs == 0
            ? presetsType.GetField("None", BindingFlags.Public | BindingFlags.Static).GetValue(null)
            : presetType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static).Invoke(null,
                new object[] { $"SingleCarry {delayMs}ms", "Single Carry movement latency test", delayMs, 0, 0, 0 });
        target.GetType().GetProperty("ConnectionPreset").SetValue(target, preset);
    }

    private void QueueKeys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }

    private IEnumerator WaitForForwardMotion(Transform player, Vector3 start, Vector3 forward, float minimum, string label)
    {
        float deadline = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < deadline && ForwardDisplacement(player.position, start, forward) < minimum) yield return null;
        Assert.That(ForwardDisplacement(player.position, start, forward), Is.GreaterThanOrEqualTo(minimum), label);
    }

    private IEnumerator WaitForStableSpawn(NetworkObject player, string label)
    {
        Component firstPerson = player.GetComponent(FirstPersonType);
        Component playerInput = player.GetComponent(InputNewType);
        float deadline = Time.realtimeSinceStartup + 5f;
        float stableSince = -1f;
        Vector3 previousPosition = player.transform.position;
        Vector3 lastVelocity = Vector3.zero;
        Vector2 lastMoveInput = Vector2.zero;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            Vector3 currentPosition = player.transform.position;
            lastVelocity = (Vector3)FirstPersonType.GetProperty("HorizontalVelocity").GetValue(firstPerson);
            lastMoveInput = (Vector2)InputNewType.GetMethod("GetMoveVectorValue").Invoke(playerInput, null);
            bool grounded = (bool)FirstPersonType.GetField("Grounded").GetValue(firstPerson);
            bool stableFrame = grounded && lastVelocity.magnitude < 0.1f && lastMoveInput.sqrMagnitude < 0.0001f
                && HorizontalDistance(currentPosition, previousPosition) < 0.02f;
            if (stableFrame)
            {
                if (stableSince < 0f) stableSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - stableSince >= 0.25f)
                {
                    AppendCheckpoint($"{label}:stable position={currentPosition.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
                    yield break;
                }
            }
            else stableSince = -1f;
            previousPosition = currentPosition;
        }
        Assert.Fail($"{label} did not settle within 5s; grounded={(bool)FirstPersonType.GetField("Grounded").GetValue(firstPerson)}; "
            + $"position={player.transform.position}; horizontalVelocity={lastVelocity}; move={lastMoveInput}; "
            + $"nearby={DescribeNearbyColliders(player.transform)}");
    }

    private static Vector3 HorizontalForward(Vector3 forward)
    {
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    private static float ForwardDisplacement(Vector3 position, Vector3 start, Vector3 forward)
    {
        Vector3 displacement = position - start;
        displacement.y = 0f;
        return Vector3.Dot(displacement, forward);
    }

    private static string DescribeNearbyColliders(Transform player)
    {
        var descriptions = new List<string>();
        foreach (Collider collider in Physics.OverlapSphere(player.position + Vector3.up, 1.5f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (collider == null || collider.isTrigger) continue;
            NetworkObject networkObject = collider.GetComponentInParent<NetworkObject>();
            descriptions.Add($"{collider.transform.root.name}/{collider.name}:{collider.GetType().Name}:enabled={collider.enabled}:active={collider.gameObject.activeInHierarchy}:manager={networkObject?.NetworkManager?.name ?? "none"}");
        }
        return string.Join(";", descriptions);
    }

    private static IEnumerator WaitUntil(Func<bool> condition, string label)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out waiting for " + label);
    }

    private static Transform Ghost(Component controller)
    {
        object ghost = ControllerType.GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        return ((GameObject)ghost.GetType().GetProperty("Root").GetValue(ghost)).transform;
    }

    private static void SetPrefabHash(NetworkObject networkObject, uint hash) =>
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(networkObject, hash);

    private static uint GetNetworkObjectHash(NetworkObject networkObject) =>
        (uint)typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(networkObject);

    private static NetworkObject Find(NetworkManager manager, ulong id) =>
        manager?.SpawnManager?.SpawnedObjects.TryGetValue(id, out NetworkObject found) == true ? found : null;

    private static void IgnoreReplicaCollisions(NetworkManager host, NetworkManager client)
    {
        foreach (ulong id in host.ConnectedClientsIds)
        {
            NetworkObject authoritative = host.ConnectedClients[id].PlayerObject;
            NetworkObject replica = Find(client, authoritative.NetworkObjectId);
            if (replica == null) continue;
            foreach (Collider source in authoritative.GetComponentsInChildren<Collider>(true))
            foreach (Collider mirror in replica.GetComponentsInChildren<Collider>(true))
                if (source != null && mirror != null) Physics.IgnoreCollision(source, mirror, true);
        }
    }

    private static void DisableSourceMirrorCollisions(NetworkManager host, NetworkManager owner, NetworkObject ownerPlayer, ulong sourceId)
    {
        NetworkObject ownerSource = Find(owner, sourceId);
        NetworkObject serverSource = Find(host, sourceId);
        AssertPlacementShapePresent(ownerSource, serverSource);
        Collider[] ownerSourceColliders = PlacementShapeColliders(ownerSource);
        Collider[] serverSourceColliders = PlacementShapeColliders(serverSource);
        foreach (Collider ownerCollider in ownerSourceColliders)
        foreach (Collider serverCollider in serverSourceColliders)
            Physics.IgnoreCollision(ownerCollider, serverCollider, true);
        foreach (Collider collider in Find(host, ownerPlayer.NetworkObjectId).GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Collider collider in Find(owner, host.LocalClient.PlayerObject.NetworkObjectId).GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    private static void AssertPlacementShapePresent(NetworkObject ownerSource, NetworkObject serverSource)
    {
        Assert.That(ownerSource, Is.Not.Null, "The owner's carried source replica must be present.");
        Assert.That(serverSource, Is.Not.Null, "The server's authoritative source must be present.");
        Assert.That(PlacementShapeColliders(ownerSource).Length, Is.GreaterThan(0),
            "The owner source replica must retain active, non-trigger colliders for local placement validation.");
        Assert.That(PlacementShapeColliders(serverSource).Length, Is.GreaterThan(0),
            "The authoritative server source must retain active, non-trigger colliders for geometry validation.");
    }

    private static void SetPlacementCollidersEnabled(Collider[] colliders, bool enabled)
    {
        foreach (Collider collider in colliders)
            if (collider != null) collider.enabled = enabled;
    }

    private static Collider[] PlacementShapeColliders(NetworkObject source) => source == null
        ? Array.Empty<Collider>()
        : source.GetComponentsInChildren<Collider>(true)
            .Where(collider => collider != null && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger)
            .ToArray();

    private static string DescribePlacementValidation(object validation)
    {
        if (validation == null) return "validation=<null>";
        Type type = validation.GetType();
        object status = type.GetProperty("Status")?.GetValue(validation);
        object depth = type.GetProperty("PenetrationDepth")?.GetValue(validation);
        Collider blocker = type.GetProperty("BlockingCollider")?.GetValue(validation) as Collider;
        return $"validationStatus={status}; penetrationDepth={depth}; blockingCollider={blocker?.name ?? "<none>"}";
    }

    private static string DescribeSurfaceConfirmState(Component controller)
    {
        Type type = controller.GetType();
        object ReadField(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(controller);
        object rendered = ReadField("lastRenderedPose");
        Type renderedType = rendered?.GetType();
        Collider surface = renderedType?.GetProperty("SurfaceCollider")?.GetValue(rendered) as Collider;
        NetworkObject surfaceObject = surface != null ? surface.GetComponentInParent<NetworkObject>() : null;
        Component interaction = controller.GetComponent(InteractionType);
        GameObject heldSource = interaction != null
            ? InteractionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null) as GameObject
            : null;
        NetworkObject heldSourceObject = heldSource != null ? heldSource.GetComponent<NetworkObject>() : null;
        return $"networkSurfaceAnchorRejected={ReadField("networkSurfaceAnchorRejected")}; hasNetworkSurfaceAnchor={ReadField("hasNetworkSurfaceAnchor")}; "
            + $"anchoredSurface={((NetworkObject)ReadField("anchoredSurface"))?.NetworkObjectId.ToString() ?? "<null>"}; "
            + $"lastRenderedSurface={surface?.name ?? "<null>"}; surfaceRoot={surface?.transform.root.name ?? "<null>"}; surfaceParent={surface?.transform.parent?.name ?? "<null>"}; "
            + $"lastRenderedSurfaceNetworkObject={surfaceObject?.NetworkObjectId.ToString() ?? "<static>"}; surfaceManager={surfaceObject?.NetworkManager?.name ?? "<none>"}; "
            + $"surfaceManagerLocalClientId={surfaceObject?.NetworkManager?.LocalClientId}; surfaceManagerIsServer={surfaceObject?.NetworkManager?.IsServer}; "
            + $"heldSourceObject={heldSourceObject?.NetworkObjectId.ToString() ?? "<null>"}; heldSourceManager={heldSourceObject?.NetworkManager?.name ?? "<none>"}; "
            + $"sameAsHeldSource={surfaceObject != null && ReferenceEquals(surfaceObject, heldSourceObject)}; "
            + $"lastRenderedPosition={renderedType?.GetProperty("Position")?.GetValue(rendered)}; lastRenderedStatus={renderedType?.GetProperty("Status")?.GetValue(rendered)}";
    }

    private static void DisableNonOwnerSourceMirrors(NetworkManager owner, NetworkManager[] peers, ulong sourceId)
    {
        GameObject held = (GameObject)InteractionType.GetMethod("GetPickedUpGameObject")
            .Invoke(owner.LocalClient.PlayerObject.GetComponent(InteractionType), null);
        foreach (NetworkManager peer in peers)
        {
            NetworkObject mirror = Find(peer, sourceId);
            if (mirror == null) continue;
            bool enableForLocalGeometry = peer == owner && held == mirror.gameObject;
            foreach (Collider collider in mirror.GetComponentsInChildren<Collider>(true))
                collider.enabled = enableForLocalGeometry;
        }
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }

    private static void DisableAllExcept(NetworkManager manager, NetworkObject controlledPlayer)
    {
        if (manager?.SpawnManager == null) return;
        foreach (NetworkObject player in manager.SpawnManager.SpawnedObjectsList)
        {
            if (player == null || !player.IsPlayerObject
                || controlledPlayer != null && player.NetworkObjectId == controlledPlayer.NetworkObjectId) continue;
            Component playerInputNew = player.GetComponent(InputNewType);
            if (playerInputNew != null)
            {
                InputNewType.GetMethod("DisposeInput", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(playerInputNew, null);
                if (playerInputNew is Behaviour playerInputBehaviour) playerInputBehaviour.enabled = false;
            }
            PlayerInput playerInput = player.GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                if (playerInput.inputIsActive) playerInput.DeactivateInput();
                playerInput.actions?.Disable();
            }
            if (FirstPersonType != null && player.GetComponent(FirstPersonType) is Behaviour firstPerson) firstPerson.enabled = false;
            CharacterController characterController = player.GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = false;
            foreach (Collider collider in player.GetComponentsInChildren<Collider>(true))
                if (collider != null) collider.enabled = false;
        }
    }

    private static void DisableServerMirrorLocomotion(NetworkObject serverPlayer)
    {
        if (serverPlayer == null) return;
        if (FirstPersonType != null && serverPlayer.GetComponent(FirstPersonType) is Behaviour firstPerson) firstPerson.enabled = false;
        CharacterController characterController = serverPlayer.GetComponent<CharacterController>();
        if (characterController != null) characterController.enabled = false;
    }

    private static void EnableControlledPlayerMotion(NetworkObject controlledPlayer)
    {
        Assert.That(controlledPlayer, Is.Not.Null);
        foreach (Collider collider in controlledPlayer.GetComponentsInChildren<Collider>(true))
            if (collider != null) collider.enabled = true;
        CharacterController characterController = controlledPlayer.GetComponent<CharacterController>();
        Assert.That(characterController, Is.Not.Null);
        characterController.enabled = true;
        if (FirstPersonType != null && controlledPlayer.GetComponent(FirstPersonType) is Behaviour firstPerson)
            firstPerson.enabled = true;
    }

    private static ushort ReservePort()
    {
        using (var socket = new UdpClient(0)) return (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Exception traceExportFailure = null;
        try
        {
            try { ExportFallbackTrace(); }
            catch (Exception exception) { traceExportFailure = exception; }
            DisposeActionMaps();
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (mouse != null) { InputSystem.RemoveDevice(mouse); mouse = null; }
            if (simulator != null)
            {
                SetSimulatorPreset(simulator, 0);
                if (simulator is Behaviour simulatorBehaviour) simulatorBehaviour.enabled = false;
            }
            List<GameObject> networkCopies = CaptureFixtureNetworkCopies();
            foreach (GameObject networkCopy in networkCopies)
            {
                NetworkObject networkObject = networkCopy != null ? networkCopy.GetComponent<NetworkObject>() : null;
                if (networkObject != null && networkObject.IsSpawned && networkObject.NetworkManager != null
                    && networkObject.NetworkManager.IsServer)
                    networkObject.Despawn(true);
            }
            for (int i = managers.Count - 1; i >= 0; i--)
                if (managers[i] != null && managers[i].IsListening) managers[i].Shutdown(true);
            float deadline = Time.realtimeSinceStartup + 15f;
            while (managers.Any(manager => manager != null && (manager.IsListening || manager.ShutdownInProgress))
                && Time.realtimeSinceStartup < deadline) yield return null;
            bool shutdownComplete = managers.All(manager => manager == null || (!manager.IsListening && !manager.ShutdownInProgress));
            foreach (GameObject root in roots) if (root != null) UnityEngine.Object.Destroy(root);
            foreach (GameObject networkCopy in networkCopies) if (networkCopy != null) UnityEngine.Object.Destroy(networkCopy);
            foreach (GameObject obj in objects) if (obj != null) UnityEngine.Object.Destroy(obj);
            if (isolatedPrefabRoot != null) UnityEngine.Object.Destroy(isolatedPrefabRoot);
            if (isolatedActions != null) { isolatedActions.Disable(); UnityEngine.Object.Destroy(isolatedActions); }
            yield return null;
            Assert.That(shutdownComplete, Is.True, "Loopback NGO managers must finish shutdown before fixture teardown completes.");
            if (traceExportFailure != null)
                throw new InvalidOperationException("Fallback trace export failed after fixture cleanup completed.", traceExportFailure);
        }
        finally
        {
            try
            {
                if (simulator != null) SetSimulatorPreset(simulator, 0);
                if (capturedSingleton)
                {
                    if (previousSingleton != null) previousSingleton.SetSingleton();
                    else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
                }
            }
            finally
            {
                inputScope?.DisableForTest();
                inputScope?.RestoreOriginalMapState();
                inputScope?.RestoreInputSettings();
                inputScope = null;
                managers.Clear(); roots.Clear(); objects.Clear(); spawned.Clear();
                ResetTrace();
                simulator = null; boardPrefab = null; isolatedActions = null; isolatedPrefabRoot = null; mouse = null;
                previousSingleton = null; capturedSingleton = false;
            }
        }
    }

    private void DisposeActionMaps()
    {
        foreach (NetworkManager manager in managers)
        {
            if (manager?.SpawnManager == null) continue;
            foreach (NetworkObject player in manager.SpawnManager.SpawnedObjectsList)
            {
                Component input = player != null ? player.GetComponent(InputNewType) : null;
                if (input != null) InputNewType.GetMethod("DisposeInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, null);
            }
        }
    }

    private List<GameObject> CaptureFixtureNetworkCopies()
    {
        var copies = new List<GameObject>();
        var seen = new HashSet<int>();
        foreach (NetworkObject candidate in Resources.FindObjectsOfTypeAll<NetworkObject>())
        {
            if (candidate == null || !candidate.IsSpawned || !candidate.gameObject.scene.IsValid()
                || !candidate.gameObject.scene.isLoaded || !managers.Contains(candidate.NetworkManager)
                || !seen.Add(candidate.gameObject.GetInstanceID()))
                continue;
            copies.Add(candidate.gameObject);
        }
        return copies;
    }
}
