using System;
using System.Collections;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class SingleCarryPlacementStage4PlayModeTests : InputTestFixture
{
    private GameObject player;
    private GameObject item;
    private GameObject aimCameraObject;
    private GameObject blocker;
    private GameObject missingBackendItem;
    private GameObject missingPhysicsItem;
    private Camera[] savedAimFailureCameras;
    private bool[] savedAimFailureCameraStates;
    private Component noAimInteraction;
    private Transform savedInteractionOrigin;
    private ScriptableObject resourceProfile;
    private ScriptableObject mountableProfile;
    private Keyboard keyboard;
    private NetworkManager startedHost;
    private GameObject temporaryNetworkRoot;
    private UnityTransport hostTransportToRestore;
    private UnityTransport.ConnectionAddressData previousConnectionData;
    private GameObject previousPlayerPrefab;
    private bool restoreHostConfiguration;

    [SetUp]
    public override void Setup() => base.Setup();

    [UnityTest]
    public IEnumerator ConfirmReleasesBucketAtDisplayedPoseWithContentsAndNoDropImpulse_ThenAllowsRepickup()
    {
        if (IsNetworkListening()) Assert.Ignore("This release integration test requires an offline session.");
        yield return CreateLocalPlayerAndKeyboard();

        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/PortableBucket.prefab");
#else
        Assert.Ignore("This fixture loads the established bucket prefab through the Unity Editor AssetDatabase.");
        yield break;
#endif
        Assert.That(prefab, Is.Not.Null, "The release path must preserve the real portable bucket and its visuals/contents.");
        item = UnityEngine.Object.Instantiate(prefab);
        item.name = "Stage 4 held bucket";
        item.transform.position = player.transform.position + Vector3.right * 4f;
        Component container = item.GetComponent(TypeByName("PortableSubstanceContainer, Assembly-CSharp"));
        Component resource = item.GetComponent(TypeByName("BaseResourceNew, Assembly-CSharp"));
        Assert.That(container, Is.Not.Null);
        Assert.That(resource, Is.Not.Null);
        int unitsBefore;
        object substanceBefore;
#if UNITY_EDITOR
        UnityEngine.Object water = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
            "Assets/ScriptableObjectAssets/New/Substances/Water.asset");
        MethodInfo addUnits = container.GetType().GetMethod("TryAddUnits", new[] { TypeByName("ContainerSubstanceSO, Assembly-CSharp"), typeof(int) });
        Assert.That(addUnits.Invoke(container, new[] { water, (object)2 }), Is.EqualTo(true), "The bucket should contain real substance before release.");
        unitsBefore = (int)container.GetType().GetProperty("CurrentUnits").GetValue(container);
        substanceBefore = container.GetType().GetProperty("CurrentSubstance").GetValue(container);
#else
        Assert.Ignore("This fixture populates the established bucket prefab through Editor assets.");
        yield break;
#endif
        Rigidbody body = item.GetComponent<Rigidbody>();
        Assert.That(body, Is.Not.Null);

        Type interactionType = TypeByName("PlayerInteractionNew, Assembly-CSharp");
        Component interaction = player.GetComponent(interactionType);
        resource.GetType().GetMethod("PickedUp").Invoke(resource, new object[] { player.transform });
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(item));
        Assert.That(body.isKinematic, Is.True);
        Assert.That(body.useGravity, Is.False);

        Component controller = player.GetComponent(TypeByName("PlayerSingleCarryPlacementController, Assembly-CSharp"));
        QueueKeys("F");
        yield return null;
        QueueKeys();
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.True);
        Assert.That((bool)controller.GetType().GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True);
        Transform ghost = GetGhostRoot(controller).transform;
        QueueKeys("UpArrow");
        float rotationElapsed = 0f;
        float rotationDeadline = Time.realtimeSinceStartup + 2f;
        while (rotationElapsed < 0.2f && Time.realtimeSinceStartup < rotationDeadline)
        {
            yield return null;
            rotationElapsed += Time.deltaTime;
        }
        Assert.That(rotationElapsed, Is.GreaterThanOrEqualTo(0.19f), "The bounded arrow input interval should complete.");
        QueueKeys();
        yield return null;
        Assert.That(Quaternion.Angle(Quaternion.identity, ghost.rotation), Is.GreaterThan(1f),
            "Arrow input must produce an actual non-identity placement rotation before confirmation.");
        Vector3 shownPosition = ghost.position;
        Quaternion shownRotation = ghost.rotation;

        QueueKeys("E");
        InputSystem.Update();
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.Null,
            "E commits a valid offline placement instead of invoking ordinary drop.");
        Assert.That(controller.GetType().GetProperty("IsActive").GetValue(controller), Is.EqualTo(false));
        Assert.That(item.transform.position, Is.EqualTo(shownPosition));
        Assert.That(Quaternion.Angle(item.transform.rotation, shownRotation), Is.LessThan(0.01f));
        Assert.That(body.isKinematic, Is.False);
        Assert.That(body.useGravity, Is.True);
        Assert.That(body.linearVelocity.x, Is.EqualTo(0f).Within(0.001f));
        Assert.That(body.linearVelocity.y, Is.EqualTo(0f).Within(0.001f));
        Assert.That(body.linearVelocity.z, Is.EqualTo(0f).Within(0.001f));
        Assert.That(body.angularVelocity.sqrMagnitude, Is.LessThan(0.0001f));
        Assert.That((int)container.GetType().GetProperty("CurrentUnits").GetValue(container), Is.EqualTo(unitsBefore));
        Assert.That(container.GetType().GetProperty("CurrentSubstance").GetValue(container), Is.SameAs(substanceBefore));
        Assert.That(item.GetComponentInChildren<Renderer>(true).forceRenderingOff, Is.False,
            "The original bucket renderers return when placement mode ends.");

        Vector3 releasedPosition = item.transform.position;
        yield return new WaitForSeconds(0.1f);
        Assert.That(item.transform.position.y, Is.LessThan(releasedPosition.y), "Ordinary gravity resumes after placement.");
        Assert.That(Vector2.Distance(new Vector2(item.transform.position.x, item.transform.position.z),
                new Vector2(shownPosition.x, shownPosition.z)), Is.LessThan(0.01f),
            "The release applies no ordinary-drop horizontal impulse.");
        Assert.That(Quaternion.Angle(item.transform.rotation, shownRotation), Is.LessThan(0.5f),
            "The release applies no ordinary-drop spin.");

        resource.GetType().GetMethod("PickedUp").Invoke(resource, new object[] { player.transform });
        QueueKeys();
        yield return null;
        QueueKeys("F");
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.True,
            "A successfully placed bucket can be picked up and enter placement again.");
        QueueKeys();
        yield return null;
        QueueKeys("Escape");
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.False);
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(item),
            "Cancelling a later placement keeps the held object.");
    }

    [UnityTest]
    public IEnumerator SingleCarryMountableUsesTheSamePreciseLocalReleasePath()
    {
        if (IsNetworkListening()) Assert.Ignore("This release integration test requires an offline session.");
        yield return CreateLocalPlayerAndKeyboard();
        item = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        item.name = "Stage 4 single carry mountable";
        item.transform.position = player.transform.position + Vector3.right * 4f;
        Rigidbody body = item.AddComponent<Rigidbody>();
        body.linearVelocity = new Vector3(2f, 3f, -4f);
        body.angularVelocity = new Vector3(1f, 2f, 3f);
        Component mountable = item.AddComponent(TypeByName("MountableBridgeComponent, Assembly-CSharp"));
        mountableProfile = ScriptableObject.CreateInstance(TypeByName("MountableBridgeComponentSO, Assembly-CSharp"));
        mountable.GetType().GetField("mountableBridgeComponentSO", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mountable, mountableProfile);
        mountable.GetType().GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });

        Component interaction = player.GetComponent(TypeByName("PlayerInteractionNew, Assembly-CSharp"));
        Component controller = player.GetComponent(TypeByName("PlayerSingleCarryPlacementController, Assembly-CSharp"));
        QueueKeys("F");
        yield return null;
        QueueKeys();
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.True);
        Assert.That((bool)controller.GetType().GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True);
        Vector3 position = GetGhostRoot(controller).transform.position;
        Quaternion rotation = GetGhostRoot(controller).transform.rotation;

        QueueKeys("E");
        InputSystem.Update();
        Assert.That(interaction.GetType().GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.Null);
        Assert.That(item.transform.position, Is.EqualTo(position));
        Assert.That(Quaternion.Angle(item.transform.rotation, rotation), Is.LessThan(0.01f));
        Assert.That(body.isKinematic, Is.False);
        Assert.That(body.useGravity, Is.True);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ListeningHostConsumesEAndKeepsPlacementUnavailable()
    {
        if (IsNetworkListening()) Assert.Ignore("A NetworkManager is already listening before this isolated host-gate test.");
        keyboard = InputSystem.AddDevice<Keyboard>();
#if UNITY_EDITOR
        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
#else
        Assert.Ignore("This fixture loads the player network prefab through the Unity Editor AssetDatabase.");
        yield break;
#endif
        Assert.That(playerPrefab, Is.Not.Null);
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            temporaryNetworkRoot = new GameObject("Stage 4 temporary listening host");
            UnityTransport transport = temporaryNetworkRoot.AddComponent<UnityTransport>();
            manager = temporaryNetworkRoot.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = playerPrefab };
        }
        UnityTransport hostTransport = manager.NetworkConfig.NetworkTransport as UnityTransport;
        Assert.That(hostTransport, Is.Not.Null, "Host gate test requires the established UnityTransport.");
        previousConnectionData = hostTransport.ConnectionData;
        previousPlayerPrefab = manager.NetworkConfig.PlayerPrefab;
        restoreHostConfiguration = true;
        hostTransportToRestore = hostTransport;
        if (manager.NetworkConfig.PlayerPrefab == null) manager.NetworkConfig.PlayerPrefab = playerPrefab;
        hostTransport.SetConnectionData("127.0.0.1", ReserveFreeUdpPort(), "127.0.0.1");
        startedHost = manager;
        Assert.That(manager.StartHost(), Is.True, "A local host should start to exercise the listening-session gate.");
        Assert.That(manager.IsListening, Is.True);

        float spawnDeadline = Time.realtimeSinceStartup + 10f;
        while ((manager.LocalClient == null || manager.LocalClient.PlayerObject == null)
            && Time.realtimeSinceStartup < spawnDeadline)
            yield return null;
        Assert.That(manager.LocalClient?.PlayerObject, Is.Not.Null, "The host owner should receive its actual spawned player prefab.");
        player = manager.LocalClient.PlayerObject.gameObject;
        Type inputType = TypeByName("PlayerInputNew, Assembly-CSharp");
        Type interactionType = TypeByName("PlayerInteractionNew, Assembly-CSharp");
        Component input = player.GetComponent(inputType);
        Component interaction = player.GetComponent(interactionType);
        Component controller = player.GetComponent(TypeByName("PlayerSingleCarryPlacementController, Assembly-CSharp"));
        Assert.That(input, Is.Not.Null);
        Assert.That(interaction, Is.Not.Null);
        Assert.That(controller, Is.Not.Null);
        CreateResourceItem();
        Component resource = item.GetComponent(TypeByName("BaseResourceNew, Assembly-CSharp"));
        resource.GetType().GetMethod("PickedUp").Invoke(resource, new object[] { player.transform });
        QueueKeys("F");
        yield return null;
        QueueKeys();
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.True);
        Assert.That((bool)controller.GetType().GetProperty("CanConfirmPlacement").GetValue(controller), Is.False);
        Assert.That(controller.GetType().GetProperty("PlacementConfirmationHint").GetValue(controller).ToString(),
            Does.Contain("Unavailable in multiplayer"));

        QueueKeys("E");
        InputSystem.Update();
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(item),
            "The local host must not use the Stage 4 offline placement path.");
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.True,
            "A refused multiplayer confirmation does not optimistically release or exit placement.");

        manager.Shutdown();
        yield return null;
        RestoreNetworkSettings();
        startedHost = null;
    }

    [UnityTest]
    public IEnumerator ConfirmationRevalidatesUnsyncedNewBlockerAndLeavesPreviewEditable()
    {
        if (IsNetworkListening()) Assert.Ignore("This release integration test requires an offline session.");
        yield return CreateLocalPlayerAndKeyboard();
        CreateResourceItem();
        Component interaction = player.GetComponent(TypeByName("PlayerInteractionNew, Assembly-CSharp"));
        item.GetComponent(TypeByName("BaseResourceNew, Assembly-CSharp")).GetType()
            .GetMethod("PickedUp").Invoke(item.GetComponent(TypeByName("BaseResourceNew, Assembly-CSharp")), new object[] { player.transform });
        Component controller = player.GetComponent(TypeByName("PlayerSingleCarryPlacementController, Assembly-CSharp"));
        QueueKeys("F");
        yield return null;
        QueueKeys();
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True);
        Vector3 displayedPosition = GetGhostRoot(controller).transform.position;

        blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = "Stage 4 blocker moved after preview validation";
        blocker.transform.position = displayedPosition;
        blocker.transform.localScale = Vector3.one * 0.25f;
        // Deliberately do not call Physics.SyncTransforms here: the confirmation path owns synchronization.
        QueueKeys("E");
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.True);
        Assert.That((bool)controller.GetType().GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.False);
        Assert.That(interaction.GetType().GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(item));
        Assert.That(GetGhostRoot(controller), Is.Not.Null, "A refusal retains the editable preview.");

        UnityEngine.Object.Destroy(blocker);
        blocker = null;
        QueueKeys();
        yield return null;
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True);

        // Simulate loss of both available aim sources after a valid preview was rendered. E must
        // not reuse that stale pose while the camera/origin is temporarily unavailable.
        noAimInteraction = interaction;
        FieldInfo originField = interaction.GetType().GetField("interactionOrigin", BindingFlags.Instance | BindingFlags.NonPublic);
        savedInteractionOrigin = (Transform)originField.GetValue(interaction);
        savedAimFailureCameras = Camera.allCameras;
        savedAimFailureCameraStates = Array.ConvertAll(savedAimFailureCameras, camera => camera.enabled);
        foreach (Camera camera in savedAimFailureCameras) camera.enabled = false;
        originField.SetValue(interaction, null);
        interaction.GetType().GetMethod("SetAimCamera").Invoke(interaction, new object[] { null });
        yield return null;
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.False);
        QueueKeys("E");
        yield return null;
        Assert.That(interaction.GetType().GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(item),
            "Confirmation is refused when there is no current aim ray, even if the prior preview was green.");
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.True);
        QueueKeys();
        yield return null;
        originField.SetValue(interaction, savedInteractionOrigin);
        for (int i = 0; i < savedAimFailureCameras.Length; i++) savedAimFailureCameras[i].enabled = savedAimFailureCameraStates[i];
        interaction.GetType().GetMethod("SetAimCamera").Invoke(interaction,
            new object[] { aimCameraObject != null ? aimCameraObject.GetComponent<Camera>() : null });
        savedAimFailureCameras = null;
        savedAimFailureCameraStates = null;
        noAimInteraction = null;
        savedInteractionOrigin = null;
        yield return null;
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "Restoring the aim source resumes preview validation.");
        QueueKeys("E");
        yield return null;
        Assert.That(interaction.GetType().GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.Null);
    }

    [UnityTest]
    public IEnumerator WrongHolderAndMissingPlaceableCannotCommit_ButOrdinaryDropStillUsesItsNudge()
    {
        if (IsNetworkListening()) Assert.Ignore("This release integration test requires an offline session.");
        yield return CreateLocalPlayerAndKeyboard();
        CreateResourceItem();
        Type interactionType = TypeByName("PlayerInteractionNew, Assembly-CSharp");
        Component interaction = player.GetComponent(interactionType);
        Component resource = item.GetComponent(TypeByName("BaseResourceNew, Assembly-CSharp"));
        resource.GetType().GetMethod("PickedUp").Invoke(resource, new object[] { player.transform });

        GameObject wrongHolder = UnityEngine.Object.Instantiate(player);
        wrongHolder.name = "Stage 4 wrong holder";
        Component wrongInteraction = wrongHolder.GetComponent(interactionType);
        Type placeableType = TypeByName("ISingleCarryPlaceable, Assembly-CSharp");
        Assert.That((bool)placeableType.GetMethod("CanReleaseSingleCarryPlacement").Invoke(resource, new[] { wrongInteraction }), Is.False,
            "The backend verifies the exact holder rather than accepting a different player's request.");
        Assert.That((bool)interactionType.GetMethod("TryReleaseSingleCarryPlacement").Invoke(wrongInteraction,
            new object[] { item, resource, new Vector3(3f, 101f, 4f), Quaternion.Euler(0f, 45f, 0f) }), Is.False,
            "A non-holder cannot commit a release even when it supplies the right source/backend.");
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(item));
        UnityEngine.Object.Destroy(wrongHolder);
        interactionType.GetMethod("ForceReleasePickedUpObject").Invoke(interaction, new object[] { item });
        resource.GetType().GetMethod("DroppedDown").Invoke(resource, null);
        item.transform.SetParent(null);
        item.SetActive(false);
        UnityEngine.Object.Destroy(item);
        item = null;
        yield return null;
        Physics.SyncTransforms();
        QueueKeys();
        yield return null;
        Component controller = player.GetComponent(TypeByName("PlayerSingleCarryPlacementController, Assembly-CSharp"));
        missingBackendItem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        missingBackendItem.AddComponent<Rigidbody>();
        Type pickableType = TypeByName("IPIckableNew, Assembly-CSharp");
        MethodInfo pickUp = interactionType.GetMethod("PickUpObject", BindingFlags.Instance | BindingFlags.Public, null,
            new[] { typeof(GameObject), pickableType }, null);
        pickUp.Invoke(interaction, new object[] { missingBackendItem, null });
        QueueKeys("F");
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.False,
            "A held object with no eligible single-carry backend cannot enter placement.");
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(missingBackendItem));

        // Restore a valid held item, then verify the existing ordinary drop path remains unchanged.
        interactionType.GetMethod("ForceReleasePickedUpObject").Invoke(interaction, new object[] { missingBackendItem });
        UnityEngine.Object.Destroy(missingBackendItem);
        missingBackendItem = null;
        missingPhysicsItem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Component resourceWithoutBody = missingPhysicsItem.AddComponent(TypeByName("BaseResourceNew, Assembly-CSharp"));
        resourceWithoutBody.GetType().GetField("baseResourceSO", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(resourceWithoutBody, resourceProfile);
        resourceWithoutBody.GetType().GetMethod("PickedUp").Invoke(resourceWithoutBody, new object[] { player.transform });
        QueueKeys();
        yield return null;
        QueueKeys("F");
        yield return null;
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.True);
        bool placementIsValid = (bool)controller.GetType().GetProperty("IsCurrentPlacementValid").GetValue(controller);
        Assert.That(placementIsValid, Is.True, DescribePlacementValidation(controller));
        QueueKeys("E");
        InputSystem.Update();
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(missingPhysicsItem),
            "A missing rigidbody causes a safe refusal while preserving the held object.");
        Assert.That((bool)controller.GetType().GetProperty("IsActive").GetValue(controller), Is.True);
        QueueKeys();
        yield return null;
        QueueKeys("Escape");
        yield return null;
        interactionType.GetMethod("ForceReleasePickedUpObject").Invoke(interaction, new object[] { missingPhysicsItem });
        UnityEngine.Object.Destroy(missingPhysicsItem);
        missingPhysicsItem = null;

        CreateResourceItem();
        Component validResource = item.GetComponent(TypeByName("BaseResourceNew, Assembly-CSharp"));
        validResource.GetType().GetMethod("PickedUp").Invoke(validResource, new object[] { player.transform });
        Vector3 playerPosition = player.transform.position;
        Assert.That((bool)interactionType.GetMethod("DropObject").Invoke(interaction, null), Is.True);
        Vector3 ordinaryDropPosition = playerPosition + player.transform.forward + Vector3.up * 0.5f;
        Assert.That(item.transform.position, Is.EqualTo(ordinaryDropPosition));
        yield return new WaitForFixedUpdate();
        yield return null;
        Assert.That(Vector3.Dot(item.transform.position - ordinaryDropPosition, player.transform.forward), Is.GreaterThan(0f),
            "The ordinary drop advances after the next physics step.");
        Assert.That(item.GetComponent<Rigidbody>().linearVelocity.magnitude, Is.GreaterThan(0f),
            "Ordinary drop retains its established impulse behavior.");
    }

    [TearDown]
    public override void TearDown()
    {
        if (noAimInteraction != null && savedInteractionOrigin != null)
            noAimInteraction.GetType().GetField("interactionOrigin", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(noAimInteraction, savedInteractionOrigin);
        if (savedAimFailureCameras != null && savedAimFailureCameraStates != null)
            for (int i = 0; i < savedAimFailureCameras.Length; i++)
                if (savedAimFailureCameras[i] != null) savedAimFailureCameras[i].enabled = savedAimFailureCameraStates[i];
        if (startedHost != null && startedHost.IsListening) startedHost.Shutdown();
        RestoreNetworkSettings();
        if (player != null) UnityEngine.Object.DestroyImmediate(player);
        if (temporaryNetworkRoot != null) UnityEngine.Object.DestroyImmediate(temporaryNetworkRoot);
        if (item != null) UnityEngine.Object.DestroyImmediate(item);
        if (aimCameraObject != null) UnityEngine.Object.DestroyImmediate(aimCameraObject);
        if (blocker != null) UnityEngine.Object.DestroyImmediate(blocker);
        if (missingBackendItem != null) UnityEngine.Object.DestroyImmediate(missingBackendItem);
        if (missingPhysicsItem != null) UnityEngine.Object.DestroyImmediate(missingPhysicsItem);
        if (resourceProfile != null) UnityEngine.Object.DestroyImmediate(resourceProfile);
        if (mountableProfile != null) UnityEngine.Object.DestroyImmediate(mountableProfile);
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        base.TearDown();
    }

    private IEnumerator CreateLocalPlayerAndKeyboard()
    {
        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
#else
        Assert.Ignore("This fixture loads the established player prefab through the Unity Editor AssetDatabase.");
        yield break;
#endif
        Assert.That(prefab, Is.Not.Null);
        player = UnityEngine.Object.Instantiate(prefab);
        player.name = "Stage 4 placement integration player";
        player.transform.position = new Vector3(0f, 100f, 0f);
        keyboard = InputSystem.AddDevice<Keyboard>();
        aimCameraObject = new GameObject("Stage 4 aim camera");
        Camera aimCamera = aimCameraObject.AddComponent<Camera>();
        aimCameraObject.transform.position = player.transform.position;
        player.GetComponent(TypeByName("PlayerInteractionNew, Assembly-CSharp")).GetType()
            .GetMethod("SetAimCamera").Invoke(player.GetComponent(TypeByName("PlayerInteractionNew, Assembly-CSharp")), new object[] { aimCamera });
        yield return null;
        yield return null;
    }

    private void CreateResourceItem()
    {
        if (resourceProfile == null)
        {
            resourceProfile = ScriptableObject.CreateInstance(TypeByName("BaseResourceSO, Assembly-CSharp"));
            resourceProfile.GetType().GetField("canBeCarried").SetValue(resourceProfile, true);
            resourceProfile.GetType().GetField("allowMultipleCarriers").SetValue(resourceProfile, false);
        }
        item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = "Stage 4 held resource";
        item.AddComponent<Rigidbody>();
        Component resource = item.AddComponent(TypeByName("BaseResourceNew, Assembly-CSharp"));
        resource.GetType().GetField("baseResourceSO", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(resource, resourceProfile);
    }

    private void QueueKeys(params string[] pressedKeys)
    {
        Key[] keys = Array.ConvertAll(pressedKeys, name => (Key)Enum.Parse(typeof(Key), name));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }

    private static GameObject GetGhostRoot(Component controller)
    {
        object ghost = controller.GetType().GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        return (GameObject)ghost.GetType().GetProperty("Root").GetValue(ghost);
    }

    private static string DescribePlacementValidation(Component controller)
    {
        object result = controller.GetType().GetProperty("LastValidation").GetValue(controller);
        Type resultType = result.GetType();
        Collider blocker = (Collider)resultType.GetProperty("BlockingCollider").GetValue(result);
        float depth = (float)resultType.GetProperty("PenetrationDepth").GetValue(result);
        return $"LastValidation={resultType.GetProperty("Status").GetValue(result)}; "
            + $"blocker={(blocker != null ? blocker.name : "<none>")}; depth={depth:R}";
    }

    private static bool IsNetworkListening()
    {
        Type managerType = TypeByName("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime");
        object manager = managerType.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return manager != null && (bool)managerType.GetProperty("IsListening").GetValue(manager);
    }

    private static ushort ReserveFreeUdpPort()
    {
        using UdpClient listener = new UdpClient(0);
        return (ushort)((System.Net.IPEndPoint)listener.Client.LocalEndPoint).Port;
    }

    private void RestoreNetworkSettings()
    {
        if (!restoreHostConfiguration) return;
        if (hostTransportToRestore != null) hostTransportToRestore.ConnectionData = previousConnectionData;
        if (startedHost != null && startedHost.NetworkConfig != null)
            startedHost.NetworkConfig.PlayerPrefab = previousPlayerPrefab;
        restoreHostConfiguration = false;
        hostTransportToRestore = null;
    }

    private static Type TypeByName(string name) => Type.GetType(name);
}
