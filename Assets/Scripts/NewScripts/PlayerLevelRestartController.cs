using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerLevelRestartController : NetworkBehaviour
{
    private const string GameSceneName = "FPP_scene";

    private static bool restartInProgress;

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
        if (networkManager == null || !networkManager.IsListening)
        {
            SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
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

        restartInProgress = true;
        SceneManager.sceneLoaded += ResetRestartStateAfterSceneLoad;

        DespawnRuntimeObjectsInGameScene(networkManager);
        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            SceneManager.sceneLoaded -= ResetRestartStateAfterSceneLoad;
            restartInProgress = false;
            Debug.LogWarning($"PlayerLevelRestartController: Could not restart {GameSceneName}. Netcode status: {status}.");
        }
    }

    private static void DespawnRuntimeObjectsInGameScene(NetworkManager networkManager)
    {
        List<NetworkObject> objectsToDespawn = networkManager.SpawnManager.SpawnedObjectsList
            .Where(networkObject => networkObject != null
                && networkObject.IsSpawned
                && networkObject.IsSceneObject != true
                && networkObject.gameObject.scene.name == GameSceneName)
            .ToList();

        foreach (NetworkObject networkObject in objectsToDespawn)
        {
            networkObject.Despawn(true);
        }
    }

    private static void ResetRestartStateAfterSceneLoad(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (scene.name != GameSceneName)
        {
            return;
        }

        SceneManager.sceneLoaded -= ResetRestartStateAfterSceneLoad;
        restartInProgress = false;
    }

    private static bool IsGameSceneActive()
    {
        return SceneManager.GetActiveScene().name == GameSceneName;
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
