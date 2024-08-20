using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SpawningTest : MonoBehaviour
{
    public GameObject playerPrefab;
    public int numberOfPlayers = 4;
    public BuildingMaterialDetailsSO buildingMaterialDetailsSO;
    public PoolManager pool;

    #region Header SpawnPlayerTest function variables
    [Space(10)]
    [Header("SpawnPlayerTest function variables")]
    #endregion

    #region Tooltip
    [Tooltip("Array with control scheme declaration -> use types specified in Input Action Asset (SpawnPlayerTest function)")]
    #endregion
    public string[]  controlSchemes;

    #region Tooltip
    [Tooltip("// Array with used devices -> playerPrefab will be spawned only for active devices (SpawnPlayerTest function)")]
    #endregion
    public bool[] activeDevices;

    private void OnEnable()
    {
        // Spawning mechanic for test - uncomment when running TestScene2
        //SpawnPlayersTest();

        //InstantiatePlayersFromPlayerHolder();

    }

    private void Start()
    {
        // Pool Manager test
        IPickable cube = (IPickable)PoolManager.Instance.ReuseComponent(buildingMaterialDetailsSO.buildingMaterialPrefab, gameObject.transform.position, Quaternion.identity);
        cube.InitialiseBuildingMaterial(buildingMaterialDetailsSO.meshFilter, buildingMaterialDetailsSO.material);

    }
    /// <summary>
    /// Instnantiate players from info collected in JoinMenuTest scene
    /// </summary>
    public void InstantiatePlayersFromPlayerHolder()
    {
        PlayerHolder playerHolder = GameObject.Find("PlayerHolder").GetComponent<PlayerHolder>();

        foreach (PlayerInformationHolder playerInfo in playerHolder.playerInformationArray)
        {
            if (playerInfo.inputDevice == null) continue;

            PlayerInput.Instantiate(playerPrefab, playerIndex: playerInfo.playerID, controlScheme: playerInfo.controlScheme, pairWithDevice: playerInfo.inputDevice);
        }

    }

    /// <summary>
    /// Test player spawn function
    /// </summary>
    public void SpawnPlayersTest()
    {
        // Spawning playerPrefabs for active devices
        for (int i = 0; i < numberOfPlayers; i++)
        {
            // Skip inactive devices
            if (activeDevices[i] == false) continue;

            switch (controlSchemes[i])
            {
                case "Gamepad":
                    var temp = PlayerInput.Instantiate(playerPrefab, playerIndex: i, controlScheme: controlSchemes[i], pairWithDevice: Gamepad.all[i]);
                    break;

                case "Keyboard":
                    temp = PlayerInput.Instantiate(playerPrefab, playerIndex: i, controlScheme: controlSchemes[i]);
                    temp.name = "KeyboardPlayer";
                    break;

                case "Arrows":
                    temp = PlayerInput.Instantiate(playerPrefab, playerIndex: i, controlScheme: controlSchemes[i]);
                    // Set current keyboard as used device -> enable playing as 2 players on one keyboard
                    temp.SwitchCurrentControlScheme("Arrows", Keyboard.current);
                    temp.name = "ArrowsPlayer";
                    break;

                default:
                    break;
            }
        }
    }

    public void OnPlayerJoined(PlayerInput playerInput)
    {
        Debug.Log(playerInput.playerIndex);
        if (playerInput.devices.Count > 0) 
            Debug.Log(playerInput.devices[0]);
    }
}
