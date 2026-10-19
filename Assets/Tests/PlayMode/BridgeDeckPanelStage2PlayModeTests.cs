using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class BridgeDeckPanelStage2PlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private readonly List<GameObject> roots = new();
    private readonly List<ScriptableObject> profiles = new();
    private readonly List<NetworkObject> spawnedObjects = new();
    private NetworkManager host;
    private NetworkManager client;
    private NetworkManager priorSingleton;

    [UnityTest]
    public IEnumerator RealPlacementAndHammeredPairsFillFourQuartersAndCompleteWithoutMovingPanel()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses authored Unity prefabs.");
        yield break;
#else
        GameObject sectionRoot = new("Stage 2 physical section test");
        roots.Add(sectionRoot);
        Component section = sectionRoot.AddComponent(System.Type.GetType("BridgeDeckSection, Assembly-CSharp"));
        GameObject stateRoot = new("Stage 2 network state carrier");
        stateRoot.transform.SetParent(sectionRoot.transform, false);
        Component sectionState = stateRoot.AddComponent(System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp"));

        GameObject slotPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelBridgeComponent.prefab");
        Assert.That(slotPrefab, Is.Not.Null);
        GameObject slot = Object.Instantiate(slotPrefab);
        roots.Add(slot);
        slot.transform.SetParent(sectionRoot.transform, false);
        slot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Component slotBridge = slot.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"));
        if (slotBridge != null) ((Behaviour)slotBridge).enabled = false;
        Component site = slot.GetComponent(System.Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp"));
        Assert.That(site, Is.Not.Null);
        System.Type helperUiType = System.Type.GetType("BridgeComponentNeededEquippableItemUI, Assembly-CSharp");
        if (helperUiType != null)
            foreach (Component helper in slot.GetComponentsInChildren(helperUiType, true)) ((Behaviour)helper).enabled = false;
        FieldInfo slotsField = section.GetType().GetField("panelSlots", PrivateInstance);
        System.Array slots = System.Array.CreateInstance(slotsField.FieldType.GetElementType(), 1);
        slots.SetValue(site, 0);
        slotsField.SetValue(section, slots);
        site.GetType().GetMethod("ConfigureSection", PublicInstance).Invoke(site, new object[] { section, 0, false });

        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
        Assert.That(panelPrefab, Is.Not.Null);
        GameObject panelRoot = Object.Instantiate(panelPrefab);
        roots.Add(panelRoot);
        Component panelMountable = panelRoot.GetComponent(System.Type.GetType("MountableBridgeComponent, Assembly-CSharp"));
        Assert.That(panelMountable, Is.Not.Null);
        panelMountable.GetType().GetField("isPhysicalDeckPanel", PrivateInstance).SetValue(panelMountable, true);
        System.Type markerType = System.Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        Component panelMarker = panelRoot.GetComponent(markerType);
        if (panelMarker == null) panelMarker = panelRoot.AddComponent(markerType);
        panelRoot.transform.SetPositionAndRotation(site.transform.position, site.transform.rotation);
        Rigidbody panelBody = panelRoot.GetComponent<Rigidbody>();
        Assert.That(panelBody, Is.Not.Null);
        panelBody.useGravity = false;
        panelBody.isKinematic = true;
        Assert.That((bool)panelMountable.GetType().GetProperty("CanBePickedUp", PublicInstance).GetValue(panelMountable), Is.True,
            "A loose physical deck panel remains pickable before it is nailed.");

        GameObject girder = new("Declared Stage 2 main girder target");
        roots.Add(girder);
        girder.transform.position = panelRoot.transform.position - panelRoot.transform.up * 0.05f;
        Rigidbody girderBody = girder.AddComponent<Rigidbody>();
        girderBody.useGravity = false;
        girderBody.isKinematic = true;
        BoxCollider girderCollider = girder.AddComponent<BoxCollider>();
        girderCollider.size = new Vector3(2.2f, 0.1f, 2.0f);
        Component girderComponent = girder.AddComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"));
        ((Behaviour)girderComponent).enabled = false;
        girderCollider.enabled = true;
        FieldInfo girdersField = section.GetType().GetField("mainGirders", PrivateInstance);
        System.Array girders = System.Array.CreateInstance(girdersField.FieldType.GetElementType(), 1);
        girders.SetValue(girderComponent, 0);
        girdersField.SetValue(section, girders);
        Physics.SyncTransforms();

        Component nail = null;
        GameObject nailObject = null;
        Vector3 panelPosition = panelRoot.transform.position;
        Quaternion panelRotation = panelRoot.transform.rotation;
        Vector3 slotPosition = slot.transform.position;
        Quaternion slotRotation = slot.transform.rotation;
        bool slotDisplaced = false;
        yield return null;
        Assert.That(ReadQuarterMask(sectionState, panelMarker), Is.EqualTo((byte)0), "A loose physical panel has an independent 0/4 assessment.");
        CollectionAssert.Contains(ReadPromptDescriptions(panelMarker), "Placement correct",
            "A correctly positioned panel receives positive placement feedback before any fasteners are installed.");
        CollectionAssert.Contains(ReadPromptDescriptions(panelMarker),
            "Fastenings: 0/4 - missing back-left, back-right, front-left, front-right");
        slot.transform.position = slotPosition + Vector3.up * 0.04f;
        Physics.SyncTransforms();
        yield return null;
        Assert.That(ReadFailure(sectionState, panelMarker), Is.EqualTo("HeightOffset"));
        CollectionAssert.Contains(ReadPromptDescriptions(panelMarker), "Placement: height out of alignment");
        slot.transform.position = slotPosition;
        slot.transform.rotation = slotRotation * Quaternion.Euler(0f, 4f, 0f);
        Physics.SyncTransforms();
        yield return null;
        Assert.That(ReadFailure(sectionState, panelMarker), Is.EqualTo("Orientation"));
        CollectionAssert.Contains(ReadPromptDescriptions(panelMarker), "Placement: incorrect orientation");
        slot.transform.rotation = slotRotation;

        GameObject earlyNeighborRoot = Object.Instantiate(panelPrefab);
        roots.Add(earlyNeighborRoot);
        earlyNeighborRoot.transform.SetPositionAndRotation(panelPosition, panelRotation);
        Component earlyNeighborMountable = earlyNeighborRoot.GetComponent(System.Type.GetType("MountableBridgeComponent, Assembly-CSharp"));
        earlyNeighborMountable.GetType().GetField("isPhysicalDeckPanel", PrivateInstance).SetValue(earlyNeighborMountable, true);
        Rigidbody earlyNeighborBody = earlyNeighborRoot.GetComponent<Rigidbody>();
        earlyNeighborBody.useGravity = false;
        earlyNeighborBody.isKinematic = true;
        Physics.SyncTransforms();
        yield return null;
        Assert.That(ReadFailure(sectionState, panelMarker), Is.EqualTo("NeighborPenetration"));
        CollectionAssert.Contains(ReadPromptDescriptions(panelMarker), "Placement: overlapping adjacent panel",
            "Neighbor occupancy is assessed before any fastener exists.");
        earlyNeighborRoot.SetActive(false);
        Physics.SyncTransforms();
        yield return null;

        GameObject feedbackUiRoot = new("Panel placement feedback presenter");
        feedbackUiRoot.SetActive(false);
        roots.Add(feedbackUiRoot);
        Component feedbackInput = feedbackUiRoot.AddComponent(System.Type.GetType("PlayerInputNew, Assembly-CSharp"));
        feedbackInput.GetType().GetField("inputInitialized", PrivateInstance).SetValue(feedbackInput, true);
        Component feedbackInteraction = feedbackUiRoot.AddComponent(System.Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        Component feedbackUi = feedbackUiRoot.AddComponent(System.Type.GetType("LookingAtComponentUI, Assembly-CSharp"));
        feedbackUi.GetType().GetField("playerInteraction", PrivateInstance).SetValue(feedbackUi, feedbackInteraction);
        feedbackUi.GetType().GetField("playerInput", PrivateInstance).SetValue(feedbackUi, feedbackInput);
        MethodInfo refreshPanelFeedback = feedbackUi.GetType().GetMethod("RefreshPanelPlacementFeedback", PrivateInstance);
        MethodInfo showPlacementStatus = feedbackUi.GetType().GetMethod("ShowPlacementStatus", PrivateInstance);
        Component outlineComponent = slot.GetComponentInChildren(System.Type.GetType("BridgeDeckPanelSlotOutline, Assembly-CSharp"), true);
        LineRenderer slotOutline = outlineComponent.GetComponent<LineRenderer>();
        GameObject unrelatedHeldObject = new("Unrelated held test object");
        roots.Add(unrelatedHeldObject);
        feedbackInteraction.GetType().GetField("_pickedUpGameObject", PrivateInstance).SetValue(feedbackInteraction, unrelatedHeldObject);
        refreshPanelFeedback.Invoke(feedbackUi, new object[] { panelMarker });
        AssertOutlineRgb(slotOutline, 0.18f, 0.95f, 0.25f, "The exact matching slot renders green for a valid 0/4 panel.");
        FieldInfo uiPromptsField = feedbackUi.GetType().GetField("prompts", PrivateInstance);
        IList uiPrompts = (IList)uiPromptsField.GetValue(feedbackUi);
        uiPrompts.Clear();
        feedbackUi.GetType().GetMethod("AddPromptsForCurrentState", PrivateInstance).Invoke(feedbackUi, new object[] { panelMarker });
        List<string> heldPromptDescriptions = new();
        foreach (object prompt in uiPrompts)
            heldPromptDescriptions.Add((string)prompt.GetType().GetField("Description", PublicInstance).GetValue(prompt));
        CollectionAssert.Contains(heldPromptDescriptions, "Drop");
        CollectionAssert.Contains(heldPromptDescriptions, "Placement correct");
        CollectionAssert.Contains(heldPromptDescriptions, "Fastenings: 0/4 - missing back-left, back-right, front-left, front-right");
        CollectionAssert.DoesNotContain(heldPromptDescriptions, "Pick up");
        outlineComponent.GetType().GetProperty("enabled", PublicInstance).SetValue(outlineComponent, false);
        refreshPanelFeedback.Invoke(feedbackUi, new object[] { panelMarker });
        AssertOutlineRgb(slotOutline, 1f, 1f, 1f, "A disabled slot outline cannot reacquire focus tint.");
        outlineComponent.GetType().GetProperty("enabled", PublicInstance).SetValue(outlineComponent, true);
        refreshPanelFeedback.Invoke(feedbackUi, new object[] { panelMarker });
        AssertOutlineRgb(slotOutline, 0.18f, 0.95f, 0.25f, "A re-enabled focused outline reacquires its local feedback.");
        feedbackInteraction.GetType().GetField("_pickedUpGameObject", PrivateInstance).SetValue(feedbackInteraction, null);
        panelMountable.GetType().GetField("isPickedUp", PrivateInstance).SetValue(panelMountable, true);
        refreshPanelFeedback.Invoke(feedbackUi, new object[] { panelMarker });
        AssertOutlineRgb(slotOutline, 1f, 1f, 1f, "A carried target panel never colors its slot as a released placement.");
        CollectionAssert.Contains(ReadPromptDescriptions(panelMarker), "Placement: release panel to assess");
        CollectionAssert.Contains(ReadPromptDescriptions(panelMarker), "Fastenings: 0/4 - missing back-left, back-right, front-left, front-right");
        panelMountable.GetType().GetField("isPickedUp", PrivateInstance).SetValue(panelMountable, false);
        refreshPanelFeedback.Invoke(feedbackUi, new object[] { panelMarker });

        slot.transform.position = slotPosition + slot.transform.right * 0.1f;
        Physics.SyncTransforms();
        yield return null;
        Assert.That(ReadFailure(sectionState, panelMarker), Is.EqualTo("HorizontalOffset"));
        CollectionAssert.Contains(ReadPromptDescriptions(panelMarker), "Placement: position out of alignment",
            "Placement failure remains visible independently of quarter progress.");
        refreshPanelFeedback.Invoke(feedbackUi, new object[] { panelMarker });
        AssertOutlineRgb(slotOutline, 1f, 0.55f, 0.1f, "The same exact slot renders orange for an invalid pose.");
        refreshPanelFeedback.Invoke(feedbackUi, new object[] { null });
        AssertOutlineRgb(slotOutline, 1f, 1f, 1f, "Losing the panel target restores the neutral outline color.");
        refreshPanelFeedback.Invoke(feedbackUi, new object[] { panelMarker });
        showPlacementStatus.Invoke(feedbackUi, null);
        AssertOutlineRgb(slotOutline, 1f, 1f, 1f, "Entering F placement mode clears panel focus feedback.");

        slot.transform.position = slotPosition;
        slot.transform.rotation = slotRotation;
        Physics.SyncTransforms();
        yield return null;

        byte[] expectedQuarterBits = { 1, 3, 7, 15 };
        Vector2[] entries = { new(-0.65f, -0.65f), new(0.65f, -0.65f), new(-0.65f, 0.65f), new(0.65f, 0.65f) };
        for (int i = 0; i < entries.Length; i++)
        {
            nail = CreateNail(out nailObject);
            roots.Add(nailObject);
            MountNailOnPanel(nail, nailObject, panelRoot.transform, entries[i]);
            nail.GetType().GetMethod("OnSingleCarryPlacementCommitted", PublicInstance)
                .Invoke(nail, new object[] { null });
            Assert.That(nail.GetType().GetProperty("State", PublicInstance).GetValue(nail).ToString(), Is.EqualTo("Prepared"));
            if (i == 0) Assert.That((bool)panelMountable.GetType().GetProperty("CanBePickedUp", PublicInstance).GetValue(panelMountable), Is.True,
                "The first prepared fastener does not lock panel pickup.");
            if (i == 0) Assert.That(girder.GetComponent<FixedJoint>(), Is.Null, "A prepared fastener does not create the shared pair joint.");
            object hammer = System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer");
            for (int hit = 0; hit < 3; hit++)
            {
                Assert.That(nail.GetType().GetMethod("TryApplyHammerHit", PublicInstance).Invoke(nail, new[] { hammer }), Is.EqualTo(true));
                if (hit < 2)
                {
                    Assert.That(nail.GetType().GetProperty("State", PublicInstance).GetValue(nail).ToString(), Is.EqualTo("Partial"));
                    if (i == 0)
                    {
                        Assert.That((bool)panelMountable.GetType().GetProperty("CanBePickedUp", PublicInstance).GetValue(panelMountable), Is.True,
                            "The first partially embedded fastener does not lock panel pickup.");
                        Assert.That(girder.GetComponent<FixedJoint>(), Is.Null,
                            "A partially embedded fastener does not create the shared pair joint.");
                    }
                    yield return null;
                    byte previousMask = i == 0 ? (byte)0 : expectedQuarterBits[i - 1];
                    Assert.That(ReadQuarterMask(sectionState, panelMarker), Is.EqualTo(previousMask),
                        "Prepared/Partial fasteners do not count until the real EmbeddedPair transition.");
                }
            }
            Assert.That(nail.GetType().GetProperty("State", PublicInstance).GetValue(nail).ToString(), Is.EqualTo("EmbeddedPair"),
                "Each counted fastener must reach the real EmbeddedPair state through hammer validation.");
            if (i == 0)
            {
                Assert.That((bool)panelMountable.GetType().GetProperty("CanBePickedUp", PublicInstance).GetValue(panelMountable), Is.False,
                    "The first fully embedded nail still locks pickup through the existing connection registry.");
                FixedJoint sharedJoint = girder.GetComponent<FixedJoint>();
                Assert.That(sharedJoint, Is.Not.Null);
                Assert.That(sharedJoint.connectedBody, Is.EqualTo(panelBody));
            }
            yield return null;
            Assert.That(ReadQuarterMask(sectionState, panelMarker), Is.EqualTo(expectedQuarterBits[i]));
            Assert.That(ReadPromptDescriptions(panelMarker).Exists(text => text.StartsWith($"Fastenings: {i + 1}/4")), Is.True);

            if (i == 0)
            {
                // A second real embedded pair in the same quadrant must not replace a missing quadrant.
                Component duplicateNail = CreateNail(out GameObject duplicateNailObject);
                roots.Add(duplicateNailObject);
                MountNailOnPanel(duplicateNail, duplicateNailObject, panelRoot.transform, entries[i]);
                duplicateNail.GetType().GetMethod("OnSingleCarryPlacementCommitted", PublicInstance)
                    .Invoke(duplicateNail, new object[] { null });
                object duplicateHammer = System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer");
                for (int hit = 0; hit < 3; hit++)
                    Assert.That(duplicateNail.GetType().GetMethod("TryApplyHammerHit", PublicInstance).Invoke(duplicateNail, new[] { duplicateHammer }), Is.EqualTo(true));
                Assert.That(duplicateNail.GetType().GetProperty("State", PublicInstance).GetValue(duplicateNail).ToString(), Is.EqualTo("EmbeddedPair"));
                yield return null;
                Assert.That(ReadQuarterMask(sectionState, panelMarker), Is.EqualTo((byte)1), "Duplicate hardware in one quadrant cannot satisfy another quadrant.");

                // Temporarily revoke the declared main girder: the live assessment must lose the bit and restore it when valid again.
                System.Array emptyGirders = System.Array.CreateInstance(girdersField.FieldType.GetElementType(), 0);
                girdersField.SetValue(section, emptyGirders);
                yield return null;
                Assert.That(ReadQuarterMask(sectionState, panelMarker), Is.EqualTo((byte)0));
                girdersField.SetValue(section, girders);
                yield return null;
                Assert.That(ReadQuarterMask(sectionState, panelMarker), Is.EqualTo((byte)1));
            }

            if (i == 2)
            {
                // Full hardware does not complete until the panel is actually in the allowed pose.
                slot.transform.position = slotPosition + slot.transform.right * 0.1f;
                Physics.SyncTransforms();
                yield return null;
                Assert.That(ReadQuarterMask(sectionState, panelMarker), Is.EqualTo((byte)7));
                Assert.That(IsPanelComplete(sectionState, panelMarker), Is.False);
                slotDisplaced = true;
            }

            if (i == 3 && slotDisplaced)
            {
                Assert.That(ReadQuarterMask(sectionState, panelMarker), Is.EqualTo((byte)15));
                Assert.That(IsPanelComplete(sectionState, panelMarker), Is.False);
                Assert.That(ReadFailure(sectionState, panelMarker), Is.EqualTo("HorizontalOffset"),
                    "A full quarter mask at an invalid pose reports the pose failure and does not complete.");
                slot.transform.position = slotPosition;
                Physics.SyncTransforms();
                MethodInfo resetIncompletePanel = sectionState.GetType().GetMethod(
                    "TryResetIncompletePanelWithHammer", PrivateInstance);
                Assert.That(resetIncompletePanel, Is.Not.Null);
                Vector3 velocityBeforeRaceReset = panelBody.linearVelocity;
                Assert.That(resetIncompletePanel.Invoke(sectionState, new object[] { panelMarker, panelRoot.transform.forward }),
                    Is.EqualTo(false),
                    "A final valid pair that completes during the reset call's fresh assessment wins the same-frame race.");
                Assert.That(IsPanelComplete(sectionState, panelMarker), Is.True);
                Assert.That(panelMountable.GetType().GetProperty("IsPickedUp", PublicInstance).GetValue(panelMountable), Is.EqualTo(false));
                Assert.That(panelBody.linearVelocity, Is.EqualTo(velocityBeforeRaceReset),
                    "A rejected reset does not add impulse or destroy embedded hardware.");
                yield return null;
            }
        }

        Assert.That(panelRoot.transform.position, Is.EqualTo(panelPosition));
        Assert.That(panelRoot.transform.rotation, Is.EqualTo(panelRotation));
        yield return null;
        Assert.That(IsPanelComplete(sectionState, panelMarker), Is.True,
            "Four distinct real hammered pairs on the declared main girder complete the panel without moving it.");
        CollectionAssert.Contains(ReadPromptDescriptions(panelMarker), "Deck panel secured");
        slot.transform.position = slotPosition + slot.transform.right * 0.1f;
        Physics.SyncTransforms();
        yield return null;
        Assert.That(IsPanelComplete(sectionState, panelMarker), Is.True,
            "A committed physical panel remains complete if the slot later moves; completion lasts until level restart.");
        MethodInfo completedPanelReset = sectionState.GetType().GetMethod("TryResetIncompletePanelWithHammer", PrivateInstance);
        Vector3 velocityBeforeCompletedReset = panelBody.linearVelocity;
        Assert.That(completedPanelReset.Invoke(sectionState, new object[] { panelMarker, panelRoot.transform.forward }), Is.EqualTo(false),
            "A durable completion claim protects the panel from later hammer resets.");
        Assert.That(panelBody.linearVelocity, Is.EqualTo(velocityBeforeCompletedReset));
        // Leave the completed panel's original slot displaced so the new incomplete section is strictly nearer.
        GameObject competingSection = CreateCompetingIncompleteSection(panelMarker, panelPosition, site, roots);
        System.Type assessmentStateType = System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        MethodInfo findOwner = assessmentStateType.GetMethod("FindOwningSection", BindingFlags.Static | BindingFlags.NonPublic);
        Component competingState = competingSection.GetComponentInChildren(assessmentStateType, true);
        Assert.That(findOwner.Invoke(null, new object[] { panelMarker }), Is.SameAs(competingState),
            "The competing incomplete section must be the actual nearest owner for this regression.");
        MethodInfo resolveAssessment = null;
        foreach (MethodInfo candidate in assessmentStateType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            if (candidate.Name == "TryGetAssessmentFor" && candidate.GetParameters().Length == 3) resolveAssessment = candidate;
        object[] resolvedAssessment = { panelMarker, null, null };
        Assert.That((bool)resolveAssessment.Invoke(null, resolvedAssessment), Is.True);
        Assert.That((bool)resolvedAssessment[1].GetType().GetField("Complete").GetValue(resolvedAssessment[1]), Is.True);
        Assert.That(resolvedAssessment[2], Is.SameAs(site),
            "A durable completed claim resolves to its original slot before a nearer incomplete section record.");
        Assert.That(competingSection, Is.Not.Null);
        slot.transform.position = slotPosition;
        Physics.SyncTransforms();
        competingSection.SetActive(false);
        foreach (Collider firstPanelCollider in panelRoot.GetComponentsInChildren<Collider>(true)) firstPanelCollider.enabled = false;

        // A second physical panel with four real pairs can assess the same slot but cannot replace its completed claimant.
        GameObject secondPanelRoot = Object.Instantiate(panelPrefab);
        roots.Add(secondPanelRoot);
        secondPanelRoot.transform.SetPositionAndRotation(panelPosition, panelRotation);
        GameObject offsetColliderRoot = new("Rotated-source preview collider");
        roots.Add(offsetColliderRoot);
        offsetColliderRoot.transform.SetParent(secondPanelRoot.transform, false);
        offsetColliderRoot.transform.localPosition = new Vector3(1f, 0.1f, 0f);
        BoxCollider offsetCollider = offsetColliderRoot.AddComponent<BoxCollider>();
        offsetCollider.size = new Vector3(0.8f, 0.1f, 0.1f);
        offsetCollider.enabled = false;
        Component secondPanel = secondPanelRoot.GetComponent(System.Type.GetType("MountableBridgeComponent, Assembly-CSharp"));
        Component secondMarker = secondPanelRoot.GetComponent(markerType);
        Assert.That(secondPanel, Is.Not.Null);
        Assert.That(secondMarker, Is.Not.Null);
        secondPanel.GetType().GetField("isPhysicalDeckPanel", PrivateInstance).SetValue(secondPanel, true);
        Rigidbody secondBody = secondPanelRoot.GetComponent<Rigidbody>();
        secondBody.useGravity = false;
        secondBody.isKinematic = true;
        Physics.SyncTransforms();
        yield return null;
        Assert.That(ReadQuarterMask(sectionState, secondMarker), Is.EqualTo((byte)0));
        Assert.That(ReadFailure(sectionState, secondMarker), Is.EqualTo("SlotOccupied"));
        CollectionAssert.Contains(ReadPromptDescriptions(secondMarker), "Placement: slot occupied",
            "The occupied slot is reported before the second panel has any fasteners.");
        for (int i = 0; i < entries.Length; i++)
        {
            Component secondNail = CreateNail(out GameObject secondNailObject);
            roots.Add(secondNailObject);
            MountNailOnPanel(secondNail, secondNailObject, secondPanelRoot.transform, entries[i]);
            secondNail.GetType().GetMethod("OnSingleCarryPlacementCommitted", PublicInstance)
                .Invoke(secondNail, new object[] { null });
            object secondHammer = System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer");
            for (int hit = 0; hit < 3; hit++)
                Assert.That(secondNail.GetType().GetMethod("TryApplyHammerHit", PublicInstance).Invoke(secondNail, new[] { secondHammer }), Is.EqualTo(true));
            Assert.That(secondNail.GetType().GetProperty("State", PublicInstance).GetValue(secondNail).ToString(), Is.EqualTo("EmbeddedPair"));
        }
        yield return null;
        Assert.That(ReadQuarterMask(sectionState, secondMarker), Is.EqualTo((byte)15));
        Assert.That(ReadFailure(sectionState, secondMarker), Is.EqualTo("SlotOccupied"));
        Assert.That(IsPanelComplete(sectionState, secondMarker), Is.False,
            "Slot occupancy prevents a second panel from completing even with four independently hammered quadrants.");

        GameObject neighboringPanelRoot = Object.Instantiate(panelPrefab);
        roots.Add(neighboringPanelRoot);
        Component neighboringPanel = neighboringPanelRoot.GetComponent(System.Type.GetType("MountableBridgeComponent, Assembly-CSharp"));
        neighboringPanel.GetType().GetField("isPhysicalDeckPanel", PrivateInstance).SetValue(neighboringPanel, true);
        Rigidbody neighboringBody = neighboringPanelRoot.GetComponent<Rigidbody>();
        neighboringBody.useGravity = false;
        neighboringBody.isKinematic = true;
        Collider secondPanelCollider = FindEnabledCollider(secondPanelRoot);
        Collider neighboringCollider = FindEnabledCollider(neighboringPanelRoot);
        Assert.That(secondPanelCollider, Is.Not.Null);
        Assert.That(neighboringCollider, Is.Not.Null);
        float combinedHalfWidth = secondPanelCollider.bounds.extents.x + neighboringCollider.bounds.extents.x;
        MethodInfo acceptsNeighbor = sectionState.GetType().GetMethod("HasAcceptableNeighborPenetration", PrivateInstance);
        neighboringPanelRoot.transform.position = secondPanelRoot.transform.position
            + Vector3.right * (combinedHalfWidth - 0.001f);
        Physics.SyncTransforms();
        Assert.That((bool)acceptsNeighbor.Invoke(sectionState, new[] { secondPanel }), Is.True,
            "A real Unity collider penetration at the 1 mm limit is tolerated.");
        neighboringPanelRoot.transform.position = secondPanelRoot.transform.position
            + Vector3.right * (combinedHalfWidth - 0.0011f);
        Physics.SyncTransforms();
        Assert.That((bool)acceptsNeighbor.Invoke(sectionState, new[] { secondPanel }), Is.False,
            "A real Unity collider penetration above 1 mm is rejected.");

        MethodInfo acceptsCandidate = sectionState.GetType().GetMethod("HasAcceptableNeighborPenetrationAtPose", PrivateInstance);
        Assert.That(acceptsCandidate, Is.Not.Null);
        Assert.That(offsetCollider.transform, Is.Not.EqualTo(secondPanelRoot.transform),
            "The regression exercises an explicitly authored offset child collider, not only the panel root.");
        offsetCollider.enabled = true;
        Vector3 unchangedPanelPosition = secondPanelRoot.transform.position;
        Quaternion unchangedPanelRotation = secondPanelRoot.transform.rotation;
        byte unchangedQuarterMask = ReadQuarterMask(sectionState, secondMarker);
        string unchangedAssessment = ReadFailure(sectionState, secondMarker);
        neighboringPanelRoot.transform.position = unchangedPanelPosition + Vector3.up * 0.15f;
        neighboringPanelRoot.transform.rotation = Quaternion.Euler(0f, 17f, 0f);
        Physics.SyncTransforms();
        Assert.That((bool)acceptsCandidate.Invoke(sectionState,
            new object[] { secondPanel, unchangedPanelPosition, unchangedPanelRotation }), Is.False,
            "Candidate-pose penetration must query the actual elevated child collider bounds, including their offset and rotation.");
        Assert.That(secondPanelRoot.transform.position, Is.EqualTo(unchangedPanelPosition));
        Assert.That(secondPanelRoot.transform.rotation, Is.EqualTo(unchangedPanelRotation));
        Assert.That(ReadQuarterMask(sectionState, secondMarker), Is.EqualTo(unchangedQuarterMask),
            "A preview penetration query must not update authoritative quarter state.");
        Assert.That(ReadFailure(sectionState, secondMarker), Is.EqualTo(unchangedAssessment),
            "A preview penetration query must not update the released panel assessment.");

        foreach (Collider collider in secondPanelRoot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        offsetCollider.enabled = true;
        foreach (Collider collider in neighboringPanelRoot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        BoxCollider edgeNeighbor = neighboringPanelRoot.gameObject.AddComponent<BoxCollider>();
        edgeNeighbor.size = Vector3.one * 0.1f;
        secondPanelRoot.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        neighboringPanelRoot.transform.SetPositionAndRotation(unchangedPanelPosition + new Vector3(1.43f, 0.1f, 0f),
            Quaternion.identity);
        Physics.SyncTransforms();
        Vector3 sourcePositionBeforeCandidate = secondPanelRoot.transform.position;
        Quaternion sourceRotationBeforeCandidate = secondPanelRoot.transform.rotation;
        Assert.That((bool)acceptsCandidate.Invoke(sectionState,
            new object[] { secondPanel, unchangedPanelPosition, Quaternion.identity }), Is.False,
            "A 90-degree source-to-candidate yaw must rotate the broadphase box and retain an offset child collider near the candidate panel's longer outer extent.");
        Assert.That(secondPanelRoot.transform.position, Is.EqualTo(sourcePositionBeforeCandidate));
        Assert.That(secondPanelRoot.transform.rotation, Is.EqualTo(sourceRotationBeforeCandidate));
#endif
    }

    [UnityTest]
    public IEnumerator PerPanelStateReplicatesToLateJoinAndClientCannotWrite()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses runtime NGO fixture components.");
        yield break;
#else
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("This loopback test requires a local process without an active NGO session.");

        System.Type stateType = System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        System.Type markerType = System.Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        Assert.That(stateType, Is.Not.Null);
        Assert.That(markerType, Is.Not.Null);
        GameObject statePrefab = new("Stage2 section state network prefab");
        roots.Add(statePrefab);
        NetworkObject stateNetworkObject = statePrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(stateNetworkObject, 0x44505332u);
        statePrefab.AddComponent(stateType);
        stateNetworkObject.SetSceneObjectStatus(false);
        GameObject panelPrefab = new("Stage2 physical panel network prefab");
        roots.Add(panelPrefab);
        NetworkObject panelNetworkObject = panelPrefab.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetField("GlobalObjectIdHash", PrivateInstance).SetValue(panelNetworkObject, 0x44505032u);
        panelPrefab.AddComponent(markerType);
        panelNetworkObject.SetSceneObjectStatus(false);

        priorSingleton = NetworkManager.Singleton;
        ushort port = ReservePort();
        host = CreateManager("Stage2 state host", statePrefab, panelPrefab, port, out GameObject hostRoot);
        roots.Add(hostRoot);
        Assert.That(host.StartHost(), Is.True);
        host.SetSingleton();
        yield return WaitFor(() => host.IsListening, "Stage2 host startup");
        Assert.That(stateNetworkObject.IsSpawned, Is.False, "The non-scene state source prefab remains unspawned on host start.");
        Assert.That(panelNetworkObject.IsSpawned, Is.False, "The non-scene panel source prefab remains unspawned on host start.");

        NetworkObject stateObject = Spawn(host, statePrefab);
        NetworkObject firstPanelObject = Spawn(host, panelPrefab);
        NetworkObject secondPanelObject = Spawn(host, panelPrefab);
        Assert.That(stateObject.IsSpawned && firstPanelObject.IsSpawned && secondPanelObject.IsSpawned, Is.True,
            "Only the explicitly instantiated test objects are spawned into the network session.");
        Assert.That(stateNetworkObject.IsSpawned || panelNetworkObject.IsSpawned, Is.False,
            "Registering the source prefabs must never spawn those source GameObjects.");
        Component serverState = stateObject.GetComponent(stateType);
        Component firstMarker = firstPanelObject.GetComponent(markerType);
        Component secondMarker = secondPanelObject.GetComponent(markerType);
        Assert.That(firstPanelObject.NetworkObjectId, Is.Not.EqualTo(secondPanelObject.NetworkObjectId));
        WritePanelState(serverState, firstMarker, CreateState(firstPanelObject.NetworkObjectId, 2, 15, 0, true));
        WritePanelState(serverState, secondMarker, CreateState(secondPanelObject.NetworkObjectId, 0, 0, 2, false));
        Assert.That(PanelStateMatches(serverState, firstMarker, 2, 15, 0, true));
        Assert.That(PanelStateMatches(serverState, secondMarker, 0, 0, 2, false), Is.True,
            "The server stores an independent layout failure with no fasteners installed.");

        // This client joins only after both panel records exist, exercising initial NetworkList synchronization.
        client = CreateManager("Stage2 late joining state client", statePrefab, panelPrefab, port, out GameObject clientRoot);
        roots.Add(clientRoot);
        Assert.That(client.StartClient(), Is.True);
        client.SetSingleton();
        yield return WaitFor(() => client.IsConnectedClient && Find(client, stateObject.NetworkObjectId) != null
            && Find(client, firstPanelObject.NetworkObjectId) != null && Find(client, secondPanelObject.NetworkObjectId) != null,
            "late join network object snapshot");
        NetworkObject clientStateObject = Find(client, stateObject.NetworkObjectId);
        Component clientState = clientStateObject.GetComponent(stateType);
        Component clientFirstMarker = Find(client, firstPanelObject.NetworkObjectId).GetComponent(markerType);
        Component clientSecondMarker = Find(client, secondPanelObject.NetworkObjectId).GetComponent(markerType);
        yield return WaitFor(() => PanelStateMatches(clientState, clientFirstMarker, 2, 15, 0, true)
            && PanelStateMatches(clientState, clientSecondMarker, 0, 0, 2, false),
            "late-join per-panel assessment snapshot");
        Assert.That(PanelStateMatches(clientState, clientFirstMarker, 2, 15, 0, true), Is.True);
        Assert.That(PanelStateMatches(clientState, clientSecondMarker, 0, 0, 2, false), Is.True,
            "Late join receives the placement failure independently of the 0/4 fastening mask.");

        WritePanelState(serverState, secondMarker, CreateState(secondPanelObject.NetworkObjectId, 1, 0, 1, false));
        yield return WaitFor(() => PanelStateMatches(clientState, clientSecondMarker, 1, 0, 1, false),
            "live correct-placement 0/4 assessment update");
        Assert.That(PanelStateMatches(clientState, clientSecondMarker, 1, 0, 1, false), Is.True,
            "The client receives positive placement feedback while fastening progress remains 0/4.");

        WritePanelState(clientState, clientFirstMarker, CreateState(firstPanelObject.NetworkObjectId, 0, 1, 1, false));
        yield return null;
        Assert.That(PanelStateMatches(clientState, clientFirstMarker, 2, 15, 0, true), Is.True,
            "A client-side call to the existing writer must not mutate authoritative panel state.");
        Assert.That(PanelStateMatches(serverState, firstMarker, 2, 15, 0, true), Is.True);

        WritePanelState(serverState, secondMarker, CreateState(secondPanelObject.NetworkObjectId, 1, 15, 0, true));
        yield return WaitFor(() => PanelStateMatches(clientState, clientSecondMarker, 1, 15, 0, true),
            "live per-panel assessment update");
        Assert.That(PanelStateMatches(clientState, clientFirstMarker, 2, 15, 0, true), Is.True,
            "Updating one panel preserves the completed record for the other panel.");
#endif
    }

#if UNITY_EDITOR
    private Component CreateNail(out GameObject instance)
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(prefab, Is.Not.Null);
        System.Type nailType = System.Type.GetType("MountingNail, Assembly-CSharp");
        Component prefabNail = prefab.GetComponent(nailType);
        ScriptableObject profile = Object.Instantiate((ScriptableObject)nailType.GetProperty("Profile", PublicInstance).GetValue(prefabNail));
        profiles.Add(profile);
        profile.GetType().GetField("requiredHitsForTwoObjects", PrivateInstance).SetValue(profile, 3);
        instance = Object.Instantiate(prefab);
        nailType.GetField("profile", PrivateInstance).SetValue(instance.GetComponent(nailType), profile);
        return instance.GetComponent(nailType);
    }

    private static void MountNailOnPanel(Component nail, GameObject nailObject, Transform panel, Vector2 localXZ)
    {
        Quaternion downward = Quaternion.FromToRotation(nail.transform.forward, -panel.up) * nailObject.transform.rotation;
        nailObject.transform.rotation = downward;
        // The authored panel collider center is at local Y=0.1 with 0.2m thickness.
        Vector3 entry = panel.TransformPoint(new Vector3(localXZ.x, 0.2f, localXZ.y));
        Transform tip = (Transform)nail.GetType().GetProperty("Tip", PublicInstance).GetValue(nail);
        nailObject.transform.position += entry + panel.up * 0.005f - tip.position;
        Physics.SyncTransforms();
    }

    private static byte ReadQuarterMask(Component state, Component marker)
    {
        object[] args = { marker, null };
        bool found = (bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args);
        Assert.That(found, Is.True);
        return (byte)args[1].GetType().GetField("QuarterMask").GetValue(args[1]);
    }

    private static bool IsPanelComplete(Component state, Component marker)
    {
        object[] args = { marker, null };
        bool found = (bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args);
        return found && (bool)args[1].GetType().GetField("Complete").GetValue(args[1]);
    }

    private static string ReadFailure(Component state, Component marker)
    {
        object[] args = { marker, null };
        bool found = (bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args);
        Assert.That(found, Is.True);
        byte value = (byte)args[1].GetType().GetField("Failure").GetValue(args[1]);
        return System.Enum.ToObject(System.Type.GetType("BridgeDeckPanelPlacementFailure, Assembly-CSharp"), value).ToString();
    }

    private static List<string> ReadPromptDescriptions(Component provider)
    {
        System.Type promptType = System.Type.GetType("InteractionPrompt, Assembly-CSharp");
        System.Type promptListType = typeof(List<>).MakeGenericType(promptType);
        object prompts = System.Activator.CreateInstance(promptListType);
        provider.GetType().GetMethod("GetInteractionPrompts", PublicInstance).Invoke(provider, new[] { null, prompts });
        var descriptions = new List<string>();
        System.Collections.IEnumerable values = (System.Collections.IEnumerable)prompts;
        foreach (object prompt in values)
            descriptions.Add((string)promptType.GetField("Description").GetValue(prompt));
        return descriptions;
    }

    private static void AssertOutlineRgb(LineRenderer line, float red, float green, float blue, string message)
    {
        MaterialPropertyBlock block = new();
        line.GetPropertyBlock(block);
        Color renderedColor = block.GetColor(Shader.PropertyToID("_BaseColor"));
        Assert.That(renderedColor.r, Is.EqualTo(red).Within(0.001f), message);
        Assert.That(renderedColor.g, Is.EqualTo(green).Within(0.001f), message);
        Assert.That(renderedColor.b, Is.EqualTo(blue).Within(0.001f), message);
    }

    private static Collider FindEnabledCollider(GameObject root)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            if (collider != null && collider.enabled && !collider.isTrigger) return collider;
        return null;
    }
#endif

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            NetworkObject networkObject = spawnedObjects[i];
            if (networkObject != null && networkObject.IsSpawned && host != null && host.IsListening) networkObject.Despawn(true);
        }
        spawnedObjects.Clear();
        if (client != null && client.IsListening) client.Shutdown();
        if (host != null && host.IsListening) host.Shutdown();
        yield return null;
        foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        foreach (ScriptableObject profile in profiles) if (profile != null) Object.Destroy(profile);
        roots.Clear();
        profiles.Clear();
        if (priorSingleton != null) priorSingleton.SetSingleton();
        else if (host != null || client != null)
            typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
        host = null;
        client = null;
        priorSingleton = null;
        yield return null;
    }

    private static NetworkManager CreateManager(string objectName, GameObject statePrefab, GameObject panelPrefab,
        ushort port, out GameObject root)
    {
        root = new GameObject(objectName);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = statePrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = panelPrefab });
        return manager;
    }

    private NetworkObject Spawn(NetworkManager manager, GameObject prefab)
    {
        GameObject instance = Object.Instantiate(prefab);
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        networkObject.Spawn();
        spawnedObjects.Add(networkObject);
        return networkObject;
    }

    private static ushort ReservePort()
    {
        using UdpClient listener = new(0);
        return (ushort)((IPEndPoint)listener.Client.LocalEndPoint).Port;
    }

    private static NetworkObject Find(NetworkManager manager, ulong networkObjectId)
        => manager != null && manager.SpawnManager != null
            && manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject value) ? value : null;

    private static IEnumerator WaitFor(System.Func<bool> condition, string operation)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out waiting for " + operation + ".");
    }

    private static object CreateState(ulong panelNetworkObjectId, int slotIndex, byte quarterMask, byte failure, bool complete)
    {
        System.Type stateType = System.Type.GetType("BridgeDeckPanelReplicatedState, Assembly-CSharp");
        object state = System.Activator.CreateInstance(stateType);
        stateType.GetField("PanelNetworkObjectId").SetValue(state, panelNetworkObjectId);
        stateType.GetField("SlotIndex").SetValue(state, slotIndex);
        stateType.GetField("QuarterMask").SetValue(state, quarterMask);
        stateType.GetField("Failure").SetValue(state, failure);
        stateType.GetField("Complete").SetValue(state, complete);
        return state;
    }

    private static void WritePanelState(Component state, Component marker, object value)
        => state.GetType().GetMethod("WritePanelState", PrivateInstance).Invoke(state, new[] { marker, value });

    private static byte ReadReplicatedMask(Component state, Component marker)
    {
        object[] args = { marker, null };
        bool found = (bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args);
        Assert.That(found, Is.True);
        return (byte)args[1].GetType().GetField("QuarterMask").GetValue(args[1]);
    }

    private static bool TryReadReplicatedMask(Component state, Component marker, out byte mask)
    {
        object[] args = { marker, null };
        bool found = (bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args);
        mask = found ? (byte)args[1].GetType().GetField("QuarterMask").GetValue(args[1]) : (byte)0;
        return found;
    }

    private static bool ReadPanelComplete(Component state, Component marker)
    {
        object[] args = { marker, null };
        bool found = (bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args);
        Assert.That(found, Is.True);
        return (bool)args[1].GetType().GetField("Complete").GetValue(args[1]);
    }

    private static bool PanelStateMatches(Component state, Component marker, int slotIndex, byte quarterMask, byte failure, bool complete)
    {
        object[] args = { marker, null };
        bool found = (bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args);
        if (!found) return false;
        object value = args[1];
        System.Type valueType = value.GetType();
        return (int)valueType.GetField("SlotIndex").GetValue(value) == slotIndex
            && (byte)valueType.GetField("QuarterMask").GetValue(value) == quarterMask
            && (byte)valueType.GetField("Failure").GetValue(value) == failure
            && (bool)valueType.GetField("Complete").GetValue(value) == complete;
    }

    private static GameObject CreateCompetingIncompleteSection(Component panelMarker, Vector3 panelPosition,
        Component originalSlot, List<GameObject> createdRoots)
    {
        GameObject sectionRoot = new("Competing incomplete section");
        sectionRoot.SetActive(false);
        createdRoots.Add(sectionRoot);
        System.Type sectionType = System.Type.GetType("BridgeDeckSection, Assembly-CSharp");
        Component section = sectionRoot.AddComponent(sectionType);
        GameObject slotObject = Object.Instantiate(originalSlot.gameObject, sectionRoot.transform);
        slotObject.name = "Near competing slot";
        slotObject.transform.localPosition = Vector3.zero;
        slotObject.transform.localRotation = Quaternion.identity;
        Component competingSlot = slotObject.GetComponent(System.Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp"));
        Component bridgeComponent = slotObject.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"));
        if (bridgeComponent != null) ((Behaviour)bridgeComponent).enabled = false;

        System.Type slotType = System.Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        System.Array slots = System.Array.CreateInstance(slotType, 1);
        slots.SetValue(competingSlot, 0);
        sectionType.GetField("panelSlots", PrivateInstance).SetValue(section, slots);
        sectionRoot.transform.position = panelPosition;

        GameObject stateRoot = new("Competing section panel state");
        stateRoot.transform.SetParent(sectionRoot.transform, false);
        System.Type stateType = System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        Component state = stateRoot.AddComponent(stateType);
        sectionType.GetField("physicalPanelState", PrivateInstance).SetValue(section, state);
        System.Type replicatedStateType = System.Type.GetType("BridgeDeckPanelReplicatedState, Assembly-CSharp");
        object record = System.Activator.CreateInstance(replicatedStateType);
        replicatedStateType.GetField("SlotIndex").SetValue(record, 0);
        replicatedStateType.GetField("QuarterMask").SetValue(record, (byte)0);
        replicatedStateType.GetField("Failure").SetValue(record,
            System.Convert.ToByte(System.Enum.Parse(System.Type.GetType("BridgeDeckPanelPlacementFailure, Assembly-CSharp"), "HorizontalOffset")));
        replicatedStateType.GetField("Complete").SetValue(record, false);
        System.Type localRecordType = stateType.GetNestedType("LocalPanelState", BindingFlags.NonPublic);
        object localRecord = System.Activator.CreateInstance(localRecordType, true);
        localRecordType.GetField("Panel").SetValue(localRecord, panelMarker);
        localRecordType.GetField("State").SetValue(localRecord, record);
        IList localPanels = (IList)stateType.GetField("localPanels", PrivateInstance).GetValue(state);
        localPanels.Add(localRecord);

        sectionRoot.SetActive(true);
        return sectionRoot;
    }
}
