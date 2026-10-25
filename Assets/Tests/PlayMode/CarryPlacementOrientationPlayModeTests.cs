using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class CarryPlacementOrientationPlayModeTests
{
    private static readonly System.Type InteractionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp", true);
    private static readonly System.Type PickableType = System.Type.GetType("IPIckableNew, Assembly-CSharp", true);
    private static readonly System.Type OrientationType = System.Type.GetType("CarryPlacementOrientation, Assembly-CSharp", true);
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTest]
    public IEnumerator PickupUsesRootOffsetAndNextUnconfiguredPickupHasNoStaleOffset()
    {
        System.Type networkManagerType = System.Type.GetType("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime", true);
        object manager = networkManagerType.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        Assert.That(manager == null || !(bool)networkManagerType.GetProperty("IsListening").GetValue(manager), Is.True,
            "This direct pickup fixture requires an offline player; a listening manager means the required coverage precondition was not met.");

        GameObject playerPrefab = null;
#if UNITY_EDITOR
        playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
#else
        Assert.Fail("This test fixture requires the Unity Editor AssetDatabase to load PlayerNew.prefab.");
#endif
        Assert.That(playerPrefab, Is.Not.Null);
        GameObject player = Object.Instantiate(playerPrefab);
        GameObject configuredSource = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject legacySource = GameObject.CreatePrimitive(PrimitiveType.Cube);
        player.transform.position = new Vector3(0f, 100f, 0f);
        configuredSource.name = "Synthetic parented carry with configured root offset";
        legacySource.name = "Synthetic parented carry without configured root offset";
        try
        {
            yield return null;
            Component orientation = configuredSource.AddComponent(OrientationType);
            OrientationType.GetField("offsetEulerDegrees", InstanceNonPublic)
                .SetValue(orientation, new Vector3(0f, 90f, 0f));
            Component interaction = player.GetComponent(InteractionType);
            MethodInfo pickup = InteractionType.GetMethod("PickUpObject", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(GameObject), PickableType }, null);
            Transform hold = (Transform)InteractionType.GetMethod("GetPickUpHoldPositionHolder").Invoke(interaction, null);

            pickup.Invoke(interaction, new object[] { configuredSource, null });
            Assert.That(configuredSource.transform.parent, Is.EqualTo(hold), "A synthetic non-network source uses the production parented pickup path.");
            Assert.That(Quaternion.Angle(configuredSource.transform.localRotation, Quaternion.Euler(0f, 90f, 0f)), Is.LessThan(.1f));
            hold.rotation = Quaternion.Euler(0f, 37f, 0f);
            Assert.That(Quaternion.Angle(configuredSource.transform.rotation, hold.rotation * Quaternion.Euler(0f, 90f, 0f)), Is.LessThan(.1f),
                "The held local orientation follows subsequent hold-point rotation.");

            InteractionType.GetMethod("ForceReleasePickedUpObject").Invoke(interaction, new object[] { configuredSource });
            pickup.Invoke(interaction, new object[] { legacySource, null });
            Assert.That(legacySource.transform.parent, Is.EqualTo(hold));
            Assert.That(Quaternion.Angle(legacySource.transform.localRotation, Quaternion.identity), Is.LessThan(.1f),
                "A subsequent object without the optional component receives identity instead of stale carry state.");
        }
        finally
        {
            if (configuredSource != null) configuredSource.transform.SetParent(null);
            if (legacySource != null) legacySource.transform.SetParent(null);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(configuredSource);
            Object.DestroyImmediate(legacySource);
        }
    }
}
