using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class SingleCarryPlacementMovementPlayModeTests : InputTestFixture
{
    private GameObject player;
    private GameObject heldObject;
    private GameObject floor;
    private GameObject obstruction;
    private ScriptableObject resourceProfile;
    private Keyboard keyboard;

    [UnityTest]
    public IEnumerator PreviewAllowsCardinalAndDiagonalCharacterControllerMovement()
    {
        Type managerType = Resolve("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime");
        object manager = managerType.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (manager != null && (bool)managerType.GetProperty("IsListening").GetValue(manager))
            Assert.Ignore("This isolated local-input test requires an offline player.");

        Type inputType = Resolve("PlayerInputNew, Assembly-CSharp");
        Type interactionType = Resolve("PlayerInteractionNew, Assembly-CSharp");
        Type resourceType = Resolve("BaseResourceNew, Assembly-CSharp");
        Type profileType = Resolve("BaseResourceSO, Assembly-CSharp");
        Type pickableType = Resolve("IPIckableNew, Assembly-CSharp");
        Type firstPersonType = Resolve("StarterAssets.FirstPersonController, Assembly-CSharp");
        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
#else
        Assert.Ignore("This fixture loads the established player prefab through the Unity Editor AssetDatabase.");
        yield break;
#endif
        Assert.That(prefab, Is.Not.Null);
        player = UnityEngine.Object.Instantiate(prefab);
        player.name = "Single carry movement test player";
        player.transform.position = new Vector3(0f, 100f, 0f);
        player.transform.rotation = Quaternion.identity;
        floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Movement test floor";
        floor.transform.position = new Vector3(0f, 99.15f, 0f);
        floor.transform.localScale = new Vector3(100f, 1f, 100f);
        Component input = player.GetComponent(inputType);
        Component interaction = player.GetComponent(interactionType);
        Component firstPerson = player.GetComponent(firstPersonType);
        Assert.That(input, Is.Not.Null);
        Assert.That(interaction, Is.Not.Null);
        Assert.That(firstPerson, Is.Not.Null);
        Transform aimAnchor = (Transform)interactionType.GetProperty("SingleCarryPlacementAimAnchor").GetValue(interaction);
        Assert.That(aimAnchor, Is.Not.Null);
        GameObject cameraObject = new GameObject("Single carry movement test aim camera");
        cameraObject.transform.SetParent(aimAnchor, false);
        cameraObject.transform.localPosition = Vector3.zero;
        cameraObject.transform.localRotation = Quaternion.identity;
        Camera aimCamera = cameraObject.AddComponent<Camera>();
        aimCamera.nearClipPlane = 0.2f;
        interactionType.GetMethod("SetAimCamera").Invoke(interaction, new object[] { aimCamera });

        yield return null;
        yield return null;
        resourceProfile = ScriptableObject.CreateInstance(profileType);
        profileType.GetField("canBeCarried").SetValue(resourceProfile, true);
        profileType.GetField("allowMultipleCarriers").SetValue(resourceProfile, false);
        profileType.GetField("minAmountOfPlayersNeeded").SetValue(resourceProfile, 1);
        profileType.GetField("recommendedCarriers").SetValue(resourceProfile, 1);
        profileType.GetField("movementSpeedPenalty").SetValue(resourceProfile, 0.25f);
        heldObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Component resource = heldObject.AddComponent(resourceType);
        resourceType.GetField("baseResourceSO", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(resource, resourceProfile);
        interactionType.GetMethod("PickUpObject", BindingFlags.Instance | BindingFlags.Public, null,
            new[] { typeof(GameObject), pickableType }, null).Invoke(interaction, new object[] { heldObject, resource });

        keyboard = InputSystem.AddDevice<Keyboard>();
        yield return null;
        yield return null;
        Assert.That((float)firstPersonType.GetField("_holdedItemMovementSpeedPenaltyMultiplier", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(firstPerson), Is.EqualTo(0.25f).Within(0.001f), "The fixture must exercise the configured nonzero carried-item speed penalty.");
        Vector3 ordinaryCarryForward = Vector3.zero, forward = Vector3.zero, backward = Vector3.zero;
        Vector3 right = Vector3.zero, left = Vector3.zero, diagonal = Vector3.zero;
        // Measure ordinary carry first, then enter F while already walking and compare at steady speed.
        yield return MeasureDirection("W", null, value => ordinaryCarryForward = value, placement: false);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return WaitSeconds(0.5f);
        Vector3 beforePlacementEntry = player.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.W));
        yield return null;
        Vector3 entryStep = player.transform.position - beforePlacementEntry;
        entryStep.y = 0f;
        Assert.That(entryStep.magnitude, Is.GreaterThan(0.001f), "Entering F while walking must not insert a stopped frame.");
        yield return WaitSeconds(0.5f);
        yield return MeasureDirection("W", null, value => forward = value, placement: true);
        Assert.That(forward.magnitude, Is.EqualTo(ordinaryCarryForward.magnitude).Within(Mathf.Max(0.08f, ordinaryCarryForward.magnitude * 0.2f)),
            "F preview retains the ordinary held-item speed penalty.");
        yield return MeasureDirection("S", null, value => backward = value);
        yield return MeasureDirection("D", null, value => right = value);
        yield return MeasureDirection("A", null, value => left = value);
        yield return MeasureDirection("W", "D", value => diagonal = value);
        Assert.That(Vector3.Dot(forward, Vector3.forward), Is.GreaterThan(0.1f));
        Assert.That(Vector3.Dot(backward, Vector3.back), Is.GreaterThan(0.05f));
        Assert.That(Vector3.Dot(right, Vector3.right), Is.GreaterThan(0.1f));
        Assert.That(Vector3.Dot(left, Vector3.left), Is.GreaterThan(0.1f));
        Assert.That(Vector3.Dot(diagonal.normalized, (Vector3.forward + Vector3.right).normalized), Is.GreaterThan(0.9f));
        Assert.That(diagonal.magnitude, Is.EqualTo(forward.magnitude).Within(Mathf.Max(0.08f, forward.magnitude * 0.2f)),
            "Diagonal movement remains normalized to the same maximum speed as cardinal movement.");

        Vector3 beforeBlockedJump = player.transform.position;
        Assert.That((bool)firstPersonType.GetField("Grounded").GetValue(firstPerson), Is.True, "Sprint/jump checks require the grounded fixture.");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.LeftShift, Key.Space));
        yield return WaitSeconds(0.2f);
        Assert.That(player.transform.position.y, Is.LessThanOrEqualTo(beforeBlockedJump.y + 0.03f), "Jump remains blocked while preview is active.");
        Assert.That((bool)firstPersonType.GetField("_isSprinting", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(firstPerson), Is.False,
            "Sprint remains blocked while preview is active.");

        Component placementController = player.GetComponent(Resolve("PlayerSingleCarryPlacementController, Assembly-CSharp"));
        FieldInfo ghostField = placementController.GetType().GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic);
        object ghost = ghostField.GetValue(placementController);
        GameObject ghostRoot = (GameObject)Resolve("SingleCarryPlacementGhost, Assembly-CSharp")
            .GetProperty("Root").GetValue(ghost);
        Assert.That(ghostRoot, Is.Not.Null);
        Assert.That(ghostRoot.transform.position, Is.Not.EqualTo(heldObject.transform.position), "Preview ghost follows the aim pose independently of the carried source.");
        Vector3 initialGhostPosition = ghostRoot.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.W));
        yield return WaitSeconds(0.2f);
        Assert.That(Vector3.Distance(ghostRoot.transform.position, initialGhostPosition), Is.GreaterThan(0.02f),
            "The preview ghost follows the changing player aim pose while moving.");
        obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstruction.transform.position = ghostRoot.transform.position + Vector3.right * 0.65f;
        obstruction.transform.rotation = ghostRoot.transform.rotation;
        obstruction.transform.localScale = Vector3.one * 0.5f;
        Physics.SyncTransforms();
        yield return null;
        object validation = placementController.GetType().GetProperty("LastValidation").GetValue(placementController);
        Assert.That(validation.GetType().GetProperty("Status").GetValue(validation).ToString(), Is.EqualTo("Occupied"),
            "An occupied candidate is reflected in local preview validity.");

        // The obstruction guarantees E is a locally invalid confirmation.
        Vector3 beforeInvalidConfirm = player.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.W, Key.E));
        yield return WaitSeconds(0.2f);
        Vector3 invalidConfirmDelta = player.transform.position - beforeInvalidConfirm;
        Assert.That(invalidConfirmDelta.z, Is.GreaterThan(0.05f), "Invalid local E leaves preview movement running.");
        Assert.That((bool)placementController.GetType().GetProperty("IsActive").GetValue(placementController), Is.True,
            "Invalid local E keeps the placement preview active.");

        // The CharacterController still resolves ordinary world collision during F.
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
        yield return WaitSeconds(0.5f);
        obstruction.transform.position = new Vector3(0f, player.transform.position.y + 1f, 1f);
        obstruction.transform.localScale = new Vector3(4f, 4f, 0.5f);
        player.transform.position = new Vector3(0f, player.transform.position.y, 0f);
        Physics.SyncTransforms();
        Vector3 wallStart = player.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.W));
        yield return WaitSeconds(0.5f);
        Assert.That(player.transform.position.z, Is.GreaterThan(wallStart.z + 0.1f));
        Assert.That(player.transform.position.z, Is.LessThan(0.9f), "F-mode movement remains blocked by a world wall.");
        UnityEngine.Object.DestroyImmediate(obstruction);
    }

    private IEnumerator MeasureDirection(string first, string second, Action<Vector3> onMeasured, bool placement = true)
    {
        yield return WaitSeconds(0.5f);
        Key key = (Key)Enum.Parse(typeof(Key), first);
        Key secondKey = second != null ? (Key)Enum.Parse(typeof(Key), second) : Key.None;
        KeyboardState movementState = placement
            ? (second == null ? new KeyboardState(Key.F, key) : new KeyboardState(Key.F, key, secondKey))
            : (second == null ? new KeyboardState(key) : new KeyboardState(key, secondKey));
        InputSystem.QueueStateEvent(keyboard, movementState);
        yield return WaitSeconds(0.5f);
        Vector3 start = player.transform.position;
        yield return WaitSeconds(0.3f);
        Vector3 delta = player.transform.position - start;
        delta.y = 0f;
        InputSystem.QueueStateEvent(keyboard, placement ? new KeyboardState(Key.F) : new KeyboardState());
        yield return null;
        onMeasured(delta);
    }

    private static IEnumerator WaitSeconds(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds + 2f;
        float elapsed = 0f;
        while (elapsed < seconds && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }
        Assert.That(elapsed, Is.GreaterThanOrEqualTo(seconds * 0.9f), "Bounded movement measurement timed out.");
    }

    [TearDown]
    public override void TearDown()
    {
        if (player != null) UnityEngine.Object.DestroyImmediate(player);
        if (heldObject != null) UnityEngine.Object.DestroyImmediate(heldObject);
        if (obstruction != null) UnityEngine.Object.DestroyImmediate(obstruction);
        if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
        if (resourceProfile != null) UnityEngine.Object.DestroyImmediate(resourceProfile);
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        base.TearDown();
    }

    private static Type Resolve(string name) => Type.GetType(name);
}
