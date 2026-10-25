using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly Type CarryPlacementOrientationType = Type.GetType("CarryPlacementOrientation, Assembly-CSharp");
    private const int SingleCarryContactIsolationPriority = 64;

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

    [UnityTest]
    public IEnumerator RemoteControllerPlacementRebasesPoseWhenSameNetworkSurfaceTranslates() =>
        RunRemoteControllerPlacementRebase(0);

    [UnityTest]
    public IEnumerator RemoteControllerPlacementRebasesVisibleTargetMovedOffAimRay() =>
        RunRemoteControllerPlacementRebase(1);

    [UnityTest]
    public IEnumerator RemoteControllerPlacementRebasesPoseWhenSameNetworkSurfaceRotates() =>
        RunRemoteControllerPlacementRebase(2);

    [UnityTest]
    public IEnumerator RemoteControllerPlacementRebasesNetworkCharacterColliderWithoutRigidbody() =>
        RunRemoteControllerPlacementRebase(9);

    [UnityTest]
    public IEnumerator RemoteControllerRejectsSameTargetWhenContactIsLostBeforeServerReceipt() =>
        RunRemoteControllerPlacementRebase(3);

    [UnityTest]
    public IEnumerator RemoteControllerRejectsSameTargetWhenServerLineOfSightIsBlocked() =>
        RunRemoteControllerPlacementRebase(4);

    [UnityTest]
    public IEnumerator RemoteControllerRejectsServerRayOriginInsideSolidObstacle() =>
        RunRemoteControllerPlacementRebase(5);

    [UnityTest]
    public IEnumerator RemoteControllerRejectsSameTargetWhenItMovesOutOfRangeBeforeServerReceipt() =>
        RunRemoteControllerPlacementRebase(6);

    [UnityTest]
    public IEnumerator RemoteControllerRejectsRespawnedNetworkObjectIdentityForCachedSurface() =>
        RunRemoteControllerPlacementRebase(7);

    [UnityTest]
    public IEnumerator RemoteControllerFAndECommitPreparesRealNailExactlyOnce()
    {
        return RunRemoteControllerPlacementRebase(0, true);
    }

    [UnityTest]
    public IEnumerator RemoteOwnerNailDropUsesServerConfiguredVelocityChange()
    {
        return RunRemoteControllerPlacementRebase(0, true, false, true);
    }

    [UnityTest]
    public IEnumerator RemoteOwnerHeavyBoardDropUsesServerConfiguredVelocityChange()
    {
        return RunRemoteControllerPlacementRebase(0, false, false, true);
    }

    [UnityTest]
    public IEnumerator RemoteVerticalNailPreviewRemainsStableOnMovedTiltedWoodenBoard()
    {
        return RunRemoteControllerPlacementRebase(1, true, true);
    }

    [UnityTest]
    public IEnumerator RemoteOwnerRotationStepsSnapWorldYawAndCommitTheExactPreviewRotation()
    {
        return RunRemoteControllerPlacementRebase(0, false, false, false, true);
    }

    [UnityTest]
    public IEnumerator RemoteControllerRejectsForgedAnchoredRequestFieldsAndCanRetry()
    {
        return RunRemoteControllerPlacementRebase(8);
    }

    [UnityTest]
    public IEnumerator SurfaceContactGeometryRejectsDisabledSourceColliders()
    {
        GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
        source.name = "Stage 6 disabled-source geometry fixture";
        runtimeObjects.Add(source);
        Collider[] sourceColliders = source.GetComponentsInChildren<Collider>(true);
        Assert.That(sourceColliders.Length, Is.GreaterThan(0));
        foreach (Collider collider in sourceColliders) collider.enabled = false;

        var surface = new GameObject("Stage 6 disabled-source geometry surface");
        runtimeObjects.Add(surface);
        BoxCollider surfaceCollider = surface.AddComponent<BoxCollider>();
        Type geometryType = Type.GetType("SingleCarryPlacementGeometrySolver, Assembly-CSharp");
        object geometry = Activator.CreateInstance(geometryType, new object[] { source });
        object pose = geometryType.GetMethod("CalculateAtSurfaceContact").Invoke(geometry, new object[]
        {
            new Vector3(0f, 1f, 0f), Vector3.up, Quaternion.identity, surfaceCollider
        });
        Assert.That(pose.GetType().GetProperty("Status").GetValue(pose).ToString(), Is.EqualTo("NoPhysicalColliders"));
        yield return null;
    }

    private IEnumerator RunRemoteControllerPlacementRebase(int motion, bool useMountingNail = false,
        bool probeUnsyncedSurfaceMovement = false, bool testNetworkDropThrow = false, bool probeSteppedRotation = false)
    {
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        priorSingleton = NetworkManager.Singleton; capturedSingleton = true;
        GameObject cameraObject = new GameObject("Stage 6 moving-surface aim camera");
        runtimeObjects.Add(cameraObject);
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.2f;

        UnityEngine.Object resourceProfileObject = ScriptableObject.CreateInstance(BaseResourceProfileType);
        assets.Add(resourceProfileObject);
        resourceProfileObject.GetType().GetField("canBeCarried").SetValue(resourceProfileObject, true);
        resourceProfileObject.GetType().GetField("allowMultipleCarriers").SetValue(resourceProfileObject, false);
        GameObject playerPrefab = CreatePlayerPrefab();
        if (testNetworkDropThrow)
            InteractionType.GetField("singleCarryDropThrowMetersPerSecond", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(playerPrefab.GetComponent(InteractionType), 2.25f);
        GameObject sourcePrefab;
        if (useMountingNail)
        {
#if UNITY_EDITOR
            sourcePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
            Assert.That(sourcePrefab, Is.Not.Null, "The F/E nail integration fixture needs the saved Stage1 nail prefab.");
            additionalFixturePrefabHashes.Add(GetPrefabHash(sourcePrefab.GetComponent<NetworkObject>()));
#else
            Assert.Ignore("The actual mounting-nail fixture requires Unity Editor AssetDatabase access.");
            yield break;
#endif
        }
        else
        {
            sourcePrefab = CreateActualWoodenBoardPrefabForFixture();
        }
        if (probeSteppedRotation)
        {
            Component orientation = sourcePrefab.GetComponent(CarryPlacementOrientationType)
                ?? sourcePrefab.AddComponent(CarryPlacementOrientationType);
            CarryPlacementOrientationType.GetField("offsetEulerDegrees", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(orientation, new Vector3(0f, 90f, 0f));
        }
        bool useNoRigidbodySurface = motion == 9;
        GameObject surfacePrefab = probeUnsyncedSurfaceMovement || probeSteppedRotation
            ? sourcePrefab
            : useNoRigidbodySurface
            ? CreateNetworkCharacterSurfacePrefab(0x61A60042u)
            : CreateSourcePrefab("Stage 6 moving network surface", BaseResourceType,
                resourceProfileObject, 0x61A60041);
        if (probeUnsyncedSurfaceMovement || probeSteppedRotation) additionalFixturePrefabHashes.Add(0x61A60031u);
        if (!probeUnsyncedSurfaceMovement && !probeSteppedRotation)
            additionalFixturePrefabHashes.Add(useNoRigidbodySurface ? 0x61A60042u : 0x61A60041u);
        if (!useNoRigidbodySurface && !probeUnsyncedSurfaceMovement && !probeSteppedRotation)
            surfacePrefab.transform.localScale = new Vector3(1.5f, 1.5f, 0.12f);
        GameObject[] fixtureNetworkPrefabs = sourcePrefab == surfacePrefab
            ? new[] { sourcePrefab }
            : new[] { sourcePrefab, surfacePrefab };

        ushort port = ReservePort();
        NetworkManager host = CreateManager("Stage 6 moving-surface host", playerPrefab,
            fixtureNetworkPrefabs, port);
        Assert.That(host.StartHost(), Is.True); host.SetSingleton();
        yield return Until(() => host.LocalClient?.PlayerObject != null, "moving-surface host player");
        NetworkManager owner = CreateManager("Stage 6 moving-surface owner", playerPrefab,
            fixtureNetworkPrefabs, port);
        Assert.That(owner.StartClient(), Is.True);
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 2, "moving-surface remote owner");
        NetworkObject ownerPlayer = owner.LocalClient.PlayerObject;
        DisposeFixtureInputMaps(null);
        foreach (NetworkClient connected in host.ConnectedClientsList)
            if (connected.ClientId != host.LocalClientId && connected.PlayerObject != null)
                ((Behaviour)connected.PlayerObject.GetComponent(InteractionType)).enabled = false;

        GameObject source = UnityEngine.Object.Instantiate(sourcePrefab);
        runtimeObjects.Add(source);
        source.transform.position = new Vector3(30f, 4f, 30f);
        NetworkObject sourceObject = source.GetComponent<NetworkObject>();
        spawnedObjects.Add(sourceObject);
        sourceObject.Spawn();

        GameObject surface = UnityEngine.Object.Instantiate(surfacePrefab);
        runtimeObjects.Add(surface);
        Transform aimAnchor = GetAimAnchor(ownerPlayer);
        Vector3 initialSurfacePosition = aimAnchor.position + aimAnchor.forward * 1.25f;
        surface.transform.SetPositionAndRotation(initialSurfacePosition, Quaternion.identity);
        NetworkObject surfaceObject = surface.GetComponent<NetworkObject>();
        spawnedObjects.Add(surfaceObject);
        surfaceObject.Spawn();
        yield return Until(() => Find(owner, sourceObject.NetworkObjectId) != null
            && Find(owner, surfaceObject.NetworkObjectId) != null, "source and target network surfaces replicated");
        DisableControllerFixtureMirrorColliders(host, owner, ownerPlayer, sourceObject.NetworkObjectId);

        Assert.That(InvokePickup(source, owner.LocalClientId), Is.True);
        ConfirmLocalPickup(owner, sourceObject.NetworkObjectId);
        yield return Until(() => sourceObject.OwnerClientId == owner.LocalClientId
            && GetHeld(ownerPlayer) == Find(owner, sourceObject.NetworkObjectId).gameObject,
            "remote owner holds the real Wooden Board");
        DisableControllerFixtureMirrorColliders(host, owner, ownerPlayer, sourceObject.NetworkObjectId);

        if (testNetworkDropThrow)
        {
            NetworkObject thrownOwnerSource = Find(owner, sourceObject.NetworkObjectId);
            Rigidbody thrownServerBody = source.GetComponent<Rigidbody>();
            Rigidbody thrownOwnerBody = thrownOwnerSource.GetComponent<Rigidbody>();
            Collider[] serverSourceColliders = source.GetComponentsInChildren<Collider>(true);
            Collider[] ownerSourceColliders = thrownOwnerSource.GetComponentsInChildren<Collider>(true);
            SetCollidersEnabled(serverSourceColliders, false);
            SetCollidersEnabled(ownerSourceColliders, false);
            SetCollidersEnabled(surface.GetComponentsInChildren<Collider>(true), false);
            SetCollidersEnabled(Find(host, surfaceObject.NetworkObjectId).GetComponentsInChildren<Collider>(true), false);
            SetCollidersEnabled(Find(owner, surfaceObject.NetworkObjectId).GetComponentsInChildren<Collider>(true), false);
            float expectedMass = useMountingNail ? 0.1f : 20f;
            SetFixtureNormalMass(source, expectedMass);
            SetFixtureNormalMass(thrownOwnerSource.gameObject, expectedMass);
            Assert.That(thrownServerBody.mass, Is.EqualTo(expectedMass).Within(0.001f),
                useMountingNail ? "The production mounting nail remains a light item." : "The real Wooden Board is configured heavy in this fixture.");

            NetworkObject serverPlayer = host.ConnectedClientsList
                .Single(client => client.ClientId == owner.LocalClientId).PlayerObject;
            Vector3 expectedDropPosition = serverPlayer.transform.position + serverPlayer.transform.forward + Vector3.up * 0.5f;
            Component ownerInteraction = ownerPlayer.GetComponent(InteractionType);
            Assert.That((bool)InteractionType.GetMethod("DropObject").Invoke(ownerInteraction, null), Is.True,
                "The actual remote owner's ordinary drop must dispatch the generated throw ServerRpc.");
            ulong serverClientId = NetworkManager.ServerClientId;
            yield return Until(() => !IsPickedUp(Find(host, sourceObject.NetworkObjectId))
                && Find(host, sourceObject.NetworkObjectId).OwnerClientId == serverClientId
                && !Find(host, sourceObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic,
                "server validates the remote drop, takes ownership, and restores dynamic physics");
            Vector3 firstObservedHandoffPose = source.transform.position;
            float firstObservedHandoffFixedTime = Time.fixedTime;
            SetCollidersEnabled(source.GetComponentsInChildren<Collider>(true), false);
            SetCollidersEnabled(thrownOwnerSource.GetComponentsInChildren<Collider>(true), false);
            yield return new WaitForFixedUpdate();
            Vector3 authoritativeForward = serverPlayer.transform.forward;
            Assert.That(Vector3.Dot(thrownServerBody.linearVelocity, authoritativeForward), Is.EqualTo(2.25f).Within(0.15f),
                "The server applies exactly one profile-configured Δv after the remote RPC.");
            Assert.That(thrownServerBody.mass, Is.EqualTo(expectedMass).Within(0.001f),
                "The authoritative server body keeps its test-configured mass after carry physics is restored.");
            Assert.That(thrownOwnerBody.mass, Is.EqualTo(expectedMass).Within(0.001f),
                "The client mirror retains the matching normal mass after its carry state is restored.");
            float physicsElapsedAfterHandoff = Time.fixedTime - firstObservedHandoffFixedTime;
            float expectedForwardTravel = 2.25f * physicsElapsedAfterHandoff;
            float actualForwardTravel = Vector3.Dot(source.transform.position - firstObservedHandoffPose, authoritativeForward);
            Assert.That(actualForwardTravel, Is.EqualTo(expectedForwardTravel).Within(0.06f),
                $"The authoritative pose advances at the configured velocity after handoff; physics elapsed={physicsElapsedAfterHandoff:F3}s.");
            Assert.That(Vector3.Distance(firstObservedHandoffPose, expectedDropPosition), Is.LessThan(0.4f),
                "The first observed authoritative pose remains near the ordinary forward drop offset, allowing RPC delivery and intervening physics steps.");
            Assert.That(Vector3.Distance(source.transform.position, new Vector3(30f, 4f, 30f)), Is.GreaterThan(1f),
                "Ownership handoff does not snap the object back to its pre-pickup world pose.");
            Assert.That(thrownOwnerBody.isKinematic, Is.True,
                "The former owner remains a kinematic mirror after server ownership transfer.");

            Rigidbody lateServerBody = Find(host, sourceObject.NetworkObjectId).GetComponent<Rigidbody>();
            float forwardSpeedBeforeRejectedThrow = Vector3.Dot(lateServerBody.linearVelocity, authoritativeForward);
            MethodInfo throwServerRpc = thrownOwnerSource.GetComponent(BaseResourceType).GetType().GetMethod(
                "RequestSingleCarryThrowServerRpc", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(throwServerRpc, Is.Not.Null, "The production throw RPC is available for an unauthorized repeat attempt.");
            throwServerRpc.Invoke(thrownOwnerSource.GetComponent(BaseResourceType), new object[] { default(ServerRpcParams) });
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(IsPickedUp(Find(host, sourceObject.NetworkObjectId)), Is.False,
                "A former holder's repeated RPC cannot reacquire or release the already-dropped object.");
            Assert.That(Find(host, sourceObject.NetworkObjectId).OwnerClientId, Is.EqualTo(serverClientId),
                "A rejected repeated throw does not change server ownership.");
            Assert.That(Vector3.Dot(lateServerBody.linearVelocity, authoritativeForward),
                Is.EqualTo(forwardSpeedBeforeRejectedThrow).Within(0.15f),
                "A rejected repeated throw does not apply a second configured Δv.");

            Vector3 kickedSnapshotPose = source.transform.position;
            lateServerBody.isKinematic = true;
            lateServerBody.useGravity = false;
            NetworkManager lateJoin = CreateManager("Stage 6 post-throw late join", playerPrefab,
                fixtureNetworkPrefabs, port);
            Assert.That(lateJoin.StartClient(), Is.True);
            yield return Until(() => lateJoin.IsConnectedClient && lateJoin.LocalClient?.PlayerObject != null
                && host.ConnectedClientsIds.Count == 3, "post-throw late-joining client connects");
            NetworkObject lateSource = null;
            yield return Until(() => (lateSource = Find(lateJoin, sourceObject.NetworkObjectId)) != null,
                "late-joining client receives the thrown world object snapshot");
            foreach (Collider collider in lateSource.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            yield return Until(() => Vector3.Distance(lateSource.transform.position, kickedSnapshotPose) < 0.05f,
                "late-joining client receives the stopped post-throw authoritative pose");
            Assert.That(lateSource.OwnerClientId, Is.EqualTo(serverClientId));
            Assert.That(lateSource.GetComponent<Rigidbody>().isKinematic, Is.True,
                "Late-joining clients keep the server-owned object as a kinematic mirror.");

            Assert.That(InvokePickup(source, host.LocalClientId), Is.True, "Host can pick up the same loose nail for the host path control.");
            ConfirmLocalPickup(host, sourceObject.NetworkObjectId);
            SetFixtureNormalMass(source, expectedMass);
            SetCollidersEnabled(serverSourceColliders, false);
            SetCollidersEnabled(thrownOwnerSource.GetComponentsInChildren<Collider>(true), false);
            Component hostInteraction = host.LocalClient.PlayerObject.GetComponent(InteractionType);
            Assert.That((bool)InteractionType.GetMethod("DropObject").Invoke(hostInteraction, null), Is.True,
                "The host's ordinary drop uses the same explicit throw path.");
            SetCollidersEnabled(serverSourceColliders, false);
            SetCollidersEnabled(thrownOwnerSource.GetComponentsInChildren<Collider>(true), false);
            yield return new WaitForFixedUpdate();
            Assert.That(Vector3.Dot(thrownServerBody.linearVelocity, host.LocalClient.PlayerObject.transform.forward),
                Is.EqualTo(2.25f).Within(0.15f), "The host and remote owner receive the same configured Δv.");
            Assert.That(thrownServerBody.mass, Is.EqualTo(expectedMass).Within(0.001f),
                "The host release keeps the fixture's normal body mass after shared-carry physics restoration.");
            Assert.That(sourceObject.OwnerClientId, Is.EqualTo(serverClientId));
            yield break;
        }

        NetworkObject ownerSurface = Find(owner, surfaceObject.NetworkObjectId);
        NetworkObject serverSurface = Find(host, surfaceObject.NetworkObjectId);
        Assert.That(ownerSurface.NetworkObjectId, Is.EqualTo(serverSurface.NetworkObjectId));
        Collider ownerSurfaceCollider = ownerSurface.GetComponent<Collider>();
        Collider serverSurfaceCollider = serverSurface.GetComponent<Collider>();
        Collider[] ownerSurfaceColliders = ownerSurface.GetComponentsInChildren<Collider>(true);
        Collider[] serverSurfaceColliders = serverSurface.GetComponentsInChildren<Collider>(true);
        Rigidbody ownerSurfaceBody = ownerSurface.GetComponent<Rigidbody>();
        Rigidbody serverSurfaceBody = serverSurface.GetComponent<Rigidbody>();
        Assert.That(ownerSurfaceCollider, Is.Not.Null);
        Assert.That(serverSurfaceCollider, Is.Not.Null);
        if (useNoRigidbodySurface)
        {
            Assert.That(ownerSurfaceBody, Is.Null, "The character-collider target intentionally has no Rigidbody.");
            Assert.That(serverSurfaceBody, Is.Null, "The character-collider target intentionally has no Rigidbody.");
        }
        else
        {
            Assert.That(ownerSurfaceBody, Is.Not.Null);
            Assert.That(serverSurfaceBody, Is.Not.Null);
            ownerSurfaceBody.useGravity = false;
            ownerSurfaceBody.isKinematic = true;
            serverSurfaceBody.useGravity = false;
            serverSurfaceBody.isKinematic = true;
        }

        if (probeUnsyncedSurfaceMovement || probeSteppedRotation)
        {
            cameraObject.transform.SetPositionAndRotation(
                probeSteppedRotation ? aimAnchor.position : initialSurfacePosition + Vector3.up * 1.25f,
                probeSteppedRotation ? Quaternion.Euler(0f, 12.4f, 0f)
                    : Quaternion.LookRotation(Vector3.down, Vector3.forward));
        }
        else cameraObject.transform.SetPositionAndRotation(aimAnchor.position, aimAnchor.rotation);
        InteractionType.GetMethod("SetAimCamera").Invoke(ownerPlayer.GetComponent(InteractionType), new object[] { camera });
        Component ownerInput = ownerPlayer.GetComponent(PlayerInputType);
        ((Behaviour)ownerInput).enabled = true;
        Component controller = ownerPlayer.GetComponent(PlacementControllerType);
        Component preview = Preview(ownerPlayer);
        SetControllerRequestId(controller, 0);
        syntheticKeyboard = InputSystem.AddDevice<Keyboard>();

        // Only the owner's rendered network copy participates while its cached preview is sampled.
        SetCollidersEnabled(serverSurfaceColliders, false);
        SetCollidersEnabled(ownerSurfaceColliders, true);
        ownerSurface.transform.SetPositionAndRotation(initialSurfacePosition, Quaternion.identity);
        if (ownerSurfaceBody != null) ownerSurfaceBody.isKinematic = true;
        if (serverSurfaceBody != null) serverSurfaceBody.isKinematic = true;
        Physics.SyncTransforms();
        PressKey(Key.F);
        yield return Until(() => (bool)PlacementControllerType.GetProperty("IsActive").GetValue(controller),
            "remote owner enters placement");
        MethodInfo refresh = PlacementControllerType.GetMethod("RefreshPreview", BindingFlags.Instance | BindingFlags.NonPublic);
        Quaternion expectedStepRotationForRemote = Quaternion.identity;
        if (probeSteppedRotation)
        {
            InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState(Key.UpArrow));
            InputSystem.Update();
            InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState(Key.UpArrow, Key.LeftCtrl, Key.RightArrow));
            InputSystem.Update();
            InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState());
            InputSystem.Update();
            object[] aimArgs = { default(Ray), null, Quaternion.identity };
            MethodInfo getAimRay = InteractionType.GetMethod("TryGetAimRay", new[]
            {
                typeof(Ray).MakeByRefType(), typeof(Camera).MakeByRefType(), typeof(Quaternion).MakeByRefType()
            });
            Assert.That((bool)getAimRay.Invoke(ownerPlayer.GetComponent(InteractionType), aimArgs), Is.True);
            Quaternion baseYaw = (Quaternion)aimArgs[2];
            float yawDegrees = Mathf.Repeat(Mathf.Atan2((baseYaw * Vector3.forward).x,
                (baseYaw * Vector3.forward).z) * Mathf.Rad2Deg, 360f);
            expectedStepRotationForRemote = Quaternion.AngleAxis(1f, Vector3.up)
                * Quaternion.AngleAxis(1f, Vector3.right)
                * Quaternion.Euler(0f, Mathf.FloorToInt(yawDegrees + .5f) % 360, 0f)
                * Quaternion.Euler(0f, 90f, 0f);
            refresh.Invoke(controller, null);
            Assert.That(Quaternion.Angle(GetControllerGhost(controller).transform.rotation, expectedStepRotationForRemote), Is.LessThan(.1f),
                "The spawned non-server owner applies X and Ctrl+Right Y world-degree steps on integer camera yaw, without Z.");
        }
        if (probeUnsyncedSurfaceMovement)
            SetRotationCounter(controller, "worldXDegrees", 90);
        refresh.Invoke(controller, null);
        Assert.That(PlacementControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(),
            Is.EqualTo("SurfaceSupported"), "The owner must sample a real contact on the spawned target.");
        if (probeUnsyncedSurfaceMovement)
            Assert.That(Vector3.Dot(GetControllerGhost(controller).transform.forward, Vector3.down), Is.GreaterThan(0.99f),
                "The actual mounting nail tip must point down for this top-down preview regression.");
        NetworkObject selectedAnchor = (NetworkObject)PlacementControllerType.GetField("anchoredSurface",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        Assert.That(selectedAnchor, Is.EqualTo(ownerSurface));
        Assert.That(selectedAnchor.NetworkManager, Is.EqualTo(owner),
            "The preview must anchor to the requesting owner's target copy, not a foreign loopback collider.");
        Transform ghost = GetControllerGhost(controller).transform;
        Vector3 renderedPosition = ghost.position;
        Quaternion renderedRotation = ghost.rotation;

        // The same NetworkObject moves after preview sampling. Keep the owner render copy frozen
        // and make the authoritative collider the only visible physics target for server receipt.
        Vector3 movedSurfacePosition = motion == 0 || motion == 9 ? initialSurfacePosition + aimAnchor.forward * 0.05f
            : motion == 1 ? initialSurfacePosition + aimAnchor.right * 0.05f : initialSurfacePosition;
        Quaternion movedSurfaceRotation = motion == 2 ? Quaternion.Euler(0f, 12f, 0f) : Quaternion.identity;
        serverSurface.transform.SetPositionAndRotation(movedSurfacePosition, movedSurfaceRotation);
        Assert.That(ownerSurface.gameObject, Is.Not.SameAs(serverSurface.gameObject),
            "Loopback managers must expose separate copies of the same network target.");
        ownerSurface.transform.SetPositionAndRotation(initialSurfacePosition, Quaternion.identity);
        Physics.SyncTransforms();
        if (probeUnsyncedSurfaceMovement)
        {
            Quaternion tiltedBoardRotation = Quaternion.AngleAxis(0.25f, aimAnchor.right);
            int validSamples = 0;
            int invalidSamples = 0;
            string firstInvalid = null;
            bool priorAutoSync = Physics.autoSyncTransforms;
            Vector3 sourcePositionBeforeSamples = Find(owner, sourceObject.NetworkObjectId).transform.position;
            Quaternion sourceRotationBeforeSamples = Find(owner, sourceObject.NetworkObjectId).transform.rotation;
            for (int sample = 0; sample < 8; sample++)
            {
                // Before the fix, this could leave the raycast on the prior physics pose while
                // ComputePenetration received the updated board Transform.
                ownerSurface.transform.SetPositionAndRotation(initialSurfacePosition + Vector3.up * 0.001f, Quaternion.identity);
                refresh.Invoke(controller, null);
                if ((bool)PlacementControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller)) validSamples++;
                else invalidSamples++;
                object towardValidation = PlacementControllerType.GetProperty("LastValidation").GetValue(controller);
                if (firstInvalid == null && !(bool)PlacementControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller))
                    firstInvalid = $"toward geometry={PlacementControllerType.GetProperty("LastGeometryStatus").GetValue(controller)}, validation={towardValidation.GetType().GetProperty("Status").GetValue(towardValidation)}, depth={towardValidation.GetType().GetProperty("PenetrationDepth").GetValue(towardValidation)}, blocker={((Collider)towardValidation.GetType().GetProperty("BlockingCollider").GetValue(towardValidation))?.name}";

                ownerSurface.transform.SetPositionAndRotation(initialSurfacePosition + Vector3.down * 0.001f, tiltedBoardRotation);
                refresh.Invoke(controller, null);
                if ((bool)PlacementControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller)) validSamples++;
                else invalidSamples++;
            }
            Assert.That(Physics.autoSyncTransforms, Is.EqualTo(priorAutoSync), "The regression must not change the global physics setting.");

            ownerSurface.transform.SetPositionAndRotation(initialSurfacePosition + Vector3.up * 0.001f, tiltedBoardRotation);
            Physics.SyncTransforms();
            refresh.Invoke(controller, null);
            Assert.That(PlacementControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(),
                Is.EqualTo("SurfaceSupported"), "The tilted board remains a valid physical target after transform synchronization.");
            Assert.That((bool)PlacementControllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
                "Synchronizing moved board transforms resolves the preview mismatch.");
            Assert.That(invalidSamples, Is.Zero,
                $"Preview should remain stable while the moved-board Transform changes before the next physics step; observed={invalidSamples}/{validSamples + invalidSamples}, first invalid={firstInvalid}.");
            Assert.That(Find(owner, sourceObject.NetworkObjectId).transform.position, Is.EqualTo(sourcePositionBeforeSamples),
                "Preview refresh must not move the held nail source.");
            Assert.That(Quaternion.Angle(Find(owner, sourceObject.NetworkObjectId).transform.rotation, sourceRotationBeforeSamples),
                Is.LessThan(0.001f), "Preview refresh must not rotate the held nail source.");
            ownerSurface.transform.SetPositionAndRotation(initialSurfacePosition, Quaternion.identity);
            Physics.SyncTransforms();
            refresh.Invoke(controller, null);
            yield break;
        }
        Assert.That(ownerSurface.NetworkObjectId, Is.EqualTo(surfaceObject.NetworkObjectId));
        if (motion <= 2 || motion == 9)
            Assert.That(Vector3.Distance(ownerSurface.transform.position, serverSurface.transform.position) > 0.02f
                || Quaternion.Angle(ownerSurface.transform.rotation, serverSurface.transform.rotation) > 0.1f,
                Is.True, "The fixture must preserve distinct client-rendered and authoritative target poses.");
        Vector3 cachedLocalPosition = (Vector3)PlacementControllerType.GetField("localPosePosition",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        Quaternion cachedLocalRotation = (Quaternion)PlacementControllerType.GetField("localPoseRotation",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        Vector3 cachedLocalSurfacePoint = (Vector3)PlacementControllerType.GetField("localSurfacePoint",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        Vector3 cachedLocalSurfaceNormal = (Vector3)PlacementControllerType.GetField("localSurfaceNormal",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        string cachedPhysicalRootPath = (string)PlacementControllerType.GetField("physicalRootPath",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        bool cachedPhysicalRootHasRigidbody = (bool)PlacementControllerType.GetField("physicalRootHasRigidbody",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        Ray renderedAimRay = (Ray)PlacementControllerType.GetField("lastRenderedAimRay",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        Type geometryType = Type.GetType("SingleCarryPlacementGeometrySolver, Assembly-CSharp");
        object geometry = Activator.CreateInstance(geometryType, new object[] { Find(owner, sourceObject.NetworkObjectId).gameObject });
        Vector3 serverExpectedPosition = serverSurface.transform.TransformPoint(cachedLocalPosition);
        if (motion <= 2)
            Assert.That(Vector3.Distance(renderedPosition, serverExpectedPosition), Is.GreaterThan(0.002f),
                $"The target motion must change the rebased world pose: rendered={renderedPosition}, moved-target={serverExpectedPosition}.");

        if (motion == 7)
        {
            FieldInfo pendingField = PlacementControllerType.GetField("requestPending", BindingFlags.Instance | BindingFlags.NonPublic);
            pendingField.SetValue(controller, true);
            ulong previousTargetId = surfaceObject.NetworkObjectId;
            NetworkObjectReference staleTargetReference = new NetworkObjectReference(surfaceObject);
            surfaceObject.Despawn(false);
            surfaceObject.Spawn();
            Assert.That(surfaceObject.NetworkObjectId, Is.Not.EqualTo(previousTargetId),
                "Respawning the same GameObject must produce a new NGO object identity.");
            yield return Until(() => Find(owner, surfaceObject.NetworkObjectId) != null,
                "replacement spawn of the same GameObject reaches the owner");
            NetworkObject replacementSurface = Find(owner, surfaceObject.NetworkObjectId);
            Assert.That(replacementSurface, Is.Not.Null);
            MethodInfo staleRpc = PreviewType.GetMethod("SubmitSurfacePlacementServerRpc", BindingFlags.Instance | BindingFlags.NonPublic);
            staleRpc.Invoke(preview, new object[]
            {
                new NetworkObjectReference(Find(owner, sourceObject.NetworkObjectId)), staleTargetReference, 1UL,
                cachedLocalPosition, cachedLocalRotation, cachedLocalSurfacePoint, cachedLocalSurfaceNormal,
                cachedPhysicalRootPath, cachedPhysicalRootHasRigidbody, renderedPosition, renderedRotation,
                renderedAimRay.origin, renderedAimRay.direction, default(ServerRpcParams)
            });
            yield return Until(() => LastRequest(preview) == 1, "server rejects the stale captured target reference");
            Assert.That(ResultName(ownerPlayer), Is.EqualTo("InvalidRequest"),
                "The server must reject the despawned NetworkObjectReference instead of resolving its replacement.");
            AssertHeld(owner, sourceObject);
            pendingField.SetValue(controller, false);
            object[] rebaseArgs = { null };
            bool staleReferenceRebased = (bool)PlacementControllerType.GetMethod("TryRebaseNetworkSurfacePose",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, rebaseArgs);
            Assert.That(staleReferenceRebased, Is.False,
                "A respawned object with a new NGO identity cannot replace the surface captured at preview time.");
            PlacementControllerType.GetMethod("ConfirmPlacement", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { null, EventArgs.Empty });
            Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.False,
                "The stale surface reference must be rejected before a placement RPC is sent.");
            AssertHeld(owner, sourceObject);
            yield break;
        }

        if (motion == 8)
        {
            // Exercise the anchored ServerRpc itself with forged payload fields. The failed
            // attempts consume monotonically increasing request IDs, but must not release or
            // move the carried source; the real E action then retries with the next ID.
            SetCollidersEnabled(serverSurfaceColliders, true);
            SetCollidersEnabled(ownerSurfaceColliders, false);
            Physics.SyncTransforms();
            MethodInfo surfaceRpc = PreviewType.GetMethod("SubmitSurfacePlacementServerRpc", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(surfaceRpc, Is.Not.Null);
            NetworkObject ownerSourceForRequests = Find(owner, sourceObject.NetworkObjectId);
            NetworkObjectReference ownerSourceReference = new NetworkObjectReference(ownerSourceForRequests);
            NetworkObjectReference ownerTargetReference = new NetworkObjectReference(ownerSurface);
            Vector3 requestPosition = serverSurface.transform.TransformPoint(cachedLocalPosition);
            Quaternion requestRotation = serverSurface.transform.rotation * cachedLocalRotation;
            FieldInfo pendingField = PlacementControllerType.GetField("requestPending", BindingFlags.Instance | BindingFlags.NonPublic);
            pendingField.SetValue(controller, true);

            // A real overlapping blocker must be reported as Occupied on the anchored path.
            GameObject occupied = new GameObject("Stage 6 anchored occupancy blocker");
            runtimeObjects.Add(occupied);
            occupied.transform.position = requestPosition + serverSurface.transform.right * 0.15f;
            occupied.AddComponent<BoxCollider>().size = Vector3.one * 0.12f;
            Physics.SyncTransforms();
            Vector3 contactPoint = serverSurface.transform.TransformPoint(cachedLocalSurfacePoint);
            Vector3 contactNormal = serverSurface.transform.TransformDirection(cachedLocalSurfaceNormal).normalized;
            Type anchorType = Type.GetType("SingleCarryPlacementSurfaceAnchor, Assembly-CSharp");
            MethodInfo resolveContact = anchorType.GetMethod("TryResolveContactCollider", BindingFlags.Static | BindingFlags.Public);
            object[] resolveArgs = { serverSurface, source.transform, ownerPlayer.transform, contactPoint,
                contactNormal, cachedPhysicalRootPath, cachedPhysicalRootHasRigidbody, null };
            Assert.That((bool)resolveContact.Invoke(null, resolveArgs), Is.True);
            Collider authoritativeContact = (Collider)resolveArgs[7];
            MethodInfo hasLos = anchorType.GetMethod("HasLineOfSightToContact", BindingFlags.Static | BindingFlags.Public);
            Assert.That((bool)hasLos.Invoke(null, new object[] { authoritativeContact, serverSurface,
                cachedPhysicalRootPath, cachedPhysicalRootHasRigidbody, source.transform, ownerPlayer.transform,
                renderedAimRay.origin, contactPoint, 2f }), Is.True,
                "The occupancy probe must not obscure the selected contact from the trusted aim origin.");
            Type geometryTypeForOccupancy = Type.GetType("SingleCarryPlacementGeometrySolver, Assembly-CSharp");
            object occupancyGeometry = Activator.CreateInstance(geometryTypeForOccupancy, new object[] { source.gameObject });
            object occupancyPose = geometryTypeForOccupancy.GetMethod("CalculateAtSurfaceContact").Invoke(occupancyGeometry,
                new object[] { contactPoint, contactNormal, requestRotation, authoritativeContact });
            Type validatorTypeForOccupancy = Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp");
            object occupancyValidator = Activator.CreateInstance(validatorTypeForOccupancy, new object[] { source.gameObject });
            object occupancyResult = validatorTypeForOccupancy.GetMethod("Validate").Invoke(occupancyValidator,
                new[] { occupancyPose, null });
            Assert.That(occupancyResult.GetType().GetProperty("Status").GetValue(occupancyResult).ToString(),
                Is.EqualTo("Occupied"), "The anchored fixture must prove real candidate occupancy before the RPC.");
            surfaceRpc.Invoke(preview, new object[] { ownerSourceReference, ownerTargetReference, 1UL,
                cachedLocalPosition, cachedLocalRotation, cachedLocalSurfacePoint, cachedLocalSurfaceNormal,
                cachedPhysicalRootPath, cachedPhysicalRootHasRigidbody, requestPosition, requestRotation,
                renderedAimRay.origin, renderedAimRay.direction, default(ServerRpcParams) });
            yield return Until(() => LastRequest(preview) == 1, "anchored occupancy rejection response");
            Assert.That(ResultName(ownerPlayer), Is.EqualTo("Occupied"));
            AssertHeld(owner, sourceObject);
            UnityEngine.Object.Destroy(occupied);
            yield return null;
            Physics.SyncTransforms();

            object[][] forgedArguments =
            {
                new object[] { ownerSourceReference, ownerTargetReference, 2UL,
                    cachedLocalPosition + Vector3.right * 0.01f, cachedLocalRotation,
                    cachedLocalSurfacePoint, cachedLocalSurfaceNormal, cachedPhysicalRootPath,
                    cachedPhysicalRootHasRigidbody, requestPosition, requestRotation,
                    renderedAimRay.origin, renderedAimRay.direction, default(ServerRpcParams) },
                new object[] { ownerSourceReference, ownerTargetReference, 3UL,
                    cachedLocalPosition, new Quaternion(0f, 0f, 0f, 0f),
                    cachedLocalSurfacePoint, cachedLocalSurfaceNormal, cachedPhysicalRootPath,
                    cachedPhysicalRootHasRigidbody, requestPosition, requestRotation,
                    renderedAimRay.origin, renderedAimRay.direction, default(ServerRpcParams) },
                new object[] { ownerSourceReference, ownerTargetReference, 4UL,
                    cachedLocalPosition, cachedLocalRotation,
                    cachedLocalSurfacePoint, Vector3.zero, cachedPhysicalRootPath,
                    cachedPhysicalRootHasRigidbody, requestPosition, requestRotation,
                    renderedAimRay.origin, renderedAimRay.direction, default(ServerRpcParams) },
                new object[] { ownerSourceReference, ownerTargetReference, 5UL,
                    cachedLocalPosition, cachedLocalRotation,
                    cachedLocalSurfacePoint, cachedLocalSurfaceNormal, "999",
                    cachedPhysicalRootHasRigidbody, requestPosition, requestRotation,
                    renderedAimRay.origin, renderedAimRay.direction, default(ServerRpcParams) },
                new object[] { ownerSourceReference, new NetworkObjectReference(ownerSourceForRequests), 6UL,
                    cachedLocalPosition, cachedLocalRotation,
                    cachedLocalSurfacePoint, cachedLocalSurfaceNormal, cachedPhysicalRootPath,
                    cachedPhysicalRootHasRigidbody, requestPosition, requestRotation,
                    renderedAimRay.origin, renderedAimRay.direction, default(ServerRpcParams) }
            };
            string[] expectedForgedResults = { "InvalidPose", "InvalidRequest", "InvalidRequest", "InvalidPose", "InvalidRequest" };
            for (int i = 0; i < forgedArguments.Length; i++)
            {
                surfaceRpc.Invoke(preview, forgedArguments[i]);
                ulong expectedId = (ulong)(i + 2);
                yield return Until(() => LastRequest(preview) == expectedId, "forged anchored payload rejection");
                Assert.That(ResultName(ownerPlayer), Is.EqualTo(expectedForgedResults[i]),
                    $"Forged anchored payload #{expectedId} must fail closed.");
                AssertHeld(owner, sourceObject);
                Assert.That(IsPickedUp(sourceObject), Is.True);
            }
            SetControllerRequestId(controller, 6);
            SetCollidersEnabled(ownerSurfaceColliders, true);
            SetCollidersEnabled(serverSurfaceColliders, false);
            Physics.SyncTransforms();
            pendingField.SetValue(controller, false);
            refresh.Invoke(controller, null);
            Assert.That(PlacementControllerType.GetProperty("LastGeometryStatus").GetValue(controller).ToString(),
                Is.EqualTo("SurfaceSupported"), "After validation probes, the owner's original target contact must be reacquired.");
            Assert.That(PlacementControllerType.GetField("anchoredSurface", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller), Is.EqualTo(ownerSurface), "A foreign mirror may not replace the cached target.");
        }

        PressKey(Key.E);
        bool pendingAfterConfirm = (bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller);
        if (!pendingAfterConfirm)
        {
            object[] diagnosticArgs = { null };
            bool rebaseSucceeded = (bool)PlacementControllerType.GetMethod("TryRebaseNetworkSurfacePose",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, diagnosticArgs);
            Assert.Fail($"Controller did not submit E. validation={PlacementControllerType.GetProperty("LastValidation").GetValue(controller)}, "
                + $"hasAnchor={PlacementControllerType.GetField("hasNetworkSurfaceAnchor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)}, "
                + $"anchorRejected={PlacementControllerType.GetField("networkSurfaceAnchorRejected", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)}, "
                + $"rebase={rebaseSucceeded}, ray={renderedAimRay.origin}->{renderedAimRay.direction}, "
                + $"anchorTarget={((NetworkObject)PlacementControllerType.GetField("anchoredSurface", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller))?.name}, "
                + $"rebasedPose={diagnosticArgs[0]}");
        }
        Assert.That(pendingAfterConfirm, Is.True);
        // Local confirmation must see the owner's selected copy; the server then receives the
        // same RPC with only its authoritative copy participating in this shared Physics scene.
        SetCollidersEnabled(ownerSurfaceColliders, false);
        SetCollidersEnabled(serverSurfaceColliders, true);
        NetworkObject ownerSourceMirror = Find(owner, sourceObject.NetworkObjectId);
        Assert.That(ownerSourceMirror, Is.Not.Null, "The owner source mirror must remain available for placement cleanup.");
        foreach (Collider collider in ownerSourceMirror.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        GameObject obstruction = null;
        if (motion == 3)
            SetCollidersEnabled(serverSurfaceColliders, false);
        else if (motion == 4 || motion == 5)
        {
            obstruction = new GameObject(motion == 4
                ? "Stage 6 server line-of-sight blocker" : "Stage 6 solid blocker containing aim origin");
            runtimeObjects.Add(obstruction);
            obstruction.AddComponent<BoxCollider>().size = motion == 4 ? Vector3.one * 0.15f : Vector3.one * 0.25f;
            Vector3 anchoredPoint = serverSurface.transform.TransformPoint(cachedLocalSurfacePoint);
            obstruction.transform.position = motion == 4
                ? Vector3.Lerp(renderedAimRay.origin, anchoredPoint, 0.5f)
                : renderedAimRay.origin;
        }
        else if (motion == 6)
            serverSurface.transform.position = initialSurfacePosition + aimAnchor.forward * 1.3f;
        Physics.SyncTransforms();
        ulong requestId = GetPendingRequestId(controller);
        yield return Until(() => LastRequest(preview) == requestId, "server response to moved-surface owner request");

        string expectedResult = motion >= 3 && motion <= 6 && !useMountingNail ? "InvalidPose" : "Accepted";
        Assert.That(ResultName(ownerPlayer), Is.EqualTo(expectedResult),
            $"The selected local pose should be rebased after same-NI target motion. Motion={motion}, "
            + $"serverTarget={serverSurface.transform.position}/{serverSurface.transform.rotation.eulerAngles}, "
            + $"contactEnabled={serverSurfaceCollider.enabled}, sameNI={serverSurface.NetworkObjectId == surfaceObject.NetworkObjectId}.");
        if (expectedResult != "Accepted")
        {
            AssertHeld(owner, sourceObject);
            Assert.That(IsPickedUp(sourceObject), Is.True, "A rejected same-target request must preserve the held source.");
            Assert.That(ownerSurface.NetworkObjectId, Is.EqualTo(surfaceObject.NetworkObjectId));
            Assert.That(serverSurface.NetworkObjectId, Is.EqualTo(surfaceObject.NetworkObjectId));
            Assert.That(GetHeld(ownerPlayer), Is.EqualTo(ownerSourceMirror.gameObject));
            yield break;
        }
        yield return Until(() => sourceObject.OwnerClientId == NetworkManager.ServerClientId
            && !IsPickedUp(sourceObject) && !IsPickedUp(Find(owner, sourceObject.NetworkObjectId)),
            "accepted placement releases source ownership and pickup state");
        Vector3 expectedCommittedPosition = serverSurface.transform.TransformPoint(cachedLocalPosition);
        Quaternion expectedCommittedRotation = serverSurface.transform.rotation * cachedLocalRotation;
        if (probeSteppedRotation)
            Assert.That(Quaternion.Angle(expectedCommittedRotation, expectedStepRotationForRemote), Is.LessThan(.1f),
                "The captured owner preview quaternion sent through E is the same integer-grid yaw and world-axis step.");
        NetworkObject ownerSource = Find(owner, sourceObject.NetworkObjectId);
        Rigidbody serverBody = source.GetComponent<Rigidbody>();
        Rigidbody ownerBody = ownerSource != null ? ownerSource.GetComponent<Rigidbody>() : null;
        NetworkTransform serverTransform = source.GetComponent<NetworkTransform>();
        NetworkTransform ownerTransform = ownerSource != null ? ownerSource.GetComponent<NetworkTransform>() : null;
        string poseDiagnostics = $"serverTransform={source.transform.position}/{source.transform.rotation.eulerAngles}, "
            + $"serverBody={serverBody.position}/{serverBody.rotation.eulerAngles}, "
            + $"serverBodyMode={serverBody.isKinematic}/{serverBody.detectCollisions}/{serverBody.interpolation}, "
            + $"serverVelocity={serverBody.linearVelocity}/{serverBody.angularVelocity}, "
            + $"serverNT={serverTransform.GetSpaceRelativePosition(true)}/{serverTransform.GetSpaceRelativeRotation(true).eulerAngles}, "
            + $"ownerTransform={(ownerSource != null ? ownerSource.transform.position.ToString() : "missing")}/"
            + $"{(ownerSource != null ? ownerSource.transform.rotation.eulerAngles.ToString() : "missing")}, "
            + $"ownerBody={(ownerBody != null ? ownerBody.position.ToString() : "missing")}/"
            + $"{(ownerBody != null ? ownerBody.rotation.eulerAngles.ToString() : "missing")}, "
            + $"ownerBodyMode={(ownerBody != null ? ownerBody.isKinematic.ToString() : "missing")}/"
            + $"{(ownerBody != null ? ownerBody.interpolation.ToString() : "missing")}, "
            + $"ownerNT={(ownerTransform != null ? ownerTransform.GetSpaceRelativePosition(true).ToString() : "missing")}/"
            + $"{(ownerTransform != null ? ownerTransform.GetSpaceRelativeRotation(true).eulerAngles.ToString() : "missing")}, "
            + $"serverTarget={serverSurface.transform.position}/{serverSurface.transform.rotation.eulerAngles}, "
            + $"ownerTarget={ownerSurface.transform.position}/{ownerSurface.transform.rotation.eulerAngles}, "
            + $"heldServer={GetHeld(host.LocalClient.PlayerObject)}, heldOwner={GetHeld(ownerPlayer)}";
        Assert.That(Vector3.Distance(source.transform.position, expectedCommittedPosition), Is.LessThan(0.002f),
            $"Placement must preserve the exact previewed local position on the selected target. "
            + $"actual={source.transform.position}, expected={expectedCommittedPosition}, target={serverSurface.transform.position}/{serverSurface.transform.rotation.eulerAngles}, "
            + $"local={cachedLocalPosition}, motion={motion}; {poseDiagnostics}.");
        Assert.That(Quaternion.Angle(source.transform.rotation, expectedCommittedRotation), Is.LessThan(0.1f),
            "Placement must preserve the exact previewed local orientation on the selected target.");
        Assert.That(surfaceObject.NetworkObjectId, Is.EqualTo(ownerSurface.NetworkObjectId));
        if (useMountingNail)
        {
            Component serverNail = source.GetComponent(Type.GetType("MountingNail, Assembly-CSharp"));
            Type nailType = serverNail.GetType();
            Assert.That(nailType.GetProperty("State").GetValue(serverNail).ToString(), Is.EqualTo("Prepared"),
                "The real accepted remote F/E placement must invoke the nail commit listener after holder cleanup.");
            double preparedDeadline = (double)nailType.GetField("preparedDeadline", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(serverNail);
            NetworkObject ownerNail = Find(owner, sourceObject.NetworkObjectId);
            yield return Until(() => nailType.GetProperty("State").GetValue(ownerNail.GetComponent(nailType)).ToString() == "Prepared",
                "owner nail observes accepted Prepared state");
            MethodInfo surfaceRpc = PreviewType.GetMethod("SubmitSurfacePlacementServerRpc", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(surfaceRpc, Is.Not.Null, "The duplicate-request control must use the actual anchored ServerRpc wrapper.");
            surfaceRpc.Invoke(preview, new object[]
            {
                new NetworkObjectReference(ownerNail), new NetworkObjectReference(ownerSurface), requestId,
                cachedLocalPosition, cachedLocalRotation,
                cachedLocalSurfacePoint, cachedLocalSurfaceNormal, cachedPhysicalRootPath,
                cachedPhysicalRootHasRigidbody,
                expectedCommittedPosition, expectedCommittedRotation,
                renderedAimRay.origin, renderedAimRay.direction, default(ServerRpcParams)
            });
            yield return Until(() => ResultName(ownerPlayer) == "StaleRequest",
                "duplicate anchored request is rejected by the actual owner RPC");
            Assert.That(nailType.GetProperty("State").GetValue(serverNail).ToString(), Is.EqualTo("Prepared"));
            Assert.That((double)nailType.GetField("preparedDeadline", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(serverNail), Is.EqualTo(preparedDeadline),
                "The stale duplicate must not invoke the nail commit listener or reset its timer.");
        }
    }

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
            List<GameObject> ownedFixtureCameras = runtimeObjects.FindAll(obj => obj != null && obj.GetComponent<Camera>() != null);
            foreach (GameObject obj in runtimeObjects)
                if (obj != null && !ownedFixtureCameras.Contains(obj)) UnityEngine.Object.Destroy(obj);
            yield return null;
            foreach (UnityEngine.Object asset in assets) if (asset != null) UnityEngine.Object.Destroy(asset);
            yield return null;
            foreach (GameObject fixtureCamera in ownedFixtureCameras)
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
        SetCarryOrientationOffset(resourcePrefab, new Vector3(0f, 90f, 0f));
        SetCarryOrientationOffset(bridgePrefab, new Vector3(0f, 90f, 0f));
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
        List<(int excludedLayers, int priority)> resourceOriginalContactSettings = CaptureSolidContactSettings(resourceNetObj);
        List<(int excludedLayers, int priority)> bridgeOriginalContactSettings = CaptureSolidContactSettings(bridgeNetObj);
        Assert.That(resourceOriginalContactSettings.Count, Is.GreaterThan(0));
        Assert.That(bridgeOriginalContactSettings.Count, Is.GreaterThan(0));

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
        yield return Until(() => IsPickedUp(Find(host, resourceNetObj.NetworkObjectId))
            && IsPickedUp(Find(ownerA, resourceNetObj.NetworkObjectId))
            && IsPickedUp(Find(ownerB, resourceNetObj.NetworkObjectId))
            && IsPickedUp(Find(host, bridgeNetObj.NetworkObjectId))
            && IsPickedUp(Find(ownerA, bridgeNetObj.NetworkObjectId))
            && IsPickedUp(Find(ownerB, bridgeNetObj.NetworkObjectId)),
            "durable held state and contact isolation on server, owner, and observer");
        AssertSingleCarryContactState(Find(host, resourceNetObj.NetworkObjectId), resourceOriginalContactSettings, true, "server resource");
        AssertSingleCarryContactState(Find(ownerA, resourceNetObj.NetworkObjectId), resourceOriginalContactSettings, true, "owner resource");
        AssertSingleCarryContactState(Find(ownerB, resourceNetObj.NetworkObjectId), resourceOriginalContactSettings, true, "observer resource");
        AssertSingleCarryContactState(Find(host, bridgeNetObj.NetworkObjectId), bridgeOriginalContactSettings, true, "server mountable");
        AssertSingleCarryContactState(Find(ownerA, bridgeNetObj.NetworkObjectId), bridgeOriginalContactSettings, true, "observer mountable");
        AssertSingleCarryContactState(Find(ownerB, bridgeNetObj.NetworkObjectId), bridgeOriginalContactSettings, true, "owner mountable");

        // A disable/enable cycle must reconstruct isolation from durable held state.
        Behaviour serverResourceBehaviour = (Behaviour)resource.GetComponent(BaseResourceType);
        serverResourceBehaviour.enabled = false;
        serverResourceBehaviour.enabled = true;
        Behaviour serverBridgeBehaviour = (Behaviour)bridge.GetComponent(BridgeType);
        serverBridgeBehaviour.enabled = false;
        serverBridgeBehaviour.enabled = true;
        yield return new WaitForFixedUpdate();
        AssertSingleCarryContactState(Find(host, resourceNetObj.NetworkObjectId), resourceOriginalContactSettings, true, "re-enabled server resource");
        AssertSingleCarryContactState(Find(host, bridgeNetObj.NetworkObjectId), bridgeOriginalContactSettings, true, "re-enabled server mountable");

        NetworkManager lateJoin = CreateManager("Stage 6 late join while single-carry held", playerPrefab,
            new[] { resourcePrefab, bridgePrefab }, port);
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && lateJoin.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 4
            && Find(lateJoin, resourceNetObj.NetworkObjectId) != null
            && Find(lateJoin, bridgeNetObj.NetworkObjectId) != null,
            "late join receives both already-held sources");
        DisposeFixtureInputMaps(null);
        DisableMirrorColliders(lateJoin, resourceNetObj.NetworkObjectId, host);
        DisableMirrorColliders(lateJoin, bridgeNetObj.NetworkObjectId, host);
        yield return Until(() => IsPickedUp(Find(lateJoin, resourceNetObj.NetworkObjectId))
            && IsPickedUp(Find(lateJoin, bridgeNetObj.NetworkObjectId)),
            "late join receives durable held state for resource and mountable");
        AssertSingleCarryContactState(Find(lateJoin, resourceNetObj.NetworkObjectId), resourceOriginalContactSettings, true, "late-join resource");
        AssertSingleCarryContactState(Find(lateJoin, bridgeNetObj.NetworkObjectId), bridgeOriginalContactSettings, true, "late-join mountable");

        Vector3 originA = GetAimAnchor(ownerA.LocalClient.PlayerObject).position;
        Vector3 originB = GetAimAnchor(ownerB.LocalClient.PlayerObject).position;
        Quaternion rotation = Quaternion.Euler(0f, 37f, 0f) * ResolveCarryOrientationOffset(resourcePrefab);
        Quaternion bridgeRotation = Quaternion.Euler(0f, 37f, 0f) * ResolveCarryOrientationOffset(bridgePrefab);
        Vector3 sharedPose = originA + Vector3.forward * 2f;
        PreviewType.GetMethod("SetOwnerPreview").Invoke(Preview(ownerA.LocalClient.PlayerObject),
            new object[] { Find(ownerA, resourceNetObj.NetworkObjectId).gameObject, sharedPose, rotation, true });
        PreviewType.GetMethod("SetOwnerPreview").Invoke(Preview(ownerB.LocalClient.PlayerObject),
            new object[] { Find(ownerB, bridgeNetObj.NetworkObjectId).gameObject, sharedPose, bridgeRotation, true });
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
        Send(ownerB, Preview(ownerB.LocalClient.PlayerObject), bridgeNetObj, 2, sharedPose, bridgeRotation,
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
        Assert.That(Quaternion.Angle(winner.transform.rotation, resultA == "Accepted" ? rotation : bridgeRotation), Is.LessThan(0.1f));

        // The loser remains held and can choose another valid point. This also exercises both backends.
        NetworkManager loserOwner = resultA == "Accepted" ? ownerB : ownerA;
        ulong loserId = loserOwner.LocalClientId;
        Vector3 loserOrigin = GetAimAnchor(loserOwner.LocalClient.PlayerObject).position;
        Vector3 secondPose = loserOrigin + Vector3.right * 2f;
        ulong retryId = LastRequest(Preview(loserOwner.LocalClient.PlayerObject)) + 1;
        Quaternion loserRotation = loser == resourceNetObj ? rotation : bridgeRotation;
        Send(loserOwner, Preview(loserOwner.LocalClient.PlayerObject), loser, retryId, secondPose, loserRotation,
            new Ray(loserOrigin, Vector3.right));
        yield return Until(() => LastRequest(Preview(loserOwner.LocalClient.PlayerObject)) == retryId,
            "loser retry response");
        AssertResult(loserOwner.LocalClient.PlayerObject, "Accepted");
        Assert.That(loser.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(Vector3.Distance(loser.transform.position, secondPose), Is.LessThan(0.003f));
        Assert.That(Quaternion.Angle(loser.transform.rotation, loserRotation), Is.LessThan(0.1f));

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
            && Quaternion.Angle(Find(ownerA, bridgeNetObj.NetworkObjectId).transform.rotation, bridgeRotation) < 0.1f
            && Quaternion.Angle(Find(ownerB, bridgeNetObj.NetworkObjectId).transform.rotation, bridgeRotation) < 0.1f,
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
        foreach (NetworkManager peer in new[] { host, ownerA, ownerB, lateJoin })
        {
            AssertSingleCarryContactState(Find(peer, resourceNetObj.NetworkObjectId), resourceOriginalContactSettings, false, peer.name + " placed resource");
            AssertSingleCarryContactState(Find(peer, bridgeNetObj.NetworkObjectId), bridgeOriginalContactSettings, false, peer.name + " placed mountable");
        }
        Assert.That(GetBody(resourceNetObj.gameObject).isKinematic, Is.False, "Server resource resumes dynamic physics after F/E placement.");
        Assert.That(GetBody(resourceNetObj.gameObject).useGravity, Is.True, "Server resource restores gravity after F/E placement.");
        Assert.That(GetBody(bridgeNetObj.gameObject).isKinematic, Is.False, "Server mountable resumes dynamic physics after F/E placement.");
        Assert.That(GetBody(bridgeNetObj.gameObject).useGravity, Is.True, "Server mountable restores gravity after F/E placement.");
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
                    Quaternion expectedRotation = committedObjects[i] == resourceNetObj ? rotation : bridgeRotation;
                    Assert.That(Quaternion.Angle(observed.transform.rotation, expectedRotation), Is.LessThan(0.1f),
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

        NetworkManager placedLateJoin = CreateManager("Stage 6 late join after placement", playerPrefab,
            new[] { resourcePrefab, bridgePrefab }, port);
        Assert.That(placedLateJoin.StartClient(), Is.True);
        yield return Until(() => placedLateJoin.IsConnectedClient && placedLateJoin.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 5
            && Find(placedLateJoin, resourceNetObj.NetworkObjectId) != null
            && Find(placedLateJoin, bridgeNetObj.NetworkObjectId) != null,
            "late join receives both already-placed sources");
        DisableDisconnectFixtureRemotePlayerPhysics(host, new[] { placedLateJoin });
        DisableMirrorColliders(placedLateJoin, resourceNetObj.NetworkObjectId, host);
        DisableMirrorColliders(placedLateJoin, bridgeNetObj.NetworkObjectId, host);
        DisposeFixtureInputMaps(null);
        yield return Until(() => Quaternion.Angle(Find(placedLateJoin, resourceNetObj.NetworkObjectId).transform.rotation, rotation) < 0.1f
            && Quaternion.Angle(Find(placedLateJoin, bridgeNetObj.NetworkObjectId).transform.rotation, bridgeRotation) < 0.1f,
            "post-placement late join receives the already-composed rotations");
        Assert.That(Vector3.Distance(Find(placedLateJoin, resourceNetObj.NetworkObjectId).transform.position,
            resultA == "Accepted" ? sharedPose : secondPose), Is.LessThan(0.02f));
        Assert.That(Vector3.Distance(Find(placedLateJoin, bridgeNetObj.NetworkObjectId).transform.position,
            resultA == "Accepted" ? secondPose : sharedPose), Is.LessThan(0.02f));
        Assert.That(Quaternion.Angle(Find(placedLateJoin, resourceNetObj.NetworkObjectId).transform.rotation, rotation), Is.LessThan(0.1f));
        Assert.That(Quaternion.Angle(Find(placedLateJoin, bridgeNetObj.NetworkObjectId).transform.rotation, bridgeRotation), Is.LessThan(0.1f));

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
        sharedLogNetworkPrefab.SetSceneObjectStatus(false);

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
        Assert.That(sharedLogNetworkPrefab.IsSpawned, Is.False,
            $"The shared Wooden Log registration template must not be auto-spawned by StartHost. id={sharedLogNetworkPrefab.NetworkObjectId}, sceneObject={sharedLogNetworkPrefab.IsSceneObject}, active={sharedLogPrefab.activeInHierarchy}, colliderStates={DescribeColliderStates(sharedLogPrefab)}");
        Assert.That(CountEnabledColliders(sharedLogPrefab), Is.EqualTo(0),
            $"The shared Wooden Log template must stay non-physical after host startup. spawned={sharedLogNetworkPrefab.IsSpawned}, active={sharedLogPrefab.activeInHierarchy}, colliderStates={DescribeColliderStates(sharedLogPrefab)}");
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
        List<(int excludedLayers, int priority)> disconnectResourceOriginalSettings = CaptureSolidContactSettings(sourceNetworkObject);
        Assert.That(InvokePickup(source, owner.LocalClientId), Is.True);
        ConfirmLocalPickup(owner, sourceNetworkObject.NetworkObjectId);
        yield return Until(() => sourceNetworkObject.OwnerClientId == owner.LocalClientId
            && GetHeld(owner.LocalClient.PlayerObject) == Find(owner, sourceNetworkObject.NetworkObjectId).gameObject,
            "server and owner agree on held resource");
        yield return Until(() => IsPickedUp(Find(host, sourceNetworkObject.NetworkObjectId))
            && IsPickedUp(Find(owner, sourceNetworkObject.NetworkObjectId))
            && IsPickedUp(Find(observer, sourceNetworkObject.NetworkObjectId)),
            "held resource isolation replicates to disconnect observer");
        AssertSingleCarryContactState(Find(host, sourceNetworkObject.NetworkObjectId), disconnectResourceOriginalSettings, true, "disconnect server resource");
        AssertSingleCarryContactState(Find(owner, sourceNetworkObject.NetworkObjectId), disconnectResourceOriginalSettings, true, "disconnect owner resource");
        AssertSingleCarryContactState(Find(observer, sourceNetworkObject.NetworkObjectId), disconnectResourceOriginalSettings, true, "disconnect observer resource");
        DisableDisconnectFixtureMirrorColliders(new[] { owner, observer, sharedOwner, inventoryOwner }, sourceNetworkObject.NetworkObjectId);

        GameObject bridge = UnityEngine.Object.Instantiate(bridgePrefab);
        runtimeObjects.Add(bridge);
        bridge.transform.position = new Vector3(18f, 3f, 11f);
        NetworkObject bridgeNetworkObject = bridge.GetComponent<NetworkObject>();
        spawnedObjects.Add(bridgeNetworkObject);
        bridgeNetworkObject.Spawn();
        yield return Until(() => Find(owner, bridgeNetworkObject.NetworkObjectId) != null
            && Find(observer, bridgeNetworkObject.NetworkObjectId) != null, "held mountable replication");
        List<(int excludedLayers, int priority)> disconnectBridgeOriginalSettings = CaptureSolidContactSettings(bridgeNetworkObject);
        Assert.That(InvokePickup(bridge, owner.LocalClientId), Is.True);
        ConfirmLocalPickup(owner, bridgeNetworkObject.NetworkObjectId);
        yield return Until(() => bridgeNetworkObject.OwnerClientId == owner.LocalClientId,
            "server and owner agree on held mountable");
        yield return Until(() => IsPickedUp(Find(host, bridgeNetworkObject.NetworkObjectId))
            && IsPickedUp(Find(owner, bridgeNetworkObject.NetworkObjectId))
            && IsPickedUp(Find(observer, bridgeNetworkObject.NetworkObjectId)),
            "held mountable isolation replicates to disconnect observer");
        AssertSingleCarryContactState(Find(host, bridgeNetworkObject.NetworkObjectId), disconnectBridgeOriginalSettings, true, "disconnect server mountable");
        AssertSingleCarryContactState(Find(owner, bridgeNetworkObject.NetworkObjectId), disconnectBridgeOriginalSettings, true, "disconnect owner mountable");
        AssertSingleCarryContactState(Find(observer, bridgeNetworkObject.NetworkObjectId), disconnectBridgeOriginalSettings, true, "disconnect observer mountable");
        DisableDisconnectFixtureMirrorColliders(new[] { owner, observer, sharedOwner, inventoryOwner }, bridgeNetworkObject.NetworkObjectId);
        Vector3 bridgeCallbackPosition = Vector3.one * float.NaN;
        Quaternion bridgeCallbackRotation = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
        ulong bridgeCallbackOwnerClientId = ulong.MaxValue;
        bool bridgeCallbackIsPickedUp = true;
        bool bridgeCallbackIsKinematic = true;
        bool bridgeCallbackUseGravity = false;
        Rigidbody bridgeBodyFrozenAtDisconnectBoundary = null;
        RigidbodyConstraints bridgeOriginalConstraints = RigidbodyConstraints.None;

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
                bridgeCallbackPosition = releasedMountable.transform.position;
                bridgeCallbackRotation = releasedMountable.transform.rotation;
                Rigidbody mountableBody = GetBody(releasedMountable.gameObject);
                initialMountableVelocity = mountableBody.linearVelocity;
                initialMountableAngularVelocity = mountableBody.angularVelocity;
                bridgeCallbackOwnerClientId = releasedMountable.OwnerClientId;
                bridgeCallbackIsPickedUp = IsPickedUp(releasedMountable);
                bridgeCallbackIsKinematic = mountableBody.isKinematic;
                bridgeCallbackUseGravity = mountableBody.useGravity;
                if (bridgeCallbackOwnerClientId == NetworkManager.ServerClientId
                    && !bridgeCallbackIsPickedUp && !bridgeCallbackIsKinematic && bridgeCallbackUseGravity
                    && initialMountableVelocity.sqrMagnitude < 0.000001f
                    && initialMountableAngularVelocity.sqrMagnitude < 0.000001f)
                {
                    bridgeBodyFrozenAtDisconnectBoundary = mountableBody;
                    bridgeOriginalConstraints = mountableBody.constraints;
                    mountableBody.constraints = RigidbodyConstraints.FreezeAll;
                }
            }
        };
        host.OnClientDisconnectCallback += disconnectBoundaryObserver;
        Vector3 bridgeHeldPosition = bridge.transform.position;
        Quaternion bridgeHeldRotation = bridge.transform.rotation;
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
            AssertSingleCarryContactState(remainingSource, disconnectResourceOriginalSettings, false, "disconnect-restored server resource");
            AssertSingleCarryContactState(observerSource, disconnectResourceOriginalSettings, false, "disconnect-restored observer resource");
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
        Assert.That(bridgeCallbackOwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(bridgeCallbackIsPickedUp, Is.False);
        Assert.That(bridgeCallbackIsKinematic, Is.False);
        Assert.That(bridgeCallbackUseGravity, Is.True);
        Assert.That(Vector3.Distance(bridgeCallbackPosition, bridgeHeldPosition), Is.LessThan(0.02f),
            "Disconnect recovery must release the mountable at the server's immediately pre-disconnect pose.");
        Assert.That(Quaternion.Angle(bridgeCallbackRotation, bridgeHeldRotation), Is.LessThan(0.1f));
        NetworkObject observerBridge = Find(observer, bridgeId);
        Assert.That(observerBridge, Is.Not.Null);
        Assert.That(IsPickedUp(observerBridge), Is.False);
        AssertSingleCarryContactState(releasedBridge, disconnectBridgeOriginalSettings, false, "disconnect-restored server mountable");
        AssertSingleCarryContactState(observerBridge, disconnectBridgeOriginalSettings, false, "disconnect-restored observer mountable");

        yield return Until(() =>
        {
            Assert.That(releasedBridge, Is.Not.Null,
                "The server release object must remain present while the observer receives its final pose.");
            Assert.That(Vector3.Distance(releasedBridge.transform.position, bridgeCallbackPosition), Is.LessThan(0.02f),
                "The server release position must remain stable while waiting for observer replication.");
            Assert.That(Quaternion.Angle(releasedBridge.transform.rotation, bridgeCallbackRotation), Is.LessThan(0.1f),
                "The server release rotation must remain stable while waiting for observer replication.");
            observerBridge = Find(observer, bridgeId);
            if (observerBridge == null) return false;
            NetworkTransform observerTransform = observerBridge.GetComponent<NetworkTransform>();
            return observerTransform != null
                && Vector3.Distance(observerTransform.GetSpaceRelativePosition(true), bridgeCallbackPosition) < 0.08f
                && Vector3.Distance(observerBridge.transform.position, bridgeCallbackPosition) < 0.08f;
        }, "disconnect release pose replication and observer interpolation");

        // Keep ordinary body physics from obscuring the separate queued NetworkTransform snapback check.
        // Release-state and velocity samples were captured before freezing; assertions validate those captured values.
        Rigidbody releasedBridgeBody = releasedBridge.GetComponent<Rigidbody>();
        Assert.That(bridgeBodyFrozenAtDisconnectBoundary, Is.SameAs(releasedBridgeBody),
            "Only a verified, zero-velocity disconnect release may enter the queued-transform isolation check.");
        try
        {
            for (int sample = 0; sample < 8; sample++)
            {
                yield return null;
                Assert.That(Vector3.Distance(releasedBridge.transform.position, bridgeCallbackPosition), Is.LessThan(0.02f),
                    "The mountable's server pose must not snap back to its former owner's queued transform.");
                observerBridge = Find(observer, bridgeId);
                Assert.That(observerBridge, Is.Not.Null);
                Assert.That(Vector3.Distance(observerBridge.transform.position, bridgeCallbackPosition), Is.LessThan(0.08f));
                Assert.That(Quaternion.Angle(observerBridge.transform.rotation, bridgeCallbackRotation), Is.LessThan(0.5f));
            }
        }
        finally
        {
            if (bridgeBodyFrozenAtDisconnectBoundary != null)
                bridgeBodyFrozenAtDisconnectBoundary.constraints = bridgeOriginalConstraints;
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
        sharedLog.SetActive(true);
        sharedLog.transform.position = new Vector3(24f, 1f, 11f);
        RestoreDisconnectFixturePhysics(sharedLog, sharedLogColliderEnabled, sharedLogIsKinematic, sharedLogUseGravity);
        NetworkObject sharedLogNetworkObject = sharedLog.GetComponent<NetworkObject>();
        sharedLogNetworkObject.SetSceneObjectStatus(false);
        Assert.That(sharedLogNetworkObject.IsSceneObject, Is.False,
            "The runtime shared-carry instance must be explicitly marked runtime-spawned, never scene-placed.");
        Assert.That(sharedLogNetworkPrefab.IsSpawned, Is.False,
            $"The separate registration template must remain unspawned before the runtime shared-carry instance is spawned. sceneObject={sharedLogNetworkPrefab.IsSceneObject}, runtimeInstance={sharedLogNetworkObject.name}");
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
        SetCarryOrientationOffset(resourcePrefab, new Vector3(0f, 90f, 0f));
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
        PressKey(Key.F);
        PressKey(Key.Escape);
        PressKey(Key.E);
        Assert.That((bool)PlacementControllerType.GetProperty("IsRequestPending").GetValue(controller), Is.True,
            "F, Escape, and E do not bypass an in-flight placement request.");

        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState(Key.UpArrow, Key.LeftCtrl, Key.RightArrow));
        InputSystem.Update();
        cameraObject.transform.position += Vector3.right;
        PlacementControllerType.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
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

        // The request's neutral gate survives its refusal. Modifier changes and the held arrow do
        // not rotate or catch up until the physical arrow is released; a fresh chord then steps once.
        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState(Key.RightArrow));
        InputSystem.Update();
        Quaternion blockedRotation = ghost.rotation;
        float neutralGateDeadline = Time.realtimeSinceStartup + .6f;
        while (Time.realtimeSinceStartup < neutralGateDeadline) yield return null;
        Assert.That(Quaternion.Angle(blockedRotation, ghost.rotation), Is.LessThan(.001f),
            "Releasing Ctrl while Right remains held cannot bypass the pending-request neutral gate or replay repeats.");

        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState());
        InputSystem.Update();
        yield return null;
        Quaternion beforeFreshChord = ghost.rotation;
        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState(Key.LeftCtrl, Key.RightArrow));
        InputSystem.Update();
        yield return null;
        Assert.That(Quaternion.Angle(Quaternion.AngleAxis(1f, Vector3.up) * beforeFreshChord, ghost.rotation),
            Is.LessThan(.1f), "After a real neutral release, a fresh Ctrl+Right chord applies exactly one Y step.");
        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState());
        InputSystem.Update();
        yield return null;

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

    private GameObject CreateNetworkCharacterSurfacePrefab(uint hash)
    {
        GameObject prefab = new GameObject("Stage 6 character surface without Rigidbody");
        assets.Add(prefab);
        prefab.AddComponent<CapsuleCollider>().height = 1.6f;
        prefab.GetComponent<CapsuleCollider>().radius = 0.32f;
        prefab.AddComponent<CharacterController>().height = 1.6f;
        SetPrefabHash(prefab.AddComponent<NetworkObject>(), hash);
        Type transformType = Type.GetType("ClientNetworkTransform, Assembly-CSharp");
        if (transformType != null) prefab.AddComponent(transformType);
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

    private static void SetCarryOrientationOffset(GameObject root, Vector3 eulerDegrees)
    {
        Component orientation = root.GetComponent(CarryPlacementOrientationType)
            ?? root.AddComponent(CarryPlacementOrientationType);
        CarryPlacementOrientationType.GetField("offsetEulerDegrees", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(orientation, eulerDegrees);
    }

    private static Quaternion ResolveCarryOrientationOffset(GameObject root)
    {
        MethodInfo resolve = CarryPlacementOrientationType.GetMethod("ResolveOffset", BindingFlags.Public | BindingFlags.Static);
        return (Quaternion)resolve.Invoke(null, new object[] { root });
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

    private static void SetRotationCounter(Component controller, string counterName, int value)
    {
        object state = PlacementControllerType.GetField("rotationState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        state.GetType().GetField(counterName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, value);
    }
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

    private static int CountEnabledColliders(GameObject source)
    {
        int count = 0;
        foreach (Collider collider in source.GetComponentsInChildren<Collider>(true))
            if (collider != null && collider.enabled) count++;
        return count;
    }

    private static string DescribeColliderStates(GameObject source)
    {
        return string.Join(",", source.GetComponentsInChildren<Collider>(true)
            .Select(collider => collider != null ? $"{collider.name}:{collider.enabled}" : "<destroyed>"));
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

    private static List<(int excludedLayers, int priority)> CaptureSolidContactSettings(NetworkObject source)
    {
        var settings = new List<(int excludedLayers, int priority)>();
        foreach (Collider collider in source.GetComponentsInChildren<Collider>(true))
            if (collider != null && !collider.isTrigger)
                settings.Add((collider.excludeLayers.value, collider.layerOverridePriority));
        return settings;
    }

    private static void AssertSingleCarryContactState(NetworkObject source,
        List<(int excludedLayers, int priority)> originalSettings, bool held, string label)
    {
        Assert.That(source, Is.Not.Null, label + " network object must exist.");
        Assert.That(IsPickedUp(source), Is.EqualTo(held), label + " replicated held state.");
        List<Collider> colliders = new List<Collider>();
        foreach (Collider collider in source.GetComponentsInChildren<Collider>(true))
            if (collider != null && !collider.isTrigger) colliders.Add(collider);
        Assert.That(colliders.Count, Is.EqualTo(originalSettings.Count), label + " solid collider count.");
        for (int i = 0; i < colliders.Count; i++)
        {
            Assert.That(colliders[i].excludeLayers.value,
                Is.EqualTo(held ? Physics.AllLayers : originalSettings[i].excludedLayers), label + " per-collider exclude mask.");
            Assert.That(colliders[i].layerOverridePriority,
                Is.EqualTo(held ? SingleCarryContactIsolationPriority : originalSettings[i].priority), label + " per-collider override priority.");
        }

        Rigidbody body = GetBody(source.gameObject);
        Assert.That(body, Is.Not.Null, label + " Rigidbody must exist.");
        if (held)
        {
            Assert.That(body.isKinematic, Is.True, label + " held Rigidbody must stay kinematic.");
            Assert.That(body.useGravity, Is.False, label + " held Rigidbody must suppress gravity.");
        }
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

    private static void SetCollidersEnabled(Collider[] colliders, bool enabled)
    {
        foreach (Collider collider in colliders)
            if (collider != null) collider.enabled = enabled;
    }

    private static void SetFixtureNormalMass(GameObject target, float mass)
    {
        Rigidbody body = target.GetComponent<Rigidbody>();
        Assert.That(body, Is.Not.Null);
        body.mass = mass;
        Component sharedCarryPhysics = target.GetComponent(Type.GetType("SharedCarryPhysicsBody, Assembly-CSharp"));
        if (sharedCarryPhysics == null) return;
        sharedCarryPhysics.GetType().GetField("normalMass", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(sharedCarryPhysics, mass);
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
