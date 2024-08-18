using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SpawningTest : MonoBehaviour
{
    public GameObject playerPrefab;
    public int numberOfPlayers = 4;

    // Array with control scheme declaration -> use types specified in Input Action Asset
    public string[]  controlSchemes;

    // Array with used devices -> playerPrefab will be spawned only for active devices
    public bool[] activeDevices;

    private void OnEnable()
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
                    break;

                case "Arrows":
                    temp = PlayerInput.Instantiate(playerPrefab, playerIndex: i, controlScheme: controlSchemes[i]);
                    // Set current keyboard as used device -> enable playing as 2 players on one keyboard
                    temp.SwitchCurrentControlScheme("Arrows", Keyboard.current);
                    break;

                default:
                    break;
            }
        }

    }
}
