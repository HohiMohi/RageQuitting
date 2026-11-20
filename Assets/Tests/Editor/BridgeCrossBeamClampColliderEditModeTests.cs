using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class BridgeCrossBeamClampColliderEditModeTests
{
    private const string PrefabPath = "Assets/Prefabs/New/BridgeComponents/WoodenCrossBeamBridgeComponent.prefab";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void PrefabExcludesOnlyClampCollidersAfterInitialCompletedAwakeAndSnapshots()
    {
        System.Type bridgeType = System.Type.GetType("BridgeComponent, Assembly-CSharp");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        Component source = prefab.GetComponent(bridgeType);
        SerializedObject serialized = new(source);
        SerializedProperty excluded = serialized.FindProperty("disabledAfterAssemblyColliders");
        Assert.That(excluded.arraySize, Is.EqualTo(2));

        Transform colliderRoot = prefab.transform.Find("CrossBeamClampColliders");
        Collider[] expected = colliderRoot.GetComponentsInChildren<Collider>(true);
        Collider[] configured = new Collider[excluded.arraySize];
        for (int i = 0; i < excluded.arraySize; i++)
            configured[i] = excluded.GetArrayElementAtIndex(i).objectReferenceValue as Collider;
        Assert.That(configured, Is.EquivalentTo(expected), "Every collider under the clamp root must be listed exactly once.");

        Collider mainBeam = source.GetComponent<Collider>();
        Assert.That(mainBeam, Is.Not.Null);
        Assert.That(System.Array.IndexOf(configured, mainBeam), Is.EqualTo(-1));

        // LoadPrefabContents creates an isolated prefab stage instead of adding a
        // temporary object to whichever production scene happens to be open.
        GameObject instance = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Component bridge = instance.GetComponent(bridgeType);
            SerializedObject instanceData = new(bridge);
            instanceData.FindProperty("startFullyCompleted").boolValue = true;
            instanceData.ApplyModifiedPropertiesWithoutUndo();

            Collider instanceMainBeam = bridge.GetComponent<Collider>();
            Collider[] instanceClamps = new Collider[configured.Length];
            for (int i = 0; i < configured.Length; i++)
            {
                string path = AnimationUtility.CalculateTransformPath(configured[i].transform, prefab.transform);
                instanceClamps[i] = instance.transform.Find(path).GetComponent<Collider>();
                instanceClamps[i].enabled = true; // Models the enabled collider overrides authored on production scene instances.
            }

            bridgeType.GetMethod("Awake", PrivateInstance).Invoke(bridge, null);
            AssertCompletedColliderState(bridge, bridgeType, instanceMainBeam, instanceClamps);

            bridgeType.GetMethod("RefreshVisualAndColliderState").Invoke(bridge, null);
            bridgeType.GetMethod("RefreshVisualAndColliderState").Invoke(bridge, null);
            AssertCompletedColliderState(bridge, bridgeType, instanceMainBeam, instanceClamps);

            int componentId = (int)bridgeType.GetProperty("ComponentID").GetValue(bridge);
            object snapshot = CreateCompletedSnapshot(componentId);
            bridgeType.GetMethod("ApplyNetworkState").Invoke(bridge, new[] { snapshot });
            bridgeType.GetMethod("ApplyNetworkState").Invoke(bridge, new[] { snapshot });
            AssertCompletedColliderState(bridge, bridgeType, instanceMainBeam, instanceClamps);
        }
        finally { PrefabUtility.UnloadPrefabContents(instance); }
    }

    private static object CreateCompletedSnapshot(int componentId)
    {
        System.Type stateType = System.Type.GetType("BridgeComponentNetworkState, Assembly-CSharp");
        object state = System.Activator.CreateInstance(stateType, new object[] { componentId });
        stateType.GetField("isMounted").SetValue(state, true);
        stateType.GetField("isAssembled").SetValue(state, true);
        stateType.GetField("canBeMounted").SetValue(state, false);
        stateType.GetField("constructionStage").SetValue(state, (int)System.Enum.Parse(
            System.Type.GetType("BridgeConstructionStage, Assembly-CSharp"), "Complete"));
        return state;
    }

    private static void AssertCompletedColliderState(Component bridge, System.Type bridgeType, Collider mainBeam, Collider[] clamps)
    {
        Assert.That((bool)bridgeType.GetProperty("IsAssembled").GetValue(bridge), Is.True);
        Assert.That(mainBeam.enabled, Is.True);
        foreach (Collider clamp in clamps) Assert.That(clamp.enabled, Is.False);
        GameObject finalVisual = new SerializedObject(bridge)
            .FindProperty("mountedComponentVisualsGameObject").objectReferenceValue as GameObject;
        Assert.That(finalVisual, Is.Not.Null);
        Assert.That(finalVisual.activeSelf, Is.True);
    }
}
