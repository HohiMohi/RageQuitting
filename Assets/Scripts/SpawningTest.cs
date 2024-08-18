using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SpawningTest : MonoBehaviour
{
    public Transform spawningPoint;

    public void OnPlayerJoined(PlayerInput input)
    {
        Debug.Log(input.playerIndex);
    }
}
