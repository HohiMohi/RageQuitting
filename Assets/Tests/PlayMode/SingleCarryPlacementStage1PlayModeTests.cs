using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class SingleCarryPlacementStage1PlayModeTests : InputTestFixture
{
    private GameObject player;
    private GameObject heldObject;
    private ScriptableObject resourceProfile;
    private Keyboard syntheticKeyboard;
    private Mouse syntheticMouse;

    [SetUp]
    public override void Setup() => base.Setup();

    [UnityTest]
    public IEnumerator FStartsPlacement_EIsConsumed_EscapeCancels_GravityAndImpulseContinue()
    {
        Type managerType = Resolve("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime");
        object manager = managerType.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (manager != null && (bool)managerType.GetProperty("IsListening").GetValue(manager))
            Assert.Ignore("This isolated local-input test requires an offline player.");

        Type inputType = Resolve("PlayerInputNew, Assembly-CSharp");
        Type interactionType = Resolve("PlayerInteractionNew, Assembly-CSharp");
        Type controllerType = Resolve("PlayerSingleCarryPlacementController, Assembly-CSharp");
        Type resourceType = Resolve("BaseResourceNew, Assembly-CSharp");
        Type profileType = Resolve("BaseResourceSO, Assembly-CSharp");
        Type impulseType = Resolve("PlayerExternalImpulseController, Assembly-CSharp");
        Type impulseDataType = Resolve("ExternalImpulseData, Assembly-CSharp");
        Type firstPersonType = Resolve("StarterAssets.FirstPersonController, Assembly-CSharp");
        Type pickableType = Resolve("IPIckableNew, Assembly-CSharp");

        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
#else
        Assert.Ignore("This fixture loads the established player prefab through the Unity Editor AssetDatabase.");
        yield break;
#endif
        Assert.That(prefab, Is.Not.Null, "The established player prefab is the fixture for movement/input dependencies.");
        player = UnityEngine.Object.Instantiate(prefab);
        player.name = "Stage 1 placement test player";
        player.transform.position = new Vector3(0f, 100f, 0f);
        Component input = player.GetComponent(inputType);
        Component interaction = player.GetComponent(interactionType);
        Component firstPerson = player.GetComponent(firstPersonType);
        Component controller;
        Assert.That(input, Is.Not.Null);
        Assert.That(interaction, Is.Not.Null);
        Assert.That(firstPerson, Is.Not.Null);


        resourceProfile = ScriptableObject.CreateInstance(profileType);
        profileType.GetField("canBeCarried").SetValue(resourceProfile, true);
        profileType.GetField("allowMultipleCarriers").SetValue(resourceProfile, false);
        heldObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Component resource = heldObject.AddComponent(resourceType);
        resourceType.GetField("baseResourceSO", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(resource, resourceProfile);
        interactionType.GetMethod("PickUpObject", BindingFlags.Instance | BindingFlags.Public, null,
            new[] { typeof(GameObject), pickableType }, null).Invoke(interaction, new object[] { heldObject, resource });

        int interactCount = 0;
        int blockedActionCount = 0;
        EventHandler onBlockedAction = (_, _) => blockedActionCount++;
        string[] blockedFieldNames = { "OnJump", "OnAction", "OnActionAlt", "OnSwapItems", "OnDropItem" };
        FieldInfo[] blockedFields = blockedFieldNames.Select(name => inputType.GetField(name, BindingFlags.Instance | BindingFlags.Public)).ToArray();
        foreach (FieldInfo field in blockedFields) field.SetValue(input, Delegate.Combine((Delegate)field.GetValue(input), onBlockedAction));
        EventHandler onInteract = (_, _) => interactCount++;
        FieldInfo interactField = inputType.GetField("OnInteract", BindingFlags.Instance | BindingFlags.Public);
        interactField.SetValue(input, Delegate.Combine((Delegate)interactField.GetValue(input), onInteract));
        try
        {
            AddSyntheticKeyboard();
            AddSyntheticMouse();
            yield return null;
            yield return null;
            controller = player.GetComponent(controllerType);
            Assert.That(controller, Is.Not.Null);
            Vector3 initialPosition = player.transform.position;

            FieldInfo horizontalVelocity = firstPersonType.GetField("_horizontalVelocity", BindingFlags.Instance | BindingFlags.NonPublic);
            horizontalVelocity.SetValue(firstPerson, new Vector3(2f, 0f, 2f));
            QueueKeyboardState("F", "W");
            yield return null;
            yield return null;

            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
            Assert.That((bool)inputType.GetProperty("IsSingleCarryPlacementActive").GetValue(input), Is.True);
            Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(heldObject));
            Assert.That(horizontalVelocity.GetValue(firstPerson), Is.EqualTo(Vector3.zero));

            object impulse = Activator.CreateInstance(impulseDataType);
            impulseDataType.GetField("InitialVelocity").SetValue(impulse, new Vector3(4f, 0f, 0f));
            impulseDataType.GetField("MaximumDuration").SetValue(impulse, 0.5f);
            impulseDataType.GetField("HorizontalDeceleration").SetValue(impulse, 0f);
            impulseDataType.GetField("MaximumHorizontalSpeed").SetValue(impulse, 5f);
            impulseType.GetMethod("TryApplyExternalImpulse").Invoke(player.GetComponent(impulseType), new[] { impulse, null });

            QueueKeyboardState("F", "W", "E", "Space", "LeftShift", "Z", "X");
            yield return null;
            Assert.That(interactCount, Is.Zero, "E must not invoke the normal interaction while placement is active.");
            Assert.That(blockedActionCount, Is.Zero, "Jump and primary, secondary, swap, and drop actions must be blocked.");
            QueueMouseButtons(true, true);
            yield return null;
            Assert.That(blockedActionCount, Is.Zero, "Mouse primary and secondary actions must be blocked.");
            QueueMouseButtons(false, false);
            QueueMouseDelta(new Vector2(3f, 2f));
            yield return null;
            Assert.That(((Vector2)inputType.GetMethod("GetLookDeltaValue").Invoke(input, null)).sqrMagnitude, Is.GreaterThan(0f), "Look input remains available in placement mode.");
            Assert.That((bool)firstPersonType.GetField("_isSprinting", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(firstPerson), Is.False, "Sprint must remain blocked.");

            QueueKeyboardState("W");
            float elapsed = 0f;
            float realtimeDeadline = Time.realtimeSinceStartup + 2f;
            while (elapsed < 0.25f && Time.realtimeSinceStartup < realtimeDeadline)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            Assert.That(elapsed, Is.GreaterThanOrEqualTo(0.2f));
            Assert.That(player.transform.position.z, Is.EqualTo(initialPosition.z).Within(0.03f), "Movement input should remain blocked.");
            Assert.That(player.transform.position.x, Is.GreaterThan(initialPosition.x + 0.05f), "The external impulse should continue moving the player.");
            Assert.That(player.transform.position.y, Is.LessThan(initialPosition.y - 0.05f), "Gravity should continue in placement mode.");
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);

            FieldInfo ghostField = controllerType.GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic);
            object ownedGhost = ghostField.GetValue(controller);
            GameObject ghostRoot = (GameObject)Resolve("SingleCarryPlacementGhost, Assembly-CSharp").GetField("root", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ownedGhost);
            QueueKeyboardState("Escape");
            yield return null;
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False);
            Assert.That(ghostField.GetValue(controller), Is.Null);
            yield return null;
            Assert.That(ghostRoot == null, Is.True, "Cancel destroys the owned ghost GameObject.");
            Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(heldObject));
            QueueKeyboardState("W");
            yield return null;
            Vector2 restoredMove = (Vector2)inputType.GetMethod("GetMoveVectorValue").Invoke(input, null);
            Assert.That(restoredMove.sqrMagnitude, Is.GreaterThan(0f), "Movement input should be restored after cancel.");

            QueueKeyboardState("F");
            yield return null;
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
            QueueKeyboardState("W");
            yield return null;
            ((Behaviour)input).enabled = false;
            QueueKeyboardState("F");
            yield return null;
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False, "Disabled input cancels and cannot react to F.");
            ((Behaviour)input).enabled = true;
            yield return null;
            yield return null;
            QueueKeyboardState("W");
            yield return null;
            QueueKeyboardState("F", "W");
            yield return null;
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True, "Re-enabled local input can enter again.");
            Assert.That(player.GetComponents(controllerType).Length, Is.EqualTo(1), "Input re-enable does not duplicate runtime composition.");

            inputType.GetMethod("SetGameplayUiOpen").Invoke(input, new object[] { true });
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False, "Opening a menu cancels placement first.");
            inputType.GetMethod("SetGameplayUiOpen").Invoke(input, new object[] { false });

            QueueKeyboardState("W");
            yield return null;
            QueueKeyboardState("W");
            yield return null;
            QueueKeyboardState("F", "W");
            yield return null;
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
            interactionType.GetMethod("ForceReleasePickedUpObject").Invoke(interaction, new object[] { heldObject });
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False, "Held-object loss cancels placement.");

            interactionType.GetMethod("PickUpObject", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(GameObject), pickableType }, null).Invoke(interaction, new object[] { heldObject, resource });
            QueueKeyboardState("W");
            yield return null;
            QueueKeyboardState("F", "W");
            yield return null;
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
            FieldInfo ghostFieldForDisable = controllerType.GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic);
            object ownedGhostForDisable = ghostFieldForDisable.GetValue(controller);
            GameObject ghostRootForDisable = (GameObject)Resolve("SingleCarryPlacementGhost, Assembly-CSharp").GetField("root", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ownedGhostForDisable);
            ((Behaviour)controller).enabled = false;
            yield return null;
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False, "Disabling the controller cancels placement.");
            Assert.That(ghostFieldForDisable.GetValue(controller), Is.Null);
            yield return null;
            Assert.That(ghostRootForDisable == null, Is.True, "Disabling the controller destroys the owned ghost GameObject.");
            ((Behaviour)controller).enabled = true;
            yield return null;
            QueueKeyboardState("W");
            yield return null;
            QueueKeyboardState("F", "W");
            yield return null;
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True, "Re-enabled controller can enter placement again.");
            Component health = player.GetComponent(Resolve("PlayerHealth, Assembly-CSharp"));
            health.GetType().GetMethod("DamageReceived", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(float), Resolve("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime") }, null)
                .Invoke(health, new object[] { 999f, null });
            Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False, "Downing the player cancels placement.");
        }
        finally
        {
            interactField.SetValue(input, Delegate.Remove((Delegate)interactField.GetValue(input), onInteract));
            foreach (FieldInfo field in blockedFields) field.SetValue(input, Delegate.Remove((Delegate)field.GetValue(input), onBlockedAction));
        }
    }

    [TearDown]
    public override void TearDown()
    {
        if (player != null) UnityEngine.Object.DestroyImmediate(player);
        if (heldObject != null) UnityEngine.Object.DestroyImmediate(heldObject);
        if (resourceProfile != null) UnityEngine.Object.DestroyImmediate(resourceProfile);
        if (syntheticMouse != null) InputSystem.RemoveDevice(syntheticMouse);
        if (syntheticKeyboard != null) InputSystem.RemoveDevice(syntheticKeyboard);
        base.TearDown();
    }

    private static Type Resolve(string name) => Type.GetType(name);

    private void AddSyntheticKeyboard()
    {
        syntheticKeyboard = InputSystem.AddDevice<Keyboard>();
    }

    private void AddSyntheticMouse()
    {
        syntheticMouse = InputSystem.AddDevice<Mouse>();
    }

    private void QueueMouseButtons(bool left, bool right)
    {
        MouseState state = default;
        state = state.WithButton(MouseButton.Left, left).WithButton(MouseButton.Right, right);
        InputSystem.QueueStateEvent(syntheticMouse, state);
    }

    private void QueueMouseDelta(Vector2 delta)
    {
        InputSystem.QueueDeltaStateEvent(syntheticMouse.delta, delta);
    }
    private void QueueKeyboardState(params string[] pressedKeys)
    {
        Key[] keys = pressedKeys.Select(name => (Key)Enum.Parse(typeof(Key), name)).ToArray();
        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState(keys));
    }
}
