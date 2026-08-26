using System.Collections.Generic;
using UnityEngine;

public sealed class SpiritLevelMeasurementPoint : MonoBehaviour, IInteractableNew, IInteractionPromptProvider,
    ILevelingConfirmationSource
{
    [SerializeField] private BridgeConstructionSite targetSite;
    [SerializeField] private int pointId;
    [SerializeField] private SpiritLevelMeasurementAxis axis;
    [SerializeField, Range(-1f, 1f)] private float readingSign = 1f;
    [SerializeField] private Vector3 positiveTiltLocalDirection = Vector3.right;
    [SerializeField, Range(-1f, 1f)] private float fallbackViewSign = 1f;
    [SerializeField] private Transform measurementPose;
    [Header("Local marker")]
    [SerializeField] private Vector3 markerLocalCenter = new Vector3(0f, 0.015f, 0f);
    [SerializeField] private Vector2 markerSize = new Vector2(0.9f, 0.28f);
    private Collider measurementCollider;

    public BridgeConstructionSite TargetSite => targetSite;
    public int PointId => pointId;
    public SpiritLevelMeasurementAxis Axis => axis;
    public float ReadingSign => Mathf.Approximately(readingSign, 0f) ? 1f : Mathf.Sign(readingSign);
    public float FallbackViewSign => Mathf.Approximately(fallbackViewSign, 0f) ? 1f : Mathf.Sign(fallbackViewSign);
    public Vector3 PositiveTiltWorldDirection
    {
        get
        {
            Transform reference = targetSite != null ? targetSite.transform : transform;
            Vector3 localDirection = positiveTiltLocalDirection.sqrMagnitude > 0.0001f
                ? positiveTiltLocalDirection.normalized
                : Vector3.right;
            return reference.TransformDirection(localDirection).normalized;
        }
    }


#if UNITY_EDITOR
    public void ConfigureEditor(
        BridgeConstructionSite site,
        int id,
        SpiritLevelMeasurementAxis measurementAxis,
        Transform pose,
        Vector3 positiveDirection,
        float fallbackSign,
        Vector3 markerCenter,
        Vector2 configuredMarkerSize)
    {
        targetSite = site;
        pointId = id;
        axis = measurementAxis;
        measurementPose = pose;
        positiveTiltLocalDirection = positiveDirection;
        fallbackViewSign = fallbackSign < 0f ? -1f : 1f;
        markerLocalCenter = markerCenter;
        markerSize = configuredMarkerSize;
    }
#endif
    public Transform MeasurementPose => measurementPose != null ? measurementPose : transform;
    public Vector3 MarkerLocalCenter => markerLocalCenter;
    public Vector2 MarkerSize => new Vector2(Mathf.Max(0.05f, markerSize.x), Mathf.Max(0.05f, markerSize.y));
    public bool IsAvailable => targetSite is ILevelingMeasurementTarget target && target.IsLevelingActive;
    public BridgeConstructionSite ConfirmationSite => targetSite;
    public LevelingConfirmationSourceType ConfirmationSourceType => LevelingConfirmationSourceType.MeasurementPoint;
    public int ConfirmationPointId => pointId;
    public Collider ConfirmationCollider => measurementCollider != null ? measurementCollider : GetComponent<Collider>();
    public bool IsLevelingConfirmationAvailable => IsAvailable && gameObject.activeInHierarchy;

    private void Awake()
    {
        if (targetSite == null) targetSite = GetComponentInParent<BridgeConstructionSite>();
        if (GetComponent<SpiritLevelMeasurementPointVisualizer>() == null)
        {
            gameObject.AddComponent<SpiritLevelMeasurementPointVisualizer>();
        }
        measurementCollider = GetComponent<Collider>();
        if (measurementCollider != null)
        {
            measurementCollider.isTrigger = true;
            measurementCollider.enabled = IsAvailable;
        }
    }

    private void Update()
    {
        if (measurementCollider != null && measurementCollider.enabled != IsAvailable)
        {
            measurementCollider.enabled = IsAvailable;
        }
    }

    public void Interact(Transform interactor)
    {
        if (IsAvailable)
        {
            targetSite.RequestLevelingConfirmation(interactor, ConfirmationSourceType, ConfirmationPointId);
        }
    }

    public void LookedAt(Transform interactor) { }
    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        if (!IsAvailable) return;
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Action, $"Hold to measure {axis.ToString().ToLowerInvariant()}"));
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.Interact, "Confirm leveling"));
    }
}
