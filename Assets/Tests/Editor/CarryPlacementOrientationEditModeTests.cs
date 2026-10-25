using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class CarryPlacementOrientationEditModeTests
{
    private static readonly System.Type OrientationType = System.Type.GetType("CarryPlacementOrientation, Assembly-CSharp", true);
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;

    [Test]
    public void MissingDisabledAndZeroOffsetsResolveToIdentity()
    {
        GameObject source = new("Carry orientation identity test");
        try
        {
            Assert.That(Quaternion.Angle(Resolve(source), Quaternion.identity), Is.LessThan(.001f));
            Component orientation = source.AddComponent(OrientationType);
            Assert.That(Quaternion.Angle(Resolve(source), Quaternion.identity), Is.LessThan(.001f));
            orientation.GetType().GetField("offsetEulerDegrees", InstanceNonPublic).SetValue(orientation, new Vector3(20f, 90f, -15f));
            ((Behaviour)orientation).enabled = false;
            Assert.That(Quaternion.Angle(Resolve(source), Quaternion.identity), Is.LessThan(.001f));
        }
        finally { Object.DestroyImmediate(source); }
    }

    [Test]
    public void OffsetAndReferenceRotationComposeInTheDocumentedOrder()
    {
        GameObject source = new("Carry orientation composition test");
        try
        {
            Component orientation = source.AddComponent(OrientationType);
            Vector3 euler = new(23f, 71f, -16f);
            orientation.GetType().GetField("offsetEulerDegrees", InstanceNonPublic).SetValue(orientation, euler);
            Quaternion offset = Resolve(source);
            Assert.That(Quaternion.Angle(offset, Quaternion.Euler(euler)), Is.LessThan(.001f));

            Quaternion reference = Quaternion.Euler(0f, 43f, 0f);
            Quaternion composed = Compose(reference, offset);
            Assert.That(Quaternion.Angle(composed, reference * offset), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(composed, offset * reference), Is.GreaterThan(1f),
                "The placement offset is local to the reference rotation.");
        }
        finally { Object.DestroyImmediate(source); }
    }

    [Test]
    public void NinetyDegreeOffsetRemainsComposedAcrossYaw359ToZeroBoundary()
    {
        Quaternion offset = Quaternion.Euler(0f, 90f, 0f);
        Quaternion beforeBoundary = Compose(Quaternion.Euler(0f, 359f, 0f), offset);
        Quaternion afterBoundary = Compose(Quaternion.Euler(0f, 0f, 0f), offset);
        Assert.That(Quaternion.Angle(beforeBoundary, Quaternion.Euler(0f, 89f, 0f)), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(afterBoundary, Quaternion.Euler(0f, 90f, 0f)), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(beforeBoundary, afterBoundary), Is.EqualTo(1f).Within(.001f));
    }

    [TestCase("Assets/Experiments/BridgeDeckAreaV1/Prefabs/WoodenDeckAreaStockPlate.prefab")]
    [TestCase("Assets/Experiments/BridgeDeckAreaV1/Prefabs/WoodenDeckAreaCutFragment.prefab")]
    public void BridgeDeckAreaStockAndFragmentPrefabsUseNinetyDegreeRootOffset(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);
        Component orientation = prefab.GetComponent(OrientationType);
        Assert.That(orientation, Is.Not.Null, $"The optional setting belongs on prefab root: {path}");
        Assert.That(Quaternion.Angle(Resolve(prefab), Quaternion.Euler(0f, 90f, 0f)), Is.LessThan(.001f), path);
    }

    private static Quaternion Resolve(GameObject source) => (Quaternion)OrientationType
        .GetMethod("ResolveOffset", StaticPublic).Invoke(null, new object[] { source });

    private static Quaternion Compose(Quaternion reference, Quaternion offset) => (Quaternion)OrientationType
        .GetMethod("Compose", StaticPublic).Invoke(null, new object[] { reference, offset });
}
