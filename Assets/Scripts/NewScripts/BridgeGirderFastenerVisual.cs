using System.Collections;
using UnityEngine;

public sealed class BridgeGirderFastenerVisual : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Vector3 exposedLocalPosition = new Vector3(0f, 0.285f, 0f);
    [SerializeField] private Vector3 seatedLocalPosition = new Vector3(0f, -0.020f, 0f);
    [SerializeField, Min(0f)] private float transitionDuration = 0.12f;

    private Coroutine transition;
    private bool hasVisibleState;
    private bool hasRequestedState;
    private bool requestedVisible;
    private float requestedProgress;

    public void ApplyState(bool visible, float normalizedProgress)
    {
        SetState(visible, normalizedProgress, Application.isPlaying);
    }

    public void SetState(bool visible, float normalizedProgress, bool animate)
    {
        float clampedProgress = Mathf.Clamp01(normalizedProgress);
        if (hasRequestedState && requestedVisible == visible &&
            Mathf.Approximately(requestedProgress, clampedProgress))
        {
            return;
        }

        hasRequestedState = true;
        requestedVisible = visible;
        requestedProgress = clampedProgress;
        CancelTransition();

        if (visualRoot == null)
        {
            return;
        }

        if (!visible)
        {
            visualRoot.gameObject.SetActive(false);
            return;
        }

        visualRoot.gameObject.SetActive(true);
        Vector3 target = GetLocalPosition(clampedProgress);
        bool shouldAnimate = animate && Application.isPlaying && hasVisibleState &&
                             transitionDuration > 0f && visualRoot.gameObject.activeInHierarchy;
        hasVisibleState = true;

        if (!shouldAnimate)
        {
            visualRoot.localPosition = target;
            return;
        }

        transition = StartCoroutine(AnimateTo(target));
    }

    public Vector3 GetLocalPosition(float normalizedProgress)
    {
        return Vector3.LerpUnclamped(exposedLocalPosition, seatedLocalPosition,
            Mathf.Clamp01(normalizedProgress));
    }

    private IEnumerator AnimateTo(Vector3 target)
    {
        Vector3 start = visualRoot.localPosition;
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            visualRoot.localPosition = Vector3.LerpUnclamped(start, target, eased);
            yield return null;
        }

        visualRoot.localPosition = target;
        transition = null;
    }

    private void OnDisable()
    {
        CancelTransition();
    }

    private void CancelTransition()
    {
        if (transition == null)
        {
            return;
        }

        StopCoroutine(transition);
        transition = null;
    }
}
