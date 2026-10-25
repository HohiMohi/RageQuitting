using System;
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

public sealed class SawCuttingNetworkPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const uint ActorHash = 0xD3100101;
    private const uint PanelHash = 0xD3100102;
    private const uint FragmentHash = 0xD3100103;
    private const uint NailSceneStateHash = 0xD3100104;
    private const uint GirderHash = 0xD3100105;
    private static readonly Type InputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
    private static readonly Type InventoryType = Type.GetType("PlayerInventory, Assembly-CSharp");
    private static readonly Type InteractionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
    private static readonly Type ActionType = Type.GetType("PlayerActionController, Assembly-CSharp");
    private static readonly Type PanelType = Type.GetType("BridgeDeckAreaPanel, Assembly-CSharp");
    private static readonly Type ItemType = Type.GetType("EquippableItemSO, Assembly-CSharp");
    private static readonly Type ProfileType = Type.GetType("SawCutProfileSO, Assembly-CSharp");
    private readonly List<GameObject> roots = new();
    private readonly List<NetworkManager> managers = new();
    private readonly CrowbarInputSettingsScope inputSettings = new();
    private NetworkManager previousSingleton, host, client, lateJoin;
    private GameObject actorPrefab, panelPrefab, fragmentPrefab, floor, nailPrefab, nailStatePrefab, girderPrefab;
    private ScriptableObject sawItem, cutProfile, canonicalProfile;
    private NetworkObject serverActor, sourceNetworkObject;
    private Component ownerActor, ownerAction, ownerInput, ownerInventory, serverPanel;
    private Camera ownerCamera;
    private Transform ownerAimAnchor;
    private Mouse ownerMouse, hostMouse;
    private Keyboard ownerKeyboard, hostKeyboard;
    private InputAction ownerPrimaryAction;
    private ushort port;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        bool needsDiagnosticNailFixtures = TestContext.CurrentContext.Test.Name
            == nameof(NarrowOpeningDiagnosticPreservesFourReplicatedNailFactsOnRemoteClient);
        nailStatePrefab = null;
        girderPrefab = null;
        if (!needsDiagnosticNailFixtures)
            inputSettings.UseFocusedGameViewRouting();
        previousSingleton = NetworkManager.Singleton;
        ownerMouse = InputSystem.AddDevice<Mouse>();
        ownerKeyboard = InputSystem.AddDevice<Keyboard>();
        hostMouse = InputSystem.AddDevice<Mouse>();
        hostKeyboard = InputSystem.AddDevice<Keyboard>();
        cutProfile = ScriptableObject.CreateInstance(ProfileType);
        canonicalProfile = ScriptableObject.CreateInstance(Type.GetType("BridgeDeckAreaProfile, Assembly-CSharp"));
        ProfileType.GetField("geometryProfile", PublicInstance).SetValue(cutProfile, canonicalProfile);
        sawItem = ScriptableObject.CreateInstance(ItemType);
        ItemType.GetField("itemName", PublicInstance).SetValue(sawItem, "Saw network test item");
        ItemType.GetField("itemType", PublicInstance).SetValue(sawItem,
            Enum.Parse(Type.GetType("EquippableItemType, Assembly-CSharp"), "Saw"));
        ItemType.GetField("inventorySlotsRequired", PublicInstance).SetValue(sawItem, 2);
        ItemType.GetField("actionRange", PublicInstance).SetValue(sawItem, 2.5f);
        ItemType.GetField("sawCutProfile", PublicInstance).SetValue(sawItem, cutProfile);

        fragmentPrefab = CreatePanelPrefab("Saw network fragment", FragmentHash);
        if (TestContext.CurrentContext.Test.Name == nameof(AtomicCutRollsBackFirstSpawnWhenSecondFragmentPreparationFails))
            fragmentPrefab.AddComponent<SawCutSecondFragmentConfigurationFailure>();
        panelPrefab = CreatePanelPrefab("Saw network source", PanelHash);
        nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null, "The existing production nail prefab is required for replicated attachment facts.");
        if (needsDiagnosticNailFixtures)
        {
            nailStatePrefab = CreateNailSceneStatePrefab();
            girderPrefab = CreateNetworkGirderPrefab();
        }
        ProfileType.GetField("fragmentNetworkPrefab", PublicInstance).SetValue(cutProfile, fragmentPrefab);
        actorPrefab = CreateActorPrefab();

        port = ReservePort();
        host = CreateManager("Saw test host", port);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return Until(() => host.IsListening, "host startup");
        client = CreateManager("Saw test remote owner", port);
        Assert.That(client.StartClient(), Is.True);
        yield return Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2,
            "remote owner connection");

        serverActor = SpawnPlayer(actorPrefab, client.LocalClientId, new Vector3(0f, 0f, 0f));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(serverActor.NetworkObjectId),
            "remote owner actor replica");
        ownerActor = client.SpawnManager.SpawnedObjects[serverActor.NetworkObjectId].GetComponent(ActionType);
        ownerInput = client.SpawnManager.SpawnedObjects[serverActor.NetworkObjectId].GetComponent(InputType);
        ownerInventory = client.SpawnManager.SpawnedObjects[serverActor.NetworkObjectId].GetComponent(InventoryType);
        ownerAction = ownerActor;
        ownerAimAnchor = ownerActor.transform.Find("AimAnchor");
        GameObject cameraObject = new("Saw network owner camera");
        roots.Add(cameraObject);
        cameraObject.transform.SetParent(ownerActor.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.2f, -.1f);
        cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, .3f, 1.2f) - cameraObject.transform.position);
        ownerCamera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        InteractionType.GetMethod("SetInteractionOrigin", PublicInstance).Invoke(
            ownerActor.GetComponent(InteractionType), new object[] { ownerAimAnchor });
        InteractionType.GetMethod("SetAimCamera", PublicInstance).Invoke(
            ownerActor.GetComponent(InteractionType), new object[] { ownerCamera });
        ConfigureInputDevices(ownerInput, ownerMouse, ownerKeyboard);
        Assert.That((bool)InventoryType.GetMethod("AddItem", PublicInstance).Invoke(ownerInventory, new[] { sawItem }), Is.True);
        yield return Until(() => ActionType.GetProperty("SelectedSawProfile", PublicInstance)
                .GetValue(serverActor.GetComponent(ActionType)) != null, "server-side selected Saw profile");

        floor = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
        floor.name = "Saw network static support";
        floor.transform.position = new Vector3(0f, .1f, 1.2f);
        floor.transform.localScale = new Vector3(5f, .2f, 5f);
        Physics.SyncTransforms();
        sourceNetworkObject = SpawnNetworked(panelPrefab, new Vector3(0f, .2f, 1.2f));
        serverPanel = sourceNetworkObject.GetComponent(PanelType);
        sourceNetworkObject.GetComponent<BoxCollider>().enabled = false;
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(sourceNetworkObject.NetworkObjectId),
            "remote source panel replica");
        IgnoreReplicaPanelCollisions(sourceNetworkObject.NetworkObjectId);
        sourceNetworkObject.GetComponent<BoxCollider>().enabled = true;
        NetworkObject clientSourceReplica = client.SpawnManager.SpawnedObjects[sourceNetworkObject.NetworkObjectId];
        clientSourceReplica.GetComponent<BoxCollider>().enabled = true;
        clientSourceReplica.GetComponent<Rigidbody>().isKinematic = true;
        Physics.SyncTransforms();
        yield return new WaitForSecondsRealtime(.5f);
        Assert.That((bool)PanelType.GetMethod("IsEligible", PublicInstance).Invoke(
            serverPanel, new object[] { cutProfile, true }), Is.True,
            "Server stock must be stable and supported before input. " + GetEligibilityDiagnostics());
        Assert.That((bool)InputType.GetProperty("IsLocalGameplayInputActive", PublicInstance).GetValue(ownerInput), Is.True);
        Assert.That(InventoryType.GetMethod("GetCurrentSelectedItem", PublicInstance).Invoke(ownerInventory, null), Is.SameAs(sawItem));
        object generated = InputType.GetField("playerGameInputActions", PrivateInstance).GetValue(ownerInput);
        object game = generated.GetType().GetProperty("Game", PublicInstance).GetValue(generated);
        ownerPrimaryAction = (InputAction)game.GetType().GetProperty("Action", PublicInstance).GetValue(game);
        Assert.That(ownerPrimaryAction.enabled, Is.True);
    }

    [UnityTest]
    public IEnumerator RemoteLeftMouseCutReplicatesPartialStateToLateJoinAndDisconnectPauses()
    {
        ProfileType.GetField("activeCutSeconds", PublicInstance).SetValue(cutProfile, 20f);
        Component ownerPanel = client.SpawnManager.SpawnedObjects[sourceNetworkObject.NetworkObjectId].GetComponent(PanelType);
        NetworkObject clientSourceReplica = client.SpawnManager.SpawnedObjects[sourceNetworkObject.NetworkObjectId];
        ((Behaviour)clientSourceReplica.GetComponent(Type.GetType(
            "Unity.Netcode.Components.NetworkTransform, Unity.Netcode.Runtime"))).enabled = false;
        sourceNetworkObject.transform.position += Vector3.right * .005f;
        Physics.SyncTransforms();
        yield return new WaitForSecondsRealtime(.45f);
        Assert.That((bool)PanelType.GetMethod("IsEligible", PublicInstance)
            .Invoke(serverPanel, new object[] { cutProfile, true }), Is.True,
            "The server panel must be stable and eligible after the small allowed pose update.");
        MethodInfo localPreview = ActionType.GetMethod("TryResolveSawPreview", PrivateInstance);
        object[] previewArgs = { null, 0, null, false };
        Assert.That((bool)localPreview.Invoke(ownerAction, previewArgs), Is.True,
            "The remote owner must still aim at the previous replicated panel pose.");
        int ownerPreviewOffset = (int)previewArgs[1];
        Vector3 forgedAimOrigin = ownerCamera.transform.position;
        Vector3 forgedAimDirection = ownerCamera.transform.forward;
        System.Reflection.MethodInfo forgedBegin = PanelType.GetMethod("RequestBeginCutServerRpc", PublicInstance);
        forgedBegin.Invoke(serverPanel, new object[] { ulong.MaxValue, forgedAimOrigin,
            forgedAimDirection, 9001u,
            Activator.CreateInstance(forgedBegin.GetParameters()[4].ParameterType) });
        yield return new WaitForSecondsRealtime(.15f);
        Assert.That((bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(serverPanel), Is.False,
            "The server must reject a forged actor identity before reserving a cut.");

        QueueOwnerLeft(true);
        Assert.That(ownerPrimaryAction.IsPressed(), Is.True);
        yield return Until(() => (bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(serverPanel)
            && (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel) > 0,
            "server-authoritative remote Saw cut progress");
        yield return Until(() => (bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(ownerPanel)
            && (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(ownerPanel) > 0,
            "replicated remote Saw progress for HUD presentation");
        MethodInfo isLocalOperator = ActionType.GetMethod("IsLocalSawOperator", PrivateInstance);
        Assert.That((bool)isLocalOperator.Invoke(ownerAction, new object[] { ownerPanel, client.LocalClientId }), Is.True,
            "A remote client recognizes its own active Saw reservation on its panel replica.");
        Assert.That((bool)isLocalOperator.Invoke(ownerAction, new object[] { serverPanel, NetworkManager.ServerClientId }), Is.True,
            "The host recognizes a host-owned reservation using the network session LocalClientId.");
        Assert.That((bool)isLocalOperator.Invoke(ownerAction, new object[] { serverPanel, client.LocalClientId }), Is.False,
            "A remote client's id is not mistaken for the host's local active operator.");
        object[] sawPresentation = { ownerPanel, null, 0f, false };
        Assert.That((bool)ActionType.GetMethod("TryGetSawCutPresentation", PublicInstance)
            .Invoke(ownerAction, sawPresentation), Is.True);
        StringAssert.Contains("hold LMB to continue", (string)sawPresentation[1],
            "The real active owner sees a continue hold prompt after network progress is replicated.");
        int sourceOffset = (int)PanelType.GetProperty("CutOffsetMillimeters", PublicInstance).GetValue(serverPanel);
        Assert.That(Math.Abs(sourceOffset - ownerPreviewOffset), Is.GreaterThanOrEqualTo(2),
            "A 5mm server/client pose difference exceeds the old 1mm check; the server must accept the verified hit and select its own line.");
        object activeState = PanelType.GetProperty("State", PublicInstance).GetValue(serverPanel);
        uint activeSequence = (uint)activeState.GetType().GetField("OperatorRequestSequence").GetValue(activeState);
        ulong activeActorId = (ulong)activeState.GetType().GetField("OperatorNetworkObjectId").GetValue(activeState);
        ulong sourceId = sourceNetworkObject.NetworkObjectId;
        int progressBeforeLateJoin = (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel);

        // Server validation resolves the Saw profile through ConnectedClients.PlayerObject;
        // a merely owned actor is insufficient to represent a second human player.
        NetworkObject competitor = SpawnPlayer(actorPrefab, NetworkManager.ServerClientId, Vector3.zero);
        Component competitorInventory = competitor.GetComponent(InventoryType);
        Assert.That((bool)InventoryType.GetMethod("AddItem", PublicInstance).Invoke(competitorInventory, new[] { sawItem }), Is.True);
        Transform competitorAnchor = competitor.transform.Find("AimAnchor");
        Assert.That(competitorAnchor, Is.Not.Null);
        Vector3 competitorDirection = (sourceNetworkObject.transform.TransformPoint(new Vector3(0f, .15f, 0f))
            - competitorAnchor.position).normalized;
        Component competitorAction = competitor.GetComponent(ActionType);
        Assert.That((bool)ActionType.GetMethod("ServerSawRequestIsValid", PublicInstance,
            null, new[] { PanelType, typeof(Vector3), typeof(Vector3), typeof(ulong) }, null)
            .Invoke(competitorAction, new object[] { serverPanel, competitorAnchor.position, competitorDirection, NetworkManager.ServerClientId }),
            Is.True, "The competing actor must independently pass server-side Saw, origin, LOS and range validation.");
        forgedBegin.Invoke(serverPanel, new object[] { competitor.NetworkObjectId, competitorAnchor.position,
            competitorDirection, 1u, Activator.CreateInstance(forgedBegin.GetParameters()[4].ParameterType) });
        yield return new WaitForSecondsRealtime(.1f);
        object afterCompetition = PanelType.GetProperty("State", PublicInstance).GetValue(serverPanel);
        Assert.That((ulong)afterCompetition.GetType().GetField("OperatorNetworkObjectId").GetValue(afterCompetition),
            Is.EqualTo(activeActorId), "A second valid Saw operator cannot take over a held cut reservation.");
        Assert.That((ulong)afterCompetition.GetType().GetField("OperatorClientId").GetValue(afterCompetition),
            Is.EqualTo(client.LocalClientId));
        int progressBeforeOperatorHandoff = (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel);

        lateJoin = CreateManager("Saw test late join observer", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3,
            "late observer connection");
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(sourceId),
            "late observer panel snapshot");
        Component latePanel = lateJoin.SpawnManager.SpawnedObjects[sourceId].GetComponent(PanelType);
        IgnoreReplicaPanelCollisions(sourceId);
        Assert.That((bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(latePanel), Is.True);
        Assert.That((int)PanelType.GetProperty("CutOffsetMillimeters", PublicInstance).GetValue(latePanel), Is.EqualTo(sourceOffset));
        Assert.That((int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(latePanel),
            Is.GreaterThanOrEqualTo(progressBeforeLateJoin));

        QueueOwnerLeft(false);
        yield return Until(() => !IsOperatorAssigned(serverPanel), "release original operator reservation");
        yield return Until(() => (bool)PanelType.GetMethod("IsEligible", PublicInstance)
                .Invoke(serverPanel, new object[] { cutProfile, true }),
            "server stability after the late observer joins");
        forgedBegin.Invoke(serverPanel, new object[] { competitor.NetworkObjectId, competitorAnchor.position,
            competitorDirection, 2u, Activator.CreateInstance(forgedBegin.GetParameters()[4].ParameterType) });
        yield return Until(() => IsOperatorAssigned(serverPanel)
                && GetOperatorSequence(serverPanel) == 2u
                && (ulong)PanelType.GetProperty("State", PublicInstance).GetValue(serverPanel)
                    .GetType().GetField("OperatorNetworkObjectId").GetValue(
                        PanelType.GetProperty("State", PublicInstance).GetValue(serverPanel)) == competitor.NetworkObjectId,
            "a different valid operator resumes the same immutable cut line");
        System.Reflection.MethodInfo stalePause = PanelType.GetMethod("PauseCutServerRpc", PublicInstance);
        stalePause.Invoke(serverPanel, new object[] { serverActor.NetworkObjectId, activeSequence,
            RpcParams(client.LocalClientId) });
        Assert.That(IsOperatorAssigned(serverPanel), Is.True,
            "The old operator cannot pause the competitor's newer attempt immediately after resume.");
        System.Reflection.MethodInfo refresh = PanelType.GetMethod("RefreshCutServerRpc", PublicInstance);
        for (int heartbeat = 0; heartbeat < 3; heartbeat++)
        {
            refresh.Invoke(serverPanel, new object[] { competitor.NetworkObjectId, competitorAnchor.position,
                competitorDirection, 2u, RpcParams(NetworkManager.ServerClientId) });
            yield return new WaitForSecondsRealtime(.08f);
        }
        object competitorResumeState = PanelType.GetProperty("State", PublicInstance).GetValue(serverPanel);
        Assert.That((int)competitorResumeState.GetType().GetField("CutOffsetMillimeters").GetValue(competitorResumeState),
            Is.EqualTo(sourceOffset), "Resuming from another operator must preserve the first cut line.");
        Assert.That((int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel),
            Is.GreaterThan(progressBeforeOperatorHandoff), "Saved active time must continue after another operator resumes.");
        Assert.That(IsOperatorAssigned(serverPanel), Is.True,
            "The competitor reservation remains active while heartbeats refresh eligibility.");

        stalePause.Invoke(serverPanel, new object[] { competitor.NetworkObjectId, 2u,
            RpcParams(NetworkManager.ServerClientId) });
        yield return Until(() => !IsOperatorAssigned(serverPanel), "competitor releases the reservation");
        QueueOwnerLeft(true);
        float resumeDeadline = Time.realtimeSinceStartup + 12f;
        while ((!IsOperatorAssigned(serverPanel) || GetOperatorSequence(serverPanel) <= activeSequence)
            && Time.realtimeSinceStartup < resumeDeadline) yield return null;
        Assert.That(IsOperatorAssigned(serverPanel) && GetOperatorSequence(serverPanel) > activeSequence, Is.True,
            "Original remote owner must resume after handoff. " + GetResumeDiagnostics(ownerPanel));
        stalePause.Invoke(serverPanel, new object[] { competitor.NetworkObjectId, 2u,
            RpcParams(NetworkManager.ServerClientId) });
        yield return new WaitForSecondsRealtime(.1f);
        Assert.That(IsOperatorAssigned(serverPanel), Is.True,
            "An old pause request cannot cancel the operator's newer resumed attempt.");
        QueueOwnerLeft(false);
        host.DisconnectClient(client.LocalClientId);
        yield return Until(() => !client.IsConnectedClient, "remote owner disconnect");
        yield return Until(() => (ulong)PanelType.GetProperty("State", PublicInstance)
                .GetValue(serverPanel).GetType().GetField("OperatorClientId").GetValue(
                    PanelType.GetProperty("State", PublicInstance).GetValue(serverPanel)) == ulong.MaxValue,
            "disconnect releases the panel reservation");
        int preservedProgress = (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel);
        Assert.That(preservedProgress, Is.GreaterThanOrEqualTo(progressBeforeLateJoin));
        yield return new WaitForSecondsRealtime(.2f);
        Assert.That((int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel),
            Is.EqualTo(preservedProgress), "Disconnected and paused time cannot accrue.");
        Assert.That((int)PanelType.GetProperty("CutOffsetMillimeters", PublicInstance).GetValue(serverPanel), Is.EqualTo(sourceOffset));
    }

    [UnityTest]
    public IEnumerator RepeatedShortRemotePressesAccumulateInsteadOfLosingEachSubHundredMillisecondAttempt()
    {
        ProfileType.GetField("activeCutSeconds", PublicInstance).SetValue(cutProfile, .8f);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            QueueOwnerLeft(true);
            yield return Until(() => IsOperatorAssigned(serverPanel), $"operator reservation for short attempt {attempt + 1}");
            yield return new WaitForSecondsRealtime(.055f);
            QueueOwnerLeft(false);
            yield return Until(() => !IsOperatorAssigned(serverPanel), $"pause for short attempt {attempt + 1}");
        }
        int progress = (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel);
        Assert.That(progress, Is.GreaterThanOrEqualTo(300),
            "Several short, server-validated holds must accumulate active time even when every hold is under 100ms.");
        Assert.That((bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(serverPanel), Is.True);
    }

    [UnityTest]
    public IEnumerator FinalizedAreaLockReplicatesToLateJoinAndCannotBeCleared()
    {
        PanelType.GetMethod("SetAreaAssessmentState", PublicInstance)
            .Invoke(serverPanel, new object[] { true, true, false });
        Component clientPanel = client.SpawnManager.SpawnedObjects[sourceNetworkObject.NetworkObjectId].GetComponent(PanelType);
        yield return Until(() => (bool)PanelType.GetProperty("IsSecuredForArea", PublicInstance).GetValue(clientPanel),
            "existing client receives the replicated four-nail state");
        Component clientRequirement = CreateClientDiagnosticRequirement(clientPanel);
        object clientAssessment = Activator.CreateInstance(Type.GetType("BridgeDeckAreaStageAssessment, Assembly-CSharp"));
        object clientResult = clientAssessment.GetType().GetMethod("Evaluate").Invoke(clientAssessment, new[] { clientRequirement });
        Assert.That((bool)clientResult.GetType().GetField("IsValid").GetValue(clientResult), Is.True);
        float sourceLengthMeters = (int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(clientPanel) * .001f;
        Assert.That((float)clientResult.GetType().GetField("CoveredLengthMeters").GetValue(clientResult),
            Is.EqualTo(sourceLengthMeters).Within(.002f));
        Assert.That((float)clientResult.GetType().GetField("SecuredLengthMeters").GetValue(clientResult),
            Is.EqualTo(sourceLengthMeters).Within(.002f),
            "The actual client-side stage evaluator must count its spawned panel's replicated secured snapshot.");
        object diagnosticOut = null;
        object[] diagnosticArgs = { clientPanel, clientResult, diagnosticOut };
        Assert.That((bool)clientRequirement.GetType().GetMethod("TryGetPanelDiagnostic", PublicInstance)
            .Invoke(clientRequirement, diagnosticArgs), Is.True);
        object clientFourNailDiagnostic = diagnosticArgs[2];
        Assert.That(clientFourNailDiagnostic.GetType().GetField("Kind").GetValue(clientFourNailDiagnostic).ToString(), Is.EqualTo("FourNails"));
        Assert.That((bool)PanelType.GetMethod("SetAreaLock", PublicInstance)
            .Invoke(serverPanel, new object[] { true }), Is.True);
        yield return Until(() => (bool)PanelType.GetProperty("IsLockedForCompletedArea", PublicInstance).GetValue(
                client.SpawnManager.SpawnedObjects[sourceNetworkObject.NetworkObjectId].GetComponent(PanelType)),
            "existing client receives finalized area lock");

        lateJoin = CreateManager("Saw test late join area-lock observer", port);
        Assert.That(lateJoin.StartClient(), Is.True);
        yield return Until(() => lateJoin.IsConnectedClient && host.ConnectedClientsIds.Count == 3,
            "late observer connection after area completion");
        yield return Until(() => lateJoin.SpawnManager.SpawnedObjects.ContainsKey(sourceNetworkObject.NetworkObjectId),
            "late observer receives finalized contributor");
        Component latePanel = lateJoin.SpawnManager.SpawnedObjects[sourceNetworkObject.NetworkObjectId].GetComponent(PanelType);
        Assert.That((bool)PanelType.GetProperty("IsSpawned", PublicInstance).GetValue(latePanel), Is.True);
        Assert.That((bool)PanelType.GetProperty("IsServer", PublicInstance).GetValue(latePanel), Is.False,
            "This assertion reads the actual late-joined client replica, not a host-side fixture.");
        object lateState = PanelType.GetProperty("State", PublicInstance).GetValue(latePanel);
        object hostState = PanelType.GetProperty("State", PublicInstance).GetValue(serverPanel);
        Assert.That((bool)lateState.GetType().GetField("AreaSecured").GetValue(lateState),
            Is.EqualTo((bool)hostState.GetType().GetField("AreaSecured").GetValue(hostState)));
        Assert.That((bool)lateState.GetType().GetField("AreaLocked").GetValue(lateState),
            Is.EqualTo((bool)hostState.GetType().GetField("AreaLocked").GetValue(hostState)));
        Assert.That((bool)lateState.GetType().GetField("CutStarted").GetValue(lateState),
            Is.EqualTo((bool)hostState.GetType().GetField("CutStarted").GetValue(hostState)));
        Assert.That((int)lateState.GetType().GetField("ProgressMilliseconds").GetValue(lateState),
            Is.EqualTo((int)hostState.GetType().GetField("ProgressMilliseconds").GetValue(hostState)));
        Assert.That((int)lateState.GetType().GetField("LengthMillimeters").GetValue(lateState),
            Is.EqualTo((int)hostState.GetType().GetField("LengthMillimeters").GetValue(hostState)));
        Assert.That((bool)PanelType.GetProperty("IsLockedForCompletedArea", PublicInstance).GetValue(latePanel), Is.True,
            "The completed-stage lock must be included in the initial network snapshot for late joiners.");
        Assert.That((bool)PanelType.GetProperty("IsSecuredForArea", PublicInstance).GetValue(latePanel), Is.True,
            "The client replica must consume the replicated 4/4 secured state used by area diagnostics.");
        Assert.That((bool)PanelType.GetProperty("IsSecuredForArea", PublicInstance).GetValue(latePanel),
            Is.EqualTo((bool)PanelType.GetProperty("IsSecuredForArea", PublicInstance).GetValue(serverPanel)));
        Component lateRequirement = CreateClientDiagnosticRequirement(latePanel);
        object lateAssessment = Activator.CreateInstance(Type.GetType("BridgeDeckAreaStageAssessment, Assembly-CSharp"));
        object lateResult = lateAssessment.GetType().GetMethod("Evaluate").Invoke(lateAssessment, new[] { lateRequirement });
        object[] lateDiagnosticArgs = { latePanel, lateResult, null };
        Assert.That((bool)lateRequirement.GetType().GetMethod("TryGetPanelDiagnostic", PublicInstance)
            .Invoke(lateRequirement, lateDiagnosticArgs), Is.True);
        object lateDiagnostic = lateDiagnosticArgs[2];
        Assert.That(lateDiagnostic.GetType().GetField("Kind").GetValue(lateDiagnostic).ToString(), Is.EqualTo("Locked"),
            "The real client evaluator/diagnostic path reads the replicated stage lock from a late-join panel.");
        Component hostRequirement = CreateClientDiagnosticRequirement(serverPanel);
        object hostAssessment = Activator.CreateInstance(Type.GetType("BridgeDeckAreaStageAssessment, Assembly-CSharp"));
        object hostResult = hostAssessment.GetType().GetMethod("Evaluate").Invoke(hostAssessment, new[] { hostRequirement });
        Assert.That((float)lateResult.GetType().GetField("CoveredLengthMeters").GetValue(lateResult),
            Is.EqualTo((float)hostResult.GetType().GetField("CoveredLengthMeters").GetValue(hostResult)).Within(.001f));
        Assert.That((float)lateResult.GetType().GetField("SecuredLengthMeters").GetValue(lateResult),
            Is.EqualTo((float)hostResult.GetType().GetField("SecuredLengthMeters").GetValue(hostResult)).Within(.001f));
        object[] hostDiagnosticArgs = { serverPanel, hostResult, null };
        Assert.That((bool)hostRequirement.GetType().GetMethod("TryGetPanelDiagnostic", PublicInstance)
            .Invoke(hostRequirement, hostDiagnosticArgs), Is.True);
        Assert.That(lateDiagnostic.GetType().GetField("Kind").GetValue(lateDiagnostic).ToString(),
            Is.EqualTo(hostDiagnosticArgs[2].GetType().GetField("Kind").GetValue(hostDiagnosticArgs[2]).ToString()),
            "Host and actual late-client assessment produce the same lock diagnosis and progress.");
        Assert.That((bool)PanelType.GetMethod("SetAreaLock", PublicInstance)
            .Invoke(serverPanel, new object[] { false }), Is.False,
            "A durable stage-finalized lock cannot be reversed by a stale or reset path.");
        Assert.That((bool)PanelType.GetProperty("IsLockedForCompletedArea", PublicInstance).GetValue(serverPanel), Is.True);
    }

    [UnityTest]
    public IEnumerator NarrowOpeningDiagnosticPreservesFourReplicatedNailFactsOnRemoteClient()
    {
        ulong setupSourceId = sourceNetworkObject.NetworkObjectId;
        sourceNetworkObject.Despawn();
        yield return Until(() => !client.SpawnManager.SpawnedObjects.ContainsKey(setupSourceId),
            "the initial setup panel to despawn before the diagnostic source spawn");
        // Keep this diagnostic-only layout static from the first spawn tick on both peers.
        // Enabling physics on the client replica before its setup coroutine resumes can move
        // an otherwise authoritative test panel and make the two assessments describe
        // different layouts.
        Rigidbody panelBodyTemplate = panelPrefab.GetComponent<Rigidbody>();
        BoxCollider panelColliderTemplate = panelPrefab.GetComponent<BoxCollider>();
        panelBodyTemplate.isKinematic = true;
        panelBodyTemplate.useGravity = false;
        panelColliderTemplate.enabled = false;
        sourceNetworkObject = SpawnNetworked(panelPrefab, new Vector3(-2.03f, .241f, 1.2f));
        serverPanel = sourceNetworkObject.GetComponent(PanelType);
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(sourceNetworkObject.NetworkObjectId),
            "the diagnostic source panel replica");
        IgnoreReplicaPanelCollisions(sourceNetworkObject.NetworkObjectId);
        NetworkObject clientSourceObject = client.SpawnManager.SpawnedObjects[sourceNetworkObject.NetworkObjectId];
        clientSourceObject.GetComponent<Rigidbody>().isKinematic = true;
        clientSourceObject.GetComponent<Rigidbody>().useGravity = false;
        clientSourceObject.GetComponent<BoxCollider>().enabled = true;
        sourceNetworkObject.GetComponent<BoxCollider>().enabled = true;
        NetworkObject rightPanelObject = SpawnNetworked(panelPrefab, new Vector3(.57f, .2f, 1.2f));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(rightPanelObject.NetworkObjectId),
            "the narrow-opening neighbor panel replica");
        IgnoreReplicaPanelCollisions(rightPanelObject.NetworkObjectId);
        NetworkObject clientRightPanelObject = client.SpawnManager.SpawnedObjects[rightPanelObject.NetworkObjectId];
        clientRightPanelObject.GetComponent<Rigidbody>().isKinematic = true;
        clientRightPanelObject.GetComponent<Rigidbody>().useGravity = false;
        clientRightPanelObject.GetComponent<BoxCollider>().enabled = true;
        rightPanelObject.GetComponent<BoxCollider>().enabled = true;

        NetworkObject stateObject = SpawnNetworked(nailStatePrefab, new Vector3(0f, -20f, 0f));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(stateObject.NetworkObjectId),
            "the remote MountingNailSceneState replica");
        NetworkObject lowerGirder = SpawnNetworked(girderPrefab, new Vector3(0f, .1f, .2f));
        NetworkObject upperGirder = SpawnNetworked(girderPrefab, new Vector3(0f, .1f, 2.2f));
        yield return Until(() => client.SpawnManager.SpawnedObjects.ContainsKey(lowerGirder.NetworkObjectId)
            && client.SpawnManager.SpawnedObjects.ContainsKey(upperGirder.NetworkObjectId),
            "both registered main-girder replicas");

        Type requirementType = Type.GetType("BridgeDeckAreaRequirement, Assembly-CSharp");
        Component hostRequirement = CreateClientDiagnosticRequirement(serverPanel);
        Component clientSourcePanel = client.SpawnManager.SpawnedObjects[sourceNetworkObject.NetworkObjectId]
            .GetComponent(PanelType);
        yield return Until(() => PanelReplicaPoseMatches(serverPanel, clientSourcePanel)
            && PanelReplicaPoseMatches(rightPanelObject.GetComponent(PanelType), clientRightPanelObject.GetComponent(PanelType)),
            "host and client panel transforms/collider dimensions to converge before assessment");
        Component clientRequirement = CreateClientDiagnosticRequirement(clientSourcePanel);
        Vector3 areaFrame = new(0f, .2f, 1.2f);
        ConfigureDiagnosticRequirement(hostRequirement, lowerGirder.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp")),
            upperGirder.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp")), areaFrame);
        ConfigureDiagnosticRequirement(clientRequirement,
            client.SpawnManager.SpawnedObjects[lowerGirder.NetworkObjectId].GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp")),
            client.SpawnManager.SpawnedObjects[upperGirder.NetworkObjectId].GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp")),
            areaFrame);

        Transform[] girderTargets = { lowerGirder.transform, lowerGirder.transform, upperGirder.transform, upperGirder.transform };
        Vector2[] quarterPoints = { new(-.5f, -1f), new(.5f, -1f), new(-.5f, 1f), new(.5f, 1f) };
        for (int i = 0; i < quarterPoints.Length; i++)
        {
            Vector3 entry = sourceNetworkObject.transform.TransformPoint(new Vector3(quarterPoints[i].x, .2f, quarterPoints[i].y));
            Component nail = CreateSpawnedNail(entry);
            Type nailType = nail.GetType();
            nailType.GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(nail,
                new object[] { sourceNetworkObject.transform, girderTargets[i], entry, -sourceNetworkObject.transform.up });
            nailType.GetMethod("SetHitCounts", PrivateInstance).Invoke(nail, new object[] { 3, 3, .02f });
            nailType.GetMethod("SetState", PrivateInstance).Invoke(nail,
                new[] { Enum.Parse(Type.GetType("MountingNailState, Assembly-CSharp"), "EmbeddedPair") });
            Assert.That((bool)nailType.GetMethod("TryRegisterPairConnection", PrivateInstance).Invoke(nail, null), Is.True,
                $"Real production nail attachment {i} must register against the spawned panel and configured main girder.");
        }
        float attachmentDeadline = Time.realtimeSinceStartup + 12f;
        while (!HasFourResolvedClientAttachments(client, sourceNetworkObject.NetworkObjectId)
            && Time.realtimeSinceStartup < attachmentDeadline) yield return null;
        Assert.That(HasFourResolvedClientAttachments(client, sourceNetworkObject.NetworkObjectId), Is.True,
            "Four real EmbeddedPair attachments must resolve on the client. " + DescribeClientAttachments(client, sourceNetworkObject.NetworkObjectId));

        object hostAssessment = Activator.CreateInstance(Type.GetType("BridgeDeckAreaStageAssessment, Assembly-CSharp"));
        object clientAssessment = Activator.CreateInstance(Type.GetType("BridgeDeckAreaStageAssessment, Assembly-CSharp"));
        object hostResult = hostAssessment.GetType().GetMethod("Evaluate").Invoke(hostAssessment, new[] { hostRequirement });
        object clientResult = clientAssessment.GetType().GetMethod("Evaluate").Invoke(clientAssessment, new[] { clientRequirement });
        IEnumerable clientContributors = (IEnumerable)clientResult.GetType().GetField("ContributingPanels").GetValue(clientResult);
        Assert.That(ContainsReference(clientContributors, clientRightPanelObject.GetComponent(PanelType)), Is.True,
            "The adjacent panel must remain geometrically valid and contribute to the measured opening. "
            + DescribeAreaResult(clientResult) + " | " + DescribeActiveAreaPanels(clientRequirement));
        object hostDiagnostic = GetPresentationDiagnostic(requirementType, hostRequirement, serverPanel, hostResult, out bool hostHasFourNails);
        object clientDiagnostic = GetPresentationDiagnostic(requirementType, clientRequirement, clientSourcePanel, clientResult,
            out bool clientHasFourNails);
        Assert.That((bool)PanelType.GetProperty("IsSpawned", PublicInstance).GetValue(clientSourcePanel), Is.True);
        Assert.That((bool)PanelType.GetProperty("IsServer", PublicInstance).GetValue(clientSourcePanel), Is.False,
            "The new factual nail suffix is evaluated on the actual spawned remote-client panel replica.");
        Assert.That(clientDiagnostic.GetType().GetField("Kind").GetValue(clientDiagnostic).ToString(), Is.EqualTo("NarrowOpening"));
        Assert.That(hostDiagnostic.GetType().GetField("Kind").GetValue(hostDiagnostic).ToString(), Is.EqualTo("NarrowOpening"));
        Assert.That(clientHasFourNails, Is.True);
        Assert.That(hostHasFourNails, Is.True);
        foreach (string metric in new[] { "MinimumGapMeters", "MaximumGapMeters", "RequiredGapMeters" })
            Assert.That((float)clientDiagnostic.GetType().GetField(metric).GetValue(clientDiagnostic),
                Is.EqualTo((float)hostDiagnostic.GetType().GetField(metric).GetValue(hostDiagnostic)).Within(.0001f), metric);
        MethodInfo formatter = requirementType.GetMethod("FormatDiagnostic", BindingFlags.Public | BindingFlags.Static);
        string clientText = (string)formatter.Invoke(null, new[] { clientDiagnostic, (object)clientHasFourNails, false });
        Assert.That(clientText, Does.Contain("too narrow").And.Contain("4/4 nails"),
            "The real client formatter retains its replicated four-nail fact beside a blocking narrow-opening warning.");
    }

    private Component CreateClientDiagnosticRequirement(Component panel)
    {
        Type requirementType = Type.GetType("BridgeDeckAreaRequirement, Assembly-CSharp");
        Component requirement = panel.gameObject.AddComponent(requirementType);
        ((Behaviour)requirement).enabled = false;
        Component presentation = panel.GetComponent(Type.GetType("BridgeDeckAreaPresentation, Assembly-CSharp"));
        if (presentation != null) ((Behaviour)presentation).enabled = false;
        GameObject frame = new("Network area diagnostic frame");
        roots.Add(frame);
        frame.transform.SetPositionAndRotation(panel.transform.position, panel.transform.rotation);
        GameObject sectionRoot = new("Network area diagnostic section");
        roots.Add(sectionRoot);
        Component section = sectionRoot.AddComponent(Type.GetType("BridgeDeckSection, Assembly-CSharp"));
        Array girders = Array.CreateInstance(Type.GetType("BridgeComponent, Assembly-CSharp"), 2);
        section.GetType().GetField("mainGirders", PrivateInstance).SetValue(section, girders);
        requirementType.GetField("profile", PrivateInstance).SetValue(requirement, canonicalProfile);
        requirementType.GetField("deckFrame", PrivateInstance).SetValue(requirement, frame.transform);
        requirementType.GetField("deckSection", PrivateInstance).SetValue(requirement, section);
        return requirement;
    }

    [UnityTest]
    public IEnumerator AtomicCutRollsBackFirstSpawnWhenSecondFragmentPreparationFails()
    {
        ProfileType.GetField("activeCutSeconds", PublicInstance).SetValue(cutProfile, .2f);
        SawCutSecondFragmentConfigurationFailure.ArmOnSecondInstance();
        ulong sourceId = sourceNetworkObject.NetworkObjectId;
        int priorProgress = (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel);
        using (var logs = new ScopedCutRollbackLogCapture())
        {
            QueueOwnerLeft(true);
            yield return Until(() => !IsOperatorAssigned(serverPanel)
                    && (bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(serverPanel)
                    && (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel) >= 200,
                "second fragment spawn failure rolls back the first child");
            Assert.That(host.SpawnManager.SpawnedObjects.ContainsKey(sourceId), Is.True,
                "The source must remain spawned when the second fragment spawn aborts the transaction.");
            Assert.That(FindCutFragments(host).Count, Is.Zero,
                "The first spawned fragment must be despawned when the second spawn fails.");
            Assert.That(FindCutFragments(client).Count, Is.Zero,
                "Observers must never see a partial fragment transaction.");
            Assert.That((int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel),
                Is.GreaterThanOrEqualTo(priorProgress), "Rollback preserves accumulated cut time.");
            Assert.That(logs.RollbackMessages.Count, Is.EqualTo(1), "Production emits one rollback diagnostic.");
            Assert.That(logs.RollbackMessages[0], Does.Contain("Fragment prefab requires BridgeDeckAreaPanel."));
            QueueOwnerLeft(false);
        }
    }

    [UnityTest]
    public IEnumerator RemoteFourSecondCutSpawnsReplicatedSizedChildrenAndRetiresSource()
    {
        ulong sourceId = sourceNetworkObject.NetworkObjectId;
        Vector3 sourcePlacementAtCutStart = sourceNetworkObject.transform.position;
        Quaternion sourceRotationAtCutStart = sourceNetworkObject.transform.rotation;
        QueueOwnerLeft(true);
        yield return Until(() => IsOperatorAssigned(serverPanel), "remote cut reservation");
        yield return Until(() => (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(serverPanel) >= 3900,
            "remote cut reaching its final active interval");
        SawCutTestPoseCapture.Reset();
        yield return Until(() => !host.SpawnManager.SpawnedObjects.ContainsKey(sourceId), "source retirement after cut");
        sourcePlacementAtCutStart = SawCutTestPoseCapture.LastServerPosition;
        sourceRotationAtCutStart = SawCutTestPoseCapture.LastServerRotation;
        yield return Until(() => FindCutFragments(host).Count == 2, "two host fragments after atomic cut");
        QueueOwnerLeft(false);
        List<Component> serverFragments = FindCutFragments(host);
        Assert.That(serverFragments.Count, Is.EqualTo(2));
        serverFragments.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
        int leftLength = (int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(serverFragments[0]);
        int rightLength = (int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(serverFragments[1]);
        Assert.That(leftLength + rightLength + 5, Is.EqualTo(2500));
        Assert.That(leftLength, Is.EqualTo(1250));
        Assert.That(rightLength, Is.EqualTo(1245));
        Vector3 expectedFirstPosition = sourcePlacementAtCutStart + sourceRotationAtCutStart * new Vector3(-.625f, 0f, 0f);
        Vector3 expectedSecondPosition = sourcePlacementAtCutStart + sourceRotationAtCutStart * new Vector3(.6275f, 0f, 0f);
        Assert.That(Vector3.Distance(serverFragments[0].transform.position, expectedFirstPosition), Is.LessThan(.005f),
            $"first actual={serverFragments[0].transform.position}, expected={expectedFirstPosition}, source={sourcePlacementAtCutStart}, rotation={sourceRotationAtCutStart}");
        Assert.That(Vector3.Distance(serverFragments[1].transform.position, expectedSecondPosition), Is.LessThan(.005f),
            $"second actual={serverFragments[1].transform.position}, expected={expectedSecondPosition}, source={sourcePlacementAtCutStart}, rotation={sourceRotationAtCutStart}");
        Assert.That(Quaternion.Angle(serverFragments[0].transform.rotation, sourceRotationAtCutStart), Is.LessThan(.1f));
        Assert.That(Quaternion.Angle(serverFragments[1].transform.rotation, sourceRotationAtCutStart), Is.LessThan(.1f));
        foreach (Component fragment in serverFragments)
        {
            Assert.That((int)PanelType.GetProperty("WidthMillimeters", PublicInstance).GetValue(fragment), Is.EqualTo(2400));
            Assert.That(fragment.GetComponent<BoxCollider>().size.x,
                Is.EqualTo(leftLength == (int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(fragment)
                    ? leftLength * .001f : rightLength * .001f).Within(.001f));
            Assert.That(fragment.GetComponent<Rigidbody>().mass,
                Is.EqualTo((int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(fragment) / 2500f).Within(.001f));
        }
        yield return Until(() => FindCutFragments(client).Count == 2, "remote owner receives both fragments");
        IsolateMatchingPanelReplicas();
        foreach (Component fragment in FindCutFragments(client))
        {
            int length = (int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(fragment);
            Assert.That(fragment.GetComponent<BoxCollider>().size.x, Is.EqualTo(length * .001f).Within(.001f));
        }
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SawCutSecondFragmentConfigurationFailure.Disarm();
        foreach (NetworkManager manager in managers)
            if (manager != null && manager.IsListening) manager.Shutdown(true);
        float shutdownDeadline = Time.realtimeSinceStartup + 12f;
        while (Time.realtimeSinceStartup < shutdownDeadline
            && managers.Exists(manager => manager != null && (manager.IsListening || manager.ShutdownInProgress)))
            yield return null;
        foreach (GameObject root in roots) if (root != null) UnityEngine.Object.Destroy(root);
        foreach (NetworkManager manager in managers) if (manager != null) UnityEngine.Object.Destroy(manager.gameObject);
        if (sawItem != null) UnityEngine.Object.Destroy(sawItem);
        if (cutProfile != null) UnityEngine.Object.Destroy(cutProfile);
        if (canonicalProfile != null) UnityEngine.Object.Destroy(canonicalProfile);
        RemoveDevice(ownerMouse); RemoveDevice(ownerKeyboard); RemoveDevice(hostMouse); RemoveDevice(hostKeyboard);
        inputSettings.Restore();
        if (previousSingleton != null && previousSingleton.IsListening) previousSingleton.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", PrivateStatic)?.Invoke(null, null);
        yield return null;
    }

    private GameObject CreatePanelPrefab(string name, uint hash)
    {
        GameObject prefab = new(name);
        roots.Add(prefab);
        prefab.AddComponent<NetworkObject>();
        prefab.AddComponent(Type.GetType("ClientNetworkTransform, Assembly-CSharp"));
        prefab.AddComponent<MeshFilter>(); prefab.AddComponent<MeshRenderer>();
        prefab.AddComponent<Rigidbody>().useGravity = false;
        prefab.AddComponent<BoxCollider>();
        prefab.AddComponent(PanelType);
        prefab.AddComponent<SawCutTestPoseCapture>();
        if (hash == FragmentHash) prefab.GetComponent<NetworkObject>().SpawnWithObservers = false;
        SetHash(prefab.GetComponent<NetworkObject>(), hash);
        SetSceneObjectStatus(prefab.GetComponent<NetworkObject>(), false);
        prefab.transform.position = new Vector3(0f, -10000f, 0f);
        return prefab;
    }

    private GameObject CreateNailSceneStatePrefab()
    {
        GameObject prefab = new("Saw network nail scene-state prefab");
        roots.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        prefab.AddComponent(Type.GetType("MountingNailSceneState, Assembly-CSharp"));
        SetHash(networkObject, NailSceneStateHash);
        SetSceneObjectStatus(networkObject, false);
        prefab.transform.position = new Vector3(0f, -10000f, 0f);
        return prefab;
    }

    private GameObject CreateNetworkGirderPrefab()
    {
        GameObject prefab = new("Saw network registered girder prefab");
        roots.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        SetHash(networkObject, GirderHash);
        SetSceneObjectStatus(networkObject, false);
        Rigidbody body = prefab.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        prefab.AddComponent<BoxCollider>().size = new Vector3(6.56f, .2f, .2f);
        ((Behaviour)prefab.AddComponent(Type.GetType("BridgeComponent, Assembly-CSharp"))).enabled = false;
        prefab.transform.position = new Vector3(0f, -10000f, 0f);
        return prefab;
    }

    private GameObject CreateActorPrefab()
    {
        GameObject prefab = new("Saw network actor prefab");
        roots.Add(prefab);
        NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
        SetHash(networkObject, ActorHash); SetSceneObjectStatus(networkObject, false);
        prefab.AddComponent(Type.GetType("ClientNetworkTransform, Assembly-CSharp"));
        prefab.AddComponent(InputType);
        Component inventory = prefab.AddComponent(InventoryType);
        UnityEditor.SerializedObject serializedInventory = new(inventory);
        UnityEditor.SerializedProperty catalog = serializedInventory.FindProperty("equippableItemCatalog");
        catalog.arraySize = 1; catalog.GetArrayElementAtIndex(0).objectReferenceValue = sawItem;
        serializedInventory.ApplyModifiedPropertiesWithoutUndo();
        Component interaction = prefab.AddComponent(InteractionType);
        prefab.AddComponent(Type.GetType("PlayerHealth, Assembly-CSharp"));
        prefab.AddComponent(ActionType);
        Transform anchor = new GameObject("AimAnchor").transform;
        anchor.SetParent(prefab.transform, false); anchor.localPosition = new Vector3(0f, 1.2f, -.1f);
        InteractionType.GetField("interactionOrigin", PrivateInstance).SetValue(interaction, anchor);
        prefab.transform.position = new Vector3(0f, -10000f, 0f);
        return prefab;
    }

    private NetworkManager CreateManager(string name, ushort localPort)
    {
        GameObject root = new(name); roots.Add(root);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", localPort, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = actorPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = panelPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = fragmentPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = nailPrefab });
        if (nailStatePrefab != null)
            manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = nailStatePrefab });
        if (girderPrefab != null)
            manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = girderPrefab });
        managers.Add(manager);
        return manager;
    }

    private NetworkObject SpawnPlayer(GameObject prefab, ulong owner, Vector3 position)
    {
        NetworkObject value = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<NetworkObject>();
        Own(value.gameObject); SetSceneObjectStatus(value, false); value.SpawnAsPlayerObject(owner);
        return value;
    }

    private NetworkObject SpawnOwnedActor(GameObject prefab, ulong owner, Vector3 position)
    {
        NetworkObject value = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<NetworkObject>();
        Own(value.gameObject); SetSceneObjectStatus(value, false); value.SpawnWithOwnership(owner);
        return value;
    }

    private NetworkObject SpawnNetworked(GameObject prefab, Vector3 position)
    {
        NetworkObject value = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<NetworkObject>();
        Own(value.gameObject); SetSceneObjectStatus(value, false);
        if (!value.gameObject.activeSelf) value.gameObject.SetActive(true);
        value.Spawn();
        return value;
    }

    private Component CreateSpawnedNail(Vector3 position)
    {
        GameObject instance = UnityEngine.Object.Instantiate(nailPrefab, position, Quaternion.identity);
        Own(instance);
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        SetSceneObjectStatus(networkObject, false);
        networkObject.Spawn();
        return instance.GetComponent(Type.GetType("MountingNail, Assembly-CSharp"));
    }

    private static void ConfigureDiagnosticRequirement(Component requirement, Component firstGirder,
        Component secondGirder, Vector3 areaFramePosition)
    {
        Type requirementType = Type.GetType("BridgeDeckAreaRequirement, Assembly-CSharp");
        Transform frame = (Transform)requirementType.GetField("deckFrame", PrivateInstance).GetValue(requirement);
        frame.SetPositionAndRotation(areaFramePosition, Quaternion.identity);
        Component section = (Component)requirementType.GetField("deckSection", PrivateInstance).GetValue(requirement);
        FieldInfo girdersField = section.GetType().GetField("mainGirders", PrivateInstance);
        Type elementType = girdersField.FieldType.GetElementType();
        Array girders = Array.CreateInstance(elementType, 2);
        girders.SetValue(firstGirder, 0); girders.SetValue(secondGirder, 1);
        girdersField.SetValue(section, girders);
    }

    private static object GetPresentationDiagnostic(Type requirementType, Component requirement, Component panel,
        object result, out bool hasFourPhysicalNails)
    {
        MethodInfo method = requirementType.GetMethod("TryGetPanelPresentationDiagnostic", PublicInstance);
        object[] args = { panel, result, null, false };
        Assert.That((bool)method.Invoke(requirement, args), Is.True);
        hasFourPhysicalNails = (bool)args[3];
        return args[2];
    }

    private bool HasFourResolvedClientAttachments(NetworkManager manager, ulong panelId)
    {
        if (manager == null || !manager.SpawnManager.SpawnedObjects.TryGetValue(panelId, out NetworkObject panelObject)) return false;
        Type nailType = Type.GetType("MountingNail, Assembly-CSharp");
        int count = 0;
        foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjects.Values)
        {
            Component nail = networkObject.GetComponent(nailType);
            if (nail == null) continue;
            object[] args = { null };
            if (!(bool)nailType.GetMethod("TryGetAttachmentInfo", PublicInstance).Invoke(nail, args)) continue;
            object info = args[0];
            if (info.GetType().GetField("State").GetValue(info).ToString() == "EmbeddedPair"
                && (Transform)info.GetType().GetField("FirstTarget").GetValue(info) == panelObject.transform
                && (Transform)info.GetType().GetField("SecondTarget").GetValue(info) != null) count++;
        }
        return count >= 4;
    }

    private string DescribeClientAttachments(NetworkManager manager, ulong panelId)
    {
        if (manager == null || !manager.SpawnManager.SpawnedObjects.TryGetValue(panelId, out _)) return "Remote panel replica missing.";
        Type nailType = Type.GetType("MountingNail, Assembly-CSharp");
        var details = new List<string>();
        foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjects.Values)
        {
            Component nail = networkObject.GetComponent(nailType);
            if (nail == null) continue;
            object networkVariable = nailType.GetField("replicatedAttachment", PrivateInstance).GetValue(nail);
            object snapshot = networkVariable.GetType().GetProperty("Value").GetValue(networkVariable);
            object first = snapshot.GetType().GetField("First").GetValue(snapshot);
            object second = snapshot.GetType().GetField("Second").GetValue(snapshot);
            object[] args = { null };
            bool resolved = (bool)nailType.GetMethod("TryGetAttachmentInfo", PublicInstance).Invoke(nail, args);
            Component sceneState = nailType.GetMethod("FindSceneState", PrivateInstance).Invoke(nail, null) as Component;
            NetworkManager nailManager = (NetworkManager)nailType.GetProperty("NetworkManager", PublicInstance).GetValue(nail);
            NetworkManager stateManager = sceneState != null
                ? (NetworkManager)sceneState.GetType().GetProperty("ScopedNetworkManager", PublicInstance).GetValue(sceneState) : null;
            bool firstCanResolve = false, secondCanResolve = false;
            ulong secondId = (ulong)second.GetType().GetField("NetworkObjectId").GetValue(second);
            bool secondReplicaFound = manager.SpawnManager.SpawnedObjects.TryGetValue(secondId, out NetworkObject secondReplica);
            bool secondManagerMatches = secondReplicaFound && secondReplica.NetworkManager == stateManager;
            bool secondIsSpawned = secondReplicaFound && secondReplica.IsSpawned;
            bool secondPathResolves = false;
            if (secondReplicaFound)
            {
                Type identityType = Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp");
                MethodInfo rootResolver = identityType.GetMethod("TryResolvePhysicalRoot", BindingFlags.Public | BindingFlags.Static);
                secondPathResolves = (bool)rootResolver.Invoke(null, new[] { secondReplica, second, null });
            }
            string cachedTarget = "<none>";
            if (sceneState != null)
            {
                var cache = sceneState.GetType().GetField("resolvedNetworkTargets", PrivateInstance).GetValue(sceneState) as System.Collections.IDictionary;
                if (cache != null && cache.Contains(second))
                {
                    object cached = cache[second];
                    FieldInfo cachedObjectField = cached.GetType().GetField("networkObject") ?? cached.GetType().GetField("Item1");
                    FieldInfo cachedRootField = cached.GetType().GetField("physicalRoot") ?? cached.GetType().GetField("Item2");
                    NetworkObject cachedNetworkObject = (NetworkObject)cachedObjectField.GetValue(cached);
                    Transform cachedRoot = (Transform)cachedRootField.GetValue(cached);
                    cachedTarget = $"{(cachedNetworkObject != null ? cachedNetworkObject.NetworkObjectId.ToString() : "<null>")}:{(cachedRoot != null ? cachedRoot.name : "<null>")}";
                }
            }
            if (sceneState != null)
            {
                MethodInfo resolve = sceneState.GetType().GetMethod("TryResolve", PublicInstance);
                object[] firstArgs = { first, null }, secondArgs = { second, null };
                firstCanResolve = (bool)resolve.Invoke(sceneState, firstArgs);
                secondCanResolve = (bool)resolve.Invoke(sceneState, secondArgs);
            }
            details.Add($"id={networkObject.NetworkObjectId}, state={nailType.GetProperty("State", PublicInstance).GetValue(nail)}, "
                + $"firstValid={first.GetType().GetProperty("IsValid").GetValue(first)}, firstId={first.GetType().GetField("NetworkObjectId").GetValue(first)}, "
                + $"secondValid={second.GetType().GetProperty("IsValid").GetValue(second)}, secondIsNetwork={second.GetType().GetField("IsNetworkObject").GetValue(second)}, "
                + $"secondId={second.GetType().GetField("NetworkObjectId").GetValue(second)}, secondPath={second.GetType().GetField("PhysicalRootPath").GetValue(second)}, "
                + $"secondFound={secondReplicaFound}, secondManagerMatches={secondManagerMatches}, secondSpawned={secondIsSpawned}, secondPathResolves={secondPathResolves}, "
                + $"cachedTarget={cachedTarget}, replicaRoot={(secondReplicaFound ? secondReplica.transform.GetComponentInParent<NetworkObject>()?.NetworkObjectId.ToString() : "<none>")}, "
                + $"manager={(nailManager != null ? nailManager.name + "#" + nailManager.GetInstanceID() : "<null>")}, "
                + $"sceneState={(sceneState != null ? sceneState.name + "#" + sceneState.GetInstanceID() : "<null>")}, "
                + $"stateManager={(stateManager != null ? stateManager.name + "#" + stateManager.GetInstanceID() : "<null>")}, "
                + $"scenes={nail.gameObject.scene.handle}/{(sceneState != null ? sceneState.gameObject.scene.handle : 0)}, "
                + $"targetCount={(sceneState != null ? sceneState.GetType().GetProperty("TargetStateCount", PublicInstance).GetValue(sceneState) : 0)}, "
                + $"resolveFirst={firstCanResolve}, resolveSecond={secondCanResolve}, resolved={resolved}, "
                + $"firstTarget={(resolved ? ((Transform)args[0].GetType().GetField("FirstTarget").GetValue(args[0])).name : "<null>")}, "
                + $"secondTarget={(resolved ? ((Transform)args[0].GetType().GetField("SecondTarget").GetValue(args[0])).name : "<null>")}");
        }
        var spawned = new List<string>();
        foreach (NetworkObject item in manager.SpawnManager.SpawnedObjects.Values)
            spawned.Add($"{item.NetworkObjectId}:{item.name}#{item.NetworkManager.GetInstanceID()}");
        return $"spawned=[{string.Join(",", spawned)}] | " + string.Join(" | ", details);
    }

    private static bool ContainsReference(IEnumerable values, object expected)
    {
        foreach (object value in values)
            if (ReferenceEquals(value, expected)) return true;
        return false;
    }

    private static bool PanelReplicaPoseMatches(Component authoritative, Component replica)
    {
        if (authoritative == null || replica == null) return false;
        if (Vector3.Distance(authoritative.transform.position, replica.transform.position) > .002f
            || Quaternion.Angle(authoritative.transform.rotation, replica.transform.rotation) > .1f) return false;
        BoxCollider a = authoritative.GetComponent<BoxCollider>(), b = replica.GetComponent<BoxCollider>();
        return a != null && b != null && a.enabled == b.enabled
            && Vector3.Distance(a.center, b.center) <= .001f && Vector3.Distance(a.size, b.size) <= .001f;
    }

    private static string DescribeAreaResult(object result)
    {
        Type type = result.GetType();
        var panels = new List<string>();
        foreach (object panel in (IEnumerable)type.GetField("ContributingPanels").GetValue(result))
            panels.Add(panel != null ? panel.ToString() : "<null>");
        var diagnostics = new List<string>();
        foreach (object entry in (IEnumerable)type.GetField("PanelDiagnostics").GetValue(result))
        {
            object panel = entry.GetType().GetField("Panel").GetValue(entry);
            object diagnostic = entry.GetType().GetField("Diagnostic").GetValue(entry);
            object kind = diagnostic.GetType().GetField("Kind").GetValue(diagnostic);
            Component component = panel as Component;
            string pose = component != null
                ? $"@{component.transform.position.ToString("F3")}/L{component.GetType().GetProperty("LengthMillimeters", PublicInstance).GetValue(component)}/W{component.GetType().GetProperty("WidthMillimeters", PublicInstance).GetValue(component)}"
                : string.Empty;
            diagnostics.Add($"{(panel != null ? panel.ToString() : "<null>")}{pose}:{kind}");
        }
        return $"valid={type.GetField("IsValid").GetValue(result)}, complete={type.GetField("IsComplete").GetValue(result)}, "
            + $"covered={type.GetField("CoveredLengthMeters").GetValue(result)}, secured={type.GetField("SecuredLengthMeters").GetValue(result)}, "
            + $"contributors=[{string.Join(",", panels)}], diagnostics=[{string.Join(",", diagnostics)}]";
    }

    private static string DescribeActiveAreaPanels(Component requirement)
    {
        Type panelType = Type.GetType("BridgeDeckAreaPanel, Assembly-CSharp");
        Type listType = typeof(List<>).MakeGenericType(panelType);
        object list = Activator.CreateInstance(listType);
        panelType.GetMethod("CopyActivePanels", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new[] { (object)requirement.gameObject.scene, requirement.GetType().GetProperty("NetworkManager", PublicInstance).GetValue(requirement), list });
        List<string> descriptions = new();
        foreach (object candidate in (IEnumerable)list)
        {
            GameObject gameObject = ((Component)candidate).gameObject;
            BoxCollider collider = gameObject.GetComponent<BoxCollider>();
            Rigidbody body = gameObject.GetComponent<Rigidbody>();
            descriptions.Add($"{gameObject.name}@{gameObject.transform.position:F3} scene={gameObject.scene.handle}/{gameObject.scene.name}"
                + $" spawned={PanelType.GetProperty("IsSpawned", PublicInstance).GetValue(candidate)}"
                + $" manager={candidate.GetType().GetProperty("NetworkManager", PublicInstance).GetValue(candidate)?.GetHashCode()}"
                + $" active={gameObject.activeInHierarchy}/{((Behaviour)candidate).enabled}"
                + $" collider={collider?.enabled}/{collider?.isTrigger}/{collider?.center:F3}/{collider?.size:F3}"
                + $" body={body?.isKinematic}/{body?.useGravity}");
        }
        return $"scope scene={requirement.gameObject.scene.handle}/{requirement.gameObject.scene.name}"
            + $" manager={requirement.GetType().GetProperty("NetworkManager", PublicInstance).GetValue(requirement)?.GetHashCode()}"
            + " candidates=[" + string.Join(" | ", descriptions) + "]";
    }

    private void ConfigureInputDevices(Component input, InputDevice mouse, InputDevice keyboard)
    {
        object generated = InputType.GetField("playerGameInputActions", PrivateInstance).GetValue(input);
        InputActionAsset asset = (InputActionAsset)generated.GetType().GetProperty("asset").GetValue(generated);
        asset.devices = new[] { keyboard, mouse };
        InputSystem.Update();
    }

    private void QueueOwnerLeft(bool down)
    {
        MouseState state = default;
        InputSystem.QueueStateEvent(ownerMouse, state.WithButton(MouseButton.Left, down));
        InputSystem.Update();
    }

    private GameObject Own(GameObject value) { roots.Add(value); return value; }
    private static bool IsOperatorAssigned(Component panel)
    {
        object state = PanelType.GetProperty("State", PublicInstance).GetValue(panel);
        return (ulong)state.GetType().GetField("OperatorClientId").GetValue(state) != ulong.MaxValue;
    }

    private static ServerRpcParams RpcParams(ulong senderClientId)
        => new() { Receive = new ServerRpcReceiveParams { SenderClientId = senderClientId } };
    private static uint GetOperatorSequence(Component panel)
    {
        object state = PanelType.GetProperty("State", PublicInstance).GetValue(panel);
        return (uint)state.GetType().GetField("OperatorRequestSequence").GetValue(state);
    }
    private static List<Component> FindCutFragments(NetworkManager manager)
    {
        var result = new List<Component>();
        if (manager == null || !manager.IsListening) return result;
        foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjectsList)
        {
            Component panel = networkObject != null ? networkObject.GetComponent(PanelType) : null;
            if (panel != null && networkObject.NetworkObjectId != 0
                && (int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(panel) < 2500)
                result.Add(panel);
        }
        return result;
    }
    private string GetEligibilityDiagnostics()
    {
        Rigidbody body = sourceNetworkObject != null ? sourceNetworkObject.GetComponent<Rigidbody>() : null;
        object stable = PanelType.GetField("stableSeconds", PrivateInstance).GetValue(serverPanel);
        bool support = (bool)PanelType.GetMethod("HasStaticSupport", PrivateInstance).Invoke(serverPanel, new object[] { cutProfile });
        return $"stable={stable}, bodyNull={body == null}, kinematic={body?.isKinematic}, "
            + $"velocity={body?.linearVelocity}, angular={body?.angularVelocity}, support={support}, "
            + $"position={sourceNetworkObject.transform.position}, hits={GetSupportHitDiagnostics()}";
    }

    private string GetResumeDiagnostics(Component ownerPanel)
    {
        return $"serverOperator={IsOperatorAssigned(serverPanel)}, serverSequence={GetOperatorSequence(serverPanel)}, "
            + $"clientHeld={ActionType.GetField("sawPrimaryHeld", PrivateInstance).GetValue(ownerAction)}, "
            + $"blockedUntilRelease={ActionType.GetField("sawBlockedUntilRelease", PrivateInstance).GetValue(ownerAction)}, "
            + $"activeTarget={ActionType.GetField("activeSawTarget", PrivateInstance).GetValue(ownerAction)}, "
            + $"previewTarget={ActionType.GetField("previewSawTarget", PrivateInstance).GetValue(ownerAction)}, "
            + $"primaryPressed={ownerPrimaryAction.IsPressed()}, previewValid={ActionType.GetField("sawPreviewValid", PrivateInstance).GetValue(ownerAction)}, "
            + $"cutStarted={PanelType.GetProperty("CutStarted", PublicInstance).GetValue(ownerPanel)}";
    }
    private string GetSupportHitDiagnostics()
    {
        string result = "";
        Vector3[] local = { new(-1.25f, 0f, -1.2f), new(1.25f, 0f, -1.2f), new(1.25f, 0f, 1.2f), new(-1.25f, 0f, 1.2f) };
        for (int i = 0; i < local.Length; i++)
        {
            Vector3 point = sourceNetworkObject.transform.TransformPoint(local[i]);
            foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * .035f, Vector3.down, .045f))
                result += $"[{i}:{hit.collider.name}@{hit.point.y:F3}/{hit.normal.y:F2}]";
        }
        return result;
    }
    private void IgnoreReplicaPanelCollisions(ulong networkObjectId)
    {
        var replicas = new List<Collider>();
        foreach (NetworkManager manager in managers)
        {
            if (manager == null || !manager.IsListening
                || !manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject replica)) continue;
            Collider collider = replica.GetComponent<Collider>();
            if (collider != null) replicas.Add(collider);
        }
        for (int i = 0; i < replicas.Count; i++)
            for (int j = i + 1; j < replicas.Count; j++)
                Physics.IgnoreCollision(replicas[i], replicas[j], true);
    }
    private void IsolateMatchingPanelReplicas()
    {
        if (host == null || !host.IsListening) return;
        foreach (NetworkObject serverObject in host.SpawnManager.SpawnedObjectsList)
            if (serverObject != null && serverObject.GetComponent(PanelType) != null)
                IgnoreReplicaPanelCollisions(serverObject.NetworkObjectId);
    }
    private static void SetHash(NetworkObject value, uint hash) => typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(value, hash);
    private static void SetSceneObjectStatus(NetworkObject value, bool sceneObject) => value.SetSceneObjectStatus(sceneObject);
    private static ushort ReservePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0); listener.Start();
        try { return (ushort)((IPEndPoint)listener.LocalEndpoint).Port; } finally { listener.Stop(); }
    }
    private static IEnumerator Until(Func<bool> condition, string message)
    {
        float deadline = Time.realtimeSinceStartup + 12f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, $"Timed out waiting for {message}.");
    }
    private static void RemoveDevice(InputDevice value)
    {
        if (value != null && value.added) InputSystem.RemoveDevice(value);
    }
}

internal sealed class SawCutSecondFragmentConfigurationFailure : MonoBehaviour
{
    private static bool armed;
    private static int instanceCount;

    internal static void ArmOnSecondInstance() { instanceCount = 0; armed = true; }
    internal static void Disarm() { armed = false; instanceCount = 0; }

    private void Awake()
    {
        if (armed && ++instanceCount == 2)
        {
            armed = false;
            Component panel = GetComponent(Type.GetType("BridgeDeckAreaPanel, Assembly-CSharp"));
            if (panel != null) DestroyImmediate(panel);
        }
    }
}

internal sealed class ScopedCutRollbackLogCapture : System.IDisposable, ILogHandler
{
    private readonly ILogHandler previous;
    public readonly List<string> RollbackMessages = new();

    public ScopedCutRollbackLogCapture()
    {
        previous = Debug.unityLogger.logHandler;
        Debug.unityLogger.logHandler = this;
    }

    public void LogException(Exception exception, UnityEngine.Object context) => previous.LogException(exception, context);

    public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
    {
        string message = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
        if (logType == LogType.Error && message.StartsWith("Deck plate cut transaction rolled back:", System.StringComparison.Ordinal))
        {
            RollbackMessages.Add(message);
            return;
        }
        previous.LogFormat(logType, context, format, args);
    }

    public void Dispose() => Debug.unityLogger.logHandler = previous;
}

public sealed class SawCutTestPoseCapture : Unity.Netcode.NetworkBehaviour
{
    private static readonly List<NetworkObject> replicas = new();
    public static Vector3 LastServerPosition { get; private set; }
    public static Quaternion LastServerRotation { get; private set; }

    protected override void OnNetworkPostSpawn()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null && !IsServer) body.isKinematic = true;
        NetworkObject ownObject = GetComponent<NetworkObject>();
        if (ownObject != null && !replicas.Contains(ownObject)) replicas.Add(ownObject);
        IsolateAcrossManagers();
        base.OnNetworkPostSpawn();
    }

    public static void Reset()
    {
        LastServerPosition = new Vector3(float.NaN, float.NaN, float.NaN);
        LastServerRotation = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            LastServerPosition = transform.position;
            LastServerRotation = transform.rotation;
        }
        NetworkObject ownObject = GetComponent<NetworkObject>();
        replicas.Remove(ownObject);
        IsolateAcrossManagers();
        base.OnNetworkDespawn();
    }

    private static void IsolateAcrossManagers()
    {
        for (int i = replicas.Count - 1; i >= 0; i--)
            if (replicas[i] == null) replicas.RemoveAt(i);
        for (int i = 0; i < replicas.Count; i++)
        {
            NetworkObject first = replicas[i];
            if (first == null || first.NetworkManager == null) continue;
            Collider[] firstColliders = first.GetComponentsInChildren<Collider>(true);
            for (int j = i + 1; j < replicas.Count; j++)
            {
                NetworkObject second = replicas[j];
                if (second == null || second.NetworkManager == null
                    || first.NetworkManager == second.NetworkManager) continue;
                Collider[] secondColliders = second.GetComponentsInChildren<Collider>(true);
                foreach (Collider a in firstColliders)
                    foreach (Collider b in secondColliders)
                        if (a != null && b != null) Physics.IgnoreCollision(a, b, true);
            }
        }
    }
}
