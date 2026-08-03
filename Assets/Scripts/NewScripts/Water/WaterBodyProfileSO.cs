using UnityEngine;

[CreateAssetMenu(fileName = "WaterBodyProfile", menuName = "Scriptable Objects/Water/Water Body Profile")]
public class WaterBodyProfileSO : ScriptableObject
{
    [Header("Player Hazard")]
    [SerializeField, Min(0f)] private float maximumSafeWadingDepth = 1.2f;
    [SerializeField, Min(0f)] private float staminaDrainPerSecond = 1f;
    [SerializeField, Min(0.1f)] private float exhaustionWarningDuration = 3f;
    [SerializeField, Min(0.1f)] private float unsupportedGraceDuration = 2f;
    [SerializeField, Min(0f)] private float downedFloatDepth = 0.35f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Surface Navigation")]
    [SerializeField] private string waterNavMeshAreaName = "WaterSurface";
    [SerializeField, Min(1f)] private float waterNavMeshAreaCost = 4f;
    [SerializeField, Range(0.1f, 1f)] private float surfaceSwimSpeedMultiplier = 0.75f;

    public float MaximumSafeWadingDepth => Mathf.Max(0f, maximumSafeWadingDepth);
    public float StaminaDrainPerSecond => Mathf.Max(0f, staminaDrainPerSecond);
    public float ExhaustionWarningDuration => Mathf.Max(0.1f, exhaustionWarningDuration);
    public float UnsupportedGraceDuration => Mathf.Max(0.1f, unsupportedGraceDuration);
    public float DownedFloatDepth => Mathf.Max(0f, downedFloatDepth);
    public LayerMask GroundMask => groundMask;
    public string WaterNavMeshAreaName => waterNavMeshAreaName;
    public float WaterNavMeshAreaCost => Mathf.Max(1f, waterNavMeshAreaCost);
    public float SurfaceSwimSpeedMultiplier => Mathf.Clamp(surfaceSwimSpeedMultiplier, 0.1f, 1f);
}
