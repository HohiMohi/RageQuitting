using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[PrebuildSetup(typeof(ExcavationTestSceneScope))]
[PostBuildCleanup(typeof(ExcavationTestSceneScope))]
public sealed class FoundationSoilShovelImpactPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const string Stage02Path = "Assets/Tests/Fixtures/TutorialTerrainStage02.unity";
    private readonly List<GameObject> created = new();
    private readonly List<Scene> openedScenes = new();
    private AsyncOperation sceneLoad;
    private Component playerAction;
    private Component interaction;
    private Component inventory;
    private Component gameplay;
    private Component previousGameplay;
    private Transform actionHolder;
    private Camera camera;
    private ScriptableObject shovel;
    private Type siteType;
    private Type volumeType;
    private Type actionType;
    private Type stageEnumType;
    private Type substageEnumType;
    private bool? lastImpactHit;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
#if UNITY_EDITOR
        Assert.That(SceneUtility.GetBuildIndexByScenePath(Stage02Path), Is.GreaterThanOrEqualTo(0));
        Type managerType = Type.GetType("GameplayManager, Assembly-CSharp");
        FieldInfo singleton = managerType.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(singleton, Is.Not.Null);
        previousGameplay = singleton.GetValue(null) as Component;
        sceneLoad = SceneManager.LoadSceneAsync(Stage02Path, LoadSceneMode.Additive);
        Assert.That(sceneLoad, Is.Not.Null);
        while (!sceneLoad.isDone) yield return null;
        Scene stage = SceneManager.GetSceneByPath(Stage02Path);
        openedScenes.Add(stage);
        siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        actionType = Type.GetType("PlayerActionController, Assembly-CSharp");
        stageEnumType = Type.GetType("BridgeConstructionStage, Assembly-CSharp");
        substageEnumType = Type.GetType("FoundationDiggingSubstage, Assembly-CSharp");
        Assert.That(siteType, Is.Not.Null);
        Assert.That(volumeType, Is.Not.Null);
        Assert.That(actionType, Is.Not.Null);

        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        Assert.That(playerPrefab, Is.Not.Null);
        GameObject player = Track(Object.Instantiate(playerPrefab));
        playerAction = player.GetComponent(actionType);
        interaction = player.GetComponent(Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
        inventory = player.GetComponent(Type.GetType("PlayerInventory, Assembly-CSharp"));
        Assert.That(playerAction, Is.Not.Null);
        Assert.That(interaction, Is.Not.Null);
        Assert.That(inventory, Is.Not.Null);
        foreach (Collider collider in player.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != playerAction && behaviour != interaction && behaviour != inventory &&
                behaviour.GetType().Name != "PlayerHealth" && behaviour.GetType().Name != "PlayerInputNew")
                behaviour.enabled = false;
        }
        actionHolder = player.transform;
        Field(actionType, "actionTransformHolder").SetValue(playerAction, actionHolder);

        GameObject cameraObject = Track(new GameObject("Foundation shovel action camera"));
        camera = cameraObject.AddComponent<Camera>();
        Field(interaction.GetType(), "aimCamera").SetValue(interaction, camera);
        shovel = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ScriptableObjectAssets/New/Shovel.asset");
        Assert.That(shovel, Is.Not.Null);
        SetSelectedItem(shovel);

        GameObject managerObject = Track(new GameObject("Foundation shovel gameplay fixture"));
        managerObject.SetActive(false);
        gameplay = managerObject.AddComponent(managerType);
        singleton.SetValue(null, gameplay);
        EventInfo impactEvent = actionType.GetEvent("OnToolActionImpact");
        impactEvent.AddEventHandler(playerAction, Delegate.CreateDelegate(impactEvent.EventHandlerType, this,
            GetType().GetMethod(nameof(CaptureImpact), PrivateInstance)));
        yield return null;
#else
        Assert.Ignore("Uses the authored Stage02 scene fixture and project assets.");
        yield break;
#endif
    }

    [UnityTest]
    public IEnumerator ProductionImpactUsesExactSupportTopForBothPitsAcrossThreeDigDepths()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the authored Stage02 scene fixture and project assets.");
        yield break;
#else
        Component[] volumes = ComponentsInOpenedScenes(volumeType);
        Assert.That(volumes, Has.Length.EqualTo(2));
        foreach (Component volume in volumes)
        {
            Component site = Property(volume, "ConstructionSite") as Component;
            Assert.That(site, Is.Not.Null);
            object workflow = Field(siteType, "workflow").GetValue(site);
            Assert.That(workflow, Is.Not.Null);
            for (int depth = 0; depth < 3; depth++)
            {
                SetDiggingState(site, "Digging", "Loosening");
                volumeType.GetMethod("ApplyDiggingState", PublicInstance).Invoke(volume, new[]
                {
                    Enum.Parse(stageEnumType, "Digging"), Enum.Parse(substageEnumType, "Loosening"),
                    (object)0f, 0f, workflow, depth
                });
                Physics.SyncTransforms();
                PositionAimAtSupportTop(volume);
                Method(interaction.GetType(), "CheckLookAtInteractable").Invoke(interaction, null);
                Assert.That(Property(interaction, "CurrentTarget"), Is.SameAs(site),
                    "PlayerInteractionNew must resolve the top support hit to its serialized site across hierarchy branches.");

                object availability = actionType.GetMethod("GetActionAvailability", PublicInstance).Invoke(playerAction, new[] { site });
                Assert.That((bool)ReadField(availability, "IsInRange"), Is.True, $"Pit {site.name}, depth {depth} should show an actionable shovel prompt.");
                Assert.That((bool)ReadField(availability, "HasCorrectTool"), Is.True);
                Assert.That(ReadField(availability, "RequiredTool").ToString(), Is.EqualTo("Shovel"));

                float before = (float)Property(site, "CurrentWorkProgress");
                Field(actionType, "actionItem").SetValue(playerAction, shovel);
                InvokeImpactAndGetHit();
                Assert.That((float)Property(site, "CurrentWorkProgress"), Is.GreaterThan(before),
                    $"The real profiled impact should apply loosening to pit {site.name}, depth {depth}.");
                Assert.That(lastImpactHit, Is.True);
            }
        }
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator ProductionImpactRejectsOutOfBoxAndObstructedAimDespiteStaleSiteTarget()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the authored Stage02 scene fixture and project assets.");
        yield break;
#else
        Component volume = ComponentsInOpenedScenes(volumeType).First();
        Component site = Property(volume, "ConstructionSite") as Component;
        object workflow = Field(siteType, "workflow").GetValue(site);
        SetDiggingState(site, "Digging", "Loosening");
        volumeType.GetMethod("ApplyDiggingState", PublicInstance).Invoke(volume, new[]
        {
            Enum.Parse(stageEnumType, "Digging"), Enum.Parse(substageEnumType, "Loosening"),
            (object)0f, 0f, workflow, 0
        });
        Physics.SyncTransforms();
        PositionAimAtSupportTop(volume);
        Field(interaction.GetType(), "_currentTarget").SetValue(interaction, site);

        Collider support = (Collider)Property(volume, "SoilSupportCollider");
        Vector3 top = SupportTop(support);
        actionHolder.position = top + support.transform.right * 1.5f;
        Assert.That((bool)ReadField(actionType.GetMethod("GetActionAvailability", PublicInstance).Invoke(playerAction, new[] { site }), "IsInRange"), Is.False);
        float before = (float)Property(site, "CurrentWorkProgress");
        Field(actionType, "actionItem").SetValue(playerAction, shovel);
        InvokeImpactAndGetHit();
        Assert.That((float)Property(site, "CurrentWorkProgress"), Is.EqualTo(before), "A hit outside the 1.25 m action box must not loosen soil.");
        Assert.That(lastImpactHit, Is.False, "The impact event must report a miss for the out-of-box ray.");

        actionHolder.position = top;
        GameObject blocker = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        Vector3 cameraToTop = top - camera.transform.position;
        blocker.transform.position = camera.transform.position + cameraToTop * 0.5f;
        blocker.transform.rotation = Quaternion.identity;
        blocker.transform.localScale = Vector3.one * 0.25f;
        Physics.SyncTransforms();
        InvokeImpactAndGetHit();
        Assert.That((float)Property(site, "CurrentWorkProgress"), Is.EqualTo(before), "The first solid obstruction must prevent a soil impact.");
        Assert.That(lastImpactHit, Is.False, "The impact event must report a miss behind an obstruction.");
        yield return null;
#endif
    }

    [UnityTest]
    public IEnumerator SideHitsWrongSubstageAndLegacyOfflineRequestCannotLoosenAnOptedInPit()
    {
#if !UNITY_EDITOR
        Assert.Ignore("Uses the authored Stage02 scene fixture and project assets.");
        yield break;
#else
        Component volume = ComponentsInOpenedScenes(volumeType).First();
        Component site = Property(volume, "ConstructionSite") as Component;
        object workflow = Field(siteType, "workflow").GetValue(site);
        SetDiggingState(site, "Digging", "Loosening");
        volumeType.GetMethod("ApplyDiggingState", PublicInstance).Invoke(volume, new[]
        {
            Enum.Parse(stageEnumType, "Digging"), Enum.Parse(substageEnumType, "Loosening"),
            (object)0f, 0f, workflow, 0
        });
        Physics.SyncTransforms();
        Collider support = (Collider)Property(volume, "SoilSupportCollider");
        Vector3 top = SupportTop(support);
        camera.transform.SetPositionAndRotation(top + support.transform.right * (support.bounds.extents.x + 0.5f),
            Quaternion.LookRotation(-support.transform.right, support.transform.up));
        Field(interaction.GetType(), "_currentTarget").SetValue(interaction, site);
        float before = (float)Property(site, "CurrentWorkProgress");
        Field(actionType, "actionItem").SetValue(playerAction, shovel);
        InvokeImpactAndGetHit();
        Assert.That((float)Property(site, "CurrentWorkProgress"), Is.EqualTo(before), "A support side face is not a soil surface.");
        Assert.That(lastImpactHit, Is.False);

        Component bridgeComponent = (Component)Property(site, "BridgeComponent");
        gameplay.GetType().GetMethod("RequestConstructionSiteWork", PublicInstance).Invoke(gameplay,
            new object[] { bridgeComponent, shovel, Property(shovel, "ConstructionWorkPower"), -1 });
        Assert.That((float)Property(site, "CurrentWorkProgress"), Is.EqualTo(before), "The legacy generic request must be rejected for opted-in loosening.");

        SetDiggingState(site, "Digging", "SoilRemoval");
        float removalBefore = (float)Property(site, "CurrentWorkProgress");
        camera.transform.SetPositionAndRotation(top + support.transform.up, Quaternion.LookRotation(-support.transform.up, Vector3.forward));
        Physics.SyncTransforms();
        InvokeImpactAndGetHit();
        Assert.That((float)Property(site, "CurrentWorkProgress"), Is.EqualTo(removalBefore), "The special shovel action is limited to Loosening.");
        Assert.That(lastImpactHit, Is.False);
        yield return null;
#endif
    }

#if UNITY_EDITOR
    private GameObject Track(GameObject value) { created.Add(value); return value; }
    private Component[] ComponentsInOpenedScenes(Type type) => Resources.FindObjectsOfTypeAll(type).Cast<Component>()
        .Where(component => component != null && component.gameObject.scene.path == Stage02Path).ToArray();
    private void PositionAimAtSupportTop(Component volume)
    {
        Collider support = (Collider)Property(volume, "SoilSupportCollider");
        Vector3 top = SupportTop(support);
        actionHolder.position = top;
        camera.transform.SetPositionAndRotation(top + support.transform.up, Quaternion.LookRotation(-support.transform.up, Vector3.forward));
        Physics.SyncTransforms();
    }
    private static Vector3 SupportTop(Collider collider)
    {
        BoxCollider box = (BoxCollider)collider;
        return box.transform.TransformPoint(box.center + Vector3.up * (box.size.y * .5f));
    }
    private bool InvokeImpactAndGetHit()
    {
        lastImpactHit = null;
        Method(actionType, "ResolveProfiledImpact").Invoke(playerAction, null);
        return lastImpactHit == true;
    }
    private void CaptureImpact(object sender, EventArgs args)
    {
        lastImpactHit = (bool)args.GetType().GetField("Hit", BindingFlags.Instance | BindingFlags.Public).GetValue(args);
    }
    private void SetSelectedItem(ScriptableObject item)
    {
        FieldInfo itemsField = Field(inventory.GetType(), "inventoryItems");
        Array items = (Array)itemsField.GetValue(inventory);
        if (items == null || items.Length == 0) items = Array.CreateInstance(itemsField.FieldType.GetElementType(), 2);
        items.SetValue(item, 0);
        itemsField.SetValue(inventory, items);
        Field(inventory.GetType(), "_currentInventoryOccupiedSlots").SetValue(inventory, 1);
        Field(inventory.GetType(), "_selectedItemIndex").SetValue(inventory, 0);
    }
    private static void SetDiggingState(Component site, string stage, string substage)
    {
        Type bridgeSiteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        Field(bridgeSiteType, "currentStage").SetValue(site, Enum.Parse(Type.GetType("BridgeConstructionStage, Assembly-CSharp"), stage));
        Field(bridgeSiteType, "diggingSubstage").SetValue(site, Enum.Parse(Type.GetType("FoundationDiggingSubstage, Assembly-CSharp"), substage));
    }
    private static FieldInfo Field(Type type, string name) => type.GetField(name, PrivateInstance) ?? type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
    private static MethodInfo Method(Type type, string name) => type.GetMethod(name, PrivateInstance);
    private static object Property(object target, string name) => target.GetType().GetProperty(name, PublicInstance).GetValue(target);
    private static object ReadField(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public).GetValue(target);
#endif

    [UnityTearDown]
    public IEnumerator TearDown()
    {
#if UNITY_EDITOR
        foreach (GameObject item in created) if (item != null) Object.Destroy(item);
        yield return null;
        foreach (Scene scene in openedScenes)
            if (scene.IsValid() && scene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                if (unload != null) while (!unload.isDone) yield return null;
            }
        openedScenes.Clear();
        if (gameplay != null)
            gameplay.GetType().GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, previousGameplay);
        created.Clear();
#else
        yield break;
#endif
    }
}
