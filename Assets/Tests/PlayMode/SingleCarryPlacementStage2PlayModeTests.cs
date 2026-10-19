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
    private GameObject heldObject;
    private ScriptableObject profile;
    private Keyboard keyboard;
    private GameObject testAimCameraObject;
    private GameObject previewSectionRoot;
    private GameObject previewSlot;
    private GameObject previewSecondSlot;

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

        FieldInfo ghostField = controllerType.GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic);
        Type ghostType = Resolve("SingleCarryPlacementGhost, Assembly-CSharp");
        GameObject ghostRoot = (GameObject)ghostType.GetProperty("Root").GetValue(ghostField.GetValue(controller));
        MethodInfo aimRayMethod = interactionType.GetMethod("TryGetAimRay", new[] { Resolve("UnityEngine.Ray, UnityEngine.CoreModule").MakeByRefType(), Resolve("UnityEngine.Camera, UnityEngine.CoreModule").MakeByRefType(), typeof(Quaternion).MakeByRefType() });
        object[] rayArgs = { default(Ray), null, default(Quaternion) };
        Assert.That((bool)aimRayMethod.Invoke(interaction, rayArgs), Is.True);
        Quaternion cameraYaw = (Quaternion)rayArgs[2];

        QueueKeys("F", "UpArrow");
        float elapsed = 0f;
        float deadline = Time.realtimeSinceStartup + 8f;
        while (elapsed < 4.5f && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }
        Assert.That(elapsed, Is.GreaterThanOrEqualTo(4.4f), "The bounded game-time rotation interval should complete.");
        Quaternion localXRotation = Quaternion.Inverse(cameraYaw) * ghostRoot.transform.rotation;
        float expectedDegrees = elapsed * 90f;
        Assert.That(Quaternion.Angle(Quaternion.AngleAxis(expectedDegrees, Vector3.right), localXRotation), Is.LessThan(10f),
            "Holding Up must rotate about local X at 90 degrees per second and continue through a full turn without clamping.");

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
        Assert.That(Quaternion.Angle(cameraYaw, ghostRoot.transform.rotation), Is.LessThan(0.1f), "Each entry resets local rotation to camera yaw.");
        Assert.That(Mathf.Abs(ghostRoot.transform.rotation.eulerAngles.x), Is.LessThan(0.1f), "Camera pitch must not tilt the base preview rotation.");
        Ray currentAimRay = (Ray)rayArgs[0];
        Assert.That(Vector3.Distance(ghostRoot.transform.position, currentAimRay.GetPoint(2f)), Is.LessThan(0.02f), "Changing camera pitch updates the aim ray and preview position.");

        QueueKeys("UpArrow", "LeftArrow");
        elapsed = 0f;
        deadline = Time.realtimeSinceStartup + 2f;
        while (elapsed < 0.25f && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }
        Assert.That(elapsed, Is.GreaterThanOrEqualTo(0.2f), "The bounded game-time diagonal input interval should complete.");
        Quaternion expectedDiagonal = cameraYaw * Quaternion.AngleAxis(22.5f, Vector3.right) * Quaternion.AngleAxis(22.5f, Vector3.forward);
        Assert.That(Quaternion.Angle(expectedDiagonal, ghostRoot.transform.rotation), Is.LessThan(8f),
            "Simultaneous Up and Left must rotate each local axis at 90 degrees per second without digital normalization.");
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
        controllerType.GetField("localRotation", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(controller, Quaternion.Euler(0f, 6f, 0f));
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        Assert.That(controllerType.GetProperty("PanelMountingGuidanceLabel").GetValue(controller), Is.EqualTo("Mount: Angle 6°"));
        Assert.That(arrowObject.activeInHierarchy, Is.False, "Orientation-only guidance does not display a translation arrow.");
        controllerType.GetField("localRotation", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(controller, Quaternion.identity);
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
        controllerType.GetMethod("ConfirmPlacement", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, new object[] { null, EventArgs.Empty });
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False,
            "E continues to release a panel when generic space is clear even when mounting feedback is orange.");
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
        if (heldObject != null) UnityEngine.Object.DestroyImmediate(heldObject);
        if (testAimCameraObject != null) UnityEngine.Object.DestroyImmediate(testAimCameraObject);
        if (previewSectionRoot != null) UnityEngine.Object.DestroyImmediate(previewSectionRoot);
        if (previewSlot != null && previewSectionRoot == null) UnityEngine.Object.DestroyImmediate(previewSlot);
        if (previewSecondSlot != null && previewSectionRoot == null) UnityEngine.Object.DestroyImmediate(previewSecondSlot);
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

    private static Type Resolve(string name) => Type.GetType(name);
}
