using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MountingNailStage1PlayModeTests
{
    private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject player;
    private GameObject nailObject;
    private GameObject target;
    private GameObject temporaryTarget;
    private ScriptableObject profileAsset;
    private GameObject inactiveRoot;
    private Component nail;
    private Component resource;
    private Component interaction;

    [UnityTest]
    public IEnumerator AcceptedPlacementCallbackPreparesOnlyValidTipFirstContact_AndOrdinaryDropNeverPrepares()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        SetupFixture();
        Physics.SyncTransforms();
        ActualPickup();
        Vector3 contactPosition = PositionForFace(target.transform.position, target.transform.localScale.z * 0.5f, GetFloat(Profile(), "LengthMeters"), true, 0.005f);
        bool accepted = (bool)InteractionType().GetMethod("TryReleaseSingleCarryPlacement", InstancePublic)
            .Invoke(interaction, new object[] { nailObject, resource, contactPosition, Quaternion.identity });
        Assert.That(accepted, Is.True);
        Assert.That(InteractionType().GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.Null,
            "Commit notification must occur after holder cleanup.");
        AssertState("Prepared");
        Assert.That(nailObject.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(nailObject.GetComponent<Rigidbody>().useGravity, Is.False);

        ActualPickup();
        AssertState("Held", "Pickup cancels Prepared immediately without an exit impulse.");
        Assert.That((bool)InteractionType().GetMethod("DropObject").Invoke(interaction, null), Is.True);
        yield return new WaitForFixedUpdate();
        AssertState("Loose", "Ordinary drop does not call the accepted placement listener.");
        Assert.That(nailObject.GetComponent<Rigidbody>().useGravity, Is.True);

        ActualPickup();
        accepted = (bool)InteractionType().GetMethod("TryReleaseSingleCarryPlacement", InstancePublic)
            .Invoke(interaction, new object[] { nailObject, resource, contactPosition, Quaternion.Euler(0f, 180f, 0f) });
        Assert.That(accepted, Is.True, "Invalid nail direction does not veto valid F/E placement.");
        AssertState("Loose");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator PreparedUsesCumulativePoseContactAndTimeoutLimitsThenExitsOnce()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        SetupFixture();
        Physics.SyncTransforms();
        ActualPickup();
        Vector3 contactPosition = PositionForFace(target.transform.position, 0.5f, GetFloat(Profile(), "LengthMeters"), true, 0.005f);
        Assert.That(Commit(contactPosition, Quaternion.identity), Is.True);
        AssertState("Prepared");

        float allowedMove = GetFloat(Profile(), "AllowedTargetDisplacementMeters");
        target.transform.position += Vector3.right * allowedMove * 0.6f;
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        AssertState("Prepared", "Pose tolerance is measured from the initial target pose.");
        target.transform.position += Vector3.right * allowedMove * 0.6f;
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        AssertState("Loose", "Cumulative target displacement beyond tolerance exits Prepared.");
        Rigidbody body = nailObject.GetComponent<Rigidbody>();
        Assert.That(body.useGravity, Is.True);

        target.transform.position = new Vector3(10f, 15f, 0f);
        Physics.SyncTransforms();
        ActualPickup();
        contactPosition = PositionForFace(target.transform.position, 0.5f, GetFloat(Profile(), "LengthMeters"), true, 0.005f);
        Assert.That(Commit(contactPosition, Quaternion.identity), Is.True);
        float allowedDegrees = GetFloat(Profile(), "AllowedTargetRotationDegrees");
        target.transform.rotation = Quaternion.Euler(0f, 0f, allowedDegrees * 0.6f);
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        AssertState("Prepared", "Angular tolerance uses the original target orientation.");
        target.transform.rotation = Quaternion.Euler(0f, 0f, allowedDegrees * 1.2f);
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        AssertState("Loose", "Cumulative target rotation beyond tolerance exits Prepared.");

        target.transform.rotation = Quaternion.identity;
        Physics.SyncTransforms();
        ActualPickup();
        contactPosition = PositionForFace(target.transform.position, 0.5f, GetFloat(Profile(), "LengthMeters"), true, 0.005f);
        Assert.That(Commit(contactPosition, Quaternion.identity), Is.True);
        target.GetComponent<Collider>().enabled = false;
        yield return new WaitForFixedUpdate();
        AssertState("Loose", "Losing the exact prepared collider exits Prepared.");

        target.GetComponent<Collider>().enabled = true;
        Physics.SyncTransforms();
        ActualPickup();
        Assert.That(Commit(contactPosition, Quaternion.identity), Is.True);
        float timeout = GetFloat(Profile(), "PreparedTimeoutSeconds");
        float deadline = Time.realtimeSinceStartup + timeout + 1f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (GetState() == "Loose") break;
            yield return null;
        }
        AssertState("Loose", "Prepared timeout is enforced by the authority clock.");
#endif
    }

    [UnityTest]
    public IEnumerator PreparedExitAppliesOneLateralNearHeadImpulseAndTopplesVerticalNail()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        SetupFixture();
        float length = GetFloat(Profile(), "LengthMeters");
        float configuredImpulse = GetFloat(Profile(), "ExitLateralImpulseMetersPerSecond");
        target.transform.position = new Vector3(10f, 15f, 0f);
        Quaternion verticalNailRotation = Quaternion.FromToRotation(Vector3.forward, Vector3.down);
        Vector3 nailPosition = target.transform.position + Vector3.up * (0.5f + length * 0.5f + 0.005f);
        nailObject.transform.SetPositionAndRotation(nailPosition, verticalNailRotation);
        Physics.SyncTransforms();

        nail.GetType().GetMethod("OnSingleCarryPlacementCommitted", InstancePublic)
            .Invoke(nail, new object[] { null });
        AssertState("Prepared");
        target.GetComponent<Collider>().enabled = false;
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();

        AssertState("Loose");
        Rigidbody body = nailObject.GetComponent<Rigidbody>();
        Vector3 lateralVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.down);
        Assert.That(lateralVelocity.magnitude, Is.EqualTo(configuredImpulse).Within(0.015f),
            "A horizontal velocity change matches the configured single exit impulse; gravity is excluded.");
        Assert.That(body.angularVelocity.magnitude, Is.GreaterThan(0.01f),
            "Applying the impulse near the head creates torque that topples a vertical nail.");
        float speedAfterExit = lateralVelocity.magnitude;
        yield return new WaitForFixedUpdate();
        Vector3 lateralVelocityNextStep = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.down);
        Assert.That(lateralVelocityNextStep.magnitude, Is.EqualTo(speedAfterExit).Within(0.002f),
            "The exit impulse is applied once, while gravity only changes vertical velocity.");
#endif
    }

    [UnityTest]
    public IEnumerator ContactGateIgnoresSelfAndTriggersAndSupportsWallAndCeilingFaces()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        SetupFixture();
        MethodInfo canPrepare = nail.GetType().GetMethod("CanPrepareAtPose", InstancePublic);
        float length = GetFloat(Profile(), "LengthMeters");
        Vector3 exactFace = PositionForFace(target.transform.position, 0.5f, length, true, 0f);
        nailObject.transform.position = exactFace;
        Physics.SyncTransforms();
        Assert.That((bool)canPrepare.Invoke(nail, new object[] { exactFace, Quaternion.identity }), Is.True);
        Assert.That((bool)canPrepare.Invoke(nail, new object[]
        {
            PositionForFace(target.transform.position, 0.5f, length, true, 0.02f), Quaternion.identity
        }), Is.True, "The configured two-centimetre boundary is inclusive within numeric epsilon.");
        Assert.That((bool)canPrepare.Invoke(nail, new object[] { PositionForFace(target.transform.position, 0.5f, length, true, 0.0205f), Quaternion.identity }), Is.False,
            "Contact just beyond the configured tip tolerance is rejected.");

        GameObject trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trigger.transform.position = target.transform.position + Vector3.back * 0.52f;
        trigger.transform.localScale = new Vector3(0.2f, 0.2f, 0.01f);
        trigger.GetComponent<Collider>().isTrigger = true;
        Physics.SyncTransforms();
        Assert.That((bool)canPrepare.Invoke(nail, new object[] { exactFace, Quaternion.identity }), Is.True,
            "Trigger colliders do not satisfy or block the physical contact query.");
        Object.Destroy(trigger);

        target.transform.position = new Vector3(10f, 15f, 0f);
        Vector3 wallRoot = target.transform.position + Vector3.left * (0.5f + length * 0.5f + 0.005f);
        Quaternion wallRotation = Quaternion.Euler(0f, 90f, 0f);
        Assert.That((bool)canPrepare.Invoke(nail, new object[] { wallRoot, wallRotation }), Is.True,
            "The same profile contact rule supports vertical wall placement.");
        Vector3 ceilingRoot = target.transform.position + Vector3.down * (0.5f + length * 0.5f + 0.005f);
        Quaternion ceilingRotation = Quaternion.Euler(-90f, 0f, 0f);
        Assert.That((bool)canPrepare.Invoke(nail, new object[] { ceilingRoot, ceilingRotation }), Is.True,
            "The same profile contact rule supports ceiling placement.");

        target.transform.position = new Vector3(10f, 15f, 50f);
        GameObject thinPlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        temporaryTarget = thinPlate;
        thinPlate.transform.position = new Vector3(10f, 15f, 0f);
        thinPlate.transform.localScale = new Vector3(0.2f, 0.2f, 0.01f);
        Physics.SyncTransforms();
        Vector3 approachingTip = thinPlate.transform.position - Vector3.forward * 0.006f;
        Vector3 approachingRoot = approachingTip - Vector3.forward * (length * 0.5f);
        Assert.That((bool)canPrepare.Invoke(nail, new object[] { approachingRoot, Quaternion.identity }), Is.True,
            "A forward-facing tip approaching the thin plate is accepted.");
        Vector3 departingTip = thinPlate.transform.position + Vector3.forward * 0.006f;
        Vector3 departingRoot = departingTip - Vector3.forward * (length * 0.5f);
        Assert.That((bool)canPrepare.Invoke(nail, new object[] { departingRoot, Quaternion.identity }), Is.False,
            "A ray that reaches the plate only behind a tip pointing away cannot prepare the nail.");
        Object.Destroy(thinPlate);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator PreparedFollowsStableRigidbodyTargetAcrossItsCollidersButRejectsReplacement()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        SetupFixture();
        Rigidbody targetBody = target.AddComponent<Rigidbody>();
        targetBody.isKinematic = true;
        BoxCollider first = target.GetComponent<BoxCollider>();
        BoxCollider second = target.AddComponent<BoxCollider>();
        second.size = first.size;
        second.center = first.center;
        Physics.SyncTransforms();

        ActualPickup();
        Vector3 contactPosition = PositionForFace(target.transform.position, 0.5f,
            GetFloat(Profile(), "LengthMeters"), true, 0.005f);
        Assert.That(Commit(contactPosition, Quaternion.identity), Is.True);
        AssertState("Prepared");

        Collider preparedCollider = (Collider)nail.GetType().GetProperty("PreparedSurface", InstancePublic)
            .GetValue(nail);
        Assert.That(preparedCollider, Is.Not.Null);
        Collider remainingSameTargetCollider = preparedCollider == first ? second : first;
        Object.Destroy(preparedCollider);
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        AssertState("Prepared", "A second collider on the same Rigidbody remains the same physical target.");

        remainingSameTargetCollider.enabled = false;
        GameObject replacement = GameObject.CreatePrimitive(PrimitiveType.Cube);
        replacement.transform.position = target.transform.position;
        replacement.transform.localScale = target.transform.localScale;
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        AssertState("Loose", "A different root cannot replace the original prepared target.");
        Object.Destroy(replacement);
#endif
    }

    [UnityTest]
    public IEnumerator ProfileCloneConfiguresLengthMassVisualCenterMarkersAndColliderBeforeAwake()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        GameObject nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null);
        Component prefabNail = nailPrefab.GetComponent(NailType());
        profileAsset = Object.Instantiate((ScriptableObject)ProfileFrom(prefabNail));
        profileAsset.GetType().GetField("lengthMeters", InstanceNonPublic).SetValue(profileAsset, 0.7f);
        profileAsset.GetType().GetField("visualWidthMultiplier", InstanceNonPublic).SetValue(profileAsset, 16f);
        profileAsset.GetType().GetField("massKg", InstanceNonPublic).SetValue(profileAsset, 0.2f);

        inactiveRoot = new GameObject("Mounting nail profile configuration fixture");
        inactiveRoot.SetActive(false);
        nailObject = Object.Instantiate(nailPrefab, inactiveRoot.transform);
        nail = nailObject.GetComponent(NailType());
        nail.GetType().GetField("profile", InstanceNonPublic).SetValue(nail, profileAsset);
        inactiveRoot.SetActive(true);

        CapsuleCollider capsule = nailObject.GetComponent<CapsuleCollider>();
        Rigidbody body = nailObject.GetComponent<Rigidbody>();
        Transform tip = (Transform)NailType().GetProperty("Tip", InstancePublic).GetValue(nail);
        Transform head = (Transform)NailType().GetProperty("Head", InstancePublic).GetValue(nail);
        Transform visual = (Transform)nail.GetType().GetField("visual", InstanceNonPublic).GetValue(nail);
        Renderer renderer = visual.GetComponentInChildren<Renderer>();
        Transform prefabVisual = (Transform)NailType().GetField("visual", InstanceNonPublic).GetValue(prefabNail);
        float savedPrefabWidth = prefabVisual.localScale.x;
        float savedPrefabRadius = prefabNail.GetComponent<CapsuleCollider>().radius;
        Assert.That(capsule.height, Is.EqualTo(0.7f).Within(0.0001f));
        Assert.That(body.mass, Is.EqualTo(0.2f).Within(0.0001f));
        Assert.That(visual.localScale.x, Is.EqualTo(savedPrefabWidth * 2f).Within(0.0001f));
        Assert.That(visual.localScale.z, Is.EqualTo(prefabVisual.localScale.z * 2f).Within(0.0001f));
        Assert.That(capsule.radius, Is.EqualTo(savedPrefabRadius * 2f).Within(0.0001f));
        Assert.That(Vector3.Distance(tip.position, head.position), Is.EqualTo(0.7f).Within(0.0001f));
        Assert.That(renderer.bounds.size.z, Is.EqualTo(0.7f).Within(0.005f));
        Assert.That(Vector3.Distance(renderer.bounds.center, nailObject.transform.position), Is.LessThan(0.005f),
            "Re-centering follows the changed length so the model remains aligned with Tip, Head, and collider.");
        Vector3 configuredScale = visual.localScale;
        NailType().GetMethod("ApplyProfileConfiguration", BindingFlags.Instance | InstanceNonPublic).Invoke(nail, null);
        Assert.That(visual.localScale, Is.EqualTo(configuredScale), "Repeated configuration must not compound the width multiplier.");
        yield return null;
#endif
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (player != null) Object.Destroy(player);
        if (inactiveRoot != null) Object.Destroy(inactiveRoot);
        else if (nailObject != null) Object.Destroy(nailObject);
        if (target != null) Object.Destroy(target);
        if (temporaryTarget != null) Object.Destroy(temporaryTarget);
        if (profileAsset != null) Object.Destroy(profileAsset);
        yield return null;
    }

#if UNITY_EDITOR
    private void SetupFixture()
    {
        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        GameObject nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(nailPrefab, Is.Not.Null);
        player = Object.Instantiate(playerPrefab, new Vector3(-20f, 15f, -20f), Quaternion.identity);
        nailObject = Object.Instantiate(nailPrefab);
        target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        target.transform.position = new Vector3(10f, 15f, 0f);
        interaction = player.GetComponent(InteractionType());
        resource = nailObject.GetComponent(ResourceType());
        nail = nailObject.GetComponent(NailType());
        Assert.That(interaction, Is.Not.Null);
        Assert.That(resource, Is.Not.Null);
        Assert.That(nail, Is.Not.Null);
    }

    private void ActualPickup() => ResourceType().GetMethod("PickedUp", InstancePublic)
        .Invoke(resource, new object[] { player.transform });

    private bool Commit(Vector3 position, Quaternion rotation) => (bool)InteractionType()
        .GetMethod("TryReleaseSingleCarryPlacement", InstancePublic)
        .Invoke(interaction, new object[] { nailObject, resource, position, rotation });

    private object Profile() => NailType().GetProperty("Profile", InstancePublic).GetValue(nail);
    private static object ProfileFrom(Component component) => NailType().GetProperty("Profile", InstancePublic).GetValue(component);
    private string GetState() => NailType().GetProperty("State", InstancePublic).GetValue(nail).ToString();
    private void AssertState(string state, string message = null) => Assert.That(GetState(), Is.EqualTo(state), message);
    private static float GetFloat(object instance, string property) => (float)instance.GetType()
        .GetProperty(property, InstancePublic).GetValue(instance);
    private static Vector3 PositionForFace(Vector3 center, float halfExtent, float length, bool forward, float gap)
    {
        float sign = forward ? -1f : 1f;
        return center + Vector3.forward * sign * (halfExtent + length * 0.5f + gap);
    }
    private static System.Type InteractionType() => System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
    private static System.Type NailType() => System.Type.GetType("MountingNail, Assembly-CSharp");
    private static System.Type ResourceType() => System.Type.GetType("BaseResourceNew, Assembly-CSharp");
#endif
}


