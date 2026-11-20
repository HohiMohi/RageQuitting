using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

public class MountingNailExtractionStage1PlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private readonly List<GameObject> roots = new();
    private readonly List<ScriptableObject> profiles = new();
    private NetworkManager previousSingleton;
    private Component nail;
    private Component panelState;
    private Component panelMarker;
    private Component panel;
    private Component section;
    private Component girder;

    [UnityTest]
    public IEnumerator ExtractionAcceptsOnlyEmbeddedStatesAndIsIdempotent()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        previousSingleton = NetworkManager.Singleton;
        GameObject target = CreateTarget("Extraction target");
        string[] embeddedStates = { "Partial", "EmbeddedSingle", "EmbeddedPair" };
        foreach (string state in embeddedStates)
        {
            nail = CreateNail(out GameObject instance);
            Configure(nail, target.transform, state == "EmbeddedPair" ? CreateTarget("Second target").transform : null, state);
            Assert.That(Extract(nail), Is.True, state);
            Assert.That(Extract(nail), Is.False, "A second same-frame request must be rejected.");
            Assert.That((bool)nail.GetType().GetProperty("BlocksPickup", PublicInstance).GetValue(nail), Is.True);
            Rigidbody removedBody = instance.GetComponent<Rigidbody>();
            Assert.That(removedBody.isKinematic, Is.True, "An extracted nail must not re-enter dynamic physics before despawn.");
            Collider[] removedColliders = instance.GetComponentsInChildren<Collider>(true);
            Assert.That(removedColliders.Length, Is.GreaterThan(0), "The authored nail fixture must expose pickup/physics colliders.");
            foreach (Collider collider in removedColliders)
                Assert.That(collider.enabled, Is.False, "An extracted nail must disable its pickup/physics colliders.");
            Object.Destroy(instance);
        }

        string[] rejectedStates = { "Loose", "Held", "Prepared" };
        foreach (string state in rejectedStates)
        {
            nail = CreateNail(out GameObject instance);
            Configure(nail, target.transform, null, state);
            Assert.That(Extract(nail), Is.False, state);
            Assert.That(instance, Is.Not.Null);
            Object.Destroy(instance);
        }

        Component inactiveNail = CreateNail(out GameObject inactiveNailObject);
        Configure(inactiveNail, target.transform, null, "EmbeddedSingle");
        inactiveNailObject.SetActive(false);
        Assert.That(Extract(inactiveNail), Is.False, "An inactive/dead nail cannot be extracted.");
        inactiveNailObject.SetActive(true);
        Object.Destroy(inactiveNailObject);

        nail = CreateNail(out GameObject deadTargetNailObject);
        Configure(nail, target.transform, null, "EmbeddedSingle");
        target.SetActive(false);
        Assert.That(Extract(nail), Is.False, "An inactive embedded target is no longer a valid attachment.");
        target.SetActive(true);
        Object.Destroy(deadTargetNailObject);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator ExtractingOneNailPreservesSharedEdgeUntilItsLastNailIsRemoved()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        previousSingleton = NetworkManager.Singleton;
        GameObject first = CreateTarget("Shared first target");
        GameObject second = CreateTarget("Shared second target");
        GameObject third = CreateTarget("Shared third target");
        nail = CreateNail(out GameObject firstNailObject);
        Configure(nail, first.transform, second.transform, "EmbeddedPair");
        Assert.That(Invoke(nail, "TryRegisterPairConnection"), Is.True);
        Component otherNail = CreateNail(out GameObject otherNailObject);
        Configure(otherNail, first.transform, second.transform, "EmbeddedPair");
        Assert.That(Invoke(otherNail, "TryRegisterPairConnection"), Is.True);
        Component chainNail = CreateNail(out GameObject chainNailObject);
        Configure(chainNail, second.transform, third.transform, "EmbeddedPair");
        Assert.That(Invoke(chainNail, "TryRegisterPairConnection"), Is.True);

        System.Type registryType = System.Type.GetType("MountingNailConnectionRegistry, Assembly-CSharp");
        object registry = registryType.GetMethod("ForOfflineScene", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { first.scene });
        MethodInfo count = registryType.GetMethod("PairJointCount", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(count.Invoke(registry, new object[] { first.transform, second.transform }), Is.EqualTo(1));
        Assert.That(Extract(nail), Is.True);
        Assert.That(count.Invoke(registry, new object[] { first.transform, second.transform }), Is.EqualTo(1));
        Assert.That(Extract(otherNail), Is.True);
        Assert.That(count.Invoke(registry, new object[] { first.transform, second.transform }), Is.Zero);
        Assert.That(count.Invoke(registry, new object[] { second.transform, third.transform }), Is.EqualTo(1),
            "Removing AB does not remove the adjacent BC graph edge.");
        yield return new WaitForFixedUpdate();
        Assert.That(second.GetComponents<FixedJoint>().Length + third.GetComponents<FixedJoint>().Length, Is.EqualTo(1));
        Assert.That(Extract(chainNail), Is.True);
        Assert.That(count.Invoke(registry, new object[] { second.transform, third.transform }), Is.Zero);
        Object.Destroy(firstNailObject); Object.Destroy(otherNailObject); Object.Destroy(chainNailObject);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator CompletedPanelClaimsProtectEveryRealAttachmentAndFreshFinalQuarter()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity panel prefabs available only in the Editor.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        previousSingleton = NetworkManager.Singleton;
        BuildPanelFixture();
        GameObject panelObject = panel.gameObject;
        Component[] quarterNails = new Component[4];
        GameObject[] quarterObjects = new GameObject[4];
        Vector2[] corners = { new(-0.5f, -0.6f), new(0.5f, -0.6f), new(-0.5f, 0.6f), new(0.5f, 0.6f) };
        for (int i = 0; i < 3; i++)
            quarterNails[i] = CreatePanelPairNail(panelObject.transform, out quarterObjects[i], corners[i]);
        Physics.SyncTransforms();
        panelState.GetType().GetMethod("Update", PrivateInstance).Invoke(panelState, null);
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.False,
            "The section has just cached an incomplete three-quarter assessment.");
        quarterNails[3] = CreatePanelPairNail(panelObject.transform, out quarterObjects[3], corners[3]);
        Physics.SyncTransforms();

        Assert.That(Extract(quarterNails[3]), Is.False,
            "A fresh assessment must commit protection when the last quarter was completed in this frame.");
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.True);

        Component extra = CreatePanelPairNail(panelObject.transform, out GameObject extraObject, new Vector2(0.7f, 0.7f));
        Assert.That(Extract(extra), Is.False, "An extra nail first-targeted into a completed panel remains protected.");
        Component partialOnPanel = CreatePanelPairNail(panelObject.transform, out GameObject partialObject,
            new Vector2(-0.7f, 0.7f), state: "Partial", register: false);
        Assert.That(Extract(partialOnPanel), Is.False, "A Partial nail first-targeted into a completed panel remains protected.");
        Component singleOnPanel = CreatePanelPairNail(panelObject.transform, out GameObject singleObject,
            new Vector2(0.7f, -0.7f), state: "EmbeddedSingle", register: false);
        Assert.That(Extract(singleOnPanel), Is.False, "An EmbeddedSingle nail first-targeted into a completed panel remains protected.");
        Component actualSecond = CreatePanelPairNail(girder.transform, out GameObject secondObject, new Vector2(0.3f, 0.3f),
            panelObject.transform, "EmbeddedPair");
        Assert.That(Extract(actualSecond), Is.False, "An actual pair is protected when the panel is its second target.");
        Component plannedSecond = CreatePanelPairNail(CreateTarget("planned attachment first").transform,
            out GameObject plannedObject, new Vector2(0f, 0f), panelObject.transform, "Partial", register: false);
        Assert.That(Extract(plannedSecond), Is.True,
            "A Partial nail whose future second target is the panel is not yet a real pair connection.");

        // A durable claim remains authoritative after the physical panel moves away from its slot.
        panelObject.transform.position += Vector3.right * 4f;
        AddCloserSectionWithoutWorkflow(panelObject.transform.position);
        Component movedExtra = CreatePanelPairNail(panelObject.transform, out GameObject movedExtraObject, new Vector2(0.8f, 0.8f));
        Assert.That(Extract(movedExtra), Is.False,
            "A durable claim in the original section must protect the panel even when a nearer unconfigured section becomes owner.");
        yield return null;
        Object.Destroy(extraObject); Object.Destroy(partialObject); Object.Destroy(singleObject); Object.Destroy(secondObject);
        Object.Destroy(plannedObject); Object.Destroy(movedExtraObject);
        foreach (GameObject quarterObject in quarterObjects) if (quarterObject != null) Object.Destroy(quarterObject);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator IncompleteAndWrongPositionPanelsRemainExtractable()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses authored Unity panel prefabs available only in the Editor.");
        yield break;
#else
        Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        previousSingleton = NetworkManager.Singleton;
        BuildPanelFixture();
        Vector2[] twoCorners = { new(-0.5f, -0.6f), new(0.5f, -0.6f) };
        for (int i = 0; i < twoCorners.Length; i++) CreatePanelPairNail(panel.transform, out _, twoCorners[i]);
        nail = CreatePanelPairNail(panel.transform, out GameObject incompleteNailObject, new Vector2(-0.5f, 0.6f));
        Assert.That(Extract(nail), Is.True, "A correctly positioned panel with fewer than four quarters is removable.");
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.False);
        Object.Destroy(incompleteNailObject);

        CreatePanelPairNail(panel.transform, out _, new Vector2(-0.5f, 0.6f));
        CreatePanelPairNail(panel.transform, out _, new Vector2(0.5f, 0.6f));
        panel.transform.position += Vector3.right * 4f;
        Physics.SyncTransforms();
        panelState.GetType().GetMethod("Update", PrivateInstance).Invoke(panelState, null);
        Assert.That(GetPanelQuarterMask(panelState, panelMarker), Is.EqualTo(0xF),
            "All four distinct quarters are present before testing the invalid pose.");
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.False,
            "The full quarter mask is still incomplete while the panel pose is outside tolerance.");
        nail = CreatePanelPairNail(panel.transform, out GameObject misplacedNailObject, new Vector2(0.8f, 0.8f));
        Assert.That(Extract(nail), Is.True, "Four quarters do not protect a panel outside the valid slot pose.");
        Assert.That(IsPanelComplete(panelState, panelMarker), Is.False);
        Object.Destroy(misplacedNailObject);
        yield return null;
#endif
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        foreach (ScriptableObject profile in profiles) if (profile != null) Object.Destroy(profile);
        if (previousSingleton != null) previousSingleton.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
        yield return null;
    }

#if UNITY_EDITOR
    private GameObject CreateTarget(string name)
    {
        GameObject target = new(name);
        roots.Add(target);
        target.AddComponent<Rigidbody>().useGravity = false;
        target.AddComponent<BoxCollider>();
        return target;
    }

    private void BuildPanelFixture()
    {
        System.Type sectionType = System.Type.GetType("BridgeDeckSection, Assembly-CSharp");
        System.Type stateType = System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        System.Type siteType = System.Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        System.Type panelType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        System.Type markerType = System.Type.GetType("BridgeDeckPanelMounting, Assembly-CSharp");
        GameObject sectionRoot = new("Stage 1 extraction panel section");
        roots.Add(sectionRoot);
        section = sectionRoot.AddComponent(sectionType);
        panelState = new GameObject("Stage 1 extraction section state").AddComponent(stateType);
        roots.Add(panelState.gameObject);
        panelState.transform.SetParent(sectionRoot.transform, false);
        sectionType.GetField("physicalPanelState", PrivateInstance).SetValue(section, panelState);

        GameObject slotPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelBridgeComponent.prefab");
        GameObject slot = Object.Instantiate(slotPrefab, sectionRoot.transform);
        roots.Add(slot);
        slot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Component bridgeComponent = slot.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"));
        if (bridgeComponent != null) ((Behaviour)bridgeComponent).enabled = false;
        foreach (Collider collider in slot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        Component site = slot.GetComponent(siteType);
        FieldInfo slotsField = sectionType.GetField("panelSlots", PrivateInstance);
        System.Array slots = System.Array.CreateInstance(slotsField.FieldType.GetElementType(), 1);
        slots.SetValue(site, 0); slotsField.SetValue(section, slots);
        siteType.GetMethod("ConfigureSection", PublicInstance).Invoke(site, new object[] { section, 0, false });

        GameObject girderRoot = new("Stage 1 extraction main girder");
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
        girders.SetValue(girder, 0); girdersField.SetValue(section, girders);

        GameObject panelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelMountable.prefab");
        GameObject panelObject = Object.Instantiate(panelPrefab);
        roots.Add(panelObject);
        panel = panelObject.GetComponent(panelType);
        panelType.GetField("isPhysicalDeckPanel", PrivateInstance).SetValue(panel, true);
        panelMarker = panelObject.GetComponent(markerType);
        panelObject.transform.SetPositionAndRotation(slot.transform.position, slot.transform.rotation);
        Rigidbody body = panelObject.GetComponent<Rigidbody>();
        body.useGravity = false; body.isKinematic = false;
    }

    private void AddCloserSectionWithoutWorkflow(Vector3 position)
    {
        System.Type sectionType = System.Type.GetType("BridgeDeckSection, Assembly-CSharp");
        System.Type stateType = System.Type.GetType("BridgeDeckPanelSectionNetworkState, Assembly-CSharp");
        System.Type siteType = System.Type.GetType("BridgeDeckPanelConstructionSite, Assembly-CSharp");
        GameObject root = new("Closer section without panel workflow");
        roots.Add(root);
        root.AddComponent(sectionType);
        Component state = root.AddComponent(stateType);
        GameObject sitePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenDeckPanelBridgeComponent.prefab");
        GameObject siteObject = Object.Instantiate(sitePrefab, root.transform);
        roots.Add(siteObject);
        siteObject.transform.position = position;
        Component siteBridge = siteObject.GetComponent(System.Type.GetType("BridgeComponent, Assembly-CSharp"));
        ((Behaviour)siteBridge).enabled = false;
        siteBridge.GetType().GetField("bridgeComponentSO", PrivateInstance).SetValue(siteBridge, null);
        Component site = siteObject.GetComponent(siteType);
        FieldInfo slotsField = sectionType.GetField("panelSlots", PrivateInstance);
        System.Array slots = System.Array.CreateInstance(slotsField.FieldType.GetElementType(), 1);
        slots.SetValue(site, 0);
        slotsField.SetValue(root.GetComponent(sectionType), slots);
        // Keep the state active and compatible, but intentionally leave its workflow unconfigured.
        Assert.That(state.GetType().GetField("workflow", PrivateInstance).GetValue(state), Is.Null);
    }

    private Component CreatePanelPairNail(Transform first, out GameObject instance, Vector2 localXZ,
        Transform second = null, string state = "EmbeddedPair", bool register = true)
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        System.Type nailType = System.Type.GetType("MountingNail, Assembly-CSharp");
        Component source = prefab.GetComponent(nailType);
        ScriptableObject profile = Object.Instantiate((ScriptableObject)nailType.GetProperty("Profile", PublicInstance).GetValue(source));
        profiles.Add(profile);
        instance = Object.Instantiate(prefab);
        roots.Add(instance);
        Component result = instance.GetComponent(nailType);
        nailType.GetField("profile", PrivateInstance).SetValue(result, profile);
        Vector3 entry = first.TransformPoint(new Vector3(localXZ.x, 0.2f, localXZ.y));
        nailType.GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(result,
            new object[] { first, second ?? girder.transform, entry, -first.up });
        nailType.GetMethod("SetHitCounts", PrivateInstance).Invoke(result, new object[] { 1, 1, 0.02f });
        nailType.GetMethod("SetState", PrivateInstance).Invoke(result,
            new[] { System.Enum.Parse(System.Type.GetType("MountingNailState, Assembly-CSharp"), state) });
        if (register && state == "EmbeddedPair")
            Assert.That(nailType.GetMethod("TryRegisterPairConnection", PrivateInstance).Invoke(result, null), Is.True);
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

    private Component CreateNail(out GameObject instance)
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(prefab, Is.Not.Null);
        System.Type nailType = System.Type.GetType("MountingNail, Assembly-CSharp");
        Component prefabNail = prefab.GetComponent(nailType);
        ScriptableObject profile = Object.Instantiate((ScriptableObject)nailType.GetProperty("Profile", PublicInstance).GetValue(prefabNail));
        profiles.Add(profile);
        GameObject inactive = new("Stage 1 extraction nail fixture");
        roots.Add(inactive);
        inactive.SetActive(false);
        instance = Object.Instantiate(prefab, inactive.transform);
        nailType.GetField("profile", PrivateInstance).SetValue(instance.GetComponent(nailType), profile);
        inactive.SetActive(true);
        return instance.GetComponent(nailType);
    }

    private static void Configure(Component targetNail, Transform first, Transform second, string state)
    {
        System.Type type = targetNail.GetType();
        type.GetMethod("SetEmbeddedTargets", PrivateInstance).Invoke(targetNail,
            new object[] { first, second, first.position, Vector3.forward });
        type.GetMethod("SetHitCounts", PrivateInstance).Invoke(targetNail, new object[] { 1, 1, 0.02f });
        type.GetMethod("SetState", PrivateInstance).Invoke(targetNail,
            new[] { System.Enum.Parse(System.Type.GetType("MountingNailState, Assembly-CSharp"), state) });
    }

    private static bool Extract(Component targetNail) => (bool)targetNail.GetType()
        .GetMethod("TryExtractForCrowbar", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(targetNail, null);
    private static bool Invoke(Component targetNail, string method) => (bool)targetNail.GetType()
        .GetMethod(method, PrivateInstance).Invoke(targetNail, null);
#endif
}
