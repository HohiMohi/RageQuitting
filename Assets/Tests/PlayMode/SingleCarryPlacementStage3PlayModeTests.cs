using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class SingleCarryPlacementStage3PlayModeTests : InputTestFixture
{
    private GameObject player;
    private GameObject heldObject;
    private GameObject childVisual;
    private GameObject inactiveVisual;
    private GameObject preHiddenVisual;
    private GameObject normalTarget;
    private GameObject aimCameraObject;
    private GameObject obstacle;
    private ScriptableObject profile;
    private Keyboard keyboard;

    [SetUp]
    public override void Setup() => base.Setup();

    [UnityTest]
    public IEnumerator PlacementStatusTracksWorldOccupancy_HudStaysVisibleAndHeldVisualRestoresExactly()
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
        player.name = "Stage 3 placement validation player";
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
        Renderer mainRenderer = heldObject.GetComponent<Renderer>();
        childVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        childVisual.name = "inactive bucket-like contents renderer";
        childVisual.transform.SetParent(heldObject.transform, false);
        childVisual.SetActive(false);
        Renderer childRenderer = childVisual.GetComponent<Renderer>();
        inactiveVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        inactiveVisual.name = "disabled child contents renderer";
        inactiveVisual.transform.SetParent(heldObject.transform, false);
        Renderer inactiveRenderer = inactiveVisual.GetComponent<Renderer>();
        inactiveRenderer.enabled = false;
        preHiddenVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        preHiddenVisual.name = "pre-hidden bucket detail";
        preHiddenVisual.transform.SetParent(heldObject.transform, false);
        Renderer preHiddenRenderer = preHiddenVisual.GetComponent<Renderer>();
        preHiddenRenderer.forceRenderingOff = true;
        preHiddenRenderer.enabled = false;
        bool priorMainForceOff = false;
        mainRenderer.forceRenderingOff = priorMainForceOff;
        childRenderer.forceRenderingOff = false;

        keyboard = InputSystem.AddDevice<Keyboard>();
        yield return null;
        yield return null;
        Component controller = player.GetComponent(controllerType);
        Assert.That(controller, Is.Not.Null);
        aimCameraObject = new GameObject("Stage 3 aim camera");
        Camera aimCamera = aimCameraObject.AddComponent<Camera>();
        aimCameraObject.transform.position = player.transform.position;
        interactionType.GetMethod("SetAimCamera").Invoke(interaction, new object[] { aimCamera });

        QueueKeys("F");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
        Assert.That(mainRenderer.forceRenderingOff, Is.True, "The held item is hidden only through the renderer force flag.");
        Assert.That(childRenderer.forceRenderingOff, Is.True, "Inactive child renderers are cached and forced off too.");
        Assert.That(inactiveRenderer.forceRenderingOff, Is.True, "Disabled renderers are suppressed without toggling enabled.");
        Assert.That(preHiddenRenderer.forceRenderingOff, Is.True);
        Assert.That(mainRenderer.enabled, Is.True);
        Assert.That(inactiveRenderer.enabled, Is.False);
        QueueKeys();
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True,
            "Releasing F after entry must leave placement mode active.");
        GameObject ghostRoot = (GameObject)Resolve("SingleCarryPlacementGhost, Assembly-CSharp")
            .GetProperty("Root").GetValue(controllerType.GetField("ghost", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller));
        Assert.That(ghostRoot.activeSelf, Is.True);
        Renderer previewRenderer = ghostRoot.GetComponentInChildren<Renderer>();
        Assert.That(previewRenderer, Is.Not.Null);
        Assert.That(previewRenderer.enabled, Is.True);
        int validationColorId = Shader.PropertyToID("_ValidationColor");
        Color initialValidationColor = previewRenderer.sharedMaterial.GetColor(validationColorId);
        Assert.That(initialValidationColor.g, Is.GreaterThan(initialValidationColor.r), "An empty aim point should tint the preview green.");
        childVisual.SetActive(true);
        Assert.That(childRenderer.forceRenderingOff, Is.True, "A cached renderer stays suppressed if it activates during placement.");

        Component ui = player.GetComponentInChildren(Resolve("LookingAtComponentUI, Assembly-CSharp"), true);
        Assert.That(ui, Is.Not.Null, "The established player prefab should carry the interaction HUD.");
        yield return null;
        Type uiType = ui.GetType();
        Assert.That(uiType.GetProperty("EvaluatedTarget").GetValue(ui), Is.Null);
        Assert.That((bool)uiType.GetProperty("CurrentTargetHasActionablePrompt").GetValue(ui), Is.False);
        GameObject visualRoot = (GameObject)uiType.GetField("visualRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
        Component text = (Component)uiType.GetField("componentInfoText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
        Assert.That(visualRoot.activeSelf, Is.True, "Status HUD remains visible while aiming into empty air.");
        Assert.That(text.GetType().GetProperty("text").GetValue(text).ToString(), Does.Contain("Placement valid"));
        Assert.That(text.GetType().GetProperty("text").GetValue(text).ToString(), Does.Contain("Up/Down X | Left/Right Z"));

        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = "off-axis placement blocker";
        obstacle.transform.position = ghostRoot.transform.position + new Vector3(0.44f, 0f, 0f);
        obstacle.transform.localScale = Vector3.one * 0.2f;
        Physics.SyncTransforms();
        yield return null;
        Assert.That(controllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.EqualTo(false));
        Assert.That(controllerType.GetProperty("PlacementStatusLabel").GetValue(controller).ToString(), Is.EqualTo("Placement blocked"));
        string statusText = text.GetType().GetProperty("text").GetValue(text).ToString();
        Assert.That(statusText, Does.Contain("Placement blocked"));
        Assert.That(statusText, Does.Contain("E Place"),
            "Offline placement mode now exposes its Stage 4 confirmation binding while occupied status still blocks the action.");
        Color blockedValidationColor = previewRenderer.sharedMaterial.GetColor(validationColorId);
        Assert.That(blockedValidationColor.r, Is.GreaterThan(blockedValidationColor.g), "An occupied preview should tint red.");
        QueueKeys("E");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True,
            "E is consumed while placement mode remains active.");
        Assert.That(interactionType.GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(heldObject),
            "E is consumed in Stage 3 and must not drop the held source.");

        UnityEngine.Object.Destroy(obstacle);
        obstacle = null;
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        Assert.That(controllerType.GetProperty("IsCurrentPlacementValid").GetValue(controller), Is.EqualTo(true),
            $"Expected a refreshed clear preview; last status={controllerType.GetProperty("PlacementStatusLabel").GetValue(controller)}, " +
            $"blocking collider={controllerType.GetProperty("LastValidation").GetValue(controller).GetType().GetProperty("BlockingCollider").GetValue(controllerType.GetProperty("LastValidation").GetValue(controller))}");

        QueueKeys();
        yield return null;
        UnityEngine.Object.Destroy(ghostRoot);
        yield return null;
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        Assert.That((bool)inputType.GetProperty("IsSingleCarryPlacementActive").GetValue(input), Is.False,
            "Losing the owned preview cancels mode and restores gameplay input.");
        Assert.That(mainRenderer.forceRenderingOff, Is.EqualTo(priorMainForceOff), "Pre-hidden renderer state is restored exactly.");
        Assert.That(childRenderer.forceRenderingOff, Is.False);
        Assert.That(inactiveRenderer.forceRenderingOff, Is.False);
        Assert.That(preHiddenRenderer.forceRenderingOff, Is.True, "Previously pre-hidden renderer state is restored exactly.");
        Assert.That(mainRenderer.enabled, Is.True);
        Assert.That(inactiveRenderer.enabled, Is.False, "The component's authored enabled state is never overwritten.");
        Assert.That(visualRoot.activeSelf, Is.False, "Placement status HUD exits with placement mode.");

        normalTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
        normalTarget.name = "ordinary interaction target after placement";
        normalTarget.transform.position = aimCameraObject.transform.position + aimCameraObject.transform.forward * 1.4f;
        Component targetResource = normalTarget.AddComponent(resourceType);
        resourceType.GetField("baseResourceSO", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(targetResource, profile);
        Physics.SyncTransforms();
        yield return null;
        Assert.That(visualRoot.activeSelf, Is.True, "Normal target prompts resume after placement ends.");
        Assert.That((bool)uiType.GetProperty("CurrentTargetHasActionablePrompt").GetValue(ui), Is.True);
        Assert.That(text.GetType().GetProperty("text").GetValue(text).ToString(), Does.Contain("Drop"));
        UnityEngine.Object.Destroy(normalTarget);
        normalTarget = null;

        QueueKeys();
        yield return null;
        QueueKeys("F");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
        QueueKeys();
        yield return null;
        QueueKeys("Escape");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False,
            "Escape independently cancels placement and restores the held renderer state.");
        Assert.That(mainRenderer.forceRenderingOff, Is.EqualTo(priorMainForceOff));

        QueueKeys();
        yield return null;
        QueueKeys("F");
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.True);
        Assert.That(childRenderer.forceRenderingOff, Is.True);
        ((Behaviour)controller).enabled = false;
        Assert.That(mainRenderer.forceRenderingOff, Is.EqualTo(priorMainForceOff), "Disabling the placement controller restores pre-hidden state synchronously.");
        Assert.That(childRenderer.forceRenderingOff, Is.False);
        ((Behaviour)controller).enabled = true;
        yield return null;
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False);

        QueueKeys();
        yield return null;
        QueueKeys("F");
        yield return null;
        Assert.That(mainRenderer.forceRenderingOff, Is.True);
        inputType.GetMethod("SetGameplayUiOpen").Invoke(input, new object[] { true });
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        Assert.That(mainRenderer.forceRenderingOff, Is.EqualTo(priorMainForceOff), "Opening gameplay UI cancels and restores the held visuals first.");
        inputType.GetMethod("SetGameplayUiOpen").Invoke(input, new object[] { false });

        QueueKeys();
        yield return null;
        QueueKeys("F");
        yield return null;
        Assert.That(mainRenderer.forceRenderingOff, Is.True);
        interactionType.GetMethod("ForceReleasePickedUpObject").Invoke(interaction, new object[] { heldObject });
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        Assert.That(mainRenderer.forceRenderingOff, Is.EqualTo(priorMainForceOff), "Forced held-object loss restores visuals without an ordinary drop.");

        interactionType.GetMethod("PickUpObject", BindingFlags.Instance | BindingFlags.Public, null,
            new[] { typeof(GameObject), pickableType }, null).Invoke(interaction, new object[] { heldObject, resource });
        QueueKeys();
        yield return null;
        QueueKeys("F");
        yield return null;
        Assert.That(mainRenderer.forceRenderingOff, Is.True);
        Component health = player.GetComponent(Resolve("PlayerHealth, Assembly-CSharp"));
        health.GetType().GetMethod("DamageReceived", BindingFlags.Instance | BindingFlags.Public, null,
            new[] { typeof(float), Resolve("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime") }, null)
            .Invoke(health, new object[] { 999f, null });
        Assert.That((bool)controllerType.GetProperty("IsActive").GetValue(controller), Is.False);
        Assert.That(mainRenderer.forceRenderingOff, Is.EqualTo(priorMainForceOff), "Downed-state cancellation restores held visuals.");
    }

    [TearDown]
    public override void TearDown()
    {
        if (player != null) UnityEngine.Object.DestroyImmediate(player);
        if (heldObject != null) UnityEngine.Object.DestroyImmediate(heldObject);
        if (aimCameraObject != null) UnityEngine.Object.DestroyImmediate(aimCameraObject);
        if (obstacle != null) UnityEngine.Object.DestroyImmediate(obstacle);
        if (normalTarget != null) UnityEngine.Object.DestroyImmediate(normalTarget);
        if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        base.TearDown();
    }

    private void QueueKeys(params string[] pressedKeys)
    {
        Key[] keys = Array.ConvertAll(pressedKeys, name => (Key)Enum.Parse(typeof(Key), name));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }

    private static Type Resolve(string name) => Type.GetType(name);
}
