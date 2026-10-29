using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class PlayModeTestScopePlayModeTests
{
    private GameObject regressionPriorRoot;
    private NetworkManager regressionPriorManager;
    private NetworkManager regressionOriginalSingleton;
    private SubstanceFractionalUnitsNetworkPlayModeTests regressionFixture;
    private GameObject unownedSentinel;
    private GameObject regressionDetachedObject;
    private readonly List<GameObject> sentinels = new();
    private bool hasRegressionOriginalSingleton;
    private readonly List<GameObject> regressionOwnedObjects = new();
    private PlayModeTestScope outstandingScope;

    [UnityTearDown]
    public IEnumerator CleanupRegressionPriorManager()
    {
        var failures = new List<Exception>();
        if (outstandingScope != null)
        {
            PlayModeTestScope scope = outstandingScope;
            yield return scope.Cleanup();
            failures.AddRange(scope.CleanupFailures);
            outstandingScope = null;
        }
        if (regressionFixture != null)
        {
            PlayModeTestScope scope = (PlayModeTestScope)typeof(SubstanceFractionalUnitsNetworkPlayModeTests)
                .GetField("scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(regressionFixture);
            if (scope != null)
            {
                yield return scope.Cleanup();
                failures.AddRange(scope.CleanupFailures);
            }
            regressionFixture = null;
        }
        if (hasRegressionOriginalSingleton)
        {
            if (regressionOriginalSingleton != null) regressionOriginalSingleton.SetSingleton();
            else
            {
                MethodInfo reset = typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.Static | BindingFlags.NonPublic);
                reset?.Invoke(null, null);
            }
        }
        if (regressionPriorManager != null)
        {
            Object.Destroy(regressionPriorRoot);
        }
        foreach (GameObject sentinel in sentinels)
            if (sentinel != null) Object.Destroy(sentinel);
        sentinels.Clear();
        regressionPriorManager = null;
        regressionPriorRoot = null;
        if (unownedSentinel != null) Object.Destroy(unownedSentinel);
        unownedSentinel = null;
        if (regressionDetachedObject != null) Object.Destroy(regressionDetachedObject);
        regressionDetachedObject = null;
        regressionOwnedObjects.Clear();
        yield return null;
        if (hasRegressionOriginalSingleton && !ReferenceEquals(NetworkManager.Singleton, regressionOriginalSingleton))
            failures.Add(new InvalidOperationException("The test did not restore the original NetworkManager.Singleton."));
        hasRegressionOriginalSingleton = false;
        if (failures.Count > 0) throw new AggregateException("PlayMode test wrapper cleanup failed.", failures);
    }

    [UnityTest]
    public IEnumerator FractionalFixtureRestoresAnInactivePriorSingleton()
    {
        regressionOriginalSingleton = NetworkManager.Singleton;
        hasRegressionOriginalSingleton = true;
        Scene originalScene = SceneManager.GetActiveScene();
        unownedSentinel = new GameObject("Unowned fractional fixture sentinel");
        unownedSentinel.AddComponent<NetworkObject>();
        regressionPriorRoot = new GameObject("Inactive prior NetworkManager");
        regressionPriorManager = regressionPriorRoot.AddComponent<NetworkManager>();
        regressionPriorManager.SetSingleton();

        regressionFixture = new SubstanceFractionalUnitsNetworkPlayModeTests();
        yield return regressionFixture.SetUp();
        yield return regressionFixture.HostBucketAndPileFractionsReplicateAndClientsCannotMutateIncludingLateJoin();
        foreach (string name in new[] { "host", "owner", "late" })
        {
            var manager = (NetworkManager)typeof(SubstanceFractionalUnitsNetworkPlayModeTests)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(regressionFixture);
            regressionOwnedObjects.Add(manager.gameObject);
            foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjectsList)
                regressionOwnedObjects.Add(networkObject.gameObject);
        }
        var hostManager = (NetworkManager)typeof(SubstanceFractionalUnitsNetworkPlayModeTests)
            .GetField("host", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(regressionFixture);
        regressionDetachedObject = new GameObject("Inactive detached owned DDOL NetworkObject");
        NetworkObject detachedNetworkObject = regressionDetachedObject.AddComponent<NetworkObject>();
        Object.DontDestroyOnLoad(regressionDetachedObject);
        regressionDetachedObject.SetActive(false);
        Assert.That(regressionDetachedObject.scene.IsValid() && regressionDetachedObject.scene.isLoaded, Is.True,
            "The detached probe must reside in a valid loaded DDOL scene.");
        FieldInfo ownerField = typeof(NetworkObject).GetField("NetworkManagerOwner", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(ownerField, Is.Not.Null, "The installed NGO NetworkObject owner field must remain available for identity-based cleanup.");
        ownerField.SetValue(detachedNetworkObject, hostManager);
        bool isSpawned = false;
        foreach (NetworkObject item in hostManager.SpawnManager.SpawnedObjectsList)
            if (ReferenceEquals(item, detachedNetworkObject)) isSpawned = true;
        Assert.That(isSpawned, Is.False);
        regressionOwnedObjects.Add(regressionDetachedObject);
        yield return regressionFixture.TearDown();
        regressionFixture = null;
        yield return null;

        Assert.That(regressionPriorManager.IsListening, Is.False);
        Assert.That(NetworkManager.Singleton, Is.SameAs(regressionPriorManager),
            "The fractional fixture must restore the prior singleton even when that manager was inactive.");
        var survivingOwnedObjects = new List<string>();
        foreach (GameObject item in regressionOwnedObjects)
            if (item != null) survivingOwnedObjects.Add(item.name);
        Assert.That(survivingOwnedObjects, Is.Empty,
            "Every owned peer root and captured network replica must be destroyed. Survived: " + string.Join(", ", survivingOwnedObjects));
        Assert.That(unownedSentinel, Is.Not.Null, "Cleanup must preserve an unrelated object.");
        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(originalScene), "Cleanup must restore the active scene.");
    }

    [UnityTest]
    public IEnumerator CleanupPreservesUnownedStateAndIsIdempotent()
    {
        Scene before = SceneManager.GetActiveScene();
        var sentinel = new GameObject("Unowned scope sentinel");
        sentinels.Add(sentinel);
        float timeScale = Time.timeScale;
        outstandingScope = new PlayModeTestScope("ownership");
        PlayModeTestScope scope = outstandingScope;
        GameObject owned = scope.Own(new GameObject("Owned scope root"));
        Time.timeScale = timeScale * 0.5f;
        yield return scope.Cleanup();
        yield return null;
        yield return scope.Cleanup();

        Assert.That(owned == null, Is.True);
        Assert.That(sentinel != null, Is.True);
        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(before));
        Assert.That(Time.timeScale, Is.EqualTo(timeScale));
        outstandingScope.AssertCleanupSucceeded();
        outstandingScope = null;
        Object.Destroy(sentinel);
    }

    [UnityTest]
    public IEnumerator CleanupUnloadsExplicitAdditionalSceneAndPreservesPriorScene()
    {
        Scene priorScene = SceneManager.GetActiveScene();
        outstandingScope = new PlayModeTestScope("additional scene ownership");
        PlayModeTestScope scope = outstandingScope;
        Scene extraScene = scope.OwnScene(SceneManager.CreateScene("OwnedExtra_" + Guid.NewGuid().ToString("N")));
        GameObject extraRoot = new GameObject("Extra scene root");
        SceneManager.MoveGameObjectToScene(extraRoot, extraScene);

        yield return scope.Cleanup();
        yield return null;

        Assert.That(SceneManager.GetSceneByName(extraScene.name).isLoaded, Is.False);
        Assert.That(priorScene.isLoaded, Is.True);
        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(priorScene));
        Assert.That(extraRoot == null, Is.True);
        scope.AssertCleanupSucceeded();
        outstandingScope = null;
    }

    [UnityTest]
    public IEnumerator CleanupFindsDetachedReplicaOwnedByDestroyedManager()
    {
        regressionOriginalSingleton = NetworkManager.Singleton;
        hasRegressionOriginalSingleton = true;
        outstandingScope = new PlayModeTestScope("destroyed manager replica ownership");
        PlayModeTestScope scope = outstandingScope;
        GameObject managerRoot = new GameObject("Inactive owned NetworkManager");
        managerRoot.SetActive(false);
        NetworkManager manager = managerRoot.AddComponent<NetworkManager>();
        scope.OwnManager(manager);
        manager.SetSingleton();

        regressionDetachedObject = new GameObject("Detached DDOL replica owned by destroyed manager");
        NetworkObject detached = regressionDetachedObject.AddComponent<NetworkObject>();
        Object.DontDestroyOnLoad(regressionDetachedObject);
        regressionDetachedObject.SetActive(false);
        FieldInfo ownerField = typeof(NetworkObject).GetField("NetworkManagerOwner", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(ownerField, Is.Not.Null);
        ownerField.SetValue(detached, manager);
        Object.Destroy(managerRoot);
        yield return null;

        var capturedOwner = ownerField.GetValue(detached) as NetworkManager;
        Assert.That(capturedOwner == null, Is.True, "The manager wrapper must compare Unity-null after destruction.");
        Assert.That(ReferenceEquals(capturedOwner, null), Is.False,
            "The NetworkObject retains the explicit managed owner identity after its Unity object is destroyed.");
        yield return scope.Cleanup();
        yield return null;

        Assert.That(regressionDetachedObject == null, Is.True,
            "Cleanup must match the explicit owner identity even after the owned manager is destroyed.");
        scope.AssertCleanupSucceeded();
        outstandingScope = null;
        regressionDetachedObject = null;
        hasRegressionOriginalSingleton = false;
    }

    [UnityTest]
    public IEnumerator CleanupContinuesAfterRestorationFailureAndReportsIt()
    {
        outstandingScope = new PlayModeTestScope("restoration failure");
        GameObject owned = outstandingScope.Own(new GameObject("Still cleaned after callback failure"));
        bool laterRestorationRan = false;
        outstandingScope.RestoreWith(() => throw new InvalidOperationException("injected cleanup failure"));
        outstandingScope.RestoreWith(() => laterRestorationRan = true);
        PlayModeTestScope scope = outstandingScope;

        yield return scope.Cleanup();
        yield return null;
        outstandingScope = null;

        Assert.That(owned == null, Is.True);
        Assert.That(laterRestorationRan, Is.True);
        Assert.That(scope.CleanupFailures, Has.Count.EqualTo(1));
        Assert.That(scope.CleanupFailures[0].ToString(), Does.Contain("injected cleanup failure"));
        Assert.Throws<AggregateException>(() => scope.AssertCleanupSucceeded());
    }

    [UnityTest]
    public IEnumerator StateRestorationIsStableForAtoB_BtoA_AndAtoA()
    {
        float originalTimeScale = Time.timeScale;
        float originalFixedDelta = Time.fixedDeltaTime;
        Vector3 originalGravity = Physics.gravity;
        bool originalAutoSyncTransforms = Physics.autoSyncTransforms;
        SimulationMode originalSimulationMode = Physics.simulationMode;
        foreach (string[] sequence in new[] { new[] { "A", "B" }, new[] { "B", "A" }, new[] { "A", "A" } })
        {
            foreach (string testState in sequence)
            {
                outstandingScope = new PlayModeTestScope("state " + testState);
                Time.timeScale = testState == "A" ? 0.37f : 0.73f;
                Time.fixedDeltaTime = testState == "A" ? 0.013f : 0.027f;
                Physics.gravity = testState == "A" ? new Vector3(0f, -5f, 0f) : new Vector3(0f, -15f, 0f);
                Physics.autoSyncTransforms = !originalAutoSyncTransforms;
                Physics.simulationMode = SimulationMode.Script;
                PlayModeTestScope scope = outstandingScope;
                yield return scope.Cleanup();
                outstandingScope = null;
                scope.AssertCleanupSucceeded();
                Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale));
                Assert.That(Time.fixedDeltaTime, Is.EqualTo(originalFixedDelta));
                Assert.That(Physics.gravity, Is.EqualTo(originalGravity));
                Assert.That(Physics.autoSyncTransforms, Is.EqualTo(originalAutoSyncTransforms));
                Assert.That(Physics.simulationMode, Is.EqualTo(originalSimulationMode));
            }
        }
    }

    [UnityTest]
    public IEnumerator InputSettingsRestoreRunsAfterOwnedDeviceCleanupCallbackFails()
    {
        outstandingScope = new PlayModeTestScope("input restoration after device failure");
        PlayModeTestScope scope = outstandingScope;
        InputSettings originalSettings = InputSystem.settings;
        var inputSettingsScope = new CrowbarInputSettingsScope();
        scope.RestoreWith(() => throw new InvalidOperationException("injected test-device removal failure"));
        scope.RestoreWith(inputSettingsScope.Restore);
        inputSettingsScope.UseFocusedGameViewRouting();
        Assert.That(InputSystem.settings, Is.Not.SameAs(originalSettings));

        yield return scope.Cleanup();
        outstandingScope = null;

        Assert.That(InputSystem.settings, Is.SameAs(originalSettings),
            "Input settings restoration must run even when removing the test-owned input device reports an error.");
        Assert.That(scope.CleanupFailures, Has.Count.EqualTo(1));
        Assert.That(scope.CleanupFailures[0].ToString(), Does.Contain("injected test-device removal failure"));
        Assert.Throws<AggregateException>(() => scope.AssertCleanupSucceeded());
    }
}
