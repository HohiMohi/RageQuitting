using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MountingNailStage2PlayModeTests
{
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject nailObject;
    private GameObject target;
    private ScriptableObject clonedProfile;
    private Component nail;
    private Component resource;
    private Component targetResource;
    private GameObject player;
    private GameObject fixtureCamera;
    private GameObject inactiveRoot;

    [UnityTest]
    public IEnumerator OneHammerHitEmbedsToFinalDepthAndBlocksNailPickupWithoutDamagingTarget()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup(1);
        Prepare(0.019f);
        float nailDurability = ResourceFloat(resource, "GetCurrentResourceDurabilityNormalized");
        Assert.That(ApplyHammer(), Is.True);
        AssertState("EmbeddedSingle");
        Assert.That(NailInt("HitCount"), Is.EqualTo(1));
        Assert.That(NailInt("RequiredHitCount"), Is.EqualTo(1));
        Assert.That(NailFloat("PenetrationDepthMeters"), Is.EqualTo(NailFloat("Profile.FinalDepthMeters")).Within(0.0001f));
        Collider targetCollider = target.GetComponentInChildren<Collider>();
        Transform tip = (Transform)nail.GetType().GetProperty("Tip", PublicInstance).GetValue(nail);
        Assert.That(tip.position.z, Is.EqualTo(targetCollider.bounds.min.z + NailFloat("Profile.FinalDepthMeters")).Within(0.002f),
            "The configured depth starts at the actual impact surface even when the prepared tip has a near-maximum gap.");
        Assert.That(nailObject.GetComponent<CapsuleCollider>().isTrigger, Is.True);
        Assert.That(nailObject.GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(ResourceBool(resource, "CanBeCarried"), Is.False, "Embedded nails cannot be picked up by players or NPCs.");
        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        player = Object.Instantiate(playerPrefab, new Vector3(-20f, 15f, -20f), Quaternion.identity);
        foreach (Collider playerCollider in player.GetComponentsInChildren<Collider>(true)) playerCollider.enabled = false;
        resource.GetType().GetMethod("PickedUp", PublicInstance).Invoke(resource, new object[] { player.transform });
        Assert.That(ResourceBool(resource, "IsPickedUp"), Is.False, "The player pickup entry point rejects an embedded nail.");
        Assert.That(ResourceFloat(resource, "GetCurrentResourceDurabilityNormalized"), Is.EqualTo(nailDurability));
        Assert.That(ResourceBool(targetResource, "CanBeCarried"), Is.True, "Embedding does not block carrying the target.");
        Assert.That(ApplyHammer(), Is.False, "Extra hammer hits have no effect after complete embedding.");
        Assert.That(NailInt("HitCount"), Is.EqualTo(1));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator CarriedTargetCannotReceiveTheFirstHammerHit()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup(3);
        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        player = Object.Instantiate(playerPrefab, new Vector3(-20f, 15f, -20f), Quaternion.identity);
        foreach (Collider playerCollider in player.GetComponentsInChildren<Collider>(true)) playerCollider.enabled = false;
        Prepare();
        targetResource.GetType().GetMethod("PickedUp", PublicInstance).Invoke(targetResource, new object[] { player.transform });
        Assert.That(ResourceBool(targetResource, "IsPickedUp"), Is.True);
        Assert.That(ApplyHammer(), Is.False);
        AssertState("Loose");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator FirstHitRevalidatesAReplacementColliderOnTheSameStableTargetRoot()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup(3);
        Prepare();
        Collider originalSurface = (Collider)nail.GetType().GetProperty("PreparedSurface", PublicInstance).GetValue(nail);
        Assert.That(originalSurface, Is.Not.Null);
        BoxCollider replacementSurface = originalSurface.gameObject.AddComponent<BoxCollider>();
        BoxCollider originalBox = originalSurface as BoxCollider;
        Assert.That(originalBox, Is.Not.Null);
        replacementSurface.center = originalBox.center;
        replacementSurface.size = originalBox.size;
        originalSurface.enabled = false;
        Physics.SyncTransforms();

        Assert.That(ApplyHammer(), Is.True, "A new collider on the same stable physical root is valid at impact.");
        AssertState("Partial");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator ThreeHitsAdvanceProportionallyDisableTimeoutAndFollowOriginalTargetPose()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup(3);
        Prepare();
        float finalDepth = NailFloat("Profile.FinalDepthMeters");
        Assert.That(ApplyHammer(), Is.True);
        AssertState("Partial");
        Assert.That(NailFloat("PenetrationDepthMeters"), Is.EqualTo(finalDepth / 3f).Within(0.0001f));

        Transform tip = (Transform)nail.GetType().GetProperty("Tip", PublicInstance).GetValue(nail);
        Transform embeddedRoot = (Transform)nail.GetType().GetField("embeddedTarget", PrivateInstance).GetValue(nail);
        Assert.That(embeddedRoot, Is.Not.Null);
        Vector3 tipPositionInRoot = embeddedRoot.InverseTransformPoint(tip.position);
        Quaternion nailRotationInRoot = Quaternion.Inverse(embeddedRoot.rotation) * nailObject.transform.rotation;
        target.transform.SetPositionAndRotation(new Vector3(11f, 16f, 1f), Quaternion.Euler(0f, 35f, 0f));
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        Assert.That(Vector3.Distance(tip.position, embeddedRoot.TransformPoint(tipPositionInRoot)), Is.LessThan(0.005f),
            "Tip follows the pose captured in target-local space through target translation and rotation.");
        Quaternion expectedRotation = embeddedRoot.rotation * nailRotationInRoot;
        Assert.That(Quaternion.Angle(nailObject.transform.rotation, expectedRotation), Is.LessThan(0.1f));

        float waitUntil = Time.realtimeSinceStartup + 3.2f;
        while (Time.realtimeSinceStartup < waitUntil) yield return null;
        Assert.That(nail.GetType().GetProperty("State", PublicInstance).GetValue(nail).ToString(),
            Is.EqualTo("Partial"), "Prepared timeout is permanently disabled after the first valid hit.");
        Assert.That(ApplyHammer(), Is.True);
        Assert.That(NailFloat("PenetrationDepthMeters"), Is.EqualTo(finalDepth * 2f / 3f).Within(0.0001f));
        Assert.That(ApplyHammer(), Is.True);
        AssertState("EmbeddedSingle");
        Assert.That(NailFloat("PenetrationDepthMeters"), Is.EqualTo(finalDepth).Within(0.0001f));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator FirstHitRejectsExpiredOrLostContactAndDestroyedTargetRemovesEmbeddedNail()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup(3);
        Prepare();
        nail.GetType().GetField("preparedDeadline", PrivateInstance).SetValue(nail, Time.timeAsDouble - 0.001d);
        Assert.That(ApplyHammer(), Is.False);
        AssertState("Loose");

        Prepare();
        Collider targetCollider = target.GetComponentInChildren<Collider>();
        targetCollider.enabled = false;
        Physics.SyncTransforms();
        Assert.That(ApplyHammer(), Is.False);
        AssertState("Loose");

        targetCollider.enabled = true;
        Physics.SyncTransforms();
        Prepare();
        Assert.That(ApplyHammer(), Is.True);
        Object.Destroy(target);
        yield return new WaitForFixedUpdate();
        yield return null;
        Assert.That(nailObject == null, Is.True, "Offline embedded nail is removed when its target is destroyed.");
#endif
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (inactiveRoot != null) Object.Destroy(inactiveRoot);
        else if (nailObject != null) Object.Destroy(nailObject);
        if (target != null) Object.Destroy(target);
        if (player != null) Object.Destroy(player);
        if (fixtureCamera != null) Object.Destroy(fixtureCamera);
        if (clonedProfile != null) Object.Destroy(clonedProfile);
        yield return null;
    }

#if UNITY_EDITOR
    private void Setup(int requiredHits)
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(prefab, Is.Not.Null);
        System.Type nailType = System.Type.GetType("MountingNail, Assembly-CSharp");
        Component prefabNail = prefab.GetComponent(nailType);
        clonedProfile = Object.Instantiate((ScriptableObject)nailType.GetProperty("Profile", PublicInstance).GetValue(prefabNail));
        clonedProfile.GetType().GetField("requiredHitsForSingleObject", PrivateInstance).SetValue(clonedProfile, requiredHits);
        inactiveRoot = new GameObject("Stage 2 nail fixture");
        inactiveRoot.SetActive(false);
        nailObject = Object.Instantiate(prefab, inactiveRoot.transform);
        nail = nailObject.GetComponent(nailType);
        nail.GetType().GetField("profile", PrivateInstance).SetValue(nail, clonedProfile);
        inactiveRoot.SetActive(true);
        resource = nailObject.GetComponent(System.Type.GetType("BaseResourceNew, Assembly-CSharp"));
        GameObject boardPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/WoodenBoardResource_prefab.prefab");
        Assert.That(boardPrefab, Is.Not.Null);
        target = Object.Instantiate(boardPrefab);
        target.transform.position = new Vector3(10f, 15f, 0f);
        targetResource = target.GetComponent(System.Type.GetType("BaseResourceNew, Assembly-CSharp"));
        Assert.That(targetResource, Is.Not.Null);
        if (Camera.main == null)
        {
            fixtureCamera = new GameObject("Mounting nail Stage 2 fixture camera");
            fixtureCamera.tag = "MainCamera";
            fixtureCamera.AddComponent<Camera>();
        }
        Physics.SyncTransforms();
    }

    private void Prepare(float gap = 0.005f)
    {
        float length = NailFloat("Profile.LengthMeters");
        Collider targetCollider = target.GetComponentInChildren<Collider>();
        Bounds bounds = targetCollider.bounds;
        nailObject.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.center.y,
            bounds.min.z - length * 0.5f - gap), Quaternion.identity);
        Physics.SyncTransforms();
        nail.GetType().GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(nail, new object[] { null });
        AssertState("Prepared");
    }

    private bool ApplyHammer() => (bool)nail.GetType().GetMethod("TryApplyHammerHit", PublicInstance)
        .Invoke(nail, new[] { System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer") });
    private int NailInt(string property) => (int)nail.GetType().GetProperty(property, PublicInstance).GetValue(nail);
    private float NailFloat(string property)
    {
        if (property.StartsWith("Profile."))
        {
            object profile = nail.GetType().GetProperty("Profile", PublicInstance).GetValue(nail);
            return (float)profile.GetType().GetProperty(property.Substring("Profile.".Length), PublicInstance).GetValue(profile);
        }
        return (float)nail.GetType().GetProperty(property, PublicInstance).GetValue(nail);
    }
    private void AssertState(string expected) => Assert.That(nail.GetType().GetProperty("State", PublicInstance).GetValue(nail).ToString(), Is.EqualTo(expected));
    private static bool ResourceBool(Component component, string method) => (bool)component.GetType().GetProperty(method, PublicInstance).GetValue(component);
    private static float ResourceFloat(Component component, string method) => (float)component.GetType().GetMethod(method, PublicInstance).Invoke(component, null);
#endif
}
