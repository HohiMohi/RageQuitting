using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RageQuitting.Tests.Editor
{
    public sealed class BridgeGirderFastenerVisualEditModeTests
    {
        private const string LoosePrefabPath =
            "Assets/Prefabs/New/BridgeComponents/WoodenMainGirderMountable.prefab";
        private const string MountedPrefabPath =
            "Assets/Prefabs/New/BridgeComponents/WoodenMainGirderBridgeComponent.prefab";

        private static readonly Vector3[] ExpectedFastenerPositions =
        {
            new Vector3(-6.65f, 0.35f, -0.18f),
            new Vector3(-6.65f, 0.35f, 0.18f),
            new Vector3(6.65f, 0.35f, -0.18f),
            new Vector3(6.65f, 0.35f, 0.18f)
        };

        [Test]
        public void LoosePrefab_AllFourNailVisualsAreInactive()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(LoosePrefabPath);
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    Transform visual = root.transform.Find($"GirderVisual/Fastener {i + 1}/Visual");
                    Assert.That(visual, Is.Not.Null, $"Missing loose fastener visual {i + 1}.");
                    Assert.That(visual.gameObject.activeSelf, Is.False);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void MountedPrefab_FastenersPreserveIntegrationAndHaveOneVisualController()
        {
            Type workPointType = RuntimeType("BridgeGirderWorkPoint");
            Type visualType = RuntimeType("BridgeGirderFastenerVisual");
            GameObject root = PrefabUtility.LoadPrefabContents(MountedPrefabPath);
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    Transform parent = root.transform.Find($"Fastener {i + 1}");
                    Assert.That(parent, Is.Not.Null, $"Missing mounted fastener {i + 1}.");
                    Assert.That((parent.localPosition - ExpectedFastenerPositions[i]).sqrMagnitude, Is.LessThan(0.00000001f));
                    Assert.That((parent.localScale - Vector3.one).sqrMagnitude, Is.LessThan(0.00000001f));
                    Assert.That(parent.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(1));
                    Assert.That(parent.GetComponents(visualType).Length, Is.EqualTo(1));

                    Component workPoint = parent.GetComponent(workPointType);
                    Assert.That(workPoint, Is.Not.Null);
                    object id = workPointType.GetProperty("WorkPointId")?.GetValue(workPoint);
                    Assert.That(Convert.ToInt32(id), Is.EqualTo(10 + i));

                    Transform visual = parent.Find("Visual");
                    Assert.That(visual, Is.Not.Null);
                    Assert.That(visual.gameObject.activeSelf, Is.False);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [TestCase(-1f, 0.285f)]
        [TestCase(0f, 0.285f)]
        [TestCase(0.5f, 0.1325f)]
        [TestCase(1f, -0.020f)]
        [TestCase(2f, -0.020f)]
        public void VisualPosition_IsExactAndClamped(float progress, float expectedY)
        {
            GameObject parent = new GameObject("FastenerVisualEditModeTest");
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(parent.transform, false);
            try
            {
                Component controller = parent.AddComponent(RuntimeType("BridgeGirderFastenerVisual"));
                SetField(controller, "visualRoot", visual.transform);
                Invoke(controller, "SetState", true, progress, false);
                Assert.That(visual.activeSelf, Is.True);
                Assert.That(visual.transform.localPosition.y, Is.EqualTo(expectedY).Within(0.000001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ConstructionVisualState_HidesBeforeFasteningAndKeepsCompletedNailsNonInteractive()
        {
            Type siteType = RuntimeType("BridgeGirderConstructionSite");
            Type workPointType = RuntimeType("BridgeGirderWorkPoint");
            GameObject root = PrefabUtility.LoadPrefabContents(MountedPrefabPath);
            try
            {
                Component site = root.GetComponent(siteType);
                Assert.That(site, Is.Not.Null);
                Component[] points = root.GetComponentsInChildren(workPointType, true);
                Array typedPoints = Array.CreateInstance(workPointType, points.Length);
                Array.Copy(points, typedPoints, points.Length);
                SetField(site, "workPoints", typedPoints);

                SetEnumField(site, "currentStage", "Leveling");
                InvokeNonPublic(site, "ApplyVisualState");
                AssertFastenerState(root, workPointType, false, false, false, false, 0.285f);

                SetEnumField(site, "currentStage", "Fastening");
                InvokeNonPublic(site, "ApplyVisualState");
                AssertFastenerState(root, workPointType, true, true, true, true, 0.285f);

                SetEnumField(site, "currentStage", "Complete");
                InvokeNonPublic(site, "ApplyVisualState");
                AssertFastenerState(root, workPointType, true, false, false, true, -0.020f);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssertFastenerState(GameObject root, Type workPointType, bool parentActive,
            bool workPointEnabled, bool collidersEnabled, bool visualActive, float expectedY)
        {
            for (int i = 0; i < 4; i++)
            {
                Transform parent = root.transform.Find($"Fastener {i + 1}");
                Transform visual = parent.Find("Visual");
                Behaviour workPoint = (Behaviour)parent.GetComponent(workPointType);
                Assert.That(parent.gameObject.activeSelf, Is.EqualTo(parentActive));
                Assert.That(workPoint.enabled, Is.EqualTo(workPointEnabled));
                Assert.That(parent.GetComponentsInChildren<Collider>(true).All(c => c.enabled == collidersEnabled), Is.True);
                Assert.That(visual.gameObject.activeSelf, Is.EqualTo(visualActive));
                if (visualActive)
                    Assert.That(visual.localPosition.y, Is.EqualTo(expectedY).Within(0.000001f));
            }
        }

        private static Type RuntimeType(string name)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Single(assembly => assembly.GetName().Name == "Assembly-CSharp")
                .GetTypes().Single(candidate => candidate.Name == name);
            Assert.That(type, Is.Not.Null);
            return type;
        }

        private static void SetEnumField(object target, string fieldName, string value)
        {
            FieldInfo field = FindField(target.GetType(), fieldName);
            field.SetValue(target, Enum.Parse(field.FieldType, value));
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FindField(target.GetType(), fieldName).SetValue(target, value);
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) return field;
                type = type.BaseType;
            }
            throw new MissingFieldException(fieldName);
        }

        private static object Invoke(object target, string methodName, params object[] arguments)
        {
            return target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(target, arguments);
        }

        private static object InvokeNonPublic(object target, string methodName)
        {
            return target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);
        }
    }
}
