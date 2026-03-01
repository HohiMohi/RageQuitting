using UnityEngine;

public class GameInput : MonoBehaviour
{
    public static GameInput Instance { get; private set; }
    private PlayerGameInputActions playerGameInputActions;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        playerGameInputActions = new PlayerGameInputActions();
    }

    public Vector2 GetMovementVectorNormalized()
    {
        Vector2 inputVector = playerGameInputActions.Game.Move.ReadValue<Vector2>();

        inputVector = inputVector.normalized;
        return inputVector;
    }

    public Vector2 GetCameraRotationInput()
    {
        return Vector2.zero; //playerGameInputActions.Game.Look.ReadValue<Vector2>();
    }

    private void Update()
    {
        Debug.Log(GetCameraRotationInput());   
    }
}
