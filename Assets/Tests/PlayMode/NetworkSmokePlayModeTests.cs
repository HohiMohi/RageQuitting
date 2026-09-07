using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RageQuitting.Tests.PlayMode
{
    [Explicit("Run only through the host-only network smoke runner.")]
    public sealed class NetworkSmokePlayModeTests
    {
        private const string MultiplayerStartScenePath = "Assets/Scenes/MultiplayerStartScene.unity";
        private const string TutorialScenePath = "Assets/Scenes/Tutorial_scene.unity";
        private const string TutorialSceneName = "Tutorial_scene";
        private const float OperationTimeoutSeconds = 45f;

        private static readonly ReadOnlyCollection<string> ExpectedDisabledNetworkBehaviourWarnings =
            Array.AsReadOnly(new[]
            {
                "[Netcode] [PlayerNew][ClientNetworkTransform][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!",
                "[Netcode] [PlayerNew][NPCFactionMember][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!",
                "[Netcode] [PlayerNew][PlayerExternalImpulseController][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!",
                "[Netcode] [PlayerNew][PlayerLevelRestartController][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!",
                "[Netcode] [PlayerNew][PlayerStaminaController][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!",
                "[Netcode] [PlayerNew][PlayerWaterExposureController][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!",
                "[Netcode] [PlayerNew][RopeToolController][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!",
                "[Netcode] [PlayerNew][PlayerRopeConstraintController][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!",
                "[Netcode] [PlayerNew][PlayerRopeUI][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!",
                "[Netcode] [PlayerNew][PlayerSpiritLevelController][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!",
                "[Netcode] [PlayerNew][PlayerConcreteTrapController][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!"
            });

        private readonly object callbackGate = new object();
        private ConcurrentQueue<string> unexpectedLogs;
        private bool captureEnabled;
        private bool logStateCaptured;
        private bool logCallbackSubscribed;
        private bool sceneLoadedCallbackSubscribed;
        private bool networkSceneCallbackSubscribed;
        private bool previousIgnoreFailingMessages;
        private NetworkManager networkManager;
        private NetworkSceneManager.OnEventCompletedDelegateHandler networkSceneLoaded;
        private Application.LogCallback captureLog;
        private UnityAction<Scene, LoadSceneMode> disableRoomManager;
        private bool multiplayerStartSceneCallbackObserved;
        private int multiplayerRoomManagerCount;
        private MonoBehaviour disabledMultiplayerRoomManager;

        [UnityTest]
        [Explicit("Run only through the host-only network smoke runner.")]
        public IEnumerator HostLifecycle_StartsLoadsTutorialSpawnsAndRestartsCleanly()
        {
            unexpectedLogs = new ConcurrentQueue<string>();
            captureEnabled = true;
            previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            logStateCaptured = true;
            captureLog = (condition, stackTrace, type) =>
            {
                if (type == LogType.Log || IsExpectedKinematicResourceWarning(condition, stackTrace, type) ||
                    IsExpectedDisabledNetworkBehaviourWarning(condition, stackTrace, type))
                    return;

                lock (callbackGate)
                {
                    if (!captureEnabled)
                        return;

                    unexpectedLogs.Enqueue(
                        $"[{type}] {condition ?? string.Empty}\n{stackTrace ?? string.Empty}");
                }
            };

            disableRoomManager = (scene, _) =>
            {
                if (!string.Equals(scene.path, MultiplayerStartScenePath, StringComparison.Ordinal))
                    return;

                multiplayerStartSceneCallbackObserved = true;
                MonoBehaviour[] managers = FindBehavioursInScene(scene, "MultiplayerRoomManager");
                multiplayerRoomManagerCount = managers.Length;
                foreach (MonoBehaviour manager in managers)
                    manager.enabled = false;
                if (managers.Length == 1)
                    disabledMultiplayerRoomManager = managers[0];
            };

            try
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceivedThreaded += captureLog;
                logCallbackSubscribed = true;

                Assert.That(SceneUtility.GetBuildIndexByScenePath(MultiplayerStartScenePath), Is.GreaterThanOrEqualTo(0),
                    $"{MultiplayerStartScenePath} is not present in Build Settings.");
                Assert.That(SceneUtility.GetBuildIndexByScenePath(TutorialScenePath), Is.GreaterThanOrEqualTo(0),
                    $"{TutorialScenePath} is not present in Build Settings.");

                SetAllBehavioursEnabled("MultiplayerRoomManager", false);
                NetworkManager existingManager = FindSingleActiveComponentOrNull<NetworkManager>();
                if (existingManager != null &&
                    (existingManager.IsListening || existingManager.ShutdownInProgress ||
                     existingManager.IsServer || existingManager.IsClient))
                {
                    existingManager.Shutdown();
                    yield return WaitForCondition(
                        () => existingManager == null ||
                              (!existingManager.IsListening && !existingManager.ShutdownInProgress),
                        "The previous NGO session did not shut down.");
                }

                SceneManager.sceneLoaded += disableRoomManager;
                sceneLoadedCallbackSubscribed = true;
                AsyncOperation loadStartScene = SceneManager.LoadSceneAsync(
                    MultiplayerStartScenePath, LoadSceneMode.Single);
                Assert.That(loadStartScene, Is.Not.Null,
                    $"Unity did not start loading {MultiplayerStartScenePath}.");
                yield return WaitForCondition(() => loadStartScene.isDone,
                    $"Loading {MultiplayerStartScenePath} timed out.");
                yield return null;

                Scene startScene = SceneManager.GetActiveScene();
                Assert.That(startScene.path, Is.EqualTo(MultiplayerStartScenePath));
                Assert.That(startScene.isLoaded, Is.True);
                Assert.That(CountActiveComponents<NetworkManager>(), Is.EqualTo(1),
                    "Expected exactly one active NetworkManager.");
                Assert.That(CountActiveComponents<UnityTransport>(), Is.EqualTo(1),
                    "Expected exactly one active UnityTransport.");
                Assert.That(CountActiveBehaviours("PlayerSpawnManager"), Is.EqualTo(1),
                    "Expected exactly one active PlayerSpawnManager.");
                Assert.That(multiplayerStartSceneCallbackObserved, Is.True,
                    "The sceneLoaded callback did not process MultiplayerStartScene.");
                Assert.That(multiplayerRoomManagerCount, Is.EqualTo(1),
                    "Expected exactly one MultiplayerRoomManager in MultiplayerStartScene.");
                Assert.That(disabledMultiplayerRoomManager, Is.Not.Null,
                    "The expected MultiplayerRoomManager was not retained by sceneLoaded.");
                Assert.That(disabledMultiplayerRoomManager.enabled, Is.False,
                    "MultiplayerRoomManager was not disabled during controlled scene loading.");
                Assert.That(disabledMultiplayerRoomManager.isActiveAndEnabled, Is.False,
                    "MultiplayerRoomManager remained active before the host was started.");

                networkManager = FindSingleActiveComponent<NetworkManager>();
                UnityTransport transport = FindSingleActiveComponent<UnityTransport>();
                ushort port = ReserveFreeUdpPort();
                transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");

                Assert.That(networkManager.StartHost(), Is.True, "NetworkManager.StartHost() returned false.");
                yield return WaitForHostConnected(networkManager, "initial host start");
                AssertHostState(networkManager);

                bool tutorialLoadCompleted = false;
                bool tutorialLoadTimedOutClient = false;
                networkSceneLoaded = (sceneName, mode, clientsCompleted, clientsTimedOut) =>
                {
                    if (!string.Equals(sceneName, TutorialSceneName, StringComparison.Ordinal))
                        return;

                    tutorialLoadCompleted = true;
                    tutorialLoadTimedOutClient = clientsTimedOut != null && clientsTimedOut.Count > 0;
                };
                networkManager.SceneManager.OnLoadEventCompleted += networkSceneLoaded;
                networkSceneCallbackSubscribed = true;

                SceneEventProgressStatus loadStatus = networkManager.SceneManager.LoadScene(
                    TutorialSceneName, LoadSceneMode.Single);
                Assert.That(loadStatus, Is.EqualTo(SceneEventProgressStatus.Started),
                    $"NGO did not start loading {TutorialScenePath}: {loadStatus}.");
                yield return WaitForCondition(
                    () => tutorialLoadCompleted &&
                          string.Equals(SceneManager.GetActiveScene().path, TutorialScenePath,
                              StringComparison.Ordinal),
                    $"NGO did not complete loading {TutorialScenePath}.");
                Assert.That(tutorialLoadTimedOutClient, Is.False,
                    "The local host client timed out during the Tutorial scene load.");
                yield return null;
                yield return null;

                Scene tutorialScene = SceneManager.GetActiveScene();
                Assert.That(tutorialScene.isLoaded, Is.True);
                Assert.That(tutorialScene.path, Is.EqualTo(TutorialScenePath));
                AssertHostState(networkManager);
                yield return WaitForLocalPlayer(networkManager, "Tutorial scene load");
                AssertLocalPlayer(networkManager);

                networkManager.SceneManager.OnLoadEventCompleted -= networkSceneLoaded;
                networkSceneCallbackSubscribed = false;
                networkSceneLoaded = null;

                networkManager.Shutdown();
                yield return null;
                yield return WaitForCondition(
                    () => IsNetworkStopped(networkManager),
                    "The first host shutdown did not complete.");

                Assert.That(networkManager.StartHost(), Is.True,
                    "NetworkManager.StartHost() returned false after restart.");
                yield return WaitForHostConnected(networkManager, "host restart");
                yield return WaitForLocalPlayer(networkManager, "host restart in Tutorial scene");
                AssertHostState(networkManager);
                AssertLocalPlayer(networkManager);

                networkManager.Shutdown();
                yield return null;
                yield return WaitForCondition(
                    () => IsNetworkStopped(networkManager),
                    "The final host shutdown did not complete.");
            }
            finally
            {
                UnsubscribeSceneCallbacks();
                InitiateShutdown();
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            bool shutdownCompleted = true;
            string[] unexpectedLogSnapshot = Array.Empty<string>();
            try
            {
                UnsubscribeSceneCallbacks();
                InitiateShutdown();
                yield return null;

                float deadline = Time.realtimeSinceStartup + OperationTimeoutSeconds;
                while (!IsNetworkStopped(networkManager) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                shutdownCompleted = IsNetworkStopped(networkManager);
            }
            finally
            {
                UnsubscribeSceneCallbacks();
                if (logCallbackSubscribed && captureLog != null)
                {
                    Application.logMessageReceivedThreaded -= captureLog;
                    logCallbackSubscribed = false;
                }

                lock (callbackGate)
                    captureEnabled = false;
                if (logStateCaptured)
                {
                    LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
                    logStateCaptured = false;
                }
                unexpectedLogSnapshot = unexpectedLogs?.ToArray() ?? Array.Empty<string>();
            }

            var cleanupErrors = new List<string>();
            if (!shutdownCompleted)
                cleanupErrors.Add("NetworkManager did not complete shutdown during UnityTearDown.");
            if (unexpectedLogSnapshot.Length > 0)
            {
                cleanupErrors.Add("Unexpected warnings or errors during host-only network smoke:\n" +
                                  string.Join("\n", unexpectedLogSnapshot));
            }
            Assert.That(cleanupErrors, Is.Empty, string.Join("\n", cleanupErrors));
        }

        private static IEnumerator WaitForHostConnected(NetworkManager manager, string phase)
        {
            yield return WaitForCondition(
                () => manager != null && manager.IsListening && manager.IsHost && manager.IsServer &&
                      manager.IsClient && manager.ConnectedClients.Count == 1 &&
                      manager.ConnectedClients.ContainsKey(manager.LocalClientId),
                $"Timed out waiting for {phase}.");
        }

        private static IEnumerator WaitForLocalPlayer(NetworkManager manager, string phase)
        {
            yield return WaitForCondition(
                () => manager != null && manager.LocalClient != null &&
                      manager.LocalClient.PlayerObject != null,
                $"Timed out waiting for the local PlayerObject after {phase}.");
        }

        private static IEnumerator WaitForCondition(Func<bool> condition, string timeoutMessage)
        {
            float deadline = Time.realtimeSinceStartup + OperationTimeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(condition(), Is.True, timeoutMessage);
        }

        private static void AssertHostState(NetworkManager manager)
        {
            Assert.That(manager.IsListening, Is.True);
            Assert.That(manager.IsHost, Is.True);
            Assert.That(manager.IsServer, Is.True);
            Assert.That(manager.IsClient, Is.True);
            Assert.That(manager.ConnectedClients.Count, Is.EqualTo(1));
            Assert.That(manager.ConnectedClients.ContainsKey(manager.LocalClientId), Is.True);
        }

        private static void AssertLocalPlayer(NetworkManager manager)
        {
            NetworkObject playerObject = manager.LocalClient.PlayerObject;
            Assert.That(playerObject, Is.Not.Null);
            Assert.That(playerObject.IsPlayerObject, Is.True);
            Assert.That(playerObject.IsSpawned, Is.True);
            Assert.That(playerObject.OwnerClientId, Is.EqualTo(manager.LocalClientId));
        }

        private void InitiateShutdown()
        {
            if (networkManager == null || networkManager.ShutdownInProgress)
                return;

            if (networkManager.IsListening || networkManager.IsHost ||
                networkManager.IsServer || networkManager.IsClient)
            {
                networkManager.Shutdown();
            }
        }

        private static bool IsNetworkStopped(NetworkManager manager)
        {
            return manager == null ||
                   (!manager.IsListening && !manager.ShutdownInProgress && !manager.IsHost &&
                    !manager.IsServer && !manager.IsClient);
        }

        private void UnsubscribeSceneCallbacks()
        {
            if (networkSceneCallbackSubscribed && networkManager != null &&
                networkManager.SceneManager != null && networkSceneLoaded != null)
            {
                networkManager.SceneManager.OnLoadEventCompleted -= networkSceneLoaded;
                networkSceneCallbackSubscribed = false;
            }

            if (sceneLoadedCallbackSubscribed && disableRoomManager != null)
            {
                SceneManager.sceneLoaded -= disableRoomManager;
                sceneLoadedCallbackSubscribed = false;
            }
        }

        private static ushort ReserveFreeUdpPort()
        {
            using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                return (ushort)((IPEndPoint)probe.Client.LocalEndPoint).Port;
        }

        private static bool IsExpectedKinematicResourceWarning(
            string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Warning || string.IsNullOrEmpty(stackTrace) ||
                stackTrace.IndexOf("BaseResourceNew:ApplySecuredTransportState (bool)",
                    StringComparison.Ordinal) < 0)
            {
                return false;
            }

            string safeCondition = condition ?? string.Empty;
            return string.Equals(safeCondition,
                       "Setting linear velocity of a kinematic body is not supported.",
                       StringComparison.Ordinal) ||
                   string.Equals(safeCondition,
                       "Setting angular velocity of a kinematic body is not supported.",
                       StringComparison.Ordinal);
        }

        private static bool IsExpectedDisabledNetworkBehaviourWarning(
            string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Warning || string.IsNullOrEmpty(stackTrace) ||
                stackTrace.IndexOf("Unity.Netcode.NetworkObject:InvokeBehaviourNetworkSpawn ()",
                    StringComparison.Ordinal) < 0)
            {
                return false;
            }

            string safeCondition = condition ?? string.Empty;
            return ExpectedDisabledNetworkBehaviourWarnings.Any(expected =>
                string.Equals(expected, safeCondition, StringComparison.Ordinal));
        }

        private static int CountActiveComponents<T>() where T : Component
        {
            return UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        }

        private static T FindSingleActiveComponent<T>() where T : Component
        {
            T[] components = UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.That(components, Has.Length.EqualTo(1),
                $"Expected exactly one active {typeof(T).Name}.");
            return components[0];
        }

        private static T FindSingleActiveComponentOrNull<T>() where T : Component
        {
            T[] components = UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.That(components.Length, Is.LessThanOrEqualTo(1),
                $"Expected at most one active {typeof(T).Name} before scene reload.");
            return components.Length == 1 ? components[0] : null;
        }

        private static int CountActiveBehaviours(string typeName)
        {
            return UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Count(behaviour => behaviour != null &&
                                    behaviour.isActiveAndEnabled &&
                                    string.Equals(behaviour.GetType().Name, typeName,
                                        StringComparison.Ordinal));
        }

        private static void SetAllBehavioursEnabled(string typeName, bool enabled)
        {
            foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour != null &&
                    string.Equals(behaviour.GetType().Name, typeName, StringComparison.Ordinal))
                {
                    behaviour.enabled = enabled;
                }
            }
        }

        private static MonoBehaviour[] FindBehavioursInScene(Scene scene, string typeName)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(behaviour => behaviour != null && behaviour.gameObject.scene == scene &&
                                    string.Equals(behaviour.GetType().Name, typeName,
                                        StringComparison.Ordinal))
                .ToArray();
        }
    }
}
