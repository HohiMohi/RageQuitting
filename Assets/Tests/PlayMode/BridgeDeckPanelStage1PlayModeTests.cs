using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public sealed class BridgeDeckPanelStage1PlayModeTests
{
    private GameObject player;
    private GameObject panel;
    private Keyboard testKeyboard;
    private GameObject supportSurface;
    private GameObject legacyGirder;
    private GameObject blockingWall;
    private GameObject isolatedPlayerPrefabRoot;
    private InputActionAsset isolatedPlayerActions;
    private Stage1ProjectWideInputActionScope projectWideActions;

    [UnitySetUp]
    public IEnumerator SetupInputFixture()
    {
        projectWideActions = new Stage1ProjectWideInputActionScope();
        projectWideActions.CaptureAndDisable();
        yield return null;
    }

    [UnityTest]
    public IEnumerator ActualPanelCanBeCarriedReleasedAndThrownAsAOnePersonPhysicalObject()
    {
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
            Assert.Ignore("The offline panel physics test cannot run during an active NGO session.");

#if UNITY_EDITOR
        GameObject playerPrefab = CreateIsolatedPlayerPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab"));
        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
#else
        Assert.Ignore("The Stage1 production prefabs are loaded through the Editor AssetDatabase.");
        yield break;
#endif
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(panelPrefab, Is.Not.Null);
        player = UnityEngine.Object.Instantiate(playerPrefab, new Vector3(0f, 4f, 0f), Quaternion.identity);
        panel = UnityEngine.Object.Instantiate(panelPrefab, new Vector3(0f, 1f, 0f), Quaternion.identity);
        yield return null;

        System.Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        System.Type mountableType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        Component interaction = player.GetComponent(interactionType);
        Component mountable = panel.GetComponent(mountableType);
        Rigidbody body = panel.GetComponent<Rigidbody>();
        Collider solid = panel.GetComponent<Collider>();
        Assert.That(interaction, Is.Not.Null);
        Assert.That(mountable, Is.Not.Null);
        Assert.That(body.mass, Is.EqualTo(1f));
        Assert.That((bool)mountableType.GetProperty("CanEnterSingleCarryPlacement").GetValue(mountable), Is.True);

        Vector3 initialPosition = panel.transform.position;
        mountableType.GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(panel));
        Assert.That(body.isKinematic, Is.True);
        Assert.That(body.useGravity, Is.False);
        Assert.That(solid.excludeLayers.value, Is.EqualTo(Physics.AllLayers), "Held contact isolation leaves the panel queryable while suppressing solid player contacts.");
        Assert.That((bool)mountableType.GetMethod("CanReleaseSingleCarryPlacement").Invoke(mountable, new object[] { interaction }), Is.True);

        Vector3 releasePosition = new Vector3(2f, 0.2f, 1f);
        Quaternion releaseRotation = Quaternion.Euler(0f, 90f, 0f);
        Assert.That((bool)interactionType.GetMethod("TryReleaseSingleCarryPlacement").Invoke(interaction,
            new object[] { panel, mountable, releasePosition, releaseRotation }), Is.True,
            "The production interaction coordinator releases the held object after a valid placement pose.");
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.Null);
        Assert.That(body.isKinematic, Is.False);
        Assert.That(body.useGravity, Is.True);
        Assert.That(solid.excludeLayers.value, Is.Zero, "Dropping restores the collider's original contact settings.");
        Assert.That(panel.transform.position, Is.EqualTo(releasePosition));
        Assert.That(Quaternion.Angle(panel.transform.rotation, releaseRotation), Is.LessThan(0.01f));

        mountableType.GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(panel));
        Vector3 preThrowVelocity = body.linearVelocity;
        Vector3 throwDirection = player.transform.forward;
        float expectedThrowSpeed = (float)interactionType.GetProperty("SingleCarryDropThrowMetersPerSecond").GetValue(interaction);
        Vector3 predictedDropPosition = player.transform.position + throwDirection + Vector3.up * 0.5f;
        float predictedHolderPenetration = MeasureMaximumPenetration(solid, predictedDropPosition, panel.transform.rotation,
            player.GetComponentsInChildren<Collider>());
        Assert.That((bool)interactionType.GetMethod("DropObject").Invoke(interaction, null), Is.True,
            "The normal production drop entrypoint releases the physical panel.");
        float resolvedHolderPenetration = MeasureMaximumPenetration(solid, panel.transform.position, panel.transform.rotation,
            player.GetComponentsInChildren<Collider>());
        Assert.That(resolvedHolderPenetration, Is.Zero,
            $"The accepted physical-panel throw pose is clear of the authoritative holder before physics simulation; resolved position={panel.transform.position}.");
        Vector3 zeroedThrowVelocity = body.linearVelocity;
        Vector3 accumulatedThrowForce = body.GetAccumulatedForce(Time.fixedDeltaTime);
        Vector3 holderPositionBeforePhysics = player.transform.position;
        yield return new WaitForFixedUpdate();
        Vector3 postPhysicsDeltaV = body.linearVelocity - zeroedThrowVelocity;
        Vector3 holderDisplacement = player.transform.position - holderPositionBeforePhysics;
        Debug.Log($"Offline enabled-holder drop overlap: {predictedHolderPenetration:F4}m at {predictedDropPosition}; pre-drop held velocity={preThrowVelocity}, post-drop pre-simulation velocity={zeroedThrowVelocity}, accumulated force={accumulatedThrowForce}, post-physics Δv from zero={postPhysicsDeltaV}, holder movement={holderDisplacement}.");
        Assert.That(Vector3.Dot(accumulatedThrowForce, throwDirection), Is.GreaterThan(0f),
            "The production X/drop input accumulates a forward throw force before simulation.");
        Assert.That(Vector3.Dot(body.linearVelocity, throwDirection), Is.GreaterThan(0.5f),
            $"The isolated offline production throw retains meaningful forward movement after contact resolution. Requested speed={expectedThrowSpeed:F2}m/s; overlap={predictedHolderPenetration:F4}m; pre-drop held velocity={preThrowVelocity}; zeroed velocity={zeroedThrowVelocity}; post-physics Δv from zero={postPhysicsDeltaV}; holder movement={holderDisplacement}.");
        Assert.That(body.linearVelocity.magnitude, Is.GreaterThan(0.1f));
        Assert.That(panel.transform.position, Is.Not.EqualTo(initialPosition));
        Assert.That((bool)mountableType.GetProperty("IsPickedUp").GetValue(mountable), Is.False,
            "Throwing leaves the panel loose and physical.");
    }

    [UnityTest]
    public IEnumerator PhysicalPanelDropAcceptsTheExactMaximumClearanceCandidate()
    {
#if UNITY_EDITOR
        GameObject playerPrefab = CreateIsolatedPlayerPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab"));
        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
#else
        Assert.Ignore("The Stage1 production prefabs are loaded through the Editor AssetDatabase.");
        yield break;
#endif
        player = UnityEngine.Object.Instantiate(playerPrefab, new Vector3(80f, 100f, 0f), Quaternion.identity);
        panel = UnityEngine.Object.Instantiate(panelPrefab, new Vector3(80f, 102f, 0f), Quaternion.identity);
        yield return null;
        System.Type mountableType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        Component mountable = panel.GetComponent(mountableType);
        BoxCollider box = panel.GetComponent<BoxCollider>();
        Assert.That(mountable, Is.Not.Null);
        mountableType.GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });

        Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, Abs(panel.transform.lossyScale));
        Vector3 direction = player.transform.forward.normalized;
        Vector3 projectedAxes = Abs(Quaternion.Inverse(panel.transform.rotation) * direction);
        float panelExtent = Vector3.Dot(projectedAxes, halfExtents);
        float holderExtent = 0f;
        foreach (Collider holderCollider in player.GetComponentsInChildren<Collider>(true))
        {
            if (holderCollider == null || !holderCollider.enabled || holderCollider.isTrigger
                || holderCollider.transform == panel.transform || holderCollider.transform.IsChildOf(panel.transform)) continue;
            Bounds bounds = holderCollider.bounds;
            holderExtent = Mathf.Max(holderExtent, Vector3.Dot(Abs(direction), bounds.extents)
                + Vector3.Dot(bounds.center - player.transform.position, direction));
        }
        float exactMaximum = panelExtent + Mathf.Max(0f, holderExtent) + 0.05f;
        System.Type resolverType = System.Type.GetType("SingleCarryDropPoseResolver, Assembly-CSharp");
        object[] resolveArgs = { panel, player.transform, exactMaximum, Vector3.zero };
        Assert.That((bool)resolverType.GetMethod("TryResolvePhysicalPanel").Invoke(null, resolveArgs), Is.True,
            "The resolver accepts a valid exact endpoint when the bounded range equals the minimum clearance distance.");
        Vector3 resolved = (Vector3)resolveArgs[3];
        Vector3 expected = player.transform.position + Vector3.up * 0.5f + direction * exactMaximum;
        Assert.That(Vector3.Distance(resolved, expected), Is.LessThan(0.001f));
    }

    private static Vector3 Abs(Vector3 value) => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

    private static float MeasureMaximumPenetration(Collider panelCollider, Vector3 panelPosition, Quaternion panelRotation, Collider[] holderColliders)
    {
        Physics.SyncTransforms();
        float maximum = 0f;
        foreach (Collider holder in holderColliders)
        {
            if (holder == null || !holder.enabled || holder.isTrigger) continue;
            if (Physics.ComputePenetration(panelCollider, panelPosition, panelRotation,
                    holder, holder.transform.position, holder.transform.rotation, out _, out float distance))
                maximum = Mathf.Max(maximum, distance);
        }
        return maximum;
    }

    [UnityTest]
    public IEnumerator ActualPanelFEnters_ECannotConsumeInvalidPlacement_EscapeCancelsWithoutConsuming()
    {
        projectWideActions.UseGameViewInputRouting();
        testKeyboard = InputSystem.AddDevice<Keyboard>();
#if UNITY_EDITOR
        GameObject playerPrefab = CreateIsolatedPlayerPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab"));
        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
#else
        Assert.Ignore("The Stage1 production prefabs are loaded through the Editor AssetDatabase.");
        yield break;
#endif
        player = UnityEngine.Object.Instantiate(playerPrefab, new Vector3(0f, 100f, 0f), Quaternion.identity);
        panel = UnityEngine.Object.Instantiate(panelPrefab, new Vector3(0f, 100f, 1f), Quaternion.identity);
        yield return null;
        System.Type inputType = System.Type.GetType("PlayerInputNew, Assembly-CSharp");
        System.Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        System.Type mountableType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        System.Type controllerType = System.Type.GetType("PlayerSingleCarryPlacementController, Assembly-CSharp");
        Component input = player.GetComponent(inputType);
        Component interaction = player.GetComponent(interactionType);
        Component mountable = panel.GetComponent(mountableType);
        Component controller = player.GetComponent(controllerType);
        Assert.That(input, Is.Not.Null);
        Assert.That(controller, Is.Not.Null);
        object generatedActions = inputType.GetField("playerGameInputActions", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(input);
        InputActionAsset generatedAsset = (InputActionAsset)generatedActions.GetType().GetProperty("asset").GetValue(generatedActions);
        projectWideActions.RestrictToSyntheticKeyboard(generatedAsset, player.GetComponent<PlayerInput>(), testKeyboard);
        mountableType.GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(panel));
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.F));
        InputSystem.Update();
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True, "F enters placement for the actual deck panel.");
        Assert.That((bool)inputType.GetProperty("IsSingleCarryPlacementActive").GetValue(input), Is.True);

        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.E));
        InputSystem.Update();
        yield return null;
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(panel),
            "An unsupported/no-aim placement cannot consume or snap the loose physical panel.");
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);

        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.Escape));
        InputSystem.Update();
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False, "Escape exits placement.");
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(panel),
            "Cancel leaves the panel held by the player.");
    }

    [UnityTest]
    public IEnumerator ActualFAndECommitPlacesTheSamePhysicalPanelOnAValidSupport()
    {
        projectWideActions.UseGameViewInputRouting();
        testKeyboard = InputSystem.AddDevice<Keyboard>();
#if UNITY_EDITOR
        GameObject playerPrefab = CreateIsolatedPlayerPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab"));
        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
#else
        Assert.Ignore("The Stage1 production prefabs are loaded through the Editor AssetDatabase.");
        yield break;
#endif
        supportSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        supportSurface.name = "Stage1 valid placement support";
        supportSurface.transform.position = new Vector3(0f, -0.5f, 4.5f);
        supportSurface.transform.localScale = new Vector3(20f, 1f, 20f);
        player = UnityEngine.Object.Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
        panel = UnityEngine.Object.Instantiate(panelPrefab, new Vector3(0f, 2f, 0f), Quaternion.identity);
        yield return null;

        System.Type inputType = System.Type.GetType("PlayerInputNew, Assembly-CSharp");
        System.Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        System.Type mountableType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        System.Type controllerType = System.Type.GetType("PlayerSingleCarryPlacementController, Assembly-CSharp");
        Component input = player.GetComponent(inputType);
        Component interaction = player.GetComponent(interactionType);
        Component mountable = panel.GetComponent(mountableType);
        Component controller = player.GetComponent(controllerType);
        Assert.That(input, Is.Not.Null);
        Assert.That(controller, Is.Not.Null);
        object generatedActions = inputType.GetField("playerGameInputActions", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(input);
        InputActionAsset generatedAsset = (InputActionAsset)generatedActions.GetType().GetProperty("asset").GetValue(generatedActions);
        projectWideActions.RestrictToSyntheticKeyboard(generatedAsset, player.GetComponent<PlayerInput>(), testKeyboard);

        GameObject aimObject = new GameObject("Stage1 production placement aim camera");
        aimObject.transform.SetParent(player.transform, false);
        Transform placementAimAnchor = (Transform)interactionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(interaction);
        aimObject.transform.position = placementAimAnchor.position;
        aimObject.transform.rotation = placementAimAnchor.rotation * Quaternion.Euler(20f, 0f, 0f);
        Camera aimCamera = aimObject.AddComponent<Camera>();
        aimCamera.nearClipPlane = 0.2f;
        interactionType.GetField("aimCamera", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(interaction, aimCamera);
        controllerType.GetField("placementDistance", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, 5f);
        mountableType.GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });

        object[] aimRayArgs = { default(Ray) };
        Assert.That((bool)interactionType.GetMethod("TryGetAimRay", new[] { typeof(Ray).MakeByRefType() })
            .Invoke(interaction, aimRayArgs), Is.True);
        Ray actualAimRay = (Ray)aimRayArgs[0];
        Collider supportCollider = supportSurface.GetComponent<Collider>();
        // The fixture moved the support after creating its collider and has not guaranteed a fixed step yet.
        Physics.SyncTransforms();
        float closestDistance = float.PositiveInfinity;
        Collider closestRelevantCollider = null;
        foreach (RaycastHit hit in Physics.RaycastAll(actualAimRay, 5f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(player.transform)
                || hit.collider.transform.IsChildOf(panel.transform)) continue;
            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestRelevantCollider = hit.collider;
            }
        }
        Assert.That(closestRelevantCollider, Is.SameAs(supportCollider),
            $"The production aim ray must select the intended support, not an unrelated collider. Ray={actualAimRay}; hit={closestRelevantCollider?.name}@{closestDistance}.");

        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.F));
        InputSystem.Update();
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.True,
            "The real geometry solver should find the supported deck-panel pose from the production aim ray.");
        Vector3 beforeCommitPosition = panel.transform.position;

        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.E));
        InputSystem.Update();
        yield return null;
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.Null,
            "E commits a valid physical-panel placement through the normal interaction coordinator.");
        Assert.That(panel != null && panel.activeInHierarchy, Is.True, "Placement keeps the original panel instance; it is not consumed or replaced.");
        Assert.That(Vector3.Distance(panel.transform.position, beforeCommitPosition), Is.GreaterThan(1f));
        for (int step = 0; step < 90 && panel.GetComponent<Collider>().bounds.min.y > 0.02f; step++)
            yield return new WaitForFixedUpdate();
        Assert.That(panel.GetComponent<Collider>().bounds.min.y, Is.EqualTo(0f).Within(0.05f), "The committed panel rests on the support top.");
        Assert.That(panel.transform.position.z, Is.GreaterThan(2.5f), "The real panel is moved to the support ray hit, away from the player.");
        Assert.That(panel.GetComponent<Rigidbody>().isKinematic, Is.False);
        Assert.That(panel.GetComponent<Rigidbody>().useGravity, Is.True);
    }

    [UnityTest]
    public IEnumerator ExistingGirderRetainsItsLegacyCarryAndLooseColliderBehavior()
    {
#if UNITY_EDITOR
        GameObject playerPrefab = CreateIsolatedPlayerPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab"));
        GameObject girderPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenMainGirderMountable.prefab");
#else
        Assert.Ignore("The existing bridge-part regression fixture loads production prefabs through the Editor AssetDatabase.");
        yield break;
#endif
        supportSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        supportSurface.name = "Legacy girder carry support floor";
        supportSurface.transform.position = new Vector3(20f, -0.5f, 20f);
        supportSurface.transform.localScale = new Vector3(20f, 1f, 20f);
        // Start outside the girder's long solid collider so the production safe-grip
        // search can move the controller to an actual attach point.
        player = UnityEngine.Object.Instantiate(playerPrefab, new Vector3(20f, 0f, 14f), Quaternion.identity);
        legacyGirder = UnityEngine.Object.Instantiate(girderPrefab, new Vector3(20f, 1f, 20f), Quaternion.identity);
        yield return null;
        System.Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        System.Type mountableType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        Component interaction = player.GetComponent(interactionType);
        Component mountable = legacyGirder.GetComponent(mountableType);
        Assert.That(interaction, Is.Not.Null);
        Assert.That(mountable, Is.Not.Null);
        Assert.That((bool)mountableType.GetProperty("IsPhysicalDeckPanel").GetValue(mountable), Is.False);
        mountableType.GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(legacyGirder),
            "The existing main girder still uses its production carry route.");
        Assert.That((bool)mountableType.GetProperty("IsPickedUp").GetValue(mountable), Is.True);

        interactionType.GetMethod("DropObject").Invoke(interaction, null);
        yield return new WaitForFixedUpdate();
        Collider girderCollider = legacyGirder.GetComponent<Collider>();
        Rigidbody girderBody = legacyGirder.GetComponent<Rigidbody>();
        Assert.That(girderCollider, Is.Not.Null);
        Assert.That(girderCollider.enabled && !girderCollider.isTrigger, Is.True, "The loose girder remains a solid world collider after release.");
        Assert.That(girderBody.isKinematic, Is.False);
        Assert.That(girderBody.useGravity, Is.True);
    }

    [UnityTest]
    public IEnumerator LoosePanelBlocksAProductionCharacterControllerWalk()
    {
#if UNITY_EDITOR
        GameObject playerPrefab = CreateIsolatedPlayerPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab"));
        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
#else
        Assert.Ignore("The Stage1 production prefabs are loaded through the Editor AssetDatabase.");
        yield break;
#endif
        player = UnityEngine.Object.Instantiate(playerPrefab, new Vector3(30f, 100f, 0f), Quaternion.identity);
        panel = UnityEngine.Object.Instantiate(panelPrefab, new Vector3(30f, 101f, 2f), Quaternion.identity);
        Rigidbody body = panel.GetComponent<Rigidbody>();
        Collider panelCollider = panel.GetComponent<Collider>();
        body.useGravity = false;
        System.Type firstPersonType = System.Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp");
        Behaviour firstPerson = firstPersonType != null ? player.GetComponent(firstPersonType) as Behaviour : null;
        if (firstPerson != null) firstPerson.enabled = false;
        CharacterController character = player.GetComponent<CharacterController>();
        Assert.That(character, Is.Not.Null);
        Assert.That(body.isKinematic, Is.False);
        Assert.That(panelCollider.enabled && !panelCollider.isTrigger, Is.True);
        yield return null;

        Vector3 start = player.transform.position;
        character.Move(Vector3.forward * 4f);
        Physics.SyncTransforms();
        float travelledForward = player.transform.position.z - start.z;
        Assert.That(travelledForward, Is.LessThan(1.25f),
            "The actual production CharacterController cannot walk through the loose full-size deck panel.");
        Assert.That(panelCollider.enabled && !panelCollider.isTrigger, Is.True);
        Assert.That(body.isKinematic, Is.False, "The panel remains a loose Rigidbody after blocking walking contact.");
    }

    [UnityTest]
    public IEnumerator PhysicalPanelDropRemainsHeldWhenEveryForwardPoseIsBlocked()
    {
#if UNITY_EDITOR
        GameObject playerPrefab = CreateIsolatedPlayerPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab"));
        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
#else
        Assert.Ignore("The Stage1 production prefabs are loaded through the Editor AssetDatabase.");
        yield break;
#endif
        player = UnityEngine.Object.Instantiate(playerPrefab, new Vector3(45f, 100f, 0f), Quaternion.identity);
        panel = UnityEngine.Object.Instantiate(panelPrefab, new Vector3(45f, 102f, 0f), Quaternion.identity);
        blockingWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blockingWall.name = "Blocked physical panel throw wall";
        blockingWall.transform.position = new Vector3(45f, 100.5f, 2f);
        blockingWall.transform.localScale = new Vector3(10f, 4f, 0.2f);
        yield return null;

        System.Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        System.Type mountableType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        Component interaction = player.GetComponent(interactionType);
        Component mountable = panel.GetComponent(mountableType);
        mountableType.GetMethod("PickedUp").Invoke(mountable, new object[] { player.transform });
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(panel));

        Assert.That((bool)interactionType.GetMethod("DropObject").Invoke(interaction, null), Is.False,
            "A forward wall blocks all candidates, so the panel stays held instead of teleporting through it.");
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(panel));
        Assert.That((bool)mountableType.GetProperty("IsPickedUp").GetValue(mountable), Is.True);
        Assert.That(panel.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(panel.GetComponent<Rigidbody>().useGravity, Is.False);
        Assert.That(panel.GetComponent<Collider>().excludeLayers.value, Is.EqualTo(Physics.AllLayers),
            "Rejected no-space drops preserve held contact isolation.");
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        try
        {
            DeactivateFixturePlayerInput(player);
            if (panel != null) UnityEngine.Object.Destroy(panel);
            if (player != null) UnityEngine.Object.Destroy(player);
            if (supportSurface != null) UnityEngine.Object.Destroy(supportSurface);
            if (legacyGirder != null) UnityEngine.Object.Destroy(legacyGirder);
            if (blockingWall != null) UnityEngine.Object.Destroy(blockingWall);
            yield return null;
            if (isolatedPlayerPrefabRoot != null) UnityEngine.Object.Destroy(isolatedPlayerPrefabRoot);
            if (isolatedPlayerActions != null)
            {
                isolatedPlayerActions.Disable();
                UnityEngine.Object.Destroy(isolatedPlayerActions);
            }
            yield return null;
            projectWideActions?.DisableForTest();
            if (testKeyboard != null) InputSystem.RemoveDevice(testKeyboard);
        }
        finally
        {
            try
            {
                projectWideActions?.DisableForTest();
                projectWideActions?.RestoreOriginalMapState();
            }
            finally
            {
                projectWideActions?.RestoreInputSettings();
                projectWideActions = null;
            }
        }
        yield return null;
    }

    private static void DeactivateFixturePlayerInput(GameObject root)
    {
        if (root == null) return;
        System.Type playerInputNewType = System.Type.GetType("PlayerInputNew, Assembly-CSharp");
        Component inputNew = playerInputNewType != null ? root.GetComponentInChildren(playerInputNewType, true) : null;
        if (inputNew != null)
            playerInputNewType.GetMethod("DisposeInput", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(inputNew, null);
        foreach (PlayerInput input in root.GetComponentsInChildren<PlayerInput>(true))
        {
            if (input == null) continue;
            if (input.inputIsActive) input.DeactivateInput();
            input.actions.Disable();
        }
    }

    private GameObject CreateIsolatedPlayerPrefab(GameObject productionPrefab)
    {
        Assert.That(productionPrefab, Is.Not.Null);
        PlayerInput sourceInput = productionPrefab.GetComponent<PlayerInput>();
        FieldInfo actionsField = typeof(PlayerInput).GetField("m_Actions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(sourceInput, Is.Not.Null);
        Assert.That(actionsField, Is.Not.Null);
        InputActionAsset productionActions = (InputActionAsset)actionsField.GetValue(sourceInput);
        Assert.That(productionActions, Is.Not.Null);

        isolatedPlayerActions = UnityEngine.Object.Instantiate(productionActions);
        isolatedPlayerPrefabRoot = new GameObject("Stage1 isolated production PlayerNew prefab holder");
        isolatedPlayerPrefabRoot.SetActive(false);
        GameObject isolatedPrefab = UnityEngine.Object.Instantiate(productionPrefab, isolatedPlayerPrefabRoot.transform);
        PlayerInput isolatedInput = isolatedPrefab.GetComponent<PlayerInput>();
        Assert.That(isolatedInput, Is.Not.Null);
        actionsField.SetValue(isolatedInput, isolatedPlayerActions);
        if (testKeyboard != null) isolatedPlayerActions.devices = new InputDevice[] { testKeyboard };
        Assert.That(isolatedInput.actions, Is.SameAs(isolatedPlayerActions));
        Unity.Netcode.NetworkBehaviour[] sourceBehaviours = productionPrefab.GetComponents<Unity.Netcode.NetworkBehaviour>();
        Unity.Netcode.NetworkBehaviour[] isolatedBehaviours = isolatedPrefab.GetComponents<Unity.Netcode.NetworkBehaviour>();
        Assert.That(isolatedBehaviours.Length, Is.EqualTo(sourceBehaviours.Length),
            "The test-only input isolation clone preserves the production NetworkBehaviour count.");
        for (int i = 0; i < sourceBehaviours.Length; i++)
            Assert.That(isolatedBehaviours[i].GetType(), Is.EqualTo(sourceBehaviours[i].GetType()),
                "The test-only input isolation clone preserves the production NetworkBehaviour component order.");
        Assert.That(InputActionsAreStructurallyEquivalent(productionActions, isolatedPlayerActions), Is.True,
            "The fixture uses an isolated copy of the production PlayerInput action asset with the same maps/actions.");
        return isolatedPrefab;
    }

    private static bool InputActionsAreStructurallyEquivalent(InputActionAsset source, InputActionAsset copy)
    {
        if (source.actionMaps.Count != copy.actionMaps.Count) return false;
        for (int mapIndex = 0; mapIndex < source.actionMaps.Count; mapIndex++)
        {
            InputActionMap sourceMap = source.actionMaps[mapIndex];
            InputActionMap copyMap = copy.actionMaps[mapIndex];
            if (sourceMap.name != copyMap.name || sourceMap.actions.Count != copyMap.actions.Count) return false;
            for (int actionIndex = 0; actionIndex < sourceMap.actions.Count; actionIndex++)
                if (sourceMap.actions[actionIndex].name != copyMap.actions[actionIndex].name) return false;
        }
        return true;
    }

}

internal sealed class Stage1ProjectWideInputActionScope
{
    private InputActionAsset originalAsset;
    private InputAction[] originalActions;
    private bool[] initiallyEnabled;
    private InputSettings originalInputSettings;
    private InputSettings fixtureInputSettings;
    private InputSettings.UpdateMode originalUpdateMode;
    private InputSettings.BackgroundBehavior originalBackgroundBehavior;
    private InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
#if UNITY_EDITOR
    private string originalInputSettingsAssetPath;
    private string originalInputSettingsAssetHash;
#endif

    public void CaptureAndDisable()
    {
        originalAsset = InputSystem.actions;
        if (originalAsset == null) return;
        originalActions = originalAsset.actionMaps.SelectMany(map => map.actions).ToArray();
        initiallyEnabled = originalActions.Select(action => action.enabled).ToArray();
        originalAsset.Disable();
    }

    public void UseGameViewInputRouting()
    {
        // The Editor test runner can lack GameView focus; route only this synthetic-input fixture to the game view.
        originalInputSettings = InputSystem.settings;
        originalUpdateMode = originalInputSettings.updateMode;
        originalBackgroundBehavior = originalInputSettings.backgroundBehavior;
        originalEditorInputBehavior = originalInputSettings.editorInputBehaviorInPlayMode;
#if UNITY_EDITOR
        originalInputSettingsAssetPath = UnityEditor.AssetDatabase.GetAssetPath(originalInputSettings);
        Assert.That(string.IsNullOrEmpty(originalInputSettingsAssetPath), Is.False,
            "The fixture must clone the project's existing InputSettings asset rather than mutate or replace it permanently.");
        string fullPath = System.IO.Path.GetFullPath(originalInputSettingsAssetPath);
        originalInputSettingsAssetHash = ComputeFileHash(fullPath);
#endif
        fixtureInputSettings = UnityEngine.Object.Instantiate(originalInputSettings);
        fixtureInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        fixtureInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = fixtureInputSettings;
    }

    public void RestrictToSyntheticKeyboard(InputActionAsset generatedAsset, PlayerInput unityPlayerInput, Keyboard keyboard)
    {
        Assert.That(generatedAsset, Is.Not.Null);
        Assert.That(unityPlayerInput, Is.Not.Null);
        Assert.That(keyboard, Is.Not.Null);
        InputDevice[] onlyFixtureKeyboard = { keyboard };
        generatedAsset.devices = onlyFixtureKeyboard;
        unityPlayerInput.actions.devices = onlyFixtureKeyboard;
        Assert.That(generatedAsset.devices.HasValue && generatedAsset.devices.Value.Count == 1
            && generatedAsset.devices.Value[0] == keyboard, Is.True,
            "The generated fixture actions are restricted to their synthetic keyboard.");
        Assert.That(unityPlayerInput.actions.devices.HasValue && unityPlayerInput.actions.devices.Value.Count == 1
            && unityPlayerInput.actions.devices.Value[0] == keyboard, Is.True,
            "The fixture PlayerInput actions are restricted to their synthetic keyboard.");
    }

    public void RestoreInputSettings()
    {
        if (fixtureInputSettings == null) return;
        InputSystem.settings = originalInputSettings;
        Assert.That(InputSystem.settings, Is.SameAs(originalInputSettings), "The exact original InputSettings reference must be restored.");
        Assert.That(originalInputSettings.updateMode, Is.EqualTo(originalUpdateMode));
        Assert.That(originalInputSettings.backgroundBehavior, Is.EqualTo(originalBackgroundBehavior));
        Assert.That(originalInputSettings.editorInputBehaviorInPlayMode, Is.EqualTo(originalEditorInputBehavior));
#if UNITY_EDITOR
        Assert.That(ComputeFileHash(System.IO.Path.GetFullPath(originalInputSettingsAssetPath)),
            Is.EqualTo(originalInputSettingsAssetHash), "The project's serialized InputSettings asset must remain byte-identical.");
#endif
        UnityEngine.Object.Destroy(fixtureInputSettings);
        fixtureInputSettings = null;
        originalInputSettings = null;
    }

#if UNITY_EDITOR
    private static string ComputeFileHash(string path)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return System.BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
#endif

    public void DisableForTest()
    {
        InputActionAsset current = InputSystem.actions;
        Assert.That(current, Is.SameAs(originalAsset), "The project-wide action asset reference must remain unchanged during the test.");
        if (current == null) return;
        current.Disable();
        foreach (InputAction action in originalActions)
            Assert.That(action.enabled, Is.False, $"Project-wide action {action.name} must remain disabled while test devices are removed.");
    }

    public void RestoreOriginalMapState()
    {
        Assert.That(InputSystem.actions, Is.SameAs(originalAsset), "The test must preserve the original project-wide action asset reference.");
        if (originalAsset == null) return;
        for (int i = 0; i < originalActions.Length; i++)
        {
            if (initiallyEnabled[i] && !originalActions[i].enabled) originalActions[i].Enable();
            else if (!initiallyEnabled[i] && originalActions[i].enabled) originalActions[i].Disable();
            Assert.That(originalActions[i].enabled, Is.EqualTo(initiallyEnabled[i]), $"Project-wide action {originalActions[i].name} must restore its original enabled state.");
        }
    }
}
