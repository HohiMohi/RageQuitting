using Cinemachine;
using StarterAssets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerMovementFeedback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FirstPersonController controller;
    [SerializeField] private PlayerCameraFeedbackComposer cameraFeedbackComposer;
    [SerializeField] private PlayerInputNew playerInput;
    [SerializeField] private PlayerHealth playerHealth;

    [Header("Head Bob")]
    [SerializeField] private float walkBobAmplitude = 0.008f;
    [SerializeField] private float sprintBobAmplitude = 0.014f;
    [SerializeField] private float bobCyclesPerMeter = 1.25f;
    [SerializeField] private float horizontalBobRatio = 0.55f;
    [SerializeField] private float positionSmoothing = 14f;

    [Header("Sway")]
    [SerializeField] private float maximumStrafeRoll = 1f;
    [SerializeField] private float accelerationSway = 0.006f;
    [SerializeField] private float maximumAccelerationOffset = 0.025f;
    [SerializeField] private float rotationSmoothing = 12f;

    [Header("Jump and Landing")]
    [SerializeField] private float jumpPositionImpulse = 0.025f;
    [SerializeField] private float landingPositionImpulse = 0.055f;
    [SerializeField] private float landingReferenceSpeed = 10f;
    [SerializeField] private float impulseRecoverySpeed = 7f;

    [Header("Sprint FOV")]
    [SerializeField] private float sprintFovBonus = 4f;
    [SerializeField] private float fovChangeSpeed = 8f;

    private NetworkObject networkObject;
    private CinemachineVirtualCamera virtualCamera;
    private float baseFieldOfView;
    private float bobPhase;
    private float verticalImpulse;
    private Vector3 currentPositionOffset;
    private Vector3 currentEulerOffset;
    private Vector3 previousPosition;
    private Vector3 previousHorizontalVelocity;
    private bool subscribed;
    private bool hasPreviousPosition;

    private void Awake()
    {
        networkObject = GetComponent<NetworkObject>();
        controller ??= GetComponent<FirstPersonController>();
        cameraFeedbackComposer ??= GetComponent<PlayerCameraFeedbackComposer>();
        playerInput ??= GetComponent<PlayerInputNew>();
        playerHealth ??= GetComponent<PlayerHealth>();
    }

    private void OnEnable()
    {
        Subscribe();
        previousPosition = transform.position;
        previousHorizontalVelocity = Vector3.zero;
        hasPreviousPosition = true;
        ResolveVirtualCamera();
    }

    private void OnDisable()
    {
        Unsubscribe();
        ResetFeedback(true);
    }

    private void OnDestroy()
    {
        Unsubscribe();
        RestoreFieldOfView();
    }

    private void Update()
    {
        if (!IsLocalFeedbackTarget() || controller == null || cameraFeedbackComposer == null)
        {
            return;
        }

        ResolveVirtualCamera();
        bool feedbackAllowed = controller.enabled
            && (playerHealth == null || !playerHealth.IsDowned)
            && (playerInput == null || !playerInput.IsGameplayUiOpen);

        Vector3 currentPosition = transform.position;
        Vector3 horizontalDelta = hasPreviousPosition ? currentPosition - previousPosition : Vector3.zero;
        horizontalDelta.y = 0f;
        previousPosition = currentPosition;
        hasPreviousPosition = true;

        float speed = controller.HorizontalSpeed;
        float speedReference = Mathf.Max(0.01f, controller.IsSprinting ? controller.SprintSpeed : controller.MoveSpeed);
        float moveAmount = feedbackAllowed && controller.Grounded ? Mathf.Clamp01(speed / speedReference) : 0f;
        if (moveAmount > 0.02f && horizontalDelta.sqrMagnitude > 0f)
        {
            bobPhase += horizontalDelta.magnitude * bobCyclesPerMeter * Mathf.PI * 2f;
        }

        float bobAmplitude = controller.IsSprinting ? sprintBobAmplitude : walkBobAmplitude;
        float verticalBob = Mathf.Sin(bobPhase * 2f) * bobAmplitude * moveAmount;
        float horizontalBob = Mathf.Cos(bobPhase) * bobAmplitude * horizontalBobRatio * moveAmount;

        Vector3 localVelocity = transform.InverseTransformDirection(controller.HorizontalVelocity);
        float strafeAmount = speedReference > 0f ? Mathf.Clamp(localVelocity.x / speedReference, -1f, 1f) : 0f;
        Vector3 acceleration = (controller.HorizontalVelocity - previousHorizontalVelocity) / Mathf.Max(Time.deltaTime, 0.0001f);
        previousHorizontalVelocity = controller.HorizontalVelocity;
        Vector3 localAcceleration = transform.InverseTransformDirection(acceleration);
        float accelerationOffsetX = Mathf.Clamp(
            -localAcceleration.x * accelerationSway,
            -maximumAccelerationOffset,
            maximumAccelerationOffset);
        float accelerationOffsetZ = Mathf.Clamp(
            -localAcceleration.z * accelerationSway * 0.5f,
            -maximumAccelerationOffset * 0.5f,
            maximumAccelerationOffset * 0.5f);

        verticalImpulse = Mathf.Lerp(
            verticalImpulse,
            0f,
            1f - Mathf.Exp(-impulseRecoverySpeed * Time.deltaTime));
        Vector3 targetPosition = feedbackAllowed
            ? new Vector3(
                horizontalBob + accelerationOffsetX,
                verticalBob + verticalImpulse,
                accelerationOffsetZ)
            : Vector3.zero;
        Vector3 targetEuler = feedbackAllowed
            ? new Vector3(0f, 0f, -strafeAmount * maximumStrafeRoll)
            : Vector3.zero;

        currentPositionOffset = Vector3.Lerp(currentPositionOffset, targetPosition, 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime));
        currentEulerOffset = Vector3.Lerp(currentEulerOffset, targetEuler, 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime));
        cameraFeedbackComposer.SetMovementFeedback(currentPositionOffset, currentEulerOffset);
        UpdateFieldOfView(feedbackAllowed && controller.IsSprinting && speed > controller.MoveSpeed);
    }

    private void Subscribe()
    {
        if (subscribed || controller == null)
        {
            return;
        }

        controller.OnJumpStarted += Controller_OnJumpStarted;
        controller.OnLanded += Controller_OnLanded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || controller == null)
        {
            return;
        }

        controller.OnJumpStarted -= Controller_OnJumpStarted;
        controller.OnLanded -= Controller_OnLanded;
        subscribed = false;
    }

    private void Controller_OnJumpStarted(object sender, System.EventArgs e)
    {
        if (IsLocalFeedbackTarget())
        {
            verticalImpulse = Mathf.Max(verticalImpulse, jumpPositionImpulse);
        }
    }

    private void Controller_OnLanded(object sender, FirstPersonController.LandedEventArgs e)
    {
        if (!IsLocalFeedbackTarget())
        {
            return;
        }

        float strength = Mathf.Clamp01(e.ImpactSpeed / Mathf.Max(0.01f, landingReferenceSpeed));
        verticalImpulse = Mathf.Min(verticalImpulse, -landingPositionImpulse * strength);
    }

    private void ResolveVirtualCamera()
    {
        if (virtualCamera != null && virtualCamera.gameObject.scene == SceneManager.GetActiveScene())
        {
            return;
        }

        virtualCamera = null;
        foreach (CinemachineVirtualCamera candidate in FindObjectsByType<CinemachineVirtualCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (candidate.gameObject.scene != SceneManager.GetActiveScene())
            {
                continue;
            }

            virtualCamera = candidate;
            baseFieldOfView = virtualCamera.m_Lens.FieldOfView;
            break;
        }
    }

    private void UpdateFieldOfView(bool sprinting)
    {
        if (virtualCamera == null)
        {
            return;
        }

        LensSettings lens = virtualCamera.m_Lens;
        float targetFov = baseFieldOfView + (sprinting ? sprintFovBonus : 0f);
        lens.FieldOfView = Mathf.MoveTowards(lens.FieldOfView, targetFov, fovChangeSpeed * Time.deltaTime);
        virtualCamera.m_Lens = lens;
    }

    private void RestoreFieldOfView()
    {
        if (virtualCamera == null)
        {
            return;
        }

        LensSettings lens = virtualCamera.m_Lens;
        lens.FieldOfView = baseFieldOfView;
        virtualCamera.m_Lens = lens;
    }

    private void ResetFeedback(bool restoreFov)
    {
        bobPhase = 0f;
        verticalImpulse = 0f;
        currentPositionOffset = Vector3.zero;
        currentEulerOffset = Vector3.zero;
        previousHorizontalVelocity = Vector3.zero;
        cameraFeedbackComposer?.ClearMovementFeedback();
        if (restoreFov)
        {
            RestoreFieldOfView();
        }
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
