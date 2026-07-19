using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerLevelRestartController : NetworkBehaviour
{
    private static bool restartInProgress;
    private static string restartSceneName;

    public bool CanRequestRestart
    {
        get
        {
            if (!IsGameSceneActive())
            {
                return false;
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
            {
                return ShouldRunAsLocalPlayer();
            }

            return IsOwner && networkManager.IsHost && !restartInProgress;
        }
    }

    public void RequestRestartLevel()
    {
        if (!CanRequestRestart)
        {
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        string sceneName = SceneManager.GetActiveScene().name;
        if (networkManager == null || !networkManager.IsListening)
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            return;
        }

        RequestRestartLevelServerRpc();
    }

    [ServerRpc]
    private void RequestRestartLevelServerRpc(ServerRpcParams rpcParams = default)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (!IsServer || networkManager == null || restartInProgress || !IsGameSceneActive())
        {
            return;
        }

        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        string sceneName = SceneManager.GetActiveScene().name;
        if (!GameplaySceneRegistry.IsGameplayScene(sceneName))
        {
            return;
        }

        restartInProgress = true;
        restartSceneName = sceneName;
        SceneManager.sceneLoaded += ResetRestartStateAfterSceneLoad;

        DespawnRuntimeObjectsInGameScene(networkManager, sceneName);
        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            SceneManager.sceneLoaded -= ResetRestartStateAfterSceneLoad;
            restartInProgress = false;
            restartSceneName = null;
            Debug.LogWarning($"PlayerLevelRestartController: Could not restart {sceneName}. Netcode status: {status}.");
        }
    }

    private static void DespawnRuntimeObjectsInGameScene(NetworkManager networkManager, string sceneName)
    {
        List<NetworkObject> objectsToDespawn = networkManager.SpawnManager.SpawnedObjectsList
            .Where(networkObject => networkObject != null
                && networkObject.IsSpawned
                && networkObject.IsSceneObject != true
                && networkObject.gameObject.scene.name == sceneName)
            .ToList();

        foreach (NetworkObject networkObject in objectsToDespawn)
        {
            networkObject.Despawn(true);
        }
    }

    private static void ResetRestartStateAfterSceneLoad(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (scene.name != restartSceneName)
        {
            return;
        }

        SceneManager.sceneLoaded -= ResetRestartStateAfterSceneLoad;
        restartInProgress = false;
        restartSceneName = null;
    }

    private static bool IsGameSceneActive()
    {
        return GameplaySceneRegistry.IsGameplayScene(SceneManager.GetActiveScene().name);
    }

    private bool ShouldRunAsLocalPlayer()
    {
        if (IsSpawned)
        {
            return IsOwner;
        }

        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
    }
}
