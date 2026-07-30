using UnityEngine;

public enum EquippableActionPhase
{
    None,
    WindUp,
    Strike,
    ImpactFreeze,
    Recovery
}

[CreateAssetMenu(fileName = "EquippableActionProfile", menuName = "Scriptable Objects/Equippable Action Profile")]
public class EquippableActionProfileSO : ScriptableObject
{
    [Header("Timing")]
    [Min(0f)] public float windUpDuration = 0.15f;
    [Min(0f)] public float strikeDuration = 0.12f;
    [Min(0f)] public float impactFreezeDuration = 0.03f;
    [Min(0f)] public float recoveryDuration = 0.25f;

    [Header("Tool pose")]
    public Vector3 toolWindUpPositionOffset;
    public Vector3 toolWindUpEulerOffset;
    public Vector3 toolImpactPositionOffset;
    public Vector3 toolImpactEulerOffset;
    public Vector3 rightArmWindUpEulerOffset;
    public Vector3 rightArmImpactEulerOffset;
    [Range(0f, 1f)] public float leftArmActionWeight = 0.35f;

    [Header("Movement and camera")]
    [Range(0f, 1f)] public float movementMultiplierDuringAction = 0.9f;
    public Vector3 cameraKickPosition;
    public Vector3 cameraKickEuler;
    [Min(0.01f)] public float cameraKickRecoveryDuration = 0.12f;
    [Min(0f)] public float impactFeedbackStrength = 1f;

    [Header("Audio")]
    public AudioClip swingClip;
    [Range(0f, 1f)] public float swingVolume = 0.6f;
    [Range(0.25f, 2f)] public float swingPitch = 1f;

    public float GetPhaseDuration(EquippableActionPhase phase)
    {
        return phase switch
        {
            EquippableActionPhase.WindUp => Mathf.Max(0f, windUpDuration),
            EquippableActionPhase.Strike => Mathf.Max(0f, strikeDuration),
            EquippableActionPhase.ImpactFreeze => Mathf.Max(0f, impactFreezeDuration),
            EquippableActionPhase.Recovery => Mathf.Max(0f, recoveryDuration),
            _ => 0f
        };
    }
}
