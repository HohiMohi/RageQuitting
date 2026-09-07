using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RageQuitting.Tests.PlayMode
{
    [Explicit("Run only through the TutorialBoot gameplay scenario.")]
    public sealed class TutorialSceneGameplayScenarioTests
    {
        private const string TutorialScenePath = "Assets/Scenes/Tutorial_scene.unity";
        private const float LoadTimeoutSeconds = 45f;
        private const string BridgeStatusPrefix =
            "Received BridgeComponentMountableStatusUpdate event in BridgeComponent with componentID:";
        private const string ThreadedCaptureWarning =
            "AgentHarness threaded log capture regression warning 4C9857D2.";

        private static readonly ReadOnlyCollection<string> ExpectedInformationalLogs = Array.AsReadOnly(new[]
        {
            "WoodenAbutment (BridgeComponentSO)",
            "WoodenCrossBeam (BridgeComponentSO)",
            "WoodenDeckPanel (BridgeComponentSO)",
            "WoodenDiagonalBracing (BridgeComponentSO)",
            "WoodenFoundation (BridgeComponentSO)",
            "WoodenMainGirder (BridgeComponentSO)"
        });

        [UnityTest]
        [Explicit("Run only through the TutorialBoot gameplay scenario.")]
        public IEnumerator TutorialBoot_LoadsStableSceneWithRequiredGameplayComponents()
        {
            var unexpectedLogs = new ConcurrentQueue<string>();
            var callbackGate = new object();
            bool captureEnabled = true;
            Application.LogCallback captureLog = (condition, stackTrace, type) =>
            {
                lock (callbackGate)
                {
                    if (!captureEnabled || IsExpectedInformationalLog(condition, type))
                        return;

                    unexpectedLogs.Enqueue(
                        $"[{type}] {condition ?? string.Empty}\n{stackTrace ?? string.Empty}");
                }
            };
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            bool callbackSubscribed = false;

            try
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceivedThreaded += captureLog;
                callbackSubscribed = true;

                Assert.That(SceneUtility.GetBuildIndexByScenePath(TutorialScenePath), Is.GreaterThanOrEqualTo(0),
                    $"{TutorialScenePath} is not present in Build Settings.");

                AsyncOperation load = SceneManager.LoadSceneAsync(TutorialScenePath, LoadSceneMode.Single);
                Assert.That(load, Is.Not.Null, $"Unity did not start loading {TutorialScenePath}.");

                float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
                while (!load.isDone && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(load.isDone, Is.True,
                    $"Loading {TutorialScenePath} exceeded {LoadTimeoutSeconds:0} seconds.");

                yield return null;
                yield return null;
                yield return null;

                Scene scene = SceneManager.GetActiveScene();
                Assert.That(scene.IsValid() && scene.isLoaded, Is.True, "The active scene is not valid and loaded.");
                Assert.That(scene.path, Is.EqualTo(TutorialScenePath));

                MonoBehaviour[] behaviours = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                    .Where(component => component != null && component.gameObject.scene == scene)
                    .ToArray();

                AssertTypeCount(behaviours, "WheelbarrowController", 1, 1);
                AssertTypeCount(behaviours, "BridgeConstructionSite", 2, int.MaxValue);
                AssertTypeCount(behaviours, "FoundationExcavationVolume", 2, int.MaxValue);
            }
            finally
            {
                if (callbackSubscribed)
                    Application.logMessageReceivedThreaded -= captureLog;
                lock (callbackGate)
                    captureEnabled = false;
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }

            string[] unexpectedLogSnapshot = unexpectedLogs.ToArray();
            Assert.That(unexpectedLogSnapshot, Is.Empty,
                "Unexpected logs while loading Tutorial_scene:\n" + string.Join("\n", unexpectedLogSnapshot));
        }

        [UnityTest]
        [Explicit("Run only to verify worker-thread log capture in the gameplay scenario harness.")]
        public IEnumerator ThreadedLogCapture_CapturesWorkerThreadWarning()
        {
            var capturedLogs = new ConcurrentQueue<string>();
            var callbackGate = new object();
            bool captureEnabled = true;
            Application.LogCallback captureLog = (condition, stackTrace, type) =>
            {
                lock (callbackGate)
                {
                    if (!captureEnabled || type != LogType.Warning ||
                        !string.Equals(condition ?? string.Empty, ThreadedCaptureWarning, StringComparison.Ordinal))
                        return;

                    capturedLogs.Enqueue(
                        $"[{type}] {condition ?? string.Empty}\n{stackTrace ?? string.Empty}");
                }
            };
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            bool callbackSubscribed = false;
            Task logTask = null;

            try
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceivedThreaded += captureLog;
                callbackSubscribed = true;

                logTask = Task.Run(() => Debug.LogWarning(ThreadedCaptureWarning));
                float deadline = Time.realtimeSinceStartup + 10f;
                while (!logTask.IsCompleted && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(logTask.IsCompleted, Is.True, "Worker-thread log task timed out.");
                Assert.That(logTask.IsFaulted, Is.False,
                    logTask.Exception == null ? "Worker-thread log task faulted." : logTask.Exception.ToString());
                yield return null;
            }
            finally
            {
                if (callbackSubscribed)
                    Application.logMessageReceivedThreaded -= captureLog;
                lock (callbackGate)
                    captureEnabled = false;
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }

            string[] capturedLogSnapshot = capturedLogs.ToArray();
            Assert.That(capturedLogSnapshot.Length, Is.EqualTo(1),
                "The threaded callback did not capture exactly one worker-thread warning. Captured:\n" +
                string.Join("\n", capturedLogSnapshot));
        }

        private static bool IsExpectedInformationalLog(string condition, LogType type)
        {
            if (type != LogType.Log)
                return false;

            string safeCondition = condition ?? string.Empty;
            return ExpectedInformationalLogs.Any(expected =>
                       string.Equals(expected, safeCondition, StringComparison.Ordinal)) ||
                   safeCondition.StartsWith(BridgeStatusPrefix, StringComparison.Ordinal);
        }

        private static void AssertTypeCount(MonoBehaviour[] behaviours, string typeName, int minimum, int maximum)
        {
            int count = behaviours.Count(component => component.GetType().Name == typeName);
            Assert.That(count, Is.InRange(minimum, maximum),
                $"Expected {FormatRange(minimum, maximum)} {typeName} components in {TutorialScenePath}, found {count}.");
        }

        private static string FormatRange(int minimum, int maximum)
        {
            return minimum == maximum ? $"exactly {minimum}" : $"at least {minimum}";
        }
    }
}
