using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MountingNailExtractionStage2PlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private readonly System.Collections.Generic.List<GameObject> roots = new();
    private readonly System.Collections.Generic.List<ScriptableObject> profiles = new();
    private Component nail;
    private Component panelState;
    private Component panelMarker;
    private Component panel;
    private Component girder;

    [UnityTest]
    public IEnumerator ReservationsAreExclusiveUseExecutorSpecificHandlesAndRequireElapsedTime()
    {
        nail = CreateNail(out _);
        Component secondNail = CreateNail(out _);
        AudioSource firstExecutor = CreateExecutor("Stage 2 executor A");
        AudioSource secondExecutor = CreateExecutor("Stage 2 executor B");
        object handleA = Begin(nail, firstExecutor, 0.06f);
        Assert.That(IsValid(handleA), Is.True);
        Assert.That(Progress(nail), Is.EqualTo(0f).Within(0.01f));
        Assert.That(IsValid(Begin(nail, secondExecutor, 0.01f)), Is.False,
            "A competing begin cannot replace the current reservation.");
        Assert.That(Cancel(nail, secondExecutor, handleA), Is.False);
        Assert.That(Commit(nail, firstExecutor, handleA), Is.False, "Commit before the accepted duration must be rejected.");
        Assert.That(Progress(nail), Is.LessThan(1f));
        object handleB = Begin(secondNail, secondExecutor, 0.2f);
        Assert.That(IsValid(handleB), Is.True, "A second executor can reserve a different nail.");
        Assert.That(Commit(nail, firstExecutor, handleA), Is.False,
            "Reaching the deadline never removes a nail until a later commit call.");
        yield return new WaitForSeconds(0.09f);
        Assert.That(Progress(nail), Is.EqualTo(1f));
        Assert.That(Commit(nail, firstExecutor, handleA), Is.True);
        Assert.That(Commit(nail, firstExecutor, handleA), Is.False, "A committed handle cannot be replayed.");
        Assert.That(Commit(secondNail, secondExecutor, handleB), Is.False,
            "The second executor's longer accepted duration remains in force.");
        yield return new WaitForSeconds(0.15f);
        Assert.That(Progress(secondNail), Is.EqualTo(1f));
        Assert.That(Commit(secondNail, secondExecutor, handleB), Is.True);
    }

    [UnityTest]
    public IEnumerator CancelResetsProgressAndOldOrCrossNailHandlesCannotAffectNewReservations()
    {
        Component firstNail = CreateNail(out _);
        Component secondNail = CreateNail(out _);
        AudioSource executor = CreateExecutor("Stage 2 cancellation executor");
        object oldHandle = Begin(firstNail, executor, 0.3f);
        yield return new WaitForSeconds(0.06f);
        Assert.That(Cancel(firstNail, executor, oldHandle), Is.True);
        Assert.That(Progress(firstNail), Is.EqualTo(0f));
        object newHandle = Begin(firstNail, executor, 0.02f);
        object secondNailHandle = Begin(secondNail, executor, 0.02f);
        Assert.That(IsValid(newHandle), Is.True);
        Assert.That(Cancel(firstNail, executor, oldHandle), Is.False);
        Assert.That(Commit(firstNail, executor, oldHandle), Is.False);
        Assert.That(Cancel(secondNail, executor, newHandle), Is.False,
            "A handle cannot cancel another nail's active reservation.");
        Assert.That(IsValid(CrowbarReadState(secondNail).GetType().GetProperty("Handle", PublicInstance)
            .GetValue(CrowbarReadState(secondNail))), Is.True,
            "The unrelated reservation remains active after the cross-nail request.");
        yield return new WaitForSeconds(0.04f);
        Assert.That(Commit(firstNail, executor, newHandle), Is.True);
        Assert.That(Commit(secondNail, executor, secondNailHandle), Is.True,
            "An unrelated reservation remains usable after an obfuscated cross-nail request.");
        Assert.That(Cancel(firstNail, executor, newHandle), Is.False);
    }

    [UnityTest]
    public IEnumerator InvalidDurationsDisabledExecutorsAndAcceptedHammerHitsRejectOrCancel()
    {
        nail = CreateNail(out _);
        AudioSource executor = CreateExecutor("Stage 2 lifecycle executor");
        Assert.That(IsValid(Begin(nail, executor, 0f)), Is.False);
        Assert.That(IsValid(Begin(nail, executor, float.NaN)), Is.False);
        Assert.That(IsValid(Begin(nail, executor, float.PositiveInfinity)), Is.False);

        object handle = Begin(nail, executor, 0.3f);
        executor.enabled = false;
        yield return null;
        Assert.That(Progress(nail), Is.EqualTo(0f), "Disabling the exact executor cancels its reservation.");
        Assert.That(Commit(nail, executor, handle), Is.False);
        executor.enabled = true;

        Invoke(nail, "SetHitCounts", 1, 4, 0.02f);
        object afterReenable = Begin(nail, executor, 0.3f);
        Assert.That((bool)nail.GetType().GetMethod("TryApplyHammerHit", PublicInstance)
            .Invoke(nail, new object[] { System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer") }), Is.True);
        Assert.That((int)nail.GetType().GetProperty("HitCount", PublicInstance).GetValue(nail), Is.EqualTo(2));
        Assert.That(nail.GetType().GetProperty("State", PublicInstance).GetValue(nail).ToString(), Is.EqualTo("Partial"));
        Assert.That(Progress(nail), Is.EqualTo(0f), "An accepted Partial hit cancels even when the state stays Partial.");
        Assert.That(Commit(nail, executor, afterReenable), Is.False);
    }

    [UnityTest]
    public IEnumerator BeginAndCancelKeepPairJointAndPickupLocksUntilCommitThenReleaseThem()
    {
        Component pairNail = CreateNail(out _);
        GameObject first = CreatePhysicsTarget("Stage 2 pair first target");
        GameObject second = CreatePhysicsTarget("Stage 2 pair second target");
        Invoke(pairNail, "SetEmbeddedTargets", first.transform, second.transform, first.transform.position, Vector3.forward);
        Invoke(pairNail, "SetHitCounts", 1, 1, 0.02f);
        Invoke(pairNail, "SetState", System.Enum.Parse(System.Type.GetType("MountingNailState, Assembly-CSharp"), "EmbeddedPair"));
        Assert.That((bool)pairNail.GetType().GetMethod("TryRegisterPairConnection", PrivateInstance).Invoke(pairNail, null), Is.True);
        object registry = pairNail.GetType().GetField("connectionRegistry", PrivateInstance).GetValue(pairNail);
        MethodInfo jointCount = registry.GetType().GetMethod("PairJointCount", PrivateInstance);
        Assert.That((int)jointCount.Invoke(registry, new object[] { first.transform, second.transform }), Is.EqualTo(1));
        Assert.That(IsTargetConnected(first.transform), Is.True);
        Assert.That(IsTargetConnected(second.transform), Is.True);

        AudioSource executor = CreateExecutor("Stage 2 pair executor");
        object handle = Begin(pairNail, executor, 0.2f);
        yield return new WaitForSeconds(0.03f);
        Assert.That(Cancel(pairNail, executor, handle), Is.True);
        Assert.That((int)jointCount.Invoke(registry, new object[] { first.transform, second.transform }), Is.EqualTo(1),
            "Begin and Cancel must leave the joint attached until a valid Commit.");
        Assert.That(IsTargetConnected(first.transform), Is.True);
        Assert.That(IsTargetConnected(second.transform), Is.True);
        Assert.That((bool)pairNail.GetType().GetProperty("BlocksPickup", PublicInstance).GetValue(pairNail), Is.True);

        object commitHandle = Begin(pairNail, executor, 0.05f);
        Assert.That(IsValid(commitHandle), Is.True);
        yield return new WaitForSeconds(0.07f);
        Assert.That(Commit(pairNail, executor, commitHandle), Is.True);
        yield return null;
        Assert.That((int)jointCount.Invoke(registry, new object[] { first.transform, second.transform }), Is.EqualTo(0));
        Assert.That(IsTargetConnected(first.transform), Is.False);
        Assert.That(IsTargetConnected(second.transform), Is.False);
    }

    [UnityTest]
    public IEnumerator DestroyedExecutorDisabledTargetAndDisabledNailCancelAndPermitFreshAttempts()
    {
        nail = CreateNail(out _);
        AudioSource executor = CreateExecutor("Stage 2 destroyable executor");
        object handle = Begin(nail, executor, 1f);
        Object.Destroy(executor.gameObject);
        yield return null;
        Assert.That(Progress(nail), Is.EqualTo(0f), "Destroying the exact executor clears its reservation.");
        Assert.That(Commit(nail, executor, handle), Is.False);

        AudioSource replacement = CreateExecutor("Stage 2 replacement executor");
        object replacementHandle = Begin(nail, replacement, 1f);
        Transform target = (Transform)nail.GetType().GetField("embeddedTarget", PrivateInstance).GetValue(nail);
        target.gameObject.SetActive(false);
        yield return null;
        Assert.That(Progress(nail), Is.EqualTo(0f), "An inactive target invalidates the reservation.");
        Assert.That(Commit(nail, replacement, replacementHandle), Is.False);
        yield return null;
        Assert.That(nail == null, Is.True,
            "The existing nail lifecycle despawns an attachment whose target becomes inactive.");

        nail = CreateNail(out _);
        object finalHandle = Begin(nail, replacement, 1f);
        ((Behaviour)nail).enabled = false;
        Assert.That(Progress(nail), Is.EqualTo(0f), "Disabling the nail removes its local reservation observers and state.");
        Assert.That(Commit(nail, replacement, finalHandle), Is.False);
        ((Behaviour)nail).enabled = true;
        Assert.That(IsValid(Begin(nail, replacement, 0.05f)), Is.True,
            "A reenabled nail accepts a fresh attempt with a new handle.");
    }

    [UnityTest]
    public IEnumerator FreshCommitReassessmentRejectsNewCompletionInTheSameFrame()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity panel prefabs available only in the Editor.");
        yield break;
#else
        Assert.That(Unity.Netcode.NetworkManager.Singleton == null
            || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
        BuildPanelFixture();
        Vector2[] corners = { new(-0.5f, -0.6f), new(0.5f, -0.6f), new(-0.5f, 0.6f), new(0.5f, 0.6f) };
        for (int i = 0; i < 3; i++) CreatePanelPairNail(panel.transform, corners[i]);
        Physics.SyncTransforms();
        panelState.GetType().GetMethod("Update", PrivateInstance).Invoke(panelState, null);
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.False);
        Assert.That(GetPanelQuarterMask(panelState, panelMarker), Is.EqualTo(0x7));

        Component pending = CreatePanelPairNail(panel.transform, new Vector2(-0.7f, 0.7f), "Partial");
        AudioSource executor = CreateExecutor("Stage 2 panel executor");
        object handle = Begin(pending, executor, 0.02f);
        Assert.That(IsValid(handle), Is.True);

        yield return new WaitForSeconds(0.04f);
        CreatePanelPairNail(panel.transform, corners[3]);
        Physics.SyncTransforms();
        Assert.That(Commit(pending, executor, handle), Is.False,
            "A fresh same-frame recheck sees the newly complete quarter mask and cancels before extraction.");
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.True);
        Assert.That(Progress(pending), Is.EqualTo(0f));
        Assert.That(CrowbarReadState(pending).GetType().GetProperty("Availability", PublicInstance)
            .GetValue(CrowbarReadState(pending)).ToString(), Is.EqualTo("PermanentlyMounted"));
        Assert.That(IsValid(Begin(pending, executor, 0.1f)), Is.False,
            "The public status and begin path both honor the completed panel claim.");
        Assert.That((bool)pending.GetType().GetProperty("BlocksPickup", PublicInstance).GetValue(pending), Is.True);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator SmallPanelMotionKeepsTheReservationButFreshFailureReassessmentCancelsIt()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity panel prefabs available only in the Editor.");
        yield break;
#else
        Assert.That(Unity.Netcode.NetworkManager.Singleton == null
            || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
        BuildPanelFixture();
        Component[] nails = new Component[3];
        Vector2[] corners = { new(-0.5f, -0.6f), new(0.5f, -0.6f), new(-0.5f, 0.6f) };
        for (int i = 0; i < nails.Length; i++) nails[i] = CreatePanelPairNail(panel.transform, corners[i]);
        Physics.SyncTransforms();
        panelState.GetType().GetMethod("Update", PrivateInstance).Invoke(panelState, null);
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.False);
        AudioSource executor = CreateExecutor("Stage 2 reset executor");
        object handle = Begin(nails[0], executor, 0.02f);
        Assert.That(IsValid(handle), Is.True);
        panel.transform.position += Vector3.right * 0.001f;
        Physics.SyncTransforms();
        panelState.GetType().GetMethod("Update", PrivateInstance).Invoke(panelState, null);
        Assert.That(IsValid(CrowbarHandle(nails[0])), Is.True,
            "Small motion that leaves the assessment unchanged does not cancel the reservation.");

        yield return new WaitForSeconds(0.04f);
        Assert.That(CrowbarReadState(nails[0]).GetType().GetProperty("Availability", PublicInstance)
            .GetValue(CrowbarReadState(nails[0])).ToString(), Is.EqualTo("Reserved"),
            "The reservation survives unchanged assessment before the large movement.");
        panel.transform.position += Vector3.right * 4f;
        Physics.SyncTransforms();
        Assert.That(Commit(nails[0], executor, handle), Is.False,
            "Commit's fresh assessment detects the Failure change before the regular section Update.");
        Assert.That(GetPanelFailure(panelState, panelMarker), Is.EqualTo("HorizontalOffset"));
        Assert.That(Progress(nails[0]), Is.EqualTo(0f));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator FreshCommitReassessmentRejectsMaskChangeThatDoesNotCompletePanel()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity panel prefabs available only in the Editor.");
        yield break;
#else
        Assert.That(Unity.Netcode.NetworkManager.Singleton == null
            || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
        BuildPanelFixture();
        Vector2[] corners = { new(-0.5f, -0.6f), new(0.5f, -0.6f), new(-0.5f, 0.6f) };
        CreatePanelPairNail(panel.transform, corners[0]);
        Component pending = CreatePanelPairNail(panel.transform, new Vector2(-0.7f, 0.7f), "Partial");
        Physics.SyncTransforms();
        panelState.GetType().GetMethod("Update", PrivateInstance).Invoke(panelState, null);
        Assert.That(GetPanelQuarterMask(panelState, panelMarker), Is.EqualTo(0x1));
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.False);
        AudioSource executor = CreateExecutor("Stage 2 mask executor");
        object handle = Begin(pending, executor, 0.02f);
        Assert.That(IsValid(handle), Is.True);
        yield return new WaitForSeconds(0.04f);
        CreatePanelPairNail(panel.transform, corners[1]);
        Physics.SyncTransforms();
        Assert.That(Commit(pending, executor, handle), Is.False,
            "Fresh assessment cancels on a changed quarter mask even while the panel remains incomplete.");
        Assert.That(GetPanelQuarterMask(panelState, panelMarker), Is.EqualTo(0x3));
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.False);
        Assert.That(Progress(pending), Is.EqualTo(0f));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator AcceptedIncompletePanelHammerResetCancelsItsActiveExtraction()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity panel prefabs available only in the Editor.");
        yield break;
#else
        Assert.That(Unity.Netcode.NetworkManager.Singleton == null
            || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
        BuildPanelFixture();
        Component[] nails = new Component[3];
        Vector2[] corners = { new(-0.5f, -0.6f), new(0.5f, -0.6f), new(-0.5f, 0.6f) };
        for (int i = 0; i < nails.Length; i++) nails[i] = CreatePanelPairNail(panel.transform, corners[i]);
        Physics.SyncTransforms();
        panelState.GetType().GetMethod("Update", PrivateInstance).Invoke(panelState, null);
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.False);
        AudioSource executor = CreateExecutor("Stage 2 panel reset executor");
        object handle = Begin(nails[0], executor, 1f);
        Assert.That(IsValid(handle), Is.True);
        MethodInfo reset = panelState.GetType().GetMethod("TryResetIncompletePanelWithHammer", PrivateInstance);
        Assert.That((bool)reset.Invoke(panelState, new object[] { panelMarker, Vector3.right }), Is.True);
        Assert.That(Progress(nails[0]), Is.EqualTo(0f), "Removing nails in the accepted panel reset cancels active extraction.");
        Assert.That(Commit(nails[0], executor, handle), Is.False);
        yield return null;
#endif
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        foreach (ScriptableObject profile in profiles) if (profile != null) Object.Destroy(profile);
        roots.Clear();
        profiles.Clear();
        yield return null;
    }

    private Component CreateNail(out GameObject instance)
    {
#if UNITY_EDITOR
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(prefab, Is.Not.Null);
        System.Type type = System.Type.GetType("MountingNail, Assembly-CSharp");
        Component source = prefab.GetComponent(type);
        profiles.Add(Object.Instantiate((ScriptableObject)type.GetProperty("Profile", PublicInstance).GetValue(source)));
        GameObject inactive = new("Stage 2 extraction nail fixture");
        roots.Add(inactive);
        inactive.SetActive(false);
        instance = Object.Instantiate(prefab, inactive.transform);
        Component result = instance.GetComponent(type);
        type.GetField("profile", PrivateInstance).SetValue(result, profiles[^1]);
        GameObject target = new("Stage 2 extraction target");
        roots.Add(target);
        Rigidbody targetBody = target.AddComponent<Rigidbody>();
        targetBody.isKinematic = true;
        targetBody.useGravity = false;
        target.AddComponent<BoxCollider>();
        type.GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(result,
            new object[] { target.transform, null, target.transform.position, Vector3.forward });
        type.GetMethod("SetHitCounts", PrivateInstance).Invoke(result, new object[] { 1, 3, 0.02f });
        type.GetMethod("SetState", PrivateInstance).Invoke(result,
            new[] { System.Enum.Parse(System.Type.GetType("MountingNailState, Assembly-CSharp"), "Partial") });
        inactive.SetActive(true);
        return result;
#else
        Assert.Fail("The test fixture requires the project's authored Unity nail prefab.");
        instance = null;
        return null;
#endif
    }

    private GameObject CreatePhysicsTarget(string name)
    {
        GameObject target = new(name);
        roots.Add(target);
        Rigidbody body = target.AddComponent<Rigidbody>();
        body.useGravity = false;
        target.AddComponent<BoxCollider>();
        return target;
    }

#if UNITY_EDITOR
    private void BuildPanelFixture()
    {
        System.Type sectionType = System.Type.GetType("BridgeDeckSection, Assembly-CSharp");
        System.Type stateType = System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        System.Type siteType = System.Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        System.Type panelType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        System.Type markerType = System.Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        GameObject sectionRoot = new("Stage 2 extraction panel section");
        roots.Add(sectionRoot);
        Component section = sectionRoot.AddComponent(sectionType);
        panelState = new GameObject("Stage 2 extraction section state").AddComponent(stateType);
        roots.Add(panelState.gameObject);
        panelState.transform.SetParent(sectionRoot.transform, false);
        sectionType.GetField("physicalPanelState", PrivateInstance).SetValue(section, panelState);
        GameObject slotPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelBridgeComponent.prefab");
        GameObject slot = Object.Instantiate(slotPrefab, sectionRoot.transform);
        roots.Add(slot);
        slot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Component slotBridge = slot.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"));
        if (slotBridge != null) ((Behaviour)slotBridge).enabled = false;
        foreach (Collider collider in slot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        Component site = slot.GetComponent(siteType);
        FieldInfo slotsField = sectionType.GetField("panelSlots", PrivateInstance);
        System.Array slots = System.Array.CreateInstance(slotsField.FieldType.GetElementType(), 1);
        slots.SetValue(site, 0);
        slotsField.SetValue(section, slots);
        siteType.GetMethod("ConfigureSection", PublicInstance).Invoke(site, new object[] { section, 0, false });

        GameObject girderRoot = new("Stage 2 extraction main girder");
        roots.Add(girderRoot);
        Rigidbody girderBody = girderRoot.AddComponent<Rigidbody>();
        girderBody.useGravity = false;
        girderBody.isKinematic = true;
        girderRoot.AddComponent<BoxCollider>();
        girder = girderRoot.AddComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"));
        ((Behaviour)girder).enabled = false;
        girder.GetType().GetField("componentID", PrivateInstance).SetValue(girder, 1);
        FieldInfo girdersField = sectionType.GetField("mainGirders", PrivateInstance);
        System.Array girders = System.Array.CreateInstance(girdersField.FieldType.GetElementType(), 1);
        girders.SetValue(girder, 0);
        girdersField.SetValue(section, girders);

        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
        GameObject panelObject = Object.Instantiate(panelPrefab);
        roots.Add(panelObject);
        panel = panelObject.GetComponent(panelType);
        panelType.GetField("isPhysicalDeckPanel", PrivateInstance).SetValue(panel, true);
        panelMarker = panelObject.GetComponent(System.Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp"));
        panelObject.transform.SetPositionAndRotation(slot.transform.position, slot.transform.rotation);
        Rigidbody panelBody = panelObject.GetComponent<Rigidbody>();
        panelBody.useGravity = false;
        panelBody.isKinematic = false;
    }

    private Component CreatePanelPairNail(Transform first, Vector2 localXZ, string state = "EmbeddedPair")
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        System.Type type = System.Type.GetType("MountingNail, Assembly-CSharp");
        Component source = prefab.GetComponent(type);
        profiles.Add(Object.Instantiate((ScriptableObject)type.GetProperty("Profile", PublicInstance).GetValue(source)));
        GameObject instance = Object.Instantiate(prefab);
        roots.Add(instance);
        Component result = instance.GetComponent(type);
        type.GetField("profile", PrivateInstance).SetValue(result, profiles[^1]);
        Transform second = state == "EmbeddedPair" ? girder.transform : null;
        Vector3 entry = first.TransformPoint(new Vector3(localXZ.x, 0.2f, localXZ.y));
        type.GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(result,
            new object[] { first, second, entry, -first.up });
        type.GetMethod("SetHitCounts", PrivateInstance).Invoke(result, new object[] { 1, 3, 0.02f });
        type.GetMethod("SetState", PrivateInstance).Invoke(result,
            new[] { System.Enum.Parse(System.Type.GetType("MountingNailState, Assembly-CSharp"), state) });
        if (state == "EmbeddedPair")
            Assert.That(type.GetMethod("TryRegisterPairConnection", PrivateInstance).Invoke(result, null), Is.True);
        return result;
    }

    private static bool IsPanelComplete(Component state, Component marker)
    {
        object[] args = { marker, null };
        if (!(bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args)) return false;
        return (bool)args[1].GetType().GetField("Complete").GetValue(args[1]);
    }

    private static byte GetPanelQuarterMask(Component state, Component marker)
    {
        object[] args = { marker, null };
        Assert.That((bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args), Is.True);
        return (byte)args[1].GetType().GetField("QuarterMask").GetValue(args[1]);
    }

    private static string GetPanelFailure(Component state, Component marker)
    {
        object[] args = { marker, null };
        Assert.That((bool)state.GetType().GetMethod("TryGetPanelState", PublicInstance).Invoke(state, args), Is.True);
        object failure = args[1].GetType().GetField("Failure").GetValue(args[1]);
        System.Type failureType = System.Type.GetType("BridgeDeckPanelPlacementFailure, Assembly-CSharp");
        return System.Enum.ToObject(failureType, failure).ToString();
    }
#endif

    private AudioSource CreateExecutor(string name)
    {
        GameObject executor = new(name);
        roots.Add(executor);
        return executor.AddComponent<AudioSource>();
    }

    private static object Begin(Component targetNail, Behaviour executor, float duration)
    {
        object[] args = { executor, duration, null };
        bool accepted = (bool)targetNail.GetType().GetMethod("TryBeginCrowbarExtraction", PublicInstance)
            .Invoke(targetNail, args);
        return accepted ? args[2] : System.Activator.CreateInstance(args[2].GetType());
    }

    private static bool IsValid(object handle) => (bool)handle.GetType().GetProperty("IsValid", PublicInstance).GetValue(handle);
    private static float Progress(Component targetNail) => (float)targetNail.GetType()
        .GetProperty("CrowbarExtractionProgressNormalized", PublicInstance).GetValue(targetNail);
    private static object CrowbarReadState(Component targetNail) => targetNail.GetType()
        .GetProperty("CrowbarExtractionReadState", PublicInstance).GetValue(targetNail);
    private static object CrowbarHandle(Component targetNail) => CrowbarReadState(targetNail).GetType()
        .GetProperty("Handle", PublicInstance).GetValue(CrowbarReadState(targetNail));
    private static bool Cancel(Component targetNail, Behaviour executor, object handle) => (bool)targetNail.GetType()
        .GetMethod("CancelCrowbarExtraction", PublicInstance).Invoke(targetNail, new[] { executor, handle });
    private static bool Commit(Component targetNail, Behaviour executor, object handle) => (bool)targetNail.GetType()
        .GetMethod("TryCommitCrowbarExtraction", PublicInstance).Invoke(targetNail, new[] { executor, handle });
    private static void Invoke(Component component, string method, params object[] args) => component.GetType()
        .GetMethod(method, PrivateInstance).Invoke(component, args);

    private static bool IsTargetConnected(Transform target) =>
        (bool)System.Type.GetType("MountingNailSceneState, Assembly-CSharp")
            .GetMethod("IsTargetConnected", BindingFlags.Static | PublicInstance)
            .Invoke(null, new object[] { target });
}
