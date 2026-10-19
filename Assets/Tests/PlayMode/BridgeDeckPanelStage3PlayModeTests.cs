using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class BridgeDeckPanelStage3PlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private readonly List<GameObject> roots = new();
    private readonly List<ScriptableObject> profiles = new();
    private SimulationMode previousSimulationMode;
    private bool restoreSimulationMode;

    [UnityTest]
    public IEnumerator ResetRemovesAllPairEdgesAndPreservesUnrelatedGraphConnection()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity prefabs available only in the Editor.");
        yield break;
#else
        System.Type sectionType = System.Type.GetType("BridgeDeckSection, Assembly-CSharp");
        System.Type stateType = System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        System.Type siteType = System.Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        System.Type panelType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        System.Type markerType = System.Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        System.Type nailType = System.Type.GetType("MountingNail, Assembly-CSharp");

        GameObject sectionRoot = new("Stage 3 reset section");
        roots.Add(sectionRoot);
        Component section = sectionRoot.AddComponent(sectionType);
        GameObject stateRoot = new("Stage 3 reset state");
        stateRoot.transform.SetParent(sectionRoot.transform, false);
        Component state = stateRoot.AddComponent(stateType);
        sectionType.GetField("physicalPanelState", PrivateInstance).SetValue(section, state);

        GameObject slotPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelBridgeComponent.prefab");
        GameObject slot = Object.Instantiate(slotPrefab, sectionRoot.transform);
        roots.Add(slot);
        slot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Component bridge = slot.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"));
        if (bridge != null) ((Behaviour)bridge).enabled = false;
        Component site = slot.GetComponent(siteType);
        // The physical slot fixture overlaps the mountable panel at its resting pose. Keep the
        // production panel collider live while preventing the authored slot collision from
        // consuming the reset impulse during this isolated physics test.
        foreach (Collider collider in slot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        FieldInfo slotsField = sectionType.GetField("panelSlots", PrivateInstance);
        System.Array slots = System.Array.CreateInstance(slotsField.FieldType.GetElementType(), 1);
        slots.SetValue(site, 0);
        slotsField.SetValue(section, slots);
        siteType.GetMethod("ConfigureSection", PublicInstance).Invoke(site, new object[] { section, 0, false });

        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
        GameObject panelRoot = Object.Instantiate(panelPrefab);
        roots.Add(panelRoot);
        Component panel = panelRoot.GetComponent(panelType);
        panelType.GetField("isPhysicalDeckPanel", PrivateInstance).SetValue(panel, true);
        Component marker = panelRoot.GetComponent(markerType);
        if (marker == null) marker = panelRoot.AddComponent(markerType);
        panelRoot.transform.SetPositionAndRotation(slot.transform.position, slot.transform.rotation);
        Rigidbody panelBody = panelRoot.GetComponent<Rigidbody>();
        panelBody.useGravity = false;
        panelBody.isKinematic = false;

        Ray panelRay = new(panelRoot.transform.position + Vector3.up * 2f, Vector3.down);
        System.Type hammerAimResolverType = System.Type.GetType("BridgeDeckPanelHammerAimResolver, Assembly-CSharp");
        object[] visiblePanelArgs = { panelRay, 3f, null, null, null };
        Assert.That((bool)hammerAimResolverType.GetMethod("TryFindVisiblePanel").Invoke(null, visiblePanelArgs), Is.True);
        Component visiblePanel = (Component)visiblePanelArgs[3];
        Collider visibleCollider = (Collider)visiblePanelArgs[4];
        Assert.That(visiblePanel, Is.SameAs(marker));
        Assert.That(visibleCollider, Is.Not.Null,
            "The panel resolver requires a real collider hit on the visible physical panel.");
        GameObject panelBlocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(panelBlocker);
        panelBlocker.transform.position = panelRoot.transform.position + Vector3.up;
        Physics.SyncTransforms();
        object[] blockedPanelArgs = { panelRay, 3f, null, null, null };
        Assert.That((bool)hammerAimResolverType.GetMethod("TryFindVisiblePanel").Invoke(null, blockedPanelArgs), Is.False,
            "A visible solid blocker prevents the hammer request from falling through to the panel.");
        Object.Destroy(panelBlocker);
        Physics.SyncTransforms();

        yield return null;

        GameObject nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Component prefabNail = nailPrefab.GetComponent(nailType);
        ScriptableObject profile = Object.Instantiate((ScriptableObject)nailType.GetProperty("Profile", PublicInstance).GetValue(prefabNail));
        profiles.Add(profile);
        profile.GetType().GetField("requiredHitsForSingleObject", PrivateInstance).SetValue(profile, 2);
        GameObject nailObject = Object.Instantiate(nailPrefab);
        roots.Add(nailObject);
        Component nail = nailObject.GetComponent(nailType);
        nailType.GetField("profile", PrivateInstance).SetValue(nail, profile);
        Quaternion downward = Quaternion.FromToRotation(nailObject.transform.forward, -panelRoot.transform.up)
            * nailObject.transform.rotation;
        nailObject.transform.rotation = downward;
        Vector3 entry = panelRoot.transform.TransformPoint(new Vector3(0f, 0.2f, 0f));
        Transform tip = (Transform)nailType.GetProperty("Tip", PublicInstance).GetValue(nail);
        nailObject.transform.position += entry + panelRoot.transform.up * 0.005f - tip.position;
        Physics.SyncTransforms();

        nailType.GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(nail, new object[] { null });
        object hammer = System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer");
        Assert.That(nailType.GetMethod("TryApplyHammerHit", PublicInstance).Invoke(nail, new[] { hammer }), Is.EqualTo(true));
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(nail).ToString(), Is.EqualTo("Partial"));

        GameObject beam = CreateGraphTarget("Panel pair beam", 0f);
        GameObject unrelatedFirst = CreateGraphTarget("Unrelated graph first", 0f);
        GameObject unrelatedSecond = CreateGraphTarget("Unrelated graph second", 0f);
        beam.transform.position = panelRoot.transform.position + Vector3.up * 0.1f;
        unrelatedFirst.transform.position = new Vector3(30f, 0f, 0f);
        unrelatedSecond.transform.position = new Vector3(31f, 0f, 0f);
        Component sharedFirstNail = CreateEmbeddedPair(nailPrefab, nailType, profile, panelRoot.transform, beam.transform);
        Component sharedSecondNail = CreateEmbeddedPair(nailPrefab, nailType, profile, panelRoot.transform, beam.transform);
        Component panelAsSecondNail = CreateEmbeddedPair(nailPrefab, nailType, profile, beam.transform, panelRoot.transform);
        Component futureSecondPartial = CreatePartialWithFutureSecond(nailPrefab, nailType, profile,
            unrelatedFirst.transform, panelRoot.transform);
        Component unrelatedNail = CreateEmbeddedPair(nailPrefab, nailType, profile, unrelatedFirst.transform, unrelatedSecond.transform);
        GameObject preparedPanelRoot = panelRoot;
        MethodInfo reset = stateType.GetMethod("TryResetIncompletePanelWithHammer", PrivateInstance);
        GameObject preparedNailObject = Object.Instantiate(nailPrefab);
        roots.Add(preparedNailObject);
        Component preparedNail = preparedNailObject.GetComponent(nailType);
        nailType.GetField("profile", PrivateInstance).SetValue(preparedNail, profile);
        MountNailOnPanel(preparedNail, preparedNailObject, preparedPanelRoot.transform, new Vector2(0.4f, -0.4f));
        nailType.GetMethod("OnSingleCarryPlacementCommitted", PublicInstance)
            .Invoke(preparedNail, new object[] { null });
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(preparedNail).ToString(), Is.EqualTo("Prepared"),
            "The prepared control nail is physically contacting the panel before reset.");
        System.Type registryType = System.Type.GetType("MountingNailConnectionRegistry, Assembly-CSharp");
        object registry = nailType.GetField("connectionRegistry", PrivateInstance).GetValue(sharedFirstNail);
        MethodInfo pairJointCount = registryType.GetMethod("PairJointCount", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo degree = registryType.GetMethod("Degree", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(pairJointCount.Invoke(registry, new object[] { panelRoot.transform, beam.transform }), Is.EqualTo(1));
        Assert.That(degree.Invoke(registry, new object[] { panelRoot.transform }), Is.EqualTo(1));
        Assert.That(pairJointCount.Invoke(registry, new object[] { unrelatedFirst.transform, unrelatedSecond.transform }), Is.EqualTo(1));

        ((Behaviour)marker).enabled = false;
        Assert.That(reset.Invoke(state, new object[] { marker, Vector3.forward }), Is.EqualTo(false),
            "A stale internal reset request cannot mutate nails for a disabled panel marker.");
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(nail).ToString(), Is.EqualTo("Partial"));
        ((Behaviour)marker).enabled = true;
        Vector3 originalSlotPosition = slot.transform.position;
        slot.transform.position += slot.transform.right * 0.1f;
        object invalidPoseState = System.Activator.CreateInstance(System.Type.GetType(
            "BridgeDeckPanelReplicatedState, Assembly-CSharp"));
        System.Type stateRecordType = invalidPoseState.GetType();
        stateRecordType.GetField("SlotIndex").SetValue(invalidPoseState, 0);
        stateRecordType.GetField("QuarterMask").SetValue(invalidPoseState, (byte)15);
        stateRecordType.GetField("Failure").SetValue(invalidPoseState,
            (byte)System.Enum.Parse(System.Type.GetType("BridgeDeckPanelPlacementFailure, Assembly-CSharp"), "HorizontalOffset"));
        stateRecordType.GetField("Complete").SetValue(invalidPoseState, false);
        stateType.GetMethod("WritePanelState", PrivateInstance).Invoke(state, new[] { marker, invalidPoseState });
        Assert.That(ReadQuarterMask(state, marker), Is.EqualTo((byte)15),
            "The reset starts from a full assessment mask at a deliberately invalid slot pose.");
        Assert.That(reset.Invoke(state, new object[] { marker, Vector3.forward }), Is.EqualTo(true));
        slot.transform.position = originalSlotPosition;
        Assert.That(reset.Invoke(state, new object[] { marker, Vector3.forward }), Is.EqualTo(false),
            "A duplicate reset cannot schedule a second impulse while pair-joint destruction is pending.");
        Assert.That(nailType.GetMethod("TryApplyHammerHit", PublicInstance).Invoke(nail, new[] { hammer }), Is.EqualTo(false),
            "The nail becomes unusable synchronously when reset starts, before its deferred destruction.");
        Assert.That((bool)nailType.GetProperty("BlocksPickup", PublicInstance).GetValue(nail), Is.True,
            "A cached reference cannot pick up the nail between synchronous invalidation and deferred destruction.");
        object[] attachmentArgs = { null };
        Assert.That((bool)nailType.GetMethod("TryGetAttachmentInfo", PublicInstance).Invoke(nail, attachmentArgs), Is.False,
            "A pending reset nail cannot remain in the panel's active attachment snapshot.");
        Assert.That(ReadQuarterMask(state, marker), Is.Zero,
            "The panel's quarter state is refreshed immediately after its nails are removed.");
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(sharedFirstNail).ToString(), Is.EqualTo("Loose"));
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(sharedSecondNail).ToString(), Is.EqualTo("Loose"));
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(panelAsSecondNail).ToString(), Is.EqualTo("Loose"),
            "An actual EmbeddedPair is removed when the reset panel is its second target.");
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(futureSecondPartial).ToString(), Is.EqualTo("Partial"),
            "A Partial nail on another first target is not deleted merely because it selected this panel as its future second target.");
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(unrelatedNail).ToString(), Is.EqualTo("EmbeddedPair"));
        Assert.That(pairJointCount.Invoke(registry, new object[] { panelRoot.transform, beam.transform }), Is.Zero);
        Assert.That(degree.Invoke(registry, new object[] { panelRoot.transform }), Is.Zero);
        Assert.That(pairJointCount.Invoke(registry, new object[] { unrelatedFirst.transform, unrelatedSecond.transform }), Is.EqualTo(1),
            "Resetting the panel preserves unrelated graph edges.");

        Vector3 resetPanelStart = panelBody.position;
        float resetMovementDeadline = Time.realtimeSinceStartup + 2f;
        while (Vector3.Distance(panelBody.position, resetPanelStart) < 0.2f
            && Time.realtimeSinceStartup < resetMovementDeadline) yield return null;
        Assert.That(Vector3.Distance(panelBody.position, resetPanelStart), Is.GreaterThan(0.2f),
            "The graph cleanup reset moves the panel after all pair constraints have been removed.");
        Assert.That(nailObject == null, Is.True, "The removed offline nail is destroyed without recovery.");
        Assert.That(panelRoot.GetComponents<FixedJoint>(), Is.Empty,
            "All panel constraints are gone before the first reset impulse is observed.");
        float preparedDeadline = Time.realtimeSinceStartup + 2f;
        while (nailType.GetProperty("State", PublicInstance).GetValue(preparedNail).ToString() == "Prepared"
            && Time.realtimeSinceStartup < preparedDeadline) yield return null;
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(preparedNail).ToString(), Is.EqualTo("Loose"),
            "The Prepared nail survives reset and naturally cancels after that panel moves away from its prepared pose.");

        GameObject emptyPanelRoot = Object.Instantiate(panelPrefab);
        roots.Add(emptyPanelRoot);
        Component emptyPanel = emptyPanelRoot.GetComponent(panelType);
        panelType.GetField("isPhysicalDeckPanel", PrivateInstance).SetValue(emptyPanel, true);
        Component emptyMarker = emptyPanelRoot.GetComponent(markerType);
        if (emptyMarker == null) emptyMarker = emptyPanelRoot.AddComponent(markerType);
        emptyPanelRoot.transform.SetPositionAndRotation(slot.transform.position + Vector3.right * 6f, slot.transform.rotation);
        Rigidbody emptyBody = emptyPanelRoot.GetComponent<Rigidbody>();
        emptyBody.useGravity = false;
        emptyBody.mass = 20f;
        emptyBody.isKinematic = false;
        emptyBody.linearVelocity = new Vector3(0f, 0f, 1.25f);
        yield return null;
        previousSimulationMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        restoreSimulationMode = true;
        Vector3 emptyVelocityBeforeReset = emptyBody.linearVelocity;
        Assert.That(reset.Invoke(state, new object[] { emptyMarker, Vector3.left }), Is.EqualTo(true),
            "A panel with zero embedded nails still receives the reset impulse.");
        Assert.That(reset.Invoke(state, new object[] { emptyMarker, Vector3.left }), Is.EqualTo(false),
            "Repeated reset requests do not stack impulses.");
        PropertyInfo emptyResetPending = emptyMarker.GetType().GetProperty("ResetImpulsePending",
            BindingFlags.Instance | BindingFlags.NonPublic);
        float emptyResetDeadline = Time.realtimeSinceStartup + 2f;
        while ((bool)emptyResetPending.GetValue(emptyMarker) && Time.realtimeSinceStartup < emptyResetDeadline)
            yield return null;
        Assert.That((bool)emptyResetPending.GetValue(emptyMarker), Is.False,
            "Deferred joint cleanup finishes before the measured physics step.");
        Physics.Simulate(Time.fixedDeltaTime);
        Vector3 emptyResetDeltaVelocity = emptyBody.linearVelocity - emptyVelocityBeforeReset;
        Assert.That(emptyResetDeltaVelocity.y, Is.EqualTo(2f + Physics.gravity.y * Time.fixedDeltaTime).Within(0.1f),
            "The zero-nail reset adds its configured vertical delta-V while gravity acts during the measured step.");
        Assert.That(emptyResetDeltaVelocity.x, Is.LessThan(-0.4f));
        Assert.That(emptyBody.linearVelocity.z, Is.GreaterThan(1.2f),
            "The reset adds its delta velocity to existing motion and remains mass-independent at a non-unit mass.");
        Physics.simulationMode = previousSimulationMode;
        restoreSimulationMode = false;
        FieldInfo pickedUpField = panelType.GetField("isPickedUp", PrivateInstance);
        Vector3 velocityBeforeHeldReset = emptyBody.linearVelocity;
        pickedUpField.SetValue(emptyPanel, true);
        Assert.That(reset.Invoke(state, new object[] { emptyMarker, Vector3.left }), Is.EqualTo(false),
            "The shared held-state guard rejects a reset while either a player or NPC owns the panel.");
        Assert.That(emptyBody.linearVelocity, Is.EqualTo(velocityBeforeHeldReset),
            "A rejected held-panel reset has no physics side effects.");
        pickedUpField.SetValue(emptyPanel, false);

        GameObject cancelledPanelRoot = Object.Instantiate(panelPrefab);
        roots.Add(cancelledPanelRoot);
        Component cancelledPanel = cancelledPanelRoot.GetComponent(panelType);
        panelType.GetField("isPhysicalDeckPanel", PrivateInstance).SetValue(cancelledPanel, true);
        Component cancelledMarker = cancelledPanelRoot.GetComponent(markerType);
        if (cancelledMarker == null) cancelledMarker = cancelledPanelRoot.AddComponent(markerType);
        cancelledPanelRoot.transform.SetPositionAndRotation(slot.transform.position + Vector3.right * 12f, slot.transform.rotation);
        Rigidbody cancelledBody = cancelledPanelRoot.GetComponent<Rigidbody>();
        cancelledBody.useGravity = false;
        cancelledBody.isKinematic = false;
        yield return null;
        previousSimulationMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        restoreSimulationMode = true;
        Assert.That(reset.Invoke(state, new object[] { cancelledMarker, Vector3.forward }), Is.EqualTo(true));
        Assert.That(cancelledBody.linearVelocity, Is.EqualTo(Vector3.zero));
        ((Behaviour)cancelledMarker).enabled = false;
        yield return null;
        Physics.Simulate(Time.fixedDeltaTime);
        Assert.That(cancelledBody.useGravity, Is.True,
            "Reset enables natural gravity even when marker shutdown cancels the deferred impulse.");
        Assert.That(cancelledBody.linearVelocity.y,
            Is.EqualTo(Physics.gravity.y * Time.fixedDeltaTime).Within(0.1f),
            "The cancelled reset receives gravity-only velocity after one physics step, with no reset delta-V.");
        Assert.That(new Vector2(cancelledBody.linearVelocity.x, cancelledBody.linearVelocity.z).magnitude,
            Is.LessThan(0.05f), "A cancelled reset adds no horizontal away impulse.");
        Physics.simulationMode = previousSimulationMode;
        restoreSimulationMode = false;

        // Use the authored PlayerActionController and action profile to prove the panel reset
        // occurs at Strike impact time, after the click has started its wind-up.
        // Hold physics simulation while the real action winds up so its camera ray remains on
        // the gravity-enabled, dynamic panel; the accepted reset is measured in one real step.
        foreach (Collider priorBeamCollider in beam.GetComponentsInChildren<Collider>(true))
            priorBeamCollider.enabled = false;
        panelRoot.transform.SetPositionAndRotation(slot.transform.position, slot.transform.rotation);
        panelBody.linearVelocity = Vector3.zero;
        panelBody.angularVelocity = Vector3.zero;
        panelBody.isKinematic = false;
        panelBody.useGravity = true;
        Physics.SyncTransforms();
        foreach (GameObject priorRoot in roots)
        {
            if (priorRoot == null) continue;
            foreach (Component priorNail in priorRoot.GetComponentsInChildren(nailType, true))
                foreach (Collider priorCollider in ((Component)priorNail).GetComponentsInChildren<Collider>(true))
                    priorCollider.enabled = false;
        }
        GameObject impactNailObject = Object.Instantiate(nailPrefab);
        roots.Add(impactNailObject);
        Component impactNail = impactNailObject.GetComponent(nailType);
        nailType.GetField("profile", PrivateInstance).SetValue(impactNail, profile);
        Quaternion impactDown = Quaternion.FromToRotation(impactNailObject.transform.forward, -panelRoot.transform.up)
            * impactNailObject.transform.rotation;
        impactNailObject.transform.rotation = impactDown;
        Vector3 impactEntry = panelRoot.transform.TransformPoint(new Vector3(-0.7f, 0.2f, -0.7f));
        Transform impactTip = (Transform)nailType.GetProperty("Tip", PublicInstance).GetValue(impactNail);
        impactNailObject.transform.position += impactEntry + panelRoot.transform.up * 0.005f - impactTip.position;
        Physics.SyncTransforms();
        nailType.GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(impactNail, new object[] { null });
        object hammerType = System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer");
        Assert.That(nailType.GetMethod("TryApplyHammerHit", PublicInstance).Invoke(impactNail, new[] { hammerType }), Is.True);
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(impactNail).ToString(), Is.EqualTo("Partial"));

        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        Assert.That(playerPrefab, Is.Not.Null);
        GameObject player = Object.Instantiate(playerPrefab, panelRoot.transform.position + Vector3.up * 4f, Quaternion.identity);
        roots.Add(player);
        foreach (Collider collider in player.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        System.Type actionControllerType = System.Type.GetType("PlayerActionController, Assembly-CSharp");
        System.Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        System.Type inventoryType = System.Type.GetType("PlayerInventory, Assembly-CSharp");
        Component actionController = player.GetComponent(actionControllerType);
        Component interaction = player.GetComponent(interactionType);
        Component inventory = player.GetComponent(inventoryType);
        Assert.That(actionController, Is.Not.Null);
        GameObject aimCameraObject = new("Stage 3 panel hammer impact camera");
        roots.Add(aimCameraObject);
        Vector3 aimCameraPosition = panelRoot.transform.position + Vector3.up;
        Vector3 barePanelAimPoint = panelRoot.transform.TransformPoint(new Vector3(0.7f, 0.2f, 0.7f));
        aimCameraObject.transform.SetPositionAndRotation(aimCameraPosition,
            Quaternion.LookRotation(barePanelAimPoint - aimCameraPosition, Vector3.forward));
        Camera aimCamera = aimCameraObject.AddComponent<Camera>();
        interactionType.GetField("aimCamera", PrivateInstance).SetValue(interaction, aimCamera);
        ScriptableObject impactHammer = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        FieldInfo itemsField = inventoryType.GetField("inventoryItems", PrivateInstance);
        System.Array items = System.Array.CreateInstance(itemsField.FieldType.GetElementType(), 1);
        items.SetValue(impactHammer, 0);
        itemsField.SetValue(inventory, items);
        inventoryType.GetField("_currentInventoryOccupiedSlots", PrivateInstance).SetValue(inventory, 1);
        inventoryType.GetField("_selectedItemIndex", PrivateInstance).SetValue(inventory, 0);
        Physics.SyncTransforms();
        MethodInfo getAimRay = interactionType.GetMethod("TryGetAimRay", PublicInstance, null,
            new[] { typeof(Ray).MakeByRefType() }, null);
        object[] actionRayArgs = { default(Ray) };
        Assert.That((bool)getAimRay.Invoke(interaction, actionRayArgs), Is.True);
        Ray actionRay = (Ray)actionRayArgs[0];
        FieldInfo actionRangeField = impactHammer.GetType().GetField("actionRange", PrivateInstance | BindingFlags.Public);
        float hammerActionRange = (float)actionRangeField.GetValue(impactHammer);
        object[] candidateArgs = { actionRay, hammerActionRange, player.transform, null, null, null };
        System.Type aimResolver = System.Type.GetType("MountingNailAimResolver, Assembly-CSharp");
        Assert.That((bool)aimResolver.GetMethod("TryFindCandidate").Invoke(null, candidateArgs), Is.False,
            "The panel-corner action ray does not overlap the embedded nail on the opposite corner.");
        System.Type panelAimResolver = System.Type.GetType("BridgeDeckPanelHammerAimResolver, Assembly-CSharp");
        object[] panelCandidateArgs = { actionRay, hammerActionRange, player.transform, null, null };
        Assert.That((bool)panelAimResolver.GetMethod("TryFindVisiblePanel").Invoke(null, panelCandidateArgs), Is.True,
            $"The actual profiled action ray sees the bare physical panel corner: ray={actionRay}, target={barePanelAimPoint}.");
        Assert.That(panelCandidateArgs[3], Is.SameAs(marker),
            "The actual profiled strike resolves the physical panel while the nail stays embedded elsewhere.");
        previousSimulationMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        restoreSimulationMode = true;
        Vector3 velocityAtClick = panelBody.linearVelocity;
        actionController.GetType().GetMethod("StartProfiledAction", PrivateInstance)
            .Invoke(actionController, new object[] { impactHammer });
        Assert.That(actionController.GetType().GetProperty("CurrentActionPhase", PublicInstance).GetValue(actionController).ToString(),
            Is.EqualTo("WindUp"));
        Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(impactNail).ToString(), Is.EqualTo("Partial"),
            "Starting the action does not reset the panel before the strike impact.");
        Assert.That(panelBody.linearVelocity, Is.EqualTo(velocityAtClick));
        float impactDeadline = Time.realtimeSinceStartup + 8f;
        while (impactNailObject != null && Time.realtimeSinceStartup < impactDeadline) yield return null;
        Assert.That(impactNailObject == null, Is.True,
            $"The actual profiled hammer impact removes the attached nail without recovery (phase={actionController.GetType().GetProperty("CurrentActionPhase", PublicInstance).GetValue(actionController)}, nailState={nailType.GetProperty("State", PublicInstance).GetValue(impactNail)}).");
        Assert.That(ReadQuarterMask(state, marker), Is.Zero);
        PropertyInfo resetPendingProperty = marker.GetType().GetProperty("ResetImpulsePending",
            BindingFlags.Instance | BindingFlags.NonPublic);
        float impulseDeadline = Time.realtimeSinceStartup + 2f;
        while ((bool)resetPendingProperty.GetValue(marker) && Time.realtimeSinceStartup < impulseDeadline)
            yield return null;
        Physics.Simulate(Time.fixedDeltaTime);
        Assert.That(panelBody.linearVelocity.y - velocityAtClick.y,
            Is.EqualTo(2f + Physics.gravity.y * Time.fixedDeltaTime).Within(0.2f),
            "The profiled Strike impact adds the reset delta-V at impact while authored gravity acts during the measured physics step.");
        Physics.simulationMode = previousSimulationMode;
        restoreSimulationMode = false;
#endif
    }

    private static byte ReadQuarterMask(Component state, Component marker)
    {
        object[] args = { marker, null };
        Assert.That((bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args), Is.True);
        return (byte)args[1].GetType().GetField("QuarterMask").GetValue(args[1]);
    }

    private GameObject CreateGraphTarget(string name, float mass)
    {
        GameObject target = new(name);
        roots.Add(target);
        Rigidbody body = target.AddComponent<Rigidbody>();
        body.mass = mass;
        body.isKinematic = mass <= 0f;
        return target;
    }

    private static void MountNailOnPanel(Component nail, GameObject nailObject, Transform panel, Vector2 localXZ)
    {
        Quaternion downward = Quaternion.FromToRotation(nail.transform.forward, -panel.up) * nailObject.transform.rotation;
        nailObject.transform.rotation = downward;
        Vector3 entry = panel.TransformPoint(new Vector3(localXZ.x, 0.2f, localXZ.y));
        Transform tip = (Transform)nail.GetType().GetProperty("Tip", PublicInstance).GetValue(nail);
        nailObject.transform.position += entry + panel.up * 0.005f - tip.position;
        Physics.SyncTransforms();
    }

    private Component CreateEmbeddedPair(GameObject nailPrefab, System.Type nailType, ScriptableObject profile,
        Transform first, Transform second)
    {
        GameObject nailObject = Object.Instantiate(nailPrefab);
        roots.Add(nailObject);
        Component nail = nailObject.GetComponent(nailType);
        nailType.GetField("profile", PrivateInstance).SetValue(nail, profile);
        nailType.GetMethod("SetEmbeddedTargets", PrivateInstance)
            .Invoke(nail, new object[] { first, second, first.position, Vector3.down });
        nailType.GetMethod("SetHitCounts", PrivateInstance).Invoke(nail, new object[] { 1, 1, 0.02f });
        object pairState = System.Enum.Parse(System.Type.GetType("MountingNailState, Assembly-CSharp"), "EmbeddedPair");
        nailType.GetMethod("SetState", PrivateInstance).Invoke(nail, new[] { pairState });
        Assert.That(nailType.GetMethod("TryRegisterPairConnection", PrivateInstance).Invoke(nail, null), Is.EqualTo(true));
        return nail;
    }

    private Component CreatePartialWithFutureSecond(GameObject nailPrefab, System.Type nailType, ScriptableObject profile,
        Transform first, Transform futureSecond)
    {
        GameObject nailObject = Object.Instantiate(nailPrefab);
        roots.Add(nailObject);
        Component nail = nailObject.GetComponent(nailType);
        nailType.GetField("profile", PrivateInstance).SetValue(nail, profile);
        nailType.GetMethod("SetEmbeddedTargets", PrivateInstance)
            .Invoke(nail, new object[] { first, futureSecond, first.position, Vector3.down });
        nailType.GetMethod("SetHitCounts", PrivateInstance).Invoke(nail, new object[] { 1, 2, 0.02f });
        object partialState = System.Enum.Parse(System.Type.GetType("MountingNailState, Assembly-CSharp"), "Partial");
        nailType.GetMethod("SetState", PrivateInstance).Invoke(nail, new[] { partialState });
        return nail;
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (restoreSimulationMode) Physics.simulationMode = previousSimulationMode;
        foreach (GameObject root in roots)
            if (root != null) Object.Destroy(root);
        foreach (ScriptableObject profile in profiles)
            if (profile != null) Object.Destroy(profile);
        roots.Clear();
        profiles.Clear();
        yield return null;
    }
}
