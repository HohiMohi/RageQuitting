using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    private Rigidbody playerRB;
    [SerializeField]
    private int playerID;
    private Vector2 movementVector;
    [SerializeField]
    private float movementSpeed = 5;


    private void Awake()
    {
        playerRB = GetComponent<Rigidbody>();
    }

    public void OnMove(InputAction.CallbackContext callbackContext)
    {
        movementVector = callbackContext.ReadValue<Vector2>();
    }
     public void Move(Vector2 movementVector)
    {
        // Temp version
        Vector2 newPosition = new Vector2(gameObject.transform.position.x + movementVector.x * Time.deltaTime * movementSpeed, gameObject.transform.position.z + movementVector.y * Time.deltaTime * movementSpeed);
        Vector3 newPosition3 = new Vector3(newPosition.x, gameObject.transform.position.y, newPosition.y);
        playerRB.MovePosition(newPosition3);
    }
    private void Update()
    {
        Move(movementVector);
    }
}
