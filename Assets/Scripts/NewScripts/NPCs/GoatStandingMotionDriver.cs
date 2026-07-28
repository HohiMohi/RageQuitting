using UnityEngine;

public class GoatStandingMotionDriver : MonoBehaviour
{
    private Vector3 startPosition;
    private Vector3 endPosition;
    private Quaternion startRotation;
    private Quaternion endRotation;
    private float duration;
    private float arcHeight;
    private float startedAt;

    public bool IsMoving { get; private set; }
    public bool IsComplete { get; private set; }

    public void Begin(
        Vector3 start,
        Vector3 end,
        Quaternion fromRotation,
        Quaternion toRotation,
        float movementDuration,
        float movementArcHeight)
    {
        startPosition = start;
        endPosition = end;
        startRotation = fromRotation;
        endRotation = toRotation;
        duration = Mathf.Max(0.01f, movementDuration);
        arcHeight = Mathf.Max(0f, movementArcHeight);
        startedAt = Time.time;
        IsMoving = true;
        IsComplete = false;
        ApplyPose(0f);
    }

    public void Cancel()
    {
        IsMoving = false;
        IsComplete = false;
    }

    private void Update()
    {
        if (!IsMoving)
        {
            return;
        }

        float progress = Mathf.Clamp01((Time.time - startedAt) / duration);
        ApplyPose(progress);
        if (progress < 1f)
        {
            return;
        }

        IsMoving = false;
        IsComplete = true;
    }

    private void ApplyPose(float progress)
    {
        float arc = 4f * progress * (1f - progress) * arcHeight;
        transform.SetPositionAndRotation(
            Vector3.Lerp(startPosition, endPosition, progress) + Vector3.up * arc,
            Quaternion.Slerp(startRotation, endRotation, progress));
    }
}
