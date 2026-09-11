using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RageQuitting.Tests.PlayMode
{
    public sealed class PlayerFirstPersonArmsCameraStackPlayModeTests
    {
        private readonly List<GameObject> createdObjects = new();
        private readonly List<(GameObject gameObject, string tag)> retaggedObjects = new();

        [UnityTest]
        public IEnumerator CameraStack_RecreationMainCameraReplacementAndTeardown_RestoreAllState()
        {
            Type armsType = FindLoadedType("PlayerFirstPersonArms");
            Type cameraDataType = FindLoadedType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            int postProcessingLayer = LayerMask.NameToLayer("PostProcessing");
            Assert.That(postProcessingLayer, Is.GreaterThanOrEqualTo(0), "PostProcessing layer must exist.");

            foreach (Camera existing in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (existing.CompareTag("MainCamera"))
                {
                    retaggedObjects.Add((existing.gameObject, existing.tag));
                    existing.tag = "Untagged";
                }
            }

            Camera firstBase = CreateCamera("CameraStackTestBase1", cameraDataType, "MainCamera");
            Camera secondBase = CreateCamera("CameraStackTestBase2", cameraDataType, "Untagged");
            int firstOriginalMask = (1 << 0) | (1 << 8) | (1 << 30);
            int secondOriginalMask = (1 << 0) | (1 << 9) | (1 << 30);
            ConfigureBase(firstBase, cameraDataType, firstOriginalMask, true, "FastApproximateAntialiasing", "Medium");
            ConfigureBase(secondBase, cameraDataType, secondOriginalMask, false, "TemporalAntiAliasing", "Low");

            GameObject player = new GameObject("PlayerFirstPersonArmsCameraStackTest");
            createdObjects.Add(player);
            Component arms = player.AddComponent(armsType);
            MethodInfo ensure = armsType.GetMethod("EnsureFirstPersonCamera", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo destroyOverlay = armsType.GetMethod("DestroyFirstPersonCamera", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(ensure, Is.Not.Null);
            Assert.That(destroyOverlay, Is.Not.Null);

            ensure.Invoke(arms, null);
            AssertConfigured(firstBase, cameraDataType, firstOriginalMask, postProcessingLayer);

            ensure.Invoke(arms, null);
            AssertConfigured(firstBase, cameraDataType, firstOriginalMask, postProcessingLayer);

            destroyOverlay.Invoke(arms, null);
            AssertRestored(firstBase, cameraDataType, firstOriginalMask, true, "FastApproximateAntialiasing", "Medium");
            yield return null;
            ensure.Invoke(arms, null);
            AssertConfigured(firstBase, cameraDataType, firstOriginalMask, postProcessingLayer);

            firstBase.tag = "Untagged";
            secondBase.tag = "MainCamera";
            ensure.Invoke(arms, null);
            AssertRestored(firstBase, cameraDataType, firstOriginalMask, true, "FastApproximateAntialiasing", "Medium");
            AssertConfigured(secondBase, cameraDataType, secondOriginalMask, postProcessingLayer);

            ((Behaviour)arms).enabled = false;
            AssertRestored(secondBase, cameraDataType, secondOriginalMask, false, "TemporalAntiAliasing", "Low");
            yield return null;

            ((Behaviour)arms).enabled = true;
            ensure.Invoke(arms, null);
            AssertConfigured(secondBase, cameraDataType, secondOriginalMask, postProcessingLayer);
            UnityEngine.Object.Destroy(player);
            yield return null;
            AssertRestored(secondBase, cameraDataType, secondOriginalMask, false, "TemporalAntiAliasing", "Low");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var item in retaggedObjects)
            {
                if (item.gameObject != null)
                {
                    item.gameObject.tag = item.tag;
                }
            }

            foreach (GameObject createdObject in createdObjects)
            {
                if (createdObject != null)
                {
                    UnityEngine.Object.Destroy(createdObject);
                }
            }

            retaggedObjects.Clear();
            createdObjects.Clear();
            yield return null;
        }

        private Camera CreateCamera(string name, Type cameraDataType, string tag)
        {
            var gameObject = new GameObject(name);
            createdObjects.Add(gameObject);
            gameObject.tag = tag;
            Camera camera = gameObject.AddComponent<Camera>();
            gameObject.AddComponent(cameraDataType);
            return camera;
        }

        private static void ConfigureBase(Camera camera, Type cameraDataType, int cullingMask, bool postProcessing, string antialiasing, string quality)
        {
            camera.cullingMask = cullingMask;
            Component data = camera.GetComponent(cameraDataType);
            SetProperty(data, "renderPostProcessing", postProcessing);
            SetEnumProperty(data, "antialiasing", antialiasing);
            SetEnumProperty(data, "antialiasingQuality", quality);
        }

        private static void AssertConfigured(Camera baseCamera, Type cameraDataType, int originalMask, int postProcessingLayer)
        {
            Component baseData = baseCamera.GetComponent(cameraDataType);
            Assert.That(baseCamera.cullingMask, Is.EqualTo(originalMask & ~(1 << 30)));
            Assert.That(ReadProperty<bool>(baseData, "renderPostProcessing"), Is.False);
            Assert.That(ReadProperty<object>(baseData, "antialiasing").ToString(), Is.EqualTo("None"));

            var stack = (IEnumerable<Camera>)baseData.GetType().GetProperty("cameraStack").GetValue(baseData);
            Camera[] overlays = stack.Where(camera => camera != null).ToArray();
            Assert.That(overlays, Has.Length.EqualTo(1));
            Camera overlay = overlays[0];
            Assert.That(overlay.cullingMask, Is.EqualTo(1 << 30));
            Component overlayData = overlay.GetComponent(cameraDataType);
            Assert.That(ReadProperty<object>(overlayData, "renderType").ToString(), Is.EqualTo("Overlay"));
            Assert.That(ReadProperty<bool>(overlayData, "renderPostProcessing"), Is.True);
            Assert.That(ReadProperty<object>(overlayData, "antialiasing").ToString(), Is.EqualTo("SubpixelMorphologicalAntiAliasing"));
            Assert.That(ReadProperty<object>(overlayData, "antialiasingQuality").ToString(), Is.EqualTo("High"));
            var volumeMask = (LayerMask)ReadProperty<object>(overlayData, "volumeLayerMask");
            Assert.That((volumeMask.value & (1 << postProcessingLayer)) != 0, Is.True);
        }

        private static void AssertRestored(Camera camera, Type cameraDataType, int cullingMask, bool postProcessing, string antialiasing, string quality)
        {
            Component data = camera.GetComponent(cameraDataType);
            Assert.That(camera.cullingMask, Is.EqualTo(cullingMask));
            Assert.That(ReadProperty<bool>(data, "renderPostProcessing"), Is.EqualTo(postProcessing));
            Assert.That(ReadProperty<object>(data, "antialiasing").ToString(), Is.EqualTo(antialiasing));
            Assert.That(ReadProperty<object>(data, "antialiasingQuality").ToString(), Is.EqualTo(quality));
            var stack = (IEnumerable<Camera>)data.GetType().GetProperty("cameraStack").GetValue(data);
            Assert.That(stack.Count(cameraInStack => cameraInStack != null), Is.Zero);
        }

        private static void SetProperty(object target, string propertyName, object value)
        {
            target.GetType().GetProperty(propertyName).SetValue(target, value);
        }

        private static void SetEnumProperty(object target, string propertyName, string enumName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName);
            property.SetValue(target, Enum.Parse(property.PropertyType, enumName));
        }

        private static T ReadProperty<T>(object target, string propertyName)
        {
            return (T)target.GetType().GetProperty(propertyName).GetValue(target);
        }

        private static Type FindLoadedType(string fullName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, "Required loaded type was not found: " + fullName);
            return type;
        }
    }
}
