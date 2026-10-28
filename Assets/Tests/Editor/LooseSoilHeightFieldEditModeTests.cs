using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Type = System.Type;

public sealed class LooseSoilHeightFieldEditModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private Type modelType, snapshotType, adapterType;
    private GameObject root;

    [SetUp]
    public void SetUp()
    {
        modelType = Type.GetType("LooseSoilHeightField, Assembly-CSharp");
        adapterType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        Assert.That(modelType, Is.Not.Null);
        Assert.That(adapterType, Is.Not.Null);
        snapshotType = modelType.GetNestedType("Snapshot", BindingFlags.Public);
        Assert.That(snapshotType, Is.Not.Null);
    }

    [TearDown]
    public void TearDown()
    {
        if (root == null) return;
        Component adapter = root.GetComponent(adapterType);
        if (adapter != null) Call(adapter, "ReleaseRuntimeMesh");
        Object.DestroyImmediate(root);
    }

    [Test]
    public void LooseSoilHeightFieldSixUnitsRemoveExactlyOneLayerIncludingPartialEdgeCells()
    {
        object field = CreateModel();
        Assert.That((int)Prop(field, "Columns"), Is.EqualTo(33));
        Assert.That((int)Prop(field, "Rows"), Is.EqualTo(20));
        Assert.That((float)Call(field, "CellWidth", 32), Is.EqualTo(.1f).Within(.00001f));
        Assert.That((float)Call(field, "CellLength", 19), Is.EqualTo(.2f).Within(.00001f));
        Assert.That((float)Prop(field, "TotalVolume"), Is.EqualTo(10.4f).Within(.0001f));
        Assert.That((float)Prop(field, "TotalUnits"), Is.EqualTo(6f).Within(.0001f));
        float units = (float)Call(field, "Remove", 0f, 0f, 20f, 6f);
        Assert.That(units, Is.EqualTo(6f).Within(.0001f));
        Assert.That((float)Call(field, "GetHeight", 16, 10), Is.EqualTo(0f).Within(.00001f));
        Assert.That((float)Call(field, "Add", 0f, 0f, 20f, 6f), Is.EqualTo(6f).Within(.0001f));

        object edgeField = CreateModel();
        float edgeTransfer = (float)Call(edgeField, "Remove", 3.2f, 1.9f, .001f, 1f);
        Assert.That(edgeTransfer, Is.GreaterThan(0f).And.LessThan(.02f),
            "A request clipped to the real 0.1 x 0.2 m edge cell must return its actual fractional volume.");
    }

    [Test]
    public void LooseSoilHeightFieldWholeFieldRemovalCollectsExactLayerAcrossPartialEdges()
    {
        object field = CreateModel();
        float collected = (float)Call(field, "RemoveEverywhere", 6f);
        Assert.That(collected, Is.EqualTo(6f).Within(.00001f));
        Assert.That((float)Prop(field, "TotalUnits"), Is.EqualTo(0f).Within(.00001f));
        Assert.That((float)Call(field, "RemoveEverywhere", .1f), Is.Zero);
        Call(field, "Clear");
        Assert.That((float)Prop(field, "TotalUnits"), Is.Zero);
    }

    [Test]
    public void LooseSoilHeightFieldRepeatedPartialTransfersRemainConservativeNearCellLimits()
    {
        const float width = 0.6f;
        const float length = 0.4f;
        object field = CreateModel(width, length);
        float initialVolume = (float)Prop(field, "TotalVolume");
        float[] requestedUnits = { 0.049f, 0.000001f, 0.137f, 0.0000008f, 0.05f, 0.011f };
        float[] positions = { -0.2f, 0f, 0.2f };

        for (int i = 0; i < 120; i++)
        {
            float x = positions[i % positions.Length];
            float radius = (i % 4 == 0) ? 0.21f : 0.15f;
            float request = requestedUnits[i % requestedUnits.Length];
            float beforeRemoval = (float)Prop(field, "TotalVolume");

            float removed = (float)Call(field, "Remove", x, 0f, radius, request);
            float afterRemoval = (float)Prop(field, "TotalVolume");
            Assert.That(removed, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(request));
            Assert.That(afterRemoval, Is.LessThanOrEqualTo(beforeRemoval + 0.000001f),
                "Removal must never create material, including when the remaining volume is close to a cell-height ULP.");

            float added = removed > 0f ? (float)Call(field, "Add", x, 0f, radius, removed) : 0f;
            float afterAddition = (float)Prop(field, "TotalVolume");
            Assert.That(added, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(removed + 0.000001f));
            Assert.That(afterAddition, Is.LessThanOrEqualTo(initialVolume + 0.000001f),
                "Adding back the measured transfer must not create material beyond the original field volume.");

            for (int row = 0; row < (int)Prop(field, "Rows"); row++)
            for (int column = 0; column < (int)Prop(field, "Columns"); column++)
            {
                float height = (float)Call(field, "GetHeight", column, row);
                Assert.That(height, Is.InRange(0f, 0.4f), "Cell heights must remain finite and within the configured layer.");
            }
        }

        Assert.That((float)Prop(field, "TotalVolume"), Is.LessThanOrEqualTo(initialVolume + 0.000001f));
    }

    [Test]
    public void LooseSoilHeightFieldAdditionFromNegativeZeroSnapshotKeepsHeightsAndCapacityValid()
    {
        object field = CreateModel(0.2f, 0.2f);
        object template = Call(field, "CaptureSnapshot");
        double volumePerUnit = (double)snapshotType.GetField("VolumePerUnit", Instance).GetValue(template);
        float negativeZero = BitConverter.Int32BitsToSingle(int.MinValue);
        object[] snapshotArguments =
        {
            0.2f, 0.2f, 0.2f, 0.4f, volumePerUnit, 1, 1, new[] { negativeZero }, null
        };
        MethodInfo createSnapshot = modelType.GetMethod("TryCreateSnapshot", BindingFlags.Public | BindingFlags.Static);
        Assert.That((bool)createSnapshot.Invoke(null, snapshotArguments), Is.True);
        Assert.That((bool)Call(field, "RestoreSnapshot", snapshotArguments[8]), Is.True);
        float restoredHeight = (float)Call(field, "GetHeight", 0, 0);
        Assert.That(BitConverter.SingleToInt32Bits(restoredHeight), Is.EqualTo(int.MinValue),
            "The public snapshot API must preserve the valid negative-zero input used by this regression.");

        const float request = 0.0001f;
        float added = (float)Call(field, "Add", 0f, 0f, 0.15f, request);
        float height = (float)Call(field, "GetHeight", 0, 0);
        float unitsAfter = (float)Prop(field, "TotalUnits");

        Assert.That(added, Is.GreaterThan(0f).And.LessThanOrEqualTo(request));
        Assert.That(float.IsNaN(height) || float.IsInfinity(height), Is.False);
        Assert.That(height, Is.InRange(0f, 0.4f));
        Assert.That(Math.Abs(unitsAfter - request), Is.LessThan(0.0000001f),
            "A tiny request from an empty negative-zero cell must add nearby representable volume instead of spending only a subnormal height.");
    }

    [Test]
    public void LooseSoilHeightFieldLocalTransfersRespectBoundsAndRejectInvalidInputs()
    {
        object field = CreateModel();
        Assert.That((float)Call(field, "Remove", -3.15f, .1f, .01f, 1f), Is.GreaterThan(0f));
        float untouched = (float)Call(field, "GetHeight", 16, 10);
        Assert.That(untouched, Is.EqualTo(.4f).Within(.00001f));
        Assert.That((float)Call(field, "Add", -3.15f, .1f, .01f, 99f), Is.LessThan(1f));
        Assert.That((float)Call(field, "GetHeight", 0, 10), Is.EqualTo(.4f).Within(.00001f));

        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.That((float)Call(field, "Remove", 0f, 0f, .5f, invalid), Is.Zero);
            Assert.That((float)Call(field, "Add", 0f, 0f, .5f, invalid), Is.Zero);
        }
        Assert.That((float)Call(field, "Remove", float.NaN, 0f, .5f, 1f), Is.Zero);
        Assert.That((float)Call(field, "Add", 0f, 0f, -1f, 1f), Is.Zero);
        TargetInvocationException oversized = Assert.Throws<TargetInvocationException>(() =>
            Activator.CreateInstance(modelType, new object[] { 10000f, 10000f, .01f, .4f, .4f, 6f }));
        Assert.That(oversized.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void LooseSoilHeightFieldSmallTransfersConserveReportedUnitsWithoutOverspending()
    {
        object field = CreateModel();
        float requested = .0001f;
        float removed = (float)Call(field, "Remove", 0f, 0f, .15f, requested);
        Assert.That(removed, Is.GreaterThan(0f).And.LessThanOrEqualTo(requested));
        Assert.That(6f - (float)Prop(field, "TotalUnits"), Is.EqualTo(removed).Within(.00001f));
        float returned = (float)Call(field, "Add", 0f, 0f, .15f, removed);
        Assert.That(returned, Is.EqualTo(removed).Within(.00001f));
        Assert.That((float)Prop(field, "TotalUnits"), Is.EqualTo(6f).Within(.00001f));
    }

    [Test]
    public void LooseSoilHeightFieldExpansionPreservesLayerCalibrationAndCarvedShape()
    {
        object field = CreateModel(0.6f, 0.4f);
        Call(field, "Remove", -0.1f, 0f, 0.16f, 1.25f);
        float[] before = CaptureHeights(field);
        object beforeSnapshot = Call(field, "CaptureSnapshot");
        FieldInfo snapshotVolumePerUnit = snapshotType.GetField("VolumePerUnit", Instance);
        double volumePerUnit = (double)snapshotVolumePerUnit.GetValue(beforeSnapshot);
        double unitsBefore = (double)Prop(field, "TotalUnitsPrecise");
        ulong revisionBefore = (ulong)Prop(field, "MutationRevision");

        Assert.That((bool)Call(field, "ExpandMaximumHeight", 1.2f), Is.True);
        Assert.That((float)Prop(field, "MaximumHeight"), Is.EqualTo(1.2f));
        object expandedSnapshot = Call(field, "CaptureSnapshot");
        Assert.That((double)snapshotVolumePerUnit.GetValue(expandedSnapshot), Is.EqualTo(volumePerUnit));
        Assert.That((double)Prop(field, "TotalUnitsPrecise"), Is.EqualTo(unitsBefore));
        Assert.That((ulong)Prop(field, "MutationRevision"), Is.GreaterThan(revisionBefore));
        Assert.That((bool)Call(field, "CanRestoreSnapshot", beforeSnapshot), Is.False,
            "A snapshot with the prior capacity must not be mistaken for the expanded configuration.");
        for (int row = 0; row < (int)Prop(field, "Rows"); row++)
        for (int column = 0; column < (int)Prop(field, "Columns"); column++)
            Assert.That((float)Call(field, "GetHeight", column, row),
                Is.EqualTo(before[row * (int)Prop(field, "Columns") + column]));

        Assert.That((bool)Call(field, "ExpandMaximumHeight", 0.8f), Is.False,
            "Expansion cannot silently lower capacity or invalidate existing heights.");
        Assert.That((float)Prop(field, "MaximumHeight"), Is.EqualTo(1.2f));
    }

    [Test]
    public void LooseSoilHeightFieldBoundedAdditionReportsOnlyAcceptedUnits()
    {
        object field = CreateModel(0.2f, 0.2f);
        Call(field, "Remove", 0f, 0f, 0.15f, 6f);
        Assert.That((bool)Call(field, "ExpandMaximumHeight", 1.2f), Is.True);
        double before = (double)Prop(field, "TotalUnitsPrecise");

        float accepted = (float)Call(field, "AddWithHeightCeiling", 0f, 0f, 0.15f, 20f, 0.8f);
        double addedFromShape = (double)Prop(field, "TotalUnitsPrecise") - before;
        Assert.That(accepted, Is.GreaterThan(0f).And.LessThan(20f));
        Assert.That(addedFromShape, Is.EqualTo(accepted).Within(0.000001d));
        Assert.That((float)Call(field, "GetHeight", 0, 0), Is.InRange(0.799999f, 0.8f));
        ulong saturatedRevision = (ulong)Prop(field, "MutationRevision");
        Assert.That((float)Call(field, "AddWithHeightCeiling", 0f, 0f, 0.15f, 1f, 0.8f), Is.Zero);
        Assert.That((ulong)Prop(field, "MutationRevision"), Is.EqualTo(saturatedRevision));
        Assert.That((float)Call(field, "AddWithHeightCeiling", 0f, 0f, 0.15f, 1f, 1.3f), Is.Zero,
            "A per-call ceiling above field capacity must be rejected rather than bypassing the cap.");
        Assert.That((float)Call(field, "AddWithHeightCeiling", 0f, 0f, 0.15f, 1f, 0.4f), Is.Zero,
            "A ceiling below the existing cell height must not remove or overdraw material.");
    }

    [Test]
    public void LooseSoilHeightFieldBoundedAdditionHonorsLocalRadiusAndPartialEdges()
    {
        object field = CreateModel(0.3f, 0.2f);
        Call(field, "Remove", -0.05f, 0f, 0.07f, 6f);
        Assert.That((bool)Call(field, "ExpandMaximumHeight", 1.2f), Is.True);
        double before = (double)Prop(field, "TotalUnitsPrecise");
        float outsideCellBefore = (float)Call(field, "GetHeight", 1, 0);

        float accepted = (float)Call(field, "AddWithHeightCeiling", -0.05f, 0f, 0.07f, 20f, 0.8f);
        double addedFromShape = (double)Prop(field, "TotalUnitsPrecise") - before;
        Assert.That(accepted, Is.GreaterThan(0f).And.LessThan(20f));
        Assert.That(addedFromShape, Is.EqualTo(accepted).Within(0.000001d));
        Assert.That((float)Call(field, "GetHeight", 0, 0), Is.InRange(0.799999f, 0.8f));
        Assert.That((float)Call(field, "GetHeight", 1, 0), Is.EqualTo(outsideCellBefore),
            "The adjacent cell outside the brush radius must remain untouched.");

        double beforeTiny = (double)Prop(field, "TotalUnitsPrecise");
        float tiny = (float)Call(field, "AddWithHeightCeiling", 0.1f, 0f, 0.01f, 0.0000001f, 0.8f);
        double tinyFromShape = (double)Prop(field, "TotalUnitsPrecise") - beforeTiny;
        Assert.That(tiny, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(0.0000001f));
        Assert.That(tinyFromShape, Is.EqualTo(tiny).Within(0.000000001d),
            "The returned amount must match any representable shape change.");
        Assert.That((float)Call(field, "GetHeight", 0, 0), Is.EqualTo(0.8f));
        Assert.That((float)Call(field, "GetHeight", 1, 0), Is.LessThanOrEqualTo(0.8f));
    }

    [Test]
    public void LooseSoilHeightFieldAdditionCapacityMatchesActualBrushAtBoundaries()
    {
        Vector2[] centers =
        {
            new Vector2(-3.15f, -1.9f), // clipped lower-left edge and an axial radius boundary
            new Vector2(3.15f, 1.9f),   // clipped upper-right edge
            new Vector2(0f, 0f)         // interior brush
        };

        foreach (Vector2 center in centers)
        {
            object field = CreateModel();
            Assert.That((bool)Call(field, "ExpandMaximumHeight", 1.2f), Is.True);
            double capacity = (double)Call(field, "GetAdditionCapacity", center.x, center.y, .6f, .8f);
            double before = (double)Prop(field, "TotalUnitsPrecise");
            float accepted = (float)Call(field, "AddWithHeightCeiling", center.x, center.y, .6f, 20f, .8f);
            double represented = (double)Prop(field, "TotalUnitsPrecise") - before;

            Assert.That(capacity, Is.GreaterThan(0d), $"Test brush should have capacity at {center}.");
            Assert.That(accepted, Is.EqualTo(capacity).Within(.000001d),
                $"Reported brush capacity must match saturating Add at {center}.");
            Assert.That(represented, Is.EqualTo(capacity).Within(.000001d),
                $"The changed cells must represent the queried brush capacity at {center}.");
        }
    }

    [Test]
    public void LooseSoilHeightFieldSpendsOneUlpBucketCapacityAcrossTallCells()
    {
        object field = CreateModel();
        Assert.That((bool)Call(field, "ExpandMaximumHeight", 1.2f), Is.True);
        Assert.That((float)Call(field, "AddWithHeightCeiling", 0f, 0f, 100f, 6f, 0.8f),
            Is.EqualTo(6f).Within(0.00001f));
        Assert.That((float)Call(field, "GetHeight", 16, 10), Is.EqualTo(0.8f));

        float almostFull = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(3f) - 1);
        float oneUlpCapacity = 3f - almostFull;
        object before = Call(field, "CaptureSnapshot");
        double unitsBefore = (double)Prop(field, "TotalUnitsPrecise");

        float removed = (float)Call(field, "RemoveEverywhere", oneUlpCapacity);
        double measuredRemoval = unitsBefore - (double)Prop(field, "TotalUnitsPrecise");

        Assert.That(removed, Is.GreaterThan(0f).And.LessThanOrEqualTo(oneUlpCapacity));
        Assert.That(measuredRemoval, Is.GreaterThan(0d).And.LessThanOrEqualTo(oneUlpCapacity),
            "Fallback may use representable per-cell height steps, but must remain within the source budget.");
        Assert.That((bool)Call(field, "MatchesSnapshot", before), Is.False,
            "The one-ULP bucket increment must correspond to real changes in the height grid.");
    }

    [Test]
    public void LooseSoilHeightFieldDoesNotSpendBelowSingleCellHeightQuantum()
    {
        object field = CreateModel(0.3f, 0.2f);
        Assert.That((bool)Call(field, "ExpandMaximumHeight", 1.2f), Is.True);
        Call(field, "Clear");
        Assert.That((float)Call(field, "AddWithHeightCeiling", -0.05f, 0f, 0.01f, 8f, 0.8f),
            Is.EqualTo(8f).Within(0.00001f));
        float height = (float)Call(field, "GetHeight", 0, 0);
        float nextLowerHeight = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(height) - 1);
        float smallestCellRemoval = (height - nextLowerHeight) *
                                    (float)Call(field, "CellWidth", 0) *
                                    (float)Call(field, "CellLength", 0) /
                                    (float)Prop(field, "VolumePerUnit");
        float requestBelowCellQuantum = smallestCellRemoval * 0.5f;
        object before = Call(field, "CaptureSnapshot");
        ulong revisionBefore = (ulong)Prop(field, "MutationRevision");

        Assert.That((float)Call(field, "Remove", -0.05f, 0f, 0.01f, requestBelowCellQuantum), Is.Zero);

        Assert.That((bool)Call(field, "MatchesSnapshot", before), Is.True,
            "A budget below the smallest eligible cell change must leave every height unchanged.");
        Assert.That((ulong)Prop(field, "MutationRevision"), Is.EqualTo(revisionBefore));
    }

    [Test]
    public void LooseSoilHeightFieldSnapshotRestoresDistinctShapesAndOwnsItsData()
    {
        object field = CreateModel();
        Call(field, "Remove", -1.95f, .1f, 1f, .5f);
        float snapshotUnits = (float)Prop(field, "TotalUnits");
        object snapshot = Call(field, "CaptureSnapshot");
        float savedLeftHeight = (float)Call(field, "GetHeight", 6, 10);
        Assert.That((float)Call(field, "Add", -1.95f, .1f, 1f, .5f), Is.EqualTo(.5f).Within(.0001f));
        Assert.That((float)Call(field, "Remove", 2.05f, .1f, 1f, .5f), Is.EqualTo(.5f).Within(.0001f));
        float rightHeight = (float)Call(field, "GetHeight", 26, 10);
        Assert.That((float)Call(field, "GetHeight", 6, 10), Is.EqualTo(.4f).Within(.00001f));
        Assert.That(rightHeight, Is.LessThan(.4f));
        Assert.That((float)Prop(field, "TotalUnits"), Is.EqualTo(snapshotUnits).Within(.00001f),
            "The redistributed field must have a different shape with the same total material volume.");

        Assert.That((bool)Call(field, "RestoreSnapshot", snapshot), Is.True);
        Assert.That((float)Call(field, "GetHeight", 6, 10), Is.EqualTo(savedLeftHeight).Within(.00001f));
        Assert.That((float)Call(field, "GetHeight", 26, 10), Is.EqualTo(.4f).Within(.00001f));
        Assert.That((bool)Call(CreateModel(5f, 4f), "RestoreSnapshot", snapshot), Is.False);
        Assert.That(snapshotType.GetField("heights", Instance).GetValue(snapshot), Is.Not.SameAs(
            snapshotType.GetField("heights", Instance).GetValue(Call(field, "CaptureSnapshot"))));
    }

    [Test]
    public void LooseSoilHeightFieldMutationRevisionIgnoresEqualSnapshotReplay()
    {
        object field = CreateModel();
        ulong initialRevision = (ulong)Prop(field, "MutationRevision");
        object initialSnapshot = Call(field, "CaptureSnapshot");

        Assert.That((float)Call(field, "Remove", 0f, 0f, .6f, .25f), Is.GreaterThan(0f));
        ulong carvedRevision = (ulong)Prop(field, "MutationRevision");
        Assert.That(carvedRevision, Is.GreaterThan(initialRevision));
        object carvedSnapshot = Call(field, "CaptureSnapshot");
        Assert.That((bool)Call(field, "RestoreSnapshot", carvedSnapshot), Is.True);
        Assert.That((ulong)Prop(field, "MutationRevision"), Is.EqualTo(carvedRevision),
            "Replaying the identical shape must not invalidate rounding state.");

        Assert.That((bool)Call(field, "RestoreSnapshot", initialSnapshot), Is.True);
        Assert.That((ulong)Prop(field, "MutationRevision"), Is.GreaterThan(carvedRevision),
            "Restoring a changed shape must invalidate rounding state.");
    }

    [Test]
    public void LooseSoilHeightFieldMeshAdapterBuildsOwnedMeshWithoutCollider()
    {
        root = new GameObject("Loose soil field adapter fixture");
        Component adapter = root.AddComponent(adapterType);
        Call(adapter, "Initialize", 6.5f, 4f, .2f, .4f, .4f, 6f);
        Mesh mesh = (Mesh)Prop(adapter, "RuntimeMesh");
        Assert.That(mesh, Is.Not.Null);
        Assert.That(mesh.vertexCount, Is.GreaterThan(0));
        Vector2[] uvs = mesh.uv;
        Assert.That(uvs.Length, Is.EqualTo(mesh.vertexCount));
        bool hasFullWidthUv = false, hasFullLengthUv = false;
        foreach (Vector2 uv in uvs)
        {
            hasFullWidthUv |= Mathf.Abs(uv.x - 6.5f) < .0001f;
            hasFullLengthUv |= Mathf.Abs(uv.y - 4f) < .0001f;
        }
        Assert.That(hasFullWidthUv && hasFullLengthUv, Is.True, "UVs must span the local 6.5 x 4 m footprint.");
        Assert.That(root.GetComponent<Collider>(), Is.Null, "Loose surface rendering must not add a solid collider.");
        Assert.That(root.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
        object originalField = Prop(adapter, "HeightField");
        Assert.Throws<TargetInvocationException>(() => Call(adapter, "Initialize", 6.5f, 4f, 0f, .4f, .4f, 6f));
        Assert.That(Prop(adapter, "HeightField"), Is.SameAs(originalField));
        Assert.That(Prop(adapter, "RuntimeMesh"), Is.SameAs(mesh));

        Call(adapter, "ReleaseRuntimeMesh");
        Assert.That(mesh == null, Is.True, "The adapter owns and destroys its runtime mesh.");
        Object.DestroyImmediate(root);
        root = null;
    }

    private object CreateModel(float width = 6.5f, float length = 4f) =>
        Activator.CreateInstance(modelType, new object[] { width, length, .2f, .4f, .4f, 6f });

    private float[] CaptureHeights(object field)
    {
        int columns = (int)Prop(field, "Columns"), rows = (int)Prop(field, "Rows");
        var result = new float[columns * rows];
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
            result[row * columns + column] = (float)Call(field, "GetHeight", column, row);
        return result;
    }

    private static object Prop(object instance, string name) => instance.GetType().GetProperty(name).GetValue(instance);
    private static object Call(object instance, string name, params object[] args)
    {
        MethodInfo method = null;
        foreach (MethodInfo candidate in instance.GetType().GetMethods(Instance))
        {
            if (candidate.Name != name || candidate.GetParameters().Length != args.Length) continue;
            if (method != null)
                throw new AmbiguousMatchException($"Multiple {name} overloads accept {args.Length} arguments.");
            method = candidate;
        }
        Assert.That(method, Is.Not.Null, $"Expected {name} overload accepting {args.Length} arguments.");
        return method.Invoke(instance, args);
    }
}
