using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class LooseSoilCycleLifecycleEditModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private GameObject root;
    private GameObject supportObject;
    private GameObject fieldObject;
    private Type volumeType;
    private Type fieldType;
    private Type workflowType;
    private Type stageType;
    private Type substageType;
    private Type siteType;
    private Type bridgeComponentType;
    private Type containerType;
    private Type substanceType;
    private Type substanceKindType;
    private object workflow;
    private object volume;
    private object field;
    private object site;
    private object bucket;
    private Object substance;
    private Object waterSubstance;
    private GameObject bucketObject;
    private BoxCollider support;
    private BoxCollider exitRampCollider;
    private MeshRenderer exitRampRenderer;
    private Transform soilSurface;

    [SetUp]
    public void SetUp()
    {
        volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        workflowType = Type.GetType("BridgeConstructionWorkflowSO, Assembly-CSharp");
        stageType = Type.GetType("BridgeConstructionStage, Assembly-CSharp");
        substageType = Type.GetType("FoundationDiggingSubstage, Assembly-CSharp");
        siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        bridgeComponentType = Type.GetType("BridgeComponent, Assembly-CSharp");
        containerType = Type.GetType("PortableSubstanceContainer, Assembly-CSharp");
        substanceType = Type.GetType("ContainerSubstanceSO, Assembly-CSharp");
        substanceKindType = Type.GetType("ContainerSubstanceKind, Assembly-CSharp");
        Assert.That(volumeType, Is.Not.Null);
        Assert.That(fieldType, Is.Not.Null);
        Assert.That(workflowType, Is.Not.Null);
        Assert.That(stageType, Is.Not.Null);
        Assert.That(substageType, Is.Not.Null);
        Assert.That(siteType, Is.Not.Null);
        Assert.That(bridgeComponentType, Is.Not.Null);
        Assert.That(containerType, Is.Not.Null);
        Assert.That(substanceType, Is.Not.Null);
        Assert.That(substanceKindType, Is.Not.Null);

        root = new GameObject("Loose soil lifecycle fixture");
        volume = root.AddComponent(volumeType);
        supportObject = new GameObject("Hard support");
        supportObject.transform.SetParent(root.transform, false);
        soilSurface = supportObject.transform;
        support = supportObject.AddComponent<BoxCollider>();
        support.size = new Vector3(6.5f, .2f, 4f);
        fieldObject = new GameObject("Loose surface");
        fieldObject.transform.SetParent(root.transform, false);
        field = fieldObject.AddComponent(fieldType);
        Set(volume, "useLooseSoilHeightField", true);
        Set(volume, "soilSurface", soilSurface);
        Set(volume, "soilSupportCollider", support);
        Set(volume, "looseSoilField", field);
        Set(volume, "surfaceStartLocalY", 0f);
        GameObject rampObject = new GameObject("Exit ramp fixture");
        rampObject.transform.SetParent(root.transform, false);
        exitRampCollider = rampObject.AddComponent<BoxCollider>();
        exitRampRenderer = rampObject.AddComponent<MeshRenderer>();
        Set(volume, "exitRampCollider", exitRampCollider);
        Set(volume, "exitRampRenderer", exitRampRenderer);

        workflow = ScriptableObject.CreateInstance(workflowType);
        Set(workflow, "diggingCycleCount", 3);
        Set(workflow, "soilUnitsPerCycle", 6);
        Set(workflow, "finalExcavationDepth", 1.2f);

        Component bridgeComponent = root.AddComponent(bridgeComponentType);
        site = root.AddComponent(siteType);
        Set(site, "bridgeComponent", bridgeComponent);
        Set(site, "excavationVolume", volume);
        Set(site, "workflow", workflow);

        bucketObject = new GameObject("Loose soil test bucket");
        bucket = bucketObject.AddComponent(containerType);
        substance = ScriptableObject.CreateInstance(substanceType);
        waterSubstance = ScriptableObject.CreateInstance(substanceType);
        Set(waterSubstance, "substanceKind", Enum.Parse(substanceKindType, "Water"));
        Array supported = Array.CreateInstance(substanceType, 2);
        supported.SetValue(substance, 0);
        supported.SetValue(waterSubstance, 1);
        Set(bucket, "supportedSubstances", supported);
    }

    [TearDown]
    public void TearDown()
    {
        if (field != null) Call(field, "ReleaseRuntimeMeshForSceneSave");
        if (root != null) Object.DestroyImmediate(root);
        if (bucketObject != null) Object.DestroyImmediate(bucketObject);
        if (workflow is Object asset) Object.DestroyImmediate(asset);
        if (substance != null) Object.DestroyImmediate(substance);
        if (waterSubstance != null) Object.DestroyImmediate(waterSubstance);
    }

    [Test]
    public void LooseSoilCyclesResetOnceUseCorrectSupportAndFinishEmpty()
    {
        object digging = Enum.Parse(stageType, "Digging");
        object soilRemoval = Enum.Parse(substageType, "SoilRemoval");
        object loosening = Enum.Parse(substageType, "Loosening");
        object concretePouring = Enum.Parse(stageType, "ConcretePouring");
        Apply(digging, soilRemoval, 0f, 0);
        Assert.That((bool)Property(field, "IsSurfaceActive"), Is.True);
        object model = Property(field, "HeightField");
        Assert.That((float)Property(model, "TotalUnits"), Is.EqualTo(6f).Within(.00001f));
        Assert.That(soilSurface.localPosition.y, Is.EqualTo(-.4f).Within(.00001f));

        float beforeUnits = (float)Property(model, "TotalUnits");
        float removed = (float)Call(model, "Remove", 0f, 0f, .5f, .75f);
        Assert.That(removed, Is.GreaterThan(0f).And.LessThan(.75f),
            "A small radius must remove only the real clipped cell volume it intersects.");
        Assert.That((float)Property(model, "TotalUnits"), Is.EqualTo(beforeUnits - removed).Within(.00001f));
        float carvedHeight = (float)Call(model, "GetHeight", 16, 10);
        Apply(digging, soilRemoval, removed, 0);
        Assert.That((float)Call(model, "GetHeight", 16, 10), Is.EqualTo(carvedHeight),
            "Repeated application of the same cycle must retain its carved shape.");

        Call(volume, "ResetLooseSoilState");
        Apply(digging, soilRemoval, 0f, 0);
        Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.EqualTo(6f).Within(.00001f),
            "A reset followed by replay of cycle zero must reconstruct its original layer.");
        Assert.That((float)Call(Property(field, "HeightField"), "GetHeight", 16, 10), Is.EqualTo(.4f).Within(.00001f));

        Apply(digging, loosening, 6f, 1);
        Assert.That((bool)Property(field, "IsSurfaceActive"), Is.False);
        Assert.That(soilSurface.localPosition.y, Is.EqualTo(-.4f).Within(.00001f),
            "The next layer's hard support starts at the depth of the completed cycle.");
        Apply(digging, soilRemoval, 6f, 1);
        Assert.That((bool)Property(field, "IsSurfaceActive"), Is.True);
        Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.EqualTo(6f).Within(.00001f));
        Assert.That(soilSurface.localPosition.y, Is.EqualTo(-.8f).Within(.00001f));

        Apply(digging, soilRemoval, 12f, 2);
        Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.EqualTo(6f).Within(.00001f));
        Assert.That(soilSurface.localPosition.y, Is.EqualTo(-1.2f).Within(.00001f));

        Apply(concretePouring, soilRemoval, 18f, 3);
        Assert.That((bool)Property(field, "IsSurfaceActive"), Is.False);
        Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.Zero,
            "Completed excavation must not retain hidden soil.");
        Assert.That(soilSurface.localPosition.y, Is.EqualTo(-1.2f).Within(.00001f));
        Apply(concretePouring, soilRemoval, 18f, 3);
        Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.Zero,
            "Reapplying completed state must not recreate soil.");
    }

    [Test]
    public void LooseSoilCollectionTransfersSpatiallyAndRespectsBucketCapacityAndSubstance()
    {
        PrepareCollection(0f, 0);
        object model = Property(field, "HeightField");
        float before = (float)Property(model, "TotalUnits");
        Vector3[] meshBefore = ((Mesh)Property(field, "RuntimeMesh")).vertices;
        Vector3 localCenter = new Vector3(.05f, 0f, .1f);
        Vector3 point = ((Component)field).transform.TransformPoint(localCenter);
        float collected = Collect(point, .6f, 3f, .05f);
        Assert.That(collected, Is.GreaterThan(0f).And.LessThan(3f));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(collected).Within(.000001f));
        Assert.That((float)Property(model, "TotalUnits"), Is.EqualTo(before - collected).Within(.00001f));
        Assert.That(MeshVerticesChanged(meshBefore, (Mesh)Property(field, "RuntimeMesh")), Is.True,
            "Committed host collection must rebuild the visible mesh.");

        Call(bucket, "Empty", false, Vector3.zero);
        Call(bucket, "TryAddUnits", waterSubstance, 1f);
        float incompatibleBefore = (float)Property(model, "TotalUnits");
        Assert.That(Collect(point, .6f, .5f, .05f), Is.Zero,
            "A bucket containing a different supported substance must reject collection.");
        Assert.That((float)Property(model, "TotalUnits"), Is.EqualTo(incompatibleBefore).Within(.000001f));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(1f));

        Call(bucket, "Empty", false, Vector3.zero);
        Call(bucket, "TryAddUnits", substance, 2.9f);
        float capacityLimited = Collect(point, 3f, .5f, .05f);
        Assert.That(capacityLimited, Is.GreaterThan(0f).And.LessThanOrEqualTo(.100001f));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.LessThanOrEqualTo(3f));

        Call(bucket, "Empty", false, Vector3.zero);
        float oneUlpBelowCapacity = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(3f) - 1);
        Call(bucket, "TryAddUnits", substance, oneUlpBelowCapacity);
        float tinyCapacity = 3f - oneUlpBelowCapacity;
        object beforeTinyTransfer = Call(model, "CaptureSnapshot");
        float tinyCollected = Collect(point, 100f, .5f, .05f);
        Assert.That(tinyCollected, Is.GreaterThan(0f).And.LessThanOrEqualTo(tinyCapacity));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(3f).Within(.0000001f));
        Assert.That(SnapshotsDiffer(beforeTinyTransfer, Call(model, "CaptureSnapshot")), Is.True,
            "A one-ULP bucket capacity still has to move real field material.");
    }

    [Test]
    public void LooseSoilWholeTailRequiresFullCapacityAndPositiveThreshold()
    {
        PrepareCollection(5.96f, 0);
        object model = Property(field, "HeightField");
        Call(model, "RemoveEverywhere", 5.96f);
        Vector3 point = ((Component)field).transform.TransformPoint(new Vector3(.05f, 0f, .1f));
        Call(bucket, "TryAddUnits", substance, 2.99f);
        float tailNotFit = Collect(point, .01f, .05f, .05f);
        Assert.That(tailNotFit, Is.GreaterThan(0f).And.LessThan(.04f));
        Assert.That((float)Call(model, "GetHeight", 30, 15), Is.GreaterThan(0f),
            "Insufficient room must limit the transfer to the local brush, not empty the whole layer.");
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.LessThanOrEqualTo(3f));

        PrepareCollection(5.96f, 0);
        model = Property(field, "HeightField");
        Call(model, "RemoveEverywhere", 5.96f);
        point = ((Component)field).transform.TransformPoint(new Vector3(.05f, 0f, .1f));
        float localOnly = Collect(point, .01f, .01f, 0f);
        Assert.That(localOnly, Is.GreaterThan(0f).And.LessThan(.04f),
            "A zero threshold disables global tail collection.");
        Assert.That((float)Call(model, "GetHeight", 30, 15), Is.GreaterThan(0f));

        PrepareCollection(5.96f, 0);
        model = Property(field, "HeightField");
        Call(model, "RemoveEverywhere", 5.96f);
        float expectedTail = (float)Property(model, "TotalUnits");
        point = ((Component)field).transform.TransformPoint(new Vector3(.05f, 0f, .1f));
        float wholeTail = Collect(point, .01f, .01f, .05f);
        Assert.That(wholeTail, Is.EqualTo(expectedTail).Within(.00001f));
        Assert.That((float)Property(model, "TotalUnits"), Is.Zero);
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(.04f).Within(.00001f));
    }

    [TestCase(0f)]
    [TestCase(.35f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(2.7f)]
    [TestCase(2.95f)]
    public void LooseSoilWholeTailClosesAgainstPartiallyFilledBuckets(float startingBucketUnits)
    {
        PrepareCollection(0f, 0);
        object model = Property(field, "HeightField");
        Call(model, "RemoveEverywhere", 5.96f);
        double sourceBefore = (double)Property(model, "TotalUnitsPrecise");
        Assert.That(sourceBefore, Is.GreaterThan(0d).And.LessThan(.05d));
        if (startingBucketUnits > 0f) Call(bucket, "TryAddUnits", substance, startingBucketUnits);
        float bucketBefore = (float)Property(bucket, "CurrentUnits");
        Assert.That((double)(float)Call(bucket, "GetAvailableCapacity", substance), Is.GreaterThanOrEqualTo(sourceBefore));

        Vector3 point = ((Component)field).transform.TransformPoint(new Vector3(0f, 0f, 0f));
        float reportedTransfer = Collect(point, .01f, .01f, .05f);
        float bucketAfter = (float)Property(bucket, "CurrentUnits");
        float actualBucketDelta = bucketAfter - bucketBefore;
        Assert.That(reportedTransfer, Is.EqualTo(actualBucketDelta),
            "The collection return value must match the bucket's actual stored delta, including any final float rounding.");
        Assert.That(actualBucketDelta, Is.GreaterThan(0f));
        Assert.That((double)Property(model, "TotalUnitsPrecise"), Is.Zero);
        Assert.That((int)Property(site, "DiggingCycleIndex"), Is.EqualTo(1));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.LessThanOrEqualTo(3f));
        Assert.That(Math.Abs((double)actualBucketDelta - sourceBefore),
            Is.LessThanOrEqualTo(SingleSpacing(bucketAfter) + 0.00000000001d),
            "Any closure difference must stay within one destination float spacing.");
    }

    [Test]
    public void LooseSoilCollectionConservesPreciseSourceAndPartialBucketAcrossSnapshotReplay()
    {
        PrepareCollection(0f, 0);
        Call(bucket, "TryAddUnits", substance, 1.1f);
        object model = Property(field, "HeightField");
        const double floatRoundingBound = 0.000002d;

        for (int callIndex = 0; callIndex < 64 && (float)Call(bucket, "GetAvailableCapacity", substance) > 0f; callIndex++)
        {
            double inventoryBefore = (double)Property(model, "TotalUnitsPrecise") + (float)Property(bucket, "CurrentUnits");
                    float moved = Collect(((Component)field).transform.TransformPoint(new Vector3(0f, 0f, 0f)),
                100f, .05f, .05f);
            Assert.That(moved, Is.GreaterThan(0f), $"Collection stopped unexpectedly at call {callIndex}.");
            double inventoryAfter = (double)Property(model, "TotalUnitsPrecise") + (float)Property(bucket, "CurrentUnits");
            Assert.That(Math.Abs(inventoryBefore - inventoryAfter), Is.LessThanOrEqualTo(floatRoundingBound),
                $"Source plus bucket inventory drifted beyond the explicit float-rounding bound at call {callIndex}.");

            if (callIndex == 0)
            {
                ulong revision = (ulong)Property(model, "MutationRevision");
                object sameShape = Call(field, "CaptureSnapshot");
                Assert.That((bool)Call(field, "RestoreSnapshot", sameShape), Is.True);
                Assert.That((ulong)Property(model, "MutationRevision"), Is.EqualTo(revision),
                    "An equal authoritative snapshot replay must preserve the active rounding balance.");
            }
        }

        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(3f).Within(.000001f),
            "An existing partial bucket must still fill to its actual capacity.");
    }

    [Test]
    public void LooseSoilTinySourceDoesNotRoundUpIntoAnUnrepresentablePartialBucketIncrement()
    {
        PrepareCollection(0f, 0);
        object model = Property(field, "HeightField");
        object template = Call(model, "CaptureSnapshot");
        Type snapshotType = template.GetType();
        float[] tinyHeights = new float[(int)Property(template, "CellCount")];
        for (int i = 0; i < tinyHeights.Length; i++) tinyHeights[i] = 1e-9f;
        object[] snapshotArguments =
        {
            snapshotType.GetField("Width").GetValue(template),
            snapshotType.GetField("Length").GetValue(template),
            snapshotType.GetField("CellSize").GetValue(template),
            snapshotType.GetField("MaximumHeight").GetValue(template),
            snapshotType.GetField("VolumePerUnit").GetValue(template),
            snapshotType.GetField("Columns").GetValue(template),
            snapshotType.GetField("Rows").GetValue(template),
            tinyHeights,
            null
        };
        MethodInfo createSnapshot = model.GetType().GetMethod("TryCreateSnapshot", BindingFlags.Public | BindingFlags.Static);
        Assert.That((bool)createSnapshot.Invoke(null, snapshotArguments), Is.True);
        Assert.That((bool)Call(model, "RestoreSnapshot", snapshotArguments[8]), Is.True);

        Call(bucket, "TryAddUnits", substance, 2f);
        double sourceBefore = (double)Property(model, "TotalUnitsPrecise");
        Assert.That(sourceBefore, Is.GreaterThan(0d).And.LessThan(0.0000001d));
        Vector3 point = ((Component)field).transform.TransformPoint(new Vector3(0f, 0f, 0f));
        Assert.That(Collect(point, 100f, .5f, 0f), Is.Zero,
            "The partial bucket's next representable increment exceeds this genuine tiny source.");
        Assert.That((double)Property(model, "TotalUnitsPrecise"), Is.EqualTo(sourceBefore),
            "A source that cannot be represented in the destination must remain in the field.");
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(2f));
    }

    [Test]
    public void LooseSoilCollectionRejectsNonFiniteAndOutOfPitPositionsWithoutMutation()
    {
        PrepareCollection(0f, 0);
        object model = Property(field, "HeightField");
        Vector3 valid = ((Component)field).transform.TransformPoint(new Vector3(.05f, 0f, .1f));
        float before = (float)Property(model, "TotalUnits");
        Assert.That(Collect(valid, float.NaN, 1f, .05f), Is.Zero);
        Assert.That(Collect(valid, .5f, float.PositiveInfinity, .05f), Is.Zero);
        Assert.That(Collect(valid, .5f, 1f, float.NaN), Is.Zero);
        Assert.That(Collect(new Vector3(float.NaN, valid.y, valid.z), .5f, 1f, .05f), Is.Zero);
        Assert.That(Collect(new Vector3(valid.x, float.PositiveInfinity, valid.z), .5f, 1f, .05f), Is.Zero);
        Assert.That(Collect(((Component)field).transform.TransformPoint(new Vector3(10f, .2f, 0f)), .5f, 1f, .05f), Is.Zero);
        Assert.That(Collect(((Component)field).transform.TransformPoint(new Vector3(.05f, .46f, .1f)), .5f, 1f, .05f), Is.Zero,
            "A position immediately above the soil surface cannot collect material.");
        Assert.That((float)Property(model, "TotalUnits"), Is.EqualTo(before).Within(.000001f));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.Zero);
        Assert.That((float)Call(site, "TryExtractSoil", 1f, substance), Is.Zero,
            "Legacy direct extraction remains disabled for height-field pits.");
        Assert.That((bool)Call(site, "CanExtract", substance), Is.False);
        Assert.That((float)Call(site, "ReturnSoil", 1f), Is.Zero,
            "Local returns remain disabled until the later stage.");
    }

    [Test]
    public void LooseSoilThreeCyclesTakeTwoFullBucketLoadsEachAndTotalEighteenUnits()
    {
        double totalCollected = 0d;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            if (cycle > 0)
            {
                Assert.That(Property(site, "CurrentStage").ToString(), Is.EqualTo("Digging"));
                Assert.That((int)Property(site, "DiggingCycleIndex"), Is.EqualTo(cycle));
                Assert.That(Property(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"),
                    "Completing a layer advances to the next cycle's loosening stage.");
            }
            PrepareCollection(cycle * 6f, cycle);
            Vector3 point = ((Component)field).transform.TransformPoint(new Vector3(0f, 0f, 0f));
            for (int load = 0; load < 2; load++)
            {
                int ticks = 0;
                while ((float)Call(bucket, "GetAvailableCapacity", substance) > 0f &&
                       Property(site, "CurrentStage").ToString() == "Digging" &&
                       Property(site, "DiggingSubstage").ToString() == "SoilRemoval" && ticks < 70)
                {
                    float moved = Collect(point, 100f, .05f, .05f);
                    Assert.That(moved, Is.GreaterThan(0f).And.LessThanOrEqualTo(.050001f),
                        $"cycle={cycle}, load={load}, tick={ticks}, bucket={Property(bucket, "CurrentUnits")}, " +
                        $"removed={Property(site, "RemovedSoilUnits")}, index={Property(site, "DiggingCycleIndex")}, " +
                        $"substage={Property(site, "DiggingSubstage")}, field={Property(Property(field, "HeightField"), "TotalUnits")}");
                    ticks++;
                }
                Assert.That(ticks, Is.GreaterThan(0).And.LessThanOrEqualTo(70));
                float loadUnits = (float)Property(bucket, "CurrentUnits");
                Assert.That(loadUnits, Is.EqualTo(3f).Within(.00001f));
                if (load == 0)
                    Assert.That((float)Call(bucket, "GetAvailableCapacity", substance), Is.Zero,
                        "The first bucket load must stop only when its actual capacity is exhausted.");
                totalCollected += loadUnits;
                Call(bucket, "Empty", false, Vector3.zero);
            }

            if (cycle < 2)
            {
                Assert.That((int)Property(site, "DiggingCycleIndex"), Is.EqualTo(cycle + 1),
                    $"cycle={cycle}, stage={Property(site, "CurrentStage")}, removed={Property(site, "RemovedSoilUnits")}, " +
                    $"field={Property(Property(field, "HeightField"), "TotalUnits")}");
                Assert.That(Property(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
            }
        }

        Assert.That(totalCollected, Is.EqualTo(18f).Within(.0001f));
        Assert.That((float)Property(site, "RemovedSoilUnits"), Is.EqualTo(18f).Within(.0001f));
        Assert.That(Property(site, "CurrentStage").ToString(), Is.EqualTo("ConcretePouring"));
        Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.Zero);
    }

    [Test]
    public void ExitRampStaysHiddenThroughAllDiggingCyclesAndReturnsAfterFinalExcavation()
    {
        object digging = Enum.Parse(stageType, "Digging");
        object pouring = Enum.Parse(stageType, "ConcretePouring");
        object noFailure = Enum.Parse(Type.GetType("FoundationConcreteFailureState, Assembly-CSharp"), "None");
        MethodInfo applyConcrete = volumeType.GetMethod("ApplyConcreteState", Instance);
        object[] concreteArgs = { digging, 0, 1, 0f, 30f, noFailure, 0f, Vector3.zero, 0f };

        Set(volume, "useLooseSoilHeightField", false);
        applyConcrete.Invoke(volume, concreteArgs);
        Assert.That(exitRampRenderer.enabled && exitRampCollider.enabled, Is.True,
            "Legacy foundations without loose-soil fields keep the ramp during digging.");

        Set(volume, "useLooseSoilHeightField", true);
        double totalCollected = 0d;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            if (cycle > 0)
            {
                Assert.That(Property(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
                Assert.That(exitRampRenderer.enabled || exitRampCollider.enabled, Is.False,
                    "The ramp remains hidden during the inter-layer loosening stage.");
                Assert.That((bool)Call(site, "TryApplyToolWork", Property(workflow, "DiggingTool"),
                    Property(workflow, "LooseningProgressPerCycle"), -1), Is.True);
            }

            if (cycle == 0) PrepareCollection(0f, 0);
            Call(site, "ApplyVisualState");
            Assert.That(exitRampRenderer.enabled || exitRampCollider.enabled, Is.False,
                $"The ramp is hidden before collection in digging cycle {cycle}.");
            Vector3 point = ((Component)field).transform.TransformPoint(new Vector3(0f, 0f, 0f));
            for (int load = 0; load < 2; load++)
            {
                int calls = 0;
                while ((float)Call(bucket, "GetAvailableCapacity", substance) > 0f && calls < 70)
                {
                    float moved = Collect(point, 100f, .05f, .05f);
                    Assert.That(moved, Is.GreaterThan(0f),
                        $"cycle={cycle}, load={load}, call={calls}, stage={Property(site, "CurrentStage")}, " +
                        $"substage={Property(site, "DiggingSubstage")}, bucket={Property(bucket, "CurrentUnits")}, " +
                        $"remaining={Property(Property(field, "HeightField"), "TotalUnitsPrecise")}");
                    calls++;
                }
                Assert.That(calls, Is.GreaterThan(0).And.LessThan(70), $"cycle={cycle}, load={load}");
                Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(3f).Within(.00001f),
                    $"cycle={cycle}, load={load} must finish as a full bucket.");
                totalCollected += (float)Property(bucket, "CurrentUnits");
                Call(bucket, "Empty", false, Vector3.zero);
                if (Property(site, "CurrentStage").ToString() == "Digging")
                    Assert.That(exitRampRenderer.enabled || exitRampCollider.enabled, Is.False,
                        $"The ramp remains hidden during digging cycle {cycle}, load {load}.");
            }
        }

        Assert.That(totalCollected, Is.EqualTo(18d).Within(.00001d));
        Assert.That(Property(site, "CurrentStage").ToString(), Is.EqualTo("ConcretePouring"));
        Assert.That(exitRampRenderer.enabled, Is.True,
            "The ramp renderer returns only after all eighteen units have been excavated.");
        Assert.That(exitRampCollider.enabled, Is.True,
            "The ramp collider returns only after all eighteen units have been excavated.");

        object awaitingRecovery = Enum.Parse(noFailure.GetType(), "AwaitingWheelbarrowExit");
        concreteArgs[0] = pouring;
        concreteArgs[1] = 1;
        concreteArgs[5] = awaitingRecovery;
        applyConcrete.Invoke(volume, concreteArgs);
        Assert.That(exitRampRenderer.enabled, Is.True);
        Assert.That(exitRampCollider.enabled, Is.True,
            "The failed-concrete wheelbarrow-exit recovery route keeps the ramp available.");
    }

    [TestCase(17, 10f)]
    [TestCase(29, 1000f)]
    [TestCase(101, 3600f)]
    public void VariableFloatTimeCollectionFinishesEachLayerInExactlyTwoFullBuckets(int seed, float startingTime)
    {
        var random = new System.Random(seed);
        float currentTime = startingTime;
        float previousTime = startingTime;
        float totalCollected = 0f;

        for (int cycle = 0; cycle < 3; cycle++)
        {
            PrepareCollection(cycle * 6f, cycle);
            Vector3 point = ((Component)field).transform.TransformPoint(new Vector3(0f, 0f, 0f));

            for (int load = 0; load < 2; load++)
            {
                int calls = 0;
                while ((float)Call(bucket, "GetAvailableCapacity", substance) > 0f &&
                       Property(site, "CurrentStage").ToString() == "Digging" &&
                       Property(site, "DiggingSubstage").ToString() == "SoilRemoval" && calls < 128)
                {
                    currentTime += .1f + (float)random.NextDouble() * .04f;
                    float elapsed = currentTime - previousTime;
                    previousTime = currentTime;
                    float requested = Mathf.Clamp(.5f * elapsed, 0f, .25f);
                    float moved = Collect(point, 100f, requested, .05f);
                    Assert.That(moved, Is.GreaterThan(0f),
                        $"seed={seed}, cycle={cycle}, load={load}, call={calls}, elapsed={elapsed:R}, " +
                        $"bucket={Property(bucket, "CurrentUnits")}, remaining={Property(Property(field, "HeightField"), "TotalUnits")}");
                    calls++;
                }

                Assert.That(calls, Is.GreaterThan(0).And.LessThan(128),
                    $"seed={seed}, cycle={cycle}, load={load}: collection did not fill the bucket in bounded calls.");
                float loadUnits = (float)Property(bucket, "CurrentUnits");
                Assert.That(loadUnits, Is.EqualTo(3f).Within(.000001f),
                    $"seed={seed}, cycle={cycle}, load={load}: expected a full nominal bucket.");
                if (load == 0)
                    Assert.That((float)Call(bucket, "GetAvailableCapacity", substance), Is.Zero,
                        $"seed={seed}, cycle={cycle}: first bucket should be full before emptying.");
                totalCollected += loadUnits;
                Call(bucket, "Empty", false, Vector3.zero);
            }

            Assert.That(Property(site, "DiggingCycleIndex"), Is.EqualTo(cycle + 1),
                $"seed={seed}, cycle={cycle}: exactly two full bucket loads must advance the layer without a third; " +
                $"old field shape remaining={Property(Property(field, "HeightField"), "TotalUnits")}, " +
                $"preciseRemaining={Property(Property(field, "HeightField"), "TotalUnitsPrecise")}, " +
                $"roundingBalance={siteType.GetField("collectionRoundingBalance", Instance).GetValue(site)}, " +
                $"removed={Property(site, "RemovedSoilUnits")}, stage={Property(site, "CurrentStage")}, " +
                $"substage={Property(site, "DiggingSubstage")}");
            Assert.That((float)Property(site, "RemovedSoilUnits"), Is.EqualTo((cycle + 1) * 6f).Within(.000001f),
                $"seed={seed}, cycle={cycle}: layer accounting must advance by six nominal units.");
            if (cycle < 2)
            {
                Assert.That(Property(site, "CurrentStage").ToString(), Is.EqualTo("Digging"));
                Assert.That(Property(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
                Assert.That((bool)Property(field, "IsSurfaceActive"), Is.False);
                Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.EqualTo(6f).Within(.000001f),
                    "The following layer is initialized while inactive during loosening.");
            }
            else
            {
                Assert.That(Property(site, "CurrentStage").ToString(), Is.EqualTo("ConcretePouring"));
                Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.Zero);
            }
        }

        Assert.That(totalCollected, Is.EqualTo(18f).Within(.000001f), $"seed={seed}: expected 18 nominal units.");
        Assert.That(Property(site, "CurrentStage").ToString(), Is.EqualTo("ConcretePouring"));
        Assert.That((float)Property(Property(field, "HeightField"), "TotalUnits"), Is.Zero);
    }

    [Test]
    public void LooseSoilReturnToActiveLayerRestoresOnlyAcceptedSourceAndKeepsHardSupport()
    {
        PrepareCollection(0f, 0);
        Vector3 point = PitPoint();
        float collected = Collect(point, .2f, 1f, 0f);
        Assert.That(collected, Is.GreaterThan(0f));
        object model = Property(field, "HeightField");
        double beforeReturn = (double)Property(model, "TotalUnitsPrecise");
        float bucketBefore = (float)Property(bucket, "CurrentUnits");

        float returned = Return(bucketBefore, point);

        Assert.That(returned, Is.GreaterThan(0f).And.LessThanOrEqualTo(bucketBefore));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.LessThan(bucketBefore));
        Assert.That((double)Property(model, "TotalUnitsPrecise") - beforeReturn,
            Is.EqualTo(returned).Within(.000001d));
        Assert.That((float)Property(site, "RemovedSoilUnits"), Is.LessThan(collected));
        Assert.That(soilSurface.localPosition.y, Is.EqualTo(-.4f).Within(.00001f));
        for (int row = 0; row < (int)Property(model, "Rows"); row++)
        for (int column = 0; column < (int)Property(model, "Columns"); column++)
            Assert.That((float)Call(model, "GetHeight", column, row), Is.InRange(0f, .4f));
    }

    [Test]
    public void LooseSoilReturnAndRecollectionPreserveMeasuredLedgerAcrossRejectedReturnAndSnapshotReplay()
    {
        PrepareCollection(0f, 0);
        Vector3 point = PitPoint();
        Assert.That(Collect(point, .2f, .25f, 0f), Is.GreaterThan(0f));
        object model = Property(field, "HeightField");
        FieldInfo roundingBalance = siteType.GetField("collectionRoundingBalance", Instance);
        FieldInfo transferCount = siteType.GetField("collectionBalanceTransferCount", Instance);
        double balanceBeforeReject = (double)roundingBalance.GetValue(site);
        int transferCountBeforeReject = (int)transferCount.GetValue(site);
        float bucketBeforeReject = (float)Property(bucket, "CurrentUnits");
        double fieldBeforeReject = (double)Property(model, "TotalUnitsPrecise");
        float removedBeforeReject = (float)Property(site, "RemovedSoilUnits");
        object beforeReject = Call(model, "CaptureSnapshot");
        Vector3 outside = ((Component)field).transform.TransformPoint(new Vector3(100f, 0f, 100f));

        Assert.That(Return(bucketBeforeReject, outside), Is.Zero);
        Assert.That((double)Property(model, "TotalUnitsPrecise"), Is.EqualTo(fieldBeforeReject));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(bucketBeforeReject));
        Assert.That((float)Property(site, "RemovedSoilUnits"), Is.EqualTo(removedBeforeReject));
        Assert.That(SnapshotsDiffer(beforeReject, Call(model, "CaptureSnapshot")), Is.False);
        Assert.That((double)roundingBalance.GetValue(site), Is.EqualTo(balanceBeforeReject));
        Assert.That((int)transferCount.GetValue(site), Is.EqualTo(transferCountBeforeReject));

        double inventoryBeforeReturn = fieldBeforeReject + bucketBeforeReject;
        Vector3 localReturnPoint = ((Component)field).transform.InverseTransformPoint(point);
        double localReturnCapacity = (double)Call(model, "GetAdditionCapacity", localReturnPoint.x,
            localReturnPoint.z, (float)Property(field, "ReturnRadius"), .4f);
        double logicalReturnAvailability = Math.Max(0d, 6d - fieldBeforeReject + balanceBeforeReject);
        double logicalLocalCapacity = Math.Max(0d, localReturnCapacity + balanceBeforeReject);
        double logicalReturnBudget = Math.Min(bucketBeforeReject,
            Math.Min(logicalReturnAvailability, logicalLocalCapacity));
        float sourceDebitReported = Return(bucketBeforeReject, point);
        float bucketAfterReturn = (float)Property(bucket, "CurrentUnits");
        double actualSourceDebit = (double)bucketBeforeReject - bucketAfterReturn;
        double fieldAdded = (double)Property(model, "TotalUnitsPrecise") - fieldBeforeReject;
        double balanceAfterReturn = (double)roundingBalance.GetValue(site);
        Assert.That(sourceDebitReported, Is.GreaterThan(0f));
        Assert.That(actualSourceDebit, Is.GreaterThan(0d).And.LessThanOrEqualTo(logicalReturnBudget),
            "The actual source debit must stay within source availability and eligible local capacity.");
        Assert.That(fieldAdded - actualSourceDebit,
            Is.EqualTo(balanceAfterReturn - balanceBeforeReject).Within(0.000000001d));
        Assert.That((double)Property(model, "TotalUnitsPrecise") + bucketAfterReturn - inventoryBeforeReturn,
            Is.EqualTo(balanceAfterReturn - balanceBeforeReject).Within(0.000000001d));

        object unchanged = Call(model, "CaptureSnapshot");
        ulong revisionBeforeReplay = (ulong)Property(model, "MutationRevision");
        Assert.That((bool)Call(model, "RestoreSnapshot", unchanged), Is.True);
        Assert.That((ulong)Property(model, "MutationRevision"), Is.EqualTo(revisionBeforeReplay));
        Assert.That((double)roundingBalance.GetValue(site), Is.EqualTo(balanceAfterReturn));

        double inventoryBeforeRecollect = (double)Property(model, "TotalUnitsPrecise") +
                                          (float)Property(bucket, "CurrentUnits");
        double balanceBeforeRecollect = (double)roundingBalance.GetValue(site);
        float bucketBeforeRecollect = (float)Property(bucket, "CurrentUnits");
        double fieldBeforeRecollect = (double)Property(model, "TotalUnitsPrecise");
        Assert.That(Collect(point, .2f, .05f, 0f), Is.GreaterThan(0f));
        double actualStoredDelta = (double)(float)Property(bucket, "CurrentUnits") - bucketBeforeRecollect;
        double preciseRemoved = fieldBeforeRecollect - (double)Property(model, "TotalUnitsPrecise");
        double balanceAfterRecollect = (double)roundingBalance.GetValue(site);
        Assert.That(actualStoredDelta - preciseRemoved,
            Is.EqualTo(balanceAfterRecollect - balanceBeforeRecollect).Within(0.000000001d));
        Assert.That((double)Property(model, "TotalUnitsPrecise") + (float)Property(bucket, "CurrentUnits") -
                    inventoryBeforeRecollect,
            Is.EqualTo(balanceAfterRecollect - balanceBeforeRecollect).Within(0.000000001d));
    }

    [Test]
    public void LooseSoilReturnAndRecollectionSettlePositiveCollectionCredit()
    {
        PrepareCollection(0f, 0);
        object model = Property(field, "HeightField");
        FieldInfo roundingBalance = siteType.GetField("collectionRoundingBalance", Instance);
        Vector3 point = ((Component)field).transform.TransformPoint(new Vector3(0f, 0f, 0f));
        System.Random schedule = new(613);
        float clock = 1000f, previousClock = clock;
        int calls = 0;
        while ((float)Call(bucket, "GetAvailableCapacity", substance) > 0f && calls < 128)
        {
            clock += .28f + (float)schedule.NextDouble() * .19f;
            float requested = Mathf.Min(.25f, .5f * (clock - previousClock));
            previousClock = clock;
            float moved = Collect(point, 100f, requested, .05f);
            Assert.That(moved, Is.GreaterThan(0f), $"variable collection call={calls}");
            calls++;
        }
        Assert.That(calls, Is.GreaterThan(1).And.LessThan(128));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(3f).Within(.000001f));
        double positiveCredit = (double)roundingBalance.GetValue(site);
        Assert.That(positiveCredit, Is.GreaterThan(0d),
            "This deterministic variable-request schedule must exercise positive collection credit.");

        double returned = 0d;
        int returnCalls = 0;
        for (int row = 0; row < (int)Property(model, "Rows") && (float)Property(bucket, "CurrentUnits") > 0f; row++)
        for (int column = 0; column < (int)Property(model, "Columns") && (float)Property(bucket, "CurrentUnits") > 0f; column++)
        {
            float x = -3.25f + column * .2f + .1f;
            float z = -2f + row * .2f + .1f;
            Vector3 position = ((Component)field).transform.TransformPoint(new Vector3(x, 0f, z));
            float sourceBefore = (float)Property(bucket, "CurrentUnits");
            double fieldBefore = (double)Property(model, "TotalUnitsPrecise");
            double balanceBefore = (double)roundingBalance.GetValue(site);
            float reported = Return(sourceBefore, position);
            float sourceAfter = (float)Property(bucket, "CurrentUnits");
            double sourceDebit = (double)sourceBefore - sourceAfter;
            double fieldAfter = (double)Property(model, "TotalUnitsPrecise");
            double fieldAdded = fieldAfter - fieldBefore;
            double balanceAfter = (double)roundingBalance.GetValue(site);
            Assert.That(reported, Is.EqualTo(sourceDebit).Within(SingleSpacing(sourceBefore) + 1e-8d));
            Assert.That(balanceAfter - balanceBefore, Is.EqualTo(fieldAdded - sourceDebit).Within(1e-9d));
            if (sourceDebit > 0d)
            {
                Assert.That(Math.Abs(fieldAdded - (sourceDebit - balanceBefore)), Is.LessThan(.00001d),
                    $"Credit-aware return must add only the unfunded part. call={returnCalls}; " +
                    $"source={sourceBefore:R}->{sourceAfter:R}; field={fieldBefore:R}->{fieldAfter:R}; " +
                    $"balance={balanceBefore:R}->{balanceAfter:R}");
                returned += sourceDebit;
            }
            returnCalls++;
        }
        string beforeSecondSweep = FormatReturnStopDiagnostic(model, "positive-credit second sweep before");
        for (int row = 0; row < (int)Property(model, "Rows") && (float)Property(bucket, "CurrentUnits") > 0f; row++)
        for (int column = 0; column < (int)Property(model, "Columns") && (float)Property(bucket, "CurrentUnits") > 0f; column++)
        {
            float x = -3.25f + column * .2f + .1f;
            float z = -2f + row * .2f + .1f;
            Vector3 position = ((Component)field).transform.TransformPoint(new Vector3(x, 0f, z));
            float sourceBefore = (float)Property(bucket, "CurrentUnits");
            double fieldBefore = (double)Property(model, "TotalUnitsPrecise");
            double balanceBefore = (double)roundingBalance.GetValue(site);
            float reported = Return(sourceBefore, position);
            float sourceAfter = (float)Property(bucket, "CurrentUnits");
            double sourceDebit = (double)sourceBefore - sourceAfter;
            double fieldAdded = (double)Property(model, "TotalUnitsPrecise") - fieldBefore;
            double balanceAfter = (double)roundingBalance.GetValue(site);
            Assert.That(reported, Is.EqualTo(sourceDebit).Within(SingleSpacing(sourceBefore) + 1e-8d));
            Assert.That(balanceAfter - balanceBefore, Is.EqualTo(fieldAdded - sourceDebit).Within(1e-9d));
            if (sourceDebit > 0d)
            {
                Assert.That(Math.Abs(fieldAdded - (sourceDebit - balanceBefore)), Is.LessThan(.00001d),
                    $"Credit-aware second-sweep return must add only the unfunded part. " +
                    $"source={sourceBefore:R}->{sourceAfter:R}; field={fieldBefore:R}->" +
                    $"{Property(model, "TotalUnitsPrecise"):R}; balance={balanceBefore:R}->{balanceAfter:R}");
                returned += sourceDebit;
            }
            returnCalls++;
        }
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.Zero,
            beforeSecondSweep + "\n" + FormatReturnStopDiagnostic(model, "positive-credit second sweep after"));
        Assert.That(returned, Is.EqualTo(3d).Within(.00001d));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.Zero);
        Assert.That((float)Property(model, "TotalUnits"), Is.EqualTo(6f).Within(.00001f));
        Assert.That(Math.Abs((double)roundingBalance.GetValue(site)), Is.LessThan(.00001d));

        double inventoryBeforeRecollect = (double)Property(model, "TotalUnitsPrecise") +
                                          (float)Property(bucket, "CurrentUnits");
        double balanceBeforeRecollect = (double)roundingBalance.GetValue(site);
        schedule = new System.Random(613);
        clock = 1000f;
        previousClock = clock;
        calls = 0;
        while ((float)Call(bucket, "GetAvailableCapacity", substance) > 0f && calls < 128)
        {
            clock += .28f + (float)schedule.NextDouble() * .19f;
            float requested = Mathf.Min(.25f, .5f * (clock - previousClock));
            previousClock = clock;
            float moved = Collect(point, 100f, requested, .05f);
            Assert.That(moved, Is.GreaterThan(0f), $"variable recollection call={calls}");
            calls++;
        }
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(3f).Within(.000001f));
        double balanceAfterRecollect = (double)roundingBalance.GetValue(site);
        Assert.That((double)Property(model, "TotalUnitsPrecise") + (float)Property(bucket, "CurrentUnits") -
                    inventoryBeforeRecollect,
            Is.EqualTo(balanceAfterRecollect - balanceBeforeRecollect).Within(1e-9d));
    }

    [Test]
    public void LooseSoilReturnSourceDebitNeverExceedsNonRepresentableLocalHeadroom()
    {
        PrepareCollection(1f, 0);
        object model = Property(field, "HeightField");
        Vector3 point = PitPoint();
        Call(model, "Remove", .05f, .1f, .01f, 1f);
        Call(bucket, "TryAddUnits", substance, 3f);
        double fieldBefore = (double)Property(model, "TotalUnitsPrecise");
        float bucketBefore = (float)Property(bucket, "CurrentUnits");
        double balanceBefore = (double)siteType.GetField("collectionRoundingBalance",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(site);
        double logicalLocalCapacity = (double)Call(model, "GetAdditionCapacity",
            ((Component)field).transform.InverseTransformPoint(point).x,
            ((Component)field).transform.InverseTransformPoint(point).z,
            (float)Property(field, "ReturnRadius"), .4f);

        float reportedDebit = Return(bucketBefore, point);

        double actualSourceDebit = (double)bucketBefore - (float)Property(bucket, "CurrentUnits");
        double physicalAdded = (double)Property(model, "TotalUnitsPrecise") - fieldBefore;
        Assert.That(reportedDebit, Is.GreaterThan(0f));
        Assert.That(actualSourceDebit, Is.GreaterThan(0d).And.LessThanOrEqualTo(logicalLocalCapacity),
            "The source debit must remain within the exact eligible local capacity.");
        double balanceAfter = (double)siteType.GetField("collectionRoundingBalance",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(site);
        Assert.That(balanceAfter - balanceBefore, Is.EqualTo(physicalAdded - actualSourceDebit).Within(1e-9d),
            "The ledger must record the difference between logical source debit and represented grid volume.");
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.GreaterThan(0f),
            "Any source amount beyond local headroom must remain in the bucket.");
        Assert.That((float)Property(site, "RemovedSoilUnits"),
            Is.EqualTo(6f - (float)(fieldBefore + physicalAdded)).Within(.00001f));
    }

    [Test]
    public void LooseSoilReturnedOverlayKeepsPartialLooseningAndResumesSameCycle()
    {
        PrepareCollection(0f, 0);
        Vector3 point = PitPoint();
        for (int load = 0; load < 2; load++)
        {
            CollectUntilBucketFull(point, "overlay setup " + load);
            Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(3f).Within(.000001f));
            Call(bucket, "Empty", false, Vector3.zero);
        }
        Assert.That(Property(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
        Set(site, "currentWorkProgress", 27.5f);
        Set(site, "diggingSubstage", Enum.Parse(substageType, "Loosening"));
        Apply(Enum.Parse(stageType, "Digging"), Enum.Parse(substageType, "Loosening"), 6f, 1);
        Call(bucket, "TryAddUnits", substance, 1f);

        Assert.That(Return(1f, point), Is.GreaterThan(0f));
        object model = Property(field, "HeightField");
        float returnedOverlayUnits = (float)Property(model, "TotalUnits");
        Assert.That(Property(site, "DiggingSubstage").ToString(), Is.EqualTo("ReturnedSoilRemoval"));
        Assert.That(returnedOverlayUnits, Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.GreaterThan(0f),
            "The portion that does not fit in the local pit must remain in the source bucket.");
        Assert.That((float)Property(site, "CurrentWorkProgress"), Is.EqualTo(27.5f));
        Assert.That(soilSurface.localPosition.y, Is.EqualTo(-.4f).Within(.00001f));

        Call(bucket, "Empty", false, Vector3.zero);
        Assert.That(Collect(point, 100f, 1f, returnedOverlayUnits + .00001f), Is.GreaterThan(0f));
        Assert.That(Property(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
        Assert.That(Property(site, "DiggingCycleIndex"), Is.EqualTo(1));
        Assert.That((float)Property(site, "CurrentWorkProgress"), Is.EqualTo(27.5f));
        model = Property(field, "HeightField");
        Assert.That((float)Property(model, "TotalUnits"), Is.Zero);
        Assert.That(soilSurface.localPosition.y, Is.EqualTo(-.4f).Within(.00001f));

        Set(site, "diggingSubstage", Enum.Parse(substageType, "SoilRemoval"));
        Set(site, "currentWorkProgress", 60f);
        Apply(Enum.Parse(stageType, "Digging"), Enum.Parse(substageType, "SoilRemoval"), 6f, 1);
        model = Property(field, "HeightField");
        Assert.That((float)Property(model, "TotalUnits"), Is.EqualTo(6f).Within(.00001f));
        Assert.That(soilSurface.localPosition.y, Is.EqualTo(-.8f).Within(.00001f));
    }

    [Test]
    public void LooseSoilReturnCanExceedOneCycleAndCollectionDoesNotAdvanceEarly()
    {
        PrepareCollection(6f, 1);
        object model = Property(field, "HeightField");
        Call(bucket, "TryAddUnits", substance, 3f);
        double returnedTotal = 0d;
        double firstLoadSourceDebit = 0d;
        string firstReturnDiagnostic = null;
        for (int row = 0; row < (int)Property(model, "Rows") && returnedTotal < 3d; row++)
        for (int column = 0; column < (int)Property(model, "Columns") && returnedTotal < 3d; column++)
        {
            float x = -3.25f + column * .2f + .1f;
            float z = -2f + row * .2f + .1f;
            Vector3 position = ((Component)field).transform.TransformPoint(new Vector3(x, 0f, z));
            float sourceBefore = (float)Property(bucket, "CurrentUnits");
            double fieldBefore = (double)Property(model, "TotalUnitsPrecise");
            double balanceBefore = (double)siteType.GetField("collectionRoundingBalance", Instance).GetValue(site);
            float moved = Return((float)Property(bucket, "CurrentUnits"), position);
            float sourceAfter = (float)Property(bucket, "CurrentUnits");
            double fieldAfter = (double)Property(model, "TotalUnitsPrecise");
            double balanceAfter = (double)siteType.GetField("collectionRoundingBalance", Instance).GetValue(site);
            double sourceDebit = (double)sourceBefore - sourceAfter;
            double physicalAdded = fieldAfter - fieldBefore;
            double ledgerDelta = balanceAfter - balanceBefore;
            if (sourceDebit > 0d)
            {
                firstLoadSourceDebit += sourceDebit;
                if (firstReturnDiagnostic == null && Math.Abs(physicalAdded - sourceDebit) > 0.000001d)
                    firstReturnDiagnostic = FormatReturnDiagnostic("first-load", model, position, sourceBefore,
                        sourceAfter, moved, fieldBefore, fieldAfter, sourceDebit, balanceBefore, balanceAfter);
                Assert.That(Math.Abs(physicalAdded - sourceDebit), Is.LessThan(.00001d),
                    $"A bounded local return may differ only by height-grid float representation. {firstReturnDiagnostic}");
                Assert.That(moved, Is.EqualTo(sourceDebit).Within(SingleSpacing(sourceBefore) + 1e-8d),
                    $"The public return amount must track the source's actual stored delta. {firstReturnDiagnostic}");
                Assert.That(ledgerDelta, Is.EqualTo(physicalAdded - sourceDebit).Within(1e-9d),
                    $"The ledger must preserve the signed physical/source difference. {firstReturnDiagnostic}");
            }
            else if (firstReturnDiagnostic == null && sourceBefore > 0f &&
                     HasPositiveLocalReturnOpportunity(model, position, sourceBefore))
                firstReturnDiagnostic = FormatReturnDiagnostic("first-load rejected with positive local capacity", model,
                    position, sourceBefore, sourceAfter, moved, fieldBefore, fieldAfter, 0d, balanceBefore, balanceAfter);
            returnedTotal += moved;
        }
        Assert.That(returnedTotal, Is.EqualTo(3d).Within(.00001d));
        Assert.That(firstLoadSourceDebit, Is.EqualTo(3d).Within(.00001d),
            $"The first full bucket must debit exactly three units. First return diagnostic: {firstReturnDiagnostic}");
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.Zero,
            $"The first load must not be refilled early or leave source material behind. First return diagnostic: {firstReturnDiagnostic}");
        double secondLoadSourceDebit = 0d;
        Call(bucket, "Empty", false, Vector3.zero);
        Call(bucket, "TryAddUnits", substance, 3f);
        for (int row = (int)Property(model, "Rows") - 1; row >= 0 && returnedTotal < 6d; row--)
        for (int column = (int)Property(model, "Columns") - 1; column >= 0 && returnedTotal < 6d; column--)
        {
            float x = -3.25f + column * .2f + .1f;
            float z = -2f + row * .2f + .1f;
            Vector3 position = ((Component)field).transform.TransformPoint(new Vector3(x, 0f, z));
            float sourceBefore = (float)Property(bucket, "CurrentUnits");
            double fieldBefore = (double)Property(model, "TotalUnitsPrecise");
            double balanceBefore = (double)siteType.GetField("collectionRoundingBalance", Instance).GetValue(site);
            float moved = Return(sourceBefore, position);
            float sourceAfter = (float)Property(bucket, "CurrentUnits");
            double fieldAfter = (double)Property(model, "TotalUnitsPrecise");
            double balanceAfter = (double)siteType.GetField("collectionRoundingBalance", Instance).GetValue(site);
            double sourceDebit = (double)sourceBefore - sourceAfter;
            double physicalAdded = fieldAfter - fieldBefore;
            double ledgerDelta = balanceAfter - balanceBefore;
            if (sourceDebit > 0d)
            {
                secondLoadSourceDebit += sourceDebit;
                if (firstReturnDiagnostic == null && Math.Abs(physicalAdded - sourceDebit) > 0.000001d)
                    firstReturnDiagnostic = FormatReturnDiagnostic("second-load", model, position, sourceBefore,
                        sourceAfter, moved, fieldBefore, fieldAfter, sourceDebit, balanceBefore, balanceAfter);
                Assert.That(Math.Abs(physicalAdded - sourceDebit), Is.LessThan(.00001d),
                    $"A bounded local return may differ only by height-grid float representation. {firstReturnDiagnostic}");
                Assert.That(moved, Is.EqualTo(sourceDebit).Within(SingleSpacing(sourceBefore) + 1e-8d),
                    $"The public return amount must track the source's actual stored delta. {firstReturnDiagnostic}");
                Assert.That(ledgerDelta, Is.EqualTo(physicalAdded - sourceDebit).Within(1e-9d),
                    $"The ledger must preserve the signed physical/source difference. {firstReturnDiagnostic}");
            }
            else if (firstReturnDiagnostic == null && sourceBefore > 0f &&
                     HasPositiveLocalReturnOpportunity(model, position, sourceBefore))
                firstReturnDiagnostic = FormatReturnDiagnostic("second-load rejected with positive local capacity", model,
                    position, sourceBefore, sourceAfter, moved, fieldBefore, fieldAfter, 0d, balanceBefore, balanceAfter);
            returnedTotal += moved;
            if (returnedTotal >= 6d) break;
        }
        string beforeTailSweepDiagnostic = null;
        double tailSweepDebit = 0d;
        if ((float)Property(bucket, "CurrentUnits") > 0f)
        {
            beforeTailSweepDiagnostic = FormatReturnStopDiagnostic(model, "before second local sweep");
            for (int row = 0; row < (int)Property(model, "Rows") && (float)Property(bucket, "CurrentUnits") > 0f; row++)
            for (int column = 0; column < (int)Property(model, "Columns") && (float)Property(bucket, "CurrentUnits") > 0f; column++)
            {
                float x = -3.25f + column * .2f + .1f;
                float z = -2f + row * .2f + .1f;
                Vector3 position = ((Component)field).transform.TransformPoint(new Vector3(x, 0f, z));
                float sourceBefore = (float)Property(bucket, "CurrentUnits");
                double fieldBefore = (double)Property(model, "TotalUnitsPrecise");
                double balanceBefore = (double)siteType.GetField("collectionRoundingBalance", Instance).GetValue(site);
                float reported = Return(sourceBefore, position);
                float sourceAfter = (float)Property(bucket, "CurrentUnits");
                double sourceDebit = (double)sourceBefore - sourceAfter;
                double fieldAdded = (double)Property(model, "TotalUnitsPrecise") - fieldBefore;
                double balanceAfter = (double)siteType.GetField("collectionRoundingBalance", Instance).GetValue(site);
                Assert.That(balanceAfter - balanceBefore, Is.EqualTo(fieldAdded - sourceDebit).Within(1e-9d));
                if (sourceDebit > 0d)
                {
                    Assert.That(reported, Is.EqualTo(sourceDebit).Within(SingleSpacing(sourceBefore) + 1e-8d));
                    Assert.That(Math.Abs(fieldAdded - Math.Max(0d, sourceDebit - balanceBefore)), Is.LessThan(.00001d));
                    tailSweepDebit += sourceDebit;
                    secondLoadSourceDebit += sourceDebit;
                    returnedTotal += reported;
                }
            }
        }
        string afterTailSweepDiagnostic = (float)Property(bucket, "CurrentUnits") > 0f
            ? FormatReturnStopDiagnostic(model, "after second local sweep")
            : null;
        Assert.That(returnedTotal, Is.EqualTo(6d).Within(.00001d));
        Assert.That(firstLoadSourceDebit + secondLoadSourceDebit, Is.EqualTo(6d).Within(.00001d),
            $"Both bucket loads must debit exactly six source units, with only bounded representation error. First return diagnostic: {firstReturnDiagnostic}");
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.Zero,
            $"All six available source units must fit across the two excavated support layers. " +
            $"Second sweep debited={tailSweepDebit:R}; before=[{beforeTailSweepDiagnostic}]; after=[{afterTailSweepDiagnostic}]");
        Assert.That((float)Property(model, "TotalUnits"), Is.EqualTo(12f).Within(.0001f));
        Assert.That((float)Property(site, "RemovedSoilUnits"), Is.Zero.Within(.00001f));
        Assert.That(firstReturnDiagnostic, Is.Null,
            $"The return phase must not mask an oversized physical/source mismatch before recollection. {firstReturnDiagnostic}");
        Vector3 point = PitPoint();
        for (int load = 0; load < 4; load++)
        {
            Call(bucket, "Empty", false, Vector3.zero);
            double collectedThisLoad = CollectUntilBucketFullOrFieldEmpty(point, $"recollect load {load}");
            Assert.That(collectedThisLoad, Is.GreaterThan(0d), $"load {load}");
            if (load < 3)
                Assert.That(Property(site, "DiggingCycleIndex"), Is.EqualTo(1),
                    "A layer must not advance while returned soil remains in the local field.");
        }

        Assert.That((float)Property(model, "TotalUnits"), Is.Zero);
        Assert.That(Property(site, "DiggingCycleIndex"), Is.EqualTo(2));
        Assert.That(Property(site, "DiggingSubstage").ToString(), Is.EqualTo("Loosening"));
        Assert.That((float)Property(site, "RemovedSoilUnits"), Is.EqualTo(12f).Within(.00001f));
    }

    [Test]
    public void LooseSoilFinalPrePourReturnRecollectsAndConcreteOrActiveFailureRejectsReturn()
    {
        PrepareCollection(18f, 3);
        Vector3 point = PitPoint();
        Set(site, "currentStage", Enum.Parse(stageType, "ConcretePouring"));
        Set(site, "diggingCycleIndex", 3);
        Set(site, "concreteFailureState", Enum.Parse(Type.GetType("FoundationConcreteFailureState, Assembly-CSharp"), "Ready"));
        Call(bucket, "TryAddUnits", substance, 1f);
        float returned = Return(1f, point);
        Assert.That(returned, Is.GreaterThan(0f));
        Assert.That(Property(site, "CurrentStage").ToString(), Is.EqualTo("Digging"));
        Assert.That(Property(site, "DiggingCycleIndex"), Is.EqualTo(2));
        Assert.That((float)Property(site, "RemovedSoilUnits"), Is.EqualTo(18f - returned).Within(.00001f));
        Assert.That(soilSurface.localPosition.y, Is.EqualTo(-1.2f).Within(.00001f));

        Call(bucket, "Empty", false, Vector3.zero);
        Assert.That(Collect(point, 100f, 1f, returned + .00001f), Is.GreaterThan(0f));
        Assert.That(Property(site, "CurrentStage").ToString(), Is.EqualTo("ConcretePouring"));

        Set(site, "concreteLoadsPoured", 0);
        Set(site, "concreteFailureState", Enum.Parse(Type.GetType("FoundationConcreteFailureState, Assembly-CSharp"), "HardenedFailure"));
        Call(bucket, "TryAddUnits", substance, 1f);
        float rejectedBucket = (float)Property(bucket, "CurrentUnits");
        double rejectedField = (double)Property(Property(field, "HeightField"), "TotalUnitsPrecise");
        Assert.That(Return(1f, point), Is.Zero);
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(rejectedBucket));
        Assert.That((double)Property(Property(field, "HeightField"), "TotalUnitsPrecise"), Is.EqualTo(rejectedField));

        Set(site, "concreteFailureState", Enum.Parse(Type.GetType("FoundationConcreteFailureState, Assembly-CSharp"), "Ready"));
        Set(site, "concreteLoadsPoured", 1);
        Assert.That(Return(1f, point), Is.Zero);
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(rejectedBucket));
        Assert.That((double)Property(Property(field, "HeightField"), "TotalUnitsPrecise"), Is.EqualTo(rejectedField));
    }

    [Test]
    public void LooseSoilReturnWithUnrepresentableSourceDoesNotMutateEitherSide()
    {
        PrepareCollection(0.00000001f, 0);
        object model = Property(field, "HeightField");
        Vector3 point = PitPoint();
        float counterBefore = (float)Property(site, "RemovedSoilUnits");
        Call(model, "Remove", .05f, .1f, .01f, 1f);
        Assert.That((float)Call(model, "GetHeight", 16, 10), Is.Zero,
            "The return fixture needs a real local hole that can represent a tiny addition.");
        Call(bucket, "Empty", false, Vector3.zero);
        Call(bucket, "TryAddUnits", substance, 1f);
        object before = Call(model, "CaptureSnapshot");
        double sourceBefore = (double)Property(model, "TotalUnitsPrecise");
        float bucketBefore = (float)Property(bucket, "CurrentUnits");

        Assert.That(Return(0.00000001f, point), Is.Zero);
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(bucketBefore));
        Assert.That((double)Property(model, "TotalUnitsPrecise"), Is.EqualTo(sourceBefore));
        Assert.That(SnapshotsDiffer(before, Call(model, "CaptureSnapshot")), Is.False);
        Assert.That((float)Property(site, "RemovedSoilUnits"), Is.EqualTo(counterBefore));
    }

    private void Apply(object stage, object substage, float removedUnits, int cycleIndex) =>
        Call(volume, "ApplyDiggingState", stage, substage, 0f, removedUnits, workflow, cycleIndex);

    private void PrepareCollection(float removedUnits, int cycleIndex)
    {
        Call(bucket, "Empty", false, Vector3.zero);
        Call(volume, "ResetLooseSoilState");
        Set(site, "currentStage", Enum.Parse(stageType, "Digging"));
        Set(site, "diggingSubstage", Enum.Parse(substageType, "SoilRemoval"));
        Set(site, "diggingCycleIndex", cycleIndex);
        Set(site, "removedSoilUnits", removedUnits);
        Apply(Enum.Parse(stageType, "Digging"), Enum.Parse(substageType, "SoilRemoval"), removedUnits, cycleIndex);
    }

    private float Collect(Vector3 position, float radius, float requested, float tailThreshold) =>
        (float)Call(site, "TryCollectLooseSoil", bucket, substance, position, radius, requested, tailThreshold);

    private float Return(float requested, Vector3 position) =>
        (float)Call(site, "TryReturnLooseSoilFromBucket", bucket, substance, position, requested);

    private bool HasPositiveLocalReturnOpportunity(object model, Vector3 worldPosition, float sourceUnits)
    {
        Vector3 local = ((Component)field).transform.InverseTransformPoint(worldPosition);
        float radius = (float)Property(field, "ReturnRadius");
        float ceiling = (float)Property(workflow, "FinalExcavationDepth") /
                        (int)Property(workflow, "DiggingCycleCount") *
                        ((int)Property(site, "DiggingCycleIndex") + 1);
        double capacity = (double)Call(model, "GetAdditionCapacity", local.x, local.z, radius, ceiling);
        double sourceBudget = Math.Min(sourceUnits,
            Math.Min((float)Property(site, "RemovedSoilUnits"), capacity));
        double preview = (double)Call(bucket, "GetMaximumRemovableUnits", substance, sourceBudget);
        return capacity > 0d && preview > 0d;
    }

    private string FormatReturnDiagnostic(string role, object model, Vector3 worldPosition,
        float sourceBefore, float sourceAfter, float reported, double fieldBefore, double fieldAfter,
        double sourceDebit, double balanceBefore, double balanceAfter)
    {
        Vector3 local = ((Component)field).transform.InverseTransformPoint(worldPosition);
        float radius = (float)Property(field, "ReturnRadius");
        float ceiling = (float)Property(workflow, "FinalExcavationDepth") /
                        (int)Property(workflow, "DiggingCycleCount") *
                        ((int)Property(site, "DiggingCycleIndex") + 1);
        double capacity = (double)Call(model, "GetAdditionCapacity", local.x, local.z, radius, ceiling);
        double sourceBudget = Math.Min(sourceBefore,
            Math.Min((float)Property(site, "RemovedSoilUnits"), capacity));
        double preview = (double)Call(bucket, "GetMaximumRemovableUnits", substance, sourceBudget);
        return $"role={role}; stage={Property(site, "CurrentStage")}/{Property(site, "DiggingSubstage")}; " +
               $"position=({local.x:R},{local.y:R},{local.z:R}); radius={radius:R}; ceiling={ceiling:R}; " +
               $"maximumHeight={Property(model, "MaximumHeight")}; volumePerUnit={Property(model, "VolumePerUnit")}; " +
               $"localCapacity={capacity:R}; sourceBudget={sourceBudget:R}; preview={preview:R}; " +
               $"source={sourceBefore:R}->{sourceAfter:R}; reported={reported:R}; sourceDebit={sourceDebit:R}; " +
               $"fieldUnits={fieldBefore:R}->{fieldAfter:R}; balance={balanceBefore:R}->{balanceAfter:R}";
    }

    private string FormatReturnStopDiagnostic(object model, string role)
    {
        double fieldUnits = (double)Property(model, "TotalUnitsPrecise");
        double balance = (double)siteType.GetField("collectionRoundingBalance", Instance).GetValue(site);
        int cycle = (int)Property(site, "DiggingCycleIndex");
        bool returnedOverlay = Property(site, "DiggingSubstage").ToString() == "ReturnedSoilRemoval";
        double cycleStart = cycle * (int)Property(workflow, "SoilUnitsPerCycle");
        double baseline = returnedOverlay ? cycleStart : cycleStart + (int)Property(workflow, "SoilUnitsPerCycle");
        double logicalAvailable = Math.Max(0d, baseline - fieldUnits + balance);
        float sourceUnits = (float)Property(bucket, "CurrentUnits");
        float radius = (float)Property(field, "ReturnRadius");
        int supportLayers = returnedOverlay ? cycle : cycle + 1;
        float ceiling = (float)Property(workflow, "FinalExcavationDepth") /
                        (int)Property(workflow, "DiggingCycleCount") * supportLayers;
        double maxPhysicalHeadroom = 0d;
        double maxLogicalHeadroom = 0d;
        double maxPreview = -1d;
        double maxPhysicalRequest = 0d;
        Vector3 maxPoint = default;
        for (int row = 0; row < (int)Property(model, "Rows"); row++)
        for (int column = 0; column < (int)Property(model, "Columns"); column++)
        {
            float x = -3.25f + column * .2f + .1f;
            float z = -2f + row * .2f + .1f;
            double physicalHeadroom = (double)Call(model, "GetAdditionCapacity", x, z, radius, ceiling);
            double logicalHeadroom = Math.Max(0d, physicalHeadroom + balance);
            double budget = Math.Min(sourceUnits, Math.Min(logicalAvailable, logicalHeadroom));
            double preview = (double)Call(bucket, "GetMaximumRemovableUnits", substance, budget);
            double physicalRequest = Math.Max(0d, preview - balance);
            maxPhysicalHeadroom = Math.Max(maxPhysicalHeadroom, physicalHeadroom);
            maxLogicalHeadroom = Math.Max(maxLogicalHeadroom, logicalHeadroom);
            if (preview > maxPreview)
            {
                maxPreview = preview;
                maxPhysicalRequest = physicalRequest;
                maxPoint = ((Component)field).transform.TransformPoint(new Vector3(x, 0f, z));
            }
        }

        return $"role={role}; stage={Property(site, "CurrentStage")}/{Property(site, "DiggingSubstage")}; cycle={cycle}; " +
               $"source={sourceUnits:R}; F={fieldUnits:R}; R={balance:R}; B={baseline:R}; C={logicalAvailable:R}; " +
               $"ceiling={ceiling:R}; maxH={maxPhysicalHeadroom:R}; maxLogicalH={maxLogicalHeadroom:R}; " +
               $"preview={maxPreview:R}; physicalRequest={maxPhysicalRequest:R}; bestPoint={maxPoint}; " +
               $"VPU={Property(model, "VolumePerUnit")}; TotalUnits={Property(model, "TotalUnits")}";
    }

    private void CollectUntilBucketFull(Vector3 point, string context)
    {
        double moved = CollectUntilBucketFullOrFieldEmpty(point, context);
        Assert.That(moved, Is.GreaterThan(0d), context);
        Assert.That((float)Property(bucket, "CurrentUnits"), Is.EqualTo(3f).Within(.000001f), context);
    }

    private double CollectUntilBucketFullOrFieldEmpty(Vector3 point, string context)
    {
        object model = Property(field, "HeightField");
        double totalMoved = 0d;
        int calls = 0;
        while ((float)Call(bucket, "GetAvailableCapacity", substance) > 0f &&
               (double)Property(model, "TotalUnitsPrecise") > 0d &&
               Property(site, "CurrentStage").ToString() == "Digging" &&
               Property(site, "DiggingSubstage").ToString() is "SoilRemoval" or "ReturnedSoilRemoval")
        {
            float moved = Collect(point, 100f, .25f, .05f);
            if (moved <= 0f)
            {
                string stopDiagnostic = FormatCollectionStopDiagnostic(model, point, calls);
                float repeatedMove = Collect(point, 100f, .25f, .05f);
                Assert.That(repeatedMove, Is.Zero,
                    $"A repeated identical transfer unexpectedly progressed after the first zero. {stopDiagnostic}");
                Assert.Fail($"{context}, call={calls}; {stopDiagnostic}");
            }
            Assert.That(moved, Is.GreaterThan(0f),
                $"{context}, call={calls}; point={point}; stage={Property(site, "CurrentStage")}/" +
                $"{Property(site, "DiggingSubstage")}; bucket={Property(bucket, "CurrentUnits")}; " +
                $"capacity={Call(bucket, "GetAvailableCapacity", substance)}; " +
                $"field={Property(model, "TotalUnitsPrecise")}; cycle={Property(site, "DiggingCycleIndex")}; " +
                $"removed={Property(site, "RemovedSoilUnits")}");
            totalMoved += moved;
            calls++;
            Assert.That(calls, Is.LessThan(512), $"{context}: transfer failed to fill a bucket in bounded calls.");
        }
        return totalMoved;
    }

    private string FormatCollectionStopDiagnostic(object model, Vector3 worldPoint, int call)
    {
        Vector3 local = ((Component)field).transform.InverseTransformPoint(worldPoint);
        double fieldUnits = (double)Property(model, "TotalUnitsPrecise");
        double balance = (double)siteType.GetField("collectionRoundingBalance", Instance).GetValue(site);
        double logicalSource = Math.Max(0d, fieldUnits - balance);
        double bucketCapacity = (float)Call(bucket, "GetAvailableCapacity", substance);
        double budget = Math.Min(.25d, Math.Min(bucketCapacity, logicalSource));
        float bucketUnits = (float)Property(bucket, "CurrentUnits");
        MethodInfo addPreviewMethod = siteType.GetMethod("MaximumAddRequestWithinBudget",
            BindingFlags.Static | BindingFlags.NonPublic);
        float addPreview = (float)addPreviewMethod.Invoke(null, new object[]
        {
            (float)budget, bucketUnits, (float)bucketCapacity, (int)Property(bucket, "Capacity"), budget
        });
        double minCellQuantum = double.PositiveInfinity;
        float maxHeight = 0f;
        float minHeight = float.PositiveInfinity;
        double volumePerUnit = (float)Property(model, "VolumePerUnit");
        for (int row = 0; row < (int)Property(model, "Rows"); row++)
        for (int column = 0; column < (int)Property(model, "Columns"); column++)
        {
            float height = (float)Call(model, "GetHeight", column, row);
            maxHeight = Mathf.Max(maxHeight, height);
            if (height <= 0f) continue;
            minHeight = Mathf.Min(minHeight, height);
            float lower = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(height) - 1);
            double quantum = (double)(height - lower) * (float)Call(model, "CellWidth", column) *
                             (float)Call(model, "CellLength", row) / volumePerUnit;
            minCellQuantum = Math.Min(minCellQuantum, quantum);
        }
        return $"call={call}; stage={Property(site, "CurrentStage")}/{Property(site, "DiggingSubstage")}; " +
               $"cycle={Property(site, "DiggingCycleIndex")}; local=({local.x:R},{local.y:R},{local.z:R}); " +
               $"fieldUnits={fieldUnits:R}; sourceUnits={bucketUnits:R}; " +
               $"bucketCapacity={bucketCapacity:R}; logicalSource={logicalSource:R}; nextRequestBudget={budget:R}; " +
               $"destinationAddPreview={addPreview:R}; " +
               $"R={balance:R}; heightRange=[{minHeight:R},{maxHeight:R}]; " +
               $"minimumEligibleCellRemovalQuantum={minCellQuantum:R}; VPU={volumePerUnit:R}";
    }

    private Vector3 PitPoint() => ((Component)field).transform.TransformPoint(new Vector3(.05f, 0f, .1f));

    private static bool MeshVerticesChanged(Vector3[] before, Mesh mesh)
    {
        Vector3[] after = mesh.vertices;
        if (before.Length != after.Length) return true;
        for (int i = 0; i < before.Length; i++)
            if (Mathf.Abs(before[i].y - after[i].y) > .000001f) return true;
        return false;
    }

    private static double SingleSpacing(float value)
    {
        int bits = BitConverter.SingleToInt32Bits(Mathf.Abs(value));
        return bits >= BitConverter.SingleToInt32Bits(float.MaxValue)
            ? 0d
            : (double)BitConverter.Int32BitsToSingle(bits + 1) - Mathf.Abs(value);
    }

    private static bool SnapshotsDiffer(object before, object after)
    {
        int count = (int)Property(before, "CellCount");
        for (int i = 0; i < count; i++)
            if ((float)Call(before, "GetHeightAtIndex", i) != (float)Call(after, "GetHeightAtIndex", i)) return true;
        return false;
    }

    private static object Property(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private static object Call(object target, string name, params object[] args)
    {
        MethodInfo method = Array.Find(target.GetType().GetMethods(Instance), candidate =>
        {
            if (candidate.Name != name || candidate.GetParameters().Length != args.Length) return false;
            ParameterInfo[] parameters = candidate.GetParameters();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == null ? parameters[i].ParameterType.IsValueType :
                    !parameters[i].ParameterType.IsInstanceOfType(args[i])) return false;
            return true;
        });
        Assert.That(method, Is.Not.Null, $"No matching overload for {target.GetType().Name}.{name}.");
        return method.Invoke(target, args);
    }
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Instance).SetValue(target, value);
}
