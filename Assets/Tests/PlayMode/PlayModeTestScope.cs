using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Owns scene objects and shared engine state created or changed by one PlayMode test.</summary>
public sealed class PlayModeTestScope
{
    private readonly List<GameObject> roots = new();
    private readonly List<NetworkManager> managers = new();
    private readonly List<Object> transientAssets = new();
    private readonly List<Scene> additionalOwnedScenes = new();
    private readonly List<Action> restorations = new();
    private readonly List<Exception> failures = new();
    private readonly Scene previousActiveScene;
    private readonly Scene ownedScene;
    private readonly NetworkManager previousNetworkManager;
    private readonly string scopeName;
    private readonly float previousTimeScale = Time.timeScale;
    private readonly float previousFixedDeltaTime = Time.fixedDeltaTime;
    private readonly float previousMaximumDeltaTime = Time.maximumDeltaTime;
    private readonly Vector3 previousGravity = Physics.gravity;
    private readonly bool previousAutoSyncTransforms = Physics.autoSyncTransforms;
    private readonly SimulationMode previousSimulationMode = Physics.simulationMode;
    private bool cleaned;

    public PlayModeTestScope(string name)
    {
        scopeName = name;
        previousActiveScene = SceneManager.GetActiveScene();
        previousNetworkManager = NetworkManager.Singleton;
        ownedScene = SceneManager.CreateScene("Test_" + name + "_" + Guid.NewGuid().ToString("N"));
        SceneManager.SetActiveScene(ownedScene);
    }

    public IReadOnlyList<Exception> CleanupFailures => failures;
    public Scene OwnedScene => ownedScene;

    public GameObject Own(GameObject root)
    {
        if (root != null && !roots.Contains(root)) roots.Add(root);
        return root;
    }

    public Scene OwnScene(Scene scene)
    {
        if (scene.IsValid() && scene != ownedScene && !additionalOwnedScenes.Contains(scene))
            additionalOwnedScenes.Add(scene);
        return scene;
    }

    public NetworkManager OwnManager(NetworkManager manager)
    {
        if (manager != null)
        {
            if (!managers.Contains(manager)) managers.Add(manager);
            Own(manager.gameObject);
        }
        return manager;
    }

    public T OwnTransientAsset<T>(T asset) where T : Object
    {
        if (asset != null && !transientAssets.Contains(asset)) transientAssets.Add(asset);
        return asset;
    }

    public void RestoreWith(Action restoration)
    {
        if (restoration != null) restorations.Add(restoration);
    }

    public IEnumerator Cleanup(float shutdownTimeoutSeconds = 12f)
    {
        if (cleaned) yield break;
        cleaned = true;

        var networkObjects = new HashSet<GameObject>();
        if (managers.Count > 0)
        {
            FieldInfo ownerField = typeof(NetworkObject).GetField("NetworkManagerOwner", BindingFlags.Instance | BindingFlags.NonPublic);
            if (ownerField == null)
                failures.Add(new MissingFieldException(typeof(NetworkObject).FullName, "NetworkManagerOwner"));
            else
            {
                foreach (NetworkObject networkObject in Resources.FindObjectsOfTypeAll<NetworkObject>())
                {
                    Try(() =>
                    {
                        if (networkObject == null) return;
                        Scene objectScene = networkObject.gameObject.scene;
                        if (!objectScene.IsValid() || !objectScene.isLoaded) return;
                        var owner = ownerField.GetValue(networkObject) as NetworkManager;
                        if (ReferenceEquals(owner, null)) return;
                        foreach (NetworkManager manager in managers)
                        {
                            if (!ReferenceEquals(owner, manager)) continue;
                            networkObjects.Add(networkObject.gameObject);
                            break;
                        }
                    }, "inspect NetworkObject ownership");
                }
            }
        }

        foreach (NetworkManager manager in managers)
        {
            Try(() =>
            {
                if (manager == null || manager.SpawnManager == null) return;
                foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjectsList)
                    if (networkObject != null) networkObjects.Add(networkObject.gameObject);
                foreach (NetworkObject networkObject in manager.GetComponentsInChildren<NetworkObject>(true))
                    if (networkObject != null) networkObjects.Add(networkObject.gameObject);
            }, "capture spawned replicas");
        }

        foreach (NetworkManager manager in managers)
        {
            Try(() =>
            {
                if (manager != null && (manager.IsListening || manager.ShutdownInProgress)) manager.Shutdown(true);
            }, "shutdown " + (manager == null ? "destroyed manager" : manager.name));
        }

        float deadline = Time.realtimeSinceStartup + Mathf.Max(0f, shutdownTimeoutSeconds);
        while (Time.realtimeSinceStartup < deadline && HasPendingShutdown()) yield return null;
        if (HasPendingShutdown())
            failures.Add(new TimeoutException("Timed out waiting for all owned NGO managers to finish shutdown: " + PendingManagers()));

        var scheduledForDestruction = new HashSet<GameObject>();
        foreach (GameObject replica in networkObjects)
            if (scheduledForDestruction.Add(replica)) DestroyOwned(replica, "spawned replica");
        foreach (GameObject root in roots)
            if (scheduledForDestruction.Add(root)) DestroyOwned(root, "owned root");
        foreach (Object asset in transientAssets) DestroyOwnedAsset(asset);
        yield return null;

        if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            Try(() => SceneManager.SetActiveScene(previousActiveScene), "restore active scene");
        else
            failures.Add(new InvalidOperationException("Scope '" + scopeName + "' cannot restore the prior active scene because it is no longer loaded."));
        if (ownedScene.IsValid() && ownedScene.isLoaded)
        {
            yield return UnloadOwnedScene(ownedScene, shutdownTimeoutSeconds);
        }
        foreach (Scene scene in additionalOwnedScenes)
            if (scene.IsValid() && scene.isLoaded) yield return UnloadOwnedScene(scene, shutdownTimeoutSeconds);

        foreach (GameObject replica in networkObjects)
            if (replica != null) failures.Add(new InvalidOperationException("Scope '" + scopeName + "' left a spawned replica: " + replica.name));
        foreach (GameObject root in roots)
            if (root != null) failures.Add(new InvalidOperationException("Scope '" + scopeName + "' left an owned root: " + root.name));
        foreach (Object asset in transientAssets)
            if (asset != null) failures.Add(new InvalidOperationException("Scope '" + scopeName + "' left a transient asset: " + asset.name));
        foreach (NetworkManager manager in managers)
            if (manager != null && (manager.IsListening || manager.ShutdownInProgress))
                failures.Add(new InvalidOperationException("Scope '" + scopeName + "' left an active NGO manager: " + manager.name));
        if (ownedScene.IsValid() && ownedScene.isLoaded)
            failures.Add(new InvalidOperationException("Scope '" + scopeName + "' left its owned scene loaded: " + ownedScene.name));
        foreach (Scene scene in additionalOwnedScenes)
            if (scene.IsValid() && scene.isLoaded)
                failures.Add(new InvalidOperationException("Scope '" + scopeName + "' left an additional owned scene loaded: " + scene.name));
        if (previousActiveScene.IsValid() && previousActiveScene.isLoaded && SceneManager.GetActiveScene() != previousActiveScene)
            failures.Add(new InvalidOperationException("Scope '" + scopeName + "' did not restore the active scene."));

        foreach (Action restoration in restorations) Try(restoration, "registered state restoration");
        Try(() =>
        {
            if (previousNetworkManager != null) previousNetworkManager.SetSingleton();
            else
            {
                var reset = typeof(NetworkManager).GetMethod("ResetSingleton", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (reset == null) throw new MissingMethodException(typeof(NetworkManager).FullName, "ResetSingleton");
                reset.Invoke(null, null);
            }
            if (!ReferenceEquals(NetworkManager.Singleton, previousNetworkManager))
                throw new InvalidOperationException("The captured NetworkManager.Singleton value was not restored.");
        }, "restore NetworkManager.Singleton");
        Try(() => Time.timeScale = previousTimeScale, "restore Time.timeScale");
        Try(() => Time.fixedDeltaTime = previousFixedDeltaTime, "restore Time.fixedDeltaTime");
        Try(() => Time.maximumDeltaTime = previousMaximumDeltaTime, "restore Time.maximumDeltaTime");
        Try(() => Physics.gravity = previousGravity, "restore Physics.gravity");
        Try(() => Physics.autoSyncTransforms = previousAutoSyncTransforms, "restore Physics.autoSyncTransforms");
        Try(() => Physics.simulationMode = previousSimulationMode, "restore Physics.simulationMode");
    }

    public void AssertCleanupSucceeded()
    {
        if (failures.Count == 0) return;
        throw new AggregateException("PlayMode scope '" + scopeName + "' cleanup failed:\n" + string.Join("\n", failures), failures);
    }

    private bool HasPendingShutdown()
    {
        foreach (NetworkManager manager in managers)
            if (manager != null && (manager.IsListening || manager.ShutdownInProgress)) return true;
        return false;
    }

    private IEnumerator UnloadOwnedScene(Scene scene, float timeoutSeconds)
    {
        AsyncOperation unload = null;
        Try(() => unload = SceneManager.UnloadSceneAsync(scene), "begin unload of owned scene " + scene.name);
        float unloadDeadline = Time.realtimeSinceStartup + Mathf.Max(0f, timeoutSeconds);
        while (unload != null && !unload.isDone && Time.realtimeSinceStartup < unloadDeadline) yield return null;
        if (unload != null && !unload.isDone)
            failures.Add(new TimeoutException("Timed out unloading owned scene for scope '" + scopeName + "': " + scene.name));
    }

    private string PendingManagers()
    {
        var names = new List<string>();
        foreach (NetworkManager manager in managers)
            if (manager != null && (manager.IsListening || manager.ShutdownInProgress))
                names.Add(manager.name + "(listening=" + manager.IsListening + ",shutdown=" + manager.ShutdownInProgress + ")");
        return string.Join(", ", names);
    }

    private void DestroyOwned(GameObject instance, string label)
    {
        if (instance == null) return;
        Try(() => Object.Destroy(instance), "destroy " + label + " " + instance.name);
    }

    private void DestroyOwnedAsset(Object asset)
    {
        if (asset == null) return;
        Try(() => Object.Destroy(asset), "destroy transient asset " + asset.name);
    }

    private void Try(Action action, string label)
    {
        try { action(); }
        catch (Exception exception) { failures.Add(new InvalidOperationException(label, exception)); }
    }
}
