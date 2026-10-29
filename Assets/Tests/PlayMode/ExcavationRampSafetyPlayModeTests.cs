using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ExcavationRampSafetyPlayModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private PlayModeTestScope scope;
    private FieldInfo gameplayInstance;
    private object previousGameplay;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        scope = new PlayModeTestScope("ExcavationRampSafetyPlayMode");
        Type gameplayType = Type.GetType("GameplayManager, Assembly-CSharp");
        gameplayInstance = gameplayType.GetField("<Instance>k__BackingField", Static);
        previousGameplay = gameplayInstance.GetValue(null);
        scope.RestoreWith(() => gameplayInstance.SetValue(null, previousGameplay));
        GameObject dummyRoot = scope.Own(new GameObject("Inactive GameplayManager fixture"));
        dummyRoot.SetActive(false);
        dummyRoot.AddComponent(gameplayType);
        gameplayInstance.SetValue(null, dummyRoot.GetComponent(gameplayType));
        yield break;
    }

    [UnityTest]
    public IEnumerator PhysicalAndDisabledOfflinePlayerOccupantsBlockRampUntilTheyLeave()
    {
        GameObject siteRoot = scope.Own(new GameObject("Ramp safety PlayMode site"));
        GameObject volumeRoot = scope.Own(new GameObject("Ramp safety PlayMode volume"));
        GameObject ramp = scope.Own(new GameObject("Ramp safety PlayMode ramp"));
        GameObject occupant = scope.Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
        GameObject disabledPlayer = null;
            ramp.transform.SetParent(volumeRoot.transform, false);
            ramp.transform.rotation = Quaternion.Euler(12f, 35f, 7f);
            BoxCollider rampCollider = ramp.AddComponent<BoxCollider>();
            rampCollider.size = new Vector3(1f, 0.2f, 4f);
            MeshRenderer rampRenderer = ramp.AddComponent<MeshRenderer>();
            Component volume = volumeRoot.AddComponent(System.Type.GetType("FoundationExcavationVolume, Assembly-CSharp"));
            System.Type volumeType = volume.GetType();
            volumeType.GetField("useLooseSoilHeightField", Instance).SetValue(volume, true);
            volumeType.GetField("exitRampCollider", Instance).SetValue(volume, rampCollider);
            volumeType.GetField("exitRampRenderer", Instance).SetValue(volume, rampRenderer);

            Component site = siteRoot.AddComponent(System.Type.GetType("BridgeConstructionSite, Assembly-CSharp"));
            ((Behaviour)siteRoot.GetComponent(Type.GetType("BridgeComponent, Assembly-CSharp"))).enabled = false;
            System.Type siteType = site.GetType();
            siteType.GetField("excavationVolume", Instance).SetValue(site, volume);
            siteType.GetField("currentStage", Instance).SetValue(site,
                System.Enum.Parse(System.Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "ConcreteDrying"));
            siteType.GetField("concreteLoadsPoured", Instance).SetValue(site, 1);
            siteType.GetField("concreteFailureState", Instance).SetValue(site,
                System.Enum.Parse(System.Type.GetType("FoundationConcreteFailureState, Assembly-CSharp"), "None"));
            siteType.GetMethod("ApplyConcreteVisualState", Instance).Invoke(site, null);

            Rigidbody body = occupant.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = false;
            occupant.transform.position = ramp.transform.TransformPoint(new Vector3(0.15f, 0.3f, 0.35f));
            siteType.GetField("nextRampClearanceCheckTime", Instance).SetValue(site, 0f);
            siteType.GetMethod("UpdateFoundationRampClearance", Instance).Invoke(site, null);
            yield return new WaitForSeconds(0.25f);

            Assert.That(rampCollider.enabled, Is.False,
                "A dynamic Rigidbody in the future oriented ramp volume keeps the ramp physically off.");
            Assert.That(rampRenderer.enabled, Is.False,
                "Both renderer and collider remain off while activation is pending.");
            Assert.That(body.linearVelocity.sqrMagnitude, Is.LessThan(0.0001f),
                "Waiting for clearance does not teleport or launch the occupant.");
            Assert.That((bool)siteType.GetField("foundationRampClearanceGranted", Instance).GetValue(site), Is.False);

            occupant.transform.position = new Vector3(80f, 30f, 80f);
            siteType.GetField("nextRampClearanceCheckTime", Instance).SetValue(site, 0f);
            siteType.GetMethod("UpdateFoundationRampClearance", Instance).Invoke(site, null);
            yield return new WaitForSeconds(0.25f);

            Assert.That(rampCollider.enabled, Is.True,
                "After the physical prop leaves, offline authority can grant clearance.");
            Assert.That(body.linearVelocity.sqrMagnitude, Is.LessThan(0.0001f),
                "The moving physical occupant is not given an impulse while waiting or when the ramp activates.");
            Set(siteType.GetField("foundationRampClearanceGranted", Instance), site, false);
            Set(siteType.GetField("rampLocalClearanceGranted", Instance), site, false);
            Set(siteType.GetField("rampClearancePending", Instance), site, false);
            siteType.GetMethod("ApplyConcreteVisualState", Instance).Invoke(site, null);
            disabledPlayer = scope.Own(new GameObject("Offline disabled player capsule"));
            disabledPlayer.transform.position = ramp.transform.TransformPoint(new Vector3(0.1f, -0.45f, 0.1f));
            CharacterController controller = disabledPlayer.AddComponent<CharacterController>();
            controller.enabled = false;
            siteType.GetField("nextRampClearanceCheckTime", Instance).SetValue(site, 0f);
            siteType.GetMethod("UpdateFoundationRampClearance", Instance).Invoke(site, null);
            yield return new WaitForSeconds(0.25f);
            Assert.That(rampCollider.enabled, Is.False,
                "Offline pending checks find a disabled active player capsule outside the physics broadphase.");
            disabledPlayer.transform.position = new Vector3(80f, 30f, 80f);
            siteType.GetField("nextRampClearanceCheckTime", Instance).SetValue(site, 0f);
            siteType.GetMethod("UpdateFoundationRampClearance", Instance).Invoke(site, null);
            yield return new WaitForSeconds(0.25f);

            Assert.That((bool)siteType.GetField("foundationRampClearanceGranted", Instance).GetValue(site), Is.True,
                "Offline authority grants clearance automatically after the occupant leaves.");
            Assert.That(rampCollider.enabled, Is.True);
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (scope == null) yield break;
        PlayModeTestScope cleanupScope = scope;
        yield return cleanupScope.Cleanup();
        scope = null;
        cleanupScope.AssertCleanupSucceeded();
    }

    private static void Set(FieldInfo field, object target, object value) => field.SetValue(target, value);
}
