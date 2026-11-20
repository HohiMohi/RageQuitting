using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class BridgeDeckPanelStage4EditModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

    [Test]
    public void CompletingCurrentStageAdvancesAcrossPreviouslyCompletedStagesAndRaisesFinalEventOnce()
    {
        Type managerType = Type.GetType("GameplayManager, Assembly-CSharp");
        Assert.That(managerType, Is.Not.Null);
        GameObject root = new GameObject("Stage 4 progression manager");
        Component manager = root.AddComponent(managerType);
        FieldInfo instance = managerType.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previousInstance = instance.GetValue(null);
        instance.SetValue(null, manager);
        try
        {
            Array data = CreateComponentData(managerType, new[] { false, true, true });
            Set(managerType, manager, "bridgeComponentDataArray", data);
            Set(managerType, manager, "bridgeBuildingStages", CreateStages(managerType, new[] { 0, 1, 2 }));
            Set(managerType, manager, "currentBridgeBuildingStageIndex", 0);

            int completionEvents = 0;
            managerType.GetEvent("OnBridgeFullyAssembled", PublicInstance)
                .AddEventHandler(manager, new EventHandler((_, __) => completionEvents++));
            MethodInfo checkProgress = managerType.GetMethod("CheckCurrentStageMountingProgress", PrivateInstance);

            checkProgress.Invoke(manager, null);

            Assert.That(managerType.GetProperty("CurrentBridgeStageIndex", PublicInstance).GetValue(manager), Is.EqualTo(0),
                "Future completed stages remain recorded while the current stage is incomplete.");
            Assert.That(managerType.GetProperty("IsFullyAssembled", PublicInstance).GetValue(manager), Is.False);
            Assert.That(completionEvents, Is.Zero);

            SetDataFlag(data, 0, "isMounted", true);
            SetDataFlag(data, 0, "isAssembled", true);
            Set(managerType, manager, "bridgeComponentDataArray", data);
            checkProgress.Invoke(manager, null);
            checkProgress.Invoke(manager, null);

            Assert.That(managerType.GetProperty("CurrentBridgeStageIndex", PublicInstance).GetValue(manager), Is.EqualTo(3),
                "The manager advances through every already-completed stage.");
            Assert.That(managerType.GetProperty("IsFullyAssembled", PublicInstance).GetValue(manager), Is.True);
            Assert.That(completionEvents, Is.EqualTo(1));
        }
        finally
        {
            instance.SetValue(null, previousInstance);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static Array CreateComponentData(Type managerType, bool[] completed)
    {
        Type dataType = managerType.GetField("bridgeComponentDataArray", PrivateInstance).FieldType.GetElementType();
        Array data = Array.CreateInstance(dataType, completed.Length);
        for (int i = 0; i < completed.Length; i++)
        {
            object value = Activator.CreateInstance(dataType);
            dataType.GetField("isMounted").SetValue(value, completed[i]);
            dataType.GetField("isAssembled").SetValue(value, completed[i]);
            data.SetValue(value, i);
        }
        return data;
    }

    private static Array CreateStages(Type managerType, int[] componentIndexes)
    {
        Type stageType = managerType.GetField("bridgeBuildingStages", PrivateInstance).FieldType.GetElementType();
        Array stages = Array.CreateInstance(stageType, componentIndexes.Length);
        for (int i = 0; i < componentIndexes.Length; i++)
        {
            object stage = Activator.CreateInstance(stageType);
            stageType.GetField("bridgeComponentDataIndexes").SetValue(stage, new[] { componentIndexes[i] });
            stages.SetValue(stage, i);
        }
        return stages;
    }

    private static void SetDataFlag(Array data, int index, string fieldName, bool value)
    {
        object item = data.GetValue(index);
        item.GetType().GetField(fieldName).SetValue(item, value);
        data.SetValue(item, index);
    }

    private static void Set(Type type, object target, string fieldName, object value)
        => type.GetField(fieldName, PrivateInstance).SetValue(target, value);
}
