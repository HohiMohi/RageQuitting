using UnityEngine;

[CreateAssetMenu(fileName = "ConcreteMixerProfile", menuName = "Scriptable Objects/Concrete Mixer Profile")]
public class ConcreteMixerProfileSO : ScriptableObject
{
    [Header("Recipe")]
    [SerializeField, Min(1)] private int drumCapacity = 15;
    [SerializeField, Min(1)] private int requiredWaterUnits = 6;
    [SerializeField, Min(1)] private int requiredGravelUnits = 6;
    [SerializeField, Min(1)] private int requiredCementBags = 1;
    [SerializeField, Min(1)] private int cementBagVolume = 3;

    [Header("Mixing")]
    [SerializeField, Min(1)] private int requiredRotations = 6;
    [SerializeField, Min(0)] private int minimumLoadedVolumeToStartMixing = 6;
    [SerializeField, Min(1f)] private float maximumCrankAngularSpeed = 240f;
    [SerializeField, Min(0.01f)] private float crankResponseTime = 0.12f;
    [SerializeField, Range(5f, 60f)] private float crankInputRate = 20f;

    [Header("Interaction")]
    [SerializeField, Min(0.5f)] private float interactionDistance = 3.5f;
    [SerializeField, Min(0f)] private float pouringDelay = 0.75f;

    [Header("Testing")]
    [SerializeField] private bool alwaysReadyConcreteForTesting;

    public int DrumCapacity => Mathf.Max(1, drumCapacity);
    public int RequiredWaterUnits => Mathf.Max(1, requiredWaterUnits);
    public int RequiredGravelUnits => Mathf.Max(1, requiredGravelUnits);
    public int RequiredCementBags => Mathf.Max(1, requiredCementBags);
    public int CementBagVolume => Mathf.Max(1, cementBagVolume);
    public int RequiredRotations => Mathf.Max(1, requiredRotations);
    public int MinimumLoadedVolumeToStartMixing => Mathf.Clamp(minimumLoadedVolumeToStartMixing, 0, DrumCapacity);
    public float MaximumCrankAngularSpeed => Mathf.Max(1f, maximumCrankAngularSpeed);
    public float CrankResponseTime => Mathf.Max(0.01f, crankResponseTime);
    public float CrankInputInterval => 1f / Mathf.Clamp(crankInputRate, 5f, 60f);
    public float InteractionDistance => Mathf.Max(0.5f, interactionDistance);
    public float PouringDelay => Mathf.Max(0f, pouringDelay);
    public bool AlwaysReadyConcreteForTesting => alwaysReadyConcreteForTesting;
}
