using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
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
    private static string expectedContactCollider;
    private static string expectedContactSource;
    private static bool contactAssertionFailed;
    private static bool expectedContactObserved;

    [MenuItem("Tools/Wheelbarrow Physics Probe/Rope Attachment And Pull Profile")]
    private static void RunRopeAttachmentAndPullProfile()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Wheelbarrow.prefab");
        RopeToolProfileSO ropeProfile = AssetDatabase.LoadAssetAtPath<RopeToolProfileSO>(
            "Assets/ScriptableObjectAssets/New/RopeToolProfile.asset");
        WheelbarrowController controller = prefab != null ? prefab.GetComponent<WheelbarrowController>() : null;
        string lifecycleResult = "wheelbarrow prefab/controller missing";
        bool lifecyclePassed = controller != null && controller.RunEditorRopeLifecycleProbe(out lifecycleResult);
        bool pullPassed = RopeToolController.RunEditorWheelbarrowPullProfileProbe(ropeProfile, out string pullResult);
        string result = $"lifecycle=({lifecycleResult}), pull=({pullResult})";
        if (lifecyclePassed && pullPassed)
            Debug.Log($"[WheelbarrowPhysicsProbe] Rope attachment/pull PASS: {result}");
        else
            Debug.LogError($"[WheelbarrowPhysicsProbe] Rope attachment/pull FAIL: {result}");
    }

    [MenuItem("Tools/Wheelbarrow Physics Probe/Rope Tow - Physical Scenarios")]
    private static void RunRopeTowPhysicalScenarios()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("Wheelbarrow rope towing physical probe requires Play Mode.");
            return;
        }
        if (Object.FindFirstObjectByType<WheelbarrowRopeTowPhysicalProbe>() != null)
        {
            Debug.LogWarning("Wheelbarrow rope towing physical probe is already running.");
            return;
        }

        new GameObject("__WheelbarrowRopeTowPhysicalProbe")
            .AddComponent<WheelbarrowRopeTowPhysicalProbe>();
    }

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

    [MenuItem("Tools/Wheelbarrow Physics Probe/Wheel Contact - Low Ceiling")]
    private static void RunLowCeiling()
    {
        Run("wheel-contact-low-ceiling", BuildLowCeiling, 1f, 0f, true, 4d,
            "Ground", "SphereCast");
    }

    [MenuItem("Tools/Wheelbarrow Physics Probe/Wheel Contact - Starting Overlap Fallback")]
    private static void RunStartingOverlapFallback()
    {
        Run("wheel-contact-starting-overlap", BuildStartingOverlap, 0f, 0f, true, 1d,
            "Ground", "RaycastFallback");
        if (wheelbarrow == null || body == null) return;

        Vector3 linearVelocity = body.linearVelocity;
        Vector3 angularVelocity = body.angularVelocity;
        bool contactPassed = wheelbarrow.RunEditorWheelContactQueryProbe(out string result);
        string source = Read<object>(wheelbarrow, "drivenWheelContactSource")?.ToString() ?? "None";
        RaycastHit hit = Read<RaycastHit>(wheelbarrow, "drivenWheelHit");
        bool expected = contactPassed && source == "RaycastFallback" &&
            hit.collider != null && hit.collider.name == "Ground" &&
            hit.point.sqrMagnitude > 0.000001f;
        bool noImpulse = Vector3.Distance(linearVelocity, body.linearVelocity) <= 0.0001f &&
            Vector3.Distance(angularVelocity, body.angularVelocity) <= 0.0001f;
        if (expected && noImpulse)
            Debug.Log($"[WheelbarrowPhysicsProbe] Starting overlap fallback PASS: {result}, noImpulse={noImpulse}");
        else
            Debug.LogError($"[WheelbarrowPhysicsProbe] Starting overlap fallback FAIL: {result}, " +
                $"expectedSource=RaycastFallback, expectedCollider=Ground, noImpulse={noImpulse}");
        Cleanup();
    }

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

    [MenuItem("Tools/Wheelbarrow Physics Probe/Safe Exit Geometry And Ejection Profile")]
    private static void RunSafeExitGeometryAndEjectionProfile()
    {
        GameObject root = new GameObject("__WheelbarrowSafeExitProbe");
        GameObject wheelbarrowObject = null;
        GameObject player = null;
        try
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Wheelbarrow.prefab");
            wheelbarrowObject = prefab != null ? Object.Instantiate(prefab, root.transform) : null;
            WheelbarrowController controller = wheelbarrowObject != null
                ? wheelbarrowObject.GetComponent<WheelbarrowController>()
                : null;
            if (controller == null)
            {
                Debug.LogError("[WheelbarrowPhysicsProbe] Safe exit probe FAIL: wheelbarrow prefab/controller missing.");
                return;
            }

            GameObject ground = CreateGround(root.transform, "ExitProbeGround", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            Collider groundCollider = ground.GetComponent<Collider>();
            player = new GameObject("ExitProbePlayer");
            player.transform.SetParent(root.transform, false);
            CharacterController characterController = player.AddComponent<CharacterController>();
            characterController.height = 2f;
            characterController.radius = 0.5f;
            characterController.center = new Vector3(0f, 0.93f, 0f);
            characterController.skinWidth = 0.02f;
            Vector3 groundedRoot = new Vector3(5f, 0.09f, 0f);

            MethodInfo buildCapsule = typeof(WheelbarrowController).GetMethod(
                "BuildPaddedPlayerCapsule",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo isCapsuleFree = typeof(WheelbarrowController).GetMethod(
                "IsCapsuleFree",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo shouldApplyTippedPassengerImpulse = typeof(WheelbarrowController).GetMethod(
                "ShouldApplyTippedPassengerImpulse",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo hasForcedExitFallbackElapsed = typeof(WheelbarrowController).GetMethod(
                "HasForcedExitFallbackElapsed",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo isPendingExitCapsuleReserved = typeof(WheelbarrowController).GetMethod(
                "IsPendingExitCapsuleReserved",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo resolveEjectionDirection = typeof(WheelbarrowController).GetMethod(
                "ResolveEjectionDirection",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (buildCapsule == null || isCapsuleFree == null ||
                shouldApplyTippedPassengerImpulse == null || hasForcedExitFallbackElapsed == null ||
                isPendingExitCapsuleReserved == null || resolveEjectionDirection == null)
            {
                Debug.LogError("[WheelbarrowPhysicsProbe] Safe exit probe FAIL: required methods missing.");
                return;
            }

            object[] capsuleArgs = { groundedRoot, characterController, Vector3.zero, Vector3.zero, 0f };
            buildCapsule.Invoke(controller, capsuleArgs);
            Vector3 bottom = (Vector3)capsuleArgs[2];
            Vector3 top = (Vector3)capsuleArgs[3];
            float radius = (float)capsuleArgs[4];
            bool clearOnGround = (bool)isCapsuleFree.Invoke(
                controller,
                new object[] { groundedRoot, characterController, player.transform, groundCollider });

            GameObject blocker = CreateGround(root.transform, "ExitProbeBlocker", new Vector3(5f, 1f, 0f), new Vector3(0.5f, 1f, 0.5f));
            Physics.SyncTransforms();
            bool blockedByObstacle = !(bool)isCapsuleFree.Invoke(
                controller,
                new object[] { groundedRoot, characterController, player.transform, groundCollider });

            WheelbarrowProfileSO wheelbarrowProfile = AssetDatabase.LoadAssetAtPath<WheelbarrowProfileSO>(
                "Assets/GeneratedAssets/Wheelbarrow/WheelbarrowProfile.asset");
            ExternalImpulseProfileSO impulseProfile = wheelbarrowProfile != null
                ? wheelbarrowProfile.PassengerTippedEjectionImpulseProfile
                : null;
            ExternalImpulseData impulse = impulseProfile != null
                ? impulseProfile.CreateImpulse(Vector3.right)
                : default;
            bool profileValid = wheelbarrowProfile != null &&
                Mathf.Approximately(wheelbarrowProfile.ForcedExitFallbackDelay, 3f) &&
                impulseProfile != null &&
                Vector3.Distance(impulse.InitialVelocity, new Vector3(6f, 3f, 0f)) <= 0.001f &&
                Mathf.Approximately(impulse.HorizontalDeceleration, 10f) &&
                Mathf.Approximately(impulse.MaximumDuration, 1.5f) &&
                Mathf.Approximately(impulse.MovementControlMultiplier, 0.5f) &&
                Mathf.Approximately(impulse.MaximumHorizontalSpeed, 10f) &&
                Mathf.Approximately(impulse.MaximumVerticalSpeed, 6f) &&
                impulse.ForceDropHeldObject;
            bool impulseQualificationValid =
                (bool)shouldApplyTippedPassengerImpulse.Invoke(null, new object[] { true, true, WheelbarrowState.Tipped }) &&
                !(bool)shouldApplyTippedPassengerImpulse.Invoke(null, new object[] { false, true, WheelbarrowState.Tipped }) &&
                !(bool)shouldApplyTippedPassengerImpulse.Invoke(null, new object[] { true, false, WheelbarrowState.Tipped }) &&
                !(bool)shouldApplyTippedPassengerImpulse.Invoke(null, new object[] { true, true, WheelbarrowState.Free });
            bool fallbackValid =
                !(bool)hasForcedExitFallbackElapsed.Invoke(null, new object[] { true, false, 10f, 12.99f, 3f }) &&
                (bool)hasForcedExitFallbackElapsed.Invoke(null, new object[] { true, false, 10f, 13f, 3f }) &&
                !(bool)hasForcedExitFallbackElapsed.Invoke(null, new object[] { true, true, 10f, 20f, 3f }) &&
                !(bool)hasForcedExitFallbackElapsed.Invoke(null, new object[] { false, false, 10f, 20f, 3f });
            bool reservationValid = ValidatePendingExitReservation(
                controller,
                bottom,
                top,
                radius,
                isPendingExitCapsuleReserved);
            Vector3 verticalFallbackDirection = (Vector3)resolveEjectionDirection.Invoke(
                null,
                new object[] { Vector3.zero, Vector3.up, Vector3.down, Vector3.up });
            bool ejectionFallbackValid = Vector3.Distance(verticalFallbackDirection, Vector3.right) <= 0.0001f &&
                Mathf.Approximately(verticalFallbackDirection.magnitude, 1f);
            bool rolledEjectionValid = ValidateRolledPassengerEjectionDirections(
                controller,
                resolveEjectionDirection,
                out float positiveRollDot,
                out float negativeRollDot);
            bool geometryValid = Mathf.Approximately(radius, 0.55f) &&
                bottom.y - radius >= -0.0001f && top.y > bottom.y;
            bool passed = geometryValid && clearOnGround && blockedByObstacle && profileValid &&
                impulseQualificationValid && fallbackValid && reservationValid && ejectionFallbackValid &&
                rolledEjectionValid;
            string result = $"geometry={geometryValid}, bottom={bottom}, top={top}, radius={radius:F3}, " +
                $"clearOnGround={clearOnGround}, blockedByObstacle={blockedByObstacle}, profile={profileValid}, " +
                $"impulseQualification={impulseQualificationValid}, fallback={fallbackValid}, " +
                $"reservation={reservationValid}, ejectionFallback={ejectionFallbackValid}, " +
                $"rolledEjection={rolledEjectionValid} (+90dot={positiveRollDot:F3}, -90dot={negativeRollDot:F3})";
            if (passed) Debug.Log($"[WheelbarrowPhysicsProbe] Safe exit/ejection PASS: {result}");
            else Debug.LogError($"[WheelbarrowPhysicsProbe] Safe exit/ejection FAIL: {result}");
            Object.DestroyImmediate(blocker);
        }
        finally
        {
            if (player != null) Object.DestroyImmediate(player);
            if (wheelbarrowObject != null) Object.DestroyImmediate(wheelbarrowObject);
            if (root != null) Object.DestroyImmediate(root);
        }
    }

    [MenuItem("Tools/Wheelbarrow Physics Probe/Pending Exit Reservation And Ejection Fallback")]
    private static void RunPendingExitReservationAndEjectionFallback()
    {
        GameObject root = new GameObject("__WheelbarrowPendingExitProbe");
        GameObject wheelbarrowObject = null;
        GameObject player = null;
        try
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Wheelbarrow.prefab");
            wheelbarrowObject = prefab != null ? Object.Instantiate(prefab, root.transform) : null;
            WheelbarrowController controller = wheelbarrowObject != null
                ? wheelbarrowObject.GetComponent<WheelbarrowController>()
                : null;
            if (controller == null)
            {
                Debug.LogError("[WheelbarrowPhysicsProbe] Pending exit probe FAIL: wheelbarrow prefab/controller missing.");
                return;
            }

            player = new GameObject("PendingExitProbePlayer");
            player.transform.SetParent(root.transform, false);
            CharacterController characterController = player.AddComponent<CharacterController>();
            characterController.height = 2f;
            characterController.radius = 0.5f;
            characterController.center = new Vector3(0f, 0.93f, 0f);

            MethodInfo buildCapsule = typeof(WheelbarrowController).GetMethod(
                "BuildPaddedPlayerCapsule",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo reservationMethod = typeof(WheelbarrowController).GetMethod(
                "IsPendingExitCapsuleReserved",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo resolveDirection = typeof(WheelbarrowController).GetMethod(
                "ResolveEjectionDirection",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (buildCapsule == null || reservationMethod == null || resolveDirection == null)
            {
                Debug.LogError("[WheelbarrowPhysicsProbe] Pending exit probe FAIL: required methods missing.");
                return;
            }

            object[] capsuleArgs = { Vector3.zero, characterController, Vector3.zero, Vector3.zero, 0f };
            buildCapsule.Invoke(controller, capsuleArgs);
            Vector3 bottom = (Vector3)capsuleArgs[2];
            Vector3 top = (Vector3)capsuleArgs[3];
            float radius = (float)capsuleArgs[4];
            bool reservationValid = ValidatePendingExitReservation(
                controller,
                bottom,
                top,
                radius,
                reservationMethod);
            Vector3 fallback = (Vector3)resolveDirection.Invoke(
                null,
                new object[] { Vector3.zero, Vector3.up, Vector3.down, Vector3.up });
            bool fallbackValid = Vector3.Distance(fallback, Vector3.right) <= 0.0001f &&
                Mathf.Approximately(fallback.magnitude, 1f);
            bool rolledEjectionValid = ValidateRolledPassengerEjectionDirections(
                controller,
                resolveDirection,
                out float positiveRollDot,
                out float negativeRollDot);
            string result = $"reservation={reservationValid}, ejectionFallback={fallbackValid}, direction={fallback}, " +
                $"rolledEjection={rolledEjectionValid} (+90dot={positiveRollDot:F3}, -90dot={negativeRollDot:F3})";
            if (reservationValid && fallbackValid && rolledEjectionValid)
                Debug.Log($"[WheelbarrowPhysicsProbe] Pending exit reservation/ejection PASS: {result}");
            else
                Debug.LogError($"[WheelbarrowPhysicsProbe] Pending exit reservation/ejection FAIL: {result}");
        }
        finally
        {
            if (player != null) Object.DestroyImmediate(player);
            if (wheelbarrowObject != null) Object.DestroyImmediate(wheelbarrowObject);
            if (root != null) Object.DestroyImmediate(root);
        }
    }

    private static bool ValidateRolledPassengerEjectionDirections(
        WheelbarrowController controller,
        MethodInfo resolveDirection,
        out float positiveRollDot,
        out float negativeRollDot)
    {
        positiveRollDot = -1f;
        negativeRollDot = -1f;
        if (controller == null || controller.PassengerAnchor == null || resolveDirection == null) return false;

        Quaternion originalRotation = controller.transform.rotation;
        positiveRollDot = ValidateRolledPassengerEjectionDirection(
            controller,
            resolveDirection,
            90f,
            new Vector3(1.35f, 0f, -0.4f));
        negativeRollDot = ValidateRolledPassengerEjectionDirection(
            controller,
            resolveDirection,
            -90f,
            new Vector3(-1.35f, 0f, -0.4f));
        controller.transform.rotation = originalRotation;
        return positiveRollDot > 0.999f && negativeRollDot > 0.999f;
    }

    private static float ValidateRolledPassengerEjectionDirection(
        WheelbarrowController controller,
        MethodInfo resolveDirection,
        float rollDegrees,
        Vector3 localExitPosition)
    {
        controller.transform.rotation = Quaternion.Euler(0f, 0f, rollDegrees);
        Vector3 origin = controller.PassengerAnchor.position;
        Vector3 exitPosition = controller.transform.TransformPoint(localExitPosition);
        Vector3 expected = Vector3.ProjectOnPlane(exitPosition - origin, Vector3.up);
        if (expected.sqrMagnitude <= 0.0001f) return -1f;

        Vector3 resolved = (Vector3)resolveDirection.Invoke(
            null,
            new object[]
            {
                origin,
                controller.transform.forward,
                controller.transform.right,
                exitPosition
            });
        if (!Mathf.Approximately(resolved.magnitude, 1f)) return -1f;
        return Vector3.Dot(resolved, expected.normalized);
    }

    private static bool ValidatePendingExitReservation(
        WheelbarrowController controller,
        Vector3 bottom,
        Vector3 top,
        float radius,
        MethodInfo reservationMethod)
    {
        FieldInfo pendingField = typeof(WheelbarrowController).GetField(
            "pendingSafeExits",
            BindingFlags.Instance | BindingFlags.NonPublic);
        System.Type pendingType = typeof(WheelbarrowController).GetNestedType(
            "PendingSafeExit",
            BindingFlags.NonPublic);
        if (pendingField?.GetValue(controller) is not IDictionary pending || pendingType == null) return false;

        object reservation = System.Activator.CreateInstance(pendingType);
        pendingType.GetField("ClientId")?.SetValue(reservation, 101UL);
        pendingType.GetField("PlacementRequested")?.SetValue(reservation, true);
        pendingType.GetField("ReservedCapsuleBottom")?.SetValue(reservation, bottom);
        pendingType.GetField("ReservedCapsuleTop")?.SetValue(reservation, top);
        pendingType.GetField("ReservedCapsuleRadius")?.SetValue(reservation, radius);
        pending.Add(101UL, reservation);

        bool identicalBlocked = (bool)reservationMethod.Invoke(
            controller,
            new object[] { 202UL, bottom, top, radius });
        bool ownReservationIgnored = !(bool)reservationMethod.Invoke(
            controller,
            new object[] { 101UL, bottom, top, radius });
        Vector3 separation = Vector3.right * (radius * 2f + 0.1f);
        bool separatedAvailable = !(bool)reservationMethod.Invoke(
            controller,
            new object[] { 202UL, bottom + separation, top + separation, radius });
        pending.Clear();
        return identicalBlocked && ownReservationIgnored && separatedAvailable;
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
        float steering = 0f, bool loaded = true, double duration = TestDuration,
        string requiredContactCollider = null, string requiredContactSource = null)
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
        expectedContactCollider = requiredContactCollider;
        expectedContactSource = requiredContactSource;
        contactAssertionFailed = false;
        expectedContactObserved = false;
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
        ConfigureQueryOnlyObstacleCollisions();
        Physics.SyncTransforms();

        if (!string.IsNullOrEmpty(expectedContactCollider))
        {
            bool immediatePassed = wheelbarrow.RunEditorWheelContactQueryProbe(out string immediateResult);
            EvaluateCurrentContact();
            if (!immediatePassed || contactAssertionFailed)
                Debug.LogError($"[WheelbarrowPhysicsProbe] Immediate contact validation FAIL for {name}: " +
                    immediateResult);
            else
                Debug.Log($"[WheelbarrowPhysicsProbe] Immediate contact validation PASS for {name}: " +
                    immediateResult);
        }

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
        EvaluateCurrentContact();
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
        bool contactProbePassed = string.IsNullOrEmpty(expectedContactCollider) ||
            expectedContactObserved && !contactAssertionFailed;
        if (contactProbePassed) Debug.Log(report);
        else Debug.LogError(report + $", contactAssertionFailed={contactAssertionFailed}, " +
            $"expectedContactObserved={expectedContactObserved}, expected={expectedContactSource}/{expectedContactCollider}");
        Cleanup();
    }

    private static void EvaluateCurrentContact()
    {
        if (wheelbarrow == null || string.IsNullOrEmpty(expectedContactCollider)) return;
        if (!Read<bool>(wheelbarrow, "drivenWheelGrounded")) return;

        RaycastHit hit = Read<RaycastHit>(wheelbarrow, "drivenWheelHit");
        Vector3 point = Read<Vector3>(wheelbarrow, "filteredWheelContactPoint");
        string source = Read<object>(wheelbarrow, "drivenWheelContactSource")?.ToString() ?? "None";
        string colliderName = hit.collider != null ? hit.collider.name : "none";
        bool invalidPoint = float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
            float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z) ||
            point.sqrMagnitude <= 0.000001f;
        bool forbiddenCollider = colliderName.Contains("Roof") || colliderName.Contains("Passenger");
        if (invalidPoint || forbiddenCollider)
        {
            contactAssertionFailed = true;
            return;
        }

        if (colliderName == expectedContactCollider &&
            (string.IsNullOrEmpty(expectedContactSource) || source == expectedContactSource))
            expectedContactObserved = true;
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

    private static void BuildLowCeiling(Transform root)
    {
        BuildFlat(root);
        CreateGround(root, "ProbeRoof", new Vector3(0f, TestHeight + 1.25f, -7.34f),
            new Vector3(1.2f, 0.1f, 1.2f));
        GameObject passenger = new GameObject("ProbePassenger");
        passenger.transform.SetParent(root, false);
        passenger.transform.position = new Vector3(0f, TestHeight + 0.44f, -7.34f);
        CapsuleCollider passengerCollider = passenger.AddComponent<CapsuleCollider>();
        passengerCollider.radius = 0.25f;
        passengerCollider.height = 0.8f;
        passengerCollider.direction = 1;
    }

    private static void BuildStartingOverlap(Transform root)
    {
        CreateGround(root, "Ground", new Vector3(0f, TestHeight - 0.37f, 10f),
            new Vector3(120f, 1f, 120f));
    }

    private static void ConfigureQueryOnlyObstacleCollisions()
    {
        if (testRoot == null || wheelbarrow == null) return;
        Transform roof = testRoot.transform.Find("ProbeRoof");
        Transform passenger = testRoot.transform.Find("ProbePassenger");
        if (passenger != null)
            wheelbarrow.SetEditorWheelContactIgnoredPassenger(passenger);
        Collider roofCollider = roof != null ? roof.GetComponent<Collider>() : null;
        Collider passengerCollider = passenger != null ? passenger.GetComponent<Collider>() : null;
        foreach (Collider wheelbarrowCollider in wheelbarrow.GetComponentsInChildren<Collider>(true))
        {
            if (wheelbarrowCollider == null) continue;
            if (roofCollider != null)
                Physics.IgnoreCollision(wheelbarrowCollider, roofCollider, true);
            if (passengerCollider != null)
                Physics.IgnoreCollision(wheelbarrowCollider, passengerCollider, true);
        }
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
        expectedContactCollider = null;
        expectedContactSource = null;
        contactAssertionFailed = false;
        expectedContactObserved = false;
    }

    private static T Read<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null ? (T)field.GetValue(target) : default;
    }
}

internal sealed class WheelbarrowRopeTowPhysicalProbe : MonoBehaviour
{
    private const float Height = 100f;
    private const float PullDuration = 2f;
    private readonly List<string> failures = new List<string>();
    private GameObject prefab;
    private RopeToolProfileSO ropeProfile;
    private MethodInfo setConcreteLoads;
    private MethodInfo setDriver;
    private MethodInfo setState;
    private MethodInfo applyRopeTow;
    private MethodInfo notifyRopeTowDetached;

    private IEnumerator Start()
    {
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Wheelbarrow.prefab");
        ropeProfile = AssetDatabase.LoadAssetAtPath<RopeToolProfileSO>(
            "Assets/ScriptableObjectAssets/New/RopeToolProfile.asset");
        if (prefab == null || ropeProfile == null)
        {
            Debug.LogError("[WheelbarrowPhysicsProbe] Rope tow physical FAIL: prefab or rope profile missing.");
            Destroy(gameObject);
            yield break;
        }
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        setConcreteLoads = typeof(WheelbarrowController).GetMethod("SetConcreteLoads", flags);
        setDriver = typeof(WheelbarrowController).GetMethod("SetDriver", flags);
        setState = typeof(WheelbarrowController).GetMethod("SetState", flags);
        applyRopeTow = typeof(WheelbarrowController).GetMethod("ApplyRopeTow", flags);
        notifyRopeTowDetached = typeof(WheelbarrowController).GetMethod("NotifyRopeTowDetached", flags);
        if (setConcreteLoads == null || setDriver == null || setState == null || applyRopeTow == null ||
            notifyRopeTowDetached == null)
        {
            Debug.LogError("[WheelbarrowPhysicsProbe] Rope tow physical FAIL: required controller methods missing.");
            Destroy(gameObject);
            yield break;
        }

        yield return RunScenario("free-empty-side", false, false, false);
        yield return RunScenario("free-loaded-high", true, false, true);
        yield return RunScenario("tipped-empty-side", false, true, false);
        yield return RunScenario("tipped-loaded-high", true, true, true);

        if (failures.Count == 0)
            Debug.Log("[WheelbarrowPhysicsProbe] Rope tow physical PASS: Free/Tipped empty/loaded scenarios translated, " +
                "respected speed/material/state constraints, and restored all materials.");
        else
            Debug.LogError("[WheelbarrowPhysicsProbe] Rope tow physical FAIL:\n- " + string.Join("\n- ", failures));
        Destroy(gameObject);
    }

    private IEnumerator RunScenario(string name, bool loaded, bool tipped, bool highAttachment)
    {
        GameObject root = new GameObject("__RopeTow_" + name);
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.SetParent(root.transform, false);
        ground.transform.SetPositionAndRotation(new Vector3(0f, Height - 0.5f, 0f), Quaternion.identity);
        ground.transform.localScale = new Vector3(40f, 1f, 40f);

        Quaternion rotation = tipped ? Quaternion.Euler(0f, 0f, 72f) : Quaternion.identity;
        Vector3 spawn = new Vector3(0f, Height + (tipped ? 0.65f : 0f), -5f);
        GameObject instance = Instantiate(prefab, spawn, rotation, root.transform);
        instance.name = "Wheelbarrow";
        WheelbarrowController controller = instance.GetComponent<WheelbarrowController>();
        Rigidbody rigidbody = instance.GetComponent<Rigidbody>();
        setConcreteLoads.Invoke(controller, new object[] { loaded ? 1 : 0 });
        setDriver.Invoke(controller, new object[] { WheelbarrowController.NoClient });
        setState.Invoke(controller, new object[] { tipped ? WheelbarrowState.Tipped : WheelbarrowState.Free });
        rigidbody.position = spawn;
        rigidbody.rotation = rotation;
        rigidbody.linearVelocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();

        Dictionary<Collider, PhysicsMaterial> originals = new Dictionary<Collider, PhysicsMaterial>();
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            if (collider != null && !collider.isTrigger) originals[collider] = collider.sharedMaterial;

        Vector3 localPoint = highAttachment
            ? new Vector3(0.5f, 1.05f, 0.25f)
            : new Vector3(0.62f, 0.55f, 0.15f);
        ApplyTow(controller, localPoint, Vector3.forward, 1f, 0.5f, true);
        yield return new WaitForFixedUpdate();
        if (controller.IsRopeTowActive || controller.RopeTowSwappedColliderCount != 0)
            failures.Add($"{name}: blocked rope activated towing or changed materials.");

        yield return new WaitForSeconds(0.35f);
        Vector3 start = rigidbody.position;
        float maximumSpeed = 0f;
        float maximumAngularSpeed = 0f;
        float maximumVerticalDisplacement = 0f;
        int expectedSwappedCount = -1;
        bool activeObserved = false;
        float elapsed = 0f;
        while (elapsed < PullDuration)
        {
            ApplyTow(controller, localPoint, Vector3.forward, 1f, 0.5f, false);
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;
            activeObserved |= controller.IsRopeTowActive;
            maximumSpeed = Mathf.Max(maximumSpeed, rigidbody.linearVelocity.magnitude);
            maximumAngularSpeed = Mathf.Max(maximumAngularSpeed, rigidbody.angularVelocity.magnitude);
            maximumVerticalDisplacement = Mathf.Max(maximumVerticalDisplacement,
                Mathf.Abs(rigidbody.position.y - start.y));
            if (controller.RopeTowSwappedColliderCount > 0)
            {
                if (expectedSwappedCount < 0) expectedSwappedCount = controller.RopeTowSwappedColliderCount;
                else if (expectedSwappedCount != controller.RopeTowSwappedColliderCount)
                    failures.Add($"{name}: swapped material cache changed from {expectedSwappedCount} to " +
                        $"{controller.RopeTowSwappedColliderCount} while continuously towing.");
            }
        }

        float planarDistance = Vector3.ProjectOnPlane(rigidbody.position - start, Vector3.up).magnitude;
        if (!activeObserved) failures.Add($"{name}: towing never became active.");
        if (planarDistance < 0.35f) failures.Add($"{name}: translated only {planarDistance:F3}m in {PullDuration:F1}s.");
        if (maximumSpeed > 2.85f) failures.Add($"{name}: speed {maximumSpeed:F3}m/s exceeded safety margin.");
        if (!highAttachment && maximumAngularSpeed < 0.01f)
            failures.Add($"{name}: side attachment produced no measurable rotation.");
        if (highAttachment && planarDistance <= maximumVerticalDisplacement)
            failures.Add($"{name}: high attachment lifted more than it translated " +
                $"(planar={planarDistance:F3}, vertical={maximumVerticalDisplacement:F3}).");
        if (controller.State != (tipped ? WheelbarrowState.Tipped : WheelbarrowState.Free))
            failures.Add($"{name}: towing changed state to {controller.State}.");

        if (name == "free-empty-side")
        {
            yield return VerifySlackRelease(controller, localPoint, originals, expectedSwappedCount);
            yield return VerifyExplicitDetach(controller, localPoint, originals);
            yield return VerifyMissingSignalRelease(controller, localPoint, originals);
        }
        else
        {
            ApplyTow(controller, localPoint, Vector3.forward, 0f, 0f, false);
            yield return new WaitForSeconds(0.3f);
            if (controller.IsRopeTowActive || controller.RopeTowSwappedColliderCount != 0)
                failures.Add($"{name}: towing/material cache did not release after slack.");
            AssertMaterialsRestored(name, originals);
        }

        Debug.Log($"[WheelbarrowPhysicsProbe] Rope tow {name}: planar={planarDistance:F3}, " +
            $"maxSpeed={maximumSpeed:F3}, maxAngular={maximumAngularSpeed:F3}, " +
            $"maxVertical={maximumVerticalDisplacement:F3}, swapped={expectedSwappedCount}, state={controller.State}.");
        Destroy(root);
        yield return null;
    }

    private IEnumerator VerifySlackRelease(WheelbarrowController controller, Vector3 localPoint,
        Dictionary<Collider, PhysicsMaterial> originals, int expectedSwappedCount)
    {
        ApplyTow(controller, localPoint, Vector3.forward, 0f, 0f, false);
        yield return new WaitForSeconds(0.1f);
        if (controller.IsRopeTowActive)
            failures.Add("slack-grace: tow remained active after receiving a slack signal.");
        if (expectedSwappedCount > 0 && controller.RopeTowSwappedColliderCount != expectedSwappedCount)
            failures.Add("slack-grace: materials were restored before the 0.2s grace elapsed.");

        yield return new WaitForSeconds(0.15f);
        if (controller.IsRopeTowActive || controller.RopeTowSwappedColliderCount != 0)
            failures.Add("slack-grace: materials were not restored after one 0.2s grace period.");
        AssertMaterialsRestored("slack-grace", originals);
    }

    private IEnumerator VerifyExplicitDetach(WheelbarrowController controller, Vector3 localPoint,
        Dictionary<Collider, PhysicsMaterial> originals)
    {
        ApplyTow(controller, localPoint, Vector3.forward, 1f, 0.5f, false);
        yield return new WaitForFixedUpdate();
        if (!controller.IsRopeTowActive || controller.RopeTowSwappedColliderCount == 0)
            failures.Add("explicit-detach: failed to reactivate towing before detach.");

        notifyRopeTowDetached.Invoke(controller, null);
        if (controller.IsRopeTowActive || controller.RopeTowSwappedColliderCount != 0)
            failures.Add("explicit-detach: tow/materials were not restored immediately.");
        AssertMaterialsRestored("explicit-detach", originals);
    }

    private IEnumerator VerifyMissingSignalRelease(WheelbarrowController controller, Vector3 localPoint,
        Dictionary<Collider, PhysicsMaterial> originals)
    {
        ApplyTow(controller, localPoint, Vector3.forward, 1f, 0.5f, false);
        yield return new WaitForFixedUpdate();
        if (!controller.IsRopeTowActive || controller.RopeTowSwappedColliderCount == 0)
            failures.Add("missing-signal: failed to reactivate towing before timeout.");

        yield return new WaitForSeconds(0.1f);
        if (!controller.IsRopeTowActive || controller.RopeTowSwappedColliderCount == 0)
            failures.Add("missing-signal: tow released before the configured grace elapsed.");

        yield return new WaitForSeconds(0.15f);
        if (controller.IsRopeTowActive || controller.RopeTowSwappedColliderCount != 0)
            failures.Add("missing-signal: fallback required more than one 0.2s release delay.");
        AssertMaterialsRestored("missing-signal", originals);
    }

    private void AssertMaterialsRestored(string context, Dictionary<Collider, PhysicsMaterial> originals)
    {
        foreach (KeyValuePair<Collider, PhysicsMaterial> original in originals)
        {
            if (original.Key != null && original.Key.sharedMaterial != original.Value)
                failures.Add($"{context}: material was not restored on {original.Key.name}.");
        }
    }

    private void ApplyTow(WheelbarrowController controller, Vector3 localPoint, Vector3 desiredDirection,
        float tension, float extension, bool blocked)
    {
        applyRopeTow.Invoke(controller,
            new object[] { localPoint, -desiredDirection, tension, extension, blocked, ropeProfile });
    }
}
