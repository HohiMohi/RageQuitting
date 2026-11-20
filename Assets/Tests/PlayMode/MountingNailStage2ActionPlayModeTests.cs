using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MountingNailStage2ActionPlayModeTests
{
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject nailObject;
    private GameObject target;
    private GameObject player;
    private GameObject cameraObject;
    private static GameObject sharedCameraObject;
    private GameObject uiObject;
    private readonly List<GameObject> blockers = new();
    private ScriptableObject profile;
    private ScriptableObject actionProfileFixture;
    private ScriptableObject hammerFixture;
    private Component nail;
    private Component nailResource;
    private Component targetResource;
    private Component actionController;
    private Component playerInteraction;
    private Component inventory;
    private System.Type nailType;
    private Camera aimCamera;
    private Vector3 originalGravity;
    private bool gravityCaptured;

    [UnityTest]
    public IEnumerator ActualOfflineActionPrioritizesNailAndUpdatesMountedHudWithoutDamage()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        Prepare();
        Collider boardCollider = target.GetComponentInChildren<Collider>();
        float boardDurability = ResourceFloat(targetResource, "GetCurrentResourceDurabilityNormalized");
        float nailDurability = ResourceFloat(nailResource, "GetCurrentResourceDurabilityNormalized");
        cameraObject.transform.position += Vector3.right * 0.05f;
        Ray interactionAimRay = default;
        object[] rayArguments = { default(Ray) };
        Assert.That((bool)playerInteraction.GetType().GetMethod("TryGetAimRay", new[] { typeof(Ray).MakeByRefType() }).Invoke(playerInteraction, rayArguments), Is.True);
        interactionAimRay = (Ray)rayArguments[0];
        object selectedItem = inventory.GetType().GetMethod("GetCurrentSelectedItem", PublicInstance).Invoke(inventory, null);
        Assert.That(selectedItem, Is.Not.Null);
        Assert.That(selectedItem.GetType().GetField("itemType").GetValue(selectedItem).ToString(), Is.EqualTo("IndustrialHammer"));
        Assert.That(TryResolveAim(interactionAimRay, 1.3f), Is.True, "The actual PlayerInteraction ray finds the nail within hammer range.");
        Assert.That(TryResolveAim(new Ray(cameraObject.transform.position, Vector3.forward)), Is.True,
            "A five-centimetre aim offset still resolves the exposed nail ahead of the board.");

        Assert.That(PerformAction(), Is.True, "The normal PlayerActionController action path consumes the aimed nail.");
        AssertState("Partial");
        Assert.That(NailInt("HitCount"), Is.EqualTo(1));
        Assert.That(ResourceFloat(targetResource, "GetCurrentResourceDurabilityNormalized"), Is.EqualTo(boardDurability));
        Assert.That(ResourceFloat(nailResource, "GetCurrentResourceDurabilityNormalized"), Is.EqualTo(nailDurability));
        Assert.That(ResourceBool(targetResource, "CanBeCarried"), Is.True);
        Assert.That(boardCollider.enabled, Is.True);

        AddHudAndAssertPartialPrompt();
        Assert.That(PerformAction(), Is.True);
        AssertState("Partial");
        Assert.That(NailInt("HitCount"), Is.EqualTo(2));
        Assert.That(PerformAction(), Is.True);
        AssertState("EmbeddedSingle");
        Assert.That(NailInt("HitCount"), Is.EqualTo(3));
        InvokeUiProgress(nailResource);
        UnityEngine.UI.Image progress = uiObject.GetComponent<UnityEngine.UI.Image>();
        Assert.That(progress.fillAmount, Is.EqualTo(1f).Within(0.001f));
        Assert.That(uiObject.activeSelf, Is.True);
        Assert.That(DescribeHudPrompts(), Is.EqualTo("Nail mounted"),
            "Completed nails retain an information prompt and never revert to the pickup prompt.");
        Assert.That(ResourceFloat(targetResource, "GetCurrentResourceDurabilityNormalized"), Is.EqualTo(boardDurability));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator PreparedNearAimSelectsNailAndHudShowsFilteredHammerPrompt()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        Prepare();
        cameraObject.transform.position += Vector3.right * 0.05f;
        Ray interactionAimRay = default;
        object[] rayArguments = { default(Ray) };
        Assert.That((bool)playerInteraction.GetType().GetMethod("TryGetAimRay", new[] { typeof(Ray).MakeByRefType() }).Invoke(playerInteraction, rayArguments), Is.True);
        interactionAimRay = (Ray)rayArguments[0];
        object selectedItem = inventory.GetType().GetMethod("GetCurrentSelectedItem", PublicInstance).Invoke(inventory, null);
        Assert.That(selectedItem, Is.Not.Null);
        Assert.That(selectedItem.GetType().GetField("itemType").GetValue(selectedItem).ToString(), Is.EqualTo("IndustrialHammer"));
        Assert.That(TryResolveAim(interactionAimRay, 1.3f), Is.True, "The actual PlayerInteraction ray finds the nail within hammer range.");
        playerInteraction.GetType().GetMethod("CheckLookAtInteractable", PrivateInstance).Invoke(playerInteraction, null);
        Assert.That(playerInteraction.GetType().GetProperty("CurrentTarget", PublicInstance).GetValue(playerInteraction), Is.SameAs(nailResource),
            "Near-aim assistance selects the visible nail resource instead of the board behind it.");

        uiObject = new GameObject("Stage 2 prepared nail HUD fixture");
        Component hud = uiObject.AddComponent(System.Type.GetType("LookingAtComponentUI, Assembly-CSharp"));
        hud.GetType().GetField("playerInteraction", PrivateInstance).SetValue(hud, playerInteraction);
        hud.GetType().GetField("playerActionController", PrivateInstance).SetValue(hud, actionController);
        System.Type textType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        Assert.That(textType, Is.Not.Null);
        GameObject promptText = new("Prepared nail prompt text", typeof(RectTransform), textType);
        promptText.transform.SetParent(uiObject.transform, false);
        hud.GetType().GetField("componentInfoText", PrivateInstance).SetValue(hud, promptText.GetComponent(textType));

        SetInventoryItem(UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/Shovel.asset"));
        hud.GetType().GetMethod("Update", PrivateInstance).Invoke(hud, null);
        Assert.That(GetPromptCount(hud, "Action"), Is.Zero, "The unavailable hammer action is filtered while ordinary pickup remains available.");
        Assert.That(GetPromptCount(hud, "Interact"), Is.EqualTo(1));
        Assert.That(promptText.GetComponent(textType).GetType().GetProperty("text").GetValue(promptText.GetComponent(textType)).ToString(), Does.Contain("Equip Industrial Hammer"));

        SetInventoryItem(UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/IndustrialHammer.asset"));
        hud.GetType().GetMethod("Update", PrivateInstance).Invoke(hud, null);
        Assert.That((bool)hud.GetType().GetProperty("CurrentTargetHasActionablePrompt", PublicInstance).GetValue(hud), Is.True);
        Assert.That(promptText.GetComponent(textType).GetType().GetProperty("text").GetValue(promptText.GetComponent(textType)).ToString(), Does.Contain("Hammer nail"));
        AssertState("Prepared");
        yield return null;
#endif
    }
    [UnityTest]
    public IEnumerator WrongToolConsumesNailTargetButCannotApplyHitOrDamage()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        Prepare();
        ScriptableObject shovel = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/Shovel.asset");
        SetInventoryItem(shovel);
        float durability = ResourceFloat(targetResource, "GetCurrentResourceDurabilityNormalized");

        Assert.That(PerformAction(), Is.True, "The nail target takes priority over the board even with a wrong tool.");
        AssertState("Prepared");
        Assert.That(NailInt("HitCount"), Is.Zero);
        Assert.That(ResourceFloat(targetResource, "GetCurrentResourceDurabilityNormalized"), Is.EqualTo(durability));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator ImpactResolvedAfterPreparedDeadlineDoesNotAdvanceHitCount()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        Prepare();
        ScriptableObject hammer = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        actionController.GetType().GetMethod("StartProfiledAction", PrivateInstance)
            .Invoke(actionController, new object[] { hammer });
        nailType.GetField("preparedDeadline", PrivateInstance).SetValue(nail, Time.timeAsDouble + 0.4d);
        float remainingAtClick = (float)nailType.GetMethod("RemainingSeconds", PrivateInstance).Invoke(nail, null);
        Assert.That(remainingAtClick, Is.GreaterThan(0.35d), "The click begins while the nail is still Prepared.");
        AssertState("Prepared");
        Assert.That(NailInt("HitCount"), Is.Zero,
            "Clicking starts WindUp but does not apply a hit before the Strike impact.");
        float timeout = Time.realtimeSinceStartup + 2f;
        while ((bool)actionController.GetType().GetProperty("IsActionInProgress", PublicInstance).GetValue(actionController)
               && Time.realtimeSinceStartup < timeout)
            yield return null;
        Assert.That(Time.realtimeSinceStartup, Is.LessThan(timeout), "The hammer action reaches its resolved impact.");
        AssertState("Loose");
        Assert.That(NailInt("HitCount"), Is.Zero);
        Assert.That(ResourceFloat(targetResource, "GetCurrentResourceDurabilityNormalized"), Is.EqualTo(targetDurabilityAtSetup));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator LooseHammerImpactAddsConfiguredVelocityWithoutPreparingNail()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        SetProfileFloat("looseHammerHorizontalImpulseMetersPerSecond", 2.4f);
        SetProfileFloat("looseHammerVerticalImpulseMetersPerSecond", 0.45f);
        Rigidbody body = nailObject.GetComponent<Rigidbody>();
        body.linearDamping = 0f;
        body.linearVelocity = new Vector3(0.3f, 0f, -0.7f);
        body.angularVelocity = Vector3.zero;
        Vector3 attackerPosition = nailObject.transform.position - Vector3.right * 2f;
        MethodInfo impact = nailType.GetMethod("TryApplyHammerImpact", PublicInstance);
        object hammerType = System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer");
        Assert.That((bool)impact.Invoke(nail, new[] { hammerType, (object)attackerPosition, Vector3.forward }), Is.True);
        yield return new WaitForFixedUpdate();
        AssertState("Loose");
        Assert.That(NailInt("HitCount"), Is.Zero);
        Assert.That(body.linearVelocity.x, Is.EqualTo(2.7f).Within(0.03f));
        float expectedVerticalVelocity = 0.45f + Physics.gravity.y * Time.fixedDeltaTime;
        Assert.That(body.linearVelocity.y, Is.EqualTo(expectedVerticalVelocity).Within(0.03f));
        Assert.That(body.linearVelocity.z, Is.EqualTo(-0.7f).Within(0.03f));

        body.linearVelocity = Vector3.zero;
        nailType.GetMethod("SetHeldWithoutImpulse", PrivateInstance).Invoke(nail, null);
        Assert.That((bool)impact.Invoke(nail, new[] { hammerType, (object)attackerPosition, Vector3.forward }), Is.False,
            "Held nails remain unaffected by hammer impacts.");
        Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
        nailType.GetMethod("SetLooseWithoutImpulse", PrivateInstance).Invoke(nail, null);
        nailResource.GetType().GetMethod("SetSecuredForTransport", PrivateInstance)
            .Invoke(nailResource, new object[] { true });
        AssertState("Loose");
        Assert.That((bool)impact.Invoke(nail, new[] { hammerType, (object)attackerPosition, Vector3.forward }), Is.False,
            "Nails secured for transport remain unaffected by hammer impacts.");
        Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator PreparedDeadlineExpiredAtImpactBecomesLooseAndReceivesConfiguredKick()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        Prepare();
        SetProfileFloat("looseHammerHorizontalImpulseMetersPerSecond", 1.6f);
        SetProfileFloat("looseHammerVerticalImpulseMetersPerSecond", 0.35f);
        SetProfileFloat("exitLateralImpulseMetersPerSecond", 0f);
        nailType.GetField("preparedDeadline", PrivateInstance).SetValue(nail, Time.timeAsDouble - 0.01d);
        AssertState("Prepared");
        Assert.That(NailInt("HitCount"), Is.Zero);

        originalGravity = Physics.gravity;
        gravityCaptured = true;
        Physics.gravity = Vector3.zero;
        Rigidbody body = nailObject.GetComponent<Rigidbody>();
        body.linearDamping = 0f;
        MethodInfo impact = nailType.GetMethod("TryApplyHammerImpact", PublicInstance);
        object hammerType = System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer");
        Vector3 attackerPosition = nailObject.transform.position - Vector3.right;
        bool applied = (bool)impact.Invoke(nail, new[] { hammerType, (object)attackerPosition, Vector3.forward });
        Assert.That(applied, Is.True, "The actual impact accepts a just-expired Prepared nail after transitioning it to Loose.");
        AssertState("Loose");
        Assert.That(NailInt("HitCount"), Is.Zero, "Expiry does not consume a hit or embed the nail.");
        foreach (Collider targetCollider in target.GetComponentsInChildren<Collider>()) targetCollider.enabled = false;
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        Assert.That(body.linearVelocity.x, Is.EqualTo(1.6f).Within(0.03f));
        Assert.That(body.linearVelocity.y, Is.EqualTo(0.35f).Within(0.03f));
        Assert.That(body.linearVelocity.z, Is.EqualTo(0f).Within(0.03f));
        Assert.That(NailInt("HitCount"), Is.Zero);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator LooseNailOnlyReceivesHammerImpulseAtResolvedImpactPhase()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        Prepare();
        nailType.GetMethod("SetLooseWithoutImpulse", PrivateInstance).Invoke(nail, null);
        foreach (Collider targetCollider in target.GetComponentsInChildren<Collider>()) targetCollider.enabled = false;
        Physics.SyncTransforms();
        SetProfileFloat("looseHammerHorizontalImpulseMetersPerSecond", 2.4f);
        SetProfileFloat("looseHammerVerticalImpulseMetersPerSecond", 0.45f);
        originalGravity = Physics.gravity;
        gravityCaptured = true;
        Physics.gravity = Vector3.zero;
        Rigidbody body = nailObject.GetComponent<Rigidbody>();
        body.linearDamping = 0f;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        ScriptableObject hammer = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        hammerFixture = Object.Instantiate(hammer);
        FieldInfo actionProfileField = hammerFixture.GetType().GetField("actionProfile");
        actionProfileFixture = Object.Instantiate((ScriptableObject)actionProfileField.GetValue(hammerFixture));
        actionProfileFixture.GetType().GetField("windUpDuration").SetValue(actionProfileFixture, 0.02f);
        actionProfileFixture.GetType().GetField("strikeDuration").SetValue(actionProfileFixture, 0.02f);
        actionProfileField.SetValue(hammerFixture, actionProfileFixture);
        SetInventoryItem(hammerFixture);
        actionController.GetType().GetMethod("StartProfiledAction", PrivateInstance)
            .Invoke(actionController, new object[] { hammerFixture });
        Assert.That(body.linearVelocity.sqrMagnitude, Is.LessThan(0.000001f),
            "Starting the action does not move the loose nail before Strike resolves.");

        string phase = "";
        float timeout = Time.realtimeSinceStartup + 3f;
        while (phase != "ImpactFreeze" && Time.realtimeSinceStartup < timeout)
        {
            phase = actionController.GetType().GetProperty("CurrentActionPhase", PublicInstance).GetValue(actionController).ToString();
            if (phase == "WindUp" || phase == "Strike")
                Assert.That(body.linearVelocity.sqrMagnitude, Is.LessThan(0.000001f),
                    $"No loose-hit impulse is applied during {phase} before impact resolution.");
            yield return null;
        }
        Assert.That(phase, Is.EqualTo("ImpactFreeze"), "The production action reaches its actual impact phase.");
        yield return new WaitForFixedUpdate();
        AssertState("Loose");
        Assert.That(NailInt("HitCount"), Is.Zero);
        Vector3 expectedAway = nailObject.transform.position - player.transform.position;
        expectedAway.y = 0f;
        expectedAway.Normalize();
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        Assert.That(horizontalVelocity.magnitude, Is.EqualTo(2.4f).Within(0.15f),
            "The impact applies one configured horizontal kick without doubling it.");
        Assert.That(Vector3.Dot(horizontalVelocity.normalized, expectedAway), Is.GreaterThan(0.98f),
            "The horizontal kick points away from the authoritative player.");
        Assert.That(body.linearVelocity.y, Is.GreaterThan(0.2f), "The profile's upward velocity change is applied at impact.");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator AimRadiusIsConfigurableAndLineOfSightFailsClosedOnBlockers()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        Prepare();
        SetProfileFloat("aimRadiusMeters", 0.12f);
        Physics.SyncTransforms();
        Vector3 nailMidpoint = (NailTransform("Head").position + NailTransform("Tip").position) * 0.5f;
        Ray widerRadiusRay = new(new Vector3(nailMidpoint.x + 0.1f, nailMidpoint.y, aimCamera.transform.position.z),
            Vector3.forward);
        Assert.That(TryResolveAim(widerRadiusRay), Is.True, "The broad phase follows the largest configured active profile radius.");

        Ray outsideRadiusRay = new(new Vector3(nailMidpoint.x + 0.13f, nailMidpoint.y, aimCamera.transform.position.z),
            Vector3.forward);
        Assert.That(TryResolveAim(outsideRadiusRay), Is.False);

        Ray blockerRay = new(aimCamera.transform.position, Vector3.forward);
        Vector3 blockerAimPoint = ResolveAimPoint(blockerRay);
        float blockerDistance = Vector3.Distance(blockerRay.origin, blockerAimPoint);
        GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blockers.Add(blocker);
        blocker.name = "Stage 2 nail aim blocker";
        blocker.transform.position = blockerRay.origin + blockerRay.direction * (blockerDistance * 0.5f);
        blocker.transform.localScale = new Vector3(0.1f, 0.1f, 0.03f);
        Physics.SyncTransforms();
        Assert.That(Physics.Raycast(blockerRay, out RaycastHit blockerControlHit, blockerDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True,
            "The control ray must physically intersect the blocker placed before the resolved exposed aim point.");
        Assert.That(blockerControlHit.collider.gameObject, Is.SameAs(blocker));
        Assert.That(TryResolveAim(blockerRay), Is.False,
            "The server-compatible visibility query rejects a real blocker before the exposed segment.");
        blocker.SetActive(false);

        Ray saturatedRay = new(aimCamera.transform.position, Vector3.forward);
        Assert.That(TryResolveAim(saturatedRay), Is.True,
            "The control ray resolves the candidate before the visibility buffer is saturated.");

        Vector3 aimPoint = ResolveAimPoint(saturatedRay);
        float visibilityDistance = Vector3.Distance(saturatedRay.origin, aimPoint);
        float triggerSpacing = visibilityDistance / 66f;
        for (int i = 0; i < 65; i++)
        {
            GameObject trigger = new GameObject($"Stage 2 saturation trigger {i}");
            trigger.transform.position = saturatedRay.origin + saturatedRay.direction * triggerSpacing * (i + 1);
            SphereCollider triggerCollider = trigger.AddComponent<SphereCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.radius = triggerSpacing * 0.1f;
            blockers.Add(trigger);
        }
        Physics.SyncTransforms();
        RaycastHit[] saturatedControlHits = new RaycastHit[64];
        int saturatedControlCount = Physics.RaycastNonAlloc(saturatedRay, saturatedControlHits, visibilityDistance + 0.002f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        Assert.That(saturatedControlCount, Is.EqualTo(saturatedControlHits.Length),
            "The controlled ray query must actually saturate the production visibility buffer.");
        Assert.That(TryResolveAim(saturatedRay), Is.False,
            "A full fixed visibility buffer fails closed instead of trusting an arbitrary unsorted subset.");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator AimRejectsBuriedOnlySegmentAndCandidatesOutsideActionRange()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        Prepare();
        Assert.That(ApplyHammer(), Is.True);
        AssertState("Partial");
        Transform tip = NailTransform("Tip");
        Vector3 axis = tip.forward.normalized;
        Vector3 entryPoint = tip.position - axis * NailFloat("PenetrationDepthMeters");
        Vector3 buriedPoint = entryPoint + axis * 0.04f;
        Assert.That(TryResolveAim(new Ray(buriedPoint, tip.forward)), Is.False,
            "A ray starting four centimetres behind the exposed endpoint cannot snap back to the hidden segment.");
        Assert.That(TryResolveAim(new Ray(entryPoint + axis * 0.08f, tip.forward)), Is.False,
            "A ray starting farther inside the target also cannot aim backward through the nail.");
        Assert.That(TryResolveAim(new Ray(cameraObject.transform.position, Vector3.forward), 0.5f), Is.False,
            "The candidate is rejected outside the equipped action range.");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator OriginInsideTargetMaterialCannotSeeExposedNail()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        BoxCollider targetBox = target.GetComponentInChildren<BoxCollider>();
        Assert.That(targetBox, Is.Not.Null, "The test uses a solid box collider as its target surface.");
        Vector3 originalBoxSize = targetBox.size;
        targetBox.size = new Vector3(originalBoxSize.x, originalBoxSize.y, 0.2f);
        Prepare();
        Assert.That(ApplyHammer(), Is.True);
        AssertState("Partial");
        Transform tip = NailTransform("Tip");
        Vector3 axis = tip.forward.normalized;
        Vector3 entryPoint = tip.position - axis * NailFloat("PenetrationDepthMeters");
        Ray fromInsideTargetTowardHead = new(entryPoint + axis * 0.04f, -axis);
        Physics.SyncTransforms();
        Vector3 containedPoint = targetBox.ClosestPoint(fromInsideTargetTowardHead.origin);
        Assert.That(Vector3.Distance(containedPoint, fromInsideTargetTowardHead.origin), Is.LessThan(0.0001f),
            "The regression origin is verified inside the target collider, four centimetres past the entry point.");
        Assert.That(TryResolveAim(fromInsideTargetTowardHead), Is.False,
            "A ray starting inside the target material cannot pass through it merely because Raycast skips the containing collider.");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator ExpiredPreparedNailReturnsLoosePickupPrompt()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        Prepare();
        nailType.GetField("preparedDeadline", PrivateInstance).SetValue(nail, Time.timeAsDouble - 0.01d);
        nailType.GetMethod("FixedUpdate", PrivateInstance).Invoke(nail, null);
        AssertState("Loose");
        uiObject = new GameObject("Loose nail HUD regression fixture");
        Component hud = uiObject.AddComponent(System.Type.GetType("LookingAtComponentUI, Assembly-CSharp"));
        hud.GetType().GetField("playerInteraction", PrivateInstance).SetValue(hud, playerInteraction);
        hud.GetType().GetField("playerActionController", PrivateInstance).SetValue(hud, actionController);
        playerInteraction.GetType().GetMethod("SetCurrentTarget", PrivateInstance).Invoke(playerInteraction, new[] { nailResource });
        System.Type textType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        GameObject promptText = new("Loose nail prompt text", typeof(RectTransform), textType);
        promptText.transform.SetParent(uiObject.transform, false);
        hud.GetType().GetField("componentInfoText", PrivateInstance).SetValue(hud, promptText.GetComponent(textType));
        hud.GetType().GetMethod("Update", PrivateInstance).Invoke(hud, null);
        Assert.That(promptText.GetComponent(textType).GetType().GetProperty("text")
                .GetValue(promptText.GetComponent(textType)).ToString(), Does.Contain("Pick up"),
            "Expiry restores the ordinary resource pickup prompt for a Loose nail.");
        Assert.That((bool)hud.GetType().GetProperty("CurrentTargetHasActionablePrompt", PublicInstance).GetValue(hud), Is.True);
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator PartiallyEmbeddedNailFollowsTargetWhenThePlayerCarriesIt()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        Prepare();
        Assert.That(ApplyHammer(), Is.True);
        Transform tip = NailTransform("Tip");
        Vector3 tipInTarget = target.transform.InverseTransformPoint(tip.position);
        Quaternion nailRotationInTarget = Quaternion.Inverse(target.transform.rotation) * nailObject.transform.rotation;

        targetResource.GetType().GetMethod("PickedUp", PublicInstance).Invoke(targetResource, new object[] { player.transform });
        Assert.That(ResourceBool(targetResource, "IsPickedUp"), Is.True,
            "A partially embedded board remains available through the ordinary pickup path.");
        Assert.That(ResourceBool(targetResource, "CanBeCarried"), Is.True);
        Vector3 targetPositionBeforeCarryMove = target.transform.position;
        player.transform.position += new Vector3(3f, 0f, 0f);
        playerInteraction.GetType().GetMethod("MovePickedUpObjectToHoldPosition", PrivateInstance)
            .Invoke(playerInteraction, null);
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        yield return null;

        Assert.That(Vector3.Distance(target.transform.position, targetPositionBeforeCarryMove), Is.GreaterThan(0.5f),
            "The normal carry follow path moves the partially embedded board.");
        Assert.That(Vector3.Distance(tip.position, target.transform.TransformPoint(tipInTarget)), Is.LessThan(0.005f));
        Assert.That(Quaternion.Angle(nailObject.transform.rotation, target.transform.rotation * nailRotationInTarget), Is.LessThan(0.1f));
#endif
    }

    [UnityTest]
    public IEnumerator PreparedNailOnPlayerHealthTargetDoesNotDamagePlayer()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        ReplaceTargetWithCharacter("PlayerHealth, Assembly-CSharp");
        yield return null;
        Prepare();
        Component health = target.GetComponent(System.Type.GetType("PlayerHealth, Assembly-CSharp"));
        float before = (float)health.GetType().GetProperty("CurrentHealth", PublicInstance).GetValue(health);
        Assert.That(TryResolveAim(new Ray(cameraObject.transform.position, Vector3.forward), 1.3f), Is.True,
            "The hammer's normal aim resolver selects the nail placed on the PlayerHealth collider.");
        Assert.That(PerformAction(), Is.True, "The normal hammer action consumes the prepared nail on the player's collider.");
        AssertState("Partial");
        Assert.That((float)health.GetType().GetProperty("CurrentHealth", PublicInstance).GetValue(health), Is.EqualTo(before));
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator PreparedNailOnNpcHealthTargetDoesNotDamageNpc()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the project's authored Unity prefab assets.");
        yield break;
#else
        Setup();
        ReplaceTargetWithCharacter("NPCHealth, Assembly-CSharp");
        yield return null;
        Prepare();
        Component health = target.GetComponent(System.Type.GetType("NPCHealth, Assembly-CSharp"));
        float before = (float)health.GetType().GetProperty("CurrentHealth", PublicInstance).GetValue(health);
        Assert.That(TryResolveAim(new Ray(cameraObject.transform.position, Vector3.forward), 1.3f), Is.True,
            "The hammer's normal aim resolver selects the nail placed on the separate NPCHealth collider.");
        Assert.That(PerformAction(), Is.True, "The normal hammer action consumes the prepared nail on the NPC's collider.");
        AssertState("Partial");
        Assert.That((float)health.GetType().GetProperty("CurrentHealth", PublicInstance).GetValue(health), Is.EqualTo(before));
        yield return null;
#endif
    }
    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (uiObject != null) Object.Destroy(uiObject);
        foreach (GameObject blocker in blockers)
            if (blocker != null) Object.Destroy(blocker);
        blockers.Clear();
        if (nailObject != null) Object.Destroy(nailObject);
        if (target != null) Object.Destroy(target);
        if (player != null)
        {
            foreach (Behaviour behaviour in player.GetComponentsInChildren<Behaviour>(true))
                if (behaviour.GetType().Name == "LookAtCamera") behaviour.enabled = false;
            Object.Destroy(player);
        }
        yield return null;
        if (cameraObject != null) Object.Destroy(cameraObject);
        if (profile != null) Object.Destroy(profile);
        if (actionProfileFixture != null) Object.Destroy(actionProfileFixture);
        if (hammerFixture != null) Object.Destroy(hammerFixture);
        if (gravityCaptured)
        {
            Physics.gravity = originalGravity;
            gravityCaptured = false;
        }
        yield return null;
    }

#if UNITY_EDITOR
    private float targetDurabilityAtSetup;

    private void Setup()
    {
        GameObject nailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab");
        Assert.That(nailPrefab, Is.Not.Null);
        nailType = System.Type.GetType("MountingNail, Assembly-CSharp");
        Component prefabNail = nailPrefab.GetComponent(nailType);
        profile = Object.Instantiate((ScriptableObject)nailType.GetProperty("Profile", PublicInstance).GetValue(prefabNail));
        SetProfileFloat("aimRadiusMeters", 0.06f);
        profile.GetType().GetField("requiredHitsForSingleObject", PrivateInstance).SetValue(profile, 3);

        nailObject = Object.Instantiate(nailPrefab);
        nail = nailObject.GetComponent(nailType);
        nailType.GetField("profile", PrivateInstance).SetValue(nail, profile);
        nailType.GetMethod("ApplyProfileConfiguration", PrivateInstance).Invoke(nail, null);
        nailResource = nailObject.GetComponent(System.Type.GetType("BaseResourceNew, Assembly-CSharp"));

        GameObject boardPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Resources/WoodenBoardResource_prefab.prefab");
        Assert.That(boardPrefab, Is.Not.Null);
        target = Object.Instantiate(boardPrefab);
        target.transform.position = new Vector3(10f, 15f, 0f);
        targetResource = target.GetComponent(System.Type.GetType("BaseResourceNew, Assembly-CSharp"));
        Assert.That(targetResource, Is.Not.Null);

        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        Assert.That(playerPrefab, Is.Not.Null);
        player = Object.Instantiate(playerPrefab, new Vector3(-20f, 15f, -20f), Quaternion.identity);
        foreach (Collider playerCollider in player.GetComponentsInChildren<Collider>(true)) playerCollider.enabled = false;
        actionController = player.GetComponent(System.Type.GetType("PlayerActionController, Assembly-CSharp"));
        playerInteraction = player.GetComponent(System.Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        inventory = player.GetComponent(System.Type.GetType("PlayerInventory, Assembly-CSharp"));
        Assert.That(actionController, Is.Not.Null);
        Assert.That(playerInteraction, Is.Not.Null);
        Assert.That(inventory, Is.Not.Null);
        foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != actionController && behaviour != playerInteraction && behaviour != inventory
                && behaviour.GetType().Name != "PlayerHealth" && behaviour.GetType().Name != "PlayerInputNew")
                behaviour.enabled = false;
        }

        if (sharedCameraObject == null)
        {
            sharedCameraObject = new GameObject("Stage 2 action aim camera");
            sharedCameraObject.tag = "MainCamera";
            sharedCameraObject.AddComponent<Camera>();
        }
        cameraObject = sharedCameraObject;
        cameraObject.SetActive(true);
        aimCamera = cameraObject.GetComponent<Camera>();
        SetAimCamera(aimCamera);
        SetInventoryItem(UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
            "Assets/ScriptableObjectAssets/New/IndustrialHammer.asset"));
        targetDurabilityAtSetup = ResourceFloat(targetResource, "GetCurrentResourceDurabilityNormalized");
        Physics.SyncTransforms();
    }

    private void ReplaceTargetWithCharacter(string assemblyQualifiedType)
    {
        Object.Destroy(target);
        target = new GameObject("Stage 2 nail character target");
        target.transform.position = new Vector3(10f, 15f, 0f);
        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.size = new Vector3(1.2f, 1.2f, 0.15f);
        System.Type healthType = System.Type.GetType(assemblyQualifiedType);
        Assert.That(healthType, Is.Not.Null);
        target.AddComponent(healthType);
        targetResource = null;
        Physics.SyncTransforms();
    }
    private void Prepare()
    {
        Collider boardCollider = target.GetComponentInChildren<Collider>();
        Bounds bounds = boardCollider.bounds;
        Vector3 desiredTipPosition = new(bounds.center.x, bounds.center.y, bounds.min.z - 0.005f);
        Vector3 tipOffset = NailTransform("Tip").position - nailObject.transform.position;
        nailObject.transform.SetPositionAndRotation(desiredTipPosition - tipOffset, Quaternion.identity);
        cameraObject.transform.SetPositionAndRotation(
            new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 1f), Quaternion.identity);
        Physics.SyncTransforms();
        nailType.GetMethod("OnSingleCarryPlacementCommitted", PublicInstance).Invoke(nail, new object[] { null });
        AssertState("Prepared");
    }

    private bool PerformAction() => (bool)actionController.GetType().GetMethod("PerformAction", PublicInstance).Invoke(actionController, null);

    private void SetInventoryItem(ScriptableObject item)
    {
        Assert.That(item, Is.Not.Null);
        FieldInfo itemsField = inventory.GetType().GetField("inventoryItems", PrivateInstance);
        System.Array items = (System.Array)itemsField.GetValue(inventory);
        if (items == null || items.Length == 0)
        {
            items = System.Array.CreateInstance(itemsField.FieldType.GetElementType(), 2);
        }
        items.SetValue(item, 0);
        itemsField.SetValue(inventory, items);
        inventory.GetType().GetField("_currentInventoryOccupiedSlots", PrivateInstance).SetValue(inventory, 1);
        inventory.GetType().GetField("_selectedItemIndex", PrivateInstance).SetValue(inventory, 0);
    }

    private void SetAimCamera(Camera camera) => playerInteraction.GetType()
        .GetField("aimCamera", PrivateInstance).SetValue(playerInteraction, camera);

    private void AddHudAndAssertPartialPrompt()
    {
        uiObject = new GameObject("Stage 2 nail HUD fixture");
        UnityEngine.UI.Image image = uiObject.AddComponent<UnityEngine.UI.Image>();
        image.type = UnityEngine.UI.Image.Type.Filled;
        Component hud = uiObject.AddComponent(System.Type.GetType("LookingAtComponentUI, Assembly-CSharp"));
        hud.GetType().GetField("playerInteraction", PrivateInstance).SetValue(hud, playerInteraction);
        hud.GetType().GetField("playerActionController", PrivateInstance).SetValue(hud, actionController);
        hud.GetType().GetField("assemblingProgressBar", PrivateInstance).SetValue(hud, uiObject.GetComponent<UnityEngine.UI.Image>());
        System.Type textType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        Assert.That(textType, Is.Not.Null);
        GameObject promptText = new("Nail prompt text", typeof(RectTransform), textType);
        promptText.transform.SetParent(uiObject.transform, false);
        hud.GetType().GetField("componentInfoText", PrivateInstance)
            .SetValue(hud, promptText.GetComponent(textType));
        GameObject progressHolder = new GameObject("Nail progress holder");
        progressHolder.transform.SetParent(uiObject.transform, false);
        hud.GetType().GetField("progressCircleHolder", PrivateInstance).SetValue(hud, progressHolder);
        hud.GetType().GetMethod("AddPromptsForTarget", PrivateInstance).Invoke(hud, new[] { nailResource });
        IList prompts = (IList)hud.GetType().GetField("prompts", PrivateInstance).GetValue(hud);
        Assert.That(prompts.Count, Is.EqualTo(1), "Embedded nail prompt replaces the ordinary pickup prompt.");
        object prompt = prompts[0];
        Assert.That(prompt.GetType().GetField("Description").GetValue(prompt).ToString(), Does.Contain("Hammer nail (1/3)"));
        InvokeUiProgress(nailResource);
        Assert.That(progressHolder.activeSelf, Is.True);
        Assert.That(uiObject.GetComponent<UnityEngine.UI.Image>().fillAmount, Is.EqualTo(1f / 3f).Within(0.01f));
    }

    private void InvokeUiProgress(Component targetComponent)
    {
        Component hud = uiObject.GetComponent(System.Type.GetType("LookingAtComponentUI, Assembly-CSharp"));
        GameObject holder = (GameObject)hud.GetType().GetField("progressCircleHolder", PrivateInstance).GetValue(hud);
        holder.SetActive(false);
        hud.GetType().GetMethod("UpdateProgressVisual", PrivateInstance).Invoke(hud, new object[] { targetComponent });
    }

    private int GetPromptCount(Component hud, string actionKind)
    {
        IList prompts = (IList)hud.GetType().GetField("prompts", PrivateInstance).GetValue(hud);
        int count = 0;
        foreach (object prompt in prompts)
            if (prompt.GetType().GetField("ActionKind").GetValue(prompt).ToString() == actionKind) count++;
        return count;
    }
    private string DescribeHudPrompts()
    {
        Component hud = uiObject.GetComponent(System.Type.GetType("LookingAtComponentUI, Assembly-CSharp"));
        IList prompts = (IList)hud.GetType().GetField("prompts", PrivateInstance).GetValue(hud);
        prompts.Clear();
        hud.GetType().GetMethod("AddPromptsForTarget", PrivateInstance).Invoke(hud, new[] { nailResource });
        Assert.That(prompts.Count, Is.EqualTo(1));
        return prompts[0].GetType().GetField("Description").GetValue(prompts[0]).ToString();
    }

    private Vector3 ResolveAimPoint(Ray ray, float range = 3f)
    {
        System.Type resolver = System.Type.GetType("MountingNailAimResolver, Assembly-CSharp");
        MethodInfo method = resolver.GetMethod("TryFindCandidate", BindingFlags.Public | BindingFlags.Static);
        object[] args = { ray, range, player.transform, null, null, Vector3.zero };
        Assert.That((bool)method.Invoke(null, args), Is.True);
        return (Vector3)args[5];
    }
    private bool TryResolveAim(Ray ray, float range = 3f)
    {
        System.Type resolver = System.Type.GetType("MountingNailAimResolver, Assembly-CSharp");
        MethodInfo method = resolver.GetMethod("TryFindCandidate", BindingFlags.Public | BindingFlags.Static);
        object[] args = { ray, range, player.transform, null, null, Vector3.zero };
        return (bool)method.Invoke(null, args);
    }

    private bool ApplyHammer() => (bool)nailType.GetMethod("TryApplyHammerHit", PublicInstance)
        .Invoke(nail, new[] { System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "IndustrialHammer") });

    private void SetProfileFloat(string field, float value) => profile.GetType()
        .GetField(field, PrivateInstance).SetValue(profile, value);
    private int NailInt(string name) => (int)nailType.GetProperty(name, PublicInstance).GetValue(nail);
    private float NailFloat(string name)
    {
        if (name.StartsWith("Profile."))
        {
            object sourceProfile = nailType.GetProperty("Profile", PublicInstance).GetValue(nail);
            return (float)sourceProfile.GetType().GetProperty(name.Substring("Profile.".Length), PublicInstance)
                .GetValue(sourceProfile);
        }
        return (float)nailType.GetProperty(name, PublicInstance).GetValue(nail);
    }
    private Transform NailTransform(string name) => (Transform)nailType.GetProperty(name, PublicInstance).GetValue(nail);
    private float ResourceFloat(Component component, string method) => (float)component.GetType()
        .GetMethod(method, PublicInstance).Invoke(component, null);
    private bool ResourceBool(Component component, string property) => (bool)component.GetType()
        .GetProperty(property, PublicInstance).GetValue(component);
    private void AssertState(string expected) => Assert.That(nailType.GetProperty("State", PublicInstance).GetValue(nail).ToString(), Is.EqualTo(expected));
#endif
}
