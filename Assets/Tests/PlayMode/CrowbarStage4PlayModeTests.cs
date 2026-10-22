using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public sealed class CrowbarStage4PlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private static readonly Type NailType = Type.GetType("MountingNail, Assembly-CSharp");
    private static readonly Type NailStateType = Type.GetType("MountingNailState, Assembly-CSharp");
    private static readonly Type NailAimResolverType = Type.GetType("MountingNailAimResolver, Assembly-CSharp");
    private static readonly Type PlayerInputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
    private static readonly Type InventoryType = Type.GetType("PlayerInventory, Assembly-CSharp");
    private static readonly Type HealthType = Type.GetType("PlayerHealth, Assembly-CSharp");
    private static readonly Type InteractionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
    private static readonly Type ActionType = Type.GetType("PlayerActionController, Assembly-CSharp");
    private static readonly Type EquippableItemTypeInfo = Type.GetType("EquippableItem, Assembly-CSharp");
    private readonly System.Collections.Generic.List<GameObject> extras = new();
    private readonly CrowbarInputSettingsScope inputSettings = new();
    private GameObject player, target, nailObject, cameraObject;
    private Mouse mouse;
    private Keyboard keyboard;
    private Component action, nail, health, inventory;
    private Component hud, highlight;
    private Component hudText;
    private Image hudProgressImage;
    private GameObject hudProgressHolder;
    private Material outlineMaterial;
    private ScriptableObject crowbar;

    [UnitySetUp]
    public IEnumerator SetUpFixture()
    {
        inputSettings.UseFocusedGameViewRouting();
        mouse = InputSystem.AddDevice<Mouse>();
        keyboard = InputSystem.AddDevice<Keyboard>();
        Assert.That(Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening,
            Is.True, "The local input fixture must not run alongside a live NGO session.");
#if UNITY_EDITOR
        crowbar = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/Crowbar.asset");
#endif
        Assert.That(crowbar, Is.Not.Null);
        player = new GameObject("Crowbar Stage 4 local input player");
        player.AddComponent(PlayerInputType);
        inventory = player.AddComponent(InventoryType);
        Component interaction = player.AddComponent(InteractionType);
        health = player.AddComponent(HealthType);
        action = player.AddComponent(ActionType);

        cameraObject = new GameObject("MainCamera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.transform.SetPositionAndRotation(player.transform.position, Quaternion.identity);
        interaction.GetType().GetMethod("SetInteractionOrigin", PublicInstance).Invoke(interaction, new object[] { cameraObject.transform });
        interaction.GetType().GetMethod("SetAimCamera", PublicInstance).Invoke(interaction, new object[] { camera });

        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(prefab, Is.Not.Null);
        target = new GameObject("Crowbar Stage 4 embedded target");
        target.transform.position = new Vector3(0f, 0f, 1.2f);
        nailObject = UnityEngine.Object.Instantiate(prefab);
        nailObject.transform.position = target.transform.position;
        nail = nailObject.GetComponent(NailType);
        Rigidbody body = target.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
        target.AddComponent<BoxCollider>().enabled = false;
        SetNailState(nail, target.transform);
        AimAtNail(nail);
        yield return null;
        yield return null;
        Physics.SyncTransforms();
        ConfigureInputDevices(player.GetComponent(PlayerInputType));
        Assert.That(Read(nail, "IsCrowbarExtractionAvailable"), Is.True);
        Assert.That(ResolveAim(camera), Is.SameAs(nail), "The mapped RMB ray must resolve the visible nail through production code.");
        Assert.That((bool)inventory.GetType().GetMethod("AddItem").Invoke(inventory, new object[] { crowbar }), Is.True);
    }

    [UnityTearDown]
    public IEnumerator TearDownFixture()
    {
        try
        {
            if (player != null) UnityEngine.Object.Destroy(player);
            if (target != null) UnityEngine.Object.Destroy(target);
            if (nailObject != null) UnityEngine.Object.Destroy(nailObject);
            if (cameraObject != null) UnityEngine.Object.Destroy(cameraObject);
            foreach (GameObject extra in extras) if (extra != null) UnityEngine.Object.Destroy(extra);
            if (outlineMaterial != null) UnityEngine.Object.Destroy(outlineMaterial);
            extras.Clear();
            yield return null;
        }
        finally
        {
            try
            {
                RemoveOwnedDevice(keyboard);
                RemoveOwnedDevice(mouse);
            }
            finally
            {
                inputSettings.Restore();
            }
        }
    }

    [UnityTest]
    public IEnumerator VirtualRmbReleaseResetsProgressAndFreshPressCommitsWithoutMeleeEvent()
    {
        int meleeEvents = 0;
        action.GetType().GetEvent("OnActionPerformed").AddEventHandler(action,
            (EventHandler)((_, _) => meleeEvents++));
        Component playerInput = player.GetComponent(PlayerInputType);
        Assert.That(Read(playerInput, "IsLocalGameplayInputActive"), Is.True);
        Assert.That(inventory.GetType().GetMethod("GetCurrentSelectedItem", PublicInstance).Invoke(inventory, null), Is.SameAs(crowbar));
        object inputActions = PlayerInputType.GetField("playerGameInputActions", PrivateInstance).GetValue(playerInput);
        object gameActions = inputActions.GetType().GetProperty("Game", PublicInstance).GetValue(inputActions);
        InputAction altAction = (InputAction)gameActions.GetType().GetProperty("ActionAlt", PublicInstance).GetValue(gameActions);
        Assert.That(altAction.enabled, Is.True, "The actual generated ActionAlt action must be enabled.");
        int altEvents = 0;
        EventHandler altHandler = (_, _) => altEvents++;
        FieldInfo altEvent = PlayerInputType.GetField("OnActionAlt", PublicInstance);
        altEvent.SetValue(playerInput, (EventHandler)altEvent.GetValue(playerInput) + altHandler);
        QueueRmb(true); yield return null;
        Assert.That(mouse.rightButton.isPressed, Is.True,
            $"The virtual mouse right-button state must be applied by InputSystem.Update (added={mouse.added}, enabled={mouse.enabled}, " +
            $"update={InputState.currentUpdateType}, mode={InputSystem.settings.updateMode}, value={mouse.rightButton.ReadValue()}, " +
            $"devices={InputSystem.devices.Count}, current={InputSystem.GetDevice<Mouse>() == mouse}).");
        Assert.That(altAction.ReadValue<float>(), Is.GreaterThan(0.5f), "The generated ActionAlt binding must read the real virtual mouse state.");
        Assert.That(altEvents, Is.EqualTo(1), "The real mapped right mouse button must reach PlayerInputNew exactly once.");
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"));
        Assert.That(Read(action, "ActionMovementMultiplier"), Is.EqualTo(0.65f).Within(0.001f));
        Assert.That(ReadFloat(nail, "CrowbarExtractionProgressNormalized"), Is.EqualTo(0f).Within(0.05f));
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.That(ReadFloat(nail, "CrowbarExtractionProgressNormalized"), Is.GreaterThan(0.1f));
        QueueRmb(false); yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"));
        Assert.That(ReadFloat(nail, "CrowbarExtractionProgressNormalized"), Is.EqualTo(0f));
        Assert.That(Read(action, "ActionMovementMultiplier"), Is.EqualTo(1f));
        QueueRmb(true);
        float deadline = Time.realtimeSinceStartup + 2.5f;
        while (nail != null && Read(nail, "State").ToString() != "Loose" && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(nail == null || Read(nail, "State").ToString() == "Loose", Is.True,
            "A fresh press commits the one-time extraction without recovering the nail.");
        Assert.That(nail == null || (bool)ReadField(nail, "removalPending"), Is.True,
            "Successful extraction marks the nail for actual removal/despawn rather than merely changing it to Loose.");
        Assert.That(meleeEvents, Is.Zero, "Extraction completion is not a fictitious LPM action.");
        Assert.That(Read(action, "ActionMovementMultiplier"), Is.EqualTo(1f));
    }

    [UnityTest]
    public IEnumerator PhysicalRmbRemovesSharedPairNailsOneAtATimeAndReleasesJointOnlyAfterLastCommit()
    {
        GameObject firstTarget = CreatePairPhysicsTarget("Crowbar shared pair first target");
        GameObject secondTarget = CreatePairPhysicsTarget("Crowbar shared pair second target");
        Component firstNail = CreatePairNailAt(firstTarget.transform, secondTarget.transform, new Vector3(-0.12f, 0f, 1.2f));
        Component lastNail = CreatePairNailAt(firstTarget.transform, secondTarget.transform, new Vector3(0.12f, 0f, 1.2f));
        object registry = ReadField(firstNail, "connectionRegistry");
        MethodInfo pairJointCount = registry.GetType().GetMethod("PairJointCount", PrivateInstance);
        Assert.That((int)pairJointCount.Invoke(registry, new object[] { firstTarget.transform, secondTarget.transform }), Is.EqualTo(1),
            "Two shared pair nails create one physical joint for their target pair.");
        MethodInfo targetConnected = registry.GetType().GetMethod("IsOfflineTargetConnected", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That((bool)targetConnected.Invoke(null, new object[] { firstTarget.scene, firstTarget.transform }), Is.True);
        FixedJoint initialJoint = secondTarget.GetComponent<FixedJoint>();
        Assert.That(initialJoint, Is.Not.Null);
        Assert.That(initialJoint.connectedBody, Is.SameAs(firstTarget.GetComponent<Rigidbody>()));
        Component firstResource = firstNail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"));
        CreatePresentationHud(firstResource);
        SetCurrentTarget(firstResource);
        EvaluatePresentationHud();
        InvokeHighlightLateUpdate();

        AimAtNail(firstNail);
        Assert.That(ResolveAim(cameraObject.GetComponent<Camera>()), Is.SameAs(firstNail));
        QueueRmb(true);
        yield return new WaitForSecondsRealtime(1.7f);
        yield return null;
        Assert.That(firstNail == null, Is.True, "A committed extraction removes the nail object without recovery.");
        Assert.That((int)pairJointCount.Invoke(registry, new object[] { firstTarget.transform, secondTarget.transform }), Is.EqualTo(1),
            "Extracting the first of two shared pair nails leaves their shared connection and pickup lock intact.");
        Assert.That((bool)targetConnected.Invoke(null, new object[] { firstTarget.scene, firstTarget.transform }), Is.True,
            "The gameplay pickup predicate remains blocked while the last pair nail still connects the targets.");
        FixedJoint survivingJoint = secondTarget.GetComponent<FixedJoint>();
        Assert.That(survivingJoint, Is.Not.Null);
        Assert.That(survivingJoint.connectedBody, Is.SameAs(firstTarget.GetComponent<Rigidbody>()));
        Assert.That(Read(action, "IsCrowbarExtractionActiveOrPending"), Is.False);
        cameraObject.transform.rotation = Quaternion.LookRotation(Vector3.up);
        RefreshTargetThroughProductionRay();
        EvaluatePresentationHud();
        InvokeHighlightLateUpdate();
        Assert.That(GetHudText(), Is.Empty, "The committed extraction clears HUD text.");
        Assert.That((MonoBehaviour)ReadField(highlight, "currentTarget"), Is.Null, "The committed extraction clears the nail outline.");

        QueueRmb(false); yield return null;
        AimAtNail(lastNail);
        Assert.That(ResolveAim(cameraObject.GetComponent<Camera>()), Is.SameAs(lastNail));
        SetCurrentTarget(lastNail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp")));
        EvaluatePresentationHud();
        QueueRmb(true);
        yield return null;
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Contain("Extracting nail:"));
        yield return new WaitForSecondsRealtime(1.7f);
        yield return null;
        Assert.That(lastNail == null, Is.True, "The final committed extraction removes the last nail without recovery.");
        yield return new WaitForFixedUpdate();
        Assert.That((int)pairJointCount.Invoke(registry, new object[] { firstTarget.transform, secondTarget.transform }), Is.EqualTo(0));
        Assert.That((bool)targetConnected.Invoke(null, new object[] { firstTarget.scene, firstTarget.transform }), Is.False,
            "The gameplay pickup predicate opens after the final pair nail is extracted.");
        Assert.That(secondTarget.GetComponent<FixedJoint>(), Is.Null, "The final commit destroys the shared joint after the physics step.");
        cameraObject.transform.rotation = Quaternion.LookRotation(Vector3.up);
        RefreshTargetThroughProductionRay();
        yield return new WaitForSecondsRealtime(0.25f);
        EvaluatePresentationHud();
        InvokeHighlightLateUpdate();
        Assert.That(GetHudText(), Is.Empty);
        Assert.That((MonoBehaviour)ReadField(highlight, "currentTarget"), Is.Null);
        Assert.That(GetHighlightTargetCount(), Is.Zero,
            "The destroyed-target outline wrapper finishes its fade and releases cached proxy state.");
    }

    [UnityTest]
    public IEnumerator PhysicalRmbAttemptCancelsWhenIncompletePanelCompletesAndProtectsItsNails()
    {
        BuildCompletedPanelFixture(out Component panel, out Component panelState, out Component[] nails, initialNailCount: 3);
        foreach (Collider collider in panel.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        for (int i = 1; i < nails.Length; i++)
            foreach (Collider collider in nails[i].GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        Component targetNail = nails[0];
        Assert.That(Read(Read(targetNail, "CrowbarExtractionReadState"), "Availability").ToString(), Is.EqualTo("Available"));
        GetAimSegment(targetNail, out Vector3 segmentStart, out Vector3 segmentEnd);
        cameraObject.transform.position = (segmentStart + segmentEnd) * 0.5f - Vector3.forward * 0.65f;
        cameraObject.transform.rotation = Quaternion.LookRotation((segmentStart + segmentEnd) * 0.5f - cameraObject.transform.position);
        Physics.SyncTransforms();
        AimAtNail(targetNail);
        Assert.That(ResolveAim(cameraObject.GetComponent<Camera>()), Is.SameAs(targetNail));
        CreatePresentationHud(targetNail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp")));
        SetCurrentTarget(targetNail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp")));
        EvaluatePresentationHud();

        QueueRmb(true); yield return null;
        Assert.That(Read(action, "IsCrowbarExtractionActiveOrPending"), Is.True);
        Assert.That(ReadAvailability(targetNail), Is.EqualTo("Reserved"));

        Transform girder = (Transform)ReadField(targetNail, "embeddedSecondTarget");
        CreatePanelPairNail(panel.transform, girder, new Vector2(0.5f, 0.6f));
        Physics.SyncTransforms();
        panelState.GetType().GetMethod("Update", PrivateInstance).Invoke(panelState, null);
        Assert.That(Read(Read(targetNail, "CrowbarExtractionReadState"), "Availability").ToString(), Is.EqualTo("PermanentlyMounted"));
        float cancelDeadline = Time.realtimeSinceStartup + 1f;
        while ((bool)Read(action, "IsCrowbarExtractionActiveOrPending") && Time.realtimeSinceStartup < cancelDeadline)
            yield return null;
        Assert.That(Read(action, "IsCrowbarExtractionActiveOrPending"), Is.False,
            "The panel's real assessment change cancels the active extraction on the next player update.");
        EvaluatePresentationHud();
        InvokeHighlightLateUpdate();
        Assert.That(GetHudText(), Does.Contain("Nail permanently mounted"));
        Assert.That(ReadField(highlight, "currentTarget"), Is.SameAs(targetNail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"))));

        yield return new WaitForSecondsRealtime(1.7f);
        Assert.That(Read(targetNail, "State").ToString(), Is.EqualTo("EmbeddedPair"));
        Assert.That((bool)ReadField(targetNail, "removalPending"), Is.False,
            "The canceled timer cannot remove a nail after the panel becomes durably complete.");
        Assert.That((bool)Read(targetNail, "BlocksPickup"), Is.True);
    }

    [UnityTest]
    public IEnumerator PhysicalRmbAttemptClearsWhenIncompletePanelIsHammerReset()
    {
        BuildCompletedPanelFixture(out Component panel, out _, out Component[] nails, initialNailCount: 3);
        foreach (Collider collider in panel.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        for (int i = 1; i < nails.Length; i++)
            foreach (Collider collider in nails[i].GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        Component targetNail = nails[0];
        CreatePresentationHud(targetNail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp")));
        SetCurrentTarget(targetNail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp")));
        GetAimSegment(targetNail, out Vector3 segmentStart, out Vector3 segmentEnd);
        cameraObject.transform.position = (segmentStart + segmentEnd) * 0.5f - Vector3.forward * 0.65f;
        cameraObject.transform.rotation = Quaternion.LookRotation((segmentStart + segmentEnd) * 0.5f - cameraObject.transform.position);
        Physics.SyncTransforms();
        AimAtNail(targetNail);
        Assert.That(ResolveAim(cameraObject.GetComponent<Camera>()), Is.SameAs(targetNail));
        EvaluatePresentationHud();
        QueueRmb(true); yield return null;
        Assert.That(Read(action, "IsCrowbarExtractionActiveOrPending"), Is.True);
        Assert.That(ReadAvailability(targetNail), Is.EqualTo("Reserved"));
        Assert.That(GetHudText(), Does.Contain("Extracting nail:"));

        Component marker = panel.GetComponent(Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"));
        MethodInfo reset = Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp")
            .GetMethod("TryResetPanelWithHammer", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That((bool)reset.Invoke(null, new object[] { marker, Vector3.forward }), Is.True,
            "The actual incomplete-panel hammer reset path accepts the reset.");
        Assert.That(Read(targetNail, "State").ToString(), Is.EqualTo("Loose"));
        Assert.That((bool)ReadField(targetNail, "removalPending"), Is.True,
            "The accepted panel reset removes its embedded nails without recovery.");
        float cancelDeadline = Time.realtimeSinceStartup + 1f;
        while ((bool)Read(action, "IsCrowbarExtractionActiveOrPending") && Time.realtimeSinceStartup < cancelDeadline)
            yield return null;
        Assert.That(Read(action, "IsCrowbarExtractionActiveOrPending"), Is.False);
        Assert.That(targetNail == null, Is.True, "The reset-destroyed nail is gone after the deferred Unity destroy.");
        EvaluatePresentationHud();
        InvokeHighlightLateUpdate();
        Assert.That(GetHudText(), Is.Empty, "Reset clears the stale extraction HUD immediately.");
        Assert.That(ReadField(highlight, "currentTarget"), Is.Null, "Reset clears the stale nail outline.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator PhysicalRmbContinuesWhenEmbeddedSingleTargetMovesWithinSameAssessment()
    {
        SetNailState(nail, target.transform);
        nail.GetType().GetMethod("CompleteEmbeddedSingle", PrivateInstance).Invoke(nail, null);
        Assert.That(Read(nail, "State").ToString(), Is.EqualTo("EmbeddedSingle"));
        AimAtNail(nail);
        Assert.That(ResolveAim(cameraObject.GetComponent<Camera>()), Is.SameAs(nail));
        QueueRmb(true); yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"));
        object acceptedHandle = Read(Read(nail, "CrowbarExtractionReadState"), "Handle");

        target.transform.position += cameraObject.transform.forward * 0.03f;
        Physics.SyncTransforms();
        yield return null;
        Assert.That(ResolveAim(cameraObject.GetComponent<Camera>()), Is.SameAs(nail));
        Assert.That(Read(Read(nail, "CrowbarExtractionReadState"), "Handle"), Is.EqualTo(acceptedHandle),
            "Same-assessment target motion preserves the existing reservation handle.");
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"));
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.That(ReadFloat(nail, "CrowbarExtractionProgressNormalized"), Is.GreaterThan(0.1f),
            "Progress continues through the target motion instead of restarting.");
        yield return new WaitForSecondsRealtime(1.3f);
        float removalDeadline = Time.realtimeSinceStartup + 1f;
        while (nail != null && Time.realtimeSinceStartup < removalDeadline) yield return null;
        Assert.That(nail == null, Is.True,
            "Small target motion that preserves the attachment assessment does not cancel physical RMB extraction.");
    }

    [UnityTest]
    public IEnumerator HoldingCannotAutoSelectNextNailAndUiCancelsTheActiveAttempt()
    {
        QueueRmb(true); yield return new WaitForSecondsRealtime(1.7f);
        Assert.That(nail == null || Read(nail, "State").ToString() == "Loose", Is.True);
        Component second = CreateSecondNailAtAim(); yield return null;
        Assert.That(Read(second, "State").ToString(), Is.EqualTo("Partial"));
        Assert.That(Read(action, "IsCrowbarExtractionActiveOrPending"), Is.False);
        QueueRmb(false); yield return null; AimAtNail(second); QueueRmb(true); yield return null;
        player.GetComponent(PlayerInputType).GetType().GetMethod("SetGameplayUiOpen", PublicInstance).Invoke(
            player.GetComponent(PlayerInputType), new object[] { true });
        yield return null;
        Assert.That(ReadAvailability(second), Is.EqualTo("Available"));
        Assert.That(Read(action, "ActionMovementMultiplier"), Is.EqualTo(1f));
    }

    [UnityTest]
    public IEnumerator CrowbarNailHudUsesSharedProgressAndInfoOnlyHighlightWithoutReplacingHammerProgress()
    {
        Component nailResource = nail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"));
        Assert.That(nailResource, Is.Not.Null, "The nail prefab exposes the same BaseResourceNew current target used in gameplay.");
        CreatePresentationHud(nailResource);
        yield return null;
        SetCurrentTarget(nailResource);
        EvaluatePresentationHud();

        Assert.That(GetHudText(), Does.Contain("Hold to extract nail"));
        Assert.That(GetHudText(), Does.Not.Contain("Hammer nail"));
        Assert.That((bool)Read(hud, "CurrentTargetHasActionablePrompt"), Is.True);
        Assert.That((bool)Read(hud, "CurrentTargetHasCrowbarNailHighlight"), Is.True);
        Assert.That(hudProgressHolder.activeSelf, Is.False,
            "Crowbar presentation must not reuse the nail penetration circle.");
        Assert.That(hudProgressImage.fillAmount, Is.Zero);

        cameraObject.transform.position = player.transform.position - Vector3.forward * 0.5f;
        AimAtNail(nail);
        Physics.SyncTransforms();
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Contain("Move closer - Extract nail"));
        Assert.That(GetHudText(), Does.Not.Contain("Hold to extract nail"));
        Assert.That((bool)Read(hud, "CurrentTargetHasActionablePrompt"), Is.False,
            "The visible 1.3–2.0 m nail remains informational and cannot advertise an out-of-range action.");
        cameraObject.transform.position = player.transform.position;
        AimAtNail(nail);
        Physics.SyncTransforms();
        EvaluatePresentationHud();

        QueueRmb(true);
        yield return null;
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Contain("Extracting nail: "));
        Assert.That(GetHudText(), Does.Not.Contain("Awaiting server"),
            "An offline accepted attempt reads the actual shared nail snapshot immediately.");
        yield return new WaitForSecondsRealtime(0.25f);
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Match(@"Extracting nail: [1-9][0-9]*%"));
        Assert.That(hudProgressHolder.activeSelf, Is.False);
        Assert.That(hudProgressImage.fillAmount, Is.Zero);

        QueueRmb(false);
        yield return null;
        EvaluatePresentationHud();
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"));
        Assert.That(GetHudText(), Does.Contain("Hold to extract nail"));
        Assert.That(GetHudText(), Does.Not.Contain("Extracting nail:"), "Release clears local progress immediately.");

        GameObject otherActor = new("Crowbar Stage 4 other extraction owner");
        extras.Add(otherActor);
        Behaviour otherExecutor = otherActor.AddComponent<Camera>();
        Type handleType = Type.GetType("MountingNailExtractionHandle, Assembly-CSharp");
        object[] beginArgs = { otherExecutor, 1.5f, Activator.CreateInstance(handleType) };
        Assert.That((bool)NailType.GetMethod("TryBeginCrowbarExtraction", PublicInstance).Invoke(nail, beginArgs), Is.True);
        object otherHandle = beginArgs[2];
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Contain("Nail is being extracted by another player"));
        Assert.That(GetHudText(), Does.Not.Contain("Extracting nail:"), "Another executor's shared progress is never exposed.");
        Assert.That((bool)Read(hud, "CurrentTargetHasActionablePrompt"), Is.False,
            "Busy information must not be represented as an available PPM action.");
        Assert.That((bool)Read(hud, "CurrentTargetHasCrowbarNailHighlight"), Is.True);
        cameraObject.transform.position = player.transform.position - Vector3.forward * 0.5f;
        AimAtNail(nail);
        Physics.SyncTransforms();
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Contain("Nail is being extracted by another player"),
            "Range affects starting an extraction, not the truth of another executor's reservation.");
        Assert.That(GetHudText(), Does.Not.Contain("Move closer"));
        cameraObject.transform.position = player.transform.position;
        AimAtNail(nail);
        Physics.SyncTransforms();
        EvaluatePresentationHud();
        InvokeHighlightLateUpdate();
        Assert.That(ReadField(highlight, "currentTarget"), Is.SameAs(nailResource),
            "The info-only Crowbar nail target remains highlightable without an actionable prompt.");
        Assert.That((bool)NailType.GetMethod("CancelCrowbarExtraction", PublicInstance)
            .Invoke(nail, new[] { otherExecutor, otherHandle }), Is.True);

        SetNailState(nail, target.transform);
        target.transform.SetParent(null, true);
        target.name = "Crowbar Stage 4 loose nail target";
        NailType.GetMethod("SetState", PrivateInstance).Invoke(nail, new[] { Enum.Parse(NailStateType, "Loose") });
        SetCurrentTarget(nailResource);
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Contain("Pick up"), "Loose nails retain their normal interaction prompts while Crowbar is selected.");
        Assert.That(GetHudText(), Does.Not.Contain("Nail unavailable"));
        Assert.That(GetHudText(), Does.Not.Contain("Extract nail"));
        Assert.That((bool)Read(hud, "CurrentTargetHasCrowbarNailHighlight"), Is.False);

        SetNailState(nail, target.transform);
        NailType.GetMethod("SetState", PrivateInstance).Invoke(nail, new[] { Enum.Parse(NailStateType, "Prepared") });
        SetCurrentTarget(nailResource);
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Not.Contain("Nail unavailable"));
        Assert.That(GetHudText(), Does.Not.Contain("Extract nail"));
        Assert.That((bool)Read(hud, "CurrentTargetHasCrowbarNailHighlight"), Is.False);

        GameObject downedActor = new("Crowbar Stage 4 downed target");
        extras.Add(downedActor);
        Component downedHealth = downedActor.AddComponent(HealthType);
        HealthType.GetMethod("DamageReceived", new[] { typeof(float) }).Invoke(downedHealth, new object[] { 1000f });
        Assert.That(Read(downedHealth, "IsDowned"), Is.True);
        SetCurrentTarget(downedHealth);
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Not.Contain("Revive"), "Crowbar consumes PPM, so revive is never advertised as a secondary action.");
        Assert.That((bool)Read(hud, "CurrentTargetHasCrowbarNailHighlight"), Is.False);

        SetNailState(nail, target.transform);
        NailType.GetMethod("SetState", PrivateInstance).Invoke(nail, new[] { Enum.Parse(NailStateType, "Partial") });
        AimAtNail(nail);
        SetCurrentTarget(nailResource);
        EvaluatePresentationHud();

        ScriptableObject hammer = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        HashSet<int> priorObjects = CaptureEquippableItemInstanceIds();
        Assert.That(InventoryType.GetMethod("RemoveItem", PrivateInstance).Invoke(inventory, null), Is.EqualTo(true));
        TrackNewEquippableItemDrops(priorObjects);
        Assert.That(InventoryType.GetMethod("AddItem", PublicInstance).Invoke(inventory, new object[] { hammer }), Is.True);
        yield return null;
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Contain("Hammer nail"));
        Assert.That(hudProgressHolder.activeSelf, Is.True, "The Industrial Hammer retains its existing penetration circle.");
        Assert.That(hudProgressImage.fillAmount, Is.EqualTo(ReadFloat(nail, "PenetrationProgressNormalized")).Within(0.001f));

        SetGameplayUiOpen(true);
        EvaluatePresentationHud();
        InvokeHighlightLateUpdate();
        Assert.That((bool)Read(hud, "CurrentTargetHasCrowbarNailHighlight"), Is.False);
        Assert.That(ReadField(highlight, "currentTarget"), Is.Null,
            "A modal UI prevents stale target outlines.");
        SetCurrentTarget(null);
        SetGameplayUiOpen(false);
        EvaluatePresentationHud();
        Assert.That((bool)Read(hud, "CurrentTargetHasCrowbarNailHighlight"), Is.False,
            "Changing or losing the target clears extraction presentation state.");
        Assert.That(GetHudText(), Is.Empty, "Hiding the HUD clears its retained text as well as the visible root.");
    }

    [UnityTest]
    public IEnumerator CrowbarNailHudLabelsCompletedPanelAttachmentAsPermanent()
    {
        BuildCompletedPanelFixture(out Component panel, out Component panelState, out Component[] nails);
        panelState.GetType().GetMethod("Update", PrivateInstance).Invoke(panelState, null);
        Component targetNail = nails[0];
        object readState = Read(targetNail, "CrowbarExtractionReadState");
        Assert.That(Read(readState, "Availability").ToString(), Is.EqualTo("PermanentlyMounted"));

        foreach (Collider panelCollider in panel.GetComponentsInChildren<Collider>(true))
            panelCollider.enabled = false;
        for (int i = 1; i < nails.Length; i++)
            foreach (Collider nailCollider in nails[i].GetComponentsInChildren<Collider>(true))
                nailCollider.enabled = false;

        Component resource = targetNail.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"));
        Assert.That(resource, Is.Not.Null);
        CreatePresentationHud(resource);
        yield return null;
        object selectedItem = inventory.GetType().GetMethod("GetCurrentSelectedItem", PublicInstance).Invoke(inventory, null);
        Assert.That(Read(selectedItem, "itemType").ToString(), Is.EqualTo("Crowbar"),
            "The permanent-state presentation must be evaluated with the actual selected Crowbar.");
        object[] targetArgs = { resource, null };
        Assert.That((bool)hud.GetType().GetMethod("TryGetCrowbarNailTarget", PrivateInstance).Invoke(hud, targetArgs), Is.True,
            $"The real HUD target resolves its attached nail. nailState={Read(targetNail, "State")}, target={resource}, parent={resource.transform.parent}");
        GetAimSegment(targetNail, out Vector3 segmentStart, out Vector3 segmentEnd);
        cameraObject.transform.position = (segmentStart + segmentEnd) * 0.5f - Vector3.forward * 0.65f;
        cameraObject.transform.rotation = Quaternion.LookRotation((segmentStart + segmentEnd) * 0.5f - cameraObject.transform.position);
        Physics.SyncTransforms();
        AimAtNail(targetNail);
        Assert.That(ResolveAim(cameraObject.GetComponent<Camera>()), Is.SameAs(targetNail),
            "The test aims through the production visible-segment resolver at the protected attachment.");
        SetCurrentTarget(resource);
        EvaluatePresentationHud();
        Assert.That(GetHudText(), Does.Contain("Nail permanently mounted"));
        Assert.That(GetHudText(), Does.Not.Contain("Hold to extract nail"));
        Assert.That((bool)Read(hud, "CurrentTargetHasActionablePrompt"), Is.False);
        Assert.That((bool)Read(hud, "CurrentTargetHasCrowbarNailHighlight"), Is.True);
        InvokeHighlightLateUpdate();
        Assert.That(ReadField(highlight, "currentTarget"), Is.SameAs(resource));
        yield return null;
    }

    [Test]
    public void CrowbarProfileOwnsPositiveIndependentExtractionDuration()
    {
        object profile = Read(crowbar, "actionProfile");
        Assert.That(Read(profile, "crowbarExtractionDuration"), Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(Read(profile, "movementMultiplierDuringAction"), Is.EqualTo(0.65f).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator OrdinaryDamageKeepsReservationButDownedAndToolChangeCancelIt()
    {
        QueueRmb(true); yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"));
        HealthType.GetMethod("DamageReceived", new[] { typeof(float) }).Invoke(health, new object[] { 1f });
        yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"), "Ordinary damage alone does not interrupt extraction.");
        HealthType.GetMethod("DamageReceived", new[] { typeof(float) }).Invoke(health, new object[] { 1000f });
        yield return null;
        Assert.That(Read(health, "IsDowned"), Is.True);
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"), "Downed state cancels the reservation.");

        // A fresh fixture test covers tool switching without mutating any action timing or tolerance.
    }

    [UnityTest]
    public IEnumerator ToolChangeControllerDisableAndInputDisableReleaseTheReservation()
    {
        ScriptableObject hammer = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        QueueRmb(true); yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"));
        HashSet<int> priorObjects = CaptureEquippableItemInstanceIds();
        Assert.That(InventoryType.GetMethod("RemoveItem", PrivateInstance).Invoke(inventory, null), Is.EqualTo(true));
        TrackNewEquippableItemDrops(priorObjects);
        Assert.That(InventoryType.GetMethod("AddItem", PublicInstance).Invoke(inventory, new object[] { hammer }), Is.True);
        yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"), "Changing away from the Crowbar releases the reservation.");

        priorObjects = CaptureEquippableItemInstanceIds();
        Assert.That(InventoryType.GetMethod("RemoveItem", PrivateInstance).Invoke(inventory, null), Is.EqualTo(true));
        TrackNewEquippableItemDrops(priorObjects);
        Assert.That(InventoryType.GetMethod("AddItem", PublicInstance).Invoke(inventory, new[] { crowbar }), Is.True);
        QueueRmb(false); yield return null; QueueRmb(true); yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"));
        ((Behaviour)action).enabled = false; yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"), "Disabling the action controller cancels immediately.");
        ((Behaviour)action).enabled = true; yield return null;
        QueueRmb(false); yield return null; QueueRmb(true); yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"));
        ((Behaviour)player.GetComponent(PlayerInputType)).enabled = false; yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"), "Disabling gameplay input releases the reservation.");
    }

    private static HashSet<int> CaptureEquippableItemInstanceIds()
    {
        var ids = new HashSet<int>();
        foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(EquippableItemTypeInfo))
            ids.Add(item.GetInstanceID());
        return ids;
    }

    private void TrackNewEquippableItemDrops(HashSet<int> previousIds)
    {
        foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(EquippableItemTypeInfo))
            if (!previousIds.Contains(item.GetInstanceID())) extras.Add(((Component)item).gameObject);
    }

    [UnityTest]
    public IEnumerator LosingAimRangeOrLineOfSightCancelsTheCurrentTarget()
    {
        QueueRmb(true); yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"));
        cameraObject.transform.position = new Vector3(10f, 0f, 0f);
        AimAtNail(nail);
        yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"), "Moving the trusted local aim origin out of range cancels.");

        cameraObject.transform.position = Vector3.zero; AimAtNail(nail);
        QueueRmb(false); yield return null; QueueRmb(true); yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"));
        GameObject obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstruction.transform.position = (cameraObject.transform.position + GetAimPoint(nail)) * 0.5f;
        obstruction.transform.localScale = Vector3.one * 0.2f;
        yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"), "A newly blocked line of sight cancels the attempt.");
        UnityEngine.Object.Destroy(obstruction);
    }

    [UnityTest]
    public IEnumerator SameFrameLeftThenRightPreservesPrimaryAndRightThenLeftBlocksPrimaryUntilFreshPress()
    {
        QueueMouse(false, false);
        yield return null;
        QueueMouse(true, false);
        Assert.That(ReadField(action, "performAction"), Is.True,
            "The first physical left press must queue its primary action before a right press arrives in this frame.");
        QueueMouse(true, true);
        yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"),
            "A PPM arriving while the LPM action is pending is rejected instead of stealing the action.");
        Assert.That(ReadField(action, "performAction"), Is.True, "The original LPM action remains queued.");

        QueueMouse(false, false);
        float actionDeadline = Time.realtimeSinceStartup + 3f;
        while (((bool)Read(action, "IsActionInProgress") || (bool)ReadField(action, "performAction"))
               && Time.realtimeSinceStartup < actionDeadline) yield return null;
        Assert.That(Read(action, "IsActionInProgress"), Is.False, "The one queued LPM action completes within its authored timing.");
        Assert.That(ReadField(action, "performAction"), Is.False, "The queued LPM action is no longer pending.");
        Assert.That(Read(action, "IsActionInProgress"), Is.False);
        QueueMouse(false, true);
        QueueMouse(true, true);
        yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"), "An active RMB reservation wins over a same-frame LPM.");
        Assert.That(Read(action, "IsCrowbarExtractionActiveOrPending"), Is.True);
        Assert.That(Read(action, "IsActionInProgress"), Is.False, "The rejected LPM did not start a second action.");
        QueueMouse(false, false);
        yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"));
    }

    [UnityTest]
    public IEnumerator RealMovementUsesCrowbarMultiplierAndSprintRemainsBlocked()
    {
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Crowbar Stage 4 movement floor";
        floor.transform.position = new Vector3(0f, -0.1f, 0f);
        floor.transform.localScale = new Vector3(8f, 0.2f, 8f);
        extras.Add(floor);
        target.transform.position = new Vector3(0f, 0.55f, 1.2f);
        nailObject.transform.position = target.transform.position;
        SetNailState(nail, target.transform);
        MethodInfo applyNailPhysics = NailType.GetMethod("ApplyPhysicsForState", PrivateInstance);
        MethodInfo followEmbeddedTarget = NailType.GetMethod("FollowEmbeddedTarget", PrivateInstance);
        applyNailPhysics.Invoke(nail, new[] { Read(nail, "State") });
        followEmbeddedTarget.Invoke(nail, null);
        Physics.SyncTransforms();
        AimAtNail(nail);
        Type controllerType = Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp");
        Type starterInputsType = Type.GetType("StarterAssets.StarterAssetsInputs, Assembly-CSharp");
        Assert.That(controllerType, Is.Not.Null);
        Assert.That(starterInputsType, Is.Not.Null);
        player.AddComponent(starterInputsType);
        Component movement = player.AddComponent(controllerType);
        CharacterController characterController = player.GetComponent<CharacterController>();
        Assert.That(characterController, Is.Not.Null);
        characterController.height = 2f;
        characterController.radius = 0.5f;
        characterController.skinWidth = 0.02f;
        characterController.minMoveDistance = 0f;
        characterController.stepOffset = 0.25f;
        characterController.center = new Vector3(0f, 0.93f, 0f);
        controllerType.GetField("MoveSpeed", PublicInstance).SetValue(movement, 3.5f);
        controllerType.GetField("SprintSpeed", PublicInstance).SetValue(movement, 5f);
        controllerType.GetField("GroundLayers", PublicInstance).SetValue(movement, (LayerMask)1);
        controllerType.GetField("MaxStamina", PublicInstance).SetValue(movement, 10f);
        controllerType.GetField("_currentStamina", PrivateInstance).SetValue(movement, 10f);
        GameObject cameraTarget = new("Crowbar Stage 4 camera target");
        cameraTarget.transform.SetParent(player.transform, false);
        controllerType.GetField("CinemachineCameraTarget", PublicInstance).SetValue(movement, cameraTarget);
        player.transform.position = new Vector3(0f, 0.12f, 0.25f);
        for (int physicsStep = 0; physicsStep < 4 && ResolveAim(cameraObject.GetComponent<Camera>()) != nail; physicsStep++)
        {
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
            AimAtNail(nail);
        }
        Component movementAim = ResolveAim(cameraObject.GetComponent<Camera>());
        GetAimSegment(nail, out Vector3 movementAimStart, out Vector3 movementAimEnd);
        bool movementRayHit = Physics.Raycast(cameraObject.transform.position, cameraObject.transform.forward,
            out RaycastHit movementHit, 2f);
        Assert.That(movementAim, Is.SameAs(nail),
            $"The movement fixture must begin with the production ray on its visible nail. camera={cameraObject.transform.position}, forward={cameraObject.transform.forward}, segment={movementAimStart}..{movementAimEnd}, firstHit={(movementRayHit ? movementHit.collider.name : "<none>")}@{(movementRayHit ? movementHit.distance : -1f)}");
        QueueRmb(true);
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"),
            "The physical RMB callback must reserve the visible nail before the next frame.");
        yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"), "The local movement test begins only while its nail reservation is active.");
        Assert.That(Read(action, "ActionMovementMultiplier"), Is.EqualTo(0.65f).Within(0.001f));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.LeftShift));
        InputSystem.Update();
        Assert.That(keyboard.dKey.isPressed, Is.True, "The queued full KeyboardState must contain D.");
        Assert.That(keyboard.leftShiftKey.isPressed, Is.True, "The queued full KeyboardState must contain Shift.");
        Component localInput = player.GetComponent(PlayerInputType);
        Vector2 moveValue = (Vector2)localInput.GetType().GetMethod("GetMoveVectorValue", PublicInstance).Invoke(localInput, null);
        Assert.That(moveValue, Is.EqualTo(Vector2.right), "The real PlayerInputNew Move action must receive the fixture D key.");
        float moveUntil = Time.realtimeSinceStartup + 0.08f;
        while (Time.realtimeSinceStartup < moveUntil)
        {
            Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"), "Sprint remains checked only while extraction is valid.");
            AimAtNail(nail);
            yield return null;
        }
        Assert.That(Read(movement, "IsSprinting"), Is.False, "A real sprint input is rejected throughout extraction.");
        Vector3 start = player.transform.position;
        moveUntil = Time.realtimeSinceStartup + 0.12f;
        while (Time.realtimeSinceStartup < moveUntil)
        {
            Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"), "The movement measurement stays inside the active extraction session.");
            AimAtNail(nail);
            yield return null;
        }
        float traveled = Vector3.ProjectOnPlane(player.transform.position - start, Vector3.up).magnitude;
        Assert.That(traveled, Is.GreaterThan(0.15f).And.LessThan(1.5f),
            $"The real CharacterController moves while extraction is held at the reduced action speed. rawD={keyboard.dKey.isPressed}, rawShift={keyboard.leftShiftKey.isPressed}, move={localInput.GetType().GetMethod("GetMoveVectorValue", PublicInstance).Invoke(localInput, null)}, actionActive={Read(action, "IsCrowbarExtractionActiveOrPending")}, fpcEnabled={((Behaviour)movement).isActiveAndEnabled}, controller={characterController.enabled}, minMoveDistance={characterController.minMoveDistance}, stepOffset={characterController.stepOffset}, grounded={Read(movement, "Grounded")}, groundLayers={controllerType.GetField("GroundLayers", PublicInstance).GetValue(movement)}, horizontal={Read(movement, "HorizontalVelocity")}, collisionFlags={characterController.collisionFlags}, position={player.transform.position}, deltaTime={Time.deltaTime}, timeScale={Time.timeScale}, speed={Read(movement, "HorizontalSpeed")}");
        Assert.That(ReadAvailability(nail), Is.EqualTo("Reserved"));
        QueueRmb(false); yield return null;
        Assert.That(ReadAvailability(nail), Is.EqualTo("Available"));
    }

    private void CreatePresentationHud(Component targetComponent)
    {
        GameObject hudObject = new("Crowbar Stage 4 HUD fixture");
        hudObject.transform.SetParent(player.transform, false);
        hud = hudObject.AddComponent(Type.GetType("LookingAtComponentUI, Assembly-CSharp"));
        GameObject canvasObject = new("Crowbar Stage 4 canvas");
        canvasObject.transform.SetParent(hudObject.transform, false);
        canvasObject.AddComponent<Canvas>();
        GameObject visualObject = new("Crowbar Stage 4 HUD visual");
        visualObject.transform.SetParent(canvasObject.transform, false);
        visualRoot = visualObject;
        GameObject textObject = new("Crowbar Stage 4 HUD text");
        textObject.transform.SetParent(visualObject.transform, false);
        Type textMeshProType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        Assert.That(textMeshProType, Is.Not.Null, "The actual TMP UI component is available to the fixture.");
        hudText = textObject.AddComponent(textMeshProType);
        GameObject progressHolder = new("Crowbar Stage 4 penetration progress");
        progressHolder.transform.SetParent(visualObject.transform, false);
        hudProgressHolder = progressHolder;
        GameObject progressObject = new("Crowbar Stage 4 penetration image");
        progressObject.transform.SetParent(progressHolder.transform, false);
        hudProgressImage = progressObject.AddComponent<Image>();
        SetField(hud, "componentInfoText", hudText);
        SetField(hud, "visualRoot", visualRoot);
        SetField(hud, "playerInteraction", player.GetComponent(InteractionType));
        SetField(hud, "assemblingProgressBar", hudProgressImage);
        SetField(hud, "progressCircleHolder", hudProgressHolder);

        highlight = player.AddComponent(Type.GetType("PlayerTargetHighlightController, Assembly-CSharp"));
        SetField(highlight, "playerInteraction", player.GetComponent(InteractionType));
        SetField(highlight, "lookingAtUi", hud);
        Material source = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/TargetOutline.mat");
        Assert.That(source, Is.Not.Null);
        outlineMaterial = new Material(source);
        SetField(highlight, "outlineMaterial", outlineMaterial);
        SetCurrentTarget(targetComponent);
    }

    private void BuildCompletedPanelFixture(out Component panel, out Component panelState, out Component[] nails,
        int initialNailCount = 4)
    {
        Type sectionType = Type.GetType("BridgeDeckSection, Assembly-CSharp");
        Type stateType = Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        Type siteType = Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        Type panelType = Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        Type markerType = Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        Type bridgeType = Type.GetType("BridgeComponent, Assembly-CSharp");
        GameObject sectionRoot = new("Crowbar Stage 4 completed panel section");
        extras.Add(sectionRoot);
        sectionRoot.transform.position = Vector3.forward * 1.2f;
        Component section = sectionRoot.AddComponent(sectionType);
        GameObject stateObject = new("Crowbar Stage 4 panel state");
        extras.Add(stateObject);
        stateObject.transform.SetParent(sectionRoot.transform, false);
        panelState = stateObject.AddComponent(stateType);
        sectionType.GetField("physicalPanelState", PrivateInstance).SetValue(section, panelState);

        GameObject slotPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelBridgeComponent.prefab");
        GameObject slot = UnityEngine.Object.Instantiate(slotPrefab, sectionRoot.transform);
        extras.Add(slot);
        Component slotBridge = slot.GetComponent(bridgeType);
        if (slotBridge != null) ((Behaviour)slotBridge).enabled = false;
        foreach (Collider collider in slot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        Component site = slot.GetComponent(siteType);
        Array slots = Array.CreateInstance(sectionType.GetField("panelSlots", PrivateInstance).FieldType.GetElementType(), 1);
        slots.SetValue(site, 0);
        sectionType.GetField("panelSlots", PrivateInstance).SetValue(section, slots);
        siteType.GetMethod("ConfigureSection", PublicInstance).Invoke(site, new object[] { section, 0, false });

        GameObject girderRoot = new("Crowbar Stage 4 panel girder");
        extras.Add(girderRoot);
        girderRoot.AddComponent<Rigidbody>().isKinematic = true;
        girderRoot.AddComponent<BoxCollider>();
        Component girder = girderRoot.AddComponent(bridgeType);
        ((Behaviour)girder).enabled = false;
        bridgeType.GetField("componentID", PrivateInstance).SetValue(girder, 1);
        Array girders = Array.CreateInstance(sectionType.GetField("mainGirders", PrivateInstance).FieldType.GetElementType(), 1);
        girders.SetValue(girder, 0);
        sectionType.GetField("mainGirders", PrivateInstance).SetValue(section, girders);

        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
        GameObject panelObject = UnityEngine.Object.Instantiate(panelPrefab, sectionRoot.transform);
        extras.Add(panelObject);
        panel = panelObject.GetComponent(panelType);
        panelType.GetField("isPhysicalDeckPanel", PrivateInstance).SetValue(panel, true);
        panelObject.GetComponent(markerType);
        panelObject.transform.SetPositionAndRotation(slot.transform.position, slot.transform.rotation);
        Rigidbody panelBody = panelObject.GetComponent<Rigidbody>();
        panelBody.useGravity = false;
        panelBody.isKinematic = true;

        Vector2[] corners = { new(-0.5f, -0.6f), new(0.5f, -0.6f), new(-0.5f, 0.6f), new(0.5f, 0.6f) };
        nails = new Component[initialNailCount];
        for (int i = 0; i < nails.Length; i++) nails[i] = CreatePanelPairNail(panelObject.transform, girderRoot.transform, corners[i]);
        Physics.SyncTransforms();
        panelState.GetType().GetMethod("Update", PrivateInstance).Invoke(panelState, null);
    }

    private Component CreatePanelPairNail(Transform panel, Transform girder, Vector2 corner)
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        extras.Add(instance);
        Component result = instance.GetComponent(NailType);
        Vector3 entry = panel.TransformPoint(new Vector3(corner.x, 0.2f, corner.y));
        instance.transform.position = entry;
        NailType.GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(result,
            new object[] { panel, girder, entry, -panel.up });
        NailType.GetMethod("SetHitCounts", PrivateInstance).Invoke(result, new object[] { 1, 1, 0.02f });
        NailType.GetMethod("SetState", PrivateInstance).Invoke(result, new[] { Enum.Parse(NailStateType, "EmbeddedPair") });
        Assert.That(NailType.GetMethod("TryRegisterPairConnection", PrivateInstance).Invoke(result, null), Is.True);
        return result;
    }

    private GameObject CreatePairPhysicsTarget(string name)
    {
        GameObject result = new(name); extras.Add(result);
        result.transform.position = new Vector3(0f, 0f, 1.2f);
        Rigidbody body = result.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
        result.AddComponent<BoxCollider>().enabled = false;
        return result;
    }

    private Component CreatePairNailAt(Transform first, Transform second, Vector3 entry)
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab); extras.Add(instance);
        instance.transform.position = entry;
        Component result = instance.GetComponent(NailType);
        NailType.GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(result, new object[] { first, second, entry, Vector3.forward });
        NailType.GetMethod("SetHitCounts", PrivateInstance).Invoke(result, new object[] { 1, 1, 0.02f });
        NailType.GetMethod("SetState", PrivateInstance).Invoke(result, new[] { Enum.Parse(NailStateType, "EmbeddedPair") });
        Assert.That(NailType.GetMethod("TryRegisterPairConnection", PrivateInstance).Invoke(result, null), Is.True);
        return result;
    }

    private GameObject visualRoot;

    private void SetCurrentTarget(Component targetComponent)
    {
        player.GetComponent(InteractionType).GetType().GetMethod("SetCurrentTarget", PrivateInstance)
            .Invoke(player.GetComponent(InteractionType), new object[] { targetComponent });
    }

    private string GetHudText() => (string)hudText.GetType().GetProperty("text", PublicInstance).GetValue(hudText);

    private void EvaluatePresentationHud()
    {
        hud.GetType().GetMethod("Update", PrivateInstance).Invoke(hud, null);
    }

    private void InvokeHighlightLateUpdate()
    {
        highlight.GetType().GetMethod("LateUpdate", PrivateInstance).Invoke(highlight, null);
    }

    private void SetGameplayUiOpen(bool value)
    {
        player.GetComponent(PlayerInputType).GetType().GetMethod("SetGameplayUiOpen", PublicInstance)
            .Invoke(player.GetComponent(PlayerInputType), new object[] { value });
    }

    private void RefreshTargetThroughProductionRay()
    {
        player.GetComponent(InteractionType).GetType().GetMethod("LateUpdate", PrivateInstance)
            .Invoke(player.GetComponent(InteractionType), null);
    }

    private int GetHighlightTargetCount() => ((System.Collections.ICollection)ReadField(highlight, "targets")).Count;

    private static void SetField(object instance, string fieldName, object value)
    {
        instance.GetType().GetField(fieldName, PrivateInstance).SetValue(instance, value);
    }

    private Component CreateSecondNailAtAim()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab); instance.name = "Crowbar Stage 4 second nail";
        extras.Add(instance);
        Component result = instance.GetComponent(NailType);
        Transform secondTarget = new GameObject("Crowbar Stage 4 second target").transform;
        extras.Add(secondTarget.gameObject);
        secondTarget.position = new Vector3(0f, 0f, 1.2f);
        instance.transform.position = secondTarget.position;
        secondTarget.gameObject.AddComponent<Rigidbody>().isKinematic = true;
        secondTarget.gameObject.AddComponent<BoxCollider>().enabled = false;
        SetNailState(result, secondTarget);
        secondTarget.SetParent(instance.transform, true);
        return result;
    }

    private static void SetNailState(Component targetNail, Transform embeddedTarget)
    {
        targetNail.GetType().GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(targetNail,
            new object[] { embeddedTarget, null, embeddedTarget.position, Vector3.forward });
        targetNail.GetType().GetMethod("SetHitCounts", PrivateInstance).Invoke(targetNail, new object[] { 1, 3, 0.02f });
        targetNail.GetType().GetMethod("SetState", PrivateInstance).Invoke(targetNail,
            new[] { Enum.Parse(NailStateType, "Partial") });
    }

    private void AimAtNail(Component targetNail)
    {
        GetAimSegment(targetNail, out Vector3 start, out Vector3 end);
        cameraObject.transform.rotation = Quaternion.LookRotation((start + end) * 0.5f - cameraObject.transform.position);
    }

    private static Vector3 GetAimPoint(Component targetNail)
    {
        GetAimSegment(targetNail, out Vector3 start, out Vector3 end);
        return (start + end) * 0.5f;
    }

    private static void GetAimSegment(Component component, out Vector3 start, out Vector3 end)
    {
        object[] args = { Vector3.zero, Vector3.zero };
        Assert.That((bool)component.GetType().GetMethod("TryGetExposedAimSegment", PublicInstance).Invoke(component, args), Is.True);
        start = (Vector3)args[0]; end = (Vector3)args[1];
    }

    private void QueueRmb(bool pressed)
    {
        QueueMouse(false, pressed);
    }

    private void QueueMouse(bool left, bool right)
    {
        EnsureMouse();
        Assert.That(mouse.enabled, Is.True, "The fixture mouse must stay enabled even when the Editor GameView is unfocused.");
        Assert.That(IsOwnDeviceRegistered(mouse), Is.True, "Input events target this fixture's own virtual mouse.");
        MouseState state = default;
        state = state.WithButton(MouseButton.Left, left).WithButton(MouseButton.Right, right);
        InputSystem.QueueStateEvent(mouse, state);
        InputSystem.Update();
    }

    private void EnsureMouse()
    {
        if (mouse == null || !mouse.added || !IsOwnDeviceRegistered(mouse))
        {
            mouse = InputSystem.AddDevice<Mouse>();
            ConfigureInputDevices(player.GetComponent(PlayerInputType));
            QueueMouse(false, false);
        }
    }

    private void ConfigureInputDevices(Component playerInput)
    {
        object actions = PlayerInputType.GetField("playerGameInputActions", PrivateInstance).GetValue(playerInput);
        Assert.That(actions, Is.Not.Null, "PlayerInputNew must initialize its own generated action asset before binding fixture devices.");
        InputActionAsset asset = (InputActionAsset)actions.GetType().GetProperty("asset", PublicInstance).GetValue(actions);
        Assert.That(asset, Is.Not.Null);
        asset.devices = new InputDevice[] { keyboard, mouse };
        InputSystem.Update();
        object game = actions.GetType().GetProperty("Game", PublicInstance).GetValue(actions);
        InputAction alt = (InputAction)game.GetType().GetProperty("ActionAlt", PublicInstance).GetValue(game);
        Assert.That(ActionUsesDevice(alt, mouse), Is.True,
            "The generated action must resolve its real button binding to this fixture's own virtual mouse.");
    }

    private static bool ActionUsesDevice(InputAction inputAction, InputDevice expected)
    {
        foreach (InputControl control in inputAction.controls)
            if (ReferenceEquals(control.device, expected)) return true;
        return false;
    }

    private static bool IsOwnDeviceRegistered(InputDevice device)
    {
        foreach (InputDevice registered in InputSystem.devices)
            if (ReferenceEquals(registered, device)) return true;
        return false;
    }

    private static void RemoveOwnedDevice(InputDevice device)
    {
        if (device != null && IsOwnDeviceRegistered(device)) InputSystem.RemoveDevice(device);
    }

    private static object ReadField(object component, string field) => component.GetType().GetField(field, PrivateInstance).GetValue(component);

    private static object Read(object component, string property)
    {
        PropertyInfo info = component.GetType().GetProperty(property, PublicInstance);
        if (info != null) return info.GetValue(component);
        return component.GetType().GetField(property, PublicInstance).GetValue(component);
    }
    private static float ReadFloat(object component, string property) => Convert.ToSingle(Read(component, property));
    private static string ReadAvailability(Component component) => Read(Read(component, "CrowbarExtractionReadState"), "Availability").ToString();
    private Component ResolveAim(Camera camera)
    {
        MethodInfo method = NailAimResolverType.GetMethod("TryFindCandidate", BindingFlags.Static | PublicInstance);
        object[] args = { new Ray(camera.transform.position, camera.transform.forward), ReadFloat(crowbar, "actionRange"), player.transform, null, null, Vector3.zero };
        return (bool)method.Invoke(null, args) ? (Component)args[3] : null;
    }
}

internal sealed class CrowbarInputSettingsScope
{
    private InputSettings originalSettings;
    private InputSettings fixtureSettings;
    private InputSettings.UpdateMode originalUpdateMode;
    private InputSettings.BackgroundBehavior originalBackgroundBehavior;
    private InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
#if UNITY_EDITOR
    private string originalAssetPath;
    private string originalAssetHash;
#endif

    public void UseFocusedGameViewRouting()
    {
        originalSettings = InputSystem.settings;
        originalUpdateMode = originalSettings.updateMode;
        originalBackgroundBehavior = originalSettings.backgroundBehavior;
        originalEditorInputBehavior = originalSettings.editorInputBehaviorInPlayMode;
#if UNITY_EDITOR
        originalAssetPath = UnityEditor.AssetDatabase.GetAssetPath(originalSettings);
        Assert.That(string.IsNullOrEmpty(originalAssetPath), Is.False,
            "The test must clone the project's InputSettings asset rather than changing the persisted asset.");
        originalAssetHash = ComputeFileHash(System.IO.Path.GetFullPath(originalAssetPath));
#endif
        fixtureSettings = UnityEngine.Object.Instantiate(originalSettings);
        fixtureSettings.hideFlags = HideFlags.HideAndDontSave;
        fixtureSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        fixtureSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = fixtureSettings;
    }

    public void Restore()
    {
        if (fixtureSettings == null) return;
        InputSettings clone = fixtureSettings;
        try
        {
            InputSystem.settings = originalSettings;
            Assert.That(InputSystem.settings, Is.SameAs(originalSettings), "The exact original InputSettings reference must be restored.");
            Assert.That(originalSettings.updateMode, Is.EqualTo(originalUpdateMode));
            Assert.That(originalSettings.backgroundBehavior, Is.EqualTo(originalBackgroundBehavior));
            Assert.That(originalSettings.editorInputBehaviorInPlayMode, Is.EqualTo(originalEditorInputBehavior));
#if UNITY_EDITOR
            Assert.That(ComputeFileHash(System.IO.Path.GetFullPath(originalAssetPath)), Is.EqualTo(originalAssetHash),
                "The project's serialized InputSettings asset remains byte-identical.");
#endif
        }
        finally
        {
            UnityEngine.Object.Destroy(clone);
            fixtureSettings = null;
            originalSettings = null;
        }
    }

#if UNITY_EDITOR
    private static string ComputeFileHash(string path)
    {
        using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            return System.BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", string.Empty);
    }
#endif
}
