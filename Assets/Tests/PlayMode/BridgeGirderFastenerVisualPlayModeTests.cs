using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RageQuitting.Tests.PlayMode
{
    public sealed class BridgeGirderFastenerVisualPlayModeTests
    {
        [UnityTest]
        public IEnumerator ChangedProgress_AnimatesAndFinishesAtExactTarget()
        {
            GameObject parent = CreateController(out Component controller, out Transform visual);
            float originalCaptureDeltaTime = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                Invoke(controller, true, 0f, true);
                Invoke(controller, true, 1f, true);
                Assert.That(visual.localPosition.y, Is.LessThan(0.285f).And.GreaterThan(-0.020f));

                for (int i = 0; i < 8; i++)
                    yield return null;
                Assert.That(visual.localPosition.y, Is.EqualTo(-0.020f).Within(0.000001f));
            }
            finally
            {
                Time.captureDeltaTime = originalCaptureDeltaTime;
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [UnityTest]
        public IEnumerator IdenticalProgress_DoesNotRestartActiveTransition()
        {
            GameObject parent = CreateController(out Component controller, out Transform visual);
            float originalCaptureDeltaTime = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                Invoke(controller, true, 0f, true);
                Invoke(controller, true, 1f, true);
                for (int i = 0; i < 4; i++)
                    yield return null;
                Invoke(controller, true, 1f, true);
                for (int i = 0; i < 4; i++)
                    yield return null;
                Assert.That(visual.localPosition.y, Is.EqualTo(-0.020f).Within(0.000001f));
            }
            finally
            {
                Time.captureDeltaTime = originalCaptureDeltaTime;
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [UnityTest]
        public IEnumerator FirstVisibleNonzeroState_SnapsForLoadOrLateJoin()
        {
            GameObject parent = CreateController(out Component controller, out Transform visual);
            try
            {
                Invoke(controller, false, 0f, true);
                Invoke(controller, true, 0.5f, true);
                Assert.That(visual.gameObject.activeSelf, Is.True);
                Assert.That(visual.localPosition.y, Is.EqualTo(0.1325f).Within(0.000001f));
                yield return null;
                Assert.That(visual.localPosition.y, Is.EqualTo(0.1325f).Within(0.000001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static GameObject CreateController(out Component controller, out Transform visual)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Single(assembly => assembly.GetName().Name == "Assembly-CSharp")
                .GetTypes().Single(candidate => candidate.Name == "BridgeGirderFastenerVisual");
            var parent = new GameObject("FastenerVisualPlayModeTest");
            var visualObject = new GameObject("Visual");
            visualObject.transform.SetParent(parent.transform, false);
            visual = visualObject.transform;
            controller = parent.AddComponent(type);
            type.GetField("visualRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(controller, visual);
            return parent;
        }

        private static void Invoke(Component controller, bool visible, float progress, bool animate)
        {
            controller.GetType().GetMethod("SetState", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(controller, new object[] { visible, progress, animate });
        }
    }
}
