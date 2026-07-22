using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerSpawnManager : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            return;
        }

        networkManager.OnServerStarted += OnNetworkStarted;
        networkManager.OnClientStarted += OnNetworkStarted;
        networkManager.OnClientConnectedCallback += OnClientConnected;

        if (networkManager.IsListening)
        {
            SubscribeToSceneEvents();
        }
    }

    private void Unsubscribe()
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            return;
        }

        networkManager.OnServerStarted -= OnNetworkStarted;
        networkManager.OnClientStarted -= OnNetworkStarted;
        networkManager.OnClientConnectedCallback -= OnClientConnected;
        UnsubscribeFromSceneEvents();
    }

    private void OnNetworkStarted()
    {
        SubscribeToSceneEvents();
    }

    private void SubscribeToSceneEvents()
    {
        var sceneManager = NetworkManager.Singleton?.SceneManager;
        if (sceneManager == null)
        {
            return;
        }

        sceneManager.OnLoadEventCompleted -= OnSceneLoaded;
        sceneManager.OnLoadEventCompleted += OnSceneLoaded;
    }

    private void UnsubscribeFromSceneEvents()
    {
        var sceneManager = NetworkManager.Singleton?.SceneManager;
        if (sceneManager == null)
        {
            return;
        }

        sceneManager.OnLoadEventCompleted -= OnSceneLoaded;
    }

    private void OnSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!GameplaySceneRegistry.IsGameplayScene(sceneName) || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        SpawnAllConnectedPlayers();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (!GameplaySceneRegistry.IsGameplayScene(SceneManager.GetActiveScene().name))
        {
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject != null)
        {
            return;
        }

        var spawnPoints = FindSpawnPoints();
        if (spawnPoints.Length == 0)
        {
            Debug.LogWarning("PlayerSpawnManager: No spawn points found for late-joining client.");
            return;
        }

        int occupiedSlots = NetworkManager.Singleton.ConnectedClientsList.Count - 1;
        SpawnPlayerForClient(clientId, spawnPoints[occupiedSlots % spawnPoints.Length]);
    }

    private void SpawnAllConnectedPlayers()
    {
        PlayerNetworkSetup.DisableScenePlacedPlayers();

        var spawnPoints = FindSpawnPoints();
        if (spawnPoints.Length == 0)
        {
            Debug.LogWarning("PlayerSpawnManager: No spawn points found in game scene.");
            return;
        }

        int spawnIndex = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                continue;
            }

            SpawnPlayerForClient(client.ClientId, spawnPoints[spawnIndex % spawnPoints.Length]);
            spawnIndex++;
        }
    }

    private void SpawnPlayerForClient(ulong clientId, Transform spawnPoint)
    {
        var player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        var networkObject = player.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError("PlayerSpawnManager: Player prefab is missing a NetworkObject component.");
            Destroy(player);
            return;
        }

        networkObject.SpawnAsPlayerObject(clientId, true);
        if (player.TryGetComponent(out PlayerNetworkSetup networkSetup))
        {
            networkSetup.ConfirmInitialSpawnPlacement(spawnPoint.position, spawnPoint.rotation);
        }
    }

    private static Transform[] FindSpawnPoints()
    {
        return FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None)
            .OrderBy(point => point.SpawnIndex)
            .Select(point => point.transform)
            .ToArray();
    }

    public static Transform GetSpawnPointForClient(ulong clientId)
    {
        var spawnPoints = FindSpawnPoints();
        if (spawnPoints.Length == 0)
        {
            return null;
        }

        int spawnIndex = (int)(clientId % (ulong)spawnPoints.Length);
        return spawnPoints[spawnIndex];
    }
}
