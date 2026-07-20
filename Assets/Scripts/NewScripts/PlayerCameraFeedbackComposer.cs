using UnityEngine;

[DefaultExecutionOrder(1000)]
public class PlayerCameraFeedbackComposer : MonoBehaviour
{
    [SerializeField] private Transform baseCameraTarget;
    [SerializeField] private Transform outputTarget;

    private Vector3 movementPositionOffset;
    private Vector3 movementEulerOffset;
    private Vector3 damagePositionOffset;
    private Vector3 damageEulerOffset;
    private Vector3 turnPositionOffset;
    private Vector3 turnEulerOffset;

    public Transform OutputTarget
    {
        get
        {
            EnsureOutputTarget();
            return outputTarget != null ? outputTarget : baseCameraTarget;
        }
    }

    private void Awake()
    {
        EnsureOutputTarget();
        ApplyOffsets();
    }

    private void LateUpdate()
    {
        ApplyOffsets();
    }

    private void OnDisable()
    {
        ClearAllFeedback();
        ApplyOffsets();
    }

    public void SetMovementFeedback(Vector3 positionOffset, Vector3 eulerOffset)
    {
        movementPositionOffset = positionOffset;
        movementEulerOffset = eulerOffset;
    }

    public void SetDamageFeedback(Vector3 positionOffset, Vector3 eulerOffset = default)
    {
        damagePositionOffset = positionOffset;
        damageEulerOffset = eulerOffset;
    }

    public void SetTurnFeedback(Vector3 positionOffset, Vector3 eulerOffset)
    {
        turnPositionOffset = positionOffset;
        turnEulerOffset = eulerOffset;
    }

    public void ClearMovementFeedback()
    {
        movementPositionOffset = Vector3.zero;
        movementEulerOffset = Vector3.zero;
    }

    public void ClearDamageFeedback()
    {
        damagePositionOffset = Vector3.zero;
        damageEulerOffset = Vector3.zero;
    }

    public void ClearTurnFeedback()
    {
        turnPositionOffset = Vector3.zero;
        turnEulerOffset = Vector3.zero;
    }

    public void ClearAllFeedback()
    {
        ClearMovementFeedback();
        ClearDamageFeedback();
        ClearTurnFeedback();
    }

    private void EnsureOutputTarget()
    {
        if (baseCameraTarget == null)
        {
            StarterAssets.FirstPersonController controller = GetComponent<StarterAssets.FirstPersonController>();
            if (controller != null && controller.CinemachineCameraTarget != null)
            {
                baseCameraTarget = controller.CinemachineCameraTarget.transform;
            }
        }

        if (outputTarget != null || baseCameraTarget == null)
        {
            return;
        }

        Transform existingTarget = baseCameraTarget.Find("CameraFeedbackOffset");
        if (existingTarget != null)
        {
            outputTarget = existingTarget;
            return;
        }

        GameObject targetObject = new GameObject("CameraFeedbackOffset");
        outputTarget = targetObject.transform;
        outputTarget.SetParent(baseCameraTarget, false);
    }

    private void ApplyOffsets()
    {
        EnsureOutputTarget();
        if (outputTarget == null || outputTarget == baseCameraTarget)
        {
            return;
        }

        outputTarget.localPosition = movementPositionOffset + damagePositionOffset + turnPositionOffset;
        outputTarget.localRotation = Quaternion.Euler(movementEulerOffset + damageEulerOffset + turnEulerOffset);
    }
}
