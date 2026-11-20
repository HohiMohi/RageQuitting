using NUnit.Framework;
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class LooseSoilStage3MovementEditModeTests
{
    private GameObject fieldRoot;
    private Component field;
    private Type fieldType;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [TearDown]
    public void TearDown()
    {
        if (field != null) Invoke(field, "ReleaseRuntimeMeshForSceneSave");
        if (fieldRoot != null) Object.DestroyImmediate(fieldRoot);
    }

    [Test]
    public void ImmersionSamplesCurrentLocalCellAndRejectsInactiveOutsideAndUnsupportedDepth()
    {
        fieldRoot = new GameObject("Stage 3 field probe");
        field = CreateField(fieldRoot, 2f, 2f, .5f, .4f, .4f, 6f);

        Assert.That(GetImmersion(new Vector3(0f, .2f, 0f)), Is.EqualTo(.5f).Within(.001f));
        Assert.That(GetImmersion(new Vector3(0f, 0f, 0f)), Is.EqualTo(1f).Within(.001f));
        Assert.That(GetImmersion(new Vector3(0f, -.00001f, 0f)), Is.EqualTo(1f).Within(.001f),
            "Tiny transform round-off at full immersion is clamped to the valid maximum.");
        Assert.That(GetImmersion(new Vector3(0f, -.1f, 0f)), Is.Zero,
            "A foot materially below the hard support has no effect.");
        Assert.That(GetImmersion(new Vector3(0f, .4f, 0f)), Is.Zero,
            "A foot above the current surface has no effect.");
        Assert.That(GetImmersion(new Vector3(1.1f, 0f, 0f)), Is.Zero,
            "A foot outside the XZ footprint has no effect.");

        SetSurface(field, false);
        Assert.That(GetImmersion(new Vector3(0f, .2f, 0f)), Is.Zero);
        SetSurface(field, true);
        ((Behaviour)field).enabled = false;
        Invoke(field, "OnDisable");
        Assert.That(GetImmersion(new Vector3(0f, .2f, 0f)), Is.Zero);
    }

    [Test]
    public void SamplingUsesFieldWorldTransformAndEmptyCellHasNoSurface()
    {
        fieldRoot = new GameObject("Stage 3 transformed field probe");
        fieldRoot.transform.SetPositionAndRotation(new Vector3(4f, 3f, -2f), Quaternion.Euler(0f, 35f, 0f));
        fieldRoot.transform.localScale = new Vector3(2f, 1.5f, .5f);
        field = CreateField(fieldRoot, 2f, 2f, .5f, .4f, .4f, 6f);
        Vector3 halfDepthFoot = field.transform.TransformPoint(new Vector3(.25f, .2f, .25f));
        Assert.That(GetImmersion(halfDepthFoot), Is.EqualTo(.5f).Within(.001f));
        object model = Property(field, "HeightField");
        Assert.That((float)model.GetType().GetMethod("Remove").Invoke(model, new object[] { .25f, .25f, .01f, .1875f }), Is.GreaterThan(0f));
        Invoke(field, "RebuildMesh");
        float loweredHeight = (float)model.GetType().GetMethod("GetHeight").Invoke(model, new object[] { 2, 2 });
        Assert.That(loweredHeight, Is.GreaterThan(0f).And.LessThan(.4f), "The queried cell remains partially filled.");
        Vector3 loweredFoot = field.transform.TransformPoint(new Vector3(.25f, -.1f, .25f));
        Assert.That(GetImmersion(loweredFoot), Is.Zero,
            "A lowered surface cannot admit a foot below the hard support.");
        Assert.That((float)model.GetType().GetMethod("Remove").Invoke(model, new object[] { .25f, .25f, .01f, .1875f }), Is.GreaterThan(0f));
        Invoke(field, "RebuildMesh");
        Assert.That(GetImmersion(field.transform.TransformPoint(new Vector3(.25f, 0f, .25f))), Is.EqualTo(0f).Within(0.000001f),
            "A fully emptied cell has no sample surface.");
        Vector3 unsupported = field.transform.TransformPoint(new Vector3(0f, -1f, 0f));
        Assert.That(GetImmersion(unsupported), Is.Zero, "A foot far below support has no effect.");
    }

    [Test]
    public void ProductionPlayerPrefabUsesSafeLooseSoilDefaultsWithoutPrefabOverrides()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        Assert.That(prefab, Is.Not.Null);
        Component controller = prefab.GetComponent(Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp"));
        Assert.That(controller, Is.Not.Null);
        UnityEditor.SerializedObject serialized = new UnityEditor.SerializedObject(controller);
        Assert.That(serialized.FindProperty("looseSoilWalkSpeedAtFullImmersion").floatValue, Is.EqualTo(.75f));
        Assert.That(serialized.FindProperty("looseSoilSprintSpeedAtFullImmersion").floatValue, Is.EqualTo(.65f));
        Assert.That(serialized.FindProperty("looseSoilJumpHeightAtFullImmersion").floatValue, Is.EqualTo(.65f));
    }

    private Component CreateField(GameObject root, float width, float length, float cellSize, float depth,
        float initial, float units)
    {
        fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        Assert.That(fieldType, Is.Not.Null);
        Component created = root.AddComponent(fieldType);
        created.GetType().GetMethod("Initialize").Invoke(created,
            new object[] { width, length, cellSize, depth, initial, units });
        Invoke(created, "OnEnable");
        SetSurface(created, true);
        return created;
    }

    private float GetImmersion(Vector3 foot) =>
        (float)fieldType.GetMethod("GetMaximumImmersion", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { foot });

    private static void SetSurface(Component target, bool visible) =>
        target.GetType().GetMethod("SetSurfaceState").Invoke(target, new object[] { visible, null });

    private static object Property(object target, string name) =>
        target.GetType().GetProperty(name, Instance).GetValue(target);

    private static void Invoke(Component target, string name) =>
        target.GetType().GetMethod(name, Instance | BindingFlags.Public).Invoke(target, null);
}
