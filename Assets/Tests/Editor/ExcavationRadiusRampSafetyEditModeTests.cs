using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ExcavationRadiusRampSafetyEditModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string BucketPath = "Assets/Prefabs/New/Substances/PortableBucket.prefab";
    private const string WheelbarrowPath = "Assets/Prefabs/New/Wheelbarrow.prefab";

    [Test]
    public void LooseSoilBrushRadiusDefaultAndPrefabArePointEightMeters()
    {
        GameObject fixture = new GameObject("Radius default fixture");
        try
        {
            Component container = fixture.AddComponent(System.Type.GetType("PortableSubstanceContainer, Assembly-CSharp"));
            Assert.That(ReadFloat(container, "looseSoilBrushRadius"), Is.EqualTo(0.8f).Within(0.0001f));

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BucketPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(ReadFloat(prefab.GetComponent(container.GetType()), "looseSoilBrushRadius"),
                Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(ReadFloat(prefab.GetComponent(container.GetType()), "looseSoilUnitsPerSecond"),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(ReadFloat(prefab.GetComponent(container.GetType()), "looseSoilWholeLayerTailThreshold"),
                Is.EqualTo(0.05f).Within(0.0001f));
        }
        finally { Object.DestroyImmediate(fixture); }
    }

    [Test]
    public void DisabledRotatedRampColliderUsesItsAuthoredGeometryAndAvoidsWorldAabbFalsePositive()
    {
        GameObject root = new GameObject("Ramp volume geometry fixture");
        GameObject ramp = new GameObject("Disabled ramp collider");
        GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        List<GameObject> saturationFixtures = new List<GameObject>();
        try
        {
            ramp.transform.SetParent(root.transform, false);
            ramp.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            BoxCollider rampCollider = ramp.AddComponent<BoxCollider>();
            rampCollider.size = new Vector3(1f, 0.2f, 4f);
            rampCollider.enabled = false;
            Component volume = root.AddComponent(System.Type.GetType("FoundationExcavationVolume, Assembly-CSharp"));
            volume.GetType().GetField("exitRampCollider", Instance).SetValue(volume, rampCollider);
            Rigidbody body = prop.AddComponent<Rigidbody>();
            body.isKinematic = true;
            prop.transform.position = new Vector3(1.55f, 0f, -1.55f);
            Physics.SyncTransforms();
            Assert.That((bool)volume.GetType().GetMethod("IsExitRampVolumeClear").Invoke(volume, null), Is.True,
                "This prop is inside the rotated box's world AABB but outside the actual oriented collider.");

            prop.transform.position = new Vector3(0f, 0f, 0.35f);
            Physics.SyncTransforms();
            Assert.That((bool)volume.GetType().GetMethod("IsExitRampVolumeClear").Invoke(volume, null), Is.False,
                "A kinematic Rigidbody overlapping the future ramp must block activation even though its collider is disabled.");

            Physics.IgnoreCollision(rampCollider, prop.GetComponent<Collider>(), true);
            Assert.That((bool)volume.GetType().GetMethod("IsExitRampVolumeClear").Invoke(volume, null), Is.True,
                "Explicitly ignored collider pairs must not block the ramp.");
            Physics.IgnoreCollision(rampCollider, prop.GetComponent<Collider>(), false);

            body.isKinematic = false;
            Object.DestroyImmediate(body);
            Assert.That((bool)volume.GetType().GetMethod("IsExitRampVolumeClear").Invoke(volume, null), Is.True,
                "Static scene colliders do not count as movable occupants.");
            prop.GetComponent<Collider>().isTrigger = true;
            Assert.That((bool)volume.GetType().GetMethod("IsExitRampVolumeClear").Invoke(volume, null), Is.True,
                "Trigger colliders do not block ramp activation.");

            prop.GetComponent<Collider>().isTrigger = false;
            for (int i = 0; i < 65; i++)
            {
                GameObject staticContact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                staticContact.transform.position = new Vector3(0f, 0f, 0.35f);
                staticContact.transform.localScale = Vector3.one * 0.05f;
                saturationFixtures.Add(staticContact);
            }
            Physics.SyncTransforms();
            Assert.That((bool)volume.GetType().GetMethod("IsExitRampVolumeClear").Invoke(volume, null), Is.True,
                "A saturated first overlap buffer grows and rechecks so ignored static contacts cannot be mistaken for an occupant.");

            GameObject disabledPlayer = new GameObject("Disabled player capsule fixture");
            try
            {
                CharacterController controller = disabledPlayer.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.25f;
                controller.enabled = false;
                disabledPlayer.transform.position = Vector3.zero;
                Physics.SyncTransforms();
                Assert.That((bool)volume.GetType().GetMethod("IsExitRampVolumeClear").Invoke(volume, null), Is.False,
                    "The offline clearance path finds an active player whose CharacterController is disabled and absent from broadphase.");
                MethodInfo capsuleTest = volume.GetType().GetMethod("CapsuleOverlapsBox", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That((bool)capsuleTest.Invoke(null, new object[] { controller, Vector3.zero,
                    new Vector3(0.52f, 0.12f, 2.02f), Quaternion.Euler(0f, 45f, 0f) }), Is.True,
                    "Disabled player controllers are checked from capsule geometry rather than collider bounds.");
                disabledPlayer.transform.position = new Vector3(1.55f, 0f, -1.55f);
                Assert.That((bool)capsuleTest.Invoke(null, new object[] { controller, Vector3.zero,
                    new Vector3(0.52f, 0.12f, 2.02f), Quaternion.Euler(0f, 45f, 0f) }), Is.False,
                    "The oriented capsule check must reject world-AABB-only overlap.");

                controller.radius = 0.25f;
                controller.height = 1.8f;
                controller.center = Vector3.zero;
                disabledPlayer.transform.position = new Vector3(0.98f, 0f, 0f);
                disabledPlayer.transform.localScale = new Vector3(2f, 1.5f, 1f);
                Assert.That((bool)capsuleTest.Invoke(null, new object[] { controller, Vector3.zero,
                    new Vector3(0.5f, 0.1f, 0.5f), Quaternion.identity }), Is.True,
                    "Non-uniform radial scale expands a disabled CharacterController capsule.");
                disabledPlayer.transform.position = new Vector3(1f, 0f, 0f);
                Assert.That((bool)capsuleTest.Invoke(null, new object[] { controller, Vector3.zero,
                    new Vector3(0.5f, 0.1f, 0.5f), Quaternion.identity }), Is.True,
                    "A tangential capsule/box endpoint contact is treated conservatively as occupied.");
                disabledPlayer.transform.position = new Vector3(1.02f, 0f, 0f);
                Assert.That((bool)capsuleTest.Invoke(null, new object[] { controller, Vector3.zero,
                    new Vector3(0.5f, 0.1f, 0.5f), Quaternion.identity }), Is.False,
                    "The scaled capsule just outside the conservative clearance epsilon is free.");
            }
            finally { Object.DestroyImmediate(disabledPlayer); }
        }
        finally
        {
            foreach (GameObject fixture in saturationFixtures) Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(prop);
            Object.DestroyImmediate(root);
        }
    }

    [TestCase(BucketPath)]
    [TestCase(WheelbarrowPath)]
    public void ProductionPortablePropsBlockTheFutureRampWithTheirAuthoredPhysicalColliders(string prefabPath)
    {
        GameObject volumeRoot = new GameObject("Production prop ramp volume fixture");
        GameObject ramp = new GameObject("Production prop future ramp");
        GameObject prop = null;
        try
        {
            ramp.transform.SetParent(volumeRoot.transform, false);
            BoxCollider rampCollider = ramp.AddComponent<BoxCollider>();
            rampCollider.size = new Vector3(1f, 0.2f, 4f);
            rampCollider.enabled = false;
            Component volume = volumeRoot.AddComponent(System.Type.GetType("FoundationExcavationVolume, Assembly-CSharp"));
            volume.GetType().GetField("exitRampCollider", Instance).SetValue(volume, rampCollider);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            prop.transform.position = new Vector3(0f, 0.1f, 0.2f);
            Physics.SyncTransforms();
            Collider[] colliders = prop.GetComponentsInChildren<Collider>(true);
            Assert.That(System.Array.Exists(colliders, collider => collider != null && collider.enabled && !collider.isTrigger),
                Is.True, prefabPath + " must retain at least one active, non-trigger physical collider.");
            Assert.That((bool)volume.GetType().GetMethod("IsExitRampVolumeClear").Invoke(volume, null), Is.False,
                prefabPath + " physical collider geometry overlaps the future ramp and must block activation.");
        }
        finally
        {
            if (prop != null) Object.DestroyImmediate(prop);
            Object.DestroyImmediate(ramp);
            Object.DestroyImmediate(volumeRoot);
        }
    }

    [Test]
    public void FoundationRampGrantParticipatesInNetworkStateEquality()
    {
        System.Type type = System.Type.GetType("BridgeComponentNetworkState, Assembly-CSharp");
        object withoutGrant = System.Activator.CreateInstance(type, new object[] { 42 });
        object withGrant = System.Activator.CreateInstance(type, new object[] { 42 });
        type.GetField("foundationRampClearanceGranted").SetValue(withGrant, true);
        Assert.That(withoutGrant.Equals(withGrant), Is.False);

        object matching = System.Activator.CreateInstance(type, new object[] { 42 });
        type.GetField("foundationRampClearanceGranted").SetValue(matching, true);
        Assert.That(withGrant.Equals(matching), Is.True);
        Assert.That(withGrant.GetHashCode(), Is.EqualTo(matching.GetHashCode()));
    }

    [Test]
    public void FoundationRampGrantRoundTripsThroughFullStateWriterAndReader()
    {
        System.Type stateType = System.Type.GetType("BridgeComponentNetworkState, Assembly-CSharp");
        System.Type managerType = System.Type.GetType("GameplayManager, Assembly-CSharp");
        System.Type writerType = System.Type.GetType("Unity.Netcode.FastBufferWriter, Unity.Netcode.Runtime");
        System.Type readerType = System.Type.GetType("Unity.Netcode.FastBufferReader, Unity.Netcode.Runtime");
        System.Type allocatorType = FindLoadedType("Unity.Collections.Allocator");
        Assert.That(stateType, Is.Not.Null);
        Assert.That(managerType, Is.Not.Null);
        Assert.That(writerType, Is.Not.Null);
        Assert.That(readerType, Is.Not.Null);
        Assert.That(allocatorType, Is.Not.Null);

        GameObject managerRoot = new GameObject("Full state codec fixture");
        object writer = null;
        object reader = null;
        try
        {
            Component manager = managerRoot.AddComponent(managerType);
            object temporaryAllocator = System.Enum.Parse(allocatorType, "Temp");
            writer = System.Activator.CreateInstance(writerType,
                new[] { (object)256, temporaryAllocator, 1024 });
            object state = System.Activator.CreateInstance(stateType, new object[] { 42 });
            stateType.GetField("foundationRampClearanceGranted").SetValue(state, true);
            stateType.GetField("constructionValueA").SetValue(state, 17);
            stateType.GetField("mountAlignmentCandidateNetworkObjectId").SetValue(state, 123456789UL);
            managerType.GetMethod("WriteState", Instance).Invoke(manager, new[] { writer, state });

            ConstructorInfo readerConstructor = null;
            foreach (ConstructorInfo candidate in readerType.GetConstructors(Instance))
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length > 1 && parameters[0].ParameterType == writerType)
                {
                    readerConstructor = candidate;
                    break;
                }
            }
            Assert.That(readerConstructor, Is.Not.Null);
            ParameterInfo[] readerParameters = readerConstructor.GetParameters();
            object[] readerArguments = new object[readerParameters.Length];
            readerArguments[0] = writer;
            readerArguments[1] = temporaryAllocator;
            for (int i = 2; i < readerArguments.Length; i++)
                readerArguments[i] = readerParameters[i].HasDefaultValue ? readerParameters[i].DefaultValue : 0;
            reader = readerConstructor.Invoke(readerArguments);

            object roundTrip = managerType.GetMethod("ReadState", Instance).Invoke(manager, new[] { reader });
            Assert.That(state.Equals(roundTrip), Is.True,
                "The full byte writer/reader must preserve the clearance bit and all existing fields.");
        }
        finally
        {
            DisposeNetcodeBuffer(reader);
            DisposeNetcodeBuffer(writer);
            Object.DestroyImmediate(managerRoot);
        }
    }

    [Test]
    public void ReapplyingGrantedWetConcreteStateKeepsHiddenRampRendererAndActiveCollider()
    {
        GameObject root = new GameObject("Granted ramp visual state fixture");
        GameObject ramp = new GameObject("Ramp visual and collider");
        GameObject recoveryWheelbarrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            ramp.transform.SetParent(root.transform, false);
            MeshRenderer renderer = ramp.AddComponent<MeshRenderer>();
            BoxCollider collider = ramp.AddComponent<BoxCollider>();
            Component volume = root.AddComponent(System.Type.GetType("FoundationExcavationVolume, Assembly-CSharp"));
            volume.GetType().GetField("useLooseSoilHeightField", Instance).SetValue(volume, true);
            volume.GetType().GetField("exitRampRenderer", Instance).SetValue(volume, renderer);
            volume.GetType().GetField("exitRampCollider", Instance).SetValue(volume, collider);
            object stage = System.Enum.Parse(System.Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "ConcreteDrying");
            object failure = System.Enum.Parse(System.Type.GetType("FoundationConcreteFailureState, Assembly-CSharp"), "None");
            MethodInfo apply = volume.GetType().GetMethod("ApplyConcreteState");
            object[] args = { stage, 1, 1, 5f, 10f, failure, 0f, Vector3.one, 0f, true, false };
            apply.Invoke(volume, args);
            Assert.That(renderer.enabled, Is.False, "Wet full concrete keeps the existing hidden-renderer request.");
            Assert.That(collider.enabled, Is.True, "A clearance grant preserves the existing wet-concrete collider request.");
            apply.Invoke(volume, args);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(collider.enabled, Is.True,
                "Repeated visual-state application must not turn off an already granted ramp under an occupant.");

            args[9] = false;
            args[10] = true;
            apply.Invoke(volume, args);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(collider.enabled, Is.False, "Both ramp components stay disabled while clearance is pending.");

            args[5] = System.Enum.Parse(System.Type.GetType("FoundationConcreteFailureState, Assembly-CSharp"), "AwaitingWheelbarrowExit");
            args[10] = false;
            args[9] = false;
            apply.Invoke(volume, args);
            Assert.That(renderer.enabled, Is.False, "Recovery ramp visibility waits for occupancy clearance in loose-soil mode.");
            Assert.That(collider.enabled, Is.False, "An occupied recovery wheelbarrow must not be overlapped by ramp activation.");
            recoveryWheelbarrow.AddComponent<Rigidbody>().isKinematic = true;
            recoveryWheelbarrow.transform.position = new Vector3(0f, 0f, 0.2f);
            Physics.SyncTransforms();
            Assert.That((bool)volume.GetType().GetMethod("IsExitRampVolumeClear").Invoke(volume, null), Is.False,
                "A wheelbarrow occupying the requested recovery ramp blocks activation.");
            Object.DestroyImmediate(recoveryWheelbarrow);
            recoveryWheelbarrow = null;
            args[9] = true;
            apply.Invoke(volume, args);
            Assert.That(renderer.enabled, Is.True, "Once the server grant arrives, recovery keeps its legacy ramp visibility request.");
            Assert.That(collider.enabled, Is.True, "Once clear, recovery keeps its legacy ramp collider request.");
        }
        finally
        {
            Object.DestroyImmediate(ramp);
            Object.DestroyImmediate(root);
            if (recoveryWheelbarrow != null) Object.DestroyImmediate(recoveryWheelbarrow);
        }
    }

    [Test]
    public void FoundationDockRequiresAuthoritativeAndLocalRampReadiness()
    {
        GameObject siteRoot = new GameObject("Foundation dock readiness fixture");
        GameObject volumeRoot = new GameObject("Foundation dock volume fixture");
        try
        {
            GameObject ramp = new GameObject("Foundation dock ramp collider");
            ramp.transform.SetParent(volumeRoot.transform, false);
            BoxCollider rampCollider = ramp.AddComponent<BoxCollider>();
            Component volume = volumeRoot.AddComponent(System.Type.GetType("FoundationExcavationVolume, Assembly-CSharp"));
            volume.GetType().GetField("useLooseSoilHeightField", Instance).SetValue(volume, true);
            volume.GetType().GetField("exitRampCollider", Instance).SetValue(volume, rampCollider);

            Component site = siteRoot.AddComponent(System.Type.GetType("BridgeConstructionSite, Assembly-CSharp"));
            System.Type siteType = site.GetType();
            siteType.GetField("excavationVolume", Instance).SetValue(site, volume);
            siteType.GetField("currentStage", Instance).SetValue(site,
                System.Enum.Parse(System.Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "ConcretePouring"));
            siteType.GetField("concreteFailureState", Instance).SetValue(site,
                System.Enum.Parse(System.Type.GetType("FoundationConcreteFailureState, Assembly-CSharp"), "Ready"));

            Assert.That((bool)siteType.GetProperty("CanAcceptFoundationDock").GetValue(site), Is.False,
                "A dock is rejected before the server has granted clearance.");
            siteType.GetField("foundationRampClearanceGranted", Instance).SetValue(site, true);
            Assert.That((bool)siteType.GetProperty("CanAcceptFoundationDock").GetValue(site), Is.False,
                "A grant alone cannot allow docking before the client has checked local occupancy and enabled its collider.");
            siteType.GetField("rampLocalClearanceGranted", Instance).SetValue(site, true);
            siteType.GetField("rampClearancePending", Instance).SetValue(site, false);
            rampCollider.enabled = true;
            Assert.That((bool)siteType.GetProperty("CanAcceptFoundationDock").GetValue(site), Is.True);
            rampCollider.enabled = false;
            Assert.That((bool)siteType.GetProperty("CanAcceptFoundationDock").GetValue(site), Is.False,
                "A locally disabled ramp is not dock-ready even with a server grant.");
        }
        finally
        {
            Object.DestroyImmediate(siteRoot);
            Object.DestroyImmediate(volumeRoot);
        }
    }

    [Test]
    public void RampRequestOffThenRecoveryRequestOnRequiresFreshClearance()
    {
        GameObject siteRoot = new GameObject("Ramp grant lifecycle fixture");
        GameObject volumeRoot = new GameObject("Ramp grant lifecycle volume");
        GameObject ramp = new GameObject("Ramp grant lifecycle collider");
        GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            ramp.transform.SetParent(volumeRoot.transform, false);
            BoxCollider rampCollider = ramp.AddComponent<BoxCollider>();
            Renderer renderer = ramp.AddComponent<MeshRenderer>();
            Component volume = volumeRoot.AddComponent(System.Type.GetType("FoundationExcavationVolume, Assembly-CSharp"));
            volume.GetType().GetField("useLooseSoilHeightField", Instance).SetValue(volume, true);
            volume.GetType().GetField("exitRampCollider", Instance).SetValue(volume, rampCollider);
            volume.GetType().GetField("exitRampRenderer", Instance).SetValue(volume, renderer);

            Component site = siteRoot.AddComponent(System.Type.GetType("BridgeConstructionSite, Assembly-CSharp"));
            System.Type siteType = site.GetType();
            FieldInfo stageField = siteType.GetField("currentStage", Instance);
            FieldInfo failureField = siteType.GetField("concreteFailureState", Instance);
            siteType.GetField("excavationVolume", Instance).SetValue(site, volume);
            siteType.GetField("concreteLoadsPoured", Instance).SetValue(site, 1);
            failureField.SetValue(site, System.Enum.Parse(
                System.Type.GetType("FoundationConcreteFailureState, Assembly-CSharp"), "None"));
            MethodInfo visuals = siteType.GetMethod("ApplyConcreteVisualState", Instance);
            MethodInfo clearance = siteType.GetMethod("UpdateFoundationRampClearance", Instance);
            FieldInfo nextCheck = siteType.GetField("nextRampClearanceCheckTime", Instance);

            stageField.SetValue(site, System.Enum.Parse(
                System.Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "ReadyForMount"));
            siteType.GetField("foundationRampClearanceGranted", Instance).SetValue(site, true);
            siteType.GetField("rampLocalClearanceGranted", Instance).SetValue(site, true);
            visuals.Invoke(site, null);
            Assert.That((bool)siteType.GetField("foundationRampClearanceGranted", Instance).GetValue(site), Is.False,
                "A request-off state resets the prior clearance grant immediately.");
            Assert.That(rampCollider.enabled, Is.False);

            stageField.SetValue(site, System.Enum.Parse(
                System.Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "ConcreteDrying"));
            blocker.AddComponent<Rigidbody>().isKinematic = true;
            blocker.transform.position = new Vector3(0f, 0f, 0.2f);
            Physics.SyncTransforms();
            visuals.Invoke(site, null);
            nextCheck.SetValue(site, 0f);
            clearance.Invoke(site, null);
            Assert.That(rampCollider.enabled, Is.False,
                "A new activation request remains pending while a Rigidbody occupies the future ramp.");

            Object.DestroyImmediate(blocker);
            blocker = null;
            nextCheck.SetValue(site, 0f);
            clearance.Invoke(site, null);
            Assert.That(rampCollider.enabled, Is.True, "The ramp activates automatically once the volume clears.");
            Assert.That((bool)siteType.GetField("foundationRampClearanceGranted", Instance).GetValue(site), Is.True);

            stageField.SetValue(site, System.Enum.Parse(
                System.Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "ReadyForMount"));
            visuals.Invoke(site, null);
            Assert.That((bool)siteType.GetField("foundationRampClearanceGranted", Instance).GetValue(site), Is.False);

            stageField.SetValue(site, System.Enum.Parse(
                System.Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "ConcretePouring"));
            failureField.SetValue(site, System.Enum.Parse(
                System.Type.GetType("FoundationConcreteFailureState, Assembly-CSharp"), "AwaitingWheelbarrowExit"));
            blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.AddComponent<Rigidbody>().isKinematic = true;
            blocker.transform.position = new Vector3(0f, 0f, 0.2f);
            Physics.SyncTransforms();
            visuals.Invoke(site, null);
            nextCheck.SetValue(site, 0f);
            clearance.Invoke(site, null);
            Assert.That(rampCollider.enabled, Is.False,
                "Critical wheelbarrow recovery also waits for its requested ramp volume to clear.");
        }
        finally
        {
            if (blocker != null) Object.DestroyImmediate(blocker);
            Object.DestroyImmediate(siteRoot);
            Object.DestroyImmediate(volumeRoot);
        }
    }

    private static float ReadFloat(Component component, string fieldName) =>
        (float)component.GetType().GetField(fieldName, Instance).GetValue(component);

    private static System.Type FindLoadedType(string fullName)
    {
        foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type type = assembly.GetType(fullName);
            if (type != null) return type;
        }
        return null;
    }

    private static void DisposeNetcodeBuffer(object buffer)
    {
        if (buffer != null) buffer.GetType().GetMethod("Dispose", Instance)?.Invoke(buffer, null);
    }
}
