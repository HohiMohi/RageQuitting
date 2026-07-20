using StarterAssets;
using Unity.Netcode;
using UnityEngine;

public class PlayerTurnFeedback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FirstPersonController controller;
    [SerializeField] private PlayerCameraFeedbackComposer cameraFeedbackComposer;
    [SerializeField] private PlayerInputNew playerInput;
    [SerializeField] private PlayerHealth playerHealth;

    [Header("Turn Feedback")]
    [SerializeField] private float maximumTurnRoll = 0.75f;
    [SerializeField] private float maximumLateralOffset = 0.012f;
    [SerializeField] private float responseSpring = 90f;
    [SerializeField] private float responseDamping = 12f;
    [SerializeField] private float maximumResponse = 1.15f;

    private NetworkObject networkObject;
    private float turnResponse;
    private float turnResponseVelocity;

    public float TurnAmount => turnResponse * GetMotionIntensity();

    private void Awake()
    {
        networkObject = GetComponent<NetworkObject>();
        controller ??= GetComponent<FirstPersonController>();
        cameraFeedbackComposer ??= GetComponent<PlayerCameraFeedbackComposer>();
        playerInput ??= GetComponent<PlayerInputNew>();
        playerHealth ??= GetComponent<PlayerHealth>();
    }

    private void Update()
    {
        if (!IsLocalFeedbackTarget() || controller == null || cameraFeedbackComposer == null)
        {
            return;
        }

        float intensity = GetMotionIntensity();
        bool feedbackAllowed = intensity > 0f
            && controller.enabled
            && (playerHealth == null || !playerHealth.IsDowned)
            && (playerInput == null || !playerInput.IsGameplayUiOpen);

        float targetResponse = 0f;
        if (feedbackAllowed)
        {
            targetResponse = controller.CameraBodyYawOffsetNormalized;
        }

        float deltaTime = Mathf.Min(Time.deltaTime, 1f / 30f);
        float acceleration = (targetResponse - turnResponse) * Mathf.Max(0f, responseSpring)
            - turnResponseVelocity * Mathf.Max(0f, responseDamping);
        turnResponseVelocity += acceleration * deltaTime;
        turnResponse += turnResponseVelocity * deltaTime;
        turnResponse = Mathf.Clamp(turnResponse, -Mathf.Max(1f, maximumResponse), Mathf.Max(1f, maximumResponse));

        if (!feedbackAllowed && Mathf.Abs(turnResponse) < 0.0001f && Mathf.Abs(turnResponseVelocity) < 0.0001f)
        {
            turnResponse = 0f;
            turnResponseVelocity = 0f;
        }

        float scaledResponse = TurnAmount;
        cameraFeedbackComposer.SetTurnFeedback(
            new Vector3(-scaledResponse * maximumLateralOffset, 0f, 0f),
            new Vector3(0f, 0f, -scaledResponse * maximumTurnRoll));
    }

    private void OnDisable()
    {
        turnResponse = 0f;
        turnResponseVelocity = 0f;
        cameraFeedbackComposer?.ClearTurnFeedback();
    }

    private float GetMotionIntensity()
    {
        return CameraMotionSettings.Instance != null
            ? CameraMotionSettings.Instance.RotationMotionIntensity
            : 1f;
    }

    private bool IsLocalFeedbackTarget()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return networkObject != null && networkObject.IsSpawned && networkObject.IsOwner;
        }

        return true;
    }
}
