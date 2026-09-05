using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RageQuitting.Tests.Editor
{
    public sealed class AgentHarnessBasicEditModeTests
    {
        private static readonly string[] PassengerProbeObjectNames =
        {
            "PassengerTransportLifecycleProbe",
            "DestroyedTransportProbe",
            "ReplacementTransportProbe"
        };

        [Test]
        public void QuickValidatorsSuite_Run_ExceptionIsFailed()
        {
            object result = InvokeProbe("QuickValidatorsSuite", "Run", false, "failing-check",
                (Func<string>)(() => throw new Exception("ordinary failure")));
            Assert.That(ReadResultField(result, "status"), Is.EqualTo("failed"));
            Assert.That((string)ReadResultField(result, "message"), Does.Contain("ordinary failure"));
            Assert.That((double)ReadResultField(result, "durationMs"), Is.GreaterThanOrEqualTo(0d));
        }

        [Test]
        public void QuickValidatorsSuite_Run_ExplicitBlockedIsBlocked()
        {
            object result = InvokeProbe("QuickValidatorsSuite", "Run", false, "blocked-check",
                (Func<string>)(() => throw new InvalidOperationException("BLOCKED: test precondition")));
            Assert.That(ReadResultField(result, "status"), Is.EqualTo("blocked"));
            Assert.That((string)ReadResultField(result, "message"), Does.Contain("BLOCKED: test precondition"));
        }

        [Test]
        public void QuickValidatorsSuite_Run_FailureDoesNotPreventFollowingCheck()
        {
            var executed = new List<string>();
            // Exercise the same per-check boundary as the production suite without
            // changing its two registrations or adding a public injection API.
            object[] results =
            {
                InvokeProbe("QuickValidatorsSuite", "Run", false, "first",
                    (Func<string>)(() =>
                    {
                        executed.Add("first");
                        throw new Exception("first check failed");
                    })),
                InvokeProbe("QuickValidatorsSuite", "Run", false, "second",
                    (Func<string>)(() =>
                    {
                        executed.Add("second");
                        return "second check passed";
                    }))
            };
            Assert.That(executed, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(results.Select(result => (string)ReadResultField(result, "status")),
                Is.EqualTo(new[] { "failed", "passed" }));
            Assert.That(ReadResultField(results[1], "message"), Is.EqualTo("second check passed"));
        }

        [Test]
        public void QuickValidatorsSuite_Validate_PassesAndPreservesSceneSetup()
        {
            Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt).ToArray();
            bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
            bool[] loaded = scenes.Select(scene => scene.isLoaded).ToArray();
            string[] paths = scenes.Select(scene => scene.path).ToArray();
            Scene active = SceneManager.GetActiveScene();
            int previews = EditorSceneManager.previewSceneCount;
            try
            {
                object result = InvokeProbe("QuickValidatorsSuite", "Validate", true);
                Assert.That(ReadResultField(result, "status"), Is.EqualTo("passed"));
                Assert.That(ReadResultField(result, "total"), Is.EqualTo(2));
                Assert.That(ReadResultField(result, "passed"), Is.EqualTo(2));
                Assert.That(ReadResultField(result, "failed"), Is.EqualTo(0));
                Assert.That(ReadResultField(result, "blocked"), Is.EqualTo(0));
                object[] checks = ((Array)ReadResultField(result, "checks")).Cast<object>().ToArray();
                Assert.That(checks.Select(check => (string)ReadResultField(check, "name")),
                    Is.EquivalentTo(new[] { "FoundationConcreteFailureProbe", "PlayerConcreteTrapProbe" }));
                foreach (object check in checks)
                {
                    Assert.That(ReadResultField(check, "status"), Is.EqualTo("passed"),
                        (string)ReadResultField(check, "message"));
                    Assert.That((string)ReadResultField(check, "message"), Is.Not.Empty);
                    Assert.That((double)ReadResultField(check, "durationMs"), Is.GreaterThanOrEqualTo(0d));
                }
                Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previews));
                Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes.Length));
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
                for (int i = 0; i < scenes.Length; i++)
                {
                    Scene current = SceneManager.GetSceneAt(i);
                    Assert.That(current, Is.EqualTo(scenes[i]));
                    Assert.That(current.path, Is.EqualTo(paths[i]));
                    Assert.That(current.isLoaded, Is.EqualTo(loaded[i]));
                    Assert.That(current.isDirty, Is.EqualTo(dirty[i]));
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                // The probe owns preview cleanup in its own finally. Only discard
                // unexpected new regular scenes; never reload/save user scenes.
                for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    Scene current = SceneManager.GetSceneAt(i);
                    if (!scenes.Contains(current))
                        EditorSceneManager.CloseScene(current, true);
                }
                if (active.IsValid() && active.isLoaded && SceneManager.GetActiveScene() != active)
                    SceneManager.SetActiveScene(active);
            }
        }

        [Test]
        public void FoundationConcreteFailureProbe_Validate_PreservesSceneSetup()
        {
            Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt).ToArray();
            bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
            bool[] loaded = scenes.Select(scene => scene.isLoaded).ToArray();
            string[] paths = scenes.Select(scene => scene.path).ToArray();
            Scene active = SceneManager.GetActiveScene();
            int previews = EditorSceneManager.previewSceneCount;
            try
            {
                object result = InvokeProbe("FoundationConcreteFailureProbe", "Validate", true);
                Assert.That(result, Is.EqualTo(
                    "Foundation concrete failure probe passed for both Tutorial_scene foundations."));
                Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previews));
                Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes.Length));
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
                for (int i = 0; i < scenes.Length; i++)
                {
                    Scene current = SceneManager.GetSceneAt(i);
                    Assert.That(current, Is.EqualTo(scenes[i]));
                    Assert.That(current.path, Is.EqualTo(paths[i]));
                    Assert.That(current.isLoaded, Is.EqualTo(loaded[i]));
                    Assert.That(current.isDirty, Is.EqualTo(dirty[i]));
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                // The probe owns preview cleanup in its own finally. Only discard
                // unexpected new regular scenes; never reload/save user scenes.
                for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    Scene current = SceneManager.GetSceneAt(i);
                    if (!scenes.Contains(current))
                        EditorSceneManager.CloseScene(current, true);
                }
                if (active.IsValid() && active.isLoaded && SceneManager.GetActiveScene() != active)
                    SceneManager.SetActiveScene(active);
            }
        }

        [Test]
        public void PlayerConcreteTrapProbe_ValidateFromMenu_Passes()
        {
            LogAssert.Expect(LogType.Log,
                "Player concrete trap probe passed: activation orders, pause reasons, collapse/tip, " +
                "carry eligibility, required tool/fallback, pourable gates and prefab wiring.");
            object result = InvokeProbe("PlayerConcreteTrapProbe", "ValidateFromMenu", true);
            Assert.That(result, Is.TypeOf<string>());
            Assert.That((string)result, Is.Not.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void WheelbarrowPhysicsProbe_RopeAttachmentAndPullProfile_Passes()
        {
            LogAssert.Expect(LogType.Log,
                new Regex("^" + Regex.Escape("[WheelbarrowPhysicsProbe] Rope attachment/pull PASS:")));
            InvokeProbe("WheelbarrowPhysicsProbe", "RunRopeAttachmentAndPullProfile", false);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void WheelbarrowPhysicsProbe_PassengerTransportLifecycle_Passes()
        {
            try
            {
                LogAssert.Expect(LogType.Log,
                    new Regex("^" + Regex.Escape(
                        "[WheelbarrowPhysicsProbe] Passenger transport lifecycle PASS:")));
                InvokeProbe("WheelbarrowPhysicsProbe", "RunPassengerTransportLifecycle", false);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                DestroyProbeObjects();
            }
        }

        private static object ReadResultField(object result, string name)
        {
            Assert.That(result, Is.Not.Null);
            FieldInfo field = result.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Missing result field: {name}");
            return field.GetValue(result);
        }

        private static object InvokeProbe(string typeName, string methodName, bool isPublic, params object[] arguments)
        {
            Assembly editorAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .SingleOrDefault(assembly => assembly.GetName().Name == "Assembly-CSharp-Editor");
            Assert.That(editorAssembly, Is.Not.Null, "Assembly-CSharp-Editor is not loaded.");

            Type[] matchingTypes = GetLoadableTypes(editorAssembly)
                .Where(type => type.Name == typeName)
                .ToArray();
            Assert.That(matchingTypes.Length, Is.EqualTo(1),
                $"Expected exactly one '{typeName}' in Assembly-CSharp-Editor, found {matchingTypes.Length}.");

            BindingFlags visibility = isPublic ? BindingFlags.Public : BindingFlags.NonPublic;
            MethodInfo[] matchingMethods = matchingTypes[0]
                .GetMethods(BindingFlags.Static | visibility)
                .Where(method => method.Name == methodName && method.GetParameters().Length == arguments.Length)
                .ToArray();
            Assert.That(matchingMethods.Length, Is.EqualTo(1),
                $"Expected exactly one static '{typeName}.{methodName}' with {arguments.Length} arguments, found {matchingMethods.Length}.");

            try
            {
                return matchingMethods[0].Invoke(null, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }

        private static void DestroyProbeObjects()
        {
            foreach (string objectName in PassengerProbeObjectNames)
            {
                foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>()
                             .Where(candidate => candidate != null && candidate.name == objectName))
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
        }
    }
}
