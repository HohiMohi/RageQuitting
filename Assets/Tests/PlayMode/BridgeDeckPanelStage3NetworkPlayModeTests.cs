using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using System;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class BridgeDeckPanelStage3NetworkPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const uint PlayerHash = 0x42503331u;
    private readonly List<NetworkManager> managers = new();
    private readonly List<GameObject> roots = new();
    private readonly List<GameObject> playerCopies = new();
    private NetworkManager priorSingleton;
    private Scene originalActiveScene;
    private Scene tutorialSmokeScene;
    private bool unloadTutorialSmokeScene;
    private SimulationMode priorPhysicsSimulationMode;
    private bool restorePhysicsSimulationMode;
    private NetworkManager host;
    private NetworkManager owner;
    private NetworkManager observer;
    private NetworkManager lateJoin;
    private GameObject playerPrefab;
    private GameObject panelPrefab;
    private GameObject sectionPrefab;
    private GameObject sectionStatePrefab;
    private GameObject nailPrefab;
    private ScriptableObject hammer;
    private ScriptableObject shovel;

    [UnityTest]
    public IEnumerator TutorialTerrainStage02InScenePanelUsesServerDroppedPhysicsAfterNetworkSpawn()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the authored tutorial scene available only in the Editor.");
        yield break;
#else
        const string tutorialScenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True,
            "The in-scene smoke requires no active NGO session.");
        priorSingleton = NetworkManager.Singleton;
        Assert.That(SceneUtility.GetBuildIndexByScenePath(tutorialScenePath), Is.GreaterThanOrEqualTo(0));
        originalActiveScene = SceneManager.GetActiveScene();
        Assert.That(originalActiveScene.IsValid() && originalActiveScene.isLoaded, Is.True,
            "The smoke preserves the active test-runner scene.");
        tutorialSmokeScene = SceneManager.GetSceneByPath(tutorialScenePath);
        if (!tutorialSmokeScene.IsValid() || !tutorialSmokeScene.isLoaded)
        {
            unloadTutorialSmokeScene = true;
            AsyncOperation load = SceneManager.LoadSceneAsync(tutorialScenePath, LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null, "The actual TutorialTerrainStage02 scene starts loading additively.");
            yield return Until(() => load.isDone, "actual TutorialTerrainStage02 additive scene load");
            tutorialSmokeScene = SceneManager.GetSceneByPath(tutorialScenePath);
        }
        Assert.That(tutorialSmokeScene.IsValid() && tutorialSmokeScene.isLoaded, Is.True,
            "The actual TutorialTerrainStage02 scene is loaded for the smoke.");
        Assert.That(SceneManager.SetActiveScene(tutorialSmokeScene), Is.True,
            "The authored scene is active while its in-scene NetworkObjects spawn.");
        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(tutorialScenePath));

        GameObject managerRoot = new("Stage 3 authored-scene gravity smoke host");
        roots.Add(managerRoot);
        UnityTransport transport = managerRoot.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", ReservePort(), "127.0.0.1");
        host = managerRoot.AddComponent<NetworkManager>();
        host.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = true
        };
        managers.Add(host);
        Assert.That(host.StartHost(), Is.True, "The isolated server starts after the authored scene is loaded.");
        yield return Until(() => host.IsListening, "authored-scene host startup");

        System.Type markerType = Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        Assert.That(markerType, Is.Not.Null);
        Component scenePanel = null;
        foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (behaviour != null && behaviour.gameObject.scene == tutorialSmokeScene
                && markerType.IsInstanceOfType(behaviour)
                && (bool)markerType.GetProperty("IsPhysicalPanel", PublicInstance).GetValue(behaviour))
            {
                scenePanel = behaviour;
                break;
            }
        }
        Assert.That(scenePanel, Is.Not.Null, "The real scene contains an active physical panel.");
        NetworkObject panelNetworkObject = scenePanel.GetComponent<NetworkObject>();
        Rigidbody sceneBody = scenePanel.GetComponent<Rigidbody>();
        Assert.That(panelNetworkObject, Is.Not.Null);
        Assert.That(panelNetworkObject.IsSpawned, Is.True,
            "The panel is spawned as an in-scene NetworkObject by NGO's scene load.");
        Assert.That(panelNetworkObject.IsSceneObject, Is.True,
            "This check covers an actual scene-authored object, not a runtime prefab instance.");
        Assert.That(panelNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(sceneBody, Is.Not.Null);
        Assert.That(sceneBody.useGravity, Is.True,
            "A never-picked-up in-scene panel remains gravity-enabled after NetworkTransform authority initializes.");
        Assert.That(sceneBody.isKinematic, Is.False,
            "The server owns dynamic simulation for the unheld scene panel.");

#endif
    }

    [UnityTest]
    public IEnumerator RemoteOwnerPanelHammerRpcClaimsServerMotionAndReplicatesToObserverAndLateJoin()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored panel and player prefabs available only in the Editor.");
        yield break;
#else
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The loopback fixture requires no pre-existing NGO session.");

        priorSingleton = NetworkManager.Singleton;
        hammer = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        shovel = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/Shovel.asset");
        panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
        sectionPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/BridgeDeckSection.prefab");
        nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(hammer, Is.Not.Null);
        Assert.That(panelPrefab, Is.Not.Null);
        Assert.That(sectionPrefab, Is.Not.Null);
        Assert.That(nailPrefab, Is.Not.Null);
        playerPrefab = CreatePlayerPrefab();
        sectionStatePrefab = CreateSectionStatePrefab();

        ushort port = ReservePort();
        host = CreateManager("Stage 3 panel reset host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        Assert.That(((NetworkObject)playerPrefab.GetComponent(typeof(NetworkObject))).IsSpawned, Is.False,
            "The registered runtime player source prefab remains unspawned.");
        yield return Until(() => host.LocalClient?.PlayerObject != null, "host player");

        owner = CreateManager("Stage 3 panel reset owner", port);
        Assert.That(owner.StartClient(), Is.True);
        owner.SetSingleton();
        yield return Until(() => owner.IsConnectedClient && owner.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 2, "remote owner player");
        CapturePlayerCopies();
        DisablePlayerColliders();

        observer = CreateManager("Stage 3 panel reset observer", port);
        Assert.That(observer.StartClient(), Is.True);
        observer.SetSingleton();
        yield return Until(() => observer.IsConnectedClient && observer.LocalClient?.PlayerObject != null
            && host.ConnectedClientsIds.Count == 3, "observer player");
        CapturePlayerCopies();
        DisablePlayerColliders();

        host.SetSingleton();
        GameObject sectionRoot = UnityEngine.Object.Instantiate(sectionPrefab,
            new Vector3(100f, 25f, 100f), Quaternion.identity);
        roots.Add(sectionRoot);
        // BridgeComponent child scripts subscribe to the scene GameplayManager. This focused
        // network fixture uses the real section/state and panel slot transforms without that
        // unrelated scene singleton, so disable only those presentation/workflow components.
        System.Type bridgeComponentType = System.Type.GetType("BridgeComponent, Assembly-CSharp");
        Assert.That(bridgeComponentType, Is.Not.Null, "BridgeComponent fixture type resolves from Assembly-CSharp.");
        foreach (Component component in sectionRoot.GetComponentsInChildren(bridgeComponentType, true))
            if (component.GetType() == bridgeComponentType) ((Behaviour)component).enabled = false;
        System.Type sectionStateType = System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        Component authoredChildState = sectionRoot.GetComponentInChildren(sectionStateType, true);
        Assert.That(authoredChildState, Is.Not.Null, "Authored section contains its child state that is replaced by the fixture state.");
        ((Behaviour)authoredChildState).enabled = false;
        NetworkObject authoredChildStateObject = authoredChildState.GetComponent<NetworkObject>();
        if (authoredChildStateObject != null) authoredChildStateObject.enabled = false;
        Component section = sectionRoot.GetComponent(System.Type.GetType("BridgeDeckSection, Assembly-CSharp"));
        Assert.That(section, Is.Not.Null, "Authored section prefab has its BridgeDeckSection controller.");
        System.Collections.IList slots = (System.Collections.IList)section.GetType()
            .GetProperty("PanelSlots", PublicInstance).GetValue(section);
        Assert.That(slots, Is.Not.Null.And.Not.Empty, "Authored section exposes its panel construction slots.");
        Component site = slots[0] as Component;
        Assert.That(site, Is.Not.Null);
        Vector3 slotPosition = site.transform.position;
        foreach (Collider slotCollider in site.GetComponentsInChildren<Collider>(true)) slotCollider.enabled = false;

        GameObject floor = new("Stage 3 panel reset landing floor");
        roots.Add(floor);
        floor.transform.position = slotPosition + Vector3.down * 2.5f;
        BoxCollider floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.size = new Vector3(20f, 0.2f, 20f);

        // The authored section geometry stays host-local. A small registered state prefab
        // binds its section controller to the real authored slot transforms on the host.
        GameObject stateRoot = UnityEngine.Object.Instantiate(sectionStatePrefab,
            sectionRoot.transform.position, sectionRoot.transform.rotation);
        roots.Add(stateRoot);
        Component networkSection = stateRoot.GetComponent(System.Type.GetType("BridgeDeckSection, Assembly-CSharp"));
        networkSection.GetType().GetField("panelSlots", PrivateInstance).SetValue(networkSection,
            section.GetType().GetField("panelSlots", PrivateInstance).GetValue(section));
        NetworkObject sectionStateObject = stateRoot.GetComponent<NetworkObject>();
        sectionStateObject.Spawn();
        Component serverSceneState = stateRoot.GetComponent(Type.GetType("MountingNailSceneState, Assembly-CSharp"));
        yield return Until(() => Find(owner, sectionStateObject.NetworkObjectId) != null
            && Find(observer, sectionStateObject.NetworkObjectId) != null, "nail scene-state mirrors");
        Component ownerSceneState = Find(owner, sectionStateObject.NetworkObjectId)
            .GetComponent(Type.GetType("MountingNailSceneState, Assembly-CSharp"));
        Component observerSceneState = Find(observer, sectionStateObject.NetworkObjectId)
            .GetComponent(Type.GetType("MountingNailSceneState, Assembly-CSharp"));

        // Keep the authored prefab's gravity/damping values intact, but hold physics until the
        // actual reset so setup latency cannot move the panel out of its section slot.
        priorPhysicsSimulationMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        restorePhysicsSimulationMode = true;

        GameObject panelRoot = UnityEngine.Object.Instantiate(panelPrefab, slotPosition, Quaternion.identity);
        roots.Add(panelRoot);
        foreach (Component component in panelRoot.GetComponentsInChildren(bridgeComponentType, true))
            if (component.GetType() == bridgeComponentType) ((Behaviour)component).enabled = false;
        NetworkObject panelNetworkObject = panelRoot.GetComponent<NetworkObject>();
        Assert.That(panelNetworkObject, Is.Not.Null);
        panelNetworkObject.SetSceneObjectStatus(false);
        Rigidbody serverBody = panelRoot.GetComponent<Rigidbody>();
        Assert.That(serverBody, Is.Not.Null);
        Assert.That(serverBody.useGravity, Is.True, "The authored panel prefab starts with gravity enabled.");
        float authoredMass = serverBody.mass;
        float authoredLinearDamping = serverBody.linearDamping;
        float authoredAngularDamping = serverBody.angularDamping;
        panelNetworkObject.Spawn();
        bool gravityImmediatelyAfterSpawn = serverBody.useGravity;
        bool kinematicImmediatelyAfterSpawn = serverBody.isKinematic;
        yield return Until(() => Find(owner, panelNetworkObject.NetworkObjectId) != null
            && Find(observer, panelNetworkObject.NetworkObjectId) != null, "panel mirrors");

        NetworkObject hostOwnerPlayer = host.ConnectedClients[owner.LocalClientId].PlayerObject;
        NetworkObject ownerPlayer = owner.LocalClient.PlayerObject;
        Component ownerInteraction = ownerPlayer.GetComponent(System.Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        Component ownerInventory = ownerPlayer.GetComponent(System.Type.GetType("PlayerInventory, Assembly-CSharp"));
        Component ownerAction = ownerPlayer.GetComponent(System.Type.GetType("PlayerActionController, Assembly-CSharp"));
        Component serverInteraction = hostOwnerPlayer.GetComponent(System.Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        Component serverInventory = hostOwnerPlayer.GetComponent(System.Type.GetType("PlayerInventory, Assembly-CSharp"));
        Component serverAction = hostOwnerPlayer.GetComponent(System.Type.GetType("PlayerActionController, Assembly-CSharp"));
        Assert.That(ownerAction, Is.Not.Null);
        ownerInventory.GetType().GetMethod("AddItem", PublicInstance).Invoke(ownerInventory, new object[] { hammer });
        serverInventory.GetType().GetMethod("SetNetworkSlotValues", PrivateInstance).Invoke(serverInventory,
            new object[] { (int)System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer"), -1 });
        yield return Until(() => SelectedServerItemType(serverInventory) == "IndustrialHammer",
            "server-selected hammer");

        Component marker = panelRoot.GetComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"));
        Assert.That(marker, Is.Not.Null);
        Component owningSectionState = (Component)sectionStateType
            .GetMethod("FindOwningSection", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { marker });
        Assert.That(owningSectionState, Is.SameAs(stateRoot.GetComponent(sectionStateType)),
            "The server's active owning-section registry selects the spawned runtime state bound to the authored slots.");
        NetworkObject ownerPanel = Find(owner, panelNetworkObject.NetworkObjectId);
        NetworkObject observerPanel = Find(observer, panelNetworkObject.NetworkObjectId);
        DisableClientPanelColliders(ownerPanel);
        DisableClientPanelColliders(observerPanel);
        // Keep the camera ray origin on the trusted placement aim anchor.
        Transform ownerAnchor = (Transform)ownerInteraction.GetType()
            .GetProperty("SingleCarryPlacementAimAnchor", PublicInstance).GetValue(ownerInteraction);
        FieldInfo hammerRangeField = hammer.GetType().GetField("actionRange", PrivateInstance | PublicInstance);
        Vector3 desiredAnchorPosition = slotPosition + Vector3.up * 0.5f + Vector3.back * 0.1f;
        Vector3 playerPosition = desiredAnchorPosition - ownerAnchor.localPosition;
        Quaternion playerRotation = Quaternion.identity;
        ownerPlayer.transform.SetPositionAndRotation(playerPosition, playerRotation);
        hostOwnerPlayer.transform.SetPositionAndRotation(playerPosition, playerRotation);
        Vector3 panelAimPoint = panelRoot.transform.TransformPoint(new Vector3(0.6f, 0.2f, 0.6f));
        ownerAnchor.rotation = Quaternion.LookRotation(panelAimPoint - ownerAnchor.position, Vector3.up);
        Camera ownerCamera = ownerAnchor.GetComponentInChildren<Camera>(true);
        Camera serverCamera = hostOwnerPlayer.GetComponentInChildren<Camera>(true);
        ownerInteraction.GetType().GetField("aimCamera", PrivateInstance).SetValue(ownerInteraction, ownerCamera);
        serverInteraction.GetType().GetField("aimCamera", PrivateInstance).SetValue(serverInteraction, serverCamera);
        bool IsCurrentAimTrustedByServer()
        {
            Ray currentRay = GetAimRay(ownerInteraction);
            return (bool)serverAction.GetType().GetMethod("IsTrustedMountingNailAimOrigin", PrivateInstance)
                .Invoke(serverAction, new object[] { currentRay.origin, currentRay.direction });
        }
        yield return Until(IsCurrentAimTrustedByServer, "authoritative owner transform and trusted aim convergence");

        Ray aimRay = GetAimRay(ownerInteraction);
        Assert.That(Vector3.Distance(aimRay.origin, ownerAnchor.position), Is.LessThan(0.02f),
            "The camera near-plane origin stays close to the trusted placement aim anchor.");
        Assert.That(IsCurrentAimTrustedByServer(), Is.True,
            "The server independently accepts only the converged trusted owner aim origin.");
        Type resolverType = Type.GetType("BridgeDeckPanelHammerAimResolver, Assembly-CSharp");
        object[] resolverArgs = { aimRay, (float)hammerRangeField.GetValue(hammer), hostOwnerPlayer.transform, null, null };
        Assert.That((bool)resolverType.GetMethod("TryFindVisiblePanel").Invoke(null, resolverArgs), Is.True,
            "The host independently sees the same physical panel hit as the remote owner.");
        Assert.That(resolverArgs[3], Is.SameAs(marker));

        ulong panelId = panelNetworkObject.NetworkObjectId;
        Assert.That(panelNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
            "The authored-prefab panel begins server-owned and has not been carried before its first hammer hit.");

        // Reproduce the initial scene-panel case first: the authored prefab is unheld, starts
        // server-owned, and has no nail or registry edges when the real remote hammer RPC resets it.
        Assert.That(SelectedServerItemType(serverInventory), Is.EqualTo("IndustrialHammer"));
        Assert.That((bool)Type.GetType("MountingNailAimResolver, Assembly-CSharp")
            .GetMethod("TryFindCandidate").Invoke(null, new object[]
            {
                aimRay, (float)hammerRangeField.GetValue(hammer), hostOwnerPlayer.transform, null, null, null
            }), Is.False, "The initial panel hit has no higher-priority nail target.");
        Vector3 initialVelocity = serverBody.linearVelocity;
        owner.SetSingleton();
        InvokePanelRpc(ownerAction, panelId, aimRay.origin, aimRay.direction, 1);
        host.SetSingleton();
        yield return Until(() => (uint)serverAction.GetType()
            .GetField("lastServerMountingNailImpactSequence", PrivateInstance).GetValue(serverAction) == 1u,
            "first zero-nail panel hammer RPC");
        yield return Until(() => !(bool)marker.GetType()
            .GetProperty("ResetImpulsePending", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(marker),
            "initial panel reset impulse");
        bool gravityAfterInitialReset = serverBody.useGravity;
        bool kinematicAfterInitialReset = serverBody.isKinematic;
        Assert.That(serverBody.useGravity, Is.True,
            $"The first reset of an unheld server-owned authored prefab keeps gravity enabled (spawnGravity={gravityImmediatelyAfterSpawn}, spawnKinematic={kinematicImmediatelyAfterSpawn}).");
        Assert.That(panelNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Physics.Simulate(Time.fixedDeltaTime);
        Assert.That(serverBody.linearVelocity.y - initialVelocity.y,
            Is.EqualTo(2f + Physics.gravity.y * Time.fixedDeltaTime).Within(0.1f),
            "The first zero-nail reset produces the expected upward impulse while authored gravity acts.");
        float initialUpwardVelocity = serverBody.linearVelocity.y;
        for (int step = 0; step < 30; step++) Physics.Simulate(Time.fixedDeltaTime);
        Assert.That(serverBody.linearVelocity.y, Is.LessThan(initialUpwardVelocity - 3f),
            "The initially spawned panel begins falling under its authored gravity after the reset.");
        serverBody.linearVelocity = Vector3.zero;
        serverBody.angularVelocity = Vector3.zero;
        panelRoot.transform.SetPositionAndRotation(slotPosition, Quaternion.identity);
        panelRoot.GetComponent<NetworkTransform>().Teleport(slotPosition, Quaternion.identity, panelRoot.transform.localScale);
        Physics.SyncTransforms();
        yield return Until(() => Vector3.Distance(Find(owner, panelId).transform.position, slotPosition) < 0.05f
            && Vector3.Distance(Find(observer, panelId).transform.position, slotPosition) < 0.05f,
            "panel replicas return to slot for the separate paired-edge reset case");
        panelNetworkObject.ChangeOwnership(owner.LocalClientId);
        yield return Until(() => panelNetworkObject.OwnerClientId == owner.LocalClientId,
            "panel ownership transfer for the existing paired-edge case");
        bool gravityAfterClientOwnership = serverBody.useGravity;
        bool kinematicAfterClientOwnership = serverBody.isKinematic;

        // Pair a spawned nail with the panel as its first target and a registered, static
        // scene beam as its second target before the remote impact. This makes the same live
        // hammer hit exercise panel-as-first-target cleanup while the original aim remains valid.
        GameObject beam = new("Stage 3 static beam target");
        roots.Add(beam);
        beam.transform.position = slotPosition + Vector3.right * 1.35f;
        Rigidbody beamBody = beam.AddComponent<Rigidbody>();
        beamBody.isKinematic = true;
        beamBody.useGravity = false;
        BoxCollider beamCollider = beam.AddComponent<BoxCollider>();
        Component beamIdentity = beam.AddComponent(Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp"));
        beamIdentity.GetType().GetMethod("SetBakedId", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(beamIdentity, new object[] { "stage3-panel-reset-static-beam" });
        MethodInfo registerTarget = serverSceneState.GetType().GetMethod("RegisterLocalTarget", PublicInstance);
        Assert.That((bool)registerTarget.Invoke(serverSceneState, new[] { beamIdentity }), Is.True);
        Assert.That((bool)registerTarget.Invoke(ownerSceneState, new[] { beamIdentity }), Is.True);
        Assert.That((bool)registerTarget.Invoke(observerSceneState, new[] { beamIdentity }), Is.True);

        GameObject nailRoot = UnityEngine.Object.Instantiate(nailPrefab, slotPosition + Vector3.right * 1.35f
            + Vector3.up * 0.2f, Quaternion.identity);
        roots.Add(nailRoot);
        NetworkObject nailNetworkObject = nailRoot.GetComponent<NetworkObject>();
        Assert.That(nailNetworkObject, Is.Not.Null);
        nailNetworkObject.SetSceneObjectStatus(false);
        nailNetworkObject.Spawn();
        ulong nailId = nailNetworkObject.NetworkObjectId;
        yield return Until(() => Find(owner, nailId) != null
            && Find(observer, nailId) != null, "paired nail mirrors");
        DisableClientPanelColliders(Find(owner, nailId));
        DisableClientPanelColliders(Find(observer, nailId));

        Component nail = nailRoot.GetComponent(Type.GetType("MountingNail, Assembly-CSharp"));
        Transform beamTransform = beam.transform;
        nail.GetType().GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(nail,
            new object[] { panelRoot.transform, beamTransform, slotPosition, Vector3.forward });
        nail.GetType().GetMethod("SetHitCounts", PrivateInstance).Invoke(nail,
            new object[] { 4, 4, 0.02f });
        nail.GetType().GetMethod("SetState", PrivateInstance).Invoke(nail,
            new object[] { Enum.Parse(Type.GetType("MountingNailState, Assembly-CSharp"), "EmbeddedPair") });
        Assert.That((bool)nail.GetType().GetMethod("TryRegisterPairConnection", PrivateInstance).Invoke(nail, null), Is.True,
            "The test creates a real production pair connection in the scoped server registry.");
        bool gravityAfterPairRegistration = serverBody.useGravity;
        bool kinematicAfterPairRegistration = serverBody.isKinematic;
        // The pair remains a real registry FixedJoint, but its controlled static endpoint
        // does not act as an accidental solid obstacle to the panel's server-driven motion.
        beamCollider.enabled = false;
        FieldInfo registryField = nail.GetType().GetField("connectionRegistry", PrivateInstance);
        object registry = registryField.GetValue(nail);
        Assert.That(registry, Is.Not.Null);
        MethodInfo pairJointCount = registry.GetType().GetMethod("PairJointCount", PrivateInstance);
        Assert.That((int)pairJointCount.Invoke(registry, new object[] { panelRoot.transform, beamTransform }), Is.EqualTo(1));
        Assert.That(panelNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
            "Pair registration transfers the client-owned panel to the server before reset.");
        object[] attachmentArgs = { null };
        Assert.That((bool)nail.GetType().GetMethod("TryGetAttachmentInfo", PublicInstance)
            .Invoke(nail, attachmentArgs), Is.True);
        object attachment = attachmentArgs[0];
        object firstReference = attachment.GetType().GetField("FirstTargetReference", PublicInstance).GetValue(attachment);
        object secondReference = attachment.GetType().GetField("SecondTargetReference", PublicInstance).GetValue(attachment);
        MethodInfo isTargetLocked = serverSceneState.GetType().GetMethod("IsTargetLocked", PublicInstance);
        Assert.That((bool)isTargetLocked.Invoke(serverSceneState, new[] { firstReference }), Is.True);
        Assert.That((bool)isTargetLocked.Invoke(serverSceneState, new[] { secondReference }), Is.True);

        // Each rejected remote request consumes its own sequence but leaves the paired nail,
        // panel assessment, and pending-reset state untouched.
        object hammerItemType = Enum.Parse(Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer");
        object shovelItemType = Enum.Parse(Type.GetType("EquippableItemType, Assembly-CSharp"), "Shovel");
        MethodInfo setServerSlots = serverInventory.GetType().GetMethod("SetNetworkSlotValues", PrivateInstance);
        setServerSlots.Invoke(serverInventory, new object[] { Convert.ToInt32(shovelItemType), -1 });
        Assert.That(SelectedServerItemType(serverInventory), Is.EqualTo("Shovel"),
            "The wrong-tool probe changes the authoritative selected item before its RPC.");
        owner.SetSingleton();
        InvokePanelRpc(ownerAction, panelId, aimRay.origin, aimRay.direction, 2);
        host.SetSingleton();
        yield return Until(() => (uint)serverAction.GetType()
            .GetField("lastServerMountingNailImpactSequence", PrivateInstance).GetValue(serverAction) == 2u,
            "wrong-tool request validation");
        Assert.That(nailNetworkObject.IsSpawned, Is.True);
        Assert.That((bool)marker.GetType().GetProperty("ResetImpulsePending", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(marker), Is.False);
        setServerSlots.Invoke(serverInventory, new object[] { Convert.ToInt32(hammerItemType), -1 });
        yield return Until(() => SelectedServerItemType(serverInventory) == "IndustrialHammer", "hammer restore");

        FieldInfo catalogField = serverInventory.GetType().GetField("equippableItemCatalog", PrivateInstance);
        Array originalCatalog = (Array)catalogField.GetValue(serverInventory);
        Array limitedCatalog = (Array)originalCatalog.Clone();
        ScriptableObject limitedHammer = UnityEngine.Object.Instantiate(hammer);
        FieldInfo hammerItemRange = hammer.GetType().GetField("actionRange", PrivateInstance | BindingFlags.Public);
        hammerItemRange.SetValue(limitedHammer, 0.1f);
        int hammerCatalogIndex = -1;
        for (int i = 0; i < limitedCatalog.Length; i++)
            if (limitedCatalog.GetValue(i) == hammer) { hammerCatalogIndex = i; break; }
        Assert.That(hammerCatalogIndex, Is.GreaterThanOrEqualTo(0));
        limitedCatalog.SetValue(limitedHammer, hammerCatalogIndex);
        catalogField.SetValue(serverInventory, limitedCatalog);
        Assert.That(serverInventory.GetType().GetMethod("GetSelectedItemForServerValidation", PublicInstance)
            .Invoke(serverInventory, null), Is.SameAs(limitedHammer));
        Assert.That((float)limitedHammer.GetType().GetField("actionRange", PrivateInstance | BindingFlags.Public)
            .GetValue(limitedHammer), Is.LessThan(0.2f));
        owner.SetSingleton();
        InvokePanelRpc(ownerAction, panelId, aimRay.origin, aimRay.direction, 3);
        host.SetSingleton();
        yield return Until(() => (uint)serverAction.GetType()
            .GetField("lastServerMountingNailImpactSequence", PrivateInstance).GetValue(serverAction) == 3u,
            "out-of-range request validation");
        Assert.That(nailNetworkObject.IsSpawned, Is.True);
        Assert.That((bool)marker.GetType().GetProperty("ResetImpulsePending", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(marker), Is.False);
        catalogField.SetValue(serverInventory, originalCatalog);
        UnityEngine.Object.Destroy(limitedHammer);

        GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(blocker);
        Assert.That(Physics.Raycast(aimRay, out RaycastHit unobstructedHit,
            (float)hammerRangeField.GetValue(hammer), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
        blocker.transform.position = aimRay.GetPoint(unobstructedHit.distance * 0.5f);
        blocker.transform.localScale = Vector3.one * 0.12f;
        Physics.SyncTransforms();
        Assert.That(Physics.Raycast(aimRay, out RaycastHit blockedHit,
            (float)hammerRangeField.GetValue(hammer), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
        Assert.That(blockedHit.collider.transform.root.gameObject, Is.SameAs(blocker),
            $"The occluder must be the closest hit before the panel (panel distance {unobstructedHit.distance}, " +
            $"first hit {blockedHit.collider.name} at {blockedHit.distance}).");
        object[] blockedArgs = { aimRay, (float)hammerRangeField.GetValue(hammer), hostOwnerPlayer.transform, null, null };
        Assert.That((bool)resolverType.GetMethod("TryFindVisiblePanel").Invoke(null, blockedArgs), Is.False,
            "The host ray resolver confirms the test blocker hides the physical panel.");
        owner.SetSingleton();
        InvokePanelRpc(ownerAction, panelId, aimRay.origin, aimRay.direction, 4);
        host.SetSingleton();
        yield return Until(() => (uint)serverAction.GetType()
            .GetField("lastServerMountingNailImpactSequence", PrivateInstance).GetValue(serverAction) == 4u,
            "occluded request validation");
        Assert.That(nailNetworkObject.IsSpawned, Is.True);
        Assert.That((bool)marker.GetType().GetProperty("ResetImpulsePending", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(marker), Is.False);
        blocker.GetComponent<Collider>().enabled = false;
        blocker.SetActive(false);
        UnityEngine.Object.Destroy(blocker);
        Physics.SyncTransforms();

        Assert.That(SelectedServerItemType(serverInventory), Is.EqualTo("IndustrialHammer"),
            "The authoritative inventory has restored the Industrial Hammer for the accepted request.");
        Assert.That((bool)serverAction.GetType().GetMethod("IsTrustedMountingNailAimOrigin", PrivateInstance)
            .Invoke(serverAction, new object[] { aimRay.origin, aimRay.direction }), Is.True,
            "The accepted request origin remains trusted by the server after rejected probes.");
        object[] validPanelArgs = { aimRay, (float)hammerRangeField.GetValue(hammer), hostOwnerPlayer.transform, null, null };
        Assert.That((bool)resolverType.GetMethod("TryFindVisiblePanel").Invoke(null, validPanelArgs), Is.True,
            "The server sees the physical panel after the occluder collider is synchronously disabled.");
        Assert.That(validPanelArgs[3], Is.SameAs(marker));
        object[] validNailArgs = { aimRay, (float)hammerRangeField.GetValue(hammer), hostOwnerPlayer.transform, null, null, null };
        Assert.That((bool)Type.GetType("MountingNailAimResolver, Assembly-CSharp")
            .GetMethod("TryFindCandidate").Invoke(null, validNailArgs), Is.False,
            "The accepted panel request has no higher-priority nail candidate on its ray.");
        double nextImpactTime = (double)serverAction.GetType().GetField("nextServerCombatImpactTime", PrivateInstance)
            .GetValue(serverAction);
        yield return Until(() => host.ServerTime.Time >= nextImpactTime,
            "shared server hammer cadence expiration");

        Vector3 velocityBeforeReset = serverBody.linearVelocity;
        bool gravityBeforePairReset = serverBody.useGravity;
        bool kinematicBeforePairReset = serverBody.isKinematic;
        Physics.simulationMode = SimulationMode.Script;
        restorePhysicsSimulationMode = true;
        owner.SetSingleton();
        InvokePanelRpc(ownerAction, panelId, aimRay.origin, aimRay.direction, 5);
        host.SetSingleton();
        yield return Until(() => (uint)serverAction.GetType()
            .GetField("lastServerMountingNailImpactSequence", PrivateInstance).GetValue(serverAction) == 5u,
            "remote panel RPC sequence consumption");
        yield return Until(() => panelNetworkObject.OwnerClientId == NetworkManager.ServerClientId
            && !(bool)marker.GetType().GetProperty("ResetImpulsePending", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(marker),
            "deferred remote reset impulse after nail cleanup");
        yield return Until(() => Find(host, nailId) == null
            && Find(owner, nailId) == null && Find(observer, nailId) == null,
            "server and replicated removal of the embedded pair nail");
        Assert.That((int)pairJointCount.Invoke(registry, new object[] { panelRoot.transform, beamTransform }), Is.Zero,
            "Reset removes the actual panel-as-first-target FixedJoint edge.");
        Assert.That((bool)isTargetLocked.Invoke(serverSceneState, new[] { firstReference }), Is.False);
        Assert.That((bool)isTargetLocked.Invoke(serverSceneState, new[] { secondReference }), Is.False);
        Assert.That(serverBody.useGravity, Is.True,
            $"A scene-authored unheld panel retains gravity after the real remote hammer reset. " +
            $"spawn={gravityImmediatelyAfterSpawn}/{kinematicImmediatelyAfterSpawn}; " +
            $"firstReset={gravityAfterInitialReset}/{kinematicAfterInitialReset}; " +
            $"clientTransfer={gravityAfterClientOwnership}/{kinematicAfterClientOwnership}; " +
            $"pairRegistration={gravityAfterPairRegistration}/{kinematicAfterPairRegistration}; " +
            $"beforePairReset={gravityBeforePairReset}/{kinematicBeforePairReset}; " +
            $"afterPairReset={serverBody.useGravity}/{serverBody.isKinematic}.");
        Assert.That(serverBody.mass, Is.EqualTo(authoredMass));
        Assert.That(serverBody.linearDamping, Is.EqualTo(authoredLinearDamping));
        Assert.That(serverBody.angularDamping, Is.EqualTo(authoredAngularDamping));
        Assert.That(panelNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
            "Static second-target cleanup must not restore the panel's saved client owner.");
        Assert.That(ReadReplicatedMask(stateRoot.GetComponent(Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp")), marker),
            Is.Zero, "The server promptly reassesses the reset panel to a zero-quarter mask.");
        Assert.That(ReadReplicatedMask(Find(owner, sectionStateObject.NetworkObjectId)
            .GetComponent(Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp")),
            Find(owner, panelId).GetComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"))), Is.Zero);
        Assert.That(ReadReplicatedMask(Find(observer, sectionStateObject.NetworkObjectId)
            .GetComponent(Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp")),
            Find(observer, panelId).GetComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"))), Is.Zero);
        // The accepted RPC and deferred joint cleanup have completed. Simulate one explicit
        // physics step so the accumulated VelocityChange is measured without frame-load or
        // gravity drift obscuring the real server-side delta-V.
        Physics.Simulate(Time.fixedDeltaTime);
        Vector3 appliedDeltaVelocity = serverBody.linearVelocity - velocityBeforeReset;
        Assert.That(appliedDeltaVelocity.y, Is.EqualTo(2f + Physics.gravity.y * Time.fixedDeltaTime).Within(0.1f),
            $"The remote RPC applies its configured upward delta-V while enabled gravity acts during the same simulated step; before={velocityBeforeReset}, after={serverBody.linearVelocity}.");
        Assert.That(new Vector2(appliedDeltaVelocity.x, appliedDeltaVelocity.z).magnitude, Is.GreaterThan(0.4f),
            $"The remote RPC also applies the configured horizontal away delta-V; delta={appliedDeltaVelocity}.");
        Assert.That(panelNetworkObject.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId),
            "The reset panel remains server-owned after the actual client RPC.");
        Assert.That(marker.GetType().GetProperty("ResetImpulsePending", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(marker), Is.False);
        Assert.That(ownerPanel.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(observerPanel.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(ownerPanel.GetComponent<Rigidbody>().isKinematic, Is.True,
            "The old client owner becomes a kinematic server-driven replica after ownership transfer.");
        float acceptedUpwardVelocity = serverBody.linearVelocity.y;
        double acceptedNextImpactTime = (double)serverAction.GetType()
            .GetField("nextServerCombatImpactTime", PrivateInstance).GetValue(serverAction);
        InvokePanelRpc(ownerAction, panelId, aimRay.origin, aimRay.direction, 5);
        InvokePanelRpc(ownerAction, panelId, aimRay.origin, aimRay.direction, 6);
        yield return new WaitForFixedUpdate();
        Assert.That(serverBody.linearVelocity.y, Is.LessThan(acceptedUpwardVelocity + 0.2f),
            "Replay/extra requests do not add another upward reset impulse; contact response may reduce velocity.");
        Assert.That((double)serverAction.GetType().GetField("nextServerCombatImpactTime", PrivateInstance)
            .GetValue(serverAction), Is.EqualTo(acceptedNextImpactTime),
            "Replay/extra requests do not consume or advance the shared hammer cadence.");

        float upwardVelocityBeforeGravityCheck = serverBody.linearVelocity.y;
        for (int step = 0; step < 30; step++) Physics.Simulate(Time.fixedDeltaTime);
        Assert.That(serverBody.useGravity, Is.True);
        Assert.That(serverBody.linearVelocity.y, Is.LessThan(upwardVelocityBeforeGravityCheck - 3f),
            "The reset panel naturally loses upward velocity under its authored gravity instead of remaining in constant flight.");
        Physics.simulationMode = priorPhysicsSimulationMode;
        restorePhysicsSimulationMode = false;

        lateJoin = CreateManager("Stage 3 panel reset late join", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        lateJoin.SetSingleton();
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 4
            && Find(lateJoin, panelId) != null, "late join panel snapshot");
        NetworkObject latePanel = Find(lateJoin, panelId);
        DisableClientPanelColliders(latePanel);
        Assert.That(Find(lateJoin, nailId), Is.Null, "Late joiners do not receive the removed pair nail.");
        Vector3 serverPositionAtSnapshot = panelRoot.transform.position;
        Vector3 ownerPositionAtSnapshot = ownerPanel.transform.position;
        Vector3 observerPositionAtSnapshot = observerPanel.transform.position;
        Vector3 latePositionAtSnapshot = latePanel.transform.position;
        // At the configured ~2 m/s reset speed, 0.4 m bounds roughly 0.2 s of loopback
        // interpolation lag. Wait for measurable server travel, then require each replica to
        // advance in the direction the server actually moved after any physical contacts.
        bool IsWithinReplicationBound() =>
            Vector3.Distance(ownerPanel.transform.position, panelRoot.transform.position) <= 0.4f
            && Vector3.Distance(observerPanel.transform.position, panelRoot.transform.position) <= 0.4f
            && Vector3.Distance(latePanel.transform.position, panelRoot.transform.position) <= 0.4f;
        bool HasSettledOnFloor() => serverBody.linearVelocity.magnitude < 0.15f
            && Physics.Raycast(panelRoot.transform.position, Vector3.down, out RaycastHit floorHit, 5f)
            && floorHit.collider == floorCollider;
        bool HasMovingReplication()
        {
            Vector3 displacement = panelRoot.transform.position - serverPositionAtSnapshot;
            if (displacement.sqrMagnitude <= 0.01f || serverBody.linearVelocity.magnitude <= 0.1f
                || !IsWithinReplicationBound()) return false;
            Vector3 direction = displacement.normalized;
            return Vector3.Dot(ownerPanel.transform.position - ownerPositionAtSnapshot, direction) > 0.02f
                && Vector3.Dot(observerPanel.transform.position - observerPositionAtSnapshot, direction) > 0.02f
                && Vector3.Dot(latePanel.transform.position - latePositionAtSnapshot, direction) > 0.02f;
        }
        float positionDeadline = Time.realtimeSinceStartup + 4f;
        while (Time.realtimeSinceStartup < positionDeadline
            && !HasMovingReplication()
            && !(HasSettledOnFloor() && IsWithinReplicationBound())) yield return null;
        Vector3 serverDisplacement = panelRoot.transform.position - serverPositionAtSnapshot;
        Vector3 serverMotionDirection = serverDisplacement.normalized;
        string replicationDiagnostic =
            $"server start={serverPositionAtSnapshot}; owner start={ownerPositionAtSnapshot}; " +
            $"observer start={observerPositionAtSnapshot}; late start={latePositionAtSnapshot}; " +
            $"server end={panelRoot.transform.position}; owner end={ownerPanel.transform.position}; " +
            $"observer end={observerPanel.transform.position}; late end={latePanel.transform.position}; " +
            $"server velocity={serverBody.linearVelocity}; gravity={serverBody.useGravity}; " +
            $"linearDamping={serverBody.linearDamping}; isKinematic={serverBody.isKinematic}; " +
            $"owner isKinematic={ownerPanel.GetComponent<Rigidbody>().isKinematic}, ownerId={ownerPanel.OwnerClientId}; " +
            $"observer isKinematic={observerPanel.GetComponent<Rigidbody>().isKinematic}, observerId={observerPanel.OwnerClientId}; " +
            $"late isKinematic={latePanel.GetComponent<Rigidbody>().isKinematic}, lateId={latePanel.OwnerClientId}; " +
            $"simulationMode={priorPhysicsSimulationMode}";
        bool settledOnFloor = HasSettledOnFloor();
        Assert.That(HasMovingReplication() || settledOnFloor && IsWithinReplicationBound(), Is.True,
            "The server and replicas either continue matching actual server motion or settle together on the fixture floor. "
            + replicationDiagnostic);
        if (!settledOnFloor)
        {
            Assert.That(serverDisplacement.magnitude, Is.GreaterThan(0.1f),
                "The server continues moving after the reset impulse. " + replicationDiagnostic);
            Assert.That(serverBody.linearVelocity.magnitude, Is.GreaterThan(0.1f),
                "The authoritative body retains motion after contact response. " + replicationDiagnostic);
            Assert.That(Vector3.Dot(ownerPanel.transform.position - ownerPositionAtSnapshot, serverMotionDirection), Is.GreaterThan(0.02f),
                "The former owner replica advances in the server's actual motion direction. " + replicationDiagnostic);
            Assert.That(Vector3.Dot(observerPanel.transform.position - observerPositionAtSnapshot, serverMotionDirection), Is.GreaterThan(0.02f),
                "The observer replica advances in the server's actual motion direction. " + replicationDiagnostic);
            Assert.That(Vector3.Dot(latePanel.transform.position - latePositionAtSnapshot, serverMotionDirection), Is.GreaterThan(0.02f),
                "The late join replica advances in the server's actual motion direction. " + replicationDiagnostic);
        }
        Assert.That(Vector3.Distance(ownerPanel.transform.position, panelRoot.transform.position), Is.LessThan(0.4f),
            $"Owner replication lag exceeds 0.4m: server={panelRoot.transform.position}, owner={ownerPanel.transform.position}, velocity={serverBody.linearVelocity}.");
        Assert.That(Vector3.Distance(observerPanel.transform.position, panelRoot.transform.position), Is.LessThan(0.4f),
            $"Observer replication lag exceeds 0.4m: server={panelRoot.transform.position}, observer={observerPanel.transform.position}, velocity={serverBody.linearVelocity}.");
        Assert.That(Vector3.Distance(latePanel.transform.position, panelRoot.transform.position), Is.LessThan(0.4f),
            $"Late-join replication lag exceeds 0.4m: server={panelRoot.transform.position}, late={latePanel.transform.position}, velocity={serverBody.linearVelocity}.");
        Assert.That(latePanel.OwnerClientId, Is.EqualTo(NetworkManager.ServerClientId));
        Assert.That(ReadReplicatedMask(Find(lateJoin, sectionStateObject.NetworkObjectId)
            .GetComponent(Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp")),
            latePanel.GetComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"))), Is.Zero,
            "Late joiners receive the reset panel's zero-quarter mask.");
        Component mountable = panelRoot.GetComponent(System.Type.GetType("MountableBridgeComponent, Assembly-CSharp"));
        Assert.That(mountable.GetType().GetProperty("IsPickedUp", PublicInstance).GetValue(mountable), Is.False,
            "The server fixture remains an unheld physical panel throughout reset.");
#endif
    }

#if UNITY_EDITOR
    private GameObject CreatePlayerPrefab()
    {
        GameObject prefab = new("Stage 3 panel RPC player prefab");
        roots.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, PlayerHash);
        networkObject.SetSceneObjectStatus(false);
        Behaviour input = (Behaviour)prefab.AddComponent(Type.GetType("PlayerInputNew, Assembly-CSharp"));
        input.enabled = false;
        Component inventory = prefab.AddComponent(Type.GetType("PlayerInventory, Assembly-CSharp"));
        FieldInfo catalogField = inventory.GetType().GetField("equippableItemCatalog", PrivateInstance);
        System.Array catalog = System.Array.CreateInstance(catalogField.FieldType.GetElementType(), 2);
        catalog.SetValue(hammer, 0);
        catalog.SetValue(shovel, 1);
        catalogField.SetValue(inventory, catalog);
        Component interaction = prefab.AddComponent(Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        CharacterController characterController = prefab.AddComponent<CharacterController>();
        characterController.height = 1.8f;
        characterController.radius = 0.3f;
        characterController.center = new Vector3(0f, 0.9f, 0f);
        prefab.AddComponent(Type.GetType("PlayerHealth, Assembly-CSharp"));
        prefab.AddComponent(Type.GetType("PlayerSingleCarryPlacementController, Assembly-CSharp"));
        prefab.AddComponent(Type.GetType("PlayerSingleCarryPlacementNetworkPreview, Assembly-CSharp"));
        prefab.AddComponent(Type.GetType("ClientNetworkTransform, Assembly-CSharp"));
        prefab.AddComponent(Type.GetType("PlayerActionController, Assembly-CSharp"));
        Transform anchor = new GameObject("SingleCarryPlacementAimAnchor").transform;
        anchor.SetParent(prefab.transform, false);
        anchor.localPosition = Vector3.zero;
        interaction.GetType().GetField("interactionOrigin", PrivateInstance).SetValue(interaction, anchor);
        GameObject camera = new("Stage 3 panel RPC camera");
        camera.transform.SetParent(anchor, false);
        camera.tag = "MainCamera";
        Camera cameraComponent = camera.AddComponent<Camera>();
        cameraComponent.nearClipPlane = 0.01f;
        interaction.GetType().GetField("aimCamera", PrivateInstance).SetValue(interaction, cameraComponent);
        return prefab;
    }

    private GameObject CreateSectionStatePrefab()
    {
        GameObject prefab = new("Stage 3 panel section state prefab");
        roots.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(networkObject, 0x42505331u);
        networkObject.SetSceneObjectStatus(false);
        prefab.AddComponent(Type.GetType("BridgeDeckSection, Assembly-CSharp"));
        prefab.AddComponent(Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp"));
        prefab.AddComponent(Type.GetType("MountingNailSceneState, Assembly-CSharp"));
        return prefab;
    }

    private NetworkManager CreateManager(string name, ushort port)
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
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = panelPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = nailPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = sectionStatePrefab });
        managers.Add(manager);
        return manager;
    }

    private void CapturePlayerCopies()
    {
        foreach (NetworkManager manager in managers)
        {
            if (manager == null || !manager.IsListening) continue;
            foreach (NetworkClient client in manager.ConnectedClientsList)
            {
                NetworkObject player = client.PlayerObject;
                if (player != null && !playerCopies.Contains(player.gameObject)) playerCopies.Add(player.gameObject);
            }
        }
    }

    private void DisablePlayerColliders()
    {
        CapturePlayerCopies();
        foreach (GameObject player in playerCopies)
            if (player != null)
                foreach (Collider collider in player.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    private static void DisableClientPanelColliders(NetworkObject panel)
    {
        if (panel == null) return;
        foreach (Collider collider in panel.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    private static NetworkObject Find(NetworkManager manager, ulong id) => manager != null
        && manager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject value) ? value : null;

    private static string SelectedServerItemType(Component inventory)
    {
        object item = inventory.GetType().GetMethod("GetSelectedItemForServerValidation", PublicInstance)
            .Invoke(inventory, null);
        return item?.GetType().GetField("itemType")?.GetValue(item)?.ToString();
    }

    private static byte ReadReplicatedMask(Component state, Component marker)
    {
        object[] args = { marker, null };
        Assert.That((bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args), Is.True,
            "The section state has a current panel assessment.");
        return (byte)args[1].GetType().GetField("QuarterMask").GetValue(args[1]);
    }

    private static Ray GetAimRay(Component interaction)
    {
        object[] args = { default(Ray) };
        Assert.That((bool)interaction.GetType().GetMethod("TryGetAimRay", new[] { typeof(Ray).MakeByRefType() })
            .Invoke(interaction, args), Is.True);
        return (Ray)args[0];
    }

    private static void InvokePanelRpc(Component action, ulong panelId, Vector3 origin, Vector3 direction, uint sequence)
    {
        MethodInfo rpc = action.GetType().GetMethod("RequestPanelHammerImpactServerRpc", PrivateInstance);
        Assert.That(rpc, Is.Not.Null);
        rpc.Invoke(action, new object[] { panelId, origin, direction, sequence, default(ServerRpcParams) });
    }

    private static ushort ReservePort()
    {
        TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        ushort port = (ushort)((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static IEnumerator Until(System.Func<bool> condition, string description)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out waiting for " + description + ".");
    }
#endif

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
#if UNITY_EDITOR
        try
        {
            if (restorePhysicsSimulationMode)
            {
                Physics.simulationMode = priorPhysicsSimulationMode;
                restorePhysicsSimulationMode = false;
            }
            for (int i = managers.Count - 1; i >= 0; i--)
            {
                NetworkManager manager = managers[i];
                if (manager != null && manager.IsListening) manager.Shutdown(true);
            }
            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline
                && managers.Exists(manager => manager != null && (manager.IsListening || manager.ShutdownInProgress))) yield return null;
            foreach (GameObject player in playerCopies)
                if (player != null) UnityEngine.Object.Destroy(player);
            foreach (GameObject root in roots)
                if (root != null) UnityEngine.Object.Destroy(root);
            yield return null;
            RestoreSmokeScenes();
            if (unloadTutorialSmokeScene && tutorialSmokeScene.IsValid() && tutorialSmokeScene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(tutorialSmokeScene);
                if (unload != null)
                {
                    float sceneDeadline = Time.realtimeSinceStartup + 15f;
                    while (!unload.isDone && Time.realtimeSinceStartup < sceneDeadline) yield return null;
                    Assert.That(unload.isDone, Is.True, "The fixture-owned tutorial scene unloads after NGO teardown.");
                }
            }
        }
        finally
        {
            for (int i = managers.Count - 1; i >= 0; i--)
            {
                NetworkManager manager = managers[i];
                if (manager != null && manager.IsListening) manager.Shutdown(true);
            }
            foreach (GameObject player in playerCopies)
                if (player != null) UnityEngine.Object.Destroy(player);
            foreach (GameObject root in roots)
                if (root != null) UnityEngine.Object.Destroy(root);
            if (priorSingleton != null) priorSingleton.SetSingleton();
            else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            RestoreSmokeScenes();
            managers.Clear();
            roots.Clear();
            playerCopies.Clear();
        }
#endif
    }

#if UNITY_EDITOR
    private void RestoreSmokeScenes()
    {
        if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
            SceneManager.SetActiveScene(originalActiveScene);
    }
#endif
}
