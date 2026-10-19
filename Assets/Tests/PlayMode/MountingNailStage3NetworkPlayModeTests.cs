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
using UnityEngine.TestTools;

public class MountingNailStage3NetworkPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const uint StatePrefabHash = 0x4E415334u;
    private const uint TargetPrefabHash = 0x4E415335u;
    private readonly List<NetworkManager> managers = new();
    private readonly List<GameObject> roots = new();
    private readonly List<ScriptableObject> clonedProfiles = new();
    private NetworkManager previousSingleton;
    private bool capturedSingleton;
    private NetworkManager host;
    private NetworkManager client;
    private NetworkManager lateJoin;
    private NetworkManager tombstoneJoin;
    private GameObject statePrefab;
    private GameObject networkTargetPrefab;
    private GameObject nailPrefab;
    private GameObject hostTarget;
    private GameObject clientTarget;
    private GameObject lateTarget;
    private GameObject tombstoneTarget;
    private NetworkObject stateNetworkObject;
    private Component hostState;
    private Component clientState;
    private Component lateState;
    private Component tombstoneState;
    private Type stateType;
    private Type identityType;
    private bool hostStopped;
    private bool noRoots;

    [UnityTest]
    public IEnumerator SceneTargetStateIsServerOwnedScopedAndRestoredFromLateJoinSnapshot()
    {
#if !UNITY_EDITOR
        Assert.Fail("This integration test requires authored Unity networking components.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        previousSingleton = NetworkManager.Singleton;
        capturedSingleton = true;
        BuildFixtures();
        ushort port = ReservePort();
        host = CreateManager("Mounting Nail Stage 3 host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "host start");

        client = CreateManager("Mounting Nail Stage 3 client", port);
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "remote peer");

        stateNetworkObject = UnityEngine.Object.Instantiate(statePrefab).GetComponent<NetworkObject>();
        roots.Add(stateNetworkObject.gameObject);
        stateNetworkObject.SetSceneObjectStatus(false);
        stateNetworkObject.Spawn();
        hostState = stateNetworkObject.GetComponent(stateType);
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(stateNetworkObject.NetworkObjectId), "replicated state object");
        clientState = client.SpawnManager.SpawnedObjects[stateNetworkObject.NetworkObjectId].GetComponent(stateType);
        Assert.That(Register(hostState, hostTarget), Is.True);
        Assert.That(Register(clientState, clientTarget), Is.True);
        GameObject clientOnlyTarget = CreateTarget("client-only-scene-target", new Vector3(105f, 30f, 100f));
        Assert.That(Register(clientState, clientOnlyTarget), Is.True);
        object clientOnlyReference = CreateReference(clientState, clientOnlyTarget);
        Assert.That(InvokeBool(hostState, "TryResolve", clientOnlyReference, null), Is.False,
            "Peer-local scene identity indexes must not leak across NetworkManagers.");

        object reference = CreateReference(hostState, hostTarget);
        Assert.That(InvokeBool(hostState, "SetTargetConnected", reference, true), Is.True);
        yield return Until(() => InvokeBool(clientState, "IsTargetLocked", reference), "replicated target lock");
        Assert.That(InvokeBool(clientState, "SetTargetConnected", reference, false), Is.False,
            "A client cannot publish connected state.");

        NetworkObject spawnedTarget = UnityEngine.Object.Instantiate(networkTargetPrefab).GetComponent<NetworkObject>();
        spawnedTarget.SetSceneObjectStatus(false);
        spawnedTarget.Spawn();
        GameObject spawnedPhysicalRoot = spawnedTarget.transform.GetChild(0).gameObject;
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(spawnedTarget.NetworkObjectId),
            "network target child arrives at client");
        object spawnedReference = CreateReference(hostState, spawnedPhysicalRoot);
        object[] resolvedChildArgs = { spawnedReference, null };
        bool childResolved = (bool)stateType.GetMethod("TryResolve", PublicInstance).Invoke(clientState, resolvedChildArgs);
        Assert.That(childResolved, Is.True,
            "NetworkObject references resolve through the matching manager and child path without scene markers.");
        NetworkObject clientTargetObject = client.SpawnManager.SpawnedObjects[spawnedTarget.NetworkObjectId];
        Assert.That(resolvedChildArgs[1], Is.SameAs(clientTargetObject.transform.GetChild(0)),
            "The reference resolves to the selected physical Rigidbody child, not the NetworkObject root.");
        ulong oldNetworkId = (ulong)spawnedReference.GetType().GetField("NetworkObjectId").GetValue(spawnedReference);
        spawnedTarget.Despawn(true);
        yield return Until(() => !InvokeBool(hostState, "TryResolve", spawnedReference, null)
            && !InvokeBool(clientState, "TryResolve", spawnedReference, null), "despawn invalidates exact network target reference");
        NetworkObject replacementTarget = UnityEngine.Object.Instantiate(networkTargetPrefab).GetComponent<NetworkObject>();
        replacementTarget.SetSceneObjectStatus(false);
        replacementTarget.Spawn();
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(replacementTarget.NetworkObjectId),
            "replacement spawned target arrives at client");
        object replacementReference = CreateReference(hostState, replacementTarget.transform.GetChild(0).gameObject);
        Assert.That((ulong)replacementReference.GetType().GetField("NetworkObjectId").GetValue(replacementReference), Is.Not.EqualTo(oldNetworkId));
        Assert.That(InvokeBool(hostState, "TryResolve", spawnedReference, null), Is.False,
            "A recycled prefab/child path cannot replace the selected spawned object.");
        GameObject replacementPhysicalRoot = replacementTarget.transform.GetChild(0).gameObject;
        object replacementChildReference = CreateReference(hostState, replacementPhysicalRoot);
        Rigidbody authoritativeChildBody = replacementPhysicalRoot.GetComponent<Rigidbody>();
        authoritativeChildBody.isKinematic = true;
        authoritativeChildBody.position = new Vector3(14f, 6f, -7f);
        authoritativeChildBody.rotation = Quaternion.Euler(17f, 28f, 39f);
        Assert.That(InvokeBool(hostState, "SetTargetConnected", replacementChildReference, true), Is.True);
        NetworkObject clientReplacementTarget = client.SpawnManager.SpawnedObjects[replacementTarget.NetworkObjectId];
        Transform clientPhysicalRoot = clientReplacementTarget.transform.GetChild(0);
        yield return Until(() => InvokeBool(clientState, "IsTargetLocked", replacementChildReference)
            && Vector3.Distance(clientPhysicalRoot.position, replacementPhysicalRoot.transform.position) < 0.02f
            && Quaternion.Angle(clientPhysicalRoot.rotation, replacementPhysicalRoot.transform.rotation) < 0.2f,
            "connected Rigidbody child pose replicates through the owning manager");
        Assert.That(clientPhysicalRoot.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(clientPhysicalRoot.GetComponents<FixedJoint>().Length, Is.Zero,
            "Peers receive authoritative attachment pose without constructing their own physics joint.");

        Rigidbody authoritativeBody = hostTarget.GetComponent<Rigidbody>();
        authoritativeBody.isKinematic = true;
        authoritativeBody.position = new Vector3(11f, 4f, -3f);
        authoritativeBody.rotation = Quaternion.Euler(0f, 25f, 45f);
        yield return Until(() => Vector3.Distance(clientTarget.transform.position, hostTarget.transform.position) < 0.02f
            && Quaternion.Angle(clientTarget.transform.rotation, hostTarget.transform.rotation) < 0.2f,
            "server pose and rotation reach peer-local scene target");
        Assert.That(clientTarget.GetComponent<Rigidbody>().isKinematic, Is.True,
            "The remote scene body is kinematic while the replicated connection is active.");
        clientTarget.GetComponent<Rigidbody>().isKinematic = false;
        clientTarget.GetComponent<Rigidbody>().useGravity = true;
        yield return new WaitForFixedUpdate();
        Assert.That(clientTarget.GetComponent<Rigidbody>().isKinematic, Is.True,
            "A stationary connected replica remains server-physics-controlled after another callback changes body flags.");
        Assert.That(clientTarget.GetComponent<Rigidbody>().useGravity, Is.False);

        lateJoin = CreateManager("Mounting Nail Stage 3 late join", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3, "late join peer");
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(stateNetworkObject.NetworkObjectId), "late state snapshot");
        lateState = lateJoin.SpawnManager.SpawnedObjects[stateNetworkObject.NetworkObjectId].GetComponent(stateType);
        Assert.That(Register(lateState, lateTarget), Is.True);
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(replacementTarget.NetworkObjectId),
            "late join network target child arrives");
        Transform latePhysicalRoot = lateJoin.SpawnManager.SpawnedObjects[replacementTarget.NetworkObjectId].transform.GetChild(0);
        yield return Until(() => InvokeBool(lateState, "IsTargetLocked", replacementChildReference)
            && Vector3.Distance(latePhysicalRoot.position, replacementPhysicalRoot.transform.position) < 0.02f
            && Quaternion.Angle(latePhysicalRoot.rotation, replacementPhysicalRoot.transform.rotation) < 0.2f,
            "late join receives connected child pose snapshot");
        Assert.That(latePhysicalRoot.GetComponent<Rigidbody>().isKinematic, Is.True);
        yield return Until(() => InvokeBool(lateState, "IsTargetLocked", reference), "late join connected snapshot");
        Assert.That(lateTarget.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(Vector3.Distance(lateTarget.transform.position, hostTarget.transform.position), Is.LessThan(0.02f),
            "The late joiner receives the current connected pose.");

        Assert.That(InvokeBool(hostState, "SetTargetConnected", reference, false), Is.True);
        yield return Until(() => !clientTarget.GetComponent<Rigidbody>().isKinematic
            && !lateTarget.GetComponent<Rigidbody>().isKinematic, "unlock restores original body flags");
        Assert.That(clientTarget.GetComponent<Rigidbody>().useGravity, Is.True);
        Assert.That(lateTarget.GetComponent<Rigidbody>().useGravity, Is.True);
        Assert.That(clientTarget.GetComponent<Rigidbody>().constraints, Is.EqualTo(RigidbodyConstraints.FreezeAll));
        Assert.That(lateTarget.GetComponent<Rigidbody>().constraints, Is.EqualTo(RigidbodyConstraints.FreezeAll));
        Assert.That(InvokeBool(hostState, "SetTargetConnected", reference, true), Is.True);
        Assert.That(InvokeBool(hostState, "SetTargetConnected", replacementChildReference, false), Is.True);
        yield return Until(() => !clientPhysicalRoot.GetComponent<Rigidbody>().isKinematic
            && !latePhysicalRoot.GetComponent<Rigidbody>().isKinematic, "connected child unlock restores captured body flags");
        Assert.That(clientPhysicalRoot.GetComponent<Rigidbody>().useGravity, Is.True);
        Assert.That(latePhysicalRoot.GetComponent<Rigidbody>().useGravity, Is.True);
        Assert.That(clientPhysicalRoot.GetComponent<Rigidbody>().constraints, Is.EqualTo(RigidbodyConstraints.FreezeAll));
        Assert.That(latePhysicalRoot.GetComponent<Rigidbody>().constraints, Is.EqualTo(RigidbodyConstraints.FreezeAll));
        Assert.That(InvokeBool(hostState, "SetTargetConnected", replacementChildReference, true), Is.True);
        yield return Until(() => clientPhysicalRoot.GetComponent<Rigidbody>().isKinematic,
            "network child re-lock before non-destroying despawn");
        replacementTarget.Despawn(false);
        yield return Until(() => !InvokeBool(hostState, "IsTargetLocked", replacementChildReference),
            "Despawn(false) clears transient NetworkObject locks without persisting a scene tombstone");
        Assert.That(replacementTarget != null && !replacementTarget.IsSpawned, Is.True,
            "The server-side NetworkObject survives NGO despawn(false), so its authoritative body remains available.");
        Assert.That(replacementPhysicalRoot.GetComponent<Rigidbody>().isKinematic, Is.True,
            "Server-owned body settings are unchanged by the replica lock record.");
        ulong previousTargetSpawnId = replacementTarget.NetworkObjectId;
        replacementTarget.Spawn();
        yield return Until(() => replacementTarget.IsSpawned
            && client.SpawnManager.SpawnedObjects.ContainsKey(replacementTarget.NetworkObjectId)
            && lateJoin.SpawnManager.SpawnedObjects.ContainsKey(replacementTarget.NetworkObjectId),
            "target NetworkObject respawns with a new spawn identity");
        Assert.That(replacementTarget.NetworkObjectId, Is.Not.EqualTo(previousTargetSpawnId));
        object respawnedFirstChildReference = CreateReference(hostState, replacementPhysicalRoot.gameObject);
        object respawnedSecondChildReference = CreateReference(hostState, replacementTarget.transform.GetChild(1).gameObject);
        Assert.That(ReferenceId(respawnedFirstChildReference), Is.EqualTo(replacementTarget.NetworkObjectId));
        Assert.That(ReferenceId(respawnedSecondChildReference), Is.EqualTo(replacementTarget.NetworkObjectId));
        Assert.That(ReferencePath(respawnedFirstChildReference), Is.EqualTo("0"));
        Assert.That(ReferencePath(respawnedSecondChildReference), Is.EqualTo("1"));
        Assert.That(ResolveReference(clientState, respawnedFirstChildReference),
            Is.EqualTo(client.SpawnManager.SpawnedObjects[replacementTarget.NetworkObjectId].transform.GetChild(0)));
        Assert.That(ResolveReference(clientState, respawnedSecondChildReference),
            Is.EqualTo(client.SpawnManager.SpawnedObjects[replacementTarget.NetworkObjectId].transform.GetChild(1)));

        Assert.That(InvokeBool(hostState, "SetTargetConnected", reference, true), Is.True);
        yield return Until(() => InvokeBool(clientState, "IsTargetLocked", reference), "pre-respawn service lock");
        stateNetworkObject.Despawn(false);
        yield return Until(() => !stateNetworkObject.IsSpawned, "scene state service despawns without destroying its object");
        stateNetworkObject.Spawn();
        yield return Until(() => stateNetworkObject.IsSpawned
            && client.SpawnManager.SpawnedObjects.ContainsKey(stateNetworkObject.NetworkObjectId)
            && lateJoin.SpawnManager.SpawnedObjects.ContainsKey(stateNetworkObject.NetworkObjectId),
            "respawned service replicas arrive with a fresh NetworkList");
        clientState = client.SpawnManager.SpawnedObjects[stateNetworkObject.NetworkObjectId].GetComponent(stateType);
        lateState = lateJoin.SpawnManager.SpawnedObjects[stateNetworkObject.NetworkObjectId].GetComponent(stateType);
        Assert.That(Register(hostState, hostTarget), Is.True);
        Assert.That(Register(clientState, clientTarget), Is.True);
        Assert.That(Register(lateState, lateTarget), Is.True);
        yield return Until(() => !InvokeBool(hostState, "IsTargetLocked", reference)
            && !InvokeBool(clientState, "IsTargetLocked", reference)
            && !InvokeBool(lateState, "IsTargetLocked", reference)
            && !clientTarget.GetComponent<Rigidbody>().isKinematic,
            "service respawn clears live locks and restores the exact scene body flags");
        Assert.That(clientTarget.GetComponent<Rigidbody>().useGravity, Is.True);
        Assert.That(clientTarget.GetComponent<Rigidbody>().constraints, Is.EqualTo(RigidbodyConstraints.FreezeAll));

        UnityEngine.Object.Destroy(hostTarget);
        yield return Until(() => hostTarget == null, "server scene target destroyed");
        yield return Until(() => hostTarget == null && !clientTarget.activeSelf && !lateTarget.activeSelf,
            "all peers apply authoritative tombstone");
        tombstoneJoin = CreateManager("Mounting Nail Stage 3 tombstone join", port);
        Assert.That(tombstoneJoin.StartClient(), Is.True);
        yield return Until(() => tombstoneJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 4, "post-tombstone late join peer");
        yield return Until(() => tombstoneJoin.SpawnManager.SpawnedObjects.ContainsKey(stateNetworkObject.NetworkObjectId), "tombstone snapshot");
        tombstoneState = tombstoneJoin.SpawnManager.SpawnedObjects[stateNetworkObject.NetworkObjectId].GetComponent(stateType);
        Assert.That(Register(tombstoneState, tombstoneTarget), Is.True);
        Assert.That(tombstoneTarget.activeSelf, Is.False, "Tombstone remains in the scene service snapshot for future joiners.");
#endif
    }

    [UnityTest]
    public IEnumerator PairAttachmentProgressAndLocksReplicateToLateJoinWithoutClientJoint()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses authored Unity networking and nail prefab assets.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        previousSingleton = NetworkManager.Singleton;
        capturedSingleton = true;
        BuildFixtures();
        nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null);
        ushort port = ReservePort();
        host = CreateManager("Stage 3 paired nail host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "paired nail host start");
        client = CreateManager("Stage 3 paired nail client", port);
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "paired nail client");

        stateNetworkObject = UnityEngine.Object.Instantiate(statePrefab).GetComponent<NetworkObject>();
        roots.Add(stateNetworkObject.gameObject);
        stateNetworkObject.SetSceneObjectStatus(false);
        stateNetworkObject.Spawn();
        hostState = stateNetworkObject.GetComponent(stateType);
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(stateNetworkObject.NetworkObjectId),
            "paired nail scene state mirror");
        clientState = client.SpawnManager.SpawnedObjects[stateNetworkObject.NetworkObjectId].GetComponent(stateType);

        GameObject hostFirst = CreatePairTarget("stage3-first", new Vector3(200f, 35f, 200f), true, false);
        GameObject hostSecond = CreatePairTarget("stage3-second", new Vector3(200f, 35f, 200.2f), true, false);
        GameObject clientFirst = CreatePairTarget("stage3-first", new Vector3(200f, 35f, 200f), false, false);
        GameObject clientSecond = CreatePairTarget("stage3-second", new Vector3(200f, 35f, 200.2f), false, false);
        Assert.That(Register(hostState, hostFirst), Is.True);
        Assert.That(Register(hostState, hostSecond), Is.True);
        Assert.That(Register(clientState, clientFirst), Is.True);
        Assert.That(Register(clientState, clientSecond), Is.True);
        object firstReference = CreateReference(hostState, hostFirst);
        object secondReference = CreateReference(hostState, hostSecond);

        Type nailType = Type.GetType("MountingNail, Assembly-CSharp");
        GameObject nailInstance = UnityEngine.Object.Instantiate(nailPrefab);
        roots.Add(nailInstance);
        Component serverNail = nailInstance.GetComponent(nailType);
        ScriptableObject pairProfile = UnityEngine.Object.Instantiate(
            (ScriptableObject)nailType.GetProperty("Profile", PublicInstance).GetValue(serverNail));
        clonedProfiles.Add(pairProfile);
        pairProfile.GetType().GetField("requiredHitsForTwoObjects", PrivateInstance).SetValue(pairProfile, 3);
        nailType.GetField("profile", PrivateInstance).SetValue(serverNail, pairProfile);
        nailType.GetMethod("ApplyProfileConfiguration", PrivateInstance).Invoke(serverNail, null);
        Collider firstCollider = hostFirst.GetComponent<Collider>();
        float length = (float)pairProfile.GetType().GetProperty("LengthMeters", PublicInstance).GetValue(pairProfile);
        Bounds bounds = firstCollider.bounds;
        nailInstance.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.center.y,
            bounds.min.z - length * 0.5f - 0.005f), Quaternion.identity);
        NetworkObject nailNetworkObject = nailInstance.GetComponent<NetworkObject>();
        nailNetworkObject.SetSceneObjectStatus(false);
        nailNetworkObject.Spawn();
        Physics.SyncTransforms();
        nailType.GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(serverNail, new object[] { null });
        Assert.That(State(serverNail), Is.EqualTo("Prepared"));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(nailNetworkObject.NetworkObjectId),
            "paired nail mirror");
        NetworkObject clientNailObject = client.SpawnManager.SpawnedObjects[nailNetworkObject.NetworkObjectId];
        foreach (Collider collider in clientNailObject.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

        Physics.SyncTransforms();
        Assert.That(InvokeBool(serverNail, "TryApplyHammerHit", Enum.Parse(Type.GetType("EquippableItemType, Assembly-CSharp"),
            "IndustrialHammer")), Is.True);
        Assert.That(State(serverNail), Is.EqualTo("Partial"));
        Assert.That(NailInt(serverNail, "HitCount"), Is.EqualTo(1));
        yield return Until(() => State(clientNailObject.GetComponent(nailType)) == "Partial"
            && NailInt(clientNailObject.GetComponent(nailType), "HitCount") == 1
            && TransformField(clientNailObject.GetComponent(nailType), "embeddedTarget") == clientFirst.transform
            && TransformField(clientNailObject.GetComponent(nailType), "embeddedSecondTarget") == clientSecond.transform,
            "replicated pair partial state and client-local target resolution");
        Assert.That(TransformField(clientNailObject.GetComponent(nailType), "embeddedTarget"), Is.SameAs(clientFirst.transform),
            "The client resolves the selected stable scene identity inside its own manager scope.");
        Assert.That(TransformField(clientNailObject.GetComponent(nailType), "embeddedSecondTarget"), Is.SameAs(clientSecond.transform));
        hostFirst.transform.position += Vector3.up * 0.05f;
        Physics.SyncTransforms();
        yield return Until(() => Vector3.Distance(clientFirst.transform.position, hostFirst.transform.position) < 0.02f
            && Vector3.Distance(clientNailObject.transform.position, serverNail.transform.position) < 0.04f,
            "a partial nail tracks its moving unlocked scene target on the existing client");
        Assert.That(InvokeBool(clientState, "IsTargetLocked", firstReference), Is.False,
            "Partial target pose replication does not acquire the completed-pair pickup lock.");

        // A second independently embedded nail follows this same no-NetworkObject root.
        // Its reverse axis enters the first target from the opposite face, so the nearby
        // second pair target is behind the entry and cannot become a pair member.
        GameObject secondNailInstance = UnityEngine.Object.Instantiate(nailPrefab);
        roots.Add(secondNailInstance);
        Component secondServerNail = secondNailInstance.GetComponent(nailType);
        ScriptableObject secondProfile = UnityEngine.Object.Instantiate(
            (ScriptableObject)nailType.GetProperty("Profile", PublicInstance).GetValue(secondServerNail));
        clonedProfiles.Add(secondProfile);
        secondProfile.GetType().GetField("requiredHitsForSingleObject", PrivateInstance).SetValue(secondProfile, 3);
        secondProfile.GetType().GetField("requiredHitsForTwoObjects", PrivateInstance).SetValue(secondProfile, 3);
        nailType.GetField("profile", PrivateInstance).SetValue(secondServerNail, secondProfile);
        nailType.GetMethod("ApplyProfileConfiguration", PrivateInstance).Invoke(secondServerNail, null);
        secondNailInstance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Transform secondTip = (Transform)nailType.GetProperty("Tip", PublicInstance).GetValue(secondServerNail);
        Bounds firstBounds = firstCollider.bounds;
        Vector3 reverseEntry = new(firstBounds.center.x, firstBounds.center.y, firstBounds.max.z + 0.005f);
        secondNailInstance.transform.position = reverseEntry - (secondTip.position - secondNailInstance.transform.position);
        NetworkObject secondNailObject = secondNailInstance.GetComponent<NetworkObject>();
        secondNailObject.SetSceneObjectStatus(false);
        secondNailObject.Spawn();
        nailType.GetMethod("OnSingleCarryPlacementCommitted", PublicInstance)
            .Invoke(secondServerNail, new object[] { null });
        Assert.That(State(secondServerNail), Is.EqualTo("Prepared"));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(secondNailObject.NetworkObjectId),
            "second partial follower nail mirror");
        NetworkObject secondClientNailObject = client.SpawnManager.SpawnedObjects[secondNailObject.NetworkObjectId];
        foreach (Collider collider in secondClientNailObject.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        Assert.That(InvokeBool(secondServerNail, "TryApplyHammerHit",
            Enum.Parse(Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer")), Is.True);
        Assert.That(State(secondServerNail), Is.EqualTo("Partial"));
        Assert.That(TransformField(secondServerNail, "embeddedTarget"), Is.SameAs(hostFirst.transform));
        Assert.That(TransformField(secondServerNail, "embeddedSecondTarget"), Is.Null);
        yield return Until(() => State(secondClientNailObject.GetComponent(nailType)) == "Partial"
            && GetFollowCount(clientState, firstReference) == 2,
            "two independent nails register follow references to one scene target");

        lateJoin = CreateManager("Stage 3 partial nail late join", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3, "partial attachment late join");
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(stateNetworkObject.NetworkObjectId)
            && lateJoin.SpawnManager.SpawnedObjects.ContainsKey(nailNetworkObject.NetworkObjectId), "partial attachment snapshot");
        lateState = lateJoin.SpawnManager.SpawnedObjects[stateNetworkObject.NetworkObjectId].GetComponent(stateType);
        Component lateNail = lateJoin.SpawnManager.SpawnedObjects[nailNetworkObject.NetworkObjectId].GetComponent(nailType);
        GameObject lateFirst = CreatePairTarget("stage3-first", new Vector3(-20f, -20f, -20f), false, false);
        GameObject lateSecond = CreatePairTarget("stage3-second", new Vector3(-20f, -20f, -20f), false, false);
        Assert.That(Register(lateState, lateFirst), Is.True);
        Assert.That(Register(lateState, lateSecond), Is.True);
        foreach (Collider collider in lateJoin.SpawnManager.SpawnedObjects[nailNetworkObject.NetworkObjectId]
                     .GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        yield return Until(() => State(lateNail) == "Partial" && NailInt(lateNail, "HitCount") == 1
            && TransformField(lateNail, "embeddedTarget") == lateFirst.transform
            && Vector3.Distance(lateFirst.transform.position, hostFirst.transform.position) < 0.02f,
            "late join receives the moved partial target pose and original attachment");
        lateNail.GetType().GetMethod("FixedUpdate", PrivateInstance).Invoke(lateNail, null);
        Assert.That(Vector3.Distance(lateNail.transform.position, serverNail.transform.position), Is.LessThan(0.04f),
            "Late-joining partial nails follow their server-authored no-NetworkObject target pose.");

        Assert.That(InvokeBool(serverNail, "TryApplyHammerHit", Enum.Parse(Type.GetType("EquippableItemType, Assembly-CSharp"),
            "IndustrialHammer")), Is.True);
        Assert.That(InvokeBool(serverNail, "TryApplyHammerHit", Enum.Parse(Type.GetType("EquippableItemType, Assembly-CSharp"),
            "IndustrialHammer")), Is.True);
        Assert.That(State(serverNail), Is.EqualTo("EmbeddedPair"));
        Assert.That(hostFirst.GetComponents<FixedJoint>().Length + hostSecond.GetComponents<FixedJoint>().Length, Is.EqualTo(1));
        yield return Until(() => InvokeBool(clientState, "IsTargetLocked", firstReference)
            && InvokeBool(clientState, "IsTargetLocked", secondReference)
            && State(clientNailObject.GetComponent(nailType)) == "EmbeddedPair"
            && NailInt(clientNailObject.GetComponent(nailType), "HitCount") == 3, "replicated completed pair and both locks");
        Assert.That(clientFirst.GetComponents<FixedJoint>().Length + clientSecond.GetComponents<FixedJoint>().Length, Is.Zero);

        NetworkObject childTarget = UnityEngine.Object.Instantiate(networkTargetPrefab).GetComponent<NetworkObject>();
        roots.Add(childTarget.gameObject);
        childTarget.SetSceneObjectStatus(false);
        childTarget.Spawn();
        childTarget.ChangeOwnership(client.LocalClientId);
        yield return Until(() => childTarget.OwnerClientId == client.LocalClientId, "child target client ownership");
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(childTarget.NetworkObjectId), "child target client mirror");
        GameObject childA = childTarget.transform.GetChild(0).gameObject;
        GameObject childB = childTarget.transform.GetChild(1).gameObject;
        Vector3 childLocalPositionBeforeTakeover = childA.transform.localPosition;
        Quaternion childLocalRotationBeforeTakeover = childA.transform.localRotation;
        Transform clientChildA = client.SpawnManager.SpawnedObjects[childTarget.NetworkObjectId].transform.GetChild(0);
        yield return Until(() => Vector3.Distance(clientChildA.localPosition, childLocalPositionBeforeTakeover) < 0.005f
            && Quaternion.Angle(clientChildA.localRotation, childLocalRotationBeforeTakeover) < 0.1f,
            "parented local-space NetworkTransform child synchronizes before takeover");
        GameObject otherA = CreatePairTarget("ownership-other-a", new Vector3(220f, 40f, 220f), false, true);
        GameObject otherB = CreatePairTarget("ownership-other-b", new Vector3(230f, 40f, 230f), false, true);
        Assert.That(Register(hostState, otherA), Is.True);
        Assert.That(Register(hostState, otherB), Is.True);
        object childAReference = CreateReference(hostState, childA);
        object childBReference = CreateReference(hostState, childB);
        object otherAReference = CreateReference(hostState, otherA);
        object otherBReference = CreateReference(hostState, otherB);
        object registry = stateType.GetProperty("Connections", PublicInstance | PrivateInstance).GetValue(hostState);
        Component edgeNailA = UnityEngine.Object.Instantiate(nailPrefab).GetComponent(nailType);
        Component edgeNailB = UnityEngine.Object.Instantiate(nailPrefab).GetComponent(nailType);
        roots.Add(edgeNailA.gameObject);
        roots.Add(edgeNailB.gameObject);
        InvokeRegistryAdd(registry, edgeNailA, childA.transform, otherA.transform, childAReference, otherAReference);
        Assert.That(Vector3.Distance(childA.transform.localPosition, childLocalPositionBeforeTakeover), Is.LessThan(0.0001f),
            "Server ownership takeover preserves a parented child NetworkTransform local position.");
        Assert.That(Quaternion.Angle(childA.transform.localRotation, childLocalRotationBeforeTakeover), Is.LessThan(0.01f),
            "Server ownership takeover flushes the child's current local rotation before ownership changes.");
        yield return Until(() => childTarget.OwnerClientId == NetworkManager.ServerClientId
            && Vector3.Distance(clientChildA.localPosition, childLocalPositionBeforeTakeover) < 0.005f
            && Quaternion.Angle(clientChildA.localRotation, childLocalRotationBeforeTakeover) < 0.1f,
            "ownership transfer flushes the saved local pose to the former owner without a snap");
        InvokeRegistryAdd(registry, edgeNailB, childB.transform, otherB.transform, childBReference, otherBReference);
        Assert.That(childTarget.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
            "Joining either physical Rigidbody child transfers the NetworkObject authority to the server.");
        registry.GetType().GetMethod("Remove", PrivateInstance).Invoke(registry, new object[] { edgeNailA });
        Assert.That(childTarget.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
            "Unlocking one child does not restore ownership while a sibling child remains connected.");
        registry.GetType().GetMethod("Remove", PrivateInstance).Invoke(registry, new object[] { edgeNailB });
        Assert.That(childTarget.OwnerClientId, Is.EqualTo(client.LocalClientId),
            "Ownership returns to its original holder only after every connected child is unlocked.");
        Component survivingSiblingNail = UnityEngine.Object.Instantiate(nailPrefab).GetComponent(nailType);
        roots.Add(survivingSiblingNail.gameObject);
        InvokeRegistryAdd(registry, survivingSiblingNail, childB.transform, otherB.transform, childBReference, otherBReference);
        Component deletedChildEdgeNail = UnityEngine.Object.Instantiate(nailPrefab).GetComponent(nailType);
        roots.Add(deletedChildEdgeNail.gameObject);
        InvokeRegistryAdd(registry, deletedChildEdgeNail, childA.transform, otherA.transform, childAReference, otherAReference);
        Assert.That(InvokeBool(hostState, "IsTargetLocked", childBReference), Is.True);
        UnityEngine.Object.Destroy(childA);
        NetworkObject clientChildTarget = client.SpawnManager.SpawnedObjects[childTarget.NetworkObjectId];
        yield return Until(() => !InvokeBool(hostState, "IsTargetLocked", childAReference)
            && InvokeBool(hostState, "IsTargetLocked", childBReference)
            && !clientChildTarget.transform.GetChild(0).gameObject.activeSelf,
            "destroying one physical child does not rebind its locked path to the sibling Rigidbody",
            () => "A=" + InvokeBool(hostState, "IsTargetLocked", childAReference)
                + ", B=" + InvokeBool(hostState, "IsTargetLocked", childBReference)
                + ", clientChild0Active=" + clientChildTarget.transform.GetChild(0).gameObject.activeSelf
                + ", otherA=" + InvokeBool(hostState, "IsTargetLocked", otherAReference)
                + ", otherB=" + InvokeBool(hostState, "IsTargetLocked", otherBReference)
                + ", pinA=" + State(deletedChildEdgeNail) + ", pinB=" + State(edgeNailB)
                + ", pinSibling=" + State(survivingSiblingNail));
        Assert.That(InvokeBool(hostState, "IsTargetLocked", otherAReference), Is.False,
            "The dead first endpoint releases its counterpart lock and removes its nail edge.");
        Assert.That(InvokeBool(hostState, "IsTargetLocked", otherBReference), Is.True,
            "The sibling's separate connection remains locked after its indexed sibling is removed.");
        Assert.That(childB.GetComponents<FixedJoint>().Length, Is.EqualTo(1),
            "The surviving sibling retains its own physical joint.");
        object survivingReferenceAfterSiblingDeletion = CreateReference(hostState, childB);
        Assert.That(survivingReferenceAfterSiblingDeletion, Is.EqualTo(childBReference),
            "The first reference requested after deleting child zero retains child one's original cached path.");
        registry.GetType().GetMethod("Remove", PrivateInstance).Invoke(registry, new object[] { survivingSiblingNail });
        yield return Until(() => childTarget.OwnerClientId == client.LocalClientId,
            "destroying one connected child and unlocking the last sibling restores the live NI parent's original owner");

        NetworkObject lateChildTarget = lateJoin.SpawnManager.SpawnedObjects[childTarget.NetworkObjectId];
        Assert.That(lateChildTarget.transform.GetChild(0).gameObject.activeSelf, Is.False,
            "A late join receives the live-parent child tombstone and does not retain a ghost physical target.");
        Assert.That(lateChildTarget.transform.GetChild(1).gameObject.activeSelf, Is.True,
            "The sibling with its own live edge remains present for a late join.");
        object[] lateChildResolveArgs = { childBReference, null };
        bool lateChildResolved = (bool)stateType.GetMethod("TryResolve", PublicInstance)
            .Invoke(lateState, lateChildResolveArgs);
        Assert.That(lateChildResolved, Is.True);
        Assert.That(lateChildResolveArgs[1], Is.EqualTo(lateChildTarget.transform.GetChild(1)),
            "A late join resolves the first post-deletion child reference to original child one's prefab path.");
        foreach (Collider collider in lateJoin.SpawnManager.SpawnedObjects[nailNetworkObject.NetworkObjectId]
                     .GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        yield return Until(() => InvokeBool(lateState, "IsTargetLocked", firstReference)
            && InvokeBool(lateState, "IsTargetLocked", secondReference)
            && State(lateNail) == "EmbeddedPair" && NailInt(lateNail, "HitCount") == 3
            && TransformField(lateNail, "embeddedTarget") == lateFirst.transform
            && TransformField(lateNail, "embeddedSecondTarget") == lateSecond.transform,
            "the late-joining partial nail receives the completed pair and both locks");
        yield return Until(() => Vector3.Distance(lateFirst.transform.position, hostFirst.transform.position) < 0.02f,
            "the late first target has received its authoritative snapshot pose");
        Assert.That(lateFirst.GetComponents<FixedJoint>().Length + lateSecond.GetComponents<FixedJoint>().Length, Is.Zero,
            "Late joiners do not construct physics joints.");
        yield return null;
        Assert.That(Vector3.Distance(lateFirst.transform.position, hostFirst.transform.position), Is.LessThan(0.02f),
            DescribeLatePose(lateState, firstReference, lateFirst.transform, hostFirst.transform));
        Assert.That(Vector3.Distance(lateSecond.transform.position, hostSecond.transform.position), Is.LessThan(0.02f),
            DescribeLatePose(lateState, secondReference, lateSecond.transform, hostSecond.transform));
        nailNetworkObject.Despawn(true);
        yield return Until(() => !InvokeBool(hostState, "IsTargetLocked", firstReference)
            && !InvokeBool(hostState, "IsTargetLocked", secondReference)
            && GetFollowCount(clientState, firstReference) == 1
            && clientFirst.GetComponent<Rigidbody>().isKinematic,
            "removing one of two target followers preserves the remaining client's authoritative pose mode");
        hostFirst.transform.position += Vector3.up * 0.04f;
        Physics.SyncTransforms();
        yield return Until(() => Vector3.Distance(clientFirst.transform.position, hostFirst.transform.position) < 0.02f
            && Vector3.Distance(secondClientNailObject.transform.position, secondNailInstance.transform.position) < 0.04f,
            "the surviving partial nail continues to synchronize the shared target and its own attachment");
        secondNailObject.Despawn(true);
        yield return Until(() => GetFollowCount(clientState, firstReference) == 0
            && !clientFirst.GetComponent<Rigidbody>().isKinematic
            && !lateFirst.GetComponent<Rigidbody>().isKinematic,
            "removing the final follower restores the original replica body mode");
        Vector3 clientPositionAfterLastFollow = clientFirst.transform.position;
        hostFirst.transform.position += Vector3.up * 0.05f;
        Physics.SyncTransforms();
        yield return null;
        Assert.That(Vector3.Distance(clientFirst.transform.position, clientPositionAfterLastFollow), Is.LessThan(0.001f),
            "A noNI scene target stops receiving pose writes after its last embedded nail is removed.");
        Assert.That(!clientSecond.GetComponent<Rigidbody>().isKinematic
            && !lateSecond.GetComponent<Rigidbody>().isKinematic, Is.True,
            "The second endpoint restores its original body mode when the pair graph reaches degree zero.");
        Assert.That(clientFirst.GetComponent<Rigidbody>().constraints, Is.EqualTo(RigidbodyConstraints.FreezeAll));
        Assert.That(clientSecond.GetComponent<Rigidbody>().constraints, Is.EqualTo(RigidbodyConstraints.FreezeAll));
        Assert.That(lateFirst.GetComponent<Rigidbody>().constraints, Is.EqualTo(RigidbodyConstraints.FreezeAll));
        Assert.That(lateSecond.GetComponent<Rigidbody>().constraints, Is.EqualTo(RigidbodyConstraints.FreezeAll));

        childTarget.ChangeOwnership(client.LocalClientId);
        yield return Until(() => childTarget.OwnerClientId == client.LocalClientId, "child target ownership for deletion cleanup");
        Component destroyedOnlyChildNail = UnityEngine.Object.Instantiate(nailPrefab).GetComponent(nailType);
        roots.Add(destroyedOnlyChildNail.gameObject);
        InvokeRegistryAdd(registry, destroyedOnlyChildNail, childB.transform, otherB.transform, childBReference, otherBReference);
        Component reversedEndpointNail = UnityEngine.Object.Instantiate(nailPrefab).GetComponent(nailType);
        roots.Add(reversedEndpointNail.gameObject);
        InvokeRegistryAdd(registry, reversedEndpointNail, otherB.transform, childB.transform, otherBReference, childBReference);
        Assert.That(childTarget.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        UnityEngine.Object.Destroy(childB);
        yield return Until(() => childTarget.OwnerClientId == client.LocalClientId
            && !clientChildTarget.transform.GetChild(1).gameObject.activeSelf
            && !lateChildTarget.transform.GetChild(1).gameObject.activeSelf
            && State(reversedEndpointNail) == "EmbeddedSingle",
            "destroying the sole connected child restores its live parent owner and tombstones the child for current and late peers");
        Assert.That(otherB.activeSelf, Is.True,
            "A reversed pin whose first endpoint survives does not tombstone or deactivate that live endpoint.");
        Assert.That(InvokeBool(hostState, "IsTargetLocked", childBReference), Is.False);
#endif
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        bool mapsStopped = true;
        try
        {
            foreach (NetworkManager manager in managers)
                if (manager != null && manager.IsListening) manager.Shutdown(true);
            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline
                && managers.Exists(manager => manager != null && (manager.IsListening || manager.ShutdownInProgress))) yield return null;
            mapsStopped = !managers.Exists(manager => manager != null && (manager.IsListening || manager.ShutdownInProgress));
            foreach (GameObject root in roots) if (root != null) UnityEngine.Object.Destroy(root);
            foreach (ScriptableObject profile in clonedProfiles) if (profile != null) UnityEngine.Object.Destroy(profile);
            yield return null;
            noRoots = roots.TrueForAll(root => root == null);
        }
        finally
        {
            foreach (NetworkManager manager in managers)
                if (manager != null && manager.IsListening) manager.Shutdown(true);
            foreach (GameObject root in roots) if (root != null) UnityEngine.Object.Destroy(root);
            foreach (ScriptableObject profile in clonedProfiles) if (profile != null) UnityEngine.Object.Destroy(profile);
            if (capturedSingleton)
            {
                if (previousSingleton != null) previousSingleton.SetSingleton();
                else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            }
            managers.Clear(); roots.Clear(); clonedProfiles.Clear();
        }
        Assert.That(mapsStopped, Is.True);
        Assert.That(noRoots, Is.True);
    }

#if UNITY_EDITOR
    private void BuildFixtures()
    {
        stateType = Type.GetType("MountingNailSceneState, Assembly-CSharp");
        identityType = Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
        Assert.That(stateType, Is.Not.Null);
        Assert.That(identityType, Is.Not.Null);
        statePrefab = new GameObject("Stage 3 scene state network prefab");
        roots.Add(statePrefab);
        NetworkObject networkObject = statePrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, StatePrefabHash);
        statePrefab.AddComponent(stateType);
        statePrefab.GetComponent<NetworkObject>().SetSceneObjectStatus(false);
        networkTargetPrefab = new GameObject("Stage 3 network target prefab");
        roots.Add(networkTargetPrefab);
        NetworkObject targetNetworkObject = networkTargetPrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(targetNetworkObject, TargetPrefabHash);
        GameObject physicalChild = new("Physical Rigidbody child");
        physicalChild.transform.SetParent(networkTargetPrefab.transform, false);
        physicalChild.transform.localPosition = new Vector3(0.31f, 0.14f, 0.07f);
        physicalChild.transform.localRotation = Quaternion.Euler(0f, 0f, 13f);
        Type clientTransformType = Type.GetType("ClientNetworkTransform, Assembly-CSharp");
        Assert.That(clientTransformType, Is.Not.Null);
        Component childNetworkTransform = physicalChild.AddComponent(clientTransformType);
        clientTransformType.GetField("InLocalSpace", PublicInstance).SetValue(childNetworkTransform, true);
        Rigidbody physicalBody = physicalChild.AddComponent<Rigidbody>();
        physicalBody.useGravity = true;
        physicalBody.constraints = RigidbodyConstraints.FreezeAll;
        BoxCollider targetCollider = physicalChild.AddComponent<BoxCollider>();
        targetCollider.enabled = false;
        GameObject secondPhysicalChild = new("Second physical Rigidbody child");
        secondPhysicalChild.transform.SetParent(networkTargetPrefab.transform, false);
        Rigidbody secondPhysicalBody = secondPhysicalChild.AddComponent<Rigidbody>();
        secondPhysicalBody.useGravity = true;
        secondPhysicalBody.constraints = RigidbodyConstraints.FreezeAll;
        BoxCollider secondTargetCollider = secondPhysicalChild.AddComponent<BoxCollider>();
        secondTargetCollider.enabled = false;
        targetNetworkObject.SetSceneObjectStatus(false);
        hostTarget = CreateTarget("stage3-shared-scene-target", new Vector3(100f, 30f, 100f));
        clientTarget = CreateTarget("stage3-shared-scene-target", new Vector3(100f, 30f, 100f));
        lateTarget = CreateTarget("stage3-shared-scene-target", new Vector3(100f, 30f, 100f));
        tombstoneTarget = CreateTarget("stage3-shared-scene-target", new Vector3(100f, 30f, 100f));
    }

    private GameObject CreateTarget(string id, Vector3 position)
    {
        GameObject target = new("Stage 3 peer-local target");
        roots.Add(target);
        target.transform.position = position;
        Rigidbody body = target.AddComponent<Rigidbody>();
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.FreezeAll;
        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.enabled = false;
        Component identity = target.AddComponent(identityType);
        identityType.GetField("stableTargetId", PrivateInstance).SetValue(identity, id);
        return target;
    }

    private NetworkManager CreateManager(string name, ushort port)
    {
        GameObject root = new(name);
        roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = statePrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = networkTargetPrefab });
        if (nailPrefab != null) manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = nailPrefab });
        managers.Add(manager);
        return manager;
    }

    private bool Register(Component state, GameObject target) => (bool)stateType.GetMethod("RegisterLocalTarget", PublicInstance)
        .Invoke(state, new[] { target.GetComponent(identityType) });

    private static int GetFollowCount(Component state, object reference)
    {
        object states = state.GetType().GetField("targetStates", PrivateInstance).GetValue(state);
        Type listType = states.GetType();
        int count = (int)listType.GetProperty("Count", PublicInstance).GetValue(states);
        PropertyInfo item = listType.GetProperty("Item", PublicInstance);
        for (int i = 0; i < count; i++)
        {
            object record = item.GetValue(states, new object[] { i });
            object target = record.GetType().GetField("Target").GetValue(record);
            if (target.Equals(reference)) return (int)record.GetType().GetField("FollowCount").GetValue(record);
        }
        return 0;
    }

    private GameObject CreatePairTarget(string stableId, Vector3 position, bool enableCollider, bool kinematic)
    {
        GameObject target = new("Stage 3 connected pair target");
        roots.Add(target);
        target.transform.position = position;
        Rigidbody body = target.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = kinematic;
        body.constraints = RigidbodyConstraints.FreezeAll;
        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.size = new Vector3(1f, 1f, 0.1f);
        collider.enabled = enableCollider;
        Component identity = target.AddComponent(identityType);
        identityType.GetField("stableTargetId", PrivateInstance).SetValue(identity, stableId);
        return target;
    }

    private object CreateReference(Component state, GameObject target)
    {
        object[] args = { target.transform, null };
        bool created = (bool)stateType.GetMethod("TryCreateReference", PublicInstance).Invoke(state, args);
        Assert.That(created, Is.True);
        return args[1];
    }

    private ulong ReferenceId(object reference) => (ulong)reference.GetType().GetField("NetworkObjectId").GetValue(reference);

    private string ReferencePath(object reference) => reference.GetType().GetField("PhysicalRootPath").GetValue(reference).ToString();

    private Transform ResolveReference(Component state, object reference)
    {
        object[] args = { reference, null };
        bool resolved = (bool)stateType.GetMethod("TryResolve", PublicInstance).Invoke(state, args);
        Assert.That(resolved, Is.True);
        return (Transform)args[1];
    }

    private static void InvokeRegistryAdd(object registry, Component nail, Transform first, Transform second,
        object firstReference, object secondReference)
    {
        Type nailType = nail.GetType();
        nailType.GetField("embeddedTarget", PrivateInstance).SetValue(nail, first);
        nailType.GetField("embeddedSecondTarget", PrivateInstance).SetValue(nail, second);
        nailType.GetField("embeddedTargetReference", PrivateInstance).SetValue(nail, firstReference);
        nailType.GetField("embeddedSecondTargetReference", PrivateInstance).SetValue(nail, secondReference);
        // These test nails exercise the registry directly and are deliberately not spawned NGO objects.
        nailType.GetField("embeddedTargetWasSpawnedNetworkObject", PrivateInstance).SetValue(nail, false);
        nailType.GetField("embeddedSecondTargetWasSpawnedNetworkObject", PrivateInstance).SetValue(nail, false);
        MethodInfo add = registry.GetType().GetMethod("Add", PrivateInstance);
        bool added = (bool)add.Invoke(registry, new[] { nail, first, second, firstReference, secondReference, first.position });
        Assert.That(added, Is.True);
    }

    private static Vector3 GetTargetStatePosition(Component state, object reference)
    {
        object list = state.GetType().GetField("targetStates", PrivateInstance).GetValue(state);
        PropertyInfo countProperty = list.GetType().GetProperty("Count");
        PropertyInfo itemProperty = list.GetType().GetProperty("Item");
        int count = (int)countProperty.GetValue(list);
        for (int i = 0; i < count; i++)
        {
            object entry = itemProperty.GetValue(list, new object[] { i });
            object target = entry.GetType().GetField("Target").GetValue(entry);
            if (target.Equals(reference)) return (Vector3)entry.GetType().GetField("Position").GetValue(entry);
        }
        return Vector3.negativeInfinity;
    }

    private static string DescribeLatePose(Component state, object reference, Transform peer, Transform server)
    {
        object[] resolveArgs = { reference, null };
        bool resolved = (bool)state.GetType().GetMethod("TryResolve", PublicInstance).Invoke(state, resolveArgs);
        Rigidbody body = peer.GetComponent<Rigidbody>();
        object flags = state.GetType().GetField("savedBodyFlags", PrivateInstance).GetValue(state);
        bool hasFlags = (bool)flags.GetType().GetMethod("ContainsKey").Invoke(flags, new[] { reference });
        object manager = state.GetType().GetProperty("ScopedNetworkManager", PublicInstance).GetValue(state);
        bool isServer = (bool)state.GetType().GetProperty("IsServer", PublicInstance).GetValue(state);
        return $"Late peer pos={peer.position}, Rigidbody.position={body.position}, kinematic={body.isKinematic}, gravity={body.useGravity}, constraints={body.constraints}, server={server.position}, snapshot={GetTargetStatePosition(state, reference)}, resolved={resolved}/{resolveArgs[1]}, savedFlags={hasFlags}, stateServer={isServer}, manager={manager}.";
    }

    private static bool InvokeBool(Component target, string method, params object[] args) =>
        (bool)target.GetType().GetMethod(method, PublicInstance).Invoke(target, args);

    private static string State(Component target) => target.GetType().GetProperty("State", PublicInstance).GetValue(target).ToString();
    private static int NailInt(Component target, string property) =>
        (int)target.GetType().GetProperty(property, PublicInstance).GetValue(target);
    private static Transform TransformField(Component target, string field) =>
        (Transform)target.GetType().GetField(field, PrivateInstance).GetValue(target);

    private static ushort ReservePort()
    {
        TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        ushort port = (ushort)((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static IEnumerator Until(Func<bool> predicate, string description, Func<string> failureDetails = null)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        bool succeeded = predicate();
        Assert.That(succeeded, Is.True, "Timed out waiting for " + description
            + (succeeded || failureDetails == null ? string.Empty : $" ({failureDetails()})"));
    }
#endif
}
