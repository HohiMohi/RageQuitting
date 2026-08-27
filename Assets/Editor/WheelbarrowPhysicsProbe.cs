using UnityEditor;
using UnityEngine;
using System.Reflection;

internal static class WheelbarrowPhysicsProbe
{
    private const float TestHeight = 100f;
    private const double TestDuration = 10.0;

    private static GameObject testRoot;
    private static WheelbarrowController wheelbarrow;
    private static Rigidbody body;
    private static double startedAt;
    private static float minY;
    private static float maxY;
    private static float maxAbsVerticalSpeed;
    private static float maxAbsYawSpeed;
    private static float maximumSpeed;
    private static float maximumRolloverRisk;
    private static float maximumTiltAngle;
    private static Vector3 startPosition;
    private static int samples;
    private static string scenarioName;
    private static float probeThrottle;
    private static float probeSteering;
    private static bool probeLoaded = true;
    private static bool tippedScenario;
    private static float minimumTiltAngle;
    private static double activeTestDuration = TestDuration;
    private static double firstTippedAt = -1d;
    private static float previousTimeScale = 1f;
    private static bool timeScaleOverridden;

    [MenuItem("Tools/Wheelbarrow Physics Probe/Loaded - Stationary")]
    private static void RunLoadedStationary() => Run("stationary", BuildFlat, 0f);

    [MenuItem("Tools/Wheelbarrow Physics Probe/Loaded - Flat")]
    private static void RunLoadedFlat() => Run("flat", BuildFlat, 1f);

    [MenuItem("Tools/Wheelbarrow Physics Probe/Loaded - Collider Seam")]
    private static void RunLoadedSeam() => Run("seam", BuildSeam, 1f);

    [MenuItem("Tools/Wheelbarrow Physics Probe/Loaded - Ramp")]
    private static void RunLoadedRamp() => Run("ramp", BuildRamp, 1f);

    [MenuItem("Tools/Wheelbarrow Physics Probe/Loaded - Small Bump")]
    private static void RunLoadedBump() => Run("bump", BuildBump, 1f);

    [MenuItem("Tools/Wheelbarrow Physics Probe/Cornering - Empty Full Turn")]
    private static void RunEmptyFullTurn() => Run("cornering-empty", BuildFlat, 1f, 1f, false);

    [MenuItem("Tools/Wheelbarrow Physics Probe/Cornering - Concrete Full Turn")]
    private static void RunConcreteFullTurn() => Run("cornering-concrete", BuildFlat, 1f, 1f, true);

    [MenuItem("Tools/Wheelbarrow Physics Probe/Cornering - Concrete Legacy Normalized Input")]
    private static void RunConcreteLegacyNormalizedInput()
    {
        float normalizedAxis = 1f / Mathf.Sqrt(2f);
        Run("cornering-concrete-legacy-normalized-input", BuildFlat,
            normalizedAxis, normalizedAxis, true);
    }

    [MenuItem("Tools/Wheelbarrow Physics Probe/Cornering - One Resource Full Turn")]
    private static void RunOneResourceFullTurn() => RunResourceFullTurn(1);

    [MenuItem("Tools/Wheelbarrow Physics Probe/Cornering - Two Resources Full Turn")]
    private static void RunTwoResourcesFullTurn() => RunResourceFullTurn(2);

    [MenuItem("Tools/Wheelbarrow Physics Probe/Cornering - Three Resources Full Turn")]
    private static void RunThreeResourcesFullTurn() => RunResourceFullTurn(3);

    [MenuItem("Tools/Wheelbarrow Physics Probe/Loaded - Tipped Rest")]
    private static void RunLoadedTippedRest()
    {
        Run("tipped-rest", BuildFlat, 0f);
        if (body == null) return;
        tippedScenario = true;
        Vector3 airborne = new Vector3(body.position.x, TestHeight + 1f, body.position.z);
        Quaternion tipped = Quaternion.Euler(0f, 0f, 72f);
        body.position = airborne;
        body.rotation = tipped;
        wheelbarrow.transform.SetPositionAndRotation(airborne, tipped);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        minimumTiltAngle = Vector3.Angle(wheelbarrow.transform.up, Vector3.up);
    }

    [MenuItem("Tools/Wheelbarrow Physics Probe/Passenger Mass Transition")]
    private static void RunPassengerMassTransition()
    {
        Run("passenger-mass-transition", BuildFlat, 0f, 0f, false);
        if (wheelbarrow == null) return;
        bool passed = wheelbarrow.RunEditorPassengerMassTransitionProbe(out string result);
        if (passed) Debug.Log($"[WheelbarrowPhysicsProbe] Passenger mass transition PASS: {result}");
        else Debug.LogError($"[WheelbarrowPhysicsProbe] Passenger mass transition FAIL: {result}");
    }

    [MenuItem("Tools/Wheelbarrow Physics Probe/Passenger Transport Lifecycle")]
    private static void RunPassengerTransportLifecycle()
    {
        GameObject player = new GameObject("PassengerTransportLifecycleProbe");
        CharacterController characterController = player.AddComponent<CharacterController>();
        BoxCollider extraCollider = player.AddComponent<BoxCollider>();
        PlayerTransportCollisionController transport = player.AddComponent<PlayerTransportCollisionController>();
        LayerMask originalCharacterMask = characterController.excludeLayers;
        LayerMask originalExtraMask = extraCollider.excludeLayers;
        bool originalDetectCollisions = characterController.detectCollisions;
        bool originalOverlapRecovery = characterController.enableOverlapRecovery;
        GameObject firstTransport = new GameObject("DestroyedTransportProbe");
        GameObject secondTransport = new GameObject("ReplacementTransportProbe");

        bool firstBegin = transport.BeginTransport(firstTransport);
        Object.DestroyImmediate(firstTransport);
        transport.EnsureSuppressed();
        bool restoredAfterDestroy = !transport.IsTransportCollisionSuppressed &&
            characterController.excludeLayers == originalCharacterMask &&
            extraCollider.excludeLayers == originalExtraMask &&
            characterController.detectCollisions == originalDetectCollisions &&
            characterController.enableOverlapRecovery == originalOverlapRecovery;
        bool secondBegin = transport.BeginTransport(secondTransport);
        transport.EndTransport(secondTransport);
        bool restoredAfterSecondTransport = !transport.IsTransportCollisionSuppressed &&
            characterController.excludeLayers == originalCharacterMask &&
            extraCollider.excludeLayers == originalExtraMask &&
            characterController.detectCollisions == originalDetectCollisions &&
            characterController.enableOverlapRecovery == originalOverlapRecovery;
        bool passed = firstBegin && restoredAfterDestroy && secondBegin && restoredAfterSecondTransport;
        string result = $"firstBegin={firstBegin}, restoredAfterDestroy={restoredAfterDestroy}, " +
            $"secondBegin={secondBegin}, restoredAfterSecondTransport={restoredAfterSecondTransport}";

        Object.DestroyImmediate(secondTransport);
        Object.DestroyImmediate(player);
        if (passed) Debug.Log($"[WheelbarrowPhysicsProbe] Passenger transport lifecycle PASS: {result}");
        else Debug.LogError($"[WheelbarrowPhysicsProbe] Passenger transport lifecycle FAIL: {result}");
    }

    internal static void RunLoadedFlatFromAutomation() => RunLoadedFlat();

    private static void RunResourceFullTurn(int resourceCount)
    {
        Run($"cornering-resources-{resourceCount}", BuildFlat, 1f, 1f, false,
            resourceCount == 1 ? 24d : TestDuration);
        if (wheelbarrow != null)
            wheelbarrow.SendMessage("BeginEditorPhysicsProbe", resourceCount, SendMessageOptions.RequireReceiver);
    }

    private static void Run(string name, System.Action<Transform> buildSurface, float throttle,
        float steering = 0f, bool loaded = true, double duration = TestDuration)
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("Wheelbarrow physics probes require Play Mode.");
            return;
        }

        Cleanup();
        previousTimeScale = Time.timeScale;
        timeScaleOverridden = true;
        Time.timeScale = 1f;
        scenarioName = name;
        probeThrottle = throttle;
        probeSteering = steering;
        probeLoaded = loaded;
        activeTestDuration = duration;
        firstTippedAt = -1d;
        tippedScenario = false;
        testRoot = new GameObject("__WheelbarrowPhysicsProbe");
        buildSurface(testRoot.transform);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Wheelbarrow.prefab");
        if (prefab == null)
        {
            Debug.LogError("Wheelbarrow physics probe could not load the prefab.");
            Cleanup();
            return;
        }

        GameObject instance = Object.Instantiate(prefab, new Vector3(0f, TestHeight, -8f), Quaternion.identity, testRoot.transform);
        instance.name = "Wheelbarrow";
        wheelbarrow = instance.GetComponent<WheelbarrowController>();
        body = instance.GetComponent<Rigidbody>();
        wheelbarrow.SendMessage("BeginEditorPhysicsProbe", loaded, SendMessageOptions.RequireReceiver);

        startedAt = EditorApplication.timeSinceStartup;
        minY = maxY = body.position.y;
        maxAbsVerticalSpeed = 0f;
        maxAbsYawSpeed = 0f;
        maximumSpeed = 0f;
        maximumRolloverRisk = 0f;
        maximumTiltAngle = 0f;
        minimumTiltAngle = 180f;
        startPosition = body.position;
        samples = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || wheelbarrow == null || body == null)
        {
            Cleanup();
            return;
        }

        wheelbarrow.SubmitDriveInput(probeThrottle, probeSteering, 0);
        minY = Mathf.Min(minY, body.position.y);
        maxY = Mathf.Max(maxY, body.position.y);
        maxAbsVerticalSpeed = Mathf.Max(maxAbsVerticalSpeed, Mathf.Abs(body.linearVelocity.y));
        maxAbsYawSpeed = Mathf.Max(maxAbsYawSpeed,
            Mathf.Abs(Vector3.Dot(body.angularVelocity, Vector3.up) * Mathf.Rad2Deg));
        maximumSpeed = Mathf.Max(maximumSpeed, body.linearVelocity.magnitude);
        maximumRolloverRisk = Mathf.Max(maximumRolloverRisk, wheelbarrow.CorneringRolloverRisk);
        maximumTiltAngle = Mathf.Max(maximumTiltAngle, Vector3.Angle(wheelbarrow.transform.up, Vector3.up));
        if (wheelbarrow.State == WheelbarrowState.Tipped)
        {
            if (firstTippedAt < 0d) firstTippedAt = EditorApplication.timeSinceStartup - startedAt;
            minimumTiltAngle = Mathf.Min(minimumTiltAngle, Vector3.Angle(wheelbarrow.transform.up, Vector3.up));
        }
        samples++;

        if (EditorApplication.timeSinceStartup - startedAt < activeTestDuration) return;

        string report =
            $"WHEELBARROW_PROBE {scenarioName} loaded={probeLoaded}: samples={samples}, " +
            $"yRange={maxY - minY:F4}, maxAbsVy={maxAbsVerticalSpeed:F4}, " +
            $"maxAbsYaw={maxAbsYawSpeed:F3}, distance={Vector3.Distance(startPosition, body.position):F3}, " +
            $"maxSpeed={maximumSpeed:F3}, maxRisk={maximumRolloverRisk:F3}, maxTilt={maximumTiltAngle:F2}, " +
            $"final={body.position}, grounded={Read<bool>(wheelbarrow, "drivenWheelGrounded")}, " +
            $"wheelError={Read<float>(wheelbarrow, "wheelSuspensionError"):F4}, " +
            $"wheelSupport={Read<float>(wheelbarrow, "wheelSupportAcceleration"):F3}, " +
            $"driverSupport={Read<float>(wheelbarrow, "driverSupportAcceleration"):F3}, " +
            $"shares={Read<float>(wheelbarrow, "wheelLoadShare"):F3}/{Read<float>(wheelbarrow, "driverSupportLoadShare"):F3}, " +
            $"state={wheelbarrow.State}, tilt={Vector3.Angle(wheelbarrow.transform.up, Vector3.up):F2}, " +
            $"rolloverRisk={wheelbarrow.CorneringRolloverRisk:F3}, " +
            $"receivedInput={Read<float>(wheelbarrow, "lastReceivedThrottleInput"):F3}/" +
            $"{Read<float>(wheelbarrow, "lastReceivedSteeringInput"):F3}, " +
            $"steeringAngle={Read<float>(wheelbarrow, "currentSteeringAngle"):F2}, " +
            $"loadRatio={wheelbarrow.CorneringLoadRatio:F3}, " +
            $"loadSource={wheelbarrow.CorneringLoadSource}, demand={wheelbarrow.CorneringRolloverDemand:F3}, " +
            $"contactAge={wheelbarrow.TimeSinceDrivenWheelContact:F3}, " +
            $"firstTippedAt={(firstTippedAt >= 0d ? firstTippedAt : 0d):F2}, " +
            $"rolloverReferenceSpeed={wheelbarrow.EffectiveRolloverReferenceSpeed:F3}, " +
            $"minimumTippedTilt={(minimumTiltAngle < 180f ? minimumTiltAngle : 0f):F2}";
        SessionState.SetString("WheelbarrowPhysicsProbe.LastResult", report);
        Debug.Log(report);
        Cleanup();
    }

    private static void BuildFlat(Transform root)
    {
        CreateGround(root, "Ground", new Vector3(0f, TestHeight - 0.5f, 10f), new Vector3(120f, 1f, 120f));
    }

    private static void BuildSeam(Transform root)
    {
        CreateGround(root, "Ground_A", new Vector3(0f, TestHeight - 0.5f, -4f), new Vector3(16f, 1f, 16f));
        CreateGround(root, "Ground_B", new Vector3(0f, TestHeight - 0.495f, 20f), new Vector3(16f, 1f, 32f));
    }

    private static void BuildRamp(Transform root)
    {
        CreateGround(root, "RampApproach", new Vector3(0f, TestHeight - 0.5f, -4f), new Vector3(16f, 1f, 16f));
        GameObject ramp = CreateGround(root, "Ramp", new Vector3(0f, TestHeight + 0.4f, 14f), new Vector3(16f, 1f, 22f));
        ramp.transform.rotation = Quaternion.Euler(-8f, 0f, 0f);
    }

    private static void BuildBump(Transform root)
    {
        BuildFlat(root);
        CreateGround(root, "Bump", new Vector3(0f, TestHeight + 0.025f, 1f), new Vector3(4f, 0.05f, 0.8f));
    }

    private static GameObject CreateGround(Transform root, string name, Vector3 position, Vector3 scale)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = name;
        ground.transform.SetParent(root, false);
        ground.transform.SetPositionAndRotation(position, Quaternion.identity);
        ground.transform.localScale = scale;
        return ground;
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (testRoot != null) testRoot.SetActive(false);
        if (timeScaleOverridden)
        {
            Time.timeScale = previousTimeScale;
            timeScaleOverridden = false;
        }
        testRoot = null;
        wheelbarrow = null;
        body = null;
    }

    private static T Read<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null ? (T)field.GetValue(target) : default;
    }
}
