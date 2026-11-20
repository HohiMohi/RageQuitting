using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.TestTools;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CrowbarStage3PlayModeTests
{
    private const BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;
    private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
    private GameObject testCamera;

    [SetUp]
    public void SetUpTestCamera()
    {
        testCamera = new GameObject("Crowbar Stage3 test camera");
        testCamera.tag = "MainCamera";
        testCamera.AddComponent<Camera>();
    }

    [TearDown]
    public void TearDownTestCamera()
    {
        if (testCamera != null) UnityEngine.Object.DestroyImmediate(testCamera);
    }

    [Test]
    public void CrowbarIsAppendedWithoutChangingExistingItemTypeValues()
    {
        Type itemType = Type.GetType("EquippableItemType, Assembly-CSharp");
        Assert.That(itemType, Is.Not.Null);

        string[] names = { "Axe", "Saw", "Pickaxe", "Hammer", "Weapon", "IndustrialHammer", "Shovel", "None", "Wrench", "Rope", "SpiritLevel", "Crowbar" };
        for (int value = 0; value < names.Length; value++)
        {
            Assert.That(Enum.GetName(itemType, value), Is.EqualTo(names[value]), $"Enum value {value} must remain compatible.");
        }

        Assert.That(Convert.ToInt32(Enum.Parse(itemType, "Crowbar")), Is.EqualTo(11));
    }

    [Test]
    public void CrowbarVisualHasBentForkedMetalHeadAndSecondaryGripForBothPlayerVisualPaths()
    {
        Type itemType = Type.GetType("EquippableItemType, Assembly-CSharp");
        object crowbarType = Enum.Parse(itemType, "Crowbar");
        Type armsType = Type.GetType("PlayerFirstPersonArms, Assembly-CSharp");
        Type otherPlayerVisualsType = Type.GetType("PlayerEquippableItemVisuals, Assembly-CSharp");
        Assert.That(armsType, Is.Not.Null);
        Assert.That(otherPlayerVisualsType, Is.Not.Null);
        Assert.That((bool)armsType.GetMethod("IsSupportedToolType", StaticNonPublic).Invoke(null, new[] { crowbarType }), Is.True,
            "First-person held visuals must accept the appended item type.");
        Assert.That((bool)otherPlayerVisualsType.GetMethod("IsSupportedToolType", StaticNonPublic).Invoke(null, new[] { crowbarType }), Is.True,
            "Other-player held/back visuals must accept the appended item type.");

        Type builderType = Type.GetType("EquippableToolVisualBuilder, Assembly-CSharp");
        MethodInfo buildVisual = builderType.GetMethod("BuildVisual", StaticPublic);
        ParameterInfo[] parameters = buildVisual.GetParameters();
        GameObject parent = new GameObject("Crowbar visual test parent");
        try
        {
            object emptyMaterials = Activator.CreateInstance(parameters[2].ParameterType);
            GameObject visual = (GameObject)buildVisual.Invoke(null, new[] { crowbarType, parent.transform, emptyMaterials });
            Assert.That(visual, Is.Not.Null);
            Assert.That(FindChild(visual.transform, "BentNeck"), Is.Not.Null);
            Assert.That(FindChild(visual.transform, "HookForkLeft"), Is.Not.Null);
            Assert.That(FindChild(visual.transform, "HookForkRight"), Is.Not.Null);
            Assert.That(FindChild(visual.transform, "StrikingEnd"), Is.Not.Null);
            Assert.That(FindChild(visual.transform, "SecondaryGrip"), Is.Not.Null,
                "The two-handed arm pose locates its off-hand target by this exact name.");
            Assert.That(visual.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThanOrEqualTo(5));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(parent);
        }
    }

#if UNITY_EDITOR
    [Test]
    public void AuthoredCrowbarUsesIndependentHammerCycleStatsAndRegisteredPrefab()
    {
        ScriptableObject crowbar = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/Crowbar.asset");
        ScriptableObject hammer = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/IndustrialHammer.asset");
        Assert.That(crowbar, Is.Not.Null);
        Assert.That(hammer, Is.Not.Null);
        Type type = crowbar.GetType();
        Assert.That(type.GetField("itemName").GetValue(crowbar), Is.EqualTo("Crowbar"));
        Assert.That((int)type.GetField("inventorySlotsRequired").GetValue(crowbar), Is.EqualTo(2));
        Assert.That((float)type.GetField("actionRange").GetValue(crowbar), Is.EqualTo(1.3f).Within(0.001f));
        Assert.That((float)type.GetField("actionCooldown").GetValue(crowbar), Is.EqualTo(1.03f).Within(0.001f));
        Assert.That((float)type.GetField("damage").GetValue(crowbar), Is.EqualTo(8f));
        Assert.That((float)type.GetField("movementSpeedPenalty").GetValue(crowbar), Is.EqualTo(0.06f).Within(0.001f));
        Assert.That((bool)type.GetField("actionRepeatability").GetValue(crowbar), Is.True);
        Assert.That(type.GetField("itemType").GetValue(crowbar).ToString(), Is.EqualTo("Crowbar"));
        Assert.That(type.GetField("impactImpulseProfile").GetValue(crowbar), Is.Null);
        Assert.That((float)type.GetField("constructionWorkPower").GetValue(crowbar), Is.EqualTo(24f));
        Assert.That(type.GetField("actionProfile").GetValue(crowbar), Is.Not.SameAs(type.GetField("actionProfile").GetValue(hammer)));
        ScriptableObject action = (ScriptableObject)type.GetField("actionProfile").GetValue(crowbar);
        Type at = action.GetType();
        float cycle = (float)at.GetField("windUpDuration").GetValue(action) + (float)at.GetField("strikeDuration").GetValue(action)
            + (float)at.GetField("impactFreezeDuration").GetValue(action) + (float)at.GetField("recoveryDuration").GetValue(action);
        Assert.That(cycle, Is.EqualTo(1.03f).Within(0.001f));
        Assert.That((float)at.GetField("movementMultiplierDuringAction").GetValue(action), Is.EqualTo(0.65f).Within(0.001f));
        GameObject prefab = (GameObject)type.GetField("equippableItemPrefab").GetValue(crowbar);
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.name, Is.EqualTo("Crowbar"));
        Component item = prefab.GetComponent(Type.GetType("EquippableItem, Assembly-CSharp"));
        Assert.That(item, Is.Not.Null);
        Assert.That(item.GetType().GetMethod("GetEquippableItemSO").Invoke(item, null), Is.SameAs(crowbar));
        UnityEditor.SerializedObject networkObject = new UnityEditor.SerializedObject(prefab.GetComponent(Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime")));
        long crowbarHash = networkObject.FindProperty("GlobalObjectIdHash").longValue;
        GameObject hammerPrefab = (GameObject)type.GetField("equippableItemPrefab").GetValue(hammer);
        UnityEditor.SerializedObject hammerNetworkObject = new UnityEditor.SerializedObject(hammerPrefab.GetComponent(Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime")));
        Assert.That(crowbarHash, Is.Not.EqualTo(hammerNetworkObject.FindProperty("GlobalObjectIdHash").longValue),
            "The actual authored Crowbar prefab must not retain IndustrialHammer's NGO hash.");
        ScriptableObject registry = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/DefaultNetworkPrefabs.asset")[0] as ScriptableObject;
        UnityEditor.SerializedProperty entries = new UnityEditor.SerializedObject(registry).FindProperty("List");
        bool registered = false;
        for (int i = 0; i < entries.arraySize; i++) if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == prefab) registered = true;
        Assert.That(registered, Is.True, "DefaultNetworkPrefabs must register the real Crowbar prefab.");
        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        UnityEditor.SerializedProperty catalog = new UnityEditor.SerializedObject(playerPrefab.GetComponent(Type.GetType("PlayerInventory, Assembly-CSharp"))).FindProperty("equippableItemCatalog");
        bool cataloged = false;
        for (int i = 0; i < catalog.arraySize; i++) if (catalog.GetArrayElementAtIndex(i).objectReferenceValue == crowbar) cataloged = true;
        Assert.That(cataloged, Is.True, "PlayerNew catalog must contain the real Crowbar asset.");
    }

    [UnityTest]
    public IEnumerator TwoSlotPickupRejectsOccupiedInventoryAndDroppedCrowbarCanBeRepicked()
    {
        ScriptableObject crowbar = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/Crowbar.asset");
        ScriptableObject axe = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/Axe.asset");
        GameObject prefab = (GameObject)crowbar.GetType().GetField("equippableItemPrefab").GetValue(crowbar);
        Type inventoryType = Type.GetType("PlayerInventory, Assembly-CSharp");
        Type itemType = Type.GetType("EquippableItem, Assembly-CSharp");
        GameObject player = new GameObject("Crowbar pickup test player");
        player.AddComponent(Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime"));
        player.AddComponent(Type.GetType("PlayerInputNew, Assembly-CSharp"));
        Component inventory = player.AddComponent(inventoryType);
        GameObject rejected = null;
        GameObject held = null;
        GameObject dropped = null;
        try
        {
            Assert.That(inventoryType.GetMethod("AddItem").Invoke(inventory, new[] { axe }), Is.EqualTo(true));
            rejected = UnityEngine.Object.Instantiate(prefab);
            Component rejectedItem = rejected.GetComponent(itemType);
            rejectedItem.GetType().GetMethod("Interact").Invoke(rejectedItem, new object[] { player.transform });
            yield return null;
            Assert.That(rejected != null, Is.True, "Crowbar pickup must be rejected while a one-slot item occupies the inventory.");
            Assert.That(inventoryType.GetMethod("GetCurrentSelectedItem").Invoke(inventory, null), Is.SameAs(axe));
            UnityEngine.Object.DestroyImmediate(rejected); rejected = null;

            Array storage = (Array)inventoryType.GetField("inventoryItems", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(inventory); storage.SetValue(null, 0);
            inventoryType.GetField("_currentInventoryOccupiedSlots", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(inventory, 0);
            held = UnityEngine.Object.Instantiate(prefab);
            Component heldItem = held.GetComponent(itemType);
            heldItem.GetType().GetMethod("Interact").Invoke(heldItem, new object[] { player.transform });
            yield return null;
            Assert.That(inventoryType.GetMethod("GetCurrentSelectedItem").Invoke(inventory, null), Is.SameAs(crowbar));
            Assert.That(inventoryType.GetMethod("IsSlotReserved", BindingFlags.Instance | BindingFlags.Public).Invoke(inventory, new object[] { 1 }), Is.EqualTo(true));

            inventoryType.GetMethod("RemoveItem", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(inventory, null);
            yield return null;
            foreach (GameObject candidate in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                Component component = candidate.GetComponent(itemType);
                if (component != null && component.GetType().GetMethod("GetEquippableItemSO").Invoke(component, null) == crowbar)
                    dropped = candidate;
            }
            Assert.That(dropped, Is.Not.Null, "Normal inventory removal should spawn the actual Crowbar prefab.");
            Component droppedItem = dropped.GetComponent(itemType);
            droppedItem.GetType().GetMethod("Interact").Invoke(droppedItem, new object[] { player.transform });
            yield return null;
            Assert.That(inventoryType.GetMethod("GetCurrentSelectedItem").Invoke(inventory, null), Is.SameAs(crowbar),
                "The dropped world Crowbar must be pickable again.");
        }
        finally
        {
            if (rejected != null) UnityEngine.Object.DestroyImmediate(rejected);
            if (held != null) UnityEngine.Object.DestroyImmediate(held);
            if (dropped != null) UnityEngine.Object.DestroyImmediate(dropped);
            if (player != null) UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [UnityTest]
    public IEnumerator HeldPrimaryInputRunsProfiledCycleRepeatsAndAltDoesNotExtractOrDamage()
    {
        ScriptableObject crowbar = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/Crowbar.asset");
        Type inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        Type inventoryType = Type.GetType("PlayerInventory, Assembly-CSharp");
        Type interactionType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        Type actionType = Type.GetType("PlayerActionController, Assembly-CSharp");
        Type healthType = Type.GetType("PlayerHealth, Assembly-CSharp");
        GameObject player = new GameObject("Crowbar profiled-action test player");
        player.AddComponent(inputType);
        Component inventory = player.AddComponent(inventoryType);
        Component interaction = player.AddComponent(interactionType);
        Component controller = player.AddComponent(actionType);
        Transform holder = new GameObject("ActionTransformHolder").transform;
        holder.SetParent(player.transform, false);
        holder.localPosition = new Vector3(0f, 0.5f, 0.8f);
        actionType.GetField("actionTransformHolder", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, holder);
        GameObject target = new GameObject("Crowbar profiled-action target");
        target.transform.position = new Vector3(0f, 0f, 1.1f);
        target.AddComponent<BoxCollider>().size = new Vector3(0.5f, 1.6f, 0.5f);
        Component health = target.AddComponent(healthType);
        target.AddComponent(inputType);
        target.AddComponent(interactionType);
        Component impulseReceiver = target.AddComponent(Type.GetType("PlayerExternalImpulseController, Assembly-CSharp"));
        try
        {
            yield return null;
            ((Behaviour)interaction).enabled = false;
            interactionType.GetField("_currentTarget", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(interaction, health);
            Assert.That(inventoryType.GetMethod("AddItem").Invoke(inventory, new object[] { crowbar }), Is.EqualTo(true));
            yield return null;
            GameObject nailObject = new GameObject("Prepared nail construction-permission contract");
            Component nail = nailObject.AddComponent(Type.GetType("MountingNail, Assembly-CSharp"));
            nail.GetType().GetField("offlineState", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(nail, Enum.Parse(Type.GetType("MountingNailState, Assembly-CSharp"), "Prepared"));
            Assert.That(controller.GetType().GetMethod("CanUseSelectedToolOn").Invoke(controller, new object[] { nail }), Is.EqualTo(false),
                "A Crowbar does not inherit the IndustrialHammer construction permission for a prepared mounting nail.");
            float initialHealth = Convert.ToSingle(healthType.GetProperty("CurrentHealth").GetValue(health));
            EventHandler primary = (EventHandler)inputType.GetField("OnAction").GetValue(player.GetComponent(inputType));
            primary.Invoke(player.GetComponent(inputType), EventArgs.Empty);
            var phases = new HashSet<string>();
            bool sawMovementPenalty = false;
            float deadline = Time.realtimeSinceStartup + 3.5f;
            while (Convert.ToSingle(healthType.GetProperty("CurrentHealth").GetValue(health)) > initialHealth - 15.9f
                && Time.realtimeSinceStartup < deadline)
            {
                string phase = controller.GetType().GetProperty("CurrentActionPhase").GetValue(controller).ToString();
                if (phase != "None")
                {
                    phases.Add(phase);
                    float movement = Convert.ToSingle(controller.GetType().GetProperty("ActionMovementMultiplier").GetValue(controller));
                    if (Math.Abs(movement - 0.65f) < 0.001f) sawMovementPenalty = true;
                }
                yield return new WaitForFixedUpdate();
            }
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The held LPM reaches two actual impacts.");
            Assert.That(Convert.ToSingle(healthType.GetProperty("CurrentHealth").GetValue(health)), Is.EqualTo(initialHealth - 16f).Within(0.01f),
                "Holding LPM repeats the configured eight-damage Crowbar attack.");
            Assert.That(phases, Does.Contain("WindUp"));
            Assert.That(phases, Does.Contain("Strike"));
            Assert.That(phases, Does.Contain("ImpactFreeze"));
            Assert.That(phases, Does.Contain("Recovery"));
            Assert.That(sawMovementPenalty, Is.True, "The profiled action applies the configured 0.65 movement multiplier.");
            Assert.That(impulseReceiver.GetType().GetProperty("IsImpulseActive").GetValue(impulseReceiver), Is.EqualTo(false));
            Assert.That((Vector3)impulseReceiver.GetType().GetProperty("CurrentExternalVelocity").GetValue(impulseReceiver), Is.EqualTo(Vector3.zero));

            EventHandler cancel = (EventHandler)inputType.GetField("OnActionCanceled").GetValue(player.GetComponent(inputType));
            cancel.Invoke(player.GetComponent(inputType), EventArgs.Empty);
            yield return new WaitForFixedUpdate();
            EventHandler alt = (EventHandler)inputType.GetField("OnActionAlt").GetValue(player.GetComponent(inputType));
            alt.Invoke(player.GetComponent(inputType), EventArgs.Empty);
            yield return null;
            Assert.That(Convert.ToSingle(healthType.GetProperty("CurrentHealth").GetValue(health)), Is.EqualTo(initialHealth - 16f).Within(0.01f),
                "Stage 3 secondary input does not extract, alter, or damage a target.");
        }
        finally
        {
            GameObject nailObject = GameObject.Find("Prepared nail construction-permission contract");
            if (nailObject != null) UnityEngine.Object.DestroyImmediate(nailObject);
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            if (player != null) UnityEngine.Object.DestroyImmediate(player);
        }
    }
#endif
    private static Transform FindChild(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }
}
















