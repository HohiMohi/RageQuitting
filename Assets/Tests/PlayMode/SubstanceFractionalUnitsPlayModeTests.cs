using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Type = System.Type;

public sealed class SubstanceFractionalUnitsPlayModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<GameObject> roots = new();
    private readonly List<Object> transientAssets = new();
    private GameObject bucketPrefab, pilePrefab, playerPrefab, foundationPrefab, mixerPrefab;
    private Object soil, water, gravel, mixerProfile;
    private Type bucketType, pileType, substanceType, playerType;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        bucketType = Type.GetType("PortableSubstanceContainer, Assembly-CSharp");
        pileType = Type.GetType("LooseSubstancePile, Assembly-CSharp");
        substanceType = Type.GetType("ContainerSubstanceSO, Assembly-CSharp");
        playerType = Type.GetType("PlayerInteractionNew, Assembly-CSharp");
        Assert.That(new[] { bucketType, pileType, substanceType, playerType }, Has.None.Null);
#if UNITY_EDITOR
        bucketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/PortableBucket.prefab");
        pilePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/EarthPile.prefab");
        playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        foundationPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/BridgeComponents/WoodenFoundationBridgeComponent.prefab");
        mixerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Concrete/ConcreteMixer.prefab");
        mixerProfile = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/GeneratedAssets/ConcretePreparation/ConcreteMixerProfile.asset");
        soil = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
        water = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/Substances/Water.asset");
        gravel = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/Substances/Gravel.asset");
#endif
        Assert.That(bucketPrefab, Is.Not.Null); Assert.That(pilePrefab, Is.Not.Null); Assert.That(soil, Is.Not.Null);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        foreach (Object asset in transientAssets) if (asset != null) Object.Destroy(asset);
        roots.Clear(); transientAssets.Clear(); yield return null;
    }

    [UnityTest]
    public IEnumerator BucketPreservesFractionsBoundsAndRejectsInvalidOrMixedAmounts()
    {
        Component bucket = CreateBucket();
        Assert.That(Add(bucket, soil, .25f), Is.EqualTo(.25f).Within(.00001f));
        Assert.That(Add(bucket, soil, .5f), Is.EqualTo(.5f).Within(.00001f));
        Assert.That(Add(bucket, soil, 2.75f), Is.EqualTo(2.25f).Within(.00001f));
        Assert.That(Units(bucket), Is.EqualTo(3f).Within(.00001f));
        Assert.That(Remove(bucket, soil, 3f), Is.EqualTo(3f).Within(.00001f));
        Assert.That(Add(bucket, soil, 2.75f), Is.EqualTo(2.75f).Within(.00001f));
        Assert.That(Add(bucket, water, .25f), Is.Zero);
        Assert.That(Add(bucket, soil, .5f), Is.EqualTo(.25f).Within(.00001f));
        Assert.That(Remove(bucket, soil, 2.9f), Is.EqualTo(2.9f).Within(.00001f));
        Assert.That(Units(bucket), Is.EqualTo(.1f).Within(.00001f));
        Assert.That((string)Prop(bucket, "ContentsLabel"), Does.Contain("0.1"));
        Assert.That((string)Static("SubstanceAmount", "Format", .004f), Is.Not.EqualTo("0"));
        Assert.That((string)Static("SubstanceAmount", "Format", .1f), Is.EqualTo("0.1"));
        foreach (float invalid in new[] { 0f, -.25f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.That(Add(bucket, soil, invalid), Is.Zero);
            Assert.That(Remove(bucket, soil, invalid), Is.Zero);
            Assert.That(Units(bucket), Is.EqualTo(.1f).Within(.00001f));
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator RequestContextActionMovesOnlyActualRemainderAndSourceTail()
    {
        Component bucket = CreateBucket();
        Add(bucket, soil, 2.9f);
        GameObject playerObject = CreateIsolatedPlayer();
        Component player = playerObject.GetComponent(playerType);
        Assert.That(player, Is.Not.Null);
        Component resource = bucket.gameObject.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"));
        resource.GetType().GetMethod("PickedUp").Invoke(resource, new object[] { playerObject.transform });

        Component pile = CreatePile(soil, .25f);
        pile.transform.position = playerObject.transform.position + Vector3.forward;
        bucketType.GetMethod("RequestContextAction").Invoke(bucket, new object[] { player, pile });
        Assert.That(Units(bucket), Is.EqualTo(3f).Within(.00001f));
        Assert.That(Units(pile), Is.EqualTo(.15f).Within(.00001f));

        Remove(bucket, soil, .2f);
        bucketType.GetMethod("RequestContextAction").Invoke(bucket, new object[] { player, pile });
        Assert.That(Units(bucket), Is.EqualTo(2.95f).Within(.00001f));
        Assert.That(pile == null || Units(pile) == 0f, Is.True);

        // Free space, then let the real RMB contract request one unit from a smaller source tail.
        Remove(bucket, soil, .1f);
        Component tail = CreatePile(soil, .05f);
        tail.transform.position = playerObject.transform.position + Vector3.forward;
        bucketType.GetMethod("RequestContextAction").Invoke(bucket, new object[] { player, tail });
        Assert.That(Units(bucket), Is.EqualTo(2.9f).Within(.00001f));
        Assert.That(tail == null || Units(tail) == 0f, Is.True);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PartialDumpKeepsContentsWhenSpawnFailsAndEmptyPileIsDestroyed()
    {
        Component bucket = CreateBucket(); Add(bucket, soil, .25f);
        Field(bucket, "loosePilePrefab", null);
        bucketType.GetMethod("Empty").Invoke(bucket, new object[] { true, new Vector3(100, 50, 100) });
        Assert.That(Units(bucket), Is.EqualTo(.25f).Within(.00001f));
        Field(bucket, "loosePilePrefab", pilePrefab.GetComponent(pileType));
        bucketType.GetMethod("Empty").Invoke(bucket, new object[] { true, new Vector3(100, 50, 100) });
        Assert.That(Units(bucket), Is.Zero);
        Component dumped = null;
        foreach (Component candidate in Object.FindObjectsByType<Component>(FindObjectsSortMode.None))
            if (candidate != null && candidate.GetType() == pileType && Vector3.Distance(candidate.transform.position, new Vector3(100, 50, 100)) < .01f)
            { dumped = candidate; break; }
        Assert.That(dumped, Is.Not.Null, "The successful dump should create a world pile at the requested position.");
        roots.Add(dumped.gameObject);
        Assert.That(Units(dumped), Is.EqualTo(.25f).Within(.00001f));
        Component pile = CreatePile(soil, .25f);
        Assert.That(Extract(pile, soil, .25f), Is.EqualTo(.25f).Within(.00001f));
        yield return null;
        Assert.That(pile == null, Is.True);
    }

    [UnityTest]
    public IEnumerator MixerDepositRejectsPartialBucketAtomicallyAndAcceptsFullThreeUnitBucket()
    {
        Type mixerType = Type.GetType("ConcreteMixerController, Assembly-CSharp");
        Assert.That(mixerType, Is.Not.Null); Assert.That(mixerPrefab, Is.Not.Null); Assert.That(mixerProfile, Is.Not.Null);
        GameObject mixerObject = Object.Instantiate(mixerPrefab); roots.Add(mixerObject);
        Component mixer = mixerObject.GetComponent(mixerType);
        Object profile = Object.Instantiate(mixerProfile);
        transientAssets.Add(profile);
        profile.GetType().GetField("alwaysReadyConcreteForTesting", Instance).SetValue(profile, false);
        mixerType.GetField("profile", Instance).SetValue(mixer, profile);
        GameObject playerObject = CreateIsolatedPlayer();
        Component player = playerObject.GetComponent(playerType);
        Transform loadingPoint = (Transform)mixerType.GetField("loadingInteractionPoint", Instance).GetValue(mixer);
        playerObject.transform.position = loadingPoint.position;
        yield return null;

        Component partial = CreateBucket(); Add(partial, water, .25f);
        Assert.That((bool)mixerType.GetMethod("TryDeposit").Invoke(mixer, new object[] { partial, player }), Is.False);
        Assert.That(Units(partial), Is.EqualTo(.25f).Within(.00001f));
        Assert.That((float)Prop(mixer, "WaterUnits"), Is.Zero);
        Assert.That((float)Prop(mixer, "GravelUnits"), Is.Zero);

        foreach (Object ingredient in new[] { water, gravel })
        {
            for (int i = 0; i < 2; i++)
            {
                Component fullIngredient = CreateBucket(); Add(fullIngredient, ingredient, 3f);
                Assert.That((bool)mixerType.GetMethod("TryDeposit").Invoke(mixer, new object[] { fullIngredient, player }), Is.True);
                Assert.That(Units(fullIngredient), Is.Zero);
            }
        }
        Assert.That((float)Prop(mixer, "WaterUnits"), Is.EqualTo(6f).Within(.00001f));
        Assert.That((float)Prop(mixer, "GravelUnits"), Is.EqualTo(6f).Within(.00001f));
        mixerType.GetField("localCementBags", Instance).SetValue(mixer, 1);
        mixerType.GetField("localBatchState", Instance).SetValue(mixer, System.Enum.Parse(Type.GetType("ConcreteMixerBatchState, Assembly-CSharp"), "Mixing"));
        mixerType.GetField("localCrankOwner", Instance).SetValue(mixer, 0ul);
        mixerType.GetField("localMixingDegrees", Instance).SetValue(mixer, 2150f);
        Transform crankPoint = (Transform)mixerType.GetField("crankInteractionPoint", Instance).GetValue(mixer);
        playerObject.transform.position = crankPoint.position;
        mixerType.GetMethod("ApplyCrankDelta", Instance).Invoke(mixer, new object[] { 10f, 2160f, 0ul });
        Assert.That(Prop(mixer, "BatchState").ToString(), Is.EqualTo("ConcreteReady"),
            "The established full recipe remains 6 water + 6 gravel + 1 cement bag.");

    }

    [UnityTest]
    public IEnumerator BridgeNetworkStateKeepsQuarterUnitWithoutDirectAssemblyTypes()
    {
        if (foundationPrefab == null) Assert.Ignore("Foundation prefab unavailable outside Unity Editor asset loading.");
        GameObject instance = Object.Instantiate(foundationPrefab); roots.Add(instance);
        Type siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        Type stateType = Type.GetType("BridgeComponentNetworkState, Assembly-CSharp");
        Type bridgeType = Type.GetType("BridgeComponent, Assembly-CSharp");
        Assert.That(siteType, Is.Not.Null); Assert.That(stateType, Is.Not.Null);
        Behaviour bridge = instance.GetComponentInChildren(bridgeType) as Behaviour;
        Assert.That(bridge, Is.Not.Null);
        // The multiplayer-start test scene has no GameplayManager; avoid invoking the prefab's unrelated Start subscription.
        bridge.enabled = false;
        Component site = instance.GetComponentInChildren(siteType);
        Assert.That(site, Is.Not.Null);
        object state = System.Activator.CreateInstance(stateType, new object[] { 1 });
        stateType.GetField("constructionStage", Instance).SetValue(state, 1);
        stateType.GetField("constructionValueB", Instance).SetValue(state, 1);
        stateType.GetField("constructionAux0", Instance).SetValue(state, .25f);
        siteType.GetMethod("ApplyNetworkState").Invoke(site, new[] { state });
        Assert.That((float)Prop(site, "RemovedSoilUnits"), Is.EqualTo(.25f).Within(.00001f));
        object roundTrip = System.Activator.CreateInstance(stateType, new object[] { (int)Prop(Prop(site, "BridgeComponent"), "ComponentID") });
        object[] roundTripArgs = { roundTrip };
        siteType.GetMethod("PopulateNetworkState").Invoke(site, roundTripArgs);
        roundTrip = roundTripArgs[0];
        Assert.That((float)stateType.GetField("constructionAux0", Instance).GetValue(roundTrip), Is.EqualTo(.25f).Within(.00001f));
        Component returned = CreatePile(soil, .75f);
        Assert.That((float)pileType.GetMethod("TryReturnTo").Invoke(returned, new object[] { site }), Is.EqualTo(.25f).Within(.00001f));
        Assert.That((float)Prop(site, "RemovedSoilUnits"), Is.Zero);
        Assert.That(Units(returned), Is.EqualTo(.5f).Within(.00001f), "Unaccepted material remains in the source pile.");
        Assert.That(Extract(returned, soil, .5f), Is.EqualTo(.5f).Within(.00001f));
        yield return null;
        Assert.That(returned == null, Is.True, "Removing the remaining fractional material consumes the pile.");
    }

    private Component CreateBucket()
    { GameObject go = Object.Instantiate(bucketPrefab); roots.Add(go); return go.GetComponent(bucketType); }
    private GameObject CreateIsolatedPlayer()
    {
        GameObject inactiveParent = new GameObject("Fractional substance inactive player fixture");
        inactiveParent.SetActive(false);
        roots.Add(inactiveParent);
        GameObject player = Object.Instantiate(playerPrefab, inactiveParent.transform, false);
        player.name = "Fractional substance test player";
        Component interaction = player.GetComponent(playerType);
        Type inputType = Type.GetType("PlayerInputNew, Assembly-CSharp");
        Type inputSystemPlayerType = Type.GetType("UnityEngine.InputSystem.PlayerInput, Unity.InputSystem");
        Component gameInput = inputType != null ? player.GetComponent(inputType) : null;
        Component inputSystemPlayer = inputSystemPlayerType != null ? player.GetComponent(inputSystemPlayerType) : null;
        Assert.That(interaction, Is.Not.Null);
        Assert.That(gameInput, Is.Not.Null);
        Assert.That(inputSystemPlayer, Is.Not.Null);
        if (gameInput is Behaviour gameInputBehaviour) gameInputBehaviour.enabled = false;
        if (inputSystemPlayer is Behaviour systemInputBehaviour) systemInputBehaviour.enabled = false;
        foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == interaction || behaviour == gameInput || behaviour == inputSystemPlayer) continue;
            behaviour.enabled = false;
        }
        inactiveParent.SetActive(true);
        Assert.That(interaction.gameObject.activeInHierarchy, Is.True);
        Assert.That(gameInput is Behaviour disabledGameInput && !disabledGameInput.enabled, Is.True);
        Assert.That(inputSystemPlayer is Behaviour disabledInputSystem && !disabledInputSystem.enabled, Is.True);
        return player;
    }
    private Component CreatePile(Object substance, float amount)
    {
        GameObject go = Object.Instantiate(pilePrefab); roots.Add(go);
        Component pile = go.GetComponent(pileType);
        pileType.GetMethod("Initialize").Invoke(pile, new object[] { substance, amount });
        return pile;
    }
    private static float Add(Component c, Object s, float n) => (float)c.GetType().GetMethod("TryAddUnits", new[] { Type.GetType("ContainerSubstanceSO, Assembly-CSharp"), typeof(float) }).Invoke(c, new object[] { s, n });
    private static float Remove(Component c, Object s, float n) => (float)c.GetType().GetMethod("TryRemoveUnits").Invoke(c, new object[] { s, n });
    private static float Extract(Component c, Object s, float n) => (float)c.GetType().GetMethod("TryExtract").Invoke(c, new object[] { s, n });
    private static float Units(Component c) => (float)Prop(c, "CurrentUnits");
    private static object Prop(object o, string name) => o.GetType().GetProperty(name).GetValue(o);
    private static void Field(Component c, string name, object value) => c.GetType().GetField(name, Instance).SetValue(c, value);
    private static object Static(string type, string method, object value) => Type.GetType(type + ", Assembly-CSharp").GetMethod(method).Invoke(null, new[] { value });
}
