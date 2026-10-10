using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

public class SingleCarryPlacementStage5PlayModeTests
{
    private static readonly Type PreviewType = Type.GetType("PlayerSingleCarryPlacementNetworkPreview, Assembly-CSharp");
    private static readonly Type GhostType = Type.GetType("SingleCarryPlacementGhost, Assembly-CSharp");
    private NetworkManager priorSingleton;
    private bool capturedPriorSingleton;
    private readonly List<NetworkManager> fixtureManagers = new List<NetworkManager>();
    private readonly List<GameObject> fixtureRoots = new List<GameObject>();
    private readonly List<GameObject> fixtureObjects = new List<GameObject>();
    private readonly List<UnityEngine.Object> fixtureAssets = new List<UnityEngine.Object>();
    private readonly List<NetworkObject> fixtureNetworkObjects = new List<NetworkObject>();
    private GameObject fixturePlayerPrefab;
    private GameObject fixtureSourcePrefab;

    [UnityTearDown]
    public IEnumerator CleanupConnectedPeerFixture()
    {
        if (!capturedPriorSingleton && fixtureManagers.Count == 0) yield break;

        List<GameObject> networkCopies = CaptureFixtureNetworkCopies();

        foreach (NetworkObject networkObject in fixtureNetworkObjects)
            if (networkObject != null && networkObject.IsSpawned && networkObject.NetworkManager.IsServer)
                networkObject.Despawn(true);

        foreach (GameObject networkCopy in networkCopies)
        {
            NetworkObject networkObject = networkCopy != null ? networkCopy.GetComponent<NetworkObject>() : null;
            if (networkObject != null && networkObject.IsSpawned && networkObject.NetworkManager.IsServer)
                networkObject.Despawn(true);
        }

        for (int i = fixtureManagers.Count - 1; i >= 0; i--)
            if (fixtureManagers[i] != null && fixtureManagers[i].IsListening)
                fixtureManagers[i].Shutdown(true);

        float deadline = Time.realtimeSinceStartup + 12f;
        while (Time.realtimeSinceStartup < deadline && fixtureManagers.Exists(manager =>
            manager != null && (manager.IsListening || manager.ShutdownInProgress)))
            yield return null;
        bool shutdownComplete = !fixtureManagers.Exists(manager =>
            manager != null && (manager.IsListening || manager.ShutdownInProgress));

        foreach (GameObject root in fixtureRoots)
            if (root != null) UnityEngine.Object.Destroy(root);
        foreach (GameObject networkCopy in networkCopies)
            if (networkCopy != null) UnityEngine.Object.Destroy(networkCopy);
        foreach (GameObject fixtureObject in fixtureObjects)
            if (fixtureObject != null) UnityEngine.Object.Destroy(fixtureObject);
        yield return null;

        foreach (UnityEngine.Object asset in fixtureAssets)
            if (asset != null) UnityEngine.Object.Destroy(asset);
        yield return null;

        if (priorSingleton != null) priorSingleton.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
        fixtureManagers.Clear();
        fixtureRoots.Clear();
        fixtureObjects.Clear();
        fixtureAssets.Clear();
        fixtureNetworkObjects.Clear();
        fixturePlayerPrefab = null;
        fixtureSourcePrefab = null;
        priorSingleton = null;
        capturedPriorSingleton = false;
        Assert.That(shutdownComplete, Is.True, "A test NetworkManager did not finish shutdown before teardown.");
    }

    [UnityTest]
    public IEnumerator ConnectedPeersReplicateIndependentPreviewsLateJoinAndCleanup()
    {
        priorSingleton = NetworkManager.Singleton;
        capturedPriorSingleton = true;
        NetworkManager host = null;
        NetworkManager clientOne = null;
        NetworkManager clientTwo = null;
        NetworkManager lateClient = null;
        GameObject hostRoot = null;
        GameObject clientOneRoot = null;
        GameObject clientTwoRoot = null;
        GameObject lateClientRoot = null;
        GameObject playerPrefab = null;
        GameObject sourcePrefab = null;
        GameObject sourceOne = null;
        GameObject sourceTwo = null;
        ScriptableObject resourceProfile = null;
        NetworkObject sourceNetworkObject = null;
        NetworkObject sourceTwoNetworkObject = null;
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True,
            "The isolated loopback fixture cannot replace an already-listening project NetworkManager.");
        Assert.That(PreviewType, Is.Not.Null);
        Assert.That(GhostType, Is.Not.Null);
        Assert.That(PreviewType.GetMethod("SetOwnerPreview"), Is.Not.Null);

        playerPrefab = new GameObject("Stage 5 network preview player prefab");
        fixturePlayerPrefab = playerPrefab;
        fixtureAssets.Add(playerPrefab);
        SetPrefabHash(playerPrefab.AddComponent<NetworkObject>(), 0x51A50001);
        playerPrefab.AddComponent(PreviewType);

        sourcePrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fixtureSourcePrefab = sourcePrefab;
        fixtureAssets.Add(sourcePrefab);
        sourcePrefab.name = "Stage 5 network preview item prefab";
        sourcePrefab.transform.position = new Vector3(0f, -1000f, 0f);
        sourcePrefab.AddComponent<Rigidbody>();
        SetPrefabHash(sourcePrefab.AddComponent<NetworkObject>(), 0x51A50002);
        Component sourcePrefabBehaviour = sourcePrefab.AddComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"));
        resourceProfile = ScriptableObject.CreateInstance(Type.GetType("BaseResourceSO, Assembly-CSharp"));
        fixtureAssets.Add(resourceProfile);
        resourceProfile.GetType().GetField("canBeCarried").SetValue(resourceProfile, true);
        resourceProfile.GetType().GetField("allowMultipleCarriers").SetValue(resourceProfile, false);
        sourcePrefabBehaviour.GetType().GetField("baseResourceSO", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(sourcePrefabBehaviour, resourceProfile);

        ushort port = ReserveFreeUdpPort();
        host = CreateManager("Stage 5 loopback host", playerPrefab, sourcePrefab, port, out hostRoot);
        TrackManager(host, hostRoot);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.LocalClient?.PlayerObject != null, "host player spawn");

        clientOne = CreateManager("Stage 5 loopback owner one", playerPrefab, sourcePrefab, port, out clientOneRoot);
        TrackManager(clientOne, clientOneRoot);
        Assert.That(clientOne.StartClient(), Is.True);
        yield return Until(() => clientOne.IsConnectedClient && clientOne.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count >= 2, "first client connection and player spawn");
        ulong ownerOneId = clientOne.LocalClientId;

        clientTwo = CreateManager("Stage 5 loopback owner two", playerPrefab, sourcePrefab, port, out clientTwoRoot);
        TrackManager(clientTwo, clientTwoRoot);
        Assert.That(clientTwo.StartClient(), Is.True);
        yield return Until(() => clientTwo.IsConnectedClient && clientTwo.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count >= 3, "second client connection and player spawn");
        ulong ownerTwoId = clientTwo.LocalClientId;

        sourceOne = UnityEngine.Object.Instantiate(sourcePrefab);
        fixtureObjects.Add(sourceOne);
        sourceOne.name = "Stage 5 owner one held item";
        sourceOne.transform.position = Vector3.zero;
        sourceNetworkObject = sourceOne.GetComponent<NetworkObject>();
        fixtureNetworkObjects.Add(sourceNetworkObject);
        sourceNetworkObject.SpawnWithObservers = false;
        sourceNetworkObject.Spawn();
        AddHolder(sourceOne, ownerOneId);
        sourceNetworkObject.NetworkShow(ownerOneId);

        sourceTwo = UnityEngine.Object.Instantiate(sourcePrefab);
        fixtureObjects.Add(sourceTwo);
        sourceTwo.name = "Stage 5 owner two held item";
        sourceTwo.transform.position = Vector3.right * 2f;
        sourceTwoNetworkObject = sourceTwo.GetComponent<NetworkObject>();
        fixtureNetworkObjects.Add(sourceTwoNetworkObject);
        sourceTwoNetworkObject.SpawnWithObservers = false;
        sourceTwoNetworkObject.Spawn();
        AddHolder(sourceTwo, ownerTwoId);
        sourceTwoNetworkObject.NetworkShow(ownerTwoId);
        yield return Until(() => FindSpawnedObject(clientOne, sourceNetworkObject.NetworkObjectId) != null
            && FindSpawnedObject(clientTwo, sourceTwoNetworkObject.NetworkObjectId) != null,
            "each owner sees its own held source");

        Vector3 poseOne = new Vector3(2f, 3f, 4f);
        Quaternion rotationOne = Quaternion.Euler(10f, 20f, 30f);
        Vector3 poseTwo = new Vector3(-4f, 1.5f, 6f);
        Quaternion rotationTwo = Quaternion.Euler(-15f, 70f, 5f);
        SetOwnerPreview(clientOne.LocalClient.PlayerObject.GetComponent(PreviewType),
            FindSpawnedObject(clientOne, sourceNetworkObject.NetworkObjectId).gameObject, poseOne, rotationOne, true);
        SetOwnerPreview(clientTwo.LocalClient.PlayerObject.GetComponent(PreviewType),
            FindSpawnedObject(clientTwo, sourceTwoNetworkObject.NetworkObjectId).gameObject, poseTwo, rotationTwo, false);

        yield return Until(() => HasActiveState(GetPlayer(host, ownerOneId).GetComponent(PreviewType))
            && HasActiveState(GetPlayer(host, ownerTwoId).GetComponent(PreviewType)), "server accepting both owner preview states");
        yield return Until(() => HasRemoteGhost(GetPlayer(host, ownerOneId).GetComponent(PreviewType))
            && HasRemoteGhost(GetPlayer(host, ownerTwoId).GetComponent(PreviewType)), "host observing two simultaneous ghosts");

        Component ownerOneOnHost = GetPlayer(host, ownerOneId).GetComponent(PreviewType);
        Component ownerTwoOnHost = GetPlayer(host, ownerTwoId).GetComponent(PreviewType);
        Assert.That(GetRemoteGhost(clientOne.LocalClient.PlayerObject.GetComponent(PreviewType)), Is.Null,
            "An owning peer must not create a second network ghost over its local preview.");
        AssertGhost(ownerOneOnHost, poseOne, rotationOne, true);
        AssertGhost(ownerTwoOnHost, poseTwo, rotationTwo, false);

        lateClient = CreateManager("Stage 5 late loopback observer", playerPrefab, sourcePrefab, port, out lateClientRoot);
        TrackManager(lateClient, lateClientRoot);
        Assert.That(lateClient.StartClient(), Is.True);
        yield return Until(() => lateClient.IsConnectedClient && lateClient.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count >= 4, "late client connection and player spawn");
        ulong lateClientId = lateClient.LocalClientId;
        Component lateOwnerOne = GetPlayer(lateClient, ownerOneId).GetComponent(PreviewType);
        Component lateOwnerTwo = GetPlayer(lateClient, ownerTwoId).GetComponent(PreviewType);
        yield return Until(() => HasActiveState(lateOwnerOne) && HasActiveState(lateOwnerTwo),
            "late joiner receives persistent preview states");
        Assert.That(FindSpawnedObject(lateClient, sourceNetworkObject.NetworkObjectId), Is.Null,
            "The source intentionally remains hidden to the late joiner to exercise reference retry.");
        Assert.That(FindSpawnedObject(lateClient, sourceTwoNetworkObject.NetworkObjectId), Is.Null);
        Assert.That(GetRemoteGhost(lateOwnerOne), Is.Null);
        Assert.That(GetRemoteGhost(lateOwnerTwo), Is.Null);

        sourceNetworkObject.NetworkShow(lateClientId);
        sourceTwoNetworkObject.NetworkShow(lateClientId);
        yield return Until(() => HasRemoteGhost(lateOwnerOne) && HasRemoteGhost(lateOwnerTwo),
            "late joiner resolves delayed source references and creates both ghosts");
        AssertGhost(lateOwnerOne, poseOne, rotationOne, true);
        AssertGhost(lateOwnerTwo, poseTwo, rotationTwo, false);

        SetOwnerPreview(clientOne.LocalClient.PlayerObject.GetComponent(PreviewType),
            FindSpawnedObject(clientOne, sourceNetworkObject.NetworkObjectId).gameObject, poseOne, rotationOne, false);
        yield return Until(() => GhostMatches(ownerOneOnHost, poseOne, rotationOne, false)
            && GhostMatches(lateOwnerOne, poseOne, rotationOne, false),
            "validity-only change at the bounded network rate");
        Component clientOnePreview = clientOne.LocalClient.PlayerObject.GetComponent(PreviewType);
        GameObject clientOneSource = FindSpawnedObject(clientOne, sourceNetworkObject.NetworkObjectId).gameObject;
        Vector3 smoothedPose = poseOne + Vector3.right;
        Quaternion smoothedRotation = Quaternion.Euler(10f, 350f, 30f);
        Assert.That(GetRemoteInterpolationDuration(ownerOneOnHost), Is.EqualTo(0.05f).Within(0.0001f));
        Assert.That(GetRemoteInterpolationDuration(lateOwnerOne), Is.EqualTo(0.05f).Within(0.0001f));
        SetRemoteInterpolationDuration(ownerOneOnHost, 0.8f);
        SetRemoteInterpolationDuration(lateOwnerOne, 0.8f);
        yield return UntilOwnerPreview(clientOnePreview, clientOneSource, smoothedPose, smoothedRotation, false,
            () => PreviewTargetMatches(ownerOneOnHost, smoothedPose, smoothedRotation)
                && PreviewTargetMatches(lateOwnerOne, smoothedPose, smoothedRotation),
            "new smoothed pose snapshot reaches both observers");

        Vector3 hostFramePosition = GetGhostRoot(ownerOneOnHost).transform.position;
        Vector3 lateFramePosition = GetGhostRoot(lateOwnerOne).transform.position;
        yield return null;
        Assert.That(Vector3.Distance(hostFramePosition, GetGhostRoot(ownerOneOnHost).transform.position),
            Is.GreaterThan(0.0001f), "Observer pose should advance on rendered frames between network snapshots.");
        Assert.That(Vector3.Distance(lateFramePosition, GetGhostRoot(lateOwnerOne).transform.position),
            Is.GreaterThan(0.0001f), "Late-join observer pose should advance on rendered frames between snapshots.");
        Vector3 hostIntermediatePosition = GetGhostRoot(ownerOneOnHost).transform.position;
        Quaternion hostIntermediateRotation = GetGhostRoot(ownerOneOnHost).transform.rotation;
        Vector3 lateIntermediatePosition = GetGhostRoot(lateOwnerOne).transform.position;
        Quaternion lateIntermediateRotation = GetGhostRoot(lateOwnerOne).transform.rotation;
        yield return new WaitForSecondsRealtime(0.32f);
        AssertInterpolatedPose(poseOne, smoothedPose, GetGhostRoot(ownerOneOnHost).transform.position,
            "Host observer ghost should advance without overshooting.");
        AssertInterpolatedPose(poseOne, smoothedPose, GetGhostRoot(lateOwnerOne).transform.position,
            "Late-join observer ghost should advance without overshooting.");
        Assert.That(Vector3.Distance(hostIntermediatePosition, GetGhostRoot(ownerOneOnHost).transform.position),
            Is.GreaterThan(0.1f), "A scheduled appearance poll must not restart interpolation toward the unchanged target.");
        Assert.That(Vector3.Distance(lateIntermediatePosition, GetGhostRoot(lateOwnerOne).transform.position),
            Is.GreaterThan(0.1f), "Late-join appearance synchronization must not restart pose interpolation.");
        float totalRotation = Quaternion.Angle(rotationOne, smoothedRotation);
        float hostRotationProgress = Quaternion.Angle(rotationOne, GetGhostRoot(ownerOneOnHost).transform.rotation);
        float lateRotationProgress = Quaternion.Angle(rotationOne, GetGhostRoot(lateOwnerOne).transform.rotation);
        Assert.That(hostRotationProgress, Is.GreaterThan(1f).And.LessThan(totalRotation));
        Assert.That(lateRotationProgress, Is.GreaterThan(1f).And.LessThan(totalRotation));
        Assert.That(Quaternion.Angle(hostIntermediateRotation, GetGhostRoot(ownerOneOnHost).transform.rotation),
            Is.GreaterThan(0.1f));
        Assert.That(Quaternion.Angle(lateIntermediateRotation, GetGhostRoot(lateOwnerOne).transform.rotation),
            Is.GreaterThan(0.1f));

        yield return UntilOwnerPreview(clientOnePreview, clientOneSource, smoothedPose, smoothedRotation, true,
            () => PreviewValidityMatches(ownerOneOnHost, true) && PreviewValidityMatches(lateOwnerOne, true),
            "validation tint follows state immediately during motion");
        AssertGhostColor(ownerOneOnHost, true);
        AssertGhostColor(lateOwnerOne, true);
        AssertInterpolatedPose(poseOne, smoothedPose, GetGhostRoot(ownerOneOnHost).transform.position,
            "A validity update must not restart host pose interpolation.");
        AssertInterpolatedPose(poseOne, smoothedPose, GetGhostRoot(lateOwnerOne).transform.position,
            "A validity update must not restart late-join pose interpolation.");

        yield return Until(() => GhostMatches(ownerOneOnHost, smoothedPose, smoothedRotation, true)
            && GhostMatches(lateOwnerOne, smoothedPose, smoothedRotation, true)
            && !IsRemotePoseInterpolating(ownerOneOnHost) && !IsRemotePoseInterpolating(lateOwnerOne),
            "observer interpolation reaches the exact target in finite time");
        Assert.That(IsRemotePoseInterpolating(ownerOneOnHost), Is.False);
        Assert.That(IsRemotePoseInterpolating(lateOwnerOne), Is.False);
        for (int i = 0; i < 3; i++)
        {
            yield return null;
            AssertGhost(ownerOneOnHost, smoothedPose, smoothedRotation, true);
            AssertGhost(lateOwnerOne, smoothedPose, smoothedRotation, true);
        }
        Vector3 cancelledPose = smoothedPose + Vector3.up;
        Quaternion cancelledRotation = Quaternion.Euler(10f, 25f, 30f);
        yield return UntilOwnerPreview(clientOnePreview, clientOneSource, cancelledPose, cancelledRotation, false,
            () => PreviewTargetMatches(ownerOneOnHost, cancelledPose, cancelledRotation)
                && PreviewTargetMatches(lateOwnerOne, cancelledPose, cancelledRotation),
            "second pose begins before cancellation");
        Assert.That(IsRemotePoseInterpolating(ownerOneOnHost), Is.True);
        Assert.That(IsRemotePoseInterpolating(lateOwnerOne), Is.True);
        GameObject cancelledHostGhost = GetGhostRoot(ownerOneOnHost);
        GameObject cancelledLateGhost = GetGhostRoot(lateOwnerOne);
        PreviewType.GetMethod("ClearOwnerPreview").Invoke(clientOnePreview, null);
        yield return Until(() => !HasActiveState(ownerOneOnHost) && !HasActiveState(lateOwnerOne)
            && !HasRemoteGhost(ownerOneOnHost) && !HasRemoteGhost(lateOwnerOne), "clear while pose interpolation is active");
        yield return null;
        Assert.That(cancelledHostGhost == null, Is.True);
        Assert.That(cancelledLateGhost == null, Is.True);
        Assert.That(IsRemotePoseInterpolating(ownerOneOnHost), Is.False);
        Assert.That(IsRemotePoseInterpolating(lateOwnerOne), Is.False);

        SetOwnerPreview(clientOnePreview, clientOneSource, poseOne, rotationOne, true);
        yield return Until(() => HasRemoteGhost(ownerOneOnHost) && HasRemoteGhost(lateOwnerOne), "owner preview restart");
        AssertGhost(ownerOneOnHost, poseOne, rotationOne, true);
        AssertGhost(lateOwnerOne, poseOne, rotationOne, true);
        SetRemoteInterpolationDuration(ownerOneOnHost, 0.05f);
        SetRemoteInterpolationDuration(lateOwnerOne, 0.05f);

        GameObject ownerTwoHostGhost = GetGhostRoot(ownerTwoOnHost);
        GameObject ownerTwoLateGhost = GetGhostRoot(lateOwnerTwo);
        clientTwo.Shutdown();
        yield return Until(() => !host.ConnectedClients.ContainsKey(ownerTwoId), "owner disconnect removes its player object");
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.That(ownerTwoHostGhost == null, Is.True, "Host must dispose an active ghost when its source player despawns.");
        Assert.That(ownerTwoLateGhost == null, Is.True, "Observers must dispose an active ghost when its source player despawns.");

        RemoveHolder(sourceOne, ownerOneId);
        yield return Until(() => !HasActiveState(ownerOneOnHost) && !HasRemoteGhost(ownerOneOnHost)
            && !HasActiveState(lateOwnerOne) && !HasRemoteGhost(lateOwnerOne),
            "holder loss clears previews while the source remains spawned");
        Assert.That(sourceNetworkObject.IsSpawned, Is.True, "Holder loss must not require source despawn.");
        AddHolder(sourceOne, ownerOneId);
        yield return UntilOwnerPreview(clientOne.LocalClient.PlayerObject.GetComponent(PreviewType),
            FindSpawnedObject(clientOne, sourceNetworkObject.NetworkObjectId).gameObject,
            poseOne, rotationOne, true,
            () => HasRemoteGhost(ownerOneOnHost) && HasRemoteGhost(lateOwnerOne),
            "preview restarts after its source is held again");

        GameObject ownerOneHostGhost = GetGhostRoot(ownerOneOnHost);
        GameObject ownerOneLateGhost = GetGhostRoot(lateOwnerOne);
        sourceNetworkObject.Despawn(true);
        sourceNetworkObject = null;
        yield return Until(() => !HasActiveState(ownerOneOnHost) && !HasRemoteGhost(ownerOneOnHost)
            && !HasActiveState(lateOwnerOne) && !HasRemoteGhost(lateOwnerOne),
            "server clears preview state when the held source despawns");
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.That(ownerOneHostGhost == null, Is.True);
        Assert.That(ownerOneLateGhost == null, Is.True);

        clientOne.Shutdown();
        yield return Until(() => !host.ConnectedClients.ContainsKey(ownerOneId), "owner disconnect cleanup");

        if (sourceTwoNetworkObject != null && sourceTwoNetworkObject.IsSpawned)
        {
            sourceTwoNetworkObject.Despawn(true);
            sourceTwoNetworkObject = null;
        }
    }

    private List<GameObject> CaptureFixtureNetworkCopies()
    {
        var copies = new List<GameObject>();
        var seen = new HashSet<int>();
        foreach (NetworkObject candidate in Resources.FindObjectsOfTypeAll<NetworkObject>())
        {
            uint hash = candidate != null ? GetPrefabHash(candidate) : 0;
            if (candidate == null || candidate.gameObject == fixturePlayerPrefab || candidate.gameObject == fixtureSourcePrefab
                || hash != 0x51A50001 && hash != 0x51A50002
                || !candidate.gameObject.scene.IsValid() || !candidate.gameObject.scene.isLoaded
                || !candidate.name.EndsWith("(Clone)", StringComparison.Ordinal))
                continue;
            NetworkManager candidateManager = candidate.NetworkManager;
            if (!fixtureManagers.Contains(candidateManager) || !seen.Add(candidate.gameObject.GetInstanceID())) continue;
            copies.Add(candidate.gameObject);
        }
        return copies;
    }

    [UnityTest]
    public IEnumerator ObserverGhostTracksSourceMaterialsAndRendererState()
    {
        GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
        source.name = "replicated bucket visual";
        GameObject contents = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        contents.name = "replicated bucket contents";
        contents.transform.SetParent(source.transform, false);
        Renderer sourceContents = contents.GetComponent<Renderer>();
        sourceContents.forceRenderingOff = true;
        Material runtimeSourceMaterial = null;
        object ghost = GhostType.GetConstructor(new[] { typeof(GameObject) }).Invoke(new object[] { source });
        GameObject root = (GameObject)GhostType.GetProperty("Root").GetValue(ghost);
        try
        {
            Assert.That(root, Is.Not.Null);
            GhostType.GetMethod("SetPlacementValid").Invoke(ghost, new object[] { false });
            Color changed = new Color(0.8f, 0.2f, 0.1f, 1f);
            runtimeSourceMaterial = sourceContents.material;
            runtimeSourceMaterial.color = changed;
            sourceContents.enabled = false;
            yield return new WaitForSecondsRealtime(0.12f);
            GhostType.GetMethod("SyncSourceAppearance").Invoke(ghost, null);
            Renderer copiedContents = FindPreviewRenderer(root, contents.name);
            Assert.That(copiedContents, Is.Not.Null);
            Assert.That(copiedContents.enabled, Is.False);
            Assert.That(sourceContents.forceRenderingOff, Is.True, "Preview syncing must preserve owner-local source suppression.");
            Assert.That(copiedContents.forceRenderingOff, Is.False, "A ghost must not inherit forceRenderingOff from the source.");
            Color expected = Color.Lerp(changed, new Color(0.72f, 0.74f, 0.76f, 1f), 0.18f);
            expected.a = 0.52f;
            Assert.That(Vector4.Distance(copiedContents.sharedMaterial.GetColor("_BaseColor"), expected), Is.LessThan(0.01f));
            Assert.That(Vector4.Distance(copiedContents.sharedMaterial.GetColor("_ValidationColor"),
                new Color(1f, 0.08f, 0.06f, 1f)), Is.LessThan(0.01f),
                "A replaced source material should keep the ghost's current validation tint.");

            sourceContents.enabled = true;
            contents.SetActive(false);
            yield return new WaitForSecondsRealtime(0.12f);
            GhostType.GetMethod("SyncSourceAppearance").Invoke(ghost, null);
            Assert.That(copiedContents.gameObject.activeInHierarchy, Is.False);
        }
        finally
        {
            GhostType.GetMethod("Dispose").Invoke(ghost, null);
            if (runtimeSourceMaterial != null) UnityEngine.Object.Destroy(runtimeSourceMaterial);
            UnityEngine.Object.Destroy(source);
        }
        yield return null;
    }

    private static NetworkManager CreateManager(string name, GameObject playerPrefab, GameObject sourcePrefab,
        ushort port, out GameObject root)
    {
        root = new GameObject(name);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = playerPrefab,
            EnableSceneManagement = false
        };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = sourcePrefab });
        return manager;
    }

    private static IEnumerator Until(Func<bool> condition, string description)
    {
        float deadline = Time.realtimeSinceStartup + 12f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out waiting for " + description + ".");
    }

    private static IEnumerator UntilOwnerPreview(Component preview, GameObject source, Vector3 position,
        Quaternion rotation, bool valid, Func<bool> condition, string description)
    {
        float deadline = Time.realtimeSinceStartup + 12f;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            SetOwnerPreview(preview, source, position, rotation, valid);
            yield return null;
        }
        Assert.That(condition(), Is.True, "Timed out waiting for " + description + ".");
    }

    private static void SetOwnerPreview(Component preview, GameObject source, Vector3 position, Quaternion rotation, bool valid)
    {
        PreviewType.GetMethod("SetOwnerPreview").Invoke(preview, new object[] { source, position, rotation, valid });
    }

    private static void SetPrefabHash(NetworkObject networkObject, uint value)
    {
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(networkObject, value);
    }

    private static uint GetPrefabHash(NetworkObject networkObject)
    {
        return (uint)typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(networkObject);
    }

    private static void AddHolder(GameObject source, ulong clientId)
    {
        FieldInfo holdersField = source.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp")).GetType()
            .GetField("holderClientIds", BindingFlags.Instance | BindingFlags.NonPublic);
        ((List<ulong>)holdersField.GetValue(source.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp")))).Add(clientId);
    }

    private static void RemoveHolder(GameObject source, ulong clientId)
    {
        Component resource = source.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"));
        FieldInfo holdersField = resource.GetType().GetField("holderClientIds", BindingFlags.Instance | BindingFlags.NonPublic);
        ((List<ulong>)holdersField.GetValue(resource)).Remove(clientId);
    }

    private static NetworkObject GetPlayer(NetworkManager manager, ulong clientId)
    {
        if (manager.IsServer && manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            return client.PlayerObject;
        foreach (NetworkObject obj in manager.SpawnManager.SpawnedObjects.Values)
            if (obj != null && obj.IsPlayerObject && obj.OwnerClientId == clientId) return obj;
        return null;
    }

    private static NetworkObject FindSpawnedObject(NetworkManager manager, ulong networkObjectId)
    {
        return manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject obj) ? obj : null;
    }

    private static bool HasActiveState(Component preview)
    {
        if (preview == null) return false;
        object networkVariable = PreviewType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
        object value = networkVariable.GetType().GetProperty("Value").GetValue(networkVariable);
        return (bool)value.GetType().GetField("Active").GetValue(value);
    }

    private static object GetRemoteGhost(Component preview) => preview == null ? null
        : PreviewType.GetField("remoteGhost", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);

    private static bool HasRemoteGhost(Component preview) => GetRemoteGhost(preview) != null;

    private static bool PreviewTargetMatches(Component preview, Vector3 position, Quaternion rotation)
    {
        object networkVariable = PreviewType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(preview);
        object value = networkVariable.GetType().GetProperty("Value").GetValue(networkVariable);
        Vector3 targetPosition = (Vector3)value.GetType().GetField("Position").GetValue(value);
        Quaternion targetRotation = (Quaternion)value.GetType().GetField("Rotation").GetValue(value);
        return Vector3.Distance(targetPosition, position) < 0.001f
            && Quaternion.Angle(targetRotation, rotation) < 0.01f;
    }

    private static void SetRemoteInterpolationDuration(Component preview, float duration)
    {
        PreviewType.GetField("remoteInterpolationDuration", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(preview, duration);
    }

    private static float GetRemoteInterpolationDuration(Component preview)
    {
        return (float)PreviewType.GetField("remoteInterpolationDuration", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(preview);
    }

    private static bool IsRemotePoseInterpolating(Component preview)
    {
        return (bool)PreviewType.GetField("remotePoseInterpolating", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(preview);
    }

    private static bool PreviewValidityMatches(Component preview, bool valid)
    {
        object networkVariable = PreviewType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(preview);
        object value = networkVariable.GetType().GetProperty("Value").GetValue(networkVariable);
        return (bool)value.GetType().GetField("Valid").GetValue(value) == valid;
    }

    private static void AssertGhostColor(Component preview, bool valid)
    {
        Material material = GetGhostRoot(preview).GetComponentInChildren<Renderer>(true).sharedMaterial;
        Color expected = valid ? new Color(0.08f, 1f, 0.22f, 1f) : new Color(1f, 0.08f, 0.06f, 1f);
        Assert.That(Vector4.Distance(material.GetColor("_ValidationColor"), expected), Is.LessThan(0.02f));
    }

    private static void AssertInterpolatedPose(Vector3 start, Vector3 target, Vector3 actual, string message)
    {
        Vector3 path = target - start;
        float progress = Vector3.Dot(actual - start, path) / path.sqrMagnitude;
        Assert.That(progress, Is.GreaterThan(0.15f).And.LessThan(0.95f), message);
        Assert.That(Vector3.Distance(actual, Vector3.Lerp(start, target, progress)), Is.LessThan(0.01f),
            "The ghost should remain on the straight interpolation segment.");
    }

    private static GameObject GetGhostRoot(Component preview)
    {
        object ghost = GetRemoteGhost(preview);
        return ghost == null ? null : (GameObject)GhostType.GetProperty("Root").GetValue(ghost);
    }

    private static bool GhostMatches(Component preview, Vector3 position, Quaternion rotation, bool valid)
    {
        GameObject root = GetGhostRoot(preview);
        if (root == null || Vector3.Distance(root.transform.position, position) > 0.02f
            || Quaternion.Angle(root.transform.rotation, rotation) > 0.2f) return false;
        Material material = root.GetComponentInChildren<Renderer>(true)?.sharedMaterial;
        if (material == null || !material.HasProperty("_ValidationColor")) return false;
        Color expected = valid ? new Color(0.08f, 1f, 0.22f, 1f) : new Color(1f, 0.08f, 0.06f, 1f);
        return Vector4.Distance(material.GetColor("_ValidationColor"), expected) < 0.02f;
    }

    private static void AssertGhost(Component preview, Vector3 position, Quaternion rotation, bool valid)
    {
        Assert.That(GhostMatches(preview, position, rotation, valid), Is.True,
            "Observer ghost should match the owner pose and green/red validation state.");
    }

    private static Renderer FindPreviewRenderer(GameObject root, string sourceName)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            if (renderer.gameObject.name.StartsWith(sourceName)) return renderer;
        return null;
    }

    private static ushort ReserveFreeUdpPort()
    {
        using UdpClient listener = new UdpClient(0);
        return (ushort)((System.Net.IPEndPoint)listener.Client.LocalEndPoint).Port;
    }

    private void TrackManager(NetworkManager manager, GameObject root)
    {
        fixtureManagers.Add(manager);
        fixtureRoots.Add(root);
    }
}
