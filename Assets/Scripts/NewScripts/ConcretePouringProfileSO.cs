using UnityEngine;

[CreateAssetMenu(fileName = "ConcretePouringProfile", menuName = "Scriptable Objects/Concrete Pouring Profile")]
public class ConcretePouringProfileSO : ScriptableObject
{
    [SerializeField, Min(0.0001f)] private float cursorSensitivity = 0.0025f;
    [SerializeField, Min(0.05f)] private float maximumCursorSpeed = 0.85f;
    [SerializeField, Range(0f, 1f)] private float synchronizedTolerance = 0.15f;
    [SerializeField, Range(0f, 1f)] private float criticalDifference = 0.35f;
    [SerializeField, Min(0.05f)] private float criticalDifferenceDuration = 0.6f;
    [SerializeField, Range(5f, 60f)] private float inputSendRate = 20f;
    [SerializeField, Range(5f, 85f)] private float maximumPourAngle = 55f;
    [SerializeField, Min(0.05f)] private float returnDuration = 0.5f;
    [SerializeField] private bool allowSinglePlayerTesting;
    [SerializeField, Min(0f)] private float automaticPartnerDelay = 0.12f;
    [SerializeField, Min(0.05f)] private float automaticPartnerSpeed = 0.8f;

    [Header("Critical Failure Recovery")]
    [SerializeField, Min(0.05f)] private float criticalFailureSequenceDuration = 0.8f;
    [SerializeField, Min(1f)] private float failedConcreteWorkRequired = 100f;
    [SerializeField, Min(0.05f)] private float failedConcreteCollapseDuration = 0.4f;
    [SerializeField] private Vector3 failedConcreteCrackThresholds = new Vector3(1f, 34f, 67f);

    [Header("Participant Placement")]
    [SerializeField, Min(0.05f)] private float participantPlacementDuration = 0.25f;
    [SerializeField, Min(0.25f)] private float maximumJoinDistance = 3f;
    [SerializeField, Min(0.5f)] private float participantGroundProbeDistance = 3f;
    [SerializeField, Min(0f)] private float participantCapsulePadding = 0.05f;

    [Header("Participant Markers")]
    [SerializeField] private Color availableStationColor = new Color(0.1f, 0.9f, 1f, 0.85f);
    [SerializeField] private Color targetedStationColor = new Color(0.25f, 1f, 0.55f, 1f);
    [SerializeField] private Color occupiedStationColor = new Color(0.42f, 0.45f, 0.48f, 0.5f);
    [SerializeField, Min(0.005f)] private float stationMarkerLineWidth = 0.035f;
    [SerializeField, Range(0f, 0.25f)] private float stationMarkerPulseAmount = 0.08f;
    [SerializeField, Min(0f)] private float stationMarkerPulseSpeed = 2.5f;
    [SerializeField] private Vector2 stationFootprintSize = new Vector2(0.18f, 0.36f);
    [SerializeField, Min(0f)] private float stationFootprintSeparation = 0.24f;
    [SerializeField, Min(0f)] private float gripMarkerPadding = 0.05f;

    public float CursorSensitivity => Mathf.Max(0.0001f, cursorSensitivity);
    public float MaximumCursorSpeed => Mathf.Max(0.05f, maximumCursorSpeed);
    public float SynchronizedTolerance => synchronizedTolerance;
    public float CriticalDifference => Mathf.Max(synchronizedTolerance, criticalDifference);
    public float CriticalDifferenceDuration => criticalDifferenceDuration;
    public float InputSendInterval => 1f / Mathf.Max(5f, inputSendRate);
    public float MaximumPourAngle => maximumPourAngle;
    public float ReturnDuration => returnDuration;
    public bool AllowSinglePlayerTesting => allowSinglePlayerTesting;
    public float AutomaticPartnerDelay => automaticPartnerDelay;
    public float AutomaticPartnerSpeed => automaticPartnerSpeed;
    public float CriticalFailureSequenceDuration => Mathf.Max(0.05f, criticalFailureSequenceDuration);
    public float FailedConcreteWorkRequired => Mathf.Max(1f, failedConcreteWorkRequired);
    public float FailedConcreteCollapseDuration => Mathf.Max(0.05f, failedConcreteCollapseDuration);
    public Vector3 FailedConcreteCrackThresholds
    {
        get
        {
            float first = Mathf.Max(0f, failedConcreteCrackThresholds.x);
            float second = Mathf.Max(first, failedConcreteCrackThresholds.y);
            float third = Mathf.Max(second, failedConcreteCrackThresholds.z);
            return new Vector3(first, second, third);
        }
    }
    public float ParticipantPlacementDuration => Mathf.Max(0.05f, participantPlacementDuration);
    public float MaximumJoinDistance => Mathf.Max(0.25f, maximumJoinDistance);
    public float ParticipantGroundProbeDistance => Mathf.Max(0.5f, participantGroundProbeDistance);
    public float ParticipantCapsulePadding => Mathf.Max(0f, participantCapsulePadding);
    public Color AvailableStationColor => availableStationColor;
    public Color TargetedStationColor => targetedStationColor;
    public Color OccupiedStationColor => occupiedStationColor;
    public float StationMarkerLineWidth => Mathf.Max(0.005f, stationMarkerLineWidth);
    public float StationMarkerPulseAmount => Mathf.Clamp(stationMarkerPulseAmount, 0f, 0.25f);
    public float StationMarkerPulseSpeed => Mathf.Max(0f, stationMarkerPulseSpeed);
    public Vector2 StationFootprintSize => new Vector2(
        Mathf.Max(0.05f, stationFootprintSize.x),
        Mathf.Max(0.1f, stationFootprintSize.y));
    public float StationFootprintSeparation => Mathf.Max(0f, stationFootprintSeparation);
    public float GripMarkerPadding => Mathf.Max(0f, gripMarkerPadding);
}
