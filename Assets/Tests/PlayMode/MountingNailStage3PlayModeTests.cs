using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

public class MountingNailStage3PlayModeTests
{
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> roots = new();
    private readonly List<ScriptableObject> profiles = new();
    private Component nail;
    private GameObject nailObject;
    private GameObject first;
    private GameObject second;
    private GameObject replacement;
    private NetworkManager previousSingleton;
    private NetworkManager offlineSingleton;

    [UnityTest]
    public IEnumerator PairUsesFrozenThreeHitProgressAndSharesOneJointAcrossNails()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        CreatePair();
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "Partial");
        Assert.That(Int(nail, "RequiredHitCount"), Is.EqualTo(3));
        object profile = nail.GetType().GetProperty("Profile", PublicInstance).GetValue(nail);
        profile.GetType().GetField("requiredHitsForTwoObjects", PrivateInstance).SetValue(profile, 1);
        profile.GetType().GetField("finalDepthMeters", PrivateInstance).SetValue(profile, 0.08f);
        Assert.That(Int(nail, "RequiredHitCount"), Is.EqualTo(3), "The original pair hit count is frozen on impact.");
        Assert.That(Float(nail, "PenetrationDepthMeters"), Is.EqualTo(0.1f).Within(0.0002f),
            "The original final depth is frozen on impact.");
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "Partial");
        Assert.That(Int(nail, "HitCount"), Is.EqualTo(2));

        Transform firstTransform = first.transform;
        Transform secondTransform = second.transform;
        Vector3 firstPosition = firstTransform.position;
        Vector3 secondPosition = secondTransform.position;
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedPair");
        Assert.That(Int(nail, "HitCount"), Is.EqualTo(3));
        Assert.That(Vector3.Distance(firstTransform.position, firstPosition), Is.LessThan(0.002f));
        Assert.That(Vector3.Distance(secondTransform.position, secondPosition), Is.LessThan(0.002f));
        FixedJoint[] firstJoints = first.GetComponents<FixedJoint>();
        Assert.That(firstJoints.Length + second.GetComponents<FixedJoint>().Length, Is.EqualTo(1),
            "The pair is connected at the captured pose without adding a second joint.");

        Component secondNail = CreateNail(out GameObject secondNailObject);
        roots.Add(secondNailObject);
        PrepareNail(secondNail, secondNailObject, first);
        Assert.That(ApplyHammer(secondNail), Is.True);
        Assert.That(ApplyHammer(secondNail), Is.True);
        Assert.That(ApplyHammer(secondNail), Is.True);
        AssertState(secondNail, "EmbeddedPair");
        Assert.That(first.GetComponents<FixedJoint>().Length + second.GetComponents<FixedJoint>().Length,
            Is.EqualTo(1), "Additional nails on the same physical pair share its existing joint.");

        Object.Destroy(nailObject);
        for (int i = 0; i < 2; i++) yield return new WaitForFixedUpdate();
        yield return null;
        Assert.That(first.GetComponents<FixedJoint>().Length + second.GetComponents<FixedJoint>().Length,
            Is.EqualTo(1), "Removing one nail preserves the joint while another nail still owns the pair.");
        Object.Destroy(secondNailObject);
        for (int i = 0; i < 2; i++) yield return new WaitForFixedUpdate();
        yield return null;
        Assert.That(first.GetComponents<FixedJoint>().Length + second.GetComponents<FixedJoint>().Length, Is.Zero);
#endif
    }

    [UnityTest]
    public IEnumerator MovedSelectedSecondTargetFallsBackToFirstWithoutSubstitutingDeeperObject()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        CreatePair();
        replacement = CreateTarget("deeper target", new Vector3(50f, 20f, 0.28f), true);
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(ApplyHammer(nail), Is.True);
        second.transform.position += Vector3.right * 2f;
        Physics.SyncTransforms();
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedSingle");
        Assert.That(Int(nail, "HitCount"), Is.EqualTo(3));
        Assert.That(first.GetComponents<FixedJoint>().Length + replacement.GetComponents<FixedJoint>().Length,
            Is.Zero, "Final validation uses the original second target and saved first entry/axis.");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator OneHitPairCompletesImmediatelyAndRetainsPoseUnderForce()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        CreatePair();
        object profile = nail.GetType().GetProperty("Profile", PublicInstance).GetValue(nail);
        profile.GetType().GetField("requiredHitsForTwoObjects", PrivateInstance).SetValue(profile, 1);
        PrepareNail(nail, nailObject, first);
        Vector3 firstPosition = first.transform.position;
        Vector3 secondPosition = second.transform.position;
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedPair");
        Assert.That(first.GetComponents<FixedJoint>().Length + second.GetComponents<FixedJoint>().Length, Is.EqualTo(1));
        Vector3 initialPairOffset = secondPosition - firstPosition;
        first.GetComponent<Rigidbody>().AddForce(Vector3.right * 1f, ForceMode.Impulse);
        for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
        Assert.That(Vector3.Distance(first.transform.position, firstPosition), Is.GreaterThan(0.01f),
            "The server/offline connected bodies remain dynamically simulated under impact.");
        Assert.That(Vector3.Distance(second.transform.position - first.transform.position, initialPairOffset), Is.LessThan(0.01f),
            "A modest impact moves the connected dynamic pair together without changing its captured relative pose.");
#endif
    }

    [UnityTest]
    public IEnumerator OfflinePairEligibilityDoesNotDependOnAnUnstartedSingleton()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        previousSingleton = NetworkManager.Singleton;
        GameObject managerObject = new("Unstarted NetworkManager singleton fixture");
        roots.Add(managerObject);
        offlineSingleton = managerObject.AddComponent<NetworkManager>();
        offlineSingleton.SetSingleton();
        CreatePair();
        Object.Destroy(second.GetComponent<Rigidbody>());
        yield return null;
        Physics.SyncTransforms();
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True,
            "An unstarted NetworkManager singleton must not turn offline collision targets into invalid online targets.");
        AssertState(nail, "Partial");
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedPair");
        Assert.That(first.GetComponents<FixedJoint>().Length + second.GetComponents<FixedJoint>().Length, Is.EqualTo(1));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator ConnectedChildPhysicalRootBlocksParentResourceAndNpcPickupWithoutFreezingPhysics()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail and board prefabs.");
        yield break;
#else
        GameObject boardPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/WoodenBoardResource_prefab.prefab");
        Assert.That(boardPrefab, Is.Not.Null);
        System.Type resourceType = System.Type.GetType("BaseResourceNew, Assembly-CSharp");
        System.Type npcType = System.Type.GetType("NPCCarrier, Assembly-CSharp");
        Assert.That(resourceType, Is.Not.Null);
        Assert.That(npcType, Is.Not.Null);

        first = new GameObject("resource parent with physical child");
        roots.Add(first);
        first.transform.position = new Vector3(50f, 20f, 0f);
        Component resource = first.AddComponent(resourceType);
        Component prefabResource = boardPrefab.GetComponent(resourceType);
        object resourceProfile = resourceType.GetMethod("GetBaseResourceSO", PublicInstance).Invoke(prefabResource, null);
        resourceType.GetField("baseResourceSO", PrivateInstance).SetValue(resource, resourceProfile);
        GameObject physicalChild = new("resource Rigidbody child");
        roots.Add(physicalChild);
        physicalChild.transform.SetParent(first.transform, false);
        Rigidbody childBody = physicalChild.AddComponent<Rigidbody>();
        childBody.useGravity = false;
        physicalChild.AddComponent<BoxCollider>();

        second = CreateTarget("other connected target", new Vector3(50f, 20f, 0.2f), true);
        Component connectionNail = CreateNail(out GameObject connectionNailObject);
        roots.Add(connectionNailObject);
        ConfigureRegisteredPair(connectionNail, physicalChild.transform, second.transform);

        GameObject colliderDecoration = new("nested pickup collider under connected body");
        roots.Add(colliderDecoration);
        colliderDecoration.transform.SetParent(physicalChild.transform, false);
        colliderDecoration.AddComponent<BoxCollider>();
        System.Type sceneStateType = System.Type.GetType("MountingNailSceneState, Assembly-CSharp");
        Assert.That((bool)sceneStateType.GetMethod("IsTargetConnected", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { colliderDecoration.transform }), Is.True,
            "Offline pickup queries from collider descendants resolve through the locked Rigidbody physical root.");

        Assert.That((bool)resourceType.GetProperty("CanBeCarried", PublicInstance).GetValue(resource), Is.True,
            "Connection admission does not change the object's underlying carry/physics capability.");
        Assert.That((bool)resourceType.GetProperty("CanBePickedUp", PublicInstance).GetValue(resource), Is.False,
            "A connected child physical root blocks pickup through its carryable parent.");
        resourceType.GetMethod("UpdatePickedUpProperties", PrivateInstance).Invoke(resource, null);
        Assert.That(childBody.isKinematic, Is.False, "The connected physics body remains dynamic on the authoritative offline simulation.");
        Assert.That(childBody.useGravity, Is.False);

        GameObject npcObject = new("NPC pickup fixture");
        roots.Add(npcObject);
        Component npc = npcObject.AddComponent(npcType);
        bool acceptedByNpc = (bool)npcType.GetMethod("TryPickup", PublicInstance).Invoke(npc, new object[] { first });
        Assert.That(acceptedByNpc, Is.False, "NPCCarrier delegates to the same connected-object admission gate.");
        Assert.That(npcType.GetProperty("CarriedObject", PublicInstance).GetValue(npc), Is.Null);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator ConnectedEquippableToolRejectsInventoryPickup()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Industrial Hammer item data.");
        yield break;
#else
        first = CreateTarget("connected industrial hammer", new Vector3(50f, 20f, 0f), true);
        second = CreateTarget("connected board", new Vector3(50f, 20f, 0.2f), true);
        ScriptableObject hammerData = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        Assert.That(hammerData, Is.Not.Null);
        Component equippable = first.AddComponent(System.Type.GetType("EquippableItem, Assembly-CSharp"));
        equippable.GetType().GetField("equippableItemSO", PrivateInstance).SetValue(equippable, hammerData);
        GameObject player = new("Valid inventory pickup fixture");
        roots.Add(player);
        Behaviour input = (Behaviour)player.AddComponent(System.Type.GetType("PlayerInputNew, Assembly-CSharp"));
        input.enabled = false;
        Component inventory = player.AddComponent(System.Type.GetType("PlayerInventory, Assembly-CSharp"));
        inventory.GetType().GetField("_inventorySlots", PrivateInstance).SetValue(inventory, 6);
        inventory.GetType().GetField("_currentInventoryOccupiedSlots", PrivateInstance).SetValue(inventory, 0);
        Assert.That((bool)inventory.GetType().GetMethod("CanAddItem", PublicInstance).Invoke(inventory, new object[] { hammerData }), Is.True,
            "The Industrial Hammer is otherwise admissible to this player's inventory.");
        Component connectionNail = CreateNail(out GameObject connectionNailObject);
        roots.Add(connectionNailObject);
        ConfigureRegisteredPair(connectionNail, first.transform, second.transform);
        equippable.GetType().GetMethod("Interact", PublicInstance).Invoke(equippable, new object[] { player.transform });
        equippable.GetType().GetMethod("CompletePickup", PrivateInstance).Invoke(equippable, new object[] { player.transform });
        Assert.That(first, Is.Not.Null, "Both Equippable pickup entrypoints reject the connected tool before inventory transfer.");
        Assert.That((int)inventory.GetType().GetField("_currentInventoryOccupiedSlots", PrivateInstance).GetValue(inventory), Is.Zero);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator ConnectedMountableRejectsPlayerAndNpcPickupEntrypoints()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail and mountable component types.");
        yield break;
#else
        first = CreateTarget("connected mountable bridge", new Vector3(50f, 20f, 0f), true);
        second = CreateTarget("mountable partner", new Vector3(50f, 20f, 0.2f), true);
        System.Type mountableType = System.Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        System.Type mountableProfileType = System.Type.GetType("MountableBridgeComponentSO, Assembly-CSharp");
        Component mountable = first.AddComponent(mountableType);
        ScriptableObject mountableProfile = ScriptableObject.CreateInstance(mountableProfileType);
        profiles.Add(mountableProfile);
        mountableType.GetField("mountableBridgeComponentSO", PrivateInstance).SetValue(mountable, mountableProfile);
        Component connectionNail = CreateNail(out GameObject connectionNailObject);
        roots.Add(connectionNailObject);
        ConfigureRegisteredPair(connectionNail, first.transform, second.transform);

        GameObject player = new("Connected mountable player admission fixture");
        roots.Add(player);
        player.AddComponent(System.Type.GetType("PlayerInputNew, Assembly-CSharp"));
        Component playerInteraction = player.AddComponent(System.Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        MethodInfo localPickup = playerInteraction.GetType().GetMethod("PickUpObject", PublicInstance,
            null, new[] { typeof(GameObject), System.Type.GetType("IPIckableNew, Assembly-CSharp"), typeof(bool) }, null);
        Assert.That(localPickup, Is.Not.Null);
        localPickup.Invoke(playerInteraction, new object[] { first, mountable, false });
        Assert.That(playerInteraction.GetType().GetMethod("GetPickedUpGameObject", PublicInstance).Invoke(playerInteraction, null), Is.Null,
            "PlayerInteraction's actual local pickup admission leaves the connected mountable on the ground.");
        mountableType.GetMethod("PickedUp", PublicInstance).Invoke(mountable, new object[] { player.transform });
        Assert.That((bool)mountableType.GetProperty("IsPickedUp", PublicInstance).GetValue(mountable), Is.False,
            "MountableBridgeComponent.PickedUp refuses the connected bridge.");

        Component npc = new GameObject("Connected mountable NPC admission fixture").AddComponent(
            System.Type.GetType("NPCCarrier, Assembly-CSharp"));
        roots.Add(npc.gameObject);
        Assert.That((bool)npc.GetType().GetMethod("TryPickup", PublicInstance).Invoke(npc, new object[] { first }), Is.False,
            "NPCCarrier rejects the connected mountable through its production TryPickup route.");
        Assert.That((bool)mountableType.GetMethod("TryPickupByCarrier", PublicInstance)
            .Invoke(mountable, new object[] { npc }), Is.False,
            "The mountable's carrier-level admission also rejects the connection.");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator ActorCannotBecomeSecondPairMember()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        CreatePair();
        System.Type actorType = System.Type.GetType("NPCFactionMember, Assembly-CSharp");
        Assert.That(actorType, Is.Not.Null);
        Assert.That(second.AddComponent(actorType), Is.Not.Null);
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedSingle");
        Assert.That(second.GetComponents<FixedJoint>().Length, Is.Zero);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator NailFollowsFirstTargetDuringPartialInsertion()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        CreatePair();
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        Vector3 initialNailPosition = nailObject.transform.position;
        first.transform.position += Vector3.up * 0.05f;
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        Assert.That(nailObject.transform.position.y - initialNailPosition.y, Is.EqualTo(0.05f).Within(0.005f));
        AssertState(nail, "Partial");
#endif
    }

    [UnityTest]
    public IEnumerator RemovingSecondKeepsNailOnFirstAndRemovingFirstRemovesNail()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        CreatePair();
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedPair");
        Object.Destroy(second);
        for (int i = 0; i < 2; i++) yield return new WaitForFixedUpdate();
        yield return null;
        Assert.That(nailObject == null, Is.False, "Loss of the second target preserves the nail on its first target.");
        AssertState(nail, "EmbeddedSingle");
        Assert.That(TransformField(nail, "embeddedTarget"), Is.SameAs(first.transform));
        yield return null;
        Object.Destroy(first);
        for (int i = 0; i < 2; i++) yield return new WaitForFixedUpdate();
        yield return null;
        Assert.That(nailObject == null, Is.True, "Loss of the originally selected first target removes the nail.");
#endif
    }

    [UnityTest]
    public IEnumerator MultipleCollidersOnOnePhysicalRootDoNotCreateAFakePair()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        first = CreateTarget("single physical target", new Vector3(50f, 20f, 0f), true);
        GameObject child = new("second collider on the same target");
        roots.Add(child);
        child.transform.SetParent(first.transform, false);
        child.transform.localPosition = new Vector3(0.2f, 0f, 0f);
        child.AddComponent<BoxCollider>().size = new Vector3(0.4f, 0.4f, 0.1f);
        nail = CreateNail(out nailObject);
        nail.GetType().GetProperty("Profile", PublicInstance).GetValue(nail).GetType()
            .GetField("requiredHitsForSingleObject", PrivateInstance).SetValue(
                nail.GetType().GetProperty("Profile", PublicInstance).GetValue(nail), 1);
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedSingle");
        Assert.That(Int(nail, "RequiredHitCount"), Is.EqualTo(1));
        Assert.That(first.GetComponents<FixedJoint>().Length, Is.Zero);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator BodylessSceneIdentityGroupsStaticChildCollidersIntoOnePhysicalRoot()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses Unity's authored scene identity component.");
        yield break;
#else
        GameObject physicalRoot = new("bodyless static physical root");
        roots.Add(physicalRoot);
        Component identity = physicalRoot.AddComponent(System.Type.GetType("MountingNailSceneTargetIdentity, Assembly-CSharp"));
        identity.GetType().GetField("stableTargetId", PrivateInstance).SetValue(identity, "stage3-test-static-root");
        GameObject firstColliderObject = new("static collider one");
        GameObject secondColliderObject = new("static collider two");
        roots.Add(firstColliderObject);
        roots.Add(secondColliderObject);
        firstColliderObject.transform.SetParent(physicalRoot.transform, false);
        secondColliderObject.transform.SetParent(physicalRoot.transform, false);
        Collider firstCollider = firstColliderObject.AddComponent<BoxCollider>();
        Collider secondCollider = secondColliderObject.AddComponent<BoxCollider>();
        MethodInfo resolve = System.Type.GetType("MountingNail, Assembly-CSharp").GetMethod("ResolveTargetRoot", PrivateInstance | BindingFlags.Static);
        Assert.That(resolve.Invoke(null, new object[] { firstCollider }), Is.SameAs(physicalRoot.transform));
        Assert.That(resolve.Invoke(null, new object[] { secondCollider }), Is.SameAs(physicalRoot.transform),
            "An explicit bodyless scene identity groups static sibling colliders without relying on names or hierarchy guessing.");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator StaticFirstTargetAnchorsDynamicSecondToWorldAtCurrentPose()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        first = new GameObject("static first target");
        roots.Add(first);
        first.transform.position = new Vector3(50f, 20f, 0f);
        first.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.1f);
        second = CreateTarget("moving second target", new Vector3(50f, 20f, 0.2f), true);
        nail = CreateNail(out nailObject);
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedPair");
        FixedJoint joint = second.GetComponent<FixedJoint>();
        Assert.That(joint, Is.Not.Null);
        Assert.That(joint.connectedBody, Is.Null, "A target without a Rigidbody is a world anchor.");
        Assert.That(Vector3.Distance(first.transform.position, new Vector3(50f, 20f, 0f)), Is.LessThan(0.002f));
        Assert.That(Vector3.Distance(second.transform.position, new Vector3(50f, 20f, 0.2f)), Is.LessThan(0.002f));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator DynamicFirstTargetAnchorsStaticSecondToWorldAtCurrentPose()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        first = CreateTarget("moving first target", new Vector3(50f, 20f, 0f), true);
        second = new GameObject("static second target");
        roots.Add(second);
        second.transform.position = new Vector3(50f, 20f, 0.2f);
        second.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.1f);
        nail = CreateNail(out nailObject);
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedPair");
        FixedJoint joint = first.GetComponent<FixedJoint>();
        Assert.That(joint, Is.Not.Null);
        Assert.That(joint.connectedBody, Is.Null);
        Assert.That(Vector3.Distance(first.transform.position, new Vector3(50f, 20f, 0f)), Is.LessThan(0.002f));
        Assert.That(Vector3.Distance(second.transform.position, new Vector3(50f, 20f, 0.2f)), Is.LessThan(0.002f));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator PairRangeIncludesBoundaryAndRejectsTargetBeyondIt()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        CreatePair();
        second.transform.position = new Vector3(50f, 20f, 0.30f);
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(Int(nail, "RequiredHitCount"), Is.EqualTo(3), "A second collider at the penetration limit is eligible.");
        Object.Destroy(nailObject);
        yield return null;
        nail = CreateNail(out nailObject);
        second.transform.position = new Vector3(50f, 20f, 0.305f);
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(Int(nail, "RequiredHitCount"), Is.EqualTo(1), "A target beyond the limit cannot form a pair.");
#endif
    }

    [UnityTest]
    public IEnumerator CarriedSecondAtFinalHitFallsBackToTheOriginalFirst()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        CreatePair();
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(ApplyHammer(nail), Is.True);
        System.Type resourceType = System.Type.GetType("BaseResourceNew, Assembly-CSharp");
        Assert.That(resourceType, Is.Not.Null);
        Component carriedResource = second.AddComponent(resourceType);
        resourceType.GetField("isPickedUp", PrivateInstance).SetValue(carriedResource, true);
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedSingle");
        Assert.That(TransformField(nail, "embeddedTarget"), Is.SameAs(first.transform));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator CarriedFirstAtFinalHitFallsBackToSingleWithoutJoint()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        CreatePair();
        PrepareNail(nail, nailObject, first);
        Assert.That(ApplyHammer(nail), Is.True);
        Assert.That(ApplyHammer(nail), Is.True);
        System.Type resourceType = System.Type.GetType("BaseResourceNew, Assembly-CSharp");
        Assert.That(resourceType, Is.Not.Null);
        Component carriedResource = first.AddComponent(resourceType);
        resourceType.GetField("isPickedUp", PrivateInstance).SetValue(carriedResource, true);
        Assert.That(ApplyHammer(nail), Is.True);
        AssertState(nail, "EmbeddedSingle");
        Assert.That(TransformField(nail, "embeddedTarget"), Is.SameAs(first.transform));
        Assert.That(first.GetComponents<FixedJoint>().Length + second.GetComponents<FixedJoint>().Length, Is.Zero);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator ThreeTargetChainKeepsRemainingEdgeAfterOneEdgeIsRemoved()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        first = CreateTarget("chain A", new Vector3(50f, 20f, 0f), true);
        second = CreateTarget("chain B", new Vector3(50f, 20f, 0.2f), true);
        GameObject third = CreateTarget("chain C", new Vector3(50f, 20f, 0.4f), true);
        Component edgeAB = CreateNail(out GameObject edgeABObject);
        Component edgeBC = CreateNail(out GameObject edgeBCObject);
        roots.Add(edgeABObject);
        roots.Add(edgeBCObject);
        ConfigureRegisteredPair(edgeAB, first.transform, second.transform);
        ConfigureRegisteredPair(edgeBC, second.transform, third.transform);
        object registry = edgeAB.GetType().GetField("connectionRegistry", PrivateInstance).GetValue(edgeAB);
        Assert.That(first.GetComponents<FixedJoint>().Length + second.GetComponents<FixedJoint>().Length, Is.EqualTo(2));
        Assert.That(second.GetComponents<FixedJoint>().Length + third.GetComponents<FixedJoint>().Length, Is.EqualTo(1));
        registry.GetType().GetMethod("Remove", PrivateInstance | BindingFlags.Instance).Invoke(registry, new object[] { edgeAB });
        yield return new WaitForFixedUpdate();
        Assert.That(first.GetComponents<FixedJoint>().Length, Is.Zero);
        Assert.That(second.GetComponents<FixedJoint>().Length + third.GetComponents<FixedJoint>().Length, Is.EqualTo(1),
            "Removing AB leaves the BC edge and its physical joint intact.");
        Assert.That((int)registry.GetType().GetMethod("Degree", PrivateInstance | BindingFlags.Instance)
            .Invoke(registry, new object[] { second.transform }), Is.EqualTo(1));
#endif
    }

    [UnityTest]
    public IEnumerator ReversedNailsSharingAnEdgeHandleEndpointDeletionPerNail()
    {
#if !UNITY_EDITOR
        Assert.Fail("Uses the project's authored Unity nail prefab.");
        yield break;
#else
        first = CreateTarget("first endpoint", new Vector3(50f, 20f, 0f), true);
        second = CreateTarget("second endpoint", new Vector3(50f, 20f, 0.2f), true);
        Component forwardNail = CreateNail(out GameObject forwardObject);
        Component reverseNail = CreateNail(out GameObject reverseObject);
        roots.Add(forwardObject);
        roots.Add(reverseObject);
        ConfigureRegisteredPair(forwardNail, first.transform, second.transform);
        ConfigureRegisteredPair(reverseNail, second.transform, first.transform);
        object registry = forwardNail.GetType().GetField("connectionRegistry", PrivateInstance).GetValue(forwardNail);
        Assert.That(registry, Is.Not.Null);
        second.SetActive(false);
        Physics.SyncTransforms();
        registry.GetType().GetMethod("Tick", PrivateInstance | BindingFlags.Instance).Invoke(registry, null);
        AssertState(forwardNail, "EmbeddedSingle");
        yield return null;
        Assert.That(reverseObject == null, Is.True, "A nail whose own first endpoint is removed is destroyed.");
#endif
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (offlineSingleton != null && offlineSingleton.IsListening) offlineSingleton.Shutdown(true);
        foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        if (nailObject != null) Object.Destroy(nailObject);
        if (first != null) Object.Destroy(first);
        if (second != null) Object.Destroy(second);
        if (replacement != null) Object.Destroy(replacement);
        foreach (ScriptableObject profile in profiles) if (profile != null) Object.Destroy(profile);
        yield return null;
        if (previousSingleton != null) previousSingleton.SetSingleton();
        else typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
    }

#if UNITY_EDITOR
    private void CreatePair()
    {
        first = CreateTarget("first target", new Vector3(50f, 20f, 0f), true);
        second = CreateTarget("second target", new Vector3(50f, 20f, 0.20f), true);
        nail = CreateNail(out nailObject);
    }

    private GameObject CreateTarget(string name, Vector3 position, bool dynamic)
    {
        GameObject target = new(name);
        roots.Add(target);
        target.transform.position = position;
        Rigidbody body = target.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = !dynamic;
        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.size = new Vector3(1f, 1f, 0.1f);
        return target;
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
        profile.GetType().GetField("requiredHitsForTwoObjects", PrivateInstance).SetValue(profile, 3);
        GameObject inactive = new("Stage 3 nail fixture");
        roots.Add(inactive);
        inactive.SetActive(false);
        instance = Object.Instantiate(prefab, inactive.transform);
        nailType.GetField("profile", PrivateInstance).SetValue(instance.GetComponent(nailType), profile);
        inactive.SetActive(true);
        return instance.GetComponent(nailType);
    }

    private static void PrepareNail(Component targetNail, GameObject targetNailObject, GameObject target)
    {
        Assert.That(target, Is.Not.Null);
        Collider targetCollider = target.GetComponent<Collider>();
        object profile = targetNail.GetType().GetProperty("Profile", PublicInstance).GetValue(targetNail);
        float length = (float)profile.GetType().GetProperty("LengthMeters", PublicInstance).GetValue(profile);
        Bounds bounds = targetCollider.bounds;
        targetNailObject.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.center.y,
            bounds.min.z - length * 0.5f - 0.005f), Quaternion.identity);
        Physics.SyncTransforms();
        targetNail.GetType().GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(targetNail, new object[] { null });
        AssertState(targetNail, "Prepared");
    }

    private static void ConfigureRegisteredPair(Component targetNail, Transform firstTarget, Transform secondTarget)
    {
        MethodInfo setTargets = targetNail.GetType().GetMethod("SetEmbeddedTargets", PrivateInstance);
        setTargets.Invoke(targetNail, new object[] { firstTarget, secondTarget, firstTarget.position, Vector3.forward });
        bool registered = (bool)targetNail.GetType().GetMethod("TryRegisterPairConnection", PrivateInstance)
            .Invoke(targetNail, null);
        Assert.That(registered, Is.True);
    }

    private static bool ApplyHammer(Component targetNail) => (bool)targetNail.GetType().GetMethod("TryApplyHammerHit", PublicInstance)
        .Invoke(targetNail, new[] { System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer") });
    private static int Int(Component targetNail, string property) => (int)targetNail.GetType().GetProperty(property, PublicInstance).GetValue(targetNail);
    private static float Float(Component targetNail, string property) => (float)targetNail.GetType().GetProperty(property, PublicInstance).GetValue(targetNail);
    private static Transform TransformField(Component targetNail, string field) =>
        (Transform)targetNail.GetType().GetField(field, PrivateInstance).GetValue(targetNail);
    private static void AssertState(Component targetNail, string expected) =>
        Assert.That(targetNail.GetType().GetProperty("State", PublicInstance).GetValue(targetNail).ToString(), Is.EqualTo(expected));
#endif
}
