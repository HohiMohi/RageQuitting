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

public class MountingNailStage1NetworkPlayModeTests
{
    private readonly List<NetworkManager> managers = new();
    private readonly List<GameObject> roots = new();
    private readonly List<GameObject> playerCopies = new();
    private readonly List<NetworkObject> spawned = new();
    private NetworkManager previousSingleton;
    private bool capturedSingleton;

    [UnityTest]
    public IEnumerator ServerOwnsPreparedState_RemoteAndLateJoinMirrorKinematic_AndDisconnectLeavesWorldNail()
    {
        Debug.Log("[MountingNailStage1Network] begin");
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True,
            "This isolated loopback test requires no already-running NGO session.");
        previousSingleton = NetworkManager.Singleton;
        capturedSingleton = true;
#if UNITY_EDITOR
        GameObject playerPrefab = CreatePlayerPrefab();
#else
        GameObject playerPrefab = null;
#endif

#if UNITY_EDITOR
        GameObject nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
#else
        GameObject nailPrefab = null;
#endif
        Assert.That(nailPrefab, Is.Not.Null);
        ushort port = ReservePort();
        NetworkManager host = CreateManager("Mounting nail host", playerPrefab, nailPrefab, port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.LocalClient?.PlayerObject != null, "host player");
        CapturePlayerCopies();

        NetworkManager owner = CreateManager("Mounting nail remote client", playerPrefab, nailPrefab, port);
        Assert.That(owner.StartClient(), Is.True);
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 2, "remote owner client");
        CapturePlayerCopies();
        Debug.Log("[MountingNailStage1Network] host and remote connected");
        DisableFixturePlayerPhysics(host.LocalClient.PlayerObject);
        DisableFixturePlayerPhysics(host.ConnectedClients[owner.LocalClientId].PlayerObject);
        DisableFixturePlayerPhysics(owner.LocalClient.PlayerObject);

        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(target);
        target.transform.position = new Vector3(40f, 8f, 40f);
        target.transform.localScale = new Vector3(3f, 3f, 0.1f);
        GameObject nailObject = Object.Instantiate(nailPrefab);
        roots.Add(nailObject);
        System.Type nailType = System.Type.GetType("MountingNail, Assembly-CSharp");
        Component nail = nailObject.GetComponent(nailType);
        NetworkObject nailNetworkObject = nailObject.GetComponent<NetworkObject>();
        nailObject.transform.SetPositionAndRotation(
            target.transform.position + Vector3.back * (0.5f + ProfileFloat(nail, "LengthMeters") * 0.5f + 0.005f),
            Quaternion.identity);
        Physics.SyncTransforms();
        spawned.Add(nailNetworkObject);
        nailNetworkObject.Spawn();
        yield return Until(() => Find(owner, nailNetworkObject.NetworkObjectId) != null, "nail mirror on remote client");
        Debug.Log("[MountingNailStage1Network] nail spawned and mirrored");

        NetworkObject remoteMirror = Find(owner, nailNetworkObject.NetworkObjectId);
        DisableColliders(remoteMirror.gameObject);
        Assert.That(nailNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(remoteMirror.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(remoteMirror.GetComponent<Rigidbody>().isKinematic, Is.True,
            "Remote copies never simulate world physics themselves.");
        Assert.That(remoteMirror.GetComponent<Rigidbody>().useGravity, Is.False);
        AssertState(nail, "Loose");

        Component serverResource = nailObject.GetComponent(System.Type.GetType("BaseResourceNew, Assembly-CSharp"));
        System.Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        Component serverPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject.GetComponent(interactionType);
        Vector3 anchorOffset = (Vector3)interactionType.GetProperty("CarryBodyAnchorLocalOffset").GetValue(serverPlayer);
        float controllerRadius = (float)interactionType.GetMethod("GetCharacterControllerRadius").Invoke(serverPlayer, null);
        bool pickedUp = (bool)serverResource.GetType().GetMethod("TryCompleteNetworkPickup", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(serverResource, new object[] { owner.LocalClientId, anchorOffset, controllerRadius });
        Assert.That(pickedUp, Is.True,
            "The remote player with production carry and placement components must pick up the nail on the server.");
        yield return Until(() => nailNetworkObject.OwnerClientId == owner.LocalClientId && State(nail) == "Held", "server nail Held state");
        yield return Until(() => Find(owner, nailNetworkObject.NetworkObjectId).OwnerClientId == owner.LocalClientId
            && State(Find(owner, nailNetworkObject.NetworkObjectId).GetComponent(nail.GetType())) == "Held", "client nail Held state");
        Debug.Log("[MountingNailStage1Network] remote pickup confirmed");

        NetworkObject ownerPlayerObject = owner.LocalClient.PlayerObject;
        Component ownerInteraction = ownerPlayerObject.GetComponent(interactionType);
        NetworkObject ownerSource = Find(owner, nailNetworkObject.NetworkObjectId);
        MethodInfo confirmPickup = System.Array.Find(interactionType.GetMethods(), method =>
            method.Name == "ConfirmPickedUpObject" && method.GetParameters().Length == 6);
        Assert.That(confirmPickup, Is.Not.Null);
        confirmPickup.Invoke(ownerInteraction, new object[] { ownerSource.gameObject,
            ownerSource.GetComponent(serverResource.GetType()), false, 0f, false, Vector3.zero });
        Component ownerPreview = ownerPlayerObject.GetComponent(System.Type.GetType("PlayerSingleCarryPlacementNetworkPreview, Assembly-CSharp"));
        Assert.That(ownerPreview, Is.Not.Null);
        Transform aimAnchor = (Transform)interactionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(serverPlayer);
        NetworkTransform authoritativeTransform = serverPlayer.GetComponent<NetworkTransform>();
        Vector3 anchorLocalOffset = serverPlayer.transform.InverseTransformPoint(aimAnchor.position);
        Vector3 authoritativePosition = authoritativeTransform.GetSpaceRelativePosition(true);
        Quaternion authoritativeRotation = authoritativeTransform.GetSpaceRelativeRotation(true);
        if (authoritativeTransform.InLocalSpace && serverPlayer.transform.parent != null)
        {
            authoritativePosition = serverPlayer.transform.parent.TransformPoint(authoritativePosition);
            authoritativeRotation = serverPlayer.transform.parent.rotation * authoritativeRotation;
        }
        Vector3 trustedAimOrigin = authoritativePosition
            + authoritativeRotation * Vector3.Scale(anchorLocalOffset, serverPlayer.transform.localScale);
        Vector3 aimDirection = authoritativeRotation * Vector3.forward;
        target.transform.SetPositionAndRotation(trustedAimOrigin + aimDirection * 1.5f,
            Quaternion.LookRotation(aimDirection, Vector3.up));
        Physics.SyncTransforms();
        Ray aimRay = new Ray(trustedAimOrigin, aimDirection);
        Quaternion baseYaw = authoritativeRotation;
        System.Type geometryType = System.Type.GetType("SingleCarryPlacementGeometrySolver, Assembly-CSharp");
        object geometry = System.Activator.CreateInstance(geometryType, new object[] { nailObject });
        float placementDistance = (float)ownerPreview.GetType().GetProperty("PlacementDistance").GetValue(ownerPreview);
        object computedPose = geometryType.GetMethod("Calculate").Invoke(geometry, new object[]
            { aimRay, placementDistance, baseYaw, Quaternion.identity, ownerPlayerObject.transform });
        Vector3 acceptedPosition = (Vector3)computedPose.GetType().GetProperty("Position").GetValue(computedPose);
        Quaternion acceptedRotation = (Quaternion)computedPose.GetType().GetProperty("Rotation").GetValue(computedPose);
        MethodInfo request = ownerPreview.GetType().GetMethod("RequestNetworkPlacement");
        bool nailCanPrepare = (bool)nail.GetType().GetMethod("CanPrepareAtPose", BindingFlags.Instance | BindingFlags.Public)
            .Invoke(nail, new object[] { acceptedPosition, acceptedRotation });
        Transform tip = (Transform)nail.GetType().GetProperty("Tip", BindingFlags.Instance | BindingFlags.Public).GetValue(nail);
        Vector3 acceptedTip = acceptedPosition + acceptedRotation * nailObject.transform.InverseTransformPoint(tip.position);
        Assert.That(nailCanPrepare, Is.True,
            $"The accepted pose must put the tip into the same real target surface. status={computedPose.GetType().GetProperty("Status").GetValue(computedPose)}, collider={computedPose.GetType().GetProperty("SurfaceCollider").GetValue(computedPose)}, pos={acceptedPosition}, rot={acceptedRotation.eulerAngles}, tip={acceptedTip}, target={target.GetComponent<Collider>().ClosestPoint(acceptedTip)}, aim={aimRay.direction}, surface={computedPose.GetType().GetProperty("SurfaceNormal").GetValue(computedPose)}");
        request.Invoke(ownerPreview, new object[] { ownerSource, 1UL, acceptedPosition + Vector3.right, acceptedRotation, aimRay });
        yield return Until(() => (ulong)ownerPreview.GetType().GetProperty("LastCompletedPlacementRequestId").GetValue(ownerPreview) == 1UL, "invalid network placement");
        Assert.That(ownerPreview.GetType().GetProperty("LastPlacementResult").GetValue(ownerPreview).ToString(), Is.EqualTo("InvalidPose"));
        Debug.Log("[MountingNailStage1Network] invalid request rejected");
        AssertState(nail, "Held", "A refused network request must not invoke the commit listener.");
        request.Invoke(ownerPreview, new object[] { ownerSource, 2UL, acceptedPosition, acceptedRotation, aimRay });
        yield return Until(() => (ulong)ownerPreview.GetType().GetProperty("LastCompletedPlacementRequestId").GetValue(ownerPreview) == 2UL, "accepted network placement");
        Assert.That(ownerPreview.GetType().GetProperty("LastPlacementResult").GetValue(ownerPreview).ToString(), Is.EqualTo("Accepted"));
        NetworkObject ownerMirror = Find(owner, nailNetworkObject.NetworkObjectId);
        Rigidbody ownerMirrorBody = ownerMirror.GetComponent<Rigidbody>();
        float prepareDeadline = Time.realtimeSinceStartup + 5f;
        while ((State(ownerMirror.GetComponent(nail.GetType())) != "Prepared"
                || ownerMirror.OwnerClientId != NetworkManager.ServerClientId)
            && Time.realtimeSinceStartup < prepareDeadline)
        {
            Assert.That(ownerMirrorBody.isKinematic, Is.True,
                "The remote world nail must remain kinematic while the accepted release and server state replicate.");
            yield return null;
        }
        Assert.That(State(nail), Is.EqualTo("Prepared"), "The accepted server commit must prepare the nail after holder cleanup.");
        Assert.That(State(ownerMirror.GetComponent(nail.GetType())), Is.EqualTo("Prepared"), "The owner mirror must observe Prepared.");
        Debug.Log("[MountingNailStage1Network] accepted request prepared nail");
        Assert.That(nailNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(nailObject.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(ownerMirror.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(ownerMirrorBody.isKinematic, Is.True);
        double preparedDeadline = (double)nail.GetType().GetField("preparedDeadline", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(nail);
        request.Invoke(ownerPreview, new object[] { ownerSource, 2UL, acceptedPosition, acceptedRotation, aimRay });
        yield return Until(() => ownerPreview.GetType().GetProperty("LastPlacementResult").GetValue(ownerPreview).ToString() == "StaleRequest",
            "duplicate request rejected as stale");
        Assert.That((double)nail.GetType().GetField("preparedDeadline", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(nail),
            Is.EqualTo(preparedDeadline), "A stale duplicate cannot reset the prepared timer.");

        nail.GetType().GetMethod("OnSingleCarryPlacementCommitted", BindingFlags.Instance | BindingFlags.Public)
            .Invoke(remoteMirror.GetComponent(nail.GetType()), new object[] { null });
        yield return null;
        AssertState(nail, "Prepared", "A non-authoritative mirror cannot change replicated state.");
        NetworkManager lateJoin = CreateManager("Mounting nail late-join client", playerPrefab, nailPrefab, port);
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && lateJoin.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 3, "late join client");
        CapturePlayerCopies();
        yield return Until(() => Find(lateJoin, nailNetworkObject.NetworkObjectId) != null
            && State(Find(lateJoin, nailNetworkObject.NetworkObjectId).GetComponent(nail.GetType())) == "Prepared",
            "late join prepared state snapshot");
        Debug.Log("[MountingNailStage1Network] late join received Prepared snapshot");
        NetworkObject lateMirror = Find(lateJoin, nailNetworkObject.NetworkObjectId);
        DisableColliders(lateMirror.gameObject);
        Assert.That(lateMirror.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(lateMirror.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(lateMirror.GetComponent<Rigidbody>().useGravity, Is.False);
        Assert.That(Find(owner, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(Find(lateJoin, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True);

        target.transform.position += Vector3.right * (ProfileFloat(nail, "AllowedTargetDisplacementMeters") + 0.02f);
        Physics.SyncTransforms();
        yield return Until(() => State(nail) == "Loose", "server prepared exit after target displacement");
        yield return Until(() => State(Find(owner, nailNetworkObject.NetworkObjectId).GetComponent(nail.GetType())) == "Loose"
            && State(Find(lateJoin, nailNetworkObject.NetworkObjectId).GetComponent(nail.GetType())) == "Loose",
            "loose state replication to all clients");
        Debug.Log("[MountingNailStage1Network] exit replicated to all peers");
        Assert.That(nailObject.GetComponent<Rigidbody>().isKinematic, Is.False,
            "Only the server resumes dynamic physics when the nail becomes loose.");
        Assert.That(Find(owner, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(Find(lateJoin, nailNetworkObject.NetworkObjectId).GetComponent<Rigidbody>().isKinematic, Is.True);


        owner.Shutdown(true);
        yield return Until(() => host.ConnectedClientsIds.Count == 2, "remote owner disconnect");
        Assert.That(nailNetworkObject.IsSpawned, Is.True);
        Assert.That(nailNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(State(nail), Is.EqualTo("Loose"));
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        bool inputMapsDisabled = true;
        bool managersStopped = false;
        bool noPlayerClonesRemain = false;
        try
        {
            CapturePlayerCopies();
            List<InputActionAsset> inputAssets = CapturePlayerInputAssets();
            try { DisposePlayerInputMaps(); }
            catch (System.Exception exception) { Debug.LogException(exception); inputMapsDisabled = false; }
            foreach (InputActionAsset asset in inputAssets)
                if (asset != null)
                    foreach (InputActionMap map in asset.actionMaps)
                        inputMapsDisabled &= !map.enabled;
            foreach (NetworkObject obj in spawned)
                if (obj != null && obj.IsSpawned && obj.NetworkManager != null && obj.NetworkManager.IsServer)
                    obj.Despawn(true);
            foreach (NetworkManager manager in managers)
                if (manager != null && manager.IsListening) manager.Shutdown(true);
            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline
                && managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress))) yield return null;
            managersStopped = !managers.Exists(m => m != null && (m.IsListening || m.ShutdownInProgress));
            CapturePlayerCopies();
            foreach (GameObject playerCopy in playerCopies)
                if (playerCopy != null) Object.Destroy(playerCopy);
            foreach (GameObject root in roots)
                if (root != null) Object.Destroy(root);
            yield return null;
            noPlayerClonesRemain = FindPlayerCopies().Count == 0 && !playerCopies.Exists(copy => copy != null);
        }
        finally
        {
            foreach (NetworkManager manager in managers)
                if (manager != null && manager.IsListening) manager.Shutdown(true);
            foreach (GameObject playerCopy in playerCopies)
                if (playerCopy != null) Object.Destroy(playerCopy);
            foreach (GameObject playerCopy in FindPlayerCopies())
                if (playerCopy != null) Object.Destroy(playerCopy);
            foreach (GameObject root in roots)
                if (root != null) Object.Destroy(root);
            if (capturedSingleton)
            {
                if (previousSingleton != null) previousSingleton.SetSingleton();
                else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            }
            managers.Clear(); roots.Clear(); playerCopies.Clear(); spawned.Clear();
        }
        Assert.That(inputMapsDisabled, Is.True, "Fixture input maps must be disabled before player teardown.");
        Assert.That(managersStopped, Is.True, "Every fixture NGO manager must finish shutdown before its player copies are destroyed.");
        Assert.That(noPlayerClonesRemain, Is.True, "No Stage 1 fixture player clone may remain in the loaded scene after teardown.");
    }

    private List<InputActionAsset> CapturePlayerInputAssets()
    {
        List<InputActionAsset> assets = new();
        System.Type inputType = System.Type.GetType("PlayerInputNew, Assembly-CSharp");
        FieldInfo actionsField = inputType?.GetField("playerGameInputActions", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (GameObject player in playerCopies)
        {
            if (player == null || actionsField == null) continue;
            Component input = player.GetComponent(inputType);
            object wrapper = input != null ? actionsField.GetValue(input) : null;
            InputActionAsset asset = wrapper?.GetType().GetProperty("asset")?.GetValue(wrapper) as InputActionAsset;
            if (asset != null && !assets.Contains(asset)) assets.Add(asset);
        }
        return assets;
    }

    private void CapturePlayerCopies()
    {
        HashSet<int> seen = new();
        foreach (GameObject copy in playerCopies)
            if (copy != null) seen.Add(copy.GetInstanceID());
        foreach (GameObject copy in FindPlayerCopies())
        {
            if (copy == null || !seen.Add(copy.GetInstanceID())) continue;
            playerCopies.Add(copy);
        }
    }

    private static List<GameObject> FindPlayerCopies()
    {
        List<GameObject> copies = new();
        foreach (NetworkObject candidate in Resources.FindObjectsOfTypeAll<NetworkObject>())
        {
            if (candidate == null || GetPrefabHash(candidate) != 0x4E41494Cu
                || !candidate.name.StartsWith("Mounting Nail Stage 1 minimal player fixture", System.StringComparison.Ordinal)
                || !candidate.name.EndsWith("(Clone)", System.StringComparison.Ordinal)) continue;
            copies.Add(candidate.gameObject);
        }
        return copies;
    }

    private static uint GetPrefabHash(NetworkObject networkObject) =>
        (uint)typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(networkObject);

    private void DisposePlayerInputMaps()
    {
        System.Type inputType = System.Type.GetType("PlayerInputNew, Assembly-CSharp");
        MethodInfo disposeInput = inputType?.GetMethod("DisposeInput", BindingFlags.Instance | BindingFlags.NonPublic);
        if (disposeInput == null) return;
        HashSet<int> seen = new();
        foreach (GameObject player in playerCopies)
        {
            if (player == null || !seen.Add(player.GetInstanceID())) continue;
            Component input = player.GetComponent(inputType);
            if (input != null) disposeInput.Invoke(input, null);
        }
        foreach (NetworkManager manager in managers)
        {
            if (manager == null || manager.SpawnManager == null) continue;
            foreach (NetworkObject player in manager.SpawnManager.SpawnedObjects.Values)
            {
                if (player == null || !seen.Add(player.GetInstanceID())) continue;
                Component input = player.GetComponent(inputType);
                if (input != null) disposeInput.Invoke(input, null);
            }
        }
    }

    private static void DisableFixturePlayerPhysics(NetworkObject player)
    {
        DisableColliders(player.gameObject);
        foreach (Behaviour behaviour in player.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour is NetworkBehaviour) continue;
            string typeName = behaviour.GetType().Name;
            if (typeName == "PlayerInteractionNew" || typeName == "PlayerHealth"
                || typeName == "PlayerSingleCarryPlacementNetworkPreview" || typeName == "ClientNetworkTransform")
                continue;
            behaviour.enabled = false;
        }
    }

    private GameObject CreatePlayerPrefab()
    {
        GameObject prefab = new("Mounting Nail Stage 1 minimal player fixture");
        roots.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(networkObject, 0x4E41494Cu);

        System.Type inputType = System.Type.GetType("PlayerInputNew, Assembly-CSharp");
        Assert.That(inputType, Is.Not.Null);
        Behaviour input = (Behaviour)prefab.AddComponent(inputType);
        input.enabled = false;
        System.Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        Component interaction = prefab.AddComponent(interactionType);
        prefab.AddComponent(System.Type.GetType("PlayerHealth, Assembly-CSharp"));
        prefab.AddComponent(System.Type.GetType("PlayerSingleCarryPlacementController, Assembly-CSharp"));
        prefab.AddComponent(System.Type.GetType("PlayerSingleCarryPlacementNetworkPreview, Assembly-CSharp"));
        prefab.AddComponent(System.Type.GetType("ClientNetworkTransform, Assembly-CSharp"));

        Transform anchor = new GameObject("SingleCarryPlacementAimAnchor").transform;
        anchor.SetParent(prefab.transform, false);
        anchor.localPosition = new Vector3(0f, 1.1f, 0f);
        interactionType.GetField("interactionOrigin", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(interaction, anchor);
        return prefab;
    }

    private static void DisableColliders(GameObject root)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    private NetworkManager CreateManager(string name, GameObject playerPrefab, GameObject nailPrefab, ushort port)
    {
        GameObject root = new(name);
        roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = playerPrefab,
            EnableSceneManagement = false
        };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = nailPrefab });
        managers.Add(manager);
        return manager;
    }

    private static NetworkObject Find(NetworkManager manager, ulong id) =>
        manager != null && manager.SpawnManager != null && manager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject value)
            ? value : null;

    private static float ProfileFloat(Component nail, string property) => (float)nail.GetType().GetProperty("Profile", BindingFlags.Instance | BindingFlags.Public).GetValue(nail).GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public).GetValue(nail.GetType().GetProperty("Profile", BindingFlags.Instance | BindingFlags.Public).GetValue(nail));

    private static string State(Component nail) => nail.GetType().GetProperty("State", BindingFlags.Instance | BindingFlags.Public)
        .GetValue(nail).ToString();
    private static void AssertState(Component nail, string state, string message = null) => Assert.That(State(nail), Is.EqualTo(state), message);
    private static ushort ReservePort()
    {
        for (ushort port = 46106; port < 46206; port++)
        {
            try { using UdpClient listener = new(port); return port; }
            catch (SocketException) { }
        }
        throw new System.InvalidOperationException("No free local UDP port for MountingNail test.");
    }

    private static IEnumerator Until(System.Func<bool> condition, string label)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out waiting for " + label + ".");
    }
}














