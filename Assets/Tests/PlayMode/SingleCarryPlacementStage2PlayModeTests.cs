using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class SingleCarryPlacementStage2PlayModeTests : InputTestFixture
{
    private GameObject player;
    private GameObject testFloorObject;
    private GameObject heldObject;
    private ScriptableObject profile;
    private Keyboard keyboard;
    private GameObject testAimCameraObject;
    private GameObject previewSectionRoot;
    private GameObject previewSlot;
    private GameObject previewSecondSlot;
    private GameObject testVirtualCameraObject;
    private Component testVirtualCameraNoise;

    [SetUp]
    public override void Setup() => base.Setup();

    [UnityTest]
    public IEnumerator ArrowInputRotatesPreviewAndRotationResetsAfterReentry()
    {
        Type managerType = Resolve("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime");
        object manager = managerType.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (manager != null && (bool)managerType.GetProperty("IsListening").GetValue(manager))
            Assert.Ignore("This isolated preview test requires an offline player.");

        Type inputType = Resolve("PlayerInputNew, Assembly-CSharp");
        Type interactionType = Resolve("PlayerInteractionNew, Assembly-CSharp");
        Type controllerType = Resolve("PlayerSingleCarryPlacementController, Assembly-CSharp");
        Type resourceType = Resolve("BaseResourceNew, Assembly-CSharp");
        Type profileType = Resolve("BaseResourceSO, Assembly-CSharp");
        Type pickableType = Resolve("IPIckableNew, Assembly-CSharp");

        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
#else
        Assert.Ignore("This fixture loads the established player prefab through the Unity Editor AssetDatabase.");
        yield break;
#endif
        Assert.That(prefab, Is.Not.Null);
        player = UnityEngine.Object.Instantiate(prefab);
        player.name = "Stage 2 placement rotation test player";
        player.transform.position = new Vector3(0f, 100f, 0f);
        testFloorObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testFloorObject.name = "Stage 2 placement camera stability floor";
        testFloorObject.transform.position = new Vector3(0f, 99.5f, 0f);
        testFloorObject.transform.localScale = new Vector3(30f, 1f, 30f);
        Component input = player.GetComponent(inputType);
        Component interaction = player.GetComponent(interactionType);
        Assert.That(input, Is.Not.Null);
        Assert.That(interaction, Is.Not.Null);

        profile = ScriptableObject.CreateInstance(profileType);
        profileType.GetField("canBeCarried").SetValue(profile, true);
        profileType.GetField("allowMultipleCarriers").SetValue(profile, false);
        heldObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Component resource = heldObject.AddComponent(resourceType);
        resourceType.GetField("baseResourceSO", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(resource, profile);
        interactionType.GetMethod("PickUpObject", BindingFlags.Instance | BindingFlags.Public, null,
            new[] { typeof(GameObject), pickableType }, null).Invoke(interaction, new object[] { heldObject, resource });

        AddSyntheticKeyboard();
        yield return null;
        yield return null;
        Component controller = player.GetComponent(controllerType);
        Assert.That(controller, Is.Not.Null);
        testAimCameraObject = new GameObject("Stage 2 synthetic aim camera");
        Camera testAimCamera = testAimCameraObject.AddComponent<Camera>();
        testAimCameraObject.transform.position = player.transform.position;
        interactionType.GetMethod("SetAimCamera").Invoke(interaction, new object[] { testAimCamera });
        QueueKeys("F");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
        Type placementUiType = Resolve("LookingAtComponentUI, Assembly-CSharp");
        Component placementUi = player.GetComponentInChildren(placementUiType, true);
        FieldInfo infoField = placementUiType.GetField("componentInfoText", BindingFlags.Instance | BindingFlags.NonPublic);
        object placementInfo = infoField.GetValue(placementUi);
        Assert.That(placementInfo, Is.Not.Null);
        RectTransform placementHudRect = (RectTransform)placementUi.transform;
        string displayedPlacementHint = (string)placementInfo.GetType().GetProperty("text").GetValue(placementInfo);
        float preferredHintHeight = (float)placementInfo.GetType().GetProperty("preferredHeight").GetValue(placementInfo);
        Assert.That(displayedPlacementHint, Does.Contain("Ctrl + Left/Right Y"), "The existing placement HUD explains the Ctrl Y-axis chord.");
        Assert.That(preferredHintHeight, Is.LessThanOrEqualTo(placementHudRect.rect.height + 1f),
            "The existing fixed-size HUD grows only while placement is active so the controls are not clipped.");

        FieldInfo ghostField = controllerType.GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic);
        Type ghostType = Resolve("SingleCarryPlacementGhost, Assembly-CSharp");
        GameObject ghostRoot = (GameObject)ghostType.GetProperty("Root").GetValue(ghostField.GetValue(controller));
        MethodInfo aimRayMethod = interactionType.GetMethod("TryGetAimRay", new[] { Resolve("UnityEngine.Ray, UnityEngine.CoreModule").MakeByRefType(), Resolve("UnityEngine.Camera, UnityEngine.CoreModule").MakeByRefType(), typeof(Quaternion).MakeByRefType() });
        object[] rayArgs = { default(Ray), null, default(Quaternion) };
        Assert.That((bool)aimRayMethod.Invoke(interaction, rayArgs), Is.True);
        Quaternion cameraYaw = (Quaternion)rayArgs[2];
        AssertPlacementCameraNoiseRebinding(controller, player);

        // Queue a press and release before the next coroutine frame. The input callbacks must
        // preserve this tap even though LateUpdate never observes the held value.
        QueueKeys("UpArrow");
        InputSystem.Update();
        QueueKeys();
        InputSystem.Update();
        yield return null;
        Quaternion oneDegreeYaw = Quaternion.AngleAxis(1f, Vector3.right)
            * Quaternion.Euler(0f, Mathf.Repeat(Mathf.Round(cameraYaw.eulerAngles.y), 360f), 0f);
        Assert.That(Quaternion.Angle(oneDegreeYaw, ghostRoot.transform.rotation), Is.LessThan(0.1f),
            "A rotation press and release processed within one rendered frame still applies exactly one step.");

        // A single event from neutral must interpret the chord as Y, never transient Z.
        QueueKeys("LeftCtrl", "RightArrow");
        InputSystem.Update();
        object rotationState = controllerType.GetField("rotationState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        Assert.That((int)rotationState.GetType().GetProperty("WorldYDegrees").GetValue(rotationState), Is.EqualTo(1),
            "The combined keyboard state event is sampled with Ctrl active.");
        Assert.That((int)rotationState.GetType().GetProperty("WorldZDegrees").GetValue(rotationState), Is.Zero,
            "The same event must not generate a transient Z step.");
        QueueKeys();
        InputSystem.Update();
        yield return null;
        Quaternion chordCameraYaw = Quaternion.Euler(0f, Mathf.Repeat(Mathf.Round(cameraYaw.eulerAngles.y), 360f), 0f);
        Quaternion yawAndX = Quaternion.AngleAxis(1f, Vector3.right) * chordCameraYaw;
        Quaternion expectedControlRight = Quaternion.AngleAxis(1f, Vector3.up) * yawAndX;
        Assert.That(Quaternion.Angle(expectedControlRight, ghostRoot.transform.rotation), Is.LessThan(0.1f),
            "One keyboard state event containing Ctrl+Right applies Y only, in addition to the earlier X tap.");

        QueueKeys("LeftCtrl", "RightCtrl", "RightArrow");
        InputSystem.Update();
        yield return null;
        Quaternion expectedBothControls = Quaternion.AngleAxis(2f, Vector3.up) * yawAndX;
        Assert.That(Quaternion.Angle(expectedBothControls, ghostRoot.transform.rotation), Is.LessThan(0.1f),
            "Adding the other Ctrl while the arrow remains held does not double-step.");

        QueueKeys("RightCtrl", "RightArrow");
        InputSystem.Update();
        yield return null;
        Assert.That(Quaternion.Angle(expectedBothControls, ghostRoot.transform.rotation), Is.LessThan(0.1f),
            "Releasing one Ctrl while the other remains held does not switch axes.");

        QueueKeys();
        InputSystem.Update();
        yield return null;
        Assert.That(Quaternion.Angle(expectedBothControls, ghostRoot.transform.rotation), Is.LessThan(0.1f),
            "Releasing Ctrl and Right together does not produce a transient Z step; the accumulated Y offset remains.");

        QueueKeys("RightArrow");
        InputSystem.Update();
        yield return null;
        Quaternion expectedZStep = Quaternion.AngleAxis(2f, Vector3.up)
            * Quaternion.AngleAxis(1f, Vector3.right)
            * Quaternion.AngleAxis(359f, Vector3.forward) * chordCameraYaw;
        Assert.That(Quaternion.Angle(expectedZStep, ghostRoot.transform.rotation), Is.LessThan(0.1f),
            "Right without Ctrl immediately selects Z and preserves the previously accumulated Y angle.");

        QueueKeys("LeftCtrl", "RightArrow");
        InputSystem.Update();
        yield return null;
        Quaternion expectedSecondYStep = Quaternion.AngleAxis(3f, Vector3.up)
            * Quaternion.AngleAxis(1f, Vector3.right)
            * Quaternion.AngleAxis(359f, Vector3.forward) * chordCameraYaw;
        Assert.That(Quaternion.Angle(expectedSecondYStep, ghostRoot.transform.rotation), Is.LessThan(0.1f),
            "Pressing Ctrl while Right remains held switches to Y immediately and starts a fresh delay.");

        QueueKeys("RightArrow");
        InputSystem.Update();
        yield return null;
        Quaternion expectedReleasedControl = Quaternion.AngleAxis(3f, Vector3.up)
            * Quaternion.AngleAxis(1f, Vector3.right)
            * Quaternion.AngleAxis(358f, Vector3.forward) * chordCameraYaw;
        Assert.That(Quaternion.Angle(expectedReleasedControl, ghostRoot.transform.rotation), Is.LessThan(0.1f),
            "Releasing the last Ctrl while Right stays held switches to Z without erasing Y.");
        QueueKeys();
        InputSystem.Update();
        yield return null;

        QueueKeys("RightArrow", "RightCtrl");
        InputSystem.Update();
        yield return null;
        Quaternion expectedReverseBindingOrder = Quaternion.AngleAxis(4f, Vector3.up)
            * Quaternion.AngleAxis(1f, Vector3.right)
            * Quaternion.AngleAxis(358f, Vector3.forward) * chordCameraYaw;
        Assert.That(Quaternion.Angle(expectedReverseBindingOrder, ghostRoot.transform.rotation), Is.LessThan(0.1f),
            "The same keyboard chord works when arrow and modifier controls are listed in the opposite order.");
        QueueKeys();
        InputSystem.Update();
        yield return null;

        QueueKeys("Escape");
        yield return null;
        QueueKeys();
        yield return null;
        QueueKeys("F");
        yield return null;
        ghostRoot = (GameObject)ghostType.GetProperty("Root").GetValue(ghostField.GetValue(controller));
        QueueKeys();
        yield return null;

        QueueKeys("UpArrow");
        InputSystem.Update();
        double pressTime = ReadRotationAxisTimestamp(controller, "xAxis");
        float deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartupAsDouble - pressTime < 4.5 && Time.realtimeSinceStartup < deadline)
            yield return null;
        double heldSeconds = Time.realtimeSinceStartupAsDouble - pressTime;
        Assert.That(heldSeconds, Is.GreaterThanOrEqualTo(4.4), "The bounded real-time rotation interval should complete.");
        int expectedMinimumRepeatCount = 1 + Mathf.FloorToInt((float)(System.Math.Max(0d, heldSeconds - .35d - Time.unscaledDeltaTime) * 90d) + .0001f);
        int expectedMaximumRepeatCount = 1 + Mathf.FloorToInt((float)(System.Math.Max(0d, heldSeconds - .35d) * 90d) + .0001f);
        int actualRepeatCount = ReadRotationAxisRepeatCount(controller, "xAxis");
        Assert.That(actualRepeatCount, Is.InRange(expectedMinimumRepeatCount, expectedMaximumRepeatCount),
            "The repeat count follows the .35 second delay and 90 degree/second cadence, within one rendered-frame sampling interval.");
        int expectedDegrees = (1 + actualRepeatCount) % 360;
        Quaternion expectedHeldRotation = Quaternion.AngleAxis(expectedDegrees, Vector3.right)
            * Quaternion.Euler(0f, Mathf.Round(cameraYaw.eulerAngles.y), 0f);
        Assert.That(Quaternion.Angle(expectedHeldRotation, ghostRoot.transform.rotation), Is.LessThan(1f),
            "The ghost rotation matches the repeat steps applied by the production controller.");

        QueueKeys("Escape");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        QueueKeys();
        yield return null;
        QueueKeys("F");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
        ghostRoot = (GameObject)ghostType.GetProperty("Root").GetValue(ghostField.GetValue(controller));

        testAimCameraObject.transform.rotation = Quaternion.Euler(-60f, 45f, 0f);
        yield return null;
        rayArgs = new object[] { default(Ray), null, default(Quaternion) };
        Assert.That((bool)aimRayMethod.Invoke(interaction, rayArgs), Is.True);
        cameraYaw = (Quaternion)rayArgs[2];
        Quaternion roundedCameraYaw = Quaternion.Euler(0f, Mathf.Repeat(Mathf.Round(cameraYaw.eulerAngles.y), 360f), 0f);
        Assert.That(Quaternion.Angle(roundedCameraYaw, ghostRoot.transform.rotation), Is.LessThan(0.1f), "Each entry resets the object direction to integer world yaw.");
        Assert.That(Mathf.Abs(ghostRoot.transform.rotation.eulerAngles.x), Is.LessThan(0.1f), "Camera pitch must not tilt the base preview rotation.");
        Ray currentAimRay = (Ray)rayArgs[0];
        Assert.That(Vector3.Distance(ghostRoot.transform.position, currentAimRay.GetPoint(2f)), Is.LessThan(0.02f), "Changing camera pitch updates the aim ray and preview position.");

        QueueKeys("UpArrow", "LeftArrow");
        double diagonalPressTime = Time.realtimeSinceStartupAsDouble;
        deadline = Time.realtimeSinceStartup + 2f;
        while (Time.realtimeSinceStartupAsDouble - diagonalPressTime < 0.25 && Time.realtimeSinceStartup < deadline)
            yield return null;
        double diagonalHeldSeconds = Time.realtimeSinceStartupAsDouble - diagonalPressTime;
        Assert.That(diagonalHeldSeconds, Is.GreaterThanOrEqualTo(0.2), "The bounded real-time diagonal input interval should complete.");
        Quaternion expectedDiagonal = Quaternion.AngleAxis(1f, Vector3.right)
            * Quaternion.AngleAxis(1f, Vector3.forward) * roundedCameraYaw;
        Assert.That(Quaternion.Angle(expectedDiagonal, ghostRoot.transform.rotation), Is.LessThan(0.1f),
            "Simultaneous Up and Left apply one degree on each independent world axis without digital normalization.");
        QueueKeys();
        yield return null;

        SetupProjectCameraPipeline(controller, player);
        Component projectNoise = testVirtualCameraNoise;
        Assert.That(ReadNoiseAmplitude(projectNoise), Is.EqualTo(0f), "F mode mutes the authored follow-camera Perlin channel.");
        // Let the newly enabled virtual camera complete its first Brain update before taking
        // the ray baseline; subsequent frames must keep both ray geometry and ghost position fixed.
        for (int i = 0; i < 12; i++) yield return null;
        rayArgs = new object[] { default(Ray), null, default(Quaternion) };
        Assert.That((bool)aimRayMethod.Invoke(interaction, rayArgs), Is.True);
        Ray stableAimRay = (Ray)rayArgs[0];
        Component composer = player.GetComponent(Resolve("PlayerCameraFeedbackComposer, Assembly-CSharp"));
        Transform outputTarget = (Transform)composer.GetType().GetProperty("OutputTarget").GetValue(composer);
        Vector3 stableFollowPosition = outputTarget.position;
        Vector3 stablePreviewPosition = ghostRoot.transform.position;
        Quaternion stableAimRotation = testAimCameraObject.transform.rotation;
        Quaternion stablePreviewRotation = ghostRoot.transform.rotation;
        for (int i = 0; i < 8; i++) yield return null;
        rayArgs = new object[] { default(Ray), null, default(Quaternion) };
        Assert.That((bool)aimRayMethod.Invoke(interaction, rayArgs), Is.True);
        Ray settledAimRay = (Ray)rayArgs[0];
        Assert.That(Vector3.Distance(stableAimRay.origin, settledAimRay.origin), Is.LessThan(.002f),
            $"The actual Cinemachine Brain camera ray origin remains stable during F mode. start={stableAimRay.origin}, end={settledAimRay.origin}, camera={testAimCameraObject.transform.position}, followStart={stableFollowPosition}, followEnd={outputTarget.position}, player={player.transform.position}.");
        Assert.That(Vector3.Angle(stableAimRay.direction, settledAimRay.direction), Is.LessThan(.02f),
            "The actual Cinemachine Brain camera ray direction remains stable during F mode.");
        Assert.That(Vector3.Distance(stablePreviewPosition, ghostRoot.transform.position), Is.LessThan(.01f),
            "The preview position stays fixed while the actual camera pipeline runs without rotation input.");
        Assert.That(Quaternion.Angle(stableAimRotation, testAimCameraObject.transform.rotation), Is.LessThan(0.02f),
            "The actual Cinemachine Brain view remains stable while F mode suppresses camera noise.");
        Assert.That(Quaternion.Angle(stablePreviewRotation, ghostRoot.transform.rotation), Is.LessThan(0.02f),
            "The preview does not drift while the real camera pipeline runs without rotation input.");

        Type composerType = composer.GetType();
        composerType.GetMethod("SetDamageFeedback").Invoke(composer, new object[] { Vector3.right * .03f, Vector3.up });
        yield return null;
        Assert.That(outputTarget.localPosition.x, Is.EqualTo(.03f).Within(.0001f),
            "Damage feedback continues to move the composer output during placement mode.");
        composerType.GetMethod("SetDamageFeedback").Invoke(composer, new object[] { Vector3.zero, Vector3.zero });
        QueueKeys("Escape");
        yield return null;
        Assert.That(ReadNoiseAmplitude(projectNoise), Is.EqualTo(.5f), "Leaving F restores the authored Perlin amplitude.");
        Assert.That((bool)composerType.GetProperty("IsPlacementModeSuppressed").GetValue(composer), Is.False);
    }

    [UnityTest]
    public IEnumerator V1StockPlateReceivesConfiguredOffsetDuringCarryAndFPreview()
        => RunBridgeDeckPrefabOrientation("Assets/Experiments/BridgeDeckAreaV1/Prefabs/WoodenDeckAreaStockPlate.prefab");

    [UnityTest]
    public IEnumerator V1CutFragmentReceivesConfiguredOffsetDuringCarryAndFPreview()
        => RunBridgeDeckPrefabOrientation("Assets/Experiments/BridgeDeckAreaV1/Prefabs/WoodenDeckAreaCutFragment.prefab");

    private IEnumerator RunBridgeDeckPrefabOrientation(string prefabPath)
    {
#if UNITY_EDITOR
        Type interactionType = Resolve("PlayerInteractionNew, Assembly-CSharp");
        Type controllerType = Resolve("PlayerSingleCarryPlacementController, Assembly-CSharp");
        Type mountableType = Resolve("MountableBridgeComponent, Assembly-CSharp");
        Type orientationType = Resolve("CarryPlacementOrientation, Assembly-CSharp");
        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        GameObject sourcePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(sourcePrefab, Is.Not.Null, prefabPath);
        player = UnityEngine.Object.Instantiate(playerPrefab);
        player.name = "Configured bridge-area carry test player";
        player.transform.position = new Vector3(0f, 100f, 0f);
        heldObject = UnityEngine.Object.Instantiate(sourcePrefab);
        heldObject.name = "Configured bridge-area source";
        Component mountable = heldObject.GetComponent(mountableType);
        Assert.That(mountable, Is.Not.Null);
        GameObject cameraObject = testAimCameraObject = new GameObject("Configured bridge-area aim camera");
        Camera aimCamera = cameraObject.AddComponent<Camera>();
        cameraObject.transform.SetPositionAndRotation(player.transform.position, Quaternion.identity);
        Component interaction = player.GetComponent(interactionType);
        interactionType.GetMethod("SetAimCamera").Invoke(interaction, new object[] { aimCamera });
        mountableType.GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(heldObject));
        Quaternion offset = (Quaternion)orientationType.GetMethod("ResolveOffset", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { heldObject });
        Transform hold = (Transform)interactionType.GetMethod("GetPickUpHoldPositionHolder").Invoke(interaction, null);
        Assert.That(Quaternion.Angle(heldObject.transform.rotation, hold.rotation * offset), Is.LessThan(.1f),
            "The actual network-prefab source follows the hold point with its root offset during ordinary carry.");

        AddSyntheticKeyboard();
        yield return null;
        yield return null;
        QueueKeys("F");
        yield return null;
        QueueKeys();
        yield return null;
        Component controller = player.GetComponent(controllerType);
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True,
            "The actual V1 prefab is eligible for the standard F placement flow.");
        object[] aimArgs = { default(Ray), null, Quaternion.identity };
        MethodInfo getAimRay = interactionType.GetMethod("TryGetAimRay", new[]
        {
            typeof(Ray).MakeByRefType(), typeof(Camera).MakeByRefType(), typeof(Quaternion).MakeByRefType()
        });
        Assert.That((bool)getAimRay.Invoke(interaction, aimArgs), Is.True);
        object rotationState = controllerType.GetField("rotationState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        Quaternion referenceRotation = (Quaternion)rotationState.GetType().GetMethod("BuildWorldRotation")
            .Invoke(rotationState, new object[] { aimArgs[2] });
        FieldInfo ghostField = controllerType.GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic);
        Type ghostType = Resolve("SingleCarryPlacementGhost, Assembly-CSharp");
        GameObject ghostRoot = (GameObject)ghostType.GetProperty("Root").GetValue(ghostField.GetValue(controller));
        Assert.That(Quaternion.Angle(ghostRoot.transform.rotation, referenceRotation * offset), Is.LessThan(.1f),
            "F captures the same root offset and composes it once after the existing placement reference rotation.");
        QueueKeys("Escape");
        yield return null;
        QueueKeys();
        yield return null;
#else
        Assert.Ignore("The V1 fixture loads authored prefabs through Unity Editor AssetDatabase.");
        yield break;
#endif
    }

    [UnityTest]
    public IEnumerator PhysicalPanelFPreviewSeparatesFreeSpaceFromMountingAndAllowsMisalignedEPlacement()
    {
#if UNITY_EDITOR
        Type controllerType = Resolve("PlayerSingleCarryPlacementController, Assembly-CSharp");
        Type interactionType = Resolve("PlayerInteractionNew, Assembly-CSharp");
        Type sectionType = Resolve("BridgeDeckSection, Assembly-CSharp");
        Type sectionStateType = Resolve("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        Type siteType = Resolve("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        Type outlineType = Resolve("BridgeDeckPanelSlotOutline, Assembly-CSharp");

        previewSectionRoot = new GameObject("Single-carry panel placement preview section");
        Component section = previewSectionRoot.AddComponent(sectionType);
        GameObject stateRoot = new("Single-carry panel preview state");
        stateRoot.transform.SetParent(previewSectionRoot.transform, false);
        stateRoot.AddComponent(sectionStateType);
        GameObject slotPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelBridgeComponent.prefab");
        previewSlot = UnityEngine.Object.Instantiate(slotPrefab);
        Assert.That(previewSlot, Is.Not.Null);
        previewSlot.transform.SetParent(previewSectionRoot.transform, false);
        previewSlot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Component slotBridge = previewSlot.GetComponent(Resolve("BridgeComponent, Assembly-CSharp"));
        if (slotBridge != null) ((Behaviour)slotBridge).enabled = false;
        Component site = previewSlot.GetComponent(siteType);
        System.Reflection.FieldInfo slotsField = sectionType.GetField("panelSlots", BindingFlags.Instance | BindingFlags.NonPublic);
        Array slots = Array.CreateInstance(slotsField.FieldType.GetElementType(), 1);
        slots.SetValue(site, 0);
        slotsField.SetValue(section, slots);
        siteType.GetMethod("ConfigureSection", BindingFlags.Instance | BindingFlags.Public)
            .Invoke(site, new object[] { section, 0, false });
        foreach (Collider collider in previewSlot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
        heldObject = UnityEngine.Object.Instantiate(panelPrefab);
        heldObject.name = "F preview physical panel";
        heldObject.transform.position = new Vector3(10f, 0f, 0f);
        Type mountableType = Resolve("MountableBridgeComponent, Assembly-CSharp");
        Component mountable = heldObject.GetComponent(mountableType);
        mountableType.GetField("isPhysicalDeckPanel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mountable, true);
        Assert.That(heldObject.GetComponent(Resolve("BridgeDeckPanelMounting, Assembly-CSharp")), Is.Not.Null);

        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        player = UnityEngine.Object.Instantiate(playerPrefab);
        player.name = "F preview panel player";
        player.transform.position = new Vector3(0f, 100f, 0f);
        Component interaction = player.GetComponent(interactionType);
        Component controller = player.GetComponent(controllerType);
        mountableType.GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });
        testAimCameraObject = new GameObject("F preview synthetic world camera");
        Camera aimCamera = testAimCameraObject.AddComponent<Camera>();
        aimCamera.nearClipPlane = 0.3f;
        float cameraToPlacementDistance = 2f + aimCamera.nearClipPlane;
        testAimCameraObject.transform.SetPositionAndRotation(site.transform.position - site.transform.forward * cameraToPlacementDistance,
            Quaternion.LookRotation(site.transform.forward, site.transform.up));
        interactionType.GetMethod("SetAimCamera").Invoke(interaction, new object[] { aimCamera });
        controllerType.GetField("placementDistance", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, 2f);

        AddSyntheticKeyboard();
        yield return null;
        yield return null;
        QueueKeys("F");
        yield return null;
        QueueKeys();
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
        Assert.That((bool)controllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "A free-space candidate remains generically placeable.");
        Assert.That(controllerType.GetProperty("PanelMountingGuidanceLabel").GetValue(controller), Is.EqualTo("Mount: Aligned"));
        LineRenderer outline = previewSlot.GetComponentInChildren(outlineType, true).GetComponent<LineRenderer>();
        object guidance;
        GameObject arrowObject;
        AssertOutlineColor(outline, 0.18f, 0.95f, 0.25f);

        previewSecondSlot = UnityEngine.Object.Instantiate(slotPrefab);
        previewSecondSlot.transform.SetParent(previewSectionRoot.transform, false);
        previewSecondSlot.transform.SetPositionAndRotation(previewSlot.transform.position + previewSlot.transform.right * 4f,
            previewSlot.transform.rotation);
        Component secondSite = previewSecondSlot.GetComponent(siteType);
        Component secondBridge = previewSecondSlot.GetComponent(Resolve("BridgeComponent, Assembly-CSharp"));
        if (secondBridge != null) ((Behaviour)secondBridge).enabled = false;
        siteType.GetMethod("ConfigureSection", BindingFlags.Instance | BindingFlags.Public)
            .Invoke(secondSite, new object[] { section, 1, false });
        Array twoSlots = Array.CreateInstance(slotsField.FieldType.GetElementType(), 2);
        twoSlots.SetValue(site, 0);
        twoSlots.SetValue(secondSite, 1);
        slotsField.SetValue(section, twoSlots);
        foreach (Collider collider in previewSecondSlot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        testAimCameraObject.transform.SetPositionAndRotation(secondSite.transform.position - secondSite.transform.forward * cameraToPlacementDistance,
            Quaternion.LookRotation(secondSite.transform.forward, secondSite.transform.up));
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        LineRenderer secondOutline = previewSecondSlot.GetComponentInChildren(outlineType, true).GetComponent<LineRenderer>();
        Assert.That(controllerType.GetProperty("PanelMountingGuidanceLabel").GetValue(controller), Is.EqualTo("Mount: Aligned"));
        AssertOutlineColor(outline, 1f, 1f, 1f, "Switching candidate slots clears the old outline feedback.");
        AssertOutlineColor(secondOutline, 0.18f, 0.95f, 0.25f);
        testAimCameraObject.transform.SetPositionAndRotation(site.transform.position - site.transform.forward * cameraToPlacementDistance,
            Quaternion.LookRotation(site.transform.forward, site.transform.up));
        Physics.SyncTransforms();
        yield return null;
        yield return null;

        previewSlot.transform.position += previewSlot.transform.up * 0.04f;
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        Assert.That(controllerType.GetProperty("PanelMountingGuidanceLabel").GetValue(controller), Is.EqualTo("Mount: Height +4cm"));
        guidance = controllerType.GetField("panelPlacementGuidance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        arrowObject = (GameObject)guidance.GetType().GetField("arrowObject", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(guidance);
        LineRenderer arrowLine = arrowObject.GetComponent<LineRenderer>();
        Assert.That(arrowObject.activeInHierarchy, Is.True);
        Assert.That(arrowLine.GetPosition(1).y, Is.GreaterThan(arrowLine.GetPosition(0).y));
        Assert.That(Vector3.Distance(arrowLine.GetPosition(2), arrowLine.GetPosition(4)), Is.GreaterThan(0.01f),
            "The vertical correction arrowhead must remain visible when its direction is parallel to world up.");
        previewSlot.transform.position -= previewSlot.transform.up * 0.04f;
        SetRotationCounter(controller, "worldZDegrees", 6);
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        Assert.That(controllerType.GetProperty("PanelMountingGuidanceLabel").GetValue(controller), Is.EqualTo("Mount: Angle 6°"));
        Assert.That(arrowObject.activeInHierarchy, Is.False, "Orientation-only guidance does not display a translation arrow.");
        SetRotationCounter(controller, "worldZDegrees", 0);
        yield return null;
        yield return null;

        previewSlot.transform.position += previewSlot.transform.right * 0.08f;
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "Mounting misalignment must not change the free-space ghost verdict.");
        Assert.That(controllerType.GetProperty("PanelMountingGuidanceLabel").GetValue(controller), Is.EqualTo("Mount: Along +8cm"));
        AssertOutlineColor(outline, 1f, 0.55f, 0.1f);
        guidance = controllerType.GetField("panelPlacementGuidance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        arrowObject = (GameObject)guidance.GetType().GetField("arrowObject", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(guidance);
        Assert.That(arrowObject, Is.Not.Null);
        Assert.That(arrowObject.activeInHierarchy, Is.True, "A translation failure displays a short local correction arrow.");
        Assert.That(arrowObject.GetComponent<LineRenderer>().sharedMaterial, Is.SameAs(outline.sharedMaterial),
            "The local correction arrow reuses the authored outline material.");
        Assert.That(Vector3.Distance(arrowObject.GetComponent<LineRenderer>().GetPosition(0),
            arrowObject.GetComponent<LineRenderer>().GetPosition(1)), Is.LessThanOrEqualTo(0.801f),
            "The correction arrow stays short when the nearest slot is far away.");
        Assert.That(controllerType.GetProperty("CanConfirmPlacement").GetValue(controller), Is.True);
        Assert.That(controllerType.GetProperty("PlacementConfirmationHint").GetValue(controller), Is.EqualTo("E Place"));

        QueueKeys("Escape");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False,
            "Esc cancels F mode and clears panel-specific guidance.");
        Assert.That(arrowObject.activeInHierarchy, Is.False);
        AssertOutlineColor(outline, 1f, 1f, 1f);

        Type orientationType = Resolve("CarryPlacementOrientation, Assembly-CSharp");
        Component orientation = heldObject.AddComponent(orientationType);
        orientationType.GetField("offsetEulerDegrees", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(orientation, new Vector3(0f, 90f, 0f));
        QueueKeys("F");
        yield return null;
        QueueKeys();
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
        Assert.That((bool)controllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True);
        Assert.That(controllerType.GetProperty("CanConfirmPlacement").GetValue(controller), Is.True);
        Assert.That(controllerType.GetMethod("CanContinue", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, null), Is.True, "The production E path must still see the live carried object.");
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(heldObject));
        Assert.That(mountableType.GetProperty("IsPickedUp").GetValue(mountable), Is.True);
        Rigidbody heldBody = heldObject.GetComponent<Rigidbody>();
        Assert.That(heldBody, Is.Not.Null);
        Assert.That(heldBody.isKinematic, Is.True);
        Assert.That(heldBody.useGravity, Is.False);
        Assert.That(mountableType.GetMethod("CanReleaseSingleCarryPlacement").Invoke(mountable, new object[] { interaction }), Is.True,
            "The physical panel’s normal offline release prerequisites must remain satisfied after the cancel/re-enter cycle.");
        Type ghostType = Resolve("SingleCarryPlacementGhost, Assembly-CSharp");
        FieldInfo ghostField = controllerType.GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic);
        GameObject configuredGhost = (GameObject)ghostType.GetProperty("Root").GetValue(ghostField.GetValue(controller));
        Quaternion expectedConfiguredPoseRotation = configuredGhost.transform.rotation;
        controllerType.GetMethod("ConfirmPlacement", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, new object[] { null, EventArgs.Empty });
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False,
            "E continues to release a panel when generic space is clear even when mounting feedback is orange.");
        Assert.That(Quaternion.Angle(heldObject.transform.rotation, expectedConfiguredPoseRotation), Is.LessThan(.1f),
            "The offline E commit uses the exact already-composed preview pose, without applying the root offset twice.");
        Assert.That(arrowObject.activeInHierarchy, Is.False, "Placement exit cleans the correction arrow.");
        AssertOutlineColor(outline, 1f, 1f, 1f);

        mountableType.GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });
        yield return null;
        QueueKeys("F");
        yield return null;
        QueueKeys();
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
        heldObject.SetActive(false);
        yield return null;
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False,
            "Disabling the carried source exits F mode and cleans its local preview.");
        Assert.That(arrowObject.activeInHierarchy, Is.False);
        AssertOutlineColor(outline, 1f, 1f, 1f);
#else
        Assert.Ignore("This fixture loads authored prefabs through the Unity Editor AssetDatabase.");
        yield break;
#endif
    }

    [TearDown]
    public override void TearDown()
    {
        if (player != null) UnityEngine.Object.DestroyImmediate(player);
        if (testFloorObject != null) UnityEngine.Object.DestroyImmediate(testFloorObject);
        if (heldObject != null) UnityEngine.Object.DestroyImmediate(heldObject);
        if (testAimCameraObject != null) UnityEngine.Object.DestroyImmediate(testAimCameraObject);
        if (previewSectionRoot != null) UnityEngine.Object.DestroyImmediate(previewSectionRoot);
        if (previewSlot != null && previewSectionRoot == null) UnityEngine.Object.DestroyImmediate(previewSlot);
        if (previewSecondSlot != null && previewSectionRoot == null) UnityEngine.Object.DestroyImmediate(previewSecondSlot);
        if (testVirtualCameraObject != null) UnityEngine.Object.DestroyImmediate(testVirtualCameraObject);
        if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        base.TearDown();
    }

    private static void AssertOutlineColor(LineRenderer line, float red, float green, float blue, string message = null)
    {
        MaterialPropertyBlock block = new();
        line.GetPropertyBlock(block);
        Color color = block.GetColor(Shader.PropertyToID("_BaseColor"));
        Assert.That(color.r, Is.EqualTo(red).Within(0.001f), message);
        Assert.That(color.g, Is.EqualTo(green).Within(0.001f), message);
        Assert.That(color.b, Is.EqualTo(blue).Within(0.001f), message);
    }

    private void AddSyntheticKeyboard()
    {
        keyboard = InputSystem.AddDevice<Keyboard>();
    }

    private void QueueKeys(params string[] pressedKeys)
    {
        Key[] keys = pressedKeys.Select(name => (Key)Enum.Parse(typeof(Key), name)).ToArray();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }

    private static void AssertPlacementCameraNoiseRebinding(Component controller, GameObject owner)
    {
        Type composerType = Resolve("PlayerCameraFeedbackComposer, Assembly-CSharp");
        Type virtualCameraType = Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine", true);
        Type noiseType = Type.GetType("Cinemachine.CinemachineBasicMultiChannelPerlin, Cinemachine", true);
        Component composer = owner.GetComponent(composerType);
        Transform follow = (Transform)composerType.GetProperty("OutputTarget").GetValue(composer);

        Component firstNoise = CreateCameraNoise("Placement camera A", virtualCameraType, noiseType, follow, .5f, owner.transform);
        Component secondNoise = CreateCameraNoise("Placement camera B", virtualCameraType, noiseType, follow, 0f, owner.transform);
        Component firstCamera = owner.transform.Find("Placement camera A").GetComponent(virtualCameraType);
        Assert.That(virtualCameraType.GetProperty("Follow").GetValue(firstCamera), Is.EqualTo(follow));
        MethodInfo bindingChanged = controller.GetType().GetMethod("OnLocalVirtualCameraBindingChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        bindingChanged.Invoke(controller, new[] { firstCamera });
        object mutedNoise = controller.GetType().GetField("mutedCameraNoise", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        bool isLocal = (bool)controller.GetType().GetMethod("IsLocalPlayer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
        Assert.That(ReadNoiseAmplitude(firstNoise), Is.EqualTo(0f),
            $"The local virtual camera noise is muted while F is active. active={controller.GetType().GetProperty("IsActive").GetValue(controller)}, local={isLocal}, muted={mutedNoise}, expected={firstNoise}, follow={virtualCameraType.GetProperty("Follow").GetValue(firstCamera)}, target={follow}");
        Type brainType = Type.GetType("Cinemachine.CinemachineBrain, Cinemachine", true);
        GameObject unrelatedBrainObject = new("Unrelated camera brain");
        unrelatedBrainObject.transform.SetParent(owner.transform, false);
        unrelatedBrainObject.AddComponent<Camera>();
        Component unrelatedBrain = unrelatedBrainObject.AddComponent(brainType);
        controller.GetType().GetMethod("CinemachineCameraUpdated", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, new[] { unrelatedBrain });
        Assert.That(ReadNoiseAmplitude(firstNoise), Is.EqualTo(0f),
            "A global camera-updated callback for another Brain cannot restore the local camera's mute.");
        bindingChanged.Invoke(controller, new[] { owner.transform.Find("Placement camera B").GetComponent(virtualCameraType) });
        Assert.That(ReadNoiseAmplitude(firstNoise), Is.EqualTo(.5f), "Rebinding restores the previously active camera's exact amplitude.");
        Assert.That(ReadNoiseAmplitude(secondNoise), Is.EqualTo(0f), "A camera originally configured with zero remains zero while muted.");

        Component thirdNoise = CreateCameraNoise("Placement camera C", virtualCameraType, noiseType, follow, .37f, owner.transform);
        bindingChanged.Invoke(controller, new[] { owner.transform.Find("Placement camera C").GetComponent(virtualCameraType) });
        Assert.That(ReadNoiseAmplitude(secondNoise), Is.EqualTo(0f));
        Assert.That(ReadNoiseAmplitude(thirdNoise), Is.EqualTo(0f));
        bindingChanged.Invoke(controller, new object[] { null });
        Assert.That(ReadNoiseAmplitude(thirdNoise), Is.EqualTo(.37f), "Leaving/rebinding restores the exact original amplitude.");
    }

    private static Component CreateCameraNoise(string name, Type virtualCameraType, Type noiseType, Transform follow, float amplitude,
        Transform ownerRoot)
    {
        GameObject cameraObject = new(name);
        cameraObject.transform.SetParent(ownerRoot, false);
        Component virtualCamera = cameraObject.AddComponent(virtualCameraType);
        virtualCameraType.GetProperty("Follow").SetValue(virtualCamera, follow);
        MethodInfo addNoise = virtualCameraType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(method => method.Name == "AddCinemachineComponent" && method.IsGenericMethodDefinition)
            .MakeGenericMethod(noiseType);
        Component noise = (Component)addNoise.Invoke(virtualCamera, null);
        noiseType.GetField("m_AmplitudeGain").SetValue(noise, amplitude);
        return noise;
    }

    private static float ReadNoiseAmplitude(Component noise)
        => (float)noise.GetType().GetField("m_AmplitudeGain").GetValue(noise);

    private void SetupProjectCameraPipeline(Component controller, GameObject owner)
    {
#if UNITY_EDITOR
        Type virtualCameraType = Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine", true);
        Type brainType = Type.GetType("Cinemachine.CinemachineBrain, Cinemachine", true);
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/StarterAssets/FirstPersonController/Prefabs/PlayerFollowCamera.prefab");
        Assert.That(prefab, Is.Not.Null, "The integration check uses the project's authored follow camera and noise profile.");
        testVirtualCameraObject = UnityEngine.Object.Instantiate(prefab);
        testVirtualCameraObject.name = "Placement test authored follow camera";
        Component virtualCamera = testVirtualCameraObject.GetComponent(virtualCameraType);
        Component brain = testAimCameraObject.GetComponent(brainType);
        if (brain == null) brain = testAimCameraObject.AddComponent(brainType);
        Component composer = owner.GetComponent(Resolve("PlayerCameraFeedbackComposer, Assembly-CSharp"));
        Transform follow = (Transform)composer.GetType().GetProperty("OutputTarget").GetValue(composer);
        virtualCameraType.GetProperty("Follow").SetValue(virtualCamera, follow);
        virtualCameraType.GetProperty("Priority").SetValue(virtualCamera, 50);
        Type noiseType = Type.GetType("Cinemachine.CinemachineBasicMultiChannelPerlin, Cinemachine", true);
        testVirtualCameraNoise = (Component)virtualCameraType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(method => method.Name == "GetCinemachineComponent" && method.IsGenericMethodDefinition)
            .MakeGenericMethod(noiseType).Invoke(virtualCamera, null);
        Assert.That(testVirtualCameraNoise, Is.Not.Null);
        Assert.That(ReadNoiseAmplitude(testVirtualCameraNoise), Is.EqualTo(.5f).Within(.0001f));
        controller.GetType().GetMethod("OnLocalVirtualCameraBindingChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, new[] { virtualCamera });
#else
        Assert.Ignore("The authored Cinemachine camera prefab is loaded through the Unity Editor AssetDatabase.");
#endif
    }

    private static Type Resolve(string name) => Type.GetType(name);

    private static void SetRotationCounter(Component controller, string counterName, int value)
    {
        object state = controller.GetType().GetField("rotationState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        state.GetType().GetField(counterName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, value);
    }

    private static double ReadRotationAxisTimestamp(Component controller, string axisName)
    {
        object state = controller.GetType().GetField("rotationState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        object axis = state.GetType().GetField(axisName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
        return (double)axis.GetType().GetField("PressedAt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(axis);
    }

    private static int ReadRotationCounter(Component controller, string counterName)
    {
        object state = controller.GetType().GetField("rotationState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        return (int)state.GetType().GetField(counterName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
    }

    private static int ReadRotationAxisRepeatCount(Component controller, string axisName)
    {
        object state = controller.GetType().GetField("rotationState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        object axis = state.GetType().GetField(axisName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
        return (int)axis.GetType().GetField("RepeatsApplied", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(axis);
    }
}
