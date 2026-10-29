using System.Collections;
using System.Reflection;
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class LooseSoilStage3MovementPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject fieldRoot;
    private GameObject playerRoot;
    private GameObject inactiveParent;
    private GameObject floorRoot;
    private Keyboard keyboard;
    private Type fieldType;
    private CrowbarInputSettingsScope inputSettingsScope;
    private PlayModeTestScope scope;

    [UnitySetUp]
    public IEnumerator SetUpSyntheticKeyboardRouting()
    {
        scope = new PlayModeTestScope("LooseSoilStage3Movement");
        inputSettingsScope = new CrowbarInputSettingsScope();
        scope.RestoreWith(RemoveTestKeyboard);
        scope.RestoreWith(RestoreInputSettings);
        inputSettingsScope.UseFocusedGameViewRouting();
        yield break;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (scope == null) yield break;
        PlayModeTestScope cleanupScope = scope;
        yield return cleanupScope.Cleanup();
        fieldRoot = playerRoot = inactiveParent = floorRoot = null;
        keyboard = null; inputSettingsScope = null; fieldType = null;
        scope = null;
        cleanupScope.AssertCleanupSucceeded();
    }

    private void RemoveTestKeyboard()
    {
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        keyboard = null;
    }

    private void RestoreInputSettings()
    {
        try { inputSettingsScope?.Restore(); }
        finally { inputSettingsScope = null; }
    }

    [UnityTest]
    public IEnumerator ProductionControllerUsesSoilForRealJumpLaunchAndRestoresItAfterExit()
    {
#if !UNITY_EDITOR
        Assert.Ignore("The production player prefab is loaded through AssetDatabase in the Editor.");
        yield break;
#else
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        Assert.That(prefab, Is.Not.Null);
        fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        Assert.That(fieldType, Is.Not.Null);
        fieldRoot = scope.Own(new GameObject("Stage 3 jump field"));
        Component field = fieldRoot.AddComponent(fieldType);
        fieldType.GetMethod("Initialize").Invoke(field, new object[] { 12f, 12f, .5f, .4f, .4f, 6f });
        fieldType.GetMethod("SetSurfaceState").Invoke(field, new object[] { true, null });

        floorRoot = scope.Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
        floorRoot.name = "Stage 3 movement floor";
        floorRoot.transform.position = new Vector3(0f, -.1f, 0f);
        floorRoot.transform.localScale = new Vector3(100f, .2f, 100f);

        inactiveParent = scope.Own(new GameObject("Stage 3 inactive player parent"));
        inactiveParent.SetActive(false);
        playerRoot = scope.Own(Object.Instantiate(prefab, inactiveParent.transform, false));
        Behaviour gameInput = (Behaviour)playerRoot.GetComponent(Type.GetType("PlayerInputNew, Assembly-CSharp"));
        Behaviour inputSystemPlayer = playerRoot.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        Assert.That(gameInput, Is.Not.Null);
        Assert.That(inputSystemPlayer, Is.Not.Null);
        gameInput.enabled = false;
        inputSystemPlayer.enabled = false;
        keyboard = InputSystem.AddDevice<Keyboard>();
        Component playerInteraction = playerRoot.GetComponent(Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        Component playerInventory = playerRoot.GetComponent(Type.GetType("PlayerInventory, Assembly-CSharp"));
        Component controller = playerRoot.GetComponent(Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp"));
        foreach (MonoBehaviour behaviour in playerRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == controller || behaviour == gameInput) continue;
            behaviour.enabled = false;
        }
        playerRoot.transform.position = new Vector3(0f, .07f, 0f);
        inactiveParent.SetActive(true);
        yield return null;
        gameInput.enabled = true;
        yield return null;

        CharacterController character = playerRoot.GetComponent<CharacterController>();
        Assert.That(controller, Is.Not.Null);
        Assert.That(character, Is.Not.Null);
        Set(controller, "GroundLayers", (LayerMask)1);
        SetFoot(character, playerRoot.transform, field.transform.TransformPoint(new Vector3(7f, 0f, 0f)));
        Set(controller, "_horizontalVelocity", Vector3.zero);
        Set(controller, "_verticalVelocity", 0f);
        Physics.SyncTransforms();
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return null;
        yield return SettleMovement(.4f);
        float baseWalk = (float)Get(controller, "MoveSpeed");
        float baseSprint = (float)Get(controller, "SprintSpeed");
        float baseHeight = (float)Get(controller, "JumpHeight");
        float outsideWalkSpeed = (float)Get(controller, "HorizontalSpeed");
        string outsideDiagnostics = $"position={playerRoot.transform.position}, foot={CurrentFoot(character)}, horizontal={Get(controller, "HorizontalVelocity")}, vertical={Get(controller, "VerticalVelocity")}, dt={Time.deltaTime:F6}s";
        Assert.That(outsideWalkSpeed, Is.EqualTo(baseWalk).Within(.2f),
            "Actual movement outside the field retains the ordinary speed. " + outsideDiagnostics);

        floorRoot.transform.position = new Vector3(0f, .1f, 0f);
        SetFoot(character, playerRoot.transform, field.transform.TransformPoint(new Vector3(0f, .2f, 0f)));
        Physics.SyncTransforms();
        Set(controller, "_horizontalVelocity", Vector3.zero);
        yield return null;
        yield return SettleMovement(.4f);
        float shallowWalkSpeed = (float)Get(controller, "HorizontalSpeed");
        float shallowImmersion = GetImmersion(CurrentFoot(character));
        Assert.That(shallowImmersion, Is.EqualTo(.5f).Within(.08f),
            "The half-depth movement measurement remains at half immersion while the support stays elevated.");
        Assert.That(shallowWalkSpeed, Is.EqualTo(baseWalk * Mathf.Lerp(1f, .75f, shallowImmersion)).Within(.2f),
            "Half immersion slows actual frame-driven walk movement.");

        floorRoot.transform.position = new Vector3(0f, -.1f, 0f);
        SetFoot(character, playerRoot.transform, field.transform.TransformPoint(new Vector3(0f, 0f, 0f)));
        Physics.SyncTransforms();
        Set(controller, "_horizontalVelocity", Vector3.zero);
        yield return null;
        yield return SettleMovement(.4f);
        float fullWalkSpeed = (float)Get(controller, "HorizontalSpeed");
        float fullImmersion = GetImmersion(CurrentFoot(character));
        Assert.That(fullImmersion, Is.EqualTo(1f).Within(.08f));
        Assert.That(fullWalkSpeed, Is.EqualTo(baseWalk * Mathf.Lerp(1f, .75f, fullImmersion)).Within(.2f),
            "Full immersion slows actual walk movement further.");

        Set(controller, "_horizontalVelocity", Vector3.zero);
        SetFoot(character, playerRoot.transform, field.transform.TransformPoint(new Vector3(0f, 0f, 0f)));
        Physics.SyncTransforms();
        Set(controller, "_holdedItemMovementSpeedPenaltyMultiplier", 0f);
        Set(controller, "_inventoryItemMovementSpeedPenaltyMultiplier", 0f);
        Set(controller, "_isSprinting", true);
        Set(controller, "_currentStamina", 100f);
        Set(controller, "MaxStamina", 100f);
        yield return null;
        yield return SettleMovement(.4f);
        float fullSprintWithoutPenalty = (float)Get(controller, "HorizontalSpeed");
        Assert.That(fullSprintWithoutPenalty, Is.EqualTo(baseSprint * Mathf.Lerp(1f, .65f, GetImmersion(CurrentFoot(character)))).Within(.2f),
            "Actual unladen sprint uses the distinct full-immersion sprint multiplier.");

        SetFoot(character, playerRoot.transform, field.transform.TransformPoint(new Vector3(0f, 0f, 0f)));
        Physics.SyncTransforms();
        Set(controller, "_holdedItemMovementSpeedPenaltyMultiplier", .3f);
        Set(controller, "_inventoryItemMovementSpeedPenaltyMultiplier", .2f);
        Set(controller, "_horizontalVelocity", Vector3.zero);
        Set(controller, "_isSprinting", false);
        yield return null;
        yield return SettleMovement(.4f);
        float composedWalkSpeed = (float)Get(controller, "HorizontalSpeed");
        Assert.That(composedWalkSpeed, Is.EqualTo(baseWalk * .7f * .8f * Mathf.Lerp(1f, .75f, GetImmersion(CurrentFoot(character)))).Within(.2f),
            "Held and inventory penalties multiply with soil in the production walk path.");

        SetFoot(character, playerRoot.transform, field.transform.TransformPoint(new Vector3(0f, 0f, 0f)));
        Physics.SyncTransforms();
        Set(controller, "_isSprinting", true);
        Set(controller, "_horizontalVelocity", Vector3.zero);
        yield return null;
        yield return SettleMovement(.4f);
        float composedSprintSpeed = (float)Get(controller, "HorizontalSpeed");
        float expectedComposed = baseSprint * .7f * .8f * Mathf.Lerp(1f, .65f, GetImmersion(CurrentFoot(character)));
        Assert.That(composedSprintSpeed, Is.EqualTo(expectedComposed).Within(.2f), "Held and inventory penalties multiply with soil without replacing either penalty.");
        Assert.That((float)Get(controller, "_holdedItemMovementSpeedPenaltyMultiplier"), Is.EqualTo(.3f));
        Assert.That((float)Get(controller, "_inventoryItemMovementSpeedPenaltyMultiplier"), Is.EqualTo(.2f));
        Assert.That((float)Get(controller, "MoveSpeed"), Is.EqualTo(baseWalk));
        Assert.That((float)Get(controller, "SprintSpeed"), Is.EqualTo(baseSprint));
        Assert.That((float)Get(controller, "JumpHeight"), Is.EqualTo(baseHeight));

        AssertJumpHeight(controller, character, field.transform.TransformPoint(new Vector3(0f, 0f, 0f)), baseHeight, 1f);
        AssertJumpHeight(controller, character, field.transform.TransformPoint(new Vector3(0f, .2f, 0f)), baseHeight, .5f);
        AssertJumpHeight(controller, character, field.transform.TransformPoint(new Vector3(7f, 0f, 0f)), baseHeight, 0f);

        object heightField = Property(field, "HeightField");
        heightField.GetType().GetMethod("Remove").Invoke(heightField, new object[] { .25f, .25f, .01f, 6f });
        Invoke(field, "RebuildMesh");
        Vector3 cellFoot = field.transform.TransformPoint(new Vector3(0f, 0f, 0f));
        Assert.That(GetImmersion(cellFoot), Is.EqualTo(0f).Within(0.000001f), "Empty cells have no sample surface.");
        Invoke(field, "ResetToInitial");
        Assert.That(GetImmersion(cellFoot), Is.EqualTo(1f).Within(.001f), "Field reset restores its initial layer.");

        fieldType.GetMethod("SetSurfaceState").Invoke(field, new object[] { false, null });
        Assert.That(GetImmersion(field.transform.TransformPoint(new Vector3(0f, 0f, 0f))), Is.Zero);
        fieldType.GetMethod("SetSurfaceState").Invoke(field, new object[] { true, null });
        ((Behaviour)field).enabled = false;
        Assert.That(GetImmersion(field.transform.TransformPoint(new Vector3(0f, 0f, 0f))), Is.Zero);
        ((Behaviour)field).enabled = true;
        Assert.That(GetImmersion(field.transform.TransformPoint(new Vector3(0f, 0f, 0f))), Is.EqualTo(1f).Within(.001f));
        ((Behaviour)controller).enabled = false;
        Assert.That((float)Get(controller, "_looseSoilImmersion"), Is.Zero, "Disabling the player clears its sampled state.");
        ((Behaviour)controller).enabled = true;
        yield return null;
        UnityEngine.SceneManagement.Scene soilScene = scope.OwnScene(UnityEngine.SceneManagement.SceneManager.CreateScene("Stage3FieldUnloadFixture"));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fieldRoot, soilScene);
        AsyncOperation unload = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(soilScene);
        while (!unload.isDone) yield return null;
        fieldRoot = null;
        Assert.That(GetImmersion(playerRoot.transform.position), Is.Zero, "Unloaded fields leave the registry.");
        fieldRoot = scope.Own(new GameObject("Stage 3 destroyed registry field"));
        Component destroyedField = fieldRoot.AddComponent(fieldType);
        fieldType.GetMethod("Initialize").Invoke(destroyedField, new object[] { 4f, 4f, .5f, .4f, .4f, 6f });
        fieldType.GetMethod("SetSurfaceState").Invoke(destroyedField, new object[] { true, null });
        Assert.That(GetImmersion(Vector3.zero), Is.EqualTo(1f).Within(.001f));
        Object.Destroy(fieldRoot);
        fieldRoot = null;
        yield return null;
        Assert.That(GetImmersion(Vector3.zero), Is.Zero, "Destroyed fields leave the registry.");
        Object.Destroy(inactiveParent);
        inactiveParent = null;
        playerRoot = null;
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator OneRealJumpPressHasOneWeakenedFullFlightWithOrWithoutCarryPenalties()
    {
#if !UNITY_EDITOR
        Assert.Ignore("The production player prefab is loaded through AssetDatabase in the Editor.");
        yield break;
#else
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        Assert.That(prefab, Is.Not.Null);
        fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        Assert.That(fieldType, Is.Not.Null);
        fieldRoot = scope.Own(new GameObject("Stage 3 full-flight regression field"));
        Component field = fieldRoot.AddComponent(fieldType);
        fieldType.GetMethod("Initialize").Invoke(field, new object[] { 12f, 12f, .5f, .4f, .4f, 6f });
        fieldType.GetMethod("SetSurfaceState").Invoke(field, new object[] { true, null });

        floorRoot = scope.Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
        floorRoot.name = "Stage 3 full-flight support";
        floorRoot.transform.position = new Vector3(0f, -.1f, 0f);
        floorRoot.transform.localScale = new Vector3(100f, .2f, 100f);

        inactiveParent = scope.Own(new GameObject("Stage 3 full-flight inactive parent"));
        inactiveParent.SetActive(false);
        playerRoot = scope.Own(Object.Instantiate(prefab, inactiveParent.transform, false));
        Behaviour gameInput = (Behaviour)playerRoot.GetComponent(Type.GetType("PlayerInputNew, Assembly-CSharp"));
        Behaviour inputSystemPlayer = playerRoot.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        Assert.That(gameInput, Is.Not.Null);
        Assert.That(inputSystemPlayer, Is.Not.Null);
        gameInput.enabled = false;
        inputSystemPlayer.enabled = false;
        keyboard = InputSystem.AddDevice<Keyboard>();
        Component controller = playerRoot.GetComponent(Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp"));
        foreach (MonoBehaviour behaviour in playerRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == controller || behaviour == gameInput) continue;
            behaviour.enabled = false;
        }
        playerRoot.transform.position = new Vector3(0f, .07f, 0f);
        inactiveParent.SetActive(true);
        yield return null;
        gameInput.enabled = true;
        yield return null;

        CharacterController character = playerRoot.GetComponent<CharacterController>();
        Assert.That(controller, Is.Not.Null);
        Assert.That(character, Is.Not.Null);
        Set(controller, "GroundLayers", (LayerMask)1);
        Set(controller, "MaxStamina", 100f);
        Set(controller, "_currentStamina", 100f);
        Set(controller, "looseSoilJumpHeightAtFullImmersion", .1f);
        Assert.That((float)Get(controller, "looseSoilJumpHeightAtFullImmersion"), Is.EqualTo(.1f),
            "The runtime controller instance must use the reported 0.1 jump-height multiplier.");

        JumpTrajectoryResult noCarryPenalty = new JumpTrajectoryResult();
        yield return RecordFullFlight(controller, character, field.transform, noCarryPenalty, Vector3.zero, 0f, 0f);
        JumpTrajectoryResult withCarryPenalties = new JumpTrajectoryResult();
        yield return RecordFullFlight(controller, character, field.transform, withCarryPenalties, Vector3.zero, .3f, .2f);
        JumpTrajectoryResult outsideNoPenalty = new JumpTrajectoryResult();
        yield return RecordFullFlight(controller, character, field.transform, outsideNoPenalty, new Vector3(7f, 0f, 0f), 0f, 0f);
        JumpTrajectoryResult outsideWithPenalties = new JumpTrajectoryResult();
        yield return RecordFullFlight(controller, character, field.transform, outsideWithPenalties, new Vector3(7f, 0f, 0f), .3f, .2f);

        string measurements = $"inside/no-penalty[{Describe(noCarryPenalty)}]; inside/held+inventory[{Describe(withCarryPenalties)}]; outside/no-penalty[{Describe(outsideNoPenalty)}]; outside/held+inventory[{Describe(outsideWithPenalties)}]";
        TestContext.WriteLine(measurements);

        Assert.That(noCarryPenalty.ControllerImmersionBeforeMove, Is.GreaterThan(.9f), "The inside jump's pre-Move sample is fully immersed. " + measurements);
        Assert.That(noCarryPenalty.FieldQueryImmersionAtPress, Is.GreaterThan(.9f), "The inside jump's actual foot query is fully immersed. " + measurements);
        Assert.That(withCarryPenalties.ControllerImmersionBeforeMove, Is.GreaterThan(.9f), "The held-penalty pre-Move sample is fully immersed. " + measurements);
        Assert.That(withCarryPenalties.FieldQueryImmersionAtPress, Is.GreaterThan(.9f), "The held-penalty actual foot query is fully immersed. " + measurements);
        Assert.That(outsideNoPenalty.ControllerImmersionBeforeMove, Is.LessThan(.01f), "The outside control must not sample soil. " + measurements);
        Assert.That(outsideNoPenalty.FieldQueryImmersionAtPress, Is.LessThan(.01f), "The outside control foot must remain outside soil. " + measurements);
        Assert.That(noCarryPenalty.ConfiguredMultiplier, Is.EqualTo(.1f), measurements);
        Assert.That(withCarryPenalties.ConfiguredMultiplier, Is.EqualTo(.1f), measurements);
        Assert.That(outsideNoPenalty.ConfiguredMultiplier, Is.EqualTo(.1f), measurements);
        Assert.That(outsideWithPenalties.ConfiguredMultiplier, Is.EqualTo(.1f), measurements);
        Assert.That(noCarryPenalty.GroundedAtPress && withCarryPenalties.GroundedAtPress &&
                    outsideNoPenalty.GroundedAtPress && outsideWithPenalties.GroundedAtPress, Is.True,
            "Each run must start from settled CharacterController support. " + measurements);
        Assert.That(noCarryPenalty.Landed && withCarryPenalties.Landed && outsideNoPenalty.Landed && outsideWithPenalties.Landed, Is.True,
            "Each physical press must complete a bounded takeoff, apex, and landing trajectory. " + measurements);
        Assert.That(noCarryPenalty.LaunchCount, Is.EqualTo(1), "One physical press should produce one launch event. " + measurements);
        Assert.That(withCarryPenalties.LaunchCount, Is.EqualTo(1), "Held/inventory penalties must not repeat a single launch. " + measurements);
        Assert.That(outsideNoPenalty.LaunchCount, Is.EqualTo(1), "Outside soil, one physical press should produce one launch. " + measurements);
        Assert.That(outsideWithPenalties.LaunchCount, Is.EqualTo(1), "Outside soil with carry penalties, one press should produce one launch. " + measurements);
        Assert.That(noCarryPenalty.ApexDelta, Is.LessThan(outsideNoPenalty.ApexDelta * .5f),
            "Full immersion must lower the measured apex substantially relative to the outside flight. " + measurements);
        Assert.That(withCarryPenalties.ApexDelta, Is.LessThan(outsideWithPenalties.ApexDelta * .5f),
            "The soil jump effect must persist with held/inventory speed penalties. " + measurements);
#endif
    }

    private IEnumerator RecordFullFlight(Component controller, CharacterController character, Transform field,
        JumpTrajectoryResult result, Vector3 localFoot, float heldPenalty, float inventoryPenalty)
    {
        Set(controller, "_holdedItemMovementSpeedPenaltyMultiplier", heldPenalty);
        Set(controller, "_inventoryItemMovementSpeedPenaltyMultiplier", inventoryPenalty);
        Set(controller, "_horizontalVelocity", Vector3.zero);
        Set(controller, "_verticalVelocity", 0f);
        Set(controller, "_isJumpPerformed", false);
        Set(controller, "_jumpTimeoutDelta", 0f);
        SetFoot(character, controller.transform, field.TransformPoint(localFoot));
        Physics.SyncTransforms();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());

        float groundDeadline = Time.realtimeSinceStartup + 5f;
        float settledGameTime = 0f;
        while ((settledGameTime < .15f || !character.isGrounded || !(bool)Get(controller, "Grounded")) &&
               Time.realtimeSinceStartup < groundDeadline)
        {
            settledGameTime += Time.deltaTime;
            yield return null;
        }
        result.GroundedAtPress = character.isGrounded && (bool)Get(controller, "Grounded");
        result.ControllerImmersionBeforeMove = (float)Get(controller, "_looseSoilImmersion");
        result.FieldQueryImmersionAtPress = GetImmersion(CurrentFoot(character));
        result.ConfiguredMultiplier = (float)Get(controller, "looseSoilJumpHeightAtFullImmersion");

        EventHandler launched = (sender, args) => result.LaunchCount++;
        EventInfo jumpStarted = controller.GetType().GetEvent("OnJumpStarted");
        Assert.That(jumpStarted, Is.Not.Null);
        jumpStarted.AddEventHandler(controller, launched);
        try
        {
            float startY = CurrentFoot(character).y;
            float elapsed = 0f;
            float wallDeadline = Time.realtimeSinceStartup + 7f;
            result.ApexDelta = 0f;
            result.Landed = false;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());

            while (elapsed < 5f && Time.realtimeSinceStartup < wallDeadline)
            {
                yield return null;
                elapsed += Time.deltaTime;
                float currentY = CurrentFoot(character).y;
                result.ApexDelta = Mathf.Max(result.ApexDelta, currentY - startY);
                bool tookOff = result.ApexDelta > .01f;
                if (tookOff && elapsed > .08f && character.isGrounded && currentY <= startY + .04f)
                {
                    result.Landed = true;
                    break;
                }
            }
            result.Duration = elapsed;
            result.Landed = result.Landed || (result.ApexDelta > .01f && character.isGrounded &&
                                                CurrentFoot(character).y <= startY + .04f);
        }
        finally
        {
            jumpStarted.RemoveEventHandler(controller, launched);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }
    }

    private sealed class JumpTrajectoryResult
    {
        public int LaunchCount;
        public float ControllerImmersionBeforeMove;
        public float FieldQueryImmersionAtPress;
        public float ConfiguredMultiplier;
        public float ApexDelta;
        public float Duration;
        public bool Landed;
        public bool GroundedAtPress;
    }

    private static string Describe(JumpTrajectoryResult result) =>
        $"sample={result.ControllerImmersionBeforeMove:F3}, footQuery={result.FieldQueryImmersionAtPress:F3}, multiplier={result.ConfiguredMultiplier:F3}, launches={result.LaunchCount}, apex={result.ApexDelta:F3}m, grounded={result.GroundedAtPress}, landed={result.Landed}, duration={result.Duration:F3}s";

    private static object Get(Component component, string name)
    {
        PropertyInfo property = component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null) return property.GetValue(component);
        return component.GetType().GetField(name, PrivateInstance | BindingFlags.Public).GetValue(component);
    }

    private static void Set(Component component, string name, object value) =>
        component.GetType().GetField(name, PrivateInstance | BindingFlags.Public).SetValue(component, value);

    private float GetImmersion(Vector3 foot) =>
        (float)fieldType.GetMethod("GetMaximumImmersion", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { foot });

    private static object Property(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);

    private static void SetFoot(CharacterController character, Transform root, Vector3 targetFoot)
    {
        bool wasEnabled = character.enabled;
        character.enabled = false;
        Vector3 currentFoot = CurrentFoot(character);
        root.position += targetFoot - currentFoot;
        Physics.SyncTransforms();
        character.enabled = wasEnabled;
        Physics.SyncTransforms();

        Component controller = root.GetComponent(Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp"));
        if (controller != null)
        {
            Set(controller, "_horizontalVelocity", Vector3.zero);
            Set(controller, "_verticalVelocity", 0f);
        }
    }

    private static Vector3 CurrentFoot(CharacterController character) =>
        character.transform.TransformPoint(character.center + Vector3.down * (character.height * .5f));

    private static IEnumerator SettleMovement(float seconds)
    {
        float elapsed = 0f;
        float deadline = Time.realtimeSinceStartup + 5f;
        while (elapsed < seconds && Time.realtimeSinceStartup < deadline)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        Assert.That(elapsed, Is.GreaterThanOrEqualTo(seconds), "Movement fixture must settle within its bounded game-time wait.");
    }

    private static void AssertJumpHeight(Component controller, CharacterController character, Vector3 foot,
        float baseHeight, float expectedImmersion)
    {
        SetFoot(character, controller.transform, foot);
        Invoke(controller, "RefreshLooseSoilImmersion");
        Set(controller, "_verticalVelocity", 0f);
        Set(controller, "_isJumpPerformed", true);
        Set(controller, "_jumpTimeoutDelta", 0f);
        Set(controller, "Grounded", true);
        float gravity = (float)Get(controller, "Gravity");
        float deltaTime = Time.deltaTime;
        Invoke(controller, "JumpAndGravity");
        float launchVelocity = (float)Get(controller, "VerticalVelocity") - gravity * deltaTime;
        float measuredHeight = launchVelocity * launchVelocity / (-2f * gravity);
        float expectedHeight = baseHeight * Mathf.Lerp(1f, .65f, expectedImmersion);
        Assert.That(measuredHeight, Is.EqualTo(expectedHeight).Within(.015f),
            "Reversing this frame's gravity step recovers the effective jump height from the production launch path.");
    }

    private static void Invoke(Component component, string name) =>
        component.GetType().GetMethod(name, PrivateInstance | BindingFlags.Public).Invoke(component, null);
}
