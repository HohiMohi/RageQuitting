using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerHolder : MonoBehaviour
{
    public PlayerInformationHolder[] playerInformationArray;
    private int maxPlayers = 4; //change it later for variable defined in global settings

    private void Awake()
    {
        playerInformationArray = new PlayerInformationHolder[maxPlayers];
        DontDestroyOnLoad(this);

    }

    public void OnPlayerJoined(PlayerInput playerInput)
    {
        playerInformationArray[playerInput.playerIndex] = new PlayerInformationHolder(playerInput.playerIndex, playerInput.currentControlScheme, playerInput.devices[0]);
    }

}



public struct PlayerInformationHolder
{
    public int playerID;
    public string controlScheme;
    public InputDevice inputDevice;

     public PlayerInformationHolder(int playerID, string controlScheme, InputDevice inputDevice)
    {
        this.playerID = playerID;
        this.controlScheme = controlScheme;
        this.inputDevice = inputDevice;
    }
}