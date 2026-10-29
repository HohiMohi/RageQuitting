using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ExcavationRiverWallPlayModeTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private PlayModeTestScope scope;
    private CrowbarInputSettingsScope inputSettings;
    private Keyboard keyboard;
    private GameObject playerParent;
    private GameObject player;
    private Scene stage;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        scope = new PlayModeTestScope("ExcavationRiverWall");
        inputSettings = new CrowbarInputSettingsScope();
        scope.RestoreWith(() =>
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            keyboard = null;
        });
        scope.RestoreWith(() => inputSettings?.Restore());
        inputSettings.UseFocusedGameViewRouting();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (scope == null) yield break;
        PlayModeTestScope cleanup = scope;
        yield return cleanup.Cleanup();
        player = null;
        playerParent = null;
        stage = default;
        keyboard = null;
        inputSettings = null;
        scope = null;
        cleanup.AssertCleanupSucceeded();
    }

    [UnityTest]
    public IEnumerator ProductionPlayerMaintainsContactAgainstBothRiverWallsAtAllThreeDigDepths()
    {
#if !UNITY_EDITOR
        Assert.Ignore("PlayerNew is loaded from AssetDatabase in Editor PlayMode tests.");
        yield break;
#else
        const string scenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
        AsyncOperation load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
        Assert.That(load, Is.Not.Null);
        while (!load.isDone) yield return null;
        stage = SceneManager.GetSceneByPath(scenePath);
        scope.OwnScene(stage);
        Assert.That(stage.IsValid() && stage.isLoaded, Is.True, "The authored Stage02 scene is owned immediately after its additive load.");
        Assert.That(SceneManager.SetActiveScene(stage), Is.True);

        Type volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        Assert.That(volumeType, Is.Not.Null);
        Component[] volumes = Resources.FindObjectsOfTypeAll(volumeType).Cast<Component>()
            .Where(v => v != null && v.gameObject.scene == stage).OrderBy(v => v.transform.position.x).ToArray();
        Assert.That(volumes, Has.Length.EqualTo(2));
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        Assert.That(prefab, Is.Not.Null);
        Type controllerType = Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp");
        Type inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        Assert.That(controllerType, Is.Not.Null);
        Assert.That(inputType, Is.Not.Null);
        DisableAuthoredStagePlayerInput(stage, inputType);

        playerParent = scope.Own(new GameObject("River wall test player parent"));
        playerParent.SetActive(false);
        player = scope.Own(Object.Instantiate(prefab, playerParent.transform, false));
        Behaviour gameplayInput = (Behaviour)player.GetComponent(inputType);
        UnityEngine.InputSystem.PlayerInput inputComponent = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        Assert.That(gameplayInput, Is.Not.Null);
        gameplayInput.enabled = false;
        if (inputComponent != null) inputComponent.enabled = false;
        Component controller = player.GetComponent(controllerType);
        CharacterController character = player.GetComponent<CharacterController>();
        Assert.That(controller, Is.Not.Null);
        Assert.That(character, Is.Not.Null);
        foreach (MonoBehaviour behavior in player.GetComponentsInChildren<MonoBehaviour>(true))
            if (behavior != controller && behavior != gameplayInput) behavior.enabled = false;
        SetLayerRecursively(player, LayerMask.NameToLayer("Ignore Raycast"));
        Set(controller, "GroundLayers", (LayerMask)LayerMask.GetMask("Default"));
        keyboard = InputSystem.AddDevice<Keyboard>();
        playerParent.SetActive(true);
        yield return null;
        gameplayInput.enabled = true;
        yield return null;

        var testCases = new[]
        {
            new ContactCase("walk_into", Key.W, false, true),
            new ContactCase("sprint_into", Key.W, true, true),
            new ContactCase("diagonal_into", Key.W, false, true, Key.D),
            new ContactCase("diagonal_sprint_into", Key.W, true, true, Key.D),
            new ContactCase("walk_along", Key.W, false, false)
        };
        string metricsDirectory = Path.GetFullPath("Artifacts/ExcavationWalls_20261029");
        Directory.CreateDirectory(metricsDirectory);
        string metricsPath = Path.Combine(metricsDirectory, "wall-contact-metrics-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + ".jsonl");
        File.AppendAllText(metricsPath, "");
        TestContext.WriteLine("WALL_CONTACT_METRICS_FILE=" + metricsPath);
        var failures = new System.Collections.Generic.List<string>();

        for (int pitIndex = 0; pitIndex < volumes.Length; pitIndex++)
        {
            Component volume = volumes[pitIndex];
            Transform wall = volume.transform.Find("PitWallRiver");
            Assert.That(wall, Is.Not.Null, "Authored river wall is required for physical contact measurements.");
            BoxCollider wallCollider = wall.GetComponent<BoxCollider>();
            Assert.That(wallCollider, Is.Not.Null);

            Component site = GetProperty(volume, "ConstructionSite") as Component;
            Assert.That(site, Is.Not.Null);
            Type bridgeComponentType = Type.GetType("BridgeComponent, Assembly-CSharp");
            Type networkStateType = Type.GetType("BridgeComponentNetworkState, Assembly-CSharp");
            Type stageEnum = Type.GetType("BridgeConstructionStage, Assembly-CSharp");
            Type substageEnum = Type.GetType("FoundationDiggingSubstage, Assembly-CSharp");
            Component bridgeComponent = site.GetComponentInParent(bridgeComponentType);
            Assert.That(bridgeComponent, Is.Not.Null);
            Assert.That(networkStateType, Is.Not.Null);
            Assert.That(stageEnum, Is.Not.Null);
            Assert.That(substageEnum, Is.Not.Null);
            FieldInfo workflowField = site.GetType().GetField("workflow", InstanceMembers);
            Assert.That(workflowField, Is.Not.Null);
            Object workflow = workflowField.GetValue(site) as Object;
            Assert.That(workflow, Is.Not.Null);
            ((Behaviour)site).enabled = false; // This owned Stage02 site's updater must not overwrite our cycle state.
            BoxCollider support = (BoxCollider)new UnityEditor.SerializedObject(volume)
                .FindProperty("soilSupportCollider").objectReferenceValue;
            Assert.That(support, Is.Not.Null);
            float initialSupportTop = support.transform.TransformPoint(support.center + Vector3.up * (support.size.y * .5f)).y;
            site.GetType().GetMethod("ApplyInitialCompletedState", InstanceMembers).Invoke(site, null);
            int componentId = (int)GetProperty(bridgeComponent, "ComponentID");
            ApplyDiggingNetworkState(bridgeComponent, networkStateType, stageEnum, substageEnum, componentId, 0, 0);

            Type failureEnum = Type.GetType("FoundationConcreteFailureState, Assembly-CSharp");
            MethodInfo applyConcrete = volumeType.GetMethod("ApplyConcreteState");
            Assert.That(failureEnum, Is.Not.Null);
            Assert.That(applyConcrete, Is.Not.Null);
            float finalDepth = (float)GetProperty(workflow, "FinalExcavationDepth");
            int cycles = (int)GetProperty(workflow, "DiggingCycleCount");
            float layerDepth = finalDepth / cycles;
            int unitsPerCycle = (int)GetProperty(workflow, "SoilUnitsPerCycle");

            for (int cycle = 0; cycle < 3; cycle++)
            {
                ApplyDiggingNetworkState(bridgeComponent, networkStateType, stageEnum, substageEnum,
                    componentId, cycle, cycle * unitsPerCycle);
                applyConcrete.Invoke(volume, new object[]
                {
                    Enum.Parse(stageEnum, "Digging"), 0, (int)GetProperty(workflow, "RequiredConcreteLoads"),
                    0f, 30f, Enum.Parse(failureEnum, "None"), 0f, new Vector3(1f, 34f, 67f), 0f
                });
                Physics.SyncTransforms();
                Vector3 supportTop = support.transform.TransformPoint(support.center + Vector3.up * (support.size.y * .5f));
                Assert.That(Mathf.Abs(supportTop.y - (initialSupportTop - layerDepth * (cycle + 1))),
                    Is.LessThan(.08f), $"Actual support surface should descend one production layer per cycle (cycle {cycle + 1}).");

                foreach (ContactCase contactCase in testCases)
                {
                    bool alongWall = !contactCase.Approach;
                    float direction = volume.transform.position.x < 5f ? 1f : -1f;
                    float faceX = direction > 0f ? wallCollider.bounds.min.x : wallCollider.bounds.max.x;
                    Vector3 capsuleCenter = wallCollider.bounds.center;
                    Vector3 desiredFoot = new Vector3(
                        alongWall ? faceX - direction * (character.radius + .06f) : faceX - direction * (character.radius + .48f),
                        supportTop.y + .015f,
                        capsuleCenter.z);
                    Quaternion rotation = alongWall
                        ? Quaternion.identity
                        : Quaternion.Euler(0f, direction > 0f ? 90f : 270f, 0f);
                    PlaceFoot(character, controller, desiredFoot, rotation);
                    Set(controller, "_horizontalVelocity", Vector3.zero);
                    Set(controller, "_verticalVelocity", 0f);
                    Physics.SyncTransforms();
                    Vector3 rootAtPlacement = player.transform.position;
                    string[] placementOverlaps = DescribeOverlappingColliders(character);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    yield return null;
                    Vector3 rootBeforeInput = player.transform.position;
                    Key[] keys = contactCase.Sprint
                        ? new[] { contactCase.Forward, Key.LeftShift }.Concat(contactCase.Extra.HasValue ? new[] { contactCase.Extra.Value } : Array.Empty<Key>()).ToArray()
                        : new[] { contactCase.Forward }.Concat(contactCase.Extra.HasValue ? new[] { contactCase.Extra.Value } : Array.Empty<Key>()).ToArray();
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

                    Vector3 previous = player.transform.position;
                    float maxFrameDelta = 0f;
                    float maxFrameSpeed = 0f;
                    float maxBackwardDeltaAfterContact = 0f;
                    float maxBackwardSpeedAfterContact = 0f;
                    float closestContactClearance = float.PositiveInfinity;
                    float maxRetreatFromClosestContact = 0f;
                    bool contactReached = false;
                    int collisionFlagsSeen = 0;
                    bool crossedBelowWallTop = false;
                    bool wallPlaneCrossed = false;
                    bool wallPlaneCrossedAboveTop = false;
                    var pathSamples = new System.Collections.Generic.List<FrameSample>();
                    float elapsed = 0f;
                    while (elapsed < .65f)
                    {
                        yield return null;
                        elapsed += Time.deltaTime;
                        Vector3 current = player.transform.position;
                        CollisionFlags collisionFlags = character.collisionFlags;
                        collisionFlagsSeen |= (int)collisionFlags;
                        Vector3 delta = current - previous;
                        delta.y = 0f;
                        float frameDelta = delta.magnitude;
                        float frameSpeed = Time.deltaTime > 0f ? frameDelta / Time.deltaTime : 0f;
                        maxFrameDelta = Mathf.Max(maxFrameDelta, frameDelta);
                        maxFrameSpeed = Mathf.Max(maxFrameSpeed, frameSpeed);
                        Vector3 currentFoot = CurrentFoot(character);
                        float clearance = direction * (faceX - character.transform.TransformPoint(character.center).x) - character.radius * character.transform.lossyScale.x;
                        float centerPastWallPlane = direction * (character.transform.TransformPoint(character.center).x - faceX);
                        if (!wallPlaneCrossed && centerPastWallPlane > character.skinWidth)
                        {
                            wallPlaneCrossed = true;
                            wallPlaneCrossedAboveTop = currentFoot.y >= wallCollider.bounds.max.y - character.skinWidth * 2f;
                            crossedBelowWallTop = !wallPlaneCrossedAboveTop;
                        }
                        Vector2 moveInput = (Vector2)gameplayInput.GetType().GetMethod("GetMoveVectorValue", InstanceMembers).Invoke(gameplayInput, null);
                        pathSamples.Add(new FrameSample
                        {
                            elapsed = elapsed,
                            position = current,
                            footPosition = currentFoot,
                            delta = delta,
                            collisionFlags = (int)collisionFlags,
                            grounded = (bool)GetMember(controller, "Grounded"),
                            verticalVelocity = (float)GetProperty(controller, "VerticalVelocity"),
                            horizontalVelocity = (Vector3)GetProperty(controller, "HorizontalVelocity"),
                            bodyYaw = player.transform.eulerAngles.y,
                            moveInput = moveInput,
                            sprinting = (bool)GetProperty(controller, "IsSprinting"),
                            wPressed = keyboard.wKey.isPressed,
                            dPressed = keyboard.dKey.isPressed,
                            shiftPressed = keyboard.leftShiftKey.isPressed
                        });
                        if (clearance <= .16f) contactReached = true;
                        if (contactReached)
                        {
                            closestContactClearance = Mathf.Min(closestContactClearance, clearance);
                            maxRetreatFromClosestContact = Mathf.Max(maxRetreatFromClosestContact, clearance - closestContactClearance);
                        }
                        float reverse = -direction * delta.x;
                        if (contactReached && reverse > 0f)
                        {
                            maxBackwardDeltaAfterContact = Mathf.Max(maxBackwardDeltaAfterContact, reverse);
                            if (Time.deltaTime > 0f) maxBackwardSpeedAfterContact = Mathf.Max(maxBackwardSpeedAfterContact, reverse / Time.deltaTime);
                        }
                        previous = current;
                    }
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    Vector3 finalPosition = player.transform.position;
                    float finalClearance = direction * (faceX - character.transform.TransformPoint(character.center).x) - character.radius * character.transform.lossyScale.x;
                    float diagnosticsSpeed = (float)GetProperty(controller, "HorizontalSpeed");
                    float tangentialTravel = Mathf.Abs(finalPosition.z - desiredFoot.z);
                    var metricData = new WallContactMetric
                    {
                        pit = volume.name,
                        cycle = cycle + 1,
                        depthMeters = layerDepth * (cycle + 1),
                        movementMode = contactCase.Name,
                        pitIndex = pitIndex,
                        supportCenterX = support.bounds.center.x,
                        crossedBelowWallTop = crossedBelowWallTop,
                        steppedOverWallTop = finalClearance < -.08f && wallPlaneCrossedAboveTop,
                        wallPlaneCrossedAboveTop = wallPlaneCrossedAboveTop,
                        finalClearanceMeters = finalClearance,
                        contactReached = contactReached,
                        maxFrameDeltaMeters = maxFrameDelta,
                        maxFrameSpeedMetersPerSecond = maxFrameSpeed,
                        controllerHorizontalSpeed = diagnosticsSpeed,
                        maxBackwardDeltaAfterContactMeters = maxBackwardDeltaAfterContact,
                        maxBackwardSpeedAfterContactMetersPerSecond = maxBackwardSpeedAfterContact,
                        maxRetreatFromClosestContactMeters = maxRetreatFromClosestContact,
                        tangentialTravelMeters = tangentialTravel,
                        wallBoundsMin = wallCollider.bounds.min,
                        wallBoundsMax = wallCollider.bounds.max,
                        wallFaceX = faceX,
                        stepOffset = character.stepOffset,
                        skinWidth = character.skinWidth,
                        collisionFlagsSeen = collisionFlagsSeen,
                        rootAtPlacement = rootAtPlacement,
                        rootBeforeInput = rootBeforeInput,
                        placementOverlaps = placementOverlaps,
                        startFoot = desiredFoot,
                        finalPosition = finalPosition,
                        supportTop = supportTop,
                        path = pathSamples.ToArray()
                    };
                    string metric = JsonUtility.ToJson(metricData);
                    File.AppendAllText(metricsPath, metric + Environment.NewLine);
                    string caseSummary = $"pit={pitIndex} centerX={support.bounds.center.x:F2} cycle={cycle + 1} mode={contactCase.Name}";
                    TestContext.WriteLine($"{caseSummary} contact={contactReached} aboveTop={wallPlaneCrossedAboveTop} belowTop={crossedBelowWallTop} clearance={finalClearance:F3} speed={maxFrameSpeed:F2} retreat={maxRetreatFromClosestContact:F3} tangent={tangentialTravel:F2}");
                    if (!contactReached) failures.Add($"contact not reached ({caseSummary}; metrics={metricsPath})");
                    if (crossedBelowWallTop) failures.Add($"capsule crossed wall below its top ({caseSummary}; metrics={metricsPath})");
                    if (finalClearance > .18f) failures.Add($"capsule detached from wall without crossing above its top ({caseSummary}; clearance={finalClearance:F3}; metrics={metricsPath})");
                    if (maxFrameDelta >= .35f) failures.Add($"single-frame displacement exceeded limit ({caseSummary}; delta={maxFrameDelta:F3}; metrics={metricsPath})");
                    if (maxFrameSpeed >= 20f) failures.Add($"horizontal speed exceeded limit ({caseSummary}; speed={maxFrameSpeed:F2}; metrics={metricsPath})");
                    if (maxBackwardDeltaAfterContact >= .12f) failures.Add($"single-frame backward ejection exceeded limit ({caseSummary}; delta={maxBackwardDeltaAfterContact:F3}; metrics={metricsPath})");
                    if (maxBackwardSpeedAfterContact >= 8f) failures.Add($"backward ejection speed exceeded limit ({caseSummary}; speed={maxBackwardSpeedAfterContact:F2}; metrics={metricsPath})");
                    if (maxRetreatFromClosestContact > Mathf.Max(.1f, character.skinWidth * 2f))
                        failures.Add($"retreat from closest contact exceeded skin-width tolerance ({caseSummary}; retreat={maxRetreatFromClosestContact:F3}; metrics={metricsPath})");
                    if (alongWall)
                    {
                        if (Mathf.Abs(finalClearance) >= .18f) failures.Add($"along-wall movement lost side contact ({caseSummary}; clearance={finalClearance:F3}; metrics={metricsPath})");
                        if (tangentialTravel <= .3f) failures.Add($"along-wall movement did not travel tangentially ({caseSummary}; travel={tangentialTravel:F3}; metrics={metricsPath})");
                    }
                }
            }
        }
        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
#endif
    }

    private struct ContactCase
    {
        public readonly string Name;
        public readonly Key Forward;
        public readonly bool Sprint;
        public readonly bool Approach;
        public readonly Key? Extra;

        public ContactCase(string name, Key forward, bool sprint, bool approach, Key? extra = null)
        {
            Name = name;
            Forward = forward;
            Sprint = sprint;
            Approach = approach;
            Extra = extra;
        }
    }

    [Serializable]
    private sealed class WallContactMetric
    {
        public string pit;
        public int cycle;
        public float depthMeters;
        public string movementMode;
        public int pitIndex;
        public float supportCenterX;
        public bool crossedBelowWallTop;
        public bool steppedOverWallTop;
        public bool wallPlaneCrossedAboveTop;
        public float finalClearanceMeters;
        public bool contactReached;
        public float maxFrameDeltaMeters;
        public float maxFrameSpeedMetersPerSecond;
        public float controllerHorizontalSpeed;
        public float maxBackwardDeltaAfterContactMeters;
        public float maxBackwardSpeedAfterContactMetersPerSecond;
        public float maxRetreatFromClosestContactMeters;
        public float tangentialTravelMeters;
        public Vector3 wallBoundsMin;
        public Vector3 wallBoundsMax;
        public float wallFaceX;
        public float stepOffset;
        public float skinWidth;
        public int collisionFlagsSeen;
        public Vector3 rootAtPlacement;
        public Vector3 rootBeforeInput;
        public string[] placementOverlaps;
        public Vector3 startFoot;
        public Vector3 finalPosition;
        public Vector3 supportTop;
        public FrameSample[] path;
    }

    [Serializable]
    private struct FrameSample
    {
        public float elapsed;
        public Vector3 position;
        public Vector3 footPosition;
        public Vector3 delta;
        public int collisionFlags;
        public bool grounded;
        public float verticalVelocity;
        public Vector3 horizontalVelocity;
        public float bodyYaw;
        public Vector2 moveInput;
        public bool sprinting;
        public bool wPressed;
        public bool dPressed;
        public bool shiftPressed;
    }

    private static void PlaceFoot(CharacterController character, Component controller, Vector3 targetFoot, Quaternion rotation)
    {
        bool wasEnabled = character.enabled;
        character.enabled = false;
        character.transform.rotation = rotation;
        MethodInfo forceViewYaw = controller.GetType().GetMethod("SetForcedViewYaw", InstanceMembers);
        Assert.That(forceViewYaw, Is.Not.Null, "The production controller must expose its view-yaw setter for deterministic movement measurement.");
        forceViewYaw.Invoke(controller, new object[] { rotation.eulerAngles.y });
        Vector3 currentFoot = character.transform.TransformPoint(character.center + Vector3.down * (character.height * .5f));
        character.transform.position += targetFoot - currentFoot;
        Physics.SyncTransforms();
        character.enabled = wasEnabled;
        Physics.SyncTransforms();
    }

    private static Vector3 CurrentFoot(CharacterController character) =>
        character.transform.TransformPoint(character.center + Vector3.down * (character.height * .5f));

    private static void DisableAuthoredStagePlayerInput(Scene ownedStage, Type playerInputType)
    {
        int disabledInputs = 0;
        foreach (GameObject root in ownedStage.GetRootGameObjects())
        {
            foreach (Component input in root.GetComponentsInChildren(playerInputType, true))
            {
                if (input is Behaviour behavior && behavior.enabled)
                {
                    behavior.enabled = false;
                    disabledInputs++;
                }
            }
            foreach (UnityEngine.InputSystem.PlayerInput input in root.GetComponentsInChildren<UnityEngine.InputSystem.PlayerInput>(true))
                if (input.enabled) input.enabled = false;
        }
        Debug.Log($"Disabled {disabledInputs} authored Stage02 player input component(s) while measuring synthetic river-wall contact.");
    }

    private static void ApplyDiggingNetworkState(Component bridgeComponent, Type networkStateType,
        Type stageEnum, Type substageEnum, int componentId, int cycle, int removedUnits)
    {
        object state = Activator.CreateInstance(networkStateType, new object[] { componentId });
        networkStateType.GetField("constructionStage").SetValue(state, (int)Enum.Parse(stageEnum, "Digging"));
        networkStateType.GetField("constructionValueA").SetValue(state, cycle);
        networkStateType.GetField("constructionValueB").SetValue(state, (int)Enum.Parse(substageEnum, "SoilRemoval"));
        networkStateType.GetField("constructionAux0").SetValue(state, (float)removedUnits);
        MethodInfo applyState = bridgeComponent.GetType().GetMethod("ApplyNetworkState", InstanceMembers);
        Assert.That(applyState, Is.Not.Null);
        applyState.Invoke(bridgeComponent, new[] { state });
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
    }

    private static string[] DescribeOverlappingColliders(CharacterController character)
    {
        Bounds bounds = character.bounds;
        float radius = character.radius * character.transform.lossyScale.x;
        Vector3 bottom = new(bounds.center.x, bounds.min.y + radius, bounds.center.z);
        Vector3 top = new(bounds.center.x, bounds.max.y - radius, bounds.center.z);
        return Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Collide)
            .Where(collider => collider != null && collider != character)
            .Select(collider => $"{collider.name}@{collider.bounds.center}/layer{collider.gameObject.layer}/trigger{collider.isTrigger}")
            .ToArray();
    }

    private static void Set(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, InstanceMembers);
        if (field != null) { field.SetValue(target, value); return; }
        PropertyInfo property = target.GetType().GetProperty(name, InstanceMembers);
        Assert.That(property, Is.Not.Null, "Missing member " + name);
        property.SetValue(target, value);
    }

    private static object GetProperty(object target, string name) =>
        target.GetType().GetProperty(name, InstanceMembers).GetValue(target);

    private static object GetMember(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, InstanceMembers);
        return field != null ? field.GetValue(target) : GetProperty(target, name);
    }

}
