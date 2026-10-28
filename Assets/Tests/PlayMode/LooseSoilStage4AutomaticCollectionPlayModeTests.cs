using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;
using Type = System.Type;

public sealed class LooseSoilStage4AutomaticCollectionPlayModeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void BucketPrefabUsesApprovedAutomaticCollectionDefaults()
    {
#if UNITY_EDITOR
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/New/Substances/PortableBucket.prefab");
        Assert.That(prefab, Is.Not.Null);
        Component container = prefab.GetComponent(Type.GetType("PortableSubstanceContainer, Assembly-CSharp"));
        Assert.That(container, Is.Not.Null);
        Assert.That(Read<float>(container, "looseSoilUnitsPerSecond"), Is.EqualTo(.5f));
        Assert.That(Read<float>(container, "looseSoilBrushRadius"), Is.EqualTo(.6f));
        Assert.That(Read<float>(container, "looseSoilWholeLayerTailThreshold"), Is.EqualTo(.05f));
        Assert.That(Read<float>(container, "looseSoilSampleInterval"), Is.EqualTo(.1f));
        Assert.That(Read<bool>(container, "automaticLooseSoilCollection"), Is.True);
#else
        Assert.Ignore("Prefab configuration is verified through the Unity Editor AssetDatabase.");
#endif
    }

    [Test]
    public void ActiveCollectionFieldCanBeSelectedAtClearedBrushCenter()
    {
        Type fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        Assert.That(fieldType, Is.Not.Null);
        GameObject root = new GameObject("Stage4 collection field fixture");
        try
        {
            root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();
            Component field = root.AddComponent(fieldType);
            fieldType.GetMethod("Initialize", Instance).Invoke(field,
                new object[] { 2f, 2f, .5f, .4f, .4f, 6f });
            fieldType.GetMethod("SetSurfaceState", Instance).Invoke(field, new object[] { true, null });
            object heightField = fieldType.GetProperty("HeightField").GetValue(field);
            float initialUnits = (float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField);
            heightField.GetType().GetMethod("Remove", new[] { typeof(float), typeof(float), typeof(float), typeof(float) })
                .Invoke(heightField, new object[] { -.25f, -.25f, .3f, 1f });
            Assert.That((float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField), Is.LessThan(initialUnits));
            fieldType.GetMethod("RebuildMesh", Instance).Invoke(field, null);

            MethodInfo find = fieldType.GetMethod("TryFindCollectionField",
                BindingFlags.Public | BindingFlags.Static);
            Vector3 carvedCell = new Vector3(-.25f, 0f, -.25f);
            object[] args = { carvedCell, null };
            Assert.That(find.Invoke(null, args), Is.True,
                "Collection eligibility is based on brush bounds, not positive depth at the holder's exact cell.");
            Assert.That(args[1], Is.SameAs(field));
            args = new object[] { carvedCell + Vector3.up * .46f, null };
            Assert.That(find.Invoke(null, args), Is.False, "Collection above the permitted surface tolerance is rejected.");
            fieldType.GetMethod("SetSurfaceState", Instance).Invoke(field, new object[] { false, null });
            args = new object[] { carvedCell, null };
            Assert.That(find.Invoke(null, args), Is.False, "Hidden fields cannot be collected.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void InvalidCollectorConfigurationIsRejectedAndHolderFootUsesCapsuleBottom()
    {
        Type containerType = Type.GetType("PortableSubstanceContainer, Assembly-CSharp");
        GameObject bucket = new GameObject("Stage4 sampler config fixture");
        GameObject holder = new GameObject("Stage4 capsule holder");
        try
        {
            Component container = bucket.AddComponent(containerType);
            MethodInfo valid = containerType.GetMethod("HasValidAutomaticCollectionSettings", Instance);
            Assert.That(valid.Invoke(container, null), Is.True);
            containerType.GetField("looseSoilBrushRadius", Instance).SetValue(container, float.NaN);
            Assert.That(valid.Invoke(container, null), Is.False);
            containerType.GetField("looseSoilBrushRadius", Instance).SetValue(container, .6f);
            containerType.GetField("looseSoilUnitsPerSecond", Instance).SetValue(container, 0f);
            Assert.That(valid.Invoke(container, null), Is.False);

            holder.transform.position = new Vector3(0f, 3f, 0f);
            CharacterController capsule = holder.AddComponent<CharacterController>();
            capsule.height = 2f;
            capsule.center = new Vector3(0f, 1f, 0f);
            Vector3 point = (Vector3)containerType.GetMethod("GetCollectionPoint", Instance)
                .Invoke(container, new object[] { holder.transform });
            Assert.That(point.y, Is.EqualTo(3f).Within(.001f));
        }
        finally
        {
            Object.DestroyImmediate(bucket);
            Object.DestroyImmediate(holder);
        }
    }

    [UnityTest]
    public IEnumerator PickedUpBucketCollectsOnlyAfterActualHorizontalHolderMovement()
    {
#if !UNITY_EDITOR
        Assert.Ignore("The production player and soil assets require Editor AssetDatabase access.");
        yield break;
#else
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
            Assert.Ignore("This integration fixture requires an offline authority context.");
        GameObject playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        GameObject bucketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/PortableBucket.prefab");
        Object soil = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
        Object water = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/ScriptableObjectAssets/New/Substances/Water.asset");
        Assert.That(playerPrefab, Is.Not.Null); Assert.That(bucketPrefab, Is.Not.Null);
        Assert.That(soil, Is.Not.Null); Assert.That(water, Is.Not.Null);
        GameObject siteRoot = CreateActiveLooseSoilSite();
        Type siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        Type volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        Type fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        Component fixtureSite = siteRoot.GetComponentInChildren(siteType);
        Component fixtureVolume = siteRoot.GetComponentInChildren(volumeType);
        Component fixtureField = siteRoot.GetComponentInChildren(fieldType);
        Assert.That(fixtureField.GetComponentInParent(siteType), Is.Null,
            "The Stage02 field and volume live under a separate hierarchy from the construction-site component.");
        Assert.That(Read<Component>(fixtureVolume, "constructionSite"), Is.SameAs(fixtureSite),
            "The volume's serialized construction-site reference provides the owning site.");
        Assert.That(Read<Component>(fixtureSite, "excavationVolume"), Is.SameAs(fixtureVolume));
        GameObject inactivePlayerRoot = new GameObject("Inactive stage4 player parent");
        inactivePlayerRoot.SetActive(false);
        GameObject player = Object.Instantiate(playerPrefab, new Vector3(3.3f, .08f, 0f), Quaternion.identity,
            inactivePlayerRoot.transform);
        GameObject bucket = Object.Instantiate(bucketPrefab, new Vector3(0f, .5f, 0f), Quaternion.identity);
        try
        {
            Type fpcType = Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp");
            ((Behaviour)player.GetComponent(fpcType)).enabled = false;
            player.GetComponent<PlayerInput>().enabled = false;
            Type playerInputNewType = Type.GetType("PlayerInputNew, Assembly-CSharp");
            ((Behaviour)player.GetComponent(playerInputNewType)).enabled = false;
            inactivePlayerRoot.SetActive(true);
            yield return null;
            CharacterController playerCapsule = player.GetComponent<CharacterController>();
            playerCapsule.center += Vector3.left * .2f;
            Component resource = bucket.GetComponent(Type.GetType("BaseResourceNew, Assembly-CSharp"));
            Component container = bucket.GetComponent(Type.GetType("PortableSubstanceContainer, Assembly-CSharp"));
            container.GetType().GetField("capacity", Instance).SetValue(container, 100);
            container.GetType().GetField("looseSoilBrushRadius", Instance).SetValue(container, 100f);
            Component interaction = player.GetComponent(Type.GetType("PlayerInteractionNew, Assembly-CSharp"));
            ((Behaviour)interaction).enabled = false;
            resource.GetType().GetMethod("PickedUp").Invoke(resource, new object[] { player.transform });
            Assert.That(interaction.GetType().GetMethod("GetPickedUpGameObject").Invoke(interaction, null), Is.SameAs(bucket));
            playerCapsule.enabled = false; // Keep the synthetic holder pose exact while retaining its capsule-bottom point.
            yield return new WaitForSeconds(.3f);
            Assert.That(Read<float>(container, "CurrentUnits"), Is.Zero, "Holding the bucket still does not collect soil.");

            float movementStarted = Time.time;
            while (Time.time - movementStarted < 1.05f)
            {
                player.transform.position += Vector3.forward * (.6f * Time.deltaTime);
                yield return null;
            }
            yield return new WaitForSeconds(.15f); // Let a movement interval begun on the final frame settle.
            float collected = Read<float>(container, "CurrentUnits");
            float actualMovingTime = Time.time - movementStarted;
            Assert.That(collected, Is.EqualTo(.5f * actualMovingTime).Within(.12f),
                "The bucket collects at about the configured rate while the holder remains horizontally moving.");
            yield return new WaitForSeconds(2f);
            Assert.That(Read<float>(container, "CurrentUnits"), Is.EqualTo(collected).Within(.000001f),
                "A long stationary interval does not continue collection or bank time for later.");
            container.GetType().GetField("looseSoilBrushRadius", Instance).SetValue(container, .6f);
            player.transform.position += Vector3.up * .25f;
            yield return new WaitForSeconds(.3f);
            Assert.That(Read<float>(container, "CurrentUnits"), Is.EqualTo(collected).Within(.000001f),
                "Vertical-only motion cannot collect soil.");

            player.transform.position = new Vector3(100f, .08f, 0f);
            yield return new WaitForSeconds(.3f);
            player.transform.position = new Vector3(3.3f, .08f, 0f);
            yield return new WaitForSeconds(.3f);
            float beforeReentry = Read<float>(container, "CurrentUnits");
            player.transform.position += Vector3.forward * .2f;
            yield return new WaitForSeconds(.35f);
            float afterReentry = Read<float>(container, "CurrentUnits");
            Assert.That(afterReentry - beforeReentry, Is.InRange(.000001f, .2f),
                "Re-entry should collect a bounded moving interval, without accumulated idle time.");

            object field = siteRoot.GetComponentInChildren(Type.GetType("FoundationLooseSoilField, Assembly-CSharp"));
            object heightField = field.GetType().GetProperty("HeightField").GetValue(field);
            float beforeIncompatible = (float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField);
            container.GetType().GetMethod("Empty").Invoke(container, new object[] { false, Vector3.zero });
            MethodInfo add = container.GetType().GetMethod("TryAddUnits", new[] { soil.GetType(), typeof(float) });
            add.Invoke(container, new[] { water, (object)0.25f });
            player.transform.position += Vector3.forward * .2f;
            yield return new WaitForSeconds(.35f);
            Assert.That(Read<float>(container, "CurrentUnits"), Is.EqualTo(.25f).Within(.00001f));
            Assert.That((float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField),
                Is.EqualTo(beforeIncompatible).Within(.00001f), "An incompatible bucket substance cannot remove soil.");

            container.GetType().GetMethod("Empty").Invoke(container, new object[] { false, Vector3.zero });
            container.GetType().GetField("capacity", Instance).SetValue(container, 3);
            add.Invoke(container, new[] { soil, (object)2.98f });
            float beforeNearlyFull = (float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField);
            player.transform.position += Vector3.forward * .2f;
            yield return new WaitForSeconds(.35f);
            Assert.That(Read<float>(container, "CurrentUnits"), Is.EqualTo(3f).Within(.0001f),
                "A bucket with only .02 capacity accepts exactly the available local soil amount.");
            Assert.That((float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField), Is.LessThan(beforeNearlyFull),
                "Near-full capacity still removes the matching partial amount from the source.");

            container.GetType().GetMethod("Empty").Invoke(container, new object[] { false, Vector3.zero });
            add.Invoke(container, new[] { soil, (object)3f });
            float beforeFull = (float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField);
            player.transform.position += Vector3.forward * .2f;
            yield return new WaitForSeconds(.35f);
            Assert.That(Read<float>(container, "CurrentUnits"), Is.EqualTo(3f).Within(.00001f));
            Assert.That((float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField),
                Is.EqualTo(beforeFull).Within(.00001f), "A full bucket cannot remove additional soil.");

            container.GetType().GetMethod("Empty").Invoke(container, new object[] { false, Vector3.zero });
            add.Invoke(container, new[] { soil, (object)0.25f });
            float beforeOutside = (float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField);
            player.transform.position = new Vector3(100f, .08f, player.transform.position.z);
            yield return new WaitForSeconds(.35f);
            Assert.That(Read<float>(container, "CurrentUnits"), Is.EqualTo(.25f).Within(.00001f),
                "A held compatible bucket outside the field cannot collect.");
            Assert.That((float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField),
                Is.EqualTo(beforeOutside).Within(.00001f));
            player.transform.position = new Vector3(3.3f, .08f, player.transform.position.z);
            yield return new WaitForSeconds(.3f);
            player.transform.position += Vector3.forward * .2f;
            yield return new WaitForSeconds(.35f);
            Assert.That(Read<float>(container, "CurrentUnits"), Is.GreaterThan(.25f),
                "After leaving and returning inside brush range, a compatible bucket collects actual soil again.");
            Assert.That((float)heightField.GetType().GetProperty("TotalUnits").GetValue(heightField),
                Is.LessThan(beforeOutside), "The eligible return-to-pit movement removes material.");

            resource.GetType().GetField("isPickedUp", Instance).SetValue(resource, false);
            float beforeDrop = Read<float>(container, "CurrentUnits");
            player.transform.position += Vector3.forward * .2f;
            yield return new WaitForSeconds(.35f);
            Assert.That(Read<float>(container, "CurrentUnits"), Is.EqualTo(beforeDrop).Within(.00001f),
                "Dropping the bucket stops automatic collection.");

        }
        finally
        {
            Object.Destroy(player); Object.Destroy(bucket); Object.Destroy(siteRoot);
            if (inactivePlayerRoot != null) Object.Destroy(inactivePlayerRoot);
        }
        yield return null;
#endif
    }

    private static GameObject CreateActiveLooseSoilSite()
    {
        Type bridgeType = Type.GetType("Bridge, Assembly-CSharp");
        Type componentType = Type.GetType("BridgeComponent, Assembly-CSharp");
        Type siteType = Type.GetType("BridgeConstructionSite, Assembly-CSharp");
        Type volumeType = Type.GetType("FoundationExcavationVolume, Assembly-CSharp");
        Type fieldType = Type.GetType("FoundationLooseSoilField, Assembly-CSharp");
        GameObject bridgeRoot = new GameObject("Stage4 automatic collection site");
        bridgeRoot.SetActive(false);
        Component bridge = bridgeRoot.AddComponent(bridgeType);
        GameObject holder = new GameObject("Foundation holder"); holder.transform.SetParent(bridgeRoot.transform, false);
        bridgeType.GetField("bridgeComponentHolder", Instance).SetValue(bridge, holder);
        UnityEngine.Object definition = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
            "Assets/ScriptableObjectAssets/New/BridgeComponentSO/WoodenFoundation.asset");
        Material looseMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat");
        GameObject siteObject = new GameObject("Foundation site"); siteObject.transform.SetParent(holder.transform, false);
        Component component = siteObject.AddComponent(componentType);
        // The isolated site has no GameplayManager; its component Start subscription is unrelated to soil collection.
        ((Behaviour)component).enabled = false;
        componentType.GetField("componentID", Instance).SetValue(component, 0);
        componentType.GetField("bridgeComponentSO", Instance).SetValue(component, definition);
        Component site = siteObject.AddComponent(siteType);
        Set(site, "requiresSiteClearing", false);
        GameObject volumeObject = new GameObject("Separate excavation-volume branch");
        volumeObject.transform.SetParent(holder.transform, false);
        Component volume = volumeObject.AddComponent(volumeType);
        GameObject supportObject = new GameObject("Dynamic soil support"); supportObject.transform.SetParent(volumeObject.transform, false);
        supportObject.transform.localPosition = new Vector3(0f, .02f, 0f);
        BoxCollider support = supportObject.AddComponent<BoxCollider>(); support.size = new Vector3(10f, .12f, 4f);
        MeshRenderer supportRenderer = supportObject.AddComponent<MeshRenderer>();
        GameObject fieldObject = new GameObject("Loose soil field", typeof(MeshFilter), typeof(MeshRenderer));
        fieldObject.transform.SetParent(volumeObject.transform, false);
        Component field = fieldObject.AddComponent(fieldType);
        fieldType.GetMethod("Initialize", Instance).Invoke(field, new object[] { 6.5f, 4f, .2f, .4f, .4f, 6f });
        Set(volume, "soilSurface", supportObject.transform);
        Set(volume, "constructionSite", site);
        Set(volume, "soilRenderer", supportRenderer);
        Set(volume, "loosenedSoilMaterial", looseMaterial);
        Set(volume, "surfaceStartLocalY", .02f);
        Set(volume, "useLooseSoilHeightField", true);
        Set(volume, "looseSoilField", field);
        Set(volume, "soilSupportCollider", support);
        Set(volume, "concreteFootprintSize", new Vector2(6.5f, 4f));
        Set(site, "excavationVolume", volume);
        bridgeRoot.SetActive(true);
        siteType.GetMethod("Initialize", Instance).Invoke(site, null);
        Type stageType = Type.GetType("BridgeConstructionStage, Assembly-CSharp");
        Type substageType = Type.GetType("FoundationDiggingSubstage, Assembly-CSharp");
        object digging = Enum.Parse(stageType, "Digging");
        object soilRemoval = Enum.Parse(substageType, "SoilRemoval");
        Set(site, "currentStage", digging);
        Set(site, "diggingSubstage", soilRemoval);
        object bridgeDefinition = componentType.GetMethod("GetBridgeComponentSO").Invoke(component, null);
        object workflow = bridgeDefinition.GetType().GetField("constructionWorkflow").GetValue(bridgeDefinition);
        volumeType.GetMethod("ApplyDiggingState", Instance).Invoke(volume,
            new object[] { digging, soilRemoval, 0f, 0f, workflow, 0 });
        return bridgeRoot;
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Instance).SetValue(target, value);

    private static T Read<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, Instance);
        if (field != null) return (T)field.GetValue(target);
        return (T)target.GetType().GetProperty(name, Instance).GetValue(target);
    }
}
