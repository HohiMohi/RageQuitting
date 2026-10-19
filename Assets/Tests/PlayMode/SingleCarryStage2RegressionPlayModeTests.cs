using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Reproductions for single-carry contact side effects reported in Stage 2.
/// These tests deliberately assert the expected isolation behavior; against the current
/// implementation they are expected to expose any push from a held kinematic collider.
/// </summary>
public sealed class SingleCarryStage2RegressionPlayModeTests
{
    private const int UnityMaximumLayerOverridePriority = 64;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private readonly List<GameObject> roots = new();
    private readonly List<UnityEngine.Object> transientAssets = new();
    private GameObject player;
    private GameObject resource;
    private GameObject testBody;
    private GameObject holdAnchor;
    private GameObject rayOrigin;
    private Component interaction;
    private Component resourceComponent;
    private Rigidbody heldBody;
    private Collider heldCollider;
    private Component playerInput;
    private Component playerInputSystem;

    [UnityTest]
    public IEnumerator CarryingWoodenBoardDoesNotPushDynamicItemAndDropRestoresPhysics()
    {
#if !UNITY_EDITOR
        Assert.Ignore("This fixture loads production prefabs using the Unity Editor AssetDatabase.");
        yield break;
#else
        yield return ReproduceCarryContact("Assets/Prefabs/New/Resources/WoodenBoardResource_prefab.prefab", false);
#endif
    }

    [UnityTest]
    public IEnumerator CarryingLooseNailDoesNotPushDynamicItemAndDropRestoresPhysics()
    {
#if !UNITY_EDITOR
        Assert.Ignore("This fixture loads production prefabs using the Unity Editor AssetDatabase.");
        yield break;
#else
        yield return ReproduceCarryContact("Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab", true);
#endif
    }

    [UnityTest]
    public IEnumerator PerColliderContactExclusionProbePreservesQueriesPlacementAndCharacterControllerMove()
    {
        GameObject held = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(held);
        held.name = "Single Carry Stage 2 per-collider exclusion probe";
        held.transform.position = new Vector3(0f, 100f, 0f);
        Rigidbody heldBodyProbe = held.AddComponent<Rigidbody>();
        heldBodyProbe.useGravity = false;
        heldBodyProbe.isKinematic = true;
        Collider heldColliderProbe = held.GetComponent<Collider>();
        LayerMask originalExcludes = heldColliderProbe.excludeLayers;
        int originalPriority = heldColliderProbe.layerOverridePriority;
        heldColliderProbe.layerOverridePriority = int.MaxValue;
        heldColliderProbe.excludeLayers = Physics.AllLayers;

        GameObject dynamicTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(dynamicTarget);
        dynamicTarget.name = "Single Carry Stage 2 exclusion probe dynamic body";
        dynamicTarget.transform.position = new Vector3(0f, 100f, 0f);
        dynamicTarget.transform.localScale = Vector3.one * 0.2f;
        Rigidbody targetBodyProbe = dynamicTarget.AddComponent<Rigidbody>();
        targetBodyProbe.useGravity = false;
        targetBodyProbe.mass = 0.15f;
        targetBodyProbe.linearDamping = 0f;
        SingleCarryStage2CollisionRecorder recorder = dynamicTarget.AddComponent<SingleCarryStage2CollisionRecorder>();
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        Vector3 targetStart = targetBodyProbe.position;

        Assert.That(Physics.Raycast(new Vector3(-3f, 100f, 0f), Vector3.right, out RaycastHit queryRay,
            6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide), Is.True);
        Assert.That(queryRay.collider, Is.SameAs(heldColliderProbe));
        Assert.That(Physics.SphereCast(new Vector3(-3f, 100f, 0f), 0.02f, Vector3.right,
            out RaycastHit querySphere, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide), Is.True);
        Assert.That(querySphere.collider, Is.SameAs(heldColliderProbe));

        const int steps = 48;
        for (int i = 1; i <= steps; i++)
        {
            held.transform.position = new Vector3(Mathf.Lerp(-2f, 2f, i / (float)steps), 100f, 0f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
        }
        Assert.That(recorder.HadCollision, Is.False, "Maximum-priority per-collider exclusions should suppress physical contacts.");
        Assert.That(Vector3.Distance(targetBodyProbe.position, targetStart), Is.LessThan(0.005f));

        // The layer exclusion affects contact generation, not geometry queries. Exercise the
        // production pose solver and occupancy validator with a separate held-shape candidate.
        GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(source);
        source.name = "Single Carry Stage 2 exclusion probe placement source";
        source.transform.position = new Vector3(8f, 100f, 0f);
        Collider sourceCollider = source.GetComponent<Collider>();
        sourceCollider.excludeLayers = Physics.AllLayers;
        sourceCollider.layerOverridePriority = int.MaxValue;
        GameObject support = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(support);
        support.name = "Single Carry Stage 2 exclusion probe support";
        support.transform.position = new Vector3(8f, 100f, 5f);
        support.transform.localScale = new Vector3(3f, 3f, 0.1f);
        Physics.SyncTransforms();

        Type geometryType = Type.GetType("SingleCarryPlacementGeometrySolver, Assembly-CSharp");
        Type validatorType = Type.GetType("SingleCarryPlacementValidator, Assembly-CSharp");
        Assert.That(geometryType, Is.Not.Null);
        Assert.That(validatorType, Is.Not.Null);
        object solver = Activator.CreateInstance(geometryType, new object[] { source });
        MethodInfo calculate = geometryType.GetMethod("Calculate", PublicInstance);
        Ray placementRay = new(new Vector3(8f, 100f, 0f), Vector3.forward);
        object pose = calculate.Invoke(solver, new object[] { placementRay, 5f, Quaternion.identity, Quaternion.identity, null });
        Assert.That(pose.GetType().GetProperty("Status").GetValue(pose).ToString(), Is.EqualTo("SurfaceSupported"));
        object validator = Activator.CreateInstance(validatorType, new object[] { source });
        MethodInfo validate = validatorType.GetMethod("Validate", PublicInstance);
        object validResult = validate.Invoke(validator, new object[] { pose, null });
        Assert.That((bool)validResult.GetType().GetProperty("IsValid").GetValue(validResult), Is.True,
            "Contact exclusions must not make a geometrically supported F placement fail validation.");

        Vector3 candidatePosition = (Vector3)pose.GetType().GetProperty("Position").GetValue(pose);
        GameObject occupancyBlocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(occupancyBlocker);
        occupancyBlocker.name = "Single Carry Stage 2 exclusion probe occupancy blocker";
        occupancyBlocker.transform.position = candidatePosition;
        occupancyBlocker.transform.localScale = Vector3.one * 0.08f;
        Physics.SyncTransforms();
        object blockedResult = validate.Invoke(validator, new object[] { pose, null });
        Assert.That(blockedResult.GetType().GetProperty("Status").GetValue(blockedResult).ToString(), Is.EqualTo("Occupied"),
            "Contact exclusions must not hide a genuinely occupied F pose.");

        GameObject character = new("Single Carry Stage 2 exclusion probe CharacterController");
        roots.Add(character);
        character.transform.position = new Vector3(-3f, 100f, 0f);
        CharacterController characterController = character.AddComponent<CharacterController>();
        characterController.height = 1f;
        characterController.radius = 0.2f;
        characterController.center = new Vector3(0f, 0.5f, 0f);
        held.transform.position = Vector3.zero + new Vector3(0f, 100f, 0f);
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        CollisionFlags characterFlags = characterController.Move(Vector3.right * 6f);
        TestContext.WriteLine($"Per-collider exclusion probe: ray={queryRay.collider.name}, sphere={querySphere.collider.name}, targetDisplacement={Vector3.Distance(targetBodyProbe.position, targetStart):F5}m, collisionCallback={recorder.HadCollision}, placement={pose.GetType().GetProperty("Status").GetValue(pose)}, blockedPose={blockedResult.GetType().GetProperty("Status").GetValue(blockedResult)}, ccX={character.transform.position.x:F3}, ccFlags={characterFlags}, originalExclude={originalExcludes.value}, originalPriority={originalPriority}");
        Assert.That(character.transform.position.x, Is.GreaterThan(1f),
            "CharacterController.Move should pass through the held contact-excluded collider.");

        Cleanup();
        yield return null;
    }

    [UnityTest]
    public IEnumerator SharedCarryTargetDoesNotEnableSingleCarryContactIsolation()
    {
#if !UNITY_EDITOR
        Assert.Ignore("This fixture loads production prefabs using the Unity Editor AssetDatabase.");
        yield break;
#else
        Type resourceType = Type.GetType("BaseResourceNew, Assembly-CSharp");
        GameObject sharedPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/WoodenLogResource_prefab.prefab");
        Assert.That(sharedPrefab, Is.Not.Null);
        resource = UnityEngine.Object.Instantiate(sharedPrefab, new Vector3(0f, 100f, 0f), Quaternion.identity);
        roots.Add(resource);
        resource.name = "Single Carry Stage 2 shared carry positive control";
        Type billboardType = Type.GetType("LookAtCamera, Assembly-CSharp");
        if (billboardType != null)
        {
            foreach (Component billboard in resource.GetComponentsInChildren(billboardType, true))
                if (billboard is Behaviour behaviour) behaviour.enabled = false;
        }
        resourceComponent = resource.GetComponent(resourceType);
        heldBody = resource.GetComponent<Rigidbody>();
        Assert.That(resourceComponent, Is.Not.Null);
        Assert.That(heldBody, Is.Not.Null);
        object profile = resourceType.GetField("baseResourceSO", PrivateInstance).GetValue(resourceComponent);
        Assert.That((bool)profile.GetType().GetField("allowMultipleCarriers", PublicInstance).GetValue(profile), Is.True,
            "The positive control must use a production shared-carry resource profile.");

        List<(Collider collider, int excluded, int priority)> original = new();
        foreach (Collider collider in resource.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || collider.isTrigger) continue;
            collider.excludeLayers = 1 << (original.Count % 6);
            collider.layerOverridePriority = 17 + original.Count;
            original.Add((collider, collider.excludeLayers.value, collider.layerOverridePriority));
        }

        MethodInfo setPickedUp = resourceType.GetMethod("SetPickedUpState", PrivateInstance);
        Assert.That(setPickedUp, Is.Not.Null);
        setPickedUp.Invoke(resourceComponent, new object[] { true });
        foreach (var setting in original)
        {
            Assert.That(setting.collider.excludeLayers.value, Is.EqualTo(setting.excluded));
            Assert.That(setting.collider.layerOverridePriority, Is.EqualTo(setting.priority));
        }
        setPickedUp.Invoke(resourceComponent, new object[] { false });
        foreach (var setting in original)
        {
            Assert.That(setting.collider.excludeLayers.value, Is.EqualTo(setting.excluded));
            Assert.That(setting.collider.layerOverridePriority, Is.EqualTo(setting.priority));
        }
        TestContext.WriteLine($"Shared carry positive control: profile allowsMultipleCarriers=true; checked {original.Count} solid colliders before/after picked-up state.");
        Cleanup();
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator MountableBridgeSingleCarryPickupRestoresOriginalColliderSettings()
    {
#if !UNITY_EDITOR
        Assert.Ignore("This fixture loads production prefabs using the Unity Editor AssetDatabase.");
        yield break;
#else
        Type interactionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        Type mountableType = Type.GetType("MountableBridgeComponent, Assembly-CSharp");
        Type profileType = Type.GetType("MountableBridgeComponentSO, Assembly-CSharp");
        Type inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        GameObject mountablePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/BridgeComponents/WoodenBasicRoadwayMountable.prefab");
        UnityEngine.Object profileAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenAbutmentMountable.asset");
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(mountablePrefab, Is.Not.Null);
        Assert.That(profileAsset, Is.Not.Null);

        CreateIsolatedPlayer(playerPrefab, new Vector3(0f, 100f, 0f),
            "Single Carry Stage 2 bridge single carry player", interactionType, inputType);
        Assert.That(interaction, Is.Not.Null);
        holdAnchor = new GameObject("Single Carry Stage 2 bridge hold anchor");
        roots.Add(holdAnchor);
        holdAnchor.transform.position = new Vector3(-2f, 100f, 1.2f);
        interactionType.GetField("pickUpHoldPositionHolder", PrivateInstance).SetValue(interaction, holdAnchor.transform);

        resource = UnityEngine.Object.Instantiate(mountablePrefab, holdAnchor.transform.position, Quaternion.identity);
        roots.Add(resource);
        resource.name = "Single Carry Stage 2 bridge single carry target";
        Component mountable = resource.GetComponent(mountableType);
        Assert.That(mountable, Is.Not.Null);
        UnityEngine.Object profileClone = UnityEngine.Object.Instantiate(profileAsset);
        transientAssets.Add(profileClone);
        profileType.GetField("allowMultipleCarriers", PublicInstance).SetValue(profileClone, false);
        mountableType.GetField("mountableBridgeComponentSO", PrivateInstance).SetValue(mountable, profileClone);

        List<(Collider collider, int excluded, int priority)> original = new();
        foreach (Collider collider in resource.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || collider.isTrigger) continue;
            collider.excludeLayers = 1 << (original.Count % 6);
            collider.layerOverridePriority = 23 + original.Count;
            original.Add((collider, collider.excludeLayers.value, collider.layerOverridePriority));
        }
        Assert.That(original.Count, Is.GreaterThan(0));

        MethodInfo pickedUp = mountableType.GetMethod("PickedUp", PublicInstance);
        MethodInfo dropped = mountableType.GetMethod("DroppedDown", PublicInstance);
        Assert.That(pickedUp, Is.Not.Null);
        Assert.That(dropped, Is.Not.Null);
        pickedUp.Invoke(mountable, new object[] { player.transform });
        yield return new WaitForFixedUpdate();
        Assert.That((bool)mountableType.GetProperty("IsPickedUp", PublicInstance).GetValue(mountable), Is.True);
        foreach (var setting in original)
        {
            Assert.That(setting.collider.excludeLayers.value, Is.EqualTo(Physics.AllLayers));
            Assert.That(setting.collider.layerOverridePriority, Is.EqualTo(UnityMaximumLayerOverridePriority));
        }
        dropped.Invoke(mountable, null);
        yield return new WaitForFixedUpdate();
        foreach (var setting in original)
        {
            Assert.That(setting.collider.excludeLayers.value, Is.EqualTo(setting.excluded));
            Assert.That(setting.collider.layerOverridePriority, Is.EqualTo(setting.priority));
        }
        TestContext.WriteLine($"Mountable bridge single-carry positive path: pickedUp={mountableType.GetProperty("IsPickedUp", PublicInstance).GetValue(mountable)}, checked {original.Count} solid colliders, restored custom masks/priorities.");
        Cleanup();
        yield return null;
#endif
    }

#if UNITY_EDITOR
    private IEnumerator ReproduceCarryContact(string resourcePath, bool isNail)
    {
        Type interactionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        Type resourceType = System.Type.GetType("BaseResourceNew, Assembly-CSharp");
        Type inputType = System.Type.GetType("PlayerInputNew, Assembly-CSharp");
        Assert.That(interactionType, Is.Not.Null);
        Assert.That(resourceType, Is.Not.Null);
        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        GameObject resourcePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(resourcePath);
        Assert.That(playerPrefab, Is.Not.Null, "Production player fixture must exist.");
        Assert.That(resourcePrefab, Is.Not.Null, $"Production carry resource must exist at {resourcePath}.");

        CreateIsolatedPlayer(playerPrefab, new Vector3(0f, 100f, 0f),
            "Single Carry Stage 2 collision repro player", interactionType, inputType);
        Assert.That(interaction, Is.Not.Null);

        holdAnchor = new GameObject("Single Carry Stage 2 controlled hold anchor");
        roots.Add(holdAnchor);
        holdAnchor.transform.SetPositionAndRotation(new Vector3(-1.5f, 100f, 1.2f), Quaternion.identity);
        interactionType.GetField("pickUpHoldPositionHolder", PrivateInstance).SetValue(interaction, holdAnchor.transform);

        resource = UnityEngine.Object.Instantiate(resourcePrefab, holdAnchor.transform.position, Quaternion.identity);
        resource.name = isNail ? "Single Carry Stage 2 held nail" : "Single Carry Stage 2 held board";
        roots.Add(resource);
        Type billboardType = Type.GetType("LookAtCamera, Assembly-CSharp");
        if (billboardType != null)
        {
            foreach (Component billboard in resource.GetComponentsInChildren(billboardType, true))
                if (billboard is Behaviour behaviour) behaviour.enabled = false;
        }
        resourceComponent = resource.GetComponent(resourceType);
        Assert.That(resourceComponent, Is.Not.Null);
        heldBody = resource.GetComponent<Rigidbody>();
        heldCollider = resource.GetComponent<Collider>();
        Assert.That(heldBody, Is.Not.Null);
        Assert.That(heldCollider, Is.Not.Null);
        Assert.That(heldCollider.enabled, Is.True);

        Dictionary<Collider, (int excludedLayers, int priority)> originalContactSettings = new();
        int settingsIndex = 0;
        foreach (Collider collider in resource.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || collider.isTrigger) continue;
            int customExcludedLayers = 1 << (settingsIndex % 6);
            int customPriority = 5 + settingsIndex;
            originalContactSettings.Add(collider, (customExcludedLayers, customPriority));
            collider.excludeLayers = customExcludedLayers;
            collider.layerOverridePriority = customPriority;
            settingsIndex++;
        }

        // The actual production pickup entry point performs BaseResourceNew eligibility,
        // holder bookkeeping, and kinematic carry setup.
        resourceType.GetMethod("PickedUp", PublicInstance).Invoke(resourceComponent, new object[] { player.transform });
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();

        Assert.That((bool)resourceType.GetProperty("IsPickedUp", PublicInstance).GetValue(resourceComponent), Is.True);
        Assert.That(heldBody.isKinematic, Is.True);
        Assert.That(heldBody.useGravity, Is.False);
        Assert.That(heldCollider.enabled, Is.True);
        foreach (KeyValuePair<Collider, (int excludedLayers, int priority)> pair in originalContactSettings)
        {
            Assert.That(pair.Key.excludeLayers.value, Is.EqualTo(Physics.AllLayers),
                "Every solid collider in the carried compound must exclude contact with all layers while held.");
            Assert.That(pair.Key.layerOverridePriority, Is.EqualTo(UnityMaximumLayerOverridePriority));
        }

        // Baseline query visibility while held, before the isolated detectCollisions probe.
        rayOrigin = new GameObject("Single Carry Stage 2 collision-query ray origin");
        roots.Add(rayOrigin);
        Vector3 rayStart = heldCollider.bounds.center - Vector3.forward * 5f;
        Vector3 rayDirection = Vector3.forward;
        rayOrigin.transform.position = rayStart;
        Assert.That(Physics.Raycast(rayStart, rayDirection, out RaycastHit heldRayHit, 10f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide), Is.True,
            "The enabled held collider must be targetable by a trigger-inclusive ray query.");
        Assert.That(heldRayHit.collider, Is.SameAs(heldCollider));
        Assert.That(Physics.SphereCast(rayStart, 0.01f, rayDirection, out RaycastHit heldSphereHit, 10f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide), Is.True,
            "The enabled held collider must be targetable by a trigger-inclusive sphere query.");
        Assert.That(heldSphereHit.collider, Is.SameAs(heldCollider));

        // The real carried prefab must also stop CharacterController.Move contacts while
        // retaining query visibility; the separate synthetic probe above is not a substitute.
        GameObject heldPathCharacter = new("Single Carry Stage 2 production held-path CharacterController");
        roots.Add(heldPathCharacter);
        heldPathCharacter.transform.position = heldCollider.bounds.center + new Vector3(-2f, -0.5f, 0f);
        CharacterController heldPathController = heldPathCharacter.AddComponent<CharacterController>();
        heldPathController.height = 1f;
        heldPathController.radius = 0.2f;
        heldPathController.center = new Vector3(0f, 0.5f, 0f);
        Physics.SyncTransforms();
        CollisionFlags heldPathFlags = heldPathController.Move(Vector3.right * 4f);
        TestContext.WriteLine($"Production held { (isNail ? "nail" : "board") } CharacterController sweep: heldColliderEnabled={heldCollider.enabled}, rayVisible={heldRayHit.collider == heldCollider}, sphereVisible={heldSphereHit.collider == heldCollider}, finalX={heldPathCharacter.transform.position.x:F3}, flags={heldPathFlags}");
        Assert.That(heldPathCharacter.transform.position.x, Is.GreaterThan(heldCollider.bounds.center.x + 1f),
            "A CharacterController must move through the actually carried single-carry collider.");

        // Isolated query behavior probe only: the production code is not altered. Unity's
        // detectCollisions switch is nonserialized and is restored before the contact test.
        heldBody.detectCollisions = false;
        Physics.SyncTransforms();
        bool querySeesWithCollisionDetectionDisabled = Physics.Raycast(rayStart, rayDirection,
            out RaycastHit disabledRayHit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
            && disabledRayHit.collider == heldCollider;
        bool sphereSeesWithCollisionDetectionDisabled = Physics.SphereCast(rayStart, 0.01f, rayDirection,
            out RaycastHit disabledSphereHit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
            && disabledSphereHit.collider == heldCollider;
        TestContext.WriteLine($"detectCollisions=false query visibility: ray={querySeesWithCollisionDetectionDisabled}, sphere={sphereSeesWithCollisionDetectionDisabled}, colliderEnabled={heldCollider.enabled}");
        heldBody.detectCollisions = true;
        Physics.SyncTransforms();

        testBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testBody.name = "Single Carry Stage 2 dynamic item contact target";
        roots.Add(testBody);
        testBody.transform.position = new Vector3(0f, 100f, 1.2f);
        testBody.transform.localScale = Vector3.one * 0.2f;
        Rigidbody targetBody = testBody.AddComponent<Rigidbody>();
        targetBody.useGravity = false;
        targetBody.mass = 0.15f;
        targetBody.linearDamping = 0f;
        targetBody.angularDamping = 0f;
        targetBody.interpolation = RigidbodyInterpolation.None;
        testBody.AddComponent<SingleCarryStage2CollisionRecorder>();
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();

        Vector3 targetStartPosition = targetBody.position;
        Vector3 targetStartVelocity = targetBody.linearVelocity;
        bool hadBoundsOverlap = false;
        MethodInfo moveHold = interactionType.GetMethod("MovePickedUpObjectToHoldPosition", PrivateInstance);
        Assert.That(moveHold, Is.Not.Null);
        const int sweepSteps = 72;
        const float sweepDuration = 1.2f;
        for (int i = 1; i <= sweepSteps; i++)
        {
            float t = i / (float)sweepSteps;
            float x = Mathf.Lerp(-1.5f, 1.5f, t);
            holdAnchor.transform.SetPositionAndRotation(new Vector3(x, 100f, 1.2f),
                Quaternion.Euler(0f, Mathf.Lerp(0f, 90f, t), 0f));
            moveHold.Invoke(interaction, null);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            if (heldCollider.bounds.Intersects(testBody.GetComponent<Collider>().bounds)) hadBoundsOverlap = true;
        }

        bool hadCollision = testBody.GetComponent<SingleCarryStage2CollisionRecorder>().HadCollision;
        float bodyDisplacement = Vector3.Distance(targetBody.position, targetStartPosition);
        float bodySpeed = targetBody.linearVelocity.magnitude;
        bool wasKinematicDuringHold = heldBody.isKinematic;
        bool hadCollisionDetectionDuringHold = heldBody.detectCollisions;
        bool hadGravityDuringHold = heldBody.useGravity;
        TestContext.WriteLine($"resource={(isNail ? "nail" : "board")}; boundsOverlap={hadBoundsOverlap}; actualCollisionCallback={hadCollision}; displacement={bodyDisplacement:F5}m; speed={bodySpeed:F5}m/s; kinematic={heldBody.isKinematic}; detectCollisions={heldBody.detectCollisions}; sweepSeconds={sweepDuration:F2}");
        Assert.That(hadBoundsOverlap, Is.True, "The controlled sweep must geometrically overlap the dynamic item.");
        // Exercise the real drop route after contact, then assert ordinary dynamic physics is back.
        interactionType.GetMethod("DropObject", PublicInstance).Invoke(interaction, null);
        yield return new WaitForFixedUpdate();
        Assert.That(heldBody.isKinematic, Is.False);
        Assert.That(heldBody.useGravity, Is.True);
        Assert.That(heldBody.detectCollisions, Is.True);
        Assert.That(heldCollider.enabled, Is.True);
        foreach (KeyValuePair<Collider, (int excludedLayers, int priority)> pair in originalContactSettings)
        {
            Assert.That(pair.Key.excludeLayers.value, Is.EqualTo(pair.Value.excludedLayers),
                "Drop must restore each collider's original exclude mask.");
            Assert.That(pair.Key.layerOverridePriority, Is.EqualTo(pair.Value.priority),
                "Drop must restore each collider's original override priority.");
        }

        Assert.That(Vector3.Distance(targetStartVelocity, Vector3.zero), Is.LessThan(0.0001f));
        string evidence = $"boundsOverlap={hadBoundsOverlap}, actualCollisionCallback={hadCollision}, " +
            $"displacement={bodyDisplacement:F5}m, speed={bodySpeed:F5}m/s, " +
            $"detectCollisionsFalseRayVisible={querySeesWithCollisionDetectionDisabled}, " +
            $"detectCollisionsFalseSphereVisible={sphereSeesWithCollisionDetectionDisabled}, " +
            $"duringHoldKinematic={wasKinematicDuringHold}, duringHoldDetectCollisions={hadCollisionDetectionDuringHold}, duringHoldGravity={hadGravityDuringHold}, " +
            $"afterDropKinematic={heldBody.isKinematic}, afterDropDetectCollisions={heldBody.detectCollisions}, afterDropGravity={heldBody.useGravity}";
        Assert.That(hadCollision, Is.False,
            "Held single-carry objects should not create solid contact pairs with separate dynamic items. " + evidence);
        Assert.That(bodyDisplacement, Is.LessThan(0.005f),
            "A carried single-carry collider must not impart motion to a separate dynamic item during hold/rotation. " + evidence);
        Assert.That(bodySpeed, Is.LessThan(0.02f),
            "The dynamic item should remain still while the single-carry object is held. " + evidence);

        Cleanup();
    }

    private void CreateIsolatedPlayer(GameObject prefab, Vector3 position, string objectName,
        Type interactionType, Type inputType)
    {
        GameObject inactiveParent = new("Single Carry Stage 2 inactive player fixture parent");
        inactiveParent.SetActive(false);
        roots.Add(inactiveParent);

        player = UnityEngine.Object.Instantiate(prefab, inactiveParent.transform, false);
        player.name = objectName;
        player.transform.SetPositionAndRotation(position, Quaternion.identity);
        roots.Add(player);
        interaction = player.GetComponent(interactionType);
        playerInput = inputType != null ? player.GetComponent(inputType) : null;
        playerInputSystem = player.GetComponent(typeof(UnityEngine.InputSystem.PlayerInput));
        Assert.That(interaction, Is.Not.Null, "The production PlayerInteractionNew component must remain present.");
        Assert.That(playerInput, Is.Not.Null, "The production PlayerInputNew event component must remain present.");

        if (playerInput is Behaviour gameInputBehaviour) gameInputBehaviour.enabled = false;
        if (playerInputSystem is Behaviour inputSystemBehaviour) inputSystemBehaviour.enabled = false;
        foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == interaction || behaviour == playerInput || behaviour == playerInputSystem) continue;
            behaviour.enabled = false;
        }
        foreach (Collider collider in player.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (CharacterController controller in player.GetComponentsInChildren<CharacterController>(true)) controller.enabled = false;

        inactiveParent.SetActive(true);
        Assert.That(player.activeInHierarchy, Is.True);
        Assert.That(interaction, Is.SameAs(player.GetComponent(interactionType)));
        Assert.That(playerInput, Is.SameAs(player.GetComponent(inputType)));
        Assert.That(playerInput is Behaviour inputNewBehaviour && !inputNewBehaviour.enabled, Is.True,
            "PlayerInputNew remains present but disabled for the direct-carry test.");
        Assert.That(playerInputSystem is Behaviour inputSystemBehaviourAfterActivation && !inputSystemBehaviourAfterActivation.enabled,
            Is.True, "The Input System PlayerInput remains disabled for the direct-carry test.");
    }
#endif

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Cleanup();
        yield return null;
    }

    private void Cleanup()
    {
        // The real DropObject path may already have released the resource. Cleanup remains
        // fixture-local and idempotent for either passing or expected-red contact assertions.
        if (interaction != null && resource != null && resourceComponent != null)
        {
            object heldObject = interaction.GetType().GetMethod("GetPickedUpGameObject", PublicInstance)?.Invoke(interaction, null);
            if (heldObject == resource)
            {
                object dropped = interaction.GetType().GetMethod("DropObject", PublicInstance)?.Invoke(interaction, null);
                Assert.That(dropped, Is.EqualTo(true), "Cleanup must complete the real holder drop before destroying the fixture.");
            }
        }
        DisposePlayerInput();
        for (int i = roots.Count - 1; i >= 0; i--)
            if (roots[i] != null) UnityEngine.Object.Destroy(roots[i]);
        foreach (UnityEngine.Object asset in transientAssets)
            if (asset != null) UnityEngine.Object.Destroy(asset);
        if (player != null) UnityEngine.Object.Destroy(player);
        roots.Clear();
        transientAssets.Clear();
        player = null; resource = null; testBody = null; holdAnchor = null; rayOrigin = null;
        interaction = null; resourceComponent = null; heldBody = null; heldCollider = null;
        playerInputSystem = null;
    }

    private void DisposePlayerInput()
    {
        if (playerInput == null) return;
        MethodInfo disposeInput = playerInput.GetType().GetMethod("DisposeInput", PrivateInstance);
        disposeInput?.Invoke(playerInput, null);
        FieldInfo initializedField = playerInput.GetType().GetField("inputInitialized", PrivateInstance);
        if (initializedField != null)
            Assert.That((bool)initializedField.GetValue(playerInput), Is.False,
                "The offline production PlayerInputNew action maps must be disposed before fixture destruction.");
        if (playerInput is Behaviour behaviour) behaviour.enabled = false;
        playerInput = null;
    }
}

public sealed class SingleCarryStage2CollisionRecorder : MonoBehaviour
{
    public bool HadCollision { get; private set; }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision != null) HadCollision = true;
    }
}
