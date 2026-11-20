using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Type = System.Type;

public sealed class LooseSoilStage5ReturnPlayModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<GameObject> roots = new();
    private readonly List<Mouse> ownedTestMice = new();
    private PlayModeTestScope scope;
    private GameObject bucketPrefab, playerPrefab, foundationPrefab;
    private Mouse testMouse;
    private CrowbarInputSettingsScope testInputSettings;
    private bool focusedInputRoutingUsed;
    private Object soil, looseSoilMaterial;
    private Type bucketType, siteType, fieldType, volumeType, bridgeType, playerType, heightFieldType;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        scope = new PlayModeTestScope("LooseSoilStage5Return");
        testInputSettings = new CrowbarInputSettingsScope();
        scope.RestoreWith(RemoveOwnedTestMice);
        scope.RestoreWith(RestoreInputSettings);
        bucketType = Type.GetType("PortableSubstanceContainer, Assembly-CSharp");
        siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        bridgeType = Type.GetType("BridgeComponent, Assembly-CSharp");
        playerType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        heightFieldType = Type.GetType("LooseSoilHeightField, Assembly-CSharp");
#if UNITY_EDITOR
        bucketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/PortableBucket.prefab");
        playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        foundationPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenFoundationBridgeComponent.prefab");
        soil = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
        looseSoilMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat");
#endif
        Assert.That(new[] { bucketType, siteType, fieldType, volumeType, bridgeType, playerType, heightFieldType }, Has.None.Null);
        Assert.That(bucketPrefab, Is.Not.Null);
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(foundationPrefab, Is.Not.Null);
        Assert.That(soil, Is.Not.Null);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (scope == null) yield break;
        PlayModeTestScope cleanupScope = scope;
        foreach (GameObject root in roots) cleanupScope.Own(root);
        yield return cleanupScope.Cleanup();
        roots.Clear(); ownedTestMice.Clear();
        bucketPrefab = playerPrefab = foundationPrefab = null;
        testMouse = null; testInputSettings = null; focusedInputRoutingUsed = false;
        soil = looseSoilMaterial = null;
        bucketType = siteType = fieldType = volumeType = bridgeType = playerType = heightFieldType = null;
        scope = null;
        cleanupScope.AssertCleanupSucceeded();
    }

    private void UseFocusedGameViewRouting()
    {
        if (focusedInputRoutingUsed) return;
        focusedInputRoutingUsed = true;
        testInputSettings.UseFocusedGameViewRouting();
    }

    private void AddOwnedTestMouse()
    {
        testMouse = InputSystem.AddDevice<Mouse>();
        ownedTestMice.Add(testMouse);
    }

    private void RemoveOwnedTestMice()
    {
        foreach (Mouse mouse in ownedTestMice)
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        testMouse = null;
    }

    private void RestoreInputSettings()
    {
        try { testInputSettings?.Restore(); }
        finally { testInputSettings = null; }
    }

    [UnityTest]
    public IEnumerator ActualRmbNearAimReturnsIntoTheSameEmptyPrePourPitAsTheDumpPoint()
    {
        yield return RunActualRmbPitReturn(aimLocalZ: -1.1f, expectReturn: true);
    }

    [UnityTest]
    public IEnumerator ActualRmbFarAimReturnsToTheSamePitWhenDumpPointIsStillInside()
    {
        yield return RunActualRmbPitReturn(aimLocalZ: 0f, expectReturn: true);
    }

    [UnityTest]
    public IEnumerator ReleasingActualRmbEarlyCancelsTheReturnWithoutTransferringSoil()
    {
        RmbScenario scenario = CreateActiveRmbScenario(.25f);
        yield return InitializeActualRmbInput(scenario);
        float bucketBefore = Units(scenario.bucket);
        double fieldBefore = (double)Prop(Prop(scenario.fixture.field, "HeightField"), "TotalUnitsPrecise");
        var performed = new RmbHoldCompletion();
        var interactionPerformed = playerType.GetEvent("OnInteractionPerformed");
        System.EventHandler callback = (_, _) => performed.count++;
        interactionPerformed.AddEventHandler(scenario.player, callback);
        try
        {
            QueueStage5Mouse(true);
            yield return null;
            Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(true));
            yield return new WaitForSeconds(.2f);
            QueueStage5Mouse(false);
            yield return null;
            yield return new WaitForSeconds(.2f);
        }
        finally
        {
            QueueStage5Mouse(false);
            interactionPerformed.RemoveEventHandler(scenario.player, callback);
        }

        Assert.That(performed.count, Is.Zero, "A physical release before 1.5 seconds does not perform the bucket action.");
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(false));
        Assert.That(Units(scenario.bucket), Is.EqualTo(bucketBefore).Within(.000001f));
        Assert.That((double)Prop(Prop(scenario.fixture.field, "HeightField"), "TotalUnitsPrecise"),
            Is.EqualTo(fieldBefore).Within(.000001d));
    }

    [UnityTest]
    public IEnumerator ActualRmbSuppressesMovingCollectionUntilPhysicalReleaseEvenAfterOneReturn()
    {
        RmbScenario scenario = CreateActiveRmbScenario(.5f);
        yield return InitializeActualRmbInput(scenario);
        yield return new WaitForSeconds(.25f);

        float bucketBefore = Units(scenario.bucket);
        double fieldBefore = (double)Prop(Prop(scenario.fixture.field, "HeightField"), "TotalUnitsPrecise");
        var performed = new RmbHoldCompletion();
        var interactionPerformed = playerType.GetEvent("OnInteractionPerformed");
        System.EventHandler callback = (_, _) => performed.count++;
        interactionPerformed.AddEventHandler(scenario.player, callback);
        try
        {
            QueueStage5Mouse(true);
            yield return null;
            Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(true));
            float deadline = Time.realtimeSinceStartup + 4f;
            while (performed.count == 0 && Time.realtimeSinceStartup < deadline)
            {
                MoveRmbScenario(scenario, .45f);
                yield return null;
            }
            Assert.That(performed.count, Is.EqualTo(1), "The held RMB performs exactly one return.");
            Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(false),
                "Completion ends the UI hold while the physical RMB remains down.");

            float bucketAfterReturn = Units(scenario.bucket);
            double fieldAfterReturn = (double)Prop(Prop(scenario.fixture.field, "HeightField"), "TotalUnitsPrecise");
            Assert.That(bucketAfterReturn, Is.InRange(.000001f, bucketBefore - .000001f),
                "The real aimed return accepts part of this setup amount and leaves soil available to collect later.");
            Assert.That(fieldAfterReturn, Is.GreaterThan(fieldBefore));
            Assert.That(bucketBefore - bucketAfterReturn,
                Is.EqualTo(fieldAfterReturn - fieldBefore).Within(.00001d), "The accepted return conserves soil.");

            yield return MoveRmbScenarioFor(scenario, .4f);
            Assert.That(performed.count, Is.EqualTo(1), "Keeping RMB pressed does not repeat the return.");
            Assert.That(Units(scenario.bucket), Is.EqualTo(bucketAfterReturn).Within(.000001f),
                "Movement cannot auto-collect before physical release, even though the UI hold completed.");
            Assert.That((double)Prop(Prop(scenario.fixture.field, "HeightField"), "TotalUnitsPrecise"),
                Is.EqualTo(fieldAfterReturn).Within(.000001d));

            QueueStage5Mouse(false);
            yield return null;
            yield return MoveRmbScenarioFor(scenario, .45f);
            float bucketAfterRelease = Units(scenario.bucket);
            double fieldAfterRelease = (double)Prop(Prop(scenario.fixture.field, "HeightField"), "TotalUnitsPrecise");
            Assert.That(bucketAfterRelease, Is.GreaterThan(bucketAfterReturn + .05f),
                "Moving collection resumes after actual RMB release.");
            Assert.That(fieldAfterRelease, Is.LessThan(fieldAfterReturn));
            Assert.That(bucketAfterRelease - bucketAfterReturn,
                Is.EqualTo(fieldAfterReturn - fieldAfterRelease).Within(.00002d),
                "Resumed collection conserves the amount removed from the field.");
        }
        finally
        {
            QueueStage5Mouse(false);
            interactionPerformed.RemoveEventHandler(scenario.player, callback);
        }
    }

    [UnityTest]
    public IEnumerator UiPlacementAndInteractionDisableCancelPhysicalBucketIntent()
    {
        RmbScenario scenario = CreateActiveRmbScenario(.25f);
        yield return InitializeActualRmbInput(scenario);
        Type inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        Component playerInput = scenario.playerObject.GetComponent(inputType);

        QueueStage5Mouse(true);
        yield return null;
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(true));
        Call(playerInput, "SetGameplayUiOpen", true);
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(false));
        yield return MoveRmbScenarioFor(scenario, .35f);
        Assert.That(Units(scenario.bucket), Is.GreaterThan(.25f),
            "Opening gameplay UI cancels the physical action intent and restores collection.");
        Call(playerInput, "SetGameplayUiOpen", false);
        QueueStage5Mouse(false);
        yield return null;

        Call(scenario.bucket, "Empty", false, Vector3.zero);
        Add(scenario.bucket, soil, .25f);
        QueueStage5Mouse(true);
        yield return null;
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(true));
        Call(playerInput, "SetSingleCarryPlacementActive", true);
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(false));
        yield return MoveRmbScenarioFor(scenario, .35f);
        Assert.That(Units(scenario.bucket), Is.GreaterThan(.25f),
            "Entering single-carry placement cancels the intent and restores collection.");
        Call(playerInput, "SetSingleCarryPlacementActive", false);
        QueueStage5Mouse(false);
        yield return null;

        Call(scenario.bucket, "Empty", false, Vector3.zero);
        Add(scenario.bucket, soil, .25f);
        QueueStage5Mouse(true);
        yield return null;
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(true));
        ((Behaviour)scenario.player).enabled = false;
        yield return MoveRmbScenarioFor(scenario, .35f);
        Assert.That(Units(scenario.bucket), Is.GreaterThan(.25f),
            "Disabling the local interaction component clears the intent rather than leaving collection locked.");
        ((Behaviour)scenario.player).enabled = true;
        QueueStage5Mouse(false);
        yield return null;

        Call(scenario.bucket, "Empty", false, Vector3.zero);
        Add(scenario.bucket, soil, .25f);
        var disabledInputCompletion = new RmbHoldCompletion();
        var disabledInputPerformed = playerType.GetEvent("OnInteractionPerformed");
        System.EventHandler disabledInputCallback = (_, _) => disabledInputCompletion.count++;
        disabledInputPerformed.AddEventHandler(scenario.player, disabledInputCallback);
        QueueStage5Mouse(true);
        yield return null;
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(true));
        ((Behaviour)playerInput).enabled = false;
        Assert.That(FieldValue(scenario.bucket, "bucketActionCollectionSuppressed"), Is.EqualTo(false),
            "Disabling PlayerInputNew emits cancellation and clears the actual-input intent.");
        QueueStage5Mouse(false);
        yield return null;
        QueueStage5Mouse(true);
        yield return null;
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(false),
            "A disabled input component cannot start the bucket hold again.");
        yield return new WaitForSeconds(1.6f);
        Assert.That(disabledInputCompletion.count, Is.Zero,
            "Disabling input cancels the UI hold instead of allowing its timer to perform a late return.");
        disabledInputPerformed.RemoveEventHandler(scenario.player, disabledInputCallback);
        yield return MoveRmbScenarioFor(scenario, .35f);
        Assert.That(Units(scenario.bucket), Is.GreaterThan(.25f),
            "Disabling local input while RMB is held does not leave collection locked.");
        ((Behaviour)playerInput).enabled = true;
        yield return null;
        ConfigureVirtualMouse(playerInput);
        QueueStage5Mouse(false);
        yield return null;

        Call(scenario.bucket, "Empty", false, Vector3.zero);
        Add(scenario.bucket, soil, .25f);
        var droppedHoldCompletion = new RmbHoldCompletion();
        var droppedHoldPerformed = playerType.GetEvent("OnInteractionPerformed");
        System.EventHandler droppedHoldCallback = (_, _) => droppedHoldCompletion.count++;
        droppedHoldPerformed.AddEventHandler(scenario.player, droppedHoldCallback);
        QueueStage5Mouse(true);
        yield return null;
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(true));
        Call(scenario.player, "DropHeldObjectForStateChange");
        Hold(scenario.bucket, scenario.playerObject);
        Assert.That(FieldValue(scenario.bucket, "bucketActionCollectionSuppressed"), Is.EqualTo(false),
            "A forced holder change/drop clears the action intent.");
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(false));
        yield return MoveRmbScenarioFor(scenario, .35f);
        Assert.That(Units(scenario.bucket), Is.GreaterThan(.25f),
            "Re-picking the bucket after a forced drop does not inherit a stuck intent.");
        yield return new WaitForSeconds(1.6f);
        Assert.That(droppedHoldCompletion.count, Is.Zero,
            "Dropping and reacquiring the same bucket in one frame cancels the old UI timer.");
        droppedHoldPerformed.RemoveEventHandler(scenario.player, droppedHoldCallback);
        QueueStage5Mouse(false);
        yield return null;

        Call(scenario.bucket, "Empty", false, Vector3.zero);
        Add(scenario.bucket, soil, .25f);
        QueueStage5Mouse(true);
        yield return null;
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(true));
        Component health = scenario.playerObject.GetComponent(Type.GetType("PlayerHealth, Assembly-CSharp"));
        Call(health, "ApplyDamageLocal", 1000f);
        Assert.That(Prop(health, "IsDowned"), Is.EqualTo(true));
        yield return null;
        Assert.That(FieldValue(scenario.bucket, "bucketActionCollectionSuppressed"), Is.EqualTo(false),
            "Downing the holder clears its physical intent before/with the forced drop.");
        Assert.That(Prop(scenario.player, "IsBucketActionHoldActive"), Is.EqualTo(false));
        QueueStage5Mouse(false);
        yield return null;
    }

    private IEnumerator RunActualRmbPitReturn(float aimLocalZ, bool expectReturn)
    {
        SiteFixture fixture = CreateSite(2, 18f, false);
        Set(fixture.site, "currentStage", EnumValue("BridgeConstructionStage", "ReadyForMount"));
        Set(fixture.site, "diggingCycleIndex", 2);
        Call(fixture.site, "ApplyVisualState");
        Assert.That((double)Prop(Prop(fixture.field, "HeightField"), "TotalUnitsPrecise"), Is.Zero,
            "An 18-unit ReadyForMount site is the legal, empty pre-pour pit state.");

        Vector3 floorPoint = fixture.field.transform.TransformPoint(Vector3.zero);
        Component bucket = CreateBucket();
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        Type firstPersonType = Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp");
        Component firstPerson = playerObject.GetComponent(firstPersonType);
        Assert.That(firstPerson, Is.Not.Null);
        GameObject cameraTargetObject = (GameObject)FieldValue(firstPerson, "CinemachineCameraTarget");
        Assert.That(cameraTargetObject, Is.Not.Null);
        Transform cameraTarget = cameraTargetObject.transform;

        Vector3 localBody = new Vector3(0f, 1.2f, -2.2f);
        playerObject.transform.SetPositionAndRotation(
            fixture.field.transform.TransformPoint(localBody), fixture.field.transform.rotation);
        GameObject cameraObject = new GameObject("Stage5 actual viewport aim camera");
        TrackRoot(cameraObject);
        cameraObject.transform.SetParent(cameraTarget, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = .2f;
        Vector3 aimPoint = fixture.field.transform.TransformPoint(new Vector3(0f, 0f, aimLocalZ));
        cameraObject.transform.rotation = Quaternion.LookRotation(aimPoint - cameraObject.transform.position, Vector3.up);
        player.GetType().GetMethod("SetInteractionOrigin").Invoke(player, new object[] { cameraTarget });
        player.GetType().GetMethod("SetAimCamera").Invoke(player, new object[] { camera });
        Hold(bucket, playerObject);

        // Populate the bucket from a separate real pile so setup material transfer is conserved.
        Component pilePrefab = (Component)FieldValue(bucket, "loosePilePrefab");
        Assert.That(pilePrefab, Is.Not.Null);
        GameObject sourceObject = Object.Instantiate(pilePrefab.gameObject,
            fixture.field.transform.TransformPoint(new Vector3(-1f, 0f, -2.2f)), Quaternion.identity);
        TrackRoot(sourceObject);
        Component sourcePile = sourceObject.GetComponent(Type.GetType("LooseSubstancePile, Assembly-CSharp"));
        Call(sourcePile, "Initialize", soil, .25f);
        Call(bucket, "RequestContextAction", player, sourcePile);
        Assert.That(Units(bucket), Is.EqualTo(.25f).Within(.000001f), "The bucket receives only the setup pile's soil.");
        float remainingSetupPile = sourcePile != null ? (float)Prop(sourcePile, "CurrentUnits") : 0f;
        Assert.That(remainingSetupPile, Is.Zero, "The setup source loses the same amount the bucket gains.");

        Component playerInput = playerObject.GetComponent(Type.GetType("PlayerInputNew, Assembly-CSharp"));
        Assert.That(playerInput, Is.Not.Null);
        UseFocusedGameViewRouting();
        AddOwnedTestMouse();
        ((Behaviour)playerInput).enabled = true;
        yield return null;
        ConfigureVirtualMouse(playerInput);

        object[] rayArgs = { default(Ray) };
        Assert.That((bool)playerType.GetMethod("TryGetAimRay", Instance, null,
            new[] { typeof(Ray).MakeByRefType() }, null).Invoke(player, rayArgs), Is.True);
        Ray actualRay = (Ray)rayArgs[0];
        object[] pitArgs = { actualRay, float.MaxValue, null, null, Vector3.zero, 0f };
        Assert.That((bool)fieldType.GetMethod("TryFindAimedPit", BindingFlags.Static | BindingFlags.Public)
            .Invoke(null, pitArgs), Is.True);
        Assert.That(pitArgs[2], Is.SameAs(fixture.field));
        Assert.That(pitArgs[3], Is.SameAs(fixture.site));
        float aimDistance = (float)pitArgs[5];
        Vector3 dumpPoint = DumpPoint(playerObject);
        Vector3 closestPitPoint = (Vector3)bucketType.GetMethod("GetClosestPitSupportPoint", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { fixture.field, playerObject.transform.position });
        float pitProximity = Vector3.Distance(playerObject.transform.position, closestPitPoint);
        bool dumpInsidePit = (bool)Call(fixture.field, "ContainsHorizontalFootprint", dumpPoint);
        Assert.That(dumpInsidePit, Is.True, "Both gaze targets keep the authoritative forward dump column inside this same pit.");
        Vector3 localDump = fixture.field.transform.InverseTransformPoint(dumpPoint);
        object heightField = Prop(fixture.field, "HeightField");
        double localCapacity = (double)Call(heightField, "GetAdditionCapacity", localDump.x, localDump.z,
            (float)Prop(fixture.field, "ReturnRadius"), 1.2f);
        bool rayValid = (bool)bucketType.GetMethod("IsValidBucketAimRay", Instance)
            .Invoke(bucket, new object[] { player, actualRay });
        bool clearPath = (bool)bucketType.GetMethod("HasClearAimPath", Instance)
            .Invoke(bucket, new object[] { player, fixture.site, actualRay, aimDistance });
        float sourceBefore = sourcePile != null ? (float)Prop(sourcePile, "CurrentUnits") : 0f;
        float bucketBefore = Units(bucket);
        double fieldBefore = (double)Prop(heightField, "TotalUnitsPrecise");
        HashSet<int> pilesBefore = CapturePileIds();
        Set(player, "_currentTarget", fixture.site);

        var holdCompletion = new RmbHoldCompletion();
        yield return PerformActualRmbHold(player, holdCompletion);

        double fieldAfter = (double)Prop(Prop(fixture.field, "HeightField"), "TotalUnitsPrecise");
        float bucketAfter = Units(bucket);
        float sourceAfter = sourcePile != null ? (float)Prop(sourcePile, "CurrentUnits") : 0f;
        string evidence = $"aimLocalZ={aimLocalZ:F2}, aimDistance={aimDistance:F3}, rayValid={rayValid}, " +
            $"clearPath={clearPath}, dumpInsidePit={dumpInsidePit}, localCapacity={localCapacity:F5}, " +
            $"closestPitPoint={closestPitPoint}, pitProximity={pitProximity:F3}, " +
            $"stage={Prop(fixture.site, "CurrentStage")}, substage={Prop(fixture.site, "DiggingSubstage")}, " +
            $"removed={Prop(fixture.site, "RemovedSoilUnits")}, source={sourceBefore:F3}->{sourceAfter:F3}, " +
            $"bucket={bucketBefore:F3}->{bucketAfter:F3}, field={fieldBefore:F5}->{fieldAfter:F5}, " +
            $"holdElapsed={holdCompletion.elapsed:F3}s, rayOrigin={actualRay.origin}, anchor={cameraTarget.position}, " +
            $"player={playerObject.transform.position}, dump={dumpPoint}.";
        if (expectReturn)
        {
            Assert.That(bucketAfter, Is.LessThan(bucketBefore), "Expected an in-pit dump to return soil for this valid gaze. " + evidence);
            Assert.That(fieldAfter, Is.GreaterThan(fieldBefore));
            Assert.That(bucketBefore - bucketAfter, Is.EqualTo(fieldAfter - fieldBefore).Within(.00001d));
        }
        else
        {
            Assert.That(bucketAfter, Is.EqualTo(bucketBefore).Within(.000001f), "Far gaze preserves soil when only the aim hit exceeds interaction range. " + evidence);
            Assert.That(fieldAfter, Is.EqualTo(fieldBefore).Within(.000001d));
        }
        Assert.That(sourceAfter, Is.EqualTo(sourceBefore).Within(.000001f), "No soil reappears in the consumed setup pile.");
        Assert.That(CaptureNewPiles(pilesBefore), Is.Empty, "Pit return/rejection never spawns a fallback pile.");
        Assert.That(holdCompletion.elapsed, Is.GreaterThanOrEqualTo((float)Prop(bucket, "ActionHoldDuration") - .01f));
    }

    private IEnumerator PerformActualRmbHold(Component player, RmbHoldCompletion completion)
    {
        var interactionPerformed = playerType.GetEvent("OnInteractionPerformed");
        System.EventHandler callback = (_, _) => completion.count++;
        interactionPerformed.AddEventHandler(player, callback);
        float startedAt = Time.unscaledTime;
        try
        {
            QueueStage5Mouse(true);
            yield return null;
            Assert.That(Prop(player, "IsBucketActionHoldActive"), Is.EqualTo(true),
                "The real generated ActionAlt binding starts the bucket hold.");
            float deadline = Time.realtimeSinceStartup + 4f;
            while (completion.count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            completion.elapsed = Time.unscaledTime - startedAt;
            Assert.That(completion.count, Is.EqualTo(1), "The hold completed exactly once.");
            Assert.That(Prop(player, "IsBucketActionHoldActive"), Is.EqualTo(false));
        }
        finally
        {
            QueueStage5Mouse(false);
            interactionPerformed.RemoveEventHandler(player, callback);
        }
        yield return null;
    }

    private RmbScenario CreateActiveRmbScenario(float bucketUnits)
    {
        SiteFixture fixture = CreateSite(1, 6f, true);
        Component bucket = CreateBucket();
        Add(bucket, soil, bucketUnits);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        playerObject.transform.SetPositionAndRotation(
            fixture.field.transform.TransformPoint(new Vector3(0f, .08f, -1.1f)), fixture.field.transform.rotation);
        CharacterController capsule = playerObject.GetComponent<CharacterController>();
        if (capsule != null) capsule.enabled = false;

        Type firstPersonType = Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp");
        Component firstPerson = playerObject.GetComponent(firstPersonType);
        GameObject cameraTarget = (GameObject)FieldValue(firstPerson, "CinemachineCameraTarget");
        GameObject cameraObject = new GameObject("Stage5 actual held-input camera");
        TrackRoot(cameraObject);
        cameraObject.transform.SetParent(cameraTarget.transform, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = .2f;
        Vector3 aimPoint = fixture.field.transform.TransformPoint(new Vector3(0f, .4f, 0f));
        cameraObject.transform.rotation = Quaternion.LookRotation(aimPoint - cameraObject.transform.position, Vector3.up);
        player.GetType().GetMethod("SetInteractionOrigin").Invoke(player, new object[] { cameraTarget.transform });
        player.GetType().GetMethod("SetAimCamera").Invoke(player, new object[] { camera });
        Hold(bucket, playerObject);

        Type inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        Component playerInput = playerObject.GetComponent(inputType);
        UseFocusedGameViewRouting();
        AddOwnedTestMouse();
        ((Behaviour)playerInput).enabled = true;
        Set(player, "_currentTarget", fixture.site);
        return new RmbScenario(fixture, bucket, playerObject, player, playerInput);
    }

    private IEnumerator InitializeActualRmbInput(RmbScenario scenario)
    {
        yield return null;
        ConfigureVirtualMouse(scenario.playerInput);
    }

    private IEnumerator InitializeControlledPlayerInput(GameObject playerObject)
    {
        Component playerInput = playerObject.GetComponent(Type.GetType("PlayerInputNew, Assembly-CSharp"));
        UseFocusedGameViewRouting();
        AddOwnedTestMouse();
        ((Behaviour)playerInput).enabled = true;
        yield return null;
        ConfigureVirtualMouse(playerInput);
    }

    private static void MoveRmbScenario(RmbScenario scenario, float metersPerSecond)
    {
        scenario.playerObject.transform.position += Vector3.right * (metersPerSecond * Time.deltaTime);
    }

    private IEnumerator MoveRmbScenarioFor(RmbScenario scenario, float duration)
    {
        float end = Time.realtimeSinceStartup + duration;
        while (Time.realtimeSinceStartup < end)
        {
            MoveRmbScenario(scenario, .45f);
            yield return null;
        }
    }

    private void ConfigureVirtualMouse(Component playerInput)
    {
        object wrapper = FieldValue(playerInput, "playerGameInputActions");
        Assert.That(wrapper, Is.Not.Null, "PlayerInputNew initialized its generated action asset.");
        var asset = (InputActionAsset)wrapper.GetType().GetProperty("asset", BindingFlags.Public | BindingFlags.Instance).GetValue(wrapper);
        asset.devices = new InputDevice[] { testMouse };
        InputSystem.Update();
        object game = wrapper.GetType().GetProperty("Game", BindingFlags.Public | BindingFlags.Instance).GetValue(wrapper);
        var alt = (InputAction)game.GetType().GetProperty("ActionAlt", BindingFlags.Public | BindingFlags.Instance).GetValue(game);
        Assert.That(alt.enabled, Is.True);
        bool boundToFixtureMouse = false;
        foreach (InputControl control in alt.controls)
            if (ReferenceEquals(control.device, testMouse)) boundToFixtureMouse = true;
        Assert.That(boundToFixtureMouse, Is.True, "Generated ActionAlt is bound to the fixture's virtual mouse.");
    }

    private void QueueStage5Mouse(bool rightPressed)
    {
        MouseState state = default;
        state = state.WithButton(MouseButton.Right, rightPressed);
        InputSystem.QueueStateEvent(testMouse, state);
        InputSystem.Update();
    }

    [UnityTest]
    public IEnumerator AimedSamePitPartiallyReturnsSoilWithoutSpawningPile()
    {
        SiteFixture fixture = CreateSite(1, 6f, true);
        Component bucket = CreateBucket();
        Add(bucket, soil, .5f);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        Hold(bucket, playerObject);
        Ray ray = AimAtSurface(fixture, playerObject, Vector3.zero);
        Vector3 dumpPoint = DumpPoint(playerObject);
        Assert.That((bool)Call(fixture.field, "ContainsHorizontalFootprint", dumpPoint), Is.True);

        object model = Prop(fixture.field, "HeightField");
        double fieldBefore = (double)Prop(model, "TotalUnitsPrecise");
        float bucketBefore = Units(bucket);
        HashSet<int> pilesBefore = CapturePileIds();
        Call(bucket, "RequestContextActionWithAimRay", player, null, ray);
        Assert.That(CaptureNewPiles(pilesBefore), Is.Empty, "A successful pit return does not spawn a fallback pile.");
        yield return null;

        double fieldAfter = (double)Prop(Prop(fixture.field, "HeightField"), "TotalUnitsPrecise");
        float bucketAfter = Units(bucket);
        Assert.That(fieldAfter, Is.GreaterThan(fieldBefore));
        Assert.That(bucketAfter, Is.LessThan(bucketBefore), "Only the amount represented by the field is debited.");
        Assert.That(bucketAfter, Is.GreaterThan(0f), "Excess soil remains in the bucket after a local partial return.");
        Assert.That(bucketBefore - bucketAfter, Is.EqualTo((double)fieldAfter - fieldBefore).Within(.00001d));
    }

    [UnityTest]
    public IEnumerator LoosePilePartialReturnRetriesWithoutZeroAcceptanceLogSpam()
    {
        SiteFixture fixture = CreateSite(2, 18f, false);
        Set(fixture.site, "currentStage", EnumValue("BridgeConstructionStage", "ReadyForMount"));
        Set(fixture.site, "diggingCycleIndex", 2);
        Call(fixture.site, "ApplyVisualState");
        Component templateBucket = CreateBucket();
        Component pilePrefab = (Component)FieldValue(templateBucket, "loosePilePrefab");
        Assert.That(pilePrefab, Is.Not.Null);
        Vector3 returnPosition = fixture.field.transform.TransformPoint(Vector3.zero);
        GameObject pileObject = Object.Instantiate(pilePrefab.gameObject, returnPosition, Quaternion.identity);
        TrackRoot(pileObject);
        Component pile = pileObject.GetComponent(Type.GetType("LooseSubstancePile, Assembly-CSharp"));
        Call(pile, "Initialize", soil, 3f);

        object field = Prop(fixture.field, "HeightField");
        double fieldBefore = (double)Prop(field, "TotalUnitsPrecise");
        const float sourceBefore = 3f;
        List<string> pileReturnLogs = new();
        Application.LogCallback logHandler = (string message, string stackTrace, LogType type) =>
        {
            if (message.StartsWith("[LooseSubstancePile]", StringComparison.Ordinal)) pileReturnLogs.Add(message);
        };
        Application.logMessageReceived += logHandler;
        try
        {
            float acceptedFirst = (float)Call(pile, "TryReturnTo", fixture.site);
            float pileAfterFirst = (float)Prop(pile, "CurrentUnits");
            double fieldAfterFirst = (double)Prop(Prop(fixture.field, "HeightField"), "TotalUnitsPrecise");
            Assert.That(acceptedFirst, Is.GreaterThan(0f), "The physical pile fills the local hard-capacity first.");
            Assert.That(pileAfterFirst, Is.GreaterThan(0f).And.LessThan(sourceBefore),
                "Only the locally accepted portion leaves the pile.");
            Assert.That(sourceBefore - pileAfterFirst,
                Is.EqualTo(fieldAfterFirst - fieldBefore).Within(.00002d), "The partial return conserves soil.");
            Assert.That(pileReturnLogs, Has.Count.EqualTo(1), "An accepted physical transfer retains its transaction log.");

            object fullFieldSnapshot = Call(Prop(fixture.field, "HeightField"), "CaptureSnapshot");
            float rejectedFirst = (float)Call(pile, "TryReturnTo", fixture.site);
            float rejectedSecond = (float)Call(pile, "TryReturnTo", fixture.site);
            Assert.That(rejectedFirst, Is.Zero);
            Assert.That(rejectedSecond, Is.Zero);
            Assert.That((float)Prop(pile, "CurrentUnits"), Is.EqualTo(pileAfterFirst).Within(.000001f));
            Assert.That((bool)Call(Prop(fixture.field, "HeightField"), "MatchesSnapshot", fullFieldSnapshot), Is.True,
                "Repeated full-capacity retries preserve the exact field snapshot.");
            Assert.That(pileReturnLogs, Has.Count.EqualTo(1),
                "Zero-acceptance retries from the trigger stay quiet while preserving accepted-transfer diagnostics.");

            Component collector = CreateBucket();
            float collected = (float)Call(fixture.site, "TryCollectLooseSoil", collector, soil,
                returnPosition, .6f, .25f, 0f);
            double fieldAfterCollect = (double)Prop(Prop(fixture.field, "HeightField"), "TotalUnitsPrecise");
            Assert.That(collected, Is.GreaterThan(0f), "A real collection transfer frees local room.");
            Assert.That(Units(collector), Is.EqualTo(collected).Within(.00001f));
            Assert.That(fieldAfterFirst - fieldAfterCollect, Is.EqualTo(collected).Within(.00002d));

            float pileBeforeRetry = (float)Prop(pile, "CurrentUnits");
            float acceptedAgain = (float)Call(pile, "TryReturnTo", fixture.site);
            float pileAfterRetry = (float)Prop(pile, "CurrentUnits");
            double fieldAfterRetry = (double)Prop(Prop(fixture.field, "HeightField"), "TotalUnitsPrecise");
            Assert.That(acceptedAgain, Is.GreaterThan(0f), "Retrying after collection accepts newly freed capacity.");
            Assert.That(pileBeforeRetry - pileAfterRetry,
                Is.EqualTo(fieldAfterRetry - fieldAfterCollect).Within(.00002d), "The accepted retry conserves soil.");
            Assert.That(pileReturnLogs, Has.Count.EqualTo(2), "The retry logs its positive accepted transfer.");
        }
        finally
        {
            Application.logMessageReceived -= logHandler;
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator FullAimedPitRejectsWithoutChangingBucketOrFieldOrSpawningPile()
    {
        SiteFixture fixture = CreateSite(0, 0f, false);
        Component bucket = CreateBucket();
        Add(bucket, soil, .25f);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        Hold(bucket, playerObject);
        Ray ray = AimAtSurface(fixture, playerObject, Vector3.zero);
        Assert.That(Prop(fixture.field, "IsSurfaceActive"), Is.EqualTo(false));
        object[] pitQuery = { ray, float.MaxValue, null, null, Vector3.zero, 0f };
        Assert.That((bool)fieldType.GetMethod("TryFindAimedPit", BindingFlags.Static | BindingFlags.Public)
            .Invoke(null, pitQuery), Is.True, "Semantic pit targeting remains available with the loose-surface renderer inactive.");
        Assert.That(pitQuery[2], Is.SameAs(fixture.field));
        object model = Prop(fixture.field, "HeightField");
        object before = Call(model, "CaptureSnapshot");
        float bucketBefore = Units(bucket);
        Vector3 dumpPoint = DumpPoint(playerObject);
        HashSet<int> pilesBefore = CapturePileIds();

        Call(bucket, "RequestContextActionWithAimRay", player, null, ray);
        Assert.That(CaptureNewPiles(pilesBefore), Is.Empty,
            "An aimed-pit rejection must not fall back to spawning a pile in that pit.");
        yield return null;

        Assert.That(Units(bucket), Is.EqualTo(bucketBefore));
        Assert.That((bool)Call(Prop(fixture.field, "HeightField"), "MatchesSnapshot", before), Is.True,
            "Rejected full-pit returns preserve the exact field shape.");
    }

    [UnityTest]
    public IEnumerator BlockedAndOutOfRangePitAimsKeepBucketContents()
    {
        SiteFixture fixture = CreateSite(1, 6f, true);
        Component bucket = CreateBucket();
        Add(bucket, soil, .25f);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        Hold(bucket, playerObject);
        Ray ray = AimAtSurface(fixture, playerObject, Vector3.zero);
        Vector3 dumpPoint = DumpPoint(playerObject);
        object model = Prop(fixture.field, "HeightField");
        object before = Call(model, "CaptureSnapshot");
        HashSet<int> pilesBefore = CapturePileIds();

        GameObject blocker = new GameObject("Stage5 return aim blocker");
        TrackRoot(blocker);
        blocker.transform.SetParent(fixture.volume.transform, true);
        blocker.transform.position = ray.origin + ray.direction * .2f;
        blocker.AddComponent<BoxCollider>().size = Vector3.one * .2f;
        Call(bucket, "RequestContextActionWithAimRay", player, null, ray);
        Assert.That(CaptureNewPiles(pilesBefore), Is.Empty);
        Assert.That(Units(bucket), Is.EqualTo(.25f).Within(.000001f));
        Assert.That((bool)Call(Prop(fixture.field, "HeightField"), "MatchesSnapshot", before), Is.True);
        pilesBefore = CapturePileIds();
        Object.Destroy(blocker);

        Vector3 surface = ((Component)fixture.field).transform.TransformPoint(new Vector3(0f, .4f, 0f));
        playerObject.transform.position = surface + Vector3.up * 3f - Vector3.forward * 1.1f;
        Vector3 farAimOrigin = playerObject.transform.position + Vector3.up * .8f;
        SetAimAnchor(player, farAimOrigin);
        ray = new Ray(farAimOrigin, (surface - farAimOrigin).normalized);
        dumpPoint = DumpPoint(playerObject);
        before = Call(Prop(fixture.field, "HeightField"), "CaptureSnapshot");
        pilesBefore = CapturePileIds();
        Call(bucket, "RequestContextActionWithAimRay", player, null, ray);
        Assert.That(CaptureNewPiles(pilesBefore), Is.Empty, "Over-range pit aims do not spawn fallback piles.");
        yield return null;

        Assert.That(Units(bucket), Is.EqualTo(.25f).Within(.000001f), "An over-range pit aim is rejected.");
        Assert.That((bool)Call(Prop(fixture.field, "HeightField"), "MatchesSnapshot", before), Is.True);
    }

    [UnityTest]
    public IEnumerator HoverDistanceRemainsBoundedForActiveAndReadyForMountPits()
    {
        SiteFixture activePit = CreateSite(1, 6f, false);
        SiteFixture hiddenReadyForMountPit = CreateSite(2, 18f, false);
        ((Component)hiddenReadyForMountPit.site).transform.root.position += Vector3.right * 8f;
        Set(hiddenReadyForMountPit.site, "currentStage", EnumValue("BridgeConstructionStage", "ReadyForMount"));
        Call(hiddenReadyForMountPit.site, "ApplyVisualState");
        Assert.That(Prop(hiddenReadyForMountPit.field, "IsSurfaceActive"), Is.False);

        Component bucket = CreateBucket();
        Add(bucket, soil, .25f);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        Hold(bucket, playerObject);

        foreach (SiteFixture fixture in new[] { activePit, hiddenReadyForMountPit })
        {
            Transform fieldTransform = ((Component)fixture.field).transform;
            playerObject.transform.SetPositionAndRotation(
                fieldTransform.TransformPoint(new Vector3(0f, 5f, -1.1f)), Quaternion.identity);
            Vector3 dumpPoint = DumpPoint(playerObject);
            bool dumpInsidePit = (bool)Call(fixture.field, "ContainsHorizontalFootprint", dumpPoint);
            Assert.That(dumpInsidePit, Is.True);
            object heightField = Prop(fixture.field, "HeightField");
            Vector3 closestPoint = (Vector3)bucketType.GetMethod("GetClosestPitSupportPoint", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { fixture.field, playerObject.transform.position });
            float pitProximity = Vector3.Distance(playerObject.transform.position, closestPoint);
            float rangeLimit = (float)FieldValue(bucket, "interactionDistance");
            Vector3 floorPoint = fieldTransform.TransformPoint(Vector3.zero);
            Vector3 aimOrigin = playerObject.transform.position + Vector3.up * .8f;
            SetAimAnchor(player, aimOrigin);
            Ray ray = new Ray(aimOrigin, (floorPoint - aimOrigin).normalized);

            object[] pitArgs = { ray, float.MaxValue, null, null, Vector3.zero, 0f };
            Assert.That((bool)fieldType.GetMethod("TryFindAimedPit", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, pitArgs), Is.True);
            Assert.That(pitArgs[2], Is.SameAs(fixture.field));
            Assert.That(pitArgs[3], Is.SameAs(fixture.site));
            float aimDistance = (float)pitArgs[5];
            bool rayValid = (bool)bucketType.GetMethod("IsValidBucketAimRay", Instance)
                .Invoke(bucket, new object[] { player, ray });
            bool clearPath = (bool)bucketType.GetMethod("HasClearAimPath", Instance)
                .Invoke(bucket, new object[] { player, fixture.site, ray, aimDistance });
            Vector3 localDump = fieldTransform.InverseTransformPoint(dumpPoint);
            double localCapacity = (double)Call(heightField, "GetAdditionCapacity", localDump.x, localDump.z,
                (float)Prop(fixture.field, "ReturnRadius"), 1.2f);
            string evidence = $"stage={Prop(fixture.site, "CurrentStage")}, surfaceActive={Prop(fixture.field, "IsSurfaceActive")}, " +
                $"rayValid={rayValid}, clearPath={clearPath}, dumpInsidePit={dumpInsidePit}, localCapacity={localCapacity:F5}, " +
                $"pitProximity={pitProximity:F3}, rangeLimit={rangeLimit:F3}, aimDistance={aimDistance:F3}.";
            Assert.That(rayValid, Is.True, evidence);
            Assert.That(clearPath, Is.True, evidence);
            Assert.That(localCapacity, Is.GreaterThan(0d), evidence);
            Assert.That(pitProximity, Is.GreaterThan(rangeLimit), evidence);

            object before = Call(heightField, "CaptureSnapshot");
            HashSet<int> pilesBefore = CapturePileIds();
            Call(bucket, "RequestContextActionWithAimRay", player, null, ray);
            yield return null;
            Assert.That(Units(bucket), Is.EqualTo(.25f).Within(.000001f), evidence);
            Assert.That((bool)Call(Prop(fixture.field, "HeightField"), "MatchesSnapshot", before), Is.True, evidence);
            Assert.That(CaptureNewPiles(pilesBefore), Is.Empty, evidence);
        }
    }
    [UnityTest]
    public IEnumerator AimingAtOnePitCannotDumpIntoASecondPit()
    {
        SiteFixture aimedPit = CreateSite(1, 6f, true);
        SiteFixture dumpPit = CreateSite(1, 6f, true);
        ((Component)dumpPit.site).transform.position += Vector3.right * 8f;
        Component bucket = CreateBucket();
        Add(bucket, soil, .25f);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        Hold(bucket, playerObject);

        Vector3 dumpTarget = ((Component)dumpPit.field).transform.TransformPoint(new Vector3(0f, .4f, 0f));
        playerObject.transform.position = dumpTarget - Vector3.up * .25f - Vector3.forward * 1.1f;
        Vector3 aimOrigin = playerObject.transform.position + Vector3.up * .8f;
        SetAimAnchor(player, aimOrigin);
        Vector3 aimedTarget = ((Component)aimedPit.field).transform.TransformPoint(new Vector3(0f, .4f, 0f));
        Ray ray = new Ray(aimOrigin, (aimedTarget - aimOrigin).normalized);
        object aimedBefore = Call(Prop(aimedPit.field, "HeightField"), "CaptureSnapshot");
        object dumpBefore = Call(Prop(dumpPit.field, "HeightField"), "CaptureSnapshot");
        Vector3 dumpPoint = DumpPoint(playerObject);
        HashSet<int> pilesBefore = CapturePileIds();

        Call(bucket, "RequestContextActionWithAimRay", player, null, ray);
        Assert.That(CaptureNewPiles(pilesBefore), Is.Empty,
            "A valid ray at pit A paired with a forward dump in pit B must keep the source and not spawn in pit B.");
        yield return null;

        Assert.That(Units(bucket), Is.EqualTo(.25f).Within(.000001f));
        Assert.That((bool)Call(Prop(aimedPit.field, "HeightField"), "MatchesSnapshot", aimedBefore), Is.True);
        Assert.That((bool)Call(Prop(dumpPit.field, "HeightField"), "MatchesSnapshot", dumpBefore), Is.True);
    }

    [UnityTest]
    public IEnumerator BucketHoldKeepsSamePitWhenRawTargetChangesAcrossBranches()
    {
        SiteFixture fixture = CreateSite(1, 6f, true);
        Component bucket = CreateBucket();
        Add(bucket, soil, .25f);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        Hold(bucket, playerObject);
        Vector3 target = ((Component)fixture.field).transform.TransformPoint(new Vector3(0f, .4f, 0f));
        GameObject cameraObject = new GameObject("Stage5 semantic pit aim camera");
        TrackRoot(cameraObject);
        cameraObject.transform.position = target + Vector3.up * 1f - Vector3.forward * 1.1f;
        cameraObject.transform.rotation = Quaternion.LookRotation(target - cameraObject.transform.position);
        Set(player, "aimCamera", cameraObject.AddComponent<Camera>());
        yield return InitializeControlledPlayerInput(playerObject);

        Set(player, "_currentTarget", fixture.site);
        Call(player, "BeginBucketActionHold", bucket, fixture.site);
        Assert.That(Prop(player, "IsBucketActionHoldActive"), Is.EqualTo(true));
        Set(player, "_currentTarget", fixture.volume);
        Call(player, "UpdateBucketActionHold");
        yield return new WaitForSeconds(.11f);
        Call(player, "UpdateBucketActionHold");
        Assert.That(Prop(player, "IsBucketActionHoldActive"), Is.EqualTo(true),
            "Changing from the site collider branch to the separate volume branch of the same pit does not cancel the hold.");
        Assert.That((float)FieldValue(player, "bucketTargetLostAt"), Is.EqualTo(-1f));

        Set(player, "_currentTarget", null);
        Call(player, "UpdateBucketActionHold");
        yield return new WaitForSeconds(.11f);
        Call(player, "UpdateBucketActionHold");
        Assert.That(Prop(player, "IsBucketActionHoldActive"), Is.EqualTo(true),
            "A colliderless raw target still represents the same aimed pit.");
        Assert.That((float)FieldValue(player, "bucketTargetLostAt"), Is.EqualTo(-1f));
        Call(player, "CancelBucketActionHold");
    }

    [UnityTest]
    public IEnumerator ColliderlessHoldCancelsAfterAimLeavesItsPit()
    {
        SiteFixture fixture = CreateSite(1, 6f, true);
        Component bucket = CreateBucket();
        Add(bucket, soil, .25f);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        Hold(bucket, playerObject);
        Vector3 target = ((Component)fixture.field).transform.TransformPoint(new Vector3(0f, .4f, 0f));
        GameObject cameraObject = new GameObject("Stage5 colliderless target loss camera");
        TrackRoot(cameraObject);
        cameraObject.transform.position = target + Vector3.up + Vector3.back;
        cameraObject.transform.rotation = Quaternion.LookRotation(target - cameraObject.transform.position);
        Set(player, "aimCamera", cameraObject.AddComponent<Camera>());
        yield return InitializeControlledPlayerInput(playerObject);
        Set(player, "_currentTarget", null);
        Call(player, "BeginBucketActionHold", bucket, null);
        Assert.That(Prop(player, "IsBucketActionHoldActive"), Is.EqualTo(true));

        cameraObject.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        yield return new WaitForSeconds(.11f);
        Call(player, "UpdateBucketActionHold");
        Assert.That(Prop(player, "IsBucketActionHoldActive"), Is.EqualTo(true),
            "The existing target-loss grace period is retained while the semantic pit target changes.");
        float deadline = Time.unscaledTime + .35f;
        while ((bool)Prop(player, "IsBucketActionHoldActive") && Time.unscaledTime < deadline)
        {
            yield return null;
            Call(player, "UpdateBucketActionHold");
        }
        Assert.That(Prop(player, "IsBucketActionHoldActive"), Is.EqualTo(false),
            "A hold started on a colliderless pit must cancel within the configured grace plus aim-sampling interval.");
    }

    [UnityTest]
    public IEnumerator NonPitPileSourceKeepsPriorityWhenPitIsBehindIt()
    {
        SiteFixture fixture = CreateSite(1, 6f, true);
        Component bucket = CreateBucket();
        Add(bucket, soil, .25f);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        Hold(bucket, playerObject);
        Ray ray = AimAtSurface(fixture, playerObject, Vector3.zero);
        Component pilePrefab = (Component)FieldValue(bucket, "loosePilePrefab");
        Assert.That(pilePrefab, Is.Not.Null);
        GameObject pileObject = Object.Instantiate(pilePrefab.gameObject, ray.origin + ray.direction * .2f, Quaternion.identity);
        TrackRoot(pileObject);
        Component pile = pileObject.GetComponent(Type.GetType("LooseSubstancePile, Assembly-CSharp"));
        Call(pile, "Initialize", soil, 1f);
        object fieldBefore = Call(Prop(fixture.field, "HeightField"), "CaptureSnapshot");
        Assert.That(Call(bucket, "ShouldTrackLooseSoilPitAim", pile), Is.EqualTo(false),
            "A nonpit source remains on its existing target identity even when a pit is behind it.");

        Call(bucket, "RequestContextActionWithAimRay", player, pile, ray);
        yield return null;

        Assert.That(Units(bucket), Is.GreaterThan(0f), "The legacy scoop leaves its excess amount in the bucket.");
        Assert.That((float)Prop(pile, "CurrentUnits"), Is.LessThan(1f), "The in-front pile source retains priority over the pit behind it.");
        Assert.That((bool)Call(Prop(fixture.field, "HeightField"), "MatchesSnapshot", fieldBefore), Is.True,
            "A source-priority scoop must not also alter the pit.");
    }

    [UnityTest]
    public IEnumerator OutsideAimKeepsLegacyPileDumpBehavior()
    {
        SiteFixture fixture = CreateSite(1, 6f, false);
        Component bucket = CreateBucket();
        Add(bucket, soil, .25f);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        playerObject.transform.position = new Vector3(20f, 1f, 20f);
        SetAimAnchor(player, playerObject.transform.position);
        Hold(bucket, playerObject);
        Vector3 dumpPoint = DumpPoint(playerObject);
        Ray outsideRay = new Ray(playerObject.transform.position, Vector3.forward);
        HashSet<int> pilesBefore = CapturePileIds();

        Call(bucket, "RequestContextActionWithAimRay", player, null, outsideRay);
        List<Component> spawned = CaptureNewPiles(pilesBefore);
        Assert.That(spawned, Has.Count.EqualTo(1));
        Assert.That((float)Prop(spawned[0], "CurrentUnits"), Is.EqualTo(.25f).Within(.000001f));
        yield return null;

        Assert.That(Units(bucket), Is.Zero);
        Assert.That(spawned[0] != null, Is.True, "The spawned outside pile must remain a valid source after a physics frame.");
        Assert.That(fixture.field != null, Is.True);
    }

    [UnityTest]
    public IEnumerator TallLooseSurfaceIsAimTargetBeforeHardFloorRange()
    {
        SiteFixture fixture = CreateSite(1, 6f, true);
        Component bucket = CreateBucket();
        Add(bucket, soil, .25f);
        GameObject playerObject = CreatePlayer();
        Component player = playerObject.GetComponent(playerType);
        Hold(bucket, playerObject);
        object model = Prop(fixture.field, "HeightField");
        Assert.That((float)Call(model, "AddWithHeightCeiling", 0f, 0f, 100f, 4.5f, .8f),
            Is.EqualTo(4.5f).Within(.00001f));
        Call(fixture.field, "RebuildMesh");
        Vector3 surfacePoint = ((Component)fixture.field).transform.TransformPoint(new Vector3(0f, .7f, 0f));
        Vector3 hardFloorPoint = ((Component)fixture.field).transform.position;
        playerObject.transform.rotation = Quaternion.identity;
        playerObject.transform.position = surfacePoint - Vector3.up * .25f - Vector3.forward * 1.1f;
        Vector3 aimOrigin = surfacePoint + Vector3.up * .1f - Vector3.forward;
        SetAimAnchor(player, aimOrigin);
        Ray ray = new Ray(aimOrigin, (surfacePoint - aimOrigin).normalized);

        float floorCrossingDistance = (ray.origin.y - hardFloorPoint.y) / -ray.direction.y;
        Assert.That(floorCrossingDistance, Is.GreaterThan(2.5f),
            "The hard-floor crossing is outside interaction range in this regression setup.");
        object[] queryArgs = { ray, 2.5f, null, null, Vector3.zero, 0f };
        bool found = (bool)fieldType.GetMethod("TryFindAimedPit", BindingFlags.Static | BindingFlags.Public)
            .Invoke(null, queryArgs);
        Assert.That(found, Is.True,
            "A ray that reaches visible tall soil within range must resolve the pit without requiring a collider.");
        Assert.That(queryArgs[2], Is.SameAs(fixture.field));
        Assert.That(queryArgs[3], Is.SameAs(fixture.site));
        Vector3 dumpPoint = DumpPoint(playerObject);
        Assert.That((bool)Call(fixture.field, "ContainsHorizontalFootprint", dumpPoint), Is.True);
        float before = Units(bucket);
        Call(bucket, "RequestContextActionWithAimRay", player, null, ray);
        yield return null;
        Assert.That(Units(bucket), Is.LessThan(before));
    }

    private SiteFixture CreateSite(int cycle, float removedUnits, bool surfaceActive)
    {
        GameObject root = Object.Instantiate(foundationPrefab);
        TrackRoot(root);
        Component bridgeBehaviour = root.GetComponentInChildren(bridgeType, true);
        if (bridgeBehaviour is Behaviour bridgeAsBehaviour) bridgeAsBehaviour.enabled = false;
        Component site = root.GetComponentInChildren(siteType, true);
        Assert.That(site, Is.Not.Null);
        Call(site, "Initialize");
        Component bridge = Prop(site, "BridgeComponent") as Component;
        object definition = Call(bridge, "GetBridgeComponentSO");
        object workflow = FieldValue(definition, "constructionWorkflow");
        (Component volume, Component field) = CreateLooseSoilVolume(root.transform, site);

        object stage = EnumValue("BridgeConstructionStage", "Digging");
        object substage = EnumValue("FoundationDiggingSubstage", "SoilRemoval");
        Set(site, "currentStage", stage);
        Set(site, "diggingSubstage", substage);
        Set(site, "diggingCycleIndex", cycle);
        Set(site, "removedSoilUnits", removedUnits);
        Set(site, "currentWorkProgress", 0f);
        Call(volume, "ApplyDiggingState", stage, substage, 0f, removedUnits, workflow, cycle);
        Call(field, "SetSurfaceState", surfaceActive, looseSoilMaterial);
        return new SiteFixture(site, volume, field);
    }

    private (Component volume, Component field) CreateLooseSoilVolume(Transform parent, Component site)
    {
        GameObject volumeRoot = new GameObject("Stage5 separate excavation volume");
        volumeRoot.transform.SetParent(parent, false);
        Component volume = volumeRoot.AddComponent(volumeType);
        GameObject supportRoot = new GameObject("Stage5 hard support");
        supportRoot.transform.SetParent(volumeRoot.transform, false);
        supportRoot.transform.localPosition = new Vector3(0f, .02f, 0f);
        BoxCollider support = supportRoot.AddComponent<BoxCollider>();
        support.size = new Vector3(6.5f, .12f, 4f);
        MeshRenderer supportRenderer = supportRoot.AddComponent<MeshRenderer>();

        GameObject fieldRoot = new GameObject("Stage5 loose soil field", typeof(MeshFilter), typeof(MeshRenderer));
        fieldRoot.transform.SetParent(volumeRoot.transform, false);
        Component field = fieldRoot.AddComponent(fieldType);
        Call(field, "Initialize", 6.5f, 4f, .2f, .4f, .4f, 6f);

        Set(volume, "soilSurface", supportRoot.transform);
        Set(volume, "constructionSite", site);
        Set(volume, "soilRenderer", supportRenderer);
        Set(volume, "loosenedSoilMaterial", looseSoilMaterial);
        Set(volume, "surfaceStartLocalY", .02f);
        Set(volume, "useLooseSoilHeightField", true);
        Set(volume, "looseSoilField", field);
        Set(volume, "soilSupportCollider", support);
        Set(volume, "concreteFootprintSize", new Vector2(6.5f, 4f));
        Set(site, "excavationVolume", volume);
        return (volume, field);
    }

    private Component CreateBucket()
    {
        GameObject go = Object.Instantiate(bucketPrefab);
        TrackRoot(go);
        return go.GetComponent(bucketType);
    }

    private GameObject CreatePlayer()
    {
        GameObject inactiveParent = new GameObject("Stage5 inactive player test parent");
        inactiveParent.SetActive(false);
        TrackRoot(inactiveParent);
        GameObject player = Object.Instantiate(playerPrefab, inactiveParent.transform, false);
        player.name = "Stage5 bucket return holder";
        player.transform.rotation = Quaternion.identity;
        Component interaction = player.GetComponent(playerType);
        Type inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        Type systemInputType = Type.GetType("UnityEngine.InputSystem.PlayerInput, Unity.InputSystem");
        Behaviour gameInput = player.GetComponent(inputType) as Behaviour;
        Behaviour systemInput = player.GetComponent(systemInputType) as Behaviour;
        if (gameInput != null) gameInput.enabled = false;
        if (systemInput != null) systemInput.enabled = false;
        foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour != interaction && behaviour != gameInput && behaviour != systemInput) behaviour.enabled = false;
        inactiveParent.SetActive(true);
        return player;
    }

    private void Hold(Component bucket, GameObject player)
    {
        Component resource = bucket.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"));
        Call(resource, "PickedUp", player.transform);
        Assert.That(Call(player.GetComponent(playerType), "GetPickedUpGameObject"), Is.SameAs(bucket.gameObject));
    }

    private Ray AimAtSurface(SiteFixture fixture, GameObject player, Vector3 localOffset)
    {
        Vector3 target = ((Component)fixture.field).transform.TransformPoint(localOffset + Vector3.up * .4f);
        player.transform.rotation = Quaternion.identity;
        player.transform.position = target + Vector3.up * 1f - Vector3.forward * 1.1f;
        SetAimAnchor(player.GetComponent(playerType), player.transform.position + Vector3.up * .8f);
        Vector3 origin = player.transform.position + Vector3.up * .8f;
        return new Ray(origin, (target - origin).normalized);
    }

    private static Vector3 DumpPoint(GameObject player) =>
        player.transform.position + player.transform.forward * 1.1f + Vector3.up * .25f;

    private static void SetAimAnchor(Component player, Vector3 worldPosition)
    {
        GameObject anchor = new GameObject("Stage5 test aim anchor");
        anchor.transform.SetParent(player.transform, true);
        anchor.transform.position = worldPosition;
        Set(player, "interactionOrigin", anchor.transform);
    }

    private HashSet<int> CapturePileIds()
    {
        HashSet<int> ids = new HashSet<int>();
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (behaviour != null && behaviour.GetType().Name == "LooseSubstancePile")
                ids.Add(behaviour.GetInstanceID());
        return ids;
    }

    private List<Component> CaptureNewPiles(HashSet<int> existingIds)
    {
        List<Component> added = new List<Component>();
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (behaviour == null || behaviour.GetType().Name != "LooseSubstancePile" ||
                existingIds.Contains(behaviour.GetInstanceID())) continue;
            TrackRoot(behaviour.gameObject);
            added.Add(behaviour);
        }
        return added;
    }

    private void TrackRoot(GameObject root)
    {
        if (root == null) return;
        if (!roots.Contains(root)) roots.Add(root);
        scope?.Own(root);
    }

    private static float Add(Component container, Object substance, float amount) =>
        (float)Call(container, "TryAddUnits", substance, amount);
    private static float Units(Component component) => (float)Prop(component, "CurrentUnits");
    private static object EnumValue(string typeName, string value) => System.Enum.Parse(Type.GetType(typeName + ", Assembly-CSharp"), value);
    private static object Prop(object instance, string name) => instance.GetType().GetProperty(name).GetValue(instance);
    private static void Set(object instance, string name, object value) =>
        instance.GetType().GetField(name, Instance).SetValue(instance, value);
    private static object FieldValue(object instance, string name) =>
        instance.GetType().GetField(name, Instance).GetValue(instance);
    private static object Call(object instance, string name, params object[] args)
    {
        Type instanceType = instance as Type;
        Type type = instanceType ?? instance.GetType();
        MethodInfo method = null;
        foreach (MethodInfo candidate in type.GetMethods(Instance | (instanceType != null ? BindingFlags.Static : 0)))
        {
            if (candidate.Name != name || candidate.GetParameters().Length != args.Length) continue;
            if (method != null) throw new AmbiguousMatchException($"Multiple {name} methods accept {args.Length} arguments.");
            method = candidate;
        }
        Assert.That(method, Is.Not.Null, $"Expected method {name}/{args.Length} on {type.Name}.");
        return method.Invoke(instanceType != null ? null : instance, args);
    }

    private readonly struct SiteFixture
    {
        public readonly Component site;
        public readonly Component volume;
        public readonly Component field;
        public SiteFixture(Component site, Component volume, Component field)
        { this.site = site; this.volume = volume; this.field = field; }
    }

    private sealed class RmbScenario
    {
        public readonly SiteFixture fixture;
        public readonly Component bucket;
        public readonly GameObject playerObject;
        public readonly Component player;
        public readonly Component playerInput;

        public RmbScenario(SiteFixture fixture, Component bucket, GameObject playerObject,
            Component player, Component playerInput)
        {
            this.fixture = fixture;
            this.bucket = bucket;
            this.playerObject = playerObject;
            this.player = player;
            this.playerInput = playerInput;
        }
    }

    private sealed class RmbHoldCompletion
    {
        public int count;
        public float elapsed;
    }
}
