using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class SingleCarryPlacementStage2PlayModeTests : InputTestFixture
{
    private GameObject player;
    private GameObject heldObject;
    private ScriptableObject profile;
    private Keyboard keyboard;
    private GameObject testAimCameraObject;

    [SetUp]
    public override void Setup() => base.Setup();

    [UnityTest]
    public IEnumerator ArrowInputRotatesPreviewAndRotationResetsAfterReentry()
    {
        Type managerType = Resolve("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime");
        object manager = managerType.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (manager != null && (bool)managerType.GetProperty("IsListening").GetValue(manager))
            Assert.Ignore("This isolated preview test requires an offline player.");

        Type inputType = Resolve("PlayerInputNew, Assembly-CSharp");
        Type interactionType = Resolve("PlayerInteractionNew, Assembly-CSharp");
        Type controllerType = Resolve("PlayerSingleCarryPlacementController, Assembly-CSharp");
        Type resourceType = Resolve("BaseResourceNew, Assembly-CSharp");
        Type profileType = Resolve("BaseResourceSO, Assembly-CSharp");
        Type pickableType = Resolve("IPIckableNew, Assembly-CSharp");

        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
#else
        Assert.Ignore("This fixture loads the established player prefab through the Unity Editor AssetDatabase.");
        yield break;
#endif
        Assert.That(prefab, Is.Not.Null);
        player = UnityEngine.Object.Instantiate(prefab);
        player.name = "Stage 2 placement rotation test player";
        player.transform.position = new Vector3(0f, 100f, 0f);
        Component input = player.GetComponent(inputType);
        Component interaction = player.GetComponent(interactionType);
        Assert.That(input, Is.Not.Null);
        Assert.That(interaction, Is.Not.Null);

        profile = ScriptableObject.CreateInstance(profileType);
        profileType.GetField("canBeCarried").SetValue(profile, true);
        profileType.GetField("allowMultipleCarriers").SetValue(profile, false);
        heldObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Component resource = heldObject.AddComponent(resourceType);
        resourceType.GetField("baseResourceSO", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(resource, profile);
        interactionType.GetMethod("PickUpObject", BindingFlags.Instance | BindingFlags.Public, null,
            new[] { typeof(GameObject), pickableType }, null).Invoke(interaction, new object[] { heldObject, resource });

        AddSyntheticKeyboard();
        yield return null;
        yield return null;
        Component controller = player.GetComponent(controllerType);
        Assert.That(controller, Is.Not.Null);
        testAimCameraObject = new GameObject("Stage 2 synthetic aim camera");
        Camera testAimCamera = testAimCameraObject.AddComponent<Camera>();
        testAimCameraObject.transform.position = player.transform.position;
        interactionType.GetMethod("SetAimCamera").Invoke(interaction, new object[] { testAimCamera });
        QueueKeys("F");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);

        FieldInfo ghostField = controllerType.GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic);
        Type ghostType = Resolve("SingleCarryPlacementGhost, Assembly-CSharp");
        GameObject ghostRoot = (GameObject)ghostType.GetProperty("Root").GetValue(ghostField.GetValue(controller));
        MethodInfo aimRayMethod = interactionType.GetMethod("TryGetAimRay", new[] { Resolve("UnityEngine.Ray, UnityEngine.CoreModule").MakeByRefType(), Resolve("UnityEngine.Camera, UnityEngine.CoreModule").MakeByRefType(), typeof(Quaternion).MakeByRefType() });
        object[] rayArgs = { default(Ray), null, default(Quaternion) };
        Assert.That((bool)aimRayMethod.Invoke(interaction, rayArgs), Is.True);
        Quaternion cameraYaw = (Quaternion)rayArgs[2];

        QueueKeys("F", "UpArrow");
        float elapsed = 0f;
        float deadline = Time.realtimeSinceStartup + 8f;
        while (elapsed < 4.5f && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }
        Assert.That(elapsed, Is.GreaterThanOrEqualTo(4.4f), "The bounded game-time rotation interval should complete.");
        Quaternion localXRotation = Quaternion.Inverse(cameraYaw) * ghostRoot.transform.rotation;
        Assert.That(Quaternion.Angle(Quaternion.AngleAxis(405f, Vector3.right), localXRotation), Is.LessThan(10f),
            "Holding Up must rotate about local X at 90 degrees per second and continue through a full turn without clamping.");

        QueueKeys("Escape");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        QueueKeys();
        yield return null;
        QueueKeys("F");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
        ghostRoot = (GameObject)ghostType.GetProperty("Root").GetValue(ghostField.GetValue(controller));

        testAimCameraObject.transform.rotation = Quaternion.Euler(-60f, 45f, 0f);
        yield return null;
        rayArgs = new object[] { default(Ray), null, default(Quaternion) };
        Assert.That((bool)aimRayMethod.Invoke(interaction, rayArgs), Is.True);
        cameraYaw = (Quaternion)rayArgs[2];
        Assert.That(Quaternion.Angle(cameraYaw, ghostRoot.transform.rotation), Is.LessThan(0.1f), "Each entry resets local rotation to camera yaw.");
        Assert.That(Mathf.Abs(ghostRoot.transform.rotation.eulerAngles.x), Is.LessThan(0.1f), "Camera pitch must not tilt the base preview rotation.");
        Ray currentAimRay = (Ray)rayArgs[0];
        Assert.That(Vector3.Distance(ghostRoot.transform.position, currentAimRay.GetPoint(2f)), Is.LessThan(0.02f), "Changing camera pitch updates the aim ray and preview position.");

        QueueKeys("UpArrow", "LeftArrow");
        elapsed = 0f;
        deadline = Time.realtimeSinceStartup + 2f;
        while (elapsed < 0.25f && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }
        Assert.That(elapsed, Is.GreaterThanOrEqualTo(0.2f), "The bounded game-time diagonal input interval should complete.");
        Quaternion expectedDiagonal = cameraYaw * Quaternion.AngleAxis(22.5f, Vector3.right) * Quaternion.AngleAxis(22.5f, Vector3.forward);
        Assert.That(Quaternion.Angle(expectedDiagonal, ghostRoot.transform.rotation), Is.LessThan(8f),
            "Simultaneous Up and Left must rotate each local axis at 90 degrees per second without digital normalization.");
    }

    [TearDown]
    public override void TearDown()
    {
        if (player != null) UnityEngine.Object.DestroyImmediate(player);
        if (heldObject != null) UnityEngine.Object.DestroyImmediate(heldObject);
        if (testAimCameraObject != null) UnityEngine.Object.DestroyImmediate(testAimCameraObject);
        if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        base.TearDown();
    }

    private void AddSyntheticKeyboard()
    {
        keyboard = InputSystem.AddDevice<Keyboard>();
    }

    private void QueueKeys(params string[] pressedKeys)
    {
        Key[] keys = pressedKeys.Select(name => (Key)Enum.Parse(typeof(Key), name)).ToArray();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }

    private static Type Resolve(string name) => Type.GetType(name);
}
