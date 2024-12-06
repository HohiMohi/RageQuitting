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
    #region Tooltip
    [Tooltip("Movement speed in unit per second.")]
    #endregion 
    [SerializeField]
    private float movementSpeed;

    #region Tooltip
    [Tooltip("Rotation speed in degrees per second.")]
    #endregion
    [SerializeField]
    private float rotationSpeed = 360.0f;


    private void Awake()
    {
        playerRB = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        Move(movementVector);
        RotateTowards(movementVector);
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

    /// <summary>
    /// Rotate gameObject towards movement direction.
    /// </summary>
    public void RotateTowards(Vector2 movementVector)
    {
        if (movementVector != Vector2.zero)
        {
            // Calculate max rotation step during frame
            float step = rotationSpeed * Time.deltaTime;

            // Calculating target angle from movement vector
            float radians = Mathf.Atan2(-movementVector.y, movementVector.x);
            var deg = radians * (180 / Mathf.PI);

            // Transforming angle to Quaternion
            Quaternion targetQuaternion = new Quaternion();
            targetQuaternion = Quaternion.Euler(0, deg, 0);

            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetQuaternion, step);
        }
    }

    public void Jump()
    {
        playerRB.AddForce(0, 100, 0);
    }
}
