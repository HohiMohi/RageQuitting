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
}
